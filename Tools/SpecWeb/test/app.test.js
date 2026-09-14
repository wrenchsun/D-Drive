'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');
const { createFakeDom } = require('./dom-stub.js');

// 2026-09-14 追補 AC（実デプロイで判明した誤りの修正、docs/32_spec_web.md §2.4 訂正）:
// html/App.html は google.script.run 経由でサーバーを呼び、画面遷移は iframe 内で
// window.SpecWebNavigate により完結する（location.hash/hashchange には依存しない）。

function createFakeGoogleScript(overrides) {
  var runCalls = [];
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
    specWebUiCall: function (name, params) {
      runCalls.push({ name: name, params: params });
      var behavior = (overrides && overrides.uiCall) || function () { return { ok: true }; };
      var result = behavior(name, params);
      if (result && result.__throw) {
        if (run._failureHandler) run._failureHandler(new Error(result.__throw));
      } else if (run._successHandler) {
        run._successHandler(result);
      }
    }
  };

  var history = {
    push: function (state, params, hash) {
      historyCalls.push({ method: 'push', state: state, hash: hash });
    },
    replace: function (state, params, hash) {
      historyCalls.push({ method: 'replace', state: state, hash: hash });
    },
    setChangeHandler: function (fn) {
      changeHandler = fn;
    }
  };

  return {
    google: { script: { run: run, history: history } },
    runCalls: runCalls,
    historyCalls: historyCalls,
    fireHistoryChange: function (state) {
      if (changeHandler) changeHandler({ state: state, location: { parameters: {}, hash: (state && state.screen) || '' } });
    }
  };
}

function setup(googleOverrides) {
  const dom = createFakeDom();
  var appRoot = dom.document.createElement('div');
  var appNav = dom.document.createElement('nav');
  dom.document.getElementById = function (id) {
    if (id === 'app-root') return appRoot;
    if (id === 'app-nav') return appNav;
    return null;
  };

  const fakeGoogle = createFakeGoogleScript(googleOverrides);
  var domContentLoadedHandlers = [];
  const sandbox = {
    console: console,
    document: dom.document,
    google: fakeGoogle.google,
    window: {
      google: fakeGoogle.google,
      addEventListener: function (type, handler) {
        if (type === 'DOMContentLoaded') domContentLoadedHandlers.push(handler);
      }
    }
  };

  const ctx = loadHtmlScript('App', sandbox);

  return {
    ctx: ctx,
    dom: dom,
    appRoot: appRoot,
    appNav: appNav,
    fakeGoogle: fakeGoogle,
    fireDomContentLoaded: function () {
      domContentLoadedHandlers.forEach(function (h) { h(); });
    }
  };
}

test('registerScreen + SpecWebNavigate: 画面を切り替えると root が再構築され、google.script.history.push が呼ばれる', () => {
  const { ctx, appRoot, fakeGoogle } = setup();
  var renderedA = 0, renderedB = 0;
  ctx.window.registerScreen('orders', function (root) {
    renderedA++;
    root.textContent = 'orders-screen';
  });
  ctx.window.registerScreen('assets', function (root) {
    renderedB++;
    root.textContent = 'assets-screen';
  });

  ctx.window.SpecWebNavigate('assets');
  assert.equal(renderedB, 1);
  assert.equal(appRoot.textContent, 'assets-screen');
  assert.equal(fakeGoogle.historyCalls.length, 1);
  assert.equal(fakeGoogle.historyCalls[0].hash, 'assets');
});

test('未登録の画面 id に遷移しようとすると既定画面（orders）にフォールバックする', () => {
  const { ctx, appRoot } = setup();
  ctx.window.registerScreen('orders', function (root) {
    root.textContent = 'orders-screen';
  });
  ctx.window.SpecWebNavigate('no-such-screen');
  assert.equal(appRoot.textContent, 'orders-screen');
});

test('DOMContentLoaded で既定画面（orders）が初期表示される', () => {
  const { ctx, appRoot, fireDomContentLoaded } = setup();
  ctx.window.registerScreen('orders', function (root) {
    root.textContent = 'orders-screen';
  });
  fireDomContentLoaded();
  assert.equal(appRoot.textContent, 'orders-screen');
});

test('google.script.history.setChangeHandler（戻る/進む相当）で画面が切り替わり、push は呼ばれない', () => {
  const { ctx, appRoot, fakeGoogle } = setup();
  ctx.window.registerScreen('orders', function (root) { root.textContent = 'orders-screen'; });
  ctx.window.registerScreen('members', function (root) { root.textContent = 'members-screen'; });

  const beforeCalls = fakeGoogle.historyCalls.length;
  fakeGoogle.fireHistoryChange({ screen: 'members' });
  assert.equal(appRoot.textContent, 'members-screen');
  assert.equal(fakeGoogle.historyCalls.length, beforeCalls, 'setChangeHandler からの遷移は履歴を積み直さない');
});

test('SpecWebClient.callApi: google.script.run.specWebUiCall を呼び、成功結果で resolve する', async () => {
  const { ctx } = setup({
    uiCall: function (name, params) {
      assert.equal(name, 'assets.list');
      assert.deepEqual(params, { includeArchived: '0' });
      return { ok: true, items: [{ id: 'Se::Slash' }] };
    }
  });
  const result = await ctx.window.SpecWebClient.callApi('assets.list', { includeArchived: '0' });
  assert.equal(result.ok, true);
  assert.equal(result.items[0].id, 'Se::Slash');
});

test('SpecWebClient.callApi: withFailureHandler 経路は ok:false で resolve する（例外にしない）', async () => {
  const { ctx } = setup({
    uiCall: function () {
      return { __throw: 'サーバーエラー' };
    }
  });
  const result = await ctx.window.SpecWebClient.callApi('ping', {});
  assert.equal(result.ok, false);
  assert.match(result.error, /サーバーエラー/);
});

test('SpecWebClient.callApi: google.script.run が無い環境（テスト・ローカル開発）でも例外を投げず ok:false で resolve する', async () => {
  const dom = createFakeDom();
  const appRoot = dom.document.createElement('div');
  dom.document.getElementById = function (id) {
    return id === 'app-root' ? appRoot : null;
  };
  const sandbox = { console: console, document: dom.document, window: { addEventListener: function () {} } };
  const ctx = loadHtmlScript('App', sandbox);
  const result = await ctx.window.SpecWebClient.callApi('ping', {});
  assert.equal(result.ok, false);
  assert.match(result.error, /google\.script\.run/);
});
