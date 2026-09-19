using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CameraFx
{
    // [16_camera_haptics.md] §C-2(5-2c) — 「波形表示」の読み取り専用の簡易グラフ(編集 UI は
    // ValueDefDrawer([17] §5)を使うため、ここでは重ね描きの参考表示のみを担う)。
    // ShakeEditor: pos/rot の時系列と Envelope、HapticsEditor: Low/High の 2 本、を重ねて描く。
    public static class WaveformGraphGui
    {
        public readonly struct Curve
        {
            public readonly string Label;
            public readonly Color Color;
            public readonly float[] Samples; // 0..1 に正規化済みの値(表示側で -1..1 も許容してよいが上限は ±1 目安)

            public Curve(string label, Color color, float[] samples)
            {
                Label = label;
                Color = color;
                Samples = samples;
            }
        }

        // durationSec<=0 のときは案内文だけを表示する高さの IMGUIContainer を返す。
        public static IMGUIContainer Create(float height, System.Func<IReadOnlyList<Curve>> getCurves, System.Func<string> getEmptyMessage = null)
        {
            var container = new IMGUIContainer(() => Draw(getCurves(), getEmptyMessage?.Invoke()));
            container.style.height = height;
            container.style.marginTop = 2;
            container.style.marginBottom = 4;
            return container;
        }

        private static void Draw(IReadOnlyList<Curve> curves, string emptyMessage)
        {
            var rect = GUILayoutUtility.GetRect(10, 10000, 10, 10000);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));

            if (curves == null || curves.Count == 0)
            {
                if (!string.IsNullOrEmpty(emptyMessage))
                {
                    GUI.Label(rect, emptyMessage, EditorStyles.centeredGreyMiniLabel);
                }

                return;
            }

            var midY = rect.y + rect.height * 0.5f;
            Handles.color = new Color(1f, 1f, 1f, 0.2f);
            Handles.DrawAAPolyLine(2f, new Vector3(rect.x, midY), new Vector3(rect.xMax, midY));

            foreach (var curve in curves)
            {
                DrawCurve(rect, curve);
            }

            var legendY = rect.y + 2;
            var legendX = rect.x + 4;
            foreach (var curve in curves)
            {
                var size = EditorStyles.miniLabel.CalcSize(new GUIContent(curve.Label));
                var swatch = new Rect(legendX, legendY + 2, 8, 8);
                EditorGUI.DrawRect(swatch, curve.Color);
                GUI.Label(new Rect(legendX + 10, legendY, size.x, size.y), curve.Label, EditorStyles.miniLabel);
                legendX += size.x + 20;
            }
        }

        private static void DrawCurve(Rect rect, Curve curve)
        {
            if (curve.Samples == null || curve.Samples.Length < 2)
            {
                return;
            }

            var points = new Vector3[curve.Samples.Length];
            for (var i = 0; i < curve.Samples.Length; i++)
            {
                var t = i / (float)(curve.Samples.Length - 1);
                var x = Mathf.Lerp(rect.x, rect.xMax, t);
                // value 0..1 を下端..上端へ(1 = 上端寄り 80%地点。振れ幅がある値は呼び出し側で -1..1 を渡してよい)。
                var v = Mathf.Clamp(curve.Samples[i], -1f, 1f);
                var y = Mathf.Lerp(rect.yMax - 4, rect.y + 4, (v + 1f) * 0.5f);
                points[i] = new Vector3(x, y);
            }

            Handles.color = curve.Color;
            Handles.DrawAAPolyLine(2.5f, points);
        }
    }
}
