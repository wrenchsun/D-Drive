using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Anchoring;

namespace DDrive.Editor.Anchor
{
    // AnchorData の案内表(新デザイン AnchorEditor 用。docs/1010 UX-11)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: AnchorDataValidator の Error は「Parent が未登録・循環・深すぎ」と「Space が BoneName / NamedObject なのに Path が空」だが、いずれもその設定を選んだときだけの条件なので
    //   「必須」の段にはせず、Parent・Space・Path を「よく使う」にした(Error / Warning は欄の下に出て、折りたたまれていても見える)。Warning の対象(SpawnChance・生成ディレイ・ScaleRange・子での Space/Path)=
    //   よく使う、既定のままでよい欄 = 詳細。位置のパッドと試し出しは「かんたん」でも見せたいので必須の段に置く(バッジは出さない)。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(基準のツール・連鎖・試し出し・ヒエラルキーから作る)。
    internal sealed class AnchorFieldGuide : FieldGuide<AnchorData>
    {
        public const string PurPlace = "出る位置";
        public const string PurTry = "試し出し";
        public const string PurBase = "基準を決める";
        public const string PurChain = "親 Anchor とつなぐ";
        public const string PurRandom = "ばらつき・タイミング";
        public const string PurCreate = "ヒエラルキーから作る";

        public const string RowTools = "(tools)";
        public const string RowChain = "(chain)";
        public const string RowTry = "(try)";
        public const string RowCreate = "(create)";

        public static readonly AnchorFieldGuide Instance = new AnchorFieldGuide();

        private static readonly string[] PurposeList = { PurPlace, PurTry, PurBase, PurChain, PurRandom, PurCreate };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            // 位置のパッド(LocalOffset の視覚部品)。同じ欄の通常入力は詳細モードの「位置の全設定」として別の行で出す。
            E(nameof(AnchorData.LocalOffset), "位置のオフセット(全設定)", "基準(ルートは解決先のボーンなど、子は親 Anchor の姿勢)からのローカル位置オフセット(メートル)。ルートの Space が World のときはワールド座標そのものです。",
                FieldTier.Advanced, PurPlace, FieldRowKind.Visual, "位置", "オフセット", "座標", "高さ", "パッド", "LocalOffset", "ずらす"),
            E(nameof(AnchorData.LocalEuler), "向き(回転オフセット)", "基準からのローカル回転オフセット(オイラー角、度)。VFX の向きや SE の発生向きに使います。",
                FieldTier.Common, PurPlace, FieldRowKind.Normal, "回転", "角度", "オイラー", "LocalEuler", "傾き"),
            E(nameof(AnchorData.LocalScale), "大きさ(スケール)", "ローカルスケール。(0, 0, 0) は 1 扱い。VFX だけに効き、SE には関係しません。",
                FieldTier.Advanced, PurPlace, FieldRowKind.Normal, "スケール", "拡大", "サイズ", "LocalScale"),

            E(RowTry, "確認用の VFX / SE", "▶ で、この Anchor の位置に実際に出します(連打すると、ばらつき・ディレイ・確率も毎回サンプリングし直されます)。固定バーの ▶ は VFX と SE をまとめて出し、リピートを入れると繰り返し確かめられます。",
                FieldTier.Required, PurTry, FieldRowKind.Visual, "確認", "再生", "VFX", "SE", "試す", "ディレイ", "確率", "連打"),

            E(nameof(AnchorData.Space), "基準の種類(Space)", "World = ワールド座標に固定 / BoneName・NamedObject = スポーン先の階層から Path の名前を探す / ContextTarget = スポーン先そのもの。子 Anchor では無視されます(ルートの基準が使われる)。",
                FieldTier.Common, PurBase, FieldRowKind.Normal, "基準", "ワールド", "ボーン", "ContextTarget", "NamedObject", "BoneName", "Space"),
            E(nameof(AnchorData.Path), "探す名前(Path)", "Space が BoneName / NamedObject のときに、スポーン先の階層から探す名前(ボーン名またはオブジェクト名)。子 Anchor では無視されます。",
                FieldTier.Common, PurBase, FieldRowKind.Normal, "パス", "ボーン名", "オブジェクト名", "AnchorPoint", "Path", "一覧"),
            E(RowTools, "スポーン先(試すときの基準)", "キャラクターや AnchorRig をここに入れると、Path をその階層から探して位置を解決します。「Path を一覧から選ぶ」と SceneView の表示切替もここです。",
                FieldTier.Common, PurBase, FieldRowKind.Visual, "スポーン先", "ボーン", "Path", "SceneView", "ハンドル", "AnchorPoint", "キャラクター", "解決"),
            E(nameof(AnchorData.FollowRotation), "回転にも追従する", "アタッチ後、アタッチ先の回転に追従するか。子 Anchor では無視されます(ルートの値が効く)。",
                FieldTier.Advanced, PurBase, FieldRowKind.Normal, "追従", "回転", "FollowRotation"),
            E(nameof(AnchorData.DetachOnStop), "アタッチ先が消えても残す", "アタッチ先が破棄された後もその場に残して、再生完了まで続けるか。子 Anchor では無視されます。",
                FieldTier.Advanced, PurBase, FieldRowKind.Normal, "残す", "切り離し", "DetachOnStop", "破棄"),

            E(nameof(AnchorData.Parent), "親 Anchor", "指定すると入れ子になり、この Anchor は親で決まった姿勢を基準に自分のオフセットを積みます。0(未設定)ならルート。",
                FieldTier.Common, PurChain, FieldRowKind.Normal, "入れ子", "親", "階層", "連鎖", "Parent", "ルート"),
            E(RowChain, "連鎖(ルート → この Anchor)", "ルートからこの Anchor までの連鎖。押すとその Anchor に切り替わります。",
                FieldTier.Common, PurChain, FieldRowKind.Visual, "連鎖", "ルート", "入れ子", "親"),

            E(nameof(AnchorData.PositionJitterRadius), "位置のばらつき", "生成位置のランダム半径(メートル)。0 で無効。この半径の球内に散らばります。",
                FieldTier.Common, PurRandom, FieldRowKind.Normal, "ランダム", "散らばり", "半径", "PositionJitterRadius", "ジッター"),
            E(nameof(AnchorData.EulerJitter), "向きのばらつき", "生成時の回転(オイラー角、度)への ±ランダム範囲。(0, 0, 0) で無効。",
                FieldTier.Advanced, PurRandom, FieldRowKind.Normal, "ランダム", "回転", "EulerJitter"),
            E(nameof(AnchorData.ScaleRange), "大きさのばらつき", "生成スケール倍率のランダム範囲(x = 最小、y = 最大)。両方 1 で無効。SE には関係しません。",
                FieldTier.Common, PurRandom, FieldRowKind.Normal, "ランダム", "スケール", "ScaleRange", "拡大"),
            E(nameof(AnchorData.DelaySec), "待ち時間", "生成を遅らせる秒数。連鎖しているときは各段の値が足されます。10 秒を超えると警告が出ます。",
                FieldTier.Common, PurRandom, FieldRowKind.Normal, "ディレイ", "遅延", "待つ", "DelaySec"),
            E(nameof(AnchorData.DelayJitterSec), "待ち時間のばらつき", "待ち時間(秒)に足されるランダム秒数(0〜この値)。",
                FieldTier.Advanced, PurRandom, FieldRowKind.Normal, "ディレイ", "ランダム", "DelayJitterSec"),
            E(nameof(AnchorData.SpawnChance), "出る確率", "0〜1。1 未満で確率生成、0 は「出さない」。連鎖しているときは各段の値が掛け算されます。",
                FieldTier.Common, PurRandom, FieldRowKind.Normal, "確率", "チャンス", "出さない", "SpawnChance"),

            E(RowCreate, "Anchor を作る", "Hierarchy で Transform(ボーン配下の空オブジェクト・AnchorPoint など)を選んで作成すると、その親を基準にしたオフセットが自動で入ります。AnchorRig からは AnchorPoint ごとにまとめて作れます。",
                FieldTier.Common, PurCreate, FieldRowKind.Visual, "作成", "Transform", "AnchorRig", "AnchorPoint", "ヒエラルキー", "ボーン", "新規"),

            FlagsEntry(AdminPurpose),
            EventsEntry(AdminPurpose),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
