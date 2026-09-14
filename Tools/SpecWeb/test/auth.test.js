'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// W-3 AC: 許可リスト外は拒否 / トークン無し・間違いは拒否 / ロールで書き込み可否
// (docs/32_spec_web.md §8 W-3, §2.3, §3.4 / Auth.js)

function usersFixture(list) {
  const items = {};
  for (const u of list) {
    items[u.email.toLowerCase()] = { id: u.email.toLowerCase(), email: u.email, displayName: u.displayName, role: u.role, revision: 1 };
  }
  return { 'users.json': JSON.stringify({ items }) };
}

test('authenticateSession: 許可リストに載っている Google アカウントは role 付きで認証できる', () => {
  const ctx = loadGas({
    activeUserEmail: 'admin@example.com',
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }])
  });
  const result = ctx.authenticateSession();
  assert.equal(result.ok, true);
  assert.equal(result.role, 'admin');
  assert.equal(result.email, 'admin@example.com');
});

test('authenticateSession: 許可リスト外の Google アカウントは 403 で拒否される', () => {
  const ctx = loadGas({
    activeUserEmail: 'outsider@example.com',
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }])
  });
  const result = ctx.authenticateSession();
  assert.equal(result.ok, false);
  assert.equal(result.status, 403);
});

test('authenticateSession: ログインしていない（getActiveUser がメール無し）場合は 401 で拒否される', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.authenticateSession();
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

test('authenticateSession: email の大文字小文字は区別しない', () => {
  const ctx = loadGas({
    activeUserEmail: 'Admin@Example.com',
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }])
  });
  const result = ctx.authenticateSession();
  assert.equal(result.ok, true);
  assert.equal(result.role, 'admin');
});

test('authenticateRequest: 有効な read トークンは viewer 相当で認証できる', () => {
  const ctx = loadGas();
  ctx.issueApiToken('read'); // 実値はログにだけ出る想定なので、ここでは発行して一覧を読み直す
  const token = ctx.__fakes.properties.store.get('SPEC_WEB_TOKENS_READ');
  const readToken = JSON.parse(token)[0];

  const result = ctx.authenticateRequest({ parameter: { api: '1', token: readToken } });
  assert.equal(result.ok, true);
  assert.equal(result.tokenKind, 'read');
  assert.equal(result.role, 'viewer');
});

test('authenticateRequest: 有効な write トークンは editor 相当で認証できる', () => {
  const ctx = loadGas();
  const writeToken = ctx.issueApiToken('write');
  const result = ctx.authenticateRequest({ parameter: { api: '1', token: writeToken } });
  assert.equal(result.ok, true);
  assert.equal(result.tokenKind, 'write');
  assert.equal(result.role, 'editor');
});

test('authenticateRequest: 間違ったトークンは 401 で拒否される', () => {
  const ctx = loadGas();
  ctx.issueApiToken('write');
  const result = ctx.authenticateRequest({ parameter: { api: '1', token: 'not-the-right-token' } });
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

test('authenticateRequest: トークン無し・かつログインもしていない（②への無認証アクセス）は拒否される', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.authenticateRequest({ parameter: { api: '1' } });
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

// 2026-09-14 追補（オーケストレーター指示）: api=1 経路でトークン無し/間違いのときの文言を、
// 人向けの「ログインが必要です」から D-Drive 向けの案内に分ける。

test('authenticateRequest: api=1 でトークン無し・未ログインは「API トークンがありません」の案内文になる', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.authenticateRequest({ parameter: { api: '1' } });
  assert.equal(result.status, 401);
  assert.match(result.message, /API トークンがありません/);
  assert.match(result.message, /仕様書と同期/);
});

test('authenticateRequest: api=1 でトークンが間違っていると「API トークンが正しくありません」の案内文になる', () => {
  const ctx = loadGas();
  ctx.issueApiToken('write');
  const result = ctx.authenticateRequest({ parameter: { api: '1', token: 'not-the-right-token' } });
  assert.equal(result.status, 401);
  assert.match(result.message, /API トークンが正しくありません/);
  assert.match(result.message, /仕様書と同期/);
});

test('authenticateRequest: 許可リスト外の Google ログイン（403）は人向けメッセージのまま変わらない', () => {
  const ctx = loadGas({ activeUserEmail: 'outsider@example.com' });
  const result = ctx.authenticateRequest({ parameter: { api: '1' } });
  assert.equal(result.status, 403);
  assert.match(result.message, /メンバーのみ利用できます/);
});

test('authenticateRequest: トークン無しでも許可リストに載った Google ログインならセッション経路で認証できる', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const result = ctx.authenticateRequest({ parameter: { api: '1' } });
  assert.equal(result.ok, true);
  assert.equal(result.role, 'editor');
  assert.equal(result.email, 'editor@example.com');
});

test('ローテーション後は新旧トークンが一時的に両方有効で、失効させた旧トークンだけ拒否される', () => {
  const ctx = loadGas();
  const oldToken = ctx.issueApiToken('write');
  const newToken = ctx.rotateApiToken('write');

  assert.equal(ctx.authenticateRequest({ parameter: { api: '1', token: oldToken } }).ok, true);
  assert.equal(ctx.authenticateRequest({ parameter: { api: '1', token: newToken } }).ok, true);

  const removed = ctx.revokeApiToken('write', oldToken);
  assert.equal(removed, true);

  assert.equal(ctx.authenticateRequest({ parameter: { api: '1', token: oldToken } }).ok, false);
  assert.equal(ctx.authenticateRequest({ parameter: { api: '1', token: newToken } }).ok, true);
});

test('revokeAllApiTokens: 漏洩時の緊急失効で全トークンが即時に無効化される', () => {
  const ctx = loadGas();
  const t1 = ctx.issueApiToken('read');
  const t2 = ctx.issueApiToken('read');
  ctx.revokeAllApiTokens('read');
  assert.equal(ctx.authenticateRequest({ parameter: { api: '1', token: t1 } }).ok, false);
  assert.equal(ctx.authenticateRequest({ parameter: { api: '1', token: t2 } }).ok, false);
});

test('hasRole: ロールの階層（viewer < editor < admin）で書き込み可否を判定できる', () => {
  const ctx = loadGas();
  const viewer = { role: 'viewer' };
  const editor = { role: 'editor' };
  const admin = { role: 'admin' };

  assert.equal(ctx.hasRole(viewer, 'editor'), false);
  assert.equal(ctx.hasRole(editor, 'editor'), true);
  assert.equal(ctx.hasRole(editor, 'admin'), false);
  assert.equal(ctx.hasRole(admin, 'admin'), true);
  assert.equal(ctx.hasRole(admin, 'editor'), true);
  assert.equal(ctx.hasRole(null, 'viewer'), false);
});

test('upsertSpecWebUser / removeSpecWebUser: 許可リストの追加・削除ができる（admin がエディタから手動実行する想定）', () => {
  const ctx = loadGas({ activeUserEmail: 'new@example.com' });
  assert.equal(ctx.authenticateSession().ok, false);

  ctx.upsertSpecWebUser('new@example.com', '新メンバー', 'viewer');
  assert.equal(ctx.authenticateSession().ok, true);

  ctx.removeSpecWebUser('new@example.com');
  assert.equal(ctx.authenticateSession().ok, false);
});
