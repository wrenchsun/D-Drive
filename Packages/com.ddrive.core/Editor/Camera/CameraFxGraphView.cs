using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CameraFx
{
    // Shake / Haptics の波形グラフ(docs/1010 UX-10。旧 WaveformGraphGui〔IMGUI の読み取り専用〕を Painter2D の視覚部品として作り直したもの)。
    // 時間軸(秒)の上に Envelope・位置・回転(Shake)/ Low・High(Haptics)の線を重ねて描く。数値の欄を編集すると描き直される(Sync)。
    // ドラッグで編集できるもの:
    //   - 長さ: 右端の縦棒(Shake = Envelope の尺、Haptics = 両モーターの尺を同じ比率で)。Shift で 0.25 秒刻み
    //   - 強さ(Haptics のみ): 時間 0 の位置の点(Low / High それぞれ。固定値 / 始まりの値)。Shift で 0.1 刻み
    // 1 回のドラッグは 1 回の Undo にまとめる。色は USS の --ux-p-*(UxPalette)から読む。
    internal sealed class CameraFxGraphView : VisualElement, IUxSyncable
    {
        private enum Drag
        {
            None,
            Duration,
            Handle0,
            Handle1,
        }

        private static readonly float[] AxisSteps = { 0.25f, 0.5f, 1f, 2f, 3f, 5f, 8f, 10f, 15f, 20f, 30f, 60f };

        private const float PadL = 14f;
        private const float PadR = 18f;
        private const float PadT = 12f;
        private const float PadB = 12f;
        private const float HitRadius = 11f;

        private readonly CameraFxGraphSource _source;
        private readonly Action<bool> _changed;
        private readonly UxPalette _pal;
        private readonly VisualElement _track;
        private readonly VisualElement _legend;
        private readonly Label _axisMid;
        private readonly Label _axisEnd;
        private readonly Label _readout;
        private readonly Label _message;

        private IReadOnlyList<CameraFxSeries> _series;
        private float _duration;
        private float _axisMax = 1f;
        private int _signature;
        private Drag _drag;
        private Drag _hover;
        private int _undoGroup;

        public CameraFxGraphSource Source => _source;

        // changed(final): ドラッグ中は false、離したとき true。
        public CameraFxGraphView(CameraFxGraphSource source, Action<bool> changed)
        {
            _source = source;
            _changed = changed;
            AddToClassList("ux-fxg");

            _legend = new VisualElement();
            _legend.AddToClassList("ux-fxg__legend");
            Add(_legend);

            _track = new VisualElement();
            _track.AddToClassList("ux-fxg__track");
            _track.generateVisualContent += Draw;
            _pal = UxPalette.Attach(_track);
            _track.RegisterCallback<PointerDownEvent>(OnDown);
            _track.RegisterCallback<PointerMoveEvent>(OnMove);
            _track.RegisterCallback<PointerUpEvent>(OnUp);
            _track.RegisterCallback<PointerCaptureOutEvent>(e => EndDrag());
            _track.RegisterCallback<PointerLeaveEvent>(e =>
            {
                if (_drag == Drag.None && _hover != Drag.None)
                {
                    _hover = Drag.None;
                    _track.RemoveFromClassList("ux-fxg__track--grab");
                    _track.MarkDirtyRepaint();
                }
            });
            _track.RegisterCallback<GeometryChangedEvent>(e => _track.MarkDirtyRepaint());
            Add(_track);

            _message = new Label();
            _message.AddToClassList("ux-dim");
            _message.AddToClassList("ux-fxg__message");
            _track.Add(_message);

            var axis = new VisualElement();
            axis.AddToClassList("ux-fxg__axis");
            var start = new Label("0 秒");
            _axisMid = new Label();
            _axisEnd = new Label();
            foreach (var l in new[] { start, _axisMid, _axisEnd })
            {
                l.AddToClassList("ux-dim");
                axis.Add(l);
            }

            Add(axis);

            _readout = new Label();
            _readout.AddToClassList("ux-fxg__readout");
            Add(_readout);

            RegisterCallback<AttachToPanelEvent>(e => Undo.undoRedoPerformed += Sync);
            RegisterCallback<DetachFromPanelEvent>(e => Undo.undoRedoPerformed -= Sync);

            Rebuild(true);
        }

        // 外部変更(欄の編集・Inspector・Undo)を取り込んで描き直す。線が変わっていなければ何もしない。
        public void Sync()
        {
            if (_drag != Drag.None)
            {
                return;
            }

            Rebuild(false);
        }

        // ── データの取り込み ──

        private void Rebuild(bool force)
        {
            var series = _source.BuildSeries();
            var duration = _source.Duration;
            var sig = Signature(series, duration);
            if (!force && sig == _signature)
            {
                return;
            }

            _signature = sig;
            _series = series;
            _duration = duration;
            _axisMax = AxisFor(duration);
            RefreshTexts();
            _track.MarkDirtyRepaint();
        }

        private static int Signature(IReadOnlyList<CameraFxSeries> series, float duration)
        {
            var h = 17 + Mathf.RoundToInt(duration * 1000f);
            if (series == null)
            {
                return h;
            }

            for (var s = 0; s < series.Count; s++)
            {
                h = h * 31 + series[s].Label.GetHashCode();
                var samples = series[s].Samples;
                for (var i = 0; i < samples.Length; i++)
                {
                    h = h * 31 + Mathf.RoundToInt(samples[i] * 1000f);
                }
            }

            return h;
        }

        private static float AxisFor(float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }

            var need = duration * 1.35f;
            for (var i = 0; i < AxisSteps.Length; i++)
            {
                if (AxisSteps[i] >= need)
                {
                    return AxisSteps[i];
                }
            }

            return AxisSteps[AxisSteps.Length - 1];
        }

        private void RefreshTexts()
        {
            _axisMid.text = $"{_axisMax * 0.5f:0.##} 秒";
            _axisEnd.text = $"{_axisMax:0.##} 秒";

            _legend.Clear();
            if (_series != null)
            {
                for (var i = 0; i < _series.Count; i++)
                {
                    var item = new VisualElement();
                    item.AddToClassList("ux-fxg__legenditem");
                    var swatch = new VisualElement();
                    swatch.AddToClassList("ux-fxg__swatch");
                    swatch.AddToClassList("ux-fxg__swatch--" + Mathf.Clamp(_series[i].Slot, 0, 2));
                    item.Add(swatch);
                    var label = new Label(_series[i].Label);
                    label.AddToClassList("ux-dim");
                    item.Add(label);
                    _legend.Add(item);
                }
            }

            var empty = _series == null || _series.Count == 0;
            _message.text = empty ? _source.EmptyMessage : string.Empty;
            _message.Show(empty);

            if (empty)
            {
                _readout.text = string.Empty;
                _readout.Show(false);
                return;
            }

            _readout.Show(true);
            var text = $"1 周期 {_duration:0.00} 秒";
            if (!_source.CanEditDuration)
            {
                text += "(尺が Speed / Rate で決まっているため、ドラッグでは変えられません。欄で編集してください)";
            }

            _readout.text = text;
        }

        // ── 座標 ──

        private Rect Plot()
        {
            var r = _track.contentRect;
            return new Rect(PadL, PadT, Mathf.Max(1f, r.width - PadL - PadR), Mathf.Max(1f, r.height - PadT - PadB));
        }

        private float X(Rect plot, float t) => plot.x + t / _axisMax * plot.width;

        private float Y(Rect plot, float v) =>
            _source.Bipolar ? plot.yMax - (Mathf.Clamp(v, -1f, 1f) + 1f) * 0.5f * plot.height : plot.yMax - Mathf.Clamp01(v) * plot.height;

        private Color SlotColor(int slot)
        {
            switch (slot)
            {
                case 1: return _pal.Fill;
                case 2: return _pal.Dot;
                default:
                    var c = _pal.Handle;
                    c.a = 0.85f;
                    return c;
            }
        }

        // ── 描画 ──

        private void Draw(MeshGenerationContext mgc)
        {
            var r = _track.contentRect;
            if (r.width < 8f || r.height < 8f)
            {
                return;
            }

            _pal.Read();
            var p = mgc.painter2D;
            var plot = Plot();

            UxPalette.RoundRect(p, 0, 0, r.width, r.height, 8f);
            p.fillColor = _pal.Track;
            p.Fill();

            // 目盛り(縦 4 分割)と基準線(Shake = 中央、Haptics = 下端と半分)
            p.lineWidth = 1f;
            p.strokeColor = _pal.Line;
            p.BeginPath();
            for (var k = 0; k <= 4; k++)
            {
                var x = plot.x + plot.width * k / 4f;
                p.MoveTo(new Vector2(x, plot.y));
                p.LineTo(new Vector2(x, plot.yMax));
            }

            p.Stroke();
            p.strokeColor = _pal.LineStrong;
            p.BeginPath();
            if (_source.Bipolar)
            {
                p.MoveTo(new Vector2(plot.x, plot.center.y));
                p.LineTo(new Vector2(plot.xMax, plot.center.y));
            }
            else
            {
                p.MoveTo(new Vector2(plot.x, plot.yMax));
                p.LineTo(new Vector2(plot.xMax, plot.yMax));
                p.MoveTo(new Vector2(plot.x, plot.center.y));
                p.LineTo(new Vector2(plot.xMax, plot.center.y));
            }

            p.Stroke();

            if (_series == null || _series.Count == 0 || _duration <= 0f)
            {
                return;
            }

            // 1 周期の外側を薄くする
            var endX = X(plot, _duration);
            if (endX < plot.xMax - 1f)
            {
                var shade = _pal.Track;
                shade.a = 0.55f;
                p.fillColor = shade;
                p.BeginPath();
                p.MoveTo(new Vector2(endX, plot.y - 4f));
                p.LineTo(new Vector2(plot.xMax + 4f, plot.y - 4f));
                p.LineTo(new Vector2(plot.xMax + 4f, plot.yMax + 4f));
                p.LineTo(new Vector2(endX, plot.yMax + 4f));
                p.ClosePath();
                p.Fill();
            }

            // 線
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            for (var s = 0; s < _series.Count; s++)
            {
                var samples = _series[s].Samples;
                if (samples == null || samples.Length < 2)
                {
                    continue;
                }

                p.strokeColor = SlotColor(_series[s].Slot);
                p.BeginPath();
                for (var i = 0; i < samples.Length; i++)
                {
                    var t = i / (float)(samples.Length - 1) * _duration;
                    var pt = new Vector2(X(plot, t), Y(plot, samples[i]));
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

            // 長さのつまみ(右端の縦棒)
            var hotDuration = _hover == Drag.Duration || _drag == Drag.Duration;
            var barW = hotDuration ? 6f : 4f;
            var barH = plot.height + 8f;
            if (_source.CanEditDuration)
            {
                UxPalette.RoundRect(p, endX - barW * 0.5f, plot.center.y - barH * 0.5f, barW, barH, barW * 0.5f);
                p.fillColor = _pal.Handle;
                p.Fill();
            }
            else
            {
                p.lineWidth = 1.5f;
                p.strokeColor = _pal.Dim;
                UxPalette.DashedLine(p, new Vector2(endX, plot.y - 4f), new Vector2(endX, plot.yMax + 4f), 4f, 3f);
            }

            // 強さのつまみ(Haptics: 時間 0 の位置の点)
            for (var h = 0; h < _source.HandleCount; h++)
            {
                if (!_source.CanEditHandle(h))
                {
                    continue;
                }

                var idx = _source.HandleSeries(h);
                var slot = idx >= 0 && idx < _series.Count ? _series[idx].Slot : 1;
                var c = new Vector2(X(plot, 0f), Y(plot, _source.GetHandle(h)));
                var hot = _hover == (h == 0 ? Drag.Handle0 : Drag.Handle1) || _drag == (h == 0 ? Drag.Handle0 : Drag.Handle1);
                p.fillColor = _pal.Track;
                p.BeginPath();
                p.Arc(c, hot ? 8f : 6.5f, 0f, 360f);
                p.ClosePath();
                p.Fill();
                p.fillColor = SlotColor(slot);
                p.BeginPath();
                p.Arc(c, hot ? 6f : 4.5f, 0f, 360f);
                p.ClosePath();
                p.Fill();
            }
        }

        // ── ドラッグ ──

        private Drag HitTest(Vector2 pos)
        {
            if (_series == null || _series.Count == 0 || _duration <= 0f)
            {
                return Drag.None;
            }

            var plot = Plot();
            for (var h = 0; h < _source.HandleCount; h++)
            {
                if (!_source.CanEditHandle(h))
                {
                    continue;
                }

                var c = new Vector2(X(plot, 0f), Y(plot, _source.GetHandle(h)));
                if (Vector2.Distance(pos, c) <= HitRadius)
                {
                    return h == 0 ? Drag.Handle0 : Drag.Handle1;
                }
            }

            if (_source.CanEditDuration && Mathf.Abs(pos.x - X(plot, _duration)) <= HitRadius)
            {
                return Drag.Duration;
            }

            return Drag.None;
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0)
            {
                return;
            }

            _drag = HitTest(e.localPosition);
            if (_drag == Drag.None)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            _track.CapturePointer(e.pointerId);
            ApplyDrag(e.localPosition, e.shiftKey);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_drag != Drag.None)
            {
                if (_track.HasPointerCapture(e.pointerId))
                {
                    ApplyDrag(e.localPosition, e.shiftKey);
                }

                return;
            }

            var hit = HitTest(e.localPosition);
            _track.EnableInClassList("ux-fxg__track--grab", hit != Drag.None);
            if (hit != _hover)
            {
                _hover = hit;
                _track.MarkDirtyRepaint();
            }
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_drag == Drag.None)
            {
                return;
            }

            if (_track.HasPointerCapture(e.pointerId))
            {
                _track.ReleasePointer(e.pointerId);
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
            Undo.CollapseUndoOperations(_undoGroup);
            Rebuild(true);
            _changed?.Invoke(true);
        }

        private void ApplyDrag(Vector2 pos, bool coarse)
        {
            var plot = Plot();
            if (_drag == Drag.Duration)
            {
                var d = (pos.x - plot.x) / plot.width * _axisMax;
                var step = coarse ? 0.25f : 0.05f;
                d = Mathf.Max(0.05f, Mathf.Round(Mathf.Clamp(d, 0.05f, _axisMax) / step) * step);
                _source.SetDuration(d);
            }
            else
            {
                var h = _drag == Drag.Handle0 ? 0 : 1;
                var v = (plot.yMax - pos.y) / plot.height;
                var step = coarse ? 0.1f : 0.05f;
                v = Mathf.Clamp01(Mathf.Round(Mathf.Clamp01(v) / step) * step);
                _source.SetHandle(h, v);
            }

            // ドラッグ中は軸を動かさない(つまみが逃げるため)。線だけ作り直す。
            var series = _source.BuildSeries();
            _series = series;
            _duration = _source.Duration;
            _signature = Signature(series, _duration);
            _track.MarkDirtyRepaint();
            _changed?.Invoke(false);
        }
    }
}
