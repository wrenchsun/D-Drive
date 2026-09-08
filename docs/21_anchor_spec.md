# 21. Anchor 仕様改定案（2026-09-08）

> **状態: 実装済み（2026-09-08）。** §6 は全項目「推奨」で決定し、同日 2-14a〜g を実装した。本書は仕様として維持し、種別ごとの記述は [04_vfx.md](04_vfx.md) §2/§3/§5 と [03_audio.md](03_audio.md) §2/§3 に反映済み。
>
> **実装の要点（§3 との差分）**: 連鎖の合成は `Runtime/Anchoring/AnchorChain.cs`（固定長バッファ、GC alloc 0）。ランダムは各段でサンプリングし、位置はその段の向きでルート基準へ畳み込む。`AnchorId` 未登録は Placeholder（World 既定値）。エディタのプレビュー Registry は `EditorAnchorRegistry` がプロジェクト内の全 AnchorData を登録する（未保存の編集もそのまま反映）。AnchorRig からの一括生成では、AnchorPoint の SpawnOffset/ランダムを AnchorData に写し、基準は AnchorPoint 自身ではなく「その親（AnchorRig 名）」にする（AnchorPoint を Path に指すと SpawnOffset が二重適用されるため）。
> 実装済みの先行分: VfxEditor のプレハブモード内再生（[04] §5、チケット 2-13）。
> 続編: 「Anchor 側にアセットを登録して複数の位置へ一括で出す」単位は [22_anchor_group.md](22_anchor_group.md)（配置セット）。本書の AnchorData は 1 つの位置、配置セットはその集合 + 出すもの。

---

## 1. 要望（2026-09-08、原文の要約）

| # | 要望 | 本書での扱い |
|---|---|---|
| R1 | VfxEditor の再生機能をエフェクト Prefab の中（プレハブモード）でも確認できるように | **実装済み**（2-13） |
| R2 | Anchor 同士は入れ子可能 | §3.2 |
| R3 | Anchor 単体でアセットにする。専用シーンまたはエディタで編集・確認できる | §3.1 / §3.6 |
| R4 | Anchor はあくまでも「生成オフセット情報」のみ | §3.1（Anchor は何を出すかを持たない） |
| R5 | エフェクトだけでなくサウンドも同じ仕組みにする | §3.3 / §3.5 |
| R6 | ランダム、生成ディレイなど「生成時のイベント」が欲しい | §3.1 / §3.5 |
| R7 | 既存ボーンやヒエラルキーの流用もできるように（2026-09-08 追加） | §3.9 |

## 2. 現状（As-Is）

- `AnchorDef`（struct）が `VfxData.Anchor` / `SeData.Anchor` に**埋め込み**で存在する（Space / Path / LocalOffset / LocalEuler / LocalScale / FollowRotation / DetachOnStop）。同じ内容を複数の VFX/SE で使いたい場合はコピーするしかない
- ランダム（位置半径・回転・スケール）はシーン配置型の `AnchorPoint`（MonoBehaviour）にしかない。データ側（AnchorDef）にはない
- 入れ子は「シーン上の Transform 階層」でしか表現できない（AnchorRig の子に AnchorPoint）。データ同士の参照関係はない
- 生成ディレイ・確率・イベントは無い（Presentation 層 [08] のトラックでしか時間差を作れない）
- 姿勢の式は `AnchorPose`（[04] §2.6）に統一済み。解決は `AnchorResolver`。VFX/SE で共用

## 3. 提案（To-Be）

### 3.1 AnchorData アセット（R3 / R4 / R6）

`AssetType.Anchor` を **enum 末尾に追加**し、他種別と同じく ID（`AssetId<AnchorMarker>`、生成定数クラス `ANCHORID`）で参照する。

```csharp
[AssetIdDefinition(AssetType.Anchor, typeof(AnchorMarker), "ANCHORID")]
public sealed class AnchorData : AssetDataBase
{
    [Header("入れ子")]
    public AssetId<AnchorMarker> Parent;      // 0 = ルート。子は親の姿勢を基準にオフセットを積む(§3.2)

    [Header("基準(ルートのみ有効。子は親から継承)")]
    public AnchorSpace Space;                 // World / BoneName / NamedObject / ContextTarget(既存 enum)
    public string Path;

    [Header("オフセット")]
    public Vector3 LocalOffset;
    public Vector3 LocalEuler;
    public Vector3 LocalScale = Vector3.one;  // SE には無関係
    public bool FollowRotation;
    public bool DetachOnStop;

    [Header("ランダム(Spawn/Play 時に 1 回サンプリング)")]
    [Min(0)] public float PositionJitterRadius;
    public Vector3 EulerJitter;
    public Vector2 ScaleRange = new(1, 1);    // SE には無関係

    [Header("生成タイミング")]
    [Min(0)] public float DelaySec;           // 生成を遅らせる秒数
    [Min(0)] public float DelayJitterSec;     // DelaySec への +ランダム
    [Range(0, 1)] public float SpawnChance = 1f;  // 1 未満で確率生成(0 は「出さない」)
}
```

- Anchor は **「どこに・いつ・どう置くか」だけ**を持ち、「何を出すか」（VFX/SE の参照）は持たない（R4）。逆方向、つまり VfxData / SeData 側が `AnchorId` で Anchor を参照する（§3.3）
- 既存の `AnchorDef` の項目はそのまま `AnchorData` へ写す。`AnchorPose` の式は変えない（`AnchorData` → `AnchorDef` 相当の値へ展開して同じ式を通す）
- ランダム 3 項目は `AnchorPoint` と同じ意味。データ側で持てるようになるので、**AnchorPoint 側のランダムは「後方互換のため残すが新規では使わない」**扱いにする（§6-4）

### 3.2 入れ子（R2）

**採用案: 親子連鎖（フレームの合成）。** 子 Anchor の LocalOffset/LocalEuler/LocalScale は「親 Anchor で決まった姿勢」を基準に積む。

```
ルート: target = AnchorResolver.Resolve(root.Space, root.Path, contextRoot)
姿勢(ルート) = AnchorPose(root, target)                  ← 既存の式
姿勢(子)     = 姿勢(親) × Offset(子) × Jitter(子)         ← 親の位置・回転・スケールを Transform 相当として同じ式を再適用
```

- 例: `Anchor_RightHand`（BoneName=RightHand）→ 子 `Anchor_Muzzle`（+0.3m 前方）→ 孫 `Anchor_MuzzleSpark`（回転ランダム ±15°）。銃口位置を変えれば孫まで追従する
- `FollowRotation` / `DetachOnStop` / `Space` / `Path` は**ルートの値だけ**が効く（子は継承）。`DelaySec` / `SpawnChance` は**各段の値を合算**（Delay は加算、Chance は乗算）
- 追従（Tick）は「ルートの解決先 Transform」に対して行う。連鎖のオフセットは Spawn 時に 1 回だけ合成して `Instance` が保持する（[04] §2.6 の extra/jitter と同じ扱い。毎フレーム連鎖を辿らない＝GC/計算コスト 0）
- 循環（A→B→A）と深さ 8 超は Validation Error。ランタイムでは警告 + ルート扱い（例外で止めない）
- **不採用（§6-1 で確認）:** 「親を指定すると子 Anchor 全部に一斉生成する（扇状展開）」案。これは Anchor ではなく Presentation [08] の役割で、Anchor に持たせると R4（オフセット情報のみ）と衝突する

### 3.3 参照方法と優先順位（R5）

`VfxData` / `SeData` に `AssetId<AnchorMarker> AnchorId` を**追加**する（既存の埋め込み `Anchor` は削除しない）。

```
解決の優先順位(VFX/SE 共通):
  1. 呼び出し引数の anchorId          Vfx.Spawn(id, anchorId, ctx) / Audio.PlaySe(id, anchorId, ctx)
  2. 呼び出し引数の位置/Transform     従来どおり(Data を完全に上書き)
  3. Data.AnchorId (≠0)              AnchorData を Registry から解決
  4. Data.Anchor (埋め込み。旧形式)   従来どおり
  5. World 原点
```

- ファサード追加: `Vfx.Spawn(VfxId, AnchorId)` / `Vfx.Spawn(VfxId, AnchorId, Transform ctx)` / `Audio.PlaySe(SeId, AnchorId)` / `Audio.PlaySe(SeId, AnchorId, Transform ctx)`。`PlayContext` を導入する場合はそこに `AnchorId` を載せる（[19] §2 の未着手項目と統合）
- `AnchorId` 未登録は他種別と同じく **Placeholder（= World 原点、警告 1 回）**で継続

### 3.4 AnchorPoint / AnchorRig（シーン配置型）の位置づけ

- 残す。役割は「**シーン/プレハブ上の Transform を名前で示すマーカー**」に限定していく
- `AnchorData.Path` が AnchorPoint 名を指せば従来どおり解決される（既存 `AnchorResolver` のまま）
- AnchorPoint の `SpawnOffset` / ランダム 3 項目は互換のため残し、解決先に AnchorPoint がある場合は**従来どおり加算**する（挙動を変えない）。エディタでは「AnchorData 側で設定してください」と案内する（§6-4）

### 3.5 ランタイム（生成ディレイ・確率）（R6）

- `DelaySec > 0` のとき Manager は **Pending 状態の Instance** を作って Handle を返す。`IsPlaying` は true、`GetGameObject` は null。`Tick` でカウントダウンし、到達時に実体を生成（VFX）/ 再生（SE）。Pending 中の `Stop/Kill` は生成をキャンセルする。Handle の世代管理は既存のまま
- `SpawnChance` に外れた場合は `Handle.Invalid` を返す（警告なし。設計上の期待動作）
- Pending は `_allActive` と同じ台帳で管理し、`StopAll` / `OnSceneUnload` / Pause に従う（Pause 中はカウントダウンも止める）
- 定常経路にクロージャ・LINQ・boxing を持ち込まない（[12] §3）。ランダムは `UnityEngine.Random`、Pending は構造体フィールドで表現
- ネットワーク（[14]）: Cosmetic 同期の payload に `anchorId` を含めるかは別チケット。Delay/Random はローカル解決なので同期不要（Seed 同期が必要になった時点で検討）

### 3.6 エディタ（R3）

| 機能 | 内容 |
|---|---|
| AnchorEditor（新規 `Tools/D-Drive/Editors/Anchor`） | 対象 AnchorData を選択追従 + ロック。**親子の連鎖をパンくずで表示**（ルート → … → 対象）。スポーン先（シーン内オブジェクト / AnchorRig）を指定して解決状態を表示。SceneView に姿勢ギズモ（ルートから対象までの各段を線で結ぶ、ランダム半径の球、Delay の秒数ラベル）。移動/回転ハンドルで LocalOffset/LocalEuler を逆変換して保存（`VfxEditorWindow.Anchor.cs` の SceneView ハンドル部分を `AnchorSceneHandles` として共通化し、両エディタで使う） |
| 試し出し | ウィンドウ内で「確認用 VFX」「確認用 SE」を 1 つずつ選び ▶ で実 Manager 経由に再生する（ADR-4。`SceneVfxPreviewDriver` を再利用、SE は既存の AudioEditor プレビュー経路）。Delay/Chance/ランダムの効き方をその場で確認できる。プレハブモード内でも可（2-13 の仕組み） |
| 専用シーン | 新設しない。VFX 確認用シーン（AnchorRig 配置済み）をそのまま使う（§6-5） |
| SceneView 描画権（2026-09-08 追加） | 複数ウィンドウの描画が重なる対策として `Editor/Preview/SceneGuiOwner`。最後にフォーカスしたウィンドウだけが連鎖・ランダム半径・ハンドルを描き、他は薄い目印のみ。各ウィンドウの「SceneView 表示」チェックで完全オフ |
| VfxEditor / AudioEditor の Anchor 欄 | 先頭に「Anchor アセット」ドロップダウン（AnchorId）+「開く」ボタン。AnchorId≠0 のときは埋め込み Anchor 欄を折りたたみ「AnchorData 側で編集」と表示。0 のときは従来どおり埋め込みを編集 |
| 「埋め込み → アセット化」ボタン | 現在の埋め込み Anchor から AnchorData を新規作成して AnchorId を差し替える（移行補助。埋め込み値は残す） |
| AssetBrowser | 種別タブに Anchor を追加。作成ダイアログ・命名（接頭辞 `ANC`、配置 `Assets/GameData/Anchor/<Category>/`）・カタログ `AnchorCatalog` を `AssetNamingService` / `AssetCreationService.GetCatalogName` に追加 |

### 3.7 Validation（AnchorDataValidator 新規）

| 検査 | 重大度 |
|---|---|
| Parent が循環 / 深さ 8 超 | Error |
| Parent が未登録 ID | Error |
| ルートで Space=BoneName/NamedObject かつ Path 空 | Error |
| 子で Space/Path/FollowRotation/DetachOnStop が既定値以外（効かない設定） | Warning |
| SpawnChance = 0 | Warning（「出ない」意図が明確なら理由をコメント） |
| DelaySec + DelayJitterSec > 10 秒 | Warning |
| VfxData/SeData: AnchorId≠0 と埋め込み Anchor が両方非既定 | Warning（埋め込みは無視される旨） |

### 3.8 互換性 / 移行

- **フィールド削除・型変更なし**（[12] §3 Data/シリアライズ規約）。既存アセットは `AnchorId=0` のため挙動不変
- `AssetType.Anchor` は enum 末尾に追加（並び替え禁止）
- 生成定数 `ANCHORID` はコード生成の既存経路（`AssetIdGenerator`）で自動対応
- 埋め込み `AnchorDef` の廃止は**本改定では行わない**。全アセットの移行が済んだ段階で別途判断

### 3.9 既存ボーン・ヒエラルキーの流用（R7）

Anchor を新しく「置く」だけでなく、モデルのボーンやシーン/プレハブの既存階層を**そのまま基準にできる**ようにする。

| 手段 | 内容 |
|---|---|
| 名前で参照（既存機能） | ルート Anchor の `Space=BoneName/NamedObject` + `Path` で既存ボーン・オブジェクト名を直接指す。解決は既存 `AnchorResolver`（contextRoot 配下の名前検索）のまま |
| ヒエラルキーから選ぶ | AnchorEditor のスポーン先（キャラクター等）を指定すると、その階層のボーン/オブジェクトを**ドロップダウンで選べる**（VfxEditor と同じ一覧。AnchorPoint は ★ 付きで先頭）。手入力不要 |
| 選択した Transform から Anchor を作成 | Hierarchy で選んだ Transform（ボーン配下に置いた空オブジェクト、AnchorPoint、既存の子オブジェクト等）から `AnchorData` を生成する。**最寄りの「名前で解決できる親」**（AnchorPoint または指定した基準ボーン）を `Space/Path` にし、そこからの相対位置・回転を `LocalOffset/LocalEuler` に書き込む。生成後はその Transform 自体は不要（消しても Anchor は残る） |
| AnchorRig から一括生成 | AnchorRig（AnchorPoint 群）を選んで「Anchor アセットを一括生成」→ AnchorPoint ごとに `AnchorData`（Space=NamedObject, Path=AnchorPoint 名, SpawnOffset/ランダムを写す）を作る。AnchorPoint の親子関係は `Parent` の連鎖として写す |
| 埋め込み → アセット化 | §3.6 の「アセット化」ボタン（VfxData/SeData の埋め込み Anchor から生成） |

命名は `ANC_<Category>_<Identifier>`。一括生成時は Category = スポーン先の名前、Identifier = AnchorPoint 名から `Anchor_` 接頭辞を除いた PascalCase。

## 4. 影響範囲（変更予定ファイル）

| 層 | ファイル | 変更 |
|---|---|---|
| Foundation | `Identity/AssetType.cs` | `Anchor` 追加（末尾） |
| Runtime | `Anchoring/AnchorData.cs`（新規）/ `AnchorMarker` / `AnchorDataValidator.cs`（新規） | §3.1 / §3.7 |
| Runtime | `Anchoring/AnchorChain.cs`（新規） | 連鎖を `AnchorDef` 相当の値 + 合算 Delay/Chance に展開する純粋関数（テスト対象） |
| Runtime | `Vfx/VfxData.cs` `Audio/SeData.cs` | `AnchorId` 追加 |
| Runtime | `Vfx/VfxManager.cs` `Audio/AudioManager.cs` | 優先順位 §3.3、Pending §3.5 |
| Runtime | `Vfx/Vfx.cs` `Audio/Audio.cs` | ファサード追加 |
| Editor | `Anchor/AnchorEditorWindow.cs`（新規、partial 分割）/ `Preview/AnchorSceneHandles.cs`（新規、Vfx から抽出） | §3.6 |
| Editor | `Vfx/VfxEditorWindow.Anchor.cs` `Audio/AudioEditorWindow*.cs` | Anchor 欄の改修 |
| Editor | `AssetBrowser/AssetNamingService.cs` `AssetCreationService.cs` `Menu/DDriveMenu.cs` | 種別追加・メニュー |
| Tests | `AnchorChainTests` `AnchorDataValidatorTests` `VfxManagerTests`(Pending/優先順位) `AudioManagerTests` `AnchorEditorTests` | 新規/追加 |
| docs | 03 §2, 04 §2/§3/§5, 02(Validation 表), 09(メニュー), 10(命名), 11 | 決定後に反映 |

概算: 基盤 5 人日 + エディタ 5 人日 + テスト/文書 2 人日 = **約 12 人日**（決定内容で ±3）。

## 5. チケット分割案（決定後に 11_tasks.md へ転記）

| # | 内容 | 依存 |
|---|---|---|
| 2-14a | AssetType.Anchor / AnchorData / AnchorMarker / 命名・カタログ・コード生成 | — |
| 2-14b | AnchorChain（連鎖展開 + Delay/Chance 合算）+ Validator + テスト | 2-14a |
| 2-14c | VfxManager: AnchorId 優先順位 + Pending（Delay/Chance）+ ファサード + テスト | 2-14b |
| 2-14d | AudioManager: 同上（SE） | 2-14b |
| 2-14e | AnchorSceneHandles 抽出 + AnchorEditorWindow（連鎖表示・ギズモ・試し出し） | 2-14b, 2-13 |
| 2-14f | VfxEditor / AudioEditor の Anchor 欄改修 + 「アセット化」ボタン | 2-14e |
| 2-14g | docs 反映（03/04/02/09/10/11）+ DesignerManual | 全部 |

## 6. 決定項目（2026-09-08 決定: すべて推奨案を採用）

| # | 質問 | 決定（=推奨） | 理由 |
|---|---|---|---|
| 1 | 入れ子の意味は「親子連鎖（子は親の位置基準）」でよいか。「親を指定すると子全部に一斉生成」は Presentation [08] 側に任せてよいか | **連鎖**。一斉生成は Anchor に持たせない | R4「オフセット情報のみ」と整合。一斉生成は VFX/SE の組み合わせ＝演出の責務 |
| 2 | Anchor アセットの粒度は「1 ノード = 1 アセット（Parent で連鎖）」でよいか。代案は「1 アセットに複数ノードを内包（ツリー）」 | **1 ノード = 1 アセット** | VfxData/SeData から ID 1 つで参照できる。既存の ID / カタログ / コード生成の仕組みがそのまま使える。ツリー内包だと「ID + ノード名」の 2 段参照が必要になる |
| 3 | 「生成時のイベント」の範囲: Delay / ランダム / 確率 で足りるか。追加候補 = 繰り返し（Count/Interval）、OnSpawn で別アセットを鳴らす（`AssetEvent`） | **v1 は Delay / ランダム / 確率**。繰り返し・OnSpawn 連鎖は Presentation [08] に任せる | Anchor に「何を出すか」を持ち込まない（R4）。繰り返しは VFX の LifeMode/Loop と役割が重なる |
| 4 | AnchorPoint（シーン配置型）のランダム 3 項目と SpawnOffset は「互換のため残す」でよいか。代案は AnchorData 優先で AnchorPoint 側を無視 | **残して従来どおり加算** | 既存アセット・プレハブの挙動を変えない。新規はエディタで AnchorData 側へ誘導 |
| 5 | 専用シーンは新設せず VFX 確認用シーンを共用でよいか | **共用** | AnchorRig 配置済みで、VFX/SE の試し出しも同じシーンで済む |
| 6 | 埋め込み `AnchorDef` は当面残す（削除しない）でよいか | **残す** | フィールド削除は移行コード + 手順書が必要（[12] §3）。移行完了後に別判断 |
| 7 | SE のスケール項目（LocalScale / ScaleRange）は AnchorData に置いたまま SE では無視、でよいか | **無視** | Anchor を VFX/SE で 1 種類にするため。SE 側の Validator で「効かない」旨は出さない（共用前提） |
