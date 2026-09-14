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

function handleApiRequest_(e) {
  var params = e.parameter || {};
  var auth = authenticateRequest(e);
  if (!auth.ok) {
    return ContentAdapter.json({ ok: false, error: auth.message }, auth.status);
  }
  var name = params.name;
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
    if (err && err.name === 'RevisionConflictError') {
      return ContentAdapter.json(
        { ok: false, error: err.message, currentRevision: err.currentRevision },
        err.status || 409
      );
    }
    return ContentAdapter.json({ ok: false, error: String((err && err.message) || err) }, 500);
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
