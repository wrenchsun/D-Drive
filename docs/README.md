# D-Drive 設計ドキュメント

**D-Drive** — **D**esigner-**D**riven **Re:** **I**DE **V**isual **E**nvironment
（**Re:** は「Unity 標準機能の再定義・再実装」。uGUI Button / Slider を使わない全面新規実装、AnimationEvent・Resources.Load の独自基盤への置換という設計思想を表す）

Unity 6 / URP / Addressables / UniTask+R3 前提。ネットワークは NGO 2.13.2（[MS2026](14_networking.md) §12 へ移植する前提で統一）。AI エージェント向けの入口はルートの [CLAUDE.md](../CLAUDE.md)。
コンセプト: **プログラマーは ID だけでモックを完成させ、デザイナーが専用エディタで中身を作る。**

**AI エージェント向けの実務手順**（新しい AssetType の追加チェックリスト・Unity MCP 検証ループ・ワークツリー運用・コミット慣習・SpecWeb 検証の注意）は Claude Code 用が [`.claude/skills/ddrive-agent-workflow/SKILL.md`](../.claude/skills/ddrive-agent-workflow/SKILL.md)、Codex 等その他のエージェント用の自己完結した要約が [`AGENTS.md`](../AGENTS.md)（リポジトリ直下）にある。2026-09-14 追加、[11_tasks.md](11_tasks.md) 6-9b。

## 読む順番

| # | ドキュメント | 内容 |
|---|---|---|
| 00 | [要件定義](00_requirements.md) | 目的・用語・機能/非機能要件・禁止事項・成功基準 |
| 01 | [アーキテクチャ](01_architecture.md) | 4 層構成・Data/Instance/Handle 分離・ID 解決・Placeholder・ADR |
| 02 | [基盤フレームワーク](02_core_framework.md) | AssetId / AssetDataBase / Registry / Loader / Pool / Event / Validation |
| 03 | [Audio (BGM/SE)](03_audio.md) | データ構造・Manager API・エディタ・運用・Validation |
| 04 | [VFX](04_vfx.md) | Anchor・UI パーティクル・パラメータ公開・プレビュー |
| 05 | [モデル / アニメーション](05_model_animation.md) | Material スロット・StateMachine 連携・フレームイベント・BlendShape・2D スプライトアニメ（既存ツール統合） |
| 06 | [マテリアル / 画像](06_material_texture.md) | 共通チャンネル規約・シェーダー変換・Maya 自動生成・インポート規約 |
| 07 | [Canvas / Prefab](07_canvas_prefab.md) | UI スタック・十字キー配線・ボタン配線・汎用 Prefab |
| 08 | [Presentation](08_presentation.md) | 複数アセット統合演出（Anim+SE+VFX+Shake+HitStop） |
| 09 | [エディタツール](09_editor_tools.md) | AssetBrowser・プレビュー基盤・依存関係・CI |
| 10 | [運用フロー・規約](10_workflow.md) | ロール別責務・開発フロー・命名規約・ブランチ運用 |
| 11 | [タスク分割](11_tasks.md) | Phase 0〜7・チケット（1〜3 人日）・約 36 週 |
| 12 | [レビュープロセス](12_review.md) | PR ルール・チェックリスト・マイルストーン基準 |
| 13 | [推奨拡張機能](13_extensions.md) | 発注リスト・デバッグオーバーレイ・Live Tuning・予算管理・バリアント ほか |
| 14 | [ネットワーク設計](14_networking.md) | INetBridge・NetMode・Presentation 同期再生・Late Join・ContentHash 照合 |
| 15 | [UI インタラクション](15_ui_interaction.md) | UiButton 全面新規実装・イージング/スプライン Tween・出現/常時/消滅演出 |
| 16 | [カメラシェイク / 振動](16_camera_haptics.md) | Trauma 合成シェイク・2 モーターカーブ振動・実揺れ/実パッドプレビュー |
| 17 | [値定義の統一規約](17_value_definition.md) | 定数 / パラメトリック曲線 / 任意カーブ + スピード（ValueDef）・共通 Drawer |
| 18 | [UI コントロール](18_ui_controls.md) | UiInteractable 共通基底・UiSlider 全面新規実装・応答曲線・標準オプション直結 |
| 19 | [VFX 使い勝手レビュー](19_vfx_usability_review.md) | 2026-09-08 のレビュー記録: 問題点 → 判断 → 改修内容 → 未着手 |
| 21 | [Anchor 仕様](21_anchor_spec.md) | Anchor のアセット化（AnchorData / AnchorId）・入れ子連鎖・VFX/SE 共通・生成ディレイ/ランダム/確率・既存ボーン流用・AnchorEditor（2026-09-08 実装済み） |
| 24 | [Phase 4 コードレビュー結果（2026-09-11）](24_phase4_review_2026-09-11.md) | Codex 未レビュー分（4-8〜4-17）の自前レビュー: P1 6 件 / P2 8 件 / 整理 6 件と対応状況 |
| 23 | [実装確認手順書（2026-09-11）](23_manual_verification_2026-09-11.md) | 3-5〜4-18 の自律実装分を人が確認するための手順（見た目・音・操作感は未確認） |
| 22 | [配置セット（AnchorGroup）](22_anchor_group.md) | 複数の位置にまとめて出す: 原点 + Grid/Circle/Line/Random/手置き + 全点共通/点ごとのアセット + 入れ子。`Anchors.Play(groupId, ctx)`（2026-09-08 実装済み） |
| 20 | [MCP セットアップ](20_mcp_setup.md) | AI ⇄ Unity Editor 連携（MCP for Unity）の導入手順・運用ルール・バージョン管理 |
| 26 | [Timeline 連携](26_timeline.md) | Timeline の基礎・CutsceneData と Presentation の使い分け・D-Drive トラック（SE/VFX/イベント/Camera）・カメラのシームレス化とステップ fps・URP Volume へのピント書き込み・ネット同期・Maya FBX 自動取り込み（Humanoid、FBX セット）と Maya 作業者の手順・命名規則・ゲームカメラ実行順の契約（`DefaultExecutionOrder(1000)`、違反検出）・入力ロックの分担・決定事項（2026-09-18 に全件確定、未決なし） |
| 27 | [仕様書スプレッドシート連携（ドラフト）](27_spec_sheet.md) | 人向け + ツール向けタブのテンプレート・一方向同期・差分プレビュー・仕様書リンク（2026-09-13）。**32 へ置き換え予定（旧方式）** |
| 32 | [仕様書 Web 化（Google Apps Script、ドラフト）](32_spec_web.md) | 27 を置き換える新方式。GAS Web アプリに人向け機能仕様 + ツール向けアセット一覧・調整値を統合、正本は Web 側、D-Drive は `Specs/*.json` へスナップショット同期（2026-09-14） |
| 28/31 | [Phase 5 実装確認手順書](28_manual_verification_phase5.md#0-確認の進め方2026-09-14-まとめ) / [Phase 5 要判断一覧](31_phase5_decisions.md) | P5 のユーザー確認の進め方チェックリスト（§0）と、決めていない事項の一覧（A/B/C 優先度付き）（2026-09-14） |
| 33 | [CI セットアップ](33_ci_setup.md) | 6-1: セルフホストランナー登録・ブランチ保護・public/private 別の注意・ローカル実行スクリプトの使い方（2026-09-15） |
| 25 | [Phase 3 後半コードレビュー結果（2026-09-11）](25_phase3_material_anim2d_review_2026-09-11.md) | Codex 未レビュー分（3-14〜3-21、Material/Anim2D）の自前レビュー: 実バグ 8 件 + P2 大半の対応状況 |
| 29 | [実機ネットワーク確認手順](29_network_device_test.md) | 6-0: PC-A Host + PC-B Client の実機 2 台確認手順・実施記録（遅延 0ms/200ms・Late Join・偽造メッセージ破棄・切断） |
| 30 | [Phase 5 コードレビュー結果（2026-09-14）](30_phase5_review_2026-09-14.md) | 5-1〜5-16 の自前レビュー結果と対応状況 |
| 34 | [オンボーディング（プログラマー・新メンバー向け）](34_onboarding.md) | 6-4: 環境構築・リポジトリの地図・「ID だけでモックを作る」流れ・検証ループ・CI・AI エージェント・発注ツールとの関わり方（2026-09-15） |
| 35 | [チュートリアル動画の台本](35_tutorial_video_scripts.md) | 6-4: 5 本分の収録台本（目的・事前準備・カットごとの操作/ナレーション）+ 収録時の注意（個人情報・URL 非表示）。収録自体は人の作業（2026-09-15） |
| 36 | [デザイナーマニュアル スクリーンショット撮影リスト](36_manual_screenshot_list.md) | 6-4: `docs/DesignerManual/*.html` 全 26 ページの撮影リスト（優先度 A/B/C・ファイル名案・挿入位置・撮り方・撮影順グループ）。既存 6 枚のうち撮り直しが必要な 2 枚も明記。撮影自体は人（またはエージェントの画面キャプチャ）の作業（2026-09-15） |
| 37 | [Phase 6 実装確認手順書](37_manual_verification_phase6.md) | 6-0〜6-9b のユーザー確認の進め方チェックリスト（§0）。実装中・未着手のチケットは見出しと確認観点の仮置きのみ（2026-09-15） |
| 38 | [受け入れデモ手順書](38_acceptance_demo.md) | 6-8: 要件 §7 成功基準 5 項目それぞれの手順・期待される結果・証跡（既存/未取得）、事前確認チェックリスト、リードへの確認事項。実施とリード承認は人の作業（2026-09-15） |
| 39 | [使い勝手の修正リスト（2026-09-17）](39_usability_fixes_2026-09-17.md) | スクリーンショット撮影と実機での通し確認で見つかった不具合・要望 26 件（U-1〜U-26）。Phase 7 より先に片付ける（2026-09-17） |
| 40 | [マニュアル スクリーンショットの撮影・取り込み手順](40_manual_screenshot_workflow.md) | 撮る人のチェックリスト（構図・伏せるもの・命名）と、取り込む側の手順 1〜9（突き合わせ → 伏せ字 → 最適化 → HTML 挿入 → docs 更新 → Web 版再生成）+ 落とし穴（2026-09-17） |
| 41 | [Phase 6 + W/O チケット 自前レビュー結果（2026-09-17）](41_phase6_review_2026-09-17.md) | docs/30 以降の未レビュー分 `b533bf6..HEAD`（139 コミット）を runtime / editor / tests+CI / SpecWeb の 4 系統でレビュー。P1 8 件（Pool の ABA・調整値の全消し・GAS の認可の抜け 3 件・CI の握りつぶし ほか）（2026-09-17） |
| 42 | [配布・移植・更新・互換性ポリシー（P チケット、設計）](42_distribution.md) | Timeline の後に行う P-1〜P-13 の設計: システム/データの線引き表と境界違反 10 件、配布方式の比較（**UPM git URL + 埋め込み開発を推奨**）、SemVer・スキーマ版・マイグレーション・ロールバックの更新フロー、**P 完了後に発効する互換性ポリシー**（9 互換面 + スナップショットテスト 11 種）、要判断 A/B/C（2026-09-17） |
| 50 | [持ち込み先向け持ち込み先ガイド（導入・更新・運用、HTML）](50_consumer_guide.md) | 持ち込み先（MS2026 等）のプログラマー向けに、導入手順・更新方法・運用方法を個別 HTML ページにまとめたガイド（`docs/50_consumer_guide/`、DesignerManual/ProgrammerManual と同じ書式）。正本はこのリポジトリで、リリースのたびに `Packages/com.ddrive.core/Documentation~/ConsumerGuide/` へ同期される（2026-09-20） |
| 51 | [T-Drive 連携（FacialController + Toon マテリアル、FC チケットの設計）](51_tdrive_integration.md) | T-Drive の Facial（`com.tdrive.facial`）と Toon（`com.tdrive.toon`）を D-Drive で使うための FC-0〜FC-20: 方針（旧 7-8 は T-Drive 版を使う）・doc16/17・f27702e の実コードでの裏取りと相違・チケット別設計（同じモデルへのバインド / スポーン・返却の通知 / MaterialData のパス無効化 / 現在の視点 API 他）・不採用の D 群・T-Drive へ返す事項。**FC-1〜FC-7・FC-10〜FC-12・FC-14・FC-15・FC-19・FC-20 は実装済み（2026-10-03）、レビュー [53](53_review_fc_2026-10-03.md) の修正ラウンド 1 も対応済み。ほかは保留・未着手** |
| 52 | [FC チケット（T-Drive 連携）の人による確認手順](52_manual_verification_fc.md) | FC-1〜FC-20 を人が確認する手順書。チケットごとの節（手順 → 期待する結果 → 結果欄）と「T-Drive 導入後に確認」。実装済みのチケットの節は埋め済み（修正ラウンド 1 の確認項目は FC-4 の 4-2・FC-11 の 11.3・FC-15 の 15.6）、保留のチケットは枠のみ |
| 53 | [FC チケット（T-Drive 連携）自前レビュー結果（2026-10-03）](53_review_fc_2026-10-03.md) | `9f40cbb..59111a7`（PR #86〜#97、FC-1〜FC-20 の実装）を読むだけでレビュー。P1 0 件・P2 6 件（右クリック作成で知らないシェーダーが Lit に戻る / 欠けたシェーダーを保つ / 0 秒のマーカーが発火しない / markerTrack の収集 / ApplyBindings の再入 / タグ無しパスの無効化）・P3 18 件、公開面の整理案と v1.4.0 前の順序（2026-10-03） |
| 54 | [P-15（更新ウィンドウの追加パッケージ対応）/ U-28（Canvas の埋め込み）自前レビュー結果（2026-10-03）](54_review_p15_canvas_2026-10-03.md) | PR #98（`cd312f3`）・#100（`cb5ba07`）を読むだけでレビュー。P1 0 件・P2 7 件（取得が作業ツリーをチェックアウトする / プレリリースのタグを丸めて存在しないタグを書く / 版上げ前の同期 clone で最大 30 秒固まる / 重なった埋め込みで配線が二重に発火 / 選択追従で入力途中の値が別の CanvasData に書かれうる / 埋め込み時の SignalArgs.ElementPath の意味 / ddriveUpdate の拡張規則が未定）・P3 14 件、`ddriveUpdate` と `EmbeddedCanvas` の公開面の所見と v1.4.0 前の順序（2026-10-03） |
| 55 | [修正ラウンド 1・2（docs/53・54 の指摘への対応）の自前レビュー結果（2026-10-03）](55_review_fix_rounds_2026-10-03.md) | PR #102（`20c75bf`）・#103（`64c0301`）を読むだけでレビュー。元の P2 13 件は解消 10・一部 3（FC-R-02 / 03 / 09）。新規 P1 0 件・P2 2 件（ネット受信側で 0 秒のマーカーが鳴らない / シェーダーが欠けている間の再生成で既存 MaterialData の参照が Lit に置き換わる）・P3 12 件、v1.3.1 → 現在の挙動の変更と CHANGELOG の照合、v1.4.0 前の順序（2026-10-03） |
| 56 | [修正ラウンド 3（docs/55 の指摘への対応）の自前レビュー結果（2026-10-04）](56_review_fix_round3_2026-10-04.md) | PR #105（`84269f4`）を読むだけでレビュー。FX-R 14 件は解消 12・一部 2（FX-R-01 / 02）。新規 P1 0 件・P2 1 件（欠けたシェーダーの間の再生成で既存 MaterialData の Common が上書きされうる）・P3 6 件（Client 送信の 2 区間と全か無かの規則 / `PresentationManager.Tick` の同形の走査 ほか）、FX-R-01 の立場別の発火回数の表、挙動の変更と CHANGELOG の照合、v1.4.0 のタグの所見と残る実機確認（2026-10-04） |
| 57 | [修正ラウンド 4（docs/56 の指摘への対応）と M-4（禁止 API の許可）の自前レビュー結果（2026-10-05）](57_review_round4_m4_2026-10-05.md) | PR #107（`0a41d03`）・#108（`d0b6971`）を読むだけでレビュー。FY-R 7 件はすべて解消（`PresentationManager.Tick` の回帰なし）。新規 P1 0 件・P2 5 件（MS2026 への判断基準「`ITimeSource` に直す」が実行できない / 設定の許可リストのパスが文字列の前方一致 / Editor で当たりの一覧を見られない / 設定画面が編集できない可能性〔推定〕/ D-Drive 自身の 12 件で `run-ci.cmd` が赤）・P3 7 件、FY-R-02 の立場別・時刻別の発火の表、許可コメントの書式の所見（書式は変えず契約の文面を追記）、D-Drive 自身の 12 件の扱い、v1.4.0 のタグの所見と残る確認（2026-10-05） |
| 58 | [修正ラウンド 5（docs/57 の指摘への対応）と P-15 確認の不具合対応（BUG-1 / Q-1〜Q-4）の自前レビュー結果（2026-10-06）](58_review_round5_p15fix_2026-10-06.md) | PR #110（`6d42329`）・#111（`9020727`）を読むだけでレビュー（v1.4.0 タグ前の最後のレビュー）。FZ-R は記録どおり解消（D-Drive 自身の許可コメント 11 件はコメントだけの変更で理由も妥当）、BUG-1 / Q-2〜Q-4 は解消、Q-1 は文面が承認と違う。新規 P1 0 件・P2 1 件（MS2026 への時間の案内に登録解除と `Instance` が null のときが無い）・P3 11 件、案内 6 か所の一致表、リリース手順で止まりそうな点、v1.4.0 のタグの所見と残る人の確認（2026-10-06） |
| 59 | [修正ラウンド 6（docs/58 の指摘への対応）と固定値(Constant)の ValueDef の検査の修正の自前レビュー結果（2026-10-06）](59_review_round6_valuedef_2026-10-06.md) | PR #113（`d4a2588`）・#115（`6bf56f7`）を読むだけでレビュー（v1.4.0 タグ前の最終確認）。GA-R-01〜12 は記録どおり解消（運用ページのコード例はそのままコンパイルが通る、全体 Validator の件数・重さ・コードは不変）。新規 P1 0 件・P2 2 件（CameraShake の Envelope と Haptics は Constant でも Time を寿命として読むので尺 0 が黙って通る / 新規作成・取り込み直後の BgmData が 0 / 0 で必ず Error）・P3 6 件、ValueDef の欄ごとに Time を誰が読むかの表、レビュー済み / 未レビューの PR の対応表（未レビューのコードの PR なし）、タグ前に残る作業の順序（2026-10-06） |
| 60 | [v1.4.0 リリース準備（チェック表・当日の手順・リリースノート下書き・MS2026 / T-Drive 宛ての文面）](60_release_1_4_0_prep.md) | v1.4.0 のタグ前の状態（レビュー・全段の確認・人による確認・SpecWeb の事実のチェック表）、リリース当日の順序つきの手順（`run-ci.cmd` 全段 → `check-release.ps1` → `bump-version.ps1` → タグ → push → SpecWeb → 持ち込み先）と止まりやすい点、GitHub のリリースノートの下書き、MS2026 の担当へ渡す文面（Validation 53 件への返答）、T-Drive 側へ渡す文面、リリース後のチケット候補・レビューで見送った項目の一覧（2026-10-06。版上げ・タグ・push・SpecWeb のデプロイは未実施） |

## デザイナー向けマニュアル

Unity を操作しながら使う人向けの HTML マニュアルは [DesignerManual/Readme.html](DesignerManual/Readme.html)。プログラム知識は不要。新規メンバーの最初の一歩は [DesignerManual/getting-started.html](DesignerManual/getting-started.html)（所要時間つきの「はじめての15分」）。

## AI エージェント向けの入口（再掲）

Claude Code 用のスキルは [.claude/skills/ddrive-agent-workflow/SKILL.md](../.claude/skills/ddrive-agent-workflow/SKILL.md)、Codex 等その他のエージェント用の自己完結した要約は [AGENTS.md](../AGENTS.md)（リポジトリ直下）。プログラマー・新メンバー向けの入口は [34_onboarding.md](34_onboarding.md) §7 にまとめてある。

## 全体像 1 枚図

```
Game ── Presentation.Play(id, ctx) ──┐
  │                                  ▼
  │ ID のみ           ┌── Presentation Layer ──┐
  ▼                   │  トラックを各Managerへ委譲 │
Manager Layer ◀───────┴────────────────────────┘
  Audio/Vfx/Anim/Mats/Ui/Models/Prefabs
  Play(id) → Registry解決 → Pool生成 → Instance → Handle返却
  │
Foundation: Registry / Loader(Addressables) / Pool / EventBus / Pause / Validation
  │
Editor: AssetBrowser + 専用エディタ + プレビュー(実Manager駆動) + CI Validation
```

## 元案からの主な設計強化点

1. **Data / Instance / Handle の分離** — 定義と実体の混在を解消。世代式 Handle で安全に操作
2. **Placeholder 戦略** — 未登録 ID でも動く = アセット 0 でモック完成という理想の実現手段
3. **Presentation 層** — Anim+SE+VFX+Shake+HitStop を 1 データに統合、コード 1 行で再生
4. **共通イベント / 共通フラグ / 共通基底** — 全 9 種別で二重実装しない
5. **UI パーティクルを VFX の RenderMode として内包** — 外部ツール不要
6. **プレビュー = 実 Manager 駆動** — 「エディタと実機で違う」問題を構造的に排除
7. **Validation + 依存グラフ + CI** — 参照切れ・未使用・ID 重複を機械検出
8. **コード PR とデータ PR の完全分離** — レビューが速く、コンフリクトしない運用
9. **未登録 ID = 発注書（Placeholder レポート）** — 作るべきアセットが機械的に一覧化される
10. **Live Tuning + デバッグオーバーレイ + 予算管理** — 実機調整の往復とパフォーマンス劣化を運用で防ぐ
11. **マルチプレイ標準対応（v1 必須）** — ID がそのまま同期メッセージになる。NetMode（Local/Cosmetic/Simulated）で配送を自動選択し、Presentation の同期再生・Late Join 復元・ContentHash 照合まで v1 に含む。マイルストーンデモは常に 2 クライアント + サーバー構成で実施
12. **UiButton 全面新規実装 + イージング/スプライン Tween** — Click/LongPress/Repeat 等の全イベントを R3/UniTask で提供。31 種イージング + 4 種スプラインで、任意 UI 要素の出現/常時/消滅演出をデザイナーが割当。FadeIn/SlideIn/PopIn/Shake 等 **50 種以上のプリセット**をギャラリーから選ぶだけで適用でき、コード側にも同名 1 行関数を完備
13. **2D スプライトアニメは既存ツールを統合** — スプライト分割→Clip→BlendTree→ID 登録までワンストップ。3D と別系統（AnimId / Anim2DId）で設計
14. **画面揺れ・コントローラー振動もデータ駆動** — Trauma 合成シェイクと 2 モーターカーブ振動を ID 管理し、エディタでカメラ実揺れ・実パッド振動をプレビューしながら調整。オプションの揺れ/振動スケールに全再生が追従
15. **UiSlider も全面新規実装** — UiButton と共通の UiInteractable 基底上に構築。応答曲線（音量は対数等）・表示の追従演出・ノッチ SE/触覚をデータで定義し、音量や画面揺れなどの標準オプションにはコード 0 行で接続できる
16. **調整値の定義形式を統一（ValueDef）** — デザイナーが触る値はすべて「定数 / パラメトリック曲線 / 任意カーブ」+「スピード」の 1 形式に統一。編集 UI も共通 Drawer 1 本に集約し、種別が増えても学習コストが増えない
