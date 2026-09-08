using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Runtime.Material
{
    public enum ShaderPipelineKind
    {
        Unknown,        // shader が null
        BuiltIn,        // どのサブシェーダーにも RenderPipeline タグが無い(Built-in RP 専用)
        Universal,
        HighDefinition,
        Custom,         // URP/HDRP 以外の RenderPipeline タグを持つ(サードパーティ SRP 等)
    }

    // シェーダーの SubShader タグ("RenderPipeline")を解析し、現在アクティブなレンダーパイプラインと
    // 互換性があるか判定する。Built-in RP 用シェーダー(Particles/Alpha Blended 等)を URP プロジェクトで
    // 使うと Scene/Game ビューで完全に透明になる、という落とし穴([04_vfx.md] 実装メモ参照)を
    // Validator で機械的に検出するために作った。
    //
    // 判定は Unity 公式の SubShader タグ("RenderPipeline"="UniversalPipeline"/"HDRenderPipeline")を
    // 読むため、シェーダー名の文字列一致より確実(URP/HDRP のシェーダーは必ずこのタグを持つ)。
    public static class ShaderPipelineAnalyzer
    {
        private static readonly ShaderTagId RenderPipelineTag = new("RenderPipeline");

        public static ShaderPipelineKind Detect(Shader shader)
        {
            if (shader == null)
            {
                return ShaderPipelineKind.Unknown;
            }

            var sawAnyTag = false;

            for (var i = 0; i < shader.subshaderCount; i++)
            {
                var value = shader.FindSubshaderTagValue(i, RenderPipelineTag);
                if (value.name == "UniversalPipeline")
                {
                    return ShaderPipelineKind.Universal;
                }

                if (value.name == "HDRenderPipeline")
                {
                    return ShaderPipelineKind.HighDefinition;
                }

                if (!string.IsNullOrEmpty(value.name))
                {
                    sawAnyTag = true;
                }
            }

            return sawAnyTag ? ShaderPipelineKind.Custom : ShaderPipelineKind.BuiltIn;
        }

        // 現在アクティブなパイプライン(GraphicsSettings.currentRenderPipeline)と噛み合うか。
        // 未知のカスタム SRP がアクティブな場合は誤検出を避けるため許容(true)扱いにする。
        public static bool IsCompatibleWithActivePipeline(Shader shader)
        {
            var kind = Detect(shader);
            if (kind == ShaderPipelineKind.Unknown)
            {
                return true; // shader 自体が無い(Missing Material)のは別の検査(Prefab/Missing)の担当
            }

            var active = GraphicsSettings.currentRenderPipeline;

            if (active == null)
            {
                return kind == ShaderPipelineKind.BuiltIn;
            }

            var activeTypeName = active.GetType().Name;
            if (activeTypeName.Contains("Universal"))
            {
                return kind == ShaderPipelineKind.Universal;
            }

            if (activeTypeName.Contains("HDRenderPipeline") || activeTypeName.Contains("HighDefinition"))
            {
                return kind == ShaderPipelineKind.HighDefinition;
            }

            return true;
        }

        public static string DisplayName(ShaderPipelineKind kind) => kind switch
        {
            ShaderPipelineKind.BuiltIn => "Built-in Render Pipeline",
            ShaderPipelineKind.Universal => "URP",
            ShaderPipelineKind.HighDefinition => "HDRP",
            ShaderPipelineKind.Custom => "別のカスタム SRP",
            _ => "不明",
        };

        public static string ActivePipelineDisplayName()
        {
            var active = GraphicsSettings.currentRenderPipeline;
            if (active == null)
            {
                return "Built-in Render Pipeline";
            }

            var name = active.GetType().Name;
            if (name.Contains("Universal"))
            {
                return "URP";
            }

            if (name.Contains("HDRenderPipeline") || name.Contains("HighDefinition"))
            {
                return "HDRP";
            }

            return name;
        }
    }
}
