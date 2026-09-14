'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');
const { loadGas } = require('./load-gas.js');

// O-13 AC: 発注リンクをコピーする機能。
// (docs/32_spec_web.md §10.8 / Tools/SpecWeb/html/OrderLinkLogic.html / src/Code.js の resolveInitialScreen_)

function loadLogic() {
  return loadHtmlScript('OrderLinkLogic').window.OrderLinkLogic;
}

test('buildOrderUrl: execUrl + ?page=order&id=<種別::識別子> を組み立てる（末尾スラッシュは除去）', () => {
  const logic = loadLogic();
  assert.equal(
    logic.buildOrderUrl('https://script.google.com/macros/s/abc/exec', 'Se::Slash'),
    'https://script.google.com/macros/s/abc/exec?page=order&id=Se%3A%3ASlash'
  );
  assert.equal(
    logic.buildOrderUrl('https://script.google.com/macros/s/abc/exec/', 'Se::Slash'),
    'https://script.google.com/macros/s/abc/exec?page=order&id=Se%3A%3ASlash'
  );
});

test('buildOrderUrl/buildGroupUrl: execUrl か id が空なら空文字（呼び出し側が「取得できません」を表示する）', () => {
  const logic = loadLogic();
  assert.equal(logic.buildOrderUrl('', 'Se::Slash'), '');
  assert.equal(logic.buildOrderUrl('https://example.com/exec', ''), '');
  assert.equal(logic.buildGroupUrl('', 'og_1'), '');
});

test('buildGroupUrl: execUrl + ?page=group&id=<id> を組み立てる', () => {
  const logic = loadLogic();
  assert.equal(
    logic.buildGroupUrl('https://script.google.com/macros/s/abc/exec', 'og_1'),
    'https://script.google.com/macros/s/abc/exec?page=group&id=og_1'
  );
});

test('formatLinkText: url のみ（既定）/ 名前付き / Markdown の3形式を組み立てる', () => {
  const logic = loadLogic();
  const url = 'https://example.com/exec?page=order&id=Se%3A%3ASlash';
  assert.equal(logic.formatLinkText('url', '斬撃音', url), url);
  assert.equal(logic.formatLinkText(undefined, '斬撃音', url), url); // 既定は url
  assert.equal(logic.formatLinkText('named', '斬撃音', url), '斬撃音 - ' + url);
  assert.equal(logic.formatLinkText('markdown', '斬撃音', url), '[斬撃音](' + url + ')');
});

test('formatLinkText: url が空なら常に空文字', () => {
  const logic = loadLogic();
  assert.equal(logic.formatLinkText('url', '斬撃音', ''), '');
  assert.equal(logic.formatLinkText('markdown', '斬撃音', ''), '');
});

test('formatLinkText: markdown で label が無ければ url をラベルに使う', () => {
  const logic = loadLogic();
  const url = 'https://example.com/exec?page=order&id=X';
  assert.equal(logic.formatLinkText('markdown', '', url), '[' + url + '](' + url + ')');
});

// ---- resolveInitialScreen_（src/Code.js）の page=order / page=group 追加分 ----

test('resolveInitialScreen_: page=order & id があれば assets 画面を openId 付きで返す', () => {
  const ctx = loadGas();
  const result = ctx.resolveInitialScreen_({ page: 'order', id: 'Se::Slash' });
  assert.equal(result.screen, 'assets');
  // vm コンテキスト（別の実現域）で作られたオブジェクトのため deepEqual はプロトタイプ不一致で
  // 失敗する（assets.api.test.js 既存の注意点と同じ）。フィールドを個別に比較する。
  assert.equal(result.params.openId, 'Se::Slash');
  assert.equal(Object.keys(result.params).length, 1);
});

test('resolveInitialScreen_: page=order だが id が無ければ既定画面のまま（screen: null）', () => {
  const ctx = loadGas();
  const result = ctx.resolveInitialScreen_({ page: 'order' });
  assert.equal(result.screen, null);
});

test('resolveInitialScreen_: page=group & id があれば orders 画面を openGroupId 付きで返す', () => {
  const ctx = loadGas();
  const result = ctx.resolveInitialScreen_({ page: 'group', id: 'og_1' });
  assert.equal(result.screen, 'orders');
  assert.equal(result.params.openGroupId, 'og_1');
  assert.equal(Object.keys(result.params).length, 1);
});

test('resolveInitialScreen_: page=group だが id が無ければ既定画面のまま（screen: null）', () => {
  const ctx = loadGas();
  const result = ctx.resolveInitialScreen_({ page: 'group' });
  assert.equal(result.screen, null);
});

test('resolveInitialScreen_: page=manual は従来どおり動作し続ける（回帰確認）', () => {
  const ctx = loadGas();
  const result = ctx.resolveInitialScreen_({ page: 'manual', p: 'Readme' });
  assert.equal(result.screen, 'manual');
});

test('specWebExecUrl_: ScriptApp.getService().getUrl() をそのまま返す', () => {
  const ctx = loadGas({ scriptExecUrl: 'https://script.google.com/macros/s/fake/exec' });
  assert.equal(ctx.specWebExecUrl_(), 'https://script.google.com/macros/s/fake/exec');
});

test('doGet: ?page=order&id=... でも許可リスト済みユーザーなら例外にならず SPA テンプレートを返す（回帰: ScriptApp 追加後も renderUi_ が動く）', () => {
  const usersFixture = { 'users.json': JSON.stringify({ items: { 'member@example.com': { id: 'member@example.com', email: 'member@example.com', displayName: 'メンバー', role: 'editor', revision: 1 } } }) };
  const ctx = loadGas({ activeUserEmail: 'member@example.com', driveFiles: usersFixture });
  const output = ctx.doGet({ parameter: { page: 'order', id: 'Se::Slash' } });
  assert.equal(output.getContent(), '<!-- rendered:html/Index -->');
});
