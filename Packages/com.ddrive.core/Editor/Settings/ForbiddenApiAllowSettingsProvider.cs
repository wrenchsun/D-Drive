using DDrive.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Settings
{
    // [11_tasks.md] M-4(2026-10-05) — Project Settings > D-Drive > 禁止 API の除外。
    // `DDriveProjectSettings.ForbiddenApiAllowEntries` を編集する(用途は自分で書き換えられない外部コード・
    // 生成コード。自分のコードの個別の行は行単位の許可コメントを使う)。
    public static class ForbiddenApiAllowSettingsProvider
    {
        public const string SettingsPath = "Project/D-Drive/禁止 API の除外";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "禁止 API の除外",
                guiHandler = _ => DrawGui(),
                keywords = new[] { "D-Drive", "ForbiddenApi", "ddrive-allow", "禁止 API" },
            };
        }

        private static void DrawGui()
        {
            EditorGUILayout.HelpBox(
                "自分で書き換えられない外部コード・生成コードのフォルダ/ファイルを、禁止 API の検査から外します。" +
                "自分のコードの個別の行は、行末または直前の行に `// ddrive-allow: 規則名(理由)` を書いてください。" +
                $"理由は必須です(空の要素は無効)。規則名: {string.Join(" / ", ForbiddenApiScanner.RuleNames)}(空欄 = 全規則)。",
                MessageType.Info);

            var settings = DDriveProjectSettings.instance;
            var so = new SerializedObject(settings);
            var list = so.FindProperty("_forbiddenApiAllowEntries");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(list, new GUIContent("除外の一覧"), true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Edit Forbidden API Allow Entries");
                so.ApplyModifiedProperties();
                settings.SaveForbiddenApiAllowEntries();
            }

            foreach (var entry in settings.ForbiddenApiAllowEntries)
            {
                var problem = ForbiddenApiScanner.DescribeEntryProblem(entry);
                if (problem != null)
                {
                    EditorGUILayout.HelpBox($"'{entry?.Path}': {problem}", MessageType.Warning);
                }
            }
        }
    }
}
