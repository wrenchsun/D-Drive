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

registerApi_('paramSchemas.list', function (ctx) {
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
registerApi_('assetParams', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.EDITOR, 'パラメータの送信には editor 以上の権限が必要です');
  // 2026-09-17（[41](../../docs/41_phase6_review_2026-09-17.md) P2-12）: 他の 3 kind
  // （choices / assetState / tuningUsage、DDriveSync.js）と docs/32 §7「送信 API にはレート制限を
  // 設ける」に合わせる。これが無いと、書き込みトークンを持っている人が連打するだけで
  // Drive のクォータを食い潰せてしまう（書き込みトークンはチーム全員に配る、§9-9 決定）。
  specWebCheckRateLimit_(ctx.auth.principal, 'assetParams');
  var payload = specWebParsePayload_(ctx.params);
  var schemas = Array.isArray(payload.schemas) ? payload.schemas : [];
  var items = Array.isArray(payload.items) ? payload.items : [];

  // 2026-09-17（[41] P2-11）: 1 件ごとの getItem + putItem をやめ、コレクション単位で
  // 「1 回のロック内で 1 読み・N 件更新・1 書き」にする（Storage.mutateMany。docs/32 §2.4）。
  var updatedSchemaTypes = [];
  if (schemas.length > 0) {
    Storage.mutateMany(SPEC_WEB_PARAM_SCHEMAS_COLLECTION, function (tx) {
      schemas.forEach(function (schema) {
        if (!schema || !schema.concreteType) return;
        var doc = {
          assetType: schema.assetType || '',
          concreteType: schema.concreteType,
          fields: Array.isArray(schema.fields) ? schema.fields : []
        };
        tx.put(schema.concreteType, doc);
        updatedSchemaTypes.push(schema.concreteType);
      });
    }, { actor: ctx.auth.principal });
  }

  var updatedIds = [];
  var skippedIds = [];
  if (items.length > 0) {
    Storage.mutateMany(SPEC_WEB_ASSETS_COLLECTION, function (tx) {
      items.forEach(function (entry) {
        if (!entry || !entry.id) return;
        var existing = tx.get(entry.id);
        if (!existing) {
          skippedIds.push(entry.id);
          return;
        }
        var params = {
          concreteType: entry.concreteType || null,
          currentValues: entry.currentValues && typeof entry.currentValues === 'object' ? entry.currentValues : {}
        };
        tx.put(entry.id, { params: params });
        updatedIds.push(entry.id);
      });
    }, { actor: ctx.auth.principal });
  }

  return { updatedSchemaTypes: updatedSchemaTypes, updatedIds: updatedIds, skippedIds: skippedIds };
});
