using System;
using System.Linq;
using System.Text;

namespace DDrive.Editor.Compat
{
    // [42_distribution.md] §5.11(P-3) — スナップショット群が共有する「読める型名」の整形ロジック。
    // System.Reflection の Type.Name/FullName は開いた総称型(Handle`1)や配列(Int32[])が
    // 人間に読みにくい形になるため、ここで C# ライクな表記(Handle<T>、int[])に直す。
    public static class TypeNameFormatter
    {
        public static string Format(Type type, bool nameOnly = false)
        {
            if (type == null)
            {
                return "void";
            }

            if (type.IsByRef)
            {
                return Format(type.GetElementType(), nameOnly);
            }

            if (type.IsArray)
            {
                return Format(type.GetElementType(), nameOnly) + "[]";
            }

            var alias = BuiltInAlias(type);
            if (alias != null)
            {
                return alias;
            }

            if (type.IsGenericType)
            {
                var name = nameOnly ? StripGenericArity(type.Name) : StripGenericArity(type.FullName ?? type.Name);
                var args = type.GetGenericArguments().Select(a => Format(a, nameOnly: false));
                return $"{name}<{string.Join(", ", args)}>";
            }

            return nameOnly ? type.Name : (type.FullName ?? type.Name).Replace('+', '.');
        }

        private static string StripGenericArity(string name)
        {
            var backtick = name.IndexOf('`');
            return backtick >= 0 ? name.Substring(0, backtick) : name;
        }

        private static string BuiltInAlias(Type type)
        {
            if (type == typeof(void))
            {
                return "void";
            }

            if (type == typeof(bool))
            {
                return "bool";
            }

            if (type == typeof(int))
            {
                return "int";
            }

            if (type == typeof(uint))
            {
                return "uint";
            }

            if (type == typeof(long))
            {
                return "long";
            }

            if (type == typeof(ulong))
            {
                return "ulong";
            }

            if (type == typeof(short))
            {
                return "short";
            }

            if (type == typeof(ushort))
            {
                return "ushort";
            }

            if (type == typeof(byte))
            {
                return "byte";
            }

            if (type == typeof(sbyte))
            {
                return "sbyte";
            }

            if (type == typeof(float))
            {
                return "float";
            }

            if (type == typeof(double))
            {
                return "double";
            }

            if (type == typeof(string))
            {
                return "string";
            }

            if (type == typeof(object))
            {
                return "object";
            }

            if (type == typeof(char))
            {
                return "char";
            }

            return null;
        }
    }
}
