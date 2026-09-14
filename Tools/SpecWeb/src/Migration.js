/**
 * O-1: 既存データ（旧「アセット仕様」スキーマ）を新スキーマ（アセット発注）へ移行する。
 * docs/32_spec_web.md §10.2.2（既存データの移行方法）を実装したもの。
 *
 * 方針（オーケストレーター決定、2026-09-14。実運用のデータはまだ無いため移行スクリプトは最小限）:
 *   - 旧 `assignee` → `contractor`
 *   - 旧 `status`（未着手/仮/本番/保留）→ 新3値（発注済/納品済/インポート済）
 *     ・保留 → 発注済 + コメントに「(旧: 保留)」を自動追記
 *   - 旧 `note`（プレーンテキスト） → `referenceMd`（Markdown はプレーンテキストの上位互換のため変換不要）
 *   - `orderer` は空欄で確定（§10.7 要判断2 (a)。誤った発注者を自動で入れるより空欄で気付いてもらう）
 *   - `parentId` は常に null（単体）。Presentation への紐付けは自動推定しない
 *   - 旧フィールド（assignee/note/旧4値status）は物理削除しない（§10.7 要判断3 (a)。残置）
 *
 * 「旧フィールドは読み込み時に変換し、保存時には書かない」の原則:
 *   - specWebNormalizeLegacyOrderItem_ は「読み込み時の表示用の変換」（副作用なし、Storage には書かない）。
 *     未移行データでも assets.list/assets.get は常に新スキーマの形で返す（この関数を経由するのは
 *     Assets.js の specWebAssetItemsArray_/assets.get のみ）。
 *   - migrateLegacyOrdersToNewSchema は「実データを一度だけ物理的に書き換える」移行スクリプト
 *     （admin が Apps Script エディタから直接実行するか、registerApi('migration.runLegacyOrders')
 *     経由で叩く）。二重実行しても安全（すでに新3値の status を持つ項目はスキップする＝冪等）。
 */

// 旧語彙（docs/27_spec_sheet.md §7.2 を継承していた W-4 時点の4値）。
// 値はリテラルで書く（Storage.js 冒頭の呼び出し規約と同じ理由: トップレベルの `var` 初期化で
// 他ファイル（Assets.js）のトップレベル `var` を読むと、GAS の連結順序に依存してしまうため）。
var SPEC_WEB_LEGACY_ASSET_STATUSES = ['未着手', '仮', '本番', '保留'];
var SPEC_WEB_LEGACY_STATUS_MAP = {
  '未着手': '発注済',
  '仮': '納品済',
  '本番': 'インポート済',
  '保留': '発注済'
};

/**
 * 読み込み時の表示用正規化（副作用なし）。旧データ（status が旧4値のまま等）でも
 * 新スキーマのキーで読めるようにする。すでに新スキーマの項目はそのまま返す（no-op）。
 * @param {object} item Storage から読んだ生の項目
 * @return {object} 新スキーマの形の項目（元オブジェクトは変更しない）
 */
function specWebNormalizeLegacyOrderItem_(item) {
  if (!item) return item;
  var isLegacyStatus = SPEC_WEB_LEGACY_ASSET_STATUSES.indexOf(item.status) !== -1;
  var normalized = Object.assign({}, item);
  normalized.contractor = item.contractor !== undefined && item.contractor !== '' ? item.contractor : (item.assignee || '');
  normalized.orderer = item.orderer || '';
  normalized.orderDate = item.orderDate || '';
  normalized.deliveredDate = item.deliveredDate !== undefined ? item.deliveredDate : null;
  normalized.referenceMd = item.referenceMd !== undefined ? item.referenceMd : (item.note || '');
  normalized.parentId = item.parentId !== undefined ? item.parentId : null;
  normalized.params = item.params !== undefined ? item.params : null;
  if (isLegacyStatus) {
    normalized.status = SPEC_WEB_LEGACY_STATUS_MAP[item.status];
  }
  return normalized;
}

/**
 * 実データの一度だけの物理移行（docs/32 §10.2.2）。すでに新3値の status を持つ項目は
 * スキップする（冪等。何度実行しても安全）。
 * @return {{migratedIds: string[]}}
 */
function migrateLegacyOrdersToNewSchema() {
  var itemsMap = Storage.listItems(SPEC_WEB_ASSETS_COLLECTION);
  var migratedIds = [];
  Object.keys(itemsMap).forEach(function (id) {
    var item = itemsMap[id];
    if (SPEC_WEB_LEGACY_ASSET_STATUSES.indexOf(item.status) === -1) return; // 既に新スキーマ

    var wasOnHold = item.status === '保留';
    var patch = {
      status: SPEC_WEB_LEGACY_STATUS_MAP[item.status],
      contractor: item.contractor || item.assignee || '',
      orderer: item.orderer || '',
      referenceMd: item.referenceMd !== undefined ? item.referenceMd : (item.note || ''),
      parentId: item.parentId !== undefined ? item.parentId : null
    };
    if (wasOnHold) {
      var comments = Array.isArray(item.comments) ? item.comments.slice() : [];
      comments.push({
        id: UtilitiesAdapter.newUuid(),
        author: 'migration',
        body: '(旧: 保留)',
        createdAt: new Date().toISOString(),
        resolved: false
      });
      patch.comments = comments;
    }
    Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, id, patch, { actor: 'migration', expectedRevision: item.revision });
    migratedIds.push(id);
  });
  return { migratedIds: migratedIds };
}

// Web API 経由（admin ロールのみ）でも実行できるようにする（Apps Script エディタから
// 直接 migrateLegacyOrdersToNewSchema() を呼ぶ運用と両方に対応）。
registerApi('migration.runLegacyOrders', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.ADMIN, 'この移行操作には admin 権限が必要です');
  return migrateLegacyOrdersToNewSchema();
});
