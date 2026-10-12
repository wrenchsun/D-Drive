using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Prefab;

namespace DDrive.Editor.PrefabTool
{
    // PrefabData の案内表(新デザイン PrefabEditor 用。docs/1010 UX-14)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: PrefabDataValidator が Error にする = Prefab 未設定・CollisionLayer が未定義 → Prefab = 必須(CollisionLayer は既定 -1 で
    //   通常は触らないので「よく使う」にして、Error のときは「注意あり」で必ず見える)。Warning にする欄(Kind = Projectile のときの Flags.Pool・GameplayTags の空白 / 重複 / タイポ)・
    //   日常的に触る欄(Kind・GameplayTags・Flags)= よく使う。既定のままでよい欄(Lod)= 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(シーンで確認)。
    internal sealed class PrefabFieldGuide : FieldGuide<PrefabData>
    {
        public const string PurPrefab = "見た目（Prefab）";
        public const string PurGame = "ゲームでの扱い";
        public const string PurCheck = "確認する（シーン）";

        public const string RowScene = "(scene)";

        public static readonly PrefabFieldGuide Instance = new PrefabFieldGuide();

        private static readonly string[] PurposeList = { PurPrefab, PurGame, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table = BuildTable();

        private static FieldGuideEntry[] BuildTable()
        {
            // Flags は共通欄だが、Prefab では Pool(Projectile は Pooled 推奨)とネット(Simulated)の検査があるので「よく使う」でゲームでの扱いに置く。
            var flags = FlagsEntry(PurGame);
            flags.Tier = FieldTier.Common;
            return new[]
            {
                E("Prefab", "Prefab", "Spawn する実体。未設定の場合は空の GameObject を Placeholder として生成します(検証はエラー)。",
                    FieldTier.Required, PurPrefab, FieldRowKind.Normal, "プレハブ", "実体", "Spawn"),

                E("Kind", "種類", "用途の目安(Gimmick / Projectile / Pickup / Environment / Character / Other)。ロジックの分岐には使いません。Projectile は Pool を Pooled にするのがおすすめです。",
                    FieldTier.Common, PurGame, FieldRowKind.Normal, "種類", "弾", "ギミック", "ピックアップ", "Projectile", "Gimmick", "Pickup"),
                E("GameplayTags", "ゲームロジック用タグ", "\"Destructible\" などのタグ。ほかの PrefabData のタグと見比べて、タイポの疑いを警告します。",
                    FieldTier.Common, PurGame, FieldRowKind.Normal, "タグ", "Tags", "Destructible", "判定"),
                E("CollisionLayer", "コリジョンレイヤー", "生成時に Prefab 全体へ再帰的に設定するレイヤー番号。-1 = Prefab のレイヤーをそのまま使う。0〜31 は定義済みのレイヤーだけ。",
                    FieldTier.Common, PurGame, FieldRowKind.Normal, "レイヤー", "コリジョン", "当たり判定", "Layer", "Collision"),
                flags,
                E("Lod", "LOD の閾値の上書き", "任意。Prefab の LODGroup の切り替えしきい値(0〜1)をデータ側から差し替えます。",
                    FieldTier.Advanced, PurGame, FieldRowKind.Normal, "LOD", "LODGroup", "距離"),

                E(RowScene, "シーンで確認", "確認用シーン / 今のシーンに、実 PrefabsManager で Spawn した実体を置いて(保存されない)SceneView で見ます。",
                    FieldTier.Common, PurCheck, FieldRowKind.Visual, "シーン", "確認用シーン", "配置", "撤去", "SceneView", "Spawn"),

                EventsEntry(FieldGuide<PrefabData>.AdminPurpose),
            };
        }

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
