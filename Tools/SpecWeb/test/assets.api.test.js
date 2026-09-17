'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// 2026-09-17 追補（docs/41 P1-3 の修正に追随）: `issueApiToken` 等の**公開名**の運用関数は
// admin セッション必須になった（`specWebAssertAdminSession_`）。ここでのトークン発行・移行・
// ユーザー登録は「テストの前提を組み立てる」ためのものなので、内部実装（末尾 `_`）を直接呼ぶ。
// 公開名の関数が admin セッション無しで必ず失敗することは test/globals.test.js が固定している。

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
  return ctx.getApi_(name)({ params: params || {}, auth: auth || editorAuth() });
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

// 2026-09-17 修正の回帰テスト（docs/41_phase6_review_2026-09-17.md P1-6）。
// 「status が現在値と同じ」patch は検証から外す。以前は、D-Drive の同期で
// 「インポート済」になった発注の表示名やメモを直そうとすると、patch に載っていた
// status（=インポート済、変更なし）が「手動で設定できません」に引っかかり 400 になり、
// クライアント側も status の表示先が無いため保存ボタンが無反応になっていた。
test('assets.update: status が現在値と同じなら検証されない（他フィールドだけ更新できる）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  const updated = call(ctx, 'assets.update', {
    id: created.id,
    expectedRevision: created.revision,
    patch: JSON.stringify({ status: created.status, displayName: '斬撃音（改）' })
  }).item;
  assert.equal(updated.displayName, '斬撃音（改）');
  assert.equal(updated.status, created.status);
});

test('assets.update: インポート済の発注も、status を据え置いたまま他フィールドを保存できる', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;

  // D-Drive の同期（assetState）でのみ「インポート済」へ進む（O-7）。
  ctx.getApi_('assetState')({
    params: {
      payload: JSON.stringify({
        items: [{ id: created.id, created: true, isPlaceholder: false, hasIcon: false, usageCount: 1, lastSyncedAt: '2026-09-17T00:00:00Z' }]
      })
    },
    auth: { ok: true, principal: 'ddrive', email: 'ddrive', role: 'editor', displayName: 'D-Drive' }
  });
  const imported = call(ctx, 'assets.get', { id: created.id }).item;
  assert.equal(imported.status, 'インポート済');

  const updated = call(ctx, 'assets.update', {
    id: imported.id,
    expectedRevision: imported.revision,
    patch: JSON.stringify({ status: 'インポート済', displayName: '斬撃音（納品後に改名）' })
  }).item;
  assert.equal(updated.displayName, '斬撃音（納品後に改名）');
  assert.equal(updated.status, 'インポート済');
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
  const token = ctx.specWebIssueApiToken_('read');
  // 2026-09-14 追補: token は POST（doPost）の本文でのみ受け付ける（§7、routing.test.js 参照)。
  const output = ctx.doPost({ parameter: { api: '1', name: 'whoami', token: token } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
  assert.equal(body.role, 'viewer'); // read トークンは viewer 相当(Auth.js の既存仕様)
});

// 2026-09-17 更新（docs/41 P1-4 / docs/32 §2.3.1(2)）: `?api=1` は API トークン必須になり、
// Google セッションでは通らなくなった（`assets.create` は書き込みトークンの許可リスト外でもある）。
// 人向けの実リクエスト経路は `google.script.run` → `specWebUiCall` なので、そちらで
// 「検証エラーが本文の status 400 で返る」ことを確認する（例外を投げず本文に載せる形は同じ）。
test('specWebUiCall 経由（人向け SPA の実リクエスト相当）: 検証エラーが 400 相当で本文に返る', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const body = ctx.specWebUiCall('assets.create', {
    patch: JSON.stringify(validSePatch({ identifier: 'not-pascal' }))
  });
  assert.equal(body.ok, false);
  assert.equal(body.status, 400);
});

test('?api=1 は Google ログインでは通らない（トークン必須。docs/32 §2.3.1(2)）', () => {
  const ctx = loadGas({
    activeUserEmail: 'editor@example.com',
    driveFiles: usersFixture([{ email: 'editor@example.com', displayName: '編集者', role: 'editor' }])
  });
  const output = ctx.doGet({
    parameter: { api: '1', name: 'assets.create', patch: JSON.stringify(validSePatch()) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 401);
  assert.equal(ctx.Storage.getItem('assets', 'Se::Slash'), null, 'CSRF で作成されない');
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

// 2026-09-17 更新（docs/41 P1-4）: 上と同じ理由で `?api=1` ではなく specWebUiCall 経由にした。
test('specWebUiCall 経由: viewer の書き込みは 403 相当で本文に返る', () => {
  const ctx = loadGas({
    activeUserEmail: 'viewer@example.com',
    driveFiles: usersFixture([{ email: 'viewer@example.com', displayName: '閲覧者', role: 'viewer' }])
  });
  const body = ctx.specWebUiCall('assets.create', { patch: JSON.stringify(validSePatch()) });
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

// ---- O-15: assets.rename（発注後の識別子・種別の変更） ----

test('assets.rename: D-Drive 未作成・インポート済でなければ識別子を変更でき、全フィールド・コメント・orderGroup 所属を引き継ぎ、旧 id は削除される', () => {
  const ctx = loadGas();
  const group = call(ctx, 'orderGroups.create', { patch: JSON.stringify({ name: 'スキル: 斬撃' }) }).item;
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ parentId: group.id })) }).item;
  call(ctx, 'assets.comments.add', { id: created.id, body: '既存コメント' });
  const beforeRename = call(ctx, 'assets.get', { id: created.id }).item;

  const renamed = call(ctx, 'assets.rename', {
    id: created.id, identifier: 'SlashHeavy', expectedRevision: beforeRename.revision
  }).item;

  assert.equal(renamed.id, 'Se::SlashHeavy');
  assert.equal(renamed.identifier, 'SlashHeavy');
  assert.equal(renamed.assetType, 'Se');
  assert.equal(renamed.displayName, '斬撃音');
  assert.equal(renamed.parentId, group.id);
  assert.equal(renamed.comments.length, 1);
  assert.equal(renamed.comments[0].body, '既存コメント');

  assert.throws(() => call(ctx, 'assets.get', { id: 'Se::Slash' }), (err) => {
    assert.equal(err.status, 404);
    return true;
  });
  const reread = call(ctx, 'assets.get', { id: 'Se::SlashHeavy' }).item;
  assert.equal(reread.id, 'Se::SlashHeavy');
});

test('assets.rename: 種別も変更できる（種別+識別子を同時に変更）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  const renamed = call(ctx, 'assets.rename', {
    id: created.id, assetType: 'Vfx', identifier: 'SlashFx', expectedRevision: created.revision
  }).item;
  assert.equal(renamed.id, 'Vfx::SlashFx');
  assert.equal(renamed.assetType, 'Vfx');
  assert.equal(renamed.identifier, 'SlashFx');
});

test('assets.rename: D-Drive で作成済み（ddriveState.created。Placeholder のままで status はまだ発注済でも）だと 400 で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  // isPlaceholder: true にすることで、status は「発注済」のまま（O-7 の自動判定はまだ
  // インポート済へ進めない）で ddriveState.created だけが true の状態を作る
  // （status===imported の分岐とは独立に、ddriveState.created の分岐だけを検証する）。
  call(ctx, 'assetState', {
    payload: JSON.stringify({ items: [{ id: created.id, created: true, isPlaceholder: true }] })
  }, { ok: true, principal: 'ddrive:write', role: 'editor' });
  const afterSync = call(ctx, 'assets.get', { id: created.id }).item;
  assert.equal(afterSync.status, '発注済');
  assert.equal(afterSync.ddriveState.created, true);

  assert.throws(
    () => call(ctx, 'assets.rename', { id: created.id, identifier: 'SlashHeavy' }),
    (err) => {
      assert.equal(err.status, 400);
      assert.match(err.message, /D-Drive で作成済み/);
      return true;
    }
  );
});

test('assets.rename: status が「インポート済」だと 400 で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  call(ctx, 'assetState', {
    payload: JSON.stringify({ items: [{ id: created.id, created: true, isPlaceholder: false }] })
  }, { ok: true, principal: 'ddrive:write', role: 'editor' });
  const imported = call(ctx, 'assets.get', { id: created.id }).item;
  assert.equal(imported.status, 'インポート済');

  assert.throws(
    () => call(ctx, 'assets.rename', { id: created.id, identifier: 'SlashHeavy' }),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('assets.rename: 変更後の種別+識別子が既存の別アセットと重複すると 409 で拒否される', () => {
  const ctx = loadGas();
  const a = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch({ identifier: 'Heavy' })) });

  assert.throws(
    () => call(ctx, 'assets.rename', { id: a.id, identifier: 'Heavy', expectedRevision: a.revision }),
    (err) => {
      assert.equal(err.status, 409);
      return true;
    }
  );
});

test('assets.rename: 種別・識別子のどちらも変わっていなければ 400 で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  assert.throws(
    () => call(ctx, 'assets.rename', { id: created.id, identifier: 'Slash' }),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('assets.rename: 識別子が PascalCase でなければ 400 で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  assert.throws(
    () => call(ctx, 'assets.rename', { id: created.id, identifier: 'slash-heavy' }),
    (err) => {
      assert.equal(err.status, 400);
      return true;
    }
  );
});

test('assets.rename: revision 不一致は 409（currentRevision 付き）で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  call(ctx, 'assets.update', { id: created.id, expectedRevision: created.revision, patch: JSON.stringify({ contractor: '別の人' }) });

  assert.throws(
    () => call(ctx, 'assets.rename', { id: created.id, identifier: 'SlashHeavy', expectedRevision: created.revision }),
    (err) => {
      assert.equal(err.status, 409);
      assert.equal(err.currentRevision, 2);
      return true;
    }
  );
});

test('assets.rename: viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  assert.throws(
    () => call(ctx, 'assets.rename', { id: created.id, identifier: 'SlashHeavy' }, viewerAuth()),
    (err) => {
      assert.equal(err.status, 403);
      return true;
    }
  );
});

test('assets.rename: 存在しない id は 404 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(
    () => call(ctx, 'assets.rename', { id: 'Se::NotExist', identifier: 'X' }),
    (err) => {
      assert.equal(err.status, 404);
      return true;
    }
  );
});

test('handleApiRequest_ 経由: D-Drive の書き込みトークンから assets.rename は呼べない（許可リスト外）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  const token = ctx.specWebIssueApiToken_('write');
  const output = ctx.doPost({
    parameter: { api: '1', name: 'assets.rename', token: token, id: created.id, identifier: 'SlashHeavy' }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403);
});

// ── 2026-09-17（docs/41 P2-14）: リネーム記録（assetRenames）の解決 ──
//
// `assetRenames[X] = {newId}` は「X という発注はもう存在せず newId に移った」という意味しか
// 持たないため、X が再び実在する id になった瞬間に記録は嘘になる。書き込み側（create / rename）で
// 「assetRenames のキーは今は実在しない id だけ」という不変条件を保つ。

test('assets.rename: 旧 id のリンク（?page=order&id=旧id）は新 id へ解決される', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  call(ctx, 'assets.rename', { id: created.id, identifier: 'SlashHeavy', expectedRevision: created.revision });

  assert.equal(ctx.specWebResolveAssetRenameChain_('Se::Slash'), 'Se::SlashHeavy');
  const initial = ctx.resolveInitialScreen_({ page: 'order', id: 'Se::Slash' });
  assert.equal(initial.screen, 'assets');
  assert.equal(initial.params.openId, 'Se::SlashHeavy');
});

test('assets.create: 同じ識別子を作り直すと、その id の古いリネーム記録は消える（別の発注へ飛ばさない）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item;
  call(ctx, 'assets.rename', { id: created.id, identifier: 'SlashHeavy', expectedRevision: created.revision });
  assert.equal(ctx.specWebResolveAssetRenameChain_('Se::Slash'), 'Se::SlashHeavy');

  // 改めて Se::Slash という発注を新規作成する（Se::SlashHeavy とは別物）。
  const recreated = call(ctx, 'assets.create', {
    patch: JSON.stringify(validSePatch({ displayName: '作り直した斬撃音' }))
  }).item;
  assert.equal(recreated.id, 'Se::Slash');

  assert.equal(
    ctx.specWebResolveAssetRenameChain_('Se::Slash'),
    'Se::Slash',
    'コピー済みリンクが Se::SlashHeavy（別の発注）へ飛んでしまわない'
  );
  const initial = ctx.resolveInitialScreen_({ page: 'order', id: 'Se::Slash' });
  assert.equal(initial.params.openId, 'Se::Slash');
});

test('assets.rename: リネーム先が以前の旧 id だった場合も記録が残らない（A→B→A と戻す）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', { patch: JSON.stringify(validSePatch()) }).item; // Se::Slash
  const renamed = call(ctx, 'assets.rename', {
    id: created.id, identifier: 'SlashHeavy', expectedRevision: created.revision
  }).item;
  const back = call(ctx, 'assets.rename', {
    id: renamed.id, identifier: 'Slash', expectedRevision: renamed.revision
  }).item;

  assert.equal(back.id, 'Se::Slash');
  assert.equal(ctx.specWebResolveAssetRenameChain_('Se::Slash'), 'Se::Slash', '自分自身へ戻った id は素通し');
  assert.equal(ctx.specWebResolveAssetRenameChain_('Se::SlashHeavy'), 'Se::Slash');
});
