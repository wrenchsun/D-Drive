using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEngine;

namespace DDrive.Runtime.Prefab
{
    // [07_canvas_prefab.md] B-4 — プロジェクト内の全 PrefabData から集めた GameplayTags の既知タグ辞書。
    // PrefabDataValidator が Validate のたびに Rebuild し、他アセットのタグとの近似一致(大文字小文字違い /
    // 編集距離1以内)を「タイポの疑い」として警告するために使う。Validator と同じく Runtime asmdef に置く
    // (ModelDataValidator と同じ配置。CI/Editor からの実行が主用途で、ゲームプレイからは通常参照しない)。
    public static class GameplayTagDictionary
    {
        public static readonly HashSet<string> KnownTags = new(StringComparer.Ordinal);

        public static void Rebuild(IReadOnlyList<AssetDataBase> allAssets)
        {
            KnownTags.Clear();
            if (allAssets == null)
            {
                return;
            }

            for (var i = 0; i < allAssets.Count; i++)
            {
                if (allAssets[i] is not PrefabData data || data.GameplayTags == null)
                {
                    continue;
                }

                for (var j = 0; j < data.GameplayTags.Length; j++)
                {
                    var tag = data.GameplayTags[j];
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        KnownTags.Add(tag);
                    }
                }
            }
        }

        // tag 自身の完全一致(ordinal)は対象外にして、既知タグの中から「大文字小文字違い」または
        // 「編集距離1以内(挿入/削除/置換いずれか1回)」の近似一致を探す。見つかった側を match に返す。
        public static bool TryFindNearMiss(string tag, out string match)
        {
            match = null;
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            foreach (var known in KnownTags)
            {
                if (string.Equals(known, tag, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(known, tag, StringComparison.OrdinalIgnoreCase) || IsEditDistanceAtMostOne(known, tag))
                {
                    match = known;
                    return true;
                }
            }

            return false;
        }

        // O(len) の一発判定(フル DP は不要)。長さ差が2以上なら即 false、そうでなければ
        // 先頭から食い違いが出た箇所で「挿入/削除/置換」のどれかを1回だけ許して読み進める。
        private static bool IsEditDistanceAtMostOne(string a, string b)
        {
            var lenA = a.Length;
            var lenB = b.Length;
            if (Math.Abs(lenA - lenB) > 1)
            {
                return false;
            }

            var i = 0;
            var j = 0;
            var edits = 0;
            while (i < lenA && j < lenB)
            {
                if (a[i] == b[j])
                {
                    i++;
                    j++;
                    continue;
                }

                edits++;
                if (edits > 1)
                {
                    return false;
                }

                if (lenA == lenB)
                {
                    i++;
                    j++;
                }
                else if (lenA > lenB)
                {
                    i++;
                }
                else
                {
                    j++;
                }
            }

            if (i < lenA || j < lenB)
            {
                edits++;
            }

            return edits <= 1;
        }
    }

    // [07_canvas_prefab.md] B-4。
    public sealed class PrefabDataValidator : IValidator
    {
        public AssetType Target => AssetType.Prefab;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not PrefabData prefab)
            {
                yield break;
            }

            if (prefab.Prefab == null)
            {
                yield return ValidationResult.Error("Prefab が未設定(または Missing)です");
            }

            if (prefab.CollisionLayer < -1 || prefab.CollisionLayer > 31 ||
                (prefab.CollisionLayer >= 0 && string.IsNullOrEmpty(LayerMask.LayerToName(prefab.CollisionLayer))))
            {
                yield return ValidationResult.Error($"CollisionLayer '{prefab.CollisionLayer}' は未定義のレイヤーです(-1=Prefab のレイヤーを維持、0-31=定義済みレイヤー)");
            }

            if (prefab.Kind == PrefabKind.Projectile && prefab.Flags.Pool.Kind != DDrive.Foundation.Data.PoolPolicyKind.Pooled)
            {
                yield return ValidationResult.Warning("Kind=Projectile ですが Flags.Pool が Pooled ではありません(Pooled(32, 128) を推奨)");
            }

            foreach (var result in ValidateTags(prefab, ctx))
            {
                yield return result;
            }
        }

        private static IEnumerable<ValidationResult> ValidateTags(PrefabData prefab, ValidationContext ctx)
        {
            if (prefab.GameplayTags == null || prefab.GameplayTags.Length == 0)
            {
                yield break;
            }

            GameplayTagDictionary.Rebuild(ctx.AllAssets);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < prefab.GameplayTags.Length; i++)
            {
                var tag = prefab.GameplayTags[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    yield return ValidationResult.Warning($"GameplayTags[{i}] が空白です");
                    continue;
                }

                if (!seen.Add(tag))
                {
                    yield return ValidationResult.Warning($"GameplayTags[{i}] '{tag}' は重複しています");
                    continue;
                }

                if (GameplayTagDictionary.TryFindNearMiss(tag, out var match))
                {
                    yield return ValidationResult.Warning($"GameplayTags[{i}] '{tag}' は既知タグ '{match}' のタイポの可能性があります");
                }
            }
        }
    }
}
