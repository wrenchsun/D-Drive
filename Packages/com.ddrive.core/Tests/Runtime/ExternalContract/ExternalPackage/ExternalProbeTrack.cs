using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ExternalPackage.Fake
{
    // 外部の TrackAsset(D-Drive の型ではない)。バインド先は Animator。
    [TrackBindingType(typeof(Animator))]
    [TrackClipType(typeof(ExternalProbeClip))]
    public sealed class ExternalProbeTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
            => ScriptPlayable<ExternalProbeMixer>.Create(graph, inputCount);
    }
}
