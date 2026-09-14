/**
 * D-Drive → Web 送信 API（W-12）。docs/32_spec_web.md §5.2 の 3 kind
 * （choices / assetState / tuningUsage）を実装する。
 *
 * 書き込みトークンで呼べる API をこの 3 つ（+ping/whoami）に限定するゲートは
 * Code.js（handleApiRequest_ の DDRIVE_WRITE_TOKEN_ALLOWED_APIS）にある。
 * このファイルの API 自身も、企画が入力した本文・調整値の値・機能仕様ページ・コメントには
 * 一切書き込まない（choices/tuningUsage は専用の独立コレクション、assetState は
 * assets コレクションの ddriveState フィールドだけを patch する）。
 *
 * revision 楽観ロックは使わない: これらは「D-Drive 側が把握している補助情報」の
 * 最新値を都度上書きするだけの用途で、他の人の編集と衝突しても「上書きされて困る」
 * 種類のデータではない（Storage.putItem は呼び出し時点の最新状態から revision を
 * 進めるため、lost update にはならない）。
 *
 * レート制限（§5.2「送信 API にはレート制限を設ける」）: 同じ principal + API 名の組で
 * 直近 60 秒間の呼び出し回数を PropertiesService に記録し、上限を超えたら 429 相当で拒否する
 * （例外で止めない。D-Drive 側は次回同期まで待てばよい。CLAUDE.md §0-4 と同じ考え方）。
 */

var DDRIVE_SYNC_CHOICES_COLLECTION = 'choices';
var DDRIVE_SYNC_CHOICES_DOC_ID = 'current';
var DDRIVE_SYNC_TUNING_USAGE_COLLECTION = 'tuningUsage';
var DDRIVE_SYNC_TUNING_USAGE_DOC_ID = 'current';
var DDRIVE_SYNC_ASSETS_COLLECTION = 'assets'; // Assets.js と同じコレクション名

var DDRIVE_SYNC_RATE_LIMIT_WINDOW_MS = 60 * 1000;
var DDRIVE_SYNC_RATE_LIMIT_MAX_CALLS = 10;
var DDRIVE_SYNC_RATE_LIMIT_PROPERTY_PREFIX = 'DDRIVE_SYNC_RATE_';

/** 429 相当(レート制限超過)。ContentAdapter が本文の status フィールドに変換する。 */
function SpecWebRateLimitError(message) {
  this.name = 'SpecWebRateLimitError';
  this.message = message;
  this.status = 429;
  if (typeof Error.captureStackTrace === 'function') {
    Error.captureStackTrace(this, SpecWebRateLimitError);
  }
}
SpecWebRateLimitError.prototype = Object.create(Error.prototype);
SpecWebRateLimitError.prototype.constructor = SpecWebRateLimitError;

/**
 * principal + apiName ごとに直近 DDRIVE_SYNC_RATE_LIMIT_WINDOW_MS 内の呼び出し回数を数え、
 * 上限を超えていれば SpecWebRateLimitError を投げる。超えていなければ今回の呼び出しを記録する。
 */
function specWebCheckRateLimit_(principal, apiName) {
  var key = DDRIVE_SYNC_RATE_LIMIT_PROPERTY_PREFIX + String(principal) + '_' + apiName;
  var now = Date.now();
  var raw = PropertiesAdapter.getScriptProperty(key);
  var timestamps = [];
  if (raw) {
    try {
      var parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) timestamps = parsed;
    } catch (err) {
      timestamps = [];
    }
  }
  timestamps = timestamps.filter(function (t) {
    return now - t < DDRIVE_SYNC_RATE_LIMIT_WINDOW_MS;
  });
  if (timestamps.length >= DDRIVE_SYNC_RATE_LIMIT_MAX_CALLS) {
    throw new SpecWebRateLimitError(
      apiName + ' の呼び出しが多すぎます（' + DDRIVE_SYNC_RATE_LIMIT_MAX_CALLS + ' 回/分 まで）。しばらく待って再度お試しください。'
    );
  }
  timestamps.push(now);
  PropertiesAdapter.setScriptProperty(key, JSON.stringify(timestamps));
}

/** choices: AssetType 一覧・カテゴリ一覧・タグ候補([32] §3.3・§5.2)。 */
registerApi('choices', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.EDITOR, '選択肢の送信には editor 以上の権限が必要です');
  specWebCheckRateLimit_(ctx.auth.principal, 'choices');
  var payload = specWebParsePayload_(ctx.params);
  var doc = {
    assetTypes: Array.isArray(payload.assetTypes) ? payload.assetTypes : [],
    categories: Array.isArray(payload.categories) ? payload.categories : [],
    tags: Array.isArray(payload.tags) ? payload.tags : []
  };
  var saved = Storage.putItem(DDRIVE_SYNC_CHOICES_COLLECTION, DDRIVE_SYNC_CHOICES_DOC_ID, doc, { actor: ctx.auth.principal });
  return { item: saved };
});

/**
 * assetState: 各アセットの ddriveState だけを patch する(§3.1「D-Drive → Web」)。
 * assets コレクションに存在しない id は静かにスキップする(D-Drive 側が把握している id が
 * Web 側にまだ存在しない状況は通常起きないはずだが、例外にはしない。CLAUDE.md §0-4)。
 */
registerApi('assetState', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.EDITOR, 'アセット実状態の送信には editor 以上の権限が必要です');
  specWebCheckRateLimit_(ctx.auth.principal, 'assetState');
  var payload = specWebParsePayload_(ctx.params);
  var items = Array.isArray(payload.items) ? payload.items : [];
  var updatedIds = [];
  var skippedIds = [];
  items.forEach(function (entry) {
    if (!entry || !entry.id) return;
    var existing = Storage.getItem(DDRIVE_SYNC_ASSETS_COLLECTION, entry.id);
    if (!existing) {
      skippedIds.push(entry.id);
      return;
    }
    var ddriveState = {
      created: !!entry.created,
      isPlaceholder: !!entry.isPlaceholder,
      iconAssetId: entry.iconAssetId || null,
      // hasIcon(2026-09-14 追補): [32] §9 の要判断「isPlaceholder/iconAssetId」対応の一部。
      // アイコン画像そのもの(base64)は Drive アップロード実装が無いため送らず、
      // 「割り当て済みかどうか」の bool だけを受け取る(下記 Assets.js の既定値・
      // html/AssetsLogic.html の表示側と対にする)。
      hasIcon: !!entry.hasIcon,
      usageCount: typeof entry.usageCount === 'number' ? entry.usageCount : 0,
      lastSyncedAt: entry.lastSyncedAt || new Date().toISOString()
    };
    Storage.putItem(DDRIVE_SYNC_ASSETS_COLLECTION, entry.id, { ddriveState: ddriveState }, { actor: ctx.auth.principal });
    updatedIds.push(entry.id);
  });
  return { updatedIds: updatedIds, skippedIds: skippedIds };
});

/** tuningUsage: TUNING 定数へのコード参照が無いキーの一覧([32] §3.2.4「コード未使用の検出」)。 */
registerApi('tuningUsage', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.EDITOR, '調整値使用状況の送信には editor 以上の権限が必要です');
  specWebCheckRateLimit_(ctx.auth.principal, 'tuningUsage');
  var payload = specWebParsePayload_(ctx.params);
  var doc = { unusedKeys: Array.isArray(payload.unusedKeys) ? payload.unusedKeys : [] };
  var saved = Storage.putItem(DDRIVE_SYNC_TUNING_USAGE_COLLECTION, DDRIVE_SYNC_TUNING_USAGE_DOC_ID, doc, { actor: ctx.auth.principal });
  return { item: saved };
});
