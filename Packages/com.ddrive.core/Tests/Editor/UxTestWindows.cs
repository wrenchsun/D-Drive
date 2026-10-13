using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // UX 系 EditMode テストの衛生(docs/09 §16「UX テストの衛生ルール」、docs/1010 §5 UX-1b)。
    // 実ウィンドウを Show するテストが型限定の掃除で取りこぼし、ウィンドウが溜まってメモリを食い潰した(2026-10-13 に Unity が OOM で 2 回落ちた)ため、
    // 全 UX テストの [SetUp] / [TearDown] はこの CloseAll に統一する。
    internal static class UxTestWindows
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // Editor/Ux の型は internal(InternalsVisibleTo を置かない方針)なのでリフレクションで立てる。
        private static void SetSuppress(bool value)
        {
            var hooks = typeof(DDrive.Editor.Menu.DDriveMenu).Assembly.GetType("DDrive.Editor.Ux.UxTestHooks", true);
            hooks.GetField("SuppressDialogs", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, value);
        }

        // D-Drive のウィンドウか(UxEditorWindow<> 派生、または名前空間 DDrive.Editor の旧ウィンドウ)。
        private static bool IsDDriveWindow(Type t)
        {
            for (var b = t; b != null; b = b.BaseType)
            {
                if (b.IsGenericType && b.GetGenericTypeDefinition().FullName == "DDrive.Editor.Ux.UxEditorWindow`1")
                {
                    return true;
                }
            }

            var ns = t.Namespace;
            return ns != null && (ns == "DDrive.Editor" || ns.StartsWith("DDrive.Editor.", StringComparison.Ordinal));
        }

        private static List<EditorWindow> Collect()
        {
            var list = new List<EditorWindow>();
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && IsDDriveWindow(w.GetType()))
                {
                    list.Add(w);
                }
            }

            return list;
        }

        // 残っている D-Drive ウィンドウの数(テストの前後で増えていないことの確認に使う)。
        public static int Count() => Collect().Count + EmptyContainers().Count;

        // すべて閉じる。閉じる前に対象の Data の dirty を捨て、未保存確認のダイアログを出さない。
        // 以後(Release を呼ぶまで)UxEditorWindow は未保存の確認を出さない。
        public static void CloseAll()
        {
            SetSuppress(true);
            foreach (var w in Collect())
            {
                if (w == null)
                {
                    continue;
                }

                try
                {
                    ClearTargetDirty(w);
                    RemovePreviews(w);
                    w.Close();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[UxTestWindows] ウィンドウを閉じられませんでした: " + e.Message);
                }

                if (w != null)
                {
                    UnityEngine.Object.DestroyImmediate(w);
                }
            }

            CloseEmptyContainers();
        }

        // 中身(EditorWindow)が破棄済みで枠だけ残った ContainerWindow(フローティングの OS ウィンドウ)。
        // CreateInstance + Show した窓を DestroyImmediate すると残る。Close() なら枠も片付く。MainWindow(m_ShowMode == 4)は対象外。
        private static List<object> EmptyContainers()
        {
            var result = new List<object>();
            var editorAsm = typeof(EditorWindow).Assembly;
            var cwType = editorAsm.GetType("UnityEditor.ContainerWindow");
            var hostType = editorAsm.GetType("UnityEditor.HostView");
            var viewType = editorAsm.GetType("UnityEditor.View");
            var windowsProp = cwType?.GetProperty("windows", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (windowsProp == null || hostType == null || viewType == null)
            {
                return result;
            }

            var showMode = cwType.GetField("m_ShowMode", Inst);
            var rootView = cwType.GetProperty("rootView", Inst);
            var allChildren = viewType.GetProperty("allChildren", Inst);
            var actualView = hostType.GetProperty("actualView", Inst);
            if (!(windowsProp.GetValue(null) is Array windows) || showMode == null || rootView == null || allChildren == null || actualView == null)
            {
                return result;
            }

            foreach (var cw in windows)
            {
                if (cw == null || (int)showMode.GetValue(cw) == 4)
                {
                    continue;
                }

                var empty = true;
                var root = rootView.GetValue(cw);
                if (root != null && allChildren.GetValue(root) is Array children)
                {
                    foreach (var child in children)
                    {
                        if (hostType.IsInstanceOfType(child) && actualView.GetValue(child) is UnityEngine.Object view && view != null)
                        {
                            empty = false;
                            break;
                        }
                    }
                }

                if (empty)
                {
                    result.Add(cw);
                }
            }

            return result;
        }

        private static void CloseEmptyContainers()
        {
            foreach (var cw in EmptyContainers())
            {
                try
                {
                    cw.GetType().GetMethod("Close", Inst, null, Type.EmptyTypes, null)?.Invoke(cw, null);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[UxTestWindows] ContainerWindow を閉じられませんでした: " + e.Message);
                }
            }
        }

        // クラスの [OneTimeTearDown] 用: CloseAll + 抑止の解除 + 未使用アセットの解放。
        public static void Release()
        {
            CloseAll();
            SetSuppress(false);
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
        }

        // プレビュー用の実体(シーンのオブジェクト)を持つ窓は、閉じる前に片付ける(窓ごとの後始末メソッド名。無ければ何もしない)。
        private static void RemovePreviews(EditorWindow w)
        {
            foreach (var name in new[] { "RemovePreview", "RemoveMain" })
            {
                for (var t = w.GetType(); t != null; t = t.BaseType)
                {
                    var m = t.GetMethod(name, Inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (m != null)
                    {
                        m.Invoke(w, null);
                        break;
                    }
                }
            }
        }

        private static void ClearTargetDirty(EditorWindow w)
        {
            for (var t = w.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField("_target", Inst | BindingFlags.DeclaredOnly);
                if (f != null && typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                {
                    if (f.GetValue(w) is UnityEngine.Object data && data != null)
                    {
                        EditorUtility.ClearDirty(data);
                    }

                    return;
                }
            }
        }

        // アセットの生成・削除を 1 回の Refresh にまとめる(サンプル Data の作成で Asset Pipeline Refresh が何十回も走るのを避ける)。
        public static T Batch<T>(Func<T> build)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                return build();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        public static void Batch(Action build) => Batch<int>(() =>
        {
            build();
            return 0;
        });

        public static void DeleteFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                Batch(() => AssetDatabase.DeleteAsset(folder));
            }
        }
    }
}
