using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Extension;
using UnityEngine;

namespace DDrive.Runtime.Extension
{
    // [1014_extension_platform.md] §2.2(X-2、2026-10-16) — IDDriveModule の一覧。発見の流儀は AssetKindRegistry と同じ
    // (AppDomain の全アセンブリ走査、テストアセンブリは既定で除外、型の一覧はキャッシュ)。
    // X-R-01: キャッシュするのは「型」だけ。module のインスタンスは ModuleRunner ごとに CreateInstances() で新しく作る
    // (Bootstrap と EditorModuleHost・複数の Bootstrap が同じインスタンスの状態を取り合わない)。
    // Order 昇順、同値は型のフルネーム(序数比較)順。コンストラクタが例外を出した module は警告して飛ばす。
    // 走査は DDrive.Runtime を参照するアセンブリ(と DDrive.Runtime 自身)だけに絞る(IDDriveModule を実装できるのはそれだけ)。
    public static class ModuleRegistry
    {
        private const string RuntimeAssemblyName = "DDrive.Runtime";

        private static List<Type> _types;

        // テスト専用アセンブリ(DDrive.Tests* / ExternalContract.Tests*)の module も含めるか。既定 false。
        public static bool IncludeTestAssemblies { get; set; }

        // 発見した module の型(型のフルネーム順)。
        public static IReadOnlyList<Type> Types => _types ??= DiscoverTypes();

        // 呼ぶたびに新しいインスタンスを作った一覧(Order 昇順・同値は型名順)。ModuleRunner に渡す用で、状態は共有されない。
        public static IReadOnlyList<IDDriveModule> All => CreateInstances();

        public static void Refresh() => _types = null;

        public static IReadOnlyList<IDDriveModule> CreateInstances()
        {
            var types = Types;
            var instances = new List<IDDriveModule>(types.Count);
            for (var i = 0; i < types.Count; i++)
            {
                try
                {
                    instances.Add((IDDriveModule)Activator.CreateInstance(types[i]));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] IDDriveModule '{types[i].FullName}' could not be created and was skipped: {e.Message}");
                }
            }

            // List.Sort は不安定なので、型名順に並べた後に Order で安定に並べ直す(挿入ソート)。Order は module ごとに 1 回だけ読む。
            var ordered = new List<(IDDriveModule module, int order)>(instances.Count);
            foreach (var module in instances)
            {
                var order = SafeOrder(module);
                var i = ordered.Count;
                while (i > 0 && ordered[i - 1].order > order)
                {
                    i--;
                }

                ordered.Insert(i, (module, order));
            }

            var result = new List<IDDriveModule>(ordered.Count);
            foreach (var (module, _) in ordered)
            {
                result.Add(module);
            }

            return result;
        }

        private static List<Type> DiscoverTypes()
        {
            var found = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IncludeTestAssemblies && AssetKindRegistry.IsTestAssemblyName(asm.GetName().Name))
                {
                    continue;
                }

                if (!MayContainModules(asm))
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
                catch (Exception)
                {
                    continue;
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

                    found.Add(t);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return found;
        }

        // IDDriveModule を実装するには DDrive.Runtime を参照している必要がある(参照していないアセンブリの全型走査を省く)。
        private static bool MayContainModules(Assembly asm)
        {
            try
            {
                if (asm.GetName().Name == RuntimeAssemblyName)
                {
                    return true;
                }

                foreach (var r in asm.GetReferencedAssemblies())
                {
                    if (r.Name == RuntimeAssemblyName)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception)
            {
                return true; // 判定できないときは走査する(取りこぼさない)
            }
        }

        private static int SafeOrder(IDDriveModule m)
        {
            try
            {
                return m.Order;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] IDDriveModule '{m.GetType().FullName}'.Order threw and 0 is used: {e}");
                return 0;
            }
        }
    }
}
