using DDrive.Editor.Ux;
using DDrive.Runtime.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // 「流れ（タイムライン）」の欄: ルーラー + レーン + マーカーのドラッグ(時刻変更。Undo 1 回にまとめる)+ 再生ヘッド + ズーム / パン / ミニスクロールバー + D&D 追加 + ルーラーのクリック / ドラッグでシーク。
    // 旧エディターのタイムライン(IMGUI)をそのまま IMGUIContainer で載せ、色だけ Light / Dark に合わせた。操作のヒントは UI Toolkit のラベル(折り返す)に出す。
    internal sealed partial class PresentationEditor
    {
        private const float RulerHeight = 34f;
        private const float LaneHeight = 20f;
        private const float MiniScrollbarGap = 4f;
        private const float MiniScrollbarHeight = 14f;
        private const float PlayheadFrameRate = 60f; // 表示専用(フレーム数表示。ランタイムの完了判定には無関係)

        private const string OperationHint =
            "上段クリック / ドラッグ: シーク ・ マーカーをドラッグ: 時刻変更 ・ Data をドラッグ&ドロップ: トラック追加 ・ Ctrl+ホイール: ズーム ・ ホイール: スクロール";

        private IMGUIContainer _timelineContainer;
        private Slider _zoomSlider;
        private int _draggingTrack = -1;
        private int _dragUndoGroup;
        private bool _seekDragging;
        private bool _scrollDragging;
        private float _scrollDragStartMouseX;
        private float _scrollDragStartViewStart;

        // タイムラインの表示範囲(秒)。PresentationData には保存しない。
        private float DisplayDuration => PresentationTimelineRange.DisplayDuration(Target);

        // ── 欄 ──

        private UxRow CreateTimelineRow(FieldGuideEntry e)
        {
            var host = new VisualElement();

            var controls = new VisualElement();
            controls.AddToClassList("ux-toolsline");
            controls.Add(MakeButton("－", null, () => ZoomBy(1f / 1.5f, (_viewStart + _viewEnd) * 0.5f), "ズームアウト(表示範囲の中心を基準)"));
            _zoomSlider = new Slider(1f, PresentationTimelineZoom.MaxZoomFactor) { value = 1f, showInputField = true, tooltip = "タイムラインのズーム倍率(1x = 全体表示)" };
            _zoomSlider.AddToClassList("ux-ptl__zoom");
            _zoomSlider.RegisterValueChangedCallback(evt => SetZoomFactor(evt.newValue, (_viewStart + _viewEnd) * 0.5f));
            controls.Add(_zoomSlider);
            controls.Add(MakeButton("＋", null, () => ZoomBy(1.5f, (_viewStart + _viewEnd) * 0.5f), "ズームイン(表示範囲の中心を基準)"));
            controls.Add(MakeButton("全体表示", null, ResetViewToFit, "演出の全体が幅に収まるようにズーム・スクロールをリセットする"));
            var follow = new Toggle("再生ヘッドに追従") { value = _followPlayhead, tooltip = "再生中に再生ヘッドが画面外に出ないよう、表示範囲を自動でスクロールする" };
            follow.AddToClassList("ux-bar__repeat");
            follow.RegisterValueChangedCallback(evt => _followPlayhead = evt.newValue);
            controls.Add(follow);
            host.Add(controls);

            _timelineContainer = new IMGUIContainer(DrawTimeline);
            _timelineContainer.AddToClassList("ux-ptl");
            host.Add(_timelineContainer);

            var hint = new Label(OperationHint);
            hint.AddToClassList("ux-dim");
            hint.AddToClassList("ux-note");
            host.Add(hint);

            RefreshZoomUi();
            return new PresentationAuxRow(e, e.Label, e.Hint, e.Tier, host, null);
        }

        private UxRow CreateDurationRow(FieldGuideEntry e)
        {
            var input = new PresentationDurationInput(So, Target, FitTotalDuration);
            return new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, input, RowChanged);
        }

        // 「トラックの最後に合わせる」: TotalDuration を各トラックの終了時刻(分かる場合はアセットの長さを加味、分からなければ最大時刻 + 0.5 秒)の最大値に設定する。
        private void FitTotalDuration()
        {
            if (Target == null)
            {
                return;
            }

            PresentationTrackEditOps.FitTotalDurationToTracks(Target);
            So?.Update();
            AfterDataWrite();
            ResetViewToFit();
        }

        // ── ズーム / パン ──

        // 対象アセットの切替時と、初回描画で表示範囲が未初期化(0,0)のときに呼ぶ。
        private void ResetViewToFit()
        {
            (_viewStart, _viewEnd) = PresentationTimelineZoom.Fit(DisplayDuration);
            RefreshZoomUi();
            _timelineContainer?.MarkDirtyRepaint();
        }

        private void SetView(float start, float end)
        {
            (_viewStart, _viewEnd) = PresentationTimelineZoom.ClampRange(start, end, DisplayDuration);
            RefreshZoomUi();
            _timelineContainer?.MarkDirtyRepaint();
        }

        private void SetView((float start, float end) range) => SetView(range.start, range.end);

        private void ZoomBy(float relativeFactor, float pivotTime) => SetView(PresentationTimelineZoom.ZoomAroundPivot(_viewStart, _viewEnd, pivotTime, relativeFactor, DisplayDuration));

        private void SetZoomFactor(float factor, float pivotTime) => SetView(PresentationTimelineZoom.WithZoomFactor(_viewStart, _viewEnd, pivotTime, factor, DisplayDuration));

        private void PanView(float deltaNotches)
        {
            var width = _viewEnd - _viewStart;
            SetView(PresentationTimelineZoom.Pan(_viewStart, _viewEnd, deltaNotches * width * 0.05f, DisplayDuration));
        }

        private void RefreshZoomUi()
            => _zoomSlider?.SetValueWithoutNotify(Mathf.Clamp(PresentationTimelineZoom.ZoomFactor(_viewStart, _viewEnd, DisplayDuration), 1f, PresentationTimelineZoom.MaxZoomFactor));

        // ── 描画(IMGUI) ──

        private readonly struct TimelineColors
        {
            public readonly Color Background;
            public readonly Color LaneAlt;
            public readonly Color Bar;
            public readonly Color Tick;
            public readonly Color TickStrong;
            public readonly Color Selected;
            public readonly Color Playhead;
            public readonly Color MiniBack;
            public readonly Color MiniTrack;
            public readonly Color MiniThumb;

            private readonly bool _dark;

            // レーンの色。Light では背景に負けないよう暗くする(HitStop の白は灰色に)。
            public Color MarkerColor(TrackKind kind)
            {
                var c = PresentationTrackKindMapping.LaneColor(kind);
                if (_dark)
                {
                    return c;
                }

                return kind == TrackKind.HitStop ? new Color(0.42f, 0.44f, 0.5f) : Color.Lerp(c, Color.black, 0.2f);
            }

            public TimelineColors(bool dark)
            {
                _dark = dark;
                Background = dark ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.93f, 0.94f, 0.96f);
                LaneAlt = dark ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.045f);
                Bar = dark ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.72f, 0.74f, 0.78f);
                Tick = dark ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.14f);
                TickStrong = dark ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.4f);
                Selected = dark ? new Color(0.45f, 0.75f, 1f) : new Color(0.1f, 0.12f, 0.18f);
                Playhead = dark ? new Color(1f, 0.85f, 0.15f) : new Color(0.85f, 0.42f, 0.02f);
                MiniBack = dark ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.86f, 0.88f, 0.91f);
                MiniTrack = dark ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.78f, 0.8f, 0.84f);
                MiniThumb = dark ? new Color(0.6f, 0.6f, 0.65f, 0.9f) : new Color(0.4f, 0.45f, 0.55f, 0.9f);
            }
        }

        private static GUIStyle _laneLabelStyle;

        private static GUIStyle LaneLabelStyle => _laneLabelStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperRight };

        private static float TimelineTotalHeight => RulerHeight + LaneHeight * Lanes.Length + MiniScrollbarGap + MiniScrollbarHeight;

        private void DrawTimeline()
        {
            var colors = new TimelineColors(EditorGUIUtility.isProSkin);
            var rect = GUILayoutUtility.GetRect(100, TimelineTotalHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, colors.Background);

            var evt = Event.current;
            var target = Target;
            if (target == null)
            {
                GUI.Label(rect, "対象アセットが未選択です", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var displayDuration = DisplayDuration;
            // 表示範囲(0,0)は未初期化の目印。それ以外は現在の尺に合わせてクランプするだけで、ユーザーのズーム / パンは保つ。
            if (_viewEnd <= _viewStart)
            {
                (_viewStart, _viewEnd) = PresentationTimelineZoom.Fit(displayDuration);
            }
            else
            {
                (_viewStart, _viewEnd) = PresentationTimelineZoom.ClampRange(_viewStart, _viewEnd, displayDuration);
            }

            var bar = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 6f);
            EditorGUI.DrawRect(bar, colors.Bar);
            DrawTimeRuler(bar, _viewStart, _viewEnd, colors);

            var playing = _preview != null && _preview.IsPlaying && !_auditioning;
            if (playing)
            {
                var runtimeDuration = PresentationTiming.EffectiveDuration(target);
                var elapsed = Mathf.Clamp01(_preview.NormalizedTime) * Mathf.Max(0f, runtimeDuration);
                var frame = Mathf.RoundToInt(elapsed * PlayheadFrameRate);
                GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 170f, 14f), $"⏱ {elapsed:0.00}s (Frame {frame})", EditorStyles.miniBoldLabel);
            }

            if (target.TotalDuration <= 0f)
            {
                GUI.Label(new Rect(rect.xMax - 132f, rect.y + 2f, 128f, 14f), "⚠ 尺が未設定です", EditorStyles.miniLabel);
            }

            // レーン背景 + ラベル
            for (var i = 0; i < Lanes.Length; i++)
            {
                var laneRect = new Rect(rect.x, rect.y + RulerHeight + i * LaneHeight, rect.width, LaneHeight);
                if (i % 2 == 0)
                {
                    EditorGUI.DrawRect(laneRect, colors.LaneAlt);
                }

                // レーン名は右寄せ(時刻 0 付近のマーカーと重ならないように)。
                GUI.Label(new Rect(laneRect.xMax - 188f, laneRect.y + 2f, 182f, LaneHeight - 2f), Lanes[i].label, LaneLabelStyle);
            }

            var tracks = target.Tracks;
            if (tracks != null)
            {
                for (var i = 0; i < tracks.Length; i++)
                {
                    var track = tracks[i];
                    if (track.Trigger != TrackTrigger.AtTime)
                    {
                        continue;
                    }

                    var laneY = rect.y + RulerHeight + LaneIndexFor(track.Kind) * LaneHeight;
                    var x = PresentationTimelineZoom.TimeToX(bar, _viewStart, _viewEnd, track.Time);
                    var marker = new Rect(x - 5f, laneY + 2f, 10f, LaneHeight - 4f);
                    EditorGUI.DrawRect(marker, i == _selectedTrack ? colors.Selected : colors.MarkerColor(track.Kind));

                    if (evt.type == EventType.MouseDown && marker.Contains(evt.mousePosition))
                    {
                        _draggingTrack = i;
                        _dragUndoGroup = Undo.GetCurrentGroup();
                        SelectTrack(i, true);
                        evt.Use();
                    }
                }
            }

            // 再生ヘッド(全レーンを貫く)。表示範囲の外に出ているときは描かない(下部のミニスクロールバーで位置は分かる)。
            if (playing)
            {
                var runtimeDuration = PresentationTiming.EffectiveDuration(target);
                var elapsed = Mathf.Clamp01(_preview.NormalizedTime) * Mathf.Max(0f, runtimeDuration);
                if (elapsed >= _viewStart - 1e-3f && elapsed <= _viewEnd + 1e-3f)
                {
                    var px = PresentationTimelineZoom.TimeToX(bar, _viewStart, _viewEnd, elapsed);
                    EditorGUI.DrawRect(new Rect(px - 1f, rect.y, 2f, RulerHeight + LaneHeight * Lanes.Length), colors.Playhead);
                }
            }

            DrawMiniScrollbar(new Rect(rect.x, rect.yMax - MiniScrollbarHeight, rect.width, MiniScrollbarHeight), displayDuration, evt, colors);

            switch (evt.type)
            {
                case EventType.MouseDrag when _draggingTrack >= 0 && tracks != null && _draggingTrack < tracks.Length:
                {
                    // ドラッグごとに記録し、MouseUp で 1 つにまとめる(1 回の Undo で元に戻る)。
                    var t = PresentationTimelineZoom.XToTime(bar, _viewStart, _viewEnd, evt.mousePosition.x);
                    PresentationTrackEditOps.SetTrackTime(target, _draggingTrack, t);
                    So?.Update();
                    RefreshTrackSummaries();
                    SceneView.RepaintAll();
                    evt.Use();
                    break;
                }
                case EventType.MouseDrag when _seekDragging:
                    SeekToTime(PresentationTimelineZoom.XToTime(bar, _viewStart, _viewEnd, evt.mousePosition.x));
                    evt.Use();
                    break;
                case EventType.MouseUp when _draggingTrack >= 0:
                    _draggingTrack = -1;
                    Undo.CollapseUndoOperations(_dragUndoGroup);
                    AfterDataWrite();
                    evt.Use();
                    break;
                case EventType.MouseUp when _seekDragging:
                    _seekDragging = false;
                    evt.Use();
                    break;
                case EventType.MouseDown when rect.Contains(evt.mousePosition) && evt.mousePosition.y < rect.y + RulerHeight:
                    // ルーラー部分のクリック / ドラッグでシーク(トラックのマーカーはレーン側にしかないので競合しない)。
                    _seekDragging = true;
                    SeekToTime(PresentationTimelineZoom.XToTime(bar, _viewStart, _viewEnd, evt.mousePosition.x));
                    evt.Use();
                    break;
                case EventType.ScrollWheel when rect.Contains(evt.mousePosition):
                {
                    // Ctrl(Win)/ Cmd(Mac)+ホイール: カーソル位置を中心にズーム。それ以外(単独 / Shift 併用)は横スクロール。
                    if (evt.control || evt.command)
                    {
                        var pivot = PresentationTimelineZoom.XToTime(bar, _viewStart, _viewEnd, evt.mousePosition.x);
                        ZoomBy(Mathf.Pow(1.12f, -evt.delta.y), pivot);
                    }
                    else
                    {
                        var notches = Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y) ? evt.delta.x : evt.delta.y;
                        PanView(notches);
                    }

                    evt.Use();
                    break;
                }
                case EventType.DragUpdated when rect.Contains(evt.mousePosition):
                    if (HasDraggableAssets())
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }

                    break;
                case EventType.DragPerform when rect.Contains(evt.mousePosition):
                {
                    var t = PresentationTimelineZoom.XToTime(bar, _viewStart, _viewEnd, evt.mousePosition.x);
                    if (AddTracksFromDrag(Mathf.Max(0f, t)))
                    {
                        DragAndDrop.AcceptDrag();
                    }

                    evt.Use();
                    break;
                }
            }
        }

        // 時刻ベースの目盛り(ズーム倍率に応じて 1s / 0.5s / 0.1s / 1 フレーム(1/60s)を自動選択)。
        private static void DrawTimeRuler(Rect bar, float viewStart, float viewEnd, TimelineColors colors)
        {
            var range = Mathf.Max(1e-4f, viewEnd - viewStart);
            var step = PresentationTimelineZoom.ChooseTickStep(range, bar.width);
            var pxPerTick = bar.width * (step / range);
            var labelStride = PresentationTimelineZoom.LabelStride(pxPerTick);

            var first = Mathf.Ceil(viewStart / step) * step;
            var index = Mathf.RoundToInt(first / step);
            for (var t = first; t <= viewEnd + step * 0.5f; t += step, index++)
            {
                var x = bar.x + bar.width * ((t - viewStart) / range);
                var labeled = index % labelStride == 0;
                EditorGUI.DrawRect(new Rect(x, bar.y - (labeled ? 4f : 2f), 1f, bar.height + (labeled ? 8f : 4f)), labeled ? colors.TickStrong : colors.Tick);
                if (labeled)
                {
                    GUI.Label(new Rect(x - 20f, bar.yMax + 6f, 40f, 12f), step < 0.2f ? $"{t:0.###}s" : $"{t:0.##}s", EditorStyles.centeredGreyMiniLabel);
                }
            }
        }

        // 下部の横スクロールバー(演出全体を縮小したミニマップ + 現在の表示範囲を示すつまみ)。つまみのドラッグ / 空いている場所のクリックでパンする。
        private void DrawMiniScrollbar(Rect stripRect, float displayDuration, Event evt, TimelineColors colors)
        {
            EditorGUI.DrawRect(stripRect, colors.MiniBack);
            var track = new Rect(stripRect.x + 8f, stripRect.y + 2f, stripRect.width - 16f, stripRect.height - 4f);
            EditorGUI.DrawRect(track, colors.MiniTrack);

            var totalWidth = Mathf.Max(displayDuration, PresentationTimelineZoom.MinVisibleRange);
            var thumbX0 = track.x + track.width * Mathf.Clamp01(_viewStart / totalWidth);
            var thumbX1 = track.x + track.width * Mathf.Clamp01(_viewEnd / totalWidth);
            var thumb = new Rect(thumbX0, track.y, Mathf.Max(4f, thumbX1 - thumbX0), track.height);
            EditorGUI.DrawRect(thumb, colors.MiniThumb);

            switch (evt.type)
            {
                case EventType.MouseDown when thumb.Contains(evt.mousePosition):
                    _scrollDragging = true;
                    _scrollDragStartMouseX = evt.mousePosition.x;
                    _scrollDragStartViewStart = _viewStart;
                    evt.Use();
                    break;
                case EventType.MouseDown when track.Contains(evt.mousePosition):
                {
                    var trackWidth = Mathf.Max(1e-4f, track.width);
                    var t = totalWidth * Mathf.Clamp01((evt.mousePosition.x - track.x) / trackWidth);
                    var width = _viewEnd - _viewStart;
                    SetView(t - width * 0.5f, t + width * 0.5f);
                    evt.Use();
                    break;
                }
                case EventType.MouseDrag when _scrollDragging:
                {
                    var trackWidth = Mathf.Max(1e-4f, track.width);
                    var deltaSeconds = (evt.mousePosition.x - _scrollDragStartMouseX) / trackWidth * totalWidth;
                    var width = _viewEnd - _viewStart;
                    SetView(_scrollDragStartViewStart + deltaSeconds, _scrollDragStartViewStart + deltaSeconds + width);
                    evt.Use();
                    break;
                }
                case EventType.MouseUp when _scrollDragging:
                    _scrollDragging = false;
                    evt.Use();
                    break;
            }
        }
    }
}
