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
| 安全な削除（2026-09-14、5-6。UE 風の確認画面に置き換え、同日追加） | 行の右クリックメニュー「削除...」（**複数選択にも対応**）。旧: `EditorUtility.DisplayDialog` ベースの確認ダイアログ → 新: `AssetDeleteWindow`(依存関係と削除後の扱いが分かる専用ウィンドウ、Unreal Engine の Delete Assets 相当)。参照元(Data/Prefab/Scene)・依存先(一緒に削除できるもの)を表示し、「参照を差し替えてから削除」「強制削除」「アーカイブのみ」「キャンセル」から選ぶ → 実行後は同じウィンドウが結果画面(ゴミ箱からの復元手順・コード参照・差し替え一覧)に切り替わる。実装は §10 の実装メモ参照 |
| お気に入り/最近 | ユーザーローカル（EditorPrefs）に保存 |
| ID 定数再生成 | ツールバーから 1 クリック。保存フックでの自動生成も設定可 |
| Validation | ⚠ボタンで全体検査 → 結果一覧（Error/Warning、FixAction ボタン付き）。行クリックで該当 Data へ |
| 一括操作 | 複数選択 → タグ付与 / カテゴリ移動 / Addressable グループ変更 |
| ダブルクリックで専用エディタを開く（2026-09-14） | 行をダブルクリック（or 選択中に Enter）→ その Data の専用エディタ（§8 の `[DataEditor]`）があれば主エディタを開いて対象にセット（Inspector の「エディターで開く」列の先頭ボタンと同じ）。専用エディタが無い種別は従来どおり Inspector で選択 + Ping。右クリックメニューにも同じ経路の「エディターで開く」（候補が複数ある種別はサブメニューで全候補）を追加 |

### 実装メモ

- 一覧のデータソースは AssetRegistry の Entries + 依存グラフキャッシュ。`AssetPostprocessor` で差分更新
- 検索インデックスは起動時に構築し EditorPrefs でなく `Library/DDrive/` にキャッシュ
- 大量アセット対応: ListView の仮想化（1 万件でスクロール 60fps）
- **ダブルクリックで専用エディタを開く（2026-09-14）**: `AssetBrowserWindow.OnItemsChosen`（`ListView.itemsChosen`。ダブルクリックと Enter キーの両方を通す）が `DataEditorRegistry.OpenDefault(AssetDataBase)` に委譲するだけで、§8 の「エディターで開く」ボタン列と全く同じ経路を通る（新しい開き方は増やしていない）。1 つの Data 型に複数の `[DataEditor]` が付いている種別（`MaterialData`＝Material Editor / Material 変換 / プレビューをポップアップ、`SliderSkinData`＝Skin Editor / Slider Editor）の**優先順位は既存の `Order` 昇順**（`DataEditorRegistry.GetEntries` が既に返している順）の先頭で、これを `DataEditorRegistry.TryGetPrimary` として公開した。既定の `Order`（未指定＝0）が「主エディタ」（`MaterialEditorWindow` / `SliderSkinEditorWindow`）に付き、変換・プレビュー等の副次ツールだけが明示的に大きい `Order` を付ける既存の運用にそのまま合致するため、この機能のために優先順位ルールを新設していない。対応する `[DataEditor]` が無い種別（例: 現状すべての Data 型に専用エディタがあるため無いが、将来増えた場合)はダブルクリックしても何も開かず、選択 + Ping のみ行う

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
- **1 階層目が種別フォルダであること(重要)**: `ImportRuleService.TryMatchRule` は `SourceAssets/` の**直下 1 階層目のフォルダ名**(`Ordinal`、大文字小文字も区別)だけを種別として見る。`SourceAssets/` の直下に置いたファイル・種別フォルダの**上**に別のフォルダを挟んだ場合(例: `SourceAssets/_Check/Se/...`)・綴りや大文字小文字が違うフォルダ(例: `SourceAssets/se/...`)は、どれも「種別フォルダではない」として無視される。カテゴリは種別フォルダの**下**に階層を作って表現する(例: `SourceAssets/Se/Player/Slash.wav`)
- **二重生成防止**: 新設の `AssetDataBase.ImportSourceGuid`(元ファイルの GUID。`[HideInInspector]`)で同定する。`ImportRuleService` は種別(DataType)ごとに `ImportSourceGuid → Data` の索引を 1 回だけ作って使い回す([06] A-2 実装メモの `MayaMaterialImporter.BeginBatch/EndBatch` と同じ狙い)。既に見つかった場合は何もしない(デザイナーの調整を上書きしない)
- **入口**: `ImportRulePostprocessor`(`AssetPostprocessor.OnPostprocessAllAssets`。`MayaModelPostprocessor` と同じく delayCall でまとめて `ImportRuleService.ProcessPaths` へ渡す。`deletedAssets` は見ない = 元ファイル削除時に Data を消さない)。テスト等からの抑止は `ImportRulePostprocessor.Suppress` / `ImportRuleService.AutoImport`
- **手動フォールバック**: `Tools/D-Drive/Generate/SourceAssets からインポートルールを再実行`(`ImportRuleService.ScanAll`)。AutoImport=OFF だった期間や機能導入前から置かれていたファイルを一括で取り込む
- **既定フォルダの作成(2026-09-14 追加)**: `Tools/D-Drive/Generate/SourceAssets の既定フォルダを作成`(`ImportRuleDefaultFolders.EnsureDefaultFolders`)が上記 9 種別のフォルダを `SourceAssets/` 直下に作る(既にあれば何もしない、冪等)。各フォルダ(と `SourceAssets/` 自体)に置き方を説明する `README.md` を入れる(git は空フォルダを保存できず `.meta` だけが残ると clone 先で Unity が警告して消してしまうための対策も兼ねる。Unity では TextAsset として読み込まれるだけの内容)。既存の `Shaders`/`Data` 等の他フォルダには触らない。README は既存があれば上書きしない(デザイナーが書き換えている可能性があるため)。種別一覧は `ImportRuleService.Handlers` から取るためハードコードしていない(Cutscene 追加時にここも自動で増える)
- **置き方を間違えたときの案内ログ(2026-09-14 追加)**: `SourceAssets/` 配下だがルールに合わないファイル(種別フォルダの直下・不明な種別フォルダ・対応外拡張子)を置くと、Data は作らずに Console へ `[DDrive] ImportRule 案内: ...` の `Debug.LogWarning` を出す(例外にはしない)。同じファイルパスはセッション内(ドメインリロードまで)で 1 回だけ警告し、`ProcessPaths` 1 回の呼び出し内ではカテゴリ(直下/不明フォルダ名ごと/種別ごと)にまとめて 1 行にする(`ScanAll` でまとめて大量に流し込んでも Console が荒れない)。`Shaders`/`Data`(Maya→Material 経路・サンプル資産が既に使っている既知の非対象フォルダ、`ImportRuleService.KnownNonTargetTypeFolders`)、フォルダ自体、隠しファイル(`.`/`~` 始まり)、`README.md`、`.meta` は警告の対象外
- **「欠落」表示**: 元ファイルを削除しても Data は消えない(参照フィールドが null になるだけ)。各種別の既存 Validator(`SeDataValidator`/`BgmDataValidator`/`TextureDataValidator`/`ModelDataValidator`/`AnimDataValidator`/`Anim2DDataValidator`/`PrefabDataValidator`/`CanvasDataValidator`/`VfxDataValidator`)がすでに「未設定(または Missing)です」の Error を出す実装だったため、新規 Validator は追加していない(AssetBrowser の Validation 一覧・⚠に既存のまま出る)
- **対象外の種別**(元ファイルが無い): Presentation / Shake / Haptics / UiTween / Anchor / AnchorGroup / ControlSkin(5-13 で別枠)。Cutscene は 6-10c で `IImportRuleHandler` を 1 つ追加する形で拡張する想定
- 要判断は [28_manual_verification_phase5.md](28_manual_verification_phase5.md) の「5-11」節末尾を参照(Anim2D の元ファイル解釈・複数テイク FBX 等)
- テスト: `Tests/Editor/ImportRuleServiceTests.cs`(ルーティング/カテゴリ抽出/9 種別の生成/再取り込みでの二重生成防止/元ファイル削除後も Data が残ることの確認/置き方を間違えた場合の案内ログが 1 回だけ出ること・既知の非対象フォルダでは出ないこと、18 件)、`Tests/Editor/ImportRuleDefaultFoldersTests.cs`(既定フォルダ+README 生成・冪等性・既存 README を上書きしないこと・生成された README がインポート対象/案内ログ対象にならないこと、4 件)
- **レビュー対応(2026-09-14、P5 レビュー第 1 弾、整理)**: `ImportRulePostprocessor.AddPending` の重複チェックが
  `List<string>.Contains`(O(n))で、大量ファイルの一括インポート/移動時に O(n²) になっていた
  (review1_editor.md #2)。順序を保つ `Pending`(List)はそのまま残し、重複判定だけ対になる
  `HashSet<string> PendingSet` で O(1) にした。`DependencyGraphPostprocessor.AddPending` も同じ問題
  (`PendingChanged`/`PendingDeleted` それぞれに対応する `PendingChangedSet`/`PendingDeletedSet` を追加)。

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
    ├─ Presentation Editor          ← 目玉機能につき最上段。2026-09-14 実装(5-4。PresentationEditorWindow。トラック編集(Kind ごとのレーン + D&D + 時間ドラッグ + 複製/削除)+ 統合プレビュー(モデル選択→ Anim/Vfx/Se/CameraShake/Haptic を実 Manager で同時再生)+ Signal レーン手動発火 + パラメータ上書き + 環境切替(ライト強度/背景色)。[08_presentation.md] 実装メモ参照)
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
    │   ├─ Shake / Haptics             ← 2026-09-14 実装(5-2c。CameraFxEditorWindow。1 ウィンドウで CameraShakeData/HapticsData 両方を扱う(AudioEditorWindow の SE/BGM と同じ設計)。波形編集は ValueDefDrawer の PropertyField のまま、読み取り専用の重ね描き波形(WaveformGraphGui)を追加。Shake は SceneCameraShakePreviewDriver が実 CameraFxManager で開いているシーンの Camera.main を直接揺らす(連打で Trauma 合成を確認可)。Haptics は EditorHapticsPreviewDriver が実 HapticsManager 経由で接続中のパッドを「Test on Pad」で振動。プリセット 10 種(Pulse/Rumble/Heartbeat/Explosion/Hit_Small/Hit_Large/Landing/Earthquake/Alarm/Engine)は `CameraFxPresets` が Undo 付きで適用。詳細は [16] 実装メモ参照)
    │   └─ 揺れ・振動確認用シーンを開く ← 2026-09-14 追加(5-2c。CameraShakePreviewSceneSetup。VfxPreviewSceneSetup と同じ流儀)
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
- 現在の対応: SeData / BgmData → AudioEditor、VfxData → VfxEditor、ModelData → ModelEditor、AnimData → AnimEditor、AnchorData → AnchorEditor、AnchorGroupData → AnchorGroupEditor、ButtonSkinData → ButtonSkinEditorWindow(2026-09-11 追加)、CameraShakeData / HapticsData → CameraFxEditorWindow(2026-09-14 追加)、PresentationData → PresentationEditorWindow(2026-09-14 追加、5-4)

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

**レビュー対応(2026-09-14、P5 レビュー第 1 弾)**:
- **P2: `CodeReferenceScan` が削除のたびに全 .cs を同期で全文読み込んでいた(review1_editor.md #3)**:
  走査対象を「自前コード」(`Assets/DDrive`・`Assets/Generated`)に限定した(`Assets/TextMesh Pro` 等の
  同梱サンプルコードは対象外。生成された ID 定数の利用箇所はこの 2 フォルダにしか無い前提)。加えて、
  ファイル内容を static な `Dictionary<string,(DateTime writeTimeUtc, string text)>` キャッシュに保持し、
  同じ Editor セッション内で複数回呼ばれても更新時刻が変わっていないファイルは再読み込みしないようにした
  (「一括削除で件数が増えるほど重くなる」問題への対応)。
- **整理: 削除確認でキャンセルしても Archived タグは残る旨をダイアログ文言に明記(review1_editor.md #8)**:
  下記の要判断(3番目の項目)自体は設計判断として変更していないが、`SafeDeleteService.BuildConfirmMessage`
  の確認ダイアログ本文に「キャンセルしても、削除候補として付けた Archived タグは残ります。」を追加した
  (これまでは docs のコメントにしか書かれておらず、実際のダイアログを見るデザイナーには伝わらなかった)。

要判断:
- **依存ツリーは事前に全展開**(遅延展開・仮想化 TreeView にしていない)。1個のアセットが数百件を再帰的に参照するような極端なケースでは初回表示が重くなり得るが、5-5 のコメント同様このプロジェクト規模(Scene 17・Data 数百件)では実測上問題にならなかった。将来重くなったら `TreeView` の遅延展開(`IsExpanded` に応じてその場で `FindReferencesIn` する)に切り替える
- **コード参照チェック(`CodeReferenceScan`)は grep ベースの best-effort**: 生成定数名(`ToConstantName` と同じ規則で組み立てた文字列)を `Assets/DDrive`・`Assets/Generated` 配下の .cs から単純文字列検索するだけで、コメント内・文字列内・別名 using・部分一致等での誤検知/見逃しがあり得る。削除を止める判定には使わず、確認ダイアログの注意書きに留めた
- **「グラフ未構築」の判定は `CachedFileCount == 0` のみ**: 「古いかもしれない(Library はあるが最新の変更を反映していない)」ケースは検出できない(5-5 の要判断と同じ制約を引き継ぐ)
- **Scene ジャンプは自動テスト対象外**: `EditorSceneManager.OpenScene(Single)` はアクティブシーンを差し替える副作用があり、共有の Test Runner セッションを不安定にし得るため、`DependencyJumpServiceTests` は `.asset`/`.prefab` 分岐のみを自動テストし、Scene 分岐は手動検証([28_manual_verification_phase5.md](28_manual_verification_phase5.md) の「5-6」節)に委ねた
- **Archived というタグ名の予約語化**: `AssetDataBase.Tags` は本来 TagCatalog(未実装)からの選択制だが、`"Archived"` という文字列を予約語にした。将来 TagCatalog を実装する際はこの文字列を辞書から除外する(またはタグでなく専用の bool フィールドに移行する)必要がある

### 実装メモ(2026-09-14、削除の確認画面: Unreal Engine の Delete Assets 相当)

ユーザー要望「削除するときに UnrealEngine のように依存関係などわかりやすく、消した後どうするかも」に対応。5-6 の `EditorUtility.DisplayDialog` ベースの確認(`SafeDeleteService.TryDelete`)を、`AssetBrowserWindow` の行コンテキストメニュー「削除...」の入口では専用ウィンドウ `AssetDeleteWindow` に置き換えた(`SafeDeleteService.TryDelete` 自体はテストとの後方互換のため削除していないが、新しい入口からは呼ばない)。すべて `Assets/DDrive/Editor/Dependencies/` に追加。

- **複数選択に対応**: `AssetBrowserWindow` の一覧の `ListView.selectionType` を `Single` → `Multiple` に変更。右クリックした行が現在の選択に含まれていれば選択中の全行、含まれていなければ右クリックした行だけを削除対象にする(`AssetBrowserWindow.DeleteRows`)。
- **分析ロジックと UI を分離**: 削除の判断ロジック(参照元の分類・依存先の判定・実際の削除/差し替え/アーカイブの実行)を UI(`AssetDeleteWindow`)から独立した static サービスに切り出した。EditMode テストはウィンドウを開かずにこれらのサービスだけを呼ぶ(既存の `UsagesWindow`/`DependencyTreeWindow`/`UnusedAssetsWindow` と同じく、この種の `EditorWindow` 自体は自動テスト対象外という前例に合わせた。要判断参照)。
  - `AssetDeleteAnalysisService.Analyze(IReadOnlyList<DeleteTarget>)` → `AssetDeleteAnalysis`(`AssetDeleteAnalysis.cs`): 削除対象一覧から (1) 参照元一覧(`Usages`。`FindUsages` を対象ごとに集めて `.unity`/`.prefab`/`.asset` の拡張子で `ReferenceFileKind` に分類し、参照元自身が削除対象のどれかなら `IsFromDeleteTarget=true` にする。UI はこれで「外部からの参照」と「削除対象どうしの参照(まとめて消すなら問題ない)」を分けて表示する) (2) 依存先一覧(`Dependencies`。削除対象全部の `FindReferencesIn` を (Type,Id) で重複排除しつつ集め、削除対象自身が依存先なら `IsAlsoDeleteTarget=true`、削除後にその依存先を使う場所が削除対象以外に一つも無ければ `WouldBecomeUnused=true`。「この削除でどこからも使われなくなるもの」の判定はこのフラグで、`FindUnusedIds` を呼び直すのではなく `FindUsages` の結果を削除対象パスで除外するだけで済ませている)
  - `ReferenceReplaceService.Replace(IReadOnlyList<ReplacementPlan>)` → `ReferenceReplaceResult`(`ReferenceReplaceService.cs`): (Type,OldId) への参照を (Type,NewId) に書き換える。対象は **Data と Prefab のみ**。Data は `SerializedObject` + `Undo.RecordObject` + `SetDirty`(Ctrl+Z で戻せる)。Prefab は `PrefabUtility.LoadPrefabContents` → 対象コンポーネントの `SerializedProperty` を書き換え → `SaveAsPrefabAsset` → `UnloadPrefabContents`(**通常の Undo スタックには乗らない**。要判断参照)。プロパティが `AssetId<T>`/`AssetRef` のどちらかを判定する処理は `DependencyGraphCollector.TryGetIdTypeFieldNames`(収集ロジックと共有。二重実装しない)に切り出した。**Scene 内の参照は書き換えない**(開いているシーンを勝手に保存しない方針、[08_data_import_mock.md] 系の既存方針と同じ)。書き換えられなかった Scene 側の使用箇所は `RemainingSceneUsages` として返す
  - `AssetDeleteExecutionService.Execute(DeleteExecutionRequest)` → `DeleteExecutionResult`(`AssetDeleteExecutionService.cs`): `DeleteAction`(`ArchiveOnly`/`ForceDelete`/`ReplaceThenDelete`。`Cancel` は何もしない)に応じて実処理を行う。`ArchiveOnly` は対象全部を `ArchiveTagService.SetArchived` するだけ。`ForceDelete` は参照の有無を無視して `SafeDeleteService.PerformDelete`(旧 `TryDelete` の非公開メソッドを `internal` に変更して再利用。カタログ/Addressables 登録解除 + `MoveAssetToTrash` + 依存グラフ更新の低レベル処理だけを取り出したもの)を呼ぶ。`ReplaceThenDelete` は `ReferenceReplaceService.Replace` を実行した後、削除対象ごとに(差し替え後も)`FindUsages` を取り直して**削除対象以外からまだ使われていれば削除せず Archive のみに留める**(Scene 参照が残っている場合はここで必ず引っかかる)。「一緒に削除」で選んだ依存先(`CascadeTargets`)も同様に、実際に削除された対象以外から使われていないかを実行結果を見てから判定する(`SkippedCascadeStillUsed` に残る)
  - `CodeReferenceScan.FindPossibleReferenceHits`(新設): 既存の `FindPossibleReferences`(確認文言用、ファイル一覧のみ)と同じ走査・キャッシュを共有しつつ、結果画面で「クリックでエディタを開く」ためのファイル:行(`Hit.RelativePath`/`Hit.Line`)を返す。`AssetDatabase.OpenAsset(obj, line)` で開く
- **UI(`AssetDeleteWindow.cs`)**: `ScrollView` ルート(CLAUDE.md §0-6)。分析画面(削除対象一覧(対象ごとに `CodeReferenceScan.FindPossibleReferences` の警告文言もここで表示。旧ダイアログ版と同じ「削除前に気づける」タイミングを維持) → 参照元一覧(Foldout で Data/Prefab/Scene ごと、ダブルクリック相当は「ジャンプ」ボタン→`DependencyJumpService`) → 依存先一覧(チェックボックス。`WouldBecomeUnused && !IsAlsoDeleteTarget && !IsUnresolved` のときだけ有効) → 削除方法(`RadioButtonGroup`。参照が無ければ「削除する」ボタン1つに簡略化)) → 実行後は同じウィンドウの内容を結果画面に差し替える(対象ごとの結果・コード参照ヒット(ファイル:行、クリックでエディタを開く)・差し替え一覧・手動で直す Scene 参照一覧・「依存関係グラフを再構築」/「Addressables 登録を同期」/「ID 定数を再生成」ボタン)。**モーダルダイアログは一切使わない**(このウィンドウ自体が確認画面のため。強制削除は専用チェックボックスで「実行」ボタンを有効化する方式にして `EditorUtility.DisplayDialog` を避けた。テストからダイアログ差し替えを気にする必要が無い)
- **依存グラフが未構築なら削除させない既存ガードを継承**: `AssetDeleteWindow` も `DependencyGraphService.CachedFileCount == 0` を見て、警告 + 「再構築」ボタンだけを出し、それ以外のセクションを組み立てない(`SafeDeleteService.TryDelete` の `requireGraphBuilt` と同じ考え方)

要判断:
- **「一緒に削除」はカスケードが1段のみ**: `WouldBecomeUnused` のチェックで選んだ依存先を削除しても、その依存先がさらに使っていたもの(孫依存先)の未使用判定は再計算しない。孫依存先も片付けたい場合は削除後にもう一度「未使用アセット」または安全な削除を実行する運用になる
- **Prefab の参照差し替えは Ctrl+Z で戻せない**: `PrefabUtility.LoadPrefabContents`→`SaveAsPrefabAsset` は通常の Undo スタックに乗らないため、Data 側(戻せる)と非対称になっている。結果画面ではその旨を明記するだけに留め、Prefab 用の独自 Undo 機構は実装していない(スコープ超過と判断)
- **`AssetDeleteWindow` 自体は自動テスト対象外**: `UsagesWindow`/`DependencyTreeWindow`/`UnusedAssetsWindow` と同じ前例に合わせ、EditMode テストは `AssetDeleteAnalysisService`/`ReferenceReplaceService`/`AssetDeleteExecutionService` のみを対象にした。ウィンドウの実際の見た目・操作感は [28_manual_verification_phase5.md] の手動確認に委ねる
- **複数選択の削除で置き換え先の候補選択 UI は「対象ごとに 1 つの `ObjectField`」**: 一括で同じ置き換え先を割り当てる UI(例: 「全部同じ置き換え先にする」チェックボックス)は無い。対象が多い場合は 1 件ずつ選ぶ必要がある

### 実装メモ(2026-09-14、5-7: Preload リスト自動集計 + シーンロード統合)

[10_workflow.md](10_workflow.md) §5 の設計(`ScenePreloadList` = シーンごとの「使用 ID 一覧」SO、依存グラフから自動集計)をそのまま実装。新規は `Assets/DDrive/Editor/Preload/`(`ScenePreloadAggregator.cs`/`ScenePreloadGenerator.cs`/`ScenePreloadBuildPreprocessor.cs`)と `Assets/DDrive/Runtime/Loading/`(`PreloadEntry.cs`/`ScenePreloadList.cs`/`ScenePreload.cs`/`SceneLoadingScreen.cs`)。詳細は [02_core_framework.md](02_core_framework.md) §5/§14 実装メモ。

- **集計ロジック(`ScenePreloadAggregator.Aggregate(rootPath)`)**: 5-6 の `DependencyTreeBuilder`(同ファイル `DependencyTreeNode.cs`)と全く同じ「`FindReferencesIn` を再帰的に辿り、祖先パスの集合に戻ってきたら打ち切る」方式を流用し、ツリーではなく `ulong Id` で重複排除したフラットな一覧(`List<PreloadEntry>`、ID 昇順)を作る点だけが違う。見つからない参照先(削除済み等)も `PreloadEntry` としては採用する(それ以上は展開しない)。ランタイム側で Address 解決に失敗した時点でどのみち警告+スキップされるため、集計側で弾く必要が無いという判断
- **`ScenePreloadList`**(Runtime asmdef、`ScriptableObject`): `SceneName`(表示用) + `IReadOnlyList<PreloadEntry> Entries`(`AssetType`/`ulong Id`/`DisplayName`)。`SetEntries` は Editor 専用の更新口(CLAUDE.md §0-5: Data は読み取り専用、書き換えは Editor API 経由のみ)。**Addressables には登録しない**(`ScenePreloadGenerator` 冒頭コメント参照。理由: (1) シーン専用のデータでカタログのような「ID→Data」解決の対象ではない (2) 実行中の `Assets/AddressableAssetsData/AssetGroups/*.asset` が別チケットの未コミット変更と衝突するのを避けるため)。シーン側の `SceneLoadingScreen` 等から `[SerializeField]` で直参照する運用(`UiLayerSettings`/`TuningTable` と同じ「Bootstrap 直参照、Addressables 対象外」パターン)
- **`.asset` の生成先**: `Assets/GameData/Preload/<シーン名>_PreloadList.asset`(`ScenePreloadGenerator.DefaultOutputRoot`)。既存があれば `Undo.RecordObject` + `SetEntries` + `SetDirty` で上書き、無ければ `AssetDatabase.CreateAsset` で新規作成(重複生成しない)
- **自動更新のタイミング(要判断で確定させた設計判断)**: 「シーン保存時」は付けなかった。5-5 が既に「全 Scene の Open/Close は重く、デザイナーの作業を止めない([00_requirements.md] 方針)ため保存の度の自動再構築はしない」と判断しており、Preload 集計はその上に乗っているため保存の度に実行するとさらに重くなる。代わりに次の 2 経路のみ:
  1. 手動メニュー `Tools > D-Drive > Generate > Preload リストを再集計(現在のシーン)` / `(ビルド設定の全シーン)`(`DDriveMenu.Generate`)
  2. ビルド前フック `ScenePreloadBuildPreprocessor`(`IPreprocessBuildWithReport`)。Build Settings の有効シーン全部を `ScenePreloadGenerator.GenerateForAllBuildScenes()` で一括更新してからビルドする。失敗しても例外を握り警告に留め、ビルド自体は止めない(CLAUDE.md §0-4)
  - どちらも依存グラフ(5-5)が未構築(`CachedFileCount == 0`)なら「Preload リストが空になる可能性があります」と警告するだけで処理は続行する
- **ランタイム側 API**: [02_core_framework.md](02_core_framework.md) §5/§14 参照。`IAssetRegistry.PreloadIdsAsync(ids, progress)` / `ReleaseIds(ids)` を新設し、既存の `IAssetLoader.PreloadAsync`(参照カウント式)をそのまま再利用。静的ファサード `DDrive.Runtime.Loading.ScenePreload`(`Bind`/`RunAsync`/`Release`)を `DDriveRuntimeBootstrap` から Bind/Unbind する
- **ロード画面の確認用実装**: 専用の CanvasData/Data 種別は起こさず(スコープ超過と判断)、`SceneLoadingScreen`(MonoBehaviour、`UnityEngine.UI.Slider`/`Text` を任意で受ける最小実装)を Runtime に追加した。デザイナー向けの本実装(Canvas/UiManager ベース)は別チケットで置き換えてよい前提

要判断:
- **シーン→Preload リストの対応付けは「シーンに置いた `SceneLoadingScreen` が直参照する」方式のみ**: `Catalogs[]`(Bootstrap 直参照配列)のような「シーン名→リスト」の中央インデックスは作らなかった(スコープ超過と判断)。複数シーンを一括で扱うロード画面(タイトル→複数シーンをまとめて Preload 等)が要る場合は、`DDriveRuntimeBootstrap` に `ScenePreloadList[]` を足して名前引きする仕組みを追加検討してほしい
- **`GenerateForAllBuildScenes` / `ScenePreloadBuildPreprocessor` は自動テスト対象外**: `EditorBuildSettings.scenes` は `ProjectSettings/EditorBuildSettings.asset`(git 管理下)を書き換えるため、テストが失敗して復元できなかった場合に実プロジェクトの設定を汚しかねない。自動テストは `ScenePreloadGenerator.GenerateForScene`(パス直接指定)のみとし、全ビルドシーン一括・ビルド前フックの経路は [28_manual_verification_phase5.md](28_manual_verification_phase5.md) の手動確認に委ねた
- **Preload の粒度は「Data(.asset)そのもの」まで**: Data が内部で持つ AudioClip/Texture/Prefab 等のサブアセットを個別に先読みする API は無い(Addressables が Data の依存関係として同じ/依存バンドルに含めてロードする前提)。極端に重いサブアセットを持つ Data がある場合、体感のロード時間短縮効果が薄い可能性がある(要実測)
- **`PreloadIdsAsync` で確保した参照カウントの解放漏れリスク**: `ScenePreload.Release` を呼び忘れる(例: `SceneLoadingScreen` を使わず `RunAsync` だけ直接呼ぶ)と `IAssetLoader` 内の参照が張られたままになる。`SceneLoadingScreen.OnDisable` では解放するが、他の呼び出し経路を追加する場合は対で `Release` を呼ぶ運用を徹底する必要がある
