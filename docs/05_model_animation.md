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
    public uint LightLayerMask = 1;      // Rendering Layer Mask。0 = Prefab の設定を上書きしない(2026-09-17 改定)
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

> **実装メモ(2026-07-27, Phase 2 時点)**: `MaterialSlot.Material`(AssetId&lt;MaterialMarker&gt;)と `DefaultAnimation`(AssetId&lt;AnimMarker&gt;)は、参照先の MaterialData（[06] 3-5）・AnimManager（3-1）が Phase 3 でしか実装されないため、現時点では **ID の保存・Inspector 編集・Validator 検査のみ**が完成している。`Models.SetMaterial` を呼んでも実際のレンダラーへの反映は行われず(開発ビルドでは警告ログを出す)、`PlayAnim` 相当の API もまだ提供していない。Phase 3 完了後、両 Manager をここに繋ぎ込むだけで動く設計にしてある。**2026-09-10（3-5）**: `MaterialManager` を接続（コンストラクタ第 4 引数 / `SetMaterialManager`）。`Slots[].Material` は Spawn 時に `Mats.Apply` 相当で共有 Material が割り当てられ、`SetMaterial` も実際にレンダラーへ反映される。

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
> **2026-09-10（修正）**: `ModelData` の `[AssetIdDefinition]` が `AssetType.Prefab` のままだった（`AssetType.Model` 追加時の漏れ。`ValidatorRegistry` が属性の Type で Validator を選ぶため `ModelDataValidator` が走らず、PrefabData（4-4）と種別が衝突していた）→ `AssetType.Model` に修正。既存の `PREFAB_Player_Model` は `MODEL_Player_Model`（`GameData/Model/Player/`）へ移動・リネームし、PrefabCatalog から ModelCatalog へ登録し直した（参照は GUID なので Prefab / シーンの参照は影響なし）。

> **2026-09-09（レビュー対応）**: (1) `SetMaterial` は共有 `ModelData.Slots` を書き換えず、Instance 側の `Materials[]`（初期値 = `Slots[i].Material`）に保持する。現在値は `TryGetMaterial(h, slot, out id)`。(2) Instance は自分の Animator で再生した Anim Handle を所有し（`DefaultAnimation` / `PlayAnim` の両方）、`Despawn` で所有分を `Stop`、さらに `AnimManager.StopAllFor(animator)` で外部が `Anim.Play` した分も中断してからプールへ返す。プール再利用時に旧アニメの時間・イベント・BlendShape / IK が残らない。`GetOwnedAnimCount(h)` で確認できる。テスト: `AnimLifetimeReviewTests`

> **2026-09-10（Codex レビュー対応、P1）**: `Flags.Pool.Kind`（既定は `None`）は「プールしない」の意味だが、従来は `Despawn` が常に `IPoolService.Return` を呼んでいたため、None 指定でも Instance が Free に無限に貯まり続けていた（`MaxCount`/`Persistent` も効かない）。修正後は Instance 生成/親付け/上限管理は引き続き `IPoolService.Rent` 経由で統一しつつ、`Despawn` は `Kind == Pooled` のときだけ `Return`、`Kind == None`（既定）のときは新設の `IPoolService.Discard` で Active から取り除いて即破棄する（Play モードは `Object.Destroy`、Edit モードは `DestroyImmediate`）。PrefabsManager も同じ規則（[07] 実装メモ参照）。**VFX / SE は対象外**（VfxManager/AudioManager は短命で再生頻度が高いため、Kind に関わらず常にプールする設計を維持）。テスト: `ModelsManagerTests.Despawn_NonePolicy_DestroysGameObject_AndReuseGivesDifferentGameObject` / `Despawn_PooledPolicy_ReturnsToPool_AndReuseGivesSameGameObject`、`PoolServiceTests.Discard_*`

## A-4. プレビュー / 運用 / Validation

- プレビュー: ターンテーブル回転(自動回転トグル+速度) / 複数モデル並列表示(最大4体、横に並べて配置) / 背景色・ライト強度切替 / Material スロットの ID 差し替え(Slots を PropertyField で編集。実際の見た目反映は Phase 3 完了後) / DefaultAnimation は情報表示のみ(再生確認は Phase 3 の AnimManager 実装後)

> **2026-09-10 改定（SceneView 方式へ統一）**: `Editor/Model/ModelEditorWindow.cs` はウィンドウ内ビューポートと背景色・ライト切替を廃止し、AnimEditor と同じ `SceneAnimPreviewDriver` で **開いているシーン / プレハブモードに配置して SceneView で確認**する。ツールバー「確認用シーンを開く」= 確認用シーン（VFX と共通）を開いて対象を原点に配置、「Prefab を開く」= `ModelData.Prefab` をプレハブモードで開く（Renderer / Material をその場で編集。Ctrl+S で保存）。ターンテーブルは配置したモデルを回す（プレハブモードの実体は回さない）。並列表示は `SpawnExtraModel` で対象の隣に 2m 間隔。DefaultAnimation は配置時に実 AnimManager が自動再生（`EditorAnchorRegistry` に Anim / Model も登録するようにした）。配置物は DontSave で保存されない
- 運用: モデラーが FBX→Prefab 化 → AssetBrowser で登録 → Slots 自動収集ボタン（Prefab の Renderer を走査して Slot リストを生成、既存の Material 割当は RendererPath+SlotIndex が一致する分だけ保持、**未割当のスロットは Renderer が使っている Material から作られた MaterialData を自動で割当**）→ 必要なら MaterialId を差し替え
- Validation: Prefab Missing (Error)、Animator はあるが Avatar 未設定 (Error。Animator を持たない静的モデルは対象外)、Slot の RendererPath 不整合 (Error)、Material 未割当 Slot (Warning)、Prefab のマテリアルのシェーダーが現在のレンダーパイプラインと非互換 (Error。VfxDataValidator と共通の `ShaderPipelineAnalyzer` を使用、[04] §7参照)
- Skybox・Post Process 切替は見送り（`RenderSettings` がプロジェクト全体で共有されるため、実シーンへの副作用を避けた）

> **2026-09-17（不具合修正 U-1 / U-2 / U-3。[39](39_usability_fixes_2026-09-17.md)）**
>
> - **U-1「3D プレビューが全て透明」の真因は `LightLayerMask` の既定値 0**（Material スロットが None であることとは別の問題）。`ModelsManager.SpawnData` が `renderer.renderingLayerMask = data.LightLayerMask` を**無条件に**全 Renderer へ書いていたため、既定値 0 がそのまま入り、URP（SRP）の描画フィルタ（`FilteringSettings.renderingLayerMask` の既定は全ビット）とのビット積が 0 になってモデルが 1 つも描画されなかった。ライトレイヤーにも一致しないので、仮に描画されてもライトが当たらない。**VFX で 2026-09-08 に直したのと同じ不具合（[19](19_vfx_usability_review.md) B-1）が Model 側に残っていた**。`VfxData` と同じ規約に揃え、`ModelData.LightLayerKeepPrefab = 0`（= Prefab の Renderer 設定を上書きしない）を定義してフィールドの既定値を 1（Default）にした。既存アセットは 0 が保存されているので、そのまま「Prefab の設定を使う」に倒れて直る。テスト: `ModelsManagerTests.LightLayerMask_*`（3 件）
> - **U-2「Slot がすべて None」**: FBX インポート時に `MayaMaterialImporter` が `MaterialData` を作っても、それを `ModelData.Slots` に結び付ける経路がどこにも無かった（「Slot 自動収集」は `RendererPath` + `SlotIndex` を並べるだけで `Material` は常に None）。`Editor/Model/ModelSlotBinder.cs` を追加し、**Renderer の `sharedMaterials[slotIndex]` → その Material から作られた `MaterialData`** を `MaterialData.SourceMaterial`（既存の同定キー）で引いてスロットに入れる。同定キーは FBX 内蔵 Material なら `<FBX名>/<GUID>/<マテリアル名>`、単体 `.mat` なら `UnityMaterial/<GUID>/<名前>`（[06] A-2）。**既に有効な ID が入っているスロットは上書きしない**
> - **結び付けの入口は 3 つ**（いずれも既存経路に乗せる。新しい並行経路は作らない）: (1) `ImportRuleHandlers.ModelImportHandler.Configure`（`SourceAssets/Model/` への FBX 配置で `ModelData` を作るとき。MaterialData は同じ delayCall 列で先に走る `MayaModelPostprocessor` が作っているので、ここでは探して結び付けるだけ）(2) `MayaModelPostprocessor`（FBX インポート直後・メニュー `Generate/選択したモデルから MaterialData を生成` の直後に `ModelSlotBinder.RebindForModelPath` で、その FBX を使っている `ModelData` の Slots を貼り直す）(3) Model Editor の「Slot 自動収集」
> - **U-3「再読み込み」**: Model Editor の Material スロット欄に「元ファイル再読み込み」ボタンを追加。Prefab の Renderer が参照している FBX / `.mat` を洗い出し、`MayaMaterialImporter.ImportModel` / `UnityMaterialMigrator.Migrate`（= 既存の生成経路）で `MaterialData`（+ `TextureData`）を作り直してから Slots を貼り直す。再実行は `Common` だけ更新で `Specific` / `Anims` / `Render` は保持（[06] A-2 の再インポート規約と同じ）。見つからなかったスロットは None のまま残し、件数を Console に警告として出す（例外で止めない）。テスト: `ModelSlotBinderTests`（3 件）
> - ボタン行は横幅 500px で折り返す（[09] §7.1）。`ModelEditorWindow.minSize` も 520 → 500 に下げた

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
> **2026-09-09（レビュー対応）**: `AnimatorProxy` は Layer ごとに再生中の Data / 正規化時間を持ち（`GetActiveData(layer)` / `GetNormalizedTime(layer)`）、`OnAnimatorIK(layerIndex)` はその Layer の Data だけを見る（複数 Layer 同時再生で IK が上書きされない。BlendShape は各再生が自分のトラックを書くため、同じ ShapeName を複数 Layer で同時に使うと後勝ち）。`SetSpeed` / Pause で書いた `Animator.speed` は解放時に戻す（同じ Animator の別再生が残ればその速度、無ければ 1。触っていなければ何もしない）。`Tick` は 1 回の dt が複数周回分でも通過した周回数だけ `OnLoop` と Frame/Time を処理する（フレーム落ち・復帰直後・倍速で欠落しない）。`StopAllFor(animator)` を追加（ModelsManager.Despawn が使う）。`SetPaused(h, bool)` / `IsPaused(h)`（Handle 単位の一時停止。時間・イベント・ポーズ更新が止まり、Seek は効く。エディタのシーク用。2026-09-10）
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
| ブレンド確認 | ①遷移シーケンス（対象 → Step1 → Step2 …、遷移ごとの CrossFade 秒と切替位置、ループ）②レイヤー同時再生（別 Layer の AnimData を重ね、Controller のレイヤー重みスライダーで AvatarMask の効きを確認）③Blend Tree パラメータ（float を 2D パッド / スライダーで操作）。2026-09-10 高級化 |
| Mask/IK 確認 | AvatarMask 適用結果、IK ウェイトカーブの効果を表示 |
| 同時プレビュー | イベントに設定した SE・VFX を実 Manager 経由で同時再生 ★ |
| BlendShape 編集 | ShapeName をモデルから選択、カーブエディタでウェイト編集 |

> **実装メモ(2026-09-09, 3-3/3-4)**: `Editor/Anim/AnimEditorWindow.cs`（`Tools > D-Drive > Editors > Animation (3D)`）。
> - プレビューは `PreviewService` のプレビューシーン（`ModelEditor` と同じ実 ModelsManager + オービットカメラ）。「確認用モデル」に ModelData を入れると配置し、その Animator に対して実 `AnimManager` で再生する。EditMode では `AnimManager` が Controller ありなら `Animator.Update(dt)`、無しなら `Clip.SampleAnimation` でポーズを進める（PlayMode では Unity 任せ）
> - タイムライン: 0.5 秒目盛り、再生ヘッド、Frame/Time イベントのマーカー（PlayAsset=橙、他=水色、Clip 長超過=赤）。**クリックでシーク**（`AnimManager.Seek`: イベントは発火せず、その時刻以前を発火済みに揃える = `EventBus.SeekAnimation`）、**マーカーのドラッグで Time を変更**（Frame はフレーム単位、Time は 0.01 秒単位。Undo 対応）
> - **SE タブ由来の使い勝手**（2026-09-10、ITAMI の Sprite Animation Tool「SE タブ」から移植。`Editor/Anim/AnimEditorWindow.Source.cs`）: ①**Animator から選択** — 対象 Animator の Controller をスキャンして「レイヤー / ステート [クリップ]」の一覧を出し、選ぶと対応する AnimData を探す（Clip 一致 > StateName + Layer 一致）。無ければ「このクリップの AnimData を作成」で AssetBrowser の作成パイプライン（ID・カタログ・Addressables）を通して作り、そのまま対象にする ②**SE 波形** — タイムラインの SE マーカーから SE の長さぶん波形（`WaveformTextureCache`、`WaveformRenderer` 共用）を半透明で重ねる ③**SE / VFX イベント一覧** — 「＋ 現在位置に SE / VFX / 配置セット」、行ごとにアセットのドロップダウン（プロジェクト内の SeData / VfxData / AnchorGroupData）、Frame / Time 切替、フレーム・秒の直接入力、▶ 単体試聴（実 Manager、対象の位置から）、✕。中身は `AnimData.Events` の PlayAsset なのでデータ形式は不変、Undo 対応 ④**イベントのコピー** — 別の AnimData へ、または同じステートの他クリップ（方向違い等）の AnimData へ一括（無ければ作成）。取り込まなかったもの: クリップへの AnimationEvent 書き出し（D-Drive はクリップにイベントを埋めない方針）、SfxId enum / Receiver / AnimationMode サンプリング（ID / Manager / シーンドライバが同じ役割）
> - **イベントの繰り返し**（2026-09-10、`AssetEvent.Repeat`）: 行の「毎周回 / 1 回 / 再生中は維持」。ダッシュの土煙のようにループする追従 VFX は「再生中は維持」にすると 1 回だけ出て、モーションの終了・中断で止まる。イベント行の「↗」でそのアセットの専用エディタを開く（ツールチップにエディタ名）。**▶ で最初から再生し直すとき・■ 停止のときは、前回出した SE / VFX / 配置セットを全部消す**
> - 再生操作（2026-09-10 改定）: **■ 停止はポーズをその瞬間のまま残す**（初期ポーズに戻さない）。戻すのは「↺ ポーズを戻す」、対象解除、ステージ切替、Prefab 保存の直前。**タイムラインをクリックするとその位置で一時停止**（`AnimManager.SetPaused`）し、⏸ / ▶ で**続きから**再開（最初からにしない）。一時停止はプレビュー全体に効く: 同時再生中の他レイヤーの Anim、イベントで出た SE（`AudioManager.SetPausedAll`）/ VFX（`VfxManager.SetPausedAll` + `SceneVfxPreviewDriver.Paused` で EditMode の手動 Simulate も停止）/ 配置セットのディレイもまとめて止まる。ステータスは「⏸ 一時停止 xx%（Frame n）」。**一時停止の解除は Play 系の入口（Play / シーケンス / レイヤー同時再生）・停止・対象解除・ステージ切替・Dispose で一括**（`SceneAnimPreviewDriver.ClearPause` → `AnimManager.SetPausedAll(false)` 等。無効 Handle での一時停止は弾く。2026-09-10 レビュー対応）。マーカードラッグの Undo は MouseDrag ごとに `RecordObject` し MouseUp で 1 操作にまとめる
> - ブレンド確認（2026-09-10 高級化、`Editor/Anim/AnimEditorWindow.Blend.cs`）: ①**遷移シーケンス** — Steps（AnimData / CrossFade 秒 / SwitchAt=前の Clip の切替位置 0〜1、1 以上は終了時）を任意個並べ「▶ シーケンスを再生」。対象 → Step1 → … を実 AnimManager の CrossFade で再生し、ループ可。Step 1 つで従来の A → B と同じ ②**レイヤー同時再生** — 「一緒に再生する Anim」を対象と同時に `AnimManager.PlayData`（Layer は各 AnimData の値。同じ Layer は中断されるので警告）。Controller の各レイヤーに重みスライダー（`Animator.SetLayerWeight`、AvatarMask 名を表示。Layer 0 は固定） ③**Blend Tree パラメータ** — `Animator.parameters` の float をスライダー、選んだ 2 つを 2D パッド（範囲 -1〜1 / 0〜1 切替）で `SetFloat`。Int / Bool / Trigger も操作可。いずれも Animator への直接操作で Data は変えない。EditMode でも AnimManager が `Animator.Update` を回すので反映される
> - 同時プレビュー（3-4）: `Runtime/Presentation/AssetEventDispatcher.cs` が `EventBus.OnEventFired` を購読し、`Action=PlayAsset` の Target 種別に応じて AudioManager / VfxManager / AnchorGroupPlayer へ配送する（contextRoot = 発火元 Animator の Transform → SE/VFX の Anchor がそのモデルの階層から解決される）。PreviewService がこれを組み込んでいるので、イベントに設定した SE/VFX はプレビュー中に実際に鳴る/出る。発火順は「イベントログ」に表示
> - Mask/IK: 情報表示（IK ターゲットは `AnimatorProxy` の *Target に設定。ウィンドウからの配置 UI は未実装）。BlendShape 名は確認用モデルの SkinnedMeshRenderer から列挙して表示
> - Validation: 静的 `AnimDataValidator` に加え、確認用モデルに対する実行時検査（StateName が Controller に無い = Error、BlendShape 名がモデルに無い = Warning）
> - **シーン(SceneView)で再生**（2026-09-09、`Editor/Anim/SceneAnimPreviewDriver.cs`。同日中にウィンドウ内ビューポートは廃止し、VFX Editor と同じ SceneView 方式のみに統一）: 開いているシーン / プレハブモードのモデルをその場で実 AnimManager で動かし SceneView で確認する。対象の Animator は自動で決まる — 「確認用シーンを開く」→ 確認用 ModelData を `[D-Drive] Anim Preview` ルート（DontSave）に配置して対象にする、「モデル Prefab を開く」/ プレハブモードに入る → その Prefab の Animator を対象にする。Hierarchy の別モデルを「選択から取得」で指定してもよい。モデル情報は 1 行の要約 + 折りたたみの詳細（BlendShape 一覧）。借用した Animator は再生前の全 Transform / BlendShape ウェイトをスナップショットし、対象解除・ステージ切替・Prefab 保存・**シーン保存**（`EditorSceneManager.sceneSaving`。停止後に残したポーズは保存で元に戻る。再生中なら次の Tick で再サンプル）の直前に復元する（AnimManager が付ける `AnimatorProxy` も DontSave にして解除時に外す。解放時の `Animator.speed` は 1 ではなく触る前の値へ戻す）。EditMode で同じ Animator に複数インスタンス（レイヤー同時再生）があっても `Animator.Update` は Tick ごとに 1 回。イベントの SE / VFX は `AssetEventDispatcher` → 自前の AudioManager / `SceneVfxPreviewDriver.Adopt` でシーンに出る。配置セット（PlayAsset=AnchorGroup）は `AssetEventDispatcher.OnGroupPlayed` で台帳（`_groupHandles`）に載せ、その VFX を `AnchorGroupPlayer.CollectVfxHandles` で引き取る（KeepWhilePlaying なら Anim 終了で `Stop`。2026-09-10）。ツールバーに「確認用シーンを開く」「モデル Prefab を開く」を追加
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

### 実装メモ（2026-09-10、チケット 3-12: ランタイム側を先行実装）

> - `Runtime/Anim2D/Anim2DData.cs`: **`Anim2DData : AnimData`**（`AssetType.Anim2D` / `Anim2DMarker` / `ANIM2DID`）。時間追跡・Frame/Time/OnLoop イベント・CrossFade・EditMode サンプリングは AnimManager をそのまま使う（実装共有。Registry は `ResolveOrPlaceholder<AnimData>` で派生型も返す）。追加フィールド: `Directions`(None / Four / Eight) / `DirectionClips`（角度順） / `ParamXName`・`ParamYName`（既定 "x" / "y"） / `Retiming`(ValueDef。Clip 生成時に焼き込む。ランタイム未参照)
> - `Anim2D` ファサード（`Bind(AnimManager, IAssetRegistry)`、`Play(id, animator)` / `Play(id, animator, dir)` / `SetDirection(animator, dir, x, y)` / `SetSpeed` / `Stop` / `IsPlaying`）: 方向は正規化して BlendTree の float パラメータへ、0 ベクトルは無視、パラメータが無ければ何もしない。**Handle は 3D と同じ `Handle<AnimMarker>`**（設計の `Anim2DHandle` は AnimManager 共有のため同型にした）。`DDriveRuntimeBootstrap` が Bind
> - `Anim2DDataValidator`（C-6 の静的検査: Clip 未生成 / FrameRate / DirectionClips 不足・欠損 / パラメータ名空 / 未使用の DirectionClips）。BlendTree の x,y 有無（FixAction=追加）とスライス済みスプライトの参照切れは Editor 側（3-13）
> - テスト: `Anim2DTests`。3-11（既存ツール移植 + Anim2DData 自動生成）と 3-13（エディタ）は未着手

### バグ修正（2026-09-17、U-20: SE・VFX を設定しても再生されない）

- **症状**: Anim Editor（`AnimEditorWindow`）で Anim2DData に Frame/Time＋`Action=PlayAsset` のイベント（SE/VFX）を設定しても、実行時（Play Mode）にその Anim2DId を直接 `Anim2D.Play` すると SE/VFX が一切鳴らない/出ない。アニメーション自体（見た目）は Animator 自身の状態遷移で動き続けるため気づきにくい。
- **真因**: `Anim2D.Play`/`Anim.Play`（ID 版）はどちらも `AnimManager.Play` → `AssetRegistry.ResolveOrPlaceholder<AnimData>` という**完全同期**の解決経路しか持たない（Canvas/ControlSkin/Presentation/Shake/Haptics で 2026-09-12〜14 に見つかったのと同じ罠。[07_canvas_prefab.md](07_canvas_prefab.md) の「バグ修正（2026-09-12）」参照）。`ResolveOrPlaceholder` は「既に `_loaded` キャッシュにあるものしか返さない」同期専用の解決で、自らはロードを開始しない。`_loaded` に乗るのは `Flags.Load=Preload`（カタログ登録時に自動ロード）のものだけで、既定値の `LazyLoad`（「初回参照時にロード」）は非同期経路（`ResolveAsync`）専用のため、他の経路（`ModelData.DefaultAnimation` の依存解決等）で先にロードされていない Anim(2D)Id を直接 Play すると、実データではなく `Events` が空の Placeholder（`AnimManager.CreatePlaceholder`）が再生される。`AssetCreationService.cs` の新規作成時デフォルトに `AssetType.Anim`/`AssetType.Anim2D` が含まれていなかったのが漏れの原因（Canvas 等を直した時点でこの 2 種別は対象外のまま残っていた）。2D キャラクターは 3D 専用の `ModelData` を経由しないため「他経路での先行ロード」が起きにくく、3D より顕在化しやすかった。
- **対応**: `AssetCreationService.Create()`（`Assets/DDrive/Editor/AssetBrowser/AssetCreationService.cs`）の新規作成デフォルトに `AssetType.Anim`/`AssetType.Anim2D` を追加（`Flags.Load=Preload` になる）。既存アセット向けに `AddressablesRegistrationValidator`（`Assets/DDrive/Editor/Validation/AddressablesRegistrationValidator.cs`）の `NeedsPreload` にも同じ 2 種別を追加し、`Validation > Run All` で `Flags.Load != Preload` を Error + FixAction として検出・修正できるようにした（Canvas/ControlSkin と同じ仕組みをそのまま再利用。複製しない）。既存の `Assets/GameData/Anim/Player/ANIM_Player_Jump.asset`（実際に PlayAsset イベントが付いていた）と `Assets/GameData/Anim2D/**/ANIM2D_*.asset` は本対応の中で `Flags.Load=Preload` に修正済み。
- **注意（要判断ではなく既存の仕様のまま）**: この修正は「同期解決でしか引かれない Anim(2D)Id は Preload にする」という運用を敷いただけで、`ResolveOrPlaceholder` 自体（非同期ロードを自ら開始しない設計）は変更していない。`AnimManager`/`Anim`/`Anim2D` の API・シリアライズ形式に変更はない。
- テスト: `Anim2DEventDispatchTests`（PlayMode、`Assets/DDrive/Tests/Runtime/`）。`Anim2D_FrameEvent_PlaysSeAndSpawnsVfx_ThroughFacade` が既存経路（Data を先に解決済み）で SE/VFX が発火することを固定し、`Anim2D_IdPlay_WithoutPriorPreload_ResolvesPlaceholder_AndEventsDoNotFire` が真因（LazyLoad のまま ID 直接 Play → Placeholder → イベント発火なし）を再現する。`AddressablesRegistrationValidatorTests.Anim2DAsset_CreatedWithPreload_AndFlagsLoadRegression_IsDetectedAndFixed`（EditMode）が「作成時に既定 Preload になること」と「LazyLoad に戻ったものを Validator が検出・修正できること」を固定する。

## C-5. エディタ（Anim2DEditor = 既存 ToolWindow の移植 + 拡張）

既存の Create / Edit / Preview の 3 モード構成を維持し、以下を追加:

- 生成完了時に Anim2DData を自動作成し AssetBrowser に登録（ID 発行）
- タイムライン上で Frame イベント（SE/VFX）を D&D 設定 → 共通プレビューで同時再生
- 方向スプライトの一括処理（8 方向シートを一括スライス → 角度別 Clip → BlendTree 登録）
- Validation パネル統合

### 実装メモ（2026-09-10、3-11: 既存ツール移植 + Anim2DData 自動生成 + ID 発行）

> - **プレビュー方式（2026-09-10 決定）**: 編集モードのプレビューはウィンドウ内描画ではなく「確認用シーンを開く / 今のシーンに配置」で `[D-Drive] Anim2D Preview`（SpriteRenderer + Animator、DontSave）を置き、`SceneAnimPreviewDriver` で ▶ / ■ して SceneView で確認する（[09] §2 の全エディタ共通ルール）

> - 移植元 `Katsuya.Tools.SpriteAnimation`（`Editor/`: SpriteSlicer / AutomaticSpriteSlicer / ExistingSpriteCollector / AnimationClipBuilder / AnimationClipEditorUtility / BlendTreeRegistrar / DirectionAngle / NamingRuleResolver 等）を `Assets/DDrive/Editor/Anim2D/`（namespace `DDrive.Editor.Anim2D`）へロジックそのままで移植。`AnimationSfxEditorUtility` は移植せず廃止（SE マーカーは 3-13 の共通 AssetEvent Frame イベントに置換）
> - C-2 表の対応どおり: **EasingFunction / CubicBezierEvaluator → Foundation `EasingCore`/`ValueDef`/`EaseDef`**（既に [15] §B-2 で昇格済みのものを利用。新規実装なし）。`AnimationClipEditorUtility.BuildTimes` は `PlacementMode.Uniform`(等間隔) / `PlacementMode.Retiming`(`ValueDef.Evaluate` をそのまま使用) の 2 モードに単純化した
> - スプライト分割は **`TextureImporter.spritesheet`(SpriteMetaData 配列)によるフォールバック実装**。本プロジェクトに `com.unity.2d.sprite` パッケージが未導入(`Packages/manifest.json` に無し)のため `UnityEditor.U2D.Sprites.ISpriteEditorDataProvider` は使わず、旧 API `TextureImporter.spritesheet`(Obsolete 警告のみ、削除はされていない)で矩形を書き込む。`AutomaticSpriteSlicer` の自動検出自体は `UnityEditorInternal.InternalSpriteUtility.GenerateAutomaticSpriteRectangles`(コア UnityEditor.dll、パッケージ非依存)を使用
> - `SpriteAnimationNameData`(固定パスの命名規則 SO) → **`Anim2DImportProfile`**(`TextureImportProfile` と同じ `FindOrDefault()` パターンで AssetData 化。Entries{Name, States[]} / DefaultFrameRate / DefaultDirections / DefaultClipFolder)
> - **`Anim2DEditorWindow`**(`[DataEditor(typeof(Anim2DData), "Anim2D Editor で開く")]`、`Tools/D-Drive/Editors/Animation (2D)`)。ScrollView ルート + 作成/編集のトグル 2 モード:
>   - 作成: 入力モード(Grid/Automatic/既存) → 命名(Import Profile から選択 or 手入力) → アニメーション(FrameRate/Loop/Length) → Animator(任意、BlendTree 登録) → 「生成」。単一クリップ(方向なし、`DirectionMode` で角度サフィックスのみ付与も可)と、方向セット一括(Four=4方向/Eight=8方向のテクスチャをまとめて投入 → 角度別 Clip を生成し `DirectionClips` を角度順で構築、0° の Clip が `Anim2DData.Clip`)の両方に対応。生成完了時に `AssetCreationService.Create` で Anim2DData を自動発行(ID・カタログ・Addressables 登録込み)
>   - 編集: Anim2DData を読み込み、`AnimationClipEditorUtility.LoadSprites` でスプライト/時刻を取得 → 配置モード(Uniform/Retiming) → 「適用」で `RebuildClip` + `Retiming`(ValueDef)を `Undo.RecordObject`+`SetDirty` で書き戻す。スプライトのミニプレビュー(EditorApplication.update で再生)付き。`Retiming` フィールドは既存 `ValueDefDrawer` を `PropertyField` 経由でそのまま流用
>   - 旧「Sequence Preview」「Sound」モードは廃止(廃止を知らせる注記ラベルは 3-13 完了後の 2026-09-11 に削除)。共通プレビュー・イベント D&D・Validation パネル統合は 3-13
> - テスト: `Anim2DToolTests`(EditMode, 22 件)。NamingRuleResolver の角度抽出/クリップ命名、DirectionAngle の 8 方向マッピング往復、BuildUniformTimes/BuildRetimingTimes の単調性・範囲、AnimationClipBuilder が Sprite キーを持つ Clip を生成すること、BlendTreeRegistrar が 2D Freeform Directional Tree + x/y パラメータを登録すること、Anim2DImportProfile.FindOrDefault の組み込み既定値

#### Codex レビュー対応（2026-09-10）

> - **P1**: `AutomaticSpriteSlicer.DetectRects` がユーザーの確定操作前にソーステクスチャの TextureImporter 設定(Sprite/Multiple・readable・no mipmap・Point・Uncompressed・NPOT None・maxTextureSize 16384)を恒久的に書き換えて `SaveAndReimport` していた問題を修正。検出前に対象プロパティをスナップショットし、検出後（`keepImportSettings=false` が既定）に元の値へ復元して再度 `SaveAndReimport` する。確定処理(`ApplyRectsAndCollect`)は従来どおり自分で必要な設定をセットする。テスト: `Anim2DToolTests.DetectRects_DoesNotMutatePersistedImporterSettings`（mipmap ON・Compressed でインポート → DetectRects → 設定が保たれていることを確認）
> - **P2**: `AnimationClipBuilder.BuildWithTimes` に `Build` と同じ frameRate/totalSeconds ≤ 0 の検証を追加(不正値は false を返し警告ログのみ)。テスト: `BuildWithTimes_NonPositiveFrameRate_ReturnsFalse_AndDoesNotThrow` / `BuildWithTimes_NonPositiveTotalSeconds_ReturnsFalse_AndDoesNotThrow`

### 実装メモ（2026-09-10、3-13: 共通プレビュー移植 + イベント D&D + Validator）

> - **イベント編集・SE/VFX 同時プレビュー・タイムラインの D&D は Anim2DEditor で作り直さず、`AnimEditorWindow`(3D 用、B-4)をそのまま再利用する**。`Anim2DData : AnimData` なので `DataEditorRegistry`(継承元を遡って引く実装、[09] §8)が `Anim2DData` に対して `Anim2DEditorWindow`(作成/編集)と `AnimEditorWindow`(タイムライン・イベント・Validation)の両方を自動で解決する。属性の追加は不要だった(`DataEditorRegistryTests` 相当のアサーションを `Anim2DEditorTests.DataEditorRegistry_ResolvesAnim2DData_ToBothEditors` で固定)
> - `Anim2DEditorWindow.Edit` の「編集」タブに「イベント / 同時プレビュー」を追加: 「Anim Editor で開く(イベント D&D・SE/VFX 同時再生)」ボタンで `AnimEditorWindow.Open(_editTarget)` を呼ぶだけ。Events(Frame/Time/PlayAsset)の件数だけを読み取り専用で要約表示し(編集はしない)、`OnFocus`(Anim Editor から戻ったとき)・読み込み・生成直後に更新する
> - **`Anim2DPreviewObject`**(新規、`Assets/DDrive/Editor/Anim2D/Anim2DPreviewObject.cs`)— `[D-Drive] Anim2D Preview`(SpriteRenderer + Animator、DontSave、Clip 先頭 Sprite キーを反映)の生成/破棄を `Create(Anim2DData)` / `Destroy(GameObject)` に切り出し、`Anim2DEditorWindow.Edit`(既存の配置ロジックを置換)と `AnimEditorWindow` の両方から使う
> - **`AnimEditorWindow` に Anim2D フォールバック**: 対象 `AnimData` が `Anim2DData` で確認用モデル(ModelData、3D 専用)が未設定のとき、`EnsureSceneTarget`(「確認用シーンを開く」/ ▶ の入口)が `Anim2DPreviewObject.Create` で配置した Animator を対象にする。`SetSceneTarget` で手動選択に切り替えたとき・`OnModelChanged`・`OnDisable` で配置物を手放す(3D の `SpawnModel`+`OwnsCurrent` とは別経路で追跡する専用フィールド `_anim2DPreview` を持つ。DontSave なのでシーン切替時は Unity 側でも破棄される)
> - Validation パネル(Create / Edit どちらのモードでも見える共通ルートに配置): `Anim2DDataValidator`(ランタイム側、3-12) + `AnimDataValidator`(基底、Clip/StateName/イベント範囲等) + 新規 `Anim2DEditorValidator` をまとめて実行し、`HelpBox` + `FixAction` がある行だけ「修正」ボタンを表示する
> - **`Anim2DEditorValidator`**(新規、Editor API 依存の 2 検査。`CI.DiscoverValidators` がリフレクションで自動発見): (a) `Directions != None` のとき、`AssetDatabase.FindAssets("t:AnimatorController")` でプロジェクト内から `StateName` のステートを持つ Controller を探し、`ParamXName`/`ParamYName` の Float パラメータが無ければ Error(FixAction で `controller.AddParameter` を追加)。Controller 自体が見つからないときは Warning に留める(配線は任意のため)。(b) `Clip` と `DirectionClips` それぞれについて `AnimationUtility.GetObjectReferenceCurve` の `m_Sprite` キーフレームに `null`(参照切れ)が無いか調べ、件数付きで Error
> - テスト: `Anim2DEditorTests`(EditMode、7 件)。`Anim2DPreviewObject.Create` が DontSave の SpriteRenderer+Animator を配置し先頭 Sprite を反映すること(Clip 無しでも例外にしない)、`Anim2DEditorValidator` が x/y パラメータ不足を検出し FixAction で追加できること・方向無しでは検査しないこと、Sprite キー参照切れを件数付きで検出すること・全て有効なら何も出ないこと、`DataEditorRegistry.GetEntries(typeof(Anim2DData))` が `Anim2DEditorWindow` と `AnimEditorWindow` の両方を返すこと

### 実装メモ（2026-09-11、3-21: OH_CASE2026_ITAMI の Sprite Animation Tool から取り込み）

> - 移植元（`Katsuya.Tools.SpriteAnimation`）を再調査し、D-Drive 側に無かったものだけを取り込んだ。構造面（Anim2DData / Registry / Undo / テスト / 方向セット一括生成 / Data 側の AssetEvent / ValueDef Retiming / 遷移シーケンス）は D-Drive が既に上回っているため戻していない
> - **検出オーバーレイ**: 作成モードの「検出プレビュー(Automatic)」で、検出矩形をテクスチャ縮小表示の上に緑枠 + 番号で重ねて描く（`IMGUIContainer`、テクスチャ左下原点 → IMGUI 左上原点の Y 反転）。「Sprite Editor で手動補正」ボタンで矩形を Importer に確定（`AutomaticSpriteSlicer.ApplyRectsAndCollect`）→ `Window/2D/Sprite Editor` を開き、入力モードを「既存スプライト」に切り替える。**Sprite Editor は `com.unity.2d.sprite` 未導入だと開けない**（案内を出す。パッケージ導入は manifest 変更のため未実施）
> - **方向 Clip への一括リタイミング**: 編集モードの「適用」が主 Clip に加えて `DirectionClips` の全 Clip（主 Clip と同じ枚数の Sprite キーを持つもの）へ同じ配置を適用する（`Anim2DRetiming.ApplyToDirectionClips`、Undo 対応、枚数違いはスキップして件数を報告）
> - **ランタイム補助**: `Anim2D.FreezeAtFirstFrame(h)`（Seek 0 + speed 0。チャージ中の構え）/ `Unfreeze(h)`。`Runtime/Anim2D/Anim2DFacing.cs`（MonoBehaviour。`SetWorldDirection(移動ベクトル)` → カメラ Yaw 基準の画面向きへ変換（`CameraRelative`）→ 指数平滑化（`Smoothing` 秒、`t = 1 − exp(−dt/τ)`）→ 毎フレーム `Anim2D.SetDirection`。純関数 `ToScreenDirection` / `Smooth` をテスト）。残像・ビルボードはゲーム固有のため取り込まない
> - **AnimEditor（3D / 2D 共用）の SE / VFX 連携を OH 側の SE タブと同じ見え方に**: タイムラインの目盛りを 0.5 秒刻みからフレーム刻み（幅に応じてラベルを間引き）に変更、イベントマーカーの直下に時刻 + 対象名（SE / VFX の DisplayName）を表示、秒モードの行にフレーム換算を併記。マーカーのドラッグ・波形・試聴・シーク・Undo は既存どおり
> - **人による確認で判明した修正（2026-09-11）**: (1) 検出オーバーレイの縮尺を「インポート後サイズ」で計算していて、Max Size で縮小されるテクスチャ（2500×2000 → 2048×1638）で矩形がずれた → Importer の元画像サイズ（`GetSourceTextureWidthAndHeight`）基準に。(2) 「検出プレビュー」後も入力モードが Grid のままで、「生成」が既定の 4×1 Grid で切っていた → 検出時に入力モードを Automatic に切り替える。(3) `SpriteSlicer`（Grid）がセルをインポート後サイズから計算し、`SpriteMetaData.rect`（元画像座標）と食い違っていた → Automatic と同じく Max Size 16384 + 元画像サイズで計算（`SpriteSlicerTests`）
> - **プレビュー物の引き継ぎ問題（2026-09-11、人による確認で判明）**: (1) Anim Editor で対象を切り替えても `[D-Drive] Anim2D Preview` と確認用モデルが前の対象のまま残り、前の絵が出ていた → `SetTarget` で対象が変わったら、2D なら確認用モデル（ModelData、3D 専用）を None にして配置済みモデルを手放し、既存のプレビュー物は新しい Data の先頭スプライトに差し替える（`Anim2DPreviewObject.FindOrCreate` = Animator を素に戻して先頭 Sprite を当て直す）。3D に切り替えたら 2D の配置物は破棄。(2) Anim2D Editor の「確認用シーンを開く」→「Anim Editor で開く」で両方がプレビュー物を作り、前のものが残った → プレビュー物はシーン内で同名 1 つを共有（`FindExisting` / `FindOrCreate`、`Create` も既存があれば再利用）し、Anim2D Editor は「Anim Editor で開く」の前に再生を止めてドライバの対象だけ手放し（プレビュー物はシーンに残す = スプライトが消えない）、Anim Editor は `SetTarget` で対象の変更有無に関係なくシーンの同名プレビュー物を引き取って対象にする
> - テスト: `Anim2DFacingTests` 8 件、`Anim2DRetimingTests` 4 件、`SpriteSlicerTests` 1 件

### レビュー対応（2026-09-11）

Phase 3（Anim2D）の自前レビューで確認した指摘の修正。挙動の変更点だけを挙げる。

- **プレビュー物の所有権**: `[D-Drive] Anim2D Preview`（DontSave）は Anim2D Editor と Anim Editor の共有物なので、**どちらの `OnDisable` でも破棄しない**（ウィンドウを閉じる / ドメインリロードで相手の対象と絵が消えていた）。破棄は Anim2D Editor の「撤去」ボタンと、Anim Editor の明示操作（確認用モデルの設定変更 / 3D 対象への切替）だけ。「Anim Editor で開く」では所有権も渡す（`_previewObject = null`、状態表示は「Anim Editor に引き渡し済み」）。残った物は次の `FindOrCreate` が拾う。`FindOrCreate` は名前だけで拾うため、再利用時に `HideFlags.DontSave` と SpriteRenderer / Animator を付け直す
- **リタイミングの安全弁**: `Anim2DData.Retiming` の既定値は `ValueDef.Constant01(1)` で、そのまま「適用」すると全フレームが末尾に潰れ、主 Clip も 4 / 8 方向 Clip も 1 枚のアニメになっていた。`AnimationClipEditorUtility.TryBuildTimes` で**狭義単調増加**を検証し、満たさなければ警告 + no-op（何も書き換えない）。`Anim2DRetiming.ApplyToDirectionClips` にも同じ門を置き、時刻配列は枚数ごとに 1 回だけ作る
- **保存回数**: `RebuildClip` 内の `AssetDatabase.SaveAssets()` を廃止（1 クリックで 9 回保存していた）。`SetDirty` だけ行い、保存は `ApplyRetiming` の最後に 1 回
- **Grid 分割**: `SpriteSlicer` が `maxTextureSize` を 16384 に固定して 2 回リインポートしていたのをやめた。`SpriteMetaData.rect` は元画像座標で Unity 側がスケールするため、セル計算に `GetSourceTextureWidthAndHeight` を使えば足りる（ユーザーの Max Size 設定を壊さない。リインポートは spritesheet 書き込み後の 1 回だけ）
- **Anim Editor の 2D 切替**: 対象が `Anim2DData` になったら、自分で配置したモデルだけでなく**手で入れた 3D の Animator も必ず外す**（3D リグで 2D の Clip を再生していた）
- **定常経路の alloc**: `Anim2D.SetDirection` にハッシュ版オーバーロード（`SetDirection(Animator, Vector2, int, int)`）と `ResolveFloatParameterHash` を追加。`Anim2DFacing` は対象 / Controller / パラメータ名が変わったときだけ解決し、`Update` では `animator.parameters`（配列 alloc）を踏まない。平滑化 1 ステップは `Tick(dt)` として公開（テスト用）
- **エディタ UI**: 検出プレビューの高さをウィンドウ幅の変化（`GeometryChangedEvent`）でも取り直す（縦に切れていた）。AnimEditor のタイムラインは細目盛りが 2px 未満に詰まる場合に間引く

## C-6. Validation

Clip 未生成/Missing (Error) / Directions=Eight なのに DirectionClips 不足 (Error) / BlendTree に x,y パラメータ無し (Error, FixAction=追加) / FrameRate ≤ 0 (Error) / スライス済みスプライトの参照切れ（元テクスチャ再インポートで消失）(Error)

### 実装メモ（2026-09-14、5-11 ImportRule。Part A/B/C 共通）

> `Assets/SourceAssets/<種別>/<カテゴリ>/` に元ファイルを置くだけで Data が自動生成される仕組み（`ImportRule`、[09_editor_tools.md](09_editor_tools.md) §1.1）を Model/Anim/Anim2D にも適用した。
> - **Model**: `SourceAssets/Model/<カテゴリ>/*.fbx` → `ModelData.Prefab` に FBX のインポート直後のルート GameObject をそのまま設定（Slots/Avatar/Lod は未設定のまま。ModelEditor の「Slot 自動収集」等で追って調整）
> - **Anim**: `SourceAssets/Anim/<カテゴリ>/*.anim` または `*.fbx`(埋め込みクリップの先頭 1 本、`__preview__` は除く) → `AnimData.Clip`
> - **Anim2D**: `SourceAssets/Anim2D/<カテゴリ>/*.anim` または `*.fbx` → `Anim2DData.Clip` のみ設定する Placeholder(Directions=None のまま)。方向づけ(DirectionClips)は既存の Anim2DEditor(C-5)でスプライトから組み立てる運用とした(要判断。[28_manual_verification_phase5.md](28_manual_verification_phase5.md) 参照)
> - いずれも元ファイル削除時は Data を消さず、本節・B-6・A-4 の既存 Validator の「未設定(または Missing)です」がそのまま欠落表示を担う(新規 Validator は追加していない)
> - 既存の Maya→Material 経由の MaterialData 自動生成（[06] A-2）とは独立に動く(同じ FBX インポートで両方が発火してよい)
