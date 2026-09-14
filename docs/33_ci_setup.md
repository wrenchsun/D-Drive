# 33. CI セットアップ（6-1: セルフホストランナー + PR ゲート）

関連: [11_tasks.md](11_tasks.md) 6-1 / [12_review.md](12_review.md) / [09_editor_tools.md](09_editor_tools.md) §5 / ルートの [CLAUDE.md](../CLAUDE.md) / ワークフロー本体 `.github/workflows/ci.yml`

> **2026-09-15 ユーザー決定: CI の本稼働は P7 の最後に回す**（MS2026 側に CI が実装済みのため、D-Drive 側ではランナーを運用しない）。
> 現在の `ci.yml` は PR/push では起動せず、手動実行（Actions タブの Run workflow）のみ。以下の手順（ランナー登録・ブランチ保護）は P7 末に行う。
> それまでの検査は §7 のローカル実行スクリプト `Tools/CI/run-ci.cmd` と Unity の Test Runner で行う。本稼働時は `ci.yml` の `on:` に `pull_request` / `push`（branches: [main]）を戻す。

## 0. 何ができるようになったか

`.github/workflows/ci.yml` が PR（main 向け）と main への push で以下を自動実行する。

1. **Validation**（`DDrive.Editor.CI.ValidateAll`）— 全 `IValidator` + `ForbiddenApiScanner`。Error があれば fail
2. **ID 差分検出**（`DDrive.Editor.CI.RegenerateIds`）— Asset ID を再生成して `git diff`。コミットし忘れがあれば fail
3. **EditMode テスト**
4. **PlayMode テスト**
5. 結果を PR の Summary（Actions の実行結果ページ、Checks タブから見える）に表示 + 失敗テスト名を列挙。XML 一式は Artifact として保存（14 日）

**現時点ではこのワークフローはまだ動かない**。GitHub Actions の実行場所として「この PC に常駐するセルフホストランナー」を指定しているが、そのランナー自体をまだ登録していないため。ランナー登録とブランチ保護の設定はユーザー作業（本書の §1・§2）。登録が終わるまで、PR の Checks は「Waiting for a runner…」のまま進まない（マージ自体は妨げない。ブランチ保護で必須チェックに設定するまでは無視してマージできる）。

なぜ 1 つの `jobs:` にまとめたか: このランナーは PC 1 台しかなく、Unity は同一プロジェクトを多重起動できない。GitHub Actions の「複数 job」に分けても実質は直列にしかならず、job をまたぐたびにチェックアウトのやり直しやアーティファクトの受け渡しが増えるだけなので、1 job・複数 step の構成にしている。

---

## 1. セルフホストランナーの登録（ユーザー作業）

このリポジトリの **Settings → Actions → Runners → New self-hosted runner** から、OS に `Windows` を選んで案内される手順（トークン付きのダウンロード・設定コマンド）をこの PC 上の任意のターミナルで実行する。本書ではトークンや URL は書かない（画面に一度しか出ない・失効するため、その場でコピーして使う）。

進める上での注意点:

- **ラベルを `windows` と `unity-6000.3.13f1` の 2 つ追加する**（デフォルトの `self-hosted` に加えて）。`config.cmd` の対話プロンプト、または `--labels windows,unity-6000.3.13f1` オプションで指定できる。ワークフロー側は `runs-on: [self-hosted, windows, unity-6000.3.13f1]` でこの 3 つ全部を要求しているので、ラベルが 1 つでも欠けるとジョブが永遠に「待ち」になる
- **作業フォルダ（work folder）は Unity Editor で開いている作業ディレクトリとは別にする**。`config.cmd` が作る既定の `_work` サブフォルダのままで良い。同じチェックアウトを人間が Unity で開いたまま CI が動かすと、Unity の多重起動エラーで CI が確実に失敗する
- **サービス化して常駐させる**: `config.cmd` 実行後に案内される `run.cmd` を都度手動で叩く運用は PC 再起動で切れるため、`./svc.cmd install` → `./svc.cmd start`（Windows サービスとして常駐）にする。タスクスケジューラでの自動起動でも良いが、サービス化の方がランナー側の標準機能で楽
- ランナーは既定で **Administrator 権限は不要**（サービスはローカルシステムまたは専用ユーザーで動かせる）。Unity のバッチ実行自体も通常ユーザー権限で足りる
- ランナーのプロセスがログオン中のユーザーの環境変数（`UNITY_EXE` を設定する場合）を見られるように、サービスの実行ユーザーと `UNITY_EXE` を設定したユーザーを揃える（§4 参照）

### 動作確認

登録・起動後、GitHub の **Settings → Actions → Runners** にこのランナーが `Idle`（緑）で表示されること。適当な PR を出す（または既存の PR に空コミットを push する）と、Checks に `D-Drive CI / ci` が現れ、`Waiting` → `Queued` → `In progress` と進めば配線は成功。

---

## 2. ブランチ保護（必須ステータスチェック）の設定（ユーザー作業）

**Settings → Branches → Branch protection rules → `main` に対するルールを追加/編集**:

- 「Require status checks to pass before merging」を有効化
- 一覧から `D-Drive CI / ci`（ワークフロー名 `D-Drive CI` の job 名 `ci`）を選択して必須チェックに追加
  - **一覧に出てくるのは、そのチェック名で最低 1 回 CI が走った後**。まずランナーを登録して PR を 1 回通し、チェック名が出てから保護ルールに追加する
- 「Require branches to be up to date before merging」は任意（1 人開発 + 単一ランナーなら必須にしなくても実害は小さいが、有効にしておくとマージ直前の再検証が保証される)
- 管理者（自分自身）にもルールを適用するか（「Do not allow bypassing the above settings」）は運用次第。1 人開発で緊急時に自分だけ通したい場面があるなら外しておいてよい

設定後は、上記チェックが green にならない PR は「Merge」ボタンがグレーアウトされる。

---

## 3. public/private 別の注意（セキュリティ）

現在このリポジトリは **private**。private の間は、リポジトリへの Collaborator/Team しか PR を出せない（フォークからの野良 PR がそもそも来ない）ため、セルフホストランナー特有の「知らない第三者の PR で自分の PC 上で任意コードが実行される」リスクは低い。

ただし将来 **public に変更する場合は再確認すること**:

- GitHub は「public リポジトリでセルフホストランナーを使うと、fork からの `pull_request` で誰でも任意コードをそのランナー（= このユーザーの PC）上で実行できる」ことを明確に警告している
- `.github/workflows/ci.yml` の job には
  ```yaml
  if: >-
    github.event_name != 'pull_request' ||
    github.event.pull_request.head.repo.full_name == github.repository
  ```
  というガードを入れており、**fork からの PR ではこのジョブを実行しない**（このリポジトリ自身のブランチからの PR と、main への push だけ実行する）。このガードは public / private のどちらでも安全側なので外さないこと
- それでも public 化するなら、加えて以下を検討する: Settings → Actions → General で「Require approval for all outside collaborators」相当のワークフロー承認設定を有効にする（Fork からの PR は毎回人が Approve するまで動かない）。あるいはセルフホストランナーを使うワークフロー自体を public リポジトリでは無効化し、GitHub-hosted runner 版の簡易チェック（Node のテストなど、Unity を使わないもの）だけ public 用に用意する
- 上記ガードは `pull_request` イベントの `head.repo.full_name` を見ている。GitHub 既知の落とし穴として `pull_request_target` は使っていない（意図的に使わない。secrets を伴わない前提でも `pull_request` のままにしている）

---

## 4. Unity のパス・ライセンス

- 既定の Unity パスはワークフロー内にハードコードしてある: `C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe`（CLAUDE.md 記載のバージョンと一致させること。Unity を上げたら両方同時に更新する）
- 別バージョン/別パスを使う場合は、**ランナーの環境変数 `UNITY_EXE`** にフルパスを設定すればワークフローが自動でそれを使う（`Resolve Unity executable path` ステップが `$env:UNITY_EXE` を優先し、未設定なら既定値にフォールバックする）。ランナーをサービス化している場合、環境変数はサービスのプロセスから見える場所（システム環境変数、またはサービスを動かすユーザーのユーザー環境変数）に設定してからサービスを再起動する
- **ライセンス**: このワークフローは Unity Hub へのログイン状態を前提にしている（別途 `-serial`/`-username`/`-password` でのアクティベートは行わない）。この PC の Unity Hub に既にログイン済みでライセンスがアクティベートされている前提。ランナーをサービスとして動かす場合、**サービスの実行ユーザーが、Unity にログインしているユーザーと同じであること**（別ユーザーのサービスで動かすとライセンスが見えず batchmode がライセンスエラーで即終了する)
- ライセンスエラーが出た場合の対処: 一度そのユーザーで Unity Hub / Unity Editor を通常起動してログイン状態を確認 → 必要なら `Unity.exe -batchmode -quit -returnlicense` でいったん返却してから Unity Hub 経由で再アクティベート → もう一度 CI を走らせる
- Personal ライセンスはシート数の制約があるため、同じライセンスで別 PC・別ユーザーが同時にログインしないよう注意

---

## 5. CI 実行中の PC 負荷

- Unity をバッチモードで最大 4 回（Validation / ID 再生成 / EditMode / PlayMode）連続起動する。プロジェクトの規模次第だが、初回（`Library/` キャッシュなし）は特に時間がかかる（インポートからやり直しになるため）
- ランナーの作業フォルダに `Library/` を残す運用にしているため、2 回目以降は大幅に短縮される想定（`actions/cache` も併用しているが、Windows self-hosted では作業フォルダそのものが再利用されるため cache の恩恵は薄いことがある。両方仕込んであるので、どちらが効いても速くなる）
- CI 実行中はこの PC の Unity バッチプロセスが CPU・メモリを消費する。**この PC で人が Unity Editor を同時に開いて作業する運用とは相性が悪い**（同じプロジェクトを開くとロック競合で確実に失敗し、別プロジェクト(MS2026 等)を開いているだけなら動くはずだが重くなる）。CI の実行タイミングを見ながら使うか、専用の別 PC/仮想マシンに切り替えることを将来検討してもよい
- `timeout-minutes: 45` を設定してあるので、ハングした場合もそこで強制終了される

---

## 6. 失敗時の見方

1. GitHub の PR ページ → Checks タブ → `D-Drive CI / ci` を開く
2. 各ステップ名（① Validation → ② ID 差分 → ③ EditMode → ④ PlayMode → プレースホルダー → ⑤ Summarize）のうち、どこで ✗ になったかを確認
3. 失敗したステップのログ末尾 300 行がそのままステップの出力に出る（Unity の `-logFile` の内容）。詳細を見たい場合は、そのジョブの実行結果ページ下部にある `ddrive-ci-results-<run id>` という Artifact をダウンロードすると `TestResults/` 配下の全 XML・ログが入っている
4. **Summarize results** ステップが成功していれば、実行結果ページの **Summary** タブに Validation/EditMode/PlayMode それぞれの件数と、失敗したテスト名の一覧が Markdown で出る（JUnit/NUnit の XML を都度開かなくて良い）
5. ② ID 差分（`Regenerate Asset IDs & check diff`）で落ちた場合は、ローカルで Unity を開いて **`Tools > D-Drive > Generate > Regenerate Asset IDs`** を実行し、変更された `Assets/Generated/AssetIds.g.cs`（や Id が新規割当された `.asset`）をコミットし直せば直る

---

## 7. ローカルでの手動実行

Unity を開いていない状態で、リポジトリ直下から:

```
Tools\CI\run-ci.cmd
```

別バージョン/別パスの Unity を使う場合は引数で渡す:

```
Tools\CI\run-ci.cmd "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"
```

CI ワークフローと同じ 4 ステップ（Validation → ID 再生成+diff → EditMode → PlayMode）を順番に実行し、最後に `Tools/CI/Summarize-Results.ps1`（`pwsh` があれば）で結果を要約表示する。結果ファイルは `TestResults/`（gitignore 済み）に出る。

**注意**:

- **このプロジェクトを Unity Editor で開いている間は使えない**（同一プロジェクトの多重起動不可のため、途中のステップで Unity がエラー終了する）。使う前に Unity を閉じること
- `.ps1` を主経路にしなかった理由: この PC の `powershell.exe`（Windows PowerShell 5.1）は既定の実行ポリシーで `.ps1` の直接実行がブロックされたり、BOM 無し UTF-8 のコメント（日本語）が化けたりすることがある。`run-ci.cmd` は内部で `pwsh -ExecutionPolicy Bypass -File ...` の形で明示的に呼ぶことでこれを避けている。`pwsh`（PowerShell 7+）自体が入っていない場合、テスト実行自体は`.cmd` だけで完走するが、結果サマリの整形だけスキップされる（`TestResults/` の XML を直接見る）
- git の作業ツリーが汚れている状態（未コミットの変更がある状態）で実行すると、②の `git diff --exit-code` がその既存の差分も検出して fail する（CI 由来の差分と区別できない）。実行前に `git status` で作業ツリーがきれいであることを確認しておくと結果が分かりやすい

---

## 8. 6-2（性能テスト）の現状（2026-09-15 実装 / 方針変更）

**2026-09-15 に方針変更**: GitHub Actions のセルフホストランナー導入（本書 §1〜§2）は**P7 の最後に回す**ことになった（MS2026 側に既に CI があるため、D-Drive 側でランナーを別途用意しない）。そのため `.github/workflows/ci.yml` の `Performance tests (6-2, placeholder)` ステップは**このチケットでは実処理化していない**（placeholder のまま）。6-1/6-7 を含め、GitHub Actions での自動実行は P7 末の CI 導入まで保留（トリガーを `workflow_dispatch`（手動実行）のみに変更するのは親セッションの作業）。「PR ごとに実行」というユーザー決定自体は変わっていないが、**発効するのは CI 導入時**になる。

**この間の実行経路**: 6-2 で追加した性能テスト（新規 asmdef `DDrive.Tests.Performance`、`Assets/DDrive/Tests/Performance/`、カテゴリ `Performance`）は、Unity Editor の Test Runner（Window > General > Test Runner、PlayMode タブ、`Performance` カテゴリで絞り込み）と、ローカル一括実行の `Tools/CI/run-ci.cmd`（本書 §7）で回す。`run-ci.cmd` は 2026-09-15 に **[5/5] Performance テスト**ステップを追加し、`-testPlatform PlayMode -testCategory "Performance"` で絞り込んで実行、結果は `TestResults/performance-results.xml` に出力、`Tools/CI/Summarize-Results.ps1` が Validation/EditMode/PlayMode と同じ表に追加する（`-PerformanceResultsPath` パラメータ、省略可）。

**テストの内容・既知課題**: Pool の Rent/Return・各 Manager の Tick（+Presentation の Signal）・GameLoopDriver の 1 フレームは 0 alloc を hard assert する。Spawn/Play 系（1 アクションにつき 1 回呼ばれる経路）は Instance クラスを 1 個 new する既存設計のため厳密な 0 alloc ではなく、Performance レポートへの記録のみ（assert しない）。詳細は [12_review.md](12_review.md) §3 と [11_tasks.md](11_tasks.md) 6-2 の実装メモを参照。

**将来、CI 導入時に実処理化する人向け**: `.github/workflows/ci.yml` の `Performance tests (6-2, placeholder)` ステップに、`run-ci.cmd` の [5/5] と同じ `-runTests -testPlatform PlayMode -testCategory "Performance"` 呼び出しを追加し、結果 XML を `Summarize results` ステップの `Summarize-Results.ps1` 呼び出しに `-PerformanceResultsPath` として渡す（既にパラメータ対応済み）。失敗時は非ゼロ終了で他のステップと同じ扱いにする。
