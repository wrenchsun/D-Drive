/**
 * doGet / doPost のルーティング（唯一の入口）。
 *
 * 2 つのデプロイ（docs/32_spec_web.md §2.3）は同じこの doGet/doPost を指す:
 *   ① 人向け SPA: 実行者=アクセスした人、Anyone with Google account
 *   ② D-Drive API: 実行者=Me、Anyone（ログイン不要・token で保護）
 *
 * 振り分けは `?api=1` の有無だけで行う。
 *   - 無い場合: ① の SPA を返す（Google ログイン + users.json 許可リスト）
 *   - 有る場合: API 経路。`token` パラメータがあれば② のトークン検証、
 *     無ければ① のセッションと同じ許可リスト判定を行う
 *     （authenticateRequest が両方を吸収する。Auth.js 参照）。
 *
 * 実際の API（アセット CRUD・調整値 等）はこのファイルを編集せず、
 * 各チケットが自分のファイルで `registerApi(name, handler)` するだけで追加できる
 * （Api/Registry.js）。
 */

function doGet(e) {
  return handleSpecWebRequest_(e || {});
}

function doPost(e) {
  return handleSpecWebRequest_(e || {});
}

function handleSpecWebRequest_(e) {
  var params = e.parameter || {};
  if (params.api === '1') {
    return handleApiRequest_(e);
  }
  return renderUi_();
}

/**
 * D-Drive の書き込みトークン（principal が 'ddrive:write'）で呼べる API 名の許可リスト
 * （docs/32_spec_web.md §5.2・§7、W-12）。
 *
 * 書き込みトークンはチーム全員の D-Drive に配る（同期担当者だけに限定しない、§9-9 決定）ため、
 * 「持っている人が増えても、企画が Web で入力した内容（アセット仕様の本文・調整値の値・
 * 機能仕様ページ・コメント）を D-Drive から上書きできない」ことをコード側で強制する必要がある。
 * auth.role（write トークンは editor 相当）による権限チェックだけでは
 * tuningScalarUpdate/assets.update 等も通ってしまう（実際に W-6〜W-8 実装時点でこの穴が
 * 残っていた。docs/32_spec_web.md「既知の未対応・引き継ぎ事項」参照）ため、
 * API 名そのものをここで固定する。ping/whoami は状態を変更しない（動作確認・トークン検証用）
 * ため許可リストに含めている。
 */
var DDRIVE_WRITE_TOKEN_ALLOWED_APIS = ['ping', 'whoami', 'choices', 'assetState', 'tuningUsage'];

function handleApiRequest_(e) {
  var params = e.parameter || {};
  var auth = authenticateRequest(e);
  if (!auth.ok) {
    return ContentAdapter.json({ ok: false, error: auth.message }, auth.status);
  }
  var name = params.name;
  if (auth.tokenKind === 'write' && DDRIVE_WRITE_TOKEN_ALLOWED_APIS.indexOf(name) === -1) {
    return ContentAdapter.json(
      { ok: false, error: '書き込みトークンで呼べる API ではありません（choices/assetState/tuningUsage のみ許可）: ' + name },
      403
    );
  }
  var handler = getApi(name);
  if (!handler) {
    return ContentAdapter.json({ ok: false, error: '未登録の API です: ' + name }, 404);
  }
  try {
    var result = handler({ e: e, params: params, auth: auth }) || {};
    var body = {};
    for (var key in result) {
      if (Object.prototype.hasOwnProperty.call(result, key)) body[key] = result[key];
    }
    body.ok = true;
    return ContentAdapter.json(body, 200);
  } catch (err) {
    // RevisionConflictError（409・currentRevision 付き）専用の分岐を、
    // 「err.status を持つ任意のエラー」を汎用的に本文の status へ変換する形に一般化した
    // （W-4/W-5 の入力検証エラー、W-6/W-7 の SpecWebValidationError(400)/SpecWebNotFoundError(404)/
    // SpecWebForbiddenError(403) 等、個別の err.name チェックではなく err.status を汎用的に見る形に
    // すれば同じ throw new Error() + err.status で表現できる。RevisionConflictError の挙動・
    // 既存テストは変えていない）。
    var status = err && typeof err.status === 'number' ? err.status : 500;
    var body = { ok: false, error: String((err && err.message) || err) };
    if (err && err.currentRevision !== undefined) {
      body.currentRevision = err.currentRevision;
    }
    return ContentAdapter.json(body, status);
  }
}

/** ① SPA 本体を返す。許可リスト外なら「メンバーのみ利用できます」ページを返す。 */
function renderUi_() {
  var auth = authenticateSession();
  if (!auth.ok) {
    return HtmlService.createHtmlOutput(
      '<!DOCTYPE html><html><head><meta charset="utf-8"></head>' +
        '<body style="font-family:sans-serif;padding:2rem;">' +
        '<p>メンバーのみ利用できます。管理者に users.json への追加を依頼してください。</p>' +
        '</body></html>'
    );
  }
  var template = HtmlService.createTemplateFromFile('html/Index');
  template.currentUser = auth;
  return template
    .evaluate()
    .setTitle('D-Drive 仕様書')
    .addMetaTag('viewport', 'width=device-width, initial-scale=1')
    .setXFrameOptionsMode(HtmlService.XFrameOptionsMode.ALLOWALL);
}

/** html/*.html から include するためのヘルパー（HtmlTemplate のスクリプトレット内で使う）。 */
function include(filename) {
  return HtmlService.createHtmlOutputFromFile(filename).getContent();
}
