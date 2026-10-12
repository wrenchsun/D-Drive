using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Ui;

namespace DDrive.Editor.Ui
{
    // UiTweenData の案内表(新デザイン UiTweenEditor 用。docs/1010 UX-12)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: UiTweenDataValidator の Error は「PathMove なのに Path の制御点が 2 点未満」だけで、その設定を選んだときだけの条件なので「必須」の欄は無い。
    //   ただし「かんたん」で雛形を選んで動きを見られるよう、雛形とトラックの一覧は必須の段に置く(バッジは出さない)。
    //   Warning の対象(Tracks が空・Loop で Duration 0・TotalDuration が最長トラックより短い)と普段触る欄(Tracks・TotalDuration)= よく使う。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(雛形・確認する)。雛形は「かんたん」でも見せたいので必須の段に置く(バッジは出さない)。
    internal sealed class UiTweenFieldGuide : FieldGuide<UiTweenData>
    {
        public const string PurStart = "雛形から始める";
        public const string PurTracks = "トラック";
        public const string PurTotal = "全体の長さ";
        public const string PurCheck = "確認する";

        public const string RowPresets = "(presets)";
        public const string RowCheck = "(check)";

        public static readonly UiTweenFieldGuide Instance = new UiTweenFieldGuide();

        private static readonly string[] PurposeList = { PurStart, PurTracks, PurTotal, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(RowPresets, "雛形", "出現・消滅・常時・強調の定番プリセットや、登録済みのカタログから選んで Tracks に入れます(置き換え / 追加。Undo で戻せます)。数値はたたき台なので、あとで調整してください。",
                FieldTier.Required, PurStart, FieldRowKind.Visual, "プリセット", "ギャラリー", "テンプレート", "FadeIn", "PopIn", "Slide", "お気に入り", "カタログ", "登録", "保存"),

            E(nameof(UiTweenData.Tracks), "トラック(動かす項目)", "並列に動く項目の一覧。1 行 = 1 トラック(位置・拡縮・回転・透明度・色など)。バーのドラッグで開始と長さを変え、⚙ ですべての欄を開きます。同時に動かせるのは 8 本まで。",
                FieldTier.Required, PurTracks, FieldRowKind.Visual, "動き", "位置", "拡縮", "スケール", "回転", "透明度", "アルファ", "色", "イージング", "ループ", "遅れ", "ディレイ", "経路", "スプライン", "Delay", "Motion", "Path", "FromValue", "ToValue"),

            E(nameof(UiTweenData.TotalDuration), "全体の長さ(秒)", "0 = 自動(いちばん遅く終わるトラックまで)。手動で決めると、最長トラックより短いときに警告が出ます。",
                FieldTier.Common, PurTotal, FieldRowKind.Normal, "尺", "秒", "自動", "合計", "TotalDuration"),

            E(RowCheck, "実際の要素で確かめる", "シーン上の UI 要素(または確認用シーンの仮画像)を対象にして、実際に動かして確かめます。再生が終わる・止めると元の見た目に戻ります。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "確認", "再生", "プレビュー", "要素", "収集", "確認用シーン", "停止", "撤去"),

            FlagsEntry(AdminPurpose),
            EventsEntry(AdminPurpose),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
