/**
 * O-9: メンバー管理（ガント担当者マスタの貼り付け取り込み）。
 * docs/32_spec_web.md §10.3.6（画面）・§10.5①（ガント連携、案A）を実装したもの。
 *
 * `users.json`（Auth.js の許可リスト＝ログインできる人）とは別のコレクション
 * （`members`）。ログインできる人と、発注に名前が出る人を分けるための決定
 * （docs/32 §10「決定済み」）。表記はガント側の「名前(職種)」のまま保持し、
 * 独自に変えない（例: "吉田(PLN)"）。取り込んだメンバーは発注者/受注者の選択肢になる。
 *
 * 取り込み方式は案A（貼り付け。§10.7 要判断7）: ガントのスプレッドシートには
 * 一切アクセスしない。人がガントの担当者マスタ範囲をコピー&ペーストするだけ。
 *
 * ドキュメント id は表記（label）そのもの（例: "吉田(PLN)"）。表記が一意な
 * 識別子として使える前提（同じ表記が2回貼り付けられたら同一人物として upsert する）。
 *
 * 登録している API:
 *   - members.list          一覧
 *   - members.importPaste   貼り付けテキストを解析して一括 upsert（source="gantt"）
 *   - members.upsert        手動での追加・編集（source="manual" 既定。email の対応付けも可）
 *   - members.remove        削除
 */

var SPEC_WEB_MEMBERS_COLLECTION = 'members';

function specWebMembersError_(message, status) {
  var err = new Error(message);
  err.status = status || 400;
  return err;
}

function specWebMembersRequireEditor_(auth) {
  if (!hasRole_(auth, SPEC_WEB_ROLES.EDITOR)) {
    throw specWebMembersError_('この操作には編集権限が必要です（viewer は読み取りのみ）', 403);
  }
}

/**
 * 貼り付けテキストを1行1名としてパースする純粋関数（改行区切り、前後空白を除去、
 * 空行は捨てる、重複行は1件にまとめる）。表記そのものを変えない
 * （docs/32 §10.5「表記はガント側の『名前(職種)』のまま」）。
 * CRLF/LF 両対応（既存 TuningGrid.html の parsePastedText と同じ考え方）。
 * @return {string[]} 一意化した表記の配列（出現順）
 */
function specWebParseMembersPasteText_(text) {
  if (!text) return [];
  var lines = String(text).split(/\r\n|\r|\n/);
  // 2026-09-17（[41](../../docs/41_phase6_review_2026-09-17.md) P2-15 と同根）:
  // 表記そのものがキーになるため、素の `{}` だと `constructor` / `toString` のような表記が
  // 「既に見た」と判定されて 1 件も取り込めない。prototype を持たないマップを使う。
  var seen = Object.create(null);
  var result = [];
  lines.forEach(function (line) {
    var trimmed = line.trim();
    if (!trimmed) return;
    if (seen[trimmed]) return;
    seen[trimmed] = true;
    result.push(trimmed);
  });
  return result;
}

function specWebMembersArray_() {
  var itemsMap = Storage.listItems(SPEC_WEB_MEMBERS_COLLECTION);
  return Object.keys(itemsMap).map(function (key) {
    return itemsMap[key];
  });
}

registerApi_('members.list', function () {
  return { items: specWebMembersArray_() };
});

registerApi_('members.importPaste', function (ctx) {
  specWebMembersRequireEditor_(ctx.auth);
  var text = ctx.params.text || '';
  var labels = specWebParseMembersPasteText_(text);
  if (labels.length === 0) {
    throw specWebMembersError_('貼り付けテキストが空です（1行1名の形式で貼り付けてください）', 400);
  }
  // 2026-09-17（[41] P2-11）: 貼り付けは数十名ぶんが一度に来るため、1 件ごとの
  // getItem + putItem（= members.json の全文シリアライズを人数ぶん）をやめ、
  // 1 回のロック内で「1 読み・N 件更新・1 書き」にする（Storage.mutateMany）。
  var imported = Storage.mutateMany(SPEC_WEB_MEMBERS_COLLECTION, function (tx) {
    return labels.map(function (label) {
      var existing = tx.get(label);
      // 既存の email 対応付け（members.upsert で人が入れたもの）は再取り込みでも保持する。
      return tx.put(label, { label: label, source: 'gantt', email: (existing && existing.email) || '' });
    });
  }, { actor: specWebActor_(ctx.auth) });
  return { imported: imported, importedCount: imported.length };
});

registerApi_('members.upsert', function (ctx) {
  specWebMembersRequireEditor_(ctx.auth);
  var params = ctx.params;
  var label = String(params.label || '').trim();
  if (!label) throw specWebMembersError_('表記（名前(職種)）は必須です', 400);
  var existing = Storage.getItem(SPEC_WEB_MEMBERS_COLLECTION, label);
  var saved = Storage.putItem(
    SPEC_WEB_MEMBERS_COLLECTION,
    label,
    {
      label: label,
      source: params.source || (existing ? existing.source : 'manual'),
      email: params.email !== undefined ? params.email : (existing ? existing.email : '')
    },
    { actor: specWebActor_(ctx.auth) }
  );
  return { item: saved };
});

registerApi_('members.remove', function (ctx) {
  specWebMembersRequireEditor_(ctx.auth);
  var label = ctx.params.label;
  if (!label) throw specWebMembersError_('表記（名前(職種)）は必須です', 400);
  var removed = Storage.deleteItem(SPEC_WEB_MEMBERS_COLLECTION, label, {});
  return { removed: removed };
});
