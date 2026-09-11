using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // Texture を Sprite (Multiple) に変換し、Grid 分割 + 有効枚数制限を適用する。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.SpriteSlicer。
    // com.unity.2d.sprite パッケージ(ISpriteEditorDataProvider)が本プロジェクトに無いため、
    // 旧来の TextureImporter.spritesheet(SpriteMetaData 配列)経由で矩形を書き込むフォールバック実装にしている
    // ([05] C-2, チケット 3-11)。
    public static class SpriteSlicer
    {
        // 指定 Texture を Sprite 化して分割し、分割後の Sprite 配列を取得する。
        // columns/rows: 横縦分割数。spriteCount: 有効枚数(左上から行優先で使用する枚数)。
        public static bool SliceAndCollect(
            Texture2D texture,
            int columns,
            int rows,
            int spriteCount,
            out Sprite[] outSprites)
        {
            outSprites = null;

            if (texture == null)
            {
                Debug.LogError("[SpriteSlicer] Texture is null.");
                return false;
            }

            if (columns <= 0 || rows <= 0)
            {
                Debug.LogError("[SpriteSlicer] columns / rows は 1 以上を指定してください。");
                return false;
            }

            if (spriteCount <= 0)
            {
                Debug.LogError("[SpriteSlicer] spriteCount は 1 以上を指定してください。");
                return false;
            }

            var maxCount = columns * rows;
            var useCount = Mathf.Min(spriteCount, maxCount);

            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[SpriteSlicer] Texture のアセットパスが取得できません。");
                return false;
            }

            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogError($"[SpriteSlicer] TextureImporter が取得できません: {path}");
                return false;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;

            // SpriteMetaData.rect は「元画像のピクセル座標」で、Max Size で縮小されたぶんは Unity 側がスケールする。
            // よってセルは元画像サイズ(GetSourceTextureWidthAndHeight)で計算すれば足り、Max Size は触らない
            // (勝手に 16384 へ書き換えるとプロジェクトのテクスチャ設定を壊す。2026-09-11 レビュー対応)。
            importer.GetSourceTextureWidthAndHeight(out var texW, out var texH);
            if (texW <= 0 || texH <= 0)
            {
                texW = texture.width;
                texH = texture.height;
            }

            var cellW = texW / columns;
            var cellH = texH / rows;
            if (cellW <= 0 || cellH <= 0)
            {
                Debug.LogError("[SpriteSlicer] セルサイズが 0 以下になりました。columns/rows を見直してください。");
                return false;
            }

            var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
            var metas = new List<SpriteMetaData>(useCount);

            // 左上から行優先(Sprite 座標は左下原点なので Y を反転)。
            var idx = 0;
            for (var row = 0; row < rows && idx < useCount; row++)
            {
                for (var col = 0; col < columns && idx < useCount; col++)
                {
                    var x = col * cellW;
                    var y = texH - (row + 1) * cellH;
                    metas.Add(new SpriteMetaData
                    {
                        name = $"{baseName}_{idx}",
                        rect = new Rect(x, y, cellW, cellH),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                    });
                    idx++;
                }
            }

#pragma warning disable CS0618 // spritesheet は旧 API だが 2D Sprite パッケージ非依存のフォールバック経路として使う
            importer.spritesheet = metas.ToArray();
#pragma warning restore CS0618
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            AssetDatabase.Refresh();

            return SpriteCollectUtility.CollectOrdered(path, baseName, useCount, out outSprites);
        }
    }

    // Slicer 系で共通の「保存済みインポート結果から名前で Sprite を引き当てる」処理。
    internal static class SpriteCollectUtility
    {
        public static bool CollectOrdered(string path, string baseName, int count, out Sprite[] outSprites)
        {
            outSprites = null;

            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            var nameToSprite = new Dictionary<string, Sprite>();
            foreach (var a in all)
            {
                if (a is Sprite s)
                {
                    nameToSprite[s.name] = s;
                }
            }

            var collected = new Sprite[count];
            var hit = 0;
            for (var i = 0; i < count; i++)
            {
                if (nameToSprite.TryGetValue($"{baseName}_{i}", out var sp))
                {
                    collected[i] = sp;
                    hit++;
                }
            }

            if (hit < count)
            {
                Debug.LogError($"[SpriteSlicer] 分割後の Sprite 取得に失敗しました。取得数: {hit}/{count}");
                return false;
            }

            outSprites = collected;
            return true;
        }
    }
}
