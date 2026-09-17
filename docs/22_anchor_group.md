# 22. 配置セット（AnchorGroup）仕様（2026-09-08）

> **状態: 実装済み（2026-09-08）。** [21_anchor_spec.md](21_anchor_spec.md) の Anchor アセット（1 アセット = 1 つの位置）を土台に、「Anchor 側にアセットを登録し、複数の位置へ一括で出す」単位を追加した。
> 背景: 3×3 の格子点にエフェクトを出したい場合に、格子点ごとに Anchor を作って登録する作業をデザイナーにさせない（相談 2026-09-08）。

---

## 1. 位置づけ

| 単位 | 役割 | 参照の向き |
|---|---|---|
| AnchorData（[21]） | **1 つの位置**。ボーン基準 + オフセット + ランダム + ディレイ + 確率。入れ子は Parent 連鎖 | VfxData / SeData / コードが AnchorId で参照する（アセット → Anchor） |
| **AnchorGroupData（本書）** | **位置の集合 + 何を出すか**。原点 + パターン生成/手置きの点 + 各点に出す VFX/SE | Group が VFX / SE / 原点 Anchor / 子 Group を参照する（Anchor → アセット） |

将来 Presentation（[08]）が出来たら、Group はトラック種別 `AnchorGroup` として吸収する。それまでは `Anchors.Play(groupId, ctx)` が暫定 API。

## 2. データ構造（`Runtime/Anchoring/AnchorGroupData.cs`）

```csharp
[AssetIdDefinition(AssetType.AnchorGroup, typeof(AnchorGroupMarker), "ANCHORGROUPID")]
public sealed class AnchorGroupData : AssetDataBase
{
    // 原点(基準ボーン + ずれ)
    public AssetId<AnchorMarker> OriginAnchorId;   // 既存 Anchor アセットを原点にする(優先)
    public AnchorDef Origin;                        // 埋め込みの原点(OriginAnchorId=0 のとき)

    // 配置パターン
    public AnchorLayoutKind Layout;                 // Manual / Grid / Circle / Line / Random
    public int GridCountX, GridCountY, GridCountZ; public Vector3 GridSpacing; public bool GridCentered;
    public int CircleCount; public float CircleRadius, CircleStartAngle, CircleArc; public bool CircleFaceOutward;
    public int LineCount; public float LineLength; public Vector3 LineDirection; public bool LineCentered;
    public int RandomCount; public float RandomRadius; public int RandomSeed;   // Seed 0 = 毎回変える
    public AnchorGroupPoint[] Points;               // 手置き(Manual のとき、またはパターンへ追加)

    // 点ごとの生成
    public float DelayPerIndex, DelayJitterSec;     // 番号順に遅らせる(波紋)
    public float ChancePerPoint;                    // 各点の生成確率
    public float PositionJitterRadius; public Vector3 EulerJitter; public Vector2 ScaleRange;

    // 出すもの
    public AssetId<VfxMarker>[] SharedVfx; public AssetId<SeMarker>[] SharedSe;     // 全点共通(一括登録)
    public AnchorGroupOverride[] Overrides;         // { Index, Skip, Vfx[], Se[] } 特定の点だけ変える / 出さない
    public AnchorGroupChild[] Children;             // { Group, AtIndex(-1=全点) } 入れ子
}
```

- 点数の上限は `MaxPoints = 256`（固定長バッファ、超過分は打ち切り + Validation Warning）
- 接頭辞 `ANCG`、配置 `Assets/GameData/AnchorGroup/<カテゴリ>/`、カタログは Anchor と共用の `AnchorCatalog`

## 3. 仕様

### 3.1 原点

`OriginAnchorId` があれば AnchorChain（[21] §3.2）で合成した定義、無ければ埋め込み `Origin`。Space/Path の解決はスポーン先（contextRoot）に対して既存 `AnchorResolver` で行う。原点自身のランダム・ディレイ・確率（Anchor アセット側）も各点に引き継がれる。

### 3.2 点の生成（`AnchorLayout.Generate`、純粋関数）

| Layout | 生成 |
|---|---|
| Grid | X×Y×Z の格子。`GridCentered` で原点を中心 or 角に。番号は X → Z → Y の順 |
| Circle | XZ 平面の円周。`CircleArc` < 360 なら両端を含む扇形。`CircleFaceOutward` で各点を外向き |
| Line | `LineDirection` 方向に `LineLength` を等分。`LineCentered` で原点を中央に |
| Random | 半径内の球状ランダム。`RandomSeed` ≠ 0 で毎回同じ配置（エディタ表示も固定）。0 なら再生ごとに変わる（エディタ表示は固定シード 1） |
| Manual | 手置き `Points` のみ |

`Points` はどのパターンにも**追加**される（番号はパターンの後ろ）。各点は `AnchorLayout.ComposePoint` で原点に積む（[04] §2.6 と同じ式）。`DelayPerIndex × 番号` を加算、`ChancePerPoint` を乗算、位置/回転/スケールのランダムは点ごとにサンプリング。

### 3.3 再生計画（`AnchorGroupPlanner.Plan`）

点ごとに「出すアセット」を決めてアクション列 `{Kind(Vfx/Se), AssetId, PointIndex, Depth, Spec}` に展開する。

- `Overrides[Index]`: `Skip` なら何も出さない。Vfx/Se が入っていればそれに**差し替え**（空なら共通に戻る）
- それ以外は `SharedVfx` / `SharedSe` を全点に
- `Children`: `AtIndex`（-1 = 全点）の点を基準にして子 Group を展開。**子の原点（OriginAnchorId/Origin）は無視**され、その点が基準になる。深さ 4 まで、自己参照は無視
- 同じ計画をランタイム（AnchorGroupPlayer）とエディタ（試し出し）が実行する

### 3.4 ランタイム（`AnchorGroupPlayer` / `Anchors`）

- `CollectVfxHandles(handle, into)`: そのグループが（ディレイ後も含めて）出した VFX Handle を列挙する。エディタのプレビュー台帳（`SceneAnimPreviewDriver.AdoptGroupVfx`）が DontSave / 手動 Simulate のために引き取るのに使う
- `AssetEventDispatcher` は AnchorGroup イベントを `Play` したあと `OnGroupPlayed(handle)` を出し、`Repeat=KeepWhilePlaying` なら発火元の終了で `Stop` する（2026-09-10）

```csharp
var h = Anchors.Play(ANCHORGROUPID.HealField, ctx);   // 全点分を VfxManager.SpawnData(data, spec) / AudioManager.PlaySeData(data, spec) で再生
Anchors.Stop(h); Anchors.Kill(h); Anchors.IsPlaying(h);  // まとめて操作
```

- Manager には「合成済み spec で Spawn/Play する」経路を追加した（`SpawnData(VfxData, in AnchorSpawnSpec, Transform)` 等）。ディレイ（Pending）・確率・Cosmetic 配送は既存の仕組みがそのまま効く
- GroupHandle は `Handle<AnchorGroupMarker>`。`IsPlaying` は 1 点でも再生中なら true。`Tick()` で全点が終わった Group を台帳から外す（起動コードで Manager の Tick の後に呼ぶ）
- 計画リストと Instance は使い回し（定常経路で GC alloc なし。点数分の Handle は List に積む）

### 3.5 Validation（`AnchorGroupDataValidator`）

| 検査 | 重大度 |
|---|---|
| 点が 0 | Error |
| 原点（埋め込み）で Space=BoneName/NamedObject かつ Path 空 | Error |
| Children に自分自身 / 循環 | Error |
| 出すもの（Shared / Overrides / Children）が何も無い | Warning |
| Overrides.Index / Children.AtIndex が範囲外 | Warning |
| 点数が 256 で打ち切り、Circle 半径 0、Line 長さ 0、ChancePerPoint 0 | Warning |

### 3.6 エディタ（`Tools > D-Drive > Editors > Anchor Group`）

| 機能 | 内容 |
|---|---|
| 設定 | SerializedObject バインド。Layout に応じて Grid / Circle / Line / Random の欄だけ表示 |
| 状態表示 | 原点の解決（✓/⚠）・点数・共通アセット数・入れ子数・ディレイ/確率 |
| SceneView | 原点（大きい円）と全点（番号付きの小さい球。上書きあり = 橙、出さない = 灰 ×、選択 = 黄）。**クリックで点を選択**。手置きの点は直接ドラッグ、パターンの点をドラッグすると **Grid の間隔 / Circle の半径 / Line の長さ** が変わる（`ApplyPatternDrag`）。描画権（[04] §5 SceneGuiOwner）と「SceneView 表示」チェックは他のエディタと共通 |
| 選択点の操作 | 「選択点を Overrides に追加」→ その点だけ別アセット / 出さない、を設定できる行が出来る。「▶ 選択点のみ」で 1 点だけ試し出し |
| **手置きの点に変換**（2026-09-17 追加、U-22） | §3.7 |
| 試し出し | 「▶ 全点」で計画どおりに実 Manager 経由（VFX は `SceneVfxPreviewDriver.Play(data, attach, spec)`、SE は `PreviewService.PlaySe(data, ctx, spec)`）。ディレイ・確率・ランダムもそのまま効く |
| 検証 | `AnchorGroupDataValidator` をその場で表示 |

プレビュー用 Registry（`EditorAnchorRegistry`）は AnchorData に加えて AnchorGroupData / VfxData / SeData も登録する（計画が ID から VFX/SE を引くため）。

### 3.7 自動配置 → 手置きの点に変換（2026-09-17 追加、U-22）

Grid 3×3 などのパターンで並べたあと「この 1 点だけ少しずらしたい」となったときに、**パターンの計算結果をそのまま手置きの点（`Points`）へ焼き付ける**ボタン。実装は `Editor/Anchor/AnchorGroupPointConverter.cs`、UI は Anchor Group Editor の「設定」内、パターンの欄と `Points` の間（パターンのときだけ表示）。

**データ構造の切り替え**:

```
変換前: Layout = Grid/Circle/Line/Random  +  Points = [手置き分だけ]
        → 点 = AnchorLayout.GeneratePattern(…) ++ Points

変換後: Layout = Manual                   +  Points = [パターンの計算結果 ++ 元の手置き分]
        → 点 = Points のみ
```

- 計算は再実装せず、ランタイムの純粋関数 `AnchorLayout.GeneratePattern` / `AnchorLayout.AppendManualPoints` をそのまま通す（このために従来の `AnchorLayout.Generate` を「パターン生成」と「手置きの追加」に分割した。`Generate` の挙動と既存の呼び出し側は変わらない）。**変換の前後で点の位置は一致する**
- **並び順（= 点の番号）を保つ**ので、`Overrides.Index` / `Children.AtIndex` が指す点は変換後も同じ。SceneView の番号も変わらない
- ランダム配置は**エディタ表示と同じ固定シード**（`sampleRandom: false`。`RandomSeed = 0` なら 1）で焼く。「今 SceneView に見えている配置」がそのまま点になる
- 各点の `Name` は `Grid0` `Circle3` のようにパターン名 + 番号。元からあった手置きの点は名前をそのまま引き継ぐ
- `Undo.RecordObject` + `EditorUtility.SetDirty` 済みで **Ctrl+Z で元のパターンに戻せる**。実行前に `EditorUtility.DisplayDialog` で点数・番号が変わらないこと・Undo で戻せることを確認する
- パターンの設定値（間隔・半径・長さなど）は**消さずに残す**ので、`Layout` を戻せばやり直せる（ただし戻すと焼いた点と二重になるため、その場合は `Points` を空にする）
- `Layout = Manual` のときはボタンを出さない（押しても警告 + no-op）
- テスト: `Tests/Editor/AnchorGroupPointConverterTests.cs`（Grid の焼き付け・変換前後で位置が一致・手置き分が末尾に残る・Circle の点ごとの回転・Manual で no-op）

## 4. 実装ファイル

| 層 | ファイル |
|---|---|
| Foundation | `Identity/AssetType.cs`（`AnchorGroup` 追加） |
| Runtime | `Anchoring/AnchorGroupData.cs` / `AnchorLayout.cs` / `AnchorGroupPlanner.cs` / `AnchorGroupPlayer.cs`（+ `Anchors` ファサード）/ `AnchorGroupDataValidator.cs`、`Vfx/VfxManager.cs` `Audio/AudioManager.cs`（spec 経路） |
| Editor | `Anchor/AnchorGroupEditorWindow.cs`、`Anchor/AnchorGroupPointConverter.cs`（2026-09-17、§3.7）、`Preview/EditorAnchorRegistry.cs`、`Preview/AnchorSceneHandles.cs`（2026-09-17: 基準の描画。[21] §3.10）、`Vfx/SceneVfxPreviewDriver.cs`、`Preview/PreviewService.cs`、`AssetBrowser/AssetNamingService.cs` `AssetCreationService.cs` |
| Tests | `Tests/Runtime/AnchorGroupTests.cs`（PlayMode 16 件: 各パターン・合成・計画・上書き・入れ子・Player・Validator）、`Tests/Editor/AnchorGroupPointConverterTests.cs`（EditMode 4 件: 手置きへの変換、2026-09-17） |

## 5. 未対応・今後

- Presentation 統合（トラック種別 AnchorGroup）は Phase 5
- ネット同期は各点の Cosmetic 配送（位置のみ）に任せる。Group 単位の同期は未対応
- Spiral / 曲線パターン、点ごとの個別ディレイ表は要望があれば

## 6. 変更履歴

- 2026-09-17: §3.7「自動配置 → 手置きの点に変換」を追加（U-22）。`AnchorLayout.Generate` を `GeneratePattern` + `AppendManualPoints` に分割（挙動は不変）。SceneView に基準（原点）の 3 軸・座標ラベル・基準 → 原点の線を追加（U-24。定義は [21](21_anchor_spec.md) §3.10）
