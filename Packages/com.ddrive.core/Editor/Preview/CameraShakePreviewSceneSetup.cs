using DDrive.Editor.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DDrive.Editor.Preview
{
    // [16_camera_haptics.md] §C-2(5-2c) — ShakeEditor / HapticsEditor の確認用シーン。
    // VfxPreviewSceneSetup と同じ流儀(無ければ床/ライト/カメラ/Volume を備えた最小構成を生成)。
    // Shake は「Camera.main を実際に揺らして SceneView / Game ビューで見る」方式なので、専用の
    // 確認用シーンを用意しておくと、本番のカメラ演出やポストプロセスと混ざらずに調整できる。
    public static class CameraShakePreviewSceneSetup
    {
        public const string ScenePath = "Assets/GameData/PreviewScenes/CameraShakePreviewScene.unity";

        [MenuItem(DDriveMenu.Editors + "揺れ・振動確認用シーンを開く")]
        public static void OpenOrCreate() => TryOpenOrCreate();

        // 戻り値は「確認用シーンが開いている状態になったか」(U-5、VfxPreviewSceneSetup と同じ流儀)。
        public static bool TryOpenOrCreate()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == ScenePath)
            {
                return true;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                // ユーザーが保存ダイアログでキャンセルした場合は何もしない(現在の作業を失わせない)。
                return false;
            }

            if (System.IO.File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return true;
            }

            CreateScene();
            return true;
        }

        private static void CreateScene()
        {
            var folder = System.IO.Path.GetDirectoryName(ScenePath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Editor.AssetBrowser.AssetCreationService.EnsureFolder(folder);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 揺れの大小・方向が分かりやすいよう、目印になる立方体をいくつか置く(スケール感の基準)。
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);

            for (var i = 0; i < 3; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = $"Marker_{i}";
                marker.transform.position = new Vector3((i - 1) * 2f, 0.5f, 4f);
            }

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -4f), Quaternion.Euler(15f, 0f, 0f));
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<UniversalAdditionalCameraData>();

            BuildDefaultVolume(folder);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[DDrive] 揺れ・振動確認用シーンを新規作成しました: {ScenePath}。カメラの揺れは Shake Editor の再生ボタンで確認してください。");
        }

        // Bloom/ColorAdjustments 程度の最小構成(VfxPreviewSceneSetup と同じたたき台)。
        private static void BuildDefaultVolume(string sceneFolder)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Bloom>(true).threshold.value = 1f;
            profile.Add<ColorAdjustments>(true);

            var profilePath = $"{sceneFolder}/CameraShakePreviewVolumeProfile.asset";
            AssetDatabase.CreateAsset(profile, profilePath);

            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = profile;
        }
    }
}
