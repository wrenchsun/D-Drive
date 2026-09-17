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

/**
 * 2026-09-17（[41](../../docs/41_phase6_review_2026-09-17.md) P2-15）:
 * items マップの prototype を外す。
 *
 * ドキュメント id は自由入力が入口になり得る（`members` の id は貼り付けた表記そのもの、
 * `users` はメールアドレス、`tuning` はキー）。素の `{}` は `Object.prototype` を継承するため、
 * id が `constructor` / `toString` / `hasOwnProperty` だと `items[id]` が**継承した関数**を返し、
 * 呼び出し側の `existing.revision` が `undefined`（→ `revision: NaN`）になってしまう。
 * さらに id が `__proto__` の場合、素のオブジェクトへの `items['__proto__'] = item` は
 * own property を作らず prototype の差し替えになるため、書いたはずの 1 件が消える。
 * prototype を null にすれば、どの id も普通のキーとして扱える
 * （読み取り側は `specWebOwnItem_` でも二重に守る）。
 */
function specWebAdoptItemsMap_(items) {
  return Object.setPrototypeOf(items, null);
}

/**
 * items マップから「own property として存在する 1 件」だけを返す（無ければ null）。
 * P2-15 の二重防御。`specWebAdoptItemsMap_` を通っていないマップ（他ファイルが組んだもの）でも
 * 継承プロパティを掴まないようにする。
 */
function specWebOwnItem_(items, itemId) {
  if (!items) return null;
  if (!Object.prototype.hasOwnProperty.call(items, itemId)) return null;
  return items[itemId] || null;
}

/**
 * items マップを「普通の prototype を持つオブジェクト」へ浅くコピーする（`Storage.listItems` 用）。
 *
 * `specWebAdoptItemsMap_` でプロトタイプを外したマップをそのまま API の戻り値にすると、
 * `google.script.run` / `JSON.stringify` のシリアライズ挙動に依存してしまうため、外へ渡す分は
 * 素のオブジェクトに戻す（`tuningScalarList` / `tuningTableList` は listItems の結果を
 * そのまま返している）。
 *
 * `Object.assign` を使わないのは、キーが `__proto__` のとき `[[Set]]` 経由で
 * prototype の差し替えになり 1 件消えるため（`defineProperty` なら own property になる）。
 */
function specWebPlainItemsCopy_(items) {
  var copy = {};
  Object.keys(items).forEach(function (key) {
    Object.defineProperty(copy, key, {
      value: items[key],
      enumerable: true,
      writable: true,
      configurable: true
    });
  });
  return copy;
}

function specWebEmptyCollection_() {
  return { items: specWebAdoptItemsMap_({}) };
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
  specWebAdoptItemsMap_(data.items);
  return data;
}

function specWebWriteCollectionRaw_(name, data) {
  DriveAdapter.writeFile(name + '.json', JSON.stringify(data));
}

function specWebNowIso_() {
  return new Date().toISOString();
}

/**
 * 読み込み済みのコレクション（`data`）に対して 1 件を作成/更新する共通処理。
 * `Storage.putItem`（1 件 = 1 ロック）と `Storage.mutateMany`（N 件 = 1 ロック）の**両方**が
 * これを呼ぶため、件数によって意味が変わることが構造的に起きない
 * （revision 検査・revision 加算・updatedBy / updatedAt の付与はここだけにある）。
 *
 * 呼び出し側は必ず `withStorageLock_` の中から呼ぶこと（書き込みは呼び出し側が行う）。
 */
function specWebPutItemInto_(data, collectionName, itemId, patch, options) {
  options = options || {};
  var expectedRevision = options.expectedRevision;
  var existing = specWebOwnItem_(data.items, itemId);
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
    updatedBy: options.actor || 'unknown',
    updatedAt: specWebNowIso_()
  });
  data.items[itemId] = next;
  return next;
}

var Storage = {
  /** 1 件取得。無ければ null。 */
  getItem: function (collectionName, itemId) {
    var data = specWebReadCollectionRaw_(collectionName);
    return specWebOwnItem_(data.items, itemId);
  },

  /** コレクション全件を { id: item } の形で返す。 */
  listItems: function (collectionName) {
    var data = specWebReadCollectionRaw_(collectionName);
    return specWebPlainItemsCopy_(data.items);
  },

  /**
   * 1 件を作成/更新する。
   * options.expectedRevision を渡すと、現在の revision と一致しない場合に
   * RevisionConflictError を throw する（省略時は無条件で上書き＝新規作成）。
   * options.actor は updatedBy に入れる識別子（メールアドレス or トークン種別）。
   *
   * **`expectedRevision: 0` は「まだ存在しないこと」の原子的な要求**になる
   * （putItem が保存する revision は必ず 1 以上なので、revision 0 = 未作成と同義）。
   * 作成系 API（`assets.create` / `tuningScalarCreate` / `tuningTableCreate`）は、
   * ロック外の `getItem` による重複チェックだけでは同時作成を防げないため、これを必ず渡す
   * （2026-09-17、[41](../../docs/41_phase6_review_2026-09-17.md) P2-13）。
   */
  putItem: function (collectionName, itemId, patch, options) {
    return withStorageLock_(function () {
      var data = specWebReadCollectionRaw_(collectionName);
      var next = specWebPutItemInto_(data, collectionName, itemId, patch, options);
      specWebWriteCollectionRaw_(collectionName, data);
      return next;
    });
  },

  /**
   * 2026-09-17（[41](../../docs/41_phase6_review_2026-09-17.md) P2-11）:
   * **1 回のロックの中で「1 読み・N 件更新・1 書き」**を行う。
   *
   * `assetState` / `assetParams` / `members.importPaste` のように「D-Drive / 貼り付けから
   * 送られてきた N 件をまとめて反映する」API のために足した。1 件ごとに `getItem` + `putItem` を
   * 呼ぶと、1 件あたり Drive の読み書きが 3〜5 回・`<collection>.json` 全文のシリアライズが
   * 2 回以上発生し、数百件で GAS の実行時間 6 分上限（docs/32 §2.4）に近づく。途中で切れると
   * 部分反映にもなる（各 `putItem` は原子的なので壊れはしないが、反映済みと未反映が混ざる）。
   *
   * `fn(tx)` の中で使える操作（**1 件ずつ呼んだときと意味を変えない**。実体は `putItem` と
   * 同じ `specWebPutItemInto_`）:
   *   - `tx.get(itemId)`   現在の 1 件（同じ `fn` の中で既に put した内容も反映済み）。無ければ null
   *   - `tx.list()`        全件の { id: item }（件数の検査等に使う。内部のマップそのものなので
   *                        書き換えない・API の戻り値にそのまま入れない。外へ返すなら listItems）
   *   - `tx.put(itemId, patch, { expectedRevision, actor })`  patch を merge して revision を進める
   *   - `tx.remove(itemId, { expectedRevision })`             1 件削除（無ければ false）
   *
   * 書き込みは `fn` が正常に返った後に**最大 1 回**だけ行う（1 件も変更が無ければ書かない）。
   * `fn` が例外を投げた場合は**何も書かない**（ロックは finally で解放される）ので、
   * 「検査 → 書き込み」をまとめて原子的に行う用途にも使える（P2-17 の `users.upsert`）。
   *
   * @param {string} collectionName
   * @param {function(Object):*} fn
   * @param {{actor?: string}} [options] actor は tx.put で個別に指定しなかった場合の既定値
   * @return {*} fn の戻り値
   */
  mutateMany: function (collectionName, fn, options) {
    options = options || {};
    var defaultActor = options.actor;
    return withStorageLock_(function () {
      var data = specWebReadCollectionRaw_(collectionName);
      var dirty = false;
      var tx = {
        get: function (itemId) {
          return specWebOwnItem_(data.items, itemId);
        },
        list: function () {
          return data.items;
        },
        put: function (itemId, patch, putOptions) {
          putOptions = putOptions || {};
          var next = specWebPutItemInto_(data, collectionName, itemId, patch, {
            expectedRevision: putOptions.expectedRevision,
            actor: putOptions.actor || defaultActor
          });
          dirty = true;
          return next;
        },
        remove: function (itemId, removeOptions) {
          removeOptions = removeOptions || {};
          var expectedRevision = removeOptions.expectedRevision;
          var existing = specWebOwnItem_(data.items, itemId);
          if (!existing) return false;
          if (expectedRevision !== undefined && expectedRevision !== null && existing.revision !== expectedRevision) {
            throw new RevisionConflictError(
              collectionName + '/' + itemId + ' の revision が一致しません（現在: ' + existing.revision + '、要求: ' + expectedRevision + '）',
              existing.revision
            );
          }
          delete data.items[itemId];
          dirty = true;
          return true;
        }
      };
      var result = fn(tx);
      if (dirty) {
        specWebWriteCollectionRaw_(collectionName, data);
      }
      return result;
    });
  },

  /**
   * O-15: 1 件を別の id（キー）へ改名する。種別・識別子が id そのものを構成するアセット発注の
   * リネーム向けに新設した（putItem→deleteItem のように 2 回に分けて呼ぶと、その間に別の
   * リクエストが割り込む余地が残るため、旧 id の revision 楽観ロック + 新 id の重複チェック +
   * 旧 id の削除・新 id での保存を 1 回の withStorageLock_ 内で原子的に行う）。
   * @param {string} collectionName
   * @param {string} oldId
   * @param {string} newId
   * @param {Object} patch 新 id へ反映する差分（呼び出し側が「旧アイテムの全フィールドのうち
   *   変更したいものだけ」を渡す。Object.assign({}, existing, patch, {...}) の順で適用するため、
   *   コメント・orderGroup への所属（parentId）等、patch に含めないフィールドは旧アイテムから
   *   そのまま引き継がれる）。
   * @param {Object} options { expectedRevision, actor }
   * @return {Object} 新 id で保存された項目
   */
  renameItem: function (collectionName, oldId, newId, patch, options) {
    options = options || {};
    var expectedRevision = options.expectedRevision;
    var actor = options.actor || 'unknown';
    return withStorageLock_(function () {
      var data = specWebReadCollectionRaw_(collectionName);
      var existing = specWebOwnItem_(data.items, oldId);
      if (!existing) {
        var notFound = new Error(collectionName + '/' + oldId + ' が見つかりません');
        notFound.status = 404;
        throw notFound;
      }
      var currentRevision = existing.revision;
      if (expectedRevision !== undefined && expectedRevision !== null && currentRevision !== expectedRevision) {
        throw new RevisionConflictError(
          collectionName + '/' + oldId + ' の revision が一致しません（現在: ' + currentRevision + '、要求: ' + expectedRevision + '）',
          currentRevision
        );
      }
      if (specWebOwnItem_(data.items, newId)) {
        var duplicate = new Error(collectionName + '/' + newId + ' は既に存在します');
        duplicate.status = 409;
        throw duplicate;
      }
      var next = Object.assign({}, existing, patch, {
        id: newId,
        revision: currentRevision + 1,
        updatedBy: actor,
        updatedAt: specWebNowIso_()
      });
      delete data.items[oldId];
      data.items[newId] = next;
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
      var existing = specWebOwnItem_(data.items, itemId);
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
