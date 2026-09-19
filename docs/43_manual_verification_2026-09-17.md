# 43. 人による確認手順（2026-09-17、U チケット完了時点）

関連: [39_usability_fixes_2026-09-17.md](39_usability_fixes_2026-09-17.md)（U チケット本体） / [42_distribution.md](42_distribution.md)（移植・P チケット） / [36_manual_screenshot_list.md](36_manual_screenshot_list.md)（撮影リスト） / [41_phase6_review_2026-09-17.md](41_phase6_review_2026-09-17.md)（レビューの残り） / [33_ci_setup.md](33_ci_setup.md)

> この書面は 2026-09-17 の作業（U-7 / U-8 / U-11 / U-20 / U-21 / U-23 / U-25 / U-27、プログラマーマニュアル新設、
> SpecWeb 配信対応、移植方針 A-1〜A-9 の確定）に対する**人の確認手順**をまとめたもの。
> **上から順に実施できる並びにしてある**（`clasp push` の前後で節を分けている）。
> コード変更はすべてコンパイル・テスト green まで確認済みだが、**画面の見た目と操作は未確認**。

## 0. 朝いちばんにやること（2026-09-18 追記）

> 2026-09-18 の深夜作業で状況が変わった。**この節を先に見ること。** §0.5 以降は 2026-09-17 時点の手順で、内容は有効。

### 0.1 あなたの操作が要る

- [x] ~~Windows ファイアウォールの Block 規則を 2 件外す~~ → **2026-09-18 朝に対応済み**。これで §14 が実施でき、合格した

**Unity 上の目視確認（未実施。§1 以降が本体）** — コード変更はすべてテスト green だが、画面の見た目と操作は未確認。
撮影（§5、12 枚）が目視確認を兼ねるので、**撮影しながら §1.1〜1.2 を確認するのが効率的**。

### 0.2 あなたの判断が要る

| # | 判断 | 背景 |
|---|---|---|
| 1 | **`VFX_Player_Slash2.asset` を残すか消すか** | 「Slash を消す → Slash2 を作る → git で Slash の削除を discard」という経緯の残骸とみられる。`VfxCatalog` に正規のエントリがあり Addressables 登録も正しいが、**Prefab 未設定**で剣攻撃デモでは未使用。**Data の削除は取り消しにくいので触っていない** |
| 2 | **`AnchorCatalog` の孤立エントリ `ANC_Can_Vas` の扱い** | カタログにエントリがあるのに対応する Data ファイルが存在しない。新設した `CatalogAddressCoverageValidator` が検出した 1 件目 |
| 3 | **`Validation > Run All` の Error 69 件の方針** | 約 8 割（55 件前後）が Test / Demo / Jam 等のサンプル・実験データ。**この状態そのものが問題**で、実際に `VFX_Player_Slash` の Addressables 未登録は Validator が検出できていたのに埋もれた。サンドボックスフォルダへの隔離案などを [41](41_phase6_review_2026-09-17.md) に記載 |
| 4 | **実運用アセットの Error 14 件**（[41](41_phase6_review_2026-09-17.md) の 9 項目） | `SE_Player_Slash` のクリップ未設定、**`VFX_Player_Slash` の URP で透明になるシェーダー不整合**（§1.3 の U-1「3D プレビューの透明」と同じ症状の可能性）、`SKIN_Button_Skin` の Retiming 12 件の正しい Duration 値など |
| 5 | `heartbeat` の `content_hash` 欄に不一致の文字列が毎行出る | 長時間の実機確認でログが膨らむ。判定には影響しない。直すかどうか |

### 0.2.1 ネットワークの残り（PC-C が繋がったら 10 分）

**リリースビルド相当の切断確認**だけが残っている。手順は [29 §21](29_network_device_test.md) に用意済み
（PC-B = Host、PC-C = Client、PC-A はネットワークの親と配布のみ）。PC-C に Claude Code が無くても
人が上から順に実行できる形にしてある。

**実施前に必ず §21.6 を確認すること** — リリースビルドで `Debug.Log` が残らないと何も判定できない。

### 0.3 2026-09-18 に終わったこと

| | 結果 |
|---|---|
| **K2 の実機再確認** | **合格**（[29 §17](29_network_device_test.md)）。`rtt_app_ms` が 204 → 25978 まで単調増加、平常時の `stale` は 0 件、3 回目の未応答から立つ |
| **§14 ハッシュ不一致** | **合格**（[29 §20](29_network_device_test.md)）。不一致を検出でき（Host / Client 双方に警告、実データは漏れない、接続は継続）、一致に戻せば `OK` になることも確認 |
| **Addressables の登録漏れ** | 修正済み（`9705e2a`）。`VFX_Player_Slash` が未登録でカタログ登録ごと失敗し、**全 ID が Placeholder に落ちていた**。混入は `c67f81a` で、**それ以降に作ったビルドはすべて壊れていた** |
| **カタログ起点の検査を新設** | `CatalogAddressCoverageValidator`。「git で `.asset` を戻しても Addressables の登録は追従しない」という気づけない事故を捕まえる |
| **A-6（`KnownPrefixes`）** | 実施済み（`ead2149`）。`MODELID.MODELPlayerModel` → `MODELID.PlayerModel` 等。**P-5 発効後は永久に直せない項目だったので、期限つきの宿題は無くなった** |
| **Timeline の設計** | 未決ゼロ（`63bce5f`）。**6-10a に着手できる状態** |
| **マニュアルの不備 2 件** | 修正済み（`0801c02`）。未撮影画像の参照と、デザイナー向けページから開発ドキュメントへのリンク。SpecWeb 521 件 green |

### 0.4 今日見つかった実バグ（6 件）

いずれも**静かに壊れていて気づけない型**だった。実機で 2 台繋ぐまで分からなかったものが複数ある。

| # | 内容 | なぜ気づけなかったか |
|---|---|---|
| 1 | **U-20** Anim2D の SE / VFX が出ない | Animator は動き続けるので**見た目は正常**。`Events` だけが空の Placeholder |
| 2 | **U-23** ElementFx の連打で位置ずれ | 開き直すと直るため再現条件が分かりにくい |
| 3 | **偽造 Result** で ContentHash の警告を「OK」に偽装できる | 攻撃者が能動的に送らないと起きない |
| 4 | **K2** `rtt_app_ms` が経過時間で増えない | ユニットテストが 1 秒周期の実時間経過を再現していなかった |
| 5 | **K2** `rtt_app_stale` が平常時にも立つ | 値は正常なのでフラグだけ見ないと分からない |
| 6 | **Addressables の登録漏れ** | `content_hash=OK` が「一致した」ではなく**「比較対象が無くて素通りした」**を意味していた |

---

## 0.5 前提の確認（最初に 1 回）

- [ ] **仕様書同期のトークンを入れ直す** — `EditorPrefs` はマシンごとなので移行されない。`Tools > D-Drive > 仕様書と同期` で
      読み取り / 書き込みトークンを再入力する。URL（`DDriveSpecSettings.asset`）はコミット済みなのでそのまま使える
- [ ] Unity を開き直す（この日の変更はエディタ拡張が中心のため、ドメインリロード済みの状態で確認する）

## 1. `clasp push` の前にやること（Unity 上の目視確認）

### 1.1 この日直した不具合（直っていることの確認が目的）

| # | 何を | どう確認するか | 出典 |
|---|---|---|---|
| 1 | **U-20 Anim2D の SE / VFX が出ない** | Play Mode で、SE / VFX の Frame イベントを設定した Anim2D キャラクターを再生し、**実際に音とエフェクトが出ること**。以前は無音だった | [39](39_usability_fixes_2026-09-17.md) U-20 |
| 2 | **U-20 の副作用確認** | `Validation > Run All` を 1 回実行し、新しいエラーが出ないこと（Anim / Anim2D / Presentation / Shake / Haptics の `Flags.Load` 検出が増えている） | 同上 |
| 3 | **U-23 ElementFx の連打で位置がずれる** | Canvas Editor の ElementFx で `SlideIn` 系を**連打**し、位置がずれないこと | [39](39_usability_fixes_2026-09-17.md) U-23 |
| 4 | **U-11 Inspector の編集可否** | 任意の Data（`TextureData` / `VfxData` 等）を Inspector で開き、`Id` / `Version` / `Author` / `UpdatedAt` が**グレーアウト**していること。`DisplayName` などは編集できること。UiTween / Canvas / Material / Prefab の各エディタが埋め込む「Inspector(全フィールド)」でも同じであること | [09 §8.6](09_editor_tools.md) |

### 1.2 この日変わった操作（新しい導線が期待どおり動くかの確認）

| # | 何を | どう確認するか |
|---|---|---|
| 5 | **U-7 Presentation のシークバー** | Presentation Editor の統合プレビューが、丸ノブではなく **Anim Editor と同じバー**（暗い背景 + 目盛り + 白い再生ヘッド + クリックでシーク）になっていること。**Anim Editor 側は見た目が変わっていないこと**（共通化しただけなので不変が正しい） |
| 6 | **U-8 Anim2D の作成ポップアップ** | `Tools > D-Drive > Editors > Animation (2D)` を開き、ツールバーの「スプライトから新規作成…」でポップアップが出て、**生成まで一通り通ること**。生成後にポップアップが閉じてエディタが対象を開くこと |
| 7 | **U-21 Canvas Editor から要素を移動** | ツールバーの「Prefab を開く(要素の移動)」と、ElementFx 各行の「選択して移動(Prefab を開く)」。プレハブモードが開いて対象が選択され、SceneView がそこを向くこと。Move ツールで動かして **Ctrl+Z で戻ること** |
| 8 | **U-25 Signal の手動送信** | Presentation Editor の Signal ボタンが、**停止中はグレーアウト**し（ツールチップに理由）、再生中に有効になること |
| 9 | **U-27 横幅 500px** | 全エディタを 500px 幅に縮め、上から下まで見切れがないこと。特に **Anim / Presentation の再生行、イベント行（削除 ✕ と開く ↗ のボタン）、Canvas の ElementFx 行、Vfx の Anchor トグル行**。以前は Presentation が 620px より縮まなかったが 500px まで縮むようになっている |
| 10 | **プログラマーマニュアル（ローカル）** | `Tools > D-Drive > プログラマーマニュアルを開く` でブラウザに表示されること。ツールバー「？ マニュアル」▼ の「プログラマーマニュアル/…」から各ページに飛べること |

### 1.3 以前からの持ち越し（2026-09-17 以前の分。未確認のもの）

| # | 何を |
|---|---|
| 11 | **U-1 3D プレビューの透明** — Model Editor で確認用シーンに配置 / 複数モデル並列表示 / `PresentationSkillSlashPreviewScene`。3 つとも同時に直っているはず |
| 12 | **U-2 FBX のマテリアルスロット** — FBX を入れ直して `ModelData` の Material スロットが埋まるか。既存モデルは Model Editor の「元ファイル再読み込み」で埋まるか → **2026-09-18 合格**（再読み込み後に確認用シーンでテクスチャ反映を確認。下の追記 2 件を修正後） |
| 13 | **U-4 / U-5 / U-6 配置ボタン** — Prefab Editor が確認用シーンに置くか / 全エディタで右クリック → 2 メニューが出るか / 配置後に SceneView がフォーカスするか / 「このシーンに本配置」がシーン保存後も残るか（一時配置の掃除に巻き込まれないこと） |
| 14 | **U-16〜U-19 メニュー** — `Assets/D-Drive/Data を作成/`（12 種、対象外のアセットで灰色になるか） / `GameObject/D-Drive/`（7 項目、Undo が効くか） / `Tools > D-Drive > Generate > Canvas + Panel と CanvasData を作成` |
| 15 | **U-22 / U-24 Anchor** — SceneView に基準の 3 軸とワールド座標ラベルが出るか / Anchor Group の「手置きの点に変換」で点の位置と番号が変わらないか |
| 16 | **U-9 / U-10 / U-12 / U-13 / U-14 / U-15** — PresetGallery ボタン / 「SE も鳴らす」の見切れ / プレビューバー / 検証セクション / Fade 欄 / 仕様書 URL |
| 17 | **P1-2 の安全弁（意図的に壊して確認する）** — トークンを空にして「取得」→「適用」。TuningTable が消えず、画面に理由が出ること。修正前はここで TUNING が空生成されコンパイル不能になっていた |
| 18 | **P1-1 Pool** — `PrefabData` の `Flags.Pool` を `Kind = Pooled` / `MaxCount = 1` にして Spawn → Spawn → 1 つ目を Despawn。2 つ目が消えないこと |

**2026-09-18 追記(項番 12 の修正)**: 「元ファイル再読み込み」でスロットは埋まるが、確認用シーンに配置中のモデルが
白いままになる別バグを確認・修正した。原因は `ModelEditorWindow` の `SceneAnimPreviewDriver` が持つ
`EditorAnchorRegistry` スナップショットが再読み込みで新規作成した `MaterialData`/`TextureData` を認識できず、
`MaterialManager.ResolveTexture` が解決に失敗して DDrive/Lit の既定 `_BaseMap`（白）になっていたこと。
`SceneAnimPreviewDriver.RefreshRegistry()`（`Assets/DDrive/Editor/Anim/SceneAnimPreviewDriver.cs`、
`EditorAnchorRegistry.Refresh` + 共有 Material キャッシュ `Clear()`）を追加し、`ModelEditorWindow.RunSlotBinder`
（`Assets/DDrive/Editor/Model/ModelEditorWindow.cs`）から呼んで、配置中なら配置し直すようにした
（`MaterialEditorWindow.EnsureRegistryFresh` と同じ方式）。(当初は未検証と書いたが、下の発光修正と合わせて 2026-09-18 にユーザー確認で合格)。確認内容:
「元ファイル再読み込み」の前に対象モデルを確認用シーンに配置した状態で実行し、白くならずマテリアルが反映されること。

**2026-09-18 追記(項番 12・実際の原因は別にもう1つあった)**: 上記のレジストリ再構築を直した後も、Unity MCP
接続下で確認すると `Shirts` 等の Renderer が真っ白(発光)のまま残った。実際の原因はレジストリではなく、
`UnityMaterialMigrator`/`MayaMaterialImporter` で生成した `MAT_Materials_*`(9件、`Assets/GameData/Material/Materials/`、
元は UnityChan の UTS 系 `.mat`)の `MaterialCommon.EmissionColor` が白(1,1,1,1)・`EmissionIntensity` が 1 になっていたこと。
元の `.mat` は `_EMISSION` キーワードを立てていない(発光オフ)のに `_EmissionColor` プロパティ自体は白の値を残しており、
`MayaMaterialImporter.BuildCommon`(`Assets/DDrive/Editor/Material/MayaMaterialImporter.cs`)がキーワードを見ずに
色の値だけで発光判定していたため、発光オフの Material が `MaterialCommonBinding.Apply` で `_EMISSION` を立てられ
白発光していた。`source.IsKeywordEnabled("_EMISSION")` を見てから `_EmissionColor` を引き継ぐように修正し(キーワード無し
なら黒・Intensity 0 に落とす)、`ModelSlotBinder.Rebuild(MODEL_Player_Model, ensureMaterials:true)` で 9 件を再移行して
確認した(Common が更新され、Albedo は引き継がれたまま)。`MaterialCommonBinding.Apply` を直接呼んで `_EMISSION` が
立たなくなったことも確認済み。EditMode 全 819 件 green(`MayaMaterialImporterTests` にキーワード有無それぞれの
テストを追加)。上記のレジストリ再構築の修正自体は別の不具合(テクスチャ未解決)への対応として有効なので取り下げない。

## 2. `clasp push`

```
cd Tools/SpecWeb && clasp push
```

生成物（`html/manual/designer/` と `html/manual/programmer/`、`src/ManualPages.js`）はコミット済みで、
ドリフト検出テストも green。push とデプロイだけが残っている。

## 3. `clasp push` の後にやること（Web 側の目視確認）

| # | 何を |
|---|---|
| 19 | **画面が表示されるか** — `XFrameOptionsMode` を `ALLOWALL` → `DEFAULT` に変えた影響。ダメなら `src/Code.js` の 1 行を戻す |
| 20 | **マニュアルのナビ** — 「デザイナーマニュアル」「プログラマーマニュアル」の 2 つが並び、それぞれ正しく開くこと |
| 21 | **マニュアル間リンク** — デザイナー ⇄ プログラマーのリンクで iframe の中身が**両方向とも**切り替わること |
| 22 | **プログラマーマニュアルのコード表示** — `pre` / `.sig` / `.bad` / `.good` のスタイルが当たっていること（`@import` のインライン展開が効いている証拠） |
| 23 | **古い URL の後方互換** — `kind` を持たない既存の `?page=manual&p=...` の URL が、これまでどおりデザイナーマニュアルを開くこと |
| 24 | **D-Drive の「Web に送信」** — `assetState` / `assetParams` が `mutateMany` 経由になった |
| 25 | **Markdown プレビューの画像 alt** |
| 26 | **`?api=1` はトークン必須になった**（CSRF 対策の仕様変更）。トークン無しで叩く運用が残っていれば read トークンに切り替える |

## 4. CI

- [ ] `Tools\CI\run-ci.cmd` を通す。**`[6/6] NetCheck` の失敗が従来握りつぶされていた**ので、初回は今まで見えていなかった失敗が出る可能性がある

## 5. スクリーンショット撮影（12 枚）

原因が解消して撮影可能になったもの。手順は [40](40_manual_screenshot_workflow.md)、対象は [36](36_manual_screenshot_list.md) §5.4 / §5.6。
**撮影作業が上記 1.1〜1.2 の目視確認を兼ねる。**

- 新規 10 枚: #6 / #11 / #14 / #15 / #19 / #20 / #23 / #27 / #54 / **#53（U-25 完了により撮影可能になった）**
- 撮り直し 3 枚: **#32 / #33 / #34**（U-8 で Anim2D Editor の UI 構成が変わり、配置済みの画像が実画面と食い違っている。[36 §5.6](36_manual_screenshot_list.md) に撮り直しの指示あり）

確認事項として残っているもの（[36 §5.5](36_manual_screenshot_list.md)）:
- #58 が「詳細パネル」ではなく「新規発注」パネルである件
- #1 / #3 の下端切れと「更新者」列の `yamag`
- #58 の実名 `山口`

## 6. この日に見つかった残課題（確認ではなく、今後の作業）

| 項目 | 内容 |
|---|---|
| テスト実行が実データを汚す | EditMode / PlayMode のテスト実行が、実在する `PresentationData` アセットの `Version` / `UpdatedAt` を書き換える（テスト分離の不備）。2026-09-17 の作業中に毎回手で戻していた。**先に直しておくと、以後の `git status` が読みやすくなる** |
| ~~Presentation に Anchor 上書きが無い~~ | **決定・実装済み（2026-09-19）**。`Audio.PlaySe(id, anchor)` / `Vfx.Spawn(id, anchor)` にはあるコード側からの Anchor 上書きが Presentation のトラックには無かった問題（`PresentationManager` に参照先 VfxData/SeData の `AnchorId`/埋め込み Anchor が 1 件もヒットしない）を解消し、トラックの Anchor とアセット側の Anchor の両方を参照するようにした（3 ケース: アセット側のみ / トラックのみ / 両方=親子合成。実装は `Runtime/Presentation/PresentationTrackAnchorComposer.cs` に集約）。詳細・確認手順は [08_presentation.md](08_presentation.md) 実装メモ（2026-09-19、トラック/アセット両方の Anchor 参照）と本書 §11 を参照 |
| 種別固有フィールドの編集可否 | U-11 は `AssetDataBase` 共通 4 フィールドのみ対象と決定済み（2026-09-17）。将来変えるなら `[InspectorReadOnly]` を足すだけでよい |
| Toolbar の折り返し | U-27 で「Unity の制約で折り返せない」は**誤りだと判明**（`flexWrap` + `height: StyleKeyword.Auto` で可能）。現状溢れている Toolbar は無いため未適用。手法は [09 §7.1.2](09_editor_tools.md) |
| テストの穴 13 件 | [41](41_phase6_review_2026-09-17.md)。ネット系 5 件（偽造 Result / 保留のフラッシュ / タイムアウト時のイベント / 保留経由の偽造 / Cancel のレート制限）は実バグが 5 回出ている領域なので優先度が高い。**PC 2 台の実機確認が要る** |
| P2-6 の本筋 | Validation の「全体結果」を Asset 無しの別経路にする（`ValidatorRegistry` = Foundation を触るため範囲外にしていた） |
| 移植（P チケット） | [42](42_distribution.md)。A-1〜A-9 は 2026-09-17 に確定済み。**A-6（`KnownPrefixes` の不整合）は P-5（1.0.0）より前が最後の修正機会** |

## 7. Timeline(6-10a〜d)の人による確認(2026-09-18 追記)

> **2026-09-19 決定**: 本節と §10(Cutscene の Edit Mode プレビュー)の確認は、デザイナーに UnityChan 素体の確認用 FBX を作ってもらってから行う([46_cutscene_fbx_request_unitychan.md](46_cutscene_fbx_request_unitychan.md) に依頼票と Unity 側の事前準備〔ModelData の Avatar 設定・識別子〕)。そのため **P チケット([42](42_distribution.md))の後**に実施する。

Timeline(Maya FBX 取り込み + D-Drive トラック、[26_timeline.md]、6-10a〜d)はコンパイル・EditMode/PlayMode テスト green(860/860・717/717)まで確認済みだが、**この環境には実 Maya 由来の FBX(カメラ・キャラアニメ付き)が無いため、実際の見た目・実データでの取り込みは未確認**。以下は実 Maya 素材が用意できたときに行う確認手順。

### 7.1 まず必要なもの

- Maya で書き出した実 FBX セット([DesignerManual/cutscene-maya-export.html](DesignerManual/cutscene-maya-export.html)の手順どおり): `<ショット>.fbx`(カメラ + 小物)+ `<ショット>__<Model識別子>.fbx`(キャラごと)
- 対応する `ModelData`(`MODEL_*_<識別子>.asset`)が Humanoid Avatar 込みで先に登録済みであること

### 7.2 Maya FBX 取り込みの確認

- [ ] `Assets/SourceAssets/Cutscene/<カテゴリ>/` に FBX セットを置くと自動で取り込まれ、Asset Browser の「Cutscene」にショットが現れる
- [ ] `Validation > Run All` で以下を確認する(いずれも今回追加した検査、[26_timeline.md] §5.3/§5.4):
  - fps 不一致(Warning): カメラ/キャラ/小物のファイル間で fps が違う場合に出る
  - `FrameRate` と取り込んだアニメーションの fps の不一致(Warning、FixAction で自動修正可)
  - Humanoid クリップだが Avatar 未設定(Warning、`ModelData` 側)
  - `CutsceneImportProfile.DefaultFrameRate` を 30↔60 に変えても既存 `CutsceneData.FrameRate` が変わらないこと([26] §5.3 の AC どおり)
- [ ] 画角(FieldOfView)のカーブが実際に付くか([26] §7.3 未検証事項)。ピント距離・絞り・イベント用ロケーターは今回未対応(空のまま、または手動設定)であることを確認する

### 7.3 Inspector 導線・確認用シーンの確認

- [ ] 取り込まれた `CutsceneData` を選択し、Inspector 上部の「▶ Timeline ウィンドウで開く」を押すと標準 Timeline ウィンドウが開くこと
- [ ] 同じく「▶ Cutscene確認用シーンを開く」を押すと `Assets/GameData/PreviewScenes/CutscenePreviewScene.unity` が開くこと(無ければ自動生成される)
- [ ] バインド検査(Bindings ⇔ Timeline のトラック名)の一覧が、実際の役割名と一致していること。取り込みで自動生成された役割名(カメラ/キャラの識別子/小物の名前空間)が Bindings 側にも登録されていること
- [ ] 確認用シーンで **Play ボタンを押して Play Mode に入る**(`CutsceneManager` は起動オブジェクト `DDriveRuntimeBootstrap` 経由でしか組み立てられないため。Camera/SE/VFX/UI/AnchorGroup/Shake/Haptic/Event/Signal は Edit Mode のまま「▶ Timeline ウィンドウで開く」でも確認できるようになった〔2026-09-19、§7.5〕。Play Mode でしか確認できないのは Presentation クリップ・通信対戦・入力ロック・Skip)
- [ ] Play Mode 中に CutsceneData の Inspector の「● 再生(Play Mode)」を押し、カメラがゲームカメラから Maya カメラへ滑らかに繋がり、終了後に元へ戻ること(§4.6.2 のブレンド)
- [ ] Camera クリップの `StepFps` を変えると、カメラだけコマ落ちしキャラは滑らかなままであること(§4.6.3)
- [ ] Play Mode 中に標準 Timeline ウィンドウでスクラブし、SE/VFX が連打・残留しないこと(§4.4)
- [ ] Console に「上書きされました」という警告(`CameraExecutionOrderValidator` の実行時検出 2、[26] §4.6.5)が出ないこと(出た場合はゲームカメラ制御の実行順が契約に違反している)

### 7.4 再取り込み・削除の確認

- [ ] Maya で直して同じファイル名で上書き書き出し → 自動生成トラックだけ差し替わり、Unity 側で足した SE/VFX トラックや Camera クリップの `StepFps`/`BlendIn`/`BlendOut`/`Focus` が保持されること
- [ ] キャラの FBX を削除しても、対応する Timeline トラックは削除されずミュートになること(§5.2 の簡略化。自動ミュートは未実装なので、実際には「残るだけ」の可能性がある — 挙動を確認して食い違えばこのページと [26_timeline.md] を更新すること)

## 8. Presentation Editor の SceneView Anchor 表示の確認(2026-09-19 追記)

[08_presentation.md](08_presentation.md) 実装メモ(2026-09-19、SceneView に Anchor を表示)の人による確認手順。コンパイル・EditMode/PlayMode テストの結果は本チケットの最終報告を参照。

- [ ] `Tools > D-Drive > Presentation Editor` を開き、Vfx トラックと Se トラックを 1 本ずつ含む `PresentationData`(例: `PRES_Demo_SkillSlash.asset`)を対象にする
- [ ] 「確認用シーンを開く」でモデルを配置する
- [ ] トラック一覧の上にある「SceneView 表示」トグルと「表示対象」(すべて / 選択中のみ)が表示されること
- [ ] 「表示対象」=「すべて」のとき、Vfx/Se トラックの位置に色つきの点(Vfx=マゼンタ、Se=シアン)がラベル付き(`[index] Kind アセット名`)で表示され、Anim/CameraShake/Haptic/HitStop/Marker/Signal 等のトラックは何も描かれないこと
- [ ] SceneView 上の点をクリックすると、トラック一覧の対応する行が選択状態(展開)に切り替わり、その点に移動ハンドルが表示されること(移動ツール)。回転ツールに切り替えると回転ハンドルに変わること
- [ ] 移動/回転ハンドルを操作すると、トラック一覧の該当トラックの「Anchor」欄(LocalOffset/LocalEuler)がその場で更新されること(Undo(Ctrl+Z)で戻せること)
- [ ] 「表示対象」=「選択中のみ」にすると、選択中の 1 本の点(+ハンドル)だけが残り、他のトラックの点が消えること
- [ ] Presentation Editor を 2 つ開いて別々の `PresentationData` を選ぶと、最後にフォーカスしたウィンドウだけがハンドル付きで描画され、もう片方は薄い目印だけになること(`SceneGuiOwner`)。VFX Editor / Anchor Editor を同時に開いても同様に競合しないこと
- [ ] SceneView でハンドルを動かした直後は「▶ 再生」中の実体はその場では動かない(既知の制約。次に「▶ 再生」/「⏮ 最初から」を押すと新しい位置が反映されること)
- [ ] `Target`(Self/ContextTarget/World/Anchor)を切り替えると、点の基準(ワールド原点扱いになるか、配置したモデル基準になるか)が説明どおりに変わること。特に `ContextTarget` は統合プレビューでは常にワールド原点扱いになること(`ctx.Target` が常に null のため)
- [ ] デザイナーマニュアル `docs/DesignerManual/presentation.html`(「専用エディタの使い方」の新しい手順)の説明どおりに操作できること

## 9. Presentation の AnchorGroup トラックの確認(2026-09-19 追記)

[08_presentation.md](08_presentation.md) 実装メモ(2026-09-19、AnchorGroup トラック)の人による確認手順。コンパイル・EditMode/PlayMode テストの結果は本チケットの最終報告を参照。

- [ ] `Tools > D-Drive > Presentation Editor` で、既存の `PresentationData` に Asset Browser / Project ウィンドウから `AnchorGroupData`(例: `Assets/GameData/AnchorGroup/` 配下の格子状の配置セット)をタイムラインへドラッグ&ドロップすると、Kind=AnchorGroup のトラックが「Vfx / AnchorGroup」レーンに追加されること
- [ ] トラック一覧で Kind を「AnchorGroup」に切り替えると、Asset 欄が `AnchorGroupData` を受け付けるドロップダウンになること(Vfx 用の ID を選ぼうとすると弾かれる/選べないこと)
- [ ] Asset 欄に配置セット以外の ID を強引に割り当てた場合、「検証」に「トラック N(AnchorGroup)の Asset の種別が AnchorGroup ではありません」の Error が出ること
- [ ] 「確認用シーンを開く」でモデルを配置し「▶ 再生」を押すと、AnchorGroup トラックの発火タイミングで配置セットの全点に VFX / SE が実際に出ること(既存の Anchor Group Editor の「▶ 全点」と同じ見え方になること)
- [ ] SceneView で AnchorGroup トラックの点が Vfx(マゼンタ)/Se(シアン)とは異なる黄緑色で、番号付きの点として**全点**表示されること。「表示対象」=「選択中のみ」で AnchorGroup トラックを選んでも、1 点だけでなく全点が表示され続けること
- [ ] AnchorGroup の点には移動/回転ハンドルが一切出ないこと(クリックしてもトラックが選択状態になるだけで、位置は動かせないこと)。点の近くに「(編集は Anchor Group Editor)」の案内ラベルが出ること
- [ ] 再生中、SceneView に出た VFX が(静止画ではなく)実際にアニメーション(パーティクルの動き)して見えること(EditMode の手動 Simulate に Adopt されているかの確認。動いていなければ `ScenePresentationPreviewDriver`/`SceneAnimPreviewDriver.Groups` の Adopt 配線を疑う)
- [ ] Stop On Cancel を ON にしたトラックで、演出の途中に「⏸」→「■」(Cancel 相当の操作、またはウィンドウを閉じて `StopCurrent`)を行うと、その配置セットの VFX/SE が止まること。OFF のときは Cancel 後も鳴り続けること
- [ ] デザイナーマニュアル `docs/DesignerManual/presentation.html`(トラック種別の表・SceneView の説明)と `docs/DesignerManual/anchor-group.html`(「他の演出とまとめて使いたいとき」)の説明どおりに操作できること

## 10. Cutscene の Edit Mode プレビュー(Timeline ウィンドウ主導)の確認(2026-09-19 追記)

[26_timeline.md] §4.4 実装メモ(2026-09-19)。コンパイル・EditMode 898/898・PlayMode 721/721 green まで確認済み。Unity MCP `execute_code` で一時的な CutsceneData(SE クリップ + Signal マーカー + Camera クリップ)を組み立てて実 Editor Manager 経由で動くことを自動確認済みだが、**実際の Timeline ウィンドウの操作感(ドラッグでのスクラブ、再生ボタンの手触り)は人による確認が必要**。

### 10.1 SE/VFX/UI/AnchorGroup/Event/Signal の確認

- [ ] Assets/GameData/Cutscene 配下(または新規)の `CutsceneData` に SE/VFX/UI/AnchorGroup クリップ、Event/Signal マーカーを 1 つずつ置く
- [ ] Inspector の「▶ Timeline ウィンドウで開く」を押す → `Assets/GameData/PreviewScenes/CutscenePreviewScene.unity` が開き、`"Cutscene Timeline Preview (Edit Mode)"` という GameObject が選択された状態で標準 Timeline ウィンドウが開くこと(**Play Mode に入っていないこと**を確認)
- [ ] Timeline ウィンドウの再生ボタンを押すと、置いた SE が実際に鳴り、VFX が実際に再生され、UI(Canvas)が開き、AnchorGroup が再生されること
- [ ] Console に Signal マーカーのログ(`[DDrive] Cutscene Signal (Edit Mode プレビュー): '...'`)が、置いた時刻を過ぎたタイミングで 1 回だけ出ること
- [ ] Event マーカー(AssetEvent=PlayAsset)で指定した SE/VFX/AnchorGroup が、マーカーの時刻で実際に鳴る/出ること
- [ ] 再生を止めて(一時停止 or 頭出し)、再生ヘッドをドラッグしてスクラブしても、SE が連打されないこと(ドラッグ中は無音)
- [ ] 再生ヘッドを後ろへドラッグしてから、もう一度再生ボタンを押すと、通過し直したマーカー/Event が正しく再発火すること(巻き戻し後の再発火)
- [ ] 一時停止中に Timeline ウィンドウで新しいトラック/マーカーを追加してから再生すると、新しく追加した分もその場で反映されること

### 10.2 Camera クリップ・Shake/Haptic マーカーの確認

- [ ] Camera クリップを置いて再生すると、`Camera.main` がブレンド無しでカーブどおりに動くこと(ゲームカメラへの繋ぎのブレンドは無し、§4.4 の Edit Mode の仕様どおり)
- [ ] クリップの区間外(HasData=false)へスクラブすると、Camera が「Timeline ウィンドウで開く」を押す前の元の位置・画角・ピント距離へ戻ること
- [ ] Shake マーカーを置いて再生すると、`Camera.main` が実際に揺れること。Haptic マーカーを置いて再生すると、接続したゲームパッドが実際に振動すること(パッドが無ければ確認をスキップ)

### 10.3 Play Mode でのみ確認できるもの

- [ ] Presentation クリップは Edit Mode では何も再生されない(no-op、警告も出ない)ことを確認したうえで、確認用シーンで Play ボタンを押して Play Mode に入り、CutsceneData の Inspector の「● 再生(Play Mode)」から同じ CutsceneData を再生し、Presentation クリップが実際に再生されることを確認する
- [ ] 通信対戦(`Flags.Net=Cosmetic`)・入力ロックの通知(`Cutscene.OnInputLockChanged`)・Skip は Play Mode でのみ確認する(docs/29 の実機確認手順に準じる)

### 10.4 導線・表示の確認

- [ ] `CutsceneDataEditor` の Inspector 上部の HelpBox の文言が実際の挙動と一致していること(Edit Mode でできること/できないことの案内)
- [ ] デザイナーマニュアル `docs/DesignerManual/cutscene-maya-export.html`(「Unity 側で確認する」「Unity の Timeline ウィンドウで演出を足す」節)の説明どおりに操作できること

## 11. Presentation のトラック/アセット両方の Anchor 参照の確認(2026-09-19 追記)

[08_presentation.md](08_presentation.md) 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照)の人による確認手順。コンパイル・EditMode/PlayMode テストの結果は本チケットの最終報告を参照。§6 の「Presentation に Anchor 上書きが無い」問題への対応。

- [ ] `Tools > D-Drive > Presentation Editor` で、Vfx トラックを 1 本含む `PresentationData` を対象にする。参照先 `VfxData` の埋め込み `Anchor` を(NamedObject 等で)設定し、トラック自身の Anchor は既定値(未設定)のままにして「確認用シーンを開く」→「▶ 再生」すると、**アセット側の Anchor の位置**に VFX が出ること(以前はアセット側が一切無視され World 原点に出ていた)
- [ ] 同じトラックのトラック側 Anchor も別の位置に設定すると、**トラックの Anchor を親、アセット側の Anchor を子として合成した位置**(両方のオフセットが積み重なった位置)に出ること
- [ ] トラック一覧の各トラックの「Anchor(VFX/SE の位置)」の見出しに、現在のケース(アセット側のみ / トラックのみ / 両方=親子合成 / 未設定)が 1 行で表示されること。Anchor 欄や Asset 欄を編集すると、その場で表示が切り替わること
- [ ] SceneView 表示(「SceneView 表示」トグル ON)で、ケース1(アセット側のみ)を選択したトラックには**移動/回転ハンドルが出ず**、「トラックの Anchor を設定すると親として上書きできます」の注記が出ること
- [ ] ケース3(両方設定)を選択したトラックには、基準 → トラック Anchor(親、ハンドルあり) → アセット側の各段(点線、ハンドル無し) → 最終位置、の順に描画されること。**ハンドルを動かすとトラック自身の Anchor だけが変わり**、参照先 VfxData/SeData の Anchor は変わらないこと(Undo(Ctrl+Z)で戻せること)
- [ ] `Validation > Run All` で、ケース3のトラックを含む Presentation に Info「トラックとアセット側の両方に Anchor が設定されているため、親子合成されます」が出ること(Error にはならないこと)
- [ ] SE トラックでも同様(埋め込み Anchor だけ設定 → 音の定位が変わること。3D 対応の AudioSource/ミキサー設定があれば、Play Mode で実際に聞こえる位置が変わることも確認できると尚良い)
- [ ] 既存の `PresentationData`(特に `PRES_Demo_SkillSlash.asset` 等)を開き、Validation の Info 一覧を確認し、意図せず親子合成になってしまっているものが無いか確認する
