# 55. 2026-10-03 自前レビュー結果（修正ラウンド 1・2 = docs/53・docs/54 の指摘への対応）

> **対象**: 2026-10-03 に main へ入った修正ラウンド 2 本。どちらも Sonnet のサブエージェントが実装し、まとめ役は本体の差分をほぼ読まずにマージした。
>
> | マージ | PR | 内容 |
> |---|---|---|
> | `20c75bf` | #102 | 修正ラウンド 1（[53](53_review_fc_2026-10-03.md) の対応）: FC-R-01 / 02（右クリック作成の確認・`UnknownShaderHandling.ConvertKeepingExisting`・`UnknownShaderGuard.IsMissing`）、FC-R-03（0 秒のマーカーの発火。既存 4 種にも適用）、FC-R-05 / 09（バインド解決・外部マーカー発火の再入耐性）、FC-R-06（LightMode タグ無しパス = `SRPDefaultUnlit`）、公開面の整理、P-15 の git 引数（`GitArguments`・`--`・`ArgumentList`）、P3 数件 |
> | `64c0301` | #103 | 修正ラウンド 2（[54](54_review_p15_canvas_2026-10-03.md) の対応）: PC-R-01（`clone --no-checkout` + `git show`・`?path=` 検査・`GitProcess` のツリー停止・一時フォルダ掃除）、PC-R-02（`GitTag`・プレリリース）、PC-R-03（バックグラウンド化・進捗・キャンセル）、PC-R-04（担当表 `EmbedClaims`）、PC-R-05（`owner` 捕捉・`Blur()`）、PC-R-06（`SignalArgs.EmbeddedRootPath`）、PC-R-07（`ddriveUpdate` の拡張規則）、PC-R-08（(要素, トリガー) 単位）、`EmbeddedCanvasPaths` の internal 化 + Editor 側の複製、新規 Warning 3 種、P3 数件 |
>
> **方法**: 専用 worktree を `64c0301`（detached）に合わせ、`git diff 20c75bf^1 20c75bf` / `git diff 64c0301^1 64c0301` / `git diff 9f40cbb..64c0301 -- …/Compat/Snapshots/` と、変更後のファイル全体（`CutsceneManager` の Play / Tick / マーカー / ネット受信、`CutsceneEditModePreviewProvider`、`CutsceneEditModeDirectorSetup`、`UnityMaterialMigrator`、`UnknownShaderGuard`、`SourceDataCreation`、`ModelSlotBinder`、`MayaMaterialImporter` の既存 Data 経路、`MaterialShaderInfo`、`GitArguments`・`GitProcess`・`GitPackageJsonFetcher`・`GitCliTagLister`・`GitTag`・`GitTagListParser`・`UpdateCheckLogic`・`UpdatePreflight`・`PackageAddPlanner`・`PackageDependencyChecker`・`PackageManifestOps`・`UpdateWindow` の非同期 / 版上げ / 更新チェック節、`UiManager` の埋め込み節と `SignalArgs`、`CanvasDataValidator`、`CanvasEmbeddedValidator`、`CanvasEmbeddedEditing`、`CanvasEditorWindow` の差分、追加テスト）を**読むだけ**で確認した。実装者の報告（[53] / [54] の「→ 対応」・CHANGELOG・docs/26・docs/42・docs/07）は信用せず、コードと突き合わせた。**Unity は起動しておらず、コンパイル・EditMode / PlayMode テスト・実 `git` は一切実行していない。** 指摘はコードを読んで確認した事実か、Unity / git / .NET の挙動についての推定で、推定のものは「確度」欄に**推定**と書いた。
>
> 前提として読んだもの: `CLAUDE.md`（§0）、[docs/12](12_review.md) §3、[docs/42](42_distribution.md) §4.2.1・§5（E-20）、[53](53_review_fc_2026-10-03.md)・[54](54_review_p15_canvas_2026-10-03.md)（元の指摘と対応記録。書式と重大度の基準）、`CHANGELOG.md` の `[Unreleased]`、[docs/07](07_canvas_prefab.md)（埋め込みの確定仕様）、[docs/26](26_timeline.md) §4.3 / §4.4 の 2026-10-03 追記、[docs/43](43_manual_verification_2026-09-17.md) §15・§16、[docs/52](52_manual_verification_fc.md)。

## 総評

- **リリースを止める実バグ（P1）は見つからなかった**。互換面は v1.3.1 から見て**追加のみ**（`9f40cbb..64c0301` のスナップショット 4 ファイルの差分は `+120 / -0`。v1.3.1 にあった行の削除・変更は 0 件、増えた行は FC / U-28 / 本ラウンドの `SignalArgs.EmbeddedRootPath`・5 引数の ctor・`SendSignal` の 5 引数オーバーロードの追加だけ）。`EmbeddedCanvasPaths` の internal 化は v1.3.1 に無い型なので削除にならない。
- 元の P2 13 件は **10 件が解消、3 件が一部解消**。一部の 3 件はいずれも「書いたとおりには直っているが、書いていない経路が残る」形:
  - **FC-R-03（0 秒のマーカー）**: ローカルの `Play` では直ったが、**ネットワーク受信側は `elapsed = NetworkTime − StartNetTime > 0` が常態なので、途中参加でなくても 0 秒のマーカーが鳴らない**。発信側（予測再生）だけ鳴る非対称になった（以前はどちらも鳴らなかった）。対応記録・E-20・docs/26 の「Late Join = 開始位置 > 0」「Host / Client とも同じ」は実装と食い違う（**FX-R-01、P2**）。
  - **FC-R-02（欠けたシェーダー）**: 新規 Data は Lit になるようになったが、**シェーダーが一時的に欠けている間（T-Drive の解決失敗・GUID 切れ）に既存の MaterialData を再生成すると、既存 Data の Shader 参照が Lit に置き換わり、元の参照が失われる**（`KeepSource` / 「保つ」を選んでも同じ）。docs/52 15.6 の 3 の手順をそのまま実データで行うと起きる（**FX-R-02、P2**）。
  - **FC-R-09（Fire 中の停止）**: 外部マーカーのループだけ直した。既存の Signal マーカーの購読者（`CutsceneHandle.OnMarker` = ゲームのコード）が中で止めた場合と、**別のカットシーンを止めた場合の `Tick` の添字ずれ**（同じ Tick に 2 回進む）は残る。対応記録の「既存 4 種は外部コードを呼ばない」は誤り（FX-R-03、P3）。
- **修正が入れた新しい P1 / P2 は無い**（FX-R-02 はラウンド 1 前から同じ経路で参照を失っていた〔`KeepSource` なら `Hidden/InternalErrorShader` で上書き〕ので「対処の取りこぼし」として P2 に置いた）。P3 は 12 件（Edit Mode の「先頭から」判定が更新間隔に依存・`GitProcess` の `WaitForExit()` の無期限待ち・ドメインリロード時の孤児プロセス・プレリリースだけのときの既定選択・`KeepsExistingUnknownShader` が元の `.mat` を標準シェーダーに戻しても効く・重なる登録で「外側ほど強い」が逆転・CHANGELOG の漏れと古い記述 ほか）。
- **git まわりは堅くなった**: 作業ツリーを作らない取得・`--` の位置・`ArgumentList`・`?path=` の検査・環境変数・標準入力を閉じる・ツリー停止・`Temp/DDriveUpdate` の外を消さない掃除は、読んだ範囲で正しい。残りは「子孫プロセスがパイプを掴んだままのときの無期限待ち」（推定）と「ドメインリロード時の後始末」。
- **Canvas の担当表は docs/07 のとおりに実装されている**（`Open` した CanvasData を種にし、浅い段から幅優先・段の中は `RootPath` の深い順・(要素, トリガー) 単位）。**v1.3.1 の埋め込みなしの挙動は変わっていない**（`EmbeddedCanvases` が空なら担当表を作らず、`SignalArgs.EmbeddedRootPath` は空文字、`ElementPath` は従来どおり）。ただ「重なる登録では内側が先」は「外側ほど強い」と逆向きになる組み合わせがあり、テストがその逆転を固定している（FX-R-10）。タグ前に意図を確認したい。
- **CHANGELOG の「互換性」節**は本ラウンドの変更をほぼ拾っているが、D-Drive 自身の更新チェックで**認証プロンプトが出なくなった**（`GIT_TERMINAL_PROMPT=0` / `GCM_INTERACTIVE=never`。v1.3.1 の P-14 では GCM のログイン画面が出えた）ことが漏れている。FC-15 の項の「非対話は従来どおり（結果は不変）」は `ConvertKeepingExisting` で事実でなくなった（FX-R-11 / 12）。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **0** | – |
| P2（直すべき不具合・設計上の穴） | **2** | FX-R-01〜02 |
| P3（整理・改善） | **12** | FX-R-03〜14 |

---

## 元の指摘 13 件の解消確認

「解消」= 元の失敗の筋書きが起きなくなり、対応記録が実装と一致し、別の経路を壊していない。「一部」= 書かれた経路は直ったが、同じ指摘の範囲に残りがある。

| 指摘 | 判定 | 根拠（ファイル:行） | 補足 |
|---|---|---|---|
| FC-R-01 右クリック作成で確認が出ず Keep 済み Data を Lit に戻す | **解消**（副作用 FX-R-09） | `Editor/Creation/SourceDataCreation.cs:147-174`（`BeginBatch` で選択全体を 1 回 `TryResolve`）・`:244-275`（キャンセル時は何も作らず `EndBatch`、作成ループは `try/finally` で `EndBatch`）、`Editor/Material/UnknownShaderGuard.cs:76-85`（`Ask` → `ConvertKeepingExisting`）、`Editor/Material/UnityMaterialMigrator.cs:136`・`:160-169` | `BeginBatch` が例外を投げたときは `EndBatch` が呼ばれないが、`BeginBatch` は先頭で `_materialHandling = null` にしているので状態は残らない。`ConvertKeepingExisting` を `== Keep` で見る経路（`MayaMaterialImporter.ResolveTargetShader:265`、`UnityMaterialMigrator:83`）では `Convert` と同じ扱いになり一貫している。switch で default を持つ箇所は無い。Editor の enum でスナップショットの対象外 |
| FC-R-02 欠けたシェーダーを「知らないシェーダー」として保つ | **一部 / 新たな問題あり**（FX-R-02） | `UnknownShaderGuard.cs:60-67`（`IsMissing` = 名前が `Hidden/InternalErrorShader`、`IsUnknown` から除外）、`UnityMaterialMigrator.cs:91-103` | 新規 Data は直った。既存 Data の側（欠けている間の再生成で既存の参照を Lit に置き換える）が残る。`IsMissing` は名前の完全一致なので正規のシェーダーを誤判定しない（コンパイルに失敗したシェーダーは自分の名前のまま返るので「知らないシェーダー」側に入る = 保てる） |
| FC-R-03 0 秒のマーカーが発火しない | **一部**（FX-R-01・FX-R-04） | `Runtime/Cutscene/CutsceneManager.cs:271-274`（`Elapsed > 0` のときだけ無音追い付き）、`:1020`（受信側の `elapsed`）、`Editor/Cutscene/CutsceneEditModePreviewProvider.cs:205-219` | ローカル: 最初の Tick・二重発火なし（カーソルは instance ごと・前進のみ）・再 Play（新しい instance）・プール再利用（カーソルは Director ではなく instance）・一時停止からの再開（停止中は Tick しないので再開後の最初の Tick で 1 回）・速度 0（最初の Tick で 0 秒だけ 1 回）・負の速度（`SetSpeed` が 0 に丸める）・`Seek(0)`（無音 = 規則どおり）・ループ（`CutsceneManager` は尺で `Complete` するので巻き戻らない）はいずれも問題なし。ネット受信側と Edit Mode の判定に残り |
| FC-R-04 `markerTrack` 上のマーカー | **解消（実在せず。根拠テストは妥当）** | `Tests/Runtime/ExternalContract/ExternalContractMarkerTests.cs`（`E20_MarkerOnTimelineMarkerTrack_Fires` は `GetOutputTracks()` に `markerTrack` が含まれることを `Assert.IsTrue(found)` で直接確かめ、Play で 0 秒 / 1 秒の 2 回の発火も見る）、`Tests/Runtime/CutsceneTimelineTracksTests.cs`（既存 Signal）、EditMode 版 | 含まれなければテストが赤になる形なので、「実在せず」の根拠として足りる |
| FC-R-05 `ApplyBindings` の共有バッファが再入で壊れる | **解消** | `CutsceneManager.cs:351-375`（使用中フラグ + 再入時だけ一時リスト、`finally` でフラグとリストを戻す）・`:380-433`、`Editor/Cutscene/CutsceneEditModeDirectorSetup.cs:214-288`（static 版も同形） | 例外時もフラグは下りる。外側は共有、内側は一時リストで、`ResolveSameAsTrack` は引数のリストだけを見るので結果は混ざらない。通常経路は割り当てなし |
| FC-R-06 タグ無しパス（`SRPDefaultUnlit`） | **解消** | `Runtime/Material/MaterialShaderInfo.cs:11-44`、`Tests/Editor/UntaggedPassRenderTests.cs`（実描画。GPU の無い環境は Inconclusive）、`Tests/Runtime/MaterialPassKeywordTests.cs` | テスト用シェーダーは `Hidden/` なので持ち込み先のシェーダー一覧を汚さない |
| PC-R-01 作業ツリーのチェックアウト・パス・子プロセス | **解消**（推定の残り FX-R-05） | `Editor/Update/GitArguments.cs:39-86`、`GitPackageJsonFetcher.cs:39-173`、`GitProcess.cs:140-319` | `--` は `clone` / `ls-remote` とも位置引数の直前。`git show HEAD:<path>/package.json` の `<rev>:<path>` の右側はパスとして扱われるので `^{}` 等の rev 記法・pathspec の魔法は効かず、リポジトリの外も指せない。`?path=` は `\`→`/`・UNC・`:`・`.`/`..`/空区間・`-` 始まり・制御文字（NUL を含む）を弾く。掃除は `_tempRoot` 直下の 10 分より古いフォルダだけで、作業フォルダは毎回新しい GUID。`ClearReadOnly` は再解析点に降りない |
| PC-R-02 プレリリースのタグを丸める | **解消**（P3: FX-R-07・FX-R-08） | `Editor/Update/GitTag.cs`、`GitTagListParser.cs:32-50`、`UpdateCheckLogic.cs:104-183`、`PackageAddPlanner.cs:99-128`、`UpdatePreflight.cs:42-63`、`UpdateWindow.cs:727-728`・`:990-1007` | SemVer の優先順位（数値識別子は桁数→辞書順で溢れない・数字 < 英字・短い方が小さい・正式版 > プレリリース）どおり。`+` のビルドメタデータ付きは版として読まない（旧 `SemVer.TryParse` も読めなかったので変化なし）。大文字 `V` は受け付ける。`facial-v0.2.0` のような接頭辞付きは読まない。D-Drive の行の P-14 は「`v` 無しタグも採る」「ハッシュ / ブランチは package.json の版で比較」が従来どおり。`#ref` に元のタグ名を書く |
| PC-R-03 同期 `git clone` で Editor が止まる | **解消**（P3: FX-R-05・FX-R-06） | `UpdateWindow.cs:151-229`（`StartBusy` / `PollBusy`）、`:1016-1055`（D-Drive の行は取得しない）、`GitCliTagLister.cs` | バックグラウンドから Unity API は呼んでいない（`Application.dataPath` は主スレッドで `DefaultTempRoot()`、`Debug.LogWarning` はスレッド安全）。二重起動は `_busyTask != null` で防ぐ。`EditorApplication.update` の解除は完了時・`OnDisable` で行う。版上げの続き（`onConfirmed`）は manifest を読み直してから書くので、待ちの間に行が変わっても `row.Id` に対して正しく書く |
| PC-R-04 重なる登録で二重に付く | **解消**（設計確認 FX-R-10） | `Runtime/Canvas/UiManager.cs:1402-1530`、`CanvasDataValidator.cs:101-122`、`Editor/Canvas/CanvasEmbeddedValidator.cs:91-145`（`FindOverlappingRegistration` は `:105`）、`CanvasEmbeddedEditing.cs:138-168`（`IsInsideOtherRoot` は `:146`）、`Tests/Runtime/EmbeddedCanvasTests.cs:368-480` | 1 要素（ボタン / スライダーは (要素, トリガー)）1 回。両方向（親が勝つ・重なる登録で内側が先・同じ子を 2 か所）を PlayMode テストが固定。ただし「内側が先」は「外側ほど強い」と逆転する組み合わせがある |
| PC-R-05 選択追従と遅延確定の欄 | **解消**（コード上。イベント順は目視 [43] 16-24） | `Editor/Canvas/CanvasEditorWindow.cs:446`（`FlushPendingInput`）・`:967-1019`（`BuildEmbeddedRow`）・`:797-866`（`BuildFxRow`）・`:1184-1294`（`BuildPhaseRow`） | 遅延確定の欄は `RootPath` の 1 つだけ（[54] が例に挙げた「Delay 欄」は存在しない）。`owner` を捕まえて `Undo.RecordObject(owner)` に書くので、Undo は正しい対象に積まれる。`owner != _target` のときは UI を作り直さない。ドメインリロード後は UI ごと作り直されるので古い `owner` は残らない。複数選択は Canvas Editor の対象が 1 つなので影響なし |
| PC-R-06 `SignalArgs.ElementPath` の意味 | **解消** | `UiManager.cs:22-50`（`SignalArgs` は `:29`。5 引数 ctor・4 引数は `EmbeddedRootPath = ""` に委譲）、`:1090-1110`・`:1161`・`:1269`・`:1860-1872` | 埋め込みなし（v1.3.1 の使い方）では `ElementPath` は従来どおり・`EmbeddedRootPath` は空文字。`default(SignalArgs).EmbeddedRootPath` だけは null（ctor を通らない）だが、D-Drive は常に ctor で作る |
| PC-R-07 `ddriveUpdate` の拡張規則 | **解消** | `PackageDependencyChecker.cs:15-21`・`:290`・`:360-364`（`IsDeclaredVersion`）、docs/42 §4.2.1「形式の拡張規則」、`Tests/Editor/Update/DdriveUpdateFormatCompatTests.cs` | 文書の 6 項目と実装が一致。`\d` は Unicode の数字にも当たるが、続く `Version.TryParse` が読めないので結局 BAD-DECLARATION（Warning）になり実害なし |

**見送りの妥当性**（v1.4.0 をこのまま出してよいか）: FC-R-10・12・13・16(a)・17・19・20・23、PC-R-12・13(後半)・15(a)・16(後半)・17(一部)・20 は、いずれも「追加のみ」で後から直せる・Editor の効率・目視でしか判断できない、のどれかで、v1.4.0 を止める理由にはならない。**FC-R-14 だけはタグ前に決める方が安い**（FX-R-14）。

---

## P2 — 直すべき不具合・設計上の穴

### FX-R-01. 【FC-R-03】ネットワーク受信側では、途中参加でなくても 0 秒のマーカーが鳴らない（発信側だけ鳴る非対称）

- **場所**: `Runtime/Cutscene/CutsceneManager.cs:1020`（`var elapsed = Math.Max(0d, _netBridge.NetworkTime - msg.StartNetTime);`）→ `:1046`（`PlayLocalInternal(..., elapsedSeek: elapsed, ...)`）→ `:271-274`（`if (instance.Elapsed > 0d) AdvanceMarkers(..., fire: false)`）。発信側は `:873`（`PredictLocal` で `elapsedSeek: 0d`）
- **何が問題か**: 受信側の開始位置は「発信からの経過時間」なので、通常の（遅れて入ったのではない）再生でもネットワーク遅延ぶん 0 より大きい。FC-R-03 の対処は `Elapsed > 0` を「途中から始まる再生」と見なすので、**受信側では 0 秒のマーカー（と遅延時間以内に置いたマーカー）が無音で飛ばされる**。発信側（`PredictLocal`）は `elapsedSeek = 0` なので鳴る。以前（v1.3.1）は 0 秒のマーカーはどちらでも鳴らなかったので、今回の変更で **Host / Client で結果が食い違う**ようになった（遅延時間以内のマーカーが受信側で鳴らないのは以前からの規則）。対応記録（[53] FC-R-03「ネットは各クライアントのローカル発火のまま（Host / Client とも同じ Play を各自の Tick で処理する）」）・E-20（「ネット遅延復元（Late Join = 開始位置 > 0）」）・docs/26 の追記・CHANGELOG（「途中から始まる再生（ネットワークの途中参加）」）は、受信がすべて「開始位置 > 0」になることを書いておらず、実装と食い違う。
- **失敗の筋書き**: 4 人対戦（A Host / B・C Client）で、A の操作で Cosmetic の `CutsceneData`（`PredictLocal` 有効）を再生 → 0 秒に置いた Event マーカー（SE）・外部の `FacialMarker`（表情の初期化）が A だけで鳴り、B・C では鳴らない。T-Drive が「ショット先頭で表情を初期化する」用途（[53] FC-R-03 の失敗の筋書きそのもの）は、ネット越しの端末では直っていない。
- **直し方の案**: (a) 受信側でも「最初から再生」と見なせる範囲を決める。例: `elapsed` が小さい（遅延の許容 = 例えば 0.5 秒、または `NetBridge` の RTT から決める）なら `[0, elapsed]` のマーカーを**最初の Tick でまとめて発火**（追い付き発火）、それより大きければ Late Join として無音。`CutscenePlayMsg` に欄は足さない（ネットメッセージの互換）。(b) 直さないなら、E-20・docs/26・CHANGELOG・[51] の T-Drive 宛てに「ネット受信側では、開始から遅延時間以内（0 秒を含む）のマーカーは鳴らない。0 秒での初期化はマーカーではなく `OnModelSpawned` / Play 直後のコードで行う」と明記する。どちらにしても PlayMode テストを 1 件（`OnReceivePlayMsg` 相当で `StartNetTime` を少し過去にした受信 → 0 秒のマーカーの発火回数）。
- **確度**: 確認済み（コード読み。受信の `elapsed` の式と `Elapsed > 0` の分岐から一意に決まる）

### FX-R-02. 【FC-R-02】シェーダーが一時的に欠けている間に既存の MaterialData を再生成すると、既存 Data の Shader 参照が DDrive/Lit に置き換わる（「保つ」を選んでも）

- **場所**: `Editor/Material/UnityMaterialMigrator.cs:77-103`（元の `.mat` のシェーダーが `IsMissing` → `target = Lit`）、`:136`（`data.Shader != target && !(keepSource && data.Shader != null) && !KeepsExistingUnknownShader(data.Shader, …)`）、`:160-169`（`KeepsExistingUnknownShader` は `IsUnknown(existing)` が偽なら false）。呼び出し元: `SourceDataCreation`（右クリック作成）・`Generate` メニュー・`ModelSlotBinder.EnsureMaterialData`（Model エディタの「元ファイルを再読み込み」で単体の `.mat` を通す、`Editor/Model/ModelSlotBinder.cs:294`）
- **何が問題か**: T-Drive のパッケージが一時的に解決できない（git の認証切れ・ブランチ切り替え直後・新しい端末での初回）と、Toon の `.mat` も、既存の MaterialData が参照している Toon シェーダーも欠けた状態になる。このとき (1) 元の `.mat` は `Hidden/InternalErrorShader` → `IsMissing` → 変換先は Lit、(2) 既存 Data の `Shader` は欠けた参照なので Unity の `==` では null（**推定**: 欠けたオブジェクト参照は Unity の null 比較で null になる一般的な挙動）→ `keepSource && data.Shader != null` も `KeepsExistingUnknownShader(null)` も偽 → **既存 Data の `Shader` を Lit に書き換え、`Specific` も Lit 側へ合成する**。シリアライズされた参照（GUID）がこの時点で上書きされるので、T-Drive が戻っても Toon には戻らない。`KeepSource` の Profile でも、ダイアログで「保つ」を選んでも同じ（`IsMissing` の Material は保つ対象から外れたため）。ラウンド 1 の前も同じ経路で参照を失っていた（`KeepSource` なら `Hidden/InternalErrorShader` で上書き）ので新たな退行ではないが、FC-R-02 の対処は既存 Data の側を守っていない。なお、ダイアログは「知らないシェーダー」が 1 件も無いと出ない（`UnknownShaderGuard.cs:150`）ので、欠けた Material だけを選んだ操作では「欠けている N 件」の案内も出ない。
- **失敗の筋書き**: MS2026 で T-Drive の Toon を使った MaterialData がある。別のメンバーが T-Drive のリポジトリへのアクセス権が無い状態でプロジェクトを開き（UPM はエラーを出すがプロジェクトは開く）、Model エディタで「元ファイルを再読み込み」→ その Model が使う Toon の `.mat` の MaterialData が Lit に書き換わり、コミットされる。または、[52] 15.6 の手順 3（「パッケージを外した Toon など」の `.mat` で右クリック作成）を、手順 1 で作った実データに対してそのまま行う → 期待結果「そのデータは `DDrive/Lit` になる」のとおり既存 Data が Lit になる（手順書が既存データの破壊を期待結果にしている）。
- **直し方の案**: (a) `MigrateCore` で、元の `.mat` が `IsMissing` で既存 Data があるときは `Shader` / `Specific` に触れず、警告「シェーダーが見つからないため、既存の MaterialData のシェーダーは変更しませんでした」だけにする（新規作成だけ Lit）。(b) 既存 Data の `Shader` が「欠けた参照」（`SerializedObject` の `objectReferenceInstanceIDValue != 0` なのに値が null、または `!ReferenceEquals(data.Shader, null) && data.Shader == null`）のときも同様に保つ。`MayaMaterialImporter` の既存 Data 経路（`MayaMaterialImporter.cs:202` `if (existing.Shader == null)`）も同じ判定にする（こちらは FC-15 以前からの経路で、Common に差分があるときだけ書く）。(c) テスト: 「Toon で作った既存 Data → シェーダーを欠けさせた `.mat` で `Migrate`（Ask / KeepSource / 明示 Keep）→ 既存 Data の `Shader` 参照が変わらない」。(d) [52] 15.6 の手順 3 を「新規の `.mat`（既存 Data が無いもの）で」に直す。BuildPrompt が欠けた Material だけのときにも案内を出すか（ダイアログを出すか、Console の警告で足りるか）を決める。
- **確度**: コードの分岐は確認済み。欠けた参照が Unity の `==` で null になる点は**推定**

---

## P3 — 整理・改善

### FX-R-03. 【FC-R-09】Signal マーカーの購読者（ゲームのコード）の中で止めた場合・別のカットシーンを止めた場合が残る

- **場所**: `Runtime/Cutscene/CutsceneManager.cs:694-707`（`AdvanceSignalMarkers` は `MarkerSubject.OnNext` = `CutsceneHandle.OnMarker` の購読者を同期で呼ぶ。止められても残りを続ける）、`:662-669`（`AdvanceMarkers` は Event → Signal → Shake → Haptic → 外部の順に全部回る）、`:1445-1485`（`Tick` は `_active` を後ろから添字で回す）、`:1607`（`Cleanup` が `_active.Remove(handle)`）
- (a) 対応記録の「既存 4 種は発火の中でカットシーンを止める経路が無い（外部コードを呼ばない）」は誤りで、Signal マーカーは `OnMarker` の購読者を呼ぶ。購読者が `Cutscene.Cancel` / `Stop` すると `Cleanup` が `MarkerSubject` を `Dispose` した後も、同じ Tick で跨いだ残りの Signal（破棄済みの Subject への `OnNext`。R3 が例外にするかは**推定**で未確認）・Shake（カメラが揺れる）・Haptic・外部マーカーが呼ばれる。(b) **外部マーカーの `Fire` / Signal の購読者が「別の」カットシーンを止める**と、`_active` から添字の小さい要素が消えて、いま処理中の instance が 1 つ前の添字にずれ、`for (i--)` の次の周回で**同じ Tick に 2 回進む**（`Elapsed += dt` が 2 回、マーカーとイベントも 2 回分）。FC-4 で外部コードを呼ぶ口が増えたので起きやすくなった。直し方: `AdvanceMarkers` の 5 つの間とループの中で `_instances.IsValidSilent` を見る。`Tick` は `_active` の数の変化を見て添字を補正する（または開始時の写しを回す。割り当てを避けるなら再利用リスト）。テストを 2 件（Signal の購読者の中で Cancel / 外部マーカーの中で別のカットシーンを Stop）。確度: (a)(b) の経路はコード読みで確認済み、R3 の破棄後 `OnNext` の挙動は推定

### FX-R-04. 【FC-R-03】Edit Mode の「先頭からの再生」判定が更新の間隔に依存する

- **場所**: `Editor/Cutscene/CutsceneEditModePreviewProvider.cs:148-150`（`dt` = 前回の `EditorApplication.update` からの時間）、`:205`（`elapsed <= Math.Min(dt, 0.1) + 1e-4` なら「先頭から」）
- 判定が「再生開始後の最初の更新で `director.time` がこの監視役の `dt` 以内しか進んでいないか」なので、(a) Timeline ウィンドウ側の 1 回目の進みが監視役の `dt` より大きいと、先頭から再生しても 0 秒のマーカーが無音になる、(b) スクラブで 0.1 秒以内の位置に置いてから再生すると、その位置までのマーカーが発火する（「途中からは無音」の規則に反する）。どちらになるかは更新の順序と間隔次第（**推定**）。自動テスト（`ExternalContractMarkerEditModeTests.cs:154`）は `director.time = 0` ちょうどで呼ぶので、この判定の幅は検証されていない。直し方: 再生していなかった直前の更新の `session.LastTime`（停止中の再生位置）が 0（≦ 1e-4）なら「先頭から」とする（間隔に依存しない）。確度: 推定

### FX-R-05. 【PC-R-01 / 03】`GitProcess` の終了後の `WaitForExit()` が無期限・`pkill -P` は直下の子だけ

- **場所**: `Editor/Update/GitProcess.cs:233`（ループを抜けた後の `process.WaitForExit()`。タイムアウトなし）、`:291-296`（Windows 以外は `pkill -KILL -P <pid>`）
- (a) .NET の `WaitForExit()`（引数なし）は、リダイレクトした標準出力 / エラーが EOF になるまで待つ。git の子孫プロセスがパイプを継承したまま残る場合（ssh の `ControlPersist` のマスター、認証ヘルパーの常駐等）、git 本体が終わってもここで止まり、`_busyTask` が完了せず、ウィンドウが「確認中…」のまま「キャンセル」も効かない（キャンセルの確認はループの中だけ）。(b) `pkill -P` は直下の子だけを止め、孫（`git-remote-https` → `fetch-pack` / `index-pack` 等）は残りうる。直し方: (a) `WaitForExit(5000)` にして、戻らなければ読み取り途中のまま結果を返す。(b) Windows 以外は子を再帰的に集めて止める（`pgrep -P` を繰り返す）か、残っても一時フォルダは次回 `CleanupStale` が消すことを docs/42 に書く。確度: 推定（.NET / Mono の `WaitForExit()` の仕様と ssh の挙動から）

### FX-R-06. 【PC-R-03】ドメインリロード中の後始末と `_busyTask` の残り

- **場所**: `Editor/Update/UpdateWindow.cs:151-156`（`OnDisable` は `Cancel` するだけで終わりを待たず、`_busyTask` も null に戻さない）
- (a) スクリプトの再コンパイル（または Play Mode 突入時のドメインリロード）で `OnDisable` → `Cancel` の直後にドメインが破棄されると、`GitProcess` のループ（100 ms ごとに確認）が `KillTree` に届く前にワーカースレッドが止まり、git が孤児として残る（タイムアウトの監視も消える。到達できないホストへの接続なら長く残る）。(b) ドメインリロードを伴わない `OnDisable` → `OnEnable` が起きた場合、`_busyTask` が非 null のまま `PollBusy` の購読が外れているので、以後のボタンが「別の確認が進行中です」で動かない（ウィンドウを閉じ直すまで）。Unity がドメインリロードなしに EditorWindow の `OnDisable` を呼ぶ場面があるかは**推定**。直し方: `OnDisable`（と `AssemblyReloadEvents.beforeAssemblyReload`）で `Cancel` の後に `_busyTask.Wait(1000)` 程度だけ待ち（`GitProcess` は 100 ms で気づく）、`_busyTask` / `_busyContinuation` を null にする。確度: 推定（Mono のドメイン破棄時のスレッドの扱い）

### FX-R-07. 【PC-R-02】正式版が無くプレリリースだけのとき、「更新先の版」の既定がプレリリースになり 1 クリックで書ける

- **場所**: `Editor/Update/UpdateWindow.cs:1005`（`dropdown.value = check.LatestTag ?? choices[0];`）・`:1006-1007`（更新ボタンを出す）
- 「自動では勧めない」と表示しつつ、既定の選択値が最新のプレリリースで、そのまま「manifest を選んだ版に更新する」を押せる。URL 入力の新規導入（`PackageAddPlanner`）は「`#<タグ名>` を付けて入力」で止めているのと揃っていない。直し方: `LatestTag == null` のときは既定を空（または「選んでください」）にしてボタンを無効にする、または確認ダイアログの見出しに「（プレリリース）」を出す。確度: 確認済み

### FX-R-08. 【PC-R-02】`GitTag.TryParse` は 2 区間・4 区間の版（`v1.5`・`v1.5.0.1`）も受け付ける（コメントと食い違う）

- **場所**: `Editor/Update/GitTag.cs:30-31`（コメント「`v1.5` は false」）・`:55`（`Version.TryParse(core, …)` は 2〜4 区間を受け付ける）
- `v1.5` は `Version(1,5)`（Build = -1 で `v1.5.0` より小さい）、`v1.5.0.1` は `v1.5.0` より新しい PATCH として「最新」に勧められる。旧 `SemVer.TryParse` も同じだったので P-14 からの変化ではないが、`vX.Y.Z` 形式だけを扱う、という [42] §4.2.1 の記述と揃えるなら 3 区間だけにする（または docs とコメントを実装に合わせる）。テスト（`PrereleaseTagTests` の拒否表）に `v1.5` / `v1.5.0.1` を足す。確度: 確認済み

### FX-R-09. 【FC-R-01】`KeepsExistingUnknownShader` は、元の `.mat` を標準シェーダーに戻したときにも効く

- **場所**: `Editor/Material/UnityMaterialMigrator.cs:136`・`:160-169`
- 判定は「既存 Data のシェーダーが知らないシェーダーか」だけで、元の `.mat` のシェーダーを見ない。デザイナーが `.mat` を Toon から URP Lit に戻して右クリック作成をやり直すと、ダイアログは出ず（知らないシェーダーが無い）、`Ask` の既定 `ConvertKeepingExisting` で既存 Data は Toon のまま変わらない（ログも出ない）。直し方: 「既存を保つ」のは元の `.mat` も知らないシェーダー（または欠けている = FX-R-02）のときだけにする。CHANGELOG の FC-R-01 の項の「非対話 + `Ask`」は、対話的な操作でもダイアログが出なかった場合に同じ扱いになることを含めて書く。確度: 確認済み

### FX-R-10. 【PC-R-04 設計】重なる登録の「内側が先」は、「外側ほど強い」と逆向きになる組み合わせがある

- **場所**: `Runtime/Canvas/UiManager.cs:1476-1489`（段の中は `RootPath` の深い順）、`Tests/Runtime/EmbeddedCanvasTests.cs:432`（`OverlappingEmbedRegistrations_ApplyEachElementFxOnlyOnce`）、docs/07 優先順位
- Hud が `OptionRoot`（Option）と `OptionRoot/Inner`（Volume）を重ねて登録すると、Option 自身の行 `Inner/Deep` と Volume の行 `Deep`（同じ要素）では **Volume が勝つ**。一方、正しい形（Option 自身が `Inner` に Volume を埋め込む）では外側の Option が勝つ。つまり、冗長な登録を 1 本足すと Option と Volume の優先が入れ替わる。上記テストはこの「入れ替わり」を固定している。docs/07 には書かれており Validator も Warning（`DD-CANVAS-EMBED-NESTED-ROOT`）を出すので実害は小さいが、v1.4.0 で意味が固定されるので、意図した規則かをタグ前に確認したい（「外側ほど強い」に揃えるなら、同じ親の重なる登録は浅い方を先にし、二重適用は担当表で防げる）。確度: 確認済み（コード読み）

### FX-R-11. 【互換・CHANGELOG】D-Drive 自身の更新チェックで認証プロンプトが出なくなった（記載漏れ）

- **場所**: `Editor/Update/GitProcess.cs:163-165`（`GIT_TERMINAL_PROMPT=0` / `GCM_INTERACTIVE=never` / `GIT_LFS_SKIP_SMUDGE=1`・標準入力を閉じる）、`GitCliTagLister.cs`（v1.3.1 の P-14 は環境変数なしで `git ls-remote` を起動していた）
- v1.3.1 では、D-Drive の「最新の版を確認」で資格情報が切れていると Git Credential Manager のログイン画面が出て、入力すれば続けられた。v1.4.0 では出ずに「git ls-remote: …Authentication failed」等の警告で終わる（認証ヘルパーのキャッシュが有効なら従来どおり動く）。docs/42 §4.2.1 には書かれているが、CHANGELOG の「互換性」節（PC-R-01・03・21 の項）は「Editor を止めずに実行」としか書いておらず、MS2026 で「更新チェックが急に失敗するようになった」と受け取られうる。項に 1 文（「認証の対話プロンプトは出さない。失敗したら一度 `git fetch` 等で資格情報を更新してから押し直す」）を足す。確度: 確認済み（コード読み。GCM の画面が出なくなることは環境変数の仕様からの推定）

### FX-R-12. 【docs】CHANGELOG・対応記録の古い記述・食い違い

- CHANGELOG FC-15 の項「FBX の自動取り込み・バッチモード・テスト等の非対話の経路は従来どおり `DDrive/Lit` に変換する（結果は不変）」→ `ConvertKeepingExisting` で、`.mat` の非対話経路（バッチモードの `Generate`・非対話の `EnsureMaterialData`）は既存 Data の知らないシェーダーを保つようになった。FC-R-01 の項と食い違う
- CHANGELOG FC-11 の項が `MaterialData.HasPassesOrKeywords` と `MaterialShaderInfo.ContainsIgnoreCase` を「公開 API に追加」と書いたまま（FC-R-24 で internal / private にした）
- CHANGELOG P-15 の項が「上げ先の package.json を `git` の浅い sparse clone で取得」のまま（PC-R-01 で `--no-checkout` + `git show` に変更）。`[Unreleased]` の中で後の項が前の項を打ち消しており、MS2026 から見た v1.3.1 → v1.4.0 の差分として読みにくい。U-28（未リリース）の確定仕様の項の「以前は配線も要素単位」「以前は親ルート基準」も、v1.3.1 には無い状態との比較
- [53] FC-R-09 の対応記録「既存 4 種は外部コードを呼ばない」（FX-R-03）、FC-R-03 の対応記録「Host / Client とも同じ」（FX-R-01）
- `GitTag.cs:30-31` のコメント（FX-R-08）
- 直し方: タグ前に `[Unreleased]` を「v1.3.1 から見た最終形」に整理する（未リリースの中間状態の打ち消しを畳む）。確度: 確認済み

### FX-R-13. 【テスト】修正に対するテストの抜け

- FX-R-01: ネット受信（`StartNetTime` が過去）での 0 秒のマーカーのテストが無い（`E20_LateJoin_*` は 1.0 秒からの明確な途中参加だけ）
- FX-R-02: 既存 Data + シェーダーが欠けた `.mat` のテストが無い（`MissingShader_IsNotUnknown_AndIsNeverKept` は新規 Data だけ）
- FC-R-01: `ContextMenuMaterialCreation_AsksOncePerOperation_AndCancelCreatesNothing` は `option.BeginBatch` の戻り値と確認回数だけを見ており、`CreateFromSelection` の流れ（キャンセルで本当に何も作らない・「保つ」で既存 Data が変わらない・`EndBatch` が必ず呼ばれる）は通していない（Selection を差し替える必要があるため。`CreateFromSelection` に対象パスを渡す内部の入口を作れば試験できる）
- FX-R-04: Edit Mode の判定の幅（`director.time` が 0 より少し進んでいる / 0.05 にスクラブしてから再生）が無い
- FX-R-03: Signal の購読者の中で Cancel / 別のカットシーンを Stop するテストが無い
- `EmbeddedCanvasPathsTests` のリフレクションは型名・メソッド名の改名で `GetType` が null → `NullReferenceException` になる（`RuntimeAndEditorCopies_ExistAndAreNotPublic` が先に存在を確かめるので理由は分かる）。表で当てる方式は妥当
- 修正前に失敗し修正後に通るか: `Markers_AtZero_FireOnFirstTick_WhenPlayedFromStart`・`E20_MarkerAtZero_FiresOnFirstTick_*`・`Migrate_AskNonInteractive_DoesNotOverwriteExistingKeptUnknownShader`・`MissingShader_IsNotUnknown_AndIsNeverKept`・`OverlappingEmbedRegistrations_*`・`ParentButtonWire_OnlyWinsForTheSameTrigger_*`・`ChildButtonWire_SendSignal_ElementPathIsChildRooted_*`・`PrereleaseTagTests` の主要ケースは、いずれも修正前のコードでは失敗する内容（読んだ範囲）。`UntaggedPassRenderTests` は GPU の無い環境で Inconclusive（CI では実質検証されない）。実 git・実ネットワーク・実 manifest に触れるテストは無い（`ManagedPackageRowsTests` の実設定ファイルは [54] PC-R-17 のとおり `TearDown` 付きで残る）。static の差し替え口（`PromptOverrideForTests` / `ProfileOverrideForTests` / `SourceDataCreation._materialHandling`）はテストの `TearDown` / `finally` で戻る
- 確度: 確認済み

### FX-R-14. 【FC-R-14 の見送り】リスナーの発見規則（internal 型も拾う）はタグ前に決める方が安い

- **場所**: `Editor/Cutscene/ICutsceneImportListener.cs`（`Discover` が `IsPublic` を見ない）、docs/42 §5.14 E-19（「public な `ICutsceneImportListener`」）
- 見送りの理由「発見規則を変えると外部の既存実装の挙動が変わりうる」は、v1.4.0 が未リリースで外部実装がまだ無い今は当たらない。タグ後に「public だけ」に絞ると、internal で書かれた外部リスナーが黙って呼ばれなくなる（E-19 の文面どおりにするための変更が、実質的な挙動の変更になる）。タグ前に `ExtensionPointDiscovery` に寄せる（public だけ）か、E-19 の文面を「internal 型も含む」に直すかのどちらかに決める。キャッシュ（毎回発見し直す件）は後回しでよい。確度: 確認済み

---

## 確認して問題なしだった観点

**FC（ラウンド 1）**

- **0 秒のマーカーのローカル再生**: 二重発火なし（カーソルは instance ごとの前進のみ。Director のプール再利用・再 Play は新しい instance）・`Play` の呼び出し自体では発火しない（最初の `Tick`）・一時停止中は Tick しない・速度 0 / 負（0 に丸め）・`Seek(0)` は無音・Skip は無音・`Elapsed >= Duration` の即完了経路でも追い付きの有無は結果に影響しない
- **再入耐性（FC-R-05）**: フラグは `finally` で下りる・内側は一時リスト・`ResolveSameAsTrack` はリストを引数で受ける・通常経路は割り当てなし。Editor 側の static 版も同形
- **FC-R-09（外部マーカー）**: `Fire` の後の `IsValidSilent` で残りを止め、`Tick` も `AdvanceMarkers` の後に有効性を見て `_events.Tick` / `Complete` に進まない
- **`IsMissing` の判定**: 名前の完全一致（`Hidden/InternalErrorShader`）で、正規のシェーダーを誤判定しない
- **`ConvertKeepingExisting` の一貫性**: `== Keep` だけを見る経路では `Convert` と同じ。`HandlingFor` の default は未知の将来値も `ConvertKeepingExisting`（安全側）。Editor の enum でスナップショット対象外・switch の default への影響なし
- **`BeginBatch` / `EndBatch`**: キャンセル時・作成中の例外時とも `EndBatch` が呼ばれる。`BeginBatch` の中の例外でも先頭で状態を消しているので残らない
- **FC-R-06**: タグ無しのパスを `SRPDefaultUnlit` として一覧に入れるだけで、`MaterialManager` の適用は不変。Warning は減る方向のみ
- **公開面の整理（FC-R-24）**: v1.3.1 のスナップショット行は 1 行も消えていない

**P-15 / U-28（ラウンド 2）**

- **`--` の位置と引数**: `clone … --branch <ref> -- <url> <dir>`・`ls-remote --tags -- <url>`。`ArgumentList` で 1 個ずつ渡し、引用符の組み立てが無い。URL・タグは `-` 始まり・制御文字を弾く
- **`git show HEAD:<path>`**: rev はユーザー入力を使わない（clone が取ったタグのコミット）。パス連結は `/` のみ。`?path=` のバックスラッシュ・末尾の `/`・NUL・`..`・ドライブ名・UNC は弾く。末尾ドットや紛らわしい Unicode はリポジトリのツリー内の名前として扱われるだけで外には出ない（見つからなければ「package.json が見つかりませんでした」の警告）
- **環境変数・標準入力**: `GIT_TERMINAL_PROMPT=0`・`GCM_INTERACTIVE=never`・`GIT_LFS_SKIP_SMUDGE=1`・標準入力を閉じる（認証ヘルパーのキャッシュ・SSH 鍵は使える）。副作用は FX-R-11 の記載漏れだけ
- **タイムアウトと Kill の競合**: `HasExited` を見てから止める・`taskkill` が無い環境は `Kill()` へ・すべて例外を握る。PID の渡し方は `/PID <pid>` / `-P <pid>` で、他のプロセスを名前で巻き込まない
- **`CleanupStale`**: `Temp/DDriveUpdate` 直下の 10 分より古いフォルダだけ。進行中の取得（最大 30 秒）のフォルダは消さない。プロジェクトは同時に 1 つの Editor しか開けないので別ウィンドウの作業フォルダと衝突しない
- **バックグラウンド化**: ワーカーから Unity API を呼ばない・二重起動防止・`EditorApplication.update` は完了時と `OnDisable` で解除・結果の反映は主スレッド・版上げの書き込みは manifest を読み直す・`Client.Add` 中は版上げを始めない
- **プレリリース / P-14 の回帰**: D-Drive の行は `v` 無しタグも採る・ハッシュ / ブランチは package.json の版で比べる・`+` メタデータ付きは従来どおり読まない・`#ref` は元のタグ名
- **`ddriveUpdate` の規則**: 文書の 6 項目と実装・テストが一致
- **Canvas の担当表**: Open した CanvasData の行は適用できたかに関わらず種にする（親が常に勝つ）・幅優先で浅い段から・(要素, トリガー) のキーは `(string, int)` の値タプルで文字列は Ordinal・循環（祖先の連なり）・深さ 8・同じ子を 2 か所（キーが違うので両方動く）。埋め込みがあるときだけ担当表・順序配列を割り当てる（Open 経路）
- **v1.3.1 の埋め込みなしの挙動**: `EmbeddedCanvases` が空なら `SetupEmbeddedCanvases` は最初に戻り、配線は `claims == null`・`embedRoot == null` で従来の経路。`SendSignal` の `ElementPath` は従来どおり、`EmbeddedRootPath` は空文字。既存の 4 引数 `SignalArgs` ctor と `SendSignal` の署名は不変
- **新規 Validator 3 種**: `-NESTED-ROOT` は一方が他方の配下（同じパスは `-DUP` 側）・`-PATH-FORM` は書式だけ・`-NOT-PRELOAD` は子の `Flags.Load != Preload`（既定の `LazyLoad` では必ず出るが、実行時に同期解決できないのは事実なので誤検出ではない）。いずれも Warning で、埋め込みを使うときだけ
- **`ConditionalWeakTable` のキャッシュ**: 1 つの `ValidationContext` につき 1 回。ctx が回収されれば消える
- **PC-R-05**: `owner` を捕まえて `Undo.RecordObject(owner)`・`owner == _target` のときだけ UI を作り直す・切り替え直前に `Blur()` で確定（確定のコールバックは `owner` に書くので、`Blur` で `RefreshAfterEmbeddedEdit` が先に走っても書き先は正しい）
- **マニュアル**: 本ラウンドで変わった DesignerManual / ProgrammerManual / consumer guide に「以前」「従来」「これまで」等の比較の記述は無い（差分の追加行を検索）
- **互換スナップショット**: `git diff 9f40cbb..64c0301 -- …/Snapshots/` は `+120 / -0`

---

## v1.3.1 → 現在（`64c0301`）の「挙動の変更」一覧と CHANGELOG との照合

本ラウンド 2 本で変わったもの、および本ラウンドで v1.3.1 から見た意味が変わったものに限る（FC / P-15 / U-28 の本体の追加は [53] / [54] で照合済み）。

| # | v1.3.1 から見た挙動の変更 | 区分（本レビューの見立て） | CHANGELOG `[Unreleased]` 互換性節 |
|---|---|---|---|
| 1 | ローカルの `Play` で、0 秒ちょうどのマーカー（Event / Signal / Shake / Haptic / 外部）が最初の Tick で 1 回発火する | MINOR（既存データの結果が変わる） | **あり**（MINOR）。ただし「途中参加」の説明が受信全般に当たることが**漏れ**（FX-R-01） |
| 2 | ネットワーク受信側では 0 秒のマーカーは引き続き鳴らず、発信側（予測再生）だけ鳴る | MINOR の一部（非対称が新たに生じる） | **なし**（FX-R-01） |
| 3 | Edit Mode のプレビューで、先頭（0.1 秒以内）からの再生では 0 秒のマーカーが鳴る | Editor | あり（「先頭からの再生では 0 秒も発火」。判定の幅は書かれていない = FX-R-04） |
| 4 | 外部マーカーの `Fire` の中で止めたら同じ Tick の残りを呼ばない。`Tick` も止まった instance に `_events.Tick` / `Complete` を進めない（Signal 購読者の中で止めた場合の `_events.Tick` も含む） | PATCH（不具合修正） | あり（外部マーカーのみの記述。既存 Signal 購読者に効く部分は書かれていない。軽微） |
| 5 | 右クリックの「Material を作成」で、`Ask` なら知らないシェーダーの確認ダイアログが 1 操作 1 回出る | PATCH 相当（Editor） | あり |
| 6 | `.mat` の非対話経路・ダイアログが出なかった対話経路で、既存 MaterialData の知らないシェーダーを Lit に戻さない（元の `.mat` が標準シェーダーに戻っていても） | PATCH 相当（Editor） | あり（ただし FC-15 の項の「結果は不変」と矛盾 = FX-R-12、元の `.mat` 側の条件が無いこと = FX-R-09） |
| 7 | シェーダーが欠けた `.mat` は `KeepSource` / 「保つ」でも Lit に変換。**既存 Data の参照も Lit に置き換わる** | PATCH 相当（Editor） | あり（新規の側のみ。既存 Data の置き換えは**なし** = FX-R-02） |
| 8 | Material Editor の候補・Validator がタグ無しのパスを `SRPDefaultUnlit` として扱う | PATCH（Warning が減る方向） | あり |
| 9 | D-Drive 自身の更新チェック: 元のタグ名を `#ref` に書く・プレリリースは自動で勧めない・`v` 無しタグはそのままの名前 | PATCH 相当（Editor） | あり |
| 10 | D-Drive 自身の更新チェック: git をバックグラウンドで実行し、進捗とキャンセルを出す | Editor | あり |
| 11 | D-Drive 自身の更新チェック: **認証の対話プロンプト（GCM のログイン画面・端末のパスワード入力）を出さない** | Editor（体感が変わる） | **なし**（FX-R-11） |
| 12 | D-Drive の版上げ / 元に戻すの事前確認は上げ先の `package.json` を取得しない（他パッケージの宣言との照合はタグの版で行う） | Editor | あり |
| 13 | `ddriveUpdate` の値の検査を厳密化（`"1.4.0 - 1.x"` 等を BAD-DECLARATION） | 追加のみ（P-15 自体が未リリース） | あり |
| 14 | U-28 の確定仕様（担当表・(要素, トリガー)・`ElementPath` は子ルート基準・`EmbeddedRootPath`）と Validator の新規 Warning 3 種 | 追加のみ（U-28 自体が未リリース。v1.3.1 の使い方は不変） | あり（「以前は」は未リリースの中間状態との比較 = FX-R-12） |
| 15 | `EmbeddedCanvasPaths` の internal 化・テスト用差し替え口の改名・`HasPassesOrKeywords` 等の internal 化 | 変更なし（v1.3.1 に無い） | あり |

MS2026 が踏みそうなもの: **#1・#2**（0 秒に Event マーカーを置いていたカットシーンがあれば、発信側の端末だけ鳴り始める）、**#11**（更新チェックの失敗の仕方が変わる）、**#7**（T-Drive の導入順や解決失敗の時期に MaterialData を再生成した場合）。

---

## v1.4.0 のタグ前にやるべきことの順序

1. **FX-R-01**（ネット受信側の 0 秒のマーカー）: 直すか、規則として書くかを決める（E-20 で契約になるので、タグ後に変えると挙動の変更）。決めた内容で E-20・docs/26・CHANGELOG・[51] の T-Drive 宛て・[53] の対応記録を直し、PlayMode テストを 1 件
2. **FX-R-02**（欠けたシェーダーで既存 Data の参照を置き換える）を直し、テストを足す。**[52] 15.6 の手順 3 を、直すまでは実データで行わない**（新規の `.mat` で行う）
3. タグ前に意味を確定するもの: **FX-R-10**（重なる登録の優先の向き）、**FX-R-14**（リスナーの発見規則）、FX-R-07（プレリリースだけのときの既定選択）
4. P3 のうち小さく安全なもの: FX-R-03（`AdvanceMarkers` の途中の有効性確認・`Tick` の添字補正）、FX-R-05(a)（`WaitForExit` に上限）、FX-R-06（`OnDisable` で短く待って `_busyTask` を戻す）、FX-R-04（`LastTime` で判定）、FX-R-08・09
5. **CHANGELOG の整理**（FX-R-11・12）: `[Unreleased]` を「v1.3.1 から見た最終形」に畳み、認証プロンプトの件・受信側の 0 秒・欠けたシェーダーの既存 Data の扱いを書く
6. EditMode / PlayMode の両方を green にする
7. **人による確認で特に見るべき点**:
   - [52] 4-2（0 秒のマーカー）を **Play Mode の 2 台構成（Host / Client）でも**見る（Client 側で鳴るか。FX-R-01 を直した場合はその確認、直さない場合は鳴らないことの確認）
   - [52] 4-2 の Edit Mode で、再生位置を 0 に戻して再生を数回繰り返し、毎回 1 回鳴るか（FX-R-04）。0.05 秒付近にスクラブしてから再生して鳴らないか
   - [52] 15.6 の手順 3 は**既存 Data の無い `.mat`** で（FX-R-02）
   - [43] 15-16 / 15-17（進捗・キャンセル）に加えて、確認中にスクリプトを保存して再コンパイルを起こし、`git` のプロセスが残らないか・ウィンドウを開き直して操作できるか（FX-R-06）
   - [43] 15-16 で、資格情報が切れている private リポジトリのとき、GCM の画面が出ずに警告で終わり、`git fetch` で資格情報を更新すると成功するか（FX-R-11）
   - [43] 16-19（重なる埋め込み）で、Option 自身の行と Volume の行が同じ要素を指すとき、どちらが効くか（FX-R-10 の意図どおりか）
   - [43] 16-24（入力途中の切り替え）
8. 残りの P3（FX-R-05(b)・FX-R-13 のテストの抜け）と、[53] / [54] で見送った P3 は v1.4.x / 次の MINOR で可

---

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・実 `git`・実描画・UI の見た目・UI Toolkit のフォーカスのイベント順）。特に FX-R-02 の欠けた参照の null 比較、FX-R-03 の R3 の破棄後 `OnNext`、FX-R-04 の Timeline ウィンドウと監視役の更新順、FX-R-05 / 06 の .NET・Mono のプロセス / スレッドの挙動は推定のまま
- `UpdateWindow` の差分（約 500 行）のうち、パッケージ一覧の表示（`RefreshPackagesSection`）・登録解除の確認・CHANGELOG 表示・「3. 更新後の確認」節は流し読み。`ResumePendingAdd` の再開経路は前回 [54] の確認から変わっていないことだけ確認
- `CanvasEditorWindow` は PC-R-05 に関わる差分のみ。`CanvasEmbeddedValidator.FindOverlap` の再帰は読んだが、実データでの誤検出の有無は未確認
- `DdriveUpdateFormatCompatTests`・`GitArgumentsTests`・`PrereleaseTagTests` は主要ケースの名前と代表的な表のみ。`CutsceneSameAsTrackTests` の再入テスト・`ExternalContractModelTests` の FC-R-08 テストは名前と構成のみ
- SpecWeb の再生成物（`Tools/SpecWeb/html/manual/*`）は対象外（正本の `docs/DesignerManual` / `docs/ProgrammerManual` の差分の文言検索のみ）
- T-Drive 側のコード・実際の `package.json` は無く、相性は docs のコピーと [51] で判断した
