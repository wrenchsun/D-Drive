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
| U-11 | 改善 | 自作エディタに「Inspector（全フィールド）」があるものと無いものがある。ID など編集させたくない項目もあるため、**編集させるもの / させないものを選び分けて分離**する（無い側は「Inspector に移動」程度で十分） | 要望 | 未着手 |
| U-12 | 不具合 | Asset Browser 下部のサウンドのプレビューバーの UI が崩れている | [36](36_manual_screenshot_list.md) #14 | 実装済み |
| U-13 | 不具合 | 個別検証があるものと無いものがある。VFX Editor では「検証」を展開しても何も表示されない | [36](36_manual_screenshot_list.md) #15 | 実装済み |
| U-14 | 不具合 | BgmData の Fade 欄が `No GUI Implementation` と表示される | [36](36_manual_screenshot_list.md) #17 | 実装済み |
| U-15 | 不具合 | 仕様書 URL を設定したのに「仕様書 URL が未設定」と表示される | [36](36_manual_screenshot_list.md) #6 | 実装済み |
| U-16 | 改善 | Asset Browser から新規作成した後、そのアセットを専用エディタで開く | 要望 | 実装済み |
| U-17 | 追加 | Project ウィンドウの右クリックメニューを拡充（音源を右クリック → SeData を作成 など、**できるものはすべて**） | 要望 | 実装済み |
| U-18 | 追加 | Hierarchy の右クリックメニューから AnchorRig・SeEmitter など基本オブジェクトを配置できるように | 要望 | 実装済み |
| U-19 | 改善 | CanvasData の作成が毎回「Canvas → Panel」の 2 手順になっている。Hierarchy と `Tools > D-Drive` からの一発生成を追加 | 要望 | 実装済み |
| U-20 | 不具合 | Animation2D の Anim Editor から SE・VFX を**設定はできるが再生されない** | 要望 | 未着手 |
| U-21 | 追加 | Canvas Editor で要素の移動などができるように | 要望 | 未着手 |
| U-22 | 追加 | Anchor Group で 3×3 などを自動配置した後、**手置きの点に変換する**ボタン | 要望 | 実装済み（[22 §3.7](22_anchor_group.md)） |
| U-23 | 不具合 | ElementFx で SlideIn などを設定して再生ボタンを連打すると位置ずれが起きる（開き直すと戻る） | 要望 | 未着手 |
| U-24 | 追加 | Anchor の SceneView 表示で基準が分からない。LocalOffset だけでなく**基準（原点）の座標も SceneView に描画**する | [36](36_manual_screenshot_list.md) #19・#23 | 実装済み（[21 §3.10](21_anchor_spec.md) / [09 §2.2](09_editor_tools.md)）。#19 / #20 / #23 は撮影待ち |
| U-25 | 改善 | Presentation の Signal を手動で送る操作のやり方が分からない。導線・説明を分かりやすくする | [36](36_manual_screenshot_list.md) #53 | 未着手 |
| U-27 | 改善 | **全エディタ共通の条件**: Windows の拡大/縮小 100%・ウィンドウ横幅 500px で要素が見切れないこと（[09 §7.1](09_editor_tools.md)）。U-10 は個別の 1 件で、これはその全体点検 | 要望 | 未着手 |
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

### U-11（Inspector 全フィールド）
「全部出す / 全部隠す」の二択ではなく、**種別ごとに編集させる項目と読み取り専用にする項目を決める**のが本題。ID のように編集されると壊れるものは読み取り専用にし、どうしても触る必要があるときは Inspector 側で行う。どの項目をどちら側にするかを決めたら [09_editor_tools.md](09_editor_tools.md) に表として残すこと。

### U-27（横幅 500px で見切れない）
2026-09-17 にユーザーが出した**全エディタ共通の条件**。基準環境は Windows の拡大/縮小 **100%**、ウィンドウ横幅の下限は **500px**。規約本体は [09_editor_tools.md §7.1](09_editor_tools.md) に書いた（縦方向の §7「ScrollView ルート必須」と対になる）。

U-10（Button Skin Editor の「SE も鳴らす」が見切れる）はこの条件に引っかかった 1 件目。U-27 は**全エディタを 500px 幅で上から下まで見て、切れている箇所を洗い出して直す**チケット。新規エディタ・レイアウト変更のときは以後この幅で確認する。

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

### U-24（Anchor の原点描画）
[36](36_manual_screenshot_list.md) の #19 / #23 に加えて **#20（`vfx-editor-anchor-handle.png`）もこれ待ち**。直ったら 3 枚まとめて撮影する。

**2026-09-17 実装済み。** 基準の定義（何を原点として描くか）は [21 §3.10](21_anchor_spec.md)、描画メソッドの一覧は [09 §2.2](09_editor_tools.md)。Anchor Editor / VFX Editor（埋め込み Anchor）/ Anchor Group Editor（原点）の 3 か所で、基準に 3 軸 + ワールド座標ラベル、基準 → 最終位置に線 + `LocalOffset` の値が出る。**#19 / #20 / #23 の 3 枚を撮影できる状態**（[36 §5.4](36_manual_screenshot_list.md) の該当行は撮影時に更新する）。

### U-26（実機テスト）— 不要
2026-09-17 にユーザー判断で対応不要となった。[29_network_device_test.md](29_network_device_test.md) の PC-A Host + PC-B Client の確認が済んでいるため、U-1〜U-25 の修正後に再度通す必要はない。

## 2. 変更履歴

- 2026-09-17: U-7（Presentation Editor のシークバーを Anim Editor と同じ形に）を実装。共通部品 `Editor/Common/SeekBarGui.cs` を追加し、`AnimEditorWindow.DrawTimeline` もこれを使うようリファクタリング（見た目は不変）。詳細は [08_presentation.md](08_presentation.md) の「追補（2026-09-17、U-7）」。
- 2026-09-17: U-9 / U-10 / U-12 / U-13 / U-14 / U-15 を実装。共通部品として `Editor/Validation/DataValidationSection.cs`（個別検証、[09 §11](09_editor_tools.md)）と `Editor/Common/CompactFieldLayout.cs`（横並び行のラベル幅、[09 §7.1](09_editor_tools.md)）を追加。`AssetDataInspector` を UI Toolkit 化（U-14、[09 §8](09_editor_tools.md)）。`NewAssetDialog` の仕様書 URL 判定を `WebAppUrl` に統一（U-15、[32 §6](32_spec_web.md)）。**Unity MCP に接続できなかったため、実際の描画・Test Runner での実行は未確認**（`dotnet build` で全 asmdef のコンパイルのみ確認）。
- 2026-09-17: U-16 / U-17 / U-18 / U-19（アセット作成の導線）を実装済みに。新規メニュー定数（`DDriveMenu.AssetsRoot` / `AssetsCreateData` / `GameObjectRoot`）・`Editor/Creation/`・`Editor/Canvas/CanvasSetupService.cs`・`Editor/Inspector/CreatedAssetOpener.cs` を追加。
- 2026-09-17: U-27（全エディタ 横幅 500px・拡大縮小 100% で見切れない）を追加。規約は [09 §7.1](09_editor_tools.md) に記載。
- 2026-09-17: U-24（Anchor の基準を SceneView に描画。[21 §3.10](21_anchor_spec.md) / [09 §2.2](09_editor_tools.md)）と U-22（Anchor Group の自動配置 → 手置きの点に変換。[22 §3.7](22_anchor_group.md)）を実装。U-24 待ちだったスクリーンショット #19 / #20 / #23（[36 §5.4](36_manual_screenshot_list.md)）が撮影可能になった。
- 2026-09-17: 新規作成。スクリーンショット撮影（[36 §5](36_manual_screenshot_list.md)）と実機での通し確認で見つかった 26 件を登録。U-1〜U-6・U-9・U-10・U-12〜U-19 に着手。U-26（実機テスト）はユーザー判断で対応不要に。
