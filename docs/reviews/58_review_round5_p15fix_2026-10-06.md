# 58. 2026-10-06 自前レビュー結果（修正ラウンド 5 = docs/57 の指摘 FZ-R-01〜12 への対応 + P-15 の人による確認で見つかった BUG-1 / Q-1〜Q-4 への対応）

> **対象**: 2026-10-05〜06 に main へ入った 2 つの PR。どちらも Sonnet のサブエージェントが実装し、まとめ役は差分を読まずにマージした。**v1.4.0 のタグ前の最後のコードレビューの予定**。
>
> | マージ | PR | 内容 |
> |---|---|---|
> | `6d42329` | #110 | 修正ラウンド 5（[57](57_review_round4_m4_2026-10-05.md) の対応）: FZ-R-01（MS2026 への時間の案内の書き直し）・FZ-R-02（設定の許可リストのパスを区切り単位に）・FZ-R-03（`ForbiddenApiWindow`）・FZ-R-04（設定画面）・FZ-R-05（D-Drive 自身の当たり 12 件 → 許可コメント 11 + 言い換え 1）・FZ-R-06（§5.9 の契約の追記）・FZ-R-07（`PresentationManager` の「自分が止められたら残りを発火しない」）・FZ-R-10（受信した Cutscene の開始位置のログ） |
> | `9020727` | #111 | P-15 の人による確認（[43](../verification/43_manual_verification_2026-09-17.md) §15）の対応: BUG-1（git の出力を UTF-8 で読む・`DdriveUpdateDeclaration.TryParse` / `IsUnreadable`）・Q-1（再追加の文）・Q-2（依存の表示の出し分け）・Q-3（`PackageDependencyValidator` をアセットに紐付けない）・Q-4（実行順の検査の除外の拡張点 `ICameraExecutionOrderExemptionProvider`・契約 E-23）・docs/43 §15 への結果の記入 |
>
> **方法**: 専用 worktree を `9020727`（detached → 本ブランチ）に合わせ、`git diff 6d42329^1 6d42329`（32 ファイル）・`git diff 9020727^1 9020727`（38 ファイル）・`git diff 9f40cbb..9020727 -- …/Compat/Snapshots/` と、変更後のファイル全体（`ForbiddenApiScanner`（全 742 行）・`ForbiddenApiWindow`・`ForbiddenApiAllowSettingsProvider`・`CameraExecutionOrderExemptionSettingsProvider`・`CameraExecutionOrderExemptions`・`CameraExecutionOrderValidator`・`CI`（`RunValidation` / `ResolveForbiddenApiScanRoot` / JUnit）・`DataValidationSection`・`PackageDependencyValidator`・`PackageDependencyChecker`・`UpdatePreflight`・`InstalledPackages`・`ManagedPackageRows.DescribeDependency`・`UpdateWindow`（行の表示・再追加）・`GitProcess`・`GitPackageJsonFetcher`・`EditorContractSnapshotBuilder`・`DDriveProjectSettings`、`PresentationManager`（`PlayLocalInternal` / `SignalLocal` / `ApplySignal` / `CancelInternal` / `Seek` / `Tick` / `Complete` / `Cleanup` / `FireDueTracks` / `SeekInitialTracks` / `FireTrack`）、`CutsceneManager.OnReceivePlayMsgInternal`、`GameLoop` / `GameLoopDriver` / `TimeService` / `PauseService` / `IAssetManager` / `ITimeSource` / `DDriveRuntimeBootstrap`（`Instance` / `Loop` / `Awake` / `OnDestroy`）、許可コメントを足した 6 ファイル、`AnimManager` の Pause、追加・変更テスト（`ForbiddenApiAllowanceTests`・`PresentationTickReentrancyTests`・`P15VerificationFixTests`・`CameraExecutionOrderExemptionTests`・`ExternalContractCameraExemptionTests` とダミー・`DdriveUpdateFormatCompatTests`）、`Tools/CI/run-ci.cmd`・`Tools/Release/{check-release,bump-version}.ps1`、`ProjectSettings/DDriveProjectSettings.asset`）を**読むだけ**で確認した。D-Drive 自身の禁止 API の当たりは、`ForbiddenApiScanner` と同じ規則・同じ除外を grep で写して数えた（Unity は使っていない）。P-15 の確認結果は報告の原文（scratchpad の `p15_verification_report_2026-10-06.md`）と docs/43 §15 を突き合わせた。実装者の報告（[57] の「→ 対応」・[11] M-4 / P-15 節・CHANGELOG・docs/42 §4.2.1 / §5.9 / §5.14 E-23・docs/43 §15・docs/51 §7.1）は信用せず、コードと突き合わせた。**Unity は起動しておらず、コンパイル・EditMode / PlayMode テスト・`CI.ValidateAll`・`run-ci.cmd`・実 git・実ネットワークは一切実行していない**（対応記録の「green」「実機で確認」は未確認）。指摘はコードを読んで確認した事実か、Unity / .NET / Newtonsoft.Json / R3 / git の挙動についての推定で、推定のものは「確度」欄に**推定**と書いた。
>
> 前提として読んだもの: `CLAUDE.md`（§0）、[docs/12](../12_review.md) §3 / §7、[docs/42](../42_distribution.md) §4.2.1・§5（§5.8・§5.9・§5.14 E-23）、[57](57_review_round4_m4_2026-10-05.md)（元の指摘と対応記録。書式と重大度の基準）、[docs/11](../11_tasks.md) M-4 節・P-15 行、[docs/43](../verification/43_manual_verification_2026-09-17.md) §15・§17、[docs/26](../26_timeline.md) §4.6.5、[docs/51](../51_tdrive_integration.md) §7.1、[docs/52](../verification/52_manual_verification_fc.md)、[docs/02](../02_core_framework.md) §10、`CHANGELOG.md` の `[Unreleased]`。

## 総評

- **リリースを止める実バグ（P1）は見つからなかった**。互換面は v1.3.1 から見て**追加のみ**のまま（`git diff 9f40cbb..9020727 -- …/Compat/Snapshots/` は 4 ファイル `+127 / −0`、削除・変更行 0 件。本 2 PR で増えたのは `editor-contract.txt` の 7 行だけ）。ネットメッセージの形式・送信回数・`DDriveProtocol` は不変。
- **FZ-R-05（挙動は変えない約束）は守られている**: 12 か所の差分は、追加された 11 行がすべて `//` コメントだけの行、残り 1 行（`NgoNetBridge.cs:127`）は行末コメントの文言だけの変更で、コードの文字は 1 つも変わっていない。各許可コメントは当たりの行の 1 行上（コメントだけの行）にあり、当たりはどれも 1 行 1 規則。同じ規則・同じ除外で数え直すと**許可されていない当たり 0 件・許可 11 件**。理由はすべて実コードと合っている（下の表。文言の細部 1 件のみ）。`ThisPackage_HasNoForbiddenApiViolations` は `IsDevelopmentRepo` でなければ `Assert.Ignore` するので、持ち込み先でテストを有効にしても落ちない。
- **FZ-R-01（MS2026 への案内）は「方向」は正しく、6 か所の内容もほぼ一致**。`IAssetManager` を `DDriveRuntimeBootstrap.Instance.Loop.GameLoop.Register` で登録すれば `Tick(dt)` に `Time.unscaledDeltaTime × TimeService.TimeScale`（HitStop 込み）が届き、`AssetType.None` でよい（`GameLoop` は `Type` を見ない・同じ `Type` の二重登録も問題ない）。(b) の式も `GameLoopDriver.Update` と一致し、API はすべて public。`Time` 規則の対象外の主張も正規表現と一致。ただし**案内のとおりに書くと、シーンを跨いだときに壊れる穴が 1 つある**: `GameLoop` は登録解除を自動でせず、Tick に try/catch も無いのに、案内に「破棄時に `Unregister` する」「`Instance` が null のとき」が書かれていない（**GA-R-01、P2**。返答を送る前に 1 文足せば済む）。
- **M-4 の残り（FZ-R-02 / 03 / 04 / 06）は解消**。パスの一致は docs/42 §5.9 (12) の規則どおりで、無効な要素が黙って効く経路は無い（黙って「効かない」経路は、走査ルートの外を指す要素と未使用の要素で、どちらも安全側）。
- **FZ-R-07 は正しい**（止められていないのに残りを飛ばす誤判定は無い。`OnCompleted` / 中止通知は 1 回）。残りは実装者も報告済みの `SignalLocal` / `ApplySignal`（`OnSignal` 経由）のループで、v1.3.1 からの同形の取り残しが残る（**GA-R-02、P3**）。
- **BUG-1 の修正は正しい**: git を起動する経路は `BuildStartInfo` の 1 か所だけになった（`taskkill` / `pgrep` / `kill` は git ではない）。「読めない」= Warning は安全側で、正常な package.json で誤警告を出す経路は BOM 付きの package.json（**推定**）くらい（**GA-R-06、P3**）。docs/42 §4.2.1 の拡張規則 3 は「`ddriveUpdate` 自体が null 以外のオブジェクトでないときは読めなかった扱い」に書き換えられていて、**v1.4.0 が `ddriveUpdate` を読む最初の版**（v1.3.1 には無い）なので旧版との相互運用の問題は無い。ただし固定のためのテスト `DdriveUpdateItself_NotAnObject_IsEmpty` は今も `Parse`（互換用）を見ていて、実際の経路（`TryParse`）を固定していない（**GA-R-05、P3**）。
- **Q-4（新しい拡張点）は T-Drive が使える**: ブリッジの Editor アセンブリから `typeof(FacialCorrectionRunner)` を渡せば、検査が使う `MonoScript.GetClass().FullName` と一致して除外される。Applier 自身の Error は除外できない（判定の順序で確認）。ただし**契約の柱である 2 つのコンストラクタがスナップショットに入っていない**（docs/42 §5.14 E-23 は「シグネチャを固定する」と書いている。**GA-R-03、P3**）。
- **Q-1 は承認された決定と文面が違う**: 決定は「ウィンドウに『manifest に同じ URL の … があります。管理対象に登録しました』を出す（確認項目の期待は変えない）」だが、実装は既に管理対象のものを再追加したとき（= 15-5 の手順そのもの）に「<ID> は既に管理対象に登録されています(manifest は変わりません)。」を出し、docs/43 15-5 の期待も書き換えた。文面としてはむしろ正確だが、ユーザーの確認が要る（**GA-R-04、P3**）。
- **docs/43 §15 の記入は報告の原文と一致**（結果を作っていない。15-1〜15-24 の結果・メモ・「確認中」を「未確認」と書いた点まで原文どおり。修正で期待が変わった行に「要再確認」を足し、新しい確認 15-25〜15-30 は `□ 未`）。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **0** | – |
| P2（直すべき不具合・設計上の穴） | **1** | GA-R-01 |
| P3（整理・改善） | **11** | GA-R-02〜12 |

---

## FZ-R-01〜12 の解消確認

「解消」= 元の失敗の筋書きが起きなくなり、対応記録が実装と一致し、別の経路を壊していない。

| 指摘 | 判定 | 根拠（ファイル:行） | 補足 |
|---|---|---|---|
| FZ-R-01 `ITimeSource` の案内 | **解消**（1 点の追記が要る = GA-R-01） | `docs/11_tasks.md:386-387`、`docs/50_consumer_guide/operation.html:105`、`Documentation~/…/common-warnings.md:29`、`Documentation~/AGENTS_CONSUMER.md:12`、`docs/12_review.md:36`、`docs/ProgrammerManual/rules.html:20,38`、`Editor/Validation/ForbiddenApiScanner.cs:33` | (a)(b) と実時間 API の主張は実コードで成立（下の「MS2026 への案内」）。6 か所の食い違いは小さなもの 3 点（同表） |
| FZ-R-02 パスの前方一致 | **解消** | `ForbiddenApiScanner.cs:614-669`（`NormalizeEntryPath` / `EntryPathMatches`）・`:566-608`（走査ルートそのもの / 親は無効）・`:521-551` | 下の「FZ-R-02 の照合規則の確認」 |
| FZ-R-03 Editor の当たりの一覧 | **解消**（細部 = GA-R-08） | `Editor/Validation/ForbiddenApiWindow.cs`（`DDriveMenu.Validation + "禁止 API の検査"`、`ScrollView` に 3 つの `Foldout`、`OpenAt` は `MonoScript` → `OpenAsset(line)`、無ければ `OpenFileAtLineExternal`） | `Run All` に混ぜていない（`CI.RunAll` は `RunValidation` だけ）。`Violation` の追加欄は `DDrive.Editor`（互換面の外） |
| FZ-R-04 設定画面 | **解消**（推定は外れ、実装者が実機で確認。細部 = GA-R-09） | `ForbiddenApiAllowSettingsProvider.cs:33-90`、`ProjectSettings/DDriveProjectSettings.asset` の `m_ObjectHideFlags: 53`（= HideInHierarchy + DontSave、`NotEditable`(8) を含まない） | `activateHandler` で 1 回 `SerializedObject`、変更時 `Undo.RecordObject` → `ApplyModifiedProperties` → `Save(true)` |
| FZ-R-05 D-Drive 自身の 12 件 | **解消**（挙動の変更なし） | 下の「D-Drive 自身の許可コメント 11 件」 | grep の写しで許可されていない当たり 0 件。`ThisPackage_HasNoForbiddenApiViolations` / `ThisPackage_HasNoAllowNotices` は開発リポジトリ専用（`Assert.Ignore`） |
| FZ-R-06 契約の追記 | **解消** | `docs/42_distribution.md:599`（(8)〜(13)）、テスト 4 件（`TextAfterTheClosingParenthesis_IsIgnored` 等） | 実装の挙動は変えていない（パーサの差分なし。`FindLineCommentStart` / `ParseDirectives` は [57] 時点と同じ） |
| FZ-R-07 Presentation の残りのトラック | **解消**（`OnSignal` 経由の残り = GA-R-02） | `PresentationManager.cs:1540-1547`・`:1592-1598`・`:1686-1694` | 下の「FZ-R-07 の確認」 |
| FZ-R-08 規則の実体の文書化 | **解消** | `docs/42` §5.9 (13)、`rules.html:26` | `Time.fixedDeltaTime` / `realtimeSinceStartup(AsDouble)` / `unscaledTimeAsDouble` / `timeScale` が当たらないのは正規表現 `\bTime\.(time|deltaTime|unscaledDeltaTime|timeAsDouble|unscaledTime)\b` で確認（`timeScale` は `time` の直後が単語文字なので `\b` で外れる） |
| FZ-R-09 報告の細部 | **一部対応**（記録どおり） | `ForbiddenApiScanner.cs:224-226` のコメント | 見送り分（それらしい形の Warning・束ね・設定の未使用 Info）は記録どおり |
| FZ-R-10 開始位置のログ | **解消** | `CutsceneManager.cs:1129-1138` | `#if DEVELOPMENT_BUILD \|\| UNITY_EDITOR`。最初の `Tick` の前なのでカーソルの合計 = 無音にした数、は正しい（`SkipMarkersOlderThan` だけがカーソルを進める）。`_netBridge` はこの経路では非 null（直前で `NetworkTime` を読んでいる） |
| FZ-R-11 予測再生キーの上限 | **見送り**（記録どおり） | – | – |
| FZ-R-12 文書の食い違い | **解消** | `docs/11_tasks.md:367`、`CLAUDE.md` §0-3、`docs/14_networking.md:1181` | `AGENTS_CONSUMER.md` の規則の列挙は 3 つのまま（GA-R-12） |

## BUG-1 / Q-1〜Q-4 の解消確認

| 項目 | 判定 | 根拠（ファイル:行） | 補足 |
|---|---|---|---|
| BUG-1 (1) UTF-8 | **解消** | `Editor/Update/GitProcess.cs:89-118`（`StandardOutputEncoding` / `StandardErrorEncoding` = `UTF8Encoding(false)`）・`:126`（`Run` は必ず `BuildStartInfo`） | git を起動するのは `GitCliTagLister.cs:41`・`GitPackageJsonFetcher.cs:79,93` の 3 か所で、すべて `GitProcess.Run`。他の `new ProcessStartInfo`（`:266` taskkill・`:354` pgrep / kill）は git ではない。判定は終了コードだけで stderr の文字列を見ないので、stderr が化けても判定は変わらない（理由の表示だけ） |
| BUG-1 (2) 宣言なし / 読めない | **解消**（細部 = GA-R-05 / 06） | `PackageDependencyChecker.cs:31-105`（`Unreadable` / `TryParse`）・`:243-253`（Warning `DD-PKGDEP-BAD-DECLARATION`）、`UpdatePreflight.cs:87-99`、`InstalledPackages.cs:52-66` | 境界: 空文字・空白 → 読めない / 壊れた JSON・根が配列 → 読めない / `ddriveUpdate` 無し・`null` → 読める（宣言なし）/ 文字列・配列・数値・真偽 → 読めない / `requires` が文字列・中身が数値 → 読める（その項目は無視 = 規則 3）。コメント付き JSON・末尾カンマは Json.NET が受け付ける（**推定**）ので読める。`Parse` は `Unreadable` → `Empty` で従来どおり |
| BUG-1 (3) テスト | **解消**（環境依存の細部 = GA-R-06） | `P15VerificationFixTests.cs:40-99`（実 git は `Assume.That(probe.Success)`、一時フォルダは `finally` で属性を戻して削除）・`:101-218` | 事前確認の 4 経路（日本語・壊れた JSON・宣言なし・読めない + 他パッケージの照合）を偽の fetcher で固定 |
| Q-1 再追加の文 | **実装はされたが文面が承認と違う** | `PackageAddPlanner.cs:66,80`（過去形）・`UpdateWindow.cs:562-575`（既に管理対象なら別の文） | GA-R-04 |
| Q-2 依存の表示 | **解消** | `ManagedPackageRows.cs:116-207`、`UpdateWindow.cs:466-476` | 下の「Q-2 の全分岐」 |
| Q-3 アセットに紐付けない | **解消**（同じ問題が他の Validator に残る = GA-R-07） | `CI.cs:129-176`（`IsProjectScoped` → `asset = null` で 1 回）、`DataValidationSection.cs:171-173`（個別検証から除外） | 下の「Q-3 の呼び出し元ごとの確認」 |
| Q-4 実行順の検査の除外 | **解消**（契約の固定の穴 = GA-R-03、細部 = GA-R-10） | `CameraExecutionOrderExemptions.cs`、`CameraExecutionOrderValidator.cs:84-119`、`DDriveProjectSettings.cs:90-100`、`EditorContractSnapshotBuilder.cs:78-81` | 下の「Q-4 の確認」 |
| docs/43 §15 の記入 | **原文と一致** | `docs/verification/43_manual_verification_2026-09-17.md:338-382` | 15-2・15-14 に「D-Drive を git URL で入れたプロジェクトで確認」を足したのは原文の「確認不可」の理由と矛盾しない |

---

## D-Drive 自身の許可コメント 11 件（+ 言い換え 1 件）の妥当性

差分は 12 か所とも「コメントだけの行の追加」または「行末コメントの文言の変更」で、コードの文字の変更は 0。位置はすべて当たりの行の 1 行上のコメントだけの行（`ForbiddenApiScanner.cs:315-321` の `prev` に入り、次の非コメント行で使われる）。行番号は `9020727`。

| # | 場所（当たりの行） | 規則 | 理由（要約） | 事実か | 判定 |
|---|---|---|---|---|---|
| 1 | `Runtime/Anim2D/Anim2DFacing.cs:128` | Time | GameLoop に載らない単独の MonoBehaviour。Animator と同じ Unity の timeScale に従う見た目の補間で、HitStop には揃えない | 事実（`GameLoop` に登録されず `Update` で `Time.deltaTime`。`AnimManager` は HitStop を Animator に掛けない＝`AnimManager.cs:290,479` はポーズだけ）。補足: D-Drive のポーズ（`PauseChannel`）中は Animator の `speed = 0` だがこの補間は進む。入力（`SetWorldDirection`）もポーズ中は止まるのが普通なので実害は無い | **妥当** |
| 2 | `Runtime/Loop/DDriveRuntimeBootstrap.cs:714` | AddressablesLoad | 起動配線: `IAssetLoader` が組み上がる前にラベルでカタログを列挙する唯一の箇所 | 事実（`RegisterCatalogsAsync`。他に `Addressables.Load*` は `AddressablesAssetLoader.cs` だけ） | **妥当** |
| 3 | 同 `:723` | AddressablesLoad | 同上（カタログを読み込む） | 事実 | **妥当** |
| 4 | 同 `:802` | Time | CameraFx は HitStop 中も揺れを止めないため unscaled で駆動（docs/16 Part A） | 事実（`UnscaledCameraFxAdapter.Tick` は渡された `dt` を捨てて `unscaledDeltaTime`。`docs/16_camera_haptics.md:176-183` の決定どおり） | **妥当** |
| 5 | `Runtime/Ngo/NetDebugOverlay.cs:116` | Time | デバッグ表示の受信レートを実時間の 1 秒窓で数える | 事実 | **妥当** |
| 6 | `Runtime/Ngo/NgoNetBridge.cs:127`（行末コメントの言い換え） | – | `Time.time` の文字列をコメントから消した | コードは `public float ReleaseAtTime;` のまま | **妥当** |
| 7 | 同 `:214` | Time | 開発ビルド専用の擬似遅延キュー（`-ddrive-sim-latency`）。元の `UniTask.Delay` の既定と同じスケール済み時間 | 事実（`_appLayerSimLatencyMs = Debug.isDebugBuild && simLatencyMs > 0 ? … : 0`、`:298`。Editor でも `isDebugBuild` は真なので「開発ビルド / Editor」。`UniTask.Delay` の既定は `DelayType.DeltaTime`・timeScale に従う = スケール済み） | **妥当** |
| 8 | 同 `:439` | Time | 同上 | 同上 | **妥当** |
| 9 | 同 `:588` | Time | 同上 | 同上 | **妥当** |
| 10 | 同 `:704` | Time | 同上 | 同上 | **妥当** |
| 11 | `Runtime/Ui/UiButton.cs:192` | Time | UI の押下アニメ・長押し判定はポーズ中も反応させるため実時間 | 事実（`Advance(unscaledDt)` が `_heldSec`・LongPress / Repeat・見た目を進める） | **妥当** |
| 12 | `Runtime/Ui/UiSlider.cs:365` | Time | 同上（UiButton と同文） | 実時間で進めるのは正しいが、UiSlider に「長押し判定」は無い（実際はパッド入力のリピート・スロットル・値の追従アニメ・ノッチ SE の間隔、`:344-362`） | **妥当**（理由の文言だけ不正確。直すなら「UI のリピート・追従アニメはポーズ中も実時間で進める」） |

**直すべきバグが許可で隠れているもの: なし**。4 の CameraFx・7〜10 の擬似遅延は「スケール済みか実時間か」の選択がコメント・docs に記録された設計で、許可はそれを変えない。

→ **対応（2026-10-06、修正ラウンド 6）**: 許可コメントの理由 2 件を実態に合わせた（コメントのみ。`UiSlider.cs` = 「UI のパッド入力のリピート・値の追従アニメ・ノッチ SE の間隔はポーズ中も反応させるため実時間で進める」、`Anim2DFacing.cs` = 「…D-Drive の HitStop には揃えず、D-Drive のポーズ中も補間は進む。入力が止まるので実害は無い」。理由の中に括弧を使うと閉じ括弧で切れるので使っていない）。

---

## MS2026 への案内（FZ-R-01）の確認

### 実コードでの成立（(a)〜(d)）

- **(a) `IAssetManager` を登録すれば `Tick(dt)` に HitStop 込みの dt が届く → 成立**。
  - `IAssetManager`（`Foundation/Manager/IAssetManager.cs`）のメンバーは `Type` / `Tick(float)` / `OnPause(PauseChannel, bool)` / `StopAll(StopReason)` / `OnSceneUnload()` の 5 つ。持ち込み先は 5 つとも実装する必要がある（`StopAll` / `OnSceneUnload` は空でよい）。
  - `GameLoop.Register`（`Foundation/Manager/GameLoop.cs:11-17`）は `List` に追加するだけで、`Type` を見ない。**同じ `AssetType`（`None`）の二重登録も問題ない**（同じインスタンスの二重登録だけ `Contains` で弾く）。`AssetType.None = 0` は実在。
  - **順序**: D-Drive 自身の Manager は `DDriveRuntimeBootstrap.Awake → Build` で先に登録されるので、持ち込み先の Manager は**常に D-Drive の後**に Tick される（同じフレームで D-Drive の Presentation が HitStop を掛けた場合、その Tick の dt はまだ掛かる前の値。次のフレームから 0 / スロー）。
  - **dt**: `GameLoopDriver.Update`（`Runtime/Loop/GameLoopDriver.cs:19-24`）が `TimeService.Tick(Time.unscaledDeltaTime)` → `GameLoop.Tick(TimeService.ScaledDeltaTime(unscaledDt))`。Unity の `Time.timeScale` は掛からない（`unscaledDeltaTime` 起点）。
  - **ポーズ**: `PauseService` の変化は `GameLoop.BroadcastPause` で全 Manager の `OnPause(channel, paused)` に届くが、**Tick はポーズ中も呼ばれ、dt は 0 にならない**（各 Manager が `OnPause` を受けて自分で止める設計）。案内の「ポーズは `OnPause` で受ける」は正しい。「dt が 0 になる」とは書いていないので誤りではない。
  - **登録解除・null**: `GameLoop.Unregister` はあるが、**自動では外れない**。`DDriveRuntimeBootstrap.Instance` は `[DefaultExecutionOrder(-1000)]` の `Awake` で入るので他の `Awake` からは見えるが、Bootstrap の無いシーン（テスト用シーン等）では null。→ GA-R-01。
  - **ネットのクライアント**: `GameLoopDriver` / `TimeService` は端末ごとにあり、HitStop は Presentation のトラックが各端末で発火したときに各端末の `TimeService` に掛かる。Host / Client で同じ。
  - 実装者の「登録 API を実行するテストは足していない」は事実（テストは無い）。
- **(b) `Time.unscaledDeltaTime * Loop.TimeService.TimeScale`、`Loop.PauseService.IsPaused(PauseChannel.Gameplay)` → 成立**。`TimeService.ScaledDeltaTime` と同じ式で、`TimeService` / `TimeScale`（public get）・`PauseService.IsPaused`（public）・`PauseChannel.Gameplay` は実在。差は実行順だけ: 持ち込み先の `Update` が `GameLoopDriver.Update`（既定の実行順 0）より先に走ると、HitStop の開始 / 終了のフレームで `TimeScale` が 1 フレーム前の値になる（実害は 1 フレーム）。
- **(c) `Time.realtimeSinceStartupAsDouble` / `Stopwatch` は対象外 → 正規表現と一致**。`Time.realtimeSinceStartup`（AsDouble でない方）・`Time.unscaledTimeAsDouble` も当たらない（`unscaledTime` の直後が単語文字で `\b` が成り立たない）。同じ理由で `Time.timeSinceLevelLoad` / `fixedTime` / `smoothDeltaTime` も当たらない（ゲームプレイの時間をこれらで書くと検査をすり抜ける。v1.3.1 から同じ規則の実体で、§5.9 (13) に「当たらない」側として一部載っている）。
- **(d) 判断基準の表 → API 名は実在**: `Prefabs.Spawn(PrefabAssetId, …)`（`Runtime/Prefab/Prefabs.cs:17,20`）、`PoolService`（`Foundation/Pool/PoolService.cs`、`DDriveRuntimeBootstrap.Instance.Pool`）、`IAssetLoader`、`Audio`。NGO の `NetworkObject` の Instantiate → Spawn は許可、は D-Drive の規則（`Instantiate` 規則）と矛盾しない。

### 6 か所の一致

| 場所 | (a) Tick に乗せる | (b) 時間源 1 か所 + 許可 | `ITimeSource` はゲーム向けでない | 実時間は対象外 API | Instantiate の判断 | 食い違い |
|---|---|---|---|---|---|---|
| 返答文（`docs/11_tasks.md:384-391`） | ○ | ○ | ○ | ○ | ○（NGO 許可 / 他は `Prefabs.Spawn`・`PoolService`） | なし |
| 運用ページ（`docs/50_consumer_guide/operation.html:105-108`） | ○ | ○ | ○ | ○ | ○ | なし |
| 消費側スキル（`…/references/common-warnings.md:29`） | ○ | ○ | ○ | ○ | ○ | なし |
| AGENTS（`Documentation~/AGENTS_CONSUMER.md:12`） | ○（要約） | ○（要約） | ○ | ○ | ○（NGO の例） | 冒頭の「検出するもの」が `Instantiate` / `Resources.Load` / `AudioSource.Play` の 3 つのまま（`Time` / `Addressables.Load*` が無い。CLAUDE.md §0-3 は直した） |
| ProgrammerManual（`docs/ProgrammerManual/rules.html:20,38`） | ○ | ○（本文） | ○ | ○ | ○ | 表の行は (a) と実時間だけ（本文で (b) を補足）。パッケージ同梱の `Documentation~/ProgrammerManual/rules.html:20` は旧文（`ITimeSource`）のままで、`bump-version.ps1` の同期で置き換わる |
| スキャナのメッセージ（`ForbiddenApiScanner.cs:33`） | ○ | –（「理由を書いて許可」で代替） | 言及なし（問題なし） | ○ | – | 参照先の `[../02_core_framework.md] §10` は Pause / TimeService の節で、持ち込み先が `IAssetManager` を登録する方法は書いていない |
| （参考）docs/12 §3（`:36`） | ○ | ○ | ○ | ○ | ○ | なし |

**6 か所に共通の抜け**: 「シーンを跨ぐ・破棄されるときに `GameLoop.Unregister` する」「`DDriveRuntimeBootstrap.Instance` が null なら何もしない」（GA-R-01）。

---

## FZ-R-02 の照合規則の確認（docs/42 §5.9 (12) と実装）

| 入力 | 規則 | 実装 | 一致 |
|---|---|---|---|
| `\` と `/` | 同一視 | `NormalizeEntryPath` の `Replace('\\','/')`、走査側も `file.Replace` | ○ |
| 大文字小文字 | 区別しない | `EntryPathMatches` の `OrdinalIgnoreCase`、走査ルートとの比較も同じ | ○ |
| 先頭の `./`・連続 `/`・末尾 `/` | 無視 | 空の要素と `.` を捨てて `/` でつなぎ直す（`:624-641`）。走査側の先頭 `./` は `:658-661` | ○ |
| 区切り単位 | 完全一致 or `<要素>/…` | `:663-668`（`Assets/Foo` は `Assets/FooBar/x.cs` に当たらない、`Gen.cs` は `Gen.cs.bak.cs` に当たらない） | ○ |
| 1 階層（`Assets` 等） | 無効 + Warning | `kept.Count < 2`（`:643-647`） | ○ |
| 絶対パス（`/…`・`C:…`・UNC） | 無効 + Warning | `:618-622`（UNC は `\\` → `//` で `/` 始まり） | ○ |
| `..` | 無効 + Warning | `:634-638` | ○ |
| 走査ルートそのもの・親 | 無効 + Warning | `:583-589`（開発リポジトリは `packageInfo.resolvedPath`〔絶対〕を `ToProjectRelative` で `Packages/com.ddrive.core` に、持ち込み先は `Assets`） | ○ |
| パッケージ内のパス | 開発リポジトリでは効く、持ち込み先では走査されないので効かない | 走査ファイルは `relative`（プロジェクト相対）と `normalized` の両方で照合（`:680`） | ○（持ち込み先で `Packages/…` を書いても何にも当たらず、Warning も出ない = 安全側だが黙る） |
| 無効な要素が黙って効く経路 | 無いこと | 無効な要素は `ResolveSettingsEntries` で捨てられ Notice が積まれる（`:538-545`）。`ProjectSetupValidator` と設定画面も同じ走査ルートで判定（`ProjectSetupValidator.cs:133`・設定画面 `:83-89`） | ○ |

黙って**効かない**経路: 走査ルートの外を指す要素・どのファイルにも当たらない要素（未使用の Info は FZ-R-09 で見送り）。どちらも当たりが Error のまま残るので安全側。2 階層の `Assets/Scripts` のような「ゲームコード全体」の指定は有効になる（規則どおり。理由必須とウィンドウの「許可済み」で見える）。

---

## FZ-R-07 の確認

- **位置と条件**: `FireDueTracks` / `SeekInitialTracks` は各 `FireTrack` の直後に `_instances.IsValidSilent(handle)`（世代つき）を見て、無効なら残りを呼ばずに返る。`FireTrack` の末尾も `TrackFiredSubject.OnNext` の前で同じ確認。止められていなければ常に有効なので**残りを飛ばす誤判定は無い**（他の Presentation を止めても自分の Handle は有効のまま）。
- **完了 / 中止通知**: 購読者が自分を `Cancel` → `CancelInternal` が `Done = true` → `CancelledSubject.OnNext` → `Cleanup`。戻った `Tick` は `!instance.Done` で `Complete` に進まないので、`OnCompleted` は出ず、中止通知は 1 回。`Seek`（デバッグ）も `FireDueTracks` を通るので同じ保護が効く。ループ再生の仕組みは Presentation には無い（`Tick` は尺で `Complete`）。
- **ネット**: `Broadcast` / 受信 / Cancel の送信には触れていない。受信した Signal（`ApplySignal`）は保護の外（GA-R-02）。
- **R3 の根拠**: 「Dispose 後の `OnNext` で `ObjectDisposedException`」は実装者がテストで観測したと記録している（本レビューでは R3 のソースを確認していない = **推定のまま**）。どちらであっても、`FireTrack` 末尾の確認で `OnNext` は呼ばれなくなるので結論は変わらない。
- **`SeekInitialTracks` 側のテストが無い点**: 購読者は `Play` の戻り値の Handle が無いと購読できないので、`Marker` 経由では再現できない、という説明は正しい。再現できるのは `PlayContext.OnSignal`（Kind = Signal の AtTime 0 トラック）の中から `StopAll` / `CancelAllNetworked` などで自分を止める場合だけで、まれ。コードは `FireDueTracks` と同じ形なので、テストなしは許容範囲。
- **CHANGELOG**: 「挙動の変更(v1.3.1 からの不具合修正)」の項（`CHANGELOG.md` の FZ-R-07 の行）は実装どおり（`OnSignal` 経由のループは変更なし、と明記）。

## Q-2 の全分岐（`ManagedPackageRows.DescribeDependency`）

| 状況 | 宣言した側の行 | 相手側の行 | 確認 |
|---|---|---|---|
| 問題なし | 依存 OK | 依存 OK | ○ |
| `requires` 未導入 | ✗ 依存を満たしていません（X vA 以降が必要、未導入） | （相手の行が無い） | ○ |
| `requires` 古い | ✗ …（X vA 以降が必要、現在 vB） | ℹ <宣言側> が vA 以降を要求しています（現在 vB） | ○ |
| `compatibleWith` 古い | ⚠ 依存に注意（X vA 以降に対応、現在 vB） | ℹ <宣言側> が vA 以降を想定しています（現在 vB） | ○ |
| MAJOR 差 | ℹ 確認事項あり（X の MAJOR が上がっています） | ℹ <宣言側> が宣言しているのは vA 以降です（現在 vB。MAJOR が…） | ○ |
| 版の文字列が読めない（`TargetId` あり） | ⚠ 依存に注意（X の宣言を読めません） | 出さない（`CodeBadDeclaration` を除外） | ○ |
| package.json が読めない（`TargetId` 空） | ⚠ 依存に注意（package.json を読めません） | – | ○ |
| 相手側が複数 | – | 最初の 1 件 +（ほか N 件） | ○ |
| 両方 | 「 / 」でつなぐ | ○ | ○ |
| 宣言した側に問題が複数 | 最も重い 1 件だけ（件数は出ない） | – | 小さな情報の欠け（GA-R-10 の補足） |

## Q-3 の呼び出し元ごとの確認

| 呼び出し元 | `PackageDependencyValidator` の扱い | 二重報告 / 漏れ |
|---|---|---|
| `Validation > Run All`（`CI.RunAll` → `RunValidation()`） | `RunAll` から外し、`asset = null` で 1 回（`CI.cs:140-166`） | なし（`_reportedForCtx` も別の `ValidationContext`） |
| `CI.ValidateAll`（バッチ・JUnit） | 同上。JUnit の classname は `(project)` | なし |
| 更新ウィンドウの「Validation」段（`UpdateStepsFactory.cs:97`） | 同上（件数だけ数える） | なし |
| SpecWeb 送信（`RunValidation(includeProjectWideValidators: false)`） | `IsProjectWide` に名前を足したので**新たに除外**された（以前は最初の Data に紐付いて走っていた） | 漏れなし（SpecWeb は Error しか使わず、この Validator は Warning / Info だけ） |
| 個別検証（`DataValidationRunner`） | `ProjectWideValidatorNames` で除外 | なし |
| テスト | `PackagesProvider` を差し替えて `CI.RunValidation()` を実行 | – |

`(unknown)` → `(project)` の表示変更: `Tools/CI/Summarize-Results.ps1`・`run-ci.cmd`・`check-release.ps1` に `(unknown)` を見る箇所は無い（grep）。持ち込み先の CI が JUnit の classname で絞り込んでいれば影響するが、MS2026 の CI は本レビューでは見ていない。

## Q-4 の確認

- **公開メンバー**: インターフェースは `GetExemptions()` の 1 つ、struct は `Type` / `TypeName` / `Reason` と 2 つのコンストラクタ。最小。
- **2 通りの指定の一致**: `Type` 指定は `Type.FullName`（入れ子は `Outer+Inner`）を、文字列指定はそのまま、`+` → `.` に揃えて検査側の `MonoScript.GetClass().FullName` と照合（`CameraExecutionOrderExemptions.cs:221`、`CameraExecutionOrderValidator.cs:202`）。文字列指定は `TypeExistsInLoadedAssemblies`（`Assembly.GetType(name)` = 入れ子は `+` で書く必要がある）で実在を確認、無ければ Warning。ジェネリックの MonoBehaviour はスクリプトにならないので対象外。`GetClass()` が null のスクリプトは検査の一覧に入らない。細部は GA-R-10。
- **安全側か**: 除外は「理由必須 + 毎回 Info で型・理由・宣言元を列挙」。除外された型が実際にカメラを書いていても Warning は消えるが、(c) のファイル走査（`PlayerLoop` / `onBeforeRender` 等の Info、`ScanRiskyPatternFiles`）は除外の影響を受けない。**Applier 自身の Error は除外できない**（`info.TypeName == ApplierTypeName` の判定が除外の前、かつ D-Drive のパスは Warning の対象外）。十分と判断。
- **プロバイダの異常**: 例外は `Debug.LogException` で隔離（遅延列挙の途中の例外も同じ `try` の中）。null は無視。重複は先勝ち（提供口 → 設定の順）。巨大な一覧は Info 1 件の文字列が長くなるだけ。
- **発見規則**: `ExtensionPointDiscovery.Instantiate`（E-19 / E-21 と同じ。public・`IsVisible`・非 abstract・引数なしコンストラクタ・`DDrive.Tests*` 除外・フルネーム順・ドメインリロードまでキャッシュ）。§5.14 E-23 の記述と一致。
- **E-23 のテストの実効性**: 外部アセンブリ（`ExternalContract.Tests.Editor`）のダミーが実際の発見経路（`ResolveCurrent`）で見つかること、宣言した型だけが外れること、理由なし・例外・入れ子の可視性、を固定していて実効がある。ダミーは既定で何も宣言しない（`ExternalCameraExemptionProbe.Enabled = false`）ので、開発リポジトリの実際の `Run All` を汚さない。
- **T-Drive の `FacialCorrectionRunner` で使えるか**: 使える。ブリッジ（`Bridges.DDrive` の Editor asmdef、`com.ddrive.core` の `versionDefines` `[1.4.0,)`）が `DDrive.Editor` と T-Drive Facial のランタイム asmdef を参照していれば、`yield return new CameraExecutionOrderExemption(typeof(FacialCorrectionRunner), "カメラを読むだけ(書き込みはしない)");`（docs/51 §7.1 の例）で、`Type.FullName` と検査側の `GetClass().FullName` が一致する。ランタイム asmdef を参照できない場合は文字列で完全修飾名（例 `TDrive.Facial.FacialCorrectionRunner`。入れ子なら `Outer+Inner`）を渡す。注意: D-Drive の開発リポジトリの `package.json` の `version` が 1.4.0 になるまで（= `bump-version.ps1` まで）`[1.4.0,)` の define は立たないので、T-Drive 側はタグの後でないとこの経路を通しで試せない（FC 全体と同じ前提）。

---

## P2 — 直すべき不具合・設計上の穴

### GA-R-01. MS2026 への案内 (a) に「登録解除」と「`Instance` が null のとき」が無い。案内どおりに書くとシーンを跨いだときに例外が毎フレーム出て、後ろに登録した Manager も止まる

- **場所**: 案内 6 か所（上の表）。実体: `Foundation/Manager/GameLoop.cs:11-27`（`Register` は `List` に足すだけ、`Tick` は `for` で各 Manager の `Tick` を直接呼び try/catch なし）、`Runtime/Loop/DDriveRuntimeBootstrap.cs:114-116,176-196`（`Instance` / `Loop`、`KeepAcrossScenes = true` が既定で `DontDestroyOnLoad`）、`:256-264`（`OnDestroy` で `Instance = null`）
- **何が問題か**: 案内は「`IAssetManager` を実装して `DDriveRuntimeBootstrap.Instance.Loop.GameLoop.Register(...)` で登録」とだけ書いている。持ち込み先は自然に「シーンに置いた MonoBehaviour が `IAssetManager` を実装し、`Awake` / `OnEnable` で `Register`」と書く。Bootstrap は既定でシーンを跨いで生き残るので、シーン切り替えでその MonoBehaviour が破棄されても `GameLoop` は参照を持ち続け、毎フレーム破棄済みオブジェクトの `Tick` を呼ぶ（`transform` 等に触れれば `MissingReferenceException`）。`GameLoop.Tick` に例外の隔離が無いので、**その後ろに登録された持ち込み先の Manager もそのフレームは Tick されない**（D-Drive 自身の Manager は先に登録されているので影響しない）。`BroadcastPause` / `StopAll` も同じ。逆に Bootstrap の無いシーン（テスト用シーン・`KeepAcrossScenes = false` で作り直された直後）では `Instance` が null で `NullReferenceException`。
- **失敗の筋書き**: MS2026 が 29 件の `Time.deltaTime` のうちゲームプレイのものを案内どおり `IAssetManager` に移す → タイトル → バトル → リザルトのシーン遷移で、バトルのシーンに置いた Manager が破棄後も Tick され、例外が毎フレーム出る。同じフレームで後ろに登録した別の Manager（例: クールタイム）が止まる。
- **直し方の案（コード変更なし。返答を送る前に）**: 案内 (a) に 1 文足す: 「`OnEnable` で `Register`、`OnDisable`（または `OnDestroy`）で `DDriveRuntimeBootstrap.Instance?.Loop.GameLoop.Unregister(this)` する。`Instance` が null（Bootstrap の無いシーン）のときは何もしない。Tick 中の例外は他の Manager に波及するので Tick の中で握る。ポーズ中も Tick は呼ばれ dt は 0 にならない（`OnPause` で自分で止める）」。返答文・運用ページ・消費側スキルの 3 か所に入れ、AGENTS / rules.html / docs/12 は「詳細は運用ページ」でよい。D-Drive 側で `GameLoop.Tick` に例外の隔離を足すのは挙動の変更なので M-5（案）と一緒に検討。
- **確度**: 確認済み（コード読み）。`MissingReferenceException` の出方は Manager の中身次第

→ **対応（2026-10-06、修正ラウンド 6、96c5b09）**: 案内 6 か所（`docs/11` M-4 の返答文・運用ページ・消費側スキル `common-warnings.md`・`AGENTS_CONSUMER.md`・`docs/12` §3・ProgrammerManual `rules.html`）と docs/02 §8・スキャナのメッセージ（参照先を §10 から §8 + 運用ページへ）に、「`OnEnable` で `Register`・`OnDisable` で必ず `Unregister`（`GameLoop` は自動で外さず `Tick` に例外の隔離も無い）」「`Instance` / `Loop` が null のとき（Bootstrap の無いシーン・起動前・終了時）は登録しない」「登録に `IsReady` は不要（`Loop` は Bootstrap の `Awake` = 実行順 -1000 で揃う。`IsReady` はカタログ登録の完了）」「`Tick` で例外を出さない」「ポーズ中も `Tick` は呼ばれ `dt` は 0 にならない」を追記。コード例（登録 / 解除の対）は運用ページ（`docs/50_consumer_guide/operation.html`）に 1 つだけ載せ、他は参照。`AGENTS_CONSUMER.md` の検出対象の列挙も 5 つに直した。`Documentation~/ProgrammerManual/` は生成物なので手で触らず、リリース時の同期に任せる（`docs/ProgrammerManual/rules.html` と `Tools/SpecWeb/html/manual/programmer/rules.html` は `build-manual.js` で再生成済み）。**契約テスト E-9b**（`ExternalContract.Tests.Runtime`、実 `DDriveRuntimeBootstrap` + 案内どおりの外部 `MonoBehaviour`）: Bootstrap が無いと登録しない / `Tick` で `dt` が届く / `HitStop` 静止中は `dt = 0`・スローは `unscaledDeltaTime × 0.5` / 破棄（`OnDisable`）後は `Tick` されない。docs/42 §5.14 E-9 の行を更新。

---

## P3 — 整理・改善

### GA-R-02. 【FZ-R-07 の残り】`SignalLocal` / `ApplySignal`（`OnSignal` 経由）のループは、購読者が自分を止めても残りの OnSignal トラックを発火し、出した VFX / SE が取り残される（v1.3.1 から既存。実装者が報告済み）

- **場所**: `Runtime/Presentation/PresentationManager.cs:915-933`（`SignalLocal`）・`:976-994`（`ApplySignal`）。関連: `:398-411`（`PlayLocalInternal` は `SeekInitialTracks` で自分が止められても `_active.Add` と `FlushPendingUnknownKey` を続ける）
- **筋書き**: `onHit` に Marker（Trigger = OnSignal）と ループ VFX（OnSignal）があり、Marker の購読者が `Cancel(self)` → `StopFiredForCancel` → `Cleanup` → 戻ったループが VFX を新しく出す（`FiredVfx` に積まれるが誰も止めない）。`FireTrack` 末尾の確認のおかげで例外は出なくなった。`PlayLocalInternal` 側は、Kind = Signal の AtTime 0 トラックの `ctx.OnSignal` から `StopAll` 等で自分を止めた場合に、無効な Handle が `_active` に入り（次の `Tick` で除かれるので無害）、保留中の Signal / Cancel が止めた後のインスタンスに適用されうる（まれ）。
- **直し方**: 2 つのループの `FireTrack` の後にも `if (!_instances.IsValidSilent(handle)) return;`、`PlayLocalInternal` は `SeekInitialTracks` の後で無効なら `_active.Add` / `FlushPendingUnknownKey` を飛ばす。v1.4.x の PATCH で可（FZ-R-07 と同じ扱い）。
- **確度**: 確認済み（コード読み）

→ **対応（2026-10-06、修正ラウンド 6、dd06368）**: `SignalLocal` と `ApplySignal` の `FireTrack` の後にも `IsValidSilent(handle)` の確認を追加（FZ-R-07 と同じ形）。ネットへの送信は呼び出し側で済んでおり回数・順序は変わらない。テスト 2 件（購読者が自分を止めたら残りの `OnSignal` は発火しない / 止められなければ全部発火）。**`PlayLocalInternal` 側（`SeekInitialTracks` の後に無効な Handle が `_active` に入る）は直していない**: 次の `Tick` で除かれて無害で、`FlushPendingUnknownKey` を飛ばすと保留中の Signal / Cancel が期限切れの警告を出す副作用があるため（まれなケースで、レビューも「まれ」としている）。CHANGELOG は既存の FZ-R-07 の項に追記。

### GA-R-03. E-23 の契約の柱である 2 つのコンストラクタがスナップショットに入っていない（docs/42 は「シグネチャを固定する」と書いている）

- **場所**: `Editor/Compat/EditorContractSnapshotBuilder.cs:87-117`（`AppendType` は public なインスタンスのフィールド・プロパティ・メソッドだけを出し、コンストラクタを出さない）、`Tests/Editor/Compat/Snapshots/editor-contract.txt:100-106`、`docs/42_distribution.md:597`（「`EditorContractSnapshotTests` が … シグネチャを固定する」「2 つのコンストラクタ」）
- **何が問題か**: `CameraExecutionOrderExemption` は readonly フィールドだけの struct なので、外部パッケージが値を作る手段は 2 つのコンストラクタだけ。そこを消す・引数の型を変えても `editor-contract.txt` は変わらず、互換テストが通ってしまう。他の型も同じ（`CutsceneImportResult` 等、コンストラクタで作る Editor 契約の型）。表示上は struct が `class` と出る（`AppendType` の種別判定）。
- **直し方**: `AppendType` に public コンストラクタ（`ctor(Type, String)` 形式）を足す（全型のスナップショットに行が**増えるだけ**なので追加のみ）。タグ前に入れれば v1.4.0 の基準に入る。少なくとも docs/42 の「シグネチャを固定」を「フィールド / メソッドを固定（コンストラクタは対象外）」に直す。
- **確度**: 確認済み（コード読み）

→ **対応（2026-10-06、修正ラウンド 6、8eef472）**: `EditorContractSnapshotBuilder.AppendType` が public コンストラクタを `ctor(型 名, …)` の行として出すようにした（前者を採用）。`editor-contract.txt` は **+4 行、削除・変更 0**（`ctor()` 2 行 = 引数なしの型、`CameraExecutionOrderExemption` の 2 つ）。`9f40cbb..HEAD` の Compat スナップショットは `+127 → +131`、削除 0。既存の行の表記は変えていない（`ctor` は名前順で各型の先頭に入るだけ）。docs/42 §5.9 と E-23 に追記。

### GA-R-04. Q-1 の文面が承認された決定と違う（15-5 の手順では承認された文が出ない）

- **場所**: `Editor/Update/UpdateWindow.cs:562-575`、`docs/verification/43_manual_verification_2026-09-17.md` 15-5 の期待・15-27、報告の「まとめ役の決定」Q-1
- **何が問題か**: 決定は「ウィンドウに『manifest に同じ URL の … があります。管理対象に登録しました』を表示する（確認項目の期待は変えない）」。実装は、既に管理対象のもの（= 15-4 で導入・登録した URL をもう一度入れる 15-5 の手順そのもの）では「<ID> は既に管理対象に登録されています(manifest は変わりません)。」を出し、承認された文はまだ管理対象でないもの（15-3 の候補と同じ）を入れたときだけ出る。docs/43 15-5 の期待も実装に合わせて書き換えた。
- **見立て**: 実装の文の方が事実に合っている（何も増えない）ので、変えるべきは決定の方かもしれない。ユーザーに「この文面でよいか」を確認し、よければ報告の決定に追記する。
- **確度**: 確認済み

→ **対応（2026-10-06、修正ラウンド 6、5ea6e30）**: 動作はそのまま（確認済み: `UpdateWindow.ApplyAddPlan` は既に登録済みなら「<ID> は既に管理対象に登録されています(manifest は変わりません)。」、manifest にあって未登録なら `PackageAddPlanner` の「manifest に同じ URL の <ID> があります。管理対象に登録しました。」）。docs/43 15-5 は「15-4 の時点で既に登録済みなので前者が出る」と状態を明記し、承認された文は 15-27 (a)（未登録の状態を作ってから確認）で見る形に直した。15-27 の (a) / (b) の状態の作り方も明記。**ユーザーに「この文面でよいか」の確認が要る**（まとめ役の決定は上記の 2 通りで確定）。

### GA-R-05. `ddriveUpdate` 自体が文字列 / 配列のときの扱いを変えたのに、形式の固定テストは旧い `Parse` を見たまま。docs/42 §4.2.1 の最後の 1 行も旧い記述

- **場所**: `Tests/Editor/Update/DdriveUpdateFormatCompatTests.cs:117-124`（`DdriveUpdateItself_NotAnObject_IsEmpty` は `Parse(...).IsEmpty` を確認。`Parse` は `Unreadable` を `Empty` に丸めるので今も通る）、`docs/42_distribution.md:360`（規則 3 は書き換え済み）・`:364`（「フィールド無しや壊れた JSON は空の宣言として扱う」= 旧記述）
- **何が問題か**: 規則 3 の書き換え（`ddriveUpdate` が null 以外のオブジェクトでなければ「読めなかった」= Warning）は実際の経路（`InstalledPackages` / `UpdatePreflight` の `TryParse`）にだけ効き、「v1.4.0 で固定」のためのテストは名前どおり「空になる」を固定している。後で誰かが `TryParse` を「空」に戻しても固定テストは通る。互換の観点では、v1.4.0 が `ddriveUpdate` を読む最初の版（v1.3.1 には無い）で、型の変更は規則 6 で MAJOR 扱いなので、「読めない」にしたこと自体は将来の拡張の余地を潰さない（新しい形は新しいキーで足す規則のまま）。
- **直し方**: 固定テストを `TryParse` で 4 入力（`[]` / `"x"` → false、`null` / `{ "requires": "x" }` → true + 空）に直し、名前を変える。docs/42 `:364` を「フィールド無しは空、壊れた JSON・`ddriveUpdate` が不正な形は『読めなかった』（Warning）」に直す。
- **確度**: 確認済み

→ **対応（2026-10-06、修正ラウンド 6、3b29577）**: **規則を 1 つに決めた**（まとめ役の決定）: `ddriveUpdate` の値はオブジェクト。無い・`null` は宣言なし、オブジェクトでない値は「この版では読めない宣言」として Warning `DD-PKGDEP-BAD-DECLARATION` + 事前確認は「確認できませんでした」（黙って無視しない = 警告の見逃しを防ぐ方を優先）。将来の拡張は `ddriveUpdate` オブジェクトの中のキー追加か別のトップレベルキー。「値が文字列でない項目は黙って無視」は `requires` / `compatibleWith` の中の項目の規則で、`ddriveUpdate` 自体には適用しない（矛盾しない）。docs/42 §4.2.1 の規則 3 と最後の行を直し、CHANGELOG の `ddriveUpdate` 拡張規則の項に追記。`DdriveUpdateFormatCompatTests` の `Pkg` ヘルパーを実際の経路（`TryParse`）に変え、4 入力（`[]` / `"x"` / 数値 / 真偽 → 読めない + Warning）・6 入力（無し / `null` / `{}` / `requires` が文字列・配列・値が数値 → 読めた + 空）・旧 `Parse` の互換・BOM を固定。

### GA-R-06. git の出力の細部（BOM・日本語ロケール・実 git のテストの環境依存）

- **BOM**: `GitProcess.cs:88` のコメント「BOM があれば StreamReader が読み捨てる」は、非同期読み取り（`BeginOutputReadLine`）では成り立たない可能性が高い（.NET の非同期読み取りはエンコーディングのデコーダーを直接使い、BOM 検出をしない = **推定**）。BOM 付きの package.json を `git show` すると先頭に U+FEFF が残り、Json.NET はそれを不正な文字として失敗する（**推定**）→ 事前確認が「読めませんでした」になる。導入済みの側（`File.ReadAllText` は BOM を除く）は読めるので、更新前だけ誤って「事前確認できませんでした」と出る（安全側。黙って見逃すことは無い）。直すなら `TryParse` の先頭で `TrimStart('\uFEFF')`。
- **日本語ロケール**: コメントの「日本語のメッセージでも読める」は、git のメッセージが日本語だと `fatal:` が「致命的なエラー:」になり、`ErrorLine` は最初の非空行（`clone` の「… にクローンしています…」の進捗行）を理由として出しうる（表示だけの問題。v1.3.1 から同じ）。
- **実 git のテスト**: `Run_ReadsUtf8Output_OfRealGit_InTemporaryRepository` は git の有無は `Assume` で見るが、`init` / `add` / `commit` の成否は `Assert`。グローバル設定の `core.hooksPath`（コミットフック）などで commit が失敗する開発機では赤になる（`commit.gpgsign` は `-c` で外してある）。直すなら commit に `-c core.hooksPath=` を足すか、失敗を `Assume` にする。一時フォルダの後始末は `finally` で行っていて問題なし。
- **確度**: BOM と Json.NET は**推定**、他は確認済み

→ **対応（2026-10-06、修正ラウンド 6、3b29577 / ef21994）**: **推定の確認**: BOM と Json.NET の挙動は Unity 上で再現していないが、`TryParse` の先頭で U+FEFF を取り除くようにし（テスト `TryParse_AcceptsLeadingByteOrderMark` で固定。取り除かなくても通る環境でも害はない）、`GitProcess` のコメントを実態に合わせた。実 git のテスト: `init` / `add` / `commit` を `-c core.hooksPath=<存在しないフォルダ> -c init.templateDir= -c commit.gpgsign=false -c tag.gpgsign=false -c user.name/email` と `--no-verify` で隔離し、準備に失敗したら `Assume`（Inconclusive）に。後始末は従来どおり `finally`。日本語ロケールの理由の 1 行は表示だけの問題で見送り。

### GA-R-07. Q-3 と同じ「最初の Data に紐付く」問題が、他のプロジェクト全体の Validator に残る（新しい Info `DD-CAMEXEC-EXEMPT` もその 1 つ）

- **場所**: `CI.cs:172`（`IsProjectScoped` は `PackageDependencyValidator` だけ）、`DataValidationSection.cs:162-174`（`ProjectWideValidatorNames` = `ProjectSetupValidator` / `SpecDiffValidator` / `ContentHashCatalogCoverageValidator` / `CameraExecutionOrderValidator` / `PackageDependencyValidator` …）
- **何が問題か**: `Run All` では、`CameraExecutionOrderValidator`（G-1 の Warning・Q-4 の Info / Warning）・`ProjectSetupValidator`（`DD-FORBIDDEN-ALLOW-SETTINGS-INVALID` 等）・`SpecDiffValidator`・`ContentHashCatalogCoverageValidator` の結果が、今も「たまたま最初の Data」のパス付きで出る。P-15 の確認で Q-4 の Runner の Warning も同じ形で出ていたはず。
- **直し方**: `IsProjectScoped` を `DataValidationRunner.IsProjectWide` と同じ一覧にする（`RunValidation(includeProjectWideValidators: false)` の経路は先に除外されるので影響なし）。表示が `(project)` に変わるだけで件数・重さは同じ。タグ後の PATCH でも可。
- **確度**: 確認済み（コード読み）

→ **対応（2026-10-06、修正ラウンド 6、ef21994）**: 共通の判定 `DataValidationRunner.IsProjectScopedInRunAll`（= `IsProjectWide` の一覧 + `ProjectSetupValidator`）を 1 か所に作り、`CI.RunValidation` の「Data に紐付けず asset = null で 1 回」の経路をそれに揃えた（`IsProjectWide` 自体は個別検証・SpecWeb 用なので変えていない）。対象: `PackageDependencyValidator`・`ProjectSetupValidator`・`SpecDiffValidator`・`ContentHashCatalogCoverageValidator`・`CatalogAddressCoverageValidator`・`CameraExecutionOrderValidator`。**`SpecDiffValidator` だけは Data ごとの指摘（仕様書との差分）も出すので、null の呼び出しの後に同じ context で各 Data にも呼び、Data ごとの指摘は従来どおり Data に紐付ける**（報告漏れなし）。件数・重さ・コードは不変。SpecWeb（`includeProjectWideValidators: false`）は `IsProjectWide` のものは従来どおり除外、`ProjectSetupValidator`（Warning / Info のみで Error なし）は asset = null になるだけで isPlaceholder 判定（Error のみ・asset 付きのみを見る）に影響しない。個別検証（Validation ウィンドウ相当 = Inspector の「検証」節）は変えていない。テスト 3 件（対象の判定 / Run All でアセットに紐付かず二重報告もない / 除外時も asset 付きで出ない）。既存テストの期待値の変更は不要だった。CHANGELOG に「挙動の変更(PATCH 相当。Editor の出力)」として記載。

### GA-R-08. `ForbiddenApiWindow` の細部（ドメインリロードのたびの全走査・`ScrollView` の外の状態行・大量の当たり）

- **場所**: `Editor/Validation/ForbiddenApiWindow.cs:28-47`
- **細部**: (1) `CreateGUI` で同期的に `Rescan()` するので、ウィンドウを開いたまま（ドッキングしたまま）だとスクリプトを保存するたびにドメインリロード後の `CreateGUI` で `Assets` 配下の全 `.cs` を読み直す（持ち込み先の規模次第で毎回のコンパイル待ちが伸びる）。開いた直後だけ自動で走査し、ドメインリロード後は「再走査」を押すまで前回の結果（または空）にする方が軽い。(2) 状態の `Label` が `ScrollView` の外にある（docs/09 §7 は Toolbar 以外を `ScrollView` に積む規約。高さの小さいウィンドウで状態行が長いと切れる）。(3) 当たり 1 件ごとに `Button` + `Label` を作る（仮想化なし）。29 件なら問題ないが、数千件では重い。
- **確度**: 確認済み（コード読み。体感の重さは未測定）

→ **対応（2026-10-06、修正ラウンド 6、3f4f587）**: (1) メニューから開いたとき（`Open()`）だけ走査し、ドメインリロード後の `CreateGUI` では走査せず「スクリプトの再コンパイル後は自動では走査しません。「再走査」を押すと検査します。」と表示。既に開いているウィンドウをメニューで開き直したときは再走査する。(2)(3) は見送り: 状態の `Label` は docs/09 §7 の規約（Toolbar 以外を ScrollView）に厳密には反するが、短い 1 行の状態で、当たりが 29 件規模なら問題がないため（数千件規模になれば仮想化を検討）。

### GA-R-09. 2 つの設定画面の細部（OnGUI ごとの重い処理・Undo の保存・購読の重複）

- **場所**: `ForbiddenApiAllowSettingsProvider.cs:46-52,66-89`、`CameraExecutionOrderExemptionSettingsProvider.cs:46-49,60-85`
- **細部**: (1) 描画のたびに `CI.ResolveForbiddenApiScanRoot()`（開発リポジトリでは `PackageInfo.FindForAssembly`）と、要素ごとに `TypeExistsInLoadedAssemblies`（全アセンブリの `GetType`）を呼ぶ。マウスを動かすだけで再描画されるので、要素が多いと重くなる。変更時だけ計算してキャッシュするのが安い。(2) `OnUndoRedo` は**どの** Undo / Redo でも保存する（画面が開いている間）。逆に画面を閉じた後の Undo は保存されない。(3) `DrawGui` の「`_serialized` が無ければ `OnActivate()`」は `undoRedoPerformed` に重ねて購読しうる（解除は 1 回）。いずれも実害は小さい。
- **確度**: 確認済み（コード読み）

→ **対応（2026-10-06、修正ラウンド 6、69ace40）**: (1) 2 つの設定画面とも、要素ごとの問題の表示を開いたとき・変更時・Undo / Redo 時にだけ作り直す（描画のたびに走査ルートの解決・全アセンブリの `GetType` をしない）。(3) 購読は `-=` してから `+=`（二重購読しない）。**(2) は見送り**: 画面が開いている間の Undo / Redo で毎回保存するのは、他の設定の変更の Undo でも保存されるだけで害がなく、`SerializedObject` 側の変更検知で絞ると取りこぼす恐れがあるため。

### GA-R-10. Q-4 の照合の細部

- **場所**: `CameraExecutionOrderExemptions.cs:82-103,221`
- **細部**: (1) `+` → `.` の正規化で、入れ子 `A.B+C` と名前空間 `A.B.C` が同じ名前になり、片方の除外がもう片方にも効く（まれ）。(2) 同じ完全修飾名の型が別のアセンブリにあると、両方とも除外される（名前だけで照合）。(3) 提供口の例外は Console の `LogException` だけで、Validation の結果（Warning）には出ない（`Run All` の結果だけ見ている人は「宣言したのに効いていない」理由が分からない）。(4) `IsExempt` の短い名前での照合（`fullTypeName` が空のとき）はテスト専用の経路だが、実際の `ScriptOrderInfo` が `FullName` を持たない場合にも働き、名前空間違いの同名の型を除外しうる（実際の経路では `GetClass()` が null の型は一覧に入らないので起きない）。(5) Q-2 の補足: 宣言した側に問題が複数あっても行には最も重い 1 件しか出ない（件数なし）。どれも小さい。
- **確度**: 確認済み（コード読み）

→ **対応（2026-10-06、修正ラウンド 6、69ace40）**: (3) 提供口の例外を Console に加えて Warning `DD-CAMEXEC-EXEMPT-INVALID` にも出すようにした（既存コード・Warning。追加のみ）。テスト拡張。(1)(2)(4) は**見送り（まれ・安全側）**で、docs/42 §5.14 E-23 に「既知の限界」として記載（照合は完全修飾名だけ）。(4) は `GetClass()` が null の型は検査の一覧に入らないので実際の経路では起きない。(5) Q-2 の「最も重い 1 件だけ」は表示を短く保つための仕様で、詳細は Validation の結果に出るため見送り。

### GA-R-11. CHANGELOG `[Unreleased]` の細部

- **場所**: `CHANGELOG.md:14-15`（P-15 確認の 2 項）・`:59-60`（修正ラウンド 5 の 2 項）・`:63-83`（`### 追加`）
- **細部**: (1) `### 追加` に `Tools > D-Drive > Validation > 禁止 API の検査`（FZ-R-03）と `ICameraExecutionOrderExemptionProvider` / Project Settings の「実行順の検査の除外」（Q-4）が無い（互換性節には書いてある）。(2) 修正ラウンド 5 の項の「(6) … 開発リポジトリの `CI.ValidateAll` が緑になる」は、確認したのは禁止 API の段だけ（実装者の記録どおり `run-ci.cmd` の他の段は未実行。開発リポジトリの GameData に Validation の Error が無いかは誰も見ていない）。「禁止 API の段が緑になる」に直すのが正確。(3) `(unknown)` → `(project)` は Validation の出力（JUnit の classname）の変更なので、区分「変更なし」より「挙動の変更(Editor の出力)」の方が持ち込み先の CI 担当に伝わる。(4) 「`ddriveUpdate` の拡張規則」の項（`:28`）は「文字列でない値は黙って無視」のままで、P-15 確認の項の「`ddriveUpdate` の形が読めない → Warning」と並べると読み手が迷う。前者に「`ddriveUpdate` 自体がオブジェクトでないときは読めなかった扱い（2026-10-06）」を 1 語足す。
- **確度**: 確認済み

→ **対応（2026-10-06、修正ラウンド 6）**: (1) `### 追加` に検査ウィンドウと `ICameraExecutionOrderExemptionProvider` / 設定を追記。(2) 「CI.ValidateAll が緑」→「禁止 API の走査の当たりが 0 件（確認したのは禁止 API の段だけ）」。(3) `(unknown)` → `(project)` を「挙動の変更(PATCH 相当。Editor の出力)」の独立した項に（GA-R-07 と合わせて）。(4) `ddriveUpdate` の拡張規則の項に「`ddriveUpdate` 自体はオブジェクト、そうでなければ読めない宣言」を追記。

### GA-R-12. コミットされている設定ファイルが今のスキーマより古い・案内の細かい食い違い

- **設定ファイル**: `ProjectSettings/DDriveProjectSettings.asset` は `_managedPackages: []` まであり、`_forbiddenApiAllowEntries` / `_cameraExecutionOrderExemptions` が無い（最後の更新は `eddc805`）。読むときは初期化子で空の一覧になるので**挙動の問題は無い**。ただし Unity がこのファイルを保存する操作（設定画面・更新ウィンドウの登録・マイグレーションの適用・MCP での確認）のたびに 2 行が足され、実装者は毎回 `git checkout` で戻している。リリース手順の `check-release.ps1` / `bump-version.ps1` は作業ツリーがクリーンであることを求めるので、手順の途中で Unity が保存すると止まる（下の「リリース手順」）。持ち込み先では自分の設定ファイルに初回保存で 2 行足されるだけ（正常）。**タグ前に、Unity で設定画面を 1 度開いて保存し（テキスト編集はしない = CLAUDE.md §0-1）、2 行が足されたファイルをコミットする**のを推奨。
- **案内**: `AGENTS_CONSUMER.md:12` の「検出するもの」が 3 つのまま（`Time` / `Addressables.Load*` が無い）。スキャナのメッセージの参照先 `[../02_core_framework.md] §10` に `IAssetManager` の登録方法が無い（§8 か運用ページを指す方がよい）。`DD-FORBIDDEN-ALLOW-SUMMARY` のメッセージ（`ForbiddenApiScanner.cs:285`）は旧メニュー「Forbidden API 許可一覧」だけを案内している（新しいウィンドウの方が便利）。
- **確度**: 確認済み

→ **対応（2026-10-06、修正ラウンド 6、8eb9d6a）**: MCP の `execute_code` で `DDriveProjectSettings.instance.SaveForbiddenApiAllowEntries()` を呼び、**Unity が書き出した内容をそのままコミット**（`.asset` のテキスト編集なし）。差分は `_forbiddenApiAllowEntries: []` と `_cameraExecutionOrderExemptions: []` の 2 行の追加のみ。案内の食い違い（`AGENTS_CONSUMER.md` の列挙・スキャナのメッセージの参照先）は GA-R-01 で直した。`DD-FORBIDDEN-ALLOW-SUMMARY` のメッセージを新ウィンドウへ案内する件は未対応（旧メニューも有効で害がないため見送り）。許可コメントの理由（UiSlider = パッド入力のリピート・値の追従アニメ・ノッチ SE の間隔 / Anim2DFacing = D-Drive のポーズ中も補間は進む旨）は d9c91e2（コメントのみ）。

---

## 挙動の変更と CHANGELOG の照合（v1.3.1 → `9020727`）

[57] の一覧からの差分（本 2 PR で変わった行・新しく生じた行）に限る。

| # | v1.3.1 から見た挙動の変更 | 区分（本レビューの見立て） | CHANGELOG `[Unreleased]` 互換性節 |
|---|---|---|---|
| 15（変更） | 禁止 API の設定の許可リストのパスが区切り単位・大文字小文字無視、絶対パス / `..` / 1 階層 / 走査ルートとその親は無効（Warning） | 未リリースの機能の確定（v1.3.1 に無い） | **あり**（修正ラウンド 5 の項 (1)） |
| 16（新規） | `Presentation` の購読者が自分を止めたら、同じ呼び出しの残りのトラックを発火しない（以前は例外で Tick が中断 / 取り残し） | 不具合修正（PATCH 相当の挙動の変更） | **あり**（FZ-R-07 の項。`OnSignal` 経由は変更なしと明記） |
| 17（新規） | `Time` 規則の指摘メッセージの文言 | Validation の文言（重さ・件数・Code は不変 = §5.8 の対象外） | あり（(4)） |
| 18（新規） | 開発ビルド / Editor で、受信した Cutscene ごとに Log 1 行 | 開発用の出力 | あり（(5)） |
| 19（新規） | D-Drive 自身の当たり 12 件 → 0 件（コメントのみ） | 挙動の変更なし | あり（(6)。「CI.ValidateAll が緑」は言い過ぎ = GA-R-11） |
| 20（新規） | 更新ウィンドウ: git の出力を UTF-8 で読む / 読めない package.json は Warning・「事前確認できませんでした」/ 再追加の文 / 行の依存の表示 | 未リリースの P-15 の確定（v1.3.1 に無い） | **あり**（P-15 確認の項 (1)〜(4)） |
| 21（新規） | `Run All` / `CI.ValidateAll` で `DD-PKGDEP-*` がアセットに紐付かず `(project)`。`asset = null` の報告の表示が `(unknown)` → `(project)` | Editor の出力の変更 | あり（(5)。区分は「変更なし」= GA-R-11） |
| 22（新規） | SpecWeb 送信の判定から `PackageDependencyValidator` が外れた | 影響なし（Error を出さない Validator） | 記載なし（記載不要） |
| 23（新規） | 実行順の検査の除外（宣言が無ければ従来と同じ。新規コード Info / Warning） | 追加のみ（MINOR。Editor の弱い互換面） | **あり**（Q-4 の項） |

MS2026 が踏みそうなもの: **#15**（29 件の仕分けで設定の許可リストを使うなら区切り単位）、**#21**（MS2026 の CI が JUnit の `(unknown)` を見ていれば）、そして返答文の (a)（GA-R-01）。

---

## リリース手順（docs/12 §7）を今の状態で進めたときに止まりそうな点

| 手順 | 止まりそうな点 | 対処 |
|---|---|---|
| 1. スナップショット差分 | 止まらない（`+127 / −0`、意図した追加だけ） | – |
| 2. CHANGELOG 互換性節 | 止まらない（空でない） | GA-R-11 の文言の修正はお好みで |
| 3. `check-release.ps1` | **作業ツリーがクリーンでないと fail**（`check-release.ps1:69-72`）。Unity を開いて設定画面・更新ウィンドウ・マイグレーションを触ると `ProjectSettings/DDriveProjectSettings.asset` に 2 行が足される（GA-R-12）。`TestResults/` は `.gitignore` 済み | 先に設定ファイルを Unity で保存してコミットしておく。Editor を閉じてから実行 |
| 3. CHANGELOG ガード | 止まらない見込み（スナップショットの変更に CHANGELOG の変更が伴っている） | – |
| 4. `run-ci.cmd` [2/8] MigrateCheck | 未確認（未適用のマイグレーションの有無は見ていない） | – |
| 4. `run-ci.cmd` [3/8] `CI.ValidateAll` | 禁止 API の段は 0 件の見込み（grep の写しで確認）。**それ以外の Validation の Error（開発リポジトリの GameData）は誰も確認していない**。v1.0.0 以来この段は禁止 API で赤だったので、隠れていた Error が初めて見える可能性がある | 最初に `[3/8]` だけ実行してログを確認 |
| 4. `run-ci.cmd` [4/8] ID 再生成 + `git diff --exit-code` | [2/8] / [3/8] の Unity 起動で何かが保存されると、ID と無関係な差分でも「ID 再生成でコミットされていない差分」と出る（メッセージが紛らわしい）。設定ファイルは [2/8]・[3/8] では保存されない見込み（`Save` の呼び出し元を確認） | 差分が出たら `git diff --stat` で中身を見る |
| 4. `run-ci.cmd` [5/8]〜[7/8] テスト | 実 git のテスト（GA-R-06）がコミットフックのある環境で赤。テストが `CI.RunValidation()` を丸ごと呼ぶ（`P15VerificationFixTests`）ので EditMode の時間が少し延びる | フックがあれば一時的に外すか、テストを直す |
| 4. `run-ci.cmd` 全体 | 同じプロジェクトを開いた Editor があると全段失敗（多重起動不可。スクリプトの注意どおり） | Editor を閉じる |
| 5. `bump-version.ps1 -Tag` | **作業ツリーのクリーン検査**（`bump-version.ps1:128-131`）。手順 4 の後に `run-ci` が何かを書き換えていると止まる。同梱物の同期（`docs/ProgrammerManual` → `Documentation~/ProgrammerManual`、`docs/50_consumer_guide` → `Documentation~/ConsumerGuide`）は robocopy で行われ、今は `Documentation~/ProgrammerManual/rules.html` が旧文（`ITimeSource`）なので**この同期が無いと旧い案内が配られる** | 手順 4 の後に `git status` を確認。同期後の差分を目視 |
| 7. SpecWeb | `docs/ProgrammerManual/{rules,extending}.html` を変更済み → `Tools/SpecWeb/push.cmd`（`build-manual.js` + `clasp push`）が要る。`Tools/SpecWeb/html/manual/programmer/*.html` は再生成済みでコミットされている。デプロイ②は UI で更新（メモリの「clasp update-deployment は ② を壊す」） | 手順どおり |
| 8. 持ち込み先への案内 | 返答文に GA-R-01 の 1 文を足してから送る | – |

---

## v1.4.0 のタグを打ってよいかの所見

**P1 は無く、互換面（スナップショット・メッセージ形式・公開 API）は追加のみなので、コードの正しさの面ではタグを止める理由は無い。** 修正ラウンド 5 の約束（D-Drive 自身の当たりはコメントだけで 0 件・挙動を変えない）は守られ、FZ-R-01〜12 と BUG-1 / Q-1〜Q-4 は記録どおり解消している。

ただし**タグで固定される・タグの前にしかできない**ものとして、次をタグの前に済ませるのを推奨する:

1. **GA-R-03**（E-23 のコンストラクタをスナップショットに入れる、または docs/42 の記述を直す）。タグの後に入れても追加のみだが、v1.4.0 の基準に入れておく方が安い。
2. **GA-R-12 の設定ファイル**（Unity で保存して 2 行をコミット）。手順 3・5 のクリーン検査で止まらないため。
3. **`run-ci.cmd` を全段**（特に [3/8] の禁止 API 以外の Validation と [5/8]〜[7/8]）。対応記録の「EditMode / PlayMode green」は本レビューでは未確認で、`run-ci` の全段は実装者も実行していない。
4. **GA-R-04 のユーザー確認**（Q-1 の文面）。
5. **GA-R-01** はタグとは独立だが、**MS2026 へ返答を送る前に**（返答文・運用ページ・消費側スキルに 1 文。運用ページと消費側スキルはパッケージに同梱されるので、タグの前に入れれば v1.4.0 の `Documentation~` に載る）。

タグを止めないが v1.4.x でよい: GA-R-02・05〜11。

**タグの前に残る人による確認・実機確認**（自動テストでは見られない、または未実施のもの）:

- **[43] §17（M-4 禁止 API の許可）17-1〜17-8**（全行 `□ 未`。許可コメントの書式・設定の許可リストはタグで固定されるので**タグ前に**。17-5 は最初に「欄が灰色でなく編集できる」こと、17-7 はウィンドウの各行のボタンでその行が開くこと）。17-9（MS2026 で v1.4.0 に更新して 29 件を仕分け）はタグの後。
- **[43] §15 の再確認（日本語 Windows）**: **15-25**（日本語入りの package.json で事前の警告が出る = BUG-1 の本筋）・**15-26**（壊れた JSON / `"ddriveUpdate": "x"` で「事前確認できませんでした」と `DD-PKGDEP-BAD-DECLARATION`）・**15-27**（再追加の文。GA-R-04 の確認と一緒に）・**15-28**（行の表示）・**15-29**（`(project)` 表示）。15-5 / 15-10 / 15-11 / 15-12 の「要再確認」はこれで兼ねる。**15-2 / 15-14**（D-Drive を git URL で入れたプロジェクト）・15-17a の残り・15-19 / 15-20 / 15-23・15-24 の残りは、導入テスト用プロジェクトか MS2026 で（タグ後でも可）。**15-30**（Q-4）と 15-13 の残りは T-Drive のブリッジ対応後（タグ後）。
- **[43] §16（Canvas の埋め込み）**: 全 25 行 `□ 未`（U-28。v1.4.0 の新機能なのでタグ前が望ましい）。
- **[52]（FC チケット）**: §1（FC-1、1-1〜1-15）・§2（FC-2 / FC-12）・§3（FC-3）・§4（4-1〜4-5。**4-4 (c)** の Edit Mode の途中再生と **4-5** のネット 3〜4 台〔Client 送信 → 別の Client、0.5 秒より後の途中参加、`host_migration` 後、FZ-R-10 のログで開始位置を記録〕）・§5〜§7・§11（11-1〜11-10、11.3）・§14・§15（15.1〜15.6、特に **15.6 手順 4** = 実データの複製で T-Drive を外して再生成しても Common が変わらない）・§19・§20。ほぼ全行 `□ 未`。§22（T-Drive 導入後）はタグの後。
- **[43] §7 / §10（Timeline）**: [46] の FBX の到着後（タグの後でよい。CLAUDE.md の「次は人による確認」）。
- **開発リポジトリで `Tools/CI/run-ci.cmd` 全段 green**（上の 3）。

---

## 確認して問題なしだった観点

- **FZ-R-05 の「コメント以外の変更 0」**: 12 か所の差分を 1 件ずつ確認。許可コメントの位置（当たりの 1 行上のコメントだけの行）・1 行 1 規則・複数行の文なし。grep の写しで許可されていない当たり 0 件、許可 11 件。
- **`ThisPackage_*` テストの持ち込み先での扱い**: どちらも `IsDevelopmentRepo` でなければ `Assert.Ignore`。
- **FZ-R-02**: 規則 (12) と実装が全項目一致。無効な要素が黙って効く経路なし。テストは絶対パスが無効になったためプロジェクト内の `Temp/` に置き、`ToProjectRelative` の前提（カレントディレクトリ = プロジェクトルート）と一致。
- **FZ-R-07**: 誤判定なし・通知 1 回・ネット不変・`Seek` も保護される。
- **FZ-R-10**: 開発ビルド / Editor 限定、定常経路に文字列を作らない、カーソルの合計の意味が正しい。
- **BUG-1**: git の起動は 1 か所に集約。stderr の文字化けは判定に影響しない。「読めない」が誤って「宣言なし」に落ちる経路なし（`InstalledPackages` のファイル読み取りの例外だけは従来どおり「宣言なし」= 権限エラー等で、文字化けとは別）。事前確認は「読めない」のときも他のパッケージの宣言との照合を行う。
- **Q-3**: 呼び出し元 6 つで二重報告・漏れなし。`(unknown)` を見るスクリプトは無い。
- **Q-4**: 宣言が無いときの結果は従来と完全に同じ（`Valid` / `Problems` が空なら `IsExempt` は false、`ToResults` は空）。Applier の Error は除外不可。テストのダミーは既定で何も宣言しない。T-Drive のブリッジから使える。
- **互換**: スナップショット 4 ファイル `+127 / −0`。`DDriveProjectSettings` の追加フィールド 3 つ（`_managedPackages`・`_forbiddenApiAllowEntries`・`_cameraExecutionOrderExemptions`）はどれも初期化子 + null 合体のプロパティで、旧ファイル（今コミットされているものを含む）を空で読む。新規コード（`DD-CAMEXEC-EXEMPT` Info / `DD-CAMEXEC-EXEMPT-INVALID` Warning）は §5.8 の「新しい検査は Warning 以下」に合う。メニューは `DDriveMenu.Validation` 定数経由。新しいウィンドウ・設定画面は `UnityEditor` 参照を Editor asmdef の中だけに持つ。
- **docs/43 §15 の記入**: 報告の原文と行ごとに一致（OK / NG / 一部 OK / 確認不可 / 未確認、メモの中身）。結果を作っていない。
- **docs/51 §7.1 の T-Drive 向けの案内**: コード例・発見条件・`[1.4.0,)`・手動の逃げ道の記述が実装と一致。

---

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・`CI.ValidateAll`・`run-ci.cmd`・`ForbiddenApiWindow` と 2 つの設定画面の見た目・実 git・実ネットワーク）。特に GA-R-06 の BOM と Json.NET の挙動、FZ-R-07 の R3 の Dispose 後の `OnNext`、GA-R-08 の走査の重さは推定・未測定のまま。
- D-Drive 自身の当たりは grep の写しで数えた。`ForbiddenApiScanner` の実行結果ではない。
- 開発リポジトリの GameData に Validation の Error が残っているか（`run-ci.cmd` [3/8] の禁止 API 以外の部分）。未適用のマイグレーションの有無（[2/8]）。
- MS2026 の 29 件の中身と、MS2026 の CI が JUnit の classname を使っているか。
- T-Drive 側のブリッジの asmdef の実際の参照関係（docs/51 の記述と D-Drive 側の実装から判断した）。
- `UiTweenManager` / `UiManager` の同形の走査（[56] FY-R-03 の「報告のみ」）は今回も読んでいない。
- SpecWeb の再生成物（`Tools/SpecWeb/html/manual/*`）は差分の有無だけ確認し、中身は正本（`docs/ProgrammerManual`）で確認した。
