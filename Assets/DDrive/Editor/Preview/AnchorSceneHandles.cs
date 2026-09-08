using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Preview
{
    // SceneView 上の Anchor 編集ハンドル(移動/回転)と、AnchorData の連鎖をエディタ側で合成する補助。
    // VfxEditorWindow(埋め込み Anchor)と AnchorEditorWindow(AnchorData)が同じ描画・逆変換を使う
    // ([21_anchor_spec.md] §3.6)。式は Runtime の AnchorPose / AnchorChain と同じものを通す。
    public static class AnchorSceneHandles
    {
        public struct Result
        {
            public bool PositionChanged;
            public Vector3 LocalOffset;   // 合成済み(target-local)の新しいオフセット
            public bool RotationChanged;
            public Vector3 LocalEuler;    // 合成済み(target-local)の新しい回転
        }

        // anchor: 表示・編集対象の(合成済み)定義。baseTransform: 解決先(null = ワールド)。extraOffset: AnchorPoint/ランダム分。
        // 戻り値の LocalOffset/LocalEuler は合成済み空間の値。埋め込み Anchor ならそのまま書き戻し、
        // AnchorData の子ノードなら ToChildLocal で親基準に変換してから書き戻す。
        public static Result Draw(in AnchorDef anchor, Transform baseTransform, Vector3 extraOffset, string label, Color color)
        {
            var result = default(Result);
            var worldPos = AnchorPose.WorldPosition(anchor, baseTransform, extraOffset);
            var worldRot = AnchorPose.WorldRotation(anchor, baseTransform, Quaternion.identity);
            var size = HandleUtility.GetHandleSize(worldPos);

            Handles.color = color;
            Handles.DrawWireDisc(worldPos, Vector3.up, size * 0.25f);
            Handles.Label(worldPos + Vector3.up * size * 0.35f, label);

            if (Tools.current == Tool.Rotate)
            {
                EditorGUI.BeginChangeCheck();
                var newRot = Handles.RotationHandle(worldRot, worldPos);
                if (EditorGUI.EndChangeCheck())
                {
                    result.RotationChanged = true;
                    result.LocalEuler = AnchorPose.LocalEulerFromWorld(anchor, baseTransform, newRot);
                }

                return result;
            }

            EditorGUI.BeginChangeCheck();
            var handleRot = Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity;
            var newPos = Handles.PositionHandle(worldPos, handleRot);
            if (EditorGUI.EndChangeCheck())
            {
                result.PositionChanged = true;
                result.LocalOffset = AnchorPose.LocalOffsetFromWorld(baseTransform, newPos, extraOffset);
            }

            return result;
        }

        // 描画権を持たないウィンドウ用: ハンドル無し・薄い円と短いラベルだけ(重なっても読める最小限)。
        public static void DrawInactiveMarker(in AnchorDef anchor, Transform baseTransform, Vector3 extraOffset, string label, Color color)
        {
            var worldPos = AnchorPose.WorldPosition(anchor, baseTransform, extraOffset);
            var size = HandleUtility.GetHandleSize(worldPos);
            Handles.color = new Color(color.r, color.g, color.b, 0.25f);
            Handles.DrawWireDisc(worldPos, Vector3.up, size * 0.18f);
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(color.r, color.g, color.b, 0.5f) } };
            Handles.Label(worldPos + Vector3.up * size * 0.28f, label, style);
        }

        // 連鎖(ルート → 対象)の各段の位置を線で結び、ランダム半径とディレイをラベル表示する。
        public static void DrawChain(IReadOnlyList<AnchorData> rootToTarget, Transform baseTransform, Color color)
        {
            if (rootToTarget == null || rootToTarget.Count == 0)
            {
                return;
            }

            Handles.color = new Color(color.r, color.g, color.b, 0.6f);
            Vector3? previous = null;
            for (var i = 0; i < rootToTarget.Count; i++)
            {
                var def = AnchorChainEditor.ComposeUpTo(rootToTarget, i);
                var pos = AnchorPose.WorldPosition(def, baseTransform, Vector3.zero);
                var size = HandleUtility.GetHandleSize(pos);

                if (previous.HasValue)
                {
                    Handles.DrawDottedLine(previous.Value, pos, 4f);
                }

                if (i < rootToTarget.Count - 1)
                {
                    Handles.DrawWireDisc(pos, Vector3.up, size * 0.12f);
                    Handles.Label(pos + Vector3.up * size * 0.2f, rootToTarget[i].name);
                }

                var node = rootToTarget[i];
                if (node.PositionJitterRadius > 0f)
                {
                    Handles.DrawWireDisc(pos, Vector3.up, node.PositionJitterRadius);
                    Handles.DrawWireDisc(pos, Vector3.right, node.PositionJitterRadius);
                    Handles.DrawWireDisc(pos, Vector3.forward, node.PositionJitterRadius);
                }

                previous = pos;
            }
        }
    }

    // AnchorData の連鎖をアセット参照(AssetDatabase)から集めて合成する。Registry を持たないエディタ UI 用。
    public static class AnchorChainEditor
    {
        // 対象から Parent を辿り、ルート → 対象の順に返す。循環・深さ超過は途中で打ち切る(Validator が別途報告する)。
        public static List<AnchorData> CollectRootToTarget(AnchorData target)
        {
            var leafToRoot = new List<AnchorData>();
            var cursor = target;
            while (cursor != null && leafToRoot.Count < AnchorChain.MaxDepth && !leafToRoot.Contains(cursor))
            {
                leafToRoot.Add(cursor);
                cursor = cursor.Parent.IsValid ? EditorAnchorRegistry.Find(cursor.Parent.Value) : null;
            }

            leafToRoot.Reverse();
            return leafToRoot;
        }

        // ルート → index 番目までを合成した AnchorDef(ランダム無し)。
        public static AnchorDef ComposeUpTo(IReadOnlyList<AnchorData> rootToTarget, int index)
        {
            var count = index + 1;
            var leafToRoot = new AnchorData[count];
            for (var i = 0; i < count; i++)
            {
                leafToRoot[i] = rootToTarget[index - i];
            }

            return AnchorChain.Compose(leafToRoot, count, sampleRandom: false).Def;
        }

        // 合成済み(target-local)のオフセットを、親までの合成姿勢を基準にした「子ノード自身の LocalOffset」へ戻す。
        // parent: ルート → 親 の合成結果(ルート自身なら null を渡す)。
        public static Vector3 ToChildLocalOffset(AnchorDef? parent, Vector3 composedOffset)
        {
            if (!parent.HasValue)
            {
                return composedOffset;
            }

            var p = parent.Value;
            var rot = Quaternion.Euler(p.LocalEuler);
            var scale = p.LocalScale == Vector3.zero ? Vector3.one : p.LocalScale;
            var local = Quaternion.Inverse(rot) * (composedOffset - p.LocalOffset);
            return new Vector3(
                scale.x != 0f ? local.x / scale.x : 0f,
                scale.y != 0f ? local.y / scale.y : 0f,
                scale.z != 0f ? local.z / scale.z : 0f);
        }

        public static Vector3 ToChildLocalEuler(AnchorDef? parent, Vector3 composedEuler)
        {
            if (!parent.HasValue)
            {
                return composedEuler;
            }

            return (Quaternion.Inverse(Quaternion.Euler(parent.Value.LocalEuler)) * Quaternion.Euler(composedEuler)).eulerAngles;
        }
    }
}
