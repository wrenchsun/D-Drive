'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// W-6/W-7 AC: コメントの投稿と一覧（スカラー・テーブル全体・テーブル行）。
// (docs/32_spec_web.md §3.6・§4.4・§8 W-6/W-7)

const EDITOR = { principal: 'editor@example.com', role: 'editor' };
const VIEWER = { principal: 'viewer@example.com', role: 'viewer' };

function callApi(ctx, name, params, auth) {
  return ctx.getApi(name)({ params: params || {}, auth: auth });
}

function createScalar(ctx) {
  return callApi(
    ctx,
    'tuningScalarCreate',
    { payload: JSON.stringify({ key: 'Influence/FanBase', valueType: 'float', value: 1.0, min: 0, max: 10 }) },
    EDITOR
  ).item;
}

function createTable(ctx) {
  return callApi(
    ctx,
    'tuningTableCreate',
    {
      payload: JSON.stringify({
        key: 'Enemy/Params',
        columns: [{ key: 'Hp', valueType: 'int', min: 1, max: 9999 }],
        rows: [{ rowId: 'Slime', cells: { Hp: 10 } }]
      })
    },
    EDITOR
  ).item;
}

test('スカラー調整値へのコメント投稿と一覧', () => {
  const ctx = loadGas();
  const scalar = createScalar(ctx);
  const added = callApi(ctx, 'tuningCommentAdd', { payload: JSON.stringify({ targetKind: 'scalar', key: scalar.id, body: 'この値は要調整' }) }, EDITOR);
  assert.equal(added.comment.author, 'editor@example.com');
  assert.equal(added.comment.body, 'この値は要調整');
  assert.equal(added.comment.resolved, false);
  assert.equal(typeof added.comment.id, 'string');

  const list = callApi(ctx, 'tuningCommentList', { targetKind: 'scalar', key: scalar.id }, VIEWER).comments;
  assert.equal(list.length, 1);
  assert.equal(list[0].body, 'この値は要調整');
});

test('テーブル全体へのコメント投稿と一覧', () => {
  const ctx = loadGas();
  const table = createTable(ctx);
  callApi(ctx, 'tuningCommentAdd', { payload: JSON.stringify({ targetKind: 'table', key: table.id, body: '全体コメント' }) }, EDITOR);
  const list = callApi(ctx, 'tuningCommentList', { targetKind: 'table', key: table.id }, VIEWER).comments;
  assert.equal(list.length, 1);
  assert.equal(list[0].body, '全体コメント');
});

test('テーブルの行単位のコメント投稿と一覧（他の行には influence しない）', () => {
  const ctx = loadGas();
  const table = callApi(
    ctx,
    'tuningTableCreate',
    {
      payload: JSON.stringify({
        key: 'Enemy/Params',
        columns: [{ key: 'Hp', valueType: 'int', min: 1, max: 9999 }],
        rows: [
          { rowId: 'Slime', cells: { Hp: 10 } },
          { rowId: 'Archer', cells: { Hp: 20 } }
        ]
      })
    },
    EDITOR
  ).item;

  callApi(ctx, 'tuningCommentAdd', { payload: JSON.stringify({ targetKind: 'tableRow', key: table.id, rowId: 'Slime', body: '初期敵' }) }, EDITOR);

  const slimeComments = callApi(ctx, 'tuningCommentList', { targetKind: 'tableRow', key: table.id, rowId: 'Slime' }, VIEWER).comments;
  assert.equal(slimeComments.length, 1);
  assert.equal(slimeComments[0].body, '初期敵');

  const archerComments = callApi(ctx, 'tuningCommentList', { targetKind: 'tableRow', key: table.id, rowId: 'Archer' }, VIEWER).comments;
  assert.equal(archerComments.length, 0);
});

test('コメント投稿は revision を要求しない（値の編集と競合しない）', () => {
  const ctx = loadGas();
  const scalar = createScalar(ctx);
  // 値を編集して revision を進める（コメント側は古い revision を意識しなくてよいことの確認）。
  callApi(ctx, 'tuningScalarUpdate', { payload: JSON.stringify({ key: scalar.id, value: 5, revision: scalar.revision }) }, EDITOR);

  assert.doesNotThrow(() => {
    callApi(ctx, 'tuningCommentAdd', { payload: JSON.stringify({ targetKind: 'scalar', key: scalar.id, body: '後からコメント' }) }, EDITOR);
  });
  const list = callApi(ctx, 'tuningCommentList', { targetKind: 'scalar', key: scalar.id }, VIEWER).comments;
  assert.equal(list.length, 1);
});

test('viewer はコメントを投稿できない（403 相当）が一覧は見られる', () => {
  const ctx = loadGas();
  const scalar = createScalar(ctx);
  assert.throws(
    () => callApi(ctx, 'tuningCommentAdd', { payload: JSON.stringify({ targetKind: 'scalar', key: scalar.id, body: '駄目' }) }, VIEWER),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
  assert.doesNotThrow(() => callApi(ctx, 'tuningCommentList', { targetKind: 'scalar', key: scalar.id }, VIEWER));
});

test('空のコメント本文は 400 相当で拒否される', () => {
  const ctx = loadGas();
  const scalar = createScalar(ctx);
  assert.throws(
    () => callApi(ctx, 'tuningCommentAdd', { payload: JSON.stringify({ targetKind: 'scalar', key: scalar.id, body: '   ' }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('存在しない行への tableRow コメントは 404 相当', () => {
  const ctx = loadGas();
  const table = createTable(ctx);
  assert.throws(
    () => callApi(ctx, 'tuningCommentAdd', { payload: JSON.stringify({ targetKind: 'tableRow', key: table.id, rowId: 'NoSuch', body: 'x' }) }, EDITOR),
    (err) => {
      assert.equal(err.status, 404);
      return true;
    }
  );
});
