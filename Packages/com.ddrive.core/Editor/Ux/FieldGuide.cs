using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;

namespace DDrive.Editor.Ux
{
    // 欄の重要度(docs/1008 §2)。Required = Validator が Error にする / Common = よく触る・Warning の対象 / Advanced = 既定のままでよい。
    public enum FieldTier
    {
        Required = 0,
        Common = 1,
        Advanced = 2,
    }

    // 行の種類。Normal = PropertyField の通常欄 / Visual = 視覚部品(DurationBar・AnchorPad など。種別が行を作る)/
    // Knobs = 調整つまみ(TuneKnobs。種別が行を作る)/ ReadOnly = 表示のみ(編集不可)。
    public enum FieldRowKind
    {
        Normal = 0,
        Visual = 1,
        Knobs = 2,
        ReadOnly = 3,
    }

    // 1 欄ぶんの案内。Field は SerializedObject.FindProperty に渡すフィールド名(入れ子は "Anchor" のようにルート名)。
    public sealed class FieldGuideEntry
    {
        public string Field;
        public string Label;
        public string Hint;
        public FieldTier Tier;
        public string Purpose;   // 目的カード名
        public string[] Keywords;
        public FieldRowKind Kind;

        // 「既定値のまま」かの判定を欄ごとに差し替える(prop, 既定値の prop → 既定のままなら true)。null なら DataEquals で比べる。
        public Func<UnityEditor.SerializedProperty, UnityEditor.SerializedProperty, bool> DefaultCheck;

        // ラベル・説明・検索語・フィールド名の部分一致(大小無視)。
        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            var q = query.Trim();
            if (Has(Label, q) || Has(Hint, q) || Has(Field, q))
            {
                return true;
            }

            if (Keywords != null)
            {
                for (var i = 0; i < Keywords.Length; i++)
                {
                    if (Has(Keywords[i], q))
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

    // 種別ごとの静的な表(docs/1010 §2)。AssetDataBase の共通欄は基底が持ち、「管理情報」カードに出る
    // (既定で DisplayName と Category だけ「よく使う」、残りは「詳細」。Id / Version / Author / UpdatedAt は読み取り専用の一行)。
    // 種別ごとの表は SpecificEntries / SpecificPurposes を実装して書く。段の根拠は Validator(Error にする欄 = 必須)。
    public abstract class FieldGuide<TData> where TData : AssetDataBase
    {
        public const string AdminPurpose = "管理情報";

        private IReadOnlyList<FieldGuideEntry> _entries;
        private IReadOnlyList<string> _purposes;

        public Type DataType => typeof(TData);

        // 種別固有の欄(管理情報を除く)。
        protected abstract IReadOnlyList<FieldGuideEntry> SpecificEntries { get; }

        // 目的カードの並び(管理情報を除く。管理情報は常に最後に付く)。
        protected abstract IReadOnlyList<string> SpecificPurposes { get; }

        public IReadOnlyList<FieldGuideEntry> Entries => _entries ??= BuildEntries();

        public IReadOnlyList<string> Purposes => _purposes ??= BuildPurposes();

        // 種別の表を書くときの短縮記法。
        protected static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, params string[] keywords) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = keywords };

        // Flags(Pool / ポーズ / ネット)と Events(節目の連携)は共通欄だが、置くカードは種別が決める。
        protected static FieldGuideEntry FlagsEntry(string purpose) =>
            E("Flags", "動作の取り決め(Pool / ポーズ / ネット)", "Pool 上限・ポーズ時の挙動・ネット配送など。Loop のときは Pool 上限を設定してください。",
                FieldTier.Advanced, purpose, "プール", "ポーズ", "ネット", "優先度", "同期", "ロード", "ドメイン");

        protected static FieldGuideEntry EventsEntry(string purpose) =>
            E("Events", "きっかけで鳴らす・出す(イベント)", "出現・終了などの節目で、別の SE / VFX などを一緒に再生します。",
                FieldTier.Advanced, purpose, "イベント", "音", "SE", "連動", "同時", "節目");

        // AssetDataBase の共通欄(管理情報カード)。
        private static readonly FieldGuideEntry[] CommonTable =
        {
            E("DisplayName", "表示名", "一覧に出る名前。未設定ならファイル名。", FieldTier.Common, AdminPurpose, "名前", "タイトル"),
            E("Category", "カテゴリ", "AssetBrowser のフォルダ分け。", FieldTier.Common, AdminPurpose, "分類", "フォルダ"),
            E("Description", "説明", "この Data のメモ(任意)。", FieldTier.Advanced, AdminPurpose, "メモ", "備考"),
            E("Tags", "タグ", "検索用のタグ。", FieldTier.Advanced, AdminPurpose, "検索", "ラベル"),
            E("Icon", "アイコン", "一覧に出す画像(任意)。", FieldTier.Advanced, AdminPurpose, "画像", "サムネイル"),
            E("Assignee", "担当者", "担当者名(自由入力)。", FieldTier.Advanced, AdminPurpose, "担当", "人"),
            E("SpecUrl", "仕様書 URL", "この仕様の参照先 URL。", FieldTier.Advanced, AdminPurpose, "仕様", "リンク", "URL"),
            E("ChangeNote", "変更メモ", "何を変えたかのメモ(任意)。", FieldTier.Advanced, AdminPurpose, "履歴", "変更", "更新"),
        };

        private IReadOnlyList<FieldGuideEntry> BuildEntries()
        {
            var list = new List<FieldGuideEntry>(SpecificEntries);
            list.AddRange(CommonTable);
            return list;
        }

        private IReadOnlyList<string> BuildPurposes()
        {
            var list = new List<string>(SpecificPurposes);
            if (!list.Contains(AdminPurpose))
            {
                list.Add(AdminPurpose);
            }

            return list;
        }

        public FieldGuideEntry Find(string field)
        {
            var entries = Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Field == field)
                {
                    return entries[i];
                }
            }

            return null;
        }
    }
}
