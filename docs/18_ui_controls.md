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

- フォーカス移動（上下キー）は [07] の `NavNode` に従う。**左右キーはスライダー操作に消費される**ため、`NavNode.Left/Right` へは端到達時のみ抜ける（`EscapeOnLimit` フラグで切替）
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
| ShakeScale | `CameraFx.SetGlobalScale`（[16] A-2）★ FR-17.4 アクセシビリティ要件の受け皿 |
| HapticScale | `Haptics.SetGlobalScale`（[16] B-2） |
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
