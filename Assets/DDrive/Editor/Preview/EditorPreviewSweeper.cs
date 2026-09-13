using System;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DDrive.Editor.Preview
{
    // D-Drive の各エディタが開いているシーンに置く確認用プレビュー(名前が "[D-Drive]" で始まり DontSave のルート)の
    // 一括後片付け(2026-09-14、ユーザー指示「エディタを閉じたとき・違うシーンに移動したときにプレビューを持ち越さない」)。
    //
    // DontSave のオブジェクトはシーンを閉じても破棄されず、どのシーンにも属さない「孤児」になって Hierarchy に出ないまま
    // 描画され続ける(2026-09-12 の UI Root 17 個、2026-09-14 の Slider Editor / UI Root の残骸)。エディタごとの後片付けは
    // 閉じ忘れ・ドメインリロードでの参照喪失・テスト実行などで漏れるため、ここで次のタイミングにまとめて片付ける:
    //   - シーンを閉じる直前: そのシーンのプレビューを消す(孤児になる前に)
    //   - シーンを開いた後 / ドメインリロード後 / Play Mode から戻った後: 孤児を消す
    //   - Play Mode に入る直前: 全プレビューを消す
    // 対象は「[D-Drive] で始まる DontSave のルート」だけ。デザイナーが置く本物のオブジェクト([D-Drive] Runtime 等)は
    // DontSave ではないので触らない。Unity 内部の孤児(SceneCamera 等)も名前で除外される。
    [InitializeOnLoad]
    public static class EditorPreviewSweeper
    {
        public const string Prefix = "[D-Drive]";

        static EditorPreviewSweeper()
        {
            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += OnAfterReload;
        }

        public static bool IsPreviewRoot(GameObject go)
            => go != null
               && go.transform.parent == null
               && (go.hideFlags & HideFlags.DontSaveInEditor) != 0
               && go.name.StartsWith(Prefix, StringComparison.Ordinal)
               && !EditorUtility.IsPersistent(go);

        [MenuItem(DDriveMenu.Debug + "確認用プレビューの残骸を掃除")]
        private static void CleanFromMenu()
        {
            var count = DestroyAll();
            Debug.Log($"[DDrive] 確認用プレビューの残骸を {count} 個片付けました。");
        }

        // どのシーンにも属さなくなったプレビュー(孤児)を消す。Play Mode 中は触らない。
        public static int DestroyOrphans()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return 0;
            }

            var count = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (IsPreviewRoot(go) && !go.scene.IsValid())
                {
                    Object.DestroyImmediate(go);
                    count++;
                }
            }

            return count;
        }

        public static int DestroyInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (IsPreviewRoot(root))
                {
                    Object.DestroyImmediate(root);
                    count++;
                }
            }

            return count;
        }

        public static int DestroyAll()
        {
            var count = DestroyOrphans();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                count += DestroyInScene(SceneManager.GetSceneAt(i));
            }

            return count;
        }

        private static void OnSceneClosing(Scene scene, bool removingScene)
        {
            if (!EditorApplication.isPlaying)
            {
                DestroyInScene(scene);
            }
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => DestroyOrphans();

        private static void OnAfterReload() => DestroyOrphans();

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                DestroyAll();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                DestroyOrphans();
            }
        }
    }
}
