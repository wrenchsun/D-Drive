using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Foundation.Extension
{
    // [1014_extension_platform.md] §2.1(X-1、2026-10-16) — IAssetKindProvider の一覧。
    // 発見は AppDomain の全アセンブリ走査(CI.DiscoverValidators / AssetIdGenerator.FindDefinitions と同じ流儀。
    // Foundation は UnityEditor を参照できないので TypeCache は使わない)。結果はキャッシュし、ドメインリロードで作り直される。
    // KindKey の重複は警告 1 回 + 先勝ち(型の完全名の昇順で先のもの)。
    public static class AssetKindRegistry
    {
        private static List<IAssetKindProvider> _providers;
        private static readonly HashSet<string> WarnedDuplicates = new(StringComparer.Ordinal);

        // テスト専用アセンブリ(DDrive.Tests* / ExternalContract.Tests*)の provider も含めるか。
        // 既定 false: テストが置くダミー種別を実際の AssetBrowser・生成物に載せないため。契約テストだけが true にして Refresh する。
        public static bool IncludeTestAssemblies { get; set; }

        public static bool IsTestAssemblyName(string name)
            => name != null
               && (name.StartsWith("DDrive.Tests", StringComparison.Ordinal)
                   || name.StartsWith("ExternalContract.Tests", StringComparison.Ordinal));

        // Order 昇順(同じなら KindKey の辞書順)。
        public static IReadOnlyList<IAssetKindProvider> All
        {
            get
            {
                _providers ??= Discover();
                return _providers;
            }
        }

        // 発見し直す(IncludeTestAssemblies を変えたとき・テスト用)。
        public static void Refresh()
        {
            _providers = null;
            WarnedDuplicates.Clear();
        }

        public static bool TryGet(string kindKey, out IAssetKindProvider provider)
        {
            provider = null;
            if (string.IsNullOrEmpty(kindKey))
            {
                return false;
            }

            var all = All;
            for (var i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].KindKey, kindKey, StringComparison.Ordinal))
                {
                    provider = all[i];
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetByDataType(Type dataType, out IAssetKindProvider provider)
        {
            provider = null;
            if (dataType == null)
            {
                return false;
            }

            var all = All;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].DataType == dataType)
                {
                    provider = all[i];
                    return true;
                }
            }

            return false;
        }

        // Data 型の [AssetIdDefinition] に書かれた KindKey(無ければ空)。
        public static string KindKeyOf(Type dataType)
            => dataType?.GetCustomAttribute<AssetIdDefinitionAttribute>(false)?.KindKey ?? string.Empty;

        // いずれかの provider の接頭辞か(ID 定数名から接頭辞を落とす判定用)。
        public static bool IsKnownPrefix(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            var all = All;
            for (var i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Prefix, token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // AssetBrowser のフィルタ・一覧に出す種別名。Extension は provider の DisplayName(provider が無ければ KindKey)、
        // 組み込み種別は enum 名。
        public static string LabelOf(AssetType type, string kindKey)
        {
            if (type != AssetType.Extension)
            {
                return type.ToString();
            }

            if (TryGet(kindKey, out var provider) && !string.IsNullOrEmpty(provider.DisplayName))
            {
                return provider.DisplayName;
            }

            return string.IsNullOrEmpty(kindKey) ? type.ToString() : kindKey;
        }

        private static bool IsBuiltInTypeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (var builtIn in Enum.GetNames(typeof(AssetType)))
            {
                if (string.Equals(builtIn, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<IAssetKindProvider> Discover()
        {
            var candidates = new List<(string typeName, IAssetKindProvider provider)>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IncludeTestAssemblies && IsTestAssemblyName(asm.GetName().Name))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null || !t.IsClass || t.IsAbstract || t.IsGenericTypeDefinition || !t.IsVisible
                        || !typeof(IAssetKindProvider).IsAssignableFrom(t)
                        || t.GetConstructor(Type.EmptyTypes) == null)
                    {
                        continue;
                    }

                    try
                    {
                        candidates.Add((t.FullName, (IAssetKindProvider)Activator.CreateInstance(t)));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[DDrive] IAssetKindProvider '{t.FullName}' could not be created and was skipped: {e.Message}");
                    }
                }
            }

            candidates.Sort((a, b) => string.CompareOrdinal(a.typeName, b.typeName));

            var result = new List<IAssetKindProvider>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            var seenDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (typeName, provider) in candidates)
            {
                var key = provider.KindKey;
                if (string.IsNullOrEmpty(key))
                {
                    Debug.LogWarning($"[DDrive] IAssetKindProvider '{typeName}' has an empty KindKey and was skipped.");
                    continue;
                }

                if (seen.TryGetValue(key, out var firstType))
                {
                    if (WarnedDuplicates.Add(key))
                    {
                        Debug.LogWarning($"[DDrive] Duplicate KindKey '{key}': '{typeName}' was ignored ('{firstType}' wins).");
                    }

                    continue;
                }

                // X-R-05: 組み込みの種別名(AssetType の名前。None / Extension を含む。大小無視)と衝突する KindKey / DisplayName は
                // 登録しない(MCP の scope や AssetBrowser のフィルタで組み込みの種別と取り違えるため)。DisplayName の重複も先勝ち。
                if (IsBuiltInTypeName(key) || IsBuiltInTypeName(provider.DisplayName))
                {
                    Debug.LogWarning($"[DDrive] IAssetKindProvider '{typeName}': KindKey '{key}' / DisplayName '{provider.DisplayName}' collides with a built-in AssetType name and was ignored.");
                    continue;
                }

                var display = string.IsNullOrEmpty(provider.DisplayName) ? key : provider.DisplayName;
                if (seenDisplayNames.TryGetValue(display, out var displayOwner))
                {
                    Debug.LogWarning($"[DDrive] IAssetKindProvider '{typeName}': DisplayName '{display}' is already used by '{displayOwner}' and was ignored.");
                    continue;
                }

                seenDisplayNames[display] = typeName;
                seen[key] = typeName;
                result.Add(provider);
            }

            result.Sort((a, b) =>
            {
                var c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : string.CompareOrdinal(a.KindKey, b.KindKey);
            });
            return result;
        }
    }
}
