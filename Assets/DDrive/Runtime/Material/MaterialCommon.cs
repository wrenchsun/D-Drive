using System;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-2 — 透過モード。RenderQueue とシェーダーのブレンド設定は Manager が Blend から決める。
    public enum BlendType
    {
        Opaque,
        Cutout,
        Transparent,
    }

    // [06_material_texture.md] A-2 — ★共通データ。どのシェーダーでも意味が共通するチャンネル定義(2026-09-10 規約確定、3-5)。
    //
    // 規約:
    //   Albedo   : sRGB カラー。AlbedoTint を乗算(既定 白)
    //   Normal   : タンジェント空間ノーマル(インポート設定 NormalMap)。NormalScale で強度(既定 1)
    //   Mask     : R=Metallic / G=Occlusion / B=Detail / A=Smoothness(URP Lit の _MetallicGlossMap + _OcclusionMap と
    //              HDRP の _MaskMap の両方にそのまま渡せる並び。sRGB off)
    //   Emission : sRGB カラー。EmissionColor × EmissionIntensity を乗算(Intensity 0 で無効)
    //   Blend    : Opaque / Cutout(Cutoff) / Transparent。DoubleSided で裏面も描く(Cull Off)
    //
    // テクスチャは TextureData の ID で参照する(直参照しない)。TextureData.Channel がここのチャンネルと 1:1 対応する。
    [Serializable]
    public struct MaterialCommon
    {
        [Tooltip("ベースカラー(アルベド)テクスチャの TextureData ID。")]
        public AssetId<TextureMarker> Albedo;

        [Tooltip("アルベドに乗算する色。")]
        public Color AlbedoTint;

        [Tooltip("ノーマルマップの TextureData ID。")]
        public AssetId<TextureMarker> Normal;

        [Tooltip("ノーマルの強度。")]
        public float NormalScale;

        [Tooltip("マスクテクスチャ(R:Metallic G:Occlusion B:Detail A:Smoothness)の TextureData ID。")]
        public AssetId<TextureMarker> Mask;

        [Tooltip("Mask が無いときの Metallic 定数(0..1)。")]
        [Range(0f, 1f)] public float Metallic;

        [Tooltip("Mask が無いときの Smoothness 定数(0..1)。")]
        [Range(0f, 1f)] public float Smoothness;

        [Tooltip("エミッションテクスチャの TextureData ID。")]
        public AssetId<TextureMarker> Emission;

        [Tooltip("エミッションの色。EmissionIntensity を乗算する。")]
        public Color EmissionColor;

        [Tooltip("エミッションの強さ(0 で無効)。")]
        [Min(0f)] public float EmissionIntensity;

        [Tooltip("Opaque / Cutout / Transparent。")]
        public BlendType Blend;

        [Tooltip("Cutout のアルファしきい値。")]
        [Range(0f, 1f)] public float Cutoff;

        [Tooltip("裏面も描画する(Cull Off)。")]
        public bool DoubleSided;

        public static MaterialCommon Default => new()
        {
            AlbedoTint = Color.white,
            NormalScale = 1f,
            Metallic = 0f,
            Smoothness = 0.5f,
            EmissionColor = Color.black,
            EmissionIntensity = 0f,
            Blend = BlendType.Opaque,
            Cutoff = 0.5f,
            DoubleSided = false,
        };
    }
}
