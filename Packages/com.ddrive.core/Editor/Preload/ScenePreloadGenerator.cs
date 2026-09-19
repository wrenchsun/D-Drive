using System.Collections.Generic;
using System.IO;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Editor.Menu;
using DDrive.Editor.Settings;
using DDrive.Editor.Versioning;
using DDrive.Runtime.Loading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DDrive.Editor.Preload
{
    // [11_tasks.md] 5-7 — ScenePreloadAggregator の結果を ScenePreloadList(.asset)へ書き出す。
    // 自動更新のタイミングは「手動メニュー(このファイル)」+「ビルド前(ScenePreloadBuildPreprocessor)」の
    // 2 経路のみ(シーン保存時のフックは意図的に付けていない。理由は docs/02_core_framework.md §14
    // 実装メモ(5-7)/docs/28 5-7 節の要判断を参照。デザイナーの作業を止めない方針(CLAUDE.md §0-4)と、
    // 5-5 が既に「保存の度に重い処理を自動実行しない」判断をしていることに合わせた)。
    //
    // .asset の生成・更新は必ずこの Editor API 経由で行う(CLAUDE.md §0-1: .asset をテキスト編集しない)。
    public static class ScenePreloadGenerator
    {
        public const string DefaultOutputRoot = "Assets/GameData/Preload";

        // [42_distribution.md] §3.4/§7 B-6(P-5) — Preload は GameData 配下の固定サブフォルダなので、
        // DDriveProjectSettings に専用フィールドは持たず GameDataRoot から導出する。
        private static string ResolveOutputRoot(string requested) =>
            string.IsNullOrEmpty(requested) || requested == DefaultOutputRoot
                ? $"{DDriveProjectSettings.instance.GameDataRoot}/Preload"
                : requested;

        // 指定シーン 1 つ分の ScenePreloadList を集計・保存する(無ければ新規作成、あれば上書き)。
        public static ScenePreloadList GenerateForScene(string scenePath, string outputRoot = DefaultOutputRoot)
        {
            outputRoot = ResolveOutputRoot(outputRoot);

            if (string.IsNullOrEmpty(scenePath))
            {
                return null;
            }

            var entries = ScenePreloadAggregator.Aggregate(scenePath);
            var sceneName = Path.GetFileNameWithoutExtension(scenePath);
            var assetPath = $"{outputRoot}/{sceneName}_PreloadList.asset";

            AssetCreationService.EnsureFolder(outputRoot);

            var list = AssetDatabase.LoadAssetAtPath<ScenePreloadList>(assetPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<ScenePreloadList>();
                AssetDatabase.CreateAsset(list, assetPath);
            }

            Undo.RecordObject(list, "Update Scene Preload List");
            list.SetEntries(sceneName, entries);
            EditorUtility.SetDirty(list);
            // [44_review_2026-09-19.md] P1-1: 対象は list 1 個だけなので、それだけ保存する。
            DDriveAssetSave.SaveDirty(list);

            return list;
        }

        // Build Settings に登録済み(かつ有効)な全シーン分を一括更新する(ビルド前フック / 一括メニュー用)。
        public static List<ScenePreloadList> GenerateForAllBuildScenes(string outputRoot = DefaultOutputRoot)
        {
            outputRoot = ResolveOutputRoot(outputRoot);
            WarnIfGraphNotBuilt();

            var result = new List<ScenePreloadList>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene == null || !scene.enabled || string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }

                var list = GenerateForScene(scene.path, outputRoot);
                if (list != null)
                {
                    result.Add(list);
                }
            }

            return result;
        }

        [MenuItem(DDriveMenu.Generate + "Preload リストを再集計(現在のシーン)")]
        private static void GenerateForActiveSceneMenuItem()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path))
            {
                Debug.LogWarning("[DDrive] アクティブシーンが保存されていません。先にシーンを保存してから実行してください。");
                return;
            }

            WarnIfGraphNotBuilt();

            var list = GenerateForScene(active.path);
            if (list == null)
            {
                return;
            }

            Selection.activeObject = list;
            EditorGUIUtility.PingObject(list);
            Debug.Log($"[DDrive] Preload リストを更新しました: '{AssetDatabase.GetAssetPath(list)}'({list.Entries.Count} 件)。");
        }

        [MenuItem(DDriveMenu.Generate + "Preload リストを再集計(ビルド設定の全シーン)")]
        private static void GenerateForAllBuildScenesMenuItem()
        {
            var lists = GenerateForAllBuildScenes();
            Debug.Log($"[DDrive] Preload リストを {lists.Count} シーン分更新しました(Build Settings の有効シーンが対象)。");
        }

        private static void WarnIfGraphNotBuilt()
        {
            if (DependencyGraphService.CachedFileCount == 0)
            {
                Debug.LogWarning("[DDrive] 依存関係グラフが未構築のため、Preload リストが空になる可能性があります。先に「依存関係グラフを再構築」を実行してください。");
            }
        }
    }
}
