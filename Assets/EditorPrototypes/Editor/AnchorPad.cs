using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // D の「出る位置」入力: 上から見た 2D パッド(横 = X、縦 = Z、上が奥 +Z)+ 側面の高さスライダー(Y)。
    // B のパッドより大きく、1m ごとのグリッド・原点・距離リング・現在位置の点・原点からの破線・ホバー位置を描く。
    // パッドの縮尺は縦 ±3m 固定で、横は幅に応じて広がる(同じ縮尺)。範囲外の値は端に寄せて警告色にする。
    // 書き込みは Undo.RecordObject + SetDirty(1 回のドラッグは 1 回の Undo)。外部変更・Undo は Sync() で再描画する。
    public sealed class AnchorPad : VisualElement
    {
        private const float RangeZ = 3f;
        private const float MinY = -1f;
        private const float MaxY = 5f;

        private readonly UnityEngine.Object _undoTarget;
        private readonly Func<Vector3> _get;
        private readonly Action<Vector3> _apply;
        private readonly Action<bool> _changed;
        private readonly PdPalette _pal;
        private readonly VisualElement _pad;
        private readonly Slider _height;
        private readonly FloatField _fx;
        private readonly FloatField _fy;
        private readonly FloatField _fz;
        private readonly Label _note;

        private Vector3 _last;
        private bool _dragging;
        private bool _hover;
        private Vector2 _hoverPos;
        private int _undoGroup;

        public AnchorPad(UnityEngine.Object undoTarget, Func<Vector3> get, Action<Vector3> apply, Action<bool> changed)
        {
            _undoTarget = undoTarget;
            _get = get;
            _apply = apply;
            _changed = changed;
            AddToClassList("pd-anchor");

            var main = new VisualElement();
            main.AddToClassList("pd-anchor__main");
            Add(main);

            var padWrap = new VisualElement();
            padWrap.AddToClassList("pd-anchor__padwrap");
            main.Add(padWrap);

            _pad = new VisualElement { tooltip = "ドラッグで出る位置(X / Z)を変える。中心 = 原点、縦 = ±3m、Shift で 0.25m 刻み" };
            _pad.AddToClassList("pd-anchor__pad");
            _pad.generateVisualContent += Draw;
            _pal = PdPalette.Attach(_pad);
            _pad.RegisterCallback<PointerDownEvent>(OnDown);
            _pad.RegisterCallback<PointerMoveEvent>(OnMove);
            _pad.RegisterCallback<PointerUpEvent>(OnUp);
            _pad.RegisterCallback<PointerCaptureOutEvent>(e => EndDrag());
            _pad.RegisterCallback<PointerLeaveEvent>(e =>
            {
                _hover = false;
                _pad.MarkDirtyRepaint();
            });
            _pad.RegisterCallback<GeometryChangedEvent>(e => _pad.MarkDirtyRepaint());
            AddTag(_pad, "奥  +Z", "pd-anchor__tag--top");
            AddTag(_pad, "手前  -Z", "pd-anchor__tag--bottom");
            AddTag(_pad, "-X 左", "pd-anchor__tag--left");
            AddTag(_pad, "右 +X", "pd-anchor__tag--right");
            padWrap.Add(_pad);

            var side = new VisualElement();
            side.AddToClassList("pd-anchor__height");
            side.Add(new Label("高さ") { tooltip = "Anchor.LocalOffset の Y(メートル)" });
            _height = new Slider(MinY, MaxY, SliderDirection.Vertical) { tooltip = "高さ(Y)" };
            _height.AddToClassList("pd-anchor__vslider");
            _height.RegisterValueChangedCallback(e =>
            {
                var v = _last;
                v.y = Snap(e.newValue, false);
                WriteSlider(v);
            });
            side.Add(_height);
            side.Add(new Label("m") { name = "pd-height-unit" });
            main.Add(side);

            var nums = new VisualElement();
            nums.AddToClassList("pd-anchor__nums");
            _fx = NumField("X", nums, v => { var o = _last; o.x = v; return o; });
            _fz = NumField("Z", nums, v => { var o = _last; o.z = v; return o; });
            _fy = NumField("高さ", nums, v => { var o = _last; o.y = v; return o; });
            var origin = new Button(() => Write(new Vector3(0f, _last.y, 0f), true)) { text = "原点へ", tooltip = "X / Z を 0 に戻す(高さはそのまま)" };
            origin.AddToClassList("pd-btn");
            origin.AddToClassList("pd-btn--small");
            nums.Add(origin);
            Add(nums);

            _note = new Label();
            _note.AddToClassList("pd-dim");
            _note.AddToClassList("pd-anchor__note");
            Add(_note);

            RegisterCallback<AttachToPanelEvent>(e => Undo.undoRedoPerformed += Sync);
            RegisterCallback<DetachFromPanelEvent>(e => Undo.undoRedoPerformed -= Sync);

            _last = _get();
            Refresh();
        }

        // Anchor の基準(World 以外 / AnchorId あり)などの補足を出す。
        public void SetNote(string text)
        {
            _note.text = text ?? string.Empty;
            _note.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetDimmed(bool dim) => EnableInClassList("pd-anchor--dim", dim);

        public void Sync()
        {
            var v = _get();
            if (v == _last && !_dragging)
            {
                return;
            }

            _last = v;
            Refresh();
        }

        private static void AddTag(VisualElement parent, string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("pd-anchor__tag");
            l.AddToClassList(cls);
            parent.Add(l);
        }

        private FloatField NumField(string label, VisualElement parent, Func<float, Vector3> compose)
        {
            var f = new FloatField(label);
            f.RegisterValueChangedCallback(e =>
            {
                if (!Mathf.Approximately(e.newValue, e.previousValue))
                {
                    Write(compose(e.newValue), true);
                }
            });
            parent.Add(f);
            return f;
        }

        // ── 書き込み ──

        private void Write(Vector3 v, bool final)
        {
            Undo.RecordObject(_undoTarget, "出る位置を変更");
            _apply(v);
            EditorUtility.SetDirty(_undoTarget);
            _last = v;
            Refresh();
            _changed?.Invoke(final);
        }

        private void WriteSlider(Vector3 v)
        {
            if (v == _last)
            {
                return;
            }

            Write(v, true);
        }

        private static float Snap(float v, bool coarse)
        {
            var step = coarse ? 0.25f : 0.01f;
            return Mathf.Round(v / step) * step;
        }

        // ── 座標変換 ──

        private float Scale => Mathf.Max(1f, _pad.contentRect.height) / (RangeZ * 2f); // px / m

        private Vector2 ToPad(Vector2 xz)
        {
            var r = _pad.contentRect;
            return new Vector2(r.width * 0.5f + xz.x * Scale, r.height * 0.5f - xz.y * Scale);
        }

        private Vector2 FromPad(Vector2 local, bool coarse)
        {
            var r = _pad.contentRect;
            var x = (local.x - r.width * 0.5f) / Scale;
            var z = (r.height * 0.5f - local.y) / Scale;
            var maxX = r.width * 0.5f / Scale;
            x = Mathf.Clamp(x, -maxX, maxX);
            z = Mathf.Clamp(z, -RangeZ, RangeZ);
            return new Vector2(Snap(x, coarse), Snap(z, coarse));
        }

        // ── 描画 ──

        private void Draw(MeshGenerationContext mgc)
        {
            var r = _pad.contentRect;
            if (r.width < 8f || r.height < 8f)
            {
                return;
            }

            _pal.Read();
            var p = mgc.painter2D;
            var sc = Scale;
            var origin = new Vector2(r.width * 0.5f, r.height * 0.5f);

            PdPalette.RoundRect(p, 0, 0, r.width, r.height, 8f);
            p.fillColor = _pal.Track;
            p.Fill();

            // 1m グリッド
            p.lineWidth = 1f;
            p.strokeColor = _pal.Line;
            p.BeginPath();
            var maxX = Mathf.FloorToInt(r.width * 0.5f / sc);
            for (var i = -maxX; i <= maxX; i++)
            {
                var x = origin.x + i * sc;
                p.MoveTo(new Vector2(x, 0f));
                p.LineTo(new Vector2(x, r.height));
            }

            for (var j = -(int)RangeZ; j <= (int)RangeZ; j++)
            {
                var y = origin.y - j * sc;
                p.MoveTo(new Vector2(0f, y));
                p.LineTo(new Vector2(r.width, y));
            }

            p.Stroke();

            // 距離リング(1 / 2 / 3m)
            for (var ring = 1; ring <= 3; ring++)
            {
                p.strokeColor = _pal.Line;
                p.BeginPath();
                p.Arc(origin, ring * sc, 0f, 360f);
                p.Stroke();
            }

            // 軸
            p.strokeColor = _pal.LineStrong;
            p.BeginPath();
            p.MoveTo(new Vector2(origin.x, 0f));
            p.LineTo(new Vector2(origin.x, r.height));
            p.MoveTo(new Vector2(0f, origin.y));
            p.LineTo(new Vector2(r.width, origin.y));
            p.Stroke();

            // 原点マーク(十字 + 四角)
            p.strokeColor = _pal.Dim;
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.MoveTo(origin + new Vector2(-8f, 0f));
            p.LineTo(origin + new Vector2(8f, 0f));
            p.MoveTo(origin + new Vector2(0f, -8f));
            p.LineTo(origin + new Vector2(0f, 8f));
            p.Stroke();
            PdPalette.RoundRect(p, origin.x - 4f, origin.y - 4f, 8f, 8f, 2f);
            p.Stroke();

            // ホバー位置(ゴースト)
            if (_hover && !_dragging)
            {
                p.fillColor = new Color(_pal.Dot.r, _pal.Dot.g, _pal.Dot.b, 0.35f);
                p.BeginPath();
                p.Arc(ToPad(_hoverPos), 5f, 0f, 360f);
                p.Fill();
            }

            // 現在位置
            var v = _last;
            var raw = ToPad(new Vector2(v.x, v.z));
            var clamped = new Vector2(Mathf.Clamp(raw.x, 6f, r.width - 6f), Mathf.Clamp(raw.y, 6f, r.height - 6f));
            var outside = (raw - clamped).sqrMagnitude > 0.5f;
            var color = outside ? _pal.Warn : _pal.Dot;

            p.strokeColor = new Color(color.r, color.g, color.b, 0.7f);
            p.lineWidth = 1.5f;
            PdPalette.DashedLine(p, origin, clamped, 4f, 4f);

            p.fillColor = outside ? new Color(color.r, color.g, color.b, 0.28f) : _pal.Halo;
            p.BeginPath();
            p.Arc(clamped, _dragging ? 17f : 14f, 0f, 360f);
            p.Fill();
            p.fillColor = color;
            p.BeginPath();
            p.Arc(clamped, 7f, 0f, 360f);
            p.Fill();
            p.strokeColor = _pal.Handle;
            p.lineWidth = 2f;
            p.BeginPath();
            p.Arc(clamped, 7f, 0f, 360f);
            p.Stroke();
        }

        // ── 操作 ──

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0)
            {
                return;
            }

            _last = _get();
            _dragging = true;
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            _pad.CapturePointer(e.pointerId);
            Apply(e.localPosition, e.shiftKey);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_dragging)
            {
                if (_pad.HasPointerCapture(e.pointerId))
                {
                    Apply(e.localPosition, e.shiftKey);
                }

                return;
            }

            _hover = true;
            _hoverPos = FromPad(e.localPosition, e.shiftKey);
            _pad.MarkDirtyRepaint();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (!_dragging)
            {
                return;
            }

            if (_pad.HasPointerCapture(e.pointerId))
            {
                _pad.ReleasePointer(e.pointerId);
            }

            EndDrag();
        }

        private void EndDrag()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            Undo.CollapseUndoOperations(_undoGroup);
            Refresh();
            _changed?.Invoke(true);
        }

        private void Apply(Vector2 local, bool coarse)
        {
            var xz = FromPad(local, coarse);
            Write(new Vector3(xz.x, _last.y, xz.y), false);
        }

        private void Refresh()
        {
            var v = _last;
            _height.SetValueWithoutNotify(Mathf.Clamp(v.y, MinY, MaxY));
            _fx.SetValueWithoutNotify(v.x);
            _fz.SetValueWithoutNotify(v.z);
            _fy.SetValueWithoutNotify(v.y);
            _pad.MarkDirtyRepaint();
        }
    }
}
