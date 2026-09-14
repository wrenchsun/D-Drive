/**
 * users.json 許可リスト（docs/32_spec_web.md §3.4）の管理。
 *
 * Web API 経由では呼び出せない（TokenAdmin.js と同様、ApiRegistry に未登録）。
 * 管理者が Apps Script エディタから直接実行することを想定した関数群。
 * 管理画面（Web UI からの CRUD）は後続チケットで追加する。
 * これが無いと誰もログインできず①のブートストラップができないため、
 * 最初の admin 登録にはこの関数を使う。
 */

/**
 * ユーザーを追加・更新する。
 * @param {string} email
 * @param {string} displayName
 * @param {('viewer'|'editor'|'admin')} role
 */
function upsertSpecWebUser(email, displayName, role) {
  if (!email) throw new Error('email は必須です');
  if (SPEC_WEB_ROLE_RANK[role] === undefined) {
    throw new Error('role は viewer/editor/admin のいずれかです: ' + role);
  }
  return Storage.putItem(
    SPEC_WEB_USERS_COLLECTION,
    specWebNormalizeEmail_(email),
    { email: email, displayName: displayName || email, role: role },
    { actor: 'admin-console' }
  );
}

function removeSpecWebUser(email) {
  if (!email) throw new Error('email は必須です');
  return Storage.deleteItem(SPEC_WEB_USERS_COLLECTION, specWebNormalizeEmail_(email), {});
}

function listSpecWebUsers() {
  return Storage.listItems(SPEC_WEB_USERS_COLLECTION);
}
