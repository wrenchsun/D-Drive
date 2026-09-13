using DDrive.Editor.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

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

        // (レビュー対応 2026-09-14) [MenuItem] のメソッドが bool を返していた(VfxPreviewSceneSetup 等は void)。
        // メニュー用の void ラッパーと、戻り値付きの TryOpenOrCreate に分ける。
        [MenuItem(DDriveMenu.Editors + "Canvas確認用シーンを開く")]
        public static void OpenOrCreate() => TryOpenOrCreate();

        // 戻り値は「実際に切り替わったか」(CanvasEditorWindow が続けて OpenData してよいかの判断に使う)。
        internal static bool TryOpenOrCreate()
        {
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

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            cameraGo.AddComponent<AudioListener>();

            AddEventSystem();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[DDrive] Canvas確認用シーンを新規作成しました: {ScenePath}。");
        }

        // Codex レビュー対応(2026-09-12): 当初は EditorApplication.ExecuteMenuItem("GameObject/UI/Event System")
        // で Unity 標準の入力モジュール判定に任せていたが、この呼び出しは(フォーカスや選択状態次第で)
        // 何も作らずに黙って失敗することがあり、実際に EventSystem が入らないシーンができてしまった。
        // DDrive.Editor から Unity.InputSystem を直接参照する(asmdef 変更)のは避けたいので、リフレクションで
        // InputSystemUIInputModule を探して付ける。見つからない場合だけ警告して手動対応を促す
        // (activeInputHandler=Input System 専用のプロジェクトでは StandaloneInputModule は動かないため
        // フォールバックにしない)。
        internal static void AddEventSystem()
        {
            var esGo = new GameObject("EventSystem", typeof(EventSystem));
            var moduleType = FindType("UnityEngine.InputSystem.UI.InputSystemUIInputModule");
            if (moduleType != null)
            {
                esGo.AddComponent(moduleType);
            }
            else
            {
                Debug.LogWarning("[DDrive] InputSystemUIInputModule が見つかりませんでした(Unity.InputSystem 未導入?)。EventSystem に入力モジュールを手動で追加してください。");
            }
        }

        private static System.Type FindType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
