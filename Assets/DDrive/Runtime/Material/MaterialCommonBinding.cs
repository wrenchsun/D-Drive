using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-2 — MaterialCommon(共通データ)を実際のシェーダープロパティへ流し込む規約(2026-09-10 確定、3-5)。
    //
    // シェーダーごとのプロパティ名は候補を順に HasProperty で探す(URP Lit / Simple Lit / Unlit / HDRP Lit / Built-in Standard を
    // 追加登録なしで扱う)。プロジェクト固有シェーダーの固有パラメータは MaterialData.Specific、シェーダー間の固有パラメータ
    // 変換は ShaderConversionTable(3-6)の責務で、ここは共通チャンネルだけを担当する。
    //
    // 規約(チャンネル → プロパティ候補):
    //   Albedo         : _BaseMap → _MainTex             / Tint  : _BaseColor → _Color
    //   Normal         : _BumpMap                        / Scale : _BumpScale(_NormalScale)
    //   Mask(RGBA規約) : _MetallicGlossMap + _OcclusionMap(URP Lit: R=Metallic, A=Smoothness, G=Occlusion)、_MaskMap(HDRP)
    //                    定数: _Metallic / _Smoothness(_Glossiness)
    //   Emission       : _EmissionMap / _EmissionColor(× Intensity)+ keyword _EMISSION
    //   Blend          : URP は _Surface/_Blend/_SrcBlend/_DstBlend/_ZWrite/_AlphaClip/_Cutoff + keyword、Built-in は _Mode 相当を直接設定
    //   DoubleSided    : _Cull(0=Off / 2=Back)
    public static class MaterialCommonBinding
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");
        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScale = Shader.PropertyToID("_BumpScale");
        private static readonly int NormalScale = Shader.PropertyToID("_NormalScale");
        private static readonly int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int OcclusionMap = Shader.PropertyToID("_OcclusionMap");
        private static readonly int MaskMap = Shader.PropertyToID("_MaskMap");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Glossiness = Shader.PropertyToID("_Glossiness");
        private static readonly int SmoothnessTextureChannel = Shader.PropertyToID("_SmoothnessTextureChannel");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int Surface = Shader.PropertyToID("_Surface");
        private static readonly int Blend = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
        private static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        private static readonly int Cull = Shader.PropertyToID("_Cull");
        private static readonly int Mode = Shader.PropertyToID("_Mode");

        // テクスチャ ID の解決は呼び出し側(Manager: Registry 経由 / エディタ: AssetDatabase 経由)が渡す。
        public delegate Texture TextureResolver(DDrive.Foundation.Identity.AssetId<TextureMarker> id);

        public static void Apply(UnityEngine.Material material, in MaterialCommon common, TextureResolver resolveTexture)
        {
            if (material == null)
            {
                return;
            }

            // Albedo
            var albedo = resolveTexture?.Invoke(common.Albedo);
            SetTextureFirst(material, albedo, BaseMap, MainTex);
            SetColorFirst(material, common.AlbedoTint, BaseColor, ColorProp);

            // Normal
            var normal = resolveTexture?.Invoke(common.Normal);
            SetTextureFirst(material, normal, BumpMap);
            SetFloatFirst(material, common.NormalScale, BumpScale, NormalScale);
            SetKeyword(material, "_NORMALMAP", normal != null);

            // Mask(R=Metallic G=Occlusion B=Detail A=Smoothness)
            var mask = resolveTexture?.Invoke(common.Mask);
            if (material.HasProperty(MaskMap))
            {
                material.SetTexture(MaskMap, mask);
                SetKeyword(material, "_MASKMAP", mask != null);
            }
            else
            {
                SetTextureFirst(material, mask, MetallicGlossMap);
                SetTextureFirst(material, mask, OcclusionMap);
                SetKeyword(material, "_METALLICSPECGLOSSMAP", mask != null);
                SetKeyword(material, "_OCCLUSIONMAP", mask != null);
                if (material.HasProperty(SmoothnessTextureChannel))
                {
                    material.SetFloat(SmoothnessTextureChannel, 0f); // 0 = Metallic Alpha
                }
            }

            SetFloatFirst(material, common.Metallic, Metallic);
            SetFloatFirst(material, common.Smoothness, Smoothness, Glossiness);

            // Emission
            var emission = resolveTexture?.Invoke(common.Emission);
            var emissionOn = common.EmissionIntensity > 0f && (emission != null || common.EmissionColor.maxColorComponent > 0f);
            SetTextureFirst(material, emission, EmissionMap);
            SetColorFirst(material, emissionOn ? common.EmissionColor * common.EmissionIntensity : Color.black, EmissionColor);
            SetKeyword(material, "_EMISSION", emissionOn);
            material.globalIlluminationFlags = emissionOn
                ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            // Blend
            ApplyBlend(material, common.Blend, common.Cutoff);

            // DoubleSided
            if (material.HasProperty(Cull))
            {
                material.SetFloat(Cull, common.DoubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            }

            material.doubleSidedGI = common.DoubleSided;
        }

        // Blend モードのブレンドステート。URP の ShaderGUI(BaseShaderGUI.SetupMaterialBlendMode)がやることを
        // ランタイムでも再現する(GUI 無しで生成するため)。
        public static void ApplyBlend(UnityEngine.Material material, BlendType blend, float cutoff)
        {
            var cutout = blend == BlendType.Cutout;
            var transparent = blend == BlendType.Transparent;

            if (material.HasProperty(Surface))
            {
                material.SetFloat(Surface, transparent ? 1f : 0f);
            }

            if (material.HasProperty(Blend))
            {
                material.SetFloat(Blend, 0f); // Alpha
            }

            if (material.HasProperty(SrcBlend) && material.HasProperty(DstBlend))
            {
                material.SetFloat(SrcBlend, transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
                material.SetFloat(DstBlend, transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            }

            if (material.HasProperty(ZWrite))
            {
                material.SetFloat(ZWrite, transparent ? 0f : 1f);
            }

            if (material.HasProperty(AlphaClip))
            {
                material.SetFloat(AlphaClip, cutout ? 1f : 0f);
            }

            if (material.HasProperty(Cutoff))
            {
                material.SetFloat(Cutoff, cutoff);
            }

            if (material.HasProperty(Mode))
            {
                // Built-in Standard: 0=Opaque 1=Cutout 3=Transparent
                material.SetFloat(Mode, transparent ? 3f : cutout ? 1f : 0f);
            }

            SetKeyword(material, "_ALPHATEST_ON", cutout);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
            SetKeyword(material, "_ALPHABLEND_ON", transparent && material.HasProperty(Mode));
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
        }

        private static void SetTextureFirst(UnityEngine.Material material, Texture texture, int a, int b = 0)
        {
            if (material.HasProperty(a))
            {
                material.SetTexture(a, texture);
            }
            else if (b != 0 && material.HasProperty(b))
            {
                material.SetTexture(b, texture);
            }
        }

        private static void SetColorFirst(UnityEngine.Material material, Color color, int a, int b = 0)
        {
            if (material.HasProperty(a))
            {
                material.SetColor(a, color);
            }
            else if (b != 0 && material.HasProperty(b))
            {
                material.SetColor(b, color);
            }
        }

        private static void SetFloatFirst(UnityEngine.Material material, float value, int a, int b = 0)
        {
            if (material.HasProperty(a))
            {
                material.SetFloat(a, value);
            }
            else if (b != 0 && material.HasProperty(b))
            {
                material.SetFloat(b, value);
            }
        }

        private static void SetKeyword(UnityEngine.Material material, string keyword, bool on)
        {
            if (on)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }
    }
}
