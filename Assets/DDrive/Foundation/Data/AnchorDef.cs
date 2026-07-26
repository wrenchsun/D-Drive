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
        public AnchorSpace Space;
        public string Path;
        public Vector3 LocalOffset;
        public Vector3 LocalEuler;
        public Vector3 LocalScale;
        public bool FollowRotation;
        public bool DetachOnStop;

        public static AnchorDef WorldDefault => new AnchorDef
        {
            Space = AnchorSpace.World,
            LocalScale = Vector3.one,
        };
    }
}
