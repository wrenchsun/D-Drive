# CLAUDE.md — AI エージェント向けプロジェクト説明書（D-Drive）

> AI（Claude Code 等）が最初に読むファイル。人間向けの入口は [docs/README.md](docs/README.md)。
> **設計の真実は `docs/` にある。コードと docs が食い違ったら、まず docs を確認し、変更したら docs も同時に更新する。**

## 0. TL;DR（これだけは守る）

1. **`.unity` / `.prefab` / `.asset` / 画像 / 音 をテキスト編集しない。** Unity Editor（MCP ツール or 人）経由で行う。`.meta` を手で作らない・消さない・GUID を書き換えない
2. **`Library/ Temp/ Logs/ UserSettings/ obj/ *.csproj *.sln` は生成物。** 読むのは可、編集・コミット不可
3. **禁止 API**: `Instantiate` / `Resources.Load` / `AudioSource.Play` の直接呼び出し（`ForbiddenApiScanner` が検出）。ランタイム asmdef から `UnityEditor` 参照禁止。定常経路（Tick/Spawn/Play）で LINQ・クロージャ・boxing 禁止（[docs/12_review.md](docs/12_review.md) §3）
4. **例外で止めない。** 警告 + no-op / Placeholder で継続する（デザイナーの作業を止めない）
5. **Data は読み取り専用。** Manager が Data を書き換えない。エディタが書き換えるときは必ず `Undo.RecordObject` + `EditorUtility.SetDirty`
6. **メニューパス直書き禁止。** `DDriveMenu` 定数経由。新規 EditorWindow は `ScrollView` ルート必須（[docs/09_editor_tools.md](docs/09_editor_tools.md) §6-7）。Data 専用エディタには `[DataEditor(typeof(XxxData), "…で開く")]` を付ける（Inspector 最上部の「エディターで開く」が自動で付く。§8）
7. **プレビューは実 Manager を Editor から駆動する**（ADR-4）。Editor 専用の再生経路を作らない。**ウィンドウ内での描画確認は避け、確認用シーン / Prefab を開いて SceneView で確認する**（2026-09-10）
8. **Manager を new するのは `DDriveRuntimeBootstrap`（[docs/02](docs/02_core_framework.md) §14）・テスト・Editor プレビューだけ。** 作成した Data は Addressables に同じ address で登録されていること（AssetBrowser が自動、`Validation > Run All` が検出）
9. 迷ったら実装せずに聞く。特にシリアライズ形式（フィールド削除・型変更）・asmdef 構成・ProjectSettings

## 1. プロジェクト概要

| 項目 | 値 |
|---|---|
| 名称 | D-Drive（Designer-Driven Re: IDE Visual Environment）。C# 識別子は `DDrive` |
| コンセプト | プログラマーは **ID だけ**でモックを完成させ、デザイナーが専用エディタで中身を作る |
| Unity | **6000.3.13f1**（勝手に上げない）/ URP 17.3 / Addressables / UniTask / NGO 2.13.2 / R3 1.3.1（5-1 で導入。[docs/01](docs/01_architecture.md) §4 外部依存パッケージ参照） |
| テスト | Unity Test Framework。`Assets/DDrive/Tests/{Editor,Runtime}` |
| 進捗 | Phase 0（基盤）・Phase 1（Audio）・Phase 2（VFX + Model + Anchor アセット化 [docs/21](docs/21_anchor_spec.md) + 配置セット [docs/22](docs/22_anchor_group.md)）実装済み。Phase 3 は 3-1〜3-13 まで実装済み（Phase 3 完了）、次は Phase 4（4-4 Prefab → 4-1 Canvas）。0-14 起動配線（`DDriveRuntimeBootstrap`）+ Addressables 同期は 2026-09-09 に追加。Phase 4 完了（4-1〜4-18 実装済み。4-13 の NGO 複製は Phase 6 の NGO 統合で接続）。Phase 5 は 2026-09-14 に全チケット実装完了（5-1〜5-16 + 自前レビュー対応 5-R、レビュー = [docs/30](docs/30_phase5_review_2026-09-14.md)）。6-0 NGO 統合（Bootstrap で NgoNetBridge 選択可、実機確認 = [docs/29](docs/29_network_device_test.md)）も 2026-09-14 に実装し、**PC-A Host + PC-B Client の実機 2 台で確認済み**（遅延 0ms / 200ms・Late Join・偽造メッセージ破棄・切断、docs/29 §8）。6-10a（Timeline 基盤: CutsceneData/CutsceneManager/`Cutscene.Play`・ネット・入力ロック、[docs/26](docs/26_timeline.md)）は 2026-09-18 に実装完了（EditMode 820/820・PlayMode 706/706 green）。6-10b（D-Drive トラック群 + Camera クリップ + スクラブプレビュー）も同日実装完了（EditMode 820/820・PlayMode 713/713 green。Event/Signal/Shake/Haptic マーカーは Unity 標準 Signal 配送ではなく `CutsceneManager` 独自の時刻カーソル方式、Camera クリップは `DDriveCutsceneCameraApplier`〔実行順 1000〕が LateUpdate で Camera/Volume に書く）。6-10c（Maya FBX → Timeline 自動構築、`CutsceneImportService`/`CutsceneFbxPostprocessor`/`CutsceneImportProfile`）も 2026-09-18 に実装完了。`IImportRuleHandler` は N:1 の対応(1 ショット=カメラ+小物 FBX 1 本+キャラごとの FBX N 本)を表現できないため採用せず、`MayaModelPostprocessor` と同じ専用パイプラインにした(理由は [docs/26](docs/26_timeline.md) 実装メモ)。イベント用ロケーター(アニメ付きカスタムプロパティ→Signal)・実 FBX でのカメラ画角/ピント/絞りの取得可否は実 Maya 素材が無く未検証のまま(§7.3)。6-10d(確認用シーン `CutscenePreviewSceneSetup`〔起動オブジェクト + `CutscenePreviewHarness` 同梱〕・Inspector 導線 `CutsceneDataEditor`・Validator 拡張〔Humanoid/Avatar 不整合・Presentation⇄Cutscene 循環参照・Cosmetic+Simulated 参照・OnSignal 付き Presentation クリップ・fps 検査 6 種・`CameraExecutionOrderValidator`〔実行順の契約検出〕〕・マニュアル更新)も 2026-09-18 に実装完了(EditMode 860/860・PlayMode 717/717 green)。**Timeline(6-10a〜d)完了**。2026-09-19 に Cutscene の Edit Mode プレビュー(Timeline ウィンドウ主導。Edit Mode では `CutsceneManager` を通さない決定、[docs/26 §4.4](docs/26_timeline.md))・Presentation Editor の SceneView Anchor 表示・`TrackKind.AnchorGroup`・Presentation のトラック Anchor とアセット側 Anchor の親子合成(docs/08)を追加。**次は人による確認([docs/43](docs/43_manual_verification_2026-09-17.md) §7〜§10)→ コードレビュー([docs/44](docs/44_review_2026-09-19.md)、Cutscene 分は別途)→ P チケット(移植・更新・互換性)**。Codex 未レビュー分の自前レビューは Phase 4 = [docs/24](docs/24_phase4_review_2026-09-11.md)、Phase 3 後半(3-14〜3-21)= [docs/25](docs/25_phase3_material_anim2d_review_2026-09-11.md)(いずれも 2026-09-11 に対応済み。P4 の整理項目 1〜6 と P4 全体の再レビュー対応は 2026-09-14 に完了、P3 後半の整理項目のみ後続)。**人による確認は [docs/23](docs/23_manual_verification_2026-09-11.md) の手順書を参照**。[docs/11_tasks.md](docs/11_tasks.md)。P5 のユーザー確認は [docs/28 §0](docs/28_manual_verification_phase5.md#0-確認の進め方2026-09-14-まとめ)、要判断は [docs/31](docs/31_phase5_decisions.md) |

> **予告（2026-09-17）**: Timeline（6-10a〜d）の後に **P チケット（移植・更新・互換性、[docs/42](docs/42_distribution.md)）** を行う。`Assets/DDrive/` を UPM パッケージ化して別プロジェクト（MS2026 等）から git URL で参照できるようにし、更新の取り込み手順を用意する。**P チケット完了（P-13 発効）後は、シリアライズ形式・enum・ID/定数名・公開 API・ContentHash・ネットメッセージ・生成コード・Validation の重さの互換性維持が必須になり、[docs/42 §5](docs/42_distribution.md) のスナップショットテストが赤なら変更できなくなる**。発効時に上の §0 TL;DR へ昇格させる（それまでは §0-9 の「シリアライズ形式・asmdef 構成・ProjectSettings は迷ったら聞く」がそのまま適用される）。着手前に [docs/42 §7 A 群](docs/42_distribution.md#a-着手前に決めたい) のユーザー回答が要る。

## 2. ディレクトリ地図

```
Assets/DDrive/                 ← システム本体。層 = asmdef（[docs/01_architecture.md] §4-5）
  Foundation/   Registry / Loader / Pool / Handle / EventBus / Pause / ValueDef / Validation
  Runtime/      種別ごとの Data / Manager / 静的ファサード(Audio, Vfx, Anim ...) / Anchoring(AnchorData・AnchorChain・AnchorGroup・AnchorPoint) / Presentation(PresentationData・PresentationManager・Presentation ファサード・AssetEventDispatcher) / Loop(GameLoopDriver・DDriveRuntimeBootstrap = 唯一の起動配線) / Net
  Editor/       AssetBrowser / 各専用エディタ(Audio, Vfx, Model) / Preview / Codegen / Validation
  Tests/        Editor(EditMode テスト) / Runtime(asmdef が全プラットフォーム対象のため Test Runner では PlayMode テスト。MCP の run_tests は mode=PlayMode で実行する)
Assets/GameData/               ← ツールが管理する Data(.asset)・カタログ・標準プレハブ・確認用シーン
Assets/SourceAssets/           ← 人が管理する実データ(音源・モデル)
docs/                          ← 設計書(00〜20)。DesignerManual/ はデザイナー向け HTML
```

## 3. 作業手順

1. 関連する設計書（`docs/0X_*.md`）と既存コードを **grep してから** 書く。似たクラスの重複が最大の事故要因
2. 実装 → [docs/12_review.md](docs/12_review.md) §3 のチェックリストで自己レビュー
3. **コンパイル・テスト確認**: Unity MCP が繋がっていれば `read_console` でエラー 0、`run_tests` を **EditMode と PlayMode の両方**で green にする（Tests/Runtime は PlayMode でしか走らない）。繋がっていなければ「未検証」と明示する
4. 公開 API / データ構造 / エディタ機能を変えたら、対応する `docs/` を同じ PR で更新する（変更履歴は該当節に日付付きで追記する慣習）
5. コミットは `main` 直接でよい（現状 1 人開発）。ただし 1 コミット = 1 チケット単位を意識する

## 4. Unity MCP

**状態**: ブリッジ `com.coplaydev.unity-mcp` **v10.2.0** を `Packages/manifest.json` に導入済み。クライアント設定はリポジトリ直下の `.mcp.json`（HTTP `http://127.0.0.1:8081/mcp`。8080 は別アプリが使用中のため 2026-09-08 に変更）。
**2026-09-10 から組み込み型 `jp.shiranui-isuzu.unity-mcp` v4.2.0 を併用評価中**（サーバー名 `isuzu-unity`、ポート 27725、Bearer トークン必須。登録は各自の `claude mcp add`、[docs/20](docs/20_mcp_setup.md) §4）。両方が繋がっているときは isuzu 版を優先して使う（`test_run` + `test_results`、`compile_status`、`console_read_logs`、`execute_code`、`menu_execute`）。
セットアップ手順・運用ルールは [docs/20_mcp_setup.md](docs/20_mcp_setup.md)。

- 接続前提: Unity 起動中 + MCP ウィンドウ（`Window > MCP for Unity`）で HTTP サーバ起動 + Connect
- 取得は resource（`mcpforunity://editor/state` 等）、変更は tool（`manage_scene` / `manage_gameobject` / `manage_asset` / `execute_menu_item` / `run_tests` / `read_console`）
- **Play Mode 中は書き込み系を実行しない**。同一 PC で別プロジェクト（MS2026）の Unity も開いている場合は対象インスタンスを明示する
- **接続できていないのに Unity を操作した／確認したと報告しない**

## 5. 参照ドキュメント（抜粋）

| ファイル | 内容 |
|---|---|
| [docs/01_architecture.md](docs/01_architecture.md) | 4 層 / Data・Instance・Handle 分離 / ADR |
| [docs/02_core_framework.md](docs/02_core_framework.md) | AssetId / Registry / Loader / Pool / Event / Validation |
| [docs/03_audio.md](docs/03_audio.md) / [docs/04_vfx.md](docs/04_vfx.md) / [docs/05_model_animation.md](docs/05_model_animation.md) | 種別ごとの詳細設計 |
| [docs/09_editor_tools.md](docs/09_editor_tools.md) | AssetBrowser / プレビュー基盤 / メニュー / ウィンドウ規約 |
| [docs/10_workflow.md](docs/10_workflow.md) | ロール / 命名・配置規約（ツールが維持する） |
| [docs/12_review.md](docs/12_review.md) | PR チェックリスト |
| [docs/19_vfx_usability_review.md](docs/19_vfx_usability_review.md) | VFX 使い勝手レビューと改定内容（2026-09-08） |
| [docs/20_mcp_setup.md](docs/20_mcp_setup.md) | MCP セットアップ・運用ルール |
