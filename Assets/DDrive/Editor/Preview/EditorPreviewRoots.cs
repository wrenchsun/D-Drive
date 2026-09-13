using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Preview
{
    // 開いているシーンに置く確認用プレビュー(DontSave の仮オブジェクト)の生成と後片付け(2026-09-14)。
    // エディタごとに固有の名前を使うこと(以前は Button Skin / Slider Skin / UI Tween が同じ名前を共有し、
    // 片方の「撤去」がもう片方のプレビューまで消していた)。ウィンドウを閉じた・ドメインリロードで参照を
    // 失った等で残った「残骸」は、同じ名前で DestroyAll すれば確実に消える。
    public static class EditorPreviewRoots
    {
        public static GameObject CreateRoot(string name, params System.Type[] components)
            => new(name, components) { hideFlags = HideFlags.DontSave };

        // 読み込み中の全シーンから、この名前のルートを全て消す(非アクティブも含む)。消した数を返す。
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
                    if (root != null && root.name == name)
                    {
                        Object.DestroyImmediate(root);
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
