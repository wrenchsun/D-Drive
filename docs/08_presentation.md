# 08. Presentation（演出統合） 詳細設計

関連: [01_architecture.md](01_architecture.md) §8 / 03〜07 各アセット設計書

---

## 1. 目的

「剣攻撃」= Animation + SE + VFX + CameraShake + HitStop のように、実際の演出は複数アセットの束で成立する。これを **1 つの PresentationData** にまとめ、ゲームコードは 1 行で再生できるようにする。

```csharp
// プログラマーが書くのはこれだけ
Presentation.Play(PRESENTID.SkillSlash, ctx);
```

## 2. データ構造

```csharp
public class PresentationData : AssetDataBase
{
    public PresentationTrack[] Tracks;
    public float TotalDuration;            // 0=トラックから自動算出
    public bool Interruptible;             // 途中キャンセル可否
}

[Serializable]
public struct PresentationTrack
{
    public TrackTrigger Trigger;       // AtTime(秒) / OnSignal(key) ★"onHit"等
    public float Time;
    public string SignalKey;
    public TrackKind Kind;             // Anim/Anim2D/SE/BGM/VFX/CameraShake(ShakeId)/
                                       // Haptic(HapticId)/HitStop/Timeline/Canvas/
                                       // UiTween/Marker/Signal   ※Shake/Haptic は [16] 参照
    public AssetRef Asset;             // 対応するID
    public TrackTargetMode Target;     // Self / ContextTarget / World / Anchor
    public AnchorDef Anchor;           // VFX 用 (04参照)
    public ParamValue[] Params;        // 上書きパラメータ
}
```

```csharp
public struct PlayContext             // 再生文脈。プログラマーが渡す
{
    public Transform Self;            // 再生主体（プレイヤー等）
    public Transform Target;          // 対象（敵等, null可）
    public Vector3 Position;
    public Action<string> OnSignal;   // 逆方向: 演出→コードへの通知
}
```

## 3. 実行モデル

```
Presentation.Play(id, ctx) → PresentationHandle
  ├ AtTime(0.00) トラック → 各 Manager に即委譲
  ├ AtTime(t)    トラック → Tick でスケジュール実行
  └ OnSignal("hit") トラック → handle.Signal("hit") が来たら実行
```

- Presentation 自身は再生せず **各 Manager への委譲のみ**（薄いオーケストレータ）
- `handle.Signal("hit")` はゲームコード（当たり判定）から発行 → CameraShake / HitStop トラックが発火
- `handle.Cancel()` で全トラック停止（Interruptible=true のとき）。発行済み VFX の扱いは各トラックの `StopOnCancel` フラグ
- HitStop / CameraShake は TimeService / CameraManager の薄い API を Foundation 側に用意（v1 は最小実装で良い）
- Timeline トラック: TimelineAsset を PlayableDirector で再生。Timeline 内からも AssetEvent 経由で SE/VFX を呼べる（Marker 拡張）

## 3.5 PresentationHandle のイベント・関数（プログラマー向けサポート API）

演出とゲームロジックの同期に必要な操作・通知を Handle に揃える。

```csharp
public readonly struct PresentationHandle
{
    // ── 制御関数 ──
    public void Signal(string key);                  // "hit" 等 → OnSignalトラック発火
    public void Cancel();                            // 全トラック停止 (Interruptible時)
    public void Pause();  public void Resume();
    public void SetSpeed(float speed);               // スロー演出等
    public void Seek(float time);                    // デバッグ/スキップ用
    public float NormalizedTime { get; }
    public bool IsPlaying { get; }

    // ── イベント (R3 / UniTask) ──
    public Observable<Unit> OnCompleted { get; }     // 全トラック終了
    public Observable<Unit> OnCancelled { get; }
    public Observable<string> OnMarker { get; }      // データ側 Markerトラック通過
    public Observable<PresentationTrack> OnTrackFired { get; }
    public UniTask WaitAsync(CancellationToken ct);  // await Presentation.Play(...)
}
```

- **Marker トラック**（TrackKind.Marker）: デザイナーがタイムラインに置いた文字列マーカーを通過時に `OnMarker` で通知 → 「演出の山でダメージ数値を出す」等をコード側が購読できる（Signal の逆方向）
- `await` 対応により「演出終了までスキル入力をロック」が 1 行で書ける

## 4. 専用エディタ（PresentationEditor）

Timeline 風の複数トラック UI。

| 機能 | 内容 |
|---|---|
| トラック編集 | Kind ごとの行にクリップを D&D 配置。時間ドラッグ |
| Signal レーン | "onHit" 等のシグナルトラックを可視化。プレビュー中に手動発火ボタン |
| 統合プレビュー | モデル選択 → 全トラックを実 Manager で同時再生 ★システムの目玉機能 |
| パラメータ上書き | トラック単位で VFX 色や SE 音量を上書き |
| 環境切替 | 背景・ライト・スロー再生（0.1x〜） |

## 5. 運用方法（剣攻撃を作る流れ）

1. プログラマー: `Presentation.Play(PRESENTID.SkillSlash, ctx)` と当たり判定時の `handle.Signal("hit")` を実装（アセット 0 でも Placeholder で動く）
2. アニメーター: Attack01 の AnimData を登録
3. デザイナー: PresentationEditor で SkillSlash を作成
   - [0.00] Anim: Attack01 / [0.00] SE: SwordSwing03 / [0.10] VFX: SlashBlue (RightHand)
   - [onHit] CameraShake: Shake_Hit_Small / [onHit] Haptic: Haptic_Hit_Punch / [onHit] HitStop: 0.08 / [onHit] SE: HitFlesh01
4. 統合プレビューで調整 → 保存 → ゲームで即反映。**コード変更なし**

## 6. Validation

| 検査 | 重度 |
|---|---|
| Track.Asset 未設定 / Missing | Error |
| OnSignal トラックの SignalKey 空 | Error |
| AtTime が TotalDuration 超過 | Warning |
| Interruptible=false かつ長尺(>10s) | Warning |
| 循環参照（Presentation が自身を含む） | Error |
