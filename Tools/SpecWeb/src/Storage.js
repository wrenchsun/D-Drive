/**
 * Drive JSON コレクションの読み書き + LockService による直列化 + revision 楽観ロック。
 * docs/32_spec_web.md §2.2（ストレージ方式）・§3（全エンティティ共通の revision）を実装したもの。
 *
 * コレクション（例: "assets" / "tuning" / "users"）1 つ = Drive 上の "<name>.json" 1 ファイル。
 * ファイルの中身は { "items": { "<id>": { ...フィールド, id, revision, updatedBy, updatedAt } } }。
 *
 * 呼び出し規約:
 * - このファイルの中身は他ファイルから「関数の中」でだけ参照すること
 *   （GAS は複数ファイルを 1 つのグローバルスコープに連結するが、
 *   トップレベルの実行順序はファイル名に依存し保証されない。
 *   トップレベルの実行文で他ファイルの var を読むと、連結順序によって
 *   未定義になる可能性がある。関数本体の中であれば呼び出し時点で
 *   全ファイルの読み込みは完了しているため安全）。
 */

var SPEC_WEB_LOCK_TIMEOUT_MS = 30 * 1000;

/**
 * revision の不一致（楽観ロックの競合）を表すエラー。
 * docs/32_spec_web.md の「409 相当」の実体（ContentAdapter が本文の
 * status フィールドに変換する。Content Service に実 HTTP ステータスは無いため）。
 */
function RevisionConflictError(message, currentRevision) {
  this.name = 'RevisionConflictError';
  this.message = message;
  this.status = 409;
  this.currentRevision = currentRevision;
  if (typeof Error.captureStackTrace === 'function') {
    Error.captureStackTrace(this, RevisionConflictError);
  }
}
RevisionConflictError.prototype = Object.create(Error.prototype);
RevisionConflictError.prototype.constructor = RevisionConflictError;

function withStorageLock_(fn) {
  var lock = LockAdapter.getScriptLock();
  var acquired = LockAdapter.waitLock(lock, SPEC_WEB_LOCK_TIMEOUT_MS);
  if (!acquired) {
    throw new Error('ロックを取得できませんでした（タイムアウト）。しばらく待って再度お試しください。');
  }
  try {
    return fn();
  } finally {
    LockAdapter.releaseLock(lock);
  }
}

function specWebEmptyCollection_() {
  return { items: {} };
}

function specWebReadCollectionRaw_(name) {
  var text = DriveAdapter.readFile(name + '.json');
  if (text === null || text === undefined || text === '') {
    return specWebEmptyCollection_();
  }
  var data;
  try {
    data = JSON.parse(text);
  } catch (err) {
    throw new Error('コレクション "' + name + '" の JSON が壊れています: ' + err.message);
  }
  if (!data || typeof data !== 'object' || typeof data.items !== 'object' || data.items === null) {
    return specWebEmptyCollection_();
  }
  return data;
}

function specWebWriteCollectionRaw_(name, data) {
  DriveAdapter.writeFile(name + '.json', JSON.stringify(data));
}

function specWebNowIso_() {
  return new Date().toISOString();
}

var Storage = {
  /** 1 件取得。無ければ null。 */
  getItem: function (collectionName, itemId) {
    var data = specWebReadCollectionRaw_(collectionName);
    return data.items[itemId] || null;
  },

  /** コレクション全件を { id: item } の形で返す。 */
  listItems: function (collectionName) {
    var data = specWebReadCollectionRaw_(collectionName);
    return data.items;
  },

  /**
   * 1 件を作成/更新する。
   * options.expectedRevision を渡すと、現在の revision と一致しない場合に
   * RevisionConflictError を throw する（省略時は無条件で上書き＝新規作成）。
   * options.actor は updatedBy に入れる識別子（メールアドレス or トークン種別）。
   */
  putItem: function (collectionName, itemId, patch, options) {
    options = options || {};
    var expectedRevision = options.expectedRevision;
    var actor = options.actor || 'unknown';
    return withStorageLock_(function () {
      var data = specWebReadCollectionRaw_(collectionName);
      var existing = data.items[itemId] || null;
      var currentRevision = existing ? existing.revision : 0;
      if (expectedRevision !== undefined && expectedRevision !== null && currentRevision !== expectedRevision) {
        throw new RevisionConflictError(
          collectionName + '/' + itemId + ' の revision が一致しません（現在: ' + currentRevision + '、要求: ' + expectedRevision + '）',
          currentRevision
        );
      }
      var next = Object.assign({}, existing, patch, {
        id: itemId,
        revision: currentRevision + 1,
        updatedBy: actor,
        updatedAt: specWebNowIso_()
      });
      data.items[itemId] = next;
      specWebWriteCollectionRaw_(collectionName, data);
      return next;
    });
  },

  /**
   * 1 件を削除する。存在しなければ false を返す（エラーにしない）。
   * options.expectedRevision を渡すと revision 不一致時に RevisionConflictError。
   */
  deleteItem: function (collectionName, itemId, options) {
    options = options || {};
    var expectedRevision = options.expectedRevision;
    return withStorageLock_(function () {
      var data = specWebReadCollectionRaw_(collectionName);
      var existing = data.items[itemId] || null;
      if (!existing) return false;
      if (expectedRevision !== undefined && expectedRevision !== null && existing.revision !== expectedRevision) {
        throw new RevisionConflictError(
          collectionName + '/' + itemId + ' の revision が一致しません（現在: ' + existing.revision + '、要求: ' + expectedRevision + '）',
          existing.revision
        );
      }
      delete data.items[itemId];
      specWebWriteCollectionRaw_(collectionName, data);
      return true;
    });
  }
};
