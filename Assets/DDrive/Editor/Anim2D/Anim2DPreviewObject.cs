using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // [05_model_animation.md] C-5/C-6 実装メモ(2026-09-10、3-13) — 2D 確認用プレビュー物(SpriteRenderer + Animator、
    // DontSave)の生成/破棄を Anim2DEditorWindow.Edit と AnimEditorWindow(Anim2DData を開いたときのフォールバック)
    // で共有する。プレビューはウィンドウ内描画ではなく開いているシーン / プレハブモードに配置し、SceneAnimPreviewDriver で
    // 実 AnimManager から動かして SceneView で確認する(2026-09-10 決定、全エディタ共通)。
    public static class Anim2DPreviewObject
    {
        public const string Name = "[D-Drive] Anim2D Preview";

        // 開いているシーン / プレハブモードに既にあるプレビュー物(同名)を返す。無ければ null。
        // Anim2D Editor と Anim Editor が別々に作って前のものが残らないよう、両方がこれで 1 つを共有する(2026-09-11)。
        public static GameObject FindExisting()
        {
            var stage = StageUtility.GetCurrentStage();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.name != Name || EditorUtility.IsPersistent(go) || !go.scene.IsValid())
                {
                    continue;
                }

                if (stage == null || StageUtility.GetStage(go) == stage)
                {
                    return go;
                }
            }

            return null;
        }

        // 既存があればそれを新しい Data 用に作り直して返し、無ければ新規に作る。対象を切り替えたときに前の Data の
        // スプライト / Animator 状態が残らないよう、再利用時も先頭 Sprite を当て直し Animator を素の状態に戻す。
        public static GameObject FindOrCreate(Anim2DData data)
        {
            var existing = FindExisting();
            if (existing == null)
            {
                return Create(data);
            }

            var animator = existing.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                animator.Rebind();
            }

            ApplyFirstSprite(existing, data);
            return existing;
        }

        // DontSave の SpriteRenderer + Animator を開いているシーン / プレハブモードに配置し、Clip の先頭 Sprite キーを割り当てる。
        // 同名のプレビュー物が既にあればそれを使う(FindOrCreate と同じ。二重配置を防ぐ)。
        public static GameObject Create(Anim2DData data)
        {
            var existing = FindExisting();
            if (existing != null)
            {
                return FindOrCreate(data);
            }

            var go = new GameObject(Name) { hideFlags = HideFlags.DontSave };
            StageUtility.PlaceGameObjectInCurrentStage(go);
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<Animator>();
            if (SceneView.lastActiveSceneView != null)
            {
                go.transform.position = SceneView.lastActiveSceneView.pivot;
            }

            ApplyFirstSprite(go, data);
            return go;
        }

        // Clip の先頭 Sprite キーを SpriteRenderer に反映する(未設定・読み込めなければ何もしない、例外にしない)。
        public static void ApplyFirstSprite(GameObject go, Anim2DData data)
        {
            if (go == null || data == null || data.Clip == null)
            {
                return;
            }

            if (!AnimationClipEditorUtility.LoadSprites(data.Clip, out var sprites, out _) || sprites == null || sprites.Length == 0)
            {
                return;
            }

            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.sprite = sprites[0];
            }
        }

        public static void Destroy(GameObject go)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
