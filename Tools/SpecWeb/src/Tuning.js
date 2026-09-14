/**
 * 調整値: スカラー型 API（W-6）。docs/32_spec_web.md §3.2.1・§8 W-6 の AC を実装する。
 *
 * ストレージ: Storage.js の "tuning" コレクション（1 件 = 1 キー）。
 * id = キー（<機能>/<名前>、TuningCommon.js の specWebValidateTuningKey_）。
 *
 * ロール:
 * - 一覧・取得は viewer 以上（誰でも）
 * - 作成・更新・削除は editor 以上
 * - 対象が locked=true、またはこの呼び出しで locked を変更する場合は admin 以上
 *   （docs/32 §3.2.4「locked はプログラマーだけが変更できる」）
 *
 * 書き込み系は params.payload に JSON 文字列で本体を渡す規約
 * （TuningCommon.js の specWebParsePayload_。html/App.html の SpecWebClient を
 * 変更せず、既存の GET + クエリパラメータの範囲で複雑な構造を送るため）。
 */

var TUNING_SCALAR_COLLECTION = 'tuning';

/**
 * payload（+ 既存値）からスカラー調整値エントリ全体を組み立てて検証する。
 * 常に「完全なエントリ」を返す（部分更新でも欠けたフィールドは existing から補う）。
 */
function specWebBuildScalarEntry_(payload, existing) {
  var merged = Object.assign({}, existing, payload);
  var valueType = merged.valueType;
  if (TUNING_VALUE_TYPES.indexOf(valueType) === -1) {
    throw new SpecWebValidationError('valueType が不正です: ' + valueType);
  }
  var enumOptions = valueType === 'enum' ? merged.enumOptions || [] : [];
  if (valueType === 'enum' && (!Array.isArray(enumOptions) || enumOptions.length === 0)) {
    throw new SpecWebValidationError('enum の選択肢（enumOptions）が空です');
  }
  specWebValidateScalarValue_(valueType, merged.value, merged.min, merged.max, enumOptions, 'value');

  return {
    kind: 'scalar',
    valueType: valueType,
    value: merged.value,
    enumOptions: enumOptions,
    min: merged.min === undefined ? null : merged.min,
    max: merged.max === undefined ? null : merged.max,
    step: merged.step === undefined ? null : merged.step,
    unit: merged.unit || '',
    description: merged.description || '',
    group: merged.group || '',
    tags: Array.isArray(merged.tags) ? merged.tags : [],
    locked: !!merged.locked,
    comments: Array.isArray(existing && existing.comments) ? existing.comments : []
  };
}

registerApi('tuningScalarList', function () {
  return { items: Storage.listItems(TUNING_SCALAR_COLLECTION) };
});

registerApi('tuningScalarGet', function (ctx) {
  var key = ctx.params.key;
  var item = Storage.getItem(TUNING_SCALAR_COLLECTION, key);
  if (!item) throw new SpecWebNotFoundError('存在しません: ' + key);
  return { item: item };
});

registerApi('tuningScalarCreate', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.EDITOR, '調整値の作成には editor 以上の権限が必要です');
  var payload = specWebParsePayload_(ctx.params);
  var key = payload.key;
  specWebValidateTuningKey_(key);
  if (Storage.getItem(TUNING_SCALAR_COLLECTION, key)) {
    throw new SpecWebValidationError('キーが既に存在します: ' + key);
  }
  var entry = specWebBuildScalarEntry_(payload, null);
  if (entry.locked) {
    specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.ADMIN, 'locked な調整値の作成には admin 権限が必要です');
  }
  var saved = Storage.putItem(TUNING_SCALAR_COLLECTION, key, entry, { actor: ctx.auth.principal });
  return { item: saved };
});

registerApi('tuningScalarUpdate', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var key = payload.key;
  if (!key) throw new SpecWebValidationError('key が必要です');
  var existing = Storage.getItem(TUNING_SCALAR_COLLECTION, key);
  if (!existing) throw new SpecWebNotFoundError('存在しません: ' + key);
  if (payload.revision === undefined || payload.revision === null) {
    throw new SpecWebValidationError('revision が必要です（楽観ロック）');
  }

  var changingLock = Object.prototype.hasOwnProperty.call(payload, 'locked') && !!payload.locked !== !!existing.locked;
  var requiresAdmin = existing.locked || changingLock;
  specWebRequireRole_(
    ctx.auth,
    requiresAdmin ? SPEC_WEB_ROLES.ADMIN : SPEC_WEB_ROLES.EDITOR,
    existing.locked
      ? 'locked な調整値の変更には admin 権限が必要です'
      : 'locked の変更には admin 権限が必要です'
  );

  var entry = specWebBuildScalarEntry_(payload, existing);
  var saved = Storage.putItem(TUNING_SCALAR_COLLECTION, key, entry, {
    actor: ctx.auth.principal,
    expectedRevision: payload.revision
  });
  return { item: saved };
});

registerApi('tuningScalarDelete', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var key = payload.key;
  if (!key) throw new SpecWebValidationError('key が必要です');
  var existing = Storage.getItem(TUNING_SCALAR_COLLECTION, key);
  if (!existing) throw new SpecWebNotFoundError('存在しません: ' + key);
  specWebRequireRole_(
    ctx.auth,
    existing.locked ? SPEC_WEB_ROLES.ADMIN : SPEC_WEB_ROLES.EDITOR,
    existing.locked ? 'locked な調整値の削除には admin 権限が必要です' : '調整値の削除には editor 以上の権限が必要です'
  );
  if (payload.revision === undefined || payload.revision === null) {
    throw new SpecWebValidationError('revision が必要です（楽観ロック）');
  }
  var removed = Storage.deleteItem(TUNING_SCALAR_COLLECTION, key, { expectedRevision: payload.revision });
  return { removed: removed };
});
