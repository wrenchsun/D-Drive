# 09. エディタツール詳細設計（AssetBrowser / プレビュー基盤）

関連: [02_core_framework.md](02_core_framework.md) §11-12 / 各アセット設計書のエディタ節

UI Toolkit で実装（Unity 6 前提）。すべての操作は Undo 対応（NFR-5）。

---

## 1. AssetBrowser（システムの中心ウィンドウ）

```
┌────────────────────────────────────────────────────────────┐
│ [検索____________] [種別▼] [タグ▼] [作成者▼] [⚠Validation] │
├──────────┬─────────────────────────┬───────────────────────┤
│ ツリー     │ アセット一覧 (グリッド/リスト) │ インスペクタ+プレビュー │
│ ├ Audio  │ ┌────┐┌────┐┌────┐      │  ┌─────────────────┐ │
│ │ ├ SE   │ │icon││icon││icon│      │  │  Preview ペイン   │ │
│ │ └ BGM  │ └────┘└────┘└────┘      │  │  ▶ ■ ⟳ 1.0x     │ │
│ ├ VFX    │  SE_Slash  VFX_Fire ...  │  ├─────────────────┤ │
│ ├ Anim   │                         │  │  Data Inspector  │ │
│ ├ ...    │                         │  │  (種別ごとの専用UI)│ │
│ ├ ★お気に入り│                       │  ├─────────────────┤ │
│ └ 🕒最近   │                        │  │ 使用箇所 / 依存    │ │
└──────────┴─────────────────────────┴───────────────────────┘
```

### 機能一覧

| 機能 | 仕様 |
|---|---|
| 横断検索 | ID / 名前 / タグ / 種別 / 作成者 / 説明文。インクリメンタル。"Fire" → FireSE, FireVFX, FireMaterial... を横断表示 |
| フィルタ | 種別・タグ・Validation 状態（エラーのみ表示等）・未使用のみ |
| 新規登録 | 「新規」ボタン or **Prefab/Clip を一覧へ D&D** → 種別自動判定して Data 生成 + ID 発行。**入力は意味情報のみ**（表示名〈日本語可〉・カテゴリ・タグ・識別子）で、ファイル名・ID・カタログ登録・Addressables アドレス・**配置フォルダ（カテゴリ階層を GameData 配下にミラー）**はツールが自動生成（[00] FR-1.5、[10] §3/§3.3）。2026-09-09: Addressables グループ（`DDrive_GameData`）への登録もこの 1 トランザクションに含む（`AddressablesSync`、[02] §5）。既存分は `Generate/Addressables 登録を同期` または Validation の FixAction |
| 名前の変更・正規化 | 表示名・識別子・カテゴリの変更はブラウザ上で行い、ファイル名・配置フォルダはツールが規約へ追従（`Generate/GameData をカテゴリ配置に整理` がフォルダ移動 + リネーム + カタログ Address 更新を実施）。直接リネームされたファイルは Validation の FixAction で正規化 |
| 使用箇所検索 | 選択アセットを参照する Data / Scene / Prefab を一覧表示（依存グラフ逆引き）。ダブルクリックでジャンプ |
| 依存関係ツリー | Player.prefab → Fire.mat → Fire.shader → FireVFX → FireSE をツリー/グラフ表示。深さ切替 |
| 未使用検出 | どこからも参照されない Data の一覧。一括アーカイブ（削除でなく Archived タグ付与 → 次リリースで削除） |
| 安全な削除（2026-09-14、5-6） | 行の右クリックメニュー「削除...」。参照チェック（1件でもあれば一覧を出して中止）→ Archived タグ付与 → 確認ダイアログ → カタログ登録解除 + Addressables エントリ削除 + アイコン PNG ごと `MoveAssetToTrash`（OS のゴミ箱、復元可能）。実装は §10 の実装メモ参照 |
| お気に入り/最近 | ユーザーローカル（EditorPrefs）に保存 |
| ID 定数再生成 | ツールバーから 1 クリック。保存フックでの自動生成も設定可 |
| Validation | ⚠ボタンで全体検査 → 結果一覧（Error/Warning、FixAction ボタン付き）。行クリックで該当 Data へ |
| 一括操作 | 複数選択 → タグ付与 / カテゴリ移動 / Addressable グループ変更 |

### 実装メモ

- 一覧のデータソースは AssetRegistry の Entries + 依存グラフキャッシュ。`AssetPostprocessor` で差分更新
- 検索インデックスは起動時に構築し EditorPrefs でなく `Library/DDrive/` にキャッシュ
- 大量アセット対応: ListView の仮想化（1 万件でスクロール 60fps）

### 1.1 インポート検知による Data 自動生成（ImportRule、チケット 5-11、2026-09-14）

**`Assets/SourceAssets/<種別>/<カテゴリ.../>` に元ファイルを置くだけで、D&D も新規ダイアログも使わずに Data・ID・カタログ・Addressables 登録までができる。** [06_material_texture.md] A-2 の Maya→Material（3-7）と同じ「作成そのものは `AssetCreationService.Create` に一本化する」方針を、種別を横断する形で汎用化したもの。

- **監視フォルダ → 種別の対応表**（`ImportRuleService` の `IImportRuleHandler` 9 種。`Editor/Import/`）:

  | フォルダ(`SourceAssets/` 配下) | 種別 | 対象拡張子 | 割り当て先フィールド |
  |---|---|---|---|
  | `Se/<カテゴリ>/` | Se | .wav / .mp3 / .ogg / .aiff / .aif | `SeData.Clips`(先頭 1 本) |
  | `Bgm/<カテゴリ>/` | Bgm | 同上 | `BgmData.LoopBody` |
  | `Texture/<カテゴリ>/` | Texture | .png / .jpg / .jpeg / .tga / .psd / .tif / .tiff / .exr / .bmp | `TextureData.Texture`(Usage/Channel は既存 `TextureImportProfile` の命名規約に一致すればその既定値、無ければ Data の既定値 Model/Albedo のまま) |
  | `Model/<カテゴリ>/` | Model | .fbx | `ModelData.Prefab`(FBX のインポート直後のルート GameObject を直接参照。ラッパー Prefab を挟む運用なら別途差し替える) |
  | `Anim/<カテゴリ>/` | Anim | .anim / .fbx | `AnimData.Clip`(.fbx は埋め込みの `AnimationClip` サブアセットの先頭 1 本。Unity が自動生成する `__preview__` は除く) |
  | `Anim2D/<カテゴリ>/` | Anim2D | .anim / .fbx | `Anim2DData.Clip`(Anim と同じ。`Directions`/`DirectionClips` は既存の Anim2DEditor(3-11/3-12)でスプライトから追加する運用) |
  | `Prefab/<カテゴリ>/` | Prefab | .prefab | `PrefabData.Prefab` |
  | `Canvas/<カテゴリ>/` | Canvas | .prefab | `CanvasData.Prefab` |
  | `Vfx/<カテゴリ>/` | Vfx | .prefab | `VfxData.Prefab` |

- **識別子・カテゴリ**: 識別子は元ファイル名(`AssetNamingService.ToIdentifier`)、カテゴリはフォルダの `<種別>/` から先の階層パス(`Player/Attack` のように複数階層可、空でも可)。FBX のルート名やプレハブのルート GameObject 名はデザイナーが揃えているとは限らないため使わない
- **二重生成防止**: 新設の `AssetDataBase.ImportSourceGuid`(元ファイルの GUID。`[HideInInspector]`)で同定する。`ImportRuleService` は種別(DataType)ごとに `ImportSourceGuid → Data` の索引を 1 回だけ作って使い回す([06] A-2 実装メモの `MayaMaterialImporter.BeginBatch/EndBatch` と同じ狙い)。既に見つかった場合は何もしない(デザイナーの調整を上書きしない)
- **入口**: `ImportRulePostprocessor`(`AssetPostprocessor.OnPostprocessAllAssets`。`MayaModelPostprocessor` と同じく delayCall でまとめて `ImportRuleService.ProcessPaths` へ渡す。`deletedAssets` は見ない = 元ファイル削除時に Data を消さない)。テスト等からの抑止は `ImportRulePostprocessor.Suppress` / `ImportRuleService.AutoImport`
- **手動フォールバック**: `Tools/D-Drive/Generate/SourceAssets からインポートルールを再実行`(`ImportRuleService.ScanAll`)。AutoImport=OFF だった期間や機能導入前から置かれていたファイルを一括で取り込む
- **「欠落」表示**: 元ファイルを削除しても Data は消えない(参照フィールドが null になるだけ)。各種別の既存 Validator(`SeDataValidator`/`BgmDataValidator`/`TextureDataValidator`/`ModelDataValidator`/`AnimDataValidator`/`Anim2DDataValidator`/`PrefabDataValidator`/`CanvasDataValidator`/`VfxDataValidator`)がすでに「未設定(または Missing)です」の Error を出す実装だったため、新規 Validator は追加していない(AssetBrowser の Validation 一覧・⚠に既存のまま出る)
- **対象外の種別**(元ファイルが無い): Presentation / Shake / Haptics / UiTween / Anchor / AnchorGroup / ControlSkin(5-13 で別枠)。Cutscene は 6-10c で `IImportRuleHandler` を 1 つ追加する形で拡張する想定
- 要判断は [28_manual_verification_phase5.md](28_manual_verification_phase5.md) の「5-11」節末尾を参照(Anim2D の元ファイル解釈・複数テイク FBX 等)
- テスト: `Tests/Editor/ImportRuleServiceTests.cs`(ルーティング/カテゴリ抽出/9 種別の生成/再取り込みでの二重生成防止/元ファイル削除後も Data が残ることの確認、14 件)

## 2. プレビュー基盤（PreviewService）

> **開いているシーンに置く確認用プレビューの規約（2026-09-14、ユーザー指示「エディタを閉じたとき・違うシーンに移動したときにプレビューを持ち越さない」）**
> - ルートは **エディタ固有の `[D-Drive] 〜 Preview` という名前 + `HideFlags.DontSave`** で作る（`EditorPreviewRoots.CreateRoot`）。名前を他のエディタと共有しない（以前 Button Skin / Slider Skin / UI Tween が `[D-Drive] Ui Preview` を共有し、片方の「撤去」がもう片方を消していた）
> - ウィンドウの `OnDisable`（閉じる・ドメインリロード）で自分のプレビューを消し、`OnEnable` で同名の残骸を `EditorPreviewRoots.DestroyAll` で消す（2026-09-14: `DestroyAll` は名前だけでなく「DontSave のプレビュールート」であることも条件にした。同じ名前の本物は消さない）
> - **HideFlags は子に引き継がれない**。ルートだけ DontSave だと、プレビューを置いたままシーンを保存したときに子（ボタン・Fill・Handle など）が親無しで保存される。子は `EditorPreviewRoots.CreateChild` で作り、組み立て後に `MarkDontSaveRecursive(root)` で全体に掛け直す。Canvas 付きのルートは `CreateOverlayCanvas`、スライダーの部品は `PreviewSliderFactory.Create` を使う（2026-09-14 レビュー対応）
> - プレビューが動いている間の描き直しは `ViewRepaintThrottle` で **30fps 上限**に間引き、止まった直後に 1 回だけ描く（Edit Mode の Game ビューは自動で描き直されないため `RepaintAllViews` を使うが、毎 update では呼ばない）
> - 自動で進む処理（状態遷移のループ等）はプレビューを**作り直さない**。無くなっていたら止める。プレビューを置くのはユーザーが ▶ を押したときだけ（撤去・シーン移動の後に勝手に復活させない）
> - **DontSave のオブジェクトはシーンを閉じても破棄されず、どのシーンにも属さない「孤児」になって Hierarchy に出ないまま描画され続ける**（2026-09-12 の `[D-Drive] UI Root` 17 個、2026-09-14 の Slider Editor / UI Root の残骸）。個別の後片付けの漏れに備え、`EditorPreviewSweeper`（`[InitializeOnLoad]`）が「シーンを閉じる直前にそのシーンのプレビューを消す」「シーンを開いた後・ドメインリロード後・Play Mode から戻った後に孤児を消す」「Play Mode に入る直前に全部消す」を一括で行う。対象は `[D-Drive]` で始まる DontSave のルートだけ（`[D-Drive] Runtime` 等の本物・Unity 内部の孤児には触らない）。手動は `Tools > D-Drive > Debug > 確認用プレビューの残骸を掃除`
> - プレビューを参照し続けるコードは「いつ消されてもよい」前提で Unity の null 判定をして作り直すこと

各専用エディタ（Audio/VFX/Anim/Material/Presentation）が共有する基盤。

- **専用プレビューシーン**を `EditorSceneManager.NewPreviewScene` で生成し、そこで**実 Manager 群を初期化して駆動**する（ADR-4: Editor 専用再生経路を作らない）
- EditMode 中は `EditorApplication.update` から `Tick(dt)` を回す
- 共通 UI: 再生 / 停止 / ループ / 速度（0.1x–2x）/ シーク / 背景切替（暗室・グレー・屋外・任意シーン）/ ライト切替 / ポスプロ ON-OFF / **比較表示（2 ペイン同期再生）** / スクリーンショット→PreviewImage 保存
- 種別固有プレビューは各設計書（03〜08）の仕様に従い、この基盤上に実装
- **SceneView 方式**（2026-09-08 VFX / 2026-09-09 Anim）: 独自ビューポートではなく、開いているシーン / プレハブモードに直接スポーン（Anim は借用した Animator をその場で駆動）して SceneView で確認する。VFX は `SceneVfxPreviewDriver`、Anim は `SceneAnimPreviewDriver`（`Editor/Anim/`）。どちらも実 Manager を駆動し、配置物は `[D-Drive] … Preview` ルート（`HideFlags.DontSave`）にまとめてシーン / Prefab に保存しない。Anim は再生前のポーズをスナップショットし、停止・対象解除・ステージ切替・Prefab 保存の直前に復元する。VFX / Anim はこの方式のみ（AnimEditor のウィンドウ内ビューポートは 2026-09-09 に廃止）。`PreviewService` のプレビューシーンは Audio / Model / Anchor 系が使う
- **プレビューはウィンドウ内描画ではなく、確認用シーン / Prefab を開いて SceneView で実 Manager を駆動する**（2026-09-10 決定、全エディタ共通。Material = `MaterialPreviewBuilder`、Anim2D = `SceneAnimPreviewDriver` に SpriteRenderer + Animator の DontSave 物を渡す）。静的な補助表示（スライス矩形の輪郭など）はウィンドウ内でよい。**例外: Material（2026-09-11 決定）** — `MaterialThumbnailRenderer`（`PreviewRenderUtility`）で実 `MaterialManager` が生成した共有 Material を球/板/Cube に描くウィンドウ内サムネイルを併用する。時間軸を持たず再生経路を二重化しないため ADR-4 の趣旨は保てる。既定ライトのみで描くので、実シーン照明・ModelData 適用・並列比較は従来どおりシーン配置で確認する

## 3. ID 参照 PropertyDrawer

- `SeIdRef` 等のフィールドを Inspector で「検索付きドロップダウン + プレビューボタン + Browser で開く」として描画
- 未登録/削除済み ID は赤表示 → プログラマーのモックコードでも設定ミスが即見える

## 4. 保存フック（AssetDataBase 共通）

保存時に自動実行: Version+1 / Author・UpdatedAt 記録 / 該当種別の Validator 実行（結果を Inspector 上部にバナー表示）/ 依存グラフ差分更新 / （設定時）ID 定数再生成

## 5. CI 連携

```
Unity -batchmode -executeMethod DDrive.Editor.CI.ValidateAll -logFile -
  → 全 Validation 実行、Error があれば exit 1
  → 結果を JUnit XML で出力（PR に表示）
Unity -batchmode -executeMethod DDrive.Editor.CI.RegenerateIds
  → ID 定数の生成漏れ検出（生成結果に差分があれば fail）
```

## 6. メニュー構成

メニューパスの文字列直書きは禁止（[00] §5）。定数クラス `DDriveMenu` に集約し、全 `[MenuItem]` がこれを経由する（後の改名・再配置を 1 箇所で吸収する）。
新規のトップレベルメニューは追加せず、Unity 標準の `Tools` メニュー配下に置く。

```csharp
// DDrive.Editor
public static class DDriveMenu
{
    public const string Root       = "Tools/D-Drive/";
    public const string Editors    = Root + "Editors/";
    public const string Validation = Root + "Validation/";
    public const string Generate   = Root + "Generate/";
    public const string Debug      = Root + "Debug/";
    // 使用例: [MenuItem(DDriveMenu.Root + "Asset Browser")]
}
```

```
Tools/
└─ D-Drive/
    ├─ Asset Browser
    ├─ 未使用アセット                ← 2026-09-14 追加(5-6。UnusedAssetsWindow。AssetBrowser の「未使用...」ボタンからも開く)
    ├─ 仕様書と同期                 ← 2026-09-14 追加(5-13。SpecSyncWindow。差分プレビュー + 適用 + TSV コピー、[27] §8.2)
    ├─ Presentation Editor          ← 目玉機能につき最上段
    ├─ Editors/
    │   ├─ Audio
    │   ├─ VFX
    │   ├─ VFX確認用シーンを開く
    │   ├─ Anchor                      ← 2026-09-08 追加(AnchorData 専用エディタ、[21])
    │   ├─ Anchor Group                ← 2026-09-08 追加(配置セット、[22])
    │   ├─ Animation (3D)
    │   ├─ Animation (2D)              ← 2026-09-10 実装(Katsuya.Tools.SpriteAnimation 移植 + Anim2DData 自動生成、[05] C-5 実装メモ)
    │   ├─ Material                   ← 2026-09-10 追加(MaterialData / TextureData 共用エディタ。3-9 で球/板/Cube/任意 ModelData の切替・ターンテーブル・ライト回転・変換前後比較を追加、[06] A 実装メモ)
    │   ├─ Material 変換              ← 2026-09-10 追加(シェーダー変換、差分プレビュー、[06] A-2。2026-09-11 ウィンドウ内に変換前 / 変換後のサムネイル比較を追加)
    │   ├─ (Generate) 選択した Material を D-Drive/Lit・Unlit の MaterialData に変換 ← 2026-09-11 追加(UnityMaterialMigrator、[06] A 実装メモ)
    │   ├─ Material プレビュー        ← 2026-09-11 追加(MaterialThumbnailWindow。サムネイルだけの独立ウィンドウ。Material Editor の「ポップアップ」/ Inspector の「プレビューをポップアップ」からも開く。[06] A 実装メモ)
    │   ├─ Canvas                     ← 2026-09-11 実装(CanvasEditorWindow。SerializedObject バインド + Selectable 自動収集 + 確認用シーンで開く/閉じる + Validation リスト。4-10 で ElementFx 割当セクション(Appear/Idle/Disappear ごとに なし/組み込みプリセット/UiPresetCatalog/UiTweenData 直接指定 + 他の要素へコピー)を追加。4-3 で NavigationGraph/NavigationGraphView(手組みノードグラフ) + パッド操作シミュレーションを追加、[15] B-5 実装メモ)
    │   ├─ Button Skin                ← 2026-09-11 追加(ButtonSkinEditorWindow。ウィンドウ内描画なし、SerializedObject の InspectorElement のみ + 「確認用シーンに配置」で実 UiButton を生成、[15] A-4 実装メモ)
    │   ├─ UI Tween                    ← 2026-09-11 実装、4-10 でカーブ一覧(Track ごとの要約 + 64 サンプルの曲線プレビュー、選択して `PropertyField` 編集)+ PathMove 選択中の SceneView スプライン制御点ハンドル(追加/削除ボタン付き)+ プリセット/カタログのドロップダウン生成を追加(実行中の Tween 自体はウィンドウ内に描かず「確認用シーンに配置」+実 UiTweenManager で見る。[15] B-5/B-6 実装メモ)。プリセットギャラリーは 4-12 のまま未実装
    │   ├─ Slider Skin                 ← 2026-09-11 追加(SliderSkinEditorWindow。ButtonSkinEditorWindow と同じ設計 + 音量/感度/HPバー/スタミナ/キャラメイクの 5 プリセット。応答曲線グラフ・ノッチ可視化・追従比較は 4-17 の SliderEditor に委譲(共有ボタン付き)、[18] B-6 実装メモ)
    │   ├─ Slider                      ← 2026-09-11 実装(4-17。SliderEditorWindow。応答曲線グラフ(IMGUI 静的描画 + ValueDef の PropertyField)・ノッチ/SnapThreshold 可視化(ウィンドウ内 + SceneView オーバーレイ)・実操作(パッド ◀▶・微調整・ドラッグ模擬)・追従比較(2 体目配置)・Skin プレビュー(全 6 状態並べ)・プリセット 5 種(`SliderPresets` 共有)・Validation リスト、[18] B-6 実装メモ)
    │   ├─ UI Tween · Preset Gallery    ← 2026-09-11 実装(4-12。UiPresetGalleryWindow。タブ(出現/常時/消滅/強調/カタログ)+ 検索 + お気に入り(EditorPrefs)の静的カード一覧(名前・カテゴリ・64 サンプルの静的イージング曲線スケッチ)。カードから「この要素に適用」「Canvas 内一括適用」「選択中のシーン要素で再生」「設定を他の要素へコピー」「独自プリセットとして登録」、[15] B-3.5 実装メモ)
    │   └─ Shake · Haptics
    ├─ Validation/
    │   ├─ Run All
    │   └─ Report Window
    ├─ Generate/
    │   ├─ Regenerate Asset IDs
    │   ├─ Regenerate Tuning Keys       ← 2026-09-14 追加(5-13。TuningTable.Entries から Assets/Generated/Tuning.g.cs の TUNING.キー定数を生成、[27] §8.4)
    │   ├─ Anchor プレハブを生成 / 選択した Transform から Anchor を作成 / 選択した AnchorRig から Anchor を一括生成   ← [21] §3.9
    │   ├─ Rebuild Dependency Graph
    │   └─ Live Tuning Connect
    └─ Debug/
        ├─ Runtime Overlay
        └─ Missing Asset Report（発注リスト）
```

## 7. ウィンドウレイアウト規約（拡縮前提）

**すべての `EditorWindow`（AssetBrowser 本体を除く各専用エディタ）は、ウィンドウが最小サイズまで縮小されてもコンテンツの下端まで到達できなければならない。**

- `CreateGUI()` では `rootVisualElement` に直接コンテンツを積まず、まず `ScrollView`（`ScrollViewMode.Vertical`、`flexGrow = 1`）を1つ生成して `rootVisualElement` に追加し、以降のセクションはすべてその `ScrollView` に積む
- セクションが増える設計（VfxEditor/ModelEditor のように環境切替・複数同時再生・パラメータ等を Foldout で積み重ねる形）は特に対象。`minSize` を大きめに設定して回避しない（ユーザー環境の画面解像度は前提にできない）
- 例外: `AssetBrowserWindow` のように `ListView` 自体が仮想化スクロールを持つ場合、その `ListView` に `flexGrow: 1` を与えれば足りる（二重にラップする必要はない）
- 発見の経緯: VfxEditor/ModelEditor（Phase 2, 2-4/2-6）でこの対応を忘れ、ウィンドウを小さくすると下部のセクション（イベント編集等）に到達できなくなる不具合があった。以後の新規エディタ実装ではこの規約を最初から満たすこと

## 8. Inspector の「エディターで開く」ボタン（2026-09-09）

**専用エディタを持つ Data アセットは、Inspector の最上部に「〜で開く」ボタンが出る。既存・今後追加する種別すべてに適用する。**

- 仕組み: `Editor/Inspector/AssetDataInspector.cs`（`[CustomEditor(typeof(AssetDataBase), true)]`）が全 Data 共通の Inspector として、先頭に `DataEditorHeader.Draw` を描いてから既定の描画をする
- 対応表は属性で宣言する。EditorWindow に `[DataEditor(typeof(XxxData), "Xxx Editor で開く")]` を付けるだけ（`Editor/Inspector/DataEditorAttribute.cs`）。`public static Open(XxxData)`（引数型は基底でも可、名前は `openMethod` で変更可）を `DataEditorRegistry` が TypeCache で拾う。1 ウィンドウが複数種別を扱う場合は属性を複数付ける（AudioEditor = SE / BGM）
- 継承した Data（`VfxData` の派生など）は基底型の登録を引き継ぐ
- 種別独自の Inspector を作る場合は `AssetDataInspector` を継承し、`OnInspectorGUI` の先頭で `DrawOpenEditorHeader()` を呼ぶ（`SeDataEditor` 参照）。UI Toolkit 製なら `DataEditorHeader.Build(target)` を先頭に追加する
- **付け忘れ防止**: `Tests/Editor/DataEditorRegistryTests.cs` が `DDrive.*` の全 concrete `AssetDataBase` 派生型に登録があるかを検査する。専用エディタを持たない種別は同テストの `Exempt` に理由付きで明示する
- 現在の対応: SeData / BgmData → AudioEditor、VfxData → VfxEditor、ModelData → ModelEditor、AnimData → AnimEditor、AnchorData → AnchorEditor、AnchorGroupData → AnchorGroupEditor、ButtonSkinData → ButtonSkinEditorWindow(2026-09-11 追加)

### 8.1 アイコン行（2026-09-10）

同じヘッダーに **アイコン行**（サムネイル + 「フォルダから選択」「シーンから作成」「クリア」+ 撮影サイズ）が出る（`Editor/Inspector/AssetIconService.cs` / `AssetIconGui`）。`AssetDataBase.Icon` を Unity 内で用意するためのもの。

- **フォルダから選択**: 画像ファイルを選ぶ。プロジェクト内ならそのまま参照、外なら `Icons` フォルダへコピーして取り込む
- **シーンから作成**: SceneView（無ければ Main Camera）を**丸ごと撮影**して `IconCropWindow` を開く → ドラッグで正方形の範囲を決める（枠の移動・ホイールで拡縮・ダブルクリックで中央最大）→「この範囲でアイコンを作成」で 128 / 256 / 512 px（EditorPrefs に記憶）に縮小して PNG 保存 → Icon に割り当て。撮影は `EditorApplication.delayCall` で GUI の外で行う（`OnInspectorGUI` 内で `Camera.Render` すると URP の RenderPass と衝突して真っ黒になった — 2026-09-10 の実例）。「再撮影」で SceneView を動かした後に撮り直し、「画面からスクショ」は表示そのまま（ギズモ込み）を `ReadScreenPixel` で読む代替
- 保存先は **`Assets/GameData/Icons/<種別>/<アセット名>_<GUID 先頭 8 桁>_Icon.png`**（ツール管理、[10] §3。同じアセットは上書き）。取り込んだ画像は Editor 表示用に「ミップ無し・非圧縮・最大 512・NPOT そのまま」に設定する
  - **レビュー対応（2026-09-11）**: 以前は `<アセット名>_Icon.png` で、種別が同じで名前も同じ Data（別カテゴリの同名など）が同じ PNG を無条件に上書きし合っていた。GUID の先頭 8 桁を混ぜて一意にする。既に Icons 配下の PNG が Icon に割り当たっている場合は**そのファイルを上書き**するので、作り直してもファイルは増えず、旧名のアイコンもそのまま使い続けられる
- **自動生成（初期アイコン、2026-09-11）**: Data 種別ごとに提供元を登録し（`AssetIconService.RegisterSource<T>` = 元アセット / `RegisterRenderer<T>` = 描画）、Icon 未設定なら自動で PNG を作って割り当てる。元アセット: Model / Prefab / Vfx / Canvas の Prefab（`AssetPreview` の描画結果。バックグラウンド生成なので完了を待つ、最大 30 秒・同時 4 件）、Texture の Texture2D / Sprite（`textureRect` で切り出し）、ControlSkin / SliderSkin の Sprite（`DefaultIconProviders`）。描画: Material は元アセットが無くても表現できるので、実 MaterialManager の共有 Material を `MaterialThumbnailRenderer` で球に描く（`MaterialIconProvider`、Editor/Material）。入口は 3 つ: `AssetCreationService.Create` 直後（delayCall。configure で元アセットが入っている場合と Material）、Inspector アイコン行の「自動生成」ボタン（提供元が無ければ無効、ツールチップに由来）、メニュー `Tools/D-Drive/Generate/初期アイコンを生成(未設定の Data のみ)`（一括）。手で選んだアイコンは上書きしない（Icon が null のときだけ）
- Undo 対応。テスト: `Tests/Editor/AssetIconServiceTests.cs`（自動生成: Texture 元アセット / 未設定 / Material 描画）
- **レビュー対応（2026-09-11、一括生成の重さと Undo）**:
  - 一括生成（メニュー）は **PNG の書き出しだけ先に全件済ませ、インポート・Importer 設定・Icon 割り当てを最後にまとめて行う**（`AssetIconService.BeginBatch` / `EndBatch`）。1 件ごとに `ImportAsset` / `SaveAndReimport` していたときは、そのたびに `projectChanged` と `OnPostprocessAllAssets` が飛んで `AssetSearch` のキャッシュ（§9）と `MaterialIconProvider` の Registry が捨てられ、件数分の `FindAssets` が走っていた
  - `MaterialIconProvider` は `AssetRegistry` / `MaterialManager` / `MaterialThumbnailRenderer` を**静的に共有**し、`projectChanged` のときだけ作り直す（以前は 1 マテリアルごとに `EditorAnchorRegistry.Build()` = 12 型分の `FindAssets` + 全 Data ロード）。ドメインリロード前に `AssemblyReloadEvents.beforeAssemblyReload` で破棄する
  - Prefab の `AssetPreview` 待ちは **同時 4 件まで**（残りは順番待ち）。全件同時に 30 秒ポーリングすると AssetPreview のキャッシュが溢れてどれも生成されないことがあった
  - `AssetCreationService.Create` 直後の自動生成は `recordUndo: false`。アセット作成自体が Undo 対象でないため、Undo を積むと直後の Ctrl+Z が「アイコン割り当てだけ」を取り消して紛らわしかった
  - サムネイルの描き直し（Material Editor / ポップアップ）は **30fps 上限**に間引き、`MaterialAnim` 再生中でも**ウィンドウが非フォーカスかつターンテーブル off なら止める**

### 8.2 アイコン表示の拡張（2026-09-14、5-10）

**§8.1 で作った `AssetDataBase.Icon` を、AssetBrowser の一覧行と Project ウィンドウ（グリッド表示）のサムネイルにも出す。**

- **AssetBrowser**: `AssetBrowserWindow` の各行の先頭に `Image`（18×18）を追加（`MakeRowElement` / `BindRowElement`）。`row.Asset.Icon` があればそれを表示、無ければ `AssetPreview.GetMiniThumbnail(row.Asset)`（Unity 既定のアセットサムネイル。ScriptableObject のスクリプトアイコンなど）にフォールバックする。追加のポーリングや再描画フックは不要（`GetMiniThumbnail` は同期キャッシュから即値が返る既定アイコンのぶんだけを使うため）
- **Project ウィンドウ**: 全 Data 共通の `AssetDataInspector`（§8、`[CustomEditor(typeof(AssetDataBase), true)]`）に `RenderStaticPreview(assetPath, subAssets, width, height)` を追加。`target.Icon` が設定されていれば `AssetIconService.ScaleForPreview` で要求サイズに縮小して返し、未設定なら `base.RenderStaticPreview`（既定のスクリプトアイコン）に委ねる
  - **既存の種別独自 Inspector との共存**: `SeDataEditor` のように `AssetDataInspector` を継承しつつ `OnInspectorGUI` だけを上書きしている場合、`RenderStaticPreview` を上書きしていなければ基底クラスの実装がそのまま効く。新しい専用 Inspector を追加するときも同様（継承を切らない限り自動で効く）
  - `AssetIconService.ScaleForPreview(source, width, height)`: `Icon`（128/256/512px 想定）を crop 無しでそのまま `width×height` へ `Graphics.Blit` で縮小する（§8.1 の `CropAndSave` と同じ Blit パターンだが、切り出し矩形が無い分だけ単純）。`RenderStaticPreview` はズームレベルごとに違うサイズを要求してくるため、都度作り直す（呼び出し側管理。null は「元テクスチャ無し」）
- テスト: `Tests/Editor/AssetDataInspectorPreviewTests.cs`（Icon あり/無しでの `RenderStaticPreview`、`SeDataEditor` のような継承先での挙動、`ScaleForPreview` 単体）
- 要判断: 生成済みアイコンの解像度は 128〜512px 止まりなので、Project ウィンドウをズームで最大化した際にわずかに滲む。実害が出た場合は `AssetIconService.SizeChoices` の上限を上げるか、`RenderStaticPreview` 側でバイリニア以外の縮小方法を検討する（今回は据え置き）

### 8.3 各エディタの「＋ 新規作成」ボタン（2026-09-14、5-15）

**`[DataEditor]` 付きの全専用エディタのツールバーに共通の「＋ 新規作成」ボタンを置く。押すと `NewAssetDialog` をそのエディタの対応種別に固定して開き、作成完了で自動的にそのエディタへ切り替える。** AssetBrowser を経由せず、専用エディタからその場で命名規則どおりの新規アセットを作れるようにする(FR-1.5 の入口を増やす)。

- **共通ヘルパー**: `Editor/Inspector/NewAssetToolbarButton.cs`。§8 の `[DataEditor]` 反射処理をそのまま再利用する(新しい逆引き索引は作らない)
  - `GetDataTypes(Type windowType)`: windowType 自身に付いている `[DataEditor]` 属性を直接読み、Data 型一覧(重複除去)を返す。1 ウィンドウが複数種別を扱う場合(AudioEditorWindow = SE/BGM、MaterialEditorWindow = MaterialData/TextureData)は複数返る
  - `CreateToolbarButton(windowType)` / `CreateButton(windowType)`: 押すと `NewAssetDialog.Open(lockedTypes, onCreated)` を呼ぶ `ToolbarButton`(`UnityEditor.UIElements.Toolbar` の子用) / `Button`(単独配置用) を返す
  - `SwitchToCreated(windowType, created)`: 作成された Data を、`DataEditorRegistry.GetEntries(created.GetType())` から windowType 自身のエントリを探して `Open(created)` で開く。既存の Inspector の「エディターで開く」ボタン(§8)と全く同じ経路を通るため、専用の切り替えロジックを別に持たない。エントリが見つからない場合は警告ログのみで例外にしない([00] §0-4)
- **`NewAssetDialog.Open(Type[] lockedTypes, Action<AssetDataBase> onCreated)`**(新設オーバーロード。既存の `Open(AudioClip[] pendingClips = null)` はそのまま維持): 種別ドロップダウンの選択肢を `lockedTypes` に含まれる型だけへ絞る(候補が 1 つならドロップダウン自体を無効化)。作成が成功したら既存の Ping/Selection/AssetBrowser 更新のあとに `onCreated(asset)` を呼んでから閉じる。`GetWindow<T>()` は既存インスタンスがあると `CreateGUI` を呼び直さないため、ロック対象を static な受け渡し領域(`_pendingLockedTypes`/`_pendingOnCreated`)に置き、既存ウィンドウは一度 `Close()` してから開き直して確実に反映する
- **配置**: 既存の `BuildToolbar`(`Toolbar`)を持つエディタ(Anchor / Anchor Group / Anim / Model / VFX)はそこに追加。`CreateGUI` 内で直接 `Toolbar` を組んでいるエディタ(Canvas / Material / Material プレビュー / Prefab)も同様。トップレベルの `Toolbar` を持たなかったエディタ(Audio / Anim2D(既存の作成/編集モードトグルの Toolbar に相乗り) / Button Skin / Slider / Slider Skin / UI Tween / Material 変換)は新しく 1 行だけの `Toolbar`(または `MaterialConvertWindow` のみ `Toolbar` 1 個だけの行)を `CreateGUI` の先頭(スクロールしても隠れない `rootVisualElement` 直下)に追加した
- **対応済みの全 16 宣言**: AudioEditorWindow(SeData/BgmData)、VfxEditorWindow、ModelEditorWindow、AnimEditorWindow、Anim2DEditorWindow、PrefabEditorWindow、CanvasEditorWindow、MaterialEditorWindow(MaterialData/TextureData)、MaterialConvertWindow、MaterialThumbnailWindow、AnchorEditorWindow、AnchorGroupEditorWindow、ButtonSkinEditorWindow、SliderEditorWindow、SliderSkinEditorWindow、UiTweenEditorWindow
- テスト: `Tests/Editor/NewAssetToolbarButtonTests.cs`(`GetDataTypes` が既存の全 `[DataEditor]` ウィンドウで 1 つ以上の `AssetDataBase` 派生型を返すこと、既知の対応(Audio/Material 等)、`SwitchToCreated` が実際にウィンドウを開いて対象を切り替えること・対応が無くても例外にしないこと、`NewAssetDialog.Open(Type[], ...)` が種別ロックを内部状態に反映すること)
- 要判断: [28_manual_verification_phase5.md](28_manual_verification_phase5.md) の「5-15」節末尾を参照(MaterialConvertWindow / MaterialThumbnailWindow / SliderEditorWindow のような二次的な専用エディタにまで同じボタンを付けるべきか)

### 8.4 「仕様書を開く」ボタン + AssetBrowser の変更バッジ（2026-09-14、5-13/5-14）

- `AssetDataBase.SpecUrl` が設定されていれば、§8 のヘッダー(`DataEditorHeader.Draw` の次)に `SpecUrlGui.Draw` が「📄 仕様書を開く」ボタンを追加で描く(`Editor/Inspector/SpecUrlGui.cs`)。空なら何も描かない(ボタンを無効表示にはしない)。押すと `Application.OpenURL(SpecUrl)`
- `AssetBrowserWindow` のツールバーに `SpecCache.Updated` を購読するバッジ用 `ToolbarButton` を追加。`SpecAutoSync`(起動時自動取得、[27] §8.5)や `SpecSyncWindow` の「取得」が差分を見つけると「仕様書に変更 n 件」と表示され、押すと `SpecSyncWindow`(§6 のメニュー「仕様書と同期」)が開く。差分が 0 件なら非表示
- 詳細な同期の仕組み・列定義・データフローは [27_spec_sheet.md](27_spec_sheet.md) §8 を参照

### 8.5 NewAssetDialog の「仕様書から選ぶ」（2026-09-14、5-16）

`NewAssetDialog`(§8.3)の先頭に「仕様書から選ぶ」セクションを追加した。`SpecCache.GetUncreatedRows(...)`([27] §8.6)で
まだ Data の無い仕様書の行を検索付きで一覧表示し、選ぶと 種別/カテゴリ/識別子/表示名/備考(新設)/仕様リンク(新設)が
入力済みになる(手入力も従来どおり可)。「作成」を押すと `SpecSyncService.ApplyExtraFields`(新規 → Placeholder 作成
と同じ反映ロジック、コピペしない)で状態タグ/Assignee/Description/SpecUrl も設定され、作成後は `SpecCache.RecomputeDiff()`
(ネットへ行かず既存キャッシュから差分だけ再計算)でその行が一覧から消える。設定 URL 未設定時は案内文のみ。
詳細・要判断は [27_spec_sheet.md](27_spec_sheet.md) §4.5.1/§9.1 を参照。

## 9. AssetDatabase.FindAssets のキャッシュ（2026-09-11）

- **`AssetDatabase.FindAssets` を直接呼ばない。** 必ず `DDrive.Editor.AssetSearch.FindAssets(filter[, folders])` を通す（既定の検索範囲は `Assets` 配下）
- 背景: Unity 6000.3.13 の `FindAssets` は 1 回ごとに走査ファイル数に比例したネイティブメモリ（フォルダ指定なし ≈ 9.6 MB、`Assets` 配下 ≈ 5 MB、`Assets/GameData` + `Assets/DDrive` だけなら 0）を確保し、GC / `UnloadUnusedAssets` でも解放されずフレームをまたいで残る（ドメインリロードで戻る）。`EditorAnchorRegistry.Build` が 12 型分呼ぶためエディタウィンドウを開くたびに約 100 MB、`AnchorChainEditor.CollectRootToTarget` が SceneView のハンドル描画のたびに呼ぶため操作するほど増え続けていた
- `AssetSearch` は同じ (filter, folders) の結果をキャッシュし、`EditorApplication.projectChanged` と `AssetPostprocessor.OnPostprocessAllAssets`（import / delete / move）で無効化する。アセットを作った直後に同じフレームで検索するコード（`AssetCreationService.Create` など）は `AssetSearch.Invalidate()` を明示的に呼ぶ
- 2026-09-11 に `Assets/DDrive` 内の 22 か所を `AssetSearch` 経由に一括置換。合わせて `MaterialEditorWindow` の「再生成」から `EditorAnchorRegistry.Refresh` を外し、`projectChanged` で dirty を立てたときだけ再走査する（[06] A 実装メモ）
- **レビュー対応（2026-09-11）**: 置換漏れだった `AssetReorganizer.Reorganize`（GameData 全走査）と `AddressablesSync.RemoveEntriesUnder`（フォルダ配下の全 GUID）、`AssetIconServiceTests` を `AssetSearch` 経由に直した。テストは `AssetSearchTests`（キャッシュ／フォルダ別エントリ／**作成直後でも手動 `Invalidate` 無しで見つかる**＝`ImportWatcher` の自動無効化）

## 10. 依存関係グラフ（DependencyGraphService、チケット 5-5、2026-09-14）

§1 の「使用箇所検索」「依存関係ツリー」「未使用検出」（5-6 で UI 化）と、5-7（Preload 自動集計）・7-1（MissingAssetLog）が使う基盤。実装は `Assets/DDrive/Editor/Dependencies/`。

### 収集方式

- **テキストで `.unity`/`.prefab`/`.asset` をパースしない。** すべて Unity API 経由:
  - Data(`.asset`、`AssetDataBase` 派生): `AssetDatabase.LoadAssetAtPath` → `new SerializedObject(asset)`
  - Prefab: `AssetDatabase.LoadAssetAtPath<GameObject>(path)` → `GetComponentsInChildren<Component>(true)` → 各コンポーネントを `SerializedObject` で走査（`PrefabUtility.LoadPrefabContents` は使わない。読み取りだけなら `LoadAssetAtPath` で得た GameObject に直接 `GetComponentsInChildren` できることを実測で確認済み）
  - Scene(`.unity`): `EditorSceneManager.OpenScene(path, OpenSceneMode.Additive)` で開き、`GetRootGameObjects()` → `GetComponentsInChildren<Component>(true)` を走査してから **必ず** `EditorSceneManager.CloseScene(scene, removeScene: true)` で閉じる。現在アクティブなシーンには触れない（Additive で開いて閉じるだけなので `SceneManager.sceneCount` は前後で変わらないことをテストで確認）。**既に開いているシーン**（ユーザーが編集中のシーンを保存した直後の差分更新など）は `SceneManager.GetSceneByPath` で検出し、開き直さず・閉じずにそのまま走査する（2026-09-14 修正: `OpenScene` は開いているシーンを返すため、そのまま `CloseScene` するとユーザーのシーンを閉じてしまっていた）
- **検出対象の見分け方**: `SerializedProperty.propertyType == Generic` かつ `SerializedProperty.type` が `"AssetId\`1"`(`AssetId<TMarker>`。`AssetIdDrawer` と同じ実測値)または `"AssetRef"`(`AssetEvent.Target` 等の弱い型の相互参照)。子プロパティ `value`/`type`(`AssetId<TMarker>`)または `Id`/`Type`(`AssetRef`)から `(AssetType, ulong)` を読む
- **入れ子・配列・`[SerializeReference]`**: `SerializedObject.GetIterator()` + `NextVisible(true)` を先頭から最後まで辿るだけの素朴な深さ優先走査にした。配列(`vector`)も `[SerializeReference]` による多態も Unity 側が可視プロパティとして展開してくれるため、型ごとの特別扱いは不要（2026-09-14 時点でプロジェクト内に `[SerializeReference]` フィールドは無いが、将来追加されても収集ロジックの変更は不要なはず。要判断: 未検証)
- 例外は 1 ファイル/1 コンポーネント単位で警告 + スキップ（CLAUDE.md §0-4: 例外で止めない）。壊れた Prefab・開けないシーンがあっても全体は止まらない

### キャッシュ

- `Library/DDriveDeps/<guid>.json`(ファイル単位、`DependencyFileRecord` を `JsonUtility` でそのまま保存)。`Library/` は `.gitignore` 済みなのでコミットされない
- 起動時 / ドメインリロード時に自動で全再構築はしない（全 Scene の Open/Close は重く、デザイナーの作業を止めない方針(CLAUDE.md §0-4)に反するため）。`Library` が既にある限り `AssetPostprocessor` の差分更新だけで維持できる。**Library を消した直後や導入直後は空**なので、`Tools > D-Drive > Generate > 依存関係グラフを再構築` を一度手動で実行する必要がある(要判断)
- Unity 自身がエディタ起動時に外部変更されたファイルを再インポートし `OnPostprocessAllAssets` を呼ぶ挙動に相乗りしているため、Editor を閉じている間の外部変更(git pull 等)も次回起動時の差分更新で拾えるはず(要判断: 明示的な再検証パスは未実装)

### 差分更新

- `DependencyGraphPostprocessor`(`AssetPostprocessor`)が `imported`/`moved`/`deleted`/`movedFrom` を集めて `delayCall` で `DependencyGraphService.UpdatePaths(changed, deleted)` にまとめて渡す(`ImportRulePostprocessor` と同じ形)
- Play Mode 中(`isPlayingOrWillChangePlaymode`)は保留し、`playModeStateChanged` で Edit Mode に戻ってから実行する(Scene の Open/Close を Play Mode 中に行わない)
- テストからは `DependencyGraphPostprocessor.Suppress = true` で自動実行を止め、`DependencyGraphService.UpdatePaths` を直接呼ぶ(`ImportRulePostprocessor.Suppress` と同じ流儀)

### 公開 API（`DependencyGraphService`、5-6/5-7/7-1 が使う想定）

| メソッド | 用途 |
|---|---|
| `FindUsages(AssetType type, ulong id) : IReadOnlyList<DependencyReference>` | この ID を使っている場所一覧(参照元パス・オブジェクトパス・コンポーネント/Data 型名・プロパティパス) |
| `FindReferencesIn(string assetPath) : IReadOnlyList<DependencyReference>` | このアセット(.asset/.prefab/.unity)が参照する ID 一覧 |
| `FindUnusedIds() : IReadOnlyList<UnusedAssetId>` | どこからも参照されない ID 一覧(登録済み ID の全体は既存の `AssetIdLookup.GetAllDefinitions()` を再利用して求める) |
| `RebuildAll()` | 全再構築(メニュー用。全 Prefab/Scene を開閉するため重い) |
| `UpdatePaths(changed, deleted)` | 差分更新(Postprocessor・テストから) |

### メニュー

`Tools > D-Drive > Generate > 依存関係グラフを再構築`(`DDriveMenu.Generate`)。全再構築 + 件数ログのみ(UI は 5-6 で作る)。

### テスト

`Tests/Editor/DependencyGraphServiceTests.cs`。一時フォルダ(`Assets/DDrive/Tests/Editor/TempDepsGameData`)に Data/Prefab/Scene(`EditorSceneManager.SaveScene(activeScene, path, saveAsCopy: true)` で作成。**`EditorSceneManager.NewScene(..., Additive)` は Test Runner の「無題・未保存シーン」上では使えない**ため、アクティブシーンのコピー保存で代替した)を作り、`UpdatePaths` の結果と `FindUsages`/`FindReferencesIn`/`FindUnusedIds`/削除時の索引除去を確認。`DependencyGraphService.ResetInMemoryCacheForTests()` でテスト間のプロセス内キャッシュを分離し、`TearDown` で `UpdatePaths(null, 作成したパス)` を呼んで実プロジェクトの `Library` キャッシュに残骸(削除済みファイルを指す索引)を残さないようにしている。

### 実装メモ(2026-09-14、5-6: 使用箇所検索 / 未使用検出 / 依存ツリー UI + 安全な削除)

§1 の3機能と「安全な削除」を実装。すべて `Assets/DDrive/Editor/Dependencies/` に置き、5-5 の `DependencyGraphService` の上に薄く乗せてある(新しい索引は増やさない)。

- **使用箇所検索**: `UsagesWindow.Open(type, id, title)`。`AssetBrowserWindow` の行を右クリック →「使用箇所を表示」から開く。`FindUsages` の結果を `ListView` で表示し、`itemsChosen`(ダブルクリック/Enter)で `DependencyJumpService.Reveal` へ渡す。依存グラフが未構築(`CachedFileCount == 0`)なら警告 + 「再構築」ボタンを出す
- **依存ツリー**: `DependencyTreeWindow.Open(assetPath, label)`。木構造は `DependencyTreeBuilder.Build`(同ファイル、`DependencyTreeNode`)が `FindReferencesIn` を再帰的に辿って**事前にすべて組み立ててから** `UnityEngine.UIElements.TreeView.SetRootItems` に渡す(遅延展開はしていない。プロジェクト規模的に一括構築で十分速いという判断。要判断は末尾)。循環検出は「現在たどっている経路(祖先の assetPath 集合)に戻ってきたら打ち切り、`IsCycle=true` を立てる」方式。ダイヤモンド型の共有参照(循環ではない)は複数回展開されるため、同じ Data が複数箇所に出ることがある(意図した挙動)。解決できない(削除済み・存在しない) (Type,Id) は `IsUnresolved=true` で赤字表示
  - (Type, Id) → 実 Data の解決は `DependencyAssetResolver.Find`(`AssetIdLookup.GetAllDefinitions()` を流用。5-6 で新設した唯一の「もう1つの列挙ロジック」だが、依存ツリー・使用箇所検索の表示名解決・未使用一覧の3箇所で共有しているため重複はしていない)
- **未使用検出**: `UnusedAssetsWindow`(メニュー `Tools > D-Drive > 未使用アセット`、AssetBrowser ツールバーの「未使用...」からも開く)。`FindUnusedIds` の一覧をチェックボックス付き `ListView` で表示し、「選択項目を一括Archive」で `ArchiveTagService.SetArchived(asset, true)` を選択分だけ実行する(削除はしない)。ダブルクリックで対象 Data を選択
- **Archived タグ**: `ArchiveTagService`。[10_workflow.md] §3 の「Archived タグ→1リリース後に削除」の運用に、専用フィールドや TagCatalog が無いため `AssetDataBase.Tags` に予約タグ `"Archived"` を載せる最小実装(`SpecStatusTag` の `"State/…"` と同じ発想だが、ライフサイクルの軸が違うためプレフィックスは共有しない)。`Undo.RecordObject` + `SetDirty` 済み
- **安全な削除**: `SafeDeleteService.TryDelete(asset, type, requireGraphBuilt: true, scanCodeReferences: true)`。`AssetBrowserWindow` の行コンテキストメニュー「削除...」から呼ぶ。手順:
  1. `requireGraphBuilt` かつ `CachedFileCount == 0` → 「先に依存関係グラフを再構築してください」と案内して中止(`DeleteOutcome.GraphNotBuilt`)
  2. `FindUsages(type, id)` が 1 件でもあれば、参照元一覧(パス・オブジェクトパス・コンポーネント型.プロパティ名、最大20件)を出して中止(`DeleteOutcome.BlockedByUsages`)。この時点では確認ダイアログを一切出さない
  3. まだ Archived でなければ `ArchiveTagService.SetArchived(asset, true)` を実行(削除フローの一部としての2段階目。ここで最終確認をキャンセルしても Archived タグは残る = 「未使用・削除候補」の印として有効なまま。Undo 可能な通常の Data 変更なので Ctrl+Z で戻せる)
  4. `CodeReferenceScan.FindPossibleReferences` で「生成済み ID 定数(`SEID.PlayerSlash` 等)をコードから grep」した簡易チェック(見つかれば確認ダイアログの文言に警告を追加するだけで、削除は止めない。`DependencyGraphService` はコード側の参照を追わないため、これでしか拾えない。要判断は末尾)
  5. 最終確認ダイアログ → 確定で「カタログ登録解除(`AssetCatalog.Remove(id)`、新設)」「Addressables エントリ削除(`AddressablesSync.RemoveEntry`)」「アイコン PNG と Data 本体を `AssetDatabase.MoveAssetToTrash`(OS のゴミ箱。復元可能)」「`DependencyGraphService.UpdatePaths(null, [assetPath])` で依存グラフからも除去」の順で実行(`DeleteOutcome.Deleted`)
  - `AssetCreationService.Create`(作成: Data生成→ID発行→カタログ登録→Addressables登録)の**逆操作を同じ層に対称に用意する**という [09] §1 の設計メモどおりの構成
- **ダブルクリックジャンプ**: `DependencyJumpService.RevealAt(sourcePath, objectPath)`。`.asset` は Data を選択+Ping。`.prefab` は `GameObject.transform.Find(objectPath)`(空なら Prefab ルート自身)を選択+Ping。`.unity` はまず確認ダイアログ→`EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()`→`OpenScene(Single)`(既に開いていればそのまま使う)→ ルート名(`objectPath` の先頭セグメント。5-5 の Scene 収集がオブジェクトパスの先頭にルート名を含める仕様と対応)から `GetRootGameObjects()` で探し、残りを `Transform.Find` で辿って選択+Ping。ダイアログ2箇所(`ConfirmOpenSceneOverride`/`SaveModifiedScenesOverride`)・削除確認(`SafeDeleteService.ConfirmDialogOverride`/`InfoDialogOverride`)はすべてテストから差し替え可能な `public static` デリゲート(`NewAssetDialog.TestGameDataRootOverride` と同じ流儀)
- **AssetBrowser 側の配線**: 行の `VisualElement` に `ContextualMenuManipulator` を1つだけ付け(仮想化 `ListView` で使い回されるため)、対象は `BindRowElement` が差し替える `element.userData` から読む。ツールバーに「未使用...」ボタンを追加

要判断:
- **依存ツリーは事前に全展開**(遅延展開・仮想化 TreeView にしていない)。1個のアセットが数百件を再帰的に参照するような極端なケースでは初回表示が重くなり得るが、5-5 のコメント同様このプロジェクト規模(Scene 17・Data 数百件)では実測上問題にならなかった。将来重くなったら `TreeView` の遅延展開(`IsExpanded` に応じてその場で `FindReferencesIn` する)に切り替える
- **コード参照チェック(`CodeReferenceScan`)は grep ベースの best-effort**: 生成定数名(`ToConstantName` と同じ規則で組み立てた文字列)を `Assets/**/*.cs` から単純文字列検索するだけで、コメント内・文字列内・別名 using・部分一致等での誤検知/見逃しがあり得る。削除を止める判定には使わず、確認ダイアログの注意書きに留めた
- **「グラフ未構築」の判定は `CachedFileCount == 0` のみ**: 「古いかもしれない(Library はあるが最新の変更を反映していない)」ケースは検出できない(5-5 の要判断と同じ制約を引き継ぐ)
- **Scene ジャンプは自動テスト対象外**: `EditorSceneManager.OpenScene(Single)` はアクティブシーンを差し替える副作用があり、共有の Test Runner セッションを不安定にし得るため、`DependencyJumpServiceTests` は `.asset`/`.prefab` 分岐のみを自動テストし、Scene 分岐は手動検証([28_manual_verification_phase5.md](28_manual_verification_phase5.md) の「5-6」節)に委ねた
- **Archived というタグ名の予約語化**: `AssetDataBase.Tags` は本来 TagCatalog(未実装)からの選択制だが、`"Archived"` という文字列を予約語にした。将来 TagCatalog を実装する際はこの文字列を辞書から除外する(またはタグでなく専用の bool フィールドに移行する)必要がある
