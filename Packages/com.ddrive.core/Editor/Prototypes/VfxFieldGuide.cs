using System;
using System.Collections.Generic;
using DDrive.Runtime.Vfx;

namespace DDrive.EditorPrototypes
{
    // VfxData の案内表。段の根拠: VfxDataValidator が Error にする = Prefab のみ → 必須。
    // 普段触る / Validator の Warning 対象(LifeMode・Render・Anchor)= よく使う。既定のままで警告が出ない = 詳細。
    internal sealed class VfxFieldGuide : FieldGuide
    {
        public const string SecPrefab = "Prefab";
        public const string SecAnchor = "出る場所(Anchor)";
        public const string SecLife = "長さ・消え方";
        public const string SecRender = "描画";
        public const string SecParams = "調整(Params)";
        public const string SecEvents = "連携(イベント)";
        public const string SecAdmin = "管理情報";

        public const string PurShow = "出す";
        public const string PurPlace = "出る場所を変える";
        public const string PurLife = "長さ・消え方を変える";
        public const string PurLook = "見た目を調整する(Params)";
        public const string PurLink = "音・イベントと合わせる";
        public const string PurAdmin = "管理情報";

        public static readonly VfxFieldGuide Instance = new VfxFieldGuide();

        private static readonly string[] SectionList = { SecPrefab, SecAnchor, SecLife, SecRender, SecParams, SecEvents, SecAdmin };
        private static readonly string[] PurposeList = { PurShow, PurPlace, PurLife, PurLook, PurLink, PurAdmin };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string section, string purpose, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Section = section, Purpose = purpose, Keywords = kw };

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(VfxData.Prefab), "エフェクトの実体(Prefab)", "再生するパーティクル / VFX Graph の Prefab。これだけ入れれば再生できます。",
                FieldTier.Required, SecPrefab, PurShow, "エフェクト", "パーティクル", "VFX Graph", "実体", "ParticleSystem"),
            E(nameof(VfxData.Render), "表示先(3D / UI の上)", "通常のシーンに出すか、UI の上に重ねて出すか。",
                FieldTier.Common, SecRender, PurShow, "UI", "ワールド", "3D", "オーバーレイ", "重ねる"),

            E(nameof(VfxData.Anchor), "出る位置(この Data 専用)", "Space=World ならワールド座標、ボーン名ならキャラの部位。高さは LocalOffset の Y。",
                FieldTier.Common, SecAnchor, PurPlace, "位置", "場所", "高さ", "オフセット", "ボーン", "アタッチ", "向き", "回転", "スケール", "座標"),
            E(nameof(VfxData.AnchorId), "共通の出る位置(Anchor アセット)", "複数の Data で位置を共有したいときに Anchor アセットを指定。設定すると上の位置は無視されます。",
                FieldTier.Advanced, SecAnchor, PurPlace, "共有", "共通", "再利用", "アセット"),

            E(nameof(VfxData.LifeMode), "再生の終わり方", "OneShot=一回で終わる / Loop=止めるまで続く / Duration=決めた秒数で終わる。",
                FieldTier.Common, SecLife, PurLife, "ループ", "一回", "ワンショット", "繰り返し", "常駐", "終了"),
            E(nameof(VfxData.Duration), "続く秒数", "終わり方が Duration のときの長さ(秒)。OneShot でも終了判定の保険に使われます。",
                FieldTier.Common, SecLife, PurLife, "秒", "長さ", "時間", "継続"),
            E(nameof(VfxData.FadeOutSec), "停止後の余韻(秒)", "止めてから消すまで待つ秒数。残ったパーティクルが消えるのを待ちます。",
                FieldTier.Advanced, SecLife, PurLife, "フェード", "消える", "余韻", "消え方"),
            E("Flags", "動作の取り決め(Pool / ポーズ / ネット)", "Pool 上限・ポーズ時の挙動・ネット配送など。Loop のときは Pool 上限を設定してください。",
                FieldTier.Advanced, SecLife, PurLife, "プール", "ポーズ", "ネット", "優先度", "同期", "ロード", "ドメイン"),

            E(nameof(VfxData.RenderLayer), "表示レイヤー", "出した実体に付ける Layer(カメラの映す / 映さない用)。UI の上に出すときは VfxUI レイヤー。",
                FieldTier.Advanced, SecRender, PurLook, "レイヤー", "カリング", "カメラ"),
            E(nameof(VfxData.LightLayerMask), "ライトの当たり方(Light Layer)", "どのライトを受けるか。0 なら Prefab の設定をそのまま使います。",
                FieldTier.Advanced, SecRender, PurLook, "ライト", "レイヤー", "明るさ", "照明"),

            E(nameof(VfxData.Params), "調整つまみ(Params)", "コードや演出から色・サイズなどを変えるための公開項目。名前を付けて登録します。",
                FieldTier.Common, SecParams, PurLook, "色", "パラメータ", "サイズ", "調整", "変数", "Color", "つまみ", "プロパティ"),

            E("Events", "きっかけで鳴らす・出す(イベント)", "出現・終了などの節目で、別の SE / VFX などを一緒に再生します。",
                FieldTier.Advanced, SecEvents, PurLink, "イベント", "音", "SE", "連動", "同時", "節目"),

            E("DisplayName", "表示名", "一覧に出る名前。未設定ならファイル名。", FieldTier.Common, SecAdmin, PurAdmin, "名前", "タイトル"),
            E("Category", "カテゴリ", "AssetBrowser のフォルダ分け。", FieldTier.Common, SecAdmin, PurAdmin, "分類", "フォルダ"),
            E("Description", "説明", "この Data のメモ(任意)。", FieldTier.Advanced, SecAdmin, PurAdmin, "メモ", "備考"),
            E("Tags", "タグ", "検索用のタグ。", FieldTier.Advanced, SecAdmin, PurAdmin, "検索", "ラベル"),
            E("Icon", "アイコン", "一覧に出す画像(任意)。", FieldTier.Advanced, SecAdmin, PurAdmin, "画像", "サムネイル"),
            E("Assignee", "担当者", "担当者名(自由入力)。", FieldTier.Advanced, SecAdmin, PurAdmin, "担当", "人"),
            E("SpecUrl", "仕様書 URL", "この仕様の参照先 URL。", FieldTier.Advanced, SecAdmin, PurAdmin, "仕様", "リンク", "URL"),
            E("ChangeNote", "変更メモ", "何を変えたかのメモ(任意)。", FieldTier.Advanced, SecAdmin, PurAdmin, "履歴", "変更", "更新"),
        };

        public override Type DataType => typeof(VfxData);
        public override IReadOnlyList<FieldGuideEntry> Entries => Table;
        public override IReadOnlyList<string> Sections => SectionList;
        public override IReadOnlyList<string> Purposes => PurposeList;
    }
}
