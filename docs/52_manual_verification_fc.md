# 52. FC チケット（T-Drive 連携）の人による確認手順

関連: [51_tdrive_integration.md](51_tdrive_integration.md)（設計・FC チケット本体） / [11_tasks.md](11_tasks.md)（FC 節） / [43_manual_verification_2026-09-17.md](43_manual_verification_2026-09-17.md)・[28_manual_verification_phase5.md](28_manual_verification_phase5.md)（同じ書式の手順書）

> FC チケット（T-Drive 連携のための D-Drive 側の追加、FC-1〜FC-20）の**人による確認**をチケットごとにまとめる手順書。
> 自動検証（コンパイル 0 エラー・EditMode / PlayMode 全件 green）は各チケットの PR で通している。**見た目・操作・Inspector の出し分け**は人が確認する。
> 実装済みのチケットだけ手順を書いてある。未実装のチケットは枠だけ置いてあり、**実装した担当が自分のチケットの節を埋める**（書式は FC-1 の節に合わせる）。
> 不具合を見つけたら、該当チケット行（[11](11_tasks.md) FC 節）と [51](51_tdrive_integration.md) の該当節「実装メモ」を参照して修正する。

## 0. 確認の進め方

- **所要時間の目安**: FC-1 = 約 25 分 / FC-2・FC-12 = 約 15 分（実装済み分の合計 約 40 分）
- **前提**: Unity 6000.3.13f1 で D-Drive を開き、`main`（FC-1 と FC-2 / FC-12 のマージ後）を取得済みであること。コンパイルエラーが無いこと。確認用シーンは `Tools > D-Drive > Editors > Cutscene確認用シーンを開く` で開く（シーンは `Assets/GameData/PreviewScenes/CutscenePreviewScene.unity`、無ければ自動生成される）
- **順番**: §1（FC-1、Cutscene）→ §2（FC-2 / FC-12、Model）の順。互いに独立なので片方だけでもよい。T-Drive 側と合わせる項目は §22 にまとめてある（T-Drive のパッケージが入ってから）
- **書式**: 各項目は「手順 → 期待する結果 → 結果欄」。結果欄は `□ 未 / OK / NG` のいずれかに書き換え、NG はメモを残す
- 自動テストで担保済みで目視が要らないものは「自動テストで確認済み（テスト名）」と書いてある（再確認は不要）

## 1. FC-1: 同じ相手へのバインド（`CutsceneBindTarget.SameAsTrack`）の確認

[51] §4.2、[26](26_timeline.md) §4.2・§4.4。自動テストで確認済み: 2 パス解決・並び順非依存・鎖・循環と未解決の警告 + no-op・モデルが 1 体（`CutsceneSameAsTrackTests`）、Edit Mode の解決（`CutsceneEditModeDirectorSetupTests`）、Validator の 4 検査（`CutsceneDataValidatorTests`）。以下は Inspector と実際の再生の目視。

### 1.1 Inspector の出し分け

準備: `CutsceneData` を 1 つ選び（確認用シーンのものか、`Assets/GameData` の既存の Cutscene）、Inspector で `Bindings` を開く。Bindings が 2 件以上無ければ「+」で足して `Track Name` に別々の名前（例 `Hero`、`Hero_Ext`）を入れる。**変更は確認後に Ctrl+Z か `git checkout` で戻すこと**（実データを汚さないため、できれば複製した Cutscene で行う）。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 1-1 | 2 件目の `Target` を `SameAsTrack` にする | その要素の下に **`Source Track Name`** のプルダウンだけが出る。`Model` と `Scene Object Name` は出ない | □ 未 |
| 1-2 | `Target` を `SpawnModel` に戻す | `Model` だけが出る（`Source Track Name` は消える） | □ 未 |
| 1-3 | `Target` を `SceneObjectByName`、続けて `AnchorPoint` にする | `Scene Object Name` だけが出る | □ 未 |
| 1-4 | `Target` を `MainCamera` / `Self` / `Target` にする | 追加の項目は何も出ない | □ 未 |
| 1-5 | `SameAsTrack` に戻し、`Source Track Name` のプルダウンを開く | 候補は**同じ CutsceneData の Bindings の TrackName**（重複なし）。先頭に「(未設定)」。選ぶと値が入る | □ 未 |
| 1-6 | `Source Track Name` に候補に無い名前（Bindings を `Hero2` などに書き換えて typo にする等）が入っている状態にする | 値は消されずプルダウンに残って見える（検査・Validator で警告される。§1.2・§1.3） | □ 未 |

### 1.2 バインド検査の表示

準備: `Timeline` が設定された CutsceneData（確認用シーンの Cutscene。Timeline が未設定だと検査自体が出ない）。Inspector の「バインド検査(Timeline のトラック名 ⇔ Bindings)」を見る。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 1-7 | `Hero_Ext`（Timeline に同名のトラックがあること）を `SameAsTrack` + `Source Track Name = Hero` にする | 検査に `✓ Hero_Ext → Hero(同じ相手にバインド)` が出る | □ 未 |
| 1-8 | `Source Track Name` を自分自身（`Hero_Ext`）にする | `✗ Hero_Ext → Hero_Ext(…)` と問題の説明が出る | □ 未 |
| 1-9 | `Source Track Name` を Bindings に無い名前にする | `✗ …(…)` で未解決の説明が出る | □ 未 |
| 1-10 | 2 つの binding を互いに `SameAsTrack` で参照させる（A → B、B → A） | どちらも `✗`（循環）になる | □ 未 |

### 1.3 Validation の警告

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 1-11 | §1.1〜1.2 の不正な状態（空 / 自己参照 / 存在しない参照先 / 循環）を作ったまま、`Tools > D-Drive > Validation > Run All` を実行する | Console に該当の CutsceneData について **Warning**（Error ではない）が出る。正しい設定（1-7）に戻して再実行すると消える | □ 未 |

### 1.4 実際の再生（モデルが 1 体だけ出る）

準備: `Tools > D-Drive > Editors > Cutscene確認用シーンを開く` で確認用シーンを開く。Cutscene に `SpawnModel` の binding（`Hero`、`Model` に有効な ModelData）と、`SameAsTrack` の binding（`Hero_Ext` → `Hero`）を作る。Timeline には `Hero` のトラックに加えて、`Hero_Ext` の名前のトラックを足す（Unity 標準の Animation トラックか Activation トラックでよい。Timeline ウィンドウでトラックを追加して**名前を `Hero_Ext` にする**）。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 1-12 | Play Mode に入り、確認用シーンの Cutscene 再生導線（Inspector の「Play Mode 中の再生」）で再生する | **モデルは 1 体だけ**出る（Hierarchy にも 1 体）。2 体目は出ない。`Hero_Ext` のトラックのバインド先が `Hero` と同じモデル（Timeline ウィンドウでトラックを選ぶと同じオブジェクトが入っている） | □ 未 |
| 1-13 | 再生終了 / 停止後 | モデルが片付く（1 体とも消える） | □ 未 |
| 1-14 | Edit Mode に戻り、Cutscene の Inspector の「▶ Timeline ウィンドウで開く」を押し、Timeline ウィンドウでスクラブする | Edit Mode のプレビューでもモデルは **1 体**。`Hero_Ext` のトラックも同じ相手にバインドされる（Timeline ウィンドウを閉じる / 別のアセットを選ぶと片付く） | □ 未 |
| 1-15 | `Hero_Ext` の `Source Track Name` を壊して（空にして）1-12 を再度行う | Console に警告が **1 回**出る。`Hero_Ext` のトラックだけが動かない（ミュート）。`Hero` のトラックと Cutscene の再生は継続する。例外は出ない | □ 未 |

**要判断（FC-1）**: なし（U-1 / U-2 / U-5 は決定済み。[51] §8）。

## 2. FC-2 / FC-12: プール返却時のブレンドシェイプ復元・モデルのスポーン / 返却の通知の確認

[51] §4.3・§4.13、[05](05_model_animation.md) 2026-10-03 追記。**ほぼ自動テストで担保済みで、目視は 2 項目だけ**。

自動テストで確認済み（再確認不要）: `ModelsManagerReturnNotifyTests`（Despawn → 再 Spawn で重みが既定に戻る / `FC_*`・`fcs_*` を含む全シェイプが戻る / Prefab の初期重みが非 0 でもその値へ戻る / 強制回収でも戻る / `sharedMesh == null`・非 Skinned で例外なし / Listener の呼び出し順（返却通知 → リセット）/ Discard 経路でも通知 / Listener が例外を投げても他の Listener と返却が続く / ルートに外部の `IPoolable` があっても両方の `OnReturn` が呼ばれる）、`PoolServiceTests`（複数の `IPoolable` 全部に `OnReturn`・例外で止まらない）、`ModelReturnAllocTests`（Performance レポートに記録）。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 2-1 | ブレンドシェイプ（表情）を持つモデルの ModelData を用意する（Prefab に `SkinnedMeshRenderer` + ブレンドシェイプのあるメッシュ）。Model エディタ（`Tools > D-Drive > Editors > Model`）でその ModelData を開き、`Flags` の Pool を **Pooled**（Max 1 以上）にする。「▶ シーンに配置」で配置し、Hierarchy で配置されたモデルの `SkinnedMeshRenderer` を選んで、Inspector の `BlendShapes` のスライダーを動かして表情を変える（例: 全部 100）。「■ 撤去」を押し、もう一度「▶ シーンに配置」する | 再配置されたモデルの表情が**元の状態に戻っている**（スライダーが Prefab の値。前の表情が残っていない）。**プレビューが毎回新しい GameObject を作る実装のときは、この項目は意味を持たない**（その場合は 2-2 で確認する。どちらだったかをメモに書く） | □ 未 |
| 2-2 | 同じモデルを Play Mode で `Models.Spawn` する簡単なテスト用シーン（または既存の確認用シーン）で、Spawn → Inspector で表情を変える → `Models.Despawn` → もう一度 Spawn する | 再 Spawn したモデルの表情が Prefab の値に戻っている | □ 未 |

目視が要らないもの: `FC_*` / `fcs_*` の扱い（接頭辞で除外しない = 自動テストで確認済み）、通知の順序・例外隔離（自動テストで確認済み）、プールの強制回収（上限超過）経路（自動テストで確認済み）。`IModelInstanceListener` は Prefab に付けたコンポーネントが実装するインターフェースで、画面に出るものではない（T-Drive の `ToonCharacter` が使う。§22）。

**要判断（FC-2 / FC-12）**: なし（U-3 / U-12 は決定済み。[51] §8）。

## 3. FC-3: 「今の視点カメラ」を返す公開 API

未実装（実装時に追記）

## 4. FC-4: 外部パッケージのマーカーの汎用の受け口

未実装（実装時に追記）

## 5. FC-5: カットシーン取り込み完了の公開イベント（Editor）

未実装（実装時に追記）

## 6. FC-6: 取り込みルールの外部拡張 / 不明な種別フォルダの扱い

未実装（実装時に追記）

## 7. FC-7: 依存関係の追跡が Timeline クリップ内の参照まで届くかの調査

未実装（実装時に追記）

## 8. FC-8: カットシーンのキャラ FBX でブレンドシェイプのカーブを通す（保留）

未実装（実装時に追記）

## 9. FC-9: デバッグ / 調整（7-3 / 7-4）との接続（保留）

未実装（実装時に追記）

## 10. FC-10: 外部拡張の契約テスト

未実装（実装時に追記）

## 11. FC-11: MaterialData にパスの無効化・キーワードの欄

未実装（実装時に追記）

## 12. FC-12: モデルのスポーン / 返却の通知

§2（FC-2 と同一 PR のため同じ節）を参照

## 13. FC-13: インスタンスごとのマテリアル値（保留）

未実装（実装時に追記）

## 14. FC-14: 変換表・テクスチャ規則の提供口

未実装（実装時に追記）

## 15. FC-15: 知らないシェーダーを `DDrive/Lit` に変換しない

未実装（実装時に追記）

## 16. FC-16: モデルの名前付きスロットセット

未実装（実装時に追記）

## 17. FC-17: ModelData に外部データへの汎用参照欄

未実装（実装時に追記）

## 18. FC-18: プロジェクト設定の検証の拡張点

未実装（実装時に追記）

## 19. FC-19: 検証の警告の調整

未実装（実装時に追記）

## 20. FC-20: 所有接頭辞（`FC_` / `fcs_`）の一覧と検査

未実装（実装時に追記）

## 22. T-Drive 導入後に確認

T-Drive のパッケージ（`TDrive.*`）が入ってから、T-Drive 側と合わせて確認する項目。今は項目名だけ（実装した担当・T-Drive 側の担当が手順を足す）。

- FC-1: T-Drive の fctrack 取り込み（FC-5 のリスナー）が `SameAsTrack` の binding を足し、Facial トラックがカットシーンのキャラと同じ相手に結ばれる
- FC-2 / FC-12: `ToonCharacter` が `IModelInstanceListener` でスロット適用後に `CharacterLook` を配る / 返却で後片付けする。`FacialCorrectionRunner` の `OnDisable` と重みの復元が二重になっても表情が壊れない
- FC-3 / FC-4 / FC-5 / FC-14 / FC-15: T-Drive 側の対応が入ったとき

## 要判断（全体）

- なし（実装済みチケットの要判断は各節末尾。未決は [51] §8 の U-14・U-15）
