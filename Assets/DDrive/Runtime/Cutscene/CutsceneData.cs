using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene
{
    public readonly struct CutsceneMarker
    {
    }

    // [26_timeline.md] §4.2 — トラックのバインド先をどう解決するか。
    public enum CutsceneBindTarget
    {
        MainCamera,
        Self,
        Target,
        SpawnModel,
        SceneObjectByName,
        AnchorPoint,
    }

    // [26_timeline.md] §4.2.1 — Maya のワールド座標をゲームのどこに置くか。
    public enum CutsceneOrigin
    {
        Self,
        World,
        AnchorPoint,
    }

    // [26_timeline.md] §4.1 / §4.7 — Skip() の許可範囲。
    public enum CutsceneSkip
    {
        Disabled,
        Immediate,
        ToMarker,
    }

    // FBX から取り込むフレーム範囲。End は含む。Start=End=0 で「FBX 全体」(既定、[26] §5.1)。
    [Serializable]
    public struct FrameRange
    {
        public int Start;
        public int End;
    }

    // 役割名(TimelineAsset 上のトラック名) → 解決方法([26] §4.2)。
    [Serializable]
    public struct CutsceneBinding
    {
        [Tooltip("TimelineAsset 上のトラック名(FBX のノード名・ファイル名から自動、または手入力)。")]
        public string TrackName;

        [Tooltip("このトラックをどの Transform/Animator へバインドするか。")]
        public CutsceneBindTarget Target;

        [Tooltip("Target=SpawnModel のとき使う ModelData。再生開始時に生成し、終了時に返却する。")]
        public AssetId<DDrive.Runtime.Model.ModelMarker> Model;

        [Tooltip("Target=SceneObjectByName/AnchorPoint のとき使う名前。")]
        public string SceneObjectName;
    }

    // [26_timeline.md] §4.1 — Maya FBX 取り込み + D-Drive トラックの基盤データ(6-10a)。
    // TimelineAsset 自体は Unity 標準 Timeline ウィンドウで編集する([26] §3「編集 UI は Unity 標準の
    // Timeline ウィンドウを使う」)。CutsceneManager は「誰にバインドするか」「原点」「ネット同期」を
    // 薄く上乗せするだけで、Track/Clip の再生自体は PlayableDirector に委譲する(ADR-4 と同じ考え方)。
    [CreateAssetMenu(menuName = "D-Drive/Cutscene/Cutscene Data", fileName = "CUT_NewCutscene")]
    [AssetIdDefinition(AssetType.Cutscene, typeof(CutsceneMarker), "CUTID")]
    public class CutsceneData : AssetDataBase
    {
        [Header("Timeline")]
        [Tooltip("再生する TimelineAsset(6-10c の自動生成、または手置き)。")]
        public TimelineAsset Timeline;

        [Tooltip("役割名(トラック名) → 解決方法の一覧([26] §4.2)。")]
        public CutsceneBinding[] Bindings = Array.Empty<CutsceneBinding>();

        [Tooltip("Maya のワールド座標をゲームのどこに置くか([26] §4.2.1)。")]
        public CutsceneOrigin Origin = CutsceneOrigin.Self;

        [Tooltip("Origin=AnchorPoint のとき使う、シーン上の AnchorPoint(DDrive.Runtime.Anchoring.AnchorPoint)の名前。")]
        public string OriginAnchorName;

        [Tooltip("Maya のシーン fps(取り込みで自動設定、[26] §5.3)。再生自体は秒ベースなので、これは Timeline " +
                 "ウィンドウの表示・スナップ用であり StepFps(6-10b の Camera クリップ)とは別物。")]
        public float FrameRate = 30f;

        [Tooltip("取り込むフレーム範囲。Start=End=0 で FBX 全体(既定、[26] §5.1「逃げ道」)。")]
        public FrameRange SourceFrameRange;

        [Tooltip("Skip() の挙動。Disabled=不可 / Immediate=即終了 / ToMarker=指定マーカーまで飛ばす([26] §4.1)。")]
        public CutsceneSkip Skip = CutsceneSkip.Immediate;

        [Tooltip("Skip=ToMarker のときの目標マーカー名。6-10b の D-Drive Signal マーカーが無いうちは見つからず、" +
                 "警告のうえ Immediate と同じ(末尾まで)にフォールバックする。")]
        public string SkipToMarkerKey;

        [Tooltip("PlayableDirector.extrapolationMode。既定 None(終わったら解除)。")]
        public DirectorWrapMode Wrap = DirectorWrapMode.None;

        [Tooltip("再生中はプレイヤー入力を止める、という意図の宣言。実際に入力を止めるのはゲーム側の責務" +
                 "([26] §4.5.1)。D-Drive は CutsceneHandle.IsInputLocked / Cutscene.OnInputLockChanged / " +
                 "EventBus の Custom トリガ(cutscene/input_lock, cutscene/input_unlock)で通知するだけ。")]
        public bool LockInput;

        [Tooltip("Flags.Net=Cosmetic のとき、行為者は Host 確定の Broadcast を待たず即ローカル再生する" +
                 "(予測再生。PresentationData.PredictLocal と同じ意味、[26] §4.7)。既定 false。")]
        public bool PredictLocal;

        // Events(AssetEvent[])は AssetDataBase 共通。Trigger=Time は Timeline 上の秒として扱う([26] §4.3)。
    }
}
