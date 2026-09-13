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

