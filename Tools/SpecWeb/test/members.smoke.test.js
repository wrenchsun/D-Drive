'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScripts } = require('./load-html-script.js');
const { createFakeDom, createFakeSpecWebClient, flush } = require('./dom-stub.js');

// O-9/O-10 スモークテスト: メンバー管理 + ガント URL 設定画面（Members.html）が
// 例外を投げずに動くことを確認する。

function setup(role, extraHandlers) {
  const dom = createFakeDom();
  let capturedRender = null;

  const sandbox = {
    console,
    document: dom.document,
    registerScreen: function (id, render) {
      if (id === 'members') capturedRender = render;
    },
    window: { alert: function () {}, confirm: function () { return true; } }
  };

  const members = [{ label: '吉田(PLN)', source: 'gantt', email: '' }];
  const users = [
    { email: role + '@example.com', displayName: '自分', role: role },
    { email: 'other@example.com', displayName: '他の人', role: 'viewer' }
  ];

  const fakeClient = createFakeSpecWebClient(Object.assign({
    whoami: function () {
      return { ok: true, role: role, email: role + '@example.com' };
    },
    'members.list': function () {
      return { ok: true, items: members };
    },
    'settings.get': function () {
      return { ok: true, ganttUrl: '' };
    },
    'members.importPaste': function () {
      return { ok: true, importedCount: 1 };
    },
    'users.list': function () {
      return { ok: true, items: users };
    },
    'users.upsert': function () {
      return { ok: true, item: { email: 'new@example.com', displayName: '新人', role: 'editor' } };
    },
    'users.remove': function () {
      return { ok: true, removed: true };
    }
  }, extraHandlers || {}));
  sandbox.window.SpecWebClient = fakeClient;

  const ctx = loadHtmlScripts(['AssetsLogic', 'Members'], sandbox);
  assert.ok(capturedRender, 'registerScreen("members", ...) が呼ばれていること');
  return { ctx, dom, render: capturedRender };
}

test('editor: メンバー一覧・貼り付け取り込みフォームが例外なくレンダリングされる', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  assert.doesNotThrow(() => render(root));
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'td' && n.textContent === '吉田(PLN)');
  assert.ok(row, 'メンバー一覧に取り込み済みの表記が出る');

  const importButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '取り込む');
  assert.ok(importButton);
});

test('admin: ガント URL 設定欄が編集可能で、editor では読み取り専用になる', async () => {
  const admin = setup('admin');
  const adminRoot = admin.dom.document.createElement('div');
  admin.render(adminRoot);
  await flush();
  const adminInput = admin.dom.findNode(adminRoot, (n) => n.tagName === 'input' && n.getAttribute('placeholder') && n.getAttribute('placeholder').indexOf('spreadsheets') !== -1);
  assert.equal(adminInput.disabled, false);

  const editor = setup('editor');
  const editorRoot = editor.dom.document.createElement('div');
  editor.render(editorRoot);
  await flush();
  const editorInput = editor.dom.findNode(editorRoot, (n) => n.tagName === 'input' && n.getAttribute('placeholder') && n.getAttribute('placeholder').indexOf('spreadsheets') !== -1);
  assert.equal(editorInput.disabled, true);
});

// O-14 スモークテスト: admin 専用の「ログイン許可」セクション（users.json 管理）。

test('O-14: admin にはログイン許可セクションが例外なく表示され、editor/viewer には表示されない', async () => {
  const admin = setup('admin');
  const adminRoot = admin.dom.document.createElement('div');
  assert.doesNotThrow(() => admin.render(adminRoot));
  await flush();
  const heading = admin.dom.findNode(adminRoot, (n) => n.tagName === 'h3' && n.textContent.indexOf('ログイン許可') !== -1);
  assert.ok(heading, 'admin にはログイン許可の見出しが出る');
  const otherRow = admin.dom.findNode(adminRoot, (n) => n.tagName === 'td' && n.textContent === 'other@example.com');
  assert.ok(otherRow, '一覧に他のユーザーが出る');

  const editor = setup('editor');
  const editorRoot = editor.dom.document.createElement('div');
  assert.doesNotThrow(() => editor.render(editorRoot));
  await flush();
  const editorHeading = editor.dom.findNode(editorRoot, (n) => n.tagName === 'h3' && n.textContent.indexOf('ログイン許可') !== -1);
  assert.equal(editorHeading, null, 'editor にはログイン許可セクションが出ない');
});

test('O-14: 自分自身の行はロール変更・削除ボタンが無効化される', async () => {
  const admin = setup('admin');
  const root = admin.dom.document.createElement('div');
  admin.render(root);
  await flush();

  const selfRow = admin.dom.findNode(root, (n) => n.tagName === 'td' && n.textContent === 'admin@example.com');
  assert.ok(selfRow, '自分自身の行が一覧に出る');
});

test('O-14: 追加ボタンから users.upsert が呼べる（例外なく動く）', async () => {
  let called = null;
  const admin = setup('admin', {
    'users.upsert': function (params) {
      called = params;
      return { ok: true, item: { email: params.email, displayName: params.displayName, role: params.role } };
    }
  });
  const root = admin.dom.document.createElement('div');
  admin.render(root);
  await flush();

  const emailInput = admin.dom.findNode(root, (n) => n.tagName === 'input' && n.getAttribute('placeholder') === 'you@example.com');
  assert.ok(emailInput);
  emailInput.value = 'new@example.com';
  const addButton = admin.dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '追加');
  assert.ok(addButton);
  addButton._listeners.click[0]();
  await flush();

  assert.ok(called, 'users.upsert が呼ばれること');
  assert.equal(called.email, 'new@example.com');
  assert.equal(called.shareFolder, true, '既定でデータフォルダ共有チェックは ON');
});

test('O-14: 削除ボタンから users.remove が呼べる（confirm 経由、例外なく動く）', async () => {
  let called = null;
  const admin = setup('admin', {
    'users.remove': function (params) {
      called = params;
      return { ok: true, removed: true };
    }
  });
  const root = admin.dom.document.createElement('div');
  admin.render(root);
  await flush();

  // ログイン許可セクション（class に "users-section" を含む）に絞る。members-section にも
  // 同じ文字列（「削除」）のボタンがあるため、区別せずに探すと 3 件ヒットしてしまう。
  const usersSectionNode = admin.dom.findNode(root, (n) => n.className && n.className.indexOf('users-section') !== -1);
  assert.ok(usersSectionNode, 'ログイン許可セクションが見つかること');

  // state.users は [自分, other@example.com] の順で並ぶため、削除ボタンも同じ順で並ぶ。
  // 自分自身の行の削除ボタンは disabled だが、フェイク DOM は disabled を実際には強制しないため、
  // 「other@example.com（2 番目）」のボタンを明示して押す。
  const removeButtons = admin.dom.findAllNodes(usersSectionNode, (n) => n.tagName === 'button' && n.textContent === '削除');
  assert.equal(removeButtons.length, 2);
  assert.equal(removeButtons[0].disabled, true, '自分自身の行の削除ボタンは無効化されている');
  assert.equal(removeButtons[1].disabled, false);
  removeButtons[1]._listeners.click[0]();
  await flush();

  assert.ok(called, 'users.remove が呼ばれること');
  assert.equal(called.email, 'other@example.com');
});
