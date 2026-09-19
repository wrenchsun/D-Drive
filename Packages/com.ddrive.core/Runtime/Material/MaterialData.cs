using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Material
{
    // シェーダー固有パラメータ(名前 + 型 + 値)。共通データ(MaterialCommon)に無いものだけをここに置く。
    [Serializable]
    public struct ShaderParam
    {
        [Tooltip("シェーダープロパティ名(例: _SpecColor)。")]
        public string Property;

        [Tooltip("値。Type に応じたフィールドを使う。Object はテクスチャ等。")]
        public ParamValue Value;
    }

    // MaterialAnim が動かす対象。Property の意味は Channel で決まる。
    public enum MaterialAnimChannel
    {
        Float,    // SetFloat(Property, value)
        OffsetU,  // テクスチャ Property の UV オフセット X(UV スクロール)
        OffsetV,  // テクスチャ Property の UV オフセット Y
    }

    // [06_material_texture.md] A-2 — 「常時イベント」の実体。UV スクロール等を ValueDef([17])で表す。
    //   UV スクロール → Channel=OffsetU, Value: Parametric(Linear) From 0 To 1, Time=Rate, Loop=Loop
    //   サイン波      → Channel=Float,   Value: Parametric(InOutSine) + PingPong
    //   任意波形      → Channel=Float,   Value: Curve
    [Serializable]
    public struct MaterialAnim
    {
        [Tooltip("対象のシェーダープロパティ名(Float なら数値プロパティ、OffsetU/V ならテクスチャプロパティ。例: _BaseMap)。")]
        public string Property;

        [Tooltip("Property をどう動かすか。")]
        public MaterialAnimChannel Channel;

        [Tooltip("形 + スピード([17] ValueDef)。")]
        public ValueDef Value;
    }

    // [06_material_texture.md] A-2 — マテリアルの Data。共通データ(MaterialCommon)+ シェーダー固有 + 描画設定 + 常時アニメ。
    // Unity の Material 実体は MaterialManager がこの Data から生成して共有する(Data 自体は読み取り専用、[01] ADR-1)。
    [CreateAssetMenu(menuName = "D-Drive/Material/Material Data", fileName = "MAT_NewMaterial")]
    [AssetIdDefinition(AssetType.Material, typeof(MaterialMarker), "MATID")]
    public class MaterialData : AssetDataBase
    {
        [Header("Shader")]
        [Tooltip("使うシェーダー。未設定なら Manager がプロジェクトの既定 Lit(URP Lit)で生成する。")]
        public Shader Shader;

        [Header("Common")]
        [Tooltip("どのシェーダーでも意味が共通するチャンネル(Albedo / Normal / Mask / Emission / Blend)。")]
        public MaterialCommon Common = MaterialCommon.Default;

        [Header("Specific")]
        [Tooltip("シェーダー固有のパラメータ。Common に無いものだけ。")]
        public ShaderParam[] Specific;

        [Header("Render")]
        [Tooltip("Blend から決まる基準 RenderQueue(Opaque=2000 / Cutout=2450 / Transparent=3000)へのオフセット。")]
        public int RenderQueueOffset;

        [Tooltip("Rendering Layer Mask(0 なら Renderer 側の設定を変えない。ライトレイヤーは Renderer 単位の設定なので ModelData.LightLayerMask も参照)。")]
        public uint RenderingLayerMask;

        [Header("Anim")]
        [Tooltip("常時アニメ(UV スクロール等)。MaterialManager の Tick が共有 Material に対して一括で駆動する。")]
        public MaterialAnim[] Anims;

        [Header("Import")]
        [Tooltip("Maya FBX 自動生成の由来(\"FBX名/マテリアル名\")。再インポート時の同定に使う。手動で作った Data は空。")]
        public string SourceMaterial;

        // Blend から決まる基準 RenderQueue。
        public int BaseRenderQueue => Common.Blend switch
        {
            BlendType.Cutout => (int)UnityEngine.Rendering.RenderQueue.AlphaTest,
            BlendType.Transparent => (int)UnityEngine.Rendering.RenderQueue.Transparent,
            _ => (int)UnityEngine.Rendering.RenderQueue.Geometry,
        };

        public int RenderQueue => BaseRenderQueue + RenderQueueOffset;

        public bool HasAnims => Anims != null && Anims.Length > 0;
    }
}
