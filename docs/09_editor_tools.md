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
| 新規作成した直後に専用エディタで開く（2026-09-17、U-16） | 「新規」/ D&D / Project の右クリック（§1.2）から作ったアセットは、作成後そのまま**その種別の専用エディタが開いて対象にセットされる**。経路は既存の `[DataEditor]` → `DataEditorRegistry.OpenDefault`（§8）で、ダブルクリックで開くのと同じ。専用エディタが無い種別は Ping + Inspector で選択状態になるだけ（無害なフォールバック）。実装は `Editor/Inspector/CreatedAssetOpener.cs`（`NewAssetDialog.CreateAsset` が呼ぶ。エディタの「＋ 新規作成」（§8.3）から開いた場合は従来どおりそのエディタへ切り替える） |
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
  | `Model/<カテゴリ>/` | Model | .fbx | `ModelData.Prefab`(FBX のインポート直後のルート GameObject を直接参照。ラッパー Prefab を挟む運用なら別途差し替える) + `ModelData.Slots`(2026-09-17 追加。`ModelSlotBinder.BuildSlots` が Renderer を走査し、各スロットの Material から作られた `MaterialData` の ID を割り当てる。MaterialData を作るのは同じ delayCall 列で先に走る `MayaModelPostprocessor`。見つからないスロットは None のまま残り、件数を警告に出す) |
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
- **置き方を間違えたときの案内ログ(2026-09-14 追加)**: `SourceAssets/` 配下だがルールに合わないファイル(種別フォルダの直下・不明な種別フォルダ・対応外拡張子)を置くと、Data は作らずに Console へ `[DDrive] ImportRule 案内: ...` の `Debug.LogWarning` を出す(例外にはしない)。同じファイルパスはセッション内(ドメインリロードまで)で 1 回だけ警告し、`ProcessPaths` 1 回の呼び出し内ではカテゴリ(直下/不明フォルダ名ごと/種別ごと)にまとめて 1 行にする(`ScanAll` でまとめて大量に流し込んでも Console が荒れない)。`Shaders`/`Data`/`Samples`(Maya→Material 経路・サンプル資産が既に使っている既知の非対象フォルダ、`ImportRuleService.KnownNonTargetTypeFolders`。`Samples` はサンプル素材の退避先 = [10_workflow.md](10_workflow.md) §3.3、2026-09-14)、フォルダ自体、隠しファイル(`.`/`~` 始まり)、`README.md`、`.meta` は警告の対象外
- **Model の Material スロット自動割当(2026-09-17、U-2。[39](39_usability_fixes_2026-09-17.md))**: FBX 配置で `ModelData` を作るとき、`Slots` も同時に埋める。`MayaModelPostprocessor`(FBX インポート・手動生成の直後)も `ModelSlotBinder.RebindForModelPath` で既存の `ModelData` の Slots を貼り直す。作り直しの導線は Model Editor の「元ファイル再読み込み」(U-3)。詳細は [05 A-4](05_model_animation.md) / [06 A-2](06_material_texture.md) の実装メモ
- **「欠落」表示**: 元ファイルを削除しても Data は消えない(参照フィールドが null になるだけ)。各種別の既存 Validator(`SeDataValidator`/`BgmDataValidator`/`TextureDataValidator`/`ModelDataValidator`/`AnimDataValidator`/`Anim2DDataValidator`/`PrefabDataValidator`/`CanvasDataValidator`/`VfxDataValidator`)がすでに「未設定(または Missing)です」の Error を出す実装だったため、新規 Validator は追加していない(AssetBrowser の Validation 一覧・⚠に既存のまま出る)
- **対象外の種別**(元ファイルが無い): Presentation / Shake / Haptics / UiTween / Anchor / AnchorGroup / ControlSkin(5-13 で別枠)。Cutscene は 6-10c で `IImportRuleHandler` を 1 つ追加する形で拡張する想定
- 要判断は [28_manual_verification_phase5.md](28_manual_verification_phase5.md) の「5-11」節末尾を参照(Anim2D の元ファイル解釈・複数テイク FBX 等)
- テスト: `Tests/Editor/ImportRuleServiceTests.cs`(ルーティング/カテゴリ抽出/9 種別の生成/再取り込みでの二重生成防止/元ファイル削除後も Data が残ることの確認/置き方を間違えた場合の案内ログが 1 回だけ出ること・既知の非対象フォルダでは出ないこと、18 件)、`Tests/Editor/ImportRuleDefaultFoldersTests.cs`(既定フォルダ+README 生成・冪等性・既存 README を上書きしないこと・生成された README がインポート対象/案内ログ対象にならないこと、4 件)
- **レビュー対応(2026-09-14、P5 レビュー第 1 弾、整理)**: `ImportRulePostprocessor.AddPending` の重複チェックが
  `List<string>.Contains`(O(n))で、大量ファイルの一括インポート/移動時に O(n²) になっていた
  (review1_editor.md #2)。順序を保つ `Pending`(List)はそのまま残し、重複判定だけ対になる
  `HashSet<string> PendingSet` で O(1) にした。`DependencyGraphPostprocessor.AddPending` も同じ問題
  (`PendingChanged`/`PendingDeleted` それぞれに対応する `PendingChangedSet`/`PendingDeletedSet` を追加)。

### 1.2 Project ウィンドウの右クリックから Data を作る（U-17、2026-09-17）

**Project でソースアセット（音源・画像・FBX・.anim・.prefab・.mat）を右クリック →「D-Drive/Data を作成/〜」で、その種類から作れる Data をその場で作る。** AssetBrowser を開かず、SourceAssets/ のフォルダ規約（§1.1）にも従わずに済む「3 つ目の入口」。

- **対応表は新設していない**。「どの拡張子が、どの Data 型の、どのフィールドに入るか」は §1.1 の `IImportRuleHandler` が既に宣言しているので、`SourceDataCreation`（`Editor/Creation/SourceDataCreation.cs`）が `ImportRuleService.Handlers` をそのまま読んで選択肢を組み立てる。**ImportRule にハンドラを 1 つ足せば（Cutscene 等）、この右クリックメニューの中身も自動で増える**（対応表が二重にならない）
- ImportRule に無いが元アセットから作れるものだけを `SourceDataCreation.ExtraOptions` に足している:

  | 選択するアセット | 作れる Data | 備考 |
  |---|---|---|
  | AudioClip(.wav/.mp3/.ogg/.aiff/.aif) | SeData / BgmData | `Clips[0]` / `LoopBody` |
  | 画像(.png/.jpg/.jpeg/.tga/.psd/.tif/.tiff/.exr/.bmp) | TextureData / ButtonSkinData / SliderSkinData | Texture は §1.1 と同じ（`TextureImportProfile` の規約に合えば Usage/Channel も入る）。Skin は Sprite を `Normal.OverrideSprite` に入れるだけで、他の状態は Skin Editor で足す |
  | .fbx | ModelData / AnimData / Anim2DData | Anim 系は埋め込み `AnimationClip` の先頭 1 本 |
  | .anim | AnimData / Anim2DData | |
  | .prefab | PrefabData / CanvasData / VfxData | どれにもなり得るので 3 つとも出す |
  | .mat | MaterialData | 既存の `UnityMaterialMigrator.Migrate`（[06] A 実装メモ。シェーダー変換 + テクスチャの TextureData 化）を呼ぶ。この種別だけ `Option.CreateOverride` 経由 |

- **無効化**: `[MenuItem(..., true)]` の validate で、選択中のアセットの拡張子に合わないメニューを灰色にする（判定は拡張子だけ。選択のたびに重いロードをしない）
- **カテゴリの推測**（`SourceDataCreation.ResolveCategory`）: `SourceAssets/<種別>/<カテゴリ...>/` にあれば §1.1 と同じ「種別フォルダから先」、それ以外は直上のフォルダ名 1 つ（`Assets/Art/UI/Btn.png` → `UI`）。あくまで推測なので、後から AssetBrowser でカテゴリを変えれば `Generate/GameData をカテゴリ配置に整理` がフォルダごと追従する
- **二重生成防止**: §1.1 と同じ `AssetDataBase.ImportSourceGuid`。同じ元ファイルから既に作られた Data があれば新しく作らず、警告を出して既存のものを開く
- **作成経路は 1 本**: 実体は `AssetCreationService.Create`（ファイル名・ID・カタログ・Addressables 登録・初期アイコン）だけを通る。作成後は U-16 と同じ `CreatedAssetOpener.Reveal` で専用エディタが開く
- `[MenuItem]` のパスは定数でなければならないため `Editor/Creation/AssetContextMenu.cs` に種別ぶんのメソッドが並ぶが、そこにあるのは宣言だけで判断は全て `SourceDataCreation` にある
- テスト: `Tests/Editor/SourceDataCreationTests.cs`（ImportRule の全ハンドラぶん選択肢ができること・Data 型の重複が無いこと・`AssetContextMenu` が宣言している 12 種別が全て引けること（`[MenuItem]` だけ増えて対応表に無い状態の検出）・`.mat` が専用経路を通ること・`ResolveCategory` のフォルダ規約）。実アセットを作るテストは既存の `AssetCreationServiceTests` / `ImportRuleServiceTests` に任せ、ここは純粋ロジックだけを見る

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
- **AssetBrowser 下部のプレビューバー（`AudioPreviewPane`）のレイアウト（2026-09-17、[39](39_usability_fixes_2026-09-17.md) U-12）**:
  「▶ 再生 / ■ 停止 / ループ / 速度」を 1 行に並べるバー。崩れていた原因は (1) `Toggle` / `Slider` は `BaseField` で、
  ラベル部に USS 既定の `min-width: 120px` が付くため「ループ」「速度」の 2〜3 文字でも 120px を占めてコントロールを
  右へ押し出す (2) 行が `flex-wrap: nowrap` のうえ速度スライダーが固定幅 180px で、バーが狭いと折り返さず右側が見切れる、の 2 点。
  ラベル幅を内容なりにし（`CompactFieldLayout.ShrinkLabel`、`Editor/Common/`）、行を `flexWrap` で折り返し可能にし、
  固定幅をやめた（タイトルは省略記号で縮む）。§7.1 の横幅 500px 下限を満たす
- **プレビューはウィンドウ内描画ではなく、確認用シーン / Prefab を開いて SceneView で実 Manager を駆動する**（2026-09-10 決定、全エディタ共通。Material = `MaterialPreviewBuilder`、Anim2D = `SceneAnimPreviewDriver` に SpriteRenderer + Animator の DontSave 物を渡す）。静的な補助表示（スライス矩形の輪郭など）はウィンドウ内でよい。**例外: Material（2026-09-11 決定）** — `MaterialThumbnailRenderer`（`PreviewRenderUtility`）で実 `MaterialManager` が生成した共有 Material を球/板/Cube に描くウィンドウ内サムネイルを併用する。時間軸を持たず再生経路を二重化しないため ADR-4 の趣旨は保てる。既定ライトのみで描くので、実シーン照明・ModelData 適用・並列比較は従来どおりシーン配置で確認する

### 2.1 「確認用シーンに配置」ボタンの共通化（U-4/U-5/U-6、2026-09-17）

**ユーザー報告**（そのまま）:
- 「PrefabEditor が確認用シーンに配置しても確認用シーンではなく現在開いているシーンにしか配置されない」（U-4）
- 「すべての確認用シーンに配置ボタンについて、右クリックでこのシーンに配置、このシーンに本配置（シーン移動しても消されない）ができるように。配置後シーンのカメラが配置場所から遠いこともあるので、配置後はシーンのカメラが配置したオブジェクトをちゃんと映すようにフォーカスすること」（U-5）
- 「PresentationEditor ちゃんと確認用シーンで開くこと」（U-6）

#### 共通部品（同じコードを各エディタにコピーしない）

| 置き場所 | 役割 |
|---|---|
| `Editor/Preview/PreviewPlacement.cs` | `PreviewPlaceMode`（`CheckScene` / `CurrentScene` / `CurrentScenePersistent`）と、配置の共通処理。`PrepareScene` / `Persist` / `PlacePrefabPersistent` / `Focus` / `IsPersistent` |
| `Editor/Preview/PreviewPlacementButton.cs` | UI Toolkit のボタン生成。`Create`（`Button`）/ `CreateToolbarButton`（`ToolbarButton`）/ 既存ボタンへの `AttachContextMenu` / 横幅対策の `ApplyNarrowWindowStyle`・`ConfigureRow` |

各エディタは `Action<PreviewPlaceMode>` を 1 つ渡すだけでよい。ボタンの挙動:

1. **左クリック** = `PreviewPlaceMode.CheckScene`。従来どおり「片付ける → 確認用シーンを開く → 配置する」
2. **右クリック** = コンテキストメニュー
   - 「このシーンに配置(一時・保存されない)」= `CurrentScene`。シーンを切り替えず、今開いているシーン / プレハブステージに従来どおり `HideFlags.DontSave` で置く
   - 「このシーンに本配置(シーンを移動しても消えない)」= `CurrentScenePersistent`
3. **配置後は必ず `PreviewPlacement.Focus`** で `SceneView.lastActiveSceneView.Frame(bounds)` する。SceneView が無い場合は `Debug.LogWarning` のみで落ちない（[CLAUDE.md] §0-4）。バウンズは `Renderer` と `RectTransform`（UI にはレンダラが無いため）の両方から作り、大きさ 0 のときは最低 0.5 の広がりを与える

#### 「一時配置」と「本配置」の区別（既存実装の調査結果）

現状の一時配置の目印は **①ルート名が `[D-Drive]` で始まる ②`HideFlags.DontSave`（子も `EditorPreviewRoots.MarkDontSaveRecursive` で付ける）** の 2 つで、`EditorPreviewSweeper` がこの 2 条件を満たすルートだけを掃除する（§2 の規約）。したがって **本配置 = この 2 条件をどちらも外すこと**であり、専用のフラグや別のクリーンアップ処理は追加していない。

- `PreviewPlacement.Persist(go, displayName)`: 一時プレビューを昇格させる。プレビュールート配下の子なら親から外し（`StageUtility.PlaceGameObjectInCurrentStage`）、**子まで再帰的に `hideFlags = None`**（`MarkDontSaveRecursive` の逆）、名前から `[D-Drive]` 接頭辞を外し（付いたままだと Sweeper に消される）、`Undo.RegisterCreatedObjectUndo` + `EditorSceneManager.MarkSceneDirty` + 選択 + フォーカス。**Manager / Pool が追跡しているインスタンスには使わない**（後で `Despawn` / `StopAll` に巻き込まれて消えるため）
- `PreviewPlacement.PlacePrefabPersistent(prefab, pos, rot)`: Prefab を持つ種別（Prefab / Model / Vfx / Canvas / Presentation のモデル）の本配置。プレビュー実体を昇格させる代わりに `PrefabUtility.InstantiatePrefab` で置き直す。**Prefab リンクが付くのでデザイナーが後から編集でき**、Manager / Pool の追跡にも入らない。本配置は「確認のための再生」ではなくシーンの作り込みなので、実 Manager を通さなくても ADR-4 の趣旨（Editor 専用の *再生経路* を作らない）には反しない
- 本配置した後、各エディタは自分の参照（`_previewRoot` / `_previewButton` / `_previewObject` / ハンドル）を手放す。「撤去」や `OnDisable` で本配置したものを消さないため
- Play Mode 中は本配置しない（シーンに保存されないため警告 + no-op）

#### 確認用シーンを開く関数（戻り値付きに統一）

`VfxPreviewSceneSetup` / `CameraShakePreviewSceneSetup` にも `CanvasPreviewSceneSetup` と同じ `public static bool TryOpenOrCreate()` を追加し、`OpenOrCreate()`（`[MenuItem]` 用）はその void ラッパーにした。3 つとも **既にその確認用シーンが開いていれば開き直さない**（保存ダイアログ・読み直しが無駄で、置いてあるプレビューも消えるため。`CanvasEditorWindow` 側に書かれていた同じ判定はここへ集約した）。ユーザーが保存ダイアログをキャンセルしたら `false` を返し、`PreviewPlacement.PrepareScene` が配置を中止する。

#### 対応したエディタ

| エディタ | 左クリックで開く確認用シーン | 本配置の実体 |
|---|---|---|
| Prefab Editor（U-4。**以前は確認用シーンを開かず、今開いているシーンにしか置いていなかった**） | `VfxPreviewScene` | `PrefabData.Prefab` |
| Presentation Editor（U-6。**「確認用シーンを開く」が開くだけでモデルを置かず、そのままでは確認できなかった**。「配置」ボタンも共通部品に） | `VfxPreviewScene` | `ModelData.Prefab` |
| Model Editor / Anim Editor / Anim2D Editor | `VfxPreviewScene` | Model/Anim は `ModelData.Prefab`、Anim2D はプレビュー物（`Anim2DPreviewObject`）を昇格 |
| VFX Editor（左クリックで確認用シーンを開いて **そのまま再生**するようにした） | `VfxPreviewScene` | `VfxData.Prefab` |
| Canvas Editor | `CanvasPreviewScene` | `CanvasData.Prefab` |
| Button Skin / Slider Skin / Slider / UI Tween（**以前はどれも確認用シーンを開かず、今開いているシーンに置いていた**） | `CanvasPreviewScene` | プレビュー用 Canvas ごと昇格（UI 要素だけ外すと描画できないため） |

- **対象外**: 「確認用シーンを開く」だけで何も配置しないボタン（`CameraFxEditorWindow` の揺れ・振動確認用シーン、`AnchorEditorWindow` / `AnchorGroupEditorWindow`）。置くものが無く右クリックメニューが意味を成さないため、従来の `ToolbarButton` のまま。揺れ・振動側は `TryOpenOrCreate` の追加だけ行った
- ▶ などから**暗黙に**呼ばれる配置（`ControlSkinPreviewSection.EnsurePreview` / Slider Skin → Slider Editor の受け渡し）は `CurrentScene` 固定にした。ボタンを押していないのに保存ダイアログが出てシーンが切り替わるのを避けるため
- 横幅（[09] §7.1）: 共通ボタンは `flexShrink=1` / `minWidth=0` / `whiteSpace=Normal` で、幅 500px でも切れずに折り返す。説明はラベルに足さず tooltip へ逃がす（右クリックの案内も tooltip に自動で付く）。ボタンを並べる行は `flexWrap=Wrap`

### 2.2 Anchor 系の SceneView 表示（基準の描画、U-24、2026-09-17）

**ユーザー報告**（そのまま）: 「Anchor のシーン表示で基準がわからないので LocalOffset だけではなく基準（原点）の座標もシーンに描画する」

SceneView に最終位置しか描かれておらず、そのオフセットが**何を起点にしているか**が読み取れなかった（[36 §5.4](36_manual_screenshot_list.md) の #19 / #20 / #23 がこれ待ちだった）。共通描画 `Editor/Preview/AnchorSceneHandles.cs` に次の 3 つを追加し、**Anchor Editor / VFX Editor（埋め込み Anchor）/ Anchor Group Editor（原点）の 3 か所が同じ経路を通す**。

| メソッド | 描くもの |
|---|---|
| `DrawOrigin` | 基準の位置に 3 軸（X=赤 / Y=緑 / Z=青）+ 球 + ラベル「基準: 〜 (x, y, z)」。2 行目に回転の基準（`FollowRotation`）。AnchorPoint の `SpawnOffset` があれば点線で継ぎ足す。戻り値 = `LocalOffset` の起点ワールド位置 |
| `DrawOffsetLink` | 基準 → 最終位置を実線で結び、中点に `LocalOffset (x, y, z) (距離 m)` |
| `DrawChain` | 連鎖の各中間段に 3 軸 + 「基準N: <アセット名>」+ 前段からの点線。最終段の手前までを描き、対象の基準位置を返す |

- 基準の定義（何を原点として描くか）は [21 §3.10](21_anchor_spec.md) の表を真とする。解決できなかったときは「⚠ … → ワールド原点」と明示し、「設定が効いていない」のか「そこが基準」なのかを見分けられるようにする
- 既存の描画（ランダム半径の円・移動/回転ハンドル・`SceneGuiOwner` の描画権）に**足す形**。描画権が他のウィンドウにあるときは従来どおり薄い目印だけで、基準は描かない
- Anchor Group Editor の「手置きの点に変換」（U-22）は [22 §3.7](22_anchor_group.md)

## 3. ID 参照 PropertyDrawer

- `SeIdRef` 等のフィールドを Inspector で「検索付きドロップダウン + プレビューボタン + Browser で開く」として描画
- 未登録/削除済み ID は赤表示 → プログラマーのモックコードでも設定ミスが即見える

## 4. 保存フック（AssetDataBase 共通）

保存時に自動実行（構想）: Version+1 / Author・UpdatedAt 記録 / 該当種別の Validator 実行（結果を Inspector 上部にバナー表示）/ 依存グラフ差分更新 / （設定時）ID 定数再生成。
**現時点で実装済みなのは Version+1 / Author・UpdatedAt 記録のみ**（チケット 6-3、2026-09-15）。Validator 実行・依存グラフ差分更新・ID 定数再生成の保存時自動化は別チケット（現状はメニュー手動実行、[11_tasks.md] 参照）で、この節の記述は将来の統合先を示す構想のまま残す。

### 4.1 バージョン記録（`VersionStampProcessor`、チケット 6-3、2026-09-15）

**ユーザー決定**: 保存時に自動更新・今の値だけ表示する。過去の履歴は git に任せる。データ形式（シリアライズ）は変えない（`AssetDataBase.Version`/`Author`/`UpdatedAt`/`ChangeNote` は既存フィールドのまま、フィールド追加・型変更はしない、[02_core_framework.md] §2）。

- **保存フック本体**: `Editor/Versioning/VersionStamp.cs` の `VersionStampProcessor`（`UnityEditor.AssetModificationProcessor` を継承し、Unity が名前で拾う `static string[] OnWillSaveAssets(string[] paths)` を実装）。
  - `paths` の各パスを `AssetDatabase.LoadMainAssetAtPath` で読み、`AssetDataBase` かつ `EditorUtility.IsDirty` な**実際に変更されたものだけ**を対象にする（未変更アセットは対象外）。
  - 対象ごとに `Undo.RecordObject`（バージョン表示行も含めて Ctrl+Z で戻せるようにする）→ `Version++` / `Author = Environment.UserName` / `UpdatedAt = 保存時刻`（ISO 8601、秒まで、ローカル時刻。`yyyy-MM-ddTHH:mm:ss`）→ `EditorUtility.SetDirty`。
  - フィールドを書き換えるだけで、ここから `AssetDatabase.SaveAssets()` 等を呼び直すことはしない（`OnWillSaveAssets` はこの直後にそのまま物理書き込みされるため、二重加算や無限ループにならない。1 回の保存につき 1 回だけ加算される）。
  - `ChangeNote` は触らない（手入力のまま、自動では消さない）。
  - **新規作成時の v1（2026-09-15 修正）**: `AssetDatabase.CreateAsset` は作成と同時に書き込み、作成直後のアセットは dirty にならないため、保存フックでは `0→1` にならない（EditMode テストで判明）。そこで `AssetCreationService.Create` が `CreateAsset` の直前に `VersionStampProcessor.StampNew(asset)` を呼び、v1・作成者・作成日時を記録する（抑止スコープ中でも付ける。初版の記録はノイズではないため）。この経路を通らない作成（Project ウィンドウの Create メニュー等）は v0 のままで、最初の編集 + 保存で v1 になる。

- **抑止スコープ（`VersionStampSuppression`）**: `using (VersionStampSuppression.Scope())` で囲むと、その間に走る保存では版数を上げない（参照カウント方式で入れ子安全）。**ツールによる一括処理（大量のアセットの版数が機械的に上がってノイズになるのを防ぐ）専用**で、以下の 5 箇所にだけ差し込んでいる（最小限の変更方針。他のツールに広げる場合はここに追記する）:
  1. `AssetCreationService.Create` の初期アイコン自動生成（`delayCall` 内、[09] §8.1）— 作成直後に Icon が自動で入って Version が 1→2 にならないようにする
  2. `AssetCreationService.RegisterExisting`（Validation の FixAction からのカタログ登録漏れ修正）
  3. `AddressablesSync.SyncAll`（Addressables 登録の一括同期。実際には Data 自体を dirty にしないので現状は保険）
  4. `SpecSyncService.ApplyChanged`（仕様書同期の「変更を反映」。抑止スコープの間に `AssetDatabase.SaveAssetIfDirty` も済ませている — 呼び出し元(`SpecSyncWindow`)がまとめて保存するのを待つと、その時点では抑止スコープが外れてしまうため）
  5. `AssetIdGenerator.Regenerate`（ID 定数再生成に伴う、未発行 ID の一括確定）
  - **既知の制約**: `AssetDatabase.SaveAssets()`（グローバル版）はパスの呼び出し元を区別しないため、上記のスコープ内で偶然「他の dirty な `AssetDataBase`」が同じ保存に乗ると、それも一時的に加算対象から外れる。実運用では上記 5 箇所はほぼ単発 or 同種アセットの一括処理でしか呼ばないため実害は小さいと判断し、パス単位の判定は行っていない（要判断: 将来問題になれば、抑止対象パスの集合を明示的に渡す設計に変える）。
  - `ImportRuleService`（インポート検知による自動生成）と Maya→Material 経由の新規作成は、上記 1 経由（`AssetCreationService.Create` を再利用している）でカバーされるため個別の抑止は不要。既存アセットを機械的に上書きする Maya 再インポート（`MayaMaterialImporter`）は対象外（Maya 側の実データ変更を反映するものなので、版数が上がるのは意図した挙動として扱う）。

- **表示（今の値だけ、履歴は持たない）**: `Editor/Inspector/VersionStampGui.cs`。`AssetDataInspector.DrawOpenEditorHeader()`（[09] §8）が `DataEditorHeader.Draw` の直後に `VersionStampGui.Draw(target)` を呼び、「エディターで開く」ボタンの直下に `v12 ・ yamag ・ 2026-09-15 14:03` の形式で 1 行表示する（`UpdatedAt` の ISO 8601 を `yyyy-MM-dd HH:mm` に整形。パースできなければ生の文字列をそのまま出す）。`ChangeNote` が入っていればその下にもう 1 行表示する。未保存（`Version <= 0`）なら「未保存(保存すると v1 になります)」と出す。UI Toolkit 製の専用エディタから使う場合向けに `VersionStampGui.Build(target)`（`VisualElement` 版、`DataEditorHeader.Build` と同じ位置付け）も用意している（現時点でどの専用エディタからも未使用。IMGUI の `AssetDataInspector` だけが実際に呼んでいる）。
- **AssetBrowser の一覧**（要望: 更新日時・更新者列、できれば並べ替え可能）: `AssetBrowserWindow` の各行に「更新者」「更新日時」の 2 ラベルを追加した(`MakeRowElement`/`BindRowElement`)。**並べ替えは未実装**——現状の一覧は単一列の仮想化 `ListView`（[09] §1）であり、列ヘッダーでのソートには `MultiColumnListView` への切り替えが必要。今回は最小限の変更で表示のみ足すに留め、ソート対応は次回チケットに回す。
- **テスト**: `Tests/Editor/VersionStampTests.cs`。保存で 1 回だけ加算 / 未変更アセットは加算しない / 抑止スコープ中は加算しない(入れ子安全) / Author・UpdatedAt の形式(ISO 8601、`VersionStampGui.FormatForDisplay` との対応) / Undo で戻せる、を検証。`Assets/DDrive/Tests/Editor/TempVersionStamp/` の一時アセットのみ使い、実 GameData・カタログ・Addressables には触れない(`AssetCreationService` を経由しないため Addressables 登録も発生しない)。
- **デザイナー向け表記**: [DesignerManual/asset-browser.html](DesignerManual/asset-browser.html) に「保存すると版数・更新者・日時が自動で入る」旨を追記（`Tools/SpecWeb/tools/build-manual.js` で HTML 本体を再生成）。

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
    // 2026-09-17(U-17/U-18/U-19): Project / Hierarchy の右クリックメニューもここに集約する。
    // Unity 標準の "Assets/" "GameObject/" 配下に D-Drive/ を 1 段だけ足す(トップレベルは増やさない)。
    public const string AssetsRoot       = "Assets/D-Drive/";
    public const string AssetsCreateData = AssetsRoot + "Data を作成/";
    public const string GameObjectRoot   = "GameObject/D-Drive/";
    public const int    GameObjectPriority = 12; // 50 以上にすると Hierarchy 右クリックで別グループへ落ちる
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
    │   ├─ Canvas + Panel と CanvasData を作成   ← 2026-09-17 追加(U-19。CanvasSetupService。§6.3)
    │   ├─ Rebuild Dependency Graph
    │   └─ Live Tuning Connect
    └─ Debug/
        ├─ Runtime Overlay
        └─ Missing Asset Report（発注リスト）
```

Project ウィンドウ（右クリック）と Hierarchy（右クリック）にも 2026-09-17 に入口を足した。

```
Assets/                         ← Project ウィンドウの右クリック(U-17、§1.2)
└─ D-Drive/
    └─ Data を作成/
        ├─ SeData を作成 / BgmData を作成            ← AudioClip
        ├─ TextureData を作成 / MaterialData を作成  ← 画像 / .mat
        ├─ ModelData を作成                          ← .fbx
        ├─ AnimData を作成 / Anim2DData を作成       ← .anim / .fbx
        ├─ PrefabData を作成 / CanvasData を作成 / VfxData を作成  ← .prefab
        └─ ButtonSkinData を作成 / SliderSkinData を作成           ← 画像(Sprite)
   ※選択中のアセットの拡張子に合わないものは validate で灰色になる

GameObject/                     ← Hierarchy の右クリック(U-18/U-19、§6.2)
└─ D-Drive/
    ├─ SeEmitter                                ← 標準プレハブ(DefaultPrefabs.EnsureSeEmitterPrefab)
    ├─ AnchorRig                                ← 標準プレハブ(無ければ DefaultPrefabs.CreateAnchorRigPrefab で生成してから配置)
    ├─ AnchorPoint(選択中の子に追加)
    ├─ 標準プレハブ...                          ← Assets/GameData/Prefabs 配下の全プレハブから選ぶ(GenericMenu)
    ├─ Canvas + Panel(CanvasData も作成)        ← U-19(§6.3)
    ├─ UiButton / UiSlider
    └─ 起動オブジェクト(DDriveRuntimeBootstrap) ← Tools 側と同じ BootstrapSceneSetup.PlaceInScene
```

### 6.1 メインツールバーの「マニュアル」ボタン（2026-09-14 追加）

**再生ボタンの近くからデザイナーマニュアルをブラウザで開けるようにする。** Unity 6.3 の公式 API `UnityEditor.Toolbars` の
`[MainToolbarElement]` を使う（旧 `Toolbar` への VisualElement 差し込み・リフレクションによる内部 API 利用は行わない）。
実シグネチャは isuzu-unity MCP の `reflect_find_type` / `execute_code` で `UnityEditor.CoreModule` /
`UnityEditor.EditorToolbarModule` から確認済み。再生ボタン自体（`UnityEditor.Toolbars.PlayModeButtons.Create`）は
`path="Play Mode Controls"` / `defaultDockPosition=Middle` / `defaultDockIndex=0` で、Middle ドックの唯一の要素だったため、
`ManualToolbarButtons`（`Editor/Manual/ManualToolbarButtons.cs`）は同じ Middle ドックの `defaultDockIndex=1` に置いて
再生ボタンの右隣に表示する。1 つの `[MainToolbarElement]` メソッドが複数の `MainToolbarElement`（ボタン + ドロップダウン）を
返す構成は `PlayModeButtons` / `SubToolbarZone` と同じ形を踏襲したもの。

**初回は非表示（2026-09-14 実機で判明）**: Unity 6.3 はユーザー定義の `[MainToolbarElement]` を登録はするが既定で非表示にする
（`MainToolbar.GetAllElementDefinitions` に `CreateManualElements` が載っていることは確認済み）。表示を切り替える
`MainToolbar.ShowAll` / `SetDisplayedAll` は internal のため、コードからは強制表示しない。各自がメインツールバーの空いている所を
右クリックして「D-Drive/Manual」を表示にする（設定は各自の Editor レイアウトに保存される）。見つからなくても
メニュー `Tools/D-Drive/マニュアルを開く` で同じ処理を呼べる。

- ボタン（アイコン `EditorGUIUtility.IconContent("_Help")`、ツールチップ「デザイナーマニュアルをブラウザで開く」）: クリックでマニュアルのトップ（`Readme`）を開く
- 横のドロップダウン（アイコン `"icon dropdown"`）: `docs/DesignerManual/*.html` のページ一覧（表示名は各 HTML の `<title>` から動的に取得。`ManualPages.DiscoverPages`）+ 「Web 版を優先」トグル + 「ローカルのマニュアルを開く」
- 開く先の決定（契約）:
  1. `DDriveSpecSettings.Load()?.HumanAppUrl` が空でなく、かつ `ManualPrefs.PreferWeb`（EditorPrefs、既定 ON）が ON なら
     `<HumanAppUrl>?page=manual&p=<ページ名(拡張子なし)>`（既にクエリがあれば `&` で連結）を `Application.OpenURL`
     （`ManualUrlBuilder.BuildWebUrl` / `ResolveUseWeb`）
  2. それ以外はローカルの `docs/DesignerManual/<page>.html` を `file:///` URI で開く（`ManualUrlBuilder.BuildLocalFileUrl`）。
     ファイルが無ければ `Debug.LogWarning` のみ（例外で止めない）
- 実装: `Editor/Manual/`（`ManualUrlBuilder` = URL 組み立ての純粋関数、`ManualPages` = ページ一覧・表示名解決、
  `ManualPrefs` = EditorPrefs トグル、`ManualLauncher` = 副作用側、`ManualToolbarButtons` = ツールバー要素、
  `ManualMenu` = 同じ処理を呼ぶメニュー項目）
- メニューからも開ける: `Tools/D-Drive/マニュアルを開く`（`DDriveMenu.Root` 経由。単発アクションのため既存の
  「Asset Browser」等と同様に専用の定数は追加していない）
- テスト: `Tests/Editor/ManualUrlBuilderTests.cs`（URL 組み立ての純粋関数。空 URL / 既存クエリあり / ページ名エスケープ）、
  `Tests/Editor/ManualPagesTests.cs`（`docs/DesignerManual/*.html` の実ファイルと `DiscoverPages` の結果を照合、
  `<title>` からの表示名解決）

**2026-09-17 追記 — プログラマーマニュアル（`docs/ProgrammerManual/`）の配線を追加**: `docs/ProgrammerManual/` に
13 ページ + `style.css`（`Readme` / `getting-started` / `concepts` / `bootstrap` / `audio-api` / `vfx-api` /
`model-anim-api` / `presentation-api` / `ui-api` / `handle` / `net-api` / `rules` / `extending`）が新設されたのに合わせ、
`ManualPages` をデザイナー/プログラマーの 2 マニュアルで共通化した。

- `ManualPages` に `ManualKind { Designer, Programmer }` を追加し、`GetManualFolder` / `DiscoverPages` /
  `ResolveDisplayName` / `StripManualSuffix` に `ManualKind kind = ManualKind.Designer` 引数を追加した
  （既定値をデザイナーにしているため、既存の呼び出し側はソース変更なしで従来どおり動く）。
  フォルダ相対パスと `<title>` 接尾辞（`" | D-Drive プログラマーマニュアル"`）を `ManualKind` ごとに切り替える。
  トップページ名は両マニュアルとも共通で `Readme`（`ManualPages.TopPageName`）
- **プログラマーマニュアルは Tools/SpecWeb（発注ツール）に配信されていない**ため、`ManualLauncher` に
  Web/ローカルの分岐を持つ `OpenPage` 系とは別に、常にローカル HTML を開く専用の
  `OpenProgrammerTop` / `OpenProgrammerPage(pageName)` を追加した（内部的には既存の
  `OpenLocal(pageName, kind)` に `ManualKind` 引数を足したものを呼ぶだけで、デザイナー側の
  Web-優先の分岐（`ManualUrlBuilder.ResolveUseWeb` / `ManualPrefs.PreferWeb`）には影響しない）
- メニュー: `Tools/D-Drive/プログラマーマニュアルを開く`（`DDriveMenu.Root` 経由、`ManualMenu` に追加。
  デザイナー側の「マニュアルを開く」はそのまま）
- メインツールバーの「マニュアル」ドロップダウン（`ManualToolbarButtons.CreatePagesDropdown`）に区切り線を挟んで
  「プログラマーマニュアル/…」サブメニュー（トップ + 各ページ）を追加した。デザイナー側の既存項目
  （ページ一覧 / 「Web 版を優先」/ 「ローカルのマニュアルを開く」）は変更していない。プログラマーマニュアルは
  常にローカルのため「Web 版を優先」に相当する項目は無い
- テスト: `Tests/Editor/ManualPagesTests.cs` に `ManualKind.Programmer` を渡す対の照合テストを追加
  （`docs/ProgrammerManual/*.html` の実ファイルと `DiscoverPages` の結果、`<title>` からの表示名解決）。
  デザイナー側の既存テストはそのまま残している
- `Tools/SpecWeb/tools/build-manual.js` はこの変更の対象外（`docs/DesignerManual` のみを正本として扱う既存の
  ドリフト検出のまま。`docs/ProgrammerManual/Readme.html` 冒頭の運用メモにも「SpecWeb への配信対応は範囲外」と明記している）

### 6.2 Hierarchy の右クリックから基本オブジェクトを置く（U-18、2026-09-17）

**`GameObject > D-Drive > …`（= Hierarchy の右クリック）から、D-Drive の基本オブジェクトをシーンに置く。** 実装は `Editor/Creation/GameObjectMenu.cs`。

- **共通の約束**（全項目で守る）:
  - 右クリックした GameObject の子として置く（`MenuCommand.context` → `GameObjectUtility.SetParentAndAlign`）
  - `Undo.RegisterCreatedObjectUndo` で Ctrl+Z 一発で消せる。置いたオブジェクトを選択 + Ping し、シーンを dirty にする
  - コンポーネントを `AddComponent` で組まず、**標準プレハブ（`Assets/GameData/Prefabs/<ドメイン>/`、[10] §3.3）があるものは `PrefabUtility.InstantiatePrefab`** で置く。無ければ `DefaultPrefabs` が生成してから置く（生成は冪等）
  - `GameObject/` のメニュー項目は Unity の仕様で**選択中の GameObject の数だけ呼ばれる**ため、`ShouldRun(command)`（`context == Selection.activeGameObject` の呼び出しだけ通す）で 1 クリック 1 個にしている
- **項目**: SeEmitter / AnchorRig / AnchorPoint(選択中の子に追加) / 標準プレハブ...（`Assets/GameData/Prefabs` 配下の全プレハブを `GenericMenu` で列挙。新しい標準プレハブを足せば自動で出る） / Canvas + Panel(CanvasData も作成)（§6.3） / UiButton / UiSlider / 起動オブジェクト(DDriveRuntimeBootstrap)
- **置き場所の注意はログで案内する**（例外にしない、[00] §0-4）: AnchorPoint を AnchorRig の外に置いた・UiButton / UiSlider を Canvas の外に置いた場合は `Debug.LogWarning` で正しい置き方を出す
- **UiSlider の組み立て（Track + Fill + Handle）は `PreviewSliderFactory` と共有**する。配置用に別の組み立てコードを作らないため、同関数に `dontSave` 引数を足した（既定 `true` = 従来のプレビュー用、`false` = シーンに残す実オブジェクト）
- 起動オブジェクトは `Tools > D-Drive > Generate > 起動オブジェクト…` と同じ `BootstrapSceneSetup.PlaceInScene` を呼ぶだけ（既にあれば選択してカタログを再収集するので、何度押しても増えない）

### 6.3 Canvas + Panel + CanvasData の一発生成（U-19、2026-09-17）

**「Canvas を作る → Panel を足す → Prefab 化する → CanvasData を作る → Prefab 欄に入れる」を 1 操作にまとめる。** 実装は `Editor/Canvas/CanvasSetupService.cs`。入口は 2 つで、どちらも同じメソッド（`CanvasSetupService.CreateCanvasWithPanel`）を呼ぶ:

- `Tools > D-Drive > Generate > Canvas + Panel と CanvasData を作成` — アセットだけ作る（シーンには触らない）
- `GameObject > D-Drive > Canvas + Panel(CanvasData も作成)`（Hierarchy 右クリック） — 上に加えて、右クリックした GameObject の子として Prefab インスタンスを配置する

**並行経路を作らないための構成**:

1. 命名・カテゴリの入力と CanvasData の作成は既存の `NewAssetDialog.Open(Type[], Action<AssetDataBase>)`（§8.3 のオーバーロード。種別を CanvasData に固定）→ `AssetCreationService.Create`（ファイル名・ID・カタログ・Addressables 登録・`Flags.Load=Preload`）をそのまま通す
2. その `onCreated` コールバックで Canvas + Panel の Prefab を組み立てて保存し、`CanvasData.Prefab` に入れる（`Undo.RecordObject` + `EditorUtility.SetDirty`、[00] §0-5）
3. `CreatedAssetOpener.Reveal`（U-16 と同じ）で Canvas Editor を開く。Hierarchy 経由の場合は最後にシーンへ配置して、そのインスタンスを選択状態にする

**生成される Prefab の形**（[07] A-2 / `DesignerManual/canvas-data.html` の「最小構成」に合わせる）:

```
<CANVAS_カテゴリ_識別子>   RectTransform + Canvas(ScreenSpaceOverlay) + CanvasScaler(1920x1080, Match 0.5) + GraphicRaycaster
└─ Panel                   RectTransform(四辺ストレッチ) + Image
```

- ルートの Canvas は `UiManager.OpenData` が `overrideSorting = true` / `sortingOrder = Layer*100 + SortOffset` で使う（Canvas が無い Prefab は兄弟順で並べ替えるだけになる）ため、付けておくほうが `SortOffset` が効く
- 保存先は **`Assets/GameData/Prefabs/Canvas/<CanvasData のファイル名>.prefab`**（ツール管理、[10] §3.3）。`SourceAssets/Canvas/` に置くと ImportRule（§1.1）が 2 つ目の CanvasData を作ってしまうので使わない
- シーンへ配置するときは `EventSystem` が無ければ作る（`CanvasPreviewSceneSetup.AddEventSystem` を共有。2026-09-17 に「作った GameObject を返す」形へ変え、呼び出し側で `Undo.RegisterCreatedObjectUndo` を積めるようにした）

## 7. ウィンドウレイアウト規約（拡縮前提）

**すべての `EditorWindow`（AssetBrowser 本体を除く各専用エディタ）は、ウィンドウが最小サイズまで縮小されてもコンテンツの下端まで到達できなければならない。**

- `CreateGUI()` では `rootVisualElement` に直接コンテンツを積まず、まず `ScrollView`（`ScrollViewMode.Vertical`、`flexGrow = 1`）を1つ生成して `rootVisualElement` に追加し、以降のセクションはすべてその `ScrollView` に積む
- セクションが増える設計（VfxEditor/ModelEditor のように環境切替・複数同時再生・パラメータ等を Foldout で積み重ねる形）は特に対象。`minSize` を大きめに設定して回避しない（ユーザー環境の画面解像度は前提にできない）
- 例外: `AssetBrowserWindow` のように `ListView` 自体が仮想化スクロールを持つ場合、その `ListView` に `flexGrow: 1` を与えれば足りる（二重にラップする必要はない）
- 発見の経緯: VfxEditor/ModelEditor（Phase 2, 2-4/2-6）でこの対応を忘れ、ウィンドウを小さくすると下部のセクション（イベント編集等）に到達できなくなる不具合があった。以後の新規エディタ実装ではこの規約を最初から満たすこと

### 7.1 横幅の下限（2026-09-17 追加）

**Windows の拡大/縮小 100%・ウィンドウ横幅 500px で、どのエディタも要素が見切れないこと。** 縦方向の §7 と対で、横方向の下限を定めたもの。

- **基準環境**: Windows の表示スケール（拡大/縮小）**100%**。`EditorGUIUtility.pixelsPerPoint` に依存したレイアウトを書かない
- **下限**: ウィンドウ横幅 **500px**。この幅でラベル・チェックボックス・ボタンの文字が切れたり、右端のコントロールが画面外に出たりしてはいけない
- やること:
  - 固定幅（`width` の直書き・`EditorGUIUtility.labelWidth` の大きな固定値）を避け、`flexShrink` / `flexWrap` で折り返す。横一列に詰め込む行は、狭いときに 2 段へ折り返す
  - ラベルが長い項目は短くするか、`tooltip` に逃がす。横に並べる必要のないものは縦に積む
  - `minSize` を 500px より大きくして回避しない（§7 と同じ理由）
- 確認のしかた: ウィンドウをフローティングにして横幅 500px まで縮め、上から下まで見て切れている箇所が無いことを見る。新規エディタ・レイアウト変更のときは毎回行う
- 発見の経緯: Button Skin Editor の「SE も鳴らす」が見切れていた（[39](39_usability_fixes_2026-09-17.md) U-10）。個別の 1 件ではなく全エディタ共通の条件として決めた
- **共通の小物**: `DDrive.Editor.Common.CompactFieldLayout.ShrinkLabel(field.labelElement)`（2026-09-17 追加）。
  `BaseField<T>`（`Toggle` / `Slider` / `TextField` …）のラベル部には USS 既定で `min-width: 120px` / `flex-basis: 120px` が付く。
  Inspector のように縦に積むときは列が揃って都合が良いが、**1 行に複数のフィールドを並べる行では 2 文字のラベルでも 120px を占め、
  チェックボックスやつまみを右へ押し出して見切れさせる**。横並びの行に入れるフィールドにだけこれを使う（縦に積むものは既定のまま）
- 2026-09-17 に直した箇所（1 巡目）: Button Skin / Slider Skin Editor の状態遷移行（`ControlSkinPreviewSection.Row()` を
  `flexWrap` 対応にし、この行のラベルを短縮 + tooltip 化。U-10）、Asset Browser 下部のプレビューバー（`AudioPreviewPane`。U-12）、
  UI Tween Editor のプリセット行（U-9 でボタンが 1 つ増えるため）。**全エディタの横断点検は U-27** で別途行う
- 2026-09-17 に直した箇所（2 巡目、[41](41_phase6_review_2026-09-17.md) P2-9）:
  - **`AssetDeleteWindow`（削除の確認ウィンドウ）**: `minSize` を `(620, 480)` → `(480, 360)` にした
    （500px を下回れず、そもそも下限を確認できなかった）。説明・パス・結果文言の `Label` は
    `WrappingLabel()`（`whiteSpace = Normal` + `flexShrink=1` / `minWidth=0`）に通し、
    横並びの行（対象・参照元・依存先・ボタン列・ヒット行）は `flexWrap = Wrap` にした。
    ScrollView は縦専用なので、折り返さないと右側が読めなくなる
  - **`PresentationEditorWindow` のタイムライン操作ヒント**: `rect.width - 12` の 1 行 `GUI.Label` で
    後半が切れていた。ルーラーの高さは固定で 2 行にできないため、**幅 620px 未満では短縮版を出し、
    全文は `tooltip` に逃がす**（§7.1 の「ラベルが長い項目は短くするか tooltip に逃がす」）

## 8. Inspector の「エディターで開く」ボタン（2026-09-09）

**専用エディタを持つ Data アセットは、Inspector の最上部に「〜で開く」ボタンが出る。既存・今後追加する種別すべてに適用する。**

> **Data 共通 Inspector の本文は UI Toolkit（2026-09-17、[39](39_usability_fixes_2026-09-17.md) U-14）**
> `AssetDataInspector` は `OnInspectorGUI` + `DrawDefaultInspector()`（IMGUI）で本文を描いていたが、
> `ValueDefDrawer`（[17_value_definition.md](17_value_definition.md) §5）は 2026-07-27 に `CreatePropertyGUI`（UI Toolkit）専用へ
> 書き直されていて `OnGUI` を持たない。IMGUI の Inspector から描かれると Unity は `PropertyDrawer.OnGUI` の既定実装に落ち、
> **`No GUI Implementation` というラベルだけ**を出す。`BgmData` の Fade In / Fade Out がこれで編集できなくなっていた
> （同じ理由で `CameraShakeData` / `HapticsData` / `Anim2DData` / `MaterialData` / `UiTweenData` / `ControlSkinData` も同症状）。
> - 直し方: `ValueDefDrawer` に IMGUI 実装を足し直す案は採らない。手動 Rect + `GetPropertyHeight` の IMGUI 版は
>   `AnimationCurve` のカーブエディタを開くとクラッシュする既知の不具合があり、それが UI Toolkit へ書き直した理由そのもの。
>   代わりに描く側を UI Toolkit にする（`AssetDataInspector.CreateInspectorGUI` が
>   ヘッダー（`IMGUIContainer` で従来の `DrawOpenEditorHeader`）+ `InspectorElement.FillDefaultInspector` を返す）
> - IMGUI の `PropertyDrawer`（`AssetIdDrawer`）は UI Toolkit の `PropertyField` が自動で `IMGUIContainer` に包むためそのまま動く
> - **派生クラスが `OnInspectorGUI` を上書きしている場合（`SeDataEditor`）は `CreateInspectorGUI` が `null` を返し、従来の IMGUI 経路に戻す**
>   （Unity は `CreateInspectorGUI` が null のとき `OnInspectorGUI` にフォールバックする）。判定はリフレクションで
>   「`OnInspectorGUI` の `DeclaringType` が `AssetDataInspector` か」を見るだけなので、新しい派生 Inspector でも付け忘れが起きない

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
- **配置**: 既存の `BuildToolbar`(`Toolbar`)を持つエディタ(Anchor / Anchor Group / Anim / Model / VFX)はそこに追加。`CreateGUI` 内で直接 `Toolbar` を組んでいるエディタ(Canvas / Material / Material プレビュー / Prefab)も同様。トップレベルの `Toolbar` を持たなかったエディタ(Audio / Anim2D(当時は既存の作成/編集モードトグルの Toolbar に相乗り。U-8(2026-09-17)でその作成タブ自体を `Anim2DCreateWindow` ポップアップへ分離したため、現在はトグルの無い `Toolbar` に「スプライトから新規作成…」と並んで乗っている) / Button Skin / Slider / Slider Skin / UI Tween / Material 変換)は新しく 1 行だけの `Toolbar`(または `MaterialConvertWindow` のみ `Toolbar` 1 個だけの行)を `CreateGUI` の先頭(スクロールしても隠れない `rootVisualElement` 直下)に追加した
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

**2026-09-17 修正（[39](39_usability_fixes_2026-09-17.md) U-15「仕様書 URL を設定したのに未設定と出る」）**:
「設定 URL 未設定」の判定だけが W-9（Web アプリ方式への移行、[32](32_spec_web.md) §5.1）以前の旧フィールド
`DDriveSpecSettings.SpreadsheetUrl` を見たままだった。取得・同期の実装（`SpecAutoSync` / `SpecSyncWindow`）は
すべて新フィールド `WebAppUrl` を見ているため、「仕様書と同期」で Web API URL を設定しても、このダイアログだけ
「仕様書の URL が未設定です」の案内文を出し続けていた。判定を `WebAppUrl` に統一した（`NewAssetDialog.RebuildSpecSection`。
旧フィールドは [32] §9 の要判断が済むまで残置）。テストも `NewAssetDialogSpecPickerTests` で `WebAppUrl` を使うよう更新。

### 8.6 Inspector の編集可否を分離（2026-09-17、[39](39_usability_fixes_2026-09-17.md) U-11）

**課題**: `AssetDataInspector`（§8）の本文は「全部出す」だけで、`AssetDataBase` の `Id` / `Version` / `Author` / `UpdatedAt` のように
コメントで「手編集しないこと」と書いてあるだけの項目も、実際には普通のテキストフィールドとして編集できてしまっていた。
`Version`/`Author`/`UpdatedAt` は §4.1 の保存フック（`VersionStampProcessor`）が保存ごとに書き換える値で、`VersionStampGui`
（ヘッダー、§8 直下の「v12・名前・日時」の行）に読み取り専用の要約が既に出ているにもかかわらず、本文側でも同じ値が
（別の見た目で）二重に、しかも編集可能な形で出ていたのが実害（うっかり書き換えると保存フックの記録と食い違う・
`Id` は `AssetIdGenerator` 等が前提にしている安定 ID なので書き換えると参照が壊れる）。

**方針**: 「全部出す/全部隠す」ではなく、**フィールド単位で編集可 / 読み取り専用（グレーアウト表示、値は見える）を宣言する**。
新設の `[InspectorReadOnly]`（`Foundation/Data/InspectorReadOnlyAttribute.cs`、`AssetDataBase` 派生型のフィールドに付ける）を
`AssetDataInspector` が反射で収集し、UI Toolkit 本文（`CreateInspectorGUI`）では対応する `PropertyField` を `SetEnabled(false)`、
IMGUI 本文（`OnInspectorGUI`、派生クラスが `OnInspectorGUI` を上書きしていない場合のフォールバック経路）では
`EditorGUI.DisabledScope` で同じ判定を使う。型は基底 `AssetDataBase` まで遡って収集するので、派生型のフィールドに
付けても効く。**Data クラス側にこの属性を付けるだけで反映され、Editor コード側の変更は不要。**

- `UiTweenEditorWindow` / `CanvasEditorWindow` / `MaterialEditorWindow` / `PrefabEditorWindow` が埋め込んでいる
  「Inspector(全フィールド)」セクション（`new InspectorElement(so)`）も、内部的に同じ `AssetDataInspector`
  （`editorForChildClasses=true` で拾われる）を経由するため、**この修正だけで自動的に反映される**（専用エディタ側の
  コード変更は不要）。これが「自作エディタに『Inspector（全フィールド）』があるものと無いものがある」の実体だった
  （無い側 = Audio/Vfx/Anim/Anchor 等は元々このセクションを持たず、種別独自の GUI か §8 の共通 Inspector そのままで
  十分という判断はそのまま変えていない）
- 種別独自の Inspector（`SeDataEditor` のように `OnInspectorGUI` を上書きしているもの）は自前で `DrawPropertiesExcluding`
  等を呼んでいるため、この機構の対象外（`CreateInspectorGUI` が `null` を返すのでそもそも通らない）。これらは
  そもそも `Id`/`Version`/`Author`/`UpdatedAt` を自前 GUI で表に出していない（`DrawOpenEditorHeader()` の
  `VersionStampGui` が読み取り専用の要約を出すのみ）ので、現状は追加対応不要

**現時点の割り当て（`AssetDataBase` 共通フィールド）**:

| フィールド | 分類 | 理由 |
|---|---|---|
| `Id` | 読み取り専用 | 安定 ID。`AssetIdGenerator`/ID 定数生成・参照解決の前提。書き換えると参照が壊れる |
| `Version` | 読み取り専用 | §4.1 の保存フックが保存ごとに +1 する。ヘッダーの `VersionStampGui` に同じ値の要約表示がある |
| `Author` | 読み取り専用 | 保存フックが保存ごとに記録する。同上 |
| `UpdatedAt` | 読み取り専用 | 保存フックが保存ごとに記録する。同上 |
| `ImportSourceGuid` | （対象外、既に `[HideInInspector]`） | ImportRule（§1.1）が二重生成防止に使う内部値。本文にそもそも出ない |
| `DisplayName` / `Description` / `Category` / `Tags` / `Icon` / `Assignee` / `SpecUrl` / `ChangeNote` / `Flags` / `Events` | 編集可能 | 人が入力・調整する項目（`Icon`/`SpecUrl` はヘッダーにも専用 GUI があるが、本文側の直接編集も残す） |

種別ごとの固有フィールド（`MaterialData.SourceMaterial` のような「インポート由来を記録するだけの値」等）は**対象にしない**
（**2026-09-17 決定**: 直接編集させたくないのは上表の `AssetDataBase` 共通 4 フィールドだけでよい、とユーザーが判断した。
`Id` ほど「編集すると即壊れる」わけではなく、種別ごとに判断が割れるため、種別固有フィールドは編集可能のままとする）。
将来この判断を変える場合は、その Data クラスのフィールドに `[InspectorReadOnly]` を付けるだけでよく、Editor コード側の
変更は要らない（変えたら本表に追記すること）。

テスト: `Tests/Editor/AssetDataInspectorReadOnlyFieldsTests.cs`（`Id`/`Version`/`Author`/`UpdatedAt` の `PropertyField` が
disabled になること、`DisplayName` 等の通常フィールドは有効なまま、`SeDataEditor` のように上書きされている場合は
`CreateInspectorGUI` が `null` を返すこと）。

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

#### レビュー対応（2026-09-17、[41](41_phase6_review_2026-09-17.md) P2-7 / P2-8 / P2-9）

- **P2-7: 結果画面の「コード参照のファイル:行（開くボタン）」が絶対に出なかった**。ヒット一覧を削除**後**に
  `CodeReferenceScan.FindPossibleReferenceHits` で取り直しており、条件の `r.Target.Asset != null` が
  `MoveAssetToTrash` 済みの fake null で常に false になっていた（上の「クリックでエディタを開く」が不動作）。
  → `AssetDeleteExecutionService` が `PerformDelete` の**前**に警告文言とヒット一覧をまとめて取り
  （`CollectCodeReferences`）、`DeleteExecutionResult.PerAssetResult.CodeReferenceHits`（新設）に入れる。
  ウィンドウはそれを表示するだけにした。これに伴い `CodeReferenceScan` を `internal` → `public` にした
  （テスト asmdef から結果を検証できるようにするため。`SpecDiffService.BuildExistingIndex` と同じ理由）
- **P2-8: 結果画面の文言が実際の操作と食い違っていた**。`Deleted=false` が一律
  「参照が残っているため削除せず、アーカイブ済みの印だけ付けました」で、ユーザーが明示的に
  「アーカイブのみ」を選んだ場合にも同じ文言が出ていた。→ `DeleteExecutionResult.Action`（新設）を見て
  `ArchiveOnly` のときは「『アーカイブのみ』を選んだため、削除はせずアーカイブ済みの印だけ付けました。」に出し分ける
- **P2-9: 横幅 500px で見切れていた**（§7.1 の「2 巡目」を参照）
- テスト追加（`AssetDeleteExecutionServiceTests`）: `ScanCodeReferences=true` の経路で
  `CodeReferenceHits` が削除前に取れていること（テストソース中の定数参照でヒットを再現）、
  実行した `DeleteAction` が結果に載ること

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

## 11. 各専用エディタ共通の「検証」セクション（2026-09-17、[39](39_usability_fixes_2026-09-17.md) U-13）

**専用エディタを持つ Data 種別には、すべて同じ「検証」セクション（`DataValidationSection`）を出す。**
それまでは Vfx / Anchor / AnchorGroup / Anim / Anim2D / Canvas / Presentation が「`Foldout` を作って種別の Validator を
`new` して `HelpBox` を積む」ほぼ同じコードを各自持ち、Audio / Shake · Haptics / Material / Model / Prefab / Button Skin /
Slider Skin には無い、というばらつきがあった（ユーザー報告「個別検証があるものとないものがある」）。

- **実装**: `Assets/DDrive/Editor/Validation/DataValidationSection.cs`
  - `DataValidationSection`（`VisualElement`）: 見出し「検証」の `Foldout`。`Bind(AssetDataBase)` で対象を設定、
    `Refresh()` で引き直す。見出しに件数（`検証: ✓ 問題なし` / `検証: エラー n / 警告 m`）を出し、
    `FixAction` 付きの結果には「修正」ボタンを付ける（AssetBrowser の Validation 一覧と同じ）
  - `DataValidationRunner`（UI 無しの実行部。テスト対象）: 対象 1 件に対して Validator を実行する
- **実行する Validator**: その Data の `AssetType`（`AssetIdDefinitionAttribute`）に一致するもの + 1 アセット単位で意味がある
  `IUniversalValidator`（`ValueDefValidator` / `AddressablesRegistrationValidator` / `NetModeUnsetValidator`）。
  **プロジェクト全体を 1 回まとめて見る Validator（`SpecDiffValidator` / `ContentHashCatalogCoverageValidator`）は除外する**
  （編集のたびに `Specs/*.json` の読み込みとカタログ全走査が走るため。Run All / CI には従来どおり出る）。
  発見規則は `CI.DiscoverValidators()` を共用する（Validator 発見の実装を二重に持たない。この 1 件のために `public` にした）
- **`ValidationContext`**: 既定は対象 1 件だけの軽い文脈。`includeSameTypeAssets: true` を渡すと同じ Data 型のアセットを
  すべて載せる（Anchor / AnchorGroup のように入れ子・循環を見る Validator 用。従来の各エディタの挙動をそのまま引き継いだ）
- **例外で止めない**（CLAUDE.md §0-4）: 1 つの Validator が例外を投げても `Debug.LogWarning` だけ出して他の結果は表示する
- **付けたエディタ**: Vfx（独自実装から置き換え）/ Anchor / Anchor Group（同）/ Audio / Shake · Haptics / Material /
  Model / Prefab / Button Skin / Slider Skin（新規に追加）
- **まだ独自実装のままのエディタ**: Anim / Anim2D / Canvas / Presentation。いずれも種別 Validator の結果に加えて
  **エディタ固有の追加検査**（Anim: StateName / BlendShape が対象モデルにあるか、Anim2D: 3 Validator の合成、
  Canvas: 個別の Fix ボタン）を出しており、そのまま置き換えると情報が減るため今回は触っていない
  （Presentation は同時に別チケット U-6 で改修中だったため見送り）。移行は後続で行う
- **VFX Editor で「検証」を展開しても何も出なかった件（同じ U-13 の別不具合）**:
  原因は検証セクション自体ではなく、その手前で例外が出て `RefreshValidation()` に到達していなかったこと。
  `VfxEditorWindow.RefreshAnchorUi()` が `_serializedTarget.FindProperty("AnchorId")` の結果をそのまま
  `BindProperty` に渡しており、対象アセットが破棄済み（削除・再インポート・Undo 後）だと `FindProperty` が `null` を返して
  `ArgumentNullException` になる（Editor.log に実際の記録あり）。`RefreshTargetUi()` / `OnUndoRedo()` は
  「Anchor → Params → 検証」の順に呼ぶため、Anchor で落ちると検証セクションが `Clear()` された空のまま残っていた。
  → (1) `FindProperty` の結果を null チェックしてから `BindProperty` する（null なら `Unbind`）、
  (2) `OnUndoRedo` は破棄済みの `SerializedObject` を使い回さず `EnsureSerializedTarget()` で作り直す、の 2 点で修正
- **テスト**: `Tests/Editor/DataValidationRunnerTests.cs`（null で落ちないこと / 種別 Validator が走ること /
  プロジェクト全体向け Validator が除外されていること / 1 アセット単位の `IUniversalValidator` は含まれること）

> **2026-09-17 追補（[41](41_phase6_review_2026-09-17.md) P2-6）**: 「プロジェクト全体を 1 回まとめて見る
> Validator か」の判定を `DataValidationRunner.IsProjectWide(IValidator)` として `public` にし、
> **アセット単位で Validation 結果を見る他の経路からも共用する**ようにした（定義はここ 1 箇所）。
> `ValidatorRegistry.RunAll`（Foundation）は `IUniversalValidator` の結果も「その時渡されたアセット」の
> `ValidationReport` にするため、全体結果を混ぜるとアセット単位の判定が壊れる（無関係なアセットが
> Placeholder 扱いになる）。利用先: `CI.RunValidation(includeProjectWideValidators: false)`
> → `SpecWebSender` の `isPlaceholder`、および `SpecDiffValidator.IsPlaceholder`（自前の
> `ValidatorRegistry` 実行をやめて `DataValidationRunner.Run` に寄せた）。詳細は
> [32](32_spec_web.md) の「実装メモ（2026-09-17、[41] editor 系レビュー対応）」。
