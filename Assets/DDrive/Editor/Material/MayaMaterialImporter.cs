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
            BeginBatch(gameDataRoot);
            try
            {
                foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
                {
                    if (sub is UnityEngine.Material material)
                    {
                        ImportMaterial(material, category, sourceKey, profile, report, gameDataRoot);
                    }
                }
            }
            finally
            {
                EndBatch();
            }

            return report;
        }

        // ── 一括処理の同定インデックス(2026-09-11 レビュー対応) ──
        // FindExisting / EnsureTextureData は 1 件ごとに全 MaterialData / TextureData を検索していた。
        // 一括の開始時に SourceMaterial → MaterialData / Texture → TextureData の辞書を 1 度だけ作って使い回す。
        // (検索自体も AssetSearch 経由にしてある。[12_review.md] §3 / [09] §9)
        private static int _batchDepth;
        private static string _batchRoot;
        private static Dictionary<string, MaterialData> _materialIndex;
        private static Dictionary<Texture, TextureData> _textureIndex;

        public static void BeginBatch(string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            if (_batchDepth++ == 0 || _batchRoot != gameDataRoot)
            {
                _batchRoot = gameDataRoot;
                _materialIndex = null;
                _textureIndex = null;
            }
        }

        public static void EndBatch()
        {
            _batchDepth = Mathf.Max(0, _batchDepth - 1);
            if (_batchDepth == 0)
            {
                _batchRoot = null;
                _materialIndex = null;
                _textureIndex = null;
            }
        }

        private static Dictionary<string, MaterialData> GetMaterialIndex(string gameDataRoot)
        {
            if (_batchDepth > 0 && _batchRoot == gameDataRoot && _materialIndex != null)
            {
                return _materialIndex;
            }

            var index = new Dictionary<string, MaterialData>();
            if (AssetDatabase.IsValidFolder(gameDataRoot))
            {
                foreach (var guid in AssetSearch.FindAssets("t:" + nameof(MaterialData), new[] { gameDataRoot }))
                {
                    var data = AssetDatabase.LoadAssetAtPath<MaterialData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (data != null && !string.IsNullOrEmpty(data.SourceMaterial))
                    {
                        index.TryAdd(data.SourceMaterial, data);
                    }
                }
            }

            if (_batchDepth > 0 && _batchRoot == gameDataRoot)
            {
                _materialIndex = index;
            }

            return index;
        }

        private static Dictionary<Texture, TextureData> GetTextureIndex(string gameDataRoot)
        {
            if (_batchDepth > 0 && _batchRoot == gameDataRoot && _textureIndex != null)
            {
                return _textureIndex;
            }

            var index = new Dictionary<Texture, TextureData>();
            if (AssetDatabase.IsValidFolder(gameDataRoot))
            {
                foreach (var guid in AssetSearch.FindAssets("t:" + nameof(TextureData), new[] { gameDataRoot }))
                {
                    var data = AssetDatabase.LoadAssetAtPath<TextureData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (data != null && data.Texture != null)
                    {
                        index.TryAdd(data.Texture, data);
                    }
                }
            }

            if (_batchDepth > 0 && _batchRoot == gameDataRoot)
            {
                _textureIndex = index;
            }

            return index;
        }

        // ── SourceMaterial(再インポート時の同定キー) ──
        // "<sourceKey>/<元アセットの GUID>/<マテリアル名>"。GUID を挟むのは、同名の Material が別フォルダにある場合や
        // 同名の FBX が複数ある場合に 1 つの MaterialData を奪い合っていたため(2026-09-11 レビュー対応)。
        // FBX のサブアセットは GetAssetPath が FBX 自身を返すので、Material アセットでも FBX でも同じ経路で求まる。
        public static string BuildSourceMaterial(UnityEngine.Material source, string sourceKey)
        {
            if (source == null)
            {
                return null;
            }

            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            var head = string.IsNullOrEmpty(sourceKey) ? string.Empty : sourceKey + "/";
            return string.IsNullOrEmpty(guid) ? head + source.name : head + guid + "/" + source.name;
        }

        // 旧形式("<sourceKey>/<マテリアル名>")。既存アセットとの互換のために探し、見つかったら新形式へ移行する。
        public static string BuildLegacySourceMaterial(UnityEngine.Material source, string sourceKey)
            => source == null ? null : string.IsNullOrEmpty(sourceKey) ? source.name : sourceKey + "/" + source.name;

        // 1 マテリアル分。sourceKey は FBX 名(再インポート時の同定に使う)。
        public static MaterialData ImportMaterial(UnityEngine.Material source, string category, string sourceKey, MayaImportProfile profile, Report report,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            report ??= new Report();
            if (source == null || profile == null)
            {
                return null;
            }

            var sourceMaterial = BuildSourceMaterial(source, sourceKey);
            var common = BuildCommon(source, category, profile, report, gameDataRoot);

            var existing = FindExisting(sourceMaterial, BuildLegacySourceMaterial(source, sourceKey), gameDataRoot);
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
                    existing.Shader = ResolveTargetShader(profile, source);
                    existing.Specific = MaterialSpecificResolver.Merge(existing.Specific, existing.Shader);
                    UnityMaterialMigrator.CopySpecificValues(source, existing); // 既存アセットなので Undo に積む
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
                mat.Shader = ResolveTargetShader(profile, source);
                mat.Common = capturedCommon;
                mat.SourceMaterial = sourceMaterial;
                // 新規作成時はシェーダーの固有を既定値で登録し、元 Material に同名があれば値を引き継ぐ
                // (aiStandardSurface の Coat / Sheen / IOR 等。再インポート時は Specific を保持するので触らない。2026-09-11)。
                mat.Specific = MaterialSpecificResolver.Merge(null, mat.Shader);
                // configure は AssetDatabase.CreateAsset の前に呼ばれる(まだアセットではない)ので Undo に積まない。
                UnityMaterialMigrator.CopySpecificValues(source, mat, recordUndo: false);
            }, gameDataRoot) as MaterialData;

            if (created != null)
            {
                GetMaterialIndex(gameDataRoot)[sourceMaterial] = created; // 同じ一括処理の後続が見つけられるように
                report.Created++;
                report.Log($"新規: {created.name}(カテゴリ {category})");
            }

            return created;
        }

        // 生成する MaterialData のシェーダー(2026-09-11)。
        //   1. Profile の TargetShader が設定されていればそれ
        //   2. 元 Material が D-Drive のシェーダー(AiStandardSurfacePreprocessor が割り当てた DDrive/AiStandardSurface 等)ならそのまま
        //   3. それ以外は DDrive/Lit(無ければ null = Manager の既定 Lit)
        public static Shader ResolveTargetShader(MayaImportProfile profile, UnityEngine.Material source)
        {
            if (profile != null && profile.TargetShader != null)
            {
                return profile.TargetShader;
            }

            if (source != null && source.shader != null && source.shader.name.StartsWith("DDrive/", System.StringComparison.Ordinal))
            {
                return source.shader;
            }

            return Shader.Find(UnityMaterialMigrator.LitShaderName);
        }

        // 同じ元アセット / マテリアル名から作られた MaterialData を探す(SourceMaterial で同定)。
        public static MaterialData FindExisting(string sourceMaterial, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
            => FindExisting(sourceMaterial, null, gameDataRoot);

        // legacySourceMaterial は旧形式(GUID 無し)のキー。新形式で見つからず旧形式で見つかったら、新形式へ移行する。
        public static MaterialData FindExisting(string sourceMaterial, string legacySourceMaterial, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            if (string.IsNullOrEmpty(sourceMaterial))
            {
                return null;
            }

            var index = GetMaterialIndex(gameDataRoot);
            if (index.TryGetValue(sourceMaterial, out var hit) && hit != null)
            {
                return hit;
            }

            if (!string.IsNullOrEmpty(legacySourceMaterial) && legacySourceMaterial != sourceMaterial &&
                index.TryGetValue(legacySourceMaterial, out var legacy) && legacy != null)
            {
                Undo.RecordObject(legacy, "Migrate SourceMaterial Key");
                legacy.SourceMaterial = sourceMaterial;
                EditorUtility.SetDirty(legacy);
                index.Remove(legacySourceMaterial);
                index[sourceMaterial] = legacy;
                return legacy;
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
            if (texture == null)
            {
                return TextureId.Invalid;
            }

            var index = GetTextureIndex(gameDataRoot);
            if (index.TryGetValue(texture, out var existing) && existing != null)
            {
                return new TextureId(existing.Id, AssetType.Texture);
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

            index[texture] = created; // 同じ一括処理の後続が見つけられるように
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
