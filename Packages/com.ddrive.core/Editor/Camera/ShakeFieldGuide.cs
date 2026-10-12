using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.CameraShake;

namespace DDrive.Editor.CameraFx
{
    // CameraShakeData の案内表(新デザイン ShakeEditor 用。docs/1010 UX-10)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: CameraShakeDataValidator に Error は無い(Warning のみ)ので「必須」の欄は無い。Warning の対象(PosAmplitude / RotAmplitude・Envelope の尺・
    //   MaxStack・TraumaWeight)と普段触る欄(Pattern・Frequency)= よく使う、既定のままでよい欄(Space)= 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(雛形・波形グラフ・確認する)。雛形と波形グラフは「かんたん」でも見せたいので必須の段に置く(バッジは出さない)。
    internal sealed class ShakeFieldGuide : FieldGuide<CameraShakeData>
    {
        public const string PurStart = "雛形から始める";
        public const string PurGraph = "波形を見る";
        public const string PurShape = "揺れ方";
        public const string PurPower = "強さ";
        public const string PurStack = "重なったとき";
        public const string PurCheck = "確認する(カメラ)";

        public const string RowPresets = "(presets)";
        public const string RowGraph = "(graph)";
        public const string RowCheck = "(check)";

        public static readonly ShakeFieldGuide Instance = new ShakeFieldGuide();

        private static readonly string[] PurposeList = { PurStart, PurGraph, PurShape, PurPower, PurStack, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(RowPresets, "雛形", "10 種の雛形のどれかを押すと、波形・揺れ幅・減衰・重なりの設定を一式書き換えます(Undo で戻せます)。数値はたたき台なので、あとで調整してください。",
                FieldTier.Required, PurStart, FieldRowKind.Visual, "プリセット", "テンプレート", "Pulse", "Explosion", "Heartbeat", "爆発", "着地", "地震"),

            E(RowGraph, "波形グラフ", "減衰(Envelope)と、位置・回転の揺れを時間軸に重ねた参考表示。右端の縦棒をドラッグすると揺れの長さが変わります。",
                FieldTier.Required, PurGraph, FieldRowKind.Visual, "グラフ", "カーブ", "波形", "長さ", "尺", "Envelope", "ドラッグ"),

            E(nameof(CameraShakeData.Pattern), "波形の種類", "Perlin Noise = 手ブレ・地響き / Decay Sine = 一定のリズムで震える / Impulse = 一方向にグッと押される一撃 / Custom Curve = 今は Impulse と同じ。",
                FieldTier.Common, PurShape, FieldRowKind.Normal, "パターン", "ノイズ", "サイン", "インパルス", "Pattern", "Perlin"),
            E(nameof(CameraShakeData.Frequency), "揺れの速さ(Frequency)", "Hz。Perlin Noise / Decay Sine で使います。大きいほど細かく速く揺れます(時間で変えることもできます)。",
                FieldTier.Common, PurShape, FieldRowKind.Normal, "周波数", "ヘルツ", "Hz", "速さ", "細かさ"),
            E(nameof(CameraShakeData.Envelope), "減衰と長さ(Envelope)", "揺れの強さが時間とともにどう弱まるかと、揺れ全体の長さ。既定は 0.3 秒で 1→0 に収まります。",
                FieldTier.Common, PurShape, FieldRowKind.Normal, "減衰", "フェード", "尺", "長さ", "秒", "イージング", "エンベロープ", "Duration"),
            E(nameof(CameraShakeData.Space), "揺れる向きの基準(Space)", "Camera Local = カメラの向きに追従(既定)/ World = 常に同じワールド方向 / From Source = 発生位置からカメラへ押し出す。",
                FieldTier.Advanced, PurShape, FieldRowKind.Normal, "空間", "方向", "基準", "ワールド", "ローカル", "Space"),

            E(nameof(CameraShakeData.PosAmplitude), "位置の揺れ幅(m)", "上下・左右・前後にどれだけ動くか(メートル)。軸ごとに設定できます。大きすぎると酔いやすくなります。",
                FieldTier.Common, PurPower, FieldRowKind.Normal, "位置", "強さ", "振幅", "アンプリチュード", "幅", "PosAmplitude"),
            E(nameof(CameraShakeData.RotAmplitude), "回転の揺れ幅(度)", "どれだけ傾く・回るか(度)。Z だけ入れると「首をかしげる」ような揺れになります。",
                FieldTier.Common, PurPower, FieldRowKind.Normal, "回転", "強さ", "傾き", "ロール", "振幅", "RotAmplitude"),

            E(nameof(CameraShakeData.TraumaWeight), "重なったときの強さ", "複数の揺れが同時に起きたときの寄与度。既定 1。大きいほど、他の揺れと重なったときによく効きます。0 以下だと合成に寄与しません。",
                FieldTier.Common, PurStack, FieldRowKind.Normal, "トラウマ", "合成", "重み", "TraumaWeight"),
            E(nameof(CameraShakeData.MaxStack), "同時に重ねられる数", "同じ揺れを同時に何個まで重ねるか。既定 3。超えた分は無視されます。0 以下だと常に無視されます。",
                FieldTier.Common, PurStack, FieldRowKind.Normal, "同時", "上限", "連打", "スタック", "MaxStack"),

            E(RowCheck, "実際にカメラを揺らす", "確認用シーン(または今のシーンの Camera.main)を実際に揺らして確かめます。連打すると、揺れが重なったときの合成(揺れすぎない仕組み)も確認できます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "確認", "再生", "連打", "確認用シーン", "停止", "カメラ"),

            FlagsEntry(AdminPurpose),
            EventsEntry(AdminPurpose),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
