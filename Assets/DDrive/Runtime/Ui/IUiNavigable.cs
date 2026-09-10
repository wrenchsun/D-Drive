using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // [07_canvas_prefab.md] NavNode / [18_ui_controls.md] A-1 —
    // Selectable でない UiInteractable にも明示ナビゲーション(NavNode)を適用できるようにする最小インタフェース。
    public interface IUiNavigable
    {
        Transform Transform { get; }
        bool CanFocus { get; }
        void SetFocused(bool focused);
    }
}
