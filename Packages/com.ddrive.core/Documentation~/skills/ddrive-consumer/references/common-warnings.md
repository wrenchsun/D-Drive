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

1. **D-Drive の API に直す**: Prefab を置く `Instantiate` は `Prefabs.Spawn` / プール、`Resources.Load` / `Addressables.Load*` は `IAssetLoader`、`AudioSource.Play` は `Audio` 経由。**ゲームプレイの時間**（ポーズ・ヒットストップに従わせたいもの）は、(a) `IAssetManager` を実装して `DDriveRuntimeBootstrap.Instance.Loop.GameLoop.Register(...)` で登録し、`Tick(float dt)` の `dt`（ヒットストップ込み。ポーズは `OnPause` で受ける。ポーズ中も `Tick` は呼ばれ `dt` は 0 にならない）を使う。**登録は `OnEnable`、解除は `OnDisable` で必ず対にする**（`GameLoop` は破棄されたオブジェクトを自動では外さない。外し忘れると破棄後も毎フレーム `Tick` が呼ばれ、`Tick` に例外の隔離は無いので、例外が出るとその後ろに登録された Manager のそのフレームの `Tick` も止まる）。`DDriveRuntimeBootstrap.Instance` / `Loop` が null（Bootstrap の無いシーン・起動前・終了時）のときは登録せず何もしない。登録に `IsReady` は要らない（`Loop` は Bootstrap の `Awake` で揃う）。`Tick` の中で例外を出さない。同じシーンに最初から置くスクリプトで実行順が Bootstrap（`[DefaultExecutionOrder(-1000)]`、`Awake` で起動配線）より小さいとき、または Bootstrap が後からロードされるシーン構成では、`OnEnable` の時点で `Instance` が null のため登録されず、例外も警告も出ないまま `Tick` が一度も来ません。`OnEnable` に加えて `Start` でも未登録なら登録を試し（`Register` は二重登録しても 1 回扱い）、それでも無ければ警告ログを 1 行出してください（コード例のとおり）。`Tick` の中で自分自身や他の登録済み Manager を無効化したり `Unregister` したりしないでください。`GameLoop` は登録順の添字で走査するため、走査中に外すと直後の Manager 1 つがそのフレームだけ `Tick` されません（例外は出ません。`Destroy` は遅延するので影響しません）。外したいときはフラグを立てて、次のフレームの頭か `LateUpdate` で外します。コードを自分の asmdef に置く場合は `DDrive.Foundation` と `DDrive.Runtime` への参照が必要です（asmdef なしの `Assembly-CSharp` なら不要）。登録 / 解除のコード例は持ち込み先ガイドの運用ページ（`Documentation~/ConsumerGuide/operation.html`「禁止 API の指摘への対処」）。(b) Tick に乗せにくければ、ゲーム側の時間源を 1 か所（例: `GameTime`）に作り、その中だけで `Time.unscaledDeltaTime * DDriveRuntimeBootstrap.Instance.Loop.TimeService.TimeScale` を読んで許可コメントを 1 行書く（ポーズに従わせるなら `Loop.PauseService.IsPaused(PauseChannel.Gameplay)` で 0 にする）。**`ITimeSource` はゲームのコード向けではない**（Foundation 内部向け。`Time.deltaTime` を返すだけでヒットストップ・ポーズに従わず、置き換えても当たりが消えるだけ）。**実時間の計測**（タイムアウト等）は、検査の対象外の `Time.realtimeSinceStartupAsDouble` / `Stopwatch` に替えれば許可が要らない。`Time` 規則が当たるのは `Time.time` / `deltaTime` / `unscaledDeltaTime` / `timeAsDouble` / `unscaledTime` だけ
2. **正当な理由があれば、その行に許可コメント**: 同じ行の行末、または直前の行（コメントだけの行）に `// ddrive-allow: 規則名(理由)`。その 1 行の、規則名が一致する当たりだけが許可される。**理由（括弧内）は必須**（空・括弧なしは無効）。規則名は `Time` / `Instantiate` / `ResourcesLoad` / `AddressablesLoad` / `AudioSourcePlay`（大文字小文字は区別しない）。1 行に 2 規則なら接頭辞ごと繰り返す。例: `// ddrive-allow: Instantiate(NGO の NetworkObject は Instantiate → Spawn が正規手順)`、`// ddrive-allow: Time(Host 引き継ぎのタイムアウトは実時間で測る)`。使われていない許可は Info で出るので、直した後に残った許可は消す
3. **自分で書き換えられない外部コード・生成コードは設定の許可リスト**: `Project Settings > D-Drive > 禁止 API の除外` にパス（プロジェクトルートからの相対パス、2 階層以上。ファイルと完全一致、またはフォルダの配下に一致。`Assets/Foo` は `Assets/FooBar/` に当たらない。大文字小文字は区別しない。`Assets` 単独・走査ルートそのもの・絶対パス・`..` は無効）・規則名・理由（必須）を足す

エージェントは、本来 1 で直せるものを 2 の許可で済ませない。許可を足すときは理由を具体的に書き、人に伝える。当たりと許可の一覧は `Tools > D-Drive > Validation > 禁止 API の検査`（ウィンドウ。許可されていない当たり・許可済み・無効 / 未使用の許可。`Validation > Run All` は禁止 API を走査しない）。許可コメントの書き方の注意: 理由の閉じ括弧より後ろは無視される / `ddrive-allow-file:` のような別の接頭辞は許可として扱われない / 「直前の行」は当たりの出た行の 1 行上（複数行の文は当たりの行の上に書く）。
