using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Anchoring;

namespace DDrive.Editor.Anchor
{
    // AnchorGroupData の案内表(新デザイン AnchorGroupEditor 用。docs/1010 UX-11)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: AnchorGroupDataValidator の Error は「点が 1 つもない」「原点の Space=BoneName / NamedObject なのに Path が空」「Children の循環」だが、いずれも設定しだいで起きる条件なので
    //   「必須」の段にはせず、配置パターン・原点・出すものを「よく使う」にした(Error / Warning は欄の下に出て、折りたたまれていても見える)。Warning の対象(Overrides の Index・Children の AtIndex・
    //   Circle の半径 0・Line の長さ 0・ChancePerPoint 0・出すものが空)= よく使う、パターンごとの細かい欄と散らばりの細かい欄 = 詳細。
    //   点の一覧と試し出しは「かんたん」でも見せたいので必須の段に置く(バッジは出さない)。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(試し出し・点の一覧・スポーン先・手置きへの変換・原点の連鎖)。
    internal sealed class AnchorGroupFieldGuide : FieldGuide<AnchorGroupData>
    {
        public const string PurTry = "試し出し";
        public const string PurPoints = "点の一覧";
        public const string PurLayout = "配置パターン";
        public const string PurOrigin = "原点";
        public const string PurContent = "出すもの";
        public const string PurTiming = "タイミング・ばらつき";

        public const string RowTry = "(try)";
        public const string RowTools = "(tools)";
        public const string RowPoints = "(points)";
        public const string RowConvert = "(convert)";
        public const string RowOrigin = "(origin)";

        public static readonly AnchorGroupFieldGuide Instance = new AnchorGroupFieldGuide();

        private static readonly string[] PurposeList = { PurTry, PurPoints, PurLayout, PurOrigin, PurContent, PurTiming };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(RowTry, "全点 / 選択点を出す", "全点、または選んだ 1 点だけを、実際の VFX / SE の再生で確かめます(ランダム・ディレイ・確率も毎回サンプリングされます)。SceneView で点をクリックしても選べます。Grid は端の点、Circle は 0 番、Line は端の点をドラッグするとパターンの大きさが変わり、手置きの点は直接ドラッグできます。",
                FieldTier.Required, PurTry, FieldRowKind.Visual, "確認", "再生", "選択点", "全点", "試す", "停止", "上書き"),
            E(RowTools, "スポーン先と状態", "キャラクターや AnchorRig をここに入れると、原点の Path をその階層から探します。原点の解決状況・点の数・出すものの数がここに出ます。SceneView の表示切替もここです。",
                FieldTier.Common, PurTry, FieldRowKind.Visual, "スポーン先", "ボーン", "Path", "SceneView", "ハンドル", "AnchorPoint", "キャラクター", "解決", "状態"),

            E(RowPoints, "全点の一覧", "パターンと手置きの全点を番号順に 1 行ずつ。絞り込み・▶ で 1 点だけ試し出し・選択・上書きの追加ができます。20 行を超えるとスクロール式になります。",
                FieldTier.Required, PurPoints, FieldRowKind.Visual, "点", "番号", "一覧", "選択", "上書き", "出さない", "手置き", "パターン", "Points"),

            E(nameof(AnchorGroupData.Layout), "配置パターン", "Manual = 手置きの点だけ / Grid = 格子 / Circle = 円周(扇形も可)/ Line = 直線 / Random = 球内ランダム。パターンの下に手置きの点を足すこともできます。",
                FieldTier.Common, PurLayout, FieldRowKind.Normal, "パターン", "格子", "円", "直線", "ランダム", "Grid", "Circle", "Line", "Random", "Manual"),
            E(nameof(AnchorGroupData.GridCountX), "Grid: 横の数(X)", "格子の X 方向の点の数。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "格子", "グリッド", "X", "GridCountX"),
            E(nameof(AnchorGroupData.GridCountY), "Grid: 段の数(Y)", "格子の Y 方向(高さ)の段数。", FieldTier.Advanced, PurLayout, FieldRowKind.Normal, "格子", "グリッド", "Y", "高さ", "GridCountY"),
            E(nameof(AnchorGroupData.GridCountZ), "Grid: 奥行きの数(Z)", "格子の Z 方向の点の数。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "格子", "グリッド", "Z", "GridCountZ"),
            E(nameof(AnchorGroupData.GridSpacing), "Grid: 間隔(m)", "格子の点と点の間隔(メートル、軸ごと)。SceneView で端の点をドラッグしても変えられます。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "格子", "間隔", "GridSpacing"),
            E(nameof(AnchorGroupData.GridCentered), "Grid: 原点を中心にする", "ON: 原点が格子の中心 / OFF: 原点が格子の角(最小側)。", FieldTier.Advanced, PurLayout, FieldRowKind.Normal, "格子", "中心", "GridCentered"),
            E(nameof(AnchorGroupData.CircleCount), "Circle: 点の数", "円周上に並べる点の数。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "円", "サークル", "CircleCount"),
            E(nameof(AnchorGroupData.CircleRadius), "Circle: 半径(m)", "円の半径(メートル)。0 だと全点が原点に重なります。SceneView で 0 番の点をドラッグしても変えられます。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "円", "半径", "CircleRadius"),
            E(nameof(AnchorGroupData.CircleStartAngle), "Circle: 開始角(度)", "最初の点の角度。", FieldTier.Advanced, PurLayout, FieldRowKind.Normal, "円", "角度", "CircleStartAngle"),
            E(nameof(AnchorGroupData.CircleArc), "Circle: 範囲(度)", "円周のどこまでを使うか(360 = 全周、小さくすると扇形)。", FieldTier.Advanced, PurLayout, FieldRowKind.Normal, "円", "扇形", "角度", "CircleArc"),
            E(nameof(AnchorGroupData.CircleFaceOutward), "Circle: 各点を外向きにする", "ON: 各点の向きが外向き(放射状) / OFF: 原点と同じ向き。", FieldTier.Advanced, PurLayout, FieldRowKind.Normal, "円", "外向き", "放射", "CircleFaceOutward"),
            E(nameof(AnchorGroupData.LineCount), "Line: 点の数", "直線上に並べる点の数。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "直線", "ライン", "LineCount"),
            E(nameof(AnchorGroupData.LineLength), "Line: 長さ(m)", "直線の全長(メートル)。0 だと全点が原点に重なります。SceneView で端の点をドラッグしても変えられます。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "直線", "長さ", "LineLength"),
            E(nameof(AnchorGroupData.LineDirection), "Line: 向き", "直線が伸びる向き(原点のローカル)。", FieldTier.Advanced, PurLayout, FieldRowKind.Normal, "直線", "方向", "LineDirection"),
            E(nameof(AnchorGroupData.LineCentered), "Line: 原点を中心にする", "ON: 原点が直線の中央 / OFF: 原点が端。", FieldTier.Advanced, PurLayout, FieldRowKind.Normal, "直線", "中心", "LineCentered"),
            E(nameof(AnchorGroupData.RandomCount), "Random: 点の数", "球内にランダムに散らす点の数。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "ランダム", "RandomCount"),
            E(nameof(AnchorGroupData.RandomRadius), "Random: 半径(m)", "散らす球の半径(メートル)。", FieldTier.Common, PurLayout, FieldRowKind.Normal, "ランダム", "半径", "RandomRadius"),
            E(nameof(AnchorGroupData.RandomSeed), "Random: シード", "0 = 再生ごとに違う配置 / 0 以外 = 毎回同じ配置(エディタ表示も固定)。", FieldTier.Advanced, PurLayout, FieldRowKind.Normal, "ランダム", "乱数", "シード", "RandomSeed"),
            E(RowConvert, "手置きの点に変換", "今のパターンで計算された点を、そのまま手置きの点(Points)として書き込み、Layout を Manual に切り替えます。点を 1 つずつ動かしたくなったときに使います(Ctrl+Z で戻せます)。",
                FieldTier.Common, PurLayout, FieldRowKind.Visual, "変換", "手置き", "焼き付け", "Manual", "Points"),
            E(nameof(AnchorGroupData.Points), "手置きの点", "Manual のとき、またはパターンに足す点(名前・位置・向き・大きさ)。パターンの点のあとに続く番号になります。SceneView で直接ドラッグもできます。",
                FieldTier.Common, PurLayout, FieldRowKind.Normal, "手置き", "点", "Points", "追加"),

            E(nameof(AnchorGroupData.OriginAnchorId), "原点の Anchor", "原点として使う Anchor アセット。設定するとこちらが優先され、下の埋め込みの原点は無視されます。",
                FieldTier.Common, PurOrigin, FieldRowKind.Normal, "原点", "Anchor", "アセット", "OriginAnchorId", "基準"),
            E(nameof(AnchorGroupData.Origin), "原点(埋め込み)", "原点の Anchor アセットが 0 のときだけ使う埋め込みの定義。Space / Path で基準(ボーンなど)を決め、オフセットでずらします。",
                FieldTier.Common, PurOrigin, FieldRowKind.Normal, "原点", "埋め込み", "Space", "Path", "オフセット", "Origin"),
            E(RowOrigin, "原点の連鎖", "原点に Anchor アセットを使っているとき、そのルートからの連鎖と合成後のオフセットを確認します。押すと Anchor Editor で開きます。",
                FieldTier.Common, PurOrigin, FieldRowKind.Visual, "連鎖", "合成", "親子", "入れ子", "原点", "Anchor Editor"),

            E(nameof(AnchorGroupData.SharedVfx), "全点で出す VFX", "すべての点に出す VFX(複数可)。上書きのある点では、その点の指定が優先されます。",
                FieldTier.Common, PurContent, FieldRowKind.Normal, "VFX", "エフェクト", "共通", "SharedVfx"),
            E(nameof(AnchorGroupData.SharedSe), "全点で鳴らす SE", "すべての点で鳴らす SE(複数可)。",
                FieldTier.Common, PurContent, FieldRowKind.Normal, "SE", "音", "共通", "SharedSe"),
            E(nameof(AnchorGroupData.Overrides), "点ごとの上書き", "点の番号(一覧の #)ごとに、出さない(Skip)か、別の VFX / SE にします。点の一覧の「上書き」で足すと番号が入ります。",
                FieldTier.Common, PurContent, FieldRowKind.Normal, "上書き", "スキップ", "出さない", "差し替え", "Overrides"),
            E(nameof(AnchorGroupData.Children), "入れ子", "各点(AtIndex = -1 なら全点)に別の配置セットを出します。子の原点は無視され、その点が基準になります。",
                FieldTier.Common, PurContent, FieldRowKind.Normal, "入れ子", "子", "階層", "Children", "AtIndex"),

            E(nameof(AnchorGroupData.DelayPerIndex), "番号順のディレイ", "番号順に少しずつ遅らせる秒数(秒 / 点。端から順に出す・波紋)。",
                FieldTier.Common, PurTiming, FieldRowKind.Normal, "ディレイ", "遅延", "波紋", "順番", "DelayPerIndex"),
            E(nameof(AnchorGroupData.DelayJitterSec), "ディレイのばらつき", "ディレイ(秒)に足されるランダム秒数(0〜この値)。",
                FieldTier.Advanced, PurTiming, FieldRowKind.Normal, "ディレイ", "ランダム", "DelayJitterSec"),
            E(nameof(AnchorGroupData.ChancePerPoint), "各点の確率", "0〜1。1 未満でランダムに欠けます。0 だと何も出ません(警告)。",
                FieldTier.Common, PurTiming, FieldRowKind.Normal, "確率", "チャンス", "間引く", "ChancePerPoint"),
            E(nameof(AnchorGroupData.PositionJitterRadius), "位置のばらつき", "点ごとに個別サンプリングする位置のランダム半径(メートル)。",
                FieldTier.Advanced, PurTiming, FieldRowKind.Normal, "ランダム", "散らばり", "半径", "PositionJitterRadius"),
            E(nameof(AnchorGroupData.EulerJitter), "向きのばらつき", "点ごとに個別サンプリングする回転(度)の ±範囲。",
                FieldTier.Advanced, PurTiming, FieldRowKind.Normal, "ランダム", "回転", "EulerJitter"),
            E(nameof(AnchorGroupData.ScaleRange), "大きさのばらつき", "点ごとに個別サンプリングするスケール倍率の範囲(x = 最小、y = 最大)。",
                FieldTier.Advanced, PurTiming, FieldRowKind.Normal, "ランダム", "スケール", "ScaleRange"),

            FlagsEntry(AdminPurpose),
            EventsEntry(AdminPurpose),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
