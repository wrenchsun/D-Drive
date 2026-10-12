using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
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
    // 純関数、EditMode テスト対象)に切り出してある。2026-09-19(トラック/アセット両方の Anchor 参照)から、
    // 実効 Anchor はトラック自身の Anchor だけでなく参照先 VfxData/SeData の AnchorId/埋め込み Anchor も
    // 見るようになった(3 ケース、PresentationTrackAnchorComposer に集約)。編集(ハンドル)の書き戻し先は
    // 常に「このトラックの PresentationTrack.Anchor」のみ(ケース1〔アセット側のみ設定〕はハンドル自体を
    // 出さない)。共有アセット(AnchorData/VfxData/SeData)はここでは一切書き換えない。
    //
    // AnchorGroup(配置セット、2026-09-19、[22_anchor_group.md] §5 で予告されていた Presentation 統合)は
    // Vfx/Se と解決方法が異なる: track.Anchor(単一の AnchorDef)ではなく、参照先 AnchorGroupData の原点 +
    // 全点(AnchorGroupPlanner.EnumeratePoints、AnchorGroupEditorWindow と同じ見た目)を番号付きで描く。
    // 点の編集(移動)は Anchor Group Editor に任せ、ここでは表示のみ(ハンドルを出さない)。
    internal sealed partial class PresentationEditor
    {
        // Vfx/Se(位置を持つ Kind)ごとの表示色。VFX Editor の埋め込み Anchor 表示(teal 系)・
        // Anchor Editor(黄)・Anchor Group Editor(水色)のいずれとも衝突しない配色にする。
        private static readonly Color SceneAnchorVfxColor = new(0.9f, 0.4f, 0.85f);   // マゼンタ
        private static readonly Color SceneAnchorSeColor = new(0.3f, 0.85f, 0.95f);   // シアン

        // AnchorGroup(配置セット)専用。上記いずれとも、Cutscene の AnchorGroup トラック色(黄緑系
        // (0.6, 0.8, 0.4)、CutsceneAnchorGroupClip)とも大きくは衝突しない黄緑にした。
        private static readonly Color SceneAnchorGroupColor = new(0.6f, 0.85f, 0.3f);

        // AnchorGroup の点バッファ(AnchorGroupData.MaxPoints 分を使い回す。AnchorGroupEditorWindow と同じ)。
        private readonly AnchorSpawnSpec[] _sceneAnchorGroupPoints = new AnchorSpawnSpec[AnchorGroupData.MaxPoints];

        // ケース3(両方設定)の合成段バッファ([08_presentation.md] 実装メモ 2026-09-19「トラック/アセット
        // 両方の Anchor 参照」)。Editor 専用の描画に使うだけなので使い回す(定常経路ではないため必須ではないが、
        // 他のバッファと同じ流儀に揃える)。
        private readonly PresentationTrackAnchorComposer.Stage[] _sceneAnchorComposedStages = new PresentationTrackAnchorComposer.Stage[PresentationTrackAnchorComposer.MaxStages];

        [SerializeField] private bool _sceneAnchorEnabled = true;
        [SerializeField] private bool _sceneAnchorShowAll = true;

        // ── 欄(SceneView に位置を出す) ──

        private Label _sceneOwnerLabel;

        protected override bool WantsSceneGui => _sceneAnchorEnabled;

        private UxRow CreateSceneViewRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var sceneToggle = new Toggle("SceneView 表示")
            {
                value = _sceneAnchorEnabled,
                tooltip = "OFF: Vfx / Se トラックの Anchor(出す位置)を SceneView に一切描かない。ON: 目印を描き、" +
                    "このウィンドウを最後に操作していれば選択中のトラックに移動 / 回転ハンドルも出す(回転ツール選択時は回転)",
            };
            sceneToggle.AddToClassList("ux-bar__repeat");
            sceneToggle.RegisterValueChangedCallback(evt =>
            {
                _sceneAnchorEnabled = evt.newValue;
                RefreshSceneOwnerLabel();
                SceneView.RepaintAll();
            });
            line.Add(sceneToggle);

            var modeField = new DropdownField("表示対象", new List<string> { "すべて(位置を持つ全トラック)", "選択中のみ" }, _sceneAnchorShowAll ? 0 : 1)
            {
                tooltip = "すべて: 位置を持つ全トラック(Vfx / Se)を番号付きの点で表示。選択中のみ: 一覧で選択中の 1 本だけに絞る(トラックが多いときに見やすくする)。どちらでも、ハンドルで動かせるのは選択中の 1 本だけ",
            };
            modeField.AddToClassList("ux-pathmenu");
            modeField.RegisterValueChangedCallback(evt =>
            {
                _sceneAnchorShowAll = modeField.index == 0;
                SceneView.RepaintAll();
            });
            line.Add(modeField);
            box.Add(line);

            _sceneOwnerLabel = new Label();
            _sceneOwnerLabel.AddToClassList("ux-dim");
            _sceneOwnerLabel.AddToClassList("ux-note");
            box.Add(_sceneOwnerLabel);
            RefreshSceneOwnerLabel();
            return new PresentationAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private void RefreshSceneOwnerLabel()
        {
            if (_sceneOwnerLabel == null)
            {
                return;
            }

            var text = _sceneAnchorEnabled ? SceneGuiOwner.DescribeFor(this) : "SceneView: 表示オフ";
            if (_sceneOwnerLabel.text != text)
            {
                _sceneOwnerLabel.text = text;
            }
        }

        // ── SceneView 描画 ──

        protected override void DrawSceneGui()
        {
            if (!_sceneAnchorEnabled || Target?.Tracks == null || _preview == null)
            {
                return;
            }

            var tracks = Target.Tracks;
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

                var asset = ResolveTrackAsset(in track);
                var effective = PresentationTrackAnchorResolver.ResolveEffective(in track, self, target, _preview.Registry, asset);
                var color = SceneAnchorColorFor(track.Kind);
                var label = SceneAnchorLabel(i, in track);

                if (!isSelected)
                {
                    DrawSelectableEffectiveMarker(i, in effective, color, label);
                    continue;
                }

                switch (effective.Case)
                {
                    case PresentationTrackAnchorComposer.Case.AssetOnly:
                        DrawAssetOnlyCase(i, in track, in effective, color, label);
                        break;

                    case PresentationTrackAnchorComposer.Case.Both:
                        DrawBothCase(i, in track, asset, in effective, color, label);
                        break;

                    default: // TrackOnly / Neither — 従来どおり track.Anchor がそのまま実効値。
                        var originWorld = AnchorSceneHandles.DrawOrigin(effective.BaseTransform, effective.ExtraOffset, AnchorSceneHandles.DescribeBase(track.Anchor, effective.BaseTransform), track.Anchor.FollowRotation);
                        AnchorSceneHandles.DrawOffsetLink(originWorld, AnchorPose.WorldPosition(track.Anchor, effective.BaseTransform, effective.ExtraOffset), track.Anchor.LocalOffset, color);
                        var result = AnchorSceneHandles.Draw(track.Anchor, effective.BaseTransform, effective.ExtraOffset, label, color);
                        if (result.PositionChanged || result.RotationChanged)
                        {
                            ApplyTrackAnchorHandleResult(i, result);
                        }

                        break;
                }
            }
        }

        // ケース1(アセット側のみ設定): 2026-09-20、指摘2でユーザー要望により設計変更(以前は表示のみだった)。
        // 最終位置に移動/回転ハンドルを出し、ドラッグしたら「合成後の最終位置がハンドルの位置になる」ように
        // トラックの Anchor(親)を逆算して設定する(ケース3へ遷移する)。掴む点を「最終位置」で統一するため、
        // ケース3(DrawBothCase)でも同じ点をドラッグ対象にする([08_presentation.md] 実装メモ参照)。
        private void DrawAssetOnlyCase(int index, in PresentationTrack track, in PresentationTrackAnchorResolver.EffectiveResult effective, Color color, string label)
        {
            var originWorld = AnchorSceneHandles.DrawOrigin(effective.BaseTransform, effective.ExtraOffset, AnchorSceneHandles.DescribeBase(effective.ComposedDef, effective.BaseTransform), effective.ComposedDef.FollowRotation);
            AnchorSceneHandles.DrawOffsetLink(originWorld, AnchorPose.WorldPosition(effective.ComposedDef, effective.BaseTransform, effective.ExtraOffset), effective.ComposedDef.LocalOffset, color);

            var result = AnchorSceneHandles.Draw(effective.ComposedDef, effective.BaseTransform, effective.ExtraOffset, $"{label}(アセット側の Anchor。動かすとトラック側に上書きします)", color);
            if (result.PositionChanged || result.RotationChanged)
            {
                ApplyAssetOnlyDragToTrack(index, in track, in effective, result);
            }
        }

        // ケース1 → ケース3 への遷移。アセット側の連鎖(= effective.ComposedDef、ドラッグ前の値)を「子」として、
        // ドラッグ後の望む最終位置/回転を再現する「親」(トラック Anchor)を逆算する。
        // トラック Anchor の Space/Path/FollowRotation/DetachOnStop はアセット側の連鎖のルートの値をコピーする
        // (コピーしないと合成の基準〔effective.BaseTransform〕がワールド原点に飛んで位置がジャンプする。
        // ケース3は親〔トラック〕の Space/Path が効くため)。LocalScale はハンドルで編集しないため常に 1 にする。
        private void ApplyAssetOnlyDragToTrack(int index, in PresentationTrack track, in PresentationTrackAnchorResolver.EffectiveResult effective, AnchorSceneHandles.Result result)
        {
            if (Target?.Tracks == null || index < 0 || index >= Target.Tracks.Length)
            {
                return;
            }

            var child = effective.ComposedDef; // ドラッグ前(アセット側のみ)の合成値 = 逆算の「子」
            var desiredOffset = result.PositionChanged ? result.LocalOffset : child.LocalOffset;
            var desiredEuler = result.RotationChanged ? result.LocalEuler : child.LocalEuler;
            PresentationTrackAnchorComposer.SolveTrackLocal(desiredOffset, desiredEuler, child.LocalOffset, child.LocalEuler, out var trackOffset, out var trackEuler);

            Undo.RecordObject(Target, "Set Presentation Track Anchor (from Asset Anchor)");
            var updated = track;
            updated.Anchor.Space = child.Space;
            updated.Anchor.Path = child.Path;
            updated.Anchor.FollowRotation = child.FollowRotation;
            updated.Anchor.DetachOnStop = child.DetachOnStop;
            updated.Anchor.LocalScale = Vector3.one;
            updated.Anchor.LocalOffset = trackOffset;
            updated.Anchor.LocalEuler = trackEuler;
            Target.Tracks[index] = updated;
            EditorUtility.SetDirty(Target);
            So?.Update();
            AfterDataWrite(); // 行の要約・Anchor の注記を更新するため
            SceneView.RepaintAll();
        }

        // ケース3(両方設定): 基準 → トラック Anchor(親、表示のみ) → アセット側の各段(表示のみ) → 最終位置
        // (編集可能なハンドル)。掴む点はケース1と同じ「最終位置」にする(ユーザーが実際に見ている/触りたいのは
        // 常に最終位置のため。トラック Anchor 自体の位置は表示のみに変更した。理由は [08_presentation.md] 参照)。
        private void DrawBothCase(int index, in PresentationTrack track, AssetDataBase asset, in PresentationTrackAnchorResolver.EffectiveResult effective, Color color, string label)
        {
            var originWorld = AnchorSceneHandles.DrawOrigin(effective.BaseTransform, effective.ExtraOffset, AnchorSceneHandles.DescribeBase(track.Anchor, effective.BaseTransform), track.Anchor.FollowRotation);

            // トラック Anchor(親)は表示のみ(ドラッグ対象は最終位置に統一)。
            var trackWorld = AnchorPose.WorldPosition(track.Anchor, effective.BaseTransform, effective.ExtraOffset);
            AnchorSceneHandles.DrawOffsetLink(originWorld, trackWorld, track.Anchor.LocalOffset, color);
            AnchorSceneHandles.DrawTargetMarker(track.Anchor, effective.BaseTransform, effective.ExtraOffset, $"{label}(トラック Anchor、表示のみ)", color);

            // アセット側の各段。最後の段(= effective.ComposedDef と同じ最終位置)だけハンドルを出す。
            var count = PresentationTrackAnchorComposer.TryGetAssetAnchor(asset, out var assetAnchorId, out var assetEmbedded)
                ? PresentationTrackAnchorComposer.ResolveStages(in track, assetAnchorId, in assetEmbedded, _preview.Registry, _sceneAnchorComposedStages)
                : 0;

            var previous = trackWorld;
            for (var s = 1; s < count; s++)
            {
                var stage = _sceneAnchorComposedStages[s];
                var pos = AnchorPose.WorldPosition(stage.ComposedDef, effective.BaseTransform, effective.ExtraOffset);
                var rot = AnchorPose.WorldRotation(stage.ComposedDef, effective.BaseTransform, Quaternion.identity);
                var isLast = s == count - 1;
                if (isLast)
                {
                    Handles.color = new Color(color.r, color.g, color.b, 0.6f);
                    Handles.DrawDottedLine(previous, pos, 4f);
                    var handleResult = AnchorSceneHandles.Draw(stage.ComposedDef, effective.BaseTransform, effective.ExtraOffset, $"{stage.Label}(最終位置。動かすとトラック Anchor に逆算します)", color);
                    if (handleResult.PositionChanged || handleResult.RotationChanged)
                    {
                        ApplyBothDragToTrack(index, asset, in effective, handleResult);
                    }
                }
                else
                {
                    var stageLabel = $"{stage.Label}(アセット側)";
                    AnchorSceneHandles.DrawChainNode(previous, pos, rot, stageLabel, stage.PositionJitterRadius, color);
                }

                previous = pos;
            }
        }

        // ケース3での最終位置ドラッグ → トラック Anchor(親)の逆算。「子」はアセット側の連鎖だけを合成した値
        // (Case.AssetOnly と同じ式、DetermineCase の結果に関係なく求められる ComposeAssetOnly)。
        private void ApplyBothDragToTrack(int index, AssetDataBase asset, in PresentationTrackAnchorResolver.EffectiveResult effective, AnchorSceneHandles.Result result)
        {
            if (Target?.Tracks == null || index < 0 || index >= Target.Tracks.Length
                || !PresentationTrackAnchorComposer.TryGetAssetAnchor(asset, out var assetAnchorId, out var assetEmbedded))
            {
                return;
            }

            var child = PresentationTrackAnchorComposer.ComposeAssetOnly(assetAnchorId, in assetEmbedded, _preview?.Registry, sampleRandom: false).Def;
            var desiredOffset = result.PositionChanged ? result.LocalOffset : effective.ComposedDef.LocalOffset;
            var desiredEuler = result.RotationChanged ? result.LocalEuler : effective.ComposedDef.LocalEuler;
            PresentationTrackAnchorComposer.SolveTrackLocal(desiredOffset, desiredEuler, child.LocalOffset, child.LocalEuler, out var trackOffset, out var trackEuler);

            Undo.RecordObject(Target, "Move Presentation Track Anchor (Final Position)");
            var updated = Target.Tracks[index];
            updated.Anchor.LocalOffset = trackOffset;
            updated.Anchor.LocalEuler = trackEuler;
            Target.Tracks[index] = updated;
            EditorUtility.SetDirty(Target);
            So?.Update();
            SceneView.RepaintAll();
        }

        // トラック一覧で選択されていないトラックの点(effective.ComposedDef を使うため、ケースを問わず
        // 常に実際の再生位置に一致する)。クリックで選択に切り替える(2026-09-20、指摘1: 当たり判定を
        // 広げた共通 API AnchorSceneHandles.DrawClickableMarker を使う)。
        private void DrawSelectableEffectiveMarker(int index, in PresentationTrackAnchorResolver.EffectiveResult effective, Color color, string label)
        {
            if (AnchorSceneHandles.DrawClickableMarker(effective.ComposedDef, effective.BaseTransform, effective.ExtraOffset, label, color, active: true))
            {
                SelectTrackFromScene(index);
            }
        }

        // 描画権を持たないウィンドウ: 薄い目印のみ、ただしクリックは可能にする(2026-09-20、指摘1)。
        // クリックしたら SceneGuiOwner.Claim + Focus() でこのウィンドウが所有者になり、そのトラックを選択する
        // (VfxEditorWindow / AnchorEditorWindow と同じ「薄い目印」の見た目のまま、行き来できるようにする)。
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

                var asset = ResolveTrackAsset(in track);
                var effective = PresentationTrackAnchorResolver.ResolveEffective(in track, self, target, _preview?.Registry, asset);
                if (AnchorSceneHandles.DrawClickableMarker(effective.ComposedDef, effective.BaseTransform, effective.ExtraOffset, SceneAnchorLabel(i, in track), SceneAnchorColorFor(track.Kind), active: false))
                {
                    SelectTrackFromScene(i);
                }
            }
        }

        // AnchorGroup: 参照先 AnchorGroupData の全点を番号付きで描く(AnchorGroupEditorWindow と同じ見た目)。
        // 点の編集(移動/上書き)は Anchor Group Editor に任せるため、ハンドルは一切出さない(表示のみ)。
        // クリックはどちらの状態でも受け付ける(2026-09-20、指摘1: 非所有ウィンドウでもクリックで
        // Claim+Focus+選択できるようにする。SelectTrackFromScene がオーナー切替まで面倒を見る)。
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
                var handleSize = HandleUtility.GetHandleSize(worldPos);
                var visualSize = handleSize * 0.1f;
                var pickSize = handleSize * 0.25f; // 指摘1: 当たり判定を広げる(以前は size*1.5≈0.15 相当)

                if (Handles.Button(worldPos, Quaternion.identity, visualSize, pickSize, Handles.SphereHandleCap))
                {
                    SelectTrackFromScene(trackIndex);
                }

                Handles.Label(worldPos + Vector3.up * visualSize * 1.6f, p.ToString(), EditorStyles.miniLabel);
            }

            var overviewPos = AnchorPose.WorldPosition(_sceneAnchorGroupPoints[0].Def, baseTransform, extraOffset);
            Handles.Label(overviewPos + Vector3.up * HandleUtility.GetHandleSize(overviewPos) * 0.3f,
                $"{SceneAnchorLabel(trackIndex, in track)}(編集は Anchor Group Editor)", EditorStyles.miniLabel);
        }

        // 参照先 VfxData/SeData を解決する(見つからなければ null。その場合はケース TrackOnly/Neither 相当)。
        private AssetDataBase ResolveTrackAsset(in PresentationTrack track)
        {
            if (!track.Asset.IsAssigned)
            {
                return null;
            }

            var assetType = PresentationTrackKindMapping.AssetTypeFor(track.Kind);
            return assetType != null ? PresentationTrackKindMapping.FindAssetById(assetType, track.Asset.Id) : null;
        }

        // SceneView の点をクリックしたら、このウィンドウが SceneGuiOwner になっていなくても Claim + Focus() で描画権を奪い、
        // 選択したトラックの行を一覧で展開・スクロール表示する(既に選択中のトラックの点をクリックしても冪等)。
        private void SelectTrackFromScene(int index)
        {
            SceneGuiOwner.Claim(this);
            Focus();
            SelectTrack(index, true);
            ScrollToSelectedTrackRow();
            RefreshSceneOwnerLabel();
        }

        // 一覧の選択中の行(ux-fxrow--hl)までスクロールする。行を作り直した直後は geometry が未確定のことがあるので、次のフレームへ回す。
        private void ScrollToSelectedTrackRow()
        {
            if (_trackList == null)
            {
                return;
            }

            var list = _trackList;
            list.schedule.Execute(() =>
            {
                var row = list.Q(className: "ux-fxrow--hl");
                if (row != null)
                {
                    ScrollToElement(row);
                }
            });
        }

        // ハンドルの結果をこのトラック自身の Anchor へ書き戻す(常にこのトラック。参照先アセットは書き換えない)。
        // 変更は次回の再生から反映される(このエディタの他のトラック編集〔Kind/Time/Params 等〕と同じ流儀。
        // 再生中の実体への即時反映〔ReapplyAnchor 相当〕は行わない — VfxManager.ReapplyAnchor は
        // Data.AnchorId/Data.Anchor から再合成する実装のため、track.Anchor しか使わない Presentation 経由の
        // 実体にそのまま使うと誤った位置〔Data 側の既定 Anchor〕へ戻ってしまう。安全側に倒し、何もしない)。
        private void ApplyTrackAnchorHandleResult(int index, AnchorSceneHandles.Result result)
        {
            if (Target?.Tracks == null || index < 0 || index >= Target.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(Target, "Move Presentation Track Anchor");
            var track = Target.Tracks[index];
            if (result.PositionChanged)
            {
                track.Anchor.LocalOffset = result.LocalOffset;
            }

            if (result.RotationChanged)
            {
                track.Anchor.LocalEuler = result.LocalEuler;
            }

            Target.Tracks[index] = track;
            EditorUtility.SetDirty(Target);
            So?.Update();
            SceneView.RepaintAll();
        }

        private static Color SceneAnchorColorFor(TrackKind kind) => kind == TrackKind.Se ? SceneAnchorSeColor : SceneAnchorVfxColor;

        // "[{index}] {Kind} {アセット表示名}" 形式(トラック一覧の TrackFoldoutTitle と揃えつつ、
        // Scene 上は時刻/SignalKey まで出すと長すぎるので Kind とアセット名だけにする)。
        private string SceneAnchorLabel(int index, in PresentationTrack track)
        {
            var asset = ResolveTrackAsset(in track);
            var assetName = asset != null ? $" {asset.DisplayName ?? asset.name}" : string.Empty;
            return $"[{index}] {track.Kind}{assetName}";
        }
    }
}
