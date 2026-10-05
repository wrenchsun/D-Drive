# よくある警告と対処

`Validation > Run All`（`-executeMethod DDrive.Editor.CI.ValidateAll`）で出やすい警告・エラーと、対処の目安。詳しい仕組みは開発リポジトリの `docs/02_core_framework.md` §4・`docs/42_distribution.md` §5.8 を参照。

## Error（CI を fail させる。放置しない）

| 症状 | 典型的な原因 | 対処 |
|---|---|---|
| `カタログ未登録: '...'(ID 0x...)がどのカタログにもありません` | Data を Asset Browser 以外の方法で作成した、またはカタログの登録処理を挟まずにファイルをコピーした | Asset Browser 経由で作り直すか、`Tools > D-Drive > Generate > Regenerate Asset IDs` を実行してカタログ登録を揃える |
| `Addressables 未登録: '...' がグループに入っていません` | Addressables への同期を実行していない | `Tools > D-Drive > Update > 更新ウィンドウ` の「Addressables 同期」、またはセットアップウィザードの該当ステップを実行する |
| `Flags.Load が Preload ではありません` | 同期解決 API（Play/Spawn を同期で呼ぶ種別。Audio・Vfx・Anim・Presentation など）の Data で `Load` が `Preload` 以外になっている | 対象 Data の Inspector で `Flags.Load` を `Preload` に変更する |
| 禁止 API の検出（`ForbiddenApiScanner`） | ゲームコードで `Instantiate`/`Resources.Load`/`AudioSource.Play`/`Time.time` 等を直接呼んでいる | 下の「禁止 API の指摘への対処」の順に対処する |

## Warning（更新のたびに増えることがある。次の更新までに直す）

| 症状 | 対処 |
|---|---|
| `DD-SETUP-*`（依存パッケージ・ProjectSettings・置き場所等の不足） | `Tools > D-Drive > Setup > セットアップウィザード` を開き、該当ステップの「直す」を実行する |
| `DD-SETUP-UPDATE-PENDING`（更新がまだ適用されていない） | `Tools > D-Drive > Update > 更新ウィンドウ` の「更新を適用」を実行する |
| `DD-SCHEMA-OUTDATED`（Data の形式が古い） | 更新ウィンドウの「更新を適用」がマイグレーションも実行するので、通常はこの手順で解消する |
| `DD-SETUP-EMBEDDED-MODIFIED`（パッケージが埋め込み〔改造可能な状態〕になっている） | 通常の git URL 参照に戻す。どうしても改造が必要な場合は開発リポジトリへの提案を検討する（`Documentation~/AGENTS_CONSUMER.md` §1-4） |

## 例外にならず警告ログだけ出るケース

未登録 ID を Play/Spawn すると、Console に `[DDrive] Unregistered AssetId 0x{id:X} resolved to Placeholder.` という警告が 1 ID につき 1 回出るだけで処理は継続します。デザイナー側の作業がまだ完了していないだけの可能性が高く、慌てて実装を変える必要はありません。

## 禁止 API の指摘への対処（順番に）

1. **D-Drive の API に直す**: ゲームプレイの時間は `ITimeSource`、Prefab を置く `Instantiate` は `Prefabs.Spawn` / プール、`Resources.Load` / `Addressables.Load*` は `IAssetLoader`、`AudioSource.Play` は `Audio` 経由
2. **正当な理由があれば、その行に許可コメント**: 同じ行の行末、または直前の行（コメントだけの行）に `// ddrive-allow: 規則名(理由)`。その 1 行の、規則名が一致する当たりだけが許可される。**理由（括弧内）は必須**（空・括弧なしは無効）。規則名は `Time` / `Instantiate` / `ResourcesLoad` / `AddressablesLoad` / `AudioSourcePlay`（大文字小文字は区別しない）。1 行に 2 規則なら接頭辞ごと繰り返す。例: `// ddrive-allow: Instantiate(NGO の NetworkObject は Instantiate → Spawn が正規手順)`、`// ddrive-allow: Time(Host 引き継ぎのタイムアウトは実時間で測る)`。使われていない許可は Info で出るので、直した後に残った許可は消す
3. **自分で書き換えられない外部コード・生成コードは設定の許可リスト**: `Project Settings > D-Drive > 禁止 API の除外` にパス（前方一致）・規則名・理由（必須）を足す

エージェントは、本来 1 で直せるものを 2 の許可で済ませない。許可を足すときは理由を具体的に書き、人に伝える。許可の一覧は `Tools > D-Drive > Validation > Forbidden API 許可一覧`。
