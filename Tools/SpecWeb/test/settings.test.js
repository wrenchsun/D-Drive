'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// O-10 AC: ガント URL の設定値（docs/32_spec_web.md §10.5②）。
// URL・スプレッドシート ID は本書・コード・テストに書かない方針のため、ここでは常にダミーの
// https://example.com/... を使う。

function adminAuth() {
  return { ok: true, principal: 'admin@example.com', email: 'admin@example.com', role: 'admin', displayName: '管理者' };
}
function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}
function viewerAuth() {
  return { ok: true, principal: 'viewer@example.com', email: 'viewer@example.com', role: 'viewer', displayName: '閲覧者' };
}
function call(ctx, name, params, auth) {
  return ctx.getApi(name)({ params: params || {}, auth: auth || editorAuth() });
}

test('settings.get: 未設定なら空文字を返す', () => {
  const ctx = loadGas();
  assert.equal(call(ctx, 'settings.get', {}, viewerAuth()).ganttUrl, '');
});

test('settings.setGanttUrl: admin が設定でき、viewer でも読める', () => {
  const ctx = loadGas();
  const result = call(ctx, 'settings.setGanttUrl', { url: 'https://example.com/gantt-dummy' }, adminAuth());
  assert.equal(result.ganttUrl, 'https://example.com/gantt-dummy');

  const read = call(ctx, 'settings.get', {}, viewerAuth());
  assert.equal(read.ganttUrl, 'https://example.com/gantt-dummy');
});

test('settings.setGanttUrl: editor/viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(() => call(ctx, 'settings.setGanttUrl', { url: 'https://example.com/x' }, editorAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
  assert.throws(() => call(ctx, 'settings.setGanttUrl', { url: 'https://example.com/x' }, viewerAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
});

test('settings.setGanttUrl: http(s) で始まらない値は 400 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(() => call(ctx, 'settings.setGanttUrl', { url: 'javascript:alert(1)' }, adminAuth()), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
});

test('settings.setGanttUrl: 空文字を渡すとクリアされる', () => {
  const ctx = loadGas();
  call(ctx, 'settings.setGanttUrl', { url: 'https://example.com/gantt-dummy' }, adminAuth());
  call(ctx, 'settings.setGanttUrl', { url: '' }, adminAuth());
  assert.equal(call(ctx, 'settings.get', {}, viewerAuth()).ganttUrl, '');
});
