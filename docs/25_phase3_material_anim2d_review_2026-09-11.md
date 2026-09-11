# 25. Phase 3 後半（Material / Anim2D）コードレビュー結果（2026-09-11、Codex 未レビュー分の自前レビュー）

> 対象: main のコミット 7e654cd〜acfc31a（3-14/3-15 Material 共通チャンネル + Specific 自動解決 + D-Drive 標準 Lit/Unlit、3-16 初期アイコン、3-17 Unity Material 変換、3-18 TextureData → Importer 同期、3-19 Substance 命名、3-20 aiStandardSurface、3-21 Anim2D 取り込み + その後の Anim2D 修正 4 件）。読み取り専用のレビューエージェント（Opus）3 本で領域別に実施し、P1 は全件コードを再読して確認済み。行番号はレビュー時点（コミット acfc31a）のもの。
> **対応状況**: 本ファイル末尾の「対応状況」を参照。

## A. Anim2D / AnimEditor（3-21 とその後の修正）

### P1

- **A1. プレビュー物の所有者が決まっておらず、どちらかのウィンドウを閉じると共有物が消える** — `Anim2DEditorWindow.Edit.cs:83-85` は「Anim Editor で開く」で `StopPreview` + `ReleaseTarget` のみ行い `_previewObject` を保持したまま渡す。一方 `OnDisable`（`Anim2DEditorWindow.cs:55-59`）→ `DisposeScenePreview` → `RemovePreview` が `DestroyImmediate` する。Anim2D Editor を閉じる（またはスクリプト再コンパイル）と Anim Editor が引き取った物が消え、直したはずの「スプライトが消える」が再発。逆向き（`AnimEditorWindow.cs:97` の `DestroyAnim2DPreview`）も同じ。修正: 引き渡し時に `_previewObject = null` にして所有権を放棄、破棄は「撤去」の明示操作だけ。`OnDisable` は `_scene.Dispose()` のみ。
- **A2. Retiming モードが既定データで全キーを終端に潰し、方向 Clip 4/8 本を一括で壊す** — 生成直後の `Anim2DData.Retiming` は `ValueDef.Constant01(1f)` で `Evaluate` が常に 1。`BuildRetimingTimes`（`AnimationClipEditorUtility.cs:125-135`）は検証せずそのまま使い、`Anim2DRetiming.ApplyToDirectionClips` が全方向 Clip も 1 枚表示に書き換えて `SaveAssets`。修正: 狭義単調増加でなければ警告 + no-op。

### P2

- A3. `Anim2D.SetDirection` → `HasParameter`（`Anim2D.cs:94`）が `animator.parameters` で毎フレーム配列 ×2 を生成。`Anim2DFacing.Update` から呼ばれる定常経路の alloc（[12] §3 違反）。修正: hash と存在判定を対象設定時にキャッシュ。
- A4. 手動指定した 3D の確認用対象が Anim2DData へ切り替えても残る（`AnimEditorWindow.cs:618-634` は `OwnsCurrent` のときしか落とさない）。2D クリップを 3D リグに再生する。
- A5. `SpriteSlicer.cs:64-70,121` が Grid 分割で `maxTextureSize` を 16384 に恒久変更し `SaveAndReimport` を 2 回走らせる。`SpriteMetaData.rect` は元画像座標で Unity が換算するため不要。
- A6. `Anim2DPreviewObject.FindOrCreate` が名前一致だけで引き取り、`SpriteRenderer` / `Animator` / `hideFlags` を保証しない（同名のユーザー物で NRE、DontSave でない物がシーンに保存される）。
- A7. `RebuildClip` が 1 本ごとに `AssetDatabase.SaveAssets()`（方向 Clip 8 本で 9 回）。呼び出し側で 1 回に集約。
- A8. 検出オーバーレイのコンテナ高さが検出時にしか計算されず、ウィンドウ幅を変えると下が切れる。`GeometryChangedEvent` で再計算。
- A9. タイムライン目盛りが 1 フレーム 1 `DrawRect`（20 秒 @60fps で 1200 本/リペイント）。2px 未満なら間引く。

### テスト

- A10. `SpriteSlicerTests.cs:48-64` は自分で `maxTextureSize=16384` にするため「元サイズで計算」の回帰を検出できず、A5 の副作用を仕様として固定している。
- A11. `Anim2DRetimingTests.cs` は Uniform のみで A2 の経路が未検証。`MakeClip` が正規化時刻に秒を渡している（length=1 のときだけ一致）。
- A12. `Anim2DFacingTests.cs` は純関数 2 本のみ。`SetWorldDirection`（カメラ無し・長さ 0）が未検証。

### 整理項目

1. `Anim2DFacing.cs:61-64` の `_camera` キャッシュは利得がなく、main カメラ差し替え時だけ古い Yaw を使う。
2. `AnimEditorWindow.Source.cs:487,494` の `= F{...}` ラベルが隣の秒フィールド編集で更新されない。
3. `Anim2DPreviewObject.FindExisting` が `Resources.FindObjectsOfTypeAll` 全走査で、`SetTarget` のたびに呼ばれる。

## M. Material 基盤（3-14 / 3-15 / 3-16、AssetSearch）

### P1

- **M1. `MaterialCommonBinding.ApplyBlend` が `_SrcBlendAlpha` / `_DstBlendAlpha` / `_AlphaToMask` を設定しない** — `DDrive_Lit.shader:108,111` / `DDrive_Unlit.shader:52,62` は使う（既定 One/Zero/0）。URP の `BaseShaderGUI` は Transparent で `dstBlendA=OneMinusSrcAlpha`、Opaque+AlphaClip で `alphaToMask=1` にする。半透明素材のサムネイル・PNG アイコン・RenderTexture 出力でアルファが上書きされ穴が開き、Cutout の輪郭が URP Lit と変わる。
- **M2. 共通チャンネル名が `Specific` に載っていても検出されず、`Common` を黙って上書きする** — `MaterialManager` は Common → Specific の順に適用。`MaterialSpecificResolver.Merge`（`:57-78`）は既存項目を `MaterialCommonNaming` でふるわず、`MaterialDataValidator` も「シェーダーに無い」しか警告しない。`_Metallic` などが Specific に入ると Editor で Common を動かしても反映されず、原因が追えない。
- **M3. 初期アイコンの一括生成が「FindAssets メモリ対策」を無効化する** — `MaterialIconProvider.cs:28` が Material 1 件ごとに `EditorAnchorRegistry.Build()`（12 種別の FindAssets + 全 Data ロード）、かつ `SavePng` の `ImportAsset` が `AssetSearch` のキャッシュを毎回全消しするため、50 件で 600 回のキャッシュミス。
- **M4. アイコン PNG のファイル名がアセット名だけ**（`AssetIconService.cs:407-411`）— 別フォルダの同名 Data が同じ PNG に書き、一括生成で後勝ち上書き。既存アイコンも確認なく潰れる。

### P2

- M5. 一括生成が Prefab 系 Data 全件の `WaitForAssetPreview` を同時に起動し、`AssetPreview` のキャッシュを追い出し合って大半がタイムアウト。
- M6. `MaterialEditorWindow.OpenCompare`（`:111-127`）は呼び出し元が無く、初回オープン時は `CreateGUI` 前で NRE。削除。
- M7. `AssetSearch.Roots` が `Assets` 固定で `Packages/` を検索しない（旧 `FindAssets(filter)` と非同一）。現状実害なし。仕様として明記。
- M8. [12] §3「`AssetDatabase.FindAssets` 直呼び禁止」未対応: `MayaMaterialImporter.cs:147,220`（マテリアル/テクスチャごとに全走査）、`AssetReorganizer.cs:59`、`AddressablesSync.cs:68`、`AssetIconServiceTests.cs:64`。
- M9. `MaterialEditorWindow` / `MaterialThumbnailWindow` の状態（対象・比較対象・比較モード・ロック・形状・回転）が `[SerializeField]` でなく、ドメインリロードで全部リセット（他ウィンドウは付けている）。
- M10. `HasAnims` の対象は毎 `EditorApplication.update` でサムネイル再描画（比較中は 2 枚）、非フォーカスでも走る。
- M11. `AssetCreationService.cs:81-88` の自動アイコン割り当てが Undo 記録付きで 1 フレーム遅れて走り、作成直後の Ctrl+Z が「Set Asset Icon」だけを取り消す。

### テスト

- M12. `MaterialThumbnailRendererTests` / `AssetIconServiceTests` はサイズとパスしか見ておらず、真っ黒でも通る。
- M13. `MaterialSpecificResolverTests` は合成シェーダーのみで実 `DDrive/Lit` / `Unlit` を通していない。M2 のケースも無い。
- M14. `AssetSearchTests` は手動 `Invalidate` のみで、自動無効化（`ImportWatcher`）が未検証。

### 整理項目

1. `AssetSearch.Roots` が書き換え可能な `public static readonly string[]`。
2. `MaterialEditorWindow._compareThumbnail` が比較対象を外しても破棄されない。
3. `MaterialIconProvider.cs:41` の一時 Texture2D に `HideFlags.DontSave` が無い。
4. `MaterialDataValidator.cs:70-79` の「未登録の固有」Info が Specific 空の全 Data で毎回出る。

## I. Material インポート / 変換（3-17〜3-20）

### P1

- **I1. Substance の `_MetallicSmoothness` / `_SpecularSmoothness` を Mask 扱いにすると環境光が消える** — `TextureImportProfile.cs:88-89`。D-Drive の Mask は G=Occlusion で `MaterialCommonBinding` が同じテクスチャを `_OcclusionMap` にも入れ `_OCCLUSIONMAP` を ON にするが、Substance の Unity 5 テンプレートは G=0。間接光・GI が 0 になり真っ黒。修正: `_Metallic` 等と同じ `TextureChannel.Other`。
- **I2. `SourceMaterial` がマテリアル名だけなので同名 Material / 同名 FBX が 1 つの MaterialData に衝突する** — `UnityMaterialMigrator.cs:21,93` + `MayaMaterialImporter.cs:41,63,140-157`。`Enemy/Body.mat` と `Player/Body.mat` を一緒に変換すると 2 件目が 1 件目を上書きして「更新」と報告。修正: キーに元アセットの GUID を含める（旧形式は初回に移行）。

### P2

- I3. `AiStandardSurfacePreprocessor` に `GetVersion()` が無く、`Shader.Find` を依存登録しない。シェーダー未インポート時に URP の ArnoldStandardSurface のまま確定し、以後再インポートされない。
- I4. `TextureDataImporterSync` が DisplayName 編集でも `Apply` → `SaveAndReimport`、`SliceBorder` がテクスチャより大きいと読み戻しが一致せず毎回再インポート + 警告。
- I5. `MaterialConvertWindow.cs:599` の `delayCall += Refresh` が `OnDisable` 後に走り、一時 MaterialData をリーク。
- I6. `AiStandardSurfaceMapper.cs:46,100` の `Color.gray * 1.6f` / `Color.white * emission` が alpha も掛けて 1.6 に。Cutout が抜けず、`Common.AlbedoTint.a = 1.6` が保存される。
- I7. `MayaMaterialImporter` が `AssetDatabase.FindAssets` を直呼び、しかもマテリアル/テクスチャごとに全走査（M8 と同件）。
- I8. `DDrive_AiStandardSurfaceInput.hlsl` が Opacity / Metalness / SpecularRoughness / SpecularColor の 4 マップをキーワード無しで常時サンプル。`ForwardPass.hlsl:224` が `GetMainLight` を 2 回呼ぶ。
- I9. `UnityMaterialMigrator.CopySpecificValues`（`:181`）が `AssetCreationService.Create` の `configure`（CreateAsset 前）から `Undo.RecordObject` する。無意味な Undo エントリ。

### テスト

- I10. `TextureImportRulesTests.cs:102-109` が `:55-61` と同一で、規約の優先順（`_Normal` vs `_Normal_DirectX` vs `_N` など）を検証していない。
- I11. `TextureDataImporterSyncTests` は `AutoApply=false` で、いちばんリスクのある自動適用経路が未検証。
- I12. `UnityMaterialMigratorTests.cs:105` の `Specific == null || Length == 0` は解決が動かなくても通る。I2 の同名衝突・未対応シェーダーのフォールバックが未テスト。

### 整理項目

1. `MaterialConvertWindow._lastCreated` が未使用。
2. `Input.hlsl:144` が `metallic` を常に 1 に固定（Rendering Debugger の表示だけ実態と違う）。
3. `DDrive_AiStandardSurface.shader:442` の Meta パスだけ `_SPECGLOSSMAP` を宣言。

## 対応状況（2026-09-11、ブランチ `fix/phase3-material-anim2d-review`）

修正は領域別の Opus エージェント 3 本で実施し、コンパイル 0 エラー・シェーダー 0 エラー・**EditMode 337 / PlayMode 484 green**（修正前 EditMode 307。回帰テスト 30 件追加）。

- **A（Anim2D）**: A1〜A9 すべて修正。A1 は引き渡し時に `_previewObject = null`、両ウィンドウの `OnDisable` は参照放棄のみ（破棄は「撤去」/ モデル変更 / 対象切替の明示操作だけ）。A2 は `AnimationClipEditorUtility.TryBuildTimes` が狭義単調増加を検査し警告 + no-op（方向 Clip にも適用）。A3 は `Anim2D.SetDirection(Animator, Vector2, int, int)` + `Anim2DFacing` 側のハッシュキャッシュ。テスト A10〜A12 対応（alloc 計測のみ見送り）。整理項目 1〜3 は未対応。
- **M（Material 基盤）**: M1〜M6、M8〜M14 修正。M7 は「既定は `Assets` 配下のみ」を仕様として本書に明記（呼び出し側で `Packages` が要るときは folders を渡す）。M2 は `MergeReport.Conflict` + Validator の Warning（FixAction は Runtime asmdef から Editor を参照できないため付けず、Material Editor の「共通チャンネルの重複を削除」ボタンで手当て）。M4 のファイル名は `<名前>_<GUID8>_Icon.png`（既存の Icons 配下 PNG はそのまま上書き）。整理項目 1〜4 は未対応。
- **I（インポート / 変換）**: I1〜I9 すべて修正。I2 の新キーは `<SourceKey>/<GUID>/<名前>`、旧形式は `FindExisting` が見つけた時点で移行。I8 は `_METALNESSMAP / _SPECULARROUGHNESSMAP / _SPECULARCOLORMAP / _OPACITYMAP` の `shader_feature_local_fragment`（**既存の aiStandardSurface マテリアルは FBX 再インポートで直る**。`GetVersion()=1` により自動）。テスト I10〜I12 対応（I12 の未対応シェーダー警告は `Report.Lines` で検証）。整理項目 1〜3 のうち 2（metallic 実値化）は I8 と同時に対応、1・3 は未対応。
- 人による確認項目は [23](23_manual_verification_2026-09-11.md) 末尾「Phase 3 後半レビュー対応」。
