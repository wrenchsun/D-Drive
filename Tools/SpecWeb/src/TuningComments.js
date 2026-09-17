/**
 * 調整値のコメント（W-6/W-7 共通、docs/32_spec_web.md §3.6・§4.4）。
 *
 * 対象は 3 種類（§3.2.4 の粒度決定に対応）:
 *   - "scalar"   スカラー調整値 1 件（tuning コレクション、comments[]）
 *   - "table"    テーブル全体（tuningTables コレクション、comments[]）
 *   - "tableRow" テーブルの行 1 件（tuningTables コレクション、rows[].comments[]）
 * セル単位のコメントは docs/32 §3.2.2・§9-5 の決定により実装しない。
 *
 * 投稿は revision を要求しない（TuningCommon.js の specWebMutateItemAtomic_ で
 * ロック内に読み直してから追記するため、他の人が値を編集していても
 * コメントだけは競合しない）。閲覧は viewer 以上、投稿は editor 以上
 * （viewer は閲覧のみ、docs/32 §3.4）。
 */

var TUNING_COMMENT_TARGET_KINDS = ['scalar', 'table', 'tableRow'];

function specWebCommentCollectionFor_(targetKind) {
  if (targetKind === 'scalar') return TUNING_SCALAR_COLLECTION;
  if (targetKind === 'table' || targetKind === 'tableRow') return TUNING_TABLE_COLLECTION;
  throw new SpecWebValidationError('targetKind が不正です: ' + targetKind);
}

function specWebReadCommentsFor_(item, targetKind, rowId) {
  if (targetKind === 'tableRow') {
    var row = null;
    (item.rows || []).forEach(function (r) {
      if (r.rowId === rowId) row = r;
    });
    if (!row) throw new SpecWebNotFoundError('行が存在しません: ' + rowId);
    return row.comments || [];
  }
  return item.comments || [];
}

registerApi_('tuningCommentList', function (ctx) {
  var params = ctx.params;
  var targetKind = params.targetKind;
  if (TUNING_COMMENT_TARGET_KINDS.indexOf(targetKind) === -1) {
    throw new SpecWebValidationError('targetKind が不正です: ' + targetKind);
  }
  var collection = specWebCommentCollectionFor_(targetKind);
  var item = Storage.getItem(collection, params.key);
  if (!item) throw new SpecWebNotFoundError('存在しません: ' + params.key);
  return { comments: specWebReadCommentsFor_(item, targetKind, params.rowId) };
});

registerApi_('tuningCommentAdd', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.EDITOR, 'コメントの投稿には editor 以上の権限が必要です');
  var payload = specWebParsePayload_(ctx.params);
  var targetKind = payload.targetKind;
  if (TUNING_COMMENT_TARGET_KINDS.indexOf(targetKind) === -1) {
    throw new SpecWebValidationError('targetKind が不正です: ' + targetKind);
  }
  var collection = specWebCommentCollectionFor_(targetKind);
  var author = ctx.auth.principal;
  var comment = specWebMakeComment_(author, payload.body);

  var saved = specWebMutateItemAtomic_(
    collection,
    payload.key,
    function (existing) {
      if (targetKind === 'tableRow') {
        var found = false;
        var rows = existing.rows.map(function (r) {
          if (r.rowId !== payload.rowId) return r;
          found = true;
          var comments = (r.comments || []).concat([comment]);
          return Object.assign({}, r, { comments: comments });
        });
        if (!found) throw new SpecWebNotFoundError('行が存在しません: ' + payload.rowId);
        return { rows: rows };
      }
      var comments = (existing.comments || []).concat([comment]);
      return { comments: comments };
    },
    author
  );

  return { item: saved, comment: comment };
});
