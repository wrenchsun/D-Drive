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

    // 現在アクティブなレンダーパイプラインでそのシェーダーがどう描かれるか。
    public enum ShaderPipelineCompatibility
    {
        Compatible,           // パイプライン用のシェーダー(または判定対象外)
        RendersButNotNative,  // 別パイプライン用だが描画はされる(下記コメント参照)。置き換え推奨
        Incompatible,         // 描画されない(エディタでは magenta、ビルドでは透明)
    }

    // シェーダーの SubShader タグ("RenderPipeline")を解析し、現在アクティブなレンダーパイプラインと
    // 互換性があるか判定する。Built-in RP 用シェーダー(Standard 等)を URP プロジェクトで使うと
    // Scene/Game ビューで描画されない、という落とし穴([04_vfx.md] 実装メモ参照)を Validator で
    // 機械的に検出するために作った。
    //
    // 判定は Unity 公式の SubShader タグ("RenderPipeline"="UniversalPipeline"/"HDRenderPipeline")を
    // 読むため、シェーダー名の文字列一致より確実(URP/HDRP のシェーダーは必ずこのタグを持つ)。
    //
    // 2026-09-19 追補: RenderPipeline タグの無い(=Built-in 用の)シェーダーでも、URP/HDRP は
    // 「LightMode タグの無いパス」と "SRPDefaultUnlit" のパスを通常どおり描画する(URP の DrawObjectsPass が
    // これらを拾う)。Legacy Shaders/Particles/* や Unlit/* のようなライティング無しの単純なシェーダーは
    // このため URP でも普通に見える。描画されないのは ForwardBase/Deferred 等 Built-in のライティング用
    // LightMode しか持たないもの(Standard・サーフェスシェーダー)で、URP はこれらを magenta のエラー
    // シェーダーで描く(ビルドでは描かない)。以前は前者も「完全に透明になる」として Error にしていたが
    // 実際には見えていた(ユーザー報告)ため、CheckActivePipeline で両者を区別する。
    public static class ShaderPipelineAnalyzer
    {
        private static readonly ShaderTagId RenderPipelineTag = new("RenderPipeline");
        private static readonly ShaderTagId LightModeTag = new("LightMode");
        private const string SrpDefaultUnlit = "SRPDefaultUnlit";

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

        // 現在アクティブなパイプライン(GraphicsSettings.currentRenderPipeline)用のシェーダーか。
        // RendersButNotNative(描画はされるが別パイプライン用)も false を返す点は従来どおり。
        // 描画されるかどうかまで含めて知りたい場合は CheckActivePipeline を使う。
        // 未知のカスタム SRP がアクティブな場合は誤検出を避けるため許容(true)扱いにする。
        public static bool IsCompatibleWithActivePipeline(Shader shader)
            => CheckActivePipeline(shader) == ShaderPipelineCompatibility.Compatible;

        public static ShaderPipelineCompatibility CheckActivePipeline(Shader shader)
        {
            var kind = Detect(shader);
            if (kind == ShaderPipelineKind.Unknown)
            {
                return ShaderPipelineCompatibility.Compatible; // shader 自体が無い(Missing Material)のは別の検査(Prefab/Missing)の担当
            }

            var active = GraphicsSettings.currentRenderPipeline;

            if (active == null)
            {
                // Built-in がアクティブ: URP/HDRP 用シェーダーは Built-in では描画されない。
                return kind == ShaderPipelineKind.BuiltIn ? ShaderPipelineCompatibility.Compatible : ShaderPipelineCompatibility.Incompatible;
            }

            var activeTypeName = active.GetType().Name;
            ShaderPipelineKind expected;
            if (activeTypeName.Contains("Universal"))
            {
                expected = ShaderPipelineKind.Universal;
            }
            else if (activeTypeName.Contains("HDRenderPipeline") || activeTypeName.Contains("HighDefinition"))
            {
                expected = ShaderPipelineKind.HighDefinition;
            }
            else
            {
                return ShaderPipelineCompatibility.Compatible;
            }

            if (kind == expected)
            {
                return ShaderPipelineCompatibility.Compatible;
            }

            // 別パイプライン用。LightMode 無し / SRPDefaultUnlit のパスがあれば SRP はそれを描く。
            return HasSrpDefaultUnlitPass(shader) ? ShaderPipelineCompatibility.RendersButNotNative : ShaderPipelineCompatibility.Incompatible;
        }

        // いずれかの SubShader に「LightMode タグが無い」か "SRPDefaultUnlit" のパスがあるか。
        // URP/HDRP はこのパスを通常のオブジェクト描画で拾うため、Built-in 用シェーダーでも描画される。
        public static bool HasSrpDefaultUnlitPass(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            for (var s = 0; s < shader.subshaderCount; s++)
            {
                var passCount = shader.GetPassCountInSubshader(s);
                for (var p = 0; p < passCount; p++)
                {
                    var lightMode = shader.FindPassTagValue(s, p, LightModeTag).name;
                    if (string.IsNullOrEmpty(lightMode) || lightMode == SrpDefaultUnlit)
                    {
                        return true;
                    }
                }
            }

            return false;
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
