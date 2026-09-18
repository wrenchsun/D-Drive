using System;
using DDrive.Foundation.Data;
using UnityEngine;

namespace DDrive.Runtime.Presentation
{
    // [08_presentation.md] §3 — トラックがいつ発火するか。
    public enum TrackTrigger
    {
        // Time(秒)に到達したら発火。0 は Play() 呼び出し時に即時発火する([08] §3)。
        AtTime,

        // handle.Signal(SignalKey) が呼ばれたら発火(ゲームコードの当たり判定等から)。1 トラックにつき最大 1 回。
        OnSignal,
    }

    // [01_architecture.md] §6 / [08_presentation.md] §2 のトラック種別。
    // CameraShake([16] Part A)/ Haptic([16] Part B)は 5-2/5-2b で実装済み。Timeline は 6-10a で
    // CutsceneManager に接続済み(未配線時は警告 1 回 + no-op)。
    // Marker / Signal は他 Manager に委譲せず、SignalKey をそのまま「名前」として使う(実装メモ: [08] 実装メモ参照)。
    //   Marker → handle.OnMarker(string) を発火(データ→コードの通知)
    //   Signal → PlayContext.OnSignal(string) を呼ぶ(データ→コードの通知。handle.Signal() はコード→データの逆方向)
    public enum TrackKind
    {
        Anim,
        Anim2D,
        Se,
        Bgm,
        Vfx,
        CameraShake,
        Haptic,
        HitStop,
        Timeline,
        Canvas,
        UiTween,
        Marker,
        Signal,
    }

    // [08_presentation.md] §2 の「シーン配置型アンカーとの連携」— トラック単位でどの Transform を基準に
    // Anchor(BoneName/NamedObject)を解決するか。
    //   Self          : PlayContext.Self 配下から解決する(既定)
    //   ContextTarget : PlayContext.Target 配下から解決する
    //   World         : PlayContext を参照せず、Anchor.LocalOffset をそのまま絶対ワールド座標として使う
    //   Anchor        : World と同じ解決(PlayContext 非参照)だが、「環境据え置きの演出」であることを
    //                   データ上明示するための値(現状は World と同一実装。要判断: [08] 実装メモ参照)
    public enum TrackTargetMode
    {
        Self,
        ContextTarget,
        World,
        Anchor,
    }

    [Serializable]
    public struct PresentationTrack
    {
        [Tooltip("AtTime=Time(秒)で発火 / OnSignal=SignalKeyの一致で発火。")]
        public TrackTrigger Trigger;

        [Tooltip("Trigger=AtTime のときの発火秒数。0 は Play() 呼び出し時に即時発火。")]
        public float Time;

        [Tooltip("Trigger=OnSignal のときの一致キー。Kind=Marker/Signal のときは通知する名前として使う。")]
        public string SignalKey;

        [Tooltip("このトラックが委譲する Manager の種別。")]
        public TrackKind Kind;

        [Tooltip("再生する対象アセットの ID(種別は Kind と対応させること)。Marker/Signal/HitStop は未使用。")]
        public AssetRef Asset;

        [Tooltip("Anchor(BoneName/NamedObject)をどの Transform 配下から解決するか([04_vfx.md] §2.5)。")]
        public TrackTargetMode Target;

        [Tooltip("VFX/SE 用のアタッチ位置定義。Target で決まる基準からの相対位置(BoneName/NamedObject/ContextTarget)、" +
                 "またはワールド絶対位置(World)を表す。")]
        public AnchorDef Anchor;

        [Tooltip("Kind ごとの上書きパラメータ(例: HitStop の秒数 Params[0])。")]
        public ParamValue[] Params;

        [Tooltip("Cancel() 時、このトラックが起動した実体(VFX/SE/UiTween 等)も一緒に止めるか。")]
        public bool StopOnCancel;
    }
}
