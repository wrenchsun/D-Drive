using System.Collections.Generic;
using DDrive.Runtime.Ui;
using UnityEditor;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-3.5 — UiPresetCatalog の収集・検証(チケット 4-11 残り)。
    // プロジェクト内の全 UiPresetCatalog(Tests フォルダ配下は除外)を集約し、
    // UiTweenEditorWindow / CanvasEditorWindow のドロップダウンへ「[Catalog] 名前」として並べる。
    public static class UiPresetCatalogUtility
    {
        // 全 UiPresetCatalog の Entries を 1 本のリストへ集約する(Tests フォルダは除外)。
        public static List<(string name, UiTweenData tween)> Collect()
        {
            var result = new List<(string name, UiTweenData tween)>();
            var guids = AssetDatabase.FindAssets("t:" + nameof(UiPresetCatalog));
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path.Contains("/Tests/"))
                {
                    continue;
                }

                var catalog = AssetDatabase.LoadAssetAtPath<UiPresetCatalog>(path);
                if (catalog == null || catalog.Entries == null)
                {
                    continue;
                }

                foreach (var entry in catalog.Entries)
                {
                    if (entry.Tween == null)
                    {
                        continue;
                    }

                    result.Add((entry.Name, entry.Tween));
                }
            }

            return result;
        }

        // カタログ単体の妥当性検査(名前空・Tween 未設定・Id=0 を検出)。エディタの Validation 表示用。
        public static List<string> Validate(UiPresetCatalog catalog)
        {
            var issues = new List<string>();
            if (catalog == null || catalog.Entries == null)
            {
                return issues;
            }

            for (var i = 0; i < catalog.Entries.Length; i++)
            {
                var entry = catalog.Entries[i];
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    issues.Add($"[{i}] Name が空です");
                }

                if (entry.Tween == null)
                {
                    issues.Add($"[{i}] Tween が未設定です");
                    continue;
                }

                if (entry.Tween.Id == 0)
                {
                    issues.Add($"[{i}] '{entry.Name}' の Tween('{entry.Tween.name}') は Id が未採番です(AssetBrowser で採番してください)");
                }
            }

            return issues;
        }
    }
}
