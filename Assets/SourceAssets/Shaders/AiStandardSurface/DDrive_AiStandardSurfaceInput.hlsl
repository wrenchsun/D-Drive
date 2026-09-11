// D-Drive: aiStandardSurface(Arnold Standard Surface)対応シェーダーの入力(2026-09-11)。
// URP 17.3 の Shaders/LitInput.hlsl を土台に、Arnold のパラメータを UnityPerMaterial に追加したもの。
// URP 側のパス hlsl(ShadowCaster / DepthOnly / DepthNormals / GBuffer / Meta)がそのまま使えるよう、
// LitInput.hlsl と同じ関数名(InitializeStandardLitSurfaceData 等)を提供する。
//
// リアルタイム近似の対応表(docs/06 A 実装メモ参照):
//   base × baseColor            → albedo = _BaseMap * _BaseColor * _BaseWeight
//   metalness                   → _Metallic × _MetalnessMap.r
//   specular / specularColor    → 誘電体 F0 = ((IOR-1)/(IOR+1))^2 × _SpecularWeight × _SpecularColor(× map)。金属は albedo
//   specularRoughness           → smoothness = _Smoothness × (1 - _SpecularRoughnessMap.r)(Common の Smoothness = 1 - roughness)
//   coat / coatRoughness        → URP ClearCoat(mask = _CoatWeight、smoothness = 1 - _CoatRoughness。coatColor は反射に乗算)
//   sheen                       → ForwardLit で Charlie 風の追加項(GBuffer/Deferred では出ない)
//   emission × emissionColor    → _EmissionColor(前処理で weight を乗算済み)
//   opacity / transmission      → alpha = base.a × _Opacity × _OpacityMap.r × (1 - _TransmissionWeight)
//   subsurface / diffuseRoughness / specularAnisotropy / thinWalled → 値を保持(描画には未反映)。thinWalled は前処理で _Cull=Off
#ifndef DDRIVE_AI_STANDARD_SURFACE_INPUT_INCLUDED
#define DDRIVE_AI_STANDARD_SURFACE_INPUT_INCLUDED

// 常に specular ワークフロー(F0 を自前で計算)+ ClearCoat 有効。
#define _SPECULAR_SETUP 1
#define _CLEARCOAT 1

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

// NOTE: SRP Batcher のため #ifdef で増減させない。
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseMap_TexelSize;
float4 _DetailAlbedoMap_ST;
half4 _BaseColor;
half4 _SpecColor;
half4 _EmissionColor;
half _Cutoff;
half _Smoothness;
half _Metallic;
half _BumpScale;
half _Parallax;
half _OcclusionStrength;
half _ClearCoatMask;
half _ClearCoatSmoothness;
half _DetailAlbedoMapScale;
half _DetailNormalMapScale;
half _Surface;
// ── Arnold Standard Surface ──
half _BaseWeight;
half _DiffuseRoughness;
half _SpecularWeight;
half4 _SpecularColor;
half _SpecularIOR;
half _SpecularAnisotropy;
half _CoatWeight;
half4 _CoatColor;
half _CoatRoughness;
half _CoatIOR;
half _SheenWeight;
half4 _SheenColor;
half _SheenRoughness;
half _Opacity;
half _TransmissionWeight;
half _SubsurfaceWeight;
half4 _SubsurfaceColor;
half _ThinWalled;
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

TEXTURE2D(_ParallaxMap);        SAMPLER(sampler_ParallaxMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DetailMask);         SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailAlbedoMap);    SAMPLER(sampler_DetailAlbedoMap);
TEXTURE2D(_DetailNormalMap);    SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);
TEXTURE2D(_ClearCoatMap);       SAMPLER(sampler_ClearCoatMap);
// ── Arnold のテクスチャ入力(キーワード無しで常にサンプルする。既定値: Metalness=white / Roughness=black / SpecColor=white / Opacity=white) ──
TEXTURE2D(_MetalnessMap);          SAMPLER(sampler_MetalnessMap);
TEXTURE2D(_SpecularRoughnessMap);  SAMPLER(sampler_SpecularRoughnessMap);
TEXTURE2D(_SpecularColorMap);      SAMPLER(sampler_SpecularColorMap);
TEXTURE2D(_OpacityMap);            SAMPLER(sampler_OpacityMap);

half SampleOcclusion(float2 uv)
{
    #ifdef _OCCLUSIONMAP
        half occ = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
        return LerpWhiteTo(occ, _OcclusionStrength);
    #else
        return half(1.0);
    #endif
}

void ApplyPerPixelDisplacement(half3 viewDirTS, inout float2 uv)
{
#if defined(_PARALLAXMAP)
    uv += ParallaxMapping(TEXTURE2D_ARGS(_ParallaxMap, sampler_ParallaxMap), viewDirTS, _Parallax, uv);
#endif
}

half3 ApplyDetailNormal(float2 detailUv, half3 normalTS, half detailMask)
{
    return normalTS; // Detail は未対応(Arnold に相当パラメータが無い)
}

// 誘電体の垂直入射反射率。IOR 1.5 で 0.04。
half DielectricF0(half ior)
{
    half r = (ior - 1.0h) / (ior + 1.0h);
    return r * r;
}

inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));

    // opacity / transmission
    half opacity = _Opacity * SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, uv).r * (1.0h - _TransmissionWeight);
    outSurfaceData.alpha = Alpha(albedoAlpha.a * opacity, _BaseColor, _Cutoff);

    half3 baseColor = albedoAlpha.rgb * _BaseColor.rgb * _BaseWeight;

    // metalness
    half metallic = _Metallic * SAMPLE_TEXTURE2D(_MetalnessMap, sampler_MetalnessMap, uv).r;
    #ifdef _METALLICSPECGLOSSMAP
    metallic *= SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv).r; // Common の Mask(R=Metallic)
    #endif

    // roughness → smoothness
    half smoothness = _Smoothness;
    #ifdef _METALLICSPECGLOSSMAP
    smoothness *= SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv).a; // Common の Mask(A=Smoothness)
    #endif
    smoothness *= 1.0h - SAMPLE_TEXTURE2D(_SpecularRoughnessMap, sampler_SpecularRoughnessMap, uv).r;

    // specular F0(specular ワークフロー)。誘電体 = IOR × weight × color、金属 = baseColor
    half3 specColor = _SpecularColor.rgb * SAMPLE_TEXTURE2D(_SpecularColorMap, sampler_SpecularColorMap, uv).rgb;
    half3 dielectricF0 = DielectricF0(_SpecularIOR).xxx * _SpecularWeight * specColor;
    half3 f0 = lerp(dielectricF0, baseColor, metallic);

    outSurfaceData.albedo = baseColor * (1.0h - metallic);
    outSurfaceData.albedo = AlphaModulate(outSurfaceData.albedo, outSurfaceData.alpha);
    outSurfaceData.metallic = half(1.0);
    outSurfaceData.specular = f0;
    outSurfaceData.smoothness = smoothness;
    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    outSurfaceData.occlusion = SampleOcclusion(uv);
    outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));

    // coat(URP ClearCoat。色付きコートは反射に乗算する近似)
    outSurfaceData.clearCoatMask = _CoatWeight * max(max(_CoatColor.r, _CoatColor.g), _CoatColor.b);
    outSurfaceData.clearCoatSmoothness = 1.0h - _CoatRoughness;
}

#endif // DDRIVE_AI_STANDARD_SURFACE_INPUT_INCLUDED
