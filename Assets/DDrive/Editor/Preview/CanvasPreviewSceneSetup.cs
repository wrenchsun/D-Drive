using DDrive.Editor.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DDrive.Editor.Preview
{
    // [07_canvas_prefab.md] Part A — CanvasEditor の確認は長らく「今開いているシーンで OpenData する」
    // (「確認用シーンで開く」)のみだった。UI は Screen Space - Overlay で描くため 3D のライト/カメラに
    // 依存せず、VFX/Anim のような専用シーンを作る意味が薄いという判断だったが、シーンに既にユーザー
    // 自身の Canvas 等が配置されていると D-Drive のプレビューと見分けが付かず混乱する(2026-09-12
    // ユーザー報告)。VFX 等と同じ「切り替えれば必ずまっさらな専用シーン」も選べるようにする。
    public static class CanvasPreviewSceneSetup
    {
        public const string ScenePath = "Assets/GameData/PreviewScenes/CanvasPreviewScene.unity";

        [MenuItem(DDriveMenu.Editors + "Canvas確認用シーンを開く")]
        public static void OpenOrCreate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                // ユーザーが保存ダイアログでキャンセルした場合は何もしない(現在の作業を失わせない)。
                return;
            }

            if (System.IO.File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }

            CreateScene();
        }

        private static void CreateScene()
        {
            var folder = System.IO.Path.GetDirectoryName(ScenePath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Editor.AssetBrowser.AssetCreationService.EnsureFolder(folder);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            cameraGo.AddComponent<AudioListener>();

            // EventSystem は Unity 標準のメニュー経由で作る(Input System(新)/旧いずれのモジュールが
            // 正しいかは Unity 自身に判断させ、DDrive.Editor から Unity.InputSystem を直接参照しない)。
            EditorApplication.ExecuteMenuItem("GameObject/UI/Event System");

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[DDrive] Canvas確認用シーンを新規作成しました: {ScenePath}。パッド操作の確認に使う EventSystem の入力モジュールを確認してください。");
        }
    }
}
