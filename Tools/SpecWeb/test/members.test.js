'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// O-9 AC: メンバー管理（ガント担当者マスタの貼り付け取り込み）。docs/32_spec_web.md §10.5①。

function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}
function viewerAuth() {
  return { ok: true, principal: 'viewer@example.com', email: 'viewer@example.com', role: 'viewer', displayName: '閲覧者' };
}
function call(ctx, name, params, auth) {
  return ctx.getApi(name)({ params: params || {}, auth: auth || editorAuth() });
}

test('members.importPaste: 1行1名で貼り付けて取り込める（表記はそのまま・空行は無視・重複行は1件）', () => {
  const ctx = loadGas();
  const text = '吉田(PLN)\n山口(PRG)\n\n吉田(PLN)\n岸本(DZN)\n';
  const result = call(ctx, 'members.importPaste', { text: text });
  assert.equal(result.importedCount, 3);

  const list = call(ctx, 'members.list', {}).items;
  const labels = Array.from(list, (m) => m.label).sort();
  assert.deepEqual(labels, ['吉田(PLN)', '山口(PRG)', '岸本(DZN)'].sort());
  assert.ok(list.every((m) => m.source === 'gantt'));
});

test('members.importPaste: CRLF も改行として扱う', () => {
  const ctx = loadGas();
  const result = call(ctx, 'members.importPaste', { text: '吉田(PLN)\r\n山口(PRG)\r\n' });
  assert.equal(result.importedCount, 2);
});

test('members.importPaste: 空テキストは 400、viewer は 403', () => {
  const ctx = loadGas();
  assert.throws(() => call(ctx, 'members.importPaste', { text: '  \n  ' }), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
  assert.throws(() => call(ctx, 'members.importPaste', { text: 'x' }, viewerAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
});

test('members.importPaste: 再取り込みしても既存の email 対応付けは保持される', () => {
  const ctx = loadGas();
  call(ctx, 'members.importPaste', { text: '吉田(PLN)' });
  call(ctx, 'members.upsert', { label: '吉田(PLN)', email: 'yoshida@example.com' });
  call(ctx, 'members.importPaste', { text: '吉田(PLN)\n山口(PRG)' });

  const list = call(ctx, 'members.list', {}).items;
  const yoshida = list.find((m) => m.label === '吉田(PLN)');
  assert.equal(yoshida.email, 'yoshida@example.com');
});

test('members.upsert: 手動追加（社外メンバー等）は source=manual になる', () => {
  const ctx = loadGas();
  const result = call(ctx, 'members.upsert', { label: 'ゲスト外注(社外)', email: 'guest@example.com' });
  assert.equal(result.item.source, 'manual');
  assert.equal(result.item.email, 'guest@example.com');
});

test('members.remove: 削除できる', () => {
  const ctx = loadGas();
  call(ctx, 'members.importPaste', { text: '吉田(PLN)' });
  const result = call(ctx, 'members.remove', { label: '吉田(PLN)' });
  assert.equal(result.removed, true);
  assert.equal(call(ctx, 'members.list', {}).items.length, 0);
});

test('members.upsert / members.remove: viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(() => call(ctx, 'members.upsert', { label: 'x' }, viewerAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
  assert.throws(() => call(ctx, 'members.remove', { label: 'x' }, viewerAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
});
