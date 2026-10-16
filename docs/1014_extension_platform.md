# 1014. 外部拡張（プラグイン）基盤 — 持ち込み先が D-Drive の更新を待たずに種別・エディター・トラックを足せるようにする

> 2026-10-16 起票（ユーザー指示: 「このような外部拡張を D-Drive の更新を待たずにできるようにする〔プラグインのようなもの〕を導入先で作って簡単に対応できるようにする」）。きっかけは MS2026 からの依頼が続いたこと（2026-10-15 の `Ui.OpenInto` / `CutscenePlayOptions` = v1.9.0、2026-10-16 のステージ照明 = 種別 2 つ + エディター 3 窓 + Timeline トラック、[1004](1004_tasks.md) MS 節・L 節）。**目標: 「新しい種別（Data + Manager + ファサード + Validator + 専用エディター + Timeline トラック + サンプル）」を持ち込み先のアセンブリだけで作れる**。D-Drive 本体に手を入れるのは「D-Drive 自身の挙動の変更」（`Ui.OpenInto` のような既存 Manager の API）だけにする。チケットは [1004](1004_tasks.md) X 節。

## 1. いま何が足りないか（2026-10-16 時点の実態）

| 持ち込み先が作りたいもの | 今できるか | 足りない点 |
|---|---|---|
| `IValidator` の追加 | **できる**（[42](42_distribution.md) §5.14 E-6。`CI.DiscoverValidators()` が全アセンブリから拾う） | — |
| 外部の `IAssetManager` を Tick に乗せる | **できる**（E-9、`GameLoop.Register`） | Bootstrap の後に自分で `Register` する必要がある（起動順・Bind のタイミングを持ち込み先が面倒を見る）。`DDriveRuntimeBootstrap` には外部の口が無い（`loop.Register(Audio); …` の固定列） |
| Timeline の Track / Clip / Marker | **できる**（E-2 / E-5 / E-20、Unity 標準の拡張） | — |
| `[DataEditor]` 付きの Inspector ボタン、`DDriveMenu` 配下のメニュー | **できる**（`DataEditorAttribute` / `DDriveMenu` は public、`TypeCache` 発見） | — |
| MCP ツール `[McpTool]` | **できる**（isuzu の仕組み） | — |
| **新しい種別（`AssetType`）** | **できない** | `AssetType` は閉じた `byte` enum。`AssetIdDefinition` / `IAssetManager.Type` / `IValidator.Target` / `CatalogEntry.Type` / `AssetNamingService`（接頭辞・フォルダの `switch`）/ `AssetCreationService`（カタログ名、`Enum.GetValues` で種別一覧）/ AssetBrowser の種別フィルタ、がすべて enum 前提 |
| **D-Drive 流の専用エディター**（[1010](1010_editor_ux_migration.md) の新デザイン） | **できない** | `Editor/Ux` の全型が `internal`（`UxEditorWindow<T>` / `FieldGuide<T>` / `UxCard` / `UxRow` / `TuneKnobs` / `DurationBar` / `AnchorPad` / `DataSwitcher` / `UxTheme`）。持ち込み先は見た目を複製するしかない（MS2026 11 §7 が最初に書いた「Ux 基底を複製する回避策」） |
| 「サンプルを作る」・確認用シーン・マニュアル導線 | **できない** | 全部 D-Drive 内部の列挙（UX-19 の `Editor/Ux/Samples/`、`PreviewSceneSetup`、`ManualLauncher`） |
| ID 定数の生成（`XXID.Name`） | **できる** | `AssetIdGenerator.FindDefinitions` は全アセンブリの `[AssetIdDefinition]` を拾う。ただし `AssetType` が要る（上の行） |

つまり **足りないのは「種別」「エディター基盤」「サンプル / 確認用シーン / マニュアルの口」の 3 つ**。Manager・Validator・Timeline・メニュー・MCP はすでに開いている。

## 2. 設計（追加のみ、MINOR）

### 2.1 種別の外部定義（X-1）

- `AssetType` の**末尾に 1 値 `Extension`** を足す（enum はこれ以降増やさない。持ち込み先の種別はすべて `Extension` + **種別キー（string）**で区別する）。
- `AssetIdDefinitionAttribute` に省略可能な `KindKey`（例 `"LightCue"`）を足す（既存の 3 引数コンストラクタは残す）。`AssetType.Extension` のときは必須。
- 新しい拡張点 **`IAssetKindProvider`**（`DDrive.Foundation`、public、`TypeCache` 発見〔[42](42_distribution.md) §5.14 の発見規則どおり public・非 abstract・引数なしコンストラクタ〕）: `KindKey` / `DataType` / `DisplayName` / `Prefix`（`CUE`）/ `Folder`（`Assets/GameData/LightCue`）/ `CatalogName` / `DefaultLoad`（既定 Preload）/ `Icon`（任意）/ `Order`。
- D-Drive 内部で `AssetType` を `switch` している 4 か所（`AssetNamingService.GetTypePrefix` / `GetTargetFolder`、`AssetCreationService` のカタログ名と種別一覧、AssetBrowser の種別フィルタ、Validation の種別別の集計）は、`Extension` のときに `AssetKindRegistry`（provider の一覧）へ委譲する。
- `CatalogEntry` に `KindKey`（string、空 = 組み込み種別）を**追加**（シリアライズ形式の追加 = `serialized-layout.txt` 更新、MINOR）。`AssetRegistry` の種別別 index は `(AssetType, KindKey)` で引く。既存データは `KindKey` 空のまま読める。
- `IAssetManager.Type` が `Extension` の Manager は、追加のインターフェース `IExtensionAssetManager.KindKey` で種別キーを返す（既定実装なし。既存 Manager は触らない）。`IValidator` も同様に `IKindValidator.TargetKindKey`（任意）。
- ID 定数: `AssetIdGenerator` は `KindKey` ごとに `ConstantsClassName` を出す（今と同じ仕組み。`KnownPrefixes` は provider の `Prefix` も見る）。
- Validation の重さの互換（§5.8）: 外部の Validator の Code は持ち込み先の責任（`DD-` 接頭辞を使わない規約を docs に）。

### 2.2 起動配線の口（X-2）

- **`IDDriveModule`**（`DDrive.Runtime`、public、`TypeCache` 発見）: `int Order` / `IReadOnlyList<IAssetManager> CreateManagers(in DDriveBootstrapContext ctx)` / `void Bind()` / `void Unbind()`。`DDriveBootstrapContext` は Bootstrap が持つ `IAssetRegistry` / `IPoolService` / `ITimeSource` / `PauseService` / `EventBus` / `INetBridge`（null 可）/ `UiManager` 等の既存 Manager への読み取り参照。
- `DDriveRuntimeBootstrap` は組み込み Manager を登録した**後**に module を `Order` 順で呼び、返った Manager を `loop.Register`、`Bind()`。終了時は逆順に `Unbind()`。例外は捕まえて警告 1 件 + その module を飛ばす（例外で止めない）。
- Editor プレビュー（ADR-4）用に `DDriveBootstrapContext` を Editor からも作れる口（既存の `Preview` 系が使っている生成経路に module を乗せる）。
- 契約 E-25（module の発見・順序・Bind / Unbind・例外時の継続）をテストで固定。

### 2.3 エディター基盤の公開（X-3）

- `Editor/Ux` のうち**持ち込み先が継承・配置する型を public** にする: `UxEditorWindow<TData>`（+ 抽象メンバーの契約）/ `FieldGuide<TData>` / `FieldGuideEntry` / `UxCard` / `UxRow` / `GuidedField` / `TuneKnobs` + `ITuneSource` / `DurationBar` / `AnchorPad` / `DataSwitcher` / `UxTheme`（USS の読み込み）/ `UxShow`。`UxTestHooks` / `UxScreenshot` / `UxPalette` は internal のまま。
- 公開した型は **Editor 契約**（`editor-contract.txt`、[42](42_distribution.md) §5.9）に載せ、以降は追加のみ。シグネチャの整理が要るものは X-3 の中で先に済ませる（公開してからは変えられない）。
- 確認用シーンで実 Manager を駆動する経路（`PreviewService` / `SceneXxxPreviewDriver` の基底）のうち汎用部分を public にし、持ち込み先の Manager でも「▶ で実物を動かし ■ で戻す」が書けるようにする。
- `[DataEditor]` の主エディター検査（`DataEditorRegistryTests`）は `Extension` 種別の窓も対象にする（`UxEditorWindow<T>` 派生であること）。

### 2.4 サンプル・確認用シーン・マニュアルの口（X-4）

- `ISampleProvider`（Editor、public、`TypeCache`）: 空状態の「サンプルを作る」が呼ぶ。`KindKey` / `CreateSample(folder)`。
- `IPreviewSceneProvider`: 「確認用シーン」ボタンが開くシーン（持ち込み先の `Assets/…/Preview/*.unity`）。
- `IManualPageProvider`: `ManualLauncher` が開くページの追加（持ち込み先の HTML / Markdown のパス）。`?` ボタンの導線が外部ページにも効く。
- AssetBrowser の種別フィルタ・「＋ 新規作成」の一覧に `Extension` 種別が provider の `Order` 順で並ぶ。

### 2.5 契約・雛形・手順書（X-5）

- 契約テスト E-25〜E-29（module / kind provider / Ux 公開型のコンストラクタと抽象メンバー / sample・preview・manual provider / `Extension` 種別の ID 生成）を `Tests/{Runtime,Editor}/ExternalContract/` に。
- **雛形 `Samples~/ExtensionTemplate/`**: 最小のプラグイン 1 式（種別 1 つ `Sample`〔`SMPID`〕+ Data + Manager + ファサード + Validator + `UxEditorWindow<T>` 派生の窓 + サンプル + 確認用シーン + asmdef 2 本〔Runtime / Editor〕）。持ち込み先はこれをコピーして名前を変える。
- `ProgrammerManual/extending.html` を「**D-Drive を変えずに種別を足す手順**」に書き換え（今の 14 手順は D-Drive 本体に足す手順）。[13](13_extensions.md) に X 節、[42](42_distribution.md) §5.14 に E-25〜。
- MS2026 側の `CLAUDE.md` に 1 行（「D-Drive に無い機能は、まず拡張点〔1014〕で自分のアセンブリに作る。D-Drive 本体を変えるのは既存 Manager の挙動を変えるときだけ」）— MS2026 リポジトリへの変更なのでユーザー承認後。

## 3. 使い方のイメージ（ステージ照明を MS2026 側で作る場合）

```
Assets/_Project/DDriveExtensions/Lighting/            （または Packages/com.ms2026.lighting）
  Runtime/  LightCueData.cs   [AssetIdDefinition(AssetType.Extension, typeof(LightCueMarker), "CUEID", KindKey = "LightCue")]
            LightShowData.cs  同上 "SHOWID" / "LightShow"
            LightingKinds.cs  IAssetKindProvider × 2（CUE_ / SHOW_、フォルダ、Preload）
            LightingModule.cs IDDriveModule（LightingManager を作って loop に載せ、Lighting.Bind）
            LightingManager.cs / Lighting.cs / StageFixture.cs / LightingRig.cs / LightPattern*.cs / Validator × 2
            Timeline/ LightCueTrack.cs / LightCueClip.cs / LightCueMixer.cs
  Editor/   LightCueEditor.cs : UxEditorWindow<LightCueData>  [DataEditor(typeof(LightCueData), "Light Cue Editor で開く")]
            LightShowEditor.cs / LightingRigEditor.cs / LightingSamples.cs (ISampleProvider) / LightingPreviewScene (IPreviewSceneProvider)
```

- D-Drive 側の変更は **0**（X-1〜X-5 が入った版以降）。D-Drive のリリースを待つのは「拡張点そのものが足りないとき」だけになる。
- 代償: 種別が持ち込み先に閉じる（他の持ち込み先で再利用したければ後で D-Drive 本体へ「昇格」する。昇格の手順も X-5 の手順書に書く: `AssetType` 末尾に正式値を足し、`KindKey` からのマイグレーション〔`IProjectMigration`〕で `CatalogEntry` を書き換える）。

## 4. ステージ照明（L 節）との関係 = ユーザー判断

| 案 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| **A（推奨）** | **X-1〜X-5 を先に D-Drive で作り（v1.10.0、約 8.5 日）、照明は MS2026 側で雛形から作る**（D-L1〜D-L4 を MS2026 の `Lighting` アセンブリに。D-Drive の L 節は閉じる） | 2 件目以降の依頼が D-Drive 待ちにならない。照明の仕様変更が MS2026 側だけで閉じる。D-Drive 本体に「ステージ照明」というゲーム寄りの種別が入らない | 照明の着手が X の完了（約 1.5 週）後になる。MS2026 の α 期限（11/05）に対して D-L 側 12.5 日が後ろへずれる。他の持ち込み先で照明を再利用するには昇格が要る |
| B | **L-1〜L-5 を D-Drive 本体で作る（v1.10.0）**、X は次（v1.11.0）に並行で進める | 照明が最短で出る（仕様 10 / 11 の決定 U-6 のまま）。D-Drive の種別として再利用できる | 3 件目以降の依頼はまた D-Drive 待ち。D-Drive 本体に照明が入る |
| C | 両方を並行（L を Sonnet、X を別の Sonnet） | 期限と仕組みを両取り | 同じファイル（`AssetType.cs` / `AssetNamingService` / Bootstrap）を 2 本が触るので衝突する。レビューと run-ci が 2 倍 |

## 5. 付随して直すもの（v1.9.0 のリリース作業で見つかった道具の穴。[1001](1001_open_items.md) §2 に 1 行ずつ）

- `check-release.ps1` に **pwsh が無ければ止める**検査（Windows PowerShell 5.1 で `bump-version.ps1` を動かすと BOM 無しの CHANGELOG を cp932 で読む）。
- `run-ci.cmd` の 8 段目の前に **NetCheck のビルドが最新コミットより古ければ警告**（`DDrive.Runtime.dll` の日付 vs `git log -1 --format=%ct`。batch の `-executeMethod DDrive.Editor.Build.NetCheckBuilder.BuildFromMenu` で作り直せる）。
- [12](12_review.md) §3 に 1 行: Editor 内のテスト失敗が `pathID >= 0` / `!pathName.empty()` / `m_PreventLoadingFromFile` の内部アサーションなら、Editor 再起動か batch の run-ci で再確認してから原因調査に入る（`CutsceneEditModeCameraSaveTests` が保存・削除する `Assets/__GeSaveTest.unity` が引き金の候補）。
- Sonnet への委任プロンプトの定型に「RT・描画・ウィンドウを開くテストは `RequiresGraphicsGuard`」を入れる（`.claude/skills/ddrive-agent-workflow/SKILL.md` §2）。

## 更新履歴

- 2026-10-16: 作成（最初は「依頼の受け渡し手順」として書き始めたが、ユーザーの意図は「持ち込み先で作れるプラグイン基盤」だったため設計文書に書き換えた）

## X-1 実装メモ（2026-10-16）

決めたこと:

- **KindKey の形**: `AssetIdDefinitionAttribute.KindKey`（名前付き引数のプロパティ、`Extension` では必須。空だと `AssetIdGenerator.TryAcceptDefinition` が Error を 1 件出して飛ばす）。`CatalogEntry.KindKey`（string、末尾に追加）。ContentHash は KindKey が空なら不変。
- **発見の仕組み**: `AssetKindRegistry`（`DDrive.Foundation.Extension`）が `AppDomain` の全アセンブリを走査（Foundation は UnityEditor を参照できないため `TypeCache` ではなく `AssetIdGenerator.FindDefinitions` と同じ流儀）。型の完全名順に集めて Order → KindKey で並べ、重複 KindKey は警告 1 回 + 先勝ち。`DDrive.Tests*` / `ExternalContract.Tests*` のアセンブリの provider は既定で除外（`IncludeTestAssemblies` を契約テストだけが true にする。実際の AssetBrowser・生成物にダミー種別を載せないため）。`AssetIdGenerator` も同じ除外を `ExternalContract.Tests*` に広げた。
- **index の持ち方**: `AssetRegistry` は `_byType`（従来）に加えて `(AssetType, KindKey)` の `_byKind` を持つ。`IAssetRegistry.Entries(type, kindKey)` は既定実装つきの追加（外部の Fake を壊さない）。
- `IExtensionAssetManager : IAssetManager`（`KindKey`）、`IKindValidator : IValidator`（`TargetKindKey`、`ValidatorRegistry` が Extension 種別の Data に対し KindKey が合うものだけ実行）。
- `IAssetKindProvider.Folder` は GameData ルートからの相対（`Sample` → `Assets/GameData/Sample`）。`Icon` は `Texture2D`（Foundation は UnityEngine を参照できる）。
- Editor: `AssetNamingService` / `AssetCreationService`（`GetCatalogName` / `NeedsPreloadDefault` の kindKey 付きオーバーロード、`AllCatalogNames` は provider ごとに展開）、`AssetReorganizer`、`AssetBrowserWindow`（`TypeFilterChoices`）、`NewAssetDialog`、`AddressablesRegistrationValidator`、`ddrive_validate` の行（`type` 欄に KindKey）、`ddrive_help types`（provider ごとの行）。既存種別の挙動は不変（KindKey を渡しても無視）。

未実装（X-5 へ持ち越し。[1004](1004_tasks.md) X-5 行）: `ddrive_asset_*` / `FieldTables` の Extension 対応（種別キーごとの欄表。`FieldTables` は組み込み 18 種のまま）、`ddrive_validate` の `scope=type:<KindKey>`、`SpecIdentifierCodec` / 仕様書同期の Extension 対応。

## X-2 実装メモ（2026-10-16）

- **型の置き場**: `IDDriveModule` / `DDriveBootstrapContext` / `ModuleRegistry` はすべて `DDrive.Runtime.Extension`（`Runtime/Extension/`）。`DDriveBootstrapContext` が `UiManager` 等の Runtime の型を持つため Foundation には置けない。
- **発見**: `ModuleRegistry.All`（AppDomain 全アセンブリ走査、結果キャッシュ、`Refresh()`）。規則は §5.14（`Type.IsVisible` = 入れ子は外側まで public・非 abstract・ジェネリック定義でない・public 引数なしコンストラクタ）。`DDrive.Tests*` / `ExternalContract.Tests*` は既定で除外し、`ModuleRegistry.IncludeTestAssemblies = true` + `Refresh()` で契約テストだけが拾う（AssetKindRegistry と同じ作法）。コンストラクタ / `Order` の例外は警告して飛ばす（`Order` は 0 扱い）。並びは型のフルネーム順にしてから `Order` 昇順で安定に並べ直す（同値は型名順）。
- **Context の中身**: `Registry`(IAssetRegistry) / `Pool` / `TimeSource`（`NetworkTimeSource(NetBridge)`。Bootstrap は元々 ITimeSource を持たないのでここで作る）/ `PauseService` / `Events`（module 間で共有する `EventBus` を Bootstrap が 1 つ新規に作る。組み込み Manager の EventBus とは別）/ `NetBridge` / `GameLoop` / `IsEditorPreview` / 組み込み Manager 13 個（`Audio` `Bgm` `Vfx` `Anim` `Materials` `Models` `Prefabs` `Ui` `UiTweens` `CameraFx` `Haptics` `Presentation` `Cutscene`）。すべて get 専用プロパティ、コンストラクタは internal（Bootstrap だけが作る）。項目は今後も追加のみ。
- **Bootstrap の順序**（既存の順序は変えていない）: 組み込み Manager の生成 → `loop.Register` → facade の `Bind` → **module（`CreateManagers` → 戻りの Manager を `loop.Register`〔null 要素は module 単位で警告 1 回して飛ばす〕→ `Bind()`）**。破棄時: `StopAll` → **module を逆順に `Unbind()`** → module の Manager と組み込みを `Unregister` → 組み込み facade の `Unbind`。`Bind` まで成功した module だけが `Unbind` される。`BindFacades = false` でも module は動く。
- **例外**: `CreateManagers` / `Bind` / `Unbind` の例外は module 単位で捕まえ、警告 1 件（module の型のフルネームつき）+ その module を飛ばす。`CreateManagers` が成功した後に `Bind` が例外を出した module の Manager は GameLoop に登録されたまま（Unbind は呼ばれない）。Tick の定常経路には何も足していない（呼び出しは起動と終了だけ）。
- **Editor プレビューは X-3 に回した（判断）**: プレビューが Manager を new している場所は `SceneAnimPreviewDriver` / `SceneVfxPreviewDriver` / `ScenePresentationPreviewDriver` / `PreviewService` / `EditorAudioFactory` / `CanvasEditorWindow` / `PrefabEditorWindow` / `MaterialEditorWindow` / `CutsceneEditModeManagers` ほか個別で、種別ごとに必要な部品の組が違い、共通の生成経路が無い。ここで無理に統一すると多数のエディターの挙動を変えるうえ、持ち込み先が module を使うプレビューは X-3（`PreviewService` 系の public 化）で初めて必要になる。X-3 で共通の生成口を作るときに `DDriveBootstrapContext`（`IsEditorPreview = true`）と module 呼び出しを乗せる。いまは `IsEditorPreview` を持つだけで、Bootstrap は常に false を渡す。
- **契約テスト E-25**: `ExternalContractModuleTests`（PlayMode 6 件）+ ダミー `ExternalPackage.Fake.ExternalModuleProbe`（First〔-5〕/ SameA / SameB〔0〕/ Faulty〔3〕/ Late〔5〕）。

## X-3 実装メモ（2026-10-16）

**公開した型**（`DDrive.Editor.Ux`、すべて `public`。`DDrive.Editor` を参照する持ち込み先の Editor asmdef から継承・配置できる）: `UxEditorWindow<TData>`（3 つの partial 全部。`protected abstract` = `PrefsKey` / `Guide` / `ManualPage` / `CardDescription`、他は `protected virtual` の口）/ `FieldGuide<TData>` / `FieldGuideEntry` / `FieldTier` / `FieldRowKind` / `UxCard` / `UxRow` / `UxVisualRow` / `GuidedField` / `TuneKnobs` / `TuneKnobsRow` / `ITuneSource`（+ `TuneValueKind` / `TuneCandidate` / `TuneAddPlan` / `TuneCategories`）/ `DurationBar`（+ `Values` / `UxLifeMode`）/ `AnchorPad` / `DataSwitcher` / `UxTheme` / `UxShow` / `IUxSyncable`。`UxTestHooks` / `UxScreenshot` / `UxPalette` は internal のまま。`TuneKnobsRow` は spec の一覧に無かったが、`TuneKnobs`（パネル）を欄にするのに外部が必要なので公開した。

**公開前に整理した点**（公開後は追加のみなので、先に直した）:
- `UxRow.MakeInput` / `IsCompound` は `GuidedField.MakeInput` / `IsCompound` の複製だったので internal にした（外部は `GuidedField` を使う）。`UxRow.TierName` / `IssueKey` / `MakeIssue` も内部の道具なので internal。
- `DataSwitcher` の静的ヘルパー（`Name` / `CategoryOf` / `CollectAll` / `Record` / `LoadRecent`）は内部用なので internal。
- `TuneCategories.Names` は可変な `string[]` の public フィールドだったのを `IReadOnlyList<string>` のプロパティに（配列の中身を外から書き換えられない）。`Words` / `HasWord` は内部用なので internal。公開は `Names` と `Infer` だけ。
- 既存の窓の挙動は変えていない（型の可視性だけの変更。メンバーの名前・引数・戻り値は変えていない）。`UxRow` のコンストラクタが引数 9 個と長いが、公開してからは変えられない形になるので、整理せず現状の並びを契約にした（将来は別の入口を追加する）。

**プレビュー基底**（`Editor/Preview/`）: `UxPreviewDriverBase`（public abstract。ルート `[D-Drive] …` の遅延作成〔DontSave、プレハブモード中はステージに置く〕・Edit Mode の Tick〔`EditorApplication.update`、dt は 0.25 秒で頭打ち、`Speed` を掛けて `OnTick` へ、`Paused` 中は呼ばない〕・シーン切替 / プレハブモードの開閉 / Play Mode 突入 / 再コンパイル / シーン保存の直前 / Dispose での自動停止 `OnReset(PreviewResetReason)`。保存の直前だけルートを残し、それ以外は破棄）と `PreviewResetReason`。派生は `OnTick` と `OnReset` を書き、Manager の生成と登録は自分で行う。**既存の `Scene*PreviewDriver`（Vfx / Anim / Presentation）と `PreviewService` は変えていない**（重複が出るが今回は許容）。

**Editor プレビューへの module の乗せ方（X-2 から回した分）**: `DDriveBootstrapContext.ForEditorPreview(...)`（`IsEditorPreview = true`、必須は Registry / Pool / GameLoop、他は省略可で省略した組み込み Manager は null）と `EditorModuleHost : IDisposable`（`Editor/Preview/`。`ModuleRegistry` の module から Manager を作り、自前の `GameLoop` に登録して Edit Mode で Tick。Dispose で StopAll → 逆順に Unbind → 解除）。起動と終了の実体は Runtime の新しい公開クラス `ModuleRunner`（`DDriveRuntimeBootstrap` もこれを使うようにした。順序・例外時の扱いは X-2 のまま）。既存の窓・Driver には乗せていない。

**`[DataEditor]` の検査**: `DataEditorRegistryTests` に `EveryExtensionKindDataType_HasEditor`（`AssetType.Extension` + `KindKey` の Data は、アセンブリを問わず〔`DDrive.Tests*` を除く〕専用エディターを持つこと）を足した。既存の `EveryDataType_PrimaryIsNewEditor_…` は登録済みの全 Data 型（外部のものを含む）を見るので、外部の窓も「Order 0 の `UxEditorWindow<T>` 派生」であることが検査される。`editor-contract.txt` の `[DataEditor]` 節はテスト用アセンブリの窓を含めない。

**契約**: `editor-contract.txt` に `== UxPublicApi ==`（`DDrive.Editor.Ux` の public 型すべて。public / protected の全メンバー、static / abstract / virtual / override の区別つき）と `== PreviewApi ==`（`UxPreviewDriverBase` / `PreviewResetReason` / `EditorModuleHost`）を追加（追加のみ）。E-27 = EditMode `ExternalContractUxTests`（8 件）。窓の模擬は `ExternalSampleEditorWindow`（`Tests/Editor/ExternalContract/`。`ExternalSampleData` に欄を足した〔追加のみ〕）。

**将来の整理候補**: ① `SceneVfxPreviewDriver` / `SceneAnimPreviewDriver` / `ScenePresentationPreviewDriver` を `UxPreviewDriverBase` の上に載せ替える（同じ自動停止の処理が 3 か所と基底に重複している）② `UxRow` のコンストラクタ（引数 9 個）に対する名前付きの作成口 ③ `FieldGuideEntry` の public フィールドをプロパティに（公開してしまったので MAJOR 扱い。必要なら別の型を足す）④ `ITuneSource` の必須メンバーが多い（既定実装つきメンバーの追加は MINOR で済むが、必須メンバーの追加は MAJOR）。

## X-4 実装メモ（2026-10-16）

**拡張点**（`DDrive.Editor.Extension`、`Editor/Extension/`、すべて public）:
- `ISampleProvider`（`KindKey` / `DisplayName` / `AssetDataBase CreateSample(string folder)`）: Extension 種別（`[AssetIdDefinition].KindKey`）の窓の空状態「サンプルを作る」。`folder` = `<GameData>/<種別のフォルダ>/Sample`（素材の置き場。D-Drive が用意する）。**保存していない Data を返すと D-Drive が `AssetCreationService.Create` で命名規則どおりに保存し、カタログ・Addressables に登録する**（カテゴリ `Sample`。既定 Load は種別の既定）。保存済みで Id のある Data を返すと `RegisterExisting` で登録だけ行う。同じ DisplayName（カテゴリ Sample）が既にあれば provider を呼ばずそれを選ぶ。例外・null は警告 + 何もしない。
- `IPreviewSceneProvider`（`KindKey` / `ScenePath` / 既定実装つき `Setup(Scene)`）: ヒーローの先頭に「確認用シーン」ボタンが付き、シーンを開く（未保存の確認 = `SaveCurrentModifiedScenesIfUserWantsTo`、既に開いていれば開き直さない、開いた直後に `Setup`）。provider が無ければボタンを出さない。シーンが無ければ警告。
- `IManualPageProvider`（`Pages` = `(key, path, title)` の一覧）: `ManualLauncher.OpenPage(key[, anchor])` が、組み込みのページ（`docs/DesignerManual/<key>.html`）が無い key を外部ページから解決して開く（**組み込みが先**）。`UxCard` の「?」は `ManualLauncher.OpenPage` を呼ぶので、窓の `ManualPage` / `ManualAnchor` が返す名前が外部 key でもそのまま効く。path はプロジェクト基準（相対はプロジェクトのルートから、絶対も可）、`file://` で開く。解決だけの `ManualLauncher.TryResolveExternalPage`（開かない）も public。

**発見**（`ExtensionProviders`、public static）: `TypeCache`（Editor なので可）で public〔`Type.IsVisible`〕・非 abstract・ジェネリック定義でない・public 引数なしコンストラクタを集め、型のフルネーム順。`DDrive.Tests*` / `ExternalContract.Tests*` は既定で除外（`IncludeTestAssemblies` + `Refresh()` で契約テストだけが拾う。X-1 の `AssetKindRegistry` / X-2 の `ModuleRegistry` と同じ作法）。KindKey / ページ key の重複は警告 1 回 + 先勝ち。コンストラクタの例外は隔離。呼び出しの実体は `ExtensionProviderActions`（`CreateOrGetSample` / `OpenPreviewScene`、public）。

**接続**: `UxEditorWindow<TData>` の空状態は、`HasSample`（組み込み）が false で provider があるときだけ「サンプルを作る」を出す（組み込み種別の経路は不変）。サンプルを作る GameData のルートは追加の `protected virtual string SampleGameDataRoot`（既定 = `DDriveProjectSettings.GameDataRoot`。テストは一時フォルダへ向ける）。AssetBrowser の種別フィルタと「＋ 新規作成」の一覧に Extension 種別が provider の `Order` 順で並ぶのは X-1 で実装済み（`AssetBrowserWindow.TypeFilterChoices` / `NewAssetDialog`）なので、ここでは確認だけ。

**契約**: `editor-contract.txt` に `== ExtensionProviders ==` 節（3 interface + `ExtensionProviders` + `ExtensionProviderActions`）と `UxEditorWindow<T>.SampleGameDataRoot` の 1 行（いずれも追加のみ）。E-28 = EditMode `ExternalContractProviderTests`（5 件。発見〔テスト用アセンブリ除外・重複の先勝ち〕/ 空状態 → サンプルを作る → `SMP_…` が一時フォルダに出来てカタログに KindKey 付きで載る → 確認用シーンが開き `Setup` が呼ばれる → ▶ / provider が無いときボタンが出ない / provider の失敗は警告で継続 / `ManualLauncher` が外部ページを解決し組み込みが先）。ダミーは `ExternalSampleProviders.cs`。テストの一時フォルダは `Assets/Tests/DDriveTemp/X4Providers`（TearDown で削除）。

**判断**: `ISampleProvider.CreateSample` が「保存していない Data を返す」形を基本にしたのは、Id・カタログ・Addressables・Version の扱い（`AssetCreationService.Create` が持つ）を外部に再実装させないため。素材（音・画像など）は渡した `folder` に自分で作る。

## X-5 実装メモ（2026-10-16）

**雛形 `Packages/com.ddrive.core/Samples~/ExtensionTemplate/`**（`package.json` の `samples` に "Extension Template" として登録。`Samples~` は Unity から見えない置き場なのでビルド・Validation・他のテストに混ざらない。`Samples~/Demo` と同じ扱い）:
- `Runtime/`（asmdef `Sample.Extension.Runtime`〔`DDrive.Foundation` / `DDrive.Runtime` 参照〕）: `SampleData`（`[AssetIdDefinition(AssetType.Extension, typeof(SampleMarker), "SMPID", KindKey = "Sample")]`、欄 = `Intensity` / `Duration` / `Loop` / `ValueDef Fade`）/ `SampleKind`（`IAssetKindProvider`: 接頭辞 `SMP`・フォルダ `Sample`・カタログ `SampleCatalog`・Preload・Order 1000）/ `SampleManager`（`IExtensionAssetManager`。`Play(id)` → `Handle`、`Tick` で経過時間、`StopAll`。`InstanceStore` + `List` を for で走査、LINQ・クロージャ・boxing なし）/ `Samples`（静的ファサード。未 Bind は no-op）/ `SampleModule`（`IDDriveModule`）/ `SampleDataValidator`（`IKindValidator`、Code は `SMP-` 接頭辞）。
- `Editor/`（asmdef `Sample.Extension.Editor`〔`DDrive.Editor` 参照、Editor 専用〕）: `SampleEditor`（`UxEditorWindow<SampleData>` 派生 + `[DataEditor]` + メニュー `DDriveMenu.Editors + "Sample Editor"` + `SampleFieldGuide` + ▶ は `SamplePreviewDriver : UxPreviewDriverBase` が `EditorModuleHost` で module の Manager を実物のまま動かす）/ `SampleSamples`（`ISampleProvider`）/ `SamplePreviewScene`（`IPreviewSceneProvider`。シーンは同梱せず、無ければ Console に案内が出る）/ `SampleManual`（`IManualPageProvider`、`Documentation~/sample-editor.md`）。
- `README.md`: コピー手順・名前の付け替え箇所の表・作り込むところ・昇格の手順。

**雛形とテスト用ダミーの共通化 = しなかった（判断）**: テスト側（`ExternalSampleData` / `ExternalSampleKind` / `ExternalModuleProbe` / `ExternalSampleEditorWindow` / provider 3 つ）は今のまま残し、雛形は別に置く。理由: ① 雛形のソースをテストのアセンブリへコピーする形にすると、同じ KindKey `Sample` の定義が 2 重になり（テスト用アセンブリは `AssetKindRegistry` 等が既定で除外する規則に頼る）、`AssetIdGenerator` / `ModuleRegistry` / `ExtensionProviders` の発見や既存の E-25〜E-28 のテストの前提が崩れる ② テスト用ダミーは「契約のどこを突くか」のために雛形より細かく作ってある（重複キー・Faulty module・状態の差し替え）③ ビルドや Validation に雛形が混ざらないことを優先した。代わりに、雛形そのものは `ExtensionTemplateCompilesTests`（EditMode、`[UnityTest]` + `RecompileScripts`）が「`Assets/Tests/DDriveTemp/ExtensionTemplateSmoke/` へ丸ごとコピー → コンパイルが通る → `IAssetKindProvider` / `IDDriveModule` / 3 provider / `[DataEditor]` が D-Drive に認識される → 消して再コンパイル」で確かめる（ドメインリロードを 2 回跨ぐので約 1 分）。空プロジェクトでの導入試験（[archive/48](archive/48_p11_install_test_2026-09-20.md) と同じ手順）は別途行う。

**E-29（Extension 種別の ID 生成）**: `ExternalContractIdGenerationTests`（3 件）。KindKey ごとの定数クラス（`SMPID.Xxx`）に出る / provider の `Prefix`（`SMP`）が既知の接頭辞として効き定数名に残らない（`AssetKindRegistry.IsKnownPrefix`）/ 既存種別の定数クラスは増減しない。E-25〜E-29 は `Tests/{Runtime,Editor}/ExternalContract/` に揃った（E-25 = Runtime `ExternalContractModuleTests`、E-26 = `ExternalContractKindProviderTests`、E-27 = `ExternalContractUxTests`、E-28 = `ExternalContractProviderTests`、E-29 = `ExternalContractIdGenerationTests`）。

**X-1 から持ち越した MCP の対応**（`mcp-tools.txt` は変更なし = 引数の名前・型・必須は同じで、`type` / `scope` が受ける値が増えただけ）:
- **対応した**: `ddrive_asset_list` / `create` / `get` / `set` / `delete` の `type` が Extension の種別キー（`KindKey` / `DisplayName`、大小文字無視。例 `Sample`）を受ける（`FieldTables.TryResolve` が `IAssetKindProvider` から表を作る。欄 = Data クラスが宣言した公開欄の先頭 6 個〔非シリアライズ・`HideInInspector` を除く〕、`FieldTables.TypeNames()` に種別キーも出る）。作成は `AssetNamingService` の KindKey 版（接頭辞・フォルダ）で、一覧・取得・検索は種別 + KindKey で絞る（`FieldTables.Matches`）。`ddrive_validate` の `scope=type:<KindKey>` / `asset:<KindKey>:<id>`（`ValidationScope.KindKey`）。`SpecIdentifierCodec.TryExtractIdentifier` の KindKey 付きオーバーロード（接頭辞は `IAssetKindProvider.Prefix`）。テストは `DDriveExtensionKindMcpTests`（5 件。`DDrive.Editor.Mcp` は isuzu 版 MCP 導入時だけコンパイルされる）。
- **見送り**: **仕様書同期（`SpecDiffService` / `SpecDiffValidator` のシート定義）の Extension 種別対応**。仕様書のタブは種別ごとに列セットを持つ設計（[27](27_spec_sheet.md)・[32](32_spec_web.md)）で、KindKey ごとの列定義を外部が宣言する口（新しい拡張点）と SpecWeb（GAS）側の変更が要り、X-5 に入れると大きい。`SpecIdentifierCodec` の下地（KindKey 付きの接頭辞判定）だけ入れた。必要になったら別チケット（[1001](1001_open_items.md) に 1 行）。`ddrive_asset_usages` / `unused` / `ddrive_editor_open` / `ddrive_preview` の `type` も `FieldTables.RequireType` を共有するので種別キーを受けるが、Extension 種別の依存グラフ・プレビューの再生は未検証（`ddrive_preview play` は組み込みの再生種別だけ）。

**手順書**: `ProgrammerManual/extending.html` を「新しい種別を追加する」に書き換え、先頭に「D-Drive を変えずに種別を足す（雛形から）」（6 手順・拡張点の一覧・守ること・うまくいかないとき）を置き、従来の 14 手順は「D-Drive 本体に種別を足す手順（昇格する場合）」として残した。[13](13_extensions.md) に X 節（拡張点の一覧）、[50](50_consumer_guide.md) に `extend.html`（拡張の作り方）を追加。
