using DDrive.Foundation.Handle;
using DDrive.Runtime.Vfx;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // [26_timeline.md] §4.3(6-10b) — VFX クリップ。区間終了で Stop する(ループ VFX 向け。ワンショットは
    // 既に再生済みのため無害)。実再生は VfxManager(静的ファサード Vfx.Spawn)に委譲する(D-Drive の禁止 API
    // である Instantiate 直呼びを Timeline 標準 Control トラックの代わりに回避する狙い、[26] §1.3)。
    public sealed class CutsceneVfxClip : PlayableAsset
    {
        [Tooltip("再生する VfxId。区間終了で Stop する(ループ VFX 向け)。")]
        public VfxId VfxId;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CutsceneVfxBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.VfxId = VfxId;
            // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19)。
            behaviour.Context = owner != null ? owner.GetComponent<CutsceneDirectorContext>() : null;
            return playable;
        }
    }

    public sealed class CutsceneVfxBehaviour : PlayableBehaviour
    {
        public VfxId VfxId;
        public CutsceneDirectorContext Context;
        private Handle<VfxMarker> _handle;
        private bool _fired;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (_fired || !VfxId.IsValid || Context == null || !Context.FireEnabled)
            {
                return;
            }

            _fired = true;
            var root = ResolveRoot(playerData);
            // [26_timeline.md] §4.4(2026-09-19) — Edit Mode は Context.ManagerRefs.Vfx(Editor 用
            // VfxManager)、Play Mode(ManagerRefs 未設定)は従来どおり静的ファサード。
            var vfx = Context.ManagerRefs?.Vfx;
            if (vfx != null)
            {
                _handle = root != null ? vfx.Spawn(VfxId, root) : vfx.Spawn(VfxId);
                return;
            }

            // 完全修飾で呼ぶ(このファイルの名前空間 DDrive.Runtime.Cutscene.Tracks から素の `Vfx` は
            // DDrive.Runtime.Vfx を子ネームスペースとして解決してしまい、`Vfx.Spawn` がコンパイルエラーになる)。
            _handle = root != null ? DDrive.Runtime.Vfx.Vfx.Spawn(VfxId, root) : DDrive.Runtime.Vfx.Vfx.Spawn(VfxId);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!_fired)
            {
                return;
            }

            _fired = false;
            var vfx = Context?.ManagerRefs?.Vfx;
            if (vfx != null)
            {
                if (vfx.IsPlaying(_handle))
                {
                    vfx.Stop(_handle);
                }

                return;
            }

            if (DDrive.Runtime.Vfx.Vfx.IsPlaying(_handle))
            {
                DDrive.Runtime.Vfx.Vfx.Stop(_handle);
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

    [TrackClipType(typeof(CutsceneVfxClip))]
    [TrackBindingType(typeof(Transform))]
    [TrackColor(0.9f, 0.5f, 0.8f)]
    public sealed class CutsceneVfxTrack : TrackAsset
    {
    }
}
