# 01. アーキテクチャ設計書

関連: [00_requirements.md](00_requirements.md) / [02_core_framework.md](02_core_framework.md)

---

## 1. レイヤー構成

システムは 4 層に分離する。**上の層は下の層しか知らない。**

```
┌─────────────────────────────────────────────┐
│ Game Layer（ゲームロジック）                    │  ← プログラマーが書く。IDしか持たない
│   PlayerController, SkillSystem, ...        │
├─────────────────────────────────────────────┤
│ Presentation Layer（演出統合）                 │  ← PresentationManager.Play(id) で
│   PresentationManager / PresentationData    │     複数アセットを束ねて再生
├─────────────────────────────────────────────┤
│ Manager Layer（実行層）                        │  ← 種別ごとの Manager
│   AudioManager  VfxManager  AnimManager     │     Play/Spawn/Stop/Pool
│   MaterialManager  CanvasManager  ...       │
├─────────────────────────────────────────────┤
│ Foundation Layer（基盤層）                     │  ← 全 Manager が共有
│   AssetRegistry(ID→Data解決)                 │
│   AssetLoader(Addressables)  PoolService    │
│   EventBus  PauseService  ValidationCore    │
└─────────────────────────────────────────────┘
            │
            ▼ (Editor 専用 asmdef)
┌─────────────────────────────────────────────┐
│ Editor Layer                                │
│   AssetBrowser / 各種専用エディタ / Preview /   │
│   Validation UI / ID定数生成 / 依存関係解析     │
└─────────────────────────────────────────────┘
```

## 2. Data / Instance / Handle の分離（本設計の核）

元案の最大の弱点だった「定義と実体の混在」をここで解決する。

```
AssetData (定義・不変・共有)          AssetInstance (実体・可変・個別)
┌──────────────────┐   Spawn   ┌──────────────────────┐
│ VfxData "Fire"   │ ────────▶ │ VfxInstance #1024     │
│  prefab, loop,   │           │  position, velocity,  │
│  lifetime, layer │ ────────▶ │  elapsed, state       │
│  events, flags   │           │  (同じDataから複数生成)  │
└──────────────────┘           └──────────────────────┘
                                        ▲
                              Handle (struct, 世代付き)
                              ゲーム側はこれ経由でのみ操作
```

- **Data**: ScriptableObject。ロードされたら読み取り専用。全 Instance で共有
- **Instance**: Manager 内部のプールで管理。位置・寿命・再生状態を持つ
- **Handle**: `readonly struct { int index; int generation; }`。破棄後のアクセスは黙って no-op（世代不一致で検知）。GC alloc 0

## 3. ID 解決フロー

```
VfxManager.Spawn(VfxId.FireBall, ctx)
   │
   ▼
AssetRegistry.Resolve<VfxData>(id)     … O(1) 辞書引き
   │  ├─ 未ロード → AssetLoader.LoadAsync (UniTask, Addressables)
   │  └─ 未登録   → 警告ログ + PlaceholderData を返す ★モック動作の要
   ▼
PoolService.Rent(data.Prefab)          … プール取得 or 新規生成
   ▼
VfxInstance を初期化 → OnSpawn イベント発火 (EventBus)
   ▼
VfxHandle を返す
```

**Placeholder 戦略**（FR-1.4）: 種別ごとにダミーを定義。
SE=無音 0.5s + コンソールログ / VFX=マゼンタの小球パーティクル / Model=マゼンタ Capsule / Canvas=ID 名を表示するデバッグパネル。
これにより **プログラマーはアセットが 1 つも無くてもモックを完成できる**。

## 4. アセンブリ（asmdef）構成

```
DDrive.Foundation      … Registry/Loader/Pool/Event/Flags/ValueDef (Unity非依存部は最小)
DDrive.Runtime         … 各Data/Manager/Instance (Foundationに依存)
DDrive.Runtime.Audio   … 種別ごとに分割してもよい（チーム規模で判断）
DDrive.Editor          … AssetBrowser/各エディタ/Validation (Runtimeに依存, Editorのみ)
DDrive.Tests.Runtime   … PlayModeテスト
DDrive.Tests.Editor    … Validation/ID生成のテスト
Game.*                 … ゲーム本体。DDrive.Runtime のみ参照（Editor参照禁止）
```

### 外部依存パッケージ（2026-09-14 追記、5-1）

`DDrive.Runtime.PresentationHandle` のイベント公開（[08_presentation.md] §3.5）のため **R3**(Reactive Extensions for Unity)を導入した。

- `Packages/manifest.json`: `scopedRegistries` に UnityNuGet(`https://unitynuget-registry.openupm.com`、スコープ `org.nuget`)を追加し、`org.nuget.r3`(コア型 `R3.dll`。`Observable<T>`/`Unit`/`Subject<T>` 等)と `com.cysharp.r3`(git、`R3.Unity`。Unity 統合層で Player Loop スケジューラ等を提供するが 5-1 時点では未使用)をどちらも 1.3.1 で追加
- `DDrive.Runtime.asmdef` / `DDrive.Samples.asmdef`(`overrideReferences: false`)は R3.dll が自動参照されるため無編集。`overrideReferences: true` の asmdef(`DDrive.Tests.Runtime.asmdef` 等)は `precompiledReferences` に `"R3.dll"` を明示追加する必要がある
- DLL 重複の懸念(`org.nuget.system.runtime.compilerservices.unsafe` と isuzu MCP 側の同名 DLL)があったが、導入後も「Multiple precompiled assemblies」等のエラーは発生せず、isuzu MCP 自体も問題なく動作を継続した(詳細: [08_presentation.md] 実装メモ)

**2026-09-15 追記(6-2)**: `com.unity.test-framework.performance`(**3.4.0**)を `Packages/manifest.json` に正式追加した。`com.unity.test-framework`(EditMode/PlayMode テスト本体)の依存として `Library/PackageCache` に既に transitive で解決されていたバージョンにそのまま固定している(依存関係の版ズレを避けるため)。新規 asmdef `DDrive.Tests.Performance`(`Assets/DDrive/Tests/Performance/`、`Unity.PerformanceTesting` を参照)で 0 alloc 検証([12_review.md] §3、[11_tasks.md] 6-2)に使用。既存 asmdef(`DDrive.Runtime`/`DDrive.Editor`/`DDrive.Tests.Runtime`/`DDrive.Tests.Editor`)は変更していない(この用途は新規テスト asmdef 側だけで閉じている)。

**2026-09-20 追記(P-5、[42_distribution.md] §6)**: `Assets/DDrive/` を `Packages/com.ddrive.core/` へパッケージ化(埋め込みパッケージ)した。7 つの asmdef 名・`references`・`versionDefines` はすべて不変(移設は `AssetDatabase.MoveAsset` による .meta ごとの移動のみで、asmdef の中身は変更していない)。`Assets/DDrive/Samples/` はスクリプトのみ `Packages/com.ddrive.core/Samples~/Demo/`(UPM 標準の import 前提の非コンパイル領域)へ移設し、`DDrive.Samples.asmdef` は開発リポジトリでは import するまでコンパイル対象外になる(実害は無い。参照コード 0 件を確認済み)。テストは開発 `Packages/manifest.json` の `testables: ["com.ddrive.core"]` で引き続き Test Runner から実行できる。

### R3 の asmdef 明示参照 — 調査結果(2026-09-15 追記、[31] A6、P6)

**決定は「(c) asmdef に R3 の明示参照を追加する」だったが、調査の結果 `DDrive.Runtime.asmdef`/`DDrive.Editor.asmdef` 自体は変更しなかった。理由と調査結果を以下に記録する(CLAUDE.md §0-9: asmdef 構成は迷ったら聞く、に該当するため実施を見送り、要判断として残す)。**

- **どの asmdef がどの R3 型に依存しているか**: `DDrive.Runtime`(`Presentation.cs`/`PresentationManager.cs`/`PresentationHandle.cs`)と `DDrive.Editor`(`ScenePresentationPreviewDriver.cs`/`PresentationEditorWindow.Preview.cs`)はいずれも **素の R3**(`org.nuget.r3`、アセンブリ `R3.dll`。`namespace R3` の `Observable<T>`/`Subject<T>`/`Unit`)を使っている。`com.cysharp.r3`(`R3.Unity`、5-1 時点導入・現時点では未使用。UI の R3 化で使う予定のため manifest には残す)の型は現時点でどちらの asmdef からも参照されていない
- **「R3.Unity を references に足すだけ」では済まない**: `R3.Unity.asmdef` 自体が `overrideReferences: true` + `precompiledReferences: ["R3.dll", ...]` で `R3.dll` を参照する側であり(`rootNamespace` は `"R3"` だが中身は Unity 統合の拡張メソッドのみ)、`DDrive.Runtime`/`DDrive.Editor` が使っている `Observable<T>`/`Subject<T>` 等は `R3.Unity` 自身には定義されていない。`references` に `"R3.Unity"` を追加しても、素の R3 型への**明示参照には当たらない**(アセンブリ参照は推移的に解決されないため、`R3.dll` 型を実際に使うコードには `R3.dll` 自体への参照が別途必要)
- **素の R3(`R3.dll`)を明示参照するには `overrideReferences: true` が必要**: `asmdef.references` は他の asmdef(アセンブリ定義)しか書けず、`org.nuget.r3` は asmdef を持たないプリコンパイル DLL(`R3.dll`)なので、`precompiledReferences` への列挙が唯一の手段。`overrideReferences: true` にすると、その asmdef は「暗黙のプリコンパイル DLL 自動参照」から外れ、**そのアセンブリのコードが直接使うプリコンパイル DLL を全て明示的に列挙しないとコンパイルが壊れる**
- **具体的な壊れうる箇所(確認済み)**: `DDrive.Editor` 配下の `Editor/Spec/*.cs`(`SpecWebSender.cs`/`SpecWebParser.cs`/`SpecSyncService.cs`/`SpecSnapshotWriter.cs`/`SpecSheetRow.cs`/`SpecParamSchemaBuilder.cs`)が `Newtonsoft.Json` を直接 `using` している。現在 `DDrive.Editor.asmdef` は `overrideReferences: false` のため暗黙参照で解決できているが、`R3.dll` を明示するために `overrideReferences: true` に切り替えると、**`Newtonsoft.Json.dll` も同時に `precompiledReferences` へ列挙しない限り `Editor/Spec/*.cs` がコンパイルできなくなる**(既存の `DDrive.Tests.Editor.asmdef` が `overrideReferences: true` で `"R3.dll"` に加えて `"Newtonsoft.Json.dll"` も列挙しているのは、同じ理由による実例)。`DDrive.Runtime` 側は grep 上 `Newtonsoft`/`TimeProvider` 等の直接使用は見つからなかったが、`R3.dll` 自身が要求する補助 DLL(`R3.Unity.asmdef` が列挙している `Microsoft.Bcl.TimeProvider.dll`/`Microsoft.Bcl.AsyncInterfaces.dll`)が実際に必要になるかは **Unity Editor でのコンパイルでしか確定できない**(このセッションは Unity MCP 未接続のため実機確認していない)
- **結論**: `overrideReferences: true` への切替は「今使っている型を列挙するだけ」では済まず、各アセンブリが暗黙に頼っている**他の**プリコンパイル DLL を全て洗い出して同時に列挙する必要があり、洗い出しが漏れると静かにビルドが壊れる(デザイナーの作業を止める、CLAUDE.md §0-4 に反する)リスクがある。今回は Unity 実機でのコンパイル確認ができない状態での変更を避け、**asmdef 自体は変更せず**、上記の依存関係をこの節に明記する形で対応した。実際に `overrideReferences: true` へ切り替える場合は、Unity Editor に接続した状態で `DDrive.Runtime`/`DDrive.Editor` 配下の全 `.cs` を洗い出し、コンパイルが green になることを確認してから行うこと
## 5. データ配置・カタログ構成

**配置の基本原則**: D-Drive に関連するアセット・スクリプトファイルは、基本的にすべて `Packages/com.ddrive.core/` 以下に置く（2026-09-20 P-5 で `Assets/DDrive/` から移設。埋め込みパッケージなので開発リポジトリ内では引き続き編集・テスト実行できる）。その中を層・種別ごとに適切にディレクトリ分割し、各スクリプトを対応するディレクトリへ配置する（`Assets/` 直下や無関係なフォルダへの散在を禁止）。ディレクトリの分割単位は §4 の asmdef 構成と一致させる。

```
Packages/
  com.ddrive.core/               … システム本体(埋め込みパッケージ。旧 Assets/DDrive/)
    Foundation/                  … Registry / Loader / Pool / EventBus / Pause / ValueDef / Validation
    Runtime/                     … 各種別の Data / Manager / Instance
      Audio/  Vfx/  Anim/  Material/  Canvas/  Presentation/  Ui/  Camera/  Net/  Shaders/
    Editor/                      … AssetBrowser / 各専用エディタ / Preview / ID生成 / 依存解析
      AssetBrowser/  Inspectors/  Preview/  Codegen/  Validation/  Settings/
    Tests/
      Runtime/  Editor/  Performance/
    Samples~/Demo/                … サンプルスクリプト(Unity からは見えない。Package Manager から import)
    package.json  README.md  Documentation~/
Assets/
  GameData/
    Catalogs/
      AudioCatalog.asset         … 種別ごとのカタログ(ID→Data参照のリスト)
      VfxCatalog.asset
      ...
    Audio/
      SE/
        Player/  SE_Player_Slash.asset   … カテゴリ = フォルダ階層（1アセット1ファイル、NFR-6）
        Enemy/   SE_Enemy_Attack.asset       トリム済み wav 等のベイク生成物は Data の隣に置く
      BGM/
        Battle/  BGM_Battle_Boss.asset
    Vfx/
      Skill/     VFX_Skill_FireBall.asset
    Prefabs/
      Audio/     SeEmitter.prefab       … D-Drive 標準プレハブ（Tools > D-Drive > Generate で生成）
    Model/       MODEL_Player.asset     … モデル本体(2-5)。Prefab とは別概念（下記参照）
    ...
  SourceAssets/                  … 実データ（インポートした音源・モデル等）。人間管理（[10] §3.3）
    Audio/  SE/  Player/  sword_slash_take3.wav
Generated/
  AssetIds.g.cs                  … ID定数（自動生成、手編集禁止）
```

- カタログは Addressables のエントリポイント。起動時（またはシーン単位）にカタログをロードし、Registry に登録（実装: `DDriveRuntimeBootstrap`（[02] §14）が Inspector 直参照 + ラベル `DDriveCatalog` で集める。カタログと Data の Addressables 登録は AssetBrowser の作成パイプラインと Validation が維持する（[02] §5））
- Data 本体は Lazy ロード（カタログは ID とアドレスのみ持つ軽量構造も選択可。`CatalogEntry { ulong id; string address; AssetFlags flags; }`）
- **`GameData/` 配下のファイル名・フォルダ配置はツール（AssetBrowser）が管理する**。人は意味情報（表示名・カテゴリ・識別子）を入力するだけで、上図の規約名・配置はツールが自動生成・追従リネームする（[00] FR-1.5/1.6、[10] §3）。人がファイル名を手付けする運用を前提にしない
- **カテゴリはフォルダ階層にも反映される**（`Player/Attack` → `Audio/SE/Player/Attack/`）。カテゴリ変更後の再配置は `Tools/D-Drive/Generate/GameData をカテゴリ配置に整理` が行う。フォルダはビュー、参照の真実は ID/Address（[10] §3.3）
- シーンへ配置する既製コンポーネント（SeEmitter 等）は `GameData/Prefabs/<ドメイン>/` の**標準プレハブを使うこと推奨**。Phase 4 の Prefab 管理（4-4）もこのルートを基点にする
- **`AssetType.Model`（Phase 2）と `AssetType.Prefab`（Phase 4, 4-4）は別種別**: ModelData はモデル本体（Slots/Avatar/Animation）、PrefabData はゲームプレイ用オブジェクト（GameplayTags/CollisionLayer、弾・ギミック等）。混同しやすいため enum を分けている

## 6. 共通イベントシステム

```
AssetEvent (全種別共通)
  trigger : OnSpawn | OnEnable | OnLoop | OnDisable | OnDestroy
          | Frame(int) | Time(float) | Custom(string)
  action  : PlayAsset(AssetRef) | SetParam | SendMessage(string)
  target  : AssetRef … 任意種別のIDを型安全に参照 { AssetType type; ulong id; }
```

- 実行は Foundation の EventBus が担当。Manager は Instance のライフサイクル節目で `EventBus.Fire(instance, trigger)` を呼ぶだけ
- 例: AnimationData の `Frame(15) → PlayAsset(SE:Slash01)` はデザイナーがエディタで設定

## 7. ポーズ・グローバル制御

```
PauseService.Push(PauseChannel.Gameplay)
  → 全Managerに通知
  → 各Instanceは自分の flags.pauseMode を見て停止/継続を判断
     (PauseMode: PauseWithGame | IgnorePause | UIOnly)
```

音量 Duck・スローモーション（TimeScale 連動）も同型のチャンネル通知で実装する。

## 8. Presentation 層

```
PresentationData "SkillSlash"
  tracks:
    [0.00s] Animation : Attack01        (target: self)
    [0.00s] SE        : SwordSwing03
    [0.10s] VFX       : SlashBlue       (anchor: RightHand)
    [onHit] CameraShake : Small         (条件トラック)
    [onHit] HitStop   : 0.08s
```

- ゲーム側: `PresentationManager.Play(PresentationId.SkillSlash, ctx)` の 1 行
- `ctx` (PlayContext) が self Transform / ターゲット / ヒット通知コールバックを運ぶ
- 各トラックは対応 Manager に委譲するだけ。Presentation 自身は再生しない（薄いオーケストレータ）
- Timeline 連携: トラックの 1 種として TimelineAsset を再生可能

## 9. 主要シーケンス（剣攻撃の例）

```
[Game] SkillSystem.Execute()
  └ PresentationManager.Play(SkillSlash, ctx)
      ├ AnimManager.Play(Attack01, ctx.self)      → AnimHandle
      │    └ Frame15イベント → EventBus → AudioManager.Play(Slash01)
      ├ VfxManager.Spawn(SlashBlue, anchor=RightHand) → VfxHandle
      └ ctx.onHit 発火時
          ├ CameraManager.Shake(Small)
          └ TimeService.HitStop(0.08)
```

## 10. 設計判断の記録 (ADR 抜粋)

| # | 判断 | 理由 |
|---|---|---|
| 1 | ID は enum でなく生成定数 + ulong | enum は並び替え・削除でシリアライズ破壊。ulong は安定・高速・Burst 可 |
| 2 | Handle は class でなく struct + 世代 | GC alloc 0、破棄後アクセスの安全性を世代で担保 |
| 3 | Manager は static でなく DI 可能なサービス + static ファサード | テスト容易性とデザイナー向けの書きやすさを両立 |
| 4 | プレビューは実行時 Manager を Editor で駆動 | Editor 専用経路を作ると「プレビューと実機で違う」問題が必ず起きる |
| 5 | イベントは UnityEvent でなく独自 AssetEvent | シリアライズの安定性・種別横断参照・Validation 対象にするため |
| 6 | UI パーティクルは VFX の RenderMode の 1 つとして吸収 | 別ツール導入の手間をなくし、通常 VFX と同じデータ・同じエディタで作る |
| 7 | UI コントロールは uGUI Selectable 系を使わず UiInteractable 基底に統一 | 標準実装は長押し・リピート・演出連携・応答曲線が後付けになり、プロジェクト内で操作感がバラける |
| 8 | 調整値は ValueDef（定数/パラメトリック曲線/任意カーブ + スピード）に統一 | 種別ごとに float/Curve/Ease が混在すると編集 UI も語彙も毎回別物になり、デザイナーの学習コストが種別数に比例する |
