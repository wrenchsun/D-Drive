'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createFakeDom, flush } = require('./dom-stub.js');

const HTML_ROOT = path.join(__dirname, '..', 'html');
const SCRIPT_TAG_PATTERN = /<script>([\s\S]*?)<\/script>/g;

/**
 * test/load-html-script.js の loadHtmlScripts は `window` を毎回新しい空オブジェクトとして
 * サンドボックスに合成する（Object.assign での浅いコピー）ため、呼び出し側が
 * `sandbox.window = sandbox`（自己参照）を仕込んでも合成後には失われてしまう。
 *
 * この統合テストは「実ブラウザでは window === globalThis」という前提
 * （html/Assets.html・html/OrderTree.html が `registerScreen(...)` を bare 識別子で呼ぶことに依存）
 * を再現する必要があるため、ここだけ専用の小さいローダーを使う（loadHtmlScripts を汚さない）。
 */
function loadHtmlScriptsWithRealWindow(fileBaseNames, sandbox) {
  sandbox.window = sandbox; // 実ブラウザの window === globalThis を再現する。
  const context = vm.createContext(sandbox);
  fileBaseNames.forEach(function (fileBaseName) {
    const filePath = path.join(HTML_ROOT, fileBaseName + '.html');
    const html = fs.readFileSync(filePath, 'utf8');
    let match;
    SCRIPT_TAG_PATTERN.lastIndex = 0;
    while ((match = SCRIPT_TAG_PATTERN.exec(html))) {
      vm.runInContext(match[1], context, { filename: filePath });
    }
  });
  return context;
}

/**
 * O-15 追補（2026-09-14、緊急修正）: 「編集を押しても一覧へ飛ぶだけで編集できない」の再現・修正確認。
 *
 * 既存の *.smoke.test.js は各画面モジュール（Assets.html/OrderTree.html）を
 * `registerScreen` をスタブして単体で呼ぶだけで、html/App.html が持つ実際の画面遷移
 * （window.SpecWebNavigate → renderScreen → render(root, params)）を経由していなかった。
 * そのため「ノードのテストは通るのに実画面では動かない」を検出できなかった
 * （このファイルの目的そのもの）。
 *
 * ここでは html/App.html を含む実際の <script> をすべて 1 つの vm コンテキストに読み込み、
 * OrderTree.html の「編集」ボタンのクリック→window.SpecWebNavigate→Assets.html の詳細パネルが
 * 開くところまでを、本物の関数を通して確認する。
 */

function createFakeGoogleScript(apiHandlers) {
  var historyCalls = [];
  var changeHandler = null;

  var run = {
    _successHandler: null,
    _failureHandler: null,
    withSuccessHandler: function (fn) {
      run._successHandler = fn;
      return run;
    },
    withFailureHandler: function (fn) {
      run._failureHandler = fn;
      return run;
    },
    // 重要: OrderTree.html/Assets.html の reload() は Promise.all で複数の callApi() を
    // 同時に呼ぶ（例: orderGroups.list・assets.list・settings.get・whoami を並行呼び出し）。
    // withSuccessHandler/withFailureHandler は同じ `run` を共有する chainable な API のため、
    // 「今の handler」を非同期（Promise.resolve().then）で読みに行くと、その前に次の
    // callApi() 呼び出しが handler を上書きしてしまい、別の呼び出しの結果が別の呼び出しの
    // 成功ハンドラに届く（実 GAS では 1 文＝1 回の紐付けなので起きない、この fake 特有の
    // 問題）。specWebUiCall の実行時点（同期）で handler を確定させることで防ぐ。
    specWebUiCall: function (name, params) {
      var handler = apiHandlers[name];
      var successHandler = run._successHandler;
      var failureHandler = run._failureHandler;
      if (!handler) {
        if (failureHandler) failureHandler(new Error('未対応の API: ' + name));
        return;
      }
      var result;
      try {
        result = handler(params);
      } catch (err) {
        if (failureHandler) failureHandler(err);
        return;
      }
      Promise.resolve(result).then(
        function (res) {
          if (successHandler) successHandler(res);
        },
        function (err) {
          if (failureHandler) failureHandler(err);
        }
      );
    }
  };

  var history = {
    push: function (state, urlParams, hash) {
      historyCalls.push({ method: 'push', state: state, hash: hash });
    },
    replace: function (state, urlParams, hash) {
      historyCalls.push({ method: 'replace', state: state, hash: hash });
    },
    setChangeHandler: function (fn) {
      changeHandler = fn;
    }
  };

  return {
    google: { script: { run: run, history: history } },
    historyCalls: historyCalls,
    fireHistoryChange: function (state) {
      if (changeHandler) changeHandler({ state: state, location: { parameters: {}, hash: (state && state.screen) || '' } });
    }
  };
}

function setup(role) {
  const dom = createFakeDom();
  const appRoot = dom.document.createElement('div');
  const appNav = dom.document.createElement('nav');
  dom.document.getElementById = function (id) {
    if (id === 'app-root') return appRoot;
    if (id === 'app-nav') return appNav;
    return null;
  };

  const group = { id: 'og_1', name: 'スキル: 斬撃', wbsNo: '3.2.1', revision: 1 };
  const asset = {
    id: 'Se::Hit', assetType: 'Se', identifier: 'Hit', displayName: '斬撃音',
    category: '', status: '発注済', orderer: 'よしだ', contractor: 'たなか',
    orderDate: '', dueDate: '', deliveredDate: '', priority: '', referenceMd: '',
    parentId: 'og_1', fileFormat: '', fileName: '', archived: false, revision: 1,
    comments: [], ddriveState: null, params: null
  };

  const apiHandlers = {
    whoami: function () {
      return { ok: true, role: role, email: role + '@example.com', displayName: role };
    },
    'orderGroups.list': function () {
      return { ok: true, items: [group] };
    },
    'assets.list': function () {
      return { ok: true, items: [asset] };
    },
    'settings.get': function () {
      return { ok: true, ganttUrl: '' };
    },
    'members.list': function () {
      return { ok: true, items: [] };
    },
    'assets.get': function () {
      return { ok: true, item: asset };
    },
    'paramSchemas.list': function () {
      return { ok: true, items: [] };
    }
  };

  const fakeGoogle = createFakeGoogleScript(apiHandlers);
  const domContentLoadedHandlers = [];

  // 重要: 実ブラウザでは `window === globalThis` であり、html/Assets.html・html/OrderTree.html は
  // `registerScreen(...)`（bare 識別子）を呼ぶ（`window.registerScreen` ではなく、実ブラウザでは
  // どちらでも同じものを指す）。Node の vm.createContext ではこの2つは既定では別物になるため、
  // `window` をサンドボックス自身（= vm コンテキストのグローバルオブジェクト）にして
  // 「window.foo = ...」が bare な「foo」からも見えるようにする（実ブラウザの挙動の再現）。
  const sandbox = {
    console: console,
    setTimeout: setTimeout,
    clearTimeout: clearTimeout,
    document: dom.document,
    google: fakeGoogle.google,
    confirm: function () { return true; },
    alert: function () {},
    SpecWebExecUrl: 'https://script.google.com/macros/s/fake/exec',
    addEventListener: function (type, handler) {
      if (type === 'DOMContentLoaded') domContentLoadedHandlers.push(handler);
    }
  };

  // 実 Index.html と同じ順序（docs/32_spec_web.md の include 順）で読み込む。
  const ctx = loadHtmlScriptsWithRealWindow(
    ['App', 'OrderLinkLogic', 'ClipboardCopy', 'UiFeedback', 'MarkdownToolbar', 'AssetsLogic', 'Assets', 'OrderTreeLogic', 'OrderTree'],
    sandbox
  );

  return {
    ctx: ctx,
    dom: dom,
    appRoot: appRoot,
    fakeGoogle: fakeGoogle,
    fireDomContentLoaded: function () {
      domContentLoadedHandlers.forEach(function (h) { h(); });
    }
  };
}

test('発注ツリーの「編集」を押すと、画面を離れずに一覧画面へ遷移し詳細パネルが開く（実際の画面遷移経由）', async () => {
  const { dom, appRoot, fireDomContentLoaded } = setup('editor');

  // DEFAULT_SCREEN_ID は 'orders'（発注ツリー）。
  fireDomContentLoaded();
  await flush();
  await flush();

  // '編集' ボタンは発注グループ自体の編集(renderGroupEditForm)と、各発注の編集(renderOrderItem)の
  // 2種類がある。このテストのサンプルはグループ1件・発注1件なので、renderOrderItem が
  // 描画する（発注の行の）ものは DOM 構築順で最後の '編集' ボタンになる。
  const editButtons = dom.findAllNodes(appRoot, (n) => n.tagName === 'button' && n.textContent === '編集');
  assert.ok(editButtons.length >= 1, '発注ツリーに「編集」ボタンが表示される');
  const editButton = editButtons[editButtons.length - 1];

  assert.doesNotThrow(() => dom.fire(editButton, 'click'));

  // Assets.html の reload()（3 API）→ maybeOpenPending → openDetail の assets.get 再取得、
  // という複数段の非同期を待つ。
  await flush();
  await flush();
  await flush();
  await flush();

  const heading = dom.findNode(appRoot, (n) => n.tagName === 'h2');
  assert.ok(heading, '詳細パネルの見出し(h2)が表示される');
  assert.equal(heading.textContent, 'Se :: Hit', '編集ボタンを押した発注の詳細パネルが開く');

  // 緊急修正（2026-09-14 追補）: 発注ツリーから来た場合は「← 発注ツリーへ戻る」ボタンが出て、
  // 押すと（画面遷移を経由して）発注ツリーの画面に戻る。
  const backButton = dom.findNode(appRoot, (n) => n.tagName === 'button' && n.textContent === '← 発注ツリーへ戻る');
  assert.ok(backButton, '「← 発注ツリーへ戻る」ボタンが出る');

  dom.fire(backButton, 'click');
  await flush();
  await flush();

  const headingAfterBack = dom.findNode(appRoot, (n) => n.tagName === 'h2');
  assert.equal(headingAfterBack, null, '発注ツリーに戻ると詳細パネルの見出しは無くなる');
  const editButtonsAfterBack = dom.findAllNodes(appRoot, (n) => n.tagName === 'button' && n.textContent === '編集');
  assert.ok(editButtonsAfterBack.length >= 1, '発注ツリーの画面に戻っている（編集ボタンが再度見える）');
});
