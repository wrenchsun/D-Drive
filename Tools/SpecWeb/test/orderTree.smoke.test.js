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

  const group = Object.assign({ id: 'og_1', name: 'スキル: 斬撃', wbsNo: '3.2.1', revision: 1 }, options.groupOverrides);
  const asset = Object.assign({
    id: 'Se::Hit', assetType: 'Se', identifier: 'Hit', displayName: '斬撃音',
    status: '納品済', orderer: 'よしだ', contractor: 'たなか', parentId: 'og_1', archived: false
  }, options.assetOverrides);

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
    },
    // O-15: orderGroups.update の既定フェイク（patch をそのまま反映して revision を進める）。
    'orderGroups.update': options.updateGroupHandler || function (params) {
      const patch = JSON.parse(params.patch);
      return { ok: true, item: Object.assign({}, group, patch, { revision: group.revision + 1 }) };
    },
    // 緊急修正（2026-09-14 追補）: 発注ツリーの行から直接削除（アーカイブ）するテスト用。
    'assets.delete': options.assetDeleteHandler || function (params) {
      return { ok: true, item: Object.assign({}, asset, { id: params.id, archived: true, revision: (asset.revision || 1) + 1 }) };
    }
  });
  sandbox.window.SpecWebClient = fakeClient;
  sandbox.window.confirm = options.confirm !== undefined ? options.confirm : function () { return true; };

  // O-15: 「編集」ボタンが呼ぶ画面遷移（html/App.html の window.SpecWebNavigate）のスパイ。
  // 実アプリでは常に存在するが、この画面コード単体のスモークテストの sandbox には無いため、
  // OrderTree.html 側は typeof で存在確認してから呼ぶ（このテストでは呼び出しを記録する）。
  const navigateCalls = [];
  sandbox.window.SpecWebNavigate = function (id, navOptions) {
    navigateCalls.push({ id: id, options: navOptions });
  };

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
  return { ctx, dom, render: capturedRender, navigateCalls: navigateCalls };
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

  // O-15: 発注名は li 直下の span に入る（li 自体には「編集」ボタン等も並ぶため）。
  const item = dom.findNode(root, (n) => n.tagName === 'li' && dom.findNode(n, (c) => (c.textContent || '').indexOf('Hit') !== -1));
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

// ---- O-15: 各発注への「一覧で開く」導線（発注ツリーには詳細パネルが無いため、一覧画面へ
// 遷移する。2026-09-15 三度目の修正で「編集」から改名し、識別子で一覧を絞り込む q を追加した） ----

test('editor: 発注の行に「一覧で開く」ボタンが出て、押すと SpecWebNavigate("assets", {params:{q, openId, backTo}}) が呼ばれる', async () => {
  const { dom, render, navigateCalls } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const editButton = dom.findNode(root, (n) => n.tagName === 'li' && dom.findNode(n, (c) => c.tagName === 'button' && c.textContent === '一覧で開く'));
  assert.ok(editButton, '発注の行に「一覧で開く」ボタンが出る');
  const button = dom.findNode(editButton, (n) => n.tagName === 'button' && n.textContent === '一覧で開く');
  dom.fire(button, 'click');
  // 2026-09-15 二度目の修正: 画面遷移は setTimeout(…, 0) でこのクリックの処理が完全に
  // 終わった後に行うようにした（docs/32_spec_web.md 参照）。
  await flush();

  assert.equal(navigateCalls.length, 1);
  assert.equal(navigateCalls[0].id, 'assets');
  // 2026-09-15 三度目の修正: 検索欄をその発注の識別子で絞り込むための q を渡す
  // （画面をまたぐ自動オープンが実デプロイで不安定だったための変更、docs/32_spec_web.md 参照）。
  assert.equal(navigateCalls[0].options.params.q, 'Hit');
  // openId/backTo は「できたら自動で開く」ための保険として引き続き渡す。
  assert.equal(navigateCalls[0].options.params.openId, 'Se::Hit');
  // 緊急修正（2026-09-14 追補）: 戻り先（この画面）を backTo として渡す
  // （一覧画面の詳細パネルに「← 発注ツリーへ戻る」を出すため）。
  assert.equal(navigateCalls[0].options.params.backTo, 'orders');
});

test('viewer: 発注の行に「一覧で開く」ボタンが出ない', async () => {
  const { dom, render } = setup('viewer');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const editButton = dom.findNode(root, (n) => n.tagName === 'li' && dom.findNode(n, (c) => c.tagName === 'button' && c.textContent === '一覧で開く'));
  assert.equal(editButton, null, 'viewer には「一覧で開く」ボタンが出ない');
});

// ---- 2026-09-15 三度目の修正: 「編集は一覧の各行の『編集』から」の案内 ----

test('editor: 「編集は一覧の各行の『編集』から行えます。」という案内が出る', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const hint = dom.findNode(root, (n) => n.tagName === 'p' && (n.textContent || '').indexOf('編集は一覧の各行の「編集」から') !== -1);
  assert.ok(hint, 'editor には編集導線の案内が出る');
});

test('viewer: 編集導線の案内が出ない（viewer は編集できないため）', async () => {
  const { dom, render } = setup('viewer');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const hint = dom.findNode(root, (n) => n.tagName === 'p' && (n.textContent || '').indexOf('編集は一覧の各行の「編集」から') !== -1);
  assert.equal(hint, null, 'viewer には案内が出ない');
});

// ---- 緊急修正（2026-09-14 追補）: 発注ツリーの行から直接削除（アーカイブ） ----

test('editor: 発注の行に「削除」ボタンが出て、押すと確認→assets.delete→その場でツリーから消える', async () => {
  const deleteCalls = [];
  const { dom, render } = setup('editor', {
    assetDeleteHandler: function (params) {
      deleteCalls.push(params);
      return { ok: true, item: Object.assign({}, params, { archived: true }) };
    }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const itemLi = dom.findNode(root, (n) => n.tagName === 'li' && dom.findNode(n, (c) => (c.textContent || '').indexOf('Hit') !== -1));
  assert.ok(itemLi);
  const deleteButton = dom.findNode(itemLi, (n) => n.tagName === 'button' && n.textContent === '削除');
  assert.ok(deleteButton, '発注の行に「削除」ボタンが出る');

  dom.fire(deleteButton, 'click');
  await flush();

  assert.equal(deleteCalls.length, 1, 'assets.delete が呼ばれる');
  assert.equal(deleteCalls[0].id, 'Se::Hit');

  const itemLiAfter = dom.findNode(root, (n) => n.tagName === 'li' && dom.findNode(n, (c) => (c.textContent || '').indexOf('Hit') !== -1));
  assert.equal(itemLiAfter, null, '削除後はツリーからその場で消える');

  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('削除しました') !== -1);
  assert.ok(toast);
});

test('viewer: 発注の行に「削除」ボタンが出ない', async () => {
  const { dom, render } = setup('viewer');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const itemLi = dom.findNode(root, (n) => n.tagName === 'li' && dom.findNode(n, (c) => (c.textContent || '').indexOf('Hit') !== -1));
  const deleteButton = dom.findNode(itemLi, (n) => n.tagName === 'button' && n.textContent === '削除');
  assert.equal(deleteButton, null, 'viewer には削除ボタンが出ない');
});

// ---- O-16: 発注ツリーの各発注にも「メモあり」アイコン + 展開表示 ----

test('メモが入力済みの発注には 📝 アイコンが出て、押すと整形表示が展開される（メモが無ければ出ない）', async () => {
  const { dom, render } = setup('editor', {
    assetOverrides: { referenceMd: '参考: [動画](https://example.com/video)' }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const memoButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '📝');
  assert.ok(memoButton, 'メモが入力済みの発注には 📝 アイコンが出る');
  dom.fire(memoButton, 'click');

  const memoBox = dom.findNode(root, (n) => n.className === 'assets-md-preview assets-memo-clip');
  assert.ok(memoBox, '押すと整形表示が展開される');
  assert.match(memoBox.innerHTML, /example\.com\/video/);
});

test('メモが無い発注には 📝 アイコンが出ない', async () => {
  const { dom, render } = setup('editor');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const memoButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '📝');
  assert.equal(memoButton, null);
});

// ---- O-15: 発注グループの編集（orderGroups.update、既存 API を使う） ----

test('editor: グループヘッダーの「編集」を押すと編集フォームが開き、保存すると orderGroups.update が呼ばれて反映される', async () => {
  let updateCalledWith = null;
  const { dom, render } = setup('editor', {
    updateGroupHandler: function (params) {
      updateCalledWith = params;
      const patch = JSON.parse(params.patch);
      return { ok: true, item: Object.assign({ id: 'og_1', revision: 2 }, patch) };
    }
  });
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const editGroupButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '編集');
  assert.ok(editGroupButton, 'グループヘッダーに「編集」ボタンが出る');
  dom.fire(editGroupButton, 'click');

  const nameField = dom.findNode(root, (n) => {
    if (n.tagName !== 'div') return false;
    const label = (n.children || [])[0];
    return label && label.tagName === 'label' && label.textContent === '名前';
  });
  assert.ok(nameField, '編集フォームの「名前」欄が出る');
  const nameInput = nameField.children[1];
  nameInput.value = 'スキル: 斬撃（改）';
  dom.fire(nameInput, 'input');

  const saveButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '保存');
  dom.fire(saveButton, 'click');
  await flush();

  assert.ok(updateCalledWith, 'orderGroups.update が呼ばれる');
  assert.equal(updateCalledWith.id, 'og_1');
  assert.equal(JSON.parse(updateCalledWith.patch).name, 'スキル: 斬撃（改）');

  const heading = dom.findNode(root, (n) => n.tagName === 'span' && n.textContent === 'スキル: 斬撃（改）');
  assert.ok(heading, '保存後は一覧のヘッダーがその場で更新される');

  const toast = dom.findNode(dom.document.body, (n) => (n.textContent || '').indexOf('保存しました') !== -1);
  assert.ok(toast);
});

test('viewer: グループヘッダーに「編集」ボタンが出ない', async () => {
  const { dom, render } = setup('viewer');
  const root = dom.document.createElement('div');
  render(root);
  await flush();

  const editGroupButton = dom.findNode(root, (n) => n.tagName === 'button' && n.textContent === '編集');
  assert.equal(editGroupButton, null);
});
