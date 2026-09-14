/**
 * users.json 許可リスト（docs/32_spec_web.md §3.4）の管理。
 *
 * 下段の `upsertSpecWebUser`/`removeSpecWebUser`/`listSpecWebUsers` は Web API 経由では
 * 呼び出せない（TokenAdmin.js と同様、ApiRegistry に未登録）。管理者が Apps Script エディタ
 * から直接実行することを想定した関数群で、**最初の admin 登録専用**に残す
 * （これが無いと誰もログインできず①のブートストラップができないため。README §5）。
 *
 * O-14（2026-09-14）: 2 人目以降のメンバー追加・ロール変更・削除は、この節の下に追加した
 * Web API（`users.list`/`users.upsert`/`users.remove`）+ `html/Members.html` の admin 専用
 * セクションから行える（docs/32_spec_web.md §11 参照）。
 */

/**
 * ユーザーを追加・更新する。
 * @param {string} email
 * @param {string} displayName
 * @param {('viewer'|'editor'|'admin')} role
 */
function upsertSpecWebUser(email, displayName, role) {
  if (!email) throw new Error('email は必須です');
  if (SPEC_WEB_ROLE_RANK[role] === undefined) {
    throw new Error('role は viewer/editor/admin のいずれかです: ' + role);
  }
  return Storage.putItem(
    SPEC_WEB_USERS_COLLECTION,
    specWebNormalizeEmail_(email),
    { email: email, displayName: displayName || email, role: role },
    { actor: 'admin-console' }
  );
}

function removeSpecWebUser(email) {
  if (!email) throw new Error('email は必須です');
  return Storage.deleteItem(SPEC_WEB_USERS_COLLECTION, specWebNormalizeEmail_(email), {});
}

function listSpecWebUsers() {
  return Storage.listItems(SPEC_WEB_USERS_COLLECTION);
}

/**
 * O-14: ログイン許可（users.json）の Web 管理 API。admin ロールのみ呼べる
 * （`specWebRequireRole_` がサーバー側で強制する。クライアントの表示制御だけに頼らない）。
 * D-Drive の API トークン（read/write）はロールが viewer/editor 相当にしかならないため
 * （Auth.js の `authenticateRequest`）、これらの API は自動的にトークンでは呼べない
 * （`DDRIVE_WRITE_TOKEN_ALLOWED_APIS` に追加する必要も無い）。
 */

function specWebUsersError_(message, status) {
  var err = new Error(message);
  err.status = status || 400;
  return err;
}

function specWebUsersRequireAdmin_(auth) {
  specWebRequireRole_(auth, SPEC_WEB_ROLES.ADMIN, 'ログイン許可の管理には admin 権限が必要です');
}

var SPEC_WEB_USERS_EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** users コレクション中の admin ロールの人数を数える。 */
function specWebUsersAdminCount_(itemsMap) {
  var count = 0;
  Object.keys(itemsMap).forEach(function (key) {
    if (itemsMap[key] && itemsMap[key].role === SPEC_WEB_ROLES.ADMIN) count++;
  });
  return count;
}

registerApi('users.list', function (ctx) {
  specWebUsersRequireAdmin_(ctx.auth);
  var itemsMap = Storage.listItems(SPEC_WEB_USERS_COLLECTION);
  var items = Object.keys(itemsMap).map(function (key) {
    return itemsMap[key];
  });
  return { items: items };
});

/**
 * 追加・更新（role 変更も同じ API）。
 * 自分自身の admin 権限の降格・最後の admin の降格は拒否する
 * （ロックアウト防止。docs/32_spec_web.md §11）。
 * `shareFolder`（既定 ON はクライアント側のチェックボックスの初期値。ここでは渡された値をそのまま使う）
 * が truthy なら、データフォルダの編集者共有も試みる。失敗しても users.json の追加/更新自体は
 * 成功扱いにし、`driveShareWarning` を返す（CLAUDE.md §0-4「例外で止めない」）。
 */
registerApi('users.upsert', function (ctx) {
  specWebUsersRequireAdmin_(ctx.auth);
  var params = ctx.params;
  var email = specWebNormalizeEmail_(params.email);
  if (!email) {
    throw specWebUsersError_('メールアドレスは必須です', 400);
  }
  if (!SPEC_WEB_USERS_EMAIL_PATTERN.test(email)) {
    throw specWebUsersError_('メールアドレスの形式が不正です: ' + params.email, 400);
  }
  var role = params.role || SPEC_WEB_ROLES.EDITOR;
  if (SPEC_WEB_ROLE_RANK[role] === undefined) {
    throw specWebUsersError_('role は viewer/editor/admin のいずれかです: ' + role, 400);
  }

  var existing = findSpecWebUserByEmail_(email);
  var actorEmail = specWebNormalizeEmail_(ctx.auth.email || '');
  var isSelf = !!actorEmail && actorEmail === email;
  var isDemotion = !!existing && existing.role === SPEC_WEB_ROLES.ADMIN && role !== SPEC_WEB_ROLES.ADMIN;

  if (isDemotion) {
    if (isSelf) {
      throw specWebUsersError_('自分自身の管理者権限を降格することはできません（ロックアウト防止のため）', 400);
    }
    var itemsMapForCount = Storage.listItems(SPEC_WEB_USERS_COLLECTION);
    if (specWebUsersAdminCount_(itemsMapForCount) <= 1) {
      throw specWebUsersError_('最後の管理者を降格することはできません', 400);
    }
  }

  var displayName = params.displayName || (existing && existing.displayName) || email;
  var saved = Storage.putItem(
    SPEC_WEB_USERS_COLLECTION,
    email,
    { email: email, displayName: displayName, role: role },
    { actor: specWebActor_(ctx.auth) }
  );

  var result = { item: saved };
  if (params.shareFolder) {
    try {
      DriveAdapter.addFolderEditor(email);
    } catch (err) {
      result.driveShareWarning = 'フォルダの共有に失敗しました。Drive で手動共有してください。';
    }
  }
  return result;
});

/**
 * 削除。自分自身の削除・最後の admin の削除は拒否する（ロックアウト防止）。
 * `shareFolderRemove` が truthy ならデータフォルダの編集者共有も解除を試みる
 * （失敗しても削除自体は成功扱い）。
 */
registerApi('users.remove', function (ctx) {
  specWebUsersRequireAdmin_(ctx.auth);
  var email = specWebNormalizeEmail_(ctx.params.email);
  if (!email) {
    throw specWebUsersError_('メールアドレスは必須です', 400);
  }
  var existing = findSpecWebUserByEmail_(email);
  if (!existing) {
    return { removed: false };
  }

  var actorEmail = specWebNormalizeEmail_(ctx.auth.email || '');
  var isSelf = !!actorEmail && actorEmail === email;
  if (existing.role === SPEC_WEB_ROLES.ADMIN) {
    if (isSelf) {
      throw specWebUsersError_('自分自身を削除することはできません（管理者ロックアウト防止のため）', 400);
    }
    var itemsMapForCount = Storage.listItems(SPEC_WEB_USERS_COLLECTION);
    if (specWebUsersAdminCount_(itemsMapForCount) <= 1) {
      throw specWebUsersError_('最後の管理者を削除することはできません', 400);
    }
  }

  var removed = Storage.deleteItem(SPEC_WEB_USERS_COLLECTION, email, {});
  var result = { removed: removed };
  if (ctx.params.shareFolderRemove) {
    try {
      DriveAdapter.removeFolderEditor(email);
    } catch (err) {
      result.driveShareWarning = 'フォルダの共有解除に失敗しました。Drive で手動解除してください。';
    }
  }
  return result;
});
