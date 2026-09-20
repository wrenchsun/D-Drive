# 47. 2026-09-20 自前レビュー結果（P-3〜P-10 = 移植・更新・互換性チケットの実装）

> **対象**: [11_tasks.md](11_tasks.md) / [42_distribution.md](42_distribution.md) §6 の P-3〜P-10 で入った 7 コミット（P-10.5 のレビュー対象）。
>
> | コミット | 内容 |
> |---|---|
> | `4bb8142` | P-3: 互換性スナップショットテスト群（`Tests/Editor/Compat`、`Editor/Compat`） |
> | `2dbee72` | P-4: 境界違反の解消、NGO の `versionDefines` 化、`DDriveProjectSettings` 新設、テストの DevRepoOnly 化、UniTask タグ固定 |
> | `c44a728` | P-5: パッケージ化（`Assets/DDrive` → `Packages/com.ddrive.core`、`package.json`、`testables`、sentinel resolve、`AssetSearch.Roots` 等） |
> | `89fed1d` | P-6: セットアップウィザード（`Editor/Setup/`）+ `ProjectSetupValidator`、`SpecAutoSync` の Play Mode ガード |
> | `8303cac` | P-7: `SchemaVersion` + `Editor/Migration/`、`CI.MigrateCheck`、`SchemaVersionValidator` |
> | `964b763` | P-8: `Editor/Update/`（更新ウィンドウ・`UpdateActions`・`ChangelogRangeReader` 等）、`CatalogContentHashMsg` の版照合、`NetDebugOverlay` |
> | `1fdb4ab` | P-9: `Tools/Release/*.ps1`、`run-ci.cmd` への CHANGELOG ガード、`Documentation~` 同期 |
> | `9e5f23c` | P-10: `README` / `AGENTS_CONSUMER` / `ddrive-consumer` スキル / `Tools~/CI` / SpecWeb README §16 |
>
> **方法**: [docs/44](44_review_2026-09-19.md) / [docs/45](45_review_cutscene_2026-09-19.md) と同じく**読み取り専用**。作業ツリーは clean（`HEAD == origin/main == 9e5f23c`）なので HEAD 状態のファイルをそのまま読み、必要な箇所だけ `git show <hash>:<path>` で差分を確認した。**Unity MCP は未接続（`isuzu-unity` が CONNECT_TIMEOUT）のため、コンパイル・テスト実行は一切行っていない。** したがって本書の指摘はすべて **コードとファイルを読んで確認した事実**（および `grep` / ファイル内容の実測）であり、Unity 上での実挙動の確認は含まない。「Unity がこう振る舞うはず」という推定が混じる項目には明示的にその旨を書いた。
>
> 前提として読んだもの: `CLAUDE.md`、[docs/12](12_review.md) §3（互換性節）・§7（リリース手順）、[docs/42](42_distribution.md) 全節、[docs/44](44_review_2026-09-19.md) / [docs/45](45_review_cutscene_2026-09-19.md)（観点・書式・分類）。

## 観点

[docs/12](12_review.md) §3 のチェックリスト + docs/44 / 45 の観点に、今回の依頼で明示された「移植特有の観点」を足した。

- **持ち込み先で壊れる経路**: 埋め込みパッケージ（開発リポジトリ）と git URL 解決（`Library/PackageCache/com.ddrive.core@<hash>`、読み取り専用）の両方で動くか。`PackageInfo.FindForAssembly` / `resolvedPath` / `assetPath` の使い分け、決め打ちパスの残り、`Documentation~` / `Tools~` / `Samples~` の参照、`AssetDatabase` で PackageCache 配下に書こうとする箇所、`AssetSearch.Roots` の走査範囲
- **`DDriveProjectSettings`**: `ScriptableSingleton` + `[FilePath]` の保存先、`IsDevelopmentRepo` と `DevRepoSettingsSync`、「sentinel resolve」の穴、`EmitGeneratedAsmdef`
- **P-3 スナップショット**: 列挙順の安定性、ゴールデン更新経路、`DDRIVE_UPDATE_COMPAT_SNAPSHOTS`、フィクスチャ（実 Data 型）が各種走査に混ざらないか
- **P-4 NGO 切り離し**: `#if DDRIVE_NGO` の網羅、`versionDefines`、asmdef の `references`
- **P-6 ウィザード / P-8 更新ツール**: `ManifestJson` の編集、`Client.Add`、途中失敗時の状態、スキルコピー、`testables`、`ChangelogLocator` の探索順
- **P-7 マイグレーション**: `TypeCache` 発見の範囲、多段適用、`AppliesTo` の例外、Undo グループ、`SchemaVersion` を誰がいつ書くか
- **P-8 版照合**: `ProtocolVersion` 既定 0、判定順、`NetDebugOverlay` の `#if`
- **P-9 スクリプト**: PowerShell 5.1 互換（BOM・`-Encoding`・stderr）、`robocopy /MIR`、`git diff` の base、正規表現の堅牢性
- **tests**: 追加テストが本当に検証しているか、Unity 無しの CI で走る順序と失敗の握りつぶし、`DevRepoOnly` の `Assume`
- **docs**: docs/42 の実装メモと実装の一致、README の 5 ステップが実メニュー名・実 API と一致するか、消費側ドキュメントへの開発リポジトリ専用記述の混入

---

## P1 — 持ち込み先で導入 / 更新が失敗する・データ破損・互換性ポリシーの穴

### 導入（P-4 / P-5 / P-10）

**P1-1. `DDrive.Foundation` / `DDrive.Runtime` の asmdef が UniTask と NGO を無条件の `references` で持ったままなので、README どおりに manifest に 1 行足しただけの素のプロジェクトでは D-Drive のどのアセンブリもコンパイルできず、README 手順 3 の「セットアップウィザードを開く」に到達できない**

- `Packages/com.ddrive.core/Foundation/DDrive.Foundation.asmdef:5` — `"references": ["UniTask"]`
- `Packages/com.ddrive.core/Runtime/DDrive.Runtime.asmdef:5-16` — `"references"` に `"UniTask"` と **`"Unity.Netcode.Runtime"`** が入ったまま。`versionDefines` で `DDRIVE_NGO` を足しただけで、参照自体は外していない
- `Packages/com.ddrive.core/Samples~/Demo/DDrive.Samples.asmdef:5` / `Tests/Runtime/DDrive.Tests.Runtime.asmdef:5` も同じ
- `Packages/com.ddrive.core/package.json:19-26` — `dependencies` は Addressables / InputSystem / Newtonsoft / URP / Timeline / ugui の 6 件のみ。UniTask・R3・NGO は（仕様上書けないので）無い
- `Runtime/` の 6 ファイル（`Presentation.cs` / `PresentationHandle.cs` / `PresentationManager.cs` / `Cutscene.cs` / `CutsceneHandle.cs` / `CutsceneManager.cs`）が `using R3;`、34 ファイルが `using Cysharp...`

つまり、`Packages/manifest.json` に `com.ddrive.core` を 1 行足しただけの状態では **UniTask / R3 / NGO のどれも存在しない**。asmdef の `references` に列挙された名前が解決できないアセンブリは Unity がコンパイル対象から外す（`Assembly for Assembly Definition File ... will not be compiled, because it has references to non-existent assemblies` の経路。**この Unity の挙動自体はレビューでは実行検証していない**が、[42] §2.3 #9 の実装メモ自身が「**NGO 無しでの実際のコンパイル確認は未実施**」と書いており、少なくとも「動く保証が無い」ことは確定している）。`DDrive.Foundation` が落ちれば `DDrive.Runtime`・`DDrive.Editor` も連鎖で落ち、**`Tools > D-Drive > …` のメニューが 1 つも出ない**。

これは README（`Packages/com.ddrive.core/README.md:23-37`）の手順と矛盾する。README 手順 2 は「Package Manager が `package.json` の依存を自動解決します。git 配布の依存（UniTask・R3）はこの時点では自動追加されないため、**次のステップのウィザードで追加します**」と書いているが、そのウィザード（`DDrive.Editor` 内の `ProjectSetupWizardWindow`）はまさに UniTask/R3 が無いと存在しない。鶏と卵になっている。

**再現条件**: 空の Unity プロジェクト（= P-11 そのもの）で README 手順 1〜3 を実行する。

**直し方（どちらも要る）**:
1. README 手順 1 を「`dependencies` に 3 行 + `scopedRegistries` を足す」に変える（`com.ddrive.core` / `com.cysharp.unitask#2.5.11` / `com.cysharp.r3#1.3.1` / `org.nuget.r3` + Unity NuGet レジストリ）。値は `Editor/Setup/ProjectSetupInspector.cs:21-32` に定数があるので、そのまま貼れる形で README に載せる。ウィザードの「1. 依存」は「後から欠けに気づいたとき用」の保険に位置づけを下げる
2. NGO は `references` から外す。Unity で optional 依存を表す標準手段は「NGO 依存コードだけを別 asmdef（例 `DDrive.Runtime.Ngo`）に切り出し、その asmdef に `Unity.Netcode.Runtime` 参照 + `defineConstraints: ["DDRIVE_NGO"]` を付ける」。`versionDefines` は define を足すだけで参照解決には効かないため、現状の構成では A-7（NGO を必須依存から外す）は成立していない

**P1-2. `CI.ValidateAll` の `ForbiddenApiScanner` が、持ち込み先では必ず 26 件の Error を出す（走査ルートが D-Drive 自身のパッケージになり、`Samples~` と `Tests` が除外されていない）**

- `Packages/com.ddrive.core/Editor/Validation/CI.cs:215-219` — `ResolveForbiddenApiScanRoot()` が `PackageInfo.resolvedPath`（消費側では `<project>/Library/PackageCache/com.ddrive.core@<hash>`）を返す
- `Packages/com.ddrive.core/Editor/Validation/ForbiddenApiScanner.cs:85` — `Directory.GetFiles(rootFolder, "*.cs", SearchOption.AllDirectories)`。`~` 付きフォルダもファイルシステム上は普通のフォルダなので `Samples~/` `Tools~/` も走査対象
- 同 `:99` — 除外は `normalized.Contains("/Samples/")` だけ。P-5 で `Assets/DDrive/Samples/` → `Samples~/Demo/` に移したため、**この除外がもう効かない**（`"/Samples~/"` は `"/Samples/"` を含まない）。`Tests/` は元から除外されていない

HEAD のパッケージに対して同じ規則を機械的に適用した実測値は **26 件**（内訳: `Runtime/` 12 件 = `Anim2D/Anim2DFacing.cs:127`・`Loop/DDriveRuntimeBootstrap.cs:637,645,723`・`Net/NetDebugOverlay.cs:96`・`Net/NgoNetBridge.cs:103,189,396,544,659`・`Ui/UiButton.cs:191`・`Ui/UiSlider.cs:364`／`Samples~/Demo/` 6 件 = `NetBridgeSmokeTest.cs:61`・`NetCheckRunner.cs:214,227,246,284,532`／`Tests/` 8 件 = `Tests/Editor/ForbiddenApiScannerTests.cs:38,49,59,69,79,89`・`Tests/Runtime/UiSliderTests.cs:236,241`）。

`Runtime/` の 12 件と `Tests/` の 8 件は P チケット以前からある既存の当たり（`Assets/DDrive` を走査していた頃も同じ）だが、**`Samples~` の 6 件は P-5 の移設で新しく増えた回帰**であり、かつ **P-4 の変更（走査ルートを `PackageInfo` 由来にした）によって、これまで「持ち込み先ではフォルダが無いので 0 件で静かに通っていた」ものが「持ち込み先でも必ず 26 件出る」に変わった**。

影響が大きいのは P-10 の CI テンプレで、`Tools~/CI/run-ddrive-ci.cmd:52-59` と `Tools~/CI/ddrive-ci.yml:77-85` はどちらも `CI.ValidateAll` の exit code を fail 条件にしている。**持ち込み先の CI は初日から赤になる**（`AGENTS_CONSUMER.md:27` も「CI ではこれを fail 条件にしてください」と指示している）。

**直し方**:
1. `ForbiddenApiScanner.Scan` の除外を `/Samples/` から `/Samples~/`・`/Tests/`・`/Tools~/`・`/Documentation~/` を含む形に広げる（`Samples~` は「製品コードの規約対象外」という元の意図そのもの）
2. `Runtime/` の 12 件は本物の当たりなので、許可リスト（`AllowedFileSuffixes`）に足すか、`ITimeSource` / `IAssetLoader` 経由に直すかを決める。どちらにせよ **P-11 までに 0 件にしておかないと、消費側 CI の「Error があれば fail」が最初から成立しない**
3. `Tests/Editor/ForbiddenApiScannerTests.cs` は「禁止パターンを含む文字列リテラル」を書くテストなので、`ForbiddenApiScanner.cs` と同じく許可リストに `ForbiddenApiScannerTests.cs` を足すのが素直

**P1-3. パッケージ同梱のテスト群が、読み取り専用の `Library/PackageCache` では必ず失敗する（`testables` を ON にした持ち込み先でテストが赤くなる）**

[42] §2.1 の Tests 行は「**持ち込み先で ON にしても通るように**」を条件に「テストはパッケージに置くが既定は無効」と決めている。HEAD の実装はこれを満たしていない。

(a) **パッケージ配下にフォルダ・アセットを作るテスト**（PackageCache は immutable なので `AssetDatabase.CreateFolder` / `CreateAsset` が失敗する）
- `Tests/Editor/Compat/CodegenGoldenTests.cs:36-38, 104-105` — `AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "Temp")` → `CreateAsset(data, "Packages/com.ddrive.core/Tests/Editor/Temp/SE_Codegen_ShapeCheck.asset")`
- `Tests/Editor/Compat/ConstantNameGoldenTests.cs:54-56, 88-89` — 同じ手口で 20 件作る
- `Tests/Editor/Compat/ValidatorSeverityRegistryTests.cs:63-72` — 同じ `Tests/Editor/Temp`
- `Tests/Editor/Setup/ProjectSetupActionsTests.cs:22, 58-73` — `Packages/com.ddrive.core/Tests/Editor/TempGameDataSetup`
（`Tests/Editor/` 配下にはこれ以外にも同じ流儀の一時アセット生成が多数ある。上記は P-3/P-6 で新規に増えた分）

(b) **リポジトリ直下のファイルを前提にするテスト**
- `Tests/Editor/Compat/PackageVersionConsistencyTests.cs:23-24, 55-59` — `Directory.GetParent(Application.dataPath)` + `"CHANGELOG.md"`。持ち込み先ではプロジェクト直下に D-Drive の `CHANGELOG.md` は無く、あったとしてもそれは**持ち込み先自身の CHANGELOG** なので、`Assert.IsTrue(File.Exists(...))` か `Assert.AreEqual(changelogVersion, runtimeBaseVersion)` のどちらかで必ず落ちる。P-9 が `Packages/com.ddrive.core/CHANGELOG.md` を同梱したのに、このテストはそちらを見ていない
- 同 `:39` — `Packages/com.ddrive.core/package.json` をプロジェクト直下からの相対で探すため、git URL 解決では見つからず `Assert.Ignore`（= 版の一致検査が持ち込み先では無効）

**直し方**: (a) は一時アセットの置き場所を `Assets/` 配下の一時フォルダ（`Assets/DDriveTestTemp/` を `[SetUp]` で作り `[TearDown]` で消す）か、`Path.GetTempPath()` + `AssetDatabase` を使わない経路に寄せる。`AssetIdGenerator.Regenerate` を通す必要があるテストは `Assets/` 配下でないと `AssetSearch` にも乗らないので、`Assets/` 側に置くのが確実。(b) は `PackageInfo.resolvedPath` 基準に直し、`<package>/CHANGELOG.md` → 無ければ 2 階層上（`ChangelogLocator.ResolvePath` と同じ規則）にする。

### データ（P-7）

**P1-4. `VersionStampProcessor.OnWillSaveAssets` が保存のたびに `SchemaVersion = DDriveSchema.Current` を書くため、マイグレーション未適用のデータが「適用済み」に化ける**

`Packages/com.ddrive.core/Editor/Versioning/VersionStamp.cs:89-95`:

```csharp
asset.Version++;
asset.Author = userName;
asset.UpdatedAt = nowIso;
asset.SchemaVersion = DDriveSchema.Current;   // ← 無条件に現在値へ引き上げる
```

`DDriveMigrationRunner.Plan`（`Editor/Migration/DDriveMigrationRunner.cs:143`）は `asset.SchemaVersion < migration.ToSchema` で対象を選ぶので、**先に保存されてしまった Data は永久に対象外になる**。`SchemaVersionValidator`（`Editor/Validation/SchemaVersionValidator.cs:19`）も `data.SchemaVersion < DDriveSchema.Current` でしか警告しないので、警告も出ない。`CI.MigrateCheck` も「未適用なし」と報告する。

**再現条件**（[42] §4.2 の標準手順をそのまま踏むだけで起きる）: 持ち込み先が manifest のタグを新版（`DDriveSchema.Current` が 2 に上がった版）へ進める → 手順 4「Unity を開く」→ 手順 5「更新を適用」の**前に**、デザイナーが専用エディタで既存 Data を 1 つ開いて 1 文字編集し保存する → その Data の `SchemaVersion` が 2 になる → 手順 5 のマイグレーションはその Data を飛ばす → **旧形式のフィールドが旧形式のまま、新形式として扱われる**（= 静かなデータ破損）。

現時点では `DDriveSchema.Current == 1` かつ `IDataMigration` の実装が 0 件なので実害は出ていないが、**マイグレーション基盤の前提そのものを壊している**ため、`SchemaVersion` が 2 になる最初の版が出た瞬間に顕在化する。しかも「一度スタンプされてしまった Data」は後から見分けられない（旧形式か新形式かを値から判定できないのが `SchemaVersion` 導入の動機、[42] §4.3 冒頭）。

**直し方**: `OnWillSaveAssets` では `SchemaVersion` を**上げない**。書いてよいのは (a) `StampNew`（新規作成 = 定義上その時点のスキーマ、`VersionStamp.cs:121-123`。これは正しい）と (b) `DDriveMigrationRunner.Apply`（`DDriveMigrationRunner.cs:200`）だけ。どうしても保存時に補正したいなら `if (asset.SchemaVersion > DDriveSchema.Current) { /* ダウングレード検知 */ }` のような単調性チェックに留める。あわせて「`SchemaVersion` は保存では変わらない」ことを EditMode テストで固定する（下記「テストの穴」4）。

**P1-5. `SchemaVersionValidator` が既存データ全件に「解消手段の無い Warning」を出す**

- `Editor/Validation/SchemaVersionValidator.cs:19-25` — `data.SchemaVersion < DDriveSchema.Current`（= 1）で Warning `DD-SCHEMA-OUTDATED`
- `Foundation/Data/AssetDataBase.cs:68-72` — `SchemaVersion` はフィールド追加のみなので、**既存 `.asset` には行が存在せず 0 で読まれる**

実測: `Assets/GameData` 配下で `m_EditorClassIdentifier: DDrive.Runtime::` を持つ `.asset` は 73 件、うち `DisplayName` を持つ（= `AssetDataBase` 派生）ものが約 67 件。`SchemaVersion` を含む `.asset` は **0 件**（`grep -rl "SchemaVersion" Assets/GameData --include=*.asset` が 0）。つまり `Validation > Run All` を実行すると **67 件前後の `DD-SCHEMA-OUTDATED` Warning** が一気に出る。

この Warning が案内する操作（`Tools > D-Drive > Update > マイグレーション(適用)`）は、`IDataMigration` の実装が 0 件なので `Plan().TotalCount == 0` になり **何もしない**。つまり「警告は出るが、書いてあるとおりにしても消えない」状態。唯一消す方法は全 Data を再保存することだが、それは `VersionStampProcessor` により **67 件の `Version` が一斉に +1** される（[11] 6-3 が明示的に避けたかった「版数の一括汚染」そのもの）。

持ち込み先でも同じことが起きる: v1.0.0 で作ったデータは `StampNew` で `SchemaVersion=1` が入るので当面は無害だが、**P-11 / P-12 より前に作られた開発リポジトリのデータ**と、`SaveAllSuppressed` 系の一括処理しか通っていないデータは 0 のまま残る。

**直し方**: (a) P1-4 の修正とセットで、`DDriveSchema.Current` が 1 の間は「0 と 1 は同じ形式」と扱う（`SchemaVersion == 0` を「未スタンプ = 1.0.0 形式」と読み替える定数を置き、Validator の条件を `data.SchemaVersion < DDriveSchema.Oldest` 等にする）か、(b) 「`SchemaVersion` を現在値に揃えるだけの `IProjectMigration`」を 1 本用意して、`VersionStampSuppression` 内で `Version` を上げずに一括スタンプする経路を作る。(b) なら「マイグレーション(適用)を押せば消える」という Validator のメッセージとも一致する。

### CI・リリース（P-9）

**P1-6. `run-ci.cmd` の `[1/8] CHANGELOG ガード` が、遅延展開の付け忘れで**常に**`[OK]` になる（ガードが一度も働いていない）**

`Tools/CI/run-ci.cmd:48-60`:

```bat
where pwsh >nul 2>nul
if "%ERRORLEVEL%"=="0" (
    echo [1/8] CHANGELOG ガード ...
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\Release\check-release.ps1" -GuardOnly
    if not "%ERRORLEVEL%"=="0" (        ← ここ
        ...
        set "OVERALL_EXIT=1"
    ) else (
        echo [OK] CHANGELOG ガード
    )
)
```

`cmd` は括弧ブロック全体を 1 つの複合コマンドとして読み込むときに `%VAR%` を展開するため、**内側の `%ERRORLEVEL%` は `where pwsh` の結果（= 0）に固定**される。`if not "0"=="0"` は常に偽なので、`check-release.ps1` が exit 1 を返しても `[OK] CHANGELOG ガード` が出て `OVERALL_EXIT` は 0 のまま。

同じファイルの `:146-150` に、まさにこの罠を NetCheck で踏んで直したときのコメント（「括弧ブロックの中では `%ERRORLEVEL%` はブロックに入る前の値」）が残っているのに、新規追加した `[1/8]` で再発している。

**直し方**: `if not "!ERRORLEVEL!"=="0"`（`setlocal EnableDelayedExpansion` は 2 行目で有効化済み）。`:163-175` の Summarize ブロックも同じ形だが、そちらは判定に使っていないので実害なし。

**P1-7. `bump-version.ps1 -Tag` が、版を書き換える*前*のコミットにタグを打つ（`#vX.Y.Z` で参照した持ち込み先には旧版の `package.json` が届く）**

`Tools/Release/bump-version.ps1` の流れ:

1. `:116-119` 事前チェックで「作業ツリーがクリーン」を要求 → この時点の `HEAD` は**版を上げる前**のコミット
2. `:161-219` `package.json` / `DDriveVersion.cs` / `CHANGELOG.md` を**ワークツリーに書き込む**（コミットはしない）
3. `:234-240` `Documentation~` と `Packages/com.ddrive.core/CHANGELOG.md` を同期（これもワークツリー）
4. `:248-255` `git -C $repoRoot tag -a vX.Y.Z` → **`HEAD` = 手順 1 のコミット**にタグが付く
5. `:258` 「変更されたファイルを確認し、コミットしてください」

つまり `vX.Y.Z` タグは「バージョンを上げる前」のツリーを指す。[docs/12](12_review.md) §7 の手順もこの順序をそのまま書いており（手順 5 で `bump-version.ps1 -Tag`、手順 6 で `git push --tags`）、**間にコミットする手順が無い**。手順どおりに実行して push すると、持ち込み先が `#v1.1.0` で引いたパッケージの `package.json` は `1.0.0` のまま、`CHANGELOG.md` にも `[1.1.0]` の節が無い、という状態になる。P-9 の AC「手順どおりに `v1.0.0` タグが切れ、`PackageVersionConsistencyTests` が green」は満たされていない（※ P-9 では実際にはタグを作っていないので、この不整合は顕在化していない）。

**直し方**: `-Tag` の処理を「(a) 変更ファイルを明示パスで `git add` + `git commit -m "Release vX.Y.Z"` → (b) `git tag -a`」にするか、`-Tag` を廃して docs/12 §7 に「5.5. 変更をコミットする」を挿入し、タグ作成は手順 6 の直前に人が行う形にする。前者にするなら、CLAUDE.md の「破壊的な git 操作は指示されたときだけ」に沿って `-Commit` を別スイッチにするのが安全。

---

## P2 — エッジケース / 規約違反 / パフォーマンス

### 走査・混入（P-3 / P-5）

**P2-1. P-3 の旧版フィクスチャ 19 件（実 Data 型）が、`AssetBrowser` の一覧・仕様書インデックス・ID ピッカー等に混ざる**

`/Compat/Fixtures/` を除外しているのは **3 箇所だけ**（`grep` で確認）:
- `Editor/Codegen/AssetIdGenerator.cs:111`
- `Editor/Migration/DDriveMigrationRunner.cs:104`
- `Editor/Validation/CI.cs:194`

`AssetSearch.Roots`（`Editor/AssetSearch.cs:25-37`）は `{"Assets", packageInfo.assetPath}` なので、**パッケージ内の `Tests/Editor/Compat/Fixtures/v1_0_0/*.asset` は `t:SeData` 等の検索に必ずヒットする**。除外していない主な走査:

- `Editor/AssetBrowser/AssetBrowserWindow.cs:344-353` — `/Tests/` も `/Compat/Fixtures/` も見ていない。`definitionByDataType` は Data 型の**名前空間**で絞っている（`DDrive.Runtime.Audio.SeData` 等は当然通る）ので、**デザイナーの Asset Browser に `CompatFixture_SeData` 等 19 行が常時並ぶ**（開発リポジトリでも持ち込み先でも）
- `Editor/Spec/SpecDiffService.cs:164-180` — 同様。フィクスチャのファイル名が `SeData.asset`（規約接頭辞が無い）なので `SpecIdentifierCodec.TryExtractIdentifier` で弾かれて実害は無いが、これは偶然の防御
- `Editor/Inspector/AssetIconService.cs:176-186`・`Editor/AssetBrowser/AddressablesSync.cs:138-194` は `/Tests/` で除外されるので安全（`Packages/com.ddrive.core/Tests/...` は `/Tests/` を含む）
- `Editor/Preview/EditorAnchorRegistry.cs:103,124,144` / `Editor/Inspectors/AssetIdLookup.cs:79` / `Editor/Ui/DataIdLookup.cs:46` / `Editor/Presentation/PresentationTrackKindMapping.cs:125` / `Editor/Dependencies/DependencyGraphService.cs:72,133` — いずれも除外なし。フィクスチャの `AnchorData` / `AnchorGroupData` はプレビュー用 Registry に登録され、ID ドロップダウンにも並ぶ

**直し方**: 除外条件を 1 箇所（例 `DDrive.Editor.AssetSearch.IsCompatFixture(path)` か、`AssetSearch.FindAssets` の戻り値からフィクスチャを落とす専用オーバーロード）に集約し、上記の走査から呼ぶ。もっと簡単な手としては、フィクスチャの拡張子を `.asset` のままにする必要があるか（`LegacyAssetFixtureTests` は `AssetDatabase.LoadAssetAtPath` を使うので必要）を再検討し、`Fixtures~/` のような Unity 非可視フォルダ + `File.ReadAllText` ベースの検証に切り替える案もある。

**P2-2. `ManualPages` が `Documentation~` を優先するようになったため、開発リポジトリで `docs/DesignerManual` を更新しても Editor の「マニュアル」ボタンには反映されない**

- `Editor/Manual/ManualPages.cs:70-84` — `PackageInfo.resolvedPath + "Documentation~/DesignerManual"` が存在すればそちらを返す
- P-4 のコメント（`:63-69`）は「`Documentation~` の同梱は P-9 のリリース手順で行う予定なので、**当面はフォールバック側が使われ挙動は変わらない**」と書いているが、**P-9（`1fdb4ab`）がまさにその `Documentation~/DesignerManual`（28 ページ）と `Documentation~/ProgrammerManual`（14 ページ）を同梱したので、前提が変わっている**

`Documentation~` の中身は `Tools/Release/bump-version.ps1` の robocopy `/MIR` でしか更新されない（`:234-237`）。したがって、マニュアルを直して `bump-version.ps1` を回すまでの間、Editor のマニュアルボタンは**古い内容**を開く。HEAD 時点では `diff -rq docs/DesignerManual Packages/com.ddrive.core/Documentation~/DesignerManual` が差分なしなので今は一致しているが、構造的に必ずズレる。

`Tests/Editor/ManualPagesTests.cs:16-31` も `GetManualFolder` 経由で列挙しているので、**ズレていてもテストは green のまま**（同じフォルダを列挙して同じフォルダと比較しているだけ）。

**直し方**: (a) 開発リポジトリ（`DDriveProjectSettings.IsDevelopmentRepo == true`）では `docs/` を優先する、(b) `ManualPagesTests` に「`docs/DesignerManual` と `Documentation~/DesignerManual` のファイル一覧が一致する」検査を足して同期漏れを機械検出する、のどちらか（両方が望ましい）。

### 更新ツール（P-7 / P-8）

**P2-3. `DDriveMigrationRunner.Plan` は計画時点の状態だけで `AppliesTo` を評価するため、多段（0→1→2）の連鎖で 2 段目が取りこぼされる。`AppliesTo` / `Migrate` が投げた例外も捕まえていない**

`Editor/Migration/DDriveMigrationRunner.cs:141-147`:

```csharp
foreach (var migration in orderedMigrations)
{
    if (asset.SchemaVersion < migration.ToSchema && migration.AppliesTo(asset))
    {
        dataPlan.Add(new PlannedDataMigration(migration, asset));
    }
}
```

- **連鎖の取りこぼし**: 0→1 の適用で初めて 1→2 の条件を満たすようになる Data（典型例: 0→1 で新フィールドに値を移し、1→2 でその新フィールドを見る）は、計画時点では `AppliesTo` が false なので 1→2 が計画に入らない。適用後の `SchemaVersion` だけが 1 に上がり、次回の `MigrateCheck` で再度拾われる（= 2 回実行しないと完了しない）
- **`FromSchema` を見ていない**: 条件は `SchemaVersion < ToSchema` だけなので、`SchemaVersion == 1` の Data に `0→2` のマイグレーションが適用され得る。`FromSchema` は `:129` のソートにしか使われていない
- **例外**: `AppliesTo`（`:143`）も `Migrate`（`:199`）も try/catch が無い。1 つの実装が投げると `PlanProject()` / `Apply()` ごと落ち、`CI.MigrateCheck`（`Editor/Validation/CI.cs:87`）と `UpdateWindow.RefreshMigrationSection`（`Editor/Update/UpdateWindow.cs:150`）が例外で止まる。CLAUDE.md §0-4「例外で止めない」に反する。`UpdateStepsFactory.MigrateStep`（`Editor/Update/UpdateStepsFactory.cs:31-45`）だけは try/catch しているが、その場合 **一部の Data だけ `SchemaVersion` が上がった中途半端な状態**で止まる
- **Undo グループ**: `:198` で Data ごとに `Undo.RecordObject` するだけで `Undo.SetCurrentGroupName` / `CollapseUndoOperations` が無い。67 件マイグレートしたら Ctrl+Z を 67 回押すことになる
- **`MarkMigrationApplied` の対象**: `:202` が **Data マイグレーションの Id も** `AppliedMigrationIds` に記録するが、`Plan` の側では `IProjectMigration` にしかこの台帳を見ていない（`:161`）。将来 Data と Project で同じ Id を使うと Project 側が「適用済み」と誤認して飛ばされる

**直し方**: 条件を `migration.FromSchema <= asset.SchemaVersion && asset.SchemaVersion < migration.ToSchema` にし、`Apply` 側で「1 つ適用するたびに残りの `AppliesTo` を再評価する」ループにする（Plan は「何件くらい動くか」の見積もりに留める）。`AppliesTo` / `Migrate` は try/catch して警告 + その Data をスキップ。`Apply` の先頭で `Undo.IncrementCurrentGroup()`、末尾で `Undo.CollapseUndoOperations(group)`。`MarkMigrationApplied` は `IProjectMigration` だけに限定する。

**P2-4. `ChangelogLocator` は「`resolvedPath` の 2 階層上」を先に見るので、持ち込み先で埋め込み配置（[42] §4.5 の緊急回避）になっていると*持ち込み先自身の* `CHANGELOG.md` を D-Drive のものとして表示する**

`Editor/Update/ChangelogLocator.cs:28-39`。解決先は配置形態で変わる:

| 配置 | `resolvedPath` | 2 階層上 | 結果 |
|---|---|---|---|
| 開発リポジトリ（埋め込み） | `<repo>/Packages/com.ddrive.core` | `<repo>` | `<repo>/CHANGELOG.md` = 正しい |
| git URL / レジストリ | `<proj>/Library/PackageCache/com.ddrive.core@<hash>` | `<proj>/Library` | 無いのでパッケージ直下へフォールバック = 正しい（P-9 が同梱した） |
| **持ち込み先で埋め込み** | `<proj>/Packages/com.ddrive.core` | `<proj>` | **`<proj>/CHANGELOG.md`（持ち込み先の変更履歴）を掴む** |
| ローカルパス参照（`file:`、P-11 の想定） | 開発リポジトリの実パス | 開発リポジトリ root | 正しい |

3 行目は「更新ウィンドウに他人の CHANGELOG が出て、『破壊あり』の判定（`ChangelogCompatibilityAnalyzer`）まで別物の本文で行われる」ことを意味する。

**直し方**: 探索順を逆にする（パッケージ直下を先に見て、無ければ 2 階層上）。P-9 で同梱が始まった以上、パッケージ直下が正本。

**P2-5. `ProjectSetupValidator` は Data が 1 件も無いプロジェクトでは一度も呼ばれないため、P-6 の AC「空プロジェクト + manifest 1 行から、ウィザードの『すべて直す』だけで `Run All` Error 0」は検査が空振りしたまま満たされる**

`Editor/Validation/ProjectSetupValidator.cs:20-25` のコメント自身がこの制約を書いている（`ValidatorRegistry.RunAll` は `context.AllAssets` を foreach するだけ）。`CI.LoadAllAssetDataAssets`（`Editor/Validation/CI.cs:181-207`）は `/Compat/Fixtures/` を除外するので、**素の持ち込み先では `AllAssets.Count == 0`** → `IUniversalValidator` が 1 つも走らない。ウィザードが作るもの（カタログ・`UiLayerSettings`・`DDriveSpecSettings`）はどれも `AssetDataBase` 派生ではないので、最初の Data を作るまでこの状態が続く。

つまり導入直後は「`Run All` が Error 0 / Warning 0」になるが、それは**何も検査していない**からで、UniTask が無くても URP でなくても Addressables が未初期化でも同じ結果になる。P-11 でこの AC を確認するときに誤った安心を与える。

**直し方**: `ValidatorRegistry.RunAll` が `AllAssets` が空でも `IUniversalValidator` を 1 回呼ぶようにする（Foundation 側の変更なので [42] §5.4 の公開 API 互換に注意。`RunAll` の**挙動**の変更であってシグネチャは変わらない）。それが重いなら、`CI.RunValidation` 側で「Data 0 件のときは `AllAssets` にダミー 1 件ではなく、Universal だけを明示的に 1 回回す」分岐を入れる。

**P2-6. `Test-ChangelogGuard` の判定（スナップショットが変わったら `package.json` の version も上がっていること）は、通常の開発コミットでは必ず fail する設計になっている**

`Tools/Release/ReleaseChecks.ps1:231-249` — スナップショットに差分があると、`CHANGELOG.md` の変更だけでは足りず `package.json` の version が `$BaseRef` 時点より上がっていることまで要求する。

今回の対象範囲では P-7（`editor-contract.txt` / `public-api-DDrive.Foundation.txt`）と P-8（`net-messages.txt` / `public-api-*.txt`）がスナップショットを更新しているが、どちらも version は 1.0.0 のまま。したがって、もし P1-6 の `%ERRORLEVEL%` バグが無ければ、**P-7・P-8 のコミット時点で `run-ci.cmd` が落ちていたはず**。P-9 の実装メモが「CHANGELOG ガードは green」と書いているのは、実行時点で `origin/main..HEAD` に差分が無かった（= ガードの対象外パス）ためで、ガードが実際に効くことは一度も確認されていない。

[42] §5.11-10 の文言（「`version` が上がっていなければ fail」）は「リリース PR」を想定した条件であって、日々の開発コミットには合わない。

**直し方**: version の一致検査は `check-release.ps1`（`-GuardOnly` なし = リリース時）だけに残し、`run-ci.cmd` から呼ぶ `-GuardOnly` は「スナップショットが変わったら `CHANGELOG.md` も変わっていること」だけにする。docs/42 §5.11-10 の記述も合わせて直す。

### PowerShell（P-9）

**P2-7. `$ErrorActionPreference = 'Stop'` と `& git ... 2>&1` の組み合わせは Windows PowerShell 5.1 で終了コード判定に到達しない**

`Tools/Release/ReleaseChecks.ps1:9`（`$ErrorActionPreference = 'Stop'`）+ 以下の 3 箇所:
- `:166` `$diffOutput = & git diff --name-only "$CompareRef" -- $ProtocolCsRelativePath 2>&1`
- `:206` `$diffOutput = & git diff --name-only "$BaseRef..HEAD" 2>&1`
- `:234` `$basePackageJsonLines = & git show "${BaseRef}:Packages/..." 2>&1`（こちらは try/catch 内なので救われる）

PowerShell 5.1 は native コマンドの stderr を `2>&1` でパイプラインに載せるとき `ErrorRecord` にラップするため、`$ErrorActionPreference='Stop'` だと **その時点で終了エラー（`NativeCommandError`）になる**。つまり `:167` / `:207` の「`$LASTEXITCODE -ne 0` なら丁寧なメッセージを返す」分岐には**到達できず**、スクリプトが例外で落ちる。

実害が出る場面: shallow clone / detached HEAD / `origin/main` が無い CI（`check-release.ps1:35` の既定 `-Base origin/main`）、初回リリース前でタグが無い環境（`bump-version.ps1:133` の `git describe --tags` は try/catch があるので救われている）。`run-ci.cmd` は `pwsh`（PS7）を明示的に呼ぶので開発機では踏まないが、`Tools~/CI` を写して `powershell.exe` で回す持ち込み先では踏む。

**直し方**: `2>&1` を外して `$diffOutput = & git ... ; $code = $LASTEXITCODE` の形にするか、各 git 呼び出しを `try { ... } catch { }` で囲む（`:234` と同じ流儀に揃える）。

### 消費側ドキュメント（P-10）

**P2-8. `ddrive-consumer` スキルのサンプルコードがコンパイルできない（`Presentation.Play` / `Cutscene.Play` は `ref PlayContext` を取る）**

`Packages/com.ddrive.core/Documentation~/skills/ddrive-consumer/SKILL.md:28-34`:

```csharp
var handle = Presentation.Play(PRESID.X, ctx);
var cutsceneHandle = Cutscene.Play(CUTID.X, ctx);
```

実シグネチャ（`Tests/Editor/Compat/Snapshots/public-api-DDrive.Runtime.txt:515, 1509`）:

```
method static Play(AssetId<CutsceneMarker> id, ref PlayContext ctx) : CutsceneHandle
method static Play(AssetId<PresentationMarker> id, ref PlayContext ctx) : PresentationHandle
```

`ref` が要るので、書いてあるとおりに書くと CS1620 でコンパイルできない。docs/44 P1-2（マニュアルが「指示どおりに書くとコンパイルできない」コードを案内していた件）と同種で、今回は**持ち込み先の AI エージェントが最初に読むスキル**に入っている点がより悪い。

なお `Audio.PlaySe(SEID.X)`（README `:51`、SKILL.md `:23`）と `Vfx.Spawn(VFXID.X, position, rotation)`（SKILL.md `:26`）はスナップショット（`:323`、`:2527`）と一致しており正しい。

**直し方**: `var ctx = new PlayContext(...); var handle = Presentation.Play(PRESID.X, ref ctx);` に直す（`PlayContext` の作り方も 1 行添えると親切）。

**P2-9. 消費側ドキュメントが、同梱されていない `docs/migrations/` を参照先として案内している**

- `Packages/com.ddrive.core/README.md:75` 「エラーがあれば `CHANGELOG.md` の「破壊あり」を疑い、`docs/migrations/` の移行ガイドを確認する」
- `Documentation~/AGENTS_CONSUMER.md:33`（同旨）
- `Documentation~/skills/ddrive-consumer/SKILL.md:65` 「`docs/migrations/` の移行ガイドを確認する」

[42] §2.1 のとおり `docs/` は **D（持っていかない）** で、パッケージにも `Documentation~` にも `migrations/` は入っていない（`ls Packages/com.ddrive.core/Documentation~` = `AGENTS_CONSUMER.md` / `DesignerManual` / `ProgrammerManual` / `README.md` / `skills`）。持ち込み先の人・エージェントは、指示された場所を探しても見つけられない。

**直し方**: (a) `docs/migrations/*.md` を `Documentation~/migrations/` に同梱する（`bump-version.ps1` の同期対象に 1 行足すだけ）か、(b) 参照先を「開発リポジトリ `github.com/wrenchsun/D-Drive` の `docs/migrations/`」という URL 付きの表記に統一する。`CHANGELOG.md` 内のリンク（`docs/migrations/`）も同じ問題を持つ。

---

## 整理項目（バグではない）

### P-3 / 互換性スナップショット

- `Editor/Compat/PublicApiSnapshotBuilder.cs:82-101` — `IsApiVisible` は `type.IsPublic || type.IsNestedPublic` で判定するが、`IsNestedPublic` は**外側の型が internal でも true** になる。コメント（`:16`）の「外側の型が public なネスト public」という意図と実装がずれており、実際には API として見えない型がスナップショットに載り得る。`type.IsVisible` を使えば 1 行で正しくなる
- `Editor/Compat/NetMessageSnapshotBuilder.cs:54` — `GetFields(Public|Instance|DeclaredOnly)` なので `[SerializeField] private` フィールドは対象外。現状の `INetMessage` 実装はすべて public フィールドなので実害は無いが、「フィールドを private + `[SerializeField]` に変える」変更がワイヤ互換を変えずに検出漏れする
- `Editor/Compat/CompatSnapshotMenu.cs:38-48` — メソッド名が `WriteIfChanged` だが、実際には内容を比較せず常に `File.WriteAllText` している（無害だが名前と実装が食い違う）
- `Tests/Editor/Compat/PackageVersionConsistencyTests.cs:42` — `package.json` が無いときの `Assert.Ignore` メッセージが「P-5 でパッケージ化するまでは無くてよい」のまま。P-5 は完了しているので、この分岐に入る＝持ち込み先で探索に失敗している、という意味に変わっている
- `Tests/Editor/Compat/Fixtures/v1_0_0/` のフィクスチャは `Version: 0` で作られている（`SeData.asset` を確認）。`StampNew` を通っていないため「Unity Editor 経由で作った本物の旧版」ではあるが、実データの初期状態（`Version: 1`）とは違う。`LegacyAssetFixtureTests` は `Version` を見ていないので問題にはならないが、フィクスチャの作り方をドキュメント（`LegacyAssetFixtureTests.cs:13-18`）に書くなら明記しておくとよい

### P-4 / P-5 / パッケージ化

- `Editor/Material/MayaImportProfile.cs:96` と `Editor/Material/TextureImportProfile.cs:178` の `assetPath.StartsWith("Assets/DDrive/")` は、P-5 の移設で**死にコード**になった（D-Drive 自身のアセットは `Packages/com.ddrive.core/` にあり、そもそも同じ行の `!assetPath.StartsWith("Assets/")` で弾かれる）。消すか、`PackageInfo.assetPath` 基準に直すか決める
- `Editor/Validation/ForbiddenApiScanner.cs:99` の `/Samples/` は `Samples~/` に追随していない（P1-2 で記載）。同じく `Tools~/` `Documentation~/` も走査対象に入っている（`.cs` は無いので今は実害なし）
- `Editor/AssetSearch.cs:25` — `Roots` は `public static readonly string[]`（要素は書き換え可能）で、かつ**静的初期化時に 1 回だけ** `PackageInfo.FindForAssembly` を呼ぶ。docs/25 M-1 で既に「書き換え可能な public static readonly」が整理項目に挙がっており、P-13 発効後は `DDrive.Editor` の public なので互換面の対象外とはいえ、`IReadOnlyList<string>` にしておくほうがよい
- `Packages/com.ddrive.core/` に **`LICENSE` が無い**（[42] §2.2 は `package.json` / `CHANGELOG.md` / `LICENSE` を **P**（同梱）としている）。`package.json` にも `licensesUrl` / `documentationUrl` / `changelogUrl` が無いので、Package Manager の UI からドキュメントに飛べない
- `ProjectSettings/ProjectSettings.asset:836-837` — `scriptingDefineSymbols` は `Standalone:` にしか `DDRIVE_DEV_REPO` が無い。アクティブなビルドターゲットを Android 等に切り替えると、`DevRepoOnlyGuard`（9 件）が静かに Inconclusive になり、`DevRepoSettingsSync` も動かなくなる（= `IsDevelopmentRepo` が更新されなくなる）
- `Editor/Settings/DevRepoSettingsSync.cs:18-29` — `EmitGeneratedAsmdef = false` を書くのは `IsDevelopmentRepo` が false だった初回だけ。誰かがウィザードで ON に戻すと二度と false に戻らない（意図どおりかもしれないが、コメントの「開発リポジトリ初回検出時だけ」だけでは読み取れない）

### P-6 / P-7 / P-8

- `Editor/Setup/ManifestJson.cs:49-55` — `JsonConvert.SerializeObject(manifest, Formatting.Indented)` で manifest 全体を書き直すため、元ファイルのコメント（Newtonsoft は既定でコメントを読み飛ばす）や空行は失われる。キー順は `JObject` が保持するので並びは保たれる。`packages-lock.json` には一切触らない（正しい）
- `Editor/Setup/ProjectSetupActions.cs:180-217` — `CopyConsumerSkillIfBundled` は確認なしで `overwrite: true` コピーし、**宛先にしか無いファイルは消さない**（旧版の残骸が残る）。書き出す `.ddrive-version` を**読む側が誰もいない**ので、「既に最新」「利用者が手を入れている」を検出できない。更新ウィンドウのボタン（`Editor/Update/UpdateWindow.cs:239-252`）も同様に無確認
- `Editor/Update/UpdateWindow.cs:166` — 「更新を適用」ボタンに再入ガードが無い（同期実行なので実害は小さいが、長時間かかる処理なので `EditorUtility.DisplayProgressBar` か `SetEnabled(false)` があるとよい）
- `Editor/Update/SemVer.cs:11-21` — `Version.TryParse` に委譲しているので `"1.0"` や `"1.0.0.0"` も通る。`LastAppliedVersion` に何が入るかは自分たちで制御しているので実害は無いが、CHANGELOG の見出しは人が書くので `^\d+\.\d+\.\d+$` の検査があると安全
- `Runtime/Net/CatalogContentHashGate.cs:257` — `LastKnownRemotePackageVersion` は Client ごとではなく 1 本しか持たない（1v1 前提なので実害なし、[14] §12）。Client 側からは Host の版が分からないまま（`NetDebugOverlay` に `?` と出る）で、この非対称は実装メモ（[42] §5.6）に書かれているとおり
- `Editor/Validation/ProjectSetupValidator.cs:30` — `private static ValidationContext _reportedForCtx` が最後の `ValidationContext`（= 全アセットの `List<AssetDataBase>`）を静的に保持し続ける。docs/44 整理項目の `CatalogAddressCoverageValidator._lastRunContext` と同じ既存パターン

### P-9 / P-10

- `Tools/Release/list-obsolete.ps1:53` — 正規表現 `\[Obsolete\s*\(\s*"` なので、**引数なしの `[Obsolete]`** と **`[System.Obsolete(...)]`**（完全修飾）を拾えない。`PublicApiSnapshotBuilder` は引数なしの `[Obsolete]` も `[Obsolete]` として記録するので、両者の対象が食い違う
- `Tools/Release/ReleaseChecks.ps1:34-46` — `ConvertTo-SemVer` が `^\d+\.\d+\.\d+$` 固定なので、`package.json` の version に prerelease サフィックス（`1.1.0-rc1` 等）を付けると `bump-version.ps1` / `check-release.ps1` が throw する。[42] §4.1 は prerelease の運用を決めていないので、当面は「使わない」と明記しておくとよい
- `Tools~/CI/ddrive-ci.yml:102-112` — EditMode/PlayMode を無条件で実行するが、`testables` が既定 OFF なら D-Drive のテストは 1 件も出ない。テンプレのコメント（`run-ddrive-ci.cmd:79-82` にはある）を yml 側にも書くとよい
- `Documentation~/skills/ddrive-consumer/SKILL.md:38` — `Tools > D-Drive > AssetBrowser` と書いてあるが、実メニューは `Tools/D-Drive/Asset Browser`（`Editor/AssetBrowser/AssetBrowserWindow.cs:45` の `DDriveMenu.Root + "Asset Browser"`）。スペースの有無
- 消費側ドキュメント（`README.md` / `AGENTS_CONSUMER.md` / `SKILL.md` / `Tools~/CI/*`）を読み直したが、**開発リポジトリ専用の記述（新種別追加手順・ワークツリー運用・SpecWeb のテスト・MCP のトークン運用）の混入は無い**。MCP は「持ち込み先の構成に従う」の 1 行のみ（[42] §3.5 の 2026-09-20 決定どおり）。実 URL は `github.com/wrenchsun/D-Drive` と `unitynuget-registry.openupm.com`、実名は `author.name: "wrenchsun"` のみで、いずれも意図的なもの。個人のメールアドレス・トークン・Drive の ID 等は含まれていない

---

## テストの穴（この範囲で追加すべきテスト）

1. **NGO / UniTask 無しでコンパイルが通ることの検証が無い**（P1-1）。[42] §2.3 #9 の実装メモ自身が「`#if` の網羅性は grep で確認、実コンパイルは未実施」と書いている。`grep` では「asmdef の `references` が残っている」ことは分からないので、grep では代替にならない。P-11 のスモーク（`run-consumer-smoke.cmd`、[42] §5.11-11）に「NGO 無し」「UniTask 無し（= 導入手順の最初の状態）」の 2 パターンを入れる
2. **`SerializedLayoutSnapshotTests` はフィールドの順序変更を検出できない**。`Editor/Compat/SerializedLayoutSnapshotBuilder.cs:53` が全行を `lines.Sort(StringComparer.Ordinal)` でソートしているため、`propertyPath` の並びが変わっても出力は同じ。[42] §5.1 は「`[Serializable]` struct のフィールド順変更 = **禁止**（バイナリ形式が順序依存）」と定めているので、この互換面は機械判定できていない。あわせて `NextVisible` は `[HideInInspector]` を辿らないので、`SchemaVersion` / `ImportSourceGuid` 等の**隠しフィールドの削除も検出されない**（[42] §4.3 実装メモが「更新不要」と書いているのは事実だが、それは「検出できない」と同義）。型ごとにソートし、型内は宣言順のまま出力する形に変えるべき
3. **`validator-severity.txt` が 1 行しか無い**（実測: `DD-ADDR-CATALOG-MISSING=Error` のみ）。`ValidatorSeverityRegistryTests.CollectGenericValidatorCodes`（`:104-107`）は `ResolveDataType(validator.Target)` が null になる `IUniversalValidator`（`Target == AssetType.None`）を全部スキップするため、P-6/P-7/P-8 で新設した 14 個の Code（`DD-SETUP-*` 12 種 + `DD-SCHEMA-OUTDATED` + `DD-SETUP-UPDATE-PENDING`）が 1 つもゴールデンに載っていない。[42] §5.8 の互換面（Validation の重さ）は実質的に未カバー
4. **`SchemaVersion` を誰が書いてよいかを固定するテストが無い**（P1-4）。「通常の保存で `SchemaVersion` が上がらない」「`StampNew` では上がる」「`DDriveMigrationRunner.Apply` でのみ `ToSchema` まで上がる」の 3 本。`Tests/Editor/Migration/DDriveMigrationRunnerTests.cs` は Runner 側しか見ていない
5. **`Tools/Release/*.ps1` と `Tools/CI/run-ci.cmd` に一切テストが無い**。P1-6（`%ERRORLEVEL%`）と P1-7（タグの対象コミット）はどちらも「1 回でも実際に失敗させてみれば分かる」種類のもの。最低限、`check-release.ps1 -GuardOnly` を「わざと CHANGELOG を更新しないコミット」に対して実行して `exit 1` になり、`run-ci.cmd` が `[FAIL]` を出すことを手で 1 回確認して [docs/12](12_review.md) §7 に記録する
6. **`DevRepoOnlyGuard` が効いているかどうかを誰も検査していない**。`Assume.That(false, ...)` は NUnit で Inconclusive になり、Unity の Test Runner はこれを失敗として数えない（= CI は緑のまま）。これは持ち込み先向けには意図どおりだが、**開発リポジトリで `DDRIVE_DEV_REPO` が外れても誰も気づかない**（9 件が静かに消える）。`#if DDRIVE_DEV_REPO` のときだけ走る「DevRepoOnly カテゴリのテストが 9 件実行されたこと」を確認するテスト、または `run-ci.cmd` の結果サマリに Inconclusive 件数を出す処理があるとよい
7. **`ManifestJson` の往復テスト（`Tests/Editor/Setup/ManifestJsonTests.cs`）は `JObject` 相手の純粋関数しか見ていない**ので、「実 `manifest.json` を読んで書き戻したときに Unity が壊れない（整形・キー順）」は未検証。一時ファイルに現行 `Packages/manifest.json` の内容をコピーして `Load` → `Save` → 再 `Load` で `JToken.DeepEquals` が真、くらいは足せる
8. **`ProjectSetupActionsTests.AllCatalogNames_ReturnsDistinctNonEmptyNames`（`:51-52`）の 3 つ目の assert は恒真式**: `names.Contains("MiscCatalog") || names.All(n => n != "MiscCatalog")` は常に true（「含む or 含まない」）。何も検査していないので消すか、意図する条件に書き直す

---

## 誤検知（疑ったが読み直して問題無しと判断）

次のレビューで同じ道を通らないために残す。

### 持ち込み先での解決

- **`AiStandardSurfacePreprocessor.ShaderPath` が `Packages/com.ddrive.core/...` 決め打ちで git URL 解決時に壊れる** — Unity はパッケージを AssetDatabase 上で常に `Packages/<package name>/` として見せるため、`Library/PackageCache/com.ddrive.core@<hash>` に展開されていても `Packages/com.ddrive.core/Runtime/Shaders/...` は有効なアセットパス。`Editor/Material/AiStandardSurfacePreprocessor.cs:19` は正しい
- **`ControlSkinPreviewSection.DefaultScrollMaterialPath` の決め打ち** — `Editor/Ui/ControlSkinPreviewSection.cs:909-919` で GUID（`541fe40e...`）→ `AssetDatabase.GUIDToAssetPath` 解決に変わっており、フォールバックのパス文字列も新しい場所を指している。docs/44 の指摘どおりに直っている
- **`AddressablesSync.SyncAll` が Compat フィクスチャを Addressables に登録してしまう** — `Editor/AssetBrowser/AddressablesSync.cs:141, 191` の `path.Contains("/Tests/")` で除外される（フィクスチャのパスは `Packages/com.ddrive.core/Tests/Editor/Compat/Fixtures/...`）。`Assets/AddressableAssetsData` を grep しても `CompatFixture` は 0 件
- **`Sync-MirrorDirectory` の robocopy `/MIR` が `Documentation~/AGENTS_CONSUMER.md` や `skills/` を消す** — `bump-version.ps1:229, 231` のミラー先は `Documentation~/DesignerManual` と `Documentation~/ProgrammerManual` というサブフォルダ単位なので、兄弟のファイル・フォルダには触れない
- **`Tools/Release/*.ps1` が BOM 無し UTF-8 で、Windows PowerShell 5.1 が日本語（`'^###\s*互換性'` 等）を化けさせる** — 4 ファイルとも先頭 3 バイトが `EF BB BF`（BOM 付き）であることを確認した。`Tools/CI/*.ps1` は BOM 無しだが、これらは `run-ci.cmd` が `pwsh` を明示的に呼ぶ既存経路（`run-ci.cmd:15-17` のコメントに経緯あり）
- **`bump-version.ps1` の CHANGELOG 再構築（`:214` の `[string]::Join("`r`n", ...)`）が改行コードを変えて全行差分になる** — `CHANGELOG.md` は現状すでに CRLF（`file` コマンドで確認）なので差分は出ない。`.gitattributes` に `CHANGELOG.md` の指定は無いが、`core.autocrlf` の既定でも CRLF のまま
- **`ManifestJson` が `packages-lock.json` を壊す** — `ManifestJson` は `Packages/manifest.json` しか触らない。lock は Unity が再生成する
- **`DDriveProjectSettings` が読み取り専用の場所に保存される** — `[FilePath("ProjectSettings/DDriveProjectSettings.asset", FilePathAttribute.Location.ProjectFolder)]`（`Editor/Settings/DDriveProjectSettings.cs:26`）なので、持ち込み先のプロジェクト直下（書き込み可・コミット対象）。正しい
- **`GeneratedAsmdefWriter` がパッケージ内に asmdef を書こうとする** — `Editor/Codegen/GeneratedAsmdefWriter.cs:56-60` が `Assets/` 配下でなければ何もしない。テストの一時出力先（`Packages/...` や `Path.GetTempPath()`）でも発火しない

### sentinel resolve

- **「呼び出し側が既定値を渡したときだけ設定を引く」方式でテストが壊れる** — `AssetCreationService.ResolveGameDataRoot`（`:28-31`）・`ImportRuleService.ResolveSourceRoot`・`AssetIconService.ResolveIconRoot`・`ScenePreloadGenerator.ResolveOutputRoot`・`AssetIdGenerator.Regenerate`（`:73`）・`TuningCodegen.Regenerate`（`:38`）をすべて確認した。テストはいずれも一時パスを**明示**で渡しており、既定値リテラルを渡しているテストは見つからなかった。開発リポジトリでは設定値 = 既定値なので挙動も不変。ただし「持ち込み先が `GameDataRoot` を変えた状態で、誰かが明示的に `"Assets/GameData"` を渡す」と設定側に飛ぶ非対称は残る（現在そのような呼び出しは無い）
- **`ComputeFolderLayout` の `UnderParentFolder` が `Specs` も `Assets/` 配下に置いてしまう** — `Editor/Setup/ProjectSetupInspector.cs:176-180` で意図的にそうしている（[42] §3.6 の実装メモに「`SpecSnapshotWriter` はリポジトリ直下からの相対として使うため、実際には `Assets/…` 配下に JSON が置かれる形になる」と明記済み）

### ネット（P-8）

- **`ProtocolVersion` 不一致の分岐が `ContentHash` の比較を飛ばすので、Client が「未検証」のまま残る** — `Runtime/Net/CatalogContentHashGate.cs:254-270` は `_pendingHostSideDeadlines.Remove(senderId)` の後に `ApplyOutcome`（既存の `CatalogContentHashPolicy.Decide`）を通しており、切断でない場合は `CatalogContentHashResultMsg{Matched=false}` を返す。既存の不一致経路と同じ扱いで、宙ぶらりんにはならない
- **旧版 Client（`ProtocolVersion` 欠落 = 0）が素通りする** — `msg.ProtocolVersion != DDriveProtocol.Current`（= 1）なので 0 も不一致として弾かれる。設計どおり
- **`NetDebugOverlay` の `DDriveProtocol` 参照が `#if DDRIVE_NGO` の外にある** — `Runtime/Net/NetDebugOverlay.cs:4` でファイル全体が `#if DDRIVE_NGO` に囲まれている

### その他

- **`CI.DiscoverValidators` / `DDriveMigrationRunner.DiscoverDataMigrations` が持ち込み先のゲームコードの実装を拾う** — `TypeCache.GetTypesDerivedFrom<IDataMigration>()` は全ロード済みアセンブリが対象なので拾う。`IDataMigration` は `DDrive.Editor` の型なので、参照できるのは持ち込み先の **Editor asmdef だけ**。「持ち込み先が自分のデータ用マイグレーションを書ける」のは [42] §4.3 の拡張点の考え方と整合しており、意図どおりと判断した（ただし docs に明記されていないので、書いておくとよい）
- **`UpdateActions.Apply` が途中失敗しても `LastAppliedVersion` を進めてしまう** — `Editor/Update/UpdateActions.cs:71-83` は失敗で即 return し `MarkApplied` を呼ばない。`UpdateActionsTests` でも固定されている
- **`RunValidationStep` が Error を無視して成功にする** — `Editor/Update/UpdateStepsFactory.cs:112-116` のコメントどおり、[42] §4.2 手順 5-4 の意図（更新そのものの失敗ではない）に沿っている
- **`SpecAutoSync` の Play Mode ガードが Editor 起動時の自動取得まで止める** — `Editor/Spec/SpecAutoSync.cs:61-65` は `EditorApplication.isPlayingOrWillChangePlaymode` だけを見るので、通常の Editor 起動時のドメインリロードでは止まらない
- **`Documentation~` / `Samples~` / `Tools~` が git URL 解決で消える** — git パッケージは `?path=` 配下をそのままクローンするため、`~` 付きフォルダもファイルシステム上には存在する（`CopyConsumerSkillIfBundled` / `ManualPages` が `resolvedPath` + `Documentation~` で辿れる前提は成立する）。Unity の AssetDatabase から見えないだけ
- **`docs/DesignerManual` と `Documentation~/DesignerManual` がずれている** — HEAD 時点では `diff -rq` で差分なし（28 + 14 ファイル）。P-10 が両方を手で直している

---

## 件数

| 区分 | 件数 |
|---|---|
| P1 | 7（導入 3 / データ 2 / CI・リリース 2） |
| P2 | 9（走査・混入 2 / 更新ツール 4 / PowerShell 1 / 消費側 docs 2） |
| 整理項目 | 20 |
| テストの穴 | 8 |
| 誤検知 | 21 |

## P-11（空プロジェクトで導入）の前に直すべきもの 上位 5 件

1. **P1-1 — asmdef の UniTask / NGO 参照と README 手順の不整合**。これを直さないと P-11 は「Unity を開いた瞬間にコンパイルエラーでメニューが出ない」で終わる。README 手順 1 に依存 3 行 + scoped registry を統合し、NGO は `defineConstraints` 付きの別 asmdef へ切り出す
2. **P1-2 — `ForbiddenApiScanner` が持ち込み先で必ず 26 件 Error**。`Samples~` / `Tests` の除外と `Runtime/` 12 件の処遇（許可リスト or 実装修正）を決める。P-10 の CI テンプレが `ValidateAll` を fail 条件にしているため、放置すると P-11 の「CI が通る」を確認できない
3. **P1-4 + P1-5 — `SchemaVersion` を保存フックが引き上げる／既存データ全件に解消不能な Warning**。P-11 は「版を 1 つ進めて更新手順 → ロールバック」を確認するチケットなので、マイグレーションの前提が壊れたままだと検証にならない。2 件はセットで直す
4. **P1-3 — パッケージ同梱テストが読み取り専用の PackageCache で失敗**。P-11 の AC は「ON / OFF の両方を確認する」([42] §2.1)。一時アセットの置き場所を `Assets/` 配下へ移し、`PackageVersionConsistencyTests` を `PackageInfo` 基準に直す
5. **P1-6 — `run-ci.cmd` の CHANGELOG ガードが常に `[OK]`**（1 文字の修正）。あわせて P2-6（`-GuardOnly` から version 検査を外す）も直さないと、修正した途端に日々のコミットで CI が落ちる

## 変更履歴

- 2026-09-20: 新規作成。P-10.5 として `4bb8142`〜`9e5f23c`（P-3〜P-10 の 7 コミット）を読み取り専用レビュー（Unity MCP 未接続のためコンパイル・テスト実行なし。HEAD == `origin/main` == `9e5f23c`、作業ツリー clean）。
