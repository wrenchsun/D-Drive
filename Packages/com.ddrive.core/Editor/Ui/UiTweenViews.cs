using System;
using DDrive.Editor.Ux;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // イージング曲線の静的スケッチ(Painter2D。UiPresetGallery のカードとトラック行の詳細で使う)。動かさない(docs/1010 UX-12・CLAUDE.md §0-7)。
    internal sealed class CurveSketch : VisualElement
    {
        private readonly UxPalette _pal;
        private float[] _samples;

        public CurveSketch()
        {
            AddToClassList("ux-ut-sketch");
            _pal = UxPalette.Attach(this);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
        }

        public float[] Samples => _samples;

        public void SetSamples(float[] samples)
        {
            _samples = samples;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 8f || r.height < 8f)
            {
                return;
            }

            _pal.Read();
            var p = mgc.painter2D;
            UxPalette.RoundRect(p, 0, 0, r.width, r.height, 6f);
            p.fillColor = _pal.Track;
            p.Fill();
            if (_samples == null || _samples.Length < 2)
            {
                return;
            }

            // 0 と 1 の基準線(オーバーシュートする曲線は枠いっぱいまで使う)
            var lo = 0f;
            var hi = 1f;
            for (var i = 0; i < _samples.Length; i++)
            {
                lo = Mathf.Min(lo, _samples[i]);
                hi = Mathf.Max(hi, _samples[i]);
            }

            const float pad = 6f;
            var h = r.height - pad * 2f;
            var w = r.width - pad * 2f;
            p.lineWidth = 1f;
            p.strokeColor = _pal.Line;
            for (var k = 0; k < 2; k++)
            {
                var y = pad + h * (1f - (k - lo) / (hi - lo));
                p.BeginPath();
                p.MoveTo(new Vector2(pad, y));
                p.LineTo(new Vector2(pad + w, y));
                p.Stroke();
            }

            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = _pal.FillHi;
            p.BeginPath();
            for (var i = 0; i < _samples.Length; i++)
            {
                var t = i / (float)(_samples.Length - 1);
                var pt = new Vector2(pad + w * t, pad + h * (1f - (_samples[i] - lo) / (hi - lo)));
                if (i == 0)
                {
                    p.MoveTo(pt);
                }
                else
                {
                    p.LineTo(pt);
                }
            }

            p.Stroke();
        }
    }

    // トラック 1 本ぶんの時間バー(開始の遅れ + 長さ。docs/1010 UX-12)。バー全体のドラッグ = 開始(Delay)を動かす / 右端のドラッグ = 長さ(Motion.Duration)を変える
    // (長さは Time が「秒」のときだけ。Shift = 0.25 秒刻み、既定 0.05 秒刻み)。バーの中にイージング曲線の形を描く。
    // 軸の長さは全トラックで共通(axis。ドラッグ中は固定して、掴んだバーが逃げないようにする)。書き込みは apply が行う(Undo はここで 1 ドラッグ = 1 回にまとめる)。
    internal sealed class TrackTimeBar : VisualElement, IUxSyncable
    {
        private enum Drag { None, Move, Duration }

        private const float Pad = 8f;
        private const float BarH = 18f;
        private const float EdgeHit = 8f;
        private const float MinDuration = 0.05f;

        private readonly Func<TweenTrack> _get;
        private readonly Func<float> _axis;
        private readonly Action<float, float, bool> _apply;
        private readonly UnityEngine.Object _undoTarget;
        private readonly UxPalette _pal;

        private TweenTrack _track;
        private float[] _shape;
        private int _shapeKey;
        private float _axisMax = 1f;
        private Drag _drag;
        private Drag _hover;
        private float _dragAxis;
        private float _grabSec;
        private int _undoGroup;
        private bool _wrote;

        public TrackTimeBar(UnityEngine.Object undoTarget, Func<TweenTrack> get, Func<float> axis, Action<float, float, bool> apply)
        {
            _undoTarget = undoTarget;
            _get = get;
            _axis = axis;
            _apply = apply;
            AddToClassList("ux-ut-bar");
            _pal = UxPalette.Attach(this);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(e => EndDrag());
            RegisterCallback<PointerLeaveEvent>(e =>
            {
                if (_drag == Drag.None)
                {
                    SetHover(Drag.None);
                }
            });
            tooltip = "バーのドラッグ: 開始を遅らせる / 右端のドラッグ: 長さ(Shift で 0.25 秒刻み)";
            _track = get();
            _axisMax = Mathf.Max(0.5f, axis());
            RebuildShape();
        }

        // 外部の変更(⚙ の欄・Undo・ほかの行のドラッグで軸が変わった)を取り込んで描き直す。
        public void Sync()
        {
            if (_drag != Drag.None)
            {
                return;
            }

            var t = _get();
            var axis = Mathf.Max(0.5f, _axis());
            var key = ShapeKey(t);
            if (key != _shapeKey)
            {
                _track = t;
                RebuildShape();
                MarkDirtyRepaint();
            }
            else if (!SameTiming(t, _track) || !Mathf.Approximately(axis, _axisMax))
            {
                _track = t;
                MarkDirtyRepaint();
            }

            _axisMax = axis;
        }

        private static bool SameTiming(in TweenTrack a, in TweenTrack b) =>
            Mathf.Approximately(a.Delay, b.Delay) && Mathf.Approximately(a.Motion.Duration, b.Motion.Duration) && a.Motion.Loop == b.Motion.Loop && a.Motion.LoopCount == b.Motion.LoopCount;

        private static int ShapeKey(in TweenTrack t)
        {
            unchecked
            {
                var h = (int)t.Motion.Mode;
                h = h * 31 + (int)t.Motion.Parametric.Kind;
                h = h * 31 + (int)t.Motion.Parametric.Ease;
                h = h * 31 + t.Motion.Parametric.BezierP1.GetHashCode();
                h = h * 31 + t.Motion.Parametric.BezierP2.GetHashCode();
                h = h * 31 + (t.Motion.Curve != null ? t.Motion.Curve.length * 7 + 1 : 0);
                return h;
            }
        }

        private void RebuildShape()
        {
            _shapeKey = ShapeKey(_track);
            _shape = UiTweenPresetOps.ShapeSamples(_track.Motion, 48);
        }

        // ── 配置 ──

        private float Span(in TweenTrack t) => t.Motion.Loop != LoopMode.Once && t.Motion.LoopCount > 0 ? t.Motion.LoopCount : 1f;

        private bool Infinite(in TweenTrack t) => t.Motion.Loop != LoopMode.Once && t.Motion.LoopCount <= 0;

        private float Pps(float axis) => Mathf.Max(1f, contentRect.width - Pad * 2f) / Mathf.Max(0.01f, axis);

        // バーの左端 x と、1 周期の右端 x。
        private void Bar(in TweenTrack t, float axis, out float x0, out float x1, out float xEnd)
        {
            var pps = Pps(axis);
            x0 = Pad + t.Delay * pps;
            x1 = x0 + Mathf.Max(0.05f, t.Motion.Duration) * pps;
            xEnd = x0 + Mathf.Max(0.05f, t.Motion.Duration) * Span(t) * pps;
        }

        private bool DurationEditable(in TweenTrack t) => t.Motion.Time.Mode == TimeMode.Duration;

        // ── 描画 ──

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 24f || r.height < 8f)
            {
                return;
            }

            _pal.Read();
            var p = mgc.painter2D;
            var t = _track;
            var axis = _drag != Drag.None ? _dragAxis : _axisMax;
            var pps = Pps(axis);
            var cy = r.height * 0.5f;
            var top = cy - BarH * 0.5f;

            UxPalette.RoundRect(p, 0, 0, r.width, r.height, 6f);
            p.fillColor = _pal.Track;
            p.Fill();

            // 目盛り(0.5 / 1 / 5 / 10 秒刻み)
            var step = axis <= 3f ? 0.5f : axis <= 10f ? 1f : axis <= 30f ? 5f : 10f;
            p.lineWidth = 1f;
            p.strokeColor = _pal.Line;
            p.BeginPath();
            for (var s = 0f; s <= axis + 0.001f; s += step)
            {
                var x = Pad + s * pps;
                p.MoveTo(new Vector2(x, 4f));
                p.LineTo(new Vector2(x, r.height - 4f));
            }

            p.Stroke();

            Bar(t, axis, out var x0, out var x1, out var xEnd);
            var looping = t.Motion.Loop != LoopMode.Once;

            // 開始までの遅れ(薄い点線)
            if (t.Delay > 0.0001f)
            {
                p.strokeColor = _pal.Dim;
                UxPalette.DashedLine(p, new Vector2(Pad, cy), new Vector2(x0, cy), 3f, 3f);
            }

            // 本体(1 周期)。ループは 2 周目以降を薄く重ねる
            var barW = Mathf.Max(5f, x1 - x0);
            UxPalette.RoundRect(p, x0, top, barW, BarH, 5f);
            var fill = _pal.Fill;
            fill.a = _drag == Drag.Move || _hover == Drag.Move ? 1f : 0.88f;
            p.fillColor = fill;
            p.Fill();

            if (looping)
            {
                var more = Infinite(t) ? 3 : Mathf.Max(0, Mathf.RoundToInt(Span(t)) - 1);
                for (var k = 1; k <= more; k++)
                {
                    var bx = x0 + barW * k;
                    if (bx >= r.width - Pad)
                    {
                        break;
                    }

                    UxPalette.RoundRect(p, bx + 1f, top, Mathf.Min(barW - 2f, r.width - Pad - bx), BarH, 5f);
                    var faint = _pal.Fill;
                    faint.a = Infinite(t) ? 0.38f - 0.1f * (k - 1) : 0.45f;
                    p.fillColor = faint;
                    p.Fill();
                }

                if (Infinite(t))
                {
                    var ax = Mathf.Min(r.width - Pad, x0 + barW * 4f + 2f);
                    p.fillColor = _pal.Dim;
                    p.BeginPath();
                    p.MoveTo(new Vector2(ax, cy - 6f));
                    p.LineTo(new Vector2(ax + 8f, cy));
                    p.LineTo(new Vector2(ax, cy + 6f));
                    p.ClosePath();
                    p.Fill();
                }
            }

            // バーの中のイージング曲線
            if (_shape != null && barW > 24f)
            {
                var lo = 0f;
                var hi = 1f;
                for (var i = 0; i < _shape.Length; i++)
                {
                    lo = Mathf.Min(lo, _shape[i]);
                    hi = Mathf.Max(hi, _shape[i]);
                }

                var ih = BarH - 6f;
                p.lineWidth = 1.6f;
                p.lineJoin = LineJoin.Round;
                p.strokeColor = new Color(1f, 1f, 1f, 0.92f);
                p.BeginPath();
                for (var i = 0; i < _shape.Length; i++)
                {
                    var u = i / (float)(_shape.Length - 1);
                    var pt = new Vector2(x0 + 4f + (barW - 8f) * u, top + 3f + ih * (1f - (_shape[i] - lo) / (hi - lo)));
                    if (i == 0)
                    {
                        p.MoveTo(pt);
                    }
                    else
                    {
                        p.LineTo(pt);
                    }
                }

                p.Stroke();
            }

            // 右端のつまみ(長さ)
            if (DurationEditable(t))
            {
                var hot = _drag == Drag.Duration || _hover == Drag.Duration;
                var w = hot ? 6f : 4f;
                UxPalette.RoundRect(p, x1 - w * 0.5f, top - 3f, w, BarH + 6f, w * 0.5f);
                p.fillColor = _pal.Handle;
                p.Fill();
            }
        }

        // ── 操作 ──

        private Drag HitTest(Vector2 pos, in TweenTrack t)
        {
            var axis = _drag != Drag.None ? _dragAxis : _axisMax;
            Bar(t, axis, out var x0, out var x1, out _);
            if (DurationEditable(t) && Mathf.Abs(pos.x - x1) <= EdgeHit)
            {
                return Drag.Duration;
            }

            return pos.x >= x0 - 2f && pos.x <= x1 + 2f ? Drag.Move : Drag.None;
        }

        private void SetHover(Drag hover)
        {
            if (hover == _hover)
            {
                return;
            }

            _hover = hover;
            EnableInClassList("ux-ut-bar--edge", hover == Drag.Duration);
            EnableInClassList("ux-ut-bar--move", hover == Drag.Move);
            MarkDirtyRepaint();
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0)
            {
                return;
            }

            _track = _get();
            _axisMax = Mathf.Max(0.5f, _axis());
            var hit = HitTest(e.localPosition, _track);
            if (hit == Drag.None)
            {
                return;
            }

            _drag = hit;
            _dragAxis = _axisMax;
            _wrote = false;
            Bar(_track, _dragAxis, out var x0, out _, out _);
            _grabSec = (e.localPosition.x - x0) / Pps(_dragAxis);
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_drag == Drag.None)
            {
                SetHover(HitTest(e.localPosition, _track));
                return;
            }

            if (!this.HasPointerCapture(e.pointerId))
            {
                return;
            }

            var t = _track;
            var pps = Pps(_dragAxis);
            var delay = t.Delay;
            var duration = t.Motion.Duration;
            var coarse = e.shiftKey;
            if (_drag == Drag.Move)
            {
                delay = Snap((e.localPosition.x - Pad) / pps - _grabSec, coarse);
                var maxDelay = Mathf.Max(0f, _dragAxis - MinDuration);
                delay = Mathf.Clamp(delay, 0f, maxDelay);
            }
            else
            {
                var d = ((e.localPosition.x - Pad) / pps - t.Delay) / Span(t);
                duration = Mathf.Max(MinDuration, Snap(Mathf.Clamp(d, MinDuration, _dragAxis), coarse));
            }

            if (Mathf.Approximately(delay, t.Delay) && Mathf.Approximately(duration, t.Motion.Duration))
            {
                return;
            }

            _wrote = true;
            _apply(delay, duration, false);
            _track = _get();
            MarkDirtyRepaint();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_drag == Drag.None)
            {
                return;
            }

            if (this.HasPointerCapture(e.pointerId))
            {
                this.ReleasePointer(e.pointerId);
            }

            EndDrag();
        }

        private void EndDrag()
        {
            if (_drag == Drag.None)
            {
                return;
            }

            _drag = Drag.None;
            if (_wrote)
            {
                Undo.CollapseUndoOperations(_undoGroup);
                var t = _get();
                _apply(t.Delay, t.Motion.Duration, true);
            }

            _track = _get();
            _axisMax = Mathf.Max(0.5f, _axis());
            MarkDirtyRepaint();
        }

        private static float Snap(float v, bool coarse)
        {
            var step = coarse ? 0.25f : 0.05f;
            return Mathf.Max(0f, Mathf.Round(v / step) * step);
        }

        // テスト・自己確認用: 指定の位置(要素ローカル px)を押して動かして離す動作の再現ではなく、書き込みを 1 回だけ行う(Undo は呼び出し側で管理)。
        internal void ApplyForTest(float delay, float duration) => _apply(delay, duration, true);

        internal UnityEngine.Object UndoTarget => _undoTarget;
    }
}
