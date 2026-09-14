'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');

// O-16 AC（追加要望「Markdown を打つのが面倒なので、ボタンでカーソル位置に各種書式を挿入できるように」）:
// html/MarkdownToolbar.html の純粋関数 applyAction(text, selStart, selEnd, action, options)。
// (docs/32_spec_web.md「実装メモ（O-16）」§書式ツールバー)

function logic() {
  return loadHtmlScript('MarkdownToolbar').window.MarkdownToolbar;
}

// ---- インライン装飾: 太字・コード ----

test('bold: 選択範囲があれば ** で囲み、選択部分を選択状態にする', () => {
  const r = logic().applyAction('foo bar baz', 4, 7, 'bold');
  assert.equal(r.text, 'foo **bar** baz');
  assert.equal(r.text.slice(r.selStart, r.selEnd), 'bar');
});

test('bold: 選択が無ければ **太字** を挿入し、"太字" を選択状態にする', () => {
  const r = logic().applyAction('foo ', 4, 4, 'bold');
  assert.equal(r.text, 'foo **太字**');
  assert.equal(r.text.slice(r.selStart, r.selEnd), '太字');
});

test('code: 選択範囲があれば ` で囲む', () => {
  const r = logic().applyAction('見て var x = 1 ね', 3, 12, 'code');
  assert.equal(r.text, '見て `var x = 1` ね');
});

test('code: 空文字のテキストに対しても例外にならない（選択も無し）', () => {
  const r = logic().applyAction('', 0, 0, 'code');
  assert.equal(r.text, '`コード`');
  assert.equal(r.text.slice(r.selStart, r.selEnd), 'コード');
});

// ---- リンク・画像 ----

test('link: 選択範囲があれば [選択](https://) にし、URL 部分（https://）を選択状態にする', () => {
  const r = logic().applyAction('参考: 動画', 4, 6, 'link');
  assert.equal(r.text, '参考: [動画](https://)');
  assert.equal(r.text.slice(r.selStart, r.selEnd), 'https://');
});

test('link: 選択が無ければ [リンクの文字](https://) を挿入し、ラベル部分を選択状態にする', () => {
  const r = logic().applyAction('参考: ', 4, 4, 'link');
  assert.equal(r.text, '参考: [リンクの文字](https://)');
  assert.equal(r.text.slice(r.selStart, r.selEnd), 'リンクの文字');
});

test('image: 選択範囲があれば ![選択](https://) にし、URL 部分を選択状態にする', () => {
  const r = logic().applyAction('図: サンプル', 3, 8, 'image');
  assert.equal(r.text, '図: ![サンプル](https://)');
  assert.equal(r.text.slice(r.selStart, r.selEnd), 'https://');
});

test('image: 選択が無ければ ![画像の説明](https://) を挿入し、alt 部分を選択状態にする', () => {
  const r = logic().applyAction('', 0, 0, 'image');
  assert.equal(r.text, '![画像の説明](https://)');
  assert.equal(r.text.slice(r.selStart, r.selEnd), '画像の説明');
});

// ---- 行頭系のトグル: 見出し・引用・箇条書き・番号付き・チェックリスト ----

test('h2: カーソル行（選択なし）の先頭に "## " を付ける', () => {
  const r = logic().applyAction('見出し候補', 2, 2, 'h2');
  assert.equal(r.text, '## 見出し候補');
});

test('h2: すでに "## " が付いている行はトグルで外れる', () => {
  const r = logic().applyAction('## 見出し候補', 2, 2, 'h2');
  assert.equal(r.text, '見出し候補');
});

test('h3 と h2 は互いに独立している（h3 の行に h2 を適用しても二重にならず、h3 のまま h2 が追加される）', () => {
  const r = logic().applyAction('### 見出し', 1, 1, 'h2');
  assert.equal(r.text, '## ### 見出し');
});

test('quote: 複数行選択でトグル ON にすると、まだ付いていない行だけに "> " が付く', () => {
  const text = '1行目\n> 2行目\n3行目';
  const r = logic().applyAction(text, 0, text.length, 'quote');
  assert.equal(r.text, '> 1行目\n> 2行目\n> 3行目');
});

test('quote: 全行に付いている状態でトグルすると全行から外れる', () => {
  const text = '> 1行目\n> 2行目';
  const r = logic().applyAction(text, 0, text.length, 'quote');
  assert.equal(r.text, '1行目\n2行目');
});

test('ul: 箇条書きのトグル ON/OFF。チェックリスト行は箇条書きとして誤検出しない', () => {
  const onceOn = logic().applyAction('項目A\n項目B', 0, 6, 'ul');
  assert.equal(onceOn.text, '- 項目A\n- 項目B');
  const toggledOff = logic().applyAction(onceOn.text, 0, onceOn.text.length, 'ul');
  assert.equal(toggledOff.text, '項目A\n項目B');

  const withChecklist = '- [ ] 済み\n未処理';
  const r = logic().applyAction(withChecklist, 0, withChecklist.length, 'ul');
  // チェックリスト行は "ul" の対象外として扱われ、そのまま残る（誤って "- " が重複しない）。
  assert.equal(r.text, '- [ ] 済み\n- 未処理');
});

test('ol: 番号付きリストは選択範囲内の各行に連番を振る', () => {
  const text = '一番目\n二番目\n三番目';
  const r = logic().applyAction(text, 0, text.length, 'ol');
  assert.equal(r.text, '1. 一番目\n2. 二番目\n3. 三番目');
});

test('ol: すでに番号が付いている行はトグルで外れる', () => {
  const text = '1. 一番目\n2. 二番目';
  const r = logic().applyAction(text, 0, text.length, 'ol');
  assert.equal(r.text, '一番目\n二番目');
});

test('checklist: "- [ ] " のトグル ON/OFF（"- [x] " も既存として認識して外せる）', () => {
  const on = logic().applyAction('買うもの', 0, 4, 'checklist');
  assert.equal(on.text, '- [ ] 買うもの');
  const off = logic().applyAction('- [x] 済んだもの', 0, 10, 'checklist');
  assert.equal(off.text, '済んだもの');
});

test('行頭系トグル: 選択が複数行にまたがる場合、行の一部だけを選択していても行全体が対象になる', () => {
  const text = 'AAAA\nBBBB\nCCCC';
  // "AAA" の途中(index=2) 〜 "BBB" の途中(index=8) の選択でも、1〜2行目全体が対象。
  const r = logic().applyAction(text, 2, 8, 'ul');
  assert.equal(r.text, '- AAAA\n- BBBB\nCCCC');
});

test('行頭系トグル: 適用後の選択範囲は書き換えたブロック全体になる', () => {
  const text = 'foo';
  const r = logic().applyAction(text, 1, 1, 'ul');
  assert.equal(r.text.slice(r.selStart, r.selEnd), r.text);
});

// ---- ブロック挿入: 区切り線・参考リンク・納品物チェックリスト ----

test('hr: カーソルが行末以外にあれば改行してから "---" を挿入する', () => {
  const r = logic().applyAction('途中まで書いた文章', 5, 5, 'hr');
  assert.equal(r.text, '途中まで書\n---\nいた文章');
});

test('hr: 空文字（先頭）に挿入しても余分な改行は入らない', () => {
  const r = logic().applyAction('', 0, 0, 'hr');
  assert.equal(r.text, '---\n');
});

test('referenceLinkBlock: 「参考リンク」の定型ブロックを挿入し、ラベル部分を選択状態にする', () => {
  const r = logic().applyAction('既存のメモ', 5, 5, 'referenceLinkBlock');
  assert.match(r.text, /既存のメモ\n\n### 参考リンク\n- \[リンクの文字\]\(https:\/\/\)\n/);
  assert.equal(r.text.slice(r.selStart, r.selEnd), 'リンクの文字');
});

test('deliverableChecklist: 発注のファイル形式・ファイル名を差し込み、カーソルは「枚数: 」の直後になる', () => {
  const r = logic().applyAction('', 0, 0, 'deliverableChecklist', { fileFormat: '.wav', fileName: 'SE_Slash.wav' });
  assert.match(r.text, /### 納品物チェックリスト/);
  assert.match(r.text, /- \[ \] ファイル形式: \.wav/);
  assert.match(r.text, /- \[ \] ファイル名: SE_Slash\.wav/);
  assert.equal(r.selStart, r.selEnd);
  assert.equal(r.text.slice(0, r.selStart).endsWith('枚数: '), true);
});

test('deliverableChecklist: fileFormat/fileName 未設定なら「（未設定）」で埋める', () => {
  const r = logic().applyAction('', 0, 0, 'deliverableChecklist', {});
  assert.match(r.text, /ファイル形式: （未設定）/);
  assert.match(r.text, /ファイル名: （未設定）/);
});

// ---- 未知の action・境界値 ----

test('未知の action は何もせず、そのままのテキスト・選択範囲を返す', () => {
  const r = logic().applyAction('foo', 1, 2, 'not-a-real-action');
  assert.equal(r.text, 'foo');
  assert.equal(r.selStart, 1);
  assert.equal(r.selEnd, 2);
});

test('selStart/selEnd が範囲外・逆転していても例外にならない（クランプされる）', () => {
  assert.doesNotThrow(() => logic().applyAction('foo', -5, 999, 'bold'));
  assert.doesNotThrow(() => logic().applyAction('foo', 5, 1, 'bold'));
  assert.doesNotThrow(() => logic().applyAction(null, 0, 0, 'bold'));
  assert.doesNotThrow(() => logic().applyAction(undefined, undefined, undefined, 'h2'));
});

test('ACTIONS: ボタン一覧（id・label）が公開されている', () => {
  const ACTIONS = logic().ACTIONS;
  assert.ok(Array.isArray(ACTIONS));
  const ids = ACTIONS.map((a) => a.id);
  ['h2', 'h3', 'bold', 'ul', 'ol', 'checklist', 'link', 'image', 'hr', 'quote', 'code', 'referenceLinkBlock', 'deliverableChecklist'].forEach((id) => {
    assert.ok(ids.indexOf(id) !== -1, id + ' が ACTIONS に含まれる');
  });
});
