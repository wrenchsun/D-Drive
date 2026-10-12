using DDrive.Runtime.Ui;
using UnityEditor;

namespace DDrive.Editor.Settings
{
    // [07_canvas_prefab.md] 2026-10-12(C-1) — Project Settings の CanvasScaler 既定を、ランタイムの
    // `UiCanvasScalerDefaults` へ Editor 起動(ドメインリロード)ごとに反映する。
    [InitializeOnLoad]
    internal static class CanvasScalerDefaultsSync
    {
        static CanvasScalerDefaultsSync() => Apply();

        internal static void Apply()
        {
            var settings = DDriveProjectSettings.instance;
            UiCanvasScalerDefaults.ReferenceResolution = settings.CanvasScalerReferenceResolution;
            UiCanvasScalerDefaults.Match = settings.CanvasScalerMatch;
        }
    }
}
