'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// O-6（Web 側の受け皿のみ）AC: パラメータスキーマ + 現在値の保存・表示用 API。
// docs/32_spec_web.md §10.2.4・§10.4.2。D-Drive からの実際の送信は別チケット
// （書き込みトークンの許可表への追加も別チケット。ここでは Google ログイン相当で直接呼ぶ）。

function editorAuth() {
  return { ok: true, principal: 'editor@example.com', email: 'editor@example.com', role: 'editor', displayName: '編集者' };
}
function viewerAuth() {
  return { ok: true, principal: 'viewer@example.com', email: 'viewer@example.com', role: 'viewer', displayName: '閲覧者' };
}
function call(ctx, name, params, auth) {
  return ctx.getApi(name)({ params: params || {}, auth: auth || editorAuth() });
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

test('assetParams: 書き込みトークンの許可表には含まれていない（別チケット O-6 が D-Drive 側実装時に追加する）', () => {
  const ctx = loadGas();
  const token = ctx.issueApiToken('write');
  const output = ctx.doPost({
    parameter: { api: '1', name: 'assetParams', token: token, payload: JSON.stringify(schemaPayload()) }
  });
  const body = JSON.parse(output.getContent());
  assert.equal(body.ok, false);
  assert.equal(body.status, 403, 'Code.js の DDRIVE_WRITE_TOKEN_ALLOWED_APIS に未追加のため拒否される');
});

test('assets.create/update: params フィールドは書き込み系 API から設定できない（assetParams 専用）', () => {
  const ctx = loadGas();
  const created = call(ctx, 'assets.create', {
    patch: JSON.stringify({ assetType: 'Se', identifier: 'Slash', displayName: '斬撃音', params: { concreteType: 'SeData', currentValues: {} } })
  }).item;
  assert.equal(created.params, null);
});
