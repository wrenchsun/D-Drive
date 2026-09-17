'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// 2026-09-17 追補（docs/41 P1-3 の修正に追随）: `issueApiToken` 等の**公開名**の運用関数は
// admin セッション必須になった（`specWebAssertAdminSession_`）。ここでのトークン発行・移行・
// ユーザー登録は「テストの前提を組み立てる」ためのものなので、内部実装（末尾 `_`）を直接呼ぶ。
// 公開名の関数が admin セッション無しで必ず失敗することは test/globals.test.js が固定している。

// O-6 AC: パラメータスキーマ + 現在値の保存・表示用 API。docs/32_spec_web.md §10.2.4・§10.4.2。
// 受け皿（paramSchemas コレクション・assetParams API）は Web 側チケットで実装済み。
// D-Drive からの実際の送信（SerializedObject+Tooltip 反射）と、書き込みトークンの許可表
// （Code.js の DDRIVE_WRITE_TOKEN_ALLOWED_APIS）への 'assetParams' 追加は D-Drive 側チケットで
// 2026-09-14 に対応した（Assets/DDrive/Editor/Spec/SpecWebSender.cs・SpecParamSchemaBuilder.cs）。
// ここの多くのテストは Google ログイン相当（editor/viewer 権限）で直接 API を呼んで確認する。

function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}
function viewerAuth() {
  return { ok: true, principal: 'viewer@example.com', email: 'viewer@example.com', role: 'viewer', displayName: '閲覧者' };
}
function call(ctx, name, params, auth) {
  return ctx.getApi_(name)({ params: params || {}, auth: auth || editorAuth() });
}

function schemaPayload() {
  return {
    schemas: [
      { assetType: 'Se', concreteType: 'SeData', fields: [
        { name: 'Clips', type: 'AudioClip[]', tooltip: '再生するクリップ' },
        { name: 'Volume', type: 'float', tooltip: '再生音量', min: 0, max: 1 }
      ] },
      { assetType: 'ControlSkin', concreteType: 'ButtonSkinData', fields: [{ name: 'Normal', type: 'Sprite', tooltip: '' }] },
      { assetType: 'ControlSkin', concreteType: 'SliderSkinData', fields: [{ name: 'Fill', type: 'Sprite', tooltip: '' }] }
    ],
    items: []
  };
}

test('assetParams: schemas を送るとスキーマが保存され、paramSchemas.list で読める', () => {
  const ctx = loadGas();
  const result = call(ctx, 'assetParams', { payload: JSON.stringify(schemaPayload()) });
  assert.deepEqual(Array.from(result.updatedSchemaTypes).sort(), ['ButtonSkinData', 'SeData', 'SliderSkinData']);

  const all = call(ctx, 'paramSchemas.list', {}, viewerAuth()).items;
  assert.equal(all.length, 3);

  const controlSkin = call(ctx, 'paramSchemas.list', { assetType: 'ControlSkin' }, viewerAuth()).items;
  assert.equal(controlSkin.length, 2, 'ControlSkin は ButtonSkinData/SliderSkinData の2つ');
});

test('assetParams: items（現在値）を送ると assets コレクションの params フィールドに patch される', () => {
  const ctx = loadGas();
  call(ctx, 'assets.create', { patch: JSON.stringify({ assetType: 'Se', identifier: 'Slash', displayName: '斬撃音' }) });

  const payload = Object.assign({}, schemaPayload(), {
    items: [{ id: 'Se::Slash', concreteType: 'SeData', currentValues: { Volume: 0.8, Mixer: 'SE_Main', Clips: '2 件' } }]
  });
  const result = call(ctx, 'assetParams', { payload: JSON.stringify(payload) });
  assert.deepEqual(Array.from(result.updatedIds), ['Se::Slash']);

  const item = call(ctx, 'assets.get', { id: 'Se::Slash' }, viewerAuth()).item;
  assert.equal(item.params.concreteType, 'SeData');
  assert.equal(item.params.currentValues.Volume, 0.8);
  assert.equal(item.params.currentValues.Mixer, 'SE_Main');
});

test('assetParams: 存在しない id は例外にせず skippedIds に積む', () => {
  const ctx = loadGas();
  const payload = { schemas: [], items: [{ id: 'Se::NotExist', concreteType: 'SeData', currentValues: {} }] };
  const result = call(ctx, 'assetParams', { payload: JSON.stringify(payload) });
  assert.deepEqual(Array.from(result.skippedIds), ['Se::NotExist']);
  assert.equal(result.updatedIds.length, 0);
});

test('assetParams: viewer は 403 で拒否される', () => {
  const ctx = loadGas();
  assert.throws(() => call(ctx, 'assetParams', { payload: JSON.stringify(schemaPayload()) }, viewerAuth()), (err) => {
    assert.equal(err.status, 403);
    return true;
  });
});

// 2026-09-14 更新（O-6 D-Drive 側実装）: Code.js の DDRIVE_WRITE_TOKEN_ALLOWED_APIS に
// 'assetParams' を追加したため、書き込みトークンで呼べるようになった。
// 他の書き込み API が引き続き拒否されることは ddriveSync.test.js
// （'書き込みトークンで assets.update を呼ぶと 403 で拒否される' 等）で確認している。
test('assetParams: 書き込みトークンで呼べる（O-6 D-Drive 側で許可表に追加済み）', () => {
  const ctx = loadGas();
  const token = ctx.specWebIssueApiToken_('write');
  const output = ctx.doPost({
    parameter: { api: '1', name: 'assetParams', token: token, payload: JSON.stringify(schemaPayload()) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, true);
  assert.deepEqual(Array.from(body.updatedSchemaTypes).sort(), ['ButtonSkinData', 'SeData', 'SliderSkinData']);
});

test('assets.create/update: params フィールドは書き込み系 API から設定できない（assetParams 専用）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', {
    patch: JSON.stringify({ assetType: 'Se', identifier: 'Slash', displayName: '斬撃音', params: { concreteType: 'SeData', currentValues: {} } })
  }).item;
  assert.equal(created.params, null);
});

// ── 2026-09-17（docs/41 P2-12 / P2-11） ──

test('assetParams: 他の送信 API と同じレート制限が効く（上限を超えると 429 相当）', () => {
  const ctx = loadGas();
  const empty = { payload: JSON.stringify({ schemas: [], items: [] }) };
  for (let i = 0; i < 10; i++) {
    call(ctx, 'assetParams', empty);
  }
  assert.throws(() => call(ctx, 'assetParams', empty), (err) => {
    assert.equal(err.status, 429);
    return true;
  });
  // principal が違えば影響を受けない（DDriveSync.js の他 3 kind と同じ挙動）。
  const other = { ok: true, principal: 'other@example.com', role: 'editor' };
  assert.doesNotThrow(() => call(ctx, 'assetParams', empty, other));
});

test('assetParams: 複数件でも paramSchemas.json / assets.json への書き込みは各 1 回だけ', () => {
  const ctx = loadGas();
  call(ctx, 'assets.create', { patch: JSON.stringify({ assetType: 'Se', identifier: 'Slash', displayName: '斬撃音' }) });
  call(ctx, 'assets.create', { patch: JSON.stringify({ assetType: 'Se', identifier: 'Thrust', displayName: '刺突音' }) });

  const schemasBefore = ctx.__fakes.drive.writeCount('paramSchemas.json');
  const assetsBefore = ctx.__fakes.drive.writeCount('assets.json');

  const payload = Object.assign({}, schemaPayload(), {
    items: [
      { id: 'Se::Slash', concreteType: 'SeData', currentValues: { Volume: 0.8 } },
      { id: 'Se::Thrust', concreteType: 'SeData', currentValues: { Volume: 0.5 } },
      { id: 'Se::NotExist', concreteType: 'SeData', currentValues: {} }
    ]
  });
  const result = call(ctx, 'assetParams', { payload: JSON.stringify(payload) });

  assert.deepEqual(Array.from(result.updatedIds), ['Se::Slash', 'Se::Thrust']);
  assert.deepEqual(Array.from(result.skippedIds), ['Se::NotExist']);
  assert.equal(
    ctx.__fakes.drive.writeCount('paramSchemas.json') - schemasBefore,
    1,
    'スキーマ 3 件ぶんの書き込みが 1 回にまとまっている'
  );
  assert.equal(ctx.__fakes.drive.writeCount('assets.json') - assetsBefore, 1);
  assert.equal(call(ctx, 'paramSchemas.list', {}, viewerAuth()).items.length, 3);
});
