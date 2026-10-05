using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    /// <summary>
    /// M-6 再発防止: アセットになりうる型(ScriptableObject / MonoBehaviour 派生の具象型)は、
    /// クラス名と同じ名前のファイルに置く。違うと Unity が MonoScript を持てず、
    /// .playable などに <c>m_Script: {fileID: 0}</c> で保存され、Player ビルドで読み込めない。
    /// 持ち込み先(パッケージが PackageCache にある)でも <c>AssetDatabase</c> で MonoScript を探すので動く。
    /// </summary>
    public sealed class MonoScriptFileNameTests
    {
        static readonly string[] RuntimeAssemblies =
        {
            "DDrive.Foundation", "DDrive.Runtime", "DDrive.Runtime.Ngo",
        };

        [Test]
        public void ScriptableObject_And_MonoBehaviour_Types_Live_In_Same_Named_File()
        {
            var withScript = new HashSet<Type>();
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                var t = ms != null ? ms.GetClass() : null;
                if (t != null) withScript.Add(t);
            }

            var missing = new List<string>();
            var checkedCount = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (Array.IndexOf(RuntimeAssemblies, asm.GetName().Name) < 0) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsGenericType || t.IsNested || t.IsInterface) continue;
                    if (!typeof(ScriptableObject).IsAssignableFrom(t) && !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                    if (t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) continue;
                    checkedCount++;
                    if (!withScript.Contains(t)) missing.Add(asm.GetName().Name + ": " + t.FullName);
                }
            }

            Assert.Greater(checkedCount, 20, "検査対象の型が少なすぎる(走査の不具合)");
            CollectionAssert.IsEmpty(missing,
                "クラス名と違う名前のファイルにある型(Player ビルドで読み込めない)。1 型 1 ファイルで、クラス名と同じ名前のファイルへ分けること:\n"
                + string.Join("\n", missing.OrderBy(x => x)));
        }
    }
}
