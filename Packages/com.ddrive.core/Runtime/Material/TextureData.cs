using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Runtime.Material
{
    public enum TextureUsage
    {
        Model,
        UI,
    }

    // MaterialCommon のチャンネル規約と 1:1 対応([06] B-2)。
    public enum TextureChannel
    {
        Albedo,
        Normal,
        Mask,
        Emission,
        Other,
    }

    // [06_material_texture.md] B-2 — 画像(テクスチャ)の Data(3-5 で MaterialCommon の参照先として最小構成を先行実装。
    // Importer 規約 / TextureImportProfile / UsedIn* の自動収集 / Validator は 3-8 で追加する)。
    [CreateAssetMenu(menuName = "D-Drive/Material/Texture Data", fileName = "TEX_NewTexture")]
    [AssetIdDefinition(AssetType.Texture, typeof(TextureMarker), "TEXID")]
    public class TextureData : AssetDataBase
    {
        [Header("Texture")]
        [Tooltip("実体のテクスチャ。")]
        public Texture2D Texture;

        [Tooltip("UI 用か 3D モデル用か。")]
        public TextureUsage Usage = TextureUsage.Model;

        [Header("UI")]
        [Tooltip("Usage=UI のときの Sprite。")]
        public Sprite Sprite;

        [Tooltip("拡縮してよいか(9-slice 未設定の警告に使う)。")]
        public bool AllowScale;

        [Tooltip("9-slice のボーダー(L,B,R,T)。")]
        public Vector4 SliceBorder;

        [Header("Model")]
        [Tooltip("MaterialCommon のどのチャンネルに使う画像か(Albedo / Normal / Mask / Emission)。")]
        public TextureChannel Channel = TextureChannel.Albedo;
    }
}
