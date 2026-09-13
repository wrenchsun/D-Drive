# 18. UI コントロール（UiInteractable 共通基底 / UiSlider）

関連: [15_ui_interaction.md](15_ui_interaction.md) / [07_canvas_prefab.md](07_canvas_prefab.md) / [17_value_definition.md](17_value_definition.md) / [03_audio.md](03_audio.md) / [16_camera_haptics.md](16_camera_haptics.md)

対応要件: [00] FR-18

---

## 1. 方針

スライダーは音量・感度設定・HP バー・スタミナゲージ・キャラメイクなど実装頻度が高い。uGUI の `Slider` を使うと、UiButton を新規実装したのと同じ理由で破綻する。

- ゲームパッド操作（左右キー Step・長押しリピート・微調整修飾）が毎回アドホック実装になる
- 値変更のたびに `OnValueChanged` が発火し、SE やセーブ処理が過剰に走る（スロットル制御が毎回手書き）
- 応答曲線（音量スライダーは対数的に動かしたい等）をデータとして持てない
- 状態演出・SE・Locked が Button と別実装になり、プロジェクト内で操作感が揃わない

そこで **UiButton と UiSlider の共通部分を `UiInteractable` 基底に集約**し、その上に UiSlider を新規実装する。「Unity 標準の UI コントロールを再定義する」という D-Drive（Re:）の思想の中核をなす設計である。

---

# Part A — UiInteractable（共通基底）

## A-1. 責務の切り分け

```
UiInteractable (abstract)                ← 状態機械 / 入力受付 / Skin / Locked / ナビゲーション
  ├ UiButton   ([15] Part A)             ← Click / DoubleClick / LongPress / Repeat
  ├ UiSlider   (本書 Part B)             ← 連続値のドラッグ・Step 操作
  └ (将来) UiToggle / UiStepper / UiScrollbar / UiTabBar
```

```csharp
public abstract class UiInteractable : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler,
    IUiNavigable                              // 独自ナビゲーション（[07] NavNode）対応
{
    public ControlState State { get; }        // Normal/Hover/Pressed/Selected/Disabled/Locked
    public bool Interactable { get; set; }
    public void SetLocked(bool locked, string reasonKey = null);  // 灰色 + 理由ツールチップ

    public float CooldownSec { get; set; }    // 発火後の不感時間
    public bool  BlockDoubleFire { get; set; }// 同フレーム多重発火防止（全コントロール横断）

    public Observable<bool> OnPress { get; }  // down=true / up=false
    public Observable<bool> OnHover { get; }
    public Observable<bool> OnFocus { get; }  // パッド選択
    public Observable<ControlState> OnStateChanged { get; }

    public void SetVisual(ControlSkinId id);
    protected abstract void OnSkinApplied(in StateVisual v);
}
```

- `ControlState` は Button / Slider で共通の状態列挙（コントロール固有の名前を持たせない）
- Skin は `ControlSkinData` を基底とし、`ButtonSkinData`（[15] A-3）/ `SliderSkinData`（本書 B-2）が派生する
- 状態遷移演出は [15] Part B の Tween システムを使用。`StateVisual` の数値は [17] の `ValueDef` で定義する

### 実装メモ（2026-09-11、4-6）

`UiInteractable`/`UiButton` の実装詳細・R3 非導入の扱い・ナビゲーション統合・ButtonWire 実行は [15_ui_interaction.md](15_ui_interaction.md) の実装メモを参照(本書と実装は共通)。要点のみ:

- `Observable<T>` は `event Action`/`Action<bool>`/`Action<ControlState>` で代替(R3 未導入)
- `ControlSkinData`(abstract、`Assets/DDrive/Runtime/Ui/ControlSkinData.cs`)は `AssetIdDefinition` を持たない(codegen は具象型のみ走査するため、派生の `ButtonSkinData` 側に付与する。`SliderSkinData` を追加する際も同様に派生側へ付ける)
- `IUiNavigable` は Selectable でない `UiInteractable` 向けの最小インタフェース。実際のフォーカス移動は `UiNavigation`(新設 MonoBehaviour)+ `UiManager.MoveFocus(Vector2)` が担う

---

# Part B — UiSlider

## B-1. コンポーネント構成

```csharp
public class UiSlider : UiInteractable,
    IDragHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler, IMoveHandler
{
    // ── 値 ──
    public float Min = 0f, Max = 1f;
    public float Value { get; set; }            // set は通知あり
    public void SetValueSilent(float v);        // 通知なし（初期化・外部同期用）
    public float NormalizedValue { get; set; }  // 0..1

    // ── 刻み ──
    public float Step = 0f;                     // 0 = 連続値
    public bool  WholeNumbers = false;
    public int   Notches = 0;                   // >0 でノッチ表示 + 吸着
    public float SnapThreshold = 0.02f;         // ノッチ吸着の効き幅（正規化）

    // ── 入力 ──
    public SliderDirection Direction;           // LeftToRight/RightToLeft/BottomToTop/TopToBottom
    public bool  JumpOnTrackClick = true;       // トラック直押しで即ジャンプ（false=ページ送り）
    public float PadStepAmount = 0.05f;         // パッド左右キー 1 回の移動量
    public float PadRepeatDelaySec = 0.4f;      // 長押しリピート開始
    public float PadRepeatIntervalSec = 0.06f;
    public float FineStepMultiplier = 0.2f;     // 微調整修飾（Shift/L2 等）時の倍率
    public bool  WheelEnabled = true;

    // ── 応答・追従（[17] ValueDef）──
    public ValueDef Response;                   // 入力位置(0..1) → 正規化値(0..1)
    public ValueDef FollowMotion;               // 表示値が実値に追いつく動き（0=即時）

    // ── 通知制御 ──
    public float ChangeThrottleSec = 0f;        // 0=毎フレーム通知
    public bool  NotifyOnlyOnCommit = false;    // true=ドラッグ終了時のみ OnValueChanged

    // ── イベント（R3）──
    public Observable<float> OnValueChanged { get; }   // スロットル適用後
    public Observable<float> OnCommit       { get; }   // ドラッグ / 操作の終了（確定）
    public Observable<Unit>  OnDragBegin    { get; }
    public Observable<Unit>  OnDragEnd      { get; }
    public Observable<int>   OnNotchPassed  { get; }   // ノッチ通過（刻み音・触覚に使う）
    public Observable<bool>  OnLimitReached { get; }   // 端に到達（true=Max / false=Min）

    // ── 関数 ──
    public UniTask<float> WaitCommitAsync(CancellationToken ct);
    public void SetRange(float min, float max, bool keepNormalized = true);
    public void AnimateTo(float value, in ValueDef motion);   // ゲージ演出用
    public void SetVisual(SliderSkinId id);
}
```

### 応答曲線（Response）

入力位置と値の関係を線形以外にできる。「音量は対数」「感度は低域を細かく」といった調整が **コードでなくデータ**で完結する。

| 用途 | Response の設定 |
|---|---|
| 通常（線形） | 未設定 = 線形（Validation は Info） |
| 音量スライダー | `Mode=Parametric, Ease=InQuad`（つまみ下側で細かく動く） |
| 感度・難易度 | `Mode=Curve` で任意カーブ |

- 逆変換（値 → つまみ位置）は数値解で解く。そのため `Response` は **単調増加であることを Validation で強制**する（非単調はつまみ位置が一意に決まらない = Error）

### 追従演出（FollowMotion）

`Value` は即座に変わるが、フィル / ハンドルの **表示** は `FollowMotion` に従って追いつく。

- `Mode=Constant, Value=0` → 即時追従（設定スライダーの既定）
- `Mode=Parametric, Ease=OutCubic, Duration=0.25` → HP バーが滑らかに減る
- ダメージ表現の「白い遅延バー」は、Skin 内の 2 本目のフィル（`DelayFill`）の `FollowMotion` を遅くするだけで実現できる（専用スクリプト不要）

## B-2. SliderSkinData

```csharp
public class SliderSkinData : ControlSkinData      // Skin 基底は ButtonSkinData と共通（A-1）
{
    [Header("パーツ")]
    public StateVisual Track, Fill, Handle, DelayFill;   // 状態別の見た目
    public Sprite NotchSprite;
    public bool   HideHandleOnGamepad;                   // パッド時はフォーカス枠のみ
    public Vector2 ExtraHitPadding;                      // タッチ用ヒット領域拡張

    [Header("SE")]
    public SeId GrabSe, ReleaseSe, NotchSe, LimitSe, DeniedSe;
    public float NotchSeMinIntervalSec = 0.04f;          // 高速ドラッグ時の音の詰まり防止

    [Header("触覚")]
    public HapticId NotchHaptic, LimitHaptic;            // [16] Part B と連携（任意）
}
```

> **実装メモ(2026-09-14、5-2b)**: 実装では `NotchHaptic`/`LimitHaptic` ではなく `NotchHapticId`/`LimitHapticId`(`ulong`)というフィールド名・型のまま(シリアライズ形式の変更は事前確認が必要なため、5-2b では型を変えず接続だけ行った。要判断: `HapticId`(`AssetId<HapticMarker>`)への置換は 5-2c 側で判断してほしい)。`UiSlider` の `UpdateNotchTracking`/`UpdateLimitTracking` から、値が非 0 のときだけ `new AssetId<HapticMarker>(id, AssetType.Haptics)` を組み立てて `Haptics.Play(...)` する。

- ノッチ音・端到達音を Skin 側に持たせることで、**プロジェクト全体のスライダーの操作感が 1 箇所で揃う**
- `NotchSeMinIntervalSec` により、素早くドラッグしても SE が飽和しない（[03] の `CooldownSec` と同じ思想）

## B-3. 入力仕様

| 入力 | 挙動 |
|---|---|
| ハンドルドラッグ | ポインタ位置 → `Response` 経由で値算出。`Step`/`Notches` があれば吸着 |
| トラッククリック | `JumpOnTrackClick=true` で即ジャンプ + ドラッグ継続、false でページ送り（1 Step 分） |
| ホイール | `WheelEnabled` 時に 1 Step。スクロールビュー内では親を優先（`IScrollHandler` で吸収判定） |
| パッド左右（`IMoveHandler`） | `PadStepAmount` 分移動。長押しで `PadRepeatDelaySec` 後に `PadRepeatIntervalSec` 間隔でリピート |
| 微調整修飾 | 修飾入力中は移動量 × `FineStepMultiplier` |
| タッチ | ハンドル外を押しても掴めるヒット領域拡張（`ExtraHitPadding`） |

- フォーカス移動（上下キー）は [07] の `NavNode` に従う。**左右キーはスライダー操作に消費される**ため、`NavNode.Left/Right` へは端到達時のみ抜ける（`EscapeOnLimit` フラグで切替）。実装は `UiSlider.OnMove`(2026-09-12 に `UiInteractable.OnMove` の override として整理。[15] 実装メモ参照)
- `Locked` 状態でのドラッグは `DeniedSe` を鳴らして無視（理由ツールチップは UiButton と同じ機構）

## B-4. CanvasData 配線（[07] §A-2 の `SliderWire`）

```csharp
[Serializable]
public struct SliderWire
{
    public string ElementPath;          // Prefab 内 UiSlider への相対パス
    public SliderTrigger Trigger;       // Changed / Commit / NotchPassed / LimitReached
    public UiAction Action;             // SendSignal / SetOption / PlayPresentation
    public string SignalKey;            // 例 "option/bgm_volume" → SignalArgs.Float に値が入る
    public OptionKey Option;            // Action=SetOption のとき（下記）
    public float ThrottleSec;           // Changed の通知間引き
}
```

### 標準オプションへの直結（SetOption）

設定画面はほぼ全てのプロジェクトで必要になるため、**コード不要で完結する経路**を用意する。

| OptionKey | 接続先 |
|---|---|
| MasterVolume / BgmVolume / SeVolume / VoiceVolume | `Audio.SetBusVolume`（[03] §3） |
| ShakeScale | `CameraFx.SetGlobalScale`（[16] A-2）★ FR-17.4 アクセシビリティ要件の受け皿。✅ 5-2 で接続済み(0 で完全に無揺れ) |
| HapticScale | `Haptics.SetGlobalScale`（[16] B-2）。✅ 5-2b で接続済み(0 で出力 0) |
| UiSpeedScale | UiTween の `GlobalScale`（[17] §3） |

- 値の保存・読込は `OptionStore`（Foundation の軽量 SO + セーブデータ抽象）が担当。Canvas 側は Open 時に現在値でスライダーを初期化する（`SetValueSilent`）
- これにより **「音量設定画面」がスクリプト 0 行で完成する**。[00] §1 の思想（デザイナー完結）を最も端的に示す実例であり、M4 デモの合格基準（[12] §5）に含まれる

## B-5. ElementFx との関係

スライダーも通常の UI 要素であり、[15] §B-4 の `ElementFx`（Appear / Idle / Disappear + スタッガー + SE）がそのまま適用できる。追加実装は不要。

- 設定画面を開くと、各スライダーが `SlideFadeInLeft` で順次出現 → 操作可能。閉じると `FadeOut` 完了後に非アクティブ化
- 対象は `ElementPath` 単位なので、トラック / ハンドルなど内部パーツ単位の演出割当も可能

## B-6. 専用エディタ（SliderEditor）

| 機能 | 内容 |
|---|---|
| 実操作プレビュー | プレビュー Canvas 上で実際にドラッグ・パッド操作。SE・触覚（Test on Pad）込みで確認 |
| 応答曲線グラフ | `Response` を [17] の共通 Drawer で編集。横軸=つまみ位置 / 縦軸=値。実測点をグラフ上に表示 |
| ノッチ可視化 | `Notches` / `Step` の刻み位置をトラック上にオーバーレイ表示。吸着幅も帯で表示 |
| 追従比較 | `FollowMotion` 違いを 2 ペイン同期再生で比較（[09] §2 の比較表示を利用） |
| Skin プレビュー | 全状態（Normal〜Locked）を一覧で並べて確認。Skin 差し替えで全スライダーの見た目が変わることを確認 |
| プリセット | 「音量」「感度」「HP バー」「スタミナ」「キャラメイク」の 5 種を標準同梱。選ぶだけで Response/Step/Skin/SE が入る |

### 実装メモ（2026-09-11、4-17 SliderEditor）

> - `Assets/DDrive/Editor/Ui/SliderEditorWindow.cs`（`Tools/D-Drive/Editors/Slider`、`[DataEditor(typeof(SliderSkinData), "Slider Editor で開く")]`）: 対象はシーン / プレハブステージ上の `UiSlider`（「確認用シーンにサンプルを配置」で DontSave のサンプルを作れる）。応答曲線グラフとノッチ可視化は**静的描画**（IMGUIContainer）、実操作 / 追従比較（2 体目を配置）/ Skin プレビュー（全状態を並べる）は**シーン上の実 UiSlider を駆動**して Game ビューで確認する（ウィンドウ内での再生描画はしない、[09] §2）。`SliderPresets`（音量 / 感度 / HP バー / スタミナ / キャラメイク）を Slider Skin エディタと共用、`SliderEditorMath` に曲線サンプリング・ノッチ位置の純関数。
> - 自動テスト: `SliderEditorTests`(EditMode 237 / PlayMode 466 green、Unity 再起動後に確認)。人による確認手順は [23_manual_verification_2026-09-11.md](23_manual_verification_2026-09-11.md)

## B-7. Validation

| 検査 | 重度 |
|---|---|
| Min ≥ Max | Error |
| `Response` が単調増加でない | Error（つまみ位置が一意に決まらない） |
| `Step` が (Max−Min) を割り切れない | Warning（端に到達できない刻み） |
| `Notches` > 0 かつ `Step` > 0 で両者が不一致 | Error |
| `WholeNumbers=true` かつ `Step` が非整数 | Error |
| `Skin` 未割当 | Warning（Layer 既定 Skin にフォールバック） |
| `SliderWire.Trigger=Changed` で `ThrottleSec=0` かつ Action が重処理（PlayPresentation 等） | Warning |
| `Action=SetOption` で `OptionKey` 未設定 | Error |
| `FollowMotion` が Loop 設定 | Error（表示値が収束しない） |
| `ElementPath` 不整合 | Error |
| 左右 `NavNode` 設定ありかつ `EscapeOnLimit=false` | Warning（フォーカスが抜けられない） |

カーブ未設定・尺 ≤ 0 等の ValueDef 共通検査は [17] §6 に集約。

### 実装メモ(2026-09-11、4-14 / 4-15 / 4-16 / 4-18)

- 実装: `Assets/DDrive/Runtime/Ui/UiSlider.cs`(+`SliderDirection`)、`SliderSkinData.cs`(+`SliderSkinDataValidator.cs`)、`OptionStore.cs`(`OptionKey` / `IOptionStorage` / `PlayerPrefsOptionStorage` / 静的ファサード `Options` を同居)、`UiSliderValidation.cs`(静的検査、`CanvasDataValidator` と将来の SliderEditor 4-17 が共用)。エディタは `Assets/DDrive/Editor/Ui/SliderSkinEditorWindow.cs`
- R3 は未導入のため `OnValueChanged`/`OnCommit`/`OnDragBegin`/`OnDragEnd`/`OnNotchPassed`/`OnLimitReached` は全て素の `event Action<T>`。`WaitCommitAsync` だけ UniTask(`UiButton.WaitClickAsync` と同じパターン)
- テスト用フック(`BeginDragAt`/`DragTo`/`EndDrag`/`TrackClickAt`/`Wheel`/`Move`/`MoveRelease`/`Advance`)は `UiButton` の `Press`/`Release`/`Advance` と同じ設計で、EventSystem 無しで PlayMode 同期テストから直接駆動できる。`Update()` は `Advance(Time.unscaledDeltaTime)` を呼ぶだけの薄いラッパー
- **Response(応答曲線)**: `Mode=Constant`(未設定既定を含む)は線形として扱う。ドラッグ位置→値は `Response.Evaluate(p)` を直接使うが、値→ハンドル表示位置の逆変換は解析的に解けない(任意のカーブ/パラメトリック曲線を許容するため)ので、単調増加を前提に **16 分探索(二分探索)** で近似する(`InverseResponse`)。Validation(`UiSliderValidation`)は 32 サンプルで非単調を検出する
- **FollowMotion / DelayFill**: 表示だけを追従させる仕組みは `UiTweenManager` の `EvaluateShape`(Constant→shape=1=即時反映)と同じ考え方を流用した簡易実装を `UiSlider` 内に持つ(専用の Tween インスタンスは使わない。ノッチ可視化やグラフ表示を伴う本格的な演出比較は SliderEditor 4-17 に委ねる)。`DelayFill` は `DelayFollowMotion` が未設定なら `FollowMotion` を共有する
- **AnimateTo**: `Value`(実値)自体を `ValueDef` の尺/イージングに沿って動かす(HP バーの減少演出等)。表示だけを追従させる `FollowMotion` とは独立した機構で、`Advance` の中で両方が並行して進む
- **通知制御**: `NotifyOnlyOnCommit` はドラッグ中の `SetValueInternal(commit:false)` 呼び出しでは `OnValueChanged` を保留し、`EndDrag`(`commit:true`)でまとめて 1 回発火する。`ChangeThrottleSec` は保留値を持ち、`Advance` のタイマーが切れた時点でまとめて発火する(いずれも `OnCommit` は常に即時)
- **SliderWire + OptionStore**: [07_canvas_prefab.md] A-2/A-3 の 2026-09-11 追記を参照。`UiManager.WireSliders` が `Open` 時に `OptionStore` の現在値で初期化し、`Trigger` ごとに購読 → `Action=SetOption`/`SendSignal`/`PlayPresentation(Phase5警告)` を実行する
- **音量バス**: `Audio.SetBusVolume`([03])は未実装のため、`OptionStore` は `MasterVolume` のみ `AudioListener.volume` に直結し、`BgmVolume`/`SeVolume`/`VoiceVolume` は値を保持した上で `ExternalApplier` フック(未設定なら 1 回だけ警告)に委ねる。`UiSpeedScale` は `UiTweenManager.GlobalSpeed`(新設)に反映する。`ShakeScale`/`HapticScale` は 5-2/5-2b で `CameraFxManager.SetGlobalScale`/`HapticsManager.SetGlobalScale` に接続済み(`OptionStore.CameraFx`/`Haptics` フィールドを `DDriveRuntimeBootstrap` が Bind する。[16_camera_haptics.md] 実装メモ参照)
- **触覚**: `SliderSkinData.NotchHapticId`/`LimitHapticId` は `ulong` のプレースホルダで、[16] Part B の `HapticId` 実装時に置換する(現状は未使用)
- **Skin の AssetIdDefinition**: `SliderSkinData` は `ButtonSkinData` と同じ `AssetType.ControlSkin`/`ControlSkinMarker` を使うが、`ConstantsClassName` は `"SLIDERSKINID"`(`ButtonSkinData` は `"SKINID"`)にした。`AssetIdGenerator` は `ConstantsClassName` ごとに別の `static class` を生成するため、同名にすると生成コードで `CS0101`(クラス重複定義)になる
- 繰り延べ: 本格的な SliderEditor(応答曲線グラフ・ノッチ可視化オーバーレイ・追従比較・Skin プレビュー一覧、4-17)、Audio バス別音量([03] `Audio.SetBusVolume`、Phase 5)、`SliderSkinData.NotchHapticId`/`LimitHapticId` の `HapticId` 型への置換([16] Part B、5-2c で判断)
- テスト: `Assets/DDrive/Tests/Runtime/UiSliderTests.cs`(`UiSliderTests` 19 件 + `OptionStoreTests` 4 件 + `SliderSkinDataValidatorTests` 2 件 + `UiSliderValidationTests` 6 件)。`UiManagerTests`/`CanvasDataValidatorTests` への追加は [07_canvas_prefab.md] 参照
- **2026-09-13 追記**: `SliderSkinEditorWindow` に ButtonSkin と共通の `ControlSkinPreviewSection`([15] A-4 実装メモの 2026-09-13 追記)を追加。6 状態の演出再生・一時停止・停止・「✎ Tween Editor」と、Grab / Release / Notch / Limit / Denied の SE 試聴ができる。演出はスライダー本体(`UiSlider` の RectTransform)に掛かる。パーツ(`Track`/`Fill`/`Handle`/`DelayFill`)の `StateVisual` は `UiSlider.OnSkinApplied` が空実装で実行時に反映されないため、プレビュー対象にしていない(見た目を偽って見せない。反映は別途)。2026-09-14 に設定欄と一体化(状態の箱に ▶、SE 欄の横に ▶/■)。詳細は [15] 同節の 2026-09-14 改修
- **2026-09-14 追記**: 当たり判定を ButtonSkin と共通化(`ControlSkinData.HitAreaExpand` / `AlphaHitThreshold`)。**長らく未接続だった `SliderSkinData.ExtraHitPadding` が効くようになった**(`EffectiveHitAreaExpand` で X を左右、Y を上下に加算し、Track の `TargetGraphic.raycastPadding` に反映)。状態遷移の自動再生と当たり判定の表示・ドラッグ調整も SliderSkin エディタで使える([15] 同節)
- **2026-09-14 追記(ユーザー要望: つまみの当たり判定 / 入力の許可 / 動かしたときのプレビュー)**:
  - **入力の許可**: `UiSlider` に `PointerInput`(マウス / タッチ: ドラッグ・溝クリック・ホイール・ホバー)と `NavigationInput`(キーボード / パッド)を追加(既定 ON)。部品ごとの性質なので Skin ではなく UiSlider 側に置いた(同じ Skin を音量スライダーと HP バーで共用できる)。OFF のときは EventSystem のハンドラ(`OnPointerDown/Up/Enter/Exit`・`OnBeginDrag/Drag/EndDrag`・`OnScroll`、`OnMove`)で入力を捨て、`NavigationInput=false` は `CanFocus` も false(`UiInteractable.CanFocus` を virtual 化)にしてパッドのフォーカス移動で飛ばす。`BeginDragAt`/`Move`/`Value` 等の API 直呼び(ゲームコード・エディタのプレビュー・テスト)は制限しない。`SliderPresets` の HPバーは両方 OFF、他のプリセットは ON に戻す
  - **つまみの当たり判定**: `SliderSkinData.HandleHitAreaExpand`(Vector4)を追加し、`UiSlider.OnSkinApplied`(これまで空実装)で `HandleRect` の Graphic の `raycastPadding` に反映。Skin Editor の「当たり判定を表示」で青い枠 + SceneView ハンドル(`ControlSkinPreviewSection.Options.ExtraHitAreas`)
  - **動かしてみる**: `SliderSkinEditorWindow` に ◀▶(`Move` + `MoveRelease`)・ドラッグ模擬(`BeginDragAt`→`DragTo`→`EndDrag`、0.8 秒)・値スライダー(`Value` = ゲームコードからの変更と同じ)を追加。プレビュー実体を `EditorApplication.update` で `Advance` し(Follow Motion の追従)、`OnDragBegin/End`・`OnNotchPassed`(`NotchSeMinIntervalSec` で間引き)・`OnLimitReached`・`OnDenied` に合わせて Grab / Release / Notch / Limit / Denied の SE を試聴側(`ControlSkinPreviewSection.PlaySeField`)で鳴らす(Editor では Audio が未 Bind で UiSlider 自身の SE は鳴らないため)
  - テスト: `UiSliderInputTests`(PlayMode 5 件)、`SliderEditorTests` に HPバーの入力 OFF / 他プリセットで ON に戻る を追加
  - **つまみの位置ずれを修正(ユーザー報告: 値 0〜1 で動かすとかなりずれる)**: `ApplyFillAndHandle` は「つまみのアンカーが溝の左端にある」前提で `anchoredPosition.x = 溝の幅 × 値` にしていたため、Unity 既定の中央アンカーのつまみ(確認用プレビューも、デザイナーが普通に作った Prefab も該当)では値 0 で溝の中央、値 1 で右端より半幅はみ出していた。Unity 標準 Slider と同じく**つまみのアンカーを値の位置へ動かし(軸方向の anchorMin/Max = 値)、軸方向の anchoredPosition を 0 にする**方式へ変更。つまみの中心が親(溝、またはスライド領域)の中の値の位置に乗り、元のアンカー設定に依存しない。軸方向にストレッチしていたつまみは点アンカーになる(Unity 標準 Slider と同じ挙動)。回帰テスト `Handle_FollowsValue_EvenWithCenterAnchor`
  - **パーツの見た目を反映(ユーザー報告: パーツに入れた Override Sprite が反映されない)**: `SliderSkinData.Track/Fill/Handle/DelayFill` は長らく未接続だった。`UiSlider.OnSkinApplied` で各 RectTransform の Graphic へ画像・色・拡大率を適用(状態に依らない固定の見た目)。既存 Skin の既定値で消えたり潰れたりしないよう、Tint 未設定(0,0,0,0)と Scale ≤ 0 は触らない。Track の Graphic が TargetGraphic と同じなら色は状態が決め、Track の画像は状態に画像(Override Sprite / コマ)が無いときだけ使う。パーツ画像を外したら元の画像に戻す
  - **パッド / ホイールで動かなくなる不具合を修正(ユーザー報告: スタミナ設定で ◀▶ が効かず「Commit: 100」のまま)**: 移動量(`PadStepAmount` 既定 0.05)が `Step` より小さいと `SnapValue` の丸めで元に戻り、さらに目盛りの吸い付き幅(SnapThreshold)に入ると元の目盛りへ吸い戻されていた。`StepTarget` を新設し、パッド・押しっぱなしのリピート・ホイール・溝クリックのページ送りで共通に「移動量は Step 未満にしない」「それでも吸い付きで元の値に戻るなら隣の目盛りへ進める」。回帰テスト 3 件(`UiSliderInputTests`)
  - **コードレビュー対応(2026-09-14)**:
    - `StepTarget`
      - `WholeNumbers`(Step=0)でも移動量を 1 未満にしない。以前は範囲の途中で「端に着いた」扱いになり、EscapeOnLimit でフォーカスが抜けていた。
      - Min > Max でも、隣の目盛りへ進む向きを値の向きに合わせる。
    - `PointerInput` が止めるのは始まり側だけにした(押す・入る・ドラッグ開始・ドラッグ・ホイール)。離す・出る・ドラッグ終了は OFF でも処理する(操作の途中で OFF にしても押下・ホバー・ドラッグ中のまま残らない)。
    - つまみの当たり判定(`HandleHitAreaExpand`)と本体の `HitAreaExpand` は、Prefab で手設定した `raycastPadding` に**足す**(Skin を当てても手設定を消さない)。透明判定も Skin が 0 なら手設定を使う。
    - Skin が外れたら(`SetVisual(null)` / Resolver が null)、差し替えた画像・スクロール用マテリアル・コマ送り・当たり判定を元に戻す。
    - 透明判定が有効なまま読めない画像の状態へ替わるときは、差し替えの前に透明判定を外す。以前は setter が例外を投げて外せず、ポインタが動くたびに Image がエラーを出していた。コマに読めない画像が 1 枚でもある状態では透明判定を使わない。
    - スクロールはシェーダー標準の `_Time` をやめ、`UiInteractable` が配る止まらない時計(`_DDriveUiUnscaledTime`)で動かす(timeScale=0 のポーズ中も止まらない)。共有マテリアルは使っている数を数え、誰も使わなくなったら破棄する。
    - `TickFollow` は表示値が変わらないフレームでは描き直さない(docs/24 整理項目 5)。
    - つまみの位置はアンカーで決めるため、つまみの親は溝(またはスライド領域)にし、つまみのアンカーは点(min=max)で置く前提。横に stretch したつまみは幅が sizeDelta に潰れる。
  - **エディタ側のレビュー対応(2026-09-14)**:
    - Skin Editor(Button / Slider 共通の `ControlSkinPreviewSection`)
      - Scroll Material を自動で書き込まない(開いただけでアセットが dirty になり、Undo しても即座に書き戻していた)。空なら警告と「既定のマテリアルを設定」ボタン(Undo 付き)を出す。
      - 状態遷移の自動で進む段はプレビューを作り直さず、無くなっていたら止まる(撤去・シーン移動の後に復活していた)。
      - 「Anim2D から読み込む」のコマ/秒は、キーの平均間隔から求める(以前は `clip.frameRate` で、長さ 8 フレームに 4 コマのクリップが 2 倍速になった)。
      - `Dispose()` をウィンドウの `OnDisable` から呼び、試聴用のプレビューシーンを確実に閉じる。
      - 描き直しは 30fps 上限。
      - Skin を外すとプレビューも Skin 無しに戻り、当たり判定の枠も消える。
      - 遷移の再生中は状態ごとの ⏸ を遷移側の一時停止に回す。
    - Slider Editor
      - ドメインリロード後もイベント購読(SE・イベントログ)と、サンプルに当てた Skin(`_explicitSkin` を保存)を戻す。
      - ドラッグ模擬は確認用サンプルだけで動き、実物のスライダーでは理由を表示して中止する。
      - 実物へのパッド操作は子(Handle / Fill)ごと Undo に積む。
      - SE の試聴は `SliderSePreview`、Id → Data の検索は `DataIdLookup`(キャッシュ付き)に共通化した。
    - スライダーのプレビュー部品は `PreviewSliderFactory` に一本化し、子まで DontSave にする。
    - 残る制約: Slider Editor の比較用 2 体目と全状態プレビューは、ドメインリロードで参照が切れて止まったまま残る(撤去で消える)。
  - **SliderEditor**: Editor では Audio が未 Bind で UiSlider 自身の SE が鳴らなかった(docs/23 の確認項目と不一致)。`OnDragBegin/End`・`OnNotchPassed`(`NotchSeMinIntervalSec` で間引き)・`OnLimitReached`・`OnDenied` に合わせてプレビュー用 `PreviewService` から Grab / Release / Notch / Limit / Denied を鳴らす。Slider Skin から開いたサンプルは Skin Id を持たないため、「全状態を並べる」と SE は開いたときの Skin を使う(以前は 6 本とも Skin 無しだった)

### レビュー対応(2026-09-11、Phase 4 コードレビュー)

- **パッドリピートが EventSystem 経由だと止まらない**: `OnMove`(`IMoveHandler`)は `Move()` を呼ぶだけで `_padActive=true` にする一方、実行時に `MoveRelease()` を呼ぶ経路が無かった(呼んでいたのはテストのみ)。`Move()` で `Time.frameCount` を `_lastMoveFrame` に記録し、`Advance()` の先頭で「`_padActive` かつ 1 フレーム以上 `Move` が来ていない」なら自動的に `MoveRelease()` する。同一フレーム内の `Move→Advance`(テストの典型パターン)は継続扱いになるよう `Time.frameCount > _lastMoveFrame + 1` を条件にした。テスト専用に `SetPadHeldForTest(bool)` を追加(明示的に「押しっぱなし」を模擬したいテスト向け)
- **Direction(RightToLeft/TopToBottom)とキー入力の関係を明文化**: `SignFor`(十字キー/パッド)は Direction に関わらず Right/Up が常に「値を増やす」(`Wheel` も同様、既に Direction を見ていない)。ポインタ操作(`ComputePointerFraction`)は Direction 通りの空間的な向きに従う(`RightToLeft` なら画面右へドラッグすると値は減る)。この非対称は意図的な仕様(キー入力は操作感、ポインタは見た目の並びを優先)として `SignFor` にコメントを追加した。コード変更は無し(方針の明文化のみ)
- **`UiInteractable`/`UiButton` の Pool 再利用汚染**: `CanvasGroup`/`RectTransform` と違い、`_pointerDown`/`_pointerOver`/`_focused`/`_cooldownRemaining`/`_stateTween`(基底)や `_isHeld`/`_longPressFired`/`_pendingClickActive`(`UiButton`)は Pool から Return されても(`SetActive(false)`)クリアされず、次に Rent された瞬間に「まだ押されている/ホバー中」扱いになっていた。基底に `protected virtual void OnDisable()` → `ResetInteractionState()`(public、テストからも呼べる)を追加し、`UiButton` は追加のフィールドをクリアしてから `base.OnDisable()` を呼ぶ
- テスト: `Assets/DDrive/Tests/Runtime/UiSliderTests.cs` の `PadMove_StopsRepeating_WhenNoFurtherMove_AcrossFrames`(UnityTest)/`Move_Right_AlwaysIncreasesValue_RegardlessOfDirection`、`Assets/DDrive/Tests/Runtime/UiButtonTests.cs` の `OnDisable_ResetsHoverState_ToNormal`/`OnDisable_ResetsHeldAndLongPress_SoRepeatDoesNotFireAfterReuse`

## B-8. ネットワーク（[14] との整合）

- UiSlider は Canvas 配下の要素であり `NetMode = Local` 固定（[14] §4「Canvas / UI は常に Local」）
- ゲームロジックに影響する値（キャラメイクのパラメータ等）は、スライダー操作そのものを同期せず、**確定操作（決定ボタン）でゲームコードが同期変数へ書く**

## B-9. 将来枠（v1.x）

同じ `UiInteractable` 基底の上に、基盤改修なしで追加できる。

| コントロール | 概要 |
|---|---|
| UiToggle | On/Off + 中間状態。Skin と SE は共通 |
| UiStepper | 「◀ 3 ▶」型の離散値選択（解像度・言語選択など） |
| UiScrollbar | UiSlider の派生（サイズ可変ハンドル + コンテンツ連動） |
| UiRangeSlider | ハンドル 2 個（フィルタ範囲指定など） |
| UiTabBar | フォーカス移動と Presentation 連動 |
