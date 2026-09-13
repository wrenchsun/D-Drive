using System.Collections.Generic;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-5 — 依存関係グラフの差分更新の入口。ImportRulePostprocessor(5-11)と同じ形:
    // インポート中に重い処理(特にシーンの Open/Close)を行うと事故るため、delayCall でまとめて実行する。
    public sealed class DependencyGraphPostprocessor : AssetPostprocessor
    {
        // テスト専用の抑止(ImportRulePostprocessor.Suppress と同じ用途。テストは DependencyGraphService.
        // UpdatePaths を直接呼ぶため、AssetPostprocessor 経由の delayCall と二重に走らせないようにする)。
        public static bool Suppress;

        private static readonly List<string> PendingChanged = new();
        private static readonly List<string> PendingDeleted = new();
        private static bool _retryHooked;

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (Suppress)
            {
                return;
            }

            AddPending(PendingChanged, importedAssets);
            AddPending(PendingChanged, movedAssets);
            AddPending(PendingDeleted, deletedAssets);
            AddPending(PendingDeleted, movedFromAssetPaths);

            if (PendingChanged.Count == 0 && PendingDeleted.Count == 0)
            {
                return;
            }

            ScheduleFlush();
        }

        private static void AddPending(List<string> list, string[] paths)
        {
            if (paths == null)
            {
                return;
            }

            foreach (var path in paths)
            {
                if (!string.IsNullOrEmpty(path) && !list.Contains(path))
                {
                    list.Add(path);
                }
            }
        }

        private static void ScheduleFlush()
        {
            EditorApplication.delayCall -= Flush;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            // Play Mode 中はシーンの Open/Close を伴う走査を避ける([04_mcp_setup] / CLAUDE.md §4 と同じ方針:
            // Play Mode 中は書き込み系を実行しない)。Edit Mode に戻ってから改めて処理する。
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                if (!_retryHooked)
                {
                    _retryHooked = true;
                    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                }

                return;
            }

            var changed = PendingChanged.ToArray();
            var deleted = PendingDeleted.ToArray();
            PendingChanged.Clear();
            PendingDeleted.Clear();

            if (changed.Length == 0 && deleted.Length == 0)
            {
                return;
            }

            DependencyGraphService.UpdatePaths(changed, deleted);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _retryHooked = false;

            if (PendingChanged.Count > 0 || PendingDeleted.Count > 0)
            {
                ScheduleFlush();
            }
        }

        // 手動: 初回導入時や Library 削除後の作り直し用(全 Prefab/Scene を開閉するため重い)。
        [MenuItem(DDriveMenu.Generate + "依存関係グラフを再構築")]
        private static void RebuildAllMenuItem()
        {
            DependencyGraphService.RebuildAll();
        }
    }
}
