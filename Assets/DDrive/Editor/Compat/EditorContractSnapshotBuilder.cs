using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;

namespace DDrive.Editor.Compat
{
    // [42_distribution.md] §5.9 / §5.11-8(P-3、2026-09-20) — 「弱い互換面」(人の手順・マニュアル・CI
    // スクリプトが依存するもの)を固定する。`DDriveMenu` の public const(メニューパス文字列)、
    // `CI` の public static メソッド名(`-executeMethod` のエントリ)、`[DataEditor]` 対応表
    // (DataEditorRegistryTests とは別に「どの Data がどのウィンドウ・ラベル・並び順で開くか」まで
    // ゴールデン化する)。
    public static class EditorContractSnapshotBuilder
    {
        public static string Build()
        {
            var sb = new System.Text.StringBuilder();

            sb.Append("== DDriveMenu ==\n");
            foreach (var (name, value) in PublicConstStrings(typeof(DDriveMenu)))
            {
                sb.Append(name).Append(" = ").Append(value).Append('\n');
            }

            sb.Append("== CI ==\n");
            var methodNames = typeof(DDrive.Editor.CI)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal);
            foreach (var name in methodNames)
            {
                sb.Append(name).Append('\n');
            }

            sb.Append("== DataEditor ==\n");
            var entries = new List<string>();
            foreach (var dataType in DataEditorRegistry.RegisteredDataTypes)
            {
                foreach (var entry in DataEditorRegistry.GetEntries(dataType))
                {
                    entries.Add($"{dataType.FullName} -> {entry.WindowType.FullName} \"{entry.Label}\" order={entry.Order}");
                }
            }

            entries.Sort(StringComparer.Ordinal);
            foreach (var line in entries)
            {
                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }

        private static IEnumerable<(string name, string value)> PublicConstStrings(Type type)
        {
            var list = new List<(string, string)>();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                {
                    list.Add((field.Name, (string)field.GetRawConstantValue()));
                }
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list;
        }
    }
}
