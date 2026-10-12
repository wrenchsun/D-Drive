using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Anim;

namespace DDrive.Editor.Anim
{
    // AnimData の案内表(新デザイン AnimEditor 用。docs/1010 UX-6)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: AnimDataValidator が Error にする = Clip 未設定・(StateName も Clip も無い)・Frame/Time イベントの Clip 長超過 → Clip = 必須。
    // Warning の対象(OnLoop なのに Loop=false・BlendShape の ShapeName 空 / 外部管理・DefaultCrossFade 超過)= よく使う。Info だけ(Layer・Mask・Flags)= 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく、確認用の補助の行(ステート一覧・タイムライン・対象・ブレンド確認・ログなど)。
    // 検索・表示モード・目的別カードの対象にするために表へ載せている(AnimEditor.CreateRow が行を作る)。
    internal sealed class AnimFieldGuide : FieldGuide<AnimData>
    {
        public const string PurPlay = "動かす（Clip・ステート）";
        public const string PurEvents = "合図（イベント）";
        public const string PurBody = "表情・手足（BlendShape・IK）";
        public const string PurCheck = "確認する（対象・ブレンド・ログ）";

        public const string RowStates = "(states)";
        public const string RowTimeline = "(timeline)";
        public const string RowTarget = "(target)";
        public const string RowModel = "(model)";
        public const string RowPlayCtl = "(playctl)";
        public const string RowBlend = "(blend)";
        public const string RowLog = "(log)";

        public static readonly AnimFieldGuide Instance = new AnimFieldGuide();

        private static readonly string[] PurposeList = { PurPlay, PurEvents, PurBody, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(AnimData.Clip), "再生するクリップ", "再生するアニメーション。長さ・フレームレートはイベント(Frame / Time)と終了判定に使います。",
                FieldTier.Required, PurPlay, FieldRowKind.Normal, "クリップ", "アニメ", "Clip", "モーション", "fbx", "長さ"),
            E(nameof(AnimData.StateName), "ステート名", "AnimatorController 上のステート名(CrossFade の対象)。空なら Clip の名前を使います。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "ステート", "StateName", "Controller", "コントローラー"),
            E(nameof(AnimData.Loop), "ループ再生", "ON なら周回ごとに OnLoop を発火し、止めるまで続きます。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "ループ", "繰り返し", "Loop", "OnLoop"),
            E(nameof(AnimData.DefaultCrossFade), "切り替え時間(秒)", "前の動きからこの動きへ切り替えるときの CrossFade 秒の既定値。再生時に上書きもできます。",
                FieldTier.Common, PurPlay, FieldRowKind.Normal, "クロスフェード", "CrossFade", "ブレンド", "遷移", "補間"),
            E(nameof(AnimData.Layer), "レイヤー", "ステートがある Animator のレイヤー番号。",
                FieldTier.Advanced, PurPlay, FieldRowKind.Normal, "Layer", "レイヤー", "重ねる"),
            E(nameof(AnimData.Mask), "アバターマスク", "上半身のみ等のマスク(情報用。CrossFade 自体には効かず、レイヤー設定で使います)。",
                FieldTier.Advanced, PurPlay, FieldRowKind.Normal, "マスク", "AvatarMask", "上半身"),
            FlagsEntry(PurPlay),
            E(RowStates, "Animator のステートから選ぶ", "対象の Animator の Controller にあるステート / クリップを一覧し、選ぶと対応する AnimData を探します(無ければその場で作れます)。",
                FieldTier.Common, PurPlay, FieldRowKind.Visual, "Animator", "ステート", "Controller", "クリップ", "探す", "作成", "コピー", "一括"),

            E(RowTimeline, "タイムライン", "クリックでその位置へシーク(一時停止)、マーカーのドラッグで時刻変更。黄 = SE / VFX、水色 = そのほか、赤 = Clip の長さ超え。SE は波形も重ねて出ます。",
                FieldTier.Common, PurEvents, FieldRowKind.Visual, "シーク", "再生ヘッド", "マーカー", "波形", "フレーム", "スクラブ"),
            E(nameof(AnimData.Events), "イベント(SE・VFX・そのほか)", "再生の節目(フレーム / 秒 / ループ)で SE・VFX・配置セットを出す、または値を送る。1 イベント = 1 行。追加は「＋ 現在位置に…」。",
                FieldTier.Common, PurEvents, FieldRowKind.Visual, "SE", "VFX", "配置セット", "PlayAsset", "フレーム", "秒", "Trigger", "Frame", "Time", "OnLoop", "繰り返し", "コピー", "試聴", "足音"),

            E(nameof(AnimData.Ik), "手足 IK", "手足の IK の on / off とウェイトカーブ(正規化時間 → 0〜1)。ターゲットはコードから AnimatorProxy に渡します。",
                FieldTier.Common, PurBody, FieldRowKind.Visual, "IK", "手", "足", "左手", "右手", "左足", "右足", "ウェイト", "カーブ"),
            E(nameof(AnimData.BlendShapes), "表情(BlendShape)", "ブレンドシェイプ(表情等)の時間 → ウェイト(0〜100)。1 シェイプ = 1 行。モデルに無い名前は ⚠ で知らせます。",
                FieldTier.Common, PurBody, FieldRowKind.Visual, "BlendShape", "ブレンドシェイプ", "表情", "シェイプ", "顔", "カーブ", "ShapeName"),

            E(RowTarget, "動かす相手(シーン上の Animator)", "確認用シーン / プレハブモードを開くと自動で入ります。Hierarchy の別のモデルを入れても、選択から取得してもよい。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "Animator", "対象", "モデル", "選択", "プレハブ"),
            E(RowModel, "確認用のモデルと情報", "確認用シーンに置くモデル(ModelData)と、その Controller / BlendShape の情報。StateName・BlendShape 名がモデルに無いときはここで知らせます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "ModelData", "確認用", "配置", "Controller", "BlendShape", "外部管理", "FC_", "Model Editor", "照合"),
            E(RowPlayCtl, "一時停止・ポーズを戻す・状態", "再生中の一時停止 / 再開、再生前のポーズに戻す、いまの再生位置。再生・停止・リピート・速度は下の固定バー。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "一時停止", "再開", "ポーズ", "ステータス"),
            E(RowBlend, "ブレンド確認(遷移・レイヤー・Blend Tree)", "遷移シーケンス(A → B → …)、別レイヤーとの同時再生、Blend Tree パラメータの操作。Data は書き換えません。",
                FieldTier.Advanced, PurCheck, FieldRowKind.Visual, "ブレンド", "遷移", "シーケンス", "レイヤー", "Blend Tree", "パラメータ", "パッド", "CrossFade", "SwitchAt"),
            E(RowLog, "イベントログ", "再生で発火したイベントと操作の最近 12 行(発火した順)。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "ログ", "履歴", "発火"),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
