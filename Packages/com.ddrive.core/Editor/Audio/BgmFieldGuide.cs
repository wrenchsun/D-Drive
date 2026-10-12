using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Audio;

namespace DDrive.Editor.Audio
{
    // BgmData の案内表(新デザイン BgmEditor 用。docs/1010 UX-3)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: BgmDataValidator が Error にする = LoopBody → 必須。ループ範囲(Error になりうる)・Warning の対象(Mixer・Volume)と普段触る欄(フェード)= よく使う。
    internal sealed class BgmFieldGuide : FieldGuide<BgmData>
    {
        public const string PurPlay = "鳴らす";
        public const string PurLoop = "切る・つなぐ";
        public const string PurHear = "聞こえ方";
        public const string PurBeat = "拍に合わせる";
        public const string PurLink = "音・イベントと合わせる";

        public static readonly BgmFieldGuide Instance = new BgmFieldGuide();

        private static readonly string[] PurposeList = { PurPlay, PurLoop, PurHear, PurBeat, PurLink };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(BgmData.LoopBody), "曲の本体(LoopBody)", "繰り返し流す曲のクリップ。これだけ入れれば鳴らせます。",
                FieldTier.Required, PurPlay, FieldRowKind.Normal, "クリップ", "曲", "本体", "AudioClip", "BGM"),
            E(nameof(BgmData.Intro), "イントロ(任意)", "入れると、先にこれを流してから本体へ切れ目なくつなぎます。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "導入", "前奏"),
            E(nameof(BgmData.Volume), "基準音量", "0〜1。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "ボリューム", "音量", "大きさ"),
            FlagsEntry(PurPlay),

            E(nameof(BgmData.LoopStartSec), "ループの範囲(開始〜終了)", "本体のどこからどこまでを繰り返すか。波形の端をドラッグ。終了が 0 なら最後まで。",
                FieldTier.Common, PurLoop, FieldRowKind.Visual, "ループ点", "波形", "開始", "終了", "範囲", "繰り返し"),
            E(nameof(BgmData.LoopEndSec), "ループの終わり(秒)", "ループの終わり。0 = クリップの最後まで。",
                FieldTier.Common, PurLoop, FieldRowKind.Visual, "ループ点", "終了", "終わり"),
            E(nameof(BgmData.FadeIn), "フェードイン", "形と秒数。他の BGM からクロスフェードで入ってくるときに使います。",
                FieldTier.Common, PurLoop, FieldRowKind.Normal, "クロスフェード", "フェード", "入り", "イージング"),
            E(nameof(BgmData.FadeOut), "フェードアウト", "形と秒数。止めるとき、または他の BGM へ切り替わるときに使います。",
                FieldTier.Common, PurLoop, FieldRowKind.Normal, "クロスフェード", "フェード", "終わり", "イージング"),

            E(nameof(BgmData.Mixer), "出力先(Mixer)", "AudioMixerGroup。未割当だと警告になります。",
                FieldTier.Common, PurHear, FieldRowKind.Normal, "ミキサー", "グループ", "出力", "AudioMixerGroup", "バス"),

            E(nameof(BgmData.Bpm), "テンポ(BPM)", "拍に合わせた演出に使います(任意)。",
                FieldTier.Advanced, PurBeat, FieldRowKind.Normal, "BPM", "拍", "ビート", "テンポ", "リズム"),

            EventsEntry(PurLink),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
