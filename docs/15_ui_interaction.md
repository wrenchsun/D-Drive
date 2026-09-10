# 15. UI インタラクション詳細設計（独自ボタン / イージング・Tween システム）

関連: [07_canvas_prefab.md](07_canvas_prefab.md) / [03_audio.md](03_audio.md) / [02_core_framework.md](02_core_framework.md)

---

# Part A — UiButton（ボタン全面新規実装）

## A-1. 方針

uGUI の `Button` / `Selectable` は使わない。`IPointerDownHandler` 等の EventSystem インタフェースから独自実装し、状態遷移・イベント・演出・SE を一体設計する。

理由: 標準 Button は長押し・リピート・二重押下防止・演出連携が全部後付けになり、プロジェクト内で実装がバラける。最初から全部入りの 1 コンポーネントに統一する。

## A-1.5 UiInteractable（共通基底）

状態機械・入力受付（Pointer 系ハンドラ）・Skin・Locked・ナビゲーション対応は `UiInteractable` 基底が提供し、UiButton / UiSlider（[18_ui_controls.md](18_ui_controls.md)）および将来の Toggle / Stepper / Scrollbar が共有する。状態列挙 `ControlState`（Normal/Hover/Pressed/Selected/Disabled/Locked）と Skin 基底 `ControlSkinData` も共通（基底の完全定義は [18] Part A）。

## A-2. コンポーネント構成

```csharp
public class UiButton : UiInteractable, ISubmitHandler
    // 基底 UiInteractable（[18] Part A）が Pointer 系ハンドラ・状態機械（ControlState）・
    // Interactable / SetLocked・OnPress / OnHover / OnFocus / OnStateChanged・
    // IUiNavigable（[07] NavNode）を提供する
{

    // ── 判定パラメータ (Inspector/CanvasDataから設定) ──
    public float LongPressSec = 0.5f;
    public float RepeatIntervalSec = 0.1f;   // 押しっぱなしリピート(0=無効)
    public float DoubleClickSec = 0.3f;      // (0=無効)
    public float CooldownSec = 0.15f;        // 連打防止(発火後の不感時間)
    public bool  BlockDoubleFire = true;     // 同フレーム多重発火防止(全UiButton横断)

    // ── イベント (コード購読・R3) ──
    public Observable<Unit> OnClick        { get; }
    public Observable<Unit> OnDoubleClick  { get; }
    public Observable<Unit> OnLongPress    { get; }
    public Observable<Unit> OnRepeat       { get; }   // リピート発火ごと
    // OnPress / OnHover / OnFocus / OnStateChanged は基底が提供

    // ── 関数 ──
    public void SimulateClick();                  // デバッグ・チュートリアル誘導用
    public UniTask<Unit> WaitClickAsync(CancellationToken ct);
    public void SetVisual(ButtonSkinId id);       // スキン(下記A-4)差し替え
}
```

- デザイナー向けの配線は従来通り **CanvasData.ButtonWire**（[07] §A-2）。ButtonWire に `WireTrigger`（Click / DoubleClick / LongPress / Repeat）を追加し、1 ボタンに複数配線可
- プログラマー向けは R3 Observable + UniTask。`button.OnClick.Subscribe(...)` / `await button.WaitClickAsync(ct)`

## A-3. 状態遷移と演出・SE の統合

状態ごとの見た目・音は **ButtonSkinData**（AssetData 化、ID 管理）で共通定義する。個々のボタンに手作業で設定させない。

```csharp
public class ButtonSkinData : ControlSkinData   // Skin 基底は [18] A-1（Slider と機構を共有）
{
    public StateVisual Normal, Hover, Pressed, Selected, Disabled, Locked;
    public SeId HoverSe, ClickSe, LongPressSe, DeniedSe;   // Disabled押下時=DeniedSe
}

[Serializable]
public struct StateVisual
{
    public UiTweenId EnterTween;     // この状態に入るときの Tween (Part B)
    public Color Tint;
    public ValueDef Scale;           // 押下時 0.95 等（定数指定が基本。[17]）
    public Sprite OverrideSprite;    // 任意
}
```

- 遷移演出は Part B の Tween システムで再生（Pressed に入る 80ms の縮小など）
- ゲームパッド: フォーカス移動時 Hover→Selected 相当。ナビゲーションは [07] NavNode に従う

## A-4. Validation

ButtonWire の Trigger 重複 (Warning) / LongPressSec≤0 なのに LongPress 配線あり (Error) / Skin 未割当 (Warning, Layer 既定 Skin にフォールバック) / Cooldown=0 かつ Wire=シーン遷移 (Warning: 連打多重遷移リスク)

### 実装メモ（2026-09-11、4-6 / 4-2）

- **R3 は未導入**。設計書の `Observable<T>` は全て素の C# `event Action`(引数なしは `Action`、bool/ControlState 付きは `Action<bool>`/`Action<ControlState>`)で実装した。待ち合わせだけ `UniTask`(`WaitClickAsync`)を使う。将来 R3 を導入する際は `event` → `Subject<T>`/`Observable<T>` への置換で足りるよう、イベント名・引数はそのまま踏襲した
- 実装: `Assets/DDrive/Runtime/Ui/`(`ControlState.cs` / `IUiNavigable.cs` / `ControlSkinData.cs`(`StateVisual`/`ControlSkinMarker` 含む) / `ButtonSkinData.cs` / `UiSkins.cs` / `UiInteractable.cs` / `UiButton.cs` / `UiNavigation.cs` / `ButtonSkinDataValidator.cs`)+ `Assets/DDrive/Editor/Ui/ButtonSkinEditorWindow.cs`
- `UiInteractable`(abstract): `ControlState State` / `Interactable` / `SetLocked(bool, reasonKey)` / `CooldownSec` / `BlockDoubleFire` / `OnPress・OnHover・OnFocus・OnStateChanged・OnDenied` / `SkinId`(`AssetId<ControlSkinMarker>`、`UiSkins.Resolver` で遅延解決) / `SetVisual(ControlSkinData)` / `protected abstract void OnSkinApplied(in StateVisual)`。状態は `protected void SetState(ControlState requested)` が `Locked > Disabled > (requested: Pressed > Hover/Selected > Normal)` の優先度で確定させる(`ComputeBaseState()` が Pointer/Focus の bool 3 つから requested を計算)。`TryBeginFire()` が Cooldown(`_cooldownRemaining`、`TickCooldown(dt)` で毎フレーム減算)+ 同フレーム多重発火防止(`static int _lastFireFrame` を `Time.frameCount` と比較。全 `UiInteractable` 横断)を一括判定し、拒否時は `OnDenied` を発火して false を返す。`Interactable/SetLocked` の変更でも `SetState` を呼び直し優先度を再適用する
- `UiButton`: `LongPressSec=0.5 / RepeatIntervalSec=0.1 / DoubleClickSec=0.3`(`CooldownSec=0.15` は基底の既定値をそのまま使う。全 `UiInteractable` 共通の妥当な既定として基底に持たせた)。`OnClick/OnDoubleClick/OnLongPress/OnRepeat` は全て `TryBeginFire()` を通してから発火する(Repeat のみ Cooldown を経由しない連続発火)。押下中の判定は `internal void Advance(float unscaledDt)`(`Update()` が `Time.unscaledDeltaTime` を渡すだけ)に集約し、held 秒数・LongPress 済みフラグ・Repeat 直前秒・保留中クリックのタイマーを進める。テストは `Press()/Release(bool inside=true)/Hover(bool)/Focus(bool)/Advance(dt)` を EventSystem なしで直接呼んで駆動できる(`OnPointerDown` 等の EventSystem ハンドラはこれらを呼ぶ薄いラッパー)。DoubleClickSec>0 のときは Release 時点で即クリックせず「保留」にし、その秒数以内に 2 回目の Release があれば DoubleClick として確定、無ければタイムアウトで単発 Click が発火する
- `ButtonSkinData`(`AssetType.ControlSkin` 新設、`SKIN`/`Ui/Skin`/`UiCatalog` を命名・カタログ規約に追加): `ControlSkinData`(abstract)が `Normal/Hover/Pressed/Selected/Disabled/Locked` の `StateVisual` を持つ。`StateVisual.EnterTweenId` は `ulong` のプレースホルダ(4-8 の UiTween 実装後に `AssetId<UiTweenMarker>` へ置換予定。現状未使用)。Tint/Scale(`ValueDef`)/OverrideSprite は `UiInteractable.ApplyVisual` が `TargetGraphic`(`Graphic`、Inspector で任意設定)と `RectTransform.localScale` に定数適用する(Tween 演出は 4-8 で追加)
- ナビゲーション: `UiManager.ApplyNavigation` が `NavNode.Element` の対象を判別し、`Selectable` なら従来通り `Navigation.Mode.Explicit`、`UiInteractable`(Selectable でない)なら新設の `UiNavigation`(`Up/Down/Left/Right` の Transform 参照)を付与する。`UiManager.MoveFocus(Vector2 dir)` は `EventSystem.currentSelectedGameObject` から `UiNavigation` または `Selectable.navigation` のどちらかを読んで移動する最小実装(フォーカス追跡専用のレジストリ等は持たない)
- ButtonWire 実行(4-2 完了): `UiManager.OpenData` が Open 時に `WireButtons` で `ButtonWire.ButtonPath` から `UiButton` を解決し、`Trigger`(Click/DoubleClick/LongPress/Repeat)に応じたイベントへ `Action` を 1 つ購読する(配線の数だけ小さなクロージャを生成するが、Tick 経路ではないため許容。[12]§3)。`Action`(OpenCanvas→`OpenAsync().Forget()` / CloseSelf→`Close(from)` / CloseTop→`CloseTop()` / SendSignal→`SendSignal(SignalKey, from, ButtonPath)` / PlayPresentation→`Debug.LogWarning`(Phase 5))を実行し、`ButtonWire.ClickSe` が設定されていれば `Audio.PlaySe` する。購読解除は `CanvasInstance.WireUnsubscribers`(`List<Action>`)に積んだ unsubscribe デリゲートを `FinalizeClose` で全呼び出しすることで行う
- Skin 解決: `UiSkins.Bind(IAssetRegistry)` を `DDriveRuntimeBootstrap.Build()`(Bind)/`Teardown()`(Unbind)に配線。内部は `registry.TryResolveSync<ControlSkinData>` を使い、未解決でも例外を投げず `null`(Skin 未適用のまま)を返す
- Validation: `ButtonSkinDataValidator`(`Target=ControlSkin`。全状態 Tint.a=0 → Warning、ClickSe 未設定 → Info)を新設。`CanvasDataValidator` に A-4 の残り 3 項目(ButtonWire の `(ButtonPath, Trigger)` 重複 → Warning、`Trigger=LongPress` かつ対象 `UiButton.LongPressSec<=0` → Error、`Action=OpenCanvas` かつ対象 `UiButton.CooldownSec<=0` → Warning。いずれも Prefab 内で対象 UiButton が解決できたときのみ判定する)を追加
- 未実装(後続チケット): `StateVisual.EnterTweenId`/`OnSkinApplied` を実際に使う遷移演出(4-8 UiTween)、Skin 未割当時の Layer 既定 Skin フォールバック(4-7)、CanvasEditor のノードグラフ/パッドシミュレーション(4-3)
- テスト: `Assets/DDrive/Tests/Runtime/UiButtonTests.cs`(13 件。Click/Cooldown/BlockDoubleFire/LongPress/Repeat/DoubleClick/単発 Click のタイムアウト発火/Disabled・Locked の OnDenied/状態遷移/WaitClickAsync/SetVisual)、`ButtonSkinDataValidatorTests.cs`(3 件)、`UiManagerTests.cs` に ButtonWire の SendSignal・CloseSelf・Close 後の購読解除(3 件)と `CanvasDataValidatorTests` に重複 Trigger・LongPressSec=0・CooldownSec=0 の 3 件を追加

---

# Part B — UiTween（イージング・スプライン演出システム）

## B-1. 要件

- 大量のイージング関数 + 多種のスプライン曲線による制御
- 任意のスプライト・ボタン・UI 要素にデザイナーが割当可能
- **出現(Appear) / 常時(Idle) / 消滅(Disappear)** の 3 フェーズそれぞれに設定可能
- コードからも 1 行で再生できる関数群

## B-2. イージングライブラリ（EasingCore — Runtime/Foundation 層）

既存実装（`Katsuya.Tools.SpriteAnimation` の EasingFunction / CubicBezierEvaluator）を Foundation に昇格・拡張する。Editor asmdef から Runtime asmdef へ移設。データとして持つときは常に [17] の `ValueDef` 経由（`ValueDef.Parametric`）とし、Ease 単体をフィールドに露出させない。

```csharp
public enum Ease   // 31種 + カスタム2種
{
    Linear,
    InSine, OutSine, InOutSine,
    InQuad, OutQuad, InOutQuad,
    InCubic, OutCubic, InOutCubic,
    InQuart, OutQuart, InOutQuart,
    InQuint, OutQuint, InOutQuint,
    InExpo, OutExpo, InOutExpo,
    InCirc, OutCirc, InOutCirc,
    InBack, OutBack, InOutBack,        // 既存実装を継承
    InElastic, OutElastic, InOutElastic,
    InBounce, OutBounce, InOutBounce,
    CubicBezier,     // 既存 CubicBezierEvaluator (P1,P2指定, CSS互換)
    CustomCurve,     // AnimationCurve
}
public static class Easing
{
    public static float Eval(Ease e, float t);
    public static float Eval(in EaseDef def, float t);  // Bezier/Curve のパラメータ込み
}
```

### スプライン（値の補間 と 経路移動 の2用途）

```csharp
public enum SplineType { CatmullRom, Bezier, Hermite, BSpline, Linear }

[Serializable]
public class SplinePath      // 2D/3D 経路。UI の飛んでいく演出等
{
    public SplineType Type;
    public Vector3[] Points;         // 制御点 (RectTransform ローカル座標)
    public float[] Tension;          // Hermite/CatmullRom 用 (任意)
    public bool Closed;
    public Vector3 Evaluate(float t);        // 等速化(弧長パラメータ化)済み
    public Vector3 EvaluateRaw(float t);
}
```

- 弧長テーブルを事前計算し、`Evaluate` は等速移動（イージングは t 側に掛ける = 「経路×速度カーブ」の分離）

## B-3. UiTweenData（デザイナーが作る演出単位・ID 管理）

```csharp
public class UiTweenData : AssetDataBase
{
    public TweenTrack[] Tracks;      // 並列実行
    public float TotalDuration;      // 0=自動
}

[Serializable]
public struct TweenTrack
{
    public TweenProperty Property;   // AnchoredPos/Scale/Rotation/Alpha/Color/
                                     // FillAmount/SizeDelta/PathMove(スプライン経路)
    public ValueDef Motion;          // 形（Ease/Bezier/Curve）+ 尺 + Loop の統一表現（[17]）
    public float Delay;
    public TweenFromMode From;       // Current / Absolute / Relative / OffScreen(方向)
    public ParamValue FromValue, ToValue;
    public SplinePath Path;          // Property=PathMove のとき
}
```

## B-3.5 プリセットライブラリ（デザイナーはここから選ぶだけ）

UiTweenData をゼロから組まなくても使えるよう、**定番演出をプリセットとして大量に標準搭載**する。デザイナーの基本操作は「プリセットを選ぶ → 時間と距離だけ調整」。

```csharp
public enum UiPreset
{
    // ── 出現系 (Appear) ──
    FadeIn, SlideInLeft, SlideInRight, SlideInTop, SlideInBottom,
    ScaleIn, PopIn /*OutBack*/, BounceIn, ElasticIn, FlipInX, FlipInY,
    RotateIn, ZoomInFade, SlideFadeInLeft, SlideFadeInRight,
    SlideFadeInTop, SlideFadeInBottom, ExpandWidth, ExpandHeight, TypeFillIn,
    // ── 消滅系 (Disappear) ──
    FadeOut, SlideOutLeft, SlideOutRight, SlideOutTop, SlideOutBottom,
    ScaleOut, PopOut, BounceOut, ElasticOut, FlipOutX, FlipOutY,
    RotateOut, ZoomOutFade, SlideFadeOutLeft, SlideFadeOutRight,
    SlideFadeOutTop, SlideFadeOutBottom, CollapseWidth, CollapseHeight,
    // ── 常時系 (Idle / Loop) ──
    Pulse, Blink, Float /*上下ふわふわ*/, Sway /*左右*/, Breathe /*拡縮*/,
    RotateLoop, ShimmerAlpha, RainbowTint, WobbleLoop,
    // ── 強調系 (単発。通知・エラー・獲得演出) ──
    PunchScale, PunchRotation, Shake, ShakeHard, Flash, ColorFlash,
    HeartBeat, Jelly, Tada, RubberBand, AttentionJump,
}

[Serializable]
public struct UiPresetRef      // ElementFx / ButtonSkin から参照する軽量指定
{
    public UiPreset Preset;
    public float Duration;         // 0 = プリセット既定
    public float Distance;         // Slide系の移動量 (0 = 要素サイズから自動)
    public EaseDef EaseOverride;   // 未指定 = プリセット既定
    public SeId Se;                // 同時再生SE (任意)
}
```

- 実体は **プリセットファクトリ**が UiPresetRef → TweenTrack[] を生成（UiTweenData と同じ実行エンジンに乗る。二重実装しない）
- `ElementFx` の Appear/Idle/Disappear は **UiPresetRef と UiTweenId のどちらでも指定可**（プリセットで足りない凝った演出だけ UiTweenData を自作）
- プロジェクト独自プリセットの追加: `UiPresetCatalog`（SO）に名前 + UiTweenData を登録すると、デザイナーの選択肢に並ぶ

### コード側にも同名の関数を全プリセット分用意

```csharp
public static class UiFx
{
    // プリセット1行呼び出し (全プリセット分を自動生成で用意)
    public static TweenHandle FadeIn(RectTransform t, float sec = 0.25f);
    public static TweenHandle SlideInLeft(RectTransform t, float sec = 0.3f, float distance = 0);
    public static TweenHandle PopIn(RectTransform t, float sec = 0.3f);
    public static TweenHandle Shake(RectTransform t, float strength = 8f, float sec = 0.4f);
    public static TweenHandle Pulse(RectTransform t, float scale = 1.06f, float period = 1.2f);
    // ... (UiPreset 全種に対応。シグネチャは Duration/主要パラメータのみの簡易形)
    public static TweenHandle Play(UiPreset preset, RectTransform t, in UiPresetRef p = default);
    // 連結・同時 (ちょっとした演出シーケンス用)
    public static TweenSequence Sequence();   // .Append(h).Join(h).AppendInterval(0.1f).Play()
}
```

### エディタ: プリセットギャラリー

- CanvasEditor / UiTweenEditor に**プリセット一覧をサムネイル動画（実再生）で表示**。クリックで選択中の要素にその場適用 → 即プレビュー
- フェーズ（出現/常時/消滅/強調）でタブ分け、検索・お気に入り付き
- 「この要素と同じ設定を他の要素にコピー」「Canvas 内一括適用（全ボタンに PopIn 等）」

## B-4. 要素への割当（出現・常時・消滅）

CanvasData に要素単位の演出割当を追加する（[07] §A-2 拡張）。**任意の Image / スプライト / UiButton / パネル**が対象。

```csharp
// CanvasData に追加
public ElementFx[] ElementEffects;

[Serializable]
public struct ElementFx
{
    public string ElementPath;       // Prefab 内要素への相対パス
    public UiPresetRef AppearPreset;   // 基本はプリセット指定 (B-3.5)
    public UiPresetRef IdlePreset;
    public UiPresetRef DisappearPreset;
    public UiTweenId Appear;         // 凝った演出のみ UiTweenData で上書き (優先)
    public UiTweenId Idle;           // 表示中ループ (ふわふわ浮遊・点滅等)
    public UiTweenId Disappear;      // Close / SetActive(false) 時 ★完了までデアクティブ遅延
    public float AppearDelay;        // 順次出現 (リスト項目のスタッガー)
    public SeId AppearSe, DisappearSe;
}
```

- CanvasManager が Open 時: 全 ElementFx の Appear を Delay 順に再生 → 完了で入力受付。Close 時: Disappear を再生し**全完了を待ってから**非アクティブ化
- Idle は表示中 Tick でループ駆動、Pause は Flags に従う
- UiButton の StateVisual.EnterTween も同じ UiTweenData を参照（システムは 1 つ）

## B-5. コード API（プログラマー向け 1 行関数群）

```csharp
public static class UiFx
{
    public static TweenHandle Play(UiTweenId id, RectTransform target);
    public static TweenHandle Appear(RectTransform t)    /* 既定Tween */;
    public static TweenHandle Disappear(RectTransform t);
    // アドホック(データ化するほどでない場面用)
    public static TweenHandle MoveTo(RectTransform t, Vector2 to, float sec, Ease e = Ease.OutCubic);
    public static TweenHandle Scale(RectTransform t, float to, float sec, Ease e = Ease.OutBack);
    public static TweenHandle Fade(CanvasGroup g, float to, float sec, Ease e = Ease.Linear);
    public static TweenHandle MoveAlong(RectTransform t, SplinePath path, float sec, Ease e);
}
// TweenHandle: 世代式struct。await h; h.Kill(); h.Complete(); h.SetSpeed(); h.OnComplete(cb)
```

- 実装は独自 Tween エンジン（構造体ベース・0 alloc・UiFxManager の Tick 駆動）。外部 Tween ライブラリ非依存（Live Tuning・Pause・NetworkTime と統合するため）

## B-6. エディタ

- **UiTweenEditor**: Track リスト編集 + イージングカーブのグラフ表示 + スプライン経路を Scene/プレビュー上でハンドル編集 + 実寸プレビュー（対象 Canvas を読み込んで再生）
- **CanvasEditor 拡張**（[07] §A-4）: 階層ツリーから要素を選択 → Appear/Idle/Disappear に UiTweenId を D&D 割当 → その場で Open/Close シミュレーション再生
- イージング選択 UI は全種のサムネイル曲線を一覧表示（名前でなく形で選べる）

## B-7. Validation

ElementPath 不整合 (Error) / Disappear が無限ループ設定 (Error: Close が終わらない) / PathMove なのに Path 制御点 < 2 (Error) / Appear と Disappear の合計 > 3s (Warning: テンポ低下)。カーブ未設定・尺 ≤ 0・Loop 不整合等の ValueDef 共通検査は [17] §6 に集約
