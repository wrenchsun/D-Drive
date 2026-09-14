'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// O-1/O-5/O-7 AC: アセット発注 CRUD API（一覧・取得・作成・更新・削除・コメント）。
// (docs/32_spec_web.md §10.2.1, §10.3.2, §10.3.4 / Tools/SpecWeb/src/Assets.js)
// 旧 W-4/W-5（未着手/仮/本番/保留・assignee/note）からの移行テストは migration.test.js 参照。

function usersFixture(list) {
  const items = {};
  for (const u of list) {
    items[u.email.toLowerCase()] = { id: u.email.toLowerCase(), email: u.email, displayName: u.displayName, role: u.role, revision: 1 };
  }
  return { 'users.json': JSON.stringify({ items }) };
}

function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}

function viewerAuth() {
  return { ok: true, principal: 'viewer@example.com', email: 'viewer@example.com', role: 'viewer', displayName: '閲覧者' };
}

function call(ctx, name, params, auth) {
  return ctx.getApi(name)({ params: params || {}, auth: auth || editorAuth() });
}

function validSePatch(overrides) {
  return Object.assign(
    { assetType: 'Se', identifier: 'Slash', displayName: '斬撃音', category: 'Player', orderer: 'よしだ', contractor: 'たなか' },
    overrides
  );
}

test('assets.create: editor は新規作成できる（revision=1・status=発注済・orderDate 自動設定・comments=[]・archived=false・ddriveState 既定値）', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) });

  assert.equal(result.item.id, 'Se::Slash');
  assert.equal(result.item.revision, 1);
  assert.equal(result.item.displayName, '斬撃音');
  assert.equal(result.item.status, '発注済');
  assert.equal(typeof result.item.orderDate, 'string');
  assert.match(result.item.orderDate, /^\d{4}-\d{2}-\d{2}$/);
  // result.item は vm コンテキスト（別の実現域）で作られたオブジェクトのため、
  // assert.deepEqual(..., []) は Array のプロトタイプ不一致で失敗する
  // （storage.test.js の既存の注意点と同じ）。length で比較する。
  assert.equal(result.item.comments.length, 0);
  assert.equal(result.item.archived, false);
  assert.equal(result.item.ddriveState.created, false);
  assert.equal(result.item.ddriveState.isPlaceholder, false);
  assert.equal(result.item.params, null);

  const reread = ctx.Storage.getItem('assets', 'Se::Slash');
  assert.equal(reread.id, 'Se::Slash');
});

test('assets.create: status を省略すると既定値「発注済」になる', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', {
    patch: JSON.stringify({ assetType: 'Vfx', identifier: 'FireBall', displayName: '火球' })
  });
  assert.equal(result.item.status, '発注済');
});

test('assets.create: status に「インポート済」を直接指定すると 400 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ status: 'インポート済' })) }),
    (err) => {
      assert.equal(err.status, 400);
      assert.match(err.message, /インポート済/);
      return true;
    }
  );
});

test('assets.create: status=納品済 を直接指定すると deliveredDate が自動記録される', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ status: '納品済' })) });
  assert.equal(result.item.status, '納品済');
  assert.match(result.item.deliveredDate, /^\d{4}-\d{2}-\d{2}$/);
});

test('assets.create: viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }, viewerAuth()),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
});

test('assets.create: 種別+識別子の重複は 409 で拒否される', () => {
  const ctx = loadGas();
  call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) });
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ displayName: '別の斬撃音' })) }),
    (err) => {
      assert.equal(err.status, 409);
      assert.match(err.message, /Se::Slash/);
      return true;
    }
  );
});

test('assets.create: 識別子が PascalCase でなければ 400 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ identifier: 'slash' })) }),
    (err) => {
      assert.equal(err.status, 400);
      assert.match(err.message, /PascalCase/);
      return true;
    }
  );
});

test('assets.create: 種別が選択肢外なら 400 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ assetType: 'NotAType' })) }),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('assets.create: 表示名が無ければ 400 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ displayName: '' })) }),
    (err) => {
      assert.equal(err.status, 400);
      assert.match(err.message, /表示名/);
      return true;
    }
  );
});

test('assets.create: 存在しない parentId（発注グループ）を指定すると 400 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ parentId: 'og_notexist' })) }),
    (err) => {
      assert.equal(err.status, 400);
      assert.match(err.message, /発注グループ/);
      return true;
    }
  );
});

test('assets.create: 実在する parentId（発注グループ）を指定できる', () => {
  const ctx = loadGas();
  const group = call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'スキル: 斬撃' }) }).item;
  const result = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ parentId: group.id })) });
  assert.equal(result.item.parentId, group.id);
});

test('assets.create: patch に ddriveState・params を混ぜても無視される（D-Drive 専用フィールド）', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', {
    patch: JSON.stringify(validSePatch({
      ddriveState: { created: true, isPlaceholder: false, iconAssetId: 'x', usageCount: 99, lastSyncedAt: 'now' },
      params: { concreteType: 'SeData', currentValues: { Volume: 1 } }
    }))
  });
  assert.equal(result.item.ddriveState.created, false);
  assert.equal(result.item.ddriveState.usageCount, 0);
  assert.equal(result.item.params, null);
});

test('assets.get: 存在しない id は 404', () => {
  const ctx = loadGas();
  assert.throws(() => call(ctx, 'assets.get', { id: 'Se::NotExist' }), (err) => {
    assert.equal(err.status, 404);
    return true;
  });
});

test('assets.update: editor は編集でき revision が進む', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  const updated = call(ctx, 'assets.update', {
    id: created.id,
    expectedRevision: created.revision,
    patch: JSON.stringify({ contractor: '山口', referenceMd: '## 参考\n[動画](https://example.com)' })
  }).item;
  assert.equal(updated.revision, 2);
  assert.equal(updated.contractor, '山口');
  assert.equal(updated.referenceMd, '## 参考\n[動画](https://example.com)');
  assert.equal(updated.displayName, '斬撃音'); // 更新していないフィールドは残る
});

test('assets.update: 発注済 → 納品済 に進めると納品日が自動記録され、戻すと消える', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  const delivered = call(ctx, 'assets.update', {
    id: created.id, expectedRevision: created.revision, patch: JSON.stringify({ status: '納品済' })
  }).item;
  assert.equal(delivered.status, '納品済');
  assert.match(delivered.deliveredDate, /^\d{4}-\d{2}-\d{2}$/);

  const reverted = call(ctx, 'assets.update', {
    id: delivered.id, expectedRevision: delivered.revision, patch: JSON.stringify({ status: '発注済' })
  }).item;
  assert.equal(reverted.status, '発注済');
  assert.equal(reverted.deliveredDate, null);
});

test('assets.update: status に「インポート済」を直接指定すると 400 で拒否される（D-Drive 同期専用）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  assert.throws(
    () => call(ctx, 'assets.update', { id: created.id, expectedRevision: created.revision, patch: JSON.stringify({ status: 'インポート済' }) }),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('assets.update: revision 不一致は 409（currentRevision 付き）で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  call(ctx, 'assets.update', { id: created.id, expectedRevision: created.revision, patch: JSON.stringify({ status: '納品済' }) });

  assert.throws(
    () => call(ctx, 'assets.update', { id: created.id, expectedRevision: created.revision, patch: JSON.stringify({ contractor: '別の人' }) }),
    (err) => {
      assert.equal(err.status, 409);
      assert.equal(err.currentRevision, 2);
      return true;
    }
  );
});

test('assets.update: viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  assert.throws(
    () => call(ctx, 'assets.update', { id: created.id, patch: JSON.stringify({ status: '納品済' }) }, viewerAuth()),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
});

test('assets.update: 種別・識別子の変更は拒否される（id=同期のキーのため）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  assert.throws(
    () => call(ctx, 'assets.update', { id: created.id, patch: JSON.stringify({ assetType: 'Vfx' }) }),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
  assert.throws(
    () => call(ctx, 'assets.update', { id: created.id, patch: JSON.stringify({ identifier: 'Other' }) }),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('assets.update: ddriveState・params は patch に含めても書き換えられない', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  const updated = call(ctx, 'assets.update', {
    id: created.id,
    expectedRevision: created.revision,
    patch: JSON.stringify({
      ddriveState: { created: true, isPlaceholder: true, iconAssetId: 'x', usageCount: 5, lastSyncedAt: 'now' },
      params: { concreteType: 'SeData', currentValues: { Volume: 1 } }
    })
  }).item;
  assert.equal(updated.ddriveState.created, false);
  assert.equal(updated.params, null);
});

test('assets.delete → assets.list: 論理削除された項目は既定の一覧から除外され、includeArchived で表示できる', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ status: '納品済' })) }).item;
  call(ctx, 'assets.delete', { id: created.id, expectedRevision: created.revision });

  const defaultList = call(ctx, 'assets.list', {}).items;
  assert.equal(defaultList.length, 0);

  const withArchived = call(ctx, 'assets.list', { includeArchived: '1' }).items;
  assert.equal(withArchived.length, 1);
  assert.equal(withArchived[0].archived, true);
  // status は削除操作で変更されない（アーカイブは独立したフラグという設計決定）。
  assert.equal(withArchived[0].status, '納品済');
});

test('assets.restore: 論理削除を取り消せる', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  const deleted = call(ctx, 'assets.delete', { id: created.id, expectedRevision: created.revision }).item;
  call(ctx, 'assets.restore', { id: created.id, expectedRevision: deleted.revision });

  const defaultList = call(ctx, 'assets.list', {}).items;
  assert.equal(defaultList.length, 1);
  assert.equal(defaultList[0].archived, false);
});

test('assets.delete: viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  assert.throws(() => call(ctx, 'assets.delete', { id: created.id }, viewerAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
});

test('assets.list: 種別・状態・発注者・受注者・Presentation・キーワードで絞り込める', () => {
  const ctx = loadGas();
  const group = call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'スキル: 斬撃' }) }).item;
  call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ orderer: 'よしだ', contractor: 'たなか', parentId: group.id })) });
  call(ctx, 'assets.create', { patch: JSON.stringify({ assetType: 'Vfx', identifier: 'FireBall', displayName: '火球', orderer: '佐々木', contractor: 'さとう' }) });

  assert.equal(call(ctx, 'assets.list', { assetType: 'Vfx' }).items.length, 1);
  assert.equal(call(ctx, 'assets.list', { status: '発注済' }).items.length, 2);
  assert.equal(call(ctx, 'assets.list', { orderer: 'よしだ' }).items.length, 1);
  assert.equal(call(ctx, 'assets.list', { contractor: 'さとう' }).items.length, 1);
  assert.equal(call(ctx, 'assets.list', { query: 'fire' }).items.length, 1);
  assert.equal(call(ctx, 'assets.list', { parentId: group.id }).items.length, 1);
  assert.equal(call(ctx, 'assets.list', { parentId: '__none__' }).items.length, 1);
  assert.equal(call(ctx, 'assets.list', {}).items.length, 2);
});

test('assets.comments.add: コメントを追記でき、author・createdAt が入る', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;

  const afterFirst = call(ctx, 'assets.comments.add', { id: created.id, body: 'この値は要調整' }, editorAuth()).item;
  assert.equal(afterFirst.comments.length, 1);
  assert.equal(afterFirst.comments[0].author, 'editor@example.com');
  assert.equal(afterFirst.comments[0].body, 'この値は要調整');
  assert.equal(afterFirst.comments[0].resolved, false);
  assert.equal(typeof afterFirst.comments[0].createdAt, 'string');

  const afterSecond = call(ctx, 'assets.comments.add', { id: created.id, body: '2件目' }, editorAuth()).item;
  assert.equal(afterSecond.comments.length, 2);
  assert.equal(afterSecond.comments[0].body, 'この値は要調整'); // 既存コメントは失われない
});

test('assets.comments.add: 本文が空なら 400、viewer は 403', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;

  assert.throws(() => call(ctx, 'assets.comments.add', { id: created.id, body: '  ' }), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
  assert.throws(() => call(ctx, 'assets.comments.add', { id: created.id, body: 'x' }, viewerAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
});

test('whoami: 認証情報（role 等）を返す', () => {
  const ctx = loadGas({
    activeUserEmail: 'member@example.com',
    driveFiles: usersFixture([{ email: 'member@example.com', displayName: 'メンバー', role: 'editor' }])
  });
  const token = ctx.issueApiToken('read');
  // 2026-09-14 追補: token は POST（doPost）の本文でのみ受け付ける（§7、routing.test.js 参照)。
  const output = ctx.doPost({ parameter: { api: '1', name: 'whoami', token: token } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
  assert.equal(body.role, 'viewer'); // read トークンは viewer 相当(Auth.js の既存仕様)
});

test('handleApiRequest_ 経由（doGet 実リクエスト相当）: 検証エラーが 400 相当で本文に返る', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const output = ctx.doGet({
    parameter: { api: '1', name: 'assets.create', patch: JSON.stringify(validSePatch({ identifier: 'not-pascal' })) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 400);
});

// ---- O-12: ファイル形式・ファイル名 ----

test('assets.create: fileFormat は先頭ドット無しでも正規化されて保存される（png → .png）', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ fileFormat: 'wav' })) });
  assert.equal(result.item.fileFormat, '.wav');
});

test('assets.create: fileFormat/fileName を省略すると空文字で保存される（必須にしない）', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) });
  assert.equal(result.item.fileFormat, '');
  assert.equal(result.item.fileName, '');
});

test('assets.create: fileName を保存できる（推奨名を使っても自由入力でも可）', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ fileFormat: '.wav', fileName: 'SE_Slash.wav' })) });
  assert.equal(result.item.fileFormat, '.wav');
  assert.equal(result.item.fileName, 'SE_Slash.wav');
});

test('assets.create: fileFormat が長さ上限（20文字）を超えると 400 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ fileFormat: '.' + 'a'.repeat(30) })) }),
    (err) => {
      assert.equal(err.status, 400);
      assert.match(err.message, /ファイル形式/);
      return true;
    }
  );
});

test('assets.create: fileName が長さ上限（255文字）を超えると 400 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ fileName: 'a'.repeat(300) + '.wav' })) }),
    (err) => {
      assert.equal(err.status, 400);
      assert.match(err.message, /ファイル名/);
      return true;
    }
  );
});

test('assets.create/get: fileName に使えない文字が含まれていても保存はブロックされず（例外で止めない）、warnings に含まれる', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ fileName: 'bad/name.wav' })) });
  assert.equal(created.item.fileName, 'bad/name.wav'); // 保存はそのまま通る
  assert.equal(created.warnings.length, 1);
  assert.match(created.warnings[0], /使えない文字/);

  const fetched = call(ctx, 'assets.get', { id: created.item.id });
  assert.equal(fetched.warnings.length, 1);
});

test('assets.create: fileName の拡張子が fileFormat と食い違うと warnings に含まれる（保存はブロックしない）', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ fileFormat: '.wav', fileName: 'SE_Slash.ogg' })) });
  assert.equal(result.item.fileName, 'SE_Slash.ogg');
  assert.equal(result.warnings.length, 1);
  assert.match(result.warnings[0], /一致していません/);
});

test('assets.create: fileFormat/fileName に問題が無ければ warnings は空配列', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ fileFormat: '.wav', fileName: 'SE_Slash.wav' })) });
  assert.equal(result.warnings.length, 0);
});

test('assets.update: fileFormat/fileName を更新でき、正規化・warnings も update 応答に反映される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  const updated = call(ctx, 'assets.update', {
    id: created.id, expectedRevision: created.revision,
    patch: JSON.stringify({ fileFormat: 'ogg', fileName: 'weird*name.ogg' })
  });
  assert.equal(updated.item.fileFormat, '.ogg');
  assert.equal(updated.warnings.length, 1);
  assert.match(updated.warnings[0], /使えない文字/);
});

test('assets.list: fileFormat で絞り込める', () => {
  const ctx = loadGas();
  call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ fileFormat: '.wav' })) });
  call(ctx, 'assets.create', { patch: JSON.stringify({ assetType: 'Vfx', identifier: 'FireBall', displayName: '火球', fileFormat: '.prefab' }) });

  assert.equal(call(ctx, 'assets.list', { fileFormat: '.wav' }).items.length, 1);
  assert.equal(call(ctx, 'assets.list', { fileFormat: '.prefab' }).items.length, 1);
  assert.equal(call(ctx, 'assets.list', {}).items.length, 2);
});

test('handleApiRequest_ 経由: viewer の書き込みは 403 相当で本文に返る', () => {
  const ctx = loadGas({
    activeUserEmail: 'viewer@example.com',
    driveFiles: usersFixture([{ email: 'viewer@example.com', displayName: '閲覧者', role: 'viewer' }])
  });
  const output = ctx.doGet({
    parameter: { api: '1', name: 'assets.create', patch: JSON.stringify(validSePatch()) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});
