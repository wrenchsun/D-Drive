using System;
using System.Collections.Generic;
using System.Linq;

namespace DDrive.Editor.Compat
{
    // [42_distribution.md] §5.2 / §5.11-3(P-3、2026-09-20) — シリアライズされ得る全 enum(AssetType を含む)の
    // 「名前=値」を固定するためのゴールデン生成。
    //
    // 対象の絞り方: DDrive.Foundation / DDrive.Runtime に定義された public(またはネスト public な)enum を
    // すべて対象にする(手作業での選別はしない = 新種別追加時に登録し忘れる事故を避ける)。トップレベル型の
    // 直下にネストされた enum(例: DDriveRuntimeBootstrap.NetBridgeMode)も、シーンの MonoBehaviour の
    // public フィールドとして実際にシリアライズされ得るため含める。
    public static class SerializedEnumSnapshotBuilder
    {
        private static readonly string[] TargetAssemblies = { "DDrive.Foundation", "DDrive.Runtime" };

        public static string Build()
        {
            var lines = new List<string>();

            foreach (var assemblyName in TargetAssemblies)
            {
                var assembly = FindAssembly(assemblyName);
                if (assembly == null)
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (!type.IsEnum || !(type.IsPublic || type.IsNestedPublic))
                    {
                        continue;
                    }

                    var underlying = Enum.GetUnderlyingType(type);
                    foreach (var name in Enum.GetNames(type))
                    {
                        var value = Convert.ChangeType(Enum.Parse(type, name), underlying);
                        lines.Add($"{TypeNameFormatter.Format(type)}.{name}={value}");
                    }
                }
            }

            lines.Sort(StringComparer.Ordinal);

            var sb = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }

        private static System.Reflection.Assembly FindAssembly(string name)
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
