using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // 同一角度位置に既存エントリがあった場合のユーザ選択。
    public enum BlendTreeConflictResolution
    {
        Overwrite,
        Skip,
        Cancel,
    }

    // AnimatorController のステートマシン内にある「ステート名 == state 名」の BlendTree に
    // AnimationClip を角度位置で登録する。存在しなければ 2D Freeform Directional を新規作成する。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.BlendTreeRegistrar(ロジックは同一)。
    // パラメータ名は Anim2DData.ParamXName/ParamYName の既定値("x"/"y")と揃える([05] C-4)。
    public static class BlendTreeRegistrar
    {
        public const string ParamXName = "x";
        public const string ParamYName = "y";

        // AnimationClip を BlendTree に登録する。
        // controller: 登録先。layerIndex: 対象レイヤー。stateName: 命名規則のステート名(BlendTree 名にもなる)。
        // angle: 方向角度(0/45/.../315)。confirmOverwrite: 重複時にユーザ確認を行うデリゲート。
        // 戻り値: 成功(登録/スキップも含む)時 true、キャンセル時 false。
        public static bool Register(
            AnimatorController controller,
            int layerIndex,
            string stateName,
            AnimationClip clip,
            int angle,
            System.Func<int, BlendTreeConflictResolution> confirmOverwrite)
        {
            if (controller == null)
            {
                Debug.LogError("[BlendTreeRegistrar] AnimatorController が null です。");
                return false;
            }

            if (string.IsNullOrEmpty(stateName))
            {
                Debug.LogError("[BlendTreeRegistrar] stateName が空です。");
                return false;
            }

            if (clip == null)
            {
                Debug.LogError("[BlendTreeRegistrar] AnimationClip が null です。");
                return false;
            }

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                Debug.LogError($"[BlendTreeRegistrar] layerIndex が範囲外: {layerIndex}");
                return false;
            }

            if (!DirectionAngle.TryGetPosition(angle, out var position))
            {
                Debug.LogError($"[BlendTreeRegistrar] 角度 {angle} は 0/45/.../315 のいずれかではありません。");
                return false;
            }

            EnsureFloatParameter(controller, ParamXName);
            EnsureFloatParameter(controller, ParamYName);

            var layer = controller.layers[layerIndex];
            var stateMachine = layer.stateMachine;

            var targetState = FindState(stateMachine, stateName);
            BlendTree blendTree;

            if (targetState != null)
            {
                blendTree = targetState.motion as BlendTree;
                if (blendTree == null)
                {
                    Debug.LogError(
                        $"[BlendTreeRegistrar] ステート \"{stateName}\" が存在しますが、Motion が BlendTree ではありません。");
                    return false;
                }

                if (blendTree.blendType != BlendTreeType.FreeformDirectional2D)
                {
                    Debug.LogWarning(
                        $"[BlendTreeRegistrar] BlendTree \"{stateName}\" の BlendType が FreeformDirectional2D ではありません。(現: {blendTree.blendType})");
                }
            }
            else
            {
                blendTree = new BlendTree
                {
                    name = stateName,
                    blendType = BlendTreeType.FreeformDirectional2D,
                    blendParameter = ParamXName,
                    blendParameterY = ParamYName,
                    hideFlags = HideFlags.HideInHierarchy,
                };

                AssetDatabase.AddObjectToAsset(blendTree, controller);

                targetState = stateMachine.AddState(stateName);
                targetState.motion = blendTree;
                targetState.writeDefaultValues = false;
            }

            var children = blendTree.children;
            var existingIndex = -1;
            for (var i = 0; i < children.Length; i++)
            {
                if (Mathf.Approximately(children[i].position.x, position.x) &&
                    Mathf.Approximately(children[i].position.y, position.y))
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                var resolution = confirmOverwrite != null
                    ? confirmOverwrite(angle)
                    : BlendTreeConflictResolution.Skip;

                switch (resolution)
                {
                    case BlendTreeConflictResolution.Overwrite:
                        children[existingIndex].motion = clip;
                        blendTree.children = children;
                        break;
                    case BlendTreeConflictResolution.Skip:
                        Debug.Log($"[BlendTreeRegistrar] 角度 {angle} は既存エントリがあるためスキップしました。");
                        EditorUtility.SetDirty(controller);
                        AssetDatabase.SaveAssets();
                        return true;
                    case BlendTreeConflictResolution.Cancel:
                    default:
                        Debug.Log("[BlendTreeRegistrar] キャンセルされました。");
                        return false;
                }
            }
            else
            {
                blendTree.AddChild(clip, position);
            }

            EditorUtility.SetDirty(blendTree);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static void EnsureFloatParameter(AnimatorController controller, string paramName)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name == paramName)
                {
                    if (p.type != AnimatorControllerParameterType.Float)
                    {
                        Debug.LogWarning(
                            $"[BlendTreeRegistrar] パラメータ \"{paramName}\" は存在しますが Float ではありません。(現: {p.type})");
                    }

                    return;
                }
            }

            controller.AddParameter(paramName, AnimatorControllerParameterType.Float);
        }

        // ステートマシンを再帰的に辿って stateName と一致するステートを検索。
        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            var queue = new Queue<AnimatorStateMachine>();
            queue.Enqueue(stateMachine);
            while (queue.Count > 0)
            {
                var sm = queue.Dequeue();
                foreach (var cs in sm.states)
                {
                    if (cs.state != null && cs.state.name == stateName)
                    {
                        return cs.state;
                    }
                }

                foreach (var csm in sm.stateMachines)
                {
                    if (csm.stateMachine != null)
                    {
                        queue.Enqueue(csm.stateMachine);
                    }
                }
            }

            return null;
        }
    }
}
