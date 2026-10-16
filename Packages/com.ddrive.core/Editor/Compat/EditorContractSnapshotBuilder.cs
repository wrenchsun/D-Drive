using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DDrive.Editor.Cutscene;
using DDrive.Editor.Import;
using DDrive.Editor.Materials;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Validation;

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
                    // X-3(2026-10-16): テスト用アセンブリの窓(ExternalContract の UxEditorWindow<T> 派生など)は契約に含めない。
                    if (DDrive.Foundation.Extension.AssetKindRegistry.IsTestAssemblyName(entry.WindowType.Assembly.GetName().Name))
                    {
                        continue;
                    }

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

            // FC-6 / FC-14(2026-10-03、docs/42 §5.9 / §5.14 E-21・E-22): 取り込みまわりの外部拡張点。
            // ImportRule のハンドラ(IImportRuleHandler。FC-6 で外部アセンブリから登録可能になり外部が実装する)と
            // 種別フォルダ宣言、変換表・テクスチャ規則の提供口と、提供口が組み立てる規則の型。追加のみ。
            sb.Append("== ImportExtension ==\n");
            AppendType(sb, typeof(IImportRuleHandler));
            AppendType(sb, typeof(IImportRuleFolderOptOut));
            AppendType(sb, typeof(IShaderConversionTableProvider));
            AppendType(sb, typeof(ITextureImportRuleProvider));
            AppendType(sb, typeof(TextureImportProfile.Rule));
            AppendType(sb, typeof(TextureImportProfile.MatchKind));

            // Q-4(2026-10-06、docs/42 §5.9 / §5.14 E-23): 実行順の検査(契約 G-1)から「カメラを読むだけ」の型を外す拡張点。追加のみ。
            sb.Append("== CameraExecutionOrderExemption ==\n");
            AppendType(sb, typeof(ICameraExecutionOrderExemptionProvider));
            AppendType(sb, typeof(CameraExecutionOrderExemption));

            // X-3(2026-10-16、docs/42 §5.9 / §5.14 E-27、docs/1014 §2.3): 持ち込み先が継承・配置する専用エディター基盤。
            // public と protected の全メンバー(抽象メンバー・virtual の口を含む)を固定する。追加のみ(削除・改名・型変更・
            // virtual → abstract・既存メンバーの引数変更は MAJOR)。
            sb.Append("== UxPublicApi ==\n");
            var uxTypes = typeof(DDriveMenu).Assembly.GetExportedTypes()
                .Where(t => t.Namespace == "DDrive.Editor.Ux")
                .OrderBy(t => t.FullName, StringComparer.Ordinal);
            foreach (var t in uxTypes)
            {
                AppendApiType(sb, t);
            }

            // X-3: 確認用シーンで実 Manager を駆動する基底と Editor プレビューの module 口。
            sb.Append("== PreviewApi ==\n");
            AppendApiType(sb, typeof(DDrive.Editor.Preview.UxPreviewDriverBase));
            AppendApiType(sb, typeof(DDrive.Editor.Preview.PreviewResetReason));
            AppendApiType(sb, typeof(DDrive.Editor.Preview.EditorModuleHost));

            // X-4(2026-10-16、docs/42 §5.14 E-28): 空状態のサンプル・確認用シーン・マニュアルページの拡張点。追加のみ。
            sb.Append("== ExtensionProviders ==\n");
            AppendApiType(sb, typeof(DDrive.Editor.Extension.ISampleProvider));
            AppendApiType(sb, typeof(DDrive.Editor.Extension.IPreviewSceneProvider));
            AppendApiType(sb, typeof(DDrive.Editor.Extension.IManualPageProvider));
            AppendApiType(sb, typeof(DDrive.Editor.Extension.ExtensionProviders));
            AppendApiType(sb, typeof(DDrive.Editor.Extension.ExtensionProviderActions));

            return sb.ToString();
        }

        // public / protected の全メンバーを 1 行ずつ(static / abstract / virtual / override の区別つき)。並びは名前順(enum は宣言順)。
        private static void AppendApiType(System.Text.StringBuilder sb, Type type)
        {
            var kind = type.IsEnum ? "enum"
                : type.IsInterface ? "interface"
                : type.IsValueType ? "struct"
                : type.IsAbstract && type.IsSealed ? "static class"
                : type.IsAbstract ? "abstract class"
                : type.IsSealed ? "sealed class"
                : "class";
            var header = kind + " " + TypeNameFormatter.Format(type);
            if (!type.IsEnum && !type.IsInterface && type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType))
            {
                header += " : " + TypeNameFormatter.Format(type.BaseType);
            }

            sb.Append(header).Append('\n');

            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type))
                {
                    sb.Append($"  {name} = {Convert.ToInt64(Enum.Parse(type, name))}\n");
                }

                return;
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var lines = new List<string>();

            foreach (var i in type.GetInterfaces())
            {
                if (type.BaseType == null || !i.IsAssignableFrom(type.BaseType))
                {
                    lines.Add("  implements " + TypeNameFormatter.Format(i));
                }
            }

            foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (IsApiVisible(c.IsPublic, c.IsFamily || c.IsFamilyOrAssembly))
                {
                    lines.Add($"  {Access(c.IsPublic)} ctor({ParamList(c.GetParameters())})");
                }
            }

            foreach (var f in type.GetFields(Flags))
            {
                if (IsApiVisible(f.IsPublic, f.IsFamily || f.IsFamilyOrAssembly))
                {
                    var mod = f.IsLiteral ? "const " : (f.IsStatic ? "static " : string.Empty) + (f.IsInitOnly ? "readonly " : string.Empty);
                    lines.Add($"  {Access(f.IsPublic)} field {mod}{TypeNameFormatter.Format(f.FieldType)} {f.Name}");
                }
            }

            foreach (var p in type.GetProperties(Flags))
            {
                var get = p.GetMethod;
                var set = p.SetMethod;
                var vis = (get != null && IsApiVisible(get.IsPublic, get.IsFamily || get.IsFamilyOrAssembly)) || (set != null && IsApiVisible(set.IsPublic, set.IsFamily || set.IsFamilyOrAssembly));
                if (!vis)
                {
                    continue;
                }

                var m = get ?? set;
                var pub = (get != null && get.IsPublic) || (set != null && set.IsPublic);
                lines.Add($"  {Access(pub)} property {MethodMods(m)}{TypeNameFormatter.Format(p.PropertyType)} {p.Name} {{{(get != null && IsApiVisible(get.IsPublic, get.IsFamily || get.IsFamilyOrAssembly) ? " get;" : string.Empty)}{(set != null && IsApiVisible(set.IsPublic, set.IsFamily || set.IsFamilyOrAssembly) ? " set;" : string.Empty)} }}");
            }

            foreach (var m in type.GetMethods(Flags).Where(m => !m.IsSpecialName))
            {
                if (IsApiVisible(m.IsPublic, m.IsFamily || m.IsFamilyOrAssembly))
                {
                    lines.Add($"  {Access(m.IsPublic)} method {MethodMods(m)}{TypeNameFormatter.Format(m.ReturnType)} {m.Name}{GenericArgs(m)}({ParamList(m.GetParameters())})");
                }
            }

            lines.Sort(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                sb.Append(line).Append('\n');
            }
        }

        private static bool IsApiVisible(bool isPublic, bool isFamily) => isPublic || isFamily;

        private static string Access(bool isPublic) => isPublic ? "public" : "protected";

        // X-R-08(2026-10-16): 型は名前空間つき・型引数つき(Func<Values> → Func<float> の変更を検出できる)、ref / out / in の区別、既定値を出す。
        private static string ParamList(ParameterInfo[] ps) => string.Join(", ", ps.Select(FormatParam));

        private static string GenericArgs(MethodInfo m)
            => m.IsGenericMethodDefinition ? "<" + string.Join(",", m.GetGenericArguments().Select(a => a.Name)) + ">" : string.Empty;

        private static string FormatParam(ParameterInfo p)
        {
            var prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? (p.IsIn ? "in " : "ref ") : string.Empty;
            var text = prefix + TypeNameFormatter.Format(p.ParameterType) + " " + p.Name;
            if (p.IsOptional)
            {
                var d = p.DefaultValue;
                text += d == null || d is System.Reflection.Missing ? " = null"
                    : d is string s ? " = \"" + s + "\""
                    : d is bool b ? (b ? " = true" : " = false")
                    : " = " + Convert.ToString(d, System.Globalization.CultureInfo.InvariantCulture);
            }

            return text;
        }

        private static string MethodMods(MethodInfo m)
        {
            var s = m.IsStatic ? "static " : string.Empty;
            if (m.IsAbstract)
            {
                s += "abstract ";
            }
            else if (m.IsVirtual && !m.IsFinal)
            {
                s += m.GetBaseDefinition() != m ? "override " : "virtual ";
            }

            return s;
        }

        // 型の public な公開面(コンストラクタ / フィールド / プロパティ / メソッド / enum 値)を 1 行ずつ。並びは名前順。
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
            // public コンストラクタ(GA-R-03、2026-10-06): struct / 値だけのクラスは外部がコンストラクタで値を作るので、
            // 消す・引数の型を変えると互換違反。行の追加のみ(既存の行の表記は変えない)。
            foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var ctorArgs = string.Join(", ", c.GetParameters().Select(a => a.ParameterType.Name + " " + a.Name));
                lines.Add($"  ctor({ctorArgs})");
            }

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
