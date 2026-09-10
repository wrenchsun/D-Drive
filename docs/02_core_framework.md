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
- 解決失敗 → `PlaceholderProvider.Get<T>()` + 警告（モック動作保証）

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

## 6. PoolService

```csharp
public interface IPoolService
{
    PooledObject Rent(GameObject prefab);   // なければ Instantiate
    void Return(PooledObject obj);
    void Prewarm(GameObject prefab, int count);
    void Clear(PoolScope scope);            // シーン遷移時
}
```

- 対象: VFX / AudioSource / Prefab / Canvas / Projectile（AssetFlags.Pool で指定）
- Return 時に `IPoolable.OnReturn()` を呼びリセット（Trail/Particle の Clear 等）
- 上限超過時は Priority 最低の稼働 Instance を強制回収（シーン破棄等で GameObject が死んだ Active エントリは先に台帳から外し、回収しても Free に積めなければ新規 Instantiate に落とす。2026-09-10）

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

## 12. 依存関係グラフ

- Editor 時: 各 Data の `SerializedObject` を走査し `AssetRef` / オブジェクト参照を収集 → `DependencyGraph { ulong → ulong[] }` をキャッシュ（保存時に差分更新）
- 用途: AssetBrowser のツリー表示・使用箇所検索・未使用検出・循環検出
- Scene / Prefab 内の `*IdRef` フィールドも走査対象（使用箇所検索の要）

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
