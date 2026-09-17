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

// ── 2026-09-17（docs/41 P2-11 / P2-13 / P2-15） ──

test('mutateMany: N 件の更新を 1 回のロック・1 読み・1 書きで反映する', () => {
  const ctx = loadGas();
  const ids = ['Se::A', 'Se::B', 'Se::C'];
  const saved = ctx.Storage.mutateMany(
    'assets',
    (tx) => ids.map((id) => tx.put(id, { displayName: id })),
    { actor: 'ddrive:write' }
  );

  assert.equal(saved.length, 3);
  assert.equal(
    ctx.__fakes.drive.writeCount('assets.json'),
    1,
    '3 件の反映で assets.json の書き込みは 1 回だけ（1 件ずつ putItem すると 3 回になる）'
  );
  ids.forEach((id) => {
    const item = ctx.Storage.getItem('assets', id);
    assert.equal(item.displayName, id);
    assert.equal(item.revision, 1);
    assert.equal(item.updatedBy, 'ddrive:write');
    assert.equal(typeof item.updatedAt, 'string');
  });
});

test('mutateMany: put は putItem と同じ意味（patch merge・revision 加算・actor 個別指定）', () => {
  const ctx = loadGas();
  ctx.Storage.putItem('assets', 'Se::A', { displayName: 'A', category: 'Player' }, { actor: 'a@example.com' });

  ctx.Storage.mutateMany(
    'assets',
    (tx) => {
      const first = tx.put('Se::A', { displayName: 'A2' });
      assert.equal(first.revision, 2);
      assert.equal(first.category, 'Player', 'patch に無いフィールドは既存値を引き継ぐ');
      // 同じ fn の中での 2 回目は「1 回目の結果」を見てから進む（1 件ずつ putItem したときと同じ）。
      const second = tx.put('Se::A', { status: '納品済' }, { actor: 'ddrive:sync' });
      assert.equal(second.revision, 3);
      assert.equal(second.displayName, 'A2');
      assert.equal(second.updatedBy, 'ddrive:sync');
    },
    { actor: 'b@example.com' }
  );

  const reread = ctx.Storage.getItem('assets', 'Se::A');
  assert.equal(reread.revision, 3);
  assert.equal(reread.status, '納品済');
});

test('mutateMany: expectedRevision の不一致は putItem と同じ 409 で拒否される', () => {
  const ctx = loadGas();
  ctx.Storage.putItem('assets', 'Se::A', { displayName: 'A' }, { actor: 'a@example.com' });
  assert.throws(
    () => ctx.Storage.mutateMany('assets', (tx) => tx.put('Se::A', { displayName: 'x' }, { expectedRevision: 5 })),
    (err) => {
      assert.equal(err.name, 'RevisionConflictError');
      assert.equal(err.status, 409);
      assert.equal(err.currentRevision, 1);
      return true;
    }
  );
});

test('mutateMany: fn が例外を投げたら 1 件も書き込まれない（検査→書き込みを原子的にできる）', () => {
  const ctx = loadGas();
  const writesBefore = ctx.__fakes.drive.writeCount('assets.json');
  assert.throws(() => {
    ctx.Storage.mutateMany('assets', (tx) => {
      tx.put('Se::A', { displayName: 'A' });
      throw new Error('検査に失敗した');
    });
  }, /検査に失敗した/);

  assert.equal(ctx.__fakes.drive.writeCount('assets.json'), writesBefore, '書き込みは行われない');
  assert.equal(ctx.Storage.getItem('assets', 'Se::A'), null);
  // ロックは解放されている（フェイク LockService は再入で例外になる）。
  assert.doesNotThrow(() => ctx.Storage.putItem('assets', 'Se::B', { displayName: 'B' }, { actor: 'x' }));
});

test('mutateMany: 1 件も変更しなければ書き込まない / remove で削除できる', () => {
  const ctx = loadGas();
  ctx.Storage.putItem('assets', 'Se::A', { displayName: 'A' }, { actor: 'x' });
  const writesBefore = ctx.__fakes.drive.writeCount('assets.json');

  const found = ctx.Storage.mutateMany('assets', (tx) => !!tx.get('Se::A'));
  assert.equal(found, true);
  assert.equal(ctx.__fakes.drive.writeCount('assets.json'), writesBefore, '読むだけなら書き込まない');

  const removed = ctx.Storage.mutateMany('assets', (tx) => [tx.remove('Se::A'), tx.remove('Se::NotExist')]);
  assert.deepEqual(Array.from(removed), [true, false]);
  assert.equal(ctx.Storage.getItem('assets', 'Se::A'), null);
});

test('putItem: expectedRevision:0 は「まだ存在しないこと」の原子的な要求になる（P2-13、作成系 API が使う）', () => {
  const ctx = loadGas();
  ctx.Storage.putItem('assets', 'Se::A', { displayName: 'A' }, { actor: 'x', expectedRevision: 0 });
  assert.throws(
    () => ctx.Storage.putItem('assets', 'Se::A', { displayName: '後から来た方' }, { actor: 'y', expectedRevision: 0 }),
    (err) => {
      assert.equal(err.name, 'RevisionConflictError');
      assert.equal(err.status, 409);
      return true;
    }
  );
  assert.equal(ctx.Storage.getItem('assets', 'Se::A').displayName, 'A', '既存が静かに上書きされない');
});

// P2-15: id が Object.prototype のプロパティ名でも壊れない（members の id は貼り付けた表記そのもの）。
['constructor', 'toString', 'hasOwnProperty', '__proto__'].forEach((weirdId) => {
  test('id が "' + weirdId + '" でも getItem/putItem/deleteItem が正しく動く（Object.prototype を透過しない）', () => {
    const ctx = loadGas();
    assert.equal(ctx.Storage.getItem('members', weirdId), null, '未作成なら null（継承プロパティを返さない）');

    const saved = ctx.Storage.putItem('members', weirdId, { label: weirdId, source: 'gantt' }, { actor: 'x' });
    assert.equal(saved.revision, 1, '既存扱いにならず revision=1 で作成される（NaN にならない）');

    const reread = ctx.Storage.getItem('members', weirdId);
    assert.equal(reread.label, weirdId);
    assert.equal(reread.revision, 1);
    assert.deepEqual(Object.keys(ctx.Storage.listItems('members')), [weirdId]);

    const second = ctx.Storage.putItem('members', weirdId, { email: 'x@example.com' }, { actor: 'x' });
    assert.equal(second.revision, 2);
    assert.equal(second.label, weirdId);

    assert.equal(ctx.Storage.deleteItem('members', weirdId, {}), true);
    assert.equal(ctx.Storage.getItem('members', weirdId), null);
  });
});

test('renameItem: 新 id が Object.prototype のプロパティ名でも「既に存在します」にならない', () => {
  const ctx = loadGas();
  const created = ctx.Storage.putItem('members', 'ふつうの表記', { label: 'ふつうの表記' }, { actor: 'x' });
  const renamed = ctx.Storage.renameItem('members', 'ふつうの表記', 'toString', { label: 'toString' }, {
    actor: 'x',
    expectedRevision: created.revision
  });
  assert.equal(renamed.id, 'toString');
  assert.equal(ctx.Storage.getItem('members', 'ふつうの表記'), null);
});
