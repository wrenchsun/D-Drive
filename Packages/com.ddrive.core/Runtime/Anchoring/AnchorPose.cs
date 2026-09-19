using DDrive.Foundation.Data;
using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    // AnchorDef + 解決済み Transform から「実際に置くワールド姿勢」を求める純粋計算と、その逆変換
    // (ワールド座標 → AnchorDef.LocalOffset/LocalEuler)。Manager の Spawn/Tick/ReapplyAnchor と、
    // VfxEditor の SceneView ハンドル編集が同じ式を共有するために切り出した([04_vfx.md] §2/§5)。
    //
    // 規則:
    //   位置   = target.TransformPoint(LocalOffset + extraOffset)          (target 無し: LocalOffset + extraOffset をワールド座標)
    //   回転   = (FollowRotation ? target.rotation : identity) * Euler(LocalEuler) * jitterRotation
    //   スケール = (LocalScale==0 ? 1 : LocalScale) * scaleMultiplier
    // extraOffset / jitterRotation / scaleMultiplier は AnchorPoint 由来(SpawnOffset + ランダム散らばり)で、
    // Spawn 時に 1 回だけサンプリングされた値を呼び出し側が保持して渡す。
    public static class AnchorPose
    {
        public static Vector3 LocalOffsetWithExtra(in AnchorDef anchor, Vector3 extraOffset) => anchor.LocalOffset + extraOffset;

        public static Vector3 BaseScale(in AnchorDef anchor) => anchor.LocalScale == Vector3.zero ? Vector3.one : anchor.LocalScale;

        // アタッチ先の回転に追従する場合の「基準回転」。LocalEuler はこの基準に対する相対回転として扱う。
        public static Quaternion BaseRotation(in AnchorDef anchor, Transform target)
            => target != null && anchor.FollowRotation ? target.rotation : Quaternion.identity;

        public static Vector3 WorldPosition(in AnchorDef anchor, Transform target, Vector3 extraOffset)
        {
            var local = LocalOffsetWithExtra(anchor, extraOffset);
            return target != null ? target.TransformPoint(local) : local;
        }

        public static Quaternion WorldRotation(in AnchorDef anchor, Transform target, Quaternion jitterRotation)
            => BaseRotation(anchor, target) * Quaternion.Euler(anchor.LocalEuler) * jitterRotation;

        public static Vector3 WorldScale(in AnchorDef anchor, float scaleMultiplier)
            => BaseScale(anchor) * scaleMultiplier;

        // SceneView 等でワールド座標を直接動かした結果を AnchorDef.LocalOffset に戻す(extraOffset は差し引く)。
        public static Vector3 LocalOffsetFromWorld(Transform target, Vector3 worldPosition, Vector3 extraOffset)
        {
            var local = target != null ? target.InverseTransformPoint(worldPosition) : worldPosition;
            return local - extraOffset;
        }

        // ワールド回転を AnchorDef.LocalEuler に戻す(jitter は編集対象外なので identity 前提で解く)。
        public static Vector3 LocalEulerFromWorld(in AnchorDef anchor, Transform target, Quaternion worldRotation)
            => (Quaternion.Inverse(BaseRotation(anchor, target)) * worldRotation).eulerAngles;
    }
}
