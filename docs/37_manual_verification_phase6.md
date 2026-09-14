# 37. 実装確認手順書（Phase 6 分、2026-09-15〜）

> 2026-09-15 にユーザー承認のもと Claude Code が実装した Phase 6（6-0〜6-9b）の**人による確認項目**。書式は [28_manual_verification_phase5.md](28_manual_verification_phase5.md) に合わせる。
> 自動検証（isuzu MCP: コンパイル 0 エラー / EditMode / PlayMode テスト）は各コミット時に通しているが、**Validation ウィンドウの実際の表示・ドキュメントの読みやすさ・実機**は未確認。上から順にやれば P6 実装済み分を一巡できる。
> 共通の前提: Unity 6000.3.13f1。ウィンドウ内描画は廃止、確認用シーン / 実 UI / Validation ウィンドウで確認する（[09_editor_tools.md] §2）。
> Web 発注ツール（`Tools/SpecWeb`、O-12〜O-16 等）の確認は既に [28_manual_verification_phase5.md] にあるため、本書では重複させない（後述「Web 発注ツールの確認について」参照）。
> P6 の対象は 6-1〜6-9b（Timeline の 6-10 は要件未確定のため着手せず、[11_tasks.md] Phase 6 の決定事項を参照）。実装中・未着手のチケットは末尾に見出しと確認観点の仮置きだけ用意した（実装後にこの節を本文として書き直す）。

## 0. 確認の進め方（2026-09-15 まとめ）

### 事前準備（Unity を開く前に）

- `git pull` して最新の main を取得する（このドキュメント自体もその一部）
- 6-9（仕様書差分の Validation）の確認は `Specs/assets.json` / `Specs/tuning.json` が無くても Info 1 件で完結する。仕様書同期（`Tools/SpecWeb`）を実際に使っている場合だけ、追加のケースを確認する
- 6-2（性能テスト）は Unity Editor の Test Runner（PlayMode）を使うため、①②を先に済ませてから最後にまとめて実行すると Editor の再起動が少なくて済む
- Web 発注ツールの確認まで行う場合は [28_manual_verification_phase5.md] の事前準備（Google アカウント等）に従う
- デザイナーマニュアルのスクリーンショット撮影リスト（[36_manual_screenshot_list.md]）は別担当が並行作業中に追加された。撮影自体は本書の確認対象外（6-4 節「残作業」参照）

### 確認順チェックリスト

所要時間は目安（人による確認作業のみ。自動テスト実行時間は含まない）。

**①エディタ系**（6-3・A2・6-9）

- [ ] 6-3 バージョン記録 — 本書「6-3」節 — 15分 — 特になし
- [ ] A2（SliderSkin の存在しない Haptic ID の Validation 警告） — 本書「A2」節 — 10分（大半は自動テストで確認済みのため任意） — 特になし
- [ ] 6-9 仕様書差分の Validation — 本書「6-9」節 — 15分 — （任意）仕様書同期済みの `Specs/*.json`

**②ドキュメント系**（6-4・6-9b・マニュアル）

- [ ] 6-4 オンボーディング（`docs/34`/`docs/35`/`getting-started.html`） — 本書「6-4」節 — 20分 — 特になし
- [ ] 6-9b AI エージェントスキル（`.claude/skills/ddrive-agent-workflow/`・`AGENTS.md`） — 本書「6-9b」節 — 15分（任意で新種別追加を試すと +30分） — 特になし
- [ ] デザイナーマニュアルの表示確認（6-4 で再生成した HTML・6-3 の追記反映） — 本書「6-4」節内 — 10分 — 特になし

**③性能・テスト系**（2026-09-15、着手時点では未実装だったが本書作成中に main へ merge されたため追加）

- [ ] 6-2 パフォーマンス計測・0 alloc 検証 — 本書「6-2」節 — 20分 — Unity 再起動 1 回程度

**④ネット系**（6-0 v4 結果・6-6/K3 の v5 実機・6-7）

- [ ] 6-0 実機 v4 確認結果を読む — 本書「6-0」節 / [29_network_device_test.md] §12 — 10分 — 特になし
- [ ] 6-6 / K3 の v5 実機（またはローカル結合）確認 — 本書「実装中・これから」6-6 節 — 未定（実装後に追記） — 特になし
- [ ] 6-7 2 クライアント自動テスト — 本書「6-7」節 — 約5分（`Tools\CI\run-netcheck.cmd`） — ビルド済み `Builds\DDriveNetCheck\DDriveNetCheck.exe`・pwsh(PowerShell 7+)

（6-1 CI 完全化は P7 末へ延期のため確認不要。本書「6-1」節 / [33_ci_setup.md] 参照）

**④受け入れ**

- [ ] 6-8 受け入れデモ — 本書「実装中・これから」6-8 節 / [38_acceptance_demo.md] — 約115分（判定記入・雑談を除く目安。§0 参照）。⑤の実機/ローカル結合確認は事前に v5 を済ませておくと短縮できる — [38_acceptance_demo.md] §0 の準備物（Unity・PC-A/PC-B またはループバック環境・git 作業ツリーがきれいな状態）

**⑤Web 発注ツール**（docs/28 へ）

- [ ] Web 発注ツール（反映後の目視） — [28_manual_verification_phase5.md#0-確認の進め方2026-09-14-まとめ] の「②仕様書系」内 O-12〜O-16 および各追補 — [28] の既存の時間表を参照（本書では重複計上しない） — [28] と同じ準備（デプロイ済み Web アプリ・`push.ps1` 実行済み等）

合計 **12 項目**（Web 発注ツールの内訳を除く）。判明している所要時間の合計は **約2時間（120分）+ 6-8 デモ約115分**（6-7 の約5分を追加。6-8 は手順書 [38_acceptance_demo.md] が用意済みのため所要時間が判明したが、実施自体はまだ行っていないため上記合計には含めていない）。うち 1 項目（6-6/K3 の v5 確認）は未実装のため「実装後に追記」、Web 発注ツールの確認は [28_manual_verification_phase5.md] の既存の時間表（O-12〜O-16 で目安 85分程度）を参照（本書の合計には含めていない）。①→②→③→④ の単位で分割してよい。

### 確認後の後片付け（まとめ）

| 節 | 片付ける対象 |
|---|---|
| 6-3 | 確認のために保存して版数を上げた Data（Ctrl+Z の Undo で戻すか、そのまま残しても実害は無い）。**注意**: `Author` に実行した人の Windows ユーザー名が実際に記録される。既存の（他人が作った）Data を確認用に保存すると、その人の名前が自分の名前に書き換わってしまうので、確認は確認専用に新規作成した Data か、Undo/`git checkout` で戻せる状態で行うこと |
| 6-9 | 確認のために `Specs/assets.json` / `Specs/tuning.json` を一時的に書き換えた場合は `git checkout -- Specs/` 等で元に戻す。`TuningTable` の値を範囲外にして Error を再現した場合は確認後すぐ範囲内に戻す |
| 6-2 | 特になし（Test Runner での実行に副作用は無い想定。`TestResults/performance-results.xml` が増えるだけなので、コミット対象に入れないよう `git status` で確認する） |
| 6-0 | 特になし（結果を読むだけ） |
| Web 発注ツール | [28_manual_verification_phase5.md#0-確認の進め方2026-09-14-まとめ] の「確認後の後片付け」表を参照 |

## 6-3 バージョン記録（PR #60、2026-09-15）

対象: `Assets/DDrive/Editor/Versioning/VersionStamp.cs`（`VersionStampProcessor`/`VersionStampSuppression`）、`Assets/DDrive/Editor/Inspector/VersionStampGui.cs`、`Assets/DDrive/Editor/AssetBrowser/AssetBrowserWindow.cs`（一覧行の「更新者」「更新日時」列）。設計は [09_editor_tools.md] §4.1。

1. 任意の Data（既存の SeData 等、または確認用に新規作成したもの）を Inspector で開く。「エディターで開く」ボタン直下に `v数字・ユーザー名・日時` の 1 行が出ていること（未保存で Version<=0 の場合は「未保存(保存すると v1 になります)」と出ること）
2. Data 上のいずれかのフィールドを 1 つ書き換えて保存する（フォーカスを外す、または Ctrl+S）。手順1の版数が1つ増え、更新者が自分の Windows ユーザー名、更新日時が保存した時刻（`yyyy-MM-dd HH:mm`）になっていること
3. 何も変更せずに保存しても版数が増えないこと（未変更のアセットは対象外であることの確認）
4. 手順2の操作を Ctrl+Z で取り消すと、版数・更新者・更新日時も含めて直前の値に戻ること（`Undo.RecordObject` の確認）
5. `Tools > D-Drive > Asset Browser` を開き、一覧の対象行に「更新者」「更新日時」の列が出て、Inspector と同じ値になっていること（列見出しでの並べ替えはまだ未実装なので試さなくてよい）
6. **一括処理で版数が上がらないことの確認**（いずれか1つで十分）: `Tools > D-Drive > 仕様書と同期` の「適用」、または `Tools > D-Drive > Generate > Addressables 登録を同期` 等の一括処理を実行したとき、実際にフィールドを書き換えていない既存 Data 群の版数が上がらないこと
7. **新規作成時の v1**: 任意の専用エディタの「＋新規作成」（または AssetBrowser の「新規」）で Data を1つ作る → 保存操作をしなくても、作成直後から `v1・自分のユーザー名・作成時刻` が表示されていること

要判断: 特になし（既知の制約 — 抑止スコープ中に別の dirty な Data が同じ保存に乗ると一時的に対象から外れる — は実運用上の実害は小さいと判断済み。詳細は [09_editor_tools.md] §4.1）

## A2 SliderSkin の存在しない Haptic ID の Validation 警告（P6、2026-09-15）

対象: `Assets/DDrive/Runtime/Ui/SliderSkinDataValidator.cs`。設計は [18_ui_controls.md] §B-7。EditMode テスト `SliderSkinDataHapticValidatorTests`（7件）で主要ケースは**自動テストで確認済み（人による確認は任意）**。

（任意の目視確認手順。Validation ウィンドウでの実際の見え方を確かめたい場合）

1. 任意の SliderSkinData を1つ選ぶ（または確認用に新規作成する）。Inspector で `NotchHapticId` または `LimitHapticId` に、実在しない値（例: `12345`）を入力する
2. `Tools > D-Drive > Validation > Run All` を実行 → 対象の SliderSkinData の行に `NotchHapticId(0x3039) の Haptics アセットが見つかりません` のような Warning が出ること
3. `NotchHapticId`/`LimitHapticId` を `0` に戻すか、実在する HapticsData の ID に変更して再実行すると、その Warning が消えること

要判断: 特になし

## 6-9 仕様書差分の Validation（PR #63、2026-09-15）

対象: `Assets/DDrive/Editor/Validation/SpecDiffValidator.cs`。設計・読み替え表は [32_spec_web.md]「実装メモ（2026-09-15、6-9）」（[27_spec_sheet.md] §6 の旧検査からの読み替え）。EditMode テスト `SpecDiffValidatorTests`（17件）で主要ケースは確認済み。

1. `Tools > D-Drive > Validation > Run All` を実行する
2. 仕様書同期（`Tools/SpecWeb`）をまだ使っていない場合: Info「仕様書のスナップショットが見つかりません」が1件だけ出て、他の仕様書関連の検査は出ないことを確認する（ここまでで確認終了でよい）
3. 仕様書同期を使っている場合（`Specs/assets.json`・`Specs/tuning.json` が存在する場合）、下記の読み替え表のケースが実際に再現できるものだけ、意図的に1つ状況を作って確認する
   - Web 側で発注済みだが D-Drive に Data 未作成の項目がある → Info「未作成」
   - Web 側の状態が「インポート済」なのに、対応する Data が Placeholder（必須参照未設定で Validator Error）のままの項目がある → Warning
   - Web 側のスナップショットに無い（削除・アーカイブ・リネーム済み）発注由来の Data が残っている → Info
   - `TuningTable` の値が `Specs/tuning.json` の min/max の範囲外になっている → **Error**（CI を fail させる重度。確認したらすぐ範囲内に戻す）
4. （任意）`Specs/assets.json` を一時的に壊れた内容（JSON 構文エラー等）にして再実行 → 例外にならず Warning に変換されて継続することを確認する。確認後は `git checkout -- Specs/` 等で元に戻す

要判断: 特になし（「本番なのに Placeholder」の判定は既存 Validator の実行結果を再利用する設計。詳細は [32_spec_web.md] 該当節）

## 6-9b D-Drive 用 AI エージェントスキル（PR #59、2026-09-15）

対象: `.claude/skills/ddrive-agent-workflow/SKILL.md` + `references/new-asset-type-checklist.md`、リポジトリ直下 `AGENTS.md`。

1. `SKILL.md` と `AGENTS.md` を通して読み、CLAUDE.md §0 の禁止事項・新種別追加の手順・MCP 検証ループの説明が実際の運用と食い違っていないか確認する（違和感があれば指摘・修正を依頼する）
2. （任意）実際に AI エージェント（Claude Code 等）に「新しい AssetType を1つ追加してみて」と試しに頼んでみて、`references/new-asset-type-checklist.md` の11項目（enum・Data・Manager+facade・Validator・専用エディタ・テスト・docs 更新・Addressables 確認 等）どおりに一式が揃うかを確認する。実際に追加する種別が要らない場合は、着手前に取りやめてよい（このステップはお試し用）

要判断: 特になし（机上検証（Shake/Haptics/CameraFxEditorWindow の実コミットとの突き合わせ）はチケット側で完了済み。実際にエージェントへ依頼して確かめるのは今回が最初の機会になる）

## 6-4 オンボーディング + デザイナーマニュアル（PR #61、2026-09-15）

対象: `docs/DesignerManual/getting-started.html`、[34_onboarding.md]、[35_tutorial_video_scripts.md]、`docs/README.md` 目次。

1. `docs/DesignerManual/getting-started.html` をブラウザで開く（または Unity の「？ マニュアル」ボタン → ページ一覧から開く）。「はじめての15分」の内容を実際に15分程度で終えられそうか、つまずきそうな箇所の tip が十分か目を通す
2. [34_onboarding.md] を読み、環境構築・リポジトリ地図・「ID だけでモックを作る」流れ・検証ループ・CI・AI エージェント・発注ツールとの関わり方の説明が、実際の手順（本書や SKILL.md 等）と矛盾していないか確認する
3. [35_tutorial_video_scripts.md] の5本分の台本（① SE 登録 ② VFX 配置 ③ Presentation で剣攻撃 ④ 発注ツール〜インポート済 ⑤ Validation で赤を直す）を読み、カットごとの画面操作・ナレーションが実際の画面と合っているか確認する（収録前に台本のズレを直しておくため）
4. `docs/README.md` の目次に 25/29/30/34/35 が反映されていることを確認する（すでに反映済みのはず）

残作業（人の作業、本書の確認対象外）: 台本に沿った実際の動画収録・公開先の決定。デザイナーマニュアルのスクリーンショット撮影リスト（[36_manual_screenshot_list.md]）に沿った実際のスクリーンショット撮影も人（またはエージェントの画面キャプチャ）の作業。

要判断: 特になし

## 6-0 実機 v4 確認結果を読む（[29_network_device_test.md] §12、既にオーケストレーターが実施済み）

対象: [29_network_device_test.md] §12「v4 実機確認」。

1. [29_network_device_test.md] §12 を読み、遅延 0ms / 200ms の各ラウンドで合格していること、VFX の蓄積・切断後の残留が解消していることを確認する
2. 残課題 K2（通信停止中の `rtt_app_ms` 固着）・K3（Signal が Play より先に届くと未知キーで破棄）は 6-6 で対応済みのコードが main に入っているが、**実機/ローカル結合での再確認（v5）はまだ行われていない**ことを把握する（下記「実装中・これから」参照）

## 6-1 CI 完全化（P7 末へ延期。確認不要）

対象: [33_ci_setup.md]。

**この節は確認不要**。2026-09-15 のユーザー決定で、GitHub Actions セルフホストランナーの本稼働（ランナー登録・PR ゲート化）は P7 末に延期された。現在の `.github/workflows/ci.yml` は手動実行のみで、日常の検査は `Tools/CI/run-ci.cmd` と Unity の Test Runner で行う（[33_ci_setup.md] 参照）。ランナー登録・ブランチ保護の設定はユーザー作業として引き継がれているが、P7 末まで着手しない。

## 6-2 パフォーマンス計測・0 alloc 検証（PR #65、2026-09-15）

> **2026-09-15 追記**: このチケットは本書の着手時点では未着手だったが、作成中に別作業で main へ merge された（`91e2bea`/`e99d5d7`）。そのため「実装中・これから」の枠ではなく確認済み節として記載する。

対象: 新規 asmdef `DDrive.Tests.Performance`（`Assets/DDrive/Tests/Performance/`）、`Assets/DDrive/Foundation/Pool/PoolService.cs`（Rent/Return の alloc バグ修正）、`Tools/CI/run-ci.cmd`（`[5/5]` ステップ追加）、`Packages/manifest.json`（`com.unity.test-framework.performance` 追加）。

1. Unity Editor で `Window > General > Test Runner` を開き、PlayMode タブでカテゴリ `Performance` で絞り込む（または `DDrive.Tests.Performance` asmdef 配下のテストだけ実行する）。全件 green（赤が無い）ことを確認する
2. コンパイルエラーが無いこと（Unity コンソールで Error 0件）を確認する
3. （任意）`Tools\CI\run-ci.cmd` をコマンドラインから実行し、`[5/5] Performance テスト` のステップが `[OK]` になること、`TestResults\performance-results.xml` が生成されることを確認する（Unity Editor を閉じている必要がある。手順1で確認済みなら省略可）
4. 既知課題の扱いを把握する: Spawn/Play 系（`VfxManager.SpawnDataLocal`・`AudioManager.PlaySeData`・`PresentationManager.PlayLocalInternal`）は Instance を1個 new する既存設計のため厳密な 0 alloc ではなく、対応するテスト（`*_RecordsAllocForKnownIssue`）は Performance レポートに記録するだけで assert しない。これは意図した仕様であり、テストの一部が「緩い」ことを不具合と誤認しないように確認する（[12_review.md] §3 参照）

要判断: 特になし（GitHub Actions での CI 組込みは P7 末に延期。詳細は [33_ci_setup.md] §8）

## 6-7 2 クライアント自動テスト（Loopback ⇔ NGO 両ブリッジ、2026-09-15）

対象: `DDrive.Runtime.Net.NetCheckJudge`(判定の純関数、`Assets/DDrive/Runtime/Net/NetCheckJudge.cs`)、`Assets/DDrive/Samples/NetCheckRunner.cs`(自動判定の組込み)、`Assets/DDrive/Runtime/Net/NetLaunchArgs.cs`(`-ddrive-autotest-seconds` 追加)、`Tools/CI/Run-NetCheck.ps1` + `Tools/CI/run-netcheck.cmd`(ローカル 2 プロセス起動・両ログ突き合わせ)、`Tools/CI/Summarize-Results.ps1`(`-NetCheckResultsPath` 追加)、`Tools/CI/run-ci.cmd`(任意ステップ `[6/6]`)。判定条件・実装の詳細は [11_tasks.md] 6-7 と [29_network_device_test.md]「自動判定つきローカル2プロセス確認(6-7)」節。EditMode テスト `NetCheckJudgeTests`(19件→2026-09-15 修正で 23件)・`NetLaunchArgsTests` 追記(3件)で判定ロジック自体は確認済み。

**2026-09-15 追記**: `NetCheckBuilder.Build()` → `run-netcheck.cmd` を実際にビルド済み exe で初めて
通したところ、4 シナリオ全てが FAIL した。ログで原因を確認し、判定条件・シナリオ設定側の不備(⑤の
判定対象が Host にも掛かっていた、`disconnect` シナリオで `ConnectedAtEnd=false` を無条件 FAIL に
していた、遅延シナリオの位相しきい値・偽造 Cancel の in-flight 除外、`latejoin` の分母、`.cmd` の
文字化け)と、`CatalogContentHashGate`(6-5)の実バグ(Host が自分自身の `OnClientConnectedCallback`
にも保留期限を登録してしまい、5 秒後に必ずタイムアウトして `LastStatusText` が `"OK"` から
`ContentHash 未受信` に戻る)を修正した。詳細は [29_network_device_test.md]「初回実行結果と判定バグ
修正(2026-09-15)」節。**→ 2026-09-15 修正後にエージェントがメインで再ビルドして実行し、4 シナリオ全て
PASS を確認済み**([29_network_device_test.md]「2 回目(判定修正後)の実行結果」節)。人の確認は、下の手順で
同じ結果になることを見るだけでよい。

1. Unity Editor で `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド` を実行し、`Builds\DDriveNetCheck\DDriveNetCheck.exe` が最新のコードでビルドされていることを確認する(コンパイルエラー 0件も併せて確認)
2. Unity Editor を閉じずに実行してよい(この exe は別プロセスとして起動する)。リポジトリ直下で `Tools\CI\run-netcheck.cmd` を実行する
3. `pair0`(遅延0ms)→`pair200`(遅延200ms)→`latejoin`(Host起動12秒後にClient接続)→`disconnect`(Hostが先に終了し、Clientが切断を検知)の4シナリオが順に走ることを確認する(所要時間の目安: 各シナリオ25〜35秒、合計で**約5分程度**)
4. 各シナリオで `Host : PASS (...)` / `Client : PASS (...)` / `Signal 中継(位相差): PASS (...)` の3行が出て、最後に `=== すべてのシナリオが PASS です ===` と表示され、終了コード 0 になることを確認する
5. FAIL が出た場合は `TestResults\NetCheck\<シナリオ名>_host.log` / `_client.log` を開き、`[DDriveNetCheck] RESULT=FAIL scenario=... reason=...` の行(自プロセス側の判定理由)と、`Host`/`Client`/`Signal 中継` のどれが FAIL したかを確認する。`TestResults\NetCheck\summary.md` にも同じ内容がまとまっている
6. (任意)`Tools\CI\run-netcheck.cmd pair0` のように1シナリオだけ指定して再実行できることを確認する
7. (任意)`Tools\CI\run-ci.cmd` を実行し、ビルド済み exe がある状態で `[6/6] NetCheck` ステップが走り、結果サマリ(Validation/EditMode/PlayMode/Performance と同じ表)に "NetCheck (6-7、2 クライアント自動テスト)" の行が載ることを確認する

要判断:
- Signal 中継の位相差のしきい値は [docs/29] §4 の「目安100ms以内」に対し、ローカル実行のノイズ耐性として `Tools/CI/Run-NetCheck.ps1` 側で「シナリオのシミュレート遅延(片道 ms)+ 150ms」を機械判定に使っている(2026-09-15 修正。目安そのものは変えていない)。実測してマージンが厳しすぎる/緩すぎると分かった場合は同スクリプトの `$PhaseDiffMarginMs` を調整する
- 6-5(ContentHash)は 2026-09-15 に main へマージ済み(PR #70)のため、`NetCheckJudge` の判定に「ContentHash 一致(開発ビルドでは不一致でも警告のみで継続)」を含めている。Host/Client で同じビルドを使う本手順では常に一致するはずなので、`content_hash_not_ok` で FAIL する場合は実際の不整合(カタログの取り違え等)を疑う
- 初回起動時に Windows ファイアウォールの許可ダイアログが出る場合、`run-netcheck.cmd` は無人実行のため応答できずタイムアウトする。[29_network_device_test.md] §2 の通り、この PC では `ddrivenetcheck.exe` の受信許可ルールが既に作成済みのため通常は出ない想定だが、実行パス(`Builds\DDriveNetCheck\DDriveNetCheck.exe`)が変わった場合は再度出ることがある

---

## 実装中・これから（実装後に追記）

> 以下は着手中、または未着手のチケット。見出しと確認観点の仮置きだけ用意した。実装が終わったら、上の各節と同じ形式（対象・確認手順・要判断）で書き足す。

### 6-6 受信検証・レート制限・ネット Validator — K2/K3 の v5 実機（またはローカル結合）確認

コード自体は 6-6（PR #64）で main に実装済み（`Assets/DDrive/Runtime/Presentation/PresentationManager.cs` の受信検証・レート制限、`Assets/DDrive/Runtime/Net/NgoNetBridge.cs` の K2/K3 対応、`NetModeUnsetValidator` 等）。ただし [29_network_device_test.md] §13 の**実機/ローカル結合での再確認（v5）はまだ実施していない**。以下は同節から分かっている確認観点（実施後にこの節を本文として書き直す）:

- 200ms 遅延 + 通信停止を再現し、`rtt_app_ms` が停止中も固着せず経過時間に応じて増え続けること（`rtt_app_stale=1`）、復旧後に実測値へ戻ること（K2）
- 通信復旧直後に Signal が Play より先に届いて「未知のキーとして破棄」されるログが出ないこと（出ても、その後 Play が保留期限内に届けば効果が実際に発生すること。1秒を大きく超えて遅れた場合の破棄は仕様どおり）（K3）
- 既存の判定項目（接続・Late Join・偽造 Cancel 破棄・切断検知・VFX の蓄積なし・切断後の残留なし）が回帰していないこと
- 確認後は [28_manual_verification_phase5.md] と [29_network_device_test.md] の該当節に結果を追記する

### 6-5 カタログ ContentHash 生成 + 接続時照合

未着手（2026-09-15 時点、[11_tasks.md] の AC 欄に ✅ 無し）。実装後、以下の観点を確認する想定:

- Host/Client で ContentHash が一致する通常時は問題なく接続できること
- 意図的に不一致を作った場合、開発ビルド・エディタでは警告のみで継続し、Play/Presentation 等の再生が壊れないこと（ユーザー決定どおり）
- リリースビルドでは不一致時に切断されること、切断理由がログ等で分かること
- Validation に「ContentHash 生成対象外」等の CI 用検査が追加されるならその表示

### 6-8 受け入れデモ（要件 §7 成功基準の5項目）

**手順書・事前確認チェックリスト・証跡の現状は [38_acceptance_demo.md](38_acceptance_demo.md) に用意済み（2026-09-15）。実施とリード承認は人の作業**（[00_requirements.md] §7 の5項目、2026-09-15 ユーザー決定）。

- 要件定義書 §7 の5項目それぞれの手順・期待される結果・証跡（既存/未取得）は [38_acceptance_demo.md] §1/§2 を参照
- デモ前に揃えるものは [38_acceptance_demo.md] §3 の事前確認チェックリスト（Validation Error 0・EditMode/PlayMode green・性能テスト・実機 v5・6-7・デザイナーマニュアル・Web 発注ツール）
- CI 代替の扱い・⑤ の位相判定基準・実施タイミング等は [38_acceptance_demo.md] §4「リードへの確認事項」に記載。判断後、実施してリード（この場合はユーザー本人）が [38_acceptance_demo.md] §0 の記入欄と本書・[11_tasks.md] に承認結果を記録する

---

## Web 発注ツールの確認について

Web 発注ツール（`Tools/SpecWeb`、O-12〜O-16 等）の確認手順は [28_manual_verification_phase5.md] に既にあるため、本書では重複させない。反映後の目視確認は [28_manual_verification_phase5.md#0-確認の進め方2026-09-14-まとめ] の「②仕様書系」チェックリスト内、O-12〜O-16 および各追補の行を参照。
