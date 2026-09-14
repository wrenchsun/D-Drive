# D-Drive アセット発注ツール（Google Apps Script） — セットアップ手順

設計: [docs/32_spec_web.md](../../docs/32_spec_web.md)（§10 が最新の目的＝「アセットの発注」。
§1〜§9 は GAS 構成・認証・ストレージ方式・セキュリティの土台として引き続き有効）。

このディレクトリは W-1〜W-12（GAS 基盤・認証・アセット CRUD・調整値・D-Drive 連携の土台）に加えて、
O-1〜O-10（アセット発注ツールへの再定義: 発注グループ・発注ツリー・私が発注/私が受けた・
発注の詳細・メンバー管理・ガント連携）を実装したものです。
ここから先の手順（デプロイ作成・Google ログイン・トークン発行）は**すべてユーザー本人が行う**もので、
Claude が代行することはできません。

## 画面

`clasp push` してデプロイ①（人向け SPA）を開くと、ヘッダーのナビに次の画面が表示されます。
最初に開く画面は「発注ツリー」です。

- **発注ツリー**（`#/orders`、既定画面）: Presentation 発注グループごとに子（アセット発注）をまとめて
  表示し、件数・納品済数・インポート済数を集計します。Presentation に属さない発注は「単体」として
  最後にまとまります。`editor` 以上は「+ 発注グループを作成」から新規グループを作れます。
  グループに WBS 番号を設定し、かつ「メンバー」画面でガントの URL を設定済みなら、WBS 番号から
  ガントを新規タブで開くリンクが出ます
- **一覧**（`#/assets`）: 検索（識別子・表示名・リファレンス）、種類/状態/発注者/受注者/Presentation
  での絞り込み、列見出しクリックでの並べ替え（発注日・納品期限・受注者 等）、種類でのグループ化表示、
  D-Drive 実状態バッジ（⬜未作成 / 🟡Placeholder / ✅作成済）を表示します。`editor`/`admin` ロールには
  「+ 新規発注」ボタンが表示されます（`viewer` は読み取りのみ）
- **発注の詳細**（一覧の行を選ぶと右側にスライドイン）: 種類・インポート名・Presentation・発注者/受注者・
  発注日/納品期限/納品日・優先度・リファレンス（Markdown、プレビュー切替あり）を編集・保存できます
  （`revision` 楽観ロック。他の人が先に更新していた場合は再取得を促します）。状態は
  **発注済 → 納品済 → インポート済** の3段階固定で、「インポート済」への進行は D-Drive の同期でのみ
  自動的に起こります（手動では選べません）。発注済⇄納品済は「納品済にする」/「発注済に戻す」ボタンで
  手動進行し、納品日が自動記録・自動クリアされます。パラメータ一覧（種類ごとのフィールド・型・説明、
  インポート済なら現在値）は D-Drive からの同期後に表示されます（値そのものはこの画面から編集できません）。
  コメントの投稿・D-Drive 実状態（作成済み/アイコン/使用箇所数/最終同期。読み取り専用）の表示・
  削除（アーカイブ。論理削除）もできます
- **私の発注**（`#/my-orders`）: ログイン中のメールアドレスで「私が発注したもの」（未納品・期限切れを
  バッジ表示）と「私が受けたもの」（期限の昇順）に絞り込んだ2列表示です
- **メンバー**（`#/members`）: ガントの担当者マスタ（「名前(職種)」の表記）を貼り付けて取り込むと、
  発注の詳細画面の発注者/受注者の入力候補（datalist）に反映されます。手動追加（社外の受注者等）・
  メールアドレスの対応付け（任意）・削除もできます。同じ画面の下部に「ガントの URL の設定」があり、
  `admin` ロールのみ変更できます（発注ツリーの WBS リンク先になります。**URL は実データのため、
  ここにもコードにも書きません**。運用担当者が実際のガントの URL を貼り付けてください）
- **調整値**（`#/tuning`）: 既存どおり（下記「調整値編集画面」参照。ナビゲーションが増えても
  独立タブとして変わらず開けることを O-8 で確認済み）
- **マニュアル**（2026-09-14 追加。ナビの「マニュアル」リンク、または Unity の「マニュアル」ボタンが
  開く `?page=manual&p=<ページ名>`）: `docs/DesignerManual/*.html`（デザイナーマニュアル、真実は
  そちら）を配信する。下記「13. デザイナーマニュアルの配信」参照

種類・識別子の重複チェックは入力中に即時検証され、不正な項目は赤枠で表示されます
（サーバー側でも同じ検証を行うため、クライアント側の検証はあくまで UX 用です）。

## 0. 全体像

- GAS プロジェクトのソースは `Tools/SpecWeb/src/`（サーバー側）・`Tools/SpecWeb/html/`（SPA）に置く
- ローカル ⇔ Apps Script プロジェクトの同期は [`clasp`](https://github.com/google/clasp) で行う。
  **本書のコマンドは clasp v3 系の名前で統一しています**（`clasp --help` で一覧を確認できます。
  v2 の `clasp open`/`clasp deploy` 等の名前は v3 には無く、`Unknown command` になります）
- 1 つの Apps Script プロジェクトに **2 つの Web アプリ デプロイ**を作る（docs/32 §2.3）
  | デプロイ | 実行者 | アクセス権 | 用途 |
  |---|---|---|---|
  | ① 人向け SPA | User accessing the web app | Anyone with Google account | ブラウザでの編集・閲覧 |
  | ② D-Drive API | Me | Anyone | Unity Editor からの取得・送信（トークンで保護） |
- ① 人向け SPA は HtmlService の IFRAME サンドボックスで動くため、クライアント→サーバーの通信は
  `google.script.run`（`html/App.html` の `SpecWebClient.callApi` が内部で使う）、画面遷移は
  `google.script.history` を使っています。**`fetch`/`location.hash` には依存していません**
  （2026-09-14 実デプロイで判明した誤りの修正。docs/32 §2.4 の訂正参照）

## 1. Node.js のインストール（ローカルテスト用）

```powershell
winget install OpenJS.NodeJS.LTS
```

インストール後、**新しいターミナルを開いて** `node --version` で確認してください（PATH の反映に
ターミナルの再起動が必要な場合があります）。すでに `C:\Program Files\nodejs\node.exe` がある場合は
インストール済みですが PATH に無いことがあります。そのときは PowerShell から直接
`& "C:\Program Files\nodejs\node.exe" --version` で確認できます。

## 2. clasp のインストールとログイン

```powershell
npm install -g @google/clasp
clasp login
```

`clasp login` はブラウザが開くので、**あなた自身の Google アカウントでログイン**してください
（Claude が代わりにログインすることはできません）。個人の Gmail アカウントで構いません（docs/32 §2.3）。

**先に** https://script.google.com/home/usersettings で「Google Apps Script API」をオン（`clasp login`
と同じアカウントで）にしてください。オフのままだと `clasp create-script`/`clasp push` が権限エラーに
なります。オンにしてから反映まで数分かかることがあります。

## 3. Apps Script プロジェクトの作成

`Tools/SpecWeb/` に移動してから実行します。

```powershell
cd Tools/SpecWeb
clasp create-script --type webapp --title "D-Drive アセット発注ツール"
```

- 既に Apps Script プロジェクトがある場合は `clasp clone-script <scriptId>` を使ってください
- 実行すると `.clasp.json`（scriptId が入る）が生成されます。**このファイルは `.gitignore` 対象なので
  コミットされません**（`.clasp.json.example` を参考にした雛形）
- **注意**: `clasp create-script` は `Tools/SpecWeb/appsscript.json` をこのプロジェクトの既定値
  （`timeZone` `Asia/Tokyo`・`webapp` 設定・最小の `oauthScopes` 等)ではなく、新規プロジェクトの初期値
  (`timeZone` `America/New_York`、`webapp`/`oauthScopes` 無し等)で**上書き**します。
  `clasp create-script` の直後に `git diff Tools/SpecWeb/appsscript.json` で確認し、上書きされていたら
  `git checkout -- Tools/SpecWeb/appsscript.json` でコミット済みの内容に戻してから `clasp push`
  してください(戻さずに push すると、デプロイ②のタイムゾーンや oauth スコープの設定が意図しない
  ものになります)
- `clasp push` でこのディレクトリの `src/**/*.js` と `html/**/*.html` を Apps Script プロジェクトへ送ります
  （`.claspignore` で `test/`・`*.md`・`.clasp.json` 等は除外済み）

```powershell
clasp push
```

## 4. Drive フォルダの用意とスクリプトプロパティの設定

1. Google Drive に、発注データ（`assets.json`/`orderGroups.json`/`members.json`/`tuning.json`/
   `users.json` 等）を置くフォルダを 1 つ作成し、URL からフォルダ ID を控える
2. そのフォルダを、チームメンバー（個人の Google アカウント）に編集権限で共有する
   （docs/32 §7「デプロイ①は実行者=アクセスした人のため、各メンバー個人にも Drive の編集権限が必要」）
3. Apps Script エディタ（`clasp open-script` で開く）で「プロジェクトの設定」→「スクリプト プロパティ」に
   `SPEC_WEB_DRIVE_FOLDER_ID` = 上記フォルダ ID を追加する

```powershell
clasp open-script
```

## 5. 最初の管理者ユーザーの登録

まだ Web UI に許可リスト編集機能は無いため、Apps Script エディタから
`src/Api/UserAdmin.js` の関数を**あなた自身のメールアドレスで**直接実行してください。

1. `clasp open-script` で Apps Script エディタを開く
2. エディタ上部の関数選択で `upsertSpecWebUser` を選び、実行前に一時的に引数を書き換えて実行する、
   もしくは「実行」→「関数を実行」の代わりに、エディタ内で以下のような 1 行を一時的に追加して実行する:
   ```js
   function bootstrapAdmin() {
     upsertSpecWebUser('your-name@gmail.com', 'あなたの表示名', 'admin');
   }
   ```
3. 実行後、その一時関数は削除してよい（`users.json` には残る）
4. `clasp run-function bootstrapAdmin`（Apps Script API 経由でローカルから直接関数を実行することも
   できます。事前にデプロイが必要）でも実行できます

## 6. API トークンの発行（D-Drive 用）

同じくエディタから、読み取り用・書き込み用のトークンをそれぞれ発行します（`src/Api/TokenAdmin.js`）。

```js
function bootstrapTokens() {
  Logger.log('READ token: ' + issueApiToken('read'));
  Logger.log('WRITE token: ' + issueApiToken('write'));
}
```

実行後、「実行数」のログに出力されたトークンをコピーし、D-Drive 側の Unity Editor の
`EditorPrefs`（マシンごと）に保存する設定画面（既存の `DDriveSpecSettings` 相当）に貼り付けます。
**トークンは git や Slack の履歴に残る形で共有しない**（既存の連絡手段でも、後で消せるチャンネル等を推奨）。

### トークンのローテーション（推奨 3 か月ごと、または漏洩時は即時）

```js
function rotateWriteToken() {
  Logger.log('新しい WRITE token: ' + rotateApiToken('write')); // 旧トークンはまだ有効
}
// チームへ配布・猶予期間後に、旧トークンの文字列を指定して:
function revokeOldWriteToken() {
  revokeApiToken('write', '旧トークンの文字列');
}
// 漏洩時の緊急対応（猶予なしで全失効）:
function emergencyRevokeAllWriteTokens() {
  revokeAllApiTokens('write');
}
```

## 7. デプロイの作成

**エディタの UI から作成するのが確実です**（clasp v3 の `create-deployment` はマニフェストの
Web アプリ設定（実行ユーザー・アクセス権）を必ずしも意図通りに反映しないことがあるため、初回は
UI 推奨）。Apps Script エディタ右上「デプロイ」→「新しいデプロイ」から、**2 回**デプロイを作成します。

1. **① 人向け SPA**: 種類「ウェブアプリ」、実行ユーザー「**アクセスしているユーザー**」、
   アクセスできるユーザー「**Google アカウントを持つ全員**」
2. **② D-Drive API**: 種類「ウェブアプリ」、実行ユーザー「**自分**」、
   アクセスできるユーザー「**全員**」

それぞれのデプロイ URL をメモしておいてください（① はチームに共有する URL、② は D-Drive の設定に入れる URL）。

初回アクセス時、①は各メンバーが個別に Google の OAuth 同意を求められます（docs/32 §2.3）。

### コードを更新した後の再デプロイ手順（URL を変えずに新バージョンへ）

コードを変更して `clasp push` しただけでは、**既存のデプロイ URL には反映されません**
（Apps Script は「デプロイ」ごとにコードのスナップショット（バージョン）を固定するため）。
毎回、次のどちらかで両方のデプロイ（①②）を新バージョンに更新してください:

- **エディタの UI（推奨）**: 「デプロイ」→「デプロイを管理」→ 更新したいデプロイの鉛筆（編集）アイコン
  → 「バージョン」を「新バージョン」に変更 → 「デプロイ」。URL は変わりません
- **clasp コマンド**: `clasp list-deployments` で `deploymentId` を確認し、
  `clasp update-deployment <deploymentId>`（別名 `redeploy`）で同じデプロイ ID のまま新バージョンに
  更新できます（`clasp create-deployment` は**新しい** URL のデプロイを作ってしまうため、
  既存 URL を維持したい更新には使わない）

### 動作確認・ログ

```powershell
clasp open-web-app   # デプロイ済みの Web アプリを既定ブラウザで開く
clasp tail-logs      # 直近の実行ログをリアルタイム表示
clasp open-logs      # Apps Script の実行ログ（Stackdriver）をブラウザで開く
```

### 既知の注意点（実装時に確認済み・公式ドキュメント根拠）

- Content Service には HTTP ステータスコードを設定する API が無いため、エラーは常に本文の `status`
  フィールドで表現されます（実際の HTTP 応答は 200 系になります）
- `doPost`（② D-Drive API）の応答は `script.googleusercontent.com` への 302 リダイレクトを経由します。
  `UnityWebRequest` は既定でリダイレクトに追従します。**実デプロイで確認済み**（2026-09-14、
  トークン無しの拒否応答〈401 の JSON〉で経路を確認。トークン付きの正常系は D-Drive からの同期で
  確認予定。docs/32 §9-4 参照）
- 個人の Gmail アカウントでは Web アプリのアクセス権に「特定のドメインに限定」オプションが無いため、
  本実装はコード側の許可リスト（`users.json`）で代替しています
- ① 人向け SPA から `api=1` を token 無しで呼んだとき、Google ログインもしていない場合は
  「API トークンがありません」という D-Drive 向けの文言が返ります。これは② D-Drive API 宛の
  エラーメッセージであり、① の画面自体は `google.script.run`（token 不要）を使うため、この文言が
  ①の画面に出ることは通常ありません（もし出た場合は D-Drive 側のトークン設定を確認してください）

## 8. ローカルテストの実行

**npm install は不要**（Node 組み込みの `node:test`/`node:assert` だけを使っています）。

```powershell
node --test Tools/SpecWeb/test
```

`node` が PATH に無い場合は、フルパスで実行してください:

```powershell
& "C:\Program Files\nodejs\node.exe" --test "Tools/SpecWeb/test/*.test.js"
```

`test/load-gas.js` が `src/**/*.js` を Node の `vm` モジュールで 1 つの共有コンテキストに読み込み、
`DriveApp`/`LockService`/`PropertiesService`/`Session`/`ContentService`/`HtmlService`/`Utilities`/`Logger`
という GAS 側のホストグローバルだけをフェイクに差し替えます（`Storage.js`/`Auth.js`/`Code.js` 自体は
本物のコードのまま検証されます）。同様に `test/app.test.js` は `google.script.run`/`google.script.history`
のフェイクを使って `html/App.html` の画面遷移・サーバー呼び出しを検証します。

## 9. `clasp push`/`clasp pull` の運用

- コード変更後は `cd Tools/SpecWeb && clasp push` で反映する（→ 上記「7. デプロイの作成」の
  再デプロイ手順で実際の URL に反映されるまで有効にならない点に注意）。
  **`docs/DesignerManual/*.html` を編集した場合は、`clasp push` の前に必ず
  `./push.ps1`（`Tools/SpecWeb` 内で実行）を使うこと**（下記「13. デザイナーマニュアルの配信」参照。
  素の `clasp push` だけだとマニュアルの生成物が古いままになる）
- Apps Script エディタ上で直接編集した場合は `clasp pull` でローカルに取り込んでから git にコミットする
  （ソースの正本はこの repo 側。エディタでの直接編集は緊急時のみに留めることを推奨）

## 10. メンバーの取り込み（O-9）

発注者/受注者の入力候補は、ガントの担当者マスタから**貼り付けるだけ**で用意できます
（ガントのスプレッドシートへは一切アクセスしません）。

1. ① 人向け SPA にログインし、ナビゲーションの「メンバー」を開く
2. ガントのスプレッドシートの担当者マスタ範囲（1 行 1 名、「名前(職種)」の表記）を選択してコピー
3. 「メンバー」画面上部のテキストエリアに貼り付け、「取り込む」を押す
4. 表記はそのまま保存されます（独自に変換しません）。既に取り込み済みの表記に対応付けたメール
   アドレスがあれば保持されます
5. 社外の受注者等、ガントに載らないメンバーは同じ画面の下部「手動追加」から個別に追加できます

## 11. ガントの URL の設定（O-10）

発注ツリー画面の WBS 番号リンク（「WBS 3.2.1 ↗」）が開く先を設定します。**URL はこの README にも
コードにも書きません**（docs/32 §10.5②・§2.5 のトークンと同じ扱い）。

1. `admin` ロールでログインし、「メンバー」画面下部の「ガントの URL の設定」を開く
2. 実際に運用しているガントのスプレッドシート URL を貼り付けて「保存」を押す
3. 以降、Presentation 発注グループに `wbsNo`（自由入力の文字列）を設定すると、発注ツリー画面から
   このリンクで新規タブが開きます（URL 未設定の間はリンクは表示されず、WBS 番号だけがテキスト表示
   されます）

## 12. 調整値編集画面（W-6〜W-8）

デプロイ①（人向け SPA）にログインすると、ヘッダーのナビに「調整値」リンクが表示される
（`#/tuning`）。画面は 2 つのタブに分かれる（docs/32 §4.4）。

- **スカラー一覧タブ**: キー・グループ・型・値・範囲（または enum の選択肢）・単位・説明・ロックの
  一覧を表示する。値は `valueType` に応じて数値入力/チェックボックス/テキスト/ドロップダウンに
  自動で切り替わる。編集するとその場でサーバーに保存され（`tuningScalarUpdate`）、範囲外・型違いなら
  入力欄が赤くなる（クライアント側の即時検証、サーバー側でも同じ検証を行う）。上部の検索欄で
  キー・グループを絞り込める。`🔒` はロック中の調整値（`admin` ロールでログインしたときだけ編集可）。
  「+ 新規スカラー」から作成できる（`editor` 以上）
- **テーブルタブ**: 上部のドロップダウンでテーブルを選び、列（ヘッダーの `×` で削除）・行
  （「行削除」ボタン）を編集する。セルは矢印キー/Tab/Enter で移動でき、Excel やスプレッドシートから
  タブ区切りテキストを貼り付けると複数セルへ一括反映される（`tuningTableUpdateCells`、1 件でも
  検証に落ちれば貼り付け全体を中断し何も保存しない）。行・テーブル全体にコメント欄がある
- 各コメント欄（スカラー・テーブル全体・テーブルの行）は `editor` 以上が投稿でき、`viewer` は一覧のみ
- ロールは Google ログインの許可リスト（`users.json`）から決まる（`window.SpecWebCurrentUser`、
  `html/Index.html` が `currentUser` をクライアントへ渡す）

## 13. デザイナーマニュアルの配信（2026-09-14 追加）

`docs/DesignerManual/*.html`（デザイナーマニュアル、真実はそちら）を、この Web アプリからも
開けるようにしている。設計は [docs/32_spec_web.md「実装メモ（マニュアル配信）」](../../docs/32_spec_web.md)。

- Unity の「マニュアル」ボタン（メインツールバー、再生ボタンの右）が
  `<①のデプロイURL>?page=manual&p=<ページ名（拡張子なし、トップは Readme）>` を開く
- ① 人向け SPA のナビにも「マニュアル」リンクが表示される（`#/orders` 等と同じ画面切り替え）
- 本文は `docs/DesignerManual/*.html` から `Tools/SpecWeb/tools/build-manual.js`（Node 標準の
  fs/path のみ、依存ゼロ）が事前生成した断片 HTML（`Tools/SpecWeb/html/manual/<page>.html`。
  style インライン化・画像 data URI 化・ページ間リンク書き換え済み）を配信するだけで、
  この GAS プロジェクト側では本文を直接編集しない

**`docs/DesignerManual/*.html`/`style.css`/`images/*.png` を編集したら、必ず次のいずれかを行う**
（忘れると Web 側のマニュアルが古いままになり、`Tools/SpecWeb/test/build-manual.test.js` の
ドリフト検出テストが red になる）:

```powershell
cd Tools/SpecWeb
./push.ps1              # build-manual.js を実行 → clasp push（推奨。デプロイの更新は別途「7.」の手順が必要）
```

または手動で:

```powershell
& "C:\Program Files\nodejs\node.exe" Tools/SpecWeb/tools/build-manual.js
git add Tools/SpecWeb/html/manual Tools/SpecWeb/src/ManualPages.js
git commit -m "..."
cd Tools/SpecWeb && clasp push
```

再生成される生成物（`Tools/SpecWeb/html/manual/*.html`・`Tools/SpecWeb/src/ManualPages.js`）は
git にコミットする方針（Node が無い環境でも `clasp push` だけで最新化できるようにするため。
ドリフトのリスクは上記のテストで検出する）。

## 実装ファイル一覧

| ファイル | 内容 |
|---|---|
| `src/Code.js` | doGet/doPost（② D-Drive API、token）+ `specWebUiCall`（① 人向け SPA、`google.script.run` 専用の入口） |
| `src/Auth.js` | 許可リスト照合・API トークン検証・ロール判定 |
| `src/Storage.js` | Drive JSON コレクションの読み書き + `LockService` + `revision` 楽観ロック |
| `src/Assets.js` | アセット発注 CRUD API（O-1〜O-5・O-7 で発注向けに再定義） |
| `src/Migration.js` | 旧スキーマ（未着手/仮/本番/保留・assignee/note）からの読み込み時変換 + 一度だけの移行スクリプト（O-1） |
| `src/OrderGroups.js` | Presentation 発注グループ CRUD API（O-2） |
| `src/Members.js` | メンバー管理・貼り付け取り込み API（O-9） |
| `src/Settings.js` | ガント URL 等の設定値（O-10） |
| `src/AssetParams.js` | パラメータスキーマ・現在値の受け皿（O-6。D-Drive からの実送信は別チケット） |
| `src/DDriveSync.js` | D-Drive → Web 送信 API（choices/assetState/tuningUsage）。O-7 で `assetState` にインポート済み自動判定を追加 |
| `src/Tuning.js`/`TuningTable.js`/`TuningComments.js`/`TuningCommon.js` | 調整値（スカラー・テーブル・コメント） |
| `html/App.html` | SPA の枠（`google.script.run`/`google.script.history` を使う画面遷移・サーバー呼び出し） |
| `html/AssetsLogic.html` | 一覧・詳細・私の発注が使う純粋関数（絞り込み・並べ替え・検証・Markdown 変換） |
| `html/Assets.html` | 一覧・発注の詳細画面 |
| `html/OrderTreeLogic.html`/`OrderTree.html` | 発注ツリーの集計ロジック・画面（O-2） |
| `html/MyOrders.html` | 私の発注画面（O-4） |
| `html/Members.html` | メンバー管理 + ガント URL 設定画面（O-9・O-10） |
| `html/TuningGrid.html`/`Tuning.html` | 調整値編集画面 |
| `src/Manual.js` | マニュアル配信 API（`manualGet`） |
| `src/ManualPages.js`（生成物） | マニュアルのページ名許可リスト（`tools/build-manual.js` が生成） |
| `html/Manual.html` | マニュアル画面（ナビ「マニュアル」リンク + 本文差し込み + リンク処理） |
| `html/manual/*.html`（生成物） | ページごとの断片 HTML（`tools/build-manual.js` が生成） |
| `tools/build-manual.js` | `docs/DesignerManual/*.html` → 上記 2 つの生成物を作るスクリプト |
| `push.ps1` | `build-manual.js` を実行してから `clasp push` する |
