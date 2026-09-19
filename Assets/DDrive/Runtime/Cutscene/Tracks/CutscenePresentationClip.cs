using DDrive.Runtime.Presentation;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using PresentationId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Presentation.PresentationMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // [26_timeline.md] §4.3(6-10b) — Presentation クリップ。区間開始で Presentation.Play、区間終了
    // (または Cutscene の中断)で Cancel する。`OnSignal` トラックを持つ Presentation は Signal を送る
    // 手段が無いため Validation Warning とする(CutsceneDataValidator、[26] §3.1)。
    public sealed class CutscenePresentationClip : PlayableAsset
    {
        public PresentationId PresentationId;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CutscenePresentationBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.PresentationId = PresentationId;
            // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19) — Presentation クリップは
            // Edit Mode の直接 Manager 経路(ManagerRefs)を持たない(未対応、実装メモ参照)。
            // Context は FireEnabled のゲートにのみ使う。
            behaviour.Context = owner != null ? owner.GetComponent<CutsceneDirectorContext>() : null;
            return playable;
        }
    }

    public sealed class CutscenePresentationBehaviour : PlayableBehaviour
    {
        public PresentationId PresentationId;
        public CutsceneDirectorContext Context;
        private PresentationHandle _handle;
        private bool _fired;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (_fired || !PresentationId.IsValid || Context == null || !Context.FireEnabled)
            {
                return;
            }

            _fired = true;
            var root = ResolveRoot(playerData);
            var ctx = new PlayContext { Self = root };
            // 完全修飾で呼ぶ(CutsceneSeClip.cs と同じ理由: DDrive.Runtime.Presentation が子ネームスペース
            // として先に解決されてしまうため)。
            _handle = DDrive.Runtime.Presentation.Presentation.Play(PresentationId, in ctx);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!_fired)
            {
                return;
            }

            _fired = false;
            if (_handle.IsPlaying)
            {
                _handle.Cancel();
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

    [TrackClipType(typeof(CutscenePresentationClip))]
    [TrackBindingType(typeof(Transform))]
    [TrackColor(0.4f, 0.4f, 0.9f)]
    public sealed class CutscenePresentationTrack : TrackAsset
    {
    }
}
