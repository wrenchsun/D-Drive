# 05. モデル / アニメーション 詳細設計

関連: [02_core_framework.md](02_core_framework.md) / [03_audio.md](03_audio.md) / [04_vfx.md](04_vfx.md)

---

# Part A — モデル

## A-1. 要件

- Prefab メインで管理、マテリアル差し替え対応
- プレビュー（複数同時表示・背景/シェーダーと合わせた確認）

## A-2. データ構造

```csharp
public class ModelData : AssetDataBase
{
    public GameObject Prefab;
    [Header("Material")]
    public MaterialSlot[] Slots;         // Renderer名 + slotIndex → MaterialId
    [Header("Animation")]
    public AnimId DefaultAnimation;      // 任意
    public Avatar Avatar;                // Humanoid の場合
    [Header("Render")]
    public int RenderLayer;
    public uint LightLayerMask;
    public LodProfile Lod;               // 任意
}

[Serializable]
public struct MaterialSlot
{
    public string RendererPath;      // Prefab 内 Renderer への相対パス
    public int SlotIndex;
    public MaterialId Material;      // ★MaterialData の ID で参照（直参照しない）
}
```

- マテリアルを ID 参照にすることで、Material 差し替え・スキン替えがデータだけで完結（[06] と連携）

## A-3. Manager API

```csharp
public static class Models
{
    public static ModelHandle Spawn(ModelId id, Vector3 pos, Quaternion rot);
    public static ModelHandle Spawn(ModelId id, Transform parent);
    public static void Despawn(ModelHandle h);
}
// Handle 操作
h.SetMaterial(slotLabel, MaterialId);   // スキン替え
h.PlayAnim(AnimId);                      // AnimManager へ委譲
h.SetLayer(int);
```

## A-4. プレビュー / 運用 / Validation

- プレビュー: ターンテーブル回転 / 複数モデル並列表示 / 背景・Skybox・ライト切替 / Material スロットをその場で差し替え / DefaultAnimation 再生
- 運用: モデラーが FBX→Prefab 化 → AssetBrowser で登録 → Slots 自動収集ボタン（Prefab の Renderer を走査して Slot リストを生成）→ MaterialId を割当
- Validation: Prefab/Avatar Missing (Error)、Slot の RendererPath 不整合 (Error)、Material 未割当 Slot (Warning)

---

# Part B — アニメーション（3D）

アニメーションは **3D（本 Part B）と 2D スプライト（Part C）に分離**する。ID 型も `AnimId`（3D）/ `Anim2DId` を分け、Manager・エディタも別系統とする（データ構造・パイプラインが本質的に異なるため）。

## B-1. 要件

- StateMachine 前提の設計。モデル / ステート / ループ / 開始・常時・終了イベント / IK 設定
- ブレンドシェイプ対応（表情など）
- Animator 連携: 再生タイミングのイベント（Frame 指定で SE/VFX）をデザイナーが設定
- プレビュー: ブレンド時・遷移時の確認、SE・VFX と同時プレビュー

## B-2. データ構造

```csharp
public class AnimData : AssetDataBase
{
    public AnimationClip Clip;
    [Header("StateMachine")]
    public string StateName;             // AnimatorController 上のステート名
    public int Layer;
    public bool Loop;
    public float DefaultCrossFade = 0.1f;
    public AvatarMask Mask;              // 上半身のみ等
    [Header("IK")]
    public IkProfile Ik;                 // 手足IKのon/off・ウェイトカーブ
    [Header("BlendShape")]
    public BlendShapeTrack[] BlendShapes; // 時間→ウェイトのカーブ列
    // Events (基底) : Frame(15)→SE, Time(0.3)→VFX, OnEnd→...
}

[Serializable]
public struct BlendShapeTrack
{
    public string ShapeName;        // "MouthOpen" 等
    public AnimationCurve Weight;   // 正規化時間→0..100
}
```

設計方針:

- **AnimatorController は「土台」、AnimData は「再生単位」**。基本遷移（Idle/Walk 等）は Controller の StateMachine に持たせ、ワンショット（攻撃・被弾等）は AnimData 経由の CrossFade で再生する
- イベントは Unity の AnimationEvent ではなく **共通 AssetEvent**（[02] §3）を Manager が発火する。Clip にイベントを埋めない → Clip 差し替えでイベントが消えない・Validation 対象にできる

## B-3. Manager API

```csharp
public static class Anim
{
    public static AnimHandle Play(AnimId id, Animator target);        // CrossFade再生
    public static AnimHandle Play(AnimId id, Animator target, float fade);
    public static void SetTrigger(Animator target, string param);     // StateMachine遷移用
    public static void SetLayerWeight(Animator target, int layer, float w);
}
// Handle 操作
h.Stop(fade); h.SetSpeed(1.5f); h.NormalizedTime; h.OnEnd(callback);
```

内部実装:

- 対象 Animator ごとに `AnimatorProxy` コンポーネントを自動アタッチし、再生中 AnimData のイベント監視（Frame/Time トリガ）・BlendShapeTrack の適用・IK コールバック（OnAnimatorIK）を代行
- Frame イベントはループ時に毎周発火。遷移中断時は OnEnd の代わりに OnInterrupted を発火

## B-4. 専用エディタ（AnimEditor）

| 機能 | 内容 |
|---|---|
| タイムライン | Clip をシークバー表示。イベントマーカー（SE/VFX）を D&D で配置 |
| モデル選択 | 任意の ModelData を読み込んで再生確認 |
| ブレンド確認 | 2 つの AnimData を選び CrossFade 時間を変えながら遷移を再生 |
| Mask/IK 確認 | AvatarMask 適用結果、IK ウェイトカーブの効果を表示 |
| 同時プレビュー | イベントに設定した SE・VFX を実 Manager 経由で同時再生 ★ |
| BlendShape 編集 | ShapeName をモデルから選択、カーブエディタでウェイト編集 |

## B-5. 運用方法

1. アニメーターが Clip を作成 → AssetBrowser で AnimData 登録（StateName 割当）
2. AnimEditor でフレームイベント（Frame15→SE:Slash01、Frame18→VFX:SlashBlue）を設定
3. プレビューで実際のモデル + SE + VFX を同時確認
4. プログラマーは `Anim.Play(ANIMID.Attack01, animator)` のみ。ヒットフレーム通知が要る場合は `Custom("hit")` イベントを購読

## B-6. Validation

| 検査 | 重度 |
|---|---|
| Clip Missing | Error |
| StateName が Controller に存在しない | Error |
| Frame イベントが Clip 長を超過 | Error |
| BlendShape 名が対象モデルに無い | Warning |
| Loop=false なのに OnLoop イベントあり | Warning |

---

# Part C — 2D スプライトアニメーション

## C-1. 要件・方針

既存ツール `Katsuya.Tools.SpriteAnimation`（Grid/自動スライス/既存スプライトの 3 入力モード → 命名規則 SO → AnimationClip 生成 → BlendTree(2D Freeform Directional) 登録、イージング付きリタイミング、遷移シーケンスプレビューまで実装済み）を **D-Drive に統合・昇格**する。新規開発ではなく統合が中心。

- スプライト分割 → Clip 生成 → Animator/BlendTree 割当までワンストップ（既存機能を維持）
- 生成結果を `Anim2DData` として ID 登録し、他アセットと同じイベント・プレビュー・Validation に乗せる

## C-2. 統合内容（既存ツールとの対応）

| 既存 | 統合後 |
|---|---|
| EasingFunction / CubicBezierEvaluator (Editor asmdef) | Foundation の **EasingCore に昇格**（[15] §B-2）。リタイミング機能はこれを参照する形に変更 |
| SpriteAnimationNameData（命名規則 SO、固定パス） | `Anim2DImportProfile` として AssetData 化。固定パス廃止 → Registry 経由 |
| Clip 生成 + BlendTree 登録 | 維持。最終段に「Anim2DData 自動生成 + ID 発行」を追加 |
| ツール内プレビュー / 遷移シーケンスプレビュー | PreviewService（[09] §2）上に移植し、SE/VFX 同時プレビューを共通化 |
| AnimationSfxEditorUtility | 共通 AssetEvent（Frame→SE）に置換 |

## C-3. データ構造

```csharp
public class Anim2DData : AssetDataBase
{
    public AnimationClip Clip;            // ツールが生成した Sprite キー Clip
    public string StateName;              // AnimatorController 上のステート
    public int Layer;
    public bool Loop;
    public float FrameRate;               // 基準 12fps 等 (ImportProfile 既定)
    [Header("方向")]
    public DirectionSet Directions;       // None / Four / Eight (BlendTree x,y)
    public AnimationClip[] DirectionClips;// 角度順 (0/45/.../315)。ツールが自動登録
    [Header("リタイミング")]
    public ValueDef Retiming;             // フレーム配置カーブ（[17]。既存 Edit 機能を移植）
    // Events(基底): Frame(n)→SE/VFX (足音・ヒットフレーム等)
}
```

## C-4. Manager API

```csharp
public static class Anim2D
{
    public static Anim2DHandle Play(Anim2DId id, Animator target);
    public static Anim2DHandle Play(Anim2DId id, Animator target, Vector2 dir); // BlendTree x,y
    public static void SetDirection(Animator target, Vector2 dir);
    public static void SetSpeed(Anim2DHandle h, float speed);
}
```

- 方向付きは BlendTree パラメータ `x,y` を設定（既存 BlendTreeRegistrar の規約 `ParamXName="x"/"y"` を踏襲）
- Frame イベントは 3D と同じ AnimatorProxy 系で発火（実装共有）

## C-5. エディタ（Anim2DEditor = 既存 ToolWindow の移植 + 拡張）

既存の Create / Edit / Preview の 3 モード構成を維持し、以下を追加:

- 生成完了時に Anim2DData を自動作成し AssetBrowser に登録（ID 発行）
- タイムライン上で Frame イベント（SE/VFX）を D&D 設定 → 共通プレビューで同時再生
- 方向スプライトの一括処理（8 方向シートを一括スライス → 角度別 Clip → BlendTree 登録）
- Validation パネル統合

## C-6. Validation

Clip 未生成/Missing (Error) / Directions=Eight なのに DirectionClips 不足 (Error) / BlendTree に x,y パラメータ無し (Error, FixAction=追加) / FrameRate ≤ 0 (Error) / スライス済みスプライトの参照切れ（元テクスチャ再インポートで消失）(Error)
