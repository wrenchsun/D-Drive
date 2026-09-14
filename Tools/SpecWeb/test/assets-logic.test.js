'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadHtmlScript } = require('./load-html-script.js');

// O-1/O-3/O-4/O-5 AC: 一覧の絞り込み・並べ替え・グルーピング・インライン検証・D-Drive 状態バッジ・
// コメント表示順・ロール判定・私が発注/私が受けた・Markdown プレビューを、DOM に依存しない
// 純粋関数として単体テストする。
// (docs/32_spec_web.md §10.3.2, §10.3.3, §10.3.4 / Tools/SpecWeb/html/AssetsLogic.html)

function load() {
  return loadHtmlScript('AssetsLogic').window.AssetsLogic;
}

test('validateIdentifier: 先頭大文字の英数字のみを PascalCase として受け付ける', () => {
  const logic = load();
  assert.equal(logic.validateIdentifier('Slash'), true);
  assert.equal(logic.validateIdentifier('FireBall2'), true);
  assert.equal(logic.validateIdentifier('slash'), false);
  assert.equal(logic.validateIdentifier('Fire_Ball'), false);
  assert.equal(logic.validateIdentifier(''), false);
});

test('buildAssetId: 種別::識別子 を組み立てる', () => {
  const logic = load();
  assert.equal(logic.buildAssetId('Se', 'Slash'), 'Se::Slash');
});

test('isStatusManuallySelectable: インポート済だけ選べない', () => {
  const logic = load();
  assert.equal(logic.isStatusManuallySelectable('発注済'), true);
  assert.equal(logic.isStatusManuallySelectable('納品済'), true);
  assert.equal(logic.isStatusManuallySelectable('インポート済'), false);
});

test('validateAssetFields: 必須項目・書式・選択肢を検証する', () => {
  const logic = load();
  const result = logic.validateAssetFields({ assetType: '', identifier: 'slash', displayName: '' });
  assert.equal(result.valid, false);
  assert.match(result.errors.assetType, /必須/);
  assert.match(result.errors.identifier, /PascalCase/);
  assert.match(result.errors.displayName, /必須/);
});

test('validateAssetFields: 種別+識別子の重複は既存一覧を見て即時に検出する（自分自身は除外）', () => {
  const logic = load();
  const existing = [{ id: 'Se::Slash', assetType: 'Se', identifier: 'Slash' }];

  const dup = logic.validateAssetFields(
    { assetType: 'Se', identifier: 'Slash', displayName: '斬撃音2' },
    { existingItems: existing }
  );
  assert.equal(dup.valid, false);
  assert.match(dup.errors.identifier, /既に存在/);

  const selfEdit = logic.validateAssetFields(
    { assetType: 'Se', identifier: 'Slash', displayName: '斬撃音' },
    { existingItems: existing, excludeId: 'Se::Slash' }
  );
  assert.equal(selfEdit.valid, true);
});

test('validateAssetFields: 状態・優先度・日付の書式も検証する', () => {
  const logic = load();
  const result = logic.validateAssetFields({
    assetType: 'Se',
    identifier: 'Slash',
    displayName: '斬撃音',
    status: '不明な状態',
    priority: '不明',
    dueDate: '2026/09/14'
  });
  assert.equal(result.valid, false);
  assert.ok(result.errors.status);
  assert.ok(result.errors.priority);
  assert.ok(result.errors.dueDate);
});

test('validateAssetFields: 状態に「インポート済」を直接指定すると拒否される', () => {
  const logic = load();
  const result = logic.validateAssetFields({ assetType: 'Se', identifier: 'Slash', displayName: '斬撃音', status: 'インポート済' });
  assert.equal(result.valid, false);
  assert.match(result.errors.status, /インポート済/);
});

test('filterAssets: 種別・状態・発注者・受注者・Presentation・キーワードで絞り込み、既定でアーカイブを除外する', () => {
  const logic = load();
  const items = [
    { id: 'Se::Slash', assetType: 'Se', identifier: 'Slash', displayName: '斬撃音', status: '納品済', orderer: 'よしだ', contractor: 'たなか', category: 'Player', parentId: 'og_1', archived: false, referenceMd: '' },
    { id: 'Vfx::FireBall', assetType: 'Vfx', identifier: 'FireBall', displayName: '火球', status: '発注済', orderer: '佐々木', contractor: 'さとう', category: 'Skill', parentId: null, archived: false, referenceMd: '' },
    { id: 'Se::Old', assetType: 'Se', identifier: 'Old', displayName: '廃止音', status: '発注済', orderer: 'よしだ', contractor: 'たなか', category: 'Player', parentId: null, archived: true, referenceMd: '' }
  ];

  assert.equal(logic.filterAssets(items, {}).length, 2); // アーカイブは既定で除外
  assert.equal(logic.filterAssets(items, { includeArchived: true }).length, 3);
  assert.equal(logic.filterAssets(items, { assetType: 'Vfx' }).length, 1);
  assert.equal(logic.filterAssets(items, { orderer: 'よしだ' }).length, 1);
  assert.equal(logic.filterAssets(items, { contractor: 'さとう' }).length, 1);
  assert.equal(logic.filterAssets(items, { query: 'fire' }).length, 1);
  assert.equal(logic.filterAssets(items, { parentId: 'og_1' }).length, 1);
  assert.equal(logic.filterAssets(items, { parentId: '__none__' }).length, 1);
});

test('sortAssets: 指定フィールド・方向で安定ソートする（元配列は変更しない）', () => {
  const logic = load();
  const items = [
    { id: 'b', identifier: 'Banana' },
    { id: 'a', identifier: 'Apple' },
    { id: 'c', identifier: 'Cherry' }
  ];
  const asc = logic.sortAssets(items, 'identifier', 'asc');
  assert.deepEqual(asc.map((i) => i.id), ['a', 'b', 'c']);

  const desc = logic.sortAssets(items, 'identifier', 'desc');
  assert.deepEqual(desc.map((i) => i.id), ['c', 'b', 'a']);

  // 元配列は破壊されない
  assert.equal(items[0].id, 'b');
});

test('groupAssetsByType: 種別ごとにグループ化し、種別名の辞書順に並ぶ', () => {
  const logic = load();
  const items = [
    { id: '1', assetType: 'Vfx' },
    { id: '2', assetType: 'Se' },
    { id: '3', assetType: 'Se' }
  ];
  const grouped = logic.groupAssetsByType(items);
  assert.deepEqual(Array.from(grouped, (g) => g.assetType), ['Se', 'Vfx']);
  assert.equal(grouped[0].items.length, 2);
  assert.equal(grouped[1].items.length, 1);
});

test('formatDdriveStateBadge: 未作成/Placeholder/作成済を判定する', () => {
  const logic = load();
  function assertBadge(badge, icon, label) {
    assert.equal(badge.icon, icon);
    assert.equal(badge.label, label);
  }
  assertBadge(logic.formatDdriveStateBadge(null), '⬜', '未作成');
  assertBadge(logic.formatDdriveStateBadge({ created: false }), '⬜', '未作成');
  assertBadge(logic.formatDdriveStateBadge({ created: true, isPlaceholder: true }), '🟡', '作成済（Placeholder）');
  assertBadge(logic.formatDdriveStateBadge({ created: true, isPlaceholder: false }), '✅', '作成済');
});

test('formatDdriveIconLabel: hasIcon(2026-09-14 追補)の有無でラベルを切り替える', () => {
  const logic = load();
  assert.equal(logic.formatDdriveIconLabel(null), 'アイコン: なし');
  assert.equal(logic.formatDdriveIconLabel({}), 'アイコン: なし');
  assert.equal(logic.formatDdriveIconLabel({ hasIcon: false }), 'アイコン: なし');
  assert.equal(logic.formatDdriveIconLabel({ hasIcon: true }), 'アイコン: あり');
});

test('sortCommentsNewestFirst: createdAt の新しい順に並べ替える（元配列は変更しない）', () => {
  const logic = load();
  const comments = [
    { id: '1', createdAt: '2026-09-01T00:00:00Z' },
    { id: '2', createdAt: '2026-09-14T00:00:00Z' },
    { id: '3', createdAt: '2026-09-07T00:00:00Z' }
  ];
  const sorted = logic.sortCommentsNewestFirst(comments);
  assert.deepEqual(sorted.map((c) => c.id), ['2', '3', '1']);
  assert.equal(comments[0].id, '1'); // 元配列は破壊されない
});

test('roleAtLeast: viewer < editor < admin の階層で判定する', () => {
  const logic = load();
  assert.equal(logic.roleAtLeast('viewer', 'editor'), false);
  assert.equal(logic.roleAtLeast('editor', 'editor'), true);
  assert.equal(logic.roleAtLeast('admin', 'editor'), true);
  assert.equal(logic.roleAtLeast(null, 'viewer'), false);
});

// ---- O-4: 私が発注 / 私が受けた ----

test('myOrderedItems: orderer===email の発注だけを返し、未納品・期限切れのフラグを付ける', () => {
  const logic = load();
  const items = [
    { id: 'A', orderer: 'yoshida@example.com', status: '発注済', dueDate: '2026-09-10', archived: false },
    { id: 'B', orderer: 'yoshida@example.com', status: 'インポート済', dueDate: '2026-09-01', archived: false },
    { id: 'C', orderer: 'sasaki@example.com', status: '発注済', dueDate: '2026-09-10', archived: false },
    { id: 'D', orderer: 'yoshida@example.com', status: '発注済', dueDate: '2026-09-10', archived: true }
  ];
  const result = logic.myOrderedItems(items, 'yoshida@example.com', '2026-09-14');
  assert.deepEqual(Array.from(result, (i) => i.id), ['A', 'B']);
  assert.equal(result[0]._unfulfilled, true);
  assert.equal(result[0]._overdue, true); // 2026-09-10 < 2026-09-14
  assert.equal(result[1]._unfulfilled, false); // インポート済
  assert.equal(result[1]._overdue, false); // インポート済は期限切れ扱いにしない
});

test('myContractedItems: contractor===email の発注を期限の昇順（未設定は末尾）で返す', () => {
  const logic = load();
  const items = [
    { id: 'A', contractor: 'tanaka@example.com', dueDate: '2026-09-30', archived: false },
    { id: 'B', contractor: 'tanaka@example.com', dueDate: '2026-09-10', archived: false },
    { id: 'C', contractor: 'tanaka@example.com', dueDate: '', archived: false },
    { id: 'D', contractor: 'other@example.com', dueDate: '2026-09-01', archived: false }
  ];
  const result = logic.myContractedItems(items, 'tanaka@example.com', '2026-09-14');
  assert.deepEqual(Array.from(result, (i) => i.id), ['B', 'A', 'C']);
  assert.equal(result[0]._overdue, true);
  assert.equal(result[1]._overdue, false);
});

// ---- O-5: Markdown プレビュー（XSS 対策） ----

test('renderMarkdownSafe: 見出し・強調・コード・改行を変換する', () => {
  const logic = load();
  const html = logic.renderMarkdownSafe('# 見出し\n**強調** と `コード`');
  assert.match(html, /<h1>見出し<\/h1>/);
  assert.match(html, /<strong>強調<\/strong>/);
  assert.match(html, /<code>コード<\/code>/);
});

test('renderMarkdownSafe: リンクは target="_blank" rel="noopener" で開き（O-16: <base target="_top"> 環境で外部リンクが自アプリを差し替えないように別タブに変更）、画像は img タグになる', () => {
  const logic = load();
  const html = logic.renderMarkdownSafe('[参考動画](https://example.com/video) ![説明](https://example.com/a.png)');
  assert.match(html, /<a href="https:\/\/example\.com\/video" target="_blank" rel="noopener">参考動画<\/a>/);
  assert.match(html, /<img[^>]*src="https:\/\/example\.com\/a\.png"/);
});

test('renderMarkdownSafe: javascript: リンクは無効化され、テキストだけが残る', () => {
  const logic = load();
  const html = logic.renderMarkdownSafe('[クリック](javascript:alert(1))');
  assert.doesNotMatch(html, /javascript:/);
  assert.doesNotMatch(html, /<a /);
  assert.match(html, /クリック/);
});

test('renderMarkdownSafe: HTML タグ・属性は先にエスケープされ、そのままでは解釈されない（XSS 対策）', () => {
  const logic = load();
  const html = logic.renderMarkdownSafe('<script>alert(1)</script><img src=x onerror=alert(1)>');
  // <, > が実体参照化されているため、"onerror=" という文字列自体は残っても
  // ブラウザが実タグ・実属性として解釈することはない（隠れた <script> や <img> タグが無い）。
  assert.doesNotMatch(html, /<script[\s>]/);
  assert.doesNotMatch(html, /<img[\s>]/);
  assert.match(html, /&lt;script&gt;/);
  assert.match(html, /&lt;img/);
});

// ---- O-12: ファイル形式・ファイル名 ----

test('fileFormatChoicesFor: 種別ごとの候補を返す（候補が無い種別は空配列 = 自由入力のみ）', () => {
  const logic = load();
  assert.deepEqual(Array.from(logic.fileFormatChoicesFor('Se')), ['.wav', '.ogg', '.mp3']);
  assert.deepEqual(Array.from(logic.fileFormatChoicesFor('Texture')), ['.png', '.psd', '.tga']);
  assert.deepEqual(Array.from(logic.fileFormatChoicesFor('Model')), ['.fbx']);
  assert.deepEqual(Array.from(logic.fileFormatChoicesFor('Vfx')), ['.prefab', '.unitypackage']);
  assert.deepEqual(Array.from(logic.fileFormatChoicesFor('Presentation')), []);
});

test('normalizeFileFormat: 先頭ドット無しでも .xxx に揃える。空欄はそのまま空文字', () => {
  const logic = load();
  assert.equal(logic.normalizeFileFormat('png'), '.png');
  assert.equal(logic.normalizeFileFormat('.png'), '.png');
  assert.equal(logic.normalizeFileFormat('  wav  '), '.wav');
  assert.equal(logic.normalizeFileFormat(''), '');
  assert.equal(logic.normalizeFileFormat(undefined), '');
});

test('suggestFileName: 種別・カテゴリ無し・識別子・ファイル形式から命名規約に沿った推奨名を作る（§10.2.1 例）', () => {
  const logic = load();
  assert.equal(logic.suggestFileName('Se', '', 'Slash', '.wav'), 'SE_Slash.wav');
  assert.equal(logic.suggestFileName('Se', '', 'Slash', 'wav'), 'SE_Slash.wav'); // 正規化してから付ける
});

test('suggestFileName: カテゴリがあれば <接頭辞>_<カテゴリ>_<識別子> になる（AssetNamingService.BuildFileName と同じ組み立て）', () => {
  const logic = load();
  assert.equal(logic.suggestFileName('Vfx', 'Skill', 'FireBall', '.prefab'), 'VFX_Skill_FireBall.prefab');
  assert.equal(logic.suggestFileName('Vfx', 'Skill/Fire', 'FireBall', '.prefab'), 'VFX_Fire_FireBall.prefab'); // 最終セグメントのみ
});

test('suggestFileName: ファイル形式が未設定なら拡張子無しの名前になる。識別子が無ければ空文字', () => {
  const logic = load();
  assert.equal(logic.suggestFileName('Se', '', 'Slash', ''), 'SE_Slash');
  assert.equal(logic.suggestFileName('Se', '', '', '.wav'), '');
});

test('suggestFileName: 未知の種別接頭辞は ASSET_ にフォールバックする', () => {
  const logic = load();
  assert.equal(logic.suggestFileName('NotAType', '', 'X', '.dat'), 'ASSET_X.dat');
});

test('fileNameIllegalChars: Windows のファイル名禁止文字を検出する（重複は1つにまとめる）', () => {
  const logic = load();
  assert.deepEqual(Array.from(logic.fileNameIllegalChars('SE_Slash.wav')), []);
  assert.deepEqual(Array.from(logic.fileNameIllegalChars('a/b\\c:d*e?f"g<h>i|j/k')), ['/', '\\', ':', '*', '?', '"', '<', '>', '|']);
});

test('fileNameWarnings: 不正文字・拡張子の食い違いを警告として返す（保存はブロックしない）', () => {
  const logic = load();
  const illegal = logic.fileNameWarnings({ fileName: 'bad/name.wav', fileFormat: '.wav' });
  assert.equal(illegal.length, 1);
  assert.match(illegal[0], /使えない文字/);

  const mismatch = logic.fileNameWarnings({ fileName: 'SE_Slash.ogg', fileFormat: '.wav' });
  assert.equal(mismatch.length, 1);
  assert.match(mismatch[0], /一致していません/);

  const clean = logic.fileNameWarnings({ fileName: 'SE_Slash.wav', fileFormat: '.wav' });
  assert.equal(clean.length, 0);

  const noFormat = logic.fileNameWarnings({ fileName: 'SE_Slash.wav', fileFormat: '' });
  assert.equal(noFormat.length, 0); // fileFormat 未設定なら食い違い判定はしない
});

test('validateAssetFields: fileFormat/fileName の長さ上限を検証する（不正文字はここではブロックしない）', () => {
  const logic = load();
  const tooLongFormat = logic.validateAssetFields({
    assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
    fileFormat: '.' + 'a'.repeat(30)
  });
  assert.equal(tooLongFormat.valid, false);
  assert.match(tooLongFormat.errors.fileFormat, /文字以内/);

  const tooLongName = logic.validateAssetFields({
    assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
    fileName: 'a'.repeat(300) + '.wav'
  });
  assert.equal(tooLongName.valid, false);
  assert.match(tooLongName.errors.fileName, /文字以内/);

  const illegalCharsOnly = logic.validateAssetFields({
    assetType: 'Se', identifier: 'Slash', displayName: '斬撃音',
    fileName: 'bad/name.wav'
  });
  assert.equal(illegalCharsOnly.valid, true, '不正文字は errors に入れない（fileNameWarnings 側の警告のみ）');
});

// ---- O-15: canRenameAsset（発注後の識別子・種別の変更可否のクライアント側ミラー） ----

test('canRenameAsset: ddriveState が無い/未作成・status がインポート済でなければ true', () => {
  const logic = load();
  assert.equal(logic.canRenameAsset({ status: '発注済' }), true);
  assert.equal(logic.canRenameAsset({ status: '納品済', ddriveState: { created: false } }), true);
  assert.equal(logic.canRenameAsset({ status: '発注済', ddriveState: null }), true);
});

test('canRenameAsset: ddriveState.created が true なら false（D-Drive で作成済み）', () => {
  const logic = load();
  assert.equal(logic.canRenameAsset({ status: '発注済', ddriveState: { created: true } }), false);
  // Placeholder のままでも created=true なら変更不可（status はまだインポート済でなくても）。
  assert.equal(logic.canRenameAsset({ status: '発注済', ddriveState: { created: true, isPlaceholder: true } }), false);
});

test('canRenameAsset: status が「インポート済」なら false', () => {
  const logic = load();
  assert.equal(logic.canRenameAsset({ status: 'インポート済', ddriveState: { created: false } }), false);
});

test('canRenameAsset: item が無ければ false（安全側）', () => {
  const logic = load();
  assert.equal(logic.canRenameAsset(null), false);
  assert.equal(logic.canRenameAsset(undefined), false);
});

// ---- O-16: hasReferenceMd（メモが入力済みかどうか） ----

test('hasReferenceMd: 空文字・空白のみ・undefined・null は false', () => {
  const logic = load();
  assert.equal(logic.hasReferenceMd({ referenceMd: '' }), false);
  assert.equal(logic.hasReferenceMd({ referenceMd: '   ' }), false);
  assert.equal(logic.hasReferenceMd({ referenceMd: undefined }), false);
  assert.equal(logic.hasReferenceMd({}), false);
  assert.equal(logic.hasReferenceMd(null), false);
});

test('hasReferenceMd: 内容があれば true', () => {
  const logic = load();
  assert.equal(logic.hasReferenceMd({ referenceMd: '参考: https://example.com' }), true);
});
