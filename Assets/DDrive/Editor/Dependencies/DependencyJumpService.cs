using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 / [09_editor_tools.md] §1 — 使用箇所検索一覧のダブルクリックジャンプ。
    // Data/Prefab は選択 + Ping。Scene はシーンを開くかどうか確認ダイアログ(未保存変更があれば
    // EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo)してから該当オブジェクトを選択する。
    public static class DependencyJumpService
    {
        // テストから差し替え可能に(CLAUDE.md §0-9 の方針、NewAssetDialog.TestGameDataRootOverride と同じ形)。
        // 既定は実際のダイアログ。戻り値 true = 開いてよい。
        public static Func<string, bool> ConfirmOpenSceneOverride;

        // EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo の呼び出しをテストで避けるためのフック。
        // 既定は本物を呼ぶ。戻り値 false ならユーザーがキャンセルしたとみなして中止する。
        public static Func<bool> SaveModifiedScenesOverride;

        public static void Reveal(DependencyReference reference) => RevealAt(reference.SourcePath, reference.ObjectPath);

        public static void RevealAt(string sourcePath, string objectPath)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

            if (sourcePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                RevealInScene(sourcePath, objectPath);
                return;
            }

            if (sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                RevealInPrefab(sourcePath, objectPath);
                return;
            }

            // .asset(Data): objectPath は空(Data 自身の参照)のはず。
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[DDrive] ジャンプ先が見つかりませんでした: {sourcePath}");
                return;
            }

            Select(asset);
        }

        private static void RevealInPrefab(string path, string objectPath)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                Debug.LogWarning($"[DDrive] Prefab を読み込めませんでした: {path}");
                return;
            }

            GameObject target = root;
            if (!string.IsNullOrEmpty(objectPath))
            {
                var found = root.transform.Find(objectPath);
                if (found == null)
                {
                    Debug.LogWarning($"[DDrive] {path} 内に '{objectPath}' が見つかりませんでした(Prefab のルートを選択します)。");
                }
                else
                {
                    target = found.gameObject;
                }
            }

            Select(target);
        }

        private static void RevealInScene(string path, string objectPath)
        {
            var alreadyLoaded = SceneManager.GetSceneByPath(path);
            if (!alreadyLoaded.IsValid() || !alreadyLoaded.isLoaded)
            {
                var confirm = ConfirmOpenSceneOverride ?? (msg => EditorUtility.DisplayDialog("シーンを開く", msg, "開く", "キャンセル"));
                if (!confirm($"'{path}' を開いて対象のオブジェクトを選択しますか?"))
                {
                    return;
                }

                var saveModified = SaveModifiedScenesOverride ?? EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo;
                if (!saveModified())
                {
                    return; // ユーザーが保存ダイアログをキャンセル
                }

                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }

            var loaded = SceneManager.GetSceneByPath(path);
            if (!loaded.IsValid())
            {
                Debug.LogWarning($"[DDrive] シーンを開けませんでした: {path}");
                return;
            }

            var target = FindInScene(loaded, objectPath);
            if (target == null)
            {
                Debug.LogWarning($"[DDrive] {path} 内に '{objectPath}' が見つかりませんでした。");
                return;
            }

            Select(target);
        }

        private static GameObject FindInScene(Scene scene, string objectPath)
        {
            if (string.IsNullOrEmpty(objectPath))
            {
                return null;
            }

            var separator = objectPath.IndexOf('/');
            var rootName = separator < 0 ? objectPath : objectPath.Substring(0, separator);

            GameObject rootGo = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == rootName)
                {
                    rootGo = go;
                    break;
                }
            }

            if (rootGo == null)
            {
                return null;
            }

            if (separator < 0)
            {
                return rootGo;
            }

            var rest = objectPath.Substring(separator + 1);
            var found = rootGo.transform.Find(rest);
            return found != null ? found.gameObject : rootGo;
        }

        private static void Select(UnityEngine.Object target)
        {
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }
    }
}
