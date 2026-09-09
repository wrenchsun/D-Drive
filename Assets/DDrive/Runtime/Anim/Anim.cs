using DDrive.Foundation.Handle;
using UnityEngine;
using AnimId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim.AnimMarker>;

namespace DDrive.Runtime.Anim
{
    // [05_model_animation.md] B-3 — 静的ファサード。プログラマーは `Anim.Play(ANIMID.Attack01, animator)` だけ書く。
    // 未 Bind の呼び出しは no-op(Invalid Handle)で継続する(例外で止めない)。
    public static class Anim
    {
        private static AnimManager _instance;

        public static void Bind(AnimManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static Handle<AnimMarker> Play(AnimId id, Animator target) => _instance?.Play(id, target) ?? Handle<AnimMarker>.Invalid;

        public static Handle<AnimMarker> Play(AnimId id, Animator target, float fade) => _instance?.Play(id, target, fade) ?? Handle<AnimMarker>.Invalid;

        public static void Stop(Handle<AnimMarker> h, float fade = 0f) => _instance?.Stop(h, fade);

        public static bool IsPlaying(Handle<AnimMarker> h) => _instance?.IsPlaying(h) ?? false;

        public static float NormalizedTime(Handle<AnimMarker> h) => _instance?.GetNormalizedTime(h) ?? -1f;

        public static void SetSpeed(Handle<AnimMarker> h, float speed) => _instance?.SetSpeed(h, speed);

        // StateMachine 遷移用(基本遷移は Controller 側の責務)。Animator 未指定は no-op。
        public static void SetTrigger(Animator target, string param)
        {
            if (target != null && !string.IsNullOrEmpty(param))
            {
                target.SetTrigger(param);
            }
        }

        public static void SetLayerWeight(Animator target, int layer, float weight)
        {
            if (target != null && layer >= 0 && layer < target.layerCount)
            {
                target.SetLayerWeight(layer, Mathf.Clamp01(weight));
            }
        }
    }

    public static class AnimHandleExtensions
    {
        public static bool IsPlaying(this Handle<AnimMarker> h) => Anim.IsPlaying(h);

        public static void Stop(this Handle<AnimMarker> h, float fade = 0f) => Anim.Stop(h, fade);

        public static void SetSpeed(this Handle<AnimMarker> h, float speed) => Anim.SetSpeed(h, speed);

        public static float NormalizedTime(this Handle<AnimMarker> h) => Anim.NormalizedTime(h);
    }
}
