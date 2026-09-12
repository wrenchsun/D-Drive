# D-Drive 設計ドキュメント

**D-Drive** — **D**esigner-**D**riven **Re:** **I**DE **V**isual **E**nvironment
（**Re:** は「Unity 標準機能の再定義・再実装」。uGUI Button / Slider を使わない全面新規実装、AnimationEvent・Resources.Load の独自基盤への置換という設計思想を表す）

Unity 6 / URP / Addressables / UniTask+R3 前提。ネットワークは NGO 2.13.2（[MS2026](14_networking.md) §12 へ移植する前提で統一）。AI エージェント向けの入口はルートの [CLAUDE.md](../CLAUDE.md)。
コンセプト: **プログラマーは ID だけでモックを完成させ、デザイナーが専用エディタで中身を作る。**

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
| 26 | [Timeline 連携（ドラフト）](26_timeline.md) | Timeline の基礎・CutsceneData・D-Drive トラック（SE/VFX/イベント）・Maya FBX 自動取り込みと命名規則・未決事項（2026-09-13） |
| 27 | [仕様書スプレッドシート連携（ドラフト）](27_spec_sheet.md) | 人向け + ツール向けタブのテンプレート・一方向同期・差分プレビュー・仕様書リンク（2026-09-13） |

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
