# 39. 使い勝手の修正リスト（2026-09-17）

関連: [11_tasks.md](11_tasks.md) / [09_editor_tools.md](09_editor_tools.md) / [36_manual_screenshot_list.md](36_manual_screenshot_list.md)（§5 の未撮影の原因がここに入っている） / [12_review.md](12_review.md)

Phase 6 まで実装した後、**デザイナーマニュアル用のスクリーンショット撮影と実機での通し確認**で見つかった不具合・使い勝手の要望をまとめたもの。番号は `U-1`〜`U-27`。Phase 7（[11_tasks.md](11_tasks.md) の推奨拡張 A 群）とは別枠で、**Phase 7 より先に片付ける**。

出どころは 2 つ:

- ユーザー（デザイナー兼開発者）が実際に触って挙げた要望・不具合
- スクリーンショットが撮れなかった理由（[36 §5.4](36_manual_screenshot_list.md)）。直ったら該当のスクリーンショットを撮る

## 0. 一覧

| # | 区分 | 内容 | 出どころ | 状態 |
|---|---|---|---|---|
| U-1 | 不具合 | 3D オブジェクトのプレビューが全て透明（Model Editor、複数モデル並列表示、Presentation の剣攻撃デモなど） | 要望 / [36](36_manual_screenshot_list.md) #27・#54 | 実装済み |
| U-2 | 不具合 | FBX ロード時に MaterialData は作られるが、ModelData の Material スロットに反映されない（全て None） | 要望 | 実装済み |
| U-3 | 追加 | FBX などソースアセットの再読み込み（マテリアル等の再構築） | 要望 | 実装済み |
| U-4 | 不具合 | Prefab Editor の「確認用シーンに配置」が、確認用シーンではなく現在開いているシーンにしか配置しない | 要望 | 着手 |
| U-5 | 追加 | 「確認用シーンに配置」系ボタンの共通強化: 右クリックで「このシーンに配置」「このシーンに本配置（シーンを移動しても消えない）」/ 配置後に SceneView をフォーカス | 要望 | 着手 |
| U-6 | 不具合 | Presentation Editor が確認用シーンで開かない | 要望 | 着手 |
| U-7 | 改善 | Presentation Editor のシークバーを Anim Editor のシークバーと同じ形にする | 要望 | 実装済み |
| U-8 | 改善 | Anim2D Editor の「作成」はタブに分ける必要がない。新規作成から作成画面をポップアップで出す | 要望 | 実装済み |
| U-9 | 追加 | UI Tween Editor に Preset Gallery を開くボタンを追加 | 要望 | 実装済み |
| U-10 | 不具合 | Button Skin Editor の「SE も鳴らす」が見切れている（→ 全体の点検は U-27） | 要望 | 実装済み |
| U-11 | 改善 | 自作エディタに「Inspector（全フィールド）」があるものと無いものがある。ID など編集させたくない項目もあるため、**編集させるもの / させないものを選び分けて分離**する（無い側は「Inspector に移動」程度で十分） | 要望 | 実装済み |
| U-12 | 不具合 | Asset Browser 下部のサウンドのプレビューバーの UI が崩れている | [36](36_manual_screenshot_list.md) #14 | 実装済み |
| U-13 | 不具合 | 個別検証があるものと無いものがある。VFX Editor では「検証」を展開しても何も表示されない | [36](36_manual_screenshot_list.md) #15 | 実装済み |
| U-14 | 不具合 | BgmData の Fade 欄が `No GUI Implementation` と表示される | [36](36_manual_screenshot_list.md) #17 | 実装済み |
| U-15 | 不具合 | 仕様書 URL を設定したのに「仕様書 URL が未設定」と表示される | [36](36_manual_screenshot_list.md) #6 | 実装済み |
| U-16 | 改善 | Asset Browser から新規作成した後、そのアセットを専用エディタで開く | 要望 | 実装済み |
| U-17 | 追加 | Project ウィンドウの右クリックメニューを拡充（音源を右クリック → SeData を作成 など、**できるものはすべて**） | 要望 | 実装済み |
| U-18 | 追加 | Hierarchy の右クリックメニューから AnchorRig・SeEmitter など基本オブジェクトを配置できるように | 要望 | 実装済み |
| U-19 | 改善 | CanvasData の作成が毎回「Canvas → Panel」の 2 手順になっている。Hierarchy と `Tools > D-Drive` からの一発生成を追加 | 要望 | 実装済み |
| U-20 | 不具合 | Animation2D の Anim Editor から SE・VFX を**設定はできるが再生されない** | 要望 | 実装済み |
| U-21 | 追加 | Canvas Editor で要素の移動などができるように | 要望 | 実装済み |
| U-22 | 追加 | Anchor Group で 3×3 などを自動配置した後、**手置きの点に変換する**ボタン | 要望 | 実装済み（[22 §3.7](22_anchor_group.md)） |
| U-23 | 不具合 | ElementFx で SlideIn などを設定して再生ボタンを連打すると位置ずれが起きる（開き直すと戻る） | 要望 | 実装済み |
| U-24 | 追加 | Anchor の SceneView 表示で基準が分からない。LocalOffset だけでなく**基準（原点）の座標も SceneView に描画**する | [36](36_manual_screenshot_list.md) #19・#23 | 実装済み（[21 §3.10](21_anchor_spec.md) / [09 §2.2](09_editor_tools.md)）。#19 / #20 / #23 は撮影待ち |
| U-25 | 改善 | Presentation の Signal を手動で送る操作のやり方が分からない。導線・説明を分かりやすくする | [36](36_manual_screenshot_list.md) #53 | 実装済み |
| U-27 | 改善 | **全エディタ共通の条件**: Windows の拡大/縮小 100%・ウィンドウ横幅 500px で要素が見切れないこと（[09 §7.1](09_editor_tools.md)）。U-10 は個別の 1 件で、これはその全体点検 | 要望 | 実装済み |
| U-26 | 確認 | ~~実機テスト（PC 2 台での通し確認）~~ **不要**（2026-09-17 にユーザー判断。[29](29_network_device_test.md) で PC-A Host + PC-B Client の確認は済んでいるため） | 要望 | 対応不要 |

## 1. 補足

### U-1 / U-2（プレビューが透明）
U-1 は U-2 が原因である可能性が高いが、**確定させてから直す**こと。Renderer の sharedMaterial が null なのか、Placeholder マテリアルが透明なのか、URP への変換漏れなのかで直す場所が変わる。Model Editor の「複数モデル並列表示」、Presentation の剣攻撃デモ（`PresentationSkillSlashPreviewScene`）でも同じ症状が出ている。

> **結果（2026-09-17）: U-1 の真因は U-2 ではなかった。** `ModelsManager.SpawnData` が `renderer.renderingLayerMask = data.LightLayerMask` を**無条件に**全 Renderer へ書いており、`ModelData.LightLayerMask` の既定値 0 がそのまま入っていた。URP（SRP）は `FilteringSettings.renderingLayerMask`（既定は全ビット）とのビット積で描画対象を選ぶため、Renderer 側が 0 だと**どのパスにも一致せずモデルが描かれない**（ライトレイヤーにも一致しない）。`Models.Spawn` を通る経路すべて（Model Editor の配置・並列表示・Presentation の剣攻撃デモ）で同時に出るのはこのため。**VFX では 2026-09-08 に同じ不具合を直していた**（[19](19_vfx_usability_review.md) B-1）が Model 側に残っていた。修正は VFX と同じ規約に揃えるだけ（`ModelData.LightLayerKeepPrefab = 0` = Prefab の設定を上書きしない、フィールド既定値は 1）。既存アセットは 0 が保存されているので自動的に安全側へ倒れる。
>
> U-2（スロットが全て None）は独立した不具合で、こちらも直した（`Editor/Model/ModelSlotBinder.cs`。詳細は [05 A-4](05_model_animation.md) の 2026-09-17 実装メモ）。Slots が None のままでも Prefab 側の Material がそのまま使われるだけなので、透明にはならない。

### U-5（配置ボタンの共通強化）
各エディタに同じコードをコピーせず、共通ヘルパー／共通 UI 部品にまとめること。「一時配置」と「本配置」の区別（HideFlags / DontSave / クリーンアップ処理）は既存実装を確認してから設計する。

### U-7（シークバー）

**2026-09-17 実装済み。** Presentation Editor の統合プレビューにあった「シーク」(UI Toolkit の丸ノブ `Slider`)を、Anim Editor(`AnimEditorWindow.DrawTimeline`)と同じ「暗い背景 + 目盛り付きバー + 白い再生ヘッド + クリックでシーク」の形に変更した。共通部品 `Editor/Common/SeekBarGui.cs` に切り出し、Anim 側もこれを使うようリファクタリングした(見た目は既存のピクセル位置を既定値にしたため不変)。詳細は [08_presentation.md](08_presentation.md) の「追補（2026-09-17、U-7）」。Presentation のトラック編集用タイムライン(`PresentationEditorWindow.Tracks.cs`、ズーム/パン/複数レーン)は対象外(既存のまま)。

### U-8（Anim2D Editor の「作成」をポップアップに）

**2026-09-17 実装済み。** `Anim2DEditorWindow` は以前「作成」/「編集」を `ToolbarToggle` で切り替える単一ウィンドウだった（`Anim2DEditorWindow.Create.cs` が作成タブの中身）。作成はタブに分ける必要が無いため、作成用の入力・生成ロジックをすべて新設の `Editor/Anim2D/Anim2DCreateWindow.cs` に切り出し、独立したユーティリティウィンドウ（`GetWindow<T>(utility: true, title: "...")`）にした。既存の「新規アセット作成」ダイアログ（`Editor/AssetBrowser/NewAssetDialog.cs`）が同じ作法で開いているため、新しい仕組みを作らずそれを踏襲している。`Anim2DEditorWindow` 本体は常に編集(Edit)画面だけを表示し、ツールバーの「スプライトから新規作成…」から `Anim2DCreateWindow.Open()` を呼ぶ。生成に成功すると `Anim2DCreateWindow` は `Anim2DEditorWindow.Open(created)` で編集用ウィンドウをその対象で開いてから自身を閉じる（従来の「生成後に編集モードの対象欄へ自動で入る」という体験は維持したまま、ウィンドウが分かれるだけ）。生成ロジック自体（スライス・命名・AnimationClip 生成・BlendTree 登録・Anim2DData 作成）は変更していない。デザイナー向け操作は [DesignerManual/anim2d-editor.html](DesignerManual/anim2d-editor.html) を同時に更新した（旧スクリーンショット 2 枚は UI 変更のため要再撮影、[36 §5.4](36_manual_screenshot_list.md) 相当）。

### U-20（Anim2D の SE・VFX が再生されない）

**2026-09-17 実装済み（不具合）。** 症状は「Anim Editor（`AnimEditorWindow`）で Anim2DData に Frame/Time＋`Action=PlayAsset` のイベント（SE/VFX）を設定できるが、実行時に再生されない」。**対症療法（try/catch で握りつぶす等）ではなく真因を特定して直した。**

- **切り分け**: Anim Editor 自身のプレビュー（`SceneAnimPreviewDriver`、Data オブジェクト参照を直接 `PlayData` に渡す経路）は元々問題なかった。壊れていたのは**実行時（Play Mode）に ID から `Anim2D.Play`/`Anim.Play` を呼ぶ経路**。U-8（作成タブのポップアップ分離）の影響ではない（U-8 は生成ロジックに触れておらず、症状はそれ以前から存在する別要因だった）。
- **真因**: `Anim2D.Play`/`Anim.Play`（ID 版）はどちらも共有の `AnimManager.Play` → `AssetRegistry.ResolveOrPlaceholder<AnimData>` という**完全同期**の解決経路しか持たない。`ResolveOrPlaceholder` は「既に `_loaded` キャッシュにあるものしか返さない」同期専用の解決で、自らロードを開始しない。`_loaded` に乗るのは `Flags.Load=Preload`(カタログ登録時に自動ロード)のものだけで、既定値の `LazyLoad`(「初回参照時にロード」)は非同期経路(`ResolveAsync`)専用。`ModelData.DefaultAnimation` 等「他の Data からの依存解決」で先にロードされていない Anim(2D)Id を直接 Play すると、実データではなく `Events` が空の Placeholder(`AnimManager.CreatePlaceholder`)が再生され、Frame/Time で設定した SE/VFX が一切鳴らない/出ない(見た目のアニメーションは Animator 自身の状態遷移で動き続けるため気づきにくい)。これは Canvas/ControlSkin(2026-09-12)・Presentation/Shake/Haptics(2026-09-14)で見つかったのと**全く同じ罠**([07_canvas_prefab.md](07_canvas_prefab.md)「バグ修正（2026-09-12）」参照)で、`AssetCreationService.cs` の新規作成デフォルトに `AssetType.Anim`/`AssetType.Anim2D` が含まれていなかった見落としが原因。2D キャラクターは 3D 専用の `ModelData` を経由しないため「他経路での先行ロード」が起きにくく、3D 側(`Anim`)より先に顕在化した(3D 側にも同じ穴があったため合わせて直した)。
- **対応**: `AssetCreationService.Create()`(`Assets/DDrive/Editor/AssetBrowser/AssetCreationService.cs`)の新規作成デフォルトに `AssetType.Anim`/`AssetType.Anim2D` を追加(`Flags.Load=Preload` になる)。既存アセット向けに `AddressablesRegistrationValidator`(`Assets/DDrive/Editor/Validation/AddressablesRegistrationValidator.cs`)の `NeedsPreload` にも同じ 2 種別を追加し、`Validation > Run All` で `Flags.Load != Preload` を Error + FixAction として検出・修正できるようにした(既存の Canvas/ControlSkin 向け仕組みをそのまま再利用)。既存の `ANIM_Player_Jump.asset`(実際に PlayAsset イベント付き)と `Assets/GameData/Anim2D/**/ANIM2D_*.asset` は本対応の中で `Flags.Load=Preload` に修正済み。詳細・実装メモは [05_model_animation.md](05_model_animation.md) C-4「バグ修正（2026-09-17、U-20）」を参照。
- **テスト**: `Anim2DEventDispatchTests`(PlayMode、新規)で真因を再現するテスト(`Anim2D_IdPlay_WithoutPriorPreload_ResolvesPlaceholder_AndEventsDoNotFire`)を先に書いて赤にしてから直し、既存経路が壊れていないことを確認するテスト(`Anim2D_FrameEvent_PlaysSeAndSpawnsVfx_ThroughFacade`)も追加。`AddressablesRegistrationValidatorTests`(EditMode、新規)で Validator の検出・修正も固定した。

### U-21（Canvas Editor で要素の移動）

**2026-09-17 実装済み。** `CanvasEditorWindow` には要素(RectTransform)を動かす手段が無かった。「確認用シーンを開く」で置くプレビュー実体は `UiManager.OpenData` が生成する Prefab リンク無しの実体(ADR-4 の確認用プレビュー)なので、そこを直接動かしても `CanvasData.Prefab` には反映されない。ウィンドウ内に実 UI を描画・編集する専用機構を新設するのは 2026-09-10 の owner instruction(ウィンドウ内描画を避ける)に反するため、**既存の `ModelEditorWindow.OpenPrefab`/`VfxEditorWindow.OpenPrefab` と同じ導線**(`AssetDatabase.OpenAsset(prefab)` でプレハブモードを開く)を Canvas Editor にも追加した。

- ツールバーに「Prefab を開く(要素の移動)」ボタンを追加(`OpenPrefab()`)。押すとプレビュー実体を片付けてから `CanvasData.Prefab` をプレハブモードで開く
- ElementFx の各要素の Foldout 先頭に「選択して移動(Prefab を開く)」ボタンを追加(`SelectElementForMove(elementPath)`)。対象 Prefab がプレハブモードで開いていなければ自動で開き、`PrefabStage.prefabContentsRoot.transform.Find(elementPath)` でその要素の GameObject を選択・SceneView へフォーカスする(`PreviewPlacement.Focus` を再利用)
- 実際の移動・回転・リサイズは Unity 標準の Move/Rotate/Rect ツールで行う。プレハブモードは通常のシーン編集と同じ Undo 機構に乗るため、Ctrl+Z でそのまま戻せる(独自の Undo コードは不要。CLAUDE.md §0-5 は「エディタがコードでデータを書き換えるとき」の規約で、ここはユーザー自身が Unity 標準ツールで動かす経路のため該当しない)
- 実装: `Assets/DDrive/Editor/Canvas/CanvasEditorWindow.cs`(`OpenPrefab`/`SelectElementForMove`)。既存の `RemovePreview`/`PreviewPlacement.Focus` をそのまま再利用しており、新しい共通部品は増やしていない
- 未確認(Unity MCP 未接続時): 実際にプレハブモードが開くこと、要素選択後に Move ツールで動かして Ctrl+Z で戻ること、ツールバーボタンの表示崩れが無いこと(U-27 の 500px 基準)は Unity 上で確認すること

### U-11（Inspector 全フィールド）
「全部出す / 全部隠す」の二択ではなく、**種別ごとに編集させる項目と読み取り専用にする項目を決める**のが本題。ID のように編集されると壊れるものは読み取り専用にし、どうしても触る必要があるときは Inspector 側で行う。どの項目をどちら側にするかを決めたら [09_editor_tools.md](09_editor_tools.md) に表として残すこと。

**2026-09-17 実装済み。** 詳細・割り当て表は [09_editor_tools.md §8.6](09_editor_tools.md#86-inspector-の編集可否を分離2026-09-17-39-u-11) を参照。
新設の `[InspectorReadOnly]`（`Foundation/Data/InspectorReadOnlyAttribute.cs`）をフィールドに付けると、`AssetDataInspector`
（§8 の全 Data 共通 Inspector）が UI Toolkit / IMGUI どちらの経路でも対応する `PropertyField`/プロパティを disabled（グレーアウト、
値は見えるが編集不可）にする。今回は `AssetDataBase` 共通フィールドの `Id`/`Version`/`Author`/`UpdatedAt` に付けた
（保存フックが自動更新する値・安定 ID で、いずれも既存のコメントで「手編集しないこと」と書かれていたのに実際には
編集可能なテキストフィールドのままだったもの）。`UiTweenEditorWindow`/`CanvasEditorWindow`/`MaterialEditorWindow`/
`PrefabEditorWindow` が埋め込む「Inspector(全フィールド)」セクションも同じ `AssetDataInspector` を内部で経由しているため、
**個別に直さず自動的に反映される**。種別固有フィールド（`MaterialData.SourceMaterial` 等）は今回対象にしていない
（**2026-09-17 決定**: 直接編集させたくないのは `AssetDataBase` 共通の 4 フィールドだけでよい、とユーザーが判断した。
種別固有フィールドは編集可能のまま据え置く。将来変える場合は同じ属性を付けて §8.6 の表に追記する運用)。

### U-27（横幅 500px で見切れない）
2026-09-17 にユーザーが出した**全エディタ共通の条件**。基準環境は Windows の拡大/縮小 **100%**、ウィンドウ横幅の下限は **500px**。規約本体は [09_editor_tools.md §7.1](09_editor_tools.md) に書いた（縦方向の §7「ScrollView ルート必須」と対になる）。

U-10（Button Skin Editor の「SE も鳴らす」が見切れる）はこの条件に引っかかった 1 件目。U-27 は**全エディタを 500px 幅で上から下まで見て、切れている箇所を洗い出して直す**チケット。新規エディタ・レイアウト変更のときは以後この幅で確認する。

**2026-09-17 実装済み。** Unity MCP（`execute_code`）で全 28 EditorWindow を幅 500px のフローティングで開き、
`resolvedStyle`/`worldBound` を数値比較する機械的な方法で点検した（目視ではない）。点検結果の一覧・検出方法・
誤検出として除外したもの（GraphView のパン領域、TextField 内部のネイティブスクロール）は
[09_editor_tools.md §7.1.1](09_editor_tools.md#711-全エディタ横断点検2026-09-17u-273-巡目) の表を参照。

- **実際に破綻していた 10 件**（5 件は横並び行の `flexWrap` 不足、5 件は `minSize` の横幅が 500px を超えていて
  そもそも 500px まで縮められなかったもの）をすべて直した。詳細・実測値は §7.1.1 の表
- **優先度最高だったもの**: `AnimEditorWindow.Source.cs`(`BuildEventRow`、Anim2D の Events 編集とも共用)で、
  Events 行の削除(✕)・アセットを開く(↗)ボタンが最大 92px 画面外に出て**実際に押せなくなっていた**（操作不能 >
  ラベルが読めない > 見栄えの優先順位どおり最優先で対応）
- **`minSize` の横幅超過**が実質的に一番多いパターンだった: `AudioEditorWindow`/`CameraFxEditorWindow`/`VfxEditorWindow`
  (520px)・`AnimEditorWindow`(560px)・`PresentationEditorWindow`(**620px**、最大)の 5 ウィンドウは、`Open()` が
  設定する `minSize.x` がそもそも 500px を超えていたため、ユーザーが実際にウィンドウを 500px まで縮めることが
  物理的にできなかった（＝レイアウト自体は壊れていなくても規約違反）。全て 500 に下げた。`ModelEditorWindow` は
  2026-09-17 の別対応で既に 500 になっていたのが唯一の先例
- **Toolbar の残課題（「Unity 側が折り返さないため残課題」）は検証の結果、部分的に誤りと判明した。**
  `flexWrap=Wrap` だけでは `Toolbar` 自身の固定高さのせいで折り返した行が後続要素と重なるが、
  **`style.height = StyleKeyword.Auto` を併用すれば正しく複数行に伸びる**ことを合成テストで確認した。
  ただし現状 28 ウィンドウの `Toolbar` はどれも 500px で破綻していなかった（壊れていないものは直さない方針のため
  未適用）。今後の指針として [09 §7.1.2](09_editor_tools.md#712-toolbar--toolbarbutton-の折り返し残課題の検証2026-09-17) に手順を残した
- 触っていない（破綻していなかったため予防的に変更しなかった）: 上記以外の 18 ウィンドウ。一覧は §7.1.1 参照
- コンパイル・EditMode/PlayMode テストは本チケットの完了条件節を参照

### U-16 / U-17 / U-18 / U-19（アセット作成の導線）— 実装済み（2026-09-17）

4 件はまとめて「作る・置く導線」として実装した。詳細は [09_editor_tools.md](09_editor_tools.md) §1（表の「新規作成した直後に専用エディタで開く」）/ §1.2 / §6.2 / §6.3、[10_workflow.md](10_workflow.md) §6.1、[07_canvas_prefab.md](07_canvas_prefab.md) A-4 実装メモ。

- **U-16**: `Editor/Inspector/CreatedAssetOpener.cs` を新設し、`NewAssetDialog` の作成完了時に呼ぶ。開き方は既存の `[DataEditor]` → `DataEditorRegistry.OpenDefault`（§8）で、新しい経路は増やしていない。専用エディタが無い種別は Ping + Selection のみ（無害なフォールバック）。専用エディタの「＋ 新規作成」（§8.3）から開いた場合は従来どおりそのエディタへ切り替わる
- **U-17**: 「対応できる種別」は**既存の `IImportRuleHandler`（ImportRule、§1.1）を読んで洗い出している**。あの 9 ハンドラが拡張子・Data 型・割り当て先フィールドを既に宣言しているため、対応表を二重に持たずに済む（ハンドラが増えれば右クリックメニューも自動で増える）。ImportRule に無い分（`.mat` → MaterialData、画像 → ButtonSkinData / SliderSkinData）だけを `SourceDataCreation.ExtraOptions` に足した。**`.mat` は既存の `UnityMaterialMigrator.Migrate` を呼ぶ**ので、シェーダー変換とテクスチャの TextureData 化もそのまま効く
- **U-18**: `GameObject/` のメニュー項目は Unity 仕様で選択中の GameObject の数だけ呼ばれるため、`ShouldRun` で 1 クリック 1 個に制限している。UiSlider の組み立ては `PreviewSliderFactory` に `dontSave` 引数を足して共有した（配置用の組み立てコードを別に作らない）
- **U-19**: 入口は 2 つでも実体は `CanvasSetupService.CreateCanvasWithPanel` 1 つ。Data の作成は `NewAssetDialog`（CanvasData 固定）→ `AssetCreationService.Create` をそのまま通す

未確認（Unity MCP 未接続のため）: メニュー項目の実際の表示位置・validate の灰色表示・Undo の挙動は、Unity 上で 1 度通しで確認すること。

### U-13（個別検証）
種別ごとに検証セクションの有無がばらついているのを揃えるのが本題。VFX Editor で展開しても何も出ないのは別の不具合として直す。

**2026-09-17 実装済み。** 詳細は [09 §11](09_editor_tools.md)。
- 共通部品 `DataValidationSection` / `DataValidationRunner`（`Editor/Validation/DataValidationSection.cs`）に一本化し、
  無かった Audio / Shake · Haptics / Material / Model / Prefab / Button Skin / Slider Skin に追加、
  独自実装だった Vfx / Anchor / Anchor Group を置き換えた
- **まだ独自実装のまま**なのは Anim / Anim2D / Canvas / Presentation。いずれも種別 Validator に加えてエディタ固有の追加検査
  （Anim の StateName / BlendShape 照合、Anim2D の 3 Validator 合成、Canvas の個別 Fix ボタン）を出しており、
  そのまま置き換えると情報が減る。Presentation は同時に U-6 で改修中だったため見送り。**移行は後続チケット**
- VFX Editor で何も出なかった件は検証セクションではなく `VfxEditorWindow.RefreshAnchorUi()` の
  `ArgumentNullException`（破棄済み `SerializedObject` に対する `FindProperty` が null）が原因で、
  その手前で処理が止まって `RefreshValidation()` に到達していなかった（Editor.log に記録あり）

### U-23（ElementFx の連打で位置ずれ）

**2026-09-17 実装済み（不具合）。** 症状は「ElementFx(Canvas Editor)の SlideIn などを再生ボタンで連打すると要素の最終着地位置が本来の位置からずれていく。ウィンドウを開き直すと戻る」。**対症療法（try/catch や位置の補正処理を足す等）ではなく真因を特定して直した。**

- **切り分け**: ユーザーの推測（「Tween の開始位置を『現在位置』から取っているため」）は方向性としては近いが、実際には `TweenFromMode.Current` の経路が壊れているわけではなかった。SlideIn 系プリセット自体は `UiPresetFactory.Build` が `Absolute` From/To を組み立てる設計で、そこは正しく動いていた
- **真因**: Canvas Editor の ElementFx「▶ 再生」(`CanvasEditorWindow.PlayPhasePreview`)は連打時、`UiTweenManager.StopAll(elementTarget)` で前の Tween を止めてから `UiPresetFactory.Build` で同じプリセットを取り直す、という手順を踏む。`UiPresetFactory.Build` の SlideIn 系は呼び出し時点の `target.anchoredPosition`(= "現在位置")を新しい Tween の**静止位置(To)**としてそのまま採用する設計だが、修正前の `UiTweenManager.StopAll(RectTransform target)` には `Stop(handle, complete)` にある「最終値へ進めてから終わる」ための `complete` 引数が無く、中断された Tween の Instance を**完了させずに**取り除くだけだった。そのため、連打で割り込まれた瞬間の(オフスクリーンと本来の静止位置の中間の)位置がそのまま次の Tween の "静止位置" として採用されてしまい、連打するたびに本来の位置からずれていった。ウィンドウを開き直すと戻るのは、確認用シーンを作り直すことで Prefab に保存された正しい位置から Instance が再生成されるため
- **対応**: `UiTweenManager.StopAll(RectTransform target, bool complete = false)` に `Stop(handle, complete)` と同じ規約の `complete` 引数を追加し(内部実装も `Stop` を呼ぶよう統一)、`UiFx.StopAll(RectTransform, bool)` にも同じ引数を追加した。`CanvasEditorWindow.PlayPhasePreview` の呼び出しを `StopAll(elementTarget, complete: true)` に変更し、連打で中断された Tween を必ず最終値へスナップしてから次の "静止位置" を読み直すようにした。既定値 `false` は既存の呼び出し元(他に無いが将来のゲームコード利用も想定)との互換性のため据え置いた
- **テスト**: `Assets/DDrive/Tests/Runtime/UiTweenTests.cs` に `RapidReplay_SlideInPreset_WithStopAllComplete_SettlesAtRestPosition` を追加。**修正前は当時の唯一のシグネチャ `StopAll(rt)` で連打すると、本来の静止位置 `restX=0` に対して実測 `-328.05` に着地しており、赤であることを確認済み**(このテスト自体は修正後の `complete:true` 呼び出しに書き換えてある)。あわせて `StopAll_WithoutComplete_LeavesTargetAtInterruptedPosition`(complete=false の既定動作を固定)も追加した
- 詳細・実装ファイルは [07_canvas_prefab.md](07_canvas_prefab.md) の「バグ修正（2026-09-17、U-23）」と [15_ui_interaction.md](15_ui_interaction.md) の「バグ修正（2026-09-17、U-23）」を参照
- 未確認(Unity MCP 接続状況次第。本セッションでは接続できたため EditMode/PlayMode 双方 green を確認済み): 実際に Canvas Editor で ElementFx を連打して位置がずれないことの目視確認

### U-25（Signal を手動で送る導線）

**2026-09-17 実装済み（改善）。** 「Presentation の Signal を手動で送る操作のやり方が分からない」という報告について、機能自体（統合プレビュー内の「Signal レーン(手動発火)」に Signal Key ごとのボタンが並ぶ仕組み）は既に実装済みだったため、**分かりにくさの原因を特定してから直した**。

- **原因**: (1) 再生していない間に Signal ボタンを押しても `Manager.Signal(Current, key)` が無効な Handle への no-op になるだけで見た目に変化が無く、ボタンは常に押せる状態のままだったため「押しても反応がない=使い方を間違えている」と誤解しやすかった。(2) マニュアル(`docs/DesignerManual/presentation.html`)の説明が1文で簡潔すぎ、「まず再生してから」という前提条件が明示されていなかった
- **対応**: `PresentationEditorWindow.Preview.cs` の「Signal レーン(手動発火)」フォールドアウトに手順(①「▶ 再生」→②再生中に Signal ボタン)を明文化したラベルを追加し、各 Signal ボタンに「再生中のみ有効です」というツールチップを付けた上で、**再生中でなければボタンをグレーアウト**するようにした(`PresentationEditorWindow.OnEditorUpdate` の既存の毎フレーム更新ループから `UpdateSignalButtonsEnabledState` を呼び、再生状態の変化に追従させる)。OnSignal トラックが無いときの案内文も具体的にした
- `docs/DesignerManual/presentation.html` の該当段落を 2 段階の手順として書き直し、スクリーンショット #53 のプレースホルダを撮影可能な `<figure>` に差し替えた
- ランタイム API(`Presentation.Signal`/`PresentationHandle.Signal`/`ScenePresentationPreviewDriver.Signal`)の挙動自体は変更していない(UI の分かりやすさのみの改善)
- 詳細は [08_presentation.md](08_presentation.md) の「追補（2026-09-17、U-25）」を参照
- 未確認(Unity MCP 接続状況次第。本セッションでは接続できたためコンパイル・テストは確認済みだが、実際のウィンドウ表示は確認していない): Signal ボタンが再生中のみ有効になること、停止するとグレーアウトすること、ツールチップの表示、U-27 の 500px 基準での見切れが無いこと

### U-24（Anchor の原点描画）
[36](36_manual_screenshot_list.md) の #19 / #23 に加えて **#20（`vfx-editor-anchor-handle.png`）もこれ待ち**。直ったら 3 枚まとめて撮影する。

**2026-09-17 実装済み。** 基準の定義（何を原点として描くか）は [21 §3.10](21_anchor_spec.md)、描画メソッドの一覧は [09 §2.2](09_editor_tools.md)。Anchor Editor / VFX Editor（埋め込み Anchor）/ Anchor Group Editor（原点）の 3 か所で、基準に 3 軸 + ワールド座標ラベル、基準 → 最終位置に線 + `LocalOffset` の値が出る。**#19 / #20 / #23 の 3 枚を撮影できる状態**（[36 §5.4](36_manual_screenshot_list.md) の該当行は撮影時に更新する）。

### U-26（実機テスト）— 不要
2026-09-17 にユーザー判断で対応不要となった。[29_network_device_test.md](29_network_device_test.md) の PC-A Host + PC-B Client の確認が済んでいるため、U-1〜U-25 の修正後に再度通す必要はない。

## 2. 変更履歴

- 2026-09-17: U-27（全エディタ 横幅 500px・拡大縮小 100% で見切れない、全体点検）を実装。Unity MCP(`execute_code`)で
  全 28 EditorWindow を幅 500px で開き `resolvedStyle`/`worldBound` を数値比較する方法で点検し、実際に破綻していた
  10 件を修正: 横並び行の `flexWrap` 不足 5 件(`AnimEditorWindow.cs` 再生行、`AnimEditorWindow.Source.cs` の Events 行
  ─ ✕/↗ ボタンが最大 92px 画面外で操作不能だった最優先案件、`PresentationEditorWindow.Preview.cs` 再生行、
  `VfxEditorWindow.Anchor.cs` の Anchor Toggle 行、`CanvasEditorWindow.cs` の ElementFx 行)と、`minSize` の横幅が
  500px を超えていて物理的に 500px まで縮められなかった 5 件(`AudioEditorWindow`/`CameraFxEditorWindow`/
  `VfxEditorWindow`=520px、`AnimEditorWindow`=560px、`PresentationEditorWindow`=**620px**、いずれも 500 に修正)。
  「Toolbar 内の ToolbarButton は折り返さない」という残課題は合成テストで検証し、`flexWrap=Wrap` +
  `style.height=StyleKeyword.Auto` の併用で解決できることを確認(ただし現状どの Toolbar も 500px で破綻していない
  ため未適用、今後の指針として記録)。点検結果の一覧・誤検出として除外した 2 パターン(GraphView のパン領域、
  TextField 内部のネイティブスクロール)は [09_editor_tools.md §7.1.1/§7.1.2](09_editor_tools.md) を参照。
- 2026-09-17: U-25（Presentation の Signal を手動で送る導線を分かりやすくする）を実装。統合プレビューの「Signal レーン(手動発火)」に手順を明文化したラベルを追加し、再生中でなければ Signal ボタンをグレーアウトするようにした(`PresentationEditorWindow.Preview.cs`/`PresentationEditorWindow.cs`)。`docs/DesignerManual/presentation.html` を更新し、スクリーンショット #53 を撮影可能にした([36](36_manual_screenshot_list.md))。
- 2026-09-17: U-23（ElementFx の「▶ 再生」連打で位置ずれ）を実装。真因は `UiTweenManager.StopAll(RectTransform)` が中断された Tween を完了させずに取り除いていたこと(`Stop(handle, complete)` と違い complete 引数が無かった)。`StopAll` に `complete` 引数を追加し、`CanvasEditorWindow.PlayPhasePreview` を `complete: true` で呼ぶよう変更。再現テスト(`UiTweenTests.RapidReplay_SlideInPreset_WithStopAllComplete_SettlesAtRestPosition`)を先に書いて修正前に赤(実測 -328.05 vs 期待 0)であることを確認してから直した。
- 2026-09-17: U-21（Canvas Editor で要素の移動）を実装。ツールバーに「Prefab を開く(要素の移動)」、ElementFx の各要素に「選択して移動(Prefab を開く)」ボタンを追加し、`ModelEditorWindow.OpenPrefab`/`VfxEditorWindow.OpenPrefab` と同じ導線でプレハブモードを開いて Unity 標準ツールで移動・回転・リサイズできるようにした。
- 2026-09-17: U-7（Presentation Editor のシークバーを Anim Editor と同じ形に）を実装。共通部品 `Editor/Common/SeekBarGui.cs` を追加し、`AnimEditorWindow.DrawTimeline` もこれを使うようリファクタリング（見た目は不変）。詳細は [08_presentation.md](08_presentation.md) の「追補（2026-09-17、U-7）」。
- 2026-09-17: U-9 / U-10 / U-12 / U-13 / U-14 / U-15 を実装。共通部品として `Editor/Validation/DataValidationSection.cs`（個別検証、[09 §11](09_editor_tools.md)）と `Editor/Common/CompactFieldLayout.cs`（横並び行のラベル幅、[09 §7.1](09_editor_tools.md)）を追加。`AssetDataInspector` を UI Toolkit 化（U-14、[09 §8](09_editor_tools.md)）。`NewAssetDialog` の仕様書 URL 判定を `WebAppUrl` に統一（U-15、[32 §6](32_spec_web.md)）。**Unity MCP に接続できなかったため、実際の描画・Test Runner での実行は未確認**（`dotnet build` で全 asmdef のコンパイルのみ確認）。
- 2026-09-17: U-16 / U-17 / U-18 / U-19（アセット作成の導線）を実装済みに。新規メニュー定数（`DDriveMenu.AssetsRoot` / `AssetsCreateData` / `GameObjectRoot`）・`Editor/Creation/`・`Editor/Canvas/CanvasSetupService.cs`・`Editor/Inspector/CreatedAssetOpener.cs` を追加。
- 2026-09-17: U-27（全エディタ 横幅 500px・拡大縮小 100% で見切れない）を追加。規約は [09 §7.1](09_editor_tools.md) に記載。
- 2026-09-17: U-24（Anchor の基準を SceneView に描画。[21 §3.10](21_anchor_spec.md) / [09 §2.2](09_editor_tools.md)）と U-22（Anchor Group の自動配置 → 手置きの点に変換。[22 §3.7](22_anchor_group.md)）を実装。U-24 待ちだったスクリーンショット #19 / #20 / #23（[36 §5.4](36_manual_screenshot_list.md)）が撮影可能になった。
- 2026-09-17: 新規作成。スクリーンショット撮影（[36 §5](36_manual_screenshot_list.md)）と実機での通し確認で見つかった 26 件を登録。U-1〜U-6・U-9・U-10・U-12〜U-19 に着手。U-26（実機テスト）はユーザー判断で対応不要に。
