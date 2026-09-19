using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // [07_canvas_prefab.md] NavNode / [18_ui_controls.md] A-1 — UiInteractable(Selectable でない)向けの
    // 明示ナビゲーション。UiManager.ApplyNavigation が CanvasData.Navigation から Open 時に付与し、
    // UiManager.MoveFocus がこれを辿ってフォーカスを移動する。
    public sealed class UiNavigation : MonoBehaviour
    {
        public Transform Up;
        public Transform Down;
        public Transform Left;
        public Transform Right;
    }
}
