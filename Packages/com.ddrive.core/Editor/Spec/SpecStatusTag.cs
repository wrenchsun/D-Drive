using System.Collections.Generic;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §10.2.1 — 発注の「状態」は、専用フィールドや TagCatalog が無いため、
    // 既存の Tags(string[])に "State/<値>" 形式で載せる最小実装(docs/28 の「要判断」に記載)。
    //
    // 2026-09-17([41] P1-7): 値は O-1 以降 **3 値(発注済 / 納品済 / インポート済)**。
    // 正は `Tools/SpecWeb/src/Assets.js` の `SPEC_WEB_ASSET_STATUSES`。旧 4 値
    // (未着手 / 仮 / 本番 / 保留。[27_spec_sheet.md] §7.2)は GAS 側の
    // `specWebNormalizeLegacyOrderItem_` が読み込み時に 3 値へ変換して返すため、D-Drive 側は
    // 受け取った文字列をそのままタグに載せるだけでよい(値の一覧で弾く検証はしない。未知の値でも
    // そのまま載せて同期を止めない。CLAUDE.md §0-4)。
    public static class SpecStatusTag
    {
        public const string Prefix = "State/";

        // 発注ツール(O-1)の状態 3 値。表示・貼り付け補助(SpecSyncService.BuildChoicesTsv)用の一覧で、
        // GAS 側 `SPEC_WEB_ASSET_STATUSES` と同じ順序・同じ値に保つこと。
        public static readonly string[] Statuses = { "発注済", "納品済", "インポート済" };

        // D-Drive の同期でのみ到達する状態([32] §10.4.1。手動では選べない)。
        public const string ImportedStatus = "インポート済";

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
