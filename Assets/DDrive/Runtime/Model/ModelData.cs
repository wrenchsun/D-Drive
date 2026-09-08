using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Material;
using UnityEngine;

namespace DDrive.Runtime.Model
{
    public readonly struct ModelMarker
    {
    }

    // Prefab 内の Renderer への相対パス + マテリアルスロット。Material は MaterialData の ID で参照する
    // (直参照しない。[05_model_animation.md] A-2)。MaterialData 自体は Phase 3(3-5)で実装される。
    [Serializable]
    public struct MaterialSlot
    {
        [Tooltip("Prefab のルートから対象 Renderer までの相対パス(スラッシュ区切り)。")]
        public string RendererPath;

        [Tooltip("Renderer.sharedMaterials の何番目のスロットか。")]
        public int SlotIndex;

        [Tooltip("割り当てる MaterialData の ID。")]
        public AssetId<MaterialMarker> Material;
    }

    // LODGroup が持つ Screen Relative Transition Height をデータ側から上書きするための任意設定。
    // 実体の LOD Renderer 構成自体は Prefab の LODGroup が保持する(ここは閾値の差し替えのみ)。
    [Serializable]
    public struct LodProfile
    {
        [Tooltip("有効にすると、Prefab の LODGroup にこの閾値を適用する。")]
        public bool Enabled;

        [Tooltip("LOD 0,1,2... の Screen Relative Transition Height(0-1)。LODGroup の LOD 数と揃える。")]
        public float[] ScreenRelativeTransitionHeights;
    }

    [CreateAssetMenu(menuName = "D-Drive/Model/Model Data", fileName = "MODEL_NewModel")]
    [AssetIdDefinition(AssetType.Prefab, typeof(ModelMarker), "MODELID")]
    public class ModelData : AssetDataBase
    {
        [Header("Prefab")]
        [Tooltip("Spawn する実体。")]
        public GameObject Prefab;

        [Header("Material")]
        [Tooltip("Renderer名 + slotIndex → MaterialId の対応。「Slot自動収集」ボタンで Prefab から生成できる。")]
        public MaterialSlot[] Slots;

        [Header("Animation")]
        [Tooltip("任意。Spawn 直後に自動再生するアニメーション。")]
        public AssetId<AnimMarker> DefaultAnimation;

        [Tooltip("Humanoid の場合の Avatar。")]
        public Avatar Avatar;

        [Header("Render")]
        [Tooltip("Sorting/RenderingLayerMask に使う値。")]
        public int RenderLayer;

        [Tooltip("Light Layer のビットマスク。")]
        public uint LightLayerMask;

        [Tooltip("任意。LODGroup の閾値をデータ側から上書きしたい場合に使う。")]
        public LodProfile Lod;
    }
}
