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

- `AssetPostprocessor.OnPostprocessMaterial` で Maya 由来マテリアル（例: aiStandardSurface / Stingray PBS）のプロパティを規約に従って MaterialCommon にマッピングし、MaterialData + Unity Material を自動生成
- マッピング規約は `MayaImportProfile`（ScriptableObject）でデザイナーが編集可能（テクスチャ命名規則 `_BC/_N/_M` → チャンネル割当）
- 再インポート時は固有調整を上書きしない（Common のみ更新 / 差分レポート表示）

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
