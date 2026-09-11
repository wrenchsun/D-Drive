using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;
using TextureId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Material.TextureMarker>;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2「Maya FBX 自動生成」— FBX から Unity が生成した Material を MaterialCommon にマッピングし、
    // MaterialData(+ 参照する TextureData)を ID 発行・カタログ登録・規約名で自動生成する(チケット 3-7、2026-09-10)。
    // 再インポート時は同じ FBX / 同じマテリアル名の MaterialData を探して Common だけ更新し、固有調整(Specific / Anims / Render)は
    // 上書きしない(差分はログに出す)。MayaModelPostprocessor(自動)とメニュー(手動)の両方から呼ばれる。
    public static class MayaMaterialImporter
    {
        public sealed class Report
        {
            public readonly List<string> Lines = new();
            public int Created;
            public int Updated;
            public int Unchanged;
            public int TexturesCreated;

            public void Log(string line) => Lines.Add(line);

            public override string ToString()
                => $"MaterialData 新規 {Created} / 更新 {Updated} / 変更なし {Unchanged}、TextureData 新規 {TexturesCreated}\n" + string.Join("\n", Lines);
        }

        // モデル(FBX)に含まれる全 Material を処理する。
        public static Report ImportModel(string modelPath, MayaImportProfile profile, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            var report = new Report();
            if (string.IsNullOrEmpty(modelPath) || profile == null)
            {
                return report;
            }

            var category = profile.ResolveCategory(modelPath);
            var sourceKey = System.IO.Path.GetFileNameWithoutExtension(modelPath);
            foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
            {
                if (sub is UnityEngine.Material material)
                {
                    ImportMaterial(material, category, sourceKey, profile, report, gameDataRoot);
                }
            }

            return report;
        }

        // 1 マテリアル分。sourceKey は FBX 名(再インポート時の同定に使う)。
        public static MaterialData ImportMaterial(UnityEngine.Material source, string category, string sourceKey, MayaImportProfile profile, Report report,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            report ??= new Report();
            if (source == null || profile == null)
            {
                return null;
            }

            var sourceMaterial = string.IsNullOrEmpty(sourceKey) ? source.name : sourceKey + "/" + source.name;
            var common = BuildCommon(source, category, profile, report, gameDataRoot);

            var existing = FindExisting(sourceMaterial, gameDataRoot);
            if (existing != null)
            {
                var diff = DescribeDiff(existing.Common, common);
                if (diff.Count == 0)
                {
                    report.Unchanged++;
                    report.Log($"変更なし: {existing.name}");
                    return existing;
                }

                Undo.RecordObject(existing, "Reimport Maya Material");
                existing.Common = common;
                if (existing.Shader == null)
                {
                    existing.Shader = profile.TargetShader;
                }

                if (!profile.PreserveSpecificOnReimport)
                {
                    existing.Specific = null;
                }

                EditorUtility.SetDirty(existing);
                report.Updated++;
                report.Log($"更新: {existing.name}(Common: {string.Join(", ", diff)}。Specific / Anims / Render は保持)");
                return existing;
            }

            var identifier = AssetNamingService.ToIdentifier(source.name, "Mat");
            var capturedCommon = common;
            var created = AssetCreationService.Create(typeof(MaterialData), AssetType.Material, source.name, category, identifier, data =>
            {
                var mat = (MaterialData)data;
                mat.Shader = profile.TargetShader;
                mat.Common = capturedCommon;
                mat.SourceMaterial = sourceMaterial;
                // 新規作成時はシェーダーの固有を既定値で登録しておく(再インポート時は Specific を保持するので触らない。2026-09-11)。
                mat.Specific = MaterialSpecificResolver.Merge(null, mat.Shader);
            }, gameDataRoot) as MaterialData;

            if (created != null)
            {
                report.Created++;
                report.Log($"新規: {created.name}(カテゴリ {category})");
            }

            return created;
        }

        // 同じ FBX / マテリアル名から作られた MaterialData を探す(SourceMaterial で同定)。
        public static MaterialData FindExisting(string sourceMaterial, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            if (string.IsNullOrEmpty(sourceMaterial))
            {
                return null;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(MaterialData), new[] { gameDataRoot }))
            {
                var data = AssetDatabase.LoadAssetAtPath<MaterialData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && data.SourceMaterial == sourceMaterial)
                {
                    return data;
                }
            }

            return null;
        }

        private static MaterialCommon BuildCommon(UnityEngine.Material source, string category, MayaImportProfile profile, Report report, string gameDataRoot)
        {
            var common = MaterialCommon.Default;
            var textureProfile = TextureImportProfile.FindOrDefault();

            var props = source.GetTexturePropertyNames();
            for (var i = 0; i < props.Length; i++)
            {
                var tex = source.GetTexture(props[i]) as Texture2D;
                if (tex == null)
                {
                    continue;
                }

                if (!profile.TryGetChannel(props[i], out var channel))
                {
                    if (!profile.ClassifyByTextureName || !textureProfile.TryMatch(AssetDatabase.GetAssetPath(tex), out var rule) || rule.Channel == TextureChannel.Other)
                    {
                        report.Log($"  スキップ: {source.name}.{props[i]} = {tex.name}(チャンネル未対応)");
                        continue;
                    }

                    channel = rule.Channel;
                }

                var id = EnsureTextureData(tex, channel, category, report, gameDataRoot);
                switch (channel)
                {
                    case TextureChannel.Albedo:
                        if (!common.Albedo.IsValid) common.Albedo = id;
                        break;
                    case TextureChannel.Normal:
                        if (!common.Normal.IsValid) common.Normal = id;
                        break;
                    case TextureChannel.Mask:
                        if (!common.Mask.IsValid) common.Mask = id;
                        break;
                    case TextureChannel.Emission:
                        if (!common.Emission.IsValid) common.Emission = id;
                        break;
                }
            }

            common.AlbedoTint = GetColor(source, "_BaseColor", GetColor(source, "_Color", Color.white));
            common.NormalScale = GetFloat(source, "_BumpScale", 1f);
            common.Metallic = GetFloat(source, "_Metallic", 0f);
            common.Smoothness = GetFloat(source, "_Smoothness", GetFloat(source, "_Glossiness", 0.5f));
            var emission = GetColor(source, "_EmissionColor", Color.black);
            common.EmissionColor = emission;
            common.EmissionIntensity = emission.maxColorComponent > 0f || common.Emission.IsValid ? 1f : 0f;
            common.Cutoff = GetFloat(source, "_Cutoff", 0.5f);
            common.DoubleSided = source.HasProperty("_Cull") && Mathf.Approximately(source.GetFloat("_Cull"), 0f);
            common.Blend = source.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent ? BlendType.Transparent
                : source.IsKeywordEnabled("_ALPHATEST_ON") || (source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f) ? BlendType.Cutout
                : BlendType.Opaque;
            return common;
        }

        // 同じ Texture2D を指す TextureData があればその ID、無ければ作る。
        public static TextureId EnsureTextureData(Texture2D texture, TextureChannel channel, string category, Report report, string gameDataRoot)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(TextureData), new[] { gameDataRoot }))
            {
                var data = AssetDatabase.LoadAssetAtPath<TextureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && data.Texture == texture)
                {
                    return new TextureId(data.Id, AssetType.Texture);
                }
            }

            var identifier = AssetNamingService.ToIdentifier(texture.name, "Tex");
            var created = AssetCreationService.Create(typeof(TextureData), AssetType.Texture, texture.name, category, identifier, data =>
            {
                var tex = (TextureData)data;
                tex.Texture = texture;
                tex.Channel = channel;
                tex.Usage = TextureUsage.Model;
            }, gameDataRoot) as TextureData;

            if (created == null)
            {
                return TextureId.Invalid;
            }

            report.TexturesCreated++;
            report.Log($"  TextureData 新規: {created.name}({channel})");
            return new TextureId(created.Id, AssetType.Texture);
        }

        private static List<string> DescribeDiff(in MaterialCommon a, in MaterialCommon b)
        {
            var diff = new List<string>();
            if (a.Albedo != b.Albedo) diff.Add("Albedo");
            if (a.AlbedoTint != b.AlbedoTint) diff.Add("AlbedoTint");
            if (a.Normal != b.Normal) diff.Add("Normal");
            if (!Mathf.Approximately(a.NormalScale, b.NormalScale)) diff.Add("NormalScale");
            if (a.Mask != b.Mask) diff.Add("Mask");
            if (!Mathf.Approximately(a.Metallic, b.Metallic)) diff.Add("Metallic");
            if (!Mathf.Approximately(a.Smoothness, b.Smoothness)) diff.Add("Smoothness");
            if (a.Emission != b.Emission) diff.Add("Emission");
            if (a.EmissionColor != b.EmissionColor) diff.Add("EmissionColor");
            if (!Mathf.Approximately(a.EmissionIntensity, b.EmissionIntensity)) diff.Add("EmissionIntensity");
            if (a.Blend != b.Blend) diff.Add("Blend");
            if (!Mathf.Approximately(a.Cutoff, b.Cutoff)) diff.Add("Cutoff");
            if (a.DoubleSided != b.DoubleSided) diff.Add("DoubleSided");
            return diff;
        }

        private static Color GetColor(UnityEngine.Material m, string property, Color fallback)
            => m.HasProperty(property) ? m.GetColor(property) : fallback;

        private static float GetFloat(UnityEngine.Material m, string property, float fallback)
            => m.HasProperty(property) ? m.GetFloat(property) : fallback;
    }
}
