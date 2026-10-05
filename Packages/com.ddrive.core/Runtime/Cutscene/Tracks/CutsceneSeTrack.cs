using DDrive.Foundation.Handle;
using DDrive.Runtime.Audio;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    [TrackClipType(typeof(CutsceneSeClip))]
    [TrackBindingType(typeof(Transform))]
    [TrackColor(0.2f, 0.7f, 0.9f)]
    public sealed class CutsceneSeTrack : TrackAsset
    {
    }
}
