using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Presentation;
using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] SceneView Anchor 表示 — 「このトラックは今どこに出るか」をウィンドウ非依存の
    // 純関数として切り出したもの(PresentationEditorWindow.SceneAnchors.cs から使う。EditMode テスト対象)。
    //
    // 実装調査で確認した事実(推測ではない。PresentationManager.FireVfx/FireSe を参照):
    //   - Kind=Vfx/Se は必ず `AnchorSpawnSpec.FromDef(track.Anchor)` を「合成済み(presolved)」として
    //     VfxManager.SpawnData(data, in spec, root) / AudioManager.PlaySeData(data, in spec, root, seed)
    //     へ渡す。この経路(SpawnDataLocal の presolved 引数)は ResolveAnchorSpec(anchorOverride > Data.AnchorId >
    //     Data.Anchor の優先順位)を一切呼ばない。したがって **参照先 VfxData/SeData 自身の AnchorId / 埋め込み
    //     Anchor は Presentation 経由では絶対に使われない**。位置を決めるのは track.Anchor(このトラック自身の
    //     埋め込み AnchorDef)と track.Target(TrackTargetMode)だけ([43_manual_verification_2026-09-17.md] §6
    //     「Presentation に Anchor 上書きが無い」で既に指摘済みの既知事象と一致)。
    //   - TrackKind に AnchorGroup は存在しない(22_anchor_group.md §5 で「Presentation 統合は Phase 5」と
    //     予告されていたが未実装のまま)。よって「AnchorGroup トラック」は描けない(そもそも作れない)。
    //   - 位置を持つのは Vfx / Se のみ。Anim/Anim2D/Bgm/CameraShake/Haptic/HitStop/Timeline/Canvas/UiTween/
    //     Marker/Signal はいずれも track.Anchor を消費しない(CameraShake は ctx.Position を直接使うのみ)。
    public static class PresentationTrackAnchorResolver
    {
        // 解決結果。HasPosition=false は「この Kind には位置が無い」ことを示す(Anchor は無視してよい)。
        public readonly struct Result
        {
            public readonly bool HasPosition;

            // Anchor.Space/Path を contextRoot 配下で解決した Transform(見つからない/World なら null = ワールド原点)。
            public readonly Transform BaseTransform;

            // BaseTransform が AnchorPoint の場合の SpawnOffset(旧形式のシーン配置アンカーとの後方互換、[21] §3.4)。
            public readonly Vector3 ExtraOffset;

            public Result(bool hasPosition, Transform baseTransform, Vector3 extraOffset)
            {
                HasPosition = hasPosition;
                BaseTransform = baseTransform;
                ExtraOffset = extraOffset;
            }
        }

        // 位置を持つ Kind か(Vfx/Se のみ。上記調査結果のとおり)。
        public static bool HasPosition(TrackKind kind) => kind == TrackKind.Vfx || kind == TrackKind.Se;

        // ランタイムの FireVfx/FireSe と同じ解決(PresentationManager.ResolveContextRoot を共用し、
        // コピペしない)。self/target はプレビューの ctx.Self/ctx.Target 相当(統合プレビューでは
        // ctx.Target は常に null、[08] 実装メモ参照)。
        public static Result Resolve(in PresentationTrack track, Transform self, Transform target)
        {
            if (!HasPosition(track.Kind))
            {
                return default;
            }

            var ctx = new PlayContext { Self = self, Target = target };
            var root = PresentationManager.ResolveContextRoot(in ctx, track.Target);
            var baseTransform = AnchorResolver.Resolve(track.Anchor, root);

            var extraOffset = Vector3.zero;
            if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
            {
                extraOffset = point.SpawnOffset;
            }

            return new Result(true, baseTransform, extraOffset);
        }
    }
}
