namespace DDrive.Editor.Validation
{
    // 「禁止 API の検査」ウィンドウの 1 行に出す文言を作る純関数(2026-10-06、docs/43 17-1 / 17-7 の指摘対応)。
    // 見た目(UI Toolkit の要素)から切り離して EditMode テストできるようにしてある。
    public static class ForbiddenApiRowText
    {
        public const int DefaultPathChars = 60;

        // ボタンに出す「パス:行」。長いときは先頭を「…」で省略し、末尾のファイル名と行番号を必ず残す。
        public static string DisplayPath(string path, int line, int maxChars = DefaultPathChars)
        {
            var text = path ?? string.Empty;
            var suffix = line > 0 ? ":" + line : string.Empty;
            if (maxChars < suffix.Length + 2)
            {
                maxChars = suffix.Length + 2;
            }

            var room = maxChars - suffix.Length;
            if (text.Length > room)
            {
                text = "…" + text.Substring(text.Length - (room - 1));
            }

            return text + suffix;
        }

        // 許可されていない当たり 1 行の「本文」(規則名と該当行)と「案内」(その行について利用者がすべきこと。規則の説明・
        // 許可コメントに理由が必要、など)。案内は以前ツールチップにしか出ていなかったので、行の本文として別行で出す。
        public static void ViolationLines(string ruleName, string excerpt, string message, out string body, out string guidance)
        {
            if (excerpt == null)
            {
                // 走査ルートが無い等の全体エラー(規則名・該当行なし)。メッセージ自体が本文。
                body = message ?? string.Empty;
                guidance = null;
                return;
            }

            var head = string.IsNullOrEmpty(ruleName) ? string.Empty : $"[{ruleName}] ";
            body = head + excerpt;
            guidance = string.IsNullOrEmpty(message) ? null : message;
        }
    }
}
