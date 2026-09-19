using System;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Common
{
    // [11_tasks.md] U-7(2026-09-17) — AnimEditorWindow(3-3)のシークバー(暗い背景 + 目盛り付きバー +
    // 白い再生ヘッド + クリックでシーク)の「形」を PresentationEditorWindow の統合プレビューでも
    // 使えるように切り出した共通ヘルパー。ピクセル位置は AnimEditorWindow.DrawTimeline の値をそのまま
    // 既定値にしている(見た目は変えていない)。イベントマーカーや SE 波形など種別固有の描画は、
    // 呼び出し側が DrawBar が返す bar 矩形の上にこれまでどおり追加で描く。
    // Presentation のトラック編集タイムライン(PresentationEditorWindow.Tracks.cs)はズーム/パン/複数レーンを
    // 持つ別物なので、この共通化の対象にはしていない(Anim 側の見た目・挙動を壊さない方針は変えない)。
    public static class SeekBarGui
    {
        public const float DefaultMarginX = 8f;
        public const float DefaultBarTop = 18f;
        public const float DefaultBarHeight = 8f;
        public const float DefaultPlayheadOvershoot = 8f;

        private static readonly Color BackgroundColor = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color BarColor = new Color(0.3f, 0.3f, 0.3f);

        // シークバー全体の背景(暗い矩形)を塗る。
        public static void DrawBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, BackgroundColor);
        }

        // rect の内側に目盛り付きのバーを描き、そのバー矩形を返す(クリック判定・再生ヘッド描画に使う)。
        // totalUnits が 0 以下、または labelFormatter が null のときは目盛りを省略してバーだけ描く。
        public static Rect DrawBar(Rect rect, int totalUnits, Func<int, string> labelFormatter,
            float marginX = DefaultMarginX, float barTop = DefaultBarTop, float barHeight = DefaultBarHeight)
        {
            var bar = new Rect(rect.x + marginX, rect.y + barTop, rect.width - marginX * 2f, barHeight);
            EditorGUI.DrawRect(bar, BarColor);
            if (totalUnits > 0 && labelFormatter != null)
            {
                TimelineRulerGui.DrawTicks(bar, totalUnits, labelFormatter);
            }

            return bar;
        }

        // normalized(0..1)の位置に白い再生ヘッドを描く。負値(未再生/Handle 無効)のときは何も描かない。
        public static void DrawPlayhead(Rect bar, float normalized, float overshoot = DefaultPlayheadOvershoot)
        {
            if (normalized < 0f)
            {
                return;
            }

            var px = bar.x + bar.width * Mathf.Clamp01(normalized);
            EditorGUI.DrawRect(new Rect(px - 1f, bar.y - overshoot, 2f, bar.height + overshoot * 2f), Color.white);
        }

        // hitRect 内でのクリック(MouseDown)を、bar の x 幅を基準にした 0..1 の位置に変換する。
        // ドラッグでの連続シークは対象にしない(Anim Editor のシークバーもクリックのみ)。
        public static bool TryHandleClickSeek(Rect hitRect, Rect bar, Event evt, out float normalized)
        {
            normalized = 0f;
            if (evt == null || evt.type != EventType.MouseDown || !hitRect.Contains(evt.mousePosition) || bar.width <= 0f)
            {
                return false;
            }

            normalized = Mathf.Clamp01((evt.mousePosition.x - bar.x) / bar.width);
            return true;
        }
    }
}
