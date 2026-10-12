using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Settings
{
    // [07_canvas_prefab.md] 2026-10-12(C-1) — Project Settings > D-Drive > Canvas。
    // UiManager のレイヤールート Canvas の CanvasScaler 既定(Open した Prefab に CanvasScaler が無いときに使う)。
    public static class CanvasSettingsProvider
    {
        public const string SettingsPath = "Project/D-Drive/Canvas";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Canvas",
                guiHandler = _ => DrawGui(),
                keywords = new[] { "D-Drive", "Canvas", "CanvasScaler", "解像度", "全画面" },
            };
        }

        private static void DrawGui()
        {
            EditorGUILayout.HelpBox(
                "Canvas を開くとき、Prefab に CanvasScaler があればその設定がそのまま使われます。" +
                "Prefab に無いときだけ、ここの設定(ScaleWithScreenSize)で画面サイズに合わせて拡大縮小します。" +
                "製品ビルドで変えるときは、起動時に UiCanvasScalerDefaults を設定します。",
                MessageType.Info);

            var settings = DDriveProjectSettings.instance;
            EditorGUI.BeginChangeCheck();
            var resolution = EditorGUILayout.Vector2Field("基準解像度", settings.CanvasScalerReferenceResolution);
            var match = EditorGUILayout.Slider("Match(0 = 幅 / 1 = 高さ)", settings.CanvasScalerMatch, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                resolution.x = Mathf.Max(1f, resolution.x);
                resolution.y = Mathf.Max(1f, resolution.y);
                settings.SetCanvasScalerDefaults(resolution, match);
            }

            if (GUILayout.Button("既定に戻す(1920 x 1080 / 0.5)", GUILayout.Width(220)))
            {
                settings.SetCanvasScalerDefaults(
                    new Vector2(UiCanvasScalerDefaults.FallbackReferenceWidth, UiCanvasScalerDefaults.FallbackReferenceHeight),
                    UiCanvasScalerDefaults.FallbackMatch);
            }
        }
    }
}
