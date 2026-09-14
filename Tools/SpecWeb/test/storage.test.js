'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// W-2 AC: 同時に 2 リクエストが書き込んでも片方が revision 不一致で拒否される。
// (docs/32_spec_web.md §8 W-2 / Storage.js)

test('putItem: 新規作成は revision=1 で保存される', () => {
  const ctx = loadGas();
  const saved = ctx.Storage.putItem('assets', 'Se::Slash', { displayName: '斬撃音' }, { actor: 'a@example.com' });

  assert.equal(saved.revision, 1);
  assert.equal(saved.displayName, '斬撃音');
  assert.equal(saved.updatedBy, 'a@example.com');
  assert.equal(typeof saved.updatedAt, 'string');

  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.deepEqual(reread, saved);
});

test('putItem: expectedRevision が最新と一致すれば更新でき revision が進む', () => {
  const ctx = loadGas();
  const first = ctx.Storage.putItem('tuning', 'Influence/FanBase', { value: 1.0 }, { actor: 'a@example.com' });
  assert.equal(first.revision, 1);

  const second = ctx.Storage.putItem(
    'tuning',
    'Influence/FanBase',
    { value: 2.0 },
    { actor: 'b@example.com', expectedRevision: first.revision }
  );
  assert.equal(second.revision, 2);
  assert.equal(second.value, 2.0);
});

test('putItem: revision が古いままの 2 件目の書き込みは RevisionConflictError で拒否される', () => {
  const ctx = loadGas();
  const first = ctx.Storage.putItem('tuning', 'Influence/FanBase', { value: 1.0 }, { actor: 'a@example.com' });

  // 1 件目が先に書き込んで revision=2 になった状況を作る。
  ctx.Storage.putItem(
    'tuning',
    'Influence/FanBase',
    { value: 2.0 },
    { actor: 'a@example.com', expectedRevision: first.revision }
  );

  // 2 件目は first.revision（古い revision=1）を期待値のまま書き込もうとして拒否される。
  assert.throws(
    () => {
      ctx.Storage.putItem(
        'tuning',
        'Influence/FanBase',
        { value: 3.0 },
        { actor: 'b@example.com', expectedRevision: first.revision }
      );
    },
    (err) => {
      assert.equal(err.name, 'RevisionConflictError');
      assert.equal(err.status, 409);
      assert.equal(err.currentRevision, 2);
      return true;
    }
  );

  // 拒否された側の値は反映されていない（value は 2.0 のまま）。
  const current = ctx.Storage.getItem('tuning', 'Influence/FanBase');
  assert.equal(current.value, 2.0);
  assert.equal(current.revision, 2);
});

test('putItem: ロックは書き込み後に解放され、続けて別の書き込みができる', () => {
  const ctx = loadGas();
  ctx.Storage.putItem('assets', 'Se::A', { displayName: 'A' }, { actor: 'a@example.com' });
  // 直前の putItem がロックを解放していなければ、フェイク LockService が例外を投げる。
  assert.doesNotThrow(() => {
    ctx.Storage.putItem('assets', 'Se::B', { displayName: 'B' }, { actor: 'a@example.com' });
  });
});

test('deleteItem: revision 不一致は RevisionConflictError、存在しない ID は false', () => {
  const ctx = loadGas();
  const saved = ctx.Storage.putItem('assets', 'Se::Slash', { displayName: '斬撃音' }, { actor: 'a@example.com' });

  assert.throws(() => {
    ctx.Storage.deleteItem('assets', 'Se::Slash', { expectedRevision: saved.revision + 1 });
  }, /RevisionConflictError/);

  assert.equal(ctx.Storage.deleteItem('assets', 'Se::NotExist', {}), false);
  assert.equal(ctx.Storage.deleteItem('assets', 'Se::Slash', { expectedRevision: saved.revision }), true);
  assert.equal(ctx.Storage.getItem('assets', 'Se::Slash'), null);
});

test('listItems: 未初期化のコレクションは空扱いで例外にならない', () => {
  const ctx = loadGas();
  // ctx.Storage.listItems() が返すオブジェクトは vm コンテキスト（別の実現域）の
  // Object.prototype を持つため、この Node プロセス側の {} と
  // assert.deepEqual（strict、prototype も比較する）で比較すると
  // 構造が同じでも prototype 不一致で失敗する。キー数だけを見れば十分。
  assert.deepEqual(Object.keys(ctx.Storage.listItems('never-created')), []);
});

test('listItems: 壊れた JSON は（サイレントに空へ戻して上書きしてしまうより安全なため）例外にする', () => {
  const ctx = loadGas({ driveFiles: { 'broken.json': '{ this is not json' } });
  assert.throws(() => ctx.Storage.listItems('broken'), /JSON が壊れています/);
});
