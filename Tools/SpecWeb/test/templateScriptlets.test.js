'use strict';
// GAS の HtmlService テンプレート(html/Index.html)は HTML コメントの中でも <? ... ?> を評価する。
// コメント内に記法そのもの(空の印字スクリプトレットや説明用の断片)を書くと、ページ全体が
// 「SyntaxError: Unexpected token ';'」(evaluate() を呼ぶ src/Code.js の行番号で報告される)で落ちる。
// 2026-09-20 に html/Index.html のコメント内の説明文で実際に発生し、デプロイ①が開けなくなった。
// テンプレートを評価するテスト基盤が無いため、html/ 配下を機械的に検査して再発を防ぐ。
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

function listHtml(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) listHtml(p, out); else if (e.name.endsWith('.html')) out.push(p);
  }
  return out;
}

function lineOf(text, index) {
  return text.slice(0, index).split('\n').length;
}

test('html/ の HTML コメント内にテンプレート記法(<?)が無い', () => {
  const root = path.join(__dirname, '..', 'html');
  const offenders = [];
  for (const file of listHtml(root, [])) {
    const text = fs.readFileSync(file, 'utf8');
    const re = /<!--[\s\S]*?-->/g;
    let m;
    while ((m = re.exec(text)) !== null) {
      if (m[0].includes('<?')) {
        offenders.push(`${path.relative(root, file)}:${lineOf(text, m.index)}`);
      }
    }
  }
  assert.deepStrictEqual(offenders, [], 'HTML コメント内でも <? は GAS テンプレートとして評価されます: ' + offenders.join(', '));
});

test('html/ に空のテンプレートスクリプトレット(<?!= ?> / <? ?>)が無い', () => {
  const root = path.join(__dirname, '..', 'html');
  const offenders = [];
  for (const file of listHtml(root, [])) {
    const text = fs.readFileSync(file, 'utf8');
    const re = /<\?(!?=)?\s*\?>/g;
    let m;
    while ((m = re.exec(text)) !== null) {
      offenders.push(`${path.relative(root, file)}:${lineOf(text, m.index)} ${m[0]}`);
    }
  }
  assert.deepStrictEqual(offenders, [], '空のスクリプトレットはコメント内でも書けません: ' + offenders.join(', '));
});
