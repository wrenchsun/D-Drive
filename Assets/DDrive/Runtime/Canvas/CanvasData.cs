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

    // [07_canvas_prefab.md] 実装メモ 2026-09-12 追記 — CanvasEditorWindow のノードグラフ(NavigationGraphView)で
    // ドラッグして動かしたノードの表示位置。ランタイムのナビゲーション動作(UiManager.ApplyNavigation/
    // MoveFocusFrom)には一切使わない、エディタ表示専用のデータ。ユーザー要望により例外的に永続化する
    // (通常方針「Data はエディタでのみ、実 UI に関わる項目だけを書き換える」の対象外の見た目情報)。
    [Serializable]
    public struct NavNodeLayout
    {
        public string Element;   // NavigationGraph のノードパス(ルートは空文字)
        public Vector2 Position; // NavigationGraphView 座標系での表示位置
    }

    // 同上。配線(Element から Direction 方向へのリンク)に挿入した Reroute point(中継点)の一覧。
    [Serializable]
    public struct NavEdgeWaypoint
    {
        public string Element;   // From
        public string Direction; // "Up"/"Down"/"Left"/"Right"(Editor 側 NavDirection.ToString() と対応)
        public Vector2[] Points;
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
        // [18_ui_controls.md] B-4(4-16) — SliderWire 専用。OptionStore(Option)へ現在値を書く。
        SetOption,
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

    // [18_ui_controls.md] B-4 — UiSlider の入力種別(4-16)。
    public enum SliderTrigger
    {
        Changed,
        Commit,
        NotchPassed,
        LimitReached,
    }

    // スライダー配線([18_ui_controls.md] B-4、4-16)。Action=SetOption のとき Option を使い、
    // OptionStore との直結を配線だけで完結させる(コード不要で音量設定画面が組める)。
    [Serializable]
    public struct SliderWire
    {
        public string ElementPath;
        public SliderTrigger Trigger;
        public UiAction Action;
        public string SignalKey;
        public OptionKey Option;
        [Tooltip("Trigger=Changed の通知間引き(秒)。UiSlider.ChangeThrottleSec との大きい方が使われる")]
        public float ThrottleSec;
    }

    // [15_ui_interaction.md] B-4 — Canvas 内の 1 要素分の Appear/Idle/Disappear 演出(チケット 4-9)。
    // 解決順は id(Appear/Idle/Disappear) → Preset(AppearPreset/IdlePreset/DisappearPreset) →
    // レイヤー既定(UiLayerSettings.DefaultAppear/DefaultDisappear。Idle にはレイヤー既定は無い)。
    // いずれも未設定なら該当区間は何もしない(即完了扱い)。
    [Serializable]
    public struct ElementFx
    {
        [Tooltip("Prefab ルートからの相対パス(UiManager.OpenData の root.Find 基準)")]
        public string ElementPath;

        public UiPresetRef AppearPreset;
        public UiPresetRef IdlePreset;
        public UiPresetRef DisappearPreset;

        [Tooltip("設定があれば対応する Preset より優先される")]
        public AssetId<UiTweenMarker> Appear;
        public AssetId<UiTweenMarker> Idle;
        public AssetId<UiTweenMarker> Disappear;

        [Tooltip("Open 開始からこの秒数だけ遅らせて Appear を再生する(スタッガー演出用)")]
        public float AppearDelay;

        public AssetId<SeMarker> AppearSe;
        public AssetId<SeMarker> DisappearSe;
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

        [Header("Navigation グラフ(エディタ表示専用)")]
        [Tooltip("CanvasEditorWindow のノードグラフでドラッグして動かした表示位置。ランタイムの動作には影響しない。")]
        [HideInInspector]
        public NavNodeLayout[] NavigationNodeLayout;
        [Tooltip("同上。配線に挿入した Reroute point(中継点)の一覧。")]
        [HideInInspector]
        public NavEdgeWaypoint[] NavigationEdgeWaypoints;

        [Header("Wiring")]
        public ButtonWire[] Buttons;
        public SliderWire[] Sliders;

        [Header("ElementFx")]
        [Tooltip("Open/Close 時に個別再生する要素演出(4-9)。Idle は Appear 完了後にループ再生し、Close で停止する。")]
        public ElementFx[] ElementEffects;
    }
}
