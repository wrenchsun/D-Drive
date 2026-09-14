'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScripts } = require('./load-html-script.js');
const { createFakeDom, createFakeSpecWebClient, flush } = require('./dom-stub.js');

// O-2 スモークテスト: 発注ツリー画面（OrderTree.html）が例外を投げずに一通り実行できることを確認する
// （集計ロジック自体は test/orderTreeLogic.test.js で別途検証済み）。

function setup(role, options) {
  options = options || {};
  const dom = createFakeDom();
  let capturedRender = null;

  const sandbox = {
    console,
    document: dom.document,
    navigator: options.navigator,
    registerScreen: function (id, render) {
      if (id === 'orders') capturedRender = render;
    },
    window: {
      alert: function () {},
      // O-13: 発注リンクのコピー用（src/Code.js の specWebExecUrl_ → html/Index.html）。
      SpecWebExecUrl: options.execUrl !== undefined ? options.execUrl : 'https://script.google.com/macros/s/fake/exec'
    }
  };
  if (options.execCommand !== undefined) {
    dom.document.execCommand = function () {
      return options.execCommand;
    };
  }

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

  // O-13: OrderLinkLogic/ClipboardCopy も実 Index.html と同じ順序で読み込む
  // （OrderTree.html の buildCopyLinkControl が window.OrderLinkLogic/window.SpecWebClipboard を使う）。
  const ctx = loadHtmlScripts(['OrderLinkLogic', 'ClipboardCopy', 'AssetsLogic', 'OrderTreeLogic', 'OrderTree'], sandbox);
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

// ---- O-13: 発注グループにも「リンクをコピー」を付ける（専用の詳細画面が無いため、
// 一覧上のグループヘッダーを「詳細」相当として扱う。docs/32 §10.8 参照） ----

test('グループヘッダーに「リンクをコピー」ボタンが出て、押すと execCommand フォールバックでコピーできる', async () => {
  const { dom, render } = setup('editor', { execCommand: true });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const copyButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === 'リンクをコピー');
  assert.ok(copyButton, 'グループヘッダーにリンクをコピーボタンが出る');
  assert.doesNotThrow(() => dom.fire(copyButton, 'click'));
  await flush();

  const status = dom.findNode(root, (n) => n.className === 'sw-copylink-status' && n.textContent === 'コピーしました');
  assert.ok(status);
});

test('execCommand も失敗する環境では読み取り専用の入力欄（?page=group&id=... の URL 入り）を出す', async () => {
  const { dom, render } = setup('editor', { execCommand: false });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const copyButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === 'リンクをコピー');
  dom.fire(copyButton, 'click');
  await flush();

  const fallback = dom.findNode(root, (n) => n.className === 'sw-copylink-fallback');
  assert.ok(fallback);
  assert.equal(fallback.value, 'https://script.google.com/macros/s/fake/exec?page=group&id=og_1');
});

// ---- O-13: `?page=group&id=...` 深いリンクで開いた場合のスクロール+ハイライト ----

test('render(root, {openGroupId}): 対象のグループボックスに sw-highlight が付く', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root, { openGroupId: 'og_1' });
  await flush();

  const box = dom.findNode(root, (n) => n.className && n.className.indexOf('order-tree-group') !== -1);
  assert.ok(box);
  assert.match(box.className, /sw-highlight/);
});

test('render(root, {openGroupId}): 存在しないグループ id なら例外にせず案内を出す', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  assert.doesNotThrow(() => render(root, { openGroupId: 'og_notexist' }));
  await flush();

  const notice = dom.findNode(root, (n) => n.tagName === 'p' && (n.textContent || '').indexOf('見つかりません') !== -1);
  assert.ok(notice);
});
