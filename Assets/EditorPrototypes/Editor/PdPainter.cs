using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // D の Painter2D 部品(DurationBar / AnchorPad / ドロップ領域)が使う色。値は USS の --pd-p-* から読む(C# に色を持たない)。
    // 読めなかったときだけ Dark 相当の既定値で描く。
    internal sealed class PdPalette
    {
        public Color Track = new Color(0.125f, 0.133f, 0.153f);
        public Color Line = new Color(1f, 1f, 1f, 0.07f);
        public Color LineStrong = new Color(1f, 1f, 1f, 0.2f);
        public Color Fill = new Color(0.29f, 0.55f, 1f);
        public Color FillHi = new Color(0.49f, 0.69f, 1f);
        public Color Fade = new Color(0.29f, 0.55f, 1f);
        public Color Handle = Color.white;
        public Color Dot = new Color(0.95f, 0.63f, 0.24f);
        public Color Halo = new Color(0.95f, 0.63f, 0.24f, 0.28f);
        public Color Dim = new Color(0.61f, 0.63f, 0.68f);
        public Color Warn = new Color(0.94f, 0.42f, 0.42f);
        public Color Drop = new Color(0.61f, 0.63f, 0.68f, 0.55f);

        private readonly VisualElement[] _swatches = new VisualElement[12];

        private static readonly string[] SwatchNames =
        {
            "track", "line", "line-strong", "fill", "fill-hi", "fade", "handle", "dot", "halo", "dim", "warn", "drop",
        };

        // 色は USS の --pd-p-* を background-color に持つ 0 サイズの子要素から読む(CustomStyleProperty は
        // テーマ切替のあとに読み直されないため)。描画の先頭で Read() を呼ぶ。
        public static PdPalette Attach(VisualElement element)
        {
            var p = new PdPalette();
            for (var i = 0; i < SwatchNames.Length; i++)
            {
                var sw = new VisualElement { pickingMode = PickingMode.Ignore };
                sw.AddToClassList("pd-pal");
                sw.AddToClassList("pd-pal--" + SwatchNames[i]);
                element.Add(sw);
                p._swatches[i] = sw;
            }

            return p;
        }

        public void Read()
        {
            Pick(0, ref Track);
            Pick(1, ref Line);
            Pick(2, ref LineStrong);
            Pick(3, ref Fill);
            Pick(4, ref FillHi);
            Pick(5, ref Fade);
            Pick(6, ref Handle);
            Pick(7, ref Dot);
            Pick(8, ref Halo);
            Pick(9, ref Dim);
            Pick(10, ref Warn);
            Pick(11, ref Drop);
        }

        private void Pick(int i, ref Color c)
        {
            var v = _swatches[i].resolvedStyle.backgroundColor;
            if (v.a > 0.001f)
            {
                c = v;
            }
        }

        // 角丸の矩形パスを作る(Fill / Stroke は呼び出し側)。
        public static void RoundRect(Painter2D p, float x, float y, float w, float h, float r)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            p.BeginPath();
            p.MoveTo(new Vector2(x + r, y));
            p.LineTo(new Vector2(x + w - r, y));
            p.ArcTo(new Vector2(x + w, y), new Vector2(x + w, y + r), r);
            p.LineTo(new Vector2(x + w, y + h - r));
            p.ArcTo(new Vector2(x + w, y + h), new Vector2(x + w - r, y + h), r);
            p.LineTo(new Vector2(x + r, y + h));
            p.ArcTo(new Vector2(x, y + h), new Vector2(x, y + h - r), r);
            p.LineTo(new Vector2(x, y + r));
            p.ArcTo(new Vector2(x, y), new Vector2(x + r, y), r);
            p.ClosePath();
        }

        // 2 点間の破線。
        public static void DashedLine(Painter2D p, Vector2 a, Vector2 b, float dash, float gap)
        {
            var len = Vector2.Distance(a, b);
            if (len < 0.01f)
            {
                return;
            }

            var dir = (b - a) / len;
            p.BeginPath();
            for (var d = 0f; d < len; d += dash + gap)
            {
                p.MoveTo(a + dir * d);
                p.LineTo(a + dir * Mathf.Min(d + dash, len));
            }

            p.Stroke();
        }
    }
}
