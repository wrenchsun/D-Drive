using System;
using System.Collections.Generic;
using DDrive.Foundation.Extension;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Extension
{
    // [1014_extension_platform.md] §2.4(X-4、2026-10-16) — ISampleProvider / IPreviewSceneProvider / IManualPageProvider の一覧。
    // 発見の流儀は AssetKindRegistry(X-1)/ ModuleRegistry(X-2)と同じ: public(入れ子は外側まで = Type.IsVisible)・非 abstract・
    // ジェネリック定義でない・public 引数なしコンストラクタ、テスト用アセンブリ(DDrive.Tests* / ExternalContract.Tests*)は既定で除外
    // (IncludeTestAssemblies + Refresh で契約テストだけが拾う)、型のフルネーム順、重複(KindKey / ページ key)は警告 1 回 + 先勝ち。
    // Editor なので TypeCache を使う。結果はドメインリロードまでキャッシュ。コンストラクタの例外は隔離(Debug.LogException + 継続)。
    public static class ExtensionProviders
    {
        private static List<ISampleProvider> _samples;
        private static List<IPreviewSceneProvider> _scenes;
        private static List<IManualPageProvider> _manuals;
        private static List<(string key, string path, string title)> _pages;
        private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

        // テスト専用アセンブリの provider も含めるか。既定 false(ダミーを実際のエディターに載せない)。
        public static bool IncludeTestAssemblies { get; set; }

        public static void Refresh()
        {
            _samples = null;
            _scenes = null;
            _manuals = null;
            _pages = null;
            Warned.Clear();
        }

        public static IReadOnlyList<ISampleProvider> Samples => _samples ??= DiscoverUnique<ISampleProvider>(p => p.KindKey, "ISampleProvider");

        public static IReadOnlyList<IPreviewSceneProvider> PreviewScenes => _scenes ??= DiscoverUnique<IPreviewSceneProvider>(p => p.KindKey, "IPreviewSceneProvider");

        public static IReadOnlyList<IManualPageProvider> ManualProviders => _manuals ??= Discover<IManualPageProvider>();

        // 宣言された全ページ(key の重複は先勝ち)。
        public static IReadOnlyList<(string key, string path, string title)> ManualPages => _pages ??= BuildPages();

        public static bool TryGetSample(string kindKey, out ISampleProvider provider)
        {
            provider = null;
            if (string.IsNullOrEmpty(kindKey))
            {
                return false;
            }

            foreach (var p in Samples)
            {
                if (string.Equals(p.KindKey, kindKey, StringComparison.Ordinal))
                {
                    provider = p;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetPreviewScene(string kindKey, out IPreviewSceneProvider provider)
        {
            provider = null;
            if (string.IsNullOrEmpty(kindKey))
            {
                return false;
            }

            foreach (var p in PreviewScenes)
            {
                if (string.Equals(p.KindKey, kindKey, StringComparison.Ordinal))
                {
                    provider = p;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetManualPage(string key, out string path, out string title)
        {
            path = null;
            title = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            foreach (var page in ManualPages)
            {
                if (string.Equals(page.key, key, StringComparison.Ordinal))
                {
                    path = page.path;
                    title = page.title;
                    return true;
                }
            }

            return false;
        }

        private static List<(string key, string path, string title)> BuildPages()
        {
            var result = new List<(string key, string path, string title)>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var provider in ManualProviders)
            {
                IReadOnlyList<(string key, string path, string title)> pages;
                try
                {
                    pages = provider.Pages;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    continue;
                }

                if (pages == null)
                {
                    continue;
                }

                foreach (var page in pages)
                {
                    if (string.IsNullOrEmpty(page.key) || string.IsNullOrEmpty(page.path))
                    {
                        continue;
                    }

                    if (seen.TryGetValue(page.key, out var first))
                    {
                        if (Warned.Add("page:" + page.key))
                        {
                            Debug.LogWarning($"[DDrive] IManualPageProvider: ページ key '{page.key}' が重複しています('{provider.GetType().FullName}' の分は無視、'{first}' が先勝ち)。");
                        }

                        continue;
                    }

                    seen[page.key] = provider.GetType().FullName;
                    result.Add(page);
                }
            }

            return result;
        }

        private static List<T> DiscoverUnique<T>(Func<T, string> keyOf, string label) where T : class
        {
            var all = Discover<T>();
            var result = new List<T>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in all)
            {
                var key = keyOf(p);
                if (string.IsNullOrEmpty(key))
                {
                    Debug.LogWarning($"[DDrive] {label} '{p.GetType().FullName}' has an empty KindKey and was skipped.");
                    continue;
                }

                if (seen.TryGetValue(key, out var first))
                {
                    if (Warned.Add(label + ":" + key))
                    {
                        Debug.LogWarning($"[DDrive] Duplicate {label} KindKey '{key}': '{p.GetType().FullName}' was ignored ('{first}' wins).");
                    }

                    continue;
                }

                seen[key] = p.GetType().FullName;
                result.Add(p);
            }

            return result;
        }

        private static List<T> Discover<T>() where T : class
        {
            var types = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || !type.IsVisible)
                {
                    continue;
                }

                if (!IncludeTestAssemblies && AssetKindRegistry.IsTestAssemblyName(type.Assembly.GetName().Name))
                {
                    continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                types.Add(type);
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            var list = new List<T>(types.Count);
            foreach (var type in types)
            {
                try
                {
                    if (Activator.CreateInstance(type) is T instance)
                    {
                        list.Add(instance);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return list;
        }
    }
}
