using DDrive.Runtime.Anchoring;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anchor
{
    // [22_anchor_group.md] §3.7（2026-09-17、U-22）— 自動配置（Grid/Circle/Line/Random）の計算結果を
    // そのまま手置きの点（AnchorGroupData.Points）へ焼き付け、Layout を Manual に切り替える。
    // 変換後はパターンの設定から切り離され、点を 1 つずつ SceneView でドラッグして調整できる。
    //
    // 計算は再実装せず AnchorLayout（ランタイムの純粋関数）をそのまま通すので、変換の前後で点の位置は一致する。
    // ランダム配置はエディタ表示と同じ固定シード（sampleRandom: false）で焼く＝「今見えている配置」がそのまま残る。
    public static class AnchorGroupPointConverter
    {
        private static readonly AnchorLayoutPoint[] Buffer = new AnchorLayoutPoint[AnchorGroupData.MaxPoints];

        public static bool CanConvert(AnchorGroupData target) => target != null && target.Layout != AnchorLayoutKind.Manual;

        // 変換して点数を返す。0 = 何もしなかった（例外で止めず警告 + no-op）。
        // Undo.RecordObject + EditorUtility.SetDirty 済みなので Ctrl+Z で元のパターンに戻せる。
        public static int Convert(AnchorGroupData target)
        {
            if (target == null)
            {
                return 0;
            }

            if (target.Layout == AnchorLayoutKind.Manual)
            {
                Debug.LogWarning($"[DDrive] AnchorGroup '{target.name}' は既に Manual（手置き）です。変換するものがありません。");
                return 0;
            }

            var patternCount = AnchorLayout.GeneratePattern(target, Buffer, sampleRandom: false);
            var total = AnchorLayout.AppendManualPoints(target, Buffer, patternCount);
            if (total == 0)
            {
                Debug.LogWarning($"[DDrive] AnchorGroup '{target.name}' の点が 0 のため変換しませんでした。");
                return 0;
            }

            // 並び順（= 点の番号）はパターン → 手置きのまま保つ。Overrides.Index / Children.AtIndex が指す点が変わらない。
            var existing = target.Points;
            var kind = target.Layout.ToString();
            var points = new AnchorGroupPoint[total];
            for (var i = 0; i < total; i++)
            {
                var source = Buffer[i];
                var manualIndex = i - patternCount;
                points[i] = new AnchorGroupPoint
                {
                    Name = manualIndex >= 0 && existing != null && manualIndex < existing.Length ? existing[manualIndex].Name : $"{kind}{i}",
                    LocalOffset = source.LocalOffset,
                    LocalEuler = source.LocalEuler,
                    LocalScale = source.LocalScale == Vector3.zero ? Vector3.one : source.LocalScale,
                };
            }

            Undo.RecordObject(target, "Convert Anchor Group Pattern To Points");
            target.Points = points;
            target.Layout = AnchorLayoutKind.Manual;
            EditorUtility.SetDirty(target);

            System.Array.Clear(Buffer, 0, total);
            return total;
        }
    }
}
