# 28. 実装確認手順書（Phase 5 自律作業分、2026-09-14〜）

> 2026-09-14 からユーザー指示で Claude Code が自律実装した Phase 5 の **人による確認項目**。
> 自動検証（isuzu MCP: コンパイル 0 エラー / EditMode / PlayMode テスト）は各コミット時に通しているが、**見た目・音・操作感・実機・外部サービス（Google スプレッドシート等）**は未確認。上から順にやれば全機能を一巡できる。
>
> 共通の前提: Unity 6000.3.13f1。確認はすべて SceneView / Game ビュー / 確認用シーンで行う（ウィンドウ内描画は廃止、[09] §2）。
> 不具合を見つけたら、該当チケット行（[11_tasks.md](11_tasks.md)）と該当設計書の「実装メモ」を参照して修正する。
> 各節の見出しは「チケット番号 + 名前（PR / コミット）」。自律作業中に判断を保留した事項は各節末尾の「要判断」に書く。

## 5-10 アイコン表示の拡張（PR #11）

対象: `Editor/AssetBrowser/AssetBrowserWindow.cs`（一覧行のアイコン）、`Editor/Inspector/AssetDataInspector.cs`（`RenderStaticPreview`）、`Editor/Inspector/AssetIconService.cs`（`ScaleForPreview`）。設計は [09_editor_tools.md](09_editor_tools.md) §8.2。

1. **事前準備**: Inspector のアイコン行（§8.1）で、Icon が未設定の Data がいくつかあれば「自動生成」または `Tools > D-Drive > Generate > 初期アイコンを生成(未設定の Data のみ)` を実行し、少なくとも数種類（例: Se, Vfx, Material, Texture）に Icon を割り当てておく
2. **AssetBrowser の一覧**: `Tools > D-Drive > Asset Browser` を開く → 一覧の各行の左端に小さなアイコン画像が出ていること
   - Icon を割り当てた Data: Inspector のアイコン行と同じ画像がミニチュアで出る
   - Icon 未設定の Data: 空白ではなく、Unity 既定のサムネイル（ScriptableObject のスクリプトアイコンなど）が出る（真っ黒・例外・Console エラーが出ないこと）
   - 種別フィルタや検索で絞り込んでもアイコンが正しく行に追従すること（スクロールして仮想化された行が再利用されても、別の行のアイコンが残らないこと）
3. **Project ウィンドウ（グリッド表示）**: `Assets/GameData/` 配下の適当なフォルダ（例: `Icons` を割り当てた Se や Material の Data がある場所）を Project ウィンドウで開き、表示をグリッド（アイコンサイズを中〜大にするアイコン表示）に切り替える
   - Icon を割り当てた Data アセットのサムネイルが、その Icon 画像になっていること（ズームで大きくしても大きく崩れすぎないこと。128px 前後の元画像を拡大するため多少の滲みは正常）
   - Icon 未設定の Data アセットは Unity 既定のアイコン（変化なし）のままであること
   - Inspector で Icon を「クリア」または別画像に変更した直後、Project ウィンドウのサムネイルが（選択し直す・少し待つなどで）新しい状態に追従すること
4. **Inspector との整合**: いずれかの Data を選択し、Inspector 最上部のアイコン行のサムネイルと、AssetBrowser 一覧・Project ウィンドウのサムネイルが同じ画像に見えること

要判断:
- 生成済みアイコンは 128〜512px 止まり（Inspector のサイズ選択に準拠）。Project ウィンドウを最大ズームにしたときの滲みが実用上気になるレベルか、デザイナーの目で確認してほしい（気になる場合は [09] §8.2 の要判断を参照して上限サイズや縮小方法を見直す）
- AssetBrowser 未設定時のフォールバックは Unity 既定のミニサムネイル（`AssetPreview.GetMiniThumbnail`）。種別ごとに分かりやすい代替アイコン（例: 種別ロゴ）にすべきかは今回判断せず据え置いた

## 5-11 インポート検知による Data 自動生成（PR #12）

対象: `Editor/Import/ImportRuleService.cs`（+`ImportRulePostprocessor.cs` / `IImportRuleHandler.cs` / `ImportRuleHandlers.cs`）、`Foundation/Data/AssetDataBase.cs`（`ImportSourceGuid` 追加）。設計は [09_editor_tools.md](09_editor_tools.md) §1.1 / [10_workflow.md](10_workflow.md) §3.3。

事前準備: Unity Editor で `Assets/SourceAssets/` 配下に、確認用の一時サブフォルダ（例 `Assets/SourceAssets/_ImportRuleCheck/`）を作っておく（確認後にまとめて削除できるように、実運用フォルダと混ぜない）。

1. **Se**: `Assets/SourceAssets/_ImportRuleCheck/Se/Check/` に音声ファイル（.wav 等）を 1 つドラッグ＆ドロップで置く → 数秒後（Console に `[DDrive] ImportRule: ...` のログが出る）に `Assets/GameData/Audio/SE/Check/SE_Check_<ファイル名>.asset` が自動生成されていること。AssetBrowser で開き、Clips に置いた音声が入っていること
2. **Bgm**: 同様に `.../Bgm/Check/` に音声ファイルを置く → `Assets/GameData/Audio/BGM/Check/BGM_Check_<ファイル名>.asset` が生成され、LoopBody に音声が入っていること
3. **Texture**: `.../Texture/Check/` に画像ファイル（.png 等）を置く → `Assets/GameData/Texture/Check/TEX_Check_<ファイル名>.asset` が生成され、Texture に画像が入っていること
4. **Model**: `.../Model/Check/` に FBX を置く → `Assets/GameData/Model/Check/MODEL_Check_<ファイル名>.asset` が生成され、Prefab に FBX のルートが入っていること（同時に Maya→Material 経路で MaterialData/TextureData も生成されていれば正常な共存)
5. **Anim**: `.../Anim/Check/` に `.anim` ファイル（既存の AnimationClip をコピーするか、AnimEditor で作った物を配置）を置く → `Assets/GameData/Anim/Check/ANIM_Check_<ファイル名>.asset` が生成され、Clip が入っていること
6. **Anim2D**: `.../Anim2D/Check/` に `.anim` ファイルを置く → `Assets/GameData/Anim2D/Check/ANIM2D_Check_<ファイル名>.asset` が生成され、Clip が入っていること（Directions=None のまま。方向づけは Anim2DEditor で追加する）
7. **Prefab**: `.../Prefab/Check/` に Prefab を置く → `Assets/GameData/Prefab/Check/PREFAB_Check_<ファイル名>.asset` が生成され、Prefab が入っていること
8. **Canvas**: `.../Canvas/Check/` に UI Prefab を置く → `Assets/GameData/Canvas/Check/CANVAS_Check_<ファイル名>.asset` が生成され、Prefab が入っていること
9. **Vfx**: `.../Vfx/Check/` に ParticleSystem/VFX Graph の Prefab を置く → `Assets/GameData/Vfx/Check/VFX_Check_<ファイル名>.asset` が生成され、Prefab が入っていること
10. **二重生成しないこと**: 上記のいずれか 1 つを選び、そのファイルを右クリック →「Reimport」（または一度別プロジェクトへコピーして戻す）を行っても、対応する Data が増えず 1 個のままであること
11. **欠落表示**: 手順 1〜9 のいずれかで作った元ファイルを 1 つ削除する → 対応する Data 自体は消えずに残ること、AssetBrowser の ⚠ Validation（または `Tools > D-Drive > Validation > Run All`）でその Data が Error（「未設定(または Missing)です」）として表示されること
12. **AutoImport=OFF 相当の手動フォールバック**: 上記の一時フォルダ全体を一度削除し、別の場所に同じ構成のファイル一式を用意した状態で `Tools > D-Drive > Generate > SourceAssets からインポートルールを再実行` を実行 → Console にまとめて生成ログが出て、対応する Data が一括生成されること
13. 確認が終わったら、`Assets/SourceAssets/_ImportRuleCheck/` と生成された `Assets/GameData/**/Check/` 配下の Data 一式を Unity Editor から削除する（AssetBrowser の削除機能、または Project ウィンドウで `Assets/SourceAssets/_ImportRuleCheck` フォルダと `Assets/GameData/*/Check` フォルダを削除して Addressables のエントリも合わせて外す）

要判断:
- **Anim2D の元ファイルの解釈**: スプライトシート/Texture からの自動スライス(既存 Anim2DEditor のワークフローと重複)ではなく、`AnimData` と同じ「単一の `.anim`/`.fbx` を `Clip` に設定するだけの Placeholder」を採用した。方向づけ(`DirectionClips`)は既存の Anim2DEditor(3-11/3-12)で追加する運用。デザイナーの実際のワークフロー(スプライトから作ることが多いのか、既存クリップの流用が多いのか)によって、Texture フォルダ起点にすべきかどうかは要判断
- **Anim の複数テイク FBX**: 1 つの FBX に複数の `AnimationClip` が埋め込まれている場合、`ImportRule` は先頭 1 本(`__preview__` を除く)だけを取り込む。複数テイクを個別の `AnimData` に分けたい運用が多い場合は、ファイル単位でなくクリップ単位の複数生成へ拡張するか、テイクごとに FBX を分けて Export する運用にするかは要判断
- **Model の Prefab 直参照**: `ModelData.Prefab` に FBX のインポート直後のルート GameObject をそのまま設定する。Animator/追加コンポーネントを載せたラッパー Prefab を挟む運用がある場合、そのラッパー生成までは自動化していない(現状は ModelEditor 等で手動差し替え)
- **Texture の Usage/Channel 既定値**: `TextureImportProfile` の命名規約(`_N`/`_M`/`_UI` 等)に一致すればその既定値、一致しなければ `TextureData` のクラス既定値(Model/Albedo)のまま。UI 用テクスチャを規約に合わない名前で置いた場合は手動で Usage を直す必要がある
- **既存 Data と同名衝突時の挙動は未検証**: 手動で同じ識別子の Data を先に作っていた場合、`AssetCreationService.Create` が別ファイルとして作成する(既存の重複回避ロジックに委ねている)。運用上どちらが優先されるべきかは今回判断していない
- `AssetDataBase` に `[HideInInspector] string ImportSourceGuid` を追加した(シリアライズ形式の変更＝フィールド追加のみ。既存 Data は空文字で読み込まれ互換性に問題なし)。CLAUDE.md §0-9 の事前確認を自律作業中のため省略したので、問題があれば指摘してほしい

## 5-12 仕様書テンプレート（PR #13）

対象: `docs/SpecSheetTemplate/`（`DDrive_仕様書テンプレート.xlsx` / `make_template.py` / `README.md`）、[27_spec_sheet.md](27_spec_sheet.md) §7.1。

1. `docs/SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx` を Google ドライブにアップロードし、「アプリで開く → Google スプレッドシート」で開けること
2. 共有設定を「リンクを知っている全員が閲覧可」にして、別アカウント（またはシークレットウィンドウ）で内容が見えること
3. `アセット` タブの「種別」「状態」列、`調整値` タブの「型」列のプルダウンが Google スプレッドシート上でも効くこと（xlsx → Google Sheets 変換で入力規則が引き継がれるかは未検証）。`_選択肢` タブが非表示になっていること
4. `機能_サンプル` タブを複製して `機能_<名前>` を作れ、体裁が崩れないこと

要判断:
- [27] §7.1 の暫定既定（取得方法 A = リンク共有 + CSV / 調整値を D-Drive に取り込む / テンプレートはユーザーが自分のドライブへアップロード）を正式決定とするか
- `アセット` タブの「種別」表記を AssetType の enum 名（Se / Bgm / Vfx …）にした。デザイナーに馴染みのあるファイル名接頭辞（SE / BGM / VFX …）の方がよいか

## 5-15 各エディタの「＋ 新規作成」（PR #14）

対象: `Editor/Inspector/NewAssetToolbarButton.cs`（共通ヘルパー）、`Editor/AssetBrowser/NewAssetDialog.cs`（`Open(Type[], Action<AssetDataBase>)` を新設）、各専用エディタのツールバー 16 か所。設計は [09_editor_tools.md](09_editor_tools.md) §8.3。

各エディタを `Tools > D-Drive > Editors > …` から開き、ツールバー（多くは対象アセットの ObjectField や🔒トグルと同じ行、一部はウィンドウ最上部の単独行）にある **「＋ 新規作成」** ボタンを押す → `NewAssetDialog` が開き、種別ドロップダウンがそのエディタの対応種別だけに絞られていること（候補が 1 つなら選べず固定表示）を確認 → 表示名・カテゴリ・識別子を入力して「作成」→ **ダイアログが閉じて、元のエディタの対象がその場で作った新規アセットに切り替わっていること**（対象アセットの ObjectField に反映される。Anchor/Anchor Group/Model/Vfx/Anim は SceneView 側の表示も追従する）を確認する。

| エディタ（メニュー） | ボタンを押すと固定される種別 | 作成後に切り替わる対象 |
|---|---|---|
| Audio | SeData / BgmData（2 択） | 対象アセット（波形表示が空の新規データになる） |
| VFX | VfxData | 対象アセット |
| Animation (3D) | AnimData | 対象アセット |
| Animation (2D) | Anim2DData | 編集モードに切り替わり、Clip 未設定の新規データが対象になる |
| Prefab | PrefabData | 対象アセット |
| Canvas | CanvasData | 対象アセット |
| Material | MaterialData / TextureData（2 択） | 対象アセット |
| Material 変換 | MaterialData | 「変換元 MaterialData」欄 |
| Material プレビュー | MaterialData | サムネイル対象（タイトルバーの名前も切り替わる） |
| Anchor | AnchorData | 対象アセット |
| Anchor Group | AnchorGroupData | 対象アセット |
| Button Skin | ButtonSkinData | 対象 Skin |
| Slider | SliderSkinData | 確認用シーンにそのスキンのサンプルスライダーが配置される（対象は UiSlider のサンプル） |
| Slider Skin | SliderSkinData | 対象 Skin |
| UI Tween | UiTweenData | 対象 UiTweenData |

いずれも AssetBrowser を一度も開かずに完了できること、Console に想定外の Error が出ないこと（作成先エディタが無い/閉じている等の異常系で警告ログが 1 行出るのは正常）を確認する。

要判断:
- **二次的な専用エディタにも同じボタンを付けた**: `MaterialConvertWindow`（既存 MaterialData をシェーダー変換する画面。「新規 MaterialData として作成」という別の作成手段を既に持つ）、`MaterialThumbnailWindow`（サムネイルだけの独立ポップアップ）、`SliderEditorWindow`（`SliderSkinData` の応答曲線・追従比較などの高度編集。`SliderSkinEditorWindow` の方が本来の作成入口）は、チケットの「`[DataEditor]` 付きの全専用エディタ」を文字どおり解釈して含めた。実際にはこの 3 つは「新規に作る場所」としては使われにくく、ボタンが冗長・混乱の元と感じる場合は `NewAssetToolbarButton` の呼び出しをこの 3 か所だけ外すことを検討してほしい
- **種別ロックはドロップダウンを消さず選択肢を絞る方式にした**: Audio(SE/BGM)・Material(MaterialData/TextureData) のように 1 エディタが複数種別を持つ場合、ドロップダウン自体は残して候補をその 2 つだけに絞っている(候補が 1 つの場合のみ無効化して見せる)。「エディタごとに 1 種別に固定」という文言を厳密に取るなら、Audio/Material では追加のトグルなどでどちらを作るか明示すべきかは要判断
- **Anim2D は「空の Placeholder」を作る**: Anim2DEditorWindow は元々スプライト分割からクリップまで一括生成する「作成」モードを持つが、「＋ 新規作成」は(ImportRule と同じ考え方で)Clip 未設定の Anim2DData をまず作って編集モードに切り替えるだけにした。ID だけ先に確保して後でスプライトを割り当てる、という D-Drive の基本コンセプトには合致するはずだが、既存の「作成」モードと役割が重複して見えないかは要判断

## 5-13 仕様書同期（PR #15）

対象: `Assets/DDrive/Editor/Spec/*`(新規)、`Assets/DDrive/Editor/Codegen/TuningCodegen.cs`(新規)、`Assets/DDrive/Runtime/Tuning/*`(新規)、`Assets/DDrive/Runtime/Loop/DDriveRuntimeBootstrap.cs`(`TuningTable` 直参照を追加)。設計は [27_spec_sheet.md](27_spec_sheet.md) §4/§8。

実際の Google スプレッドシートを使った確認手順(テンプレートは [docs/SpecSheetTemplate/](SpecSheetTemplate/) を参照):

1. [docs/SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx](SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx) を自分の Google ドライブへアップロードし、「アプリで開く」→「Google スプレッドシート」で開く
2. 共有設定を「リンクを知っている全員が閲覧可」にする
3. `アセット` タブに 1 行追加する(例: 種別 `Se` / カテゴリ `Player` / 識別子 `Check1` / 表示名「確認用効果音」/ 状態「仮」/ 担当に自分の名前 / 仕様列に人向けタブのセルへのリンク / 備考に何か一言)
4. Unity で `Tools > D-Drive > 仕様書と同期` を開く。「スプレッドシート URL」にシートの URL を貼り「設定を保存」→「取得」
5. 「新規」に手順3の行が出ること。チェックが付いた状態で「適用」を押す → `Assets/GameData/Audio/SE/Player/SE_Player_Check1.asset` のような Placeholder(`SeData`)が作られ、Inspector の Tags に `State/仮`、Assignee に担当、Description に備考、SpecUrl に仕様列のリンクが入っていること
6. 手順3の行の「表示名」「状態」を書き換えて Unity 側で再度「取得」→「変更」に出ること。「適用」を押すと該当項目だけが上書きされ、手順5で作った Data 自体(ファイル・ID)は変わらないこと
7. 手順3の行をシートから削除して再度「取得」→「新規」「変更」には出ず、「シートから消えた(Archive 候補)」に手順5の Data が表示されるだけで、実際には削除されていないこと(Project ウィンドウにファイルが残っている)
8. 「調整値」タブに `Check/Value,1.5,float,0,10,,確認用` のような行を追加して「取得」→「適用」(「調整値も同期する」にチェックが入っていること)。`Tools > D-Drive > Generate > Regenerate Tuning Keys` を実行 → `Assets/Generated/Tuning.g.cs` に `TUNING.CheckValue` が生成されること
9. Console やテストコードから `DDrive.Runtime.Tuning.Tuning.GetFloat(DDrive.Generated.TUNING.CheckValue)` を呼んで `1.5f` が返ること(Editor から `execute_code` 等で確認、またはテストコードを一時的に書いて確認)
10. 「選択肢をコピー」「既存アセットをコピー」を押し、クリップボードに TSV がコピーされること(貼り付け先はテキストエディタで確認して構わない)
11. Unity を再起動(またはドメインリロード)し、事前に URL・自動取得 ON を設定しておいた状態で、Asset Browser のツールバーに「仕様書に変更 n 件」のバッジが自動で出ること(手順3〜7 を通しで試すなら、シートに未反映の行を残した状態で再起動する)
12. 確認が終わったら、手順5で作った `Assets/GameData/Audio/SE/Player/SE_Player_Check1.asset`(存在すれば)と `Assets/GameData/Settings/DDriveSpecSettings.asset` / `DDriveTuningTable.asset` を Unity Editor から削除する(Addressables のエントリも合わせて外す)

要判断:
- **Archive 候補の絞り込み**: `SpecUrl` が設定済みのアセットだけを対象にした(§9-1 参照)。運用開始時にまだ 1 度も同期していない既存アセットは対象に入らない
- **識別子の逆算方式(新フィールドを増やさない)**: ファイル名から `_` 区切りの最後のトークンを識別子として逆算している(§9-2 参照)。カテゴリだけを変えて識別子を変えていない場合、次回同期での結び付けが外れる可能性がある
- **ControlSkin の自動作成不可**: `ButtonSkinData`/`SliderSkinData` のどちらを作るか一意に決められないため、シートの新規行が `ControlSkin` のときは自動作成をスキップする(§9-3 参照)
- **自動同期のテスト検出**: `Application.isBatchMode` と `-runTests` 引数だけで判定しており、Test Runner ウィンドウからの対話的実行は検出できない(§9-4 参照。実害は無い設計だが要確認)
- **リネーム結び付け(§4.3 末尾)は未実装**
- **`DDriveSpecSettings`/`DDriveTuningTable` の .asset は今回コミットしていない**: `Assets/GameData/Settings/` に必要になったときだけ自動生成される。ユーザーが実際に同期を試すと生成されるので、その時点でコミットするか判断してほしい

## 5-14 仕様書リンク（PR #15）

対象: `Assets/DDrive/Foundation/Data/AssetDataBase.cs`(`SpecUrl`/`Assignee` フィールド追加)、`Assets/DDrive/Editor/Inspector/SpecUrlGui.cs`(新規)、`Assets/DDrive/Editor/Inspector/AssetDataInspector.cs`。

1. 任意の Data アセット(例: 5-13 の手順5で作った Placeholder、または既存の SeData)を選び、Inspector の `SpecUrl` フィールドに URL を入力する
2. Inspector 最上部(アイコン行の下)に「📄 仕様書を開く」ボタンが出ること。押すとブラウザで該当 URL が開くこと
3. `SpecUrl` を空にすると、ボタン自体が表示されなくなること(無効表示ではなく非表示)
4. 5-13 の手順3〜5 のとおり仕様書経由で同期した場合、「仕様」列に書いたリンクが自動的に `SpecUrl` に入り、同じボタンが機能すること

要判断:
- 特になし(5-13 の要判断と共通)

## 5-16 新規作成ダイアログ「仕様書から選ぶ」（PR #16）

対象: `Assets/DDrive/Editor/AssetBrowser/NewAssetDialog.cs`(「仕様書から選ぶ」セクション、「備考」「仕様リンク」欄を新設)、`Assets/DDrive/Editor/Spec/SpecSyncService.cs`(`ApplyExtraFields` を `public` 化)、`Assets/DDrive/Editor/Spec/SpecCache.cs`(`RecomputeDiff()` を新設)。設計は [27_spec_sheet.md](27_spec_sheet.md) §4.5.1/§9.1、[09_editor_tools.md](09_editor_tools.md) §8.5。

実際の Google スプレッドシートを使った確認手順(5-13 の手順1〜4 を先に済ませ、仕様書と同期済みの状態から始める):

1. 仕様書の `アセット` タブに、まだ D-Drive に無い行を追加する(例: 種別 `Se` / カテゴリ `Player` / 識別子 `Check5016` / 表示名「確認用効果音2」/ 状態「仮」/ 担当に自分の名前 / 仕様列にリンク / 備考に一言)
2. `Tools > D-Drive > 仕様書と同期` を開いて「取得」を押す(この時点ではまだ「適用」しない。新規行がキャッシュに乗るだけでよい)
3. `Tools > D-Drive > Asset Browser` を開き「新規」を押す(または任意の専用エディタの「＋ 新規作成」)。ダイアログ最上部の「仕様書から選ぶ」に、最終取得時刻と一覧(検索欄付き)が出ること。手順1の行が一覧にあること
4. 検索欄に識別子や表示名の一部を入れて、一覧が絞り込まれることを確認する
5. 手順1の行の「選ぶ」を押す → 種別・カテゴリ・識別子・表示名・備考・仕様リンクの各欄が自動で入ること(値は書き換えてもよい)
6. 「作成」を押す → `Assets/GameData/Audio/SE/Player/SE_Player_Check5016.asset` のような Placeholder が作られ、Inspector の Tags に `State/仮`、Assignee に担当、Description に備考、SpecUrl に仕様リンクが入っていること(5-13 の同期で作った場合と同じ結果になっていること)
7. ダイアログをもう一度開く(または `Tools > D-Drive > 仕様書と同期` の「新規」一覧を見る) → 手順1の行が「仕様書から選ぶ」一覧・同期の「新規」一覧のどちらからも消えていること(ネットへ再取得しなくても消えている)
8. いずれかの専用エディタ(例: Audio Editor)の「＋ 新規作成」から同じダイアログを開き、「仕様書から選ぶ」の一覧がそのエディタの対応種別だけに絞られていること(例: Audio Editor なら Se/Bgm の行だけ、他の種別の未作成行は出ない)
9. `Tools > D-Drive > 仕様書と同期` を開いたまま設定の「スプレッドシート URL」を空にして保存し、ダイアログを開き直す → 「仕様書から選ぶ」が案内文だけになり、一覧・検索欄が出ないこと。確認後は URL を戻しておく
10. 確認が終わったら、手順6で作った `SE_Player_Check5016.asset` を削除する(Addressables のエントリも合わせて外す)

要判断:
- **選択後に他の欄を手で書き換えても Status/Assignee は選択時のまま**: ダイアログに Status/Assignee 専用の入力欄が無いため、行を選んだ後に他の欄を書き換えても、作成時の Status/Assignee は「選んだ時点の行」のものになる([27] §9.1-8 参照)
- **キャッシュの古さの閾値(1 時間)は暫定**: 根拠のある値ではない。運用してみて長すぎる/短すぎるかを判断してほしい([27] §9.1-9 参照)
- ~~**`NewAssetDialog` に `gameDataRoot` のテスト用オーバーライドが無い**~~ → **2026-09-14 対応済み(PR #17、P5 テスト隔離)**: `NewAssetDialog.TestGameDataRootOverride` / `TestSpecSettingsOverride` を追加し、`NewAssetDialogSpecPickerTests` は実 `Assets/GameData`・実カタログ・実 Addressables・実 `DDriveSpecSettings.asset` に一切触れなくなった
- **「備考」「仕様リンク」欄はダイアログの全ケース(手入力のみの作成も含む)に常時表示**: 仕様書から選ばない通常の手入力作成でも入力できるようにした(空でも作成可、既存動作に影響なし)。専用エディタからのロック付き作成でも同じ欄が出る。UI が煩雑に見える場合は「仕様書から選ぶ」を使っている時だけ表示する等の整理を検討してほしい

## 5-5 依存関係グラフ（PR #18）

対象: `Assets/DDrive/Editor/Dependencies/`(新規: `DependencyGraphService.cs`/`DependencyGraphCollector.cs`/`DependencyGraphCache.cs`/`DependencyGraphPostprocessor.cs`/`DependencyGraphTypes.cs`)。UI は 5-6 で作るため、今回はメニュー + ログのみ。設計は [09_editor_tools.md](09_editor_tools.md) §10、[02_core_framework.md](02_core_framework.md) §12。

確認手順:

1. `Tools > D-Drive > Generate > 依存関係グラフを再構築` を実行する → Console に `[DDrive] DependencyGraph: 再構築完了(対象 N ファイル, 参照 M 件)` のログが出ること(プロジェクト内の全 Data/Prefab/Scene を開閉して走査するため、Scene 数によっては数秒〜数十秒かかる)
2. 任意の Data(例: `SeData`)の `AnchorId` に値を設定して保存する → 数秒後(delayCall)に自動で差分更新される(明示的なログは出さない設計。確認は次項の API 経由、または一旦 Unity を再起動して `依存関係グラフを再構築` を実行し直し件数が増えていることで代用してもよい)
3. Scene に `SeEmitter` 等の `AssetId<TMarker>` フィールドを持つ MonoBehaviour を配置して ID を設定し保存する → 上記同様に差分更新されるはず(シーンの Open/Close を伴うため、保存直後に若干のカクつきが起きても異常ではない)
4. `Tools > D-Drive > Generate > 依存関係グラフを再構築` を再実行し、件数ログが手順2・3の分だけ増えていること
5. Play Mode に入った状態で Data を編集した場合(通常運用ではまず起きないが)、差分更新が Edit Mode に戻るまで保留され、エラーが出ないこと

要判断:
- **起動時 / ドメインリロード時の自動再構築をしていない**: 全 Scene の Open/Close が重く、デザイナーの作業を止めない方針(CLAUDE.md §0-4)を優先した。`Library/DDriveDeps/` を消した直後や初回導入時は空なので、手動で 1 度「依存関係グラフを再構築」を実行する必要がある。運用してみて不便なら、軽量な整合性チェック(ファイルの内容ハッシュ比較。`DependencyFileRecord.ContentHash` は実装済みで保存だけしている)を起動時に追加することを検討してほしい
- **循環参照検出は未実装**: 5-6 の依存ツリー UI 側で深さ優先探索時に検出する想定
- **`[SerializeReference]` は未検証**: 2026-09-14 時点でプロジェクト内に使用箇所が無いため、実際の多態フィールドでの動作は未確認(収集ロジック自体は Unity の `SerializedProperty` 標準走査に乗っているため動くはずという設計判断)
- **循環参照や巨大な依存グラフでのパフォーマンスは未計測**: 現状のプロジェクト規模(Scene 17・Data 数百件程度)では `RebuildAll` が数秒〜十数秒で完了することを確認したのみ

## 5-6 使用箇所検索 / 未使用検出 / 安全な削除（PR #19）

対象: `Assets/DDrive/Editor/Dependencies/`(新規: `UsagesWindow.cs`/`DependencyTreeWindow.cs`/`DependencyTreeNode.cs`/`UnusedAssetsWindow.cs`/`ArchiveTagService.cs`/`SafeDeleteService.cs`/`DependencyJumpService.cs`/`DependencyAssetResolver.cs`/`CodeReferenceScan.cs`)、`Editor/AssetBrowser/AssetBrowserWindow.cs`(行の右クリックメニュー・「未使用...」ボタン)、`Foundation/Registry/AssetCatalog.cs`(`Remove(id)` 新設)。設計は [09_editor_tools.md](09_editor_tools.md) §1 / §10、運用は [10_workflow.md](10_workflow.md) §3、デザイナー向けは [DesignerManual/asset-browser.html](DesignerManual/asset-browser.html)。

事前準備: `Tools > D-Drive > Generate > 依存関係グラフを再構築` を一度実行しておく(5-5 の要判断どおり、Library を消した直後・導入直後は索引が空)。

確認手順:

1. **グラフ再構築**: 上記の事前準備を実施し、Console にログが出ることを確認
2. **使用箇所検索**: `Tools > D-Drive > Asset Browser` を開き、どこかから参照されている Data(例: 既存の `SeEmitter` 等から使われている SE)を右クリック →「使用箇所を表示」→ 参照元の一覧(パス・オブジェクトパス・コンポーネント.プロパティ)が出ること。逆にどこからも使われていない Data では「どこからも参照されていません」と出ること
3. **ダブルクリックジャンプ(Data)**: 使用箇所一覧で参照元が Data(.asset)の行をダブルクリック → Project ウィンドウでその Data が選択・ハイライトされること
4. **ダブルクリックジャンプ(Prefab)**: 参照元が Prefab の行をダブルクリック → Project ウィンドウで Prefab 内の該当 GameObject(無ければ Prefab 自身)が選択されること
5. **ダブルクリックジャンプ(Scene)**: 参照元が Scene の行をダブルクリック → 「開きますか?」の確認ダイアログが出ること。今のシーンに未保存の変更がある状態で試し、保存確認ダイアログも正しく出ること。「開く」を選ぶとシーンが開き、該当オブジェクトが選択されること
6. **依存ツリー**: 何かを参照している Data(例: Prefab や、AnchorId を設定した SE)を右クリック →「依存ツリーを表示」→ 参照先がツリーで展開されること。可能なら A→B→A のような循環参照を作る Data を用意し、循環箇所が打ち切り表示(色つき)になることを確認
7. **未使用一覧**: AssetBrowser ツールバーの「未使用...」を押す → どこからも参照されていない Data の一覧が出ること。いくつかチェックを入れて「選択項目を一括Archive」→ Console にログが出て、対象の Inspector の Tags に `Archived` が付くこと(削除はされないこと)
8. **参照ありは削除不可**: 何かから参照されている Data を右クリック →「削除...」→ 参照元一覧が出て中止されること(削除されていないこと・カタログ/Addressables 登録もそのままであること)
9. **参照なしの削除**: テスト用に何にも参照されていない Data を1つ用意し(例: 手順7で Archive したもの、または新規作成して未使用のまま)、右クリック →「削除...」→ 確認ダイアログの内容を確認してから「削除する」を押す → 一覧から消えること、`Assets/GameData/Catalogs/` の対応するカタログからエントリが消えること(カタログ .asset をテキストエディタ等で開いて確認)、Addressables Groups ウィンドウ(`Window > Asset Management > Addressables > Groups`)から該当エントリが消えていること
10. **ゴミ箱からの復元**: 手順9で削除した Data とアイコン画像を OS のゴミ箱(Windows のごみ箱)から元の場所へ復元する → Unity がファイルを再インポートし、Project ウィンドウにアセットとして戻ってくることを確認する。ただし **カタログ・Addressables の登録は自動では戻らない**こと(AssetBrowser の一覧には出ない・Validation で登録漏れとして検出される)も合わせて確認する
11. **コード参照の警告**: 生成済み ID 定数(`Assets/Generated/AssetIds.g.cs`)がコードから実際に参照されている Data を1つ選び、右クリック →「削除...」の確認ダイアログに「生成された ID 定数 '...' を参照しているコードが見つかりました」という注意が出ること(**そのまま削除は実行せずキャンセルする** — 実際に削除するとコンパイルエラーになるため)
12. 確認で作った一時 Data・Archive 済みタグはテスト後に元に戻す(Archive を解除する、または実際に不要なら安全な削除の手順で片付ける)

要判断:
- **グラフ未構築の判定は `CachedFileCount == 0` のみ**: 「古いが空ではない」状態は検出できない(5-5 の要判断を引き継ぐ)。手順1を飛ばして削除した場合の実際の挙動(「先に再構築してください」と出て中止されること)も合わせて確認してほしい
- **依存ツリーは事前に全展開**: 巨大な依存グラフ(1 アセットが数百件を再帰的に参照する等)での表示速度は未計測。実際に触ってみて重いと感じたら [09] §10 の要判断を参照して遅延展開への切り替えを検討する
- **コード参照チェックは grep ベースの簡易実装**: 誤検知(コメント中の文字列等にヒット)・見逃し(リフレクション経由の参照等)があり得る。実運用でノイズが多い/少なすぎると感じたら精度改善を検討する
- **Scene ジャンプの自動テストは無し**: `EditorSceneManager.OpenScene(Single)` がアクティブシーンを差し替える副作用があるため、自動テストは `.asset`/`.prefab` 分岐のみ(`DependencyJumpServiceTests`)。手順5の手動確認で代替している
- **Archived タグは `AssetDataBase.Tags` への予約語追加**: TagCatalog(選択制の辞書。未実装)が将来入る場合、`"Archived"` を予約語として除外するか、専用フィールドへの移行を検討する必要がある
- **「1 リリース後に削除」の自動化はしていない**: Archived タグが付いてからどれくらい経過したら安全に削除してよいかの判断・催促は今回自動化せず、人が未使用一覧を見て判断する運用のまま

## 5-7 Preload 自動集計 + シーンロード統合（PR #20）

対象: `Editor/Preload/`(新規: `ScenePreloadAggregator.cs`/`ScenePreloadGenerator.cs`/`ScenePreloadBuildPreprocessor.cs`)、`Runtime/Loading/`(新規: `PreloadEntry.cs`/`ScenePreloadList.cs`/`ScenePreload.cs`/`SceneLoadingScreen.cs`)、`Foundation/Registry/IAssetRegistry.cs`+`AssetRegistry.cs`(`PreloadIdsAsync`/`ReleaseIds` 新設)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(`ScenePreload.Bind`/`Unbind` 追加)。設計は [10_workflow.md](10_workflow.md) §5、[02_core_framework.md](02_core_framework.md) §5/§14、[09_editor_tools.md](09_editor_tools.md) §10 の 5-7 節。

事前準備: `Tools > D-Drive > Generate > 依存関係グラフを再構築` を一度実行しておく(5-5/5-6 と同じ前提)。

確認手順:

1. **集計メニュー(現在のシーン)**: 何らかの ID 参照(`SeEmitter` 等)を含む適当な確認用シーンを開いて保存し、`Tools > D-Drive > Generate > Preload リストを再集計(現在のシーン)` を実行する → Console に `[DDrive] Preload リストを更新しました: '...'（N 件）` のログが出て、`Assets/GameData/Preload/<シーン名>_PreloadList.asset` が生成され Project ウィンドウで選択状態になること
2. **中身の妥当性**: 手順1で生成された `ScenePreloadList` の Inspector を開き、`Entries` にそのシーンが直接・間接に参照する ID が入っていること(表示名 `DisplayName` が実際のアセット名と一致していること)。シーンから参照していない Data が混ざっていないこと
3. **再実行で重複しない**: 同じシーンでもう一度「Preload リストを再集計(現在のシーン)」を実行する → 同じ `.asset` が更新されるだけで新しいファイルが増えないこと(Project ウィンドウの `Preload` フォルダのファイル数が変わらないこと)
4. **全ビルドシーン一括**: `Tools > D-Drive > Generate > Preload リストを再集計(ビルド設定の全シーン)` を実行する → Build Settings(`File > Build Settings...`)に登録されている有効シーンの数だけ `.asset` が更新されること
5. **確認用シーンで Preload を Play**: 手順1のシーン(または新規の確認用シーン)に `DDriveRuntimeBootstrap` を配置し(`Tools > D-Drive > Generate > 起動オブジェクトをシーンに配置`)、空の GameObject に `SceneLoadingScreen` コンポーネントを追加して `Preload List` に手順1の `.asset` をアサインする(`Progress Slider`/`Progress Text` は uGUI の `Slider`/`Text` があれば割り当てる、無くても動作は確認できる)。Play Mode に入る → 進捗が 0 から 1 まで進み、`IsDone` が true になること(Slider/Text を割り当てていれば見た目でも進むこと)。Console にエラーが出ないこと
6. **未登録 ID のスキップ**: 手順2の `Entries` のどれか1件の ID を Inspector で書き換えて(存在しない値にする)保存し、再度 Play Mode で `SceneLoadingScreen` を走らせる → その ID については `[DDrive] ScenePreload: Unregistered AssetId 0x... was skipped.` という警告が出るだけで、Preload 全体は止まらず完了すること。確認後は書き換えた値を元に戻す(または `.asset` ごと破棄する)
7. **ビルド前フック**: 実際の開発ビルドを1回実行する(時間があれば。Development Build で可) → Console に `[DDrive] ビルド前処理: Preload リストを N シーン分更新しました。` のログが出て、ビルドが正常に完了すること
8. 確認で作った `SceneLoadingScreen` 付き GameObject・`DDriveRuntimeBootstrap`・テスト用に書き換えた `.asset` の中身は元に戻す(または確認用シーンごと破棄する)

要判断:
- **シーン→Preload リストの対応付けが「シーンに置いたコンポーネントの直参照」のみ**: `AssetCatalog`/`Catalogs[]` のような中央インデックスは作っていない。複数シーンをまとめて Preload するタイトル画面等が要る場合は `DDriveRuntimeBootstrap` に `ScenePreloadList[]` を足す拡張を検討してほしい([09] §10 5-7 節参照)
- **`GenerateForAllBuildScenes` とビルド前フックは自動テスト対象外**: `EditorBuildSettings.scenes`(git 管理下の `ProjectSettings/EditorBuildSettings.asset`)を書き換えるため、実プロジェクトの設定を汚すリスクを避けて自動テストにしなかった。上記手順4・7で手動確認する
- **Preload の粒度は Data(.asset)単位**: Data 内部の AudioClip/Texture/Prefab 等のサブアセットを個別に先読みする経路は無い(Addressables の依存バンドルとして一緒にロードされる前提)。体感のロード時間短縮効果は未実測
- **ロード画面 UI は最小実装**: `SceneLoadingScreen` は uGUI の `Slider`/`Text` を任意で受けるだけの確認用コンポーネントで、デザイナー向けの正式なロード画面(Canvas/UiManager ベース)は未実装。実運用では置き換えを検討してほしい
- **参照カウントの解放漏れリスク**: `ScenePreload.RunAsync` で確保した参照は対応する `ScenePreload.Release` を呼ぶまで解放されない。`SceneLoadingScreen.OnDisable` では解放するが、独自に `ScenePreload.RunAsync` を呼ぶコードを書く場合は解放を呼び忘れないよう注意が要る

## 5-1 Presentation（PR #21）

対象: `Runtime/Presentation/{PresentationData,PresentationTrack,PlayContext,PresentationManager,PresentationHandle,Presentation,PresentationTiming,PresentationDataValidator}.cs`(新規)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(Presentation 配線追加)、`Editor/AssetBrowser/AssetCreationService.cs`(Presentation を Preload 既定に追加)、`Samples/PresentationSkillSlashDemo.cs`(新規、確認用)。設計は [08_presentation.md](08_presentation.md)、依存パッケージは [01_architecture.md](01_architecture.md) §4。

事前準備: Unity Package Manager(`Window > Package Manager`)で「Unity NuGet」レジストリ経由の `R3`(1.3.1)と `R3 (Utility for Unity)`(`com.cysharp.r3`)が入っていることを確認する(`Packages/manifest.json` に記載済みなので、プロジェクトを開いた時点で自動解決されるはず。初回だけ Package Manager の「Importing a scoped registry」ダイアログが出ることがある。「Close」で進めてよい)。

確認手順:

1. **剣攻撃デモ**: `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity` を開いて Play Mode に入る → 起動直後に **1 API(`Presentation.Play(PRESENTID.DemoSkillSlash 相当, ctx)`)** で VFX(`VFX_Player_Slash`)と SE(`SE_Player_Slash`)が Self(`Player` オブジェクト)の位置で同時に再生されること(Console に `[PresentationSkillSlashDemo] Play() -> IsPlaying=True` が出る)
2. **Signal("hit")**: Space キーを押す → 画面が一瞬止まる(ヒットストップ、約 0.08 秒)のと同時に SE がもう一度鳴ること(Console に `Signal("hit") -> HitStop + SE` が出る)。連打しても例外が出ないこと
3. **Cancel**: C キーを押す → 演出が中断されること(Console に `Cancel() -> IsPlaying=False` が出る)。手順1の VFX は `StopOnCancel=false` にしているため、鳴らし切って自然に消えること(即座に消えないのは仕様どおり)
4. **P キーで再実行**: P キーを押すと最初から再生し直せること
5. **Package Manager 確認**: `Window > Package Manager` の「In Project」タブに `R3`(Unity NuGet 経由)と `R3 (Utility for Unity)` の 2 つが表示されていること。バージョンはどちらも 1.3.1
6. **isuzu MCP が引き続き動く**: R3 導入後も isuzu MCP(`compile_status`/`test_run`/`execute_code` 等)が問題なく動作すること(このチケット自体を isuzu MCP 経由で検証しているため、動いていなければ既に気付いているはずだが念のため)
7. Inspector からの手編集も試す: `Assets/GameData/Presentation/Demo/PRES_Demo_SkillSlash.asset` を選び、Tracks の要素を増減・Time を変更して保存 → Play し直した結果に反映されること(専用エディタ(5-4)が無いため、現状はこれが唯一の編集手段)

要判断:
- **R3 導入方法は「素の R3(org.nuget.r3) + R3.Unity(com.cysharp.r3)」の両方を入れたが、実際に使っているのは前者のみ**(`Observable<T>`/`Unit`/`Subject<T>`)。R3.Unity(Player Loop 連携・`ObservableTracker` 等)は 5-1 時点で未使用。将来 UI 側([15_ui_interaction.md] の「R3 未導入」コメント箇所)が R3 化される際に本格的に使われる想定。不要なら `com.cysharp.r3` を抜いて `org.nuget.r3` だけにする選択肢もある(DLL 増加を避けたい場合)
- **DLL 重複は発生しなかった**が、isuzu MCP のバージョンが上がった際に再度確認した方がよい(`org.nuget.system.runtime.compilerservices.unsafe` 等の transitive 依存が今後増える可能性がある)
- **TrackTargetMode.World と Anchor は同一実装**(いずれも `PlayContext` を参照せず `Anchor.LocalOffset` を絶対座標として使う)。意味的な区別が必要になったら実装を分ける
- **CameraShake / Haptic は 5-2/5-2b で実装済み、Timeline のみ警告 + no-op のまま**(6-10 で実装予定)。剣攻撃デモには 5-2/5-2b で CameraShake/Haptic トラックを追記した(下記「5-2 カメラシェイク」「5-2b コントローラー振動」の節を参照)
- **`VFX_Player_Slash`/`SE_Player_Slash`/`PRES_Demo_SkillSlash` の `Flags.Load` を `Preload` に変更した**(LazyLoad のままだと `PresentationManager` の同期解決で常に Placeholder になるため)。前者 2 つは他のデモ(AnchorGroup 等)でも使われている既存アセットのため、Preload 化の影響が無いか確認してほしい
- **Addressables グループ(`DDrive_GameData.asset`/`DDrive_Catalogs.asset`)はユーザーの未コミット変更と混ざっている**ため、5-1 のデモアセット登録に伴う変更はコミットしていない(ワーキングツリー上は両方の変更が混在した状態で残る)。次にこれらのファイルをコミットする人は、5-1 分(PresentationCatalog へのエントリ追加、VFX/SE の Preload 化)が含まれていることを把握しておくこと
- **PresentationEditor(5-4)は未実装**: `DataEditorRegistryTests` の Exempt に `PresentationData` を追加した。5-4 実装時に Exempt から外すこと

## 5-2 カメラシェイク（PR #22）

対象: `Runtime/Camera/{CameraShakeData,CameraFxManager,CameraFx,CameraShakeDataValidator}.cs`(新規)、`Runtime/Anim2D/Anim2DFacing.cs`(名前空間衝突の修正のみ)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(CameraFx 配線 + UnscaledCameraFxAdapter 追加)、`Runtime/Presentation/PresentationManager.cs`(CameraShake トラックの委譲先を実装)、`Runtime/Ui/OptionStore.cs`(ShakeScale の接続先)、`Editor/AssetBrowser/AssetCreationService.cs`(Shake を Preload 既定に追加)。設計は [16_camera_haptics.md](16_camera_haptics.md) Part A。確認用デモ資産 `Assets/GameData/Camera/Demo/SHAKE_Demo_DemoHitSmall.asset` を新規作成し、5-1 の剣攻撃デモ `PRES_Demo_SkillSlash.asset` の onHit トラックに接続した。

確認手順:

1. **剣攻撃デモで揺れを見る**: `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity` を開いて Play Mode に入る → Space キー(Signal("hit"))を押す → 画面停止(ヒットストップ)と同時に画面が一瞬揺れること
2. **連打しても破綻しない(AC)**: 手順1で Space キーを連打する → 揺れが異常に大きくなったり、カメラの位置がおかしくなったりしないこと(Console にエラーが出ないこと)
3. **オプション 0% で無揺れ(AC)**: `Runtime.Ui.Options.Set(OptionKey.ShakeScale, 0f)` を(確認用シーンに一時的なテストコード、または `execute_code`/デバッグ用ボタンで)呼んでから手順1を再実行する → 画面が一切揺れないこと。`Set(OptionKey.ShakeScale, 1f)` に戻すと揺れが復活すること
4. **カメラが後から現れても揺れる**: `Camera.main` がまだ無いシーンで `Presentation.Play` 等を呼んでシェイクを発火させる → Console に `Camera.main が見つからないため、シェイクは no-op です` の警告が(1 回だけ)出ること。その後カメラを配置する(タグ MainCamera)と、次のシェイクから正常に揺れること
5. **HitStop 中も揺れが止まらない**: 手順1のヒットストップ中(約 0.08 秒)にも画面の揺れが進行していること(スロー再生や連続スクリーンショットで確認するか、`CameraFxManagerTests` の自動テストで代替可)
6. **Inspector から調整**: `Assets/GameData/Camera/Demo/SHAKE_Demo_DemoHitSmall.asset` を選び、Pattern を Decay Sine や Impulse に変えて保存 → 再生し直した結果に反映されること(専用エディタ(5-2c)が無いため、現状はこれが唯一の編集手段)

要判断:
- **Space/Pattern の簡略化**: `World`/`FromSource` の位置変換、`CustomCurve`(現状 Impulse と同じ)、回転(Rot)は常に CameraLocal 相当で適用、など複数の簡略化を行った。詳細と理由は [16_camera_haptics.md] 実装メモを参照。5-2c(専用エディタ)で波形プレビューを作る際に、これらの挙動で十分か判断してほしい
- **`DDrive.Runtime.Camera` 名前空間が `UnityEngine.Camera` と衝突する**: `Runtime/Anim2D/Anim2DFacing.cs` の `Camera.main` 使用箇所を `UnityEngine.Camera.main` にフル修飾して解消した。今後 `DDrive.Runtime.*` 配下で `UnityEngine.Camera` を非修飾で使うコードを書くとコンパイルエラーになるので注意(詳細は [16] 実装メモ)
- **CameraShake アセットを Preload 既定に追加した**: `AssetCreationService.Create` で `AssetType.Shake` を Canvas/ControlSkin/Presentation と同じ Preload 既定グループに加えた(LazyLoad のままだと常に Placeholder になるため)。既存の Shake アセットが無い(このチケットで初めて作る種別の)ため影響範囲は無いはず
- **Addressables グループへの追加**: `CameraFxCatalog`(`DDrive_Catalogs.asset`)、`SHAKE_Demo_DemoHitSmall`(`DDrive_GameData.asset`)の 2 行が追加されたが、ユーザーの未コミット変更と同じファイルのためコミットしていない(ワーキングツリー上に残る)

## 5-2b コントローラー振動（PR #22）

対象: `Runtime/Haptics/{HapticsData,IHapticOutput,GamepadHapticOutput,HapticsManager,Haptics,HapticsDataValidator}.cs`(新規)、`Runtime/DDrive.Runtime.asmdef`(`Unity.InputSystem` 参照追加)、`Runtime/Loop/DDriveRuntimeBootstrap.cs`(Haptics 配線 + OnApplicationQuit/OnApplicationFocus での ResetOutput)、`Runtime/Presentation/PresentationManager.cs`(Haptic トラックの委譲先を実装)、`Runtime/Ui/OptionStore.cs`(HapticScale の接続先)、`Runtime/Ui/UiSlider.cs`(Notch/Limit Haptic の発火)、`Editor/AssetBrowser/AssetCreationService.cs`(Haptics を Preload 既定に追加)。設計は [16_camera_haptics.md](16_camera_haptics.md) Part B。確認用デモ資産 `Assets/GameData/Haptics/Demo/HAPTIC_Demo_DemoHitPunch.asset` を新規作成し、剣攻撃デモの onHit トラックに接続した。

事前準備: Xbox/PlayStation 系のゲームパッドを PC に USB または Bluetooth で接続する(Input System が対応する機種)。パッドが無い環境では手順1・2・4は「Console にエラーが出ず、`GamepadHapticOutput` が no-op で継続すること」だけ確認すればよい。

確認手順:

1. **パッドで振動再生(AC)**: パッドを接続した状態で `PresentationSkillSlashPreviewScene.unity` を Play Mode に入り、Space キー(Signal("hit"))を押す → パッドが一瞬振動すること
2. **同時再生で飽和しない(AC)**: Space キーを連打する(複数の Haptic インスタンスが重なる状態を作る)、または `HapticsManagerTests.PlayData_OverlappingInstances_ComposeWithMax_NotSum` を確認する → 振動が加算されて振り切れたような感覚にならないこと(自動テストで Low/High が Max 合成(加算しない)されていることを確認済み)
3. **パッド未接続時は no-op**: パッドを外した状態で手順1を実行する → 例外・エラーが出ないこと
4. **オプション 0% で振動オフ**: `Runtime.Ui.Options.Set(OptionKey.HapticScale, 0f)` を呼んでから手順1を再実行する → 振動しないこと。`1f` に戻すと振動が復活すること
5. **Pause でモーター停止**: Play Mode 中に振動をトリガーした直後に `Loop.PauseService.Push(PauseChannel.Gameplay)`(ポーズ機構があればポーズ操作)を呼ぶ → パッドの振動が即座に止まること。`Pop` で解除すると再生中の振動があれば復帰すること
6. **アプリ終了/フォーカス喪失でモーター停止**: Play Mode 中に振動をトリガーした直後に Unity エディタのフォーカスを外す(Alt+Tab)→ 振動が止まること(実機ビルドでは Alt+Tab の代わりにタスク切り替えで確認)
7. **スライダーのノッチ/端で振動**: `Assets/GameData/Ui/Skin/Skider` 等、Notches>0 のスライダーを持つ確認用 Canvas を Play Mode で操作する(Notch Haptic Id / Limit Haptic Id に `HAPTIC_Demo_DemoHitPunch` の Id を設定した Slider Skin を使う)→ 目盛りを跨いだとき・端に到達したときにパッドが振動すること
8. **Inspector から調整**: `Assets/GameData/Haptics/Demo/HAPTIC_Demo_DemoHitPunch.asset` を選び、Low Freq / High Freq のカーブを変えて保存 → 再生し直した結果に反映されること(専用エディタ(5-2c)が無いため、現状はこれが唯一の編集手段)

要判断:
- **Pause 中は per-instance の Flags.Pause を見ない**: 実機のモーターを鳴らし続ける事故を避けるため、Pause チャンネルが立ったら一律で出力 0 にする安全側の設計にした(Vfx/CameraFx とは異なる)。per-instance 制御が必要になったら見直すこと
- **`LocalPlayerOnly`/`Priority`/`Extensions` は現状ロジックに未使用**: NGO 統合前のため送信元判定ができず、`LocalPlayerOnly` は常にローカル再生扱い。`Priority` は Max 合成そのものが優先度を実現しているため未使用。`Extensions` は型だけ用意し、設定すると警告のみ
- **`NotchHapticId`/`LimitHapticId` は `ulong` のまま**: シリアライズ形式の変更(型の置換)は事前確認が必要なため、5-2b では既存の `ulong` 型のまま `Haptics.Play` への接続だけ行った。`HapticId`(`AssetId<HapticMarker>`)への置換は 5-2c で判断してほしい
- **HapticsManager は Scaled dt のまま(CameraFx とは異なる決定)**: HitStop 中に振動を止めるべきか止めないべきかが仕様書に明記されていなかったため、他の全 Manager と同じ既定(HitStop で一緒に止まる)にした。要望があれば CameraFx と同じ Unscaled 駆動に変更を検討してほしい
- **Addressables グループへの追加**: `HAPTIC_Demo_DemoHitPunch`(`DDrive_GameData.asset`)の 1 行が追加されたが、ユーザーの未コミット変更と同じファイルのためコミットしていない(ワーキングツリー上に残る)
- **実機での動作確認は未実施**: 本セッションはヘッドレスな isuzu MCP 経由の自動テストのみで検証しており、実際にゲームパッドを接続した目視確認は行っていない(「未検証」と明記)。上記確認手順1〜7は人が実機で確認すること

## 5-2c 揺れ・振動エディタ（PR #23）

対象: `Editor/Camera/{CameraFxEditorWindow,SceneCameraShakePreviewDriver,EditorHapticsPreviewDriver,CameraFxPresets,WaveformGraphGui}.cs`(新規、namespace `DDrive.Editor.CameraFx`)、`Editor/Preview/CameraShakePreviewSceneSetup.cs`(新規、確認用シーン)、`Editor/DDrive.Editor.asmdef`(`Unity.InputSystem` 参照追加、`GamepadHapticOutput` を Test on Pad が直接使うため)、`Tests/Editor/DataEditorRegistryTests.cs`(Exempt から `CameraShakeData`/`HapticsData` を除去 + `KnownPairs` に追記)。設計は [16_camera_haptics.md](16_camera_haptics.md) §C-2、実装メモは同ファイルの「実装メモ（2026-09-14、5-2c）」を参照。合わせて 5-2 で発生した `DDrive.Runtime.Camera` ⇄ `UnityEngine.Camera` の名前空間衝突を `DDrive.Runtime.CameraShake` への改名で整理した(別コミット。docs/16 の「実装メモ（2026-09-14、5-2 整理）」参照)。

事前準備: ゲームパッド(Xbox/PlayStation 系)を PC に接続しておくと Test on Pad が確認できる。無い環境では「波形のみ確認できます」の案内が出ることを確認すればよい。

確認手順:

1. **専用エディタを開く**: `Tools > D-Drive > Editors > Shake / Haptics` を開く → `Assets/GameData/Camera/Demo/SHAKE_Demo_DemoHitSmall.asset` を対象アセット欄にドラッグ(または Asset Browser の「Shake Editor で開く」)→ Shake 用の項目(Pattern/PosAmplitude/…)と波形プレビューが表示されること
2. **確認用シーンで実際に揺らす(AC)**: ツールバー「確認用シーンを開く」→ `CameraShakePreviewScene` が開く(無ければ生成される)→ ウィンドウの「▶ Shake 再生(連打可)」を押す → **SceneView / Game ビューでカメラが実際に揺れる**こと(ウィンドウ内には何も描かれないこと)
3. **連打テスト(AC)**: 手順2のボタンを素早く連打する → 例外が出ず、揺れが異常に大きくなったりカクついたりしないこと(Trauma 合成の効果)。「合成中の Instance」の数が連打に応じて増減すること
4. **閉じるとカメラが元の位置に戻る(AC)**: 手順2で揺れているままウィンドウを閉じる → Hierarchy 上でカメラの親が元に戻り(`DDriveCameraShakeNode` が残らない)、カメラの位置・回転が揺らす前と同じであること
5. **プリセット 10 種(AC)**: 「プリセット(10種)」を開き、Pulse/Rumble/Heartbeat/Explosion/Hit_Small/Hit_Large/Landing/Earthquake/Alarm/Engine を順に押す → そのたびに波形プレビューとパラメータ欄の数値が変わり、Ctrl+Z で直前の値に戻ること
6. **Haptics Editor に切り替える**: 対象アセット欄に `Assets/GameData/Haptics/Demo/HAPTIC_Demo_DemoHitPunch.asset` を入れる → Low/High の波形プレビューと「Test on Pad」ボタンが出ること
7. **Test on Pad で実際に振動させる(AC)**: パッドを接続した状態で「▶ Test on Pad」を押す → パッドが振動すること。「■ 停止」を押すと即座に止まること
8. **止め忘れ防止(AC)**: 手順7の直後にウィンドウを閉じる/エディタからフォーカスを外す(Alt+Tab)/Play ボタンを押す、のいずれかを行う → いずれの場合もパッドの振動が確実に止まること
9. **パッド未接続時の案内**: パッドを外した状態でウィンドウを開く → 「パッド未接続です」の案内が出て、波形の確認だけができること(エラーは出ない)
10. **Haptics のプリセット 10 種**: Shake と同様にプリセットボタンを押して数値・波形が変わり、Undo で戻ることを確認する
11. **自動テストの確認(代替可)**: 目視確認が難しい場合、`SceneCameraShakePreviewDriverTests`(カメラの姿勢復元・DontSave 付与・連打)/ `EditorHapticsPreviewDriverTests`(Fake 出力での 0 復帰)/ `CameraFxPresetsTests`(10 種 × Undo)で代替できる

要判断:
- **プリセットの具体的な数値は暫定値**: `CameraFxPresets` の 10 種(Shake/Haptics 各)は「こういう性格の揺れ・振動」という設計意図を反映したたたき台で、デザイナーによる実プレイでの調整前提。特に Earthquake/Alarm/Engine は Loop(無限)にしているため、実際に使う際は `CameraFx.Shake`/`Haptics.Play` の呼び出し側で明示的に `Stop` する運用になる点に注意
- **波形プレビューは近似表示**: `WaveformGraphGui` の pos/rot 波形は `CameraFxManager.SampleWave` の乱数位相(`Random.value` によるインスタンスごとのシード)を再現しておらず、Pattern ごとの疑似オシレーションで「だいたいこんな感じ」を示す参考表示にとどめた(§C-2 に明記の「編集 UI は ValueDefDrawer を使い、専用のカーブエディタは作らない」という方針を踏まえ、波形表示に工数をかけすぎない判断をした)。より正確な波形が必要になったら `CameraFxManager` 側の乱数シードを外部から注入できるようにする等の見直しが要る
- **PresentationEditor(5-4)内の同時プレビューは対象外**: チケット文面どおり、Shake + Haptic + SE + VFX の統合プレビューは 5-4 で実装する。5-2c で用意した `SceneCameraShakePreviewDriver`/`EditorHapticsPreviewDriver` はそのまま 5-4 から呼べる設計にしてある(コンストラクタで `AssetRegistry` を外部から差し替え可能)
- **`CameraFxEditorWindow` 自体の UI テストは書いていない**: 既存の VFX/Anim/Model 系エディタと同じく、このプロジェクトには EditorWindow の `CreateGUI` を直接テストする前例が無いため、実体である Driver / Presets 側のテストで代替した(詳細は docs/16 実装メモ)
- **Test on Pad のフォーカス喪失判定はエディタアプリ全体が対象**: `EditorApplication.focusChanged` を使っているため、Unity エディタの別ウィンドウ(Scene/Game/Inspector 等)に切り替えるだけでは止まらず、**Unity エディタ自体から他のアプリへ切り替えたとき**に止まる。ウィンドウ単位のフォーカス喪失(`EditorWindow.OnLostFocus`)で止めるべきという意見があれば見直すこと
