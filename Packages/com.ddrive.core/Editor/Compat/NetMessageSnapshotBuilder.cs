using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DDrive.Foundation.Net;

namespace DDrive.Editor.Compat
{
    // [42_distribution.md] §5.6 / §5.11-6(P-3、2026-09-20) — `INetMessage` 実装型(`Runtime/Net/*Messages.cs`)の
    // 型名(`typeof(T).FullName`。NgoNetBridge の受信側型解決キーそのもの、[42] §1.1)と
    // `[Serializable]` フィールド一覧・順序・型のゴールデンを作る。
    public static class NetMessageSnapshotBuilder
    {
        private static readonly string[] TargetAssemblies = { "DDrive.Foundation", "DDrive.Runtime" };

        public static string Build()
        {
            var types = new List<Type>();

            foreach (var assemblyName in TargetAssemblies)
            {
                var assembly = FindAssembly(assemblyName);
                if (assembly == null)
                {
                    continue;
                }

                Type[] all;
                try
                {
                    all = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    all = e.Types.Where(t => t != null).ToArray();
                }

                foreach (var t in all)
                {
                    if (typeof(INetMessage).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    {
                        types.Add(t);
                    }
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

            var sb = new System.Text.StringBuilder();
            foreach (var type in types)
            {
                sb.Append("MESSAGE ").Append(type.FullName).Append('\n');

                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                // フィールドの「宣言順」がワイヤ互換の一部(JsonUtility はフィールド名で復元するため実際には
                // 順序非依存だが、`.asset`/コード変更時の見通しのため MetadataToken 昇順=宣言順で固定する)。
                Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

                foreach (var field in fields)
                {
                    sb.Append("  ").Append(field.Name).Append(" : ").Append(TypeNameFormatter.Format(field.FieldType)).Append('\n');
                }
            }

            return sb.ToString();
        }

        private static Assembly FindAssembly(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == name)
                {
                    return asm;
                }
            }

            return null;
        }
    }
}
