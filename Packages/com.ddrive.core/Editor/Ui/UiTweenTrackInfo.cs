using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEngine;

namespace DDrive.Editor.Ui
{
    // 新 UiTweenEditor(docs/1010 UX-12)の「トラック 1 本 = 1 行」に出す文字と、トラックの書き換え操作(純関数。テストできるよう EditorWindow から切り離す)。
    internal static class UiTweenTrackInfo
    {
        // 一覧の種類チップ(CanvasItemList の絞り込みと色 ux-kind--N の添字)。
        public static readonly string[] KindNames = { "位置", "拡縮・サイズ", "回転", "透明度", "色", "塗り" };

        public static int KindOf(TweenProperty p)
        {
            switch (p)
            {
                case TweenProperty.AnchoredPosition:
                case TweenProperty.PathMove:
                    return 0;
                case TweenProperty.Scale:
                case TweenProperty.SizeDelta:
                    return 1;
                case TweenProperty.Rotation:
                case TweenProperty.RotationX:
                case TweenProperty.RotationY:
                    return 2;
                case TweenProperty.Alpha:
                    return 3;
                case TweenProperty.Color:
                case TweenProperty.ColorHue:
                    return 4;
                default:
                    return 5;
            }
        }

        public static string PropertyLabel(TweenProperty p)
        {
            switch (p)
            {
                case TweenProperty.AnchoredPosition: return "位置";
                case TweenProperty.Scale: return "拡縮";
                case TweenProperty.Rotation: return "回転(Z)";
                case TweenProperty.RotationX: return "回転 X";
                case TweenProperty.RotationY: return "回転 Y";
                case TweenProperty.Alpha: return "透明度";
                case TweenProperty.Color: return "色";
                case TweenProperty.ColorHue: return "色相";
                case TweenProperty.FillAmount: return "塗り量";
                case TweenProperty.SizeDelta: return "サイズ";
                case TweenProperty.PathMove: return "経路移動";
                default: return p.ToString();
            }
        }

        // 新しいトラックを足すときの選択肢(メニューの並び)。
        public static readonly TweenProperty[] AddChoices =
        {
            TweenProperty.AnchoredPosition, TweenProperty.Scale, TweenProperty.Rotation, TweenProperty.Alpha, TweenProperty.Color,
            TweenProperty.SizeDelta, TweenProperty.FillAmount, TweenProperty.PathMove, TweenProperty.RotationX, TweenProperty.RotationY, TweenProperty.ColorHue,
        };

        // ── 値の文字 ──

        public static string ValueText(TweenProperty p, in ParamValue v)
        {
            switch (p)
            {
                case TweenProperty.AnchoredPosition:
                case TweenProperty.SizeDelta:
                {
                    var x = VectorOf(v, 0);
                    var y = VectorOf(v, 1);
                    return $"({x:0.##}, {y:0.##})";
                }

                case TweenProperty.Scale:
                {
                    var x = VectorOf(v, 0);
                    var y = VectorOf(v, 1);
                    var z = VectorOf(v, 2);
                    if (Mathf.Approximately(x, y) && Mathf.Approximately(y, z))
                    {
                        return $"×{x:0.##}";
                    }

                    return $"({x:0.##}, {y:0.##}, {z:0.##})";
                }

                case TweenProperty.Rotation:
                case TweenProperty.RotationX:
                case TweenProperty.RotationY:
                    return $"{FloatOf(v):0.##}°";
                case TweenProperty.Alpha:
                case TweenProperty.FillAmount:
                case TweenProperty.ColorHue:
                    return $"{FloatOf(v):0.##}";
                case TweenProperty.Color:
                    return "#" + ColorUtility.ToHtmlStringRGBA(v.Type == ParamValueType.Color ? v.ColorValue : Color.white);
                default:
                    return string.Empty;
            }
        }

        private static float FloatOf(in ParamValue v) => v.Type == ParamValueType.Float ? v.FloatValue : v.VectorValue.x;

        private static float VectorOf(in ParamValue v, int axis)
        {
            if (v.Type == ParamValueType.Vector)
            {
                return v.VectorValue[axis];
            }

            return v.FloatValue;
        }

        public static string FromText(in TweenTrack t)
        {
            switch (t.From)
            {
                case TweenFromMode.Absolute: return ValueText(t.Property, t.FromValue);
                case TweenFromMode.Relative: return "今の値 + " + ValueText(t.Property, t.FromValue);
                case TweenFromMode.OffScreen: return "画面外(" + DirectionText(t.OffScreen) + ")";
                default: return "今の値";
            }
        }

        public static string ToText(in TweenTrack t)
        {
            if (t.Property == TweenProperty.PathMove)
            {
                var n = t.Path.Points != null ? t.Path.Points.Length : 0;
                return $"経路の終点({n} 点)";
            }

            return t.From == TweenFromMode.OffScreen ? "元の位置" : ValueText(t.Property, t.ToValue);
        }

        public static string DirectionText(OffScreenDirection d)
        {
            switch (d)
            {
                case OffScreenDirection.Left: return "左";
                case OffScreenDirection.Right: return "右";
                case OffScreenDirection.Top: return "上";
                default: return "下";
            }
        }

        public static string EaseText(in ValueDef m)
        {
            switch (m.Mode)
            {
                case ValueMode.Constant: return "即時";
                case ValueMode.Parametric: return m.Parametric.Kind == ParametricKind.CustomBezier ? "ベジェ" : m.Parametric.Ease.ToString();
                case ValueMode.Curve: return "カーブ";
                default: return m.Mode.ToString();
            }
        }

        public static string LoopText(in ValueDef m)
        {
            switch (m.Loop)
            {
                case LoopMode.Loop: return m.LoopCount > 0 ? $"ループ ×{m.LoopCount}" : "ループ";
                case LoopMode.PingPong: return m.LoopCount > 0 ? $"往復 ×{m.LoopCount}" : "往復";
                default: return string.Empty;
            }
        }

        public static bool IsLooping(in TweenTrack t) => t.Motion.Loop != LoopMode.Once;

        // 1 行の要約: 「開始 → 終了 · 長さ · イージング · 遅れ · ループ」。
        public static string Summary(in TweenTrack t)
        {
            var text = $"{FromText(t)} → {ToText(t)}  ·  {t.Motion.Duration:0.##} 秒  ·  {EaseText(t.Motion)}";
            if (t.Delay > 0.0001f)
            {
                text += $"  ·  {t.Delay:0.##} 秒遅れ";
            }

            var loop = LoopText(t.Motion);
            if (loop.Length > 0)
            {
                text += "  ·  " + loop;
            }

            return text;
        }

        // 検索に一致させる語(種類・プロパティ名の英名・イージング名)。
        public static string SearchText(in TweenTrack t) =>
            $"{PropertyLabel(t.Property)} {t.Property} {KindNames[KindOf(t.Property)]} {EaseText(t.Motion)} {LoopText(t.Motion)} {t.From}";

        // ── 時間 ──

        // トラックが終わる時刻(無限ループは 1 周期分)。
        public static float EndOf(in TweenTrack t) => t.Delay + Mathf.Max(0f, t.Motion.Duration) * LoopSpan(t.Motion);

        private static float LoopSpan(in ValueDef m) => m.Loop != LoopMode.Once && m.LoopCount > 0 ? m.LoopCount : 1f;

        // 全トラックの終わりと TotalDuration の大きいほう(時間バーの軸の基準)。
        public static float TimelineEnd(UiTweenData data)
        {
            if (data == null)
            {
                return 0f;
            }

            var max = data.TotalDuration;
            if (data.Tracks != null)
            {
                for (var i = 0; i < data.Tracks.Length; i++)
                {
                    max = Mathf.Max(max, EndOf(data.Tracks[i]));
                }
            }

            return max;
        }

        // 時間バーの軸の長さ(秒)。終わりの 1.25 倍を切りのよい値に切り上げる。
        public static float AxisMax(float end)
        {
            var need = Mathf.Max(0.5f, end * 1.25f);
            var steps = new[] { 0.5f, 1f, 1.5f, 2f, 3f, 4f, 5f, 8f, 10f, 15f, 20f, 30f, 60f };
            for (var i = 0; i < steps.Length; i++)
            {
                if (steps[i] >= need)
                {
                    return steps[i];
                }
            }

            return Mathf.Ceil(need / 10f) * 10f;
        }

        // ── 署名(「変わったか」だけを割り当て無しで見る) ──

        public static int Signature(UiTweenData data)
        {
            if (data == null || data.Tracks == null)
            {
                return 0;
            }

            unchecked
            {
                var h = 17 * 31 + data.Tracks.Length;
                h = h * 31 + data.TotalDuration.GetHashCode();
                for (var i = 0; i < data.Tracks.Length; i++)
                {
                    var t = data.Tracks[i];
                    h = h * 31 + (int)t.Property;
                    h = h * 31 + (int)t.Motion.Mode;
                    h = h * 31 + t.Motion.Time.Value.GetHashCode();
                    h = h * 31 + (int)t.Motion.Time.Mode;
                    h = h * 31 + (int)t.Motion.Loop;
                    h = h * 31 + t.Motion.LoopCount;
                    h = h * 31 + (int)t.Motion.Parametric.Kind;
                    h = h * 31 + (int)t.Motion.Parametric.Ease;
                    h = h * 31 + t.Motion.Parametric.BezierP1.GetHashCode();
                    h = h * 31 + t.Motion.Parametric.BezierP2.GetHashCode();
                    h = h * 31 + t.Motion.Constant.GetHashCode();
                    h = h * 31 + t.Delay.GetHashCode();
                    h = h * 31 + (int)t.From;
                    h = h * 31 + (int)t.OffScreen;
                    h = h * 31 + (int)t.FromValue.Type;
                    h = h * 31 + t.FromValue.FloatValue.GetHashCode();
                    h = h * 31 + t.FromValue.VectorValue.GetHashCode();
                    h = h * 31 + t.FromValue.ColorValue.GetHashCode();
                    h = h * 31 + t.ToValue.FloatValue.GetHashCode();
                    h = h * 31 + t.ToValue.VectorValue.GetHashCode();
                    h = h * 31 + t.ToValue.ColorValue.GetHashCode();
                    var pts = t.Path.Points;
                    h = h * 31 + (pts != null ? pts.Length : 0);
                    if (pts != null)
                    {
                        for (var k = 0; k < pts.Length; k++)
                        {
                            h = h * 31 + pts[k].GetHashCode();
                        }
                    }
                }

                return h;
            }
        }

        // ── トラックの組み立て・書き換え ──

        // 新しいトラック(プロパティごとの妥当な既定値。0.3 秒・OutCubic・開始 = 今の値)。
        public static TweenTrack NewTrack(TweenProperty p)
        {
            var t = new TweenTrack
            {
                Property = p,
                Motion = new ValueDef
                {
                    Mode = ValueMode.Parametric,
                    Parametric = EaseDef.Named(Ease.OutCubic),
                    Time = TimeDef.Duration(0.3f),
                    Loop = LoopMode.Once,
                },
                From = TweenFromMode.Current,
            };
            switch (p)
            {
                case TweenProperty.AnchoredPosition:
                    t.ToValue = Vec(0f, 0f, 0f);
                    t.FromValue = Vec(-100f, 0f, 0f);
                    t.From = TweenFromMode.Relative;
                    break;
                case TweenProperty.SizeDelta:
                    t.ToValue = Vec(200f, 80f, 0f);
                    t.FromValue = Vec(0f, 80f, 0f);
                    t.From = TweenFromMode.Absolute;
                    break;
                case TweenProperty.Scale:
                    t.FromValue = Vec(0f, 0f, 0f);
                    t.ToValue = Vec(1f, 1f, 1f);
                    t.From = TweenFromMode.Absolute;
                    break;
                case TweenProperty.Alpha:
                    t.FromValue = ParamValue.Of(0f);
                    t.ToValue = ParamValue.Of(1f);
                    t.From = TweenFromMode.Absolute;
                    break;
                case TweenProperty.FillAmount:
                    t.FromValue = ParamValue.Of(0f);
                    t.ToValue = ParamValue.Of(1f);
                    t.From = TweenFromMode.Absolute;
                    break;
                case TweenProperty.ColorHue:
                    t.FromValue = ParamValue.Of(0f);
                    t.ToValue = ParamValue.Of(1f);
                    t.From = TweenFromMode.Absolute;
                    break;
                case TweenProperty.Color:
                    t.FromValue = new ParamValue { Type = ParamValueType.Color, ColorValue = Color.white };
                    t.ToValue = new ParamValue { Type = ParamValueType.Color, ColorValue = Color.red };
                    t.From = TweenFromMode.Absolute;
                    break;
                case TweenProperty.PathMove:
                    t.Path = new SplinePathDef
                    {
                        Type = SplineType.CatmullRom,
                        Points = new[] { new Vector3(0f, 0f, 0f), new Vector3(60f, 40f, 0f), new Vector3(120f, 0f, 0f) },
                    };
                    break;
                default:
                    t.FromValue = ParamValue.Of(0f);
                    t.ToValue = ParamValue.Of(90f);
                    t.From = TweenFromMode.Absolute;
                    break;
            }

            return t;
        }

        private static ParamValue Vec(float x, float y, float z) => new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(x, y, z, 0f) };

        // プロパティが変わったとき、From / To の値の型をそのプロパティに合わせる(位置 → 透明度で Vector のまま残らないように)。
        public static void NormalizeValues(ref TweenTrack t)
        {
            t.FromValue = Coerce(t.Property, t.FromValue);
            t.ToValue = Coerce(t.Property, t.ToValue);
        }

        public static ParamValue Coerce(TweenProperty p, ParamValue v)
        {
            switch (p)
            {
                case TweenProperty.AnchoredPosition:
                case TweenProperty.SizeDelta:
                case TweenProperty.Scale:
                    if (v.Type == ParamValueType.Vector)
                    {
                        return v;
                    }

                    {
                        var f = v.Type == ParamValueType.Float ? v.FloatValue : 0f;
                        return Vec(f, f, p == TweenProperty.Scale ? f : 0f);
                    }

                case TweenProperty.Color:
                    return v.Type == ParamValueType.Color ? v : new ParamValue { Type = ParamValueType.Color, ColorValue = Color.white };
                case TweenProperty.PathMove:
                    return v;
                default:
                    return v.Type == ParamValueType.Float ? v : ParamValue.Of(v.Type == ParamValueType.Vector ? v.VectorValue.x : 0f);
            }
        }

        // 「既定値に戻す」: プロパティは残し、中身を NewTrack の既定にする。
        public static TweenTrack ResetTrack(in TweenTrack t) => NewTrack(t.Property);

        // 末尾に足す・置き換えるときの配列操作。
        public static TweenTrack[] Insert(TweenTrack[] tracks, int index, in TweenTrack t)
        {
            tracks ??= Array.Empty<TweenTrack>();
            index = Mathf.Clamp(index, 0, tracks.Length);
            var result = new TweenTrack[tracks.Length + 1];
            Array.Copy(tracks, 0, result, 0, index);
            result[index] = t;
            Array.Copy(tracks, index, result, index + 1, tracks.Length - index);
            return result;
        }

        public static TweenTrack[] RemoveAt(TweenTrack[] tracks, int index)
        {
            if (tracks == null || index < 0 || index >= tracks.Length)
            {
                return tracks;
            }

            var result = new TweenTrack[tracks.Length - 1];
            Array.Copy(tracks, 0, result, 0, index);
            Array.Copy(tracks, index + 1, result, index, tracks.Length - index - 1);
            return result;
        }

        public static void Swap(TweenTrack[] tracks, int a, int b)
        {
            if (tracks == null || a < 0 || b < 0 || a >= tracks.Length || b >= tracks.Length || a == b)
            {
                return;
            }

            (tracks[a], tracks[b]) = (tracks[b], tracks[a]);
        }
    }
}
