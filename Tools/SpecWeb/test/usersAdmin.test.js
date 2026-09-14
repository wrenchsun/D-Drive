'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// O-14 AC: ログイン許可（users.json）の Web 管理 API。
// admin のみ許可 / editor・viewer・トークンは拒否 / 最後の admin・自分自身の削除・降格拒否 /
// 正規化・重複（更新扱い） / Drive 共有失敗時も追加・削除自体は成功する。
// docs/32_spec_web.md §11、src/Api/UserAdmin.js。

function usersFixture(list) {
  const items = {};
  for (const u of list) {
    items[u.email.toLowerCase()] = {
      id: u.email.toLowerCase(),
      email: u.email,
      displayName: u.displayName,
      role: u.role,
      revision: 1
    };
  }
  return { 'users.json': JSON.stringify({ items }) };
}

function adminAuth(email) {
  return { ok: true, principal: email || 'admin@example.com', email: email || 'admin@example.com', role: 'admin', displayName: '管理者' };
}
function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}
function viewerAuth() {
  return { ok: true, principal: 'viewer@example.com', email: 'viewer@example.com', role: 'viewer', displayName: '閲覧者' };
}
function ddriveWriteAuth() {
  return { ok: true, principal: 'ddrive:write', tokenKind: 'write', role: 'editor' };
}
function ddriveReadAuth() {
  return { ok: true, principal: 'ddrive:read', tokenKind: 'read', role: 'viewer' };
}

function call(ctx, name, params, auth) {
  return ctx.getApi(name)({ params: params || {}, auth: auth });
}

function twoAdminsFixture() {
  return usersFixture([
    { email: 'admin@example.com', displayName: '管理者', role: 'admin' },
    { email: 'admin2@example.com', displayName: '管理者2', role: 'admin' }
  ]);
}

test('users.list: admin だけ一覧を取得できる', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  const result = call(ctx, 'users.list', {}, adminAuth());
  assert.equal(result.items.length, 1);
  assert.equal(result.items[0].email, 'admin@example.com');
});

test('users.list: editor/viewer/D-Drive トークンは 403 で拒否される', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  [editorAuth(), viewerAuth(), ddriveWriteAuth(), ddriveReadAuth()].forEach((auth) => {
    assert.throws(() => call(ctx, 'users.list', {}, auth), (err) => {
      assert.equal(err.status, 403);
      return true;
    });
  });
});

test('users.upsert: admin が新規ユーザーを追加できる（role 省略時は既定 editor）', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  const result = call(ctx, 'users.upsert', { email: 'New@Example.com', displayName: '新人' }, adminAuth());
  assert.equal(result.item.email, 'new@example.com'); // specWebNormalizeEmail_ で小文字化
  assert.equal(result.item.role, 'editor');

  const list = call(ctx, 'users.list', {}, adminAuth());
  assert.equal(list.items.length, 2);
});

test('users.upsert: editor/viewer/D-Drive トークンは 403 で拒否される', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  [editorAuth(), viewerAuth(), ddriveWriteAuth(), ddriveReadAuth()].forEach((auth) => {
    assert.throws(() => call(ctx, 'users.upsert', { email: 'x@example.com', role: 'viewer' }, auth), (err) => {
      assert.equal(err.status, 403);
      return true;
    });
  });
});

test('users.upsert: メールアドレスの形式が不正なら 400', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  assert.throws(() => call(ctx, 'users.upsert', { email: 'not-an-email', role: 'viewer' }, adminAuth()), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
});

test('users.upsert: role が viewer/editor/admin 以外なら 400', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  assert.throws(() => call(ctx, 'users.upsert', { email: 'x@example.com', role: 'superadmin' }, adminAuth()), (err) => {
    assert.equal(err.status, 400);
    return true;
  });
});

test('users.upsert: 既存のメールアドレスに対する upsert は更新扱いになる（重複は増えない）', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }, { email: 'x@example.com', displayName: '旧名', role: 'viewer' }]) });
  call(ctx, 'users.upsert', { email: 'X@Example.com', displayName: '新名', role: 'editor' }, adminAuth());
  const list = call(ctx, 'users.list', {}, adminAuth());
  assert.equal(list.items.length, 2);
  const updated = list.items.find((u) => u.email === 'x@example.com');
  assert.equal(updated.displayName, '新名');
  assert.equal(updated.role, 'editor');
});

test('users.upsert: 自分自身の admin 権限を降格しようとすると拒否される（他に admin がいても）', () => {
  const ctx = loadGas({ driveFiles: twoAdminsFixture() });
  assert.throws(() => call(ctx, 'users.upsert', { email: 'admin@example.com', role: 'editor' }, adminAuth('admin@example.com')), (err) => {
    assert.equal(err.status, 400);
    assert.match(err.message, /自分自身/);
    return true;
  });
});

test('users.upsert: 他の admin が最後の admin を降格しようとすると拒否される', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  // admin が 1 人だけの状態で、別のセッション（実運用では起こり得ないが、防御として）から降格を試す。
  assert.throws(() => call(ctx, 'users.upsert', { email: 'admin@example.com', role: 'viewer' }, adminAuth('someone-else@example.com')), (err) => {
    assert.equal(err.status, 400);
    assert.match(err.message, /最後の管理者/);
    return true;
  });
});

test('users.upsert: admin が 2 人いれば、他の admin を降格できる', () => {
  const ctx = loadGas({ driveFiles: twoAdminsFixture() });
  const result = call(ctx, 'users.upsert', { email: 'admin2@example.com', role: 'editor' }, adminAuth('admin@example.com'));
  assert.equal(result.item.role, 'editor');
});

test('users.upsert: shareFolder=true でデータフォルダの編集者に追加される', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  const result = call(ctx, 'users.upsert', { email: 'new@example.com', role: 'viewer', shareFolder: true }, adminAuth());
  assert.equal(result.driveShareWarning, undefined);
  const folderId = ctx.__fakes.drive.defaultFolderId;
  assert.ok(ctx.__fakes.drive.editorsByFolder.get(folderId).has('new@example.com'));
});

test('users.upsert: shareFolder=true でも Drive 共有が失敗しても追加自体は成功し、警告が返る', () => {
  const ctx = loadGas({
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]),
    driveShareShouldFail: true
  });
  const result = call(ctx, 'users.upsert', { email: 'new@example.com', role: 'viewer', shareFolder: true }, adminAuth());
  assert.equal(result.item.email, 'new@example.com');
  assert.match(result.driveShareWarning, /フォルダの共有に失敗しました/);
});

test('users.upsert: shareFolder を渡さない/false なら Drive 共有は呼ばれない', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  const result = call(ctx, 'users.upsert', { email: 'new@example.com', role: 'viewer' }, adminAuth());
  assert.equal(result.driveShareWarning, undefined);
  const folderId = ctx.__fakes.drive.defaultFolderId;
  assert.equal(ctx.__fakes.drive.editorsByFolder.get(folderId).has('new@example.com'), false);
});

test('users.remove: admin が他のユーザーを削除できる', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }, { email: 'x@example.com', displayName: 'x', role: 'viewer' }]) });
  const result = call(ctx, 'users.remove', { email: 'x@example.com' }, adminAuth());
  assert.equal(result.removed, true);
  const list = call(ctx, 'users.list', {}, adminAuth());
  assert.equal(list.items.length, 1);
});

test('users.remove: 存在しないメールアドレスはエラーにせず removed:false を返す', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  const result = call(ctx, 'users.remove', { email: 'nobody@example.com' }, adminAuth());
  assert.equal(result.removed, false);
});

test('users.remove: editor/viewer/D-Drive トークンは 403 で拒否される', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }, { email: 'x@example.com', displayName: 'x', role: 'viewer' }]) });
  [editorAuth(), viewerAuth(), ddriveWriteAuth(), ddriveReadAuth()].forEach((auth) => {
    assert.throws(() => call(ctx, 'users.remove', { email: 'x@example.com' }, auth), (err) => {
      assert.equal(err.status, 403);
      return true;
    });
  });
});

test('users.remove: 自分自身は削除できない', () => {
  const ctx = loadGas({ driveFiles: twoAdminsFixture() });
  assert.throws(() => call(ctx, 'users.remove', { email: 'admin@example.com' }, adminAuth('admin@example.com')), (err) => {
    assert.equal(err.status, 400);
    assert.match(err.message, /自分自身/);
    return true;
  });
});

test('users.remove: 最後の admin は削除できない', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  assert.throws(() => call(ctx, 'users.remove', { email: 'admin@example.com' }, adminAuth('someone-else@example.com')), (err) => {
    assert.equal(err.status, 400);
    assert.match(err.message, /最後の管理者/);
    return true;
  });
});

test('users.remove: admin が 2 人いれば、他の admin を削除できる', () => {
  const ctx = loadGas({ driveFiles: twoAdminsFixture() });
  const result = call(ctx, 'users.remove', { email: 'admin2@example.com' }, adminAuth('admin@example.com'));
  assert.equal(result.removed, true);
});

test('users.remove: shareFolderRemove=true で編集者共有が解除される', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }, { email: 'x@example.com', displayName: 'x', role: 'viewer' }]) });
  const folderId = ctx.__fakes.drive.defaultFolderId;
  ctx.__fakes.drive.editorsByFolder.get(folderId).add('x@example.com');

  const result = call(ctx, 'users.remove', { email: 'x@example.com', shareFolderRemove: true }, adminAuth());
  assert.equal(result.removed, true);
  assert.equal(result.driveShareWarning, undefined);
  assert.equal(ctx.__fakes.drive.editorsByFolder.get(folderId).has('x@example.com'), false);
});

test('users.remove: shareFolderRemove=true でも Drive 共有解除が失敗しても削除自体は成功し、警告が返る', () => {
  const ctx = loadGas({
    driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }, { email: 'x@example.com', displayName: 'x', role: 'viewer' }]),
    driveShareShouldFail: true
  });
  const result = call(ctx, 'users.remove', { email: 'x@example.com', shareFolderRemove: true }, adminAuth());
  assert.equal(result.removed, true);
  assert.match(result.driveShareWarning, /フォルダの共有解除に失敗しました/);
});

test('D-Drive の write トークンで users.upsert/users.remove/users.list は呼べない（許可リスト外の API）', () => {
  const ctx = loadGas({ driveFiles: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }]) });
  const token = ctx.issueApiToken('write');
  ['users.list', 'users.upsert', 'users.remove'].forEach((name) => {
    const output = ctx.doPost({ parameter: { api: '1', name: name, token: token, email: 'x@example.com' } });
    const body = JSON.parse(output.getContent());
    assert.equal(body.ok, false);
    assert.equal(body.status, 403);
    assert.match(body.error, /書き込みトークンで呼べる API ではありません/);
  });
});
