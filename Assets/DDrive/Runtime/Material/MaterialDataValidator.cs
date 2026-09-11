using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-4 — MaterialData の静的検査(3-5)。
    // 「Normal がノーマルマップ設定でない」は TextureImporter が要るため Editor 側(3-8 の TextureData Validator / FixAction)に任せる。
    public sealed class MaterialDataValidator : IValidator
    {
        public AssetType Target => AssetType.Material;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not MaterialData mat)
            {
                yield break;
            }

            if (mat.Shader == null)
            {
                yield return ValidationResult.Warning("Shader が未設定です(既定の Lit で生成されます。意図した見た目か確認してください)");
            }
            else if (!ShaderPipelineAnalyzer.IsCompatibleWithActivePipeline(mat.Shader))
            {
                var detected = ShaderPipelineAnalyzer.DisplayName(ShaderPipelineAnalyzer.Detect(mat.Shader));
                var active = ShaderPipelineAnalyzer.ActivePipelineDisplayName();
                yield return ValidationResult.Error($"シェーダー '{mat.Shader.name}' は {detected} 用ですが、プロジェクトは {active} です。正しく描画されません");
            }

            if (!mat.Common.Albedo.IsValid)
            {
                yield return ValidationResult.Warning("Common.Albedo(ベースカラー)が未設定です");
            }

            if (mat.Common.Blend == BlendType.Transparent && mat.RenderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent - 500)
            {
                yield return ValidationResult.Warning($"Transparent なのに RenderQueue({mat.RenderQueue})が Opaque 帯です(RenderQueueOffset を見直してください)");
            }

            if (mat.Common.Blend != BlendType.Transparent && mat.RenderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
            {
                yield return ValidationResult.Warning($"不透明なのに RenderQueue({mat.RenderQueue})が Transparent 帯です");
            }

            if (mat.Common.EmissionIntensity > 0f && !mat.Common.Emission.IsValid && mat.Common.EmissionColor.maxColorComponent <= 0f)
            {
                yield return ValidationResult.Warning("EmissionIntensity > 0 ですが Emission テクスチャも EmissionColor も無いため発光しません");
            }

            if (mat.Specific != null)
            {
                foreach (var param in mat.Specific)
                {
                    if (string.IsNullOrEmpty(param.Property))
                    {
                        yield return ValidationResult.Warning("Specific にプロパティ名が空の項目があります");
                    }
                    else if (mat.Shader != null && mat.Shader.FindPropertyIndex(param.Property) < 0)
                    {
                        yield return ValidationResult.Warning($"Specific '{param.Property}' はシェーダー '{mat.Shader.name}' にありません(無視されます)");
                    }
                    else if (MaterialSpecificResolver.IsConflicting(mat.Shader, param.Property))
                    {
                        // 2026-09-11 レビュー対応: Manager は Common → Specific の順に流し込むので、共通チャンネル名・描画ステート名が
                        // Specific にあると Common の値を黙って上書きする。FixAction(項目の削除)は Editor の
                        // MaterialSpecificSync.RemoveConflicts が担当する(Runtime から UnityEditor を参照しないため)。
                        var classified = MaterialCommonNaming.Classify(param.Property);
                        var kind = classified == MaterialCommonNaming.Kind.Specific
                            ? "シェーダー内部用([HideInInspector] 等)"
                            : MaterialCommonNaming.DisplayName(classified);
                        yield return ValidationResult.Warning(
                            $"Specific '{param.Property}' は{kind}名です。Common の値を上書きします" +
                            "(Material Editor の「共通チャンネルの重複を削除」で取り除けます)");
                    }
                }
            }

            // 2026-09-11: シェーダーの固有プロパティ(MaterialCommonNaming の規約で固有と判定されるもの)が Specific に無い場合は
            // シェーダー既定値のまま使われる。Material Editor の「シェーダーから固有を同期」で既定値付きで登録できる(Info)。
            if (mat.Shader != null)
            {
                var unregistered = MaterialSpecificResolver.FindUnregistered(mat.Specific, mat.Shader);
                if (unregistered.Count > 0)
                {
                    yield return ValidationResult.Info(
                        $"シェーダー '{mat.Shader.name}' の固有プロパティが Specific に未登録です(既定値で描画): {string.Join(", ", unregistered)}。" +
                        "Material Editor の「シェーダーから固有を同期」で登録できます");
                }
            }

            if (mat.Anims != null)
            {
                foreach (var anim in mat.Anims)
                {
                    if (string.IsNullOrEmpty(anim.Property))
                    {
                        yield return ValidationResult.Warning("Anims にプロパティ名が空の項目があります");
                        continue;
                    }

                    if (anim.Value.Mode == ValueMode.Constant)
                    {
                        yield return ValidationResult.Warning($"Anims '{anim.Property}' の Value が Constant です(動きません)");
                    }
                    else if (anim.Value.Duration <= 0f)
                    {
                        yield return ValidationResult.Warning($"Anims '{anim.Property}' の Time(Duration / Rate)が 0 です(動きません)");
                    }
                }
            }
        }
    }
}
