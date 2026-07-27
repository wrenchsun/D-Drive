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
    public struct AnchorDef
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
    }
}
