using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DDrive.Editor.Preview
{
    // 開いているシーンに置く確認用プレビュー(DontSave の仮オブジェクト)の生成と後片付け(2026-09-14)。
    // エディタごとに固有の名前を使うこと(以前は Button Skin / Slider Skin / UI Tween が同じ名前を共有し、
    // 片方の「撤去」がもう片方のプレビューまで消していた)。ウィンドウを閉じた・ドメインリロードで参照を
    // 失った等で残った「残骸」は、同じ名前で DestroyAll すれば確実に消える。
    //
    // (レビュー対応 2026-09-14) HideFlags は子に引き継がれない(UiManager.cs の同種コメント参照)。ルートだけ
    // DontSave にして子を HideFlags.None で作ると、プレビューを置いたままシーンを保存したとき子だけが親無しの
    // オブジェクトとして書き出される。子は CreateChild で作るか、組み立て後に MarkDontSaveRecursive を掛けること。
    public static class EditorPreviewRoots
    {
        public static GameObject CreateRoot(string name, params System.Type[] components)
            => new(name, components) { hideFlags = HideFlags.DontSave };

        // (レビュー対応 2026-09-14) Screen Space Overlay の Canvas ルート(Button Skin / Slider Skin / Slider Editor で共通)。
        public static GameObject CreateOverlayCanvas(string name)
        {
            var go = CreateRoot(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return go;
        }

        // (レビュー対応 2026-09-14) プレビュー配下の子を DontSave で作る。
        public static GameObject CreateChild(Transform parent, string name, params System.Type[] components)
        {
            var go = new GameObject(name, components) { hideFlags = HideFlags.DontSave };
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go;
        }

        // (レビュー対応 2026-09-14) root 以下(非アクティブ含む)の全 GameObject に DontSave を付ける。
        // 実行時コンポーネント(UiInteractable 等)が後から足した子にも効かせるため、組み立て後に呼ぶ。
        public static void MarkDontSaveRecursive(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null)
                {
                    t.gameObject.hideFlags |= HideFlags.DontSave;
                }
            }
        }

        // (レビュー対応 2026-09-14) 読み込み中のシーンから、この名前のプレビュー用ルートを探す(無ければ null)。
        // GameObject.Find と違い非アクティブも見つかり、同名の本物のオブジェクト(DontSave でない)は拾わない。
        public static GameObject Find(string name)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (IsNamedPreviewRoot(root, name))
                    {
                        return root;
                    }
                }
            }

            return null;
        }

        // 読み込み中の全シーンから、この名前のルートを全て消す(非アクティブも含む)。消した数を返す。
        // (レビュー対応 2026-09-14) 名前だけで照合していたため、デザイナーが同じ名前を付けた本物のオブジェクトまで
        // 消えうる。EditorPreviewSweeper.IsPreviewRoot([D-Drive] で始まる DontSave のルート)も満たすものだけにする。
        public static int DestroyAll(string name)
        {
            var count = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (IsNamedPreviewRoot(root, name))
                    {
                        Object.DestroyImmediate(root);
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool IsNamedPreviewRoot(GameObject root, string name)
            => root != null && root.name == name && EditorPreviewSweeper.IsPreviewRoot(root);
    }
}
