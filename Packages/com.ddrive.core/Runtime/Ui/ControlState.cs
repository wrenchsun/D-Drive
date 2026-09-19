namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] A-1.5 / [18_ui_controls.md] Part A —
    // UiButton/UiSlider(将来)が共有する状態列挙。優先度(高→低)は
    // Locked > Disabled > Pressed > Hover/Selected > Normal(UiInteractable.SetState が解決する)。
    public enum ControlState
    {
        Normal,
        Hover,
        Pressed,
        Selected,
        Disabled,
        Locked,
    }
}
