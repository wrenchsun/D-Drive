# 38. 受け入れデモ（6-8、要件 §7 成功基準の 5 項目）

> 2026-09-15 ユーザー決定（[11_tasks.md](11_tasks.md) Phase 6 冒頭）: **6-8 の実施とリード承認は人の作業**。エージェント（Claude）が用意するのは (1) 誰がやっても同じ結果になる手順書（本書）、(2) デモ前の事前確認チェックリスト、(3) 既存の証跡へのリンクとまだ無い証跡の一覧、まで。
> 最終的な合格判定（リード承認）は本書末尾の記入欄に人が記録する。
> [37_manual_verification_phase6.md](37_manual_verification_phase6.md) 「実装中・これから」6-8 節はこの docs へのリンクに置き換えた。

## 0. 進め方

### 所要時間の目安

| 項目 | 所要時間 | 備考 |
|---|---|---|
| ③ Validation（CI 代替） | 20分 | 意図的な参照欠落・ID 重複の再現込み |
| ④ AssetBrowser の使用箇所・依存関係 | 15分 | 既存確認済み機能の再演（docs/28 参照） |
| ① ID だけのモック → デザイナーが中身を埋める | 20分 | 一時的なコード書き換え 1 行を含む |
| ② 剣攻撃 Presentation の調整 | 15分 | コード変更なしで見た目を変える |
| ⑤ 2 クライアント + サーバー同期再生 | 45分 | ビルド作成・配布・実機（または PC-A/PC-B）起動を含む。§0 事前確認チェックリストの v5 が済んでいれば、その結果をそのまま証跡として使ってよく、この時間は「再演＋確認」だけで済む |
| 合計 | 約 115分（1時間55分） | 判定記入・雑談を除く目安 |

### 準備物

- Unity 6000.3.13f1 が起動できる PC（① 〜④）
- ⑤ のみ: [29_network_device_test.md](29_network_device_test.md) の PC-A（Host）+ PC-B（Client）環境、またはこの PC 上でのループバック（127.0.0.1）2 プロセス結合確認（実機が用意できない場合の代替。§2-⑤参照）
- Unity Editor を閉じた状態で実行する `Tools\CI\run-ci.cmd`（③ の CI 代替手順で使用。Editor 起動中は使えない）
- git の作業ツリーがきれいな状態（① と③ で一時的な変更を作り、デモ後に必ず戻すため。`git status` で事前確認）

### 当日の進め方

1. まず①→④→③（Unity 単体で完結する 3 項目）を順に行う。操作するのは基本的に 1 人でよい（プログラマー役・デザイナー役を演じ分ける）
2. 次に②（Presentation の調整。デザイナー役の操作を中心に）
3. 最後に⑤（ネットワーク。最も準備が重い。事前に §3 の v5 確認が済んでいれば結果を提示するだけで良い）
4. 各項目の完了ごとに、下記の記入欄にリードがその場で判定を記録する（後回しにしない）

### 合格判定の記入欄

| 項目 | 合格/不合格/条件付き | 判定者 | 日付 | メモ |
|---|---|---|---|---|
| ① プログラマーがアセット 0 件でモック → デザイナーが ID の中身を埋める | | | | |
| ② 「剣攻撃」Presentation をコード変更なしで調整 | | | | |
| ③ Validation（CI 代替）で参照欠落・ID 重複を検出 | | | | |
| ④ AssetBrowser から使用箇所・依存関係を辿る | | | | |
| ⑤ 2 クライアント + サーバーの同期再生（遅延 200ms・Late Join 復元） | | | | |

---

## 1. 要件 §7 成功基準（原文、[00_requirements.md](00_requirements.md) §7）

> 1. プログラマーがアセット 0 件の状態でモックシーンを実装し、後からデザイナーが ID の中身を埋めるだけで演出が完成するデモが通る
> 2. 「剣攻撃」Presentation（Anim+SE+VFX+HitStop+CameraShake）をコード変更なしでデザイナーが調整できる
> 3. Validation を CI で回し、参照欠落・ID 重複が検出できる
> 4. AssetBrowser から任意アセットの使用箇所・依存関係が辿れる
> 5. 2 クライアント + サーバー構成で剣攻撃 Presentation が同期再生され（遅延 200ms でも位相一致）、途中参加者にも常駐演出が復元される
>
> **マルチプレイは v1 の必須要件であり、将来拡張ではない。FR-13 の全項目（NGO アダプタ・Cosmetic/Simulated 配送・同期再生・Late Join・ContentHash 照合）を v1 スコープに含む。**

以下、①〜⑤ の番号は上記の項番に対応する。

---

## 2. 項目ごとの手順

### ① プログラマーがアセット 0 件でモック → デザイナーが ID の中身を埋める

**目的**: FR-1.4（未登録 ID は警告 + Placeholder）と要件§7-1 を、実際のコード・実際のエディタ操作で確認する。

**前提**:
- サンプルコード: [`Assets/DDrive/Samples/PresentationSkillSlashDemo.cs`](../Assets/DDrive/Samples/PresentationSkillSlashDemo.cs)（5-1 の AC 確認用に既存。`Presentation.Play(id, ctx)` の 1 行だけで再生する実演コード）
- 確認用シーン: [`Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity`](../Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity)（上記コンポーネントが配置済み）
- 既存の `PRES_Demo_SkillSlash.asset`（`PresentationSkillSlashDemo.cs` が参照する本番デモアセット）は既にトラックが埋まっており、かつ 6-0 のネット確認等で他のデモが依存しているため、**このデモのために空にしない**（他の確認・実機テストが壊れる）。代わりに、デモ専用の**新規・空の Presentation ID** を 1 つ作って使う

**手順**:
1. `Tools > D-Drive > Asset Browser` → Presentation タブ → 「＋新規作成」で表示名 `AcceptanceDemo_Sword`（トラック 0 件のまま保存。カテゴリ・タグは任意、`Tags` に `Demo` を付けておくと後で見つけやすい）
2. `Tools > D-Drive > Generate > Regenerate Asset IDs` を実行し、`Assets/Generated/AssetIds.g.cs` に `PRESENTID.AcceptanceDemoSword`（実際の名前は手順1の表示名から生成される）が追加されたことを確認
3. `PresentationSkillSlashDemo.cs` の `SkillSlashId` の初期化式（1 行）を、手順1で作った ID の値に**一時的に**書き換える（このデモ専用の一時的な改変。デモ後に必ず `git checkout -- Assets/DDrive/Samples/PresentationSkillSlashDemo.cs` で元に戻す。実際のゲームコードでは `Presentation.Play(PRESENTID.AcceptanceDemoSword, ctx)` の 1 行で済むことを説明する。このサンプルが生の `AssetId` を組み立てているのは asmdef 分離の都合であり意味は同じ）
4. `PresentationSkillSlashPreviewScene.unity` を開き Play Mode → `P` キー（`PresentationSkillSlashDemo.Play()`）を押す
5. **期待される結果（Placeholder 状態）**: コンソールに `Unregistered AssetId ... resolved to Placeholder.` 相当の警告が出て、画面・音に変化がないこと（Placeholder = 無音・非表示の代役なので、このケースでは「何も起きない」のが正しい動作）。例外で停止しないこと（CLAUDE.md §0-4）も確認する
6. Play Mode を止め、AssetBrowser で `AcceptanceDemo_Sword` を開き、PresentationEditor で既存サンプル（例: `SE_Player_Slash`/`VFX_Player_Slash`）を 1〜2 トラック追加して保存
7. **コードは変更せず**に Play Mode を再実行 → 同じ `P` キーで、今度は実際に SE/VFX が再生されることを確認（要件の核心: 手順3以降コード修正は 0 のまま演出が完成する）

**期待される結果**: 手順5（Placeholder で無音・非表示）→ 手順7（同じ呼び出しで実際に再生）の対比が確認できる。

**証跡**: 手順5・7 のスクリーンショット（Game ビュー + Console）、手順5 の警告ログのテキストコピー。

**既存の証跡**: `PresentationSkillSlashDemo.cs` 自体・Placeholder 機構（`AssetIdLookup`/`ResolveOrPlaceholder`、[02_core_framework.md](02_core_framework.md)）は 5-1 で実装・EditMode/PlayMode テストで確認済み。ただし①の**この形（新規空 ID → Placeholder → デザイナーが埋める → 同じコードで本物になる、を通しでデモする）は未取得**（今回が初回）。

**未達・懸念**: 手順3の一時的なコード書き換えは、デモ後に確実に戻す運用（`git checkout`）に依存する。デモ用の `AcceptanceDemo_Sword` はデモ後に安全な削除（またはアーカイブタグ）で片付ける。

**後片付け**: `git checkout -- Assets/DDrive/Samples/PresentationSkillSlashDemo.cs`、`AcceptanceDemo_Sword` を AssetBrowser の「削除...」（安全な削除）でアーカイブ or 削除。

---

### ② 「剣攻撃」Presentation をコード変更なしで調整

**目的**: FR-7.1〜7.4・要件§7-2。Anim+SE+VFX+HitStop+CameraShake の統合演出を、デザイナーがコード変更なしに調整できることを確認する。

**前提**: `Assets/GameData/Presentation/Demo/PRES_Demo_SkillSlash.asset`（既存の本番デモ、[08_presentation.md](08_presentation.md) §5 の運用例そのもの）、確認用シーン `PresentationSkillSlashPreviewScene.unity`、サンプルコード `PresentationSkillSlashDemo.cs`（① と同じだが、こちらは ID を書き換えない = 本来のデモ資産のまま使う）。

**手順**:
1. `PresentationSkillSlashPreviewScene.unity` を Play Mode で実行 → `P`（Play）→ `Space`（`Signal("hit")`）で一連の演出（Anim/SE/VFX/HitStop/CameraShake）が再生されることを確認する（現状のベースライン）
2. Play Mode を止め、AssetBrowser または Inspector の「エディターで開く」から `PRES_Demo_SkillSlash` を PresentationEditor で開く
3. 統合プレビュー（実 Manager 駆動、ADR-4）で任意のトラックを 1 つ調整する。例: VFX トラックの色/スケールパラメータ上書き（`Params`）を変える、`onHit` の `CameraShake` トラックの `ShakeId` を別の強さのものに差し替える、`HitStop` の秒数を変える
4. 保存（コードは一切変更しない）
5. 手順1 と同じ Play Mode 実行（`P`→`Space`）で、変更が**コード変更なしに**反映されていることを確認する

**期待される結果**: 手順3で変えたパラメータが、手順5の再生に反映される。ゲームコード（`PresentationSkillSlashDemo.cs`）・確認用シーンは無変更。

**証跡**: 手順1（変更前）と手順5（変更後）のスクリーンショット/画面録画、変更したトラックの Before/After（PresentationEditor のスクリーンショットでも可）。

**既存の証跡**: PresentationEditor の統合プレビュー自体は 5-1〜5-4 で実装・確認済み（[28_manual_verification_phase5.md] 該当節）。**「コード変更なしで調整できる」ことをこの形式で通しデモした記録は未取得**。

**未達・懸念**: 特になし。調整した内容は他のデモ・実機ネット確認（6-0/6-6）が参照する既存デモ資産でもあるため、**デモ後は変更を維持してよいか、それとも元に戻すかをその場で判断する**（変更が「良い調整」であれば残してよい。単なる動作確認用の変更なら Undo/`git checkout` で戻す）。

---

### ③ Validation（CI 代替）で参照欠落・ID 重複を検出

**目的**: FR-10.1/10.2/10.3・要件§7-3。

> **要件文言は「CI」だが、GitHub Actions のセルフホストランナー本稼働は P7 末に延期されている（[33_ci_setup.md](33_ci_setup.md)）。そのため本デモでは、CI と同じ検査をローカルで実行する `Tools\CI\run-ci.cmd`（`DDrive.Editor.CI.ValidateAll` を batchmode で呼ぶ）と、Unity Editor 上の `Tools > D-Drive > Validation > Run All` で代替する。この代替が受け入れ条件を満たすとみなせるかは §4 の確認事項。**

**前提**: git の作業ツリーがきれいな状態（`git status` で確認。手順中に一時的な変更を作るため）。

**手順（参照欠落）**:
1. 既存の任意の Data（例: `VfxData` 1 件）を Project ウィンドウで複製（Ctrl+D）し、ファイル名に `_AcceptanceDemoTmp` を付ける（本番 Data に触らない）
2. 複製した Data の Inspector で、必須参照フィールド（例: Prefab/Clip 参照）を空に書き換えて保存
3. `Tools > D-Drive > Validation > Run All` を実行 → 対象行に赤（Error）で参照欠落が表示されることを確認・スクリーンショット

**手順（ID 重複）**:
4. 手順1の複製 Data の `Id` フィールド（Inspector で直接編集可能な場合）を、既存の別 Data と同じ値に書き換える（Undo で戻せる範囲の操作。通常運用では ID はツールが発行するため人が重複させることはないが、検出ロジック自体の確認として意図的に作る）
5. `Tools > D-Drive > Validation > Run All` を再実行 → ID 重複が Error として一覧に出ることを確認・スクリーンショット

**手順（ローカル CI 実行での再現）**:
6. Unity Editor を閉じる
7. リポジトリ直下で `Tools\CI\run-ci.cmd` を実行し、`[1/5] Validation` ステップが Error 検出により非 0 終了になること（手順2/4 の状態を残したまま実行、または同じ状況を再現してから実行）を確認する
8. 手順2・4 の変更を破棄する（複製 Data を削除。手順4 のような ID 直接編集をしていた場合は Undo または `git checkout` で確実に戻す）
9. `Tools\CI\run-ci.cmd` を再実行し、`[1/5]` が Error 0 で通ることを確認する（後片付けの確認も兼ねる）

**期待される結果**: 手順3・5 で Validation ウィンドウに Error が表示される。手順7 で `run-ci.cmd` が非 0 終了、手順9 で green に戻る。

**証跡**: 手順3・5 のスクリーンショット、手順7・9 の `run-ci.cmd` コンソール出力コピー（または `TestResults/` 配下のログ）。

**既存の証跡**: Validation ウィンドウの実際の見え方（Warning/Info の実例）は [37_manual_verification_phase6.md](37_manual_verification_phase6.md) の「6-9」「A2」節で 2026-09-15 に確認済み。ただし **Error（参照欠落・ID 重複）を実際に発生させて検出させた記録、および `run-ci.cmd` を実行して非 0 終了を確認した記録は未取得**。

**未達・懸念**: CI（GitHub Actions）自体での実行は未検証（ランナー未登録、[33_ci_setup.md] §0）。ローカル代替の扱いは §4 の確認事項。

---

### ④ AssetBrowser から使用箇所・依存関係を辿る

**目的**: FR-8.2/8.4・要件§7-4。

**前提**: 依存関係グラフが構築済み（`Tools > D-Drive > Asset Browser` のツールバー、または未構築なら警告 + 「再構築」ボタンが出るのでその場で再構築する。[09_editor_tools.md](09_editor_tools.md) §10）。

**手順**:
1. `Tools > D-Drive > Asset Browser` を開く
2. 一覧から `VFX_Player_Slash`（または任意の、複数箇所から参照されているアセット）を選択 → 右クリック「使用箇所を表示」→ `UsagesWindow` に `PRES_Demo_SkillSlash` や `PresentationSkillSlashPreviewScene.unity` 等が一覧に出ることを確認し、ダブルクリックでジャンプできることを確認
3. 同じアセットで「依存関係ツリー」を開く → `DependencyTreeWindow` にツリー（例: Presentation → VFX → Prefab → Material → Shader のような多段の参照）が表示されることを確認
4. `Tools > D-Drive > 未使用アセット`（`UnusedAssetsWindow`）を開き、一覧が表示されること（0 件でも機能自体が動くことの確認で良い）を確認

**期待される結果**: 手順2・3・4 がいずれもエラーなく動作し、実際の参照関係が確認できる。

**証跡**: 手順2・3・4 のスクリーンショット 3 枚。

**既存の証跡**: この機能一式（使用箇所検索・依存関係ツリー・未使用検出）は 2026-09-14 に [28_manual_verification_phase5.md](28_manual_verification_phase5.md) §0 の「①アセットブラウザ系」（5-5/5-6/5-10/5-15 節）で人による確認済み（チェック済み）。本デモでは**同じ機能を受け入れデモの形式で再演するだけ**であり、新規リスクは低い。

**未達・懸念**: 特になし。

---

### ⑤ 2 クライアント + サーバー構成での同期再生（遅延 200ms・Late Join 復元）

**目的**: FR-13 全項目・要件§7-5。「マルチプレイは v1 必須要件」であることを踏まえ、実際の 2 ピア構成で確認する。

**前提**: [29_network_device_test.md](29_network_device_test.md) の PC-A（Host）+ PC-B（Client）実機環境（モバイルホットスポット経由）。実機が用意できない場合は同ドキュメント §7/§9〜§11 と同じ、このPC上でのループバック（127.0.0.1）2 プロセス結合確認で代替してよい（判定基準は同一）。

**手順**（[29_network_device_test.md] §5 の確認の流れに準拠）:
1. `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド`（`NetCheckBuilder.Build()`）で最新の `Builds/DDriveNetCheck.zip` を作る（6-6 の K2/K3 修正を含むビルドであることを確認する）
2. PC-A で Host を起動（Editor の Play Mode で `NetCheckScene` を開くか、ビルドを `-ddrive-net host` で起動）。PC-B（または 2 プロセス目）で Client を `-ddrive-net client -ddrive-host <PC-A の IP> -ddrive-port 7777` で起動
3. **遅延 0ms**: 接続後、`Player.log` の `heartbeat`/`signal_fire`/`signal_recv` を突き合わせ、位相差が「数ティック以内（目安 100ms 以内）」（[31_phase5_decisions.md] A8 の判定基準）であることを確認
4. **Late Join**: Client を Host の初回 Play より後に接続させ、接続直後の `activeCount` が Host の再生中の演出数を反映すること（0→N のように復元されること）を確認
5. **遅延 200ms**: Client を `-ddrive-sim-latency 200` で再起動し、`rtt_app_ms` が概ね 200ms 台になること、`track_fired` の `late_ms` が猶予（0.5 秒）以内で発火し画面に VFX が表示されることを確認
6. **通信停止からの復旧（K2/K3 の再確認、[29] §13）**: 200ms 遅延中に数秒間通信を止め（ホットスポットの電波が途切れるのを待つ、またはネットワークアダプタを一時無効化する）、復旧後に `rtt_app_ms` が固着せず実測値に戻ること（K2）、Signal が対応する Play より先に処理されて誤って捨てられないこと（K3、保留期限 1 秒以内に Play が届けば効果が発生すること）を確認
7. **切断**: Host を正常終了・強制終了の両方で試し、Client 側が切断を検知し、VFX が蓄積・残留しないこと（A9/A10 対応済み、[29] §12 参照）を確認

**期待される結果**: 手順3・5 で位相一致（数ティック以内）、手順4 で Late Join 復元、手順6 で K2/K3 の修正が実機/ローカル結合で機能する、手順7 で切断時に演出が正しく後片付けされる。

**証跡**: 各手順の `Player.log`/`PlayerHost.log` 抜粋、スクリーンショット、[29_network_device_test.md] への追記（v5 節として）。

**既存の証跡**: v1〜v3（[29] §8、2026-09-14）で接続・0ms/200ms 位相・Late Join・偽造メッセージ破棄・切断検知は合格。v4（[29] §12、2026-09-15）でループ VFX の蓄積・切断後残留も解消を確認済み（A10 対応後）。

**未達・懸念（重要）**: v4 は 6-6（K2/K3 修正、PR #64）より**前**のビルドで確認したものであり、**K2（通信停止中の `rtt_app_ms` 固着）・K3（Signal が Play より先に届くと未知キー破棄）の修正後の実機/ローカル結合確認（v5）はまだ実施していない**（[29_network_device_test.md] §13 に確認観点は明記済み、実施は未着手）。また **6-7（Loopback ⇔ NGO 両ブリッジでの 2 クライアント自動テスト）も未実装**のため、⑤の回帰を自動テストで継続的に保証する仕組みは無い。**受け入れデモの実施前に、可能であれば v5 を済ませておくことを推奨する**（§4 確認事項にも記載）。

---

## 3. 事前確認チェックリスト（デモ前に揃える）

デモ当日の前に、以下がすべて green/揃っていることを確認する。1 つでも欠けている場合、デモ自体は実施できるが、該当項目の判定は「条件付き合格」までにとどめ、欠けている証跡を後日追記する運用にする。

| # | チェック内容 | 状態（2026-09-15 時点） | 確認方法 |
|---|---|---|---|
| 1 | `Tools > D-Drive > Validation > Run All`（または `Tools\CI\run-ci.cmd` の `[1/5]`）が Error 0 | 未確認（Phase 6 分の一括確認は [37_manual_verification_phase6.md] の①節がまだ人による確認待ち） | Run All 実行 → Report Window で Error 0 を確認 |
| 2 | EditMode テスト全件 green | ✅ 実績: 707 件 green（2026-09-15 時点、各コミット時の isuzu MCP 確認による） | Test Runner（EditMode）または `run-ci.cmd` の `[3/5]` |
| 3 | PlayMode テスト全件 green | ✅ 実績: 637 件 green（2026-09-15 時点） | Test Runner（PlayMode）または `run-ci.cmd` の `[4/5]` |
| 4 | 性能テスト（6-2、`Performance` カテゴリ）green | 未確認（本書 6-2 節はコード実装済みだが人による Test Runner 実行はまだ、[37_manual_verification_phase6.md] 6-2 節） | Test Runner（PlayMode, カテゴリ `Performance`）または `run-ci.cmd` の `[5/5]` |
| 5 | 実機 v5（6-6 の K2/K3 修正後）合格 | **未実施**（§2-⑤「未達・懸念」参照） | [29_network_device_test.md] §13 の手順 |
| 6 | 6-7（2 クライアント自動テスト）green | **未実装**（[11_tasks.md] 6-7 行、AC 未達成） | 実装後、Test Runner または `run-ci.cmd` |
| 7 | デザイナーマニュアルが最新 | ✅ 6-3/6-4 反映済み（[37_manual_verification_phase6.md] 6-4 節。撮影リスト [36_manual_screenshot_list.md] のスクリーンショット挿入は別作業） | `docs/DesignerManual/` を目視、または生成コマンド `Tools/SpecWeb/tools/build-manual.js` の再実行結果 |
| 8 | Web 発注ツールが最新デプロイ | 未確認（別担当が並行作業中、[32_spec_web.md]/[28_manual_verification_phase5.md] 該当節） | デプロイ済み Web アプリを開き、バージョン/更新日時を確認 |

---

## 4. リードへの確認事項

1. **要件§7-3 の「CI」をローカル実行（`Tools\CI\run-ci.cmd` + Editor の `Validation > Run All`）で代替することを受け入れ条件の達成とみなしてよいか**（GitHub Actions セルフホストランナーの本稼働は P7 末に延期済み、[33_ci_setup.md]）。みなしてよい場合、本番 CI 導入後（P7 末）に改めて GitHub Actions 上での実行結果を追加証跡として残す運用でよいか
2. **要件§7-5 の「遅延 200ms でも位相一致」の判定基準**は [31_phase5_decisions.md] A8 の決定どおり「数ティック以内（目安 100ms 以内）」で確定してよいか（旧基準「RTT/2 以内」は実機で誤診断することが判明したための変更）
3. **⑤の実施タイミング**: 6-6（K2/K3 修正）後の v5 実機/ローカル結合確認がまだ済んでいない。受け入れデモの前に v5 を別途済ませておくか、受け入れデモ当日に v5 の確認も同時に行うか
4. **デモの実施者・日時**をいつにするか（① 〜④ は 1 人で完結、⑤ は PC-A/PC-B の 2 台または長めの準備時間が必要）
