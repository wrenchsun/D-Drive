using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using UnityEngine;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Runtime.Vfx
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Audio.cs と同じ設計。ADR#3)。
    public static class Vfx
    {
        private static VfxManager _instance;

        public static void Bind(VfxManager instance) => _instance = instance;

        public static Handle<VfxMarker> Spawn(VfxId id) => _instance?.Spawn(id) ?? Handle<VfxMarker>.Invalid;

        public static Handle<VfxMarker> Spawn(VfxId id, Vector3 pos, Quaternion rot)
            => _instance?.Spawn(id, pos, rot) ?? Handle<VfxMarker>.Invalid;

        public static Handle<VfxMarker> Spawn(VfxId id, Transform attach)
            => _instance?.Spawn(id, attach) ?? Handle<VfxMarker>.Invalid;

        public static void Stop(Handle<VfxMarker> h) => _instance?.Stop(h);

        public static void Kill(Handle<VfxMarker> h) => _instance?.Kill(h);

        public static void Preload(params VfxId[] ids) => _instance?.Preload(ids);
    }
}
