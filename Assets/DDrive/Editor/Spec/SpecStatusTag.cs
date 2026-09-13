using System.Collections.Generic;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §3.1/§7.2 — 「状態」列(未着手/仮/本番/保留)は、専用フィールドや TagCatalog が
    // 無いため、既存の Tags(string[])に "State/<値>" 形式で載せる最小実装(docs/28 の「要判断」に記載)。
    public static class SpecStatusTag
    {
        public const string Prefix = "State/";

        public static string GetCurrent(string[] tags)
        {
            if (tags == null)
            {
                return string.Empty;
            }

            foreach (var tag in tags)
            {
                if (!string.IsNullOrEmpty(tag) && tag.StartsWith(Prefix, System.StringComparison.Ordinal))
                {
                    return tag.Substring(Prefix.Length);
                }
            }

            return string.Empty;
        }

        // 既存の State/* タグを取り除き、status が空でなければ新しいものを付け直した配列を返す。
        public static string[] WithStatus(string[] tags, string status)
        {
            var list = new List<string>();
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    if (string.IsNullOrEmpty(tag) || tag.StartsWith(Prefix, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    list.Add(tag);
                }
            }

            if (!string.IsNullOrEmpty(status))
            {
                list.Add(Prefix + status);
            }

            return list.ToArray();
        }
    }
}
