'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// W-6 AC: 範囲外・型違い・enum 外の書き込みが 400 相当で拒否される。
// locked は editor ロールから拒否される。revision 不一致で拒否される。
// (docs/32_spec_web.md §8 W-6 / §3.2.1 / §3.2.4)

const EDITOR = { principal: 'editor@example.com', role: 'editor' };
const VIEWER = { principal: 'viewer@example.com', role: 'viewer' };
const ADMIN = { principal: 'admin@example.com', role: 'admin' };

function callApi(ctx, name, params, auth) {
  return ctx.getApi_(name)({ params: params || {}, auth: auth });
}

function createFloatEntry(ctx, overrides) {
  const payload = Object.assign(
    {
      key: 'Influence/FanBase',
      valueType: 'float',
      value: 1.0,
      min: 0,
      max: 10,
      step: 0.1,
      unit: '%',
      description: 'ファン1人あたりの影響力の素点',
      group: 'Influence',
      tags: ['Balance'],
      locked: false
    },
    overrides
  );
  return callApi(ctx, 'tuningScalarCreate', { payload: JSON.stringify(payload) }, EDITOR);
}

test('tuningScalarCreate: 正常な float エントリを作成できる（revision=1）', () => {
  const ctx = loadGas();
  const result = createFloatEntry(ctx);
  assert.equal(result.item.revision, 1);
  assert.equal(result.item.kind, 'scalar');
  assert.equal(result.item.value, 1.0);
  // result.item は vm コンテキスト（別の実現域）のオブジェクトなので、
  // comments を Node 側の配列リテラルと assert.deepEqual（strict）で比較すると
  // prototype 不一致で失敗する（storage.test.js の注記と同じ理由）。長さで見る。
  assert.equal(result.item.comments.length, 0);
});

test('tuningScalarCreate: キー書式違反は 400 相当で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => createFloatEntry(ctx, { key: 'invalid key' }),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('tuningScalarCreate: 同じキーの重複作成は拒否される', () => {
  const ctx = loadGas();
  createFloatEntry(ctx);
  assert.throws(() => createFloatEntry(ctx), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
});

test('tuningScalarCreate: viewer ロールは作成できない（403 相当）', () => {
  const ctx = loadGas();
  assert.throws(
    () => callApi(ctx, 'tuningScalarCreate', { payload: JSON.stringify({ key: 'A/B', valueType: 'bool', value: true }) }, VIEWER),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
});

test('範囲外の float 値は 400 相当で拒否される', () => {
  const ctx = loadGas();
  assert.throws(() => createFloatEntry(ctx, { value: 20 }), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
  assert.throws(() => createFloatEntry(ctx, { value: -1 }), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
});

test('型違いの値（bool 型に文字列）は 400 相当で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => callApi(ctx, 'tuningScalarCreate', { payload: JSON.stringify({ key: 'Flag/Debug', valueType: 'bool', value: 'yes' }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('int 型に小数を渡すと 400 相当で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => callApi(ctx, 'tuningScalarCreate', { payload: JSON.stringify({ key: 'Count/Max', valueType: 'int', value: 1.5 }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('enum 型で選択肢外の値は 400 相当で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () =>
      callApi(
        ctx,
        'tuningScalarCreate',
        { payload: JSON.stringify({ key: 'Difficulty/Level', valueType: 'enum', value: 'Nightmare', enumOptions: ['Easy', 'Normal', 'Hard'] }) },
        EDITOR
      ),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('enum 型で選択肢内の値は作成できる', () => {
  const ctx = loadGas();
  const result = callApi(
    ctx,
    'tuningScalarCreate',
    { payload: JSON.stringify({ key: 'Difficulty/Level', valueType: 'enum', value: 'Normal', enumOptions: ['Easy', 'Normal', 'Hard'] }) },
    EDITOR
  );
  assert.equal(result.item.value, 'Normal');
});

test('tuningScalarUpdate: revision が一致すれば editor が値を更新できる', () => {
  const ctx = loadGas();
  const created = createFloatEntry(ctx).item;
  const updated = callApi(
    ctx,
    'tuningScalarUpdate',
    { payload: JSON.stringify({ key: created.id, value: 5, revision: created.revision }) },
    EDITOR
  ).item;
  assert.equal(updated.value, 5);
  assert.equal(updated.revision, 2);
});

test('tuningScalarUpdate: revision が古い（不一致）と 409 相当で拒否される', () => {
  const ctx = loadGas();
  const created = createFloatEntry(ctx).item;
  callApi(ctx, 'tuningScalarUpdate', { payload: JSON.stringify({ key: created.id, value: 2, revision: created.revision }) }, EDITOR);

  assert.throws(
    () => callApi(ctx, 'tuningScalarUpdate', { payload: JSON.stringify({ key: created.id, value: 3, revision: created.revision }) }, EDITOR),
    (err) => {
      assert.equal(err.name, 'RevisionConflictError');
      assert.equal(err.status, 409);
      return true;
    }
  );
});

test('tuningScalarUpdate: 更新後も範囲外の値は 400 相当で拒否される', () => {
  const ctx = loadGas();
  const created = createFloatEntry(ctx).item;
  assert.throws(
    () => callApi(ctx, 'tuningScalarUpdate', { payload: JSON.stringify({ key: created.id, value: 999, revision: created.revision }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('locked な調整値は editor からの更新が 403 相当で拒否され、admin は更新できる', () => {
  const ctx = loadGas();
  const created = callApi(
    ctx,
    'tuningScalarCreate',
    { payload: JSON.stringify({ key: 'Locked/Key', valueType: 'float', value: 1, locked: true }) },
    ADMIN
  ).item;
  assert.equal(created.locked, true);

  assert.throws(
    () => callApi(ctx, 'tuningScalarUpdate', { payload: JSON.stringify({ key: created.id, value: 2, revision: created.revision }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );

  const updated = callApi(
    ctx,
    'tuningScalarUpdate',
    { payload: JSON.stringify({ key: created.id, value: 2, revision: created.revision, locked: true }) },
    ADMIN
  ).item;
  assert.equal(updated.value, 2);
});

test('locked の付け外し自体は editor からは 403 相当で拒否される（admin のみ可）', () => {
  const ctx = loadGas();
  const created = createFloatEntry(ctx).item; // locked:false
  assert.throws(
    () =>
      callApi(
        ctx,
        'tuningScalarUpdate',
        { payload: JSON.stringify({ key: created.id, value: created.value, locked: true, revision: created.revision }) },
        EDITOR
      ),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );

  const updated = callApi(
    ctx,
    'tuningScalarUpdate',
    { payload: JSON.stringify({ key: created.id, value: created.value, locked: true, revision: created.revision }) },
    ADMIN
  ).item;
  assert.equal(updated.locked, true);
});

test('tuningScalarDelete: revision 不一致は拒否、一致すれば削除できる', () => {
  const ctx = loadGas();
  const created = createFloatEntry(ctx).item;
  assert.throws(
    () => callApi(ctx, 'tuningScalarDelete', { payload: JSON.stringify({ key: created.id, revision: created.revision + 1 }) }, EDITOR),
    /RevisionConflictError/
  );
  const result = callApi(ctx, 'tuningScalarDelete', { payload: JSON.stringify({ key: created.id, revision: created.revision }) }, EDITOR);
  assert.equal(result.removed, true);
  assert.equal(callApi(ctx, 'tuningScalarList', {}, VIEWER).items[created.id], undefined);
});

test('tuningScalarList / tuningScalarGet: viewer でも一覧・取得ができる', () => {
  const ctx = loadGas();
  const created = createFloatEntry(ctx).item;
  const list = callApi(ctx, 'tuningScalarList', {}, VIEWER).items;
  assert.equal(Object.keys(list).length, 1);
  const got = callApi(ctx, 'tuningScalarGet', { key: created.id }, VIEWER).item;
  assert.equal(got.id, created.id);
});

test('tuningScalarGet: 存在しないキーは 404 相当', () => {
  const ctx = loadGas();
  assert.throws(() => callApi(ctx, 'tuningScalarGet', { key: 'No/Such' }, VIEWER), (err) => {
    assert.equal(err.status, 404);
    return true;
  });
});

// 2026-09-17（docs/41 P1-4 の仕様変更に追随、docs/32 §2.3.1(2)）: `?api=1` は API トークン
// 必須になり、Google セッションへのフォールバックは廃止された。**認証は入力検証より先**に
// 行われるため、token 無しではキーの形が不正でも 400 ではなく 401 になる。
// 入力検証そのものは上の（getApi_ 直呼びの）テストが担保している。
test('doGet 経由: token 無しの ?api=1 は Google セッションがあっても 401（入力検証より先に拒否）', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: { 'users.json': JSON.stringify({ items: { 'editor@example.com': { id: 'editor@example.com', email: 'editor@example.com', displayName: 'E', role: 'editor', revision: 1 } } }) }
  });
  const output = ctx.doGet({
    parameter: { api: '1', name: 'tuningScalarCreate', payload: JSON.stringify({ key: 'Bad Key', valueType: 'float', value: 1 }) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 401);
});

// 2026-09-17（docs/41 P2-13）: 作成系は expectedRevision:0 で「まだ存在しないこと」を
// ロックの中で要求する。既存キーに対する作成が（メッセージはそのままで）拒否されることを固定する。
test('tuningScalarCreate: 既存キーへの作成は拒否される（重複チェック + expectedRevision:0 の二重防御）', () => {
  const ctx = loadGas();
  const created = createFloatEntry(ctx).item;
  assert.throws(() => createFloatEntry(ctx), (err) => {
    assert.match(err.message, /既に存在します|revision/);
    return true;
  });
  const reread = ctx.Storage.getItem('tuning', created.id);
  assert.equal(reread.revision, 1, '2 件目に静かに上書きされない');
});
