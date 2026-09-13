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

