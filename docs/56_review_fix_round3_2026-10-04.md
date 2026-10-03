# 56. 2026-10-04 自前レビュー結果（修正ラウンド 3 = docs/55 の指摘 FX-R-01〜14 への対応）

> **対象**: 2026-10-04 に main へ入った修正ラウンド 3。Sonnet のサブエージェントが実装し、まとめ役は差分を読まずにマージした。
>
> | マージ | PR | 内容 |
> |---|---|---|
> | `84269f4` | #105 | 修正ラウンド 3（[55](55_review_fix_rounds_2026-10-03.md) の対応）: FX-R-01（受信側の追いつき発火 `RemoteFreshStartGraceSec`）・FX-R-03（`Tick` / `StopAll` / `CancelAllNetworked` の写し走査・マーカー段ごとの有効性確認）・FX-R-02 / 09（欠けたシェーダーで既存 Data を保つ・`HasMissingShaderReference`・元の `.mat` が知らないシェーダーのときだけ保つ）・FX-R-04（Edit Mode の「先頭から」を停止中の位置で判定）・FX-R-05〜08（`GitProcess` の読み切り上限・`pgrep -P` 再帰・実行中 git の台帳・`OnDisable` の後始末・プレリリースだけのとき未選択・`GitTag` 3 区間）・FX-R-10（規則 2 つの整理、挙動不変）・FX-R-11 / 12（CHANGELOG 等）・FX-R-13（テスト）・FX-R-14（リスナーの発見を public のみ） |
>
> **方法**: 専用 worktree を `84269f4`（detached → 本ブランチ）に合わせ、`git diff 84269f4^1 84269f4`（42 ファイル）・`git diff 9f40cbb..84269f4 -- …/Compat/Snapshots/` と、変更後のファイル全体（`CutsceneManager` の Play / ネット送受信 / Late Join / マーカー / Tick / Cancel / Cleanup / StopAll、`NgoNetBridge` の Broadcast・中継・Dispatch、`PresentationManager.Tick` / `StopAll` / `CancelInternal` / `WaitAsync`、`AssetEventDispatcher`、`CutsceneEditModePreviewProvider`、`UnityMaterialMigrator`、`MayaMaterialImporter.ImportMaterial` / `BuildCommon`、`UnknownShaderGuard`、`GitProcess`、`GitTag`、`UpdateWindow` の非同期と更新チェック節、`CutsceneImportListeners.Discover`、`ExtensionPointDiscovery`、`DDriveMigrationRunner` の発見、追加テスト 6 ファイル・`DelayedNetworkRelay`）を**読むだけ**で確認した。実装者の報告（[55] の「→ 対応」・CHANGELOG・E-20・docs/14 §21・docs/26）は信用せず、コードと突き合わせた。**Unity は起動しておらず、コンパイル・EditMode / PlayMode テスト・実 `git`・実ネットワークは一切実行していない**（対応記録の「EditMode 1528 / 1528・PlayMode 918 / 918 green」は未確認）。指摘はコードを読んで確認した事実か、Unity / NGO / .NET / UniTask の挙動についての推定で、推定のものは「確度」欄に**推定**と書いた。
>
> 前提として読んだもの: `CLAUDE.md`（§0）、[docs/12](12_review.md) §3、[docs/42](42_distribution.md) §5（§5.14 E-19 / E-20・外部拡張点の発見規則）、[55](55_review_fix_rounds_2026-10-03.md)（元の指摘と対応記録。書式と重大度の基準）、[docs/26](26_timeline.md) §4.3 / §4.7 の追記、[docs/14](14_networking.md) 6-0 修正6（`remoteOneShotGraceSec`）・§18（N-5）・§21、[docs/29](29_network_device_test.md) §8・§25、`CHANGELOG.md` の `[Unreleased]`、[docs/52](52_manual_verification_fc.md)。

## 総評

- **リリースを止める実バグ（P1）は見つからなかった**。互換面は v1.3.1 から見て**追加のみ**のまま（`9f40cbb..84269f4` のスナップショット 4 ファイルは `+120 / −0`、削除行 0 件。本ラウンドはスナップショットに触れていない）。追加された定数・引数（`RemoteFreshStartGraceSec`・`catchUpFireMarkers`・`_tickBuffer`）はすべて private で公開面は増えていない。ネットメッセージの形式も不変。
- 元の 14 件は **12 件が解消、2 件が一部解消**:
  - **FX-R-01（受信側の 0 秒のマーカー）は一部**: Host が送る形（テストが見ている形）では送信側・受信側の発火回数が揃った。ただし MS2026 で主になる **Client が送る形では、他の Client への到達が Client → Host → Client の 2 区間**で、開始位置が 1 区間の約 2 倍になる。片道 200ms 前後で 0.5 秒のしきい値に近づき、超えた端末だけ `[0, 開始位置]` が全部無音になる（全か無か）。`PresentationManager` の同じ値の猶予はマーカーごとの「遅れ ≤ 0.5 秒」（滑る窓）なので、名前は同じ値でも規則が違う。テスト・docs/52 4-5 の実機手順も Host 送信だけ。E-20・docs/14 §21・CHANGELOG の「Host / Client / 送信者で回数が揃う」「0〜200ms は十分収まる」は Client 送信の 2 区間を考えていない（**FY-R-02、P3。タグ前に規則を決めるのが安い**）。
  - **FX-R-02（欠けたシェーダーで既存 Data を上書き）は一部**: シェーダー参照と固有（Specific）は保たれるようになった。しかし同じ呼び出しの中で `MayaMaterialImporter.ImportMaterial` の既存 Data 経路が先に走り、**欠けたシェーダーの `.mat` から作った Common（色・テクスチャ・金属度等）で既存 Data の Common を上書きする**。シェーダーが欠けた Material からはプロパティがほぼ読めない（**推定**）ので、Toon の色・テクスチャが既定値に戻りうる。警告文「シェーダーと固有の設定は変更しませんでした」は Common の上書きに触れていない。docs/52 15.6 の新しい手順 4（実データでパッケージを外して再生成）はこの経路をそのまま通る（**FY-R-01、P2**）。
- **FX-R-03（Tick の写し走査）は正しい**: 写しの再利用バッファは入れ子の `Tick` を `_inTick` で弾くので再入で壊れない。写しの各 Handle は世代つき（`InstanceStore.TryGetQuiet` / `IsValidSilent` が `_generations[index] == handle.Generation` を見る）なので、走査中に止められて枠が再利用されても古い Handle は無効になる。Tick 中に Play されたものは写しに無くその Tick では進まない。`AddRange(List)` は容量が足りれば割り当てなし（`StopAll` / `CancelAllNetworked` の `ToArray` は定常経路ではない）。
- **`PresentationManager.Tick` / `StopAll` / `CancelAllNetworked` の同形の走査（v1.3.1 から既存）は、添字ずれだけでなく `ArgumentOutOfRangeException` まで起きる**: `Complete` の中の `Waiter.TrySetResult()`（`WaitAsync` を await したゲームのコードの続き）と `CompletedSubject` の購読者が、`Cleanup` で自分が `_active` から外れる**前に**走るため、そこで**古い（添字の小さい）ローカルの Presentation を止める**と、1 周で 2 要素減って次の添字が範囲外になる。`GameLoop.Tick` は例外を捕まえないので、その Frame の後続 Manager の Tick も止まる。v1.3.1 からの既存の形で退行ではないが、Cutscene と同じ数十行で直せるので **v1.4.0 に入れるのが望ましい（タグを止める理由ではない）**（**FY-R-03、P3**）。
- **git まわり（FX-R-05〜08）は意図どおり**。ただ FX-R-05 の「読み切りが 5 秒を超えたら `KillTree`」は、git 本体が既に終わっているので `KillTree` の先頭の `HasExited` で即 return し、**何も止めない**（パイプを掴んだ子孫はもう git の子ではない）。UI が固まらないという目的は達しているが、対応記録・docs/42 の記述と実際が違い、待っていたスレッドプールのスレッドは子孫が終わるまで残る（**FY-R-04、P3**）。
- **FX-R-14 で v1.3.1 の何かが止まることは無い**: 変えたのは v1.4.0 で新しく入る `CutsceneImportListeners.Discover`（FC-5）だけで、取り込み系 4 種の `ExtensionPointDiscovery` は以前から同じ public 規則。D-Drive 自身の取り込みハンドラ（internal）は発見に頼っていない。Migration / Validator の発見（internal も拾う）は §5.14 の対象外と明記されており変更なし。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **0** | – |
| P2（直すべき不具合・設計上の穴） | **1** | FY-R-01 |
| P3（整理・改善） | **6** | FY-R-02〜07 |

---

## FX-R-01〜14 の解消確認

「解消」= 元の失敗の筋書きが起きなくなり、対応記録が実装と一致し、別の経路を壊していない。「一部」= 書かれた経路は直ったが、同じ指摘の範囲に残りがある。

| 指摘 | 判定 | 根拠（ファイル:行） | 補足 |
|---|---|---|---|
| FX-R-01 受信側の 0 秒のマーカー | **一部**（FY-R-02） | `Runtime/Cutscene/CutsceneManager.cs:112`（定数）・`:284`（`catchUpFireMarkers` なら無音の追いつきをしない）・`:1053`（`elapsed = Max(0, NetworkTime − StartNetTime)`）・`:1082-1083`（`elapsed <= 0.5` で新規開始） | Host 送信の形は揃う（下の発火回数の表）。Client 送信の 2 区間・しきい値超えの全か無か・テストが Host 送信だけ、が残る。メッセージ形式は不変。発火は `AssetEventDispatcher`（SE / VFX / AnchorGroup のローカル再生のみ）・`CameraFx.Shake`・`Haptics.Play`・外部 `Fire` で、D-Drive 側からネット送信は増えない |
| FX-R-02 欠けたシェーダーで既存 Data を上書き | **一部**（FY-R-01） | `Editor/Material/UnityMaterialMigrator.cs:133-149`（`report.Created` の前後で新規 / 既存を判定し、既存 + `sourceMissing` なら Shader / Specific に触れない）、`MayaMaterialImporter.cs:203`（`HasMissingShaderReference` なら埋め直さない）、`UnknownShaderGuard.cs:63-76` | Shader・Specific は保たれる。`ImportMaterial` の既存経路（`MayaMaterialImporter.cs:189-218`）が Common を上書きするのは残る。`HasMissingShaderReference` は Editor アセンブリ内で Runtime / Editor の境界は正しい。`nameof(MaterialData.Shader)` で引いており、テストが実 Editor で真を確かめたと記録している（未確認） |
| FX-R-03 購読者の Stop / Play で Tick の添字ずれ | **解消** | `CutsceneManager.cs:1484-1545`（写し・`_inTick`・`TryGetQuiet`）・`:675-694`（Event・Signal の後で `IsValidSilent`）・`:715`・`:734`（各マーカーの後）・`:1211-1223`・`:1709-1723` | 写しの再入・世代つき Handle・Tick 中の Play は次の Tick から、を確認。Shake / Haptic の後に確認が無いのは、外部コードを呼ばない（`CameraFx` / `Haptics` の内部）ので問題なし。同形の `PresentationManager` は FY-R-03 |
| FX-R-04 Edit Mode の「先頭から」判定 | **解消**（P3 の残り FY-R-05） | `Editor/Cutscene/CutsceneEditModePreviewProvider.cs:209`（`session.LastTime <= 1e-4` または巻き戻って始まった） | 停止中の位置で決まり、更新の間隔に依存しない。`Collect` がカーソルを 0 に戻すので「巻き戻って始まった」分岐も `[0, elapsed]` を正しく 1 回ずつ発火する。ループ再生は `WasPlaying` が true のまま巻き戻し分岐（無音）。フレーム送り（停止中）は位置を記録するだけ。途中から始めたときの最初の 1 フレームの区間を飛ばす点は以前から（FY-R-05） |
| FX-R-05 `WaitForExit()` の上限・孫プロセス | **解消（目的は達成。記述と実際の差 FY-R-04）** | `Editor/Update/GitProcess.cs:179-186`・`:292-360` | 5 秒で戻る。`pgrep -P` は深さ 6・`found.Contains` で循環しない・葉の側から KILL。ただし読み切り超過時の `KillTree` は何もしない |
| FX-R-06 ドメインリロード / 終了 / 閉じたとき | **解消** | `GitProcess.cs:26-61`（`[InitializeOnLoadMethod]` で `beforeAssemblyReload` / `quitting` に `KillAllRunning`、台帳は lock）・`:213-219`、`UpdateWindow.cs:151-175` | `_busyTask` は `Task.Run`（同期コンテキストを捕まえない）なので主スレッドの `Wait(1500)` でデッドロックしない。`_busyCts.Dispose()` 後もワーカーは `IsCancellationRequested` しか見ない（`GitProcess.cs:166`）ので例外にならない。閉じた直後に開き直すと前の git が最大数秒並走しうるが、作業フォルダは GUID ごとで衝突しない |
| FX-R-07 プレリリースだけのとき既定選択 | **解消** | `UpdateWindow.cs:1030-1038`（`index = -1`）・`:846-855`（未選択で押したら警告して何もしない） | 「元に戻す」等の他のボタンは `dropdown` を見ないので影響なし。表示用の `formatSelectedValueCallback` は null を受けても `HashSet.Contains(null)` で例外にならない |
| FX-R-08 `GitTag` の 2 / 4 区間 | **解消** | `Editor/Update/GitTag.cs:55-59` | D-Drive の既存タグ（`v1.0.0`〜`v1.3.1`）・`v` 無しの `1.4.0`・大文字 `V`・`v01.2.0` は 3 区間なので従来どおり読む。T-Drive の想定タグ（`vX.Y.Z`）も同じ。`facial-v0.2.0` のような接頭辞付きは以前から読まない |
| FX-R-09 標準シェーダーへ戻した後も保つ | **解消** | `UnityMaterialMigrator.cs:79`（`sourceUnknown`）・`:150`・`:176-185` | 「保つ」の直後の再取り込み（元の `.mat` が Toon のまま、非対話 `Ask` = `ConvertKeepingExisting`）では `sourceUnknown` が真なので保つ = 意図せず Lit に戻らない。元の `.mat` が欠けたときは `IsUnknown` が偽（欠けは除外）なので、先の `!isNew && sourceMissing` 分岐が受け持つ。ダイアログの「変換」（`Convert`）は従来どおり上書き |
| FX-R-10 重なる登録の規則 | **解消（挙動不変・文書化）** | `Runtime/Canvas/UiManager.cs:1476-1478`（コメントのみ）、[07] の表、`EmbeddedCanvasTests` のテスト名 | コードの変更はコメントとテスト名だけで、実行時の挙動は変わっていない |
| FX-R-11 認証プロンプトの記載漏れ | **解消** | CHANGELOG 互換性節（P-15・PC-R-01 の項）・[42] §4.2.1・`docs/50_consumer_guide/update.html`・[43] 15-17a | consumer guide は機能としての記述のみ（以前との比較なし） |
| FX-R-12 CHANGELOG の古い記述 | **解消**（見送りは妥当） | CHANGELOG の FC-15・FC-11・P-15・U-28・FC-R-03 の項、[53] / [54] の訂正注記 | 矛盾していた 4 か所は直った。全面の再編の見送りは妥当（下の「見送り」） |
| FX-R-13 テストの抜け | **一部（テストの主張の幅 FY-R-06）** | `CutsceneNetMarkerSymmetryTests`・`UnknownShaderPolicyTests`・`ExternalContractMarkerEditModeTests`・`ExternalContractCutsceneListenerTests`・`PrereleaseTagTests`・`GitProcessTests` | 追加テストは修正前のコードで赤になる内容（読んだ範囲。`Delay0ms_*` と `Receiver_SeekAndSkip_*` は開始位置 0 なので修正前でも通る）。Client 送信・Host が受信・Event / Shake / Haptic / 外部の受信側・Common の保持は無い |
| FX-R-14 リスナーの発見規則 | **解消** | `Editor/Cutscene/ICutsceneImportListener.cs:103-120`、`Editor/Import/ExtensionPointDiscovery.cs:17-60`、[42] §5.14 | 5 つの拡張点（`ICutsceneImportListener` + 取り込み系 4 種）で (1) public（入れ子なら `IsNestedPublic`）(2) 非 abstract・非 interface・ジェネリック定義でない (3) `GetConstructor(Type.EmptyTypes)` = public な引数なしコンストラクタ (4) `DDrive.Tests*` 除外、が一致し §5.14 の文面とも一致。入れ子の判定の細部は FY-R-07 |

**見送り 2 点の妥当性**: (1) FX-R-12 の `[Unreleased]` 全面再編 — 矛盾する記述は無くなり、残るのは「同じ機能の項が複数に分かれている」読みにくさだけなので、v1.4.0 を止める理由ではない。タグを打つ時に冒頭へ「v1.3.1 から見て挙動が変わる点」を 5〜6 行で並べる要約を足すと MS2026 が読みやすい（任意）。(2) FX-R-13 の `CreateFromSelection` の流れのテスト — `BeginBatch` / `EndBatch` の対は [55] で `try/finally` をコード読みで確認済みで、`Selection` を差し替える仕掛けを入れる危険の方が大きい。妥当。ほかに見送られた「欠けた Material だけの操作でダイアログを出さない」も、Console の警告が出るので妥当（ただし FY-R-01 を直すまで警告文が Common について誤る）。

---

## FX-R-01: 立場別・マーカー種別別の発火回数

前提: Timeline に `M0`（0 秒）・`Md`（0 秒より後で、その端末の開始位置以内）・`Ml`（開始位置より後）を置いた Cosmetic の `CutsceneData`。「開始位置」= 受信した端末での `Max(0, NetworkTime − StartNetTime)`。`NgoNetBridge.Broadcast` は Host 発なら `ClientsAndHost`（Host 自身にも届く）、Client 発なら Host が中継して全員（送信者自身を含む）に配る（`NgoNetBridge.cs:380-401`・`:638-657`）。予測再生した端末に自分のメッセージが戻ってきたときは `_networkedHandles` に生きたインスタンスがあるので何もしない（`CutsceneManager.cs:1033-1043`）。

**種別について**: Event / Signal / Shake / Haptic / 外部 `ICutsceneMarker` は同じカーソル方式（`AdvanceMarkers`）なので、回数は種別によらず同じ。違いは受け手だけ: Signal は `OnMarker` の購読者に届くが、受信側のインスタンスの Handle はゲームのコードに返らない（`DebugActiveHandles` 以外で取れない）ので、**受信側の Signal は実質だれも聞いていない**。Event は `AssetEventDispatcher` で SE / VFX / AnchorGroup をローカル再生、Shake / Haptic はその端末だけ、外部はその端末の `Fire`。

| # | 立場 | 開始位置（推定の目安。片道遅延 L、[29] §8 の Client の ServerTime の遅れ c ≈ 80ms） | M0 | Md | Ml | v1.3.1 |
|---|---|---|---|---|---|---|
| 1 | Host が送信（予測再生あり） | 0（ローカル Play） | 1 | 1 | 1 | M0 = 0, 他 1 |
| 2 | Host が送信（予測再生なし。自分宛ての RPC で再生） | ≈ 0（アプリ層の擬似遅延があればその分） | 1 | 1 | 1 | M0 = 0, Md = 0（擬似遅延時）|
| 3 | Client（Host 発を受信） | ≈ L − c（負なら 0） | ≤0.5 なら 1 / 超えたら 0 | 同左 | 1 | M0・Md = 0 |
| 4 | Client が送信（予測再生あり） | 0（ローカル Play） | 1 | 1 | 1 | M0 = 0, 他 1 |
| 5 | Client が送信（予測再生なし。中継で戻った自分宛て） | ≈ 往復（2L） | ≤0.5 なら 1 / 超えたら 0 | 同左 | 1 | M0・Md = 0 |
| 6 | Host（Client 発の中継を受信） | ≈ L + c | ≤0.5 なら 1 / 超えたら 0 | 同左 | 1 | M0・Md = 0 |
| 7 | **他の Client（Client 発、Client → Host → Client の 2 区間）** | **≈ 2L**（両端の Client の ServerTime の遅れは打ち消し合う。各区間の送信は NGO のティック単位でまとめられる分が足される） | ≤0.5 なら 1 / **超えたら 0** | 同左 | 1 | M0・Md = 0 |
| 8 | Late Join（Host の台帳から再送、開始から 0.5 秒以内に届く） | ≤ 0.5 | 1 | 1 | 1 | 0 / 0 / 1 |
| 9 | Late Join（開始から 0.5 秒より後） | > 0.5 | 0 | 0 | 1 | 同じ |

**一致するか**: 開始位置がどの端末でも 0.5 秒以内なら、**全員が M0・Md・Ml を 1 回ずつ**で一致する（#1〜#8）。Host 送信（#1・#3）は L が 0.5 秒 + c まで揃う。**Client 送信では #7 の 2 区間が先にしきい値を超える**（片道 200ms 前後で 0.4〜0.5 秒台。**推定**: NGO の ServerTime の推定誤差とティックのまとめ送りの量は実機でしか分からない）。超えた端末だけ M0・Md が全部無音になり、送信者・Host（#4・#6）と食い違う。

**二重発火の筋書きの確認**

| 筋書き | 結果 | 根拠 |
|---|---|---|
| 予測再生した送信者に自分のメッセージが戻る | 二重にならない（既存のインスタンスがあれば無視） | `:1033-1043` |
| 予測再生のインスタンスが、自分のメッセージが戻る前にローカルで消えた（`StopAll` = シーンのアンロード、ネットを通らない終わり方） | **戻ったメッセージで受信側として再生し直し、開始位置 ≤ 0.5 なら M0・Md がもう一度鳴る**（計 2 回）。v1.3.1 でも Ml は 2 回目が鳴っていたので経路は既存、今回 M0・Md も加わった。`Cancel` はネット経由（`:1255-1265` で Broadcast して自分では止めない）で Play → Cancel の順に戻るので、この筋書きにならない。往復時間内にシーンを抜ける操作は稀 | `:1709-1723`・`:1658-1689`（`Cleanup` で `_networkedHandles` から外れる） |
| 同じ Cutscene の再 Play / Cancel → 再 Play | 二重にならない（`HandleNetKey` は毎回新しく、カーソルはインスタンスごと） | `:969-983`・`:253-276` |
| Director のプール再利用 | 二重にならない（カーソルはインスタンスにあり、スロット側は `CutsceneCameraStateHolder.HasData` も戻す） | `:869-893` |
| 最初の Tick より前に Pause | 再開後の最初の Tick で 1 回ずつ（一致） | `Tick` は Paused を飛ばすだけ |
| 最初の Tick より前に Skip（`CutsceneSeekMsg`）/ デバッグ `Seek` | `[0, 目標]` は無音で飛ぶ（送信者は既に鳴らしている）。Skip の規則どおりで、回数の非対称は Skip を押した場合だけ | `:1345-1357` |
| 最初の Tick より前に Cancel | 鳴らない（Cancel が先に届くなら送信者側も直後に止まる） | — |
| 開始位置がちょうど 0.5 秒 | 新規開始（`<=`）。docs の「0.5 秒以内」と一致 | `:1082` |
| `NetworkTime` が未同期（0）・時計のずれで負 | `Max(0, …)` で 0 → 最初から再生として全部鳴る。v1.3.1 でも開始位置 0 として最初から再生していた（M0 以外は鳴っていた）ので経路は既存。NGO は接続承認時に時刻を同期してから RPC を届けるので通常は起きない（**推定**） | `:1053` |
| `NetworkTime` が巨大（前のセッションの時刻との混在） | 開始位置 ≥ 尺なら復元しない（`:1055-1058`）。Host 引き継ぎでは `ResetNetworkedState` が台帳・保留キューを空にし、`NgoNetBridge` も切断時に遅延キューを空にするので、古い `StartNetTime` のメッセージは残らない | `:1228-1236` |
| Registry 準備前に届いて保留された | 準備完了時の `NetworkTime` で開始位置を計るので、保留が 0.5 秒を超えると無音（試合開始直後に重いカタログ読み込みをする端末だけ。仕様として妥当） | `:996-1012` |

**ネット送信・権威的な処理を伴うマーカー**: D-Drive 側のマーカー処理は送信しない（`AssetEventDispatcher` は SE / VFX / AnchorGroup のローカル再生だけ、`CameraFx` / `Haptics` は端末ローカル）。Event の購読者（`EventBus.OnEventFired`）・Signal の購読者・外部 `Fire` がゲーム側で送信や権威的な処理をすれば各端末で多重になるが、**v1.3.1 でも開始位置より後のマーカー（Ml）は全端末が発火していた**ので、その前提（マーカーは各端末の見た目専用、権威的な処理はマーカーに載せない）と整合している。追いつき発火で増えるのは「開始直後の 0.5 秒以内」の区間だけ。

**0.5 秒以内に届いた Late Join 再送の副作用**: その端末が最初からいれば鳴ったはずの区間なので不自然ではない。MS2026 の Host 引き継ぎ（`host_migration`）は `ResetNetworkedState` でネット由来のカットシーンを全部止め、新しい Host の台帳は空から始まるので、引き継ぎで古いカットシーンが再送されて鳴り直すことは無い。再接続した端末が、自分で予測再生したカットシーンの再送を開始から 0.5 秒以内に受け取ると二重になりうるが、再接続に 0.5 秒未満はかからない。

**しきい値を `remoteOneShotGraceSec` と別定数にしたことの是非**: 値の出どころは同じでも、**規則が違う**ので別定数であること自体は正しい。ただ Presentation は「マーカーごとに遅れ（開始位置 − マーカーの時刻）≤ 0.5 秒なら発火」の滑る窓、Cutscene は「開始位置 ≤ 0.5 秒なら `[0, 開始位置]` を全部、超えたら全部無音」の全か無か。docs は「同じ値・同じ根拠」とだけ書いており、規則の違いが読み取れない。どちらに揃えるかは FY-R-02。

---

## P2 — 直すべき不具合・設計上の穴

### FY-R-01. 【FX-R-02】シェーダーが欠けている間の再生成で、既存 MaterialData の Common（色・テクスチャ等）が上書きされる（警告文は「変更しませんでした」）

- **場所**: `Editor/Material/UnityMaterialMigrator.cs:133-149`（`ImportMaterial` を呼んだ**後**で `!isNew && sourceMissing` を判定）→ `Editor/Material/MayaMaterialImporter.cs:187`（`BuildCommon(source, …)`）・`:189-201`（既存 Data があり Common に差分があれば `existing.Common = common`）・`:306-360`（`BuildCommon` は `source.GetTexturePropertyNames()` と `HasProperty` を見る `GetColor` / `GetFloat`、`:422-426`）・`:210-213`（Profile の `PreserveSpecificOnReimport = false` なら `Specific = null`。Migrate 経路は true 固定なので該当しない）
- **何が問題か**: FX-R-02 の対処は、`ImportMaterial` が既存 Data を更新し終えた**後**に「Shader / Specific を書き換えない」分岐を足しただけで、`ImportMaterial` 自身の既存 Data 経路は元の `.mat` から作り直した Common で既存 Data の Common を上書きする。シェーダーが欠けた Material（`Hidden/InternalErrorShader`）では `HasProperty("_BaseColor")` 等が偽になり（**推定**: Material のプロパティはシェーダーのプロパティ表で引かれる）、テクスチャのプロパティ名もシェーダーから取られるので空になりうる（**推定**）。結果、Common が `MaterialCommon.Default` に近い値（色 = 白・テクスチャ無し）になり、既存 Data の Common と差分があれば**上書きされ、元の値は失われる**（パッケージが戻っても戻らない）。同時に `UnityMaterialMigrator` は「シェーダーと固有の設定は変更しませんでした」と警告するので、利用者は無事だと受け取る。FX-R-02 のテスト（`MissingSource_ExistingData_KeepsItsShaderAndSpecific_RegardlessOfHandling`）はテスト用の未知シェーダー（色・テクスチャの差分が出ない）で Shader と Specific の件数だけを見ており、Common を確かめていない。ラウンド 3 以前（v1.3.1 も含む）から同じ経路で Common を上書きしていたので退行ではないが、FX-R-02 の「既存 Data は保持」の範囲の取りこぼし。
- **失敗の筋書き**: [52] 15.6 の新しい手順 4 そのもの。T-Drive の Toon で色とテクスチャを持つ MaterialData がある → T-Drive を一時的に外す（または権限の無いメンバーが開く）→ その `.mat` で右クリック作成 / Model エディタの「元ファイルを再読み込み」→ Shader 欄は Toon のまま（期待結果どおり）だが、Common の色が白・Albedo が空になる → パッケージを戻すと Toon は正しく描かれるが、色とテクスチャが消えている。手順 4 の期待結果は「Shader 欄・固有が変わらない」だけなので、確認者が Common の欠落を見落とす可能性がある。
- **直し方の案**: (a) `UnityMaterialMigrator.MigrateCore` で、`sourceMissing` のときは `ImportMaterial` を呼ぶ**前に** `MayaMaterialImporter.FindExisting(BuildSourceMaterial(…), BuildLegacySourceMaterial(…), gameDataRoot)` で既存 Data を探し、あれば何も書かずに警告して返す（新規だけ従来どおり作る）。(b) `MayaMaterialImporter.ImportMaterial` の既存 Data 経路にも同じ守り（`UnknownShaderGuard.IsMissing(source.shader)` なら Common を上書きしない）を入れると、FBX の取り込み経路も含めて一貫する。(c) テスト: 色（`_BaseColor` を赤）とテクスチャを持つシェーダーで作った既存 Data → 元の `.mat` を欠けさせて `Migrate` → `Common` が変わらない。(d) [52] 15.6 の手順 4 の期待結果に「Common（色・テクスチャ）も変わらない」を足す。直すまでは、**手順 4 は実データの複製で行う**。
- **確度**: コードの順序（Common の上書きが先・保持の分岐が後）は確認済み。欠けたシェーダーの Material からプロパティ・テクスチャ名が読めない点は**推定**（読めれば差分が出ず `Unchanged` になり実害は無い）

- → 対応（修正ラウンド 4、2026-10-04、`0f8e33a`）: **修正**（レビューの案 (a)(b)(c)(d) のとおり）。`MayaMaterialImporter.TryKeepExistingWhenShaderMissing`（新規・public static）を足し、(1) `UnityMaterialMigrator.MigrateCore` の**入口**（`ResolveTargetShader` の直後・`ImportMaterial` を呼ぶ前）、(2) `MayaMaterialImporter.ImportMaterial` の先頭（FBX の取り込み = `ImportModel` 経路・`ModelSlotBinder` 経由も同じ）で、元の `.mat` のシェーダーが `IsMissing` かつ**既存の MaterialData があれば何も書かず警告して既存を返す**（`report.Unchanged++`）。既存の探索は**読み取りだけ**（旧形式の `SourceMaterial` キーの移行もしない = Data を 1 バイトも書かない）。既存が無ければ従来どおり新規に Lit で作る。`MigrateCore` の旧 `!isNew && sourceMissing` 分岐は到達しなくなったので削除した。警告文は「…既存の MaterialData '…' はシェーダー・固有・共通(色・テクスチャ等)とも変更しませんでした。パッケージを戻してから取り込み直してください」で、事実と一致する。同じ入力で既存 Data を書く経路の洗い出し（grep）: `ImportMaterial` の呼び出しは `UnityMaterialMigrator.MigrateCore` のみ、`ImportModel`（`MayaModelPostprocessor` / `ModelSlotBinder`）は内部で `ImportMaterial` を呼ぶので (2) で塞がる。`FindExisting` の呼び出し（`ModelSlotBinder.cs:217`）は読み取り側の既存利用で、書き換えの経路ではない。
  - **推定の確認結果**: 「欠けたシェーダーの Material からプロパティが読めず、Common が既定値になりうる」は**確認できた**（EditMode `MissingShaderMaterial_PropertiesAreNotReadable_EvenIfSavedInTheFile` = `.mat` に `_BaseColor` / `_Color` / `_Metallic` を保存していても `HasProperty` は偽・`GetTexturePropertyNames()` は空。実 Editor で green）。つまり修正前は既存 Data の Common が既定値（白・テクスチャ無し）に近い値で上書きされうる状況だった。
  - **テスト**（EditMode `UnknownShaderPolicyTests`）: `MissingSource_ExistingData_IsNotModifiedAtAll_IncludingCommon_RegardlessOfHandling`（Keep / Convert / ConvertKeepingExisting の 3 通り。Common の全欄〔Albedo・AlbedoTint・Normal・NormalScale・Mask・Metallic・Smoothness・Emission・EmissionColor・EmissionIntensity・Blend・Cutoff・DoubleSided〕・Shader・Specific の件数・`EditorUtility.IsDirty` が偽・`.asset` のバイト列が不変・警告に「共通」）、`MissingSource_ImportMaterial_DoesNotOverwriteExistingCommon`（FBX 経路 = `ImportMaterial` 直接）、`MissingSource_NoExistingData_StillCreatesNewLitData`（既存が無ければ新規 = Lit）、上記の推定確認。[52] 15.6 の期待結果（手順 4）に「共通（色・テクスチャ・Blend 等）も変わらない」を追加。
  - **挙動の変更（v1.3.1 から）**: v1.3.1 は欠けたシェーダーの `.mat` の再生成で既存 Data の Common（と、既存の Shader / Specific）を上書きしていた。v1.4.0 は何も書き換えない。CHANGELOG の互換性節（PATCH 相当。Editor の弱い互換面）に記載。

---

## P3 — 整理・改善

### FY-R-02. 【FX-R-01】Client 送信の 2 区間でしきい値に近づく / 全か無かの規則が Presentation と違う / テスト・docs・実機手順が Host 送信だけ

- **場所**: `CutsceneManager.cs:1082`（`elapsed <= RemoteFreshStartGraceSec` の全か無か）、`Tests/Runtime/CutsceneNetMarkerSymmetryTests.cs`（全テストが Host 送信 or `Bridge.Receive(HostId, …)` の直接注入。`DelayedNetworkRelay` は全端末が 1 区間・同じ時計）、docs/14 §21「0ms〜200ms の遅延は十分収まり」、E-20「Host / Client / 送信者で回数が揃う」、[52] 4-5（Host の操作で再生）
- MS2026 は 4 人対戦で各プレイヤー（多くは Client）が自分の演出を起こす。Client 送信の Cutscene は他の Client に Client → Host → Client の 2 区間で届くので、開始位置は Host 送信の約 2 倍になる（上の表の #7）。片道 200ms 前後で 0.5 秒に近づき、超えた端末だけ `[0, 開始位置]` のマーカーが**全部**無音になる。`PresentationManager` の同じ値の猶予は「マーカーごとの遅れ ≤ 0.5 秒」なので、開始位置 0.6 秒でも 0.1 秒以降のマーカーは鳴る（Late Join の再送にも同じ規則が一律にかかる、docs/14 6-0 修正6）。
- 直し方の案（E-20 の契約になるので**タグ前に決めるのが安い**）: (a) Presentation と同じ滑る窓にする: 受信時は `elapsed − markerTime > 猶予` のマーカーだけ無音で進め、残り（`[elapsed − 0.5, elapsed]`）は最初の Tick で発火する。2 区間で 0.5 秒を少し超えても開始直後のほとんどのマーカーは鳴り、Late Join は「直前 0.5 秒のマーカーが鳴る」（Presentation と同じ）。(b) 今の全か無かを残すなら、docs/14 §21・E-20・CHANGELOG に「Client が送るカットシーンは他の Client に 2 区間で届く。開始位置が 0.5 秒を超えた端末では開始までのマーカーは全部鳴らない」「Presentation の猶予とは規則が違う（全か無か）」と書く。どちらでも、テストに Client 送信（Client → Host → Client。`DelayedNetworkRelay` に中継の区間を足すか、受信側の `StartNetTime` を 2 区間ぶん過去にする）と Host が受信する形を足し、[52] 4-5 の実機確認に「**Client の操作で再生し、もう 1 台の Client で鳴るか**」（3 台。[29] §25 の 4 台構成が望ましい）を足す。
- 確度: 規則の違いと 2 区間の経路はコード読みで確認済み。実際の開始位置（ServerTime の推定誤差・ティックのまとめ送り）は**推定**

- → 対応（修正ラウンド 4、2026-10-04、`e924392`）: **修正**（まとめ役の決定: 「位置ごとの猶予」に揃える）。`CutsceneManager` の受信側の追いつき発火を、「開始位置が 0.5 秒以内なら `[0, 開始位置]` を全部発火、超えたら全部無音」から「**開始位置から遡って猶予（0.5 秒）以内にあるマーカーだけを最初の `Tick` で発火する。それより古いマーカーは無音**」に変えた。
  - **確定した判定式**: マーカーを無音で飛ばす条件は `開始位置 − マーカーの時刻 > 0.5`（= 発火する条件は `開始位置 − マーカーの時刻 ≤ 0.5`。**ちょうど 0.5 秒は発火する**）。`PresentationManager.SeekInitialTracks` の `lateBySec = elapsed − track.Time; if (lateBySec > _remoteOneShotGraceSec) → スキップ`（`PresentationManager.cs` の `SeekInitialTracks`）と**境界の含む / 含まないまで一致**（Presentation は float、Cutscene は double。定数は別: `CutsceneManager.RemoteMarkerGraceSec` = 0.5。旧名 `RemoteFreshStartGraceSec` は未リリースなので改名）。実装は `SkipMarkersOlderThan`（5 種のカーソルを `elapsed − 時刻 > 猶予` の間だけ無音で進める。割り当てなし）。`PlayLocalInternal(catchUpFireMarkers: true)` は受信側の再生開始（Late Join の再送を含む）で常に true になる（従来の `elapsed <= 0.5` の分岐は廃止）。ローカル再生・予測再生・`Seek` / `Skip` の経路は不変。
  - **効果**: 開始位置が 0.5 秒以内なら従来（ラウンド 3）と同じく `[0, 開始位置]` が全部発火。Client 送信 → Host 中継 → 別の Client の 2 区間で 0.5 秒を少し超えても、直近 0.5 秒分は発火し全部が無音にはならない（0 秒のマーカーは遅延が 0.5 秒を超えた端末では無音 = **仕様として文書化**）。Late Join も同じ規則（参加時点から遡って 0.5 秒以内のマーカーだけ発火、それより前は無音）。Seek / Skip は従来どおり無音。「回数が揃う」の表現は「各端末の開始位置が猶予以内のとき」に直した（E-20・[14] §21 / §22・[26]・CHANGELOG・[51]）。
  - **Client 送信者のテストの組み方**: 既存の `DelayedNetworkRelay` は全員へ 1 区間で配るので、`RelayThroughHostId`（任意。設定すると Host 以外の送った Broadcast を、Host へ 1 区間・送信者自身を含む他の全員へ 2 区間で配る = 実 NGO と同じ経路）を足し、Host（id 0）+ Client A（送信者・予測再生）+ Client B の 3 者を組んだ（片道 0.35 秒 → Host の開始位置 0.35 秒 / B の 0.7 秒）。結果: A = `m0` 1 回、Host = `m0, m1`（`[0, 0.35]` 全部）、B = `m4, m6`（0 / 0.1 秒は古すぎて無音・0.4 / 0.6 秒は発火）。
  - **表のテスト（FY-R-06 の (1)〜(5) も同時に対応）**: 開始位置 0.3 / 0.5 / 0.7 / 2.0 秒 × マーカー 0 / 0.1 / 0.4 / 0.6 秒（Signal）= `m0,m1` / `m0,m1,m4` / `m4,m6` / （無音）、Event も 0.5 / 0.7 秒。開始位置 0 でも通る旧テストの穴は、開始位置 > 0 の Skip（`CutsceneSeekMsg` を最初の `Tick` より前に受信）・Seek で無音になることを別に固定。外部 `ICutsceneMarker`（`ExternalContractMarkerTests`: 開始位置 0.75 秒 → 遅れ 0.25 / 0 の 2 件が発火）・Late Join（1.25 秒時点の参加 → 遡って 0.5 秒以内の 1.0 秒だけ）・0.5 秒より後の Late Join で直近にマーカーが無ければ無音。既存の `E20_LateJoin_*`（外部マーカー）と `LateJoin_ResentByHost_IsSilent` は、新しい規則（開始位置ちょうど・遅れ 0.5 秒のマーカーは発火）に合わせて期待値を直した（旧規則の裏返しで、不具合ではない）。Shake / Haptic は `CameraFx` / `Haptics` の静的ファサードで観測しにくいため個別テストは置かず、同じ `SkipMarkersOlderThan` の 5 本のカーソルで共通（Signal / Event / 外部で規則を固定）。
  - **狭い二重発火（予測再生した送信者が、自分のメッセージが戻る前にローカルで止められた場合）**: 実コードで確認 → 筋書きは実在した（`StopAll` = シーンのアンロードはネットを通らず `_networkedHandles` から外れ、戻ったメッセージが「既存のインスタンス無し」で新規再生になる）。小さく安全に防げたので**防いだ**: 自分が予測再生した再生キーを `_predictedKeys`（`HashSet<uint>`、上限 256 で破棄。`ResetNetworkedState` で空）に覚え、メッセージが戻ったときに除き、**既存のインスタンスが無ければ再生し直さない**。予測再生なしの送信者（自分のメッセージで再生する）は影響を受けない。テスト: `PredictedSender_StoppedLocallyBeforeMessageReturns_DoesNotReplay` / `NonPredictedSender_PlaysFromItsOwnReturnedMessage`。残る既知の制限: メッセージが戻らないまま 256 件を超えて予測再生を重ねたとき（切断等）は覚えが捨てられ、その後に戻ったメッセージの再生し直しを防げない（実害は冒頭のマーカーが 1 回余分に鳴るだけ）。
  - **挙動の変更（v1.3.1 から）**: ラウンド 3 の「0.5 秒以内なら全部・超えたら全部無音」から「直近 0.5 秒分だけ」へ（0.5 秒以内の開始位置では同じ結果）。CHANGELOG の互換性節の MINOR 項を書き直した。実機確認は [52] 4-5 に追加（Client が送信者のとき別の Client で確認・開始位置が 0.5 秒にどれだけ近づくかの記録・0.5 秒より後の Late Join・`host_migration` 後の再生）。

### FY-R-03. 【FX-R-03 の同形】`PresentationManager.Tick` / `StopAll` / `CancelAllNetworked` の添字走査は、購読者・await の続きが古い Presentation を止めると範囲外例外になる（v1.3.1 から既存）

- **場所**: `Runtime/Presentation/PresentationManager.cs:1338-1359`（`Tick`。`_active` を後ろから添字で回す）・`:1362-1368`（`Complete` は `CompletedSubject.OnNext` → `Waiter?.TrySetResult()` → `Cleanup` の順）・`:1084-1091`（`CancelInternal` も同じ順）・`:1296-1314`（`WaitAsync` は `UniTaskCompletionSource`）・`:1413-1425`（`StopAll`）・`:1186-`（`CancelAllNetworked`）。`Foundation/Manager/GameLoop.cs:21-25`（各 Manager の `Tick` を try 無しで呼ぶ）
- 筋書き: ゲームのコードが `await presentation.WaitAsync(ct); olderLocalPresentation.Cancel();`（または `OnCompleted.Subscribe(_ => other.Cancel())`）と書く。`UniTaskCompletionSource.TrySetResult` は続きを同期で走らせる（**推定**: UniTask の既定の挙動）ので、`Complete` の中・`Cleanup` の前で古い（添字の小さい）ローカルの Presentation が `Cleanup` され、続けて自分も `Cleanup` されて 1 周で `_active` が 2 減る。処理中の要素が末尾だった場合、次の `i` が `Count` と等しくなり `_active[i]` が `ArgumentOutOfRangeException`。`GameLoop.Tick` は例外を捕まえないので、同じ Frame で `PresentationManager` より後に登録された Manager の `Tick` も走らない（次の Frame は戻る）。末尾でなければ、既に処理した要素が同じ Tick でもう一度進む（二重前進・マーカーの二重発火）。`FireDueTracks` の中の Signal 購読者が古い Presentation を止めた場合も二重前進。ネット（Cosmetic）の `Cancel` は Broadcast して戻るだけなので、この筋書きはローカルの Presentation で起きる。
- 所見: v1.3.1 から同じ形（`9f40cbb..84269f4` で `PresentationManager.cs` の差分なし）で退行ではない。`await` の続きで別の演出を止めるのはゲームのコードで普通に書く形なので、**v1.4.0 に入れるのが望ましい**（Cutscene と同じ「再利用の写し + `TryGetQuiet` + 入れ子の Tick を弾く」で数十行、テストは Cutscene の 4 件を写せる）。タグを止める理由ではないので、入れない場合は別チケット（v1.4.1 PATCH）にして CHANGELOG の既知の問題に書く。`AudioManager` / `VfxManager` 等の `RemoveAt(i)` は [55] のとおり確認していない（購読者を走査中に呼ぶ形か、要確認）。
- 確度: 添字の算術はコード読みで確認済み。UniTask の続きが同期で走る点は**推定**（R3 の `Subject.OnNext` の購読者は同期なので、`OnCompleted` 経由では確実に起きる）

- → 対応（修正ラウンド 4、2026-10-04、`c0b3890`）: **修正**（まとめ役の決定: v1.4.0 に含める。**挙動の変更なし**）。`PresentationManager.Tick` を `CutsceneManager.Tick` と同じ方式にした: 再利用の写し `_tickBuffer`（`_active` のコピー。割り当てなし）を後ろから走査し、各 Handle を `_instances.TryGetQuiet`（世代つきの有効性確認）で確かめ、無効なら飛ばす。入れ子の `Tick` は `_inTick` で弾く。Tick 中に Play されたものは写しに無く次の `Tick` から進む。発火順・完了通知の順・1 `Tick` で進む量は従来どおり（後ろ = 新しい方から）。`StopAll` / `CancelAllNetworked` は `_active.ToArray()` の写し（定常経路ではない）を走査して同じ確認をする。
  - **既存テストの変更**: **不要**（既存の `PresentationManagerTests` ほか PlayMode は無改修で green）。
  - **テスト**（PlayMode `PresentationTickReentrancyTests`、新規 7 件）: `OnCompleted` の購読者が古い Presentation を Cancel（以前は範囲外）/ `WaitAsync` の続きが古いものを Cancel / マーカーの購読者がまだ処理していない別のものを Cancel（以前は処理中のものが同じ Tick で二重に進んだ）/ マーカーの購読者が新しい Presentation を Play（その Tick では進まず次から）/ `StopAll` 中に中止通知の購読者が別のものを止める / `CancelAllNetworked`（ローカルのみ）/ 通常の完了の順序・進む量が不変。
  - **他の Manager の同形の走査（確認だけ。触っていない）**: `AudioManager` / `VfxManager` / `AnimManager`（`Tick` の `_allActive`）・`HapticsManager` / `CameraFxManager` / `MaterialManager`（`_activeFades`）は、走査の中で外部の購読者・await の続きを呼ばない（Anim は `_events.Fire(OnDisable/OnDestroy/OnLoop)` の購読者が他の Anim を止めうるが、Interrupt / ループでの EventBus 経由のみで await の続きは無い）ので**危険は小さい**（Anim の `Interrupt` 内の EventBus 購読者が別の Anim を止めた場合は理論上ありうる。未確認・要判断）。**同じ危険が実在するもの**: `UiTweenManager.Tick`（`CompleteInstance` が `_active.Remove` の後に `Waiter.TrySetResult()` = `WaitAsync` の続きを同期で走らせるので、続きが他の Tween を止めると添字がずれ、最悪 `_active[i]` が範囲外）と `UiManager.Tick` の `_transitions`（`t.Completion.TrySetResult()` が走査中に続きを走らせる）。いずれも v1.3.1 から既存。**報告のみ（まとめ役の判断待ち）**。

### FY-R-04. 【FX-R-05】読み切りが 5 秒を超えたときの `KillTree` は何もしない（git 本体は既に終了）・結果に印が付かない

- **場所**: `Editor/Update/GitProcess.cs:182-186`（`drain.Wait(5000)` が偽なら `KillTree(process)`）・`:239-244`（`KillTree` は先頭で `process.HasExited` なら return）
- この時点で git 本体は終わっている（`WaitForExit(100)` のループを抜けた後）ので `KillTree` は即 return し、パイプを掴んでいる子孫（ssh の `ControlPersist` 等。親が死んで init の子になっている）は止まらない。`Task.Run` の待ちスレッドは子孫が終わるまでスレッドプールに残る（`using` での `Process` の破棄で解けるかは Mono の実装次第 = **推定**）。結果は終了コード 0 なら成功として返り、「読み切れなかった」印は付かない（行単位の非同期読み取りなので git が書いた行は届いているはずで、出力が欠ける実害は小さい = **推定**）。UI が固まらないという FX-R-05 の目的は達している。直し方: 対応記録・docs/42 の「超えたら `KillTree`」の記述を「読み切りを打ち切って結果を返す（パイプを掴んだ子孫は止められない。残った一時フォルダは次回の掃除で消える）」に直す。止めたいなら、git の起動直後に子孫の PID を `pgrep -P` / `taskkill /T` 相当で記録しておく形が要るが、費用に見合わない。
- 確度: `HasExited` の早期 return は確認済み。子孫とスレッドの残り方は**推定**

- → 対応（修正ラウンド 4、2026-10-04、`b9acea3`）: **見送り（実態に合わせて記述を直した）**。止める形（子孫の PID を git の起動直後に記録して後から止める）は費用に見合わないので、レビューの案どおり、(1) `GitProcess` の読み切り超過時の `KillTree(process)`（git 本体が終了済みなので `HasExited` で即 return する何もしない呼び出し）を**削除**し、コメントを「パイプを掴んだ子孫は git の子ではなくなっていて止められない。待っていたスレッドプールのスレッドは子孫がパイプを閉じた（または終了した）時点で自然に終わる。読めた分で結果を返す」に直した。(2) [55] FX-R-05 の対応記録（「超えたら `KillTree`」）に訂正を注記。(3) [42] §4.2.1 の記述（出力読み切りは 5 秒で打ち切る。子孫は止められない）に直した。挙動は変わらない（もともと何も止めていなかった）。

### FY-R-05. 【FX-R-04】Edit Mode で途中から再生を始めたとき・一時停止から再開したとき、開始位置から最初の更新までの区間のマーカーが鳴らない（以前から）

- **場所**: `CutsceneEditModePreviewProvider.cs:218-222`（途中からは `SilentAdvanceTo(elapsed)` = 最初の更新の位置まで無音）
- 停止中の位置 `LastTime`（例 2.0）から再生を始め、最初の更新で `elapsed` が 2.03 になると、2.01 のマーカーは「開始位置より後」なのに無音で飛ぶ。一時停止 → 再開も同じ（再開の最初の 1 フレーム分）。E-20 の「途中からの再生開始位置**まで**は無音」・Play Mode の `Seek` + `Tick`（開始位置まで無音、それより後は鳴る）と食い違う。ラウンド 1 以前からの形で、FX-R-04 の対処の範囲外。直し方: 途中からの分岐を `SilentAdvanceTo(session.LastTime)` の後に `Advance(elapsed, true, …)` にする（開始位置ちょうどは無音、それより後は鳴る）。テスト: 2.0 で停止 → 再生 + 最初の更新で 2.03 → 2.01 のマーカーが 1 回。
- 確度: 確認済み（コード読み）

- → 対応（修正ラウンド 4、2026-10-04、`337f001`）: **修正**。途中から再生を始めた分岐を `SilentAdvanceTo(elapsed)` から、`SilentAdvanceTo(session.LastTime)`（再生を始める直前の位置まで無音。その位置ちょうども無音）+ `Advance(elapsed, true, …)`（その後、最初の更新の位置までに跨いだマーカーは発火）に変えた。FX-R-04 の「先頭から」判定（`LastTime <= 1e-4` または巻き戻って始まった）は条件ごと不変（既存の `E20_EditPreview_*` 5 件は無改修で green）。Play Mode の `Seek` + `Tick`（開始位置まで無音・それより後は鳴る）・E-20 の記述と一致した。テスト（EditMode `ExternalContractMarkerEditModeTests`）: `E20_EditPreview_ScrubThenPlay_FiresMarkersPassedBeforeTheFirstUpdate`（2.0 秒で停止 → 再生 + 最初の更新で 2.03 秒 → 2.0 は無音・2.01 は 1 回・二重なし・以降 2.5 秒）、`E20_EditPreview_ResumeFromPause_FiresMarkersPassedBeforeTheFirstUpdate`（一時停止からの再開）。[52] 4-4 に手順 (c) を追加。

### FY-R-06. 【FX-R-13】追加テストの主張の幅

- `CutsceneNetMarkerSymmetryTests`: (1) `Delay0ms_*` は受信側の開始位置が 0 で、修正前のコードでも通る（回帰としては有効）。(2) `Receiver_SeekAndSkip_AreSilent` は開始位置 0 の受信にデバッグ用の `Seek`（ローカルのみ）をかけるだけで、名前の Skip（`CutsceneSeekMsg` の受信）も、追いつき発火の対象（開始位置 > 0）に最初の Tick より前に Seek / Skip が来る形も見ていない。(3) Client 送信・Host が受信・2 区間・ちょうど 0.5 秒・Pause を挟む形が無い（FY-R-02）。(4) Signal 以外の種別（Event / Shake / Haptic / 外部）の受信側の追いつき発火が無い（同じカーソル方式なので実装上は同じだが、E-20 は外部マーカーの契約）。(5) `LateJoin_ResentByHost_IsSilent` は 1.5 秒の再送だけで、0.5 秒以内の再送が鳴る側（副作用として文書化した挙動）を固定していない。
- `UnknownShaderPolicyTests.MissingSource_ExistingData_*`: Common を見ていない（FY-R-01）。
- `GitProcessTests.Run_WithCancelledToken_*`: キャンセル済みトークンでは最初の 100ms で必ず止まるので、ツリー停止・読み切りの上限は試していない（環境依存なので妥当。名前どおりの範囲）。
- 直し方: 上の (2)〜(5) と FY-R-01 のテストを足す。確度: 確認済み

- → 対応（修正ラウンド 4、2026-10-04、`e924392` + `0f8e33a`）: **修正（FY-R-02 / FY-R-01 のテスト追加で対応）**。(1) `Delay0ms_*` は開始位置 0 で修正前でも通るが回帰としては有効なので残し、開始位置 > 0 の表のテストを追加。(2) Skip（`CutsceneSeekMsg`）・Seek を、追いつき発火の対象（開始位置 0.3 秒）の最初の `Tick` より前に受けるテストを追加（無音）。(3) Client 送信（Client → Host → 別の Client の 3 者・2 区間）・ちょうど 0.5 秒（開始位置 0.5・0 秒のマーカー）・（Host が受信する形は 3 者テストの Host で確認）。Pause を挟む形は「最初の Tick より前の Pause は再開後の最初の Tick で 1 回ずつ」（`Tick` が Paused を飛ばすだけ）で、実装上の分岐が無いため個別テストは置かない。(4) Signal 以外の種別: Event（受信側の表 2 件）・外部 `ICutsceneMarker`（`ExternalContractMarkerTests` 2 件。Late Join の既存テストの期待値も新規則に更新）。Shake / Haptic は静的ファサードで観測しにくく、同じカーソルを共有するので個別テストなし。(5) 0.5 秒以内の Late Join が鳴る側（1.25 秒時点の参加 → 遡って 0.5 秒以内の 1.0 秒が発火）と、0.5 秒より後で直近にマーカーが無い Late Join が無音の側を両方固定。`UnknownShaderPolicyTests` の Common 検証は FY-R-01 で追加。

### FY-R-07. 【FX-R-14】「public な入れ子」の判定は外側の型の公開性を見ない

- **場所**: `ICutsceneImportListener.cs:107`・`ExtensionPointDiscovery.cs:22`（`!type.IsPublic && !type.IsNestedPublic`）、[42] §5.14 (1)「public な型（入れ子なら public な入れ子）」
- `internal class Outer { public class Listener : ICutsceneImportListener {} }` は `IsNestedPublic` が真なので発見される（アセンブリの外から見えない型）。5 つの発見箇所で同じ判定なので一貫はしており、文面の「public な入れ子」とも字面上は一致する。「外から見える型だけ」を意図するなら `type.IsVisible` が正確。タグ後に変えると FX-R-14 と同じく「動いていた実装が黙って止まる」変更になるので、変えるならタグ前、変えないなら §5.14 に「入れ子は自分が public なら外側の公開性は問わない」と 1 文足す。実害はほぼ無い。確度: 確認済み（.NET の `IsNestedPublic` の定義）

- → 対応（修正ラウンド 4、2026-10-04、`695c95e`）: **修正**（まとめ役の決定: `Type.IsVisible` に揃える）。5 つの発見箇所（`ExtensionPointDiscovery` = 取り込み系 4 種〔FC-6 / FC-14〕と `CutsceneImportListeners.Discover`〔FC-5〕）の `!IsPublic && !IsNestedPublic` を `!IsVisible` に変えた（入れ子なら外側の型まで含めて public な型だけ。internal な型の中の public な入れ子型は発見されない）。**v1.3.1 から存在する発見箇所の規則は変えていない**: 変えたのは全部 v1.4.0 で新しく入った拡張点（v1.3.1 には `ExtensionPointDiscovery` も 5 つの拡張点の発見も無い。`DDriveMigrationRunner` / `CI.DiscoverValidators` の発見は internal も拾う規則のままで §5.14 の対象外）。[42] §5.14 の規則文・E-19 の行を合わせた。テスト（EditMode）: `E19_NestedPublicListener_IsDiscovered_OnlyWhenTheOuterTypeIsVisible`（外側が internal の入れ子 = 発見されない / 外側も public の入れ子 = 発見される。前提として `Type.IsVisible` の値も確認）、`E22_NestedProvider_IsDiscovered_OnlyWhenTheOuterTypeIsVisible`（取り込み系の拡張点側）。ダミー: `ExternalHiddenListenerHost` / `ExternalVisibleListenerHost` / `ExternalHiddenProviderHost` / `ExternalVisibleProviderHost`。

---

## 確認して問題なしだった観点

- **FX-R-01 の Host 送信の形**: 予測再生の送信者・Host 自身の受信・Client の受信で、開始位置 ≤ 0.5 秒なら M0・Md・Ml が 1 回ずつ（上の表）。発火は `Play` の中ではなく最初の `Tick`（`catchUpFireMarkers` はカーソル 0 のまま始めるだけ）。メッセージ形式・ネット送信数は不変（`DelayedNetworkRelay.EnqueuedMessageCount` のテストも妥当）
- **FX-R-01 の境界・異常値**: ちょうど 0.5 秒は新規（`<=`、docs と一致）・負と未同期は 0 に丸めて最初から・尺以上は復元しない・Host 引き継ぎの `ResetNetworkedState` と `NgoNetBridge` の切断時のキュー破棄で古い `StartNetTime` が残らない
- **FX-R-03**: 写しの再入（入れ子の `Tick` は `_inTick` で無視、`StopAll` / `CancelAllNetworked` は別の配列）・世代つき Handle で再利用された枠を誤認しない・Tick 中の `Play` は次の Tick から・`_events.Tick` の購読者が自分を止めた場合も `Done` で `Complete` に進まない・定常経路の割り当てなし
- **FX-R-02 の分岐の一貫性**: 新規は Lit・既存 + 欠けは Shader / Specific を保つ・既存 + 知らないシェーダー + 非対話 `Ask` は保つ・ダイアログの「変換」は上書き、が `MigrateCore` の 1 か所に揃っている。`MayaMaterialImporter` の既存経路は「未設定（null かつ参照 ID 無し）」のときだけ埋める。`HasMissingShaderReference` は Editor アセンブリ
- **FX-R-04**: 「先頭から」の判定が更新の間隔に依存しない・巻き戻って始まった場合も `Collect` でカーソルが 0 に戻るので `[0, elapsed]` を 1 回ずつ・ループ・停止中のフレーム送り・プレビュー対象の切り替え（新しいセッションは `LastTime = director.time` で始まるので、再生中の Director に切り替えても誤発火しない）
- **FX-R-06**: `Task.Run` なので主スレッドの `Wait(1500)` でデッドロックしない・台帳は lock・`KillAllRunning` は Unity API を使わない・`OnDisable` で状態を戻すので開き直して操作できる
- **FX-R-07 / 08**: 未選択時の他ボタンへの影響なし・既存タグ（`v1.0.0`〜`v1.3.1`）・`v` 無し・大文字 `V`・ゼロ埋めは従来どおり読む
- **FX-R-10**: 実行時の挙動は不変（コメントとテスト名だけ）。[07] の (A) / (B) の表は `UiManager` の担当表の順序と一致
- **FX-R-14**: v1.3.1 で動いていたものは止まらない（変更は v1.4.0 新規の `CutsceneImportListeners.Discover` だけ。D-Drive 自身の internal ハンドラは発見に頼らない。Migration / Validator の発見規則は §5.14 の対象外のまま不変）
- **互換スナップショット**: `git diff 9f40cbb..84269f4 -- …/Snapshots/` は `+120 / −0`（v1.3.1 の行の削除・変更 0 件）。本ラウンドはスナップショットを変えていない
- **マニュアル**: 本ラウンドで変わった DesignerManual / ProgrammerManual / consumer guide に「以前」「従来」等の比較の記述は無い（差分の追加行を確認）
- **docs/53 / 54 の訂正注記・docs/07 の 2 規則の表・docs/52 の手順**: 実装と一致（例外は FY-R-01 の手順 4 の期待結果に Common が無いこと、FY-R-02 の 4-5 が Host 送信だけのこと）

---

## v1.3.1 → 現在（`84269f4`）の「挙動の変更」一覧と CHANGELOG との照合

[55] の一覧からの差分（本ラウンドで変わった行・新しく生じた行）に限る。

| # | v1.3.1 から見た挙動の変更 | 区分（本レビューの見立て） | CHANGELOG `[Unreleased]` 互換性節 |
|---|---|---|---|
| 1 | ローカルの `Play` で 0 秒のマーカー（全種別）が最初の Tick で 1 回発火 | MINOR | あり（[55] から変わらず） |
| 2 | **ネット受信側も、開始位置 ≤ 0.5 秒なら `[0, 開始位置]` のマーカーを最初の Tick で 1 回ずつ発火。超えたら全部無音** | MINOR（既存データの結果が変わる） | **あり**（MINOR）。**漏れ**: Client 送信が 2 区間で届くこと・全か無かで Presentation の猶予と規則が違うこと（FY-R-02） |
| 2' | 開始から 0.5 秒以内に届いた Late Join の再送で、開始直後のマーカーが鳴る | MINOR の一部 | あり（docs/14 §21・E-20・docs/26。CHANGELOG は「0.5 秒を超える途中参加は無音」の裏として読める） |
| 2'' | 予測再生をローカルで止めた（シーンのアンロード）直後に自分のメッセージが戻ると、開始直後のマーカーがもう一度鳴る | 不具合の幅が広がった（稀） | なし（記載不要の範囲。直すなら `StopAll` で止めた `HandleNetKey` を短時間覚えて無視する） |
| 3 | Edit Mode の「先頭から」は停止中の位置で判定（0.1 秒の許容は廃止） | Editor | あり |
| 4 | Signal / Event の購読者がカットシーンを止めたら同じ Tick の残りを呼ばない・他を止めても二重に進まない・`StopAll` / `CancelAllNetworked` が範囲外にならない | PATCH（不具合修正） | あり（「変更なし(Runtime の不具合修正)」の項に追記） |
| 5 | 元の `.mat` が標準シェーダーに戻っていれば、既存 Data の知らないシェーダーも `DDrive/Lit` へ変換（保つのは元も知らないシェーダーのときだけ） | PATCH 相当（Editor） | あり |
| 6 | シェーダーが欠けた `.mat` の再生成で、既存 Data の Shader / Specific を書き換えない（警告） | PATCH 相当（Editor） | あり。**ただし Common は上書きされうる**（FY-R-01。CHANGELOG・警告文とも Common に触れていない） |
| 7 | 更新ウィンドウ: 認証の対話プロンプトを出さない | Editor（体感が変わる） | **あり**（[55] の漏れ FX-R-11 は解消） |
| 8 | 更新ウィンドウ: プレリリースだけのとき「更新先の版」は未選択 | Editor | あり |
| 9 | 更新ウィンドウ: `v1.5` / `v1.5.0.1` のような 3 区間でないタグを版として読まない（v1.3.1 の P-14 は 2〜4 区間を読んでいた） | Editor | あり（PC-R-02 の項と「変更なし(Editor の更新ウィンドウ…)」の項の両方） |
| 10 | 更新ウィンドウ: 読み切り 5 秒の上限・子孫プロセスの停止・リロード / 終了 / 閉じたときの git 停止 | Editor | あり（読み切り超過時に子孫を止められない点は記述と違う = FY-R-04、軽微） |
| 11 | `ICutsceneImportListener` の発見を public のみ | 変更なし（v1.3.1 に無い） | あり |

MS2026 が踏みそうなもの: **#2**（Client が起こすカットシーンの開始直後のマーカーが、遅い回線の他 Client でだけ鳴らない）、**#6**（T-Drive の解決失敗中の再生成で MaterialData の色・テクスチャが消える可能性）、**#7**（更新チェックの失敗の仕方）。

---

## v1.4.0 のタグを打ってよいかの所見

**P1 は無く、互換面（スナップショット・メッセージ形式・公開 API）は追加のみなので、コード面ではタグを止める理由は無い**。ただし次の 2 つは、タグの前に「直す」か「書いて受け入れる」かを決めておく方が安い（どちらも E-20 / 既存データの扱いとして v1.4.0 で固定されるため）:

1. **FY-R-01**（欠けたシェーダーの間の再生成で既存 Data の Common が上書きされうる）: 小さい修正（`MigrateCore` で既存を先に探して触らない）+ テスト 1 件。直さないなら CHANGELOG・警告文・[52] 15.6 手順 4 の期待結果を「Common は元の `.mat` から読み直す（欠けている間は既定値になりうる）」に直す。
2. **FY-R-02**（受信側の追いつき発火の規則）: Presentation と同じ滑る窓にするか、全か無かのまま 2 区間の注意を docs に書くか。どちらでも Client 送信のテストを足す。

v1.4.0 に入れるのが望ましいがタグを止めない: **FY-R-03**（`PresentationManager` の同形。v1.3.1 から既存）。残りの P3（FY-R-04〜07）は v1.4.x で可。

**タグの前に残る人による確認・実機確認**（自動テストでは見られない、または本レビューで推定のまま残ったもの）:

- **ネット 2 台 → できれば 3〜4 台（[52] 4-5、[29] の流儀）**: (a) Host の操作で 0 秒・0.1 秒に Signal / SE を置いた Cosmetic のカットシーン（PredictLocal 有効）→ Host と Client の両方で 1 回ずつ鳴る（遅延 0ms / 200ms）。(b) **Client の操作で再生し、もう 1 台の Client で鳴るか**（Client → Host → Client。遅延 200ms でも鳴るか、`NetDebugOverlay` 等で開始位置が 0.5 秒にどれだけ近いかを記録する）。(c) 開始から 0.5 秒より後に途中参加した端末では開始直後のマーカーが鳴らない。(d) `host_migration` シナリオ（[29] §25）の後に同じカットシーンを再生し、二重に鳴らない。
- **[52] 15.6 手順 4**: **実データの複製**で行い、Shader 欄・固有に加えて **Common（色・テクスチャ）も変わらないか**を見る（FY-R-01 を直していない場合は変わる可能性がある）。パッケージを戻した後の見た目も確認する。
- **[52] 4-4（Edit Mode）**: 0 に戻して再生を数回、0.05 秒へスクラブして再生。加えて、2.0 秒付近で一時停止して直後（1 フレーム以内）に置いたマーカーが再開時に鳴るか（FY-R-05。鳴らないのが現状の挙動）。
- **[43] 15-17a**: 資格情報の切れた private リポジトリで GCM の画面が出ずに警告で終わる・確認中の再コンパイルで `git` が残らない・開き直して操作できる（Windows。非 Windows の `pgrep -P` 経路は未検証のまま）。
- **[43] 16-25**: 重なる登録と正しい形で、どちらの行が効くか（FX-R-10 の (A) / (B)）。
- EditMode / PlayMode の全件 green（対応記録の 1528 / 918 は本レビューでは未確認）。

---

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・実 `git`・実ネットワーク・実描画・Timeline ウィンドウの実操作）。特に FY-R-01 の「欠けたシェーダーの Material からプロパティ / テクスチャ名が読めない」、FY-R-02 の実機の開始位置（NGO の ServerTime の推定誤差・ティックのまとめ送り）、FY-R-03 の UniTask の続きが同期で走る点、FY-R-04 の子孫・スレッドの残り方は推定のまま
- `UpdateWindow` は本ラウンドの差分（`OnDisable`・更新チェックの選択・適用ボタン）のみ。`GitPackageJsonFetcher` / `GitCliTagLister` は [55] からの変更なしを確認しただけ
- `AudioManager` / `VfxManager` / `AnimManager` / `MaterialManager` の完了掃除の走査が購読者を呼ぶ形か（FY-R-03 の横展開）は未確認
- SpecWeb の再生成物（`Tools/SpecWeb/html/manual/*`）は対象外（正本の `docs/DesignerManual` / `docs/ProgrammerManual` の差分の文言のみ確認）
- T-Drive 側のコード・実際の Toon シェーダーは無く、相性は docs と [51] で判断した
