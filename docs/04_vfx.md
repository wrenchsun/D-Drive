# 04. VFX 詳細設計

関連: [02_core_framework.md](02_core_framework.md) / [08_presentation.md](08_presentation.md)

---

## 1. 要件

- 本体 Prefab / Anchor / ループ・lifetime / 共通化引数（ラベル付き）/ 描画・ライトレイヤー / イベント / ポーズ挙動
- 生成・消滅・使い回し（Pool）・移動・アタッチを Manager が担当
- **UI パーティクルを通常パーティクルと同じ感覚で作れる**（外部ツール導入なし）
- Anchor はデータ経由で調整可能、プレビュー付き
- 複数同時再生・背景/シェーダーと合わせた調整プレビュー

## 2. データ構造

```csharp
public class VfxData : AssetDataBase
{
    public GameObject Prefab;                 // ParticleSystem / VFX Graph どちらも可
    [Header("Anchor")]
    public AnchorDef Anchor;                  // 下記
    [Header("Lifetime")]
    public VfxLifeMode LifeMode;              // OneShot / Loop / Duration
    public float Duration;                    // LifeMode=Duration
    public float FadeOutSec;                  // Stop時にパーティクル放出停止→残り待ち
    [Header("Render")]
    public RenderMode Render;                 // World3D / UIOverlay ★UIパーティクル
    public int RenderLayer;                   // Sorting/RenderingLayerMask
    public uint LightLayerMask;
    [Header("Parameters")]
    public VfxParam[] Params;                 // ラベル付き公開引数
}

[Serializable]
public struct AnchorDef
{
    public AnchorSpace Space;    // World / BoneName / NamedObject / ContextTarget
    public string Path;          // ボーン名 or オブジェクトパス
    public Vector3 LocalOffset;
    public Vector3 LocalEuler;
    public Vector3 LocalScale;
    public bool FollowRotation;  // アタッチ後、回転に追従するか
    public bool DetachOnStop;    // 親破棄時に切り離して残す（軌跡等）
}

[Serializable]
public struct VfxParam    // デザイナーが命名する公開引数
{
    public string Label;             // "MainColor", "Size", "Speed"...
    public VfxParamType Type;        // Float/Int/Color/Curve/Gradient/Texture/Vector
    public string TargetProperty;    // Shader プロパティ名 or VFXGraph exposed 名
    public ParamValue Default;
    public ValueDef Anim;            // 任意。時間変化させる場合の形 + スピード（[17]）
}
```

### Data / Instance の分離（重要）

Prefab/Loop/Lifetime/Anchor は **Data**。位置・速度・経過時間・アタッチ先は **VfxInstance**。Move/Destroy は Handle 経由で Instance に対して行う。

## 3. Manager API

```csharp
public static class Vfx
{
    public static VfxHandle Spawn(VfxId id);                       // Data.Anchor 通り
    public static VfxHandle Spawn(VfxId id, Vector3 pos, Quaternion rot);
    public static VfxHandle Spawn(VfxId id, Transform attach);     // Anchor を上書き
    public static VfxHandle Spawn(VfxId id, in PlayContext ctx);   // Presentation 用

    public static void Stop(VfxHandle h);          // FadeOut→Pool返却
    public static void Kill(VfxHandle h);          // 即時返却
    public static void Preload(params VfxId[] ids);
}
// Handle 操作
h.Move(pos); h.Attach(t); h.Detach(); h.SetParam("MainColor", color); h.SetSpeed(0.5f);
```

内部実装:

- 生成は PoolService.Rent。Return 時に `ParticleSystem.Clear()` / Trail リセット（IPoolable）
- Loop でない Instance は Tick で寿命監視 → 自動 Return
- `SetParam` は Params 定義を引いて MaterialPropertyBlock / VFXGraph SetXxx に反映（文字列引きは初回のみ、以後 ID キャッシュ）
- Pause: `ParticleSystem.Pause()` / VFX Graph は `pause=true`

## 4. UI パーティクル（RenderMode.UIOverlay）

外部アセット導入なしで、通常 VFX と同じデータ・同じエディタで作る。

- 方式: **専用 UI カメラ + RenderTexture 合成** を基本とする
  - `VfxUiLayer` レイヤーに Spawn し、UI 用オーバーレイカメラ（URP Camera Stack）で描画
  - Canvas の Sorting との前後関係は CanvasData 側の Layer 設定と対応表で管理
- Spawn 時は `RectTransform` 座標→ワールド変換を Manager が吸収。デザイナー・プログラマーは意識しない
  `Vfx.Spawn(id, uiElement.transform)` で動く
- 制約の明文化: UI マスク(RectMask2D)対象外。マスクが必要な演出のみ Mesh ベーカー方式を追加検討（将来拡張）

## 5. 専用エディタ（VfxEditor）

| 機能 | 内容 |
|---|---|
| ライブプレビュー | プレビューシーンで Spawn/Stop/速度変更。**実 VfxManager を駆動** |
| Anchor 編集 | プレビューモデル（任意選択）にボーン一覧を表示し、D&D で Anchor 設定。Offset/回転をギズモでドラッグ調整 → AnchorDef に保存 |
| 複数同時再生 | 最大 8 スロットに別 VFX を並べて同時再生（打撃+火花+煙の重なり確認） |
| 環境切替 | 背景（暗室/屋外/任意シーン）、Skybox、ライト強度、ポストプロセス ON/OFF |
| パラメータ即時反映 | Params のスライダ操作が再生中 Instance に反映 |
| UI モード | UIOverlay の VFX は擬似 Canvas 上でプレビュー |
| イベント編集 | OnSpawn/OnLoop/OnDestroy → SE 再生等 |

## 6. 運用方法

1. エフェクトアーティストが Prefab（ParticleSystem/VFX Graph）を作成
2. AssetBrowser →「新規 VFX」→ Prefab を D&D → LifeMode/Layer 設定
3. Anchor をプレビューで調整（キャラモデルを読み込み、右手ボーンに Offset を付ける等）
4. 公開したい調整値を Params に登録（"MainColor" 等ラベルを付ける）
5. プログラマーは `Vfx.Spawn(VFXID.SlashBlue, ctx)` だけ。色違いは `h.SetParam` か、色違い VfxData を複製して別 ID に

## 7. Validation

| 検査 | 重度 |
|---|---|
| Prefab 未設定 / Missing | Error |
| LifeMode=Loop かつ Pool 上限未設定 | Warning（リーク危険） |
| Params.TargetProperty が Prefab に存在しない | Error |
| Anchor.BoneName がプレビューモデルに無い | Warning |
| UIOverlay なのに Domain=Game3D | Warning |
| Stop 時 FadeOut > 10s | Warning |
