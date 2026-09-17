'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScripts } = require('./load-html-script.js');
const { createFakeDom, createFakeSpecWebClient } = require('./dom-stub.js');

// 緊急修正（2026-09-14）: 実デプロイで「上部メニューの『マニュアル』を押すとトップが
// *-script.googleusercontent.com/userCodeAppPanel?page=manual&p=Readme に遷移して
// 真っ白になる」不具合が起きた。原因は html/Manual.html のナビリンク・
// tools/build-manual.js の生成物（本文中のリンク・ナビバー）が相対 href
// `?page=manual&p=...` を直接持っていたため、<base target="_top"> 環境でクリックの
// 既定動作が走ると iframe 自身の URL 基準で解決されてしまうこと。
// 修正: href は "#"（または window.SpecWebExecUrl から組み立てた絶対 URL）にし、
// data-manual-page/data-manual-exit から実行時に絶対 URL を設定し直す
// （html/Manual.html の applyAbsoluteHrefs_、html/OrderLinkLogic.html の
// buildManualUrl/buildExitUrl）。

function setup(execUrl) {
  const dom = createFakeDom();
  let capturedRender = null;

  const sandbox = {
    console,
    document: dom.document,
    registerScreen: function (id, render) {
      if (id === 'manual') capturedRender = render;
    },
    window: {
      SpecWebExecUrl: execUrl !== undefined ? execUrl : 'https://script.google.com/macros/s/fake/exec',
      SpecWebNavigate: function () {}
    }
  };
  const fakeClient = createFakeSpecWebClient({
    manualGet: function () {
      return { ok: true, html: '<p>dummy</p>' };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;

  const ctx = loadHtmlScripts(['OrderLinkLogic', 'Manual'], sandbox);
  return { ctx, dom, render: capturedRender };
}

test('モジュール読み込み: dom-stub の document.getElementById は常に null を返すため（他の画面と同じ制約）トップナビの追加処理は素通りするが、例外にならず読み込める', () => {
  assert.doesNotThrow(() => setup('https://script.google.com/macros/s/fake/exec'));
  assert.doesNotThrow(() => setup(''));
});

test('registerScreen("manual", ...): manualGet を呼び、例外なく本文を差し込む', async () => {
  const { dom, render } = setup('https://script.google.com/macros/s/fake/exec');
  const root = dom.document.createElement('div');
  assert.doesNotThrow(() => render(root, { p: 'Readme' }));
  await new Promise((resolve) => setTimeout(resolve, 0));

  const loading = dom.findNode(root, (n) => n.textContent === 'マニュアルを読み込み中...');
  // 読み込み完了後は root.innerHTML='' でクリアされるため、もう存在しない。
  assert.equal(loading, null);
});

test('registerScreen("manual", ...): manualGet が失敗しても例外にせずエラーメッセージを出す', async () => {
  const dom = createFakeDom();
  let capturedRender = null;
  const sandbox = {
    console,
    document: dom.document,
    registerScreen: function (id, render) {
      if (id === 'manual') capturedRender = render;
    },
    window: {
      SpecWebExecUrl: '',
      SpecWebNavigate: function () {},
      SpecWebClient: createFakeSpecWebClient({
        manualGet: function () {
          return { ok: false, error: 'not found' };
        }
      })
    }
  };
  loadHtmlScripts(['OrderLinkLogic', 'Manual'], sandbox);
  const root = dom.document.createElement('div');
  assert.doesNotThrow(() => capturedRender(root, { p: 'Missing' }));
  await new Promise((resolve) => setTimeout(resolve, 0));

  const errorEl = dom.findNode(root, (n) => (n.textContent || '').indexOf('読み込めませんでした') !== -1);
  assert.ok(errorEl);
});

// ---- applyAbsoluteHrefs_（html/Manual.html の SpecWebManualTestHooks_ 経由） ----

function buildAnchor(dom, attrs) {
  const a = dom.document.createElement('a');
  a.setAttribute('href', '#');
  Object.keys(attrs).forEach((key) => a.setAttribute(key, attrs[key]));
  return a;
}

test('applyAbsoluteHrefs_: execUrl があれば data-manual-page の href を絶対 URL に差し替える', () => {
  const { ctx, dom } = setup('https://script.google.com/macros/s/fake/exec');
  const container = dom.document.createElement('div');
  const pageLink = buildAnchor(dom, { 'data-manual-page': 'other-page', 'data-manual-anchor': 'section' });
  container.appendChild(pageLink);

  ctx.window.SpecWebManualTestHooks_.applyAbsoluteHrefs(container);

  assert.equal(
    pageLink.getAttribute('href'),
    'https://script.google.com/macros/s/fake/exec?page=manual&p=other-page#section'
  );
});

test('applyAbsoluteHrefs_: execUrl があれば data-manual-exit の href を発注ツールのトップ URL に差し替える', () => {
  const { ctx, dom } = setup('https://script.google.com/macros/s/fake/exec');
  const container = dom.document.createElement('div');
  const exitLink = buildAnchor(dom, { 'data-manual-exit': 'orders' });
  container.appendChild(exitLink);

  ctx.window.SpecWebManualTestHooks_.applyAbsoluteHrefs(container);

  assert.equal(exitLink.getAttribute('href'), 'https://script.google.com/macros/s/fake/exec');
});

test('applyAbsoluteHrefs_: execUrl が未取得なら href="#" のまま（白画面になる絶対 URL には絶対に書き換えない）', () => {
  const { ctx, dom } = setup('');
  const container = dom.document.createElement('div');
  const pageLink = buildAnchor(dom, { 'data-manual-page': 'other-page' });
  const exitLink = buildAnchor(dom, { 'data-manual-exit': 'orders' });
  container.appendChild(pageLink);
  container.appendChild(exitLink);

  ctx.window.SpecWebManualTestHooks_.applyAbsoluteHrefs(container);

  assert.equal(pageLink.getAttribute('href'), '#');
  assert.equal(exitLink.getAttribute('href'), '#');
});

// 2026-09-17（プログラマーマニュアル配信対応）
test('applyAbsoluteHrefs_: data-manual-kind="programmer" のリンクは &kind=programmer 付きの絶対 URL になる', () => {
  const { ctx, dom } = setup('https://script.google.com/macros/s/fake/exec');
  const container = dom.document.createElement('div');
  const pageLink = buildAnchor(dom, { 'data-manual-kind': 'programmer', 'data-manual-page': 'concepts' });
  container.appendChild(pageLink);

  ctx.window.SpecWebManualTestHooks_.applyAbsoluteHrefs(container);

  assert.equal(
    pageLink.getAttribute('href'),
    'https://script.google.com/macros/s/fake/exec?page=manual&p=concepts&kind=programmer'
  );
});

test('registerScreen("manual", ...): params.kind を manualGet にそのまま渡す（kind 未指定は designer）', async () => {
  const dom = createFakeDom();
  let capturedRender = null;
  let capturedParams = null;
  const sandbox = {
    console,
    document: dom.document,
    registerScreen: function (id, render) {
      if (id === 'manual') capturedRender = render;
    },
    window: {
      SpecWebExecUrl: 'https://script.google.com/macros/s/fake/exec',
      SpecWebNavigate: function () {},
      SpecWebClient: createFakeSpecWebClient({
        manualGet: function (params) {
          capturedParams = params;
          return { ok: true, kind: params.kind, html: '<p>dummy</p>' };
        }
      })
    }
  };
  loadHtmlScripts(['OrderLinkLogic', 'Manual'], sandbox);
  const root = dom.document.createElement('div');

  capturedRender(root, { p: 'concepts', kind: 'programmer' });
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(capturedParams.kind, 'programmer');

  capturedRender(root, { p: 'Readme' });
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(capturedParams.kind, 'designer');
});

test('applyAbsoluteHrefs_: ネストした子要素の中の <a> も見つけて書き換える', () => {
  const { ctx, dom } = setup('https://script.google.com/macros/s/fake/exec');
  const container = dom.document.createElement('div');
  const wrapperDiv = dom.document.createElement('div');
  const nestedLink = buildAnchor(dom, { 'data-manual-page': 'nested-page' });
  wrapperDiv.appendChild(nestedLink);
  container.appendChild(wrapperDiv);

  ctx.window.SpecWebManualTestHooks_.applyAbsoluteHrefs(container);

  assert.equal(
    nestedLink.getAttribute('href'),
    'https://script.google.com/macros/s/fake/exec?page=manual&p=nested-page'
  );
});
