using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Material;

namespace DDrive.Editor.Materials
{
    // MaterialData の案内表(新デザイン MaterialEditor 用。docs/1010 UX-8)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: MaterialDataValidator が Error にする = シェーダーがプロジェクトのパイプラインに合わない(Incompatible)→ Shader = 必須
    //   (未設定は Warning だが、Material の中心なので必須にして「かんたん」で最初に見せる)。Warning/Info にする欄(Albedo・RenderQueue・Emission・Specific・Passes/Keywords・Anims)= よく使う。
    //   既定のままでよい欄(RenderingLayerMask〔未使用〕・SourceMaterial・法線の強さなど)= 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(固有の同期・サムネイル・見せ方・比較・シーンで確認)。検索・表示モード・目的別カードの対象にするために表へ載せている。
    // Common は MaterialCommon 構造体。1 項目 = 1 行にするため "Common.Albedo" のように入れ子のパスで書く(SerializedObject.FindProperty がそのまま解決する)。
    internal sealed class MaterialFieldGuide : FieldGuide<MaterialData>
    {
        public const string PurLook = "見た目（シェーダーと基本）";
        public const string PurSurface = "質感と発光";
        public const string PurSpecific = "固有（シェーダー専用のつまみ）";
        public const string PurRender = "描画の順序";
        public const string PurAnim = "動き（常時アニメ）";
        public const string PurPass = "パスとキーワード";
        public const string PurCheck = "確認する（サムネイル・シーン）";

        public const string RowSpecificTools = "(spectools)";
        public const string RowThumb = "(thumb)";
        public const string RowShape = "(shape)";
        public const string RowCompare = "(compare)";
        public const string RowScene = "(scene)";
        public const string RowPassKeyword = "(passkw)";

        public static readonly MaterialFieldGuide Instance = new MaterialFieldGuide();

        private static readonly string[] PurposeList = { PurLook, PurSurface, PurSpecific, PurRender, PurAnim, PurPass, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E("Shader", "シェーダー", "使うシェーダー。変えると、そのシェーダー専用のつまみ(固有)を自動で足します。未設定なら既定の Lit で描きます。",
                FieldTier.Required, PurLook, FieldRowKind.Normal, "シェーダー", "Shader", "Lit", "Unlit", "Toon", "URP"),
            E("Common.Albedo", "ベースの絵(Albedo)", "色のもとになる画像(TextureData)。色だけで決めたいときは空でも構いません。",
                FieldTier.Common, PurLook, FieldRowKind.Normal, "アルベド", "ベースカラー", "テクスチャ", "画像", "BaseMap", "MainTex"),
            E("Common.AlbedoTint", "ベースの色(乗算)", "ベースの絵に掛ける色。白で絵のまま、絵が無ければこの色そのものになります。",
                FieldTier.Common, PurLook, FieldRowKind.Normal, "色", "ティント", "Tint", "BaseColor"),
            E("Common.Blend", "透け方", "Opaque = 不透明 / Cutout = しきい値で切り抜き / Transparent = 半透明。描画の順序(RenderQueue)もこれで決まります。",
                FieldTier.Common, PurLook, FieldRowKind.Normal, "透明", "半透明", "不透明", "Opaque", "Cutout", "Transparent", "ブレンド"),
            E("Common.Cutoff", "切り抜きのしきい値", "透け方が Cutout のとき、この値より薄い部分を描きません。",
                FieldTier.Advanced, PurLook, FieldRowKind.Normal, "カットアウト", "Cutoff", "アルファ", "しきい値"),
            E("Common.DoubleSided", "裏面も描く", "ON で裏面も描きます(Cull Off)。板ポリや葉っぱなど。",
                FieldTier.Common, PurLook, FieldRowKind.Normal, "両面", "裏", "Cull", "DoubleSided"),

            E("Common.Normal", "凹凸の絵(Normal)", "ノーマルマップ(TextureData)。表面の細かい凹凸を作ります。",
                FieldTier.Common, PurSurface, FieldRowKind.Normal, "ノーマル", "法線", "凹凸", "BumpMap"),
            E("Common.NormalScale", "凹凸の強さ", "ノーマルマップの強さ。1 で絵のまま。",
                FieldTier.Advanced, PurSurface, FieldRowKind.Normal, "ノーマル", "強さ", "NormalScale", "BumpScale"),
            E("Common.Mask", "質感の絵(Mask)", "R = 金属 / G = 遮蔽 / B = ディテール / A = つや をまとめた画像(TextureData)。",
                FieldTier.Common, PurSurface, FieldRowKind.Normal, "マスク", "金属", "メタリック", "つや", "スムース", "Mask", "Occlusion"),
            E("Common.Metallic", "金属っぽさ", "質感の絵が無いときの定数(0〜1)。",
                FieldTier.Common, PurSurface, FieldRowKind.Normal, "メタリック", "金属", "Metallic"),
            E("Common.Smoothness", "つやつや度", "質感の絵が無いときの定数(0〜1)。大きいほどツルツルして光を反射します。",
                FieldTier.Common, PurSurface, FieldRowKind.Normal, "スムース", "つや", "光沢", "Smoothness", "Glossiness"),
            E("Common.Emission", "発光の絵", "発光させる場所の画像(TextureData)。",
                FieldTier.Common, PurSurface, FieldRowKind.Normal, "エミッション", "発光", "光る", "Emission"),
            E("Common.EmissionColor", "光る色", "発光の色。黒だと光りません。",
                FieldTier.Common, PurSurface, FieldRowKind.Normal, "エミッション", "発光", "色", "EmissionColor"),
            E("Common.EmissionIntensity", "光る強さ", "発光の強さ(0 で無効)。1 を超えるとブルームで光って見えます。",
                FieldTier.Common, PurSurface, FieldRowKind.Normal, "エミッション", "発光", "強さ", "Intensity", "ブルーム"),

            E("Specific", "固有のつまみ", "シェーダー専用の値(例: Toon の影の段数、アウトラインの太さ)。1 つ = 1 行。分類のチップで絞れます。共通の欄(Common)に無いものだけをここに置きます。",
                FieldTier.Common, PurSpecific, FieldRowKind.Knobs, "固有", "Specific", "Toon", "アウトライン", "リム", "影", "シェーダー"),
            E(RowSpecificTools, "シェーダーとの同期", "シェーダーの固有プロパティと見比べて、足りないものを足す / 共通の値を上書きしてしまうものを外す。",
                FieldTier.Common, PurSpecific, FieldRowKind.Knobs, "同期", "固有", "上書き", "重複", "共通チャンネル"),

            E("RenderQueueOffset", "描画順(ずらし)", "透け方で決まる基準の順序からのずれ。大きいほど後(手前)に描きます。半透明どうしの前後を直すときに使います。",
                FieldTier.Common, PurRender, FieldRowKind.Normal, "描画順", "RenderQueue", "前後", "オフセット", "ソート"),
            E("RenderingLayerMask", "ライトレイヤー(未使用)", "未使用です。ライトレイヤーは ModelData.LightLayerMask で指定します(この値は変換でコピーされるだけ)。",
                FieldTier.Advanced, PurRender, FieldRowKind.Normal, "ライトレイヤー", "RenderingLayerMask", "未使用"),

            E("Anims", "常時アニメ", "UV スクロールやサイン波など、出している間ずっと動かす設定。1 件 = 動かすプロパティ 1 つ。",
                FieldTier.Common, PurAnim, FieldRowKind.Normal, "アニメ", "UV", "スクロール", "流れる", "点滅", "サイン波", "Anims"),

            E(RowPassKeyword, "パスの無効化とキーワードの有効化", "チェックしたパスを描かない(例: ShadowCaster で影を落とさない)。キーワードを足すとシェーダーの機能を ON にします。",
                FieldTier.Common, PurPass, FieldRowKind.Visual, "パス", "キーワード", "ShadowCaster", "DepthOnly", "影", "Keyword", "Pass", "LightMode", "DisabledPasses", "EnabledKeywords"),

            E("DisabledPasses", "無効にするパス", "チェックしたパス(LightMode 名)を描きません。上の「パスの無効化とキーワードの有効化」の欄で操作します。",
                FieldTier.Common, PurPass, FieldRowKind.Visual, "パス", "ShadowCaster", "DepthOnly", "LightMode", "影を落とさない"),
            E("EnabledKeywords", "有効にするキーワード", "ON にするシェーダーキーワード。上の「パスの無効化とキーワードの有効化」の欄で操作します。",
                FieldTier.Common, PurPass, FieldRowKind.Visual, "キーワード", "Keyword", "shader_feature", "multi_compile"),

            E(RowThumb, "サムネイル", "実際の Manager で作った Material を球などに貼って、この窓の中で見ます。ドラッグで回転。比較対象を選ぶと左右 / 切替で見比べられます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "サムネイル", "プレビュー", "回転", "見た目", "比較"),
            E(RowShape, "見せ方(形・回転・ライト)", "球 / 板 / Cube / 任意のモデルから形を選び、ターンテーブルとライトの向きを変えます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "形状", "球", "Cube", "モデル", "ターンテーブル", "ライト"),
            E(RowCompare, "比較する Material", "もう 1 つの MaterialData を選ぶと、サムネイルを左右 / 切替で見比べ、シーンでは隣に並べて確認できます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "比較", "見比べ", "並べて", "A/B"),
            E(RowScene, "シーンで確認", "確認用シーン / 今のシーンに選んだ形(保存されない)を置き、Material を貼って SceneView で見ます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "シーン", "確認用シーン", "配置", "再生成", "撤去", "SceneView"),

            // Flags / Events は AssetDataBase の共通欄。Material には再生が無いので、管理情報カードの詳細に置く。
            FlagsEntry(FieldGuide<MaterialData>.AdminPurpose),
            EventsEntry(FieldGuide<MaterialData>.AdminPurpose),

            E("SourceMaterial", "取り込み元(Maya)", "Maya の FBX から自動で作ったときの由来(「FBX名/マテリアル名」)。再取り込みで同じ物を見つけるための印です。手で作った物は空。",
                FieldTier.Advanced, FieldGuide<MaterialData>.AdminPurpose, FieldRowKind.Normal, "Maya", "FBX", "由来", "取り込み", "Import"),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
