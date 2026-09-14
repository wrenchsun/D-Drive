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

## 実装メモ（2026-09-14、5-4）

実装: `Editor/Presentation/{PresentationEditorWindow,PresentationEditorWindow.Tracks,PresentationEditorWindow.Preview,ScenePresentationPreviewDriver,PresentationTrackEditOps,PresentationTrackKindMapping}.cs`。`DataEditorRegistryTests` の Exempt から `PresentationData` を外し、`KnownPairs` に `PresentationEditorWindow` を追加した。

- **統合プレビューは既存の 1 種別 1 ドライバをそのまま束ねるだけ**(二重実装しない、ADR-4): `ScenePresentationPreviewDriver` は `SceneAnimPreviewDriver`(Anim/Anim2D の再生 + `SpawnModel` によるモデル配置 = 「モデル選択」/ [05_model_animation.md] B-4)・`SceneCameraShakePreviewDriver`(CameraShake、5-2c)・`EditorHapticsPreviewDriver`(Haptic、5-2c)を内部に持ち、それぞれの `Manager` プロパティ(`AnimManager`/`VfxManager`(`AnimDriver.Vfx.Manager`)/`CameraFxManager`/`HapticsManager`)をそのまま `PresentationManager` のコンストラクタへ渡す。Se だけはこのドライバ専用の実 `AudioManager`(後述の `EditorAudioFactory`)を持つ。Bgm/Canvas/UiTween は 5-4 時点では未配線(`PresentationManager` 自身の「Manager 未設定」警告 + no-op でそのまま継続する。要判断として下記に記載)。
- **Se 用 AudioManager を独立させた理由**: `SceneAnimPreviewDriver.Audio` はステージ切替のたびに(`ResetForStageChange`で)null に戻り、次の `SpawnModel`/`Play` 呼び出し時に新しいインスタンスとして作り直される内部実装のため、`PresentationManager` のコンストラクタに一度渡して保持すると、ステージ切替後は古い(すでに破棄された Pool を参照する)`AudioManager` を握り続けてしまう。この問題を避けるため、`ScenePresentationPreviewDriver` は自前の `[D-Drive] Presentation Preview` ルート(DontSave)+ 専用の `AudioManager` を持ち、シーン/プレハブモード切替時(`OnStageChanged`/`OnPrefabStageChanged`)には現在の再生を止めてルートごと破棄し、次の `Play()` で `EnsureAudio()` が作り直す(合わせて `PresentationManager` 自体も破棄済みの `AudioManager` を握らないよう毎回作り直す)。
- **`EditorAudioFactory`(`Editor/Preview/EditorAudioFactory.cs`、新規)**: 「PoolService を用意 → テンプレ AudioSource(非アクティブ)を作る → AudioListener の有無を確認 → `AudioManager` を construct する」という定型手順を `SceneAnimPreviewDriver.EnsureManagers` から切り出し、`ScenePresentationPreviewDriver` と共用した(コピペ禁止の指示に対応)。`SceneAnimPreviewDriver` 側の生成物(GameObject 構成・命名)は変更していない。
- **`TimelineRulerGui`(`Editor/Common/TimelineRulerGui.cs`、新規)**: `AnimEditorWindow.DrawTimeline`(3-3)のフレーム目盛り描画(幅に応じたラベル間引き含む)をそのまま切り出し、`AnimEditorWindow` 自身もこのヘルパー経由に置き換えた(ピクセル位置・間引き幅は変更なし、既存テストへの影響なし)。`PresentationEditorWindow` のタイムラインは秒数を 10 分割した目盛りとして同じヘルパーを使う。`Anim2DEditorWindow` はタイムライン自体を `AnimEditorWindow` に委譲しているため、重複していた実装はこの 1 箇所のみだった。
- **トラック編集の実体は `PresentationTrackEditOps`(純粋な static 操作、新規)に切り出した**: 追加(`AddTrack`)/複製(`DuplicateTrack`)/削除(`RemoveTrack`)/時間移動(`SetTrackTime`、ドラッグ 1 フレーム分)をそれぞれ `Undo.RecordObject` 付きで実装し、`PresentationEditorWindow` はこれを呼ぶだけにした。**このプロジェクトには `EditorWindow.CreateGUI` を直接テストする前例が無い**(5-2c 実装メモに明記)ため、`PresentationEditorWindow` 自体の UI テストは書かず、`CameraFxPresets`(5-2c)と同じ設計で「ウィンドウを起動せずに Undo 往復を検証できる」操作クラスに分離してテストした(`PresentationTrackEditOpsTests`)。
- **D&D の Kind 判定は `PresentationTrackKindMapping`(新規)に集約**: Project ウィンドウ/AssetBrowser から落とした具象 Data 型(`SeData`/`VfxData`/`AnimData`/`Anim2DData`/…)→ `TrackKind` の対応と、トラックの「Asset」欄の `ObjectField.objectType` を決める `TrackKind` → 具象型の対応を同じテーブルに置き、ズレないようにした(`Anim2DData : AnimData` のため Anim2D を先に判定する)。`RequiresAsset(TrackKind)` は `PresentationDataValidator` の既存判定をそのまま `public` 化して再利用した(複製しない)。
- **`EditorAnchorRegistry`(5-2c までは Vfx/Se/Anim/Model/Material/Texture/Prefab/Canvas/ControlSkin/UiTween/Anchor/AnchorGroup のみ登録)に `BgmData`/`CameraShakeData`/`HapticsData` を追加登録した**: `PresentationManager` は Kind ごとに `_registry.ResolveOrPlaceholder<T>(id)` で ID 解決するため、登録が無いと CameraShake/Haptic トラックは常に Placeholder になってしまう(`ShakeEditor`/`HapticsEditor` 単体は ID を経由しない `Play(data)` のため、5-2c 時点ではこの登録漏れが表面化していなかった)。
- **タイムラインのレーンは Kind を 6 グループにまとめた**(Anim/Anim2D、Se/Bgm、Vfx、CameraShake/Haptic、HitStop/Marker、Canvas/UiTween/Timeline): 13 種類の Kind をそれぞれ 1 行にすると常時縦に長くなりすぎるため。Signal トラック(`Trigger=OnSignal`)は時間軸を持たないため、タイムラインには描かず専用の「Signal レーン」セクション(統合プレビュー内、`SignalKey` ごとに手動発火ボタン)にのみ表示する。
- **パラメータ上書き(Params)は既存の `ParamValue` 構造体の `PropertyField` をそのまま表示するだけ**: [17] のような専用エディタは無く(プロジェクト内に `ParamValue` 用の `PropertyDrawer`自体が存在しない)、各トラックの折りたたみに `Params` 配列をそのまま表示する。VFX の色や SE の音量を表現するには `ParamValue.Of(Color)`/`ParamValue.Of(float)` の呼び出し側(Manager 実装)が実際に `Params[n]` を読む必要があるが、5-4 時点では `PresentationManager` は VFX/SE の Params を消費していない(**要判断**: Params の実消費経路は 5-1 のスコープ外のまま。デザイナーが値を入れても見た目には反映されない)。
- **環境切替は「確認用シーンの既存ライト/背景を切り替える」方式にした**: 2026-07-28 以降の方針(VfxEditor 等、[09] §2)で「独自ビューポート/環境切替は廃止し実シーンで確認する」に統一されているため、専用の切替ロジックは持たず、開いているシーンの `Light`(`FindFirstObjectByType`)の強度と `Camera.main` の背景色を直接いじるだけの薄い UI にとどめた。スロー再生は「速度」スライダー(0.1x〜2x、`PresentationManager.SetSpeed`)がそのまま兼ねる(docs/08 §4 の「環境切替」行に「スロー再生」が同居しているため)。
- **HitStop は Presentation 自身の Tick(`Time.ScaledDeltaTime`)にしか効かない(要判断)**: `ScenePresentationPreviewDriver.Tick` はランタイムの `GameLoopDriver` と同じく `TimeService.HitStop` → `ScaledDeltaTime` の流れで `PresentationManager.Tick` を駆動するため、AtTime トラックの進行は正しく止まる。しかし `AnimDriver`/`ShakeDriver`/`HapticsDriver` はそれぞれ独立した `EditorApplication.update` フックで **Unscaled dt** のまま自走している(5-2/5-2b の「CameraFx は HitStop 中も揺れを止めない」という仕様どおりの部分もあるが、Anim/Vfx の Tick も本来は HitStop で止まるはずが、エディタプレビューでは止まらない)。ランタイム(`DDriveRuntimeBootstrap`)では全 Manager が同じ `GameLoop.Tick(ScaledDeltaTime)` を共有するため、この差はプレビュー限定。実際の見た目確認で気になる場合は各ドライバに共有 `TimeService` を注入できるようにする改修が必要(5-4 では見送った)。
- **プレビュー開始時の LazyLoad 事前解決は未実装(要判断)**: 5-1 実装メモに記載の「`PresentationManager` は `ResolveOrPlaceholder` で同期解決するため LazyLoad は Placeholder になる」問題について、5-4 では「プレビュー側で `ResolveAsync` を先に呼ぶ」対応を見送った。`EditorAnchorRegistry.Build()` が対象種別(Vfx/Se/Anim/Model/…/Bgm/Shake/Haptics)を起動時に一括 `ResolveAsync` 済みにしているため、**プロジェクト内の既存アセットを参照する分には実害が無い**(Placeholder になるのは Registry が知らない ID だけで、それは実行時と同じ「未登録 ID」の挙動)。新規に作ったばかりで `EditorAnchorRegistry` のスナップショットに含まれない Data を参照する場合は、ウィンドウを開き直す(Registry を作り直す)必要がある。
- **モデル未配置でも再生できる**: `Play()` は `ctx.Self` が null でも呼べる(World/Anchor 基準のトラックや、Kind=Marker/Signal/HitStop だけの演出はモデル不要)。Self 基準のトラック(Vfx/Se の Target=Self、Anim/Anim2D)は「Animator が見つかりません」等の既存の警告 1 回 + no-op で継続する(`PresentationManager` の既存動作のまま)。

要判断のまとめ(詳細は上記各項目):
- ~~Params(パラメータ上書き)の実消費経路が無い~~ → **2026-09-14 対応済み(5-R、下記「レビュー対応」参照。VFX のみ)**
- ~~HitStop がプレビュー内の Anim/Vfx/Shake/Haptic の Tick を止めない~~ → **2026-09-14 対応済み(5-R。Shake は仕様どおり対象外)**
- LazyLoad アセットはプレビュー開始時に事前解決していない(新規作成直後の Data はウィンドウの開き直しが必要)
- Bgm/Canvas/UiTween トラックは 5-4 時点でプレビュー未配線(警告 + no-op)

## レビュー対応（2026-09-14、P5 レビュー第 1 弾・5-4 追補）

5-4 の 2 つの要判断のうち、パラメータ上書き(VFX)と HitStop の対象範囲を対応した(SE のパラメータ上書きと LazyLoad 事前解決・Bgm/Canvas/UiTween 未配線は引き続き要判断/未対応のまま)。

- **(a) パラメータ上書き(VFX)**: `PresentationManager.ApplyVfxTrackParams`(`Runtime/Presentation/PresentationManager.cs`)を追加し、`FireVfx` が `_vfx.SpawnData` した直後に呼ぶ。対応付けは「`PresentationTrack.Params[i]` ↔ 参照先 `VfxData.Params[i].Label`」の**インデックス対応**(`PresentationTrack` にラベル用フィールドを追加しない = シリアライズ変更を避ける決定を維持)。既存の `VfxManager.SetParam(handle, label, value)`(Label 解決)へそのまま渡すだけで、新しい消費経路は作っていない。`track.Params` が参照先 `VfxData.Params` より長い場合は超過分を無視する(例外にしない)。`PresentationEditorWindow.Tracks.cs` の Params 折りたたみに、Vfx トラックのときだけ `[0]=Alpha, [1]=Size, …` 形式のインデックス対応ヒントを表示するようにした。HitStop トラックの `Params[0]`(秒数)の既存意味は変更していない。**SE(音量等)は対象外のまま(要判断)**: `AudioManager.SetVolume`/`SetPitch` という個別 API は存在するが、`SeData` には `VfxData.Params` に相当する「ラベル付き配列」が無く、同じ「インデックス↔Label」方式を機械的に適用できない。`Params[0]=音量` のような決め打ちの対応を新たに定義するのは要判断とし、今回は実装していない(新しい API も作らない、というチケットの制約どおり)。
- **(b) プレビュー中の HitStop**: `ScenePresentationPreviewDriver`/`SceneAnimPreviewDriver`/`SceneVfxPreviewDriver`/`EditorHapticsPreviewDriver` に任意の `TimeService timeService = null` を追加した(既定 null = 従来どおり Unscaled で単体使用可能。`AnimEditorWindow`/`ModelEditorWindow`/`Anim2DEditorWindow`/`AnchorEditorWindow`/`AnchorGroupEditorWindow`/`CameraFxEditorWindow`(HapticsEditor の「Test on Pad」)は今回一切変更していない)。`ScenePresentationPreviewDriver` は自分の `Time`(`TimeService`)を `AnimDriver`(内部の `Vfx` にも伝播)と `HapticsDriver` にだけ渡す。各ドライバの private `EditorTick` は、`EditorApplication.update` 由来の Unscaled dt に `timeService?.ScaledDeltaTime(dt)` を掛けてから自分の `Tick(dt)` を呼ぶようになった(`TimeService.Tick(unscaledDt)` 自体は呼ばない。呼ぶと HitStop の残り時間を複数箇所で減算してしまうため、状態を進めるのは `ScenePresentationPreviewDriver.Tick` が 1 フレームに 1 回だけ行い、他ドライバ側は `ScaledDeltaTime`(現在の `TimeScale` を読むだけの純関数)しか呼ばない)。**`ShakeDriver`(CameraFx)には渡していない**: ランタイムの `CameraFxManager` は HitStop 中も揺れを止めない仕様(Part A)のままであり、プレビューもそれに合わせて Unscaled のままにする(意図的。バグではない)。

このほか、レビュー第 1 弾(review1_editor.md)の指摘のうち PresentationEditor 自体に関わる 2 件も同時に直した:
- **Kind 変更時の Asset 不整合(P2-1)**: `PresentationEditorWindow.Tracks.cs` の `Kind` フィールドを専用コールバックにし、変更時に `Asset` を Undo 付きでクリアして行を再構築(`RefreshTracksList`)するようにした(以前は `Asset` の `ObjectField.objectType` が古い Kind のまま残り、`Kind=Se, Asset.Type=Vfx` のような不整合データが保存され得た)。
- **Signal レーンが無かった(整理)**: `Lanes` に `TrackKind.Signal` がどこにも属していなかったため `LaneIndexFor` のフォールバック(最後のレーン)に落ちていた。`HitStop / Marker` レーンに `Signal` を加え、ラベルも `HitStop / Marker / Signal` に変えた。

## 追補（2026-09-14、タイムラインのズーム・尺 0 対応・一時停止からの再開)

デザイナーが実際に PresentationEditor を使って確認した結果の 2 件のフィードバックに対応した。人による確認手順は [28_manual_verification_phase5.md](28_manual_verification_phase5.md) の「5-4 追補」節。

**(A) タイムラインのズーム(報告: 「シークバーでどこにいるか分からない。目盛りの表示範囲が狭すぎる」)**:

- **`PresentationTimelineZoom`(`Editor/Presentation/PresentationTimelineZoom.cs`、新規)**: ズーム/パン/目盛り間隔選択をすべて純粋関数(Unity オブジェクト非依存)にした静的クラス。`Fit`/`ClampRange`/`ZoomAroundPivot`(ホイールの相対倍率)/`WithZoomFactor`(スライダーの絶対倍率)/`ZoomFactor`/`Pan`/`FollowPlayhead`/`TimeToX`/`XToTime`/`ChooseTickStep`/`LabelStride` を持つ。表示範囲の最小幅(`MinVisibleRange`=0.1s)とズームスライダーの上限倍率(`MaxZoomFactor`=50)もここで定義する。
- **目盛り間隔の自動選択**: `TickStepCandidatesFineToCoarse = { 1/60s, 0.1s, 0.5s, 1s }` から、1 目盛りが `MinPxPerTick`(6px)以上になる最も細かい候補を選ぶ(無ければ最も粗い 1s で妥協し、ラベルは `LabelStride`(目標 46px 間隔)で間引く)。**`TimelineRulerGui`(`Editor/Common/TimelineRulerGui.cs`、AnimEditorWindow と共用)は一切改修していない** — Presentation は独立した `DrawTimeRuler`(`PresentationEditorWindow.Tracks.cs` 内 private static)を新設し、目盛りの描画ロジックを完全に分離した(Anim Editor の見た目・挙動への影響ゼロを優先し、要望にあった「引数を増やしたオーバーロード」より安全な方を選んだ)。
- **表示範囲(`_viewStart`/`_viewEnd`)はウィンドウの `[SerializeField]` フィールドで持ち、`PresentationData` にはシリアライズしない**(要求どおり)。`0,0` を「未初期化」の目印にし、`DrawTimeline` の毎フレームの先頭で `_viewEnd<=_viewStart` なら `Fit`、それ以外は現在の尺(`PresentationTimelineRange.DisplayDuration`、後述)に対して `ClampRange` するだけにした(TotalDuration を編集中でも表示が暴れない)。対象アセットを `SetTarget` で切り替えたときと、共通設定の「トラックの最後に合わせる」ボタンを押したときは明示的に `ResetViewToFit()` を呼ぶ。
- **操作**: ツールバー行(`BuildTimelineControlsRow`)の ± ボタン/ズームスライダー/「全体表示」ボタン/「再生ヘッドに追従」トグル(既定 ON)。タイムライン内の Ctrl(Cmd)+ホイールでカーソル位置を中心にズーム、単独ホイールでパン、下部の横スクロールバー(`DrawMiniScrollbar`。演出全体のミニマップ + 現在の表示範囲を示すつまみ。ドラッグ/空き領域クリックでパン)。ルーラー(タイムライン上段、`RulerHeight` 以内)のクリック/ドラッグはシーク(後述 `SeekToTime` 経由)にし、トラックマーカーのドラッグ(レーン側、`RulerHeight` 以降)とは Y 座標で完全に分離しているため競合しない。
- **再生ヘッド**: 目立つ黄色(`(1, 0.85, 0.15)`)の縦線を全レーン(ルーラー+レーン、ミニスクロールバーは除く)に描く。表示範囲の外に出たら描かない(「再生ヘッドに追従」OFF でスクロールしていない場合)。上部に現在時刻(秒、小数 2 桁)とフレーム数(**60fps を仮定した表示専用の値**。Presentation には固有フレームレートの概念が無いため。ランタイムの完了判定には無関係)を表示する。

**(B) 尺(TotalDuration)が 0 のときの表示範囲(実際の原因)**: ユーザーの追加報告により、「分からなかった」根本原因は目盛りの粗さではなく **`TotalDuration` 未設定のときにタイムラインの表示範囲そのものが潰れる**ことだったと判明した(`PresentationTiming.EffectiveDuration` が AtTime トラックの最大 `Time` を余白無しで返す、あるいはトラックが無ければ 0 を返すため、`Mathf.Max(0.01f, …)` で無理にクランプしていた旧実装では実質「幅 0.01 秒」の目盛りしか描けなかった)。

- **`PresentationTimelineRange`(`Editor/Presentation/PresentationTimelineRange.cs`、新規)**: **ランタイムの `PresentationTiming.EffectiveDuration` は変更していない**(完了判定の契約を維持する、というチケットの制約どおり)。表示専用に別関数を用意した。
  - `DisplayDuration(data)`: `TotalDuration>0` ならそのまま、`0` なら「AtTime トラックの最大時刻 + `AutoMargin`(0.5秒)」、AtTime トラックが 1 つも無ければ `FallbackNoTracksDuration`(1秒)。タイムラインの描画・ズームの「全体表示」・下部スクロールバーのミニマップ全長に使う。
  - `SuggestedTotalDuration(data)`: 「トラックの最後に合わせる」ボタンが設定する値。各 AtTime トラックの「終了時刻」(`Time` + 分かる場合はアセットの長さ、分からなければ `AutoMargin`)の最大値。アセットの長さは `EstimateAssetTailSeconds` がベストエフォートで見積もる(**Anim/Anim2D は `AnimData.LengthSec`、SE は `Clips` の最長 `AudioClip.length` − `StartOffsetSec`** だけ対応。VFX/BGM/CameraShake/Haptic はループ/曲線ベースで固定長を持たないため「分からない」扱いのまま — 要判断、将来各 Data 型に明示的な長さの概念が増えたら拡張する)。アセット解決(`AssetDatabase` 検索)を伴う無引数版と、テストでリゾルバを差し替えられる `SuggestedTotalDuration(data, Func<TrackKind,ulong,AssetDataBase>)` の 2 つを公開している。
  - シーク(`SeekToTime`、後述)は表示専用の `DisplayDuration` ではなく、**実際の再生時間 `PresentationTiming.EffectiveDuration` にクランプする**(表示上の余白部分へはシークできない、という仕様)。
- **`PresentationTrackKindMapping.FindAssetById`(`public` 化)**: 元は `PresentationEditorWindow.Tracks.cs` の private メソッドだったアセット ID→実体解決を、`PresentationTimelineRange`(アセットの長さ見積り)とウィンドウ側(Asset 欄の表示)の両方から共用するために `PresentationTrackKindMapping` へ移設した(コピペ禁止対応)。
- **`PresentationTrackEditOps.FitTotalDurationToTracks`(新規)**: 「共通設定」の尺 0 警告(HelpBox)にある「トラックの最後に合わせる」ボタンの実体。`Undo.RecordObject` 付きで `TotalDuration` を `PresentationTimelineRange.SuggestedTotalDuration` の値に設定するだけの薄いラッパー(既存の `AddTrack`/`RemoveTrack` 等と同じ「ウィンドウを起動せずにテストできる」設計)。タイムライン上部にも `TotalDuration<=0` のとき小さく「⚠ 尺が未設定です」を表示する。

**(C) 一時停止からの再開(報告: 「一時停止から再生するとシークバーで最初から再生になっている」)**: 原因は `PresentationEditorWindow.Preview.cs` の `Play()` が常に `_preview.Play(_target)`(最初から再生)を呼んでいたこと(一時停止中の「▶ 再生」はリスタートであり、再開は「⏸ 一時停止」をもう一度押す `TogglePause` しか経路が無かった)。加えて `OnEditorUpdate` がステータスラベルしか更新せず、シークスライダーが再生位置に追従しないため「今どこか」も分からなかった。

- **`PresentationPreviewPlayback`(`Editor/Presentation/PresentationPreviewPlayback.cs`、新規)**: 「▶ 再生」の挙動判定・シークスライダーの追従値・巻き戻し検出を純粋関数にした(`PresentationTrackEditOps` と同じ「ウィンドウを起動せずテストできる」設計)。
  - `DecideOnPlay(previewIsPlaying, windowPaused)`: 一時停止中(両方 true)だけ `Resume`、それ以外(停止中、または再生中に連打された場合)は従来どおり `StartFresh`。
  - `ComputeSeekSliderValue(isPlaying, normalizedTime)` / `IsRewind(previousElapsed, targetElapsed, epsilon)`。
- **`Play()`**: `PresentationPreviewPlayback.DecideOnPlay` が `Resume` を返したら `_preview.SetPaused(false)` だけ行う(最初から再生し直さない)。最初からやり直す手段として **「⏮ 最初から」ボタン(`Restart`)を追加**した(「▶ 再生」の意味が変わったための代替)。
- **「⏸ 一時停止」ボタンは一時停止中「▶ 再開」に表示が切り替わる**(`_pauseButton.text` を都度更新)。ステータス欄も「⏸ 一時停止中 42% (1.26s)」のように時刻(秒、小数 2 桁)を出すようにした。
- **シークスライダーは再生中・一時停止中とも現在位置へ `SetValueWithoutNotify` で追従する**(`OnEditorUpdate`)。ユーザーがスライダーをドラッグ中は上書きしない(`PointerDownEvent`/`PointerUpEvent` を `TrickleDown` で監視する `_seekSliderDragging` フラグ)。
- **`SeekToTime(absoluteSeconds)`(Preview.cs、新規の共通処理)**: シークスライダーとタイムラインのルーラー(上記 (A) の `_seekDragging`)の両方がこれを呼ぶ(値を共有する)。実際の再生時間にクランプし、シークスライダーも同じ値に同期する。巻き戻し(過去へのシーク)を検出したら、**その再生の最初の 1 回だけ**ログへ「巻き戻しでは発火済みのトラックは再発火しません。最初から確認するには ⏮」を出す(`_rewindNoticeShown`。`StartFresh`/`Stop` でリセットする)。既存のツールチップ(「巻き戻しでは既発火のトラックを再発火しない」)を消してはいない — ログはそれに追加する形。

## 実装メモ（2026-09-14、5-8）

`PresentationManager` に `INetBridge netBridge = null` を追加し、`Flags.Net == NetMode.Cosmetic` かつ `netBridge != null` のときだけネット経路(開始時刻シーク / Signal 中継 / 予測再生 / Late Join 復元)に乗るようにした。`PresentationData` に `PredictLocal` フィールドを追加した(シリアライズ追加のみ)。**詳細な設計・メッセージ定義・シーク規則・Late Join の接続通知の口は [14_networking.md](14_networking.md) §5「実装メモ（2026-09-14、5-8）」に集約した**(Presentation 固有の話だが、ネットワーク方針全体との整合を保つため §5 に一本化し、ここでは重複させない)。§3(実行モデル)・§3.5(Handle API)の契約(`Signal`/`Cancel` の意味、`AtTime(0)` の即時発火等)は変更していない — ネット経路でも「行為者から見た挙動」は同じ形を保ち、内部で Broadcast/受信シークに委譲しているだけである。

- **Cancel Interruptible=false のチェックは Broadcast より前**: [14] のとおり Cancel はネット経路の Instance では Broadcast してから自分を含む全員が受信して初めて止まるが、`Interruptible=false` の警告・no-op 判定自体はローカルで即座に行う(ネットワークを介さない。Broadcast 前に弾くので不要な通信をしない)。
- **Haptic の LocalPlayerOnly 誤爆防止**は Presentation 側(`PresentationInstance.PlayedViaNetworkReceive`)で吸収しており、`HapticsManager`/`HapticsData` 自体は無改修([16_camera_haptics.md] の既存「NGO 統合前は常にローカル再生扱い」という要判断を、Presentation 経由の再生に限って解消した形。Haptics を直接呼ぶ既存 API(`Haptics.Play`)は今回のスコープ外で未対応のまま)。
