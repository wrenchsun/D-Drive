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
> - 未実装: MaterialEditor の球 / 板 / 任意 ModelData 切替・Skybox・変換前後比較（3-9）、Material の生成 / 消滅イベント（AssetEvent は保持するが Manager は発火しない。必要になったら Apply / Replace を節目にする）

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
> - 未実装: `UsedInCanvas` / `UsedInModels` の自動収集([02] §12 の依存グラフが未実装のため。実装され次第、保存フックで逆引き記入する)
