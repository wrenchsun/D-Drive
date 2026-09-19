using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Anchoring
{
    // [22_anchor_group.md] §3.6
    public sealed class AnchorGroupDataValidator : IValidator
    {
        private static readonly AnchorLayoutPoint[] Buffer = new AnchorLayoutPoint[AnchorGroupData.MaxPoints];

        public AssetType Target => AssetType.AnchorGroup;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not AnchorGroupData group)
            {
                yield break;
            }

            var count = AnchorLayout.Generate(group, Buffer, sampleRandom: false);
            if (count == 0)
            {
                yield return ValidationResult.Error("点が 1 つもありません(パターンの数か手置きの点を設定してください)");
            }
            else if (count >= AnchorGroupData.MaxPoints)
            {
                yield return ValidationResult.Warning($"点が {AnchorGroupData.MaxPoints} 個で打ち切られています");
            }

            if (!group.OriginAnchorId.IsValid)
            {
                var needsPath = group.Origin.Space is AnchorSpace.BoneName or AnchorSpace.NamedObject;
                if (needsPath && string.IsNullOrEmpty(group.Origin.Path))
                {
                    yield return ValidationResult.Error("原点の Space=BoneName/NamedObject ですが Path が未設定です");
                }
            }

            var hasShared = HasAny(group.SharedVfx) || HasAny(group.SharedSe);
            var hasOverrideAssets = false;
            if (group.Overrides != null)
            {
                foreach (var o in group.Overrides)
                {
                    if (o.Index < 0 || o.Index >= count)
                    {
                        yield return ValidationResult.Warning($"Overrides の Index {o.Index} は点の範囲(0〜{count - 1})外です");
                    }

                    hasOverrideAssets |= HasAny(o.Vfx) || HasAny(o.Se);
                }
            }

            var hasChildren = false;
            if (group.Children != null)
            {
                foreach (var child in group.Children)
                {
                    if (!child.Group.IsValid)
                    {
                        continue;
                    }

                    hasChildren = true;
                    if (child.Group.Value == group.Id)
                    {
                        yield return ValidationResult.Error("Children に自分自身が入っています(循環)");
                    }
                    else if (ReachesSelf(ctx, child.Group.Value, group.Id, 0))
                    {
                        yield return ValidationResult.Error("Children が循環しています(子をたどると自分に戻る)");
                    }

                    if (child.AtIndex >= count)
                    {
                        yield return ValidationResult.Warning($"Children の AtIndex {child.AtIndex} は点の範囲外です");
                    }
                }
            }

            if (!hasShared && !hasOverrideAssets && !hasChildren)
            {
                yield return ValidationResult.Warning("出すもの(SharedVfx / SharedSe / Overrides / Children)が登録されていません");
            }

            if (group.Layout == AnchorLayoutKind.Circle && group.CircleCount > 1 && group.CircleRadius <= 0f)
            {
                yield return ValidationResult.Warning("Circle の半径が 0 のため全点が原点に重なります");
            }

            if (group.Layout == AnchorLayoutKind.Line && group.LineCount > 1 && group.LineLength <= 0f)
            {
                yield return ValidationResult.Warning("Line の長さが 0 のため全点が原点に重なります");
            }

            if (group.ChancePerPoint <= 0f)
            {
                yield return ValidationResult.Warning("ChancePerPoint が 0 のため何も生成されません");
            }
        }

        private static bool HasAny<T>(AssetId<T>[] ids)
        {
            if (ids == null)
            {
                return false;
            }

            foreach (var id in ids)
            {
                if (id.IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReachesSelf(ValidationContext ctx, ulong childId, ulong selfId, int depth)
        {
            if (depth > AnchorGroupPlanner.MaxDepth)
            {
                return false;
            }

            var all = ctx.AllAssets;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] is not AnchorGroupData g || g.Id != childId || g.Children == null)
                {
                    continue;
                }

                foreach (var c in g.Children)
                {
                    if (!c.Group.IsValid)
                    {
                        continue;
                    }

                    if (c.Group.Value == selfId || ReachesSelf(ctx, c.Group.Value, selfId, depth + 1))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
