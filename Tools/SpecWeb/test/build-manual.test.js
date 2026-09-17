'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {
  scopeCss,
  rewriteLinks,
  inlineImages,
  findUnprocessedLinkTags,
  toDataUri,
  extractBodyInnerHtml,
  resolveCssImports,
  buildManualPageHtml,
  buildAll,
  buildAllManuals,
  TOP_PAGE_NAME,
  SCOPE_CLASS,
  OUTPUT_HTML_DIR,
  OUTPUT_PAGES_JS_PATH,
  DESIGNER_KIND,
  PROGRAMMER_KIND
} = require('../tools/build-manual.js');

// 1x1 の透明 PNG（テスト用の最小フィクスチャ）。
const TINY_PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=';

function makeTempManualDir() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ddrive-manual-test-'));
  fs.mkdirSync(path.join(dir, 'images'));
  return dir;
}

test('extractBodyInnerHtml: <body> の内側だけを取り出す', () => {
  const html = '<!DOCTYPE html><html><head><title>x</title></head><body>\n<h1>Hi</h1>\n</body></html>';
  assert.equal(extractBodyInnerHtml(html), '<h1>Hi</h1>');
});

test('extractBodyInnerHtml: <body> が無ければ全体をそのまま返す（例外にしない）', () => {
  assert.equal(extractBodyInnerHtml('<p>plain</p>'), '<p>plain</p>');
});

test('scopeCss: すべてのセレクタに scopeClass を前置し、body は scopeClass 自身に置き換える', () => {
  const css = 'body { color: red; } h1, h2 { margin: 0; } .tip { background: green; }';
  const scoped = scopeCss(css, 'sw-manual-page');
  assert.match(scoped, /^\.sw-manual-page \{ color: red; \}/);
  assert.match(scoped, /\.sw-manual-page h1, \.sw-manual-page h2 \{ margin: 0; \}/);
  assert.match(scoped, /\.sw-manual-page \.tip \{ background: green; \}/);
  assert.doesNotMatch(scoped, /(^|[^.\w-])body\s*\{/);
});

test('scopeCss: コメントを除去する', () => {
  const scoped = scopeCss('/* comment */ h1 { color: red; }', 'sw-manual-page');
  assert.doesNotMatch(scoped, /comment/);
});

test('rewriteLinks: 内部ページリンク（xxx.html）を href="#" + data-manual-page に書き換える（緊急修正 2026-09-14）', () => {
  // 以前は href="?page=manual&p=xxx" にしていたが、iframe サンドボックスで既定動作が走ると
  // トップフレームが iframe 自身の URL 基準で解決した絶対 URL（白画面）に遷移してしまうため、
  // ビルド時点では安全な "#" に留め、実際の絶対 URL は実行時に html/Manual.html が
  // window.SpecWebExecUrl から組み立てる（OrderLinkLogic.buildManualUrl）。
  const { html, warnings } = rewriteLinks('<a href="other-page.html">次へ</a>', { knownPages: ['other-page'] });
  assert.match(html, /href="#"/);
  assert.doesNotMatch(html, /href="\?page=/);
  assert.match(html, /data-manual-page="other-page"/);
  assert.equal(warnings.length, 0);
});

test('rewriteLinks: 内部ページリンク + アンカー（xxx.html#anchor）も href="#" にし、data-manual-anchor を付ける', () => {
  const { html, warnings } = rewriteLinks('<a href="other-page.html#section">節へ</a>', { knownPages: ['other-page'] });
  assert.match(html, /href="#"/);
  assert.doesNotMatch(html, /href="\?page=/);
  assert.match(html, /data-manual-page="other-page"/);
  assert.match(html, /data-manual-anchor="section"/);
  assert.equal(warnings.length, 0);
});

test('rewriteLinks: 同一ページ内アンカー（#foo）は href をそのまま保ち、data-manual-anchor を付ける', () => {
  const { html, warnings } = rewriteLinks('<a href="#foo">ジャンプ</a>', {});
  assert.match(html, /href="#foo"/);
  assert.match(html, /data-manual-anchor="foo"/);
  assert.equal(warnings.length, 0);
});

test('rewriteLinks: 外部リンク（http/https）は target="_blank" rel="noopener" を付けて href は変えない', () => {
  const { html, warnings } = rewriteLinks('<a href="https://example.com/x">外部</a>', {});
  assert.match(html, /href="https:\/\/example\.com\/x"/);
  assert.match(html, /target="_blank"/);
  assert.match(html, /rel="noopener"/);
  assert.equal(warnings.length, 0);
});

test('rewriteLinks: knownPages に無いページへのリンクは警告する（未知のページ検出）', () => {
  const warnings = [];
  const { html } = rewriteLinks('<a href="missing-page.html">存在しない</a>', {
    knownPages: ['other-page'],
    onWarning: (message) => warnings.push(message)
  });
  assert.match(html, /data-manual-page="missing-page"/); // 書き換え自体は行う（フォールバックは manualGet 側の責務）
  assert.equal(warnings.length, 1);
  assert.match(warnings[0], /未知のページ/);
});

test('inlineImages: 画像を data URI に変換する', () => {
  const buffer = Buffer.from(TINY_PNG_BASE64, 'base64');
  const { html, warnings } = inlineImages('<img src="images/foo.png" alt="x">', {
    resolveImage: () => buffer
  });
  assert.match(html, /src="data:image\/png;base64,/);
  assert.equal(warnings.length, 0);
});

test('inlineImages: 画像が見つからない場合は警告し、元の src を残す（例外にしない）', () => {
  const warnings = [];
  const { html } = inlineImages('<img src="images/missing.png">', {
    resolveImage: () => null,
    onWarning: (message) => warnings.push(message)
  });
  assert.match(html, /src="images\/missing\.png"/);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0], /画像が見つかりません/);
});

test('inlineImages: 外部 URL の画像はそのまま変更しない', () => {
  const { html, warnings } = inlineImages('<img src="https://example.com/x.png">', { resolveImage: () => null });
  assert.match(html, /src="https:\/\/example\.com\/x\.png"/);
  assert.equal(warnings.length, 0);
});

test('toDataUri: mime type と base64 を連結する', () => {
  const uri = toDataUri(Buffer.from('ab'), 'image/png');
  assert.equal(uri, 'data:image/png;base64,' + Buffer.from('ab').toString('base64'));
});

test('buildManualPageHtml: style を <style> でインライン化し、発注ツール/目次への戻りリンクを先頭に挿入する', () => {
  const result = buildManualPageHtml({
    pageName: 'sample',
    rawHtml: '<html><body><h1>Sample</h1></body></html>',
    scopedCss: '.' + SCOPE_CLASS + ' { color: red; }',
    knownPages: ['sample'],
    resolveImage: () => null,
    onWarning: () => {}
  });
  assert.match(result.html, /^<style>/);
  assert.match(result.html, /class="sw-manual-navbar"/);
  assert.match(result.html, /data-manual-exit="orders"/);
  assert.match(result.html, new RegExp('data-manual-page="' + TOP_PAGE_NAME + '"'));
  assert.match(result.html, new RegExp('class="' + SCOPE_CLASS + '"'));
  assert.equal(result.warnings.length, 0);
});

test('buildManualPageHtml: ナビバーのリンクは href="#"（相対 URL を残さない、緊急修正 2026-09-14）', () => {
  // 実デプロイで href="?"（← 発注ツールへ）/ href="?page=manual&p=..."（マニュアル目次）を
  // クリックしたときに白画面になった不具合の再発防止。生成物には安全な "#" だけを残し、
  // 絶対 URL への差し替えは実行時（html/Manual.html）に行う。
  const result = buildManualPageHtml({
    pageName: 'sample',
    rawHtml: '<html><body><h1>Sample</h1></body></html>',
    scopedCss: '.' + SCOPE_CLASS + ' { color: red; }',
    knownPages: ['sample'],
    resolveImage: () => null,
    onWarning: () => {}
  });
  assert.doesNotMatch(result.html, /href="\?/);
  const navbarMatch = result.html.match(/<div class="sw-manual-navbar">[\s\S]*?<\/div>/);
  assert.ok(navbarMatch, 'ナビバーが見つかる');
  const exitLink = navbarMatch[0].match(/<a[^>]*data-manual-exit="orders"[^>]*>/)[0];
  assert.match(exitLink, /href="#"/);
  const topLinkMatch = navbarMatch[0].match(new RegExp('<a[^>]*data-manual-page="' + TOP_PAGE_NAME + '"[^>]*>'));
  assert.ok(topLinkMatch);
  assert.match(topLinkMatch[0], /href="#"/);
});

test('buildAll（フィクスチャ）: 一連の変換（リンク・画像・CSS スコープ）が一括で実行され、write:false ではディスクに書き出さない', () => {
  const dir = makeTempManualDir();
  try {
    fs.writeFileSync(path.join(dir, 'style.css'), 'body { background: #fff; }');
    fs.writeFileSync(
      path.join(dir, 'Readme.html'),
      '<!DOCTYPE html><html><head><title>T</title></head><body>' +
        '<h1>Top</h1><a href="other.html">other</a><img src="images/pic.png"></body></html>'
    );
    fs.writeFileSync(path.join(dir, 'other.html'), '<html><body><h1>Other</h1><a href="Readme.html">top</a></body></html>');
    fs.writeFileSync(path.join(dir, 'images', 'pic.png'), Buffer.from(TINY_PNG_BASE64, 'base64'));

    const outDir = path.join(dir, 'out-should-not-exist');
    const result = buildAll({ sourceDir: dir, outHtmlDir: outDir, write: false });

    assert.deepEqual(result.pageNames, ['Readme', 'other']); // Readme が先頭
    assert.equal(result.warnings.length, 0);
    assert.match(result.pages.Readme, /data-manual-page="other"/);
    assert.match(result.pages.Readme, /data:image\/png;base64,/);
    assert.match(result.pagesJs, /SPEC_WEB_MANUAL_TOP_PAGE = "Readme"/);
    assert.match(result.pagesJs, /"other"/);
    assert.equal(fs.existsSync(outDir), false, 'write:false のときはディスクに書き出さない');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('buildAll（フィクスチャ）: write:true でディスクへ書き出す', () => {
  const dir = makeTempManualDir();
  try {
    fs.writeFileSync(path.join(dir, 'style.css'), 'body { background: #fff; }');
    fs.writeFileSync(path.join(dir, 'Readme.html'), '<html><body><h1>Top</h1></body></html>');

    const outHtmlDir = path.join(dir, 'out-html');
    const outPagesJsPath = path.join(dir, 'ManualPages.generated.js');
    buildAll({ sourceDir: dir, outHtmlDir, outPagesJsPath, write: true });

    assert.equal(fs.existsSync(path.join(outHtmlDir, 'Readme.html')), true);
    assert.equal(fs.existsSync(outPagesJsPath), true);
    const written = fs.readFileSync(path.join(outHtmlDir, 'Readme.html'), 'utf8');
    assert.match(written, /<h1>Top<\/h1>/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

// ドリフト検出: 実際の docs/DesignerManual から今生成した内容が、コミット済みの
// Tools/SpecWeb/html/manual/*.html・src/ManualPages.js と一致することを確認する。
// docs/DesignerManual を編集した後に `node Tools/SpecWeb/tools/build-manual.js`
// （または push.ps1）を実行し忘れると、このテストが red になって気付ける。
//
// 改行コードは正規化して比較する（Windows でのチェックアウト時に core.autocrlf が
// LF → CRLF へ変換することがあり、Node がその場で書き出す内容（常に \n）とは
// バイト単位では異なりうるため。内容そのものの一致だけを見る）。
function normalizeNewlines_(text) {
  return text.replace(/\r\n/g, '\n');
}

// 2026-09-17（プログラマーマニュアル配信対応）: デザイナーマニュアル単独だった drift 検出を
// buildAllManuals（デザイナー + プログラマー）ベースに更新した。生成物は
// `Tools/SpecWeb/html/manual/<kind>/<page>.html` に kind ごとのサブフォルダへ出力される。
test('ドリフト検出: 実際の docs/DesignerManual・docs/ProgrammerManual からの生成結果はコミット済みファイルと一致する', () => {
  const result = buildAllManuals({ write: false });
  assert.equal(result.warnings.length, 0, '実データで警告が出ないこと: ' + result.warnings.join(', '));
  assert.deepEqual(result.kinds.slice().sort(), [DESIGNER_KIND, PROGRAMMER_KIND].sort());

  for (const kind of result.kinds) {
    const pageNames = result.pageNamesByKind[kind];
    assert.ok(pageNames.length > 0, kind + ' のページが 0 件');
    for (const pageName of pageNames) {
      const committedPath = path.join(OUTPUT_HTML_DIR, kind, pageName + '.html');
      assert.equal(fs.existsSync(committedPath), true, 'コミットされているはずのファイルが無い: ' + committedPath);
      const committed = fs.readFileSync(committedPath, 'utf8');
      assert.equal(
        normalizeNewlines_(result.pagesByKind[kind][pageName]),
        normalizeNewlines_(committed),
        kind + '/' + pageName + '.html が生成結果と一致しません。node Tools/SpecWeb/tools/build-manual.js（または push.ps1）を実行してコミットしてください。'
      );
    }
  }

  const committedPagesJs = fs.readFileSync(OUTPUT_PAGES_JS_PATH, 'utf8');
  assert.equal(
    normalizeNewlines_(result.pagesJs),
    normalizeNewlines_(committedPagesJs),
    'src/ManualPages.js が生成結果と一致しません。node Tools/SpecWeb/tools/build-manual.js を実行してコミットしてください。'
  );
});

// ── 2026-09-17（docs/41 整理項目）: 書き換え漏れの検出 ──
//
// rewriteLinks / inlineImages の正規表現は `href="…"` / `src="…"`（ダブルクォート）にしか
// 掛からないため、`href='…'` や `srcset` は書き換わらないまま**警告も出ずに**通っていた。

test('findUnprocessedLinkTags: 書き換え済みの形（href="#" / 外部リンク / data URI）は警告しない', () => {
  const html =
    '<a href="#" data-manual-page="Readme">目次</a>' +
    '<a href="#section" data-manual-anchor="section">節へ</a>' +
    '<a href="https://example.com/x" target="_blank" rel="noopener">外部</a>' +
    '<a href="mailto:x@example.com">メール</a>' +
    '<a name="anchor-only">アンカーだけ</a>' +
    '<img alt="x" src="data:image/png;base64,AAAA" />' +
    '<img src="https://example.com/a.png">';
  assert.deepEqual(findUnprocessedLinkTags(html), []);
});

test('findUnprocessedLinkTags: シングルクォートの href/src は警告になる', () => {
  const warnings = findUnprocessedLinkTags("<a href='other.html'>次へ</a><img src='images/pic.png'>");
  assert.equal(warnings.length, 2);
  assert.match(warnings[0], /シングルクォート/);
  assert.match(warnings[1], /シングルクォート/);
});

test('findUnprocessedLinkTags: srcset・相対 src・相対 href は警告になる', () => {
  const srcset = findUnprocessedLinkTags('<img src="data:image/png;base64,AAAA" srcset="images/pic@2x.png 2x">');
  assert.equal(srcset.length, 1);
  assert.match(srcset[0], /srcset/);

  const relSrc = findUnprocessedLinkTags('<img src="images/pic.png">');
  assert.equal(relSrc.length, 1);
  assert.match(relSrc[0], /data URI 化されずに残って/);

  const relHref = findUnprocessedLinkTags('<a href="other.html">次へ</a>');
  assert.equal(relHref.length, 1);
  assert.match(relHref[0], /書き換えられずに残って/);
});

test('buildManualPageHtml: 書き換え漏れは warnings と onWarning の両方に載る', () => {
  const warnings = [];
  const result = buildManualPageHtml({
    pageName: 'sample',
    rawHtml: "<html><body><h1>Sample</h1><a href='other.html'>次へ</a></body></html>",
    scopedCss: '.' + SCOPE_CLASS + ' { color: red; }',
    knownPages: ['sample', 'other'],
    resolveImage: () => null,
    onWarning: (message) => warnings.push(message)
  });
  assert.equal(result.warnings.length, 1);
  assert.match(result.warnings[0], /シングルクォート/);
  assert.deepEqual(warnings, result.warnings);
});

// ── 2026-09-17（プログラマーマニュアル配信対応） ──

test('rewriteLinks: 内部ページリンクに data-manual-kind（currentKind、既定 designer）が付く', () => {
  const { html } = rewriteLinks('<a href="other-page.html">次へ</a>', { knownPages: ['other-page'] });
  assert.match(html, /data-manual-kind="designer"/);
  assert.match(html, /data-manual-page="other-page"/);
});

test('rewriteLinks: currentKind を渡すとそのまま反映される', () => {
  const { html } = rewriteLinks('<a href="other-page.html">次へ</a>', {
    knownPages: ['other-page'],
    currentKind: 'programmer'
  });
  assert.match(html, /data-manual-kind="programmer"/);
});

test('rewriteLinks: クロスマニュアルリンク（../DesignerManual/xxx.html）を data-manual-kind="designer" + data-manual-page に書き換える', () => {
  const { html, warnings } = rewriteLinks('<a href="../DesignerManual/asset-browser.html">Asset Browser</a>', {
    currentKind: 'programmer',
    knownPagesByKind: { designer: ['asset-browser'], programmer: [] }
  });
  assert.match(html, /href="#"/);
  assert.match(html, /data-manual-kind="designer"/);
  assert.match(html, /data-manual-page="asset-browser"/);
  assert.equal(warnings.length, 0);
});

test('rewriteLinks: クロスマニュアルリンク + アンカー（../ProgrammerManual/xxx.html#anchor）も書き換える', () => {
  const { html } = rewriteLinks('<a href="../ProgrammerManual/handle.html#dispose">Handle</a>', {
    currentKind: 'designer',
    knownPagesByKind: { designer: [], programmer: ['handle'] }
  });
  assert.match(html, /data-manual-kind="programmer"/);
  assert.match(html, /data-manual-page="handle"/);
  assert.match(html, /data-manual-anchor="dispose"/);
});

test('rewriteLinks: クロスマニュアルリンクで knownPagesByKind に無いページは警告する', () => {
  const warnings = [];
  rewriteLinks('<a href="../DesignerManual/no-such-page.html">存在しない</a>', {
    currentKind: 'programmer',
    knownPagesByKind: { designer: ['asset-browser'], programmer: [] },
    onWarning: (message) => warnings.push(message)
  });
  assert.equal(warnings.length, 1);
  assert.match(warnings[0], /未知のページ/);
});

test('rewriteLinks: 未知のディレクトリへの相互リンク風パス（../OtherManual/x.html）は未対応形式として警告しそのまま残す', () => {
  const warnings = [];
  const { html } = rewriteLinks('<a href="../OtherManual/x.html">?</a>', {
    onWarning: (message) => warnings.push(message)
  });
  assert.match(html, /href="\.\.\/OtherManual\/x\.html"/);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0], /未対応の形式/);
});

test('resolveCssImports: ローカル相対パスの @import をその内容で置き換える', () => {
  const { css, warnings } = resolveCssImports('@import url("../DesignerManual/style.css");\npre { color: red; }', {
    resolveImport: (url) => {
      assert.equal(url, '../DesignerManual/style.css');
      return 'body { color: blue; }';
    }
  });
  assert.doesNotMatch(css, /@import/);
  assert.match(css, /body \{ color: blue; \}/);
  assert.match(css, /pre \{ color: red; \}/);
  assert.equal(warnings.length, 0);
});

test('resolveCssImports: 外部 URL の @import は変更しない', () => {
  const { css, warnings } = resolveCssImports('@import url("https://example.com/x.css");', { resolveImport: () => null });
  assert.match(css, /@import url\("https:\/\/example\.com\/x\.css"\);/);
  assert.equal(warnings.length, 0);
});

test('resolveCssImports: 解決できない相対パスは警告してそのまま残す（例外にしない）', () => {
  const warnings = [];
  const { css } = resolveCssImports('@import url("./missing.css");', {
    resolveImport: () => null,
    onWarning: (message) => warnings.push(message)
  });
  assert.match(css, /@import url\("\.\/missing\.css"\);/);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0], /@import の CSS が見つかりません/);
});

function makeTempManualDirNamed_(name) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ddrive-manual-test-' + name + '-'));
  fs.mkdirSync(path.join(dir, 'images'));
  return dir;
}

test('buildAllManuals（フィクスチャ）: 2 つの kind を kind ごとのサブフォルダへ生成し、相互リンクを解決する', () => {
  const designerDir = makeTempManualDirNamed_('designer');
  const programmerDir = makeTempManualDirNamed_('programmer');
  try {
    fs.writeFileSync(path.join(designerDir, 'style.css'), 'body { background: #fff; }');
    fs.writeFileSync(
      path.join(designerDir, 'Readme.html'),
      '<html><head><title>D</title></head><body><h1>Designer Top</h1></body></html>'
    );

    // docs/ProgrammerManual/style.css と同じ形（デザイナー側の CSS を @import で継承する）。
    fs.writeFileSync(
      path.join(programmerDir, 'style.css'),
      '@import url("' + pathToRelativeImport(programmerDir, designerDir) + '");\npre { color: red; }'
    );
    // 相互リンクの解決先ディレクトリ名は実プロジェクトと同じ「DesignerManual」固定
    // （MANUAL_DIR_TO_KIND の既定値、実際の一時ディレクトリ名とは無関係）。
    fs.writeFileSync(
      path.join(programmerDir, 'Readme.html'),
      '<html><head><title>P</title></head><body><h1>Programmer Top</h1>' +
        '<a href="../DesignerManual/Readme.html">デザイナーマニュアル</a>' +
        '</body></html>'
    );

    const outBase = path.join(designerDir, 'out');
    const outPagesJsPath = path.join(designerDir, 'ManualPages.generated.js');
    const result = buildAllManuals({
      kinds: [
        { kind: 'designer', sourceDir: designerDir },
        { kind: 'programmer', sourceDir: programmerDir }
      ],
      outHtmlBaseDir: outBase,
      outPagesJsPath,
      write: true
    });

    assert.equal(result.warnings.length, 0, result.warnings.join(', '));
    assert.equal(fs.existsSync(path.join(outBase, 'designer', 'Readme.html')), true);
    assert.equal(fs.existsSync(path.join(outBase, 'programmer', 'Readme.html')), true);

    const programmerReadme = result.pagesByKind.programmer.Readme;
    assert.match(programmerReadme, /data-manual-kind="designer"/);
    assert.match(programmerReadme, /data-manual-page="Readme"/);

    assert.match(result.pagesJs, /SPEC_WEB_MANUAL_KINDS = \["designer","programmer"\]/);
    assert.match(result.pagesJs, /SPEC_WEB_MANUAL_DEFAULT_KIND = "designer"/);
    const writtenPagesJs = fs.readFileSync(outPagesJsPath, 'utf8');
    assert.equal(writtenPagesJs, result.pagesJs);
  } finally {
    fs.rmSync(designerDir, { recursive: true, force: true });
    fs.rmSync(programmerDir, { recursive: true, force: true });
  }
});

function pathToRelativeImport(fromDir, toDir) {
  const rel = path.relative(fromDir, toDir).split(path.sep).join('/');
  return rel + '/style.css';
}
