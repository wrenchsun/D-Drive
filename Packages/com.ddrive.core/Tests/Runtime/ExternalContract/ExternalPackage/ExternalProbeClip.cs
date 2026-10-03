using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ExternalPackage.Fake
{
    // 外部の PlayableAsset クリップ(D-Drive の型ではない)。
    public sealed class ExternalProbeClip : PlayableAsset, ITimelineClipAsset
    {
        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
            => Playable.Create(graph);
    }
}
