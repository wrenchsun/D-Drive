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

  // 実イベント伝播の再現（下記 fire 参照）に親を辿れる必要があるため、appendChild/removeChild/
  // innerHTML='' のどれでも parentNode を維持する（実 DOM の Node.parentNode と同じ役割）。
  // 2026-09-15 修正: 以前は null/undefined を黙って無視していたため、実ブラウザでは
  // 「TypeError: Failed to execute 'appendChild' on 'Node': parameter 1 is not of type 'Node'」で
  // 詳細パネルが開かなかった不具合（Assets.html の renderParams が null を返す）を見逃した。
  // 実 DOM と同じく Node 以外を渡したら例外にする。
  FakeNode.prototype.appendChild = function (child) {
    if (!child || typeof child !== 'object') {
      throw new TypeError("Failed to execute 'appendChild' on 'Node': parameter 1 is not of type 'Node'.");
    }
    this.children.push(child);
    child.parentNode = this;
    return child;
  };

  FakeNode.prototype.removeChild = function (child) {
    var index = this.children.indexOf(child);
    if (index !== -1) this.children.splice(index, 1);
    if (child) child.parentNode = null;
    return child;
  };

  FakeNode.prototype.addEventListener = function (type, handler) {
    if (!this._listeners[type]) this._listeners[type] = [];
    this._listeners[type].push(handler);
  };

  // 緊急修正（2026-09-14、コピーメニューの外側クリック/Esc で閉じる対応）:
  // buildCopyLinkControl() が開いている間だけ document に click/keydown を張り、
  // 閉じるときに外す。addEventListener の対になる最小実装。
  FakeNode.prototype.removeEventListener = function (type, handler) {
    var handlers = this._listeners[type];
    if (!handlers) return;
    var index = handlers.indexOf(handler);
    if (index !== -1) handlers.splice(index, 1);
  };

  // 同上: 「クリックされた場所がこの要素の中かどうか」の判定に使う（outside-click 判定）。
  FakeNode.prototype.contains = function (node) {
    if (node === this) return true;
    for (var i = 0; i < this.children.length; i++) {
      var child = this.children[i];
      if (child === node) return true;
      if (child.contains && child.contains(node)) return true;
    }
    return false;
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
        // 実 DOM 同様、取り除かれた子の parentNode も外す（イベント伝播の経路には残さない）。
        this.children.forEach(function (child) { if (child) child.parentNode = null; });
        this.children = [];
      }
    }
  });

  /**
   * 緊急修正（2026-09-14 二度目、docs/32_spec_web.md 参照）: 実ブラウザのクリックイベントは
   * target から document まで bubble し、bubble の途中で document に登録された listener
   * （リンクコピー▼メニューの外側クリック判定等）も同じクリックで呼ばれる。
   * 以前の実装は node 自身の listener しか呼んでおらず、この伝播が再現できていなかった
   * （「編集」ボタン→画面遷移→document リスナーが同じクリックで発火し得る、という
   * 実際の不具合の型をテストが見逃していた原因）。
   *
   * ここでは伝播経路（target→…→parentNode…→document）を dispatch 開始時点で確定してから
   * 各ノードの「その時点で登録されている」listener を順に呼ぶ（stopPropagation で打ち切る）。
   * 実 DOM も伝播経路はディスパッチ開始時に確定するため、ハンドラの中で要素を DOM から
   * 取り除いても（root.innerHTML = '' 等）、既に確定した経路への伝播は止まらない
   * （実際に起きていた不具合の型 = 古い画面の document リスナーが残っていると誤発火し得る、
   * を再現するために重要な挙動）。
   */
  function composedPath(node) {
    var path = [];
    var cur = node;
    while (cur) {
      path.push(cur);
      cur = cur.parentNode || null;
    }
    path.push(document); // 実 DOM の body/html を経て最終的に document まで bubble する分の代表。
    return path;
  }

  function fire(node, type, event) {
    event = event || {};
    var stopped = false;
    if (typeof event.stopPropagation !== 'function') {
      event.stopPropagation = function () { stopped = true; };
    } else {
      var original = event.stopPropagation;
      event.stopPropagation = function () {
        stopped = true;
        original.call(event);
      };
    }
    if (typeof event.preventDefault !== 'function') {
      event.preventDefault = function () {};
    }
    if (event.target === undefined) event.target = node;

    var path = composedPath(node);
    for (var i = 0; i < path.length && !stopped; i++) {
      var current = path[i];
      var handlers = ((current._listeners && current._listeners[type]) || []).slice();
      for (var j = 0; j < handlers.length && !stopped; j++) {
        handlers[j](event);
      }
    }
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
  // 緊急修正（2026-09-14）: buildCopyLinkControl() の外側クリック/Esc 判定が
  // document.addEventListener('click'|'keydown', ...) を張る/外すため、document 自身にも
  // FakeNode と同じ最小限のリスナー機構を持たせる（document は FakeNode を継承しないため複製）。
  var documentListeners = {};
  var document = {
    body: body,
    _listeners: documentListeners,
    addEventListener: function (type, handler) {
      if (!this._listeners[type]) this._listeners[type] = [];
      this._listeners[type].push(handler);
    },
    removeEventListener: function (type, handler) {
      var handlers = this._listeners[type];
      if (!handlers) return;
      var index = handlers.indexOf(handler);
      if (index !== -1) handlers.splice(index, 1);
    },
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
