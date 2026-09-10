using System;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] B-4 / A-3, [07_canvas_prefab.md] A-3 — UiLayer(HUD/Menu/Popup/Overlay/Loading)
    // ごとの既定演出/SE/Skin(チケット 4-9 + 4-7 残り)。CanvasData/ButtonSkinData/ElementFx が個別に
    // 未設定のときだけここへフォールバックする(優先順位: id → preset → ここ)。プロジェクト単位の
    // 設定アセットのため AssetDataBase ではない(AssetId で引かない。DDriveRuntimeBootstrap の Inspector
    // 直参照 1 個だけを想定)。
    [Serializable]
    public struct UiLayerDefaultEntry
    {
        public UiLayer Layer;

        [Tooltip("SkinId 未設定かつ SetVisual されていない UiInteractable にフォールバックする Skin")]
        public AssetId<ControlSkinMarker> DefaultButtonSkin;
        public AssetId<SeMarker> DefaultClickSe;
        public AssetId<SeMarker> DefaultHoverSe;
        public AssetId<SeMarker> DefaultDeniedSe;

        [Tooltip("ElementFx.Appear/AppearPreset が両方未設定のときのフォールバック")]
        public UiPresetRef DefaultAppear;
        [Tooltip("ElementFx.Disappear/DisappearPreset が両方未設定のときのフォールバック")]
        public UiPresetRef DefaultDisappear;
    }

    [CreateAssetMenu(menuName = "D-Drive/Ui/Ui Layer Settings", fileName = "UI_LayerSettings")]
    public class UiLayerSettings : ScriptableObject
    {
        public UiLayerDefaultEntry[] Layers = Array.Empty<UiLayerDefaultEntry>();

        public bool TryGet(UiLayer layer, out UiLayerDefaultEntry entry)
        {
            if (Layers != null)
            {
                for (var i = 0; i < Layers.Length; i++)
                {
                    if (Layers[i].Layer == layer)
                    {
                        entry = Layers[i];
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }
    }
}
