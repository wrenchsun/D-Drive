using DDrive.Foundation.Handle;
using DDrive.Runtime.Material;
using UnityEngine;
using ModelId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Model.ModelMarker>;

namespace DDrive.Runtime.Model
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Audio.cs / Vfx.cs と同じ設計。ADR#3)。
    public static class Models
    {
        private static ModelsManager _instance;

        public static void Bind(ModelsManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static Handle<ModelMarker> Spawn(ModelId id, Vector3 pos, Quaternion rot)
            => _instance?.Spawn(id, pos, rot) ?? Handle<ModelMarker>.Invalid;

        public static Handle<ModelMarker> Spawn(ModelId id, Transform parent)
            => _instance?.Spawn(id, parent) ?? Handle<ModelMarker>.Invalid;

        public static void Despawn(Handle<ModelMarker> h) => _instance?.Despawn(h);

        public static void SetMaterial(Handle<ModelMarker> h, int slotIndex, DDrive.Foundation.Identity.AssetId<MaterialMarker> materialId)
            => _instance?.SetMaterial(h, slotIndex, materialId);

        public static void SetLayer(Handle<ModelMarker> h, int layer) => _instance?.SetLayer(h, layer);

        public static GameObject GetGameObject(Handle<ModelMarker> h) => _instance?.GetGameObject(h);

        // [05] A-3 — AnimManager へ委譲(ModelsManager に AnimManager が接続されていないときは no-op)。
        public static Handle<DDrive.Runtime.Anim.AnimMarker> PlayAnim(Handle<ModelMarker> h, DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim.AnimMarker> animId)
            => _instance?.PlayAnim(h, animId) ?? Handle<DDrive.Runtime.Anim.AnimMarker>.Invalid;

        public static Handle<DDrive.Runtime.Anim.AnimMarker> PlayAnim(Handle<ModelMarker> h, DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim.AnimMarker> animId, float fade)
            => _instance?.PlayAnim(h, animId, fade) ?? Handle<DDrive.Runtime.Anim.AnimMarker>.Invalid;
    }
}
