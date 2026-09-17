/**
 * 調整値: テーブル型 API（W-7）。docs/32_spec_web.md §3.2.2・§8 W-7 の AC を実装する。
 *
 * ストレージ: Storage.js の "tuningTables" コレクション（1 件 = 1 テーブルキー）。
 * エントリ形状: { id, kind:'table', columns:[{key,valueType,min,max,unit,enumOptions}],
 *                rows:[{rowId, cells:{colKey:value}, comments:[]}], locked, comments:[] }
 *
 * ロール方針は Tuning.js（スカラー）と同じ:
 * - 一覧・取得は誰でも
 * - 作成・列/行/セルの変更は editor 以上、対象が locked なら admin 以上
 * - locked の値そのものを変更する操作は常に admin 以上（tuningTableSetLocked）
 *
 * 列削除で該当セルも削除する／列の型変更で既存セルが検証に通らない場合の扱いは
 * TuningCommon.js の specWebCoerceCellForColumn_ に実装し、docs/32 の実装メモに
 * 確定した規約として書く（型変更 = 既定値へ完全リセット、min/max/enum の変更のみ
 * = 可能な範囲でクランプ/リセットして自動修復する。例外にはしない）。
 */

var TUNING_TABLE_COLLECTION = 'tuningTables';

function specWebValidateColumnDef_(column) {
  if (!column || typeof column.key !== 'string' || !column.key) {
    throw new SpecWebValidationError('列の key が必要です');
  }
  if (TUNING_VALUE_TYPES.indexOf(column.valueType) === -1) {
    throw new SpecWebValidationError('列 ' + column.key + ' の valueType が不正です: ' + column.valueType);
  }
  if (column.valueType === 'enum' && (!Array.isArray(column.enumOptions) || column.enumOptions.length === 0)) {
    throw new SpecWebValidationError('列 ' + column.key + ' の enumOptions が空です');
  }
}

function specWebNormalizeColumnDef_(column) {
  specWebValidateColumnDef_(column);
  return {
    key: column.key,
    valueType: column.valueType,
    min: column.min === undefined ? null : column.min,
    max: column.max === undefined ? null : column.max,
    unit: column.unit || '',
    enumOptions: column.valueType === 'enum' ? column.enumOptions : []
  };
}

function specWebFindColumn_(columns, key) {
  for (var i = 0; i < columns.length; i++) {
    if (columns[i].key === key) return columns[i];
  }
  return null;
}

/** 1 行の cells を columns に対して検証する（欠けているセルは既定値で埋める）。 */
function specWebNormalizeRowCells_(columns, cells) {
  cells = cells || {};
  var next = {};
  for (var i = 0; i < columns.length; i++) {
    var col = columns[i];
    var value = Object.prototype.hasOwnProperty.call(cells, col.key)
      ? cells[col.key]
      : specWebDefaultValueForType_(col.valueType, col.min, col.enumOptions);
    specWebValidateScalarValue_(col.valueType, value, col.min, col.max, col.enumOptions, '列 ' + col.key);
    next[col.key] = value;
  }
  return next;
}

function specWebRequireTableRole_(auth, existing, message) {
  specWebRequireRole_(
    auth,
    existing && existing.locked ? SPEC_WEB_ROLES.ADMIN : SPEC_WEB_ROLES.EDITOR,
    existing && existing.locked ? 'locked なテーブルの変更には admin 権限が必要です' : message
  );
}

function specWebGetTableOrThrow_(key) {
  var item = Storage.getItem(TUNING_TABLE_COLLECTION, key);
  if (!item) throw new SpecWebNotFoundError('存在しません: ' + key);
  return item;
}

function specWebRequireRevision_(payload) {
  if (payload.revision === undefined || payload.revision === null) {
    throw new SpecWebValidationError('revision が必要です（楽観ロック）');
  }
  return payload.revision;
}

registerApi_('tuningTableList', function () {
  return { items: Storage.listItems(TUNING_TABLE_COLLECTION) };
});

registerApi_('tuningTableGet', function (ctx) {
  return { item: specWebGetTableOrThrow_(ctx.params.key) };
});

registerApi_('tuningTableCreate', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.EDITOR, 'テーブルの作成には editor 以上の権限が必要です');
  var payload = specWebParsePayload_(ctx.params);
  var key = payload.key;
  specWebValidateTuningKey_(key);
  if (Storage.getItem(TUNING_TABLE_COLLECTION, key)) {
    throw new SpecWebValidationError('キーが既に存在します: ' + key);
  }
  var columns = Array.isArray(payload.columns) ? payload.columns.map(specWebNormalizeColumnDef_) : [];
  var seenColumnKeys = Object.create(null); // prototype 無し（[41] P2-15 と同根: `constructor` 等の key で誤検知しないため）
  columns.forEach(function (c) {
    if (seenColumnKeys[c.key]) throw new SpecWebValidationError('列 key が重複しています: ' + c.key);
    seenColumnKeys[c.key] = true;
  });
  var rows = Array.isArray(payload.rows)
    ? payload.rows.map(function (r) {
        if (!r || typeof r.rowId !== 'string' || !r.rowId) throw new SpecWebValidationError('行の rowId が必要です');
        return { rowId: r.rowId, cells: specWebNormalizeRowCells_(columns, r.cells), comments: [] };
      })
    : [];
  var seenRowIds = Object.create(null); // prototype 無し（[41] P2-15 と同根）
  rows.forEach(function (r) {
    if (seenRowIds[r.rowId]) throw new SpecWebValidationError('rowId が重複しています: ' + r.rowId);
    seenRowIds[r.rowId] = true;
  });
  var locked = !!payload.locked;
  if (locked) {
    specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.ADMIN, 'locked なテーブルの作成には admin 権限が必要です');
  }
  var entry = { kind: 'table', columns: columns, rows: rows, locked: locked, comments: [] };
  // 2026-09-17（[41](../../docs/41_phase6_review_2026-09-17.md) P2-13）: 上の重複チェックは
  // ロックの外なので、`expectedRevision: 0`（=「まだ存在しないこと」）をロックの中で
  // 原子的に要求する（同時作成の 2 件目は 409）。
  var saved = Storage.putItem(TUNING_TABLE_COLLECTION, key, entry, {
    actor: ctx.auth.principal,
    expectedRevision: 0
  });
  return { item: saved };
});

registerApi_('tuningTableDelete', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, 'テーブルの削除には editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var removed = Storage.deleteItem(TUNING_TABLE_COLLECTION, payload.key, { expectedRevision: revision });
  return { removed: removed };
});

/** locked フラグそのものの変更（常に admin 以上）。 */
registerApi_('tuningTableSetLocked', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.ADMIN, 'locked の変更には admin 権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { locked: !!payload.locked },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

registerApi_('tuningTableAddColumn', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, '列の追加には editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var column = specWebNormalizeColumnDef_(payload.column || {});
  if (specWebFindColumn_(existing.columns, column.key)) {
    throw new SpecWebValidationError('列 key が既に存在します: ' + column.key);
  }
  var nextColumns = existing.columns.concat([column]);
  var defaultValue = specWebDefaultValueForType_(column.valueType, column.min, column.enumOptions);
  var nextRows = existing.rows.map(function (row) {
    var cells = Object.assign({}, row.cells);
    cells[column.key] = defaultValue;
    return Object.assign({}, row, { cells: cells });
  });
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { columns: nextColumns, rows: nextRows },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

/** 列削除。docs/32 §4.4 のとおり、該当セルも全行から削除する。 */
registerApi_('tuningTableRemoveColumn', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, '列の削除には editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var columnKey = payload.columnKey;
  if (!specWebFindColumn_(existing.columns, columnKey)) {
    throw new SpecWebNotFoundError('列が存在しません: ' + columnKey);
  }
  var nextColumns = existing.columns.filter(function (c) {
    return c.key !== columnKey;
  });
  var nextRows = existing.rows.map(function (row) {
    var cells = Object.assign({}, row.cells);
    delete cells[columnKey];
    return Object.assign({}, row, { cells: cells });
  });
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { columns: nextColumns, rows: nextRows },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

/**
 * 列定義の変更（型・min/max・enumOptions）。
 * 型が変わる場合は既存セルを新しい型の既定値へ完全リセットする。
 * 型はそのままで min/max/enumOptions だけが変わる場合は、範囲外の数値は
 * クランプ・enum 外の値は選択肢の先頭へ自動修復する（例外にしない）。
 * 実装メモ = docs/32_spec_web.md 参照（要判断だった「型変更時の既存セルの扱い」への回答）。
 */
registerApi_('tuningTableUpdateColumn', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, '列の変更には editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var columnKey = payload.columnKey;
  var oldColumn = specWebFindColumn_(existing.columns, columnKey);
  if (!oldColumn) throw new SpecWebNotFoundError('列が存在しません: ' + columnKey);
  var newColumn = specWebNormalizeColumnDef_(Object.assign({}, oldColumn, payload.patch || {}, { key: columnKey }));

  var nextColumns = existing.columns.map(function (c) {
    return c.key === columnKey ? newColumn : c;
  });
  var nextRows = existing.rows.map(function (row) {
    var coerced = specWebCoerceCellForColumn_(row.cells[columnKey], newColumn, oldColumn.valueType);
    var cells = Object.assign({}, row.cells);
    cells[columnKey] = coerced.value;
    return Object.assign({}, row, { cells: cells });
  });
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { columns: nextColumns, rows: nextRows },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

registerApi_('tuningTableAddRow', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, '行の追加には editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var rowId = payload.rowId;
  if (!rowId || typeof rowId !== 'string') throw new SpecWebValidationError('rowId が必要です');
  var rowIndex = existing.rows.findIndex ? existing.rows.findIndex(function (r) { return r.rowId === rowId; }) : -1;
  if (rowIndex === -1) {
    for (var i = 0; i < existing.rows.length; i++) {
      if (existing.rows[i].rowId === rowId) { rowIndex = i; break; }
    }
  }
  if (rowIndex !== -1) throw new SpecWebValidationError('rowId が既に存在します: ' + rowId);
  var newRow = { rowId: rowId, cells: specWebNormalizeRowCells_(existing.columns, payload.cells), comments: [] };
  var nextRows = existing.rows.concat([newRow]);
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { rows: nextRows },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

registerApi_('tuningTableRemoveRow', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, '行の削除には editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var rowId = payload.rowId;
  var found = false;
  var nextRows = existing.rows.filter(function (r) {
    if (r.rowId === rowId) { found = true; return false; }
    return true;
  });
  if (!found) throw new SpecWebNotFoundError('行が存在しません: ' + rowId);
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { rows: nextRows },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

registerApi_('tuningTableReorderRows', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, '行の並べ替えには editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var order = payload.order;
  if (!Array.isArray(order) || order.length !== existing.rows.length) {
    throw new SpecWebValidationError('order は現在の全 rowId をちょうど 1 回ずつ含む配列である必要があります');
  }
  var byId = {};
  existing.rows.forEach(function (r) {
    byId[r.rowId] = r;
  });
  var seenRowIds = Object.create(null); // prototype 無し（[41] P2-15 と同根）
  var nextRows = order.map(function (rowId) {
    if (!byId[rowId]) throw new SpecWebValidationError('order に存在しない rowId があります: ' + rowId);
    if (seenRowIds[rowId]) throw new SpecWebValidationError('order に重複した rowId があります: ' + rowId);
    seenRowIds[rowId] = true;
    return byId[rowId];
  });
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { rows: nextRows },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

registerApi_('tuningTableUpdateCell', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, 'セルの編集には editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var nextRows = specWebApplyCellUpdates_(existing, [{ rowId: payload.rowId, columnKey: payload.columnKey, value: payload.value }]);
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { rows: nextRows },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

/**
 * 複数セルの一括更新（スプレッドシートからの貼り付け対応、docs/32 §4.4）。
 * 全セルを検証してから 1 回だけ書き込む（一部だけ保存される事故を避ける = all-or-nothing）。
 */
registerApi_('tuningTableUpdateCells', function (ctx) {
  var payload = specWebParsePayload_(ctx.params);
  var existing = specWebGetTableOrThrow_(payload.key);
  specWebRequireTableRole_(ctx.auth, existing, 'セルの編集には editor 以上の権限が必要です');
  var revision = specWebRequireRevision_(payload);
  var updates = Array.isArray(payload.updates) ? payload.updates : [];
  var nextRows = specWebApplyCellUpdates_(existing, updates);
  var saved = Storage.putItem(
    TUNING_TABLE_COLLECTION,
    payload.key,
    { rows: nextRows },
    { actor: ctx.auth.principal, expectedRevision: revision }
  );
  return { item: saved };
});

/** updates（[{rowId,columnKey,value}]）を全件検証してから適用した rows を返す。 */
function specWebApplyCellUpdates_(table, updates) {
  var rowsById = {};
  table.rows.forEach(function (r) {
    rowsById[r.rowId] = r;
  });
  // 先に全件検証（列の存在・値の妥当性）。1 件でも不正なら何も適用しない。
  updates.forEach(function (u) {
    var row = rowsById[u.rowId];
    if (!row) throw new SpecWebNotFoundError('行が存在しません: ' + u.rowId);
    var col = specWebFindColumn_(table.columns, u.columnKey);
    if (!col) throw new SpecWebNotFoundError('列が存在しません: ' + u.columnKey);
    specWebValidateScalarValue_(col.valueType, u.value, col.min, col.max, col.enumOptions, '列 ' + col.key);
  });
  var patchedCellsByRowId = {};
  updates.forEach(function (u) {
    patchedCellsByRowId[u.rowId] = patchedCellsByRowId[u.rowId] || {};
    patchedCellsByRowId[u.rowId][u.columnKey] = u.value;
  });
  return table.rows.map(function (row) {
    var patch = patchedCellsByRowId[row.rowId];
    if (!patch) return row;
    return Object.assign({}, row, { cells: Object.assign({}, row.cells, patch) });
  });
}
