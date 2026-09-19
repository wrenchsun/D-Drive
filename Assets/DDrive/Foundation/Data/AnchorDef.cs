using System;
using UnityEngine;

namespace DDrive.Foundation.Data
{
    // [03_audio.md] / [04_vfx.md] で共用されるアタッチ位置定義。
    public enum AnchorSpace
    {
        World,
        BoneName,
        NamedObject,
        ContextTarget,
    }

    [Serializable]
    public struct AnchorDef : IEquatable<AnchorDef>
    {
        [Tooltip("World=固定座標 / BoneName・NamedObject=Pathで指定した名前を階層から検索 / ContextTarget=呼び出し元が渡すTransform自体。")]
        public AnchorSpace Space;

        [Tooltip("Space=BoneName/NamedObject の時に検索する名前(ボーン名またはオブジェクト名)。")]
        public string Path;

        [Tooltip("アタッチ先からのローカルオフセット位置。")]
        public Vector3 LocalOffset;

        [Tooltip("アタッチ先からのローカルオフセット回転(オイラー角)。")]
        public Vector3 LocalEuler;

        [Tooltip("ローカルスケール。")]
        public Vector3 LocalScale;

        [Tooltip("アタッチ後、アタッチ先の回転に追従するか。")]
        public bool FollowRotation;

        [Tooltip("アタッチ先が破棄された後も、その場に残って鳴り終わり/再生完了まで続けるか。")]
        public bool DetachOnStop;

        public static AnchorDef WorldDefault => new AnchorDef
        {
            Space = AnchorSpace.World,
            LocalScale = Vector3.one,
        };

        // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照) — 「設定されている」の
        // 判定に使う。LocalScale は(0,0,0)と(1,1,1)を同じ意味として扱う(AnchorPose.BaseScale が
        // LocalScale==0 を 1 として扱うのと同じ規則。struct の既定値〔全フィールド 0〕と WorldDefault
        // 〔LocalScale=1〕が「実質同じ既定値」であることを保証するため)。Path は null と空文字を同一視する。
        public bool IsDefault => Equals(WorldDefault);

        public bool Equals(AnchorDef other)
        {
            var scaleA = LocalScale == Vector3.zero ? Vector3.one : LocalScale;
            var scaleB = other.LocalScale == Vector3.zero ? Vector3.one : other.LocalScale;
            return Space == other.Space
                && string.Equals(Path ?? string.Empty, other.Path ?? string.Empty, StringComparison.Ordinal)
                && LocalOffset == other.LocalOffset
                && LocalEuler == other.LocalEuler
                && scaleA == scaleB
                && FollowRotation == other.FollowRotation
                && DetachOnStop == other.DetachOnStop;
        }

        public override bool Equals(object obj) => obj is AnchorDef other && Equals(other);

        public override int GetHashCode()
        {
            var scale = LocalScale == Vector3.zero ? Vector3.one : LocalScale;
            unchecked
            {
                var hash = (int)Space;
                hash = (hash * 397) ^ (Path ?? string.Empty).GetHashCode();
                hash = (hash * 397) ^ LocalOffset.GetHashCode();
                hash = (hash * 397) ^ LocalEuler.GetHashCode();
                hash = (hash * 397) ^ scale.GetHashCode();
                hash = (hash * 397) ^ FollowRotation.GetHashCode();
                hash = (hash * 397) ^ DetachOnStop.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(AnchorDef a, AnchorDef b) => a.Equals(b);
        public static bool operator !=(AnchorDef a, AnchorDef b) => !a.Equals(b);
    }
}
