using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Presentation;

namespace DDrive.Editor.Presentation
{
    // PresentationData の案内表(新デザイン PresentationEditor 用。docs/1010 UX-5)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: PresentationDataValidator が Error にする = トラックの Asset 未設定・SignalKey 空 → Tracks = 必須。
    // Warning の対象(TotalDuration の超過・Interruptible=false の長尺)= よく使う。Info だけ(PredictLocal・Flags)= 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく、確認用の補助の行(タイムライン・Signal・環境など)。
    // 検索・表示モード・目的別カードの対象にするために表へ載せている(PresentationEditor.CreateRow が行を作る)。
    internal sealed class PresentationFieldGuide : FieldGuide<PresentationData>
    {
        public const string PurFlow = "流れ（タイムライン）";
        public const string PurTracks = "トラック";
        public const string PurSignal = "合図（Signal）";
        public const string PurCheck = "確認する（環境・モデル・ログ）";

        public const string RowTimeline = "(timeline)";
        public const string RowSignals = "(signals)";
        public const string RowModel = "(model)";
        public const string RowPlayCtl = "(playctl)";
        public const string RowEnv = "(env)";
        public const string RowSceneView = "(sceneview)";
        public const string RowLog = "(log)";

        public static readonly PresentationFieldGuide Instance = new PresentationFieldGuide();

        private static readonly string[] PurposeList = { PurFlow, PurTracks, PurSignal, PurCheck };

        private static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(PresentationData.TotalDuration), "全体の長さ(秒)", "演出全体の長さ。0 のときは AtTime のトラックの最後の時刻から自動で決まります。OnSignal だけの演出は明示してください。",
                FieldTier.Common, PurFlow, FieldRowKind.Visual, "尺", "秒", "時間", "TotalDuration", "完了"),
            E(RowTimeline, "タイムライン", "トラックを時間の上に並べて見る・動かす。",
                FieldTier.Common, PurFlow, FieldRowKind.Visual, "ルーラー", "ズーム", "シーク", "再生ヘッド", "マーカー", "レーン", "スクラブ"),
            E(nameof(PresentationData.Interruptible), "途中で止められる", "ON のときだけ Cancel() で中断できます。OFF の Cancel は警告のうえ何もしません。",
                FieldTier.Common, PurFlow, FieldRowKind.Normal, "中断", "キャンセル", "Cancel", "止める"),
            E(nameof(PresentationData.PredictLocal), "行為者は待たずに再生(PredictLocal)", "Flags.Net=Cosmetic のときだけ有効。ON なら行為者は Host の配送を待たず、すぐローカルで再生します。",
                FieldTier.Advanced, PurFlow, FieldRowKind.Normal, "予測", "ネット", "Cosmetic", "遅延", "即時"),
            FlagsEntry(PurFlow),

            E(nameof(PresentationData.Tracks), "トラック", "演出を構成する Anim / SE / VFX / HitStop などの並び。1 トラック = 1 行。追加は「＋ トラック」か Data のドラッグ&ドロップ。",
                FieldTier.Required, PurTracks, FieldRowKind.Visual, "アセット", "Asset", "追加", "削除", "複製", "並べ替え", "Kind", "Anchor", "位置", "Params", "Scope", "StopOnCancel", "専用エディタ", "一緒に調整"),

            E(RowSignals, "Signal を手動で送る", "再生中に、コード側の合図(例: 攻撃が当たった)を手動で送って OnSignal のトラックを確かめます。停止中はボタンが無効です。Signal / Marker トラックの名前も一覧します。",
                FieldTier.Common, PurSignal, FieldRowKind.Visual, "シグナル", "合図", "OnSignal", "SignalKey", "Marker", "マーカー", "手動", "発火"),
            EventsEntry(PurSignal),

            E(RowModel, "確認用のモデル", "ctx.Self として確認用シーンに置くモデル。Animator が無くても VFX / SE / Shake / Haptic の基準点になります。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "ModelData", "キャラクター", "配置", "Self", "確認用シーン", "プレビュー"),
            E(RowPlayCtl, "一時停止・最初から・状態", "再生中の一時停止 / 再開、最初からやり直し、いまの再生位置。再生・停止・リピート・速度は下の固定バー。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "一時停止", "再開", "最初から", "シーク", "ステータス"),
            E(RowSceneView, "SceneView に位置を出す", "Vfx / Se / AnchorGroup トラックの出る位置を SceneView に表示・編集する(Anim / CameraShake / Haptic 等は対象外。AnchorGroup は全点の表示のみで、点の編集は Anchor Group Editor)。選んだトラックだけに絞ることもできます。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "SceneView", "シーン", "ハンドル", "Anchor", "位置", "表示", "AnchorGroup"),
            E(RowLog, "ログ", "再生で起きたこと(Marker・トラックの発火・Signal・完了・中断)の最近 16 行。",
                FieldTier.Common, PurCheck, FieldRowKind.Visual, "Marker", "TrackFired", "発火", "履歴"),
            E(RowEnv, "環境(ライト・背景)", "確認用シーンのライトの強さ・カメラの背景色を切り替える。",
                FieldTier.Advanced, PurCheck, FieldRowKind.Visual, "ライト", "背景", "明るさ", "環境切替", "Light", "Camera"),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
