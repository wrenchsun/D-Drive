using DDrive.Foundation.Handle;
using UnityEngine;
using BgmId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.BgmMarker>;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;
using AnchorId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker>;

namespace DDrive.Runtime.Audio
{
    // デザイナー/プログラマー向けの薄い静的ファサード。実体は Bind されたインスタンスに委譲する
    // (ADR#3: static ではなく DI 可能なサービス + static ファサードを両立)。
    public static class Audio
    {
        private static AudioManager _seInstance;
        private static BgmManager _bgmInstance;

        public static void Bind(AudioManager instance) => _seInstance = instance;

        public static void Bind(BgmManager instance) => _bgmInstance = instance;

        public static bool IsBound => _seInstance != null && _bgmInstance != null;

        public static Handle<SeMarker> PlaySe(SeId id) => _seInstance?.PlaySe(id) ?? Handle<SeMarker>.Invalid;

        public static Handle<SeMarker> PlaySe(SeId id, Vector3 pos) => _seInstance?.PlaySe(id, pos) ?? Handle<SeMarker>.Invalid;

        public static Handle<SeMarker> PlaySe(SeId id, Transform contextRoot) => _seInstance?.PlaySe(id, contextRoot) ?? Handle<SeMarker>.Invalid;

        // Anchor アセットを明示して再生する(Data.AnchorId / 埋め込み Anchor より優先。[21] §3.3)。
        public static Handle<SeMarker> PlaySe(SeId id, AnchorId anchor) => _seInstance?.PlaySe(id, anchor) ?? Handle<SeMarker>.Invalid;

        public static Handle<SeMarker> PlaySe(SeId id, AnchorId anchor, Transform contextRoot)
            => _seInstance?.PlaySe(id, anchor, contextRoot) ?? Handle<SeMarker>.Invalid;

        public static void Stop(Handle<SeMarker> h, float fade = 0f) => _seInstance?.Stop(h, fade);

        public static void PlayBgm(BgmId id, float fadeIn = -1f) => _bgmInstance?.PlayBgm(id, fadeIn);

        public static void StopBgm(float fadeOut = -1f) => _bgmInstance?.StopBgm(fadeOut);

        public static void CrossFade(BgmId next, float duration) => _bgmInstance?.CrossFade(next, duration);
    }
}
