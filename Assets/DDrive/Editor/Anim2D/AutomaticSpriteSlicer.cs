using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // Unity の Sprite Editor 自動スライス(Automatic)と同じアルゴリズムで矩形を検出し、
    // 左上→右下(Y 降順 → X 昇順)にソートして返すユーティリティ。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.AutomaticSpriteSlicer。
    // 確定処理は SpriteSlicer と同じく TextureImporter.spritesheet フォールバックで書き込む([05] C-2)。
    public static class AutomaticSpriteSlicer
    {
        // 透明領域から自動検出した矩形を取得する。並びは左上→右下にソート済み。
        public static bool DetectRects(
            Texture2D texture,
            int minSpriteSize,
            int extrudeSize,
            out Rect[] sortedRects,
            out int estimatedRows,
            out int estimatedColumns,
            out bool isRegularGrid)
        {
            sortedRects = null;
            estimatedRows = 0;
            estimatedColumns = 0;
            isRegularGrid = false;

            if (texture == null)
            {
                Debug.LogError("[AutomaticSpriteSlicer] Texture is null.");
                return false;
            }

            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[AutomaticSpriteSlicer] Texture のアセットパスが取得できません。");
                return false;
            }

            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogError($"[AutomaticSpriteSlicer] TextureImporter が取得できません: {path}");
                return false;
            }

            // SpriteRect.rect はソース画像の元サイズ座標系を期待する。GenerateAutomaticSpriteRectangles は
            // 渡した Texture2D のピクセル空間で座標を返すため、maxTextureSize によるスケールダウンが起きていると
            // 返り値の座標がソース座標より小さくなりズレる。maxTextureSize を 16384 にして元サイズのまま検出する。
            var needReimport = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                needReimport = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                importer.spriteImportMode = SpriteImportMode.Multiple;
                needReimport = true;
            }

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                needReimport = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                needReimport = true;
            }

            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                needReimport = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                needReimport = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                needReimport = true;
            }

            if (importer.maxTextureSize < 16384)
            {
                importer.maxTextureSize = 16384;
                needReimport = true;
            }

            if (needReimport)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }

            var reloaded = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (reloaded == null)
            {
                Debug.LogError("[AutomaticSpriteSlicer] 再読込に失敗しました。");
                return false;
            }

            // Unity 内部 API: Sprite Editor の "Slice → Automatic" と同等。
            var raw = InternalSpriteUtility.GenerateAutomaticSpriteRectangles(
                reloaded,
                Mathf.Max(1, minSpriteSize),
                Mathf.Max(0, extrudeSize));

            if (raw == null || raw.Length == 0)
            {
                Debug.LogWarning(
                    "[AutomaticSpriteSlicer] 検出された矩形が 0 件です。" +
                    "背景が透明でない / minSpriteSize が大きすぎる可能性があります。");
                sortedRects = System.Array.Empty<Rect>();
                return true;
            }

            var rowGroups = GroupAndSortRows(raw);

            var ordered = new List<Rect>(raw.Length);
            foreach (var row in rowGroups)
            {
                ordered.AddRange(row);
            }

            sortedRects = ordered.ToArray();

            var rowCount = rowGroups.Count;
            var colCount = rowCount > 0 ? rowGroups.Max(g => g.Count) : 0;
            var regular = rowCount > 0 && rowGroups.All(g => g.Count == colCount);

            estimatedRows = rowCount;
            estimatedColumns = colCount;
            isRegularGrid = regular;

            return true;
        }

        // 検出済み矩形を確定させ、生成された Sprite 配列を取得する。
        // 並び順は引数 sortedRects と一致する(name: "{baseName}_{i}")。
        public static bool ApplyRectsAndCollect(
            Texture2D texture,
            Rect[] sortedRects,
            out Sprite[] outSprites)
        {
            outSprites = null;

            if (texture == null)
            {
                Debug.LogError("[AutomaticSpriteSlicer] Texture is null.");
                return false;
            }

            if (sortedRects == null || sortedRects.Length == 0)
            {
                Debug.LogError("[AutomaticSpriteSlicer] 矩形配列が空です。");
                return false;
            }

            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[AutomaticSpriteSlicer] アセットパスが取得できません。");
                return false;
            }

            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogError($"[AutomaticSpriteSlicer] TextureImporter が取得できません: {path}");
                return false;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 16384;

            var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
            var metas = new SpriteMetaData[sortedRects.Length];
            for (var i = 0; i < sortedRects.Length; i++)
            {
                metas[i] = new SpriteMetaData
                {
                    name = $"{baseName}_{i}",
                    rect = sortedRects[i],
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                };
            }

#pragma warning disable CS0618
            importer.spritesheet = metas;
#pragma warning restore CS0618
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            AssetDatabase.Refresh();

            return SpriteCollectUtility.CollectOrdered(path, baseName, sortedRects.Length, out outSprites);
        }

        // 中心 Y を基準に行クラスタリングし、行内を X 昇順でソートして返す。
        // 許容誤差は平均高さの 50%(ExistingSpriteCollector と同じ方式)。
        // テクスチャ座標は左下原点のため、行は中心 Y 降順で並ぶ(=画像の上から下)。
        private static List<List<Rect>> GroupAndSortRows(Rect[] rects)
        {
            var avgH = 0f;
            foreach (var r in rects)
            {
                avgH += r.height;
            }

            avgH = rects.Length > 0 ? avgH / rects.Length : 1f;
            var yTolerance = Mathf.Max(1f, avgH * 0.5f);

            var sorted = rects
                .OrderByDescending(r => r.center.y)
                .ThenBy(r => r.xMin)
                .ToList();

            var rows = new List<List<Rect>>();

            foreach (var rect in sorted)
            {
                var cy = rect.center.y;
                var added = false;

                foreach (var row in rows)
                {
                    var refCy = row[0].center.y;
                    if (Mathf.Abs(cy - refCy) <= yTolerance)
                    {
                        row.Add(rect);
                        added = true;
                        break;
                    }
                }

                if (!added)
                {
                    rows.Add(new List<Rect> { rect });
                }
            }

            foreach (var row in rows)
            {
                row.Sort((a, b) => a.xMin.CompareTo(b.xMin));
            }

            rows.Sort((a, b) => b[0].center.y.CompareTo(a[0].center.y));

            return rows;
        }
    }
}
