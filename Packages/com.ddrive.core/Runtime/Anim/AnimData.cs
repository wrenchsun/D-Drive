using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Runtime.Anim
{
    // 手足 IK の on/off とウェイトカーブ([05_model_animation.md] B-2)。
    // ウェイトは正規化時間(0..1)→0..1。ターゲット Transform は AnimatorProxy 側にコードから渡す。
    [Serializable]
    public struct IkProfile
    {
        public bool LeftHand;
        public bool RightHand;
        public bool LeftFoot;
        public bool RightFoot;

        [Tooltip("正規化時間(0..1)→IK ウェイト(0..1)。空なら常に 1。")]
        public AnimationCurve Weight;

        public bool Any => LeftHand || RightHand || LeftFoot || RightFoot;

        public float EvaluateWeight(float normalizedTime)
            => Weight == null || Weight.length == 0 ? 1f : Mathf.Clamp01(Weight.Evaluate(Mathf.Clamp01(normalizedTime)));
    }

    // ブレンドシェイプ(表情等)の時間→ウェイト。正規化時間(0..1)→0..100。
    [Serializable]
    public struct BlendShapeTrack
    {
        public string ShapeName;
        public AnimationCurve Weight;
    }

    // [05_model_animation.md] B-2 — 3D アニメーションの再生単位。
    // AnimatorController は「土台」(基本遷移)、AnimData は「再生単位」(ワンショットの CrossFade)。
    // イベントは Unity の AnimationEvent ではなく共通 AssetEvent(基底 Events)を AnimManager が発火する。
    [CreateAssetMenu(menuName = "D-Drive/Anim/Anim Data", fileName = "ANIM_NewAnim")]
    [AssetIdDefinition(AssetType.Anim, typeof(AnimMarker), "ANIMID")]
    public class AnimData : AssetDataBase
    {
        [Header("Clip")]
        [Tooltip("再生するクリップ。長さ・フレームレートはイベント(Frame/Time)と終了判定に使う。")]
        public AnimationClip Clip;

        [Header("StateMachine")]
        [Tooltip("AnimatorController 上のステート名。CrossFade の対象。空なら Clip の名前を使う。")]
        public string StateName;

        [Tooltip("ステートがあるレイヤー。")]
        [Min(0)] public int Layer;

        [Tooltip("ループ再生するか。true なら周回ごとに OnLoop を発火し、Stop されるまで続く。")]
        public bool Loop;

        [Tooltip("CrossFade 秒数の既定値。Play(id, animator, fade) で上書きできる。")]
        [Min(0f)] public float DefaultCrossFade = 0.1f;

        [Tooltip("上半身のみ等のマスク(情報用。CrossFade 自体には効かず、レイヤー設定で使う)。")]
        public AvatarMask Mask;

        [Header("IK")]
        public IkProfile Ik;

        [Header("BlendShape")]
        public BlendShapeTrack[] BlendShapes;

        // Clip が無い(Placeholder 等)ときの長さ。
        public const float FallbackLengthSec = 0.5f;

        public float LengthSec => Clip != null && Clip.length > 0f ? Clip.length : FallbackLengthSec;

        public float FrameRate => Clip != null && Clip.frameRate > 0f ? Clip.frameRate : 30f;

        public string ResolvedStateName => !string.IsNullOrEmpty(StateName) ? StateName : (Clip != null ? Clip.name : string.Empty);
    }
}
