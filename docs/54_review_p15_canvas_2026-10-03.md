# 54. 2026-10-03 自前レビュー結果（P-15 更新ウィンドウの追加パッケージ対応 / U-28 Canvas の埋め込み）

> **対象**: 2026-10-03 に main へ入った 2 PR。どちらも実装はサブエージェント、まとめ役は本体の差分をほぼ読まずにマージした。
>
> | マージ | PR | 内容 |
> |---|---|---|
> | `cd312f3` | #98 | P-15 更新ウィンドウを D-Drive 以外の git URL パッケージにも対応 + `package.json` の `ddriveUpdate` による依存の確認（`Editor/Update/*`・`Editor/Settings/{ManagedPackageEntry,ManagedPackageList,DDriveProjectSettings}`・`Editor/Validation/PackageDependencyValidator`・テスト・docs/42 §4.2.1） |
> | `cb5ba07` | #100 | U-28 Canvas の埋め込み（入れ子）対応（`CanvasData.EmbeddedCanvases`・`EmbeddedCanvasPaths`・`UiManager.SetupEmbeddedCanvases`・`CanvasDataValidator`・`Editor/Canvas/{CanvasEmbeddedEditing,CanvasEmbeddedValidator,CanvasEditorWindow,CanvasElementFxCollector}`・`UiPresetGalleryWindow`・テスト・docs/07・09・39・43）。SpecWeb のマニュアル生成物の再生成と docs/53 の取り込みも混ざっているが、そちらは対象外 |
>
> **方法**: 専用 worktree を `cb5ba07`（detached）に合わせ、`git diff cd312f3^1 cd312f3` / `git diff cb5ba07^1 cb5ba07` と変更後のファイル全体（`UpdateWindow` / `GitSparsePackageJsonFetcher` / `GitCliTagLister` / `GitTagListParser` / `GitPackageUrl` / `UpdateCheckLogic` / `PackageDependencyChecker` / `PackageAddPlanner` / `PackageManifestOps` / `UpdatePreflight` / `InstalledPackages` / `UiManager` / `CanvasEmbeddedEditing` / `CanvasEmbeddedValidator` / `CanvasEditorWindow` の該当部 / `EditorAnchorRegistry` 等）を**読むだけ**で確認した。実装者の報告（docs/42 §4.2.1・docs/07 追記・docs/39・CHANGELOG・docs/11 の実装要約）は信用せず、コードと突き合わせた。**Unity は起動しておらず、コンパイル・テスト・実 `git` は一切実行していない。** 指摘はコードを読んで確認した事実か、Unity / git / .NET の挙動についての推定で、推定のものは「確度」欄に**推定**と書いた。
>
> 前提として読んだもの: `CLAUDE.md`（§0・§0-10）、[docs/12](12_review.md) §3、[docs/42](42_distribution.md) §4・§4.2.1・§5、[docs/53](53_review_fc_2026-10-03.md)（書式と重大度の基準）、[docs/11](11_tasks.md) P-15 行、`docs/50_consumer_guide/update.html`、[docs/43](43_manual_verification_2026-09-17.md) §14〜§16、[docs/07](07_canvas_prefab.md) の 2026-10-03 追記、[docs/09](09_editor_tools.md) の Canvas Editor 節、[docs/39](39_usability_fixes_2026-09-17.md) の U-28 追記、DesignerManual / ProgrammerManual の差分、T-Drive 側の前提のコピー（`tasks.md`・`08_unity_port_plan.md`・`15_facial_controller_design.md` §5）。
>
> **git の `--` 区切り（オプション注入対策）は別の担当が修正中と聞いているので、本書では扱わない**（PC-R-21 で関連する残りだけ触れる）。

## 総評

- **リリースを止める実バグ（P1）は見つからなかった**。互換面は両 PR とも追加のみ（`serialized-layout.txt` / `public-api-DDrive.Runtime.txt` の差分は追加行だけ、`ProjectSettings/DDriveProjectSettings.asset` は `_managedPackages: []` が 1 行増えるだけ）。`EmbeddedCanvases` が空 / null のとき `UiManager` は従来と同じ経路を通り、割り当ても増えない。D-Drive だけのプロジェクトでの P-8 / P-14 の手順（更新チェック → manifest の版上げ → 4 段の適用）は機能としては壊れていない。
- **P-15 の弱点は「外部プロセスの扱い」と「タグ名の扱い」**。上げ先の `package.json` の取得が作業ツリーを実際にチェックアウトする方式で、要らない危険（チェックアウト時のフィルター・シンボリックリンク・子プロセスの取り残し）を抱えている（PC-R-01）。プレリリースのタグ（`v1.5.0-rc.1`）が `v1.5.0` に丸められ、**存在しないタグを manifest に書く**経路がある（PC-R-02、P-14 からの既存の穴を P-15 が外部パッケージへ広げた）。D-Drive の版上げにも事前確認の `git clone` が入り、**メインスレッドが最大 30 秒止まる**（PC-R-03、従来の手順の体感が変わる）。
- **`ddriveUpdate` 形式は骨格（`requires` / `compatibleWith` = 最低版の文字列、パッケージ ID がキー）としては妥当**で、旧い読み手が新しいキーを黙って無視する作りにもなっている。ただし**拡張の規則（未知のキー・値の型・プレリリース・範囲指定）が文書化されていない**ので、v1.4.0 で形式が固定される前に決めておくべき（PC-R-07）。
- **U-28 の実行時は概ね正しい**（再帰・循環・深さ・兄弟の前方一致・外側優先・Close の Disappear 待ち・購読解除・プール再 Open）。穴は**埋め込みルートが重なる登録**（親が `OptionRoot` と `OptionRoot/Inner` の両方を登録する等）で、同じ要素に配線と演出が**二重に**付き、ボタン 1 回で `OpenCanvas` / `SendSignal` が 2 回走ること（PC-R-04）。Canvas Editor の「入れ子 Prefab から検出」がこの登録を提案しうる。
- **`SendSignal` の `ElementPath` が埋め込み時は親ルート基準になる**仕様は、受け手のコードに「単独で開いたときのパス」と「埋め込まれたときのパス」の両対応を迫る。公開後は変えられない意味なので、タグ前に補助の欄を足すかを決めるべき（PC-R-06）。
- Canvas Editor の「選択に追従」は、遅延確定（`isDelayed`）の入力欄の確定と対象の切り替えが前後すると、**入力した値が切り替え後の別の CanvasData に書かれる / 失われる**可能性がある（PC-R-05、推定）。
- テストは偽の fetcher / tag lister・偽の Loader で書かれていて、実ネットワーク・実 `git`・実 manifest には触れていない（例外: 設定の往復テストが実 `ProjectSettings/DDriveProjectSettings.asset` を書いて戻す。PC-R-17）。重なった埋め込み・2 か所への埋め込み・プレリリースのタグのテストは無い。
- **公開面**: `EmbeddedCanvas`（シリアライズ済み）は残す。`EmbeddedCanvasPaths` は Runtime の公開 API に永久に残る汎用の文字列ユーティリティなので、タグ前に internal 化（または Editor へ移す）を推奨（PC-R-18）。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **0** | – |
| P2（直すべき不具合・設計上の穴） | **7** | PC-R-01〜07 |
| P3（整理・改善） | **14** | PC-R-08〜21 |

---

## P2 — 直すべき不具合・設計上の穴

### PC-R-01. 【P-15】上げ先の `package.json` を取るのに作業ツリーをチェックアウトしている（フィルター・シンボリックリンク・パス・子プロセスの取り残し）

- **場所**: `Packages/com.ddrive.core/Editor/Update/GitSparsePackageJsonFetcher.cs:38-51`（`clone --depth 1 --filter=blob:none --sparse` → `sparse-checkout set`）、`:53`（`Path.Combine(workDir, cleanPath, "package.json")`。`cleanPath` は `?path=` の値で、`..` や絶対パスを検査していない）、`:115-131`（タイムアウト時は `process.Kill()` のみ）、`:143-164`（`Directory.GetFiles(dir, "*", AllDirectories)` で全ファイルの属性を `Normal` にしてから `Directory.Delete(dir, true)`）
- **何が問題か**: 欲しいのは 1 ファイルの中身だけなのに、`--sparse`（cone モード）は**リポジトリ直下のファイル + `?path=` のフォルダ**を実際にチェックアウトする。チェックアウトには次の副作用が付く。(1) ユーザーの環境のチェックアウト時フィルター（`git-lfs` を入れていれば LFS の smudge = 大きなファイルのダウンロード。T-Drive は LFS を使うリポジトリ〔T-Drive tasks.md R-3「LFS を取らない」〕）が走り、30 秒のタイムアウトに当たりやすい。(2) macOS / Linux ではリポジトリ内のシンボリックリンクがそのまま作られ、後始末の `GetFiles(AllDirectories)` がリンク先のディレクトリへ降りて**作業フォルダ外のファイルの属性を変える**、または再帰削除がリンク先に及ぶ可能性がある（.NET / Mono の実装次第。**推定**）。(3) `?path=../..` や絶対パスは、git が「リポジトリの外」としてエラーにするはず（**推定**）なので実害は出にくいが、コード側では検証していない（git が受け入れた場合 `Path.Combine` が `Temp/DDriveUpdate` の外の `package.json` を読む）。(4) `Process.Kill()` は `git` 本体だけを止め、子の `git-remote-https` / `ssh` / `index-pack` は残る（Mono の `Kill` はプロセスツリーを止めない。**推定**）。残った子が一時フォルダを掴んでいると `TryDelete` が黙って失敗し、`Temp/DDriveUpdate/<guid>` が溜まる。
- **失敗の筋書き**: MS2026 の Windows 機で T-Drive（LFS あり）の行を選び「manifest を選んだ版に更新する」→ clone は済むが直下の LFS ファイルの smudge でダウンロードが始まり 30 秒でタイムアウト → 「事前確認できませんでした」（毎回）。または、Mac の開発者が悪意のある（あるいは単にシンボリックリンクを含む）リポジトリの URL を入れて版上げを試す → 後始末がリンク先を辿る。
- **直し方の案**: 作業ツリーを作らない方式にする。`git clone --depth 1 --filter=blob:none --no-checkout --no-tags --branch <tag> -- <url> <dir>` → `git -C <dir> show <tag>:<path>/package.json`（または `cat-file -p`。blob 1 個だけを遅延取得し、フィルター・シンボリックリンク・パスの解決が起きない）→ 標準出力を読む。`?path=` は `..`・先頭 `/`・ドライブ名を含んだら取得せず警告にする（`git show` の `<rev>:<path>` でもリポジトリ外は指せないが、入力の段階で弾く方が分かりやすい）。後始末は `.git` だけになるので属性ループも短くなる。タイムアウト時は `taskkill /T /F /PID`（Windows）相当でツリーごと止めるか、少なくとも「`Temp/DDriveUpdate` の古いフォルダを次回起動時に掃除する」。docs/42 §4.2.1 の手順の記述も合わせて直す。
- **確度**: チェックアウトしている・パスを検査していない・`Kill` だけ、はコード読みで確認済み。LFS の影響・シンボリックリンクの辿り方・子プロセスの残り方は**推定**
- → 対応（2026-10-03、`af4105a`）: **修正**。取得を `git clone --depth 1 --filter=blob:none --no-checkout --no-tags --branch <タグ> -- <url> <dir>` + `git show HEAD:<?path=>/package.json` に変更（作業ツリーを作らない。チェックアウト時のフィルター・LFS の smudge・シンボリックリンク・パスの解決が構造的に起きない。`HEAD` はその clone が取ったタグのコミットで、ユーザー入力の rev は使わない）。`?path=` は `GitArguments.TryNormalizePackagePath` で検査（`..`・`.`・空の区間・ドライブ名やコロン・UNC・`-` 始まり・制御文字は取得せず警告。UPM の `?path=/sub`〔先頭の `/` = リポジトリ直下基準〕は許す）。タイムアウト / キャンセルは `GitProcess.KillTree`（Windows は `taskkill /T /F`、他は `pkill -P` + `Kill`）でツリーごと止め、一時フォルダを消す。消せなかった分は `GitPackageJsonFetcher.CleanupStale`（10 分より古いもの）が更新ウィンドウを開いたときに掃除する。クラスは `GitSparsePackageJsonFetcher` → `GitPackageJsonFetcher` に改名（`git mv`。Editor の契約外）。**実 git での確認**（ローカルの一時リポジトリ `file://`、`uploadpack.allowFilter` 有効、Unity の `execute_code` から実クラスを呼んだ）: (a) タグ `v1.0.0` の `unity/com.x/package.json` が取れる (b) `?path=/unity/com.x/`（先頭・末尾の `/`）と `v1.1.0-rc.1` が取れる (c) 存在しない `?path=` は「その版に package.json が見つかりませんでした(fatal: path … does not exist in HEAD)」 (d) 存在しないタグは git のエラー行を警告にする（`fatal:` / `error:` で始まる行を優先して採用。最初は進捗行 `Cloning into …` を拾っていたので直した） (e) `?path=../x` は git を呼ばずに弾く (f) 到達できない URL（`https://10.255.255.1/…`）の取得を 2.5 秒後にキャンセル → 2.9 秒で「キャンセルしました」を返し、`Temp` 側の一時フォルダは 0 件、`git` / `git-remote-https` のプロセスは残らない。**推定だったものの確認結果**: LFS の smudge・シンボリックリンクの辿り方は、作業ツリーを作らなくなったので問題自体が無くなった（検証する対象が消えた）。子プロセスの残りは (f) で残らないことを確認（`taskkill /T`）。`../..` は git に渡す前に弾く。テスト: `GitArgumentsTests`（引数の形・`?path=` の検査 19 ケース・拒否の確認）。
- → 注記（修正ラウンド 3、2026-10-04、[55] FX-R-05）: 終了後の出力読み切りに 5 秒の上限を付け、Windows 以外は子孫プロセスまで `pgrep -P` で辿って止めるようにした。CHANGELOG の P-15 の項の「浅い sparse clone」は `--no-checkout` + `git show` の記述に直した。

### PC-R-02. 【P-15 / P-14 既存】プレリリースのタグが `vX.Y.Z` に丸められ、存在しないタグを manifest に書く

- **場所**: `Editor/Update/GitTagListParser.cs:79-83`（`SemVer.TryParse(body)` は `-` 以降を捨てて `System.Version` にする。元のタグ名は捨てる）、`Editor/Update/UpdateWindow.cs:668`（ドロップダウンの選択肢を `"v" + v` で作り直す）、`Editor/Update/PackageAddPlanner.cs:238`（「最新」も `"v" + tags[0]`）
- **何が問題か**: `refs/tags/v1.5.0-rc.1` は `Version(1,5,0)` になり、選択肢には `v1.5.0` と出る。`v1.5.0` が無ければ**存在しないタグ**を manifest の `#ref` に書く。`v1.5.0` と `v1.5.0-rc.1` が両方あれば同じ `v1.5.0` が 2 行出る。`v01.2.0` のような表記も `v1.2.0` に変わる。D-Drive の行は `Parse`（`v` 無しのタグも採る）なので、`1.5.0` というタグも `v1.5.0` として書かれる。P-14 から既存の穴だが、D-Drive は `rc` タグを切らない運用で表に出ていなかった。P-15 で**任意の外部パッケージ**が対象になり、URL 入力からの新規導入（`Client.Add("<url>#v1.5.0")`）にも同じ値が使われる。
- **失敗の筋書き**: T-Drive が `v0.6.0-rc.1` だけを先に push → 更新ウィンドウの「最新」が `v0.6.0`（MINOR）と表示 → 「manifest を選んだ版に更新する」→ manifest に `#v0.6.0` → UPM の解決が失敗（パッケージが解決できない旨のエラー。UPM がその後どの状態でプロジェクトを開くかは**推定**で、最悪はパッケージが外れて T-Drive に依存するコードがコンパイルエラー）。事前確認の `git clone --branch v0.6.0` も失敗するが「事前確認できなかった。更新後に確認します」で続行できてしまう。
- **直し方の案**: `GitTagListParser` が「元のタグ名 + 解析した版 + プレリリースか」を返すようにし（既存の `Parse` / `ParseVersionTags` の戻り値は `List<Version>` のまま残し、新しいメソッドを足す）、ドロップダウン・`PackageAddPlanner` は**元のタグ名**をそのまま `#ref` に使う。プレリリースは既定で「最新」の判定から外し、選択肢には「(プレリリース)」付きで出す。テスト: `v1.5.0-rc.1` だけ / `v1.5.0` と `-rc.1` の両方 / `1.5.0`（v 無し、D-Drive の行）/ `v01.2.0`。
- **確度**: 確認済み（コード読み。UPM 解決失敗後の状態のみ推定）
- → 対応（2026-10-03、`af4105a`）: **修正（確認済み）**。`GitTag`（元のタグ名 + 版 + プレリリース部）を新設し、`GitTagListParser.ParseTags`（元のタグ名を保つ。既存の `Parse` / `ParseVersionTags` の戻り値は変えない）・`UpdateCheckLogic.EvaluateTags`・`PackageAddPlanner` が元のタグ名を `#ref` / `Client.Add` に使う。**規則（[42] §4.2.1 に文書化）**: 既定では正式版 `vX.Y.Z` だけを「最新」の候補にする。プレリリースは「更新先の版」に「（プレリリース）」付きで出すが自動では勧めない（より新しいものは「プレリリース … もあります」と知らせる）。現在の参照（または package.json の版）がプレリリースのときだけ、プレリリースも最新の候補にする。比較は SemVer の優先順位（`1.5.0-rc.1 < 1.5.0`、プレリリース同士は区間ごと、数字は数値比較）。正式版が無くプレリリースだけのとき、URL 入力は「`#<タグ名>` を付けて入力してください」と案内して止める。事前確認（`UpdatePreflight`）のタグ版は `GitTag.TryParseRef` で `X.Y.Z` に直し、プレリリース部は比較で無視する。D-Drive 自身（P-14 の既存機能）の挙動が変わる点は CHANGELOG の互換性節に「挙動の変更（PATCH 相当）」として明記（存在しないタグを書く不具合の修正。D-Drive は `rc` タグを切らない運用なので従来の手順は変わらない）。テスト `PrereleaseTagTests`（`GitTag` の解析・比較、`ParseTags`、`EvaluateTags` 8 ケース、URL 入力の計画、事前確認、同じリポジトリの案内）。UPM が存在しないタグを書かれた後にどうなるか（推定）は、書かない形に直したので確認していない。
- → 注記（修正ラウンド 3、2026-10-04、[55] FX-R-07・FX-R-08）: 正式版が無くプレリリースだけのときは「更新先の版」を何も選択済みにしない。`vX.Y.Z`（3 区間）以外のタグ（`v1.5`・`v1.5.0.1`）は版として読まない。

### PC-R-03. 【P-15】版上げ・元に戻すの前に同期の `git clone` が入り、D-Drive だけの手順でもメインスレッドが最大 30 秒止まる

- **場所**: `Editor/Update/UpdateWindow.cs:688` / `:744`（`ConfirmWithPreflight` → `UpdatePreflight.Run` → `GitSparsePackageJsonFetcher.FetchPackageJson` を UI スレッドで同期実行）、`:529-540`（「一覧の最新版をまとめて確認」は行ごとに `git ls-remote` を順番に同期実行 = 行数 × 最大 30 秒）、`Editor/Update/GitCliTagLister.cs:27-35`（`GIT_TERMINAL_PROMPT` を設定していない）
- **何が問題か**: P-14 では「manifest を選んだ版に更新する」を押すと即座に確認ダイアログが出た。P-15 では D-Drive の行でも**先に clone が走り、終わるまで Editor が無反応**（進捗表示なし）。「前の参照に戻す」も同じ。private リポジトリで Windows の Git Credential Manager が GUI のログイン画面を出すと、Unity が固まったまま別ウィンドウの入力待ちになり、30 秒で `git` だけ殺されて GCM の画面が残る（**推定**）。`GitCliTagLister` は P-14 のまま `GIT_TERMINAL_PROMPT=0` が無く、P-15 で**ユーザーが入力した任意の URL**に対して呼ばれるようになった。AC (1)「D-Drive だけのプロジェクトで見た目・操作が従来とほぼ同じ」に対して体感が変わる。
- **失敗の筋書き**: 回線の遅い環境で D-Drive を上げようとする → 押してから 10〜30 秒反応が無い → ハングと思って Unity を強制終了する（manifest はまだ書いていないので壊れはしないが、操作として不親切）。
- **直し方の案**: (a) 最低限: 取得の前後を `EditorUtility.DisplayCancelableProgressBar` で囲み、キャンセルで取得をやめて「事前確認なしで続けるか」を聞く。`GitCliTagLister` にも `GIT_TERMINAL_PROMPT=0` を足す（可能なら `GCM_INTERACTIVE=never` も。認証は既存のキャッシュだけを使う）。(b) 望ましい: 取得を `Task.Run` + `EditorApplication.update` でのポーリングに移し、ウィンドウに「事前確認中…」を出す。「まとめて確認」も同様に非同期・並列（同時 3 本程度）にする。(c) docs/42 §4.2.1 と update.html に「版上げの前に最大 30 秒かかることがある」を書く。
- **確度**: 同期実行・`GIT_TERMINAL_PROMPT` 無しはコード読みで確認済み。GCM の挙動は**推定**
- → 対応（2026-10-03、`af4105a`）: **修正**。git を呼ぶ処理（「最新の版を確認」・「一覧の最新版をまとめて確認」〔行ごとに順番。進捗は「(i/n)」〕・URL 入力のタグ取得・版上げ / 元に戻すの事前確認の取得）を `Task.Run` + `EditorApplication.update` のポーリング（`StartBusy` / `PollBusy`）でバックグラウンド化し、ウィンドウ上部に「確認中…」+「キャンセル」を出した（1 度に 1 件。ウィンドウを閉じるとキャンセル = プロセスツリーを止める）。事前確認の取得をキャンセルすると「事前確認なしで続けるか」を聞く。バックグラウンドから Unity API は呼ばない（`Application.dataPath` は開始前に主スレッドで取って渡す）。**D-Drive の版上げ / 元に戻すは上げ先の `package.json` を取得しない**（D-Drive は `ddriveUpdate` を宣言しない規約。他パッケージの宣言との照合はタグの版だけで足りる）ので、D-Drive だけのプロジェクトの従来の手順に余計な待ちは入らない（AC (1)）。`GitCliTagLister` も `GitProcess` 経由（`GIT_TERMINAL_PROMPT=0` + `GCM_INTERACTIVE=never`、標準入力を閉じる）。[42] §4.2.1 と update.html に追記。**目視に回す範囲**: ウィンドウの進捗表示・キャンセルの見た目と挙動、Git Credential Manager の GUI がどうなるか（推定のまま。`GCM_INTERACTIVE=never` で出さない想定）→ [43] §15 の 15-16 / 15-17。
- → 注記（修正ラウンド 3、2026-10-04、[55] FX-R-06・FX-R-11）: ドメインリロード直前・Editor 終了・ウィンドウを閉じたときに実行中の git を止め、`OnDisable` で `_busyTask` を戻す。認証プロンプトを出さなくなったこと（D-Drive 自身の更新チェックを含む）を CHANGELOG の互換性節・consumer guide に記載した。

### PC-R-04. 【U-28】埋め込みルートが重なる登録で、同じ要素に配線・演出が二重に付く（ボタン 1 回で 2 回発火）

- **場所**: `Runtime/Canvas/UiManager.cs:1407-1465`（`SetupEmbeddedFrom`。優先判定 `AncestorHas*Row` は**祖先の CanvasData の行**だけを見て、兄弟の埋め込みが同じ要素を既に担当しているかは見ない）、`Runtime/Canvas/CanvasDataValidator.cs:94`（重複の検査は `RootPath` の**完全一致**だけ）、`Editor/Canvas/CanvasEmbeddedEditing.cs:108-138`（`DetectCandidates` は親 Prefab 内の**すべての**入れ子 Prefab インスタンスのルートを候補にし、既に登録済みの埋め込みルートの配下かどうかを見ない）
- **何が問題か**: 親 Hud が `OptionRoot`（子 Option）を埋め込み、Option 自身が `Inner`（孫 Volume）を埋め込んでいるとき、Hud の Canvas Editor の「入れ子 Prefab から検出（未登録）」には `OptionRoot/Inner`（Volume）も候補として出る（入れ子の入れ子の Prefab インスタンスも `IsAnyPrefabInstanceRoot` が真になるため。**推定**: Unity の入れ子 Prefab の判定）。これを「埋め込みとして登録」すると、Volume の行は (1) Hud → `OptionRoot/Inner` の埋め込みとして 1 回、(2) Hud → Option → `Inner` の入れ子として 1 回、計 2 回適用される。どちらの経路でも祖先（Hud / Option）に同じ要素の行は無いので優先判定は効かない。手で `OptionRoot` と `OptionRoot/Panel`（別の Canvas）を両方登録した場合も同じ（子 Option の `Panel/BtnX` の行と、`OptionRoot/Panel` の埋め込みの `BtnX` の行が同じボタンを指す）。
- **失敗の筋書き**: 上の登録をした Hud を Open → Volume の `BtnApply`（`SendSignal "volume/apply"`）を 1 回押すと購読側に 2 回届く。`OpenCanvas` の配線なら同じ Canvas が 2 枚開く。ElementFx は同じ要素に Appear が 2 本走り `PendingAppearCount` も 2 倍（見た目は後勝ち、Idle は 2 本重なる）。Validation は何も言わない。
- **直し方の案**: (a) 実行時: `SetupEmbeddedFrom` で、既に処理した埋め込みルート（Open した Canvas のルート基準 = `next[0].Prefix`）の配下に入る埋め込みは警告 1 回 + スキップ（外側 / 先に処理した方が勝つ）。判定は `TryToChildPath` の文字列比較で割り当て無しにできる。(b) Validator: 同じ CanvasData の `EmbeddedCanvases` 同士で一方が他方の配下（`TryToChildPath` が真）なら新規コード `DD-CANVAS-EMBED-NESTED-ROOT`（Warning）。子の入れ子と重なる場合（親の `A/B` と、`A` の子が持つ `B`）は Editor 側の `CanvasEmbeddedValidator` で。(c) `DetectCandidates` は登録済みルートの配下を候補から外す（「子の Canvas Editor で登録してください」と表示）。(d) PlayMode テストを 1 件（重なった登録で配線が 1 回だけ）。
- **確度**: 二重適用はコード読みで確認済み。`DetectCandidates` が入れ子の入れ子を拾うかは**推定**（Unity の `IsAnyPrefabInstanceRoot` の挙動）
- → 対応（2026-10-03、`a48da84`）: **修正（確認済み）**。実行時を「1 要素 1 回」の担当表（`UiManager.EmbedClaims`）方式に変更。優先は Open した CanvasData 自身 > 浅い入れ子の子 > 深い入れ子の子で、同じ親が重なる登録（`OptionRoot` と `OptionRoot/Inner`）を持つときは**内側（`RootPath` が深い方）の登録が先に担当する**（浅い段から順に辿り、段の中は RootPath の深い順）。子の CanvasData 自身が `Inner` を埋め込みとして持つ正しい形（入れ子の入れ子）では、外側ほど強い（孫 < 子 < 親）まま。(b) Validator: Runtime の `CanvasDataValidator` に `DD-CANVAS-EMBED-NESTED-ROOT`（Warning。一方の RootPath が他方の配下）、Editor の `CanvasEmbeddedValidator` に子が自分で埋め込む場所が別の登録と重なる場合の同コード。(c) `DetectCandidates` は、他の候補・登録済みの埋め込みルートの配下にある入れ子 Prefab（入れ子の入れ子）を提案しない。(d) PlayMode テスト `OverlappingEmbedRegistrations_ApplyEachElementOnlyOnce`（重なる登録でシグナルが 1 回）・`…ApplyEachElementFxOnlyOnce`・`SameChildEmbeddedAtTwoPlaces_BothApplyIndependently`（同じ子を 2 か所に埋め込む = 循環ではなく両方動く）、EditMode テスト（検出・Validator）。**推定だった `IsAnyPrefabInstanceRoot` の確認**: 親 Prefab の中の入れ子の入れ子（Hud → OptionRoot → Inner）の `Inner` で **True**（`GetCorrespondingObjectFromOriginalSource` は元の Volume Prefab）。つまりレビューのとおり、除外しないと「入れ子の入れ子」が候補に出て重なる登録を提案していた。
- → 注記（修正ラウンド 3、2026-10-04、[55] FX-R-10）: 優先は 2 つの別の規則（(A) Open した CanvasData 自身の行が子に勝つ = 外側が強い / (B) 重なる登録は設定の誤りで、実行時はより内側の登録が配下を担当する）と整理し、[07] に表で書いた。実行時の挙動は変えていない。

### PC-R-05. 【U-28】「選択に追従」で対象が切り替わるとき、遅延確定の入力欄の値が別の CanvasData に書かれる / 失われる

- **場所**: `Editor/Canvas/CanvasEditorWindow.cs:1036-1072`（`FollowSceneSelection` → `SetTargetIn` → UI の作り直し）、`:1074-1083`（`IsEditingText` は `focusedWindow != this` なら false）、`:958-970`（`RootPath` 欄は `isDelayed = true`、確定時のコールバックは**その時点の `_target`** に `EmbeddedCanvases[index]` を書く）。ElementFx 行の各欄（`UpdateFx(index, …)` 系）も同じく `_target` と `index` を確定時に引く既存の形
- **何が問題か**: 遅延確定の欄は、Enter か「フォーカスが外れたとき」に値を確定する。ユーザーが欄に入力したまま Hierarchy の要素をクリックすると、(1) フォーカスは Hierarchy へ移る（この時点で `IsEditingText` は `focusedWindow != this` で false = ガードが効かない）、(2) Selection が変わり、追従で `_target` が子へ切り替わって UI が作り直される、(3) 古い欄の FocusOut が (2) の後に処理されると、確定のコールバックが**切り替え後の `_target`**（子）の `EmbeddedCanvases[index]` / `ElementEffects[index]` に書く。(2) で欄が先に破棄されれば値は失われる。どちらになるかは UI Toolkit のイベント順次第（**推定**）。docs/09 と DesignerManual は「入力中の欄があるあいだは切り替わりません」と書いているが、このガードは「このウィンドウにフォーカスがあるあいだ」しか守らない。
- **失敗の筋書き**: Hud を編集中、ElementFx 行の Delay 欄に `0.3` と打つ → Enter を押さずに Hierarchy で Option の中のボタンをクリック → 対象が Option に切り替わり、Option の同じ index の行の Delay が 0.3 になる（Undo では戻せるが、気付きにくい）。または入力した 0.3 が消える。
- **直し方の案**: (a) 各欄のコールバックで、作成時の対象を捕まえて（`var owner = _target;`）確定時に `owner != _target` なら `owner` に書く（Undo 名も owner で）か捨てる。(b) `IsEditingText` の代わりに、このウィンドウの入力欄で最後に FocusIn があってまだ確定していないものを覚えておき（`FocusInEvent` / `ChangeEvent` で管理）、未確定があるあいだは追従を保留する（1 フレーム遅らせて `EditorApplication.delayCall` で切り替えるだけでも、FocusOut の確定が先に済む可能性が高い）。(c) 手順書 docs/43 §16 に「入力途中で Hierarchy を選ぶ」確認を足す。
- **確度**: ガードが効かない経路と、コールバックが `_target` を確定時に引くことはコード読みで確認済み。値が別の対象に書かれるか失われるかは**推定**（イベント順）
- → 対応（2026-10-03、`a48da84`）: **一部確認 + 修正**。確認できた範囲: コード読みで、遅延確定の RootPath 欄・埋め込み行の子の欄・ElementFx の各欄のコールバックが**確定時の `_target`** を引いていること（ガード `IsEditingText` が `focusedWindow != this` では効かないこと）は確認済み。確認できなかった範囲: 値が別の対象に書かれるか失われるかは UI Toolkit の FocusOut と `Selection.selectionChanged` の順序次第で、EditMode テストからは再現できない（推定のまま）。**直し方（順序に依存しない形）**: (a) 各欄のコールバックが**作ったときの対象（`owner`）**を捕まえて書く（Undo 名も owner。owner が現在の `_target` のときだけ UI を再構築）。(b) 対象を切り替える直前（`ApplyTarget`）に `FlushPendingInput`（フォーカス中の要素を `Blur()`）で入力途中の欄を元の対象に確定させる。`GetFx` / `UpdateFx` は owner を引数に取る static に変更。docs/09 と DesignerManual に追記。**目視に回す**: [43] §16 の 16-24（入力途中で Hierarchy を選ぶ。再現できた / できなかったを結果欄に書く）。

### PC-R-06. 【U-28 設計】`SendSignal` の `ElementPath` が「単独で開いたとき」と「埋め込まれたとき」で変わる（公開後は変えられない意味）

- **場所**: `Runtime/Canvas/UiManager.cs:1094-1097` / `:1191-1194`（埋め込み時は `wire.ButtonPath` / `wire.ElementPath` を `Combine(next[0].Prefix, …)` に書き換えて `SendSignal` に渡す）、`SignalArgs`（`UiManager.cs:22-36`。`Key` / `Canvas` / `ElementPath` / `Value` のみ）、docs/07 の追記表・ProgrammerManual ui-api.html
- **何が問題か**: 同じ Option の CanvasData を「単独で `Ui.Open(Option)`」したときは `args.ElementPath == "BtnApply"`、Hud に埋め込まれたときは `"OptionRoot/BtnApply"`、`args.Canvas` もそれぞれ Option / Hud のハンドルになる。受け手が `ElementPath` で分岐しているコード（`if (args.ElementPath == "BtnApply")`）は埋め込んだ瞬間に動かなくなり、両対応には「末尾一致」等の脆い比較が要る。どの子 Canvas からのシグナルかを知る手段も無い（`Canvas` は親）。v1.4.0 で公開されると、この意味（親ルート基準）は §5 の互換面として固定される。
- **失敗の筋書き**: MS2026 で Option 画面を単独の画面として作り、`ElementPath` で分岐するハンドラを書く → 後日デザイナーが Option を Hud に埋め込む → ボタンを押してもハンドラが反応しない（エラーも警告も出ない）。
- **直し方の案**: タグ前に次のどちらかを決める。(a) **推奨**: `SignalArgs` に欄を足す（`readonly struct` への欄追加 + コンストラクタの多重定義の追加は追加のみで済む）: `LocalElementPath`（配線を持っている CanvasData のルート基準 = 単独でも埋め込みでも同じ値）と `Source`（配線を持っている CanvasData の `CanvasId`。単独なら `Canvas` の CanvasData と同じ）。`ElementPath`（親ルート基準）は今の意味のまま残す。ProgrammerManual には「`Key` で分岐し、要素で分けたいときは `LocalElementPath` を使う」と書く。(b) 欄を足さないなら、docs/07 と ProgrammerManual に「`ElementPath` で分岐しない。シグナルキーを要素ごとに分ける」を規約として明記する。
- **確度**: 確認済み（コード読み。仕様として docs に書かれている挙動で、バグではなく設計判断の確認）
- → 対応（2026-10-03、`a48da84`。**まとめ役の決定**）: **修正**。埋め込まれた子の配線が送る `SignalArgs.ElementPath` は**子のルート基準**（その子を単独で開いたときと同じ値）。埋め込みの位置は新しい欄 **`SignalArgs.EmbeddedRootPath`**（= 開いた Canvas のルートから見た埋め込みルートのパス。入れ子の入れ子は最外のルートからの連結 `OptionRoot/Inner`。親自身の配線・単独で開いたときは空文字）で渡す。`SignalArgs.Canvas` は従来どおり開いた（親の）ハンドル。`SignalArgs` に欄と 5 引数のコンストラクタを追加（既存の 3〜4 引数の署名は変更なし）、`UiManager.SendSignal` に 5 引数のオーバーロードを追加。docs/07・ProgrammerManual `ui-api.html` に明記。テスト: `ChildButtonWire_SendSignal_ElementPathIsChildRooted_…`・`SignalArgs_ParentOwnWire_And_StandaloneChild_…`・`NestedNested_SignalCarriesGrandchildRootedPath_…`・スライダー。

### PC-R-07. 【P-15 形式】`ddriveUpdate` の拡張の規則が未定（リリース後に変えられない形式）

- **場所**: `Editor/Update/PackageDependencyChecker.cs:15-80`（`Parse` / `ReadMap`: 未知のキーは無視、値が文字列でない項目は黙って無視、文字列だが `X.Y.Z` として読めない値は `DD-PKGDEP-BAD-DECLARATION` の Warning）、`:270-351`（比較は `SemVer.TryParse` = プレリリースを切り捨て）、docs/42 §4.2.1・§5（「追加のみ。フィールド名・意味の変更は MAJOR」）
- **何が問題か**: 骨格（`requires` / `compatibleWith`、キー = パッケージ ID、値 = 最低版の文字列）は妥当。ただし将来の拡張に効く次の点が**実装の偶然**で決まっていて、文書に無い。(1) 値に範囲（`">=1.4.0 <2.0.0"`）を書くと旧い D-Drive は BAD-DECLARATION の Warning を出す → 範囲は「値の書式の拡張」ではなく「新しいキー」で足すしかない。(2) 値をオブジェクトにすると旧い D-Drive は**黙って無視する**（`requires` の条件が消えても何も言わない）。(3) プレリリースは比較で無視される（`1.4.0-rc.1` が入っていれば `requires: "1.4.0"` を満たす。SemVer の順序では rc < 正式版）。(4) `ddriveUpdate` 直下の未知のキー（例: 将来の `below` / `platforms` / `unity`）は無視される。(5) `requires` に D-Drive が書く版とタグの関係（「`vX.Y.Z` のタグ = `package.json` の `version`」）は §4.2.1 の「外部パッケージに求める形式」にあるが、**同じリポジトリの 2 パッケージが別々に版を進める場合のタグ名**（`facial-v0.2.0` 等）は扱えない（`ParseVersionTags` は `v` で始まるものだけ）。
- **失敗の筋書き**: v1.5.0 で「上限」（例: T-Drive 0.6 は D-Drive 2.0 で動かない）を足したくなる → 値の書式を `"1.4.0 - 1.x"` に広げると、v1.4.x の D-Drive を使っている持ち込み先で T-Drive の宣言が BAD-DECLARATION になり、肝心の最低版の検査まで無視される。
- **直し方の案**: docs/42 §4.2.1 に「形式の拡張規則」を 1 節足して v1.4.0 で固定する: (a) 値は今後も `X.Y.Z` の文字列だけ（範囲・上限は**新しいキー**で足す。例: `"below": { "com.ddrive.core": "2.0.0" }`）、(b) 読み手は未知のキーを無視する（= 新しいキーは旧い D-Drive では効かない、が許容される）、(c) 値がオブジェクト・配列の項目は将来用の予約で、読み手は無視する（ただし `BAD-DECLARATION` の Info を 1 件出す方が安全。今の実装は黙って捨てる）、(d) プレリリースの扱い（「比較では無視する」で良いならそう明記。厳密にするなら今のうちに `rc < 正式版` にする）、(e) 同じリポジトリの複数パッケージは**同じタグ `vX.Y.Z` で揃える**ことを前提にする（T-Drive の 08「同じバージョン番号で一緒にリリース」と一致）。あわせて D-Drive 自身の `package.json` に `ddriveUpdate` を書かない理由（現状省略）も一文で。
- **確度**: 確認済み（コード読み。形式の吟味）
- → 対応（2026-10-03、`af4105a`。**まとめ役の決定**）: **修正**。[42] §4.2.1 に「形式の拡張規則（v1.4.0 で固定）」を追加し、実装を合わせた: (1) 値は今後も `X.Y.Z` の文字列だけ（範囲・上限は新しいキーで足す）。読めない**文字列**だけ BAD-DECLARATION（`PackageDependencyChecker.IsDeclaredVersion` で厳密化。以前の `SemVer.TryParse` は `-` 以降を捨てるため `"1.4.0 - 1.x"` を `1.4.0` として黙って読んでいた） (2) 未知のキーは黙って無視 (3) 値が文字列でない項目（オブジェクト・配列・数値・真偽・null）は将来用の予約として黙って無視（BAD-DECLARATION にしない。レビューの案 (c) の「Info を 1 件出す」は採らず、まとめ役の決定どおり無視） (4) プレリリースは比較で無視 (5) 同じリポジトリの複数パッケージは同じタグに揃える（片方だけ上げようとしたら確認ダイアログで案内。`PackageManifestOps.FindSiblingsAtOtherRef`） (6) D-Drive 自身は `ddriveUpdate` を宣言しない規約（D-Drive の版上げで上げ先の取得をしない理由）。**旧版の D-Drive が将来の拡張された宣言を読んでも壊れない**ことを `DdriveUpdateFormatCompatTests` で固定。

---

## P3 — 整理・改善

### PC-R-08. 【U-28 設計】ボタンの「親が勝つ」は要素単位で、親の `Click` 配線 1 本が子の `LongPress` 配線も消す

- **場所**: `Runtime/Canvas/UiManager.cs:1082`（`AncestorHasButtonRow(frames, wire.ButtonPath)` はトリガーを見ない）、docs/07 追記（「要素単位。親が `Click` だけ配線していれば子の `LongPress` 配線も適用されない」と明記）、DesignerManual canvas-editor.html の「親の設定が優先」（**この例外が書かれていない**）
- ElementFx は 1 行に Appear / Idle / Disappear を持つので要素単位が自然だが、`ButtonWire` は（要素, トリガー）ごとの行なので、「この画面だけクリック SE を変えたい」と親に `Click` を 1 本書くと子の長押し・連打の配線が黙って消える。予測しにくい。(a) ボタンだけ（要素, トリガー）単位にする（`IsJoinedPath` に加えて `Trigger` 一致）、または (b) 今の仕様のまま DesignerManual に一文足し、`DD-CANVAS-EMBED-OVERRIDE` の Info の文言に「トリガーに関係なく、この要素の子の配線はすべて使われません」と書く。タグ前に決める（意味が固定されるため）。確度: 確認済み
- → 対応（2026-10-03、`a48da84`。**まとめ役の決定**）: **修正**。担当の粒度を、ボタン・スライダーの配線は**(要素, トリガー)**、ElementFx は**要素**のままに確定（担当表のキー）。docs/07 の優先順位に表を追加、DesignerManual `canvas-editor.html` / `canvas-data.html`、Validator の Info（`DD-CANVAS-EMBED-OVERRIDE`。配線は「同じ要素・同じトリガーの配線は使われません(別のトリガーの配線は子の設定が使われます)」）の文言を合わせた。子の `CloseSelf` = 親（開いた Canvas）を閉じる、のまま（マニュアルに「確定」と明記）。テスト `ParentButtonWire_OnlyWinsForTheSameTrigger_ChildLongPressStillApplies`・`Validator_ParentButtonRow_OnlyOverridesTheSameTrigger`。

### PC-R-09. 【U-28】`RootPath` の正規化をしていない

- **場所**: `Runtime/Canvas/EmbeddedCanvasPaths.cs:29-55`（`TryToChildPath` は `rootPath` をそのまま前方一致）、`CanvasDataValidator.cs:88`（`root.Find(RootPath)` で存在だけ見る）
- `RootPath` に末尾 `/`・先頭 `/`・`\`・`./` が入ると、実行時の `Transform.Find` と Editor の `TryToChildPath`（追従・グループ表示・自動収集の除外）で判定が食い違いうる（`Find` が末尾 `/` を受け入れるかは**推定**）。手入力の「+ 手動で追加」で起きやすい。Validator に「`/` で始まる・終わる、`\`・`//`・`./` を含む」を新規コード `DD-CANVAS-EMBED-PATH-FORM`（Warning）で足し、`RootPath` 欄の確定時に `Trim('/')` と `\`→`/` をかける。確度: 正規化していないことは確認済み、`Find` の挙動は推定
- → 対応（2026-10-03、`b8ffad1`）: **修正**。`CanvasDataValidator` に `DD-CANVAS-EMBED-PATH-FORM`（Warning。先頭・末尾の `/`・`\`・`//`・`./`・`/./`）を追加、Canvas Editor の RootPath 欄は確定時に `\`→`/`・先頭末尾の `/` を除いて正規化。`Transform.Find` が末尾 `/` を受け入れるか（推定）は、書式を弾くようにしたので確認していない。テスト 8 ケース。

### PC-R-10. 【U-28】子の CanvasData が Preload でないことを事前に検出できない

- **場所**: `Runtime/Canvas/UiManager.cs:1426`（`TryResolveSync`。読み込まれていなければ警告 1 回 + スキップ）、DesignerManual canvas-editor.html（「子の Canvas データを Preload に」）
- 親を Preload、子を OnDemand にしたままでも Validation は何も言わず、実行して初めて「読み込まれていません」の警告になる。シーンの Preload 集計（`ScenePreloadAggregator`）は依存グラフ経由で子を拾うはずだが（`AssetId<CanvasMarker>` の汎用走査。**推定**）、手動の Preload には効かない。`CanvasEmbeddedValidator` に「子の `Flags.Load` が Preload でない」を新規コード（Warning）で足すと、マニュアルの前提を機械で守れる。確度: 確認済み（検査が無いこと）
- → 対応（2026-10-03、`b8ffad1`）: **修正**。`CanvasEmbeddedValidator` に `DD-CANVAS-EMBED-NOT-PRELOAD`（Warning。子の `Flags.Load` が Preload でない）。シーンの Preload 集計が依存グラフ経由で子を拾う可能性（推定）は確認していないが、単独の `Ui.Open` では拾われないので警告は有効。テスト 1 件。

### PC-R-11. 【U-28】`CanvasEmbeddedValidator` が CanvasData 1 件ごとにプロジェクト全体の CanvasData を検索する

- **場所**: `Editor/Canvas/CanvasEmbeddedValidator.cs:85-103`（`BuildLookup` が毎回 `CanvasLookup.Build()` = `AssetSearch.FindAssets("t:CanvasData")` + 全件 `LoadAssetAtPath`）
- 埋め込みを持つ CanvasData の数 × 全 CanvasData の読み込みになる（Run All で `ctx.AllAssets` に全件あるのに二重）。`ctx` ごとに 1 回だけ作ってキャッシュする（`ProjectSetupValidator` と同じ「ctx が同じなら使い回す」形）か、`ctx.AllAssets` に CanvasData が 1 件以上あればそれだけで足りるとする。確度: 確認済み
- → 対応（2026-10-03、`b8ffad1`）: **修正**。`CanvasEmbeddedValidator` のプロジェクト全体の CanvasData の検索を `ConditionalWeakTable<ValidationContext, CanvasLookup>` で 1 回の検証（= 1 つの ctx）につき 1 回にした。

### PC-R-12. 【U-28】「選択に追従」が埋め込みの無い使い方の挙動も変える

- **場所**: `Editor/Canvas/CanvasEditorWindow.cs:192-203`（`_followSelection` 既定オンで、埋め込みの有無に関係なく `FollowSceneSelection`）、`:1087-1108` / `:1110-1129`（プレハブステージの Prefab を持つ CanvasData を `Lookup.FindByPrefabPath` でプロジェクト全体から探す）
- 埋め込みを使わない人でも、Canvas Editor で A を開いたまま別の Canvas B の Prefab をプレハブモードで開いて要素をクリックすると、対象が B に切り替わる（従来は CanvasData アセットを選んだときだけ）。便利な面もあるが、`EmbeddedCanvases` が空のときは「従来どおり」という docs/07 の互換の記述とずれる。追従を「`ViewData`（現在の対象か、その親の連なり）の Prefab のステージ / プレビューの中だけ」に絞るか、docs/09・DesignerManual に「別の Canvas の Prefab の要素を選ぶとそちらに切り替わる」と書く。確度: 確認済み
- → 見送り（挙動は変えず docs のみ。2026-10-03、`b8ffad1` 以降の docs コミット）: 追従の範囲を絞る（現在の対象の Prefab のステージ / プレビューの中だけにする）と、別の Canvas の Prefab を開いたときに切り替わる便利さが無くなるため、挙動は変えず、docs/07・docs/09・DesignerManual `canvas-editor.html` に「別の Canvas の Prefab をプレハブモードで開いて要素を選ぶと、その Canvas に切り替わる（埋め込みを使わない場合も同じ）。切り替えたくなければチェックを外すか 🔒」を機能として追記した。絞るかはまとめ役の判断。

### PC-R-13. 【P-15】同じリポジトリの 2 パッケージ（T-Drive の toon と facial）を同じタグに揃える操作が無い

- **場所**: `Editor/Update/UpdateWindow.cs:680-722`（版上げは選択中の 1 行だけ）、`PackageManifestOps.SameRepository`（`?path=` まで比べるので 2 パッケージは別物）
- タグはリポジトリ単位なので 2 行とも同じ「最新」が出るが、上げるのは 1 行ずつで、そのたびに `Client.Resolve` と再コンパイルが走り、間の状態（toon だけ新しい）を一度通る。facial が `requires: { "com.tdrive.toon": "<新版>" }` を書いていれば、先に facial を上げると事前確認で Error →「それでも更新する」を押させることになる。「前の参照に戻す」も 1 行ずつ。版上げのダイアログに「同じリポジトリの管理対象（com.tdrive.facial）も同じタグにする」チェックを足し、manifest を 1 回で書き換えて 1 回だけ `Resolve` する（事前確認も 2 つまとめて `CheckPlanned` に渡す = 引数を一覧にする追加）。確度: 確認済み
- → 一部対応（2026-10-03、`af4105a`）: 版上げの確認ダイアログに「同じリポジトリの <ID> は <ref> のままです。同じタグに揃えてください」の案内を追加（`PackageManifestOps.FindSiblingsAtOtherRef`。[42] §4.2.1 の「同じリポジトリの複数パッケージは同じタグ」規則）。「同じタグにするチェック」で manifest を 1 回で書き換え・`Resolve` を 1 回にする・事前確認を 2 つまとめて `CheckPlanned` に渡す操作は**見送り**（UI・事前確認の組み合わせが大きい変更。T-Drive が実際に 2 パッケージで運用されてから判断）。

### PC-R-14. 【P-15】`Client.Add` の確認ダイアログに「そのパッケージのコードが Editor で実行される」旨が無い・manifest の書き換えの競合

- **場所**: `Editor/Update/UpdateWindow.cs:440-445`（「次の内容でパッケージを導入します(manifest.json に追加されます)。\n\n<URL>\n\nよろしいですか?」）、`:680-722`（版上げは `_addRequest` の進行中を見ない）
- URL を入れて導入すると、そのパッケージの `[InitializeOnLoad]` 等が再コンパイル直後に確認なしで動く（Package Manager の「Add package from git URL」と同じ権限）。ダイアログに「信頼できる提供元のパッケージだけを導入してください。導入すると、そのパッケージのスクリプトがこの Unity Editor で実行されます」を 1 行足し、URL のホスト部分を目立たせる。また `Client.Add` の実行中に版上げを押すと、版上げが読み込んだ manifest（Add 前）を保存して Add の書き込みを消しうる（`Client.Add` が manifest を書くタイミング次第。**推定**）。版上げ・戻すのボタンも `_addRequest` 進行中は無効にする。確度: 文言は確認済み、競合は推定
- → 一部対応（2026-10-03、`af4105a` / `b8ffad1`）: 導入の確認ダイアログに「信頼できる提供元のパッケージだけを導入してください。導入すると、そのパッケージのスクリプトがこの Unity Editor で実行されます」を追加。`Client.Add` の実行中は版上げ・元に戻すを始めない（警告のメッセージ）。URL のホスト部分を目立たせる表示は**見送り**（URL は全文をダイアログに出している）。`Client.Add` と manifest 保存の競合（推定）は、Add の書き込みタイミングを確認できていない（ガードで起きない形にした）。

### PC-R-15. 【P-15】「前の参照に戻す」の赤表示・登録解除・D-Drive 自身の登録

- **場所**: `Editor/Update/UpdateWindow.cs:734`（`broken` は「元に戻せる参照がある」かつ「この行に関わる Warning 以上がある」= 版上げと無関係な既存の問題でも赤くなる）、`DDriveProjectSettings.UnregisterManagedPackage`（要素ごと消すので `PreviousRef` も消える = 解除 → 再登録で元に戻せなくなる）、`PackageAddPlanner.Plan`（D-Drive 自身の URL / ID を入れると `RegisterExisting(com.ddrive.core)` → `_managedPackages` に D-Drive の要素が 1 件入る。一覧は `seen` で 1 行に保たれるが、設定に意味の無い要素が残る。docs/43 15-14 の期待「一覧は 1 行のまま」は満たすが設定は汚れる）
- (a) `broken` は「版上げ前に無かった問題」に限る（版上げ時に `CheckPlanned` の結果を `ManagedPackageEntry` / D-Drive 用の欄に残すか、`PreviousRef` を記録した時点の `Check` 結果との差で判定）。(b) 解除時に `PreviousRef` が空でなければ「元に戻す情報も消えます」と確認する。(c) `RegisterManaged` で D-Drive の ID は登録しない。確度: 確認済み
- → 一部対応（2026-10-03、`b8ffad1`）: (b) 登録解除で「前の参照に戻す」の情報（`PreviousRef`）が消えるときは確認する。(c) D-Drive 自身（ID `com.ddrive.core`）は管理対象の設定に登録しない（一覧は常に 1 行目）。(a) 「元に戻す」の赤表示を「版上げ前に無かった問題」に限る件は**見送り**（版上げ時の事前確認の結果を設定に残す必要があり、設定の形式を増やす変更。次の MINOR で）。

### PC-R-16. 【P-15】`PackageDependencyValidator` の static と、Run All での読み込み

- **場所**: `Editor/Validation/PackageDependencyValidator.cs:21-35`
- `ProjectSetupValidator` と同じ「直前の ctx を static に持つ」形で、ctx（全アセットの一覧を持ちうる）をドメインリロードまで保持する。害は小さいが、`WeakReference` か ctx の通し番号で持つ方がよい（既存の `ProjectSetupValidator` と同時に）。`ToResults` は「テスト用に公開」だが Editor 契約外なので、`[EditorBrowsable(Never)]` 等で目立たなくする（docs/53 FC-R-24 と同じ扱い）。確度: 確認済み
- → 一部対応（2026-10-03、`b8ffad1`）: `PackageDependencyValidator.ToResults` を `[EditorBrowsable(Never)]` にした。static の ctx 保持を `WeakReference` にする件は害が小さいため**見送り**（既存の `ProjectSetupValidator` と一緒に直す）。

### PC-R-17. 【テスト】実設定ファイルを書くテスト・抜けているケース

- **場所**: `Tests/Editor/Update/ManagedPackageRowsTests.cs:142-172`（`DDriveProjectSettings.instance` に登録・保存 → `finally` で解除。`Save(true)` で実 `ProjectSettings/DDriveProjectSettings.asset` を 2 回書き直す。途中で落ちると要素が残る）、`Tests/Editor/CanvasEmbeddedEditingTests.cs`（`CanvasEmbeddedValidator` のテストは `CanvasLookup.Build()` 経由でプロジェクト内の実 CanvasData も読む = 持ち込み先のデータで結果が変わりうる）
- (a) 設定の往復は `ManagedPackageList`（純関数）のテストで足りているので、実設定を書くテストは `[Explicit]` にするか、`ScriptableObject.CreateInstance<DDriveProjectSettings>()` で別インスタンスを作って `JsonUtility` で往復する。(b) 抜けているテスト: 重なった埋め込み（PC-R-04）、同じ子を 2 か所に埋め込む（今の実装で正しく動くことの固定）、親の `Click` が子の `LongPress` を消す（PC-R-08 の仕様の固定）、プレリリースのタグ（PC-R-02）、`?path=` に `..`（PC-R-01）。確度: 確認済み
- → 一部対応（2026-10-03、`b8ffad1`）: (a) 設定を書くテストに `[TearDown]`（`com.test.p15.*` の要素を確実に除去）を追加。実設定ファイルを書く往復テスト自体は残した（`DDriveProjectSettings` は ScriptableSingleton で別インスタンスを作れず、`[Explicit]` にすると CI で動かなくなるため）。(b) 抜けていたテスト: 重なった埋め込み・同じ子を 2 か所・親の Click が子の LongPress を消さない（PC-R-08）・プレリリースのタグ・`?path=` の `..` は PC-R-01/02/04/08 の対応で追加済み。`CanvasEmbeddedValidator` のテストが `CanvasLookup.Build()` 経由でプロジェクト内の実 CanvasData も読む点は、ctx 側が優先される（同じ Id は ctx 側）ため結果は変わらないので**見送り**。

### PC-R-18. 【公開面】`EmbeddedCanvasPaths` は Runtime の公開 API に置くほどのものではない

- **場所**: `Runtime/Canvas/EmbeddedCanvasPaths.cs`（`public static class`、`Combine` / `TryToChildPath` / `IsJoinedPath`）、`public-api-DDrive.Runtime.txt` の追加行
- `InternalsVisibleTo` が無いため Editor から使うには public が必要、という既存の事情は分かるが、これが入ると「パス文字列の連結・前方一致」という汎用の関数が `DDrive.Runtime` の公開 API に永久に残る（§5.4）。案: (a) `internal` にし、Runtime の AssemblyInfo に `[assembly: InternalsVisibleTo("DDrive.Editor")]` とテスト asmdef を足す（asmdef 自体は変えない。ただし今まで避けてきた方針の変更なので要判断）、(b) Editor 側に同じ 3 関数の複製を置き、Runtime 側は `UiManager` の private にする（数十行の重複）。タグ前ならどちらも互換性ポリシー違反にならない。確度: –
- → 対応（2026-10-03、`a48da84`。**まとめ役の決定: internal 化 + Editor 側に internal コピー**）: `EmbeddedCanvasPaths` を **internal** にし（`IsJoinedPath` は担当表方式で不要になり削除。`Combine` / `TryToChildPath` は Runtime 内の担当表のキー・Validator で使う）、Editor は同じ規則の internal 複製 `Editor/Canvas/EmbeddedPaths.cs`（`Combine` / `TryToChildPath`）を持つ。**両者の一致はリフレクションで固定**（`EmbeddedCanvasPathsTests`: 同じ表を両方に当てる。型が public でないことも確認）。理由: `InternalsVisibleTo` を置かない方針のため。公開 API スナップショットは更新（差分は 9f40cbb 以降に増えた `EmbeddedCanvasPaths` の 3 行の削除と `SignalArgs` / `UiManager.SendSignal` の追加のみ。v1.3.1 の行の削除 0 件）。

### PC-R-19. 【docs】マニュアルの「従来どおり」・docs の古くなる記述

- **場所**: DesignerManual canvas-editor.html（「まだ登録していない入れ子の Prefab は、従来どおり親の一覧に出ます」）、canvas-data.html / ProgrammerManual ui-api.html（「子を単独の画面として … する使い方は従来どおり」）、update.html の各所、docs/42 §4.2.1（取得方式の記述 = PC-R-01 を直すと変わる）
- マニュアルは機能だけを書き、以前との違いを書かない運用（2026-09-19 指摘）。「従来どおり」は「〜も使えます」「〜は親の一覧に出ます」に言い換える。PC-R-01・02・03・08 を直すときは docs/42 §4.2.1・docs/07 追記・update.html を同じ PR で直す。確度: 確認済み
- → 対応（2026-10-03、docs コミット）: **docs 修正**。DesignerManual `canvas-editor.html` / `canvas-data.html`、ProgrammerManual `ui-api.html` の「従来どおり」を「〜も使えます」「〜は親の一覧に出ます」に言い換え。PC-R-01・02・03・08 を直すときに [42] §4.2.1・[07] 追記・update.html・[09]・[39] も同じ PR で直した。

### PC-R-20. 【U-28】プリセットギャラリーで、プレハブモードの Prefab と `_canvas` が違うときのパス

- **場所**: `Editor/Ui/UiPresetGalleryWindow.cs`（2026-10-03 の差分。`FindCanvasRoot` はプレハブステージなら**ステージのルート**を返し、その後 `ResolveOwner(_canvas, path, …)` を `_canvas` 基準で解く）、`CanvasEmbeddedEditing.cs:282-321`
- ギャラリーの対象が親 Hud のまま、子 Option の Prefab をプレハブモードで開いて要素を選ぶと、パスは Option のルート基準なのに Hud の埋め込みとして解こうとし、Hud の行のパスとして `Panel/BtnX` が入る（Hud には無い要素）。入れ子の無い既存の使い方（対象の Prefab をプレハブモードで開いて選ぶ）は以前より正しくなった（UI Prefab のプレハブモードは環境用の Canvas の下に置かれるため、以前の `selected.root` 基準ではパスがずれていた。**推定**）。ステージの Prefab が `_canvas.Prefab` でも、その祖先・子孫の埋め込みでもないときは、そのステージの Prefab を持つ CanvasData に切り替えるか、警告を出す。確度: 確認済み（コード読み）、以前の挙動は推定
- → 見送り: プリセットギャラリーの対象とプレハブモードの Prefab が食い違うときのパスは、UI の目視でしか確認できず（推定部分もある）、直し方（ステージの Prefab を持つ CanvasData に切り替えるか警告を出すか）に判断が要る。入れ子の無い既存の使い方は正しくなっているので、v1.4.x で。

### PC-R-21. 【P-15】外部プロセスの引数の組み立てとタイムアウトの残り

- **場所**: `GitSparsePackageJsonFetcher.cs:185` / `GitCliTagLister.cs:116`（`Quote` は `"` を `\"` にするだけ。値の末尾が `\` だと閉じ引用符が逃がされる）、`GitSparsePackageJsonFetcher.cs:47`（`sparse-checkout set "<path>"`。`?path=` が `-` で始まると `--stdin` 等のオプションとして読まれる。標準入力はリダイレクトしていないので待ちになりうる）
- URL 側は `GitPackageUrl.LooksLikeGitUrl` が `https://` / `ssh://` / `git://` 等で始まるものしか通さないので、URL が `-` で始まるオプション注入・`ext::` / `file://` の transport は入口で塞がれている（問題なし）。残りは `?path=` と `#ref`（`--branch` の値として渡るので `-` で始まっても値として扱われる）。`--` の修正（別担当）に合わせて、`ProcessStartInfo.ArgumentList`（使えれば）で 1 引数ずつ渡す形にし、`RedirectStandardInput = true` で標準入力を閉じる。PC-R-01 の方式に変えれば `sparse-checkout` 自体が無くなる。確度: 確認済み（`ArgumentList` が Unity の Mono で使えるかは推定）
- → 対応（2026-10-03、`af4105a`）: **修正**。`Quote` は `GitArguments`（ラウンド 1。`ProcessStartInfo.ArgumentList` で 1 引数ずつ渡す）で無くなっていた。`sparse-checkout set` は PC-R-01 の方式変更で無くなり、`?path=` は `TryNormalizePackagePath` が `-` 始まり・制御文字を弾く。標準入力は `GitProcess` が閉じる（`RedirectStandardInput` + `Close`）。`ArgumentList` が Unity の Mono で使えること（推定）は、実 git（上記 PC-R-01 の確認）で動作したことで確認済み。

---

## 確認して問題なしだった観点

**P-15**

- **URL の入口**: `GitPackageUrl.LooksLikeGitUrl` が `git+https` / `git+ssh` / `git+http` / `git://` / `ssh://` / `.git` で終わる `https` / `http` だけを git URL とみなすので、`CloneUrl` は常にスキームで始まる（`-` で始まる URL、`ext::`・`file://`・ローカルパスは「解釈できない」で止まる）。`ssh://-oProxyCommand=…` 形のホスト名は git 側（2.14.1 以降）が拒否する（推定）
- **プロセスの読み取り**: 標準出力・標準エラーとも非同期読み取り（`BeginOutputReadLine` / `BeginErrorReadLine`）なので、出力が多くてもパイプが詰まってデッドロックしない。`WaitForExit(ms)` の後に `WaitForExit()` で読み切りを待つ。例外は握って警告にする。fetcher は `GIT_TERMINAL_PROMPT=0`
- **一時フォルダのパス**: `Application.dataPath/../Temp/DDriveUpdate/<Guid N>` を毎回新しく作り、削除はそのフォルダだけ（ユーザー入力から削除先のパスは作られない）
- **JSON の解析**: `DdriveUpdateDeclaration.Parse` / `ParseVersion` は `try/catch` + 型の確認で、壊れた JSON・想定外の型・空文字で例外を出さない。値が文字列でない項目は無視
- **依存検査の純関数**: 自己参照は無視、相互参照は再帰しないので循環で止まらない、2 桁の版（`1.4`）は `1.4.0` に揃える、`compatibleWith` は相手が未導入なら何も言わない、MAJOR 差は Info。`CheckPlanned` は「今の組み合わせに無かった問題」だけを返し、D-Drive を下げて他パッケージの `requires` / `compatibleWith` を割る場合も（取得できなかったときも）検出する。テストが主張を検証している
- **manifest の書き換え**: `TryBumpRef` は `#ref` だけを差し替え（URL・`?path=`・`git+` は不変）、書き換え前の値を設定に退避してから保存。戻すは入れ替え（2 回押すと元に戻る）。書き換え途中で失敗した場合も、退避した値が今の manifest の値なので害は無い。追加の保留は `SessionState` で、ドメインリロード後に `CreateGUI` → `ResumePendingAdd` が URL（ref を除く）一致で登録する
- **パッケージ ID と manifest のキー**: 新規導入は `Client.Add` の結果の `PackageInfo.name` で登録し、既存は manifest のキーで登録する。同じリポジトリ + 同じ `?path=` の判定は末尾 `/`・`.git` の揺れを吸収（https と ssh は別物として扱う = 安全側）
- **D-Drive だけのプロジェクト**: 1 行目は常に D-Drive、従来の「1〜6」節が出る。D-Drive の単数フィールド（`LastAppliedVersion` / `PreviousPackageRef`）はそのまま。タグの解析も D-Drive の行は従来の `Parse`。ウィンドウを開いただけではネットワークに触れない（`git ls-remote` はボタンを押したときだけ）。変わったのは PC-R-03 の待ちだけ
- **設定の互換**: `_managedPackages` は末尾追加の `[SerializeField]` で、旧設定は空の一覧として読まれる（`??=` で null も吸収）。`ManagedPackageEntry` は 3 つの文字列だけ
- **Validation**: 新規コード `DD-PKGDEP-*` は Warning / Info のみ（`requires` 未充足も Validation では Warning）。1 回の Run All につき 1 回だけ報告。`ProjectSetupValidator` の `DD-SETUP-UPDATE-PENDING` は D-Drive の `LastAppliedVersion` だけを見るので二重報告にならない
- **`vX.Y.Z` 以外の ref**: UniTask / R3 の `#2.5.11` やハッシュ・ブランチは候補に「最新版は判定できません」と出て、版上げ対象にならない（`--branch <hash>` の事前確認は失敗して警告になるだけ）
- **CHANGELOG**: 外部パッケージはパッケージ直下だけを探し、D-Drive の開発リポジトリ用の 2 階層上の探索を使わない
- **ウィンドウ規約**: `ScrollView` ルート、メニューは `DDriveMenu.Update` 経由、新規 EditorWindow は無し

**U-28**

- **空のとき**: `EmbeddedCanvases` が null / 空なら `SetupEmbeddedCanvases` は最初の判定で戻り、割り当て無し。`WireButtons` の `= new List` → `??=` の変更は、`CanvasInstance` が Open のたびに新しく作られるため挙動が同じ（プールされるのは GameObject だけ）。`SetupElementFx` の `ElementFx ??=` も同様
- **データを書き換えない**: `ButtonWire` / `SliderWire` / `ElementFx` は struct で、`wire.ButtonPath = …` はコピーへの代入（子の CanvasData は変わらない）
- **再帰・循環・深さ**: 祖先の連なりに同じ CanvasData があれば循環として警告 1 回 + その埋め込みだけスキップ、深さ 8 で打ち切り。同じ子を兄弟の 2 か所に埋め込むのは循環扱いにならず、それぞれのルートで適用される
- **パスの判定**: `TryToChildPath` は `Option2/…` を `Option` の配下と誤判定しない。`IsJoinedPath` は割り当て無し。子ルート自身（空パス）の行は `FindTransform` が null を返すので従来どおり対象外。外側ほど強い（孫 < 子 < 親）
- **実行時の後始末**: 子の ElementFx は親の `instance.ElementFx` に乗るので、Appear の入力ゲート・Close の Disappear 待ち・Idle の停止・`StopAll` が子の分にも効く。子の配線の購読解除も同じ `WireUnsubscribers`。プールからの再 Open で二重配線にならないことはテストで固定
- **同期解決**: `TryResolveSync` が失敗したら警告 1 回 + スキップ（例外なし）。Editor のプレビュー用 Registry（`EditorAnchorRegistry`）は CanvasData を全件ロード済みにしているので、確認用プレビューでも子が解決される
- **割り当て**: 埋め込みがあるときの Open で、フレームのリストと連結文字列・配線のクロージャが作られるが、Open 経路（既存も配線ごとにクロージャを作る）で Tick には無い。LINQ 無し
- **ContentHash / ネット**: `CatalogContentHasher` は `CatalogEntry` の Id / Type / Address / Net だけを混ぜるので、`CanvasData` の欄追加の影響は無い（確認済み）。Canvas はネット同期の対象外
- **シリアライズ・公開 API**: `EmbeddedCanvases` は `CanvasData` の末尾追加、スナップショットの差分は追加行だけ。`SchemaVersion` は上げていない（欄追加のみなので不要）
- **Validator**: 新規コードの Warning（`-ROOT` / `-DUP` / `-UNSET` / `-SELF` / `-MISSING` / `-CYCLE` / `-PREFAB`）と Info（`-OVERRIDE`）のみ。既存コードの重さは不変。Runtime 側は他アセットを引かない検査だけ
- **Editor の書き込み**: 埋め込みの登録・追加・削除・`RootPath` / 子の変更・自動収集は `Undo.RecordObject` + `SetDirty`。自動収集の除外は「配下の新規の行を足さない」だけで、既存の行は消さない。埋め込み配下の「一括適用」は親の既存の行にも適用しなくなるが、埋め込みを登録したときだけの変化
- **ADR-4**: 子を編集中の確認用プレビューは親（連なりの最外側）を実 `UiManager.OpenData` で開き、子の設定は本番と同じ経路で適用される。Editor 専用の再生経路は作っていない（▶ は既存どおり実 `UiTweenManager`）。プレハブモードでの再生は既存の `ElementFxStateSnapshot` で控え・復元する
- **プリセットギャラリー**: 入れ子の無い使い方（確認用プレビュー・シーンに置いた Prefab インスタンス）は、Canvas のルートが見つからなければ従来の `selected.root` 基準に戻る

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・実 `git`・実 `Client.Add`・UI の見た目）。特に PC-R-01・02・03・05・14 の推定部分
- `CanvasEditorWindow.cs` の差分（約 780 行）のうち、ElementFx 一覧のグループ表示・絞り込み・強調表示・パッド操作シミュレーションの有効 / 無効は流し読みのみ。`ElementFxStateSnapshot` が「再生中に対象を親 → 子へ切り替えた」ときに正しく戻るかは追えていない
- `UpdateWindow` の旧版との差分は更新チェック節・版と CHANGELOG 節・パッケージ節を通読。「3〜6」節（マイグレーション・適用・テスト・スキル）は表示条件の変更だけを確認
- docs/43 §15・§16 の手順は読んだが、期待結果どおりになるかは未確認。SpecWeb の再生成物（`Tools/SpecWeb/html/manual/*`）は対象外
- T-Drive 側のコード・実際の `package.json`（`ddriveUpdate` を書くのは T-Drive の今後の作業）は無く、docs のコピーだけで相性を判断した

---

## 公開 API / 形式として残してよいか（タグ前に決めるもの）

v1.4.0 のタグはまだ打たれていないので、**下の「internal 化 / 変更」はタグ前なら互換性ポリシー違反にならない**（MS2026 はタグを参照している）。タグ後は §5.1（シリアライズ）/ §5.4（Runtime）/ §4.2.1（`ddriveUpdate`）の手続きが要る。

| 型・メンバー・形式 | 層 | 推奨 | 理由 |
|---|---|---|---|
| `package.json` の `ddriveUpdate`（`requires` / `compatibleWith`、キー = パッケージ ID、値 = 最低版 `X.Y.Z` の文字列） | 外部パッケージとの契約 | **残す。ただし拡張規則を文書化してから**（PC-R-07） | 骨格は妥当（最低版のみ + MAJOR 差は Info、という割り切りは扱いやすい）。値の書式を後から広げると旧い読み手が Warning を出すので、範囲・上限・条件は新しいキーで足す、未知のキーは無視、オブジェクト値は予約、プレリリースの扱い、同じリポジトリの複数パッケージは同じタグ、を v1.4.0 で固定する<br>→ **実施**: 拡張規則を [42] §4.2.1 に固定（PC-R-07）。 |
| `DD-PKGDEP-*`（5 コード、Warning / Info） | Validation | 残す | §5.8 どおりの新規 Warning。Error への昇格は次の MINOR 以降 |
| `DDriveProjectSettings.ManagedPackages` / `ManagedPackageEntry`（3 欄） | Editor（ProjectSettings にシリアライズ） | 残す | 追加のみ。D-Drive 用の単数欄と二重管理だが、旧設定との互換のため妥当 |
| `PackageDependencyChecker` / `DdriveUpdateDeclaration` / `PackageState` / `PackageDependencyIssue` / `PackageAddPlanner` / `PackageManifestOps` / `ManagedPackageRows` / `UpdatePreflight` / `IRemotePackageJsonFetcher` / `GitSparsePackageJsonFetcher` / `InstalledPackages` | Editor（契約外） | 残してよい（テストのため public）。外部に約束しない旨を `editor-contract.txt` に載せないことで足りる | 外部が呼ぶものではない。`PackageDependencyValidator.ToResults` は `[EditorBrowsable(Never)]` 推奨（PC-R-16）<br>→ **実施**: `ToResults` は `[EditorBrowsable(Never)]`。`GitSparsePackageJsonFetcher` は `GitPackageJsonFetcher` に改名。 |
| `GitTagListParser.ParseVersionTags` / `ChangelogLocator.ResolvePackageOnlyPath` | Editor（契約外） | 残す。PC-R-02 の直しでは**新しいメソッドを足し**、既存の戻り値（`List<Version>`）は変えない | –<br>→ **実施**: 既存の戻り値は変えず、`ParseTags`（元のタグ名を保つ）を足した（PC-R-02）。 |
| `EmbeddedCanvas`（`RootPath` / `Canvas`）・`CanvasData.EmbeddedCanvases` | Runtime（シリアライズ） | **残す** | 最小の 2 欄。`AssetId<CanvasMarker>` にした判断（ピッカーが Canvas に絞られる）は妥当。将来の「子の Navigation も使う」等の切り替えは欄の追加で足せる |
| `EmbeddedCanvasPaths`（`Combine` / `TryToChildPath` / `IsJoinedPath`） | Runtime | **internal 化（または Editor へ複製）を推奨**（PC-R-18） | 汎用の文字列ユーティリティが Runtime の公開 API に永久に残る<br>→ **実施**: internal 化 + Editor 側に internal 複製（PC-R-18）。 |
| `SignalArgs.ElementPath` の意味（埋め込み時は親ルート基準） | Runtime（意味） | **タグ前に決める**（PC-R-06） | 欄を足す（`LocalElementPath` / `Source`）なら今のうち。足さないなら「`ElementPath` で分岐しない」を規約にする<br>→ **実施**: 子のルート基準 + 新しい `EmbeddedRootPath` 欄（PC-R-06）。 |
| 「親が勝つ」の粒度（ボタンはトリガーに関係なく要素単位） | Runtime（意味） | **タグ前に決める**（PC-R-08） | 後から（要素, トリガー）単位に細かくすると、親の 1 本で消えていた子の配線が復活する = 挙動の変化<br>→ **実施**: ボタン・スライダー = (要素, トリガー)、ElementFx = 要素（PC-R-08）。 |
| `DD-CANVAS-EMBED-*`（8 コード） | Validation | 残す | 新規 Warning / Info |
| `CanvasEmbeddedEditing`（`CanvasLookup` / `Candidate` / `Link` / `Owner` / `EmbedGroup` / `FxGroups` ほか） | Editor（契約外） | 残してよい | テストのため public。外部が使う型ではない |

## リリース（v1.4.0）前にやるべきことの順序

1. **形式・意味の決定（コードを書く前に決める）**: PC-R-07（`ddriveUpdate` の拡張規則を docs/42 §4.2.1 に固定）、PC-R-06（`SignalArgs` に欄を足すか規約にするか）、PC-R-08（ボタンの優先の粒度）。いずれも v1.4.0 で固定される
2. **PC-R-04**（重なった埋め込みの二重適用）を実行時・Validator・`DetectCandidates` の 3 か所で直し、PlayMode テストを足す
3. **PC-R-02**（プレリリースのタグ）: 元のタグ名を使う形に直し、テストを足す。P-14 から既存の穴なので CHANGELOG の互換性節に「修正」として 1 行
4. **PC-R-01 / PC-R-21**（取得方式を `--no-checkout` + `git show` へ。`?path=` の検査。`--` の修正と同じ PR でよい）と **PC-R-03**（進捗表示 + キャンセル、`GitCliTagLister` の `GIT_TERMINAL_PROMPT`）
5. **PC-R-05**（選択追従と遅延確定の入力欄）: 対象を捕まえる形に直す（少なくとも別の CanvasData に書かれないようにする）
6. **公開面の整理**: PC-R-18（`EmbeddedCanvasPaths`）→ `public-api-DDrive.Runtime.txt` を更新（タグ前なので削除行が出てよいが、スナップショットテストは削除 = fail なので更新手順で上書き）。docs/53 の FC-R-24 と同じ PR でまとめてよい
7. docs の更新（PC-R-19、上で変えた仕様の docs/07・docs/42 §4.2.1・update.html・DesignerManual / ProgrammerManual の反映）
8. EditMode / PlayMode の両方を green にし、docs/43 §15（特に 15-2 の待ち・15-10 の事前確認）と §16（入力途中の選択追従を追加）の人による確認を済ませてからタグ
9. 残りの P3（PC-R-09〜17・20）は v1.4.x / 次の MINOR で可

---

## 対応記録（2026-10-03、`fix/review-round-2-p15-canvas`）

> 修正ラウンド 2。P2（PC-R-01〜07）は全件対応。推定だった点の確認結果: PC-R-01 の子プロセスの残り = 実 git で**残らない**ことを確認 / PC-R-04 の `IsAnyPrefabInstanceRoot` = 入れ子の入れ子でも **True**（レビューの推定どおり。除外が必要）/ PC-R-05 の値が書かれるか失われるか = イベント順の再現は**自動では確認できず**、順序に依存しない形に直して目視に回した（[43] §16 の 16-24）/ PC-R-03 の GCM の GUI = 未確認（`GCM_INTERACTIVE=never`）。P3 は小さく安全なものを直し、大きい・判断が要るものは各項に理由を付けて見送り（PC-R-12・13・15(a)・16(後半)・20）。設計の決定（まとめ役）: PC-R-06 の `EmbeddedRootPath`・PC-R-08 の粒度・PC-R-18 の internal 化。前ラウンドの持ち越し（FC-15 / FC-11 / FC-R-06 の DesignerManual 追記）も同じ PR。
>
> 検証: EditMode・PlayMode の全件（件数は PR 本文）。実 git の確認は PC-R-01 の項。
