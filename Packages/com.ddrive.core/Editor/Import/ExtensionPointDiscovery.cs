using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Import
{
    // [51_tdrive_integration.md] §4.7(FC-6)/§4.15(FC-14) — 取り込みまわりの外部拡張点(IImportRuleHandler /
    // IImportRuleFolderOptOut / IShaderConversionTableProvider / ITextureImportRuleProvider)に共通の発見規則。
    // FC-5 の CutsceneImportListeners.Discover と同じ規則: TypeCache で派生型を集め、public・非 abstract・
    // 引数なしコンストラクタを持つ型だけをインスタンス化する。`DDrive.Tests*` で始まるアセンブリの実装は除外する
    // (CI.DiscoverValidators / DDriveMigrationRunner と同じ。D-Drive 自身のテスト用ダミーが実運用に混ざらない)。
    // 並びは型のフルネーム(序数比較)順で決定的。コンストラクタの例外は隔離する(Debug.LogException + 継続)。
    // 結果のキャッシュは呼び出し側が持つ(取り込みのたびに TypeCache 走査 + インスタンス生成をしないため)。
    internal static class ExtensionPointDiscovery
    {
        internal static List<T> Instantiate<T>(Func<Type, bool> exclude = null) where T : class
        {
            var types = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || !type.IsPublic && !type.IsNestedPublic)
                {
                    continue;
                }

                if (exclude != null && exclude(type))
                {
                    continue;
                }

                if (type.Assembly.GetName().Name.StartsWith("DDrive.Tests", StringComparison.Ordinal))
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
