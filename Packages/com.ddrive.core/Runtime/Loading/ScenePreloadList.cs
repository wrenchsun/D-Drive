using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Runtime.Loading
{
    // [11_tasks.md] 5-7 / [10_workflow.md] §5 — シーン単位の「使用 ID 一覧」。
    // Editor の ScenePreloadGenerator(Editor/Preload/)が依存関係グラフ(5-5)から自動集計して書き込む。
    // ランタイムはこの中身をそのまま ScenePreload.RunAsync に渡すだけの読み取り専用データ(CLAUDE.md §0-5:
    // Manager/ランタイムコードは書き換えない。書き換えるのは Editor API 経由のみ)。
    // Addressables には登録しない([10] §5 実装メモ参照。シーンから直参照する運用)。
    [CreateAssetMenu(menuName = "D-Drive/Loading/Scene Preload List", fileName = "NewScenePreloadList")]
    public sealed class ScenePreloadList : ScriptableObject
    {
        [Tooltip("集計元のシーン名(表示・目視確認用。実行時には使わない)")]
        [SerializeField] private string sceneName;

        [Tooltip("このシーンが依存グラフ経由で参照する ID の一覧(ScenePreloadGenerator が自動生成)")]
        [SerializeField] private List<PreloadEntry> entries = new();

        public string SceneName => sceneName;
        public IReadOnlyList<PreloadEntry> Entries => entries;

        public IReadOnlyList<ulong> GetIds()
        {
            var ids = new List<ulong>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                ids.Add(entries[i].Id);
            }

            return ids;
        }

        // Editor 専用の更新口(ScenePreloadGenerator から呼ぶ)。ランタイムコードから呼ばない。
        public void SetEntries(string newSceneName, List<PreloadEntry> newEntries)
        {
            sceneName = newSceneName;
            entries = newEntries ?? new List<PreloadEntry>();
        }
    }
}
