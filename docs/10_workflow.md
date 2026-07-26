# 10. 運用フロー・規約

関連: [09_editor_tools.md](09_editor_tools.md) / [12_review.md](12_review.md)

---

## 1. ロール別の責務と権限

| ロール | やること | 触ってよいもの | 禁止 |
|---|---|---|---|
| プログラマー | モック実装、ID 参照、Signal 購読、Manager/基盤の保守 | ゲームコード、`*IdRef` フィールド | Data の中身調整、Editor 経由以外の Data 変更 |
| デザイナー | Data 登録・調整、イベント配線、プレビュー確認 | AssetBrowser・各専用エディタ | コード編集、Script 追加、Manager 設定変更、Generated/ 配下 |
| テクニカルアーティスト | インポート規約 (Maya/Texture Profile)、変換テーブル、シェーダー | 上記 + Profile 系 SO | 基盤コード |
| リード | ID 体系・カテゴリ・タグ辞書の管理、Validation ルール承認 | 全部 | — |

## 2. 開発フロー（機能 1 つの流れ）

```
① プログラマー: モック実装
   - Presentation.Play(PRESENTID.SkillSlash, ctx) を書く
   - ID が未登録でも Placeholder で動作 ★ここでゲームロジックは完成
   - PR#A: ゲームコードのみ。アセット不要でレビュー可能
        │
② デザイナー: ID の中身を作る
   - AssetBrowser で SkillSlash(Presentation) と構成アセットを登録
   - 専用エディタで調整、統合プレビューで確認
   - Validation オールグリーンを確認
   - PR#B: GameData/ 配下の .asset のみ。コード diff なし
        │
③ 結合確認: 実機ビルドで確認 → 微調整は②の繰り返し（コード PR 不要）
```

ポイント: **コードとデータの PR が完全に分離**されるため、レビューが速く、コンフリクトしない。

## 3. 命名・ID 規約

| 対象 | 規約 | 例 |
|---|---|---|
| Data アセット名 | `<種別>_<カテゴリ>_<名前>` | `SE_Player_Slash`, `VFX_Skill_FireBall` |
| ID 定数 | PascalCase（アセット名から自動生成） | `SEID.PlayerSlash` |
| タグ | リードが管理する **タグ辞書**（TagCatalog SO）から選択制。自由入力禁止 | Enemy, Boss, UI, Fire |
| カテゴリ | 種別ごとに 2 階層まで | Audio/SE/Player |
| Signal キー | `<機能>/<動作>` 小文字 | `shop/buy`, `skill/hit` |
| システム名表記 | 表示名は `D-Drive`、C# 識別子（namespace/asmdef/フォルダ）は `DDrive`。メニューパスは `DDriveMenu` 定数経由のみ | `[MenuItem(DDriveMenu.Root + "...")]` |
| スクリプト・関連アセット配置 | 基本的に `Assets/DDrive/` 以下に層・種別ごとのディレクトリを切って配置（詳細は [01_architecture.md](01_architecture.md) §5） | `Assets/DDrive/Runtime/Audio/AudioManager.cs` |

- ID の ulong 値は初回生成後、**リネームしても不変**（アセット名変更で参照は壊れない）
- ID の削除は「Archived タグ → 1 リリース後に削除」の 2 段階（急な削除で他人の作業を壊さない）

## 4. ブランチ・コンフリクト運用

- 1 アセット 1 ファイル + カタログは追記型（ID 昇順自動ソート）でコンフリクト最小化
- カタログ・`AssetIds.g.cs` がコンフリクトしたら**手でマージしない**。両ブランチ取り込み後に「再生成」ボタンで再構築
- GameData/ 配下は基本 force-text（YAML）でレビュー可能に

## 5. シーン・ロード運用

- シーンごとに `ScenePreloadList`（使用 ID 一覧の SO）を持ち、ロード画面で `Preload` 実行
- Preload リストは依存グラフから「このシーンで参照される ID」を自動集計するボタンで生成
- Persistent フラグの BGM/UI はタイトルで常駐ロード

## 6. デザイナー向けクイックリファレンス（例: SE を追加する）

1. AssetBrowser を開く（メニュー: `D-Drive > Asset Browser`）
2. Audio/SE カテゴリで「新規」→ 名前を規約通り入力（`SE_Player_Footstep`）
3. AudioClip を D&D、Volume/Pitch/3D 設定 → プレビューで試聴
4. 必要ならイベント・Duck を設定
5. 保存 → Validation バナーが緑であること
6. プログラマーに ID 名（`SEID.PlayerFootstep`）を連絡（または先にプログラマーが仮 ID で実装済みなら、その ID 名で登録するだけ）

## 7. トラブルシューティング規約

| 症状 | 原因と対応 |
|---|---|
| マゼンタの球が出る | VFX ID 未登録。Console の警告ログに ID 名が出るので登録する |
| 無音 + ログ | SE ID 未登録。同上 |
| プレビューと実機で見た目が違う | 環境設定（ポスプロ/ライト）を確認。Manager 経路は同一のはずなので、差異があれば基盤バグとして報告 |
| ID 定数が見つからない | 「ID 定数を再生成」を実行 |
