using DDrive.Runtime.Material;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2 — Maya の aiStandardSurface(FBX の MaterialDescription)を DDrive/AiStandardSurface へ写す純関数(2026-09-11)。
    // MaterialDescription はテストで作れないため、値の取り出しを IArnoldSource に切り出している(AiStandardSurfacePreprocessor が適合させる)。
    // Maya が FBX に書く属性名(camelCase): base / baseColor / diffuseRoughness / metalness / specular / specularColor / specularRoughness /
    // specularIOR / specularAnisotropy / coat / coatColor / coatRoughness / coatIOR / sheen / sheenColor / sheenRoughness /
    // emission / emissionColor / opacity / transmission / subsurface / subsurfaceColor / thinWalled / normalCamera。
    // テクスチャが繋がっている属性はテクスチャとして取れる(色・スカラーの方は取れない)。
    public interface IArnoldSource
    {
        bool TryGetFloat(string name, out float value);
        bool TryGetColor(string name, out Vector4 value);
        bool TryGetTexture(string name, out Texture texture, out Vector2 offset, out Vector2 scale);
    }

    public static class AiStandardSurfaceMapper
    {
        public const string ShaderName = "DDrive/AiStandardSurface";
        public const float MayaArnoldTypeId = 1138001f; // Unity(URP)の FBXArnoldSurfaceMaterialDescriptionPreprocessor と同じ判定

        public static bool IsMayaArnoldStandardSurface(IArnoldSource src)
            => src.TryGetFloat("TypeId", out var typeId) && Mathf.Approximately(typeId, MayaArnoldTypeId);

        // material のシェーダーを差し替えて全パラメータを写す。shader が無ければ false(何もしない)。
        public static bool Apply(IArnoldSource src, UnityEngine.Material material, Shader shader)
        {
            if (src == null || material == null || shader == null)
            {
                return false;
            }

            material.shader = shader;

            // ── Base ──
            var baseWeight = GetFloat(src, "base", 1f);
            material.SetFloat("_BaseWeight", baseWeight);
            if (SetTexture(src, material, "baseColor", "_BaseMap"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            else
            {
                // Arnold 既定 0.8。Color の乗算はアルファも掛かる(a=1.6 になり Alpha() でクリップされなくなる)ので rgb だけ書く。
                material.SetColor("_BaseColor", GetColor(src, "baseColor", new Color(0.8f, 0.8f, 0.8f, 1f)));
            }

            material.SetFloat("_DiffuseRoughness", GetFloat(src, "diffuseRoughness", 0f));

            // ── Metalness ──
            // テクスチャの有無はキーワードで伝える(シェーダー側は #ifdef でサンプルを省く。_NORMALMAP / _EMISSION と同じ扱い)。
            var hasMetalnessMap = SetTexture(src, material, "metalness", "_MetalnessMap");
            SetKeyword(material, "_METALNESSMAP", hasMetalnessMap);
            material.SetFloat("_Metallic", hasMetalnessMap ? 1f : GetFloat(src, "metalness", 0f));

            // ── Specular ──
            material.SetFloat("_SpecularWeight", GetFloat(src, "specular", 1f));
            var hasSpecularColorMap = SetTexture(src, material, "specularColor", "_SpecularColorMap");
            SetKeyword(material, "_SPECULARCOLORMAP", hasSpecularColorMap);
            material.SetColor("_SpecularColor", hasSpecularColorMap ? Color.white : GetColor(src, "specularColor", Color.white));

            var hasSpecularRoughnessMap = SetTexture(src, material, "specularRoughness", "_SpecularRoughnessMap");
            SetKeyword(material, "_SPECULARROUGHNESSMAP", hasSpecularRoughnessMap);
            material.SetFloat("_Smoothness", hasSpecularRoughnessMap ? 1f : 1f - Mathf.Clamp01(GetFloat(src, "specularRoughness", 0.2f)));

            material.SetFloat("_SpecularIOR", GetFloat(src, "specularIOR", 1.5f));
            material.SetFloat("_SpecularAnisotropy", GetFloat(src, "specularAnisotropy", 0f));

            // ── Coat ──
            material.SetFloat("_CoatWeight", GetFloat(src, "coat", 0f));
            material.SetColor("_CoatColor", GetColor(src, "coatColor", Color.white));
            material.SetFloat("_CoatRoughness", GetFloat(src, "coatRoughness", 0.1f));
            material.SetFloat("_CoatIOR", GetFloat(src, "coatIOR", 1.5f));

            // ── Sheen ──
            material.SetFloat("_SheenWeight", GetFloat(src, "sheen", 0f));
            material.SetColor("_SheenColor", GetColor(src, "sheenColor", Color.white));
            material.SetFloat("_SheenRoughness", GetFloat(src, "sheenRoughness", 0.3f));

            // ── Emission(weight を色に畳む。Common の EmissionColor × Intensity と同じ扱い) ──
            var emission = GetFloat(src, "emission", 0f);
            var emissionColor = Color.black;
            if (emission > 0f)
            {
                // Color × float はアルファも掛かるので rgb だけ乗算する(アルファは 1 固定)。
                var baseEmission = SetTexture(src, material, "emissionColor", "_EmissionMap") ? Color.white : GetColor(src, "emissionColor", Color.white);
                emissionColor = new Color(baseEmission.r * emission, baseEmission.g * emission, baseEmission.b * emission, 1f);
            }

            material.SetColor("_EmissionColor", emissionColor);
            SetKeyword(material, "_EMISSION", emission > 0f);
            material.globalIlluminationFlags = emission > 0f ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            // ── Opacity / Transmission ──
            var hasOpacityMap = SetTexture(src, material, "opacity", "_OpacityMap");
            SetKeyword(material, "_OPACITYMAP", hasOpacityMap);
            var opacity = 1f;
            if (!hasOpacityMap && src.TryGetColor("opacity", out var opacityColor))
            {
                opacity = Mathf.Min(opacityColor.x, Mathf.Min(opacityColor.y, opacityColor.z));
            }

            material.SetFloat("_Opacity", hasOpacityMap ? 1f : opacity);
            var transmission = GetFloat(src, "transmission", 0f);
            material.SetFloat("_TransmissionWeight", transmission);

            // ── Subsurface / Thin walled ──
            material.SetFloat("_SubsurfaceWeight", GetFloat(src, "subsurface", 0f));
            material.SetColor("_SubsurfaceColor", GetColor(src, "subsurfaceColor", Color.white));
            var thinWalled = GetFloat(src, "thinWalled", 0f) > 0.5f;
            material.SetFloat("_ThinWalled", thinWalled ? 1f : 0f);

            // ── Normal ──
            var hasNormal = SetTexture(src, material, "normalCamera", "_BumpMap");
            material.SetFloat("_BumpScale", 1f);
            SetKeyword(material, "_NORMALMAP", hasNormal);

            // ── Blend / Cull(MaterialCommonBinding と同じ状態にしておく。Maya インポータはここから Common.Blend / DoubleSided を読む) ──
            var transparent = hasOpacityMap || opacity < 1f || transmission > 0f;
            MaterialCommonBinding.ApplyBlend(material, transparent ? BlendType.Transparent : BlendType.Opaque, 0.5f);
            material.renderQueue = transparent ? (int)UnityEngine.Rendering.RenderQueue.Transparent : (int)UnityEngine.Rendering.RenderQueue.Geometry;
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", thinWalled ? (float)UnityEngine.Rendering.CullMode.Off : (float)UnityEngine.Rendering.CullMode.Back);
            }

            material.doubleSidedGI = thinWalled;
            return true;
        }

        private static float GetFloat(IArnoldSource src, string name, float fallback)
            => src.TryGetFloat(name, out var v) ? v : fallback;

        private static Color GetColor(IArnoldSource src, string name, Color fallback)
            => src.TryGetColor(name, out var v) ? new Color(v.x, v.y, v.z, 1f) : fallback;

        private static bool SetTexture(IArnoldSource src, UnityEngine.Material material, string name, string property)
        {
            if (!src.TryGetTexture(name, out var texture, out var offset, out var scale) || texture == null)
            {
                return false;
            }

            material.SetTexture(property, texture);
            material.SetTextureOffset(property, offset);
            material.SetTextureScale(property, scale);
            return true;
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
