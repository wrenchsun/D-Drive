/**
 * Session を薄く包むアダプタ。
 *
 * デプロイ①（実行者=アクセスした人、docs/32_spec_web.md §2.3）でのみ、
 * Session.getActiveUser().getEmail() がアクセスした人のメールを返す。
 * デプロイ②（実行者=Me、D-Drive からのトークン付きアクセス）や、
 * 未ログイン・スコープ未許可の状況では空文字列になる
 * （developers.google.com/apps-script/reference/base/session で確認済み）。
 * 呼び出し側はこの「空文字列 = 認証できない」を前提に許可リスト判定を行う
 * （Auth.js の authenticateSession）。
 */

var SessionAdapter = {
  getActiveUserEmail: function () {
    try {
      var email = Session.getActiveUser().getEmail();
      return email || '';
    } catch (err) {
      // スコープ不足・実行コンテキストによっては例外になることがある。
      // CLAUDE.md §0-4「例外で止めない」と同じ考え方で、空文字列（未認証）として扱う。
      return '';
    }
  }
};
