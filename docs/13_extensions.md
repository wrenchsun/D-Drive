# 13. 推奨拡張機能

関連: [11_tasks.md](11_tasks.md) Phase 7 / [00_requirements.md](00_requirements.md)

v1 組み込み推奨（A 群）と、v1.x 以降でよい任意拡張（B 群）に分ける。
A 群は「ID 駆動 + デザイナー主体」という本システムの思想を実運用で成立させるために事実上必須。

---

## A 群 — v1 組み込み推奨

### A-1. アセット発注リスト（Placeholder レポート）★思想との相性が最も良い

プログラマーが先行実装する本システムでは、「未登録 ID = デザイナーへの発注書」になる。これを自動収集する。

- 実行時: Registry が Placeholder を返した ID・呼び出し箇所・回数を `MissingAssetLog` に記録
- 静的: コード / Scene / Prefab / Data 内の `*IdRef` を走査し、未登録参照を一覧化
- AssetBrowser に「未実装」タブ: ID 名 / 種別 / 参照元 / 担当（割当可能）/ 状態（未着手・作業中・完了）
- CSV / Markdown エクスポート → タスク管理ツールに貼れる

**効果**: 「何を作ればゲームが完成するか」が常に機械的に見える。発注漏れ・二重発注が消える。

### A-2. ランタイムデバッグオーバーレイ

実機・PlayMode で `F3`（またはコマンド）で表示する常駐 HUD。

- 再生中 Instance 一覧（種別 / ID / 経過時間 / 位置）と個別 Kill ボタン
- Pool 使用状況（Rent 中 / 待機 / 上限到達回数）
- ロード済みアセットとメモリ概算、参照カウント
- 直近の警告（未登録 ID / Handle 無効アクセス）
- 任意 ID をその場で再生するチートパレット（検索付き）→ 実機での音量・見え方確認に使う

### A-3. Live Tuning（実機ホットリロード）

デザイナー調整の往復（ビルド→確認→修正）を殺す機能。

- PC エディタ ⇔ 実機を TCP/WebSocket 接続（開発ビルドのみ）
- Data の変更（Volume / Anchor / VfxParam / Presentation のトラック時間）を実機の Registry に即時パッチ
- 実機側は Instance 再生成 or パラメータ直接反映（種別ごとに定義）
- 実機で調整した値を「エディタへ書き戻す」ボタン
- v1 スコープは **プリミティブ値のみ**（参照差し替えは対象外）で十分効果がある

### A-4. メモリ・パフォーマンス予算（Budget Validator）

- 種別 × カテゴリごとに予算を定義（`BudgetProfile` SO）: テクスチャ合計 MB / VFX 最大パーティクル数 / SE 同時発音数 / Canvas バッチ数
- Validation に組み込み: 予算超過 = Warning、大幅超過 = Error（CI で検出）
- AssetBrowser にシーン別集計ビュー（ScenePreloadList 単位で合算）
- 実行時: デバッグオーバーレイに現在使用量 / 予算を表示

### A-5. サムネイル・プレビュー画像の自動生成

- PreviewService を CI/バッチで駆動し、全 Data のサムネイル（VFX は代表フレーム、Model はターンテーブル 1 枚、Material は球）を自動生成して `PreviewImage` に保存
- 手動でスクリーンショットを撮る運用を廃止。ブラウザの見た目が常に最新
- 差分画像を PR に添付（データ PR のレビューが目視 1 秒で終わる）

### A-6. プラットフォーム / クオリティバリアント

- `AssetVariantSet`: 1 つの ID に対し Quality tier（Low/Mid/High）・プラットフォーム別の Data を割当
- Registry が解決時に現在の tier で自動選択。ゲームコードは無変更（ID は同一）
- 典型用途: モバイル向け軽量 VFX、低解像度テクスチャ、同時発音数の削減
- Validation: High のみ存在し Low 欠落、を警告

## B 群 — v1.x 以降の任意拡張

| # | 機能 | 概要 | 導入判断の目安 |
|---|---|---|---|
| B-1 | ローカライズバリアント | TextureData / SeData(ボイス) / CanvasData にロケール別差し替え。Registry 解決時にロケールで選択（A-6 と同じ機構に相乗り） | 多言語対応が確定したら |
| B-2 | 入力デバイス別 UI アイコン | パッド種別（Xbox/PS/KB）でボタンアイコン Sprite を自動切替。TextureData の InputVariant | パッド対応 UI が増えたら |
| B-3 | 実行時使用統計 | 再生回数・平均同時数をテレメトリ収集 → 「実測未使用」検出、優先ロード順の最適化 | QA ビルド配布開始後 |
| B-4 | Data 変更履歴 diff ビューア | Version 記録（既存）に加え、YAML diff を Inspector 内で表示・ロールバック | 誤編集事故が起きたら |
| B-5 | アセット一覧の HTML 自動出力 | カタログから検索可能な静的 HTML を CI 生成。企画・外注がエディタなしで閲覧 | 外注・非 Unity 職が増えたら |
| B-6 | タグ自動付与支援 | 名前・依存関係からタグ候補を提案（規則ベースで十分） | アセット 1000 件超えたら |
| B-7 | Addressables ビルド差分レポート | コンテンツビルドごとにサイズ増減を PR コメント化 | DL サイズ制約が出たら |
| B-8 | UI マスク対応 UI パーティクル | RectMask2D 対象の Mesh ベーカー方式（[04] §4 の将来枠） | マスク必須の演出が出たら |
| B-9 | カットシーン種別 (Timeline/Camera/Dialogue) | 新 AssetType として追加。基盤は無改修（[00] FR 拡張枠） | カットシーン制作開始時 |

## 実装の載せ方

- A-1, A-2: Foundation の警告経路と Registry に記録ポイントを Phase 0 の時点で仕込む（後付けだと呼び出し箇所の記録が取れない）→ 0-4 / 0-7 の AC に追記済み扱いとし、UI は Phase 7 で実装
- A-3: Manager 経由でしか状態が変わらない設計（本システムの前提）だから安全に成立する。逆に言えば禁止事項（[00] §5）を破るコードがあると Live Tuning が壊れる — Analyzer 検出の重要性が上がる
- A-4〜A-6: Validation / PreviewService / Registry の既存拡張点（IValidator 登録制、解決フック）に載るため基盤改修は不要

タスクは [11_tasks.md](11_tasks.md) Phase 7 に追加。
