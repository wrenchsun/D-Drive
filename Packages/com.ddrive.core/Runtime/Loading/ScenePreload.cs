using System;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Registry;
using UnityEngine;

namespace DDrive.Runtime.Loading
{
    // [11_tasks.md] 5-7 — 静的ファサード([02_core_framework.md] §8 の命名規約に合わせ、他種別と同じ形で
    // DDriveRuntimeBootstrap から Bind/Unbind される)。ロード画面(SceneLoadingScreen 等)はこれ経由で
    // Registry.PreloadIdsAsync / ReleaseIds を呼ぶ。IAssetRegistry を直接持ち回らせないための薄い窓口。
    public static class ScenePreload
    {
        private static IAssetRegistry _registry;

        public static bool IsBound => _registry != null;

        public static void Bind(IAssetRegistry registry) => _registry = registry;

        // list が null、または未 Bind(起動配線前 / テスト)なら即完了扱いにする(例外にしない。CLAUDE.md §0-4)。
        public static UniTask RunAsync(ScenePreloadList list, IProgress<float> progress = null)
        {
            if (list == null)
            {
                progress?.Report(1f);
                return UniTask.CompletedTask;
            }

            if (_registry == null)
            {
                Debug.LogWarning("[DDrive] ScenePreload: Registry が Bind されていません(起動配線前、またはテスト)。Preload をスキップします。");
                progress?.Report(1f);
                return UniTask.CompletedTask;
            }

            return _registry.PreloadIdsAsync(list.GetIds(), progress);
        }

        // RunAsync で確保した参照を返す(シーンアンロード時、ロード画面の破棄時等)。
        public static void Release(ScenePreloadList list)
        {
            if (list == null || _registry == null)
            {
                return;
            }

            _registry.ReleaseIds(list.GetIds());
        }
    }
}
