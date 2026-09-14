/**
 * アセット発注 CRUD API（W-4・W-5 を土台に O-1〜O-5・O-7 で「発注」へ再定義）。
 * docs/32_spec_web.md §10（アセット発注ツールへの再定義）・§10.2.1（データモデル）・
 * §10.3.2（一覧）・§10.3.4（詳細）の AC を実装したもの。
 *
 * コレクション名は既存どおり "assets"（旧「アセット仕様」から実体は変えていない。
 * O-1 はフィールドの追加・改称・status の3値化のみで、コレクション自体の置き換えはしない）。
 * id は「種別::識別子」（変更なし）。
 *
 * 登録している API（Api/Registry.js の拡張点）:
 *   - whoami              現在の認証情報を返す（他画面からも使う汎用 API）
 *   - assets.list         一覧（発注者/受注者/種別/状態/Presentation/キーワードで絞り込み。§10.3.2）
 *   - assets.get          1 件取得（コメント・ddriveState・params を含む全項目。§10.3.4）
 *   - assets.create       新規発注の作成
 *   - assets.update       更新（revision 楽観ロック。状態進行ボタンもこの API を使う）
 *   - assets.delete       論理削除（archived フラグ、W-4 からの既存方針を継続）
 *   - assets.restore      論理削除の取り消し
 *   - assets.comments.add コメント投稿
 *
 * O-1（データモデル移行）: `orderer`/`contractor`/`orderDate`/`deliveredDate`/`referenceMd`/
 * `parentId` を追加し、`status` を 発注済/納品済/インポート済 の3値に置き換えた
 * （旧 未着手/仮/本番/保留 は廃止）。旧フィールド（assignee/note/旧4値status）は
 * 物理削除しない（§10.7 要判断3 (a) を採用。実データがまだ無いため保守的に残置）。
 * 「旧フィールドは読み込み時に変換し、保存時には書かない」の原則（オーケストレーター決定）:
 *   - 読み込み（assets.list/assets.get）は specWebNormalizeLegacyOrderItem_（Migration.js）を
 *     必ず経由し、旧データでも新スキーマの形で返す（このファイルは変換の実装を持たず、
 *     Migration.js の関数を呼ぶだけ）
 *   - 書き込み（assets.create/assets.update）は新フィールドしか受け付けない
 *     （SPEC_WEB_ASSET_WRITABLE_FIELDS に旧フィールド名を含めない）
 *   - 実データの一括変換（既存の「保留」→コメント退避 等）は Migration.js の
 *     一時的な移行スクリプト（migrateLegacyOrdersToNewSchema）が別途行う
 *
 * 「インポート済」は API から直接設定できない（O-1 決定事項。D-Drive の assetState 同期
 * （DDriveSync.js、O-7）でのみ到達する）。発注済⇄納品済の手動進行では、状態が変わったとき
 * deliveredDate を自動的に記録/クリアする（specWebApplyOrderStatusSideEffects_）。
 *
 * D-Drive → Web で書き換える `ddriveState`（§10.2.1・§5.2）と `params`（O-6、AssetParams.js）は、
 * このファイルの API では一切受け付けない（SPEC_WEB_ASSET_WRITABLE_FIELDS に含めていない）。
 */

var SPEC_WEB_ASSETS_COLLECTION = 'assets';

// docs/32_spec_web.md §10 前提調査: 種別は AssetType の enum 名（Assets/DDrive/Foundation/Identity/AssetType.cs）。
// choices.json（D-Drive から同期される予定）が無い間の既定値としてここに列挙する。
// AssetType.cs を変更した場合はここも合わせて更新すること（None は選択肢に含めない）。
var SPEC_WEB_ASSET_TYPES = [
  'Se', 'Bgm', 'Vfx', 'Anim', 'Anim2D', 'Material', 'Texture', 'Canvas',
  'Prefab', 'Presentation', 'Shake', 'Haptics', 'UiTween', 'Model',
  'Anchor', 'AnchorGroup', 'ControlSkin'
];

// O-1（docs/32 §10.2.1）: 発注済 → 納品済 → インポート済 の3段階固定。
// 旧語彙（未着手/仮/本番/保留）は Migration.js が変換元として参照する（このファイルの
// 検証・既定値は新語彙のみを対象にする）。
var SPEC_WEB_ASSET_STATUSES = ['発注済', '納品済', 'インポート済'];
var SPEC_WEB_ASSET_STATUS_ORDERED = '発注済';
var SPEC_WEB_ASSET_STATUS_DELIVERED = '納品済';
var SPEC_WEB_ASSET_STATUS_IMPORTED = 'インポート済';

var SPEC_WEB_ASSET_PRIORITIES = ['高', '中', '低'];

// Assets/DDrive/Editor/AssetBrowser/AssetNamingService.cs の IdentifierPattern と同じ正規表現
// （先頭大文字・英数字のみの PascalCase）。
var SPEC_WEB_IDENTIFIER_PATTERN = /^[A-Z][A-Za-z0-9]*$/;

// O-12（docs/32_spec_web.md §10.2.1 追補、2026-09-14。ユーザー訂正: 「納品形式」ではなく
// 「ファイル形式」）: `fileFormat`（拡張子。先頭ドット付きで正規化）・`fileName`（納品ファイル名）
// を追加する。既存データは undefined のまま（Migration.js の specWebNormalizeLegacyOrderItem_ が
// 読み込み時に空文字として補う。非破壊）。
//
// 候補（datalist、種別ごと）は 1 か所（この定数）にまとめ、html/AssetsLogic.html 側は
// choices.json 経由の同期が無い間の既定値として同じ値を複製する（ASSET_TYPES 等と同じ既存の
// 複製方針、AssetsLogic.html 冒頭のコメント参照）。ここに無い種別は候補なし（自由入力のみ）。
var SPEC_WEB_FILE_FORMAT_CHOICES_BY_TYPE = {
  Se: ['.wav', '.ogg', '.mp3'],
  Bgm: ['.wav', '.ogg', '.mp3'],
  Texture: ['.png', '.psd', '.tga'],
  Anim2D: ['.png', '.psd', '.tga'],
  Model: ['.fbx'],
  Anim: ['.fbx'],
  Vfx: ['.prefab', '.unitypackage'],
  Prefab: ['.prefab', '.unitypackage'],
  Canvas: ['.prefab', '.unitypackage'],
  Material: ['.mat']
};

// 既存フィールドに長さ上限の先例は無い（2026-09-14 時点、grep で確認済み）ため、他の文字列
// フィールドと同じ検証の流儀（`errors` オブジェクトに追記してブロックする）だけを踏襲し、
// 妥当な上限をここで新設する。
var SPEC_WEB_FILE_FORMAT_MAX_LENGTH = 20;
var SPEC_WEB_FILE_NAME_MAX_LENGTH = 255;

// ファイル名に使えない文字（Windows のファイル名禁止文字と同じ集合）。CLAUDE.md §0-4「例外で
// 止めない」の考え方を Web 側の検証にも適用し、これらは `errors`（保存を止める）ではなく
// 呼び出し側が別途 specWebComputeFileWarnings_ で「警告」として扱う。
var SPEC_WEB_FILE_NAME_ILLEGAL_CHARS_PATTERN = /[\\/:*?"<>|]/g;

// このファイルの書き込み系 API が受け付けるフィールドのみを patch から抜き出す。
// ddriveState・params・comments・archived・revision 等の管理用フィールドは意図的に含めない
// （ddriveState は D-Drive → Web 専用(DDriveSync.js)、params は O-6(AssetParams.js) 専用、
//   comments は assets.comments.add 専用、archived は assets.delete/assets.restore 専用、
//   revision 系は Storage.js が管理する）。旧フィールド（assignee/note）も意図的に含めない
//   （O-1: 旧フィールドは読み込み時の変換専用で、書き込みには使わない）。
var SPEC_WEB_ASSET_WRITABLE_FIELDS = [
  'assetType', 'category', 'identifier', 'displayName', 'status',
  'orderer', 'contractor', 'orderDate', 'dueDate', 'deliveredDate',
  'priority', 'referenceMd', 'referenceImages', 'relatedFeaturePages', 'parentId',
  'fileFormat', 'fileName'
];

/**
 * `fileFormat` を先頭ドット付きに正規化する（O-12。`png` と入力されても `.png` に揃える）。
 * 空欄はそのまま空文字を返す（未設定を表す。必須にはしない）。
 */
function specWebNormalizeFileFormat_(raw) {
  var trimmed = String(raw === undefined || raw === null ? '' : raw).trim();
  if (trimmed === '') return '';
  return trimmed.charAt(0) === '.' ? trimmed : '.' + trimmed;
}

/** ファイル名に含まれる禁止文字（重複を除いた配列）を返す。無ければ空配列。 */
function specWebFileNameIllegalChars_(fileName) {
  if (!fileName) return [];
  var matches = String(fileName).match(SPEC_WEB_FILE_NAME_ILLEGAL_CHARS_PATTERN) || [];
  var unique = [];
  matches.forEach(function (c) {
    if (unique.indexOf(c) === -1) unique.push(c);
  });
  return unique;
}

/** fileName の拡張子が fileFormat と食い違う場合の警告文（一致 or 判定不能なら null）。 */
function specWebFileNameExtensionMismatchWarning_(fileName, fileFormat) {
  if (!fileName || !fileFormat) return null;
  var match = /\.[A-Za-z0-9]+$/.exec(String(fileName).trim());
  if (!match) return null;
  if (match[0].toLowerCase() === String(fileFormat).toLowerCase()) return null;
  return 'ファイル名の拡張子（' + match[0] + '）がファイル形式（' + fileFormat + '）と一致していません';
}

/**
 * O-12「ファイル名に使えない文字・拡張子の食い違いは警告のみ（例外で止めない）」。
 * 保存をブロックしない non-blocking な警告文の配列を返す（呼び出し側が API 応答に含める）。
 */
function specWebComputeFileWarnings_(fields) {
  var warnings = [];
  var illegal = specWebFileNameIllegalChars_(fields && fields.fileName);
  if (illegal.length > 0) {
    warnings.push('ファイル名に使えない文字が含まれています: ' + illegal.join(' '));
  }
  var mismatch = specWebFileNameExtensionMismatchWarning_(fields && fields.fileName, fields && fields.fileFormat);
  if (mismatch) warnings.push(mismatch);
  return warnings;
}

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
  // (docs/32_spec_web.md §9 の要判断参照)。isPlaceholder は O-7(DDriveSync.js)が実値化する。
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

/** サーバー時計での今日の日付（YYYY-MM-DD）。発注日の既定値・納品日の自動記録に使う。 */
function specWebTodayDateString_() {
  return new Date().toISOString().slice(0, 10);
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

/**
 * 書き込み可能フィールドだけを取り出す（ddriveState・params 等の混入を防ぐ）。
 * O-12: `fileFormat` はここで先頭ドット付きに正規化する（`png` → `.png`。保存前に必ず1回だけ
 * 通る場所のため、create/update 両方の入口をここに集約する）。
 */
function specWebSanitizeAssetPatch_(rawPatch) {
  var sanitized = {};
  SPEC_WEB_ASSET_WRITABLE_FIELDS.forEach(function (key) {
    if (Object.prototype.hasOwnProperty.call(rawPatch, key)) {
      sanitized[key] = rawPatch[key];
    }
  });
  if (Object.prototype.hasOwnProperty.call(sanitized, 'fileFormat')) {
    sanitized.fileFormat = specWebNormalizeFileFormat_(sanitized.fileFormat);
  }
  return sanitized;
}

/**
 * 入力検証（docs/32 §10.2.1「種別は選択肢内、識別子 PascalCase、重複禁止、必須項目、
 * 状態は3値でインポート済は直接設定不可」）。
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

  function isDateOrBlank(v) {
    return v === undefined || v === null || v === '' || /^\d{4}-\d{2}-\d{2}$/.test(String(v));
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

  if (fields.status !== undefined && fields.status !== '') {
    if (SPEC_WEB_ASSET_STATUSES.indexOf(fields.status) === -1) {
      errors.status = '状態は次のいずれかにしてください: ' + SPEC_WEB_ASSET_STATUSES.join('/');
    } else if (fields.status === SPEC_WEB_ASSET_STATUS_IMPORTED && !options.allowImported) {
      errors.status = '「インポート済」は手動で設定できません（D-Drive の同期でのみ切り替わります）';
    }
  }

  if (fields.priority !== undefined && fields.priority !== '' && SPEC_WEB_ASSET_PRIORITIES.indexOf(fields.priority) === -1) {
    errors.priority = '優先度は次のいずれかにしてください: ' + SPEC_WEB_ASSET_PRIORITIES.join('/');
  }

  if (!isDateOrBlank(fields.orderDate)) {
    errors.orderDate = '発注日は YYYY-MM-DD 形式にしてください';
  }
  if (!isDateOrBlank(fields.dueDate)) {
    errors.dueDate = '納品期限は YYYY-MM-DD 形式にしてください';
  }
  if (!isDateOrBlank(fields.deliveredDate)) {
    errors.deliveredDate = '納品日は YYYY-MM-DD 形式にしてください';
  }

  if (fields.referenceImages !== undefined && !Array.isArray(fields.referenceImages)) {
    errors.referenceImages = 'referenceImages は配列で渡してください';
  }

  if (fields.relatedFeaturePages !== undefined && !Array.isArray(fields.relatedFeaturePages)) {
    errors.relatedFeaturePages = 'relatedFeaturePages は配列で渡してください';
  }

  if (fields.parentId !== undefined && fields.parentId !== null && fields.parentId !== '') {
    // Presentation 発注グループ（O-2、OrderGroups.js）への参照。実在チェックはハンドラ側で行う
    // （このファイルは OrderGroups.js に依存しない形で「文字列であること」だけを見る）。
    if (typeof fields.parentId !== 'string') {
      errors.parentId = 'parentId は文字列（発注グループの id）である必要があります';
    }
  }

  // O-12: 長さ上限（既存フィールドに先例が無いため新設。他の文字列フィールドと同じ「errors に
  // 追記してブロックする」流儀のみ踏襲する）。ファイル名に使えない文字・拡張子の食い違いは
  // ここでは検証しない（警告のみ・例外で止めない方針のため specWebComputeFileWarnings_ に分離）。
  if (fields.fileFormat !== undefined && String(fields.fileFormat).length > SPEC_WEB_FILE_FORMAT_MAX_LENGTH) {
    errors.fileFormat = 'ファイル形式は' + SPEC_WEB_FILE_FORMAT_MAX_LENGTH + '文字以内にしてください';
  }
  if (fields.fileName !== undefined && String(fields.fileName).length > SPEC_WEB_FILE_NAME_MAX_LENGTH) {
    errors.fileName = 'ファイル名は' + SPEC_WEB_FILE_NAME_MAX_LENGTH + '文字以内にしてください';
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

/**
 * 状態が変わったときの副作用（docs/32 §10.3.4「納品済への進行は受注者が手動でボタンを押す
 * （納品日が自動記録される）」）。fields を直接書き換える（呼び出し側で patch として使う想定）。
 * 明示的に deliveredDate が渡されていればそれを優先する（自動記録を上書きできる）。
 */
function specWebApplyOrderStatusSideEffects_(currentStatus, fields) {
  if (fields.status === undefined || fields.status === currentStatus) return;
  if (fields.status === SPEC_WEB_ASSET_STATUS_DELIVERED && fields.deliveredDate === undefined) {
    fields.deliveredDate = specWebTodayDateString_();
  } else if (fields.status === SPEC_WEB_ASSET_STATUS_ORDERED && fields.deliveredDate === undefined) {
    fields.deliveredDate = null;
  }
}

/** parentId が指定されていれば、対応する発注グループ（OrderGroups.js、collection="orderGroups"）が実在するか確認する。 */
function specWebValidateParentIdExists_(parentId) {
  if (!parentId) return;
  var group = Storage.getItem('orderGroups', parentId);
  if (!group) {
    throw specWebAssetsError_('存在しない発注グループです（parentId）: ' + parentId, 400);
  }
}

/** 一覧の絞り込み（サーバー側は簡易フィルタのみ。並べ替え・グルーピングはクライアント側）。 */
function specWebFilterAssetItems_(items, filters) {
  filters = filters || {};
  return items.filter(function (item) {
    if (!filters.includeArchived && item.archived) return false;
    if (filters.assetType && item.assetType !== filters.assetType) return false;
    if (filters.status && item.status !== filters.status) return false;
    if (filters.orderer && item.orderer !== filters.orderer) return false;
    if (filters.contractor && item.contractor !== filters.contractor) return false;
    if (filters.category && item.category !== filters.category) return false;
    if (filters.fileFormat && item.fileFormat !== filters.fileFormat) return false;
    if (filters.parentId !== undefined && filters.parentId !== '') {
      var wantUnassigned = filters.parentId === '__none__';
      if (wantUnassigned && item.parentId) return false;
      if (!wantUnassigned && item.parentId !== filters.parentId) return false;
    }
    if (filters.query) {
      var q = String(filters.query).toLowerCase();
      var hay = (String(item.identifier || '') + ' ' + String(item.displayName || '')).toLowerCase();
      if (hay.indexOf(q) === -1) return false;
    }
    return true;
  });
}

/** 全件を配列で返す（読み込み時の O-1 正規化を必ず経由する。Migration.js 参照）。 */
function specWebAssetItemsArray_() {
  var itemsMap = Storage.listItems(SPEC_WEB_ASSETS_COLLECTION);
  return Object.keys(itemsMap).map(function (key) {
    return specWebNormalizeLegacyOrderItem_(itemsMap[key]);
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
    orderer: params.orderer,
    contractor: params.contractor,
    category: params.category,
    fileFormat: params.fileFormat,
    parentId: params.parentId,
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
  var normalized = specWebNormalizeLegacyOrderItem_(item);
  // O-12: 保存はしない non-blocking な警告（不正文字・拡張子の食い違い）を都度計算して返す。
  return { item: normalized, warnings: specWebComputeFileWarnings_(normalized) };
});

registerApi('assets.create', function (ctx) {
  var auth = ctx.auth;
  specWebRequireEditor_(auth);

  var fields = specWebSanitizeAssetPatch_(specWebParseAssetPatch_(ctx.params));
  var errors = specWebValidateAssetFields_(fields, { partial: false });
  if (Object.keys(errors).length > 0) {
    throw specWebAssetsError_(specWebJoinErrors_(errors), 400);
  }
  specWebValidateParentIdExists_(fields.parentId);

  var id = specWebBuildAssetId_(fields.assetType, fields.identifier);
  if (Storage.getItem(SPEC_WEB_ASSETS_COLLECTION, id)) {
    throw specWebAssetsError_('種別+識別子が既に存在します: ' + id, 409);
  }

  var toSave = Object.assign({}, fields);
  if (!toSave.status) toSave.status = SPEC_WEB_ASSET_STATUS_ORDERED;
  if (!toSave.orderDate) toSave.orderDate = specWebTodayDateString_();
  specWebApplyOrderStatusSideEffects_(SPEC_WEB_ASSET_STATUS_ORDERED, toSave);
  toSave.comments = [];
  toSave.archived = false;
  toSave.ddriveState = specWebDefaultDdriveState_();
  toSave.params = null; // O-6: D-Drive からの同期でのみ入る（AssetParams.js）
  if (toSave.fileFormat === undefined) toSave.fileFormat = '';
  if (toSave.fileName === undefined) toSave.fileName = '';

  var saved = Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, id, toSave, { actor: specWebActor_(auth) });
  return { item: saved, warnings: specWebComputeFileWarnings_(saved) };
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
  // 変えたい場合は削除して作り直す運用にする。
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
  if (fields.parentId !== undefined) specWebValidateParentIdExists_(fields.parentId);

  specWebApplyOrderStatusSideEffects_(current.status, fields);

  var expectedRevision = params.expectedRevision !== undefined && params.expectedRevision !== ''
    ? Number(params.expectedRevision)
    : current.revision;

  var saved = Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, id, fields, {
    actor: specWebActor_(auth),
    expectedRevision: expectedRevision
  });
  return { item: saved, warnings: specWebComputeFileWarnings_(saved) };
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
