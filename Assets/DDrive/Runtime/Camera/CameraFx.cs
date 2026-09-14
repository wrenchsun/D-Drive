using DDrive.Foundation.Handle;
using UnityEngine;
using ShakeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Camera.ShakeMarker>;

namespace DDrive.Runtime.Camera
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Vfx.cs / Audio.cs と同じ設計。ADR#3)。
    // Bind 前 / 未 Bind の呼び出しは全て no-op(Invalid Handle)で継続する(例外で止めない)。
    public static class CameraFx
    {
        private static CameraFxManager _instance;

        public static void Bind(CameraFxManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static Handle<ShakeMarker> Shake(ShakeId id) => _instance?.Shake(id) ?? Handle<ShakeMarker>.Invalid;

        // FromSource 用: 発生位置から「奥から手前に押される」方向シェイクを計算する([16] Part A)。
        public static Handle<ShakeMarker> Shake(ShakeId id, Vector3 sourcePos) => _instance?.Shake(id, sourcePos) ?? Handle<ShakeMarker>.Invalid;

        // 距離減衰等の外部係数。
        public static Handle<ShakeMarker> Shake(ShakeId id, float strengthScale) => _instance?.Shake(id, strengthScale) ?? Handle<ShakeMarker>.Invalid;

        public static void Stop(Handle<ShakeMarker> h, float fade = 0.1f) => _instance?.Stop(h, fade);

        public static void SetStrength(Handle<ShakeMarker> h, float strength) => _instance?.SetStrength(h, strength);

        public static bool IsPlaying(Handle<ShakeMarker> h) => _instance?.IsPlaying(h) ?? false;

        public static void StopAll(float fadeOut = 0.1f) => _instance?.StopAllWithFade(fadeOut);

        // オプション画面の「画面揺れ 0〜100%」。0 で完全に無揺れ([16] Part A アクセシビリティ要件)。
        public static void SetGlobalScale(float scale) => _instance?.SetGlobalScale(scale);
    }

    // `h.Stop()` / `h.SetStrength(s)` の書き味を Handle 型を汚さずに提供する拡張(VfxHandleExtensions と同じ設計)。
    public static class ShakeHandleExtensions
    {
        public static void Stop(this Handle<ShakeMarker> h, float fade = 0.1f) => CameraFx.Stop(h, fade);

        public static void SetStrength(this Handle<ShakeMarker> h, float strength) => CameraFx.SetStrength(h, strength);

        public static bool IsPlaying(this Handle<ShakeMarker> h) => CameraFx.IsPlaying(h);
    }
}
