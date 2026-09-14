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
    'orderGroups.create': options.createHandler || function () {
      return { ok: true, item: { id: 'og_2', name: '新規グループ', revision: 1 } };
    },
    'orderGroups.delete': options.deleteHandler || function () {
      return { ok: true, deleted: true };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;
  sandbox.window.confirm = options.confirm !== undefined ? options.confirm : function () { return true; };

  // O-13: OrderLinkLogic/ClipboardCopy も実 Index.html と同じ順序で読み込む
  // （OrderTree.html の buildCopyLinkControl が window.OrderLinkLogic/window.SpecWebClipboard を使う）。
  // 緊急修正（2026-09-14）: UiFeedback（window.SpecWebUi の runBusy/toast）も、実 Index.html と
  // 同じ順序（ClipboardCopy の後・AssetsLogic の前）で読み込む
  // （+発注グループを作成ボタンの二重送信防止・削除ボタンが使う）。
  const ctx = loadHtmlScripts(
    ['OrderLinkLogic', 'ClipboardCopy', 'UiFeedback', 'AssetsLogic', 'OrderTreeLogic', 'OrderTree'],
    sandbox
  );
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

// ---- 緊急修正（2026-09-14）: 連打での重複作成対策（「aaa」という空のグループが10件近く
// できた不具合の原因は、応答が遅い間に連打しても何も見た目が変わらなかったこと）。 ----

test('「+ 発注グループを作成」: 応答が返るまでボタンが無効化され「送信中...」になり、連打しても2回目は API を呼ばない', async () => {
  let createCalls = 0;
  let resolveCreate;
  const { dom, render } = setup('editor', {
    createHandler: function () {
      createCalls += 1;
      return new Promise((resolve) => {
        resolveCreate = resolve;
      });
    }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const nameInput = dom.findNode(root, (n) => n.tagName === 'input' && n.getAttribute('placeholder') === 'Presentation 発注グループ名（例: スキル: 斬撃）');
  const button = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 発注グループを作成');
  nameInput.value = 'aaa';

  dom.fire(button, 'click');
  dom.fire(button, 'click'); // 連打
  dom.fire(button, 'click');

  assert.equal(createCalls, 1, 'orderGroups.create は1回だけ呼ばれる（連打しても重複作成しない）');
  assert.equal(button.disabled, true, '送信中はボタンが無効化される');
  assert.equal(button.textContent, '送信中...');

  resolveCreate({ ok: true, item: { id: 'og_aaa', name: 'aaa', revision: 1 } });
  await flush();

  assert.equal(button.disabled, false, '完了後はボタンが元に戻る');
  assert.equal(button.textContent, '+ 発注グループを作成');
  assert.equal(nameInput.value, '', '成功後は入力欄がクリアされる');

  // 体感速度の改善: 成功後は一覧全体の再取得（reload）をせず、その場で1件だけ追加する。
  const newGroupHeading = dom.findNode(root, (n) => n.tagName === 'span' && n.textContent === 'aaa');
  assert.ok(newGroupHeading, '作成したグループがその場で一覧に反映される（reload なし）');
});

test('「+ 発注グループを作成」: 失敗時はトーストでエラーを表示し、ボタンは元に戻る', async () => {
  const { dom, render } = setup('editor', {
    createHandler: function () {
      return { ok: false, error: '権限がありません' };
    }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const nameInput = dom.findNode(root, (n) => n.tagName === 'input' && n.getAttribute('placeholder') === 'Presentation 発注グループ名（例: スキル: 斬撃）');
  const button = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 発注グループを作成');
  nameInput.value = 'bbb';
  dom.fire(button, 'click');
  await flush();

  assert.equal(button.disabled, false);
  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('作成に失敗しました') !== -1);
  assert.ok(toast, '失敗トーストが表示される');
});

// ---- 緊急修正（2026-09-14）: 発注グループの削除（「削除機能はありますか？」への回答） ----

test('editor: 発注グループヘッダーに「削除」ボタンが出て、confirm 後に orderGroups.delete を呼び、その場で一覧から消える', async () => {
  let deleteCalledWith = null;
  const { dom, render } = setup('editor', {
    deleteHandler: function (params) {
      deleteCalledWith = params;
      return { ok: true, deleted: true };
    }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const deleteButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '削除');
  assert.ok(deleteButton, 'editor には削除ボタンが出る');
  dom.fire(deleteButton, 'click');
  await flush();

  assert.ok(deleteCalledWith, 'orderGroups.delete が呼ばれる');
  assert.equal(deleteCalledWith.id, 'og_1');

  const groupHeading = dom.findNode(root, (n) => n.tagName === 'span' && n.textContent === 'スキル: 斬撃');
  assert.equal(groupHeading, null, '削除成功後は一覧からその場で取り除かれる（reload なし）');

  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('発注グループを削除しました') !== -1);
  assert.ok(toast);
});

test('editor: confirm でキャンセルすると orderGroups.delete は呼ばれない', async () => {
  let called = false;
  const { dom, render } = setup('editor', {
    confirm: function () { return false; },
    deleteHandler: function () {
      called = true;
      return { ok: true, deleted: true };
    }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const deleteButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '削除');
  dom.fire(deleteButton, 'click');
  await flush();

  assert.equal(called, false);
});

test('editor: 配下に発注が残っている場合の 400 拒否はトーストでサーバーのメッセージを表示する（配下0件なら即削除できる仕様の裏返し）', async () => {
  const { dom, render } = setup('editor', {
    deleteHandler: function () {
      return { ok: false, status: 400, error: 'この発注グループには子の発注が残っています。先に付け替えてから削除してください。' };
    }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const deleteButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '削除');
  dom.fire(deleteButton, 'click');
  await flush();

  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('子の発注が残っています') !== -1);
  assert.ok(toast);
  // 拒否されただけで削除はしていないので、一覧にはまだグループが残る。
  const groupHeading = dom.findNode(root, (n) => n.tagName === 'span' && n.textContent === 'スキル: 斬撃');
  assert.ok(groupHeading);
});

test('viewer: 発注グループヘッダーに「削除」ボタンが出ない', async () => {
  const { dom, render } = setup('viewer');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const deleteButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '削除');
  assert.equal(deleteButton, null);
});
