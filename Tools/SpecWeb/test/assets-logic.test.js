'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');

// W-4/W-5 AC: 一覧の絞り込み・並べ替え・グルーピング・インライン検証・D-Drive 状態バッジ・
// コメント表示順・ロール判定を、DOM に依存しない純粋関数として単体テストする。
// (docs/32_spec_web.md §8 W-4/W-5, §4.1, §4.5 / Tools/SpecWeb/html/AssetsLogic.html)

function load() {
  return loadHtmlScript('AssetsLogic').window.AssetsLogic;
}

test('validateIdentifier: 先頭大文字の英数字のみを PascalCase として受け付ける', () => {
  const logic = load();
  assert.equal(logic.validateIdentifier('Slash'), true);
  assert.equal(logic.validateIdentifier('FireBall2'), true);
  assert.equal(logic.validateIdentifier('slash'), false);
  assert.equal(logic.validateIdentifier('Fire_Ball'), false);
  assert.equal(logic.validateIdentifier(''), false);
});

test('buildAssetId: 種別::識別子 を組み立てる', () => {
  const logic = load();
  assert.equal(logic.buildAssetId('Se', 'Slash'), 'Se::Slash');
});

test('validateAssetFields: 必須項目・書式・選択肢を検証する', () => {
  const logic = load();
  const result = logic.validateAssetFields({ assetType: '', identifier: 'slash', displayName: '' });
  assert.equal(result.valid, false);
  assert.match(result.errors.assetType, /必須/);
  assert.match(result.errors.identifier, /PascalCase/);
  assert.match(result.errors.displayName, /必須/);
});

test('validateAssetFields: 種別+識別子の重複は既存一覧を見て即時に検出する（自分自身は除外）', () => {
  const logic = load();
  const existing = [{ id: 'Se::Slash', assetType: 'Se', identifier: 'Slash' }];

  const dup = logic.validateAssetFields(
    { assetType: 'Se', identifier: 'Slash', displayName: '斬撃音2' },
    { existingItems: existing }
  );
  assert.equal(dup.valid, false);
  assert.match(dup.errors.identifier, /既に存在/);

  const selfEdit = logic.validateAssetFields(
    { assetType: 'Se', identifier: 'Slash', displayName: '斬撃音' },
    { existingItems: existing, excludeId: 'Se::Slash' }
  );
  assert.equal(selfEdit.valid, true);
});

test('validateAssetFields: 状態・優先度・期限の書式も検証する', () => {
  const logic = load();
  const result = logic.validateAssetFields({
    assetType: 'Se',
    identifier: 'Slash',
    displayName: '斬撃音',
    status: '不明な状態',
    priority: '不明',
    dueDate: '2026/09/14'
  });
  assert.equal(result.valid, false);
  assert.ok(result.errors.status);
  assert.ok(result.errors.priority);
  assert.ok(result.errors.dueDate);
});

test('filterAssets: 種別・状態・担当・カテゴリ・キーワードで絞り込み、既定でアーカイブを除外する', () => {
  const logic = load();
  const items = [
    { id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音', status: '仮', assignee: 'よしだ', category: 'Player', archived: false },
    { id: 'Vfx::FireBall', assetType: 'Vfx', identifier: 'FireBall', displayName: '火球', status: '未着手', assignee: 'たなか', category: 'Skill', archived: false },
    { id: 'Se::Old', assetType: 'Se', identifier: 'Old', displayName: '廃止音', status: '保留', assignee: 'よしだ', category: 'Player', archived: true }
  ];

  assert.equal(logic.filterAssets(items, {}).length, 2); // アーカイブは既定で除外
  assert.equal(logic.filterAssets(items, { includeArchived: true }).length, 3);
  assert.equal(logic.filterAssets(items, { assetType: 'Vfx' }).length, 1);
  assert.equal(logic.filterAssets(items, { assignee: 'よしだ' }).length, 1);
  assert.equal(logic.filterAssets(items, { query: 'fire' }).length, 1);
  assert.equal(logic.filterAssets(items, { query: '火球' }).length, 1);
});

test('sortAssets: 指定フィールド・方向で安定ソートする（元配列は変更しない）', () => {
  const logic = load();
  const items = [
    { id: 'b', identifier: 'Banana' },
    { id: 'a', identifier: 'Apple' },
    { id: 'c', identifier: 'Cherry' }
  ];
  const asc = logic.sortAssets(items, 'identifier', 'asc');
  assert.deepEqual(asc.map((i) => i.id), ['a', 'b', 'c']);

  const desc = logic.sortAssets(items, 'identifier', 'desc');
  assert.deepEqual(desc.map((i) => i.id), ['c', 'b', 'a']);

  // 元配列は破壊されない
  assert.equal(items[0].id, 'b');
});

test('groupAssetsByType: 種別ごとにグループ化し、種別名の辞書順に並ぶ', () => {
  const logic = load();
  const items = [
    { id: '1', assetType: 'Vfx' },
    { id: '2', assetType: 'Se' },
    { id: '3', assetType: 'Se' }
  ];
  // groupAssetsByType の戻り値は vm コンテキスト（別の実現域）のオブジェクトのため、
  // assert.deepEqual は Array/Object のプロトタイプ不一致で失敗する
  // （storage.test.js の既存の注意点と同じ）。素の値だけを取り出して比較する。
  const grouped = logic.groupAssetsByType(items);
  assert.deepEqual(Array.from(grouped, (g) => g.assetType), ['Se', 'Vfx']);
  assert.equal(grouped[0].items.length, 2);
  assert.equal(grouped[1].items.length, 1);
});

test('formatDdriveStateBadge: 未作成/Placeholder/作成済を判定する', () => {
  const logic = load();
  // 戻り値は vm コンテキスト（別の実現域）のオブジェクトリテラルのため、
  // assert.deepEqual はプロトタイプ不一致で失敗する。プロパティを個別に比較する。
  function assertBadge(badge, icon, label) {
    assert.equal(badge.icon, icon);
    assert.equal(badge.label, label);
  }
  assertBadge(logic.formatDdriveStateBadge(null), '⬜', '未作成');
  assertBadge(logic.formatDdriveStateBadge({ created: false }), '⬜', '未作成');
  assertBadge(logic.formatDdriveStateBadge({ created: true, isPlaceholder: true }), '🟡', '作成済（Placeholder）');
  assertBadge(logic.formatDdriveStateBadge({ created: true, isPlaceholder: false }), '✅', '作成済');
});

test('sortCommentsNewestFirst: createdAt の新しい順に並べ替える（元配列は変更しない）', () => {
  const logic = load();
  const comments = [
    { id: '1', createdAt: '2026-09-01T00:00:00Z' },
    { id: '2', createdAt: '2026-09-14T00:00:00Z' },
    { id: '3', createdAt: '2026-09-07T00:00:00Z' }
  ];
  const sorted = logic.sortCommentsNewestFirst(comments);
  assert.deepEqual(sorted.map((c) => c.id), ['2', '3', '1']);
  assert.equal(comments[0].id, '1'); // 元配列は破壊されない
});

test('roleAtLeast: viewer < editor < admin の階層で判定する', () => {
  const logic = load();
  assert.equal(logic.roleAtLeast('viewer', 'editor'), false);
  assert.equal(logic.roleAtLeast('editor', 'editor'), true);
  assert.equal(logic.roleAtLeast('admin', 'editor'), true);
  assert.equal(logic.roleAtLeast(null, 'viewer'), false);
});
