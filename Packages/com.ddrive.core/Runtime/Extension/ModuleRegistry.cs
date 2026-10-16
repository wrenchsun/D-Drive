using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Extension;
using UnityEngine;

namespace DDrive.Runtime.Extension
{
    // [1014_extension_platform.md] §2.2(X-2、2026-10-16) — IDDriveModule の一覧。発見の流儀は AssetKindRegistry と同じ
    // (AppDomain の全アセンブリ走査、テストアセンブリは既定で除外、結果はキャッシュ)。
    // Order 昇順、同値は型のフルネーム(序数比較)順。コンストラクタが例外を出した module は警告して飛ばす。
    public static class ModuleRegistry
    {
        private static List<IDDriveModule> _modules;

        // テスト専用アセンブリ(DDrive.Tests* / ExternalContract.Tests*)の module も含めるか。既定 false。
        public static bool IncludeTestAssemblies { get; set; }

        public static IReadOnlyList<IDDriveModule> All => _modules ??= Discover();

        public static void Refresh() => _modules = null;

        private static List<IDDriveModule> Discover()
        {
            var found = new List<(string name, IDDriveModule module)>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IncludeTestAssemblies && AssetKindRegistry.IsTestAssemblyName(asm.GetName().Name))
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
                        || !typeof(IDDriveModule).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null)
                    {
                        continue;
                    }

                    try
                    {
                        found.Add((t.FullName, (IDDriveModule)Activator.CreateInstance(t)));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[DDrive] IDDriveModule '{t.FullName}' could not be created and was skipped: {e.Message}");
                    }
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            // List.Sort は不安定なので、型名順に並べた後に Order で安定に並べ直す(挿入ソート)。
            var result = new List<IDDriveModule>(found.Count);
            foreach (var (_, module) in found)
            {
                var order = SafeOrder(module);
                var i = result.Count;
                while (i > 0 && SafeOrder(result[i - 1]) > order)
                {
                    i--;
                }

                result.Insert(i, module);
            }

            return result;
        }

        private static int SafeOrder(IDDriveModule m)
        {
            try
            {
                return m.Order;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] IDDriveModule '{m.GetType().FullName}'.Order threw and 0 is used: {e.Message}");
                return 0;
            }
        }
    }
}
