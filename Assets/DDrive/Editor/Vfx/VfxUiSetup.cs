using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DDrive.Editor.Vfx
{
    // [04_vfx.md] §4 — UI パーティクルの土台(専用 UI レイヤー + URP Overlay カメラ)を用意する。
    // 明示的にメニューから実行した時だけプロジェクト設定/現在のシーンを変更する(自動実行しない)。
    // VFX Prefab 側の RenderLayer には、ここで確保したレイヤーの index を設定して使う。
    public static class VfxUiSetup
    {
        public const string LayerName = "VfxUI";
        public const string OverlayCameraName = "VFX UI Overlay Camera";

        [MenuItem(DDriveMenu.Generate + "VFX UI レイヤー/カメラを設定")]
        public static void SetupFromMenu()
        {
            var layer = EnsureLayer(LayerName);
            if (layer < 0)
            {
                Debug.LogError("[DDrive] ユーザーレイヤー(8-31)に空きがないため VfxUI レイヤーを確保できませんでした。");
                return;
            }

            var camera = EnsureOverlayCamera(layer);
            Debug.Log($"[DDrive] VfxUI レイヤー(index={layer})とオーバーレイカメラ '{camera.name}' を用意しました。VFX Prefab の RenderLayer に {layer} を設定してください。");
            EditorGUIUtility.PingObject(camera);
        }

        // 既存レイヤーがあればその index、無ければ空きスロットに確保して返す。空きが無ければ -1。
        public static int EnsureLayer(string layerName)
        {
            var tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var tagManager = new SerializedObject(tagManagerAsset);
            var layersProp = tagManager.FindProperty("layers");

            var layers = new string[32];
            for (var i = 0; i < 32 && i < layersProp.arraySize; i++)
            {
                layers[i] = layersProp.GetArrayElementAtIndex(i).stringValue;
            }

            var index = VfxUiLayerAllocator.FindOrClaim(layers, layerName);
            if (index < 0)
            {
                return -1;
            }

            layersProp.GetArrayElementAtIndex(index).stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            return index;
        }

        // 現在開いているシーンに Overlay カメラを 1 つ用意し、Camera.main のスタックに追加する。
        // Camera.main が無いシーンでは、後で手動でスタックに加えられるよう単体カメラとして残す。
        public static Camera EnsureOverlayCamera(int cullingLayer)
        {
            var existing = GameObject.Find(OverlayCameraName);
            var cameraGo = existing != null ? existing : new GameObject(OverlayCameraName);
            var camera = cameraGo.GetComponent<Camera>();
            if (camera == null)
            {
                camera = cameraGo.AddComponent<Camera>();
            }

            camera.clearFlags = CameraClearFlags.Depth;
            camera.cullingMask = 1 << cullingLayer;

            var overlayData = cameraGo.GetComponent<UniversalAdditionalCameraData>();
            if (overlayData == null)
            {
                overlayData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            }

            overlayData.renderType = CameraRenderType.Overlay;

            var baseCamera = Camera.main;
            if (baseCamera != null && baseCamera != camera)
            {
                var baseData = baseCamera.GetComponent<UniversalAdditionalCameraData>();
                if (baseData == null)
                {
                    baseData = baseCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }

                if (!baseData.cameraStack.Contains(camera))
                {
                    baseData.cameraStack.Add(camera);
                }
            }

            return camera;
        }
    }
}
