using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using UnityEngine;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Runtime.Vfx
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Audio.cs と同じ設計。ADR#3)。
    // Bind 前 / 未 Bind の呼び出しは全て no-op(Invalid Handle)で継続する(例外で止めない)。
    public static class Vfx
    {
        private static VfxManager _instance;

        public static void Bind(VfxManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        // ── Spawn / Stop ──

        public static Handle<VfxMarker> Spawn(VfxId id) => _instance?.Spawn(id) ?? Handle<VfxMarker>.Invalid;

        public static Handle<VfxMarker> Spawn(VfxId id, Vector3 pos, Quaternion rot)
            => _instance?.Spawn(id, pos, rot) ?? Handle<VfxMarker>.Invalid;

        public static Handle<VfxMarker> Spawn(VfxId id, Transform attach)
            => _instance?.Spawn(id, attach) ?? Handle<VfxMarker>.Invalid;

        public static void Stop(Handle<VfxMarker> h) => _instance?.Stop(h);

        public static void Kill(Handle<VfxMarker> h) => _instance?.Kill(h);

        public static void Preload(params VfxId[] ids) => _instance?.Preload(ids);

        // ── Handle 操作([04_vfx.md] §3。2026-09-08 追加: 以前は VfxManager 実体を掴まないと呼べなかった) ──

        public static bool IsPlaying(Handle<VfxMarker> h) => _instance?.IsPlaying(h) ?? false;

        public static void Move(Handle<VfxMarker> h, Vector3 position) => _instance?.Move(h, position);

        public static void Attach(Handle<VfxMarker> h, Transform target) => _instance?.Attach(h, target);

        public static void Detach(Handle<VfxMarker> h) => _instance?.Detach(h);

        public static void SetSpeed(Handle<VfxMarker> h, float speed) => _instance?.SetSpeed(h, speed);

        public static void SetParam(Handle<VfxMarker> h, string label, ParamValue value) => _instance?.SetParam(h, label, value);

        public static void SetParam(Handle<VfxMarker> h, string label, float value) => SetParam(h, label, ParamValue.Of(value));

        public static void SetParam(Handle<VfxMarker> h, string label, Color value) => SetParam(h, label, ParamValue.Of(value));
    }

    // `h.Move(pos)` / `h.SetParam("MainColor", color)` の書き味([04_vfx.md] §3 の想定 API)を
    // Handle 型を汚さずに提供する拡張。実体は Vfx ファサードへ委譲する。
    public static class VfxHandleExtensions
    {
        public static bool IsPlaying(this Handle<VfxMarker> h) => Vfx.IsPlaying(h);

        public static void Stop(this Handle<VfxMarker> h) => Vfx.Stop(h);

        public static void Kill(this Handle<VfxMarker> h) => Vfx.Kill(h);

        public static void Move(this Handle<VfxMarker> h, Vector3 position) => Vfx.Move(h, position);

        public static void Attach(this Handle<VfxMarker> h, Transform target) => Vfx.Attach(h, target);

        public static void Detach(this Handle<VfxMarker> h) => Vfx.Detach(h);

        public static void SetSpeed(this Handle<VfxMarker> h, float speed) => Vfx.SetSpeed(h, speed);

        public static void SetParam(this Handle<VfxMarker> h, string label, ParamValue value) => Vfx.SetParam(h, label, value);

        public static void SetParam(this Handle<VfxMarker> h, string label, float value) => Vfx.SetParam(h, label, value);

        public static void SetParam(this Handle<VfxMarker> h, string label, Color value) => Vfx.SetParam(h, label, value);
    }
}
