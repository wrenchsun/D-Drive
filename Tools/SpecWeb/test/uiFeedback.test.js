'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');
const { createFakeDom } = require('./dom-stub.js');

// 緊急修正（2026-09-14）: 「発注ツリーや新規発注のとき Web のレスポンスが遅く、連打して
// 大量に作ってしまった」への対策として追加した共通ヘルパー（html/UiFeedback.html）。
// - runBusy: 同じ key の呼び出しを二重実行させない + ボタンを「送信中...」に無効化する
// - toast/toastSuccess/toastError: 右下（狭い画面では下部）に結果を数秒表示する

function setup() {
  const dom = createFakeDom();
  const sandbox = { console, document: dom.document, window: {} };
  const ctx = loadHtmlScript('UiFeedback', sandbox);
  return { ctx, dom };
}

test('runBusy: 実行中はボタンを disabled にし、テキストを「送信中...」に変える', async () => {
  const { ctx, dom } = setup();
  const button = dom.document.createElement('button');
  button.textContent = '保存';
  button.disabled = false;

  let resolveRun;
  const runPromise = ctx.window.SpecWebUi.runBusy('test-key', button, function () {
    return new Promise((resolve) => {
      resolveRun = resolve;
    });
  });

  assert.equal(button.disabled, true, '実行中はボタンが無効化される');
  assert.equal(button.textContent, '送信中...');

  resolveRun({ ok: true });
  const result = await runPromise;

  assert.equal(result.ok, true);
  assert.equal(button.disabled, false, '完了後は元の disabled 状態に戻る');
  assert.equal(button.textContent, '保存', '完了後は元のテキストに戻る');
});

test('runBusy: 同じ key の呼び出しが完了する前に再度呼ぶと、2回目は run() を呼ばず busy:true を返す（連打対策）', async () => {
  const { ctx, dom } = setup();
  const button = dom.document.createElement('button');
  button.textContent = '+ 発注グループを作成';

  let callCount = 0;
  let resolveFirst;
  function run() {
    callCount += 1;
    return new Promise((resolve) => {
      resolveFirst = resolve;
    });
  }

  const first = ctx.window.SpecWebUi.runBusy('orderGroups.create', button, run);
  const second = ctx.window.SpecWebUi.runBusy('orderGroups.create', button, run);

  const secondResult = await second;
  assert.equal(secondResult.busy, true, '処理中の2回目呼び出しは busy:true で即座に返る');
  assert.equal(callCount, 1, 'run() は1回だけ呼ばれる（重複作成を防ぐ）');

  resolveFirst({ ok: true, item: { id: 'og_1', name: 'aaa' } });
  const firstResult = await first;
  assert.equal(firstResult.ok, true);
});

test('runBusy: 完了後は同じ key を再度実行できる（永久にロックされない）', async () => {
  const { ctx, dom } = setup();
  const button = dom.document.createElement('button');
  button.textContent = '保存';

  await ctx.window.SpecWebUi.runBusy('key-1', button, function () {
    return Promise.resolve({ ok: true });
  });
  const second = await ctx.window.SpecWebUi.runBusy('key-1', button, function () {
    return Promise.resolve({ ok: true, second: true });
  });
  assert.equal(second.second, true);
});

test('runBusy: run() が失敗（ok:false）で resolve しても、ボタンは元に戻る（次の操作を妨げない）', async () => {
  const { ctx, dom } = setup();
  const button = dom.document.createElement('button');
  button.textContent = '保存';

  const result = await ctx.window.SpecWebUi.runBusy('key-err', button, function () {
    return Promise.resolve({ ok: false, error: 'boom' });
  });

  assert.equal(result.ok, false);
  assert.equal(button.disabled, false);
  assert.equal(button.textContent, '保存');
});

test('runBusy: run() が例外を投げても、例外を外に出さずボタンを復帰し {ok:false} を返す', async () => {
  const { ctx, dom } = setup();
  const button = dom.document.createElement('button');
  button.textContent = '保存';

  const result = await ctx.window.SpecWebUi.runBusy('key-throw', button, function () {
    throw new Error('sync boom');
  });

  assert.equal(result.ok, false);
  assert.match(result.error, /sync boom/);
  assert.equal(button.disabled, false);
});

test('runBusy: button が無くても（null）二重実行防止だけは働く', async () => {
  const { ctx } = setup();
  let callCount = 0;
  function run() {
    callCount += 1;
    return Promise.resolve({ ok: true });
  }
  const first = ctx.window.SpecWebUi.runBusy('no-button-key', null, run);
  const secondResult = await ctx.window.SpecWebUi.runBusy('no-button-key', null, run);
  assert.equal(secondResult.busy, true);
  await first;
  assert.equal(callCount, 1);
});

// ---- トースト ----

test('toastSuccess/toastError: document.body 配下にトースト要素を追加する', () => {
  const { ctx, dom } = setup();
  ctx.window.SpecWebUi.toastSuccess('作成しました');
  ctx.window.SpecWebUi.toastError('失敗しました: 理由');

  const successToast = dom.findNode(dom.document.body, (n) => n.textContent === '作成しました');
  const errorToast = dom.findNode(dom.document.body, (n) => n.textContent === '失敗しました: 理由');
  assert.ok(successToast, '成功トーストが表示される');
  assert.ok(errorToast, '失敗トーストが表示される');
  assert.equal(errorToast.className.indexOf('sw-toast-error') !== -1, true, '失敗は sw-toast-error クラスを持つ');
  assert.equal(successToast.className.indexOf('sw-toast-error') !== -1, false, '成功は sw-toast-error クラスを持たない');
});

test('toast: 一定時間後に自動で消える', async () => {
  const { ctx, dom } = setup();
  ctx.window.SpecWebUi.toast('消えるはず', 'success', 5);
  const before = dom.findNode(dom.document.body, (n) => n.textContent === '消えるはず');
  assert.ok(before);

  await new Promise((resolve) => setTimeout(resolve, 20));

  const after = dom.findNode(dom.document.body, (n) => n.textContent === '消えるはず');
  assert.equal(after, null, 'durationMs 経過後は取り除かれる');
});

test('toast: document.body が無い環境でも例外にしない', () => {
  const dom = createFakeDom();
  dom.document.body = null;
  const sandbox = { console, document: dom.document, window: {} };
  const ctx = loadHtmlScript('UiFeedback', sandbox);
  assert.doesNotThrow(() => ctx.window.SpecWebUi.toastSuccess('x'));
});
