using DDrive.Foundation.Handle;
using UnityEngine;
using PrefabAssetId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Prefab.PrefabMarker>;

namespace DDrive.Runtime.Prefab
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Models.cs / Vfx.cs と同じ設計。ADR#3)。
    // Bind 前 / 未 Bind の呼び出しは全て no-op(Invalid Handle)で継続する(例外で止めない)。
    public static class Prefabs
    {
        private static PrefabsManager _instance;

        public static void Bind(PrefabsManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static Handle<PrefabMarker> Spawn(PrefabAssetId id, Vector3 pos, Quaternion rot)
            => _instance?.Spawn(id, pos, rot) ?? Handle<PrefabMarker>.Invalid;

        public static Handle<PrefabMarker> Spawn(PrefabAssetId id, Transform parent)
            => _instance?.Spawn(id, parent) ?? Handle<PrefabMarker>.Invalid;

        public static void Despawn(Handle<PrefabMarker> h) => _instance?.Despawn(h);

        public static void Preload(params PrefabAssetId[] ids) => _instance?.Preload(ids);

        // Addressables から未解決(lazy)なカタログ登録でも確実に Prewarm したいときに使う([07] 実装メモ 3-11)。
        public static Cysharp.Threading.Tasks.UniTask PreloadAsync(params PrefabAssetId[] ids)
            => _instance != null ? _instance.PreloadAsync(ids) : Cysharp.Threading.Tasks.UniTask.CompletedTask;

        public static GameObject GetGameObject(Handle<PrefabMarker> h) => _instance?.GetGameObject(h);

        public static bool HasTag(Handle<PrefabMarker> h, string tag) => _instance?.HasTag(h, tag) ?? false;

        public static void Move(Handle<PrefabMarker> h, Vector3 pos, Quaternion rot) => _instance?.Move(h, pos, rot);
    }

    // `h.Go` / `h.GetComponent<T>()` / `h.Move(pos, rot)` / `h.HasTag("Destructible")` の書き味
    // ([07_canvas_prefab.md] B-3 の想定 API)を Handle 型を汚さずに提供する拡張。実体は Prefabs ファサードへ委譲する。
    public static class PrefabHandleExtensions
    {
        public static GameObject Go(this Handle<PrefabMarker> h) => Prefabs.GetGameObject(h);

        public static T GetComponent<T>(this Handle<PrefabMarker> h) where T : Component
            => Prefabs.GetGameObject(h) is { } go ? go.GetComponent<T>() : null;

        public static void Move(this Handle<PrefabMarker> h, Vector3 pos, Quaternion rot) => Prefabs.Move(h, pos, rot);

        public static bool HasTag(this Handle<PrefabMarker> h, string tag) => Prefabs.HasTag(h, tag);

        public static void Despawn(this Handle<PrefabMarker> h) => Prefabs.Despawn(h);

        public static bool IsValid(this Handle<PrefabMarker> h) => Prefabs.GetGameObject(h) != null;
    }
}
