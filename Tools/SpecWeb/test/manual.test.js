'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// 2026-09-17 追補（docs/41 P1-3 の修正に追随）: `issueApiToken` 等の**公開名**の運用関数は
// admin セッション必須になった（`specWebAssertAdminSession_`）。ここでのトークン発行・移行・
// ユーザー登録は「テストの前提を組み立てる」ためのものなので、内部実装（末尾 `_`）を直接呼ぶ。
// 公開名の関数が admin セッション無しで必ず失敗することは test/globals.test.js が固定している。

// マニュアル配信（2026-09-14 追加、docs/32_spec_web.md「マニュアル配信」節）の AC:
//  - Unity の「マニュアル」ボタンが開く `?page=manual&p=<ページ名>` を doGet が受け取り、
//    人向け SPA の初期画面を 'manual' + { p } に解決する（resolveInitialScreen_、src/Code.js）
//  - p が不正・未知でもトップ（Readme）へフォールバックする（例外にしない）
//  - manualGet API（src/Manual.js）はページ本文（html/manual/<kind>/<page>.html、
//    Tools/SpecWeb/tools/build-manual.js が事前生成）を返す。フォールバックも同様
//
// 2026-09-17（プログラマーマニュアル配信対応）: `kind`（"designer"/"programmer"）が
// URL・manualGet の両方に追加された。kind 未指定・不正は SPEC_WEB_MANUAL_DEFAULT_KIND
// （"designer"）にフォールバックする（Unity の既存 URL・既存のコピー済みリンクとの後方互換）。

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

test('specWebUiCall: manualGet は viewer でも呼べ、有効なページ名を返す（kind 未指定は designer）', () => {
  const ctx = loggedInCtx('viewer');
  const result = ctx.specWebUiCall('manualGet', { p: 'asset-browser' });
  assert.equal(result.ok, true);
  assert.equal(result.page, 'asset-browser');
  assert.equal(result.kind, 'designer');
  assert.match(result.html, /include:html\/manual\/designer\/asset-browser/);
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

// ── 2026-09-17（プログラマーマニュアル配信対応） ──

test('specWebUiCall: manualGet に kind:"programmer" を渡すとプログラマーマニュアルのページを返す', () => {
  const ctx = loggedInCtx('viewer');
  const result = ctx.specWebUiCall('manualGet', { p: 'concepts', kind: 'programmer' });
  assert.equal(result.ok, true);
  assert.equal(result.page, 'concepts');
  assert.equal(result.kind, 'programmer');
  assert.match(result.html, /include:html\/manual\/programmer\/concepts/);
});

test('specWebUiCall: manualGet は kind が不正なら designer にフォールバックする（例外にしない）', () => {
  const ctx = loggedInCtx('viewer');
  const result = ctx.specWebUiCall('manualGet', { p: 'Readme', kind: 'no-such-kind' });
  assert.equal(result.ok, true);
  assert.equal(result.kind, 'designer');
});

test('specWebUiCall: manualGet は kind が programmer で p がそのマニュアルに無ければトップ（Readme）にフォールバックする', () => {
  const ctx = loggedInCtx('viewer');
  // "asset-browser" はデザイナーマニュアル側のページ名。programmer 側には存在しない。
  const result = ctx.specWebUiCall('manualGet', { p: 'asset-browser', kind: 'programmer' });
  assert.equal(result.ok, true);
  assert.equal(result.kind, 'programmer');
  assert.equal(result.page, 'Readme');
});

test('resolveInitialScreen_: page=manual & kind=programmer なら params.kind に反映される', () => {
  const ctx = loggedInCtx();
  const result = ctx.resolveInitialScreen_({ page: 'manual', p: 'concepts', kind: 'programmer' });
  assert.equal(result.screen, 'manual');
  assert.equal(result.params.p, 'concepts');
  assert.equal(result.params.kind, 'programmer');
});

test('resolveInitialScreen_: page=manual & kind 未指定は designer になる（後方互換）', () => {
  const ctx = loggedInCtx();
  const result = ctx.resolveInitialScreen_({ page: 'manual', p: 'Readme' });
  assert.equal(result.params.kind, 'designer');
});

test('specWebUiCall: ログインしていない場合は manualGet も 401 で拒否される（他の API と同じ規約）', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const result = ctx.specWebUiCall('manualGet', { p: 'Readme' });
  assert.equal(result.ok, false);
  assert.equal(result.status, 401);
});

test('doPost: D-Drive の読み取りトークンでも manualGet を呼べる（状態を変更しないため）', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('read');
  const output = ctx.doPost({ parameter: { api: '1', name: 'manualGet', token: token, p: 'Readme' } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
  assert.equal(body.page, 'Readme');
});

test('doPost: D-Drive の書き込みトークンは manualGet を呼べない（kind 許可リストの対象外、§5.2）', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('write');
  const output = ctx.doPost({ parameter: { api: '1', name: 'manualGet', token: token, p: 'Readme' } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

test('SPEC_WEB_MANUAL_PAGE_NAMES（生成済み src/ManualPages.js）は kind ごとに Readme を含む', () => {
  const ctx = loggedInCtx();
  // vm（別実現域）が返す配列は Node 側の Array と prototype が異なるため deepEqual は使わない
  // （このファイル冒頭の注意・storage.test.js と同じ回避）。
  assert.equal(ctx.SPEC_WEB_MANUAL_KINDS.length, 2);
  assert.equal(ctx.SPEC_WEB_MANUAL_KINDS.indexOf('designer') !== -1, true);
  assert.equal(ctx.SPEC_WEB_MANUAL_KINDS.indexOf('programmer') !== -1, true);
  assert.equal(ctx.SPEC_WEB_MANUAL_DEFAULT_KIND, 'designer');
  assert.equal(ctx.SPEC_WEB_MANUAL_TOP_PAGE, 'Readme');
  assert.ok(ctx.SPEC_WEB_MANUAL_PAGE_NAMES.designer.indexOf('Readme') !== -1);
  assert.ok(ctx.SPEC_WEB_MANUAL_PAGE_NAMES.programmer.indexOf('Readme') !== -1);
});
