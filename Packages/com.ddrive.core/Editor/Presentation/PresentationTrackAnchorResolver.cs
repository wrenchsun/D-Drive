using DDrive.Foundation.Data;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Presentation;
using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] SceneView Anchor 表示 — 「このトラックは今どこに出るか」をウィンドウ非依存の
    // 純関数として切り出したもの(PresentationEditorWindow.SceneAnchors.cs から使う。EditMode テスト対象)。
    //
    // 2026-09-19 追記(トラック/アセット両方の Anchor 参照): 以前は Kind=Vfx/Se の実効 Anchor は常に
    // track.Anchor のみだったが(参照先 VfxData/SeData の AnchorId/埋め込み Anchor は一切見なかった。
    // [43_manual_verification_2026-09-17.md] §6 で指摘されていた既知事象)、ユーザー決定によりトラックの
    // Anchor とアセット側の Anchor の両方を参照するよう仕様変更した。3 ケースの判定・合成は
    // `PresentationTrackAnchorComposer`(Runtime/Presentation、ランタイムと共通)に集約した。
    // `Resolve`(下記、track.Anchor のみを見る)は「ケース2(トラックのみ設定)/ケース Neither」でのみ正しい
    // 結果になる(既存呼び出し元・テストのために残す)。アセット情報を踏まえた解決には `ResolveEffective` を使う。
    //   - 位置を持つのは Vfx / Se / AnchorGroup。Anim/Anim2D/Bgm/CameraShake/Haptic/HitStop/Timeline/Canvas/
    //     UiTween/Marker/Signal はいずれも位置を消費しない(CameraShake は ctx.Position を直接使うのみ)。
    //   - AnchorGroup(2026-09-19、[22_anchor_group.md] §5 で予告されていた Presentation 統合)は Vfx/Se と
    //     解決方法が異なる: track.Anchor(単一の AnchorDef)ではなく、参照先 AnchorGroupData の原点 +
    //     パターン/手置きの点(AnchorGroupPlanner.EnumeratePoints)から「全点」を求める。編集は Anchor Group
    //     Editor に任せるため、ここでは列挙のみ提供する(ハンドルでの書き戻しは無い)。トラック/アセット
    //     両方の Anchor 参照の対象外(AnchorGroup は自分の点を持つため)。
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

        // 位置を持つ Kind か(Vfx/Se/AnchorGroup。上記調査結果のとおり)。
        public static bool HasPosition(TrackKind kind) => kind == TrackKind.Vfx || kind == TrackKind.Se || kind == TrackKind.AnchorGroup;

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

        // トラック/アセット両方の Anchor 参照(2026-09-19)を踏まえた解決結果。SceneView 描画専用
        // (ランタイムは PresentationManager.FireVfx/FireSe が PresentationTrackAnchorComposer を直接使う)。
        public readonly struct EffectiveResult
        {
            public readonly bool HasPosition;
            public readonly PresentationTrackAnchorComposer.Case Case;

            // 解決先(見つからない/World なら null = ワールド原点)。AssetOnly はアセット側の連鎖の最上段、
            // TrackOnly/Both/Neither はトラック自身の Space/Path で決まる。
            public readonly Transform BaseTransform;
            public readonly Vector3 ExtraOffset;

            // 合成済み(ランダム無し)の最終 Def。最終位置の表示・ハンドルの逆変換に使う。
            public readonly AnchorDef ComposedDef;

            // 参照先 VfxData/SeData(表示・注記用。見つからなければ null)。
            public readonly AssetDataBase Asset;

            public EffectiveResult(bool hasPosition, PresentationTrackAnchorComposer.Case kase, Transform baseTransform, Vector3 extraOffset, AnchorDef composedDef, AssetDataBase asset)
            {
                HasPosition = hasPosition;
                Case = kase;
                BaseTransform = baseTransform;
                ExtraOffset = extraOffset;
                ComposedDef = composedDef;
                Asset = asset;
            }
        }

        // Vfx/Se 専用(AnchorGroup は対象外。呼び出し側で HasPosition(track.Kind) && track.Kind != AnchorGroup
        // を確認してから呼ぶこと)。referencedAsset は PresentationTrackKindMapping.FindAssetById 等で
        // 呼び出し側が解決した参照先(見つからなければ null。その場合はケース TrackOnly/Neither 相当になる)。
        public static EffectiveResult ResolveEffective(in PresentationTrack track, Transform self, Transform target, IAssetRegistry registry, AssetDataBase referencedAsset)
        {
            if (track.Kind != TrackKind.Vfx && track.Kind != TrackKind.Se)
            {
                return default;
            }

            PresentationTrackAnchorComposer.TryGetAssetAnchor(referencedAsset, out var assetAnchorId, out var assetEmbedded);
            var kase = PresentationTrackAnchorComposer.DetermineCase(in track, assetAnchorId, in assetEmbedded);

            // ルートの Space/Path は「ケースに応じた合成の根」— AssetOnly はアセット側の(連鎖ならその
            // 最上段)、TrackOnly/Both/Neither はトラック自身(Neither は World 既定と同じ結果になる)。
            var rootDef = kase == PresentationTrackAnchorComposer.Case.AssetOnly
                ? PresentationTrackAnchorComposer.ResolveAssetRootDef(assetAnchorId, in assetEmbedded, registry)
                : track.Anchor;

            var ctx = new PlayContext { Self = self, Target = target };
            var contextRoot = PresentationManager.ResolveContextRoot(in ctx, track.Target);
            var baseTransform = AnchorResolver.Resolve(rootDef, contextRoot);

            var extraOffset = Vector3.zero;
            if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
            {
                extraOffset = point.SpawnOffset;
            }

            var spec = PresentationTrackAnchorComposer.Compose(in track, assetAnchorId, in assetEmbedded, registry, sampleRandom: false);

            return new EffectiveResult(true, kase, baseTransform, extraOffset, spec.Def, referencedAsset);
        }

        // AnchorGroup 専用: 参照先 AnchorGroupData の全点を列挙する(AnchorGroupEditorWindow.RefreshPoints と
        // 同じ式。ランダムはサンプリングしない = sampleRandom:false、エディタ表示と同じ固定シード。[22] §3.6)。
        // 戻り値は列挙した点数。pointsBuffer は呼び出し側で AnchorGroupData.MaxPoints 分確保して使い回すこと
        // (定常経路ではない〔SceneView 描画〕が、AnchorGroupEditorWindow と同じく配列を使い回す慣習に合わせる)。
        public static int ResolveAnchorGroupPoints(
            IAssetRegistry registry,
            AnchorGroupData group,
            Transform self,
            Transform target,
            TrackTargetMode mode,
            AnchorSpawnSpec[] pointsBuffer,
            out Transform baseTransform,
            out Vector3 extraOffset)
        {
            baseTransform = null;
            extraOffset = Vector3.zero;

            if (group == null || pointsBuffer == null)
            {
                return 0;
            }

            var ctx = new PlayContext { Self = self, Target = target };
            var root = PresentationManager.ResolveContextRoot(in ctx, mode);

            // [22_anchor_group.md] §3.1 と同じ優先順位: OriginAnchorId があればそちらを優先し、無ければ
            // 埋め込み Origin を使う(AnchorGroupEditorWindow.OriginDef と同じ)。
            var originDef = group.OriginAnchorId.IsValid && registry != null
                ? AnchorChain.Resolve(registry, group.OriginAnchorId, sampleRandom: false).Def
                : group.Origin;

            baseTransform = AnchorResolver.Resolve(originDef, root);
            if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
            {
                extraOffset = point.SpawnOffset;
            }

            return AnchorGroupPlanner.EnumeratePoints(registry, group, pointsBuffer);
        }
    }
}
