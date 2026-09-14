'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScripts } = require('./load-html-script.js');
const { createFakeDom, createFakeSpecWebClient, flush } = require('./dom-stub.js');

// O-9/O-10 スモークテスト: メンバー管理 + ガント URL 設定画面（Members.html）が
// 例外を投げずに動くことを確認する。

function setup(role) {
  const dom = createFakeDom();
  let capturedRender = null;

  const sandbox = {
    console,
    document: dom.document,
    registerScreen: function (id, render) {
      if (id === 'members') capturedRender = render;
    },
    window: { alert: function () {} }
  };

  const members = [{ label: '吉田(PLN)', source: 'gantt', email: '' }];

  const fakeClient = createFakeSpecWebClient({
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
    }
  });
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
