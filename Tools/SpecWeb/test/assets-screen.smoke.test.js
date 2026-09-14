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
      fileFormat: '.wav',
      fileName: 'SE_Slash.wav',
      archived: false,
      revision: 3,
      comments: [{ id: 'c1', author: 'a@example.com', body: '既存コメント', createdAt: '2026-09-01T00:00:00Z', resolved: false }],
      ddriveState: { created: true, isPlaceholder: false, iconAssetId: 'icon1', usageCount: 4, lastSyncedAt: '2026-09-14T00:00:00Z' },
      params: null
    },
    overrides
  );
}

function setup(role, options) {
  options = options || {};
  const dom = createFakeDom();
  let capturedRender = null;

  const sandbox = {
    console,
    document: dom.document,
    navigator: options.navigator,
    registerScreen: function (id, render) {
      if (id === 'assets') capturedRender = render;
    },
    window: {
      alert: function () {},
      confirm: function () {
        return true;
      },
      // O-13: 発注リンクのコピー用（src/Code.js の specWebExecUrl_ → html/Index.html）。
      SpecWebExecUrl: options.execUrl !== undefined ? options.execUrl : 'https://script.google.com/macros/s/fake/exec'
    }
  };
  if (options.execCommand !== undefined) {
    dom.document.execCommand = function () {
      return options.execCommand;
    };
  }

  // 緊急修正（2026-09-14）: assets.get/list が「delete/restore 後の最新状態」を返せるよう、
  // 固定値ではなく可変の currentAsset を持たせる（元は固定の asset を常に返していたため、
  // 削除→アーカイブ済みトグル→再度開く、のような一連の流れをテストできなかった）。
  let currentAsset = sampleAsset();
  const fakeClient = createFakeSpecWebClient({
    whoami: function () {
      return { ok: true, role: role, email: role + '@example.com', displayName: role };
    },
    'assets.list': function () {
      return { ok: true, items: [currentAsset], total: 1 };
    },
    'assets.get': function () {
      return { ok: true, item: currentAsset };
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
      currentAsset = Object.assign({}, currentAsset, {
        comments: currentAsset.comments.concat([{ id: 'c2', author: role + '@example.com', body: params.body, createdAt: '2026-09-14T01:00:00Z', resolved: false }])
      });
      return { ok: true, item: currentAsset };
    },
    'assets.create': options.createHandler || function () {
      return { ok: true, item: sampleAsset({ id: 'Vfx::FireBall', assetType: 'Vfx', identifier: 'FireBall', revision: 1 }) };
    },
    'assets.update': options.updateHandler || function (params) {
      const patch = JSON.parse(params.patch);
      currentAsset = Object.assign({}, currentAsset, patch, { revision: currentAsset.revision + 1 });
      return { ok: true, item: currentAsset };
    },
    'assets.delete': options.deleteHandler || function () {
      currentAsset = Object.assign({}, currentAsset, { archived: true, revision: currentAsset.revision + 1 });
      return { ok: true, item: currentAsset };
    },
    'assets.restore': options.restoreHandler || function () {
      currentAsset = Object.assign({}, currentAsset, { archived: false, revision: currentAsset.revision + 1 });
      return { ok: true, item: currentAsset };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;

  // O-13: OrderLinkLogic/ClipboardCopy も実 Index.html と同じ順序で読み込む
  // （Assets.html の buildCopyLinkControl が window.OrderLinkLogic/window.SpecWebClipboard を使う）。
  // 緊急修正（2026-09-14）: UiFeedback（window.SpecWebUi の runBusy/toast）も、実 Index.html と
  // 同じ順序（ClipboardCopy の後・AssetsLogic の前）で読み込む。
  const ctx = loadHtmlScripts(['OrderLinkLogic', 'ClipboardCopy', 'UiFeedback', 'AssetsLogic', 'Assets'], sandbox);
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

  // 緊急修正（2026-09-14）: 以前は input イベントのハンドラが renderDetail() でパネル全体を
  // 再構築していたため、この時点で input は「別の新しいノード」に置き換わっていた
  // （実ブラウザでは DOM 要素が作り直されるためフォーカスが外れる不具合の原因）。
  // 修正後は renderDetail() を呼ばずエラー表示だけを差し替えるので、
  // wrap（フィールドの囲み div）・input（実際の <input>）のどちらも同一ノードのまま残る。
  var reRenderedField = dom.findNode(root, (n) => {
    if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
    var label = (n.children || [])[0];
    return label && label.textContent === 'インポート名: 識別子（PascalCase）';
  });
  assert.equal(reRenderedField, identifierInput, '入力しても同じフィールド囲み div のまま（作り直されない）');
  assert.equal(reRenderedField.children[1], input, '入力しても同じ <input> のまま（作り直されない＝フォーカスが外れない）');
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

// ---- O-13: `?page=order&id=...` 深いリンクで開いた場合の自動オープン ----

test('render(root, {openId}): 一覧の読み込み後に対象の詳細が自動で開く', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root, { openId: 'Se::Slash' });
  await flush();
  await flush(); // openDetail 内の assets.get / paramSchemas.list 再取得も待つ

  const heading = dom.findNode(root, (n) => n.tagName === 'h2');
  assert.equal(heading.textContent, 'Se :: Slash');
});

test('render(root, {openId}): 存在しない id なら例外にせず一覧の上に案内を出す', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  assert.doesNotThrow(() => render(root, { openId: 'Se::NotExist' }));
  await flush();

  const heading = dom.findNode(root, (n) => n.tagName === 'h2');
  assert.equal(heading, null, '見つからない場合は詳細パネルを開かない');
  const notice = dom.findNode(root, (n) => n.tagName === 'p' && (n.textContent || '').indexOf('見つかりません') !== -1);
  assert.ok(notice, '「見つかりません」の案内が出る');
});

// ---- O-13: 「リンクをコピー」ボタン ----

test('一覧の行の「リンクをコピー」を押すと execCommand フォールバックで成功し「コピーしました」になる', async () => {
  const { dom, render } = setup('editor', { execCommand: true });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const copyButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === 'リンクをコピー');
  assert.ok(copyButton, '一覧の行にリンクをコピーボタンが出る');
  assert.doesNotThrow(() => dom.fire(copyButton, 'click'));
  await flush();

  const status = dom.findNode(root, (n) => n.className === 'sw-copylink-status' && n.textContent === 'コピーしました');
  assert.ok(status);
});

test('execCommand も失敗する環境では「Ctrl+C でコピーしてください」+ 読み取り専用の入力欄（URL 入り）を出す', async () => {
  const { dom, render } = setup('editor', { execCommand: false });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const copyButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === 'リンクをコピー');
  dom.fire(copyButton, 'click');
  await flush();

  const status = dom.findNode(root, (n) => n.className === 'sw-copylink-status' && n.textContent === 'Ctrl+C でコピーしてください');
  assert.ok(status);
  const fallback = dom.findNode(root, (n) => n.className === 'sw-copylink-fallback');
  assert.ok(fallback);
  assert.equal(fallback.value, 'https://script.google.com/macros/s/fake/exec?page=order&id=Se%3A%3ASlash');
});

test('execUrl が未取得（空文字）の場合は「URL を取得できませんでした」を出す（クリックしても例外にしない）', async () => {
  const { dom, render } = setup('editor', { execCommand: true, execUrl: '' });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const copyButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === 'リンクをコピー');
  assert.doesNotThrow(() => dom.fire(copyButton, 'click'));
  const status = dom.findNode(root, (n) => n.className === 'sw-copylink-status');
  assert.match(status.textContent, /取得できませんでした/);
});

// ---- 緊急修正（2026-09-14）: コピーメニューが全行ぶん開いた状態で表示され続ける不具合 ----

test('一覧の行の「リンクをコピー」メニュー（▼）は既定で非表示で、▼ を押すと開閉できる', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const menu = dom.findNode(root, (n) => n.className === 'sw-copylink-menu');
  assert.ok(menu, 'コピーメニューが存在する');
  assert.equal(menu.hidden, true, '既定では非表示（hidden）');

  const menuButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '▼');
  assert.ok(menuButton);
  dom.fire(menuButton, 'click');
  assert.equal(menu.hidden, false, '▼ を押すと開く');

  dom.fire(menuButton, 'click');
  assert.equal(menu.hidden, true, '再度押すと閉じる');
});

test('コピーメニュー: 外側クリックで閉じる', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const menu = dom.findNode(root, (n) => n.className === 'sw-copylink-menu');
  const menuButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '▼');
  dom.fire(menuButton, 'click');
  assert.equal(menu.hidden, false);

  const outside = dom.document.createElement('div');
  dom.fire(dom.document, 'click', { target: outside });
  assert.equal(menu.hidden, true, '外側クリックで閉じる');
});

test('コピーメニュー: Esc キーで閉じる', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const menu = dom.findNode(root, (n) => n.className === 'sw-copylink-menu');
  const menuButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '▼');
  dom.fire(menuButton, 'click');
  assert.equal(menu.hidden, false);

  dom.fire(dom.document, 'keydown', { key: 'Escape' });
  assert.equal(menu.hidden, true, 'Esc で閉じる');
});

test('コピーメニュー: 項目（URL のみ 等）を選ぶと閉じる', async () => {
  const { dom, render } = setup('editor', { execCommand: true });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const menu = dom.findNode(root, (n) => n.className === 'sw-copylink-menu');
  const menuButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '▼');
  dom.fire(menuButton, 'click');
  assert.equal(menu.hidden, false);

  const urlOnlyItem = dom.findNode(menu, (n) => n.tagName === 'button' && n.textContent === 'URL のみ');
  assert.ok(urlOnlyItem);
  dom.fire(urlOnlyItem, 'click');
  assert.equal(menu.hidden, true, '項目を選ぶと閉じる');
});

test('詳細パネル（編集）のヘッダーにも「リンクをコピー」ボタンが出る（新規作成モードには出ない）', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();
  const copyButtons = dom.findAllNodes(root, (n) => n.tagName === 'button' && n.textContent === 'リンクをコピー');
  assert.equal(copyButtons.length, 2, '一覧の行 + 詳細ヘッダーの2つ');

  const newButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 新規発注');
  dom.fire(newButton, 'click');
  const copyButtonsInCreate = dom.findAllNodes(root, (n) => n.tagName === 'button' && n.textContent === 'リンクをコピー');
  assert.equal(copyButtonsInCreate.length, 1, '新規作成パネルには出ない（一覧の行の分だけ残る）');
});

// ---- O-12: ファイル形式・ファイル名の画面表示 ----

test('詳細パネル: fileFormat/fileName の値が表示され、推奨名ボタンで入力欄に入る', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();

  const fileFormatField = dom.findNode(root, (n) => {
    if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
    var label = (n.children || [])[0];
    return label && label.textContent === 'ファイル形式';
  });
  assert.ok(fileFormatField, 'ファイル形式欄が見つかる');
  assert.equal(fileFormatField.children[1].value, '.wav');

  const fileNameField = dom.findNode(root, (n) => {
    if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
    var label = (n.children || [])[0];
    return label && label.textContent === '納品ファイル名';
  });
  assert.ok(fileNameField, '納品ファイル名欄が見つかる');
  assert.equal(fileNameField.children[1].value, 'SE_Slash.wav');

  const useSuggestedButton = dom.findNode(fileNameField, (n) => n.tagName === 'button' && n.textContent === '推奨名を使う');
  assert.ok(useSuggestedButton, '推奨名（identifier=Slash・fileFormat=.wav → SE_Slash.wav と一致するため既に同じ値だが）ボタンは常に出る');
});

test('詳細パネル: fileName に使えない文字が入っていると非ブロッキングな警告が表示され、保存ボタンは無効化されない', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();

  const fileNameField = dom.findNode(root, (n) => {
    if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
    var label = (n.children || [])[0];
    return label && label.textContent === '納品ファイル名';
  });
  var input = fileNameField.children[1];
  input.value = 'bad/name.wav';
  assert.doesNotThrow(() => dom.fire(input, 'input'));

  const rerendered = dom.findNode(root, (n) => n.className && n.className.indexOf('sw-field-warning-message') !== -1);
  assert.ok(rerendered, '非ブロッキングな警告が表示される');
  assert.match(rerendered.textContent, /使えない文字/);

  const saveButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '保存');
  assert.ok(saveButton, '警告だけでは保存ボタンは無くならない（例外で止めない・ブロックしない方針）');
});

// ---- 緊急修正（2026-09-14）: 1文字入力するごとにフォーカスが外れる不具合（各フィールド網羅） ----

test('詳細パネル: 表示名・カテゴリ・発注者（datalist 付き）・ファイル形式に入力しても、それぞれの <input> ノードは作り直されない', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();

  function findFieldInput(label) {
    const field = dom.findNode(root, (n) => {
      if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
      const l = (n.children || [])[0];
      return l && l.textContent === label;
    });
    return field.children[1];
  }

  const displayNameInput = findFieldInput('表示名');
  const categoryInput = findFieldInput('カテゴリ');
  const ordererInput = findFieldInput('発注者');
  const fileFormatInput = findFieldInput('ファイル形式');

  displayNameInput.value = '斬撃音２';
  categoryInput.value = 'Boss';
  ordererInput.value = 'すずき';
  fileFormatInput.value = '.ogg';
  assert.doesNotThrow(() => dom.fire(displayNameInput, 'input'));
  assert.doesNotThrow(() => dom.fire(categoryInput, 'input'));
  assert.doesNotThrow(() => dom.fire(ordererInput, 'input'));
  assert.doesNotThrow(() => dom.fire(fileFormatInput, 'input'));

  assert.equal(findFieldInput('表示名'), displayNameInput, '表示名の <input> は同じノードのまま');
  assert.equal(findFieldInput('カテゴリ'), categoryInput, 'カテゴリの <input> は同じノードのまま');
  assert.equal(findFieldInput('発注者'), ordererInput, '発注者の <input> は同じノードのまま');
  assert.equal(findFieldInput('ファイル形式'), fileFormatInput, 'ファイル形式の <input> は同じノードのまま');
});

test('詳細パネル: 識別子・カテゴリを変更すると、fileName の <input> は作り直されずに推奨名の表示だけ更新される', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();

  function findFieldWrap(label) {
    return dom.findNode(root, (n) => {
      if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
      const l = (n.children || [])[0];
      return l && l.textContent === label;
    });
  }

  const identifierInput = findFieldWrap('インポート名: 識別子（PascalCase）').children[1];
  const fileNameWrap = findFieldWrap('納品ファイル名');
  const fileNameInput = fileNameWrap.children[1];

  identifierInput.value = 'SlashHeavy';
  dom.fire(identifierInput, 'input');

  // fileName の <input> ノードは同じまま。
  const fileNameWrapAfter = findFieldWrap('納品ファイル名');
  assert.equal(fileNameWrapAfter, fileNameWrap);
  assert.equal(fileNameWrapAfter.children[1], fileNameInput);

  // 推奨名の表示（category=Player を含む SE_Player_Slash.wav → SE_Player_SlashHeavy.wav）は
  // 更新されている。
  const hint = dom.findNode(fileNameWrap, (n) => (n.textContent || '').indexOf('推奨: ') !== -1);
  assert.ok(hint);
  assert.match(hint.textContent, /SE_Player_SlashHeavy\.wav/);
});

// ---- 緊急修正（2026-09-14）: 送信中のボタン無効化・二重送信防止・トースト通知 ----

test('新規発注の保存: 応答が返るまで保存ボタンが「送信中...」になり無効化され、連打しても assets.create は1回だけ呼ばれる', async () => {
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

  const newButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '+ 新規発注');
  dom.fire(newButton, 'click');

  function fillField(label, value) {
    const field = dom.findNode(root, (n) => {
      if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
      const l = (n.children || [])[0];
      return l && l.textContent === label;
    });
    const input = field.children[1];
    input.value = value;
    dom.fire(input, 'input');
  }

  // assetType（種類）は <select>（change イベント）なので value を直接設定して change を発火する。
  const assetTypeField = dom.findNode(root, (n) => {
    if (n.tagName !== 'div' || n.className.indexOf('assets-field') === -1) return false;
    const l = (n.children || [])[0];
    return l && l.textContent === '種類';
  });
  assetTypeField.children[1].value = 'Vfx';
  dom.fire(assetTypeField.children[1], 'change');

  fillField('インポート名: 識別子（PascalCase）', 'FireBall');
  fillField('表示名', '火球');

  const saveButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '保存');
  dom.fire(saveButton, 'click');
  dom.fire(saveButton, 'click'); // 連打
  dom.fire(saveButton, 'click');

  assert.equal(createCalls, 1, 'assets.create は1回だけ呼ばれる（連打しても重複作成しない）');
  assert.equal(saveButton.disabled, true);
  assert.equal(saveButton.textContent, '送信中...');

  resolveCreate({ ok: true, item: sampleAsset({ id: 'Vfx::FireBall', assetType: 'Vfx', identifier: 'FireBall', revision: 1 }) });
  await flush();

  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('作成しました') !== -1);
  assert.ok(toast, '成功トーストが表示される');
});

// ---- 緊急修正（2026-09-14）: 削除機能はあるか？（アーカイブ済みトグル + 元に戻す） ----

test('editor: 詳細の「削除（アーカイブ）」を押すと assets.delete を呼び、一覧からその場で消える（reload なし）。トグルで再表示できる', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();

  const deleteButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '削除（アーカイブ）');
  assert.ok(deleteButton);
  dom.fire(deleteButton, 'click');
  await flush();

  const rowAfter = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  assert.equal(rowAfter, null, '既定（アーカイブ済みを表示しない）では一覧から消える');

  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('削除しました') !== -1);
  assert.ok(toast);

  // 「アーカイブ済みを表示」トグルを有効にすると、アーカイブ済みでも一覧に出る
  // （トグルは「種類でグループ化」の次に追加しているため、チェックボックスの2番目）。
  const checkboxes = dom.findAllNodes(root, (n) => n.tagName === 'input' && n.getAttribute && n.getAttribute('type') === 'checkbox');
  const archivedCheckbox = checkboxes[checkboxes.length - 1];
  archivedCheckbox.checked = true;
  dom.fire(archivedCheckbox, 'change');

  const rowAfterToggle = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  assert.ok(rowAfterToggle, 'アーカイブ済みを表示トグルを有効にすれば一覧に出る');
  assert.ok(
    dom.findNode(rowAfterToggle, (n) => (n.textContent || '').indexOf('アーカイブ済み') !== -1),
    'アーカイブ済みであることが状態列に表示される'
  );
});

test('editor: アーカイブ済みの発注の詳細には「元に戻す」ボタンが出て、押すと assets.restore を呼ぶ', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  // 詳細を開いて削除 → アーカイブ済みトグルを有効化 → 再度詳細を開くと「元に戻す」が出る。
  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();
  const deleteButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '削除（アーカイブ）');
  dom.fire(deleteButton, 'click');
  await flush();

  const checkboxes = dom.findAllNodes(root, (n) => n.tagName === 'input' && n.getAttribute && n.getAttribute('type') === 'checkbox');
  const archivedCheckbox = checkboxes[checkboxes.length - 1];
  archivedCheckbox.checked = true;
  dom.fire(archivedCheckbox, 'change');

  const archivedRow = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  assert.ok(archivedRow, 'アーカイブ済みでも一覧に出ている');
  dom.fire(archivedRow, 'click');
  await flush();

  const restoreButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '元に戻す');
  assert.ok(restoreButton, 'アーカイブ済みの詳細には「元に戻す」ボタンが出る');
  const deleteButtonShouldBeGone = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '削除（アーカイブ）');
  assert.equal(deleteButtonShouldBeGone, null, 'アーカイブ済みのときは「削除（アーカイブ）」ではなく「元に戻す」だけ出る');

  dom.fire(restoreButton, 'click');
  await flush();

  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('元に戻しました') !== -1);
  assert.ok(toast);
});

test('viewer: アーカイブ済みを表示トグルはあるが、削除・元に戻すボタンは出ない', async () => {
  const { dom, render } = setup('viewer');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const row = dom.findNode(root, (n) => n.tagName === 'tr' && n.className !== 'assets-group-row' && (n.children || []).some((td) => td.textContent === 'Slash'));
  dom.fire(row, 'click');
  await flush();

  const deleteButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '削除（アーカイブ）');
  const restoreButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '元に戻す');
  assert.equal(deleteButton, null);
  assert.equal(restoreButton, null);
});
