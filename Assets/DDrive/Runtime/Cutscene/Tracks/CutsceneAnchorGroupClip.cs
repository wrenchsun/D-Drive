using DDrive.Foundation.Handle;
using DDrive.Runtime.Anchoring;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using AnchorGroupId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorGroupMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // [26_timeline.md] §4.3(6-10b) — 配置セット(AnchorGroup)クリップ。区間終了で Stop する。
    // 実再生は AnchorGroupPlayer(静的ファサード Anchors.Play)に委譲する。
    public sealed class CutsceneAnchorGroupClip : PlayableAsset
    {
        public AnchorGroupId GroupId;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CutsceneAnchorGroupBehaviour>.Create(graph);
            playable.GetBehaviour().GroupId = GroupId;
            return playable;
        }
    }

    public sealed class CutsceneAnchorGroupBehaviour : PlayableBehaviour
    {
        public AnchorGroupId GroupId;
        private Handle<AnchorGroupMarker> _handle;
        private bool _fired;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (_fired || !Application.isPlaying || !GroupId.IsValid)
            {
                return;
            }

            _fired = true;
            var root = ResolveRoot(playerData);
            // 完全修飾で呼ぶ(CutsceneSeClip.cs と同じ理由: DDrive.Runtime.Anchoring が子ネームスペースとして
            // 先に解決されてしまうため)。
            _handle = root != null ? DDrive.Runtime.Anchoring.Anchors.Play(GroupId, root) : DDrive.Runtime.Anchoring.Anchors.Play(GroupId);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!_fired)
            {
                return;
            }

            _fired = false;
            if (DDrive.Runtime.Anchoring.Anchors.IsPlaying(_handle))
            {
                DDrive.Runtime.Anchoring.Anchors.Stop(_handle);
            }
        }

        private static Transform ResolveRoot(object playerData)
        {
            if (playerData is Transform t)
            {
                return t;
            }

            if (playerData is Animator anim)
            {
                return anim.transform;
            }

            return null;
        }
    }

    [TrackClipType(typeof(CutsceneAnchorGroupClip))]
    [TrackBindingType(typeof(Transform))]
    [TrackColor(0.6f, 0.8f, 0.4f)]
    public sealed class CutsceneAnchorGroupTrack : TrackAsset
    {
    }
}
