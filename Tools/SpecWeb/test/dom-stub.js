'use strict';

/**
 * Assets.html のような DOM 操作を含む画面コードを、npm install なしで軽くスモークテストする
 * ための最小限の DOM フェイク。jsdom 等は依存ゼロ方針（docs/32_spec_web.md「テストの流儀」）
 * に反するため使わず、このファイルで最低限必要な API だけを自作する。
 *
 * 目的は「実 DOM と見た目まで一致すること」の検証ではなく、「画面コードが例外を投げずに
 * 一通り動くこと」（未定義参照・タイポ等の smoke test）の確認。
 */

function createFakeDom() {
  function FakeNode(tag) {
    this.tagName = tag;
    this.className = '';
    this.textContent = '';
    this._innerHTML = '';
    this.children = [];
    this._attrs = {};
    this._listeners = {};
    this.style = {};
    this.value = '';
    this.disabled = false;
    this.hidden = false;
  }

  FakeNode.prototype.setAttribute = function (name, value) {
    this._attrs[name] = value;
  };

  FakeNode.prototype.getAttribute = function (name) {
    return Object.prototype.hasOwnProperty.call(this._attrs, name) ? this._attrs[name] : null;
  };

  // O-13: ClipboardCopy.html の execCommand フォールバックが textarea.select() を呼ぶ
  // （見た目の選択自体は検証しない。呼んでも例外にならないことが目的）。
  FakeNode.prototype.select = function () {};

  FakeNode.prototype.appendChild = function (child) {
    if (child) this.children.push(child);
    return child;
  };

  FakeNode.prototype.removeChild = function (child) {
    var index = this.children.indexOf(child);
    if (index !== -1) this.children.splice(index, 1);
    return child;
  };

  FakeNode.prototype.addEventListener = function (type, handler) {
    if (!this._listeners[type]) this._listeners[type] = [];
    this._listeners[type].push(handler);
  };

  // Assets.html は querySelector を「ナビゲーションに既にリンクが有るか」の確認にしか
  // 使っておらず、getElementById が null を返す限り呼ばれない。念のため空実装だけ用意する。
  FakeNode.prototype.querySelector = function () {
    return null;
  };

  Object.defineProperty(FakeNode.prototype, 'innerHTML', {
    get: function () {
      return this._innerHTML;
    },
    set: function (value) {
      this._innerHTML = value;
      // Assets.html は innerHTML = '' を「子要素をクリアする」意図でしか使っていない。
      if (value === '') {
        this.children = [];
      }
    }
  });

  function fire(node, type, event) {
    var handlers = (node._listeners && node._listeners[type]) || [];
    handlers.forEach(function (handler) {
      handler(event || {});
    });
  }

  /** children を再帰的に辿って、条件に合う最初のノードを返す（テストのアサート用）。 */
  function findNode(root, predicate) {
    if (!root) return null;
    if (predicate(root)) return root;
    for (var i = 0; i < (root.children || []).length; i++) {
      var found = findNode(root.children[i], predicate);
      if (found) return found;
    }
    return null;
  }

  function findAllNodes(root, predicate) {
    var result = [];
    (function walk(node) {
      if (!node) return;
      if (predicate(node)) result.push(node);
      (node.children || []).forEach(walk);
    })(root);
    return result;
  }

  // O-13: ClipboardCopy.html の execCommand フォールバックが document.body.appendChild/removeChild と
  // document.execCommand('copy') を呼ぶ。既定では execCommand は無い（未対応環境と同じ =
  // undefined、呼び出し側は typeof でガードしている）ので、成功させたいテストは
  // dom.document.execCommand = function () { return true; }; のように上書きする。
  var body = new FakeNode('body');
  var document = {
    body: body,
    createElement: function (tag) {
      return new FakeNode(tag);
    },
    createTextNode: function (text) {
      var node = new FakeNode('#text');
      node.textContent = text;
      return node;
    },
    getElementById: function () {
      return null;
    }
  };

  return { document: document, fire: fire, findNode: findNode, findAllNodes: findAllNodes, FakeNode: FakeNode };
}

/** callApi(name, params, options) の呼び出しに対して、name ごとの応答を返すフェイク。 */
function createFakeSpecWebClient(handlers) {
  return {
    callApi: function (name, params, options) {
      var handler = handlers[name];
      var result = typeof handler === 'function' ? handler(params, options) : handler || { ok: true };
      return Promise.resolve(result);
    }
  };
}

/** 保留中の Promise の .then コールバックを実行させるための一呼吸（マイクロタスクを flush する）。 */
function flush() {
  return new Promise(function (resolve) {
    setTimeout(resolve, 0);
  });
}

module.exports = { createFakeDom, createFakeSpecWebClient, flush };
