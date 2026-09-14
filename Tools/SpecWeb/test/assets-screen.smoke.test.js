'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScripts } = require('./load-html-script.js');
const { createFakeDom, createFakeSpecWebClient, flush } = require('./dom-stub.js');

// O-1/O-3/O-5 スモークテスト: Assets.html（DOM を組み立てる画面コード本体）が、
// 一覧の初期表示・新規作成パネル・既存発注の詳細パネル（コメント・状態進行ボタン含む）・
// viewer ロールでの読み取り専用表示を、例外を投げずに一通り実行できることを確認する。
//
// jsdom は使わず（依存ゼロ方針）、test/dom-stub.js の最小限のフェイク DOM の上で動かす。
// 見た目までは検証しないが、未定義参照・タイポ等の実行時エラーはここで検出できる
// （AssetsLogic.html 側の純粋関数は assets-logic.test.js で別途しっかり検証済み）。

function sampleAsset(overrides) {
  return Object.assign(
    {
      id: 'Se::Slash',
      assetType: 'Se',
      identifier: 'Slash',
      displayName: '斬撃音',
      category: 'Player',
      status: '発注済',
      orderer: 'よしだ',
      contractor: 'たなか',
      orderDate: '2026-09-01',
      dueDate: '',
      deliveredDate: '',
      priority: '',
      referenceMd: '',
      parentId: null,
      archived: false,
      revision: 3,
      comments: [{ id: 'c1', author: 'a@example.com', body: '既存コメント', createdAt: '2026-09-01T00:00:00Z', resolved: false }],
      ddriveState: { created: true, isPlaceholder: false, iconAssetId: 'icon1', usageCount: 4, lastSyncedAt: '2026-09-14T00:00:00Z' },
      params: null
    },
    overrides
  );
}

function setup(role) {
  const dom = createFakeDom();
  let capturedRender = null;

  const sandbox = {
    console,
    document: dom.document,
    registerScreen: function (id, render) {
      if (id === 'assets') capturedRender = render;
    },
    window: {
      alert: function () {},
      confirm: function () {
        return true;
      }
    }
  };

  const asset = sampleAsset();
  const fakeClient = createFakeSpecWebClient({
    whoami: function () {
      return { ok: true, role: role, email: role + '@example.com', displayName: role };
    },
    'assets.list': function () {
      return { ok: true, items: [asset], total: 1 };
    },
    'assets.get': function () {
      return { ok: true, item: asset };
    },
    'orderGroups.list': function () {
      return { ok: true, items: [] };
    },
    'members.list': function () {
      return { ok: true, items: [] };
    },
    'paramSchemas.list': function () {
      return { ok: true, items: [] };
    },
    'assets.comments.add': function (params) {
      const updated = Object.assign({}, asset, {
        comments: asset.comments.concat([{ id: 'c2', author: role + '@example.com', body: params.body, createdAt: '2026-09-14T01:00:00Z', resolved: false }])
      });
      return { ok: true, item: updated };
    },
    'assets.create': function () {
      return { ok: true, item: sampleAsset({ id: 'Vfx::FireBall', assetType: 'Vfx', identifier: 'FireBall', revision: 1 }) };
    },
    'assets.update': function (params) {
      const patch = JSON.parse(params.patch);
      return { ok: true, item: Object.assign({}, asset, patch, { revision: asset.revision + 1 }) };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;

  const ctx = loadHtmlScripts(['AssetsLogic', 'Assets'], sandbox);
  assert.ok(capturedRender, 'registerScreen("assets", ...) が呼ばれていること');
  return { ctx, dom, render: capturedRender };
}

test('editor: 初期表示で一覧と「+ 新規発注」ボタンが例外なくレンダリングされる', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');

  assert.doesNotThrow(() => render(root));
  await flush();

  const newButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 新規発注');
  assert.ok(newButton, '編集権限があれば + 新規発注 ボタンが表示される');

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  assert.ok(row, '一覧に Slash の行が表示される');
});

test('editor: 「+ 新規発注」を押すと新規作成パネルが開き、無効な識別子で即時に赤表示される', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const newButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 新規発注');
  assert.doesNotThrow(() => dom.fire(newButton, 'click'));

  let heading = dom.findNode(root, (n) => n.tagName === 'h2');
  assert.equal(heading.textContent, '新規発注');

  var identifierInput = dom.findNode(root, (n) => {
    if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
    var label = (n.children || [])[0];
    return label && label.textContent === 'インポート名: 識別子（PascalCase）';
  });
  assert.ok(identifierInput, '識別子フィールドが見つかる');
  var input = identifierInput.children[1];
  input.value = 'not-pascal';
  assert.doesNotThrow(() => dom.fire(input, 'input'));

  // input イベントのハンドラが renderDetail() でパネルを再構築するため、root から再検索する。
  var reRenderedField = dom.findNode(root, (n) => {
    if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
    var label = (n.children || [])[0];
    return label && label.textContent === 'インポート名: 識別子（PascalCase）';
  });
  assert.ok(reRenderedField.className.indexOf('sw-field-error') !== -1, '不正な識別子は赤表示（sw-field-error）になる');
});

test('editor: 既存発注の行を押すと詳細パネルが開き、コメントの投稿・状態進行ボタンが表示される', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  assert.doesNotThrow(() => dom.fire(row, 'click'));
  await flush(); // openDetail 内の assets.get / paramSchemas.list 再取得を待つ

  const heading = dom.findNode(root, (n) => n.tagName === 'h2');
  assert.equal(heading.textContent, 'Se :: Slash');

  const existingComment = dom.findAllNodes(root, (n) => n.className === 'assets-comment');
  assert.equal(existingComment.length, 1);

  // 状態進行ボタン（発注済 → 納品済）。
  const advanceButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '納品済にする');
  assert.ok(advanceButton, '発注済のときは「納品済にする」ボタンが出る');
  assert.doesNotThrow(() => dom.fire(advanceButton, 'click'));
  await flush();

  const textareas = dom.findAllNodes(root, (n) => n.tagName === 'textarea');
  assert.ok(textareas.length >= 2, 'リファレンス欄とコメント入力欄が少なくとも2つあるはず');
  const commentTextarea = textareas[textareas.length - 1];
  commentTextarea.value = 'テストコメント';

  const postButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === 'コメントを投稿');
  assert.ok(postButton);
  assert.doesNotThrow(() => dom.fire(postButton, 'click'));
  await flush();

  const commentsAfterPost = dom.findAllNodes(root, (n) => n.className === 'assets-comment');
  assert.equal(commentsAfterPost.length, 2, 'コメント投稿後は 2 件になる');
});

test('editor: リファレンスの Markdown プレビューを開閉できる', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();

  const previewButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === 'プレビュー');
  assert.ok(previewButton);
  assert.doesNotThrow(() => dom.fire(previewButton, 'click'));

  const hideButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === 'プレビューを隠す');
  assert.ok(hideButton, 'プレビュー表示中は「プレビューを隠す」に切り替わる');
});

test('viewer: 「+ 新規発注」ボタンが無く、詳細パネルは読み取り専用になり、状態進行ボタンも出ない', async () => {
  const { dom, render } = setup('viewer');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const newButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 新規発注');
  assert.equal(newButton, null, 'viewer には + 新規発注 ボタンが表示されない');

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  assert.doesNotThrow(() => dom.fire(row, 'click'));
  await flush();

  const readonlyNotice = dom.findNode(root, (n) => n.textContent === '閲覧のみ（viewer ロールのため編集できません）');
  assert.ok(readonlyNotice);

  const saveButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '保存');
  assert.equal(saveButton, null, 'viewer には保存ボタンが表示されない');

  const advanceButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '納品済にする');
  assert.equal(advanceButton, null, 'viewer には状態進行ボタンが表示されない');

  const commentTextarea = dom.findNode(root, (n) => n.tagName === 'textarea' && n !== undefined && n.getAttribute && n.getAttribute('placeholder') === 'コメントを入力...');
  assert.equal(commentTextarea, null, 'viewer にはコメント投稿欄が表示されない');
});
