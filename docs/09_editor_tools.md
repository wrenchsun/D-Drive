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

## 9. AssetDatabase.FindAssets のキャッシュ（2026-09-11）

- **`AssetDatabase.FindAssets` を直接呼ばない。** 必ず `DDrive.Editor.AssetSearch.FindAssets(filter[, folders])` を通す（既定の検索範囲は `Assets` 配下）
- 背景: Unity 6000.3.13 の `FindAssets` は 1 回ごとに走査ファイル数に比例したネイティブメモリ（フォルダ指定なし ≈ 9.6 MB、`Assets` 配下 ≈ 5 MB、`Assets/GameData` + `Assets/DDrive` だけなら 0）を確保し、GC / `UnloadUnusedAssets` でも解放されずフレームをまたいで残る（ドメインリロードで戻る）。`EditorAnchorRegistry.Build` が 12 型分呼ぶためエディタウィンドウを開くたびに約 100 MB、`AnchorChainEditor.CollectRootToTarget` が SceneView のハンドル描画のたびに呼ぶため操作するほど増え続けていた
- `AssetSearch` は同じ (filter, folders) の結果をキャッシュし、`EditorApplication.projectChanged` と `AssetPostprocessor.OnPostprocessAllAssets`（import / delete / move）で無効化する。アセットを作った直後に同じフレームで検索するコード（`AssetCreationService.Create` など）は `AssetSearch.Invalidate()` を明示的に呼ぶ
- 2026-09-11 に `Assets/DDrive` 内の 22 か所を `AssetSearch` 経由に一括置換。合わせて `MaterialEditorWindow` の「再生成」から `EditorAnchorRegistry.Refresh` を外し、`projectChanged` で dirty を立てたときだけ再走査する（[06] A 実装メモ）
- **レビュー対応（2026-09-11）**: 置換漏れだった `AssetReorganizer.Reorganize`（GameData 全走査）と `AddressablesSync.RemoveEntriesUnder`（フォルダ配下の全 GUID）、`AssetIconServiceTests` を `AssetSearch` 経由に直した。テストは `AssetSearchTests`（キャッシュ／フォルダ別エントリ／**作成直後でも手動 `Invalidate` 無しで見つかる**＝`ImportWatcher` の自動無効化）
