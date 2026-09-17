# 42. 配布・移植・更新・互換性ポリシー（P チケット）設計書

関連: [11_tasks.md](11_tasks.md)（P チケット表）/ [01_architecture.md](01_architecture.md) §4-5（asmdef・データ配置）/ [02_core_framework.md](02_core_framework.md) §1-2（AssetId・AssetDataBase）/ [09_editor_tools.md](09_editor_tools.md) §4.1（VersionStamp）/ [12_review.md](12_review.md) §3（PR チェックリスト）/ [14_networking.md](14_networking.md) §7・§12（ContentHash・MS2026 移植方針）/ [33_ci_setup.md](33_ci_setup.md) / [34_onboarding.md](34_onboarding.md) / ルートの [CLAUDE.md](../CLAUDE.md)

> **位置づけ（2026-09-17 ユーザー指定）**: Timeline（6-10a〜d）の後に着手する。**P チケットが完了した時点から、D-Drive の変更はすべて本書 §5 の互換性ポリシーに従わなければならない**（それまでは予告扱い。CLAUDE.md §1 の予告参照。完了時に CLAUDE.md §0 TL;DR へ昇格させる = P-13）。
>
> **本書の性格**: 実装前の設計書。**内容は 2026-09-17 時点の実ファイル（asmdef・manifest.json・ProjectSettings・Editor/Runtime コード）を読んで確認した事実に基づく**。確認できなかったこと・決められないことは §7「要判断」に列挙し、推測で確定していない。§7 の A 群は **2026-09-17 にユーザーが回答・確定済み**（P-1/P-2 の着手前提はこれで満たされた）。B/C 群は引き続き着手後・実装中に決める。

## 0. 要約

ユーザー要望は 3 つ:

1. **移植**: D-Drive（このリポジトリで作ってきたシステム）を、実際のゲーム制作プロジェクト（別 Unity プロジェクト。最初の対象は MS2026、[14] §12）へ簡単に持ち込めるようにする
2. **更新**: D-Drive 側に更新があったら、持ち込み先がその更新を取り込めるようにする
3. **互換性**: このタスク以降の変更はすべて互換性を保つ（持ち込み先を壊さない）。守れる具体的ルールとして明文化する

結論:

| 論点 | 結論 |
|---|---|
| 配布方式（§3） | **UPM パッケージ（git URL、`?path=` でサブフォルダ指定、`#vX.Y.Z` タグ固定）**（2026-09-17 決定、§7 A-1）。パッケージ名は `com.ddrive.core`（displayName `D-Drive`）。開発は**このリポジトリ内で埋め込みパッケージ**（`Packages/com.ddrive.core/`、= 現 `Assets/DDrive/` の移設）として行い、分離リポジトリは作らず、持ち込み先は同じリポジトリ（github.com/wrenchsun/D-Drive、private のまま）を `?path=` 付きの git URL で参照する。1 つのソースで「開発（編集可）」と「配布（読み取り専用・版固定）」を兼ねる |
| 線引き（§2） | `Assets/DDrive/` のコード・shader・asmdef と `docs/DesignerManual/` がパッケージ。`GameData/`・`Generated/`・`AddressableAssetsData/`・`Settings/*.asset`・`Specs/`・`SourceAssets/` は持ち込み先ごとのデータ（**持っていかず、ツールが生成する**）。**境界をまたいでいる箇所が 6 系統ある**（§2.3。`SourceAssets/Shaders/` のシステム用 shader、`Assets/DDrive` 固定のパス 5 箇所、`docs/` 参照 等）→ P-4 で解消 |
| 更新（§4） | SemVer。持ち込み先は manifest のタグを進めるだけ。更新後に `Tools > D-Drive > Update` が **マイグレーション → ID/Tuning 再生成 → Addressables 同期 → Validation** をワンボタンで実行する。**スキーマ版は `AssetDataBase.Version`（保存回数）とは別に持つ**（§4.3） |
| 互換性（§5） | 「互換面」を 9 つに定義し（シリアライズ / 列挙 / ID・Address・定数名 / 公開 API / ContentHash / ネットメッセージ / 生成コード / Validation の重さ / Editor 契約）、それぞれに **許可・条件付き・禁止** の変更を列挙。**すべて既存の `Assets/DDrive/Tests/Editor` と同じ流儀の EditMode テスト（スナップショット / ゴールデン値）で機械判定する**（§5.11） |
| いちばん厳しい制約 | **「生成コードの形と ID の導出規則が公開 API である」こと**。`AssetIdGenerator.ToConstantName` / `KnownPrefixes` / 定数クラス名 / `StableHashFromGuid` / `TuningCodegen` の命名規則は、持ち込み先の**ゲームコードがコンパイルできるかどうか**を直接左右する。現状 `KnownPrefixes` に `MODEL`/`ANC`/`ANCG`/`SKIN` が入っておらず定数名に接頭辞が残る不整合（`MODELID.MODELPlayerModel` 等）があり、**発効後は直せなくなる**ため、**発効前（P-5 より前）に `KnownPrefixes` へ追加して解消する**と決定した（2026-09-17、§7 A-6。参照しているコードは Samples を含め 0 件と確認済みで、今なら実害なく変更できる） |

## 1. 前提（確認した事実）

### 1.1 現在の構成

| 項目 | 確認した事実 | 出典 |
|---|---|---|
| Unity | `6000.3.13f1`。`apiCompatibilityLevel: 6`（.NET Standard 2.1）、`activeInputHandler: 1`（Input System Package のみ）、`allowUnsafeCode: 0` | `ProjectSettings/ProjectVersion.txt`、`ProjectSettings.asset` |
| レンダリング | URP（`GraphicsSettings.m_CustomRenderPipeline` に `Assets/Settings/PC_RPAsset` 系が設定済み）。`ShaderPipelineAnalyzer` が `GraphicsSettings.currentRenderPipeline` を見て URP/HDRP/Built-in を判別し、Validation で警告する | `ProjectSettings/GraphicsSettings.asset`、`Runtime/Material/ShaderPipelineAnalyzer.cs` |
| レイヤー・タグ | タグは追加なし。ユーザーレイヤーは `VfxUI`（index 8）のみ。これは `Editor/Vfx/VfxUiSetup.cs` が**空きレイヤーに動的に確保する**（index は持ち込み先で変わり得る）。Rendering Layer は `Default` / `Light Layer 1`（Model/Vfx の `LightLayerMask` 既定値 1 = bit0 のみ使用） | `ProjectSettings/TagManager.asset`、`VfxUiSetup.cs` |
| asmdef | 7 つ: `DDrive.Foundation`（参照: UniTask）/ `DDrive.Runtime`（Foundation, UniTask, Addressables, ResourceManager, **Unity.Netcode.Runtime**, UnityEngine.UI, **Unity.InputSystem**）/ `DDrive.Editor`（+ Addressables.Editor, **URP Runtime/Core**, InputSystem）/ `DDrive.Samples`（Netcode, InputSystem）/ `DDrive.Tests.Editor`・`DDrive.Tests.Runtime`・`DDrive.Tests.Performance`（`overrideReferences: true`、`precompiledReferences` に `R3.dll`・`Newtonsoft.Json.dll`）。Runtime/Editor は `overrideReferences: false` で R3.dll・Newtonsoft.Json.dll を**暗黙参照**（[01] §4 A6 の調査結果） | `Assets/DDrive/**/*.asmdef` |
| 外部依存（実 manifest） | `com.cysharp.unitask`（git、**タグ無し = HEAD 追従。`packages-lock.json` の hash でのみ固定**）、`com.cysharp.r3` 1.3.1（git）、`org.nuget.r3` 1.3.1（scoped registry `https://unitynuget-registry.openupm.com`、scope `org.nuget`）、`com.unity.addressables` 2.3.1、`com.unity.netcode.gameobjects` **2.13.2**（CLAUDE.md §1 の「NGO 2.2」表記は古い。[14] §12 のとおり 2.13.2 に統一済み）、`com.unity.inputsystem` 1.19.0、`com.unity.render-pipelines.universal` 17.3.0、`com.unity.nuget.newtonsoft-json` 3.2.1、`com.unity.ugui` 2.0.0、`com.unity.timeline` 1.8.12、`com.unity.test-framework` 1.6.0、`com.unity.test-framework.performance` 3.4.0、`com.unity.multiplayer.playmode` 2.0.2、`com.unity.multiplayer.center` 1.0.1。**開発専用**: `com.coplaydev.unity-mcp` v10.2.0、`jp.shiranui-isuzu.unity-mcp` v4.2.0 | `Packages/manifest.json`、`packages-lock.json` |
| 生成コード | `Assets/Generated/AssetIds.g.cs`（`namespace DDrive.Generated`、`SEID.PlayerSlash = new(0x…UL, AssetType.Se)` 形式。`AssetId<DDrive.Runtime.Xxx.XxxMarker>` を使うため **DDrive.Runtime に依存**）と `Tuning.g.cs`（`TUNING` / `TUNING_TABLE` / `TUNING_COLUMN`）。**`Assets/Generated/` に asmdef は無い**（= `Assembly-CSharp` に入る） | `Assets/Generated/`、`Editor/Codegen/*.cs` |
| ID の導出 | `AssetIdGenerator.StableHashFromGuid(guid)`（GUID 由来の ulong、冪等）。定数名は `ToConstantName(ファイル名 or DisplayName)`（`_`/`-`/空白で分割、先頭トークンが `KnownPrefixes` = `SE, BGM, VFX, ANIM, ANIM2D, MAT, TEX, CANVAS, PREFAB, PRES, SHAKE, HAPTIC, HAPTICS, UITWEEN` なら除去、C# 識別子に使えない文字を除去）。定数クラス名は `[AssetIdDefinition(AssetType, MarkerType, "SEID")]` の第 3 引数（`SKINID` と `SLIDERSKINID` のように 1 種別に複数可） | `Editor/Codegen/AssetIdGenerator.cs`、`Editor/AssetBrowser/AssetCreationService.cs:83` |
| Address / カタログ | `AddressablesSync` が Data をグループ `DDrive_GameData` に address = カタログの `Address`（ファイル名）で、カタログをグループ `DDrive_Catalogs` にラベル `DDriveCatalog` 付きで登録する。グループ・ラベルは**無ければ `CreateGroup` / `AddLabel` で自動作成**。`DDriveRuntimeBootstrap` は Inspector 直参照 `Catalogs[]` + ラベル `DDriveCatalog` でカタログを集める | `Editor/AssetBrowser/AddressablesSync.cs`、`Runtime/Loop/DDriveRuntimeBootstrap.cs` |
| カタログの中身 | `CatalogEntry { ulong Id; AssetType Type; string Address; AssetFlags Flags; }`。`AssetCatalog` は ID 昇順の追記型 | `Foundation/Registry/CatalogEntry.cs`、`AssetCatalog.cs` |
| ContentHash | `CatalogContentHasher`: 64bit FNV-1a 風、`Id` → `(int)Type` → `Address` 文字列 → `(int)Flags.Net` の順に mix、Entry 間・カタログ間は XOR。`CatalogContentHashMsg { CombinedHash, Catalogs[] }` には**プロトコル版・パッケージ版のフィールドが無い** | `Foundation/Registry/CatalogContentHasher.cs`、`Runtime/Net/CatalogContentHashMessages.cs` |
| ネットメッセージ | `INetMessage` は空のマーカー。`NgoNetBridge` は **`JsonUtility.ToJson` で直列化し、`typeof(T).FullName` をキー**に受信側で型解決する（`_keyToType[key] = typeof(T)`、`JsonUtility.FromJson(json, type)`）。→ **メッセージ struct の型名・名前空間がワイヤ互換の一部** | `Runtime/Net/NgoNetBridge.cs:317,345,391,646,654` |
| バージョン情報 | `AssetDataBase.Version` は**保存回数**（`VersionStampProcessor` が保存ごとに +1）。スキーマ版・パッケージ版を表すフィールドはどこにも無い。git tag も無い（`git tag` 空） | [09] §4.1、`Foundation/Data/AssetDataBase.cs` |
| 互換属性の使用実績 | `[FormerlySerializedAs]` 0 件、`[MovedFrom]` 0 件、`[Obsolete]`（D-Drive 自身の API に付けたもの）0 件。過去のシリアライズ変更は「フィールド追加のみ」（[31] A1: `ImportSourceGuid`/`Assignee`/`SpecUrl` 追加、W-9: `DDriveSpecSettings` の旧フィールドを残して新フィールド追加） | grep 結果、[31] |
| 移植先 | MS2026（LAN 1v1、NGO 2.13.2、Host+Client）。[14] §12 は「`Assets/DDrive/` を asmdef ごとそのまま持ち込み、ゲームコード `Assets/_Project/Scripts/` は `DDrive.Runtime` のみ参照、`Assets/GameData/` はカタログごと移す」と書いている。**本書はこの「コピー」方針を UPM 参照へ置き換える**（§3。[14] §12 の当該段落は P-1 で改訂する） | [14] §12 |

### 1.2 「システム」と「データ」の依存の向き（現状）

```
Assets/Generated/*.g.cs ──(参照)──▶ DDrive.Foundation / DDrive.Runtime      … 生成物 → システム（OK）
Assets/GameData/**/*.asset ──(m_Script GUID)──▶ DDrive.Runtime の Data 型     … データ → システム（OK）
DDrive.Editor ──(文字列パス)──▶ "Assets/GameData/…" "Assets/Generated/…"     … システム → データ。出力先・監視先として妥当。ただし固定値は設定化が要る（§2.3）
DDrive.Editor ──(文字列パス)──▶ "Assets/DDrive/…" "Assets/SourceAssets/Shaders/…" "docs/DesignerManual" … システム → 自分自身/リポジトリ構造。パッケージ化で壊れる（§2.3）
```

## 2. 線引き: 持っていくもの / 持っていかないもの / 持ち込み先で作るもの

### 2.1 分類表

凡例: **P** = パッケージに入れて持っていく / **D** = 開発リポジトリ（このリポジトリ）専用、持っていかない / **G** = 持ち込み先で新しく作る（原則ツールが生成） / **S** = パッケージの `Samples~`（持ち込み先で任意にインポート） / **?** = 要判断（§7）

| 対象 | 分類 | 根拠・備考 |
|---|---|---|
| `Assets/DDrive/Foundation/`, `Runtime/`, `Editor/`（.cs + asmdef） | **P** | システム本体。`Editor/` は `includePlatforms: ["Editor"]` のまま `Editor/` フォルダに置く（UPM の慣習と一致） |
| `Assets/DDrive/Runtime/Ui/Shaders/DDriveUIScroll.shader`, `DDrive_UI_Scroll.mat` | **P** | `Assets/DDrive` 配下で唯一の非コードアセット。`ControlSkinPreviewSection.DefaultScrollMaterialPath` が**絶対パス文字列で参照**しているため、パッケージ化時にパス修正が必要（§2.3-4） |
| `Assets/SourceAssets/Shaders/DDrive_Lit.shader`, `DDrive_Unlit.shader`, `AiStandardSurface/*` | **P（現状は誤配置）** | `UnityMaterialMigrator.LitShaderName = "DDrive/Lit"`（URP Lit 系 → DDrive/Lit へ機械変換）、`AiStandardSurfacePreprocessor.ShaderPath` が参照する**システム構成要素**が「人が管理する実データ」フォルダに置かれている。パッケージ `Runtime/Shaders/` へ移設（§2.3-1） |
| `Assets/DDrive/Tests/{Editor,Runtime,Performance}/` | **P（実行は D）** | UPM 標準の `Tests/` に置く。実行は開発リポジトリの manifest `testables` でのみ行い、**持ち込み先では走らせない**（11 ファイルが `Assets/GameData` / `Assets/SourceAssets`（UnityChan FBX、DDrive/Lit shader、実カタログ）に依存しており、素のプロジェクトでは Inconclusive/Fail になる。§2.3-6） |
| `Assets/DDrive/Samples/`（`NetBridgeSmokeTest`, `NetCheckRunner`, `PresentationSkillSlashDemo`） | **S** | `Samples~/` に移し package.json の `samples` に列挙。`NetCheckRunner` は 6-7 の NetCheck（`Editor/Build/NetCheckBuilder` がシーンパス `Assets/GameData/PreviewScenes/NetCheckScene.unity` を固定参照）で使うため、サンプル「NetCheck」を import した状態を開発リポジトリの前提にする |
| `Assets/GameData/PreviewScenes/{Vfx,Canvas,CameraShake}PreviewScene.unity` + VolumeProfile | **G** | `*PreviewSceneSetup` が無ければ生成する（`VfxPreviewSceneSetup.cs:86-90` で VolumeProfile も生成）。持っていかない |
| `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity`, `NetCheckScene.unity` | **S** | Samples の C# と `VFX_Player_Slash`/`SE_Player_Slash`/`PRES_Demo_SkillSlash` 等のサンプル Data に依存 → サンプル「Demo」「NetCheck」として Data・音源・Prefab ごと `Samples~/` に入れる。import 後は `Assets/Samples/<pkg>/<ver>/…` に展開されるので、`Addressables 登録を同期` で持ち込み先のカタログに載る（ID は GUID 由来なので import 先が変わっても不変） |
| `Assets/GameData/Catalogs/*.asset` | **G** | `AssetCreationService` が種別→カタログ名マッピングで初回作成。持ち込み先は空カタログから始める |
| `Assets/GameData/<種別>/…/*.asset`（このリポジトリの Data 全部） | **D**（一部 **S**） | 開発・確認用のテストデータ（直近コミット「テスト用アセット群」）。サンプルに必要な最小限だけ S へ |
| `Assets/GameData/Prefabs/{Anchors,Audio}/`（標準プレハブ） | **G** | `Tools > D-Drive > Generate` が生成する（[01] §5、[10] §3.3）。生成メニューがパッケージ内のテンプレートに依存しないことを P-5 で確認する |
| `Assets/GameData/Preload/*.asset` | **G** | `ScenePreloadGenerator` が持ち込み先のシーンから集計して生成 |
| `Assets/GameData/Icons/` | **G** | `AssetIconService` が生成 |
| `Assets/GameData/Settings/DDriveSpecSettings.asset`, `DDriveTuningTable.asset` | **G** | `DDriveSpecSettings.GetOrCreate()` が生成。**中身（`WebAppUrl`/`HumanAppUrl`、GameDataRoot）は持ち込み先ごとの値**。トークンは EditorPrefs 側（[32] 実装メモ W-9）なのでリポジトリには入らない |
| `Assets/GameData/Ui/UI_LayerSettings.asset`（`UiLayerSettings`） | **G** | `[CreateAssetMenu("D-Drive/Ui/Ui Layer Settings")]` あり。Bootstrap の Inspector 直参照。セットアップウィザード（P-6）で既定値付きで生成する |
| `Assets/Generated/AssetIds.g.cs`, `Tuning.g.cs` | **G** | **持ち込み先ごとに中身が違う**生成物。持ち込み先で `Regenerate` して作る（最初は空クラス）。出力先を設定化し、**`DDrive.Generated.asmdef` を同時に出力する選択肢**を用意する（**既定 ON**。2026-09-17 決定、§7 A-8。§2.3-5） |
| `Assets/AddressableAssetsData/`（Settings・Groups・Profiles） | **G** | 持ち込み先の Addressables 設定。`AddressableAssetSettingsDefaultObject.GetSettings(true)` で既定設定を作れる（ウィザード）。`DDrive_GameData`/`DDrive_Catalogs` グループとラベルは `AddressablesSync` が自動作成する。ビルドパス・バンドル分割等の Schema 設定は持ち込み先の方針に従う |
| `Assets/Settings/`（URP アセット）, `Assets/Scenes/SampleScene.unity`, `Assets/InputSystem_Actions.inputactions`, `Assets/TextMesh Pro/`, `Assets/TutorialInfo/`, `Readme.asset`, `README_UnityChan_*` | **D** | Unity テンプレート由来。D-Drive コードからの参照は無い（`InputSystem_Actions`・`TMPro` の参照 0 件を grep で確認）。持ち込み先は自分の URP 設定を使う |
| `Assets/DefaultNetworkPrefabs.asset` | **D**（持ち込み先は自前） | NGO の既定 NetworkPrefabsList。[14] §12 のとおり `Assets/` 直下、プロジェクトごと |
| `Assets/SourceAssets/`（音源・モデル・UnityChan・shizuku 等） | **D** | 人が管理する実データ。持ち込み先は `Tools > D-Drive > Generate > SourceAssets の既定フォルダを作成` で空の種別フォルダ + README を作る（Shaders だけ **P** へ移設。上記） |
| `Assets/DDrive/Editor/Menu/DDriveMenu.cs` 等のメニュー | **P** | `Tools/D-Drive/` はそのまま。パッケージ化で変わらない |
| `Tools/CI/`（`run-ci.cmd` 等） | **D**（テンプレを **P** の `Tools~/CI/` に） | 開発リポジトリの CI。持ち込み先向けには「`-executeMethod DDrive.Editor.CI.ValidateAll` / `CI.RegenerateIds` を自分の CI から呼ぶ」手順書 + テンプレスクリプトを同梱する（MS2026 は CI 済みのため合わせる。§7 B-5） |
| `.github/workflows/ci.yml` | **D** | 開発リポジトリ用（手動実行のみ、[33]） |
| `Tools/SpecWeb/`（GAS 発注ツール） | **D**（**?**） | Apps Script プロジェクト（`clasp`）。`assets.json` の `id` は `"Se::Player"` のように**プロジェクト名を含まない**ため、1 デプロイを複数プロジェクトで共有すると識別子が衝突する。→ **持ち込み先ごとに別デプロイ**を推奨。ソースをパッケージ（`SpecWeb~/`）に同梱するか、開発リポジトリのタグから取ってもらうかは要判断（§7 B-3）。D-Drive Editor ↔ GAS の API・JSON 形式は互換面（§5.9） |
| `Specs/assets.json`, `tuning.json` | **G** | `SpecSnapshotWriter` が `Application.dataPath/..`（= 持ち込み先のリポジトリ直下）に書く。持ち込み先のデータ |
| `docs/00〜41_*.md` | **D** | 設計書。パッケージには入れない（GitHub 上のリンクを README から張る） |
| `docs/DesignerManual/*.html`（+ images） | **P** | `ManualPages` が `docs/DesignerManual` を**プロジェクト直下から相対**で探し、`ManualLauncher` が file:// で開く（SpecWeb の `?page=manual` が設定されていれば Web 版）。パッケージでは `Documentation~/DesignerManual/` に同梱し、`Path.GetFullPath("Packages/com.ddrive.core/Documentation~/…")` で解決する（§2.3-3）。デザイナーマニュアルは「デザイナーが実際に触る機能のみ」（[10] §3.4）なので持ち込み先でもそのまま有効 |
| `.claude/skills/ddrive-agent-workflow/`, `AGENTS.md`, `CLAUDE.md` | **D**（消費側向け要約を **P** に、**?**） | 中身は「D-Drive を**開発する**ときの手順」（新種別追加・MCP 検証・SpecWeb 検証）。持ち込み先のエージェントに要るのは「D-Drive を**使う**ときの規約」（禁止 API、静的ファサードの使い方、Data を書き換えない、`.asset` をテキスト編集しない）。→ `Documentation~/AGENTS_CONSUMER.md`（+ 任意で `ddrive-consumer` スキル）を新規に書き、持ち込み先の CLAUDE.md からリンクしてもらう（§7 B-4） |
| `package.json`, `CHANGELOG.md`, `LICENSE`（新規） | **P** | UPM 必須/推奨ファイル。`CHANGELOG.md` は §4.1 の互換性区分を必ず書く |
| `ProjectSettings/*` | **D** | 持っていかない。持ち込み先で**必要な設定**は §3.6 の表のとおりで、P-6 のウィザードが検査・（可能なものは）自動設定する |
| `Packages/manifest.json` | **D** | 持ち込み先は自分の manifest に §3.5 の依存を追加する（ウィザードが検査・追加を提案） |

### 2.2 パッケージ内のレイアウト案

```
Packages/com.ddrive.core/                ← 現 Assets/DDrive/ を移設（.meta ごと。GUID 不変なので GameData の参照は壊れない）
  package.json                          … name / version / unity: "6000.3" / dependencies（レジストリ配布のもののみ、§3.5）/ samples
  CHANGELOG.md  LICENSE  README.md      … README は「導入 5 ステップ」+ docs へのリンク
  Foundation/  Runtime/  Editor/        … 現行のまま（asmdef 名も不変。UPM は Runtime/Editor 以外のフォルダ名を強制しない）
  Runtime/Shaders/                      … DDrive_Lit / DDrive_Unlit / AiStandardSurface / DDriveUIScroll を集約（新設）
  Tests/Editor/ Tests/Runtime/ Tests/Performance/   … 現行の 3 asmdef（開発リポジトリの manifest `testables` で実行）
  Samples~/Demo/ Samples~/NetCheck/     … 現 Samples/ + 依存する GameData/SourceAssets の最小セット（`~` 付きは Unity が import しない。PM の Import ボタンで Assets/Samples/ へ展開）
  Documentation~/DesignerManual/        … 現 docs/DesignerManual/（import 対象外だが file:// で開ける）
  Documentation~/AGENTS_CONSUMER.md     … 消費側エージェント向け規約（§2.1 末尾）
  Tools~/CI/                            … 持ち込み先 CI 用テンプレ（要判断 §7 B-5）
```

開発リポジトリ側に残るもの: `Assets/GameData/`（開発用データ）、`Assets/Generated/`、`Assets/AddressableAssetsData/`、`Assets/SourceAssets/`（Shaders 以外）、`Assets/Settings/`、`Assets/Scenes/`、`docs/`、`Tools/`、`Specs/`、`.claude/`、`AGENTS.md`、`CLAUDE.md`、`.github/`。

### 2.3 境界をまたいでいる箇所（現状のままでは移植の障害になるもの）

すべて実ファイルで確認済み。P-4 の対象。

| # | 箇所 | 何が起きるか | 対処案 |
|---|---|---|---|
| 1 | `Assets/SourceAssets/Shaders/DDrive_Lit.shader` / `DDrive_Unlit.shader` / `AiStandardSurface/*.shader,*.hlsl` | 持ち込み先に存在しない → `UnityMaterialMigrator`（URP Lit → `DDrive/Lit` 変換）と `AiStandardSurfacePreprocessor`（`ShaderPath` 定数 `Assets/SourceAssets/Shaders/AiStandardSurface/DDrive_AiStandardSurface.shader`）が機能しない。`AiStandardSurfaceMapperTests` / `UnityMaterialMigratorTests` は `Assume` で Inconclusive | パッケージ `Runtime/Shaders/` へ移設。`ShaderPath` は `Shader.Find("DDrive/…")` か `Packages/com.ddrive.core/…` パスへ |
| 2 | `Editor/Validation/CI.cs:31` `ForbiddenApiScanner.Scan("Assets/DDrive")` | パッケージ化すると走査対象フォルダが無く**違反 0 件として静かに通る**（CI の握りつぶし） | 走査ルートを `PackageInfo.FindForAssembly(typeof(CI).Assembly).resolvedPath` から導出。フォルダが無ければ Error にする |
| 3 | `Editor/Manual/ManualPages.cs:15` `FolderRelativePath = "docs/DesignerManual"`（プロジェクト直下相対） | 持ち込み先にはローカルのマニュアルが無い → 「マニュアル」ボタンが file:// を開けない | `Documentation~/DesignerManual/` に同梱し `Path.GetFullPath("Packages/com.ddrive.core/Documentation~/DesignerManual")` で解決（開発リポジトリでは埋め込みパッケージのため同じパスで動く） |
| 4 | `Editor/Ui/ControlSkinPreviewSection.cs:895` `DefaultScrollMaterialPath = "Assets/DDrive/Runtime/Ui/Shaders/DDrive_UI_Scroll.mat"` | パッケージ化でパスが `Packages/com.ddrive.core/Runtime/Ui/Shaders/…` に変わり `LoadAssetAtPath` が null | GUID 固定参照（`AssetDatabase.GUIDToAssetPath`）か `Packages/com.ddrive.core/` 起点に変更 |
| 5 | `Editor/Dependencies/CodeReferenceScan.cs:32`（`ScanRoots` の定義。旧版の本書では `:9` としていたが誤りで、正しくは 32 行目。2026-09-17 訂正） `ScanRoots = { "DDrive", "Generated" }`（`Application.dataPath` 直下）、`Editor/Spec/SpecWebSender.cs` `ScanRoots`（同形式） | 「安全な削除」のコード参照チェック（[10] §3）と調整値の未使用キー検出が**持ち込み先のゲームコード（例: `Assets/_Project/Scripts`）を一切見ない** → 使用中の ID を削除してもコンパイルエラーを予告できない | 走査対象を「`Assets/` 配下の全 `.cs`（`Library`・`Packages` 除外、大きいフォルダは除外設定可）」に変更。D-Drive 自身のコードはパッケージ側パスを追加 |
| 6 | `Tests/Editor/ImportRuleServiceTests.cs:30-31`（`Assets/SourceAssets/Data/UnityChan/...fbx` 固定）、`AssetSearchTests.cs:41`（`Assets/GameData`）、`ContentHashCatalogCoverageValidatorTests.cs`（実カタログ）、`AiStandardSurfaceMapperTests` / `UnityMaterialMigratorTests`（`Assume` で shader 依存） | 素のプロジェクトでテストが Fail/Inconclusive。テストがパッケージに同梱されても持ち込み先で価値を持たない | フィクスチャを `Tests/Editor/Fixtures/` に持つ（UnityChan は再配布条件があるため自作の最小 FBX に差し替え）。持ち込み先ではテストを走らせない方針（§2.1）でも、開発リポジトリの `Samples~` 未 import 状態で green にする |
| 7 | `Assets/Generated/` に asmdef が無い | 持ち込み先のゲームコードが asmdef 配下（`Game.*`、[01] §4 の想定）だと `Assembly-CSharp` の `DDrive.Generated` を参照できない（asmdef は Assembly-CSharp を参照できない） | `Regenerate` 時に `DDrive.Generated.asmdef`（参照: `DDrive.Foundation`, `DDrive.Runtime`）を同時出力するオプション（**既定 ON**。2026-09-17 決定、§7 A-8） |
| 8 | `Packages/manifest.json` の `com.cysharp.unitask` にタグが無い | 持ち込み先の初回 resolve で**別のコミット**が入り得る（開発側は lock の hash で固定されているだけ） | 持ち込み先向け依存表（§3.5）では UniTask をタグ付き（`#2.5.x` 等、P-1 で実際に使っている hash が属するタグを確認）で指定。開発側 manifest も同じタグに揃える |
| 9 | `DDrive.Runtime.asmdef` が `Unity.Netcode.Runtime` を参照（`NgoNetBridge` 等） | シングルプレイの持ち込み先でも NGO 2.13.2 の導入が必須 | **`versionDefines` で `DDRIVE_NGO` を切り、NGO を必須依存から外す**（2026-09-17 決定、§7 A-7。旧版の本書では誤って「§7 B-1」を参照していたが、正しくは A-7）。`NgoNetBridge`/`NetDebugOverlay`/Bootstrap 等の NGO 依存箇所を `#if DDRIVE_NGO` で囲む（P-4 で対応。MS2026 自体は NGO を導入するため実質的な影響はない） |
| 10 | `CatalogContentHashMsg` に版情報が無い | Host と Client で D-Drive の版が違うとき「カタログ不一致」としてしか見えず原因が分からない | `PackageVersion`（string）と `ProtocolVersion`（int）を**フィールド追加**（JsonUtility は未知/欠落フィールドに寛容なので旧版と混在しても落ちない）。判定は §5.6 |

## 3. 配布方式

### 3.1 比較

評価軸は「持ち込みの手間」「更新の手間」「版の固定・追跡」「依存の宣言」「持ち込み先の改造の抑止」「開発リポジトリとの二重管理」「Addressables/エディタ拡張/生成コードとの相性」。

| 方式 | 持ち込み | 更新 | 版固定 | 依存宣言 | 改造抑止 | 二重管理 | 主な難点 |
|---|---|---|---|---|---|---|---|
| **A. UPM（git URL + `?path=` + `#tag`）** | manifest 1 行 + 依存追加 | タグを書き換える（PM UI 可） | ◎ タグ/コミット | ○ レジストリ配布のものは package.json で宣言。**git 配布（UniTask/R3）と scoped registry は宣言不可** → ウィザードで補う | ◎ `Library/PackageCache` 展開で読み取り専用 | ◎ 開発リポジトリを埋め込みパッケージにすれば単一ソース | 読み取り専用ゆえ生成物・設定は Assets 側へ（§2.3）。private リポジトリなら持ち込み先の git 認証が要る |
| B. UPM（埋め込み: `Packages/` 直下にフォルダをコピー） | フォルダコピー | 手動コピー/上書き（削除ファイルが残る） | △ 手作業 | ○ | × 自由に改造できてしまう | △ | 「改造して更新できなくなる」典型経路。A の**退避策**（PackageCache からコピーして一時的に埋め込む）としてのみ許容 |
| C. `.unitypackage` / tarball | インポート操作 | 再インポート（**削除されたファイルが残りコンパイルエラー**） | △ ファイル名運用 | × | × | × | 版・依存の情報が無い。tarball を `file:` で UPM 参照する形なら A の亜種だが配布経路が手動 |
| D. git submodule / subtree（`Assets/` 配下） | submodule 追加 | `git submodule update` / subtree pull | ○ コミット | × | △ | ○ | リポジトリ全体（GameData/Tests/docs）を引き込む → 分割リポジトリか subtree split が別途要る。Unity 上で submodule 直下の .meta 管理が事故りやすい |
| E. `Assets/DDrive/` を手でコピー（現 [14] §12 の方針） | フォルダコピー | diff を見て上書き | × | × | × | × | 現状。「何版か」「何が変わったか」「どこを持ち込み先が触ったか」を機械的に追えない |

### 3.2 推奨: A（UPM git URL）+ 開発リポジトリは埋め込みパッケージ

**決定（2026-09-17、§7 A-1/A-2）**: `Assets/DDrive/` を **このリポジトリの `Packages/com.ddrive.core/` に移設**（埋め込みパッケージ。編集可・テスト実行可）し、分離リポジトリは作らず、持ち込み先は同じリポジトリ（`github.com/wrenchsun/D-Drive`。private のまま）を

```json
"com.ddrive.core": "git+ssh://git@github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0"
```

の形（`git+ssh` URL）で参照する（[14] §12 の「`Assets/DDrive/` をそのまま持ち込み」を置き換える）。

採った理由:

1. **単一ソース**: 開発リポジトリ自身がパッケージ形（読み取り専用相当のパス構造）で動くので、「パッケージにしたら壊れた」を開発側で先に踏める（§2.3 の 2〜5 はまさにこの型の事故）。別リポジトリに切り出して同期する方式（subtree split）は二重管理になる
2. **版が manifest に残る**: 持ち込み先の `manifest.json` / `packages-lock.json` に「どの版か」が commit される。更新 = 1 行の変更 + 手順（§4.2）。ロールバックは revert
3. **改造を構造的に止める**: 読み取り専用なので持ち込み先が D-Drive を直接いじれない → §5 の互換性ポリシーが「持ち込み先が壊れないこと」を保証できる前提が成立する。必要な差し替えは既存の拡張点（`IValidator` の自動発見、`ImportRule` ハンドラ、`IHapticOutput`、`INetBridge`、`IAssetBehaviour`、`DataEditor`）で行う
4. **Unity 標準の導線**: Package Manager の UI で版・Samples・ドキュメントリンクが見える。デザイナーにも「パッケージを更新する」で説明できる

採らなかった理由（要約）: B/C/E は「更新で壊れる・改造して戻れなくなる」を防げない。D はリポジトリ分割が必要で、Unity 上の運用も A より不安定。

### 3.3 Addressables との相性

- **登録対象は持ち込み先の `Assets/GameData/` のみ**。パッケージ内のアセットで Addressables 登録が要るものは現状無い（`DDrive_UI_Scroll.mat` は Skin から直参照、Shader は `Shader.Find`）。Samples は import 後 `Assets/Samples/…` に展開されるので通常の Data と同じ経路で登録できる
- Addressables のエントリは Group アセット（`Assets/AddressableAssetsData/AssetGroups/*.asset`）側に GUID で持つので、設定・グループはすべて持ち込み先の所有物。`AddressablesSync` はグループ・ラベルが無ければ作る（確認済み）が、**`AddressableAssetSettings` 自体が無い（Addressables 未初期化）と `IsAvailable = false` で何もしない** → P-6 のウィザードが `AddressableAssetSettingsDefaultObject.GetSettings(true)` で初期化する
- パッケージ内アセットを Addressables に登録すること自体は仕様上可能だが、本設計では使わない（P-5 で「登録が要らないこと」をテストで固定する。要確認事項ではあるが方針に影響しない）
- カタログ（`AssetCatalog` .asset）は持ち込み先のもの。ContentHash は Entry（Id/Type/Address/Net）のみに依存するため、**パッケージの版が同じで GameData が同じなら Host/Client で一致する**（§5.6）

### 3.4 エディタ拡張の扱い（依存の向き）

- `DDrive.Editor`（パッケージ側）→ `Assets/` 側は**文字列パス**でしか参照していない（`AssetCreationService.DefaultGameDataRoot = "Assets/GameData"` 等 21 箇所。§2.3 の 5 箇所以外は「出力先・監視先の既定値」で、パッケージ化しても意味が変わらない）。型参照は無い（asmdef 参照は Foundation/Runtime と Unity パッケージのみ）→ **依存の向きは逆転しない**
- `DDrive.Editor` は `Unity.RenderPipelines.Universal.Runtime`/`Core.Runtime`、`Unity.Addressables.Editor`、`Unity.InputSystem` を参照 → 持ち込み先に URP・Addressables・Input System が無いと Editor asmdef がコンパイルできない（Runtime も Addressables/Netcode/InputSystem/ugui が必須）。§3.5 の依存表のとおり
  - **2026-09-18 追記（[26] §7.1-10、asmdef 構成変更をユーザー承認）**: Timeline（6-10a）で `DDrive.Runtime` に `Unity.Timeline` と `Unity.RenderPipelines.Universal.Runtime`/`Core.Runtime` を**直接追加**する（Cutscene のカメラが URP の `Volume`/`DepthOfField` へ書き込むため、[26] §4.6.4）。これにより URP は「Editor 拡張の都合で要る依存」から**ゲーム実行時の必須依存**になる（`DDrive.Runtime` がコンパイルできる条件）。B-1（URP 以外は非対応）とは整合し、新しいパッケージ依存は増えない（両方とも manifest に導入済み）が、§3.5 の「持ち込み先での扱い」と §5.10 の区分に反映した
- 持ち込み先のゲームコードは `DDrive.Runtime`（と `DDrive.Foundation`）のみ参照、`DDrive.Editor` 参照禁止（[01] §4）。パッケージ化でこの規約は変わらない
- 既定値パスの設定化: `DDriveSpecSettings.GameDataRoot` は既に設定化済み。`ImportRuleService.DefaultSourceRoot`（`Assets/SourceAssets`）・`AssetIdGenerator.DefaultOutputPath`・`TuningCodegen.DefaultOutputPath`・`AssetIconService.DefaultIconRoot`・`ScenePreloadGenerator.DefaultOutputRoot` は P-5 で **プロジェクト設定（§4.3 の `DDriveProjectSettings`）から読む**ようにし、既定値は現状のままにする（持ち込み先が既存のフォルダ構成を持つ場合の逃げ道。§7 B-6）

### 3.5 外部依存の宣言

`package.json` の `dependencies` に書けるのは**レジストリ配布のパッケージだけ**（git URL・ローカルパス・scoped registry の宣言は不可。Unity の仕様）。

| 依存 | 現在の版 | package.json で宣言 | 持ち込み先での扱い |
|---|---|---|---|
| `com.unity.addressables` | 2.3.1 | ○ | 自動解決 |
| `com.unity.netcode.gameobjects` | 2.13.2 | ×（2026-09-17 決定、§7 A-7。`versionDefines` で `DDRIVE_NGO` を切り、必須依存から外す） | NGO を使う持ち込み先のみ導入する。導入すると asmdef の `versionDefines` が `DDRIVE_NGO` を自動定義し NGO 連携コードが有効になる。導入する場合は **Host/Client 全員同じ版**（[14] §12） |
| `com.unity.inputsystem` | 1.19.0 | ○ | 自動解決 + Active Input Handling の確認（§3.6） |
| `com.unity.render-pipelines.universal` | 17.3.0 | ○ | 自動解決 + URP アセットが GraphicsSettings に設定済みかの確認。**2026-09-18: 6-10 以降は `DDrive.Editor` だけでなく `DDrive.Runtime` も参照する（[26] §4.6.4 の Volume 書き込み。asmdef 変更は同日ユーザー承認）= ゲーム実行時の必須依存**。`versionDefines` で切らない（B-1 のとおり URP 以外は非対応のため、切る意味が無い） |
| `com.unity.nuget.newtonsoft-json` | 3.2.1 | ○ | 自動解決（`Editor/Spec/*` が使用） |
| `com.unity.ugui` | 2.0.0 | ○ | 自動解決 |
| `com.unity.timeline` | 1.8.12 | ○（6-10 以降） | 自動解決。**2026-09-18: 6-10a で `DDrive.Runtime` が `Unity.Timeline` を参照する（必須依存。`versionDefines` で切らない）** |
| `com.cysharp.unitask` | git（タグ無し） | **×** | ウィザードが manifest を検査し、無ければ `Client.Add("https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#<タグ>")` を提案・実行（`UnityEditor.PackageManager.Client.Add` は git URL を受け付ける） |
| `com.cysharp.r3` | git 1.3.1 | **×** | 同上（`#1.3.1`） |
| `org.nuget.r3` | 1.3.1（scoped registry） | **×** | scoped registry `Unity NuGet`（`https://unitynuget-registry.openupm.com`、scope `org.nuget`）の追加が必要。**`Client.AddScopedRegistry` が public API か未確認**（要確認 §7 C-1）。無理なら manifest.json への追記手順を README に載せ、ウィザードは「無い」ことの検出と案内だけ行う |
| `com.unity.test-framework` / `.performance` | 1.6.0 / 3.4.0 | ×（開発専用） | 持ち込み先ではテストを走らせないため不要 |
| `com.unity.multiplayer.playmode` / `.center` | 2.0.2 / 1.0.1 | ×（開発専用） | MPPM は開発側の 2 クライアント確認用 |
| `com.coplaydev.unity-mcp` / `jp.shiranui-isuzu.unity-mcp` | v10.2.0 / v4.2.0 | ×（開発専用） | **持っていかない**（asmdef 参照も無い） |

依存の版は `package.json` に**固定値**で書く（範囲指定にしない。NGO は特に全員同時更新が前提）。依存の版を上げる変更の区分は §5.10。

### 3.6 ProjectSettings への依存と伝え方

| 設定 | 必要な状態 | 自動化 | 根拠 |
|---|---|---|---|
| Render Pipeline | URP アセットが `GraphicsSettings`/`QualitySettings` に設定済み | **検査のみ**（勝手に変えない） | `ShaderPipelineAnalyzer` が URP 前提で警告。DDrive/Lit・Unlit・AiStandardSurface・UI Scroll は URP shader。HDRP/Built-in は非対応（§7 B-2） |
| Active Input Handling | `Input System Package` または `Both` | 検査のみ（変更は Editor 再起動を伴う） | `GamepadHapticOutput` が `Gamepad.current` を使用。`Old` だと Input System が無効 |
| API Compatibility Level | .NET Standard 2.1 以上 | 検査のみ | R3 1.3.1 の要件（Unity 6 の既定値なので通常は問題なし） |
| レイヤー | `VfxUI` ユーザーレイヤー（UIOverlay VFX を使う場合のみ） | **自動**（`VfxUiSetup` が空きに確保。既存） | VfxData は layer index を数値で持つため、**プロジェクト間で VfxData を移す運用は非対応**（§5.1 備考） |
| Rendering Layers | 追加不要（bit0 = Default のみ使用） | – | `LightLayerMask` 既定 1 |
| Addressables | `AddressableAssetSettings` が存在 | **自動**（`GetSettings(true)`） | §3.3 |
| NGO | `NetworkManager` をシーンに置く（NGO を使う場合） | 手順書 | Bootstrap は生成しない（[14] §12） |
| EditorBuildSettings | 確認用シーンを入れる必要なし | – | 確認用シーンは Editor 専用 |
| TagManager のタグ | 追加不要 | – | 使用箇所なし |

伝え方: **P-6 の `Tools > D-Drive > Setup > セットアップウィザード`**（1 画面。依存 → ProjectSettings → Addressables → フォルダ/設定 SO → 起動オブジェクト の順に「OK / 直す / 手順を見る」）+ 同じ検査を `IValidator`（`ProjectSetupValidator`）として `Validation > Run All` にも載せる（CI で継続検出）。手順書は `README.md`（パッケージ）と [34] に節を追加。

### 3.7 Unity バージョン

- `package.json` の `unity` は `"6000.3"`（`unityRelease` は書かない。これより古い Editor では Package Manager が導入を拒否する）
- 持ち込み先が 6000.3 より新しい場合: 禁止しないが動作保証しない。**開発リポジトリの版を先に上げてから**持ち込み先が追従する運用（CLAUDE.md §1「勝手に上げない」）。Unity の版を上げる変更の区分は §5.10
- 持ち込み先が古い場合: 非対応（Unity 6 の API・`AssetSearch` の回避策（[09] §9）等が 6000.3 前提）

## 4. 更新の取り込みフロー

### 4.1 バージョンの付け方（SemVer）

`MAJOR.MINOR.PATCH`。**何を上げるかは §5 の互換面ごとの区分で機械的に決まる**（人の裁量を残さない）:

| 区分 | 上げる桁 | 例 |
|---|---|---|
| 互換面のいずれかを**破壊**する（§5 各表の「禁止」を、手続き §5.12 を踏んで行う） | MAJOR | 公開 API の削除、シリアライズ形式の非自動移行、ContentHash 算法変更、`ToConstantName` 規則変更 |
| 互換面に**追加**する / 自動マイグレーションを伴う変更 / `[Obsolete]` 付与 / Validation の新 Warning | MINOR | 新 AssetType（末尾追加）、Data の新フィールド、新 API、新メッセージ型 |
| 互換面に触れないバグ修正・Editor UI の改善 | PATCH | ウィンドウの見た目、Validator の誤検知修正 |

- 版の置き場: `package.json` の `version`（正）+ `Foundation/DDriveVersion.cs` の `public const string Value`（ランタイムから読める写し。**両者の一致を EditMode テストで固定**: `PackageInfo.FindForAssembly(typeof(DDriveVersion).Assembly).version == DDriveVersion.Value`）
- git tag `vX.Y.Z` をパッケージの `version` と一致させる（持ち込み先の `#vX.Y.Z` がこれを指す）
- `CHANGELOG.md`（Keep a Changelog 形式）に**互換性の節を必須化**: `### 互換性` に「破壊なし / 追加のみ / マイグレーションあり（自動・手動）/ 破壊あり（移行ガイド リンク）」のいずれかを必ず書く。P-3 の CHANGELOG ガード（§5.11-8）が空欄を fail にする
- 最初の版: **1.0.0**（2026-09-17 決定、§7 A-3。0.x は SemVer 上「何を壊してもよい」の意味になり、ユーザー要望「以降は互換性を持たせる」と矛盾する）。**P-5（パッケージ化）で `1.0.0` を発効させる**

### 4.2 持ち込み先の更新手順（標準）

1. **読む**: 対象版までの `CHANGELOG.md` の「互換性」節を読む。「破壊あり」なら移行ガイドを先に読む（§5.12）
2. **退避**: 作業ツリーをクリーンにしてブランチを切る（`.asset` の一括変更が起こり得るため、PR として見られる状態にする）
3. **版を進める**: `Packages/manifest.json` のタグを書き換える（または Package Manager の UI）。`packages-lock.json` も変わる
4. **Unity を開く**: コンパイルエラーがあれば「破壊あり」の変更を踏んでいる → 移行ガイドへ。`[Obsolete]` 警告は次の MAJOR までに直す TODO
5. **`Tools > D-Drive > Update > 更新を適用`**（P-8）: 前回適用した版（`ProjectSettings/DDriveProjectSettings.asset` の `LastAppliedVersion`）と今の版を表示し、以下を**この順で**実行する
   1. データマイグレーション（§4.3。ドライランで対象件数を表示 → 実行。`Undo.RecordObject` + `SetDirty`、`VersionStampSuppression` 内で行い保存回数を汚さない）
   2. `Regenerate Asset IDs` / `Regenerate Tuning Keys`（生成規則が変わっていれば差分が出る = §5.7 の互換面なので通常は無差分）
   3. `Addressables 登録を同期`
   4. `Validation > Run All`（新しい Warning はここで初めて見える。Error は §5.8 の 2 段階ルールにより更新直後には増えない）
   5. `LastAppliedVersion` を更新
6. **検査**: 持ち込み先の CI（`-executeMethod DDrive.Editor.CI.ValidateAll` / `CI.RegenerateIds` + `git diff --exit-code` / EditMode / PlayMode。開発リポジトリの `Tools/CI/run-ci.cmd` と同じ 4 段。テンプレは `Tools~/CI/`）
7. **実機**: 起動 → SE 1 件再生 → （NGO を使うなら）Host/Client を**両方同じ版で**起動して ContentHash 一致を確認（版が違うと §5.6 の判定で切断/警告される）
8. **コミット**: manifest / lock / マイグレーションで変わった `.asset` / `Assets/Generated` を 1 コミットに

### 4.3 データマイグレーション（スキーマ版）

**判断: スキーマ版は `AssetDataBase.Version` とは別に持つ必要がある。** 理由: `Version` は「保存回数」（[09] §4.1、ユーザー決定「データ形式は変えない」）で、値からは「どの形式で書かれたか」が分からない。Unity には「このアセットをどの版のコードが最後に保存したか」を知る仕組みが無い。

設計:

- **アセット側**: `AssetDataBase` に `[HideInInspector] public int SchemaVersion;` を**追加**（フィールド追加のみ。既存 .asset は 0 で読まれる = 「1.0.0 以前の形式」の意味。**シリアライズ変更につき §7 A-4 で 2026-09-17 に承認済み**。既存の `Version` フィールドは「保存回数」であって schema 版ではないため、`SchemaVersion` は別フィールドとして持つ）。`VersionStampProcessor` が保存時に `SchemaVersion = DDriveSchema.Current` を書く。**`SchemaVersion` は `AssetDataBase` 派生 = Data 全体で 1 本の整数**（種別ごとに分けない。分けると「どの種別が何版か」の管理が増えるだけで、マイグレーション側で `data is XxxData` を見れば足りる）
- **`AssetDataBase` 派生でない SO**（`AssetCatalog`、`TuningTable`、`UiLayerSettings`、`DDriveSpecSettings`、`ScenePreloadList`、`Anim2DImportProfile` 等）は個別に `SchemaVersion` を持たせず、**プロジェクト側の `LastAppliedVersion` だけで判断する**（数が少なく、ツールが所有しているため）
- **プロジェクト側**: `ProjectSettings/DDriveProjectSettings.asset`（`ScriptableSingleton<T>` + `[FilePath]`。持ち込み先のリポジトリにコミットされる）に `LastAppliedVersion`（string）、`AppliedMigrationIds`（string[]）、§3.4 の出力先パス設定を持つ
- **マイグレーション**: `Editor/Migration/IDataMigration { string Id; int FromSchema; int ToSchema; bool AppliesTo(AssetDataBase); void Migrate(AssetDataBase, MigrationContext); }`。`IValidator` と同じく **TypeCache で自動発見・登録リスト無し**。`DDriveMigrationRunner` が `SchemaVersion < Current` の Data を `AssetSearch.FindAssets("t:AssetDataBase")` で集め、From→To の順に適用。SO 単位のマイグレーション（カタログ等）は `IProjectMigration { string Id; void Migrate(); }` で `AppliedMigrationIds` に無いものを実行
- **書き方の制約**（§5.1 と対応）: マイグレーションは**旧フィールドを消さない**（旧フィールドは `[HideInInspector] [Obsolete]` で次の MAJOR まで残す）→ 同一 MAJOR 内なら**ダウングレードしても旧コードが旧フィールドを読める**（§4.4）
- **CI**: `DDrive.Editor.CI.MigrateCheck`（未適用のマイグレーションがあれば exit 1）を `ValidateAll` の前段に追加。`ValidateAll` 自体にも `SchemaVersion` が古い Data を Warning で出す Validator を追加
- **Unity 標準の道具を先に使う**: フィールド改名は `[FormerlySerializedAs]`（コードを伴わない = マイグレーション不要、MINOR）。クラス名・名前空間の移動は `[MovedFrom]`。`IDataMigration` を書くのは「値の変換が要るとき」だけ

### 4.4 ロールバック（更新の取りやめ）

- 手順: 更新コミット（manifest / lock / `.asset` / Generated）を `git revert`。Unity を開き直す。`Library/PackageCache` は自動で前の版に戻る
- 成立条件（= §5.1 のルールが守られている前提）: 同一 MAJOR 内では旧フィールドが残っているので、新版で保存された `.asset` を旧版が開いても**旧フィールドから読める**。新版で追加されたフィールドは旧版では「未知のフィールド」として無視され、**次に保存したときに消える**（Unity の仕様）→ 再度更新すると既定値に戻る。これは許容する（データを失うのは新機能の分だけ）
- 成立しないケース: MAJOR をまたぐ更新（旧フィールドが削除され得る）。**MAJOR 更新のロールバックは「更新前のコミットに戻す」以外に保証しない**と明記する（移行ガイドに書く）
- ContentHash: ロールバック後は Host/Client を再度揃える

### 4.5 持ち込み先で D-Drive を改造していた場合

- 方針: **原則禁止**（2026-09-17 決定、§7 A-9）。git URL 参照では `Library/PackageCache` 内の編集は resolve のたびに消えるので、構造的に「改造して忘れる」が起きない
- どうしても必要なとき: (1) まず既存の拡張点で解決できないか（`IValidator` 自動発見 / `ImportRule` ハンドラ / `IHapticOutput` / `INetBridge` / `IAssetBehaviour` / `[DataEditor]` / `Placeholder` 差し替え）。(2) 修正を**開発リポジトリへ PR**（CLAUDE.md §3 の手順で）し、次の PATCH/MINOR で取り込む。(3) 緊急回避としてのみ、PackageCache から `Packages/` へコピーして埋め込み化（B 方式）。**埋め込み化した時点で §4.2 の更新手順の対象外**（差分を人が 3-way マージする）。埋め込み中であることを `ProjectSetupValidator` が Warning で出し続ける（`PackageInfo.source == Embedded` かつ開発リポジトリでない = `DDriveProjectSettings.IsDevelopmentRepo == false` のとき）。**この強制手段（Warning 検知）は P-6 以降で実装する。`DDriveProjectSettings` および `IsDevelopmentRepo` は 2026-09-17 時点では未実装で、本節は設計のみ**
- 持ち込み先固有のコード（ゲーム側のファサード呼び出し、独自 Validator 等）は持ち込み先の asmdef に置く。パッケージの名前空間 `DDrive.*` を持ち込み先で使わない（`partial` や拡張メソッドの衝突を避ける）

### 4.6 何が壊れうるか → 検知手段（更新時チェック表）

| 壊れうるもの | 症状 | 検知（更新前 = 開発側 CI） | 検知（更新後 = 持ち込み先） |
|---|---|---|---|
| ゲームコードのコンパイル | 静的ファサード / Handle / ID 定数名の変更 | `PublicApiSnapshotTests`、`CodegenGoldenTests`（§5.11） | Unity のコンパイルエラー（手順 4） |
| 既存 `.asset` の読み込み | フィールド消失・型変更で値が既定値に戻る/欠落警告 | `SerializedLayoutSnapshotTests` + `LegacyAssetFixtureTests` | `MigrateCheck`、Validation の「SchemaVersion が古い」Warning、Console の `Unknown field` 警告 |
| 種別・フラグの意味 | enum の値ズレで Se が Bgm 扱いになる等 | `SerializedEnumSnapshotTests`（全シリアライズ enum） | Validation（`CatalogEntry.Type` と Data の種別不一致は既存の Registry 解決で警告） |
| ID / Address / 定数名 | 再生成で ID や定数名が変わる | `IdHashGoldenTests`、`ConstantNameGoldenTests` | 手順 5-2 の `Regenerate` 後の `git diff`（差分が出たら規則が変わっている） |
| ContentHash | Host/Client 不一致 | `CatalogContentHasherGoldenTests` | 接続時の `CatalogContentHashGate`（版不一致は専用の理由文で表示、§5.6） |
| ネットメッセージ | 旧版 Client からのメッセージを新版 Host が解釈できない | `NetMessageSnapshotTests` | 上記の版照合で混在自体を弾く（開発ビルドは警告継続） |
| Validation の重さ | 新 Error で持ち込み先の CI が突然 fail | `ValidatorSeverityRegistryTests`（Error 昇格は CHANGELOG 必須） | 手順 5-4 |
| Editor 契約 | メニューが消えた・`[DataEditor]` 対応表が変わった | `EditorContractSnapshotTests`（メニューパス定数、DataEditor 対応表） | 人の確認（[37] 形式のチェックリスト） |
| 依存パッケージ | NGO/URP/Addressables の版差 | `package.json` と manifest の一致テスト | `ProjectSetupValidator`（版の一致を Error/Warning） |
| SpecWeb | JSON 形式・API 変更で同期が壊れる | SpecWeb 側 Node テスト + `Specs/*.json` のスキーマスナップショット | 同期ウィンドウのエラー表示（`apiVersion` 不一致を明示。§5.9） |

## 5. 互換性ポリシー（P チケット完了後に発効）

### 5.0 原則

1. **互換面（Compatibility Surface）に含まれるものは、下表の「禁止」を行わない。行うなら §5.12 の手続き + MAJOR**
2. **判定はレビュアーの心がけではなく §5.11 のテストが行う**。テストが赤なら PR はマージしない（CLAUDE.md §3-3 の「EditMode/PlayMode green」に含める）
3. **「追加」は常に安全側の既定値で**（[12] §3 Data/シリアライズ「デフォルト値が安全側」）。追加した瞬間から互換面に入る（後から消せない）ので、追加前に名前・型を吟味する（CLAUDE.md §0-9「迷ったら聞く」はこの意味で維持）
4. 互換面の外（Editor ウィンドウの見た目、内部クラス、`internal` メンバ、テストコード、docs）は自由に変えてよい。**`public` にする＝互換面に入れる**と理解する。互換面に入れたくないものは `internal` にする（既存: `AssetIdGenerator.ToConstantName` は `internal`。ただし**出力**は互換面 §5.7）

互換面の一覧: §5.1 シリアライズ形式 / §5.2 シリアライズされる enum / §5.3 ID・Address・定数名 / §5.4 公開 API / §5.5（予約: Handle の意味論、§5.4 に含む）/ §5.6 ContentHash / §5.6 ネットメッセージ / §5.7 生成コード / §5.8 Validation の重さ / §5.9 Editor 契約・SpecWeb / §5.10 依存とバージョン。

### 5.1 シリアライズ互換（`.asset` / `.prefab` / `.unity` に書かれるもの）

対象: `AssetDataBase` と全派生 Data、`AssetFlags`・`ValueDef`・`TimeDef`・`AssetEvent`・`AnchorDef`・`PresentationTrack` 等の `[Serializable]` struct/class、`AssetId<T>`、`CatalogEntry`/`AssetCatalog`、`TuningTable`、`UiLayerSettings`、`DDriveSpecSettings`、`ScenePreloadList`、`*ImportProfile`、シーンに置く MonoBehaviour（`DDriveRuntimeBootstrap`、`SeEmitter`、`AnchorRig`、UI コントロール等）の `[SerializeField]`。

| 変更 | 可否 | 条件・手順 |
|---|---|---|
| フィールド追加 | **許可（MINOR）** | 既定値が安全側（0/false/null/空で「今までどおり」の挙動になること）。`[Tooltip]` 必須（[10] §3.5）。`SerializedLayoutSnapshotTests` のスナップショットを同 PR で更新 |
| フィールド改名 | **条件付き（MINOR）** | `[FormerlySerializedAs("旧名")]` を付け、**次の MAJOR まで外さない**。プロパティ名（C#）の改名は §5.4 の公開 API 規則も同時に適用 |
| 型変更（`float`→`ValueDef`、`ulong`→`AssetId<T>` 等） | **条件付き（MINOR、2 段階）** | 旧フィールドを残し（`[HideInInspector] [Obsolete]`）新フィールドを追加、`IDataMigration` で値を移す。コードは新フィールドを読む。旧フィールドの削除は次の MAJOR |
| フィールド削除 | **禁止**（MAJOR でのみ、§5.12） | 同一 MAJOR 内では削除しない。使わなくなったら `[HideInInspector] [Obsolete]` で残す（実例: `DDriveSpecSettings` の旧 3 フィールド） |
| `[Serializable]` struct のフィールド順変更 | **禁止** | YAML は名前で復元するが、バイナリ（ビルド・AssetBundle）は順序依存。安全側で禁止 |
| クラス名・名前空間の変更（SO / MonoBehaviour） | **条件付き（MINOR）** | `[MovedFrom(true, "旧名前空間", "旧アセンブリ", "旧クラス名")]` 必須。asmdef（アセンブリ名）の変更は **禁止**（`.asset` の `m_Script` は GUID だが、`MovedFrom` は旧アセンブリ名を要求し、`typeof(T).FullName` をキーにする箇所（§5.6）が壊れる） |
| `AssetDataBase` の基底へのフィールド追加 | **許可（MINOR）** | 全種別に効くので影響を CHANGELOG に明記 |
| `SchemaVersion` の意味変更 | **禁止** | 単調増加のみ |

備考:
- `VfxData` の layer index、`PrefabData.CollisionLayer` は**プロジェクト固有の数値**。プロジェクト間で Data を移す用途は非対応（サンプルはこの値に依存しないように作る）
- `ImportSourceGuid`（`AssetDataBase`）は GUID 文字列 → 同一プロジェクト内でのみ意味を持つ
- テスト: `SerializedLayoutSnapshotTests`（`SerializedObject` を型ごとに走査し `propertyPath : propertyType` を列挙 → `Tests/Editor/Snapshots/serialized-layout.txt` と比較。**行の削除・型変更は fail、追加はスナップショット更新で通す**）、`LegacyAssetFixtureTests`（`Tests/Editor/Fixtures/v1_0_0/*.asset`（Unity で作った旧版のアセットをコミット。テキスト編集はしない）を `LoadAssetAtPath` し、期待値と `Unknown field` 警告 0 件を assert）

### 5.2 シリアライズされる enum（`AssetType` を含む全部）

対象: `AssetType`（`byte`、`.asset` の `type`/`Type` に**整数**で永続化）、`AssetFlags` 内の `PauseMode`/`LoadMode`/`PoolPolicy`/`AssetDomain`/`NetMode`、`ValueDef` の種別、`PresentationTrack.Kind`、`LifeMode`、`RenderMode`、`AnchorGroup` の配置種別 等、`[Serializable]` 型のフィールドに現れる全 enum。

| 変更 | 可否 | 何が壊れるか |
|---|---|---|
| 末尾に追加 | **許可（MINOR）** | – （既存ルール。[new-asset-type-checklist] §1）。`AssetType` は `byte` なので **最大 255 個**（現在 18） |
| 途中に挿入・並べ替え | **禁止** | 以降の値がズレる → 既存 `.asset` の Se が Bgm になる等、**種別の取り違え**。`CatalogEntry.Type`・`AssetId<T>.type` も同時にズレ、`IsRegistered(id, type)` の受信検証（6-6）が正当なメッセージを破棄する |
| 値の削除 | **禁止** | 削除した値を持つ `.asset` が「未定義の整数」になり Inspector で表示不能、`switch` の default に落ちる。**廃止は `[Obsolete] Reserved_旧名 = 旧値` として値を残す** |
| 値の改名 | **条件付き（MINOR）** | 数値は不変なので `.asset` は壊れないが、`Enum.Parse` している箇所（SpecWeb 同期の `assetType: "Anim2D"` 文字列、[31] A4）と生成コード（`AssetType.Se` を出力）が壊れる → 改名は公開 API の改名扱い（§5.4）。旧名を `[Obsolete]` エイリアスで残す |
| 基底型の変更（`byte`→`int`） | **禁止** | バイナリ形式が変わる。255 で足りなくなった時点で MAJOR |

テスト: `SerializedEnumSnapshotTests`（対象 enum を反射で列挙し `名前=値` を `Tests/Editor/Snapshots/enums.txt` と比較。削除・値変更は fail）。

### 5.3 AssetId / Address / 定数名の互換

| 要素 | 規則 | 変えると何が壊れるか |
|---|---|---|
| ulong ID の導出 `StableHashFromGuid` | **算法固定**（テストにゴールデン値） | 再生成で全 ID が変わる → 全 `.asset` の `Id`・カタログ・ゲームコードの定数値・ネット越しの ID・保存済みデータが全部不一致 |
| ID の不変性 | リネーム・カテゴリ変更・フォルダ移動で不変（既存規約） | – |
| Address（Addressables） | `= カタログの Address = 規約ファイル名`（`AssetNamingService` が生成）。**既存種別の接頭辞・フォルダ規則は固定** | 規則変更 → ファイル追従リネーム → Address 変更 → ContentHash 変更・Addressables 再ビルド。旧 Address を知っている外部（Remote カタログ運用時のクライアント）が解決不能 |
| 定数クラス名（`SEID` 等） | `[AssetIdDefinition]` の第 3 引数は固定 | 持ち込み先コードの `SEID.X` がコンパイルエラー |
| 定数名の導出 `ToConstantName` + `KnownPrefixes` | **発効後は規則固定**。`KnownPrefixes` への**追加も禁止**（追加すると `MODELID.MODELPlayerModel` が `MODELID.PlayerModel` に変わる = 破壊）。**発効前の一度きりの例外として `MODEL`/`ANC`/`ANCG`/`SKIN` を追加する（§5.13、§7 A-6、2026-09-17 決定）。この例外は 1.0.0 発効前のみで、発効後は本行のとおり凍結する** | 同上 |
| `DDrive.Generated` 名前空間・`TUNING`/`TUNING_TABLE`/`TUNING_COLUMN` クラス名・キー→定数名の変換 | 固定 | 同上 |
| 種別→カタログ名マッピング（`AssetCreationService`） | 既存種別は固定 | カタログが分裂し、ラベル `DDriveCatalog` 経由の収集は動くが Bootstrap の `Catalogs[]` 直参照が古いまま |

テスト: `IdHashGoldenTests`（固定 GUID → 固定 ulong を 3 例）、`ConstantNameGoldenTests`（`ToConstantName` の入出力 20 例、`KnownPrefixes` のスナップショット）、`CodegenGoldenTests`（§5.7）。

### 5.4 公開 API の互換（持ち込み先のゲームコードが直接呼ぶ層）

対象アセンブリ: `DDrive.Foundation`・`DDrive.Runtime` の `public` 型・メンバ全部。特に:

- 静的ファサード（`Bind` を持つ 17 クラス、確認済み）: `Audio` / `Vfx` / `Anim` / `Anim2D` / `Models` / `Mats` / `Prefabs` / `Ui` / `UiFx` / `UiSkins` / `CameraFx` / `Haptics` / `Presentation` / `Tuning` / `ScenePreload` / `OptionStore` / `AnchorGroupPlayer`（`Anchors`）
- `Handle<T>`、`AssetId<T>`、`PlayContext`、`AssetFlags`、`ValueDef`/`TimeDef`、`IAssetManager`、`IAssetRegistry`、`INetBridge`/`INetMessage`/`ITimeSource`、`AssetDataBase`（派生を持ち込み先が作ることは想定しないが `public`）、`DDriveRuntimeBootstrap` の `[SerializeField]`/public プロパティ、`GameLoopDriver`
- `DDrive.Editor` の `public` は**互換面に含めない**（持ち込み先は参照禁止）。ただし §5.9 の Editor 契約は別

| 変更 | 可否 | 条件 |
|---|---|---|
| 型・メンバの追加、オーバーロード追加 | **許可（MINOR）** | 既存呼び出しの解決が変わらないこと（省略可能引数の追加は既存バイナリ互換を壊すが、ソース互換のみ保証すればよい: 持ち込み先は常にソースから再コンパイル） |
| 既定引数の追加 | 許可（MINOR） | 同上 |
| メンバの削除・改名・シグネチャ変更・戻り値変更 | **禁止**（MAJOR でのみ） | 先に **`[Obsolete("代替: X。vN.0 で削除", false)]` を付け、少なくとも 2 回の MINOR リリースを挟む**（猶予 2 MINOR、§7 A-5）。削除は次の MAJOR。`error: true` にしない（持ち込み先の CI を止めないため） |
| 型の名前空間移動 | 禁止（MAJOR） | 旧名前空間に `[Obsolete]` な派生/エイリアスを残せる場合は条件付き MINOR |
| asmdef 名・分割（`DDrive.Runtime` を `DDrive.Runtime.Audio` に割る等、[01] §4 の余地） | **禁止（MAJOR）** | 持ち込み先の asmdef `references` が壊れる |
| 挙動の互換 | 「Bind 前・未登録 ID・null ctx で**例外を出さず no-op/Placeholder**」（CLAUDE.md §0-4）は API 契約の一部。破ると MAJOR | `Handle` の世代チェック（破棄後アクセスが false/no-op）も同じ |
| `[Obsolete]` の猶予 | **2 MINOR**（付与から少なくとも 2 回の MINOR リリースを経てから、次の MAJOR で削除できる。2026-09-17 決定、§7 A-5。MAJOR は年 1 回まで） | – |

テスト: `PublicApiSnapshotTests`（`DDrive.Foundation`/`DDrive.Runtime` の public 型・メンバのシグネチャを反射で文字列化 → `Tests/Editor/Snapshots/public-api-{asm}.txt` と比較。**削除・変更行は fail、`[Obsolete]` 付与は許可、追加はスナップショット更新**。`DataEditorRegistryTests` と同じ「反射で列挙して対応表と突き合わせる」流儀）。

### 5.5 Handle・Placeholder の意味論

§5.4 に含む。追記: `Placeholder` の見た目（ピンクの箱・無音等）は互換面に含めない。`Placeholder` が返る**条件**（未登録 / IsReady 前 / 型不一致）は含める。

### 5.6 ContentHash とネットメッセージの互換

**ContentHash**（`CatalogContentHasher`）:

| 変更 | 可否 | 影響 |
|---|---|---|
| 算法・定数（FNV basis/prime）・mix 順・対象フィールド（Id/Type/Address/Net） | **禁止（MAJOR）** | Host/Client の版が違うと全員不一致。**同版どうしなら GameData が同じ限り一致**する性質を守る |
| 対象フィールドの追加（例: `AssetDataBase.Version` を混ぜる、[14] §7 の要判断） | 禁止（MAJOR） | 同上 |

テスト: `CatalogContentHasherGoldenTests`（固定 Entry 列 → 固定 ulong。既存 `CatalogContentHasherTests` に追加）。

**版の照合（P-8 で追加）**: `CatalogContentHashMsg` に `PackageVersion`（string）・`ProtocolVersion`（int、初期値 1）を**追加**。Host の判定順: (1) `ProtocolVersion` 不一致 → 「D-Drive の版が違う」理由で `CatalogContentHashPolicy` の不一致分岐（開発は警告継続・リリースは切断、6-5 の決定を流用）。旧版 Client（フィールド無し → 0）も同じ扱い。(2) 一致なら従来どおりハッシュ比較。`ProtocolVersion` を上げるのは §5.6 のメッセージ互換を破る MAJOR のときだけ。`PackageVersion` は表示専用（`NetDebugOverlay` に出す）。

**ネットメッセージ**（`Runtime/Net/*Messages.cs`、`INetMessage` 実装 struct。`JsonUtility` + `typeof(T).FullName` キー）:

| 変更 | 可否 | 何が壊れるか |
|---|---|---|
| 新しいメッセージ型の追加 | **許可（MINOR）** | 旧版は未知キーとして無視（`NgoNetBridge` の型解決に無いため）。**新メッセージに依存する機能は旧版相手には効かない**ことを CHANGELOG に書く |
| 既存メッセージへのフィールド追加 | 許可（MINOR） | JsonUtility は欠落フィールドを既定値にする。既定値で旧挙動になること |
| メッセージ struct の改名・名前空間/アセンブリ移動 | **禁止** | キー `FullName` が変わる → 旧版と一切通じない |
| フィールドの削除・改名・型変更 | 禁止 | 旧版からの値が捨てられる/既定値になる（静かに壊れる） |
| `NetChannel`（Reliable/Unreliable）の変更 | 禁止 | 到達性の前提が変わる |
| 直列化方式の変更（JsonUtility → 別方式） | 禁止（MAJOR + `ProtocolVersion`++） | – |

テスト: `NetMessageSnapshotTests`（`INetMessage` 実装型の `FullName` と `[Serializable]` フィールド一覧をスナップショット比較）。

### 5.7 生成コード（`AssetIds.g.cs` / `Tuning.g.cs`）の互換

生成コードは**持ち込み先で再生成される**ので、ジェネレータの出力形式そのものが公開 API。

| 変更 | 可否 |
|---|---|
| 定数の追加（Data が増えた） | 持ち込み先のデータ次第（D-Drive の変更ではない） |
| クラス名・名前空間・定数名の導出規則・型（`AssetId<XxxMarker>`）・`static readonly` の形 | **固定**（変更は MAJOR） |
| ヘッダコメント・並び順・空行 | 自由（ただし並び順は `git diff` ノイズになるので固定推奨） |
| `KnownPrefixes` の追加 | **禁止**（§5.3） |
| 新 AssetType の定数クラス追加 | 許可（MINOR） |
| 出力先（`Assets/Generated/…`） | 既定値固定。設定で変更可（§3.4） |
| `DDrive.Generated.asmdef` の出力 | **既定 ON**（2026-09-17 決定、§7 A-8。1.0.0 の初期状態としてこの既定で発効する）。この既定を変えるのは MAJOR |

テスト: `CodegenGoldenTests`（`AssetIdGeneratorTests` の `includeTestAssemblies: true` 経路で固定フィクスチャ（テスト用 Data 型 + 固定 GUID）から生成した全文を `Tests/Editor/Snapshots/AssetIds.golden.cs` と比較。`TuningCodegen` も同様）。

### 5.8 Validation の重さ（Error/Warning）の互換

持ち込み先の CI は `CI.ValidateAll` を「Error があれば fail」で使う（[33]）。**新しい Error を追加すると、持ち込み先は更新しただけで CI が落ちる**。

- 新しい検査は **Warning として MINOR で追加**し、**次の MINOR 以降で Error に昇格**する（2 段階）。昇格は CHANGELOG の「互換性」節に「Error 昇格: XxxValidator/検査名」と明記
- 緊急（データ破損を招く等）で最初から Error にしたい場合は §5.12 の手続き（MAJOR ではないが、CHANGELOG + 移行ガイド + 昇格理由）
- 既存 Error → Warning への引き下げ・削除は自由（PATCH）
- テスト: `ValidatorSeverityRegistryTests`（各 `IValidator` が出し得る（検査 ID, 重さ）をスナップショット。Warning→Error の変化は「CHANGELOG に該当行がある」ことをガードスクリプトが確認）。検査 ID は `ValidationResult` に安定した `Code`（例: `DD-VFX-003`）を持たせる（追加フィールド。P-3）

### 5.9 Editor 契約・SpecWeb（弱い互換面）

持ち込み先のコードが依存しないが、**人の手順・マニュアル・スクリーンショット・CI スクリプト**が依存するもの。破っても MAJOR にはしないが CHANGELOG 必須。

- `DDriveMenu` の定数値（メニューパス）、`-executeMethod` のエントリ（`DDrive.Editor.CI.ValidateAll` / `RegenerateIds` / `MigrateCheck`）、`-ddriveOutput` 等のコマンドライン引数、コマンドライン `-ddrive-net`（[29]）: 改名は旧名を 1 MINOR 残す
- `[DataEditor]` 対応表、`AssetNamingService` の接頭辞/フォルダ（既存種別は §5.3 で固定）、`ImportRule` の監視フォルダ名（`SourceAssets/<種別>/`）、JUnit XML の形式
- SpecWeb: `Specs/assets.json` / `tuning.json` の形式と GAS API（`?api=1`）は**追加のみ**。レスポンスに `apiVersion` を追加し、D-Drive 側は不一致を同期ウィンドウに明示（P-10、§7 B-3）。GAS 側の版は D-Drive のタグと同じ番号を使う（別々に採番しない）
- テスト: `EditorContractSnapshotTests`（`DDriveMenu` の public const、`CI` の public static メソッド名、`[DataEditor]` 対応表）

### 5.10 依存パッケージと Unity バージョン

| 変更 | 区分 | 条件 |
|---|---|---|
| Unity パッチ版（6000.3.13 → 6000.3.x） | PATCH | CHANGELOG「動作確認版」を更新 |
| Unity マイナー版（6000.3 → 6000.4）で `package.json` の `unity` を上げる | **MINOR + CHANGELOG「要 Unity 6000.4」** | 持ち込み先が追従できない場合は前の MINOR に留まれる |
| Unity メジャー版 | MAJOR | – |
| 依存の PATCH/MINOR（URP 17.3 → 17.4 等） | MINOR + 明記 | 依存側の破壊が無いことを確認 |
| NGO の版 | MINOR + **太字で明記**（Host/Client 全員同時） | [14] §12 |
| 依存の追加 | MINOR + 明記（ウィザードの検査にも追加） | git 配布なら手順書更新 |
| **既存依存の参照範囲の拡大**（Editor asmdef のみ → Runtime asmdef も参照。2026-09-18 追加） | **MINOR + 明記**（「依存の追加」と同じ扱い。CHANGELOG に「vX から `<パッケージ>` はゲーム実行時にも必須」と書く） | 持ち込み先にとっては「Editor が動く条件」から「ゲームが動く条件」への格上げ。初出は 6-10a の URP + Timeline（[26] §7.1-10。P 発効前なので区分の適用対象外、記録のみ） |
| 依存の削除 | PATCH | – |

### 5.11 機械的な検査（CI で互換性を守る）

すべて `Assets/DDrive/Tests/Editor/`（パッケージ化後は `Tests/Editor/`）の EditMode テスト。**スナップショットは `Tests/Editor/Snapshots/*.txt` にコミット**し、意図した変更のときだけ同 PR で更新する（既存の `DataEditorRegistryTests`・`AssetIdGeneratorTests`・`CIJUnitXmlTests` と同じ「反射/生成 → 期待と比較」の流儀。フィクスチャで実 GameData を触らない）。

| # | テスト | 守る互換面 | 判定 |
|---|---|---|---|
| 1 | `PublicApiSnapshotTests` | §5.4 | 削除・変更 = fail。追加 = スナップショット更新 |
| 2 | `SerializedLayoutSnapshotTests` + `LegacyAssetFixtureTests` | §5.1 | 削除・型変更 = fail。旧版フィクスチャが警告 0 で読める |
| 3 | `SerializedEnumSnapshotTests` | §5.2 | 削除・値変更 = fail |
| 4 | `IdHashGoldenTests` / `ConstantNameGoldenTests` | §5.3 | ゴールデン値不一致 = fail |
| 5 | `CatalogContentHasherGoldenTests` | §5.6 | 同上 |
| 6 | `NetMessageSnapshotTests` | §5.6 | 改名・削除 = fail |
| 7 | `CodegenGoldenTests` | §5.7 | 全文不一致 = fail |
| 8 | `ValidatorSeverityRegistryTests` + `EditorContractSnapshotTests` | §5.8 / §5.9 | Error 昇格・改名は CHANGELOG 行必須 |
| 9 | `PackageVersionConsistencyTests` | §4.1 | `package.json` ↔ `DDriveVersion.Value` ↔ 直近 CHANGELOG 見出し ↔ manifest の依存版 |
| 10 | CHANGELOG ガード（`Tools/CI/Check-Changelog.ps1`。`run-ci.cmd` と `ci.yml` に 1 段追加） | 全部 | `Tests/Editor/Snapshots/**` のいずれかが変わった PR で `CHANGELOG.md` が変わっていなければ fail。`version` が上がっていなければ fail |
| 11 | 消費側スモーク（`Tools/CI/run-consumer-smoke.cmd`、P-11） | 導入手順全体 | 空プロジェクトを `Unity -createProject` で作り、manifest にローカルパス（`file:` で開発リポジトリの `Packages/com.ddrive.core`）+ 依存を書き、`DDrive.Editor.CI.ConsumerSmoke`（Addressables 初期化 → ウィザード相当 → SeData を 1 件 `AssetCreationService.Create` → カタログ・Addressables 登録・ID 再生成・`ValidateAll` Error 0）を実行 |

### 5.12 破壊的変更をどうしても行う場合の手続き

1. **issue/設計メモ**に「何を・なぜ・代替案（2 段階で回避できないか）」を書き、ユーザー承認（CLAUDE.md §0-9）
2. 少なくとも 2 回の MINOR で `[Obsolete]`・Warning・移行ツール（`IDataMigration`/`IProjectMigration`）を**先に出す**（猶予 2 MINOR、§7 A-5。持ち込み先が MAJOR 前に準備できる）
3. MAJOR で削除。`CHANGELOG.md` に「破壊あり」+ `docs/migrations/vN.md`（移行ガイド: 対象・症状・手順・ロールバック可否）
4. スナップショットを更新（削除行が消える）。`ProtocolVersion` を上げる（ネット互換を破った場合）
5. 持ち込み先（MS2026）で §4.2 を実施し、結果を移行ガイドに追記
6. **MAJOR の頻度上限**: 年 1 回まで（2026-09-17 決定、§7 A-5）。MS2026 の開発フェーズ中は 0 回

### 5.13 発効前に片付ける「最後のチャンス」リスト

互換性ポリシーが発効すると直せなくなる既知の不整合。**P-1/P-2 で採否を決め、P-5 のパッケージ化（= 1.0.0）より前にやる**（§7 A-6）:

- `KnownPrefixes` に `MODEL`/`ANC`/`ANCG`/`SKIN`/`CUT`（6-10a）が無く、定数名に接頭辞が残る（`MODELID.MODELPlayerModel`、`ANCHORID.ANCAnimJump`、`SKINID.SKINButtonSkin`）。**決定: やる（2026-09-17、§7 A-6）**。少なくとも `MODEL`/`ANC`/`ANCG`/`SKIN` を追加する。これらの接頭辞除去で定数名が変わる `AssetIdDefinition` 定義例: `MODELID.MODELPlayerModel`、`SKINID.SKINButtonSkin`、`ANCHORID.ANCAnimJump`、`ANCHORGROUPID.ANCG1PlayerSlash`、`SLIDERSKINID.SKINSkiderTest`、`ANCHORID.ANCPlayerVFXPlayerSlashAnchor` 等の重複接頭辞を解消する。調査の結果、これらの生成定数を参照しているコードは Samples を含め 0 件であり、今なら実害なく変更できる（この根拠を裏付けに P-1 で実施する）
- `DDriveSpecSettings` の旧フィールド 3 つ（`SpreadsheetUrl`/`AssetSheetName`/`TuningSheetName`、[32] 要判断）の削除
- `SliderSkinData.NotchHapticId`/`LimitHapticId` の `ulong` → `AssetId<HapticMarker>`（[31] A2 は「ulong のまま」と決定済み。据え置きなら発効後もそのまま）
- [25] の P3 後半の整理項目のうち API に触るもの
- `com.cysharp.unitask` のタグ固定（§2.3-8）
- CLAUDE.md §1 の「NGO 2.2」表記の修正（実体は 2.13.2。docs の誤記なので互換性とは無関係だが P-1 で直す）

## 6. チケット分割（P-1〜P-13。[11_tasks.md] に同じ表を追記）

番号体系は `U-*`（[39]）・`W-*`/`O-*`（[32]）に倣い **`P-*`（Portability）**。Phase 7 の番号は使わない。着手は **6-10d の後**。担当・日数・依存・AC の列は既存表と同じ。

| # | チケット | 担当 | 日数 | 依存 | AC |
|---|---|---|---|---|---|
| P-1 | **線引きの確定**: §2.1 の分類表を確定（§7 A-1/A-2/A-7 の回答反映）、§2.3 の境界違反 10 件と §5.13 の「最後のチャンス」リストの採否を決める。[14] §12 の「コピーして持ち込む」段落を UPM 参照へ改訂。CLAUDE.md §1 の NGO 表記修正 | 基盤+リード | 1 | 6-10d | 分類表に「?」が残っていない。§5.13 の各項目に「やる/やらない」が付いている |
| P-2 | **互換性ポリシーの確定**: §5 を確定（§7 A-3〜A-6）。[12] §3 に「互換性」チェック節を追加（草案）。`CHANGELOG.md` の書式・`docs/migrations/` の雛形を作る。**この時点では発効しない**（発効は P-13） | 基盤+リード | 1 | 6-10d | §5 の各表に「要判断」が残っていない。[12] §3 草案がある |
| P-3 | **互換性スナップショットテスト群**（§5.11 の 1〜10）: 公開 API / シリアライズ形式 + 旧版フィクスチャ / enum / ID・定数名・ContentHash ゴールデン / Net メッセージ / Codegen ゴールデン / Validator 重さ + Editor 契約 / 版一致 / CHANGELOG ガード。`ValidationResult.Code` の追加。**パッケージ化（P-5）より先に作り、移設で壊れないことをこのテストで確認する** | 基盤 | 3 | P-2 | 全テストが現状で green。意図的に public メンバを 1 つ消す/enum を並べ替える/`KnownPrefixes` に追加する、のそれぞれで fail することを確認 |
| P-4 | **境界違反の解消**（§2.3 #1〜#6、#8）: shader をパッケージ側へ移設、`CI.cs` の走査ルート、`ManualPages` のパス、`ControlSkinPreviewSection` のパス、`CodeReferenceScan`/`SpecWebSender` の走査範囲を Assets 全体へ、テストのフィクスチャ化（UnityChan 依存の除去）、UniTask のタグ固定 | 基盤+ED | 2 | P-1 | `Samples~` 未 import・`SourceAssets` 空でも EditMode/PlayMode が green。`ForbiddenApiScanner` が走査 0 ファイルのとき Error を出す |
| P-5 | **パッケージ化**: `Assets/DDrive/` → `Packages/com.ddrive.core/`（.meta ごと移動。Unity Editor 経由）、`package.json`（`name: "com.ddrive.core"`、`displayName: "D-Drive"`、`version: "1.0.0"`、§3.5 の依存、`unity: 6000.3`、`samples`）、`Samples~`（Demo/NetCheck + 最小データ）、`Documentation~`（DesignerManual、AGENTS_CONSUMER 雛形）、開発 manifest に `testables`、`DDriveVersion.cs`、`DDriveProjectSettings`（`ScriptableSingleton`、出力先パス設定、`IsDevelopmentRepo`）、`Regenerate` の `DDrive.Generated.asmdef` 出力オプション、出力先の設定化（§3.4） | 基盤 | 3 | P-3, P-4 | 開発リポジトリで EditMode/PlayMode/Performance が green、`run-ci.cmd` が green、P-3 のスナップショットに差分が無い（= 移設で公開 API・生成物が変わっていない）。`Package Manager` に D-Drive が表示され Samples を import できる |
| P-6 | **セットアップウィザード + ProjectSetupValidator**（§3.6）: 依存（manifest 検査、git 依存の `Client.Add`、scoped registry の検出/案内）→ URP/Input/API Level の検査 → Addressables 初期化 → `GameData`/`SourceAssets` 既定フォルダ・`UiLayerSettings`・`DDriveSpecSettings`・カタログの生成 → 起動オブジェクト配置。同じ検査を `IValidator` として `Run All` に | ED | 3 | P-5 | 空プロジェクト + manifest 1 行から、ウィザードの「すべて直す」だけで `Validation > Run All` Error 0 になる（人手はメニュー操作のみ） |
| P-7 | **スキーマ版 + マイグレーション基盤**（§4.3）: `AssetDataBase.SchemaVersion`（A-4 承認済み、2026-09-17）、`VersionStampProcessor` での書き込み、`IDataMigration`/`IProjectMigration`（TypeCache 自動発見）、`DDriveMigrationRunner`（ドライラン・Undo・`VersionStampSuppression`）、`CI.MigrateCheck`、「SchemaVersion が古い」Validator、旧版フィクスチャでの往復テスト | 基盤 | 2 | P-2, P-5 | ダミーのマイグレーション（テスト内）が対象だけに 1 回だけ適用され、Undo で戻る。`MigrateCheck` が未適用ありで exit 1 |
| P-8 | **更新ツール + 版の照合**（§4.2 手順 5、§5.6）: `Tools > D-Drive > Update` ウィンドウ（前回版/現在版/CHANGELOG 表示、「更新を適用」= Migrate → Regenerate IDs/Tuning → Addressables 同期 → Run All → `LastAppliedVersion` 更新）。`CatalogContentHashMsg` に `PackageVersion`/`ProtocolVersion` を追加し `CatalogContentHashGate` で先に照合、`NetDebugOverlay` に表示 | 基盤+ED | 2 | P-6, P-7 | 版を進めた直後に「更新を適用」1 回で Error 0。Host/Client の `ProtocolVersion` が違うと [14] §7 の方針（開発は警告・リリースは切断）で「D-Drive の版が違う」理由が表示される（`CatalogContentHashGateTests` に追加） |
| P-9 | **リリース手順の道具化**（§4.1）: `Tools/Release/bump-version.ps1`（`package.json`・`DDriveVersion.cs`・CHANGELOG 見出し・git tag を一括）、リリースチェックリスト（[12] に節追加: スナップショット差分の確認 → CHANGELOG「互換性」節 → `run-ci.cmd` → タグ）、`[Obsolete]` 棚卸し一覧（次 MAJOR で消すものを `docs/migrations/next-major.md` に自動列挙） | 基盤 | 1 | P-2 | 手順どおりに `v1.0.0` タグが切れ、`PackageVersionConsistencyTests` が green |
| P-10 | **消費側ドキュメント**: パッケージ `README.md`（導入 5 ステップ、依存表、既知の制約: URP のみ・NGO 必須）、`Documentation~/AGENTS_CONSUMER.md`（持ち込み先のエージェント向け規約。任意で `ddrive-consumer` スキル、要判断 B-4）、`Tools~/CI/` テンプレ（要判断 B-5）、SpecWeb の持ち込み先デプロイ手順（要判断 B-3）と `apiVersion`、[34] に「持ち込み先での始め方」節、DesignerManual に「パッケージを更新する」ページ | 全員 | 2 | P-5, P-6 | 新メンバーが README だけで空プロジェクトに導入し SE を 1 件鳴らせる（P-11 で実測） |
| P-11 | **持ち込み先で実際に動くことの確認**（独立チケット）: 空の Unity プロジェクト（`-createProject`）に P-10 の手順で導入 → ウィザード → SE を 1 件登録（AssetBrowser 新規作成 → 音源割当 → 試聴 → Play Mode で `Audio.Play(SEID.X)`）→ `ValidateAll`/EditMode/PlayMode 相当が green → 版を 1 つ進めて P-8 の更新手順 → ロールバック（§4.4）。これを `Tools/CI/run-consumer-smoke.cmd` として自動化（§5.11-11）し `run-ci.cmd` の任意ステップに追加。手順・所要時間・詰まった点を [37] 形式で記録 | 基盤+ED | 2 | P-6, P-8, P-10 | 人手 15 分以内で SE 1 件が鳴る。スモークが `run-ci.cmd` から通る。更新→ロールバックで Data が壊れない |
| P-12 | **MS2026 への実移植**（[14] §12。要判断 B-7 で本タスクに含めるか決める）: MS2026 の manifest に追加 → ウィザード → 既存 CI に `ValidateAll`/`RegenerateIds` を組み込み → `Assets/_Project/Scripts` から `SEID` を参照（asmdef の有無で `DDrive.Generated.asmdef` を判断）→ NGO 実機 2 台で ContentHash/版照合を確認 | 基盤 | 2 | P-11 | MS2026 で SE/VFX が ID 経由で再生され、MS2026 の CI が green。判明した障害は本書 §2.3 に追記 |
| P-13 | **互換性ルールの発効**: CLAUDE.md §0 TL;DR に「互換性ポリシー（docs/42 §5）を守る。スナップショットテストが赤なら変更しない」を追加（§1 の予告を置き換え）、AGENTS.md・`ddrive-agent-workflow` スキル・[12] §3 を更新、本書の冒頭を「発効済み（日付）」に | 基盤 | 0.5 | P-11（P-12 を含める場合は P-12） | CLAUDE.md / AGENTS.md / SKILL.md / docs/12 の 4 箇所に同じルールが載っている |

合計: 約 24.5 人日（約 5 週。基盤 1 + ED 1 の並行で 3〜3.5 週）。P-1 と P-2 は並行可。P-3 は P-4/P-5 と並行できるが、**P-5（移設）は P-3 のテストが green になってから**行う。

## 7. 要判断（ユーザーに聞くこと）

[31] と同じ書式。**A は P-1/P-2 の着手前に回答が必要**。B は着手後・実装中に決めればよい。C は確認事項（設計には影響しない）。

### A. 着手前に決めたい（2026-09-17 ユーザー回答により決定済み）

| # | 内容 | 決定（2026-09-17） | 影響・根拠 |
|---|---|---|---|
| A-1 | **パッケージ名と配布リポジトリ**。名前は逆ドメイン形式が UPM の規約 | **(a) を採用**。パッケージ名は `com.ddrive.core`（displayName `D-Drive`）。配布は分離リポジトリを作らず、同一リポジトリ（`github.com/wrenchsun/D-Drive`）を git URL の `?path=` で参照する。開発は引き続きこのリポジトリ内の埋め込みパッケージ（`Packages/com.ddrive.core/`）で行う | 配布専用リポジトリへの subtree split（選択肢 b）は二重管理と同期 CI が増えるため不採用。名前は後から変えると全持ち込み先の manifest に影響するため、この時点で確定した |
| A-2 | **リポジトリの公開範囲と持ち込み先の認証**。private のままだと持ち込み先 PC の git 認証（SSH 鍵 / PAT）が Package Manager の resolve に必要 | **(a) を採用**。リポジトリは **private のまま**。持ち込み先（MS2026 等）からは `git+ssh` 形式の URL で参照する | CI マシン（MS2026 側）にも同じ SSH 認証が要る。[33] §3 のセキュリティ注意は将来 public 化するときに再確認する |
| A-3 | **最初の版番号** | **1.0.0**。**P-5（パッケージ化）で発効**する | 0.x は SemVer 上「壊してよい期間」を意味し、ユーザー要望「以降は互換性を持たせる」と矛盾するため 1.0.0 から始める |
| A-4 | **`AssetDataBase.SchemaVersion` の追加**（シリアライズ変更 = CLAUDE.md §0-9 の事前確認対象。[31] A1 と同じ「フィールド追加のみ」） | **(a) を採用（承認済み）**。`AssetDataBase` に `SchemaVersion` フィールドを追加する（§4.3）。既存の `Version` は「保存回数」であって schema 版ではないため、両者は別フィールドとして持つ | `DDriveProjectSettings.LastAppliedVersion` だけで管理する案（選択肢 b）は、更新が途中で中断した場合にどの Data が未移行か分からず全件再走査になるため不採用 |
| A-5 | **`[Obsolete]` の猶予と MAJOR の頻度** | **猶予は 2 MINOR**（付与から少なくとも 2 回の MINOR リリースを経てから、次の MAJOR で削除できる）。**MAJOR リリースは年 1 回まで**（MS2026 の開発フェーズ中は 0 回） | MAJOR 頻度の上限を設けない案（選択肢 c）は持ち込み先の更新コストが読めなくなるため不採用。1 人開発の現状に合わせた運用とする |
| A-6 | **発効前の「最後のチャンス」（§5.13）をやるか**。特に `KnownPrefixes` の不整合（`MODEL`/`ANC`/`ANCG`/`SKIN` が未登録で `MODELID.MODELPlayerModel` のような定数名になっている）は、発効後は直せない | **発効前（P-5 より前）に不整合を直す**。`KnownPrefixes`（`Assets/DDrive/Editor/Codegen/AssetIdGenerator.cs:227-230`、現在 SE/BGM/VFX/ANIM/ANIM2D/MAT/TEX/CANVAS/PREFAB/PRES/SHAKE/HAPTIC/HAPTICS/UITWEEN の 14 個）に **MODEL / ANC / ANCG / SKIN** を追加し、`MODELID.MODELPlayerModel` `SKINID.SKINButtonSkin` `ANCHORID.ANCAnimJump` `ANCHORGROUPID.ANCG1PlayerSlash` `SLIDERSKINID.SKINSkiderTest` `ANCHORID.ANCPlayerVFXPlayerSlashAnchor` のような重複接頭辞を解消する | 調査の結果、これらの生成定数を参照しているコードは Samples を含め **0 件** であり、今なら実害なく変更できる。この根拠に基づき P-1 で実施する（§5.13 に反映済み） |
| A-7 | **NGO を必須依存のままにするか** | **(b) を採用**。NGO は必須依存のままにせず、**`versionDefines` で切り離す**（`DDRIVE_NGO` シンボルを定義し、`NgoNetBridge`/`NetDebugOverlay`/Bootstrap 等の分岐を `#if DDRIVE_NGO` で囲む。P-4 で対応） | MS2026 自体は NGO を導入するため実質的な影響はないが、NGO 不要な将来の持ち込み先でもコンパイルできるようにする |
| A-8 | **`DDrive.Generated.asmdef` を出力するか**。MS2026 の `Assets/_Project/Scripts/` が asmdef を持つか未確認 | **(b) を採用**。出力する（**既定 ON**） | asmdef 付きのゲームコードから `SEID` を参照するには既定 ON が必要。既定を後から変えるのは MAJOR（§5.7） |
| A-9 | **持ち込み先での D-Drive 改造の扱い** | **(a) を採用（暫定どおり）**。原則禁止のまま。強制手段は **P-6 以降で Warning として実装**する（`ProjectSetupValidator` が `PackageInfo.source == Embedded` かつ `DDriveProjectSettings.IsDevelopmentRepo == false` を検知）。**`DDriveProjectSettings` / `IsDevelopmentRepo` は 2026-09-17 時点では未実装**（§4.5 の記述は設計のみ） | 埋め込み運用の正式サポート（選択肢 b）は「更新が取り込めない」事故の温床になり、ユーザー要望 2（更新の取り込み）と衝突するため不採用 |

### B. 実装中・使ってみて決める

| # | 内容 | 現在の暫定 | 選択肢 | 影響 |
|---|---|---|---|---|
| B-1 | URP 以外（Built-in / HDRP）の対応 | 非対応（`ProjectSetupValidator` が Error） | (a) 非対応 (b) Built-in も許容（shader が別途要る） | DDrive/Lit・Unlit・AiStandardSurface・UI Scroll は URP shader。(b) は Material/Texture 系の作り直し |
| B-2 | Unity バージョン方針 | `unity: 6000.3`。新しい版は禁止しないが保証しない | (a) 暫定どおり (b) 6000.3 完全固定（ウィザードが他版を Error） | – |
| B-3 | **SpecWeb の持ち込み先展開**: 持ち込み先ごとに別デプロイ（推奨）か共有か。GAS ソースをパッケージ（`SpecWeb~/`）に同梱するか、開発リポジトリのタグから clasp push してもらうか | 別デプロイ。ソースは開発リポジトリのタグから（同梱しない） | (a) 暫定どおり (b) 同梱 (c) 共有デプロイ（`id` にプロジェクト接頭辞を足す = SpecWeb 側の破壊的変更） | (c) は `assets.json` の `id` 形式変更と Web UI のプロジェクト切替が必要 |
| B-4 | 持ち込み先のエージェント向けドキュメントの形 | `Documentation~/AGENTS_CONSUMER.md`（Markdown 1 枚）。スキルは作らない | (a) Markdown のみ (b) `.claude/skills/ddrive-consumer` も同梱し持ち込み先にコピーしてもらう | – |
| B-5 | 持ち込み先 CI の提供形 | `Tools~/CI/` にテンプレ（`run-ci.cmd` 相当）+ README。MS2026 は既存 CI に `ValidateAll`/`RegenerateIds` を足す | (a) 暫定どおり (b) GitHub Actions の再利用可能ワークフローとして公開 | – |
| B-6 | `ImportRule` の監視ルート・`GameData` ルート・`Generated` 出力先を `DDriveProjectSettings` で変更可にするか | 変更可（既定値は現状） | (a) 変更可 (b) 固定 | MS2026 に既存のフォルダ規約がある場合 (a) が要る。変更可にすると docs/DesignerManual の記述に「既定では」が増える |
| B-7 | **P-12（MS2026 への実移植）を本タスクに含めるか** | 含める（P-11 の次） | (a) 含める (b) 別タスク | (b) でも P-11 のスモークまでは本タスクで行う |
| B-8 | Validation の新 Error の 2 段階ルール（§5.8）の採用 | 採用 | (a) 採用 (b) Error 追加は MINOR で可（持ち込み先の CI が落ちることを許容） | (b) は持ち込み先の更新が「まず CI を直す作業」から始まる |
| B-9 | サンプルの中身（`Samples~/Demo`）に UnityChan/shizuku を含めるか | 含めない（再配布条件があるため自作の最小アセットに差し替え） | (a) 含めない (b) ライセンス表記付きで含める | (a) は最小 FBX/音源の用意が要る（P-4 のフィクスチャと共用） |

### C. 確認事項（P-5/P-6 で実機確認する。設計には影響しない）

| # | 内容 | 影響する箇所 |
|---|---|---|
| C-1 | `UnityEditor.PackageManager.Client.AddScopedRegistry` が public API か（不可なら manifest.json の JSON 編集で代替） | P-6 ウィザードの scoped registry 追加 |
| C-2 | git URL パッケージの `Samples~` に含めた `.asset`（Data）の GUID が import 後も保たれ、`StableHashFromGuid` の ID が開発リポジトリと一致すること | `Samples~/Demo` の ID |
| C-3 | `Path.GetFullPath("Packages/com.ddrive.core/Documentation~/…")` が埋め込み・PackageCache の両方で解決できること | `ManualPages` |
| C-4 | `AssetIdGenerator` の `AssetSearch.FindAssets("t:XxxData")`（`Roots = {"Assets"}`）が `Packages/` 配下を見ないこと（見ると `Tests/` のダミー Data 型が拾われ得る。`includeTestAssemblies: false` で型は除外されるが、パッケージ内に置いたフィクスチャ `.asset` は要確認） | P-4 のフィクスチャ配置 |
| C-5 | `ForbiddenApiScanner` をパッケージパスで走らせたとき `Tests/`・`Samples~/` を含めるか（現状 `Assets/DDrive` 全体 = Tests/Samples 込み） | P-4 |
| C-6 | Addressables のグループ Schema 既定（`BundledAssetGroupSchema` + `ContentUpdateGroupSchema`、`AddressablesSync.cs:223`）が持ち込み先の Build/Load Path プロファイルで問題なく動くこと | P-6 |
| C-7 | UniTask の実際の固定コミット `ceac8d69…`（`packages-lock.json`）が属するリリースタグ | §2.3-8、P-4 |

## 8. 変更履歴

- 2026-09-17: 新規作成（設計のみ、実装なし）。ユーザー要望「タスクの追加、Timeline の後に行う。この環境を Unity の実際の作業環境に簡単に移植する、D-Drive の Update があったらほかの環境に取り込むことができる。これにより、この新規タスクの後はすべて互換性を持たせる必要があります」を受けて、§2 線引き / §3 配布方式（UPM git URL を推奨）/ §4 更新フロー（SemVer・スキーマ版・マイグレーション・ロールバック）/ §5 互換性ポリシー（9 互換面 + 機械検査 11 種）/ §6 P-1〜P-13 / §7 要判断 を記載。[11_tasks.md] に P チケット表、[README.md] に目次行、[CLAUDE.md] §1 に予告を追加。
- 2026-09-17（同日追記）: §7 A-1〜A-9 をユーザー回答により「決定」に更新（設計のみ、実装なし。ドキュメント編集のみで実施）。決定内容: A-1 パッケージ名 `com.ddrive.core`（displayName `D-Drive`）・分離リポジトリを作らず同一リポジトリを `?path=` で参照 / A-2 リポジトリは private のまま・`git+ssh` URL で参照 / A-3 最初の版は 1.0.0（P-5 で発効） / A-4 `AssetDataBase.SchemaVersion` の追加を承認（既存 `Version` は保存回数であり別物と明記） / A-5 `[Obsolete]` 猶予 2 MINOR・MAJOR は年 1 回まで / A-6 発効前（P-5 より前）に `KnownPrefixes` へ `MODEL`/`ANC`/`ANCG`/`SKIN` を追加して不整合を解消（参照コード 0 件を確認済み） / A-7 NGO は `versionDefines` で必須依存から切り離す / A-8 `DDrive.Generated.asmdef` の出力は既定 ON / A-9 持ち込み先での改造は原則禁止のまま、強制手段（Warning）は P-6 以降で実装（`DDriveProjectSettings`/`IsDevelopmentRepo` は現状未実装）。この決定に合わせて §0 いちばん厳しい制約・§2.1 分類表・§2.2 レイアウト例・§2.3 境界違反 5/7/9・§3.2 推奨・§3.5 依存表・§4.1 版・§4.3 SchemaVersion・§4.5 改造の扱い・§5.3/§5.4/§5.7/§5.12/§5.13・P-5/P-7 チケット本文を整合させた。あわせて §2.3 #5 の行番号誤記（`CodeReferenceScan.cs:9` → 実際は `ScanRoots` 定義の 32 行目）を訂正。
- 2026-09-18: Timeline（[26] §7.1-10）で `DDrive.Runtime` asmdef に `Unity.Timeline` + URP（Universal.Runtime / Core.Runtime）を直接追加することをユーザーが承認したことを受け、§3.4 に追記、§3.5 の URP / Timeline 行に「6-10 以降はランタイム必須依存」を明記、§5.10 に「既存依存の参照範囲の拡大（Editor → Runtime）= 依存の追加と同じ MINOR」の行を追加。B-1（URP 以外は非対応）は変更なし。ドキュメント編集のみ（asmdef の実変更は 6-10a）。
