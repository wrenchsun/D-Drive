'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// 2026-09-14 追補 AC（実デプロイで判明した誤りの修正、docs/32_spec_web.md §2.4 訂正）:
// 人向け SPA は google.script.run 経由で specWebUiCall(name, params) を呼ぶ。
// トークンは使わず、常に Google ログイン + users.json 許可リストで認証する。

function usersFixture(list) {
  const items = {};
  for (const u of list) {
    items[u.email.toLowerCase()] = { id: u.email.toLowerCase(), email: u.email, displayName: u.displayName, role: u.role, revision: 1 };
  }
  return { 'users.json': JSON.stringify({ items }) };
}

test('specWebUiCall: 許可リストに載っている editor は登録済み API を呼べる（ping）', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const result = ctx.specWebUiCall('ping', {});
  assert.equal(result.ok, true);
  assert.equal(result.pong, true);
});

test('specWebUiCall: 許可リストに載っていない（ロールが無い）ユーザーは拒否される（403）', () => {
  const ctx = loadGas({ activeUserEmail: 'outsider@example.com' });
  const result = ctx.specWebUiCall('ping', {});
  assert.equal(result.ok, false);
  assert.equal(result.status, 403);
});

test('specWebUiCall: ログインしていない場合は 401 で拒否される', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.specWebUiCall('ping', {});
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

test('specWebUiCall: 未登録の API 名は 404 相当を返す', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const result = ctx.specWebUiCall('no-such-api', {});
  assert.equal(result.ok, false);
  assert.equal(result.status, 404);
});

test('specWebUiCall: viewer が assets.create を呼ぶと 403（各 API 自身の role チェックが働く）', () => {
  const ctx = loadGas({
    activeUserEmail: 'viewer@example.com',
    driveFiles: usersFixture([{ email: 'viewer@example.com', displayName: '閲覧者', role: 'viewer' }])
  });
  const result = ctx.specWebUiCall('assets.create', { patch: JSON.stringify({ assetType: 'Se', identifier: 'Slash', displayName: '斬撃音' }) });
  assert.equal(result.ok, false);
  assert.equal(result.status, 403);
});

test('specWebUiCall: editor が assets.create → assets.get できる（実データの往復確認）', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const created = ctx.specWebUiCall('assets.create', { patch: JSON.stringify({ assetType: 'Se', identifier: 'Slash', displayName: '斬撃音' }) });
  assert.equal(created.ok, true);
  assert.equal(created.item.id, 'Se::Slash');

  const fetched = ctx.specWebUiCall('assets.get', { id: 'Se::Slash' });
  assert.equal(fetched.ok, true);
  assert.equal(fetched.item.displayName, '斬撃音');
});

test('specWebUiCall: RevisionConflictError は 409 + currentRevision 付きで返る', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const created = ctx.specWebUiCall('assets.create', { patch: JSON.stringify({ assetType: 'Se', identifier: 'Slash', displayName: '斬撃音' }) });
  ctx.specWebUiCall('assets.update', { id: created.item.id, expectedRevision: created.item.revision, patch: JSON.stringify({ status: '納品済' }) });

  const conflict = ctx.specWebUiCall('assets.update', {
    id: created.item.id, expectedRevision: created.item.revision, patch: JSON.stringify({ contractor: 'x' })
  });
  assert.equal(conflict.ok, false);
  assert.equal(conflict.status, 409);
  assert.equal(conflict.currentRevision, 2);
});

test('specWebUiCall: D-Drive の書き込みトークン kind 許可リストの影響を受けない（token を使わないため）', () => {
  const ctx = loadGas({
    activeUserEmail: 'admin@example.com',
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }])
  });
  // tuningScalarUpdate は書き込みトークンの kind 許可リストには入っていないが、
  // specWebUiCall は token を使わないためゲートの対象外（存在しないキーなので 404 になる）。
  const result = ctx.specWebUiCall('tuningScalarUpdate', { key: 'No/Such', revision: 1, payload: JSON.stringify({ key: 'No/Such', revision: 1 }) });
  assert.equal(result.status, 404);
});
