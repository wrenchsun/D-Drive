/**
 * API トークンの発行・失効・ローテーション。
 *
 * Web API 経由では呼び出せない（doGet/doPost の ApiRegistry に登録していない）。
 * 管理者が Apps Script エディタから直接この関数を選んで実行することを想定した
 * 関数群（docs/32_spec_web.md §5.2・§7 の運用手順）。
 * 発行したトークンは実行ログ（表示 > 実行数）に出るので、そこからコピーして
 * チームに配布する。
 */

function specWebGenerateToken_() {
  // getUuid() を 2 個連結して十分な長さのランダム値にする。
  return UtilitiesAdapter.newUuid().replace(/-/g, '') + UtilitiesAdapter.newUuid().replace(/-/g, '');
}

/**
 * 新しいトークンを発行して追加する（既存のトークンは残る＝ローテーション中の
 * 一時共存に対応。§5.2 手順 1）。
 * @param {('read'|'write')} kind
 * @return {string} 発行したトークン（この場でコピーして配布する）
 */
function issueApiToken(kind) {
  specWebAssertTokenKind_(kind);
  var token = specWebGenerateToken_();
  var tokens = getStoredTokens_(kind);
  tokens.push(token);
  setStoredTokens_(kind, tokens);
  UtilitiesAdapter.log('発行した ' + kind + ' トークン（配布用、この場でコピーしてください）: ' + token);
  return token;
}

/**
 * 指定したトークンを失効させる（§5.2 手順 3。漏洩時は猶予期間を設けず即時実行）。
 * @return {boolean} 実際に削除できたか
 */
function revokeApiToken(kind, token) {
  specWebAssertTokenKind_(kind);
  var tokens = getStoredTokens_(kind);
  var next = tokens.filter(function (t) {
    return !constantTimeEquals_(t, token);
  });
  var removed = next.length !== tokens.length;
  setStoredTokens_(kind, next);
  return removed;
}

/** 漏洩が疑われる場合の緊急対応: 指定種別のトークンを全て失効させる。 */
function revokeAllApiTokens(kind) {
  specWebAssertTokenKind_(kind);
  setStoredTokens_(kind, []);
}

/**
 * ローテーション: 新トークンを発行するだけ（旧トークンはそのまま有効のまま残る）。
 * 配布・猶予期間が過ぎたら revokeApiToken で旧トークンを個別に失効させる
 * （docs/32_spec_web.md §5.2 手順 1〜3）。
 * @return {string} 新トークン
 */
function rotateApiToken(kind) {
  return issueApiToken(kind);
}

/** 実値を出さずに現在有効なトークンの件数だけ確認する（ログにも実値を残さない）。 */
function countApiTokens(kind) {
  specWebAssertTokenKind_(kind);
  return getStoredTokens_(kind).length;
}
