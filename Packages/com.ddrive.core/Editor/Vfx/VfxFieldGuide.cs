using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Vfx;

namespace DDrive.Editor.Vfx
{
    // VfxData の案内表(新デザイン VfxEditor 用。docs/1010 UX-1/2)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: VfxDataValidator が Error にする = Prefab のみ → 必須。普段触る / Validator の Warning 対象(LifeMode・Render・Anchor・Params)= よく使う。
    // 既定のままで警告が出ない = 詳細。
    internal sealed class VfxFieldGuide : FieldGuide<VfxData>
    {
        public const string PurShow = "出す";
        public const string PurPlace = "出る場所を変える";
        public const string PurLife = "長さ・消え方を変える";
        public const string PurLook = "見た目を調整する(Params)";
        public const string PurLink = "音・イベントと合わせる";

        public static readonly VfxFieldGuide Instance = new VfxFieldGuide();

        private static readonly string[] PurposeList = { PurShow, PurPlace, PurLife, PurLook, PurLink };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(VfxData.Prefab), "エフェクトの実体(Prefab)", "再生するパーティクル / VFX Graph の Prefab。これだけ入れれば再生できます。",
                FieldTier.Required, PurShow, FieldRowKind.Normal, "エフェクト", "パーティクル", "VFX Graph", "実体", "ParticleSystem"),
            E(nameof(VfxData.Render), "表示先(3D / UI の上)", "通常のシーンに出すか、UI の上に重ねて出すか。",
                FieldTier.Common, PurShow, FieldRowKind.Visual, "UI", "ワールド", "3D", "オーバーレイ", "重ねる"),

            E(nameof(VfxData.Anchor), "出る位置(この Data 専用)", "Space=World ならワールド座標、ボーン名ならキャラの部位。高さは LocalOffset の Y。",
                FieldTier.Common, PurPlace, FieldRowKind.Visual, "位置", "場所", "高さ", "オフセット", "ボーン", "アタッチ", "向き", "回転", "スケール", "座標"),
            E(nameof(VfxData.AnchorId), "共通の出る位置(Anchor アセット)", "複数の Data で位置を共有したいときに Anchor アセットを指定。設定すると上の位置は無視されます。",
                FieldTier.Advanced, PurPlace, FieldRowKind.Visual, "共有", "共通", "再利用", "アセット"),

            E(nameof(VfxData.LifeMode), "再生の終わり方", "OneShot=一回で終わる / Loop=止めるまで続く / Duration=決めた秒数で終わる。",
                FieldTier.Common, PurLife, FieldRowKind.Visual, "ループ", "一回", "ワンショット", "繰り返し", "常駐", "終了"),
            E(nameof(VfxData.Duration), "続く秒数", "終わり方が Duration のときの長さ(秒)。OneShot でも終了判定の保険に使われます。",
                FieldTier.Common, PurLife, FieldRowKind.Visual, "秒", "長さ", "時間", "継続"),
            E(nameof(VfxData.FadeOutSec), "停止後の余韻(秒)", "止めてから消すまで待つ秒数。残ったパーティクルが消えるのを待ちます。",
                FieldTier.Advanced, PurLife, FieldRowKind.Visual, "フェード", "消える", "余韻", "消え方"),
            FlagsEntry(PurLife),

            E(nameof(VfxData.RenderLayer), "表示レイヤー", "出した実体に付ける Layer(カメラの映す / 映さない用)。UI の上に出すときは VfxUI レイヤー。",
                FieldTier.Advanced, PurShow, FieldRowKind.Visual, "レイヤー", "カリング", "カメラ"),
            E(nameof(VfxData.LightLayerMask), "ライトの当たり方(Light Layer)", "どのライトを受けるか。0 なら Prefab の設定をそのまま使います。",
                FieldTier.Advanced, PurShow, FieldRowKind.Visual, "ライト", "レイヤー", "明るさ", "照明"),

            E(nameof(VfxData.Params), "調整つまみ(Params)", "コードや演出から色・サイズなどを変えるための公開項目。名前を付けて登録します。",
                FieldTier.Common, PurLook, FieldRowKind.Knobs, "色", "パラメータ", "サイズ", "調整", "変数", "Color", "つまみ", "プロパティ"),

            EventsEntry(PurLink),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
