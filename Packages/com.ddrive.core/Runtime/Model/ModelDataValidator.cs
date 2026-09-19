using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Vfx;
using UnityEngine;

namespace DDrive.Runtime.Model
{
    // [05_model_animation.md] A-4。
    public sealed class ModelDataValidator : IValidator
    {
        public AssetType Target => AssetType.Model;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not ModelData model)
            {
                yield break;
            }

            if (model.Prefab == null)
            {
                yield return ValidationResult.Error("Prefab が未設定(または Missing)です");
                yield break;
            }

            // Avatar Missing は「Humanoid として動かすつもりの Prefab」だけを対象にする。
            // Animator を持たない静的モデルで Avatar 未設定を Error にすると誤検出になるため、
            // Animator が付いていて、かつどちらの Avatar(Data 側/Animator 側)も無い場合のみ判定する。
            var animator = model.Prefab.GetComponentInChildren<Animator>(true);
            if (animator != null && model.Avatar == null && animator.avatar == null)
            {
                yield return ValidationResult.Error("Animator がありますが Avatar が未設定です");
            }

            if (model.Slots != null)
            {
                foreach (var result in ValidateSlots(model))
                {
                    yield return result;
                }
            }

            // [04_vfx.md] 実装メモ / VfxDataValidator と共通のシェーダー×パイプライン検査を再利用する。
            foreach (var result in VfxDataValidator.ValidateShaderPipelineCompatibility(model.Prefab))
            {
                yield return result;
            }
        }

        private static IEnumerable<ValidationResult> ValidateSlots(ModelData model)
        {
            for (var i = 0; i < model.Slots.Length; i++)
            {
                var slot = model.Slots[i];

                if (!ResolvesToRenderer(model.Prefab.transform, slot.RendererPath))
                {
                    yield return ValidationResult.Error($"Slots[{i}] の RendererPath '{slot.RendererPath}' が Prefab 内で見つかりません");
                    continue;
                }

                if (!slot.Material.IsValid)
                {
                    yield return ValidationResult.Warning($"Slots[{i}] に Material が未割当です");
                }
            }
        }

        private static bool ResolvesToRenderer(Transform root, string rendererPath)
        {
            if (string.IsNullOrEmpty(rendererPath))
            {
                return root.GetComponent<Renderer>() != null;
            }

            var target = root.Find(rendererPath);
            return target != null && target.GetComponent<Renderer>() != null;
        }
    }
}
