# 06. マテリアル / 画像（テクスチャ） 詳細設計

関連: [05_model_animation.md](05_model_animation.md) / [02_core_framework.md](02_core_framework.md)

---

# Part A — マテリアル

## A-1. 要件

- 変換機能のため **Albedo / Normal などの共通データを先に定義**（シェーダー間の相互変換の土台）
- 透過/非透過、相互変換機能、共通データ + 固有データ、描画・ライトレイヤー
- 生成・常時・消滅イベント（UV スクロール等）
- Maya の特定マテリアルに特化した FBX 読み込みからの自動生成

## A-2. データ構造

```csharp
// ★共通データ: すべてのシェーダーで意味が共通するチャンネル定義
[Serializable]
public struct MaterialCommon
{
    public TextureId Albedo;   public Color AlbedoTint;
    public TextureId Normal;   public float NormalScale;
    public TextureId Mask;     // R:Metallic G:Occlusion B:Detail A:Smoothness (規約固定)
    public TextureId Emission; public Color EmissionColor; [HDR] public float EmissionIntensity;
    public BlendType Blend;    // Opaque / Cutout / Transparent
    public float Cutoff;
    public bool DoubleSided;
}

public class MaterialData : AssetDataBase
{
    public Shader Shader;                 // or ShaderId で管理
    public MaterialCommon Common;         // ★どのシェーダーでも共通
    public ShaderParam[] Specific;        // シェーダー固有パラメータ (名前+型+値)
    [Header("Render")]
    public int RenderQueueOffset;
    public uint RenderingLayerMask;
    public uint LightLayerMask;
    [Header("Anim")]
    public MaterialAnim[] Anims;          // UVスクロール等 (下記)
}

[Serializable]
public struct MaterialAnim     // 「常時イベント」の実体
{
    public string Property;         // "_BaseMap_ST" 等
    public ValueDef Value;          // 形 + スピードの統一表現（[17]）
                                    //   UV スクロール → TimeMode=Rate + Loop
                                    //   サイン波      → Parametric(InOutSine) + PingPong
                                    //   任意波形      → Curve
                                    //   種別分岐の列挙型を持たないため Manager 側も単純化される
}
```

### 相互変換機能

```
MaterialData(ShaderA) ──共通データはそのまま──▶ MaterialData(ShaderB)
                        固有データは変換テーブル or 破棄(警告表示)
```

- `ShaderConversionTable`（ScriptableObject）に「ShaderA の _SpecColor → ShaderB の _F0」のようなマッピングを登録制で持つ
- 変換実行はエディタ機能（差分プレビュー付き）。共通チャンネルを最初に固定しておくことが変換成立の前提 → **プロジェクト初期に MaterialCommon の規約を確定させる**（Mask チャンネル割当等）

### Maya FBX 自動生成

- 目標は **DCC からの「Export ボタン一つ」**（[00] FR-1.5 / [10] §3 の「命名はツールが生成する」方針の DCC 側入口）。アーティストは Maya 側で意味情報（対象キャラ・部位等）を意識するだけでよく、Unity 側の規約名・ID・Data 生成はインポートパイプラインが行う
- `AssetPostprocessor.OnPostprocessMaterial` で Maya 由来マテリアル（例: aiStandardSurface / Stingray PBS）のプロパティを規約に従って MaterialCommon にマッピングし、MaterialData + Unity Material を自動生成（ID 発行・カタログ登録・規約名リネームまで一括）
- マッピング規約は `MayaImportProfile`（ScriptableObject）でデザイナーが編集可能（テクスチャ命名規則 `_BC/_N/_M` → チャンネル割当）
- 再インポート時は固有調整を上書きしない（Common のみ更新 / 差分レポート表示）

### 実装メモ（2026-09-10、チケット 3-5 で確定した規約）

> - **MaterialCommon の規約を確定**（`Runtime/Material/MaterialCommon.cs`）: Albedo(+AlbedoTint) / Normal(+NormalScale) / Mask(R=Metallic G=Occlusion B=Detail A=Smoothness、Mask 無しの定数 Metallic / Smoothness) / Emission(+EmissionColor × EmissionIntensity) / Blend(Opaque / Cutout(Cutoff) / Transparent) / DoubleSided。テクスチャは `AssetId<TextureMarker>`（TextureData の ID）で参照し、`TextureData.Channel` と 1:1 対応
> - **シェーダーへの流し込み** は `MaterialCommonBinding.Apply(material, common, resolveTexture)`: プロパティ名は候補を順に `HasProperty` で探す（Albedo=`_BaseMap`→`_MainTex`、Tint=`_BaseColor`→`_Color`、Normal=`_BumpMap`+`_BumpScale`、Mask=`_MetallicGlossMap`+`_OcclusionMap`(URP Lit)/`_MaskMap`(HDRP)、Emission=`_EmissionMap`+`_EmissionColor`+`_EMISSION`）。Blend は URP の ShaderGUI がやるブレンドステート設定（`_Surface/_SrcBlend/_DstBlend/_ZWrite/_AlphaClip` + keyword + RenderType タグ）をランタイムで再現する。固有パラメータの変換（`ShaderConversionTable`）は 3-6
> - **MaterialData**（`MaterialData.cs`）: `Shader`（未設定なら既定の URP Lit → Standard）/ `Common` / `Specific: ShaderParam[]`（名前 + `ParamValue`）/ `RenderQueueOffset`（Blend から決まる基準 2000 / 2450 / 3000 へのオフセット）/ `RenderingLayerMask` / `Anims: MaterialAnim[]`。`MaterialAnim` は `Property` + `Channel`(Float / OffsetU / OffsetV) + `ValueDef`（設計の「`_BaseMap_ST` を文字列で指定」は、UV スクロールがどの成分かを明示するため Channel に分けた）
> - **TextureData**（`TextureData.cs`）: 設計 B-2 の最小構成（Texture / Usage / Sprite / AllowScale / SliceBorder / Channel）を 3-5 で先行実装。Importer 規約・`TextureImportProfile`・`UsedIn*` 自動収集・Validator は 3-8
> - **MaterialManager**（`MaterialManager.cs`）: `Get(id)` は Data ごとに Unity Material を 1 つ生成して共有（DontSave。Data は書き換えない）。`Apply` はスロットへ共有 Material を割り当て、`Replace` は Apply 済み + シーン内の Renderer を走査して差し替える（明示呼び出し限定）。`FadeTo` は from のコピー（一時 Material）を `Material.Lerp` で to へ寄せ、終了時に共有 to へ戻して一時 Material を破棄する（シェーダーが違うときは半分で切替）。`MaterialAnim` は Tick が共有 Material に対して駆動（`OnPause` は Flags.Pause 準拠）。未登録 ID はマゼンタの Placeholder
> - **Mats** ファサード: `Get / Apply / Replace / FadeTo / SetGlobalParam / IsFading / Stop`。`DDriveRuntimeBootstrap` が生成・Bind し、`ModelsManager` に接続する（`ModelData.Slots` の Material は Spawn 時に適用、`Models.SetMaterial` も実際に差し替わる = 2-5 の残課題を解消）
> - **Validation**（`MaterialDataValidator`）: Shader 未設定(Warning) / パイプライン不一致(Error、`ShaderPipelineAnalyzer`) / Albedo 未設定(Warning) / Blend と RenderQueue 帯の不一致(Warning) / Emission が発光しない設定(Warning) / Specific がシェーダーに無い(Warning) / Anims が動かない設定(Warning)
> - **エディタ（最小版）**: `Editor/Material/MaterialEditorWindow.cs`（`Tools/D-Drive/Editors/Material`、`[DataEditor]` で MaterialData / TextureData の Inspector から開ける）。SerializedObject バインドで編集し、「シーンにプレビュー球を配置」で実 MaterialManager が生成した共有 Material を DontSave の球に適用して SceneView で確認（MaterialAnim も EditMode で動く）。TextureData は画像プレビュー
> - **相互変換（3-6、2026-09-10）**: `ShaderConversionTable`（ScriptableObject。`Rules[] = {From, To, Mappings[] = {FromProperty, ToProperty(空=意図的に破棄), Scale, Offset}}`。`D-Drive/Material/Shader Conversion Table` で作成、複数可）+ `MaterialConverter.Convert(source, targetShader, tables)`（純関数。共通データはそのまま、固有は 表で対応 → 同名・同型なら維持 → 無ければ破棄 の順で判定し、`Result.Entries` に Mapped / Kept / Dropped / Discarded を返す）+ `MaterialConverter.ApplyTo(dest, source, result)`。変換エディタは `Editor/Material/MaterialConvertWindow.cs`（`Tools/D-Drive/Editors/Material 変換`、MaterialData の Inspector の「シェーダー変換」）: 差分プレビュー（→ 対応 / = 維持 / ✕ 破棄 / − 意図的に破棄）を表示し、「新規 MaterialData として作成」（同じカテゴリへ `AssetCreationService.Create`）か「この Data を変換」（Undo）を行う。テスト: `MaterialConverterTests`
> - **Maya FBX 自動生成（3-7、2026-09-10）**: `MayaImportProfile`（`Editor/Material/`。`Create > D-Drive > Material > Maya Import Profile`。AutoImport / IncludePathContains / TargetShader / カテゴリ=FBX の親フォルダ名 or 固定 / `PropertyChannels`(テクスチャプロパティ → チャンネル。既定 `_BaseMap`・`_MainTex`→Albedo、`_BumpMap`→Normal、`_MetallicGlossMap`・`_MaskMap`・`_OcclusionMap`→Mask、`_EmissionMap`→Emission) / `ClassifyByTextureName`(プロパティで決まらなければ TextureImportProfile の `_N/_M/_E` で分類) / `PreserveSpecificOnReimport`）+ `MayaMaterialImporter.ImportModel(path, profile)`（FBX 内の全 Material → `ImportMaterial`: 参照テクスチャごとに `TextureData` を確保(同じ Texture2D を指す既存を再利用)し、色・Metallic・Smoothness・Emission・Blend(renderQueue / `_ALPHATEST_ON`)・DoubleSided(`_Cull`)を `MaterialCommon` に写して `MaterialData` を ID 発行・カタログ登録・規約名で作成。`MaterialData.SourceMaterial`("FBX名/マテリアル名")で同定し、再インポートは **Common だけ更新**、Specific / Anims / Render は保持、差分を `Report` に列挙）+ `MayaModelPostprocessor`（`OnPostprocessModel` → delayCall で実行。`Suppress` でテスト時抑止）+ メニュー `Tools/D-Drive/Generate/選択したモデルから MaterialData を生成`（手動）。テスト: `MayaMaterialImporterTests`。**注意**: Unity が FBX から生成する Material のプロパティ名は「Import Materials」設定と RP に依存する(URP では Lit)。aiStandardSurface / Stingray PBS 固有の名前が出る場合は Profile の PropertyChannels に追加する
> - **エディタ プレビュー拡張（3-9、2026-09-10）**: `Editor/Material/MaterialPreviewBuilder.cs`（`MaterialPreviewShape` = Sphere/Plane(Quad)/Cube/Model の純粋なプレビュー生成ロジック。UI から切り離してテストできる）: 球/板/Cube は `GameObject.CreatePrimitive` + `MaterialManager.ApplyData`、Model は渡された `ModelsManager.SpawnData` で配置し、`ModelData.Slots` があればその `RendererPath`/`SlotIndex` の通りに、無ければ全 Renderer の全スロットに適用する。`MaterialEditorWindow` はこれを使って「形状」（EnumField、Model 選択時のみ ObjectField<ModelData> を表示）/「ターンテーブル」（Y 軸自動回転、`ModelEditorWindow` と同じ方式）/「ライト回転」（0-360 のスライダーでシーン最初の Directional Light を Y 軸回転、無ければ何もしない。撤去時に元の回転へ復元）/「比較対象」+「並べて比較」（対象の X+1.5 に比較用 MaterialData を適用した 2 体目を並べる）を追加。「再生成」は両方(対象+比較)を再適用する。`MaterialConvertWindow` に「Material Editor で比較」ボタンを追加し、`MaterialEditorWindow.OpenCompare(a, b)`（変換元と、直近で「新規 MaterialData として作成」した場合はその結果）で開く。テスト: `MaterialEditorPreviewTests`（EditMode、8 件）
> - **共通チャンネル命名規約 + Specific 自動解決（2026-09-11）**: `Runtime/Material/MaterialCommonNaming.cs` が「どのプロパティ名が共通チャンネルで、どれが固有か」を名前で決める規約の唯一の定義。規約: (1) 共通チャンネル名（`_BaseMap`/`_MainTex`、`_BaseColor`/`_Color`、`_BumpMap`/`_BumpScale`/`_NormalScale`、`_MetallicGlossMap`/`_OcclusionMap`/`_MaskMap`/`_Metallic`/`_Smoothness`/`_Glossiness`/`_SmoothnessTextureChannel`、`_EmissionMap`/`_EmissionColor`）(2) 描画ステート名（`_Surface`/`_Blend`/`_SrcBlend`/`_DstBlend`/`_ZWrite`/`_AlphaClip`/`_Cutoff`/`_Cull`/`_Mode` + URP ShaderGUI 管理の `_ZTest`/`_ZWriteControl`/`_QueueOffset`/`_QueueControl`/`_BlendOp`/`_SrcBlendAlpha`/`_DstBlendAlpha`/`_AlphaToMask`/`_BlendModePreserveSpecular`）(3) 付随名 `<Tex>_ST`/`_TexelSize`/`_HDR` (4) 予約接頭辞 `unity_*`/`_Unity*` (5) `[HideInInspector]`/`[PerRendererData]`/`[NonModifiableTextureData]` → 以上は固有に含めず、**それ以外はすべて固有**。カスタムシェーダーで共通チャンネルを使うときはこの名前で宣言する（`MaterialCommonBinding` が流し込み、Specific には出ない）。`MaterialSpecificResolver`（Runtime、純関数）: `Resolve(shader)` = 固有を既定値付き `ShaderParam` に列挙（Float/Range→Float、`Integer`→Int（旧 `Int` 記法は Float 扱い）、Color、Vector、Texture→Object(null)）/ `Merge(existing, shader, report)` = **既存の値は保持・足りないものだけ追加・シェーダーに無いものも残す**（Maya 再インポートの「固有調整を保持」と両立）/ `FindUnregistered`。Editor 入口は `Editor/Material/MaterialSpecificSync.Sync(data)`（Undo + SetDirty、変更が無ければ触らない）。呼び出し: `MaterialEditorWindow`（Shader フィールドを `TrackPropertyValue` で監視し変更時に delayCall で自動同期 + 「シェーダーから固有を同期」ボタン + 未登録件数表示）/ `MaterialConvertWindow`（変換後に変換先の残りの固有を補完。新規作成・その場変換とも）/ `MayaMaterialImporter`（新規作成時のみ）。`MaterialDataValidator` は未登録の固有を Info で列挙（Runtime 側なので FixAction 無し）。テスト: `MaterialSpecificResolverTests`（EditMode、`ShaderUtil.CreateShaderAsset` でメモリ上のシェーダーを使う）
> - **Maya インポート後にプレビューが灰色になる修正（2026-09-11、人による確認で判明）**: Material Editor / ポップアップ / 変換ウィンドウは開いた時点で `EditorAnchorRegistry.Build()` した Registry を持つため、後から作られた TextureData の ID を解決できず、テクスチャ無し（Tint だけ）で描いていた。`projectChanged` で dirty を立てる仕組みはあったが再走査が「シーン配置」「再生成」の経路だけで、サムネイル描画が通っていなかった。各ウィンドウの描画直前に `Refresh(registry)` + `MaterialManager.Clear()`（古い解決で作った共有 Material を捨てる）を行う `EnsureRegistryFresh` を通すようにした
> - **Material Editor プレビューの修正（2026-09-11、人による確認で判明）**: (1) 共有 Material は配置時に 1 回生成されるだけで Data 編集が反映されなかった → `TrackSerializedObjectValue` で Inspector バインド / Undo の変更を検知し、`OnEditorUpdate` で 0.1 秒間隔に間引いて `RebuildPreview` を自動実行（「再生成」ボタンは残す）。(2) 配置位置がルート生成時の SceneView pivot 固定で、視点を動かした後の再配置が古い場所に出た → 配置のたびに SceneView カメラ前方（`cameraDistance` を 2〜10 に clamp。2D モードは pivot）へ置き、`SceneView.Frame` でそこへ寄せる。(3) 「全体的に重い」の原因: `RebuildPreview` が毎回 `EditorAnchorRegistry.Refresh`（12 種の Data を `FindAssets` + 同期ロード）していた → `EditorApplication.projectChanged` で dirty を立て、次回の配置 / 再生成でだけ再走査する。(4) プレビュー物を Inspector で選択したまま破棄すると `GameObjectInspector` が MissingReference を出す → 破棄前に選択を外す
> - **ウィンドウ内サムネイル（2026-09-11、[09] §2 の Material 例外）**: `Editor/Material/MaterialThumbnailRenderer.cs`（`PreviewRenderUtility`。`Render(material, shape, turntableDeg, lightDeg, w, h)` で球/板(Quad は Z 回転)/Cube を既定ライト 2 灯 + 環境光で描く。Model 形状は null）。`MaterialEditorWindow` は最上部に 220px の `Image` を置き、Data 編集（自動再生成）・形状切替・ターンテーブル・ライト回転・MaterialAnim 再生のときだけ描き直す（`_thumbnailDirty`）。描くのは `MaterialManager.GetData` が返す共有 Material そのもの。シーン配置は従来どおり残す（実シーン照明・モデル適用・並列比較）。テスト: `MaterialThumbnailRendererTests`
> - **サムネイルのポップアップ（2026-09-11）**: `Editor/Material/MaterialThumbnailWindow.cs`（`Tools/D-Drive/Editors/Material プレビュー`、`[DataEditor(typeof(MaterialData), "プレビューをポップアップ")]`、Material Editor の「ポップアップ」ボタンは形状・回転・ライト角を引き継いで開く）。ウィンドウごとに `MaterialManager` + `MaterialThumbnailRenderer` を持ち、Image をウィンドウいっぱいに描く（サイズ変更で描き直し）。Data の変更は `ObjectChangeEvents.changesPublished` の `ChangeAssetObjectProperties`（対象の instanceId 一致）で検知するので、Inspector 編集・Undo・Material Editor の自動同期のいずれでも追従する。選択追従（🔒 で固定）。Model 形状は選べない（シーン配置の担当）。**ドラッグ回転（2026-09-11）**: サムネイル（Material Editor / ポップアップ共通、`MaterialThumbnailRenderer.AttachDrag`）を左ドラッグで回す。横 = Y 軸（ターンテーブルと加算）、縦 = X 軸の傾き（±80° に丸め）。板（Quad）は裏を向かないよう Z 回転のみで傾きは無視。`Render` に yaw / pitch を分けた overload を追加（旧 signature は pitch 0 で維持）。「X 反転」「Y 反転」トグル（`MaterialThumbnailRenderer.CreateInvertToggles`、EditorPrefs `DDrive.MaterialThumbnail.InvertDragX/Y` で両ウィンドウ共有）で回転方向を反転できる
> - **サムネイルでの比較（2026-09-11）**: Material Editor の「比較対象」が入っている（または Material 変換の「Material Editor で比較」で開いた）とき、サムネイル領域が比較表示になる。「左右」= A（対象）と B（比較対象）を 2 分割で並べる / 「切替」= 1 枚を「A → B」「B → A」ボタンで切り替える。回転（ドラッグ / ターンテーブル）・傾き・ライトは両方で共通。B は SerializedObject でバインドしていないため、その編集・Undo は `ObjectChangeEvents.changesPublished`（instanceId 一致）で検知して描き直す。2 枚目は別の `MaterialThumbnailRenderer`（PreviewRenderUtility は 1 つにつき 1 枚のテクスチャしか持たないため）
> - **変換ウィンドウ内の見た目比較（2026-09-11）**: `MaterialConvertWindow` に「見た目の比較（A = 変換前 / B = 変換後）」を組み込んだ。B は `MaterialConverter.ApplyTo` + `MaterialSpecificResolver.Merge` をメモリ上の一時 `MaterialData`（HideAndDontSave）に適用したもので、**アセットを作る前から**変換先シェーダー・テーブルを変えながら見比べられる。「左右」/「切替」、形状（Model 以外）、回転、ライト、X/Y 反転、ドラッグ回転は Material Editor のサムネイルと同じ。変換元の編集・Undo は `ObjectChangeEvents` で拾って結果と B を作り直す。「Material Editor で比較」ボタンは廃止（`MaterialEditorWindow.OpenCompare` 自体は残す）
> - **変換ウィンドウの互換表（2026-09-11）**: 差分の行表示を「名前 / 型 / 値 / 互換（〇 × －）/ 備考」の表に変更。共通チャンネル（Albedo / AlbedoTint / Normal / NormalScale / Mask / Metallic / Smoothness / Emission / EmissionColor×Intensity / Blend / Cutoff / DoubleSided）は `MaterialCommonBinding.IsSupported(shader, CommonChannel)`（Apply と同じ候補名で判定）で変換先に受け口があるかを 〇 ×、変換元で未設定なら －。固有は Mapped / Kept = 〇、Dropped / Discarded = ×。さらに「変換先で追加される固有（既定値）」の節を出す
> - **Unity 標準 Material → D-Drive 標準シェーダーへの変換（2026-09-11）**: `Editor/Material/UnityMaterialMigrator.cs`。メニュー `Tools/D-Drive/Generate/選択した Material を D-Drive/Lit・Unlit の MaterialData に変換`（Material アセット、または Renderer を持つ Prefab / モデルを選択。重複なし）。`ResolveTargetShader`: URP Lit / Simple Lit / Complex Lit / Baked Lit / Built-in Standard → `DDrive/Lit`、URP Unlit / Built-in Unlit 系 → `DDrive/Unlit`、未対応は警告して Lit。共通チャンネルは `MayaMaterialImporter.ImportMaterial`（Unity Material → MaterialCommon + TextureData 確保）で写し、変換先の固有は既定値で登録したうえで元 Material に同名プロパティがあれば値を引き継ぐ（`CopySpecificValues`。例: `_OcclusionStrength`）。`SourceMaterial = "UnityMaterial/<名前>"` で同定するので再実行は Common だけ更新（固有調整は保持）。カテゴリは省略時にアセットの親フォルダ名。既に MaterialData になっているものを URP Lit → DDrive/Lit にしたい場合は Material 変換ウィンドウで変換先に `DDrive/Lit` を選ぶ（プロパティ名が同じなので固有は同名維持、テーブル不要）。テスト: `UnityMaterialMigratorTests`
> - **aiStandardSurface 対応シェーダー + FBX 前処理（2026-09-11、3-20）**: Maya の標準材質 aiStandardSurface を D-Drive フォーマットで受ける。(1) `Assets/SourceAssets/Shaders/AiStandardSurface/DDrive_AiStandardSurface.shader`（`DDrive/AiStandardSurface`。URP Lit のパス構成を土台に `DDrive_AiStandardSurfaceInput.hlsl`（UnityPerMaterial に Arnold パラメータを追加、常時 specular ワークフロー + ClearCoat）と `DDrive_AiStandardSurfaceForwardPass.hlsl`（sheen 項を追加）を自前化。共通チャンネルは D-Drive 規約名、固有は `_BaseWeight` / `_DiffuseRoughness` / `_MetalnessMap` / `_SpecularWeight` / `_SpecularColor(+Map)` / `_SpecularRoughnessMap` / `_SpecularIOR` / `_SpecularAnisotropy` / `_CoatWeight` / `_CoatColor` / `_CoatRoughness` / `_CoatIOR` / `_SheenWeight` / `_SheenColor` / `_SheenRoughness` / `_Opacity(+Map)` / `_TransmissionWeight` / `_SubsurfaceWeight` / `_SubsurfaceColor` / `_ThinWalled`）。**リアルタイム近似**: 誘電体 F0 = ((IOR−1)/(IOR+1))² × weight × color、金属は baseColor、Smoothness = 1 − roughness、coat は URP ClearCoat（色は反射に乗算）、sheen は主光源のみの Charlie 風（Deferred では出ない）、opacity / transmission は alpha に畳む。diffuseRoughness / anisotropy / subsurface / coatIOR / thinWalled は値を保持するだけ（thinWalled は Cull Off に反映）。(2) `Editor/Material/AiStandardSurfacePreprocessor.cs`（`AssetPostprocessor.OnPreprocessMaterialDescription`、順序 −950 = URP の Arnold 前処理 −960 の後に上書き）。Unity と同じ `TypeId == 1138001` で Maya の aiStandardSurface を判定し、`AiStandardSurfaceMapper.Apply`（純関数。`IArnoldSource` で値取得を抽象化しテスト可能）で全属性を写す（テクスチャ接続はマップへ、`emission` は色に畳む、opacity の最小成分 / transmission > 0 / opacity マップで透過、thinWalled で両面）。(3) `MayaMaterialImporter.ResolveTargetShader`: Profile の TargetShader → 元 Material が `DDrive/` ならそのまま → `DDrive/Lit`。新規作成・Shader 未設定の再インポート時に固有を既定値で登録し `UnityMaterialMigrator.CopySpecificValues` で元 Material の同名値を引き継ぐ（Coat / Sheen / IOR 等が MaterialData.Specific に入る）。これにより Maya 由来の MaterialData は Shader=None ではなく DDrive/AiStandardSurface（aiStandardSurface 以外は DDrive/Lit）になる。テスト: `AiStandardSurfaceMapperTests` 6 件
> - **D-Drive 標準シェーダー（2026-09-11）**: `Assets/SourceAssets/Shaders/DDrive_Lit.shader`（`DDrive/Lit`）/ `DDrive_Unlit.shader`（`DDrive/Unlit`）。URP 17.3 の Lit / Unlit の Properties ブロックだけを命名規約の区分（`[Header(Common)]` / `[Header(Render State)]` / `[Header(Specific)]`）で並べ直したもので、パス本体は URP パッケージの hlsl を include するため描画は URP 標準と同一（URP を上げたら Properties 以外を追従させる）。Metallic ワークフロー固定。D-Drive の Specific は値を流すだけでキーワードを切り替えないため、キーワード依存の URP 機能（Specular ワークフロー / Parallax / Detail / Highlights・Reflections・ReceiveShadows の OFF / ClearCoat）は `[HideInInspector]` にして固有から外している（SRP Batcher 互換のため宣言は残す。Specific にキーワード対応を足したら公開する）。結果、`DDrive/Lit` の固有は `_OcclusionStrength` のみ、`DDrive/Unlit` は無し。`MaterialManager` の既定シェーダー（Shader 未設定時）は従来どおり URP Lit のまま（`Shader.Find` はビルドに含まれる保証が無いので、切り替えるなら Always Included Shaders への登録とセットで行う）
> - **レビュー対応（2026-09-11）**:
>   - **アルファ側のブレンドステート**: `MaterialCommonBinding.ApplyBlend` が `_SrcBlendAlpha` / `_DstBlendAlpha` / `_AlphaToMask` を設定していなかった。`DDrive/Lit`・`DDrive/Unlit` は URP 17.3 と同じく `Blend [_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]` と `AlphaToMask [_AlphaToMask]` で書くので、シェーダー既定の One/Zero・0 のままだと **Transparent が描画先のアルファを潰し**（サムネイル / アイコン PNG に穴が開く）、**Cutout の輪郭が URP Lit と変わる**。URP の `BaseShaderGUI.SetupMaterialBlendMode` と同じく `_SrcBlendAlpha = One` / `_DstBlendAlpha = Transparent ? OneMinusSrcAlpha : Zero` / `_AlphaToMask = (Cutout かつ Transparent でない) ? 1 : 0` を `HasProperty` 付きで設定する。テスト: `MaterialCommonBindingTests`
>   - **共通チャンネル名が Specific に紛れる問題**: `MaterialManager` は Common → Specific の順に流し込むため、`_Metallic` などの共通チャンネル名や描画ステート名が `Specific` にあると **Common の値を黙って上書き**する。`MaterialSpecificResolver.Merge` が `MergeReport.Conflict` に列挙し（**値は消さない** = データを失わない）、`MaterialDataValidator` が Warning、`MaterialSpecificSync.Sync` が警告ログ、Material Editor が「共通チャンネルの重複を削除」ボタン（`MaterialSpecificSync.RemoveConflicts`、Undo + SetDirty）を出す。判定は `MaterialSpecificResolver.IsConflicting(shader, property)`。**Validator は Runtime asmdef なので FixAction は持たない**（Editor 側の同ボタンが役割を担う）
>   - `MaterialEditorWindow.OpenCompare` を削除（変換ウィンドウ内比較の導入で呼び出し元が消えており、`CreateGUI` 前に呼ぶと NRE だった）。比較は「比較対象」フィールドか変換ウィンドウ内の A/B で行う
>   - Material Editor / ポップアップのウィンドウ状態（対象・ロック・形状・比較対象・比較モード・回転/傾き/ライト角・ターンテーブル）に `[SerializeField]` を付けてドメインリロードを跨いで残す。UI は `SetValueWithoutNotify` で復元する。サムネイル描き直しの間引きは [09] §8.1 参照
>   - テスト追加: `MaterialSpecificResolverTests`（`DDrive/Lit` の固有は `_OcclusionStrength` のみ / `DDrive/Unlit` は固有なし / Conflict 検出 / `RemoveConflicts` + Undo / Validator の Warning）、`MaterialThumbnailRendererTests` と `AssetIconServiceTests`（背景色だけの画像になっていないことをピクセルで確認）
> - 未実装: Skybox 切替、Material の生成 / 消滅イベント（AssetEvent は保持するが Manager は発火しない。必要になったら Apply / Replace を節目にする）

## A-3. Manager API

```csharp
public static class Mats
{
    public static Material Get(MaterialId id);                    // 共有インスタンス
    public static void Apply(Renderer r, int slot, MaterialId id);
    public static void Replace(MaterialId from, MaterialId to);   // シーン内一括
    public static MatHandle FadeTo(Renderer r, MaterialId to, float sec); // 溶け替え
    public static void SetGlobalParam(string name, ParamValue v);
}
```

- ランタイム変更は MaterialPropertyBlock 優先（インスタンス増殖防止）
- MaterialAnim は MaterialManager の Tick が一括駆動（Update を持つ MonoBehaviour を量産しない）

## A-4. エディタ / Validation

- プレビュー: 球 / 板 / Cube / 任意 ModelData に適用表示。Skybox・ライト切替。変換前後の並列比較
- Validation: Shader Missing (Error) / Common.Albedo 未設定 (Warning) / Normal がノーマルマップ設定でない (Error, FixAction=インポート設定修正) / 透過なのに RenderQueue が Opaque 帯 (Warning) / 変換で破棄される固有パラメータの一覧提示

---

# Part B — 画像（テクスチャ）

## B-1. 要件

- UI 用 / 3D モデルテクスチャの 2 系統
- UI 用: どの Canvas で使うか、拡縮可否
- 3D 用: 対象モデル、テクスチャタイプ、共通/固有の区別を見えるように

## B-2. データ構造

```csharp
public class TextureData : AssetDataBase
{
    public Texture2D Texture;
    public TextureUsage Usage;        // UI / Model

    [Header("UI")]
    public Sprite Sprite;             // Usage=UI
    public CanvasId[] UsedInCanvas;   // 参照は依存グラフから自動収集(手入力不要)
    public bool AllowScale;           // 拡縮OKか(9-slice未設定の警告に使用)
    public Vector4 SliceBorder;       // 9-slice

    [Header("Model")]
    public TextureChannel Channel;    // Albedo/Normal/Mask/Emission (=MaterialCommonと対応)
    public ModelId[] UsedInModels;    // 自動収集
}
```

- `UsedInCanvas / UsedInModels` は保存フックで依存グラフ（[02] §12）から逆引きして自動記入。**手動管理させない**
- Channel は MaterialCommon のチャンネル規約と 1:1 対応 → 「このテクスチャはどの用途か」が常に明示される

## B-3. インポート規約（TexturePostprocessor）

| 命名 | 自動設定 |
|---|---|
| `*_N` | NormalMap / sRGB off |
| `*_M` | Mask / sRGB off / 圧縮 BC7 |
| `*_UI` | Sprite / Mipmap off / FullRect |
| `T_*` | Model 用既定 |

- 規約は `TextureImportProfile` で編集可能。違反は Validation で検出（FixAction=再インポート）

## B-4. Validation

| 検査 | 重度 |
|---|---|
| Texture Missing | Error |
| Usage=UI で Sprite 未生成 | Error |
| AllowScale=true で SliceBorder 未設定 | Warning |
| Channel=Normal で sRGB on | Error (FixAction) |
| 非 POT サイズ（Model 用） | Warning |
| 最大サイズ超過（プロファイル規定） | Warning |

### 実装メモ（2026-09-10、3-8）

> - **`TextureImportProfile`**（`Editor/Material/TextureImportProfile.cs`）: `Create > D-Drive/Material/Texture Import Profile` で作成できる ScriptableObject。`Rules[]`（`Name` / `Match`(Suffix・Prefix・Contains) / `Pattern` / `Type` / `SRgb` / `Mipmaps` / `Compression` / `MaxSize` / `SpriteFullRect` / 既定の `Channel` / `Usage`）+ `Enabled` + `IncludePathContains`（既定 `Assets/SourceAssets`, `Assets/GameData`）+ `ModelMaxSize`（既定 2048。Model 用の最大サイズ Warning 用、0 で検査しない）。プロジェクトに置かれていなければ組み込みの既定ルール(`DefaultRules()`: `_N`→NormalMap / `_M`→Mask / `_E`→Emission / `_UI`→Sprite(FullRect) / `T_*`→Model 既定)をメモリ上で使う。`FindOrDefault()` は Tests 配下の Profile を無視する。`AppliesTo(path)` は DDrive 本体・Tests・Packages を常に除外し、`IncludePathContains` のいずれかに一致するパスだけを対象にする。`TryMatch` は上から順に最初に一致したルールを返す。`Apply(importer, rule)` はルールを Importer に書き込み(変更有無を bool で返す。`SaveAndReimport` は呼び出し側の責務)、`Diff(importer, rule)` は食い違いを文字列リストで返す(Validation 用)
> - **`TexturePostprocessor`**（`Editor/Material/TexturePostprocessor.cs`）: `AssetPostprocessor.OnPreprocessTexture` で `FindOrDefault → AppliesTo → TryMatch → Apply` を実行する。`Apply` はインポート設定への書き込みのみで `SaveAndReimport` は呼ばない(このインポート自体に反映される)。テストが通常経路を止められるよう `public static bool Suppress` を持つ
> - **`TextureDataValidator`**（`Editor/Material/TextureDataValidator.cs`、`IValidator`、`Target=AssetType.Texture`）: B-4 の表の検査に加え、Importer が取得できる場合は `TextureImportProfile` との `Diff` を Warning として出す(FixAction=`Apply`+`SaveAndReimport`)。FixAction: 「UI で Sprite 未生成」は Importer を Sprite化 → `SaveAndReimport` → `LoadAssetAtPath<Sprite>` を `Undo.RecordObject`+`EditorUtility.SetDirty` で `TextureData.Sprite` に代入。「Channel=Normal で NormalMap でない」「Channel=Mask で sRGB on」はそれぞれ Importer のプロパティを直して `SaveAndReimport`。Texture がメモリ上だけ(Importer が取れない)ときは Importer 系の検査をスキップする(例外にしない)
> - **エディタ**: `MaterialEditorWindow` の TextureData 分岐に、Importer の現況(Texture Type / sRGB / Mipmap / 圧縮 / Max Size)と一致した規約名(未一致なら「規約に該当なし」)を表示するラベルと、食い違いがあるときだけ出る「命名規約を適用して再インポート」ボタンを追加
> - **Usage / Channel / SliceBorder の Importer 自動反映（2026-09-11、人による確認の指摘）**: `Editor/Material/TextureDataImporterSync.cs`。それまで Usage / Channel は Validation の Fix を押したときだけ Importer に反映され、`SliceBorder` はどこにも使われていなかった。今は TextureData の変更（Inspector / Material Editor / Undo。`ObjectChangeEvents` で検知、delayCall で 1 回にまとめる）で `Apply(data)` が走り、Usage=UI → Sprite(Single) + `spriteBorder = SliceBorder`（9-slice の境界 L,B,R,T。トリミングではない）+ Sprite 未設定なら割り当て、Channel=Normal → NormalMap、Mask → sRGB off / Albedo・Emission → sRGB on、Normal 以外で NormalMap/Sprite なら Default に戻す。**ファイル名規約（`TexturePostprocessor` が再インポートごとに適用）と食い違う設定は書かず警告する**（書いても次のインポートで戻されて打ち消し合うため。ファイル名か Data を合わせてもらう）。TextureType を Data に持たせる案は不採用（Usage / Channel から決まる。手動調整は Unity 標準の Importer Inspector）。`AutoApply` でテスト時に止める。テスト: `TextureDataImporterSyncTests` 5 件
> - **Substance Painter の標準エクスポート名を既定規約に追加（2026-09-11）**: `TextureImportProfile.DefaultRules` の先頭に、Painter の `$mesh_$textureSet_<channel>` 由来の接尾辞を登録。法線 `_Normal` / `_Normal_OpenGL` / `_NormalMap` → NormalMap、`_Normal_DirectX` → NormalMap + **緑反転**（Rule に `FlipGreenChannel` を追加し、`TextureImporter.flipGreenChannel` を Apply / Diff で扱う。Unity は OpenGL 形式のため）。パック済み `_MaskMap` / `_MetallicSmoothness` / `_SpecularSmoothness` → リニア・Channel=Mask。単チャンネル `_Metallic` / `_Roughness` / `_Smoothness` / `_AmbientOcclusion` / `_Ambient_occlusion` / `_Occlusion` / `_AO` / `_Height` / `_Opacity` → リニア・Channel=Other（Mask に詰め直す前提）。カラー `_BaseMap` / `_BaseColor` / `_Base_Color` / `_AlbedoTransparency` / `_Albedo` / `_Diffuse` → sRGB・Albedo、`_Emission` / `_Emissive` → sRGB・Emission。照合は大文字小文字を区別しない。D-Drive 短縮規約（`_N` 等）は後ろに置いてあり競合しない。既にプロジェクトに `TextureImportProfile` アセットがある場合はその Rules が使われるので、組み込み既定を取り込むには Rules を `DefaultRules()` で作り直す必要がある。テスト: `TextureImportRulesTests`（TryMatch 10 ケース + DirectX 反転の Apply）
> - 未実装: `UsedInCanvas` / `UsedInModels` の自動収集([02] §12 の依存グラフが未実装のため。実装され次第、保存フックで逆引き記入する)

### レビュー対応（2026-09-11、インポート / 変換）

> Maya / aiStandardSurface インポート・Unity Material 変換・Substance 命名・TextureData の Importer 同期に対するコードレビュー指摘の反映。
>
> - **Substance の `_MetallicSmoothness` / `_SpecularSmoothness` は Channel=Other に変更**（`TextureImportProfile.DefaultRules`）。D-Drive の Mask は R=Metallic / G=Occlusion / A=Smoothness で、`MaterialCommonBinding` は同じテクスチャを `_OcclusionMap` にも割り当てて `_OCCLUSIONMAP` を on にする。Unity 5 テンプレートのこの 2 枚は G=0 のため、Mask として使うと遮蔽 0 = 間接光が消える。`_Metallic` / `_Roughness` と同じく「Mask に詰め直す前提」の Other 扱いにした（`_MaskMap` は従来どおり Mask）
> - **`SourceMaterial`（再インポート時の同定キー）に元アセットの GUID を挟む**: `<SourceKey>/<GUID>/<マテリアル名>`（`MayaMaterialImporter.BuildSourceMaterial`）。名前だけだと、別フォルダの同名 Material や同名 FBX が 1 つの MaterialData を奪い合って上書きし合っていた。旧形式 `<SourceKey>/<名前>` も `FindExisting` が探し、見つかったら `Undo.RecordObject` + `SetDirty` で新形式へ書き換えて移行する
> - **同定の検索を 1 回にまとめた**: `FindExisting` / `EnsureTextureData` はマテリアル・テクスチャ 1 件ごとに `AssetDatabase.FindAssets` を呼んでいた。`MayaMaterialImporter.BeginBatch/EndBatch`（`ImportModel` と Material 一括変換が囲む）の間だけ `SourceMaterial → MaterialData` / `Texture → TextureData` の辞書を作って使い回す。検索は `AssetSearch.FindAssets` 経由（[12] §3 / [09] §9）。一括中に作った Data は辞書へ直接足す
> - **`AiStandardSurfacePreprocessor` に `GetVersion()` とシェーダー依存**: `GetVersion() => 1`（写像を変えたら上げる = 対象 FBX が再インポートされる）と `context.DependsOnSourceAsset(DDrive_AiStandardSurface.shader)` を追加。クリーンインポートで `Shader.Find` が null になっても、シェーダーが入った時点で FBX が処理し直される（null のときは従来どおり警告してフォールバック）
> - **`TextureDataImporterSync` が編集のたびに再インポートしない**: 望ましい Importer 設定を先に求め、1 つでも違うときだけ書く。`SliceBorder` はテクスチャサイズに丸めてから比べる（`ClampBorder`。丸めずに書くと Importer 側で切り詰められて毎回食い違い続けた）。ファイル名規約との食い違い警告は 1 アセットにつき 1 回だけ出す（プロジェクト変更・ドメインリロードで復帰）。変更の適用は `MarkPending` → `ProcessPending`（テストから直接呼べる）
> - **`MaterialConvertWindow` の後始末**: `EditorApplication.delayCall += Refresh` がウィンドウを閉じた後に走り、持ち主のいない `HideAndDontSave` の一時 MaterialData を作り直していた。`OnDisable` で `delayCall -= Refresh` と `_disposed` を立て、`Refresh` は破棄済みなら何もしない。未使用フィールド `_lastCreated` を削除
> - **`AiStandardSurfaceMapper` のアルファ**: `Color.gray * 1.6f` / `Color.white * emission` は色の乗算でアルファまで掛かり、`AlbedoTint.a = 1.6`（`Alpha()` が 1 を超えて Cutout が効かない）になっていた。既定は `new Color(0.8f, 0.8f, 0.8f, 1f)`、Emission は rgb だけ乗算する
> - **Arnold のテクスチャ入力をキーワード化**: `_METALNESSMAP` / `_SPECULARROUGHNESSMAP` / `_SPECULARCOLORMAP` / `_OPACITYMAP` を `shader_feature_local_fragment` として ForwardLit / GBuffer / Meta（`InitializeStandardLitSurfaceData` を通るパス）に追加し、Input.hlsl のサンプルを `#ifdef` で囲んだ。キーワードは `AiStandardSurfaceMapper` がテクスチャの有無で on/off する（`_NORMALMAP` / `_EMISSION` と同じ）。プロパティ宣言は SRP Batcher のため CBUFFER に残したまま。ForwardPass は `GetMainLight` を 1 回だけ取って sheen で使い回す。`outSurfaceData.metallic` は定数 1 ではなく `_Metallic × metalness map`（`_SPECULAR_SETUP` なのでデバッグ表示用）。キーワード off のときの見え方は従来の既定テクスチャ（Metalness=white / Roughness=black / SpecColor=white / Opacity=white）と同じ。**この変更より前に作られた Material はキーワードが立っていないため、該当マップが効かない**。FBX は `GetVersion` を上げたので再インポートで直る（手で作った Material は Mapper を通し直すか、Inspector でキーワードを有効にする）
> - **`CopySpecificValues(source, data, recordUndo)`**: `AssetCreationService.Create` の `configure` は `CreateAsset` の**前**に呼ばれる（まだアセットではない）ため、新規作成経路は `recordUndo: false` で呼ぶ。既存 Data の更新経路は従来どおり Undo に積む
> - テスト: `TextureImportRulesTests`（規約の並び順を `TestCase` 化 + `_MetallicSmoothness` の Channel）、`TextureDataImporterSyncTests`（`ClampBorder` と「適用 → 収束」）、`UnityMaterialMigratorTests`（固有件数を `MaterialSpecificResolver.Resolve` と比較 / 別フォルダの同名 Material / 未対応シェーダーの Lit フォールバック）、`AiStandardSurfaceMapperTests`（アルファとキーワード）
