/**
 * ContentService を薄く包むアダプタ。
 *
 * 重要: Apps Script の Content Service には HTTP ステータスコードを
 * 設定する API が存在しない（setMimeType 相当の setStatusCode が無い。
 * developers.google.com/apps-script/guides/content で確認）。
 * そのため 401/403/404/409 等は「本文中の status フィールド」で表現する
 * （docs/32_spec_web.md の「409 相当」という表現はこの制約を踏まえたもの）。
 * API を呼ぶ側（D-Drive の SpecWebFetcher 等、W-9 以降）は本文の
 * `ok`/`status` を見てエラー処理する。
 */

var ContentAdapter = {
  /**
   * obj を JSON として返す。status を渡すと本文に status フィールドとして
   * 埋め込む（実際の HTTP ステータスは常に 200 系になる点に注意）。
   */
  json: function (obj, status) {
    var body = {};
    for (var key in obj) {
      if (Object.prototype.hasOwnProperty.call(obj, key)) body[key] = obj[key];
    }
    if (status !== undefined && status !== null && body.status === undefined) {
      body.status = status;
    }
    var output = ContentService.createTextOutput(JSON.stringify(body));
    output.setMimeType(ContentService.MimeType.JSON);
    return output;
  }
};
