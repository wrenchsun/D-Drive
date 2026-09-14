'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScripts } = require('./load-html-script.js');
const { createFakeDom, createFakeSpecWebClient, flush } = require('./dom-stub.js');

// O-4 スモークテスト: 「私が発注」「私が受けた」画面（MyOrders.html）が例外を投げずに動くことを確認する。

function setup(options) {
  options = options || {};
  const dom = createFakeDom();
  let capturedRender = null;

  const sandbox = {
    console,
    document: dom.document,
    registerScreen: function (id, render) {
      if (id === 'my-orders') capturedRender = render;
    },
    window: {}
  };

  const items = options.items || [
    { id: 'Se::Hit', assetType: 'Se', identifier: 'Hit', orderer: 'me@example.com', contractor: 'たなか', status: '発注済', dueDate: '2020-01-01', archived: false },
    { id: 'Vfx::Trail', assetType: 'Vfx', identifier: 'Trail', orderer: '佐々木', contractor: 'me@example.com', status: '納品済', dueDate: '2099-01-01', archived: false }
  ];

  const fakeClient = createFakeSpecWebClient({
    whoami: function () {
      return { ok: true, role: options.role || 'editor', email: 'me@example.com' };
    },
    'assets.list': function () {
      return { ok: true, items: items };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;

  // O-15: 「編集」ボタンが呼ぶ画面遷移のスパイ（orderTree.smoke.test.js と同じ考え方）。
  const navigateCalls = [];
  sandbox.window.SpecWebNavigate = function (id, navOptions) {
    navigateCalls.push({ id: id, options: navOptions });
  };

  const ctx = loadHtmlScripts(['AssetsLogic', 'MyOrders'], sandbox);
  assert.ok(capturedRender, 'registerScreen("my-orders", ...) が呼ばれていること');
  return { ctx, dom, render: capturedRender, navigateCalls: navigateCalls };
}

test('私が発注/私が受けたの両方が例外なくレンダリングされ、期限切れバッジが出る', async () => {
  const { dom, render } = setup();
  const root = dom.document.createElement('div');
  assert.doesNotThrow(() => render(root));
  await flush();

  const orderedItem = dom.findNode(root, (n) => (n.textContent || '').indexOf('Hit') !== -1);
  assert.ok(orderedItem, '私が発注したものに Hit が出る');

  const overdueBadge = dom.findNode(root, (n) => n.className === 'my-orders-badge sw-overdue');
  assert.ok(overdueBadge, '期限切れの発注にはバッジが出る');

  const contractedItem = dom.findNode(root, (n) => (n.textContent || '').indexOf('Trail') !== -1);
  assert.ok(contractedItem, '私が受けたものに Trail が出る');
});

// ---- O-15: 各行への「編集」導線 ----

test('editor: 各行に「編集」ボタンが出て、押すと SpecWebNavigate("assets", {params:{openId}}) が呼ばれる', async () => {
  const { dom, render, navigateCalls } = setup();
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const editButtons = dom.findAllNodes(root, (n) => n.tagName === 'button' && n.textContent === '編集');
  assert.equal(editButtons.length, 2, '発注したもの・受けたものの各1行ぶん出る');
  dom.fire(editButtons[0], 'click');

  assert.equal(navigateCalls.length, 1);
  assert.equal(navigateCalls[0].id, 'assets');
  assert.equal(navigateCalls[0].options.params.openId, 'Se::Hit');
});

test('viewer: 「編集」ボタンが出ない', async () => {
  const { dom, render } = setup({ role: 'viewer' });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const editButtons = dom.findAllNodes(root, (n) => n.tagName === 'button' && n.textContent === '編集');
  assert.equal(editButtons.length, 0);
});

// ---- O-16: 「メモあり」アイコン + 展開表示 ----

test('メモが入力済みの行には 📝 アイコンが出て、押すと整形表示が展開される（メモが無い行には出ない）', async () => {
  const { dom, render } = setup({
    items: [
      { id: 'Se::Hit', assetType: 'Se', identifier: 'Hit', orderer: 'me@example.com', contractor: 'たなか', status: '発注済', dueDate: '2020-01-01', archived: false, referenceMd: '参考: [動画](https://example.com/video)' },
      { id: 'Vfx::Trail', assetType: 'Vfx', identifier: 'Trail', orderer: '佐々木', contractor: 'me@example.com', status: '納品済', dueDate: '2099-01-01', archived: false }
    ]
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const memoButtons = dom.findAllNodes(root, (n) => n.tagName === 'button' && n.textContent === '📝');
  assert.equal(memoButtons.length, 1, 'メモが入力済みの1行だけに 📝 アイコンが出る');

  dom.fire(memoButtons[0], 'click');
  const memoBox = dom.findNode(root, (n) => n.className === 'assets-md-preview assets-memo-clip');
  assert.ok(memoBox, '押すと整形表示が展開される');
  assert.match(memoBox.innerHTML, /example\.com\/video/);
});
