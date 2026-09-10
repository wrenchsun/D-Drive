using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    public readonly struct ControlSkinMarker
    {
    }

    // [15_ui_interaction.md] A-3 / [18_ui_controls.md] A-1 — 状態 1 つ分の見た目。
    // EnterTweenId は 4-8(UiTween)実装後に AssetId<UiTweenMarker> へ置き換える予定のプレースホルダで、
    // 現時点では未使用(生 ulong のまま保持するだけ)。
    [Serializable]
    public struct StateVisual
    {
        [Tooltip("4-8 で UiTweenId に置換")]
        public ulong EnterTweenId;
        public Color Tint;
        public ValueDef Scale;
        public Sprite OverrideSprite;

        // Inspector 上の既定値。Tint=白・Scale=定数1(Evaluate(1f)=1)で「未設定でも普通に表示される」を保証する。
        public static StateVisual Default => new() { Tint = Color.white, Scale = ValueDef.Constant01(1f) };
    }

    // [15_ui_interaction.md] A-3 / [18_ui_controls.md] A-1 — ButtonSkinData(15) /
    // SliderSkinData(18、未実装)が派生する Skin 基底。ControlState ごとの StateVisual を持ち、
    // UiInteractable.SetVisual/ApplySkinForCurrentState が Get(State) で引く。
    // 抽象型のため [AssetIdDefinition] は付けない(codegen は具象型のみを走査する。派生の
    // ButtonSkinData 側に付与する)。
    public abstract class ControlSkinData : AssetDataBase
    {
        [Header("状態別ビジュアル")]
        public StateVisual Normal = StateVisual.Default;
        public StateVisual Hover = StateVisual.Default;
        public StateVisual Pressed = StateVisual.Default;
        public StateVisual Selected = StateVisual.Default;
        public StateVisual Disabled = StateVisual.Default;
        public StateVisual Locked = StateVisual.Default;

        public ref readonly StateVisual Get(ControlState state)
        {
            switch (state)
            {
                case ControlState.Hover: return ref Hover;
                case ControlState.Pressed: return ref Pressed;
                case ControlState.Selected: return ref Selected;
                case ControlState.Disabled: return ref Disabled;
                case ControlState.Locked: return ref Locked;
                default: return ref Normal;
            }
        }
    }
}
