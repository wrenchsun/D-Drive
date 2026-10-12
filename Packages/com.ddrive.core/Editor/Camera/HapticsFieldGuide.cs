using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Haptics;

namespace DDrive.Editor.CameraFx
{
    // HapticsData の案内表(新デザイン HapticsEditor 用。docs/1010 UX-10)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: HapticsDataValidator に Error は無い(Warning / Info のみ)ので「必須」の欄は無い。Warning の対象(LowFreq / HighFreq の尺)= よく使う、
    //   既定のままでよい欄(Priority・LocalPlayerOnly・Extensions〔未実装〕)= 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(雛形・波形グラフ・確認する)。雛形と波形グラフは「かんたん」でも見せたいので必須の段に置く(バッジは出さない)。
    internal sealed class HapticsFieldGuide : FieldGuide<HapticsData>
    {
        public const string PurStart = "雛形から始める";
        public const string PurGraph = "波形を見る";
        public const string PurMotor = "モーター";
        public const string PurControl = "同時に鳴ったとき・配送";
        public const string PurCheck = "確認する(パッド)";

        public const string RowPresets = "(presets)";
        public const string RowGraph = "(graph)";
        public const string RowCheck = "(check)";

        public static readonly HapticsFieldGuide Instance = new HapticsFieldGuide();

        private static readonly string[] PurposeList = { PurStart, PurGraph, PurMotor, PurControl, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(RowPresets, "雛形", "10 種の雛形のどれかを押すと、2 つのモーターの強さカーブと優先度を一式書き換えます(Undo で戻せます)。数値はたたき台なので、あとで調整してください。",
                FieldTier.Required, PurStart, FieldRowKind.Visual, "プリセット", "テンプレート", "Pulse", "Explosion", "Heartbeat", "爆発", "着地", "地震"),

            E(RowGraph, "波形グラフ", "低周波(ドスン)と高周波(ビリビリ)の強さを時間軸に重ねて表示。左端の点で強さ、右端の縦棒で長さをドラッグして変えられます。",
                FieldTier.Required, PurGraph, FieldRowKind.Visual, "グラフ", "カーブ", "波形", "長さ", "尺", "強さ", "ドラッグ"),

            E(nameof(HapticsData.LowFreq), "低周波モーター(ドスン)", "「ドスン」という低い振動。強さが時間とともにどう変わるかと長さ。既定は 0.2 秒で 1→0 に収まります。",
                FieldTier.Common, PurMotor, FieldRowKind.Normal, "低周波", "ロー", "ドスン", "モーター", "強さ", "尺", "長さ", "LowFreq"),
            E(nameof(HapticsData.HighFreq), "高周波モーター(ビリビリ)", "「ビリビリ」という高い振動。低周波と同じ形式です。",
                FieldTier.Common, PurMotor, FieldRowKind.Normal, "高周波", "ハイ", "ビリビリ", "モーター", "強さ", "尺", "長さ", "HighFreq"),

            E(nameof(HapticsData.Priority), "優先度(Priority)", "同時に鳴ったときの優先の目安。いまは参考情報のみ(重なったら、チャンネルごとに一番強い値だけが使われます)。",
                FieldTier.Advanced, PurControl, FieldRowKind.Normal, "優先", "Priority", "High", "Low"),
            E(nameof(HapticsData.LocalPlayerOnly), "自分のときだけ振動(LocalPlayerOnly)", "ON(既定)= 自分に起きた事象のときだけ振動。OFF = 全員に配送する想定(ネット対応前は常にローカル再生)。",
                FieldTier.Advanced, PurControl, FieldRowKind.Normal, "ローカル", "自分", "ネット", "配送", "LocalPlayerOnly"),
            E(nameof(HapticsData.Extensions), "機種別の拡張(Extensions)", "DualSense のアダプティブトリガーなど機種別の特殊な振動。未対応で、設定すると情報が出るだけです。",
                FieldTier.Advanced, PurControl, FieldRowKind.Normal, "拡張", "DualSense", "アダプティブトリガー", "プラットフォーム", "Extensions"),

            E(RowCheck, "実際にパッドを振動させる", "接続中のゲームパッドを実際に振動させて確かめます(Edit Mode のままで動きます)。停止・ウィンドウを閉じる・フォーカスが外れる・再生ボタンのいずれでも必ず止まります。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "確認", "再生", "パッド", "ゲームパッド", "Test on Pad", "停止", "接続"),

            FlagsEntry(AdminPurpose),
            EventsEntry(AdminPurpose),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
