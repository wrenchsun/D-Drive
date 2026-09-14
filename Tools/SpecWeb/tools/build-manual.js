'use strict';

/**
 * docs/DesignerManual/*.html（デザイナーマニュアル、真実はこちら）から、
 * GAS（Tools/SpecWeb）が配信できる断片 HTML（Tools/SpecWeb/html/manual/<page>.html）と、
 * ページ名の許可リスト（Tools/SpecWeb/src/ManualPages.js）を生成する。
 *
 * 依存ゼロ（Node 標準の fs/path のみ）。docs/32_spec_web.md「マニュアル配信」節・
 * Tools/SpecWeb/README.md 参照。
 *
 * 実行方法:
 *   & "C:\Program Files\nodejs\node.exe" Tools/SpecWeb/tools/build-manual.js
 * （Tools/SpecWeb/push.ps1 が clasp push の前に自動で呼ぶ）
 *
 * 生成方式の選択理由（1 ページ = 1 GAS html ファイル。JSON 化して 1 ファイルにまとめる方式は不採用):
 *   - 既存の `include(filename)`（src/Code.js）がそのまま使え、サーバー側の実装が
 *     `HtmlService.createHtmlOutputFromFile('html/manual/' + page).getContent()` 1 行で済む
 *   - 1 ファイルの中身が生成後の最終形（style インライン化済み・リンク書き換え済み）のまま
 *     git 上で読めるため、レビュー・差分確認がしやすい
 *   - GAS の 1 ファイルあたりの上限（実務上 MB 単位まで問題ない）に対し、最大のページでも
 *     画像込みで数百 KB 程度であり、25 ページに分割してもプロジェクト全体のファイル数
 *     （現在 30 ファイル程度）が大きく増えるだけで、サイズ面での問題は生じない
 *   - 1 つの JSON にまとめる方式は、1 ページ更新しただけでも巨大な 1 ファイルの diff になり、
 *     レビューしにくくなるため見送った
 *
 * 生成物を git に入れるかどうか（コミットする / .gitignore してデプロイ前に毎回生成する）は、
 * 「コミットする」を選んだ。理由:
 *   - このリポジトリの既存の慣習（Assets/Generated/*.g.cs 等、生成物でもビルド再現性のために
 *     コミットするものがある）と一致させる
 *   - `clasp push` はローカルファイルをそのまま送るだけで、push 時にビルドステップを挟む仕組みが
 *     無いため、コミットしておけば Node が無い環境でも `clasp push` だけで最新化できる
 *   - ドリフト（docs/DesignerManual を直接更新して、生成物の再生成を忘れる）のリスクには、
 *     test/build-manual.test.js の「コミット済みファイルは現在の生成結果と一致する」テスト
 *     （drift チェック）で対応する。加えて Tools/SpecWeb/push.ps1 が push 前に必ず再生成する
 */

const fs = require('node:fs');
const path = require('node:path');

const TOOLS_DIR = __dirname;
const SPEC_WEB_DIR = path.join(TOOLS_DIR, '..');
const REPO_ROOT = path.join(SPEC_WEB_DIR, '..', '..');

const MANUAL_SOURCE_DIR = path.join(REPO_ROOT, 'docs', 'DesignerManual');
const OUTPUT_HTML_DIR = path.join(SPEC_WEB_DIR, 'html', 'manual');
const OUTPUT_PAGES_JS_PATH = path.join(SPEC_WEB_DIR, 'src', 'ManualPages.js');

const TOP_PAGE_NAME = 'Readme';
const SCOPE_CLASS = 'sw-manual-page';

const IMAGE_MIME_BY_EXT = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.svg': 'image/svg+xml'
};

const INTERNAL_LINK_RE = /^([A-Za-z0-9_-]+)\.html(?:#([A-Za-z0-9_-]+))?$/;
const SAME_PAGE_ANCHOR_RE = /^#([A-Za-z0-9_-]+)$/;
const EXTERNAL_LINK_RE = /^https?:\/\//i;

/** docs/DesignerManual/*.html のページ名一覧（拡張子なし）。トップ（Readme）を先頭に。 */
function listManualPageNames(sourceDir) {
  const names = fs
    .readdirSync(sourceDir)
    .filter((name) => name.endsWith('.html'))
    .map((name) => name.slice(0, -'.html'.length));
  names.sort((a, b) => {
    if (a === TOP_PAGE_NAME) return -1;
    if (b === TOP_PAGE_NAME) return 1;
    return a.localeCompare(b);
  });
  return names;
}

/** <body>...</body> の内側だけを取り出す。<body> が無ければ全体をそのまま返す（例外にしない）。 */
function extractBodyInnerHtml(html) {
  const match = /<body[^>]*>([\s\S]*)<\/body>/i.exec(html);
  return match ? match[1].trim() : html.trim();
}

/**
 * style.css をそのままインラインすると、body/h1/table 等の広い CSS セレクタが GAS SPA 全体
 * （ヘッダー・ナビ・他の画面）に漏れてしまう（<style> はサブツリーにスコープされないため）。
 * すべてのセレクタに `.sw-manual-page` を前置してスコープする。
 *
 * 前提: docs/DesignerManual/style.css はフラットな CSS（@media 等のネストが無い）。
 * 現状の style.css はこの前提を満たす。ネストが増えたら本関数の見直しが必要（テストで検出できる
 * 保証は無いため、style.css を編集する人は本関数のコメントも見ること）。
 */
function scopeCss(css, scopeClass) {
  const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '');
  return withoutComments
    .replace(/([^{}]+)\{/g, (whole, selectorList) => {
      const scoped = selectorList
        .split(',')
        .map((sel) => sel.trim())
        .filter((sel) => sel.length > 0)
        .map((sel) => (sel === 'body' ? '.' + scopeClass : '.' + scopeClass + ' ' + sel))
        .join(', ');
      return scoped + ' {';
    })
    .trim();
}

/**
 * ページ間リンク（`xxx.html`/`xxx.html#anchor`）・同一ページ内アンカー（`#anchor`）・
 * 外部リンク（http/https）を、GAS SPA（iframe 内ナビゲーション）向けに書き換える。
 *
 * 生成後の Index.html は `<base target="_top">` を持つため、素の `<a href="#foo">` や
 * `<a href="xxx.html">` はクリック時にトップフレームを動かそうとしてしまう
 * （html/App.html の説明コメント参照）。そのため実際のナビゲーションは
 * html/Manual.html 側のクリックハンドラ（`data-manual-page`/`data-manual-anchor`）に
 * 任せ、href 自体は「JS が動かなかったとき用の素朴なフォールバック」として残す。
 */
function rewriteLinks(html, options) {
  options = options || {};
  const knownPages = options.knownPages || null;
  const onWarning = options.onWarning || function () {};
  const warnings = [];

  const rewritten = html.replace(/<a([^>]*?)href="([^"]*)"([^>]*)>/gi, (whole, before, href, after) => {
    let m = INTERNAL_LINK_RE.exec(href);
    if (m) {
      const page = m[1];
      const anchor = m[2] || '';
      if (knownPages && knownPages.indexOf(page) === -1) {
        const message = '未知のページへのリンク（生成対象に無いページ名）: ' + href;
        warnings.push(message);
        onWarning(message);
      }
      const newHref = '?page=manual&p=' + encodeURIComponent(page) + (anchor ? '#' + anchor : '');
      const anchorAttr = anchor ? ' data-manual-anchor="' + anchor + '"' : '';
      return '<a' + before + 'href="' + newHref + '" data-manual-page="' + page + '"' + anchorAttr + after + '>';
    }

    m = SAME_PAGE_ANCHOR_RE.exec(href);
    if (m) {
      return '<a' + before + 'href="' + href + '" data-manual-anchor="' + m[1] + '"' + after + '>';
    }

    if (EXTERNAL_LINK_RE.test(href)) {
      return '<a' + before + 'href="' + href + '" target="_blank" rel="noopener"' + after + '>';
    }

    const message = '未対応の形式のリンクをそのまま残しました（要確認）: ' + href;
    warnings.push(message);
    onWarning(message);
    return whole;
  });

  return { html: rewritten, warnings };
}

function toDataUri(buffer, mimeType) {
  return 'data:' + mimeType + ';base64,' + buffer.toString('base64');
}

/** `<img src="images/x.png">` を data URI 化する。外部 URL / 既に data: なものは変更しない。 */
function inlineImages(html, options) {
  options = options || {};
  const resolveImage = options.resolveImage;
  const onWarning = options.onWarning || function () {};
  const warnings = [];

  const rewritten = html.replace(/<img([^>]*?)src="([^"]+)"([^>]*)>/gi, (whole, before, src, after) => {
    if (EXTERNAL_LINK_RE.test(src) || src.indexOf('data:') === 0) {
      return whole;
    }
    const buffer = resolveImage ? resolveImage(src) : null;
    if (!buffer) {
      const message = '画像が見つかりません（data URI 化できずそのまま残しました）: ' + src;
      warnings.push(message);
      onWarning(message);
      return whole;
    }
    const ext = path.extname(src).toLowerCase();
    const mime = IMAGE_MIME_BY_EXT[ext] || 'application/octet-stream';
    return '<img' + before + 'src="' + toDataUri(buffer, mime) + '"' + after + '>';
  });

  return { html: rewritten, warnings };
}

function buildNavBarHtml() {
  return (
    '<div class="sw-manual-navbar">' +
    '<a href="?" data-manual-exit="orders">\u2190 \u767a\u6ce8\u30c4\u30fc\u30eb\u3078</a>' +
    ' <a href="?page=manual&p=' +
    encodeURIComponent(TOP_PAGE_NAME) +
    '" data-manual-page="' +
    TOP_PAGE_NAME +
    '">\u30de\u30cb\u30e5\u30a2\u30eb\u76ee\u6b21</a>' +
    '</div>'
  );
}

/**
 * 1 ページ分の最終 HTML（GAS の html/manual/<page>.html に書き出す内容）を組み立てる。
 * @param {object} opts { pageName, rawHtml, scopedCss, knownPages, resolveImage, onWarning }
 */
function buildManualPageHtml(opts) {
  const bodyInner = extractBodyInnerHtml(opts.rawHtml);
  const imaged = inlineImages(bodyInner, { resolveImage: opts.resolveImage, onWarning: opts.onWarning });
  const linked = rewriteLinks(imaged.html, { knownPages: opts.knownPages, onWarning: opts.onWarning });
  const warnings = imaged.warnings.concat(linked.warnings);

  const html =
    '<style>' +
    opts.scopedCss +
    '</style>' +
    buildNavBarHtml() +
    '<div class="' +
    SCOPE_CLASS +
    '">' +
    linked.html +
    '</div>';

  return { html, warnings };
}

function buildManualPagesJs(pageNames) {
  const lines = [
    '/**',
    ' * docs/DesignerManual/*.html のページ名一覧（拡張子なし）。',
    ' * Tools/SpecWeb/tools/build-manual.js が生成する。手で編集しない。',
    ' * src/Manual.js の manualGet がこの一覧に対して p を検証し、無ければ',
    ' * SPEC_WEB_MANUAL_TOP_PAGE（Readme）にフォールバックする（未知ページで例外にしない）。',
    ' */',
    'var SPEC_WEB_MANUAL_TOP_PAGE = ' + JSON.stringify(TOP_PAGE_NAME) + ';',
    'var SPEC_WEB_MANUAL_PAGE_NAMES = ' + JSON.stringify(pageNames, null, 2) + ';',
    ''
  ];
  return lines.join('\n');
}

/**
 * @param {object} [options]
 * @param {string} [options.sourceDir] docs/DesignerManual 相当のディレクトリ（既定: 実物）
 * @param {string} [options.imagesDir] 画像ディレクトリ（既定: <sourceDir>/images）
 * @param {string} [options.cssPath] style.css のパス（既定: <sourceDir>/style.css）
 * @param {string} [options.outHtmlDir] 出力先（既定: Tools/SpecWeb/html/manual）
 * @param {string} [options.outPagesJsPath] ページ一覧の出力先（既定: Tools/SpecWeb/src/ManualPages.js）
 * @param {boolean} [options.write] ディスクへ書き出すか（既定 true。テストは false で使う）
 * @return {{pageNames:string[], pages:Object<string,string>, pagesJs:string, warnings:string[]}}
 */
function buildAll(options) {
  options = options || {};
  const sourceDir = options.sourceDir || MANUAL_SOURCE_DIR;
  const imagesDir = options.imagesDir || path.join(sourceDir, 'images');
  const cssPath = options.cssPath || path.join(sourceDir, 'style.css');
  const outHtmlDir = options.outHtmlDir || OUTPUT_HTML_DIR;
  const outPagesJsPath = options.outPagesJsPath || OUTPUT_PAGES_JS_PATH;
  const write = options.write !== false;

  const pageNames = listManualPageNames(sourceDir);
  const rawCss = fs.readFileSync(cssPath, 'utf8');
  const scopedCss = scopeCss(rawCss, SCOPE_CLASS);

  const warnings = [];
  const pages = {};

  for (const name of pageNames) {
    const rawHtml = fs.readFileSync(path.join(sourceDir, name + '.html'), 'utf8');
    const resolveImage = (src) => {
      const imgPath = path.join(imagesDir, path.basename(src));
      try {
        return fs.readFileSync(imgPath);
      } catch (err) {
        return null;
      }
    };
    const result = buildManualPageHtml({
      pageName: name,
      rawHtml,
      scopedCss,
      knownPages: pageNames,
      resolveImage,
      onWarning: (message) => warnings.push(name + ': ' + message)
    });
    pages[name] = result.html;
  }

  const pagesJs = buildManualPagesJs(pageNames);

  if (write) {
    fs.mkdirSync(outHtmlDir, { recursive: true });
    for (const name of pageNames) {
      fs.writeFileSync(path.join(outHtmlDir, name + '.html'), pages[name], 'utf8');
    }
    fs.writeFileSync(outPagesJsPath, pagesJs, 'utf8');
  }

  return { pageNames, pages, pagesJs, warnings };
}

function main() {
  const result = buildAll({ write: true });
  if (result.warnings.length > 0) {
    console.warn('[build-manual] warnings: ' + result.warnings.length + ' 件');
    result.warnings.forEach((w) => console.warn('  - ' + w));
  }
  console.log('[build-manual] ' + result.pageNames.length + ' ページを生成しました: ' + OUTPUT_HTML_DIR);
  console.log('[build-manual] ページ一覧を書き出しました: ' + OUTPUT_PAGES_JS_PATH);
}

if (require.main === module) {
  main();
}

module.exports = {
  TOP_PAGE_NAME,
  SCOPE_CLASS,
  MANUAL_SOURCE_DIR,
  OUTPUT_HTML_DIR,
  OUTPUT_PAGES_JS_PATH,
  listManualPageNames,
  extractBodyInnerHtml,
  scopeCss,
  rewriteLinks,
  inlineImages,
  toDataUri,
  buildNavBarHtml,
  buildManualPageHtml,
  buildManualPagesJs,
  buildAll
};
