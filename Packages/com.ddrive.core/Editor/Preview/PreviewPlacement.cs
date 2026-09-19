using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DDrive.Editor.Preview
{
    // 「確認用シーンに配置」ボタンの配置先([09_editor_tools.md] §2.1、U-5、2026-09-17)。
    //  - CheckScene             : 左クリック。確認用シーンを開いてから、従来どおり一時配置(DontSave)する
    //  - CurrentScene           : 右クリック。シーンを切り替えず、今開いているシーン / プレハブステージに一時配置する
    //  - CurrentScenePersistent : 右クリック。今開いているシーンに「本配置」する(DontSave を外してシーンに保存される)
    public enum PreviewPlaceMode
    {
        CheckScene = 0,
        CurrentScene = 1,
        CurrentScenePersistent = 2,
    }

    // 各専用エディタの「確認用シーンに配置 / 確認用シーンを開く」が共有する配置ヘルパー(U-5、2026-09-17)。
    // 同じコードを各エディタにコピーしないため、次の 3 つだけをここに集約する:
    //   1. 確認用シーンを開くかどうかの判断(PrepareScene)
    //   2. 「本配置」(一時オブジェクト扱いを外してシーンの住人にする。Persist / PlacePrefabPersistent)
    //   3. 配置後の SceneView フォーカス(Focus)
    // 例外で止めない([CLAUDE.md] §0-4): 失敗は Debug.LogWarning + no-op で返す。
    public static class PreviewPlacement
    {
        public const string PersistUndoName = "D-Drive: このシーンに本配置";

        private static readonly Vector3[] RectCorners = new Vector3[4];

        public static bool IsPersistent(PreviewPlaceMode mode) => mode == PreviewPlaceMode.CurrentScenePersistent;

        // 一時配置(DontSave)でよいモードか。本配置のときだけ false。
        public static bool IsTemporary(PreviewPlaceMode mode) => mode != PreviewPlaceMode.CurrentScenePersistent;

        // 配置前にシーンを整える。CheckScene のときだけ openCheckScene を呼ぶ。
        // 戻り値 false = 配置を中止する(ユーザーが保存ダイアログでキャンセルした等)。
        public static bool PrepareScene(PreviewPlaceMode mode, Func<bool> openCheckScene)
        {
            if (mode != PreviewPlaceMode.CheckScene || openCheckScene == null)
            {
                return true;
            }

            try
            {
                return openCheckScene();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] 確認用シーンを開けませんでした({e.GetType().Name}: {e.Message})。配置を中止します。");
                return false;
            }
        }

        // SceneView のカメラを配置したオブジェクトに向ける(配置場所がカメラから遠いと見えないため。U-5)。
        // SceneView が無い場合は警告のみで落ちない。
        public static void Focus(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                Debug.LogWarning($"[DDrive] SceneView が開いていないため '{go.name}' へのフォーカスを省略しました(配置自体は済んでいます)。");
                return;
            }

            view.Frame(CalculateBounds(go), instant: false);
            view.Repaint();
        }

        // 既に置いてある一時プレビュー(DontSave)を「本配置」に昇格する。
        // プレビュールート配下の子だった場合はルートから外して、ルートごと撤去されないようにする。
        // Manager / Pool が追跡しているインスタンスには使わない(後で Despawn されてしまう)。
        // そちらは PlacePrefabPersistent で Prefab から直接置くこと。
        public static GameObject Persist(GameObject go, string displayName = null)
        {
            if (go == null)
            {
                Debug.LogWarning("[DDrive] 本配置するオブジェクトがありません(配置に失敗している可能性があります)。");
                return null;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DDrive] Play Mode 中は本配置できません(シーンに保存されないため)。");
                return null;
            }

            var parent = go.transform.parent;
            if (parent != null && EditorPreviewSweeper.IsPreviewRoot(parent.root.gameObject))
            {
                // DontSave のプレビュールートごと消されないよう、ルート直下に出す(ワールド位置は維持)。
                go.transform.SetParent(null, true);
                StageUtility.PlaceGameObjectInCurrentStage(go);
            }

            ClearDontSaveRecursive(go);
            go.name = BuildPersistentName(go.name, displayName);
            return Finish(go);
        }

        // Manager / Pool が追跡しているプレビュー実体は昇格させられないため、本配置では Prefab から
        // 直接インスタンス化する(Prefab リンク付き = デザイナーが後から編集できる)。
        // 「プレビューは実 Manager を駆動する」(ADR-4)は確認のための再生経路の話で、本配置は確認ではなく
        // シーンの作り込みなので、ここでは通常の Prefab 配置と同じ経路にする。
        public static GameObject PlacePrefabPersistent(GameObject prefab, Vector3 position, Quaternion rotation, string displayName = null)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[DDrive] 本配置する Prefab が未設定です。");
                return null;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DDrive] Play Mode 中は本配置できません(シーンに保存されないため)。");
                return null;
            }

            var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (go == null)
            {
                Debug.LogWarning($"[DDrive] Prefab '{prefab.name}' を本配置できませんでした。");
                return null;
            }

            StageUtility.PlaceGameObjectInCurrentStage(go);
            go.transform.SetPositionAndRotation(position, rotation);
            ClearDontSaveRecursive(go);
            if (!string.IsNullOrEmpty(displayName))
            {
                go.name = displayName;
            }

            return Finish(go);
        }

        // Undo 登録 / シーンを dirty に / 選択 + フォーカス(本配置の共通仕上げ)。
        private static GameObject Finish(GameObject go)
        {
            Undo.RegisterCreatedObjectUndo(go, PersistUndoName);
            if (go.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            Selection.activeGameObject = go;
            Focus(go);
            Debug.Log($"[DDrive] '{go.name}' をこのシーンに本配置しました(シーンを保存すると残ります。Ctrl+Z で取り消せます)。");
            return go;
        }

        // DontSave を外す。HideFlags は子に引き継がれないため、子も含めて全部外す
        // (EditorPreviewRoots.MarkDontSaveRecursive の逆操作)。
        private static void ClearDontSaveRecursive(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null)
                {
                    t.gameObject.hideFlags = HideFlags.None;
                }
            }
        }

        // 本配置したものが EditorPreviewSweeper(= "[D-Drive]" で始まる DontSave のルートを掃除する)に
        // 拾われないよう、接頭辞を必ず外す。プレビュー用の汎用名だったときだけ、どのアセットか分かるよう補う。
        private static string BuildPersistentName(string currentName, string displayName)
        {
            var name = currentName ?? string.Empty;
            if (!name.StartsWith(EditorPreviewSweeper.Prefix, StringComparison.Ordinal))
            {
                return string.IsNullOrEmpty(name) ? "D-Drive Object" : name;
            }

            name = name.Substring(EditorPreviewSweeper.Prefix.Length).TrimStart();
            if (string.IsNullOrEmpty(name))
            {
                name = "D-Drive Object";
            }

            return string.IsNullOrEmpty(displayName) ? name : $"{name} ({displayName})";
        }

        // Renderer と RectTransform の両方から包含 AABB を作る(UI のプレビューには Renderer が無いため)。
        private static Bounds CalculateBounds(GameObject go)
        {
            var has = false;
            var bounds = new Bounds(go.transform.position, Vector3.zero);

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            foreach (var rect in go.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == null)
                {
                    continue;
                }

                rect.GetWorldCorners(RectCorners);
                for (var i = 0; i < RectCorners.Length; i++)
                {
                    if (!has)
                    {
                        bounds = new Bounds(RectCorners[i], Vector3.zero);
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(RectCorners[i]);
                    }
                }
            }

            if (!has)
            {
                bounds = new Bounds(go.transform.position, Vector3.one);
            }

            // 大きさ 0 のままだと Frame が寄りすぎて何も見えないので、最低限の広がりを与える。
            var size = bounds.size;
            bounds.size = new Vector3(Mathf.Max(size.x, 0.5f), Mathf.Max(size.y, 0.5f), Mathf.Max(size.z, 0.5f));
            return bounds;
        }
    }
}
