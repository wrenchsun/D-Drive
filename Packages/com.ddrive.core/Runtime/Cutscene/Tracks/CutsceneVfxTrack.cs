using DDrive.Foundation.Handle;
using DDrive.Runtime.Vfx;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    [TrackClipType(typeof(CutsceneVfxClip))]
    [TrackBindingType(typeof(Transform))]
    [TrackColor(0.9f, 0.5f, 0.8f)]
    public sealed class CutsceneVfxTrack : TrackAsset
    {
    }
}
