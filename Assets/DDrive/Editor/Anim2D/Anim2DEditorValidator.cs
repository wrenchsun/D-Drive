using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // [05_model_animation.md] C-6(2026-09-10、3-13) — Anim2DDataValidator(ランタイム側、3-12)ではできない
    // Editor API 依存の 2 検査。CI.DiscoverValidators がリフレクションで拾う(パラメータ無しコンストラクタが条件)。
    // (a) 方向付きなのに BlendTree の x,y パラメータが無い(Error、FixAction=パラメータ追加)。
    //     StateName を持つ AnimatorController が見つからない場合は Warning に留める(Controller は任意配線のため)。
    // (b) スライス済みスプライトの参照切れ(元テクスチャの再インポート等で Sprite キーフレームが null になったもの)(Error)。
    public sealed class Anim2DEditorValidator : IValidator
    {
        public AssetType Target => AssetType.Anim2D;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not Anim2DData anim)
            {
                yield break;
            }

            if (anim.HasDirections && !string.IsNullOrEmpty(anim.ResolvedStateName))
            {
                var controller = FindControllerWithState(anim.ResolvedStateName);
                if (controller == null)
                {
                    yield return ValidationResult.Warning($"Controller に StateName '{anim.ResolvedStateName}' が見つかりません(BlendTree のパラメータ検査をスキップしました)");
                }
                else
                {
                    var missing = new List<string>();
                    if (!HasFloatParameter(controller, anim.ParamXName))
                    {
                        missing.Add(anim.ParamXName);
                    }

                    if (!HasFloatParameter(controller, anim.ParamYName))
                    {
                        missing.Add(anim.ParamYName);
                    }

                    if (missing.Count > 0)
                    {
                        var capturedController = controller;
                        var capturedAnim = anim;
                        yield return ValidationResult.Error(
                            $"'{controller.name}' の BlendTree に x,y パラメータ {string.Join(", ", missing)} がありません",
                            () => FixAddParameters(capturedController, capturedAnim));
                    }
                }
            }

            foreach (var result in CheckMissingSprites(anim.Clip))
            {
                yield return result;
            }

            if (anim.DirectionClips != null)
            {
                foreach (var clip in anim.DirectionClips)
                {
                    foreach (var result in CheckMissingSprites(clip))
                    {
                        yield return result;
                    }
                }
            }
        }

        // Clip の m_Sprite カーブのキーフレームに null(参照切れ)が無いか調べる。
        private static IEnumerable<ValidationResult> CheckMissingSprites(AnimationClip clip)
        {
            if (clip == null)
            {
                yield break;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.type != typeof(SpriteRenderer) || binding.propertyName != "m_Sprite")
                {
                    continue;
                }

                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var nullCount = 0;
                foreach (var kf in keyframes)
                {
                    if (kf.value == null)
                    {
                        nullCount++;
                    }
                }

                if (nullCount > 0)
                {
                    yield return ValidationResult.Error($"'{clip.name}' のスライス済みスプライトの参照が {nullCount} 件切れています(元テクスチャの再インポート等で消失した可能性があります)");
                }
            }
        }

        // プロジェクト内の AnimatorController から、stateName のステートを持つ最初のものを探す。
        private static AnimatorController FindControllerWithState(string stateName)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController"))
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
                if (controller == null)
                {
                    continue;
                }

                foreach (var layer in controller.layers)
                {
                    if (layer.stateMachine != null && FindState(layer.stateMachine, stateName) != null)
                    {
                        return controller;
                    }
                }
            }

            return null;
        }

        // ステートマシンを再帰的に辿って stateName と一致するステートを検索(BlendTreeRegistrar と同じロジック)。
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

        private static bool HasFloatParameter(AnimatorController controller, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return true; // 空名はこの検査の対象外(Anim2DDataValidator 側で別に Error になる)
            }

            foreach (var p in controller.parameters)
            {
                if (p.name == name && p.type == AnimatorControllerParameterType.Float)
                {
                    return true;
                }
            }

            return false;
        }

        private static void FixAddParameters(AnimatorController controller, Anim2DData anim)
        {
            if (!HasFloatParameter(controller, anim.ParamXName))
            {
                controller.AddParameter(anim.ParamXName, AnimatorControllerParameterType.Float);
            }

            if (!HasFloatParameter(controller, anim.ParamYName))
            {
                controller.AddParameter(anim.ParamYName, AnimatorControllerParameterType.Float);
            }

            EditorUtility.SetDirty(controller);
        }
    }
}
