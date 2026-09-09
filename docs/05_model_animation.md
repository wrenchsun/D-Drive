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

> **実装メモ(2026-07-27, Phase 2 時点)**: `MaterialSlot.Material`(AssetId&lt;MaterialMarker&gt;)と `DefaultAnimation`(AssetId&lt;AnimMarker&gt;)は、参照先の MaterialData（[06] 3-5）・AnimManager（3-1）が Phase 3 でしか実装されないため、現時点では **ID の保存・Inspector 編集・Validator 検査のみ**が完成している。`Models.SetMaterial` を呼んでも実際のレンダラーへの反映は行われず(開発ビルドでは警告ログを出す)、`PlayAnim` 相当の API もまだ提供していない。Phase 3 完了後、両 Manager をここに繋ぎ込むだけで動く設計にしてある。

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

> **実装との対応**: 実装済みシグネチャは `ModelsManager.SetMaterial(Handle, int slotIndex, AssetId&lt;MaterialMarker&gt;)`(slotLabel ではなく Slots 配列の index。RendererPath+SlotIndex の組がそのままスキーマなため)。`PlayAnim` は AnimManager 未実装のため未提供（Phase 3 で追加）。`GetGameObject(Handle)` も追加済み（プレビュー用）。
>
> **2026-09-09（レビュー対応）**: (1) `SetMaterial` は共有 `ModelData.Slots` を書き換えず、Instance 側の `Materials[]`（初期値 = `Slots[i].Material`）に保持する。現在値は `TryGetMaterial(h, slot, out id)`。(2) Instance は自分の Animator で再生した Anim Handle を所有し（`DefaultAnimation` / `PlayAnim` の両方）、`Despawn` で所有分を `Stop`、さらに `AnimManager.StopAllFor(animator)` で外部が `Anim.Play` した分も中断してからプールへ返す。プール再利用時に旧アニメの時間・イベント・BlendShape / IK が残らない。`GetOwnedAnimCount(h)` で確認できる。テスト: `AnimLifetimeReviewTests`

## A-4. プレビュー / 運用 / Validation

- プレビュー: ターンテーブル回転(自動回転トグル+速度) / 複数モデル並列表示(最大4体、横に並べて配置) / 背景色・ライト強度切替 / Material スロットの ID 差し替え(Slots を PropertyField で編集。実際の見た目反映は Phase 3 完了後) / DefaultAnimation は情報表示のみ(再生確認は Phase 3 の AnimManager 実装後)
- 運用: モデラーが FBX→Prefab 化 → AssetBrowser で登録 → Slots 自動収集ボタン（Prefab の Renderer を走査して Slot リストを生成、既存の Material 割当は RendererPath+SlotIndex が一致する分だけ保持）→ MaterialId を割当
- Validation: Prefab Missing (Error)、Animator はあるが Avatar 未設定 (Error。Animator を持たない静的モデルは対象外)、Slot の RendererPath 不整合 (Error)、Material 未割当 Slot (Warning)、Prefab のマテリアルのシェーダーが現在のレンダーパイプラインと非互換 (Error。VfxDataValidator と共通の `ShaderPipelineAnalyzer` を使用、[04] §7参照)
- Skybox・Post Process 切替は見送り（`RenderSettings` がプロジェクト全体で共有されるため、実シーンへの副作用を避けた）

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

> **実装メモ(2026-09-08, 3-1)**: `Runtime/Anim/AnimData.cs` / `AnimManager.cs` / `AnimatorProxy.cs` / `Anim.cs`(ファサード + `AnimHandleExtensions`) / `AnimDataValidator.cs`。
>
> **2026-09-09（レビュー対応）**: `AnimatorProxy` は Layer ごとに再生中の Data / 正規化時間を持ち（`GetActiveData(layer)` / `GetNormalizedTime(layer)`）、`OnAnimatorIK(layerIndex)` はその Layer の Data だけを見る（複数 Layer 同時再生で IK が上書きされない。BlendShape は各再生が自分のトラックを書くため、同じ ShapeName を複数 Layer で同時に使うと後勝ち）。`SetSpeed` / Pause で書いた `Animator.speed` は解放時に戻す（同じ Animator の別再生が残ればその速度、無ければ 1。触っていなければ何もしない）。`Tick` は 1 回の dt が複数周回分でも通過した周回数だけ `OnLoop` と Frame/Time を処理する（フレーム落ち・復帰直後・倍速で欠落しない）。`StopAllFor(animator)` を追加（ModelsManager.Despawn が使う）
> - 経過時間は Manager が自前で追跡する(`Animator` の状態を読まない)。`LengthSec` は Clip 長、Clip 無し(Placeholder)は 0.5 秒。`GetNormalizedTime` / `GetLoopCount` / `SetSpeed`(Animator.speed にも反映)
> - トリガの対応: Play → `OnSpawn` + `OnEnable` / 周回 → `OnLoop`(Frame/Time は `EventBus.ResetOnce` で毎周再発火) / 自然終了 → `OnDisable` + `OnDestroy` / `Stop` と同じ Animator + Layer への別 Play による中断 → `OnDisable` のみ(設計書の OnInterrupted に相当。`EventTrigger` に種別は増やさない)
> - Frame/Time はゲームのフレーム数ではなく **クリップ時間**で判定する(`EventBus.TickAnimation(ctx, clipTime, frameRate)` を追加。Frame = `Time / Clip.frameRate` 秒)。終端(Clip 長ちょうど)のイベントは終了直前に一度だけ拾う
> - CrossFade は `Animator.HasState` で確認してから `CrossFadeInFixedTime`。Controller 無し / ステート無しは警告 1 回で時間追跡とイベントだけ続ける(例外で止めない)。`Stop(fade)` の fade は Controller の Exit 遷移に任せるため未使用
> - `AnimatorProxy` は Play 時に自動アタッチ。`OnAnimatorIK` で `IkProfile`(手足 on/off + 正規化時間→ウェイトカーブ)を `AnimatorProxy.*Target` に適用、`ApplyBlendShapes` で `BlendShapeTrack` を `SkinnedMeshRenderer` に反映(3-2 の範囲もここで実装済み)
> - `Mask`(AvatarMask)は情報用。CrossFade 単位では適用できず、Controller のレイヤー設定で使う
> - `h.OnEnd(callback)` は用意しない(クロージャ禁止)。終了通知は `OnDestroy` / `Custom` の AssetEvent を購読する
> - Validator: Clip Missing / ステート名解決不可 / Frame・Time が Clip 長超過 → Error、Loop=false で OnLoop・ShapeName 空・CrossFade > 2s → Warning。「StateName が Controller に無い」「BlendShape 名がモデルに無い」は AnimEditor(3-3)の実行時検査
> - `Models.PlayAnim` の繋ぎ込み(ModelsManager → AnimManager)と AnimEditor は 3-3 で行う

## B-4. 専用エディタ（AnimEditor）

| 機能 | 内容 |
|---|---|
| タイムライン | Clip をシークバー表示。イベントマーカー（SE/VFX）を D&D で配置 |
| モデル選択 | 任意の ModelData を読み込んで再生確認 |
| ブレンド確認 | 2 つの AnimData を選び CrossFade 時間を変えながら遷移を再生 |
| Mask/IK 確認 | AvatarMask 適用結果、IK ウェイトカーブの効果を表示 |
| 同時プレビュー | イベントに設定した SE・VFX を実 Manager 経由で同時再生 ★ |
| BlendShape 編集 | ShapeName をモデルから選択、カーブエディタでウェイト編集 |

> **実装メモ(2026-09-09, 3-3/3-4)**: `Editor/Anim/AnimEditorWindow.cs`（`Tools > D-Drive > Editors > Animation (3D)`）。
> - プレビューは `PreviewService` のプレビューシーン（`ModelEditor` と同じ実 ModelsManager + オービットカメラ）。「確認用モデル」に ModelData を入れると配置し、その Animator に対して実 `AnimManager` で再生する。EditMode では `AnimManager` が Controller ありなら `Animator.Update(dt)`、無しなら `Clip.SampleAnimation` でポーズを進める（PlayMode では Unity 任せ）
> - タイムライン: 0.5 秒目盛り、再生ヘッド、Frame/Time イベントのマーカー（PlayAsset=橙、他=水色、Clip 長超過=赤）。**クリックでシーク**（`AnimManager.Seek`: イベントは発火せず、その時刻以前を発火済みに揃える = `EventBus.SeekAnimation`）、**マーカーのドラッグで Time を変更**（Frame はフレーム単位、Time は 0.01 秒単位。Undo 対応）
> - ブレンド確認: B（遷移先）と CrossFade 秒を指定し「A → B を再生」で A の 50% で B へ遷移
> - 同時プレビュー（3-4）: `Runtime/Presentation/AssetEventDispatcher.cs` が `EventBus.OnEventFired` を購読し、`Action=PlayAsset` の Target 種別に応じて AudioManager / VfxManager / AnchorGroupPlayer へ配送する（contextRoot = 発火元 Animator の Transform → SE/VFX の Anchor がそのモデルの階層から解決される）。PreviewService がこれを組み込んでいるので、イベントに設定した SE/VFX はプレビュー中に実際に鳴る/出る。発火順は「イベントログ」に表示
> - Mask/IK: 情報表示（IK ターゲットは `AnimatorProxy` の *Target に設定。ウィンドウからの配置 UI は未実装）。BlendShape 名は確認用モデルの SkinnedMeshRenderer から列挙して表示
> - Validation: 静的 `AnimDataValidator` に加え、確認用モデルに対する実行時検査（StateName が Controller に無い = Error、BlendShape 名がモデルに無い = Warning）
> - **シーン(SceneView)で再生**（2026-09-09、`Editor/Anim/SceneAnimPreviewDriver.cs`。同日中にウィンドウ内ビューポートは廃止し、VFX Editor と同じ SceneView 方式のみに統一）: 開いているシーン / プレハブモードのモデルをその場で実 AnimManager で動かし SceneView で確認する。対象の Animator は自動で決まる — 「確認用シーンを開く」→ 確認用 ModelData を `[D-Drive] Anim Preview` ルート（DontSave）に配置して対象にする、「モデル Prefab を開く」/ プレハブモードに入る → その Prefab の Animator を対象にする。Hierarchy の別モデルを「選択から取得」で指定してもよい。モデル情報は 1 行の要約 + 折りたたみの詳細（BlendShape 一覧）。借用した Animator は再生前の全 Transform / BlendShape ウェイトをスナップショットし、停止・対象解除・ステージ切替・Prefab 保存の直前に復元する（AnimManager が付ける `AnimatorProxy` も DontSave にして解除時に外す）。イベントの SE / VFX は `AssetEventDispatcher` → 自前の AudioManager / `SceneVfxPreviewDriver.Adopt` でシーンに出る。ツールバーに「確認用シーンを開く」「モデル Prefab を開く」を追加
> - `Models.PlayAnim(h, animId)` / `ModelsManager.PlayAnim` / `GetAnimator` を追加。`ModelData.DefaultAnimation` は AnimManager 接続時に Spawn 直後に自動再生。起動コードでは `new ModelsManager(pool, registry, animManager)` または `SetAnimManager` で接続する
> - `AssetEventDispatcher` はランタイムでも使う想定（起動コードで `new AssetEventDispatcher(animManager.Events, registry, audio, vfx, animManager.GetContextTransform, groups)`）。Presentation（Phase 5）が出来たら標準配線に統合

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
