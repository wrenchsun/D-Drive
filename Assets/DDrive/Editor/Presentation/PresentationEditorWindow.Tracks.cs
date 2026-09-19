using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4 の「トラック編集」— Kind ごとのレーンに AtTime トラックを D&D 配置・時間ドラッグ・
    // 削除・複製する(Undo 1 回)。ルーラーの目盛りは Presentation 専用(ズーム対応、PresentationTimelineZoom +
    // DrawTimeRuler)。AnimEditorWindow(3-3)と共用の TimelineRulerGui は 5-4 追補(2026-09-14、ズーム機能追加)
    // でも一切改修していない(Anim Editor の見た目・挙動を壊さない方針)。
    public sealed partial class PresentationEditorWindow
    {
        private const float RulerHeight = 34f;
        private const float LaneHeight = 20f;
        private const float MiniScrollbarGap = 4f;
        private const float MiniScrollbarHeight = 14f;

        // Presentation にはアニメーションのような固有フレームレートが無いため、フレーム数表示(要件3)と
        // 目盛りの最も細かい候補(1/60s、PresentationTimelineZoom)は 60fps を仮定する(表示専用。ランタイムの
        // 完了判定には無関係)。
        private const float PlayheadFrameRate = 60f;

        // タイムラインの操作ヒント([41] P2-9 / [09] §7.1)。狭いときは短縮版、全文は tooltip。
        private const string OperationHintFull =
            "上段クリック/ドラッグ: シーク / マーカーをドラッグ: 時刻変更 / Data を D&D: トラック追加 / Ctrl+ホイール: ズーム / ホイール: スクロール";

        private const string OperationHintShort = "上段: シーク / マーカー: 時刻変更 / D&D: 追加 …";

        // レーンをまとめる粒度(Kind 単位で 13 行あると縦に長くなりすぎるため、関連する Kind をまとめる)。
        private static readonly (TrackKind[] kinds, string label)[] Lanes =
        {
            (new[] { TrackKind.Anim, TrackKind.Anim2D }, "Anim / Anim2D"),
            (new[] { TrackKind.Se, TrackKind.Bgm }, "Se / Bgm"),
            // [22_anchor_group.md] §5(Presentation 統合、2026-09-19) — AnchorGroup(配置セット)は Vfx と同じ
            // レーンにまとめる(既存のレーン数・高さを変えない。どちらも「点/対象に VFX/SE を出す」トラックで
            // 見た目の役割が近いため)。
            (new[] { TrackKind.Vfx, TrackKind.AnchorGroup }, "Vfx / AnchorGroup"),
            (new[] { TrackKind.CameraShake, TrackKind.Haptic }, "CameraShake / Haptic"),
            // P5 レビュー対応(2026-09-14) 整理項目: TrackKind.Signal がどのレーンにも属していなかったため
            // LaneIndexFor のフォールバック(最後のレーン)に落ちていた。Marker(コード→データ通知)と対を成す
            // Signal(データ→コード通知。Trigger=AtTime で使う。OnSignal の一致キーとは別物)なので同じレーンにする。
            (new[] { TrackKind.HitStop, TrackKind.Marker, TrackKind.Signal }, "HitStop / Marker / Signal"),
            (new[] { TrackKind.Canvas, TrackKind.UiTween, TrackKind.Timeline }, "Canvas / UiTween / Timeline"),
        };

        // IMGUIContainer の style.height は固定値が必要なため、PresentationEditorWindow.cs の CreateGUI から参照する。
        // 5-4 追補(2026-09-14): 下部の横スクロールバー(ミニマップ)の分だけ高さを追加した。
        internal static float TimelineTotalHeight => RulerHeight + LaneHeight * Lanes.Length + MiniScrollbarGap + MiniScrollbarHeight;

        private int _selectedTrack = -1;
        private int _draggingTrack = -1;
        private int _dragUndoGroup;
        private EnumField _addKindField;

        // ルーラー上のクリック/ドラッグでシーク(要件4)。
        private bool _seekDragging;

        // 下部の横スクロールバー(ミニマップ)のドラッグ状態。
        private bool _scrollDragging;
        private float _scrollDragStartMouseX;
        private float _scrollDragStartViewStart;

        private Slider _zoomSlider;

        private static int LaneIndexFor(TrackKind kind)
        {
            for (var i = 0; i < Lanes.Length; i++)
            {
                foreach (var k in Lanes[i].kinds)
                {
                    if (k == kind)
                    {
                        return i;
                    }
                }
            }

            return Lanes.Length - 1;
        }

        // タイムラインの表示範囲(秒)。PresentationData には保存しない([08] 実装メモ、5-4 追補)。
        private float DisplayDuration => PresentationTimelineRange.DisplayDuration(_target);

        // ── ズーム/パン用のツールバー行 ──

        private void BuildTimelineControlsRow(VisualElement root)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 6 } };
            row.Add(new Button(() => ZoomBy(1f / 1.5f, (_viewStart + _viewEnd) * 0.5f)) { text = "－", tooltip = "ズームアウト(表示範囲の中心を基準)" });

            _zoomSlider = new Slider(1f, PresentationTimelineZoom.MaxZoomFactor) { value = 1f, showInputField = true, style = { flexGrow = 1f }, tooltip = "タイムラインのズーム倍率(1x = 全体表示)" };
            _zoomSlider.RegisterValueChangedCallback(evt => SetZoomFactor(evt.newValue, (_viewStart + _viewEnd) * 0.5f));
            row.Add(_zoomSlider);

            row.Add(new Button(() => ZoomBy(1.5f, (_viewStart + _viewEnd) * 0.5f)) { text = "＋", tooltip = "ズームイン(表示範囲の中心を基準)" });
            row.Add(new Button(ResetViewToFit) { text = "全体表示", tooltip = "演出の尺全体が幅に収まるようにズーム・スクロールをリセットします" });

            var followToggle = new Toggle("再生ヘッドに追従") { value = _followPlayhead, tooltip = "再生中に再生ヘッドが画面外に出ないよう、表示範囲を自動でスクロールします" };
            followToggle.RegisterValueChangedCallback(evt => _followPlayhead = evt.newValue);
            row.Add(followToggle);

            root.Add(row);
        }

        // 対象アセットの切替時(要件5)と、初回描画で表示範囲が未初期化(0,0)のときに呼ぶ。
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

        // PresentationTimelineZoom の各操作(ZoomAroundPivot/WithZoomFactor/Pan/FollowPlayhead)は
        // (start, end) のタプルを返すため、そのまま渡せるオーバーロード。
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

        // ── タイムライン(ルーラー + レーン + D&D + 時間ドラッグ + ズーム/パン/ミニスクロールバー) ──

        private void DrawTimeline()
        {
            var totalHeight = TimelineTotalHeight;
            var rect = GUILayoutUtility.GetRect(100, totalHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));

            var evt = Event.current;

            if (_target == null)
            {
                GUI.Label(rect, "対象アセットが未選択です", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var displayDuration = DisplayDuration;

            // 表示範囲(0,0)は未初期化の目印(5-4 追補)。それ以外は現在の尺に合わせてクランプするだけで、
            // ユーザーのズーム/パンはそのまま保つ(TotalDuration をタイプ中に変えても暴れないように毎フレーム行う)。
            if (_viewEnd <= _viewStart)
            {
                (_viewStart, _viewEnd) = PresentationTimelineZoom.Fit(displayDuration);
            }
            else
            {
                (_viewStart, _viewEnd) = PresentationTimelineZoom.ClampRange(_viewStart, _viewEnd, displayDuration);
            }

            var bar = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 6f);
            EditorGUI.DrawRect(bar, new Color(0.3f, 0.3f, 0.3f));

            DrawTimeRuler(bar, _viewStart, _viewEnd);

            // 上部の読み取り行: 再生中は現在時刻/フレーム(要件3)、TotalDuration 未設定なら小さく注記(追加要望)。
            if (_preview != null && _preview.IsPlaying)
            {
                var runtimeDuration = PresentationTiming.EffectiveDuration(_target);
                var normalized = Mathf.Clamp01(_preview.NormalizedTime);
                var elapsed = normalized * Mathf.Max(0f, runtimeDuration);
                var frame = Mathf.RoundToInt(elapsed * PlayheadFrameRate);
                GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 170f, 14f), $"⏱ {elapsed:0.00}s (Frame {frame})", EditorStyles.miniBoldLabel);
            }

            if (_target.TotalDuration <= 0f)
            {
                GUI.Label(new Rect(rect.xMax - 132f, rect.y + 2f, 128f, 14f), "⚠ 尺が未設定です", EditorStyles.miniLabel);
            }

            // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-9 / [09] §7.1):
            // 1 行の GUI.Label なので、ウィンドウ横幅 500px では後半(「Ctrl+ホイール: ズーム」以降)が
            // 切れて読めなかった。ルーラーの高さ(RulerHeight)は固定なので 2 行にはできないため、
            // 狭いときは短縮版を出し、全文は tooltip に逃がす([09] §7.1 の「ラベルが長い項目は
            // 短くするか tooltip に逃がす」)。
            GUI.Label(new Rect(rect.x + 6f, rect.y + RulerHeight - 14f, rect.width - 12f, 14f),
                new GUIContent(rect.width >= 620f ? OperationHintFull : OperationHintShort, OperationHintFull),
                EditorStyles.miniLabel);

            // レーン背景 + ラベル
            for (var i = 0; i < Lanes.Length; i++)
            {
                var laneRect = new Rect(rect.x, rect.y + RulerHeight + i * LaneHeight, rect.width, LaneHeight);
                if (i % 2 == 0)
                {
                    EditorGUI.DrawRect(laneRect, new Color(1f, 1f, 1f, 0.03f));
                }

                GUI.Label(new Rect(laneRect.x + 4f, laneRect.y + 2f, 180f, LaneHeight - 2f), Lanes[i].label, EditorStyles.miniLabel);
            }

            var tracks = _target.Tracks;
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
                    var color = PresentationTrackKindMapping.LaneColor(track.Kind);
                    EditorGUI.DrawRect(marker, i == _selectedTrack ? Color.white : color);

                    if (evt.type == EventType.MouseDown && marker.Contains(evt.mousePosition))
                    {
                        _selectedTrack = i;
                        _draggingTrack = i;
                        _dragUndoGroup = Undo.GetCurrentGroup();
                        RefreshTracksList();
                        evt.Use();
                    }
                }
            }

            // 再生ヘッド(全レーンを貫く目立つ色。要件3)。表示範囲の外に出ているときは描かない
            // (「再生ヘッドに追従」OFF でスクロールしていない場合。下部のミニスクロールバーで位置は分かる)。
            if (_preview != null && _preview.IsPlaying)
            {
                var runtimeDuration = PresentationTiming.EffectiveDuration(_target);
                var elapsed = Mathf.Clamp01(_preview.NormalizedTime) * Mathf.Max(0f, runtimeDuration);
                if (elapsed >= _viewStart - 1e-3f && elapsed <= _viewEnd + 1e-3f)
                {
                    var px = PresentationTimelineZoom.TimeToX(bar, _viewStart, _viewEnd, elapsed);
                    EditorGUI.DrawRect(new Rect(px - 1f, rect.y, 2f, RulerHeight + LaneHeight * Lanes.Length), new Color(1f, 0.85f, 0.15f));
                }
            }

            var scrollbarRect = new Rect(rect.x, rect.yMax - MiniScrollbarHeight, rect.width, MiniScrollbarHeight);
            DrawMiniScrollbar(scrollbarRect, displayDuration, evt);

            switch (evt.type)
            {
                case EventType.MouseDrag when _draggingTrack >= 0 && tracks != null && _draggingTrack < tracks.Length:
                {
                    // MouseDown だけの RecordObject は変更前に記録が終わって Undo が効かないため、
                    // ドラッグごとに記録し MouseUp で 1 つにまとめる(AnimEditorWindow.DrawTimeline と同じ手法)。
                    var t = PresentationTimelineZoom.XToTime(bar, _viewStart, _viewEnd, evt.mousePosition.x);
                    PresentationTrackEditOps.SetTrackTime(_target, _draggingTrack, t);
                    _serializedTarget?.Update();
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
                    RefreshValidation();
                    RefreshTracksList();
                    evt.Use();
                    break;
                case EventType.MouseUp when _seekDragging:
                    _seekDragging = false;
                    evt.Use();
                    break;
                case EventType.MouseDown when rect.Contains(evt.mousePosition) && evt.mousePosition.y < rect.y + RulerHeight:
                {
                    // ルーラー部分のクリック/ドラッグでシーク(要件4)。トラックマーカーはレーン側(y >= RulerHeight)
                    // にしかないため、ここで競合しない。
                    _seekDragging = true;
                    SeekToTime(PresentationTimelineZoom.XToTime(bar, _viewStart, _viewEnd, evt.mousePosition.x));
                    evt.Use();
                    break;
                }
                case EventType.ScrollWheel when rect.Contains(evt.mousePosition):
                {
                    // Ctrl(Win)/Cmd(Mac)+ホイール: カーソル位置を中心にズーム。それ以外(単独 / Shift 併用)は
                    // 横スクロール(要件1(a)(c))。
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

        // 時刻ベースの目盛り(ズーム倍率に応じて 1s/0.5s/0.1s/1フレーム(1/60s)を自動選択、要件2)。
        // AnimEditorWindow が使う TimelineRulerGui(フレーム数固定の目盛り)とは独立させ、Anim 側の見た目・
        // 挙動には一切触れないようにした([08] 実装メモ参照)。
        private static void DrawTimeRuler(Rect bar, float viewStart, float viewEnd)
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
                EditorGUI.DrawRect(new Rect(x, bar.y - (labeled ? 4f : 2f), 1f, bar.height + (labeled ? 8f : 4f)), new Color(1f, 1f, 1f, labeled ? 0.35f : 0.12f));
                if (labeled)
                {
                    GUI.Label(new Rect(x - 20f, bar.yMax + 22f, 40f, 12f), step < 0.2f ? $"{t:0.###}s" : $"{t:0.##}s", EditorStyles.centeredGreyMiniLabel);
                }
            }
        }

        // 下部の横スクロールバー(演出全体を縮小したミニマップ + 現在の表示範囲を示すつまみ)。
        // つまみのドラッグ/空いている場所のクリックでパンする(要件1(c))。
        private void DrawMiniScrollbar(Rect stripRect, float displayDuration, Event evt)
        {
            EditorGUI.DrawRect(stripRect, new Color(0.1f, 0.1f, 0.1f));
            var track = new Rect(stripRect.x + 8f, stripRect.y + 2f, stripRect.width - 16f, stripRect.height - 4f);
            EditorGUI.DrawRect(track, new Color(0.25f, 0.25f, 0.25f));

            var totalWidth = Mathf.Max(displayDuration, PresentationTimelineZoom.MinVisibleRange);
            var thumbX0 = track.x + track.width * Mathf.Clamp01(_viewStart / totalWidth);
            var thumbX1 = track.x + track.width * Mathf.Clamp01(_viewEnd / totalWidth);
            var thumb = new Rect(thumbX0, track.y, Mathf.Max(4f, thumbX1 - thumbX0), track.height);
            EditorGUI.DrawRect(thumb, new Color(0.6f, 0.6f, 0.65f, 0.9f));

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
                    // つまみの外(空いている場所)をクリック: そこが中心になるようにスクロールする。
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

        private static bool HasDraggableAssets()
        {
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is AssetDataBase asset && PresentationTrackKindMapping.TryKindFor(asset, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AddTracksFromDrag(float time)
        {
            var added = false;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is not AssetDataBase asset || !PresentationTrackKindMapping.TryKindFor(asset, out var kind))
                {
                    continue;
                }

                AddTrack(kind, time, asset);
                added = true;
            }

            return added;
        }

        // ── 追加行 ──

        private void BuildAddTrackRow(VisualElement root)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 6, marginBottom = 4 } };
            _addKindField = new EnumField("追加する Kind", TrackKind.Vfx);
            row.Add(_addKindField);
            row.Add(new Button(() => AddTrack((TrackKind)_addKindField.value, 0f, null)) { text = "＋ トラックを追加" });
            root.Add(row);
        }

        private void AddTrack(TrackKind kind, float time, AssetDataBase asset)
        {
            if (_target == null)
            {
                return;
            }

            _selectedTrack = PresentationTrackEditOps.AddTrack(_target, kind, time, asset);
            _serializedTarget?.Update();
            RefreshTracksList();
            RefreshValidation();
            _timelineContainer?.MarkDirtyRepaint();
        }

        private void DuplicateTrack(int index)
        {
            if (_target == null)
            {
                return;
            }

            PresentationTrackEditOps.DuplicateTrack(_target, index);
            _serializedTarget?.Update();
            _selectedTrack = index + 1;
            RefreshTracksList();
            RefreshValidation();
            _timelineContainer?.MarkDirtyRepaint();
        }

        private void RemoveTrack(int index)
        {
            if (_target == null)
            {
                return;
            }

            PresentationTrackEditOps.RemoveTrack(_target, index);
            _serializedTarget?.Update();
            _selectedTrack = -1;
            RefreshTracksList();
            RefreshValidation();
            _timelineContainer?.MarkDirtyRepaint();
        }

        // ── トラック一覧(全項目編集 + 複製 / 削除) ──

        private void RefreshTracksList()
        {
            if (_tracksListContainer == null)
            {
                return;
            }

            _tracksListContainer.Clear();
            RefreshSignalButtons();

            if (_target?.Tracks == null || _target.Tracks.Length == 0)
            {
                _tracksListContainer.Add(new Label("トラックがありません。上の「＋ トラックを追加」か、タイムラインへ Data を D&D してください。") { style = { opacity = 0.6f } });
                return;
            }

            for (var i = 0; i < _target.Tracks.Length; i++)
            {
                _tracksListContainer.Add(BuildTrackRow(i));
            }
        }

        private VisualElement BuildTrackRow(int index)
        {
            var track = _target.Tracks[index];
            var foldout = new Foldout { value = index == _selectedTrack };
            foldout.text = TrackFoldoutTitle(index);
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    _selectedTrack = index;
                }
            });

            var prop = _tracksProp?.GetArrayElementAtIndex(index);
            if (prop == null)
            {
                foldout.Add(new Label("(内部エラー: SerializedProperty が見つかりません)"));
                return foldout;
            }

            // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照) — RefreshAfterEdit
            // (下記、Trigger/Time/SignalKey 等のコールバックから呼ばれうる)が anchorFoldout(後で構築)を
            // 参照する RefreshAnchorCaseLabel を呼ぶため、先に null で宣言しておく(CS0165 対策。
            // RefreshAnchorCaseLabel 側で null チェックする)。
            Foldout anchorFoldout = null;

            void RefreshAfterEdit()
            {
                EditorUtility.SetDirty(_target);
                RefreshValidation();
                RefreshSignalButtons();
                foldout.text = TrackFoldoutTitle(index);
                RefreshAnchorCaseLabel();
                _timelineContainer?.MarkDirtyRepaint();
            }

            // RefreshAnchorCaseLabel は anchorFoldout(後述)を参照するため、その宣言より後ろに定義する
            // (ローカル関数どうしの前方参照〔RefreshAfterEdit → RefreshAnchorCaseLabel〕は可能だが、
            // ローカル変数 anchorFoldout 自体は宣言前に参照できないため CS0841 になる)。

            void AddField(string name, string label = null)
            {
                var p = prop.FindPropertyRelative(name);
                if (p == null)
                {
                    return;
                }

                var field = new PropertyField(p, label);
                field.Bind(_serializedTarget);
                field.RegisterCallback<SerializedPropertyChangeEvent>(_ => RefreshAfterEdit());
                foldout.Add(field);
            }

            AddField("Trigger");
            AddField("Time");
            AddField("SignalKey");

            // P5 レビュー対応(2026-09-14): Kind 変更は他フィールドと違い、以下 2 点の追従が必要なため
            // 汎用の AddField(RefreshAfterEdit だけを呼ぶ)ではなく専用のコールバックにする。
            //   - Asset の ObjectField.objectType は BuildTrackRow 実行時の Kind で固定されるため、
            //     Kind を変えても古い型のまま(かつ AssetRef.Type も古いまま)残ってしまう
            //     → Asset を Undo 付きでクリアし、行を再構築(RefreshTracksList)して objectType を
            //        新しい Kind に合わせ直す。
            var kindProp = prop.FindPropertyRelative("Kind");
            if (kindProp != null)
            {
                var kindField = new PropertyField(kindProp, "Kind");
                kindField.Bind(_serializedTarget);
                // 2026-09-14 修正: PropertyField は Bind 直後にも SerializedPropertyChangeEvent を送るため、
                // 値が変わっていなくても「Asset クリア + 行の再構築」が走り、再構築 → 再 Bind → 再通知の
                // 無限ループ(表示が定期的に切り替わって荒ぶる + Asset が毎回消える)になっていた。
                // 行を作った時点の Kind と比べ、実際に変わったときだけ処理する。
                var builtKind = track.Kind;
                kindField.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
                {
                    if ((TrackKind)evt.changedProperty.intValue == builtKind)
                    {
                        return;
                    }

                    Undo.RecordObject(_target, "Change Presentation Track Kind");
                    var t = _target.Tracks[index];
                    t.Asset = default;
                    _target.Tracks[index] = t;
                    EditorUtility.SetDirty(_target);
                    _serializedTarget?.Update();
                    RefreshValidation();
                    RefreshSignalButtons();
                    _timelineContainer?.MarkDirtyRepaint();
                    // objectType が古い Kind のまま残らないよう行ごと再構築する。自分を含む行をイベント処理中に
                    // 破棄しないよう、次のフレームへ回す。
                    rootVisualElement.schedule.Execute(RefreshTracksList);
                });
                foldout.Add(kindField);
            }

            var assetType = PresentationTrackKindMapping.AssetTypeFor(track.Kind);
            if (assetType != null)
            {
                var current = PresentationTrackKindMapping.FindAssetById(assetType, track.Asset.Id);
                var assetField = new ObjectField("Asset") { objectType = assetType };
                assetField.SetValueWithoutNotify(current);
                assetField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_target, "Set Presentation Track Asset");
                    var t = _target.Tracks[index];
                    var picked = evt.newValue as AssetDataBase;
                    t.Asset = picked != null ? new AssetRef { Type = PresentationTrackKindMapping.AssetKindFor(t.Kind), Id = picked.Id } : default;
                    _target.Tracks[index] = t;
                    _serializedTarget?.Update();
                    RefreshAfterEdit();
                });
                foldout.Add(assetField);

                // [08_presentation.md] 指摘3/4(2026-09-20) — 「専用エディタで開く」導線(一緒に調整 / 単体)。
                var editorRow = BuildTrackEditorOpenRow(index, track.Kind);
                if (editorRow != null)
                {
                    foldout.Add(editorRow);
                }
            }

            AddField("Target");

            anchorFoldout = new Foldout { text = "Anchor(VFX/SE の位置)", value = false };
            var anchorProp = prop.FindPropertyRelative("Anchor");
            if (anchorProp != null)
            {
                var anchorField = new PropertyField(anchorProp);
                anchorField.Bind(_serializedTarget);
                anchorField.RegisterCallback<SerializedPropertyChangeEvent>(_ => RefreshAfterEdit());
                anchorFoldout.Add(anchorField);
            }

            foldout.Add(anchorFoldout);

            // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照) — 今どのケース
            // (アセット側のみ / トラックのみ / 両方=親子合成 / 未設定)かを Anchor 欄の見出しに 1 行で表示する。
            // Vfx/Se トラックだけが対象(AnchorGroup は自分の点を持つため対象外、他 Kind は位置を消費しない)。
            void RefreshAnchorCaseLabel()
            {
                if (anchorFoldout == null)
                {
                    return;
                }

                var current = _target.Tracks[index];
                if (current.Kind != TrackKind.Vfx && current.Kind != TrackKind.Se)
                {
                    anchorFoldout.text = "Anchor(VFX/SE の位置)";
                    return;
                }

                var currentAssetType = PresentationTrackKindMapping.AssetTypeFor(current.Kind);
                var asset = currentAssetType != null && current.Asset.IsAssigned
                    ? PresentationTrackKindMapping.FindAssetById(currentAssetType, current.Asset.Id)
                    : null;
                PresentationTrackAnchorComposer.TryGetAssetAnchor(asset, out var assetAnchorId, out var assetEmbedded);
                var kase = PresentationTrackAnchorComposer.DetermineCase(in current, assetAnchorId, in assetEmbedded);
                anchorFoldout.text = $"Anchor(VFX/SE の位置) — {DescribeAnchorCase(kase)}";
            }

            RefreshAnchorCaseLabel();

            var paramsFoldout = new Foldout { text = "Params(パラメータ上書き)", value = false };

            // P5 レビュー対応(2026-09-14) 5-4 追補(a): Params[i] は参照先 VfxData.Params[i].Label に
            // インデックス対応で渡される(PresentationManager.ApplyVfxTrackParams。[08] 実装メモ参照)。
            // デザイナーが対応関係を見て分かるよう、Vfx トラックのときだけ Label 一覧をヒント表示する
            // (PresentationTrack にラベル用フィールドを増やさない = シリアライズ追加を避けるため)。
            if (track.Kind == TrackKind.Vfx)
            {
                paramsFoldout.Add(BuildVfxParamsHintLabel(track));
            }

            var paramsProp = prop.FindPropertyRelative("Params");
            if (paramsProp != null)
            {
                var paramsField = new PropertyField(paramsProp);
                paramsField.Bind(_serializedTarget);
                paramsField.RegisterCallback<SerializedPropertyChangeEvent>(_ => RefreshAfterEdit());
                paramsFoldout.Add(paramsField);
            }

            foldout.Add(paramsFoldout);

            AddField("StopOnCancel");

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 6 } };
            buttonRow.Add(new Button(() => DuplicateTrack(index)) { text = "複製" });
            buttonRow.Add(new Button(() => RemoveTrack(index)) { text = "削除" });
            foldout.Add(buttonRow);

            return foldout;
        }

        // P5 レビュー対応(2026-09-14) 5-4 追補(a): 「Params[i] ↔ 参照先 VfxData.Params[i].Label」の
        // インデックス対応をデザイナーに見せるためのヒント文言。
        private static Label BuildVfxParamsHintLabel(PresentationTrack track)
        {
            var vfxAsset = PresentationTrackKindMapping.FindAssetById(typeof(VfxData), track.Asset.Id) as VfxData;
            if (vfxAsset == null || vfxAsset.Params == null || vfxAsset.Params.Length == 0)
            {
                return new Label("(参照先 VFX に Params が未設定のため、ここへ追加しても反映されません)")
                {
                    style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal },
                };
            }

            var labels = new string[vfxAsset.Params.Length];
            for (var i = 0; i < vfxAsset.Params.Length; i++)
            {
                var label = vfxAsset.Params[i].Label;
                labels[i] = $"[{i}]={(string.IsNullOrEmpty(label) ? "(無名)" : label)}";
            }

            return new Label($"インデックス対応(この下の要素は上から順に参照先 VFX の Params と対応): {string.Join(", ", labels)}")
            {
                style = { opacity = 0.8f, whiteSpace = WhiteSpace.Normal },
            };
        }

        private string TrackFoldoutTitle(int index)
        {
            var track = _target.Tracks[index];
            var when = track.Trigger == TrackTrigger.OnSignal
                ? $"onSignal:{track.SignalKey}"
                : $"t={track.Time:0.##}s";
            var assetName = string.Empty;
            var assetType = PresentationTrackKindMapping.AssetTypeFor(track.Kind);
            if (assetType != null && track.Asset.IsAssigned)
            {
                var asset = PresentationTrackKindMapping.FindAssetById(assetType, track.Asset.Id);
                if (asset != null)
                {
                    assetName = $" {asset.DisplayName ?? asset.name}";
                }
            }

            return $"[{index}] {track.Kind}{assetName} ({when})";
        }

        // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照) — Anchor 欄の見出しに
        // 出す 1 行(現在どのケースか)。
        private static string DescribeAnchorCase(PresentationTrackAnchorComposer.Case kase) => kase switch
        {
            PresentationTrackAnchorComposer.Case.AssetOnly => "現在: アセット側の Anchor を使用",
            PresentationTrackAnchorComposer.Case.TrackOnly => "現在: トラックの Anchor を使用",
            PresentationTrackAnchorComposer.Case.Both => "現在: 両方設定されているため親子合成(トラックが親)",
            _ => "現在: 未設定(ワールド原点)",
        };
    }
}
