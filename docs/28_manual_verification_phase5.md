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

