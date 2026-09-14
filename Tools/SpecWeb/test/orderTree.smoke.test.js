'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScripts } = require('./load-html-script.js');
const { createFakeDom, createFakeSpecWebClient, flush } = require('./dom-stub.js');

// O-2 スモークテスト: 発注ツリー画面（OrderTree.html）が例外を投げずに一通り実行できることを確認する
// （集計ロジック自体は test/orderTreeLogic.test.js で別途検証済み）。

function setup(role) {
  const dom = createFakeDom();
  let capturedRender = null;

  const sandbox = {
    console,
    document: dom.document,
    registerScreen: function (id, render) {
      if (id === 'orders') capturedRender = render;
    },
    window: { alert: function () {} }
  };

  const group = { id: 'og_1', name: 'スキル: 斬撃', wbsNo: '3.2.1', revision: 1 };
  const asset = {
    id: 'Se::Hit', assetType: 'Se', identifier: 'Hit', displayName: '斬撃音',
    status: '納品済', orderer: 'よしだ', contractor: 'たなか', parentId: 'og_1', archived: false
  };

  const fakeClient = createFakeSpecWebClient({
    whoami: function () {
      return { ok: true, role: role, email: role + '@example.com' };
    },
    'orderGroups.list': function () {
      return { ok: true, items: [group] };
    },
    'assets.list': function () {
      return { ok: true, items: [asset] };
    },
    'settings.get': function () {
      return { ok: true, ganttUrl: 'https://example.com/gantt-dummy' };
    },
    'orderGroups.create': function () {
      return { ok: true, item: { id: 'og_2', name: '新規グループ', revision: 1 } };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;

  const ctx = loadHtmlScripts(['AssetsLogic', 'OrderTreeLogic', 'OrderTree'], sandbox);
  assert.ok(capturedRender, 'registerScreen("orders", ...) が呼ばれていること');
  return { ctx, dom, render: capturedRender };
}

test('editor: 発注グループ・子の集計・WBS リンクが例外なくレンダリングされる', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  assert.doesNotThrow(() => render(root));
  await flush();

  const groupHeading = dom.findNode(root, (n) => n.tagName === 'span' && n.textContent === 'スキル: 斬撃');
  assert.ok(groupHeading, '発注グループ名が表示される');

  const wbsLink = dom.findNode(root, (n) => n.tagName === 'a' && (n.textContent || '').indexOf('WBS 3.2.1') !== -1);
  assert.ok(wbsLink, 'ganttUrl 設定済みなら WBS リンクが表示される');

  const item = dom.findNode(root, (n) => n.tagName === 'li' && (n.textContent || '').indexOf('Hit') !== -1);
  assert.ok(item, '子の発注が一覧に出る');

  const newGroupButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 発注グループを作成');
  assert.ok(newGroupButton, 'editor には発注グループ作成ボタンが出る');
});

test('viewer: 発注グループ作成ボタンが表示されない', async () => {
  const { dom, render } = setup('viewer');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const newGroupButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 発注グループを作成');
  assert.equal(newGroupButton, null);
});
