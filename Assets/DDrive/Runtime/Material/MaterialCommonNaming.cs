using System.Collections.Generic;
using UnityEngine.Rendering;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-2 — 共通チャンネル名の命名規約(2026-09-11、Specific 自動解決のために確定)。
    //
    // 「シェーダーのどのプロパティが共通チャンネル(MaterialCommon)で、どれが固有(Specific)か」は名前で決める。
    // カスタムシェーダーで共通チャンネルを使いたければ、ここに載っている名前でプロパティを宣言する。
    // そうすれば MaterialCommonBinding が自動で流し込み、Specific 自動解決(MaterialSpecificResolver)の対象からも外れる。
    //
    // 規約:
    //   1. 共通チャンネル名(正準名 + 別名)   : Albedo=_BaseMap(_MainTex) / Tint=_BaseColor(_Color) / Normal=_BumpMap + _BumpScale(_NormalScale) /
    //                                          Mask=_MetallicGlossMap + _OcclusionMap(_MaskMap) + _Metallic + _Smoothness(_Glossiness) + _SmoothnessTextureChannel /
    //                                          Emission=_EmissionMap + _EmissionColor
    //   2. 描画ステート名                      : _Surface / _Blend / _SrcBlend / _DstBlend / _ZWrite / _AlphaClip / _Cutoff / _Cull / _Mode と、
    //                                          URP の ShaderGUI が管理する _ZTest / _ZWriteControl / _QueueOffset / _QueueControl / _BlendOp /
    //                                          _SrcBlendAlpha / _DstBlendAlpha / _AlphaToMask / _BlendModePreserveSpecular
    //                                          → MaterialCommon.Blend / DoubleSided / RenderQueueOffset の担当なので固有には含めない
    //   3. 付随名                              : テクスチャの <Tex>_ST / <Tex>_TexelSize / <Tex>_HDR は固有に含めない(Unity が自動生成する)
    //   4. 予約接頭辞                          : unity_* / _Unity* は Unity 内蔵。固有に含めない
    //   5. [HideInInspector] / [PerRendererData] / [NonModifiableTextureData] を持つプロパティは固有に含めない
    //      (ShaderGUI の内部状態・Renderer 単位の値であり、デザイナーが Data として持つものではない)
    //   6. 上記に該当しない名前はすべて固有(Specific)。既定値付きで MaterialData.Specific に自動登録される
    public static class MaterialCommonNaming
    {
        public enum Kind
        {
            Specific,       // 固有(Specific に載る)
            CommonChannel,  // 規約 1: 共通チャンネル
            RenderState,    // 規約 2: 描画ステート
            Derived,        // 規約 3: 付随名
            Reserved,       // 規約 4: Unity 予約
        }

        private static readonly HashSet<string> CommonChannelNames = new()
        {
            "_BaseMap", "_MainTex", "_BaseColor", "_Color",
            "_BumpMap", "_BumpScale", "_NormalScale",
            "_MetallicGlossMap", "_OcclusionMap", "_MaskMap", "_Metallic", "_Smoothness", "_Glossiness", "_SmoothnessTextureChannel",
            "_EmissionMap", "_EmissionColor",
        };

        private static readonly HashSet<string> RenderStateNames = new()
        {
            "_Surface", "_Blend", "_SrcBlend", "_DstBlend", "_ZWrite", "_AlphaClip", "_Cutoff", "_Cull", "_Mode",
            "_ZTest", "_ZWriteControl", "_QueueOffset", "_QueueControl", "_BlendOp", "_SrcBlendAlpha", "_DstBlendAlpha",
            "_AlphaToMask", "_BlendModePreserveSpecular",
        };

        private static readonly string[] DerivedSuffixes = { "_ST", "_TexelSize", "_HDR" };

        private const ShaderPropertyFlags ExcludedFlags =
            ShaderPropertyFlags.HideInInspector | ShaderPropertyFlags.PerRendererData | ShaderPropertyFlags.NonModifiableTextureData;

        // 名前だけで分類する(フラグは見ない)。
        public static Kind Classify(string property)
        {
            if (string.IsNullOrEmpty(property))
            {
                return Kind.Reserved;
            }

            if (CommonChannelNames.Contains(property))
            {
                return Kind.CommonChannel;
            }

            if (RenderStateNames.Contains(property))
            {
                return Kind.RenderState;
            }

            if (property.StartsWith("unity_") || property.StartsWith("_Unity"))
            {
                return Kind.Reserved;
            }

            for (var i = 0; i < DerivedSuffixes.Length; i++)
            {
                if (property.Length > DerivedSuffixes[i].Length && property.EndsWith(DerivedSuffixes[i]))
                {
                    return Kind.Derived;
                }
            }

            return Kind.Specific;
        }

        public static bool IsCommon(string property) => Classify(property) != Kind.Specific;

        // 規約 5 のフラグ判定。
        public static bool IsExcludedByFlags(ShaderPropertyFlags flags) => (flags & ExcludedFlags) != 0;

        // 名前 + フラグの両方で「固有として Specific に載せるべきか」を判定する。
        public static bool IsSpecific(string property, ShaderPropertyFlags flags) =>
            Classify(property) == Kind.Specific && !IsExcludedByFlags(flags);

        public static string DisplayName(Kind kind) => kind switch
        {
            Kind.CommonChannel => "共通チャンネル",
            Kind.RenderState => "描画ステート",
            Kind.Derived => "付随名",
            Kind.Reserved => "Unity 予約",
            _ => "固有",
        };
    }
}
