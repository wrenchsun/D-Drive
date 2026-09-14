/**
 * O-6（Web 側の受け皿のみ）: 種別ごとのパラメータスキーマ・現在値。
 * docs/32_spec_web.md §10.2.4・§10.4.2 のデータ形・API を実装したもの。
 *
 * このファイルが作るのは「スキーマの保存・詳細画面での表示」の受け皿だけ。
 * D-Drive からの実際の送信（`SerializedObject`+`Tooltip` 反射での自動生成・送信）は
 * 別チケット（O-6 の D-Drive 側）が実装する。書き込みトークンの許可表
 * （Code.js の DDRIVE_WRITE_TOKEN_ALLOWED_APIS）に `assetParams` を追加するのも
 * その別チケットで行う（本チケットでは Code.js を編集しない）。それまでは
 * 書き込みトークンでこの API を呼んでも Code.js のゲートで 403 になる
 * （Google ログイン＝人が admin/editor 権限で直接呼ぶことは可能。動作確認用）。
 *
 * データ形（§10.2.4）:
 *   paramSchemas コレクション（doc id = concreteType。ControlSkin のみ 1 つの assetType に
 *   対して ButtonSkinData/SliderSkinData の 2 concreteType が並ぶ）:
 *     { assetType, concreteType, fields: [ { name, type, tooltip, min?, max?, unit? }, ... ] }
 *
 *   assets コレクションの各項目が持つ `params` フィールド（Assets.js の書き込み系 API では
 *   受け付けない。ここでだけ書く）:
 *     { concreteType, currentValues: { <field name>: <表示用の文字列/数値/真偽値> } }
 *   currentValues の ObjectReference 型フィールドは値そのものではなく表示名の文字列のみを
 *   持つ（D-Drive 側が組み立てて送る。企画が Web 側で値を書き換える経路は作らない）。
 *
 * 登録している API:
 *   - paramSchemas.list  スキーマ一覧（assetType で絞り込み可。詳細画面が使う）
 *   - assetParams        D-Drive → Web の一方向送信（schemas + items を一括 upsert）
 */

var SPEC_WEB_PARAM_SCHEMAS_COLLECTION = 'paramSchemas';

function specWebAssetParamsError_(message, status) {
  var err = new Error(message);
  err.status = status || 400;
  return err;
}

registerApi('paramSchemas.list', function (ctx) {
  var assetType = ctx.params.assetType;
  var itemsMap = Storage.listItems(SPEC_WEB_PARAM_SCHEMAS_COLLECTION);
  var items = Object.keys(itemsMap)
    .map(function (key) {
      return itemsMap[key];
    })
    .filter(function (item) {
      return !assetType || item.assetType === assetType;
    });
  return { items: items };
});

/**
 * D-Drive → Web 送信（一方向。§10.4.2「企画が値を書き換える経路は作らない」）。
 * payload: { schemas: [ {assetType, concreteType, fields} ... ], items: [ {id, concreteType, currentValues} ... ] }
 * schemas は毎回フルリプレイス（16種類分を1回の同期で送る想定、既存の choices と同じ考え方）。
 * items は Assets.js のコレクション（assets）に存在する id だけを patch する
 * （assetState と同じ「存在しない id は静かにスキップ」方式。例外にしない）。
 */
registerApi('assetParams', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.EDITOR, 'パラメータの送信には editor 以上の権限が必要です');
  var payload = specWebParsePayload_(ctx.params);
  var schemas = Array.isArray(payload.schemas) ? payload.schemas : [];
  var items = Array.isArray(payload.items) ? payload.items : [];

  var updatedSchemaTypes = [];
  schemas.forEach(function (schema) {
    if (!schema || !schema.concreteType) return;
    var doc = {
      assetType: schema.assetType || '',
      concreteType: schema.concreteType,
      fields: Array.isArray(schema.fields) ? schema.fields : []
    };
    Storage.putItem(SPEC_WEB_PARAM_SCHEMAS_COLLECTION, schema.concreteType, doc, { actor: ctx.auth.principal });
    updatedSchemaTypes.push(schema.concreteType);
  });

  var updatedIds = [];
  var skippedIds = [];
  items.forEach(function (entry) {
    if (!entry || !entry.id) return;
    var existing = Storage.getItem(SPEC_WEB_ASSETS_COLLECTION, entry.id);
    if (!existing) {
      skippedIds.push(entry.id);
      return;
    }
    var params = {
      concreteType: entry.concreteType || null,
      currentValues: entry.currentValues && typeof entry.currentValues === 'object' ? entry.currentValues : {}
    };
    Storage.putItem(SPEC_WEB_ASSETS_COLLECTION, entry.id, { params: params }, { actor: ctx.auth.principal });
    updatedIds.push(entry.id);
  });

  return { updatedSchemaTypes: updatedSchemaTypes, updatedIds: updatedIds, skippedIds: skippedIds };
});
