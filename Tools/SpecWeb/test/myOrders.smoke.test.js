'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScripts } = require('./load-html-script.js');
const { createFakeDom, createFakeSpecWebClient, flush } = require('./dom-stub.js');

// O-4 スモークテスト: 「私が発注」「私が受けた」画面（MyOrders.html）が例外を投げずに動くことを確認する。

function setup() {
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

  const items = [
    { id: 'Se::Hit', assetType: 'Se', identifier: 'Hit', orderer: 'me@example.com', contractor: 'たなか', status: '発注済', dueDate: '2020-01-01', archived: false },
    { id: 'Vfx::Trail', assetType: 'Vfx', identifier: 'Trail', orderer: '佐々木', contractor: 'me@example.com', status: '納品済', dueDate: '2099-01-01', archived: false }
  ];

  const fakeClient = createFakeSpecWebClient({
    whoami: function () {
      return { ok: true, role: 'editor', email: 'me@example.com' };
    },
    'assets.list': function () {
      return { ok: true, items: items };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;

  const ctx = loadHtmlScripts(['AssetsLogic', 'MyOrders'], sandbox);
  assert.ok(capturedRender, 'registerScreen("my-orders", ...) が呼ばれていること');
  return { ctx, dom, render: capturedRender };
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
