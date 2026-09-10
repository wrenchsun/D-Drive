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

**基本方針: 命名規則は人が守るものではなく、ツールが生成・維持するもの**（[00] FR-1.5/1.6。プラチナゲームズ・コジマプロダクションの CEDEC 講演で紹介されたアセットパイプラインの考え方を採用）。

- 人が入力するのは**意味情報のみ**: 表示名（日本語可。「剣の斬撃音」等）・カテゴリ（選択制）・タグ（選択制）・識別子（英語 PascalCase。ID 定数名になる。例: `PlayerSlash`）
- ファイル名・ulong ID・カタログ登録・Addressables アドレスは、AssetBrowser の新規作成ダイアログ / D&D 登録がすべて自動生成する
- アイコン画像（`AssetDataBase.Icon`）は Inspector の「シーンから作成」/「フォルダから選択」がツール管理で `GameData/Icons/<種別>/<アセット名>_Icon.png` に置く（[09] §8.1、2026-09-10）。人がファイル名を付けない
- **ファイル名は人が意識しない**。参照は常に ID、検索・閲覧は AssetBrowser の表示名・タグで行う
- 表示名・識別子・カテゴリの変更はツール経由。ファイル名はツールが規約名に追従リネームする（ID 不変のため参照は壊れない）。Project ウィンドウで直接リネームされたファイルは、Validation の「名前正規化」FixAction で規約名に戻せる

| 対象 | 規約（ツールが生成） | 例 |
|---|---|---|
| Data ファイル名 | `<種別>_<カテゴリ>_<識別子>`。ツールが識別子とカテゴリから自動生成・追従リネーム。種別接頭辞は `AssetNamingService.GetTypePrefix`（SE / BGM / VFX / MODEL / ANC(Anchor) / ANCG(AnchorGroup)(2026-09-08 追加) …） | `SE_Player_Slash`, `VFX_Skill_FireBall`, `ANC_Player_RightHand` |
| ID 定数 | 識別子そのまま（PascalCase） | `SEID.PlayerSlash` |
| タグ | リードが管理する **タグ辞書**（TagCatalog SO）から選択制。自由入力禁止 | Enemy, Boss, UI, Fire |
| カテゴリ | 種別ごとに 2 階層まで。選択制（自由入力はリード承認で追加） | Audio/SE/Player |
| Signal キー | `<機能>/<動作>` 小文字 | `shop/buy`, `skill/hit` |
| システム名表記 | 表示名は `D-Drive`、C# 識別子（namespace/asmdef/フォルダ）は `DDrive`。メニューパスは `DDriveMenu` 定数経由のみ | `[MenuItem(DDriveMenu.Root + "...")]` |
| スクリプト・関連アセット配置 | 基本的に `Assets/DDrive/` 以下に層・種別ごとのディレクトリを切って配置（詳細は [01_architecture.md](01_architecture.md) §5） | `Assets/DDrive/Runtime/Audio/AudioManager.cs` |
| アセット新規作成メニュー | 種別ごとの `Data` クラスには必ず `[CreateAssetMenu(menuName = "D-Drive/<種別カテゴリ>/...")]` を付与する。ただしこれは開発者向けのフォールバックで、**正規の作成経路は AssetBrowser の新規作成ダイアログ**（意味情報入力→自動命名） | `[CreateAssetMenu(menuName = "D-Drive/Audio/SE Data")]` |

- ID の ulong 値は初回生成後、**リネームしても不変**（アセット名変更で参照は壊れない）
- ID の削除は「Archived タグ → 1 リリース後に削除」の 2 段階（急な削除で他人の作業を壊さない）
- DCC 連携（Phase 3 以降）: Maya 等からの取り込みも「Export ボタン一つ」を目標とし、FBX 名・テクスチャ名から規約名・ID・Data 生成までツールが行う（[06] A-2 の Maya 自動生成・ImportProfile がこの入口）

## 3.3 フォルダ配置規約（配置もツールが維持する）

命名と同じ原則をフォルダにも適用する。**フォルダは人間が Project ウィンドウで眺めるためのビューであり、参照の真実は常に ID/Address**（コード・データがパスに依存してはならない。だからこそフォルダ整理はいつでも安全にできる）。

| ルート | 役割 | 所有者 |
|---|---|---|
| `Assets/GameData/<種別>/<カテゴリ階層>/` | 管理データ（各 `*Data` SO）とベイク生成物（トリム済み wav 等は Data の隣） | **ツール**（新規作成時に自動配置、整理メニューで追従） |
| `Assets/GameData/Catalogs/` | カタログ SO 群 | ツール |
| `Assets/GameData/Prefabs/<ドメイン>/` | D-Drive 標準プレハブ（`SeEmitter.prefab` 等）。シーンで使う際は AddComponent で組まず**既存の標準プレハブを使うこと推奨**。Phase 4 の Prefab 管理（4-4）もこのルートを基点に拡張する | ツール生成 + 人が既定値調整 |
| `Assets/SourceAssets/<ドメイン>/<カテゴリ>/` | 実データ（インポートした音源・モデル・テクスチャ等）。管理データと実データは場所が分かれるが、参照は GUID なので運用上は AssetBrowser 経由で意識しない | **人**（カテゴリ準拠は推奨であり強制しない） |

- カテゴリ `Player/Attack` の SeData → `Assets/GameData/Audio/SE/Player/Attack/SE_Attack_Slash.asset` のように、**カテゴリがそのままフォルダ階層になる**（AssetBrowser を使わなくても Project ウィンドウである程度探せる）
- カテゴリを後から変更した場合、`Tools/D-Drive/Generate/GameData をカテゴリ配置に整理` がフォルダ移動・規約名への追従リネーム・カタログ Address 更新までまとめて行う（GUID 不変 → ID 参照は壊れない）
- フォルダのセグメントはファイル名と同じ正規化（英数字のみ）。日本語のみのカテゴリはフォルダ化されず種別直下に置かれる

## 3.4 デザイナー向けマニュアル（Readme.html）

- 場所: `docs/DesignerManual/Readme.html`（ルート）。関連ページは同フォルダに分割し、ルート ⇄ 各ページ ⇄ 関連ページ間を相互リンクする
- 対象: **デザイナーが実際に触る機能のみ**。Phase 0（基盤・ID/Registry 等の内部実装）はデザイナーに関係しないため対象外
- 更新タイミング: **各 Phase のコードレビュー完了後**、その Phase で追加されたデザイナー向け機能を追記する
- 表現方針: パラメータの詳細を丁寧に、専門用語はかみ砕く（用語集ページを設ける）、ウィンドウの開き方から記述する
- スクリーンショット: `docs/DesignerManual/images/` に配置。**画像の加工は矩形切り取りのみ**を前提とし、矢印・注釈の書き込みが必要な説明は文章側で行う

## 3.5 Inspector 規約

- 公開フィールドにはなるべく `[Tooltip("...")]` を付ける。デザイナーがフィールド名だけで意味を推測しなくて済むようにする（[00_requirements.md] にはまだ明記していないが、AssetDataBase 共通フィールド・AssetFlags・AnchorDef・各種 `*Data` で徹底する）
- 新しい `Data` クラスを追加したら、その場で `[CreateAssetMenu]` と主要フィールドの `[Tooltip]` を追加してからコミットする（後回しにしない）

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
