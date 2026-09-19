using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using UnityEngine;
using MaterialId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Material.MaterialMarker>;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-3 — 静的ファサード。プログラマーは `Mats.Apply(renderer, 0, MATID.PlayerBody)` だけ書く。
    // 未 Bind の呼び出しは no-op(null / Invalid Handle)で継続する(例外で止めない)。
    public static class Mats
    {
        private static MaterialManager _instance;

        public static void Bind(MaterialManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        // 共有インスタンス(Data ごとに 1 つ)。
        public static UnityEngine.Material Get(MaterialId id) => _instance?.Get(id);

        public static void Apply(Renderer r, int slot, MaterialId id) => _instance?.Apply(r, slot, id);

        // シーン内一括差し替え。差し替えた Renderer スロット数を返す。
        public static int Replace(MaterialId from, MaterialId to) => _instance?.Replace(from, to) ?? 0;

        // 溶け替え(スロット 0)。
        public static Handle<MaterialMarker> FadeTo(Renderer r, MaterialId to, float sec)
            => _instance?.FadeTo(r, to, sec) ?? Handle<MaterialMarker>.Invalid;

        public static Handle<MaterialMarker> FadeTo(Renderer r, int slot, MaterialId to, float sec)
            => _instance?.FadeTo(r, slot, to, sec) ?? Handle<MaterialMarker>.Invalid;

        public static void SetGlobalParam(string name, ParamValue v) => _instance?.SetGlobalParam(name, v);

        public static bool IsFading(Handle<MaterialMarker> h) => _instance?.IsFading(h) ?? false;

        public static void Stop(Handle<MaterialMarker> h) => _instance?.Stop(h);
    }

    public static class MaterialHandleExtensions
    {
        public static bool IsFading(this Handle<MaterialMarker> h) => Mats.IsFading(h);

        public static void Stop(this Handle<MaterialMarker> h) => Mats.Stop(h);
    }
}
