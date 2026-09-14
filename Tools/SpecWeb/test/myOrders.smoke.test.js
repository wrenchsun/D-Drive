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
    },
    // 緊急修正（2026-09-14 追補）: 各行から直接削除（アーカイブ）するテスト用。
    'assets.delete': options.deleteHandler || function (params) {
      return { ok: true, item: Object.assign({}, items.filter(function (i) { return i.id === params.id; })[0], { archived: true }) };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;
  // 緊急修正（2026-09-14 追補）: window.SpecWebUi（送信中表示・トースト・二重送信防止）と
  // window.confirm（削除の確認）。実 Index.html と同じ順序で UiFeedback を読み込む必要がある。
  sandbox.window.confirm = options.confirm !== undefined ? options.confirm : function () { return true; };

  // O-15: 「編集」ボタンが呼ぶ画面遷移のスパイ（orderTree.smoke.test.js と同じ考え方）。
  const navigateCalls = [];
  sandbox.window.SpecWebNavigate = function (id, navOptions) {
    navigateCalls.push({ id: id, options: navOptions });
  };

  const ctx = loadHtmlScripts(['UiFeedback', 'AssetsLogic', 'MyOrders'], sandbox);
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
  // 2026-09-15 二度目の修正: 画面遷移は setTimeout(…, 0) でこのクリックの処理が完全に
  // 終わった後に行うようにした（docs/32_spec_web.md 参照）。
  await flush();

  assert.equal(navigateCalls.length, 1);
  assert.equal(navigateCalls[0].id, 'assets');
  assert.equal(navigateCalls[0].options.params.openId, 'Se::Hit');
  // 緊急修正（2026-09-14 追補）: 戻り先（この画面）を backTo として渡す
  // （一覧画面の詳細パネルに「← 私の発注へ戻る」を出すため）。
  assert.equal(navigateCalls[0].options.params.backTo, 'my-orders');
});

test('viewer: 「編集」ボタンが出ない', async () => {
  const { dom, render } = setup({ role: 'viewer' });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const editButtons = dom.findAllNodes(root, (n) => n.tagName === 'button' && n.textContent === '編集');
  assert.equal(editButtons.length, 0);
});

// ---- 緊急修正（2026-09-14 追補）: 各行から直接削除（アーカイブ） ----

test('editor: 各行に「削除」ボタンが出て、押すと確認→assets.delete→その場で（発注/受注どちらの列からも）消える', async () => {
  const deleteCalls = [];
  const { dom, render } = setup({
    deleteHandler: function (params) {
      deleteCalls.push(params);
      return { ok: true, item: { id: params.id, archived: true } };
    }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const deleteButtons = dom.findAllNodes(root, (n) => n.tagName === 'button' && n.textContent === '削除');
  assert.equal(deleteButtons.length, 2, '発注したもの・受けたものの各1行ぶん出る');

  dom.fire(deleteButtons[0], 'click');
  await flush();

  assert.equal(deleteCalls.length, 1, 'assets.delete が呼ばれる');
  assert.equal(deleteCalls[0].id, 'Se::Hit');

  const hitItem = dom.findNode(root, (n) => (n.textContent || '').indexOf('Hit') !== -1 && n.tagName === 'span');
  assert.equal(hitItem, null, '削除した行はその場で消える');

  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('削除しました') !== -1);
  assert.ok(toast);
});

test('viewer: 「削除」ボタンが出ない', async () => {
  const { dom, render } = setup({ role: 'viewer' });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const deleteButtons = dom.findAllNodes(root, (n) => n.tagName === 'button' && n.textContent === '削除');
  assert.equal(deleteButtons.length, 0);
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
