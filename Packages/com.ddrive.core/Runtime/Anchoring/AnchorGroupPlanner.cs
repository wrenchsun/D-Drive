using System.Collections.Generic;
using DDrive.Foundation.Registry;
using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    public enum AnchorGroupActionKind
    {
        Vfx,
        Se,
    }

    // 「どの点に・何を・どの姿勢で出すか」の 1 件。ランタイム(AnchorGroupPlayer)もエディタ(プレビュー)も同じ計画を実行する。
    public struct AnchorGroupAction
    {
        public AnchorGroupActionKind Kind;
        public ulong AssetId;          // VfxData / SeData の ID
        public int PointIndex;         // 生成後の通し番号(入れ子の子は親の点番号)
        public int Depth;              // 0 = 対象の配置セット自身、1 以上 = 入れ子の子
        public AnchorSpawnSpec Spec;   // 合成済み姿勢 + ランダム/ディレイ/確率
    }

    // [22_anchor_group.md] §3.3 — 配置セットを展開して再生計画(アクション列)を作る。
    // 原点は AnchorChain(Anchor アセット)または埋め込み AnchorDef。各点は AnchorLayout で生成し、
    // 上書き(Skip/差し替え)と入れ子(子の配置セット)を解決する。出力は呼び出し側のリストに追記する。
    public static class AnchorGroupPlanner
    {
        public const int MaxDepth = 4;

        private static readonly AnchorLayoutPoint[][] Buffers = new AnchorLayoutPoint[MaxDepth + 1][];

        // 戻り値: 生成した点数(対象の配置セット自身の分)。actions には入れ子分も含めて追記される。
        public static int Plan(IAssetRegistry registry, AnchorGroupData group, bool sampleRandom, List<AnchorGroupAction> actions)
        {
            if (group == null || actions == null)
            {
                return 0;
            }

            var origin = group.OriginAnchorId.IsValid
                ? AnchorChain.Resolve(registry, group.OriginAnchorId, sampleRandom)
                : AnchorSpawnSpec.FromDef(group.Origin);

            return PlanInternal(registry, group, origin, sampleRandom, actions, depth: 0, parentPointIndex: -1);
        }

        // 各点の静的な姿勢だけを列挙する(エディタのギズモ用。ランダム無し・入れ子無し)。
        public static int EnumeratePoints(IAssetRegistry registry, AnchorGroupData group, AnchorSpawnSpec[] specs)
        {
            if (group == null || specs == null)
            {
                return 0;
            }

            var origin = group.OriginAnchorId.IsValid && registry != null
                ? AnchorChain.Resolve(registry, group.OriginAnchorId, sampleRandom: false)
                : AnchorSpawnSpec.FromDef(group.Origin);

            var buffer = GetBuffer(0);
            var count = Mathf.Min(AnchorLayout.Generate(group, buffer, sampleRandom: false), specs.Length);
            for (var i = 0; i < count; i++)
            {
                specs[i] = AnchorLayout.ComposePoint(origin, buffer[i], group, sampleRandom: false);
            }

            return count;
        }

        private static int PlanInternal(IAssetRegistry registry, AnchorGroupData group, in AnchorSpawnSpec frame, bool sampleRandom,
            List<AnchorGroupAction> actions, int depth, int parentPointIndex)
        {
            if (depth > MaxDepth)
            {
                Debug.LogWarning($"[DDrive] AnchorGroup '{group.name}' nests deeper than {MaxDepth}; skipped.");
                return 0;
            }

            var buffer = GetBuffer(depth);
            var count = AnchorLayout.Generate(group, buffer, sampleRandom);

            for (var i = 0; i < count; i++)
            {
                var spec = AnchorLayout.ComposePoint(frame, buffer[i], group, sampleRandom);
                var overrideIndex = group.FindOverride(i);
                var pointIndex = depth == 0 ? i : parentPointIndex;

                if (overrideIndex >= 0 && group.Overrides[overrideIndex].Skip)
                {
                    continue;
                }

                AddAssets(group, overrideIndex, pointIndex, depth, spec, actions);

                if (group.Children != null)
                {
                    foreach (var child in group.Children)
                    {
                        if (!child.Group.IsValid || (child.AtIndex >= 0 && child.AtIndex != i))
                        {
                            continue;
                        }

                        var childGroup = registry != null ? registry.ResolveOrPlaceholder<AnchorGroupData>(child.Group.Value) : null;
                        if (childGroup == null || ReferenceEquals(childGroup, group))
                        {
                            continue;
                        }

                        // 子の原点は無視し、この点を基準(frame)にする([22] §3.4)。
                        PlanInternal(registry, childGroup, spec, sampleRandom, actions, depth + 1, pointIndex);
                    }
                }
            }

            return count;
        }

        private static void AddAssets(AnchorGroupData group, int overrideIndex, int pointIndex, int depth, in AnchorSpawnSpec spec, List<AnchorGroupAction> actions)
        {
            // 上書きがあればそのアセット、無ければ共通アセット。上書きに何も入っていなければ共通に戻る。
            var useOverride = overrideIndex >= 0 &&
                              ((group.Overrides[overrideIndex].Vfx != null && group.Overrides[overrideIndex].Vfx.Length > 0) ||
                               (group.Overrides[overrideIndex].Se != null && group.Overrides[overrideIndex].Se.Length > 0));

            var vfx = useOverride ? group.Overrides[overrideIndex].Vfx : group.SharedVfx;
            var se = useOverride ? group.Overrides[overrideIndex].Se : group.SharedSe;

            if (vfx != null)
            {
                foreach (var id in vfx)
                {
                    if (id.IsValid)
                    {
                        actions.Add(new AnchorGroupAction { Kind = AnchorGroupActionKind.Vfx, AssetId = id.Value, PointIndex = pointIndex, Depth = depth, Spec = spec });
                    }
                }
            }

            if (se != null)
            {
                foreach (var id in se)
                {
                    if (id.IsValid)
                    {
                        actions.Add(new AnchorGroupAction { Kind = AnchorGroupActionKind.Se, AssetId = id.Value, PointIndex = pointIndex, Depth = depth, Spec = spec });
                    }
                }
            }
        }

        private static AnchorLayoutPoint[] GetBuffer(int depth)
        {
            var i = Mathf.Clamp(depth, 0, MaxDepth);
            return Buffers[i] ??= new AnchorLayoutPoint[AnchorGroupData.MaxPoints];
        }
    }
}
