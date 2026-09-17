/**
 * 認証・認可。docs/32_spec_web.md §2.3（アクセス制御）・§3.4（ロール）・§7（セキュリティ）を実装したもの。
 *
 * ① 人向け SPA（デプロイ①、実行者=アクセスした人）:
 *    Session.getActiveUser().getEmail() を users.json の許可リストと照合する。
 * ② D-Drive API（デプロイ②、実行者=Me、ログイン不要）:
 *    リクエストの `token` パラメータをスクリプトプロパティ上のトークンと比較する
 *    （読み取り用・書き込み用の 2 種類）。
 *
 * 2026-09-17 のセキュリティ修正（P1-1・P1-2）で、この 2 経路は完全に分離した:
 *   - `?api=1`（doGet/doPost、Code.js）は **API トークン必須**。`authenticateRequest_(e)` は
 *     token が無ければ 401 を返すだけで、Google セッションへはフォールバックしない。
 *     理由: `/exec` への通常のブラウザ遷移はログイン済みユーザーの Google セッションで実行される
 *     ため、フォールバックがあると「リンクを踏ませるだけで admin 権限の API が走る」CSRF に
 *     なっていた（`?api=1&name=users.upsert&...` を admin に踏ませる攻撃）。
 *     `google.script.run` は Google 側の CSRF 保護を持つが、`doGet`/`doPost` の直叩きには無い。
 *   - 人向け SPA（①）は `google.script.run` → `specWebUiCall`（Code.js）→ `authenticateSession_()`
 *     のみを使う（html/App.html の SpecWebClient.callApi）。`?api=1` はもう呼ばない。
 *
 * また、クライアント（`google.script.run.<関数名>()`）から直接呼ばれてよいのは
 * `doGet`/`doPost`/`specWebUiCall` と、運用者が Apps Script エディタから手で実行する
 * 管理関数（`specWebAssertAdminSession_` で admin を要求するもの）だけである。
 * それ以外のトップレベル関数は必ず末尾 `_` にする（GAS は末尾 `_` の関数を
 * `google.script.run` から呼べない）。この規約は test/publicFunctions.test.js が機械的に検証する。
 */

var SPEC_WEB_ROLES = { VIEWER: 'viewer', EDITOR: 'editor', ADMIN: 'admin' };
var SPEC_WEB_ROLE_RANK = { viewer: 0, editor: 1, admin: 2 };
var SPEC_WEB_USERS_COLLECTION = 'users';
var SPEC_WEB_TOKEN_KINDS = ['read', 'write'];
var SPEC_WEB_TOKENS_PROPERTY_PREFIX = 'SPEC_WEB_TOKENS_';

function specWebNormalizeEmail_(email) {
  return String(email || '').toLowerCase();
}

/** users.json から 1 ユーザーを取得する（無ければ null）。 */
function findSpecWebUserByEmail_(email) {
  if (!email) return null;
  return Storage.getItem(SPEC_WEB_USERS_COLLECTION, specWebNormalizeEmail_(email));
}

/**
 * ①のセッション（Google ログイン）を許可リストと照合する。
 * @return {{ok:boolean, status?:number, message?:string, email?:string, role?:string, displayName?:string}}
 */
function authenticateSession_() {
  var email = SessionAdapter.getActiveUserEmail();
  if (!email) {
    return { ok: false, status: 401, message: 'ログインが必要です（Google アカウントでアクセスしてください）' };
  }
  var user = findSpecWebUserByEmail_(email);
  if (!user) {
    // 2026-09-14 追補（O-14）: 本人が管理者に伝えやすいよう、ログイン中のメールアドレスを
    // message とは別に email フィールドでも返す（呼び出し元の Code.js の拒否ページ・
    // authenticateRequest_ の 403 分岐がそのまま使う）。本人自身のメールなので表示してよい。
    return {
      ok: false,
      status: 403,
      message: 'メンバーのみ利用できます。管理者にこのメールアドレスを伝えて users.json への追加を依頼してください。',
      email: email
    };
  }
  return { ok: true, email: email, role: user.role, displayName: user.displayName || email };
}

/** 定数時間の文字列比較（トークン比較でのタイミング攻撃を避けるため）。 */
function constantTimeEquals_(a, b) {
  a = String(a === null || a === undefined ? '' : a);
  b = String(b === null || b === undefined ? '' : b);
  var mismatch = a.length === b.length ? 0 : 1;
  var length = Math.max(a.length, b.length, 1);
  for (var i = 0; i < length; i++) {
    var ca = i < a.length ? a.charCodeAt(i) : 0;
    var cb = i < b.length ? b.charCodeAt(i) : 0;
    mismatch |= ca ^ cb;
  }
  return mismatch === 0;
}

function specWebAssertTokenKind_(kind) {
  if (SPEC_WEB_TOKEN_KINDS.indexOf(kind) === -1) {
    throw new Error('kind は "read" か "write" のいずれかです: ' + kind);
  }
}

function specWebTokensPropertyKey_(kind) {
  specWebAssertTokenKind_(kind);
  return SPEC_WEB_TOKENS_PROPERTY_PREFIX + kind.toUpperCase();
}

/** スクリプトプロパティに保存された（read/write 種別の）トークン一覧を返す。 */
function getStoredTokens_(kind) {
  var raw = PropertiesAdapter.getScriptProperty(specWebTokensPropertyKey_(kind));
  if (!raw) return [];
  try {
    var parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch (err) {
    return [];
  }
}

function setStoredTokens_(kind, tokens) {
  PropertiesAdapter.setScriptProperty(specWebTokensPropertyKey_(kind), JSON.stringify(tokens));
}

/**
 * ②の API トークンを検証する。
 * @return {?{kind:('read'|'write')}} 一致しなければ null。
 */
function verifyApiToken_(token) {
  if (!token) return null;
  // write トークンは read 相当の操作も許可する（上位ロール）ため先に見る。
  for (var k = 0; k < SPEC_WEB_TOKEN_KINDS.length; k++) {
    var kind = SPEC_WEB_TOKEN_KINDS[SPEC_WEB_TOKEN_KINDS.length - 1 - k]; // ['write','read'] の順
    var tokens = getStoredTokens_(kind);
    for (var i = 0; i < tokens.length; i++) {
      if (constantTimeEquals_(token, tokens[i])) {
        return { kind: kind };
      }
    }
  }
  return null;
}

/**
 * `?api=1`（② D-Drive API）のリクエストを認証する。doGet/doPost から呼ばれる唯一の入口（Code.js）。
 *
 * **2026-09-17 仕様変更（P1-2、CSRF 対策）**: API トークン必須にした。以前は token が無い場合に
 * `authenticateSession_()`（Google ログイン + users.json 許可リスト）へフォールバックしていたが、
 * `/exec` への通常のブラウザ遷移はアクセスした人の Google セッションで実行されるため、
 * `https://script.google.com/.../exec?api=1&name=users.upsert&email=...&role=admin` のような
 * リンクを admin に踏ませるだけで admin 権限の API が実行できてしまっていた（CSRF）。
 * `google.script.run` には Google 側の CSRF 保護があるが、`doGet`/`doPost` の直叩きには無い。
 * 人向け SPA は 2026-09-14 の修正で `google.script.run`（→ `specWebUiCall`）のみを使うように
 * なっており、`?api=1` をセッションで叩く正当なクライアントはもう存在しない。
 * 人が `?api=1` の動作確認をしたいときは read トークンを使う（README §7）。
 *
 * @return {{ok:boolean, status?:number, message?:string, principal?:string,
 *           role?:string, tokenKind?:string}}
 */
function authenticateRequest_(e) {
  var params = (e && e.parameter) || {};
  if (!params.token) {
    return {
      ok: false,
      status: 401,
      message: 'API トークンがありません（D-Drive の「仕様書と同期」の設定を確認してください）'
    };
  }
  var tokenInfo = verifyApiToken_(params.token);
  if (!tokenInfo) {
    // 2026-09-14 追補（オーケストレーター指示）: D-Drive がトークンを入れ忘れた/間違えた
    // ケースで気付きやすいよう、api=1 経路専用の文言にする（authenticateRequest_ は
    // Code.js の handleApiRequest_ からしか呼ばれない=常に api=1 経路であるため、
    // ここでの拒否は常に「② D-Drive API」宛のリクエストに対するものである）。
    return {
      ok: false,
      status: 401,
      message: 'API トークンが正しくありません（D-Drive の「仕様書と同期」の設定を確認してください）'
    };
  }
  return {
    ok: true,
    principal: 'ddrive:' + tokenInfo.kind,
    tokenKind: tokenInfo.kind,
    // 書き込みトークンは editor 相当・読み取りトークンは viewer 相当として扱う。
    // ただし D-Drive → Web の書き込みトークンで呼べる内容自体は、
    // ロールとは別にハンドラ側で choices/assetState/tuningUsage の kind 許可リストに
    // 固定する（docs/32_spec_web.md §5.2・§7、実装は W-12）。
    role: tokenInfo.kind === 'write' ? SPEC_WEB_ROLES.EDITOR : SPEC_WEB_ROLES.VIEWER
  };
}

/**
 * 2026-09-17（P1-1）: Apps Script エディタから手で実行する運用関数（`issueApiToken` 等、
 * 末尾 `_` を付けずに公開名のまま残すもの）の先頭で必ず呼ぶガード。
 *
 * GAS では**末尾 `_` の無いトップレベル関数はすべて `google.script.run.<名前>()` で
 * クライアントから直接呼べる**（`registerApi_` に登録していないことは何の防御にもならない）。
 * 許可リスト外の Google アカウントでも、拒否ページ（renderUi_）の iframe から
 * `google.script.run` ブリッジに到達できるため、公開名のまま残す関数は必ずここで
 * 「ログイン済み かつ users.json 上 admin」であることを確認する。
 *
 * エディタから実行したときの `Session.getActiveUser()` はスクリプト所有者なので、
 * 所有者が users.json に admin として登録されていれば運用は変わらない（README §5〜§6）。
 *
 * @param {string} operationLabel エラーメッセージに出す操作名
 * @return {{email:string, role:string, displayName:string}} 認証できた admin セッション
 */
function specWebAssertAdminSession_(operationLabel) {
  var label = operationLabel || 'この操作';
  var session = authenticateSession_();
  if (!session.ok) {
    throw new Error(label + ' は admin のみ実行できます: ' + session.message);
  }
  if (session.role !== SPEC_WEB_ROLES.ADMIN) {
    throw new Error(
      label + ' は admin のみ実行できます（' + session.email + ' の現在のロール: ' + session.role + '）'
    );
  }
  return session;
}

/** auth.role が requiredRole 以上のランクかどうか。 */
function hasRole_(auth, requiredRole) {
  if (!auth || !auth.role) return false;
  var rank = SPEC_WEB_ROLE_RANK[auth.role];
  var requiredRank = SPEC_WEB_ROLE_RANK[requiredRole];
  if (rank === undefined || requiredRank === undefined) return false;
  return rank >= requiredRank;
}
