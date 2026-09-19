'use strict';
// GAS の HtmlService テンプレートは HTML コメントの中でも <? ... ?> を評価する。空の印字スクリプトレット
// (<?!= ?>) や空のスクリプトレット(<? ?>)があるとページ全体が「SyntaxError: Unexpected token ';'」で
// 落ちる(2026-09-20 に html/Index.html のコメント内の説明文で実際に発生し、デプロイ①が開けなくなった)。
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

test('html/ に空のテンプレートスクリプトレット(<?!= ?> / <? ?>)が無い', () => {
  const root = path.join(__dirname, '..', 'html');
  const offenders = [];
  for (const file of listHtml(root, [])) {
    const text = fs.readFileSync(file, 'utf8');
    const re = /<\?(!?=)?\s*\?>/g;
    let m;
    while ((m = re.exec(text)) !== null) {
      const line = text.slice(0, m.index).split('\n').length;
      offenders.push(`${path.relative(root, file)}:${line} ${m[0]}`);
    }
  }
  assert.deepStrictEqual(offenders, [], '空のスクリプトレットはコメント内でも書けません: ' + offenders.join(', '));
});
