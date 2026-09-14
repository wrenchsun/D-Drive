'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// マニュアル配信（2026-09-14 追加、docs/32_spec_web.md「マニュアル配信」節）の AC:
//  - Unity の「マニュアル」ボタンが開く `?page=manual&p=<ページ名>` を doGet が受け取り、
//    人向け SPA の初期画面を 'manual' + { p } に解決する（resolveInitialScreen_、src/Code.js）
//  - p が不正・未知でもトップ（Readme）へフォールバックする（例外にしない）
//  - manualGet API（src/Manual.js）はページ本文（html/manual/<page>.html、
//    Tools/SpecWeb/tools/build-manual.js が事前生成）を返す。フォールバックも同様

function usersFixture(list) {
  const items = {};
  for (const u of list) {
    items[u.email.toLowerCase()] = { id: u.email.toLowerCase(), email: u.email, displayName: u.displayName, role: u.role, revision: 1 };
  }
  return { 'users.json': JSON.stringify({ items }) };
}

function loggedInCtx(role) {
  return loadGas({
    activeUserEmail: 'member@example.com',
    driveFiles: usersFixture([{ email: 'member@example.com', displayName: 'メンバー', role: role || 'viewer' }])
  });
}

// vm（別実現域）が返すオブジェクトは Node 側の {} と prototype が異なるため、
// assert.deepEqual（node:assert/strict では deepStrictEqual の別名）は使わず
// 個々のフィールドを比較する（docs/32_spec_web.md「テストの流儀」・storage.test.js と同じ回避）。

test('resolveInitialScreen_: page=manual & 有効な p ならその画面を返す', () => {
  const ctx = loggedInCtx();
  const result = ctx.resolveInitialScreen_({ page: 'manual', p: 'asset-browser' });
  assert.equal(result.screen, 'manual');
  assert.equal(result.params.p, 'asset-browser');
});

test('resolveInitialScreen_: page=manual だが p が未知ならトップ（Readme）へフォールバックする', () => {
  const ctx = loggedInCtx();
  const result = ctx.resolveInitialScreen_({ page: 'manual', p: 'no-such-page' });
  assert.equal(result.screen, 'manual');
  assert.equal(result.params.p, ctx.SPEC_WEB_MANUAL_TOP_PAGE);
});

test('resolveInitialScreen_: page=manual だが p が無ければトップ（Readme）になる', () => {
  const ctx = loggedInCtx();
  const result = ctx.resolveInitialScreen_({ page: 'manual' });
  assert.equal(result.screen, 'manual');
  assert.equal(result.params.p, 'Readme');
});

test('resolveInitialScreen_: page パラメータが無ければ既定画面のまま（screen: null）', () => {
  const ctx = loggedInCtx();
  const withEmpty = ctx.resolveInitialScreen_({});
  assert.equal(withEmpty.screen, null);
  assert.equal(Object.keys(withEmpty.params).length, 0);
  const withUndefined = ctx.resolveInitialScreen_(undefined);
  assert.equal(withUndefined.screen, null);
  assert.equal(Object.keys(withUndefined.params).length, 0);
});

test('doGet: ?page=manual&p=... でも許可リスト済みユーザーなら例外にならず SPA テンプレートを返す', () => {
  const ctx = loggedInCtx();
  const output = ctx.doGet({ parameter: { page: 'manual', p: 'asset-browser' } });
  assert.equal(output.getContent(), '<!-- rendered:html/Index -->');
});

test('doGet: ?page=manual&p=... でも許可リスト外なら「メンバーのみ利用できます」ページ（従来どおり）', () => {
  const ctx = loadGas({ activeUserEmail: 'outsider@example.com' });
  const output = ctx.doGet({ parameter: { page: 'manual', p: 'asset-browser' } });
  assert.match(output.getContent(), /メンバーのみ利用できます/);
});

test('specWebUiCall: manualGet は viewer でも呼べ、有効なページ名を返す', () => {
  const ctx = loggedInCtx('viewer');
  const result = ctx.specWebUiCall('manualGet', { p: 'asset-browser' });
  assert.equal(result.ok, true);
  assert.equal(result.page, 'asset-browser');
  assert.match(result.html, /include:html\/manual\/asset-browser/);
});

test('specWebUiCall: manualGet は未知の p をトップ（Readme）にフォールバックする（例外にしない）', () => {
  const ctx = loggedInCtx('viewer');
  const result = ctx.specWebUiCall('manualGet', { p: 'no-such-page' });
  assert.equal(result.ok, true);
  assert.equal(result.page, 'Readme');
});

test('specWebUiCall: manualGet は p が無ければトップ（Readme）を返す', () => {
  const ctx = loggedInCtx('viewer');
  const result = ctx.specWebUiCall('manualGet', {});
  assert.equal(result.ok, true);
  assert.equal(result.page, 'Readme');
});

test('specWebUiCall: ログインしていない場合は manualGet も 401 で拒否される（他の API と同じ規約）', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.specWebUiCall('manualGet', { p: 'Readme' });
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

test('doPost: D-Drive の読み取りトークンでも manualGet を呼べる（状態を変更しないため）', () => {
  const ctx = loadGas();
  const token = ctx.issueApiToken('read');
  const output = ctx.doPost({ parameter: { api: '1', name: 'manualGet', token: token, p: 'Readme' } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
  assert.equal(body.page, 'Readme');
});

test('doPost: D-Drive の書き込みトークンは manualGet を呼べない（kind 許可リストの対象外、§5.2）', () => {
  const ctx = loadGas();
  const token = ctx.issueApiToken('write');
  const output = ctx.doPost({ parameter: { api: '1', name: 'manualGet', token: token, p: 'Readme' } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

test('SPEC_WEB_MANUAL_PAGE_NAMES（生成済み src/ManualPages.js）に Readme が含まれる', () => {
  const ctx = loggedInCtx();
  assert.ok(ctx.SPEC_WEB_MANUAL_PAGE_NAMES.indexOf('Readme') !== -1);
  assert.equal(ctx.SPEC_WEB_MANUAL_TOP_PAGE, 'Readme');
});
