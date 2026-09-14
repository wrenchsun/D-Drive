'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// W-12 AC: choices/assetState/tuningUsage の送信 API + レート制限 +
// 「書き込みトークンで choices/assetState/tuningUsage 以外を呼ぶと拒否される」ゲート。
// (docs/32_spec_web.md §8 W-12, §5.2, §7 / Tools/SpecWeb/src/DDriveSync.js, src/Code.js)

function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}

function viewerAuth() {
  return { ok: true, principal: 'viewer@example.com', email: 'viewer@example.com', role: 'viewer', displayName: '閲覧者' };
}

function call(ctx, name, params, auth) {
  return ctx.getApi(name)({ params: params || {}, auth: auth || editorAuth() });
}

function assetsFixture(items) {
  return { 'assets.json': JSON.stringify({ items: items }) };
}

test('choices: editor が送信すると choices コレクションに保存される', () => {
  const ctx = loadGas();
  const result = call(ctx, 'choices', {
    payload: JSON.stringify({ assetTypes: ['Se', 'Vfx'], categories: ['Player'], tags: [] })
  });

  assert.equal(result.item.assetTypes.length, 2);
  const reread = ctx.Storage.getItem('choices', 'current');
  assert.equal(reread.categories[0], 'Player');
});

test('choices: viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'choices', { payload: JSON.stringify({ assetTypes: [] }) }, viewerAuth()),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
});

test('choices: 2 回送信すると revision が積まれる(前回の内容を上書きする)', () => {
  const ctx = loadGas();
  call(ctx, 'choices', { payload: JSON.stringify({ assetTypes: ['Se'] }) });
  const second = call(ctx, 'choices', { payload: JSON.stringify({ assetTypes: ['Se', 'Vfx'] }) });
  assert.equal(second.item.revision, 2);
  assert.equal(second.item.assetTypes.length, 2);
});

test('assetState: 存在する assets の ddriveState だけを patch する(他フィールドは変わらない)', () => {
  const ctx = loadGas({
    driveFiles: assetsFixture({
      'Se::Slash': {
        id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
        category: 'Player', status: '仮', comments: [], archived: false,
        ddriveState: { created: false, isPlaceholder: false, iconAssetId: null, usageCount: 0, lastSyncedAt: null },
        revision: 1, updatedBy: 'x', updatedAt: 'x'
      }
    })
  });

  const result = call(ctx, 'assetState', {
    payload: JSON.stringify({
      items: [{ id: 'Se::Slash', created: true, isPlaceholder: false, usageCount: 3, lastSyncedAt: '2026-09-14T00:00:00Z' }]
    })
  });

  assert.equal(result.updatedIds.length, 1);
  assert.equal(result.skippedIds.length, 0);

  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.ddriveState.created, true);
  assert.equal(reread.ddriveState.usageCount, 3);
  assert.equal(reread.displayName, '斬撃音', 'ddriveState 以外のフィールドは変更されない');
});

test('assetState: Web 側に存在しない id は例外にせず skippedIds に積む', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assetState', {
    payload: JSON.stringify({ items: [{ id: 'Se::NotExist', created: true }] })
  });

  assert.equal(result.updatedIds.length, 0);
  assert.deepEqual(Array.from(result.skippedIds), ['Se::NotExist']);
});

test('assetState: viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assetState', { payload: JSON.stringify({ items: [] }) }, viewerAuth()),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
});

test('tuningUsage: editor が送信すると tuningUsage コレクションに保存される', () => {
  const ctx = loadGas();
  const result = call(ctx, 'tuningUsage', {
    payload: JSON.stringify({ unusedKeys: ['Debug/Unused'] })
  });

  assert.equal(result.item.unusedKeys.length, 1);
  const reread = ctx.Storage.getItem('tuningUsage', 'current');
  assert.equal(reread.unusedKeys[0], 'Debug/Unused');
});

test('レート制限: 同一 principal + API 名で上限を超えると 429 相当で拒否される', () => {
  const ctx = loadGas();
  for (let i = 0; i < 10; i++) {
    call(ctx, 'tuningUsage', { payload: JSON.stringify({ unusedKeys: [] }) });
  }
  assert.throws(
    () => call(ctx, 'tuningUsage', { payload: JSON.stringify({ unusedKeys: [] }) }),
    (err) => {
      assert.equal(err.status, 429);
      return true;
    }
  );
});

test('レート制限: 別の principal は上限の影響を受けない', () => {
  const ctx = loadGas();
  for (let i = 0; i < 10; i++) {
    call(ctx, 'tuningUsage', { payload: JSON.stringify({ unusedKeys: [] }) }, editorAuth());
  }
  const otherAuth = { ok: true, principal: 'other-editor@example.com', role: 'editor' };
  assert.doesNotThrow(() => call(ctx, 'tuningUsage', { payload: JSON.stringify({ unusedKeys: [] }) }, otherAuth));
});

// ── W-12 の中心的なセキュリティ要件: 書き込みトークンの kind 許可リスト(Code.js) ──

test('書き込みトークンで choices/assetState/tuningUsage は呼べる', () => {
  const ctx = loadGas();
  const token = ctx.issueApiToken('write');
  const output = ctx.doGet({
    parameter: { api: '1', name: 'tuningUsage', token: token, payload: JSON.stringify({ unusedKeys: [] }) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
});

test('書き込みトークンで tuningScalarUpdate を呼ぶと 403 で拒否される(値そのものは書き換えられない)', () => {
  const ctx = loadGas();
  const token = ctx.issueApiToken('write');
  const output = ctx.doGet({
    parameter: {
      api: '1',
      name: 'tuningScalarUpdate',
      token: token,
      payload: JSON.stringify({ key: 'Combat/HitStopSec', revision: 1, value: 999 })
    }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

test('書き込みトークンで assets.update を呼ぶと 403 で拒否される(企画が入力した内容は書き換えられない)', () => {
  const ctx = loadGas();
  const token = ctx.issueApiToken('write');
  const output = ctx.doGet({
    parameter: {
      api: '1',
      name: 'assets.update',
      token: token,
      id: 'Se::Slash',
      patch: JSON.stringify({ displayName: '書き換え' })
    }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

test('書き込みトークンで tuningTableUpdateCell を呼ぶと 403 で拒否される', () => {
  const ctx = loadGas();
  const token = ctx.issueApiToken('write');
  const output = ctx.doGet({
    parameter: { api: '1', name: 'tuningTableUpdateCell', token: token, payload: JSON.stringify({ key: 'Enemy/Params' }) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

test('読み取りトークンで choices を呼ぶと editor 未満のため 403 で拒否される(role チェックの方で拒否)', () => {
  const ctx = loadGas();
  const token = ctx.issueApiToken('read');
  const output = ctx.doGet({
    parameter: { api: '1', name: 'choices', token: token, payload: JSON.stringify({ assetTypes: [] }) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

test('Google ログイン(① 相当、admin)は許可リストの制約を受けず tuningScalarUpdate を呼べる', () => {
  const ctx = loadGas({
    activeUserEmail: 'admin@example.com',
    driveFiles: {
      'users.json': JSON.stringify({
        items: { 'admin@example.com': { id: 'admin@example.com', email: 'admin@example.com', displayName: '管理者', role: 'admin', revision: 1 } }
      })
    }
  });
  // 存在しないキーなので 404 になるが、403(kind 許可リスト)には引っかからないことを確認する。
  const output = ctx.doGet({ parameter: { api: '1', name: 'tuningScalarUpdate', payload: JSON.stringify({ key: 'No/Such', revision: 1 }) } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.status, 404);
});
