'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// 2026-09-17 追補（docs/41 P1-3 の修正に追随）: `issueApiToken` 等の**公開名**の運用関数は
// admin セッション必須になった（`specWebAssertAdminSession_`）。ここでのトークン発行・移行・
// ユーザー登録は「テストの前提を組み立てる」ためのものなので、内部実装（末尾 `_`）を直接呼ぶ。
// 公開名の関数が admin セッション無しで必ず失敗することは test/globals.test.js が固定している。

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
  return ctx.getApi_(name)({ params: params || {}, auth: auth || editorAuth() });
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

test('assetState: 存在する assets の ddriveState を patch する(displayName 等の他フィールドは変わらない)', () => {
  const ctx = loadGas({
    driveFiles: assetsFixture({
      'Se::Slash': {
        id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
        category: 'Player', status: '納品済', comments: [], archived: false,
        ddriveState: { created: false, isPlaceholder: false, iconAssetId: null, usageCount: 0, lastSyncedAt: null },
        revision: 1, updatedBy: 'x', updatedAt: 'x'
      }
    })
  });

  const result = call(ctx, 'assetState', {
    payload: JSON.stringify({
      items: [{ id: 'Se::Slash', created: true, isPlaceholder: false, hasIcon: true, usageCount: 3, lastSyncedAt: '2026-09-14T00:00:00Z' }]
    })
  });

  assert.equal(result.updatedIds.length, 1);
  assert.equal(result.skippedIds.length, 0);

  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.ddriveState.created, true);
  assert.equal(reread.ddriveState.usageCount, 3);
  assert.equal(reread.ddriveState.hasIcon, true, 'hasIcon(2026-09-14 追補)も patch される');
  assert.equal(reread.displayName, '斬撃音', 'displayName 等の入力項目は変更されない');
});

// O-7 AC: 「インポート済」自動判定（docs/32_spec_web.md §10.4.1）。
// created && !isPlaceholder で自動的にインポート済へ進み、Placeholder に戻ったら納品済へ戻る。

test('assetState(O-7): created かつ isPlaceholder=false なら発注の状態がインポート済へ進む', () => {
  const ctx = loadGas({
    driveFiles: assetsFixture({
      'Se::Slash': {
        id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
        status: '納品済', deliveredDate: '2026-09-10', comments: [], archived: false,
        ddriveState: { created: false, isPlaceholder: false, iconAssetId: null, usageCount: 0, lastSyncedAt: null },
        revision: 1, updatedBy: 'x', updatedAt: 'x'
      }
    })
  });

  const result = call(ctx, 'assetState', {
    payload: JSON.stringify({ items: [{ id: 'Se::Slash', created: true, isPlaceholder: false }] })
  });
  assert.deepEqual(Array.from(result.statusChangedIds), ['Se::Slash']);

  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.status, 'インポート済');
  assert.equal(reread.deliveredDate, '2026-09-10', '既に記録済みの納品日は変更しない');
});

test('assetState(O-7): 納品ボタンを踏まずに直接インポートされた場合は deliveredDate を自動記録する', () => {
  const ctx = loadGas({
    driveFiles: assetsFixture({
      'Se::Slash': {
        id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
        status: '発注済', deliveredDate: null, comments: [], archived: false,
        ddriveState: { created: false, isPlaceholder: false, iconAssetId: null, usageCount: 0, lastSyncedAt: null },
        revision: 1, updatedBy: 'x', updatedAt: 'x'
      }
    })
  });

  call(ctx, 'assetState', { payload: JSON.stringify({ items: [{ id: 'Se::Slash', created: true, isPlaceholder: false }] }) });

  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.status, 'インポート済');
  assert.equal(typeof reread.deliveredDate, 'string');
});

test('assetState(O-7): インポート済が Placeholder に戻ったら納品済へ戻り、コメントが残る', () => {
  const ctx = loadGas({
    driveFiles: assetsFixture({
      'Se::Slash': {
        id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
        status: 'インポート済', deliveredDate: '2026-09-10', comments: [], archived: false,
        ddriveState: { created: true, isPlaceholder: false, iconAssetId: null, usageCount: 0, lastSyncedAt: null },
        revision: 1, updatedBy: 'x', updatedAt: 'x'
      }
    })
  });

  const result = call(ctx, 'assetState', {
    payload: JSON.stringify({ items: [{ id: 'Se::Slash', created: true, isPlaceholder: true }] })
  });
  assert.deepEqual(Array.from(result.statusChangedIds), ['Se::Slash']);

  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.status, '納品済');
  assert.equal(reread.comments.length, 1);
  assert.match(reread.comments[0].body, /Placeholder/);
});

test('assetState(O-7): すでに正しい状態なら status は変更されず statusChangedIds に入らない', () => {
  const ctx = loadGas({
    driveFiles: assetsFixture({
      'Se::Slash': {
        id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
        status: 'インポート済', deliveredDate: '2026-09-10', comments: [], archived: false,
        ddriveState: { created: true, isPlaceholder: false, iconAssetId: null, usageCount: 0, lastSyncedAt: null },
        revision: 1, updatedBy: 'x', updatedAt: 'x'
      }
    })
  });

  const result = call(ctx, 'assetState', {
    payload: JSON.stringify({ items: [{ id: 'Se::Slash', created: true, isPlaceholder: false }] })
  });
  assert.equal(result.statusChangedIds.length, 0);
  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.status, 'インポート済');
  assert.equal(reread.revision, 2, 'ddriveState 自体は毎回書き込むため revision は進む');
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
//
// 2026-09-14 追補: token は POST(doPost)の本文でのみ受け付ける(§7、routing.test.js の
// 「doGet に token を付けると拒否される」を参照)。以下は doPost で呼ぶ。

test('書き込みトークンで choices/assetState/tuningUsage は呼べる', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('write');
  const output = ctx.doPost({
    parameter: { api: '1', name: 'tuningUsage', token: token, payload: JSON.stringify({ unusedKeys: [] }) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
});

// O-6（D-Drive 側、2026-09-14）: assetParams を許可表に追加した。
test('書き込みトークンで assetParams は呼べる（O-6、パラメータスキーマ + 現在値の一方向送信）', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('write');
  const payload = {
    schemas: [{ assetType: 'Se', concreteType: 'SeData', fields: [{ name: 'Volume', type: 'float', tooltip: '', min: 0, max: 1 }] }],
    items: []
  };
  const output = ctx.doPost({
    parameter: { api: '1', name: 'assetParams', token: token, payload: JSON.stringify(payload) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
  assert.deepEqual(Array.from(body.updatedSchemaTypes), ['SeData']);
});

test('書き込みトークンで tuningScalarUpdate を呼ぶと 403 で拒否される(値そのものは書き換えられない)', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('write');
  const output = ctx.doPost({
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
  const token = ctx.specWebIssueApiToken_('write');
  const output = ctx.doPost({
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
  const token = ctx.specWebIssueApiToken_('write');
  const output = ctx.doPost({
    parameter: { api: '1', name: 'tuningTableUpdateCell', token: token, payload: JSON.stringify({ key: 'Enemy/Params' }) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

test('読み取りトークンで choices を呼ぶと editor 未満のため 403 で拒否される(role チェックの方で拒否)', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('read');
  const output = ctx.doPost({
    parameter: { api: '1', name: 'choices', token: token, payload: JSON.stringify({ assetTypes: [] }) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

// 2026-09-17（docs/41 P1-4 の仕様変更に追随）: `?api=1` は API トークン必須になり、Google
// セッションへのフォールバックは廃止された（docs/32 §2.3.1(2)）。admin でログインしていても
// token 無しの `?api=1` は 401 になる。「kind 許可リストは token の種別で決まり、
// 読み取りトークンでは choices すら呼べない」ことは上の 403 のテストで担保している。
test('?api=1 は admin でログインしていても token が無ければ 401（セッションフォールバック廃止）', () => {
  const ctx = loadGas({
    activeUserEmail: 'admin@example.com',
    driveFiles: {
      'users.json': JSON.stringify({
        items: { 'admin@example.com': { id: 'admin@example.com', email: 'admin@example.com', displayName: '管理者', role: 'admin', revision: 1 } }
      })
    }
  });
  const output = ctx.doGet({ parameter: { api: '1', name: 'tuningScalarUpdate', payload: JSON.stringify({ key: 'No/Such', revision: 1 }) } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 401);
});

// ── 2026-09-17（docs/41 P2-11）: 一括反映の書き込み回数 ──

test('assetState: 複数件を送っても assets.json への書き込みは 1 回だけ（Storage.mutateMany）', () => {
  const ctx = loadGas({
    driveFiles: assetsFixture({
      'Se::A': {
        id: 'Se::A', assetType: 'Se', identifier: 'A', displayName: 'A', status: '発注済',
        comments: [], archived: false, revision: 1, updatedBy: 'x', updatedAt: 'x'
      },
      'Se::B': {
        id: 'Se::B', assetType: 'Se', identifier: 'B', displayName: 'B', status: '発注済',
        comments: [], archived: false, revision: 1, updatedBy: 'x', updatedAt: 'x'
      },
      'Se::C': {
        id: 'Se::C', assetType: 'Se', identifier: 'C', displayName: 'C', status: '発注済',
        comments: [], archived: false, revision: 1, updatedBy: 'x', updatedAt: 'x'
      }
    })
  });
  const writesBefore = ctx.__fakes.drive.writeCount('assets.json');

  const result = call(ctx, 'assetState', {
    payload: JSON.stringify({
      items: [
        { id: 'Se::A', created: true, isPlaceholder: false },
        { id: 'Se::B', created: false, isPlaceholder: false },
        { id: 'Se::C', created: true, isPlaceholder: true },
        { id: 'Se::NotExist', created: true, isPlaceholder: false }
      ]
    })
  });

  assert.deepEqual(Array.from(result.updatedIds), ['Se::A', 'Se::B', 'Se::C']);
  assert.deepEqual(Array.from(result.skippedIds), ['Se::NotExist']);
  assert.equal(
    ctx.__fakes.drive.writeCount('assets.json') - writesBefore,
    1,
    '1 件ごとに putItem していた頃は件数ぶん（+状態変更分）書き込んでいた'
  );

  // 反映内容は 1 件ずつ書いていたときと同じ。
  assert.equal(ctx.Storage.getItem('assets', 'Se::A').status, 'インポート済');
  assert.equal(ctx.Storage.getItem('assets', 'Se::B').ddriveState.created, false);
  assert.equal(ctx.Storage.getItem('assets', 'Se::C').ddriveState.isPlaceholder, true);
});

test('assetState: items が空なら Drive への書き込みも読み込みも起きない', () => {
  const ctx = loadGas();
  const writesBefore = ctx.__fakes.drive.writeCount('assets.json');
  const result = call(ctx, 'assetState', { payload: JSON.stringify({ items: [] }) });
  assert.equal(result.updatedIds.length, 0);
  assert.equal(ctx.__fakes.drive.writeCount('assets.json'), writesBefore);
});
