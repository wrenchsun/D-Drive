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

> **シーン配置型アンカー(AnchorPoint、[04] §2.5)との連携(2026-07-28 明記)**: トラックごとに `AnchorDef` を持つため、**1つの Presentation 内の複数トラックがそれぞれ別の AnchorPoint を参照できる**。各トラックの Anchor(BoneName/NamedObject)は `PlayContext.Self`(または Target)配下から名前解決されるので、キャラクターに AnchorRig を持たせておけば「斬撃 VFX は Anchor_RightHand、ヒット音は Anchor_Chest、土煙は Anchor_Foot」のように、まとめた演出の中でトラック単位に使い分けられる。AnchorPoint 固有のオフセット/ランダムも各トラックの Spawn 時に個別適用される(Phase 5 実装時はこの契約を維持すること)。

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

## 実装メモ（2026-09-14、5-1）

実装: `Runtime/Presentation/{PresentationData,PresentationTrack,PlayContext,PresentationManager,PresentationHandle,Presentation,PresentationTiming,PresentationDataValidator}.cs`。専用エディタ(§4 PresentationEditor)は 5-4 でまだ未実装のため、`DataEditorRegistryTests` の Exempt に `PresentationData` を追加した(5-1 時点では Inspector から `Tracks` を直接編集する)。

- **R3 導入**: `Packages/manifest.json` に UnityNuGet scoped registry(`org.nuget` スコープ)を追加し `org.nuget.r3`(コア型 `Observable<T>`/`Unit`/`Subject<T>` を含む素の `R3.dll`) + `com.cysharp.r3`(git、`R3.Unity` の Unity 統合層。今回は使っていない)を導入。両方とも 1.3.1。**DLL 重複は発生しなかった**(`org.nuget.system.runtime.compilerservices.unsafe@6.0.0` が isuzu MCP 側のコピーと衝突する懸念があったが、`console_read_logs`/Editor.log に "Multiple precompiled assemblies" は出ず、isuzu MCP・compile/test も導入後に問題なく動作し続けた)。`DDrive.Runtime.asmdef`/`DDrive.Samples.asmdef`(`overrideReferences: false`)は R3.dll が自動参照されるため無編集で通ったが、`DDrive.Tests.Runtime.asmdef`(`overrideReferences: true`)は `precompiledReferences` に `"R3.dll"` を追記する必要があった。
- **PresentationHandle は Handle<TMarker> の薄いラッパー struct**(他種別のような拡張メソッドではなく、`Signal`/`Cancel`/`Pause`/`Resume`/`SetSpeed`/`Seek`/`NormalizedTime`/`IsPlaying`/`OnCompleted`/`OnCancelled`/`OnMarker`/`OnTrackFired`/`WaitAsync` を直接メンバーに持つ readonly struct)。中身は `Handle<PresentationMarker>` 1 個のみで GC alloc 0。`WaitAsync` は `UniTask.WaitUntil` のポーリングではなく `UiTweenManager` と同じ `UniTaskCompletionSource` 方式(Complete/Cancel 時に同期的に `TrySetResult`)。
- **Kind の委譲先(実装済み)**: Anim/Anim2D → `AnimManager.PlayData`(対象 Animator は `TrackTargetMode` で決めた Transform から `GetComponentInChildren<Animator>` で解決) / Se → `AudioManager.PlaySeData` / Bgm → `BgmManager.PlayBgmData` / Vfx → `VfxManager.SpawnData` / Canvas → `UiManager.Open` / UiTween → `UiTweenManager.PlayData`(対象は `RectTransform`) / **CameraShake → `CameraFxManager.ShakeData`(2026-09-14、5-2)** / **Haptic → `HapticsManager.PlayData`(2026-09-14、5-2b)**。**Timeline のみ 6-10 待ちのため警告 1 回 + no-op**。
- **5-2/5-2b 実装メモ(2026-09-14)**: `CameraShake`/`Haptic` トラックは `ctx.Position` を `ShakeSpace.FromSource` 用の発生位置としてそのまま `CameraFxManager.ShakeData` に渡す(CameraLocal/World は無視するので常に渡してよい)。`StopOnCancel=true` のときは `FiredShake`/`FiredHaptic` リストに Handle を積み、Cancel 時に `CameraFx.Stop(h, fade:0)`(即時)/ `Haptics.Stop(h)` する(既存の `FiredVfx` 等と同じパターン)。CameraFx 自体は Presentation の Tick(ScaledDeltaTime)経由ではなく Unscaled dt で駆動されるため、HitStop 中も揺れは止まらない(詳細は [16_camera_haptics.md] 実装メモ)。
- **Marker / Signal Kind は他 Manager に委譲しない**: `PresentationTrack.SignalKey` を「名前」として再利用する(専用フィールドを増やさない設計判断)。Marker → `handle.OnMarker` へ通知(データ→コード)。Signal → `PlayContext.OnSignal` を呼ぶ(データ→コードのもう 1 つの経路。`handle.Signal(key)` はコード→データの逆方向)。
- **HitStop は `Params[0].FloatValue` を秒数として `TimeService.HitStop` に渡すだけ**。AtTime の進行が HitStop 後に止まるのは特別な配線をしたからではなく、`PresentationManager` も他の全 `IAssetManager` と同じく `GameLoopDriver` から `TimeService.ScaledDeltaTime(unscaledDt)` を受け取って `Tick` しているため(HitStop 中は全 Manager が同時に止まる。[16] Part A の CameraShake が実装されたら「揺れは止めない」等の個別対応が要るかもしれない)。
- **`TrackTargetMode`(Self/ContextTarget/World/Anchor)は `contextRoot` の決定にのみ使う**: Self→`ctx.Self` / ContextTarget→`ctx.Target` / World・Anchor→`null`(`AnchorDef.LocalOffset` をそのまま絶対座標として使う)。**要判断**: World と Anchor は現状まったく同じ実装(`PlayContext.Position` を消費していない)。ドキュメント上は「World=ヒット位置基準」「Anchor=環境据え置き」のような意味分けが考えられるが、5-1 では区別を導入していない。5-4(PresentationEditor)か実際の演出データが増えた時点で要否を判断してほしい。
- **AtTime(0.00) は `Play()`/`PlayData()` 呼び出し中に同期的に発火する**(§3 のとおり)。そのため `var h = Presentation.Play(id, ctx); h.OnMarker.Subscribe(...)` のように Handle を受け取ってから購読しても、Time=0 のトラックは観測できない(Vfx/Se 等の副作用は Play 前提で即時実行されるべきという既存 Manager 群と同じ考え方を踏襲した)。ゲームコードが Time=0 の通知を確実に受け取りたい場合は `PlayContext.OnSignal`(Kind=Signal)を使うこと。
- **完了判定**: `TotalDuration`(0 なら Trigger=AtTime の最大 `Time` から自動算出、`PresentationTiming.EffectiveDuration`)に `Elapsed` が到達したら `Complete()`。**要判断**: OnSignal のみで構成され `TotalDuration` を明示していない Presentation は、最初の `Tick` で(尺 0 とみなされ)即完了してしまう。Signal 待ちだけの演出を作る場合は `TotalDuration` を明示すること(Validator では検出していない)。
- **剣攻撃デモ**: `Assets/GameData/Presentation/Demo/PRES_Demo_SkillSlash.asset`(既存の `VFX_Player_Slash`/`SE_Player_Slash` を Target=Self で参照。AtTime(0.00) に Vfx+Se、OnSignal("hit") に HitStop(0.08s)+Se を配置)。確認用シーンは `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity`(`DDriveRuntimeBootstrap` + `PresentationSkillSlashDemo`(`Assets/DDrive/Samples/`、P キーで Play・Space で Signal("hit")・C で Cancel)。**要判断**: `VFX_Player_Slash`/`SE_Player_Slash`/`PRES_Demo_SkillSlash` の `Flags.Load` を `LazyLoad`(既定)から `Preload` に変更した — `PresentationManager` も他の Manager と同じく `ResolveOrPlaceholder` で同期解決するため、LazyLoad のままだと(何かが先に `ResolveAsync` を呼んでいない限り)常に Placeholder になる Canvas/ControlSkin と同種の問題(`Editor/AssetBrowser/AssetCreationService.cs` の `Create` 内コメント、2026-09-12 対応分を参照。Presentation は当時のケース分けに含まれていなかった)。この 2 つの既存アセットは他の用途(AnchorGroup デモ等)でも使われているため、Preload 化の影響範囲は要確認。
- Addressables グループ(`DDrive_GameData.asset`/`DDrive_Catalogs.asset`)はユーザーの未コミット変更と混ざるため、デモアセット作成に伴う変更はコミットしていない(下記コミット範囲を参照)。
- **2026-09-14(5-2/5-2b) 追記**: 同じ `PRES_Demo_SkillSlash.asset` の `onHit`(`SignalKey="hit"`)に `CameraShake`(`SHAKE_Demo_DemoHitSmall`)と `Haptic`(`HAPTIC_Demo_DemoHitPunch`)のトラックを追記した(`StopOnCancel=true`)。詳細は [16_camera_haptics.md] 実装メモを参照。
