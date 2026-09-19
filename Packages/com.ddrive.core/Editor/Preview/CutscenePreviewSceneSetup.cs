using DDrive.Editor.Bootstrap;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Preview
{
    // [26_timeline.md] §4.4/§6(6-10d) — Cutscene の確認用シーン。VfxPreviewSceneSetup / CameraShake
    // PreviewSceneSetup と同じ「無ければ床/ライト/カメラ/Volume を備えた最小構成を生成する」流儀に、
    // Play Mode でのハーネス(CutscenePreviewHarness)+ 起動オブジェクト(DDriveRuntimeBootstrap)を足す。
    //
    // CutsceneManager は Bootstrap 経由(Play Mode の起動時)以外では生成されないため([02] §14)、この
    // シーンで実際に Cutscene を再生・確認するには Play Mode に入る必要がある(Edit Mode でのスクラブは
    // カメラ/SE/VFX の実 Manager 適用までは届かない。CutscenePreviewHarness のコメント参照)。
    public static class CutscenePreviewSceneSetup
    {
        public const string ScenePath = "Assets/GameData/PreviewScenes/CutscenePreviewScene.unity";
        public const string ActorName = "Cutscene Preview Actor";

        [MenuItem(DDriveMenu.Editors + "Cutscene確認用シーンを開く")]
        public static void OpenOrCreate() => TryOpenOrCreate();

        // 戻り値は「確認用シーンが開いている状態になったか」(他の PreviewSceneSetup と同じ流儀)。
        public static bool TryOpenOrCreate()
        {
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

            // 原点の目印(スケール感の基準・キャラ/小物の配置確認用)。
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

            // [26_timeline.md] §4.2.1 — Origin=Self の基準点 + PlayContext.Self。ゲームキャラの代わりの
            // 目印として立方体を出す(Model のスケール感が分かるよう Ground と揃えたサイズ)。
            var actor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            actor.name = ActorName;
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            actor.AddComponent<DDrive.Runtime.Cutscene.CutscenePreviewHarness>();

            // [02_core_framework.md] §14 — Play Mode に入ったときに CutsceneManager 等が組み立てられるよう、
            // 起動オブジェクトを置く(BootstrapSceneSetup と同じ経路。プロジェクト内の全カタログを割り当てる)。
            var bootstrapGo = new GameObject(BootstrapSceneSetup.ObjectName);
            var bootstrap = bootstrapGo.AddComponent<DDrive.Runtime.Loop.DDriveRuntimeBootstrap>();
            BootstrapSceneSetup.RefreshCatalogs(bootstrap);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[DDrive] Cutscene確認用シーンを新規作成しました: {ScenePath}。Play Mode に入り、'{ActorName}' の CutscenePreviewHarness、または対象 CutsceneData の Inspector の「再生(Play Mode)」ボタンで再生を確認してください。Timeline ウィンドウでのスクラブも Play Mode 中なら実 Manager 経由でそのまま機能します。");
        }

        // Bloom/ColorAdjustments 程度の最小構成(他の PreviewSceneSetup と同じたたき台)。
        private static void BuildDefaultVolume(string sceneFolder)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Bloom>(true).threshold.value = 1f;
            profile.Add<ColorAdjustments>(true);

            var profilePath = $"{sceneFolder}/CutscenePreviewVolumeProfile.asset";
            AssetDatabase.CreateAsset(profile, profilePath);

            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = profile;
        }
    }
}
