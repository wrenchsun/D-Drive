/**
 * アセット仕様 CRUD API（W-4・W-5）。docs/32_spec_web.md §3.1（データモデル）・§4.1（一覧）・
 * §4.5（詳細）・§8 W-4/W-5 の AC を実装したもの。
 *
 * コレクション名は既存テスト（storage.test.js）が使っている "assets" をそのまま使う。
 * id は「種別::識別子」（docs/32 §3 共通メタ情報）。
 *
 * 登録している API（Api/Registry.js の拡張点、Code.js/Registry.js は編集していない）:
 *   - whoami              現在の認証情報（role 等）を返す。編集系ボタンの表示切り替え等、
 *                         このファイル以外の後続画面からも使える汎用 API として登録する
 *   - assets.list         一覧（既定でアーカイブ済みは除外。§4.1）
 *   - assets.get          1 件取得（コメント・ddriveState を含む全項目。§4.5）
 *   - assets.create       新規作成
 *   - assets.update       更新（revision 楽観ロック）
 *   - assets.delete       論理削除（§9-要判断: archived フラグを立てる方式で実装。下記コメント参照）
 *   - assets.restore      論理削除の取り消し
 *   - assets.comments.add コメント投稿（§3.6 Comment 構造。アセットのコメント欄）
 *
 * 要判断だった「削除（論理削除）」の実装方針（このチケットで決定）:
 *   docs/32 のチケット表 W-4 は「削除（論理削除 = 状態を保留 / アーカイブにするか、要判断）」としていた。
 *   ユーザーに確認できないため、**「アーカイブ用の独立したフラグ（archived: boolean）を立てる」方式**
 *   で決定する。理由: `status`（未着手/仮/本番/保留）は企画側が進行状況として自由に使う値であり、
 *   「削除（一覧から隠す）」という別の意味を `保留` に混ぜると、"保留にしたいだけ" のケースと
 *   "削除したい" のケースが区別できなくなる。`archived` を別フィールドにすることで、
 *   一覧 API は既定で `archived: true` を除外し（`includeArchived=1` で表示可能）、
 *   `status` は編集で自由に使える値として残す。詳細は docs/32_spec_web.md「実装メモ（W-4〜W-5）」参照。
 *
 * D-Drive → Web で書き換える `ddriveState`（§3.1・§5.2）は、このファイルの API では
 * 一切受け付けない（SPEC_WEB_ASSET_WRITABLE_FIELDS に含めていない。W-12 が専用 API で書く）。
 */

var SPEC_WEB_ASSETS_COLLECTION = 'assets';

// docs/32_spec_web.md §3.1: 種別は AssetType の enum 名（Assets/DDrive/Foundation/Identity/AssetType.cs）。
// choices.json（W-12 で D-Drive から同期される予定）が無い間の既定値としてここに列挙する。
// AssetType.cs を変更した場合はここも合わせて更新すること（None は選択肢に含めない）。
var SPEC_WEB_ASSET_TYPES = [
  'Se', 'Bgm', 'Vfx', 'Anim', 'Anim2D', 'Material', 'Texture', 'Canvas',
  'Prefab', 'Presentation', 'Shake', 'Haptics', 'UiTween', 'Model',
  'Anchor', 'AnchorGroup', 'ControlSkin'
];

// docs/27_spec_sheet.md §7.2 を継承した語彙（docs/32 §3.1）。
var SPEC_WEB_ASSET_STATUSES = ['未着手', '仮', '本番', '保留'];
var SPEC_WEB_ASSET_PRIORITIES = ['高', '中', '低'];

// Assets/DDrive/Editor/AssetBrowser/AssetNamingService.cs の IdentifierPattern と同じ正規表現
// （先頭大文字・英数字のみの PascalCase）。
var SPEC_WEB_IDENTIFIER_PATTERN = /^[A-Z][A-Za-z0-9]*$/;

// このファイルの書き込み系 API が受け付けるフィールドのみを patch から抜き出す。
// ddriveState・comments・archived・revision 等の管理用フィールドは意図的に含めない
// （ddriveState は W-12 専用、comments は assets.comments.add 専用、
//   archived は assets.delete/assets.restore 専用、revision 系は Storage.js が管理する）。
var SPEC_WEB_ASSET_WRITABLE_FIELDS = [
  'assetType', 'category', 'identifier', 'displayName', 'status',
  'assignee', 'dueDate', 'priority', 'note', 'referenceImages', 'relatedFeaturePages'
];

/** RevisionConflictError と同じ「name/status を持つ Error」規約に沿った汎用エラー。 */
function specWebAssetsError_(message, status) {
  var err = new Error(message);
  err.status = status || 400;
  return err;
}

function specWebActor_(auth) {
  return (auth && (auth.email || auth.principal)) || 'unknown';
}

function specWebDefaultDdriveState_() {
  // hasIcon(2026-09-14 追補): D-Drive 側にアイコン(AssetDataBase.Icon)が割り当て済みかの bool。
  // iconAssetId(Drive へのアップロード)は本チケットの範囲外のため常に null のまま
  // (docs/32_spec_web.md §9 の要判断参照)。
  return { created: false, isPlaceholder: false, iconAssetId: null, hasIcon: false, usageCount: 0, lastSyncedAt: null };
}

function specWebBuildAssetId_(assetType, identifier) {
  return assetType + '::' + identifier;
}

function specWebRequireEditor_(auth) {
  if (!hasRole(auth, SPEC_WEB_ROLES.EDITOR)) {
    throw specWebAssetsError_('この操作には編集権限が必要です（viewer は読み取りのみ）', 403);
  }
}

/**
 * e.parameter は文字列しか運べないため、複数フィールドは
 * `patch` パラメータに JSON 文字列で詰めて送る規約にする（Assets.html 側もこれに合わせる）。
 */
function specWebParseAssetPatch_(params) {
  var raw = params.patch;
  if (raw === undefined || raw === null || raw === '') return {};
  var parsed;
  try {
    parsed = JSON.parse(raw);
  } catch (err) {
    throw specWebAssetsError_('patch は JSON 文字列で渡してください', 400);
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw specWebAssetsError_('patch はオブジェクトである必要があります', 400);
  }
  return parsed;
}

/** 書き込み可能フィールドだけを取り出す（ddriveState 等の混入を防ぐ）。 */
function specWebSanitizeAssetPatch_(rawPatch) {
  var sanitized = {};
  SPEC_WEB_ASSET_WRITABLE_FIELDS.forEach(function (key) {
    if (Object.prototype.hasOwnProperty.call(rawPatch, key)) {
      sanitized[key] = rawPatch[key];
    }
  });
  return sanitized;
}

/**
 * 入力検証（docs/32 §8 W-4 AC「種別は選択肢内、識別子 PascalCase、重複禁止、必須項目」）。
 * options.partial=true のときは「フィールドが存在する場合だけ検証する」（更新の部分パッチ用）。
 * @return {Object<string,string>} フィールド名 → エラーメッセージ。空オブジェクトなら検証 OK。
 */
function specWebValidateAssetFields_(fields, options) {
  options = options || {};
  var partial = !!options.partial;
  var errors = {};

  function isBlank(v) {
    return v === undefined || v === null || String(v).trim() === '';
  }

  if (fields.assetType !== undefined) {
    if (SPEC_WEB_ASSET_TYPES.indexOf(fields.assetType) === -1) {
      errors.assetType = '種別は選択肢から選んでください: ' + SPEC_WEB_ASSET_TYPES.join('/');
    }
  } else if (!partial) {
    errors.assetType = '種別は必須です';
  }

  if (fields.identifier !== undefined) {
    if (!SPEC_WEB_IDENTIFIER_PATTERN.test(String(fields.identifier))) {
      errors.identifier = '識別子は英語 PascalCase（先頭大文字・英数字のみ）にしてください';
    }
  } else if (!partial) {
    errors.identifier = '識別子は必須です';
  }

  if (!partial && isBlank(fields.displayName)) {
    errors.displayName = '表示名は必須です';
  }

  if (fields.status !== undefined && fields.status !== '' && SPEC_WEB_ASSET_STATUSES.indexOf(fields.status) === -1) {
    errors.status = '状態は次のいずれかにしてください: ' + SPEC_WEB_ASSET_STATUSES.join('/');
  }

  if (fields.priority !== undefined && fields.priority !== '' && SPEC_WEB_ASSET_PRIORITIES.indexOf(fields.priority) === -1) {
    errors.priority = '優先度は次のいずれかにしてください: ' + SPEC_WEB_ASSET_PRIORITIES.join('/');
  }

  if (fields.dueDate !== undefined && fields.dueDate !== '' && !/^\d{4}-\d{2}-\d{2}$/.test(String(fields.dueDate))) {
    errors.dueDate = '期限は YYYY-MM-DD 形式にしてください';
  }

  if (fields.referenceImages !== undefined && !Array.isArray(fields.referenceImages)) {
    errors.referenceImages = 'referenceImages は配列で渡してください';
  }

  if (fields.relatedFeaturePages !== undefined && !Array.isArray(fields.relatedFeaturePages)) {
    errors.relatedFeaturePages = 'relatedFeaturePages は配列で渡してください';
  }

  return errors;
}

function specWebJoinErrors_(errors) {
  return Object.keys(errors)
    .map(function (key) {
      return errors[key];
    })
    .join(' / ');
}

/** 一覧の絞り込み（サーバー側は簡易フィルタのみ。並べ替え・グルーピングはクライアント側、下記「実装メモ」参照）。 */
function specWebFilterAssetItems_(items, filters) {
  filters = filters || {};
  return items.filter(function (item) {
    if (!filters.includeArchived && item.archived) return false;
    if (filters.assetType && item.assetType !== filters.assetType) return false;
    if (filters.status && item.status !== filters.status) return false;
    if (filters.assignee && item.assignee !== filters.assignee) return false;
    if (filters.category && item.category !== filters.category) return false;
    if (filters.query) {
      var q = String(filters.query).toLowerCase();
      var hay = (String(item.identifier || '') + ' ' + String(item.displayName || '')).toLowerCase();
      if (hay.indexOf(q) === -1) return false;
    }
    return true;
  });
}

function specWebAssetItemsArray_() {
  var itemsMap = Storage.listItems(SPEC_WEB_ASSETS_COLLECTION);
  return Object.keys(itemsMap).map(function (key) {
    return itemsMap[key];
  });
}

registerApi('whoami', function (ctx) {
  var auth = ctx.auth;
  return {
    email: auth.email || null,
    displayName: auth.displayName || null,
    role: auth.role || null,
    principal: auth.principal || null
  };
});

registerApi('assets.list', function (ctx) {
  var params = ctx.params;
  var includeArchived = params.includeArchived === '1' || params.includeArchived === 'true';
  var items = specWebFilterAssetItems_(specWebAssetItemsArray_(), {
    assetType: params.assetType,
    status: params.status,
    assignee: params.assignee,
    category: params.category,
    query: params.query,
    includeArchived: includeArchived
  });
  return {
    items: items,
    total: items.length,
    assetTypes: SPEC_WEB_ASSET_TYPES,
    statuses: SPEC_WEB_ASSET_STATUSES,
    priorities: SPEC_WEB_ASSET_PRIORITIES
  };
});

registerApi('assets.get', function (ctx) {
  var id = ctx.params.id;
  if (!id) throw specWebAssetsError_('id は必須です', 400);
  var item = Storage.getItem(SPEC_WEB_ASSETS_COLLECTION, id);
  if (!item) throw specWebAssetsError_('アセットが見つかりません: ' + id, 404);
  return { item: item };
});

registerApi('assets.create', function (ctx) {
  var auth = ctx.auth;
  specWebRequireEditor_(auth);

  var fields = specWebSanitizeAssetPatch_(specWebParseAssetPatch_(ctx.params));
  var errors = specWebValidateAssetFields_(fields, { partial: false });
  if (Object.keys(errors).length > 0) {
    throw specWebAssetsError_(specWebJoinErrors_(errors), 400);
  }

  var id = specWebBuildAssetId_(fields.assetType, fields.identifier);
  if (Storage.getItem(SPEC_WEB_ASSETS_COLLECTION, id)) {
    throw specWebAssetsError_('種別+識別子が既に存在します: ' + id, 409);
  }

  var toSave = Object.assign({}, fields);
  if (!toSave.status) toSave.status = SPEC_WEB_ASSET_STATUSES[0];
  toSave.comments = [];
  toSave.archived = false;
  toSave.ddriveState = specWebDefaultDdriveState_();

  var saved = Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, id, toSave, { actor: specWebActor_(auth) });
  return { item: saved };
});

registerApi('assets.update', function (ctx) {
  var auth = ctx.auth;
  specWebRequireEditor_(auth);

  var params = ctx.params;
  var id = params.id;
  if (!id) throw specWebAssetsError_('id は必須です', 400);
  var current = Storage.getItem(SPEC_WEB_ASSETS_COLLECTION, id);
  if (!current) throw specWebAssetsError_('アセットが見つかりません: ' + id, 404);

  var fields = specWebSanitizeAssetPatch_(specWebParseAssetPatch_(params));

  // 種別・識別子は id そのもの（同期のキー）なので、この API では変更を許可しない。
  // 変えたい場合は削除して作り直す運用にする（docs/32 §3.1「同期のキー」）。
  if (fields.assetType !== undefined && fields.assetType !== current.assetType) {
    throw specWebAssetsError_('種別は更新できません（削除して作り直してください）', 400);
  }
  if (fields.identifier !== undefined && fields.identifier !== current.identifier) {
    throw specWebAssetsError_('識別子は更新できません（削除して作り直してください）', 400);
  }

  var errors = specWebValidateAssetFields_(fields, { partial: true });
  if (Object.keys(errors).length > 0) {
    throw specWebAssetsError_(specWebJoinErrors_(errors), 400);
  }

  var expectedRevision = params.expectedRevision !== undefined && params.expectedRevision !== ''
    ? Number(params.expectedRevision)
    : current.revision;

  var saved = Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, id, fields, {
    actor: specWebActor_(auth),
    expectedRevision: expectedRevision
  });
  return { item: saved };
});

registerApi('assets.delete', function (ctx) {
  var auth = ctx.auth;
  specWebRequireEditor_(auth);

  var params = ctx.params;
  var id = params.id;
  if (!id) throw specWebAssetsError_('id は必須です', 400);
  var current = Storage.getItem(SPEC_WEB_ASSETS_COLLECTION, id);
  if (!current) throw specWebAssetsError_('アセットが見つかりません: ' + id, 404);

  var expectedRevision = params.expectedRevision !== undefined && params.expectedRevision !== ''
    ? Number(params.expectedRevision)
    : current.revision;

  var saved = Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, id, { archived: true }, {
    actor: specWebActor_(auth),
    expectedRevision: expectedRevision
  });
  return { item: saved };
});

registerApi('assets.restore', function (ctx) {
  var auth = ctx.auth;
  specWebRequireEditor_(auth);

  var params = ctx.params;
  var id = params.id;
  if (!id) throw specWebAssetsError_('id は必須です', 400);
  var current = Storage.getItem(SPEC_WEB_ASSETS_COLLECTION, id);
  if (!current) throw specWebAssetsError_('アセットが見つかりません: ' + id, 404);

  var expectedRevision = params.expectedRevision !== undefined && params.expectedRevision !== ''
    ? Number(params.expectedRevision)
    : current.revision;

  var saved = Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, id, { archived: false }, {
    actor: specWebActor_(auth),
    expectedRevision: expectedRevision
  });
  return { item: saved };
});

registerApi('assets.comments.add', function (ctx) {
  var auth = ctx.auth;
  specWebRequireEditor_(auth);

  var params = ctx.params;
  var id = params.id;
  var body = String(params.body || '').trim();
  if (!id) throw specWebAssetsError_('id は必須です', 400);
  if (!body) throw specWebAssetsError_('コメント本文は必須です', 400);

  var current = Storage.getItem(SPEC_WEB_ASSETS_COLLECTION, id);
  if (!current) throw specWebAssetsError_('アセットが見つかりません: ' + id, 404);

  var comments = Array.isArray(current.comments) ? current.comments.slice() : [];
  comments.push({
    id: UtilitiesAdapter.newUuid(),
    author: specWebActor_(auth),
    body: body,
    createdAt: new Date().toISOString(),
    resolved: false
  });

  // expectedRevision は「今読んだ current の revision」を使う。書き込み時点で他の変更が
  // 入っていれば putItem 内の楽観ロックが RevisionConflictError を投げる（コメントの
  // 追記が失われることはない。呼び出し側は再取得してリトライすればよい）。
  var saved = Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, id, { comments: comments }, {
    actor: specWebActor_(auth),
    expectedRevision: current.revision
  });
  return { item: saved };
});
