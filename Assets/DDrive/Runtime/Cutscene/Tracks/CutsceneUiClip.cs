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
            var behaviour = playable.GetBehaviour();
            behaviour.CanvasId = CanvasId;
            // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19)。
            behaviour.Context = owner != null ? owner.GetComponent<CutsceneDirectorContext>() : null;
            return playable;
        }
    }

    public sealed class CutsceneUiBehaviour : PlayableBehaviour
    {
        public CanvasId CanvasId;
        public CutsceneDirectorContext Context;
        private Handle<CanvasMarker> _handle;
        private bool _fired;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (_fired || !CanvasId.IsValid || Context == null || !Context.FireEnabled)
            {
                return;
            }

            _fired = true;
            // [26_timeline.md] §4.4(2026-09-19) — Edit Mode は Context.ManagerRefs.Ui、Play Mode
            // (ManagerRefs 未設定)は従来どおり静的ファサード。
            var ui = Context.ManagerRefs?.Ui;
            if (ui != null)
            {
                _handle = ui.Open(CanvasId);
                return;
            }

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
            var ui = Context?.ManagerRefs?.Ui;
            if (ui != null)
            {
                if (ui.IsOpen(_handle))
                {
                    ui.Close(_handle);
                }

                return;
            }

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
