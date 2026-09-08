using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    // [21_anchor_spec.md] §3.7。連鎖(Parent)の循環・深さ・未登録と、効かない設定を検出する。
    public sealed class AnchorDataValidator : IValidator
    {
        public AssetType Target => AssetType.Anchor;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not AnchorData anchor)
            {
                yield break;
            }

            var isChild = anchor.Parent.IsValid;

            if (isChild)
            {
                var parent = FindAnchor(ctx, anchor.Parent.Value);
                if (parent == null)
                {
                    yield return ValidationResult.Error($"Parent(0x{anchor.Parent.Value:X}) が登録されていません");
                }
                else
                {
                    var depth = 1;
                    var cursor = parent;
                    var cyclic = false;
                    while (cursor != null && cursor.Parent.IsValid)
                    {
                        if (ReferenceEquals(cursor, anchor) || cursor.Parent.Value == anchor.Id)
                        {
                            cyclic = true;
                            break;
                        }

                        cursor = FindAnchor(ctx, cursor.Parent.Value);
                        depth++;
                        if (depth >= AnchorChain.MaxDepth)
                        {
                            break;
                        }
                    }

                    if (cyclic)
                    {
                        yield return ValidationResult.Error("Parent が循環しています(自分自身に戻る)");
                    }
                    else if (depth >= AnchorChain.MaxDepth)
                    {
                        yield return ValidationResult.Error($"入れ子が {AnchorChain.MaxDepth} 段を超えています");
                    }
                }

                // 子ではルート専用の項目は効かない。
                if (anchor.Space != AnchorSpace.World || !string.IsNullOrEmpty(anchor.Path))
                {
                    yield return ValidationResult.Warning("子 Anchor では Space/Path は無視されます(ルートの基準が使われます)");
                }

                if (anchor.FollowRotation || anchor.DetachOnStop)
                {
                    yield return ValidationResult.Warning("子 Anchor では FollowRotation/DetachOnStop は無視されます(ルートの値が使われます)");
                }
            }
            else
            {
                var needsPath = anchor.Space is AnchorSpace.BoneName or AnchorSpace.NamedObject;
                if (needsPath && string.IsNullOrEmpty(anchor.Path))
                {
                    yield return ValidationResult.Error("Space=BoneName/NamedObject ですが Path が未設定です");
                }
            }

            if (anchor.SpawnChance <= 0f)
            {
                yield return ValidationResult.Warning("SpawnChance が 0 のため何も生成されません(意図的なら Description に理由を書いてください)");
            }

            if (anchor.DelaySec + anchor.DelayJitterSec > 10f)
            {
                yield return ValidationResult.Warning("生成ディレイが 10 秒を超えています");
            }

            if (anchor.ScaleRange.x < 0f || anchor.ScaleRange.y < 0f)
            {
                yield return ValidationResult.Warning("ScaleRange に負の値があります");
            }
        }

        private static AnchorData FindAnchor(ValidationContext ctx, ulong id)
        {
            var all = ctx.AllAssets;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] is AnchorData a && a.Id == id)
                {
                    return a;
                }
            }

            return null;
        }

        // VfxData / SeData 共通: AnchorId と埋め込み Anchor の両方が設定されている場合の注意。
        public static bool IsEmbeddedAnchorNonDefault(in AnchorDef def)
            => def.Space != AnchorSpace.World || !string.IsNullOrEmpty(def.Path) ||
               def.LocalOffset != Vector3.zero || def.LocalEuler != Vector3.zero ||
               def.FollowRotation || def.DetachOnStop;
    }
}
