using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace DDrive.Editor.Compat
{
    // [42_distribution.md] §5.4 / §5.11-1(P-3、2026-09-20) — DDrive.Foundation / DDrive.Runtime の
    // public 型・メンバー(名前・シグネチャ・[Obsolete] の有無)をリフレクションで列挙し、人が読める
    // テキストにする。PublicApiSnapshotTests がこの出力を
    // Tests/Editor/Compat/Snapshots/public-api-<assembly>.txt と比較する。
    //
    // 対象: 指定アセンブリで「宣言」された public(または外側の型が public なネスト public)型。
    // メンバーは DeclaredOnly(継承元 UnityEngine.Object 等の基底 API は含めない。Unity 自身の
    // 互換性はここでは扱わない対象で、含めると D-Drive が何も変えていないのにノイズで赤くなる)。
    public static class PublicApiSnapshotBuilder
    {
        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static string Build(string assemblyName)
        {
            var assembly = FindAssembly(assemblyName);
            var sb = new StringBuilder();

            if (assembly == null)
            {
                sb.Append("// assembly not found: ").Append(assemblyName).Append('\n');
                return sb.ToString();
            }

            var types = new List<Type>();
            Type[] allTypes;
            try
            {
                allTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                allTypes = e.Types.Where(t => t != null).ToArray();
            }

            foreach (var t in allTypes)
            {
                if (IsApiVisible(t))
                {
                    types.Add(t);
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

            foreach (var type in types)
            {
                sb.Append(FormatTypeHeader(type)).Append('\n');

                var members = new List<string>(EnumerateMembers(type));
                members.Sort(StringComparer.Ordinal);
                foreach (var member in members)
                {
                    sb.Append("  ").Append(member).Append('\n');
                }
            }

            return sb.ToString();
        }

        private static Assembly FindAssembly(string assemblyName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == assemblyName)
                {
                    return asm;
                }
            }

            return null;
        }

        private static bool IsApiVisible(Type type)
        {
            if (!(type.IsPublic || type.IsNestedPublic))
            {
                return false;
            }

            if (Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
            {
                return false;
            }

            // ラムダ・イテレータ等が生成する内部クラス(`<>c`, `<Foo>d__0` 等)。念のための二重チェック。
            if (type.Name.Contains("<") || type.Name.Contains(">"))
            {
                return false;
            }

            return true;
        }

        private static string FormatTypeHeader(Type type)
        {
            var isDelegate = typeof(Delegate).IsAssignableFrom(type);
            var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : isDelegate ? "delegate" : type.IsValueType ? "struct" : "class";

            var modifiers = new List<string>();
            if (!type.IsEnum && !type.IsInterface && !isDelegate && !type.IsValueType)
            {
                if (type.IsAbstract && type.IsSealed)
                {
                    modifiers.Add("static");
                }
                else if (type.IsAbstract)
                {
                    modifiers.Add("abstract");
                }
                else if (type.IsSealed)
                {
                    modifiers.Add("sealed");
                }
            }

            var mod = modifiers.Count > 0 ? " " + string.Join(" ", modifiers) : string.Empty;
            var obsolete = FormatObsolete(type);

            var baseInfo = string.Empty;
            if (!type.IsEnum && !type.IsInterface && !isDelegate && type.BaseType != null
                && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType) && type.BaseType != typeof(Enum))
            {
                baseInfo = " : " + TypeNameFormatter.Format(type.BaseType);
            }

            return $"TYPE {kind}{mod} {TypeNameFormatter.Format(type)}{baseInfo}{obsolete}";
        }

        private static IEnumerable<string> EnumerateMembers(Type type)
        {
            foreach (var ctor in type.GetConstructors(MemberFlags))
            {
                yield return FormatConstructor(type, ctor);
            }

            foreach (var field in type.GetFields(MemberFlags))
            {
                if (field.IsSpecialName)
                {
                    continue;
                }

                yield return FormatField(field);
            }

            foreach (var prop in type.GetProperties(MemberFlags))
            {
                yield return FormatProperty(prop);
            }

            foreach (var method in type.GetMethods(MemberFlags))
            {
                if (IsAccessorMethod(method.Name))
                {
                    continue;
                }

                yield return FormatMethod(method);
            }

            foreach (var evt in type.GetEvents(MemberFlags))
            {
                yield return FormatEvent(evt);
            }

            if (type.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(type);
                foreach (var name in Enum.GetNames(type))
                {
                    var value = Convert.ChangeType(Enum.Parse(type, name), underlying);
                    yield return $"enum-member {name} = {value}";
                }
            }
        }

        // get_/set_/add_/remove_ はプロパティ・イベントのアクセサとして別枠(FormatProperty/FormatEvent)で
        // 出す。演算子(op_Equality 等)は IsSpecialName=true だが API の一部として保持する必要があるため、
        // ここでは名前判定のみで弾く(IsSpecialName の一括除外はしない)。
        private static bool IsAccessorMethod(string name)
        {
            return name.StartsWith("get_", StringComparison.Ordinal)
                || name.StartsWith("set_", StringComparison.Ordinal)
                || name.StartsWith("add_", StringComparison.Ordinal)
                || name.StartsWith("remove_", StringComparison.Ordinal);
        }

        private static string FormatConstructor(Type declaringType, ConstructorInfo ctor)
        {
            var staticMark = ctor.IsStatic ? "static " : string.Empty;
            var parameters = string.Join(", ", ctor.GetParameters().Select(FormatParameter));
            return $"ctor {staticMark}{TypeNameFormatter.Format(declaringType, nameOnly: true)}({parameters}){FormatObsolete(ctor)}";
        }

        private static string FormatField(FieldInfo field)
        {
            var mods = new List<string>();
            if (field.IsStatic)
            {
                mods.Add(field.IsLiteral ? "const" : "static");
            }

            if (field.IsInitOnly)
            {
                mods.Add("readonly");
            }

            var mod = mods.Count > 0 ? string.Join(" ", mods) + " " : string.Empty;
            return $"field {mod}{field.Name} : {TypeNameFormatter.Format(field.FieldType)}{FormatObsolete(field)}";
        }

        private static string FormatProperty(PropertyInfo prop)
        {
            var accessors = new List<string>();
            var getter = prop.GetGetMethod(false);
            var setter = prop.GetSetMethod(false);
            if (getter != null)
            {
                accessors.Add("get");
            }

            if (setter != null)
            {
                accessors.Add("set");
            }

            var isStatic = (getter != null && getter.IsStatic) || (setter != null && setter.IsStatic);
            var staticMark = isStatic ? "static " : string.Empty;

            return $"property {staticMark}{prop.Name} : {TypeNameFormatter.Format(prop.PropertyType)} {{{string.Join(";", accessors)}}}{FormatObsolete(prop)}";
        }

        private static string FormatMethod(MethodInfo method)
        {
            var generic = method.IsGenericMethodDefinition
                ? "<" + string.Join(",", method.GetGenericArguments().Select(a => TypeNameFormatter.Format(a))) + ">"
                : string.Empty;

            var parameters = string.Join(", ", method.GetParameters().Select(FormatParameter));
            var staticMark = method.IsStatic ? "static " : string.Empty;

            return $"method {staticMark}{method.Name}{generic}({parameters}) : {TypeNameFormatter.Format(method.ReturnType)}{FormatObsolete(method)}";
        }

        private static string FormatEvent(EventInfo evt)
        {
            var handlerType = evt.EventHandlerType != null ? TypeNameFormatter.Format(evt.EventHandlerType) : "?";
            var add = evt.GetAddMethod(false);
            var staticMark = add != null && add.IsStatic ? "static " : string.Empty;
            return $"event {staticMark}{evt.Name} : {handlerType}{FormatObsolete(evt)}";
        }

        private static string FormatParameter(ParameterInfo p)
        {
            var prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : p.GetCustomAttribute<ParamArrayAttribute>() != null ? "params " : string.Empty;
            var typeName = TypeNameFormatter.Format(p.ParameterType);
            var defaultValue = string.Empty;
            if (p.IsOptional)
            {
                defaultValue = p.DefaultValue == null ? " = null" : $" = {FormatDefaultValue(p.DefaultValue)}";
            }

            return $"{prefix}{typeName} {p.Name}{defaultValue}";
        }

        private static string FormatDefaultValue(object value)
        {
            if (value is string s)
            {
                return "\"" + s + "\"";
            }

            if (value is bool b)
            {
                return b ? "true" : "false";
            }

            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatObsolete(MemberInfo member)
        {
            var attr = member.GetCustomAttribute<ObsoleteAttribute>();
            return attr == null ? string.Empty : " [Obsolete]";
        }
    }
}
