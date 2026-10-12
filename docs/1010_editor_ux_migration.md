# 1010. 専用エディターの新デザイン移行（仕様書）

> 2026-10-12 起票。**ユーザー決定: サンプル E（[1008](1008_editor_ux_redesign.md) §3 E、試験版 v1.7.0-preview.3）のデザインを承認。全専用エディターをこのデザインに移行する。** 旧エディターはアーカイブして `Tools > D-Drive > Archive` から開けるようにし、新エディター間の導線（「○○ Editor で開く」等）は旧エディターを使わない。旧エディターの機能はすべて引き継ぐ（機能同等）。試験で分かったこと（保存の経路・自動保存・Data 切替・固定バー・未調整の折りたたみ・調整つまみ・速度の ×1.0 デテント）をすべて反映する。
> チケットは [1004](1004_tasks.md) の UX 節（UX-1〜UX-20）。実装は Sonnet のエージェントに委任し、1 チケット = 1 PR。設計の背景と現状調査は [1008](1008_editor_ux_redesign.md)。

## 1. 決定事項（2026-10-12）

| # | 決定 |
|---|---|
| D-1 | 新エディターの見た目・構造は **E** を正とする（ヒーロー / セグメント〔かんたん・標準・詳細〕/ 目的別カード / 検索 / 未調整の折りたたみ / 固定アクションバー / 調整つまみ / 視覚部品 / 空状態 / インライン検証）。E 固有の割り切り（VfxData のみ・Dark のみ確認）は本実装で解消する |
| D-2 | **機能同等が完了条件**。各エディターのチケットは「旧エディターの機能の棚卸し表 → 全行『あり』」を PR に含める（E でやった [1008](1008_editor_ux_redesign.md) §5 の対応表と同じ形式） |
| D-3 | **旧エディターは削除せずアーカイブ**: コードは残し、メニューだけ `Tools > D-Drive > Archive > <旧名>` に移す（`DDriveMenu.Archive` を追加）。Inspector の「〜で開く」は新エディターが `Order = 0`、旧エディターは `(旧) 〜で開く` で `Order = 100` として残す（`[DataEditor]` 対応表は**追加のみ**になる。[42](42_distribution.md) §5.9）。旧エディターへの機能追加はしない（バグ修正のみ）。削除は次の MAJOR の候補として [1001](1001_open_items.md) に載せる |
| D-4 | **新エディター間の配線は新エディターだけを使う**: 「Anchor Editor で開く」「専用エディタで開く」「Prefab Editor で開く」などの `GetWindow<旧>` / `DataEditorRegistry` 経由の導線は、すべて新エディターへ向ける。旧エディター同士の既存の配線はそのまま（Archive の中で閉じる） |
| D-5 | **保存の経路**: デザイナーの編集の保存は `DDriveAssetSave.SaveDirty(target)`（版数・UpdatedAt が進む）。`SaveAllSuppressed` は機械処理（カタログ同期・新規作成直後 等）だけ。自動保存トグル（既定 OFF、EditorPrefs）・未保存のまま切替 / 閉じるときの確認・Ctrl+S でも保存できる tooltip は全エディター共通 |
| D-6 | **共通基盤はパッケージ側** `Packages/com.ddrive.core/Editor/Ux/`（asmdef は `DDrive.Editor` に含める。namespace `DDrive.Editor.Ux`）。試験版の `Editor/Prototypes/`（asmdef `DDrive.Editor.Prototypes`）は共通基盤へ移植したあと削除し、試験版タグ `v1.7.0-preview.*` も削除する（[1001](1001_open_items.md) の後始末） |
| D-7 | **互換性**: `DDrive.Foundation` / `DDrive.Runtime` の公開 API・シリアライズ形式・enum・Validator の重さに変更なし。Editor 契約（[42](42_distribution.md) §5.9、弱い互換面）の変更は「`DDriveMenu.Archive` の追加」「`[DataEditor]` 対応表の追加」「`Editors/` 配下のメニューが新エディターを指す」の 3 つで、いずれも CHANGELOG に書く。`editor-contract.txt` は追加のみで更新。**リリースは v1.8.0（MINOR）**、全エディターが揃ってから 1 回（途中の試験版は軽い経路で可） |
| D-8 | **検証**: 各チケットは Editor 専用なので EditMode green + コンパイル 0 + `ddrive_compat` 無変更 + **スクリーンショットの自己確認ループ（Dark と Light の両方、最低 2 周）** を完了条件にする。v1.8.0 のリリース時だけ run-ci 全段 |
| D-9 | マニュアル（`docs/DesignerManual/*.html`）は新エディターの機能説明に書き換える（機能のみ。「以前は〜」は書かない。[designer-manual の慣習](../CLAUDE.md)）。画像は新エディターのスクリーンショットに差し替える。旧エディターのページは `archive-*.html` に改名してトップの一覧から外す（リンクは「アーカイブ」節にまとめる） |

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
6. スクリーンショット（Dark / Light）を `docs/images/1010_<種別>.png` に、自己確認ループの記録（直した崩れ）を PR 本文に
7. マニュアルの該当ページの更新（D-9）

## 5. 実装メモ（チケットごとに追記）

（UX-1 から順に、ファイル一覧・対応表・割り切った点を追記する）
