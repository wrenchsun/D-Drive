'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// O-2 AC: Presentation 発注グループ CRUD + 発注ツリー（docs/32_spec_web.md §10.2.3・§10.3.1）。

function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}
function viewerAuth() {
  return { ok: true, principal: 'viewer@example.com', email: 'viewer@example.com', role: 'viewer', displayName: '閲覧者' };
}
function call(ctx, name, params, auth) {
  return ctx.getApi(name)({ params: params || {}, auth: auth || editorAuth() });
}

test('orderGroups.create: name は必須。作成すると id が発行され revision=1', () => {
  const ctx = loadGas();
  const result = call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'スキル: 斬撃', wbsNo: '3.2.1' }) });
  assert.match(result.item.id, /^og_/);
  assert.equal(result.item.name, 'スキル: 斬撃');
  assert.equal(result.item.wbsNo, '3.2.1');
  assert.equal(result.item.revision, 1);
  assert.equal(result.item.comments.length, 0);
});

test('orderGroups.create: name が無ければ 400、viewer は 403', () => {
  const ctx = loadGas();
  assert.throws(() => call(ctx, 'orderGroups.create', { patch: JSON.stringify({}) }), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
  assert.throws(() => call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'x' }) }, viewerAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
});

test('orderGroups.update: revision 楽観ロックで更新できる', () => {
  const ctx = loadGas();
  const created = call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'A' }) }).item;
  const updated = call(ctx, 'orderGroups.update', {
    id: created.id, expectedRevision: created.revision, patch: JSON.stringify({ wbsNo: '1.1' })
  }).item;
  assert.equal(updated.wbsNo, '1.1');
  assert.equal(updated.revision, 2);
});

test('orderGroups.list / orderGroups.get: 一覧・取得ができる', () => {
  const ctx = loadGas();
  call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'A' }) });
  call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'B' }) });
  assert.equal(call(ctx, 'orderGroups.list', {}).items.length, 2);

  assert.throws(() => call(ctx, 'orderGroups.get', { id: 'og_notexist' }), (err) => {
    assert.equal(err.status, 404);
    return true;
  });
});

test('orderGroups.delete: 子（parentId で参照する未削除の発注）が残っていると 400 で拒否される', () => {
  const ctx = loadGas();
  const group = call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'A' }) }).item;
  call(ctx, 'assets.create', {
    patch: JSON.stringify({ assetType: 'Se', identifier: 'Hit', displayName: '斬撃音', parentId: group.id })
  });
  assert.throws(() => call(ctx, 'orderGroups.delete', { id: group.id, expectedRevision: group.revision }), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
});

test('orderGroups.delete: 子が無ければ削除できる', () => {
  const ctx = loadGas();
  const group = call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'A' }) }).item;
  const result = call(ctx, 'orderGroups.delete', { id: group.id, expectedRevision: group.revision });
  assert.equal(result.deleted, true);
  assert.throws(() => call(ctx, 'orderGroups.get', { id: group.id }), (err) => {
    assert.equal(err.status, 404);
    return true;
  });
});

test('orderGroups.comments.add: コメントを追記できる', () => {
  const ctx = loadGas();
  const group = call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'A' }) }).item;
  const result = call(ctx, 'orderGroups.comments.add', { id: group.id, body: '概要を書いた' });
  assert.equal(result.item.comments.length, 1);
  assert.equal(result.item.comments[0].body, '概要を書いた');
});
