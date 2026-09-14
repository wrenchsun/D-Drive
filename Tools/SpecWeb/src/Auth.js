/**
 * 認証・認可。docs/32_spec_web.md §2.3（アクセス制御）・§3.4（ロール）・§7（セキュリティ）を実装したもの。
 *
 * ① 人向け SPA（デプロイ①、実行者=アクセスした人）:
 *    Session.getActiveUser().getEmail() を users.json の許可リストと照合する。
 * ② D-Drive API（デプロイ②、実行者=Me、ログイン不要）:
 *    リクエストの `token` パラメータをスクリプトプロパティ上のトークンと比較する
 *    （読み取り用・書き込み用の 2 種類）。
 *
 * doGet/doPost（Code.js）は `authenticateRequest(e)` だけを呼べばよい。
 * token パラメータがあれば②、無ければ①として自動的に振り分ける
 * （docs/32_spec_web.md §2.3「リクエストに token パラメータがあれば②の経路…」）。
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
function authenticateSession() {
  var email = SessionAdapter.getActiveUserEmail();
  if (!email) {
    return { ok: false, status: 401, message: 'ログインが必要です（Google アカウントでアクセスしてください）' };
  }
  var user = findSpecWebUserByEmail_(email);
  if (!user) {
    return { ok: false, status: 403, message: 'メンバーのみ利用できます。管理者に users.json への追加を依頼してください。' };
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
 * リクエスト全体（① Google ログイン or ② API トークン）を認証する。
 * doGet/doPost から呼ばれる唯一の入口（Code.js）。
 * @return {{ok:boolean, status?:number, message?:string, principal?:string,
 *           role?:string, email?:string, displayName?:string, tokenKind?:string}}
 */
function authenticateRequest(e) {
  var params = (e && e.parameter) || {};
  if (params.token) {
    var tokenInfo = verifyApiToken_(params.token);
    if (!tokenInfo) {
      return { ok: false, status: 401, message: 'トークンが無効です' };
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
  var session = authenticateSession();
  if (!session.ok) return session;
  return {
    ok: true,
    principal: session.email,
    email: session.email,
    role: session.role,
    displayName: session.displayName
  };
}

/** auth.role が requiredRole 以上のランクかどうか。 */
function hasRole(auth, requiredRole) {
  if (!auth || !auth.role) return false;
  var rank = SPEC_WEB_ROLE_RANK[auth.role];
  var requiredRank = SPEC_WEB_ROLE_RANK[requiredRole];
  if (rank === undefined || requiredRank === undefined) return false;
  return rank >= requiredRank;
}
