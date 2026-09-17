'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// 2026-09-17 追補（docs/41 P1-3 の修正に追随）: `issueApiToken` 等の**公開名**の運用関数は
// admin セッション必須になった（`specWebAssertAdminSession_`）。ここでのトークン発行・移行・
// ユーザー登録は「テストの前提を組み立てる」ためのものなので、内部実装（末尾 `_`）を直接呼ぶ。
// 公開名の関数が admin セッション無しで必ず失敗することは test/globals.test.js が固定している。

// W-1 AC: 空の doGet が 2 つの URL（＝ 2 つのデプロイ）で応答する。
// 実際には 2 つの物理 URL は用意できないが、doGet/doPost は 1 つの関数で
// 両方のデプロイを兼ねる設計（Code.js）なので、
// 「token 無し（① 相当）」「token 有り（② 相当）」の両方の呼び出しが
// 例外を投げずに応答することを確認する。
// (docs/32_spec_web.md §8 W-1)
//
// 追補（2026-09-14）: token は POST（doPost）の本文でのみ受け付ける（§7）。
// token 付きの呼び出しは doGet ではなく doPost で検証し、「doGet に token を
// 付けると（有効/無効に関わらず）拒否される」ことも別途確認する。

function usersFixture(list) {
  const items = {};
  for (const u of list) {
    items[u.email.toLowerCase()] = { id: u.email.toLowerCase(), email: u.email, displayName: u.displayName, role: u.role, revision: 1 };
  }
  return { 'users.json': JSON.stringify({ items }) };
}

test('doGet: api パラメータが無ければ① UI 経路（許可リスト済みなら SPA テンプレートを返す）', () => {
  const ctx = loadGas({
    activeUserEmail: 'member@example.com',
    driveFiles: usersFixture([{ email: 'member@example.com', displayName: 'メンバー', role: 'viewer' }])
  });
  const output = ctx.doGet({ parameter: {} });
  assert.equal(output.getContent(), '<!-- rendered:html/Index -->');
});

test('doGet: api パラメータが無く許可リスト外なら「メンバーのみ利用できます」ページを返す（例外にしない）', () => {
  const ctx = loadGas({ activeUserEmail: 'outsider@example.com' });
  const output = ctx.doGet({ parameter: {} });
  assert.match(output.getContent(), /メンバーのみ利用できます/);
});

// O-14 AC: 拒否画面に本人のメールアドレスを表示し、管理者へ伝えやすくする。
test('doGet: 許可リスト外のページに「ログイン中のアカウント」とメールアドレスが表示される', () => {
  const ctx = loadGas({ activeUserEmail: 'outsider@example.com' });
  const output = ctx.doGet({ parameter: {} });
  assert.match(output.getContent(), /ログイン中のアカウント/);
  assert.match(output.getContent(), /outsider@example\.com/);
  assert.match(output.getContent(), /このメールアドレスを管理者に伝えてください/);
});

test('doPost: ?api=1（② 相当、token を POST 本文で送る）は JSON で応答する', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('read');
  const output = ctx.doPost({ parameter: { api: '1', name: 'ping', token: token } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
  assert.equal(body.pong, true);
});

// 2026-09-14 追補: token は GET（doGet）のクエリパラメータでは受け付けない
// （token が GAS の実行ログ・中継プロキシ・ブラウザ履歴に残る経路を塞ぐため。§7）。
// 有効な token であっても、GET に付いているだけで即時に拒否される。
test('doGet: token 付きの ?api=1 は（有効な token であっても）拒否される。POST でのみ受け付ける', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('read');
  const output = ctx.doGet({ parameter: { api: '1', name: 'ping', token: token } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 400);
});

test('doGet: ?api=1 で token が無く未ログインの呼び出し（② への無認証アクセス）は拒否される', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  const output = ctx.doGet({ parameter: { api: '1', name: 'ping' } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 401);
});

test('doPost: token が間違っていれば拒否される', () => {
  const ctx = loadGas();
  ctx.specWebIssueApiToken_('read');
  const output = ctx.doPost({ parameter: { api: '1', name: 'ping', token: 'wrong' } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 401);
});

test('doPost: 未登録の API 名は 404 相当で拒否される', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('read');
  const output = ctx.doPost({ parameter: { api: '1', name: 'no-such-api', token: token } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 404);
});

test('doPost: doGet と同じルーティング関数を使い、token 付きで API を呼べる', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('write');
  const output = ctx.doPost({ parameter: { api: '1', name: 'ping', token: token } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
});

test('handleApiRequest_: RevisionConflictError を投げる登録 API は 409 相当に変換される', () => {
  const ctx = loadGas();
  // read トークンを使う(write トークンは W-12 の kind 許可リストで choices/assetState/tuningUsage
  // 以外の API を呼べないため、この動的登録の __test_conflict は通らない。この test は
  // 「RevisionConflictError → 409」への変換ロジック自体の確認であり、W-12 のゲートとは無関係)。
  const token = ctx.specWebIssueApiToken_('read');
  ctx.registerApi_('__test_conflict', function () {
    throw new ctx.RevisionConflictError('revision が一致しません', 3);
  });
  const output = ctx.doPost({ parameter: { api: '1', name: '__test_conflict', token: token } });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 409);
  assert.equal(body.currentRevision, 3);
});

test('e が省略されても（GAS の実引数省略パターンを想定して）例外にならない', () => {
  const ctx = loadGas({ activeUserEmail: '' });
  assert.doesNotThrow(() => ctx.doGet());
  assert.doesNotThrow(() => ctx.doPost());
});
