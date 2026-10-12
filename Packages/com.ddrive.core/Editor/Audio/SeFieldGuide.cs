using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Audio;

namespace DDrive.Editor.Audio
{
    // SeData の案内表(新デザイン SeEditor 用。docs/1010 UX-3)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: SeDataValidator が Error にする = Clips のみ → 必須。Warning の対象(Mixer・Volume・StartOffsetSec・Sources)と普段触る欄(MaxConcurrent・距離・Spatial)= よく使う。
    // 既定のままで警告が出ない = 詳細。
    internal sealed class SeFieldGuide : FieldGuide<SeData>
    {
        public const string PurPlay = "鳴らす";
        public const string PurCut = "切る・つなぐ";
        public const string PurHear = "聞こえ方";
        public const string PurLimit = "同時再生・連打";
        public const string PurLink = "音・イベントと合わせる";

        public static readonly SeFieldGuide Instance = new SeFieldGuide();

        private static readonly string[] PurposeList = { PurPlay, PurCut, PurHear, PurLimit, PurLink };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(SeData.Clips), "鳴らす音(Clips)", "実際に再生される AudioClip。複数入れると「選び方」に従って 1 つずつ鳴ります。「切る・つなぐ」でトリムして「トリミングを適用」すると自動で入ります。",
                FieldTier.Required, PurPlay, FieldRowKind.Normal, "クリップ", "音源", "AudioClip", "wav", "音"),
            E(nameof(SeData.SelectMode), "複数あるときの選び方", "Random = 毎回ランダム / RoundRobin = 順番に一巡 / First = いつも先頭。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "ランダム", "順番", "ラウンドロビン", "バリエーション"),
            E(nameof(SeData.Volume), "基準音量", "0〜1。再生中に動かすと、鳴っている音にそのまま反映されます。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "ボリューム", "音量", "大きさ", "小さく"),
            E(nameof(SeData.PitchRange), "ピッチの揺らぎ", "最小(X)と最大(Y)。鳴らすたびにこの範囲でランダムに高さを変えます。両方 1 なら変えません。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "高さ", "ピッチ", "ランダム", "揺れ"),
            E(nameof(SeData.Loop), "ループして鳴らす", "ON ならずっと繰り返します。OFF なら終わりで自動的に片付きます。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "繰り返し", "ループ"),

            E(nameof(SeData.Sources), "元の音とトリム範囲(Sources)", "元のクリップ(書き換わりません)と切り出す範囲。波形の端をドラッグして範囲を決め、「トリミングを適用」で Clips を作り直します。",
                FieldTier.Common, PurCut, FieldRowKind.Visual, "トリム", "波形", "切る", "無音", "範囲", "開始", "終了", "元データ"),
            E(nameof(SeData.StartOffsetSec), "再生を始める位置(秒)", "トリムとは別に、鳴らすたびにこの位置から始めます(元の音は変わりません)。",
                FieldTier.Common, PurCut, FieldRowKind.Visual, "頭出し", "オフセット", "開始位置", "スキップ"),

            E(nameof(SeData.Mixer), "出力先(Mixer)", "AudioMixerGroup。未割当だと警告になります。",
                FieldTier.Common, PurHear, FieldRowKind.Normal, "ミキサー", "グループ", "出力", "AudioMixerGroup", "バス"),
            E(nameof(SeData.Spatial), "3D の鳴らし方", "None = 2D / Anchor = 位置の設定どおりに追従 / AtPosition = 呼び出し側が座標を渡す。",
                FieldTier.Common, PurHear, FieldRowKind.Normal, "3D", "2D", "空間", "定位", "位置"),
            E(nameof(SeData.AnchorId), "共通の位置(Anchor)", "複数の Data で位置を共有したいときに Anchor アセットを指定。設定すると下の位置は無視されます。",
                FieldTier.Advanced, PurHear, FieldRowKind.Visual, "共有", "共通", "再利用", "アセット"),
            E(nameof(SeData.Anchor), "鳴る位置(この Data 専用)", "Spatial が Anchor のときの位置。Space=World ならワールド座標、ボーン名ならキャラの部位。高さは LocalOffset の Y。",
                FieldTier.Advanced, PurHear, FieldRowKind.Visual, "位置", "場所", "高さ", "オフセット", "ボーン", "アタッチ", "向き", "座標"),
            E(nameof(SeData.MinDistance), "フル音量の距離", "この距離までは減衰しません(メートル)。",
                FieldTier.Common, PurHear, FieldRowKind.Normal, "距離", "近く", "減衰", "メートル"),
            E(nameof(SeData.MaxDistance), "聞こえなくなる距離", "この距離を超えると聞こえません(メートル)。フル音量の距離より大きくしてください。",
                FieldTier.Common, PurHear, FieldRowKind.Normal, "距離", "遠く", "減衰", "メートル"),
            E(nameof(SeData.Rolloff), "距離による減衰カーブ", "距離と音量の関係を自分で描きたいときだけ(任意)。",
                FieldTier.Advanced, PurHear, FieldRowKind.Normal, "カーブ", "減衰", "ロールオフ"),
            E(nameof(SeData.Spread), "音の広がり(Spread)", "0 = 点音源(近くで左右がはっきり) / 180 = 無指向。",
                FieldTier.Advanced, PurHear, FieldRowKind.Normal, "指向性", "広がり", "ステレオ"),
            E(nameof(SeData.DopplerEnabled), "ドップラー効果", "動く音源で高さが変わる効果。ループしない短い音では分かりにくいです。",
                FieldTier.Advanced, PurHear, FieldRowKind.Normal, "ドップラー", "移動"),

            E(nameof(SeData.MaxConcurrent), "同時に鳴らせる数", "超えたときは一番古い音から止めます。",
                FieldTier.Common, PurLimit, FieldRowKind.Normal, "同時", "上限", "ポリフォニー", "ボイス数"),
            E(nameof(SeData.CooldownSec), "連打防止の間隔(秒)", "この時間内の再生要求は無視します。",
                FieldTier.Advanced, PurLimit, FieldRowKind.Normal, "連打", "クールダウン", "間隔"),
            FlagsEntry(PurLimit),

            EventsEntry(PurLink),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
