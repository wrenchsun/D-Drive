# 02. 基盤フレームワーク詳細設計

関連: [01_architecture.md](01_architecture.md) / 各アセット設計書 (03〜08)

コード例は設計意図を示すための骨格。実装時はプロジェクト規約に合わせる。

---

## 1. AssetId

```csharp
// 種別ごとに強い型。中身は安定した ulong（登録時に GUID から生成）
public readonly struct SeId  { public readonly ulong Value; }
public readonly struct BgmId { public readonly ulong Value; }
public readonly struct VfxId { public readonly ulong Value; }
// ... AnimId, MaterialId, TextureId, CanvasId, PrefabId, PresentationId

// 自動生成される定数クラス（Generated/AssetIds.g.cs, 手編集禁止）
public static class SEID
{
    public static readonly SeId PlayerSlash = new(0x8F3A_...);
    public static readonly SeId Footstep    = new(0x1C2B_...);
}
```

- **生成**: AssetBrowser の「ID 定数を再生成」ボタン、または保存フックで自動
- **Inspector 参照**: `[SerializeField] SeIdRef attackSe;` — ドロップダウン + 検索で選択できる PropertyDrawer 付きラッパー（生値を直書きさせない）
- **未登録 ID**: Resolve 失敗時は `Placeholder<T>.Data` を返し警告ログ（1 ID につき 1 回だけ）

## 2. AssetDataBase（全種別共通基底）

```csharp
public abstract class AssetDataBase : ScriptableObject
{
    [Header("Identity")]
    public ulong Id;                  // 生成時に GUID から確定。以後不変
    public string DisplayName;
    [TextArea] public string Description;
    public string Category;           // ブラウザのフォルダ分け
    public string[] Tags;             // 検索用 (Enemy, Boss, UI, Fire...)
    public Texture2D Icon;

    [Header("Meta")]
    public int Version;               // 保存フックで自動 +1
    public string Author;             // 保存フックで自動記録
    public string UpdatedAt;          // 同上
    [TextArea] public string ChangeNote;

    [Header("Common")]
    public AssetFlags Flags;
    public AssetEvent[] Events;

    // [42_distribution.md] §4.3(P-7、2026-09-20) — Version(保存回数)とは別の「スキーマ版」。
    // VersionStampProcessor が保存の都度 DDriveSchema.Current を書く。既存 .asset は 0 = 1.0.0 以前の形式。
    [HideInInspector] public int SchemaVersion;

    // 特殊制御の差し込み口（FR-2.2）
    // 派生 Data がオーバーライドすると Manager 改修なしで挙動を変えられる
    public virtual IAssetBehaviour CreateBehaviour() => null;
}
```

### AssetFlags

```csharp
[Serializable]
public struct AssetFlags
{
    public PauseMode Pause;        // PauseWithGame / IgnorePause / UIOnly
    public LoadMode Load;          // Preload / LazyLoad / Streaming
    public PoolPolicy Pool;        // None / Pooled(初期数, 上限)
    public int Priority;           // 同時再生上限を超えた時の優先度
    public bool Persistent;        // シーンを跨いで破棄しない
    public AssetDomain Domain;     // Game3D / UI / Both
    public NetMode Net;            // Local / Cosmetic / Simulated ([14] 参照)
}
```

## 2.5 ValueDef / TimeDef（調整値の統一表現）

デザイナーが調整する「値」を全種別で 1 つの型に統一する（詳細: [17_value_definition.md](17_value_definition.md)）。

- `ValueDef`: **Constant（定数）/ Parametric（Ease 31 種 + CubicBezier）/ Curve（AnimationCurve）** の 3 モード + 出力レンジ + `TimeDef` + Loop を持つ struct。`Evaluate(t)` で評価（0 alloc・純関数）
- `TimeDef`: **Duration / Speed / Rate** の 3 モード + SpeedScale + IgnoreTimeScale。「形（カーブ）」と「速さ」を分離する
- 派生: `ValueDef3`（ベクトル・Uniform 指定可）/ `ValueDefColor`（Constant / Gradient + Alpha 用 ValueDef）
- 編集 UI は共通 PropertyDrawer 1 本（[17] §5）に集約し、種別ごとにカーブエディタを作らない（NFR-8）
- 調整パラメータは必ず ValueDef で定義する（[00] §5 禁止事項）

## 3. AssetEvent

```csharp
[Serializable]
public struct AssetEvent
{
    public EventTrigger Trigger;   // OnSpawn/OnEnable/OnLoop/OnDisable/OnDestroy
    public float Time;             // Trigger=Time のとき秒、Frame のときフレーム
    public string CustomKey;       // Trigger=Custom のときのキー
    public EventAction Action;     // PlayAsset / SetParam / SendMessage / Duck
    public AssetRef Target;        // 任意種別の ID を保持 { AssetType, ulong }
    public ParamValue Param;       // 汎用パラメータ (float/color/curve/string)。時間変化する値は ValueDef（[17]）
    public EventRepeat Repeat;     // 2026-09-10 追加: EveryLoop(毎周回、既定) / Once(再生ごとに 1 回) / KeepWhilePlaying(1 回出して終了・中断で止める)
}
```

- 発火は `EventBus.Fire(instance, trigger)`。Manager は節目で呼ぶだけ
- Frame/Time は `EventBus.Tick(ctx, dt)`（ゲームフレーム/秒）か `EventBus.TickAnimation(ctx, clipTime, frameRate)`（クリップ時間。AnimManager 用、2026-09-08 追加）で判定。`ResetOnce(ctx)` でループ周回ごとに再発火できる（`Repeat=EveryLoop` のものだけ。Once / KeepWhilePlaying は発火済みのまま）。`End(ctx)` は `OnSessionEnded` を出し、Dispatcher が KeepWhilePlaying で出した SE / VFX / AnchorGroup を `Stop` する（ループする追従エフェクトの後始末。2026-09-10）。**Repeat は `Fire()`（OnLoop / Custom 等）と `SeekAnimation` でも効く**（Once / KeepWhilePlaying は Fire 経由でも再生ごとに 1 回、シークで戻しても発火済みを保持。2026-09-10 レビュー対応）。Frame 判定は `clipTime*frameRate + FrameEpsilon(0.001) >= Time` で float 誤差を吸収（63 フレーム @30fps 等の最終フレームを落とさない。`AnimDataValidator` の上限 +0.001 と整合）
- `PlayAsset` の実行は AssetType に応じて対応 Manager にディスパッチ（実装: `Runtime/Presentation/AssetEventDispatcher.cs`。Se / Vfx / AnchorGroup に対応。発火元の Transform を contextRoot にする。2026-09-09）
- Validation 対象（Target 欠落 = 赤）

## 4. AssetRegistry（ID→Data 解決）

```csharp
public interface IAssetRegistry
{
    UniTask RegisterCatalogAsync(AssetCatalog catalog); // 起動/シーン単位
    UniTask<T> ResolveAsync<T>(ulong id) where T : AssetDataBase;
    bool TryResolveSync<T>(ulong id, out T data);       // ロード済のみ
    IReadOnlyList<CatalogEntry> Entries(AssetType type); // Editor/Browser用
}
```

- 内部は `Dictionary<ulong, CatalogEntry>`。Entry は address のみ保持し Data 本体は Lazy ロード（Flags.Load = Preload のものはカタログ登録時に一括ロード）
- **`ResolveOrPlaceholder<T>` は同期解決専用**（`TryResolveSync` と同じく `_loaded` キャッシュしか見ない）: Play/Spawn を同期 API にしている Manager（Audio/Vfx/Anim/Presentation 等、ほぼ全種別）は、対象 Data が `Flags.Load = Preload` でカタログ登録時に一括ロードされているか、事前に誰かが `ResolveAsync` を呼んでいない限り、**初回参照時は必ず Placeholder になる**（LazyLoad は「遅延ロードされる」のではなく「明示的に ResolveAsync しない限りロードされない」という意味に近い）。`AssetCreationService.Create` は同期解決でしか使われない種別（Canvas/ControlSkin/Presentation、2026-09-12・2026-09-14 順に対応）の既定を Preload にしてこれを避けている。新しい種別を追加する場合、その Manager が同期 API のみなら同様に Preload をデフォルトにするか、`ScenePreload`（5-7、§14 参照）等で事前ロードする運用にすること
- 解決失敗 → `PlaceholderProvider.Get<T>()` + 警告（モック動作保証）

### レビュー対応（2026-09-14、P5 レビュー第 1 弾）

- **P2: `PreloadIdsAsync`(5-7)と `ResolveOrPlaceholder`/`ResolveAsync`(Placeholder 経路)の
  未登録 ID 警告が 1 つの `HashSet<ulong>` を共有していた（review1_runtime.md #4）**:
  `IAssetRegistry.PreloadIdsAsync` のコメントには「未登録 ID は警告(1 ID につき 1 回、
  `OnPlaceholderUsed` とは独立)」と書かれていたが、実装は `_warnedIds` を両経路で共有していたため、
  どちらかの経路で先に警告した ID はもう一方の経路で二度と警告されなかった。`AssetRegistry` の
  `_warnedIds` を `_preloadWarnedIds`/`_placeholderWarnedIds` の 2 つに分離し、コメントどおり
  独立に「1 ID につき 1 回」警告するようにした。テスト:
  `AssetRegistryTests.PreloadIdsAsync_And_ResolveOrPlaceholder_WarnIndependently_ForSameUnregisteredId`。

## 5. AssetLoader（Addressables ラッパ）

```csharp
public interface IAssetLoader
{
    UniTask<T> LoadAsync<T>(string address, CancellationToken ct);
    void Release(string address);                 // 参照カウント式
    UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> p);
}
```

- 参照カウントで多重ロード防止・自動 Release
- ゲームコードから直接呼ぶの禁止（Manager 専用）
- **カタログと Addressables の一致**（2026-09-09）: 実装 `AddressablesAssetLoader` は Addressables の address しか引かない。そのため「カタログにある Data は Addressables に同じ address で登録済み」を、①作成時（`AssetCreationService` → `AddressablesSync.EnsureEntry`、グループ `DDrive_GameData`）②`Generate/Addressables 登録を同期` ③Validation（`AddressablesRegistrationValidator` が未登録 / Address 不一致を Error、FixAction で登録）の 3 箇所で保証する。カタログ自体もグループ `DDrive_Catalogs` にラベル `DDriveCatalog` で登録され、起動オブジェクトがラベルから集められる
- **実装メモ（2026-09-14、5-7: Preload リスト自動集計 + シーンロード統合）**: `IAssetRegistry` に `PreloadIdsAsync(IReadOnlyList<ulong> ids, IProgress<float> progress)` / `ReleaseIds(IReadOnlyList<ulong> ids)` を追加した。ID を（既存の）`_index` で Address に解決し、`IAssetLoader.PreloadAsync`（参照カウント式。既存のまま変更なし）にまとめて渡すだけの薄い実装。未登録 ID は例外にせず 1 ID につき 1 回警告してスキップする（`ResolveAsync` 系と警告の重複排除セットを共有）。呼び出し元は Editor が依存グラフ（§12）から自動集計する `ScenePreloadList`（詳細: [09_editor_tools.md](09_editor_tools.md) §10 の 5-7 節）。`PreloadIdsAsync` で確保した参照は、対応する `ReleaseIds` を呼ぶまで解放されない点に注意（シーンアンロード時等に呼び忘れるとリークする。要判断は [09] §10 参照）

## 6. PoolService

```csharp
public interface IPoolService
{
    PooledObject Rent(GameObject prefab);   // なければ Instantiate
    void Return(PooledObject obj);
    void Discard(PooledObject obj);         // 2026-09-10 追加。下記参照
    void Prewarm(GameObject prefab, int count);
    void Clear(PoolScope scope);            // シーン遷移時
}
```

- 対象: VFX / AudioSource / Prefab / Canvas / Projectile（AssetFlags.Pool で指定）
- Return 時に `IPoolable.OnReturn()` を呼びリセット（Trail/Particle の Clear 等）
- 上限超過時は Priority 最低の稼働 Instance を強制回収（シーン破棄等で GameObject が死んだ Active エントリは先に台帳から外し、回収しても Free に積めなければ新規 Instantiate に落とす。2026-09-10）
- **`Kind == None` の意味（2026-09-10、Codex レビュー対応）**: `AssetFlags.Pool.Kind` の既定値 `None` は「プールしない」ことを表す。ModelsManager / PrefabsManager はこれを尊重し、`Despawn` 時に `Kind == Pooled` なら `Return`（Free に積んで再利用）、`Kind == None` なら新設の `IPoolService.Discard(PooledObject)` で Active から取り除いた上で即座に破棄する（Play モードは `Object.Destroy`、Edit モードは `DestroyImmediate`。`IPoolable.OnReturn` は「プールに戻って再利用される」通知であり Discard では呼ばない）。Instance の生成自体は `Kind` に関わらず `Rent` 経由に統一し、親付け(`SetInstanceParent`)や上限管理の一貫性を保つ。**VFX（VfxManager）/ SE（AudioManager）はこの区別の対象外で、常にプールする**（短命・高頻度再生のため、Kind の値に関わらず Return する設計を維持）

## 7. Handle と Instance

```csharp
public readonly struct VfxHandle   // 種別ごとに同型で定義
{
    readonly int _index;
    readonly int _generation;
    public bool IsValid { get; }          // 世代一致チェック
    // 操作は Manager の拡張メソッド経由:
    // handle.Move(pos) / handle.Attach(t) / handle.Stop() / handle.SetParam(...)
}

internal sealed class VfxInstance  // Manager 内部のみ。外部非公開
{
    public VfxData Data;
    public GameObject Go;
    public float Elapsed;
    public InstanceState State;    // Playing / Paused / Stopping
    public int Generation;
    public IAssetBehaviour Behaviour;   // Data.CreateBehaviour() の結果
}
```

- 無効 Handle への操作は no-op（開発ビルドのみ警告ログ）
- Instance 配列 + フリーリストで管理し、走査は for のみ（LINQ 禁止）

## 8. Manager 共通インタフェース

```csharp
public interface IAssetManager
{
    AssetType Type { get; }
    void Tick(float dt);                  // GameLoop から駆動 (Update不使用)
    void OnPause(PauseChannel ch, bool paused);
    void StopAll(StopReason reason);
    void OnSceneUnload();
}
```

- 具象 API は種別ごと（各設計書参照）だが、命名を統一する:
  `Play/Spawn(id, ctx) → Handle` / `Stop(handle)` / `Preload(ids)`
- static ファサード（`Vfx.Spawn(...)` 等）を用意し、内部で DI コンテナから実体を引く。テストでは実体を差し替え

## 9. IAssetBehaviour（特殊制御の拡張点）

```csharp
public interface IAssetBehaviour
{
    void OnSpawn(InstanceContext ctx);
    void OnTick(InstanceContext ctx, float dt);
    void OnDespawn(InstanceContext ctx);
}
```

- 例: 「BGM をビート同期でフェードする」→ `BeatSyncBgmData : BgmData` が `CreateBehaviour()` で `BeatSyncBehaviour` を返す。**Manager は無改修**
- デザイナーには「Data の種類を選ぶ」操作としてエディタに現れる

## 9.5 INetBridge / ITimeSource（ネットワーク前提の注入点、詳細: [14_networking.md](14_networking.md)）

- `INetBridge`: Broadcast/SendTo/Subscribe/NetworkTime/ResolveNetObject。シングルプレイは `LocalLoopbackBridge`
- `ITimeSource`: Foundation 内で `Time.time` を直接使わず、これ経由で取得（ネット時は NetworkTime 実装に差し替え）
- 乱数選択（SE のランダム Clip 等）は Seed 引数を受け取れる決定的 API にしておく

## 10. PauseService / TimeService

```csharp
PauseService.Push(PauseChannel.Gameplay);   // ポーズメニューを開いた
PauseService.Pop(PauseChannel.Gameplay);
TimeService.HitStop(0.08f);                 // Presentation から利用
AudioDuck.Push(DuckChannel.Dialogue, -12f); // 会話中 BGM を下げる
```

- チャンネルはスタック式（多重ポーズ・多重ダックに対応）
- 各 Instance は自分の `Flags.Pause` を見て応答を決める。各 Manager は `OnPause` と、エディタ向けの `SetPausedAll(bool)`（Flags を無視して全部止める）を `ApplyPause(paused, respectFlags)` の 1 実装に統合している（Audio / Vfx / Anim、2026-09-10）
- **無効 Handle の問い合わせと操作**: `InstanceStore.TryGet / IsValid` は無効 Handle で警告 + `InvalidAccessCount`（操作の誤りを検出する）。**`IsValidSilent` は警告なし**で、各 Manager の `IsPlaying` / `ModelsManager.IsValid` はこちらを使う（終了済み Handle を毎フレーム問い合わせるエディタのポーリングや Dispatcher の後始末は正常系）。`Remove` も冪等で警告なし。エディタは終了を検知したらローカルの Handle を `Invalid` に戻す（2026-09-10）

## 11. Validation Core

```csharp
public interface IValidator
{
    AssetType Target { get; }
    IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx);
}
// ValidationResult { Severity(Error/Warning/Info), Message, FixAction? }
```

- 種別ごとの Validator を登録制にする（新種別追加時は Validator を足すだけ）
- 実行タイミング: ①保存フック ②AssetBrowser の一括実行 ③CI バッチ
  `Unity -batchmode -executeMethod DDrive.Editor.CI.ValidateAll`（エラーで exit 1）
- 共通検査: ID 重複 / 参照欠落 / 循環参照 / Addressable 未登録（実装済み 2026-09-09: `Editor/Validation/AddressablesRegistrationValidator.cs`、カタログ未登録も Error）/ 未使用検出
- `FixAction` があるものは「自動修正」ボタンを出す（例: Addressable 登録漏れ→登録）

### 2026-09-18 追記（[29_network_device_test.md] §18 — 検出漏れの修正: `CatalogAddressCoverageValidator`）

実機確認で「カタログは `VFX_Player_Slash` を指しているのに Addressables 側にその address のエントリが無く、全カタログの登録が中断して全 ID が Placeholder になる」事故が発生した。原因は Data(.asset)を一旦削除→別アセットに置き換え→git で削除だけ discard して復元、という操作で、**Data(.asset)はファイルとして復元されても、別ファイルである Addressables のグループ登録(`Assets/AddressableAssetsData/AssetGroups/*.asset`)は git 操作に追従しない**ため。

- 既存の `AddressablesRegistrationValidator` は「渡された Data 自身が、カタログの Address と同じ address で Addressables に登録されているか」を Data 単位で見る。今回はたまたま Data ファイルが実在したため、この per-Data チェック(`Addressables 未登録` Error)で検出できていた
- ただし `ValidatorRegistry.RunAll` は「プロジェクト内に実在する Data アセット」を列挙して 1 件ずつ渡す作りのため、**Data(.asset)自体が存在しない場合は Validate() が一度も呼ばれず、上記の per-Data チェックは何も報告できない**という抜けがあった。実際、この抜けに該当する別の孤立カタログエントリ(`AnchorCatalog` の Address `ANC_Can_Vas`。対応する Data ファイルがプロジェクトに存在しない)が本件の調査中に見つかった
- この抜けを塞ぐため `Editor/Validation/CatalogAddressCoverageValidator.cs` を追加した。`ContentHashCatalogCoverageValidator` と同じ実装パターン(`IUniversalValidator` + `ValidationContext` ごとに 1 回だけプロジェクト全体を走査するガード)で、**カタログ起点**に「カタログの Address が Addressables のどのエントリにも存在しない」ことを検出する。対象の Data が `ValidationContext.AllAssets` に見つかれば `FixAction` で再登録できるが、Data 自体が見つからない場合は自動修正しない(存在しないものを生成しない)
- そのために `AddressablesSync.FindEntryByAddress(string address)`(address からエントリを逆引き)を追加した
- テスト: `Tests/Editor/CatalogAddressCoverageValidatorTests.cs`(`ContentHashCatalogCoverageValidatorTests` と同じ baseline 差分方式で、実 GameData の状態に依存しない)
- `DataValidationSection.ProjectWideValidatorNames` にも追加済み(個別検証には出さず、Run All/CI のみで実行する)

## 12. 依存関係グラフ

- Editor 時: 各 Data の `SerializedObject` を走査し `AssetRef` / オブジェクト参照を収集 → `DependencyGraph { ulong → ulong[] }` をキャッシュ（保存時に差分更新）
- 用途: AssetBrowser のツリー表示・使用箇所検索・未使用検出・循環検出
- Scene / Prefab 内の `*IdRef` フィールドも走査対象（使用箇所検索の要）

### 実装メモ（2026-09-14、5-5）

`Editor/Dependencies/DependencyGraphService`(+`Collector`/`Cache`/`Postprocessor`)として実装。詳細・API 一覧・キャッシュ形式は [09_editor_tools.md](09_editor_tools.md) §10 を参照。要点のみ:

- 「`*IdRef` フィールド」は実装上 `AssetId<TMarker>`(強い型。`SerializedProperty.type == "AssetId\`1"`)と `AssetRef`(弱い型。`AssetEvent.Target` 等。`SerializedProperty.type == "AssetRef"`)の 2 種類として現れる。どちらも `SerializedObject` の全走査で検出し、型ごとの専用パーサは書いていない
- `DependencyGraph { ulong → ulong[] }` という素朴な形ではなく、`(AssetType, ulong) → 参照元一覧(パス/オブジェクトパス/型名/プロパティパス)` の逆引きインデックスにした(使用箇所検索・未使用検出にそのまま使えるように、5-6 の要求形に合わせた)
- 循環検出は 5-5 の時点では未実装(5-6 の依存ツリー UI で深さ優先探索時に検出する想定。要判断)

## 13. テスト方針

| 対象 | 種別 | 例 |
|---|---|---|
| Registry/Loader | EditMode + PlayMode | 未登録 ID→Placeholder、参照カウント |
| Pool | PlayMode | Rent/Return、上限強制回収、Prewarm |
| Handle | PlayMode | 破棄後アクセス no-op、世代更新 |
| EventBus | EditMode | Frame/Time トリガの発火タイミング |
| Validation | EditMode | 各 Validator の検出・FixAction |
| ID 生成 | EditMode | 再生成の冪等性、重複検出 |
| ValueDef | EditMode | 3 モードの参照値、Loop/PingPong、0 alloc、純関数性（同入力同出力） |

## 14. 起動配線（Composition Root）— 2026-09-09

**ランタイムの組み立ては `Runtime/Loop/DDriveRuntimeBootstrap.cs` の 1 箇所だけで行う。** テスト・Editor プレビュー（`PreviewService` / `Scene*PreviewDriver`）以外で Manager を new しない。

- 配置: `Tools > D-Drive > Generate > 起動オブジェクト(DDriveRuntimeBootstrap)をシーンに配置`（`[D-Drive] Runtime` を作り、`GameData/Catalogs` の全カタログを直参照で割り当てる。Inspector の「カタログを再収集」で更新）。シーンに 1 つ。2 つ目は警告して自壊
- Awake（`DefaultExecutionOrder(-1000)`）: `AssetRegistry(AddressablesAssetLoader)` → `PoolService` → `AudioManager` / `BgmManager` / `VfxManager` / `AnimManager` / `ModelsManager(anim)` / `PrefabsManager`（4-4、2026-09-10 追加）/ `AnchorGroupPlayer` → `AssetEventDispatcher(Anim.Events → SE/VFX/配置セット)` + `AssetEventDispatcher(Prefabs.Events → SE/VFX/配置セット、Anim 用とは別インスタンス)` を生成し、同居する `GameLoopDriver.GameLoop` に登録、静的ファサード（`Audio` / `Vfx` / `Anim` / `Models` / `Prefabs` / `Anchors`）を Bind。`INetBridge` は `LocalLoopbackBridge`（NGO 統合時にここを差し替える）
- Start: `Catalogs`（直参照）と Addressables ラベル `DDriveCatalog` のカタログを `Registry.RegisterCatalogAsync` → `IsReady` / `OnReady` / `WhenReady`。IsReady 前の Play は未登録 ID として Placeholder になる（例外にしない）
- 破棄: `GameLoop.StopAll(SceneUnload)` → Dispatcher 破棄 → ファサード Unbind → GameLoop から解除 → Pool Clear。`KeepAcrossScenes`（既定 ON）でシーンをまたいで生きる
- テスト: `Tests/Runtime/RuntimeBootstrapTests.cs`（組み立て・Bind・Unbind・カタログ登録・多重配置の拒否）
- **2026-09-11 追記(4-9)**: `UiTweenManager` を `UiManager` より先に生成し、`new UiManager(Pool, Registry, Loop.PauseService, tweens: UiTweens)` で ElementFx の再生先として渡す。`[SerializeField] UiLayerSettings LayerSettings`(Inspector 直参照、未設定なら null のままでフォールバック無し)を追加し、`Ui.SetLayerSettings(LayerSettings)` で配る([15_ui_interaction.md] B-4 実装メモ参照)
- **2026-09-14 追記(5-13)**: `[SerializeField] TuningTable TuningTable`(`Runtime/Tuning/TuningTable.cs`。Inspector 直参照、`UiLayerSettings` と同じ扱いで Addressables には登録しない)を追加し、`Tuning.Bind(TuningTable)` で静的ファサード `Runtime.Tuning.Tuning` に配る。仕様書「調整値」タブの取り込み先([27_spec_sheet.md] §3.2/§8.4)。§2.5 の `ValueDef`(アセットのフィールドに埋め込むカーブ/イージング)とは別物で、`Tuning` はゲームコードから `TUNING.キー定数` で読む文字列キー→値のフラットな辞書
- **2026-09-14 追記(W-10、[32_spec_web.md] §5.3・§9-7 案A)**: `TuningTable` にテーブル型調整値(`Tables: TuningTableEntry[]`)と Enum 型(`TuningEntry.EnumOptions`)を追加した(既存フィールドの削除・型変更なし。旧 .asset はそのまま読める)。`Tuning` に `GetEnum(key, default)` / `GetTableFloat`・`GetTableInt`・`GetTableBool`・`GetTableString`(tableKey, rowId, columnKey, default)を追加。行/列の検索は Bind() 時の索引を使わず for ループの線形探索(テーブルの行・列数は数十件程度が前提。[12_review.md] §3 の LINQ・クロージャ・boxing 禁止を守るための実装)。未登録キー・型違いは既存の `GetFloat` 等と同じ「警告1回+既定値」方式。生成される定数は `TUNING_TABLE`(テーブルキー)・`TUNING_COLUMN`(テーブル名を頭に付けた列キー、`Editor/Codegen/TuningCodegen.cs`)
- **2026-09-14 追記(5-7)**: `BindFacades` ブロックで `Runtime.Loading.ScenePreload.Bind(Registry)` / `Teardown` で `Bind(null)` を追加(他の静的ファサードと同じ Bind/Unbind パターン)。`ScenePreload` は §5 で追加した `IAssetRegistry.PreloadIdsAsync`/`ReleaseIds` への薄い窓口で、シーンに置いた `SceneLoadingScreen`(`Runtime/Loading/SceneLoadingScreen.cs`、確認用の最小 UI)等がこれ経由で `ScenePreloadList`(Editor が自動生成する SO)を Preload する。`ScenePreloadList` は `Catalogs[]` のような Bootstrap 側の中央インデックスを持たない(シーン側から直参照する運用。要判断は [09_editor_tools.md] §10 の 5-7 節)
- **2026-09-14 追記(5-1)**: `Presentation = new PresentationManager(Registry, Loop.TimeService, Audio, Bgm, Vfx, Anim, Ui, UiTweens, CameraFx, Haptics)` を Groups 生成の直後・Dispatcher 生成の直前に追加し、`loop.Register(Presentation)` / `Runtime.Presentation.Presentation.Bind(Presentation)`(Teardown は逆順で Unregister/Unbind)。他の Manager と違い `IAssetRegistry` に加えて `Loop.TimeService` を直接渡す(HitStop トラック用。[08_presentation.md] 実装メモ参照)。CameraShake/Haptic 用の Manager は 5-2/5-2b で追加した(下記追記参照)。
- **2026-09-14 追記(5-8)**: 上記コンストラクタ呼び出しの末尾に `NetBridge` を追加し `new PresentationManager(Registry, Loop.TimeService, Audio, Bgm, Vfx, Anim, Ui, UiTweens, CameraFx, Haptics, NetBridge)` にした(Audio/Vfx/Prefabs と同じ「NetBridge を渡すだけ」の配線パターンに揃えた)。`NetBridge` は本行の変更後も常に `LocalLoopbackBridge`(180 行付近、NGO 統合は Phase 6)のままのため、この変更自体はランタイムの挙動を変えない。詳細は [14_networking.md] §5 実装メモ(5-8)。
- **2026-09-14 追記(5-2/5-2b)**: `CameraFx = new CameraFxManager(Registry)` / `Haptics = new HapticsManager(Registry)` を `UiTweens`/`Ui` 生成の直後に追加し、`OptionStore` の初期化子に `CameraFx = CameraFx, Haptics = Haptics` を渡してから `Options.Load(...)` を呼ぶ(`OptionKey.ShakeScale`/`HapticScale` が起動時の保存値から `SetGlobalScale` へ即座に反映されるようにするため)。`Haptics` は他の Manager と同じ `loop.Register(Haptics)`(GameLoop 共有の `TimeService.ScaledDeltaTime` で駆動)。**`CameraFx` だけは `loop.Register` しない** — 代わりに `UnscaledCameraFxAdapter`(`IAssetManager` を実装し `Tick(dt)` の引数を無視して `Time.unscaledDeltaTime` を渡すだけの薄いラッパー。`AnchorGroupLoopAdapter` と同じ「非標準の Tick 系列を持つものを IAssetManager に橋渡しする」パターン)を `loop.Register` する(CameraShake が HitStop 中も止まらないようにするため。[16_camera_haptics.md] 実装メモ参照)。静的ファサード `Runtime.CameraShake.CameraFx`(名前空間は 5-2 整理で `Runtime.Camera` → `Runtime.CameraShake` に改名。下記追記参照) / `Runtime.Haptics.Haptics` の Bind/Unbind、`OnApplicationQuit`/`OnApplicationFocus(false)` での `Haptics.ResetOutput()` 呼び出しも追加した。
- **2026-09-14 追記(5-2 整理)**: `Runtime/Camera/*.cs` の名前空間 `DDrive.Runtime.Camera` は `UnityEngine.Camera` と衝突する(`DDrive.Runtime` の直下に同名の入れ子名前空間ができるため、`DDrive.Runtime.*` 配下のコードで `Camera` と書くと非修飾名解決でこちらが優先され CS0118 になる)ため `DDrive.Runtime.CameraShake` に改名した(フォルダ名 `Runtime/Camera/` はそのまま。ScriptableObject 由来の参照は .meta の GUID で結ばれるため、名前空間変更でも `CameraShakeData` アセットは壊れない)。合わせて、この衝突を避けるために入っていた `Runtime/Anim2D/Anim2DFacing.cs` の `UnityEngine.Camera` 完全修飾と `CameraFxManager.cs` 内の `UnityEngine.Camera.main` 完全修飾は不要になったため `Camera`/`Camera.main` の非修飾に戻した。`AssetId<ShakeMarker>` 等の struct/enum フィールドはアセット YAML に型名を書き込まない(値はフィールド順で復元される)ため `[MovedFrom]` は不要と判断した。
- **2026-09-14 追記(6-0)**: `NetBridge = ResolveNetBridge()`(176 行付近)に変更。`ResolveNetBridge()` は `NetLaunchArgs.Parse(Environment.GetCommandLineArgs())` の結果(未指定なら Inspector の `DefaultNetBridge`)を見て `LocalLoopbackBridge` か、シーンの `NetworkManager`+`NgoNetBridge`(`NetworkManagerRef`/`NgoBridgeRef`。未設定なら `FindAnyObjectByType` で自動検索)を選ぶ。Ngo を要求されたのに見つからない場合は警告して Loopback にフォールバックする(例外で止めない)。Ngo のときは `NgoTransportConfigurator.TryConfigure` で IP/Port/シミュレータを設定してから `StartHost`/`StartClient` し、`ShowNetDebugOverlay`(既定 ON)なら `NetDebugOverlay` を追加する。**Bootstrap 自身は NetworkManager を生成しない**(シーンに置く前提。[14_networking.md] §12)。既定は `NetBridgeMode.Loopback` のため、6-0 適用前と挙動は変わらない。詳細は [14_networking.md] §12(6-0)。
- **2026-09-18 追記(6-10a)**: `Cutscene = new CutsceneManager(Registry, Models, NetBridge)` を `Groups` 生成の直後、**`Presentation` 生成より前**に追加する(`PresentationManager.TrackKind.Timeline` がこの参照を要求するため。`Presentation = new PresentationManager(..., NetBridge, cutscene: Cutscene)` の名前付き引数で渡す)。`loop.Register(Cutscene)`(`Presentation` の直前)、`CutsceneDispatcher = new AssetEventDispatcher(Cutscene.Events, Registry, Audio, Vfx, Cutscene.GetContextTransform, Groups)`(4 つ目の Dispatcher。Anim/Prefabs/Ui 用と同じパターン)、`Runtime.Cutscene.Cutscene.Bind(Cutscene)`(Teardown は逆順で Unbind/Unregister/Dispose)。切断時(`OnNetClientDisconnected`)に `Presentation?.CancelAllNetworked()` と並べて `Cutscene?.CancelAllNetworked()` も呼ぶ。詳細は [26_timeline.md] §4.5/§6・[11_tasks.md] 6-10a。
- **2026-09-19 追記(Edit Mode プレビュー)**: `CutsceneManager` のコンストラクタ・本節の Bootstrap 配線は**変更していない**。`RentDirector` が CutsceneRoot に新設 `CutsceneDirectorContext`(`FireEnabled=true` 固定・`ManagerRefs=null`)を付けるだけで、SE/VFX/UI/AnchorGroup/Presentation クリップは `ManagerRefs` が無ければ既存どおり静的ファサードへフォールバックするため、Play Mode の起動配線・挙動は本対応の前後で完全に同一。Edit Mode(Timeline ウィンドウでのスクラブ・再生)は `CutsceneManager`/Bootstrap を経由しない別経路(`CutsceneEditModePreviewProvider` が Editor 専用の Manager 群を直接駆動する)。詳細は [26_timeline.md] §4.4 実装メモ(2026-09-19)。
- **2026-09-19 追記(AnchorGroup トラック、[22_anchor_group.md] §5 Presentation 統合)**: `Presentation = new PresentationManager(..., NetBridge, cutscene: Cutscene)` の呼び出しに `groups: Groups` を追加しただけ(`Groups`〔`AnchorGroupPlayer`〕は 303 行付近で既に生成済みのものをそのまま渡す。新しい生成物・新しい生成順序は無い)。`TrackKind.AnchorGroup` トラックが `AnchorGroupPlayer.PlayData` に委譲できるようになる(詳細・設計判断は [08_presentation.md] 実装メモ「2026-09-19、AnchorGroup トラック」参照)。
- **2026-09-20 追記(P1-1、NGO を任意依存の別 asmdef に分離)**: `ResolveNetBridge()` の NGO 分岐を書き換えた。`DDrive.Runtime`(この Bootstrap 自身の asmdef)は `Unity.Netcode.Runtime` を参照しなくなったため、`NetworkManagerRef`/`NgoBridgeRef` の Inspector 直参照フィールドは Bootstrap から削除し、`DDrive.Runtime.Ngo` アセンブリの補助コンポーネント `DDriveNgoBootstrapHook`(NGO を使うシーンで Bootstrap と同じ GameObject に追加する)へ移した。`ResolveNetBridge()` は `Runtime/Net/NetBridgeFactory.cs` の `NetBridgeFactoryRegistry.Current`(`INgoBridgeFactory`)を見て、登録されていれば `factory.Create(...)` に委譲する(NetworkManager/NgoNetBridge の解決・`NgoTransportConfigurator.TryConfigure`・`NetDebugOverlay` 生成・`StartHost`/`StartClient` の遅延実行はすべて `DDrive.Runtime.Ngo` 側の `NgoBridgeFactory` が行う)。登録が無い(NGO 未導入)場合、または `factory.Create` が失敗を返した場合は、どちらも同じ 1 箇所で警告のうえ `LocalLoopbackBridge` にフォールバックする。`NetBridgeFactoryRegistry.Current` への登録は `DDrive.Runtime.Ngo` アセンブリの `NgoBridgeFactoryInstaller` が `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` で行う(NGO 未導入時はこのファイルごとコンパイル対象外になるため、登録が一切起きない)。`OnNetClientDisconnected` の切断検知は `NgoBridgeRef.ClientDisconnected` ではなく `NetBridge.ClientDisconnected`(`INetBridge` 自体のイベント)を購読する形に変えた(NGO 型を経由しない。Loopback は発火しないため既存の挙動は変わらない)。**互換性**: `NetworkManagerRef`/`NgoBridgeRef` フィールドの削除はシリアライズ形式・公開 API の破壊的変更(1.0.0 発効前の例外として実施)。既存シーン(`NetCheckScene.unity` 等)は Unity Editor 経由で `DDriveNgoBootstrapHook` を追加し直した。詳細は [14_networking.md] §13・[47_review_p_tickets_2026-09-20.md] P1-1。
