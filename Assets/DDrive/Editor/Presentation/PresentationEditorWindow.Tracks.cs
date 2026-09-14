using System;
using System.Collections.Generic;
using DDrive.Editor.Common;
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
    // 削除・複製する(Undo 1 回)。目盛りの描画は AnimEditorWindow(3-3)と共通の TimelineRulerGui を使う。
    public sealed partial class PresentationEditorWindow
    {
        private const float RulerHeight = 34f;
        private const float LaneHeight = 20f;

        // レーンをまとめる粒度(Kind 単位で 13 行あると縦に長くなりすぎるため、関連する Kind をまとめる)。
        private static readonly (TrackKind[] kinds, string label)[] Lanes =
        {
            (new[] { TrackKind.Anim, TrackKind.Anim2D }, "Anim / Anim2D"),
            (new[] { TrackKind.Se, TrackKind.Bgm }, "Se / Bgm"),
            (new[] { TrackKind.Vfx }, "Vfx"),
            (new[] { TrackKind.CameraShake, TrackKind.Haptic }, "CameraShake / Haptic"),
            // P5 レビュー対応(2026-09-14) 整理項目: TrackKind.Signal がどのレーンにも属していなかったため
            // LaneIndexFor のフォールバック(最後のレーン)に落ちていた。Marker(コード→データ通知)と対を成す
            // Signal(データ→コード通知。Trigger=AtTime で使う。OnSignal の一致キーとは別物)なので同じレーンにする。
            (new[] { TrackKind.HitStop, TrackKind.Marker, TrackKind.Signal }, "HitStop / Marker / Signal"),
            (new[] { TrackKind.Canvas, TrackKind.UiTween, TrackKind.Timeline }, "Canvas / UiTween / Timeline"),
        };

        // IMGUIContainer の style.height は固定値が必要なため、PresentationEditorWindow.cs の CreateGUI から参照する。
        internal static float TimelineTotalHeight => RulerHeight + LaneHeight * Lanes.Length;

        private int _selectedTrack = -1;
        private int _draggingTrack = -1;
        private int _dragUndoGroup;
        private EnumField _addKindField;

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

        // ── タイムライン(ルーラー + レーン + D&D + 時間ドラッグ) ──

        private void DrawTimeline()
        {
            var totalHeight = RulerHeight + LaneHeight * Lanes.Length;
            var rect = GUILayoutUtility.GetRect(100, totalHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));

            var evt = Event.current;

            if (_target == null)
            {
                GUI.Label(rect, "対象アセットが未選択です", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var duration = Mathf.Max(0.01f, PresentationTiming.EffectiveDuration(_target));
            var bar = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 6f);
            EditorGUI.DrawRect(bar, new Color(0.3f, 0.3f, 0.3f));

            // ルーラー: 10 分割 + 秒表示(AnimEditorWindow と共通の TimelineRulerGui、幅に応じたラベル間引き)。
            TimelineRulerGui.DrawTicks(bar, 10, i => $"{i / 10f * duration:0.##}s");

            GUI.Label(new Rect(rect.x + 6f, rect.y + RulerHeight - 14f, rect.width - 12f, 14f),
                "上段クリック: シーク / マーカーをドラッグ: 時刻変更 / Data を D&D: トラック追加",
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
                    var x = bar.x + bar.width * Mathf.Clamp01(track.Time / duration);
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

            // 再生ヘッド
            if (_preview != null && _preview.IsPlaying)
            {
                var normalized = _preview.NormalizedTime;
                if (normalized >= 0f)
                {
                    var px = bar.x + bar.width * normalized;
                    EditorGUI.DrawRect(new Rect(px - 1f, rect.y, 2f, rect.height), Color.white);
                }
            }

            switch (evt.type)
            {
                case EventType.MouseDrag when _draggingTrack >= 0 && tracks != null && _draggingTrack < tracks.Length:
                {
                    // MouseDown だけの RecordObject は変更前に記録が終わって Undo が効かないため、
                    // ドラッグごとに記録し MouseUp で 1 つにまとめる(AnimEditorWindow.DrawTimeline と同じ手法)。
                    var t = Mathf.Clamp01((evt.mousePosition.x - bar.x) / bar.width) * duration;
                    PresentationTrackEditOps.SetTrackTime(_target, _draggingTrack, t);
                    _serializedTarget?.Update();
                    evt.Use();
                    break;
                }
                case EventType.MouseUp when _draggingTrack >= 0:
                    _draggingTrack = -1;
                    Undo.CollapseUndoOperations(_dragUndoGroup);
                    RefreshValidation();
                    RefreshTracksList();
                    evt.Use();
                    break;
                case EventType.MouseDown when rect.Contains(evt.mousePosition) && evt.mousePosition.y < rect.y + RulerHeight:
                {
                    var t = Mathf.Clamp01((evt.mousePosition.x - bar.x) / bar.width);
                    _seekSlider?.SetValueWithoutNotify(t);
                    _preview?.Seek(t * duration);
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
                    var t = Mathf.Clamp01((evt.mousePosition.x - bar.x) / bar.width) * duration;
                    if (AddTracksFromDrag(t))
                    {
                        DragAndDrop.AcceptDrag();
                    }

                    evt.Use();
                    break;
                }
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

            void RefreshAfterEdit()
            {
                EditorUtility.SetDirty(_target);
                RefreshValidation();
                RefreshSignalButtons();
                foldout.text = TrackFoldoutTitle(index);
                _timelineContainer?.MarkDirtyRepaint();
            }

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
                var current = FindAssetById(assetType, track.Asset.Id);
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
            }

            AddField("Target");

            var anchorFoldout = new Foldout { text = "Anchor(VFX/SE の位置)", value = false };
            var anchorProp = prop.FindPropertyRelative("Anchor");
            if (anchorProp != null)
            {
                var anchorField = new PropertyField(anchorProp);
                anchorField.Bind(_serializedTarget);
                anchorField.RegisterCallback<SerializedPropertyChangeEvent>(_ => RefreshAfterEdit());
                anchorFoldout.Add(anchorField);
            }

            foldout.Add(anchorFoldout);

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
            var vfxAsset = FindAssetById(typeof(VfxData), track.Asset.Id) as VfxData;
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
                var asset = FindAssetById(assetType, track.Asset.Id);
                if (asset != null)
                {
                    assetName = $" {asset.DisplayName ?? asset.name}";
                }
            }

            return $"[{index}] {track.Kind}{assetName} ({when})";
        }

        private static AssetDataBase FindAssetById(Type dataType, ulong id)
        {
            if (id == 0 || dataType == null)
            {
                return null;
            }

            foreach (var guid in AssetSearch.FindAssets("t:" + dataType.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), dataType) as AssetDataBase;
                if (asset != null && asset.Id == id)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
