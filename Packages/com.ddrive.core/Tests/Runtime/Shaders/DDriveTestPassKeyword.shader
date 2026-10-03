// FC-11 のテスト用最小シェーダー。LightMode が UniversalForward / ShadowCaster / DepthOnly の 3 パスと、
// LightMode タグを書いていない 1 パス(Outline。URP では SRPDefaultUnlit 扱い。FC-R-06)と、
// ローカルキーワード _DDRIVE_TEST_FEATURE(+ _NORMALMAP)を持つ。描画内容に意味は無い(Material の状態確認専用)。
Shader "Hidden/DDriveTests/PassKeyword"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _BaseMap("Base Map", 2D) = "white" {}
        _BumpMap("Normal Map", 2D) = "bump" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _DDRIVE_TEST_FEATURE
            #pragma shader_feature_local _NORMALMAP
            float4 _BaseColor;
            float4 vert(float4 pos : POSITION) : SV_POSITION { return float4(pos.xy * 0.0, 0.0, 1.0); }
            float4 frag() : SV_Target { return _BaseColor; }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 pos : POSITION) : SV_POSITION { return float4(pos.xy * 0.0, 0.0, 1.0); }
            float4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // LightMode タグが無いパス(T-Drive の輪郭線パスと同じ書き方。MaterialShaderInfo は SRPDefaultUnlit として数える)。
        Pass
        {
            Name "Outline"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 pos : POSITION) : SV_POSITION { return float4(pos.xy * 0.0, 0.0, 1.0); }
            float4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 pos : POSITION) : SV_POSITION { return float4(pos.xy * 0.0, 0.0, 1.0); }
            float4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
