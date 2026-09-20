using System;
using System.Text;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.1/§6 P-8(2026-09-20) — CHANGELOG の 1 版分の本文から `### 互換性` 節を
    // 取り出し、「破壊あり」が書かれていないかを調べる純関数。更新ウィンドウが「破壊あり」を検出したら
    // 警告を出す(§4.2 手順 1「破壊ありなら移行ガイドを先に読む」)。
    public static class ChangelogCompatibilityAnalyzer
    {
        private const string CompatibilityHeading = "### 互換性";
        private const string BreakingMarker = "破壊あり";

        // `### 互換性` から次の `### `/`## ` 見出しまでの本文(前後の空白は削らない。表示用)。
        // 見出しが無ければ null。
        public static string ExtractCompatibilityText(string sectionBody)
        {
            if (string.IsNullOrEmpty(sectionBody))
            {
                return null;
            }

            var lines = sectionBody.Replace("\r\n", "\n").Split('\n');
            var collecting = false;
            var sb = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();
                if (trimmed.StartsWith("### ", StringComparison.Ordinal) || trimmed.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (collecting)
                    {
                        break; // 次の見出しに入ったら終了。
                    }

                    collecting = string.Equals(trimmed, CompatibilityHeading, StringComparison.Ordinal);
                    continue;
                }

                if (collecting)
                {
                    sb.Append(line).Append('\n');
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        public static bool HasBreakingChange(string sectionBody)
        {
            var text = ExtractCompatibilityText(sectionBody);
            return text != null && text.Contains(BreakingMarker, StringComparison.Ordinal);
        }
    }
}
