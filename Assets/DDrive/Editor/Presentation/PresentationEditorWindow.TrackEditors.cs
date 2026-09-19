using DDrive.Editor.Anchor;
using DDrive.Editor.Anim;
using DDrive.Editor.Inspector;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] 指摘3/4(2026-09-20) — トラックの行に「専用エディタで開く」導線を足し、
    // Presentation Editor と同時に編集できるようにする。2 つのモード:
    //   1. 「一緒に調整」(既定): 確認用シーン・配置済みモデル・再生状態はそのまま。専用エディタの
    //      「スポーン先」に統合プレビューが配置した Self(ScenePresentationPreviewDriver.SelfRoot)を渡す。
    //      attach 付きの Open(data, attachTarget) overload がある種別だけこの経路(PresentationTrackEditorRouting)。
    //   2. 「単体で確認用シーンに開き直す」: 専用エディタの通常の Open(data)(DataEditorRegistry 経由)。
    //      Presentation のプレビューには一切触れない(未保存の警告は各エディタの既存の仕組みに任せる)。
    public sealed partial class PresentationEditorWindow
    {
        // トラック行(PresentationEditorWindow.Tracks.cs の BuildTrackRow)から呼ぶ。Asset を持たない
        // Kind(HitStop/Marker/Signal)や、専用エディタが登録されていない種別は null を返す(行を足さない)。
        private VisualElement BuildTrackEditorOpenRow(int index, TrackKind kind)
        {
            var assetType = PresentationTrackKindMapping.AssetTypeFor(kind);
            if (assetType == null || DataEditorRegistry.GetEntries(assetType).Count == 0)
            {
                return null;
            }

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginBottom = 4 } };
            row.Add(new Button(() => OpenTrackEditorTogether(index))
            {
                text = "専用エディタで開く(一緒に調整)",
                tooltip = "確認用シーン・配置済みモデル・再生状態はそのまま。専用エディタの「スポーン先」に Presentation が配置した Self を渡します",
            });
            row.Add(new Button(() => OpenTrackEditorAlone(index))
            {
                text = "単体で確認用シーンに開き直す",
                tooltip = "専用エディタのいつもの流れで開きます(このアセット単体)。Presentation の統合プレビューは片付きます",
            });
            return row;
        }

        private void OpenTrackEditorTogether(int index)
        {
            var asset = ResolveOpenableTrackAsset(index);
            if (asset == null)
            {
                AppendLog("⚠ Asset が未設定のため専用エディタを開けません");
                return;
            }

            var attach = _preview?.SelfRoot != null ? _preview.SelfRoot.gameObject : null;
            switch (PresentationTrackEditorRouting.Classify(asset))
            {
                case PresentationTrackEditorRouting.EditorKind.VfxWithAttach:
                    VfxEditorWindow.Open((VfxData)asset, attach);
                    break;
                case PresentationTrackEditorRouting.EditorKind.AnchorGroupWithAttach:
                    AnchorGroupEditorWindow.Open((AnchorGroupData)asset, attach);
                    break;
                case PresentationTrackEditorRouting.EditorKind.AnimWithAttach:
                    AnimEditorWindow.Open((AnimData)asset, attach);
                    break;
                default:
                    DataEditorRegistry.OpenDefault(asset);
                    break;
            }

            AppendLog($"'{asset.DisplayName ?? asset.name}' を専用エディタで開きました(一緒に調整。確認用シーン・再生状態は維持)");
        }

        private void OpenTrackEditorAlone(int index)
        {
            var asset = ResolveOpenableTrackAsset(index);
            if (asset == null)
            {
                AppendLog("⚠ Asset が未設定のため専用エディタを開けません");
                return;
            }

            if (!DataEditorRegistry.OpenDefault(asset))
            {
                return;
            }

            AppendLog($"'{asset.DisplayName ?? asset.name}' を専用エディタで開きました(単体で確認用シーンに開き直す)");
        }

        private AssetDataBase ResolveOpenableTrackAsset(int index)
        {
            if (_target?.Tracks == null || index < 0 || index >= _target.Tracks.Length)
            {
                return null;
            }

            var track = _target.Tracks[index];
            var assetType = PresentationTrackKindMapping.AssetTypeFor(track.Kind);
            return assetType != null && track.Asset.IsAssigned ? PresentationTrackKindMapping.FindAssetById(assetType, track.Asset.Id) : null;
        }
    }
}
