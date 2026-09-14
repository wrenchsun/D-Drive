# 28. 実装確認手順書（Phase 5 自律作業分、2026-09-14〜）

> 2026-09-14 からユーザー指示で Claude Code が自律実装した Phase 5 の **人による確認項目**。
> 自動検証（isuzu MCP: コンパイル 0 エラー / EditMode / PlayMode テスト）は各コミット時に通しているが、**見た目・音・操作感・実機・外部サービス（Google スプレッドシート等）**は未確認。上から順にやれば全機能を一巡できる。
>
> 共通の前提: Unity 6000.3.13f1。確認はすべて SceneView / Game ビュー / 確認用シーンで行う（ウィンドウ内描画は廃止、[09] §2）。
> 不具合を見つけたら、該当チケット行（[11_tasks.md](11_tasks.md)）と該当設計書の「実装メモ」を参照して修正する。
> 各節の見出しは「チケット番号 + 名前（PR / コミット）」。自律作業中に判断を保留した事項は各節末尾の「要判断」に書く。

## 0. 確認の進め方（2026-09-14 まとめ）

> この節はオーケストレーターが後から追加した「一巡できる順番」の索引。各節本文（1〜13 の内容）は変更していない。
> 要判断の一覧（対応済みを除いた全件・優先度 A/B/C）は [31_phase5_decisions.md](31_phase5_decisions.md) にまとめた。確認しながら「これは要判断だったはず」と思ったら、まずそちらを見る。

### 事前準備（Unity を開く前に）

- `git pull` して最新の main を取得する（このドキュメント自体もその一部）
- `Library/` 配下の依存グラフキャッシュ（`Library/DDriveDeps/`）は Unity 起動後に手動で作り直す必要がある（5-5 の要判断どおり、起動時の自動再構築は無い）。①か⑤の最初に「依存関係グラフを再構築」を 1 回実行しておけば以降の節で使い回せる
- ゲームパッド（Xbox/PlayStation 系、USB か Bluetooth）を PC に接続しておく（③の 5-2b/5-2c、④は不要）。無い場合は「パッド未接続時に no-op / 案内が出ること」だけ確認すればよい（各節に明記済み）
- Google アカウント（自分の Google ドライブにテンプレートをアップロードできること）を②で使う
- 確認用シーンの場所: `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity`（5-1/5-2/5-2b/5-4 で使用）。5-2c/5-4 の確認用シーン（`CameraShakePreviewScene` 等）は各エディタのツールバー「確認用シーンを開く」から自動生成される
- ⑤のネット確認（実機 2 台）は基本的にオーケストレーターが担当する（[29_network_device_test.md](29_network_device_test.md)）。ユーザーが Unity 単体でできるのは自動テストの実行と、ローカル(127.0.0.1)結合確認の結果（[29] §7）を読むことだけ

### 確認順チェックリスト

エディターで自然に一巡できる順（チケット番号順ではない）。所要時間は目安（人による確認作業のみ。自動テスト実行時間は含まない）。

**① アセットブラウザ系**（2026-09-14 ユーザー確認済み）

- [x] 依存関係グラフの再構築 — [28 5-5節](28_manual_verification_phase5.md) — 5分 — 特になし（事前準備で済んでいれば省略可）
- [x] アイコン表示（AssetBrowser・Project ウィンドウ・Inspector の整合） — [28 5-10節](28_manual_verification_phase5.md) — 10分 — アイコンを割り当てた Data 数種類
- [x] 各エディタの「＋ 新規作成」（16 か所） — [28 5-15節](28_manual_verification_phase5.md) — 20分 — 特になし
- [x] インポート検知による Data 自動生成（9 種別 + 二重生成なし + 欠落表示 + 手動フォールバック） — [28 5-11節](28_manual_verification_phase5.md) — 30分 — 確認用の音声/画像/FBX/anim/Prefab 素材一式（無ければ既存アセットの複製で代用可）。**種別フォルダ（`Se`/`Bgm`/…）の下に置く。`SourceAssets` の直下や、種別フォルダの上に別フォルダを挟むと対象外**
- [x] 使用箇所検索 / 未使用検出 / 安全な削除(2026-09-14 から UE 風の削除確認ウィンドウに変更。複数選択・参照の差し替え・強制削除・結果画面を含む) — [28 5-6節](28_manual_verification_phase5.md) — 35分 — OS のゴミ箱からの復元手順を試すため一時的に削除して良い Data、置き換え先に使える同種別の Data 2種類以上
- [x] 再生ボタン横の「マニュアル」ボタン（2026-09-14、PR #45） — [09_editor_tools.md §6.1](09_editor_tools.md) — 5分 — 特になし。① Unity 画面上部の再生ボタンの右隣に「？ マニュアル」ボタンとページ一覧の ▼ がある（見当たらなければツールバーの右クリックメニューで「D-Drive/Manual」が表示対象になっているか確認） ② ボタンでブラウザにマニュアルのトップが開く（仕様書の `HumanAppUrl` 未設定ならローカルの `docs/DesignerManual`、設定済みなら Web 版。Web 版はマニュアル配信の反映後） ③ ▼ から任意のページ（例: アセットブラウザ）が直接開く ④ ▼ の「ローカルのマニュアルを開く」は常にローカルが開く ⑤ メニュー `Tools/D-Drive/マニュアルを開く` でも開く
- [x] 一覧のダブルクリックで専用エディタを開く（2026-09-14） — [09_editor_tools.md §1](09_editor_tools.md) — 10分 — VfxData 等(単一候補)・MaterialData/SliderSkinData 等(複数候補)・専用エディタの無い種別が混在する一覧。① ダブルクリック(または選択して Enter)で専用エディタが開き、対象アセットがセットされている(Inspector の「エディターで開く」ボタンを押したときと同じ状態になる) ② 専用エディタが無い種類は従来どおり Inspector で選択され、Project ウィンドウの実ファイルがハイライトされるだけ ③ 複数候補がある種類(Material/Slider Skin)は右クリックメニューの「エディターで開く」がサブメニューになり、全候補(Material Editor / 変換 / プレビュー等)を選べる。ダブルクリックでは既定(サブメニューの一番上と同じ)が開くことを確認

**② 仕様書系（2026-09-14 追記: HTML 仕様書 Web アプリへ移行。[32_spec_web.md](32_spec_web.md)）**

> 旧方式（Google スプレッドシート、5-12〜5-16）の確認手順は本節の下（歴史的経緯として残す）。
> **これから確認する場合は下記の「W-9〜W-12 仕様書 Web 化(HTML)の確認」を先に読むこと**。
> 旧方式のシート運用は行っていないため、5-12〜5-16 の手順は実施不要（Web アプリのデプロイが
> 済んでいない現状は W-9〜W-12 の節にある「未確認」項目を確認できないところまでで一巡とする）。

- [ ] 仕様書 Web アプリのデプロイ・トークン発行・D-Drive 側の設定（`WebAppUrl`/`HumanAppUrl`/トークン） — [28 W-9〜W-12節](28_manual_verification_phase5.md) — 30分 — Google アカウント、`clasp`（`Tools/SpecWeb/README.md`）
- [ ] 取得 → 差分プレビュー → Placeholder 作成（アセット・調整値〔スカラー・テーブル・enum〕） — [28 W-9〜W-12節](28_manual_verification_phase5.md) — 20分 — ②のデプロイ済み Web アプリ
- [ ] `Specs/assets.json`/`Specs/tuning.json` の diff 確認 — [28 W-9〜W-12節](28_manual_verification_phase5.md) — 5分 — 特になし（同期後でよい）
- [ ] D-Drive → Web 送信（選択肢・実状態・調整値使用状況）+ Web の実状態バッジ確認 — [28 W-9〜W-12節](28_manual_verification_phase5.md) — 10分 — 書き込みトークン
- [ ] マニュアル配信（デザイナーマニュアルを Web からも開ける・Unity の「マニュアル」ボタンから
  `?page=manual&p=...` で開く、2026-09-14 追加） — [32_spec_web.md「実装メモ（マニュアル配信）」の
  「目視確認」節](32_spec_web.md#実装メモ2026-09-14マニュアル配信) — 15分 — ①のデプロイ済み Web アプリ、
  `Tools/SpecWeb/push.ps1` 実行済み（`html/manual/*.html` を最新化してから `clasp push`）。
  Node テストでは iframe のナビゲーション（`google.script.history`・`<base target="_top">` 回避）を
  検証できないため、この目視確認が唯一の検証手段
- [ ] 発注ツール: ファイル形式/ファイル名（O-12）+ 発注リンクのコピー（O-13、2026-09-14 追加） —
  [28 O-12〜O-13節](28_manual_verification_phase5.md#o-12o-13-発注ツール-ファイル形式ファイル名--発注リンクのコピー2026-09-14-実装) —
  15分 — ①②のデプロイ済み Web アプリ。**コピーした URL を別タブで開くと該当の発注/発注グループの
  詳細が表示されることの確認が必須**（O-13 の唯一の目視検証手段）
- [ ] 発注ツール: 緊急修正（フォーカス外れ・コピーメニュー展開・連打対策・削除機能・
  マニュアルの白画面、2026-09-14 追加） —
  [28 緊急修正節](28_manual_verification_phase5.md#緊急修正-フォーカス外れコピーメニューコピーメニュー展開連打対策削除機能マニュアルの白画面2026-09-14-実装) —
  20分 — ①②のデプロイ済み Web アプリ、`push.ps1` 実行済み。**iframe 内の実際のクリック挙動・
  レイテンシは Node テストで再現できないため、この目視確認が唯一の検証手段**
- [ ] 発注ツール: 発注後の編集・識別子リネーム + メモの可読性向上・書式ツールバー
  （O-15〜O-16、2026-09-14 追加） —
  [28 O-15〜O-16節](28_manual_verification_phase5.md#o-15o-16-発注ツール-発注後の編集リネーム--メモの可読性向上2026-09-14-実装) —
  25分 — ①②のデプロイ済み Web アプリ、`push.ps1` 実行済み。**書式ツールバーの選択あり/なしの
  挙動・textarea のフォーカス保持は Node テストで再現できないため、この目視確認が唯一の検証手段**
- [ ] 発注ツール: 発注ツリー/私の発注の「編集」導線の再修正 + 一覧・各行からの削除・
  一括削除（2026-09-14 追加） —
  [28 O-15〜O-16節の追補](28_manual_verification_phase5.md#追補2026-09-14実デプロイで見つかった不具合の修正-o-15-導線の再修正--削除機能の拡充) —
  15分 — ①②のデプロイ済み Web アプリ、`push.ps1` 実行済み。**iframe 内の実際の画面遷移
  （`google.script.history`）の挙動は Node テストで再現できないため、この目視確認が唯一の検証手段**
- [ ] （旧方式・参考）仕様書テンプレート — [28 5-12節](28_manual_verification_phase5.md) — 実施不要
- [ ] （旧方式・参考）仕様書同期（スプレッドシート） — [28 5-13節](28_manual_verification_phase5.md) — 実施不要
- [ ] （旧方式・参考）仕様書リンク — [28 5-14節](28_manual_verification_phase5.md) — 実施不要
- [ ] （旧方式・参考）新規作成ダイアログ「仕様書から選ぶ」 — [28 5-16節](28_manual_verification_phase5.md) — 実施不要

**③ 演出系（揺れ・振動 → Presentation 基盤 → Presentation エディタ）**

- [ ] 揺れ・振動エディタ（5-2c、プリセット・波形・Test on Pad） — [28 5-2c節](28_manual_verification_phase5.md) — 20分 — ゲームパッド
- [ ] Presentation 基盤（5-1、剣攻撃デモの Play/Signal/Cancel/再実行） — [28 5-1節](28_manual_verification_phase5.md) — 10分 — 特になし
- [ ] カメラシェイク（5-2、剣攻撃デモでの揺れ・オプション 0%・カメラ切替） — [28 5-2節](28_manual_verification_phase5.md) — 10分 — 特になし
- [ ] コントローラー振動（5-2b、剣攻撃デモ・スライダーのノッチ/端） — [28 5-2b節](28_manual_verification_phase5.md) — 15分 — ゲームパッド
- [ ] Presentation エディタ（5-4、★目玉機能。D&D・統合プレビュー・Signal 発火・HitStop 連動） — [28 5-4節](28_manual_verification_phase5.md) — 25分 — ゲームパッド（Haptic トラックがあるデータを再生する場合）

**④ ロード画面**

- [ ] Preload 自動集計 + シーンロード統合（5-7、集計・重複なし・ロード画面・未登録 ID スキップ・ビルド前フック） — [28 5-7節](28_manual_verification_phase5.md) — 20分 — 開発ビルドを 1 回試す場合は時間に余裕を

**⑤ ネット（今確認できる範囲）**

- [ ] Presentation ネット再生・Late Join の PlayMode 自動テスト（5-8/5-9。Test Runner または isuzu MCP の `test_run`） — [28 5-8節](28_manual_verification_phase5.md) / [28 5-9節](28_manual_verification_phase5.md) — 10分 — 特になし
- [ ] ローカル(127.0.0.1)2 プロセス結合確認の結果を読む（6-0、既にオーケストレーターが実施済み） — [29 §7](29_network_device_test.md) — 5分 — 特になし（結果を読むだけ）
- [ ] 実機 2 台での確認（6-0 のチェックリスト本体） — [28 6-0節](28_manual_verification_phase5.md) / [29 §1〜§5](29_network_device_test.md) — オーケストレーターが担当中 — PC-B・モバイルホットスポット等（[29] §1〜§2）

合計 19 項目、人による作業の所要時間の目安合計は **約 4〜5 時間**（⑤の実機 2 台確認を除く。ゲームパッド・Google アカウントの準備込み）。1 回で終わらせる必要はなく、①→②→③→④→⑤ の単位で分割してよい。

### 確認後の後片付け（まとめ）

各節に散らばっている「確認が終わったら削除する/元に戻す」対象を一覧にする。**チェックが済んだ節の後片付けは、その場で（次の節に進む前に）行うことを推奨**（後回しにすると何が確認用の一時データだったか分からなくなる）。

| 節 | 片付ける対象 |
|---|---|
| 5-11 | 確認用に各種別フォルダの下に作った `_Check` フォルダ（例: `Assets/SourceAssets/Se/_Check/`）と、`Assets/GameData/*/Check/` 配下の生成された Data 一式（Addressables エントリも含めて削除） |
| 5-6 | 手順7で Archive したタグを解除する（または実際に不要なら安全な削除の手順で片付ける）。手順9〜17で作った一時 Data・置き換え先用の Data・Scene 参照確認用の `SeEmitter`（ゴミ箱からの復元テストが済んでいれば復元後のファイルも含む）。手順17の後は Validation を再実行してコード参照エラーが実際に出ていないか（出ている場合は誤って本当に使われている ID を削除していないか）を確認する |
| 5-13 | 作成した `Assets/GameData/Audio/SE/Player/SE_Player_Check1.asset`（存在すれば）と `Assets/GameData/Settings/DDriveSpecSettings.asset` / `DDriveTuningTable.asset`（コミットするかは [31_phase5_decisions.md](31_phase5_decisions.md) A5 を参照） |
| 5-16 | 作成した `SE_Player_Check5016.asset`（Addressables エントリも含めて削除）。手順9で空にした「スプレッドシート URL」設定は元に戻す |
| 5-7 | 確認で作った `SceneLoadingScreen` 付き GameObject・`DDriveRuntimeBootstrap`・手順6でテスト用に書き換えた `.asset` の中身（または確認用シーンごと破棄） |
| 5-2c | 特になし（確認用シーン `CameraShakePreviewScene` は残しておいて問題ない想定。気になる場合は削除してよい） |
| 5-4 | 確認用シーンに配置したモデル・Presentation Preview の残骸は「④閉じると残骸が消える」の確認項目自体が後片付け（手動で消す必要は無いはず） |

## 5-10 アイコン表示の拡張（PR #11）

対象: `Editor/AssetBrowser/AssetBrowserWindow.cs`（一覧行のアイコン）、`Editor/Inspector/AssetDataInspector.cs`（`RenderStaticPreview`）、`Editor/Inspector/AssetIconService.cs`（`ScaleForPreview`）。設計は [09_editor_tools.md](09_editor_tools.md) §8.2。

1. **事前準備**: Inspector のアイコン行（§8.1）で、Icon が未設定の Data がいくつかあれば「自動生成」または `Tools > D-Drive > Generate > 初期アイコンを生成(未設定の Data のみ)` を実行し、少なくとも数種類（例: Se, Vfx, Material, Texture）に Icon を割り当てておく
2. **AssetBrowser の一覧**: `Tools > D-Drive > Asset Browser` を開く → 一覧の各行の左端に小さなアイコン画像が出ていること
   - Icon を割り当てた Data: Inspector のアイコン行と同じ画像がミニチュアで出る
   - Icon 未設定の Data: 空白ではなく、Unity 既定のサムネイル（ScriptableObject のスクリプトアイコンなど）が出る（真っ黒・例外・Console エラーが出ないこと）
   - 種別フィルタや検索で絞り込んでもアイコンが正しく行に追従すること（スクロールして仮想化された行が再利用されても、別の行のアイコンが残らないこと）
3. **Project ウィンドウ（グリッド表示）**: `Assets/GameData/` 配下の適当なフォルダ（例: `Icons` を割り当てた Se や Material の Data がある場所）を Project ウィンドウで開き、表示をグリッド（アイコンサイズを中〜大にするアイコン表示）に切り替える
   - Icon を割り当てた Data アセットのサムネイルが、その Icon 画像になっていること（ズームで大きくしても大きく崩れすぎないこと。128px 前後の元画像を拡大するため多少の滲みは正常）
   - Icon 未設定の Data アセットは Unity 既定のアイコン（変化なし）のままであること
   - Inspector で Icon を「クリア」または別画像に変更した直後、Project ウィンドウのサムネイルが（選択し直す・少し待つなどで）新しい状態に追従すること
4. **Inspector との整合**: いずれかの Data を選択し、Inspector 最上部のアイコン行のサムネイルと、AssetBrowser 一覧・Project ウィンドウのサムネイルが同じ画像に見えること

要判断:
- 生成済みアイコンは 128〜512px 止まり（Inspector のサイズ選択に準拠）。Project ウィンドウを最大ズームにしたときの滲みが実用上気になるレベルか、デザイナーの目で確認してほしい（気になる場合は [09] §8.2 の要判断を参照して上限サイズや縮小方法を見直す）
- AssetBrowser 未設定時のフォールバックは Unity 既定のミニサムネイル（`AssetPreview.GetMiniThumbnail`）。種別ごとに分かりやすい代替アイコン（例: 種別ロゴ）にすべきかは今回判断せず据え置いた

## 5-11 インポート検知による Data 自動生成（PR #12）

対象: `Editor/Import/ImportRuleService.cs`（+`ImportRulePostprocessor.cs` / `IImportRuleHandler.cs` / `ImportRuleHandlers.cs`）、`Foundation/Data/AssetDataBase.cs`（`ImportSourceGuid` 追加）。設計は [09_editor_tools.md](09_editor_tools.md) §1.1 / [10_workflow.md](10_workflow.md) §3.3。

> **重要（2026-09-14 訂正）**: `ImportRuleService` は `Assets/SourceAssets/` の**直下 1 階層目のフォルダ名**だけを種別として見る（例: `Assets/SourceAssets/Se/...`）。種別フォルダの**上**に別のフォルダを挟む（例: 旧手順にあった `Assets/SourceAssets/_ImportRuleCheck/Se/...`）と 1 階層目が `_ImportRuleCheck` になってしまい、種別と一致せず**ログも出さずに何も起きない**。同様に `SourceAssets` の直下（1 階層目が無い）や、種別フォルダ名の綴り・大文字小文字が違う場合も対象外。以下は種別フォルダ（`Se`/`Bgm`/`Texture`/`Model`/`Anim`/`Anim2D`/`Prefab`/`Canvas`/`Vfx`）を `SourceAssets` の直下に置き、その下にカテゴリとして `_Check` フォルダを作る手順に直した（`_` はカテゴリのファイル名変換で除去されるため、生成される Data 側は `Check` という名前になる。詳細は [09_editor_tools.md](09_editor_tools.md) §1.1）。

事前準備: 種別フォルダ自体は `Assets/SourceAssets/` 直下に既定で用意されている（無ければ `Tools > D-Drive > Generate > SourceAssets の既定フォルダを作成` を実行する）。確認用ファイルは各種別フォルダの下に `_Check` サブフォルダを作って置く（実運用のカテゴリフォルダと混ざらないように）。

1. **Se**: `Assets/SourceAssets/Se/_Check/` に音声ファイル（.wav 等）を 1 つドラッグ＆ドロップで置く → 数秒後（Console に `[DDrive] ImportRule: ...` のログが出る）に `Assets/GameData/Audio/SE/Check/SE_Check_<ファイル名>.asset` が自動生成されていること。AssetBrowser で開き、Clips に置いた音声が入っていること
2. **Bgm**: 同様に `Assets/SourceAssets/Bgm/_Check/` に音声ファイルを置く → `Assets/GameData/Audio/BGM/Check/BGM_Check_<ファイル名>.asset` が生成され、LoopBody に音声が入っていること
3. **Texture**: `Assets/SourceAssets/Texture/_Check/` に画像ファイル（.png 等）を置く → `Assets/GameData/Texture/Check/TEX_Check_<ファイル名>.asset` が生成され、Texture に画像が入っていること
4. **Model**: `Assets/SourceAssets/Model/_Check/` に FBX を置く → `Assets/GameData/Model/Check/MODEL_Check_<ファイル名>.asset` が生成され、Prefab に FBX のルートが入っていること（同時に Maya→Material 経路で MaterialData/TextureData も生成されていれば正常な共存)
5. **Anim**: `Assets/SourceAssets/Anim/_Check/` に `.anim` ファイル（既存の AnimationClip をコピーするか、AnimEditor で作った物を配置）を置く → `Assets/GameData/Anim/Check/ANIM_Check_<ファイル名>.asset` が生成され、Clip が入っていること
6. **Anim2D**: `Assets/SourceAssets/Anim2D/_Check/` に `.anim` ファイルを置く → `Assets/GameData/Anim2D/Check/ANIM2D_Check_<ファイル名>.asset` が生成され、Clip が入っていること（Directions=None のまま。方向づけは Anim2DEditor で追加する）
7. **Prefab**: `Assets/SourceAssets/Prefab/_Check/` に Prefab を置く → `Assets/GameData/Prefab/Check/PREFAB_Check_<ファイル名>.asset` が生成され、Prefab が入っていること
8. **Canvas**: `Assets/SourceAssets/Canvas/_Check/` に UI Prefab を置く → `Assets/GameData/Canvas/Check/CANVAS_Check_<ファイル名>.asset` が生成され、Prefab が入っていること
9. **Vfx**: `Assets/SourceAssets/Vfx/_Check/` に ParticleSystem/VFX Graph の Prefab を置く → `Assets/GameData/Vfx/Check/VFX_Check_<ファイル名>.asset` が生成され、Prefab が入っていること
10. **二重生成しないこと**: 上記のいずれか 1 つを選び、そのファイルを右クリック →「Reimport」（または一度別プロジェクトへコピーして戻す）を行っても、対応する Data が増えず 1 個のままであること
11. **欠落表示**: 手順 1〜9 のいずれかで作った元ファイルを 1 つ削除する → 対応する Data 自体は消えずに残ること、AssetBrowser の ⚠ Validation（または `Tools > D-Drive > Validation > Run All`）でその Data が Error（「未設定(または Missing)です」）として表示されること
12. **ルールに合わない置き方をした場合の案内ログ**（2026-09-14 追加）: 種別フォルダの**直下**（例: `Assets/SourceAssets/Foo.wav`）や、種別フォルダ名を間違えた場合（例: `Assets/SourceAssets/se/...`）、対応外の拡張子（例: `Assets/SourceAssets/Se/_Check/memo.txt`）のファイルを置くと、Data は生成されないが Console に `[DDrive] ImportRule: ...` の警告ログが 1 回だけ出て、正しい置き場所を案内すること。同じファイルで何度もインポートが走っても警告が繰り返し出ないこと（セッション内でパスごとに 1 回）。`Assets/SourceAssets/Shaders/...` や `Assets/SourceAssets/Data/...`（Maya→Material 経路やサンプル資産が置かれている既知の非対象フォルダ）には警告が出ないこと
13. **AutoImport=OFF 相当の手動フォールバック**: 上記の確認用ファイル一式を一度削除し、別の場所に同じ構成のファイル一式を用意した状態で `Tools > D-Drive > Generate > SourceAssets からインポートルールを再実行` を実行 → Console にまとめて生成ログが出て、対応する Data が一括生成されること
14. 確認が終わったら、各種別フォルダの下に作った `_Check` フォルダ（`Assets/SourceAssets/<種別>/_Check/`）と生成された `Assets/GameData/**/Check/` 配下の Data 一式を Unity Editor から削除する（AssetBrowser の削除機能、または Project ウィンドウでフォルダを削除して Addressables のエントリも合わせて外す）

要判断:
- **Anim2D の元ファイルの解釈**: スプライトシート/Texture からの自動スライス(既存 Anim2DEditor のワークフローと重複)ではなく、`AnimData` と同じ「単一の `.anim`/`.fbx` を `Clip` に設定するだけの Placeholder」を採用した。方向づけ(`DirectionClips`)は既存の Anim2DEditor(3-11/3-12)で追加する運用。デザイナーの実際のワークフロー(スプライトから作ることが多いのか、既存クリップの流用が多いのか)によって、Texture フォルダ起点にすべきかどうかは要判断
- **Anim の複数テイク FBX**: 1 つの FBX に複数の `AnimationClip` が埋め込まれている場合、`ImportRule` は先頭 1 本(`__preview__` を除く)だけを取り込む。複数テイクを個別の `AnimData` に分けたい運用が多い場合は、ファイル単位でなくクリップ単位の複数生成へ拡張するか、テイクごとに FBX を分けて Export する運用にするかは要判断
- **Model の Prefab 直参照**: `ModelData.Prefab` に FBX のインポート直後のルート GameObject をそのまま設定する。Animator/追加コンポーネントを載せたラッパー Prefab を挟む運用がある場合、そのラッパー生成までは自動化していない(現状は ModelEditor 等で手動差し替え)
- **Texture の Usage/Channel 既定値**: `TextureImportProfile` の命名規約(`_N`/`_M`/`_UI` 等)に一致すればその既定値、一致しなければ `TextureData` のクラス既定値(Model/Albedo)のまま。UI 用テクスチャを規約に合わない名前で置いた場合は手動で Usage を直す必要がある
- **既存 Data と同名衝突時の挙動は未検証**: 手動で同じ識別子の Data を先に作っていた場合、`AssetCreationService.Create` が別ファイルとして作成する(既存の重複回避ロジックに委ねている)。運用上どちらが優先されるべきかは今回判断していない
- `AssetDataBase` に `[HideInInspector] string ImportSourceGuid` を追加した(シリアライズ形式の変更＝フィールド追加のみ。既存 Data は空文字で読み込まれ互換性に問題なし)。CLAUDE.md §0-9 の事前確認を自律作業中のため省略したので、問題があれば指摘してほしい

## W-9〜W-12 仕様書 Web 化(HTML)の確認（2026-09-14 実装）

対象: [32_spec_web.md](32_spec_web.md)（詳細設計・実装メモ）、`Assets/DDrive/Editor/Spec/SpecWebFetcher.cs`/
`SpecWebParser.cs`/`SpecSnapshotWriter.cs`/`SpecWebSender.cs`/`SpecSyncWindow.cs`、
`Assets/DDrive/Runtime/Tuning/TuningTable.cs`/`Tuning.cs`、`Tools/SpecWeb/`（GAS ソース）。
旧方式（5-12〜5-16、Google スプレッドシート）はこの方式に置き換わったため実施不要。

**前提（デプロイはユーザー作業）**: `Tools/SpecWeb/README.md` の手順で GAS プロジェクトを
デプロイし、①（人向け SPA）・②（D-Drive API）の 2 つの URL と、読み取り/書き込みトークンを
発行しておく。**この手順は本チケットでは未実施・未確認のまま引き継いでいる**（§9-4 の実機での
302 リダイレクト確認も含む）。

**追補（2026-09-14、トークンの送り方の変更）**: token は必ず POST の本文で送る方式に統一した
（[32] §7 追補）。GET(`doGet`)のクエリに token を付けても(有効/無効に関わらず)拒否される。
D-Drive 側は特に何もしなくても常に POST で送るため、下記の手順自体に変更は無い。
**実デプロイでの POST → 302 → 本文取得の確認もまだできていない**(ローカルの `HttpListener` での
再現テストのみ確認済み。§9-4 と同様に引き継ぐ)。

1. Unity で `Tools > D-Drive > 仕様書と同期` を開く。「Web API URL」に②の URL、
   「人向け SPA URL」に①の URL を貼り、「読み取りトークン」「書き込みトークン」を入力して
   「設定を保存」。トークンは伏せ字で入力されること
2. 「取得」を押す。① の Web アプリでアセット・調整値(スカラー・テーブル・enum)をいくつか
   登録してから取得すると、「新規」「変更」に反映されること
3. 「適用」を押す。選択した行が Placeholder として作成され、`調整値も同期する` が ON なら
   `TuningTable`(スカラー + テーブル)が更新されること。`Tools > D-Drive > Generate >
   Regenerate Tuning Keys` を実行し、`Assets/Generated/Tuning.g.cs` に `TUNING`/`TUNING_TABLE`/
   `TUNING_COLUMN` の定数が生成されること
4. repo ルートの `Specs/assets.json`・`Specs/tuning.json` が更新されていること。
   `git diff Specs/` で内容が読めること(キーがアルファベット順に整形されているため、
   実質的な変更が無い再取得では diff が空になることも確認する)
5. 「Web に送信」を押す。① の Web アプリのアセット詳細画面で「D-Drive 実状態」
   （作成済み/使用箇所数/最終同期時刻）が更新されて表示されること(v2 のダッシュボード表示は
   本チケットの範囲外のため、アセット詳細の実状態表示のみで確認する)
6. `isPlaceholder`(2026-09-14 追補で実値化。Clips/Clip 等の必須参照が未設定の Data は Validator の
   Error により `true` になり、Web の一覧で 🟡Placeholder バッジになること)・`hasIcon`(同追補。
   Inspector でアイコンを割り当てたアセットは Web の詳細画面に「アイコン: あり」と出ること)を
   確認する。（要判断・引き継ぎ）`iconAssetId`/`tags` は今も常に既定値（null/空配列）を送る
   （画像そのものの Drive アップロードと TagCatalog は未実装のため。[32_spec_web.md] §9 の
   要判断 13〜14 を参照して拡張する）

### 要判断（W-9〜W-12、2026-09-14）

- 上記「前提」のとおり、実デプロイでの動作確認(302 リダイレクト・トークンでの疎通)は
  本チケットの範囲内では実施できなかった。ユーザーがデプロイ URL・トークンを用意した後、
  最初にこの手順を通しで実行して確認してほしい
- `SpecFetcher`/`SpecCsv`/`SpecSheetParser`（旧・CSV 方式）は物理削除せず残っている
  （既存テストの CSV フィクスチャがそのまま使えるため。本番の同期経路からは呼ばれない）。
  削除してよいか、削除する場合はテストの移行が必要になる点はユーザー判断を仰ぎたい

## O-12〜O-13 発注ツール: ファイル形式/ファイル名 + 発注リンクのコピー（2026-09-14 実装）

対象: [32_spec_web.md](32_spec_web.md)（§10.2.1 追記・実装メモ「O-12 ファイル形式・ファイル名」
「O-13 発注リンクのコピー」）、[11_tasks.md](11_tasks.md) O-12/O-13、`Tools/SpecWeb/`（GAS ソース。
D-Drive 側の C# は変更していない）。前提は W-9〜W-12 と同じ（①②のデプロイ URL・トークンが必要）。

1. `push.ps1` で①②を再デプロイする
2. **O-12（ファイル形式・ファイル名）**: 発注の新規作成/編集で「ファイル形式」欄に候補
   （種別が `Se`/`Bgm` なら `.wav`/`.ogg`/`.mp3` 等）が datalist で出ること。`png` のように
   ドット無しで入力してフォーカスを外すと `.png` に揃うこと。「納品ファイル名」欄が空のとき
   「推奨: SE_Slash.wav」のような表示が出て、「推奨名を使う」ボタンで入力欄にコピーされること。
   ファイル名に `/` 等を入れると赤ではなく注意色の警告（オレンジ系）が出るが、保存ボタンは
   無効化されず保存できること（例外で止めない・ブロックしない方針の確認）。一覧に「ファイル形式」
   列が出て、絞り込み・列見出しクリックでの並べ替えができること
3. **O-13（発注リンクのコピー）**: 発注の詳細・一覧の各行・発注ツリーの Presentation
   発注グループのヘッダーに「リンクをコピー」ボタンが出ること。押すとクリップボードに
   コピーされる（ブラウザが `navigator.clipboard` を許可していない場合は、代わりに
   「Ctrl+C でコピーしてください」と読み取り専用の入力欄が出ることも確認する）。
   横の「▼」から「URL のみ」「名前付き」「Markdown」を選べること
4. **コピーした URL を別タブ（またはシークレットウィンドウ）で開くと、その発注/発注グループの
   詳細が表示されることを確認する**（発注は詳細パネルが自動で開く、発注グループは発注ツリー上の
   該当グループがハイライトされてスクロールする）
5. 存在しない id の URL（例: 末尾の id を書き換えたもの）を開いて、例外にならず
   「指定された発注が見つかりません」/「指定された発注グループが見つかりません」の案内が
   一覧・発注ツリーの上に出ることを確認する

### 要判断・引き継ぎ（O-12〜O-13、2026-09-14）

- **2026-09-14 対応済み**: O-13 の「リンクをコピー」は独自の URL 方式
  （`?page=order&id=...`/`?page=group&id=...`）を使っていたが、既存の `AssetDataBase.SpecUrl`
  （`SpecWebParser.BuildSpecLink` が組み立てるハッシュ形式 `#/assets/<id>`）はこの SPA が
  `location.hash` に依存しない設計であるため実際には機能しないと判明していた。別チケットで
  `SpecWebParser.BuildSpecLink`（`Assets/DDrive/Editor/Spec/SpecWebParser.cs`）を
  `?page=order&id=<種別::識別子>` 形式（O-13 と同じ）に揃えた（docs/32_spec_web.md §10.8 参照）。
  既存アセットの旧形式 `SpecUrl` は次回の仕様書同期で新形式に上書きされる（消えるのではなく
  更新される）。Unity Inspector の「仕様書を開く」ボタンで実際にリンクが開くかは、この対応は
  Unity 未検証のため人による確認が必要
- 上記「前提」（W-9〜W-12）と同じく、実デプロイでの動作確認は本チケットの範囲内では
  実施できなかった（Node テストのみ。302 件 green）

## O-15〜O-16 発注ツール: 発注後の編集・リネーム + メモの可読性向上（2026-09-14 実装）

対象: [32_spec_web.md](32_spec_web.md)（実装メモ「O-15: 発注後に編集できるようにする」
「O-16: 発注メモを確認する方法がない」）、[11_tasks.md](11_tasks.md) O-15/O-16、
`Tools/SpecWeb/`（GAS ソース。D-Drive 側の C# は変更していない）。前提は O-12〜O-13 と同じ
（①②のデプロイ URL・トークンが必要、`push.ps1` で再デプロイ済みであること）。

1. `push.ps1` で①②を再デプロイする
2. **発注を作る → 編集で担当・期限・メモを変えて保存する**: 発注ツリーまたは一覧から新規発注を
   作成し、一度保存する。発注ツリーの行・私の発注（発注した/受けた）の行にそれぞれ「編集」ボタンが
   出ることを確認し、押すと一覧画面へ遷移してその発注の詳細（編集）が開くことを確認する。詳細で
   発注者・受注者・納品期限を変更し、メモ（リファレンス）欄に textarea へ入力する。textarea の
   上の書式ツールバー（見出し・太字・箇条書き・番号付き・チェック・リンク・画像・区切り線・引用・
   コード・「参考リンク」・「納品物チェックリスト」）を、テキストを選択した状態/選択していない
   状態の両方で一通り試し、期待どおりの Markdown 記法が挿入されることを確認する（Ctrl+B/Ctrl+K の
   ショートカットも確認する）。保存すると詳細が閉じ、一覧にその場で反映されることを確認する
3. **保存後、詳細を開き直すとメモが整形済み表示になっている**: 手順2で保存した発注を再度開き、
   メモが Markdown をレンダリングした読み取り表示（見出し・太字・リンク等が実際に反映された見た目）
   になっていることを確認する。「メモを編集」ボタンで textarea に戻せること、リンクをクリックする
   と新しいタブで開き、この発注ツール自身の画面が差し替わらないことを確認する（`target="_blank"`
   への変更が効いているかの確認。旧仕様では自アプリの iframe が差し替わってしまっていた）
4. **識別子を直す**: 手順2の発注（D-Drive でまだ作成していない前提）の識別子・種別が入力欄で
   編集できることを確認し、識別子を変えて保存する。保存後、一覧にその発注が新しい識別子で
   反映されていること、手順2より前に控えておいた古い `?page=order&id=旧識別子` の URL を別タブで
   開くと、例外にならず新しい識別子の詳細が表示されることを確認する（O-13 のリンクコピー機能で
   識別子変更前にリンクをコピーしておくとよい）
5. **D-Drive で作成済みの発注は識別子が変えられない**: 仕様書同期を実行して D-Drive 側で
   Placeholder 以上が作られたアセット（`ddriveState.created=true` になったもの、または
   状態が「インポート済」になったもの）の詳細を開き、識別子・種別の入力欄が無効化され
   「D-Drive で作成済みのため変更できません（変更すると D-Drive 側との対応が切れます）」の
   ヒントが表示されることを確認する
6. **発注グループの編集**: 発注ツリーの Presentation 発注グループのヘッダーに「編集」ボタンが
   出て、押すと名前・Presentation 識別子・WBS 番号・発注者・目安期限・説明の編集フォームが
   開くことを確認する。値を変えて保存すると、その場でヘッダー等の表示が更新されることを確認する
7. **一覧・発注ツリー・私の発注の 📝 アイコン**: メモが入力済みの発注の行に 📝 アイコンが出て、
   押すとメモの整形表示がその場に展開されることを確認する（一覧の展開行には「続きを読む
   （詳細を開く）」ボタンがあり、押すと詳細パネルが開くことも確認する）。メモが空の行には
   アイコンが出ないことを確認する
8. **未保存の変更の確認**: 詳細パネルで何か変更してから「閉じる」ボタン（またはパネル外側の
   クリック）を押すと確認ダイアログが出て、キャンセルすればパネルが閉じないことを確認する。
   何も変更していない状態で「閉じる」を押した場合は確認ダイアログが出ずにそのまま閉じることを
   確認する

### 要判断・引き継ぎ（O-15〜O-16、2026-09-14）

- O-16 の要望3「メモ内の画像リンクは小さなサムネイル表示」は、既存の `renderMarkdownSafe` が
  `![alt](url)` を `<img>` タグに変換する実装（O-5 で実装済み）のまま活用し、専用のサムネイル化
  （固定サイズへの縮小等）は行っていない。実デプロイで画像が大きすぎて見づらい場合は追加対応が
  必要になる可能性がある（[32_spec_web.md] 実装メモ「O-16」参照）
- 上記「前提」（O-12〜O-13）と同じく、実デプロイでの動作確認は本チケットの範囲内では
  実施できなかった（Node テストのみ。436 件 green）

### 追補（2026-09-14、実デプロイで見つかった不具合の修正: O-15 導線の再修正 + 削除機能の拡充）

対象: [32_spec_web.md「実装メモ（2026-09-14 追補: 実デプロイで判明した不具合の修正）」](32_spec_web.md)。
ユーザーが実デプロイ（PR #54 まで反映済み）を操作して見つけた2点への対応
（`fix/order-edit-nav-list-delete` ブランチ）。

1. `push.ps1` で①②を再デプロイする
2. **発注ツリー・私の発注の「編集」で詳細が開くこと**: 発注ツリーの行・私の発注（発注した/
   受けた）の行の「編集」ボタンを押すと、一覧が表示されるだけで終わらず、その場で該当発注の
   詳細パネルが開くことを確認する（以前はここが実デプロイで動かなかった）。詳細パネルの
   見出しの上に「← 発注ツリーへ戻る」/「← 私の発注へ戻る」ボタンが出て、押すと元の画面
   （発注ツリー/私の発注）に戻ることを確認する。一覧の行から直接開いた場合はこの「戻る」
   ボタンが出ないことも確認する
3. **一覧の行から直接削除**: 一覧の各行に「削除」ボタンが出て、押すと確認ダイアログが出て、
   OK すると詳細パネルを開かずにその場で行が消えることを確認する（「アーカイブ済みを表示」
   チェックボックスをオンにすると戻せる）。発注ツリー・私の発注の各行の「削除」ボタンも
   同様に、確認 → その場で行が消えることを確認する（viewer ではどの画面にも「削除」ボタンが
   出ないことも確認する）
4. **一覧の複数選択 + 一括削除**: 一覧で複数行のチェックボックスを選択すると、ツールバーに
   「選択した発注を削除（n件）」ボタンが出ることを確認する。押すと件数入りの確認ダイアログが
   出て、OK すると選択した行がまとめて削除され、成功件数がトーストに表示されることを確認する。
   ヘッダーの選択チェックボックスで表示中の行を一括選択/解除できることも確認する
5. **回帰確認**: 手順2〜4の操作後も、O-15〜O-16 で確認済みの編集・リネーム・メモの表示・
   発注グループの編集が変わらず動くことを確認する

- 実デプロイでの動作確認は本チケットの範囲内では実施できなかった（Node テストのみ。
  `test/orderEditNavigation.test.js` で `html/App.html`・`html/OrderTree.html`・
  `html/Assets.html` の実際の画面遷移を通したテストを追加したが、iframe サンドボックス内の
  `google.script.history` の実際の挙動は Node では再現できない。441 件 green）

## 緊急修正: フォーカス外れ・コピーメニュー展開・連打対策・削除機能・マニュアルの白画面（2026-09-14 実装）

対象: [32_spec_web.md「実装メモ（2026-09-14、緊急修正: 実デプロイで見つかった不具合4件）」](32_spec_web.md)、
`Tools/SpecWeb/html/Assets.html`・`OrderTree.html`・`Manual.html`・`UiFeedback.html`（新規）・
`OrderLinkLogic.html`・`tools/build-manual.js`。実デプロイ①でユーザーが操作中に見つけた不具合の
修正（原因・対策の詳細は上記 docs/32 の節を参照）。

1. `push.ps1` で①②を再デプロイする
2. **フォーカス外れ**: 新規発注・発注グループ作成・詳細編集の各入力欄（識別子・表示名・
   カテゴリ・発注者・受注者・ファイル形式・納品ファイル名 等）に日本語・英数字を続けて
   入力し、1文字ごとにフォーカスが外れないことを確認する。識別子/カテゴリ/ファイル形式を
   変えると「納品ファイル名」欄の推奨表示・警告だけが更新されることも確認する
3. **コピーメニュー**: 一覧・発注ツリーの各行/グループヘッダーで「リンクをコピー」の ▼ を押し、
   メニューが1つだけ開くこと（他の行の ▼ を押すと前のメニューが閉じる）、メニュー外クリック・
   Esc キー・項目選択のいずれでも閉じることを確認する（以前は全行のメニューが常時展開していた）
4. **連打対策**: 「+ 発注グループを作成」・詳細の「保存」等の書き込みボタンを連打しても、
   ボタンが「送信中...」のまま無効化され、応答が返るまで2回目以降は何も起きないこと。
   完了後はボタンが元に戻り、画面右下（狭い画面では下部）に「作成しました」等のトースト
   （失敗時は赤で理由）が数秒表示されて自動で消えることを確認する
5. **削除機能**: 配下が空の Presentation 発注グループの「削除」ボタンで即座に削除できること、
   配下に発注が残っているグループの「削除」は拒否メッセージ（「先に付け替えてから削除して
   ください」）がトーストで表示されて削除されないことを確認する。発注の詳細で
   「削除（アーカイブ）」→ 一覧の「アーカイブ済みを表示」チェックボックスをオンにすると
   アーカイブ済みの発注が（状態列に「（アーカイブ済み）」と付いて）再表示されること、
   その発注の詳細を開くと「削除（アーカイブ）」の代わりに「元に戻す」ボタンが出て、
   押すと通常の一覧に戻ることを確認する
6. **マニュアルの白画面**: 上部メニューの「マニュアル」、各マニュアルページ上部の
   「← 発注ツールへ」「マニュアル目次」バー、本文中のページ間リンクのどれを押しても、
   トップフレームが `*-script.googleusercontent.com/userCodeAppPanel?...` のような
   白画面に遷移しないことを確認する（正しく iframe 内でページが切り替わること）

### 要判断・引き継ぎ

- 不具合3（「aaa」という空の発注グループが10件近くできていた件）は再描画/入力での重複作成
  ではなく、応答が遅い間の連打が原因と判明済み（コーディネーター確認）。連打対策・トースト・
  削除機能の追加で対応した。バリデーションでの重複名拒否は行っていない（同名の発注グループを
  複数作ることは仕様として許容する。docs/32 参照）
- 発注グループの削除に「配下を単体発注へ移す」機能は追加していない（既存 `orderGroups.delete`
  API の仕様どおり、配下が残っていれば拒否するだけ）。付け替え UI が必要かどうかは今後の
  要望次第
- 実デプロイでの動作確認は本チケットの範囲内では実施できなかった（Node テストのみ。367 件 green）

## 5-12 仕様書テンプレート（PR #13）

対象: `docs/SpecSheetTemplate/`（`DDrive_仕様書テンプレート.xlsx` / `make_template.py` / `README.md`）、[27_spec_sheet.md](27_spec_sheet.md) §7.1。

1. `docs/SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx` を Google ドライブにアップロードし、「アプリで開く → Google スプレッドシート」で開けること
2. 共有設定を「リンクを知っている全員が閲覧可」にして、別アカウント（またはシークレットウィンドウ）で内容が見えること
3. `アセット` タブの「種別」「状態」列、`調整値` タブの「型」列のプルダウンが Google スプレッドシート上でも効くこと（xlsx → Google Sheets 変換で入力規則が引き継がれるかは未検証）。`_選択肢` タブが非表示になっていること
4. `機能_サンプル` タブを複製して `機能_<名前>` を作れ、体裁が崩れないこと

要判断:
- [27] §7.1 の暫定既定（取得方法 A = リンク共有 + CSV / 調整値を D-Drive に取り込む / テンプレートはユーザーが自分のドライブへアップロード）を正式決定とするか
- `アセット` タブの「種別」表記を AssetType の enum 名（Se / Bgm / Vfx …）にした。デザイナーに馴染みのあるファイル名接頭辞（SE / BGM / VFX …）の方がよいか

## 5-15 各エディタの「＋ 新規作成」（PR #14）

対象: `Editor/Inspector/NewAssetToolbarButton.cs`（共通ヘルパー）、`Editor/AssetBrowser/NewAssetDialog.cs`（`Open(Type[], Action<AssetDataBase>)` を新設）、各専用エディタのツールバー 16 か所。設計は [09_editor_tools.md](09_editor_tools.md) §8.3。

各エディタを `Tools > D-Drive > Editors > …` から開き、ツールバー（多くは対象アセットの ObjectField や🔒トグルと同じ行、一部はウィンドウ最上部の単独行）にある **「＋ 新規作成」** ボタンを押す → `NewAssetDialog` が開き、種別ドロップダウンがそのエディタの対応種別だけに絞られていること（候補が 1 つなら選べず固定表示）を確認 → 表示名・カテゴリ・識別子を入力して「作成」→ **ダイアログが閉じて、元のエディタの対象がその場で作った新規アセットに切り替わっていること**（対象アセットの ObjectField に反映される。Anchor/Anchor Group/Model/Vfx/Anim は SceneView 側の表示も追従する）を確認する。

| エディタ（メニュー） | ボタンを押すと固定される種別 | 作成後に切り替わる対象 |
|---|---|---|
| Audio | SeData / BgmData（2 択） | 対象アセット（波形表示が空の新規データになる） |
| VFX | VfxData | 対象アセット |
| Animation (3D) | AnimData | 対象アセット |
| Animation (2D) | Anim2DData | 編集モードに切り替わり、Clip 未設定の新規データが対象になる |
| Prefab | PrefabData | 対象アセット |
| Canvas | CanvasData | 対象アセット |
| Material | MaterialData / TextureData（2 択） | 対象アセット |
| Material 変換 | MaterialData | 「変換元 MaterialData」欄 |
| Material プレビュー | MaterialData | サムネイル対象（タイトルバーの名前も切り替わる） |
| Anchor | AnchorData | 対象アセット |
| Anchor Group | AnchorGroupData | 対象アセット |
| Button Skin | ButtonSkinData | 対象 Skin |
| Slider | SliderSkinData | 確認用シーンにそのスキンのサンプルスライダーが配置される（対象は UiSlider のサンプル） |
| Slider Skin | SliderSkinData | 対象 Skin |
| UI Tween | UiTweenData | 対象 UiTweenData |

いずれも AssetBrowser を一度も開かずに完了できること、Console に想定外の Error が出ないこと（作成先エディタが無い/閉じている等の異常系で警告ログが 1 行出るのは正常）を確認する。

要判断:
- **二次的な専用エディタにも同じボタンを付けた**: `MaterialConvertWindow`（既存 MaterialData をシェーダー変換する画面。「新規 MaterialData として作成」という別の作成手段を既に持つ）、`MaterialThumbnailWindow`（サムネイルだけの独立ポップアップ）、`SliderEditorWindow`（`SliderSkinData` の応答曲線・追従比較などの高度編集。`SliderSkinEditorWindow` の方が本来の作成入口）は、チケットの「`[DataEditor]` 付きの全専用エディタ」を文字どおり解釈して含めた。実際にはこの 3 つは「新規に作る場所」としては使われにくく、ボタンが冗長・混乱の元と感じる場合は `NewAssetToolbarButton` の呼び出しをこの 3 か所だけ外すことを検討してほしい
- **種別ロックはドロップダウンを消さず選択肢を絞る方式にした**: Audio(SE/BGM)・Material(MaterialData/TextureData) のように 1 エディタが複数種別を持つ場合、ドロップダウン自体は残して候補をその 2 つだけに絞っている(候補が 1 つの場合のみ無効化して見せる)。「エディタごとに 1 種別に固定」という文言を厳密に取るなら、Audio/Material では追加のトグルなどでどちらを作るか明示すべきかは要判断
- **Anim2D は「空の Placeholder」を作る**: Anim2DEditorWindow は元々スプライト分割からクリップまで一括生成する「作成」モードを持つが、「＋ 新規作成」は(ImportRule と同じ考え方で)Clip 未設定の Anim2DData をまず作って編集モードに切り替えるだけにした。ID だけ先に確保して後でスプライトを割り当てる、という D-Drive の基本コンセプトには合致するはずだが、既存の「作成」モードと役割が重複して見えないかは要判断

## 5-13 仕様書同期（PR #15）

対象: `Assets/DDrive/Editor/Spec/*`(新規)、`Assets/DDrive/Editor/Codegen/TuningCodegen.cs`(新規)、`Assets/DDrive/Runtime/Tuning/*`(新規)、`Assets/DDrive/Runtime/Loop/DDriveRuntimeBootstrap.cs`(`TuningTable` 直参照を追加)。設計は [27_spec_sheet.md](27_spec_sheet.md) §4/§8。

実際の Google スプレッドシートを使った確認手順(テンプレートは [docs/SpecSheetTemplate/](SpecSheetTemplate/) を参照):

1. [docs/SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx](SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx) を自分の Google ドライブへアップロードし、「アプリで開く」→「Google スプレッドシート」で開く
2. 共有設定を「リンクを知っている全員が閲覧可」にする
3. `アセット` タブに 1 行追加する(例: 種別 `Se` / カテゴリ `Player` / 識別子 `Check1` / 表示名「確認用効果音」/ 状態「仮」/ 担当に自分の名前 / 仕様列に人向けタブのセルへのリンク / 備考に何か一言)
4. Unity で `Tools > D-Drive > 仕様書と同期` を開く。「スプレッドシート URL」にシートの URL を貼り「設定を保存」→「取得」
5. 「新規」に手順3の行が出ること。チェックが付いた状態で「適用」を押す → `Assets/GameData/Audio/SE/Player/SE_Player_Check1.asset` のような Placeholder(`SeData`)が作られ、Inspector の Tags に `State/仮`、Assignee に担当、Description に備考、SpecUrl に仕様列のリンクが入っていること
6. 手順3の行の「表示名」「状態」を書き換えて Unity 側で再度「取得」→「変更」に出ること。「適用」を押すと該当項目だけが上書きされ、手順5で作った Data 自体(ファイル・ID)は変わらないこと
7. 手順3の行をシートから削除して再度「取得」→「新規」「変更」には出ず、「シートから消えた(Archive 候補)」に手順5の Data が表示されるだけで、実際には削除されていないこと(Project ウィンドウにファイルが残っている)
8. 「調整値」タブに `Check/Value,1.5,float,0,10,,確認用` のような行を追加して「取得」→「適用」(「調整値も同期する」にチェックが入っていること)。`Tools > D-Drive > Generate > Regenerate Tuning Keys` を実行 → `Assets/Generated/Tuning.g.cs` に `TUNING.CheckValue` が生成されること
9. Console やテストコードから `DDrive.Runtime.Tuning.Tuning.GetFloat(DDrive.Generated.TUNING.CheckValue)` を呼んで `1.5f` が返ること(Editor から `execute_code` 等で確認、またはテストコードを一時的に書いて確認)
10. 「選択肢をコピー」「既存アセットをコピー」を押し、クリップボードに TSV がコピーされること(貼り付け先はテキストエディタで確認して構わない)
11. Unity を再起動(またはドメインリロード)し、事前に URL・自動取得 ON を設定しておいた状態で、Asset Browser のツールバーに「仕様書に変更 n 件」のバッジが自動で出ること(手順3〜7 を通しで試すなら、シートに未反映の行を残した状態で再起動する)
12. 確認が終わったら、手順5で作った `Assets/GameData/Audio/SE/Player/SE_Player_Check1.asset`(存在すれば)と `Assets/GameData/Settings/DDriveSpecSettings.asset` / `DDriveTuningTable.asset` を Unity Editor から削除する(Addressables のエントリも合わせて外す)

要判断:
- **Archive 候補の絞り込み**: `SpecUrl` が設定済みのアセットだけを対象にした(§9-1 参照)。運用開始時にまだ 1 度も同期していない既存アセットは対象に入らない
- **識別子の逆算方式(新フィールドを増やさない)**: ファイル名から `_` 区切りの最後のトークンを識別子として逆算している(§9-2 参照)。カテゴリだけを変えて識別子を変えていない場合、次回同期での結び付けが外れる可能性がある
- **ControlSkin の自動作成不可**: `ButtonSkinData`/`SliderSkinData` のどちらを作るか一意に決められないため、シートの新規行が `ControlSkin` のときは自動作成をスキップする(§9-3 参照)
- **自動同期のテスト検出**: `Application.isBatchMode` と `-runTests` 引数だけで判定しており、Test Runner ウィンドウからの対話的実行は検出できない(§9-4 参照。実害は無い設計だが要確認)
- **リネーム結び付け(§4.3 末尾)は未実装**
- **`DDriveSpecSettings`/`DDriveTuningTable` の .asset は今回コミットしていない**: `Assets/GameData/Settings/` に必要になったときだけ自動生成される。ユーザーが実際に同期を試すと生成されるので、その時点でコミットするか判断してほしい

## 5-14 仕様書リンク（PR #15）

対象: `Assets/DDrive/Foundation/Data/AssetDataBase.cs`(`SpecUrl`/`Assignee` フィールド追加)、`Assets/DDrive/Editor/Inspector/SpecUrlGui.cs`(新規)、`Assets/DDrive/Editor/Inspector/AssetDataInspector.cs`。

1. 任意の Data アセット(例: 5-13 の手順5で作った Placeholder、または既存の SeData)を選び、Inspector の `SpecUrl` フィールドに URL を入力する
2. Inspector 最上部(アイコン行の下)に「📄 仕様書を開く」ボタンが出ること。押すとブラウザで該当 URL が開くこと
3. `SpecUrl` を空にすると、ボタン自体が表示されなくなること(無効表示ではなく非表示)
4. 5-13 の手順3〜5 のとおり仕様書経由で同期した場合、「仕様」列に書いたリンクが自動的に `SpecUrl` に入り、同じボタンが機能すること

要判断:
- 特になし(5-13 の要判断と共通)

## 5-16 新規作成ダイアログ「仕様書から選ぶ」（PR #16）

対象: `Assets/DDrive/Editor/AssetBrowser/NewAssetDialog.cs`(「仕様書から選ぶ」セクション、「備考」「仕様リンク」欄を新設)、`Assets/DDrive/Editor/Spec/SpecSyncService.cs`(`ApplyExtraFields` を `public` 化)、`Assets/DDrive/Editor/Spec/SpecCache.cs`(`RecomputeDiff()` を新設)。設計は [27_spec_sheet.md](27_spec_sheet.md) §4.5.1/§9.1、[09_editor_tools.md](09_editor_tools.md) §8.5。

実際の Google スプレッドシートを使った確認手順(5-13 の手順1〜4 を先に済ませ、仕様書と同期済みの状態から始める):

1. 仕様書の `アセット` タブに、まだ D-Drive に無い行を追加する(例: 種別 `Se` / カテゴリ `Player` / 識別子 `Check5016` / 表示名「確認用効果音2」/ 状態「仮」/ 担当に自分の名前 / 仕様列にリンク / 備考に一言)
2. `Tools > D-Drive > 仕様書と同期` を開いて「取得」を押す(この時点ではまだ「適用」しない。新規行がキャッシュに乗るだけでよい)
3. `Tools > D-Drive > Asset Browser` を開き「新規」を押す(または任意の専用エディタの「＋ 新規作成」)。ダイアログ最上部の「仕様書から選ぶ」に、最終取得時刻と一覧(検索欄付き)が出ること。手順1の行が一覧にあること
4. 検索欄に識別子や表示名の一部を入れて、一覧が絞り込まれることを確認する
5. 手順1の行の「選ぶ」を押す → 種別・カテゴリ・識別子・表示名・備考・仕様リンクの各欄が自動で入ること(値は書き換えてもよい)
6. 「作成」を押す → `Assets/GameData/Audio/SE/Player/SE_Player_Check5016.asset` のような Placeholder が作られ、Inspector の Tags に `State/仮`、Assignee に担当、Description に備考、SpecUrl に仕様リンクが入っていること(5-13 の同期で作った場合と同じ結果になっていること)
7. ダイアログをもう一度開く(または `Tools > D-Drive > 仕様書と同期` の「新規」一覧を見る) → 手順1の行が「仕様書から選ぶ」一覧・同期の「新規」一覧のどちらからも消えていること(ネットへ再取得しなくても消えている)
8. いずれかの専用エディタ(例: Audio Editor)の「＋ 新規作成」から同じダイアログを開き、「仕様書から選ぶ」の一覧がそのエディタの対応種別だけに絞られていること(例: Audio Editor なら Se/Bgm の行だけ、他の種別の未作成行は出ない)
9. `Tools > D-Drive > 仕様書と同期` を開いたまま設定の「スプレッドシート URL」を空にして保存し、ダイアログを開き直す → 「仕様書から選ぶ」が案内文だけになり、一覧・検索欄が出ないこと。確認後は URL を戻しておく
10. 確認が終わったら、手順6で作った `SE_Player_Check5016.asset` を削除する(Addressables のエントリも合わせて外す)
11. **（2026-09-14 追加、P5 レビュー第 1 弾 5-R）識別子を手で書き換えると選択が解除される**: 手順5で行を選んだ後、「選択中の仕様書行: …」という表示が出ることを確認する → 識別子欄を(例)`Check5016` から `Check5016X` のように手で書き換える → 「選択中の仕様書行」の表示が消え、一覧の該当行も太字/「選択中」表示ではなくなること。この状態で「作成」を押す → Status/Assignee が付かない(空の)Placeholder が作られること(元の仕様書行の Status/Assignee が誤って別アセットに付かないことの確認)。「解除」ボタンでも同様に選択が外れることを確認する

要判断:
- ~~選択後に他の欄を手で書き換えても Status/Assignee は選択時のまま~~ → **2026-09-14 対応済み(5-R、上記手順11参照)**: 識別子/表示名/カテゴリを手で書き換えたら選択を解除するようにした([27] §9.1-8 参照)。備考/仕様リンクは自由記述として保持してよいと判断し、解除の対象にしていない
- **キャッシュの古さの閾値(1 時間)は暫定**: 根拠のある値ではない。運用してみて長すぎる/短すぎるかを判断してほしい([27] §9.1-9 参照)
- ~~**`NewAssetDialog` に `gameDataRoot` のテスト用オーバーライドが無い**~~ → **2026-09-14 対応済み(PR #17、P5 テスト隔離)**: `NewAssetDialog.TestGameDataRootOverride` / `TestSpecSettingsOverride` を追加し、`NewAssetDialogSpecPickerTests` は実 `Assets/GameData`・実カタログ・実 Addressables・実 `DDriveSpecSettings.asset` に一切触れなくなった
- **「備考」「仕様リンク」欄はダイアログの全ケース(手入力のみの作成も含む)に常時表示**: 仕様書から選ばない通常の手入力作成でも入力できるようにした(空でも作成可、既存動作に影響なし)。専用エディタからのロック付き作成でも同じ欄が出る。UI が煩雑に見える場合は「仕様書から選ぶ」を使っている時だけ表示する等の整理を検討してほしい

## 5-5 依存関係グラフ（PR #18）

対象: `Assets/DDrive/Editor/Dependencies/`(新規: `DependencyGraphService.cs`/`DependencyGraphCollector.cs`/`DependencyGraphCache.cs`/`DependencyGraphPostprocessor.cs`/`DependencyGraphTypes.cs`)。UI は 5-6 で作るため、今回はメニュー + ログのみ。設計は [09_editor_tools.md](09_editor_tools.md) §10、[02_core_framework.md](02_core_framework.md) §12。

確認手順:

1. `Tools > D-Drive > Generate > 依存関係グラフを再構築` を実行する → Console に `[DDrive] DependencyGraph: 再構築完了(対象 N ファイル, 参照 M 件)` のログが出ること(プロジェクト内の全 Data/Prefab/Scene を開閉して走査するため、Scene 数によっては数秒〜数十秒かかる)
2. 任意の Data(例: `SeData`)の `AnchorId` に値を設定して保存する → 数秒後(delayCall)に自動で差分更新される(明示的なログは出さない設計。確認は次項の API 経由、または一旦 Unity を再起動して `依存関係グラフを再構築` を実行し直し件数が増えていることで代用してもよい)
3. Scene に `SeEmitter` 等の `AssetId<TMarker>` フィールドを持つ MonoBehaviour を配置して ID を設定し保存する → 上記同様に差分更新されるはず(シーンの Open/Close を伴うため、保存直後に若干のカクつきが起きても異常ではない)
4. `Tools > D-Drive > Generate > 依存関係グラフを再構築` を再実行し、件数ログが手順2・3の分だけ増えていること
5. Play Mode に入った状態で Data を編集した場合(通常運用ではまず起きないが)、差分更新が Edit Mode に戻るまで保留され、エラーが出ないこと

要判断:
- **起動時 / ドメインリロード時の自動再構築をしていない**: 全 Scene の Open/Close が重く、デザイナーの作業を止めない方針(CLAUDE.md §0-4)を優先した。`Library/DDriveDeps/` を消した直後や初回導入時は空なので、手動で 1 度「依存関係グラフを再構築」を実行する必要がある。運用してみて不便なら、軽量な整合性チェック(ファイルの内容ハッシュ比較。`DependencyFileRecord.ContentHash` は実装済みで保存だけしている)を起動時に追加することを検討してほしい
- **循環参照検出は未実装**: 5-6 の依存ツリー UI 側で深さ優先探索時に検出する想定
- **`[SerializeReference]` は未検証**: 2026-09-14 時点でプロジェクト内に使用箇所が無いため、実際の多態フィールドでの動作は未確認(収集ロジック自体は Unity の `SerializedProperty` 標準走査に乗っているため動くはずという設計判断)
- **循環参照や巨大な依存グラフでのパフォーマンスは未計測**: 現状のプロジェクト規模(Scene 17・Data 数百件程度)では `RebuildAll` が数秒〜十数秒で完了することを確認したのみ

## 5-6 使用箇所検索 / 未使用検出 / 安全な削除（PR #19）

対象: `Assets/DDrive/Editor/Dependencies/`(新規: `UsagesWindow.cs`/`DependencyTreeWindow.cs`/`DependencyTreeNode.cs`/`UnusedAssetsWindow.cs`/`ArchiveTagService.cs`/`SafeDeleteService.cs`/`DependencyJumpService.cs`/`DependencyAssetResolver.cs`/`CodeReferenceScan.cs`)、`Editor/AssetBrowser/AssetBrowserWindow.cs`(行の右クリックメニュー・「未使用...」ボタン)、`Foundation/Registry/AssetCatalog.cs`(`Remove(id)` 新設)。設計は [09_editor_tools.md](09_editor_tools.md) §1 / §10、運用は [10_workflow.md](10_workflow.md) §3、デザイナー向けは [DesignerManual/asset-browser.html](DesignerManual/asset-browser.html)。

事前準備: `Tools > D-Drive > Generate > 依存関係グラフを再構築` を一度実行しておく(5-5 の要判断どおり、Library を消した直後・導入直後は索引が空)。

確認手順:

1. **グラフ再構築**: 上記の事前準備を実施し、Console にログが出ることを確認
2. **使用箇所検索**: `Tools > D-Drive > Asset Browser` を開き、どこかから参照されている Data(例: 既存の `SeEmitter` 等から使われている SE)を右クリック →「使用箇所を表示」→ 参照元の一覧(パス・オブジェクトパス・コンポーネント.プロパティ)が出ること。逆にどこからも使われていない Data では「どこからも参照されていません」と出ること
3. **ダブルクリックジャンプ(Data)**: 使用箇所一覧で参照元が Data(.asset)の行をダブルクリック → Project ウィンドウでその Data が選択・ハイライトされること
4. **ダブルクリックジャンプ(Prefab)**: 参照元が Prefab の行をダブルクリック → Project ウィンドウで Prefab 内の該当 GameObject(無ければ Prefab 自身)が選択されること
5. **ダブルクリックジャンプ(Scene)**: 参照元が Scene の行をダブルクリック → 「開きますか?」の確認ダイアログが出ること。今のシーンに未保存の変更がある状態で試し、保存確認ダイアログも正しく出ること。「開く」を選ぶとシーンが開き、該当オブジェクトが選択されること
6. **依存ツリー**: 何かを参照している Data(例: Prefab や、AnchorId を設定した SE)を右クリック →「依存ツリーを表示」→ 参照先がツリーで展開されること。可能なら A→B→A のような循環参照を作る Data を用意し、循環箇所が打ち切り表示(色つき)になることを確認
7. **未使用一覧**: AssetBrowser ツールバーの「未使用...」を押す → どこからも参照されていない Data の一覧が出ること。いくつかチェックを入れて「選択項目を一括Archive」→ Console にログが出て、対象の Inspector の Tags に `Archived` が付くこと(削除はされないこと)
8. **削除確認ウィンドウが開く**: 何かから参照されている Data を右クリック →「削除...」→(旧来のダイアログではなく)`AssetDeleteWindow` が開き、削除対象一覧・参照元一覧(Data/Prefab/Scene 別)・依存先一覧が表示されること
9. **参照ありは既定でブロックされる**: 手順8のウィンドウで「アーカイブのみ」を押す → 削除されず Archived タグだけ付くこと(結果画面に「参照が残っているため削除せず、アーカイブ済みの印だけ付けました」と出ること)。「キャンセル」を押した場合は何も変わらないこと(Archived タグも付かないこと。**旧ダイアログ版と異なり、ウィンドウを開いただけ・キャンセルしただけでは Archived タグは付かない**)
10. **複数選択での削除(2026-09-14 追加)**: AssetBrowser の一覧で Ctrl/Shift クリックで複数行を選択 → 右クリック →「削除...」→ 選択した全件が削除対象一覧に出ること。選択していない行を右クリックした場合はその1件だけが対象になること
11. **参照を差し替えてから削除(2026-09-14 追加)**: 何かから参照されている Data を1件削除対象にし、「参照を差し替えてから削除」を選ぶ → 置き換え先(同じ種別の別の Data)を選ぶ → 「実行」→ 参照元の Data/Prefab のフィールドが置き換え先の ID に書き換わり、元の Data がゴミ箱に移動すること。参照元が Data の場合は Ctrl+Z で書き換えが戻ることも確認する(Prefab は戻らないことも合わせて確認する)
12. **Scene から参照されている場合は保留(2026-09-14 追加)**: シーンに配置した `SeEmitter` 等から参照されている Data を削除対象にし、「参照を差し替えてから削除」を実行 → Data/Prefab の参照は書き換わるが、**削除は実行されず Archived のみになる**こと。結果画面の「手動で直す(Scene 内の参照)」一覧にその参照が出て、「ジャンプ」ボタンでシーンを開く確認が出ることを確認する
13. **強制削除(2026-09-14 追加)**: 参照が残っている Data で「強制削除」を選ぶ → 確認チェックボックスを入れないと「実行」が押せないこと → チェックを入れて実行 → 参照が残っていても削除されること(結果画面に警告が出ること)。参照元(Data/Prefab)は書き換わっていないため、Validation を実行すると Error になることも確認する
14. **依存先の一括削除(2026-09-14 追加)**: 何かを参照している Data(例: AnchorId を設定した SE)を削除対象にし、依存先一覧でその参照先にチェックが入れられること(他から使われていない場合のみ)を確認 → チェックを入れて削除を実行 → 依存先も一緒にゴミ箱に移動すること
15. **参照なしの削除**: テスト用に何にも参照されていない Data を1つ用意し(例: 手順7で Archive したもの、または新規作成して未使用のまま)、右クリック →「削除...」→ ウィンドウに参照元が無いことを確認して「削除する」を押す → 一覧から消えること、`Assets/GameData/Catalogs/` の対応するカタログからエントリが消えること(カタログ .asset をテキストエディタ等で開いて確認)、Addressables Groups ウィンドウ(`Window > Asset Management > Addressables > Groups`)から該当エントリが消えていること
16. **ゴミ箱からの復元**: 手順15で削除した Data とアイコン画像を OS のゴミ箱(Windows のごみ箱)から元の場所へ復元する → Unity がファイルを再インポートし、Project ウィンドウにアセットとして戻ってくることを確認する。ただし **カタログ・Addressables の登録は自動では戻らない**こと(AssetBrowser の一覧には出ない・Validation で登録漏れとして検出される)も合わせて確認する。結果画面に出ている「依存関係グラフを再構築」「Addressables 登録を同期」ボタンで手直しできることも確認する
17. **コード参照の警告(分析画面 + 結果画面、2026-09-14 変更)**: 生成済み ID 定数(`Assets/Generated/AssetIds.g.cs`)がコードから実際に参照されている Data を1つ選び、削除対象にする → **削除を実行する前の分析画面(削除対象一覧の直下)**に「生成された ID 定数 '...' を参照しているコードが見つかりました」という警告が出ることを確認する(旧ダイアログ版と同じ文言・同じタイミング)。そのまま「強制削除」または(置き換え先を用意した上で)「参照を差し替えてから削除」で実際に削除する → 結果画面にも同じ警告に加えてファイル:行の一覧が出て、「開く」ボタンでコードエディタが該当行で開くことを確認する(**この警告は削除を止めない**。実際に削除するとコンパイルエラーになる可能性があるため、削除前の警告を見た時点でキャンセルする運用を徹底すること)
18. 確認で作った一時 Data・Archive 済みタグはテスト後に元に戻す(Archive を解除する、または実際に不要なら安全な削除の手順で片付ける)

要判断:
- **（2026-09-14 追加）Prefab の参照差し替えは Ctrl+Z で戻せない**: 手順11で Prefab 側の書き換えが戻らないことを実際に確認してほしい。詳細は [09_editor_tools.md](09_editor_tools.md) §10 / [31_phase5_decisions.md](31_phase5_decisions.md)
- **（2026-09-14 追加）「一緒に削除」はカスケード1段のみ**: 手順14で選んだ依存先がさらに参照していたものは自動では片付かない(要判断は [09_editor_tools.md](09_editor_tools.md) §10)
- **グラフ未構築の判定は `CachedFileCount == 0` のみ**: 「古いが空ではない」状態は検出できない(5-5 の要判断を引き継ぐ)。手順1を飛ばして削除した場合の実際の挙動(「先に再構築してください」と出て中止されること)も合わせて確認してほしい
- **依存ツリーは事前に全展開**: 巨大な依存グラフ(1 アセットが数百件を再帰的に参照する等)での表示速度は未計測。実際に触ってみて重いと感じたら [09] §10 の要判断を参照して遅延展開への切り替えを検討する
- **コード参照チェックは grep ベースの簡易実装**: 誤検知(コメント中の文字列等にヒット)・見逃し(リフレクション経由の参照等)があり得る。実運用でノイズが多い/少なすぎると感じたら精度改善を検討する(**2026-09-14 対応(5-R)**: 走査対象を `Assets/DDrive`・`Assets/Generated` に限定 + 更新時刻キーのキャッシュを追加したが、grep ベースであること自体は変えていない)
- **Scene ジャンプの自動テストは無し**: `EditorSceneManager.OpenScene(Single)` がアクティブシーンを差し替える副作用があるため、自動テストは `.asset`/`.prefab` 分岐のみ(`DependencyJumpServiceTests`)。手順5の手動確認で代替している
- **Archived タグは `AssetDataBase.Tags` への予約語追加**: TagCatalog(選択制の辞書。未実装)が将来入る場合、`"Archived"` を予約語として除外するか、専用フィールドへの移行を検討する必要がある
- **「1 リリース後に削除」の自動化はしていない**: Archived タグが付いてからどれくらい経過したら安全に削除してよいかの判断・催促は今回自動化せず、人が未使用一覧を見て判断する運用のまま

## 5-7 Preload 自動集計 + シーンロード統合（PR #20）

対象: `Editor/Preload/`(新規: `ScenePreloadAggregator.cs`/`ScenePreloadGenerator.cs`/`ScenePreloadBuildPreprocessor.cs`)、`Runtime/Loading/`(新規: `PreloadEntry.cs`/`ScenePreloadList.cs`/`ScenePreload.cs`/`SceneLoadingScreen.cs`)、`Foundation/Registry/IAssetRegistry.cs`+`AssetRegistry.cs`(`PreloadIdsAsync`/`ReleaseIds` 新設)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(`ScenePreload.Bind`/`Unbind` 追加)。設計は [10_workflow.md](10_workflow.md) §5、[02_core_framework.md](02_core_framework.md) §5/§14、[09_editor_tools.md](09_editor_tools.md) §10 の 5-7 節。

事前準備: `Tools > D-Drive > Generate > 依存関係グラフを再構築` を一度実行しておく(5-5/5-6 と同じ前提)。

確認手順:

1. **集計メニュー(現在のシーン)**: 何らかの ID 参照(`SeEmitter` 等)を含む適当な確認用シーンを開いて保存し、`Tools > D-Drive > Generate > Preload リストを再集計(現在のシーン)` を実行する → Console に `[DDrive] Preload リストを更新しました: '...'（N 件）` のログが出て、`Assets/GameData/Preload/<シーン名>_PreloadList.asset` が生成され Project ウィンドウで選択状態になること
2. **中身の妥当性**: 手順1で生成された `ScenePreloadList` の Inspector を開き、`Entries` にそのシーンが直接・間接に参照する ID が入っていること(表示名 `DisplayName` が実際のアセット名と一致していること)。シーンから参照していない Data が混ざっていないこと
3. **再実行で重複しない**: 同じシーンでもう一度「Preload リストを再集計(現在のシーン)」を実行する → 同じ `.asset` が更新されるだけで新しいファイルが増えないこと(Project ウィンドウの `Preload` フォルダのファイル数が変わらないこと)
4. **全ビルドシーン一括**: `Tools > D-Drive > Generate > Preload リストを再集計(ビルド設定の全シーン)` を実行する → Build Settings(`File > Build Settings...`)に登録されている有効シーンの数だけ `.asset` が更新されること
5. **確認用シーンで Preload を Play**: 手順1のシーン(または新規の確認用シーン)に `DDriveRuntimeBootstrap` を配置し(`Tools > D-Drive > Generate > 起動オブジェクトをシーンに配置`)、空の GameObject に `SceneLoadingScreen` コンポーネントを追加して `Preload List` に手順1の `.asset` をアサインする(`Progress Slider`/`Progress Text` は uGUI の `Slider`/`Text` があれば割り当てる、無くても動作は確認できる)。Play Mode に入る → 進捗が 0 から 1 まで進み、`IsDone` が true になること(Slider/Text を割り当てていれば見た目でも進むこと)。Console にエラーが出ないこと
6. **未登録 ID のスキップ**: 手順2の `Entries` のどれか1件の ID を Inspector で書き換えて(存在しない値にする)保存し、再度 Play Mode で `SceneLoadingScreen` を走らせる → その ID については `[DDrive] ScenePreload: Unregistered AssetId 0x... was skipped.` という警告が出るだけで、Preload 全体は止まらず完了すること。確認後は書き換えた値を元に戻す(または `.asset` ごと破棄する)
7. **ビルド前フック**: 実際の開発ビルドを1回実行する(時間があれば。Development Build で可) → Console に `[DDrive] ビルド前処理: Preload リストを N シーン分更新しました。` のログが出て、ビルドが正常に完了すること
8. 確認で作った `SceneLoadingScreen` 付き GameObject・`DDriveRuntimeBootstrap`・テスト用に書き換えた `.asset` の中身は元に戻す(または確認用シーンごと破棄する)
9. **（2026-09-14 追加、P5 レビュー第 1 弾 5-R）ロード画面を手動開始にしたとき**: 手順5の `SceneLoadingScreen` の `Auto Start On Enable` チェックを外す(手動開始運用)→ Play Mode に入っても Preload が始まらないこと(`IsDone` が false のまま)を確認したうえで、`RunAsync()` を一度も呼ばずにその GameObject を非アクティブ化する(または Destroy する)→ Console にエラー/警告が出ず、他の Preload 呼び出し元の参照カウントに影響が無いこと(自動テストは `SceneLoadingScreenTests.OnDisable_WithoutRunAsyncEverStarted_DoesNotReleaseAnything` で代替済み)

要判断:
- **シーン→Preload リストの対応付けが「シーンに置いたコンポーネントの直参照」のみ**: `AssetCatalog`/`Catalogs[]` のような中央インデックスは作っていない。複数シーンをまとめて Preload するタイトル画面等が要る場合は `DDriveRuntimeBootstrap` に `ScenePreloadList[]` を足す拡張を検討してほしい([09] §10 5-7 節参照)
- **`GenerateForAllBuildScenes` とビルド前フックは自動テスト対象外**: `EditorBuildSettings.scenes`(git 管理下の `ProjectSettings/EditorBuildSettings.asset`)を書き換えるため、実プロジェクトの設定を汚すリスクを避けて自動テストにしなかった。上記手順4・7で手動確認する
- **Preload の粒度は Data(.asset)単位**: Data 内部の AudioClip/Texture/Prefab 等のサブアセットを個別に先読みする経路は無い(Addressables の依存バンドルとして一緒にロードされる前提)。体感のロード時間短縮効果は未実測
- **ロード画面 UI は最小実装**: `SceneLoadingScreen` は uGUI の `Slider`/`Text` を任意で受けるだけの確認用コンポーネントで、デザイナー向けの正式なロード画面(Canvas/UiManager ベース)は未実装。実運用では置き換えを検討してほしい
- **参照カウントの解放漏れリスク**: `ScenePreload.RunAsync` で確保した参照は対応する `ScenePreload.Release` を呼ぶまで解放されない。`SceneLoadingScreen.OnDisable` では解放するが、独自に `ScenePreload.RunAsync` を呼ぶコードを書く場合は解放を呼び忘れないよう注意が要る。→ **2026-09-14 対応済み(5-R)**: 逆方向の事故(`RunAsync` を一度も呼んでいないのに `OnDisable` が `Release` してしまい、他インスタンスの参照カウントを誤って減らす)を修正した。`_preloadStarted` フラグで「実際に確保したときだけ解放する」ようにしている(上記手順9)

## 5-1 Presentation（PR #21）

対象: `Runtime/Presentation/{PresentationData,PresentationTrack,PlayContext,PresentationManager,PresentationHandle,Presentation,PresentationTiming,PresentationDataValidator}.cs`(新規)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(Presentation 配線追加)、`Editor/AssetBrowser/AssetCreationService.cs`(Presentation を Preload 既定に追加)、`Samples/PresentationSkillSlashDemo.cs`(新規、確認用)。設計は [08_presentation.md](08_presentation.md)、依存パッケージは [01_architecture.md](01_architecture.md) §4。

事前準備: Unity Package Manager(`Window > Package Manager`)で「Unity NuGet」レジストリ経由の `R3`(1.3.1)と `R3 (Utility for Unity)`(`com.cysharp.r3`)が入っていることを確認する(`Packages/manifest.json` に記載済みなので、プロジェクトを開いた時点で自動解決されるはず。初回だけ Package Manager の「Importing a scoped registry」ダイアログが出ることがある。「Close」で進めてよい)。

確認手順:

1. **剣攻撃デモ**: `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity` を開いて Play Mode に入る → 起動直後に **1 API(`Presentation.Play(PRESENTID.DemoSkillSlash 相当, ctx)`)** で VFX(`VFX_Player_Slash`)と SE(`SE_Player_Slash`)が Self(`Player` オブジェクト)の位置で同時に再生されること(Console に `[PresentationSkillSlashDemo] Play() -> IsPlaying=True` が出る)
2. **Signal("hit")**: Space キーを押す → 画面が一瞬止まる(ヒットストップ、約 0.08 秒)のと同時に SE がもう一度鳴ること(Console に `Signal("hit") -> HitStop + SE` が出る)。連打しても例外が出ないこと
3. **Cancel**: C キーを押す → 演出が中断されること(Console に `Cancel() -> IsPlaying=False` が出る)。手順1の VFX は `StopOnCancel=false` にしているため、鳴らし切って自然に消えること(即座に消えないのは仕様どおり)
4. **P キーで再実行**: P キーを押すと最初から再生し直せること
5. **Package Manager 確認**: `Window > Package Manager` の「In Project」タブに `R3`(Unity NuGet 経由)と `R3 (Utility for Unity)` の 2 つが表示されていること。バージョンはどちらも 1.3.1
6. **isuzu MCP が引き続き動く**: R3 導入後も isuzu MCP(`compile_status`/`test_run`/`execute_code` 等)が問題なく動作すること(このチケット自体を isuzu MCP 経由で検証しているため、動いていなければ既に気付いているはずだが念のため)
7. Inspector からの手編集も試す: `Assets/GameData/Presentation/Demo/PRES_Demo_SkillSlash.asset` を選び、Tracks の要素を増減・Time を変更して保存 → Play し直した結果に反映されること(専用エディタ(5-4)が無いため、現状はこれが唯一の編集手段)

要判断:
- **R3 導入方法は「素の R3(org.nuget.r3) + R3.Unity(com.cysharp.r3)」の両方を入れたが、実際に使っているのは前者のみ**(`Observable<T>`/`Unit`/`Subject<T>`)。R3.Unity(Player Loop 連携・`ObservableTracker` 等)は 5-1 時点で未使用。将来 UI 側([15_ui_interaction.md] の「R3 未導入」コメント箇所)が R3 化される際に本格的に使われる想定。不要なら `com.cysharp.r3` を抜いて `org.nuget.r3` だけにする選択肢もある(DLL 増加を避けたい場合)
- **DLL 重複は発生しなかった**が、isuzu MCP のバージョンが上がった際に再度確認した方がよい(`org.nuget.system.runtime.compilerservices.unsafe` 等の transitive 依存が今後増える可能性がある)
- **TrackTargetMode.World と Anchor は同一実装**(いずれも `PlayContext` を参照せず `Anchor.LocalOffset` を絶対座標として使う)。意味的な区別が必要になったら実装を分ける
- **CameraShake / Haptic は 5-2/5-2b で実装済み、Timeline のみ警告 + no-op のまま**(6-10 で実装予定)。剣攻撃デモには 5-2/5-2b で CameraShake/Haptic トラックを追記した(下記「5-2 カメラシェイク」「5-2b コントローラー振動」の節を参照)
- **`VFX_Player_Slash`/`SE_Player_Slash`/`PRES_Demo_SkillSlash` の `Flags.Load` を `Preload` に変更した**(LazyLoad のままだと `PresentationManager` の同期解決で常に Placeholder になるため)。前者 2 つは他のデモ(AnchorGroup 等)でも使われている既存アセットのため、Preload 化の影響が無いか確認してほしい
- **Addressables グループ(`DDrive_GameData.asset`/`DDrive_Catalogs.asset`)はユーザーの未コミット変更と混ざっている**ため、5-1 のデモアセット登録に伴う変更はコミットしていない(ワーキングツリー上は両方の変更が混在した状態で残る)。次にこれらのファイルをコミットする人は、5-1 分(PresentationCatalog へのエントリ追加、VFX/SE の Preload 化)が含まれていることを把握しておくこと
- **PresentationEditor(5-4)は未実装**: `DataEditorRegistryTests` の Exempt に `PresentationData` を追加した。5-4 実装時に Exempt から外すこと

## 5-2 カメラシェイク（PR #22）

対象: `Runtime/Camera/{CameraShakeData,CameraFxManager,CameraFx,CameraShakeDataValidator}.cs`(新規)、`Runtime/Anim2D/Anim2DFacing.cs`(名前空間衝突の修正のみ)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(CameraFx 配線 + UnscaledCameraFxAdapter 追加)、`Runtime/Presentation/PresentationManager.cs`(CameraShake トラックの委譲先を実装)、`Runtime/Ui/OptionStore.cs`(ShakeScale の接続先)、`Editor/AssetBrowser/AssetCreationService.cs`(Shake を Preload 既定に追加)。設計は [16_camera_haptics.md](16_camera_haptics.md) Part A。確認用デモ資産 `Assets/GameData/Camera/Demo/SHAKE_Demo_DemoHitSmall.asset` を新規作成し、5-1 の剣攻撃デモ `PRES_Demo_SkillSlash.asset` の onHit トラックに接続した。

確認手順:

1. **剣攻撃デモで揺れを見る**: `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity` を開いて Play Mode に入る → Space キー(Signal("hit"))を押す → 画面停止(ヒットストップ)と同時に画面が一瞬揺れること
2. **連打しても破綻しない(AC)**: 手順1で Space キーを連打する → 揺れが異常に大きくなったり、カメラの位置がおかしくなったりしないこと(Console にエラーが出ないこと)
3. **オプション 0% で無揺れ(AC)**: `Runtime.Ui.Options.Set(OptionKey.ShakeScale, 0f)` を(確認用シーンに一時的なテストコード、または `execute_code`/デバッグ用ボタンで)呼んでから手順1を再実行する → 画面が一切揺れないこと。`Set(OptionKey.ShakeScale, 1f)` に戻すと揺れが復活すること
4. **カメラが後から現れても揺れる**: `Camera.main` がまだ無いシーンで `Presentation.Play` 等を呼んでシェイクを発火させる → Console に `Camera.main が見つからないため、シェイクは no-op です` の警告が(1 回だけ)出ること。その後カメラを配置する(タグ MainCamera)と、次のシェイクから正常に揺れること
5. **HitStop 中も揺れが止まらない**: 手順1のヒットストップ中(約 0.08 秒)にも画面の揺れが進行していること(スロー再生や連続スクリーンショットで確認するか、`CameraFxManagerTests` の自動テストで代替可)
6. **Inspector から調整**: `Assets/GameData/Camera/Demo/SHAKE_Demo_DemoHitSmall.asset` を選び、Pattern を Decay Sine や Impulse に変えて保存 → 再生し直した結果に反映されること(専用エディタ(5-2c)が無いため、現状はこれが唯一の編集手段)
7. **（2026-09-14 追加、P5 レビュー第 1 弾 5-R）カメラ切替中の揺れ**: シェイクを発火させたまま(揺れが収まる前に)`Camera.main` を別のカメラへ切り替える(2 台目の `MainCamera` タグ付きカメラを有効化して 1 台目を無効化する等)→ 元のカメラが `DDriveCameraShakeNode` の子から外れて元の位置(親)へ戻り、孤児ノードがヒエラルキーに残らないこと。切り替え後、新しいカメラでもシェイクが発火すること。さらに A→B→A と往復させても同様に孤児が残らないことを確認する(自動テストは `CameraFxManagerTests.EnsureCameraNode_SwapAtoBtoA_RestoresBothCameras_AndLeavesNoOrphanNode` で代替済みだが、実際のマルチカメラ演出(カメラ切替カットシーン等)で見た目に違和感が無いかは人の目で確認してほしい)

要判断:
- **Space/Pattern の簡略化**: `World`/`FromSource` の位置変換、`CustomCurve`(現状 Impulse と同じ)、回転(Rot)は常に CameraLocal 相当で適用、など複数の簡略化を行った。詳細と理由は [16_camera_haptics.md] 実装メモを参照。5-2c(専用エディタ)で波形プレビューを作る際に、これらの挙動で十分か判断してほしい
- **`DDrive.Runtime.Camera` 名前空間が `UnityEngine.Camera` と衝突する**: `Runtime/Anim2D/Anim2DFacing.cs` の `Camera.main` 使用箇所を `UnityEngine.Camera.main` にフル修飾して解消した。今後 `DDrive.Runtime.*` 配下で `UnityEngine.Camera` を非修飾で使うコードを書くとコンパイルエラーになるので注意(詳細は [16] 実装メモ)
- **CameraShake アセットを Preload 既定に追加した**: `AssetCreationService.Create` で `AssetType.Shake` を Canvas/ControlSkin/Presentation と同じ Preload 既定グループに加えた(LazyLoad のままだと常に Placeholder になるため)。既存の Shake アセットが無い(このチケットで初めて作る種別の)ため影響範囲は無いはず
- **Addressables グループへの追加**: `CameraFxCatalog`(`DDrive_Catalogs.asset`)、`SHAKE_Demo_DemoHitSmall`(`DDrive_GameData.asset`)の 2 行が追加されたが、ユーザーの未コミット変更と同じファイルのためコミットしていない(ワーキングツリー上に残る)

## 5-2b コントローラー振動（PR #22）

対象: `Runtime/Haptics/{HapticsData,IHapticOutput,GamepadHapticOutput,HapticsManager,Haptics,HapticsDataValidator}.cs`(新規)、`Runtime/DDrive.Runtime.asmdef`(`Unity.InputSystem` 参照追加)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(Haptics 配線 + OnApplicationQuit/OnApplicationFocus での ResetOutput)、`Runtime/Presentation/PresentationManager.cs`(Haptic トラックの委譲先を実装)、`Runtime/Ui/OptionStore.cs`(HapticScale の接続先)、`Runtime/Ui/UiSlider.cs`(Notch/Limit Haptic の発火)、`Editor/AssetBrowser/AssetCreationService.cs`(Haptics を Preload 既定に追加)。設計は [16_camera_haptics.md](16_camera_haptics.md) Part B。確認用デモ資産 `Assets/GameData/Haptics/Demo/HAPTIC_Demo_DemoHitPunch.asset` を新規作成し、剣攻撃デモの onHit トラックに接続した。

事前準備: Xbox/PlayStation 系のゲームパッドを PC に USB または Bluetooth で接続する(Input System が対応する機種)。パッドが無い環境では手順1・2・4は「Console にエラーが出ず、`GamepadHapticOutput` が no-op で継続すること」だけ確認すればよい。

確認手順:

1. **パッドで振動再生(AC)**: パッドを接続した状態で `PresentationSkillSlashPreviewScene.unity` を Play Mode に入り、Space キー(Signal("hit"))を押す → パッドが一瞬振動すること
2. **同時再生で飽和しない(AC)**: Space キーを連打する(複数の Haptic インスタンスが重なる状態を作る)、または `HapticsManagerTests.PlayData_OverlappingInstances_ComposeWithMax_NotSum` を確認する → 振動が加算されて振り切れたような感覚にならないこと(自動テストで Low/High が Max 合成(加算しない)されていることを確認済み)
3. **パッド未接続時は no-op**: パッドを外した状態で手順1を実行する → 例外・エラーが出ないこと
4. **オプション 0% で振動オフ**: `Runtime.Ui.Options.Set(OptionKey.HapticScale, 0f)` を呼んでから手順1を再実行する → 振動しないこと。`1f` に戻すと振動が復活すること
5. **Pause でモーター停止**: Play Mode 中に振動をトリガーした直後に `Loop.PauseService.Push(PauseChannel.Gameplay)`(ポーズ機構があればポーズ操作)を呼ぶ → パッドの振動が即座に止まること。`Pop` で解除すると再生中の振動があれば復帰すること
6. **アプリ終了/フォーカス喪失でモーター停止**: Play Mode 中に振動をトリガーした直後に Unity エディタのフォーカスを外す(Alt+Tab)→ 振動が止まること(実機ビルドでは Alt+Tab の代わりにタスク切り替えで確認)
7. **スライダーのノッチ/端で振動**: `Assets/GameData/Ui/Skin/Skider` 等、Notches>0 のスライダーを持つ確認用 Canvas を Play Mode で操作する(Notch Haptic Id / Limit Haptic Id に `HAPTIC_Demo_DemoHitPunch` の Id を設定した Slider Skin を使う)→ 目盛りを跨いだとき・端に到達したときにパッドが振動すること
8. **Inspector から調整**: `Assets/GameData/Haptics/Demo/HAPTIC_Demo_DemoHitPunch.asset` を選び、Low Freq / High Freq のカーブを変えて保存 → 再生し直した結果に反映されること(専用エディタ(5-2c)が無いため、現状はこれが唯一の編集手段)

要判断:
- **Pause 中は per-instance の Flags.Pause を見ない**: 実機のモーターを鳴らし続ける事故を避けるため、Pause チャンネルが立ったら一律で出力 0 にする安全側の設計にした(Vfx/CameraFx とは異なる)。per-instance 制御が必要になったら見直すこと
- **`LocalPlayerOnly`/`Priority`/`Extensions` は現状ロジックに未使用**: NGO 統合前のため送信元判定ができず、`LocalPlayerOnly` は常にローカル再生扱い。`Priority` は Max 合成そのものが優先度を実現しているため未使用。`Extensions` は型だけ用意し、設定すると警告のみ
- **`NotchHapticId`/`LimitHapticId` は `ulong` のまま**: シリアライズ形式の変更(型の置換)は事前確認が必要なため、5-2b では既存の `ulong` 型のまま `Haptics.Play` への接続だけ行った。`HapticId`(`AssetId<HapticMarker>`)への置換は 5-2c で判断してほしい
- **HapticsManager は Scaled dt のまま(CameraFx とは異なる決定)**: HitStop 中に振動を止めるべきか止めないべきかが仕様書に明記されていなかったため、他の全 Manager と同じ既定(HitStop で一緒に止まる)にした。要望があれば CameraFx と同じ Unscaled 駆動に変更を検討してほしい
- **Addressables グループへの追加**: `HAPTIC_Demo_DemoHitPunch`(`DDrive_GameData.asset`)の 1 行が追加されたが、ユーザーの未コミット変更と同じファイルのためコミットしていない(ワーキングツリー上に残る)
- **実機での動作確認は未実施**: 本セッションはヘッドレスな isuzu MCP 経由の自動テストのみで検証しており、実際にゲームパッドを接続した目視確認は行っていない(「未検証」と明記)。上記確認手順1〜7は人が実機で確認すること

## 5-2c 揺れ・振動エディタ（PR #23）

対象: `Editor/Camera/{CameraFxEditorWindow,SceneCameraShakePreviewDriver,EditorHapticsPreviewDriver,CameraFxPresets,WaveformGraphGui}.cs`(新規、namespace `DDrive.Editor.CameraFx`)、`Editor/Preview/CameraShakePreviewSceneSetup.cs`(新規、確認用シーン)、`Editor/DDrive.Editor.asmdef`(`Unity.InputSystem` 参照追加、`GamepadHapticOutput` を Test on Pad が直接使うため)、`Tests/Editor/DataEditorRegistryTests.cs`(Exempt から `CameraShakeData`/`HapticsData` を除去 + `KnownPairs` に追記)。設計は [16_camera_haptics.md](16_camera_haptics.md) §C-2、実装メモは同ファイルの「実装メモ（2026-09-14、5-2c）」を参照。合わせて 5-2 で発生した `DDrive.Runtime.Camera` ⇄ `UnityEngine.Camera` の名前空間衝突を `DDrive.Runtime.CameraShake` への改名で整理した(別コミット。docs/16 の「実装メモ（2026-09-14、5-2 整理）」参照)。

事前準備: ゲームパッド(Xbox/PlayStation 系)を PC に接続しておくと Test on Pad が確認できる。無い環境では「波形のみ確認できます」の案内が出ることを確認すればよい。

確認手順:

1. **専用エディタを開く**: `Tools > D-Drive > Editors > Shake / Haptics` を開く → `Assets/GameData/Camera/Demo/SHAKE_Demo_DemoHitSmall.asset` を対象アセット欄にドラッグ(または Asset Browser の「Shake Editor で開く」)→ Shake 用の項目(Pattern/PosAmplitude/…)と波形プレビューが表示されること
2. **確認用シーンで実際に揺らす(AC)**: ツールバー「確認用シーンを開く」→ `CameraShakePreviewScene` が開く(無ければ生成される)→ ウィンドウの「▶ Shake 再生(連打可)」を押す → **SceneView / Game ビューでカメラが実際に揺れる**こと(ウィンドウ内には何も描かれないこと)
3. **連打テスト(AC)**: 手順2のボタンを素早く連打する → 例外が出ず、揺れが異常に大きくなったりカクついたりしないこと(Trauma 合成の効果)。「合成中の Instance」の数が連打に応じて増減すること
4. **閉じるとカメラが元の位置に戻る(AC)**: 手順2で揺れているままウィンドウを閉じる → Hierarchy 上でカメラの親が元に戻り(`DDriveCameraShakeNode` が残らない)、カメラの位置・回転が揺らす前と同じであること
5. **プリセット 10 種(AC)**: 「プリセット(10種)」を開き、Pulse/Rumble/Heartbeat/Explosion/Hit_Small/Hit_Large/Landing/Earthquake/Alarm/Engine を順に押す → そのたびに波形プレビューとパラメータ欄の数値が変わり、Ctrl+Z で直前の値に戻ること
6. **Haptics Editor に切り替える**: 対象アセット欄に `Assets/GameData/Haptics/Demo/HAPTIC_Demo_DemoHitPunch.asset` を入れる → Low/High の波形プレビューと「Test on Pad」ボタンが出ること
7. **Test on Pad で実際に振動させる(AC)**: パッドを接続した状態で「▶ Test on Pad」を押す → パッドが振動すること。「■ 停止」を押すと即座に止まること
8. **止め忘れ防止(AC)**: 手順7の直後にウィンドウを閉じる/エディタからフォーカスを外す(Alt+Tab)/Play ボタンを押す、のいずれかを行う → いずれの場合もパッドの振動が確実に止まること
9. **パッド未接続時の案内**: パッドを外した状態でウィンドウを開く → 「パッド未接続です」の案内が出て、波形の確認だけができること(エラーは出ない)
10. **Haptics のプリセット 10 種**: Shake と同様にプリセットボタンを押して数値・波形が変わり、Undo で戻ることを確認する
11. **自動テストの確認(代替可)**: 目視確認が難しい場合、`SceneCameraShakePreviewDriverTests`(カメラの姿勢復元・DontSave 付与・連打)/ `EditorHapticsPreviewDriverTests`(Fake 出力での 0 復帰)/ `CameraFxPresetsTests`(10 種 × Undo)で代替できる

要判断:
- **プリセットの具体的な数値は暫定値**: `CameraFxPresets` の 10 種(Shake/Haptics 各)は「こういう性格の揺れ・振動」という設計意図を反映したたたき台で、デザイナーによる実プレイでの調整前提。特に Earthquake/Alarm/Engine は Loop(無限)にしているため、実際に使う際は `CameraFx.Shake`/`Haptics.Play` の呼び出し側で明示的に `Stop` する運用になる点に注意
- **波形プレビューは近似表示**: `WaveformGraphGui` の pos/rot 波形は `CameraFxManager.SampleWave` の乱数位相(`Random.value` によるインスタンスごとのシード)を再現しておらず、Pattern ごとの疑似オシレーションで「だいたいこんな感じ」を示す参考表示にとどめた(§C-2 に明記の「編集 UI は ValueDefDrawer を使い、専用のカーブエディタは作らない」という方針を踏まえ、波形表示に工数をかけすぎない判断をした)。より正確な波形が必要になったら `CameraFxManager` 側の乱数シードを外部から注入できるようにする等の見直しが要る
- **PresentationEditor(5-4)内の同時プレビューは対象外**: チケット文面どおり、Shake + Haptic + SE + VFX の統合プレビューは 5-4 で実装する。5-2c で用意した `SceneCameraShakePreviewDriver`/`EditorHapticsPreviewDriver` はそのまま 5-4 から呼べる設計にしてある(コンストラクタで `AssetRegistry` を外部から差し替え可能)
- **`CameraFxEditorWindow` 自体の UI テストは書いていない**: 既存の VFX/Anim/Model 系エディタと同じく、このプロジェクトには EditorWindow の `CreateGUI` を直接テストする前例が無いため、実体である Driver / Presets 側のテストで代替した(詳細は docs/16 実装メモ)
- **Test on Pad のフォーカス喪失判定はエディタアプリ全体が対象**: `EditorApplication.focusChanged` を使っているため、Unity エディタの別ウィンドウ(Scene/Game/Inspector 等)に切り替えるだけでは止まらず、**Unity エディタ自体から他のアプリへ切り替えたとき**に止まる。ウィンドウ単位のフォーカス喪失(`EditorWindow.OnLostFocus`)で止めるべきという意見があれば見直すこと

## 5-4 Presentation エディタ（PR #24）

対象: `Editor/Presentation/{PresentationEditorWindow,PresentationEditorWindow.Tracks,PresentationEditorWindow.Preview,ScenePresentationPreviewDriver,PresentationTrackEditOps,PresentationTrackKindMapping}.cs`(新規)、`Editor/Preview/EditorAudioFactory.cs`(新規、`SceneAnimPreviewDriver` と共用)、`Editor/Common/TimelineRulerGui.cs`(新規、`AnimEditorWindow` と共用)、`Editor/Preview/EditorAnchorRegistry.cs`(`BgmData`/`CameraShakeData`/`HapticsData` を登録に追加)、`Runtime/Presentation/PresentationDataValidator.cs`(`RequiresAsset` を `public` 化)、`Tests/Editor/DataEditorRegistryTests.cs`(Exempt から `PresentationData` を除去 + `KnownPairs` に追記)。設計は [08_presentation.md](08_presentation.md) §4、実装メモは同ファイルの「実装メモ（2026-09-14、5-4）」を参照。

確認手順:

1. **専用エディタを開く**: `Tools > D-Drive > Presentation Editor`(Tools メニュー最上段)を開く → 対象アセット欄に `Assets/GameData/Presentation/Demo/PRES_Demo_SkillSlash.asset` をドラッグ(または Asset Browser の「Presentation エディタで開く」)→ 既存の Tracks がタイムライン(レーンごとに色つきマーカー)と下の一覧に表示されること
2. **D&D でトラック追加(AC)**: Asset Browser または Project ウィンドウから任意の `VfxData`/`SeData` をタイムラインへドラッグ＆ドロップする → 落とした位置の時刻・対応する Kind のレーンに新しいトラックが追加されること。Undo(Ctrl+Z)1 回で追加前に戻ること
3. **時間ドラッグ(AC)**: 追加したトラックのマーカーを左右にドラッグする → 一覧側の Time も連動して変わること。ドラッグを離した後に Undo 1 回でドラッグ前の時刻に戻ること(ドラッグ中の細かい移動がまとめて 1 回になっていること)
4. **複製・削除(AC)**: 一覧のいずれかのトラックで「複製」→ 直後に同じ内容のトラックが増えること、Undo 1 回で戻ること。「削除」→ そのトラックが消えること、Undo 1 回で戻ること
5. **モデルを配置して統合プレビュー(AC ★目玉機能)**: ツールバー「確認用シーンを開く」→ 確認用シーンが開く。「モデル選択」に Animator 付きの `ModelData` を選び「配置」→ シーン原点にモデルが出る。「▶ 再生」を押す → **Vfx・Se・CameraShake・Haptic(パッド接続時は実機振動込み)が SceneView / Game ビューで同時に再生される**こと。ウィンドウ内には何も描かれないこと
6. **Signal 手動発火(AC)**: `onHit`(SignalKey="hit")のような On Signal トラックがあるデータで再生中、「Signal レーン」の「Signal: hit」ボタンを押す → CameraShake/Haptic/SE 等の onHit 側トラックがその場で発火すること。ログ欄に `Fired: ...` が追加されること
7. **速度・シーク・ループ**: 「速度」スライダーを 0.5x 程度にしてから再生 → 通常よりゆっくり進むこと。「シーク」を動かす → 再生ヘッドがその位置に飛び、通過済みのトラックがまとめて発火すること。「ループ」を ON にして再生 → 完了後に自動で最初から再生し直すこと
8. **パラメータ上書き**: いずれかの Vfx トラックの「Params(パラメータ上書き)」を開く → **2026-09-14 対応済み(5-R)**: 参照先 VFX に `Params`(`VfxParam[]`)が設定済みなら、その `Label` 一覧(`[0]=Alpha, [1]=Size, …`)がヒント表示されること。要素を追加してインデックス順に値を変え(例: `[0]` に Color、`[1]` に Float)、再生し直す → その VFX の見た目(色・サイズ等、参照先 `VfxParam.TargetProperty` に対応するマテリアルプロパティ)に実際に反映されること。**SE トラックは要判断のまま**(音量等への反映経路が無いため、値を入れても音には反映されない)
9. **（2026-09-14 追加、P5 レビュー第 1 弾 5-R）Kind 変更時の Asset 整合**: いずれかのトラックの折りたたみを開き、「Kind」を(例)Vfx → Se に変更する → その場で行が再構築され、「Asset」欄が Se 用(SeData を受け付ける `ObjectField`)に切り替わり、直前まで入っていた Vfx の参照が空にクリアされること(Undo 1 回で Kind 変更前に戻ること)。以前は Asset 欄の型が古い Kind のまま残り、`Kind=Se, Asset.Type=Vfx` のような不整合データが保存され得た
10. **（2026-09-14 追加、P5 レビュー第 1 弾 5-4 追補 b）プレビュー中の HitStop で全体が止まる**: HitStop トラック(または `handle.Signal`/デバッグ経由で HitStop を発火)を含むデータを再生する → HitStop 中は AtTime トラックの進行だけでなく、**Anim/Vfx/Haptic の見た目・振動も一緒に止まる**こと(以前はこれらが独立した Unscaled dt で自走しており、AtTime だけが止まって見た目が止まらなかった)。**CameraShake(揺れ)だけは HitStop 中も揺れ続けること**(ランタイム仕様どおりの意図的な挙動。バグではない)
11. **環境切替**: 「環境切替」を開き、ライト強度スライダーと背景色を変える → 確認用シーンの実際のライト・カメラの背景色がその場で変わること(ウィンドウを閉じても戻らない = 通常の SceneView 操作と同じ、揺れ/振動のような自動復元はしない)
12. **閉じると残骸が消え、カメラが元の位置に戻る(AC)**: 手順5で再生中(特に CameraShake が効いている状態)にウィンドウを閉じる → Hierarchy に `[D-Drive] Presentation Preview` や `[D-Drive] Anim Preview` 等の残骸が残らないこと、`DDriveCameraShakeNode` が無くカメラが元の親子構造・位置・回転に戻っていること(`SceneCameraShakePreviewDriver` 側の既存動作をそのまま利用)
13. **Validation バナー**: Tracks の Asset を意図的に空にする、または OnSignal の SignalKey を空にする → ウィンドウ上部の「検証(Validation)」に Error/Warning が表示されること
14. **自動テストの確認(代替可)**: 目視確認が難しい部分は `PresentationTrackKindMappingTests`(D&D の Kind 判定)/`PresentationTrackEditOpsTests`(追加・移動・削除・複製の Undo 往復)/`ScenePresentationPreviewDriverTests`(Play/Signal/Tick/Cancel/モデル配置、Fake Registry で実プロジェクトに触れない)/`EditorHapticsPreviewDriverTests`・`SceneVfxPreviewDriverTests`・`SceneAnimPreviewDriverTests`(HitStop スケーリング)で代替できる

要判断:
- ~~Params(パラメータ上書き)の実消費経路が無い~~ → **2026-09-14 対応済み(5-R、VFX のみ。上記手順8参照)**。SE(音量等)は `AudioManager` に `SetVolume`/`SetPitch` はあるが、`VfxData.Params` に相当する「ラベル付き配列」が `SeData` に無く、同じインデックス↔Label 方式を機械的に適用できないため要判断のまま
- ~~HitStop がプレビュー内の Anim/Vfx/Shake/Haptic の Tick を止めない~~ → **2026-09-14 対応済み(5-R、上記手順10参照)**。CameraShake だけは仕様どおり対象外
- **LazyLoad アセットの事前解決は未対応**: `EditorAnchorRegistry.Build()` が起動時に一括解決した ID しか実体が見えない。ウィンドウを開いたまま新しく作成した Data を参照する場合は、ウィンドウを閉じて開き直す(Registry を作り直す)必要がある
- **Bgm/Canvas/UiTween トラックはプレビュー未配線**: `PresentationManager` の「Manager 未設定」警告 + no-op で継続する(Se/Vfx/Anim/Anim2D/CameraShake/Haptic/HitStop/Marker/Signal のみプレビューで実際に動く)
- **タイムラインのレーンは 6 グループにまとめた固定行**: Kind ごとに 1 行(13 行)ではなく関連 Kind をまとめた 6 行にしたため、同じレーンに近い時刻のトラックが並ぶと視覚的に重なることがある。~~演出が複雑になってきたら個別レーン化や横方向のズームを検討する~~ → **2026-09-14 対応済み(5-4 追補。下記節を参照)**。レーンのグループ化自体は変更していない(縦方向の重なりは残る)
- **環境切替(ライト強度・背景色)は自動復元しない**: Shake/Haptic のような「閉じたら必ず元に戻る」プレビュー専用の仕組みとは異なり、確認用シーンの実オブジェクトを直接書き換えるだけの薄い UI にした(ユーザーが手動で SceneView を触るのと同じ扱い)。誤操作で確認用シーンの見た目が変わったままになりうる点は許容した

## 5-4 追補: タイムラインのズーム・尺 0 対応・一時停止からの再開（2026-09-14）

ユーザーが実際に触った結果の 2 件のフィードバックへの対応。設計・実装メモは [08_presentation.md](08_presentation.md) の同名見出しを参照。

**(A) タイムラインのズーム/追従/ルーラーでシーク**(報告: 「シークバーでどこにいるか分からない。目盛りの表示範囲が狭すぎる。拡縮が欲しい」— 実際の原因は次の (B) の「尺 0」だったが、拡縮自体も別途実装した)。

1. **ズームスライダー / ± ボタン**: タイムライン上部のズーム行で「－」「＋」を押す、またはスライダーを動かす → 表示範囲の中心を基準に拡大縮小されること(ルーラーの目盛りが 1s → 0.5s → 0.1s → フレーム(1/60s)の順に自動で切り替わり、ラベルが重ならないこと)
2. **Ctrl+ホイールでズーム(AC)**: タイムライン上でマウスカーソルを置いたまま Ctrl(Mac は Cmd)+ホイール → **カーソル位置の時刻を保ったまま**拡大縮小されること(カーソルの真下の目盛りが左右にずれないこと)
3. **ホイール/下部スクロールバーでパン**: ズームインした状態でタイムライン上をホイール操作(単独)する → 表示範囲が左右にスクロールすること。タイムライン下部の横スクロールバー(ミニマップ)のつまみをドラッグ、または空いている場所をクリックしても同様にスクロールできること
4. **全体表示**: ズーム/スクロールした状態で「全体表示」ボタンを押す → 演出の尺全体(尺 0 のときは (B) のフォールバック尺)が幅に収まる表示に戻ること。**別のアセットに切り替えると自動でこの状態に戻ること**(ズーム/スクロールの記憶は同じアセットの間だけ)
5. **再生ヘッドに追従(AC)**: 「再生ヘッドに追従」を ON(既定)のまま、ズームインした状態で再生する → 再生ヘッド(縦の目立つ黄色い線)が表示範囲の外に出ないよう、タイムラインが自動でスクロールすること。上部に現在時刻(秒、小数 2 桁)とフレーム数(60fps 換算の表示専用値)が出ること。OFF にすると自動スクロールしなくなること(再生ヘッドが表示範囲外に出たら線は描かれない)
6. **ルーラーでシーク(AC)**: タイムライン上段(ルーラー)をクリック/ドラッグする → 既存の「シーク」スライダーと同じ値に連動してシークされること。トラックのマーカー(レーン側、ルーラーより下)をドラッグしても、ルーラーのシークとは競合しないこと(時刻変更の Undo 1 回は従来どおり)

**(B) 尺(TotalDuration)が 0 のときの表示範囲**(実際にユーザーが「分からなかった」原因)。

7. **尺 0 でもタイムラインが潰れない**: `TotalDuration=0` で `OnSignal` トラックのみ(または AtTime トラックが無い)の `PresentationData` を開く → タイムラインが極端に狭くならず、既定で 1 秒分の幅が表示されること。AtTime トラックがある場合は、その最大時刻+0.5 秒が表示範囲になること(ランタイムの完了判定(`PresentationTiming.EffectiveDuration`)は変更していないため、実際の再生・シークの上限は従来どおり)
8. **「尺が未設定です」の表示**: `TotalDuration=0` のデータを開く → タイムライン右上に小さく「⚠ 尺が未設定です」と出ること。「共通設定」の尺 0 警告(HelpBox)にも同じことが出ること
9. **「トラックの最後に合わせる」ボタン**: 手順8の HelpBox にあるボタンを押す → `TotalDuration` が各トラックの終了時刻(Anim/Anim2D はクリップの長さ、SE はクリップの長さ-再生開始位置を加味。それ以外の種別は最大時刻+0.5 秒)の最大値に設定されること。Undo(Ctrl+Z)1 回で 0 に戻ること

**(C) 一時停止からの再開**(報告: 「一時停止から再生するとシークバーで最初から再生になっている」)。

10. **▶ 再生は再開になる(AC)**: 統合プレビューを再生 →「⏸ 一時停止」を押す(ボタンの表示が「▶ 再開」に変わること)→「▶ 再生」を押す → 最初からではなく、**一時停止した位置から再開**すること
11. **⏮ 最初から**: 手順10のように一時停止中に「⏮ 最初から」を押す → 一時停止を解けて最初から再生し直すこと。「■ 停止」→「▶ 再生」でも同様に最初から再生されること
12. **ステータス表示とシークスライダーの追従**: 再生中・一時停止中ともステータス欄が「● 再生中 42% (1.26s)」「⏸ 一時停止中 42% (1.26s)」のように時刻(秒、小数 2 桁)も出ること。「シーク」スライダーをドラッグしていない間は現在位置に自動で追従すること(ドラッグ中は追従で上書きされないこと)。ルーラーのシーク(手順6)と同じ値を共有すること
13. **巻き戻しの注意書き(1 回だけ)**: 再生中(または一時停止中)にシークスライダーを現在位置より手前に動かす(巻き戻す)→ ログ欄に「巻き戻しでは発火済みのトラックは再発火しません。最初から確認するには ⏮」が 1 回だけ出ること(同じ再生の間に何度巻き戻しても増えないこと。「⏮ 最初から」または「■ 停止」→「▶ 再生」で次の再生からまた 1 回出る)

要判断:
- **各トラックのアセットの長さ見積り(手順9)はベストエフォート**: Anim/Anim2D(`AnimData.LengthSec`)と SE(`Clips` の最長 AudioClip.length)だけ対応した。VFX/BGM/CameraShake/Haptic はループ/曲線ベースで固定長を持たないため「分からない」扱いのまま(最大時刻+0.5 秒で近似する)。より正確な見積りが必要になったら各 Data 型に明示的な長さの概念を足すことを検討する
- **ズーム/スクロール位置はウィンドウのフィールドで持つ**(`PresentationData` には保存しない)。ウィンドウを閉じて開き直すと最後の状態を覚えているが、対象アセットを切り替えると全体表示に戻る(仕様どおり)

## 5-8 Presentation ネット再生（PR #25）

対象: `Runtime/Net/PresentationMessages.cs`(新規)、`Runtime/Presentation/{PresentationManager,PresentationData,PresentationDataValidator}.cs`、`Runtime/Loop/DDriveRuntimeBootstrap.cs`、`Tests/Runtime/PresentationNetTests.cs`(新規)。設計・実装メモは [14_networking.md](14_networking.md) §5「実装メモ（2026-09-14、5-8）」を参照。

**重要な前提**: `DDriveRuntimeBootstrap` の `NetBridge` は本チケットでも変更せず **常に `LocalLoopbackBridge`** のままにした(NGO 実配線は Phase 6 の範囲、`docs/11_tasks.md` 0-14 行にも「実機 2 クライアントでの再確認が必要」と明記されている)。つまり現時点で **MPPM(Multiplayer Play Mode)で Host + Client を実際に立てても、Presentation のネット再生は起動しない**(`NgoNetBridge` が Bootstrap に繋がっていないため)。以下、**今確認できること**と**NGO 配線後(Phase 6 待ち)に確認すること**を見出しで分ける。

### 今確認できること(PlayMode 自動テスト / Loopback 単体)

1. **自動テストで代替**: `Tests/Runtime/PresentationNetTests.cs`(PlayMode)を実行する(Test Runner または isuzu MCP の `test_run mode=play filter=DDrive.Tests.Runtime.PresentationNetTests`)→ 以下がすべて green であること
   - `NonPredicted_200msLatency_BothPeers_ReachSameNormalizedTime`: 200ms 遅延を模擬した 2 つの Fake ブリッジ越しに Host が Cosmetic Presentation を再生 → Host/Client 双方の `NormalizedTime` が一致する(AC「遅延 200ms 環境で 2 クライアントの位相が揃う」の自動検証)
   - `PredictLocal_ActorPlaysImmediately_AndDoesNotDoubleFireOnConfirm`: `PredictLocal=true` の演出は `Play()` の戻り値が即座に有効になり、確定 Broadcast 受信後も Marker が再発火しない(二重発火しない)
   - `Signal_RelaysThroughHost_BothPeers_ApplyHitStop`: Host が `Signal("hit")` を発行 → Host/Client 双方の `TimeService.TimeScale` が 0 になる(Signal 中継で両方 HitStop する AC の自動検証)
   - `Haptic_LocalPlayerOnly_DoesNotFire_OnRemoteReceivedInstance_ButFiresOnPredictedLocal`: 予測再生した行為者自身では `LocalPlayerOnly` の Haptic が鳴り、ネット受信した Instance では鳴らない(誤爆防止、オーケストレーターの追加指示分)
   - `NoNetBridge_CosmeticPresentation_PlaysFullyLocally`: netBridge が無ければ Cosmetic でも常にローカル再生する回帰確認
2. **単体シーンでの目視確認(Loopback、参考程度)**: `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity` を開き Play Mode に入る → `DDriveRuntimeBootstrap` は `LocalLoopbackBridge` なので、`PRES_Demo_SkillSlash` の `Flags.Net` を `Cosmetic` に変更すれば「自分の Broadcast を自分が受信して再生する」経路を通ることを Console ログ・見た目で確認できる(2 クライアントの位相は確認できない。1 台だけの動作確認)。**要判断**: このデモアセットの `Flags.Net` は 5-1〜5-4 時点では `Local`(既定)のままのため、ネット経路を通したい場合はデモ側の変更が必要(今回は変更していない。テストのみで検証)

### NGO 配線後(Phase 6 待ち)に確認すること

3. **MPPM で Host+Client を立てて 200ms 遅延を模擬**: Phase 6 で `NgoNetBridge` が `DDriveRuntimeBootstrap` に配線されたら、Multiplayer Play Mode + Unity Transport の Simulate Latency 設定(または Network Simulator パッケージ)で 200ms を模擬し、剣攻撃デモ(`PRES_Demo_SkillSlash`、`Flags.Net=Cosmetic` に変更)を再生 → Host/Client 両方の Game ビューで Vfx/Se/CameraShake の見た目のタイミングが揃うこと
4. **Signal で両方揺れる**: 上記構成で当たり判定側(Host)が `Signal("hit")` を発行 → Host/Client 両方の画面で CameraShake が揺れ、パッド接続時は両方(または LocalPlayerOnly の設計に応じて行為者のみ)で振動すること
5. **実機 2 台 + 実 LAN**: [14_networking.md] §12 の MS2026 統一ルールに従い、最終的には実機 2 台 + 実 LAN で確認する(このセッションでは未実施)

要判断:
- **relay の簡略化**: `PresentationNetTests` の `DelayedNetworkRelay`/`DelayedNetBridge` は「Broadcast は送信者が Host/Client のどちらでも単一ホップの遅延で全員に届く」という簡略化をしている(実際の NGO は Client→Host→全員の 2 ホップ)。Manager 側のシーク/重複抑制ロジックの検証には影響しないと判断したが、正確な往復遅延を検証したい場合は 6-7(NGO 実接続の CI テスト)で見直すこと
- **HandleNetKey の一意性**: `NextHandleNetKey()` はインスタンスごとの乱数 salt と NetworkTime のビット・ローカル連番を混ぜて生成しており、`INetBridge` に `LocalClientId` が無いため厳密な一意性(clientId を上位ビットに埋める等)は保証していない。衝突確率は十分低いと判断したが、Phase 6 で `LocalClientId` 相当が手に入ったら見直すこと
- **Haptic の LocalPlayerOnly 誤爆防止の副作用**: `PredictLocal=false` の Presentation では、行為者自身も「自分の Broadcast を受信して初めて再生する」経路(`PlayedViaNetworkReceive=true`)を通るため、`LocalPlayerOnly=true` な Haptic は行為者自身にも鳴らなくなる。行為者に確実に振動させたい演出は `PredictLocal=true` にする運用で回避できるが、本来は `SelfNetId` の実解決で解決すべき問題(Phase 6)
- **観戦者の HitStop**: 1v1 前提のため「Signal を受け取った全ピアが HitStop する」を既定にした。3 人以上の構成になった場合は観戦者を除外する仕組みが必要(オーケストレーターの追加指示への対応、[14] §5 実装メモにも記載)
- **Seed の実消費経路が無い**: `PresentationPlayMsg.Seed` は生成・伝搬するだけで、SE のランダム選択・PitchRange への接続は行っていない(5-8 のスコープ外と判断)。実際に必要になった時点で `AudioManager` 側に Seed を渡す口を追加すること
- **Anim 以外の位相同期は近似**: `Bgm` トラックは頭から再生するだけで、`BgmManager` に再生位置を指定する API が無いため厳密な位相合わせは未実装(`Anim`/`Anim2D` のみ `AnimManager.Seek` で実際に位相を合わせている)

## 5-9 Late Join 復元（PR #25）

対象: `Foundation/Net/INetBridge.cs`(`ClientConnected` イベント追加)、`Foundation/Net/LocalLoopbackBridge.cs`/`Runtime/Net/NgoNetBridge.cs`/`Tests/Runtime/{FakeNetBridge,CountingNetBridge}.cs`(同イベント実装)、`Runtime/Presentation/PresentationManager.cs`(アクティブ演出台帳 + Late Join 送信)、`Tests/Runtime/PresentationLateJoinTests.cs`(新規)。設計・実装メモは [14_networking.md](14_networking.md) §5 実装メモを参照。5-8 と同じ前提(`NgoNetBridge` は Bootstrap 未配線)のため、こちらも見出しを分ける。

### 今確認できること(PlayMode 自動テスト)

1. **自動テストで代替**: `Tests/Runtime/PresentationLateJoinTests.cs` の以下が green であること
   - `LateJoin_LoopingVfx_RestoredWithSeek_ForNewlyConnectedClient`: 常駐 VFX を模した長尺 Cosmetic Presentation(`VfxData.LifeMode=Loop`)を Host が再生 → 5 秒後に新しいクライアントが接続 → 途中参加クライアントの `VfxManager.ActiveCount` が 1(シーク状態で復元される。AC「途中参加でループ VFX/BGM が復元」の VFX 側を自動検証)
   - `LateJoin_OneShotPresentation_IsNotRestored_AfterItCompletes`: 短命なワンショット Presentation が尺を超えて `Complete()` した後に新規クライアントが接続 → 何も復元されない(`DebugActiveHandles()` が空)
   - **注**: BGM のループ復元(AC 文言の「BGM」側)は専用テストを書いていない(`BgmManager` に位置シーク API が無く、`Anim`/`Anim2D`/`Vfx` と同じ仕組み(`IsContinuousAtSeek` で `TrackKind.Bgm` は常に continuous 扱い)で理論上は復元されるはずだが、実際に鳴り始めることの検証は未実施。要判断参照)
2. **`INetBridge.ClientConnected` の手動発火確認**: `LocalLoopbackBridge.RaiseClientConnected(clientId)` / `FakeNetBridge.RaiseClientConnected(clientId)` はテスト専用の手動発火 API(シングルプレイでは通常誰も接続してこないため、本番コードから呼ばれることはない)

### NGO 配線後(Phase 6 待ち)に確認すること

3. **MPPM で途中参加**: Phase 6 で `NgoNetBridge` が配線されたら、Host を先に起動して常駐 VFX/BGM を含む Presentation を再生した状態で、後から Virtual Player(Client)を接続 → 接続直後に途中参加側の画面でも VFX が(途中の見た目から)再生され、BGM が鳴り始めること。ワンショットの演出(既に終わっている攻撃演出等)は再現されないこと
4. **切断・再接続**: 途中参加した Client が切断して再接続した場合の挙動(MS2026 Networking.md §4 のチェック観点「途中で切断したときに固まらないか」)は本チケットのスコープ外だが、Phase 6 で合わせて確認すること

要判断:
- **BGM のループ復元は理論上のみ**: 上記のとおり `IsContinuousAtSeek` は `TrackKind.Bgm` を継続系として扱うため、Late Join でも「頭から再生される」形で復元されるが、`BgmManager` は Seek API を持たないため厳密な位相合わせはしない(5-8 実装メモの要判断と同じ)。実際に BGM を含む常駐演出を作る際は、この「頭から再生される」挙動で十分かデザイナーに確認してもらうこと
- **専用の Late Join メッセージを用意しなかった**: `PresentationPlayMsg` をそのまま `SendTo` するだけにしたため、Late Join で復元される演出は「Broadcast で送られたときと全く同じ形」でしか復元できない(将来、Late Join 専用の追加情報(例: 現在の Signal 発火済み状態)が必要になったら別メッセージの追加を検討すること)
- **アクティブ演出台帳はメモリ上のみ**: `_activeNetworked` は Host の `PresentationManager` インスタンスが保持するだけで、Host が再起動すると消える(想定どおり。永続化の要件は無い)
- **常駐 VFX/BGM を「Presentation でラップする」運用が前提**: 5-9 は `PresentationManager` 経由の Late Join のみ対応する。`Vfx.Spawn`/`Bgm.PlayBgm` を Presentation を介さず直接呼んだ Cosmetic な常駐エフェクトは、この台帳に乗らないため Late Join で復元されない(別途 `VfxManager`/`BgmManager` 自身に台帳を持たせる改修が必要。Phase 6 以降の課題として明記する)

## 6-0 NGO 統合 + 実機 2 台確認環境

対象: [11_tasks.md](11_tasks.md) Phase 6 の 6-0 行、[14_networking.md](14_networking.md) 実装メモ（2026-09-14、6-0）。
5-8/5-9 が「NGO 未配線」を前提に書いた要判断のうち、NGO 実配線・実機 2 台確認に関わる部分はこのチケットで
解消した(発行者検証・SelfNetId/TargetNetId 実解決・LocalClientId・Client 行為者テスト等)。

**実機 2 台(PC-A=Host / PC-B=Client)での確認手順・コマンドライン引数・ログの判定基準は
[29_network_device_test.md](29_network_device_test.md) に一本化した(§3〜§5)。** ここでは確認項目の
チェックリストだけを示す(PC-B での実施はオーケストレーターが担当。2026-09-14 時点で未実施)。

- [ ] 2 台での接続(`heartbeat` ログが両端末に出る、`NetDebugOverlay` の Role/ClientId 表示)
- [ ] Presentation の位相(Host/Client の `heartbeat.networkTime` が RTT 相応の差に収まる)
- [ ] Signal 中継(`signal=hit` が両端末に出て、HitStop が両端末で発生する)
- [ ] Late Join(Client を後から起動 → `activeCount` が 0→1 に変わる行が出る)
- [ ] 遅延 200ms(`-ddrive-sim-latency 200` を付けても接続・再生が成立する)
- [ ] 偽造メッセージ破棄(`forged_cancel_sent` の直後に `[Net/Host]`/`[Net/Client]` の破棄警告が出て、`activeCount` が変化しない)
- [ ] ファイアウォールの許可ダイアログ(初回起動時に出た場合、PC-B 側で「プライベート ネットワーク」を許可した旨を記録する。Claude からは操作できない)
- [ ] (このセッションで実施済み)ローカル(127.0.0.1)2 プロセスでの結合確認 — 結果は [29] §7 参照

要判断:
- **`-ddrive-autotest` は成否を自動判定しない**: ログ出力のみで、CI 的な pass/fail 判定は将来必要になったときに追加する
- **NetworkManager をシーンに置く前提**: `DDriveRuntimeBootstrap` は NetworkManager を動的生成しない(6-0 の設計判断、[14_networking.md] 実装メモ参照)。ゲーム側シーンにも同様の配置が必要になる

## O-14 ログイン許可の Web 管理（2026-09-14 実装）

対象: [32_spec_web.md](32_spec_web.md)「実装メモ（2026-09-14、O-14）」、`Tools/SpecWeb/src/Api/UserAdmin.js`・
`src/adapters/DriveAdapter.js`・`src/Auth.js`・`src/Code.js`、`html/Members.html`。

Node テスト（280 件、`usersAdmin.test.js`・`members.smoke.test.js` 等）はサーバー側のロール判定・
ロックアウト防止・クライアント側の DOM 操作を検証済みだが、**実際の Google アカウントでのログイン・
Drive フォルダの共有通知メール送信は Node テストの範囲外**。以下を実デプロイで確認してほしい
（前提: `Tools/SpecWeb/README.md` の手順でデプロイ済み、あなた自身が admin として登録済み）。

1. ① 人向け SPA にログインし、「メンバー」画面下部の「ログイン許可（users.json）」セクションを開く。
   自分自身の行（admin）はロール変更・削除ができない（ボタン/ドロップダウンが無効化されている）ことを確認する
2. 別アカウント（テスト用に用意した 2 つ目の Google アカウント）のメールアドレスを、表示名・ロール
   （まずは `viewer` で十分）とともに「追加」する。「データフォルダを編集者として共有する」チェックは
   ON のままにしておく
3. **その別アカウントに Google から Drive の共有通知メールが届くこと**を確認する（届かない場合は
   `appsscript.json` の `oauthScopes` が Drive の共有操作に足りているか確認する。§9 要判断・実装メモ参照）
4. その別アカウントでブラウザにログインし、① のデプロイ URL を開く。「メンバーのみ利用できます」
   ではなく、通常の SPA（発注ツリー等）が開けることを確認する
5. 元の admin アカウントに戻り、「ログイン許可」セクションで手順2で追加したユーザーを「共有も解除」
   チェックを ON にして削除する
6. その別アカウントで①のデプロイ URL を再度開き、今度は「メンバーのみ利用できません。管理者にこの
   メールアドレスを伝えて users.json への追加を依頼してください。」の拒否ページが表示され、
   「ログイン中のアカウント: <そのメールアドレス>」が本文に出ることを確認する
7. （任意）admin が自分 1 人だけの状態で、自分自身を削除・降格しようとするとエラーになることを
   確認する（2 人目の admin を一時的に作ってから試すと安全）

要判断（実装時に保守的な既定を選んだ、確認できれば解消）:
- `appsscript.json` は今回変更していない（既存の Drive スコープで `addEditor`/`removeEditor` が
  通ると判断したが未確認）。上記手順3で共有通知メールが届かない・エラーになる場合はスコープの
  見直しが必要になる
