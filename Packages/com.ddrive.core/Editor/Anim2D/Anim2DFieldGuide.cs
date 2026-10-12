using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Anim2D;

namespace DDrive.Editor.Anim2D
{
    // Anim2DData の案内表(新デザイン Anim2DEditor 用。docs/1010 UX-7)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: Anim2DDataValidator / AnimDataValidator が Error にする = Clip 未生成・方向付きなのに DirectionClips 不足・ParamX/Y 空・Frame/Time イベントの Clip 長超過
    // → Clip = 必須(Directions / DirectionClips / ParamX・Y は「方向付き」にしたときだけ必須になるので よく使う / 詳細)。Layer・Mask・Flags は Info だけ = 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(コマの一覧・リタイミング適用・プレビュー・ログ)。検索・表示モード・目的別カードの対象にするために表へ載せている。
    internal sealed class Anim2DFieldGuide : FieldGuide<Anim2DData>
    {
        public const string PurPlay = "動かす（Clip・ステート）";
        public const string PurDir = "向き（方向セット）";
        public const string PurFrames = "コマ（スプライトと間隔）";
        public const string PurEvents = "合図（イベント）";
        public const string PurCheck = "確認する（プレビュー・ログ）";

        public const string RowFrames = "(frames)";
        public const string RowRetime = "(retime)";
        public const string RowPreview = "(preview)";
        public const string RowLog = "(log)";

        public static readonly Anim2DFieldGuide Instance = new Anim2DFieldGuide();

        private static readonly string[] PurposeList = { PurPlay, PurDir, PurFrames, PurEvents, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E("Clip", "再生するクリップ", "スプライトを順に切り替える AnimationClip。「スプライトから作成」で作った物がここに入ります。長さ・フレームレートはイベントと終了判定に使います。",
                FieldTier.Required, PurPlay, FieldRowKind.Normal, "クリップ", "アニメ", "Clip", "スプライト", "絵"),
            E("StateName", "ステート名", "AnimatorController 上のステート名(方向付きなら BlendTree のステート)。空なら Clip の名前を使います。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "ステート", "StateName", "Controller", "コントローラー"),
            E("Loop", "ループ再生", "ON なら周回ごとに OnLoop を発火し、止めるまで続きます。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "ループ", "繰り返し", "Loop", "OnLoop"),
            E("DefaultCrossFade", "切り替え時間(秒)", "前の動きからこの動きへ切り替えるときの CrossFade 秒の既定値。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "クロスフェード", "CrossFade", "ブレンド", "遷移"),
            E("Layer", "レイヤー", "ステートがある Animator のレイヤー番号。",
                FieldTier.Advanced, PurPlay, FieldRowKind.Normal, "Layer", "レイヤー"),
            E("Mask", "アバターマスク", "上半身のみ等のマスク(情報用)。",
                FieldTier.Advanced, PurPlay, FieldRowKind.Normal, "マスク", "AvatarMask"),
            FlagsEntry(PurPlay),
            E("Ik", "手足 IK(3D 用)", "3D モデルの手足 IK。スプライトのアニメでは通常使いません(AnimData 共通の欄)。",
                FieldTier.Advanced, PurPlay, FieldRowKind.Normal, "IK", "手", "足"),
            E("BlendShapes", "表情 BlendShape(3D 用)", "3D モデルの BlendShape。スプライトのアニメでは通常使いません(AnimData 共通の欄)。",
                FieldTier.Advanced, PurPlay, FieldRowKind.Normal, "BlendShape", "表情", "シェイプ"),

            E("Directions", "方向セット", "なし = 1 方向 / Four = 4 方向(0・90・180・270)/ Eight = 8 方向(45 度刻み)。選ぶと、下の「方向ごとのクリップ」にその数の行が出ます。",
                FieldTier.Common, PurDir, FieldRowKind.Normal, "方向", "4方向", "8方向", "BlendTree", "向き"),
            E("DirectionClips", "方向ごとのクリップ", "角度順(0 → 315)に 1 方向 = 1 行。「スプライトから作成」の方向セットで自動で入ります。空欄・Missing は ⚠ で知らせます。",
                FieldTier.Common, PurDir, FieldRowKind.Visual, "方向", "角度", "クリップ", "DirectionClips"),
            E("ParamXName", "BlendTree の X パラメータ名", "BlendTree(2D Freeform Directional)の X に使う Animator のパラメータ名。既定は x。",
                FieldTier.Advanced, PurDir, FieldRowKind.Normal, "x", "パラメータ", "Animator", "BlendTree"),
            E("ParamYName", "BlendTree の Y パラメータ名", "BlendTree の Y に使う Animator のパラメータ名。既定は y。",
                FieldTier.Advanced, PurDir, FieldRowKind.Normal, "y", "パラメータ", "Animator", "BlendTree"),

            E(RowFrames, "コマの一覧", "Clip のスプライトのキーを 1 コマ = 1 行で並べます(絵・時刻・次のコマまでの間隔)。間隔は下の「リタイミング」で整えます。",
                FieldTier.Common, PurFrames, FieldRowKind.Visual, "スプライト", "コマ", "フレーム", "時刻", "間隔", "サムネイル"),
            E("Retiming", "リタイミングのカーブ", "コマの配置の曲線(横 = コマ番号、縦 = 時間)。直線 = 等間隔。「適用」で Clip のキー時刻に焼き込みます(再生中は参照しません)。",
                FieldTier.Common, PurFrames, FieldRowKind.Normal, "リタイミング", "カーブ", "ValueDef", "ease", "ためる", "緩急"),
            E(RowRetime, "リタイミングの適用", "配置モード(均等 / カーブ)を選んで「適用」すると、Clip と方向 Clip(枚数が同じもの)のスプライトの時刻を書き換えます。",
                FieldTier.Common, PurFrames, FieldRowKind.Visual, "適用", "焼き込み", "配置モード", "均等", "Uniform", "Retiming"),

            E("Events", "イベント(SE・VFX・そのほか)", "再生の節目(フレーム / 秒 / ループ)で SE・VFX・配置セットを出す、または値を送る。一覧は 1 イベント = 1 行の要約。編集は「Anim Editor で編集」(タイムライン上でマーカーを動かす・SE / VFX の同時再生)。",
                FieldTier.Common, PurEvents, FieldRowKind.Visual, "SE", "VFX", "配置セット", "PlayAsset", "フレーム", "秒", "Trigger", "Frame", "Time", "OnLoop", "足音"),

            E(RowPreview, "プレビュー(SceneView)", "確認用シーン / 今のシーンに SpriteRenderer + Animator のプレビュー物を置き、固定バーの ▶ ■ で実 AnimManager から動かします。ここには再生中の絵を映します。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "プレビュー", "確認用シーン", "SpriteRenderer", "再生", "撤去", "配置"),
            E(RowLog, "イベントログ", "再生で発火したイベントと操作の最近 12 行。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "ログ", "履歴", "発火"),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
