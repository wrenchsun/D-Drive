using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DDrive.Editor.Preload
{
    // [11_tasks.md] 5-7 — ビルド前に Build Settings の全シーン分の ScenePreloadList を最新化する。
    // 「手動メニューを忘れていて古いリストのままビルドされる」事故を防ぐための自動更新経路その2
    // (もう1つはメニュー。シーン保存の度には自動更新しない設計 — ScenePreloadGenerator 冒頭のコメント参照)。
    //
    // 失敗してもビルド自体は止めない(CLAUDE.md §0-4。Preload リストの更新はあくまで最適化であり、
    // 古いリストのままでも ScenePreload 側が未登録 ID を警告+スキップして動作は継続できるため)。
    public sealed class ScenePreloadBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                // ドメインリロード後などグラフが空でも、少なくとも今ロード済みの Library キャッシュで
                // 集計する(5-5 の方針どおり自動再構築はしない。空なら警告してそのまま進む)。
                var lists = ScenePreloadGenerator.GenerateForAllBuildScenes();
                Debug.Log($"[DDrive] ビルド前処理: Preload リストを {lists.Count} シーン分更新しました。");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DDrive] ビルド前処理: Preload リストの更新に失敗しました(ビルドは続行します): {e.Message}");
            }
        }
    }
}
