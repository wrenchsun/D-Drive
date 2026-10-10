using System;
using System.Collections.Generic;

namespace DDrive.EditorPrototypes
{
    // 欄の重要度(docs/1008 §2)。Required = Validator が Error にする / Common = よく触る・Warning の対象 / Advanced = 既定のままでよい。
    public enum FieldTier
    {
        Required = 0,
        Common = 1,
        Advanced = 2,
    }

    // 1 欄ぶんの案内。Field は SerializedObject.FindProperty に渡すフィールド名(入れ子は "Anchor" のようにルート名)。
    public sealed class FieldGuideEntry
    {
        public string Field;
        public string Label;
        public string Hint;
        public FieldTier Tier;
        public string Section;   // A 用: 構造上の節
        public string Purpose;   // C 用: 目的カード名
        public string[] Keywords;
    }

    // 種別ごとの静的な表。どのサンプルも同じ表を使う(将来はパッケージ側の共通基盤にする前提で、種別に依存しない形)。
    public abstract class FieldGuide
    {
        public abstract Type DataType { get; }
        public abstract IReadOnlyList<FieldGuideEntry> Entries { get; }
        public abstract IReadOnlyList<string> Sections { get; }
        public abstract IReadOnlyList<string> Purposes { get; }

        public FieldGuideEntry Find(string field)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Field == field)
                {
                    return Entries[i];
                }
            }

            return null;
        }

        // ラベル・説明・検索語・フィールド名の部分一致(大小無視)。
        public static bool Matches(FieldGuideEntry e, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            var q = query.Trim();
            if (Has(e.Label, q) || Has(e.Hint, q) || Has(e.Field, q))
            {
                return true;
            }

            if (e.Keywords != null)
            {
                for (var i = 0; i < e.Keywords.Length; i++)
                {
                    if (Has(e.Keywords[i], q))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Has(string s, string q) =>
            !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
