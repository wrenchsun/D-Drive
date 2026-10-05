using DDrive.Runtime.Presentation;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using PresentationId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Presentation.PresentationMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    [TrackClipType(typeof(CutscenePresentationClip))]
    [TrackBindingType(typeof(Transform))]
    [TrackColor(0.4f, 0.4f, 0.9f)]
    public sealed class CutscenePresentationTrack : TrackAsset
    {
    }
}
