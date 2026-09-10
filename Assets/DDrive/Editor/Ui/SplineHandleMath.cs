using UnityEngine;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-6 — UiTweenEditorWindow のスプライン制御点ハンドル用の座標変換(チケット 4-10)。
    // SplinePathDef.Points は対象 RectTransform のローカル座標なので、SceneView のワールドハンドルに出すには
    // 対象の Transform(position/rotation/scale)を通す必要がある。テストしやすいよう純関数として切り出す。
    public static class SplineHandleMath
    {
        public static Vector3 LocalToWorld(Transform target, Vector3 local) => target.TransformPoint(local);

        public static Vector3 WorldToLocal(Transform target, Vector3 world) => target.InverseTransformPoint(world);
    }
}
