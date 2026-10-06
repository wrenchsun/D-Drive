# v1.4.0 リリース準備（チェック表・当日の手順・リリースノート・渡す文面）

> 作成: 2026-10-06。**このページは準備であり、版上げ・タグ・push・SpecWeb のデプロイは未実施**（人による確認の結果を待ってから行う）。リリース手順の正本は [12_review.md](12_review.md) §7、版の決まりは [42_distribution.md](42_distribution.md) §4.1・§5.11、変更の中身は [CHANGELOG.md](../CHANGELOG.md) の `[Unreleased]`（2026-10-06 に v1.4.0 に出せる形へ整理済み）。
> 関連: [11_tasks.md](11_tasks.md)（FC・P-15・M-4・P-16）、[51](51_tdrive_integration.md)（T-Drive 連携）、[52](52_manual_verification_fc.md)・[43](43_manual_verification_2026-09-17.md)（人による確認）、レビュー [53](53_review_fc_2026-10-03.md)〜[58](58_review_round5_p15fix_2026-10-06.md)。

## 1. リリース前のチェック表（2026-10-06 時点の事実）

| 項目 | 状態 | 備考 |
|---|---|---|
| 自前レビュー docs/53〜59 | **実施済み**（7 本。docs/59 は 2026-10-06、指摘 8 件 = P2 が 2 件・P3 が 6 件、P1 は 0 件） | docs/59 の指摘は修正ラウンド 7 で対応（GB-R-01・02 を修正、GB-R-03 は見送り、GB-R-04〜08 は対応または理由つきで見送り）。**修正ラウンド 7 の差分は新たに未レビュー**（小さい。Validator の条件 2 か所と案内・テストが中心） |
| 修正ラウンド 1〜7 | **完了** | 経緯は docs/53〜59 の対応記録。CHANGELOG には最終的な状態だけを書いた |
| CHANGELOG の整理 | **完了**（本 PR） | `[Unreleased]` を「互換性（破壊の有無 / 挙動の変更 / 追加された互換面 / 検査の追加 / 持ち込み先での作業）→ 追加（機能別）→ 修正」に再編。`check-release.ps1` は green（作業ツリーがクリーンな状態で実行） |
| `run-ci.cmd` の修正 | **完了**（本 PR、[11](11_tasks.md) P-16） | 文字化けで何も実行せず green と出る問題・結果ファイルの検査・Unity が開いているときの FAIL。下の 2.2 |
| スクリプト・リリース道具の自前レビュー docs/61 | **実施済み・小さい指摘は対応済み**（2026-10-06。P1 は 0 件、P2 の GC-R-01 は v1.4.1 に見送り、他は docs/61 の「→ 対応 / 見送り」） | 対応（GC-R-02〜06・08・09 と初回実行の注意）は PR `chore/release-tools-polish`。下の 2.1・2.2 に反映済み。**見送り**（GC-R-01・07）は第 6 章 |
| バッチモードでの全段の確認 | **2026-10-06 に 1 段ずつ直接実行して OK**。**ただし PR #115・#116 のマージ後の main（`a6fc496`）では全段を通し直していない** | CHANGELOG ガード OK / マイグレーション: 未適用あり → 確認用データを修正して green / Validation: Error 118 → 0（PR #114・#116）/ Asset ID 差分なし / EditMode 失敗 0（成功 1599・保留 21）/ PlayMode 失敗 0（成功 941・保留 1）/ Performance 11 件 OK / NetCheck 9 シナリオ PASS（ビルドし直した exe）。**リリース直前に Unity を閉じて `run-ci.cmd` を全段通し直す（必須）** |
| 人による確認（別 PC。2026-10-06 に一部の結果が出た） | **一部完了・再確認待ち**。**[43] §17 = 17-1〜17-8 OK**（未実施 = バッチの `CI.ValidateAll`・行のボタンで外部エディタ・Editor の開き直し）。**[43] §16 = 必須項目 OK**（16-1・16-3〜16-6・16-8〜16-10・16-16・16-18〜16-21・16-24）、**16-2 は一部 NG**（Undo で埋め込み欄が描き直されない）。**[52] §24・4-7 は 2026-10-06 に OK（別 PC）**。**未着手 = [43] §15 の 15-5・15-25〜15-29、[52] の必須分** | 見つかった不具合（Canvas Editor の Undo の表示・`禁止 API の検査` ウィンドウの表示）と、🔒 の表示の分かりにくさは PR `fix/verification-canvas-undo-forbidden-window` で修正。**再確認 = 16-2・16-7・16-11・16-24・17-1・17-7**。§16 の「できれば」（16-11〜16-15・16-17・16-22・16-23・16-25）は未確認。**対応予定（v1.4.0、別の PR）**: プレハブモードで Idle を自動で流す・親の Canvas 側で埋め込んだ子の既定の有効 / 無効（作業用の一時切り替えも）・子を埋め込みとして登録したとき親の ElementFx にある子の配下の行を自動で削除する・ButtonWire を Canvas Editor の画面で編集する。必須の範囲（残り）= [43] §15 の 15-5・15-25〜15-29、[52] §1 の 1-1〜1-15、§4 の 4-2・4-4・4-5、§11.2 の 11-8〜11-10、§15 の 15.1・15.2・15.6。結果が NG のものは修正してから再確認 |
| Canvas Editor の埋め込みの整理・プレハブモードで Idle（U-29、Editor のみ） | **実装済み・PR 待ち**（2026-10-06）。EditMode 全件 1700/1700・PlayMode 全件 951/951 green（新規 60 件を含む）。人による確認 = [43] §16 の 16-26〜16-35 | 互換性への影響なし（Editor のみ・追加のみ）。CHANGELOG `[Unreleased]` の「Canvas」に記載済み |
| **Player ビルドでカットシーンのトラック / マーカーが読み込まれない不具合（M-6、2026-10-06）** | **修正済み・PR 待ち**。型を 1 型 1 ファイルに分け、既存の `.playable` はマイグレーション `cutscene-timeline-monoscript-v1` で直す（**v1.4.0 は「マイグレーションあり（自動）」になった**）。NetCheck の `cut_local` が全トラック種別の読み込みを見る。結果は [11](11_tasks.md) M-6・[29](29_network_device_test.md) §27 | v1.0.0 からの不具合。**v1.4.0 のタグ前に MS2026 でマイグレーションの動作を確認する**（`.playable` が無ければ何も起きない） |
| Edit Mode プレビュー中のシーン保存でカメラ姿勢が保存される不具合・Timeline API の取得失敗時の倒れ方（レビュー docs/63 GE-R-01・GE-R-02、2026-10-06） | **修正済み・PR 待ち**。保存の直前に元の姿勢へ戻し保存の後に書き直す。API を読めなければ書かない。EditMode 1755・PlayMode 951 全件 green。Editor のみ（PATCH 相当、CHANGELOG の「修正」） | 人による確認は [52](52_manual_verification_fc.md) §24(2026-10-06 に 24-1〜24-5 すべて OK、別 PC・main a8f32db) |
| Cutscene のマーカーの NetCheck シナリオ（N-8、開発用の確認道具） | **実装済み・PR 待ち**（2026-10-06）。ローカル複数プロセス（実 NGO）で既存 9 + 新 6 = 15 シナリオ PASS、EditMode 1730/1730・PlayMode 951/951 green。**実機（[29] §27 R1〜R5、Host 1 + Client 3）は 2026-10-06 に全 PASS = [52] 4-5 合格** | **Player ビルドで Signal トラック / マーカー（`CutsceneSignalTrack` 等、クラス名とファイル名が違う型）が読み込まれない不具合の疑い**を発見（本体は未修正）。v1.4.0 のタグ前に判断が要る = [14] §23.1 |
| SpecWeb（HTML マニュアル） | 再生成済み（`Tools/SpecWeb/html/manual/**` はコミット済み）。**デプロイ未実施** | リリース後に `push.cmd` + デプロイ ①②（下の 2.1 手順 8） |
| Timeline の人による確認（[43] §7・§10） | v1.4.0 の後でよい | [46](46_cutscene_fbx_request_unitychan.md) の FBX の到着後 |
| 版上げ・タグ・push | **未実施** | `bump-version.ps1` は実行していない。`package.json` は 1.3.1 のまま |

### 1.1 起きたら決めてほしいこと

- **§5.8 の「緊急の例外」の承認（2026-10-06 起票）**: M-6 の `DD-CUTSCENE-LEGACY-SCRIPT-REF` を最初から Error で入れる件は、[42](42_distribution.md) §5.12 の 1 のユーザー承認が**未取得**。v1.4.0 のタグ前に承認の可否を決め、承認なら §5.8 の項に日付を書く（不承認なら Warning に下げる）。

## 2. リリース当日の手順

### 2.1 順序つきの手順

前提: メインの checkout（`C:\Users\yamag\wrench\D-Drive`）の `main`。docs/59 の指摘への対応と、人による確認の NG の修正が全部マージ済みであること。

1. **Unity Editor を閉じる**（`Temp\UnityLockfile` が掴まれていると `run-ci.cmd` は最初に FAIL で終わる。閉じる前に、NetCheck の exe を使うなら `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド` で**最新のコードから作り直す**。`Builds\DDriveNetCheck\DDriveNetCheck.exe` が古いままだと、8 段目は古いコードを検査する）。
2. `git switch main && git pull --ff-only`、`git status` が clean であること。止まりやすい点: Unity を開いて設定画面・更新ウィンドウなどを触ると `ProjectSettings/DDriveProjectSettings.asset` が書き換わる（[58] GA-R-12）。差分があれば中身を見てコミットするか戻す。
3. **`Tools\CI\run-ci.cmd` を全段**（リポジトリ直下から。cmd・PowerShell は `Tools\CI\run-ci.cmd`、Git Bash は `./Tools/CI/run-ci.cmd`。`cmd //c "Tools\\CI\\run-ci.cmd"` でも動く。bash はバックスラッシュを消すので `Tools\CI\run-ci.cmd` とは直接書けない）。`=== ALL GREEN: ran 8 steps, skipped 0 steps ===` が出ること。**「skipped」が 0 でないときはその段が実行されていない**（pwsh が無い = 1 段目 / ビルド済み exe が無い = 8 段目）ので、リリースでは 0 にする。
   - 結果の読み方: 各段は `[OK]` / `[FAIL] step N …` で終わる。EditMode・PlayMode・Performance は**終了コードではなく結果 XML の `failed`** で判定する。`-nographics` では描画系のテスト（EditMode 21 件・PlayMode 1 件）が Inconclusive になるのが正常で、`failed 0, with Inconclusive or Skipped tests` と出る（Unity の終了コードは 2 になるが FAIL ではない。`Assert.Ignore` の Skipped だけの実行も同じ扱い。Inconclusive も Skipped も 0 なのに終了コード 2 のときだけ FAIL）。想定値（2026-10-06）: EditMode 成功 1599・保留 21、PlayMode 成功 941・保留 1、Performance 11 件。**最後の `=== Result summary ===` の表に、各段の `全 N 件 / Passed / Failed / Inconclusive / Skipped` が 1 行ずつ出る。件数は判定に使っていない（期待値との比較はしない）ので、この想定値と目で見比べる**（桁違いに少なければ、テストアセンブリの一部だけが走っている）。8 段目をスキップしたときは NetCheck の行が `skipped` と出る（前回の結果は開始時に消す）。
   - 止まりやすい点: (a) `[FAIL] Unity Editor is open …` = Unity を閉じていない。(b) `[FAIL] step N …: log file was not produced` = Unity が起動しなかった（ライセンス・パス。`UNITY_EXE` を引数で渡す）。(c) 4 段目（`git --no-pager diff --exit-code --stat` + 未追跡ファイルの検査）は ID と無関係な差分（Unity が何かを保存した）でも FAIL になる。画面に出る一覧で中身を見る（ページャは出ない）。(d) 2 段目の FAIL は 2 通りに分かれて出る: ログに `[DDrive][Migration]` の行がある = 「未適用のマイグレーションの可能性」（確認用データが古い形のままのときに出る。2026-10-06 に一度出て確認用データを直した）／ 無い = 「Unity 自体の失敗」（ライセンス・コンパイルエラー・別インスタンス。ログを読む）。(e) 3 段目の Validation は確認用データの Error（2026-10-06 に 118 → 0）。
4. **`pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Release/check-release.ps1`** と、**起点を前のタグにした `… check-release.ps1 -GuardOnly -Base v1.3.1`**（当日は `main` = `origin/main` なので、既定の起点 `origin/main` のままでは CHANGELOG ガードが何も比較しない = [61] GC-R-09。2026-10-06 に実行して、スナップショットが v1.3.1 から変わっていて `CHANGELOG.md` も変わっているので green。`-GuardOnly` なしで `-Base v1.3.1` を付けると `version` が上がっているかも見るので、版上げ前は FAIL が想定どおり）。前者は（検査専用。作業ツリーがクリーン・`[Unreleased]` の互換性節が空でない・`DDriveProtocol.Current` を変えていれば「ネットメッセージ」の記述がある〔今回は変更なし〕・CHANGELOG ガード）。`run-ci.cmd` が `TestResults/`（gitignore 済み）以外を書き換えていると作業ツリーのクリーン検査で止まる。
5. **`pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Release/bump-version.ps1 -Version 1.4.0 -DryRun`** で差分だけ確認 → 問題なければ **`… -Version 1.4.0 -Tag`**（`-Part minor` でも同じ。事前チェックは check-release と同じ）。書き換わるもの: `Packages/com.ddrive.core/package.json` の `version`、`Packages/com.ddrive.core/Runtime/DDriveVersion.cs` の `Value`、`CHANGELOG.md`（`## [Unreleased]` → `## [1.4.0] - <実行日>`、新しい空の `## [Unreleased]` を上に追加）。**同期されるもの**（版が変わらなくても毎回）: `docs/DesignerManual` → `Documentation~/DesignerManual`、`docs/ProgrammerManual` → `Documentation~/ProgrammerManual`、`docs/migrations` → `Documentation~/migrations`、`docs/50_consumer_guide` → `Documentation~/ConsumerGuide`（いずれも robocopy のミラー）、`CHANGELOG.md` → `Packages/com.ddrive.core/CHANGELOG.md`。`-Tag` は明示パスで `git add` → `git commit -m "Release v1.4.0"` → `git tag -a v1.4.0`（push はしない）。止まりやすい点: 作業ツリーが clean でないと止まる / 日付は**スクリプトを実行した日**になる（リリース日にずれたくなければその日に実行）/ 同期の差分（`Documentation~/` の旧い案内が新しくなる）がリリースのコミットに含まれるので、`-DryRun` の出力で意図しない差分が無いか見る。
6. 確認: **`pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Release/check-release.ps1 -Base v1.3.1`**（版上げ後なので version の検査も含めて green になる想定）、`git show --stat HEAD`（Release コミットに版・CHANGELOG・同梱物だけが入っている）、`git describe --tags` が `v1.4.0`。Unity を開いて再コンパイルが通り `PackageVersionConsistencyTests`（`package.json` ↔ `DDriveVersion.Value` ↔ CHANGELOG 見出し ↔ manifest の依存版）が green であること（開く前に push しない）。
7. **`git push origin main` と `git push --tags`**（明示的に許可されたときだけ）。タグが指すコミットに版の更新が含まれていることを 6 で確認済みであること。
8. **SpecWeb**: `Tools\SpecWeb\push.cmd`（`build-manual.js` の再生成 + `clasp push`）。生成物に差分が出たらコミットが要る。**デプロイ①（人向け SPA）は `push.cmd` の後に更新、デプロイ② は UI で更新**（`clasp update-deployment` は ② の設定を壊すので使わない）。この PR では実行していない。
9. **持ち込み先（MS2026）への案内**: 下の 4 の文面を送る。MS2026 側は `manifest.json` の `#v1.4.0` への更新を `Tools > D-Drive > Update > 更新ウィンドウ` で行う（[42] §4.2）。T-Drive 側には 5 の文面。
10. GitHub のリリース（任意）: 下の 3 の下書きを `v1.4.0` のリリースノートに貼る。

### 2.2 `run-ci.cmd` の変更（P-16）で知っておくこと

- ファイルは **ASCII だけ**にした。UTF-8 の日本語を含む .cmd は、コードページ 932（日本語 Windows の既定）で起動した cmd.exe が行の境目を読み違え、コメントの断片をコマンドとして実行して「何も実行せず green」になった。`chcp 65001` を先頭に置くだけでは、932 で起動したときも、ファイルが長いときも直らなかった（実測）。日本語の説明は docs に置く。
- `run-netcheck.cmd`（8 段目から呼ばれる）も、2026-10-06 の修正ラウンド 7 で**全 ASCII**にした（メッセージは英語、`chcp` は外した。引数・終了コード・`Run-NetCheck.ps1` の呼び出しは同じ）。932 のコンソールから存在しないシナリオ名で起動して、cmd 側の出力が化けないことと終了コード 1 を確認した（NetCheck の実行そのものはしていない）。pwsh が出す日本語のメッセージ（`Run-NetCheck.ps1`）は 932 のコンソールでは化けることがあるが、判定には影響しない。
- 補助スクリプト `Tools\CI\check-test-result.cmd`（結果 XML の `failed` を読む。pwsh 不要）は単体でも使える。終了コード 0 = OK、10 = OK（Inconclusive または Skipped あり。件数を表示）、1 = FAIL。それ以外の終了コードは `run-ci.cmd` が FAIL にする。

## 3. GitHub のリリースノートの下書き（v1.4.0）

> 以下をそのまま貼れる形にしてある。CHANGELOG の整理後の内容に基づく。

**D-Drive v1.4.0（`com.ddrive.core`）**

T-Drive（Facial / Toon）との連携のための汎用の拡張点、Canvas の埋め込み、更新ウィンドウの他パッケージ対応、禁止 API の許可の仕組みを追加した **MINOR リリース**です。**破壊的変更はありません**（公開 API・シリアライズ形式・enum・ID・ContentHash・ネットメッセージ・生成コードは追加のみ。データのマイグレーションもありません）。

**更新して変わること**
- **0 秒に置いたマーカーが鳴るようになります**（Event / Signal / Shake / Haptic と外部マーカー）。ネット再生の受信側は、開始位置から遡って 0.5 秒以内のマーカーを鳴らし、それより古いものは無音です（遅延が 0.5 秒を超えた端末では 0 秒のマーカーは鳴りません）。既存のカットシーンで冒頭にマーカーを置いていると、最初に鳴ります。
- プールへ戻すとき、ルートの**すべての `IPoolable`** に `OnReturn` が呼ばれます（以前は最初の 1 個だけ）。モデルは返却時にブレンドシェイプの重み（表情）が最初の値へ戻ります。
- 知らないシェーダーの Material を対話的な操作で扱うと、確認ダイアログが出ます（元のまま保つ / `DDrive/Lit` に変換 / キャンセル）。独自シェーダー（T-Drive の Toon 等）を使うプロジェクトは `MayaImportProfile` の `UnknownShaderPolicy` を `KeepSource` にしてください。シェーダー参照が欠けている間は、既存の MaterialData を書き換えません。
- 更新ウィンドウは認証のログイン画面を出さなくなりました。private リポジトリの資格情報が切れているときは、先に端末で `git ls-remote <URL>` を一度実行してください。
- `Validation > Run All` / `CI.ValidateAll` の、プロジェクト全体の指摘は「(project)」と表示されます（JUnit の classname で絞り込んでいる CI は見直してください）。
- 固定値（Constant）の ValueDef の Time を検査しなくなりました（新規作成した Anim2D・Skin・CameraShake が Error になる問題の修正）。
- 依存関係の追跡が Timeline の中まで届き、Cutscene の Timeline からだけ参照されているアセットは「使用中」になります。
- そのほか: 埋め込み Canvas の優先規則と `SendSignal` のパス、シェーダー変換表の優先順位（`Assets/` > 外部 > 同梱）、検査の警告の調整（Warning が減る方向）など。詳細は CHANGELOG の「互換性」節。

**更新後にやること**
- **更新後に「更新を適用」を必ず実行してください**（全プロジェクト。マイグレーション `cutscene-timeline-monoscript-v1` が走り、未適用だと `CI.MigrateCheck` が赤になります。カットシーンの Timeline があれば `.playable` に差分が出ます。実行しないと Player ビルドで D-Drive のトラック / マーカーが読み込まれない）。手順は下の (c)。タグを `v1.4.0` にして `Tools > D-Drive > Update > 更新ウィンドウ` を実行してください。
- 更新後に `Validation > Run All` と `Tools > D-Drive > Validation > 禁止 API の検査` を実行し、禁止 API の指摘を 1 件ずつ「D-Drive の API に直す / 許可コメントを書く」に仕分けてください（手順は持ち込み先ガイドの運用ページ「禁止 API の指摘への対処」）。「アセットの読み込み設定が Preload でない」は Validation の「自動修正」で直ります。

**追加されたもの**
- **T-Drive 連携の拡張点**: カットシーンの「同じ相手へのバインド」（`SameAsTrack`）/ 外部マーカー `ICutsceneMarker` / 取り込み完了のリスナー `ICutsceneImportListener` / 現在の視点カメラ `ViewCamera` / モデルのスポーン・返却の通知 `IModelInstanceListener` / `MaterialData` のパス無効化・キーワード / 取り込みルールの外部拡張（`IImportRuleHandler` の外部実装・`IImportRuleFolderOptOut`・`IShaderConversionTableProvider`・`ITextureImportRuleProvider`）/ 外部が所有するブレンドシェイプ接頭辞 `ExternalBlendShapePrefixes` / 実行順の検査の除外 `ICameraExecutionOrderExemptionProvider`。外部拡張の契約（docs/42 §5.14）と契約テストを追加。
- **Canvas の埋め込み**（`CanvasData.EmbeddedCanvases`）と Canvas Editor の埋め込み対応。
- **更新ウィンドウが D-Drive 以外の git URL パッケージにも対応**。`package.json` の `ddriveUpdate`（`requires` / `compatibleWith`）で「vX.Y.Z 以降対応」を宣言できます。
- **禁止 API の許可**: 許可コメント `// ddrive-allow: 規則名(理由)`、Project Settings の許可リスト、`禁止 API の検査` ウィンドウ。
- 検査（Validation）の Warning / Info を追加（既存の検査の重さは変えません）。

**修正**
- **Player ビルドでカットシーンの D-Drive トラック / マーカー（Signal / Event / Shake / Haptic / SE / VFX / UI / Camera / Presentation / AnchorGroup）が読み込まれない**不具合（v1.0.0 から。Editor では動く）。型を同名のファイルに分け、既存の Timeline はマイグレーションで直す（上の「更新後にやること」）。
- Presentation / Cutscene の購読者が走査中に自分や他を止めたときの二重進行・例外、プレリリースのタグを丸めて存在しないタグを manifest に書く不具合、元の `.mat` のシェーダーが欠けているときに既存 MaterialData の色・テクスチャを上書きする不具合、プリセットギャラリーのパス、ほか。

全文は `CHANGELOG.md` の `[1.4.0]`。

## 4. MS2026 の担当へ渡す文面（Validation 53 件への返答）

> [11_tasks.md](11_tasks.md) M-4 節の「MS2026 へ返す文面」（2026-10-05。2026-10-06 の修正ラウンド 6 で登録 / 解除の対・コード例を追記）と持ち込み先ガイドの運用ページ「禁止 API の指摘への対処」（`docs/50_consumer_guide/operation.html`）に一致させてある。**新しい主張は足していない**。チャットにそのまま貼れる。

---

D-Drive v1.4.0 のリリースに合わせて、`CI.ValidateAll` の 53 件（24 件 + 29 件）への返答です。

**(a) 24 件（`Flags.Load が Preload ではありません`）は MS2026 側のデータ修正です。** `Tools > D-Drive > Validation > Run All` を実行し、該当エラーの「自動修正」（FixAction。内部は `FixPreload`）を押すと対象 Data の `Flags.Load` が `Preload` に直ります。放置すると**ビルドで音などが Placeholder になります**（同期解決 API は Preload でないと引けないため）。D-Drive の検査は正しいので D-Drive 側の変更はありません。

**(b) 29 件（禁止 API）は、D-Drive v1.4.0 に更新した後、1 件ずつ「D-Drive の API に直す / 許可コメントを書く」を仕分けてください。** 判断基準:

| 当たり | 判断 | やること |
|---|---|---|
| ゲームプレイの時間（演出・移動・クールタイム・アニメ等。ポーズ・ヒットストップに従わせたいもの） | **D-Drive の Tick に乗せる**（乗せにくければ 1 か所に集めて許可） | (a) `IAssetManager` を実装し `DDriveRuntimeBootstrap.Instance.Loop.GameLoop.Register(...)` で登録、`Tick(float dt)` の `dt`（ヒットストップ込み）を使う。ポーズは `OnPause` で受ける（ポーズ中も `Tick` は呼ばれ、`dt` は 0 にならない）。**登録は `OnEnable`、解除は `OnDisable` で必ず対にする**（`GameLoop` は破棄されたオブジェクトを自動では外さない。外し忘れると破棄後も毎フレーム `Tick` が呼ばれ、`Tick` に例外の隔離は無いので、例外が出るとその後ろに登録された Manager のそのフレームの `Tick` も止まる）。`DDriveRuntimeBootstrap.Instance` / `Loop` が null（Bootstrap の無いシーン・起動前・終了時）のときは登録せず何もしない。登録に `IsReady` は要らない（`Loop` は Bootstrap の `Awake` で揃う。`IsReady` はカタログ登録の完了）。`Tick` の中で例外を出さない。同じシーンに最初から置くスクリプトで実行順が Bootstrap（`[DefaultExecutionOrder(-1000)]`、`Awake` で起動配線）より小さいとき、または Bootstrap が後からロードされるシーン構成では、`OnEnable` の時点で `Instance` が null のため登録されず、例外も警告も出ないまま `Tick` が一度も来ません。`OnEnable` に加えて `Start` でも未登録なら登録を試し（`Register` は二重登録しても 1 回扱い）、それでも無ければ警告ログを 1 行出してください（コード例のとおり）。`Tick` の中で自分自身や他の登録済み Manager を無効化したり `Unregister` したりしないでください。`GameLoop` は登録順の添字で走査するため、走査中に外すと直後の Manager 1 つがそのフレームだけ `Tick` されません（例外は出ません。`Destroy` は遅延するので影響しません）。外したいときはフラグを立てて、次のフレームの頭か `LateUpdate` で外します。コードを自分の asmdef に置く場合は `DDrive.Foundation` と `DDrive.Runtime` への参照が必要です（asmdef なしの `Assembly-CSharp` なら不要）。(b) 乗せにくいときは、ゲーム側の時間源を 1 か所（例: `GameTime`）に作り、その中だけで `Time.unscaledDeltaTime * Loop.TimeService.TimeScale` を読んで許可コメントを 1 行書く（`Loop.PauseService.IsPaused(PauseChannel.Gameplay)` で 0 にもできる）。**`ITimeSource` はゲームのコード向けではない**（Foundation 内部向け。実体は `Time.deltaTime` を返すだけでヒットストップ・ポーズに従わない。置き換えても当たりが消えるだけで挙動は変わらない） |
| 実時間で測りたい計測（Host 引き継ぎ・LAN 探索・通信タイムアウト・ログのタイムスタンプ等。ポーズの影響を受けてはいけないもの） | **許可、または対象外の API に替える** | `// ddrive-allow: Time(Host 引き継ぎのタイムアウトは実時間で測る)`。または `Time.realtimeSinceStartupAsDouble` / `Stopwatch` に替える（`Time` 規則の対象外なので許可コメントが要らない）。`Time` 規則が当たるのは `Time.time` / `deltaTime` / `unscaledDeltaTime` / `timeAsDouble` / `unscaledTime` だけ |
| NGO の `NetworkObject` の生成（Instantiate → Spawn が正規手順で `PoolService` 経由にできない） | **許可** | `// ddrive-allow: Instantiate(NGO の NetworkObject は Instantiate → Spawn が正規手順)` |
| それ以外の `Instantiate`（Prefab を置く・演出の実体を出す） | **直す** | `Prefabs.Spawn` / プール（`PoolService`）経由 |
| 自分で書き換えられない外部コード・生成コード | **設定の許可リスト** | Project Settings > D-Drive > 禁止 API の除外 にフォルダ + 理由を足す |

**Tick に乗せるときの登録 / 解除の例**（`OnEnable` で登録、`OnDisable` で必ず解除。登録した `GameLoop` を覚えておけば、終了時に `Instance` が null でも解除できます。Bootstrap より先に `OnEnable` が走る場合に備えて `Start` でも登録を試します）:

```csharp
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Runtime.Loop;
using UnityEngine;

public sealed class GameCooldownManager : MonoBehaviour, IAssetManager
{
    private GameLoop _loop;

    public AssetType Type => AssetType.None;

    private void OnEnable() => TryRegister();

    private void Start()
    {
        // 実行順が Bootstrap より前だったときの再試行(Register は二重登録しても 1 回扱い)
        if (_loop == null && !TryRegister())
        {
            Debug.LogWarning("DDriveRuntimeBootstrap が無いため GameLoop に登録できませんでした(Tick は来ません)", this);
        }
    }

    private bool TryRegister()
    {
        if (_loop != null)
        {
            return true;
        }

        var boot = DDriveRuntimeBootstrap.Instance;
        if (boot == null || boot.Loop == null)
        {
            return false; // Bootstrap の無いシーン・起動前は登録しない
        }

        _loop = boot.Loop.GameLoop;
        _loop.Register(this);
        return true;
    }

    private void OnDisable()
    {
        if (_loop != null)
        {
            _loop.Unregister(this); // 外し忘れると破棄後も毎フレーム Tick される
            _loop = null;
        }
    }

    public void Tick(float dt) { /* ヒットストップ込みの dt。例外を出さない */ }
    public void OnPause(PauseChannel channel, bool paused) { /* ポーズ中も Tick は来る。自分で止める */ }
    public void StopAll(StopReason reason) { }
    public void OnSceneUnload() { }
}
```

**許可コメントの書き方**: **同じ行の行末**か**直前の行（コメントだけの行）**に `// ddrive-allow: 規則名(理由)` を書くと、**その 1 行の当たりだけ**を許可します（ファイル全体・ブロック全体は許可されません）。**理由（括弧内）は必須**で、空・括弧なしは無効のままです（半角 `( )` と全角 `（ ）` のどちらでも可）。規則名は `Time` / `Instantiate` / `ResourcesLoad` / `AddressablesLoad` / `AudioSourcePlay`（大文字小文字は区別しません）。1 行に 2 規則あるときは `// ddrive-allow: Time(…) ddrive-allow: Instantiate(…)` と接頭辞ごと繰り返します。使われていない許可は Info で出るので、直した後に残った許可は消してください。

```csharp
// ddrive-allow: Instantiate(NGO の NetworkObject は Instantiate → Spawn が正規手順)
var go = Object.Instantiate(networkPrefab);

var now = Time.unscaledTime; // ddrive-allow: Time(Host 引き継ぎのタイムアウトは実時間で測る)
```

自分で書き換えられない外部コード・生成コードは、設定の許可リスト（Project Settings > D-Drive > 禁止 API の除外）にフォルダまたはファイルのパス（2 階層以上。ファイルと完全一致、またはフォルダの配下。`Assets/Foo` は `Assets/FooBar/` に当たらない）・規則名（空欄 = 全規則）・理由（必須）を足します。

確認は Editor の `Tools > D-Drive > Validation > 禁止 API の検査`（許可されていない当たり・許可済み・無効 / 未使用の許可を一覧し、クリックでその行を開く。再走査ボタンあり。`Forbidden API 許可一覧` は同じ内容の Console 出力）、CI ではバッチの `CI.ValidateAll` が同じ走査をします（`Validation > Run All` は禁止 API を走査しません）。**レビューでは「許可の理由が妥当か」を見てください。**

**(c) カットシーン（Timeline）を使っている場合: 更新後にマイグレーションが走ります（M-6）。** D-Drive v1.0.0〜v1.3.1 は、カットシーンの Timeline のトラック / マーカーの型（Signal / Event / Shake / Haptic / SE / VFX / UI / Camera / Presentation / AnchorGroup）がクラス名と違う名前のファイルにあり、**Player ビルド（開発ビルド / 製品ビルド）ではカットシーンの D-Drive トラック / マーカーが読み込まれませんでした**（Editor では動くので気づきにくい不具合です。ログに `The referenced script on this Behaviour is missing!`）。v1.4.0 で型を同名のファイルに分けました。**既存の Timeline（`.playable`）は、更新ウィンドウの「更新を適用」（または `Tools > D-Drive > Update > マイグレーション(適用)`）を実行すると直ります**（`cutscene-timeline-monoscript-v1`。`.playable` の `m_Script` の行だけが書き換わるので、Timeline の `.playable` に差分が出ます。ドライランと更新ウィンドウの「3. マイグレーション」で対象の `.playable` の一覧が出ます）。実行しないと Player では今までどおり読み込まれません。カットシーンを使っていなければ `.playable` は変わりません（ただし未適用だと `CI.MigrateCheck` が赤なので、適用は全プロジェクトで必要）。**MS2026 向けの手順（必須）**: (1) 適用の前に、開いている Timeline を保存しておく（未保存のものは書き換えず警告して止まります）。(2) **manifest の更新・書き換わった `.playable`・`ProjectSettings/DDriveProjectSettings.asset` を同じコミットにする**（manifest だけ先にコミットすると、他の人が pull したときに旧形式の `.playable` が新しい D-Drive で読み込まれる状態になります）。(3) 書き換えは Undo で戻せないので、適用の前にコミットしておく。(4) 適用の**後**に旧形式の `.playable` が入ってきた場合（v1.3.1 の時代のブランチのマージ・他プロジェクトからのコピー）は、`Validation > Run All` / `CI.ValidateAll` の Error `DD-CUTSCENE-LEGACY-SCRIPT-REF`（`Tools > D-Drive > Validation > 全体の指摘を修正` でも直せます。Error にしている理由は docs/42 §5.8 の「緊急（データ破損を招く等）で最初から Error」の例外）と `CI.MigrateCheck` の赤で分かるので、もう一度「更新を適用」（または `マイグレーション(適用)`）を実行する。(5) 旧形式のまま Timeline を開いて保存してもトラック / マーカーは失われない（開発リポジトリで実測済み、[docs/64](64_review_m6_2026-10-06.md) GF-R-02）ので、慌てて作り直さず適用する。(6) `Assets/` 配下だけが対象で、Packages/ の他パッケージの `.playable` は書き換えません。T-Drive など外部パッケージの `Marker` / `TrackAsset` 派生クラスも、クラス名と同じ名前のファイルに置いてください（ProgrammerManual `extending.html`）。

更新は `manifest.json` を `#v1.4.0` にして `Tools > D-Drive > Update > 更新ウィンドウ`（更新の前に CHANGELOG の「互換性」節をご覧ください。**カットシーンを使っている場合は更新後に「更新を適用」（マイグレーション）が必要です**〔上の (c)〕。0 秒に置いたカットシーンのマーカーが最初に鳴るようになる点と、更新ウィンドウが認証のログイン画面を出さなくなる点〔資格情報が切れているときは先に `git ls-remote <URL>` を一度実行〕が挙動の変更です）。

---

## 5. T-Drive 側へ渡す文面（D-Drive v1.4.0）

> [51](51_tdrive_integration.md) §7（7.1 Facial = FT-4 宛て / 7.2 Toon = U-21 宛て / 7.3 更新の取り込み / 7.4 輪郭線ほか）と、P-15 の人による確認（[43] §15、BUG-1・Q-1〜Q-4）への対応を 1 本にまとめた。**内容は docs/51 と一致させ、新しい主張は足していない**。チャットにそのまま貼れる。

---

T-Drive の Facial（FT-4 宛て）・Toon（U-21 宛て）・更新ウィンドウについて、D-Drive v1.4.0（MINOR、破壊なし・追加のみ）の内容と、T-Drive 側でやることをまとめます。

## 1. v1.4.0 で入るもの

方針は受け入れ済みです（2026-10-03）。Facial は T-Drive 版が正で、D-Drive は汎用の拡張点（Facial 専用にしない・`TDrive.*` を参照しない）を追加のみで足しました。T-Drive は < 1.4.0 の D-Drive でも動く回避策を残して構いません。

- **同じモデルへのバインド（FC-1）**: `CutsceneBinding{ Target = SameAsTrack, SourceTrackName = "<役名>" }`。
- **プール返却（FC-2 / FC-12）**: ルートの全 `IPoolable` に `OnReturn` が呼ばれます。モデルの返却時にブレンドシェイプの重み（`FC_*`・`fcs_*` を含む）が最初の値に戻ります。`IModelInstanceListener`（`OnModelSpawned` / `OnModelReturning`）でスロット適用後・返却直前に通知されます。
- **現在の視点（FC-3）**: `ViewCamera.TryGetCurrent(subject, out ViewPose)`（`DDrive.Runtime.Viewing`）。位置・回転は Unity のワールド（m / Y-up / 左手）、縦画角は度です。
- **外部マーカー（FC-4）**: `FacialMarker : Marker, ICutsceneMarker`。跨いだ Tick で 1 回 `Fire(in CutsceneMarkerContext)`（Seek / Skip / Late Join は無音、Edit Mode は `IsEditPreview = true`）。発火は各クライアントのローカル処理なので、全員で同じ結果にしたい処理は T-Drive 側で同期してください。
- **取り込み完了のリスナー（FC-5）**: `ICutsceneImportListener`。
- **取り込みルールの外部拡張（FC-6 / FC-14）**: `IImportRuleFolderOptOut`・`IImportRuleHandler` の外部実装・`IShaderConversionTableProvider`・`ITextureImportRuleProvider`。
- **依存追跡（FC-7）**: Timeline の中の `AssetId` / `AssetRef` が使用箇所・未使用判定・安全な削除に出ます。
- **MaterialData のパス無効化・キーワード（FC-11）**、**知らないシェーダーの扱い（FC-15）**、**検査の警告の調整（FC-19）**、**`FC_` / `fcs_` 接頭辞の予約（FC-20）**。
- **外部拡張の契約（FC-10）**: 上記を含む「D-Drive が壊さない」挙動を契約テストで固定しました。T-Drive が新しく D-Drive の挙動に依存したくなったときは D-Drive に連絡してください（契約テストに足します）。
- **実行順の検査の除外（Q-4）**と**更新ウィンドウの他パッケージ対応（P-15）**。下の 2・3。

## 2. T-Drive 側でやること

**ブリッジ（`Bridges.DDrive`、`TDrive.Toon.DDriveBridge`）が切り替える点**（`com.ddrive.core` の `versionDefines` で `[1.4.0,)` のときだけ新経路。< 1.4.0 では従来の回避策に落ちる）:

1. **バインド**: 同名の AnimationTrack のバインド先を引く回避策 → `SameAsTrack`（FC-1）。
2. **fctrack の取り込み**: `postprocessOrder` では順序制御できません（D-Drive の取り込みは `EditorApplication.delayCall`）。`ICutsceneImportListener` を 1 型実装し、`OnCutsceneShotImported(result)` で `result.Roles`（役名 → トラック・元 FBX パス）と `SourcePath` の隣の `.fctrack` を見て `result.Timeline` に Facial トラックを足し、`result.Data.Bindings` に `SameAsTrack` の binding を追記します。**保存は呼ばなくてよい**（全リスナーの後に D-Drive が 1 回保存）。**再取り込みでも毎回呼ばれ、足したトラック / binding は保持される**ので、足す前に `result.Timeline.GetOutputTracks()` の名前と `result.Data.Bindings` の `TrackName` で既にあるか確認してください。**`Roles` は今回の取り込みに含まれた FBX の分だけ**です（キャラの FBX だけ再取り込みすればそのキャラ 1 件、カメラ FBX だけ先ならカメラだけ）。全キャラを対象にするなら `Data.Bindings` / `Timeline.GetOutputTracks()` から役を拾ってください。`.fctrack` が FBX より後に取り込まれた場合は従来どおり `CutsceneImportService.ComputeCutsceneDataPath` で引きます。
3. **視点**: `Bridges.DDrive` が `ViewCamera.TryGetCurrent` を視点解決の**最後のフォールバック**にします（「視点の指定（Transform）」が最優先、次に手動の角度、最後にこれ）。`TDrive.Facial.Runtime` は D-Drive を参照しません。**Runner の `LateUpdate` の実行順は `DDriveCutsceneCameraApplier.ExecutionOrder`（1000）より後**にしてください（10000 なら満たします。それより前だとカットシーン中は 1 フレーム前の姿勢）。カメラが無いときは `false`（警告なし）→ 補正をスキップ。分割画面などは、ゲーム側が `ViewCamera.Register(IViewProvider, priority)` で `subject` ごとのカメラを返します。
4. **実行順の検査の除外の宣言**: `Validation > Run All` の実行順の検査が、`FacialCorrectionRunner`（実行順 10000。カメラを読むだけ）に「Cutscene カメラ適用順 1000 以上…」の Warning を出します。**ブリッジの Editor アセンブリが `DDrive.Editor.Validation.ICameraExecutionOrderExemptionProvider` を public クラスで実装し、`yield return new CameraExecutionOrderExemption(typeof(FacialCorrectionRunner), "カメラを読むだけ(書き込みはしない)");` と宣言**してください（理由は必須。型は `System.Type` か完全修飾名の文字列）。`TypeCache` で自動発見される（public・public な引数なしコンストラクタ・アセンブリ名が `DDrive.Tests` で始まらない）ので登録は不要です。除外された型は Warning が消え、Info `DD-CAMEXEC-EXEMPT` が 1 件出ます。ブリッジを書けないときの手動の逃げ道は Project Settings > D-Drive > 実行順の検査の除外。
5. **外部マーカー**: イベント的な切り替えをマーカーで置きたくなったら `FacialMarker : Marker, ICutsceneMarker`（上の 1）。
6. **取り込みルール**: `SourceAssets/Facial/` を「不明な種別フォルダ」扱いさせない回避策は不要になります。`Bridges.DDrive.Editor` に `IImportRuleFolderOptOut` を 1 型実装し、`FolderNames` で `"Facial"` を返します（ハンドラは持たなくてよい）。Toon の変換表は `IShaderConversionTableProvider`（`GetTables()` でパッケージ内の `ShaderConversionTable` を返す。`Assets/` に生成しなくてよい。優先順位は `Assets/` の表 > 外部提供口の表 > D-Drive 同梱の表）、`T_` のテクスチャの sRGB は `ITextureImportRuleProvider`（`GetRules()` で `TextureImportProfile.Rule` を返す。Profile の `Rules` の前に評価され、Profile に同じ条件の規則があれば Profile 優先）。
7. **Toon のブリッジ**: パスの無効化・キーワードは生成シェーダーを増やさず MaterialData の `DisabledPasses` / `EnabledKeywords`（FC-11）。スロット適用後の初期化は `IModelInstanceListener`（`ToonCharacter` が実装）。輪郭線のパス（`LightMode` タグの無い URP のパス = `SRPDefaultUnlit`）は `MaterialData.DisabledPasses = { "SRPDefaultUnlit" }` で止められます。
8. **T-Drive を使うプロジェクトは `MayaImportProfile` の `UnknownShaderPolicy = KeepSource` を設定**してください（確認ダイアログ無しで知らないシェーダーを保ちます。既定の `Ask` は Model エディタの「元ファイルを再読み込み」などの対話的な操作で確認ダイアログを出し、FBX の自動取り込みは従来どおり Lit に変換します）。T-Drive の `.mat` だけが先に入ってシェーダーが未導入のとき（`Hidden/InternalErrorShader`）は、`KeepSource` でも保たず警告して `DDrive/Lit` に変換します。導入順は「T-Drive → Toon の素材」を推奨（後から入れ直しても既存の Data のシェーダーは上書きしない仕様のため）。
9. **回避策が不要になる点**: 役名のトラックを引く回避策・`postprocessOrder`・同名ショット探し（doc15 §5.5）/ 罠 2・罠 4（変換表・テクスチャ規則の置き場所）/ doc17 §3 #12 の「Albedo に白テクスチャを入れる」回避策（`AlbedoTint` が白以外の色だけのマテリアル、または割り当てシェーダーに `_BaseMap` / `_MainTex` が無いとき、Albedo 未設定の Warning は出ません）/ **`package.json` の ASCII 化は不要**（P-15 の BUG-1 の修正で、日本語を含む `package.json` を日本語 Windows でも UTF-8 で読めるようになりました）。
10. **D-Drive の挙動で T-Drive の設計に効く点**: (R-3) `Cutscene.Pause`（インスタンスの一時停止、`PauseWithGame` のグローバル Pause）中は Timeline が**評価されません**。「毎フレーム `PushOverride`、次フレームで消える」設計だと一時停止中に補正がデフォルトへ戻るので、前回値を保持し、Mixer の `OnPlayableDestroy` / グラフ停止で解除してください。(R-4) `IValidator` の入口は `AssetDataBase`（`CutsceneData`・`ModelData`）で、`FacialCorrectionData` は入口になりません。`IValidator` の実装は public・引数なしコンストラクタが必須です。**ショット先頭（0 秒）の `ICutsceneMarker` も発火します**（最初から再生したとき。ネット再生の受信側は開始位置から遡って 0.5 秒以内のマーカー。Seek・Skip で過ぎた分は無音）。`OnModelSpawned` の中から別のカットシーンを `Play` してよく、`OnModelReturning` の中で同じハンドルを `Despawn` しても再帰しません。**依存グラフに出したいなら `AssetId` 系で参照**してください（`FacialCorrectionData` のような `ScriptableObject` への直接参照は Addressables の依存としては運ばれ再生は問題ありませんが、D-Drive の使用箇所・未使用判定・安全な削除には出ません）。

## 3. 更新ウィンドウ（P-15）と、その確認結果への対応

- **T-Drive の `package.json` に `ddriveUpdate.compatibleWith: { "com.ddrive.core": "1.4.0" }` を書けば**、D-Drive の更新ウィンドウと `Validation > Run All` が組み合わせを確認します（D-Drive が 1.4.0 より古いときに Warning「〜は D-Drive v1.4.0 以降に対応」。D-Drive が導入されていなければ何も言いません）。他の T-Drive パッケージへの必須の依存は `requires: { "com.tdrive.toon": "0.5.0" }`。値は最低版 `X.Y.Z` の文字列だけです（上限・範囲指定は書けません。読めない値は Warning `DD-PKGDEP-BAD-DECLARATION`）。
- **配布形式**: git タグ `vX.Y.Z` が `package.json` の `version` と一致する / パッケージ直下（`?path=` の場所）に `CHANGELOG.md`（Keep a Changelog、各版に `### 互換性` 節）。CHANGELOG が無くても更新チェックと版上げは使えます。同じリポジトリの複数パッケージ（toon と facial）は同じタグに揃えてください（版上げの確認ダイアログで案内します）。
- 持ち込み先は更新ウィンドウの「URL を入力して追加」に T-Drive の git URL（`https://github.com/<owner>/<repo>.git?path=unity/com.tdrive.toon`）を入れて管理対象に登録します。D-Drive 側のコードに T-Drive の URL・パッケージ ID は書かれていません。
- **確認結果（BUG-1・Q-1〜Q-4、2026-10-05〜06）への対応**: (BUG-1) 日本語を含む `package.json` が文字化けして JSON が壊れ、事前確認が警告を見逃していた → git の出力を UTF-8 で読むようにし、「JSON として読めない / `ddriveUpdate` の形が読めない」を「宣言なし」と区別して「事前確認できませんでした」にしました。(Q-1) 同じ URL の再追加のメッセージをウィンドウに出します（既に管理対象なら「<ID> は既に管理対象に登録されています(manifest は変わりません)。」、manifest にあって未登録なら「manifest に同じ URL の <ID> があります。管理対象に登録しました。」）。(Q-2) 警告の主表示は宣言した側の行（「⚠ 依存に注意（…）」）、相手側の行は「ℹ <宣言した側> が vX.Y.Z 以降を要求しています（現在 vA.B.C）」。(Q-3) 依存の警告をアセットに紐付けず「(project)」と表示します。(Q-4) 「カメラを読むだけ」の型を実行順の検査から外す拡張点（上の 2 の 4）。

## 4. 保留・後回し

- **FC-8（BlendShape カーブ）**: 表情アニメの運用が決まったら再相談。
- **FC-9（デバッグ / 調整）**: 7-3 / 7-4 着手時に再相談。
- **FC-13（インスタンスごとのマテリアル値）**: T-Drive が方式（StructuredBuffer / MPB）を決めてから。
- 後回し: FC-16（名前付きスロットセット）・FC-17（ModelData の外部データへの汎用参照欄）・FC-18（プロジェクト設定の検証の拡張点）。

---

## 6. リリース後のチケット候補

docs/11 に既にあるものは参照だけ。

| 候補 | 内容 | 出典 |
|---|---|---|
| M-5 | ゲーム向けの時間源の公開 API（`GameLoop` に登録しなくてもヒットストップ・ポーズ込みの `dt` を読める形。追加のみ・MINOR。ユーザー判断が要る。`Time` 規則のメッセージもこのとき差し替え） | [11](11_tasks.md) M-4 節の M-5（案） |
| **GC-R-01**（BGM のループ位置の検査） | `BgmDataValidator` が、`LoopStartSec` がクリップ長以上で End > Start の場合・`LoopStartSec` < 0 で End = 0 の場合に黙る（実行時は 1 サンプルのループに潰れる）。v1.0.0 からの穴で今回悪化していない。**v1.4.1 で新しい Code の Warning（例 `DD-BGM-LOOP-OUT-OF-CLIP`）を足す**（まとめ役の決定。既存の Error の条件・重さは変えない） | [61] GC-R-01 |
| GC-R-07（ゴールデンの抜け） | 尺 0 の Warning 2 件（`DD-SHAKE-ENVELOPE-ZERO-DURATION` / `DD-HAPTICS-ZERO-DURATION`）の重さが `validator-severity.txt` に固定されていない。尺 0 の Data を組んで当てる個別の収集を足し、ゴールデンに 2 行追加（追加のみ）。v1.4.x | [61] GC-R-07 |
| Canvas Editor のツールバーの重なり | 幅 500px で最上段の「Prefab を開く(要素の移動)」ボタンが「対象」欄と重なる(563px では出ない) | [43](43_manual_verification_2026-09-17.md) 16-40 |
| 確認ダイアログのキャンセル文言 | 整理 / RootPath 変更の確認のキャンセルが「キャンセル(登録しない)」(登録ダイアログと共通)。「キャンセル(何も変更しない)」が合う | [43](43_manual_verification_2026-09-17.md) 16-29 |
| UI の Tick の再入 | `UiTweenManager.Tick` / `UiManager.Tick` の添字走査が、`WaitAsync` の続きが走査中に走ると崩れる（v1.3.1 から既存。`PresentationManager` は修正済み） | [56] FY-R-03・[57] |
| `GameLoop` の走査中の `Unregister` | `GameLoop.Tick` / `BroadcastPause` / `StopAll` / `NotifySceneUnload` は添字で走査するため、走査中に自分や前の Manager を外すと直後の Manager 1 つがそのフレーム飛ばされる（案内では「Tick の中で外さない」と書いた。D-Drive 自身の Manager は影響を受けない。直すなら `PresentationManager` / `CutsceneManager` と同じ写しの走査 = Foundation の挙動変更なので MINOR 以降） | [59] GB-R-04 |
| ~~BgmData のループ位置~~ | 修正ラウンド 7 で対応済み（`LoopEndSec = 0` は末尾まで。取り込み直後も Error 0） | [59] GB-R-02 |
| EditMode 初回の Undo 系 6 件 | 初回だけ一時的に失敗する（`AudioEditorWindow.DrawListenerPad` の例外。再現せず） | 2026-10-06 のまとめ役の確認（docs/11 には未記載。起票が要る） |
| `CANVAS_Can_Vas` の編集用配置 | 確認用データの編集用配置（`NavigationNodeLayout` 等）の残り | [11](11_tasks.md) 確認用データの整理の記録（#8） |
| Timeline ウィンドウを開く API | スクリプトから `OpenTimelineWindow` を呼んだ直後、ウィンドウが別のアセットを表示していると `TimelineEditor.inspectedDirector` が null のままで追従しない場合がある(人の操作では再現せず。スクリプト経由のみ) | [52](52_manual_verification_fc.md) §24 の確認の気づき |
| FC-16・FC-17・FC-18 | 名前付きスロットセット / 外部データへの汎用参照欄 / プロジェクト設定の検証の拡張点 | [51] §4.17〜4.19 |
| FC-8・FC-9・FC-13（保留） | 上の 5 の 4 | [51] §4.9・4.10・4.14 |
| レビューで見送った項目 | 下の一覧 | docs/53〜58 |

**レビューで見送った項目（v1.4.x / 次の MINOR で可とされたもの）**:

- [53]: FC-R-10（`FadeTo` の一時 Material にフェード中だけ `from` のパス無効化が残る。Unity が無効化済みパスの一覧を返さない）・FC-R-11（一時 Material の生成と Editor の `Refresh` 頻度）・FC-R-12（使用箇所ウィンドウの行ごとの全 CutsceneData 走査）・FC-R-13（サブトラックの走査は推定のまま）・FC-R-16(a)（`TryGetView` 中の自分の `Unregister` で次を飛ばす）・FC-R-17（外部ハンドラの `Target` と `DataType` の整合の検査）・FC-R-19（スナップショットの粒度。型名の完全名・`in` / `ref`）・FC-R-20（`*ImportProfile` の型・enum をスナップショットの対象にするか）・FC-R-21 の一部・FC-R-23（Edit Mode と Play で、参照元のトラックが無いときの扱い）
- [54]: PC-R-12（「選択に追従」の範囲）・PC-R-13 後半（同じリポジトリの 2 パッケージのタグ揃え）・PC-R-14〜17 の一部（導入の確認ダイアログ・前の参照に戻す・`PackageDependencyValidator` の static・テストの抜け）・PC-R-20（プリセットギャラリーとプレハブモードの Prefab の食い違い）
- [55]: FX-R-13 の `CreateFromSelection` の流れのテスト
- [57]: FZ-R-09 の残り（それらしい形の Warning・束ね・設定の未使用 Info）・FZ-R-11（予測再生キーの上限が「全部捨てる」）
- [58]: GA-R-01〜12 は修正ラウンド 6 で対応済み。残るのは GA-R-04（Q-1 の文面「この文面でよいか」のユーザー確認。動作は変えていない）だけ
- [59]: GB-R-03（`ValueDefColor` の Alpha。D-Drive の Data に使用箇所が無い。持ち込み先が使うときだけ）・GB-R-05 の細部（`TimeMode=Rate なのに Loop=Once` の Warning を Constant で出さない）・GB-R-06 の `SpecDiffValidator` の Data ごとの指摘のテスト・GB-R-08 の E-9b の順序依存
- 人による確認に残るもの: [43] §7・§10（Timeline。FBX の到着後）、[43] §15 の 15-30（Q-4。T-Drive のブリッジ対応後）、[52] §22（T-Drive 導入後）
