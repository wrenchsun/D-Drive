'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');
const { createFakeDom } = require('./dom-stub.js');

// O-13 AC: 「navigator.clipboard.writeText は iframe サンドボックスで拒否される可能性がある
// ので、失敗時は選択済みの読み取り専用テキスト欄を出して document.execCommand('copy') →
// それも失敗なら『Ctrl+C でコピーしてください』」の3段フォールバックを検証する。
// (Tools/SpecWeb/html/ClipboardCopy.html)

function setup(navigatorOverride, execCommandResult) {
  const dom = createFakeDom();
  if (execCommandResult !== undefined) {
    dom.document.execCommand = function () {
      return execCommandResult;
    };
  }
  const sandbox = { console, document: dom.document, navigator: navigatorOverride };
  const ctx = loadHtmlScript('ClipboardCopy', sandbox);
  return { ctx, dom };
}

test('copyText: navigator.clipboard.writeText が成功すれば true で resolve する', async () => {
  const { ctx } = setup({ clipboard: { writeText: function () { return Promise.resolve(); } } });
  const result = await ctx.window.SpecWebClipboard.copyText('hello');
  assert.equal(result, true);
});

test('copyText: navigator.clipboard が拒否されたら execCommand フォールバックに落ち、成功すれば true', async () => {
  const { ctx } = setup(
    { clipboard: { writeText: function () { return Promise.reject(new Error('denied')); } } },
    true
  );
  const result = await ctx.window.SpecWebClipboard.copyText('hello');
  assert.equal(result, true);
});

test('copyText: navigator.clipboard が無い環境（iframe サンドボックス相当）でも execCommand フォールバックを試す', async () => {
  const { ctx, dom } = setup(undefined, true);
  const result = await ctx.window.SpecWebClipboard.copyText('hello');
  assert.equal(result, true);
  // textarea は一時的に body へ追加されコピー後に取り除かれる（残留しない）。
  assert.equal(dom.document.body.children.length, 0);
});

test('copyText: execCommand も無い/失敗する場合は false で resolve する（呼び出し側が Ctrl+C 表示に切り替える）', async () => {
  const { ctx } = setup(undefined, false);
  const result = await ctx.window.SpecWebClipboard.copyText('hello');
  assert.equal(result, false);
});

test('copyText: document.execCommand 自体が無い環境でも例外にせず false で resolve する', async () => {
  const { ctx } = setup(undefined, undefined);
  const result = await ctx.window.SpecWebClipboard.copyText('hello');
  assert.equal(result, false);
});

test('copyText: 空文字は false で即 resolve する（コピー対象が無い）', async () => {
  const { ctx } = setup({ clipboard: { writeText: function () { return Promise.resolve(); } } });
  const result = await ctx.window.SpecWebClipboard.copyText('');
  assert.equal(result, false);
});
