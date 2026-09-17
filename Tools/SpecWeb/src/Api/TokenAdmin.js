/**
 * API トークンの発行・失効・ローテーション。
 *
 * 管理者が Apps Script エディタから直接この関数を選んで実行することを想定した関数群
 * （docs/32_spec_web.md §5.2・§7 の運用手順）。
 *
 * **2026-09-17（[41] 整理項目）**: 発行したトークンを自前で `Logger.log` するのをやめ、
 * **戻り値だけ**で返すようにした（V8 の Logger 出力は Cloud Logging に一定期間残るため、
 * 発行するたびにトークンの実値がログに蓄積されていた）。運用手順は README §6 を参照。
 *
 * **2026-09-17 セキュリティ修正（P1-1）**: 以前このファイルの冒頭には
 * 「Web API 経由では呼び出せない（`registerApi_` に登録していない）」と書いてあったが、
 * これは `?api=1` 経路にしか当てはまらない誤りだった。GAS では**末尾 `_` の無い
 * トップレベル関数はすべて `google.script.run.<名前>()` でクライアントから直接呼べる**。
 * 許可リスト外の Google アカウントでも、拒否ページ（Code.js の renderUi_）を開けば
 * その iframe に `google.script` ブリッジが載るため、
 * `google.script.run.issueApiToken('write')` で有効な書き込みトークンを取得できてしまっていた
 * （スクリプトプロパティは実行ユーザーに関係なく共有）。
 *
 * そこで:
 *   - 実際の処理は末尾 `_` の内部関数（`specWebIssueApiToken_` 等）に移した
 *   - 公開名のまま残す運用関数は、先頭で `specWebAssertAdminSession_()` を呼び、
 *     「ログイン済み かつ users.json 上 admin」でなければ例外で止める
 * エディタ実行時の `Session.getActiveUser()` は所有者なので、所有者が admin として
 * users.json に登録されていれば運用は変わらない（README §5 → §6 の順に行う）。
 */

function specWebGenerateToken_() {
  // getUuid() を 2 個連結して十分な長さのランダム値にする。
  return UtilitiesAdapter.newUuid().replace(/-/g, '') + UtilitiesAdapter.newUuid().replace(/-/g, '');
}

// ---- 内部実装（末尾 `_`。google.script.run からは呼べない） ----

/** 新しいトークンを発行して追加する（既存のトークンは残る＝ローテーション中の一時共存）。 */
function specWebIssueApiToken_(kind) {
  specWebAssertTokenKind_(kind);
  var token = specWebGenerateToken_();
  var tokens = getStoredTokens_(kind);
  tokens.push(token);
  setStoredTokens_(kind, tokens);
  // 2026-09-17（[41](../../../docs/41_phase6_review_2026-09-17.md) 整理項目）:
  // ここで `Logger.log(token)` していたのをやめた。V8 ランタイムの `Logger` 出力は
  // Cloud Logging に一定期間保持されるため、トークンの実値がエディタの実行ログを超えて残る。
  // 発行したトークンは**戻り値だけ**で返し、ログに出すかどうかは運用者が決める（README §6）。
  return token;
}

/** 指定したトークンを失効させる。 @return {boolean} 実際に削除できたか */
function specWebRevokeApiToken_(kind, token) {
  specWebAssertTokenKind_(kind);
  var tokens = getStoredTokens_(kind);
  var next = tokens.filter(function (t) {
    return !constantTimeEquals_(t, token);
  });
  var removed = next.length !== tokens.length;
  setStoredTokens_(kind, next);
  return removed;
}

/** 指定種別のトークンを全て失効させる。 */
function specWebRevokeAllApiTokens_(kind) {
  specWebAssertTokenKind_(kind);
  setStoredTokens_(kind, []);
}

/** 実値を出さずに現在有効なトークンの件数だけ返す。 */
function specWebCountApiTokens_(kind) {
  specWebAssertTokenKind_(kind);
  return getStoredTokens_(kind).length;
}

// ---- 運用者がエディタから実行する公開関数（admin セッション必須） ----

/**
 * 新しいトークンを発行して追加する（既存のトークンは残る＝ローテーション中の
 * 一時共存に対応。§5.2 手順 1）。
 * @param {('read'|'write')} kind
 * @return {string} 発行したトークン（この場でコピーして配布する）
 */
function issueApiToken(kind) {
  specWebAssertAdminSession_('API トークンの発行（issueApiToken）');
  return specWebIssueApiToken_(kind);
}

/**
 * 指定したトークンを失効させる（§5.2 手順 3。漏洩時は猶予期間を設けず即時実行）。
 * @return {boolean} 実際に削除できたか
 */
function revokeApiToken(kind, token) {
  specWebAssertAdminSession_('API トークンの失効（revokeApiToken）');
  return specWebRevokeApiToken_(kind, token);
}

/** 漏洩が疑われる場合の緊急対応: 指定種別のトークンを全て失効させる。 */
function revokeAllApiTokens(kind) {
  specWebAssertAdminSession_('API トークンの全失効（revokeAllApiTokens）');
  return specWebRevokeAllApiTokens_(kind);
}

/**
 * ローテーション: 新トークンを発行するだけ（旧トークンはそのまま有効のまま残る）。
 * 配布・猶予期間が過ぎたら revokeApiToken で旧トークンを個別に失効させる
 * （docs/32_spec_web.md §5.2 手順 1〜3）。
 * @return {string} 新トークン
 */
function rotateApiToken(kind) {
  specWebAssertAdminSession_('API トークンのローテーション（rotateApiToken）');
  return specWebIssueApiToken_(kind);
}

/** 実値を出さずに現在有効なトークンの件数だけ確認する（ログにも実値を残さない）。 */
function countApiTokens(kind) {
  specWebAssertAdminSession_('API トークン件数の確認（countApiTokens）');
  return specWebCountApiTokens_(kind);
}
