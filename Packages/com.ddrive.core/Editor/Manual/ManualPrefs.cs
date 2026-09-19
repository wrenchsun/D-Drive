using UnityEditor;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1 — マニュアルを Web(デプロイ①)優先で開くか、常にローカル優先かの
    // ユーザーローカル設定(EditorPrefs。§8.1 の InvertX/InvertY と同じ、プロジェクトスコープ無しの単純な bool)。
    // 既定は Web 優先(HumanAppUrl が未設定ならどちらでもローカルにフォールバックする、ManualUrlBuilder.ResolveUseWeb)。
    public static class ManualPrefs
    {
        private const string PreferWebKey = "DDrive.Manual.PreferWeb";

        public static bool PreferWeb
        {
            get => EditorPrefs.GetBool(PreferWebKey, true);
            set => EditorPrefs.SetBool(PreferWebKey, value);
        }
    }
}
