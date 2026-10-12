using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Material;

namespace DDrive.Editor.Materials
{
    // TextureData の案内表(新デザイン TextureEditor 用。docs/1010 UX-8)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: TextureDataValidator が Error にする = Texture 未設定・Usage=UI なのに Sprite が無い・Channel=Normal/Mask の Importer 不一致 → Texture = 必須。
    //   Usage=UI の Sprite は Usage を UI にしたときだけ必須になるので よく使う。AllowScale の 9-slice 警告は Warning なので SliceBorder も よく使う。
    // 欄が少ないので目的カードは 1 枚(画像)。名前が "(" で始まる欄は Data のフィールドではなく補助の行(画像の見た目と Importer の現状)。
    internal sealed class TextureFieldGuide : FieldGuide<TextureData>
    {
        public const string PurImage = "画像";

        public const string RowInfo = "(texinfo)";

        public static readonly TextureFieldGuide Instance = new TextureFieldGuide();

        private static readonly string[] PurposeList = { PurImage };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = PurImage, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E("Texture", "画像", "実体のテクスチャ。Importer の設定はファイル名の規約(_N / _M / _E / _UI …)で自動で決まります。",
                FieldTier.Required, FieldRowKind.Normal, "テクスチャ", "画像", "Texture", "Texture2D"),
            E("Usage", "使い道", "3D のモデルに貼る(Model)か、UI に使う(UI)か。UI にすると Sprite が必要になります。",
                FieldTier.Common, FieldRowKind.Normal, "UI", "モデル", "Usage"),
            E("Channel", "どの用途の絵か", "Material のどのチャンネルに使う画像か(Albedo / Normal / Mask / Emission / Other)。Normal は NormalMap、Mask は sRGB off が必要です。",
                FieldTier.Common, FieldRowKind.Normal, "チャンネル", "アルベド", "ノーマル", "マスク", "エミッション", "Channel"),
            E("Sprite", "Sprite(UI 用)", "Usage=UI のとき UI に出す Sprite。無ければ検証の「修正」で作れます。",
                FieldTier.Common, FieldRowKind.Normal, "スプライト", "UI", "Sprite"),
            E("AllowScale", "拡縮してよい", "ON のとき 9-slice のボーダーが無いと警告します。",
                FieldTier.Advanced, FieldRowKind.Normal, "拡縮", "9-slice", "スライス", "AllowScale"),
            E("SliceBorder", "9-slice のボーダー", "左・下・右・上(L, B, R, T)。拡縮してよい画像で、端を伸ばさない幅を決めます。",
                FieldTier.Advanced, FieldRowKind.Normal, "9-slice", "ボーダー", "スライス", "SliceBorder"),
            E(RowInfo, "見た目と Importer の現状", "画像の大きさ、Texture Type / sRGB / Mipmap / 圧縮 / Max Size、ファイル名の規約に合っているか。食い違いは「命名規約を適用して再インポート」で直せます。",
                FieldTier.Common, FieldRowKind.Visual, "Importer", "規約", "sRGB", "圧縮", "再インポート", "サイズ", "プレビュー"),

            // Flags / Events は AssetDataBase の共通欄。Texture には再生が無いので、管理情報カードの詳細に置く。
            FlagsEntry(FieldGuide<TextureData>.AdminPurpose),
            EventsEntry(FieldGuide<TextureData>.AdminPurpose),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
