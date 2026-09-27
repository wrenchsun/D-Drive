using UnityEditor;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1 — マニュアルを Web(デプロイ①)優先で開くか、常にローカル優先かの
    // ユーザーローカル設定(EditorPrefs。§8.1 の InvertX/InvertY と同じ、プロジェクトスコープ無しの単純な bool)。
    // 既定はローカル優先(2026-09-27 変更。それまでは Web 優先だった)。Web(デプロイ①、GAS の Web アプリ)は
    // Google アカウントの閲覧権限が無い人には開けず、持ち込み先で「開ける人と開けない人」が出るため、
    // 既定は手元の HTML にし、Web は「Web 版を優先」トグルで明示的に ON にした人だけが使う。
    // HumanAppUrl が未設定ならどちらでもローカルにフォールバックする(ManualUrlBuilder.ResolveUseWeb)。
    public static class ManualPrefs
    {
        private const string PreferWebKey = "DDrive.Manual.PreferWeb";

        public static bool PreferWeb
        {
            get => EditorPrefs.GetBool(PreferWebKey, false);
            set => EditorPrefs.SetBool(PreferWebKey, value);
        }
    }
}
