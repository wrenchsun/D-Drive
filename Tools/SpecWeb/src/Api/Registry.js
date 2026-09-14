/**
 * API 登録レジストリ（後続チケットのための拡張点）。
 *
 * W-4（アセット CRUD）・W-6/W-7（調整値）・W-16（機能ページ）等は、
 * 自分のファイル（例: Assets.js）の中でこの registerApi を呼ぶだけで
 * 共通の Code.js（doGet/doPost）を編集せずに `?api=1&name=<name>` の
 * エンドポイントを追加できる。docs/32_spec_web.md の「登録式の拡張点」規約。
 *
 * 実装上の注意（GAS の複数ファイル連結について）:
 * GAS は 1 プロジェクトの全ファイルを 1 つのグローバルスコープに連結するが、
 * トップレベルの実行順序はファイル名に依存し保証されない。
 * この関数は状態を `getApiRegistry_` という「関数オブジェクト自身のプロパティ」
 * に遅延初期化して持つ（トップレベルの `var` に持たない）ため、
 * 他ファイルが自分のトップレベルで `registerApi(...)` を呼んでも、
 * 連結順序に関係なく必ず動作する（関数宣言は連結順序に関わらず全ファイルで
 * 参照可能だが、トップレベルの `var` の初期化はその行が実行されるまで
 * 完了しないため）。後続チケットで新しいレジストリを増やす場合も
 * この形（状態を関数プロパティに持つ）を踏襲すること。
 */

function getApiRegistry_() {
  if (!getApiRegistry_.handlers) {
    getApiRegistry_.handlers = {};
  }
  return getApiRegistry_.handlers;
}

/**
 * API ハンドラを登録する。
 * @param {string} name  `?api=1&name=<name>` で呼び出される名前。
 * @param {function({e:Object, params:Object, auth:Object}):(Object|undefined)} handler
 *   戻り値は `{ ok: true, ... }` として（ok は自動付与）レスポンスに merge される。
 *   revision 不一致は `RevisionConflictError`（Storage.js）を throw すればよい。
 *   呼び出し側（Code.js の handleApiRequest_）が本文の status フィールドに変換する。
 */
function registerApi(name, handler) {
  if (!name || typeof name !== 'string') {
    throw new Error('registerApi の name は空でない文字列である必要があります');
  }
  if (typeof handler !== 'function') {
    throw new Error('registerApi の handler は関数である必要があります: ' + name);
  }
  var handlers = getApiRegistry_();
  if (handlers[name]) {
    throw new Error('API はすでに登録されています: ' + name);
  }
  handlers[name] = handler;
}

function getApi(name) {
  return getApiRegistry_()[name];
}

function listRegisteredApis() {
  return Object.keys(getApiRegistry_());
}

// 動作確認・後続チケットの実装例としての組み込み API。
// W-1 の AC（doGet が UI/API を振り分ける）の smoke test に使う。
registerApi('ping', function () {
  return { pong: true, now: new Date().toISOString() };
});
