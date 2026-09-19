using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Material;
using UnityEngine;

namespace DDrive.Runtime.Vfx
{
    // [04_vfx.md] §7。Anchor.BoneName がプレビューモデルに無いかの検査は SeDataValidator と同様、
    // プレビュー実行時の関心事(2-4 VfxEditor 側)であり、静的な Validator の対象外とする。
    public sealed class VfxDataValidator : IValidator
    {
        public AssetType Target => AssetType.Vfx;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not VfxData vfx)
            {
                yield break;
            }

            if (vfx.Prefab == null)
            {
                yield return ValidationResult.Error("Prefab が未設定(または Missing)です");
            }

            if (vfx.LifeMode == VfxLifeMode.Loop &&
                (vfx.Flags.Pool.Kind != PoolPolicyKind.Pooled || vfx.Flags.Pool.MaxCount <= 0))
            {
                yield return ValidationResult.Warning("LifeMode=Loop ですが Pool 上限が未設定です(リーク危険)");
            }

            if (vfx.Render == VfxRenderMode.UIOverlay && vfx.Flags.Domain == AssetDomain.Game3D)
            {
                yield return ValidationResult.Warning("Render=UIOverlay ですが Domain=Game3D です");
            }

            if (vfx.FadeOutSec > 10f)
            {
                yield return ValidationResult.Warning("FadeOutSec が 10 秒を超えています");
            }

            if (vfx.AnchorId.IsValid && DDrive.Runtime.Anchoring.AnchorDataValidator.IsEmbeddedAnchorNonDefault(vfx.Anchor))
            {
                yield return ValidationResult.Warning("AnchorId が設定されているため、埋め込みの Anchor は無視されます");
            }

            if (vfx.Prefab != null)
            {
                if (vfx.Params != null)
                {
                    foreach (var result in ValidateParams(vfx))
                    {
                        yield return result;
                    }
                }

                foreach (var result in ValidateShaderPipelineCompatibility(vfx.Prefab))
                {
                    yield return result;
                }
            }
        }

        // [04_vfx.md] 実装メモ — Built-in RP 用シェーダー(Standard 等)を URP プロジェクトで使うと
        // Scene/Game ビューで描画されない(プレハブのアセットプレビューだけは正しく見えるため発見が
        // 遅れやすい)。SubShader の RenderPipeline タグを見て機械的に検出する。
        // Legacy Shaders/Particles/* のようなライティング無しのシェーダーは URP でも描画されるため
        // (ShaderPipelineAnalyzer のコメント参照)、そちらは Error ではなく置き換え推奨の Warning にする(2026-09-19)。
        internal static IEnumerable<ValidationResult> ValidateShaderPipelineCompatibility(GameObject prefab)
        {
            var reported = new HashSet<Shader>();

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null || mat.shader == null || !reported.Add(mat.shader))
                    {
                        continue;
                    }

                    var compatibility = ShaderPipelineAnalyzer.CheckActivePipeline(mat.shader);
                    if (compatibility == ShaderPipelineCompatibility.Compatible)
                    {
                        continue;
                    }

                    var detected = ShaderPipelineAnalyzer.DisplayName(ShaderPipelineAnalyzer.Detect(mat.shader));
                    var active = ShaderPipelineAnalyzer.ActivePipelineDisplayName();
                    if (compatibility == ShaderPipelineCompatibility.Incompatible)
                    {
                        yield return ValidationResult.Error(
                            $"マテリアル '{mat.name}' のシェーダー '{mat.shader.name}' は {detected} 用ですが、" +
                            $"プロジェクトは {active} です。Scene/Game ビューで描画されません(エディタでは magenta、ビルドでは透明)");
                    }
                    else
                    {
                        yield return ValidationResult.Warning(
                            $"マテリアル '{mat.name}' のシェーダー '{mat.shader.name}' は {detected} 用です。" +
                            $"ライティング無しの単純なパスのため {active} でも描画はされますが、{active} 用シェーダー" +
                            "(Universal Render Pipeline/Particles/Unlit 等。Generate > デフォルトパーティクルマテリアルを生成)への置き換えを推奨します");
                    }
                }
            }
        }

        private static IEnumerable<ValidationResult> ValidateParams(VfxData vfx)
        {
            var renderers = vfx.Prefab.GetComponentsInChildren<Renderer>(true);

            foreach (var param in vfx.Params)
            {
                if (string.IsNullOrEmpty(param.TargetProperty))
                {
                    continue;
                }

                if (!AnyRendererHasProperty(renderers, param.TargetProperty))
                {
                    yield return ValidationResult.Error($"Params '{param.Label}' の TargetProperty '{param.TargetProperty}' が Prefab のマテリアルに存在しません");
                }
            }
        }

        private static bool AnyRendererHasProperty(Renderer[] renderers, string propertyName)
        {
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMaterial == null)
                {
                    continue;
                }

                if (renderer.sharedMaterial.HasProperty(propertyName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
