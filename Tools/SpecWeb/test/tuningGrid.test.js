'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');

// W-8 AC: グリッドの純粋関数（貼り付けの解析・セル移動・検証表示）の単体テスト。
// (docs/32_spec_web.md §8 W-8 / html/TuningGrid.html)
//
// 戻り値のオブジェクト・配列は vm コンテキスト（別の実現域）で作られるため、
// assert.deepEqual（strict、prototype も比較する）を Node 側のリテラルに対して
// 使うと構造が同じでも失敗する（storage.test.js の注記と同じ理由）。
// JSON.stringify で構造だけを比較する。

function eq(actual, expected) {
  assert.equal(JSON.stringify(actual), JSON.stringify(expected));
}

function grid() {
  return loadHtmlScript('TuningGrid').SpecWebTuningGrid;
}

test('parsePastedText: タブ区切り・複数行をパースし、末尾の空行は捨てる', () => {
  const g = grid();
  const result = g.parsePastedText('10\t1.2\tMelee\n20\t2.0\tRanged\n');
  eq(result, [
    ['10', '1.2', 'Melee'],
    ['20', '2.0', 'Ranged']
  ]);
});

test('parsePastedText: CRLF も改行として扱う', () => {
  const g = grid();
  const result = g.parsePastedText('A\tB\r\nC\tD');
  eq(result, [
    ['A', 'B'],
    ['C', 'D']
  ]);
});

test('parsePastedText: 空文字は空配列', () => {
  const g = grid();
  eq(g.parsePastedText(''), []);
});

test('moveCursor: 矢印キーで移動し、範囲外はクランプされる', () => {
  const g = grid();
  const bounds = { rows: 3, cols: 2 };
  eq(g.moveCursor({ row: 1, col: 0 }, 'ArrowDown', bounds), { row: 2, col: 0 });
  eq(g.moveCursor({ row: 2, col: 0 }, 'ArrowDown', bounds), { row: 2, col: 0 }); // 末尾でクランプ
  eq(g.moveCursor({ row: 0, col: 0 }, 'ArrowUp', bounds), { row: 0, col: 0 }); // 先頭でクランプ
  eq(g.moveCursor({ row: 0, col: 1 }, 'ArrowRight', bounds), { row: 0, col: 1 }); // 末尾列でクランプ
  eq(g.moveCursor({ row: 0, col: 1 }, 'ArrowLeft', bounds), { row: 0, col: 0 });
});

test('moveCursor: Tab は右、Enter は下、ShiftTab は左に移動する', () => {
  const g = grid();
  const bounds = { rows: 3, cols: 3 };
  eq(g.moveCursor({ row: 0, col: 0 }, 'Tab', bounds), { row: 0, col: 1 });
  eq(g.moveCursor({ row: 0, col: 0 }, 'Enter', bounds), { row: 1, col: 0 });
  eq(g.moveCursor({ row: 0, col: 1 }, 'ShiftTab', bounds), { row: 0, col: 0 });
});

test('moveCursor: 未対応キーはそのままの位置を返す', () => {
  const g = grid();
  eq(g.moveCursor({ row: 1, col: 1 }, 'a', { rows: 3, cols: 3 }), { row: 1, col: 1 });
});

test('coerceInputForColumn: float/int/bool/string/enum を型変換する', () => {
  const g = grid();
  assert.equal(g.coerceInputForColumn('1.5', { valueType: 'float' }), 1.5);
  assert.equal(g.coerceInputForColumn('abc', { valueType: 'float' }), null);
  assert.equal(g.coerceInputForColumn('10', { valueType: 'int' }), 10);
  assert.equal(g.coerceInputForColumn('10.5', { valueType: 'int' }), null);
  assert.equal(g.coerceInputForColumn('true', { valueType: 'bool' }), true);
  assert.equal(g.coerceInputForColumn('0', { valueType: 'bool' }), false);
  assert.equal(g.coerceInputForColumn('maybe', { valueType: 'bool' }), null);
  assert.equal(g.coerceInputForColumn('  hi  ', { valueType: 'string' }), 'hi');
  assert.equal(g.coerceInputForColumn('ranged', { valueType: 'enum', enumOptions: ['Melee', 'Ranged'] }), 'Ranged');
  assert.equal(g.coerceInputForColumn('nope', { valueType: 'enum', enumOptions: ['Melee', 'Ranged'] }), null);
});

test('validateCellClient: 範囲外・型違い・enum 外を検出する', () => {
  const g = grid();
  const intCol = { valueType: 'int', min: 1, max: 10 };
  assert.equal(g.validateCellClient(5, intCol).ok, true);
  assert.equal(g.validateCellClient(20, intCol).ok, false);
  assert.equal(g.validateCellClient(0, intCol).ok, false);
  assert.equal(g.validateCellClient(1.5, intCol).ok, false);
  assert.equal(g.validateCellClient(null, intCol).ok, false);

  const enumCol = { valueType: 'enum', enumOptions: ['A', 'B'] };
  assert.equal(g.validateCellClient('A', enumCol).ok, true);
  assert.equal(g.validateCellClient('C', enumCol).ok, false);
});

test('buildCellUpdatesFromPaste: 貼り付け範囲を rowIds/columns に当てはめる', () => {
  const g = grid();
  const columns = [
    { key: 'Hp', valueType: 'int', min: 1, max: 9999 },
    { key: 'Speed', valueType: 'float', min: 0, max: 20 }
  ];
  const rowIds = ['Slime', 'Archer'];
  const pasted = g.parsePastedText('30\t1.5\n40\t2.5');
  const updates = g.buildCellUpdatesFromPaste(pasted, 0, 0, rowIds, columns);
  assert.equal(updates.length, 4);
  eq(updates[0], { rowId: 'Slime', columnKey: 'Hp', value: 30, raw: '30', valid: true });
  eq(updates[1], { rowId: 'Slime', columnKey: 'Speed', value: 1.5, raw: '1.5', valid: true });
  eq(updates[2], { rowId: 'Archer', columnKey: 'Hp', value: 40, raw: '40', valid: true });
  eq(updates[3], { rowId: 'Archer', columnKey: 'Speed', value: 2.5, raw: '2.5', valid: true });
});

test('buildCellUpdatesFromPaste: 表の範囲を超えた貼り付けは切り捨てられる', () => {
  const g = grid();
  const columns = [{ key: 'Hp', valueType: 'int' }];
  const rowIds = ['OnlyRow'];
  const pasted = g.parsePastedText('1\n2\n3'); // 3 行貼っても行は 1 つしかない
  const updates = g.buildCellUpdatesFromPaste(pasted, 0, 0, rowIds, columns);
  assert.equal(updates.length, 1);
  assert.equal(updates[0].rowId, 'OnlyRow');
});

test('buildCellUpdatesFromPaste: 解釈できないセルは valid=false になる（赤表示用）', () => {
  const g = grid();
  const columns = [{ key: 'Hp', valueType: 'int', min: 1, max: 10 }];
  const rowIds = ['R1'];
  const pasted = g.parsePastedText('not-a-number');
  const updates = g.buildCellUpdatesFromPaste(pasted, 0, 0, rowIds, columns);
  assert.equal(updates[0].valid, false);
  assert.equal(updates[0].value, null);
});
