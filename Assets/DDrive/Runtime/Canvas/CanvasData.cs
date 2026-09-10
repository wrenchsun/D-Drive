using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    public readonly struct CanvasMarker
    {
    }

    // [07_canvas_prefab.md] Part A-2 — Canvas をどの表示レイヤーに積むか。sortingOrder は
    // レイヤー index * 100 + SortOffset(UiManager が Open 時に適用する)。
    public enum UiLayer
    {
        HUD,
        Menu,
        Popup,
        Overlay,
        Loading,
    }

    public enum UiTransitionKind
    {
        None,
        Fade,
        Slide,
        Scale,
        Anim,
    }

    // Open/Close 演出の定義。Kind=Anim のときだけ Anim(AnimId)を使う(それ以外は無視される)。
    [Serializable]
    public struct UiTransition
    {
        public UiTransitionKind Kind;
        public float Duration;
        public EaseDef Ease;
        public Vector2 SlideOffset;
        public AssetId<AnimMarker> Anim;
    }

    // 十字キー/スティックの明示配線。Prefab 内 Selectable への相対パス(空 = Unity 自動)。
    [Serializable]
    public struct NavNode
    {
        public string Element;
        public string Up;
        public string Down;
        public string Left;
        public string Right;
    }

    // [15_ui_interaction.md] Part A の入力種別。UiButton 本体(4-6)より先にデータ形だけ定義しておく。
    public enum WireTrigger
    {
        Click,
        DoubleClick,
        LongPress,
        Repeat,
    }

    public enum UiAction
    {
        None,
        OpenCanvas,
        CloseSelf,
        CloseTop,
        SendSignal,
        PlayPresentation,
    }

    // ボタン配線。UiButton 本体の実装(4-2/4-6)より先にデータだけ持たせておく(現時点ではコードから
    // SendSignal を直接叩く運用でも成立する)。
    [Serializable]
    public struct ButtonWire
    {
        public string ButtonPath;
        public WireTrigger Trigger;
        public UiAction Action;
        public AssetRef Target;
        public string SignalKey;
        public AssetId<SeMarker> ClickSe;
    }

    // スライダー配線。UiSlider 本体([18_ui_controls.md])は未実装のためデータのみ(4-2/4-16 が配線する)。
    [Serializable]
    public struct SliderWire
    {
        public string SliderPath;
        public string OptionKey;
        public AssetRef Target;
        public string SignalKey;
    }

    // [07_canvas_prefab.md] Part A-2 — UI の 1 画面(Canvas Prefab)の設定。Open/Close/スタック/モーダル/
    // ポーズ/ナビゲーション/ボタン配線をまとめて持つ。UiButton/ElementFx 本体は後続チケット(4-2/4-6/4-9)。
    [CreateAssetMenu(menuName = "D-Drive/Ui/Canvas Data", fileName = "CANVAS_New")]
    [AssetIdDefinition(AssetType.Canvas, typeof(CanvasMarker), "CANVASID")]
    public class CanvasData : AssetDataBase
    {
        [Header("Prefab")]
        [Tooltip("Open する実体。未設定の場合は空の RectTransform を Placeholder として生成する。")]
        public GameObject Prefab;

        [Header("Layer/Sort")]
        public UiLayer Layer;
        [Tooltip("同一レイヤー内の重ね順の微調整(Canvas コンポーネントがあれば sortingOrder に加算、無ければ兄弟順序に使う)。")]
        public int SortOffset;

        [Header("Open/Close")]
        public UiTransition OpenTransition;
        public UiTransition CloseTransition;
        [Tooltip("戻る操作(CloseTop)で閉じる対象に含めるか。")]
        public bool CloseOnBack = true;
        [Tooltip("モーダル(Popup)として開いたとき、背後のスタックの入力をブロックするか。")]
        public bool ModalBlocksInput;
        [Tooltip("開いている間 PauseChannel.Gameplay を Push/Pop するか。")]
        public bool PauseGameWhileOpen;

        [Header("Navigation")]
        public NavNode[] Navigation;
        [Tooltip("Open 時に EventSystem.current.SetSelectedGameObject へ渡す初期フォーカス要素の相対パス。")]
        public string FirstSelected;

        [Header("Wiring")]
        public ButtonWire[] Buttons;
        public SliderWire[] Sliders;
    }
}
