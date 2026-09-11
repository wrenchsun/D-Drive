using System;
using System.Collections.Generic;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] B-3 — テクスチャのインポート規約(チケット 3-8、2026-09-10)。
    // 「命名 → 自動設定」のルール表。デザイナーが編集できるアセット(Create > D-Drive > Material > Texture Import Profile)。
    // プロジェクトに無ければ組み込みの既定ルール(`_N` → NormalMap / `_M` → Mask / `_UI` → Sprite / `T_` → Model 既定)を使う。
    // TexturePostprocessor がインポート時に適用し、TextureDataValidator が違反を検出して FixAction で再インポートする。
    [CreateAssetMenu(menuName = "D-Drive/Material/Texture Import Profile", fileName = "TextureImportProfile")]
    public sealed class TextureImportProfile : ScriptableObject
    {
        public enum MatchKind
        {
            Suffix,   // ファイル名(拡張子なし)の末尾
            Prefix,   // 先頭
            Contains, // 部分一致
        }

        [Serializable]
        public struct Rule
        {
            [Tooltip("ルール名(Validation のメッセージに出る)。")]
            public string Name;

            [Tooltip("ファイル名との照合方法。")]
            public MatchKind Match;

            [Tooltip("照合する文字列(例: _N)。大文字小文字は区別しない。")]
            public string Pattern;

            [Tooltip("Texture Type。")]
            public TextureImporterType Type;

            [Tooltip("sRGB(カラーテクスチャ)か。Normal / Mask は off。")]
            public bool SRgb;

            [Tooltip("ミップマップを生成するか。UI は off。")]
            public bool Mipmaps;

            [Tooltip("圧縮品質。")]
            public TextureImporterCompression Compression;

            [Tooltip("最大サイズ(0 なら変更しない)。")]
            public int MaxSize;

            [Tooltip("Sprite のとき FullRect にする(9-slice / 拡縮向け)。")]
            public bool SpriteFullRect;

            [Tooltip("この規約に該当するテクスチャの MaterialCommon チャンネル(TextureData 作成時の既定値)。")]
            public TextureChannel Channel;

            [Tooltip("用途(TextureData 作成時の既定値)。")]
            public TextureUsage Usage;

            [Tooltip("NormalMap のとき緑(Y)を反転する(DirectX 形式の法線を Unity の OpenGL 形式へ。Substance Painter の _Normal_DirectX 用)。")]
            public bool FlipGreenChannel;
        }

        [Tooltip("OFF なら Postprocessor は何もしない(Validation の検出は続く)。")]
        public bool Enabled = true;

        [Tooltip("このパス断片を含むテクスチャだけを対象にする(空なら Assets/ 配下すべて。DDrive 本体と Tests は常に除外)。例: Assets/SourceAssets")]
        public string[] IncludePathContains = { "Assets/SourceAssets", "Assets/GameData" };

        [Tooltip("Model 用テクスチャの最大サイズ(これを超えると Validation が Warning)。0 なら検査しない。")]
        public int ModelMaxSize = 2048;

        [Tooltip("ルール。上から順に最初に一致したものを使う。")]
        public Rule[] Rules = DefaultRules();

        // 上から順に最初に一致したものを使う。Substance Painter の標準エクスポート名(`$mesh_$textureSet_<channel>`)を先に置く
        // (2026-09-11 追加。Unity URP/HDRP テンプレート: _BaseMap/_MaskMap/_Normal/_Emission、Unity 5 テンプレート:
        // _AlbedoTransparency/_MetallicSmoothness/_SpecularSmoothness、PBR Metal Rough: _BaseColor/_Roughness/_Metallic/_Emissive/_Height/_AO)。
        // DirectX 形式の法線(_Normal_DirectX)は緑を反転して Unity(OpenGL 形式)に合わせる。
        public static Rule[] DefaultRules() => new[]
        {
            // ── Substance Painter: 法線 ──
            SubstanceNormal("_Normal_DirectX", flipGreen: true),
            SubstanceNormal("_Normal_OpenGL", flipGreen: false),
            SubstanceNormal("_NormalMap", flipGreen: false),
            SubstanceNormal("_Normal", flipGreen: false),
            // ── Substance Painter: パック済みマスク(Mask チャンネル) ──
            SubstanceLinear("_MaskMap", TextureChannel.Mask, TextureImporterCompression.CompressedHQ),
            SubstanceLinear("_MetallicSmoothness", TextureChannel.Mask, TextureImporterCompression.CompressedHQ),
            SubstanceLinear("_SpecularSmoothness", TextureChannel.Mask, TextureImporterCompression.CompressedHQ),
            // ── Substance Painter: 単チャンネル(リニア。Mask に詰め直す前提なので Channel は Other) ──
            SubstanceLinear("_Metallic", TextureChannel.Other, TextureImporterCompression.Compressed),
            SubstanceLinear("_Roughness", TextureChannel.Other, TextureImporterCompression.Compressed),
            SubstanceLinear("_Smoothness", TextureChannel.Other, TextureImporterCompression.Compressed),
            SubstanceLinear("_AmbientOcclusion", TextureChannel.Other, TextureImporterCompression.Compressed),
            SubstanceLinear("_Ambient_occlusion", TextureChannel.Other, TextureImporterCompression.Compressed),
            SubstanceLinear("_Occlusion", TextureChannel.Other, TextureImporterCompression.Compressed),
            SubstanceLinear("_AO", TextureChannel.Other, TextureImporterCompression.Compressed),
            SubstanceLinear("_Height", TextureChannel.Other, TextureImporterCompression.Compressed),
            SubstanceLinear("_Opacity", TextureChannel.Other, TextureImporterCompression.Compressed),
            // ── Substance Painter: カラー ──
            SubstanceColor("_BaseMap", TextureChannel.Albedo),
            SubstanceColor("_BaseColor", TextureChannel.Albedo),
            SubstanceColor("_Base_Color", TextureChannel.Albedo),
            SubstanceColor("_AlbedoTransparency", TextureChannel.Albedo),
            SubstanceColor("_Albedo", TextureChannel.Albedo),
            SubstanceColor("_Diffuse", TextureChannel.Albedo),
            SubstanceColor("_Emission", TextureChannel.Emission),
            SubstanceColor("_Emissive", TextureChannel.Emission),
            // ── D-Drive 短縮規約 ──
            new Rule { Name = "NormalMap", Match = MatchKind.Suffix, Pattern = "_N", Type = TextureImporterType.NormalMap, SRgb = false, Mipmaps = true, Compression = TextureImporterCompression.Compressed, Channel = TextureChannel.Normal, Usage = TextureUsage.Model },
            new Rule { Name = "Mask", Match = MatchKind.Suffix, Pattern = "_M", Type = TextureImporterType.Default, SRgb = false, Mipmaps = true, Compression = TextureImporterCompression.CompressedHQ, Channel = TextureChannel.Mask, Usage = TextureUsage.Model },
            new Rule { Name = "Emission", Match = MatchKind.Suffix, Pattern = "_E", Type = TextureImporterType.Default, SRgb = true, Mipmaps = true, Compression = TextureImporterCompression.Compressed, Channel = TextureChannel.Emission, Usage = TextureUsage.Model },
            new Rule { Name = "UI Sprite", Match = MatchKind.Suffix, Pattern = "_UI", Type = TextureImporterType.Sprite, SRgb = true, Mipmaps = false, Compression = TextureImporterCompression.Compressed, SpriteFullRect = true, Channel = TextureChannel.Other, Usage = TextureUsage.UI },
            new Rule { Name = "Model default", Match = MatchKind.Prefix, Pattern = "T_", Type = TextureImporterType.Default, SRgb = true, Mipmaps = true, Compression = TextureImporterCompression.Compressed, Channel = TextureChannel.Albedo, Usage = TextureUsage.Model },
        };

        private static Rule SubstanceNormal(string suffix, bool flipGreen) => new()
        {
            Name = "Substance " + suffix, Match = MatchKind.Suffix, Pattern = suffix, Type = TextureImporterType.NormalMap,
            SRgb = false, Mipmaps = true, Compression = TextureImporterCompression.Compressed,
            Channel = TextureChannel.Normal, Usage = TextureUsage.Model, FlipGreenChannel = flipGreen,
        };

        private static Rule SubstanceLinear(string suffix, TextureChannel channel, TextureImporterCompression compression) => new()
        {
            Name = "Substance " + suffix, Match = MatchKind.Suffix, Pattern = suffix, Type = TextureImporterType.Default,
            SRgb = false, Mipmaps = true, Compression = compression, Channel = channel, Usage = TextureUsage.Model,
        };

        private static Rule SubstanceColor(string suffix, TextureChannel channel) => new()
        {
            Name = "Substance " + suffix, Match = MatchKind.Suffix, Pattern = suffix, Type = TextureImporterType.Default,
            SRgb = true, Mipmaps = true, Compression = TextureImporterCompression.Compressed, Channel = channel, Usage = TextureUsage.Model,
        };

        // プロジェクト内の Profile(Tests 配下は除外)。無ければ組み込み既定(メモリ上、保存しない)。
        private static TextureImportProfile _builtIn;

        public static TextureImportProfile FindOrDefault()
        {
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(TextureImportProfile)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var profile = AssetDatabase.LoadAssetAtPath<TextureImportProfile>(path);
                if (profile != null)
                {
                    return profile;
                }
            }

            if (_builtIn == null)
            {
                _builtIn = CreateInstance<TextureImportProfile>();
                _builtIn.name = "TextureImportProfile (built-in default)";
                _builtIn.hideFlags = HideFlags.HideAndDontSave;
            }

            return _builtIn;
        }

        // このアセットパスが規約の対象か(DDrive 本体・Tests・Packages は常に対象外)。
        public bool AppliesTo(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            if (assetPath.Contains("/Tests/") || assetPath.StartsWith("Assets/DDrive/", StringComparison.Ordinal))
            {
                return false;
            }

            if (IncludePathContains == null || IncludePathContains.Length == 0)
            {
                return true;
            }

            foreach (var fragment in IncludePathContains)
            {
                if (!string.IsNullOrEmpty(fragment) && assetPath.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryMatch(string assetPath, out Rule rule)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath ?? string.Empty);
            if (Rules != null)
            {
                for (var i = 0; i < Rules.Length; i++)
                {
                    if (Matches(Rules[i], fileName))
                    {
                        rule = Rules[i];
                        return true;
                    }
                }
            }

            rule = default;
            return false;
        }

        private static bool Matches(in Rule rule, string fileName)
        {
            if (string.IsNullOrEmpty(rule.Pattern) || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            return rule.Match switch
            {
                MatchKind.Suffix => fileName.EndsWith(rule.Pattern, StringComparison.OrdinalIgnoreCase),
                MatchKind.Prefix => fileName.StartsWith(rule.Pattern, StringComparison.OrdinalIgnoreCase),
                _ => fileName.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0,
            };
        }

        // ルールを TextureImporter に書く。変更があれば true(呼び出し側が SaveAndReimport する)。
        public static bool Apply(TextureImporter importer, in Rule rule)
        {
            if (importer == null)
            {
                return false;
            }

            var changed = false;
            if (importer.textureType != rule.Type)
            {
                importer.textureType = rule.Type;
                changed = true;
            }

            if (rule.Type != TextureImporterType.NormalMap && importer.sRGBTexture != rule.SRgb)
            {
                importer.sRGBTexture = rule.SRgb;
                changed = true;
            }

            if (importer.mipmapEnabled != rule.Mipmaps)
            {
                importer.mipmapEnabled = rule.Mipmaps;
                changed = true;
            }

            if (importer.textureCompression != rule.Compression)
            {
                importer.textureCompression = rule.Compression;
                changed = true;
            }

            if (rule.MaxSize > 0 && importer.maxTextureSize != rule.MaxSize)
            {
                importer.maxTextureSize = rule.MaxSize;
                changed = true;
            }

            if (rule.Type == TextureImporterType.NormalMap && importer.flipGreenChannel != rule.FlipGreenChannel)
            {
                importer.flipGreenChannel = rule.FlipGreenChannel;
                changed = true;
            }

            if (rule.Type == TextureImporterType.Sprite)
            {
                if (importer.spriteImportMode == SpriteImportMode.None)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changed = true;
                }

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                var mesh = rule.SpriteFullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
                if (settings.spriteMeshType != mesh)
                {
                    settings.spriteMeshType = mesh;
                    importer.SetTextureSettings(settings);
                    changed = true;
                }
            }

            return changed;
        }

        // ルールと Importer の食い違い(Validation 用)。空なら準拠。
        public static List<string> Diff(TextureImporter importer, in Rule rule)
        {
            var diffs = new List<string>();
            if (importer == null)
            {
                return diffs;
            }

            if (importer.textureType != rule.Type)
            {
                diffs.Add($"Texture Type が {importer.textureType}(規約: {rule.Type})");
            }

            if (rule.Type != TextureImporterType.NormalMap && importer.sRGBTexture != rule.SRgb)
            {
                diffs.Add($"sRGB が {(importer.sRGBTexture ? "on" : "off")}(規約: {(rule.SRgb ? "on" : "off")})");
            }

            if (importer.mipmapEnabled != rule.Mipmaps)
            {
                diffs.Add($"Mipmap が {(importer.mipmapEnabled ? "on" : "off")}(規約: {(rule.Mipmaps ? "on" : "off")})");
            }

            if (importer.textureCompression != rule.Compression)
            {
                diffs.Add($"圧縮が {importer.textureCompression}(規約: {rule.Compression})");
            }

            if (rule.MaxSize > 0 && importer.maxTextureSize != rule.MaxSize)
            {
                diffs.Add($"Max Size が {importer.maxTextureSize}(規約: {rule.MaxSize})");
            }

            if (rule.Type == TextureImporterType.NormalMap && importer.flipGreenChannel != rule.FlipGreenChannel)
            {
                diffs.Add($"緑反転が {(importer.flipGreenChannel ? "on" : "off")}(規約: {(rule.FlipGreenChannel ? "on" : "off")})");
            }

            return diffs;
        }
    }
}
