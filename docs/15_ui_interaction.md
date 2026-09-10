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
- `ButtonSkinData`(`AssetType.ControlSkin` 新設、`SKIN`/`Ui/Skin`/`UiCatalog` を命名・カタログ規約に追加): `ControlSkinData`(abstract)が `Normal/Hover/Pressed/Selected/Disabled/Locked` の `StateVisual` を持つ。`StateVisual.EnterTween`(`AssetId<UiTweenMarker>`)/`EnterPreset`(`UiPresetRef`)は 2026-09-11(4-8+4-11)で実装済み(下記 B-5 実装メモ)。Tint/Scale(`ValueDef`)/OverrideSprite は `UiInteractable.ApplyVisual` が `TargetGraphic`(`Graphic`、Inspector で任意設定)と `RectTransform.localScale` に定数適用する
- ナビゲーション: `UiManager.ApplyNavigation` が `NavNode.Element` の対象を判別し、`Selectable` なら従来通り `Navigation.Mode.Explicit`、`UiInteractable`(Selectable でない)なら新設の `UiNavigation`(`Up/Down/Left/Right` の Transform 参照)を付与する。`UiManager.MoveFocus(Vector2 dir)` は `EventSystem.currentSelectedGameObject` から `UiNavigation` または `Selectable.navigation` のどちらかを読んで移動する最小実装(フォーカス追跡専用のレジストリ等は持たない)
- ButtonWire 実行(4-2 完了): `UiManager.OpenData` が Open 時に `WireButtons` で `ButtonWire.ButtonPath` から `UiButton` を解決し、`Trigger`(Click/DoubleClick/LongPress/Repeat)に応じたイベントへ `Action` を 1 つ購読する(配線の数だけ小さなクロージャを生成するが、Tick 経路ではないため許容。[12]§3)。`Action`(OpenCanvas→`OpenAsync().Forget()` / CloseSelf→`Close(from)` / CloseTop→`CloseTop()` / SendSignal→`SendSignal(SignalKey, from, ButtonPath)` / PlayPresentation→`Debug.LogWarning`(Phase 5))を実行し、`ButtonWire.ClickSe` が設定されていれば `Audio.PlaySe` する。購読解除は `CanvasInstance.WireUnsubscribers`(`List<Action>`)に積んだ unsubscribe デリゲートを `FinalizeClose` で全呼び出しすることで行う
- Skin 解決: `UiSkins.Bind(IAssetRegistry)` を `DDriveRuntimeBootstrap.Build()`(Bind)/`Teardown()`(Unbind)に配線。内部は `registry.TryResolveSync<ControlSkinData>` を使い、未解決でも例外を投げず `null`(Skin 未適用のまま)を返す
- Validation: `ButtonSkinDataValidator`(`Target=ControlSkin`。全状態 Tint.a=0 → Warning、ClickSe 未設定 → Info)を新設。`CanvasDataValidator` に A-4 の残り 3 項目(ButtonWire の `(ButtonPath, Trigger)` 重複 → Warning、`Trigger=LongPress` かつ対象 `UiButton.LongPressSec<=0` → Error、`Action=OpenCanvas` かつ対象 `UiButton.CooldownSec<=0` → Warning。いずれも Prefab 内で対象 UiButton が解決できたときのみ判定する)を追加
- **Codex レビュー対応(2026-09-11、a9600d3)**: 押下中に `Interactable=false` / `SetLocked(true)` になったら `Advance` が hold を打ち切り、LongPress / Repeat / 保留中の Click を発火しない(P1)。同フレーム多重発火防止(`BlockDoubleFire`)が **全コントロール横断**なのは A-2 の明示仕様(1 フレームに複数のボタンが同時に反応して二重遷移するのを防ぐ)であり意図的(P2 は据え置き)。個別に許可したいコントロールは `BlockDoubleFire=false` にする
- `StateVisual.EnterTween`/`EnterPreset` を使った遷移演出は 2026-09-11(4-8+4-11)で実装済み(下記 B-5 実装メモ)。未実装(後続チケット): Skin 未割当時の Layer 既定 Skin フォールバック(4-7)、CanvasEditor のノードグラフ/パッドシミュレーション(4-3)
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

### 実装メモ（2026-09-11、4-8 UiTween エンジン + 4-11 前半 UiPreset）

- 実装場所: `Assets/DDrive/Runtime/UiTween/`(`UiTweenData.cs`(`UiTweenMarker`/`TweenProperty`/`TweenFromMode`/`OffScreenDirection`/`SplinePathDef`/`TweenTrack`/`UiTweenData`)/ `UiTweenManager.cs` / `UiPreset.cs`(`UiPreset`/`UiPresetRef`) / `UiPresetFactory.cs` / `UiFx.cs`(`UiFx`/`TweenHandleExtensions`/`TweenSequence`) / `UiTweenDataValidator.cs`)+ `Assets/DDrive/Editor/Ui/UiTweenEditorWindow.cs`。`Handle<UiTweenMarker>` がそのまま「TweenHandle」に相当する(他種別と同じ規約。専用 struct は作らない)
- **SplinePath は Data 化不可**: `Foundation/Easing/SplinePath` は弧長テーブルをコンストラクタで構築する不変クラスでシリアライズできないため、`TweenTrack.Path` は制御点配列だけを持つ `SplinePathDef`(`Type`+`Points[]`)にした。実際の `SplinePath` は `Play()`(Tick の外)で 1 回だけ構築してインスタンスにキャッシュする
- **0 alloc 方針**: `UiTweenManager` の Instance(class)は Play のたびに `new` せず、内部フリーリスト(`Stack<TweenInstance>`)から使い回す。1 Instance あたり `TweenTrack[8]`/`TrackRuntime[8]` を生成時に 1 度だけ確保し、以後は上書きのみ。`Tick(dt)` は for ループのみで走査し、LINQ・クロージャ・boxing を一切使わない。From/To の解決や `SplinePath` の構築(alloc あり)は `Play()` 直後(Tick の外)でのみ行う。`UiTweenTests.Tick_EightRunningTweens_AllocatesNothing` が `GC.GetAllocatedBytesForCurrentThread` の差分で検証する(理想は 0、Mono のノイズを見込んで 1KB 未満を許容)
- **Tick(dt) は他 Manager と同じく渡された dt をそのまま使う**(`UnityEngine.Time` を読まない)。ticket 上の「既定は unscaled」は、本来は GameLoopDriver が scaled/unscaled 2 系統の dt を配るようになったときに `TweenInstance.UseScaledTime` で選択する想定だが、現状 `GameLoopDriver.Update` は `IAssetManager.Tick` へ単一の(TimeService で scale 済みの)dt しか渡していないため、`UseScaledTime` は今のところ実効的な差を生まない予約フラグに留まる(deferred。GameLoopDriver 側の 2 系統化は本チケットの範囲外)。EditMode/PlayMode テストは `manager.Tick(dt)` を直接呼んで決定的に検証する
- **From の解決**(`TweenFromMode`): `Current`=Play 時点の実値 / `Absolute`=`FromValue` そのまま / `Relative`=実値+`FromValue` / `OffScreen`=`AnchoredPosition` のみ有効(他プロパティは `Current` と同じ挙動にフォールバック)。`OffScreen` は親 RectTransform(無ければ `Screen.width/height`)のサイズと自身のサイズから画面外の位置を計算し、**To 側は `ToValue` を無視して Play 時点の現在位置に戻る**(「画面外→現在位置」という Slide-In の一般的な意味論に合わせた仕様。TweenTrack を手組みする場合はこの前提で使う)
- **プロパティごとの型**: AnchoredPosition/SizeDelta は `ParamValue.VectorValue`(xy)、Scale は Float(一様)/Vector(非一様)のどちらでも可(Float は内部で `(v,v,v)` に正規化)、Rotation/Alpha/FillAmount は `FloatValue`、Color は `ColorValue`。Alpha トラックは対象に `CanvasGroup` が無ければ自動追加、Color/FillAmount は `Graphic`/`Image` が見つからない場合は静かに無視する(警告なし。デザイナーが対象を間違えた場合は Validator の Info で気付ける)
- **UiPreset**(4-11 前半): docs 掲載の enum に `None=0` を先頭追加(EnterPreset の「未設定」を表現するため)。`UiPresetFactory.Build(in UiPresetRef, RectTransform, TweenTrack[] buffer)` が buffer へ書き込んで本数を返す(alloc は呼び出し側の buffer 確保のみ。Build 自体は switch ベースで LINQ 不使用)。既定 Duration/Ease は enum 値でインデックスする配列(`DefaultDurations`/`DefaultEases`)に持たせ、switch 地獄にしない
  - **実装済み**: FadeIn/FadeOut、SlideIn/Out×4 方向、SlideFadeIn/Out×4 方向、ScaleIn/Out、PopIn(OutBack)/PopOut、BounceIn/Out、ElasticIn/Out、ZoomInFade/ZoomOutFade、ExpandWidth/Height、CollapseWidth/Height、Pulse、Blink、Float、Sway、Breathe、RotateLoop、ShimmerAlpha、PunchScale、PunchRotation、Shake、ShakeHard、Flash、HeartBeat
  - **近似実装(`UiPresetFactory.IsApproximation` が true を返す。4-11 残作業で本実装に差し替え)**: FlipInX/Y→ScaleIn、FlipOutX/Y→ScaleOut、RotateIn→ZoomInFade、RotateOut→ZoomOutFade、TypeFillIn→FadeIn(文字送りは TMP 統合が必要)、RainbowTint→ShimmerAlpha(HSV サイクルには Gradient 拡張が必要)、WobbleLoop→Sway、ColorFlash→Flash(Color トラック化が必要)、Jelly/Tada/RubberBand→PunchScale(非均一スケール波形が必要)、AttentionJump→Pulse
  - `UiPresetRef.EaseOverride` は `EaseDef`(NamedEase or CustomBezier)だが、プリセット既定テーブルは `Ease`(名前付き)単体のため、上書き時は `EaseOverride.Ease` のみを見る(CustomBezier での上書きは 4-11 残作業)
  - `UiPresetCatalog`(プロジェクト独自プリセットの登録)は未実装(4-11 残作業)
- **UiFx**: `Bind(UiTweenManager)` / `Play(UiTweenId, RectTransform)` / `PlayData` / `Play(UiPreset, RectTransform, in UiPresetRef)` / 実装済みプリセット分の 1 行関数(`FadeIn`/`SlideInLeft`/`PopIn`/`Shake`/`Pulse` 等) / アドホック(`MoveTo`/`Scale`/`Fade`/`Rotate`/`MoveAlong`) / `Stop`/`StopAll(RectTransform)`/`IsPlaying`/`Progress`/`SetSpeed`/`WaitAsync`。`TweenHandleExtensions` で `h.Stop()`/`h.Complete()`/`h.Progress()`/`h.SetSpeed()`/`h.WaitAsync()` の書き味を提供
- **TweenSequence**: `Sequence().Append(h).Join(h).AppendInterval(sec).Play()` を `UniTask` で実装(Tick のホットパスではないため `List`/`async` を通常通り使う)。`Append` は直前までの並列区間を待ってから新しい区間を開始、`Join` は直前の区間に同時追加、`AppendInterval` は区間完了後に `UniTask.Delay`
- **UiButton 統合**: `ControlSkinData.StateVisual` の `EnterTweenId(ulong)` プレースホルダを `EnterTween(AssetId<UiTweenMarker>)` + `EnterPreset(UiPresetRef)` に置換。`UiInteractable.ApplySkinForCurrentState` が状態遷移のたびに前の状態遷移 Tween を `UiFx.Stop` してから、`EnterTween` があればそれを、無ければ `EnterPreset.Preset != None` のときそれを `UiFx.Play` する(UiFx 未 Bind 時は no-op)
- **Bootstrap**: `DDriveRuntimeBootstrap` に `UiTweenManager UiTweens` を追加、`GameLoop` へ登録、`UiFx.Bind`/`Unbind` を配線。`EditorAnchorRegistry` に `UiTweenData` の収集を追加(エディタプレビューから ID 解決できるように)
- **エディタ**: `UiTweenEditorWindow`(`Tools/D-Drive/Editors/UI Tween`)は SerializedObject を `InspectorElement` で表示するのみ(ウィンドウ内描画なし、ADR-4)。プリセットドロップダウン+「プリセットから Tracks を生成」で `UiPresetFactory.Build` の結果を `Undo.RecordObject` 付きで `Tracks` に書き込む。「確認用シーンに配置」が DontSave の Canvas+Image を生成し、「▶ 再生」が `EditorApplication.update` で駆動する実 `UiTweenManager` を使って実際に再生する。カーブ/スプラインのハンドル編集とプリセットギャラリー(実再生サムネ)は 4-10/4-12 で拡張する
- **テスト**: `Assets/DDrive/Tests/Runtime/UiTweenTests.cs`(MoveTo/Scale(OutBack オーバーシュート)/Fade(CanvasGroup 自動追加)/PathMove/Delay/無限ループ+Stop/Stop(complete:true)/2 トラック並列/WaitAsync/OnPause(Data.Flags 追従 + アドホックは追従しない)/未登録 ID のプレースホルダ/プリセット(FadeIn/SlideInLeft/IsApproximation)/Validator 3 件/UiButton×EnterPreset 統合/0 alloc)
- **既知の制約・deferred**: `EaseOverride` の CustomBezier 上書き未対応、近似プリセット群の本実装、`UiPresetCatalog`、プリセットギャラリー(4-12)、`UiTweenEditor` のカーブ/スプラインハンドル編集(4-10)、GameLoopDriver の scaled/unscaled dt 二系統化(UseScaledTime を実効化するため)

### 実装メモ(2026-09-11、4-9 ElementFx + 4-7 残り レイヤー既定)

- 実装場所: `CanvasData.cs`(`ElementFx` 構造体 + `ElementEffects` 追加)/ `UiLayerSettings.cs`(新規。`UiLayerDefaultEntry[]`)/ `UiManager.cs`(ElementFx ランタイム一式)/ `UiInteractable.cs`(`HasExplicitSkin`/`ApplyDefaultSkin`/`SetDefaultSe`)/ `UiButton.cs`(SE フォールバック + Selected→HoverSe)/ `CanvasDataValidator.cs`(ElementFx 検査)/ `Assets/DDrive/Editor/Canvas/CanvasElementFxCollector.cs`(新規)+ `CanvasEditorWindow.cs` 拡張
- **解決順**: Appear/Idle/Disappear は「id(`UiTweenId`が有効) → Preset(`UiPresetRef.Preset != None`) → レイヤー既定(`UiLayerSettings.DefaultAppear`/`DefaultDisappear`。Idle にはレイヤー既定は無い)」の順に 1 つだけ採用する。3 つとも未設定ならその区間は何もしない(即完了扱い)
- **スタッガー(`AppearDelay`)**: `CanvasInstance.ElementFxElapsed`(Open からの経過秒、Closing 中は増やさない)を `UiManager.Tick` が積み、`ElementFxElapsed >= Def.AppearDelay` になった要素から順に Appear を開始する(クロージャ無し、`List<ElementFxRuntime>` を for で走査するだけ)
- **入力ゲート**: `CanvasInstance.PendingAppearCount`(Appear を持つ要素の数。「id/Preset/レイヤー既定のいずれかが有効」で `SetupElementFx` が事前カウント)を `RecomputeBlocking` が見て、`> 0` の間はモーダルブロックとは独立に `CanvasGroup.interactable/blocksRaycasts` を false にする。要素の Appear が完了するたびに `Tick` が `RecomputeBlocking` を呼び直す(`UiManager.IsOpening(handle)` で外部からも問い合わせ可能)
- **Idle**: Appear 完了(即完了含む)の直後に 1 回だけ開始してループ再生し、Close 開始(`StartAllDisappearFx`)で `UiTweenManager.Stop` する(complete 無しでその場停止)
- **Close の完了待ち**: `Close()` は ①`StartAllDisappearFx`(全要素の Disappear を一斉に開始し `PendingDisappearCount` を確定)→ ②`StartTransition(CloseTransition)` の順で呼ぶ。`CompleteTransition(isOpen:false)` は実際に `FinalizeClose` を呼ばず `CloseTransitionCompleted=true` を立てて `TryFinalizeClose` に委ねる。`TryFinalizeClose` は `CloseTransitionCompleted && PendingDisappearCount<=0` の両方が揃って初めて `FinalizeClose` する(二重呼び出しは `_instances.TryGet` で無害化)。ElementEffects が空の CanvasData は従来どおり CloseTransition 完了と同時に閉じる(`PendingDisappearCount` が最初から 0 のため)
- **`StopAll`**: 演出を待たず `UiTweenManager.Stop(handle, complete:true)`(Idle は complete 無し)で全 ElementFx を畳んでから `FinalizeClose` する。Handle が既に無効でも `UiTweenManager.Stop`/`IsPlaying` は no-op なので判定を省いて全部呼んでよい
- **欠損 `ElementPath`**: `SetupElementFx` が `(CanvasData, path)` 単位で 1 回だけ `Debug.LogWarning` し、その行はスキップして継続する(例外にしない)
- **4-7 残り(レイヤー既定)**: `UiLayerSettings`(`ScriptableObject`。AssetId を持たないプロジェクト単位設定。`DDriveRuntimeBootstrap.LayerSettings` を Inspector 直参照)を `UiManager.SetLayerSettings` で受け取り、Open 時 `ApplyLayerDefaults` が Prefab 内の全 `UiInteractable` へ `SetDefaultSe(click, hover, denied)` を配り、`SkinId` 未設定 かつ `HasExplicitSkin==false` の対象にだけ `ApplyDefaultSkin(ControlSkinData)` を当てる(`SetVisual` を明示的に呼ぶと `HasExplicitSkin=true` になりフォールバック対象から外れる)。`UiButton` は `ButtonSkin?.ClickSe/HoverSe/DeniedSe` が Invalid のときだけ `DefaultClickSe/DefaultHoverSe/DefaultDeniedSe` にフォールバックする(`ResolveSe` ヘルパ)
- **Selected→HoverSe**: パッド/キーボードでのフォーカス移動(`ControlState.Selected`)もポインタ Hover と同じ `HoverSe` を再生するようにした(`UiButton.Focus` が Hover 状態から遷移してきた場合は鳴らさない。Hover 中に Selected へ来ても既にポインタの Hover 音が鳴っているため)
- **エディタ**: `CanvasEditorWindow` に「要素を自動収集(Image / UiButton / パネル)」(`CanvasElementFxCollector.CollectMerged`。Graphic+UiInteractable のパスを既存行を残したまま追加)と「一括適用: 全ボタンに <preset>」(`ApplyPresetToButtons`。全 UiButton の `AppearPreset` を一括設定、行が無ければ追加)を追加。「確認用シーンで開く」プレビューは `UiTweenManager` を新規生成して `UiManager.SetTweenManager` に渡し、`EditorApplication.update` から両方の `Tick` を駆動する(ADR-4。ウィンドウ内描画は無し)
- **テスト**: `Assets/DDrive/Tests/Runtime/ElementFxTests.cs`(Appear プリセットの再生+入力ゲート、AppearDelay スタッガー、Idle 開始/Close 停止、Close の Disappear 完了待ち、StopAll 即時完了、欠損 ElementPath の警告、レイヤー既定 Skin/SE、Validator 5 件)
- **既知の制約・deferred**: `ElementFx` のノードグラフ/タイムライン編集(4-3 の CanvasEditor 拡張と合わせて検討)、プリセットギャラリーからの ElementFx 直接プレビュー(4-12)

### 実装メモ(2026-09-11、4-11 完了 / 4-10)

- **4-11 残り(UiPresetCatalog)**: `Assets/DDrive/Runtime/UiTween/UiPresetCatalog.cs`(新規)。`UiPresetCatalog` は `AssetDataBase` ではない単なる `ScriptableObject`(`[CreateAssetMenu("D-Drive/Ui/Ui Preset Catalog")]`、AssetId を持たない)。`Entry{ Name, Tween(UiTweenData), Category }` の配列を持つだけで、**実行時にこのアセット自体を参照することは一切ない**(ランタイムは常に `UiTweenId`(`UiTweenData.Id`)で再生する。カタログは「デザイナーがエディタで選ぶための選択肢リスト」に過ぎない、と docs 冒頭に明記)。収集/検証は `Assets/DDrive/Editor/Ui/UiPresetCatalogUtility.cs`(新規): `Collect()` がプロジェクト内の全 `UiPresetCatalog`(`/Tests/` 配下は除外)から `(name, tween)` のリストを集約し、`Validate(catalog)` が「Name 空」「Tween 未設定」「Tween.Id==0(未採番)」を検出する
- 前回(前半実装)で `IsApproximation` が true だった残り約 11 種(FlipInX/Y・FlipOutX/Y・RotateIn/Out・TypeFillIn・RainbowTint・WobbleLoop・ColorFlash・Jelly・Tada・RubberBand・AttentionJump)は 9969aaf で本実装済み(このドキュメント更新時点で `UiPresetFactory.IsApproximation` は常に false)。`TweenProperty` に `RotationX`/`RotationY`(Flip 系)/`ColorHue`(RainbowTint 用。RGB 直線補間だと彩度が落ちるため HSV(H,1,1) を毎フレーム計算する専用プロパティ)を追加。`UiFx` に残り全プリセット分の 1 行関数を追加、`UiPresetTests`(新規)で追加分を検証済み(このチケット以前に完了。詳細は 9969aaf のコミットログ参照)
- **UiTweenEditor(4-10)**: `Assets/DDrive/Editor/Ui/TweenTrackSummary.cs`(新規。`Describe(in TweenTrack)` が `"<Property> / <Mode> / <duration>s / <loop> / delay <d>s"` を組み立てる純関数)+ `Assets/DDrive/Editor/Ui/SplineHandleMath.cs`(新規。`LocalToWorld`/`WorldToLocal` の Transform 変換だけを切り出した純関数、テスト対象)。`UiTweenEditorWindow` に「カーブ一覧」(Track ごとに 1 行。`TweenTrackSummary.Describe` のラベル + `IMGUIContainer` で `ValueDef.Evaluate` を 64 サンプルして `Handles.DrawAAPolyLine`(`Handles.BeginGUI/EndGUI` 越し)で描く小さな曲線。クリックで選択、選択中 Track は `PropertyField` で `Tracks[i]` を直接編集)を追加。スプライン(`Property=PathMove`)を選択中は SceneView に制御点の `Handles.PositionHandle` を出し(`SceneGuiOwner` で他の D-Drive エディタとの描画権調停に参加。ワールド座標は「確認用シーンに配置」した `_previewTarget` のローカル座標系から変換)、ドラッグで `SplinePathDef.Points` に `Undo.RecordObject`+`EditorUtility.SetDirty` 付きで書き戻す。経路は `SplinePath.Evaluate` を 32 点サンプルして `Handles.DrawAAPolyLine` で描画。「＋点を追加」(末尾に最後の点+(50,0,0))/「−最後の点を削除」ボタンを追加。ウィンドウが破棄/対象変更されても SceneView コールバックは `OnDisable` で解除する(`SceneView.duringSceneGui -=`)
- 「プリセットから Tracks を生成」は `EnumField` から `DropdownField` へ変更し、`UiPreset` 全メンバー名 + `UiPresetCatalogUtility.Collect()` の結果を `"[Catalog] <name>"` として選択肢に並べる(catalog 選択時はそのまま `UiTweenData.Tracks` をコピー、プリセット選択時は従来通り `UiPresetFactory.Build`)
- **CanvasEditor(4-10)**: `ElementFx` 割当セクションを新設(`CanvasEditorWindow.RebuildElementFxAssignments`/`BuildPhaseRow`)。各行の Appear/Idle/Disappear ごとに `PopupField<string>`(選択肢は「なし」+ 組み込み `UiPreset` 全種 + カタログ全エントリ)+ `ObjectField<UiTweenData>`(直接指定用)を出す。「なし」は Preset/Id 両方をクリア、組み込みプリセット選択は `XxxPreset.Preset` に書き込み Id をクリア、カタログ選択または ObjectField への直接ドロップは `AssetId<UiTweenMarker>(tween.Id, AssetType.UiTween)` を `Appear`/`Idle`/`Disappear` に書き込み Preset をクリアする(ランタイムの解決優先順位 id→Preset→レイヤー既定と整合)。全て `Undo.RecordObject`+`EditorUtility.SetDirty` 経由。「この要素の設定を他の要素へコピー」ボタンを各行に追加、実体は `CanvasElementFxCollector.CopyPhases(ref ElementFx[] rows, int from)`(配列操作のみを切り出し、テスト対象)
- **テスト**: `Assets/DDrive/Tests/Editor/UiTweenEditorTests.cs`(新規、7 件)。`UiPresetCatalogUtility.Validate`(空 Name/Tween 未設定/Id 未採番の検出、正常時 0 件)、`TweenTrackSummary.Describe`(Parametric/Curve の書式)、`SplineHandleMath` の World⇄Local 往復(位置・回転・スケール付き RectTransform)、`CanvasElementFxCollector.CopyPhases`(全行へのコピー、無効な `from` は no-op)
- **既知の制約・deferred**: `UiPresetCatalogUtility.Collect()` は `AssetDatabase` 依存のためエディタ専用(ランタイムから呼ばない。ランタイムは常に `UiTweenId` で解決)。プリセットギャラリー(実再生サムネ・タブ・お気に入り)は 4-12 のまま。カーブ一覧の曲線描画は「形」のみで軸ラベル・スクラブ操作は未実装(必要になれば追加)

## B-6. エディタ

- **UiTweenEditor**: Track リスト編集 + イージングカーブのグラフ表示 + スプライン経路を Scene/プレビュー上でハンドル編集 + 実寸プレビュー（対象 Canvas を読み込んで再生）
- **CanvasEditor 拡張**（[07] §A-4）: 階層ツリーから要素を選択 → Appear/Idle/Disappear に UiTweenId を D&D 割当 → その場で Open/Close シミュレーション再生
- イージング選択 UI は全種のサムネイル曲線を一覧表示（名前でなく形で選べる）

## B-7. Validation

ElementPath 不整合 (Error) / Disappear が無限ループ設定 (Error: Close が終わらない) / PathMove なのに Path 制御点 < 2 (Error) / Appear と Disappear の合計 > 3s (Warning: テンポ低下)。カーブ未設定・尺 ≤ 0・Loop 不整合等の ValueDef 共通検査は [17] §6 に集約
