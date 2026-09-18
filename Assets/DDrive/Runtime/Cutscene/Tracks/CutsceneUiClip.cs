using DDrive.Foundation.Handle;
using DDrive.Runtime.Ui;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CanvasId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Ui.CanvasMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // [26_timeline.md] §4.3(6-10b) — UI クリップ。区間中だけ Canvas を Open し、区間終了で Close する
    // (字幕・レターボックス等)。実体は UiManager(静的ファサード Ui.Open/Close)に委譲する。
    public sealed class CutsceneUiClip : PlayableAsset
    {
        public CanvasId CanvasId;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CutsceneUiBehaviour>.Create(graph);
            playable.GetBehaviour().CanvasId = CanvasId;
            return playable;
        }
    }

    public sealed class CutsceneUiBehaviour : PlayableBehaviour
    {
        public CanvasId CanvasId;
        private Handle<CanvasMarker> _handle;
        private bool _fired;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (_fired || !Application.isPlaying || !CanvasId.IsValid)
            {
                return;
            }

            _fired = true;
            // 完全修飾で呼ぶ(CutsceneSeClip.cs と同じ理由: DDrive.Runtime.Ui が子ネームスペースとして
            // 先に解決されてしまうため)。
            _handle = DDrive.Runtime.Ui.Ui.Open(CanvasId);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!_fired)
            {
                return;
            }

            _fired = false;
            if (DDrive.Runtime.Ui.Ui.IsOpen(_handle))
            {
                DDrive.Runtime.Ui.Ui.Close(_handle);
            }
        }
    }

    [TrackClipType(typeof(CutsceneUiClip))]
    [TrackColor(0.7f, 0.7f, 0.2f)]
    public sealed class CutsceneUiTrack : TrackAsset
    {
    }
}
