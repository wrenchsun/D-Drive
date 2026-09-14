'use strict';

/**
 * html/*.html に書かれた <script>...</script> の中身だけを取り出し、Node の vm で評価する
 * ローダー（test/load-gas.js の「本物のコードをそのまま検証する」方針をクライアント側にも適用したもの）。
 *
 * SPA の画面 html（Assets.html/Tuning.html 等）は DOM 操作を含むため Node では直接動かせないが、
 * 「行のレンダリング・並べ替え・絞り込み関数」「貼り付け解析・セル移動」のような純粋関数だけを
 * 1 つの `<script>` に分けておけば（TuningGrid.html/AssetsLogic.html の規約）、ここで vm に
 * 読み込んで直接テストできる（docs/32_spec_web.md §8 W-4/W-7 の
 * 「HTML を生成するロジックを純粋関数に分けて単体テスト」の実装）。
 *
 * 公開の仕方は 2 通りどちらでもよい: トップレベルの `var Foo = {...}`（vm コンテキスト自身が
 * グローバルオブジェクトになるため `context.Foo` で読める）でも、`window.Foo = {...}`
 * （`context.window.Foo` で読める）でもよい。デフォルトの sandbox には空の `window` オブジェクトを
 * 用意してあるので、`typeof window !== 'undefined'` を見て両方に生やすファイルでも動く。
 */

const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const HTML_ROOT = path.join(__dirname, '..', 'html');
const SCRIPT_TAG_PATTERN = /<script>([\s\S]*?)<\/script>/g;

function extractScripts_(fileBaseName) {
  const filePath = path.join(HTML_ROOT, fileBaseName + '.html');
  const html = fs.readFileSync(filePath, 'utf8');

  const scripts = [];
  let match;
  SCRIPT_TAG_PATTERN.lastIndex = 0;
  while ((match = SCRIPT_TAG_PATTERN.exec(html))) {
    scripts.push(match[1]);
  }
  if (scripts.length === 0) {
    throw new Error('<script> タグが見つかりません: ' + filePath);
  }
  return { filePath, scripts };
}

/**
 * @param {string} fileBaseName 拡張子無しのファイル名（例: "TuningGrid" → html/TuningGrid.html）
 * @param {object} [globals] sandbox に追加で注入するグローバル（未使用時は空でよい）
 * @return {vm.Context} 評価後のコンテキスト（トップレベルの var、または sandbox.window 経由で
 *   公開された名前空間を読める）
 */
// 緊急修正（2026-09-14）: html/UiFeedback.html のトースト自動消去が setTimeout/clearTimeout を
// 使うため、vm.createContext には無い（V8 の生コンテキストには Node/ブラウザの Timer API が無い）
// これらを既定で注入する（console/window と同じ扱い）。
const DEFAULT_SANDBOX_EXTRAS = { setTimeout: setTimeout, clearTimeout: clearTimeout };

function loadHtmlScript(fileBaseName, globals) {
  const { filePath, scripts } = extractScripts_(fileBaseName);
  const sandbox = Object.assign({ console: console, window: {} }, DEFAULT_SANDBOX_EXTRAS, globals || {});
  const context = vm.createContext(sandbox);
  for (const code of scripts) {
    vm.runInContext(code, context, { filename: filePath });
  }
  return context;
}

/**
 * 複数の html ファイルの <script> を「同じ vm コンテキスト」に順番に読み込む。
 * Assets.html が window.AssetsLogic（AssetsLogic.html で定義）に依存しているような、
 * 実際の Index.html の include 順序（App → AssetsLogic → Assets）を再現したいときに使う。
 * @param {string[]} fileBaseNames
 * @param {object} [globals]
 */
function loadHtmlScripts(fileBaseNames, globals) {
  const sandbox = Object.assign({ console: console, window: {} }, DEFAULT_SANDBOX_EXTRAS, globals || {});
  const context = vm.createContext(sandbox);
  fileBaseNames.forEach(function (fileBaseName) {
    const { filePath, scripts } = extractScripts_(fileBaseName);
    scripts.forEach(function (code) {
      vm.runInContext(code, context, { filename: filePath });
    });
  });
  return context;
}

module.exports = { loadHtmlScript, loadHtmlScripts };
