using DDrive.Editor.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Preview
{
    // [04_vfx.md] §5(2026-07-28 改定) — VfxEditor の SceneView プレビューは「実際のライティング/
    // ポストプロセスが乗ったシーン」を開いて使う前提のため、その確認専用シーンを1コマンドで
    // 開ける(無ければ最小構成で自動生成する)ようにする。
    public static class VfxPreviewSceneSetup
    {
        public const string ScenePath = "Assets/GameData/PreviewScenes/VfxPreviewScene.unity";

        [MenuItem(DDriveMenu.Editors + "VFX確認用シーンを開く")]
        public static void OpenOrCreate() => TryOpenOrCreate();

        // 戻り値は「確認用シーンが開いている状態になったか」(U-5 の PreviewPlacement.PrepareScene が
        // 続けて配置してよいかの判断に使う)。CanvasPreviewSceneSetup.TryOpenOrCreate と同じ流儀。
        public static bool TryOpenOrCreate()
        {
            // 既に確認用シーンを開いているなら開き直さない(保存ダイアログ・読み直しが無駄で、
            // 置いてあるプレビューも消えてしまうため)。
            if (SceneManager.GetActiveScene().path == ScenePath)
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

            // 参照用の床(スケール感の基準・VFXの落下/着弾確認用)。
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);

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
            Debug.Log($"[DDrive] VFX確認用シーンを新規作成しました: {ScenePath}。ライト・カメラ・Volume・床を調整してから VFX を確認してください。");
        }

        // Bloom/ColorAdjustments 程度の最小構成。プロジェクトごとの本番ポストプロセスに合わせて
        // 各自調整する前提の「たたき台」であり、これが正解値というわけではない。
        private static void BuildDefaultVolume(string sceneFolder)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Bloom>(true).threshold.value = 1f;
            profile.Add<ColorAdjustments>(true);

            var profilePath = $"{sceneFolder}/VfxPreviewVolumeProfile.asset";
            AssetDatabase.CreateAsset(profile, profilePath);

            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = profile;
        }
    }
}
