/**
 * マニュアル配信 API（2026-09-14 追加。O チケット群とは独立）。
 * docs/32_spec_web.md「マニュアル配信」節参照。
 *
 * 真実は docs/DesignerManual/*.html（デザイナーマニュアル）・docs/ProgrammerManual/*.html
 * （プログラマーマニュアル、2026-09-17 追加）。Tools/SpecWeb/tools/build-manual.js が事前に
 * style インライン化（プログラマー側は @import でデザイナー側の CSS を継承しているため、
 * インライン化の前に @import も解決する）・画像 data URI 化・ページ間/相互リンク書き換え済みの
 * 断片 HTML を html/manual/<kind>/<page>.html（GAS の HtmlService ファイル、kind は
 * "designer"/"programmer"）へ書き出し、kind ごとのページ名の許可リストを src/ManualPages.js
 * （SPEC_WEB_MANUAL_KINDS/SPEC_WEB_MANUAL_DEFAULT_KIND/SPEC_WEB_MANUAL_PAGE_NAMES/
 * SPEC_WEB_MANUAL_TOP_PAGE、同じ生成スクリプトが出力）に書き出す。このファイルはその 2 つを
 * 読んで返すだけで、HTML の組み立て自体は行わない。
 *
 * 認証: 他の API と同様、呼び出し元は google.script.run 経由の specWebUiCall
 * （① 人向け SPA、Google ログイン + users.json 許可リスト）のみを想定する。
 * viewer 以上なら誰でも読める（マニュアルは閲覧専用のため role チェックは不要）。
 * D-Drive の書き込みトークンで呼んでも実害は無い（状態を変更しないため
 * DDRIVE_WRITE_TOKEN_ALLOWED_APIS の対象外だが、読み取りトークンなら通る。
 * それ自体を制限する必要は無い）。
 */

/**
 * `params.kind`（"designer"/"programmer"）を検証する。未指定・不正なら
 * SPEC_WEB_MANUAL_DEFAULT_KIND（"designer"）にフォールバックする（例外にしない）。
 * Unity の既存 URL 契約（`?page=manual&p=...`、kind 無し）が引き続き動くようにするための
 * 後方互換の既定値でもある（Assets/DDrive/Editor/Manual/ManualUrlBuilder.cs 側も
 * kind 未指定なら designer を送る）。
 */
function specWebResolveManualKind_(rawKind) {
  var kind = String(rawKind || '');
  return SPEC_WEB_MANUAL_KINDS.indexOf(kind) !== -1 ? kind : SPEC_WEB_MANUAL_DEFAULT_KIND;
}

registerApi_('manualGet', function (ctx) {
  var params = ctx.params || {};
  var kind = specWebResolveManualKind_(params.kind);
  var pageNames = SPEC_WEB_MANUAL_PAGE_NAMES[kind] || [];
  var requested = String(params.p || '');
  var page = pageNames.indexOf(requested) !== -1 ? requested : SPEC_WEB_MANUAL_TOP_PAGE;
  var html;
  try {
    html = include_('html/manual/' + kind + '/' + page);
  } catch (err) {
    // ファイルが読めない等の予期しない状態でも例外で止めない（CLAUDE.md §0-4）。
    // トップページ自体が読めない場合はそのまま例外を伝播させる（配布物が壊れているため
    // no-op で隠すよりログに残る方が良い判断のため）。
    page = SPEC_WEB_MANUAL_TOP_PAGE;
    html = include_('html/manual/' + kind + '/' + page);
  }
  return { page: page, kind: kind, html: html };
});
