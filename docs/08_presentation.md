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
                                       // UiTween/Marker/Signal/AnchorGroup(配置セット)
                                       // ※Shake/Haptic は [16]、AnchorGroup は [22] 参照
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
| Kind=AnchorGroup なのに Asset の種別が AnchorGroup ではない | Error |

## 実装メモ（2026-09-14、5-1）

実装: `Runtime/Presentation/{PresentationData,PresentationTrack,PlayContext,PresentationManager,PresentationHandle,Presentation,PresentationTiming,PresentationDataValidator}.cs`。専用エディタ(§4 PresentationEditor)は 5-4 でまだ未実装のため、`DataEditorRegistryTests` の Exempt に `PresentationData` を追加した(5-1 時点では Inspector から `Tracks` を直接編集する)。

- **R3 導入**: `Packages/manifest.json` に UnityNuGet scoped registry(`org.nuget` スコープ)を追加し `org.nuget.r3`(コア型 `Observable<T>`/`Unit`/`Subject<T>` を含む素の `R3.dll`) + `com.cysharp.r3`(git、`R3.Unity` の Unity 統合層。今回は使っていない)を導入。両方とも 1.3.1。**DLL 重複は発生しなかった**(`org.nuget.system.runtime.compilerservices.unsafe@6.0.0` が isuzu MCP 側のコピーと衝突する懸念があったが、`console_read_logs`/Editor.log に "Multiple precompiled assemblies" は出ず、isuzu MCP・compile/test も導入後に問題なく動作し続けた)。`DDrive.Runtime.asmdef`/`DDrive.Samples.asmdef`(`overrideReferences: false`)は R3.dll が自動参照されるため無編集で通ったが、`DDrive.Tests.Runtime.asmdef`(`overrideReferences: true`)は `precompiledReferences` に `"R3.dll"` を追記する必要があった。
- **PresentationHandle は Handle<TMarker> の薄いラッパー struct**(他種別のような拡張メソッドではなく、`Signal`/`Cancel`/`Pause`/`Resume`/`SetSpeed`/`Seek`/`NormalizedTime`/`IsPlaying`/`OnCompleted`/`OnCancelled`/`OnMarker`/`OnTrackFired`/`WaitAsync` を直接メンバーに持つ readonly struct)。中身は `Handle<PresentationMarker>` 1 個のみで GC alloc 0。`WaitAsync` は `UniTask.WaitUntil` のポーリングではなく `UiTweenManager` と同じ `UniTaskCompletionSource` 方式(Complete/Cancel 時に同期的に `TrySetResult`)。
- **Kind の委譲先(実装済み)**: Anim/Anim2D → `AnimManager.PlayData`(対象 Animator は `TrackTargetMode` で決めた Transform から `GetComponentInChildren<Animator>` で解決) / Se → `AudioManager.PlaySeData` / Bgm → `BgmManager.PlayBgmData` / Vfx → `VfxManager.SpawnData` / Canvas → `UiManager.Open` / UiTween → `UiTweenManager.PlayData`(対象は `RectTransform`) / **CameraShake → `CameraFxManager.ShakeData`(2026-09-14、5-2)** / **Haptic → `HapticsManager.PlayData`(2026-09-14、5-2b)** / **AnchorGroup → `AnchorGroupPlayer.PlayData`(2026-09-19、下記実装メモ参照)**。**Timeline は 6-10a で `CutsceneManager.PlayData` に接続済み**。
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
- **「確認用シーンを開く」がモデルを配置するようになった（U-6、2026-09-17）**: 以前は「確認用シーンを開く」がシーンを開くだけで、モデル配置は統合プレビュー内の別ボタン「配置」だったため、押しても確認できる状態にならなかった（ユーザー報告「PresentationEditor ちゃんと確認用シーンで開くこと」）。Model / Anim / Anim2D と同じく「止める → `VfxPreviewSceneSetup.TryOpenOrCreate` → モデル配置 → SceneView をフォーカス」を 1 ボタンで行う。「配置」ボタンも同じ共通部品（[09_editor_tools.md] §2.1 の `PreviewPlacementButton`）にしたので、右クリックで「このシーンに配置 / このシーンに本配置」も選べる（本配置は `ModelData.Prefab` を `PrefabUtility.InstantiatePrefab` で置くだけで、`ctx.Self` にはしない）
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
- **シークバーは再生中・一時停止中とも現在位置へ追従する**(`OnEditorUpdate` が毎フレーム `_seekBarContainer.MarkDirtyRepaint()` を呼び、`DrawSeekBar` が `_preview.NormalizedTime` を読んで再生ヘッドを描き直す)。**2026-09-17(U-7)にシークバー自体を UI Toolkit の `Slider` から `IMGUIContainer`(`SeekBarGui`)描画に置き換えたため、ドラッグ中フラグ(`_seekSliderDragging`)は廃止した** — 詳細は下の「追補（2026-09-17、U-7）」参照。
- **`SeekToTime(absoluteSeconds)`(Preview.cs、共通処理)**: シークバーとタイムラインのルーラー(上記 (A) の `_seekDragging`)の両方がこれを呼ぶ(値を共有する)。実際の再生時間にクランプする。巻き戻し(過去へのシーク)を検出したら、**その再生の最初の 1 回だけ**ログへ「巻き戻しでは発火済みのトラックは再発火しません。最初から確認するには ⏮」を出す(`_rewindNoticeShown`。`StartFresh`/`Stop` でリセットする)。既存のツールチップ(「巻き戻しでは既発火のトラックを再発火しない」)を消してはいない — ログはそれに追加する形。

## 追補（2026-09-17、U-7 — シークバーを Anim Editor と同じ形に）

要望([39_usability_fixes_2026-09-17.md](39_usability_fixes_2026-09-17.md) U-7)「Presentation Editor のシークバーを Anim Editor のシークバーと同じ形にする」対応。統合プレビューの「シーク」は UI Toolkit の丸ノブ `Slider` で、`AnimEditorWindow`(3-3)の暗い背景 + 目盛り付きバー + 白い再生ヘッド + クリックでシークという「バー」の見た目とは違う形をしていた。

- **`SeekBarGui`(`Editor/Common/SeekBarGui.cs`、新規)**: `AnimEditorWindow.DrawTimeline` から「背景(暗い矩形)」「目盛り付きバー(`TimelineRulerGui.DrawTicks` を内部で呼ぶ)」「白い再生ヘッド」「バー領域のクリックを 0..1 の位置に変換する」の 4 つを共通ヘルパーとして切り出した(`DrawBackground`/`DrawBar`/`DrawPlayhead`/`TryHandleClickSeek`)。既定のピクセル位置(マージン 8px・バー開始 y=18px・バー高さ 8px・再生ヘッドのはみ出し 8px)は `AnimEditorWindow.DrawTimeline` の元の値をそのまま既定値にしており、**Anim Editor 側の見た目は変えていない**(イベントマーカー・SE 波形・イベントのドラッグなど Anim 固有の描画/操作はこれまでどおり `AnimEditorWindow.DrawTimeline` 側に残る)。
- **`PresentationEditorWindow.Preview.cs`**: `_seekSlider`(`Slider`)と `_seekSliderDragging` を廃止し、`_seekBarContainer`(`IMGUIContainer` → `DrawSeekBar`)に置き換えた。`DrawSeekBar` は `SeekBarGui` で背景・バー(目盛りは 1 秒刻み)・再生ヘッド(`_preview.NormalizedTime`。Handle 無効なら -1 で非表示、Anim と同じ判定)を描き、クリックで `SeekToTime` を呼ぶ。UI Toolkit の値変更イベントを使わなくなったため、ドラッグ中フラグでの上書き防止(`PointerDownEvent`/`PointerUpEvent`)は不要になった(クリックのみでドラッグでの連続シークは元々無い、Anim と同じ)。
- Presentation にはトラック編集用の詳細タイムライン(`PresentationEditorWindow.Tracks.cs`。ズーム/パン/複数レーン、上記 (A))が別に存在する。**これは今回の対象外**(ズーム対応の `DrawTimeRuler` は Anim 側の見た目に影響しないよう独立させる方針を継続。上記「(A) タイムラインのズーム」参照)。新しいシークバーは、再生位置の確認・簡易シークに絞った単純な 1 本のバーとして統合プレビュー欄に残す。
- `PresentationPreviewPlayback.ComputeSeekSliderValue` は関数名・シグネチャとも変更していない(EditMode テスト `PresentationPreviewPlaybackTests` が参照する純粋関数。「シークバーに表示する正規化位置」を返す意味は変わっていない)。

## 追補（2026-09-17、U-25 — Signal を手動で送る導線を分かりやすくする）

要望([39_usability_fixes_2026-09-17.md](39_usability_fixes_2026-09-17.md) U-25、[36_manual_screenshot_list.md](36_manual_screenshot_list.md) #53)「Presentation の Signal を手動で送る操作のやり方が分からない」対応。§4 の「Signal レーン」自体(`PresentationEditorWindow.Preview.cs` の `RefreshSignalButtons`)は既に実装済みで、Trigger=On Signal のトラックがあれば統合プレビュー内に `Signal Key` ごとのボタンが並んでいたが、次の 2 点が伝わりづらかった。

- 再生していない間にボタンを押しても `Manager.Signal(Current, key)` が無効な Handle への no-op になるだけで、見た目には何も起きない(ボタン自体は押せる状態のまま、成功したのか失敗したのか区別がつかない)
- ボタンが出る場所(「統合プレビュー」フォールドアウトの中の、さらに「Signal レーン(手動発火)」フォールドアウトの中)へたどり着く手順がマニュアルの文章だけでは分かりにくかった

対応(コードのみ。新しい再生経路は作らず、既存の `ScenePresentationPreviewDriver.Signal`/`RefreshSignalButtons` に手を入れた):

- **`PresentationEditorWindow.Preview.cs`**: 「Signal レーン(手動発火)」フォールドアウトの先頭に、手順(①上の「▶ 再生」を押す → ②再生中に Signal ボタンを押す)を明文化した `Label` を追加した
- 各 Signal ボタンに `tooltip = "再生中のみ有効です。まず上の「▶ 再生」を押してください。"` を追加し、**再生中でなければボタンを `SetEnabled(false)` でグレーアウト**するようにした(`RefreshSignalButtons` が構築時点の再生状態を反映し、`UpdateSignalButtonsEnabledState(bool playing)` を新設して `PresentationEditorWindow.OnEditorUpdate`(既存の毎フレーム更新ループ、ステータスラベルやシークバーの追従と同じ場所)から呼び、再生開始/停止のたびに追従させる)
- OnSignal トラックが 1 つも無いときの案内文を「(OnSignal トラックがありません)」→「(On Signal のトラックがありません。Trigger=On Signal のトラックを追加するとここにボタンが出ます)」に変更し、そもそも表示条件が何かも分かるようにした
- **`docs/DesignerManual/presentation.html`**: 「Signal を手動で送る」の段落を 2 段階の手順(①再生 ②Signal ボタン)として書き直し、停止中はグレーアウトすることも明記した。スクリーンショット #53 のプレースホルダを撮影可能な `<figure>` に差し替えた(実際の撮影はユーザー作業。[36_manual_screenshot_list.md] 側の該当行から「U-25 が未着手」の但し書きを外した)
- ランタイム API(`Presentation.Signal`/`PresentationHandle.Signal`)・`ScenePresentationPreviewDriver.Signal` 自体の挙動は変更していない(UI 側の分かりやすさのみの改善)

## 実装メモ（2026-09-14、5-8）

`PresentationManager` に `INetBridge netBridge = null` を追加し、`Flags.Net == NetMode.Cosmetic` かつ `netBridge != null` のときだけネット経路(開始時刻シーク / Signal 中継 / 予測再生 / Late Join 復元)に乗るようにした。`PresentationData` に `PredictLocal` フィールドを追加した(シリアライズ追加のみ)。**詳細な設計・メッセージ定義・シーク規則・Late Join の接続通知の口は [14_networking.md](14_networking.md) §5「実装メモ（2026-09-14、5-8）」に集約した**(Presentation 固有の話だが、ネットワーク方針全体との整合を保つため §5 に一本化し、ここでは重複させない)。§3(実行モデル)・§3.5(Handle API)の契約(`Signal`/`Cancel` の意味、`AtTime(0)` の即時発火等)は変更していない — ネット経路でも「行為者から見た挙動」は同じ形を保ち、内部で Broadcast/受信シークに委譲しているだけである。

- **Cancel Interruptible=false のチェックは Broadcast より前**: [14] のとおり Cancel はネット経路の Instance では Broadcast してから自分を含む全員が受信して初めて止まるが、`Interruptible=false` の警告・no-op 判定自体はローカルで即座に行う(ネットワークを介さない。Broadcast 前に弾くので不要な通信をしない)。
- **Haptic の LocalPlayerOnly 誤爆防止**は Presentation 側(`PresentationInstance.PlayedViaNetworkReceive`)で吸収しており、`HapticsManager`/`HapticsData` 自体は無改修([16_camera_haptics.md] の既存「NGO 統合前は常にローカル再生扱い」という要判断を、Presentation 経由の再生に限って解消した形。Haptics を直接呼ぶ既存 API(`Haptics.Play`)は今回のスコープ外で未対応のまま)。

## 実装メモ（2026-09-19、SceneView に Anchor を表示）

**ユーザー要望**: 「PresentationEditor でトラックの Anchor がシーン上のどこか分からない。SceneView に表示するボタンを付け、複数あるときは単体表示もできるようにし、表示は VFX Editor の Anchor 表示と同じにし、ギズモ(ハンドル)での操作もできるようにする」。

### 実効 Anchor の解決(2026-09-19 時点。トラック/アセット両方の Anchor 参照を実装する前の調査結果)

実装を読んで確認した結果、**トラックの実際の再生位置を決めるのは Kind=Vfx/Se のときの `PresentationTrack.Anchor`(トラック自身が持つ埋め込み `AnchorDef`)だけ**であることが分かった。優先順位の分岐は無い。

| Kind | 位置 | 決定要因 |
|---|---|---|
| Vfx | あり | `PresentationTrack.Anchor` + `PresentationTrack.Target`(contextRoot の決定)のみ |
| Se | あり | 同上 |
| Anim / Anim2D / Bgm / CameraShake / Haptic / HitStop / Timeline / Canvas / UiTween / Marker / Signal | なし | `Target` は Animator/RectTransform の検索先やアニメーションの再生対象を決めるのに使うことはあるが、空間上の「出す位置」は持たない(CameraShake は `PlayContext.Position` を直接使うのみで `Anchor` を消費しない) |

根拠: `PresentationManager.FireVfx`/`FireSe` は必ず `AnchorSpawnSpec.FromDef(track.Anchor)` を「合成済み(presolved)」として `VfxManager.SpawnData(data, in spec, root)` / `AudioManager.PlaySeData(data, in spec, root, seed)` へ渡す。この経路(`SpawnDataLocal` の `presolved` 引数)は `anchorOverride > Data.AnchorId > Data.Anchor` の優先順位を解く `ResolveAnchorSpec` を一切呼ばない。**つまり参照先 VfxData/SeData 自身の `AnchorId` も埋め込み `Anchor` も、Presentation 経由の再生では絶対に使われない。** これは新しい発見ではなく、[43_manual_verification_2026-09-17.md](43_manual_verification_2026-09-17.md) §6「Presentation に Anchor 上書きが無い」で既に指摘されていた既知事象と一致する。

**→ 2026-09-19、同日中にユーザー決定により仕様変更した。最新の優先順位・合成規則は下の「実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照)」を参照。**

また(2026-09-19 時点)`TrackKind` に `AnchorGroup`(配置セット)は存在しない。[22_anchor_group.md](22_anchor_group.md) §5 で「Presentation 統合(トラック種別 AnchorGroup)は Phase 5」と予告されていたが、実装済みの Kind 一覧(Anim/Anim2D/Se/Bgm/Vfx/CameraShake/Haptic/HitStop/Timeline/Canvas/UiTween/Marker/Signal)には含まれておらず、未実装のまま今日に至っている。したがって「AnchorGroup トラック」は作ることも描くこともできない。**→ 同日中に本チケットで実装した(下記「実装メモ(2026-09-19、AnchorGroup トラック)」参照)。**

### SceneView 表示(`PresentationEditorWindow.SceneAnchors.cs`、新規)

- 「トラック一覧」の上に「SceneView 表示」トグル(既定 ON。VFX Editor / Anchor Editor と同じ文言・流儀)と「表示対象」(すべて / 選択中のみ)を追加した。**「選択中」はトラック一覧の選択(`_selectedTrack`。タイムラインのマーカークリック・行の展開と共有している既存の状態)をそのまま使う**(表示専用の別の選択状態を増やすとトラック一覧の選択とズレるため)
- 「すべて」表示時は、位置を持つ全トラック(Vfx/Se)を番号付きの点(クリックで選択に切り替え、`AnchorGroupEditorWindow` の点選択と同じ操作感)として表示する。**移動/回転ハンドル(編集可能なギズモ)が出るのは選択中の 1 本だけ**(全トラックに常時ハンドルを出すとドラッグの取り違えが起きやすいため。AnchorGroupEditorWindow が「全点は点で表示、選択点だけフルハンドル」としているのと同じ設計判断)
- ラベルは `"[{index}] {Kind} {アセット表示名}"`。色は Kind ごとに固定(Vfx=マゼンタ `(0.9, 0.4, 0.85)`、Se=シアン `(0.3, 0.85, 0.95)`)で、VFX Editor の埋め込み Anchor 表示(teal 系 `(0.35, 0.85, 0.65)`)・Anchor Editor(黄 `(0.95, 0.75, 0.3)`)・Anchor Group Editor(水色 `(0.4, 0.8, 1)`)のいずれとも衝突しない配色にした
- 描画・ハンドルの逆変換は VFX Editor / Anchor Editor と同じ `Editor/Preview/AnchorSceneHandles.cs`(`DrawOrigin`/`DrawOffsetLink`/`Draw`/`DrawInactiveMarker`)を通す(コピペしない)。基準(原点)の描画・「⚠ … → ワールド原点」の表記は [21_anchor_spec.md] §3.10 のとおり
- `SceneGuiOwner` による描画権の調停(最後にフォーカスしたウィンドウだけがハンドルを描く)も既存の流儀のまま
- **「Kind ごとの実効 Anchor の解決」は `Editor/Presentation/PresentationTrackAnchorResolver.cs`(ウィンドウ非依存の純関数)に切り出し、EditMode テスト(`Tests/Editor/PresentationTrackAnchorResolverTests.cs`)で検証した**: `HasPosition(kind)` が Vfx/Se のみ true になること、`Target`(Self/ContextTarget/World/Anchor)ごとの contextRoot 解決、Path 未解決時のワールド原点フォールバック。共通処理 `PresentationManager.ResolveContextRoot` は `private` から `public static` に変えて Editor 側から直接再利用した(コピペしない。`Tests/Runtime/PresentationManagerTests.cs` に回帰テストを追加)
- 統合プレビューは常に `ctx.Target = null` で再生する(`ScenePresentationPreviewDriver.Play`)。したがって `Target=ContextTarget` のトラックはプレビュー中は常にワールド原点扱いになる(実際の挙動どおりに表示される。バグではない)

### 編集(ギズモ)の書き戻し先

実効 Anchor が常に `PresentationTrack.Anchor` である以上、**編集の書き戻し先も常にこのトラック自身**であり、参照先 VfxData/SeData や AnchorData のような共有アセットを書き換えることは無い(VFX Editor の「AnchorId 使用時は参照先 AnchorData を書き換える」という分岐に相当するものは、Presentation には存在しない)。`Undo.RecordObject(_target, ...)` + `EditorUtility.SetDirty(_target)` + `_serializedTarget.Update()` は他のトラック編集(Kind/Time/Params 等)と同じ流儀。

**再生中の実体への即時反映(ライブリアプライ)はしていない(意図的な判断)**: `VfxManager.ReapplyAnchor`(VFX Editor が `ApplyAnchorChange` から呼んでいるもの)は `instance.AnchorSource`(AnchorId)が無効なら `instance.Data.Anchor`(参照先 VfxData の埋め込み Anchor)から再合成する実装になっている。Presentation 経由で生成された実体は `AnchorSource` が常に無効(`presolved` 経由のため)なので、これをそのまま呼ぶと `track.Anchor` ではなく `VfxData.Anchor`(多くの場合デフォルト値)へ位置が飛んでしまう。安全側に倒し、SceneView でトラックの Anchor を動かしても再生中の実体はその場では動かない。変更は次に「▶ 再生」/「⏮ 最初から」を押したときから反映される(Presentation Editor の他のトラック編集がすべてそうであるのと同じ)。ライブリアプライを実現するには `VfxManager`/`AudioManager` に「track.Anchor をそのまま再適用する」経路を新設する必要があり、本チケットのスコープ外とする(要判断として残す)。

### 変更ファイル

| 層 | ファイル |
|---|---|
| Runtime | `Runtime/Presentation/PresentationManager.cs`(`ResolveContextRoot` を `public static` 化。ロジック自体は無変更) |
| Editor | `Editor/Presentation/PresentationTrackAnchorResolver.cs`(新規)、`Editor/Presentation/PresentationEditorWindow.SceneAnchors.cs`(新規、partial)、`Editor/Presentation/PresentationEditorWindow.cs`(SceneView 購読の配線・UI 呼び出し追加) |
| Tests | `Tests/Editor/PresentationTrackAnchorResolverTests.cs`(新規)、`Tests/Runtime/PresentationManagerTests.cs`(`ResolveContextRoot` の回帰テスト追加) |
| docs | 本節、[09_editor_tools.md] §2.3、`docs/DesignerManual/presentation.html`、[43_manual_verification_2026-09-17.md] |

### 未確認・要判断

- ライブリアプライ(再生中の実体へ即時反映)は上記のとおり未実装。次の Play/Restart まで反映されない
- ~~「Presentation に Anchor 上書きが無い」(VfxData/SeData の AnchorId が Presentation 経由では効かない)こと自体が仕様として正しいのか~~ → **2026-09-19、同日中にユーザー決定・実装済み。下の「実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照)」を参照**

## 実装メモ（2026-09-19、AnchorGroup トラック — [22_anchor_group.md] §5 Presentation 統合）

**ユーザー決定(2026-09-19)**: 上の「SceneView に Anchor を表示」実装メモで判明した「`TrackKind` に `AnchorGroup` が無い」欠落を埋め、PresentationEditor で配置セット(`AnchorGroupData`)を 1 本のトラックとして置けるようにする。

- **`TrackKind.AnchorGroup` を末尾追加**(`Runtime/Presentation/PresentationTrack.cs`)。既存値は不変、YAML の整数値はそのまま(既存アセットは壊れない)。
- **`PresentationManager.FireAnchorGroup`**: `ResolveContextRoot(ctx, track.Target)` で決めた contextRoot をそのまま `AnchorGroupPlayer.PlayData(group, contextRoot)` に渡す薄い委譲(`TrackTargetMode` の解釈は Vfx/Se と同じ)。`StopOnCancel=true` のときだけ Handle を `FiredAnchorGroup` に積み、`Cancel()` 経由で `AnchorGroupPlayer.Stop` する(`FiredVfx`/`FiredSe`/`FiredCutscene` と同じパターン)。`_groups`(コンストラクタ引数 `AnchorGroupPlayer groups = null`、既定 null = 他の Manager と同じ「未配線」警告 + no-op)。
- **Seed(ネット同期済み乱数)は消費しない**: `PresentationInstance.Seed`(ネット受信側で全クライアント同じ値になるよう同期済み)は `FireSe`/`FireCameraShake` 等と違い `FireAnchorGroup` には渡していない。`AnchorGroupPlayer.PlayData(group, contextRoot)` に Seed 引数が無く(`AnchorGroupPlanner.Plan(sampleRandom: true, ...)` を呼ぶだけで、各点のランダム(位置ジッタ・ディレイジッタ・確率)は呼び出しのたびに `UnityEngine.Random` から独立にサンプリングする設計、[22] §3.2/§3.4)、Seed を受け取る API 自体が存在しない。[14_networking.md] §12 のとおり「`AnchorPoint` のランダム散らばりは見た目専用なので各自ローカルで可(結果に影響しない)」という既定方針とも一致するため、新しい API は追加せずこの制約をそのまま受け入れた(Cosmetic 配送でも各クライアントが独立にサンプリングした配置になるが、AnchorGroup は元々「見た目の散らばり」用途であり許容できる)。
- **Validator(`PresentationDataValidator`)**: Kind=AnchorGroup で `Asset.Type != AssetType.AnchorGroup`(取り違え)なら Error。Asset 未設定は既存の `RequiresAsset` 汎用チェックがそのまま拾う(AnchorGroup を `RequiresAsset` の例外〔Marker/Signal/HitStop〕に加えていないため)。
- **`PresentationTrackKindMapping`**: `AssetTypeFor`/`AssetKindFor`/`TryKindFor`/`LaneColor` に AnchorGroup を追加(D&D で `AnchorGroupData` を落とすと Kind=AnchorGroup のトラックが作られる)。
- **レーン割り当て**: AnchorGroup は **Vfx と同じレーンにまとめた**(レーンラベルを「Vfx」→「Vfx / AnchorGroup」に変更。既存のレーン数・高さ・他 Kind の配置は変えていない)。「Vfx / AnchorGroup」用の新レーンを増設する案もあったが、レーン数が増えるとタイムラインが縦に伸び既存の見た目(6 行)を壊すため、役割が近い(どちらも「対象に VFX/SE を出す」)Vfx のレーンに同居させる方を選んだ。
- **統合プレビュー(`ScenePresentationPreviewDriver`)への Player 注入 — 設計判断**: `SceneAnimPreviewDriver`(以下 AnimDriver)が内部に持つ `AnchorGroupPlayer`(`_groups`、Animation フレームイベントの配置セット再生・`AssetEventDispatcher` と共有)を **そのまま公開して共有する**方式を選んだ(`SceneAnimPreviewDriver.Groups` プロパティを新設)。Vfx/Audio を束ねた**別インスタンス**を新設する案もあったが、以下の理由で共有を選んだ。
  - AnimDriver 自身がすでに「1 つの `_groups` を Animation イベント経由の再生と共有する」設計(`_dispatcher.OnGroupPlayed += OnGroupPlayed`)になっており、別インスタンスにすると「同じ Vfx/Audio Manager に対して 2 つの `AnchorGroupPlayer` が並存する」歪な構成になる(`AnchorGroupPlayer` 自体は状態〔`_active`/`_free`〕を持つため、二重に持つ意味がない)。
  - **Adopt(再生中の VFX の追従・停止)を正しく効かせるため**: `SceneVfxPreviewDriver`(AnimDriver.Vfx)は EditMode で `ParticleSystem` を自動シミュレートしない(`EditModeParticleStepper.Step` による手動 Simulate が必須、`SceneVfxPreviewDriver.Tick` のコメント参照)ため、`VfxManager.SpawnData` で直接生成した VFX(Presentation の `FireVfx`/`FireAnchorGroup` はいずれもこの経路)は `SceneVfxPreviewDriver.Adopt(handle)` で台帳(`_active`)に登録しない限り SceneView で静止したまま(手動 Simulate されない)。共有方式では `PresentationManager.OnAnchorGroupPlayed`(新設。開発/確認ツール専用、`OnNetworkReceivedPlay` と同じ設計の event)を `ScenePresentationPreviewDriver` が購読し、AnimDriver.`AdoptGroupVfx`(既存)と全く同じ考え方で「Handle を台帳に積む → 毎 Tick `AnchorGroupPlayer.CollectVfxHandles` で VFX Handle を集めて `AnimDriver.Vfx.Adopt` する」処理を追加した(`ScenePresentationPreviewDriver.OnGroupPlayed`/`AdoptGroupVfx`、AnimDriver 側のコードをコピペせず同じ公開 API を再利用しただけ)。
  - **タイミングの注意**: `AnimDriver.Groups`(`_groups`)は `AnimDriver.EnsureManagers()`(`SpawnModel`/`PreviewSe`/`PreviewVfx` 等で初めて呼ばれる)が済むまで `null` のままのことがある。`ScenePresentationPreviewDriver` はコンストラクタで一度 `PresentationManager` を構築する(`EnsureAudio()` 経由)ため、「配置 → 再生」という通常の操作順でも、コンストラクタ時点ではまだ `AnimDriver.Groups` が `null` だったケースが起こり得る。これを避けるため、`Play()` の冒頭(`StopCurrent()` の直後、副作用なし)で毎回 `RebuildManager()` を呼び直すようにした(以前は `EnsureAudio()` が `_root` 生存中は no-op のため、実質コンストラクタ時の 1 回しか `PresentationManager` を作り直していなかった)。
  - **既存の Vfx/Se トラックの Adopt は本チケットのスコープ外**: 調査の結果、既存の `FireVfx`/`FireSe` も同じ理由(`VfxManager.SpawnData` を直接呼ぶだけで `Adopt` していない)で、統合プレビューの EditMode では厳密には手動 Simulate の対象外になっている可能性があるが、これは AnchorGroup 追加前から存在する挙動であり本チケットでは変更していない(要判断として残す。人による確認手順に追記した)。
- **`AssetType.AnchorGroup` は新規追加ではない**: [22_anchor_group.md] の時点(2026-09-08)で `AnchorGroupData` 自身の種別として既に追加済み。今回追加したのは `TrackKind.AnchorGroup`(Presentation のトラック種別。別の enum)のみ。
- **`DDriveRuntimeBootstrap`**: `Presentation = new PresentationManager(..., cutscene: Cutscene, groups: Groups)` に 1 引数(`groups: Groups`)を追加しただけ(`Groups`〔`AnchorGroupPlayer`〕は既存の生成物をそのまま渡す。ランタイムの `Anchors.Play`/イベント経由の配置セット再生とは独立した別の呼び出し経路が増えるだけで、既存の挙動は変えない)。
- **テスト**: `Tests/Runtime/PresentationAnchorGroupTests.cs`(新規、PlayMode): AnchorGroup トラックが `AnchorGroupPlayer.PlayData` を呼ぶこと・`StopOnCancel=true` で `Cancel()` 時に `AnchorGroupPlayer.Stop` されること・`StopOnCancel=false` では止まらないこと(Vfx/Se と同じ規則)・`groups` 未設定なら警告 1 回 + no-op(例外にしない)。`Tests/Runtime/PresentationDataValidatorTests.cs` に Asset 種別不一致の Error/正しい種別で Error 無しの 2 件。`Tests/Editor/PresentationTrackAnchorResolverTests.cs` に `HasPosition(AnchorGroup)==true` と `ResolveAnchorGroupPoints`(Grid 3×3 が 9 点、null Group で 0 件)の 2 件。`Tests/Editor/PresentationTrackKindMappingTests.cs` の各 `TestCase` に AnchorGroup を追加。

### 変更ファイル(2026-09-19、AnchorGroup トラック)

| 層 | ファイル |
|---|---|
| Runtime | `Runtime/Presentation/PresentationTrack.cs`(`TrackKind.AnchorGroup` 追加)、`Runtime/Presentation/PresentationManager.cs`(`FireAnchorGroup`/`FiredAnchorGroup`/`OnAnchorGroupPlayed`/コンストラクタ引数 `groups`)、`Runtime/Presentation/PresentationDataValidator.cs`(Asset 種別不一致 Error)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(`groups: Groups` を渡す 1 行) |
| Editor | `Editor/Presentation/PresentationTrackKindMapping.cs`、`Editor/Presentation/PresentationEditorWindow.Tracks.cs`(レーン)、`Editor/Presentation/PresentationTrackAnchorResolver.cs`(`HasPosition`/`ResolveAnchorGroupPoints` 追加)、`Editor/Presentation/PresentationEditorWindow.SceneAnchors.cs`(`DrawAnchorGroupPoints` 追加)、`Editor/Presentation/ScenePresentationPreviewDriver.cs`(Groups 注入・Adopt)、`Editor/Anim/SceneAnimPreviewDriver.cs`(`Groups` プロパティ新設) |
| Tests | `Tests/Runtime/PresentationAnchorGroupTests.cs`(新規)、`Tests/Runtime/PresentationDataValidatorTests.cs`、`Tests/Editor/PresentationTrackAnchorResolverTests.cs`、`Tests/Editor/PresentationTrackKindMappingTests.cs` |
| docs | 本節、[22_anchor_group.md] §5、[02_core_framework.md] §14、`docs/DesignerManual/presentation.html`・`anchor-group.html`、[43_manual_verification_2026-09-17.md] |

### 未確認・要判断(2026-09-19、AnchorGroup トラック)

- Unity MCP(CoplayDev)でのコンパイル・EditMode/PlayMode テストの実行結果は本節末尾の報告を参照(未検証ならその旨明記する)
- SceneView での実際の見た目(全点の番号付き表示・色・ラベル)・統合プレビューでの Adopt(VFX が SceneView で実際に動いて見えるか)は人による確認が必要([43_manual_verification_2026-09-17.md] に項番追記)
- 既存の Vfx/Se トラックが統合プレビューの EditMode で Adopt されていない疑い(上記)は本チケットのスコープ外のまま
- コンパイル・EditMode(879/879)・PlayMode(721/721)はいずれも green(Unity MCP、CoplayDev 版)。**SceneView での実際の見た目・ハンドル操作・複数ウィンドウの描画権切替は未確認**([43_manual_verification_2026-09-17.md] §8 の手順を参照)

## 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照)

**ユーザー決定**: 「Presentation に Anchor 上書きが無い」(上の「実効 Anchor の解決」節で確定した事実。VfxData/SeData の `AnchorId`/埋め込み `Anchor` が Presentation 経由では一切使われない)を仕様として解消する。Vfx/Se トラックの実効 Anchor は、**トラック自身の Anchor** と **参照先 VfxData/SeData の Anchor(AnchorId の連鎖、または埋め込み Anchor)** の両方を見るように変更した。

### 優先順位(新)

| # | 条件 | 挙動 |
|---|---|---|
| 1 | アセット側だけ設定されている(トラックの Anchor が既定値) | アセット側の Anchor を使う(`VfxManager.ResolveAnchorSpec`/`AudioManager` と同じ優先順位: AnchorId の連鎖 > 埋め込み Anchor、[21_anchor_spec.md] §3.3) |
| 2 | トラック側だけ設定されている(アセット側が既定値 / AnchorId 無し) | 従来どおりトラックの Anchor(`PresentationTrack.Anchor`) |
| 3 | 両方設定されている | **トラックの Anchor を親、アセット側の Anchor をその子として合成する**(`AnchorChain.Compose` と同じ合成規則。アセット側が AnchorId の連鎖なら「トラック Anchor → 連鎖のルート → … → 末端」の順で合成する) |
| (両方既定値) | — | 従来どおりワールド既定(`AnchorDef.WorldDefault`) |

### 「設定されている」の判定(確定した事実)

- **トラック側**: `PresentationTrack.Anchor` が `AnchorDef.WorldDefault` と等価でないこと。`AnchorDef` に `IsDefault`(`Equals(WorldDefault)`)/`Equals`/`GetHashCode`(`IEquatable<AnchorDef>`)を追加した(`Assets/DDrive/Foundation/Data/AnchorDef.cs`。フィールド追加・型変更はしていない)。**`LocalScale` は `(0,0,0)` と `(1,1,1)` を同じ意味として扱う**(`AnchorPose.BaseScale` が `LocalScale==0` を 1 として扱う既存規則に合わせたもの。これにより struct の裸の既定値〔全フィールド 0〕と `WorldDefault`〔`LocalScale=one`〕が「実質同じ既定値」になる)。`Path` は `null` と空文字列を同一視する
- **アセット側**: `AnchorId.IsValid`、または埋め込み `Anchor` が `IsDefault` でないこと(`VfxData.Anchor` は元々 `AnchorDef.WorldDefault` を既定値にしているため、未編集なら「設定されていない」判定になる。`SeData.Anchor` はフィールド初期化子が無い〔裸の既定値〕が、上記の正規化により同じく「設定されていない」判定になる)
- 両方既定値なら従来どおりワールド既定

### 子(アセット側)の Space/Path の扱い(確定した事実)

`AnchorChain.Compose` の既存規則をそのまま踏襲する: **合成後の `Space`/`Path`/`FollowRotation`/`DetachOnStop` は常にルート(ケース3ではトラック、ケース1ではアセット連鎖の最上段)の値になり、子(アセット側の各段)のこれらの値は無視される**。子の `LocalOffset`/`LocalEuler`/`LocalScale` だけが、親で決まった姿勢を基準に積まれる。

### 実装(合成を 1 か所に集約)

`Runtime/Presentation/PresentationTrackAnchorComposer.cs`(新規、静的クラス)に集約した。

- **`AnchorChain` の一般化(`Runtime/Anchoring/AnchorChain.cs`)**: 合成アルゴリズムの本体を `AnchorData[]` 専用の `Compose` から、`AnchorChainNode`(新規 struct。`AnchorDef` + ジッター/ディレイ/確率フィールドを持つ値型)の配列を受け取る `ComposeNodes` に切り出した。旧 `Compose(AnchorData[], count, sampleRandom)` は `AnchorChainNode.FromAnchorData` で変換してから `ComposeNodes` を呼ぶだけの薄いラッパーになった(**既存の呼び出し・挙動は無変更**。`AnchorChainTests` はそのまま green)。トラック/埋め込み Anchor は `AnchorChainNode.FromDef`(ジッター無し・Delay=0・Chance=1)で同じ配列に混在させられる。あわせて、Parent 連鎖を呼び出し側所有の buffer へ集める `CollectChainInto`(`public` 化。既存の `private CollectChain` はこれの薄いラッパー)を追加した
- **`PresentationTrackAnchorComposer.Compose(in track, data, registry, sampleRandom)`**(`VfxData`/`SeData` それぞれの overload + 共通の `AssetId<AnchorMarker>`/`AnchorDef` 版)が上記 3 ケースを判定して `AnchorSpawnSpec` を返す。ケース3は「アセット側の連鎖(または埋め込み 1 段)」+「トラック Anchor(全体のルート、配列の末尾)」を `AnchorChainNode[]` に詰めて `AnchorChain.ComposeNodes` に通すだけ(コピペしない)。定常経路(Fire)から呼ばれるため、固定長の静的バッファ(`AnchorChain.MaxDepth + 1` 段分)を使い回して 0 alloc を保つ
- **`PresentationManager.FireVfx`/`FireSe`**(`Runtime/Presentation/PresentationManager.cs`)は `AnchorSpawnSpec.FromDef(track.Anchor)` の直呼びをやめ、`PresentationTrackAnchorComposer.Compose(in track, data, _registry, sampleRandom: true)` を呼ぶだけに変わった。`SeekInitialTracks`(ネット越しの遅延復元)や `ResolveContextRoot` 自体は Anchor を解決しないため無改修(調査済み。位置を決めるのは `FireVfx`/`FireSe` の 2 箇所だけだった)。ネットワークのメッセージ形式(`PresentationPlayMsg` 等)は変更していない(PresId + ctx を配って各自ローカルで合成する既存方針のまま)
- **AnchorGroup トラックは対象外**(配置セットは自分の点を持つため、[22_anchor_group.md] の既存の解決方法のまま)

### Editor(SceneView 表示・トラック一覧)

- **`PresentationTrackAnchorResolver.ResolveEffective`**(新規、`Editor/Presentation/PresentationTrackAnchorResolver.cs`)が `PresentationTrackAnchorComposer` の判定・合成をそのまま使い、SceneView 描画用の `BaseTransform`/`ExtraOffset`/`ComposedDef`/`Case` を返す。**基準(原点)の解決先は、ケース1(アセットのみ)はアセット連鎖の最上段の `Space`/`Path`、それ以外(ケース2/3/未設定)はトラック自身の `Space`/`Path`**(`PresentationTrackAnchorComposer.ResolveAssetRootDef` が連鎖の最上段を取り出す)。既存の `Resolve`(track.Anchor のみを見る、ケース2/未設定でのみ正しい)はそのまま残した(既存呼び出し元・テストへの影響を避けるため)
- **ケース3の SceneView 表示**(`PresentationEditorWindow.SceneAnchors.cs` の `DrawBothCase`)は「基準 → トラック Anchor(親、編集可能なハンドル) → アセット側の各段(表示のみ) → 最終位置」を `AnchorSceneHandles.DrawOrigin`/`DrawOffsetLink`/`Draw`/新設の `DrawChainNode` で描く。**ハンドルで編集できるのはトラック Anchor(親)だけ**(`Draw(track.Anchor, ...)` の結果を `ApplyTrackAnchorHandleResult` でトラック自身に書き戻す。従来と同じ書き戻し先で、アセット側は一切書き換えない)。アセット側の各段は `AnchorSceneHandles.DrawChainNode`(`DrawChain` の内部ループを汎用化して抽出した新規 public メソッド。`AnchorData` を持たない仮想ノードからも呼べる。**既存の `DrawChain`/`AnchorEditorWindow`/`VfxEditorWindow` の見た目は無変更**)で表示のみ描く
- **ケース1の SceneView 表示**(`DrawAssetOnlyCase`)は最終位置を `AnchorSceneHandles.DrawTargetMarker` で表示するだけ(ハンドルを出さない。ハンドルを出すと「トラックの Anchor を設定する」操作になってしまい、意図せずケース3へ切り替わってしまうため)。かわりに「トラックの Anchor を設定すると親として上書きできます(編集は VFX Editor / Anchor Editor で)」の注記ラベルを添える
- **非選択トラックの点(`DrawSelectableEffectiveMarker`)・描画権を持たないウィンドウの薄い目印(`DrawInactiveSceneAnchors`)も `effective.ComposedDef` を使うよう変更**(以前は `track.Anchor` を直接使っていたため、ケース1では常に間違った位置〔World 原点〕を指していた)
- **トラック一覧の見出し**(`PresentationEditorWindow.Tracks.cs`)の「Anchor(VFX/SE の位置)」フォールドアウトのタイトルに、現在どのケースか(`DescribeAnchorCase`)を 1 行追記するようにした(Anchor/Asset いずれかを編集するたびに `RefreshAnchorCaseLabel` で更新)
- **Validator**(`PresentationDataValidator`)にケース3を Info で知らせる検査を追加した(「トラックとアセット側の両方に Anchor が設定されているため、親子合成されます」。Error にはしない)

### 既存 Data への影響

これまで「トラック Anchor 既定値 + アセット側に `AnchorId`/埋め込み Anchor」だった既存の Presentation は、**今回から意図どおりアセット側の Anchor が使われるようになり、出る位置が変わる**(以前は常に World 原点扱いだったものが、アセット側の設定どおりの位置に変わる)。逆に「トラック Anchor だけ設定・アセット側は既定値」の既存 Data は挙動不変(ケース2、従来どおり)。プロジェクト内の既存 `PresentationData` を `Validation > Run All` で確認し、Info「親子合成されます」が出るものは意図どおりか確認すること(ケース1〔アセット側のみ〕への遷移は Info を出していないため、位置が変わった既存データがあれば別途目視確認が必要)。

### 変更ファイル

| 層 | ファイル |
|---|---|
| Foundation | `Foundation/Data/AnchorDef.cs`(`IEquatable<AnchorDef>`、`IsDefault`/`Equals`/`GetHashCode`/`==`/`!=` 追加。フィールド追加・型変更なし) |
| Runtime | `Runtime/Anchoring/AnchorChain.cs`(`AnchorChainNode` 新規、`ComposeNodes`/`CollectChainInto` 追加、既存 `Compose`/`CollectChain` は薄いラッパー化)、`Runtime/Presentation/PresentationTrackAnchorComposer.cs`(新規)、`Runtime/Presentation/PresentationManager.cs`(`FireVfx`/`FireSe` が Composer 経由に)、`Runtime/Presentation/PresentationDataValidator.cs`(ケース3 Info、`TryFindTrackAsset` 追加) |
| Editor | `Editor/Preview/AnchorSceneHandles.cs`(`DrawChainNode` 新設。`DrawChain` はこれを呼ぶだけに整理、見た目は無変更)、`Editor/Presentation/PresentationTrackAnchorResolver.cs`(`ResolveEffective`/`EffectiveResult` 追加)、`Editor/Presentation/PresentationEditorWindow.SceneAnchors.cs`(ケース別描画に分岐)、`Editor/Presentation/PresentationEditorWindow.Tracks.cs`(Anchor 欄見出しにケース表示) |
| Tests | `Tests/Editor/AnchorDefTests.cs`(新規)、`Tests/Runtime/PresentationTrackAnchorComposerTests.cs`(新規)、`Tests/Runtime/PresentationManagerTests.cs`(FireVfx の 3 ケース + AnchorId 連鎖の統合テスト追加)、`Tests/Editor/PresentationTrackAnchorResolverTests.cs`(`ResolveEffective` の 3 ケース追加)、`Tests/Runtime/PresentationDataValidatorTests.cs`(ケース3 Info の追加) |
| docs | 本節、[21_anchor_spec.md] §3.3、[43_manual_verification_2026-09-17.md] §6、`docs/DesignerManual/presentation.html` |

### コンパイル・テスト(2026-09-19、Unity MCP CoplayDev 版)

コンパイル(エラー 0)・EditMode(919 件中 919 件完走、失敗 4 件はいずれも本チケットと無関係。同時並行で別エージェントが作業していた `AssetCreationService`(バージョンスタンプ関連、`Assets/DDrive/Editor/AssetBrowser/AssetCreationService.cs`)の作業中の変更によるもので、本チケットのファイルは一切含まれない)・PlayMode(751/751 green)を確認した。本チケット関連のテストのみを抽出して実行しても全件 green(`AnchorDefTests` 6 件・`PresentationTrackAnchorResolverTests` 27 件の Editor 33 件、`PresentationTrackAnchorComposerTests`・`PresentationManagerTests`・`AnchorChainTests`・`PresentationDataValidatorTests` の Runtime 58 件)。

### 未確認・要判断

- SceneView での実際の見た目(ケース1の注記表示・ケース3のチェーン表示・トラック一覧の見出し文言)は人による確認が必要([43_manual_verification_2026-09-17.md] §11 に手順を追記した)
- 既存プロジェクトの `PresentationData` のうち、今回の仕様変更で実際に出る位置が変わるものが無いか(`Validation > Run All` の Info)は未確認

## 実装メモ(2026-09-20、ユーザーの確認作業〔[43] §8/§11〕で出た指摘 4 件)

### 指摘1: SceneView の点をクリックしても選択・ハンドルが出ない

原因は 2 つ: (a) `SceneGuiOwner` の描画権を持たないウィンドウの薄い目印(`DrawInactiveSceneAnchors`)にはそもそもクリック判定(`Handles.Button`)が無かった、(b) 描画権を持つウィンドウの非選択トラックの点(`DrawSelectableEffectiveMarker`)も当たり判定(`pickSize`)が `handleSize*0.18` 相当と小さすぎた。

- **`AnchorSceneHandles.DrawClickableMarker(anchor, baseTransform, extraOffset, label, color, active)`**(新規、`Editor/Preview/AnchorSceneHandles.cs`)に共通化した。`active=true`(描画権あり・非選択)/`active=false`(描画権なし・薄い目印)の両方をこの 1 つの API でカバーする。当たり判定は可視の円(`DrawTargetMarker` と同じ `handleSize*0.25`)に合わせて広げた。**コピペしない方針どおり、`PresentationEditorWindow.SceneAnchors.cs`(トラック選択)・`VfxEditorWindow.Anchor.cs`(2 か所)・`AnchorEditorWindow.cs` の計 4 か所がこの 1 つの API を使う**
- クリックされたら、呼び出し側が `SceneGuiOwner.Claim(this)` + `Focus()` を行ってから自分の「選択」処理をする。Presentation Editor の `SelectTrackFromScene(index)` にこの一連の処理(Claim → Focus → `_selectedTrack` 更新 → `RefreshTracksList()` → スクロール → `SceneView.RepaintAll()`)を集約した。VFX Editor / Anchor Editor は「選択」という概念が無い(対象は 1 つ)ため、Claim + Focus だけ行う
- **展開**: `RefreshTracksList()` は各トラックの `Foldout.value` を `index == _selectedTrack` から作り直すため、選択の変更だけで自動的に展開される(追加の作業は不要だった)
- **スクロール**: トラック一覧を包む `ScrollView`(`CreateGUI` の `scrollView`)への参照を新規フィールド `_mainScrollView` として保持し、`ScrollToSelectedTrackRow()` が `_tracksListContainer[index]`(`RefreshTracksList` が index 順に積む行)を `_mainScrollView.schedule.Execute(() => ScrollTo(row))` で次のフレームにスクロールする(直後は行の geometry が未確定なため)
- AnchorGroup の点(`DrawAnchorGroupPoints`)も同様に、`interactive` の値に関わらず常にクリック可能にし(`pickSize` も `handleSize*0.25` に統一)、非所有ウィンドウでもクリックで `SelectTrackFromScene` → オーナー切替まで面倒を見るようにした
- 回転ツール(E)で回転ハンドルに切り替わる挙動は既存の `AnchorSceneHandles.Draw`(`Tools.current == Tool.Rotate` の分岐)がそのまま担う(無改修)。確認手順は [43] §8 に追記した

### 指摘2: 「アセット側のみ」のケースでハンドルが出ない

**設計変更(ユーザー要望により、前回の「ケース1はハンドルを出さない」という決定を覆す)**: ケース1(アセット側のみ設定)でも最終位置に移動/回転ハンドルを出す。ドラッグしたら「合成後の最終位置がドラッグ後の位置に一致する」ようにトラックの Anchor(親)を逆算して設定し、ケース3(両方設定)へ遷移する。

**遷移時の初期化**: トラック Anchor の `Space`/`Path`/`FollowRotation`/`DetachOnStop` は、ドラッグ前の実効値(`effective.ComposedDef`。ケース1ではアセット側の連鎖のルートの値そのもの)をコピーする。コピーしないと、ケース3では基準(`effective.BaseTransform`)がトラック自身の `Space`/`Path` で決まるため、基準がワールド原点に飛んで位置がジャンプしてしまう。`LocalScale` は常に `Vector3.one` にする(ハンドルはスケールを編集しないため)。

**逆算の式(`PresentationTrackAnchorComposer.SolveTrackLocal`、新規、Runtime)**: `AnchorChain.ComposeNodes` は「ルート(トラック Anchor)の pos/rot/scale を初期値にして、各子ノード(アセット側の連鎖)を `pos += rot*Scale(scale,child.LocalOffset); rot *= Euler(child.LocalEuler)` の順に積む」ため、子側(アセット側の連鎖全体)を「1 つの合成済みノード」(= `PresentationTrackAnchorComposer.ComposeAssetOnly` の結果と同じ値。これは元のケース1の合成そのもの)とみなせば

```
desiredPos = trackPos + trackRot * childPos   (trackScale は 1 固定として無視)
desiredRot = trackRot * childRot
```

という単純な式になる(スケールは 1 固定のため無視できる)。これを逆に解くと

```
trackRot = desiredRot * Inverse(childRot)
trackPos = desiredPos - trackRot * childPos
```

`ComposeAssetOnly(assetAnchorId, embedded, registry, sampleRandom)`(新規)は「アセット側の連鎖(または埋め込み Anchor)だけを合成した値」を返す純関数で、旧 `Compose` の `Case.AssetOnly` 分岐をそのまま切り出した(コピペしない。`Compose` はこれを呼ぶだけになった)。ケース3のハンドル逆算では `DetermineCase` の結果(= `Both`)に関係なく「アセット側だけ」の合成値が要るため、`Case` 判定を経由しないこの関数を使う。往復(親を逆算 → `AnchorChain.ComposeNodes` で合成 → 元の位置/回転に一致すること)は EditMode テスト(`Tests/Runtime/PresentationTrackAnchorComposerTests.cs` の `SolveTrackLocal_*` 2 件)で固定した。

**掴む点は常に「最終位置」にした**: ケース1(`DrawAssetOnlyCase`)はもともと最終位置(`effective.ComposedDef`)にハンドルを出す。ケース3(`DrawBothCase`)は**以前はトラック Anchor 自身の位置にハンドルを出し、直接 `track.Anchor.LocalOffset/LocalEuler` に書き戻していた**が、これを「最終位置(アセット側の各段を合成し終えた末尾の段、`ResolveStages` の最後の要素で `effective.ComposedDef` と同じ値)」にハンドルを出す形に変更し、ドラッグ結果は `SolveTrackLocal` で同じ逆算をしてからトラック Anchor に書き戻すようにした。理由: ユーザーが SceneView で実際に見ている・操作したいのは常に VFX/SE が出る最終位置であり、ケース1↔ケース3で「どの点を掴むか」が変わると操作の一貫性が失われる。トラック Anchor 自身の位置は(ケース3で)`AnchorSceneHandles.DrawTargetMarker` による表示のみに変えた

編集は毎回 `Undo.RecordObject(_target)` + `EditorUtility.SetDirty(_target)` + `_serializedTarget.Update()` を行う。ケース1→3 の遷移時は `RefreshTracksList()` も呼び、トラック一覧の Anchor 欄見出し(`DescribeAnchorCase`)がその場で「アセット側の Anchor を使用」→「両方設定されているため親子合成」に切り替わるようにした。

### 指摘3: トラックごとに専用エディタを同時に開く

各トラック(`PresentationTrackKindMapping.AssetTypeFor` が返す型に `DataEditorRegistry` の登録がある種別)の行に「専用エディタで開く(一緒に調整)」「単体で確認用シーンに開き直す」の 2 つのボタンを追加した(`PresentationEditorWindow.TrackEditors.cs`、新規)。

| Kind | 「一緒に調整」の経路 | 備考 |
|---|---|---|
| Vfx | `VfxEditorWindow.Open(VfxData, GameObject attachTarget)`(新規 overload) | attachTarget = Presentation が配置した Self |
| AnchorGroup | `AnchorGroupEditorWindow.Open(AnchorGroupData, GameObject attachTarget)`(新規 overload) | 同上。指摘4 もこの経路 |
| Anim | `AnimEditorWindow.Open(AnimData, GameObject attachTarget)`(新規 overload、内部で `SetSceneTarget(attachTarget の Animator)`) | Self に Animator が無ければ何も対象にしない(通常の Open と同じ状態のまま) |
| Anim2D | 通常の `Open(data)`(`DataEditorRegistry.OpenDefault`) | `Anim2DData : AnimData` だが 3D の Animator を対象にする概念が無いため attach 未対応(仕様上「一緒に調整」ボタンを押しても効果は「単体」と同じ) |
| Se/Bgm | 通常の `Open(data)` | `AudioEditorWindow` に 3D 試聴の基準となるシーン Transform の概念が無い(2D のリスナーパッドのみ)ため、attach 付き overload は追加しなかった(要判断として残す) |
| CameraShake/Haptic/Canvas/UiTween/Timeline | 通常の `Open(data)` | attach(スポーン先)の概念自体が無い種別。ボタン自体は出るが 2 モードとも同じ動作になる |
| HitStop/Marker/Signal | 行自体を出さない | `AssetTypeFor` が null(Asset を持たない Kind) |

- **「一緒に調整」**: `attachTarget = ScenePresentationPreviewDriver.SelfRoot?.gameObject`(統合プレビューが `SpawnModel` で配置したモデル、または借用中の Animator)。専用エディタ側の `Open(data, attachTarget)` overload は**確認用シーンを開き直さない・プレビューを止めない**(既存の `SetTarget`/`SetSceneTarget` だけを呼ぶ薄いラッパー)。DataEditorRegistry の既定 `Open(data)` は変更していない(reflection ベースの `FindOpenMethod` は 1 引数のメソッドしか拾わないため、2 引数の overload を追加しても既存の「エディターで開く」ボタン(`DataEditorHeader`)には影響しない)
- **「単体で確認用シーンに開き直す」**: `DataEditorRegistry.OpenDefault(asset)`(通常の `Open(data)`)をそのまま呼ぶ。Presentation のプレビューには一切触れない(未保存の警告は各専用エディタの既存の仕組み〔シーン切替時の `SaveCurrentModifiedScenesIfUserWantsTo` 等〕に任せる)
- **`PresentationTrackEditorRouting.Classify(AssetDataBase)`(新規、Editor/Presentation、静的・純粋関数)**: 参照先の具象型からどちらの経路を使うかを判定する。ウィンドウを起動せずに検証できるようにするため、実際のディスパッチ(`OpenTrackEditorTogether`)から分離した(`Tests/Editor/PresentationTrackEditorRoutingTests.cs` で 6 パターンを検証。`Anim2DData : AnimData` のため `AnimData` より先に判定する必要がある点も固定した)

### 指摘4: AnchorGroup トラックから Anchor Group Editor を同時編集

指摘3の「一緒に調整」経路がそのまま AnchorGroup トラックにも適用される(`AnchorGroupEditorWindow.Open(AnchorGroupData, GameObject)`)。Anchor Group Editor の SceneView 描画(`OnSceneGui` → `RefreshPoints` → `AnchorGroupPlanner.EnumeratePoints`)は毎再描画でアセットの現在値を読み直す実装のため、Anchor Group Editor で点を動かすと Presentation Editor 側の SceneView 表示(`DrawAnchorGroupPoints`。これも毎再描画で `AnchorGroupPlanner.EnumeratePoints` を呼ぶ)に**追加の配線なしで即座に反映される**(共有アセットを 2 つのウィンドウがそれぞれ独立に読んでいるだけなので、片方の編集がもう片方の次の描画に自然に反映される)。確認手順は [43] §12 に追記した。

### 変更ファイル

| 層 | ファイル |
|---|---|
| Runtime | `Runtime/Presentation/PresentationTrackAnchorComposer.cs`(`ComposeAssetOnly`/`SolveTrackLocal` 追加、`Compose` の `Case.AssetOnly` 分岐を `ComposeAssetOnly` 呼び出しに整理) |
| Editor | `Editor/Preview/AnchorSceneHandles.cs`(`DrawClickableMarker` 新設)、`Editor/Presentation/PresentationEditorWindow.SceneAnchors.cs`(クリック選択・ケース1/3のハンドル・逆算呼び出し)、`Editor/Presentation/PresentationEditorWindow.cs`(`_mainScrollView` 追加)、`Editor/Vfx/VfxEditorWindow.cs`(`Open(VfxData, GameObject)` 追加)、`Editor/Vfx/VfxEditorWindow.Anchor.cs`(薄い目印をクリック可能に)、`Editor/Anchor/AnchorGroupEditorWindow.cs`(`Open(AnchorGroupData, GameObject)` 追加)、`Editor/Anchor/AnchorEditorWindow.cs`(薄い目印をクリック可能に)、`Editor/Anim/AnimEditorWindow.cs`(`Open(AnimData, GameObject)` 追加)、`Editor/Presentation/PresentationTrackEditorRouting.cs`(新規)、`Editor/Presentation/PresentationEditorWindow.TrackEditors.cs`(新規)、`Editor/Presentation/PresentationEditorWindow.Tracks.cs`(行に導線を追加) |
| Tests | `Tests/Runtime/PresentationTrackAnchorComposerTests.cs`(`SolveTrackLocal`/`ComposeAssetOnly` のテスト追加)、`Tests/Editor/PresentationTrackEditorRoutingTests.cs`(新規) |
| docs | 本節、[09_editor_tools.md] §2.3、[43_manual_verification_2026-09-17.md] §8/§11/§12、`docs/DesignerManual/presentation.html` |

### 未確認・要判断

- SceneView での実際のドラッグ操作感(ケース1→3 の遷移時にジャンプしないか、ケース3で最終位置を掴んだときの逆算が違和感なく追従するか)は人による確認が必要([43] §8/§11/§12)
- Se/Bgm(AudioEditorWindow)への attach 付き overload は追加していない(3D 試聴の基準となるシーン Transform の概念が無いため)。将来 AudioEditorWindow に基準 Transform を持たせる改修が入ったら追加を検討する
- 「単体で確認用シーンに開き直す」を押したときに Presentation 側のプレビューを明示的に止める処理は入れていない(専用エディタ側がシーンを開き直す過程で `PreviewPlacement.PrepareScene` 等が既存の未保存確認ダイアログを出す想定。Presentation の `ScenePresentationPreviewDriver` 自体は `OnStageChanged`/`OnPrefabStageChanged` で自動的に片付く既存の仕組みに任せている)
