'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// O-1 AC: 既存データ（旧スキーマ）の移行。docs/32_spec_web.md §10.2.2。

function adminAuth() {
  return { ok: true, principal: 'admin@example.com', email: 'admin@example.com', role: 'admin', displayName: '管理者' };
}
function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}

function legacyAssetsFixture(items) {
  return { 'assets.json': JSON.stringify({ items }) };
}

function legacyItem(overrides) {
  return Object.assign(
    {
      id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
      category: 'Player', status: '仮', assignee: 'よしだ', note: '旧メモ', dueDate: '2026-09-30',
      comments: [], archived: false,
      ddriveState: { created: false, isPlaceholder: false, iconAssetId: null, usageCount: 0, lastSyncedAt: null },
      revision: 1, updatedBy: 'x', updatedAt: 'x'
    },
    overrides
  );
}

test('specWebNormalizeLegacyOrderItem_（読み込み時変換）: 旧 assignee/note/旧status を新スキーマとして返す（副作用なし）', () => {
  const ctx = loadGas({ driveFiles: legacyAssetsFixture({ 'Se::Slash': legacyItem() }) });
  const result = ctx.getApi('assets.get')({ params: { id: 'Se::Slash' }, auth: editorAuth() });
  assert.equal(result.item.contractor, 'よしだ');
  assert.equal(result.item.referenceMd, '旧メモ');
  assert.equal(result.item.status, '納品済'); // 旧「仮」→ 新「納品済」
  assert.equal(result.item.orderer, '');
  assert.equal(result.item.parentId, null);
  // O-12（docs/32 §10.2.1 追補）: 既存データに fileFormat/fileName が無くても空文字で読める。
  assert.equal(result.item.fileFormat, '');
  assert.equal(result.item.fileName, '');

  // 副作用が無いこと（Storage 上の生データは変換されていない）。
  const raw = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(raw.status, '仮');
  assert.equal(raw.contractor, undefined);
});

test('assets.list も読み込み時に正規化する（旧データが新スキーマの形で一覧に出る）', () => {
  const ctx = loadGas({ driveFiles: legacyAssetsFixture({ 'Se::Slash': legacyItem({ status: '未着手' }) }) });
  const result = ctx.getApi('assets.list')({ params: {}, auth: editorAuth() });
  assert.equal(result.items.length, 1);
  assert.equal(result.items[0].status, '発注済'); // 旧「未着手」→ 新「発注済」
  assert.equal(result.items[0].contractor, 'よしだ');
});

test('migrateLegacyOrdersToNewSchema: 未着手/仮/本番はそのまま対応する新状態へ、旧フィールドを新フィールドへコピーする', () => {
  const ctx = loadGas({
    driveFiles: legacyAssetsFixture({
      'Se::A': legacyItem({ id: 'Se::A', identifier: 'A', status: '未着手' }),
      'Se::B': legacyItem({ id: 'Se::B', identifier: 'B', status: '仮' }),
      'Se::C': legacyItem({ id: 'Se::C', identifier: 'C', status: '本番' })
    })
  });
  const result = ctx.migrateLegacyOrdersToNewSchema();
  assert.deepEqual(Array.from(result.migratedIds).sort(), ['Se::A', 'Se::B', 'Se::C']);

  assert.equal(ctx.Storage.getItem('assets', 'Se::A').status, '発注済');
  assert.equal(ctx.Storage.getItem('assets', 'Se::B').status, '納品済');
  assert.equal(ctx.Storage.getItem('assets', 'Se::C').status, 'インポート済');
  assert.equal(ctx.Storage.getItem('assets', 'Se::A').contractor, 'よしだ');
  assert.equal(ctx.Storage.getItem('assets', 'Se::A').referenceMd, '旧メモ');
  assert.equal(ctx.Storage.getItem('assets', 'Se::A').orderer, '');
});

test('migrateLegacyOrdersToNewSchema: 保留は発注済に変換し、コメントに「(旧: 保留)」を追記する', () => {
  const ctx = loadGas({ driveFiles: legacyAssetsFixture({ 'Se::Slash': legacyItem({ status: '保留' }) }) });
  ctx.migrateLegacyOrdersToNewSchema();
  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.status, '発注済');
  assert.equal(reread.comments.length, 1);
  assert.match(reread.comments[0].body, /旧: 保留/);
});

test('migrateLegacyOrdersToNewSchema: 冪等（2回実行しても2回目は何も変わらない）', () => {
  const ctx = loadGas({ driveFiles: legacyAssetsFixture({ 'Se::Slash': legacyItem({ status: '保留' }) }) });
  ctx.migrateLegacyOrdersToNewSchema();
  const second = ctx.migrateLegacyOrdersToNewSchema();
  assert.equal(second.migratedIds.length, 0);
  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.comments.length, 1, '2回目の実行でコメントが重複しない');
});

test('migrateLegacyOrdersToNewSchema: 既に新スキーマの項目はスキップされる', () => {
  const ctx = loadGas({
    driveFiles: legacyAssetsFixture({
      'Se::New': legacyItem({ id: 'Se::New', identifier: 'New', status: '発注済', contractor: '山口', orderer: '吉田' })
    })
  });
  const result = ctx.migrateLegacyOrdersToNewSchema();
  assert.equal(result.migratedIds.length, 0);
});

test('migration.runLegacyOrders API: admin のみ実行できる', () => {
  const ctx = loadGas({ driveFiles: legacyAssetsFixture({ 'Se::Slash': legacyItem({ status: '保留' }) }) });
  assert.throws(() => ctx.getApi('migration.runLegacyOrders')({ params: {}, auth: editorAuth() }), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
  const result = ctx.getApi('migration.runLegacyOrders')({ params: {}, auth: adminAuth() });
  assert.equal(result.migratedIds.length, 1);
});
