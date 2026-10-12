using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Model;

namespace DDrive.Editor.Model
{
    // ModelData の案内表(新デザイン ModelEditor 用。docs/1010 UX-9)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: ModelDataValidator が Error にする = Prefab 未設定 → Prefab = 必須。
    //   Animator があるのに Avatar が無い(Error)→ Avatar = よく使う(条件つきなので必須にはしない。出れば「注意あり」で必ず見える)。
    //   Warning にする欄(Slots の Material 未割当)・日常的に触る欄(DefaultAnimation・RenderLayer・LightLayerMask)= よく使う。
    //   既定のままでよい欄(Lod の上書き)= 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(モデルの情報・スロットの操作・Avatar の情報・並べて確認・シーンで確認)。
    // 検索・表示モード・目的別カードの対象にするために表へ載せている。
    internal sealed class ModelFieldGuide : FieldGuide<ModelData>
    {
        public const string PurPrefab = "見た目（Prefab）";
        public const string PurSlots = "Material スロット";
        public const string PurAnim = "アニメーションと Avatar";
        public const string PurRender = "描画";
        public const string PurCheck = "確認する（シーン・並べて確認）";

        public const string RowInfo = "(info)";
        public const string RowSlotTools = "(slottools)";
        public const string RowAvatarInfo = "(avatar)";
        public const string RowParallel = "(parallel)";
        public const string RowScene = "(scene)";

        public static readonly ModelFieldGuide Instance = new ModelFieldGuide();

        private static readonly string[] PurposeList = { PurPrefab, PurSlots, PurAnim, PurRender, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E("Prefab", "Prefab", "Spawn する実体(モデルの Prefab)。未設定だと何も出せません。",
                FieldTier.Required, PurPrefab, FieldRowKind.Normal, "プレハブ", "モデル", "FBX", "実体"),
            E(RowInfo, "モデルの情報", "Prefab を調べた結果(読み取り専用)。Renderer・Material の数、BlendShape、Animator、Prefab に付いた IModelInstanceListener。",
                FieldTier.Common, PurPrefab, FieldRowKind.Visual, "情報", "Renderer", "BlendShape", "ブレンドシェイプ", "表情", "Animator", "Listener", "IModelInstanceListener", "FC"),

            E(RowSlotTools, "スロットの自動収集", "Prefab の Renderer を走査してスロットの一覧を作ります。元ファイル(FBX / Material)を読み直して MaterialData を作り直すこともできます。",
                FieldTier.Common, PurSlots, FieldRowKind.Visual, "自動収集", "再読み込み", "Binder", "元ファイル", "FBX", "Maya"),
            E("Slots", "Material スロット", "Renderer の名前 + スロット番号ごとに、割り当てる MaterialData を 1 行ずつ決めます。「開く」で Material Editor へ。↺ は Prefab の Renderer が使っている Material から割り当て直します。",
                FieldTier.Common, PurSlots, FieldRowKind.Visual, "スロット", "マテリアル", "Renderer", "RendererPath", "SlotIndex", "割り当て", "Material"),

            E("DefaultAnimation", "最初に流すアニメ", "任意。Spawn 直後に自動で再生するアニメーション。「Anim Editor で開く」で中身を調整できます。",
                FieldTier.Common, PurAnim, FieldRowKind.Normal, "アニメ", "DefaultAnimation", "自動再生", "Idle"),
            E("Avatar", "Avatar(Humanoid)", "Humanoid の場合の Avatar。Cutscene の Humanoid アニメを使うモデルには必要です。Animator があって Avatar が無いとエラーになります。",
                FieldTier.Common, PurAnim, FieldRowKind.Normal, "ヒューマノイド", "Humanoid", "アバター", "リグ", "Cutscene"),
            E(RowAvatarInfo, "Avatar の情報", "Data の Avatar と Prefab の Animator の Avatar を見比べた結果(読み取り専用)。Cutscene の検査が見る欄です。",
                FieldTier.Common, PurAnim, FieldRowKind.Visual, "Avatar", "Humanoid", "Generic", "Cutscene", "検査"),

            E("RenderLayer", "描画のレイヤー番号", "Sorting / RenderingLayerMask に使う値。",
                FieldTier.Common, PurRender, FieldRowKind.Normal, "レイヤー", "Sorting", "RenderLayer"),
            E("LightLayerMask", "ライトレイヤー", "どのライトレイヤーのライトを受けるか。0 = Prefab の Renderer の設定をそのまま使う(上書きしない)。既定は 1(Default)。",
                FieldTier.Common, PurRender, FieldRowKind.Normal, "ライト", "ライトレイヤー", "RenderingLayerMask", "LightLayer"),
            E("Lod", "LOD の閾値の上書き", "任意。Prefab の LODGroup の切り替えしきい値(0〜1)をデータ側から差し替えます。LOD の数と揃えてください。",
                FieldTier.Advanced, PurRender, FieldRowKind.Normal, "LOD", "LODGroup", "距離", "切り替え"),

            E(RowParallel, "並べて確認(複数モデル)", "別の ModelData を最大 4 つ、対象の隣に 2m 間隔で並べて見比べます(保存されません)。「確認用シーン」で対象を置き直すと消えます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "並列", "並べる", "比較", "複数", "見比べ"),
            E(RowScene, "シーンで確認", "確認用シーン / 今のシーンにモデルを置き(保存されない)SceneView で見ます。ターンテーブルで回せます。▶ で DefaultAnimation も流せます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "シーン", "確認用シーン", "配置", "撤去", "ターンテーブル", "SceneView", "再生"),

            // Flags / Events は AssetDataBase の共通欄。Model は管理情報カードの詳細に置く。
            FlagsEntry(FieldGuide<ModelData>.AdminPurpose),
            EventsEntry(FieldGuide<ModelData>.AdminPurpose),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
