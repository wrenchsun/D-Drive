// FC-R-06 の実描画確認用シェーダー(Editor テスト UntaggedPassRenderTests 専用)。
// LightMode タグの無いパス(赤。URP では SRPDefaultUnlit 扱い)と、LightMode=UniversalForward のパス(緑)を持つ。
Shader "Hidden/DDriveTests/UntaggedRender"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Untagged"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; };
            struct V { float4 p : SV_POSITION; };
            V vert(A a) { V v; v.p = TransformObjectToHClip(a.p.xyz); return v; }
            half4 frag(V v) : SV_Target { return half4(1, 0, 0, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "Tagged"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; };
            struct V { float4 p : SV_POSITION; };
            V vert(A a) { V v; v.p = TransformObjectToHClip(a.p.xyz); return v; }
            half4 frag(V v) : SV_Target { return half4(0, 1, 0, 1); }
            ENDHLSL
        }
    }
}
