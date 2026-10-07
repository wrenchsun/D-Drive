using UnityEditor;

namespace DDrive.Editor.Settings
{
    // [1002_ddrive_mcp.md] §9.1 Q-4 (c)(MCP-1、2026-10-07) — Project Settings > D-Drive > MCP。
    // MCP の書き込みツール(ddrive_* の作成・変更・削除・生成)を許可するかを切り替える
    // (`DDriveProjectSettings.McpAllowWrite`、既定 OFF)。読み取りツールは設定に関係なく使える。
    // この UI 自体は isuzu 版 MCP が無くても出る(設定値の置き場は DDrive.Editor 側にあるため)。
    public static class McpSettingsProvider
    {
        public const string SettingsPath = "Project/D-Drive/MCP";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "MCP",
                guiHandler = _ => DrawGui(),
                keywords = new[] { "D-Drive", "MCP", "ddrive_", "書き込み" },
            };
        }

        private static void DrawGui()
        {
            EditorGUILayout.HelpBox(
                "AI クライアントから使う MCP ツール(ddrive_*)のうち、Data の作成・変更・削除・コード生成など" +
                "プロジェクトを書き換えるものを許可するかを選びます。OFF のときは読み取りツール(状態・一覧・検査)だけ使えます。" +
                "開発リポジトリでは自動で ON になります。",
                MessageType.Info);

            var settings = DDriveProjectSettings.instance;
            EditorGUI.BeginChangeCheck();
            var value = EditorGUILayout.ToggleLeft(
                "MCP の書き込みツール(ddrive_* の作成・変更・削除・生成)を許可する", settings.McpAllowWrite);
            if (EditorGUI.EndChangeCheck())
            {
                settings.SetMcpAllowWrite(value);
            }
        }
    }
}
