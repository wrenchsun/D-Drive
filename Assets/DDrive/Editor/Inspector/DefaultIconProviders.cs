using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Inspector
{
    // [09_editor_tools.md] §8.1 — 「元アセット」から初期アイコンを作れる Data 種別の登録(2026-09-11)。
    // Prefab を持つもの(Model / Prefab / Vfx / Canvas)は AssetPreview の描画結果、Texture / Sprite を持つものはその画像を使う。
    // 元アセットが無くても描画で表現できる Material は MaterialIconProvider(Editor/Material)が登録する。
    // 新しい Data 種別で元アセットがあるものはここに 1 行足す。
    internal static class DefaultIconProviders
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            AssetIconService.RegisterSource<ModelData>(d => d.Prefab);
            AssetIconService.RegisterSource<PrefabData>(d => d.Prefab);
            AssetIconService.RegisterSource<VfxData>(d => d.Prefab);
            AssetIconService.RegisterSource<CanvasData>(d => d.Prefab);
            AssetIconService.RegisterSource<TextureData>(d => d.Sprite != null ? d.Sprite : (Object)d.Texture);
            AssetIconService.RegisterSource<ControlSkinData>(d => d.Normal.OverrideSprite); // Normal 状態の差し替え Sprite
            AssetIconService.RegisterSource<SliderSkinData>(d => d.NotchSprite);
        }
    }
}
