# 51. T-Drive 連携（FacialController + Toon マテリアル）（FC チケット、設計）

関連: [11_tasks.md](11_tasks.md)「FC チケット」節 / [06_material_texture.md](06_material_texture.md)（Material・Texture。FC-11/13/15/19） / [26_timeline.md](26_timeline.md)（Cutscene）/ [05_model_animation.md](05_model_animation.md)（Model・Anim・BlendShape）/ [42_distribution.md](42_distribution.md) §5（互換性ポリシー）/ [13_extensions.md](13_extensions.md)（旧 7-8 の元になった拡張案）

T-Drive（別リポジトリ。Maya + Unity のトゥーン / 表情ツール）が **FacialController**（カメラ角度に応じた顔の補正。Unity パッケージ `com.tdrive.facial`）と **Toon**（キャラクターのルック。`com.tdrive.toon`）を実装する。D-Drive は **Facial を自分では実装せず T-Drive のものを使い、キャラクターのルックも T-Drive に一任して互換ブリッジでつなぐ**。本書は「D-Drive 側に何を足せば T-Drive と相性が良くなるか」を、T-Drive 側の調査（doc16 = Facial、doc17 = Toon マテリアル）を **実コードで裏取りして** FC-0〜FC-20 のチケットに落とした設計書。さらに T-Drive の最新実装（コミット `f27702e`、FacialController のコア F0-2 / F0-3）が前提にしている決まりのうち D-Drive 側が守る / 確認すべきものを「**f27702e 由来・まとめ役の抽出**」として FC-20 と各チケットに取り込む（このコミットに D-Drive への明示的な要望は無い）。

- 2026-10-03 作成（設計のみ。コード・アセットの変更なし）。**チケット番号の注意**: doc17 の「M-n」は D-Drive 既存の M チケット（M-1 / M-2 / M-3 = MS2026 フィードバック）と衝突するため、本書では「doc17 M-n」と書き、チケットは FC-11〜FC-19 を使う（対応表は [11](11_tasks.md) FC 節の冒頭）。裏取りは `Packages/com.ddrive.core/` の v1.3.1（HEAD `9f40cbb`）を**読んだ結果**であり、Unity は起動しておらずコンパイル・実行は一切していない。確認できなかった点は「未確認」と書く
- チケット表と日数は [11_tasks.md](11_tasks.md)「FC チケット」節が正本。本書は設計の根拠と詳細
- 人による確認の手順は [52_manual_verification_fc.md](52_manual_verification_fc.md)

## 0. 要約

| 項目 | 内容 |
|---|---|
| 決定 | Facial は **T-Drive 版（`com.tdrive.facial`）が正**。D-Drive 内に `Facial` 種別は作らない。旧チケット 7-8（D-Drive 内に Facial を移植、26〜29 日）は「T-Drive 版を使う。D-Drive 側は FC チケットの小さな追加のみ」に書き換える |
| 範囲 | Facial（doc16 → FC-1〜FC-10）、Toon マテリアル（doc17 → FC-11〜FC-19）、f27702e 由来の契約（FC-20）。FC-0〜FC-20 の 21 チケット、実装対象は約 20 人日 |
| 追加するもの | すべて **追加のみ**（互換区分 MINOR）。Facial 専用ではなく**汎用の拡張点**として作る（Facial 以外の外部パッケージにも効く） |
| 優先 | 推奨順 **FC-1 → FC-2 + FC-12（同一 PR）→ FC-11 → FC-20 → FC-10 → FC-15 → FC-5 → FC-4 → FC-6 → FC-14 → FC-3 → FC-7 → FC-19**。条件付き / 保留 = FC-8・FC-9・FC-13、低優先で後回し = FC-16・FC-17・FC-18 |
| 版 | **v1.4.0（MINOR）** 見込み |
| 実装方式 | 実装者は Sonnet サブエージェント、1 チケット = 1 PR、Unity 検証（コンパイル・EditMode/PlayMode）はまとめ役がメイン checkout で行う |
| 実コードとの相違 | doc16 に対し §3.2 の 7 件（R-1〜R-7。うち 3 件は T-Drive 側の前提に影響: FC-5 の順序制御・Pause 中の上書き・IPoolable）、doc17 に対し §3.4 の 4 件（R-8〜R-11。うち R-9 = 再取り込みが既存 MaterialData のシェーダーも上書きする、は doc17 より広い） |

## 1. 決定と出典（2026-10-03）

| 項目 | 内容 |
|---|---|
| 出典 | T-Drive リポジトリ https://github.com/wrenchsun/T-Drive コミット `97bed77`（2026-10-03）の `docs/14_facial_controller_spec.md`（仕様。§7 が D-Drive / Timeline 連携）・`15_facial_controller_design.md`（設計。§5 が Unity パッケージ、§5.5 が D-Drive ブリッジ）・`16_ddrive_changes_for_facial.md`（D-Drive 側の変更調査。本書の起点）・`tasks.md`（T-Drive 側チケット。FT-2 = D-Drive ブリッジ、FT-4 = D-Drive 側への提案の起票） |
| ユーザー決定 | doc16 を受け入れる。「T-Drive の表情機能を D-Drive の将来の表情機能として使う。D-Drive では実装せず T-Drive のものを使う。だから相性が良くなるようにチケットを作る」 |
| doc16 §2（旧 7-8） | **案 1 を採用**: T-Drive 版を正とする。案 2（D-Drive 内に実装）・案 3（両方作る）は不採用 |
| doc16 §3（A 群） | 「D-Drive を変更しなくても動く」と T-Drive が依存している挙動。**今後も壊さない契約として固定する**（FC-10） |
| doc16 §4（C 群 = C-1〜C-9） | FC-1〜FC-9 としてチケット化（FC-8・FC-9 は保留） |
| doc16 §5（D 群 = D-1〜D-7） | **不採用**（§5 に理由） |
| 旧 7-8 の移植元パス | 誤記: `C:\Users\yamag\wrench\FacialController_UE` → 正は `C:\Users\yamag\wrench\ue\FacialController_UE`（doc16 §2 の指摘。[11](11_tasks.md) の 7-8 に注記済み） |
| 出典（Toon マテリアル） | 同リポジトリ コミット `4d44a32`（2026-10-03）の `docs/17_ddrive_toon_materials.md`（D-Drive でのマテリアルの扱い・罠 5 件・D-Drive 側推奨変更 M-1〜M-9）・`08_unity_port_plan.md` §6（ブリッジ `TDrive.Toon.DDriveBridge`）・`tasks.md` U-21〜U-23。ユーザー指示（2026-10-03）で doc17 の推奨変更も同じ FC チケット節に取り込む。doc17 §5 の M-1〜M-9 = FC-11〜FC-19 |
| 出典（コア実装の前提） | 同リポジトリ コミット `f27702e`（FacialController のコア F0-2 / F0-3）: `naming.py`（シェイプ名の規則）・`space.py`（座標系）・`conformance_README.md`（共通テストデータ）・`fcpose.schema.json`・`15_facial_controller_design.md` §3.2〜§3.3。**D-Drive への明示的な要望は無く、まとめ役が「D-Drive が守る / 確認すべき契約」を抜き出した**（FC-20、FC-2 / FC-3 / FC-10 への追記） |
| doc16 の調査対象パス | doc16 冒頭は `C:\Users\yamag\wrench\unity\D-Drive` と書くが、実際のこのリポジトリは `C:\Users\yamag\wrench\D-Drive`（内容は v1.3.1 / `9f40cbb` で一致） |

## 2. 役割分担と依存の原則

| 環境 | 役割 |
|---|---|
| **Maya**（T-Drive） | Facial データの**作成・編集**（格子・ポーズ・感情レイヤー・ベイク・検証・Unity 向け出力） |
| **T-Drive Unity パッケージ**（`com.tdrive.facial`） | **ランタイム**（`FacialCorrectionRunner`・`FacialCorrectionTrack`・取り込み・プレビュー・調整） |
| **D-Drive** | **受け口だけ**（バインド・プール・Timeline・取り込み・検証の拡張点）。Facial のデータ型・計算・エディタは持たない |

**依存の原則**（Toon のブリッジと同じ。T-Drive 側 `Bridges.DDrive` asmdef が接続を持つ）:

1. **D-Drive のコード・asmdef は `TDrive.*` を参照しない**。D-Drive は T-Drive の存在を知らない
2. 受け口は **Facial 専用にしない**。「同じモデルへのバインド」「外部マーカー」「取り込み完了通知」「現在の視点」はどれも Facial 以外の外部パッケージにも使える汎用の拡張点として設計する（名前に Facial を入れない）
3. ブリッジ（D-Drive があるときだけコンパイルされる asmdef。`com.ddrive.core` の `versionDefines` で有無・版を判定）は **T-Drive 側が持つ**。D-Drive が新しい拡張点を入れる前の版（< 1.4.0）でも T-Drive は回避策で動く（doc16 の前提）。D-Drive 1.4.0 以降は回避策が不要になる
4. 追加はすべて [42](42_distribution.md) §5 に従う（enum は末尾追加・シリアライズは追加フィールドのみ・公開 API は追加のみ・新しい検査は Warning から）

## 3. 実コードでの裏取り（doc16 A 群 / doc17 / f27702e）

### 3.1 doc16 §3「A 群」の対応表（A-1〜A-9）

| # | doc16 の主張 | D-Drive 側の実装箇所（確認した事実） | 判定 | 契約テスト（FC-10） |
|---|---|---|---|---|
| A-1 | Prefab に Runner を付ければ動く（プールで出すだけ） | `ModelsManager.SpawnData`（`Runtime/Model/ModelsManager.cs:131`）が `_pool.Rent(data.Prefab)`（`PoolService.Rent`、`SetActive(true)`）→ `Despawn` で `_pool.Return`（`ForceReturn`、`SetActive(false)`）。Prefab 上のコンポーネントは Instantiate されたまま生きる。`ModelInstancePoolable` を Spawn 時にルートへ `AddComponent`（同 `:187-190`） | **一致**。ただし相違 R-6（IPoolable は最初の 1 個だけ） | E-1 |
| A-2 | Timeline のトラックが標準の仕組みで見つかり、`CutsceneManager.Tick` が `time += dt; Evaluate()` | トラックの登録簿は無く `TimelineAsset` をそのまま `Director.playableAsset` に設定（`CutsceneManager.cs:253`）。`Tick`（`:1270`）は `instance.Elapsed += dt * Speed` → `Director.time = …` → `Director.Evaluate()`（Director は `DirectorUpdateMode.Manual`、`:641-644`）。Tick は `GameLoopDriver.Update`（`Runtime/Loop/GameLoopDriver.cs:19`）から呼ばれる | **一致**（Evaluate は Update 内。Runner の LateUpdate が後に走る） | E-2 |
| A-3 | シーク・スキップ・一時停止・速度変更・ネット同期に追従 | `Seek`（`:1123`）/ `Skip` は `ApplySeek` で `Director.time` を設定して `Evaluate()`。`SetSpeed`（`:1153`）は dt に乗る。ネット受信側は `elapsedSeek` で開始し初回 `Evaluate()`（`:263`）。**一時停止（`instance.Paused`）中は Tick が `continue` するため Evaluate が呼ばれない**（`:1281-1284`） | **部分一致**。相違 R-3 | E-3 |
| A-4 | 再取り込みでトラックが消えない（名前と型で照合） | `CutsceneImportService.FindExistingTrack<T>`（`Editor/Cutscene/CutsceneImportService.cs:588`）は `track is T && track.name == name`。`BuildOrUpdateAnimationRoleTrack`（`:445`）・`BuildOrUpdateCameraTrack` は自分が作る 2 型（`AnimationTrack`・`CutsceneCameraTrack`）だけ生成・更新し、他のトラックは削除しない。`data.Bindings` も既存配列から始めて追記（`:210`）なので外部が足した Binding も残る。既存テスト `ProcessPaths_Reimport_PreservesManuallyAddedTrackAndCameraSettings`（`DevRepoOnly`）が素の `AnimationTrack` で確認済み | **一致** | E-4 |
| A-5 | 知らないトラックで検証の警告は出ない | `CutsceneDataValidator.ValidateStandardTrackUsage`（`Runtime/Cutscene/CutsceneDataValidator.cs`）が警告するのは標準 `AudioTrack` / `ControlTrack` / `SignalTrack` とカメラ役割上の `AnimationTrack` だけ。`CutsceneFpsValidator`（Editor）は `AnimationTrack` / Camera クリップのみ。`ValidateReferencedAssets` は既知のクリップ型（SE/VFX/AnchorGroup/UI/Presentation）だけを `switch` し、未知は素通り | **一致**（副作用: 外部クリップの参照は検査されない = FC-7 に関係） | E-5 |
| A-6 | T-Drive の `IValidator` が CI に載る（全アセンブリから自動発見） | `CI.DiscoverValidators`（`Editor/Validation/CI.cs:137`）が `AppDomain.GetAssemblies()` を走査し、`DDrive.Tests` で始まる名前のアセンブリ**だけ**除外。**public な引数なしコンストラクタ**が必須（`GetConstructor(Type.EmptyTypes)`） | **一致**。ただし相違 R-4（検査対象は `AssetDataBase` だけ） | E-6 |
| A-7 | `.fcpose` / `.fctrack` 等の `ScriptedImporter` / `AssetPostprocessor` を外部アセンブリから使える | Unity 標準の仕組みなので D-Drive 側の制約なし。D-Drive の汎用取り込み（`ImportRuleService`）は `SourceAssets/<種別>/` 配下で対象外の拡張子・フォルダに**警告ログを 1 回出すだけ**（例外にしない）。`SourceAssets/Cutscene/` は `KnownNonTargetTypeFolders`（`ImportRuleService.cs:95`）に入っており案内ログも出ない。D-Drive の `CutsceneFbxPostprocessor` は `.fbx` 以外を無視（`CutsceneImportService.ProcessPaths` の拡張子判定） | **一致**（`SourceAssets/Facial/` に置くと警告が出る = FC-6） | E-7 |
| A-8 | アニメの BlendShape（`AnimData.BlendShapes`）と衝突しない（D-Drive は Update で名前指定、Runner は LateUpdate で `FC_*` だけ） | `AnimManager.Tick`（`Runtime/Anim/AnimManager.cs:388`、`GameLoopDriver.Update` から）→ `AnimatorProxy.ApplyBlendShapes`（`AnimatorProxy.cs:119`）が `AnimData.BlendShapes[].ShapeName` の**指定名だけ**を `SetBlendShapeWeight`。`FC_*` を触らない。Animator 自身のブレンドシェイプカーブも Update 系（評価後 LateUpdate の前） | **一致**（`DefaultExecutionOrder` 10000 の Runner LateUpdate が最後）。注: Anim の停止・Despawn で書いた重みは**戻らない**（FC-2 が埋める） | E-8 |
| A-9 | `IAssetManager` + `GameLoop.Register` が公開 | `IAssetManager`（`Foundation/Manager/IAssetManager.cs`）・`GameLoop.Register(IAssetManager)`（`Foundation/Manager/GameLoop.cs:11`）は public。インスタンスは `DDriveRuntimeBootstrap.Loop.GameLoop`（`GameLoopDriver.cs:10`、Bootstrap の `Loop` は `:116`） | **一致**（今回 T-Drive は使わない） | E-9 |

### 3.2 doc16 と実コードの相違（R-1〜R-7）

| # | 相違 | doc16 の記述 | 実コード | 影響・対応 |
|---|---|---|---|---|
| **R-1** | **FC-5（C-5）の順序制御は `postprocessOrder` では効かない** | 「回避策 = AssetPostprocessor の順番（postprocessOrder）に頼る + 同名ショットを自分で探す」 | `CutsceneFbxPostprocessor.OnPostprocessAllAssets`（`Editor/Cutscene/CutsceneFbxPostprocessor.cs:75`）は取り込み処理を `EditorApplication.delayCall += Flush`（`:91-92`）で**後回し**にする。`CutsceneData` / `.playable` が作られるのは `OnPostprocessAllAssets` が全部終わった後の `delayCall`。T-Drive の `AssetPostprocessor` が `postprocessOrder` をどれだけ大きくしても、D-Drive の `Flush` より前に走る | T-Drive 側の回避策は「同じ `delayCall` に自分も登録する」か「`.fctrack` 取り込み時に既にある `.playable` を探す」の 2 経路で、**FBX と `.fctrack` の到着順に依存して脆い**。FC-5 の価値は doc16 の想定より高い（優先は「中」のまま推奨順を FC-5 の位置まで引き上げた） |
| **R-2** | **FC-3（C-3）: 現実装ではカットシーン中の `Camera.main` は既にカット / ブレンド後の姿勢** | 「カットシーン中はカットのカメラ。`Camera.main` では決まらない」 | カットシーンのカメラは**別カメラではなく `Camera.main` 自体**に書く。`DDriveCutsceneCameraApplier`（`Runtime/Cutscene/DDriveCutsceneCameraApplier.cs:31`、`DefaultExecutionOrder(1000)`）が LateUpdate でゲームカメラとブレンド（`Weight`）して Camera/Volume を上書きする。したがって実行順 1000 より**後**の LateUpdate（Runner は 10000）で `Camera.main` を読めばカット姿勢が得られる。`CutsceneCameraStateHolder` の生値は Update 時点の中間値（ブレンド前）で視点としては使えない。D-Drive に「現在の視点」を返す既存 API は無く（`Camera.main` 直参照が Runtime だけでも `CameraFxManager`・`Anim2DFacing`・`CutsceneManager` にある）、複数カメラ / 分割画面は未対応 | FC-3 は「カットシーン中だけ特別」ではなく**分割画面・視点の上書き・実行順の契約を API にする**位置づけに修正（§4.4）。T-Drive の Runner は実行順 10000 なので現状でも `Camera.main` で正しく動く |
| **R-3** | **インスタンス単位の一時停止（`Cutscene.Pause`）中は Timeline が評価されない** | 「一時停止に追従」「`PushOverride` は毎フレーム消える」前提 | `CutsceneManager.Tick`（`:1281-1284`）は `instance.Paused` なら `continue` するので `Director.Evaluate()` も呼ばれない。`Seek` は `Paused` でも `Evaluate()` する（`:1123-1143`）。グローバルな Pause（`PauseWithGame`）も `OnPause`（`:1450`）で同じ `instance.Paused` を立てる | T-Drive の Mixer が「毎フレーム `PushOverride`、次フレームで消える」設計だと、**一時停止中に補正が一瞬デフォルトへ戻る**。T-Drive 側で「前回値を保持し、Mixer の `OnPlayableDestroy` / グラフ停止で解除」にするのが素直（§7 で返す）。D-Drive 側は動作を契約として固定（E-3）するだけで変えない |
| **R-4** | **`IValidator` の検査対象は `AssetDataBase`（D-Drive の Data）だけ** | 「T-Drive 側の検証を D-Drive の検証（CI）に載せられる」 | `ValidatorRegistry.RunAll`（`Foundation/Validation/ValidatorRegistry.cs`）は `AssetDataBase` の列を種別（`AssetIdDefinition` の `AssetType`）で振り分ける。`FacialCorrectionData`（ただの `ScriptableObject`）は検査対象にならない | T-Drive の検証は **`CutsceneData`（`Target = AssetType.Cutscene`）や `ModelData`（`AssetType.Model`）を入口にして**、`.playable` 内のトラックや Prefab 上の Runner を調べる形にする（doc15 §5.5 の「検証」行はこの形）。T-Drive 自身のデータの検査は T-Drive のエディタ検証に残す |
| **R-5** | **FC-7（C-7）: 依存グラフは AssetId 参照だけで、`.playable`（Timeline）は対象外** | 「Data アセット本体しか歩いていないなら…（未確認）」 | `DependencyGraphService`（`Editor/Dependencies/`）の対象は `.asset`（`AssetDataBase`）・`.prefab`・`.unity` のみ（`DependencyGraphService.cs:248-268`）。`WalkProperties` は `AssetId<T>` / `AssetRef` 型のプロパティだけ拾う（`DependencyGraphCollector.cs`）。`Timeline` / `Playable` への言及は `Editor/Dependencies` 全体で 0 件。つまり **D-Drive 自身のクリップ（`CutsceneSeClip.SeId` 等の `AssetId`）も「使用箇所」に出ない**（既存の穴）。Facial のデータは `AssetId` ではなく直接参照（Prefab / クリップから `ScriptableObject`）なので、`.playable` を歩くようにしても依存グラフには出ない | FC-7 は「T-Drive 側の都合」というより **D-Drive 自身の既存の穴の調査**。結論の見立てと対応案は §4.8 |
| **R-6** | **`PoolService.ForceReturn` は最初の `IPoolable` にしか `OnReturn` を呼ばない** | （言及なし。A-1 / 「プール返却時のリセット」） | `Foundation/Pool/PoolService.cs:207` の `TryGetComponent<IPoolable>` は**ルートの最初の 1 個**だけを返す。`ModelsManager` は `ModelInstancePoolable` を `AddComponent`（末尾）するだけ。Prefab のルートに別の `IPoolable` 実装があると、そちらだけに `OnReturn` が呼ばれ `ModelInstancePoolable.OnReturn`（モデル台帳の掃除）が**呼ばれない**（実際には発生しない前提で動いている） | T-Drive の Runner は `IPoolable` を**実装しない**（`OnDisable` で戻す。doc14 §6.2 のとおり）。FC-2 で「全 `IPoolable` に呼ぶ」修正を足すかは未決（§8 U-3）。FC-10 にテストで固定 |
| **R-7** | 旧 7-8 本文の内容・doc16 の 7-8 の説明 | 「新種別 Facial（`FacialData`・ベイカー・`FacialManager`・エディタ・`.fcpose.json` 入出力）」 | `docs/11_tasks.md` 7-8 の記述と一致（`docs/13_extensions.md` には 7-8 / Facial の記述なし）。移植元パスだけ誤記 | 7-8 を書き換え（[11](11_tasks.md)）。パス誤記は注記 |

R-1〜R-7 のうち、**T-Drive 側が設計を調整すべきもの** = R-1（FC-5 が入るまで回避策は脆い）・R-3（Pause 中の上書き）・R-4（検証の入口）・R-6（`IPoolable` を実装しない）。§7 で T-Drive の FT-4 への回答としてまとめる。

### 3.3 doc17 §2「D-Drive のマテリアルの仕組み」「§3 / §4」の裏取り（2026-10-03）

| doc17 の主張 | D-Drive 側の実装箇所（確認した事実） | 判定 |
|---|---|---|
| `MaterialData` の欄（`Shader` / `Common` / `Specific` / `RenderQueueOffset` / `RenderingLayerMask` / `Anims` / `SourceMaterial`） | `Runtime/Material/MaterialData.cs`。`Common` は `MaterialCommon`（Albedo・Normal・Mask・Emission・Blend・Cutoff・DoubleSided ほか） | 一致 |
| 共有 Material を `MaterialData` 1 つにつき 1 つ作り、Common → Specific → renderQueue の順に書く | `MaterialManager.GetOrBuild`（`Runtime/Material/MaterialManager.cs:408-`）: `new Material(shader)` → `MaterialCommonBinding.Apply` → `ApplySpecific` → `material.renderQueue`。`hideFlags = DontSave` | 一致 |
| Specific は Float / Int / Bool / Color / Vector / テクスチャ、シェーダーに無いプロパティは飛ばす | `ApplySpecific`（同 `:~452-`）が `HasProperty` で飛ばし、型ごとに `SetFloat` / `SetInteger` / `SetColor` / `SetVector` / `SetTexture`（Bool は 0/1 の Float） | 一致 |
| Specific からキーワード・パスの有効 / 無効・ステンシル・ZTest を指定できない | キーワード・パスの欄は無い（一致）。**ただし `_ZTest` 等の「描画ステート名」のプロパティは、`ApplySpecific` が名前を絞らないため `HasProperty` なら実行時に書かれる**。`MaterialDataValidator` が「共通チャンネル名・描画ステート名が Specific にあると Common を黙って上書き」の Warning を出し、`MaterialSpecificResolver.Merge` は登録せず、`RemoveConflicts` が取り除く（`MaterialCommonNaming`、`MaterialDataValidator.cs`）。`_Stencil*` は予約名一覧に無く Specific として通る | **相違 R-8**（軽微。「入れられない」ではなく「ツールが衝突として警告・除去する」。T-Drive の `_ToonStencilRef` 方式は影響なし） |
| インスタンスごとの値は無い（MPB はモデルでは使っていない）。`Models.SetMaterial` はマテリアル差し替えのみ | `ModelsManager.SetMaterial`（`:288`）は `AssetId<MaterialMarker>` の差し替え。`MaterialPropertyBlock` は `VfxManager` だけが使う（`Runtime/Vfx/VfxManager.cs:69,310`）。Model / Material 側には無い | 一致 |
| `MaterialAnim` は時間駆動のみで共有 Material に書く | `MaterialManager.TickAnims`（`Float` / `OffsetU` / `OffsetV` を `built.Material` に書く）。外から値を渡す口は無い | 一致 |
| 予約名以外はすべて Specific 扱い → `_Toon*` は衝突しない | `MaterialCommonNaming.cs:38-48` の一覧（`_BaseMap` … `_ZTest` `_QueueOffset` ほか）+ `_ST` / `_TexelSize` / `_HDR` の派生接尾辞 + `unity_` / `_Unity` 接頭辞。`_Toon` で始まる名前は含まれない | 一致 |
| Maya 取り込みは FBX の Material を読んで MaterialData を作る。シェーダーは Profile の指定 → 元が `DDrive/` ならそれ → `DDrive/Lit` | `MayaMaterialImporter.ResolveTargetShader`（`Editor/Material/MayaMaterialImporter.cs:240`）の 3 段階のとおり。Maya のアトリビュート・外部 JSON は読まない | 一致 |
| テクスチャは `Assets/SourceAssets`・`Assets/GameData` の下で名前の規則を毎回の取り込みで強制 | `TexturePostprocessor.OnPreprocessTexture` が `TextureImportProfile.FindOrDefault()` → `AppliesTo`（既定 `IncludePathContains = { "Assets/SourceAssets", "Assets/GameData" }`、`Assets/DDrive/` と `/Tests/` は除外）→ `TryMatch`（上から最初に一致）→ `Apply`。既定規則は `_N`（NormalMap）/ `_M`（Mask、リニア）/ `_E` / `_UI` / 接頭辞 `T_`（sRGB）+ Substance 名。規則は Profile アセットの `Rules`（編集可）| 一致。補足 **R-11**（下） |
| URP の Renderer・Renderer Feature・トーンマップを持たない / 縛らない | `ScriptableRendererFeature` / `ScriptableRendererData` / `ScriptableRenderPass` への参照は `Packages/com.ddrive.core` 全体で 0 件（grep）。`ShaderPipelineAnalyzer` / `MaterialDataValidator` はアクティブなパイプラインとシェーダーの適合を見るだけ | 一致 |
| モデルのメッシュ設定は触らない（頂点カラー・UV・法線 / 接線の規則なし） | D-Drive の Editor が書く `ModelImporter` 設定は **Cutscene のキャラ FBX（`CutsceneFbxPostprocessor.OnPreprocessModel`、`Editor/Cutscene/CutsceneFbxPostprocessor.cs:25`）の `animationType` / `avatarSetup` だけ**。`MayaModelPostprocessor.OnPostprocessModel`（`Editor/Material/MayaModelPostprocessor.cs:17`）は MaterialData の生成を `delayCall` に積むだけで `ModelImporter` に触れない。`ModelImportHandler`（`Editor/Import/ImportRuleHandlers.cs:83`）は FBX のルート GameObject を `ModelData.Prefab` にそのまま入れる | 一致（FC-20 の確認項目 1〜5 と同じ根拠。§4.21） |
| バリアント・キャラクター設定は無い（7-7 `AssetVariantSet` は品質別） | 7-7 は未実装（[13] A-6）。`ModelData` に該当欄なし | 一致 |
| `MaterialData.RenderingLayerMask` は欄だけで実行時には使われない（効くのは `ModelData.LightLayerMask`） | `RenderingLayerMask` を読むのは `MaterialConverter`（別 MaterialData へのコピー）だけ（grep）。`ModelsManager.SpawnData` が書くのは `ModelData.LightLayerMask`（`renderer.renderingLayerMask`） | 一致（FC-19 の根拠） |
| 罠 1: 「元ファイル再読み込み」が知らないシェーダーを `DDrive/Lit` の MaterialData に変換して結び直す。空・無効な ID のスロットは触られない | `ModelSlotBinder.Rebuild(ensureMaterials)`（`Editor/Model/ModelSlotBinder.cs:47`）→ `EnsureMaterialData`（FBX 内蔵は `MayaMaterialImporter.ImportModel`、単体 `.mat` は `UnityMaterialMigrator.Migrate`）。**既に有効な ID が入っているスロットは上書きしない**（`BuildSlots`）。`ModelsManager.SpawnData` は `Slots[i].Material.IsValid && slotRenderers[i] != null` のスロットだけ `Apply` する | 一致。ただし **R-9**（下） |
| 罠 2: `T_` で始まるテクスチャは sRGB オン | 上記の規則（接頭辞 `T_` = sRGB。上から最初に一致）。`_ToonMask` 等の接尾辞規則を `T_` より**前**に置けば回避できる（Profile の規則は上から順） | 一致 |
| 罠 3: FBX の自動取り込みで Look を通らず `DDrive/Lit` の MaterialData が作られる | `MayaModelPostprocessor` が `MayaImportProfile.AutoImport`（既定 true）かつ `AppliesTo`（既定 `IncludePathContains = { "Assets/SourceAssets" }`）の FBX で動く。`ModelImportHandler`（`SourceAssets/Model/`）も同様 | 一致。補足 **R-11** |
| 罠 4: 変換表（`ShaderConversionTable`）は `Assets/` と D-Drive のパッケージからしか探さない | `MaterialConvertWindow.ReloadTables`（`Editor/Material/MaterialConvertWindow.cs:295`）が `AssetSearch.FindAssets("t:ShaderConversionTable")`。`AssetSearch.Roots`（`Editor/AssetSearch.cs:26`）は `Assets` と D-Drive 自身のパッケージのパス（`/Tests/` は除外） | 一致 |
| 罠 5: カットシーン用のキャラ FBX（メッシュを含めると T-Drive の取り込み設定が当たらない） | 手順書（`cutscene-maya-export.html:64`）は Deformed Models / Skins / Blend Shapes を OFF と指示。`CutsceneFbxPostprocessor` はメッシュに関する設定をしない。**ただし `SourceAssets/Cutscene/` 配下の FBX も `MayaModelPostprocessor`（`IncludePathContains` が `Assets/SourceAssets`）の対象になり得る**（Cutscene フォルダを除外する処理は `Editor/Material` に無い）= メッシュ + マテリアルを含めると `DDrive/Lit` の MaterialData が作られる可能性（**未確認**: 実機で確認） | 部分一致（推測を補強）。FC-15 / 運用（手順書どおりメッシュを含めない） |
| M-4 の補足: docs が拡張点とする `IImportRuleHandler` が実際には外から足せない | `ImportRuleService.AllHandlers`（`Editor/Import/ImportRuleService.cs:48`）は `private static readonly` の配列（内蔵 9 件）。`ProjectSetupValidator` の警告文（`DD-SETUP-EMBEDDED-MODIFIED`）は拡張点として `IAssetBehaviour` / `ImportRule` を挙げるが、**`AssetDataBase.CreateBehaviour()`（`IAssetBehaviour`）を呼ぶ Manager は 0 件**（grep） | 一致 + **R-10** |

### 3.4 doc17 との相違（R-8〜R-11）と罠の対応表

| # | 相違・補足 | 実コード | 影響・対応 |
|---|---|---|---|
| R-8 | Specific の `_ZTest` 等は「入れられない」のではなく実行時には書かれるが、ツールが衝突として警告・除去する（上表） | `MaterialManager.ApplySpecific` は名前を絞らない / `MaterialDataValidator` + `MaterialSpecificSync.RemoveConflicts` | T-Drive は `_ToonStencil*` 方式なので影響なし。FC-11 のパス無効化・キーワードは別の欄を足すので予約名の扱いと衝突しない |
| **R-9** | **「元ファイル再読み込み」の影響は doc17 より広い: 単体 `.mat` 由来の経路（`UnityMaterialMigrator.Migrate`）は、既存の MaterialData の `Shader` も `DDrive/Lit`（または変換表の行き先）に上書きする** | `Migrate`（`Editor/Material/UnityMaterialMigrator.cs:57-110`）: 未対応シェーダー → `target = Shader.Find("DDrive/Lit")`（警告ログ）→ `if (data.Shader != target) { data.Shader = target; data.Specific = Merge(...) }`（`:101-106`）。FBX 内蔵経路（`MayaMaterialImporter.ImportMaterial`）は既存 Data の `Shader` が null のときだけ設定する（`:193`）= 既存の T-Drive シェーダーは保たれるが `Common` は上書きされる（`:188`） | T-Drive が書き出した MaterialData（シェーダー = `TDrive/Toon` 系）でも、その Prefab の Renderer が `.mat` を持っていて「元ファイル再読み込み」を押すと Lit に戻る経路がある。FC-15 の範囲に `Migrate` の上書き分岐を含める（§4.16） |
| R-10 | `IAssetBehaviour` は未配線、`ImportRule` ハンドラは外から足せないのに、`ProjectSetupValidator` の警告文が拡張点として挙げている（**FC-6 で `IImportRuleHandler` の外部登録が事実になった**。`IAssetBehaviour` は未配線のまま） | `AssetDataBase.CreateBehaviour()`（`Foundation/Data/AssetDataBase.cs:74`）の呼び出し 0 件 / `ImportRuleService.AllHandlers` は private | FC-12 は `IAssetBehaviour` を使わず Prefab 側のインターフェースにする（§4.13）。FC-6 でハンドラの外部登録を足す。文面の整合は FC-6 / FC-12 のときに [42] §4.5 とともに直す |
| R-11 | traps 2・3 の対象パスは文字列の部分一致（`Assets/SourceAssets` 等）で、置き場所を変えた持ち込み先では当たらないことがある | `TextureImportProfile.IncludePathContains` / `MayaImportProfile.IncludePathContains` の既定は `Assets/SourceAssets`（`AppliesTo` は `Contains`）。`DDriveProjectSettings.SourceAssetsRoot`（P-5）を変えた MS2026（`Assets/_Project/DDrive/SourceAssets`）は、この既定の部分一致に**一致しない**（`ImportRuleService.ResolveSourceRoot` は設定を見るが、この 2 つの Profile は見ない）。**未確認**: MS2026 の実際の Profile アセットの値 | MS2026 では Profile の `IncludePathContains` を編集していない限り罠 2・3 は起きない、とも言える（逆に D-Drive のテクスチャ規則・FBX 自動取り込みも効かない）。FC-14 の外部規則の評価でもパス判定は同じ `AppliesTo` を通る。T-Drive へは「持ち込み先の Profile の値を確認する」と返す |

**罠ごとの対応表**（doc17 §4 の罠 1〜5 を、どの FC チケットで解消するか / 運用で避けるか）:

| 罠（doc17 §4） | 解消するチケット | 運用で避ける方法（FC が入るまで） |
|---|---|---|
| 1. 元ファイル再読み込みで Toon が Lit に置換 | **FC-15**（知らないシェーダーを Lit に変換しない + `Migrate` の上書き分岐）。FC-12 は間接的（スポーン通知で後から正しい見た目に直せる） | スロットを T-Drive が書き出した MaterialData で埋める / `ModelData.Slots` を空にして Prefab のマテリアルをそのまま使う（空・無効 ID は触られない）。「元ファイル再読み込み」を押さない |
| 2. `T_` のテクスチャが sRGB オン | **FC-14**（外部規則。`T_` より前に評価。**実装済み** 2026-10-03） | プロジェクトの `TextureImportProfile` に `_ToonMask` → sRGB オフを `T_` の前に足す / テクスチャを `IncludePathContains` の外に置く |
| 3. FBX 自動取り込みで Lit の MaterialData | **FC-15**（Lit に変換しない = 作られても T-Drive のシェーダーのまま）。FC-6（外部ハンドラ・対象外フォルダ）は別軸 | T-Drive のキャラクターは T-Drive の経路で入れる / `MayaImportProfile.AutoImport = false` または対象パスから外す |
| 4. 変換表の置き場所 | **FC-14**（提供口。**実装済み** 2026-10-03） | ブリッジが変換表を `Assets/` に生成 |
| 5. カットシーン用キャラ FBX にメッシュ | 運用（手順書どおり）。**FC-8**（保留）が将来ブレンドシェイプを通す選択肢を足す | アニメーションだけの FBX にする |

### 3.5 f27702e 由来の契約（まとめ役の抽出）の裏取り

T-Drive の `f27702e`（`naming.py` / `space.py` / `conformance_README.md` / `fcpose.schema.json` / doc15 §3.2〜§3.3）が前提にする決まりのうち、D-Drive 側が守る / 確認すべきもの（(a)〜(d)）と、実コードでの現状。**D-Drive への明示的な要望ではなく、まとめ役が抜き出した契約**。

| 契約 | T-Drive 側の決まり（出典） | D-Drive 側の現状（実コード） | 対応 |
|---|---|---|---|
| (a) シェイプ名 | `FC_<asset>_<layer>_R{row}_C{col}`（+ `_Ex`）・`FC_<asset>_Persp_K{n}` は T-Drive が所有、`fcs_` は彫り用で Unity へ出さない。照合は**大文字小文字を区別する完全一致**。規則の変更は T-Drive 側で MAJOR（`naming.py`） | D-Drive は名前を加工・正規化しない: `AnimatorProxy.ApplyBlendShapes`（`Runtime/Anim/AnimatorProxy.cs:142`）は `AnimData.BlendShapes[].ShapeName` を `Mesh.GetBlendShapeIndex`（完全一致・大文字小文字区別）で引く。Editor の一覧（`AnimEditorWindow.cs:755-769`）も生の名前。**`FC_` / `fcs_` を予約扱いにする仕組みは無い**（`AnimDataValidator` は空名のみ警告） | **FC-20**（接頭辞の一覧 + `AnimDataValidator` の Warning + エディタの既定非表示）。FC-2 は `FC_*` も含めて全シェイプを戻す。FC-10 E-16 / E-17 で固定 |
| (b) ボーン名 | 名前は完全一致（`baseBone` 既定 `head`）。`BoneOffset` は親ボーン空間。Runner は頭のボーンの Transform とメッシュの `SkinnedMeshRenderer` を名前で引く | D-Drive はボーンの GameObject を消さず・改名せず・階層を変えない（`Mesh`・`bones`・`rootBone` を書くコードは 0 件、grep）。Spawn は `SetPositionAndRotation` と `SetParent(worldPositionStays: true)` だけで `localScale` を書かない（`Runtime/Model` / `Runtime/Cutscene` に `localScale` 0 件） | FC-20 の確認項目（§4.21）。FC-10 E-17 で固定 |
| (c) 座標系 | Unity は m / Y-up / 左手 / 前 +Z。計算は正準空間（UE 準拠 cm / Z-up / 左手）で、変換は T-Drive の `space` だけが持つ | D-Drive に座標変換は無い（`Camera.main`・`Transform` のワールド値をそのまま扱う）。D-Drive が変換を足さない方針と一致 | **FC-3**: 返す値は Unity のワールド（m / Y-up / 左手）の位置・回転・縦画角（度）で**単位変換しない**（§4.4）。FC-10 E-18 |
| (d) Runner の書き込み | LateUpdate で `FC_*` だけを書く。表情での弱め（`expressionDampen`）は**他のシェイプの重みを読む** | A-8 のとおり `AnimManager.Tick` は Update、Runner（実行順 10000）の LateUpdate が最後。D-Drive の Update が `AnimData` の指定外のシェイプに触らない（`AnimatorProxy.ApplyBlendShapes` は指定名のみ）ので、LateUpdate で外部が書いた重みを次フレームの D-Drive の Update が上書きしない | FC-10 E-8 / E-16（契約として固定） |

## 4. チケット別設計

### 4.0 共通方針

- **互換区分**: 以下は [42](42_distribution.md) §5 に照らしてすべて **追加のみ（MINOR）**。削除・改名・型変更はしない。FC チケットの PR は次を同時に更新する: `Tests/Editor/Compat/Snapshots/*`（`Tools > D-Drive > Compat > スナップショットを更新`）・`CHANGELOG.md` の `[Unreleased]` 互換性節・関係する `docs/` と HTML マニュアル（機能のみを書く。バグ修正の経緯は書かない）
- **禁止 API・定常経路**: `Instantiate` / `Resources.Load` / `AudioSource.Play` 直呼び禁止。Tick / Spawn / Play の経路で LINQ・クロージャ・boxing を避け、`Play` 時の一時領域は再利用する（FC-1・FC-2・FC-4）
- **例外で止めない**: 外部コードを呼ぶ箇所（FC-4 のマーカー、FC-5 のリスナー）は `try/catch` で個別に隔離し、`Debug.LogException` + 継続。解決できない参照は警告 + no-op（FC-1）
- **Validation の重さ**: 新しい検査は Warning で追加（[42] §5.8）。Error への昇格は次の MINOR 以降
- **ネット / ContentHash**: FC-1〜FC-7 で `INetMessage` のフィールド・`CatalogContentHasher` の対象（Id/Type/Address/Net）・`CutscenePlayMsg` は**変えない**（確認済み: `Tests/Editor/Compat/Snapshots/net-messages.txt` の `CutscenePlayMsg` は `CutId / SelfNetId / TargetNetId / Position / StartNetTime / Seed / HandleNetKey` のみ。Bindings の解決は各クライアントが `CutsceneData` から**ローカルに**行う）
- **doc17 / f27702e 由来の追加**: FC-11〜FC-19（doc17 M-1〜M-9）と FC-20（f27702e 由来）も同じ方針。T-Drive のブリッジ（`TDrive.Toon.DDriveBridge` / `Bridges.DDrive`）は `com.ddrive.core` の `versionDefines`（`[1.4.0,)`）で新しい拡張点の有無を判定し、無ければ従来の回避策に落ちる
- **テスト**: EditMode と PlayMode の両方を green にしてから報告（CLAUDE.md §3-3）。実機（ネット 2 台）確認は FC-1〜FC-7 のいずれも不要（ネット挙動を変えないため）

### 4.1 FC-0: 方針の確定（本書の起票）

内容: 7-8 の書き換え・doc16 の受け入れ・D 群の不採用・FC チケットの起票・本書の作成・関連 docs の追記。**コード変更なし。この起票作業で完了**。

### 4.2 FC-1（= C-1）: 同じモデルへのバインド【優先 高】

**現状のコード**

| 場所 | 内容 |
|---|---|
| `Runtime/Cutscene/CutsceneData.cs:15` `CutsceneBindTarget` | `MainCamera=0 / Self=1 / Target=2 / SpawnModel=3 / SceneObjectByName=4 / AnchorPoint=5`（明示値なし。スナップショット `enums.txt` に 6 行） |
| 同 `:47-63` `CutsceneBinding` | `TrackName / Target / Model（AssetId<ModelMarker>）/ SceneObjectName` の `[Serializable]` struct |
| `Runtime/Cutscene/CutsceneManager.cs:331` `ApplyBindings` / `:373` `ResolveBindingObject` | Bindings を配列順に 1 件ずつ解決。`SpawnModel` は binding ごとに `_models.Spawn` して `instance.SpawnedModels` に積む（`:392-409`）。戻りは Animator があれば Animator、無ければ Transform。未解決は `WarnUnresolvedBinding`（データ + トラック名で 1 回）+ `null`（トラックはミュート継続） |
| `Editor/Cutscene/CutsceneEditModeDirectorSetup.cs:195` `ApplyBindings` / `:241` `ResolveBindingObject` | 上の Edit Mode 簡略版（同じ構造。`_spawnedModels` を覚えて次回 / `TearDown` で返却） |
| `Runtime/Cutscene/CutsceneDataValidator.cs:68-95` ほか | Bindings の検査（TrackName 空 = Error、重複 = Warning、SpawnModel の Model 未設定 = Error 等）。Humanoid 不整合（`:207`）・Cosmetic+Simulated（`:265`）は `Target == SpawnModel` の binding だけを見る |
| `Editor/Cutscene/CutsceneDataEditor.cs:94-` | Inspector の「バインド検査」（トラック名 ⇔ Bindings の食い違い一覧）。Bindings 自体は既定の描画（Target・Model・SceneObjectName が全部出る） |
| `Editor/Cutscene/CutsceneImportService.cs:210` | 再取り込みは既存 Bindings を保持して追記（外部が足した Binding は残る） |

**問題**: カットシーンで出した（`SpawnModel`）キャラに、別のトラック（Facial 等）を結ぶ手段が無い。`SpawnModel` の binding を 2 つ書くとモデルが 2 体出る（`ApplyBindings` が binding ごとに `Spawn` する）。doc16 の回避策は「ブリッジが同名の AnimationTrack のバインド先を引く（名前規則に頼る）」。

**変更案（追加のみ）**

1. **enum**: `CutsceneBindTarget` の**末尾**に `SameAsTrack = 6`（既存値は不変。名前の代案: `ShareWithTrack` / `SameAsOther`。推奨は `SameAsTrack`。意味は「`SourceTrackName` で指す別トラックと同じ相手」）
2. **フィールド**: `CutsceneBinding` の**末尾**（`SceneObjectName` の後）に `public string SourceTrackName;`（`[Tooltip("Target=SameAsTrack のとき使う、同じ相手にバインドする別トラックの TrackName。")]`、既定は空 = 旧データは無影響）。`SerializedLayoutSnapshotTests` に 1 行追加
3. **解決順（Play / Edit Mode 共通のアルゴリズム）**: 2 パス
   - パス 1: `Target != SameAsTrack` の binding を従来どおり解決。解決した `Object`（Animator / Transform）を「TrackName → 解決結果」の一時表に記録（`List<Object>` の再利用バッファ。`CutsceneManager` のフィールドに持ち `Clear()` して使う。Play 経路なので新規 `Dictionary` / LINQ を作らない）
   - パス 2: `Target == SameAsTrack` の binding を解決。`SourceTrackName` を `Bindings` から TrackName 一致で探し（最初の一致）、その解決結果を `SetGenericBinding(track, 結果)` する。参照先がさらに `SameAsTrack` なら鎖をたどる（深さは `Bindings.Length` で打ち切り = 循環の検出）。**配列の並び順に依存しない**（参照元が参照先より前でも後でもよい）
4. **フェイルソフト（警告 + no-op）**: `SourceTrackName` が空 / 一致する binding が無い / 自己参照・循環 / 参照先が未解決（`SpawnModel` の `Model` 無効等）の場合は `WarnUnresolvedBinding(data, trackName, 理由)`（既存の 1 回だけ警告）+ `SetGenericBinding(track, null)`（そのトラックだけミュート。他は継続）。例外は出さない。参照先の binding に対応する Timeline トラックが無い場合は、パス 1 が従来どおり `continue` する（= Spawn されない）ので参照は未解決になる（警告の理由に「参照先トラックが Timeline に無い」と書く）
5. **`SpawnModel` を参照したときに 2 体出ない**: `SameAsTrack` は `_models.Spawn` を**呼ばない**。`instance.SpawnedModels` への追加も参照先の分（1 回）だけ。PlayMode テストで「`SpawnModel`（Hero）+ `SameAsTrack`（Hero_Ext → Hero）の 2 binding」→ `ModelsManager` の有効 Handle が 1、`Director.GetGenericBinding(両トラック)` が同じ Animator であることを検査
6. **バインド型への適合（任意・未決 U-1）**: 解決結果は既存の末尾規則（`target.GetComponent<Animator>()` があれば Animator、無ければ Transform。`CutsceneManager.cs` の `ResolveBindingObject` 末尾）と同じにする。トラックの `TrackBindingType` が Animator / Transform 以外（`GameObject` や独自コンポーネント）のときの変換は初版では行わない
7. **Edit Mode**: `CutsceneEditModeDirectorSetup.ApplyBindings` に同じ 2 パスを足す（Spawn は `managers.Models` 1 回、`_spawnedModels` も 1 回。`TearDown` の返却は不変）。Runtime と Editor の 2 実装になるが、共通化のために `public` の補助を足さず**同じ短いロジックを 2 箇所に書く**（既存の `ResolveBindingObject` も 2 重実装のため。`public` を増やして互換面を広げない）。両方をテストで固定する
8. **Validator**（`CutsceneDataValidator`、Warning で追加。§5.8）: (a) `SameAsTrack` で `SourceTrackName` が空、(b) `SourceTrackName` が Bindings に無い、(c) 自己参照・循環、(d) 参照先の binding に対応する Timeline トラックが無い。`Code` を付ける場合は新規コードで追加（既存 Code は不変）。`ValidatorSeverityRegistryTests` のスナップショット更新
9. **Inspector**: `CutsceneDataEditor.DrawBindingInspection` を拡張し、`SameAsTrack` は「→ 参照先」表示と ✓/✗（未解決・循環）を出す。Bindings の `CutsceneBinding` に `PropertyDrawer` を足し、Target に応じて Model（`SpawnModel` のみ）/ SceneObjectName（`SceneObjectByName`・`AnchorPoint` のみ）/ SourceTrackName（`SameAsTrack` のみ。Timeline のトラック名から選ぶ）を出し分ける（Editor のみ。互換面外）
10. **ネット / Late Join**: 影響なし。Bindings の解決は `PlayLocalInternal` → `ApplyBindings`（各クライアントのローカル処理）。Late Join は Host が `CutscenePlayMsg` を再送し受信側が `elapsedSeek` 付きで同じ `ApplyBindings` を通る。`SpawnModel` は Cosmetic のローカルインスタンス（`NetworkObject` ではない）。ContentHash は Id/Type/Address/Net のみ
11. **取り込み（`CutsceneImportService`）は何も生成しない**: `SameAsTrack` の binding を自動で作るのは外部（T-Drive）の役目（FC-5 のリスナーが `CutsceneData.Bindings` に追記する）。既存の再取り込みは既存 Bindings を保持するので追記した binding は残る

**T-Drive 側の使い方**: doc15 §5.5「バインドの補助」（同じ役名のトラックのバインド先を引く）が不要になる。fctrack の取り込み（FC-5 のリスナー）が `Bindings += { TrackName = "<Model>_Facial", Target = SameAsTrack, SourceTrackName = "<Model>" }` を足すだけ。手置きでも Inspector で選べる。doc14 §7.1 の「バインド先: Animator」を満たす（参照先が Animator を返す）。< 1.4.0 の D-Drive では `Bridges.DDrive` が従来の回避策に落ちる（`com.ddrive.core` の `versionDefines` で `[1.4.0,)` のときだけ新経路）

**テスト**: PlayMode（`CutsceneManagerTests` に追加）= 2 パス・順序非依存・鎖（A→B→C）・循環と未解決の警告 + no-op・2 体出ない・外部 `TrackAsset` へのバインド型 / EditMode = `CutsceneEditModeDirectorSetupTests`・`CutsceneDataValidatorTests`（Runtime 側）・スナップショット（enums / serialized-layout / public-api）

**更新する docs / マニュアル**: [26](26_timeline.md) §4.2（バインド解決の表に `SameAsTrack` を追記）・§4.4（Edit Mode）・§6 影響範囲、`docs/09_editor_tools.md`（`CutsceneDataEditor`）。HTML マニュアルは Cutscene を扱うページが無い / 限られる（`cutscene-maya-export.html` のみ。Bindings の記述は未確認）ため、**デザイナー向けの「同じ相手に結ぶ」の記述の置き場所は実装時に決める**（未決 U-5）

**未決**: U-1（バインド型への適合）・U-2（Validator を最初から Error にするか。既存データに現れない新しい Target 値なので実害は無いが、§5.8 は Warning 始まり）・U-5（マニュアルの置き場所）

**実装メモ（2026-10-03、FC-1 実装）**: U-1 = (a)（変換しない）・U-2 = (a)（Warning）・U-5 = (a)（`docs/DesignerManual` に Cutscene の Binding を扱うページが無いため HTML は今回触らない）で実装した。設計（上記 1〜11）のとおりで、変えた点は次の 3 つ。(1) パス 1 の解決結果は `Dictionary` ではなく **Bindings と同じ添字の `List<Object>`**（`CutsceneManager._bindingResolved` / Editor 側は static）に控える（TrackName 重複でも配列順の最初の一致で決まり、割り当てなし）。(2) 参照先の探索（TrackName 一致の最初の Binding）と鎖の打ち切り（深さ `Bindings.Length`）は `ResolveSameAsTrack`（Runtime は instance メソッド、Editor は static。`public` を増やさず 2 箇所に同じロジック）に切り出した。警告文の文字列は失敗時にしか作らない。(3) `serialized-layout.txt` は変更なし — このスナップショットは `CutsceneData` の最上位フィールド（`Bindings : Generic`）までで、`CutsceneBinding` の中身は載らないため（`SourceTrackName` の追加は `public-api-DDrive.Runtime.txt` の struct フィールドと `enums.txt` に出る）。Inspector は `CutsceneBindingDrawer`（IMGUI の PropertyDrawer）。テストは PlayMode `CutsceneSameAsTrackTests`（新規ファイル。10 件）・`CutsceneDataValidatorTests`（SameAsTrack 5 件追加）、EditMode `CutsceneEditModeDirectorSetupTests`（2 件追加）。

### 4.3 FC-2（= C-2）: プール返却時にブレンドシェイプの重みを戻す【優先 高】

**現状のコード**

- `Runtime/Model/ModelInstancePoolable.cs:11`（`internal sealed`）の `OnReturn()` は `OnReturnedToPool` を呼ぶだけ。`ModelsManager.SpawnData`（`:187-192`）が Spawn 時に `AddComponent`（既にあれば再利用）
- `PoolService.ForceReturn`（`Foundation/Pool/PoolService.cs:207-212`）が `OnReturn()` → `SetActive(false)`。**`Discard`（`PoolPolicyKind` が `None` のモデル）は `OnReturn` を呼ばない**（破棄されるので不要）
- BlendShape の書き込み元は `AnimatorProxy.ApplyBlendShapes`（`AnimData.BlendShapes`）と Animator のカーブ。停止・返却時に重みを戻す処理はどこにも無い（`ClearActive` も重みを触らない）→ 前の表情が次の借り手に残る（Facial に限らない既存の穴）

**変更案（追加のみ。挙動の追加 = MINOR）**

1. `ModelInstancePoolable`（`internal` のまま。公開 API は増えない）に **生成時に 1 回だけ**キャッシュを持たせる: ルート配下の `SkinnedMeshRenderer[]` と、各 Renderer の BlendShape 既定重み `float[][]`。`ModelsManager.SpawnData` が `AddComponent` した直後（= Rent 直後・DefaultAnimation 再生前）に `CaptureBlendShapeDefaults()` を 1 回呼ぶ（`Awake` に頼らない。呼び忘れ防止のため冪等にする）
2. `OnReturn()` で、キャッシュした各 Renderer の BlendShape を既定重みへ戻す（`GetBlendShapeWeight` が既定と違うものだけ `SetBlendShapeWeight`）。**ループは `for` + 配列のみ**（LINQ・クロージャ・`foreach` の列挙子 boxing・`GetComponentsInChildren` の都度呼び出しを避け、割り当てゼロ。`sharedMesh` が `null` / BlendShape 数が変わっていたら該当 Renderer だけ飛ばす = 例外にしない）
3. 既定重みは「0 固定」ではなく **Prefab 生成時の値**（Prefab 側で初期表情を持たせている場合に壊さない）。T-Drive の `FC_*` も含めて**全シェイプ**を戻す（Runner の `OnDisable` が戻す分とは冪等に重なる）
4. 強制回収（上限超過）経路も `ForceReturn` を通るので同じ処理が走る（`OnReturnedToPool` の既存順序は変えない: 台帳の掃除の後に重みを戻す）

**FC-12（スポーン / 返却の通知）と同一 PR**: FC-2 と FC-12 は同じ `ModelInstancePoolable` / `CloseInstance` / `SpawnData` を触る。生成時のキャッシュ（SkinnedMeshRenderer と既定重み + `IModelInstanceListener` の配列）を 1 か所にまとめ、返却は「`OnModelReturning` 通知 → 重みリセット」の順（§4.13）。**FC-20 由来**: 復元は **`FC_*`・`fcs_*` を含む全シェイプ**を対象にする（接頭辞で除外しない。Runner の `OnDisable` と二重になっても無害）。

**R-6 の関連（未決 U-3）**: `PoolService.ForceReturn` が最初の `IPoolable` にしか `OnReturn` を呼ばないため、外部コンポーネントが同じルートに `IPoolable` を実装すると `ModelInstancePoolable`（台帳掃除 + 本チケットのリセット）が呼ばれない。推奨は **FC-2 に「`GetComponents<IPoolable>` を共有バッファに受けて全部に `OnReturn` を呼ぶ」を含める**（Foundation の挙動追加。割り当てなし）。入れない場合は T-Drive に「ルートで `IPoolable` を実装しない」を契約として伝え、FC-10 の E-1 で現挙動をテストで固定する

**T-Drive 側の使い方**: doc15 §5.5「プール返却」の「Runner の `OnDisable` で戻すので追加処理は不要。確認のテストだけ持つ」は変わらない（二重で安全）。`AnimData.BlendShapes` が書いた通常シェイプ（Runner の対象外）も戻るので、Facial 以外の表情アニメが次の利用者に残る問題も解消する。FT-2 の「プール返却の確認テスト」は D-Drive 側（FC-10）が持つ

**テスト**: PlayMode（`ModelsManagerTests` に追加）= 合成 Mesh（`AddBlendShapeFrame` で 2 シェイプ）の Prefab → Spawn → 重みを書く → Despawn → 再 Spawn で既定値に戻る・強制回収でも戻る・Prefab の初期重みが非 0 でもその値に戻る・`sharedMesh == null` / 非 Skinned の Prefab で例外が出ない。Performance（`Tests/Performance`）に Spawn/Despawn 往復の GC 割り当てが 0 の確認を足せるか検討（任意）

**更新する docs**: [05](05_model_animation.md)（A-3 Manager API・返却の説明）、`docs/ProgrammerManual/model-anim-api.html`（返却時の挙動を機能として 1 行）、[02](02_core_framework.md) §6（Pool / `IPoolable`。U-3 を入れる場合）

**実装メモ（2026-10-03、FC-2 / FC-12 実装）**: U-3 = (a)・U-12 = (a) で実装した。設計（上記 1〜4、§4.13）のとおりで、変えた点・補足は次のとおり。(1) **キャッシュの置き場所**: `ModelInstancePoolable.Capture()`（冪等）が `SkinnedMeshRenderer[]` + 既定重み `float[][]` + `IModelInstanceListener[]` をまとめて 1 回だけ作る。`SpawnData` が `AddComponent`（既にあれば再利用）した直後・スロット適用と DefaultAnimation の前に呼ぶ。プール再利用では 2 回目以降は何もしない（返却のたびに既定へ戻すので、最初に控えた値が常に Prefab 生成時の値）。(2) `ModelInstance`（台帳の内部クラス）に `Poolable` を持たせ、`CloseInstance` から通知する。(3) **復元の対象外**: `Pool.Kind == None`（Discard）は `OnReturn` が呼ばれず GameObject ごと破棄されるので復元しない（通知 `OnModelReturning` は呼ぶ）。(4) **U-3 の実装**（`PoolService`、Foundation）: `ForceReturn` が `GetComponents<IPoolable>(共有 List)` で全 `IPoolable` に `OnReturn` を呼ぶ。バッファは static の共有で、`OnReturn` の中から別オブジェクトの返却が走る再入時だけ一時リストに切り替える。**設計から足した点**: 1 個の `OnReturn` が例外を投げても `Debug.LogException` + 継続（残りの `OnReturn` と `SetActive(false)` を止めない。以前は例外が伝播して後続が止まった）。Rent 側に対になる `OnRent` 等の通知は元から無く、足していない。(5) **割り当て**: 返却時の復元・通知は割り当てなし。ただし `SpawnData` 自体が従来から `ModelInstance`（class）と台帳のクロージャを 1 つずつ作るため、Spawn / Despawn 往復の厳密な 0 alloc は成立しない（VfxAllocTests と同じ既知の扱い）。そのため Performance は `ModelReturnAllocTests` で記録のみとした（`ModelInstancePoolable` が `internal` で `InternalsVisibleTo` が無く、Performance テストから直接測れない）。(6) 公開 API は `IModelInstanceListener` と `ModelInstanceContext`（`Handle` / `Data` / `Root` + コンストラクタ。§4.13 の署名に、テスト・外部から作れるよう公開コンストラクタを足した）のみ。名前空間は `ModelMarker` / `ModelData` と同じ `DDrive.Runtime.Model`（食い違いなし）。テストは PlayMode `ModelsManagerReturnNotifyTests`（11 件）・`PoolServiceTests`（2 件）。

### 4.4 FC-3（= C-3）: 「今の視点カメラ」を返す公開 API【優先 中】

**現状のコード**: R-2 のとおり。D-Drive に視点を返す API は無く、`Camera.main` 直参照が `Runtime/Camera/CameraFxManager.cs:271`・`Runtime/Anim2D/Anim2DFacing.cs:71`・`Runtime/Cutscene/CutsceneManager.cs:380,1348` など。カットシーンは `Camera.main` 自体を `DDriveCutsceneCameraApplier`（LateUpdate、実行順 1000）が上書きする。Edit Mode のスクラブも `CutsceneEditModeCameraWriter`（`Editor/Cutscene/CutsceneEditModeCameraWriter.cs:35`）が `Camera.main` に直接書く。

**目的（T-Drive の使い道）**: 補正は「どこから見ているか」で決まる（doc14 §6.2 の「視点の解決: 手動の角度 > 指定した Transform > メインカメラ」の 3 番目を差し替える）。分割画面・視点が複数ある（対戦で自キャラ視点 / 観戦）ときと、パース補正（R-34）が画角を要する。

**API の形（推奨案を 1 つ）**

```csharp
namespace DDrive.Runtime.Viewing   // 新規。DDrive.Runtime.Camera は UnityEngine.Camera と名前が衝突するので避ける
{
    public enum ViewSource { None = 0, MainCamera = 1, Cutscene = 2, Override = 3 }   // 末尾追加のみ

    public readonly struct ViewPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float VerticalFovDegrees;   // 画角（パース補正用）。取れなければ Camera.fieldOfView
        public readonly ViewSource Source;
        public readonly Camera Camera;               // 実カメラ（なければ null）
    }

    public interface IViewProvider                    // 分割画面・独自カメラ制御が実装する
    {
        // subject = 補正をかける対象（顔のオーナー等。null 可）。このプロバイダが担当しなければ false
        bool TryGetView(Transform subject, out ViewPose pose);
    }

    public static class ViewCamera                    // 静的ファサード（Bind 不要。Manager を new しない）
    {
        public static bool TryGetCurrent(Transform subject, out ViewPose pose);
        public static void Register(IViewProvider provider, int priority = 0);   // 優先度の高い順に問い合わせ。Unregister で解除
        public static void Unregister(IViewProvider provider);
    }
}
```

- **解決順**: (1) 登録された `IViewProvider`（優先度の降順。`subject` を渡して担当を決める。分割画面は subject → そのプレイヤーのカメラ）→ (2) **カットシーンがカメラを所有中なら `Camera.main` の現在姿勢**（`Source = Cutscene`。実行順 1000 の `Applier` より**後**に呼ぶ契約。R-2）→ (3) `Camera.main`（`Source = MainCamera`）→ (4) 無ければ `false`（`Source = None`）
- **定常経路**: `TryGetCurrent` は割り当てなし（登録リストは `List<>` を `for` で走査・struct を返す）。1 フレームに複数回呼ばれてよい（結果のキャッシュはしない = 呼び出し時の `Camera` の姿勢）
- **実行順の契約**: 「LateUpdate で、実行順が `DDriveCutsceneCameraApplier.ExecutionOrder`（1000）より後のコンポーネントから呼ぶ」。それ以前に呼ぶと 1 フレーム前のカット姿勢（Update 時点のカットの中間値は返さない）。docs/26 §4.6.5 の「実行順の契約」に 1 項目として追記する
- **カットシーン所有の判定**: `CutsceneManager` の `_cameraOwner`（private）を `DDriveCutsceneCameraApplier` 経由で読めるよう、`DDriveCutsceneCameraApplier` に `public bool IsDriving`（読み取り専用）を足す案（最小）。未確認: `Applier` を使わない Edit Mode は `Source = MainCamera`（`CutsceneEditModeCameraWriter` が `Camera.main` に書くので結果は同じ）
- **ブレンド中**: `Applier` が `Camera.main` をゲームカメラとブレンドして書くので、ブレンド途中の姿勢がそのまま返る（T-Drive の補正がブレンドに追従する）。`ViewSource.Cutscene` でもブレンド割合は返さない（初版）

**返す値の単位・座標系（f27702e 由来・まとめ役の抽出）**: `ViewPose` は **Unity のワールド座標（m / Y-up / 左手 / 前 +Z）の位置・回転と、縦画角（度、`Camera.fieldOfView` 相当）**をそのまま返し、**単位変換・座標変換はしない**（T-Drive の `space` が正準空間（UE 準拠 cm / Z-up / 左手）へ変換する。D-Drive は座標系を知らない）。物理カメラ（焦点距離）の値は初版では返さない（Cutscene の Camera クリップが書くのは `fieldOfView` と DoF の Volume。画角が欲しい T-Drive の R-34 には縦画角で足りる想定）。**カメラが無いとき**は `false` を返し**警告も出さない**（呼び出しが毎フレームでも静か。T-Drive は視点を解決できなければ補正をスキップする = フェイルソフト）。**複数あるとき**: 登録プロバイダが担当する `subject` ならそれ、無ければ `Camera.main`（`Camera.main` は Unity の「MainCamera タグの最初の有効なカメラ」で、複数でも 1 つに決まる）。分割画面などで `Camera.main` が複数の視点の 1 つでしかない場合は `IViewProvider` で解決する。

**代案**: (A) `Camera.main` を返すだけの `ViewCamera.Main`（最小。分割画面に対応できない）。(B) `PlayContext` に視点を持たせる（呼び出しごとに渡す形。Cutscene 以外は呼び出し側の負担）。(C) T-Drive 側で `Camera.main` + `IViewProvider` 相当を自前で持つ（D-Drive 変更なし。D-Drive の他機能が視点を共有できない）。推奨は上記（拡張点は `Register` だけに絞った最小の汎用形）

**T-Drive 側の使い方**: T-Drive 側の Runner は視点解決の最後のフォールバックをフック（例 `FacialViewResolver.Provider`）にしておき、`Bridges.DDrive` が `ViewCamera.TryGetCurrent` を設定する（`TDrive.Facial.Runtime` は D-Drive を参照しない）。doc15 §5.3 の「視点の指定（Transform）」は従来どおり最優先。Runner は実行順 10000 なので R-2 の契約を満たす

**互換区分**: MINOR（新しい名前空間・型の追加）。`public-api-DDrive.Runtime.txt` のスナップショット更新

**テスト**: PlayMode = カメラ未設定で `false`・`Camera.main` のみで `MainCamera`・`IViewProvider` の優先度と `subject` 振り分け・`Unregister`・割り当てなし / カットシーン所有中は `Source = Cutscene` で姿勢が `Camera.main` と一致（`DDriveCutsceneCameraApplier` の LateUpdate 後）

**更新する docs**: [26](26_timeline.md) §4.6.5、[01](01_architecture.md)（静的ファサード一覧があれば）、ProgrammerManual（`bootstrap.html` か新規。置き場所は実装時に決める。未決 U-5）

**未決**: U-4（名前空間 `DDrive.Runtime.Viewing` / 型名 `ViewCamera` の最終決定。互換面に入るので吟味）

**実装メモ（2026-10-03、FC-3）**

1. 設計どおり `Runtime/Viewing/` に `ViewSource` / `ViewPose` / `IViewProvider` / `ViewCamera` を追加（名前空間 `DDrive.Runtime.Viewing`。U-4 = 提案の名前で確定）。§4.4 の署名のとおりで、**§4.4 に無い公開メンバーは足していない**。例外は `ViewPose` の public コンストラクタ（`ViewPose(Vector3 position, Quaternion rotation, float verticalFovDegrees, ViewSource source, Camera camera)`。外部の `IViewProvider` が値を作るために必要）と、下の 2 の `IsDriving`。
2. **カットシーン所有の判定**: 設計案の「`DDriveCutsceneCameraApplier` に読み取り専用 `IsDriving` を足す」を採用。実コードで既に使える公開情報は無く（`CutsceneManager._cameraOwner` は private、`CutsceneHandle` などにも無い）、`Applier` は内部状態 `_restoreValid`（再生開始時の画角を控えてから `Restore()` するまで true = Cutscene がこのカメラを駆動中）を持っているので、`public bool IsDriving => _restoreValid;` の 1 行で済んだ（Applier の挙動は変えない）。`ViewCamera` は `Camera.main.TryGetComponent(out DDriveCutsceneCameraApplier)`（割り当てなし）で読む。
3. **解決順・隔離**: 登録プロバイダ（優先度の降順、同優先度は登録順。同じプロバイダの再登録は優先度の更新で登録順は最後）→ `IsDriving` なら `Source = Cutscene` → `Camera.main`（`MainCamera`）→ `false`。プロバイダの例外は `Debug.LogException` + 次へ、破棄済みの `UnityEngine.Object` 実装は飛ばして取り除く、`TryGetView` の中からの `Register` / `Unregister` でも例外にならない。登録は `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` で掃除（他の静的ファサードの慣習。ドメインリロード無効でも残らない）。Manager は無く、`DDriveRuntimeBootstrap` の配線も無し。
4. **正射影カメラ**: `VerticalFovDegrees` は `Camera.fieldOfView` をそのまま返す（正射影でも変換しない・正射影の大きさは返さない）。物理カメラの値も返さない（初版）。
5. 既存の `Camera.main` 直参照（`CameraFxManager`・`Anim2DFacing`・`CutsceneManager` 等）は**置き換えていない**（挙動を変えない）。D-Drive 内部の他機能を `ViewCamera` 経由にするのは別チケット。
6. **実行順の契約**: 「LateUpdate で、実行順が 1000 より後のコンポーネントから呼ぶと、そのフレームのカット姿勢が返る」を PlayMode テストで固定（`ExternalContractViewTests.E18_CutsceneOwnsCamera_*`、実行順 1001 の外部リーダー）。[26] §4.6.5 に 1 項目、`CameraExecutionOrderValidator` の説明に注記を追記（Validator のコード・重さは変更なし）。
7. テスト: PlayMode `ExternalContractViewTests`（E-18。5 件: カメラ無しで `false`・警告なし / `Camera.main` のみで無変換 / 外部プロバイダの `subject` 振り分け・優先度・`Unregister` / 割り当て 0 / カットシーン所有中の `Cutscene` と姿勢一致）、PlayMode `ViewCameraTests`（6 件: 同優先度の登録順・再登録 / 例外隔離 / 破棄済み / 再入 / 正射影 / 実 `CutsceneManager` の駆動中・追従・終了後）、EditMode `ViewCameraEditModeTests`（1 件: Edit Mode でも `Camera.main` を `MainCamera` で返す）。`public-api-DDrive.Runtime.txt` / `enums.txt` を更新（追加のみ、MINOR）。

### 4.5 FC-4（= C-4）: 外部パッケージのマーカーの汎用の受け口【優先 中】

**現状のコード**: `CutsceneManager.CollectMarkers`（`Runtime/Cutscene/CutsceneManager.cs:475`）が Play 時に `track.GetMarkers()` を回し、`switch (marker)` で 4 つの具象型（`CutsceneEventNotification` / `CutsceneSignalNotification` / `CutsceneShakeNotification` / `CutsceneHapticNotification`、`Runtime/Cutscene/Tracks/CutsceneNotificationTracks.cs`）だけを拾う（**`default` なし = 外部のマーカーは黙って無視**）。発火は `AdvanceMarkers`（Tick で跨いだら発火、Seek は無音で飛ばす）。Edit Mode は `CutsceneEditModePreviewProvider`（`Editor/Cutscene/CutsceneEditModePreviewProvider.cs:33-`）が `CutsceneMarkerCursor<T>`（`Runtime/Cutscene/CutsceneMarkerCursor.cs`、`where TMarker : Marker`）で同じ 4 型を別経路で監視する。

**変更案（追加のみ）**

```csharp
namespace DDrive.Runtime.Cutscene
{
    public interface ICutsceneMarker                    // マーカー側（Marker 派生）が実装する
    {
        void Fire(in CutsceneMarkerContext context);   // 時刻を跨いだ瞬間に 1 回
    }

    public readonly struct CutsceneMarkerContext
    {
        public readonly double MarkerTime;              // マーカーの時刻（秒）
        public readonly double Elapsed;                 // 現在の再生位置
        public readonly PlayableDirector Director;
        public readonly CutsceneDirectorContext Context;   // FireEnabled・ManagerRefs
        public readonly bool IsEditPreview;             // Edit Mode のスクラブ / プレビュー再生か
    }
}
```

- `CollectMarkers` に `if (marker is ICutsceneMarker ext)` を足し、`CutsceneInstance` に `ExternalMarkers`（時刻順の `List<(double, ICutsceneMarker)>`）+ カーソルを持つ。`AdvanceMarkers` が既存 4 種と同じ規則で発火（Seek / Skip で跨いだ分は**無音でスキップ**。`IsFireEnabled` が false のときも発火しない）。**呼び出しは `try/catch` で隔離**（外部の例外で他のマーカー・Tick を止めない。`Debug.LogException` + 継続）
- Edit Mode 側も同じ: `CutsceneEditModePreviewProvider` の `Session` に外部マーカー用のカーソルを足す。`CutsceneMarkerCursor<T>` の `where TMarker : Marker` を緩めると公開 API の制約が変わるため、**兄弟の `CutsceneExternalMarkerCursor`（新規 public 型、または `internal` + 同アセンブリ利用）を追加**する（既存型は変えない）。`Edit Mode` では `IsEditPreview = true`
- D-Drive の既存 4 種のマーカーは**そのまま**（`ICutsceneMarker` を実装させ直さない）。`CutsceneHandle.OnMarker`（文字列キー）も不変

**T-Drive 側の使い方**: 現状の設計（doc14 §7.1）は「マーカーを使わずクリップだけ」。将来 Facial のイベント的な切り替え（一点で感情を切り替える等）をマーカーで置きたくなったとき、`FacialMarker : Marker, ICutsceneMarker`（`Bridges.DDrive`。`ICutsceneMarker` は D-Drive の型なのでブリッジ側 asmdef に置く）で受けられる。Facial 以外の外部パッケージにも使える

**互換区分**: MINOR（新しい interface / struct の追加）。`public-api-DDrive.Runtime.txt` 更新

**テスト**: PlayMode = 外部マーカーが時刻を跨いだとき 1 回だけ `Fire`・Seek / Skip で無音・`FireEnabled=false` で発火しない・例外を投げるマーカーがあっても他のマーカーと Tick が継続・既存 4 種の挙動不変 / EditMode = Edit Mode プレビューで `IsEditPreview = true`・スクラブで連打しない・巻き戻しで無音

**更新する docs**: [26](26_timeline.md) §4.3（マーカー）・§4.4（Edit Mode）

**実装メモ（2026-10-03、FC-4）**

1. 設計どおり `Runtime/Cutscene/ICutsceneMarker.cs` に `ICutsceneMarker`（`Fire(in CutsceneMarkerContext)`）と `CutsceneMarkerContext`（readonly struct、public コンストラクタ）を追加。`CutsceneManager` は `CutsceneInstance.ExternalMarkers`（`List<(double, ICutsceneMarker)>`、Play 時に 1 回収集して時刻順ソート、以後再利用）+ カーソルを持ち、`AdvanceMarkers` から `AdvanceExternalMarkers` が既存 4 種と同じ規則（跨いだら 1 回・`fire=false`〔Seek / Skip / 遅延復元〕と `FireEnabled=false` は無音でカーソルだけ進める）で呼ぶ。呼び出しは 1 マーカーごとの `try/catch`（`Debug.LogException` + 継続）。`CollectMarkers` の `switch` は触らず、その後ろに独立した `if (marker is ICutsceneMarker)` を足した（既存 4 種の経路・順序は不変。4 種の型が `ICutsceneMarker` も実装した場合は両方の経路で呼ばれる）。
2. **設計から変えた点（`CutsceneMarkerContext` の欄）**: 設計案の `CutsceneDirectorContext Context` は**渡さない**（型は public だが `FireEnabled` / `ManagerRefs`〔`[NonSerialized]`、Play では null〕は D-Drive が発火可否・Edit Mode の Manager 参照のために持つ内部用の値で、外へ出すと意味が固定される。必要なら `Director.GetComponent<CutsceneDirectorContext>()` で取れる）。代わりに**再生中の `Handle<CutsceneMarker>`** を足した（外部が `Cutscene.*` / `CutsceneManager.*` へ戻れる最小限。Edit Mode は `Invalid`）。欄 = `MarkerTime` / `Elapsed` / `Director` / `Handle` / `IsEditPreview` の 5 つ。一度公開すると改名・削除できないので、これ以上は足さない方針（足すときは MINOR で末尾に追加）。
3. **Edit Mode**: `CutsceneMarkerCursor<T>`（`where T : Marker`）は変えず、**public を増やさない**ため Editor asm 内 `internal` の `ExternalMarkerCursor`（`CutsceneEditModePreviewProvider` の入れ子型）を足した。Runtime の internal は Editor から見えない（`InternalsVisibleTo` なし）ので Runtime に internal 型を置く案は不可、public の兄弟型を Runtime に増やす案は互換面が増えるため採らなかった。`Session` の収集・`SilentAdvanceTo`・再生中の `Advance` に既存 4 種と同列に組み込み、Timeline ウィンドウ再生中（`PlayableDirector.state == Playing`）のときだけ `IsEditPreview = true` で呼ぶ。スクラブ・再生開始の立ち上がり・巻き戻しは無音。
4. **ネット**: 既存 4 種と同じくローカル処理であることを実コードで確認（`AdvanceMarkers` は各クライアントの `Tick` からだけ呼ばれ、`INetBridge` へは何も送らない）。Late Join の遅延復元（`PlayLocalInternal` の `AdvanceMarkers(fire: false)`）で過ぎたマーカーは無音（テスト `E20_LateJoin_*`）。同期が要る処理は外部パッケージの責任（[26] §4.3 に記載）。
5. テスト: PlayMode `ExternalContractMarkerTests`（7 件: 跨いで 1 回 + 文脈 / 1 Tick で複数・時刻順 / Seek・Skip 無音 / `FireEnabled=false` / 例外隔離 / 跨ぐ Tick の GC 割り当て = 跨がない Tick 以下 / Late Join 無音）、EditMode `ExternalContractMarkerEditModeTests`（3 件: 再生中に 1 回 + `IsEditPreview` / スクラブ無音・再生開始で再発火なし / 巻き戻し無音。`OnEditorUpdate`〔private〕をリフレクションで 1 回ずつ進める）。ダミーの外部マーカー `ExternalPackage.Fake.ExternalFireMarker`（`ICutsceneMarker` 実装）を `ExternalContract.Tests.Runtime` に追加（外部アセンブリの public API だけで書けることの確認を兼ねる）。既存の `ExternalProbeMarker`（`ICutsceneMarker` を実装しない）はそのまま E-2 / E-5 で「無視される」ことを固定し続ける。ダミー側は `Reset` という static 名を避けた（`ScriptableObject.Reset` のマジックメソッドと衝突してエラーログが出るため。外部パッケージも同じ落とし穴がある）。契約は [42] §5.14 の E-20。既存の `CutsceneMarkerCursorTests` / Cutscene のマーカー系テストは無変更で green（既存 4 種の発火順・回数が不変）。
6. `public-api-DDrive.Runtime.txt` に `ICutsceneMarker`・`CutsceneMarkerContext` のみ追加（`CutsceneMarkerCursor<T>` の変更なし）。MINOR。

### 4.6 FC-5（= C-5）: カットシーン取り込み完了の公開イベント【優先 中】

**現状のコード**: R-1 のとおり。`CutsceneFbxPostprocessor.OnPostprocessAllAssets` → `delayCall` の `Flush` → `CutsceneImportService.ProcessPaths`（`Editor/Cutscene/CutsceneImportService.cs`）→ ショットごとに `ProcessShot`（`:158`）が `CutsceneData` と `TimelineAsset` を作る / 更新し、`data.Bindings` を書いて `DDriveAssetSave.SaveAllSuppressed()`（`:245`）。役名 → トラックは `BuildOrUpdateAnimationRoleTrack`（`:445`）で `trackName` = 役名（キャラは FBX ファイル名の `__` 以降 = `modelIdentifierRaw`、小物は `PRP_` 接頭辞を除いた名前、カメラは Camera のオブジェクト名）。通知する口は無い。

**変更案（追加のみ）**

- `Editor/Cutscene/`（`DDrive.Editor` アセンブリ）に **インターフェース + 引数の型**を追加。リスナーの発見は `TypeCache.GetTypesDerivedFrom<ICutsceneImportListener>()`（`IDataMigration` と同じ方式。`DDriveMigrationRunner.cs:76` に前例。ドメインリロードに強く、`[InitializeOnLoad]` の登録タイミングに依存しない）。public で引数なしコンストラクタが必須（`CI.DiscoverValidators` と同じ規則）

```csharp
namespace DDrive.Editor.Cutscene
{
    public interface ICutsceneImportListener
    {
        int Order { get; }                                  // 小さい順に呼ぶ（同値は型名順）
        void OnCutsceneShotImported(CutsceneImportResult result);
    }

    public sealed class CutsceneImportResult
    {
        public string ShotName;                              // ファイル名の <Shot> 部分（生の名前）
        public string Category;
        public CutsceneData Data;                            // 更新済み（Bindings も反映済み。リスナーが追記してよい）
        public TimelineAsset Timeline;                       // `.playable` 本体
        public string TimelinePath;
        public bool IsNew;
        public IReadOnlyList<CutsceneImportRole> Roles;      // 役名 → トラック
    }

    public sealed class CutsceneImportRole
    {
        public string RoleName;                              // = TrackName（Bindings の TrackName）
        public string ModelIdentifier;                       // キャラ: __ 以降（重複接尾辞を除いたもの）。カメラ / 小物は空
        public CutsceneImportRoleKind Kind;                  // Camera / Prop / Character（末尾追加のみ）
        public TrackAsset Track;                             // 生成 / 更新した AnimationTrack または CutsceneCameraTrack
        public string SourcePath;                            // 元 FBX のパス
    }
}
```

- 呼び出しは `ProcessShot` の最後、`data.Bindings`・`SourceFbxGuids` を確定して `SetDirty` + `SaveAllSuppressed` した**後**。各リスナーは `try/catch` で隔離（例外で取り込みを止めない。`Debug.LogException` + 継続）。リスナーが `Data` / `Timeline` を書き換えたら `EditorUtility.SetDirty` + 再度 `DDriveAssetSave.SaveAllSuppressed()` を呼ぶ（リスナー呼び出しの後に D-Drive 側が 1 回 `SaveAllSuppressed` するのが親切: 呼び出しの後ろに移す）
- ユースケースの順序: (a) FBX を先に取り込み → `OnCutsceneShotImported` で `SourcePath` の隣の `.fctrack`（`<Shot>__<Model>.fctrack`）を探して `.playable` にトラックを足す / 更新する。(b) `.fctrack` が後から取り込まれた場合は、T-Drive 側が既に使える公開 API `CutsceneImportService.ComputeCutsceneDataPath(gameDataRoot, category, shotIdentifier)`（`:250`）で `CutsceneData` を引いて足す。**(b) は FC-5 が無くても今できる**が、(a) は FC-5 が無いと「D-Drive の `Flush` の後」を保証できない（R-1）。`CutsceneImportService.ScanAll`（手動の再取り込み）も同じ経路を通るのでリスナーが呼ばれる
- **互換面**: `DDrive.Editor` の `public` は [42] §5.4 の互換面に**含まれない**。ただし T-Drive の `Bridges.DDrive.Editor` が `DDrive.Editor` を参照して使う以上、事実上の契約になる。**[42] §5.9「弱い互換面（Editor 契約）」に `ICutsceneImportListener` と `CutsceneImportResult` を追記**する（削除・改名は CHANGELOG 必須。`EditorContractSnapshotTests` の対象に加えるかは実装時に判断。未決 U-6）

**T-Drive 側の使い方**: doc15 §5.5「fctrack の取り込み」の `AssetPostprocessor`（postprocessOrder に依存）を `ICutsceneImportListener` に置き換える。リスナーが `.playable` に `FacialCorrectionTrack`（名前 `<Model>_Facial(auto)` で自動生成分を見分ける）を足し、`Result.Data.Bindings` に FC-1 の `SameAsTrack` binding を追記する（FC-1 と一緒に入ると T-Drive 側の回避策が全部不要になる）

**互換区分**: MINOR（Editor の追加）。CHANGELOG の互換性節に「Editor 契約の追加」と書く

**テスト**: EditMode（`DevRepoOnly` = 既存の `CutsceneImportServiceTests` と同じ UnityChan サンプル FBX を使う）= ショット取り込み後にリスナーが 1 回・正しい `ShotName` / `Roles` / `Timeline` で呼ばれる・再取り込みでもう 1 回（`IsNew=false`）・リスナーが例外を投げても取り込みが完了し他のリスナーが呼ばれる・`Order` 順・リスナーが足したトラックと Binding が再取り込みで消えない（E-4 と合わせる）。サンプル FBX が無い持ち込み先では `DevRepoOnlyGuard` でスキップ

**実装メモ（2026-10-03、FC-5 実装）**: U-6 = (a) で実装した。(1) **型**: `Editor/Cutscene/ICutsceneImportListener.cs`（`DDrive.Editor.Cutscene`）に `ICutsceneImportListener` / `CutsceneImportResult` / `CutsceneImportRole` / `CutsceneImportRoleKind`（`Camera` = 0 / `Prop` = 1 / `Character` = 2。末尾追加のみ）と、発見・通知の静的クラス `CutsceneImportListeners`（`Discover()` / `Notify(result)`。テストから直接呼べるよう public）を置いた。発見は `TypeCache.GetTypesDerivedFrom` + public・引数なしコンストラクタ + abstract / interface を除外、**`DDrive.Tests*` アセンブリの実装は除外**（`CI.DiscoverValidators` / `DDriveMigrationRunner` と同じ理由。このため D-Drive 自身のテストのダミーは `DDrive.Tests*` に置けず、外部アセンブリ相当の `ExternalContract.Tests.Editor` に置いた）。並びは `Order` 昇順・同値は型のフルネーム順（序数比較）、`Order` が例外を投げたら 0 扱い。(2) **呼び出し位置**（設計から変えた点）: 設計の「保存の**後**に呼び、リスナー後に D-Drive が再度保存」ではなく、「`Bindings` / `SourceFbxGuids` / `FrameRate` を確定して `SetDirty` した**後・保存の前**にリスナーを呼び、リスナー後に D-Drive が **1 回だけ** `SaveAllSuppressed`」にした（保存が 1 回で済み、リスナーが保存を呼ぶ必要がない）。リスナーが 1 つでも呼ばれたら `Data` / `Timeline` を改めて `SetDirty` する（リスナーが `SetDirty` を忘れても保存される）。(3) **`Roles`**: `ProcessShot` が役ごとに `BuildOrUpdateAnimationRoleTrack`（キャラ・小物）/ `BuildOrUpdateCameraTrack`（カメラ）の中で追加する。`RoleName` は設計どおり Bindings の `TrackName`（実コードではキャラ = `modelIdentifierRaw`〔`Hero_2` のように重複接尾辞付き〕、小物 = `PRP_` 接頭辞なしのノード名、カメラ = Camera の GameObject 名）、`ModelIdentifier` はキャラのみ重複接尾辞を除いたもの（小物・カメラは空。小物の識別子は D-Drive が ModelData を引くのに使うだけで公開しない）。アニメーションが無くトラックを作れなかった役は含まれない。(4) **再取り込みで重複させない情報**: 新しい項目は足さなかった。`Result.Data.Bindings`（`TrackName`）と `Result.Timeline.GetOutputTracks()`（名前）で既存を判定できる（再取り込みでも `Data.Bindings` は既存を保持したまま渡り、外部が足したトラックも `Timeline` に残っている）。この判定パターンをテストの `AddExternalBindingAndTrackIfMissing` と [26] §5.2 の 7 に示した。(5) **発火経路**: `CutsceneFbxPostprocessor.Flush` → `ProcessPaths`、手動の `ScanAll`（`Generate > SourceAssets/Cutscene からインポートルールを再実行`）、`ProcessPaths` 直呼びのすべてが `ProcessShot` を通るため同じ。(6) **テスト**: EditMode `ExternalContractCutsceneListenerTests`（`E19_*` 8 件 = 7 件（発見と順序 / 既定で何もしない / 例外の隔離（通知単体と取り込み経路）/ 1 ショット 1 回と `Result` の中身（呼び出し時点で Data がアセット・SourceFbxGuids 確定済み）/ リスナーの追加がディスクに保存され再取り込みで消えず重複しない / `ScanAll` でも呼ばれる）+ `DevRepoOnly` 1 件（UnityChan で `Roles` の Character の役を確認）。合成 FBX（`ExternalContractRig.fbx`）を「カメラ+小物」「キャラ」に見立てるとアニメーション無しなのでトラックは作られないが `CutsceneData` / Timeline は作られリスナーは呼ばれるため、`Roles` の中身以外は UnityChan 無しで通る（持ち込み先でも実行できる）。ダミーリスナー（`ExternalPackage.Fake.ExternalListenerA` ほか）は `TypeCache` で常時発見されるが、static の `ExternalListenerProbe.Sink` が設定されているテスト中だけ記録し普段は何もしない（`ThrowEnabled` のときだけ `ExternalThrowingListener` が投げる）。EditMode 1259/1259・PlayMode 854/854 green（EditMode は +8）。(7) **互換**: MINOR（Editor 契約の追加）。`EditorContractSnapshotBuilder` に「== CutsceneImportListener ==」節を足し（型ごとの public フィールド / プロパティ / メソッド / enum 値を名前順で出力）、`editor-contract.txt` を更新（既存の節に差分なし。他のスナップショットも差分なし）。[42] §5.9 / §5.14（E-19）に記載。シリアライズ形式・既存の公開 API（`DDrive.Foundation` / `DDrive.Runtime`）・Validation の変更なし。`TDrive.*` を参照せず、Facial / `.fctrack` はコードの使用例にも書いていない（API は汎用）。

**更新する docs**: [26](26_timeline.md) §5.2（Unity 側の自動処理）・§6、[42](42_distribution.md) §5.9

### 4.7 FC-6（= C-6）: 取り込みルールの外部拡張 / `SourceAssets/Facial/` の扱い【優先 低】

**現状のコード**: `ImportRuleService.AllHandlers`（`Editor/Import/ImportRuleService.cs:48`）は `private static readonly IImportRuleHandler[]`（doc16 は「internal の静的配列」と書くが実際は `private`。`Handlers`（`:63`）が読み取り専用で公開されるだけ）。`IImportRuleHandler`（`Editor/Import/IImportRuleHandler.cs`）は public。`KnownNonTargetTypeFolders`（`:95`）は `private static readonly HashSet<string>`（`Shaders / Data / Samples / Cutscene`）。知らない種別フォルダの下に置くと `RecordHint`（`:179`）→ `FlushHints` が `Debug.LogWarning`（1 パス 1 回）。

**変更案（追加のみ。Facial 専用にしない）**: **「対象外フォルダの宣言」を外部から足せるようにする**。`ImportRuleService` に `TypeCache` で発見する宣言用インターフェース（例 `IImportRuleFolderOptOut { IEnumerable<string> FolderNames { get; } }`、public・引数なしコンストラクタ）を追加し、`KnownNonTargetTypeFolders` の判定（`:201`）が内蔵の 4 つ + 宣言された名前の和集合を見る。T-Drive 側は `Facial` を宣言する。代案: (A) `KnownNonTargetTypeFolders` に `"Facial"` を直書き（D-Drive が Facial を知ることになり原則 §2-2 に反する。却下）、(B) 外部ハンドラの登録（`IImportRuleHandler` を TypeCache で足す）は、T-Drive が D-Drive の `AssetDataBase` を作らない前提では不要で規模が大きい（却下）

**2026-10-03 追記（doc17 M-4 の整理）**: doc17 M-4 の「`ImportRuleService` のハンドラ配列を他パッケージから登録できるように」は**ここ（FC-6）に含める**。`AllHandlers`（`Editor/Import/ImportRuleService.cs:48`）に `TypeCache.GetTypesDerivedFrom<IImportRuleHandler>()` で発見した外部実装（内蔵 9 件以外・public・引数なしコンストラクタ）を後ろから足す（`HandlersByFolder` / `AllowedTypeFolderList` も同じ一覧から作る。フォルダ名が内蔵と重複するものは警告 + 無視）。ただし `ImportRuleService` が作る Data は `AssetCreationService`（`AssetNamingService.GetTypePrefix` / `GetTargetFolder` が `AssetType` ごとの switch）に依存するため、**外部ハンドラが実際に使えるのは既存の `AssetType`（D-Drive の種別）の Data を作る場合に限る**（T-Drive は D-Drive の `AssetDataBase` を作らないので、T-Drive 自身は (a) の対象外フォルダ宣言だけ使う想定）。変換表とテクスチャ規則の外部登録は FC-14（§4.15）。

**実装メモ（2026-10-03、FC-6 + FC-14 実装。1 PR）**: (1) **発見の共通部**: `Editor/Import/ExtensionPointDiscovery.cs`（internal）= `TypeCache.GetTypesDerivedFrom<T>()` + public・非 abstract・引数なしコンストラクタ + `DDrive.Tests*` 除外（FC-5 の `CutsceneImportListeners.Discover` と同じ規則）+ 型のフルネーム（序数）順 + コンストラクタ例外の隔離。FC-6 / FC-14 の 4 つの提供口が共通で使い、**結果はドメインリロードまでキャッシュ**（`OnPostprocessAllAssets` のたびに走査・生成しない）。(2) **ハンドラ**: `AllHandlers` を `BuiltInHandlers`（組み込み 9 件、順序不変）に改名し、`Handlers` / `HandlersByFolder` / 許可フォルダ一覧は「組み込み + 採用された外部」から作る（`EnsureExtensions`）。外部は `ExternalImportRuleHandler`（internal の包み）で受け、名乗り（`TypeFolder` / `Target` / `DataType` / `Extensions` / `IdentifierFallback`）を採用時に 1 度だけ読む（`TypeFolder` が空 = 名乗らない / DataType が `AssetDataBase` 派生の具象型でない / `Extensions` が空 / 区切り文字入りは不採用）。`LoadSource` の例外は隔離して null（= 作成スキップ）、`Configure` の例外は `ImportOne` が捕まえて Data を作らず継続（`Configure` は `CreateAsset` の前に走るので残骸が残らない）。**組み込みのコードパスは変えていない**（例外隔離は外部ハンドラのときだけ）。(3) **競合**（組み込み優先 + 警告 1 回）: 組み込みと同じ `TypeFolder`、`KnownNonTargetTypeFolders`（`Shaders / Data / Samples / Cutscene`。専用パイプラインや別経路が使うので外部ハンドラに取らせない）、先に採用された外部ハンドラと同じ名前（型のフルネーム順で先が勝つ）。拡張子の重複は種別フォルダごとに独立なので競合にしない（拡張子は小文字・ドット付きで書く。違うと警告）。(4) **種別フォルダ宣言**: §4.7 の案どおり `IImportRuleFolderOptOut { IEnumerable<string> FolderNames }`。`RecordHint` が `KnownNonTargetTypeFolders` の次に宣言集合を見て案内を出さない。空 / null / 空白 / `/` `\` を含む名前 / 重複は無視、組み込みのハンドラ名・`KnownNonTargetTypeFolders` の宣言は警告 + 無視（宣言で組み込みの「対象拡張子」案内を消せてしまうため）。外部ハンドラの `TypeFolder` は `HandlersByFolder` に入るので、宣言なしでも「不明な種別フォルダ」の案内は出ない（拡張子違いの案内は出る）。**D-Drive 本体に `Facial` の名前は無い**（外部が宣言する）。(5) **`Handlers` の利用側**: `ImportRuleDefaultFolders`（既定フォルダ + README）・`SourceDataCreation`（ソース指定の作成）は `Handlers` を使うので外部ハンドラの種別フォルダも自動で反映される（外部が何も名乗らなければ従来どおり 9 件）。(6) **R-10**: `IImportRuleHandler`（拡張点 `ImportRule`）が外部から使えるようになったので `ProjectSetupValidator` の警告文の「ImportRule」は事実になった。同じ文の `IAssetBehaviour` は未配線のまま（R-10 の別の指摘。FC-12 は Prefab 側のインターフェースにした）— 文面は変えていない（既存 Validator のコード・重さを変えない原則）。(7) テスト用 API: `ImportRuleService.ResetExtensionCacheForTests()`（キャッシュ破棄。ダミー拡張が static フラグで名乗りを切り替えるため）・`ExternalOptOutFolders`。

**T-Drive 側の使い方**: 回避策は「Facial のデータは `SourceAssets/Cutscene/` か GameData 外に置く」。FC-6 が入れば `SourceAssets/Facial/` に置いても案内ログが出ない。**警告が出るだけで動作には影響しない**ので優先は低い

**互換区分**: MINOR（Editor の追加。弱い互換面）。**テスト**: EditMode = 宣言したフォルダに置いたファイルで案内ログ（`Report.Lines` / `LogAssert`）が出ない・宣言しないフォルダは従来どおり出る・宣言の重複 / 空文字を無視

**更新する docs**: [09](09_editor_tools.md)（ImportRule の節）、[10](10_workflow.md) §3.3（フォルダ規約）、[42](42_distribution.md) §5.9

### 4.8 FC-7（= C-7）: 依存関係の追跡が Timeline クリップ内の参照まで届くかの調査【優先 低】

**調査結果（コードを読んだ範囲）**: **届かない**（R-5）。`DependencyGraphService` は `.asset`（`AssetDataBase`）・`.prefab`・`.unity` だけを歩く。`.playable` のクリップが持つ `AssetId`（D-Drive 自身の `CutsceneSeClip.SeId` / `CutsceneVfxClip.VfxId` / `CutsceneAnchorGroupClip.GroupId` / `CutsceneUiClip.CanvasId` / `CutscenePresentationClip.PresentationId` / `CutsceneShakeNotification.ShakeId` 等）は「使用箇所」「未使用アセット」「安全な削除」に**出ない**。つまり Cutscene が参照している SE を「未使用」と判定して削除を許してしまう既存の穴がある（`CutsceneDataValidator.ValidateReferencedAssets` は Cosmetic+Simulated の検査のために `.playable` を歩くが、依存グラフとは別経路）。

**変更案（調査 → 対応）**

1. **実機確認（Unity が要る）**: SE を参照するクリップを持つ `.playable` を作り、`UsagesWindow` / `UnusedAssetsWindow` / 安全な削除に出るかを確認（コードの読みでは出ない。「未確認」を解消する）
2. **対応（確認で穴が実在した場合）**: `DependencyGraphCollector` に `.playable`（`TimelineAsset`）の走査を足す。`TimelineAsset.GetOutputTracks()` → `track.GetClips()` → `clip.asset`（`PlayableAsset`）を `SerializedObject` で歩き、`AssetId<T>` / `AssetRef` プロパティを `CollectFromPrefab` と同じ `WalkProperties` で拾う（**`.playable` を `DependencyGraphService` の対象拡張子に追加**し、`CutsceneData → Timeline` の親子はグラフ上は `CutsceneData` から `.playable` の被参照として扱う）。マーカー（`track.GetMarkers()`）の `AssetId` も同様。これは **D-Drive 自身の既存の穴の修正**であり、外部クリップの `AssetId` 参照も拾える副次効果がある
3. **Facial データは出ない**: T-Drive の `FacialCorrectionData` は `AssetId` ではなく `ScriptableObject` の直接参照なので、`.playable` を歩いても依存グラフには出ない。Addressables の依存としては `.playable` に付いて運ばれる（再生は問題ない）。出したいなら T-Drive が `AssetRef` 型で参照するか、D-Drive が `UnityEngine.Object` の直接参照も辿る（規模が大きいので対象外）

**互換区分**: 挙動の追加（MINOR）。依存グラフのキャッシュ（`DependencyGraphCache`）の版（形式が変わらなければ不要。変わるなら要確認）。**テスト**: EditMode = `.playable` に `CutsceneSeClip`（`SeId`）を持つ TimelineAsset で `DependencyGraphService` の `GetUsages` に `CutsceneData` ではなく `.playable` / `CutsceneData` が出る・安全な削除が参照ありと判定する

**更新する docs**: [09](09_editor_tools.md)（依存関係）、[26](26_timeline.md) §6 影響範囲

### 4.9 FC-8（= C-8）: カットシーンのキャラ FBX で BlendShape のカーブを通す選択肢【優先 低・保留】

**保留**: **表情アニメの運用が決まるまで着手しない**（T-Drive 側で感情の重みは Facial のクリップ / `.fctrack` で渡す設計のため、ショット FBX からブレンドシェイプのカーブを通す必要が今は無い）。

**現状（読んだ範囲）**: デザイナー向け手順書 `docs/DesignerManual/cutscene-maya-export.html`（`:64`）はキャラ FBX の Blend Shapes を **OFF** と指示。`CutsceneFbxPostprocessor.OnPreprocessModel`（`Editor/Cutscene/CutsceneFbxPostprocessor.cs:30`）は `animationType = Human` と Avatar の設定だけで `importBlendShapes` には触れない。取り込みは `AnimSourceLoader.Load(path)` で FBX の埋め込みクリップを 1 本取り、`AnimationTrack` の `AnimationPlayableAsset.clip` に設定するだけ。**未確認**: FBX 側に BlendShape カーブがあれば `AnimationClip` に含まれ、Humanoid クリップでも Generic パスのカーブとして残るか（残れば Spawn したモデルの SkinnedMeshRenderer のパスと一致する場合に再生される）。`CutsceneFrameRangeTrimmer` がブレンドシェイプのカーブをトリムするかも未確認。

**変更案（着手時）**: `CutsceneImportProfile` に「ブレンドシェイプのカーブを残す」の選択肢（既定は従来どおり OFF）を足し、`OnPreprocessModel` で `importBlendShapes` を連動、手順書の「Blend Shapes OFF」を「必要なときだけ ON」に更新（デザイナーマニュアルは機能のみ）。互換区分 MINOR + 文書。

### 4.10 FC-9（= C-9）: デバッグ / 調整（7-3 / 7-4）との接続【優先 低・保留】

**保留**: **7-3（デバッグオーバーレイ）/ 7-4（Live Tuning）着手時**に設計する。外部コンポーネント（T-Drive の Runner）が自分の値（強さ・追従・感情）を公開し、D-Drive のデバッグ / Live Tuning から触れる口が要る。具体的な形は 7-3 / 7-4 の設計（[13](13_extensions.md) A-2 / A-3）に合わせて決める。[11](11_tasks.md) の 7-3 / 7-4 の各行に「FC-9 を考慮」と追記済み。

### 4.11 FC-10（新規）: 外部拡張の契約を固定する

**目的**: §3 の A-1〜A-9 は T-Drive が「D-Drive を変更しなくても動く」前提に**既に依存している**挙動。D-Drive 側が今後無自覚に壊さないよう、**契約としてテストで固定**する（追加のみの互換性ポリシーを、外部拡張の面にも広げる）。

**契約テスト（E-1〜E-18）**: ダミーの外部 Track / Clip / Mixer / Marker / `IValidator` / `ScriptedImporter` / コンポーネントをテスト用アセンブリ内に置いて確認する。

| # | 契約 | テストの種類・要点 |
|---|---|---|
| E-1 | Prefab に付けた外部 `MonoBehaviour` が Pool の往復で生きる（A-1） | PlayMode。外部コンポーネント付き Prefab を `ModelsManager.Spawn → Despawn → Spawn`。`OnEnable` / `OnDisable` の回数・同一インスタンスの再利用。**R-6**: ルートに `IPoolable` を持つ外部コンポーネントがあると `ModelInstancePoolable` の `OnReturn` が呼ばれない現挙動を、U-3 の決定に合わせて固定（直すなら「全部呼ばれる」、直さないなら「最初の 1 個のみ」と文書化したテスト） |
| E-2 | 外部 Track / Clip / Mixer が `CutsceneManager` の再生で `ProcessFrame` される（A-2） | PlayMode。テスト内で `TimelineAsset` を組み、ダミー `TrackAsset`（`[TrackBindingType(typeof(Animator))]`）+ `PlayableBehaviour` を再生。`ProcessFrame` が毎 Tick 呼ばれ `playerData` がバインド先の Animator であること。**Evaluate は Update、後の LateUpdate で読める**こと |
| E-3 | シーク / スキップ / 速度 / ネット受信開始位置に追従し、**インスタンス一時停止中は Evaluate されない**（A-3、R-3） | PlayMode（`CutsceneManagerTests` の既存の Fake ブリッジ雛形を流用）。`Seek` で即 `Evaluate`・`Skip` で末尾の `Evaluate`・`SetSpeed`・`Paused` 中は `ProcessFrame` が来ない（契約として固定）・`elapsedSeek` 付きの受信再生で初回 `ProcessFrame` の時刻が一致 |
| E-4 | 再取り込みで外部トラックと外部 Binding が消えない（A-4） | EditMode（`DevRepoOnly`）。`CutsceneImportServiceTests.ProcessPaths_Reimport_PreservesManuallyAddedTrackAndCameraSettings` を拡張し、外部型の `TrackAsset`（名前 `Hero_Dummy`）と外部が足した Binding を残すこと |
| E-5 | 知らないトラック・クリップ・マーカーで `CutsceneDataValidator` が警告しない（A-5） | PlayMode（`CutsceneDataValidatorTests` 同様）。外部型の Track / Clip / Marker を持つ Timeline の `Validate` 結果が 0 件 |
| E-6 | 外部アセンブリの `IValidator` が見つかり実行される（A-6） | EditMode。**名前が `DDrive.Tests` で始まらない**ダミー asmdef（例 `ExternalContract.Dummy`。`defineConstraints: UNITY_INCLUDE_TESTS`）に `AssetType` が付かないダミー Data 専用の `IValidator` を置き、`CI.DiscoverValidators()` が返すこと・実データには結果を出さないこと。`DDrive.Tests*` 内のダミーは**発見されない**現仕様も固定（`CI.cs:143`）。代案: `System.Reflection.Emit` の動的アセンブリ（asmdef を増やさない） |
| E-7 | 未知の拡張子 / 未知フォルダが D-Drive の取り込みで例外を出さない（A-7） | EditMode。`ImportRuleService.ProcessPaths` に `SourceAssets/Cutscene/X/a.ddrivecontract`（案内ログなし）と `SourceAssets/Facial/x`（宣言の無いフォルダは案内ログ 1 回・例外なし。外部が `IImportRuleFolderOptOut` で宣言したフォルダは案内なし = E-21、FC-6）を渡す。外部 `ScriptedImporter` の取り込み自体はダミーで 1 件確認 |
| E-8 | `AnimData.BlendShapes`（Update）と外部 LateUpdate の BlendShape 書き込みが衝突しない（A-8） | PlayMode。合成 `SkinnedMeshRenderer`（`AddBlendShapeFrame` で 2 シェイプ）+ 外部コンポーネント（`DefaultExecutionOrder(10000)`、LateUpdate で `FC_test` を書く）。`AnimManager` が `smile` を書き、外部が `FC_test` を書く。互いを上書きしない・同名なら LateUpdate 側が最終 |
| E-9 | 外部 `IAssetManager` を `GameLoop.Register` して Tick が届く（A-9） | PlayMode。`DDriveRuntimeBootstrap.Loop.GameLoop.Register(ダミー)` → `Tick` / `OnPause` / `StopAll` / `OnSceneUnload` が来る |
| E-10 | `_Toon*` など未知の接頭辞の Specific がそのまま実行時 Material に書かれる（doc17 §1 / §3 #1） | PlayMode（`MaterialManagerTests`）。`_ToonTest`（Float）・`_ToonColor`・`_ToonVec`・`_ToonTex` を持つテスト用シェーダー（`Tests/Runtime/ExternalContract/`）+ `MaterialData.Specific` → `GetData(data)` の Material にそのまま入る（Bool は 0/1） |
| E-11 | シェーダーに無いプロパティの Specific は飛ばされ、例外なし | PlayMode。存在しない `_ToonNone` を含む Specific で他の値は書かれ継続 |
| E-12 | `_Toon*` が予約名と衝突しない | EditMode。`MaterialCommonNaming.IsSpecific("_ToonTest", flags)` = true・`MaterialDataValidator` が `_Toon*` に「予約名 / 共通チャンネル名」警告を出さない（シェーダーに無いときの既存 Warning は従来どおり） |
| E-13 | `ModelData.Slots` の空・無効 ID のスロットは Prefab の `sharedMaterials` を触らない（doc17 §4 罠 1 の回避策の前提） | PlayMode（`ModelsManagerTests`）。`Slots` を空 / `Material` を無効にした Prefab を Spawn → `Renderer.sharedMaterials` が Prefab のまま。有効 ID のスロットだけ差し替わる |
| E-14 | D-Drive が Renderer Feature に関与しない（doc17 §3 #4） | EditMode（静的）。`Packages/com.ddrive.core` の Runtime / Editor ソースに `ScriptableRendererFeature` / `ScriptableRendererData` / `ScriptableRenderPass` の参照が無い（`ForbiddenApiScanner` と同様のソース走査。Tests 自身は除外） |
| E-15 | `MaterialSpecificResolver.Merge` が `_Toon*` を既定値付きで Specific に登録する（Toon のシェーダーを割り当てたときの挙動） | EditMode。`_Toon*` を持つテスト用シェーダーで `Merge(null, shader)` が登録する・既存値を保持する |
| E-16 | **`FC_` / `fcs_` のシェイプが取り込み〜スポーン〜`AnimManager.Tick`〜プール往復で名前が変わらず、D-Drive が重みを書かない**。LateUpdate で外部が書いた重みが次フレームの D-Drive の Update で上書きされない（f27702e 由来・まとめ役の抽出。FC-20） | PlayMode。合成 `SkinnedMeshRenderer`（`AddBlendShapeFrame` で `FC_test_Neutral_R0_C0` / `fcs_test_R0_C0` / `smile`）の Prefab → Spawn → `GetBlendShapeName` が不変 → `AnimData.BlendShapes` に `smile` だけ指定した再生中、外部コンポーネント（実行順 10000・LateUpdate）が `FC_test_Neutral_R0_C0 = 100` を書く → 次フレームの Update 後も `FC_*` は 100 のまま（`smile` だけ D-Drive が書く）→ Despawn → 再 Spawn で全て既定（FC-2。`FC_*` を含む）。`AnimData.BlendShapes` に `FC_*` を指定した `AnimDataValidator` の Warning（FC-20） |
| E-17 | **取り込みがボーン・名前・スケールを変えない**（f27702e 由来。FC-20 の確認項目 1〜5） | EditMode。(静的) D-Drive の Editor ソースに `importBlendShapes` / `optimizeGameObjects` / `meshCompression` / `globalScale` / `useFileScale` / `extraExposedTransformPaths` の書き込みが無い（`CutsceneFbxPostprocessor` の `animationType` / `avatarSetup` を除く）。(取り込み) ボーン名とシェイプ名を持つ小さな FBX フィクスチャを `ImportRuleService` 経由で取り込み、`ModelData.Prefab` の `SkinnedMeshRenderer.bones` 名・`sharedMesh.GetBlendShapeName` が元ファイルと一致（フィクスチャの用意は要判断 U-15 = `DevRepoOnly` か合成 FBX） |
| E-18 | **`ViewCamera` が Unity のワールド値を無変換で返す**（FC-3。f27702e 由来） | PlayMode。FC-3 の AC に含む（`ViewPose` の位置・回転が `Camera.main` の `transform` と一致・縦画角が `fieldOfView` と一致・カメラ無しで `false` かつ警告なし）。外部 `IUniversalValidator`（FC-18 の代用方法）が `Run All` に出ることも E-6 と合わせて固定。**→ FC-3 で実装（2026-10-03）**: `ExternalContractViewTests` |


**互換面の扱い（docs/42 §5）**: 「外部拡張の契約」を [42] §5 の互換面に**追加するか**を決める。**推奨: 追加する**。内容は §5.4「挙動の互換」の拡張として、(a) Timeline の外部 Track / Clip / Marker が評価・検証・再取り込みで尊重される（E-2〜E-5）、(b) 外部アセンブリの `IValidator` が発見される（E-6）、(c) Prefab 上の外部コンポーネントが Pool の往復で生きる（E-1）、(d) `AnimManager` / Cutscene の評価は Update、外部は LateUpdate で上書きできる（E-8）、(e) `IAssetManager` / `GameLoop.Register` が使える（E-9）。破る変更は MAJOR（§5.12）。**FC-10 の PR で [42] に §5.14「外部拡張の契約」を新設**し、`Tests/Editor/Compat`（または `Tests/Runtime/ExternalContract`）のテストを §5.11 の表に 1 行足す。ポリシーの追加はユーザー承認が要るため、本書（FC-0）では [42] を**変更せず**提案に留める（未決 U-7）

**互換区分**: MINOR（テストと docs のみ。公開 API は増えない）。**更新する docs**: [42](42_distribution.md) §5.11・§5.14（新設）、[12](12_review.md) §3「互換性」チェックリスト

**テストの置き場所と注意**: ダミー型の asmdef は `Tests/Runtime/ExternalContract/` と `Tests/Editor/ExternalContract/`。**E-6 のダミー validator は持ち込み先で `testables` を ON にしたときも発見される**（MS2026 は D-Drive 同梱テストを走らせている）ため、実データに対して結果を出さないこと・ダミー Data 型以外では `yield break` すること・`defineConstraints: UNITY_INCLUDE_TESTS` で通常ビルドに入らないことを確認する

**実装メモ（2026-10-03、FC-10 実装）**: U-7 = (a)・U-15 = (b)・U-6 = (a)・U-8 = (a) で実装した。本体コードは変えていない（テスト + docs + 合成 FBX フィクスチャ）。(1) **置き場所**: PlayMode は `Tests/Runtime/ExternalContract/`（asmdef `ExternalContract.Tests.Runtime`）、EditMode は `Tests/Editor/ExternalContract/`（asmdef `ExternalContract.Tests.Editor`、Runtime 側のダミー型を参照）。どちらも `DDrive.Tests*` ではない名前・`UNITY_INCLUDE_TESTS` 制約付きのテスト専用 asmdef（本体 asmdef は無変更）。ダミーの外部型は名前空間 `ExternalPackage.Fake`（1 型 1 ファイル。Timeline の `TrackAsset` / `PlayableAsset` は保存時に MonoScript を引くためファイル名 = 型名が必須）。D-Drive の `internal` には元から `InternalsVisibleTo` が無く、既存の `FakeAssetLoader` / `FakeNetBridge` も `DDrive.Tests.Runtime` 内の `internal` で見えないため、必要な最小のテストダブル（`ExternalContractLoader` / `ExternalContractBridge`）は `ExternalContractFakes.cs` に持つ。(2) **E-6 のダミー `IValidator`**: `ExternalDummyValidator`（`Target = AssetType.None`、`ExternalDummyData`〔`AssetIdDefinition` の無いダミー Data〕以外は `yield break`）。名前が `DDrive.Tests` で始まらない asmdef に置くので `CI.DiscoverValidators()` に出る = 持ち込み先で `testables` を ON にした「Validation > Run All」にも載るが、実データには何も報告しない。実データの種別（`MaterialData` / `CutsceneData`）に結果が出ないことをテストで確認。(3) **U-15 = (b) の結果**: 合成 ASCII FBX（`Tests/Editor/ExternalContract/Fixtures/ExternalContractRig.fbx`。四角形メッシュ + スキン + ボーン Hips / Spine / head + ブレンドシェイプ 3 つ〔`FC_test_Neutral_R0_C0` / `fcs_test_R0_C0` / `smile`〕）は**手書きで 1 回目から Unity 6000.3.13f1 が正しく読めた**（`SkinnedMeshRenderer.bones` = Hips / Spine / head、`GetBlendShapeName` = 3 つ、スケール 1。法線を足して取り込み警告 0）ので (a) へのフォールバックは不要だった。パッケージ内（`Packages/com.ddrive.core/Tests/`）にあり `Assets/SourceAssets` の自動取り込み（`MayaModelPostprocessor` / `ModelImportHandler` の対象）に拾われない。E-17 の取り込み確認は、これを一時フォルダ（`Assets/Tests/DDriveTemp/…/SourceAssets/Model/`）へコピーして `ImportRuleService.ProcessPaths` を通し、`ModelData.Prefab` のボーン名・シェイプ名・ローカル姿勢・スケールとコピー先 `ModelImporter` の設定（既定のまま）を、元フィクスチャと比べる。実 FBX（shizuku / UnityChan）での確認は T-Drive 導入後の人の確認（[52] §22）。(4) **既存テストとの重複回避**: E-1b（R-6 = U-3）は `ModelsManagerReturnNotifyTests.ExternalIPoolableOnRoot_BothOnReturnsRun`、E-8 の同期 Tick 版・E-16 の名前不変・FC-20 の Validator は `ExternalBlendShapeOwnershipTests`、返却時の全シェイプ復元は `ModelsManagerReturnNotifyTests`、FC-1 / FC-11 は `CutsceneSameAsTrackTests` / `MaterialPassKeywordTests` を指す（[42] §5.14 の表）。新規は実フレーム（`GameLoopDriver` + 実行順 10000 の LateUpdate）での E-8、外部アセンブリから書いた E-1 / E-13 など。(5) **設計から変えた点**: (a) E-6 は asmdef 案を採用（動的アセンブリ案は不要）。(b) E-7 の「Facial フォルダの案内ログ 1 回」は FC-6 で変わるため**ログの有無は固定せず「例外が出ない・Data が作られない」だけ**を契約にした（Cutscene フォルダの未知拡張子は案内ログ 0 件も固定）。外部 `ScriptedImporter` の取り込みは Unity 標準の仕組みで D-Drive 側の制約が無いので契約テストにしていない。(c) E-4 は設計どおり `DevRepoOnly`（UnityChan の FBX が要る）。合成 FBX では `CutsceneImportService` が要るアニメーション付きの Maya 構成（カメラ + キャラの 2 本）を作れないため。(d) E-9 は `DDriveRuntimeBootstrap.Loop.GameLoop` ではなく**テストで `GameLoopDriver` を置いて `GameLoop.Register`**（Bootstrap はシーン配線 + Addressables が要るため。Bootstrap の `Loop` は `GameLoopDriver` を返すだけで同じ経路）。(e) E-17 の静的確認は設計の「禁止プロパティが無い」に、「`ModelImporter` に書くのは `animationType` / `avatarSetup` / `sourceAvatar` だけ」の許可リスト方式を足した（`CutsceneFbxPostprocessor` が `CopyFromOther` のとき `sourceAvatar` も書くため。`TextureImporter.isReadable` 等の誤検知を避けるため ModelImporter 固有の名前だけを禁止パターンにし、パターン自体が空振りしていない対照テスト付き）。(6) **確かめた事実**: `ModelsManager` 経由のスポーンでは、プールの `Instantiate` 直後に `SetActive(false)` してから Rent で `SetActive(true)` するので、初回スポーンの `OnEnable` は 2 回呼ばれ得る（外部コンポーネントは「Awake は 1 回・OnEnable は Spawn ごとに 1 回以上、往復で OnDisable が 1 回増える」を前提にする。テストは差分で見ている）。ネット受信開始の初回 `ProcessFrame` の時刻は `NetworkTime - StartNetTime`。`Skip(Immediate)` は末尾（尺）で Evaluate する。(7) **doc16 の相違の扱い**: R-3（一時停止中は Evaluate されない）は E-3 として**現挙動を固定**（U-8 = (a)）。R-6 は E-1b。契約にしない（理由は [42] §5.14 冒頭）: R-1（`delayCall` 順序。FC-5 で公開イベント）・R-2（`Camera.main` の中身。FC-3 で API 化）・R-4（検査対象が `AssetDataBase` のみ。T-Drive が入口を `CutsceneData` / `ModelData` にする）・R-5（依存グラフ。FC-7）・R-8（`_ZTest` 等は書かれるが Validator が衝突として警告・除去。外部が頼る挙動ではない）・R-9（`Migrate` の上書き分岐。FC-15 で変える対象）・R-10 / R-11（文面・Profile の値の問題）。(8) **互換**: 公開 API の追加なし・シリアライズ形式の変更なし（スナップショット差分なし）。MINOR（テスト + docs。`docs/42` に §5.14 を新設 = 互換面に「外部拡張の契約」を追加）。テスト: EditMode `ExternalContract*`（16 件）・PlayMode `ExternalContract*`（14 件）。

### 4.12 FC-11（= doc17 M-1）: MaterialData にパスの無効化・キーワードの欄【優先 高】

**現状のコード**: `MaterialManager.GetOrBuild`（`Runtime/Material/MaterialManager.cs:408`）が共有 Material を `new Material(shader)` で作り、`MaterialCommonBinding.Apply`（Common が `_NORMALMAP` / `_EMISSION` / `_ALPHATEST_ON` 等のキーワードと `_Surface` / `_Blend` / `_ZWrite` / `_Cull` を決める、`MaterialCommonBinding.cs:105-218,258`）→ `ApplySpecific`（プロパティのみ）→ `renderQueue`。**パスの有効 / 無効・Specific 由来のキーワードを設定する経路は無い**。`FadeTo`（`:225`）は `new Material(from)` で一時 Material を作る。`MaterialData` の欄は §3.3 の表。

**変更案（追加のみ）**

1. `MaterialData` の**末尾**に `string[] DisabledPasses` と `string[] EnabledKeywords` を追加（既定は空 = 従来どおり。`[Tooltip]` 必須・`[Header("Passes / Keywords")]`）。名前は互換面に入るので吟味: パスは **LightMode タグ値で指定する**（`Material.SetShaderPassEnabled` の仕様。Pass の `Name` とは別。**未確認**: 実機で `ShadowCaster` を指定して確認）。キーワードの `DisabledKeywords`（無効化）は Common が有効にしたものを外したい需要が出てから足す（追加のみなので後からでよい。要判断 U-10）
2. `GetOrBuild` の `ApplySpecific` の後・`renderQueue` の前に `ApplyPassesAndKeywords(material, data)`: `DisabledPasses` の各名を `SetShaderPassEnabled(name, false)`、`EnabledKeywords` の各名を `EnableKeyword`。**空文字・存在しない名前は無視**（例外にしない）。`Common` が立てたキーワードの**後**に適用するので `EnabledKeywords` が最終
3. `FadeTo`: 一時 Material が `new Material(from)` の複製であり、パスの有効 / 無効やキーワードが引き継がれるかは**未確認**。引き継がれなければフェード開始時 / 終了時に `toData` / `fromData` で再適用する（テストで確認）
4. **Validator**（`MaterialDataValidator`、Warning で追加。§5.8）: `DisabledPasses` にシェーダーに無い LightMode 名、`EnabledKeywords` にシェーダーが宣言していないキーワード（`Shader.keywordSpace`。グローバルキーワードは列挙されないので**偽陽性があり得る = Info にするか未決 U-10**）。パスの列挙は一時 `Material` の `passCount` + `Shader.FindPassTagValue(i, "LightMode")` の想定（API の細部は未確認）
5. **Editor**: `MaterialEditorWindow`（`Editor/Material/MaterialEditorWindow.cs`、Specific の行は `:355-`）に「Passes / Keywords」欄。パスは選択中のシェーダーの LightMode 一覧から選ぶ（ShadowCaster / DepthOnly 等）、キーワードは自由入力 + 候補。`Undo.RecordObject` + `SetDirty`（CLAUDE.md §0-5）
6. **互換**: ContentHash（Id / Type / Address / Net のみ）・`CutscenePlayMsg` 等は不変。`SchemaVersion` は追加フィールドのみで上げない（既定が安全側）。`SerializedLayoutSnapshotTests` / `LegacyAssetFixtureTests`（旧版フィクスチャが警告 0 で読める）。SpecWeb（`Specs/assets.json`）が MaterialData のどの欄を出すかは未確認

**T-Drive 側の使い方**: doc17 §3 #10（`_ToonCastShadow = 0` → ShadowCaster を止める）・#11（輪郭線を使わない部位）で、生成シェーダーを増やさずに `DisabledPasses = { "ShadowCaster" }` / 輪郭線パスの LightMode 名で済む。ブリッジが `CharacterLook` から MaterialData を書き出すときに設定する。Toon 以外（自作シェーダー全般）にも効く。< 1.4.0 では従来どおり生成シェーダーで回避（`versionDefines` で切り替え）

**テスト**: PlayMode（`MaterialManagerTests`）= `ShadowCaster` を持つテスト用シェーダーで無効化 / 無指定は従来どおり / 存在しない名前で例外なし / キーワードが有効かつ Common のキーワードを壊さない / `FadeTo` 中・後も保つ。EditMode = Validator 2 検査・スナップショット・旧版フィクスチャ。**更新する docs**: [06] A-2 / A-4、`DesignerManual/material-data.html`・`material-editor.html`（機能として）

**実装メモ（2026-10-03、FC-11 実装）**: U-10 = (a) で実装。設計（上記 1〜6）のとおりで、実機（Unity 6000.3.13f1 / URP 17.3、PlayMode テスト `MaterialPassKeywordTests`）で確かめた「未確認」事項と補足は次のとおり。(1) **`SetShaderPassEnabled` は LightMode タグ値で効き、大文字小文字を区別しない**（`"ShadowCaster"` / `"SHADOWCASTER"` / `"shadowcaster"` のどれでも同じ。Pass の `Name` では効かない）。Material の状態（`GetShaderPassEnabled`）が false になることまでを自動テストで確認した。**実際に影が消えるかの目視は [52] §11**（SRP の描画が無効パスを飛ばす Unity の仕様に依存し、コードからは確認できないため）。(2) **パス列挙**: `Shader` に passCount は無いので、一時 `Material.passCount` + `Shader.FindPassTagValue(i, "LightMode")` で列挙する（`MaterialShaderInfo.CollectLightModes`。Runtime に公開の静的クラスを追加し、Validator と Editor が共有）。**Unity は組み込みの LightMode 値を大文字で返す**（`ShadowCaster` → `SHADOWCASTER`）ので、比較は大文字小文字無視（`ContainsIgnoreCase`）。Editor の候補には Unity が返した綴り（`SHADOWCASTER`）がそのまま出て、保存しても有効。(3) **`new Material(from)` はパスの無効化とキーワードを引き継ぐ。`Material.Lerp` はどちらにも触らない**（相手のものも持ってこない）。したがって `FadeTo` の一時 Material は複製元（from）の状態を持ち、`MaterialManager` は**さらにフェード先（to）の DisabledPasses / EnabledKeywords を重ねて適用する**（追加のみ。to が「有効に戻す」側のとき、フェード中は from の状態が残り、完了時に to の共有 Material に切り替わって戻る = テストで固定）。(4) **キーワード検査**: `Shader.keywordSpace`（ローカルキーワードのみ列挙）で宣言を調べる。グローバルキーワードは列挙されないので Info（`DD-MAT-KEYWORD-UNDECLARED`）。パス名は Warning（`DD-MAT-PASS-UNKNOWN`）。Validator は Runtime asmdef にあるため FixAction は無し。(5) **適用の漏れ確認**: MaterialData から `Material` を作る / 複製する経路は `MaterialManager.GetOrBuild`（Apply / ApplyData / Replace はこれを通る）と `FadeToData` の一時 Material の 2 つだけで、両方に適用した（`UiInteractable` の `new Material(template)` は MaterialData を使わない別経路で対象外）。Editor のプレビューは実 Manager を通る（ADR-4）ので追加対応なし。(6) **Editor**: `MaterialPassKeywordSection`（`Editor/Material/`）を `MaterialEditorWindow` に組み込み。パスはシェーダーの LightMode 一覧のチェックボックス（チェック = 無効。指定済みでシェーダーに無い名前も「（シェーダーに無い）」で出して外せる）、キーワードは自由入力 + 「（候補から追加）」（シェーダーが宣言するキーワード）。書き込みは `Undo.RecordObject` + `SetDirty`。ウィンドウ内の描画確認は足していない。(7) **互換**: `MaterialData` 末尾に 2 フィールド・`HasPassesOrKeywords`・`MaterialShaderInfo` を追加のみ。`SchemaVersion` 不変、`LegacyAssetFixtureTests` 緑。スナップショットは serialized-layout（2 行）と public-api-DDrive.Runtime（7 行）の追加のみ。テスト用シェーダー `Tests/Runtime/Shaders/DDriveTestPassKeyword.shader`（`Hidden/DDriveTests/PassKeyword`）を新設。(8) **SpecWeb**: `Specs/assets.json` は発注データで MaterialData の欄を列挙していない（`Tools/SpecWeb` にも欄名は無い）ので影響なし。DesignerManual の HTML を変えたので SpecWeb の再生成が要る（メモリの手順どおり。自分では push しない）。

### 4.13 FC-12（= doc17 M-2）: モデルのスポーン / 返却の通知【優先 高。FC-2 と同一 PR】

**現状のコード**: `ModelsManager.SpawnData`（`Runtime/Model/ModelsManager.cs:131`）は Rent → 位置 → LightLayer → LOD → `ModelInstancePoolable` の付与 → スロットの `MaterialManager.Apply`（`:195-203`）→ DefaultAnimation の `PlayAnim`（`:209`）の順で、**完了を外へ知らせる口が無い**。返却は `Despawn`（`CloseInstance` → `Return` / `Discard`）と、上限超過の強制回収（`PoolService.ForceReturn` → `ModelInstancePoolable.OnReturn` → `CleanupBookkeeping` → `CloseInstance`）の 2 経路で、どちらも `CloseInstance`（`:~236`）を通る（`Discard` は `OnReturn` を呼ばない）。Prefab 上のコンポーネントは `OnEnable`（Rent の `SetActive(true)`）に頼るしかなく、**スロット差し替えとの前後が保証されない**。`IAssetBehaviour`（`OnSpawn` / `OnTick` / `OnDespawn`）は `AssetDataBase.CreateBehaviour()` が返す Data 側の差し込み口で、**どの Manager からも呼ばれておらず**（R-10）、Prefab 上の外部コンポーネントは実装できない。

**推奨案（1 案）**: Prefab 上のコンポーネントが実装するインターフェースを、生成時にキャッシュして呼ぶ。

```csharp
namespace DDrive.Runtime.Model
{
    public readonly struct ModelInstanceContext
    {
        public readonly Handle<ModelMarker> Handle;
        public readonly ModelData Data;
        public readonly GameObject Root;
    }

    public interface IModelInstanceListener              // Prefab（ルート / 子）のコンポーネントが実装する
    {
        void OnModelSpawned(in ModelInstanceContext context);    // スロット適用・DefaultAnimation 開始の後
        void OnModelReturning(in ModelInstanceContext context);  // プールへ戻す（Discard も含む）直前
    }
}
```

- **キャッシュ**: `ModelInstancePoolable` が**生成時に 1 回** `GetComponentsInChildren<IModelInstanceListener>(true)` を配列で保持（FC-2 の `SkinnedMeshRenderer` / 既定重みのキャッシュと同じ箇所・同じタイミング）。Spawn / Return では配列を `for` で回すだけ（割り当て・LINQ・クロージャ・`foreach` の列挙子なし）。実行時に後から足したコンポーネントは対象外（Prefab に付ける前提を文書化）
- **呼び出し位置**: `OnModelSpawned` は `SpawnData` の末尾（戻り値の直前）。`OnModelReturning` は `CloseInstance` の先頭（アニメ停止の前。`Despawn` / 強制回収 / `Discard` の全経路）。**FC-2 の重みリセット（`ModelInstancePoolable.OnReturn`）はその後**（リスナーが `OnModelReturning` で FC_* を戻したり書いたりしても最後に既定へ揃う）
- **例外**: リスナー個別に `try/catch`（`Debug.LogException` + 継続）。ドメインリロード・Prefab の Missing script は `null` チェックで飛ばす
- **代案**: (B) 静的イベント `Models.Spawned` / `Models.Returning`（購読側が `Root` で自分の Prefab か判定する。Prefab に付けないコンポーネントにも届くが、登録解除忘れ・O(購読者数) の判定・Late Join 的な順序問題がある）。(C) `IAssetBehaviour` を ModelData 派生で配線する（外部 Prefab コンポーネントが使えず却下）。推奨は (A)（要判断 U-12）

**T-Drive 側の使い方**: `ToonCharacter` はスロット適用後に `CharacterLook` を配り（共有 Material に書かず、スロットで差し替わった Material を前提に MPB / バッファへ）、返却で後片付けする。`FacialCorrectionRunner` は返却時の重みの整理に使える（`OnDisable` でも足りる）。doc17 §5 M-2 の「OnEnable に頼ると前後が保証されない」問題が解消する。< 1.4.0 では従来どおり `OnEnable` + 遅延初期化

**実装メモ**: FC-2 の実装メモ（§4.3 末尾）にまとめて記載（同一 PR）。`ModelInstanceContext` に公開コンストラクタを足した点以外は上記の署名どおり。

**テスト・docs**: AC は [11](11_tasks.md) FC-12 行。**更新する docs**: [05] A-3、`ProgrammerManual/model-anim-api.html`。**FC-2 と同一 PR にする理由**: 同じ `ModelInstancePoolable` / `CloseInstance` / `SpawnData` を触り、リセットと通知の順序（通知 → リセット）を 1 つの変更として検証できるため

### 4.14 FC-13（= doc17 M-3）: インスタンスごとのマテリアル値【優先 中・着手条件付き】

**現状（§3.3）**: インスタンス単位でできるのは `Models.SetMaterial` によるマテリアル差し替えだけ。MPB は `VfxManager` だけが使う。`MaterialAnim` は共有 Material に書く。

**着手条件**: **T-Drive の表情パラメータ（T-25）を MS2026 で使い始めるとき**に、T-Drive 側の方式（08 Q-7: キャラクター ID ごとの StructuredBuffer。SRP Batcher を崩さない）と性能を比べて決める。T-Drive が StructuredBuffer 方式で足りるなら **D-Drive 側は実装しない**。

**実装する場合の形**: `Models.SetMaterialParam(handle, slotIndex, property, ParamValue)`（`ParamValue` は `ShaderParam` と同じ型）。`ModelInstancePoolable` が Renderer ごとの `MaterialPropertyBlock` を生成時に 1 回作って保持し、`SetPropertyBlock` で反映、**返却（`OnReturn`）で必ずクリア**（FC-2 / FC-12 と同じ場所。プールで次の借り手に値が残らない）。`SetMaterial` でマテリアルを差し替えたら該当スロットの値は維持 / クリアのどちらかを決める（U-14）。`FadeTo` の一時 Material は MPB が Renderer 単位なので影響しない。

**SRP Batcher への影響**: **MPB を付けた Renderer は SRP Batcher の対象外になる**（Unity の仕様。D-Drive の `DDrive_Lit` が SRP Batcher 互換かの確認と実測は本リポジトリでは未確認）。同じ MaterialData を共有する多数のキャラクターのうち MPB を使う Renderer だけが通常のバッチ経路に落ちる。代案は (a) キャラクターごとに Material を複製（共有が崩れメモリ・バッチ増）、(b) T-Drive 方式（ID でインデックスするバッファ。MPB 不要）。

**テスト**: 保留。着手時の AC は [11](11_tasks.md) FC-13 行。**更新する docs**: [06] A-3、[05]

### 4.15 FC-14（= doc17 M-4）: 変換表・テクスチャ規則を他パッケージから登録できるようにする【優先 中】

**範囲の整理**: doc17 M-4 は 3 点（`AssetSearch` の検索範囲・`TextureImportProfile` の規則・`ImportRuleService` のハンドラ配列）。**ハンドラ配列の外部登録は FC-6（doc16 C-6）と同じ修正なので FC-6 に寄せた**（§4.7）。FC-14 は変換表とテクスチャ規則に絞り、FC-6 と相互参照する（重複して実装しない）。

**現状のコード**: 変換表は `MaterialConvertWindow.ReloadTables`（`Editor/Material/MaterialConvertWindow.cs:295`）が `AssetSearch.FindAssets`（`Editor/AssetSearch.cs:26,53`）で探す。`AssetSearch.Roots` は `public static readonly string[]`（`Assets` + D-Drive のパッケージパス）で、他の全検索（ModelData の探索ほか）に効く。テクスチャ規則は `TextureImportProfile`（`Editor/Material/TextureImportProfile.cs`）の `Rules`（Profile アセット。既定 `DefaultRules()`、上から最初に一致）を `TexturePostprocessor.OnPreprocessTexture`（`:14-`）と `TextureDataValidator` が使う。

**変更案（追加のみ。Editor の拡張点 = 弱い互換面）**

- (a) 変換表: `TypeCache` で発見する `IShaderConversionTableProvider`（`IEnumerable<ShaderConversionTable> GetTables()`、public・引数なしコンストラクタ）を追加し、`ReloadTables` が `AssetSearch` の結果に足す。**`AssetSearch.Roots` を広げない**（全検索に影響し、キャッシュ・性能の影響が大きい）。T-Drive のブリッジ（doc08 §6「シェーダー変換表」）はパッケージ内の表をそのまま返せる（罠 4 の「`Assets/` に生成」が不要）
- (b) テクスチャ規則: `TypeCache` で発見する `ITextureImportRuleProvider`（`IEnumerable<TextureImportProfile.Rule> GetRules()`）を追加し、`TryMatch`（`TexturePostprocessor` / `TextureDataValidator` 共通）が **Profile の `Rules` の前に外部規則を評価**する（`_ToonMask` → sRGB オフが `T_` 接頭辞より前に効く = 罠 2）。外部規則が Profile を上書きしてしまう懸念は、外部規則を名前空間付きの接尾辞（`_ToonMask`）に限る運用で抑える（要判断 U-11: 外部規則を前 / 後ろのどちらに置くか。推奨 = 前）。パス判定（`AppliesTo` = `IncludePathContains`）は従来どおり（R-11）
- 各提供口の呼び出しは `try/catch` で隔離

**実装メモ（2026-10-03、FC-14 実装。FC-6 と同一 PR）**: U-11 = (a)。(1) **変換表の検索範囲 = 提供口を選んだ**: `AssetSearch.Roots` を広げず、`IShaderConversionTableProvider`（`GetTables()`）を `TypeCache` で発見する案（§4.15 の推奨）で実装した。理由: `Roots` は ModelData の探索ほか全検索に効き、`AssetDatabase.FindAssets` は走査ファイル数に比例してネイティブメモリを確保する（`AssetSearch` 冒頭のコメント）ので Packages 全体（Unity 公式パッケージを含む）を足すと全検索の性能・メモリが悪化する。提供口は外部が必要な表だけを返せ、検索結果に依存せず決定的。`Editor/Material/ShaderConversionTableProvider.cs` の `ShaderConversionTables.Collect()` が集め、`MaterialConvertWindow.ReloadTables` が使う（変換表アセットを使う箇所はここだけ。`UnityMaterialMigrator` の `ShaderMap` はコード上の別物）。**優先順位（同じ元 → 先シェーダーの表が複数あるとき、先に並んだものが使われる）= ① `Assets/` の表（パスの序数順）> ② 外部提供口の表（型のフルネーム順 → 返した順）> ③ D-Drive 同梱の表（パスの序数順）**。同じ実体は 1 回だけ（null は無視）。`/Tests/` 配下は従来どおり検索結果から除外。従来の `Collect` 相当（`FindAssets` の結果順）は並びが未定義だったので、パス順にしたのは決定性の改善（表が 1 つだけ / 組が重ならない通常の使い方では結果は変わらない）。(2) **テクスチャ規則**: `ITextureImportRuleProvider`（`GetRules()`、規則の型は既存の `TextureImportProfile.Rule`。新しい概念は追加していない）。`TextureImportRuleProviders.Rules`（ドメインリロードごとに 1 回 `GetRules()` を呼んでキャッシュ。`Pattern` 空は無視）を、`TextureImportProfile.TryMatch` が **Profile の `Rules` の前**に評価する。`TexturePostprocessor` / `TextureDataValidator` / `TextureDataImporterSync` / `ImportRule(Texture)` / `MayaMaterialImporter` / `MaterialEditorWindow` はすべて `TryMatch` を通るので 1 箇所で全てに効き、`AppliesTo`（パス判定 = R-11）・`Enabled` は従来どおり Profile 側。(3) **Profile による上書き = 「同じ条件の規則があれば Profile 優先」**: Profile の `Rules` に外部規則と同じ `Match` の種類 + `Pattern`（大文字小文字無視）の規則があれば、その外部規則は使わず Profile の規則が効く（プロジェクトが Profile で `_ToonMask` に別の設定を明示した場合に勝てる）。条件が違う規則は上書きされない（外部が先。`T_` 接頭辞より前に効くのが目的）。外部規則を無効にしたいときは Profile に同じ条件の規則を足す。外部規則が 0 件なら完全に従来どおり（既存テスト無改修で green）。(4) 例外は提供口単位で隔離（`Debug.LogException` + 他の提供口は有効）。規則は 1 回だけ集めるのでログも 1 回。テスト用: `ShaderConversionTables.ResetProviderCacheForTests()` / `TextureImportRuleProviders.ResetCacheForTests()`。契約テストは §4.7 と同じ `ExternalContractImportExtensionTests`（E-21 / E-22）。

**T-Drive 側の使い方**: 罠 2・罠 4 の運用の決まり（プロジェクトの Profile に規則を足す / `Assets/` に表を生成）が不要になる。< 1.4.0 では従来どおり

**テスト・docs**: AC は [11](11_tasks.md) FC-14 行。**更新する docs**: [06] B-3、[09]（Material の節）

### 4.16 FC-15（= doc17 M-5）: 知らないシェーダーを `DDrive/Lit` に変換しない【優先 中】

**現状のコード（§3.4 R-9）**: `ModelSlotBinder.Rebuild(ensureMaterials: true)` → `EnsureMaterialData` → (FBX 内蔵) `MayaMaterialImporter.ImportModel` / (単体 .mat) `UnityMaterialMigrator.Migrate`。新規作成のシェーダーは `ResolveTargetShader`（Profile の `TargetShader` → `DDrive/` 接頭辞ならそのまま → **`DDrive/Lit`**）。`Migrate` は未対応シェーダーを Lit に変換し（警告ログ）、**既存 Data のシェーダーも `data.Shader = target` で上書き**する。FBX 経路は既存 Data のシェーダーが null のときだけ設定する。`MayaModelPostprocessor` は FBX の取り込みで自動実行する（罠 3）。

**互換区分の判定（[42] §5）**: `DDrive.Editor` の public は §5.4 の互換面外。ただし (1) **生成されるデータ（MaterialData の `Shader`）が変わる**（持ち込み先で同じ操作をしたときの結果が変わる）、(2) 「Lit への変換」は D-Drive の意図した機能（Unity 標準マテリアルの D-Drive 標準シェーダーへの移行、`UnityMaterialMigrator` の 2026-09-11 の設計）であり、既存の `Migrate` の挙動に依存する運用があり得る → **§5.9「弱い互換面」の挙動変更として MINOR（CHANGELOG の互換性節に「挙動の変更・生成されるデータが変わる」を明記）**。`MayaImportProfile` への欄追加は §5.1 のシリアライズ互換（追加のみ）。MAJOR にはしない

**推奨案（2026-10-03 決定: 不採用。決定は U-9 = (c) 確認ダイアログ + Profile の欄。下の実装メモ参照）**:

1. 知らないシェーダー（`ShaderMap` にも `DDrive/` 接頭辞にも無い）は、**そのシェーダーのまま MaterialData を作る**（`Shader = source.shader`、Specific は `MaterialSpecificResolver.Merge` で登録し同名プロパティの値は `CopySpecificValues` が引き継ぐ）。Common は従来どおり共通名から作る
2. 既存 MaterialData の `Shader` を `Migrate` が上書きする分岐は、**既に有効なシェーダー（non-null）が入っているなら触らない**（FBX 経路の `existing.Shader == null` のときだけ設定、と同じ規則に揃える）
3. `DDrive/Lit` への変換は**明示操作のときだけ**（`MaterialConvertWindow` / 既存のメニュー）
4. `MayaImportProfile` に `UnknownShaderPolicy`（`KeepSource` = 0 / `ConvertToLit`）を追加し、`ResolveTargetShader` / `Migrate` / `ModelSlotBinder.Rebuild` が従う。**既定を `KeepSource`（0）にする = 既存の持ち込み先でも新しい挙動になる**（既存 Profile の欄は 0 で読まれるため）。旧挙動に戻したい人は `ConvertToLit` を選ぶ（要判断 U-9）

**代案**: (A) 確認ダイアログを出す（バッチ・自動取り込み（罠 3）では使えず不向き）。(B) T-Drive のシェーダー名だけを許可リストにする（D-Drive が T-Drive を知ることになり原則 §2-1 に反する。却下）

**T-Drive 側の使い方**: 罠 1・3 の運用（スロットを MaterialData で埋めておく / 取り込み対象から外す）が不要になる。ただし Common の再計算は走る（`ImportMaterial` が Common を更新する）ので、ブリッジが書き出した値が Common と食い違わない前提は残る（T-Drive 側の確認事項）

**テスト・docs**: AC は [11](11_tasks.md) FC-15 行。**更新する docs**: [06]（A-2 Maya 取り込み）、[09]、`DesignerManual/model-editor.html`（「元ファイルを再読み込み」の挙動を機能として）

**実装メモ（2026-10-03、FC-15 実装）**: **推奨案（既定 `KeepSource`）は採用されず、U-9 = (c)（確認ダイアログ + Profile の欄）で実装した**（ユーザー決定: 知らないシェーダーを見つけたら確認ダイアログを出す。ダイアログを出せない自動取り込みの経路は従来どおり）。

1. **Profile の欄**: `MayaImportProfile`（`Editor/Material/`）の末尾に `UnknownShaderPolicy UnknownShaderPolicy = Ask`（`[Tooltip]` 付き。enum は同じ `DDrive.Editor.Materials` に新設: `Ask = 0` / `KeepSource = 1` / `ConvertToLit = 2`）。既存 Profile アセットは欄が無く 0 = `Ask` で読まれる。専用エディタは無く Inspector の既定表示に出る。
2. **「知らないシェーダー」の定義**: 現行コードの判定どおり（`UnityMaterialMigrator.IsSupported` = ShaderMap にも `DDrive/` 接頭辞にも当たらない。`shader == null` は対象外）。`UnknownShaderGuard.IsUnknown`。**D-Drive は T-Drive のシェーダー名を知らない**（許可リストにしない）。
3. **`Ask` の解決は 1 操作 1 回・先に走査してから適用**: `UnknownShaderGuard.TryResolve(profile, 対象 Material 全部, interactive, out handling)`。`Ask` かつ `interactive` かつ知らないシェーダーが 1 件以上あるときだけ `EditorUtility.DisplayDialogComplex`（「元のシェーダーのまま保つ」/「キャンセル（何もしない）」/「DDrive/Lit に変換」。シェーダー名と件数を本文に列挙し、5 種類を超えたら「ほか N 種類」。「今後聞かない」は付けず、本文で Profile の欄を案内）。キャンセルは false を返し、呼び出し側は MaterialData も Slots も書き換えずに中断する。結果は `UnknownShaderHandling`（`Keep` / `Convert`）で取り込み処理へ**明示的に**渡す（グローバルな推測はしない）。
4. **対話 / 非対話は呼び出し元が決める**: 対話 = `ModelEditorWindow` の「元ファイルを再読み込み」（`ModelSlotBinder.Rebuild(…, interactive: true)`）、`Generate` メニューの「選択した Material を D-Drive/Lit・Unlit の MaterialData に変換」「選択したモデルから MaterialData を生成」（いずれも `UnknownShaderGuard.IsInteractiveSession()` = `!Application.isBatchMode`）。非対話 = 既存シグネチャのまま（`MayaModelPostprocessor` の自動取り込み・`RebindForModelPath`・`SourceDataCreation` の .mat 取り込み・テスト）。既存シグネチャは Profile の欄だけで決める（`KeepSource` なら保つ、それ以外は従来どおり Lit）。
5. **`KeepSource`**: `ResolveTargetShader`（`MayaMaterialImporter`）が TargetShader → `DDrive/` 接頭辞 → （知らないシェーダーなら）元のシェーダー → Lit の順。`Migrate` は `Shader = source.shader`、Specific は `MaterialSpecificResolver.Merge` + `CopySpecificValues` が引き継ぐ。`Migrate` の既存 Data の Shader 上書き（`data.Shader != target`）は、知らないシェーダーを保つとき **既に有効な Shader（non-null）が入っていれば行わない**。`Convert` の場合の上書きは従来どおり（変えていない）。
6. **設計から変えた点**: (a) 既定は `KeepSource` ではなく `Ask`。`Ask` の非対話経路は従来と同じなので、唯一の挙動変更は対話経路でダイアログが増えること。(b) 推奨案 3「`DDrive/Lit` への変換は明示操作のときだけ」は採用しない（`ConvertToLit` / ダイアログの「変換」で従来どおり）。(c) 公開 API は追加のみ（`Migrate` / `ImportModel` / `ImportMaterial` / `ResolveTargetShader` / `Rebuild` / `EnsureMaterialData` に引数を足した**別オーバーロード**。既存のシグネチャは不変）。(d) `Migrate` の既存の警告ログに「Profile の UnknownShaderPolicy を KeepSource にすると元のシェーダーを保てます」を 1 文足した。(e) テストの差し替え口として `UnknownShaderGuard.PromptOverride`・`MayaImportProfile.TestOverride` を public static で置いた（`InternalsVisibleTo` 未設定の既存の慣習。使い終わったら null に戻す）。
7. **既知の範囲外**: `DDrive/` 接頭辞のシェーダーのうち変換表に無いもの（例 `DDrive/AiStandardSurface`）を **単体 .mat として `Migrate` すると従来どおり Lit に変換される**（`Migrate` の既存の挙動。FBX 経路の `ResolveTargetShader` は保つ）。`SourceDataCreation` の .mat 取り込みは 1 ファイルごとの呼び出しで 1 操作にまとめにくいため非対話のまま（Profile を `KeepSource` にすれば保てる）。
8. **T-Drive 側の使い方**: T-Drive を使うプロジェクトは Profile の `UnknownShaderPolicy = KeepSource` を設定する（罠 1・3 がダイアログ無しで避けられる。T-Drive の検証 U-23 で案内できる）。< 1.4.0 の D-Drive では欄が無いので従来の運用（スロットを MaterialData で埋める / 取り込み対象から外す）。
9. **テスト・互換**: EditMode `UnknownShaderPolicyTests` 19 件（TryResolve の 1 操作 1 回・3 択・知らないシェーダーが無ければ出ない・非対話・KeepSource / ConvertToLit は出さない、`Migrate` の Lit / 保つ / 既存 Data の Shader を上書きしない / `Convert` は従来どおり上書き、`ResolveTargetShader`、`Rebuild` の保つ・変換・キャンセル〔何も作らない〕・非対話）。互換区分は MINOR（Editor の弱い互換面。CHANGELOG の互換性節に明記）。スナップショット（serialized-layout / enums / editor-contract）の差分は なし（`MayaImportProfile` は Editor の ScriptableObject で serialized-layout・enums・editor-contract のいずれの対象にも入らず、`EditorContractSnapshotTests` を含む EditMode 全件が無改修で green）。

### 4.17 FC-16（= doc17 M-6）: モデルの名前付きスロットセット【優先 低】

**設計案**: `ModelData` に `MaterialSlotSet[] SlotSets`（`Name` + `MaterialSlot[]`）を**追加**し、`Models.ApplySlotSet(handle, name)` で切り替える。基本の `Slots` を `RendererPath` + `SlotIndex` で上書きし、`ModelInstance.Materials`（インスタンスの現在値。`SetMaterial` と同じ台帳）を更新する。未知の名前は警告 + no-op。`SetMaterial` との優先順位（後勝ち）を定義する。

**7-7 `AssetVariantSet`（[13] A-6）との関係**: 7-7 は **Registry が現在の品質 tier / プラットフォームで ID → Data を解決する**（ゲームコードは同じ ID を使うだけ。ID 単位）。スロットセットは **実行時にインスタンスが名前で選ぶ切り替え**（ルックの A/B・衣装違い。インスタンス単位）。層が違い共存できる（スロットセットの MaterialId 自体が 7-7 で tier 解決されてもよい）。T-Drive の A/B は当面「採用した版を確定して書き出す」方式（doc17 §3 #6）なので後回し。**FC-12（スポーン通知）が先**（切り替えの後に外部が再初期化できる）。

### 4.18 FC-17（= doc17 M-7）: ModelData に外部データへの汎用参照欄【優先 低】

**設計案**: `ModelData` に `ExternalReference[]`（`Key` 文字列 + `UnityEngine.Object`）を追加。`CharacterLook`・Facial のデータを ModelData から辿れる。**FC-7 との関連**: 依存追跡（「使用箇所」・安全な削除）は `AssetId` / `AssetRef` の辺だけを持つ（§3.2 R-5）ため、`UnityEngine.Object` の直接参照は辺にならず、**欄を足しただけでは追跡に乗らない**。乗せるには FC-7 と同じく `DependencyGraphCollector` の拡張（Object 参照の辺 = 依存グラフのキャッシュ形式の追加）が要る。Addressables の依存としては ModelData → 参照先が自動で運ばれる。Prefab のコンポーネントの直接参照（現状）で足りる間は後回し。実施するときは FC-7 の結論（`.playable` の走査）と同時に判断する。

### 4.19 FC-18（= doc17 M-8）: プロジェクト設定の検証の拡張点【優先 低】

**現状**: `ProjectSetupValidator`（`Editor/Validation/ProjectSetupValidator.cs:26`）は `IUniversalValidator`。`CI.DiscoverValidators` が発見する public・引数なしコンストラクタの `IValidator` / `IUniversalValidator` は外部アセンブリのものも載り、Data が 0 件でも 1 回呼ばれる（`ValidatorRegistry.RunAll`）。**したがって T-Drive は今でも `IUniversalValidator` で「Renderer Feature が入っているか」等を検査して `Run All` に出せる**（doc17 M-8 も「今でも代用できる」と認めている）。ウィザード（`ProjectSetupWizardWindow`）は `ProjectSetupInspector`（別の純ロジック）を使うので、外部の検査は出ない。

**設計案**: まず代用方法の文書化と FC-10 の E-6 / E-18 で固定する（コード変更なし）。ウィザードにも出したい要望が出たときだけ `TypeCache` 発見の `IProjectSetupCheck` を追加する（優先 低・後回し）。

### 4.20 FC-19（= doc17 M-9）: 検証の警告の調整【優先 低】

**現状のコード**: (a) `MaterialDataValidator`（`Runtime/Material/MaterialDataValidator.cs:41`）は `!mat.Common.Albedo.IsValid` なら**無条件に Warning**（「`Common.Albedo`（ベースカラー）が未設定です」）。`MaterialCommon` には `AlbedoTint`（色）があり、色だけのマテリアル（Albedo テクスチャ無し・`AlbedoTint` を設定）でも警告が出る。(b) `MaterialData.RenderingLayerMask` は欄だけで、読むのは `MaterialConverter`（別 MaterialData へのコピー）だけ。Tooltip は「0 なら Renderer 側の設定を変えない」と書くが実際は何もしない。効くのは `ModelData.LightLayerMask`（`ModelsManager.SpawnData` が `renderer.renderingLayerMask` に書く）。

**変更案**: (a) Albedo が無くても `AlbedoTint` が既定（白）以外なら Warning を出さない（警告を減らす変更は自由。[42] §5.8）。(b) 実装する案（`RenderingLayerMask` を Renderer に書く）は `ModelData.LightLayerMask` と意味が衝突する（Renderer 単位 vs マテリアル単位、どちらが勝つか）ので**不採用**。Tooltip を「未使用。ライトレイヤーは ModelData.LightLayerMask」に直し、値が 0 以外のときだけ Info を出す。`ValidatorSeverityRegistryTests` のスナップショット更新。PATCH 相当（CHANGELOG の互換性節に記載）。

### 4.21 FC-20（f27702e 由来・まとめ役の抽出）: `FC_` 接頭辞の予約と、モデル取り込みが名前・ボーンを保つことの確認【優先 高寄りの中。FC-10 の前か同時】

T-Drive の `f27702e` が前提にする (a) シェイプ名・(b) ボーン名の完全一致と、(d) Runner が LateUpdate で `FC_*` だけを書くこと（§3.5）のうち、D-Drive 側で**確認した事実**と、足すもの。D-Drive への明示的な要望ではなく、まとめ役が抜き出した契約。

**確認した現状（実コードを読んだ範囲。Unity 未起動のため実機の取り込み結果は未確認）**

| # | 確認項目 | 結果 |
|---|---|---|
| 1 | モデル取り込みはブレンドシェイプを取り込むか | D-Drive の Editor は一般のモデル FBX の `ModelImporter` 設定を**一切書かない**（`importBlendShapes` を含め Unity の既定のまま = 取り込む。**既定値そのものは実機で未確認**）。書くのは Cutscene のキャラ FBX（`CutsceneFbxPostprocessor.OnPreprocessModel`）の `animationType` / `avatarSetup` だけ。`ModelImportHandler` は FBX のルート GameObject を `ModelData.Prefab` に入れる（Prefab を作り直さない）。カットシーン用 FBX はブレンドシェイプ OFF が手順（FC-8 で将来の選択肢） |
| 2 | シェイプ名を変えないか / `AnimData.BlendShapes` はどの形の名前で引くか | D-Drive は名前を**加工・正規化しない**。`AnimatorProxy.ApplyBlendShapes`（`Runtime/Anim/AnimatorProxy.cs:142`）は `ShapeName` を `Mesh.GetBlendShapeIndex`（完全一致・大文字小文字区別）で引く。Editor の一覧も生の名前。Unity が FBX のシェイプ名にノード名接頭辞（`blendShape1.` 等）を付けるかは Unity の取り込みと FBX の出力設定による（D-Drive の関与なし。T-Drive の出力プリセットの責務。**未確認**） |
| 3 | ボーンの GameObject を消さないか（Optimize Game Objects / Strip Bones 相当） | D-Drive は `optimizeGameObjects` / `extraExposedTransformPaths` を設定せず、`Mesh` / `bones` / `rootBone` を書くコードも 0 件（grep）。Spawn は `SetPositionAndRotation` + `SetParent(worldPositionStays: true)` のみ |
| 4 | メッシュを複製・再構築・圧縮してシェイプを落とす経路 | 無い。`CombineMeshes` / `new Mesh` / `sharedMesh =` / `MeshUtility` / メッシュ圧縮の設定は D-Drive の Runtime / Editor に 0 件（Editor のプレビュー系は借用した Animator の重みを保存・復元するだけ: `SceneAnimPreviewDriver`）。`ApplyLodProfile` は `LODGroup` の閾値だけ |
| 5 | スケール（cm → m、自動単位）でボーン階層にスケールが入らないか | D-Drive は `localScale` / `lossyScale` を `Runtime/Model` / `Runtime/Cutscene` で書かない。FBX の単位変換は Unity の取り込み（`useFileScale` 等、D-Drive は触らない）。Cutscene の `CutsceneRoot` はスケール 1 |
| 6 | 他にブレンドシェイプを書く経路 | `SetBlendShapeWeight` は `AnimatorProxy`（`AnimData.BlendShapes` の名前指定だけ）の 1 箇所のみ。Cutscene / Presentation / VFX は書かない（grep）。Cutscene の `AnimationTrack`（FBX のアニメーションクリップ）が FBX 内のブレンドシェイプのカーブを再生する可能性はあるが、手順書は OFF（FC-8）。Maya の「キーに焼く」（doc14 §5.8 R-19）で `FC_*` のカーブが FBX のクリップに入ると Animator が書く（Update 系、Runner の LateUpdate が後で上書き。**未確認**） |

**変更案（追加のみ）**

1. **接頭辞の一覧（汎用の形）**: `DDrive.Runtime.Anim` に静的クラス（例 `ExternalBlendShapePrefixes`）を追加し、「外部パッケージが所有するシェイプ接頭辞」の一覧（既定 `FC_`・`fcs_`。照合は **Ordinal（大文字小文字を区別する完全一致 = T-Drive の規則）**）と `IsOwnedExternally(string shapeName)` を持つ。**定数は 1 か所**・Facial 専用の名前にしない・`TDrive.*` は参照しない（文字列の既定値だけ）。他の外部パッケージが接頭辞を足せる口（`Register(prefix)`）も用意するかは要判断 U-13（既定は一覧のみ）
2. **Validator**: `AnimDataValidator`（`Runtime/Anim/AnimDataValidator.cs:55`）が `BlendShapes[].ShapeName` が所有された接頭辞で始まる場合に **Warning**（「外部パッケージが管理するシェイプです。D-Drive の AnimData から書くと衝突します」）。重さは Warning（[42] §5.8 の「新しい検査は Warning で追加」に従う。Error は次の MINOR 以降）。AnimEditorWindow の「シェイプが対象モデルにありません」検査（`:988-995`）とは別
3. **Editor の表示**: `AnimEditorWindow` のモデル情報のシェイプ一覧（`:755-769`）と BlendShape 名の入力補助は、所有された接頭辞のシェイプを**既定で隠し**「外部管理 N 件」と件数だけ表示（トグルで表示可）
4. **実行時の保証**: `AnimData.BlendShapes` に何も書かなければ D-Drive は `FC_*` に触れない（構造上の保証。`AnimatorProxy` は指定名のみ書く）。実行時にも `FC_*` を弾く（警告 + no-op）案は、Validator を無視した場合の保険になるが挙動の追加になるので採らない（Validator + E-16 で足りる。要判断 U-13）
5. **FC-2**: プール返却時の既定重みへの復元は **`FC_*` を含む全シェイプ**を対象にする（Runner の `OnDisable` と二重でも無害）。FC-2 の AC に明記（§4.3）
6. **FC-10 の契約テスト**: E-16 / E-17（§4.11）で、名前・ボーンが取り込み〜スポーン〜`AnimManager.Tick`〜プール往復で変わらないこと、LateUpdate の外部書き込みが次フレームの D-Drive の Update で上書きされないことを固定

**互換区分**: MINOR（新しい public 静的クラス = `public-api-DDrive.Runtime.txt` 更新・Warning の追加。Editor の表示変更は互換面外）。**テスト**: AC は [11](11_tasks.md) FC-20 行。**更新する docs**: [05] B-6（Validation）・[05] A-3（BlendShape の節）、`ProgrammerManual/model-anim-api.html`・`DesignerManual/anim-editor.html`（機能として）

**実装メモ（2026-10-03、FC-20 実装）**: U-13 = (a) で実装。`Runtime/Anim/ExternalBlendShapePrefixes.cs`（`public static class`。`All`〔`IReadOnlyList<string>`、`FC_`・`fcs_`〕・`IsOwnedExternally(string)`）。設計との差: (1) 名前は案どおり。登録口・実行時の弾きは作らない。(2) 確認項目 1〜6 を実コードで再確認し、前回の読みと変わらず問題なし（`SetBlendShapeWeight` は `AnimatorProxy` の AnimData 指定名、FC-2 の `ModelInstancePoolable` の返却時復元、Editor プレビューの復元の 3 箇所のみ。`ModelImporter` を書くのは `CutsceneFbxPostprocessor` のみ）。(3) 「BlendShape 名の入力補助」は実コードに無い（`BlendShapes` は Inspector の素の配列）ため、Editor の変更はモデル情報の BlendShape 一覧（`AnimEditorWindow.RefreshModelInfo`）だけ。所有接頭辞のシェイプを既定で隠し「外部管理 N 件」を出す。詳細の折りたたみ内トグル「外部管理のシェイプも表示」で表示できる。AnimData の既存の値は消さない。(4) Validator のコードは `DD-ANIM-BLENDSHAPE-EXTERNAL-OWNED`（Warning）。(5) D-Drive がブレンドシェイプ名を指定して書く Data は `AnimData.BlendShapes` だけ（grep: `ShapeName` / `SetBlendShapeWeight` / `GetBlendShapeIndex`）。(6) テスト: PlayMode `ExternalBlendShapeOwnershipTests`（判定の大文字小文字・Validator・合成 Mesh に `FC_Hero_Neutral_R0_C0` / `_Ex` / `FC_Hero_Persp_K0` / `Smile` を持つ Prefab を Spawn → `AnimManager.Tick` → 外部の後書きが次の Tick で上書きされない・名前が変わらない・AnimData 指定の通常シェイプだけが書かれる・プール往復）。FBX フィクスチャを要する E-17 は FC-10。

## 5. D 群（doc16 §5）= 不採用（doc17 に D 群に当たるものは無い）

doc16 §5 の D-1〜D-7（Facial を D-Drive の一級の種別にする一式）は**やらない**。チケットにしない。

| # | doc16 の変更 | 不採用の理由 |
|---|---|---|
| D-1 | `AssetType.Facial` を追加 | Facial は T-Drive の型。D-Drive は T-Drive を知らない（§2-1）。種別を足すと `AssetType` の互換面（[42] §5.2）に**永久に**入る |
| D-2 | `AssetNamingService` / `AssetCreationService.GetCatalogName` の switch | D-1 に従属 |
| D-3 | `AssetIdGenerator.KnownPrefixes` に接頭辞 | doc16 自身が指摘のとおり P-13 以降は追加禁止（[42] §5.3）。入れないと定数名が `FACEID.FACESmile` のようになる問題も、種別を作らなければ起きない |
| D-4 | `FacialManager` の配線 + `Facial.*` の窓口（`DDriveRuntimeBootstrap` / `CutsceneDirectorManagerRefs` / `CutsceneEditModeManagers`） | Runner を自立コンポーネントにしたので不要（Manager を `new` するのは Bootstrap・テスト・Editor プレビューだけという規約にも合う） |
| D-5 | `AssetEventDispatcher` の switch で `PlayAsset` から Facial を再生 | Facial の再生入口は Timeline クリップ / Runner のフィールドで足りる |
| D-6 | `PresentationTrack.TrackKind` に Facial | 閉じた enum + `PresentationManager` の switch + PresentationEditor の UI と規模が大きい。doc16 のとおり Presentation から Cutscene を入れ子にできる（本書では未確認）ので当面は Cutscene 経由で足りる。**必要になったら FC-4（外部マーカー）か別の拡張点で再検討** |
| D-7 | D-Drive 内に Data を置く場合の手続き（`[DataEditor]`・互換スナップショット・マニュアル） | D-1 に従属 |

`ModelData` に Facial のデータを指す欄を足す案も、Runner を Prefab に付ける方式（A-1）で足りるので不要（doc16 §5 末尾に同じ判断）。

## 6. doc16 §6「T-Drive 側が従う D-Drive の決まり」の D-Drive 側からの確認

| 決まり | D-Drive 側の確認 |
|---|---|
| 消費者はパッケージを改変しない・`DDrive.*` 名前空間を使わない | **適切**（[42] §4.5 / `CLAUDE.md` §0-10）。T-Drive は `TDrive.Facial.*`。D-Drive の型を使うのはブリッジ asmdef（`com.ddrive.core` の `versionDefines` 付き）だけ。**D-Drive が FC チケットで足す型は `DDrive.Runtime` / `DDrive.Editor` の名前空間**（外部が使う側）で、`TDrive` を参照しない（§2-1） |
| 例外で止めない（警告 + no-op） | **適切**。D-Drive も同じ（`CLAUDE.md` §0-4）。FC-1・FC-4・FC-5 の外部呼び出しは `try/catch` 隔離 |
| データは実行時に読み取り専用 | **適切**。`FacialCorrectionData` / `Overrides` は D-Drive の `AssetDataBase` ではないので D-Drive の Data 規約（`Undo.RecordObject` + `SetDirty`）の直接対象外。ただし D-Drive の Editor が書き換える Data（FC-5 のリスナーが `CutsceneData.Bindings` を書く）は `EditorUtility.SetDirty` + 保存が必要（FC-5 のリスナー側の責務。D-Drive は取り込みの一部として保存を呼ぶ） |
| `Instantiate` / `Resources.Load` を直接使わない | **適切**。Runner は何も生成しない。データは Prefab / クリップからの直接参照（Addressables の依存として運ばれる）。**注意**: `ForbiddenApiScanner` は D-Drive のコード走査用で、T-Drive のコードは対象外 |
| カットシーンのファイル名 `<Shot>__<ModelIdentifier>`（アンダースコア 2 つ） | **一致**。`CutsceneShotParser.ParseFileName`（`Editor/Cutscene/CutsceneShotParser.cs`）が `__` で分け、`CutsceneFbxPostprocessor.OnPreprocessModel` が同じ規則でキャラ FBX を判定。`.fctrack` も同じ規則、置き場所 `SourceAssets/Cutscene/<Category>/` は D-Drive の取り込みフォルダ（`KnownNonTargetTypeFolders` で案内ログなし。`CutsceneImportService.ProcessPaths` は `.fbx` 以外を無視） |
| 単位・軸: cm / Y-up / FBX は自動単位、フレームレート 30 or 60 | **一致**（[26] §5.3「fps とタイミング」。`CutsceneData.FrameRate` / `CutsceneFpsValidator`）。`.fctrack` は秒単位 + frameRate を併記するので D-Drive の fps 検査の影響は受けない（トラックの型が `CutsceneFpsValidator` の対象外） |
| ブレンドシェイプは名前で引く・重みは 0〜100 | **一致**。`AnimatorProxy.ApplyBlendShapes` も名前引き（`GetBlendShapeIndex`）・Unity の 0〜100 を直接書く（`AnimData.BlendShapes[].Weight` カーブの値がそのまま）。Runner が ×100 するのは T-Drive の内部規約（0〜1）なので衝突しない |
| 再取り込みでデザイナーの編集を消さない | **一致**（A-4。D-Drive の再取り込みは自分が作るトラック（名前と型で照合）だけ更新） |
| 配布は git URL + タグ、版は SemVer | **一致**（[42] §3）。D-Drive は `com.ddrive.core` で git URL + タグ（`v1.3.1` 等）。T-Drive の `versionDefines` は `[1.4.0,)` で FC の拡張点の有無を判定できる |

### 6.1 対にした表: T-Drive が従う D-Drive の決まり / D-Drive が守る T-Drive の決まり

左は T-Drive が従う D-Drive の決まり（上の表）。右は **D-Drive 側が守る / 確認する T-Drive の決まり**（(a)〜(d) は f27702e 由来・まとめ役の抽出。D-Drive への明示的な要望ではない）。

| T-Drive が従う D-Drive の決まり | D-Drive が守る T-Drive の決まり（D-Drive 側の欄） |
|---|---|
| `DDrive.*` 名前空間を使わない・パッケージを改変しない | D-Drive は `TDrive.*` を参照しない。受け口は汎用の拡張点（§2） |
| 例外で止めない（警告 + no-op） | D-Drive の外部呼び出し（FC-4・FC-5・FC-12・FC-14）は `try/catch` で隔離 |
| データは実行時に読み取り専用 | D-Drive の Editor が外部データ（`FacialCorrectionData` 等）を書き換えない |
| `Instantiate` / `Resources.Load` を直接使わない | （対称なし） |
| カットシーンのファイル名 `<Shot>__<ModelIdentifier>` | D-Drive の `CutsceneShotParser` / 取り込みが `.fctrack` のような追加ファイルを壊さない（A-4・E-4・E-7） |
| 単位・軸: cm / Y-up / FBX は自動単位、フレームレート 30 or 60 | **(c) 座標系**: Unity は m / Y-up / 左手 / 前 +Z。計算は正準空間（UE 準拠）で、変換は T-Drive の `space` だけが持つ。**D-Drive は座標変換を足さない**（FC-3 は Unity のワールド値を無変換で返す） |
| ブレンドシェイプは名前で引く・重みは 0〜100 | **(a) シェイプ名**: `FC_<asset>_<layer>_R{row}_C{col}`（+ `_Ex`）・`FC_<asset>_Persp_K{n}` は T-Drive が所有、`fcs_` は彫り用で Unity へ出さない。照合は大文字小文字を区別する完全一致。規則の変更は T-Drive 側で MAJOR。**D-Drive は名前を加工せず、`FC_` / `fcs_` を外部所有の接頭辞として予約（FC-20）し、`AnimData.BlendShapes` が指すと Validator が警告・エディタで既定非表示、返却時は全シェイプを既定へ戻す（FC-2）** |
| （ボーン名の完全一致） | **(b) ボーン名**: 名前は完全一致（`baseBone` 既定 `head`、`BoneOffset` は親ボーン空間）。Runner は頭のボーンの Transform とメッシュの `SkinnedMeshRenderer` を名前で引く。**D-Drive はボーンを消さず・改名せず・スケールを入れない**（確認済み: FC-20 の項目 3〜5。FC-10 E-17 で固定） |
| 再取り込みでデザイナーの編集を消さない | D-Drive の再取り込みは自分が作るトラック・Binding だけ更新（A-4）。FC-15 でマテリアルのシェーダーも保つ |
| （Runner の書き込み先・順序） | **(d) Runner は LateUpdate で `FC_*` だけを書く**。表情での弱め（`expressionDampen`）は他のシェイプの重みを**読む**。D-Drive の Update（`AnimManager.Tick` → `AnimatorProxy`）は `AnimData` の指定外のシェイプに触らない（A-8・E-8・E-16） |
| 配布は git URL + タグ、版は SemVer | 一致（FC は v1.4.0 MINOR 見込み） |

## 7. T-Drive へ返す事項

### 7.1 Facial（T-Drive の FT-4 に対する回答として使える短いまとめ。出典 `97bed77`）

1. **方針は受け入れ済み**（2026-10-03）: Facial は T-Drive 版が正。D-Drive の旧 7-8 は書き換え、D 群（一級の種別化）は不採用。D-Drive 側のチケットは `docs/11_tasks.md`「FC チケット」の FC-0〜FC-10、設計は本書（`docs/51_tdrive_integration.md`）
2. **入れる順と版**: FC-1（同じモデルへのバインド）→ FC-2 + FC-12（プール返却で BlendShape の重みを戻す + スポーン / 返却の通知。同一 PR）→ FC-11（パス無効化・キーワード）→ **FC-20（`FC_` / `fcs_` 接頭辞の予約と取り込み確認）**→ FC-10（外部拡張の契約テスト）→ FC-15 → FC-5（取り込み完了のリスナー）→ FC-4（外部マーカー）→ FC-6 → FC-14 → FC-3（現在の視点 API）→ FC-7 → FC-19。保留 = FC-8・FC-9・FC-13、後回し = FC-16・17・18。リリースは **v1.4.0（MINOR）**を見込む。**日付の約束はしない**。T-Drive は < 1.4.0 の D-Drive でも動く回避策を残してよい
3. **ブリッジが切り替えるべき点**（`Bridges.DDrive`、`com.ddrive.core` の `versionDefines` で `[1.4.0,)` のときだけ新経路）:
   - バインド: 同名の AnimationTrack のバインド先を引く回避策 → `CutsceneBinding{ Target = SameAsTrack, SourceTrackName = "<役名>" }`（FC-1）
   - fctrack の取り込み: `AssetPostprocessor` の `postprocessOrder` → `ICutsceneImportListener`（FC-5）。**`postprocessOrder` では順序制御できない**（D-Drive の取り込みは `EditorApplication.delayCall`。R-1）。**1.4.0 以降は `postprocessOrder` ではなく `ICutsceneImportListener` を使う**（FC-5 実装済み、2026-10-03。`ICutsceneImportListener` を 1 型実装し、`OnCutsceneShotImported(result)` で `result.Roles`（役名 → トラック・元 FBX パス）と `SourcePath` の隣の `.fctrack` を見て `result.Timeline` に Facial トラックを足し、`result.Data.Bindings` に `SameAsTrack` の binding を追記する。**保存は呼ばなくてよい**〔全リスナーの後に D-Drive が 1 回保存〕。**再取り込みでも毎回呼ばれ、足したトラック / binding は保持される**ので、足す前に `result.Timeline.GetOutputTracks()` の名前と `result.Data.Bindings` の `TrackName` で既にあるか確認する。`.fctrack` が FBX より後に取り込まれた場合は従来どおり `CutsceneImportService.ComputeCutsceneDataPath` で引く。< 1.4.0 の D-Drive では `Bridges.DDrive` が従来の回避策に落ちる）
   - 視点: `Camera.main` → `ViewCamera.TryGetCurrent`（FC-3。**今の D-Drive でも Runner（実行順 10000）なら `Camera.main` でカット姿勢が取れる**。R-2）
     - **FC-3 実装済み（2026-10-03）— T-Drive の FT-4 / FU-3 宛て**: `Bridges.DDrive`（`[1.4.0,)`）が `ViewCamera.TryGetCurrent(subject, out ViewPose)` を視点解決の**最後のフォールバック**（doc15 §5.3 の「視点の指定（Transform）」が最優先、その次に手動の角度、最後にこれ。`FacialViewResolver.Provider` 等のフック）に設定する。`TDrive.Facial.Runtime` は D-Drive を参照しない。返る値は Unity のワールド（m / Y-up / 左手）の位置・回転と縦画角（度）で、`space` の変換は T-Drive 側。**Runner の `LateUpdate` の実行順は `DDriveCutsceneCameraApplier.ExecutionOrder`（1000）より後**にする（10000 なら満たす。それより前だとカットシーン中は 1 フレーム前の姿勢）。カメラが無いときは `false`（警告なし）→ 補正をスキップ。分割画面などで `Camera.main` が視点の 1 つでしかないときは、ゲーム側が `ViewCamera.Register(IViewProvider, priority)` で `subject` ごとのカメラを返す。
   - プール返却: Runner の `OnDisable` で戻す方針は変えなくてよい。FC-2 で `AnimData.BlendShapes` が書いた通常シェイプも D-Drive 側で戻る
4. **T-Drive 側の設計に影響する相違（今すぐ直す価値がある）**:
   - **R-3**: `Cutscene.Pause`（インスタンスの一時停止、および `PauseWithGame` のグローバル Pause）中は Timeline が**評価されない**。「毎フレーム `PushOverride`、次フレームで消える」設計だと一時停止中に補正がデフォルトへ戻る → 前回値を保持し、Mixer の `OnPlayableDestroy` / グラフ停止で解除する
   - **R-4**: `IValidator` の入口は `AssetDataBase`（`CutsceneData` = `AssetType.Cutscene`、`ModelData` = `AssetType.Model`）。`FacialCorrectionData` は入口にならない
   - **R-6**: Runner は `IPoolable` を**実装しない**（`OnDisable` で戻す）。ルートの `IPoolable` は最初の 1 個にしか `OnReturn` が呼ばれない
   - A-6: `IValidator` の実装は **public で引数なしのコンストラクタ**が必須。`DDrive.Tests*` 名のアセンブリは発見されない
5. **回避策が不要になる点**: FC-1（役名のトラックを引く）・FC-5（`postprocessOrder`・同名ショット探し）が入ると、doc15 §5.5「バインドの補助」「fctrack の取り込み」の回避策が不要になる。FC-2 が入ると「プール返却の確認テスト」は D-Drive 側（FC-10）が持つ
6. **FC-8（BlendShape カーブ）/ FC-9（デバッグ・調整）は保留**: 表情アニメの運用が決まったら / 7-3・7-4 着手時に再相談
7. **FC-4 実装済み（2026-10-03）**: Facial のイベント的な切り替えをマーカーで置きたくなったら、`Bridges.DDrive` 側で `FacialMarker : Marker, ICutsceneMarker`（`DDrive.Runtime.Cutscene`、`[1.4.0,)`）を実装すれば `Fire(in CutsceneMarkerContext)` が跨いだ Tick で 1 回呼ばれる（Seek / Skip / Late Join は無音、Edit Mode は `IsEditPreview = true`）。発火は各クライアントのローカル処理で、全員で同じ結果にしたい処理は T-Drive 側で同期する。例外は D-Drive が隔離する。
8. **FC-10（契約）**: A-1〜A-9 は D-Drive が壊さない契約としてテストで固定する。T-Drive が新しく D-Drive の挙動に依存したくなったときは D-Drive に連絡（契約テストに足す）
9. **FC-6 実装済み（2026-10-03）**: `SourceAssets/Facial/` を「不明な種別フォルダ」扱いさせない回避策（Facial のデータを `SourceAssets/Cutscene/` か GameData 外に置く）は不要になる。`Bridges.DDrive.Editor`（`[1.4.0,)`）に `IImportRuleFolderOptOut` を 1 型実装し、`FolderNames` で `"Facial"` を返す（public・引数なしコンストラクタ。`TypeCache` で自動発見、登録コード不要）。ハンドラは持たなくてよい（T-Drive は D-Drive の `AssetDataBase` を作らない）。`IImportRuleHandler` の外部実装も可能だが、作れる Data は D-Drive の既存 `AssetType` に限る。

### 7.2 Toon マテリアル（T-Drive の U-21 宛て。出典 `4d44a32`）

1. **方針は受け入れ済み**（2026-10-03）: doc17 §5 の M-1〜M-9 を D-Drive の **FC-11〜FC-19** として起票（doc17 の「M-n」は D-Drive の既存 M チケットと番号が衝突するので、D-Drive 側は「doc17 M-n」と書く。対応表は [11](11_tasks.md) FC 節の冒頭）。優先と順序は 7.1 の 2。FC-13（インスタンスごとの値）は T-Drive が Q-7 の方式（StructuredBuffer / MPB）を決めてからの着手
2. **doc17 の裏取り（§3.3・§3.4）はほぼ一致**。相違は 4 件: R-8 `_ZTest` 等は実行時には書かれるがツールが衝突として警告・除去する / **R-9 単体 `.mat` 経路（`UnityMaterialMigrator.Migrate`）は既存 MaterialData のシェーダーも Lit に上書きする（doc17 の罠 1 より広い。FC-15 で解消）** / R-10 `IAssetBehaviour` は未配線で、`ImportRule` ハンドラも外から足せない（FC-12 は Prefab 側のインターフェースにした）/ R-11 罠 2・3 の対象パスは文字列の部分一致で、`SourceAssets` の場所を変えた持ち込み先（MS2026）ではそもそも当たらない可能性がある（MS2026 の Profile の値を確認してほしい）
3. **ブリッジ（`TDrive.Toon.DDriveBridge`）が切り替えるべき点**（`com.ddrive.core` の `versionDefines` `[1.4.0,)` のときだけ新経路）: パスの無効化・キーワードは生成シェーダーを増やさず MaterialData の `DisabledPasses` / `EnabledKeywords`（FC-11）/ スロット適用後の初期化は `IModelInstanceListener`（FC-12。`ToonCharacter` が実装）/ 変換表・テクスチャ規則は提供口（FC-14。`Assets/` への生成と Profile の編集が不要）/ 罠 1・3 は FC-15 で緩和
4. **それまでの運用は doc17 §4 のとおりで足りる**。罠ごとの対応は §3.4 の表。**空・無効 ID のスロットが Prefab のマテリアルを触らない挙動は FC-10 E-13 で契約として固定する**ので、回避策（スロットを空にする）に依存してよい
5. **D-Drive が Renderer Feature に関与しない**こと・`_Toon*` が Specific にそのまま入ること・シェーダーに無いプロパティが飛ばされることも契約テスト（E-10〜E-15）で固定する
6. **T-Drive を使うプロジェクトは `MayaImportProfile` の `UnknownShaderPolicy = KeepSource` を設定する**（FC-15、D-Drive 1.4.0 以降。罠 1・3 が確認ダイアログ無しで避けられる。既定の `Ask` は Model エディタの「元ファイルを再読み込み」などの対話的な操作で確認ダイアログを出し、FBX の自動取り込みは従来どおり Lit に変換する）。T-Drive の検証 U-23 で案内できる
7. **FC-14 実装済み（2026-10-03）— 罠 2・罠 4 の回避策が不要になる**（`com.ddrive.core` `[1.4.0,)`）: (a) **罠 4（変換表の置き場所）**: `Bridges.DDrive.Editor` / `TDrive.Toon.DDriveBridge` に `IShaderConversionTableProvider` を 1 型実装し、`GetTables()` でパッケージ内の `ShaderConversionTable` を返す（`AssetDatabase.LoadAssetAtPath` 等。public・引数なしコンストラクタ。自動発見）。`Assets/` に生成しなくてよい。同じ `From → To` の表が複数あるときの優先順位は **`Assets/` の表 > 外部提供口の表 > D-Drive 同梱の表**。(b) **罠 2（`T_` のテクスチャが sRGB オン）**: `ITextureImportRuleProvider` を 1 型実装し、`GetRules()` で `TextureImportProfile.Rule`（例 `Match = Suffix, Pattern = "_ToonMask", Type = Default, SRgb = false, Mipmaps = true, Compression = CompressedHQ`）を返す。**Profile の `Rules` の前（`T_` 接頭辞の規則より先）に評価される**。プロジェクトが Profile に同じ条件（Match の種類 + Pattern）の規則を足すと Profile が優先される。対象パスは従来どおり `TextureImportProfile.IncludePathContains`（R-11。持ち込み先の `SourceAssetsRoot` が既定と違うなら Profile 側の確認が要る）。`GetRules()` はドメインリロードごとに 1 回しか呼ばれないので、規則を動的に変えたい場合は再コンパイル / ドメインリロードが要る。< 1.4.0 では従来どおり（Profile に規則を足す / `Assets/` に表を生成）。

## 8. 未決事項と決定（まとめ役の判断が要るもの）

「決定」列は 2026-10-03 のユーザー決定（実装時に記録）。「未決」のものはまだ決まっていない。

| # | 論点 | 選択肢 | 推奨 | 決定 |
|---|---|---|---|---|
| U-1 | FC-1: 解決結果をトラックの `TrackBindingType` に合わせて変換するか | (a) 既存どおり Animator / Transform の 2 択（初版）/ (b) `TrackBindingTypeAttribute` を見て `GetComponent` で適合 | (a)。Facial は Animator で足りる。独自バインド型が出てから (b) | **(a)** 変換しない（FC-1 実装時に確定） |
| U-2 | FC-1: Validator の重さ | (a) Warning（[42] §5.8 どおり）/ (b) 最初から Error（新しい Target 値は既存データに現れないため実害は無い） | (a)。次の MINOR で Error に昇格 | **(a)** Warning（FC-1 実装時に確定） |
| U-3 | FC-2: `PoolService.ForceReturn` を全 `IPoolable` に `OnReturn` を呼ぶよう直すか（R-6） | (a) FC-2 に含める（`GetComponents` を共有バッファで。割り当てなし）/ (b) 直さず「ルートで `IPoolable` を実装しない」を契約にして E-1 で固定 | (a)。Foundation の挙動追加（MINOR）だが事故の元を断てる。ただし Foundation の変更なのでユーザー確認 | **(a)**（2026-10-03 ユーザー決定。FC-2 で実装） |
| U-4 | FC-3: 名前空間と型名（`DDrive.Runtime.Viewing` / `ViewCamera` / `IViewProvider` / `ViewPose` / `ViewSource`） | 上記のまま / 別名 | 上記。互換面に入るので PR 前にユーザー確認 | **提案の名前で確定**（`DDrive.Runtime.Viewing` / `ViewCamera` / `IViewProvider` / `ViewPose` / `ViewSource`。2026-10-03 ユーザー決定。**FC-3 で実装済み（同日）**） |
| U-5 | HTML マニュアルの置き場所（Cutscene を扱う既存ページが `cutscene-maya-export.html` のみ。ProgrammerManual に Cutscene のページが無い） | (a) 実装時に既存ページへ追記 / (b) Cutscene の新ページ（デザイナー・プログラマー）を作る | (a)。必要になれば (b)。未実装の機能は載せない | **(a)** |
| U-6 | FC-5 のリスナー API を Editor 契約（[42] §5.9）に載せるか | (a) 載せる（`EditorContractSnapshotTests` 対象）/ (b) 載せない（`DDrive.Editor` の public は互換面外のまま） | (a)。T-Drive が事実上依存するため | **(a)**（U-7 に伴う） |
| U-7 | FC-10: [42] に「外部拡張の契約」を追加するか | (a) 追加する（§5.14 新設。MINOR）/ (b) テストだけ持ち docs/42 は変えない | (a)。ポリシーの追加なのでユーザー承認の上で FC-10 の PR で実施 | **(a)**（2026-10-03 ユーザー決定） |
| U-8 | R-3 の対応主体 | (a) T-Drive 側で前回値を保持（推奨）/ (b) D-Drive が一時停止中も毎フレーム `Evaluate` する（`Paused` 中の Tick の意味が変わる = 他の外部 Track にも影響する大きな変更） | (a)。D-Drive は変えない | **(a)** |
| U-9 | FC-15: `UnknownShaderPolicy` の既定値と既存挙動の変更 | (a) 既定 `KeepSource`（知らないシェーダーはそのまま。既存の持ち込み先でも次回の再取り込みから挙動が変わる）/ (b) 既定 `ConvertToLit`（従来どおり。T-Drive は Profile で `KeepSource` を設定）/ (c) 確認ダイアログ | (a)。Editor の挙動変更なので CHANGELOG に明記（弱い互換面）。ユーザー確認の上で実施 | **(c) 確認ダイアログ**（2026-10-03 ユーザー決定。ダイアログを出せない自動取り込みの経路は従来どおり）。具体化: `MayaImportProfile.UnknownShaderPolicy` = `Ask`（0、既定）/ `KeepSource`（1）/ `ConvertToLit`（2）。`Ask` は対話的な操作だけ 1 操作 1 回の 3 択ダイアログ（保つ / 変換 / キャンセル）、非対話は従来どおり Lit。`KeepSource` は確認なしで保つ（T-Drive を使うプロジェクト向けの opt-in）。FC-15 で実装済み（§4.16 実装メモ） |
| U-10 | FC-11: キーワード欄の範囲と検査の重さ | (a) `EnabledKeywords` のみ（無効化は需要が出てから）/ (b) `DisabledKeywords` も同時に。キーワードの未宣言検査はグローバルキーワードで偽陽性があり得るので Info にするか | (a)。検査は Warning（パス名）+ Info（キーワード）の併用 | **(a)**（2026-10-03 FC-11 で実装） |
| U-11 | FC-14: 外部のテクスチャ規則の評価位置 | (a) Profile の `Rules` の前（`T_` より前に効く）/ (b) 後ろ | (a)。接尾辞が名前空間付き（`_ToonMask`）の運用で、Profile の上書きを抑える | **(a)**（2026-10-03、FC-14 で実装。Profile に同じ条件の規則があれば Profile 優先 = §4.15 実装メモ） |
| U-12 | FC-12: 通知の形 | (a) Prefab 上のインターフェース（生成時キャッシュ）/ (b) 静的イベント / (c) `IAssetBehaviour` の配線 | (a)（§4.13） | **(a)**（FC-12 で実装） |
| U-13 | FC-20: 所有接頭辞の登録口と実行時の弾き | (a) 一覧のみ（既定 `FC_`・`fcs_`）+ Validator Warning / (b) + `Register(prefix)` / (c) + 実行時に `FC_*` を弾く（警告 + no-op） | (a)。必要になったら (b)。(c) は挙動の追加なので採らない | **(a)** |
| U-14 | FC-13: `SetMaterial` でマテリアルを差し替えたときの MPB の値 | (a) 維持 / (b) クリア | 着手時に決める（T-Drive の方式決定後） | 未決 |
| U-15 | FC-10 E-17: ボーン・シェイプ名を持つ FBX フィクスチャの用意 | (a) `DevRepoOnly`（開発リポジトリの UnityChan サンプル等を使う。シェイプ名の確認が弱い）/ (b) 合成の小さな FBX（ASCII）をテスト用に用意 / (c) T-Drive が合成の小 FBX を提供（shizuku は規約上コミット不可） | (b)。難しければ (a) + 静的確認 | **(b)**（2026-10-03、FC-10 で実装。手書きの合成 ASCII FBX が 1 回で読めた。実 FBX は [52] §22） |

## 9. 変更履歴

- 2026-10-03: 新規作成（設計のみ。コード・アセット・`.meta` の変更なし）。T-Drive の doc14/15/16（出典 `97bed77`）を受けて、`Packages/com.ddrive.core/` の該当コードを grep して読み、A-1〜A-9 の裏取り・相違 R-1〜R-7・FC-1〜FC-10 の設計・D 群の不採用・T-Drive への回答をまとめた。Unity 未起動のためコンパイル・テストは未実施
- 2026-10-03（同日追記 1）: T-Drive コミット `4d44a32` の doc17（Toon マテリアル）を受けて、D-Drive 側推奨変更 doc17 M-1〜M-9 を FC-11〜FC-19 として起票（ユーザー指示）。節の題と本書を T-Drive 連携全体に広げ、ファイル名を `51_tdrive_facial_integration.md` から `51_tdrive_integration.md` に改名。doc17 §2〜§4 を実コードで裏取りし（§3.3・§3.4）、相違 R-8〜R-11 と罠ごとの対応表を追加。FC-10 に E-10〜E-15 を追加。
- 2026-10-03（同日追記 2）: T-Drive コミット `f27702e`（FacialController のコア F0-2 / F0-3）が前提にする決まり（シェイプ名・ボーン名・座標系・Runner の書き込み）のうち D-Drive 側が守る / 確認すべきものを「f27702e 由来・まとめ役の抽出」として FC-20 に起票し、FC-2・FC-3・FC-10（E-16〜E-18）に追記。§3.5・§6.1 に対にした表を追加。
- 2026-10-03（同日追記 3）: FC-2 / FC-12 を実装（§4.3 実装メモ）。§8 に「決定」列を追加（U-1〜U-13 の決定を記録。U-14・U-15 は未決）。人による確認の手順書 [52](52_manual_verification_fc.md) を新設。
- 2026-10-03（同日追記 4）: FC-11 を実装（§4.12 実装メモ。実機で「未確認」だった `SetShaderPassEnabled` の挙動・`new Material(from)` / `Lerp` の扱い・パス列挙の API を確認）。U-10 を決定列に記録。
- 2026-10-03（同日追記 5）: FC-20 を実装（§4.21 実装メモ。確認項目 1〜6 を再確認、`ExternalBlendShapePrefixes` + Validator Warning + AnimEditor の表示）。
- 2026-10-03（同日追記 6）: FC-10 を実装（§4.11 実装メモ。契約テスト `ExternalContract*` を EditMode 16 件 + PlayMode 14 件、[42] §5.14 を新設〔U-7 = (a)〕、合成 FBX フィクスチャ〔U-15 = (b)〕）。U-15 を決定列に記録。
- 2026-10-03（同日追記 8）: FC-5 を実装（§4.6 実装メモ。U-6 = (a) で Editor 契約に掲載、保存はリスナー後に 1 回、`Result` に追加項目なし）。§7.1 に T-Drive 向けの案内（1.4.0 以降は `ICutsceneImportListener`）を追記。
- 2026-10-03（同日追記 9）: FC-4 を実装（§4.5 実装メモ。`CutsceneMarkerContext` は `Context` を渡さず `Handle` を足した最小の 5 欄、Edit Mode のカーソルは public を増やさず Editor asm 内 internal、契約 E-20）。§7.1 に案内を追記。
- 2026-10-03（同日追記 10）: FC-3 を実装（§4.4 実装メモ。`DDrive.Runtime.Viewing` の 4 型、カットシーン所有の判定は Applier に読み取り専用 `IsDriving` を 1 行足しただけ、公開は §4.4 の署名 + `ViewPose` の public コンストラクタ。契約 E-18。既存の `Camera.main` 直参照は置き換えず）。§7.1 に FT-4 / FU-3 宛ての案内を追記。
- 2026-10-03（同日追記 7）: FC-15 を実装（§4.16 実装メモ。**推奨案の既定 `KeepSource` は不採用、U-9 = (c) 確認ダイアログ + `MayaImportProfile.UnknownShaderPolicy`**）。U-9 の決定列を具体化、§7.2 に T-Drive 向けの案内を追記。
