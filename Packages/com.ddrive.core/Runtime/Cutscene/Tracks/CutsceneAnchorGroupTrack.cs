using DDrive.Foundation.Handle;
using DDrive.Runtime.Anchoring;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using AnchorGroupId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorGroupMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    [TrackClipType(typeof(CutsceneAnchorGroupClip))]
    [TrackBindingType(typeof(Transform))]
    [TrackColor(0.6f, 0.8f, 0.4f)]
    public sealed class CutsceneAnchorGroupTrack : TrackAsset
    {
    }
}
