using System;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // D の「長さ」入力: LifeMode / Duration / FadeOut を 1 本の横バーで表す(docs/1008 §3-D)。
    //  - OneShot / Duration: 固定長のバー + 末尾の余韻(FadeOut)を半透明で。バー(または右端)のドラッグで Duration、
    //    バーの右の空き地のドラッグで FadeOut。
    //  - Loop: ∞ 表示(止めるまで続く)。右端の余韻だけドラッグできる。
    // 書き込みは Undo.RecordObject + SetDirty(1 回のドラッグは 1 回の Undo にまとめる)。外部変更・Undo は Sync() で再描画する。
    internal sealed class DurationBar : VisualElement
    {
        internal struct Values : IEquatable<Values>
        {
            public VfxLifeMode Mode;
            public float Duration;
            public float Fade;

            public bool Equals(Values o) => Mode == o.Mode && Mathf.Approximately(Duration, o.Duration) && Mathf.Approximately(Fade, o.Fade);
        }

        private static readonly float[] AxisSteps = { 1f, 2f, 3f, 5f, 8f, 10f, 15f, 20f, 30f, 60f, 120f, 300f };
        private static readonly string[] ModeNames = { "一回で終わる", "止めるまで続く", "秒数で終わる" };
        private static readonly VfxLifeMode[] ModeOrder = { VfxLifeMode.OneShot, VfxLifeMode.Loop, VfxLifeMode.Duration };

        private enum Drag { None, Duration, Fade }

        private const float Pad = 12f;
        private const float BarH = 28f;
        private const float HandleHit = 10f;
        private const float LoopFraction = 0.68f;

        private readonly UnityEngine.Object _undoTarget;
        private readonly Func<Values> _get;
        private readonly Action<Values> _apply;
        private readonly Action<bool> _changed;
        private readonly PdPalette _pal;
        private readonly VisualElement _track;
        private readonly Button[] _modeButtons = new Button[3];
        private readonly Label _axisStart;
        private readonly Label _axisMid;
        private readonly Label _axisEnd;
        private readonly Label _readout;
        private readonly Label _note;

        private Values _last;
        private float _axisMax = 2f;
        private Drag _drag;
        private bool _hoverDuration;
        private bool _hoverFade;
        private int _undoGroup;

        // changed(final): ドラッグ中は false、離したとき・モード切替は true。
        public DurationBar(UnityEngine.Object undoTarget, Func<Values> get, Action<Values> apply, Action<bool> changed)
        {
            _undoTarget = undoTarget;
            _get = get;
            _apply = apply;
            _changed = changed;
            AddToClassList("pd-durbar");

            var modes = new VisualElement();
            modes.AddToClassList("pd-seg");
            modes.AddToClassList("pd-durbar__modes");
            for (var i = 0; i < 3; i++)
            {
                var mode = ModeOrder[i];
                var b = new Button(() => SetMode(mode)) { text = ModeNames[i] };
                b.AddToClassList("pd-seg__btn");
                _modeButtons[i] = b;
                modes.Add(b);
            }

            Add(modes);

            _track = new VisualElement { tooltip = "バーのドラッグ: 続く秒数 / バーの右の空き地のドラッグ: 停止後の余韻(Shift で 0.25 秒刻み)" };
            _track.AddToClassList("pd-durbar__track");
            _track.generateVisualContent += Draw;
            _pal = PdPalette.Attach(_track);
            _track.RegisterCallback<PointerDownEvent>(OnDown);
            _track.RegisterCallback<PointerMoveEvent>(OnMove);
            _track.RegisterCallback<PointerUpEvent>(OnUp);
            _track.RegisterCallback<PointerCaptureOutEvent>(e => EndDrag());
            _track.RegisterCallback<PointerLeaveEvent>(e =>
            {
                if (_drag == Drag.None)
                {
                    _hoverDuration = _hoverFade = false;
                    _track.MarkDirtyRepaint();
                }
            });
            _track.RegisterCallback<GeometryChangedEvent>(e => _track.MarkDirtyRepaint());
            Add(_track);

            var axis = new VisualElement();
            axis.AddToClassList("pd-durbar__axis");
            _axisStart = new Label("0 秒");
            _axisMid = new Label();
            _axisEnd = new Label();
            foreach (var l in new[] { _axisStart, _axisMid, _axisEnd })
            {
                l.AddToClassList("pd-dim");
                axis.Add(l);
            }

            Add(axis);

            _readout = new Label();
            _readout.AddToClassList("pd-durbar__readout");
            Add(_readout);
            _note = new Label();
            _note.AddToClassList("pd-dim");
            _note.AddToClassList("pd-durbar__readout");
            Add(_note);

            RegisterCallback<AttachToPanelEvent>(e => Undo.undoRedoPerformed += Sync);
            RegisterCallback<DetachFromPanelEvent>(e => Undo.undoRedoPerformed -= Sync);

            _last = _get();
            RecomputeAxis(_last);
            Refresh();
        }

        // 外部変更(Inspector・Undo・別の欄)を取り込んで描き直す。値が変わっていなければ何もしない。
        public void Sync()
        {
            var v = _get();
            if (v.Equals(_last) && _drag == Drag.None)
            {
                return;
            }

            _last = v;
            if (_drag == Drag.None)
            {
                RecomputeAxis(v);
            }

            Refresh();
        }

        private void SetMode(VfxLifeMode mode)
        {
            var v = _get();
            if (v.Mode == mode)
            {
                return;
            }

            Write(mode, v.Duration, v.Fade, true);
        }

        private void Write(VfxLifeMode mode, float duration, float fade, bool final)
        {
            Undo.RecordObject(_undoTarget, "長さを変更");
            var v = new Values { Mode = mode, Duration = duration, Fade = fade };
            _apply(v);
            EditorUtility.SetDirty(_undoTarget);
            _last = v;
            if (final && _drag == Drag.None)
            {
                RecomputeAxis(v);
            }

            Refresh();
            _changed?.Invoke(final);
        }

        // ── 軸とレイアウト ──

        private void RecomputeAxis(Values v)
        {
            var need = (v.Mode == VfxLifeMode.Loop ? 1f : v.Duration + v.Fade) * 1.5f;
            _axisMax = AxisSteps[AxisSteps.Length - 1];
            for (var i = 0; i < AxisSteps.Length; i++)
            {
                if (AxisSteps[i] >= need)
                {
                    _axisMax = AxisSteps[i];
                    break;
                }
            }
        }

        private struct Layout
        {
            public float X0;          // 時間 0 の x
            public float Width;       // 軸の幅(px)
            public float PxPerSec;
            public float EndX;        // 固定長バーの右端 / Loop のとき ∞ 区間の右端
            public float FadeEndX;
        }

        private Layout GetLayout(Values v)
        {
            var w = Mathf.Max(1f, _track.contentRect.width);
            var l = new Layout { X0 = Pad, Width = Mathf.Max(1f, w - Pad * 2f) };
            if (v.Mode == VfxLifeMode.Loop)
            {
                var fadeAxis = Mathf.Max(1f, Mathf.Ceil(v.Fade * 1.5f));
                l.EndX = l.X0 + l.Width * LoopFraction;
                l.PxPerSec = l.Width * (1f - LoopFraction) / fadeAxis;
            }
            else
            {
                l.PxPerSec = l.Width / _axisMax;
                l.EndX = l.X0 + v.Duration * l.PxPerSec;
            }

            l.FadeEndX = l.EndX + v.Fade * l.PxPerSec;
            return l;
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
            var v = _last;
            var l = GetLayout(v);
            var cy = r.height * 0.5f;
            var top = cy - BarH * 0.5f;

            PdPalette.RoundRect(p, 0, 0, r.width, r.height, 8f);
            p.fillColor = _pal.Track;
            p.Fill();

            // 目盛り
            p.lineWidth = 1f;
            if (v.Mode != VfxLifeMode.Loop)
            {
                var step = TickStep(_axisMax);
                p.strokeColor = _pal.Line;
                p.BeginPath();
                for (var t = 0f; t <= _axisMax + 0.001f; t += step)
                {
                    var x = l.X0 + t * l.PxPerSec;
                    p.MoveTo(new Vector2(x, 8f));
                    p.LineTo(new Vector2(x, r.height - 8f));
                }

                p.Stroke();
            }

            // 余韻(半透明のフェード)
            if (v.Fade > 0.0001f)
            {
                const int slices = 20;
                var fw = (l.FadeEndX - l.EndX) / slices;
                for (var i = 0; i < slices; i++)
                {
                    var c = _pal.Fade;
                    c.a = 0.55f * (1f - i / (float)slices);
                    p.fillColor = c;
                    p.BeginPath();
                    p.MoveTo(new Vector2(l.EndX + fw * i, top));
                    p.LineTo(new Vector2(l.EndX + fw * (i + 1) + 0.5f, top));
                    p.LineTo(new Vector2(l.EndX + fw * (i + 1) + 0.5f, top + BarH));
                    p.LineTo(new Vector2(l.EndX + fw * i, top + BarH));
                    p.ClosePath();
                    p.Fill();
                }
            }

            // 本体バー
            var barW = Mathf.Max(6f, l.EndX - l.X0);
            PdPalette.RoundRect(p, l.X0, top, barW, BarH, 6f);
            p.fillColor = v.Mode == VfxLifeMode.Loop ? WithAlpha(_pal.Fill, 0.45f) : _pal.Fill;
            p.Fill();
            PdPalette.RoundRect(p, l.X0 + 1f, top + 1f, barW - 2f, BarH * 0.42f, 5f);
            p.fillColor = new Color(1f, 1f, 1f, 0.14f);
            p.Fill();

            if (v.Mode == VfxLifeMode.Loop)
            {
                DrawInfinity(p, new Vector2((l.X0 + l.EndX) * 0.5f, cy), Mathf.Min(15f, barW * 0.1f), _pal.Handle);
                // 右端: 続くことを示す三角
                p.fillColor = _pal.Fill;
                p.BeginPath();
                p.MoveTo(new Vector2(l.EndX + 2f, cy - 7f));
                p.LineTo(new Vector2(l.EndX + 10f, cy));
                p.LineTo(new Vector2(l.EndX + 2f, cy + 7f));
                p.ClosePath();
                p.Fill();
                if (v.Fade <= 0.0001f)
                {
                    DrawGhostFade(p, new Vector2(l.EndX + 26f, cy));
                }
            }
            else
            {
                DrawHandle(p, new Vector2(l.EndX, cy), BarH + 8f, _hoverDuration || _drag == Drag.Duration);
                if (v.Fade <= 0.0001f)
                {
                    DrawGhostFade(p, new Vector2(l.EndX + 22f, cy));
                }
            }

            if (v.Fade > 0.0001f)
            {
                DrawHandle(p, new Vector2(l.FadeEndX, cy), BarH - 6f, _hoverFade || _drag == Drag.Fade);
            }
        }

        private void DrawHandle(Painter2D p, Vector2 c, float h, bool hot)
        {
            var w = hot ? 6f : 4f;
            PdPalette.RoundRect(p, c.x - w * 0.5f, c.y - h * 0.5f, w, h, w * 0.5f);
            p.fillColor = _pal.Handle;
            p.Fill();
            if (hot)
            {
                PdPalette.RoundRect(p, c.x - w * 0.5f - 3f, c.y - h * 0.5f - 3f, w + 6f, h + 6f, (w + 6f) * 0.5f);
                p.strokeColor = WithAlpha(_pal.Handle, 0.35f);
                p.lineWidth = 2f;
                p.Stroke();
            }
        }

        // 余韻が 0 のときの「ここから右へ引く」目印。
        private void DrawGhostFade(Painter2D p, Vector2 c)
        {
            p.strokeColor = WithAlpha(_pal.Dim, _hoverFade ? 1f : 0.7f);
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.Arc(c, 5f, 0f, 360f);
            p.Stroke();
            p.BeginPath();
            p.MoveTo(c + new Vector2(-2.5f, 0f));
            p.LineTo(c + new Vector2(2.5f, 0f));
            p.MoveTo(c + new Vector2(0f, -2.5f));
            p.LineTo(c + new Vector2(0f, 2.5f));
            p.Stroke();
        }

        // 横向きの 8 の字(レムニスケート)。
        private static void DrawInfinity(Painter2D p, Vector2 c, float a, Color color)
        {
            p.strokeColor = color;
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            const int n = 48;
            for (var i = 0; i <= n; i++)
            {
                var t = i / (float)n * Mathf.PI * 2f;
                var s = Mathf.Sin(t);
                var d = 1f + s * s;
                var x = a * Mathf.Cos(t) / d * 3.2f;
                var y = a * Mathf.Sin(t) * Mathf.Cos(t) / d * 1.5f;
                var pt = c + new Vector2(x, y);
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

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private static float TickStep(float axisMax) => axisMax <= 3f ? 0.5f : axisMax <= 10f ? 1f : axisMax <= 30f ? 5f : 10f;

        // ── 操作 ──

        private Drag HitTest(Vector2 pos, Values v)
        {
            var l = GetLayout(v);
            if (v.Mode == VfxLifeMode.Loop)
            {
                return pos.x >= l.EndX - HandleHit ? Drag.Fade : Drag.None;
            }

            if (v.Fade > 0.0001f && Mathf.Abs(pos.x - l.FadeEndX) <= HandleHit)
            {
                return Drag.Fade;
            }

            return pos.x <= l.EndX + HandleHit ? Drag.Duration : Drag.Fade;
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0)
            {
                return;
            }

            var v = _get();
            _last = v;
            _drag = HitTest(e.localPosition, v);
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

            var hit = HitTest(e.localPosition, _last);
            var hd = hit == Drag.Duration;
            var hf = hit == Drag.Fade;
            _track.EnableInClassList("pd-durbar__track--grab", hit != Drag.None);
            if (hd != _hoverDuration || hf != _hoverFade)
            {
                _hoverDuration = hd;
                _hoverFade = hf;
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
            RecomputeAxis(_last);
            Refresh();
            _changed?.Invoke(true);
        }

        private void ApplyDrag(Vector2 pos, bool snap)
        {
            var v = _last;
            var l = GetLayout(v);
            if (_drag == Drag.Duration)
            {
                var d = (pos.x - l.X0) / l.PxPerSec;
                d = Snap(Mathf.Clamp(d, 0.05f, _axisMax), snap);
                Write(v.Mode, Mathf.Max(0.05f, d), v.Fade, false);
            }
            else
            {
                var f = (pos.x - l.EndX) / l.PxPerSec;
                var max = v.Mode == VfxLifeMode.Loop ? 10f : Mathf.Max(0f, _axisMax - v.Duration);
                f = Snap(Mathf.Clamp(f, 0f, max), snap);
                Write(v.Mode, v.Duration, f, false);
            }
        }

        private static float Snap(float t, bool coarse)
        {
            var step = coarse ? 0.25f : 0.05f;
            return Mathf.Max(0f, Mathf.Round(t / step) * step);
        }

        // ── 文字 ──

        private void Refresh()
        {
            var v = _last;
            for (var i = 0; i < 3; i++)
            {
                _modeButtons[i].EnableInClassList("pd-seg__btn--on", ModeOrder[i] == v.Mode);
            }

            if (v.Mode == VfxLifeMode.Loop)
            {
                _axisStart.text = "0 秒";
                _axisMid.text = "止めるまで続く";
                _axisEnd.text = $"余韻の枠 {Mathf.Ceil(Mathf.Max(1f, v.Fade * 1.5f)):0} 秒";
                _readout.text = $"止めるまで続く + 停止後の余韻 {v.Fade:0.00} 秒";
                _note.text = "Loop: Stop / Kill されるまで再生を続けます。余韻は止めてから消えるまでの待ち時間です。";
            }
            else
            {
                _axisStart.text = "0 秒";
                _axisMid.text = $"{_axisMax * 0.5f:0.##} 秒";
                _axisEnd.text = $"{_axisMax:0.##} 秒";
                _readout.text = $"続く {v.Duration:0.00} 秒 + 余韻 {v.Fade:0.00} 秒 = 合計 {v.Duration + v.Fade:0.00} 秒";
                _note.text = v.Mode == VfxLifeMode.Duration
                    ? "秒数で終わる: 決めた秒数で強制的に終了します。"
                    : "一回で終わる: Prefab が自然に終わるまで。秒数は終了判定の保険に使われます。";
            }

            _track.MarkDirtyRepaint();
        }
    }
}
