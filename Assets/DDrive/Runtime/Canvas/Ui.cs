using System;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Handle;
using UnityEngine;
using CanvasId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Ui.CanvasMarker>;

namespace DDrive.Runtime.Ui
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Models.cs / Prefabs.cs と同じ設計。ADR#3)。
    // Bind 前 / 未 Bind の呼び出しは全て no-op(Invalid Handle / 何もしない)で継続する(例外で止めない)。
    public static class Ui
    {
        private static UiManager _instance;

        public static void Bind(UiManager instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static Handle<CanvasMarker> Open(CanvasId id) => _instance?.Open(id) ?? Handle<CanvasMarker>.Invalid;

        public static Handle<CanvasMarker> Popup(CanvasId id) => _instance?.Popup(id) ?? Handle<CanvasMarker>.Invalid;

        public static UniTask<Handle<CanvasMarker>> OpenAsync(CanvasId id)
            => _instance != null ? _instance.OpenAsync(id) : UniTask.FromResult(Handle<CanvasMarker>.Invalid);

        public static UniTask<Handle<CanvasMarker>> PopupAsync(CanvasId id)
            => _instance != null ? _instance.PopupAsync(id) : UniTask.FromResult(Handle<CanvasMarker>.Invalid);

        public static void Close(Handle<CanvasMarker> h) => _instance?.Close(h);

        public static UniTask CloseAsync(Handle<CanvasMarker> h) => _instance != null ? _instance.CloseAsync(h) : UniTask.CompletedTask;

        public static bool CloseTop() => _instance?.CloseTop() ?? false;

        public static UniTask<bool> CloseTopAsync() => _instance != null ? _instance.CloseTopAsync() : UniTask.FromResult(false);

        public static IDisposable OnSignal(string key, Action<SignalArgs> cb) => _instance?.OnSignal(key, cb) ?? NullDisposable.Instance;

        public static void SendSignal(string key, Handle<CanvasMarker> from, string elementPath = null) => _instance?.SendSignal(key, from, elementPath);

        public static void SetLayerVisible(UiLayer layer, bool visible) => _instance?.SetLayerVisible(layer, visible);

        public static GameObject GetGameObject(Handle<CanvasMarker> h) => _instance?.GetGameObject(h);

        public static bool IsOpen(Handle<CanvasMarker> h) => _instance?.IsOpen(h) ?? false;
    }

    // `h.IsOpen()` / `h.Close()` / `h.Go()` の書き味([07_canvas_prefab.md] A-3 の想定 API)を
    // Handle 型を汚さずに提供する拡張。実体は Ui ファサードへ委譲する。
    public static class CanvasHandleExtensions
    {
        public static bool IsOpen(this Handle<CanvasMarker> h) => Ui.IsOpen(h);

        public static void Close(this Handle<CanvasMarker> h) => Ui.Close(h);

        public static GameObject Go(this Handle<CanvasMarker> h) => Ui.GetGameObject(h);
    }
}
