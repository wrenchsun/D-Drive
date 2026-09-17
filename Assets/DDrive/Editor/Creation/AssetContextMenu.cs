using DDrive.Editor.Menu;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEditor;

namespace DDrive.Editor.Creation
{
    // [11_tasks.md] U-17(2026-09-17) / [09_editor_tools.md] §1.2 —
    // Project ウィンドウの右クリック(Unity 標準の "Assets/" メニュー)から、選んだソースアセットの
    // 種類に応じた Data を作る。中身は全て SourceDataCreation(= ImportRule のハンドラ表 + 追加分)に
    // 委譲しており、ここにあるのは [MenuItem] の宣言だけ。
    //
    // [MenuItem] のパスは定数でなければならないため種別ぶんメソッドが並ぶが、対応表(拡張子・割り当て先
    // フィールド)は SourceDataCreation 側の 1 箇所にしかない。
    // validate([MenuItem(..., true)])で、選択中のアセットの拡張子に合わないメニューは無効化する。
    internal static class AssetContextMenu
    {
        private const string Suffix = " を作成";

        // ── 音 ──

        [MenuItem(DDriveMenu.AssetsCreateData + "SeData" + Suffix)]
        private static void CreateSe() => SourceDataCreation.CreateFromSelection(typeof(SeData));

        [MenuItem(DDriveMenu.AssetsCreateData + "SeData" + Suffix, true)]
        private static bool ValidateCreateSe() => SourceDataCreation.CanCreate(typeof(SeData));

        [MenuItem(DDriveMenu.AssetsCreateData + "BgmData" + Suffix)]
        private static void CreateBgm() => SourceDataCreation.CreateFromSelection(typeof(BgmData));

        [MenuItem(DDriveMenu.AssetsCreateData + "BgmData" + Suffix, true)]
        private static bool ValidateCreateBgm() => SourceDataCreation.CanCreate(typeof(BgmData));

        // ── 画像・マテリアル ──

        [MenuItem(DDriveMenu.AssetsCreateData + "TextureData" + Suffix)]
        private static void CreateTexture() => SourceDataCreation.CreateFromSelection(typeof(DDrive.Runtime.Material.TextureData));

        [MenuItem(DDriveMenu.AssetsCreateData + "TextureData" + Suffix, true)]
        private static bool ValidateCreateTexture() => SourceDataCreation.CanCreate(typeof(DDrive.Runtime.Material.TextureData));

        [MenuItem(DDriveMenu.AssetsCreateData + "MaterialData" + Suffix)]
        private static void CreateMaterial() => SourceDataCreation.CreateFromSelection(typeof(DDrive.Runtime.Material.MaterialData));

        [MenuItem(DDriveMenu.AssetsCreateData + "MaterialData" + Suffix, true)]
        private static bool ValidateCreateMaterial() => SourceDataCreation.CanCreate(typeof(DDrive.Runtime.Material.MaterialData));

        // ── モデル・アニメ ──

        [MenuItem(DDriveMenu.AssetsCreateData + "ModelData" + Suffix)]
        private static void CreateModel() => SourceDataCreation.CreateFromSelection(typeof(ModelData));

        [MenuItem(DDriveMenu.AssetsCreateData + "ModelData" + Suffix, true)]
        private static bool ValidateCreateModel() => SourceDataCreation.CanCreate(typeof(ModelData));

        [MenuItem(DDriveMenu.AssetsCreateData + "AnimData" + Suffix)]
        private static void CreateAnim() => SourceDataCreation.CreateFromSelection(typeof(AnimData));

        [MenuItem(DDriveMenu.AssetsCreateData + "AnimData" + Suffix, true)]
        private static bool ValidateCreateAnim() => SourceDataCreation.CanCreate(typeof(AnimData));

        [MenuItem(DDriveMenu.AssetsCreateData + "Anim2DData" + Suffix)]
        private static void CreateAnim2D() => SourceDataCreation.CreateFromSelection(typeof(Anim2DData));

        [MenuItem(DDriveMenu.AssetsCreateData + "Anim2DData" + Suffix, true)]
        private static bool ValidateCreateAnim2D() => SourceDataCreation.CanCreate(typeof(Anim2DData));

        // ── Prefab 系(.prefab は Prefab / Canvas / Vfx のどれにもなり得るので 3 つとも出す) ──

        [MenuItem(DDriveMenu.AssetsCreateData + "PrefabData" + Suffix)]
        private static void CreatePrefab() => SourceDataCreation.CreateFromSelection(typeof(PrefabData));

        [MenuItem(DDriveMenu.AssetsCreateData + "PrefabData" + Suffix, true)]
        private static bool ValidateCreatePrefab() => SourceDataCreation.CanCreate(typeof(PrefabData));

        [MenuItem(DDriveMenu.AssetsCreateData + "CanvasData" + Suffix)]
        private static void CreateCanvas() => SourceDataCreation.CreateFromSelection(typeof(CanvasData));

        [MenuItem(DDriveMenu.AssetsCreateData + "CanvasData" + Suffix, true)]
        private static bool ValidateCreateCanvas() => SourceDataCreation.CanCreate(typeof(CanvasData));

        [MenuItem(DDriveMenu.AssetsCreateData + "VfxData" + Suffix)]
        private static void CreateVfx() => SourceDataCreation.CreateFromSelection(typeof(VfxData));

        [MenuItem(DDriveMenu.AssetsCreateData + "VfxData" + Suffix, true)]
        private static bool ValidateCreateVfx() => SourceDataCreation.CanCreate(typeof(VfxData));

        // ── UI Skin(画像 1 枚を Normal 状態に入れた状態で作る) ──

        [MenuItem(DDriveMenu.AssetsCreateData + "ButtonSkinData" + Suffix)]
        private static void CreateButtonSkin() => SourceDataCreation.CreateFromSelection(typeof(ButtonSkinData));

        [MenuItem(DDriveMenu.AssetsCreateData + "ButtonSkinData" + Suffix, true)]
        private static bool ValidateCreateButtonSkin() => SourceDataCreation.CanCreate(typeof(ButtonSkinData));

        [MenuItem(DDriveMenu.AssetsCreateData + "SliderSkinData" + Suffix)]
        private static void CreateSliderSkin() => SourceDataCreation.CreateFromSelection(typeof(SliderSkinData));

        [MenuItem(DDriveMenu.AssetsCreateData + "SliderSkinData" + Suffix, true)]
        private static bool ValidateCreateSliderSkin() => SourceDataCreation.CanCreate(typeof(SliderSkinData));
    }
}
