using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DDrive.Editor.Cutscene;
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

            // FC-5(2026-10-03、docs/42 §5.9 / §5.14): 外部パッケージ(T-Drive 等)が実装・参照する取り込みリスナー API。
            // 追加(メンバー・enum 値の末尾追加)は MINOR、削除・改名・型変更は互換違反(CHANGELOG 必須)。
            sb.Append("== CutsceneImportListener ==\n");
            AppendType(sb, typeof(ICutsceneImportListener));
            AppendType(sb, typeof(CutsceneImportResult));
            AppendType(sb, typeof(CutsceneImportRole));
            AppendType(sb, typeof(CutsceneImportRoleKind));

            return sb.ToString();
        }

        // 型の public な公開面(フィールド / プロパティ / メソッド / enum 値)を 1 行ずつ。並びは名前順。
        private static void AppendType(System.Text.StringBuilder sb, Type type)
        {
            var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : "class";
            sb.Append(kind).Append(' ').Append(type.FullName).Append('\n');

            var lines = new List<string>();
            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type))
                {
                    lines.Add($"  {name} = {Convert.ToInt64(Enum.Parse(type, name))}");
                }

                // enum は値の並び(宣言順 = 値順)も契約なので並べ替えずそのまま出す。
                foreach (var line in lines)
                {
                    sb.Append(line).Append('\n');
                }

                return;
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var f in type.GetFields(Flags))
            {
                lines.Add($"  field {f.FieldType.Name} {f.Name}");
            }

            foreach (var p in type.GetProperties(Flags))
            {
                lines.Add($"  property {p.PropertyType.Name} {p.Name} {{{(p.CanRead ? " get;" : string.Empty)}{(p.CanWrite ? " set;" : string.Empty)} }}");
            }

            foreach (var m in type.GetMethods(Flags).Where(m => !m.IsSpecialName))
            {
                var args = string.Join(", ", m.GetParameters().Select(a => a.ParameterType.Name + " " + a.Name));
                lines.Add($"  method {m.ReturnType.Name} {m.Name}({args})");
            }

            lines.Sort(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                sb.Append(line).Append('\n');
            }
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
