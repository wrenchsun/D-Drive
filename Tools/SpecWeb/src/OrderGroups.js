/**
 * O-2: Presentation 発注グループ（`orderGroups` コレクション）の CRUD API。
 * docs/32_spec_web.md §10.2.3（データモデル）・§10.3.1（発注ツリー画面）を実装したもの。
 *
 * 「ルートは Presentation」（ユーザー要件2）を表す新しいトップレベルのコレクション。
 * 子（アセット発注、Assets.js）は `parentId` にこの `id` を持つ。集計（件数・納品済数・
 * インポート済数）は保存せず、一覧・発注ツリー画面が子を都度集計して表示する
 * （docs/27 §2.1「値そのものは二重に書かない」の原則を継承。集計ロジックは
 * html/OrderTreeLogic.html の純粋関数に持たせる）。
 *
 * 「単体発注」（parentId が無い発注）は専用のレコードを作らない。表示側のグルーピングだけ。
 *
 * 登録している API:
 *   - orderGroups.list    一覧
 *   - orderGroups.get     1 件取得
 *   - orderGroups.create  新規作成（presentationIdentifier はまだ D-Drive に無くても自由入力可）
 *   - orderGroups.update  更新（revision 楽観ロック）
 *   - orderGroups.delete  削除（子（parentId で参照する発注）が残っている場合は 400 で拒否）
 */

var SPEC_WEB_ORDER_GROUPS_COLLECTION = 'orderGroups';

function specWebOrderGroupsError_(message, status) {
  var err = new Error(message);
  err.status = status || 400;
  return err;
}

function specWebOrderGroupsRequireEditor_(auth) {
  if (!hasRole_(auth, SPEC_WEB_ROLES.EDITOR)) {
    throw specWebOrderGroupsError_('この操作には編集権限が必要です（viewer は読み取りのみ）', 403);
  }
}

function specWebGenerateOrderGroupId_() {
  return 'og_' + UtilitiesAdapter.newUuid().replace(/-/g, '');
}

var SPEC_WEB_ORDER_GROUP_WRITABLE_FIELDS = ['name', 'presentationIdentifier', 'wbsNo', 'orderer', 'dueDate', 'referenceMd'];

function specWebParseOrderGroupPatch_(params) {
  var raw = params.patch;
  if (raw === undefined || raw === null || raw === '') return {};
  var parsed;
  try {
    parsed = JSON.parse(raw);
  } catch (err) {
    throw specWebOrderGroupsError_('patch は JSON 文字列で渡してください', 400);
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw specWebOrderGroupsError_('patch はオブジェクトである必要があります', 400);
  }
  var sanitized = {};
  SPEC_WEB_ORDER_GROUP_WRITABLE_FIELDS.forEach(function (key) {
    if (Object.prototype.hasOwnProperty.call(parsed, key)) sanitized[key] = parsed[key];
  });
  return sanitized;
}

function specWebValidateOrderGroupFields_(fields, options) {
  options = options || {};
  var errors = {};
  // 2026-09-17（[41](../../docs/41_phase6_review_2026-09-17.md) 整理項目）:
  // `specWebUiCall`（google.script.run）はプレーン JSON をそのまま渡すため、`e.parameter` 経由と
  // 違って数値・オブジェクト・配列が届き得る。書き込み可能フィールドは全て文字列なので型を先に弾く
  // （null / undefined は「未設定」として従来どおり許容）。
  SPEC_WEB_ORDER_GROUP_WRITABLE_FIELDS.forEach(function (key) {
    if (!Object.prototype.hasOwnProperty.call(fields, key)) return;
    var value = fields[key];
    if (value === undefined || value === null) return;
    if (typeof value !== 'string') {
      errors[key] = key + ' は文字列で渡してください';
    }
  });
  if (!options.partial && (fields.name === undefined || String(fields.name).trim() === '')) {
    errors.name = '名前は必須です';
  }
  if (fields.dueDate !== undefined && fields.dueDate !== '' && !/^\d{4}-\d{2}-\d{2}$/.test(String(fields.dueDate))) {
    errors.dueDate = '目安期限は YYYY-MM-DD 形式にしてください';
  }
  return errors;
}

function specWebOrderGroupItemsArray_() {
  var itemsMap = Storage.listItems(SPEC_WEB_ORDER_GROUPS_COLLECTION);
  return Object.keys(itemsMap).map(function (key) {
    return itemsMap[key];
  });
}

registerApi_('orderGroups.list', function () {
  return { items: specWebOrderGroupItemsArray_() };
});

registerApi_('orderGroups.get', function (ctx) {
  var id = ctx.params.id;
  if (!id) throw specWebOrderGroupsError_('id は必須です', 400);
  var item = Storage.getItem(SPEC_WEB_ORDER_GROUPS_COLLECTION, id);
  if (!item) throw specWebOrderGroupsError_('発注グループが見つかりません: ' + id, 404);
  return { item: item };
});

registerApi_('orderGroups.create', function (ctx) {
  specWebOrderGroupsRequireEditor_(ctx.auth);
  var fields = specWebParseOrderGroupPatch_(ctx.params);
  var errors = specWebValidateOrderGroupFields_(fields, { partial: false });
  if (Object.keys(errors).length > 0) {
    throw specWebOrderGroupsError_(Object.keys(errors).map(function (k) { return errors[k]; }).join(' / '), 400);
  }
  var id = specWebGenerateOrderGroupId_();
  var toSave = Object.assign({}, fields);
  toSave.comments = [];
  var saved = Storage.putItem(SPEC_WEB_ORDER_GROUPS_COLLECTION, id, toSave, { actor: specWebActor_(ctx.auth) });
  return { item: saved };
});

registerApi_('orderGroups.update', function (ctx) {
  specWebOrderGroupsRequireEditor_(ctx.auth);
  var params = ctx.params;
  var id = params.id;
  if (!id) throw specWebOrderGroupsError_('id は必須です', 400);
  var current = Storage.getItem(SPEC_WEB_ORDER_GROUPS_COLLECTION, id);
  if (!current) throw specWebOrderGroupsError_('発注グループが見つかりません: ' + id, 404);

  var fields = specWebParseOrderGroupPatch_(params);
  var errors = specWebValidateOrderGroupFields_(fields, { partial: true });
  if (Object.keys(errors).length > 0) {
    throw specWebOrderGroupsError_(Object.keys(errors).map(function (k) { return errors[k]; }).join(' / '), 400);
  }

  var expectedRevision = params.expectedRevision !== undefined && params.expectedRevision !== ''
    ? Number(params.expectedRevision)
    : current.revision;

  var saved = Storage.putItem(SPEC_WEB_ORDER_GROUPS_COLLECTION, id, fields, {
    actor: specWebActor_(ctx.auth),
    expectedRevision: expectedRevision
  });
  return { item: saved };
});

registerApi_('orderGroups.delete', function (ctx) {
  specWebOrderGroupsRequireEditor_(ctx.auth);
  var params = ctx.params;
  var id = params.id;
  if (!id) throw specWebOrderGroupsError_('id は必須です', 400);
  var current = Storage.getItem(SPEC_WEB_ORDER_GROUPS_COLLECTION, id);
  if (!current) throw specWebOrderGroupsError_('発注グループが見つかりません: ' + id, 404);

  var childrenMap = Storage.listItems(SPEC_WEB_ASSETS_COLLECTION);
  var hasChildren = Object.keys(childrenMap).some(function (key) {
    return childrenMap[key].parentId === id && !childrenMap[key].archived;
  });
  if (hasChildren) {
    throw specWebOrderGroupsError_('この発注グループには子の発注が残っています。先に付け替えてから削除してください。', 400);
  }

  var expectedRevision = params.expectedRevision !== undefined && params.expectedRevision !== ''
    ? Number(params.expectedRevision)
    : current.revision;

  Storage.deleteItem(SPEC_WEB_ORDER_GROUPS_COLLECTION, id, { expectedRevision: expectedRevision });
  return { deleted: true };
});

registerApi_('orderGroups.comments.add', function (ctx) {
  specWebOrderGroupsRequireEditor_(ctx.auth);
  var params = ctx.params;
  var id = params.id;
  var body = String(params.body || '').trim();
  if (!id) throw specWebOrderGroupsError_('id は必須です', 400);
  if (!body) throw specWebOrderGroupsError_('コメント本文は必須です', 400);

  var current = Storage.getItem(SPEC_WEB_ORDER_GROUPS_COLLECTION, id);
  if (!current) throw specWebOrderGroupsError_('発注グループが見つかりません: ' + id, 404);

  var comments = Array.isArray(current.comments) ? current.comments.slice() : [];
  comments.push({
    id: UtilitiesAdapter.newUuid(),
    author: specWebActor_(ctx.auth),
    body: body,
    createdAt: new Date().toISOString(),
    resolved: false
  });
  var saved = Storage.putItem(SPEC_WEB_ORDER_GROUPS_COLLECTION, id, { comments: comments }, {
    actor: specWebActor_(ctx.auth),
    expectedRevision: current.revision
  });
  return { item: saved };
});
