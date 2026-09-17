/**
 * 調整値（W-6 スカラー・W-7 テーブル・コメント）共通のエラー型・検証・ヘルパー。
 * docs/32_spec_web.md §3.2（データモデル）・§8 W-6/W-7 の AC を実装するための基盤。
 *
 * 呼び出し規約は Storage.js/Api/Registry.js と同じ:
 * このファイルの中身は他ファイルから「関数の中」でだけ参照すること
 * （GAS の連結順序はファイル名に依存し保証されないため。トップレベルの
 * 実行文で他ファイルの var を読まない。関数宣言はこの制約を受けない）。
 *
 * 既存の共通ファイル（Storage.js/Auth.js/Api/Registry.js/Code.js）は編集しない
 * （複数チケットが並行して触るため）。ただし Storage.js の内部関数
 * （withStorageLock_ / specWebReadCollectionRaw_ / specWebWriteCollectionRaw_ /
 * specWebNowIso_）は Storage.js 自身が「関数宣言はファイルをまたいで安全に呼べる」
 * という前提で作られているため、このファイルからもそのまま呼んでよい
 * （コメント追記のような「revision を意識しない原子的な追記」を実現するために使う。
 * §8 W-6/W-7 の AC「コメントの投稿と一覧」）。
 */

var TUNING_VALUE_TYPES = ['float', 'int', 'bool', 'string', 'enum'];

// <機能>/<名前> のちょうど 2 segment。各 segment は英字始まり + 英数字
// (docs/32_spec_web.md §3.2「既存の Signal キーと同じ書式」。既存コードに
// 厳密な正規表現が無かったため、このチケットで新規に確定する。§9 追記参照)。
var TUNING_KEY_PATTERN = /^[A-Za-z][A-Za-z0-9]*\/[A-Za-z][A-Za-z0-9]*$/;

/** 400 相当（範囲外・型違い・enum 外・書式違反等）。 */
function SpecWebValidationError(message) {
  this.name = 'SpecWebValidationError';
  this.message = message;
  this.status = 400;
  if (typeof Error.captureStackTrace === 'function') {
    Error.captureStackTrace(this, SpecWebValidationError);
  }
}
SpecWebValidationError.prototype = Object.create(Error.prototype);
SpecWebValidationError.prototype.constructor = SpecWebValidationError;

/** 404 相当。 */
function SpecWebNotFoundError(message) {
  this.name = 'SpecWebNotFoundError';
  this.message = message;
  this.status = 404;
  if (typeof Error.captureStackTrace === 'function') {
    Error.captureStackTrace(this, SpecWebNotFoundError);
  }
}
SpecWebNotFoundError.prototype = Object.create(Error.prototype);
SpecWebNotFoundError.prototype.constructor = SpecWebNotFoundError;

/** 403 相当（ロール不足・locked への editor からの変更）。 */
function SpecWebForbiddenError(message) {
  this.name = 'SpecWebForbiddenError';
  this.message = message;
  this.status = 403;
  if (typeof Error.captureStackTrace === 'function') {
    Error.captureStackTrace(this, SpecWebForbiddenError);
  }
}
SpecWebForbiddenError.prototype = Object.create(Error.prototype);
SpecWebForbiddenError.prototype.constructor = SpecWebForbiddenError;

/** auth が role 未満なら SpecWebForbiddenError を投げる（Auth.js の hasRole_ をそのまま使う）。 */
function specWebRequireRole_(auth, role, message) {
  if (!hasRole_(auth, role)) {
    throw new SpecWebForbiddenError(message || (role + ' 以上の権限が必要です'));
  }
}

function specWebValidateTuningKey_(key) {
  if (typeof key !== 'string' || !TUNING_KEY_PATTERN.test(key)) {
    throw new SpecWebValidationError(
      'キーの書式が不正です（<機能>/<名前> の 2 segment、英字始まりの英数字のみ）: ' + key
    );
  }
}

/**
 * params.payload（JSON 文字列）をパースする。書き込み系 API は複雑な構造
 * （列定義・行・セル配列等）を持つため、この 1 パラメータに JSON で詰めて送る規約
 * （既存の SpecWebClient.callApi は GET のみでクエリパラメータしか送れないため。
 * html/App.html は編集しない = クエリパラメータの範囲で表現する）。
 */
function specWebParsePayload_(params) {
  var raw = (params && params.payload) || '';
  if (!raw) return {};
  try {
    var parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch (err) {
    throw new SpecWebValidationError('payload が不正な JSON です: ' + err.message);
  }
}

function specWebIsFiniteNumber_(value) {
  return typeof value === 'number' && isFinite(value);
}

/**
 * スカラー値 1 件を valueType/min/max/enumOptions に対して検証する。
 * テーブルのセル 1 個の検証にもそのまま使う（列定義 = スカラーの型情報と同じ形）。
 * 違反時は SpecWebValidationError（400 相当）を投げる。
 */
function specWebValidateScalarValue_(valueType, value, min, max, enumOptions, label) {
  label = label || '値';
  if (TUNING_VALUE_TYPES.indexOf(valueType) === -1) {
    throw new SpecWebValidationError('valueType が不正です: ' + valueType);
  }
  switch (valueType) {
    case 'float':
      if (!specWebIsFiniteNumber_(value)) {
        throw new SpecWebValidationError(label + ' は数値である必要があります: ' + JSON.stringify(value));
      }
      break;
    case 'int':
      if (!specWebIsFiniteNumber_(value) || Math.floor(value) !== value) {
        throw new SpecWebValidationError(label + ' は整数である必要があります: ' + JSON.stringify(value));
      }
      break;
    case 'bool':
      if (typeof value !== 'boolean') {
        throw new SpecWebValidationError(label + ' は真偽値である必要があります: ' + JSON.stringify(value));
      }
      break;
    case 'string':
      if (typeof value !== 'string') {
        throw new SpecWebValidationError(label + ' は文字列である必要があります: ' + JSON.stringify(value));
      }
      break;
    case 'enum':
      if (!Array.isArray(enumOptions) || enumOptions.length === 0) {
        throw new SpecWebValidationError('enum の選択肢（enumOptions）が空です');
      }
      if (enumOptions.indexOf(value) === -1) {
        throw new SpecWebValidationError(label + ' は enumOptions のいずれかである必要があります: ' + JSON.stringify(value));
      }
      return; // enum は min/max を見ない
    default:
      return;
  }
  if ((valueType === 'float' || valueType === 'int')) {
    if (min !== undefined && min !== null && value < min) {
      throw new SpecWebValidationError(label + ' が最小値（' + min + '）未満です: ' + value);
    }
    if (max !== undefined && max !== null && value > max) {
      throw new SpecWebValidationError(label + ' が最大値（' + max + '）超過です: ' + value);
    }
  }
}

/** valueType に応じた既定値（列追加・行追加でセルを埋める用）。 */
function specWebDefaultValueForType_(valueType, min, enumOptions) {
  switch (valueType) {
    case 'float':
    case 'int':
      return typeof min === 'number' && min > 0 ? min : 0;
    case 'bool':
      return false;
    case 'string':
      return '';
    case 'enum':
      if (!Array.isArray(enumOptions) || enumOptions.length === 0) {
        throw new SpecWebValidationError('enum の選択肢（enumOptions）が空です');
      }
      return enumOptions[0];
    default:
      throw new SpecWebValidationError('valueType が不正です: ' + valueType);
  }
}

/**
 * 列定義（型・min/max・enumOptions）が変わったとき、既存セル値を新しい定義に
 * 合わせて補正する。範囲外の数値は min/max にクランプ、enum 外の値は
 * 選択肢の先頭にリセットする（例外にはしない = デザイナー作業を止めない、
 * CLAUDE.md §0-4 と同じ考え方。補正が起きたことは呼び出し元が把握できるよう
 * coerced フラグを返す）。型そのものが変わった場合は既定値へ完全リセットする
 * （実装メモ = docs/32 参照。型変更時は既存値の意味が保証できないため）。
 */
function specWebCoerceCellForColumn_(value, column, previousValueType) {
  if (previousValueType !== undefined && previousValueType !== column.valueType) {
    return { value: specWebDefaultValueForType_(column.valueType, column.min, column.enumOptions), coerced: true };
  }
  try {
    specWebValidateScalarValue_(column.valueType, value, column.min, column.max, column.enumOptions);
    return { value: value, coerced: false };
  } catch (err) {
    if (!(err instanceof SpecWebValidationError)) throw err;
  }
  if (column.valueType === 'float' || column.valueType === 'int') {
    var v = specWebIsFiniteNumber_(value) ? value : specWebDefaultValueForType_(column.valueType, column.min, column.enumOptions);
    if (column.valueType === 'int') v = Math.round(v);
    if (column.min !== undefined && column.min !== null && v < column.min) v = column.min;
    if (column.max !== undefined && column.max !== null && v > column.max) v = column.max;
    return { value: v, coerced: true };
  }
  if (column.valueType === 'enum') {
    return { value: specWebDefaultValueForType_('enum', column.min, column.enumOptions), coerced: true };
  }
  return { value: specWebDefaultValueForType_(column.valueType, column.min, column.enumOptions), coerced: true };
}

function specWebMakeComment_(author, body) {
  if (!body || typeof body !== 'string' || !body.trim()) {
    throw new SpecWebValidationError('コメント本文が空です');
  }
  return {
    id: UtilitiesAdapter.newUuid(),
    author: author,
    body: body,
    createdAt: specWebNowIso_(),
    resolved: false
  };
}

/**
 * revision を意識しない原子的な読み取り→更新→書き込み（コメント追記専用）。
 * Storage.putItem は expectedRevision が無いと無条件上書きになってしまい、
 * かつ呼び出し側で「今の comments 配列」を読んでから push するまでの間に
 * 他の変更が入ると消えてしまう（lost update）。ロックの中で読み直してから
 * 追記することでこれを避ける。Storage.js の内部関数をそのまま使う
 * （このファイル冒頭の呼び出し規約を参照）。
 */
function specWebMutateItemAtomic_(collectionName, itemId, mutateFn, actor) {
  return withStorageLock_(function () {
    var data = specWebReadCollectionRaw_(collectionName);
    // 2026-09-17（[41] P2-15）: 継承プロパティ（`constructor` 等の id）を掴まないよう
    // Storage.js の specWebOwnItem_ を通す。
    var existing = specWebOwnItem_(data.items, itemId);
    if (!existing) {
      throw new SpecWebNotFoundError('存在しません: ' + collectionName + '/' + itemId);
    }
    var patch = mutateFn(existing) || {};
    var next = Object.assign({}, existing, patch, {
      id: itemId,
      revision: existing.revision + 1,
      updatedBy: actor,
      updatedAt: specWebNowIso_()
    });
    data.items[itemId] = next;
    specWebWriteCollectionRaw_(collectionName, data);
    return next;
  });
}
