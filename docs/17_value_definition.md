# 17. 値定義の統一規約（ValueDef / TimeDef）

関連: [02_core_framework.md](02_core_framework.md) / [15_ui_interaction.md](15_ui_interaction.md) / [16_camera_haptics.md](16_camera_haptics.md) / [18_ui_controls.md](18_ui_controls.md)

対象: 全種別横断（Foundation 層） / 対応要件: [00] FR-19

---

## 1. 目的

Tween・シェイク・振動・マテリアルアニメ・スライダー応答など、システム内には「時間や入力で変化する値」が多数登場する。これらの指定方法が種別ごとにバラバラだと、デザイナーから見て **編集 UI も語彙も毎回別物**になり、学習コストが種別数に比例してしまう。また「定数で固定したい」「カーブは同じで速さだけ変えたい」といった基本操作が種別によってできたりできなかったりする。

そこで、デザイナーが調整するすべての値を 1 つの型 `ValueDef` に統一する。

- **形（カーブ）とスピード（時間軸の進め方）を分離する** — 同じ形のまま速さだけ変える、が常に可能
- **編集 UI は共通 PropertyDrawer 1 本**（§5）— どの種別でも同じ操作で編集できる（NFR-8）
- **評価は純関数** — ネット同期・Late Join のシーク再生（[14] §5）と自然に整合する

## 2. データ構造

```csharp
public enum ValueMode
{
    Constant,     // 定数
    Parametric,   // Ease 31 種 + CubicBezier（EasingCore = [15] B-2）
    Curve,        // AnimationCurve（任意カーブ）
}

[Serializable]
public struct ValueDef            // スカラー 1 本の定義。全種別共通
{
    public ValueMode Mode;

    // ── 形 ──
    public float    Constant;     // Mode=Constant
    public EaseDef  Parametric;   // Mode=Parametric（Ease 種 + Bezier P1/P2）
    public AnimationCurve Curve;  // Mode=Curve

    // ── 出力レンジ（Parametric/Curve の 0..1 を実値へ）──
    public float From, To;        // Constant のときは無視
    public bool  Normalized;      // true = Curve の値をそのまま使う（From/To 無視）

    // ── 時間軸 ──
    public TimeDef Time;
    public LoopMode Loop;         // Once / Loop / PingPong
    public int LoopCount;         // 0 = 無限

    // ── 評価 ──
    public float Evaluate(float t);        // t = 正規化時間 0..1（0 alloc・純関数）
    public float EvaluateAt(float sec);    // 経過秒から Time/Loop を解決して評価
    public float Duration { get; }         // TimeDef から解決した実尺（Loop 時は 1 周期）
}

[Serializable]
public struct TimeDef             // ★「スピード」の統一表現
{
    public TimeMode Mode;         // Duration / Speed / Rate
    public float Value;
    public float SpeedScale;      // 既定 1。実行時倍率（Handle.SetSpeed 等と合成）
    public bool  IgnoreTimeScale; // ポーズ・スロー演出中も等速で進めるか
}

public enum TimeMode
{
    Duration,   // 秒で指定（0.3s で 0→1）。既定
    Speed,      // 基準尺に対する倍率（1.0 = 基準どおり、2.0 = 2 倍速）
    Rate,       // 単位/秒（UV スクロール・回転ループなど「速さ」が主語のもの）
}
```

### 派生型（ベクトル・色への拡張）

```csharp
[Serializable] public struct ValueDef3 { public ValueDef X, Y, Z; public bool Uniform; }
[Serializable] public struct ValueDefColor
{
    public ValueMode Mode;
    public Color Constant;
    public Gradient Curve;        // Mode=Curve は Gradient を使う
    public ValueDef Alpha;        // アルファのみ別カーブにしたい要求が多いため分離可
}
```

- `Uniform=true` の `ValueDef3` は X の設定を全軸に適用する（スケール演出などの入力を減らす）
- 色は「パラメトリック曲線」に相当する概念が薄いため、Constant / Gradient の 2 モード + Alpha 用 `ValueDef` とする（3 モード規約の唯一の例外。UI 上も 2 タブ表示）

## 3. スピードの意味論（TimeMode の使い分け）

| TimeMode | 使う場面 | 解決式 |
|---|---|---|
| Duration | 出現演出・シェイク・振動など「何秒で終わるか」が主語 | `t = elapsed / Value` |
| Speed | プリセットや共有 Data の尺を相対的に伸縮したい場合 | `t = elapsed / (BaseDuration / Value)` |
| Rate | UV スクロール・常時回転・ゲージ充填など終端がない動き | `t += Value * dt`（Loop 前提） |

適用順序（すべての Manager で共通）:

```
実効速度 = TimeDef.Value
           × TimeDef.SpeedScale        （Data 側の倍率）
           × Handle.SetSpeed(s)        （呼び出し側の指定）
           × GlobalScale               （オプション画面: 揺れ / 振動 / UI 速度）
           × (IgnoreTimeScale ? 1 : TimeService.Scale)   （ヒットストップ・スロー）
```

- `GlobalScale` はシェイク・振動（[16]）の「0〜100%」設定と同じ機構を全種別へ一般化したもの
- ポーズ時の挙動は `AssetFlags.Pause` が優先する（`IgnoreTimeScale` はスロー演出のみを対象とする）

## 4. 使用箇所（本規約が適用される主なフィールド）

| ドキュメント | フィールド | 備考 |
|---|---|---|
| [15] B-3 `TweenTrack` | `Motion` | 形 + 尺 + Loop を 1 フィールドに統合 |
| [15] A-3 `StateVisual` | `Scale` | 押下縮小など。定数指定が基本 |
| [16] A-1 `CameraShakeData` | `Envelope` / `Frequency` | 時間変化する周波数も表現可能 |
| [16] B-1 `HapticsData` | `LowFreq` / `HighFreq` | 尺は TimeDef に内包 |
| [06] A-2 `MaterialAnim` | `Value` | UV スクロール = Rate、サイン波 = Parametric+PingPong。種別分岐の列挙型を持たない |
| [04] §2 `VfxParam` | `Anim` | 任意。時間変化する公開パラメータ |
| [03] §2 `BgmData` | `FadeIn` / `FadeOut` | フェードカーブをデザイナーが指定可能 |
| [05] C-3 `Anim2DData` | `Retiming` | フレーム配置カーブ |
| [18] B-1 `UiSlider` | `Response` / `FollowMotion` | 応答曲線・表示追従 |

**明示的な例外**: 横軸が時間でないカーブ（[03] の距離減衰 `Rolloff` 等）は素の `AnimationCurve` のままでよい。ValueDef は「時間（または正規化入力）→ 値」の定義に限定する。

## 5. 共通 PropertyDrawer（デザイナーが触る唯一の編集 UI）

`ValueDef` に対して 1 つの PropertyDrawer を実装し、全種別で使い回す。種別ごとに編集 UI を作らない。

```
┌ Envelope ─────────────────────────────────────────┐
│ [ 定数 ][ 曲線 ][ カーブ ]        ← モード切替タブ    │
│ ┌───────────────────┐  From [0.0]  To [1.0]       │
│ │   ╱‾‾‾╲           │  Ease  [OutQuad ▼]          │
│ │  ╱     ╲__        │  （カーブモード時はカーブ編集） │
│ └───────────────────┘                             │
│ 時間 [ Duration ▼ ] [0.30] 秒   速度倍率 [1.00]     │
│ ループ [ Once ▼ ]  回数 [0]     □ TimeScale 無視    │
│ ▶ スクラブ ━━━━●━━━━━━  現在値: 0.62               │
└───────────────────────────────────────────────────┘
```

- **モード切替でデータを捨てない**: 定数 ⇄ 曲線 ⇄ カーブを行き来しても、各モードの設定値は保持したまま `Mode` だけが変わる（試行錯誤を止めない）
- **ミニグラフ常時表示**: 折りたたみ時も 1 行分のサムネイル曲線を出す（[15] B-6 の「名前でなく形で選ぶ」方針を全種別へ）
- **スクラブ**: バーをドラッグすると対象（シェイクなら実際のカメラ、Tween なら実要素）がその位置の状態で更新される。プレビューは実 Manager 駆動（[01] ADR-4）
- **右クリックメニュー**: 「他の ValueDef からコピー」「プリセットとして保存」「反転」「イーズを推定（カーブ → 最も近い Ease に変換）」
- 全操作 Undo 対応（NFR-5）

## 6. Validation（全種別共通検査）

種別側の Validator はここに挙げる検査を再実装せず、共通 Validator（0-16）に委譲する。

| 検査 | 重度 |
|---|---|
| Mode=Curve でキーが 0 本 / null | Error |
| Mode=Parametric で EaseDef 未設定 | Error |
| TimeMode=Duration で Value ≤ 0 | Error |
| Loop=Loop / PingPong かつ TimeMode=Duration で Value=0 | Error（無限ループでフリーズ） |
| From == To（変化しない設定） | Warning |
| Mode=Constant なのに Loop 指定 | Info（無意味な設定） |
| TimeMode=Rate なのに Loop=Once | Warning |
| SpeedScale ≤ 0 | Error |
| 実尺が BudgetProfile の上限超過（[13] A-4 と連動） | Warning |

## 7. ネットワーク・決定性（[14] との整合）

- `Evaluate` は入力（正規化時間）だけで決まる純関数とし、内部状態を持たない → 全クライアントで同一結果
- 経過時間は `ITimeSource`（ネット時は `NetworkTime`）から取得する。途中参加者のシーク再生（[14] §5）でも `EvaluateAt(sec)` を呼ぶだけで位相が揃う
- `IgnoreTimeScale` はローカル演出（ヒットストップ）用のフラグであり、サーバーのシミュレーション時間には影響しない

## 8. テスト方針

| 対象 | 種別 | 例 |
|---|---|---|
| Evaluate | EditMode | 3 モード × 代表 Ease の参照値一致、Loop/PingPong の周期境界 |
| 純関数性 | EditMode | 同入力同出力（内部状態なし）、Burst 互換 |
| 0 alloc | PlayMode | 定常経路（Tick 内 Evaluate）で GC alloc なし |
| Drawer | EditMode | モード切替でのデータ保持、Undo 往復 |
