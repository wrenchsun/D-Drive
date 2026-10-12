using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Audio
{
    // 波形の表示に必要な状態(呼び出し側が毎回組み立てる)。End は「有効な終了位置」(0 = 最後まで、は呼び出し側が解決済み)。
    internal struct AudioWaveSpec
    {
        public AudioClip Clip;
        public float Start;
        public float End;
        public float Length;
        public bool Editable;       // false なら範囲の編集(ドラッグ)を受け付けない
        public float Marker;        // 再生開始位置の目印(秒)。負なら出さない
        public string RangeLabel;   // 「トリム範囲」「ループ範囲」
    }

    // 波形の最大 / 最小振幅の表(クリップごとに 1 回だけ作ってキャッシュ)。旧 AudioEditorWindow のテクスチャ方式を、Painter2D で描ける列データにしたもの。
    internal static class WavePeaks
    {
        public const int Columns = 2048;
        private const int MaxEntries = 16;

        private struct Entry
        {
            public int Samples;
            public float Length;
            public (float min, float max)[] Peaks;
        }

        private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();

        // 読めないクリップ(圧縮 / ストリーミングで GetData できない等)は null。結果も null としてキャッシュする。
        public static (float min, float max)[] Get(AudioClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            var id = clip.GetInstanceID();
            if (Cache.TryGetValue(id, out var e) && e.Samples == clip.samples && Mathf.Approximately(e.Length, clip.length))
            {
                return e.Peaks;
            }

            if (Cache.Count >= MaxEntries)
            {
                Cache.Clear();
            }

            (float min, float max)[] peaks = null;
            try
            {
                if (clip.samples > 0)
                {
                    var channels = Mathf.Max(1, clip.channels);
                    var samples = new float[clip.samples * channels];
                    if (clip.GetData(samples, 0))
                    {
                        peaks = WaveformRenderer.BuildColumns(samples, channels, Columns);
                    }
                }
            }
            catch (Exception)
            {
                peaks = null;
            }

            Cache[id] = new Entry { Samples = clip.samples, Length = clip.length, Peaks = peaks };
            return peaks;
        }

        public static void Clear() => Cache.Clear();
    }

    // 新デザインの波形(Painter2D)。範囲の両端(トリム = SE / ループ = BGM)をドラッグして変える。
    // 旧 AudioEditorWindow の IMGUI 波形と同じ操作: 端の近くを掴む / 範囲外のクリックは近い方の端をそこへ動かす。
    // 書き込みは呼び出し側(setRange が Undo.RecordObject + SetDirty)。1 回のドラッグは 1 回の Undo にまとめる。
    internal sealed class AudioWaveformView : VisualElement, IUxSyncable
    {
        private const float HandleHit = 8f;
        private const float MinGap = 0.001f;

        private readonly Func<AudioWaveSpec> _get;
        private readonly Action<float, float> _setRange;
        private readonly Action<bool> _changed;
        private readonly Func<float> _playhead;
        private readonly UxPalette _pal;
        private readonly VisualElement _wave;
        private readonly Label _rangeText;
        private readonly Label _lengthText;
        private readonly Label _message;

        private AudioWaveSpec _spec;
        private float _lastPlayhead = -1f;
        private int _dragEdge = -1; // 0 = 開始, 1 = 終了
        private int _undoGroup;
        private bool _hover;
        private float _hoverX;

        public AudioWaveformView(Func<AudioWaveSpec> get, Action<float, float> setRange, Action<bool> changed, Func<float> playhead)
        {
            _get = get;
            _setRange = setRange;
            _changed = changed;
            _playhead = playhead;
            AddToClassList("ux-wave");

            _wave = new VisualElement { tooltip = "端をドラッグして範囲を変える。範囲の外をクリックすると近い方の端がそこへ動く" };
            _wave.AddToClassList("ux-wave__canvas");
            _wave.generateVisualContent += Draw;
            _pal = UxPalette.Attach(_wave);
            _wave.RegisterCallback<PointerDownEvent>(OnDown);
            _wave.RegisterCallback<PointerMoveEvent>(OnMove);
            _wave.RegisterCallback<PointerUpEvent>(OnUp);
            _wave.RegisterCallback<PointerCaptureOutEvent>(e => EndDrag());
            _wave.RegisterCallback<PointerLeaveEvent>(e =>
            {
                _hover = false;
                _wave.MarkDirtyRepaint();
            });
            _wave.RegisterCallback<GeometryChangedEvent>(e => _wave.MarkDirtyRepaint());
            Add(_wave);

            _rangeText = new Label { pickingMode = PickingMode.Ignore };
            _rangeText.AddToClassList("ux-wave__range");
            _wave.Add(_rangeText);
            _lengthText = new Label { pickingMode = PickingMode.Ignore };
            _lengthText.AddToClassList("ux-wave__length");
            _wave.Add(_lengthText);
            _message = new Label { pickingMode = PickingMode.Ignore };
            _message.AddToClassList("ux-wave__message");
            _message.Show(false);
            _wave.Add(_message);

            RegisterCallback<AttachToPanelEvent>(e =>
            {
                Undo.undoRedoPerformed += Sync;
                schedule.Execute(PollPlayhead).Every(60);
            });
            RegisterCallback<DetachFromPanelEvent>(e => Undo.undoRedoPerformed -= Sync);

            _spec = _get();
            Refresh();
        }

        public void Sync()
        {
            _spec = _get();
            Refresh();
        }

        private void PollPlayhead()
        {
            if (_playhead == null)
            {
                return;
            }

            var p = _playhead();
            if (!Mathf.Approximately(p, _lastPlayhead))
            {
                _lastPlayhead = p;
                _wave.MarkDirtyRepaint();
            }
        }

        private void Refresh()
        {
            var s = _spec;
            if (s.Clip == null)
            {
                _message.text = "波形を表示する AudioClip がありません";
                _message.Show(true);
                _rangeText.Show(false);
                _lengthText.Show(false);
            }
            else if (WavePeaks.Get(s.Clip) == null)
            {
                _message.text = "この AudioClip の波形は読めません(圧縮形式 / ストリーミングの設定)。範囲の数値は入力できます";
                _message.Show(true);
                _rangeText.Show(true);
                _lengthText.Show(true);
            }
            else
            {
                _message.Show(false);
                _rangeText.Show(true);
                _lengthText.Show(true);
            }

            _rangeText.text = s.Clip != null ? $"{s.RangeLabel}: {s.Start:F3}s – {s.End:F3}s" : string.Empty;
            _lengthText.text = s.Clip != null ? $"全体 {s.Length:F3}s" : string.Empty;
            EnableInClassList("ux-wave--readonly", !s.Editable);
            _wave.MarkDirtyRepaint();
        }

        // ── 座標 ──

        private float XOf(float seconds, float width) => _spec.Length > 0f ? Mathf.Clamp01(seconds / _spec.Length) * width : 0f;

        private float SecondsOf(float x, float width) => width > 0f ? Mathf.Clamp01(x / width) * _spec.Length : 0f;

        // ── 描画 ──

        private void Draw(MeshGenerationContext mgc)
        {
            var r = _wave.contentRect;
            if (r.width < 8f || r.height < 8f)
            {
                return;
            }

            _pal.Read();
            var p = mgc.painter2D;
            UxPalette.RoundRect(p, 0, 0, r.width, r.height, 8f);
            p.fillColor = _pal.Track;
            p.Fill();

            var s = _spec;
            if (s.Clip == null || s.Length <= 0f)
            {
                return;
            }

            var startX = XOf(s.Start, r.width);
            var endX = XOf(s.End, r.width);
            var mid = r.height * 0.5f;
            var amp = mid - 6f;

            // 中心線
            p.strokeColor = _pal.Line;
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(0f, mid));
            p.LineTo(new Vector2(r.width, mid));
            p.Stroke();

            var peaks = WavePeaks.Get(s.Clip);
            if (peaks != null)
            {
                var px = Mathf.Max(1, Mathf.Min(WavePeaks.Columns, Mathf.FloorToInt(r.width)));
                var outer = _pal.Dim;
                outer.a = 0.55f;
                DrawPeaks(p, peaks, px, r.width, mid, amp, 0f, startX, outer);
                DrawPeaks(p, peaks, px, r.width, mid, amp, startX, endX, _pal.Fill);
                DrawPeaks(p, peaks, px, r.width, mid, amp, endX, r.width, outer);
            }

            // 再生開始位置の目印(SE)
            if (s.Marker > 0f)
            {
                var mx = XOf(s.Marker, r.width);
                p.strokeColor = _pal.FillHi;
                p.lineWidth = 1.5f;
                UxPalette.DashedLine(p, new Vector2(mx, 0f), new Vector2(mx, r.height), 3f, 3f);
            }

            // 範囲の両端
            DrawEdge(p, startX, r.height, true, _dragEdge == 0);
            DrawEdge(p, endX, r.height, false, _dragEdge == 1);

            // ホバー位置
            if (_hover && _dragEdge < 0 && s.Editable)
            {
                p.strokeColor = _pal.Line;
                p.lineWidth = 1f;
                p.BeginPath();
                p.MoveTo(new Vector2(_hoverX, 0f));
                p.LineTo(new Vector2(_hoverX, r.height));
                p.Stroke();
            }

            // 再生位置
            var head = _playhead != null ? _playhead() : -1f;
            if (head >= 0f && head <= s.Length)
            {
                var hx = XOf(head, r.width);
                p.strokeColor = _pal.Handle;
                p.lineWidth = 2f;
                p.BeginPath();
                p.MoveTo(new Vector2(hx, 0f));
                p.LineTo(new Vector2(hx, r.height));
                p.Stroke();
            }
        }

        private static void DrawPeaks(Painter2D p, (float min, float max)[] peaks, int px, float width, float mid, float amp, float x0, float x1, Color color)
        {
            if (x1 <= x0)
            {
                return;
            }

            p.strokeColor = color;
            p.lineWidth = Mathf.Max(1f, width / px);
            p.BeginPath();
            var first = Mathf.Clamp(Mathf.FloorToInt(x0 / width * px), 0, px - 1);
            var last = Mathf.Clamp(Mathf.CeilToInt(x1 / width * px), 0, px);
            for (var i = first; i < last; i++)
            {
                var a = i * peaks.Length / px;
                var b = Mathf.Max(a + 1, (i + 1) * peaks.Length / px);
                var lo = 0f;
                var hi = 0f;
                for (var k = a; k < b && k < peaks.Length; k++)
                {
                    lo = Mathf.Min(lo, peaks[k].min);
                    hi = Mathf.Max(hi, peaks[k].max);
                }

                var x = (i + 0.5f) / px * width;
                if (x < x0 || x > x1)
                {
                    continue;
                }

                var yTop = mid - Mathf.Clamp(hi, 0f, 1f) * amp;
                var yBottom = mid - Mathf.Clamp(lo, -1f, 0f) * amp;
                if (yBottom - yTop < 1f)
                {
                    yBottom = yTop + 1f;
                }

                p.MoveTo(new Vector2(x, yTop));
                p.LineTo(new Vector2(x, yBottom));
            }

            p.Stroke();
        }

        private void DrawEdge(Painter2D p, float x, float height, bool isStart, bool active)
        {
            p.strokeColor = _pal.Dot;
            p.lineWidth = active ? 3f : 2f;
            p.BeginPath();
            p.MoveTo(new Vector2(x, 0f));
            p.LineTo(new Vector2(x, height));
            p.Stroke();

            // 上端の旗(開始 = 右向き、終了 = 左向き)
            var dir = isStart ? 1f : -1f;
            p.fillColor = _pal.Dot;
            p.BeginPath();
            p.MoveTo(new Vector2(x, 0f));
            p.LineTo(new Vector2(x + dir * 10f, 0f));
            p.LineTo(new Vector2(x, 10f));
            p.ClosePath();
            p.Fill();
        }

        // ── 操作 ──

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || !_spec.Editable || _spec.Clip == null || _spec.Length <= 0f)
            {
                return;
            }

            var w = _wave.contentRect.width;
            var sx = XOf(_spec.Start, w);
            var ex = XOf(_spec.End, w);
            var x = e.localPosition.x;
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            if (Mathf.Abs(x - sx) <= HandleHit)
            {
                _dragEdge = 0;
            }
            else if (Mathf.Abs(x - ex) <= HandleHit)
            {
                _dragEdge = 1;
            }
            else
            {
                _dragEdge = Mathf.Abs(x - sx) < Mathf.Abs(x - ex) ? 0 : 1;
                DragTo(x);
            }

            _wave.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_dragEdge >= 0)
            {
                if (_wave.HasPointerCapture(e.pointerId))
                {
                    DragTo(e.localPosition.x);
                }

                return;
            }

            _hover = _spec.Editable;
            _hoverX = e.localPosition.x;
            _wave.MarkDirtyRepaint();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_dragEdge < 0)
            {
                return;
            }

            if (_wave.HasPointerCapture(e.pointerId))
            {
                _wave.ReleasePointer(e.pointerId);
            }

            EndDrag();
        }

        private void EndDrag()
        {
            if (_dragEdge < 0)
            {
                return;
            }

            _dragEdge = -1;
            Undo.CollapseUndoOperations(_undoGroup);
            Sync();
            _changed?.Invoke(true);
        }

        private void DragTo(float x)
        {
            var t = SecondsOf(x, _wave.contentRect.width);
            var s = _spec.Start;
            var en = _spec.End;
            if (_dragEdge == 0)
            {
                s = Mathf.Min(t, en - MinGap);
            }
            else
            {
                en = Mathf.Max(t, s + MinGap);
            }

            s = Mathf.Max(0f, s);
            _setRange(s, en);
            _spec = _get();
            Refresh();
            _changed?.Invoke(false);
        }
    }
}
