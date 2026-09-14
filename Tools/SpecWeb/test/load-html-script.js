'use strict';

/**
 * html/*.html の <script>...</script> の中身だけを Node の vm で実行するローダー。
 * load-gas.js（GAS 側）と同じ考え方の、クライアント側 html 用の版。
 *
 * DOM に依存しない純粋関数（html/TuningGrid.html）だけを対象にする。
 * DOM を触る画面本体（html/Tuning.html）はここでは検証しない
 * （document 等の重いフェイクを作らずに済ませるため、TuningGrid.html 側を
 * 「DOM に触らない」設計にして純粋関数だけをテスト可能にしている）。
 */

const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const HTML_ROOT = path.join(__dirname, '..', 'html');

/**
 * @param {string} fileName 例: "TuningGrid" ("html/TuningGrid.html" を読む)
 * @return {vm.Context} <script> の中身を実行した後のコンテキスト
 */
function loadHtmlScript(fileName) {
  const full = path.join(HTML_ROOT, fileName + '.html');
  const html = fs.readFileSync(full, 'utf8');
  const match = html.match(/<script>([\s\S]*?)<\/script>/);
  if (!match) {
    throw new Error('<script> タグが見つかりません: ' + full);
  }
  const code = match[1];
  const sandbox = { console: console };
  const context = vm.createContext(sandbox);
  vm.runInContext(code, context, { filename: full });
  return context;
}

module.exports = { loadHtmlScript };
