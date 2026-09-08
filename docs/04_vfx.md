# 04. VFX 詳細設計

関連: [02_core_framework.md](02_core_framework.md) / [08_presentation.md](08_presentation.md)

---

## 1. 要件

- 本体 Prefab / Anchor / ループ・lifetime / 共通化引数（ラベル付き）/ 描画・ライトレイヤー / イベント / ポーズ挙動
- 生成・消滅・使い回し（Pool）・移動・アタッチを Manager が担当
- **UI パーティクルを通常パーティクルと同じ感覚で作れる**（外部ツール導入なし）
- Anchor はデータ経由で調整可能、プレビュー付き
- 複数同時再生・背景/シェーダーと合わせた調整プレビュー

> **実装メモ(2026-07-27)**: `com.unity.visualeffectgraph` パッケージが本プロジェクトに未導入のため、VfxManager は現状 **ParticleSystem のみ対応**。VfxData.Prefab に ParticleSystem を含まない Prefab を指定すると OneShot の終了判定は Duration フォールバックになる（§7 参照）。パッケージ導入後は VisualEffect コンポーネントの検出・SetFloat 等の反映を追加すれば、同じ Handle API のまま拡張できる設計にしてある。

> ⚠ **既知の落とし穴(2026-07-28)**: 本プロジェクトは URP。パーティクルの Renderer に `Default-Particle` 等の **Built-in Render Pipeline 用シェーダー**（`Particles/Alpha Blended` 等）のマテリアルを割り当てると、Scene/Game ビューでは**完全に透明**になって描画されない（プレハブのアセットプレビューは Built-in 経路で別途描画されるため、そちらでは正しく見えてしまい発見が遅れやすい）。新規 VFX を作る際は必ず `Universal Render Pipeline/Particles/Unlit`（または `.../Lit`）系シェーダーのマテリアルを使うこと。迷ったら `Tools > D-Drive > Generate > デフォルトパーティクルマテリアルを生成` で URP 対応・テクスチャ無しでも見える既定マテリアル(`Assets/GameData/Materials/Default/M_DefaultParticleUnlit.mat`、白色・加算合成)を用意できる(`VfxDefaultMaterial`、冪等)。

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
    public VfxRenderMode Render;              // World3D / UIOverlay ★UIパーティクル
    public int RenderLayer;                   // スポーン物の GameObject.layer(カリング用。UIOverlay は VfxUI レイヤー)
    public uint LightLayerMask = 1;           // Rendering Layer Mask。0 = Prefab の設定を上書きしない(2026-09-08 改定)
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
// VfxData.Anchor の既定値は AnchorDef.WorldDefault(LocalScale=1)。LocalScale=(0,0,0) は 1 扱い(旧アセット互換)

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

### §2.5 AnchorPoint / AnchorRig（シーン配置型アンカー）— 2026-07-28 追加

AnchorDef の文字列指定（BoneName/NamedObject）だけでは「ボーンの無いオブジェクト」や「SceneView での視覚的な位置調整」に対応できないため、**シーンに実体として置くアンカーマーカー**を追加する。

```csharp
// Runtime/Anchoring/AnchorPoint.cs — シーン/プレハブに配置するアタッチ位置マーカー
public sealed class AnchorPoint : MonoBehaviour
{
    public Vector3 SpawnOffset;          // 生成位置に常に加算されるローカルオフセット
    public float PositionJitterRadius;   // 生成位置のランダム半径(球)。0で無効
    public Vector3 EulerJitter;          // 生成回転への±ランダム範囲
    public Vector2 ScaleRange;           // 生成スケール倍率のランダム範囲(VFXのみ)
    public Color GizmoColor;             // SceneView ギズモ色
}
// Runtime/Anchoring/AnchorRig.cs — AnchorPoint 群をまとめるルートマーカー
```

**仕組み（既存機構への追加であって置き換えではない）:**
- 作成: `Tools > D-Drive > Generate > Anchor プレハブを生成` → `Assets/GameData/Prefabs/Anchors/AnchorRig.prefab`（ルート=AnchorRig + 子に AnchorPoint 雛形）がクリックごとに新規生成される。プレハブモードで AnchorPoint を追加・SceneView でドラッグ配置する
- 解決: 既存の `AnchorResolver`（NamedObject/BoneName の名前検索）がそのまま AnchorPoint の GameObject を見つける。**解決先に AnchorPoint コンポーネントが付いていた場合のみ**、Manager が SpawnOffset + ランダム散らばり（位置/回転/スケール）を追加適用する。付いていなければ従来どおり
- 適用範囲: VFX は位置+回転+スケール、SE は位置のみ（回転/スケールは音に無関係）
- ランダムのサンプリングは **Spawn/Play 時に1回だけ**。追従（Follow）中も生成時に決まったオフセットを保持する（毎フレーム再抽選して震えたりしない）
- 使い方: キャラクターの子に AnchorRig を置けば `Vfx.Spawn(id, キャラTransform)` の contextRoot 検索で解決。環境オブジェクトなら単独でシーンに置き、その Transform を渡す
- **ゲーム実行中もただの GameObject 階層**なので、AnchorPoint の子に Collider を持たせて当たり判定の基準点に再利用する等、演出以外の用途にも使える
- VfxEditor のボーン一覧ドロップダウンでは、確認用モデル内の AnchorPoint が「★」付きで先頭に表示される

注意点: 解決は名前一致のため、**同一 contextRoot 配下で AnchorPoint 名を重複させない**こと（最初に見つかった方が使われる）。命名は `Anchor_` プレフィックス推奨（例: `Anchor_RightHand`, `Anchor_Muzzle`）。

### §2.6 姿勢の式（2026-09-08 統一）

Spawn 時・追従(Tick)・エディタでのライブ編集(`ReapplyAnchor`)・SceneView ハンドルの逆変換は、すべて `Runtime/Anchoring/AnchorPose.cs` の同じ式を通る。

```
位置     = target.TransformPoint(LocalOffset + extra)          target 無し: LocalOffset + extra をワールド座標
回転     = (FollowRotation ? target.rotation : identity) * Euler(LocalEuler) * jitter
スケール = (LocalScale == 0 ? 1 : LocalScale) * scaleMul
extra / jitter / scaleMul = AnchorPoint 由来(SpawnOffset + ランダム)。Spawn 時に 1 回だけサンプリングして Instance が保持
```

> 改定前は FollowRotation=true のとき LocalEuler が無視されていた(アタッチ先の回転そのものになる)。改定後は「アタッチ先に対する相対回転」として常に効く。既存アセットで LocalEuler=0 のものは挙動が変わらない。

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
// Handle 操作(2026-09-08: 静的ファサード Vfx.Move/Attach/Detach/SetParam/SetSpeed/IsPlaying と、
// VfxHandleExtensions による拡張メソッドの両方で書ける。未 Bind 時は no-op)
h.Move(pos); h.Attach(t); h.Detach(); h.SetParam("MainColor", color); h.SetSpeed(0.5f); h.IsPlaying();

// エディタ/ツール向け(VfxManager 直)
manager.ReapplyAnchor(h);                // Data.Anchor の変更を再生中 Instance に再適用(追従先の再解決はしない)
manager.TryGetAnchorTarget(h, out t);    // 解決済みの追従先
manager.GetAnchorExtraOffset(h);         // AnchorPoint 由来の追加オフセット(逆変換用)
```

> `Spawn(VfxId, in PlayContext)` は Presentation 層(Phase 5)で追加する。

内部実装:

- 生成は PoolService.Rent。Return 時に `ParticleSystem.Clear()` / Trail リセット（IPoolable）
- Loop でない Instance は Tick で寿命監視 → 自動 Return
- `SetParam` は Params 定義を引いて MaterialPropertyBlock / VFXGraph SetXxx に反映（文字列引きは初回のみ、以後 ID キャッシュ）
- Pause: `ParticleSystem.Pause()` / VFX Graph は `pause=true`
- Root がシーン破棄等で先に消えた Instance は Tick で検出して台帳から外す(Pool は破棄済み GO の `IPoolable.OnReturn` を呼ばないため、Manager 側で `CleanupBookkeeping` を必ず通す。2026-09-08)

## 4. UI パーティクル（RenderMode.UIOverlay）

外部アセット導入なしで、通常 VFX と同じデータ・同じエディタで作る。

- 方式: **専用 UI カメラ + RenderTexture 合成** を基本とする
  - `VfxUiLayer` レイヤーに Spawn し、UI 用オーバーレイカメラ（URP Camera Stack）で描画
  - Canvas の Sorting との前後関係は CanvasData 側の Layer 設定と対応表で管理
  - 実装: `Tools > D-Drive > Generate > VFX UI レイヤー/カメラを設定`（`VfxUiSetup`）が、空いているユーザーレイヤーに `VfxUI` を確保し、現在のシーンに Overlay カメラを用意して `Camera.main` のスタックへ追加する（オプトイン。自動実行はしない）。VfxData.RenderLayer にはここで確保した index を設定する
- Spawn 時は `RectTransform` 座標→ワールド変換を Manager が吸収。デザイナー・プログラマーは意識しない
  `Vfx.Spawn(id, uiElement.transform)` で動く
- 制約の明文化: UI マスク(RectMask2D)対象外。マスクが必要な演出のみ Mesh ベーカー方式を追加検討（将来拡張）

## 5. 専用エディタ（VfxEditor）— 2026-07-28 プレビュー方式改定 / 2026-09-08 使い勝手改修

**プレビューは独自ビューポートではなく「開いているシーンへ直接スポーン → SceneView で確認」方式**。
ライティング・ポストプロセス・Skybox はシーン側の設定がそのまま適用されるため、**確認専用シーン**（暗室・屋外等のライティングと Volume を組んだシーン）をプロジェクトに用意し、それを開いた状態で調整する運用とする。埋め込みビューポートでは実シーンのレンダリング環境を再現しきれない（旧実装で Skybox/PostProcess 切替を見送った理由そのもの）ため、この方式に一本化した。

**設計方針（2026-09-08）: このウィンドウだけで VfxData の調整が完結する。** Inspector との往復・「停止→再生の押し直し」を不要にする。レビュー経緯と判断は [19_vfx_usability_review.md](19_vfx_usability_review.md)。

| 機能 | 内容 |
|---|---|
| 対象の選択 | Project ウィンドウの選択に自動追従（ツールバーの「🔒 対象を固定」で固定）。ドメインリロード後も対象・スポーン先・各トグルを保持 |
| ツールバー | 確認用シーンを開く / Prefab を開く（プレハブモード）/ Project で表示。開いているシーンにカメラ・ライトが無い場合は警告を出す |
| ライブプレビュー | 開いているシーンに Spawn/Stop/速度変更。**実 VfxManager を駆動**。スポーン物は `[D-Drive] VFX Preview` ルート(HideFlags.DontSave)配下にまとめ、ウィンドウを閉じる/シーン切替で自動破棄。EditMode では手動 Simulate + SceneView 再描画 |
| リピート | OneShot/Duration の VFX が終わったら 0.35 秒後に自動で再スポーン（調整中に何度も▶を押さない）。Loop は対象外。EditMode の手動 Simulate では `ParticleSystem.IsAlive` が true のままになるため、OneShot の終了は `SceneVfxPreviewDriver` が粒子数と再生位置（`particleCount == 0` かつ `time >= duration`、ループ無し）で判定して Kill する（2026-09-08 修正。PlayMode 中は Manager の判定そのまま） |
| 基本設定 | Prefab / LifeMode / Duration / FadeOutSec / Render / RenderLayer（LayerField）/ LightLayerMask（Rendering Layer 名付きマスク）/ Flags を SerializedObject バインドで編集（Undo 対応）。Prefab 差し替えは再生中なら再スポーン |
| スポーン先指定 | シーン内のキャラクターや AnchorRig を「スポーン先」に指定 → Anchor(ボーン名/AnchorPoint)の解決起点になる。変更時は再生中なら再スポーン |
| Anchor 編集 | Space / Path（ボーン・★AnchorPoint ドロップダウン。選択時に Space=World なら NamedObject に自動切替）/ 高さ・向きスライダー / スケール / 回転追従 / 親消滅後も残す / 2D パッド。**解決状態を常に文字で表示**（「✓ 'Anchor_RightHand'」「⚠ 'xxx' が見つかりません」等）。**変更は再生中の実体へ即時反映**（`ReapplyAnchor`。Path/Space の変更は自動再スポーン） |
| プレハブモード内再生（2026-09-08 追加） | **対象 VfxData.Prefab 自身**をプレハブモードで開いている間（ツールバー「Prefab を開く」）は、別インスタンスを出さず**ステージ内の ParticleSystem をその場で再生**する（Inspector で ParticleSystem を編集しながら確認する前提。二重表示しない）。Manager を通らないため Anchor・パラメータの即時反映は対象外。ParticleSystem を選択中は Unity 標準のプレビューが進めるので、こちらからは進めない。**別の Prefab** のプレハブモード中は従来どおり Manager 経由でステージのシーンへスポーンする（スポーン物は Prefab に保存されない）。ステージの開閉はシーン切替と同じ扱い（台帳リセット・旧スポーン物の破棄） |
| SceneView ハンドル | 「SceneView で編集」ON で、Anchor のワールド位置に移動ハンドル（回転ツール選択時は回転ハンドル）を表示。ドラッグ結果を AnchorDef.LocalOffset/LocalEuler に逆変換して保存（AnchorPoint の SpawnOffset・ランダム分は差し引く） |
| 複数同時再生 | 最大 8 スロットに別 VFX を並べて同時再生（打撃+火花+煙の重なり確認） |
| パラメータ | Params の即時反映コントロール（Float/Int/Color/Vector/Texture。Curve/Gradient は表示のみ）+ **定義の追加・削除**（Label/Type/TargetProperty/Anim を PropertyField で編集。定義が変わると即時反映コントロールを作り直す） |
| イベント編集 | Events(AssetEvent[])を PropertyField で編集。OnSpawn/OnLoop/OnDestroy → SE 再生等 |
| 検証 | `VfxDataValidator` をその場で実行し Error/Warning を表示（AssetBrowser の一括検証と同じ結果）。設定変更・Undo で自動更新 |
| UI モード | Render=UIOverlay のとき、実機では専用カメラで合成される旨を案内表示 |
| Undo | 全操作 Undo 対応。Undo/Redo 後は UI・再生中実体・SceneView を同期 |

> 旧・埋め込みビューポート（RenderTexture + オービットカメラ + 環境切替 Foldout）は廃止。ModelEditor は引き続きプレビューシーン方式（ターンテーブル用途にはこちらが適する。要望があれば同様に移行検討）。EditMode の手動 Simulate は `EditModeParticleStepper` に共通化し、PreviewService と SceneVfxPreviewDriver が共有する。

**確認専用シーン**: `Tools > D-Drive > Editors > VFX確認用シーンを開く`（ウィンドウのツールバーからも可）で `Assets/GameData/PreviewScenes/VfxPreviewScene.unity` を開く(初回は自動生成)。生成される最小構成は Directional Light + 参照用の床(Plane) + Camera(URP) + Global Volume(Bloom/ColorAdjustments の最小プロファイル)。これはあくまで叩き台で、本番のライティング/ポストプロセスに合わせて各自チューニングする前提。現在開いているシーンに未保存の変更がある場合は標準の保存確認ダイアログが出る。

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
| Anchor.BoneName がプレビューモデルに無い | Warning(静的 Validator の対象外。SeDataValidator と同様、プレビュー実行時の関心事として VfxEditor 側で扱う) |
| UIOverlay なのに Domain=Game3D | Warning |
| Stop 時 FadeOut > 10s | Warning |
| Prefab のマテリアルのシェーダーが現在のレンダーパイプライン(URP/HDRP/Built-in)と非互換 | Error(2026-07-28 追加。`ShaderPipelineAnalyzer` が SubShader の `RenderPipeline` タグを見て判定。ModelDataValidator も同じ検査を共有) |
