# CLAUDE.md — AI エージェント向けプロジェクト説明書（D-Drive）

> AI（Claude Code 等）が最初に読むファイル。人間向けの入口は [docs/README.md](docs/README.md)。
> **設計の真実は `docs/` にある。コードと docs が食い違ったら、まず docs を確認し、変更したら docs も同時に更新する。**

## 0. TL;DR（これだけは守る）

1. **`.unity` / `.prefab` / `.asset` / 画像 / 音 をテキスト編集しない。** Unity Editor（MCP ツール or 人）経由で行う。`.meta` を手で作らない・消さない・GUID を書き換えない
2. **`Library/ Temp/ Logs/ UserSettings/ obj/ *.csproj *.sln` は生成物。** 読むのは可、編集・コミット不可
3. **禁止 API**: `Instantiate` / `Resources.Load` / `Addressables.Load*` / `Time.time`・`deltaTime` 等の直接参照 / `AudioSource.Play` の静的呼び出し（`ForbiddenApiScanner` が検出。何が当たるかは [docs/42 §5.9](docs/42_distribution.md) の規則の実体。正当な理由があるときだけ行単位の許可コメント `// ddrive-allow: 規則名(理由)`、理由必須・レビューで確認、[docs/42 §5.9](docs/42_distribution.md)）。ランタイム asmdef から `UnityEditor` 参照禁止。定常経路（Tick/Spawn/Play）で LINQ・クロージャ・boxing 禁止（[docs/12_review.md](docs/12_review.md) §3）
4. **例外で止めない。** 警告 + no-op / Placeholder で継続する（デザイナーの作業を止めない）
5. **Data は読み取り専用。** Manager が Data を書き換えない。エディタが書き換えるときは必ず `Undo.RecordObject` + `EditorUtility.SetDirty`
6. **メニューパス直書き禁止。** `DDriveMenu` 定数経由。新規 EditorWindow は `ScrollView` ルート必須（[docs/09_editor_tools.md](docs/09_editor_tools.md) §6-7）。Data 専用エディタには `[DataEditor(typeof(XxxData), "…で開く")]` を付ける（Inspector 最上部の「エディターで開く」が自動で付く。§8）
7. **プレビューは実 Manager を Editor から駆動する**（ADR-4）。Editor 専用の再生経路を作らない。**ウィンドウ内での描画確認は避け、確認用シーン / Prefab を開いて SceneView で確認する**（2026-09-10）
8. **Manager を new するのは `DDriveRuntimeBootstrap`（[docs/02](docs/02_core_framework.md) §14）・テスト・Editor プレビューだけ。** 作成した Data は Addressables に同じ address で登録されていること（AssetBrowser が自動、`Validation > Run All` が検出）
9. 迷ったら実装せずに聞く。特にシリアライズ形式（フィールド削除・型変更）・asmdef 構成・ProjectSettings
10. **互換性ポリシー（[docs/42 §5](docs/42_distribution.md)、2026-09-20 P-13 発効）を守る。** D-Drive は `com.ddrive.core` として持ち込み先（MS2026）から git URL で参照されている。シリアライズ形式・enum・ID/定数名・公開 API（`DDrive.Foundation`/`DDrive.Runtime`）・ContentHash・ネットメッセージ・生成コード・Validation の重さ・外部拡張の契約（[docs/42 §5.14](docs/42_distribution.md)）は **追加のみ**（削除・改名・型変更は MAJOR = [docs/42 §5.12](docs/42_distribution.md) の手続きとユーザー承認が必須、MS2026 開発中は 0 回）。`Tests/Editor/Compat` のスナップショットテストが赤なら変更しない（意図した追加なら `Tools > D-Drive > Compat > スナップショットを更新` + `CHANGELOG.md` の互換性節に追記。[docs/12 §3「互換性」](docs/12_review.md)）

## 1. プロジェクト概要

| 項目 | 値 |
|---|---|
| 名称 | D-Drive（Designer-Driven Re: IDE Visual Environment）。C# 識別子は `DDrive` |
| コンセプト | プログラマーは **ID だけ**でモックを完成させ、デザイナーが専用エディタで中身を作る |
| Unity | **6000.3.13f1**（勝手に上げない）/ URP 17.3 / Addressables / UniTask / NGO 2.13.2 / R3 1.3.1（5-1 で導入。[docs/01](docs/01_architecture.md) §4 外部依存パッケージ参照） |
| テスト | Unity Test Framework。`Packages/com.ddrive.core/Tests/{Editor,Runtime}`（2026-09-20 P-5 で `Assets/DDrive/Tests/` から移設。開発 `Packages/manifest.json` の `testables` で有効化） |
| 進捗 | Phase 0（基盤）・Phase 1（Audio）・Phase 2（VFX + Model + Anchor アセット化 [docs/21](docs/21_anchor_spec.md) + 配置セット [docs/22](docs/22_anchor_group.md)）実装済み。Phase 3 は 3-1〜3-13 まで実装済み（Phase 3 完了）、次は Phase 4（4-4 Prefab → 4-1 Canvas）。0-14 起動配線（`DDriveRuntimeBootstrap`）+ Addressables 同期は 2026-09-09 に追加。Phase 4 完了（4-1〜4-18 実装済み。4-13 の NGO 複製は Phase 6 の NGO 統合で接続）。Phase 5 は 2026-09-14 に全チケット実装完了（5-1〜5-16 + 自前レビュー対応 5-R、レビュー = [docs/30](docs/reviews/30_phase5_review_2026-09-14.md)）。6-0 NGO 統合（Bootstrap で NgoNetBridge 選択可、実機確認 = [docs/29](docs/verification/29_network_device_test.md)）も 2026-09-14 に実装し、**PC-A Host + PC-B Client の実機 2 台で確認済み**（遅延 0ms / 200ms・Late Join・偽造メッセージ破棄・切断、docs/29 §8）。6-10a（Timeline 基盤: CutsceneData/CutsceneManager/`Cutscene.Play`・ネット・入力ロック、[docs/26](docs/26_timeline.md)）は 2026-09-18 に実装完了（EditMode 820/820・PlayMode 706/706 green）。6-10b（D-Drive トラック群 + Camera クリップ + スクラブプレビュー）も同日実装完了（EditMode 820/820・PlayMode 713/713 green。Event/Signal/Shake/Haptic マーカーは Unity 標準 Signal 配送ではなく `CutsceneManager` 独自の時刻カーソル方式、Camera クリップは `DDriveCutsceneCameraApplier`〔実行順 1000〕が LateUpdate で Camera/Volume に書く）。6-10c（Maya FBX → Timeline 自動構築、`CutsceneImportService`/`CutsceneFbxPostprocessor`/`CutsceneImportProfile`）も 2026-09-18 に実装完了。`IImportRuleHandler` は N:1 の対応(1 ショット=カメラ+小物 FBX 1 本+キャラごとの FBX N 本)を表現できないため採用せず、`MayaModelPostprocessor` と同じ専用パイプラインにした(理由は [docs/26](docs/26_timeline.md) 実装メモ)。イベント用ロケーター(アニメ付きカスタムプロパティ→Signal)・実 FBX でのカメラ画角/ピント/絞りの取得可否は実 Maya 素材が無く未検証のまま(§7.3)。6-10d(確認用シーン `CutscenePreviewSceneSetup`〔起動オブジェクト + `CutscenePreviewHarness` 同梱〕・Inspector 導線 `CutsceneDataEditor`・Validator 拡張〔Humanoid/Avatar 不整合・Presentation⇄Cutscene 循環参照・Cosmetic+Simulated 参照・OnSignal 付き Presentation クリップ・fps 検査 6 種・`CameraExecutionOrderValidator`〔実行順の契約検出〕〕・マニュアル更新)も 2026-09-18 に実装完了(EditMode 860/860・PlayMode 717/717 green)。**Timeline(6-10a〜d)完了**。2026-09-19 に Cutscene の Edit Mode プレビュー(Timeline ウィンドウ主導。Edit Mode では `CutsceneManager` を通さない決定、[docs/26 §4.4](docs/26_timeline.md))・Presentation Editor の SceneView Anchor 表示・`TrackKind.AnchorGroup`・Presentation のトラック Anchor とアセット側 Anchor の親子合成(docs/08)を追加。**次は人による確認([docs/43](docs/verification/43_manual_verification_2026-09-17.md) §7〜§10)→ コードレビュー([docs/44](docs/reviews/44_review_2026-09-19.md)、Cutscene 分は別途)→ P チケット(移植・更新・互換性)**。Codex 未レビュー分の自前レビューは Phase 4 = [docs/24](docs/reviews/24_phase4_review_2026-09-11.md)、Phase 3 後半(3-14〜3-21)= [docs/25](docs/reviews/25_phase3_material_anim2d_review_2026-09-11.md)(いずれも 2026-09-11 に対応済み。P4 の整理項目 1〜6 と P4 全体の再レビュー対応は 2026-09-14 に完了、P3 後半の整理項目のみ後続)。**人による確認は [docs/23](docs/verification/23_manual_verification_2026-09-11.md) の手順書を参照**。[docs/11_tasks.md](docs/11_tasks.md)。P5 のユーザー確認は [docs/28 §0](docs/verification/28_manual_verification_phase5.md#0-確認の進め方2026-09-14-まとめ)、要判断は [docs/31](docs/archive/31_phase5_decisions.md)。**P チケット（移植・更新・互換性、[docs/42](docs/42_distribution.md)）は P-1〜P-13 まで完了**（2026-09-20。P-5 で `Assets/DDrive/` を `Packages/com.ddrive.core/` へパッケージ化、P-6 ウィザード、P-7 スキーマ版/マイグレーション、P-8 更新ツール、P-9 リリース道具、P-10 消費側ドキュメント、P-10.5 レビュー = [docs/47](docs/reviews/47_review_p_tickets_2026-09-20.md)、P-11 空プロジェクト導入 = [docs/48](docs/archive/48_p11_install_test_2026-09-20.md)、P-12 MS2026 導入 = [docs/49](docs/archive/49_p12_ms2026_install_2026-09-20.md)、P-13 発効）。**次は人による確認（Timeline は [docs/46](docs/archive/46_cutscene_fbx_request_unitychan.md) の FBX 到着後、docs/43 §7/§10）** |

> **互換性ポリシー発効（2026-09-20、P-13）**: P チケット（移植・更新・互換性、[docs/42](docs/42_distribution.md)）は P-1〜P-13 まで完了し、`Packages/com.ddrive.core` は持ち込み先（MS2026）から git URL（タグ `v1.0.0`）で参照されている。以降の変更は §0-10 の互換性ポリシーに従う。リリース手順は [docs/12 §7](docs/12_review.md)、更新の取り込みは持ち込み先の `Tools > D-Drive > Update`（[docs/42 §4.2](docs/42_distribution.md)）。移植の実施記録は [docs/48](docs/archive/48_p11_install_test_2026-09-20.md)（空プロジェクト）・[docs/49](docs/archive/49_p12_ms2026_install_2026-09-20.md)（MS2026）。**N チケット（4 人対戦向けネット改善、2026-09-22〜24、[docs/11](docs/11_tasks.md) N 表・[docs/14 §14〜§19](docs/14_networking.md)）**: N-1 手動接続 API / N-2 開発用接続 UI / N-3 NetCheck の Host 1 + Client 3 対応 / N-4 `PresentationEffectScope.ParticipantsOnly` / N-5・N-6 MS2026 の Host 引き継ぎ（`03_Network.md` §10.7 D-1〜D-5）向けリセットと `host_migration` シナリオ / N-7 ログ整備。run-netcheck 9 シナリオ PASS + **実機 4 台テスト（A Host / B Client×2 / C Client×1、通常 + Host 引き継ぎ）合格**（[docs/29 §25](docs/verification/29_network_device_test.md)）。v1.2.0 として 2026-09-24 にリリース。

> **FC チケット（T-Drive 連携、2026-10-03 起票）**: T-Drive の FacialController（`com.tdrive.facial`）と Toon（`com.tdrive.toon`）を D-Drive で使うため、旧チケット 7-8（D-Drive 内に Facial を移植）は **T-Drive 版を使う方針に書き換え**、D-Drive 側は FC-0〜FC-20（汎用の拡張点を追加のみ・v1.4.0 MINOR 見込み）に整理した。**設計と一覧は [docs/51](docs/51_tdrive_integration.md)・[docs/11_tasks.md](docs/11_tasks.md) FC 節。2026-10-03 に実装対象は完了（実装済み = FC-0〜FC-7・FC-10〜FC-12・FC-14・FC-15・FC-19・FC-20、保留 = FC-8〔表情アニメの運用決定後〕・FC-9〔7-3 / 7-4 着手時〕・FC-13〔表情パラメータを MS2026 で使い始めるとき〕、後回し = FC-16・FC-17・FC-18）。v1.4.0 は 2026-10-06 にリリース済み（タグ push 済み）、v1.4.1（Canvas Editor の「埋め込み」「ボタンの配線」欄を既定で折りたたみ）は 2026-10-07 にリリース、人による確認は [docs/52](docs/verification/52_manual_verification_fc.md)**。D-Drive のコード・asmdef は `TDrive.*` を参照しない。 **FC・P-15（更新ウィンドウの他パッケージ対応）・U-28（Canvas の埋め込み）・M-4（禁止 API の許可）が v1.4.0（MINOR）に入る。レビューは [docs/53](docs/reviews/53_review_fc_2026-10-03.md)〜、リリース準備（当日の手順・リリースノートの下書き・渡す文面）は [docs/60](docs/archive/60_release_1_4_0_prep.md)。v1.4.0 は 2026-10-06 にリリース済み。**
> **未完了事項の索引と v1.5.0（2026-10-07）**: 未確認・未実装・既知の不具合・判断待ちは **[docs/1001](docs/1001_open_items.md)** に 1 枚で集約した（新しい未完了事項はそこにも 1 行足す。終わったら消す）。**v1.5.0 = D-Drive MCP**（AI 向けの Editor 操作ツール群。isuzu 版 `[McpTool]` に乗る・トークン最小・ポート競合ゼロ）の仕様は **[docs/1002](docs/1002_ddrive_mcp.md)**、決め事は同 §9 Q-1〜Q-12、チケットは **[docs/1004](docs/1004_tasks.md)**（2026-10-07 以降の全チケットはこの 1 枚。docs/11 には足さない）。コーディング作業は Sonnet のエージェントに委任する（2026-10-07 指示）。**新しい docs の番号は `10xx`（4 桁の通し番号、フォルダ横断で共通。[docs/README](docs/README.md)「文書番号の付け方」）。**

## 2. ディレクトリ地図

```
Packages/com.ddrive.core/      ← システム本体(埋め込みパッケージ。2026-09-20 P-5 で Assets/DDrive/ から移設)。層 = asmdef（[docs/01_architecture.md] §4-5）
  Foundation/   Registry / Loader / Pool / Handle / EventBus / Pause / ValueDef / Validation
  Runtime/      種別ごとの Data / Manager / 静的ファサード(Audio, Vfx, Anim ...) / Anchoring(AnchorData・AnchorChain・AnchorGroup・AnchorPoint) / Presentation(PresentationData・PresentationManager・Presentation ファサード・AssetEventDispatcher) / Loop(GameLoopDriver・DDriveRuntimeBootstrap = 唯一の起動配線) / Net / Shaders(DDrive_Lit/Unlit/AiStandardSurface)
  Editor/       AssetBrowser / 各専用エディタ(Audio, Vfx, Model) / Preview / Codegen / Validation / Settings(DDriveProjectSettings)
  Tests/        Editor(EditMode テスト) / Runtime(asmdef が全プラットフォーム対象のため Test Runner では PlayMode テスト。MCP の run_tests は mode=PlayMode で実行する)。開発 `Packages/manifest.json` の `testables` で有効化
  Samples~/     Unity からは見えないサンプル置き場(Demo: NetCheckRunner/NetBridgeSmokeTest/PresentationSkillSlashDemo のスクリプトのみ)
  package.json / README.md / Documentation~/ ← UPM メタデータ
Assets/GameData/               ← ツールが管理する Data(.asset)・カタログ・標準プレハブ・確認用シーン(持ち込み先ごとのデータ、パッケージには入れない)
Assets/Generated/               ← AssetIds.g.cs / Tuning.g.cs(生成物、持ち込み先ごと)
Assets/SourceAssets/           ← 人が管理する実データ(音源・モデル。system 用 shader は Packages/com.ddrive.core/Runtime/Shaders/ へ移設済み)
docs/                          ← 設計書(00〜20 他)。DesignerManual/ProgrammerManual はデザイナー/プログラマー向け HTML(開発リポジトリが正本。パッケージへの同梱は P-9 で同期)
CHANGELOG.md                    ← D-Drive(com.ddrive.core)の変更履歴(リポジトリ直下のまま。[docs/42_distribution.md] §2.2 参照)
```

## 3. 作業手順

1. 関連する設計書（`docs/0X_*.md`）と既存コードを **grep してから** 書く。似たクラスの重複が最大の事故要因
2. 実装 → [docs/12_review.md](docs/12_review.md) §3 のチェックリストで自己レビュー
3. **コンパイル・テスト確認**: Unity MCP が繋がっていれば `read_console` でエラー 0、`run_tests` を **EditMode と PlayMode の両方**で green にする（Tests/Runtime は PlayMode でしか走らない）。繋がっていなければ「未検証」と明示する
4. 公開 API / データ構造 / エディタ機能を変えたら、対応する `docs/` を同じ PR で更新する（変更履歴は該当節に日付付きで追記する慣習）
5. コミットは `main` 直接でよい（現状 1 人開発）。ただし 1 コミット = 1 チケット単位を意識する

## 4. Unity MCP

**状態（2026-10-07、MCP-0）**: Editor 組み込みの **`jp.shiranui-isuzu.unity-mcp` v4.4.2 のみ**（2026-10-07 に v4.2.0 から上げた、MCP-13）（サーバー名 `isuzu-unity`、ポートはプロジェクトパスから自動〔D-Drive は 27725〕、Bearer トークン必須。登録は各自の `claude mcp add`、[docs/20](docs/20_mcp_setup.md) §1）。CoplayDev 版（`com.coplaydev.unity-mcp`、固定ポート 8081、`.mcp.json`）は 2026-10-07 に外した（[docs/1002](docs/1002_ddrive_mcp.md) §9 Q-1。戻し方は [docs/archive/1003](docs/archive/1003_coplaydev_mcp_setup.md)）。v1.5.0 で D-Drive 自身のツール群 `ddrive_*` をこのサーバーに載せる（[docs/1002](docs/1002_ddrive_mcp.md)）。
セットアップ手順・運用ルールは [docs/20_mcp_setup.md](docs/20_mcp_setup.md)。

- 接続前提: Unity 起動中（サーバーは自動起動）+ `%LOCALAPPDATA%/UnityMCP/instances/<hash>.json` の `mcpUrl` / `token` で登録済み。**ポートをリポジトリ・docs に直書きしない、Preferences でポートを固定しない**
- 主なツール: `compile_request` → `compile_status`、`console_read_logs`、`test_run` + `test_results`、`execute_code`（Roslyn）、`menu_execute`、`gameobject_*` / `asset_*` / `prefab_*`
- **Play Mode 中は書き込み系を実行しない**。テスト実行中は `test_results` 以外を呼ばない。同一 PC で別プロジェクト（MS2026）の Unity も開いている場合は記述子の `projectPath` で対象を確認する
- **接続できていないのに Unity を操作した／確認したと報告しない**。セッションにツールが載っていなければ HTTP 直叩き（SKILL.md §2）で続ける

## 5. 参照ドキュメント（抜粋）

| ファイル | 内容 |
|---|---|
| [docs/01_architecture.md](docs/01_architecture.md) | 4 層 / Data・Instance・Handle 分離 / ADR |
| [docs/02_core_framework.md](docs/02_core_framework.md) | AssetId / Registry / Loader / Pool / Event / Validation |
| [docs/03_audio.md](docs/03_audio.md) / [docs/04_vfx.md](docs/04_vfx.md) / [docs/05_model_animation.md](docs/05_model_animation.md) | 種別ごとの詳細設計 |
| [docs/09_editor_tools.md](docs/09_editor_tools.md) | AssetBrowser / プレビュー基盤 / メニュー / ウィンドウ規約 |
| [docs/10_workflow.md](docs/10_workflow.md) | ロール / 命名・配置規約（ツールが維持する） |
| [docs/12_review.md](docs/12_review.md) | PR チェックリスト |
| [docs/reviews/19_vfx_usability_review.md](docs/reviews/19_vfx_usability_review.md) | VFX 使い勝手レビューと改定内容（2026-09-08） |
| [docs/20_mcp_setup.md](docs/20_mcp_setup.md) | MCP セットアップ・運用ルール |
