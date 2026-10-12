using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Ui;

namespace DDrive.Editor.CanvasTool
{
    // CanvasData の案内表(新デザイン CanvasEditor 用。docs/1010 UX-4)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: CanvasDataValidator が Error にする欄のうち、いつでも埋めておくべきもの = Prefab(未設定は Error)→ 必須。
    // 経路の不一致(FirstSelected・ElementFx / 配線のパス)は、欄に Error が出た時点で「必須」と同じく隠れない(NeedsAttention)。
    // 普段触る欄 = よく使う / ほぼ既定のままでよい欄・まれにしか使わない欄(埋め込み・スライダー)= 詳細。
    internal sealed class CanvasFieldGuide : FieldGuide<CanvasData>
    {
        public const string PurShow = "出す";
        public const string PurFx = "要素の動き（ElementFx）";
        public const string PurWire = "ボタン・スライダーの配線";
        public const string PurEmbed = "埋め込み Canvas";
        public const string PurNav = "カーソル移動（Navigation）";
        public const string PurLink = "音・イベントと合わせる";

        public static readonly CanvasFieldGuide Instance = new CanvasFieldGuide();

        private static readonly string[] PurposeList = { PurShow, PurFx, PurWire, PurEmbed, PurNav, PurLink };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(CanvasData.Prefab), "画面の実体(Prefab)", "開く Canvas の Prefab。未設定だと空の Placeholder になります。要素を動かすときは上の「Prefab で開く」から。",
                FieldTier.Required, PurShow, FieldRowKind.Normal, "プレハブ", "画面", "UI", "ゲームオブジェクト"),
            E(nameof(CanvasData.Layer), "表示レイヤー", "HUD / Menu / Popup / Overlay / Loading。あとのレイヤーほど手前に積まれます。",
                FieldTier.Common, PurShow, FieldRowKind.Normal, "レイヤー", "重なり", "手前", "HUD", "Menu", "Popup", "Overlay", "Loading"),
            E(nameof(CanvasData.SortOffset), "同じレイヤー内の重ね順", "同じレイヤーに積んだ画面どうしの前後を微調整します(大きいほど手前)。",
                FieldTier.Advanced, PurShow, FieldRowKind.Normal, "ソート", "順序", "sortingOrder"),
            E(nameof(CanvasData.OpenTransition), "開くときの演出", "None / Fade / Slide / Scale / Anim。秒数とイージングも決められます。",
                FieldTier.Common, PurShow, FieldRowKind.Normal, "オープン", "出現", "トランジション", "フェード", "スライド", "アニメ"),
            E(nameof(CanvasData.CloseTransition), "閉じるときの演出", "閉じるときの演出。開くときと同じ形式です。",
                FieldTier.Common, PurShow, FieldRowKind.Normal, "クローズ", "消える", "トランジション", "フェード", "スライド", "アニメ"),
            E(nameof(CanvasData.CloseOnBack), "戻る操作で閉じる", "ON なら戻る操作(Ui.CloseTop)で閉じる対象に含めます。",
                FieldTier.Advanced, PurShow, FieldRowKind.Normal, "戻る", "キャンセル", "バック", "CloseTop"),
            E(nameof(CanvasData.ModalBlocksInput), "背後の入力をブロック(モーダル)", "ON なら、開いている間は背後に積まれた画面の入力を受け付けません(Popup 向け)。",
                FieldTier.Common, PurShow, FieldRowKind.Normal, "モーダル", "ブロック", "入力", "ポップアップ"),
            E(nameof(CanvasData.PauseGameWhileOpen), "開いている間ゲームを止める", "ON なら、開いている間ゲームの進行(Gameplay)を止めます。",
                FieldTier.Advanced, PurShow, FieldRowKind.Normal, "ポーズ", "停止", "メニュー", "Pause"),
            FlagsEntry(PurShow),

            E(nameof(CanvasData.ElementEffects), "要素ごとの動き", "Prefab の中の要素ごとに、出るとき(Appear)・出ている間(Idle)・消えるとき(Disappear)の動きを決めます。「自動収集」で要素の行を作れます。",
                FieldTier.Common, PurFx, FieldRowKind.Visual, "エフェクト", "アニメ", "出現", "待機", "消える", "プリセット", "ElementFx", "tween", "トゥイーン", "Appear", "Idle", "Disappear", "収集"),

            E(nameof(CanvasData.Buttons), "ボタンの配線", "Prefab の UiButton ごとに、押したときの動作(トリガー → アクション)を決めます。",
                FieldTier.Common, PurWire, FieldRowKind.Visual, "ボタン", "クリック", "シグナル", "SignalKey", "OpenCanvas", "CloseSelf", "トリガー", "アクション", "SE", "ButtonWire", "配線"),
            E(nameof(CanvasData.Sliders), "スライダーの配線", "Prefab の UiSlider ごとに、変えたとき・確定したときの動作(SetOption で設定の値に直結)を決めます。",
                FieldTier.Advanced, PurWire, FieldRowKind.Normal, "スライダー", "音量", "オプション", "SetOption", "SliderWire", "つまみ"),

            E(nameof(CanvasData.EmbeddedCanvases), "埋め込み Canvas(入れ子の子 Canvas)", "Prefab の中に入れ子で入っている子 Canvas を登録します。子の動き・配線は子の CanvasData に持ち、親を開いたときも効きます。",
                FieldTier.Advanced, PurEmbed, FieldRowKind.Visual, "入れ子", "子", "ネスト", "有効", "無効", "StartInactive", "RootPath", "EmbeddedCanvas"),

            E(nameof(CanvasData.Navigation), "カーソルの移動先(Navigation)", "十字キー / スティックでの移動先を、要素ごとに決めます。「Selectable を自動収集」→ グラフでつなぎます。",
                FieldTier.Common, PurNav, FieldRowKind.Visual, "十字キー", "ナビゲーション", "グラフ", "フォーカス", "Selectable", "上下左右", "NavNode", "到達", "パッド"),
            E(nameof(CanvasData.FirstSelected), "最初に選ばれる要素(FirstSelected)", "開いた直後にカーソルが乗る要素のパス。Prefab の中に無い名前はエラーになります。",
                FieldTier.Common, PurNav, FieldRowKind.Normal, "初期フォーカス", "最初", "選択", "SetSelectedGameObject"),

            EventsEntry(PurLink),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
