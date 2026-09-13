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
## 5. データ配置・カタログ構成

**配置の基本原則**: D-Drive に関連するアセット・スクリプトファイルは、基本的にすべて `Assets/DDrive/` 以下に置く。その中を層・種別ごとに適切にディレクトリ分割し、各スクリプトを対応するディレクトリへ配置する（`Assets/` 直下や無関係なフォルダへの散在を禁止）。ディレクトリの分割単位は §4 の asmdef 構成と一致させる。

```
Assets/
  DDrive/                        … システム本体（パッケージ化可）
    Foundation/                  … Registry / Loader / Pool / EventBus / Pause / ValueDef / Validation
    Runtime/                     … 各種別の Data / Manager / Instance
      Audio/  Vfx/  Anim/  Material/  Canvas/  Presentation/  Ui/  Camera/  Net/
    Editor/                      … AssetBrowser / 各専用エディタ / Preview / ID生成 / 依存解析
      AssetBrowser/  Inspectors/  Preview/  Codegen/  Validation/
    Tests/
      Runtime/  Editor/
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
