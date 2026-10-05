using DDrive.Foundation.Handle;
using DDrive.Runtime.Ui;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CanvasId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Ui.CanvasMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    [TrackClipType(typeof(CutsceneUiClip))]
    [TrackColor(0.7f, 0.7f, 0.2f)]
    public sealed class CutsceneUiTrack : TrackAsset
    {
    }
}
