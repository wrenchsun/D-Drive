using System;
using DDrive.Editor.Ux;
using DDrive.Runtime.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Audio
{
    // 3D サウンド確認パッド(旧 AudioEditorWindow の IMGUI パッドを Painter2D で作り直したもの)。
    // 中央 = 音源(赤)、リスナー(橙)をドラッグで動かし、角度スライダーで向きを回す。Min / MaxDistance の円(縮尺が縦横で違うと楕円)を重ねる。
    // 実際の適用は「リスナーから見た相対位置へ音源を動かす」(ListenerPadMath)。applied は位置か角度が変わるたびに呼ばれる。
    internal sealed class AudioListenerPad : VisualElement, IUxSyncable
    {
        private const float MinRange = 0.5f;

        private readonly Func<SeData> _getSe;
        private readonly Func<(bool valid, string reason)> _validity;
        private readonly Action<Vector2, float> _applied;
        private readonly UxPalette _pal;
        private readonly VisualElement _pad;
        private readonly Label _info;
        private readonly Label _reason;
        private readonly FloatField _rangeXField;
        private readonly FloatField _rangeYField;
        private readonly Slider _angleSlider;

        private Vector2 _listenerPos = new Vector2(0f, -3f); // メートル。音源(中央)の手前 3m
        private float _angleDeg;
        private float _rangeX = 10f;
        private float _rangeY = 10f;
        private bool _dragging;
        private bool _valid = true;

        public Vector2 ListenerPos => _listenerPos;
        public float AngleDeg => _angleDeg;

        public AudioListenerPad(Func<SeData> getSe, Func<(bool valid, string reason)> validity, Action<Vector2, float> applied)
        {
            _getSe = getSe;
            _validity = validity;
            _applied = applied;
            AddToClassList("ux-lpad");

            var ranges = new VisualElement();
            ranges.AddToClassList("ux-lpad__ranges");
            _rangeXField = new FloatField("横範囲(±m)") { value = _rangeX, tooltip = "パッドの横幅が表す距離(縮尺・横)" };
            _rangeXField.AddToClassList("ux-lpad__field");
            _rangeXField.RegisterValueChangedCallback(e =>
            {
                _rangeX = Mathf.Max(MinRange, e.newValue);
                OnRangeChanged();
            });
            ranges.Add(_rangeXField);
            _rangeYField = new FloatField("縦範囲(±m)") { value = _rangeY, tooltip = "パッドの縦幅が表す距離(縮尺・縦)" };
            _rangeYField.AddToClassList("ux-lpad__field");
            _rangeYField.RegisterValueChangedCallback(e =>
            {
                _rangeY = Mathf.Max(MinRange, e.newValue);
                OnRangeChanged();
            });
            ranges.Add(_rangeYField);
            Add(ranges);

            _angleSlider = new Slider("リスナー角度", 0f, 360f) { value = _angleDeg, showInputField = true, tooltip = "リスナーの向き(度)。0 = 奥向き、時計回り。ステレオの左右定位に反映される" };
            _angleSlider.RegisterValueChangedCallback(e =>
            {
                _angleDeg = e.newValue;
                _pad.MarkDirtyRepaint();
                RefreshInfo();
                _applied?.Invoke(_listenerPos, _angleDeg);
            });
            var angle = new VisualElement(); // 行の入力の直下に置くとラベルが隠れるため、1 段包む
            angle.Add(_angleSlider);
            Add(angle);

            _pad = new VisualElement { tooltip = "ドラッグでリスナー(橙)を動かす。中央が音源。緑 = Min 距離、橙 = Max 距離" };
            _pad.AddToClassList("ux-lpad__pad");
            _pad.generateVisualContent += Draw;
            _pal = UxPalette.Attach(_pad);
            _pad.RegisterCallback<PointerDownEvent>(OnDown);
            _pad.RegisterCallback<PointerMoveEvent>(OnMove);
            _pad.RegisterCallback<PointerUpEvent>(OnUp);
            _pad.RegisterCallback<PointerCaptureOutEvent>(e => _dragging = false);
            _pad.RegisterCallback<GeometryChangedEvent>(e => _pad.MarkDirtyRepaint());
            _reason = new Label { pickingMode = PickingMode.Ignore };
            _reason.AddToClassList("ux-lpad__reason");
            _pad.Add(_reason);
            Add(_pad);

            _info = new Label();
            _info.AddToClassList("ux-dim");
            _info.AddToClassList("ux-lpad__info");
            Add(_info);

            Sync();
        }

        public void Sync()
        {
            var (valid, reason) = _validity();
            _valid = valid;
            _reason.text = valid ? string.Empty : "⚠ 3D プレビュー無効\n" + reason;
            _reason.Show(!valid);
            _rangeXField.SetEnabled(valid);
            _rangeYField.SetEnabled(valid);
            _angleSlider.SetEnabled(valid);
            RefreshInfo();
            _pad.MarkDirtyRepaint();
        }

        private void RefreshInfo()
        {
            _info.text = $"距離 {ListenerPadMath.DistanceToSource(_listenerPos):F2}m   角度 {_angleDeg:F0}°   位置 ({_listenerPos.x:F1}, {_listenerPos.y:F1})m";
        }

        private void OnRangeChanged()
        {
            _listenerPos = ListenerPadMath.ClampToRange(_listenerPos, _rangeX, _rangeY);
            _pad.MarkDirtyRepaint();
            RefreshInfo();
            _applied?.Invoke(_listenerPos, _angleDeg);
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
            UxPalette.RoundRect(p, 0, 0, r.width, r.height, 8f);
            p.fillColor = _pal.Track;
            p.Fill();

            // 十字グリッド
            p.strokeColor = _pal.LineStrong;
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(0f, r.height * 0.5f));
            p.LineTo(new Vector2(r.width, r.height * 0.5f));
            p.MoveTo(new Vector2(r.width * 0.5f, 0f));
            p.LineTo(new Vector2(r.width * 0.5f, r.height));
            p.Stroke();

            if (!_valid)
            {
                return;
            }

            var se = _getSe();
            var rect = new Rect(0f, 0f, r.width, r.height);
            if (se != null)
            {
                DrawEllipse(p, rect, se.MinDistance, _pal.Fill);
                DrawEllipse(p, rect, se.MaxDistance, _pal.Dot);
            }

            // 音源(赤。中央固定)
            p.fillColor = _pal.Warn;
            p.BeginPath();
            p.Arc(rect.center, 5f, 0f, 360f);
            p.Fill();

            // リスナー(橙 + 向きの線)
            var lp = ListenerPadMath.MetersToPixel(_listenerPos, rect, _rangeX, _rangeY);
            var rad = _angleDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * 18f;
            p.strokeColor = _pal.Dot;
            p.lineWidth = 3f;
            p.BeginPath();
            p.MoveTo(lp);
            p.LineTo(lp + dir);
            p.Stroke();
            p.fillColor = _pal.Halo;
            p.BeginPath();
            p.Arc(lp, _dragging ? 16f : 13f, 0f, 360f);
            p.Fill();
            p.fillColor = _pal.Dot;
            p.BeginPath();
            p.Arc(lp, 7f, 0f, 360f);
            p.Fill();
            p.strokeColor = _pal.Handle;
            p.lineWidth = 2f;
            p.BeginPath();
            p.Arc(lp, 7f, 0f, 360f);
            p.Stroke();
        }

        private void DrawEllipse(Painter2D p, Rect rect, float radiusMeters, Color color)
        {
            var rx = radiusMeters / _rangeX * (rect.width * 0.5f);
            var ry = radiusMeters / _rangeY * (rect.height * 0.5f);
            const int segments = 48;
            p.strokeColor = new Color(color.r, color.g, color.b, 0.8f);
            p.lineWidth = 1.5f;
            p.BeginPath();
            for (var i = 0; i <= segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var pt = new Vector2(rect.center.x + Mathf.Cos(a) * rx, rect.center.y + Mathf.Sin(a) * ry);
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

        // ── 操作 ──

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || !_valid)
            {
                return;
            }

            _dragging = true;
            _pad.CapturePointer(e.pointerId);
            MoveTo(e.localPosition);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_dragging && _pad.HasPointerCapture(e.pointerId))
            {
                MoveTo(e.localPosition);
            }
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

            _dragging = false;
            _pad.MarkDirtyRepaint();
        }

        private void MoveTo(Vector2 local)
        {
            var r = _pad.contentRect;
            var meters = ListenerPadMath.PixelToMeters(local, new Rect(0f, 0f, r.width, r.height), _rangeX, _rangeY);
            _listenerPos = ListenerPadMath.ClampToRange(meters, _rangeX, _rangeY);
            _pad.MarkDirtyRepaint();
            RefreshInfo();
            _applied?.Invoke(_listenerPos, _angleDeg);
        }
    }
}
