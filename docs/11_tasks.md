# 11. タスク分割（フェーズ + チケット粒度）

粒度: 1 チケット = 1〜3 人日。`[基盤]`=基盤プログラマ / `[TA]`=テクニカルアーティスト / `[ED]`=エディタ担当。
依存: 各チケットの `←` は先行チケット。受け入れ条件（AC）はレビュー時に検証（[12_review.md](12_review.md)）。

---

## Phase 0: 基盤 (M0 — 動く骨格)  約 4.5 週

| # | チケット | 担当 | 日数 | 依存 | 受け入れ条件 |
|---|---|---|---|---|---|
| 0-1 | asmdef 構成・パッケージ雛形・CI 空ジョブ | 基盤 | 1 | — | Runtime が Editor 非依存でビルド通過 |
| 0-2 | AssetDataBase / AssetFlags / AssetRef / AnchorDef 定義 | 基盤 | 2 | 0-1 | 派生 SO を作れる。シリアライズ往復テスト green |
| 0-3 | AssetId 体系 + ID 定数ジェネレータ | 基盤 | 3 | 0-2 | 再生成が冪等。リネームで ID 不変。重複検出 |
| 0-4 | AssetRegistry + Catalog（Lazy 解決） | 基盤 | 3 | 0-3 | 未登録 ID→Placeholder + 警告 1 回。O(1) 解決。Placeholder 発生を記録するフックを持つ（7-1 の下地） |
| 0-5 | AssetLoader（Addressables + 参照カウント） | 基盤 | 2 | 0-1 | 多重ロード防止・Release テスト green |
| 0-6 | PoolService（Rent/Return/Prewarm/上限回収） | 基盤 | 2 | 0-1 | PlayMode テスト green。上限時 Priority 回収 |
| 0-7 | Handle 基盤（世代式 struct、無効時 no-op） | 基盤 | 2 | 0-4 | 破棄後アクセスで例外なし・警告のみ。無効アクセスを記録するフックを持つ（7-3 の下地） |
| 0-8 | EventBus + AssetEvent（Trigger/Action/Dispatch） | 基盤 | 3 | 0-4 | Frame/Time/Custom 発火のユニットテスト green |
| 0-9 | PauseService / TimeService / Duck チャンネル | 基盤 | 2 | 0-1 | スタック式多重 Push/Pop テスト green |
| 0-10 | IAssetManager 共通化 + GameLoop(Tick) 駆動 | 基盤 | 1 | 0-7 | — |
| 0-11 | Validation Core（IValidator 登録制 + CI 実行） | 基盤 | 2 | 0-2 | batchmode で exit code 反映。JUnit XML 出力 |
| 0-12 | ネット前提の注入点（INetBridge+Loopback / ITimeSource / NetMode フラグ / Seed 決定的乱数） | 基盤 | 2 | 0-2 | Loopback でシングルプレイが完全動作。Time.time 直接参照ゼロ（Analyzer 検出） |
| 0-13 | EasingCore（31 種 + Bezier + Curve、既存実装の Runtime 昇格）+ SplinePath（4 種、弧長等速化） | 基盤 | 3 | 0-1 | 全 Ease の参照値テスト green。Evaluate が等速（誤差 1% 以内） |
| 0-14 | 起動配線（`DDriveRuntimeBootstrap` = Composition Root）+ カタログ ⇔ Addressables 同期 | 基盤 | 2 | 0-4, 0-5, 0-10 | ✅ 2026-09-09（レビュー P0-1/P0-2 対応、[02] §14 / §5）。`Generate/起動オブジェクトをシーンに配置`、`Generate/Addressables 登録を同期`、`AddressablesRegistrationValidator`。`RuntimeBootstrapTests` / `AddressablesRegistrationValidatorTests` |
| 0-14 | NGO アダプタ（INetBridge 実装 + NetworkTime 同期） | 基盤 | 3 | 0-12 | Loopback と差し替えて 2 クライアントテストシーンが動く。**2026-09-08 改修**: NGO 2.13.2 に統一（MS2026 と同一）、`[ClientRpc]`→`[Rpc(SendTo.ClientsAndHost)]`（旧属性はホスト自身に届かない）、Client 発 Cosmetic の Host 中継 + レート制限、Unreliable 配送対応、`[Net/Host]` ログ（[14] §2/§12）。⚠ 実機 2 クライアントでの再確認が必要 |
| 0-15 | ValueDef / TimeDef / ValueDef3 / ValueDefColor 定義 + Evaluate | 基盤 | 2 | 0-13 | 全モードの参照値テスト green。定常経路 0 alloc・純関数 |
| 0-16 | ValueDef Validator（共通検査一式） | 基盤 | 2 | 0-15, 0-11 | [17] §6 の全検査 |

**マルチプレイは v1 必須要件**（[14_networking.md](14_networking.md) §11）。以降の各 Phase に通信対応チケットを含み、マイルストーンデモは 2 クライアント + サーバー構成で実施する。

**マイルストーン M0 デモ**: 未登録 ID を Play して Placeholder が出るモックシーン。

## Phase 1: Audio + AssetBrowser 最小版 (M1 — 最初の縦切り)  約 3.5 週

| # | チケット | 担当 | 日数 | 依存 | 受け入れ条件 |
|---|---|---|---|---|---|
| 1-1 | SeData/BgmData + AudioManager（Play/Stop/上限/Pool） | 基盤 | 3 | 0-* | SE 32 同時・上限超過で Priority 停止 |
| 1-2 | BGM クロスフェード / Intro→Loop サンプル精度 | 基盤 | 2 | 1-1 | ループ継ぎ目にノイズなし |
| 1-3 | 3D サウンド（AnchorDef / 位置優先規則 / Spread / Doppler / 減衰） | 基盤 | 3 | 1-1, 0-2 | ボーン追従・引数上書き・DetachOnStop 動作 |
| 1-4 | Duck 統合（AssetEvent Action=Duck） | 基盤 | 1 | 1-1, 0-9 | イベントから Duck/Pop 動作 |
| 1-5 | AssetBrowser 骨格（一覧/検索/新規/D&D 登録/自動命名） | ED | 4 | 0-4 | 1000 件で 60fps。D&D で種別自動判定。新規作成は意味情報入力のみでファイル名・ID・カタログ登録を自動生成（[10] §3） |
| 1-6 | PreviewService（プレビューシーン + 実 Manager 駆動 + 共通 UI） | ED | 4 | 0-10 | EditMode で SE/ループ/速度変更が動く |
| 1-7 | AudioEditor（波形/ループ範囲/3D 距離/試聴） | ED | 3 | 1-5, 1-6 | 設計書 03 §5 の全機能 |
| 1-8 | ID PropertyDrawer（検索ドロップダウン） | ED | 2 | 0-3 | 未登録 ID 赤表示 |
| 1-9 | Audio Validator 一式 | 基盤 | 1 | 0-11 | 設計書 03 §7 の全検査 |
| 1-10 | ValueDef 共通 PropertyDrawer（3 タブ + ミニグラフ + スクラブ） | ED | 3 | 0-15, 1-6 | モード切替でデータ保持。Undo 対応。以降の全エディタで共用 |
| 1-11 | SeEmitter（シーン配置型環境音） | 基盤 | 1 | 1-3 | 配置だけでループ環境音が鳴る・止まる |

**M1 デモ**: デザイナーが SE/BGM を登録→試聴→ゲームで再生、の一連が通る。

## Phase 2: VFX + Model (M2)  約 3.5 週 — 実装完了(2026-07-27、レビュー待ち)

| # | チケット | 担当 | 日数 | 依存 | AC |
|---|---|---|---|---|---|
| 2-1 | VfxData/AnchorDef/VfxParam + VfxManager | 基盤 | 3 | 0-* | ✅ Spawn/Stop/Kill/Attach/Pool 動作。対応は ParticleSystem のみ(下記注) |
| 2-2 | VfxParam 反映（MPB / VFXGraph、ID キャッシュ） | 基盤 | 2 | 2-1 | ✅ SetParam は 2-1 に統合実装。MaterialPropertyBlock + Shader.PropertyToID キャッシュ。VFXGraph 未対応(下記注) |
| 2-3 | UI パーティクル（UI カメラ + RectTransform 変換） | 基盤 | 3 | 2-1 | ✅ 座標変換は既存の Anchor(ContextTarget)機構がそのまま吸収。`VfxUiSetup`(Tools > D-Drive > Generate)で VfxUI レイヤー確保 + URP Overlay カメラのスタック追加を行うオプトインツールを用意 |
| 2-4 | VfxEditor（Anchor ギズモ編集/複数同時/環境切替） | ED | 4 | 1-6, 2-1 | ✅ ライブプレビュー/複数同時再生(最大8)/環境切替(背景色・ライト強度・確認用モデル)/パラメータ即時反映/イベント編集/UIモード注記。Anchor 編集は 3D ギズモではなく 2D パッド(AudioEditor の 3D確認パッドと同じ手法)+ 数値フィールドで実装 |
| 2-5 | ModelData/Models Manager（Spawn/Slot 差し替え） | 基盤 | 2 | 0-* | ⚠ Spawn/Despawn/SetLayer/Slot自動収集は動作。SetMaterial は 3-5（2026-09-10）で `MaterialManager` に接続し、実際に差し替わるようになった |
| 2-6 | ModelEditor（ターンテーブル/並列/Slot 差し替え） | ED | 2 | 1-6, 2-5 | ✅ ターンテーブル自動回転/複数モデル並列表示(最大4)/背景・ライト切替/Slot自動収集+PropertyField編集。Skybox切替は見送り(下記注) |
| 2-7 | VFX/Model Validator | 基盤 | 1 | 0-11 | ✅ 04§7, 05§A-4 の全検査(Anchor.BoneName プレビュー検査は SE と同様に対象外) |
| 2-8 | VFX/SE の Cosmetic 配送（Broadcast + Unreliable バッチ + 受信再生） | 基盤 | 3 | 0-14, 2-1, 1-1 | ⚠ 同一 INetBridge を共有する複数 Manager インスタンスで実証(EditMode)。**位置(Position)のみの配送**で、anchorNetId によるライブ追従・paramOverrides 同期は未実装(下記注) |
| 2-9 | AnchorPoint/AnchorRig（シーン配置型アンカー）+ プレビュー駆動修正 | 基盤+ED | 追加 | 2-1, 2-4 | ✅ 2026-07-28 追加実装([04] §2.5)。①EditMode プレビューのラグ/フレーム抜け修正(手動 Simulate + 再生中 Repaint) ②AnchorPoint(SpawnOffset/位置・回転・スケールのランダム/ギズモ) ③AnchorRig + `Generate/Anchor プレハブを生成` ④ボーン一覧に ★AnchorPoint 優先表示。当たり判定等への再利用は通常の GameObject としてそのまま可能 |
| 2-10 | VfxEditor の SceneView プレビュー化 + AnchorPoint ギズモ視認性修正 | ED | 追加 | 2-4, 2-9 | ✅ 2026-07-28 追加実装([04] §5 改定)。独自ビューポート/環境切替を廃止し、開いているシーンへ直接スポーン(HideFlags.DontSave、ウィンドウを閉じると自動破棄)。確認専用シーンを開いて実ライティング/ポストプロセスの下で調整する方針へ。AnchorPoint ギズモはズーム追従サイズの塗りつぶし球に変更(旧: 5cm ワイヤ球は視認不能) |
| 2-11 | URP/HDRP シェーダー互換性チェック + 既定パーティクルマテリアル生成 | 基盤+ED | 追加 | 2-7 | ✅ 2026-07-28 追加実装([04] §7)。`ShaderPipelineAnalyzer` が SubShader の RenderPipeline タグから互換性を機械判定し、VfxDataValidator/ModelDataValidator が Error 検出。`Generate/デフォルトパーティクルマテリアルを生成` で URP 対応の既定マテリアルを用意可能(Built-in RP 用マテリアルを URP プロジェクトで使うと Scene/Game ビューで透明になる不具合の再発防止) |

**実装時に判明したスコープ調整(仕様書側にも反映済み):**
| 2-13 | VfxEditor のプレハブモード（Prefab Stage）内再生 | ED | 追加 | 2-10 | ✅ 2026-09-08 追加実装（[04] §5）。`SceneVfxPreviewDriver` が対象 Prefab 自身のステージではその場再生（二重表示しない）、他のステージではステージのシーンへスポーン。EditMode の OneShot 終了判定（リピートの前提）も修正。テスト 7 件追加 |
| 2-14 | Anchor 仕様改定（アセット化・入れ子・Audio 共通・生成時イベント） | 基盤+ED | 12 | 2-1, 1-3 | ✅ 2026-09-08 実装（[21_anchor_spec.md](21_anchor_spec.md)）。`AssetType.Anchor` / `AnchorData` / `AnchorChain`（連鎖合成・ディレイ・確率・ランダム）/ `AnchorDataValidator` / VfxManager・AudioManager の AnchorId 優先順位と Pending / ファサード / `AnchorEditorWindow` / `AnchorSceneHandles` 共通化 / `AnchorAssetFactory`（Transform・AnchorRig・埋め込みからの生成）/ VfxEditor・SE Inspector の導線。テスト: PlayMode 22 件 + EditMode 6 件追加（EditMode 122 / PlayMode 258 green） |
| 2-15 | 配置セット（AnchorGroup）: パターン生成・点ごとのアセット・入れ子・一括再生・専用エディタ | 基盤+ED | 7 | 2-14 | ✅ 2026-09-08 実装（[22_anchor_group.md](22_anchor_group.md)）。PlayMode テスト 16 件追加（274 green）|
| 2-12 | VfxEditor 使い勝手改修 + VFX ランタイム設計見直し | 基盤+ED | 追加 | 2-4, 2-9, 2-10 | ✅ 2026-09-08 追加実装([19_vfx_usability_review.md](19_vfx_usability_review.md) / [04] §2.6・§3・§5)。ウィンドウ単体で調整完結(基本設定/Params 定義/検証を内包)、選択追従+ロック、ドメインリロード耐性、Anchor 変更の即時反映(`ReapplyAnchor`)+解決状態表示+SceneView 移動/回転ハンドル、リピート再生、Hierarchy 整理、Undo 同期。ランタイム: 姿勢式を `AnchorPose` に統一(FollowRotation 時も LocalEuler が効く)、`Vfx` ファサード完成+Handle 拡張、LightLayerMask 既定 1 / 0=上書きしない、破棄済み Root の台帳掃除。⚠ Unity 上でのコンパイル・EditMode テスト実行による確認が必要(MCP 接続後に実施) |

- **VFX Graph 未対応**: `com.unity.visualeffectgraph` パッケージが本プロジェクトに未導入のため、VfxManager は ParticleSystem のみを対象とする。導入後は VisualEffect コンポーネント検出処理を追加すれば同じ Handle API で扱える設計にしてある
- **AssetType に `Model` を追加**: 05 の ModelData(モデル本体・Slot・Avatar)と 07 の PrefabData(4-4、ゲームプレイ用オブジェクト・GameplayTags)は別概念だが、既存の `AssetType.Prefab` 1つしか無かったため両者が衝突していた。enum 末尾(既存値の並び順は変更しない)に `Model` を追加して分離した
- **SetMaterial/DefaultAnimation は ID 保存のみ**: `ModelData.Slots[].Material`(AssetId&lt;MaterialMarker&gt;)と `DefaultAnimation`(AssetId&lt;AnimMarker&gt;)は、参照先の MaterialData(3-5)/AnimManager(3-1)がまだ存在しないため、実際の見た目反映・再生はできない。ID の保存・Validator・Inspector 表示までは完成しており、Phase 3 側でそのまま繋ぎ込める
- **Cosmetic 配送は位置のみ**: `INetBridge` に Transform→NetId の逆引きが無いため、`anchorNetId` は将来の NGO アダプタ向けの予約フィールド(常に0)。現状は送信時点のワールド座標を送るのみで、リモート側でのアンカー追従は行わない。ParamValue の同期も未実装(AC の範囲外と判断)
- **Skybox プレビュー見送り**: `RenderSettings.skybox` はプロジェクト全体で共有される設定のため、プレビュー用に切り替えるとユーザーが開いている実シーンの見た目まで変わってしまう。安全のため ModelEditor では背景色切り替えのみ実装した

## Phase 3: Animation + Material/Texture (M3)  約 5.5 週

| # | チケット | 担当 | 日数 | 依存 | AC |
|---|---|---|---|---|---|
| 3-1 | AnimData + AnimManager + AnimatorProxy | 基盤 | 4 | 0-8 | ✅ 2026-09-08 実装（[05] B-3 実装メモ）。`AnimManagerTests` 17 件（PlayMode 290 green、2026-09-09 確認）。2026-09-09 レビュー対応: Layer 別 Proxy 状態、Animator.speed 復元、複数周回 Tick、`StopAllFor`、Model Despawn での停止、SetMaterial の Instance 側保持（`AnimLifetimeReviewTests` 7 件） |
| 3-2 | BlendShapeTrack / IkProfile 適用 | 基盤 | 2 | 3-1 | ✅ 3-1 と同時に `AnimatorProxy` へ実装（BlendShape はテスト済み。IK は OnAnimatorIK 経由のため AnimEditor(3-3) で実機確認） |
| 3-3 | AnimEditor（タイムライン/イベント D&D/ブレンド確認） | ED | 4 | 1-6, 3-1 | ✅ 2026-09-09 実装（[05] B-4 実装メモ）。タイムラインのシーク・マーカードラッグ・ブレンド確認・実行時 Validation。IK ターゲットの配置 UI は未実装。2026-09-09 追記: 再生先「シーン(SceneView)」を追加（`SceneAnimPreviewDriver`。確認用シーン / プレハブモードのモデルをその場で駆動、停止で元ポーズに復元）。全 Data の Inspector 最上部に「エディターで開く」（[09] §8）。2026-09-10: ブレンド確認を高級化（遷移シーケンス / レイヤー同時再生 + レイヤー重み / Blend Tree パラメータ 2D パッド。`AnimEditorWindow.Blend.cs`）。同日、ITAMI の SE タブから「Animator から選択 / SE 波形 / SE・VFX イベント一覧 / イベントコピー」を移植（`AnimEditorWindow.Source.cs`） |
| 3-4 | Anim × SE/VFX 同時プレビュー | ED | 2 | 3-3, 2-4, 1-7 | ✅ 2026-09-09 `AssetEventDispatcher`（Runtime/Presentation）を PreviewService に組み込み。PlayMode 293 green（`AnimIntegrationTests` 3 件追加） |
| 3-R | VFX / Anim レビュー対応（2026-09-10、実バグ 10 件） | 基盤+ED | 1 | 3-4 | ✅ Pool 上限時の死んだエントリ / Pending の台帳 / シーン保存前の復元 + speed 復元 / 一時停止の一括解除 / Frame 判定の誤差 / Animator.Update 二重 / FadeOut 中の再開 / Fire・Seek の Repeat / AnchorGroup の KeepWhilePlaying + フック / 無効 Handle 警告のノイズ。回帰テスト 13 件追加 |
| 3-5 | MaterialCommon 規約確定 + MaterialData/Mats | TA+基盤 | 3 | 0-* | ✅ 2026-09-10 実装（[06] A 実装メモ）。`MaterialCommon` 規約確定 + `MaterialCommonBinding`（URP Lit / HDRP / Built-in へ流し込み）+ `MaterialData` / `TextureData`(最小) + `MaterialManager`（Get / Apply / Replace / FadeTo / MaterialAnim / Placeholder）+ `Mats` + Validator + Bootstrap 配線 + `Models.SetMaterial` 実処理。`MaterialManagerTests` 9 件。エディタ（プレビュー）は 3-9 |
| 3-6 | シェーダー変換テーブル + 変換エディタ | TA | 3 | 3-5 | ✅ 2026-09-10 実装（[06] A 実装メモ）。`ShaderConversionTable` + `MaterialConverter`（共通維持 / 対応 / 同名維持 / 破棄レポート）+ `MaterialConvertWindow`（差分プレビュー → 新規作成 or Undo 付き変換）。`MaterialConverterTests` 4 件。見た目の並列比較は 3-9 |
| 3-7 | Maya FBX → MaterialData 自動生成（ImportProfile） | TA | 3 | 3-5 | ✅ 2026-09-10 実装（[06] A 実装メモ）。`MayaImportProfile` + `MayaMaterialImporter`（TextureData 確保 → Common へ写像 → MaterialData 作成、再インポートは Common のみ更新で固有調整を保持、差分レポート）+ `MayaModelPostprocessor` + 手動メニュー。`MayaMaterialImporterTests` 3 件。実 FBX での確認は手動 |
| 3-8 | TextureData + Importer 規約（命名→自動設定） | TA | 2 | 3-5 | ✅ 2026-09-10 実装（[06] B-4 実装メモ）。`TextureImportProfile`（既定ルール `_N/_M/_E/_UI/T_*` + カスタム可）+ `TexturePostprocessor`（`OnPreprocessTexture` で適用）+ `TextureDataValidator`（Missing/UI-Sprite/9-slice/Normal/Mask/POT/MaxSize/規約差分、FixAction 付き）+ `MaterialEditorWindow` に規約表示・再インポートボタン。`TextureImportRulesTests` 9 件 |
| 3-9 | MaterialEditor / TextureEditor プレビュー | ED | 2 | 1-6, 3-5 | ✅ 2026-09-10 実装（[06] A 実装メモ）。`MaterialPreviewBuilder`（球/板(Quad)/Cube/任意 ModelData の生成 + 適用ロジックを UI から分離）+ `MaterialEditorWindow` に形状切替 / ターンテーブル / ライト回転 / 変換前後の並列比較（比較対象 + 「並べて比較」）を追加。`MaterialConvertWindow` に「Material Editor で比較」を追加(`MaterialEditorWindow.OpenCompare`)。`MaterialEditorPreviewTests` 8 件 |
| 3-10 | Anim/Material/Texture Validator | 基盤 | 1 | 0-11 | ✅ 2026-09-10。Anim（`AnimDataValidator`）/ Material（`MaterialDataValidator`）/ Texture（`TextureDataValidator`）が設計書の静的検査を全てカバー。Controller / モデル依存の検査は各エディタ側 |
| 3-11 | 2D スプライトアニメ統合（既存ツール移植 + Anim2DData 自動生成 + ID 発行） | 基盤+ED | 4 | 0-13, 3-1 | ✅ 2026-09-10 実装（[05] C-5 実装メモ）。`Katsuya.Tools.SpriteAnimation` の Slicer/Builder/BlendTreeRegistrar/命名規則を `DDrive.Editor.Anim2D` へ移植（com.unity.2d.sprite 非依存の `TextureImporter.spritesheet` フォールバック）。`EasingFunction/CubicBezierEvaluator` は Foundation `EasingCore`/`ValueDef` に統合。`Anim2DImportProfile`（SpriteAnimationNameData の AssetData 化）+ `Anim2DEditorWindow`（作成/編集の 2 モード、UI Toolkit）で生成完了時に `AssetCreationService.Create` 経由で Anim2DData を自動発行。`Anim2DToolTests` 22 件。Sequence Preview / Sound モード、共通プレビュー・イベント D&D は 3-13 |
| 3-12 | Anim2D Manager + 方向 BlendTree 再生 + Frame イベント | 基盤 | 2 | 3-11 | ✅ 2026-09-10 実装（[05] C 実装メモ）。`Anim2DData : AnimData`（AnimManager 共有で Frame イベントも同経路）+ `Anim2D` ファサード（方向 → BlendTree x,y）+ `Anim2DDataValidator` + Bootstrap Bind。`Anim2DTests` 4 件。3-11 より先に着手（Data の形を確定するため） |
| 3-13 | Anim2DEditor（共通プレビュー移植 + イベント D&D + Validator） | ED | 3 | 3-11, 1-6 | ✅ 2026-09-10 実装（[05] C-5/C-6 実装メモ）。イベント編集・タイムライン D&D・SE/VFX 同時プレビューは `Anim2DData : AnimData` を活かし `AnimEditorWindow`(3D 用)をそのまま再利用（`DataEditorRegistry` が継承で自動解決、属性追加不要）。`Anim2DEditorWindow.Edit` に「Anim Editor で開く」ボタン + イベント件数の要約 + Validation パネル(`Anim2DDataValidator`+`AnimDataValidator`+新規 `Anim2DEditorValidator`、FixAction ボタン付き)を追加。プレビュー配置ロジックは `Anim2DPreviewObject`(新規)に切り出し、`AnimEditorWindow` にも Anim2D フォールバック(ModelData 未設定時に SpriteRenderer+Animator を配置)を追加。`Anim2DEditorTests` 7 件 |

## Phase 4: Canvas + Prefab (M4)  約 7.5 週

| # | チケット | 担当 | 日数 | 依存 | AC |
|---|---|---|---|---|---|
| 4-1 | CanvasData + Ui Manager（Open/Close/Stack/Popup/Back） | 基盤 | 3 | 0-* | スタック・モーダル・PauseGame 連動 |
| 4-2 | ButtonWire / Signal / Navigation 適用 | 基盤 | 3 | 4-1 | 配線データだけで UI 遷移が組める |
| 4-3 | CanvasEditor（Navigation ノードグラフ/パッド シミュレーション） | ED | 4 | 1-6, 4-2 | 到達不能検出、矢印編集 |
| 4-4 | PrefabData + Prefabs Manager | 基盤 | 2 | 0-* | ✅ 2026-09-10 実装（[07] B-3 実装メモ）。`PrefabData`/`PrefabsManager`/`Prefabs`(ファサード)/`PrefabHandleExtensions`。Spawn(位置/親)/Despawn/Tag/Layer/Pool(Prewarm)/Move/GetComponent<T>、OnSpawn・OnDestroy を EventBus で発火(Bootstrap に 2 つ目の `AssetEventDispatcher` を追加)、Prefab 未設定は空 GameObject の Placeholder。`PrefabsManagerTests` 9 件（PlayMode 345 green） |
| 4-5 | Canvas/Prefab Validator | 基盤 | 1 | 0-11 | ⚠ Prefab 側 ✅ 2026-09-10（`PrefabDataValidator` + `GameplayTagDictionary` によるタイポ検出。Prefab Missing / CollisionLayer 未定義値 / Projectile で Pool 未設定 / GameplayTags の空白・重複・タイポ）/ Canvas 側は 4-1 で |
| 4-6 | UiInteractable 基底 + UiButton 新規実装（状態機械 + 全イベント R3/UniTask + Cooldown/Locked） | 基盤 | 4 | 0-13 | 15§A の全 API。多重発火防止テスト green |
| 4-7 | ButtonSkinData + 状態遷移演出/SE 統合 | 基盤 | 2 | 4-6, 4-8 | Skin 差し替えで全ボタンの見た目・音が変わる |
| 4-8 | UiTween エンジン（構造体 0 alloc + TweenHandle + UiFx 関数群） | 基盤 | 3 | 0-13 | MoveTo/Scale/Fade/MoveAlong、await 可、定常 0 alloc |
| 4-9 | ElementFx（Appear/Idle/Disappear + スタッガー + Close 完了待ち） | 基盤 | 2 | 4-8, 4-1 | Open/Close 演出がデータだけで組める |
| 4-10 | UiTweenEditor + CanvasEditor 割当 UI（カーブ一覧・スプラインハンドル編集） | ED | 4 | 4-8, 4-3 | 15§B-6 の全機能 |
| 4-11 | UiPreset ライブラリ（50 種以上のファクトリ + UiFx 同名関数 + Sequence） | 基盤 | 3 | 4-8 | 全プリセットがデータ/コード両方から 1 操作で使える |
| 4-12 | プリセットギャラリー（実再生サムネ一覧・タブ・一括適用・独自プリセット登録） | ED | 3 | 4-11, 4-10 | 要素選択→クリックで適用・即プレビュー |
| 4-13 | Prefab の Simulated Spawn（サーバー権威生成 + NetworkObject 検証 + クライアント直 Spawn 禁止） | 基盤 | 3 | 0-14, 4-4 | サーバー経由でのみ複製生成される |
| 4-14 | UiSlider 実装（値・刻み・全入力・Response/FollowMotion） | 基盤 | 3 | 4-6, 0-15 | 18§B-1/B-3 の全 API。パッド Step・リピート・微調整動作 |
| 4-15 | SliderSkinData + ノッチ SE / 触覚統合 | 基盤 | 2 | 4-14, 4-7 | Skin 差し替えで全スライダーの見た目・音が変わる |
| 4-16 | SliderWire + OptionStore（標準オプション直結） | 基盤 | 2 | 4-14, 4-2 | 音量設定画面がスクリプト 0 行で完成する |
| 4-17 | SliderEditor（応答曲線 / ノッチ可視化 / 実操作プレビュー / プリセット 5 種） | ED | 3 | 4-14, 4-10 | 18§B-6 の全機能 |
| 4-18 | Slider Validator 一式 | 基盤 | 1 | 4-14, 0-11 | 18§B-7 の全検査 |

## Phase 5: Presentation + ブラウザ完成 (M5 — 目玉)  約 5 週

| # | チケット | 担当 | 日数 | 依存 | AC |
|---|---|---|---|---|---|
| 5-1 | PresentationData + Presentation Manager（AtTime/OnSignal/Cancel） | 基盤 | 3 | 1〜4 | 剣攻撃デモが 1 API で再生 |
| 5-2 | CameraShakeData + CameraFx（Trauma 合成 / 揺れ専用ノード / GlobalScale） | 基盤 | 3 | 0-9 | 多重発火で破綻しない。オプション 0% で無揺れ |
| 5-2b | HapticsData + Haptics Manager（2 モーター Max 合成 / LocalPlayerOnly / GlobalScale） | 基盤 | 2 | 0-9 | パッドで振動再生。同時再生で飽和しない |
| 5-2c | ShakeEditor / HapticsEditor（波形編集 + カメラ実揺れプレビュー + Test on Pad + プリセット 10 種） | ED | 3 | 5-2, 5-2b, 1-6 | 設計書 16 §C-2 の全機能 |
| 5-3 | Timeline トラック対応（PlayableDirector + Marker） | 基盤 | 2 | 5-1 | — |
| 5-4 | PresentationEditor（マルチトラック UI + 統合プレビュー + Signal 手動発火） | ED | 5 | 5-1, 1-6 | 設計書 08 §4 の全機能 |
| 5-5 | 依存関係グラフ（収集/キャッシュ/差分更新） | ED | 3 | 0-2 | Scene/Prefab 内 IdRef も収集 |
| 5-6 | 使用箇所検索 / 未使用検出 / 依存ツリー UI | ED | 3 | 5-5, 1-5 | ダブルクリックジャンプ、一括 Archive |
| 5-7 | Preload リスト自動集計 + シーンロード統合 | 基盤 | 2 | 5-5 | ロード画面で Preload 完了 |
| 5-8 | Presentation ネット再生（開始時刻シーク / Signal 中継 / 予測再生） | 基盤 | 4 | 2-8, 5-1 | 遅延 200ms 環境で 2 クライアントの位相が揃う |
| 5-9 | Late Join 復元（アクティブ演出スナップショット） | 基盤 | 2 | 5-8 | 途中参加でループ VFX/BGM が復元 |

## Phase 6: 仕上げ・運用化 (M6)  約 2 週

| # | チケット | 担当 | 日数 | 依存 | AC |
|---|---|---|---|---|---|
| 6-1 | CI 完全化（Validation/ID 差分/テストの PR ゲート） | 基盤 | 2 | 0-11 | PR で自動実行・fail でマージ不可 |
| 6-2 | パフォーマンス計測・0 alloc 検証（Profiler CI） | 基盤 | 2 | 全 | NFR-1/2/4 達成をベンチで証明 |
| 6-3 | バージョン記録・変更履歴 UI | ED | 1 | 0-2 | 保存フックで自動記録 |
| 6-4 | ドキュメント整備 + デザイナー向けチュートリアル動画/サンプル | 全員 | 3 | 全 | 新規メンバーが SE 追加を 15 分で完遂 |
| 6-5 | カタログ ContentHash 生成 + 接続時照合 | 基盤 | 2 | 6-1 | 不一致クライアントを検出・切断 |
| 6-6 | 受信検証・レート制限 + ネット Validator（NetMode 整合 / NetworkObject 欠落） | 基盤 | 2 | 2-8, 0-11 | 不正 ID 送信が破棄・ログ。14§10 の全検査 |
| 6-7 | 2 クライアント自動テスト（Loopback ⇔ NGO 両ブリッジで PlayMode CI） | 基盤 | 2 | 6-1, 5-8 | CI で同期再生テスト green |
| 6-8 | 受け入れデモ（要件 §7 成功基準の 5 項目） | 全員 | 2 | 全 | リード承認 |

## Phase 7: 推奨拡張 A 群 (M7)  約 3 週　※詳細は [13_extensions.md](13_extensions.md)

| # | チケット | 担当 | 日数 | 依存 | AC |
|---|---|---|---|---|---|
| 7-1 | MissingAssetLog（Placeholder 記録 + 静的走査） | 基盤 | 2 | 0-4, 5-5 | 未登録 ID の参照元・回数が取れる |
| 7-2 | 未実装タブ（発注リスト UI + CSV/MD 出力 + 担当割当） | ED | 2 | 7-1, 1-5 | 発注一覧をエクスポートできる |
| 7-3 | デバッグオーバーレイ（Instance/Pool/メモリ/チートパレット） | 基盤 | 3 | 0-6, 0-7 | 実機で任意 ID 再生・個別 Kill |
| 7-4 | Live Tuning（接続 + プリミティブ値パッチ + 書き戻し） | 基盤 | 4 | 7-3 | 実機の VFX 色変更が 1 秒以内に反映 |
| 7-5 | BudgetProfile + Budget Validator + シーン別集計 | 基盤 | 2 | 0-11, 5-7 | 予算超過が CI で fail |
| 7-6 | サムネイル自動生成（バッチ駆動 + PR 差分画像） | ED | 2 | 1-6, 6-1 | 全 Data のサムネが CI で更新 |
| 7-7 | AssetVariantSet（Quality/プラットフォーム別解決） | 基盤 | 3 | 0-4 | tier 切替で同一 ID の Data が替わる |
| 7-8 | FacialController 移植（カメラ角度依存フェイシャル補正。`C:\Users\yamag\wrench\FacialController_UE` の UE5 プラグイン。2026-09-10 決定） | 基盤+ED | 26〜29（MVP 12〜15） | 3-1, 3-3 | 新種別 Facial: `FacialData`（グリッド・感情レイヤー・ソースポーズ・ポリシー）+ ベイカー（BakeMesh − バインドポーズ → 複製 Mesh に AddBlendShapeFrame、差分ベイク・閾値カット・ミラー/IDW）+ `FacialManager`（カメラ→頭ボーン相対角、FacialCore の重み評価・スムージング・距離フェード・表情強度減衰、MaterialPropertyBlock 出力）+ エディタ（グリッド/ポーズ/作業セット/レイヤー/検証/コピー/参照リペア）+ `.fcpose.json` 入出力。UE 側 FacialCore（370 行）と JSON 形式は写経で移植可、Slate UI 6,000 行は UI Toolkit で作り直し |

**M7 デモ**: 実機接続で VFX の色・Anchor をエディタから即時調整 → 書き戻し。未実装アセット一覧が発注書として出力される。

## サマリ

| Phase | 期間目安 | 主要成果 |
|---|---|---|
| 0 | 4.5 週 | 基盤（ID/Registry/Pool/Event/Validation/EasingCore/ValueDef/NetBridge + NGO アダプタ） |
| 1 | 3.5 週 | Audio 縦切り（AnchorDef 3D / SeEmitter）+ Browser/Preview 基盤 + ValueDef 共通 Drawer |
| 2 | 3.5 週 | VFX（UI パーティクル含む）+ Model + Cosmetic 配送 |
| 3 | 5.5 週 | Animation 3D/2D（既存ツール統合）+ Material/Texture パイプライン |
| 4 | 7.5 週 | Canvas + UiButton/UiSlider（UiInteractable 基底）+ UiTween + プリセットライブラリ + Prefab（Simulated Spawn 含む） |
| 5 | 5 週 | Presentation（ネット同期再生・Late Join 含む）+ Shake/Haptics + ブラウザ完成 |
| 6 | 3 週 | CI・性能・運用化・ContentHash 照合・2 クライアント自動テスト |
| 7 | 3 週 | 発注リスト・デバッグ・Live Tuning・予算・バリアント |
| 計 | **約 36 週** | 2〜3 名（基盤 1 + エディタ 1 + TA 0.5）想定。マルチプレイは全 Phase に組込（v1 必須） |

並列化のヒント: Phase1 以降、基盤担当と ED 担当はチケット依存の範囲でほぼ常時並列。TA は Phase3 集中。
