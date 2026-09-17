'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// W-7 AC: 列追加・削除、行追加・削除、セル編集が一貫して保存される。
// 列削除で該当セルも消える。(docs/32_spec_web.md §8 W-7 / §3.2.2 / §4.4)

const EDITOR = { principal: 'editor@example.com', role: 'editor' };
const VIEWER = { principal: 'viewer@example.com', role: 'viewer' };
const ADMIN = { principal: 'admin@example.com', role: 'admin' };

function callApi(ctx, name, params, auth) {
  return ctx.getApi_(name)({ params: params || {}, auth: auth });
}

function createEnemyTable(ctx) {
  const payload = {
    key: 'Enemy/Params',
    columns: [
      { key: 'Hp', valueType: 'int', min: 1, max: 9999, unit: '' },
      { key: 'Speed', valueType: 'float', min: 0, max: 20, unit: 'm/s' },
      { key: 'Type', valueType: 'enum', enumOptions: ['Melee', 'Ranged', 'Boss'] }
    ],
    rows: [
      { rowId: 'Slime', cells: { Hp: 10, Speed: 1.2, Type: 'Melee' } },
      { rowId: 'Archer', cells: { Hp: 20, Speed: 2.0, Type: 'Ranged' } }
    ]
  };
  return callApi(ctx, 'tuningTableCreate', { payload: JSON.stringify(payload) }, EDITOR).item;
}

test('tuningTableCreate: 列定義・行を持つテーブルを作成できる', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  assert.equal(table.kind, 'table');
  assert.equal(table.columns.length, 3);
  assert.equal(table.rows.length, 2);
  assert.equal(table.rows[0].cells.Hp, 10);
});

test('tuningTableCreate: 列の型違反セルは 400 相当で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () =>
      callApi(
        ctx,
        'tuningTableCreate',
        {
          payload: JSON.stringify({
            key: 'Bad/Table',
            columns: [{ key: 'Hp', valueType: 'int', min: 1, max: 10 }],
            rows: [{ rowId: 'R1', cells: { Hp: 999 } }]
          })
        },
        EDITOR
      ),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('viewer はテーブルを作成できない（403 相当）', () => {
  const ctx = loadGas();
  assert.throws(
    () => callApi(ctx, 'tuningTableCreate', { payload: JSON.stringify({ key: 'A/B', columns: [], rows: [] }) }, VIEWER),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
});

test('tuningTableAddColumn: 列を追加すると既存行に既定値のセルが増える', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const updated = callApi(
    ctx,
    'tuningTableAddColumn',
    { payload: JSON.stringify({ key: table.id, revision: table.revision, column: { key: 'Armor', valueType: 'int', min: 0, max: 100 } }) },
    EDITOR
  ).item;
  assert.equal(updated.columns.length, 4);
  assert.equal(updated.rows[0].cells.Armor, 0);
  assert.equal(updated.rows[1].cells.Armor, 0);
});

test('tuningTableAddColumn: 同名の列は重複エラー（400 相当）', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  assert.throws(
    () =>
      callApi(
        ctx,
        'tuningTableAddColumn',
        { payload: JSON.stringify({ key: table.id, revision: table.revision, column: { key: 'Hp', valueType: 'int' } }) },
        EDITOR
      ),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('tuningTableRemoveColumn: 列を削除すると該当セルも全行から消える', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const updated = callApi(
    ctx,
    'tuningTableRemoveColumn',
    { payload: JSON.stringify({ key: table.id, revision: table.revision, columnKey: 'Speed' }) },
    EDITOR
  ).item;
  assert.equal(updated.columns.length, 2);
  assert.equal(updated.columns.find((c) => c.key === 'Speed'), undefined);
  updated.rows.forEach((row) => {
    assert.equal(Object.prototype.hasOwnProperty.call(row.cells, 'Speed'), false);
  });
});

test('tuningTableRemoveColumn: 存在しない列は 404 相当', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  assert.throws(
    () => callApi(ctx, 'tuningTableRemoveColumn', { payload: JSON.stringify({ key: table.id, revision: table.revision, columnKey: 'NoSuch' }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 404);
      return true;
    }
  );
});

test('tuningTableUpdateColumn: 型変更で既存セルは新しい型の既定値へリセットされる', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const updated = callApi(
    ctx,
    'tuningTableUpdateColumn',
    { payload: JSON.stringify({ key: table.id, revision: table.revision, columnKey: 'Speed', patch: { valueType: 'bool' } }) },
    EDITOR
  ).item;
  const col = updated.columns.find((c) => c.key === 'Speed');
  assert.equal(col.valueType, 'bool');
  updated.rows.forEach((row) => {
    assert.equal(row.cells.Speed, false);
  });
});

test('tuningTableUpdateColumn: min/max を狭めると範囲外の既存セルはクランプされる', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  // Archer の Hp=20 のあと、max を 15 に狭める。
  const updated = callApi(
    ctx,
    'tuningTableUpdateColumn',
    { payload: JSON.stringify({ key: table.id, revision: table.revision, columnKey: 'Hp', patch: { max: 15 } }) },
    EDITOR
  ).item;
  const archer = updated.rows.find((r) => r.rowId === 'Archer');
  assert.equal(archer.cells.Hp, 15);
  const slime = updated.rows.find((r) => r.rowId === 'Slime');
  assert.equal(slime.cells.Hp, 10); // 元々範囲内なので変わらない
});

test('tuningTableUpdateColumn: enumOptions を狭めて既存値が外れたら先頭にリセットされる', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const updated = callApi(
    ctx,
    'tuningTableUpdateColumn',
    { payload: JSON.stringify({ key: table.id, revision: table.revision, columnKey: 'Type', patch: { enumOptions: ['Melee', 'Boss'] } }) },
    EDITOR
  ).item;
  const archer = updated.rows.find((r) => r.rowId === 'Archer'); // 元は Ranged
  assert.equal(archer.cells.Type, 'Melee');
});

test('tuningTableAddRow / tuningTableRemoveRow: 行を追加・削除できる', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const withNewRow = callApi(
    ctx,
    'tuningTableAddRow',
    { payload: JSON.stringify({ key: table.id, revision: table.revision, rowId: 'Boss1', cells: { Hp: 500, Speed: 0.5, Type: 'Boss' } }) },
    EDITOR
  ).item;
  assert.equal(withNewRow.rows.length, 3);

  const withoutSlime = callApi(
    ctx,
    'tuningTableRemoveRow',
    { payload: JSON.stringify({ key: table.id, revision: withNewRow.revision, rowId: 'Slime' }) },
    EDITOR
  ).item;
  assert.equal(withoutSlime.rows.length, 2);
  assert.equal(withoutSlime.rows.find((r) => r.rowId === 'Slime'), undefined);
});

test('tuningTableAddRow: 重複 rowId は 400 相当で拒否される', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  assert.throws(
    () => callApi(ctx, 'tuningTableAddRow', { payload: JSON.stringify({ key: table.id, revision: table.revision, rowId: 'Slime', cells: {} }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('tuningTableReorderRows: 行を並べ替えられる', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const reordered = callApi(
    ctx,
    'tuningTableReorderRows',
    { payload: JSON.stringify({ key: table.id, revision: table.revision, order: ['Archer', 'Slime'] }) },
    EDITOR
  ).item;
  // reordered は vm コンテキストのオブジェクトなので、.map() で作った配列も
  // 別の実現域の Array になり Node 側の配列リテラルと deepEqual（strict）できない
  // （storage.test.js の注記と同じ理由）。join した文字列で比較する。
  assert.equal(reordered.rows.map((r) => r.rowId).join(','), 'Archer,Slime');
});

test('tuningTableReorderRows: rowId が不足/重複していると 400 相当', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  assert.throws(
    () => callApi(ctx, 'tuningTableReorderRows', { payload: JSON.stringify({ key: table.id, revision: table.revision, order: ['Slime', 'Slime'] }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('tuningTableUpdateCell: 単一セルを更新できる。範囲外は 400 相当', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const updated = callApi(
    ctx,
    'tuningTableUpdateCell',
    { payload: JSON.stringify({ key: table.id, revision: table.revision, rowId: 'Slime', columnKey: 'Hp', value: 50 }) },
    EDITOR
  ).item;
  assert.equal(updated.rows.find((r) => r.rowId === 'Slime').cells.Hp, 50);

  assert.throws(
    () =>
      callApi(
        ctx,
        'tuningTableUpdateCell',
        { payload: JSON.stringify({ key: table.id, revision: updated.revision, rowId: 'Slime', columnKey: 'Hp', value: 99999 }) },
        EDITOR
      ),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('tuningTableUpdateCells: 貼り付け相当の一括更新は all-or-nothing（1 件でも不正なら何も保存しない）', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  assert.throws(
    () =>
      callApi(
        ctx,
        'tuningTableUpdateCells',
        {
          payload: JSON.stringify({
            key: table.id,
            revision: table.revision,
            updates: [
              { rowId: 'Slime', columnKey: 'Hp', value: 30 },
              { rowId: 'Archer', columnKey: 'Hp', value: 99999 } // 範囲外
            ]
          })
        },
        EDITOR
      ),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
  // 1 件目（Slime.Hp=30）も保存されていないこと。
  const stillOriginal = callApi(ctx, 'tuningTableGet', { key: table.id }, VIEWER).item;
  assert.equal(stillOriginal.rows.find((r) => r.rowId === 'Slime').cells.Hp, 10);

  const applied = callApi(
    ctx,
    'tuningTableUpdateCells',
    {
      payload: JSON.stringify({
        key: table.id,
        revision: table.revision,
        updates: [
          { rowId: 'Slime', columnKey: 'Hp', value: 30 },
          { rowId: 'Archer', columnKey: 'Speed', value: 5 }
        ]
      })
    },
    EDITOR
  ).item;
  assert.equal(applied.rows.find((r) => r.rowId === 'Slime').cells.Hp, 30);
  assert.equal(applied.rows.find((r) => r.rowId === 'Archer').cells.Speed, 5);
});

test('revision 不一致の書き込みは 409 相当で拒否される（列追加）', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  callApi(ctx, 'tuningTableAddColumn', { payload: JSON.stringify({ key: table.id, revision: table.revision, column: { key: 'Armor', valueType: 'int' } }) }, EDITOR);
  assert.throws(
    () => callApi(ctx, 'tuningTableAddColumn', { payload: JSON.stringify({ key: table.id, revision: table.revision, column: { key: 'Shield', valueType: 'int' } }) }, EDITOR),
    (err) => {
      assert.equal(err.name, 'RevisionConflictError');
      assert.equal(err.status, 409);
      return true;
    }
  );
});

test('locked なテーブルは editor からの列追加が 403 相当で拒否され、admin は変更できる', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const locked = callApi(ctx, 'tuningTableSetLocked', { payload: JSON.stringify({ key: table.id, revision: table.revision, locked: true }) }, ADMIN).item;
  assert.equal(locked.locked, true);

  assert.throws(
    () => callApi(ctx, 'tuningTableAddColumn', { payload: JSON.stringify({ key: table.id, revision: locked.revision, column: { key: 'Armor', valueType: 'int' } }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );

  const updated = callApi(
    ctx,
    'tuningTableAddColumn',
    { payload: JSON.stringify({ key: table.id, revision: locked.revision, column: { key: 'Armor', valueType: 'int' } }) },
    ADMIN
  ).item;
  assert.equal(updated.columns.length, 4);
});

test('tuningTableSetLocked: editor からは 403 相当で拒否される', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  assert.throws(
    () => callApi(ctx, 'tuningTableSetLocked', { payload: JSON.stringify({ key: table.id, revision: table.revision, locked: true }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
});

test('tuningTableDelete: revision 一致で削除できる', () => {
  const ctx = loadGas();
  const table = createEnemyTable(ctx);
  const result = callApi(ctx, 'tuningTableDelete', { payload: JSON.stringify({ key: table.id, revision: table.revision }) }, EDITOR);
  assert.equal(result.removed, true);
  assert.throws(() => callApi(ctx, 'tuningTableGet', { key: table.id }, VIEWER), (err) => {
    assert.equal(err.status, 404);
    return true;
  });
});
