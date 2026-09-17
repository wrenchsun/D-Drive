'use strict';

// 2026-09-17 セキュリティ修正の回帰テスト（docs/41_phase6_review_2026-09-17.md P1-3 / P1-5）。
//
// GAS では「末尾 `_` の無いグローバル関数」はすべて `google.script.run.<名前>()` で
// クライアントから直接呼べる。`?api=1` の ApiRegistry に登録していないことは防御にならず、
// さらに許可リスト外のユーザーに返す拒否ページも HtmlService 出力なので google.script
// ブリッジが載る。つまり **公開名のままの関数は「誰でも叩ける入口」** として扱う必要がある。
//
// このファイルは 2 つを機械的に固定する:
//   1. 公開名のままの関数は下の許可リストだけ（新しく足したら必ずここに意識して足る）
//   2. 許可リストのうち「Apps Script エディタから手で実行する運用関数」は、
//      admin セッションでなければ必ず失敗する
//
// 1 が無いと、内部ヘルパーを `_` 無しで書いた瞬間に同じ穴がまた開く。

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

/** loadGas が vm に注ぎ込む GAS 側のグローバル（src のコードではない）。 */
const SANDBOX_KEYS = new Set([
  'console',
  'DriveApp',
  'PropertiesService',
  'Session',
  'LockService',
  'ContentService',
  'HtmlService',
  'Utilities',
  'Logger',
  'ScriptApp',
  '__fakes'
]);

/** Web アプリの入口。GAS の仕様でこの名前でなければならない。 */
const ENTRY_POINTS = ['doGet', 'doPost'];

/** 人向け SPA が `google.script.run` から呼ぶ唯一の入口（中で authenticateSession_ する）。 */
const UI_BRIDGE = ['specWebUiCall'];

/**
 * Apps Script エディタから管理者が手で実行する運用関数（docs/32 §5.2・§7）。
 * 公開名のまま残すが、**先頭で specWebAssertAdminSession_() を呼ぶことが必須**。
 */
const ADMIN_OPERATIONS = [
  'issueApiToken',
  'revokeApiToken',
  'revokeAllApiTokens',
  'rotateApiToken',
  'countApiTokens',
  'upsertSpecWebUser',
  'removeSpecWebUser',
  'listSpecWebUsers',
  'migrateLegacyOrdersToNewSchema'
];

/** エラー型（`throw` 用のコンストラクタ。クライアントから呼んでも副作用が無い）。 */
const ERROR_CLASSES = [
  'RevisionConflictError',
  'SpecWebForbiddenError',
  'SpecWebNotFoundError',
  'SpecWebRateLimitError',
  'SpecWebValidationError'
];

const ALLOWED_PUBLIC = new Set([...ENTRY_POINTS, ...UI_BRIDGE, ...ADMIN_OPERATIONS, ...ERROR_CLASSES]);

function publicFunctionNames(ctx) {
  return Object.keys(ctx)
    .filter((name) => !SANDBOX_KEYS.has(name))
    .filter((name) => typeof ctx[name] === 'function')
    .filter((name) => !name.endsWith('_'))
    .sort();
}

function usersFixture(list) {
  const items = {};
  for (const u of list) {
    items[u.email.toLowerCase()] = {
      id: u.email.toLowerCase(),
      email: u.email,
      displayName: u.displayName,
      role: u.role,
      revision: 1
    };
  }
  return { 'users.json': JSON.stringify({ items }) };
}

test('公開名のままのグローバル関数は許可リストだけ（残りは末尾 _ で private にする）', () => {
  const ctx = loadGas({});
  const unexpected = publicFunctionNames(ctx).filter((name) => !ALLOWED_PUBLIC.has(name));

  assert.deepEqual(
    unexpected,
    [],
    '末尾 _ の無いグローバル関数は google.script.run から誰でも呼べます。\n' +
      '内部ヘルパーなら名前の末尾に _ を付けてください。\n' +
      '意図して公開するなら、先頭で specWebAssertAdminSession_() を呼んだうえで\n' +
      'このテストの ADMIN_OPERATIONS に追加してください。\n' +
      '想定外に公開されている関数: ' + unexpected.join(', ')
  );
});

test('許可リストの運用関数はすべて実在する（改名・削除に気づけるように）', () => {
  const ctx = loadGas({});
  for (const name of [...ENTRY_POINTS, ...UI_BRIDGE, ...ADMIN_OPERATIONS]) {
    assert.equal(typeof ctx[name], 'function', name + ' が見つかりません（改名したら許可リストも直す）');
  }
});

test('運用関数は admin セッションでなければ失敗する（viewer / 許可リスト外の両方）', () => {
  // 引数は「admin チェックより先に別の検証で落ちる」ことがないよう、正しい形の値を渡す。
  const args = {
    issueApiToken: ['read'],
    revokeApiToken: ['read', 'dummy-token'],
    revokeAllApiTokens: ['read'],
    rotateApiToken: ['read'],
    countApiTokens: ['read'],
    upsertSpecWebUser: ['someone@example.com', '誰か', 'viewer'],
    removeSpecWebUser: ['someone@example.com'],
    listSpecWebUsers: [],
    migrateLegacyOrdersToNewSchema: []
  };

  const cases = [
    {
      label: 'viewer ロールのメンバー',
      email: 'viewer@example.com',
      users: usersFixture([
        { email: 'admin@example.com', displayName: '管理者', role: 'admin' },
        { email: 'viewer@example.com', displayName: '閲覧者', role: 'viewer' }
      ])
    },
    {
      label: '許可リスト外のアカウント',
      email: 'outsider@example.com',
      users: usersFixture([{ email: 'admin@example.com', displayName: '管理者', role: 'admin' }])
    }
  ];

  for (const c of cases) {
    for (const name of ADMIN_OPERATIONS) {
      const ctx = loadGas({ activeUserEmail: c.email, driveFiles: c.users });
      assert.throws(
        () => ctx[name](...args[name]),
        /admin/,
        c.label + ' が ' + name + ' を実行できてしまいます（specWebAssertAdminSession_ の呼び忘れ）'
      );
    }
  }
});

test('upsertSpecWebUser は admin が 1 人も居ないときだけブートストラップを許す', () => {
  // admin が居ない状態（初期セットアップ）では、許可リストの誰でも最初の admin を作れる。
  const ctx = loadGas({
    activeUserEmail: 'first@example.com',
    driveFiles: usersFixture([])
  });
  ctx.upsertSpecWebUser('first@example.com', '最初の管理者', 'admin');
  const after = JSON.parse(ctx.__fakes.drive.files.get('users.json'));
  assert.equal(after.items['first@example.com'].role, 'admin');

  // admin が居る状態では、もう素通りしない。
  const ctx2 = loadGas({
    activeUserEmail: 'viewer@example.com',
    driveFiles: usersFixture([
      { email: 'admin@example.com', displayName: '管理者', role: 'admin' },
      { email: 'viewer@example.com', displayName: '閲覧者', role: 'viewer' }
    ])
  });
  assert.throws(() => ctx2.upsertSpecWebUser('viewer@example.com', '自己昇格', 'admin'));
});

// ── P1-5: `<script>` へ埋め込む JSON の無害化 ──

test('specWebJsonForScript_ は </script> を作れない（< を \\u003c にする）', () => {
  const ctx = loadGas({});
  const payload = { openId: '</script><script>alert(1)</script>' };
  const out = ctx.specWebJsonForScript_(payload);

  assert.ok(!out.includes('<'), '< が残っていると script 要素を閉じられます: ' + out);
  assert.ok(!/<\/script/i.test(out));
  // JSON としての意味は変わらない（< は JSON のエスケープなので値は同じ）。
  assert.deepEqual(JSON.parse(out), payload);
});

test('specWebJsonForScript_ は JS の行終端子（U+2028 / U+2029）も退避する', () => {
  const ctx = loadGas({});
  // 2026-09-17 追補: 生の U+2028 / U+2029 をソースに直接書かず、必ず \u2028 の
  // エスケープで書く（見えない文字になるうえ、正規表現リテラルの中では構文エラーになる。
  // src/Code.js の specWebJsonForScript_ のコメント参照）。
  const out = ctx.specWebJsonForScript_({ s: 'a\u2028b\u2029c' });
  assert.ok(!out.includes('\u2028'));
  assert.ok(!out.includes('\u2029'));
  assert.deepEqual(JSON.parse(out), { s: 'a\u2028b\u2029c' });
});

test('specWebJsonForScript_ は undefined を null にする（素の undefined を出力しない）', () => {
  const ctx = loadGas({});
  assert.equal(ctx.specWebJsonForScript_(undefined), 'null');
});

test('?page=order&id=… に記号入りの値を渡すと既定画面に落ちる', () => {
  const ctx = loadGas({});
  const injected = ctx.resolveInitialScreen_({ page: 'order', id: '</script><script>alert(1)</script>' });
  assert.equal(injected.screen, null);
  // vm コンテキスト（別の実現域）のオブジェクトなので {} と deepEqual（strict）では
  // prototype 不一致で落ちる（storage.test.js の注記と同じ理由）。キー数で見る。
  assert.equal(Object.keys(injected.params).length, 0);

  const group = ctx.resolveInitialScreen_({ page: 'group', id: '"><img onerror=alert(1)>' });
  assert.equal(group.screen, null);

  // 正常な id（識別子・`Presentation:Name` 形式）はそのまま通る。
  const ok = ctx.resolveInitialScreen_({ page: 'order', id: 'Se:TestSlash' });
  assert.equal(ok.screen, 'assets');
  assert.equal(ok.params.openId, 'Se:TestSlash');
});
