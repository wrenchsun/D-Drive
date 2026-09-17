'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// 2026-09-17 追補（docs/41 P1-3 の修正に追随）: `issueApiToken` 等の**公開名**の運用関数は
// admin セッション必須になった（`specWebAssertAdminSession_`）。ここでのトークン発行・移行・
// ユーザー登録は「テストの前提を組み立てる」ためのものなので、内部実装（末尾 `_`）を直接呼ぶ。
// 公開名の関数が admin セッション無しで必ず失敗することは test/globals.test.js が固定している。

// W-3 AC: 許可リスト外は拒否 / トークン無し・間違いは拒否 / ロールで書き込み可否
// (docs/32_spec_web.md §8 W-3, §2.3, §3.4 / Auth.js)

function usersFixture(list) {
  const items = {};
  for (const u of list) {
    items[u.email.toLowerCase()] = { id: u.email.toLowerCase(), email: u.email, displayName: u.displayName, role: u.role, revision: 1 };
  }
  return { 'users.json': JSON.stringify({ items }) };
}

test('authenticateSession_: 許可リストに載っている Google アカウントは role 付きで認証できる', () => {
  const ctx = loadGas({
    activeUserEmail: 'admin@example.com',
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }])
  });
  const result = ctx.authenticateSession_();
  assert.equal(result.ok, true);
  assert.equal(result.role, 'admin');
  assert.equal(result.email, 'admin@example.com');
});

test('authenticateSession_: 許可リスト外の Google アカウントは 403 で拒否される', () => {
  const ctx = loadGas({
    activeUserEmail: 'outsider@example.com',
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }])
  });
  const result = ctx.authenticateSession_();
  assert.equal(result.ok, false);
  assert.equal(result.status, 403);
});

// O-14 AC: 拒否理由が 403（許可リスト外）のときは、本人が管理者に伝えやすいよう email も返す。
test('authenticateSession_: 許可リスト外の Google アカウントは 403 に加えて email も返す（本人が管理者に伝える用）', () => {
  const ctx = loadGas({ activeUserEmail: 'outsider@example.com' });
  const result = ctx.authenticateSession_();
  assert.equal(result.ok, false);
  assert.equal(result.status, 403);
  assert.equal(result.email, 'outsider@example.com');
});

test('authenticateSession_: ログインしていない（getActiveUser がメール無し）場合は 401 で拒否される', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.authenticateSession_();
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

test('authenticateSession_: email の大文字小文字は区別しない', () => {
  const ctx = loadGas({
    activeUserEmail: 'Admin@Example.com',
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }])
  });
  const result = ctx.authenticateSession_();
  assert.equal(result.ok, true);
  assert.equal(result.role, 'admin');
});

test('authenticateRequest_: 有効な read トークンは viewer 相当で認証できる', () => {
  const ctx = loadGas();
  ctx.specWebIssueApiToken_('read'); // 実値はログにだけ出る想定なので、ここでは発行して一覧を読み直す
  const token = ctx.__fakes.properties.store.get('SPEC_WEB_TOKENS_READ');
  const readToken = JSON.parse(token)[0];

  const result = ctx.authenticateRequest_({ parameter: { api: '1', token: readToken } });
  assert.equal(result.ok, true);
  assert.equal(result.tokenKind, 'read');
  assert.equal(result.role, 'viewer');
});

test('authenticateRequest_: 有効な write トークンは editor 相当で認証できる', () => {
  const ctx = loadGas();
  const writeToken = ctx.specWebIssueApiToken_('write');
  const result = ctx.authenticateRequest_({ parameter: { api: '1', token: writeToken } });
  assert.equal(result.ok, true);
  assert.equal(result.tokenKind, 'write');
  assert.equal(result.role, 'editor');
});

test('authenticateRequest_: 間違ったトークンは 401 で拒否される', () => {
  const ctx = loadGas();
  ctx.specWebIssueApiToken_('write');
  const result = ctx.authenticateRequest_({ parameter: { api: '1', token: 'not-the-right-token' } });
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

test('authenticateRequest_: トークン無し・かつログインもしていない（②への無認証アクセス）は拒否される', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.authenticateRequest_({ parameter: { api: '1' } });
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

// 2026-09-14 追補（オーケストレーター指示）: api=1 経路でトークン無し/間違いのときの文言を、
// 人向けの「ログインが必要です」から D-Drive 向けの案内に分ける。

test('authenticateRequest_: api=1 でトークン無し・未ログインは「API トークンがありません」の案内文になる', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.authenticateRequest_({ parameter: { api: '1' } });
  assert.equal(result.status, 401);
  assert.match(result.message, /API トークンがありません/);
  assert.match(result.message, /仕様書と同期/);
});

test('authenticateRequest_: api=1 でトークンが間違っていると「API トークンが正しくありません」の案内文になる', () => {
  const ctx = loadGas();
  ctx.specWebIssueApiToken_('write');
  const result = ctx.authenticateRequest_({ parameter: { api: '1', token: 'not-the-right-token' } });
  assert.equal(result.status, 401);
  assert.match(result.message, /API トークンが正しくありません/);
  assert.match(result.message, /仕様書と同期/);
});

// 2026-09-17 更新（docs/41 P1-4 / docs/32 §2.3.1(2)）: `?api=1` はセッションを一切見なくなったため、
// 許可リスト外かどうかに関わらず「トークンが無い」= 401 になる。人向けの 403「メンバーのみ
// 利用できます」は ① の画面（renderUi_ / specWebUiCall = authenticateSession_）側の文言として残る。
test('authenticateRequest_: 許可リスト外の Google ログインでも（セッションを見ないため）401 になる', () => {
  const ctx = loadGas({ activeUserEmail: 'outsider@example.com' });
  const result = ctx.authenticateRequest_({ parameter: { api: '1' } });
  assert.equal(result.status, 401);
  assert.match(result.message, /API トークンがありません/);
});

test('authenticateSession_: 許可リスト外の Google ログインには人向けメッセージ（403）を返す', () => {
  const ctx = loadGas({ activeUserEmail: 'outsider@example.com' });
  const session = ctx.authenticateSession_();
  assert.equal(session.ok, false);
  assert.equal(session.status, 403);
  assert.match(session.message, /メンバーのみ利用できます/);
});

// 2026-09-17 更新（docs/41 P1-4、CSRF 対策の仕様変更）: 以前はここで「トークン無しでも
// 許可リストに載った Google ログインならセッション経路で認証できる」ことを固定していたが、
// `.../exec?api=1&name=users.upsert&…` のリンクを admin に踏ませるだけで admin 権限の API が
// 走る CSRF になっていたため、セッションフォールバックは廃止した（docs/32 §2.3.1(2)）。
test('authenticateRequest_: 許可リストに載った Google ログインでもトークン無しの ?api=1 は 401（フォールバック廃止）', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const result = ctx.authenticateRequest_({ parameter: { api: '1' } });
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
  assert.match(result.message, /API トークンがありません/);
  // 人向けの経路（① の画面）は従来どおりセッションで認証できる。
  const session = ctx.authenticateSession_();
  assert.equal(session.ok, true);
  assert.equal(session.role, 'editor');
});

test('ローテーション後は新旧トークンが一時的に両方有効で、失効させた旧トークンだけ拒否される', () => {
  const ctx = loadGas();
  const oldToken = ctx.specWebIssueApiToken_('write');
  const newToken = ctx.specWebIssueApiToken_('write');

  assert.equal(ctx.authenticateRequest_({ parameter: { api: '1', token: oldToken } }).ok, true);
  assert.equal(ctx.authenticateRequest_({ parameter: { api: '1', token: newToken } }).ok, true);

  const removed = ctx.specWebRevokeApiToken_('write', oldToken);
  assert.equal(removed, true);

  assert.equal(ctx.authenticateRequest_({ parameter: { api: '1', token: oldToken } }).ok, false);
  assert.equal(ctx.authenticateRequest_({ parameter: { api: '1', token: newToken } }).ok, true);
});

test('revokeAllApiTokens: 漏洩時の緊急失効で全トークンが即時に無効化される', () => {
  const ctx = loadGas();
  const t1 = ctx.specWebIssueApiToken_('read');
  const t2 = ctx.specWebIssueApiToken_('read');
  ctx.specWebRevokeAllApiTokens_('read');
  assert.equal(ctx.authenticateRequest_({ parameter: { api: '1', token: t1 } }).ok, false);
  assert.equal(ctx.authenticateRequest_({ parameter: { api: '1', token: t2 } }).ok, false);
});

test('hasRole_: ロールの階層（viewer < editor < admin）で書き込み可否を判定できる', () => {
  const ctx = loadGas();
  const viewer = { role: 'viewer' };
  const editor = { role: 'editor' };
  const admin = { role: 'admin' };

  assert.equal(ctx.hasRole_(viewer, 'editor'), false);
  assert.equal(ctx.hasRole_(editor, 'editor'), true);
  assert.equal(ctx.hasRole_(editor, 'admin'), false);
  assert.equal(ctx.hasRole_(admin, 'admin'), true);
  assert.equal(ctx.hasRole_(admin, 'editor'), true);
  assert.equal(ctx.hasRole_(null, 'viewer'), false);
});

test('upsertSpecWebUser / removeSpecWebUser: 許可リストの追加・削除ができる（admin がエディタから手動実行する想定）', () => {
  const ctx = loadGas({ activeUserEmail: 'new@example.com' });
  assert.equal(ctx.authenticateSession_().ok, false);

  ctx.specWebUpsertUser_('new@example.com', '新メンバー', 'viewer');
  assert.equal(ctx.authenticateSession_().ok, true);

  ctx.specWebRemoveUser_('new@example.com');
  assert.equal(ctx.authenticateSession_().ok, false);
});
