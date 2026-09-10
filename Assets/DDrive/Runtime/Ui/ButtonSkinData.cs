using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using UnityEngine;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] A-3 — UiButton 用 Skin。状態別ビジュアルは基底(ControlSkinData)、
    // ボタン固有の SE(Hover/Click/LongPress/Disabled 押下時=Denied)をここに追加する。
    [CreateAssetMenu(menuName = "D-Drive/Ui/Button Skin", fileName = "SKIN_New")]
    [AssetIdDefinition(AssetType.ControlSkin, typeof(ControlSkinMarker), "SKINID")]
    public class ButtonSkinData : ControlSkinData
    {
        [Header("SE")]
        [Tooltip("Hover 状態に入ったときの SE(任意)")]
        public SeId HoverSe;
        [Tooltip("Click 発火時の SE")]
        public SeId ClickSe;
        [Tooltip("LongPress 発火時の SE")]
        public SeId LongPressSe;
        [Tooltip("Disabled/Locked 状態で押されたとき(OnDenied)の SE")]
        public SeId DeniedSe;
    }
}
