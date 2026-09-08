using UnityEditor;

namespace DDrive.Editor.Preview
{
    // SceneView への描画権の調停。VFX Editor / Anchor Editor など複数のウィンドウが同時に開いていると
    // ギズモ・ハンドル・ラベルが重なって読めなくなるため、「最後にフォーカスしたウィンドウ」だけが
    // ハンドルと詳細を描き、それ以外は薄い目印だけを描く([04] §5 / [21] §3.6、2026-09-08)。
    // 各ウィンドウは OnFocus で Claim、OnDisable で Release し、OnSceneGui で IsOwner を見る。
    public static class SceneGuiOwner
    {
        private static EditorWindow _owner;

        public static EditorWindow Current => _owner;

        public static void Claim(EditorWindow window)
        {
            if (window == null || _owner == window)
            {
                return;
            }

            _owner = window;
            SceneView.RepaintAll();
        }

        public static void Release(EditorWindow window)
        {
            if (_owner == window)
            {
                _owner = null;
                SceneView.RepaintAll();
            }
        }

        // 誰も持っていなければ最初に問い合わせたウィンドウが持つ(常に 1 つは描画される)。
        public static bool IsOwner(EditorWindow window)
        {
            if (_owner == null && window != null)
            {
                _owner = window;
            }

            return _owner == window;
        }

        // ウィンドウ内の案内文用。
        public static string DescribeFor(EditorWindow window)
        {
            if (_owner == null || _owner == window)
            {
                return "SceneView: このウィンドウが描画中(他の D-Drive ウィンドウをクリックすると切り替わります)";
            }

            return $"SceneView: '{_owner.titleContent.text}' が描画中。このウィンドウは薄い目印のみ(クリックで切り替え)";
        }
    }
}
