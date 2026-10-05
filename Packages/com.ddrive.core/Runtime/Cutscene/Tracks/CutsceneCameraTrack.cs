using DDrive.Foundation.Values;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene.Tracks
{
    [TrackClipType(typeof(CutsceneCameraClip))]
    [TrackColor(0.95f, 0.85f, 0.2f)]
    public sealed class CutsceneCameraTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<CutsceneCameraMixerBehaviour>.Create(graph, inputCount);
            mixer.GetBehaviour().Owner = go;
            return mixer;
        }
    }
}
