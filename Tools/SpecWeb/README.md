# D-Drive 仕様書 Web（Google Apps Script） — セットアップ手順

設計: [docs/32_spec_web.md](../../docs/32_spec_web.md)。このディレクトリは W-1〜W-3（雛形・ストレージ層・認証）の実装。
ここから先の手順（デプロイ作成・Google ログイン・トークン発行）は**すべてユーザー本人が行う**もので、
Claude が代行することはできません。

## 0. 全体像

- GAS プロジェクトのソースは `Tools/SpecWeb/src/`（サーバー側）・`Tools/SpecWeb/html/`（SPA）に置く
- ローカル ⇔ Apps Script プロジェクトの同期は [`clasp`](https://github.com/google/clasp) で行う
- 1 つの Apps Script プロジェクトに **2 つの Web アプリ デプロイ**を作る（docs/32 §2.3）
  | デプロイ | 実行者 | アクセス権 | 用途 |
  |---|---|---|---|
  | ① 人向け SPA | User accessing the web app | Anyone with Google account | ブラウザでの編集・閲覧 |
  | ② D-Drive API | Me | Anyone | Unity Editor からの取得・送信（トークンで保護） |

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

## 3. Apps Script プロジェクトの作成

`Tools/SpecWeb/` に移動してから実行します。

```powershell
cd Tools/SpecWeb
clasp create --type webapp --title "D-Drive 仕様書"
```

- 既に Apps Script プロジェクトがある場合は `clasp clone <scriptId>` を使ってください
- 実行すると `.clasp.json`（scriptId が入る）が生成されます。**このファイルは `.gitignore` 対象なので
  コミットされません**（`.clasp.json.example` を参考にした雛形）
- `clasp push` でこのディレクトリの `src/**/*.js` と `html/**/*.html` を Apps Script プロジェクトへ送ります
  （`.claspignore` で `test/`・`*.md`・`.clasp.json` 等は除外済み）

```powershell
clasp push
```

## 4. Drive フォルダの用意とスクリプトプロパティの設定

1. Google Drive に、仕様書データ（`assets.json`/`tuning.json`/`users.json` 等）を置くフォルダを 1 つ作成し、
   URL からフォルダ ID を控える
2. そのフォルダを、チームメンバー（個人の Google アカウント）に編集権限で共有する
   （docs/32 §7「デプロイ①は実行者=アクセスした人のため、各メンバー個人にも Drive の編集権限が必要」）
3. Apps Script エディタ（`clasp open` で開く）で「プロジェクトの設定」→「スクリプト プロパティ」に
   `SPEC_WEB_DRIVE_FOLDER_ID` = 上記フォルダ ID を追加する

```powershell
clasp open
```

## 5. 最初の管理者ユーザーの登録

まだ Web UI に許可リスト編集機能は無いため（後続チケットで追加予定）、Apps Script エディタから
`src/Api/UserAdmin.js` の関数を**あなた自身のメールアドレスで**直接実行してください。

1. `clasp open` で Apps Script エディタを開く
2. エディタ上部の関数選択で `upsertSpecWebUser` を選び、実行前に一時的に引数を書き換えて実行する、
   もしくは「実行」→「関数を実行」の代わりに、エディタ内で以下のような 1 行を一時的に追加して実行する:
   ```js
   function bootstrapAdmin() {
     upsertSpecWebUser('your-name@gmail.com', 'あなたの表示名', 'admin');
   }
   ```
3. 実行後、その一時関数は削除してよい（`users.json` には残る）

## 6. API トークンの発行（D-Drive 用）

同じくエディタから、読み取り用・書き込み用のトークンをそれぞれ発行します（`src/Api/TokenAdmin.js`）。

```js
function bootstrapTokens() {
  Logger.log('READ token: ' + issueApiToken('read'));
  Logger.log('WRITE token: ' + issueApiToken('write'));
}
```

実行後、「実行数」のログに出力されたトークンをコピーし、D-Drive 側の Unity Editor の
`EditorPrefs`（マシンごと）に保存する設定画面（既存の `DDriveSpecSettings` 相当。W-9 で接続）に貼り付けます。
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

Apps Script エディタの「デプロイ」→「新しいデプロイ」から、**2 回**デプロイを作成します。

1. **① 人向け SPA**: 種類「ウェブアプリ」、実行ユーザー「**アクセスしているユーザー**」、
   アクセスできるユーザー「**Google アカウントを持つ全員**」
2. **② D-Drive API**: 種類「ウェブアプリ」、実行ユーザー「**自分**」、
   アクセスできるユーザー「**全員**」

それぞれのデプロイ URL をメモしておいてください（① はチームに共有する URL、② は D-Drive の設定に入れる URL）。

初回アクセス時、①は各メンバーが個別に Google の OAuth 同意を求められます（docs/32 §2.3）。

### 既知の注意点（実装時に確認済み・公式ドキュメント根拠）

- Content Service には HTTP ステータスコードを設定する API が無いため、エラーは常に本文の `status`
  フィールドで表現されます（実際の HTTP 応答は 200 系になります）
- `doPost` の応答は `script.googleusercontent.com` への 302 リダイレクトを経由します。`UnityWebRequest`
  は既定でリダイレクトに追従しますが、**W-9（`SpecWebFetcher`）着手時に実機で疑似トークン込みの
  `doPost` 呼び出しを確認してください**（docs/32 §2.4・§9-4、未確認のまま残っている項目）
- 個人の Gmail アカウントでは Web アプリのアクセス権に「特定のドメインに限定」オプションが無いため、
  本実装はコード側の許可リスト（`users.json`）で代替しています

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
本物のコードのまま検証されます）。

## 9. `clasp push`/`clasp pull` の運用

- コード変更後は `cd Tools/SpecWeb && clasp push` で反映する
- Apps Script エディタ上で直接編集した場合は `clasp pull` でローカルに取り込んでから git にコミットする
  （ソースの正本はこの repo 側。エディタでの直接編集は緊急時のみに留めることを推奨）
