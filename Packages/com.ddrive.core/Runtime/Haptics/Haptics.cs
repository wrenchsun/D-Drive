using DDrive.Foundation.Handle;
using HapticId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker>;

namespace DDrive.Runtime.Haptics
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Vfx.cs / CameraFx.cs と同じ設計。ADR#3)。
    // Bind 前 / 未 Bind の呼び出しは全て no-op(Invalid Handle)で継続する(例外で止めない)。
    public static class Haptics
    {
        private static HapticsManager _instance;

        public static void Bind(HapticsManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static Handle<HapticMarker> Play(HapticId id) => _instance?.Play(id) ?? Handle<HapticMarker>.Invalid;

        public static Handle<HapticMarker> Play(HapticId id, float strengthScale) => _instance?.Play(id, strengthScale) ?? Handle<HapticMarker>.Invalid;

        public static void Stop(Handle<HapticMarker> h) => _instance?.Stop(h);

        public static bool IsPlaying(Handle<HapticMarker> h) => _instance?.IsPlaying(h) ?? false;

        public static void StopAll() => _instance?.StopAll();

        // オプション画面の「振動 0〜100%」/ OFF。
        public static void SetGlobalScale(float scale) => _instance?.SetGlobalScale(scale);
    }

    public static class HapticHandleExtensions
    {
        public static void Stop(this Handle<HapticMarker> h) => Haptics.Stop(h);

        public static bool IsPlaying(this Handle<HapticMarker> h) => Haptics.IsPlaying(h);
    }
}
