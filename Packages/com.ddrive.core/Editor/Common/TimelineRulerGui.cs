using System;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Common
{
    // [11_tasks.md] 5-4 実装メモ — AnimEditorWindow(3-3、[05_model_animation.md] B-4)の
    // タイムライン(フレーム目盛り + 幅に応じたラベル間引き)を PresentationEditor(5-4)でも使うために
    // 切り出した共通ヘルパー。見た目・間引きロジックは AnimEditorWindow.DrawTimeline から移動しただけで
    // 変更していない(ピクセル位置・間引き幅とも同一)。Anim2DEditorWindow はタイムライン自体を
    // AnimEditorWindow へ委譲しているため、重複していたのはこの 1 箇所のみ([09_editor_tools.md] 参照)。
    public static class TimelineRulerGui
    {
        // bar: 目盛りを描く領域(呼び出し側が背景・余白を決めた後のバー矩形)。
        // totalUnits: 目盛りの総数(Anim ならフレーム数、Presentation なら 10 分割 などの目安値)。
        // labelFormatter: 目盛り位置(0..totalUnits)をラベル文字列にする。
        public static void DrawTicks(Rect bar, int totalUnits, Func<int, string> labelFormatter)
        {
            if (totalUnits <= 0)
            {
                return;
            }

            // ラベル間隔は幅に応じて間引く(40px に 1 ラベル目安)。細目盛りも 2px 未満に詰まると
            // 潰れて描画負荷だけ増えるので同じように間引く(2026-09-11 レビュー対応、AnimEditorWindow 由来)。
            var labelEvery = Mathf.Max(1, Mathf.CeilToInt(totalUnits / Mathf.Max(1f, bar.width / 40f)));
            var tickEvery = Mathf.Max(1, Mathf.CeilToInt(totalUnits / Mathf.Max(1f, bar.width / 2f)));
            for (var f = 0; f <= totalUnits; f++)
            {
                var labeled = f % labelEvery == 0 || f == totalUnits;
                if (!labeled && f % tickEvery != 0)
                {
                    continue;
                }

                var x = bar.x + bar.width * (f / (float)totalUnits);
                EditorGUI.DrawRect(new Rect(x, bar.y - (labeled ? 4f : 2f), 1f, bar.height + (labeled ? 8f : 4f)), new Color(1f, 1f, 1f, labeled ? 0.35f : 0.12f));
                if (labeled)
                {
                    GUI.Label(new Rect(x - 14f, bar.yMax + 22f, 28f, 12f), labelFormatter(f), EditorStyles.centeredGreyMiniLabel);
                }
            }
        }
    }
}
