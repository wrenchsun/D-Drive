/**
 * users.json 許可リスト（docs/32_spec_web.md §3.4）の管理。
 *
 * 下段の `upsertSpecWebUser`/`removeSpecWebUser`/`listSpecWebUsers` は、管理者が
 * Apps Script エディタから直接実行することを想定した関数群で、**最初の admin 登録**の
 * ために残している（これが無いと誰もログインできず①のブートストラップができない。README §5）。
 *
 * **2026-09-17 セキュリティ修正（P1-1）**: 以前このコメントには「Web API 経由では
 * 呼び出せない（ApiRegistry に未登録）」と書いてあったが、これは `?api=1` 経路にしか
 * 当てはまらない誤りだった。GAS では**末尾 `_` の無いトップレベル関数はすべて
 * `google.script.run.<名前>()` でクライアントから直接呼べる**ため、`viewer` ロールの
 * メンバー（あるいは許可リスト外で拒否ページを開いただけの Google アカウント）が
 * `google.script.run.upsertSpecWebUser('<自分>', 'x', 'admin')` で admin へ昇格できてしまっていた。
 *
 * そこで:
 *   - 実処理は末尾 `_` の内部関数（`specWebUpsertUser_` 等）へ移した
 *   - 公開名のまま残す 3 関数は `specWebAssertAdminSession_()` で admin を要求する
 *   - ただし `upsertSpecWebUser` だけは「users.json に admin が 1 人も居ないとき」に限り
 *     無条件で許可する（README §5 の最初の 1 人のブートストラップ。以後は admin 必須）
 *
 * O-14（2026-09-14）: 2 人目以降のメンバー追加・ロール変更・削除は、この節の下に追加した
 * Web API（`users.list`/`users.upsert`/`users.remove`）+ `html/Members.html` の admin 専用
 * セクションから行える（docs/32_spec_web.md §11 参照）。
 */

// ---- 内部実装（末尾 `_`。google.script.run からは呼べない） ----

function specWebUpsertUser_(email, displayName, role, actor) {
  if (!email) throw new Error('email は必須です');
  if (SPEC_WEB_ROLE_RANK[role] === undefined) {
    throw new Error('role は viewer/editor/admin のいずれかです: ' + role);
  }
  return Storage.putItem(
    SPEC_WEB_USERS_COLLECTION,
    specWebNormalizeEmail_(email),
    { email: email, displayName: displayName || email, role: role },
    { actor: actor || 'admin-console' }
  );
}

function specWebRemoveUser_(email) {
  if (!email) throw new Error('email は必須です');
  return Storage.deleteItem(SPEC_WEB_USERS_COLLECTION, specWebNormalizeEmail_(email), {});
}

function specWebListUsers_() {
  return Storage.listItems(SPEC_WEB_USERS_COLLECTION);
}

/** users.json に admin が 1 人も居ない（＝まだ誰もログインできない）状態か。 */
function specWebHasNoAdmin_() {
  try {
    return specWebUsersAdminCount_(Storage.listItems(SPEC_WEB_USERS_COLLECTION)) === 0;
  } catch (err) {
    // users.json がまだ無い/壊れている場合もブートストラップが必要な状態とみなす
    // （CLAUDE.md §0-4「例外で止めない」）。
    return true;
  }
}

// ---- 運用者がエディタから実行する公開関数 ----

/**
 * ユーザーを追加・更新する。
 *
 * admin が 1 人も居ないとき（最初の 1 人のブートストラップ、README §5）だけ無条件に許可し、
 * それ以降は admin セッションを必須にする。
 *
 * @param {string} email
 * @param {string} displayName
 * @param {('viewer'|'editor'|'admin')} role
 */
function upsertSpecWebUser(email, displayName, role) {
  if (!specWebHasNoAdmin_()) {
    specWebAssertAdminSession_('ログイン許可の追加・更新（upsertSpecWebUser）');
  }
  return specWebUpsertUser_(email, displayName, role);
}

function removeSpecWebUser(email) {
  specWebAssertAdminSession_('ログイン許可の削除（removeSpecWebUser）');
  return specWebRemoveUser_(email);
}

function listSpecWebUsers() {
  specWebAssertAdminSession_('ログイン許可の一覧（listSpecWebUsers）');
  return specWebListUsers_();
}

/**
 * O-14: ログイン許可（users.json）の Web 管理 API。admin ロールのみ呼べる
 * （`specWebRequireRole_` がサーバー側で強制する。クライアントの表示制御だけに頼らない）。
 * D-Drive の API トークン（read/write）はロールが viewer/editor 相当にしかならないため
 * （Auth.js の `authenticateRequest_`）、これらの API は自動的にトークンでは呼べない
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

registerApi_('users.list', function (ctx) {
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
registerApi_('users.upsert', function (ctx) {
  specWebUsersRequireAdmin_(ctx.auth);
  var params = ctx.params;
  var email = specWebNormalizeEmail_(params.email);
  if (!email) {
    throw specWebUsersError_('メールアドレスは必須です', 400);
  }
  if (!SPEC_WEB_USERS_EMAIL_PATTERN.test(email)) {
    throw specWebUsersError_('メールアドレスの形式が不正です: ' + params.email, 400);
  }
  var actorEmail = specWebNormalizeEmail_(ctx.auth.email || '');
  var isSelf = !!actorEmail && actorEmail === email;

  // 2026-09-17（[41](../../../docs/41_phase6_review_2026-09-17.md) P2-17）:
  // 「admin の人数を数える → 書き込む」の間に排他が無かったため、admin が 2 人のときに
  // 双方が同時に相手を降格すると、両方が「admin は 2 人」の検査を通って admin が 0 人になり
  // 誰もログイン許可を触れなくなった（ロックアウト）。検査と書き込みを 1 回の
  // Storage.mutateMany（= 1 回の withStorageLock_）の中で行う。
  var saved = Storage.mutateMany(SPEC_WEB_USERS_COLLECTION, function (tx) {
    var existing = tx.get(email);
    // 2026-09-17（[41] 整理項目）: role 省略時の既定は「既存があればそのロール」。
    // 以前は無条件に editor だったため、表示名だけ直すつもりで role を省くと admin が降格した。
    var role = params.role || (existing && existing.role) || SPEC_WEB_ROLES.EDITOR;
    if (SPEC_WEB_ROLE_RANK[role] === undefined) {
      throw specWebUsersError_('role は viewer/editor/admin のいずれかです: ' + role, 400);
    }
    var isDemotion = !!existing && existing.role === SPEC_WEB_ROLES.ADMIN && role !== SPEC_WEB_ROLES.ADMIN;
    if (isDemotion) {
      if (isSelf) {
        throw specWebUsersError_('自分自身の管理者権限を降格することはできません（ロックアウト防止のため）', 400);
      }
      if (specWebUsersAdminCount_(tx.list()) <= 1) {
        throw specWebUsersError_('最後の管理者を降格することはできません', 400);
      }
    }
    var displayName = params.displayName || (existing && existing.displayName) || email;
    return tx.put(email, { email: email, displayName: displayName, role: role });
  }, { actor: specWebActor_(ctx.auth) });

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
registerApi_('users.remove', function (ctx) {
  specWebUsersRequireAdmin_(ctx.auth);
  var email = specWebNormalizeEmail_(ctx.params.email);
  if (!email) {
    throw specWebUsersError_('メールアドレスは必須です', 400);
  }
  var actorEmail = specWebNormalizeEmail_(ctx.auth.email || '');
  var isSelf = !!actorEmail && actorEmail === email;

  // 2026-09-17（[41] P2-17）: upsert と同じ理由で、「最後の admin か」の検査と削除を
  // 1 回のロックの中で行う（admin 2 人が同時に相手を削除すると admin が 0 人になっていた）。
  var removed = Storage.mutateMany(SPEC_WEB_USERS_COLLECTION, function (tx) {
    var existing = tx.get(email);
    if (!existing) return false;
    if (existing.role === SPEC_WEB_ROLES.ADMIN) {
      if (isSelf) {
        throw specWebUsersError_('自分自身を削除することはできません（管理者ロックアウト防止のため）', 400);
      }
      if (specWebUsersAdminCount_(tx.list()) <= 1) {
        throw specWebUsersError_('最後の管理者を削除することはできません', 400);
      }
    }
    return tx.remove(email);
  });

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
