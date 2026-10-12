# 1010. 専用エディターの新デザイン移行（仕様書）

> 2026-10-12 起票。**ユーザー決定: サンプル E（[1008](1008_editor_ux_redesign.md) §3 E、試験版 v1.7.0-preview.3）のデザインを承認。全専用エディターをこのデザインに移行する。** 旧エディターはアーカイブして `Tools > D-Drive > Archive` から開けるようにし、新エディター間の導線（「○○ Editor で開く」等）は旧エディターを使わない。旧エディターの機能はすべて引き継ぐ（機能同等）。試験で分かったこと（保存の経路・自動保存・Data 切替・固定バー・未調整の折りたたみ・調整つまみ・速度の ×1.0 デテント）をすべて反映する。
> チケットは [1004](1004_tasks.md) の UX 節（UX-1〜UX-20）。実装は Sonnet のエージェントに委任し、1 チケット = 1 PR。設計の背景と現状調査は [1008](1008_editor_ux_redesign.md)。

## 1. 決定事項（2026-10-12）

| # | 決定 |
|---|---|
| D-1 | 新エディターの見た目・構造は **E** を正とする（ヒーロー / セグメント〔かんたん・標準・詳細〕/ 目的別カード / 検索 / 未調整の折りたたみ / 固定アクションバー / 調整つまみ / 視覚部品 / 空状態 / インライン検証）。E 固有の割り切り（VfxData のみ・Dark のみ確認）は本実装で解消する |
| D-2 | **機能同等が完了条件**。各エディターのチケットは「旧エディターの機能の棚卸し表 → 全行『あり』」を PR に含める（E でやった [1008](1008_editor_ux_redesign.md) §5 の対応表と同じ形式） |
| D-3 | **旧エディターは削除せずアーカイブ**: コードは残し、メニューだけ `Tools > D-Drive > Archive > <旧名>` に移す（`DDriveMenu.Archive` を追加）。Inspector の「〜で開く」は新エディターが `Order = 0`、旧エディターは `(旧) 〜で開く` で `Order = 100` として残す（`[DataEditor]` 対応表は**追加のみ**になる。[42](42_distribution.md) §5.9）。旧エディターへの機能追加はしない（バグ修正のみ）。削除は次の MAJOR の候補として [1001](1001_open_items.md) に載せる（実装時の補足 2026-10-12: 旧エディターの `[DataEditor]` 行はラベルと Order が変わるため、`editor-contract.txt` では『削除 1 行 + 追加 2 行』になる。§5.9 の弱い互換面として CHANGELOG に書いて許容する。以後の UX-3〜 も同じ扱い） |
| D-4 | **新エディター間の配線は新エディターだけを使う**: 「Anchor Editor で開く」「専用エディタで開く」「Prefab Editor で開く」などの `GetWindow<旧>` / `DataEditorRegistry` 経由の導線は、すべて新エディターへ向ける。旧エディター同士の既存の配線はそのまま（Archive の中で閉じる） |
| D-5 | **保存の経路**: デザイナーの編集の保存は `DDriveAssetSave.SaveDirty(target)`（版数・UpdatedAt が進む）。`SaveAllSuppressed` は機械処理（カタログ同期・新規作成直後 等）だけ。自動保存トグル（既定 OFF、EditorPrefs）・未保存のまま切替 / 閉じるときの確認・Ctrl+S でも保存できる tooltip は全エディター共通 |
| D-6 | **共通基盤はパッケージ側** `Packages/com.ddrive.core/Editor/Ux/`（asmdef は `DDrive.Editor` に含める。namespace `DDrive.Editor.Ux`）。試験版の `Editor/Prototypes/`（asmdef `DDrive.Editor.Prototypes`）は共通基盤へ移植したあと削除し、試験版タグ `v1.7.0-preview.*` も削除する（[1001](1001_open_items.md) の後始末） |
| D-7 | **互換性**: `DDrive.Foundation` / `DDrive.Runtime` の公開 API・シリアライズ形式・enum・Validator の重さに変更なし。Editor 契約（[42](42_distribution.md) §5.9、弱い互換面）の変更は「`DDriveMenu.Archive` の追加」「`[DataEditor]` 対応表の追加」「`Editors/` 配下のメニューが新エディターを指す」の 3 つで、いずれも CHANGELOG に書く。`editor-contract.txt` は追加のみで更新。**リリースは v1.8.0（MINOR）**、全エディターが揃ってから 1 回（途中の試験版は軽い経路で可） |
| D-8 | **検証**: 各チケットは Editor 専用なので EditMode green + コンパイル 0 + `ddrive_compat` 無変更 + **スクリーンショットの自己確認ループ（Dark と Light の両方、最低 2 周）** を完了条件にする。v1.8.0 のリリース時だけ run-ci 全段 |
| D-9 | マニュアル（`docs/DesignerManual/*.html`）は新エディターの機能説明に書き換える（機能のみ。「以前は〜」は書かない。[designer-manual の慣習](../CLAUDE.md)）。画像は新エディターのスクリーンショットに差し替える。旧エディターのページは `archive-*.html` に改名してトップの一覧から外す（リンクは「アーカイブ」節にまとめる） |
| D-10 | **実運用規模のサンプルデータで確認する（2026-10-12 ユーザー指示）**: Canvas・Material など要素や情報量が多い種別は、折りたたみで隠すだけでは解決にならない（旧 Canvas Editor で HUD 10 要素 + 埋め込み 3 つを開くと、要素ごとの Foldout と埋め込みの読み取り一覧が縦に延々と並んだ）。各チケットは **実際に使う形のサンプルデータ**（例: Canvas = HUD 相当〔要素 10 以上・埋め込み 3 以上・配線 5 以上・ElementFx 設定済みと未設定が混在〕、Material = Specific 20 項目以上・Pass/Keyword あり、Presentation = トラック 8 本以上、Anim = Clip 10 以上）を `Editor/Ux/Samples/` の素材か確認用データ（`Assets/GameData`、保存しない）で作り、新エディターで開いた **Dark / Light のスクリーンショットを完了条件に含める**。見通しの基準: (1) 初期表示が 1〜2 画面に収まり、どこに何があるか見出しだけで分かる、(2) 一覧は **1 要素 = 1 行**（要素名・種類のチップ・設定の要約〔「Appear: Pop / Idle: なし / Disappear: Fade」のような 1 行要約〕・▶・↺）で、行の展開は**その行だけ**、(3) 一覧の上に **絞り込み**（検索・「設定済みだけ」「未設定だけ」・種類のチップ）と **件数**（「10 要素中 設定済み 3」）、(4) 埋め込み・入れ子は読み取り一覧を展開表示せず **1 行の要約 + 「開く」**（子の中身は子のエディターで見る）、(5) 一括操作（全 Appear 等）は一覧の上に 1 行、(6) 20 行を超える一覧は `ListView`（仮想化）で描画し、ウィンドウのスクロールは一覧の外側 1 本だけ、(7) 500px 幅で崩れない。折りたたみは (1)〜(6) を満たしたうえでの補助 |

## 2. 共通基盤（`Editor/Ux/`、UX-1）

E の実装（`Editor/Prototypes/`）を種別非依存に整理して移す。クラス名は移植時に `Pd*` / `Pe*` の接頭辞を外す。

| 部品 | 元（Prototypes） | 内容・移植時の変更 |
|---|---|---|
| テーマ | `Theme/PrototypeD.uss` + `PrototypeE.uss` | `Editor/Ux/Theme/DDriveUx.uss` 1 本に統合。Light / Dark の変数（`--ux-*`）。**C# のインラインスタイル禁止**（U-27 の機械点検に「Ux 配下の `.style.` 代入禁止」の検査を足す） |
| ウィンドウ基底 | `PrototypeWindowBase` | `UxEditorWindow<TData>`（`TData : AssetDataBase`）。対象の追従 / 🔒 固定 / Data 切替 / ヒーロー / セグメント / 検索 / モード・未調整の記憶（EditorPrefs、キーは種別ごと）/ 固定バー / 保存（D-5）/ 空状態 / `[SerializeField]` での再コンパイル耐性 / Undo 同期 / 検証のインライン化。`ScrollView` + フッターの構成は U-27 を通す形に固定 |
| 欄の表 | `FieldGuide` / `FieldGuideEntry` / `VfxFieldGuide` | `FieldGuide<TData>`。欄ごとに Field / 平易なラベル / 一言説明 / 段（必須・よく使う・詳細）/ 目的カード / 検索語 / 既定値判定 / 行の種類（通常・視覚部品・つまみ・読み取り専用）。**種別ごとの表は各チケットで書く**（AssetDataBase の共通 14 欄は基底の表で共有）。段の根拠は Validator（Error にする欄 = 必須） |
| 行・カード | `PdRow` / `PdCard` / `GuidedField` / `FieldGuideUi` | `UxRow` / `UxCard`。段のバッジ、変更の色バー、↺、⚙（定義の詳細）、「？」（マニュアルの該当節。`ManualLauncher` にアンカー指定を足す）、未調整の折りたたみ、空カードの文言 |
| Data 切替 | `DataSwitcher` | ◀ [名前 ▾] ▶（検索付き AdvancedDropdown、最近 5 件、カテゴリ別）。種別非依存 |
| 固定バー | `PrototypeWindowBase.CreateFooter` | ▶ / ■ / リピート / **速度（×1.0 デテント + 値ラベル + クリックで戻す）** / 変更 N 件 / 自動保存 / 保存 ●。再生の有無は種別が決める（Tuning・Material など再生の無い種別は ▶ を出さない） |
| 視覚部品 | `DurationBar` / `AnchorPad` / `PeTunePanel` | `DurationBar`（長さ・フェード）、`AnchorPad`（位置・高さ。Vfx / Se / Presentation のトラック / Anchor で共用）、`TuneKnobs`（調整つまみ: 分類チップ・型別部品・↺・⚙・追加パレット。Vfx の Params、Material の Specific、Canvas の ElementFx パラメータ、Tuning の値で共用） |
| 検証 | `DataValidationSection` + インライン | 欄に結びつく結果は欄の下、結びつかないものは「その他の検証」。結びつけは文字列照合ではなく `ValidationResult` に欄名（`Field`）を持たせる方向で、Validator 側に**任意の**追加（既存の結果の重さ・Code は変えない） |
| スクリーンショット自己確認 | エージェントの手順 | `GUIView.GrabPixels` でウィンドウを画像化する共通ヘルパー `UxScreenshot`（Editor 専用、テスト・エージェント用）。Light / Dark の切替込み |

## 3. 移行の順序と各エディターの要点

順序は「共通基盤 → 使用頻度と欄の多さ → 残り」。各チケットの完了条件は §1 D-2 / D-8。

| 順 | チケット | 旧エディター | 要点（旧の機能のうち特に落としやすいもの） |
|---|---|---|---|
| 1 | UX-1 | — | 共通基盤（§2）。VFX を題材に E を移植して動かす |
| 2 | UX-2 | `VfxEditorWindow` | E をそのまま正式化（`VfxEditorWindow2` → 名前は `VfxEditor`）。E の対応表の全行 |
| 3 | UX-3 | `AudioEditorWindow`（Se / Bgm） | 波形・トリム・ループ点の編集（IMGUI の波形は `IMGUIContainer` で載せる）、3D サウンド確認、Mixer、`AudioPreviewPane`。AnchorPad を 3D の位置確認に |
| 4 | UX-4 | `CanvasEditorWindow`（+ ButtonWires / EmbedActive / Navigation グラフ） | 要素の自動収集、ElementFx 割当（プリセット一括・全 Appear/Idle/Disappear・プレハブモードで流す）、ボタン / スライダーの配線、埋め込み Canvas とその有効 / 無効、Navigation グラフ（`NavigationGraphView`）、FirstSelected、確認用シーン。欄が最も多いので「未調整の折りたたみ」と検索が効くこと |
| 5 | UX-5 | `PresentationEditorWindow` | トラックの一覧（タイムライン IMGUI）、Signal レーン、環境切替、統合プレビュー（確認用シーンのモデル配置）、トラックごとの Anchor / Params、Scope、ログ、SceneView の Anchor 表示、「専用エディタで開く」（→ 新エディターへ） |
| 6 | UX-6 | `AnimEditorWindow` | モデル情報、Clip / StateMachine / IK / BlendShape / イベント、イベントログ、ブレンド確認、Animator から選択、SE・VFX イベント |
| 7 | UX-7 | `Anim2DEditorWindow` + `Anim2DCreateWindow` | 作成ウィザード（B の雛形方式に置き換え可）、ウィンドウ内プレビュー（Sprite。ADR-4 の例外として現状維持） |
| 8 | UX-8 | `MaterialEditorWindow` + Convert + Thumbnail + `MaterialPassKeywordSection` | Specific を TuneKnobs に、Pass / Keyword、変換ウィンドウとサムネイルは別ウィンドウのまま（テーマだけ当てる） |
| 9 | UX-9 | `ModelEditorWindow` | 複数モデル並列表示、Material スロット（→ Material の新エディターへ）、Default Animation |
| 10 | UX-10 | `CameraFxEditorWindow`（Shake / Haptics） | プリセット 10 種、波形グラフ、`EditorHapticsPreviewDriver` |
| 11 | UX-11 | `AnchorEditorWindow` + `AnchorGroupEditorWindow` | 試し出し、既存ヒエラルキーから作る、SceneView ハンドル（`AnchorSceneHandles`）。AnchorPad を主部品に |
| 12 | UX-12 | `UiTweenEditorWindow` + `UiPresetGalleryWindow` | トラック、実要素への ▶、プリセットギャラリー（お気に入り・フィルタ・検索） |
| 13 | UX-13 | `SliderEditorWindow` / `SliderSkinEditorWindow` / `ButtonSkinEditorWindow` | プリセット 5 種、`ControlSkinPreviewSection` |
| 14 | UX-14 | `PrefabEditorWindow` | 小さいので UX-9 と同じ PR でよい |
| 15 | UX-15 | `TuningWindow`（IMGUI） | カテゴリ + 検索 + Tables タブ、Play 中の再読込通知。値は TuneKnobs。再生ボタン無し |
| 16 | UX-16 | `CutsceneDataEditor`（Inspector のみ） | ウィンドウ化はしない。Inspector のボタン列にテーマを当て、新エディターへの導線（Presentation 等）を向け直すだけ |
| 17 | UX-17 | `AssetBrowserWindow` / `NewAssetDialog` | 構造は変えず、テーマ（USS）・「開く」の向き先・空状態の文言を新エディターに合わせる |
| 18 | UX-18 | — | Archive メニュー（`DDriveMenu.Archive`）に旧エディターを並べ替え、`[DataEditor]` の `(旧)` ラベルと `Order = 100`、新エディター間の配線の総点検（`GetWindow<旧>` を grep して 0 にする。Archive 内の旧→旧は除く）、`editor-contract.txt` の更新（追加のみ）、マニュアル（D-9）、[09](09_editor_tools.md) §6/§8 の表 |
| 19 | UX-19 | — | 試験版の後始末: `Editor/Prototypes/` 削除、`vfx_sample` は `Editor/Ux/Samples/` へ移して空状態の「サンプルを作る」を全種別に（種別ごとのサンプル素材は無ければボタンを出さない）、タグ `v1.7.0-preview.*` 削除、[1001](1001_open_items.md) の行を消す |
| 20 | UX-20 | — | v1.8.0 リリース（run-ci 全段、[12](12_review.md) §7）。CHANGELOG の互換性節に D-7 の 3 点。MS2026 への案内（manifest を正式版へ） |

## 4. 各チケットの PR に必ず含めるもの

1. **機能対応表**（旧 → 新、全行「あり」。[1008](1008_editor_ux_redesign.md) §5 E の表の形式）を `docs/1010` §5 に追記
2. **`FieldGuide<TData>` の表**（全フィールド。編集不可は Id / Version / Author / UpdatedAt と `HideInInspector` だけ）
3. `[DataEditor(typeof(TData), "〜で開く")]`（新、`Order = 0`）と、旧の `"(旧) 〜で開く"`（`Order = 100`）
4. メニュー: 新は `DDriveMenu.Editors + "<名前>"`（旧と同じラベル）、旧は `DDriveMenu.Archive + "<名前>"`
5. EditMode green（U-27 機械点検・`DataEditorRegistryTests`・`EditorContractSnapshotTests`）、コンパイル 0、`ddrive_compat` 無変更
6. スクリーンショット（Dark / Light）を `docs/images/1010_<種別>.png` に、自己確認ループの記録（直した崩れ）を PR 本文に。**要素の多い種別は D-10 の実運用規模のサンプルデータで開いた画像を必須にする**（空に近いデータの画像だけでは完了にしない）
7. マニュアルの該当ページの更新（D-9）

## 5. 実装メモ（チケットごとに追記）

（UX-1 から順に、ファイル一覧・対応表・割り切った点を追記する）

**2026-10-12 追記（UX-1、共通基盤 `Editor/Ux` + 題材の `VfxEditor`）**

![新 VfxEditor（左 = Dark、右 = Light。標準モード）](images/1010_vfx.png)

- **作ったファイル**（`Packages/com.ddrive.core/Editor/Ux/`、`DDrive.Editor` の中・namespace `DDrive.Editor.Ux`・全型 internal）:
  `Theme/DDriveUx.uss`（D + E の USS を 1 本に統合。変数 `--ux-*` / Painter2D 用 `--ux-p-*`、`.ux-hidden`）/ `UxEditorWindow.cs`・`.Chrome.cs`・`.Rows.cs`（`UxEditorWindow<TData>`。対象・保存・再生・定期更新 / ヒーロー・固定バー・空状態・テーマ / カード・行・検証・絞り込み・折りたたみ・一括リセット）/ `FieldGuide.cs`（`FieldTier` / `FieldRowKind` / `FieldGuideEntry` / `FieldGuide<TData>`）/ `UxRow.cs`（`UxRow` / `UxVisualRow` / `GuidedField`）/ `UxCard.cs` / `DataSwitcher.cs` / `DurationBar.cs`（`UxLifeMode` を持つ）/ `AnchorPad.cs` / `TuneKnobs.cs`（`TuneKnobs` / `TuneKnobsRow` / `ITuneSource` / `TuneCategories`）/ `UxPalette.cs` / `UxShow.cs`（`Show(bool)` と `IUxSyncable`）/ `UxScreenshot.cs`。
  Vfx 側（`Editor/Vfx/`）: `VfxEditor.cs`（`VfxEditor : UxEditorWindow<VfxData>` + `VfxSample`）/ `VfxFieldGuide.cs` / `VfxTuneSource.cs`（`ITuneSource` の実装 = Params）/ `VfxEditorRows.cs`（Render・Anchor ツール・AnchorId・LightLayer・複数同時再生・長さの入力）。他: `ValidationResult.Field` + `WithField`（Foundation、追加のみ）、`VfxDataValidator` の Prefab / Params の結果に `Field` を付与、`ManualLauncher.OpenPage(page, anchor)`、`Tests/Editor/UxFoundationTests.cs`。
- **E からの対応**（`Editor/Prototypes/` は**消していない**。UX-19 で削除）:

| E（試験版） | 共通基盤 / VfxEditor |
|---|---|
| `PrototypeWindowBase` + `PrototypeEWindow` の汎用部分 | `UxEditorWindow<TData>`（種別が決めるのは `Guide` / `PrefsKey` / `ManualPage` / `CardDescription` / `CreateRow` / 再生の 3 メソッド / 主操作列の追加 / 検証の補助語だけ） |
| `PrototypeD.uss` + `PrototypeE.uss`（`pd-*` / `pe-*`） | `DDriveUx.uss`（`ux-*` に統一。`pe-empty` は空状態の `ux-empty` と衝突するため `ux-knobs-empty`） |
| `FieldGuide` / `VfxFieldGuide` | `FieldGuide<TData>`（共通欄 8 欄 = 管理情報カードは基底が持つ）/ `VfxFieldGuide`（固有欄だけ） |
| `PdRow` / `PdCard` / `FieldGuideUi`・`GuidedField` | `UxRow` / `UxCard` / `GuidedField`（入力部品の共通ヘルパー）。A〜C 専用の `GuidedField` 本体・`FieldGuideUi.Card` は移植していない |
| `PdLifeRow` / `PdAnchorRow` | `UxVisualRow`（視覚部品を載せる汎用の行）+ `VfxLifeInput`（DurationBar + 数値） |
| `PdRenderRow` / `PeAnchorToolsRow` / `PeAnchorIdRow` / `PeLightLayerRow` / `PeSlotsRow` | `VfxRenderRow` / `VfxAnchorToolsRow` / `VfxAnchorIdRow` / `VfxLightLayerRow` / `VfxSlotsRow`（Vfx 固有） |
| `PeTunePanel` / `PeTuneRow` / `PeProps` | `TuneKnobs` / `TuneKnobsRow`（種別非依存）+ `VfxTuneSource`（候補の列挙 = 材質のシェーダープロパティ）。分類語は `TuneCategories` |
| `DataSwitcher`（VfxData 専用） | `DataSwitcher`（Data の型名で一覧を引く。最近の鍵も型ごと） |
| `PdPalette` | `UxPalette` |
| 固定バー・保存・自動保存・未保存の確認・速度のデテント | 基底に移植。再生の有無は `SupportsPlayback`（false の種別は ▶ ■ リピート 速度を出さない） |
| `VfxAnchorSceneGui`（既存の public static） | そのまま使う |
| `SampleVfx` | `VfxSample`（空状態の「サンプルを作る」は `HasSample` / `CreateSample` を持つ種別だけ） |

- **E からの変更点**: ①インラインスタイルを全廃（表示 / 非表示は `.ux-hidden`、薄表示は `.ux-faded`、折り返しは `.ux-note`）。速度の ×1.0 の目盛りは `style.left` の割合指定をやめ、Painter2D の重ね要素（色は USS から）で描く ②モード（かんたん・標準・詳細）は `[SerializeField]` から EditorPrefs（種別ごとの鍵 `DDrive.Ux.<PrefsKey>.Mode`）へ。「未調整を隠す」「自動保存」も同じ鍵の規則 ③検証は `ValidationResult.Field` で欄に結びつけるのを最優先にし、`Field` の無い結果だけ従来のメッセージ照合（`ValidationTokens`）で救済 ④Undo のあとの再描画を 1 本に統合（E は 2 つの購読が別々に走っていた）⑤`DataSwitcher` の同カテゴリ一覧を 2 秒キャッシュ（150ms ごとの全走査をやめた）⑥アセットとして保存されていない Data（メモリ上）では未保存の確認を出さない（テストでダイアログが残る事故の防止）⑦試験版の注意書き（「試験版: 確認後に削除予定」）を外した ⑧`DurationBar` の LifeMode は種別非依存の `UxLifeMode`（Vfx は `VfxEditor` で対応づけ）⑨`Open(VfxData, GameObject)`（Presentation 等から「一緒に調整」で開く入口）も旧エディターと同じ形で持たせた。
- **`ValidationResult.Field`**: `public readonly string Field`（既定 null）と `WithField(string)`。公開 API の追加のみ（`public-api-DDrive.Foundation.txt` に 2 行増加 = MINOR。更新後 `ddrive_compat` は `changed:[]`）。既存のコンストラクタ・`Code` / `Severity` / `Message` / `FixAction`・Validator の結果の重さは変えていない。付けたのは `VfxDataValidator` の Prefab 未設定と Params の TargetProperty 不一致だけ（他の Validator は UX-2 以降の各チケットで必要になったときに足す。付いていない結果は従来どおり照合 / 「その他の検証」）。
- **確認**: コンパイル 0、EditMode 2085/2085 green（`UxFoundationTests` = U-27 の機械点検に足した「Editor/Ux に `.style.` 代入が無い」「C# が付ける `ux-*` クラスが USS に定義されている」と、「`VfxFieldGuide` が VfxData の編集できる全フィールドを持つ」「管理情報の既定」「`ValidationResult.Field`」「`VfxEditor` がヒーロー・固定バー・カード 8 枚を組む」を含む）、`ddrive_compat` は `ValidationResult.Field` の 2 行だけ → 更新後 `changed:[]`、`ddrive_forbidden_api` 違反 0。
  スクリーンショットは `UxScreenshot`（`GUIView.GrabPixels`）で Dark / Light の両方（上の画像）。自己確認ループ（2 周）で直したこと: 1 周目 = `GrabPixels` の矩形がホストビュー（タブ見出し込み）の左下原点で、内容の下 26px が切れて上下も逆さだった（撮り方を直した）。E と新 VfxEditor を同じモード・同じ位置で並べて、ヒーロー・出す・出る場所を変える（AnchorPad・基準・Anchor アセット）・長さ（DurationBar）・つまみ・複数同時再生・管理情報・固定バーのすべてが同じであることを確認。2 周目 = 空状態（Dark / Light）の「サンプルを作る」がピッカーに貼り付いていたので余白を足した、幅 500px（最小幅）でヒーロー・固定バー・各カードが切れず折り返すこと、速度スライダーの ×1.0 の目盛りが新方式でも出ること、Params の調整つまみ（分類チップ・型別部品・TargetProperty 不一致が Params 欄の下に結びつく）を確認。機能の動作確認は `execute_code` で、再生 / 停止、検証の欄結びつけ（Prefab 未設定 → 「エフェクトの実体(Prefab)」の下、Flags → 動作の取り決め、スキーマ → その他の検証）。
- **割り切り**: ①`Editor/Ux` の型は internal（`InternalsVisibleTo` を置かない方針のため、テストはリフレクション）②`ManualAnchor`（カードの「？」を節に飛ばす）は仕組みだけで、Vfx のマニュアルにまだ節の id が無いので null（ページの先頭が開く）③`FieldRowKind.ReadOnly` と `FieldGuideEntry.DefaultCheck` は共通基盤に用意したが Vfx は使っていない（Id / Version / Author / UpdatedAt は E と同じく管理情報カードの 1 行表示）④PlayMode は実行していない（Editor 専用 + Foundation の追加のみ。[12](12_review.md) §7 の軽い経路に準ずる）⑤`UxScreenshot.SwitchTheme` は `InternalEditorUtility.SwitchSkinAndRepaintAllViews`（ユーザーの Unity の配色設定を一時的に切り替える。撮影後に元へ戻す運用）⑥`vfx_sample.prefab` は `Editor/Prototypes/Samples/` のまま（`VfxSample` は `/Samples/vfx_sample.prefab` で探すので UX-19 で移しても動く）。
- **UX-2 に残したこと**: ①`VfxEditorWindow` の `[DataEditor]` を `(旧) VFX Editor で開く`（Order 100）に変え、新 `VfxEditor` に `[DataEditor(typeof(VfxData), "VFX Editor で開く")]`（Order 0）を付ける ②メニュー: 新 `Tools > D-Drive > Editors > VFX Editor`（今は仮の `VFX Editor (新)`）、旧は `DDriveMenu.Archive`（UX-18 で定数を足す）③`VfxEditorWindow.Open(...)` の呼び出し元（Presentation の「一緒に調整」ほか）を新エディターへ向ける ④マニュアル（`vfx-editor.html`）の書き換えと、あれば節の id を足して `ManualAnchor` を使う ⑤機能対応表（旧 `VfxEditorWindow` → `VfxEditor` の全行「あり」。試験版 E の対応表 [1008](1008_editor_ux_redesign.md) §5 と同じ行で、UX-1 時点で E と同じ画面が出ることまでは確認済み）を UX-2 の節に載せる ⑥`docs/images/1010_vfx.png` は UX-2 で撮り直してよい。


**2026-10-12 追記（UX-2、VFX の新エディターの正式化）**

![新 VfxEditor（左 = Dark、右 = Light。標準モード。UX-2 で撮り直し）](images/1010_vfx.png)

- **やったこと**: ①`DDriveMenu.Archive`（`Tools/D-Drive/Archive/`）を追加（UX-18 の前倒し、追加のみ）②新 `VfxEditor` に `[DataEditor(typeof(VfxData), "VFX Editor で開く")]`（Order 0）とメニュー `Tools > D-Drive > Editors > VFX Editor`（旧と同じラベル）。旧 `VfxEditorWindow` は `[DataEditor(…, "(旧) VFX Editor で開く", Order = 100)]` とメニュー `Tools > D-Drive > Archive > VFX Editor`、ウィンドウの題は `(旧) VFX Editor` ③`VfxEditorWindow.Open(...)` の呼び出し元を向け直し ④マニュアル `vfx-editor.html` を新エディターの説明に書き換え（節に id: `show` / `place`（`anchor`）/ `life` / `look` / `link` / `multi` / `admin` / `validation` / `prefab-mode` / `header` / `play`。各カードの「?」は `VfxEditor.ManualAnchor` で該当節へ飛ぶ）、旧の説明は `archive-vfx-editor.html`（画像も `archive-vfx-editor-*.png`）、`Readme.html` に「アーカイブ」節 ⑤`Tools/SpecWeb/tools/build-manual.js` を再生成（`push.cmd` / clasp push はしていない）。
- **向け直した呼び出し元**（`grep -rn VfxEditorWindow Packages/com.ddrive.core/Editor`）: `PresentationEditorWindow.TrackEditors.cs`（トラックの「専用エディタで開く」「一緒に調整」= `VfxEditor.Open(data, attach)`）の 1 か所だけが旧を直接呼んでいた。Inspector の「エディターで開く」・AssetBrowser のダブルクリック・Anim の SE / VFX イベント・NewAsset 導線などは `DataEditorRegistry`（`TryGetPrimary` = Order 最小）経由なので、`[DataEditor]` の付け替えで自動的に新へ向く。試験版 `PrototypeBWindow` の「すべての設定を見る」は `DDrive.Editor.Prototypes` から internal の `VfxEditor` が見えないので `DataEditorRegistry.OpenDefault(Target)` にした（UX-19 で削除）。旧 `VfxEditorWindow` 自身の内部参照はそのまま（旧→旧）。残る `VfxEditorWindow` の文字列はコメント・`VfxAnchorSceneGui`（旧 / 新 / 試験版の共通部品）だけ。
- **テスト**: `DataEditorRegistryTests`（新を第 1 候補、旧は第 2 候補として登録されていること）・`AssetBrowserDoubleClickTests`（既定の向き先 = 新、`OpenDefault` で新が開き `Target` が入る）・`NewAssetToolbarButtonTests`（TearDown に新を追加。`GetDataTypes(旧)` は旧のまま）。新 `VfxEditor` は internal なのでテストからは型名（`DDrive.Editor.Vfx.VfxEditor`）で引く。
- **機能対応表（旧 `VfxEditorWindow` → 新 `VfxEditor`、全行あり）**: [1008](1008_editor_ux_redesign.md) §5 の UX-0f の表を元に、旧の UI 要素（`VfxEditorWindow*.cs` の `Button` / `Toggle` / `Foldout` / `AddBoundProperty`）を 1 行ずつ、新エディターを詳細モードで開いたときの全ラベルの一覧（`execute_code` で UIElements を走査）と突き合わせた。

| 旧 VfxEditorWindow の機能 | 新 VfxEditor の場所 | 状態 |
|---|---|---|
| ツールバー「🔒 対象を固定」 | ヒーローの「固定」 | あり |
| ツールバー「確認用シーンを開く」 | ヒーローの「確認用シーン」 | あり |
| ツールバー「Prefab を開く」（プレハブモード） | ヒーローの「Prefab で開く」 | あり |
| ツールバー「Project で表示」 | ヒーローの「Project」・アイコンのクリック | あり |
| 「＋ 新規作成」 | ヒーローの「＋ 新規」 | あり |
| 対象アセット欄 | ◀ [名前 ▾] ▶ と空状態のピッカー・ドロップ・最近のデータ | あり |
| スポーン先（シーン内オブジェクト） | 「出る場所を変える」カードの「出る場所の基準」 | あり |
| ▶ 再生 / ■ 停止 / 再生中のやり直し | 固定バーの「▶ 再生（再生中は ▶ やり直し）/ ■ 停止」 | あり |
| リピート | 固定バーの「リピート」 | あり |
| 速度（0.1〜2） | 固定バーの「速度」（×1.0 クリックで等倍） | あり |
| ステータス（● 再生中 / ■ 停止中） | ▶ ボタンの文言と ■ の状態 | あり |
| 一時停止（旧マニュアルに記述はあったが旧エディターの UI には無かった） | — （旧にも無いので対象外。新マニュアルからは記述を外した） | 対象外 |
| 基本設定: Prefab | 「出す」カードの「エフェクトの実体（Prefab）」 | あり |
| 基本設定: 寿命モード / 継続秒数 / 停止時フェード秒 | 「長さ・消え方を変える」カードの DurationBar と数値 | あり |
| 基本設定: 描画モード（Render）/ RenderLayer | 「出す」カードの「表示先」と表示レイヤー | あり |
| 基本設定: LightLayerMask | 「出す」カード（詳細）の「ライトの当たり方」 | あり |
| 基本設定: 共通フラグ（Pool / Pause / Net） | 「長さ・消え方を変える」カード（詳細）の「動作の取り決め」 | あり |
| Anchor: Anchor アセット（AnchorId） | 「共通の出る位置（Anchor アセット）」（詳細） | あり |
| Anchor: Anchor Editor で開く / 埋め込みをアセット化 | 同欄の下の 2 ボタン | あり |
| Anchor: Space / Path / 高さ / 向き / スケール | AnchorPad + 詳細の「位置の全設定」 | あり |
| Anchor: Path を一覧から選ぶ（★ AnchorPoint）・解決の表示 | 「出る場所の基準」の「Path を一覧から選ぶ」・解決の文 | あり |
| Anchor: 2D パッド | AnchorPad（パッド + 高さスライダー + 数値 + 原点へ） | あり |
| Anchor: 回転追従 / 親消滅後も残す | 詳細の「位置の全設定」の Follow Rotation / Detach On Stop | あり |
| Anchor: SceneView 表示（移動・回転ハンドル、Anchor アセット編集、描画権の案内） | 「出る場所の基準」の「SceneView に表示」と案内文（描画は共通の `VfxAnchorSceneGui.Draw`） | あり |
| パラメータ（再生中は即時反映） | 「見た目を調整する（Params）」の調整つまみ | あり |
| パラメータ: 定義の追加・削除（Label / Type / TargetProperty / Anim） | 「＋ つまみを追加」・行の「⚙」・自由入力 | あり |
| イベント（OnSpawn / OnLoop / OnDestroy） | 「音・イベントと合わせる」カード（詳細） | あり |
| 複数同時再生（最大 8 スロット、▶ / ■） | 「複数同時再生」カード（詳細） | あり |
| 検証（Error / Warning） | 欄の下のインライン表示・「その他の検証」・ヒーローの警告チップ | あり |
| カメラ / ライトが無い警告・プレハブモード中の案内 | ヒーロー直下の HelpBox | あり |
| プレハブモードの開閉・保存で再生中の実体を撮り直す | `PrefabStage` 購読（再生中の実体の撮り直し） | あり |
| シーン切替でプレビューを止める | `activeSceneChangedInEditMode` 購読 | あり |
| Undo / Redo の同期 | `OnUndoRedo`（再描画 + 検証 + Anchor 反映） | あり |
| 再コンパイル / PlayMode 遷移に耐える状態 | 対象・固定・リピート・速度・スポーン先・SceneView 表示を `[SerializeField]` | あり |
| `Open(VfxData, GameObject attachTarget)`（Presentation の「一緒に調整」） | 同じシグネチャで持つ（`execute_code` で attach が入ることを確認） | あり |

  新エディターで増えたもの（旧に無い、欄の整理に伴うもの）: かんたん / 標準 / 詳細の 3 モード・設定の検索・未調整の折りたたみ・すべて既定に戻す・保存ボタンと自動保存・カードの「?」からマニュアルの節へ。
- **確認**: コンパイル 0。`execute_code`: `DataEditorRegistry.GetEntries(VfxData)` = [`VfxEditor` "VFX Editor で開く" 0, `VfxEditorWindow` "(旧) VFX Editor で開く" 100]、`OpenDefault` で新が 1 つだけ開く・`entries[1].Open` で旧（題 `(旧) VFX Editor`）が開く、Presentation の「一緒に調整」相当の `VfxEditor.Open(data, go)` で `_attachTarget` が入り旧は開かない、`StartPlayback` / `StopPlayback` で再生・停止、メニュー `Tools/D-Drive/Editors/VFX Editor` と `Tools/D-Drive/Archive/VFX Editor` が有効。スクリーンショットは Dark / Light（標準モード、`UxScreenshot`）。自己確認 2 周: 1 周目で Dark の撮影がモード切替前の状態（詳細）で撮れていたので撮り直し、2 周目で両テーマとも標準モードのヒーロー・カード・固定バーが切れていないことを確認（Light のパッドの罫線が薄いのは UX-1 から同じで、承認された E のデザインのまま）。
- **互換性（editor-contract.txt）**: 追加 3 行（`Archive = Tools/D-Drive/Archive/`、`VfxData -> VfxEditor "VFX Editor で開く" order=0`、`VfxData -> VfxEditorWindow "(旧) VFX Editor で開く" order=100`）と**削除 1 行**（旧 `VfxData -> VfxEditorWindow "VFX Editor で開く" order=0`）。削除行は「旧エディターのラベルと Order が変わる」ことの機械的な帰結で、追加だけでは避けられない（新エディターが旧ラベルを引き継ぐ形でも、旧の行は変わる）。Editor 契約は弱い互換面（[42](42_distribution.md) §5.9）で、1010 §3 の方針（旧は `(旧)` Order 100）どおりだが、`ddrive_compat` が removed を出すため、スナップショット更新はユーザー承認待ち。

**2026-10-12 追記（UX-3、Audio〔Se / Bgm〕の新エディター）**

![新 Audio Editor（上 = Dark、下 = Light。左 = SE の「鳴らす」、中 = SE の「切る・つなぐ」「聞こえ方」、右 = BGM）](images/1010_audio.png)

- **構成**: `UxEditorWindow<TData>` の `TData` は具体型（基底が `CreateInstance<TData>()` で既定値の複製を作る）なので、抽象の `AssetDataBase` 基底の 1 ウィンドウにはせず、**`SeEditor : UxEditorWindow<SeData>` と `BgmEditor : UxEditorWindow<BgmData>` の 2 ウィンドウ**にした。`DataEditorRegistry`（Inspector の「Audio Editor で開く」・Asset Browser のダブルクリック）が種別に応じた方を開く。**Se ⇄ Bgm の行き来**はヒーローの「BGM Editor へ / SE Editor へ」（元のウィンドウの隣にもう片方を開き、選択中の Data か最近開いた Data を入れる。`SwitchFrom`）。メニュー `Tools > D-Drive > Editors > Audio Editor` は BGM を選んでいれば BgmEditor、それ以外は SeEditor を開く。
- **ファイル**（`Editor/Audio/`、すべて internal）: `SeEditor.cs` / `BgmEditor.cs`（土台への指定・再生・欄の組み立て）/ `SeFieldGuide.cs` / `BgmFieldGuide.cs`（欄の表）/ `AudioWaveformView.cs`（`AudioWaveSpec` + 波形の列データ `WavePeaks` + Painter2D の波形。端のドラッグ・範囲外クリックで近い端を移動・ホバー線・再生位置・再生開始位置の目印）/ `AudioListenerPad.cs`（3D サウンド確認パッド）/ `AudioEditorParts.cs`（`SeTrimInput` = Source 選択 + 波形 + 開始 / 終了の数値 + 無音検出 + トリミング適用、`BgmLoopInput` = ループ範囲、`SeStartOffsetInput`、`SeLoopRow`、`SeAnchorIdRow`）。他: `UxEditorWindow<TData>.SupportsSpeed`（固定バーの速度を出さない種別用。BGM は false）、`DDriveUx.uss` に `ux-wave*` / `ux-trim*` / `ux-lpad*` / `ux-offset*`、`SeDataValidator` / `BgmDataValidator` の結果に `.WithField(...)`（メッセージ・Code・重さは不変）、`Tests/Editor/AudioEditorTests.cs`。旧 `AudioEditorWindow` は `(旧) Audio Editor で開く(…)` Order 100 + メニュー `Archive/Audio Editor`（題 `(旧) Audio Editor`）。
- **目的別カード**（段の根拠は Validator。SE = Error は Clips のみ → 必須）: SE =「鳴らす」（Clips〔必須〕/ SelectMode / Volume / PitchRange / Loop + 試聴だけループ）・「切る・つなぐ」（波形とトリム範囲 = Sources、再生を始める位置 = StartOffsetSec）・「聞こえ方」（Mixer / Spatial / 距離 2 欄 / 共通の位置 AnchorId / 鳴る位置 Anchor〔AnchorPad〕/ Rolloff / Spread / Doppler + 3D サウンド確認パッド）・「同時再生・連打」（MaxConcurrent / CooldownSec / Flags）・「音・イベントと合わせる」（Events）・管理情報。BGM =「鳴らす」（LoopBody〔必須〕/ Intro / Volume / Flags）・「切る・つなぐ」（ループの範囲 = LoopStartSec + LoopEndSec、FadeIn / FadeOut）・「聞こえ方」（Mixer）・「拍に合わせる」（Bpm）・「音・イベントと合わせる」・管理情報。カードの「?」は `audio-editor.html` の節 id（`se-play` / `se-cut` / `se-hear` / `se-limit` / `bgm-play` / `bgm-loop` / `bgm-hear` / `bgm-beat` / `link` / `admin`）へ飛ぶ。
- **機能対応表（旧 `AudioEditorWindow` + Inspector の `SeDataEditor` → 新 `SeEditor` / `BgmEditor`、全行あり）**: 旧 `AudioEditorWindow.cs` の UI 要素（ツールバー・`ObjectField`・`DropdownField`・`IMGUIContainer` 2 つ・`Button`・`Toggle`・`Slider` 3 つ・`FloatField` 2 つ・検証セクション）を 1 行ずつ、新エディターの詳細モードの全ラベルと突き合わせた。

| 旧の機能 | 新の場所 | 状態 |
|---|---|---|
| ツールバー「＋ 新規作成」（SE / BGM の選択） | ヒーローの「＋ 新規」（各ウィンドウの種別。SE ⇄ BGM はヒーローの切替ボタン） | あり |
| 「対象アセット」ObjectField（SE / BGM） | ◀ [名前 ▾] ▶、空状態のピッカー・ドロップ・最近のデータ。Project の選択に追従、🔒 固定 | あり |
| Mixer の表示（「Mixer: 名前 / 未割当 — Validation Warning」） | 「聞こえ方」の「出力先（Mixer）」欄 + 未割当の警告が欄の下に出る（BGM も同じ） | あり |
| 編集対象 Source のプルダウン（SE） | 「波形とトリム範囲」のプルダウン（Sources が空のときは Clips[0] の波形を表示するだけ） | あり |
| 波形（IMGUI。範囲の外側を暗く、開始 = 水色・終了 = 紫、「トリム範囲 / ループ範囲: a – b / 全体」） | `AudioWaveformView`（Painter2D。範囲の外を薄く、両端は旗付きの線、左下に範囲・右下に全体の長さ。波形が読めないクリップは理由を表示） | あり |
| 端のドラッグ / 範囲外クリックで近い端を移動 / 最小幅 0.001s | 同じ操作（`PointerCapture`。1 回のドラッグ = 1 回の Undo にまとめる） | あり |
| 無音自動検出（SE） | 「無音を自動検出」ボタン（読めないクリップは警告ログ） | あり |
| トリミングを適用（Clips 再生成） | 「トリミングを適用（Clips を作り直す）」ボタン（`SeTrimApplier.Apply`） | あり |
| ▶ 再生 / ■ 停止（実 AudioManager / BgmManager 経由、SelectMode どおり） | 固定バーの ▶ / ■（再生中は「▶ やり直し」）。`PreviewService` を窓ごとに持つ | あり |
| ループ試聴 トグル（アセットは変えない） | 「鳴らす」カードの「試聴だけループさせる」（SE。BGM は元から常にループ） | あり |
| 音量スライダー（試聴用、再生中に即時反映） | 「基準音量」欄（再生中に動かすと鳴っている音へ即時反映）。**アセットの値を直接編集する形に変更**（旧は試聴専用で保存されなかった。Undo できる） | あり（変更点あり） |
| ピッチスライダー（試聴用、再生中に即時反映、0.1〜2） | 固定バーの「速度」（再生ピッチ、0.1〜2、×1.0 のデテント、再生中に即時反映。データに保存されない）。保存したい揺らぎは「ピッチの揺らぎ」欄 | あり |
| 再生開始位置スライダー（SE、0〜Clips[0] の長さ） | 「再生を始める位置」のスライダー + 数値（波形に点線の目印） | あり |
| 3D サウンド確認パッド（リスナーのドラッグ・横 / 縦範囲 ±m・リスナー角度・Min / Max の円・距離 / 角度 / 位置の表示・音源は中央） | `AudioListenerPad`（Painter2D。同じ計算 `ListenerPadMath`）。再生中の音源をリスナーから見た相対位置へ即時移動 | あり |
| パッド無効時（BGM / Spatial = None / Clips 未設定）の暗転と理由 | パッドの上に「⚠ 3D プレビュー無効」と理由（BGM はそもそもパッドを持たない） | あり |
| 検証セクション（`DataValidationSection`） | 欄の下のインライン表示（Validator の結果に欄名を付与）・「その他の検証」・ヒーローの警告チップ | あり |
| 再コンパイル / PlayMode 遷移に耐える対象 | `[SerializeField]`（対象・固定・リピート・速度・試聴だけループ） | あり |
| `AudioEditorWindow.Open(AssetDataBase)`（他エディターから） | `SeEditor.Open(SeData)` / `BgmEditor.Open(BgmData)`（`DataEditorRegistry` が呼ぶ。呼び出し元はいずれも登録表経由だった） | あり |
| Inspector（`SeDataEditor`）の Sources 一覧・無音検出・適用・「AnchorEditor で開く」・「埋め込み Anchor をアセット化」 | 「元の音とトリム範囲（一覧）」・波形の各ボタン・「共通の位置（Anchor）」の 2 ボタン（Inspector 側はそのまま残る） | あり |
| `AudioPreviewPane`（▶ ■ ループ 速度のバー。旧 AudioEditorWindow 自身は使っておらず、Asset Browser 下部のバー） | 固定バーの ▶ ■ リピート 速度が同じ役割（Asset Browser 側は変更なし） | あり |

  新エディターで増えたもの: かんたん / 標準 / 詳細・検索・未調整の折りたたみ・変更の色バーと ↺・保存と自動保存・カードの「?」・再生位置の表示（SE の Clips が 1 つのとき）・Anchor の位置パッド / AnchorId の導線・MaxConcurrent / CooldownSec / Rolloff / Spread / Doppler / Pitch Range / Select Mode / Loop / Intro / FadeIn / FadeOut / Bpm / Events / Flags（旧はウィンドウに無く Inspector だけだった欄）。
- **割り切り**: ①**再生位置のシーク（クリックでその位置から鳴らす）は作っていない**（旧にも無く、`AudioManager` にシークの API が無い。追加すると Runtime の公開 API の追加になるため本チケットでは見送り）。代わりに、SE の Clips が 1 つのときは再生位置を波形に白線で出す。BGM の再生位置は `BgmManager` が時刻を公開していないので出さない ②BGM の固定バーには速度を出さない（旧も BGM に速度は無かった。`SupportsSpeed`）③SE の波形は Source（元の音）のもの。Clips にトリム済みの音が入っているときの再生位置は、先頭の Source のトリム開始を足して合わせる（Clips / Sources が複数のときは出さない）④`AudioClip` の波形は `GetData` が読めるクリップだけ（圧縮 / ストリーミングは旧と同じく読めず、理由を表示）⑤旧の「音量 / ピッチ」の試聴専用スライダーは、上の対応表のとおり音量はアセットの値の直接編集、ピッチは固定バーの速度に置き換えた ⑥`ListenerPadMath` は旧と共用（変更なし）。
- **互換性（editor-contract.txt）**: 追加 4 行（`BgmData -> BgmEditor "Audio Editor で開く(波形・ループ編集)" order=0` / `SeData -> SeEditor "Audio Editor で開く(波形・トリム編集)" order=0` / 旧 2 行の `(旧)` `order=100`）と**削除 2 行**（旧 `AudioEditorWindow` の Order 0 の 2 行）。UX-2 と同じ扱い（旧エディターのラベルと Order が変わることの機械的な帰結）。更新前に red だったのは `EditorContractSnapshotTests.MatchesGolden` と `DDriveCompatToolTests.Compat_RealSnapshots_HaveNoDifference` の 2 件だけで、`ddrive_compat_update` のあと `changed:[]`。CHANGELOG の互換性節に追記済み。
- **確認**: コンパイル 0。EditMode 2093/2093 green（`AudioEditorTests` 8 件 = 欄の表が SeData / BgmData の編集できる全フィールドを持つ・必須の段 = Validator が Error にする欄・Validator の結果が欄に結びつく・`WavePeaks`・両エディターがヒーロー / 固定バー / カード / 波形を組む〔BGM は速度なし〕。`DataEditorRegistryTests` / `UxFoundationTests` / `DDriveDependencyToolTests.EditorOpen_OpensSeEditorForSe` は新エディターに追従）。更新前に red だったのは契約の 2 件だけ、`ddrive_compat_update` のあと `changed:[]`。PlayMode は実行していない（Editor 専用 + Validator の `WithField` 付与のみで Runtime の挙動は不変。[12](12_review.md) §7 の軽い経路に準ずる）。`execute_code`: Inspector の登録表（SeData / BgmData とも新が Order 0・旧が `(旧)` Order 100）、SE の再生 / 停止、波形のドラッグ（`PointerCapture` のイベントを送って End が動き、Undo 1 回で戻る）、開始値の変更と Undo、3D パッドのドラッグで再生中の音源が相対位置へ移動、BGM の再生 / 停止、`SwitchFrom` で SE → BGM、メニュー `Editors/Audio Editor` と `Archive/Audio Editor`。スクリーンショットは Dark / Light（標準モード、`UxScreenshot`）。自己確認 2 周: 1 周目 = 3D パッドの Max 距離の楕円がパッドの外へはみ出して他の欄の上に線が出た（Painter2D は要素の外へも描くので `overflow: hidden`）・リスナー角度のスライダーのラベルが行の入力の直下だと隠れた（1 段包む）・「試聴だけループ」のトグルのラベルが同じ理由で隠れ、`flex-basis` の流用で縦に大きく伸びた・1 つしかない Source のプルダウンが無効に見えた。2 周目 = 幅 500px（最小幅）で波形・数値欄・ボタン・固定バーが切れず折り返すこと、Light で波形・旗・パッドの色が読めること、BGM のループ範囲の波形と数値欄を確認。実データ（`Assets/GameData`）は複製を開いて操作し、保存していない。

**2026-10-12 追記（UX-4、Canvas の新エディター。D-10 対応）**

![新 CanvasEditor（左 = Dark、右 = Light。上 = 初期の画面の上部、下 = 要素の動き・配線の一覧を開いたところ。HUD 相当のサンプルデータ）](images/1010_canvas.png)

- **使ったサンプルデータ（D-10）**: このリポジトリの `Assets/GameData` に HUD（HudInteractPrompt 等）は無い（MS2026 側にある）ので、**HUD 相当を作るヘルパー `Editor/Ux/Samples/CanvasSample.cs`**（internal。`Build(folder)`）を同梱した。親 Prefab `HudSample`（要素 15 = Panel・Frame・Title / Score / Timer / Life / Icon・UiButton 5 つ・埋め込み 3 つのルートと中身）+ 子 Prefab 3 つ（InteractPrompt / GaugeView / ResultView。それぞれ Label / BtnOk / Icon）+ CanvasData 4 つ（親 + 子 3）。親は **ElementFx 15 行（設定済み 6・未設定 9 の混在。Appear / Idle / Disappear の組み合わせあり）・埋め込み 3（うち 1 つは無効で始める）・ボタン配線 7 本（5 ボタン。Click → SendSignal ×5、LongPress → CloseSelf、DoubleClick → ToggleEmbedded）・Navigation 8 要素 / リンク 14 本・FirstSelected**。呼び出し側が一時フォルダ（`Assets/…Tmp`）に作って使い終わったら消す（`Assets/GameData` には作らず、Addressables にも登録しない）。スクリーンショット（上の画像）とテスト（`CanvasEditorUxTests`）はこのサンプルで開く。20 行超の確認はこのサンプルに要素 12 行を足して行う。
- **D-10 の基準をどう満たしたか**（(1)〜(7)）:
  (1) **初期表示が 1〜2 画面に収まる**: 一覧の 4 欄（要素の動き・配線・埋め込み・Navigation）は `Foldout` で既定は閉じ（構造体・配列の欄が作る Foldout も組み終えたあとに閉じる。テスト `SampleHud_AllFoldoutsAreClosedByDefault`）、見出しだけで「15 要素・設定済み 6・埋め込み 3」「7 配線（5 ボタン）」「8 要素・リンク 14 本・最初 = Panel/BtnPause」と中身が分かる。標準モードの初期表示は 1 画面強（出す → 要素の動き → 配線 → 埋め込み → カーソル移動 → 管理情報）。
  (2) **一覧は 1 要素 = 1 行**: ElementFx = 「要素名・種類のチップ（ボタン / スライダー / テキスト / 画像 / パネル）・『Appear: PopIn / Idle: Pulse / Disappear: FadeOut』・▶・↺・⚙」、配線 = 「ボタンのパス・『Click → SendSignal 'pause' / LongPress → CloseSelf』・＋ 配線・⚙」、埋め込み = 「RootPath・有効 / 無効のチップ・→ 子の名前・開く・⚙」。展開（⚙）は**その行だけ**（ElementFx は 3 つの動きの選び方・UiTweenData 直接指定・✎・▶ ⏸ ■・出現の遅れ・SE・選択・コピー、配線は配線 1 本 = 1 行、埋め込みは RootPath・子・無効で始める・作業用の表示切替・削除）。展開のドロップダウン類は展開した行でだけ作る（15 行 × 3 つを最初から作らない）。
  (3) **絞り込みと件数**: 一覧の上に検索（パス・要約）・「すべて / 設定済み / 未設定」・種類のチップ（ElementFx）と、「15 要素中 設定済み 6（表示 n）」。ウィンドウの検索ボックスも、要素・ボタン・埋め込みのパスに一致する一覧を開いて絞る（一致が 0 件なら絞らない = 表の語だけに一致した検索で一覧が空にならない）。
  (4) **埋め込みは要約 + 「開く」**: ElementFx / 配線の一覧とも、埋め込みの子の中身（子の ElementFx・配線）は展開しない。子ごとに 1 行（「Group/InteractPrompt → CANVAS_InteractPrompt: 子の ElementFx 4 行（中身は子のエディターで）」+「開く」）。「開く」= 編集対象を子に切り替える（← 親へ戻る・確認用プレビューは残る）。テスト `SampleHud_EmbeddedChild_IsOneLineWithOpenButton_NotExpanded`。
  (5) **一括操作は一覧の上に 1 行**: 「要素を自動収集 / プリセット / 全ボタンに反映 / ▶ 全 Appear / ▶ 全 Idle / ▶ 全 Disappear / ■ 全て停止」+ 「Idle を流す」「再生時に選択」のトグル（幅が足りなければ折り返す）。
  (6) **20 行を超えたら ListView**: `CanvasItemList` が表示対象が 20 行を超えると `ListView`（`DynamicHeight`、高さ 420px）で描く（行の展開で高さが変わっても追従）。ウィンドウのスクロールは外側 1 本 + ListView 自身の 1 本だけ（行の中にスクロールは作らない）。テスト `SampleHud_FxList_MoreThan20_UsesListView`。**割り切り**: ListView は内側にスクロールバーを持つ（仮想化のため。外側のスクロールとの二重は 20 行超のときだけ）。
  (7) **500px 幅で崩れない**: 行は折り返し（`flex-wrap`）、名前・要約は省略表示（ellipsis）、ボタンは縮む。ヒーロー・固定バーは UX-1 の共通部品のまま。
- **機能対応表（旧 `CanvasEditorWindow` + `.ButtonWires` + `.EmbedActive` + 旧 Inspector の埋め込み → 新 `CanvasEditor`、全行あり）**: 旧のコード（`Button` / `Toggle` / `Foldout` / `EnumField` / `ObjectField` / `PopupField` / 検証・`BuildXxx` の全メソッド）を 1 行ずつ、新エディターの詳細モードの全ラベルと突き合わせた。

| 旧の機能 | 新の場所 | 状態 |
|---|---|---|
| ツールバー: 対象 ObjectField / 🔒 / ＋ 新規作成 | ◀ [名前 ▾] ▶・空状態のピッカー・ドロップ / ヒーローの「固定」/「＋ 新規」 | あり |
| ツールバー: Prefab を開く(要素の移動) | ヒーローの「Prefab で開く」 | あり |
| 親へ戻る・「埋め込みとして編集中: Hud > Option」 | ヒーロー直下の「← 親へ戻る」と連なりの表示 | あり |
| 選択に追従（EditorPrefs）+ 🔒 のとき灰色 + 理由の文 | ヒーロー直下の「選択に追従」+「🔒 がオンの間は追従しません」 | あり |
| Selectable を自動収集（既存の行を保持・差分が無ければ書かない） | 「カーソル移動」欄の「Selectable を自動収集」 | あり |
| 要素を自動収集（埋め込み配下は除外・差分が無ければ書かない） | 「要素の動き」欄の一括操作「要素を自動収集」 | あり |
| プリセット + 一括適用: 全ボタンに反映 | 同「プリセット」+「全ボタンに反映」 | あり |
| 埋め込み Canvas: 登録済みの行（RootPath・子 CanvasData・この Canvas を編集・無効で始める・表示 / 非表示・削除） | 「埋め込み Canvas」欄の 1 行 + ⚙ の展開（「開く」= この Canvas を編集） | あり |
| 埋め込み Canvas: 入れ子 Prefab から検出（未登録）→ 登録 / ＋ 手動で追加 / 登録時・RootPath 変更時・子の変更時の「親での上書き」整理 | 同欄の「未登録」の行の「登録」/「＋ 手動で追加」（整理の確認は `CanvasEmbeddedEditing` の同じ処理） | あり |
| ElementFx: 絞り込み（パス） | 一覧の上の検索・状態・種類のチップ + ウィンドウの検索 | あり |
| ElementFx: 親の要素 / 親での上書き / 埋め込み: 子（読み取り表示）のグループ分け | 親の行と「親での上書き」の行は通常の行、埋め込みは 1 行の要約 + 「開く」（D-10 (4)） | あり（要約に変更） |
| ElementFx: 「上書きをまとめて整理…」 | 埋め込みの要約行の「上書きを整理…」 | あり |
| ElementFx: ▶ 全 Appear / ▶ 全 Idle / ▶ 全 Disappear / ■ 全て停止 | 一括操作の同名ボタン | あり |
| Idle を流す(プレハブモード)+ ヒント（流している件数・止めた要素・入れ子の注意） | 一括操作のトグル + ヒント（`CanvasIdleFlow` と署名・停止の経路は旧と同じ） | あり |
| ▶ 再生時にその要素を選択（EditorPrefs） | 同トグル | あり |
| 行: 選択 / フォーカス / 選択して移動(Prefab を開く) / 他の要素へコピー | 展開した行の同名ボタン（要素名のクリック = 選択） | あり |
| 行: Appear / Idle / Disappear のドロップダウン（なし / プリセット / [Catalog]）+ UiTweenData 直接指定 + ✎ Tween Editor + ▶ ⏸ ■ + 状態 | 展開した行の同じ並び（行の ▶ は最初の割り当てを再生 / 停止） | あり |
| 行（旧 Inspector）: ElementPath / AppearDelay / AppearSe / DisappearSe・プリセットの Duration / Distance / Ease / Se | 展開した行の出現の遅れ・SE / 詳細モードの「一覧そのもの」 | あり |
| 行: ハイライト（選択に追従で該当行を強調・展開・スクロール） | 強調の印 + 欄を開いて絞りを解除 + スクロール | あり |
| ボタンの配線: ボタンごとの見出し・選択・＋ 配線を追加（空きトリガー・埋め込み配下は子のトリガーも避ける） | 1 ボタン = 1 行・名前クリック・＋ 配線 | あり |
| 配線 1 本: トリガー / アクション / 対象(OpenCanvas) / SignalKey / PlayPresentation の注記 / 切り替える埋め込み(RootPath) / クリック SE / 削除 / 行の ⚠（`DescribeProblem`）/ 見つからないボタンの注意 | ⚙ の展開の配線 1 本 = 1 行（同じ欄・同じ照合 `SameWire`・同じ注意） | あり |
| ボタンの配線: 親の要素 / 親での上書き / 埋め込み: 子（読み取り） | 親・上書きは通常の行、埋め込みは 1 行の要約 + 「開く」 | あり（要約に変更） |
| スライダーの配線（旧は Inspector の配列） | 「ボタン・スライダーの配線」カードの Sliders（通常のリスト欄。詳細モード） | あり |
| 確認用シーンを開く（右クリックで本配置）/ 閉じる / ステータス | ヒーローの「確認用シーン」（右クリックで本配置）/ 固定バーの ▶ 再生・■ 停止 / ステータス行 | あり |
| Navigation グラフ（自動レイアウト・到達不能・未配線・高さ・リンク・FirstSelected・切断・Reroute・レイアウトの保存・Ping・右クリック・Undo）+ パッド操作シミュレーション | 「カーソル移動」欄の Foldout の中（`NavigationGraphView` を既存のまま埋め込み。「未配線を自動リンク」は実際はリンクしないので「未配線を一覧」にした） | あり |
| Inspector 埋め込み（全フィールド）: Prefab / Layer / SortOffset / OpenTransition / CloseTransition / CloseOnBack / ModalBlocksInput / PauseGameWhileOpen / Navigation / FirstSelected / Buttons / Sliders / ElementEffects / EmbeddedCanvases / Flags / Events / 管理情報 | 欄の表 `CanvasFieldGuide`（`NavigationNodeLayout` / `NavigationEdgeWaypoints` は `HideInInspector` でグラフが保存） | あり |
| Validation（色付きの箱 + 「修正」+ 例外で止めない） | 欄の下のインライン表示 + 行の ⚠ + その他の検証 + ヒーローのチップ（`DataValidationRunner.Run` と同じ） | あり |
| 再コンパイル・シーン切替・プレハブステージ・Play Mode 遷移・Undo / Redo への追従と後始末 | 旧と同じ購読と後始末（`OnPrefabStageClosing` / `OnPrefabSaving` / `activeSceneChangedInEditMode` / `playModeStateChanged`）+ Undo 時は一覧を署名で作り直す | あり |

  新エディターで増えたもの: かんたん / 標準 / 詳細・検索（パスにも一致）・未調整の折りたたみ・変更の色バーと ↺・保存と自動保存・カードの「?」・行ごとの ⚠・件数と絞り込み・ListView。
- **ファイル**（`Editor/Canvas/`、すべて internal）: `CanvasEditor.cs` / `.Preview.cs` / `.ElementFx.cs` / `.Wires.cs` / `.Embedded.cs` / `.Navigation.cs` / `CanvasSectionView.cs`（`CanvasSectionView` / `CanvasItemList` / `CanvasListItem` / `CanvasSig`）/ `CanvasFieldGuide.cs`、`Editor/Ux/Samples/CanvasSample.cs`、`Tests/Editor/CanvasEditorUxTests.cs`（12 件）。共通基盤の追加（既定の挙動は変えない）: `SetTarget(data, confirmSave)` / `SupportsRepeat` / `SearchQuery` / `ViewMode` / `ScrollToElement` / `OnValidated` / `OnFiltersApplied` / `OnBodyBuilt`、`UxRow.NoBulkReset`（一覧の欄は「すべて既定に戻す」・行の ↺ の対象外 = 全部消えてしまうため）/ `ExtraMatch` / `MaxIssues`、`DDriveUx.uss` に `ux-sec*` / `ux-itemlist*` / `ux-fxrow*` / `ux-fxphase*` / `ux-wireline*` など。旧 `CanvasEditorWindow` は属性・メニュー・題だけ変更（`(旧) Canvas Editor で開く` Order 100・`Archive/Canvas Editor`・題 `(旧) Canvas Editor`）。
- **向け直した呼び出し元**: `grep -rn CanvasEditorWindow Packages/com.ddrive.core/Editor` で旧を直接呼ぶ箇所は無かった（Inspector の「エディターで開く」・Asset Browser のダブルクリック・Presentation / Prefab Editor / UI Tween / Skin 系の導線はすべて `DataEditorRegistry` 経由）ので、`[DataEditor]` の付け替えだけで新へ向く。旧の `CanvasEditorWindow.CollectValidation`（static）は旧のテストが使うので残し、新にも同じ入口を持たせた。旧のテスト（`CanvasEditorTests` / `CanvasEditorWindowUndoTests` / `CanvasIdleFlowTests` / `CanvasEmbeddedEditingTests`）は旧のまま。
- **互換性（editor-contract.txt）**: 追加 2 行（`CanvasData -> CanvasEditor "Canvas Editor で開く" order=0` / `CanvasData -> CanvasEditorWindow "(旧) Canvas Editor で開く" order=100`）と**削除 1 行**（旧 `CanvasEditorWindow` の Order 0 の行）。UX-2 / UX-3 と同じ扱い。更新前に red だったのは `EditorContractSnapshotTests.MatchesGolden` と `DDriveCompatToolTests.Compat_RealSnapshots_HaveNoDifference` の 2 件だけで、`ddrive_compat_update` のあと `changed:[]`。CHANGELOG の互換性節に追記済み。
- **確認**: コンパイル 0、**EditMode 全件 2105/2105 green**（途中で 1 件 red = `Editor/Ux/Samples/CanvasSample.cs` の `AssetDatabase.SaveAssets()` 直呼びを機械点検が検出 → `DDriveAssetSave.SaveAllSuppressed()` に置き換え、該当テストの再実行で green。意図した 2 件〔`EditorContractSnapshotTests` / `DDriveCompatToolTests`〕は `ddrive_compat_update` のあと green、`ddrive_compat` は `changed:[]`）。`CanvasEditorUxTests` 12 件（欄の表が編集できる全フィールドを持つ・Prefab = 必須・登録表の新 Order 0 / 旧 Order 100・ヒーロー / 固定バー / カード 8 枚・速度とリピートなし・全 Foldout が既定で閉じている・1 要素 1 行と件数と「設定済みだけ」・20 行超で ListView・ElementFx 1 行の変更と Undo・配線 1 本の追加と削除・埋め込みの有効 / 無効の切替と Undo・埋め込みの子は 1 行 + 「開く」・Navigation の自動収集・検証の行ごとの ⚠）。`execute_code`（実ウィンドウ・HUD サンプル）: Inspector の登録表（新が先頭・旧が「(旧)」）、全 Foldout が既定で閉じている、ElementFx 1 行の変更と Undo、配線の追加と削除、埋め込みの無効で始める、▶ で表示 / ■ で片付け、「確認用シーン」で CanvasPreviewScene に切り替わって表示、Navigation の自動収集。実データ（`Assets/GameData`）は触らず、サンプルは一時フォルダで作って削除した。
  スクリーンショットは `UxScreenshot` で Dark / Light（上の画像）。自己確認ループ（2 周）: 1 周目 = ①埋め込みの子の要約が「埋め込み: CANVAS_InteractPrompt(…」と名前で切れた（名前を RootPath にして子の名前は要約側へ）②埋め込み行の展開で「無効で始める」のラベルが隠れた（ラベルを Toggle 自身の text に）③件数が埋め込みの要約行まで数えていた（`Summary` の行は数えない）④構造体の欄（開く / 閉じるときの演出）の Foldout が開いたまま = 既定の折りたたみに反した（組み終えたあとに閉じる）⑤エラーが多い一覧（要素 27 行中 12 行がエラー）で欄の下に検査が 12 件並んだ（`MaxIssues = 3` と行の ⚠）⑥サンプルの要素が同じ位置に重なり Navigation グラフが 1 ノードに見えた（サンプルの要素を格子状に配置）。2 周目 = Light でも行・チップ・展開の欄・ListView の色が読めること、500px の幅（実測。HUD サンプル）で一覧の行が折り返し・展開した行も切れないこと、展開した行の下のドロップダウン・ObjectField が切れないことを確認。Light は無効の ▶ ↺ が薄く見えるが、承認済みのデザインの disabled 表現のまま。
- **割り切り**: ①ElementFx の行の ▶ は「最初に割り当てた区間」を再生する（3 区間の個別再生・一時停止は展開した行）②スライダーの配線の専用 UI は作っていない（旧も Inspector の配列だった。通常のリスト欄）③`ListView` は内側スクロールを持つ（20 行超のときだけ）④確認用シーンの再生は「▶ 再生 = 表示 / ■ 停止 = 片付け」で、リピートと速度は出さない（`SupportsRepeat` / `SupportsSpeed` = false）⑤Documentation~ の同梱マニュアルはリリース時の同期（P-9）に任せた ⑥PlayMode は実行していない（Editor 専用）。
