using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Compat
{
    // [42_distribution.md] §5.1 / §5.11-2(P-3、2026-09-20) — 全 AssetDataBase 派生型(具象)の
    // SerializedObject フィールド一覧(名前・型・配列/構造体の入れ子)のゴールデンを作る。
    //
    // `.asset` として実際に書き出されるレイアウトを最も忠実に反映するため、C# のリフレクションではなく
    // Unity の SerializedObject/SerializedProperty を使う(配列の要素・構造体の子プロパティが
    // NextVisible(true) で自動的に展開される)。既存の DataEditorRegistryTests と同じ「TypeCache で
    // AssetDataBase 派生 + DDrive.* アセンブリ(DDrive.Tests.* を除く)」の絞り込みを流用する。
    public static class SerializedLayoutSnapshotBuilder
    {
        public static string Build()
        {
            var lines = new List<string>();

            foreach (var type in ConcreteDataTypes())
            {
                ScriptableObject instance;
                try
                {
                    instance = ScriptableObject.CreateInstance(type);
                }
                catch (Exception)
                {
                    // OnEnable 等で例外を投げる型は対象外にする(CLAUDE.md §0-4: 例外で止めない)。
                    continue;
                }

                try
                {
                    var so = new SerializedObject(instance);
                    var prop = so.GetIterator();
                    var entered = prop.NextVisible(true);
                    while (entered)
                    {
                        lines.Add($"{type.FullName}::{prop.propertyPath} : {prop.propertyType}");
                        entered = prop.NextVisible(false);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            // X-R-21(2026-10-16): AssetCatalog の CatalogEntry の欄(KindKey ほか)。AssetDataBase 派生ではないので別枠で、
            // 配列の要素の欄が出るよう 1 件入れたインスタンスを調べる。追加のみ(欄の削除・型変更は MAJOR)。
            AppendCatalogLayout(lines);

            lines.Sort(StringComparer.Ordinal);

            var sb = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }

        private static void AppendCatalogLayout(List<string> lines)
        {
            ScriptableObject instance = null;
            try
            {
                var catalog = ScriptableObject.CreateInstance<DDrive.Foundation.Registry.AssetCatalog>();
                instance = catalog;
                catalog.AddOrUpdate(new DDrive.Foundation.Registry.CatalogEntry { Id = 1, Address = "A" });
                var so = new SerializedObject(catalog);
                var prop = so.GetIterator();
                var entered = prop.NextVisible(true);
                while (entered)
                {
                    lines.Add($"{typeof(DDrive.Foundation.Registry.AssetCatalog).FullName}::{prop.propertyPath} : {prop.propertyType}");
                    entered = prop.NextVisible(true);
                }
            }
            catch (Exception)
            {
                // 例外で止めない(スナップショット比較が差分として検出する)。
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        public static IEnumerable<Type> ConcreteDataTypes()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<AssetDataBase>())
            {
                if (type.IsAbstract)
                {
                    continue;
                }

                var assembly = type.Assembly.GetName().Name;
                if (!assembly.StartsWith("DDrive.", StringComparison.Ordinal) || assembly.StartsWith("DDrive.Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return type;
            }
        }
    }
}
