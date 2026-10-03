// 外部パッケージ(T-Drive の Toon シェーダー等)を模した最小シェーダー。_Toon* の固有プロパティを持つ。
// 描画内容に意味は無い(MaterialData.Specific が実行時 Material へそのまま書かれることの確認専用)。
Shader "Hidden/ExternalContract/ToonProps"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _BaseMap("Base Map", 2D) = "white" {}
        _ToonFloat("Toon Float", Float) = 0.25
        _ToonInt("Toon Int", Integer) = 3
        _ToonFlag("Toon Flag", Float) = 0
        _ToonColor("Toon Color", Color) = (0.1,0.2,0.3,1)
        _ToonVec("Toon Vector", Vector) = (1,2,3,4)
        _ToonTex("Toon Tex", 2D) = "white" {}
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
            float4 _BaseColor;
            float4 vert(float4 pos : POSITION) : SV_POSITION { return float4(pos.xy * 0.0, 0.0, 1.0); }
            float4 frag() : SV_Target { return _BaseColor; }
            ENDHLSL
        }
    }
}
