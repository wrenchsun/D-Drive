using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // 既にスライス済みの Texture から Sprite サブアセットを取得する。
    // 並びは Sprite Editor の表示順(左上→右下)に揃える。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.ExistingSpriteCollector(ロジックは同一)。
    public static class ExistingSpriteCollector
    {
        public static bool Collect(Texture2D texture, out Sprite[] outSprites)
        {
            outSprites = null;
            if (texture == null)
            {
                Debug.LogError("[ExistingSpriteCollector] Texture is null.");
                return false;
            }

            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[ExistingSpriteCollector] Texture のアセットパスが取得できません。");
                return false;
            }

            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            var sprites = all.OfType<Sprite>().ToArray();
            if (sprites.Length == 0)
            {
                Debug.LogError(
                    $"[ExistingSpriteCollector] {path} 内に Sprite が見つかりません。" +
                    "TextureImporter が Sprite/Multiple になっているか確認してください。");
                return false;
            }

            // Sprite.rect は左下原点のテクスチャ座標。
            var avgH = sprites.Average(s => s.rect.height);
            var tolY = Mathf.Max(1f, avgH * 0.5f);

            var rows = new List<List<Sprite>>();
            foreach (var s in sprites.OrderByDescending(s => s.rect.y + s.rect.height * 0.5f))
            {
                var cy = s.rect.y + s.rect.height * 0.5f;
                var placed = false;
                for (var i = 0; i < rows.Count; i++)
                {
                    var refCy = rows[i][0].rect.y + rows[i][0].rect.height * 0.5f;
                    if (Mathf.Abs(cy - refCy) <= tolY)
                    {
                        rows[i].Add(s);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    rows.Add(new List<Sprite> { s });
                }
            }

            var result = new List<Sprite>(sprites.Length);
            foreach (var row in rows)
            {
                row.Sort((a, b) => a.rect.x.CompareTo(b.rect.x));
                result.AddRange(row);
            }

            outSprites = result.ToArray();
            return true;
        }
    }
}
