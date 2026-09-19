using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Presentation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] 「SceneView に Anchor を表示」— トラック(Vfx/Se)の Anchor(出す位置)を
    // SceneView に表示・編集する(ユーザー要望 2026-09-19)。見た目・ハンドルの流儀は VFX Editor /
    // Anchor Editor と同じ Editor/Preview/AnchorSceneHandles.cs を通す(コピペしない、ADR-4 と同じ発想)。
    //
    // 「どのトラックが今どこに出るか」の解決自体は PresentationTrackAnchorResolver(ウィンドウ非依存の
    // 純関数、EditMode テスト対象)に切り出してある。実効 Anchor は常にトラック自身の Anchor(埋め込み
    // AnchorDef)のみ — 参照先 VfxData/SeData の AnchorId/埋め込み Anchor は Presentation 経由では
    // 使われない(PresentationTrackAnchorResolver のコメント参照)。したがって編集の書き戻し先も常に
    // 「このトラックの PresentationTrack.Anchor」であり、共有アセット(AnchorData 等)を書き換えることはない。
    //
    // AnchorGroup(配置セット、2026-09-19、[22_anchor_group.md] §5 で予告されていた Presentation 統合)は
    // Vfx/Se と解決方法が異なる: track.Anchor(単一の AnchorDef)ではなく、参照先 AnchorGroupData の原点 +
    // 全点(AnchorGroupPlanner.EnumeratePoints、AnchorGroupEditorWindow と同じ見た目)を番号付きで描く。
    // 点の編集(移動)は Anchor Group Editor に任せ、ここでは表示のみ(ハンドルを出さない)。
    public sealed partial class PresentationEditorWindow
    {
        // 表示対象: すべての位置持ちトラックをまとめて見せるか、トラック一覧で選択中の 1 本だけに絞るか。
        // トラック一覧の「選択中」(_selectedTrack。タイムラインのマーカークリック/行の展開と共有)を
        // そのまま流用する — 表示専用の別選択状態を増やすとトラック一覧の選択とズレるため。
        private enum SceneAnchorDisplayMode
        {
            All,
            SelectedOnly,
        }

        // Vfx/Se(位置を持つ Kind)ごとの表示色。VFX Editor の埋め込み Anchor 表示(teal 系)・
        // Anchor Editor(黄)・Anchor Group Editor(水色)のいずれとも衝突しない配色にする。
        private static readonly Color SceneAnchorVfxColor = new(0.9f, 0.4f, 0.85f);   // マゼンタ
        private static readonly Color SceneAnchorSeColor = new(0.3f, 0.85f, 0.95f);   // シアン

        // AnchorGroup(配置セット)専用。上記いずれとも、Cutscene の AnchorGroup トラック色(黄緑系
        // (0.6, 0.8, 0.4)、CutsceneAnchorGroupClip)とも大きくは衝突しない黄緑にした。
        private static readonly Color SceneAnchorGroupColor = new(0.6f, 0.85f, 0.3f);

        // AnchorGroup の点バッファ(AnchorGroupData.MaxPoints 分を使い回す。AnchorGroupEditorWindow と同じ)。
        private readonly AnchorSpawnSpec[] _sceneAnchorGroupPoints = new AnchorSpawnSpec[AnchorGroupData.MaxPoints];

        [SerializeField] private bool _sceneAnchorEnabled = true;
        [SerializeField] private bool _sceneAnchorShowAll = true;

        private Label _sceneAnchorOwnerLabel;

        // ── UI ──

        private void BuildSceneAnchorSection(VisualElement root)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 4 } };

            var sceneToggle = new Toggle("SceneView 表示")
            {
                value = _sceneAnchorEnabled,
                tooltip = "OFF: Vfx/Se トラックの Anchor(出す位置)を SceneView に一切描かない。ON: 目印を描き、" +
                    "このウィンドウを最後に操作していれば選択中のトラックに移動/回転ハンドルも出す(回転ツール選択時は回転)",
            };
            sceneToggle.RegisterValueChangedCallback(evt =>
            {
                _sceneAnchorEnabled = evt.newValue;
                RefreshSceneAnchorOwnerLabel();
                SceneView.RepaintAll();
            });
            row.Add(sceneToggle);

            var modeField = new EnumField("表示対象", _sceneAnchorShowAll ? SceneAnchorDisplayMode.All : SceneAnchorDisplayMode.SelectedOnly)
            {
                tooltip = "すべて: 位置を持つ全トラック(Vfx/Se)を番号付きの点で表示。選択中のみ: トラック一覧で選択中の" +
                    "1 本だけに絞る(トラックが多いときに見やすくする)。どちらでも、ハンドルで動かせるのは選択中の 1 本だけ",
            };
            modeField.RegisterValueChangedCallback(evt =>
            {
                _sceneAnchorShowAll = (SceneAnchorDisplayMode)evt.newValue == SceneAnchorDisplayMode.All;
                SceneView.RepaintAll();
            });
            row.Add(modeField);
            root.Add(row);

            root.Add(new Label("位置を持つのは Vfx / Se / AnchorGroup トラックのみです(Anim/CameraShake/Haptic 等は対象外)。" +
                "AnchorGroup は全点を表示のみ(点の編集は Anchor Group Editor で行います)。")
            {
                style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal, marginLeft = 4 },
            });

            _sceneAnchorOwnerLabel = new Label { style = { opacity = 0.65f, marginLeft = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_sceneAnchorOwnerLabel);
            RefreshSceneAnchorOwnerLabel();
        }

        private void RefreshSceneAnchorOwnerLabel()
        {
            if (_sceneAnchorOwnerLabel == null)
            {
                return;
            }

            _sceneAnchorOwnerLabel.text = _sceneAnchorEnabled ? SceneGuiOwner.DescribeFor(this) : "SceneView: 表示オフ";
        }

        // ── SceneView 描画権(VFX Editor / Anchor Editor と同じ SceneGuiOwner) ──

        private void OnFocus()
        {
            SceneGuiOwner.Claim(this);
            RefreshSceneAnchorOwnerLabel();
        }

        private void OnLostFocus() => RefreshSceneAnchorOwnerLabel();

        // ── SceneView 描画 ──

        private void OnSceneGui(SceneView sceneView)
        {
            if (!_sceneAnchorEnabled || _target?.Tracks == null || _preview == null)
            {
                return;
            }

            var tracks = _target.Tracks;
            var self = _preview.SelfRoot;
            // 統合プレビューは常に ctx.Target=null で再生する(ScenePresentationPreviewDriver.Play)。
            // Target=ContextTarget のトラックはプレビューでは常にワールド原点扱いになる(実際の挙動どおり)。
            Transform target = null;

            if (!SceneGuiOwner.IsOwner(this))
            {
                DrawInactiveSceneAnchors(tracks, self, target);
                return;
            }

            for (var i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                if (!PresentationTrackAnchorResolver.HasPosition(track.Kind))
                {
                    continue;
                }

                var isSelected = i == _selectedTrack;
                if (!_sceneAnchorShowAll && !isSelected)
                {
                    continue;
                }

                if (track.Kind == TrackKind.AnchorGroup)
                {
                    DrawAnchorGroupPoints(i, in track, self, target, isSelected, interactive: true);
                    continue;
                }

                var resolved = PresentationTrackAnchorResolver.Resolve(in track, self, target);
                var color = SceneAnchorColorFor(track.Kind);
                var label = SceneAnchorLabel(i, in track);

                if (isSelected)
                {
                    var originWorld = AnchorSceneHandles.DrawOrigin(resolved.BaseTransform, resolved.ExtraOffset, AnchorSceneHandles.DescribeBase(track.Anchor, resolved.BaseTransform), track.Anchor.FollowRotation);
                    AnchorSceneHandles.DrawOffsetLink(originWorld, AnchorPose.WorldPosition(track.Anchor, resolved.BaseTransform, resolved.ExtraOffset), track.Anchor.LocalOffset, color);
                    var result = AnchorSceneHandles.Draw(track.Anchor, resolved.BaseTransform, resolved.ExtraOffset, label, color);
                    if (result.PositionChanged || result.RotationChanged)
                    {
                        ApplyTrackAnchorHandleResult(i, result);
                    }
                }
                else
                {
                    DrawSelectableMarker(i, track.Anchor, resolved, color, label);
                }
            }
        }

        // 描画権を持たないウィンドウ: ハンドル無し・薄い目印のみ(VfxEditorWindow / AnchorEditorWindow と同じ)。
        private void DrawInactiveSceneAnchors(PresentationTrack[] tracks, Transform self, Transform target)
        {
            for (var i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                if (!PresentationTrackAnchorResolver.HasPosition(track.Kind))
                {
                    continue;
                }

                if (!_sceneAnchorShowAll && i != _selectedTrack)
                {
                    continue;
                }

                if (track.Kind == TrackKind.AnchorGroup)
                {
                    DrawAnchorGroupPoints(i, in track, self, target, isSelected: i == _selectedTrack, interactive: false);
                    continue;
                }

                var resolved = PresentationTrackAnchorResolver.Resolve(in track, self, target);
                AnchorSceneHandles.DrawInactiveMarker(track.Anchor, resolved.BaseTransform, resolved.ExtraOffset, SceneAnchorLabel(i, in track), SceneAnchorColorFor(track.Kind));
            }
        }

        // AnchorGroup: 参照先 AnchorGroupData の全点を番号付きで描く(AnchorGroupEditorWindow と同じ見た目)。
        // 点の編集(移動/上書き)は Anchor Group Editor に任せるため、ハンドルは一切出さない(表示のみ)。
        // interactive=true(このウィンドウが SceneGuiOwner)のときだけ点クリックでトラック選択に切り替える。
        private void DrawAnchorGroupPoints(int trackIndex, in PresentationTrack track, Transform self, Transform target, bool isSelected, bool interactive)
        {
            if (_preview?.Registry == null || !track.Asset.IsAssigned)
            {
                return;
            }

            var group = _preview.Registry.ResolveOrPlaceholder<AnchorGroupData>(track.Asset.Id);
            if (group == null)
            {
                return;
            }

            var count = PresentationTrackAnchorResolver.ResolveAnchorGroupPoints(
                _preview.Registry, group, self, target, track.Target, _sceneAnchorGroupPoints,
                out var baseTransform, out var extraOffset);

            if (count == 0)
            {
                return;
            }

            var baseColor = isSelected ? Color.yellow : SceneAnchorGroupColor;
            var alpha = interactive ? 1f : 0.3f;
            Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);

            for (var p = 0; p < count; p++)
            {
                var worldPos = AnchorPose.WorldPosition(_sceneAnchorGroupPoints[p].Def, baseTransform, extraOffset);
                var size = HandleUtility.GetHandleSize(worldPos) * 0.1f;

                if (interactive && Handles.Button(worldPos, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                {
                    SelectTrackFromScene(trackIndex);
                }
                else
                {
                    Handles.DrawWireDisc(worldPos, Vector3.up, size);
                }

                Handles.Label(worldPos + Vector3.up * size * 1.6f, p.ToString(), EditorStyles.miniLabel);
            }

            var overviewPos = AnchorPose.WorldPosition(_sceneAnchorGroupPoints[0].Def, baseTransform, extraOffset);
            Handles.Label(overviewPos + Vector3.up * HandleUtility.GetHandleSize(overviewPos) * 0.3f,
                $"{SceneAnchorLabel(trackIndex, in track)}(編集は Anchor Group Editor)", EditorStyles.miniLabel);
        }

        // 非選択トラックの点(クリックで選択に切り替える。AnchorGroupEditorWindow の点選択と同じ操作感)。
        private void DrawSelectableMarker(int index, in AnchorDef anchor, PresentationTrackAnchorResolver.Result resolved, Color color, string label)
        {
            var worldPos = AnchorPose.WorldPosition(anchor, resolved.BaseTransform, resolved.ExtraOffset);
            var size = HandleUtility.GetHandleSize(worldPos) * 0.12f;
            Handles.color = color;
            if (Handles.Button(worldPos, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
            {
                SelectTrackFromScene(index);
            }

            Handles.Label(worldPos + Vector3.up * size * 1.6f, label, EditorStyles.miniLabel);
        }

        private void SelectTrackFromScene(int index)
        {
            _selectedTrack = index;
            RefreshTracksList();
            SceneView.RepaintAll();
        }

        // ハンドルの結果をこのトラック自身の Anchor へ書き戻す(常にこのトラック。参照先アセットは書き換えない)。
        // 変更は次回の再生から反映される(このエディタの他のトラック編集〔Kind/Time/Params 等〕と同じ流儀。
        // 再生中の実体への即時反映〔ReapplyAnchor 相当〕は行わない — VfxManager.ReapplyAnchor は
        // Data.AnchorId/Data.Anchor から再合成する実装のため、track.Anchor しか使わない Presentation 経由の
        // 実体にそのまま使うと誤った位置〔Data 側の既定 Anchor〕へ戻ってしまう。安全側に倒し、何もしない)。
        private void ApplyTrackAnchorHandleResult(int index, AnchorSceneHandles.Result result)
        {
            if (_target?.Tracks == null || index < 0 || index >= _target.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(_target, "Move Presentation Track Anchor");
            var track = _target.Tracks[index];
            if (result.PositionChanged)
            {
                track.Anchor.LocalOffset = result.LocalOffset;
            }

            if (result.RotationChanged)
            {
                track.Anchor.LocalEuler = result.LocalEuler;
            }

            _target.Tracks[index] = track;
            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            SceneView.RepaintAll();
        }

        private static Color SceneAnchorColorFor(TrackKind kind) => kind == TrackKind.Se ? SceneAnchorSeColor : SceneAnchorVfxColor;

        // "[{index}] {Kind} {アセット表示名}" 形式(トラック一覧の TrackFoldoutTitle と揃えつつ、
        // Scene 上は時刻/SignalKey まで出すと長すぎるので Kind とアセット名だけにする)。
        private string SceneAnchorLabel(int index, in PresentationTrack track)
        {
            var assetType = PresentationTrackKindMapping.AssetTypeFor(track.Kind);
            var assetName = string.Empty;
            if (assetType != null && track.Asset.IsAssigned)
            {
                var asset = PresentationTrackKindMapping.FindAssetById(assetType, track.Asset.Id);
                if (asset != null)
                {
                    assetName = $" {asset.DisplayName ?? asset.name}";
                }
            }

            return $"[{index}] {track.Kind}{assetName}";
        }
    }
}
