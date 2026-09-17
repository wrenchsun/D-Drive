'use strict';

/**
 * docs/DesignerManual/*.html・docs/ProgrammerManual/*.html（デザイナー/プログラマーマニュアル、
 * 真実はこちら）から、GAS（Tools/SpecWeb）が配信できる断片 HTML
 * （Tools/SpecWeb/html/manual/<kind>/<page>.html）と、kind ごとのページ名の許可リスト
 * （Tools/SpecWeb/src/ManualPages.js）を生成する。
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
 *     `HtmlService.createHtmlOutputFromFile('html/manual/' + kind + '/' + page).getContent()` 1 行で済む
 *   - 1 ファイルの中身が生成後の最終形（style インライン化済み・リンク書き換え済み）のまま
 *     git 上で読めるため、レビュー・差分確認がしやすい
 *   - GAS の 1 ファイルあたりの上限（実務上 MB 単位まで問題ない）に対し、最大のページでも
 *     画像込みで数百 KB 程度であり、2 マニュアル分に分割してもプロジェクト全体のファイル数
 *     が大きく増えるだけで、サイズ面での問題は生じない
 *   - 1 つの JSON にまとめる方式は、1 ページ更新しただけでも巨大な 1 ファイルの diff になり、
 *     レビューしにくくなるため見送った
 *
 * 生成物を git に入れるかどうか（コミットする / .gitignore してデプロイ前に毎回生成する）は、
 * 「コミットする」を選んだ。理由:
 *   - このリポジトリの既存の慣習（Assets/Generated/*.g.cs 等、生成物でもビルド再現性のために
 *     コミットするものがある）と一致させる
 *   - `clasp push` はローカルファイルをそのまま送るだけで、push 時にビルドステップを挟む仕組みが
 *     無いため、コミットしておけば Node が無い環境でも `clasp push` だけで最新化できる
 *   - ドリフト（docs/DesignerManual|ProgrammerManual を直接更新して、生成物の再生成を忘れる）の
 *     リスクには、test/build-manual.test.js の「コミット済みファイルは現在の生成結果と一致する」
 *     テスト（drift チェック）で対応する。加えて Tools/SpecWeb/push.ps1 が push 前に必ず再生成する
 *
 * 2026-09-17（プログラマーマニュアル配信対応）: デザイナーマニュアルに加えプログラマーマニュアルも
 * 同じ仕組みで配信する。2 つのマニュアルはページ名（`Readme`/`getting-started` 等）が重複するため、
 * 出力先を kind（"designer"/"programmer"）ごとのサブフォルダに分け、ページ名の許可リスト
 * （SPEC_WEB_MANUAL_PAGE_NAMES）も kind をキーにしたオブジェクトにした。プログラマーマニュアルの
 * 本文はデザイナーマニュアルへの相互リンク（`../DesignerManual/xxx.html`）を含むため、
 * rewriteLinks にクロスマニュアルリンクの書き換えを追加した。また
 * docs/ProgrammerManual/style.css は `@import url("../DesignerManual/style.css")` で
 * デザイナー側の CSS を継承しているため、スコープ化の前に @import を解決してインライン化する
 * （resolveCssImports）。単体の関数（buildAll/buildManualPageHtml/rewriteLinks 等）は
 * 1 マニュアル分の生成という既存の役割のまま拡張し、複数マニュアルの束ねは新設の
 * buildAllManuals が行う（既存の呼び出し側・テストへの影響を最小にするため）。
 */

const fs = require('node:fs');
const path = require('node:path');

const TOOLS_DIR = __dirname;
const SPEC_WEB_DIR = path.join(TOOLS_DIR, '..');
const REPO_ROOT = path.join(SPEC_WEB_DIR, '..', '..');

const MANUAL_SOURCE_DIR = path.join(REPO_ROOT, 'docs', 'DesignerManual');
const PROGRAMMER_MANUAL_SOURCE_DIR = path.join(REPO_ROOT, 'docs', 'ProgrammerManual');
const OUTPUT_HTML_DIR = path.join(SPEC_WEB_DIR, 'html', 'manual');
const OUTPUT_PAGES_JS_PATH = path.join(SPEC_WEB_DIR, 'src', 'ManualPages.js');

const TOP_PAGE_NAME = 'Readme';
const SCOPE_CLASS = 'sw-manual-page';

const DESIGNER_KIND = 'designer';
const PROGRAMMER_KIND = 'programmer';
const DEFAULT_MANUAL_KIND = DESIGNER_KIND;

// `../DesignerManual/xxx.html` のようなクロスマニュアルリンクのディレクトリ名 → kind。
const MANUAL_DIR_TO_KIND = {
  DesignerManual: DESIGNER_KIND,
  ProgrammerManual: PROGRAMMER_KIND
};

// buildAllManuals の既定の対象（kind と docs/ 側のソースフォルダ）。
const DEFAULT_MANUAL_KIND_CONFIGS = [
  { kind: DESIGNER_KIND, sourceDir: MANUAL_SOURCE_DIR },
  { kind: PROGRAMMER_KIND, sourceDir: PROGRAMMER_MANUAL_SOURCE_DIR }
];

const IMAGE_MIME_BY_EXT = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.svg': 'image/svg+xml'
};

const INTERNAL_LINK_RE = /^([A-Za-z0-9_-]+)\.html(?:#([A-Za-z0-9_-]+))?$/;
// `../DesignerManual/xxx.html` / `../ProgrammerManual/xxx.html`（相互リンク）。
const CROSS_MANUAL_LINK_RE = /^\.\.\/([A-Za-z0-9_]+)\/([A-Za-z0-9_-]+)\.html(?:#([A-Za-z0-9_-]+))?$/;
const SAME_PAGE_ANCHOR_RE = /^#([A-Za-z0-9_-]+)$/;
const EXTERNAL_LINK_RE = /^https?:\/\//i;
const CSS_IMPORT_RE = /@import\s+url\(\s*["']([^"']+)["']\s*\)\s*;?/gi;

/** docs/<Kind>Manual/*.html のページ名一覧（拡張子なし）。トップ（Readme）を先頭に。 */
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
 * 2026-09-17（プログラマーマニュアル配信対応）: `@import url("...")` を、参照先の CSS の内容で
 * その場に展開する（インライン化）。GAS 配信後は相対 URL の @import が解決できず
 * （script.googleusercontent.com 基準になり 404 になる）、かつ生成物は 1 ページ 1 ファイルの
 * 断片 HTML として `<style>` にそのまま埋め込む方式のため、ビルド時に解決しておく必要がある。
 *
 * `docs/ProgrammerManual/style.css` の `@import url("../DesignerManual/style.css")` を
 * 解決する用途を想定しており、外部 URL（http/https）の @import はブラウザが解決できるため
 * 変更しない。ローカルの相対パスが解決できない場合は警告し、@import 文をそのまま残す
 * （例外にしない。CLAUDE.md §0-4）。再帰的な @import（インポート先がさらに @import する）にも
 * 対応する（現状は 1 段しか使っていないが、素朴に再帰させておく）。
 *
 * @param {string} css
 * @param {object} [options] { resolveImport(url): string|null, onWarning(message), _depth }
 * @return {{css:string, warnings:string[]}}
 */
function resolveCssImports(css, options) {
  options = options || {};
  const resolveImport = options.resolveImport;
  const onWarning = options.onWarning || function () {};
  const depth = options._depth || 0;
  const warnings = [];

  if (depth > 5) {
    // 循環参照等で無限に再帰しないための安全弁（実際には発生しない想定。例外にはしない）。
    const message = '@import の解決が深すぎるため中断しました（循環参照の可能性）';
    warnings.push(message);
    onWarning(message);
    return { css, warnings };
  }

  const resolvedCss = css.replace(CSS_IMPORT_RE, (whole, url) => {
    if (EXTERNAL_LINK_RE.test(url)) {
      // 外部 CSS はブラウザが解決できるため変更しない。
      return whole;
    }
    const content = resolveImport ? resolveImport(url) : null;
    if (content == null) {
      const message = '@import の CSS が見つかりません（インライン化できずそのまま残しました）: ' + url;
      warnings.push(message);
      onWarning(message);
      return whole;
    }
    const nested = resolveCssImports(content, {
      resolveImport: options.nestedResolveImport || resolveImport,
      onWarning,
      _depth: depth + 1
    });
    warnings.push(...nested.warnings);
    return nested.css;
  });

  return { css: resolvedCss, warnings };
}

/**
 * style.css をそのままインラインすると、body/h1/table 等の広い CSS セレクタが GAS SPA 全体
 * （ヘッダー・ナビ・他の画面）に漏れてしまう（<style> はサブツリーにスコープされないため）。
 * すべてのセレクタに `.sw-manual-page` を前置してスコープする。
 *
 * 前提: 解決後の CSS（resolveCssImports 済み）がフラットな CSS（@media 等のネストが無い）
 * であること。現状の style.css（デザイナー/プログラマー両方）はこの前提を満たす。
 * ネストが増えたら本関数の見直しが必要（テストで検出できる保証は無いため、style.css を
 * 編集する人は本関数のコメントも見ること）。
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
 * ページ間リンク（`xxx.html`/`xxx.html#anchor`）・クロスマニュアルリンク
 * （`../DesignerManual/xxx.html`/`../ProgrammerManual/xxx.html`）・同一ページ内アンカー
 * （`#foo`）・外部リンク（http/https）を、GAS SPA（iframe 内ナビゲーション）向けに書き換える。
 *
 * 生成後の Index.html は `<base target="_top">` を持つため、素の `<a href="#foo">` や
 * `<a href="xxx.html">` はクリック時にトップフレームを動かそうとしてしまう
 * （html/App.html の説明コメント参照）。そのため実際のナビゲーションは
 * html/Manual.html 側のクリックハンドラ（`data-manual-kind`/`data-manual-page`/
 * `data-manual-anchor`）に任せ、href 自体は「JS が動かなかったとき用の素朴なフォールバック」
 * として残す。
 *
 * 緊急修正（2026-09-14）: ページ間リンクの href に以前は `?page=manual&p=xxx` を
 * 直接入れていたが、これは相対 URL のためクリック時に既定動作が走ると
 * 「iframe 自身の URL（*-script.googleusercontent.com/userCodeAppPanel）基準で解決した
 * 絶対 URL」にトップフレームが遷移してしまい、真っ白な画面になる不具合があった
 * （実デプロイで発生）。ビルド時点では実際の exec URL（デプロイごとに変わりうる）が
 * 分からないため、href はここでは安全な `#` のプレースホルダーにとどめ、実際のジャンプ先は
 * data-manual-kind/data-manual-page/data-manual-anchor/data-manual-exit を見て
 * html/Manual.html が実行時に window.SpecWebExecUrl から絶対 URL を組み立てて設定する
 * （OrderLinkLogic.buildManualUrl/buildExitUrl、O-13 の buildOrderUrl と同じ形）。
 *
 * 2026-09-17（プログラマーマニュアル配信対応）: 同一マニュアル内のリンクにも
 * `data-manual-kind`（`options.currentKind`）を付けるようにした。クロスマニュアルリンクと
 * 同じ属性名で扱えるようにするため（呼び出し側で「同一マニュアルか他マニュアルか」を
 * 区別する必要が無くなる）。
 *
 * @param {string} html
 * @param {object} [options]
 * @param {string[]} [options.knownPages] 現在の kind のページ名一覧（未知リンクの警告用）
 * @param {string} [options.currentKind] このページ自身の kind（既定 "designer"）
 * @param {Object<string,string>} [options.manualDirToKind] クロスマニュアルリンクの
 *   ディレクトリ名 → kind（既定 MANUAL_DIR_TO_KIND）
 * @param {Object<string,string[]>} [options.knownPagesByKind] kind ごとのページ名一覧
 *   （クロスマニュアルリンクの未知ページ検証用。無ければ検証をスキップする）
 * @param {function} [options.onWarning]
 */
function rewriteLinks(html, options) {
  options = options || {};
  const knownPages = options.knownPages || null;
  const currentKind = options.currentKind || DEFAULT_MANUAL_KIND;
  const manualDirToKind = options.manualDirToKind || MANUAL_DIR_TO_KIND;
  const knownPagesByKind = options.knownPagesByKind || null;
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
      const anchorAttr = anchor ? ' data-manual-anchor="' + anchor + '"' : '';
      return (
        '<a' + before + 'href="#" data-manual-kind="' + currentKind + '" data-manual-page="' + page + '"' + anchorAttr + after + '>'
      );
    }

    m = CROSS_MANUAL_LINK_RE.exec(href);
    if (m) {
      const dirName = m[1];
      const page = m[2];
      const anchor = m[3] || '';
      const kind = manualDirToKind[dirName];
      if (!kind) {
        const message = '未対応の形式のリンクをそのまま残しました（要確認）: ' + href;
        warnings.push(message);
        onWarning(message);
        return whole;
      }
      const kindPages = knownPagesByKind ? knownPagesByKind[kind] : null;
      if (kindPages && kindPages.indexOf(page) === -1) {
        const message = '未知のページへのリンク（生成対象に無いページ名、kind=' + kind + '）: ' + href;
        warnings.push(message);
        onWarning(message);
      }
      const anchorAttr = anchor ? ' data-manual-anchor="' + anchor + '"' : '';
      return (
        '<a' + before + 'href="#" data-manual-kind="' + kind + '" data-manual-page="' + page + '"' + anchorAttr + after + '>'
      );
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

/**
 * 2026-09-17（docs/41 整理項目）: rewriteLinks / inlineImages を通した**後**の HTML に、
 * 書き換え・インライン化から漏れた `<a>` / `<img>` が残っていないかを見る。
 *
 * 両関数の正規表現は `href="…"` / `src="…"`（ダブルクォート）にしか掛からないため、
 * `href='…'`（シングルクォート）や `srcset` はヒットせず、**警告も出ないまま素通りしていた**。
 * 素通りした相対リンク・相対画像は、GAS 配信後に `script.googleusercontent.com` 基準で
 * 解決されて 404 になる（画面は白くならないが、リンクと画像が壊れる）。
 * ここで気付けるよう、最後にもう一度走査して警告にする。
 *
 * @param {string} html rewriteLinks / inlineImages を通した後の HTML
 * @return {string[]} 警告メッセージ（空なら漏れ無し）
 */
function findUnprocessedLinkTags(html) {
  const warnings = [];
  const tagRe = /<(a|img)\b([^>]*)>/gi;
  let m;
  while ((m = tagRe.exec(html)) !== null) {
    const tag = m[1].toLowerCase();
    const attrs = m[2];

    if (/(?:^|\s)(?:href|src|srcset)\s*=\s*'/i.test(attrs)) {
      warnings.push(
        '属性がシングルクォートで書かれているため書き換え・data URI 化されていません' +
          '（docs/DesignerManual 側を href="…" / src="…" に直してください）: <' + tag + attrs + '>'
      );
      continue;
    }

    if (tag === 'img') {
      if (/(?:^|\s)srcset\s*=/i.test(attrs)) {
        warnings.push('img の srcset は data URI 化されません（配信後に読み込めません）: <img' + attrs + '>');
      }
      const srcMatch = /(?:^|\s)src\s*=\s*"([^"]*)"/i.exec(attrs);
      if (srcMatch && !/^(?:data:|https?:\/\/)/i.test(srcMatch[1])) {
        warnings.push('img の src が data URI 化されずに残っています: ' + srcMatch[1]);
      }
      continue;
    }

    const hrefMatch = /(?:^|\s)href\s*=\s*"([^"]*)"/i.exec(attrs);
    if (!hrefMatch) continue;
    const href = hrefMatch[1];
    const handled = href.charAt(0) === '#' || /^(?:https?:\/\/|mailto:)/i.test(href);
    if (!handled) {
      warnings.push('a の href が書き換えられずに残っています（配信後に解決できません）: ' + href);
    }
  }
  return warnings;
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

/**
 * @param {string} [currentKind] このページ自身の kind（"マニュアル目次" リンクを同じ kind の
 *   トップへ向けるために使う。既定 "designer"）
 */
function buildNavBarHtml(currentKind) {
  const kind = currentKind || DEFAULT_MANUAL_KIND;
  // 緊急修正（2026-09-14）: href="?" / href="?page=..." は rewriteLinks() と同じ理由で
  // トップフレームの既定動作が走ると白画面になるため、ここも "#" + data 属性にする
  // （html/Manual.html が window.SpecWebExecUrl から実際の href を設定する）。
  return (
    '<div class="sw-manual-navbar">' +
    '<a href="#" data-manual-exit="orders">\u2190 \u767a\u6ce8\u30c4\u30fc\u30eb\u3078</a>' +
    ' <a href="#" data-manual-kind="' +
    kind +
    '" data-manual-page="' +
    TOP_PAGE_NAME +
    '">\u30de\u30cb\u30e5\u30a2\u30eb\u76ee\u6b21</a>' +
    '</div>'
  );
}

/**
 * 1 ページ分の最終 HTML（GAS の html/manual/<kind>/<page>.html に書き出す内容）を組み立てる。
 * @param {object} opts { pageName, rawHtml, scopedCss, knownPages, currentKind, manualDirToKind,
 *   knownPagesByKind, resolveImage, onWarning }
 */
function buildManualPageHtml(opts) {
  const currentKind = opts.currentKind || DEFAULT_MANUAL_KIND;
  const bodyInner = extractBodyInnerHtml(opts.rawHtml);
  const imaged = inlineImages(bodyInner, { resolveImage: opts.resolveImage, onWarning: opts.onWarning });
  const linked = rewriteLinks(imaged.html, {
    knownPages: opts.knownPages,
    currentKind: currentKind,
    manualDirToKind: opts.manualDirToKind,
    knownPagesByKind: opts.knownPagesByKind,
    onWarning: opts.onWarning
  });
  // 2026-09-17（docs/41 整理項目）: 書き換え漏れ（シングルクォート・srcset・相対リンク）が
  // 残っていないかを最後に確認する。onWarning を通すので buildAll の警告出力にも載る。
  const leftovers = findUnprocessedLinkTags(linked.html);
  const onWarning = opts.onWarning || function () {};
  leftovers.forEach(function (message) {
    onWarning(message);
  });
  const warnings = imaged.warnings.concat(linked.warnings, leftovers);

  const html =
    '<style>' +
    opts.scopedCss +
    '</style>' +
    buildNavBarHtml(currentKind) +
    '<div class="' +
    SCOPE_CLASS +
    '">' +
    linked.html +
    '</div>';

  return { html, warnings };
}

/** 単一 kind（旧形式・後方互換）のページ名一覧 JS。buildAll が単独で呼ばれたときに使う。 */
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
 * 2026-09-17（プログラマーマニュアル配信対応）: 複数 kind 分のページ名一覧 JS。
 * 実際に GAS へコミットする `src/ManualPages.js` はこちらの形式（kind をキーにしたオブジェクト）。
 * ページ名が両マニュアルで重複する（`Readme`/`getting-started`）ため、フラットな配列 1 つには
 * まとめられない。
 *
 * @param {Object<string,string[]>} pageNamesByKind
 * @param {string} defaultKind kind 未指定・不正なときのフォールバック（"designer"）
 * @param {string} topPageName 各 kind 共通のトップページ名（"Readme"）
 */
function buildManualPagesJsMulti(pageNamesByKind, defaultKind, topPageName) {
  const kinds = Object.keys(pageNamesByKind);
  const lines = [
    '/**',
    ' * docs/DesignerManual・docs/ProgrammerManual の *.html のページ名一覧（拡張子なし）。',
    ' * kind（"designer"/"programmer"）ごとに分かれている（ページ名が両マニュアルで重複するため）。',
    ' * Tools/SpecWeb/tools/build-manual.js が生成する。手で編集しない。',
    ' * src/Manual.js の manualGet がこの一覧に対して kind+p を検証し、無ければ',
    ' * SPEC_WEB_MANUAL_DEFAULT_KIND / SPEC_WEB_MANUAL_TOP_PAGE にフォールバックする',
    ' * （未知の kind/ページで例外にしない）。',
    ' */',
    'var SPEC_WEB_MANUAL_KINDS = ' + JSON.stringify(kinds) + ';',
    'var SPEC_WEB_MANUAL_DEFAULT_KIND = ' + JSON.stringify(defaultKind) + ';',
    'var SPEC_WEB_MANUAL_TOP_PAGE = ' + JSON.stringify(topPageName) + ';',
    'var SPEC_WEB_MANUAL_PAGE_NAMES = ' + JSON.stringify(pageNamesByKind, null, 2) + ';',
    ''
  ];
  return lines.join('\n');
}

/**
 * 1 マニュアル（1 kind）分の生成。既存の呼び出し側・テストへの影響を避けるため、
 * 単独で呼んだときの挙動（引数を省略したときに docs/DesignerManual を対象にする等）は
 * 2026-09-14 実装時点のまま変えていない。プログラマーマニュアル配信対応（2026-09-17）で
 * 追加したのは、クロスマニュアルリンクの書き換えに使う `options.kind`/`manualDirToKind`/
 * `knownPagesByKind` と、CSS の `@import` 解決・複数 kind から呼ばれたときに
 * `src/ManualPages.js` を上書きしないための `options.writePagesJs` だけで、
 * いずれも省略時は従来どおり動く。
 *
 * @param {object} [options]
 * @param {string} [options.sourceDir] docs/DesignerManual 相当のディレクトリ（既定: 実物）
 * @param {string} [options.imagesDir] 画像ディレクトリ（既定: <sourceDir>/images）
 * @param {string} [options.cssPath] style.css のパス（既定: <sourceDir>/style.css）
 * @param {string} [options.outHtmlDir] 出力先（既定: Tools/SpecWeb/html/manual）
 * @param {string} [options.outPagesJsPath] ページ一覧の出力先（既定: Tools/SpecWeb/src/ManualPages.js）
 * @param {boolean} [options.write] ディスクへ書き出すか（既定 true。テストは false で使う）
 * @param {boolean} [options.writePagesJs] `outPagesJsPath` へ書き出すか（既定 true。
 *   buildAllManuals が複数 kind をまとめて 1 つの ManualPages.js に書き出すため false を渡す）
 * @param {string} [options.kind] このマニュアルの kind（既定 "designer"）
 * @param {Object<string,string>} [options.manualDirToKind] クロスマニュアルリンクの解決に使う
 * @param {Object<string,string[]>} [options.knownPagesByKind] クロスマニュアルリンクの
 *   未知ページ検証に使う（無ければ検証をスキップ）
 * @return {{pageNames:string[], pages:Object<string,string>, pagesJs:string, warnings:string[], kind:string}}
 */
function buildAll(options) {
  options = options || {};
  const sourceDir = options.sourceDir || MANUAL_SOURCE_DIR;
  const imagesDir = options.imagesDir || path.join(sourceDir, 'images');
  const cssPath = options.cssPath || path.join(sourceDir, 'style.css');
  const outHtmlDir = options.outHtmlDir || OUTPUT_HTML_DIR;
  const outPagesJsPath = options.outPagesJsPath || OUTPUT_PAGES_JS_PATH;
  const write = options.write !== false;
  const writePagesJs = options.writePagesJs !== false;
  const kind = options.kind || DEFAULT_MANUAL_KIND;

  const pageNames = listManualPageNames(sourceDir);

  const warnings = [];

  // Windows では docs/DesignerManual が CRLF でチェックアウトされるため、そのまま通すと生成物が CRLF になり
  // 毎回の再生成（push.cmd）で改行コードだけの差分が出る。生成物は LF に揃える（.gitattributes と同じ）。
  const rawCss = fs.readFileSync(cssPath, 'utf8').replace(/\r\n/g, '\n');
  const cssDir = path.dirname(cssPath);
  const resolveCssImport = (url) => {
    try {
      return fs.readFileSync(path.join(cssDir, url), 'utf8').replace(/\r\n/g, '\n');
    } catch (err) {
      return null;
    }
  };
  const resolvedCss = resolveCssImports(rawCss, {
    resolveImport: resolveCssImport,
    onWarning: (message) => warnings.push('style.css: ' + message)
  });
  const scopedCss = scopeCss(resolvedCss.css, SCOPE_CLASS);

  const pages = {};

  for (const name of pageNames) {
    const rawHtml = fs.readFileSync(path.join(sourceDir, name + '.html'), 'utf8').replace(/\r\n/g, '\n');
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
      currentKind: kind,
      manualDirToKind: options.manualDirToKind,
      knownPagesByKind: options.knownPagesByKind,
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
    if (writePagesJs) {
      fs.writeFileSync(outPagesJsPath, pagesJs, 'utf8');
    }
  }

  return { pageNames, pages, pagesJs, warnings, kind };
}

/**
 * 2026-09-17（プログラマーマニュアル配信対応）: 複数マニュアル（kind）をまとめて生成する。
 * 実際の生成（push.ps1/push.cmd 経由の main()、drift 検出テスト）はこちらを使う。
 *
 * 1 パス目で各 kind のページ名一覧を集め（クロスマニュアルリンクの検証に使う）、
 * 2 パス目で kind ごとに buildAll を呼ぶ（`html/manual/<kind>/<page>.html` へ出力、
 * `writePagesJs: false` で個別の ManualPages.js 書き出しは止める）。最後に全 kind 分の
 * ページ名一覧を 1 つの `src/ManualPages.js` にまとめて書き出す。
 *
 * @param {object} [options]
 * @param {{kind:string, sourceDir?:string, imagesDir?:string, cssPath?:string, outHtmlDir?:string}[]} [options.kinds]
 *   既定: デザイナー（docs/DesignerManual）+ プログラマー（docs/ProgrammerManual）
 * @param {string} [options.outHtmlBaseDir] 既定 Tools/SpecWeb/html/manual（各 kind はこの下の
 *   サブフォルダに出力する）
 * @param {string} [options.outPagesJsPath] 既定 Tools/SpecWeb/src/ManualPages.js
 * @param {Object<string,string>} [options.manualDirToKind]
 * @param {boolean} [options.write] 既定 true
 * @return {{kinds:string[], pageNamesByKind:Object<string,string[]>, pagesByKind:Object<string,Object<string,string>>, pagesJs:string, warnings:string[]}}
 */
function buildAllManuals(options) {
  options = options || {};
  const write = options.write !== false;
  const kindConfigs = options.kinds || DEFAULT_MANUAL_KIND_CONFIGS;
  const outBaseDir = options.outHtmlBaseDir || OUTPUT_HTML_DIR;
  const outPagesJsPath = options.outPagesJsPath || OUTPUT_PAGES_JS_PATH;
  const manualDirToKind = options.manualDirToKind || MANUAL_DIR_TO_KIND;

  // 1 パス目: 先にページ名一覧だけ集める（クロスマニュアルリンクの未知ページ検証に使うため、
  // 各 kind の本文を処理する前にすべての kind の一覧が要る）。
  const pageNamesByKind = {};
  for (const cfg of kindConfigs) {
    pageNamesByKind[cfg.kind] = listManualPageNames(cfg.sourceDir);
  }

  const warnings = [];
  const pagesByKind = {};

  for (const cfg of kindConfigs) {
    const outHtmlDir = cfg.outHtmlDir || path.join(outBaseDir, cfg.kind);
    const result = buildAll({
      sourceDir: cfg.sourceDir,
      imagesDir: cfg.imagesDir,
      cssPath: cfg.cssPath,
      outHtmlDir,
      kind: cfg.kind,
      manualDirToKind,
      knownPagesByKind: pageNamesByKind,
      write,
      writePagesJs: false
    });
    pagesByKind[cfg.kind] = result.pages;
    result.warnings.forEach((w) => warnings.push(cfg.kind + '/' + w));
  }

  const pagesJs = buildManualPagesJsMulti(pageNamesByKind, DEFAULT_MANUAL_KIND, TOP_PAGE_NAME);

  if (write) {
    fs.writeFileSync(outPagesJsPath, pagesJs, 'utf8');
  }

  return {
    kinds: kindConfigs.map((cfg) => cfg.kind),
    pageNamesByKind,
    pagesByKind,
    pagesJs,
    warnings
  };
}

function main() {
  const result = buildAllManuals({ write: true });
  if (result.warnings.length > 0) {
    console.warn('[build-manual] warnings: ' + result.warnings.length + ' 件');
    result.warnings.forEach((w) => console.warn('  - ' + w));
  }
  for (const kind of result.kinds) {
    console.log(
      '[build-manual] (' +
        kind +
        ') ' +
        result.pageNamesByKind[kind].length +
        ' ページを生成しました: ' +
        path.join(OUTPUT_HTML_DIR, kind)
    );
  }
  console.log('[build-manual] ページ一覧を書き出しました: ' + OUTPUT_PAGES_JS_PATH);
}

if (require.main === module) {
  main();
}

module.exports = {
  TOP_PAGE_NAME,
  SCOPE_CLASS,
  MANUAL_SOURCE_DIR,
  PROGRAMMER_MANUAL_SOURCE_DIR,
  OUTPUT_HTML_DIR,
  OUTPUT_PAGES_JS_PATH,
  DESIGNER_KIND,
  PROGRAMMER_KIND,
  DEFAULT_MANUAL_KIND,
  MANUAL_DIR_TO_KIND,
  listManualPageNames,
  extractBodyInnerHtml,
  resolveCssImports,
  scopeCss,
  rewriteLinks,
  inlineImages,
  findUnprocessedLinkTags,
  toDataUri,
  buildNavBarHtml,
  buildManualPageHtml,
  buildManualPagesJs,
  buildManualPagesJsMulti,
  buildAll,
  buildAllManuals
};
