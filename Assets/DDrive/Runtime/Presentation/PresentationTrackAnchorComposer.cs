using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEngine;

namespace DDrive.Runtime.Presentation
{
    // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照) — Vfx/Se トラックの
    // 実効 Anchor を「トラックの Anchor」と「参照先 VfxData/SeData の Anchor(AnchorId の連鎖、または
    // 埋め込み Anchor)」の両方から決める(ユーザー決定 2026-09-19)。
    //
    // 3 ケース:
    //   1. アセット側だけ設定  → アセット側を使う([21_anchor_spec.md] §3.3 と同じ優先順位: AnchorId の連鎖 > 埋め込み)
    //   2. トラック側だけ設定  → 従来どおりトラックの Anchor(PresentationTrack.Anchor)
    //   3. 両方設定           → トラックの Anchor を親、アセット側をその子として合成する
    //                           (AnchorChain.Compose と同じ合成規則。「トラック Anchor → 連鎖のルート → … → 末端」の順)
    //   (どちらも既定値)      → ワールド既定(AnchorDef.WorldDefault)
    //
    // 「設定されている」の判定は AnchorDef.IsDefault(WorldDefault と等価か)/ AssetId.IsValid で行う。
    // 子(アセット側)の Space/Path/FollowRotation/DetachOnStop は無視され、常にルート(ケース3ではトラック、
    // ケース1ではアセット連鎖の最上段)の値が使われる(AnchorChain.Compose の既存規則そのまま)。
    //
    // PresentationManager.FireVfx/FireSe(定常経路)と、Editor の SceneView 表示(PresentationTrackAnchorResolver)
    // の両方がここを通ることで、実装を 1 か所に集約する(コピペしない)。定常経路から呼ばれるため、
    // ケース3の合成は固定長バッファを使い回して 0 alloc を保つ([12_review.md] §3)。
    public static class PresentationTrackAnchorComposer
    {
        // ケース1(アセットのみ)は AnchorChain.MaxDepth 段まで、ケース3(両方)はそれに「トラック Anchor」の
        // 1 段が足される。Editor 側が Stage[] バッファを確保する際の目安として公開する。
        public const int MaxStages = AnchorChain.MaxDepth + 1;

        public enum Case
        {
            Neither,   // 両方既定値 → ワールド既定
            AssetOnly, // アセット側のみ設定
            TrackOnly, // トラックのみ設定(従来どおり)
            Both,      // 両方設定 → トラックを親、アセットを子として合成
        }

        // 定常経路の合成で使う固定長バッファ(0 alloc)。呼び出しをまたいで使い回すため非 readonly の
        // 内容だけをクリアして返す(AnchorChain.Resolve と同じ流儀)。
        private static readonly AnchorData[] AssetChainBuffer = new AnchorData[AnchorChain.MaxDepth];
        private static readonly AnchorChainNode[] ComposedNodeBuffer = new AnchorChainNode[MaxStages];

        // ── 「設定されている」の判定 ──

        public static bool IsTrackAnchorSet(in PresentationTrack track) => !track.Anchor.IsDefault;

        public static bool IsAssetAnchorSet(AssetId<AnchorMarker> assetAnchorId, in AnchorDef assetEmbeddedAnchor)
            => assetAnchorId.IsValid || !assetEmbeddedAnchor.IsDefault;

        public static bool IsAssetAnchorSet(VfxData data) => data != null && IsAssetAnchorSet(data.AnchorId, data.Anchor);

        public static bool IsAssetAnchorSet(SeData data) => data != null && IsAssetAnchorSet(data.AnchorId, data.Anchor);

        // VfxData/SeData から AnchorId/埋め込み Anchor を取り出す(Editor 側が Kind を跨いで汎用的に
        // 扱えるようにするための薄いディスパッチ。それ以外の型は false を返す)。
        public static bool TryGetAssetAnchor(AssetDataBase asset, out AssetId<AnchorMarker> anchorId, out AnchorDef embedded)
        {
            switch (asset)
            {
                case VfxData vfx:
                    anchorId = vfx.AnchorId;
                    embedded = vfx.Anchor;
                    return true;

                case SeData se:
                    anchorId = se.AnchorId;
                    embedded = se.Anchor;
                    return true;

                default:
                    anchorId = default;
                    embedded = AnchorDef.WorldDefault;
                    return false;
            }
        }

        public static Case DetermineCase(in PresentationTrack track, AssetId<AnchorMarker> assetAnchorId, in AnchorDef assetEmbeddedAnchor)
        {
            var trackSet = IsTrackAnchorSet(in track);
            var assetSet = IsAssetAnchorSet(assetAnchorId, in assetEmbeddedAnchor);

            if (trackSet && assetSet)
            {
                return Case.Both;
            }

            if (assetSet)
            {
                return Case.AssetOnly;
            }

            if (trackSet)
            {
                return Case.TrackOnly;
            }

            return Case.Neither;
        }

        // ── 合成(ランタイム / Editor 共通) ──

        public static AnchorSpawnSpec Compose(in PresentationTrack track, VfxData data, IAssetRegistry registry, bool sampleRandom)
            => Compose(in track, data != null ? data.AnchorId : default, data != null ? data.Anchor : AnchorDef.WorldDefault, registry, sampleRandom);

        public static AnchorSpawnSpec Compose(in PresentationTrack track, SeData data, IAssetRegistry registry, bool sampleRandom)
            => Compose(in track, data != null ? data.AnchorId : default, data != null ? data.Anchor : AnchorDef.WorldDefault, registry, sampleRandom);

        public static AnchorSpawnSpec Compose(
            in PresentationTrack track,
            AssetId<AnchorMarker> assetAnchorId,
            in AnchorDef assetEmbeddedAnchor,
            IAssetRegistry registry,
            bool sampleRandom)
        {
            switch (DetermineCase(in track, assetAnchorId, in assetEmbeddedAnchor))
            {
                case Case.Neither:
                    return AnchorSpawnSpec.FromDef(AnchorDef.WorldDefault);

                case Case.AssetOnly:
                    // [21_anchor_spec.md] §3.3 と同じ優先順位: AnchorId の連鎖 > 埋め込み Anchor。
                    return assetAnchorId.IsValid && registry != null
                        ? AnchorChain.Resolve(registry, assetAnchorId, sampleRandom)
                        : AnchorSpawnSpec.FromDef(assetEmbeddedAnchor);

                case Case.TrackOnly:
                    return AnchorSpawnSpec.FromDef(track.Anchor);

                default: // Both — トラックを親、アセット側を子として合成する。
                    return ComposeBoth(in track, assetAnchorId, in assetEmbeddedAnchor, registry, sampleRandom);
            }
        }

        // トラック Anchor を全体のルート、アセット側(AnchorId の連鎖、または埋め込み 1 段)をその子として
        // AnchorChain.ComposeNodes に通す。leaf→root 順で並べる必要があるため、配列の末尾がトラック Anchor
        // (=全体のルート)になる([08_presentation.md] 実装メモ参照)。
        private static AnchorSpawnSpec ComposeBoth(
            in PresentationTrack track,
            AssetId<AnchorMarker> assetAnchorId,
            in AnchorDef assetEmbeddedAnchor,
            IAssetRegistry registry,
            bool sampleRandom)
        {
            var assetCount = assetAnchorId.IsValid && registry != null
                ? AnchorChain.CollectChainInto(registry, assetAnchorId, AssetChainBuffer)
                : 0;

            var total = 0;
            if (assetCount > 0)
            {
                for (var i = 0; i < assetCount; i++)
                {
                    ComposedNodeBuffer[total++] = AnchorChainNode.FromAnchorData(AssetChainBuffer[i]);
                }

                System.Array.Clear(AssetChainBuffer, 0, assetCount);
            }
            else
            {
                ComposedNodeBuffer[total++] = AnchorChainNode.FromDef(assetEmbeddedAnchor);
            }

            ComposedNodeBuffer[total++] = AnchorChainNode.FromDef(track.Anchor);

            var spec = AnchorChain.ComposeNodes(ComposedNodeBuffer, total, sampleRandom);
            System.Array.Clear(ComposedNodeBuffer, 0, total);
            return spec;
        }

        // アセットのみ(Case.AssetOnly)のとき、解決先(contextRoot)を決める基準に使う Def
        // (連鎖ならその最上段〔ルート〕、無ければ埋め込みそのもの)。Space/Path/FollowRotation/DetachOnStop は
        // ルートの値がそのまま効く(AnchorChain.Compose と同じ規則)。Editor の SceneView 表示専用。
        public static AnchorDef ResolveAssetRootDef(AssetId<AnchorMarker> assetAnchorId, in AnchorDef assetEmbeddedAnchor, IAssetRegistry registry)
        {
            if (!assetAnchorId.IsValid || registry == null)
            {
                return assetEmbeddedAnchor;
            }

            var count = AnchorChain.CollectChainInto(registry, assetAnchorId, AssetChainBuffer);
            if (count == 0)
            {
                return assetEmbeddedAnchor;
            }

            var rootDef = AssetChainBuffer[count - 1].ToDef();
            System.Array.Clear(AssetChainBuffer, 0, count);
            return rootDef;
        }

        // ── Editor の SceneView 表示専用: 合成に使った段の一覧(ルート→末端) ──

        public readonly struct Stage
        {
            public readonly string Label;
            public readonly AnchorDef ComposedDef; // ルートからこの段までを合成した Def(ランダム無し)
            public readonly float PositionJitterRadius; // このノード自身のジッター半径(表示用。0=無し)

            public Stage(string label, AnchorDef composedDef, float positionJitterRadius)
            {
                Label = label;
                ComposedDef = composedDef;
                PositionJitterRadius = positionJitterRadius;
            }
        }

        public static int ResolveStages(in PresentationTrack track, VfxData data, IAssetRegistry registry, Stage[] buffer)
            => ResolveStages(in track, data != null ? data.AnchorId : default, data != null ? data.Anchor : AnchorDef.WorldDefault, registry, buffer);

        public static int ResolveStages(in PresentationTrack track, SeData data, IAssetRegistry registry, Stage[] buffer)
            => ResolveStages(in track, data != null ? data.AnchorId : default, data != null ? data.Anchor : AnchorDef.WorldDefault, registry, buffer);

        // buffer は呼び出し側で MaxStages 分用意すること(Editor 専用、定常経路ではないため簡易な確保で良い)。
        public static int ResolveStages(
            in PresentationTrack track,
            AssetId<AnchorMarker> assetAnchorId,
            in AnchorDef assetEmbeddedAnchor,
            IAssetRegistry registry,
            Stage[] buffer)
        {
            switch (DetermineCase(in track, assetAnchorId, in assetEmbeddedAnchor))
            {
                case Case.Neither:
                    buffer[0] = new Stage("(未設定: ワールド原点)", AnchorDef.WorldDefault, 0f);
                    return 1;

                case Case.AssetOnly:
                    return ResolveAssetOnlyStages(assetAnchorId, in assetEmbeddedAnchor, registry, buffer);

                case Case.TrackOnly:
                    buffer[0] = new Stage("トラック Anchor", track.Anchor, 0f);
                    return 1;

                default: // Both
                    return ResolveComposedStages(in track, assetAnchorId, in assetEmbeddedAnchor, registry, buffer);
            }
        }

        private static int ResolveAssetOnlyStages(AssetId<AnchorMarker> assetAnchorId, in AnchorDef embedded, IAssetRegistry registry, Stage[] buffer)
        {
            var count = assetAnchorId.IsValid && registry != null
                ? AnchorChain.CollectChainInto(registry, assetAnchorId, AssetChainBuffer)
                : 0;

            if (count == 0)
            {
                buffer[0] = new Stage("埋め込み Anchor", embedded, 0f);
                return 1;
            }

            // AssetChainBuffer は leaf(0)→root(count-1)。root→leaf(表示順)へ読み替えて各段までの
            // 合成値を積む(AnchorChainEditor.ComposeUpTo と同じ考え方。Editor 専用のため小さな配列
            // コピーは許容する)。
            for (var s = 0; s < count; s++)
            {
                var stageNodeCount = s + 1;
                var startIndex = count - stageNodeCount;
                var slice = new AnchorData[stageNodeCount];
                System.Array.Copy(AssetChainBuffer, startIndex, slice, 0, stageNodeCount);
                var stageSpec = AnchorChain.Compose(slice, stageNodeCount, sampleRandom: false);
                var node = AssetChainBuffer[startIndex];
                buffer[s] = new Stage(node.name, stageSpec.Def, node.PositionJitterRadius);
            }

            System.Array.Clear(AssetChainBuffer, 0, count);
            return count;
        }

        private static int ResolveComposedStages(
            in PresentationTrack track,
            AssetId<AnchorMarker> assetAnchorId,
            in AnchorDef assetEmbeddedAnchor,
            IAssetRegistry registry,
            Stage[] buffer)
        {
            int assetCount;
            AnchorChainNode[] assetNodesLeafToRoot;
            string[] assetLabels;

            var chainCount = assetAnchorId.IsValid && registry != null
                ? AnchorChain.CollectChainInto(registry, assetAnchorId, AssetChainBuffer)
                : 0;

            if (chainCount > 0)
            {
                assetCount = chainCount;
                assetNodesLeafToRoot = new AnchorChainNode[assetCount];
                assetLabels = new string[assetCount];
                for (var i = 0; i < assetCount; i++)
                {
                    assetNodesLeafToRoot[i] = AnchorChainNode.FromAnchorData(AssetChainBuffer[i]);
                    assetLabels[i] = AssetChainBuffer[i].name;
                }

                System.Array.Clear(AssetChainBuffer, 0, assetCount);
            }
            else
            {
                assetCount = 1;
                assetNodesLeafToRoot = new[] { AnchorChainNode.FromDef(assetEmbeddedAnchor) };
                assetLabels = new[] { "埋め込み Anchor" };
            }

            // 全体(leaf→root) = アセット側(leaf→root) + トラック Anchor(全体のルート、末尾)。
            var total = assetCount + 1;
            var fullLeafToRoot = new AnchorChainNode[total];
            System.Array.Copy(assetNodesLeafToRoot, fullLeafToRoot, assetCount);
            fullLeafToRoot[assetCount] = AnchorChainNode.FromDef(track.Anchor);

            buffer[0] = new Stage("トラック Anchor", track.Anchor, 0f);

            for (var s = 1; s < total; s++)
            {
                var stageNodeCount = s + 1;
                var startIndex = total - stageNodeCount;
                var slice = new AnchorChainNode[stageNodeCount];
                System.Array.Copy(fullLeafToRoot, startIndex, slice, 0, stageNodeCount);
                var stageSpec = AnchorChain.ComposeNodes(slice, stageNodeCount, sampleRandom: false);
                var assetIndex = startIndex; // fullLeafToRoot と assetNodesLeafToRoot は leaf 側が揃っている
                buffer[s] = new Stage(assetLabels[assetIndex], stageSpec.Def, assetNodesLeafToRoot[assetIndex].PositionJitterRadius);
            }

            return total;
        }
    }
}
