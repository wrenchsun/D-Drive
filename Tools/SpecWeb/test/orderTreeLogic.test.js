'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');

// O-2 AC: 発注ツリーで子の件数・納品済/インポート済集計が表示される。「単体」グループに
// 親なし発注がまとまる。(docs/32_spec_web.md §10.2.3・§10.3.1 / html/OrderTreeLogic.html)

function load() {
  return loadHtmlScript('OrderTreeLogic').window.OrderTreeLogic;
}

// buildOrderTree の戻り値は vm コンテキスト（別の実現域）のオブジェクトリテラルのため、
// assert.deepEqual はプロトタイプ不一致で失敗する（storage.test.js 等の既存の注意点と同じ）。
// 個別のプロパティで比較する。
function assertCounts(counts, total, delivered, imported) {
  assert.equal(counts.total, total);
  assert.equal(counts.delivered, delivered);
  assert.equal(counts.imported, imported);
}

function asset(overrides) {
  return Object.assign({ id: 'x', parentId: null, status: '発注済', archived: false }, overrides);
}

test('buildOrderTree: Presentation 発注グループごとに子を集計し、名前の辞書順に並ぶ', () => {
  const logic = load();
  const groups = [
    { id: 'og_b', name: 'B グループ' },
    { id: 'og_a', name: 'A グループ', wbsNo: '1.1' }
  ];
  const assets = [
    asset({ id: '1', parentId: 'og_a', status: '発注済' }),
    asset({ id: '2', parentId: 'og_a', status: '納品済' }),
    asset({ id: '3', parentId: 'og_a', status: 'インポート済' }),
    asset({ id: '4', parentId: 'og_b', status: '発注済' })
  ];
  const tree = logic.buildOrderTree(groups, assets);
  assert.equal(tree.length, 3); // A, B, 単体
  assert.equal(tree[0].name, 'A グループ');
  assert.equal(tree[0].wbsNo, '1.1');
  assertCounts(tree[0].counts, 3, 1, 1);
  assert.equal(tree[1].name, 'B グループ');
  assertCounts(tree[1].counts, 1, 0, 0);
});

test('buildOrderTree: parentId が無い発注は「単体」グループにまとまり、常に最後に来る', () => {
  const logic = load();
  const groups = [{ id: 'og_a', name: 'A' }];
  const assets = [
    asset({ id: '1', parentId: null }),
    asset({ id: '2', parentId: '' }),
    asset({ id: '3', parentId: 'og_a' })
  ];
  const tree = logic.buildOrderTree(groups, assets);
  const last = tree[tree.length - 1];
  assert.equal(last.id, null);
  assert.equal(last.name, '単体');
  assert.equal(last.items.length, 2);
});

test('buildOrderTree: アーカイブ済みの発注は集計から除外される', () => {
  const logic = load();
  const groups = [{ id: 'og_a', name: 'A' }];
  const assets = [
    asset({ id: '1', parentId: 'og_a', archived: true }),
    asset({ id: '2', parentId: 'og_a', archived: false })
  ];
  const tree = logic.buildOrderTree(groups, assets);
  assert.equal(tree[0].counts.total, 1);
});

test('buildOrderTree: 発注グループが無ければ「単体」だけになる', () => {
  const logic = load();
  const tree = load().buildOrderTree([], [asset({ id: '1' })]);
  assert.equal(tree.length, 1);
  assert.equal(tree[0].name, '単体');
});
