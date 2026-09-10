using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] B-4 — TextureData の Validation(チケット 3-8、2026-09-10)。
    // Importer が要る検査(Channel=Normal で NormalMap 設定でない 等)は、Texture がメモリ上だけの場合は
    // AssetImporter.GetAtPath が null を返すためスキップする(例外にしない)。
    public sealed class TextureDataValidator : IValidator
    {
        public AssetType Target => AssetType.Texture;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not TextureData tex)
            {
                yield break;
            }

            if (tex.Texture == null)
            {
                yield return ValidationResult.Error("Texture が未設定(または Missing)です");
                yield break; // これ以降は Texture 前提の検査なので打ち切る
            }

            var path = AssetDatabase.GetAssetPath(tex.Texture);
            var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter;

            if (tex.Usage == TextureUsage.UI && tex.Sprite == null)
            {
                var capturedData = tex;
                var capturedPath = path;
                yield return ValidationResult.Error(
                    "Usage=UI ですが Sprite が未生成です",
                    importer != null ? () => FixMakeSprite(capturedData, importer, capturedPath) : null);
            }

            if (tex.Usage == TextureUsage.UI && tex.AllowScale && tex.SliceBorder == Vector4.zero)
            {
                yield return ValidationResult.Warning("AllowScale=true ですが SliceBorder(9-slice)が未設定です");
            }

            if (importer != null)
            {
                if (tex.Channel == TextureChannel.Normal && importer.textureType != TextureImporterType.NormalMap)
                {
                    var capturedImporter = importer;
                    yield return ValidationResult.Error(
                        "Channel=Normal ですが Texture Type が NormalMap になっていません",
                        () => FixSetNormalMap(capturedImporter));
                }

                if (tex.Channel == TextureChannel.Mask && importer.sRGBTexture)
                {
                    var capturedImporter = importer;
                    yield return ValidationResult.Error(
                        "Channel=Mask ですが sRGB が on になっています",
                        () => FixDisableSrgb(capturedImporter));
                }
            }

            if (tex.Usage == TextureUsage.Model)
            {
                if (!IsPowerOfTwo(tex.Texture.width) || !IsPowerOfTwo(tex.Texture.height))
                {
                    yield return ValidationResult.Warning($"サイズが 2 のべき乗ではありません({tex.Texture.width}x{tex.Texture.height})");
                }

                var profileForSize = TextureImportProfile.FindOrDefault();
                if (importer != null && profileForSize.ModelMaxSize > 0 && importer.maxTextureSize > profileForSize.ModelMaxSize)
                {
                    yield return ValidationResult.Warning($"Max Size({importer.maxTextureSize})がプロファイル規定({profileForSize.ModelMaxSize})を超えています");
                }
            }

            if (importer != null)
            {
                var profile = TextureImportProfile.FindOrDefault();
                if (profile.AppliesTo(path) && profile.TryMatch(path, out var rule))
                {
                    var diffs = TextureImportProfile.Diff(importer, rule);
                    if (diffs.Count > 0)
                    {
                        var capturedImporter = importer;
                        var capturedRule = rule;
                        yield return ValidationResult.Warning(
                            $"インポート規約 '{rule.Name}' と食い違っています: {string.Join(" / ", diffs)}",
                            () => FixApplyRule(capturedImporter, capturedRule));
                    }
                }
            }
        }

        private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

        private static void FixMakeSprite(TextureData data, TextureImporter importer, string path)
        {
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
            }

            if (importer.spriteImportMode == SpriteImportMode.None)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
            }

            importer.SaveAndReimport();

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                Undo.RecordObject(data, "Assign Sprite");
                data.Sprite = sprite;
                EditorUtility.SetDirty(data);
            }
        }

        private static void FixSetNormalMap(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        private static void FixDisableSrgb(TextureImporter importer)
        {
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }

        private static void FixApplyRule(TextureImporter importer, TextureImportProfile.Rule rule)
        {
            if (TextureImportProfile.Apply(importer, rule))
            {
                importer.SaveAndReimport();
            }
        }
    }
}
