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
- [ ] **(2026-09-20 追記)** Presentation Editor をフォーカスせず(他のウィンドウ・SceneView をクリックした状態のまま)、薄い目印(非所有ウィンドウの表示)をクリックしても選択できること: クリックした瞬間に Presentation Editor が SceneView の描画権を持つウィンドウに切り替わり(タブがフォーカスされる)、そのトラックの行がトラック一覧で展開され、スクロールしてその行が見える位置まで移動すること。既に選択中のトラックの点をもう一度クリックしても何も壊れないこと(エラーが出ない・選択が外れない)
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
- [ ] **(2026-09-20 更新)** SceneView 表示(「SceneView 表示」トグル ON)で、ケース1(アセット側のみ)を選択したトラックにも**移動/回転ハンドルが出る**こと(以前は表示のみだったが、ユーザー要望により変更した)。このハンドルをドラッグすると、その最終位置がドラッグ後の位置になるように**トラックの Anchor(親)が自動で設定され**、Anchor 欄の見出しが「アセット側の Anchor を使用」から「両方設定されているため親子合成」に切り替わること(ケース3へ遷移)。参照先 VfxData/SeData 自体は変わらないこと
- [ ] ケース3(両方設定)を選択したトラックには、基準 → トラック Anchor(親、表示のみ) → アセット側の各段(点線、ハンドル無し) → **最終位置(ハンドルあり)**、の順に描画されること。**掴む点は常に最終位置**で、動かすと**トラック自身の Anchor が逆算されて変わり**(位置がドラッグ先と一致すること)、参照先 VfxData/SeData の Anchor は変わらないこと(Undo(Ctrl+Z)で戻せること)。ドラッグ中、動かす前後で最終位置が正しく追従すること(逆算がずれてジャンプしないこと)
- [ ] `Validation > Run All` で、ケース3のトラックを含む Presentation に Info「トラックとアセット側の両方に Anchor が設定されているため、親子合成されます」が出ること(Error にはならないこと)
- [ ] SE トラックでも同様(埋め込み Anchor だけ設定 → 音の定位が変わること。3D 対応の AudioSource/ミキサー設定があれば、Play Mode で実際に聞こえる位置が変わることも確認できると尚良い)
- [ ] 既存の `PresentationData`(特に `PRES_Demo_SkillSlash.asset` 等)を開き、Validation の Info 一覧を確認し、意図せず親子合成になってしまっているものが無いか確認する

## 12. Presentation Editor から専用エディタを同時に開く / AnchorGroup 同時編集の確認(2026-09-20 追記)

ユーザーの確認作業(§8/§11)で出た指摘 3・4 の人による確認手順。コンパイル・EditMode/PlayMode テストの結果は本チケットの最終報告を参照。

- [ ] `Tools > D-Drive > Presentation Editor` で Vfx トラックを含む `PresentationData` を対象にし、「確認用シーンを開く」でモデルを配置してから「▶ 再生」する
- [ ] Vfx トラックの行に「専用エディタで開く(一緒に調整)」「単体で確認用シーンに開き直す」の 2 つのボタンが出ること
- [ ] 「一緒に調整」を押すと VFX Editor が開き、**Presentation Editor の確認用シーン・配置済みモデル・再生状態(▶ 再生中ならそのまま)が変わらないこと**。VFX Editor の「スポーン先(シーン内・任意)」欄に、Presentation が配置した Self(モデル)が自動で入っていること
- [ ] VFX Editor 側でパラメータや Anchor を編集すると、次に Presentation 側で「▶ 再生」/「⏮ 最初から」を押したときに反映されること(既存の「アセットを編集 → 再生し直すと反映」という仕組みと同じ)
- [ ] 「単体で確認用シーンに開き直す」を押すと、VFX Editor がいつもの単体の流れで開くこと(Presentation の統合プレビューは片付く。未保存のシーンがあれば保存確認ダイアログが出ること)
- [ ] AnchorGroup トラックの行でも同様に「専用エディタで開く(一緒に調整)」を押すと Anchor Group Editor が開き、Presentation のプレビューは維持されたままであること
- [ ] Anchor Group Editor で SceneView 上の点をドラッグして動かすと、Presentation Editor 側の SceneView に描かれている同じ配置セットの点表示(毎再描画で `AnchorGroupPlanner.EnumeratePoints` を呼ぶため)にも**即座に反映される**こと(2 つのウィンドウを並べて確認するとよい)
- [ ] Anim トラック(Kind=Anim、参照先が 3D の `AnimData`)の行でも「一緒に調整」を押すと Anim Editor が開き、対象の Animator が Presentation が配置した Self の Animator になっていること。Anim2D トラックでは「一緒に調整」ボタンを押しても(attach 概念が無いため)通常の Open と同じ動作になること
- [ ] CameraShake/Haptic/Canvas/UiTween 等、attach 概念の無い Kind のトラックでは「専用エディタで開く」の行自体は出る(専用エディタが登録されている種別のため)が、2 つのボタンとも同じ通常の Open になること(仕様どおり)
- [ ] HitStop/Marker/Signal トラック(Asset を持たない Kind)には「専用エディタで開く」の行が出ないこと
- [ ] デザイナーマニュアル `docs/DesignerManual/presentation.html`(「専用エディタを同時に開く」の新しい節)の説明どおりに操作できること

## 13. Tuning ウィンドウ(M-2a/M-2c)の確認(2026-09-27 追記)

MS2026 チームの要望で追加した `Tools > D-Drive > Editors > Tuning（調整値）`([09_editor_tools.md] 「Tuning ウィンドウ」、[11_tasks.md] M-2)。自動テストは分類ロジックと `Tuning.Rebind()` のみなので、見た目・操作は人が確認する。

1. `Tools > D-Drive > Editors > Tuning（調整値）` を開く。既定の対象が `Assets/GameData/Settings/DDriveTuningTable.asset` になっていること。**テストデータは 2026-09-27 に投入済み**(MS2026 `Docs/Spec/05_TuningKeys.md` の 46 キー = Player 13 / Interact 3 / Pickup 1 / Match 3 / Fan 5 / InfluenceObject 3 / Minigame 5 / InfluenceItem 3 / Sabotage 6 / Migration 3 / Level 1 に、確認用の `Level/FixedGravityScale`〔Min==Max〕・`DebugShowHud`〔未分類〕を加えた 48 キー + テーブル `Fan/RankBonus`〔Int/Float/String/Enum 列 × 3 行〕。`Assets/Generated/Tuning.g.cs` も再生成済み)
2. 左のカテゴリ一覧(`Player (n)` のように件数付き)をクリックし、右にそのカテゴリのキーだけが出ること。`/` の無いキーは「(未分類)」に入ること
3. Float/Int は `Min≠Max` のときスライダー、`Min==Max` のとき数値欄。Bool はトグル、Enum は `EnumOptions` のドロップダウン、String はテキスト。右に `Unit`/`Description` が出ること
4. スライダーで値を変える → Inspector の同じ Entry が変わり、`.asset` が保存される(タイトルの `*` が消える)。Ctrl+Z で戻ること
5. `Tables` を選び、`TuningTable.Tables` のグリッド(列×行)でセルを編集できること(列・行の追加削除ボタンは無い)
6. 検索欄に文字を入れると選択中カテゴリ内で絞り込まれること
7. 「キー定数を再生成」で `Assets/Generated/Tuning.g.cs` が更新される、「仕様書と同期」で `SpecSyncWindow` が開くこと
8. `TuningTable` アセットの Inspector 最上部に「Tuning ウィンドウで開く」ボタンがあり、押すとそのテーブルが対象で開くこと
9. Play Mode に入る(`DDriveRuntimeBootstrap` が `TuningTable` を Bind しているシーン)。「Play 中に再読込」ボタンが活性になる。値を変えて押すと `Tuning.Reloaded` が発火する(購読者が無ければ `Tuning.GetFloat` が次の呼び出しから新しい値を返すことをデバッグ表示等で確認)。Edit Mode ではボタンが非活性であること
10. ウィンドウを狭く・広くしてもレイアウトが崩れず、全体が `ScrollView` でスクロールできること

要判断(M-2b): カテゴリごとにアセットを分けたい(git 競合が実際に起きた)と MS2026 チームが判断したときだけ着手する([11] M-2b)。

## 14. Canvas Editor の ElementFx プレビュー(プレハブモード再生・初期状態リセット・選択/フォーカス)の確認(2026-09-29 追記)

ユーザー報告 3 件への対応(PR #85、[39_usability_fixes_2026-09-17.md] 2026-09-29 追記)。自動テストは状態の控え/復元と「再スタートしても最終位置が同じ」までなので、SceneView での見た目・操作は人が確認する。

1. `Tools > D-Drive > Editors > Canvas` で `CANVAS_Can_Vas` を開き、ElementFx のどれかの行で「選択して移動(Prefab を開く)」を押してプレハブモードにする
2. SlideIn 系が割り当てられた行で「▶ 再生」を連打する。SceneView で要素が動き、毎回同じ位置から始まって同じ位置に止まること
3. 再生後にプレハブモードのタイトル(Hierarchy 上部)に未保存の `*` が付かないこと。再生途中で Ctrl+S しても、保存された Prefab の位置・色がずれていないこと
4. 同じ要素で Disappear を再生した後に Appear を再生し、画面外に残ったままにならないこと
5. 各要素の「選択」で Inspector にその要素が出る(Hierarchy でもハイライトされる)こと。「フォーカス」で SceneView がその要素の矩形に寄ること
6. プレハブモードを閉じ、「確認用シーンを開く」の状態で 2〜5 を同様に確認する(実体が無い状態で「選択」を押すと Project の Prefab がハイライトされる)
7. ElementFx 見出し下の「▶ 再生時にその要素を選択」を OFF にして ▶ を押し、選択が変わらないこと

## 15. P-15 追加パッケージの更新(D-Drive 以外の git URL パッケージ + 依存の確認)の確認(2026-10-03 追記)

**確認の記録(2026-10-06 追記)**: 確認日 2026-10-05〜06、確認者 = T-Drive 側(山口)、環境 = `origin/main` 0a41d03(com.ddrive.core 1.3.1 埋め込み)から切ったローカルブランチ・日本語 Windows 11・Unity 6000.3、対象 = T-Drive の `unity/com.tdrive.facial`(仮のタグ `v0.4.90`〜`v0.4.95`・`v0.4.92-rc.1`。確認用で本番のリリースではない)。結果: 導入 → 最新の確認 → 更新 → 前の参照に戻す → プレリリース → 不正な入力 → 依存の警告、という通常の流れは問題なし。**不具合 1 件(BUG-1)・仕様の判断 4 件(Q-1〜Q-4)**を見つけ、2026-10-06 に対応した(下の「2026-10-06 の対応」。実装は PR `fix/p15-verification-bug1-q1-q4`)。結果欄の凡例: OK / NG / 一部 OK / 確認不可 / 未確認。**「要再確認」= 2026-10-06 の修正で期待する結果が変わった(または直した)ので、修正後の版でもう一度確認する**。

**2026-10-06 の対応**: (BUG-1) 日本語 Windows では `git show` の出力が Shift_JIS として読まれ、日本語(UTF-8)を含む package.json が文字化けして JSON が壊れ、事前確認が警告を見逃していた → `GitProcess` で標準出力 / エラーを UTF-8 で読み、さらに「JSON として読めない / `ddriveUpdate` の形が読めない」を「宣言なし」と区別して「事前確認できませんでした」にした(15-25・15-26)。(Q-1) 再追加のメッセージをウィンドウに出す(15-5・15-27)。(Q-2) 警告の主表示は宣言した側の行、相手側の行は「ℹ <宣言した側> が vX.Y.Z 以降を要求 / 想定しています(現在 vA.B.C)」(15-11・15-28)。(Q-3) 依存の警告をアセットに紐付けず、「(project)」と表示する(15-12・15-29)。(Q-4) 「カメラを読むだけ」の型を実行順の検査から外す拡張点 `ICameraExecutionOrderExemptionProvider` とプロジェクト設定を追加(15-30。T-Drive のブリッジが宣言した後に確認)。**このブランチの変更を、報告の記入を済ませたローカルブランチ `tdrive-link-test` の docs/43 に取り込むときは、§15 の行の結果欄・期待する結果が両方で書き換わっているので衝突する(行ごとに、本 PR の「要再確認」の期待する結果を採用し、`tdrive-link-test` の結果の記入を残す)。**

[42_distribution.md] §4.2.1。自動テストで確認済み: URL 入力の解釈（各形式・不正入力）・既存 manifest の登録と候補・設定の往復・`ddriveUpdate` の依存検査（未導入 / 古い / 十分 / MAJOR 差 / 自己・循環参照 / 壊れた JSON）・版上げ前の事前確認（偽の fetcher）・`vX.Y.Z` 形式でないタグ・CHANGELOG 無し。**実ネットワーク・実 `git`・実 `PackageManager.Client` を使う確認は人が行う**（以下）。

**T-Drive のタグ・package.json がまだ無い間の代替**: (a) manifest にある UniTask / R3（`#2.5.11` 等。`vX.Y.Z` 形式ではない git 依存）を「候補」から登録して壊れないことを見る（15-3）、(b) 小さな公開リポジトリ（`vX.Y.Z` タグ付き・`package.json` あり。例: 自分で作ったテスト用の UPM パッケージ。`git` の `file:///` ローカルリポジトリ + `git+file:///...` は Unity の git 依存では使えないので公開リポジトリか SSH を使う）を 15-4〜15-9 に使う。(c) 依存の警告（15-10〜15-12）は、テスト用パッケージの `package.json` に `"ddriveUpdate": { "compatibleWith": { "com.ddrive.core": "99.0.0" } }` のように今より大きい版を書いたタグを 1 つ切って確認する。T-Drive のタグが出来たら 15-13 で同じことを実物で確認する。

準備: ブランチを切ってから行う（manifest.json と `ProjectSettings/DDriveProjectSettings.asset` が変わる）。`git` が PATH にあること。確認後は `git checkout -- Packages/manifest.json Packages/packages-lock.json ProjectSettings/DDriveProjectSettings.asset` で戻せる。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 15-1 | `Tools > D-Drive > Update > 更新ウィンドウ` を開く（D-Drive だけを使っている状態） | 先頭に「パッケージ」一覧があり、1 行だけ「D-Drive(com.ddrive.core)  現在 v… / 最新 未確認 / 依存 OK」。選択中のパッケージは D-Drive で、その下は従来どおり「1. 更新チェック」〜「6. エージェント向けスキルを更新」（見た目・操作が従来とほぼ同じ） | **OK**(2026-10-06): D-Drive の 1 行だけ |
| 15-2 | 「最新の版を確認」→ 更新先を選ぶ → 「manifest を選んだ版に更新する」を押す（ダイアログまで。キャンセルする） | ダイアログに「上げ先の package.json を確認しました」（取得できたとき）または「事前確認できなかった。更新後に確認します」の旨が出る。取得はバックグラウンドで行われ、ウィンドウ上部に「確認中…」と「キャンセル」が出る（Editor は止まらない）。30 秒以上かかる・失敗しても例外で止まらず、キャンセルで何も変わらない。**D-Drive の行では上げ先の取得をせず、待ち無しですぐダイアログが出る** | **確認不可**(2026-10-06): 確認したプロジェクトは D-Drive が埋め込みで「更新チェック対象外(git URL 参照ではありません)」と出た(表示は正しい)。**D-Drive を git URL で入れたプロジェクト(MS2026 / 導入テスト用プロジェクト)で確認** |
| 15-3 | 「manifest にある git URL の依存(未登録)」に UniTask / R3 が出ていること。「登録」を押す | 一覧に行が増え「現在 v… / 最新 未確認」。「最新の版を確認」を押すと「vX.Y.Z 形式のタグが見つかりません(最新版を判定できません…)」と出て、壊れない。「登録解除」で一覧から消え、`Packages/manifest.json` は変わらない | **OK**(2026-10-06): UniTask を登録 → 最新の版を確認 → 登録解除。manifest は変わらない |
| 15-4 | 「URL を入力して追加」に、テスト用リポジトリの git URL（`https://github.com/<owner>/<repo>.git?path=<dir>`、タグなし・`#ref` なし）を入れて「追加」 | 最新の `vX.Y.Z` で導入するダイアログ → 「導入する」で Package Manager が導入し、再コンパイル後に一覧へ登録される（`Packages/manifest.json` に `#vX.Y.Z` 付きで追加） | **OK**(2026-10-06): `#v0.4.90` 付きで追加。1 回目は PC の DNS 不調で「⚠ 導入に失敗しました」と出て壊れず、再試行で成功 |
| 15-5 | 15-4 と同じ URL をもう一度入れて「追加」。`git+https://…` を前に付けた形・`#v…` を付けた形でも試す。**この手順の時点では、15-4 で導入した ID はすでに管理対象に登録されている**(15-4 が導入後に登録するため) | 追加欄に「<ID> は既に管理対象に登録されています(manifest は変わりません)。」と出て(コンソールにも同じ文)、manifest は変わらず、一覧の行は増えない。**「manifest に同じ URL の <ID> があります。管理対象に登録しました。」は、manifest にあって未登録の URL を入れたときだけ出る**ので 15-5 では出ない(15-27 の (a) で確認する。(a) と (b) は別々の状態) | **動作 OK・表示が食い違い**(2026-10-06): manifest 不変・行が増えない。ウィンドウに期待の文が出ず、コンソールに「… を管理対象に登録しました」だけが出た(Q-1)→ **2026-10-06 修正(本 PR)。要再確認** |
| 15-6 | 解釈できない入力（`hello`、空欄）・タグが 1 つも無いリポジトリの URL・存在しない URL を入れて「追加」 | 「⚠ …」の警告が出てコンソールにも Warning。manifest・設定は何も変わらない | **一部 OK**(2026-10-06): `hello`・空欄・存在しないリポジトリは警告のみで何も変わらない。**タグの無いリポジトリは未確認** |
| 15-7 | 追加したパッケージの行を選び「最新の版を確認」 | 現在の参照 `vX.Y.Z`・「最新: vX.Y.Z」。古いタグを選べる（テスト用リポジトリに 2 つ以上タグがあるとき） | **OK**(2026-10-06) |
| 15-8 | 古い方のタグ（`#ref` を一度古いタグにしておく）から「manifest を選んだ版に更新する」→ 更新する | 確認ダイアログの後、manifest の `#ref` だけが差し替わる（URL・`?path=`・`git+` は変わらない）。再コンパイル後、一覧の「現在」が新しい版になる。「2. 版と CHANGELOG」にパッケージ直下の `CHANGELOG.md` の該当節が出る（無ければ「CHANGELOG.md が見つかりませんでした」だけ） | **OK**(2026-10-06): `#ref` だけ差し替わる。CHANGELOG の該当節が出る |
| 15-9 | 「前の参照に戻す」を押す | 元の `#ref` に戻る。もう一度押すと入れ替わる（簡易 1 段 undo） | **OK**(2026-10-06): もう一度押すと入れ替わる |
| 15-10 | 15-8 で使うテスト用パッケージの上げ先のタグの `package.json` に `ddriveUpdate.compatibleWith: { "com.ddrive.core": "99.0.0" }` を書いておき、そのタグへ「manifest を選んだ版に更新する」 | 確認ダイアログに「上げ先の package.json を確認しました」+「・… は D-Drive v99.0.0 以降に対応していますが、v… が入っています。」と「依存が満たされなくなる可能性があります。それでも続けますか?」。「それでも更新する」で進める | **NG(BUG-1)**(2026-10-06): 日本語を含む package.json では事前の警告が出ず「確認しました」だけになった(日本語 Windows。`git show` の出力が Shift_JIS として読まれて JSON が壊れ、壊れた JSON が「宣言なし」として扱われた)。ASCII だけの版での再確認は実施中(未確認)→ **2026-10-06 修正(本 PR)。要再確認**(日本語入りの版での手順は 15-25) |
| 15-11 | 更新後（再コンパイル後）に更新ウィンドウを開く | 一覧の**宣言した側(テスト用パッケージ)の行**が「⚠ 依存に注意(D-Drive v… 以降に対応、現在 v…)」、**相手側(D-Drive)の行**は「⚠」ではなく「ℹ <宣言した側> が v… 以降を想定しています(現在 v…)」(`requires` なら「…を要求しています」)。「依存の確認」に同じ警告(15-28)。選択中のパッケージの「前の参照に戻す」が赤く「⚠ 前の参照に戻す(依存を満たしていません)」になっている | **OK**(2026-10-06): 「⚠ 前の参照に戻す(依存を満たしていません)」が赤く表示された。行の「⚠ 依存に注意」が D-Drive 側の行にも付いた(Q-2)→ **2026-10-06 に表示を変更(本 PR)。要再確認** |
| 15-12 | `Tools > D-Drive > Validation > Run All` を実行する（Data が 1 件以上ある状態） | コンソールに `DD-PKGDEP-COMPAT-OLD` の Warning(Error ではない)。**`[DDrive][Validation] (project): …` と出て、アセットのパスは付かない**(Run All 1 回につき 1 回)。`requires` を書いて未導入にした場合は `DD-PKGDEP-REQUIRES-MISSING` の Warning | **OK**(2026-10-06): `COMPAT-OLD` の Warning。`requires` 未導入は更新ウィンドウの「依存の確認」に × で出た。警告に関係のないアセット(`ANC_Anim_Jump.asset`)のパスが付いた(Q-3)・`FacialCorrectionRunner`(実行順 10000)に実行順の Warning が出た(Q-4)→ **2026-10-06 に対応(本 PR)。要再確認**(15-29・15-30) |
| 15-13 | **T-Drive 導入後に確認**: T-Drive の git URL（`?path=unity/com.tdrive.toon` 等）で 15-4〜15-11 を実物で行う。T-Drive の `package.json` に `ddriveUpdate.compatibleWith: { "com.ddrive.core": "1.4.0" }` がある状態で、D-Drive を 1.3.1 に「元に戻す」操作をする | 事前確認のダイアログで「T-Drive は D-Drive v1.4.0 以降に対応」の警告が出る。D-Drive を 1.4.0 以降に戻すと警告が消える | **一部 OK**(2026-10-06): 実物で 15-4〜15-11 を実施。「D-Drive 1.4.0 以降で警告が消える」は 1.4.0 のリリース後 |
| 15-14 | `Packages/manifest.json` に既に D-Drive が `#vX.Y.Z` で入っている状態で、「URL を入力して追加」に D-Drive 自身の URL を入れる | 追加欄に「com.ddrive.core は既に管理対象に登録されています(manifest は変わりません)。」と出る(D-Drive は常に管理対象)→ 一覧は 1 行のまま増えない(D-Drive は常に 1 行目) | **確認不可**(2026-10-06): D-Drive を git URL で入れたプロジェクトが必要 → **D-Drive を git URL で入れたプロジェクト(MS2026 / 導入テスト用プロジェクト)で確認**。2026-10-06 に文言を変更(Q-1)したので**要再確認** |
| 15-15 | ウィンドウを狭く・広くする。`ProjectSettings/DDriveProjectSettings.asset` の差分を `git diff` で見る | レイアウトが崩れず全体が `ScrollView` でスクロールできる。登録したパッケージが `_managedPackages` に 1 要素ずつ追加されている（既存フィールドは変わらない） | **OK**(2026-10-06): `_managedPackages` に 1 要素だけ追加。レイアウト崩れなし |
| 15-16 | **進捗表示とキャンセル（2026-10-03 追加）**: 追加パッケージの行（T-Drive 等）を選び「最新の版を確認」を押す。回線を遅くするか、存在しないホストの URL の行で試す。「一覧の最新版をまとめて確認」も押す | 押した直後に Editor が固まらず、ウィンドウ上部に「確認中…」（まとめて確認では「<名前> の最新の版を確認中…(i/n)」）と「キャンセル」が出る。「キャンセル」で止まり、「確認をキャンセルしました」と出る。Unity のタスクマネージャーで `git` / `git-remote-https` が残っていない。確認中に別の操作をすると「別の確認が進行中です」の警告で無視される | **OK**(2026-10-06): 進捗表示が出る。Editor は固まらない |
| 15-17a | **認証と再コンパイル（2026-10-04 追加、修正ラウンド 3、FX-R-06 / FX-R-11）**: (a) 資格情報が切れている private リポジトリ（T-Drive など。`git credential-manager erase` 等で切るか、認証が要る URL の行）で「最新の版を確認」を押す。(b) 確認中（回線を遅くしたとき）にスクリプトを 1 つ保存して再コンパイルを起こす（またはウィンドウを閉じる）。再コンパイル後にウィンドウを開き直して操作する | (a) Git Credential Manager のログイン画面が出ず、警告（`Authentication failed` 等）で失敗する。端末で `git ls-remote <URL>` を実行して資格情報を更新してから押し直すと成功する。D-Drive の行の「最新の版を確認」も同じ。(b) `git` のプロセスが残らない（タスクマネージャーで確認）。開き直したウィンドウのボタンが「別の確認が進行中です」で効かなくなることがない | **一部 OK**(2026-10-06): 存在しないリポジトリでログイン画面は出ず、警告で失敗した。**資格情報切れ・確認中の再コンパイルは未確認** |
| 15-17 | **事前確認のキャンセル**: 追加パッケージの行で更新先を選び「manifest を選んだ版に更新する」→ 確認中に「キャンセル」 | 「事前確認を中止しました。事前確認なしで続けますか?」が出る。「キャンセル」で何も変わらず、「事前確認なしで続ける」で通常の確認ダイアログ（事前確認なしの旨）に進む | **OK**(2026-10-06) |
| 15-18 | **プレリリースのタグ**: テスト用リポジトリに `vX.Y.Z`（正式版）と、それより新しい `vX.Y.Z-rc.1` を push する（または既存のものを使う）。追加パッケージの行で「最新の版を確認」 | 「最新:」は正式版のタグ。「プレリリース vX.Y.Z-rc.1 もあります(自動では勧めません…)」と出る。「更新先の版」の一覧に `vX.Y.Z-rc.1（プレリリース）` が並び、既定の選択は正式版。プレリリースを選んで更新すると、manifest の `#ref` が **`vX.Y.Z-rc.1` そのまま**（`vX.Y.Z` に変わらない）。そのあと「最新の版を確認」→ 現在がプレリリースなので、同じ版の正式版が出ればそれが「最新」になる | **OK**(2026-10-06): `v0.4.91` で「最新: v0.4.91」+ プレリリースの案内。rc.1 へ更新すると `#ref` は `v0.4.92-rc.1` のまま |
| 15-19 | **プレリリースだけのリポジトリ**: 正式版のタグが無く `vX.Y.Z-rc.1` だけのリポジトリの URL を「URL を入力して追加」に入れる（#ref なし） | 「正式版(vX.Y.Z)のタグがありません(プレリリースのみ: …)。… #<タグ名> を付けて入力してください」と警告が出て、manifest は変わらない。`#vX.Y.Z-rc.1` を付けると導入のダイアログに進む（タグ名はそのまま） | **未確認**(プレリリースだけのリポジトリが必要) |
| 15-20 | **同じリポジトリの 2 パッケージ**: T-Drive の toon と facial（同じリポジトリの別 `?path=`）を管理対象にし、toon だけ新しいタグへ更新しようとする | 確認ダイアログに「同じリポジトリの com.tdrive.facial は v… のままです。同じタグに揃えてください」が添えられる（自動では揃えない）。同じタグなら出ない | **未確認**(T-Drive の 2 つ目のパッケージ〔toon〕がまだ無い) |
| 15-21 | **`ddriveUpdate` の将来の拡張（2026-10-03 追加）**: テスト用パッケージの `package.json` に `"ddriveUpdate": { "requires": { "com.ddrive.core": "1.4.0", "com.x": { "min": "1.0.0" } }, "below": { "com.ddrive.core": "2.0.0" } }` を書いて「依存を再検査」 | 未知のキー `below` と値がオブジェクトの `com.x` は何も言わず無視される（警告なし）。`com.ddrive.core` の `requires` だけが検査される。値を `">=1.4.0 <2.0.0"` にすると `DD-PKGDEP-BAD-DECLARATION` の Warning。自動テスト（`DdriveUpdateFormatCompatTests`）で確認済みで、ここは通し確認 | **一部 OK**(2026-10-06): 更新後の検査で `below`・オブジェクト値の `com.x` は警告なしで無視された。**`BAD-DECLARATION` は確認中(未確認)** |
| 15-22 | **LFS を使うリポジトリ**（任意。T-Drive の URL など）で、追加パッケージの「manifest を選んだ版に更新する」 | 事前確認が 30 秒以内に終わる（作業ツリーを作らないので LFS のダウンロードが走らない）。`Temp/DDriveUpdate/` に作業フォルダが残っていない | **一部 OK**(2026-10-06): `Temp/DDriveUpdate/` に残りなし。T-Drive は LFS 不使用なので LFS の確認は未実施 |
| 15-23 | **`?path=` が不正**（`..` を含む等。manifest を手で書き換えた行で）。更新を試す | 事前確認は「パッケージのパス(?path=)が不正なため取得しませんでした…」の旨で、他の宣言との照合だけ行い、更新自体は続けられる | **未確認**(これから実施) |
| 15-24 | **導入の確認文**: 「URL を入力して追加」で新しい URL を追加する | 確認ダイアログに「信頼できる提供元のパッケージだけを導入してください。導入すると、そのパッケージのスクリプトがこの Unity Editor で実行されます」が出る。導入中に版上げのボタンを押すと「パッケージを導入中です」と出て始まらない | **一部 OK**(2026-10-06): 確認ダイアログ経由で導入。**導入中に版上げを押す確認は未実施** |
| 15-25 | **日本語を含む package.json での事前警告(BUG-1 の再確認。日本語 Windows で行う)**: 15-10 と同じ準備で、上げ先のタグの `package.json` の `description`(と `displayName`)に**日本語**(例: `表情コントローラーの確認用パッケージです`)を入れ、`ddriveUpdate.compatibleWith: { "com.ddrive.core": "99.0.0" }`(または未導入パッケージの `requires`)を書いて、「manifest を選んだ版に更新する」(ダイアログまで。キャンセルしてよい) | 確認ダイアログに「上げ先の package.json を確認しました」+「・… は D-Drive v99.0.0 以降に対応していますが、v… が入っています。」と「依存が満たされなくなる可能性があります。それでも続けますか?」が出る(「それでも更新する」が選べる)。**日本語が入っていても警告が出ること**が要点(以前は「確認しました」だけで警告が出なかった)。ASCII だけの版でも同じ結果になる(15-10 の ASCII 版の再確認を兼ねる) | □ 未(要再確認) |
| 15-26 | **読めない package.json(BUG-1)**: (a) 上げ先のタグの `package.json` を JSON として壊す(例: `description` の閉じ引用符を消す)。「manifest を選んだ版に更新する」(**ダイアログでキャンセルする**。壊れた package.json は Package Manager も読めない)。(b) 導入済みのテスト用パッケージの `package.json`(ローカル・埋め込みで試す。git 参照のものは変えない)に `"ddriveUpdate": "x"`(文字列。JSON としては正しいが形が違う)を書き、「依存を再検査」と `Validation > Run All` を実行する | (a) ダイアログに「事前確認できませんでした(上げ先の package.json を読めませんでした)。更新後に確認します。」と出て、**「確認しました」とは出ない**(続行 / キャンセルを選べる)。(b) 「依存の確認」に「⚠ <名前> の package.json を読めませんでした(JSON として壊れている、または ddriveUpdate の形が不正です)。…」、`Run All` に `DD-PKGDEP-BAD-DECLARATION` の Warning(Error ではない)。`ddriveUpdate` が無い package.json は何も言われない(従来どおり「宣言なし」) | □ 未 |
| 15-27 | **再追加のメッセージ(Q-1)**: (a) **未登録の状態を作ってから**: 管理対象にまだ登録していないが manifest にはある git URL(15-3 の「候補」と同じもの。または 15-4 で導入したものを「登録解除」して候補に戻したもの)を「URL を入力して追加」に入れて「追加」。(b) **登録済みの状態で**: 既に管理対象のパッケージの URL(`git+https://…`・`#v…` 付きの形でも)をもう一度入れて「追加」。(c) パッケージ ID(manifest のキー)を入れて「追加」 | (a) 追加欄に「manifest に同じ URL の <ID> があります。管理対象に登録しました。」(コンソールにも「… を管理対象に登録しました」)。(b) 「<ID> は既に管理対象に登録されています(manifest は変わりません)。」で、一覧の行は増えず manifest も変わらない。(c) 登録済みでなければ「<ID> を管理対象に登録しました。」、登録済みなら (b) と同じ。どの場合も入力欄は空になり、選択が追加したパッケージに移る | □ 未 |
| 15-28 | **行の依存の表示の出し分け(Q-2)**: 15-11 の状態(テスト用パッケージが D-Drive の最低版を宣言し、D-Drive が古い)で一覧を見る。続けて (a) `requires` に**未導入のパッケージ**を書く (b) `requires` に D-Drive の最低版を書く、で「依存を再検査」 | 宣言した側の行が「⚠ 依存に注意(D-Drive v… 以降に対応、現在 v…)」、相手側(D-Drive)の行が「ℹ <宣言した側> が v… 以降を想定しています(現在 v…)」(アイコンは ℹ。⚠ は付かない)。(a) 宣言した側の行だけ「✗ 依存を満たしていません(<ID> v… 以降が必要、未導入)」で、他の行は「依存 OK」のまま。(b) 宣言した側「✗ 依存を満たしていません(D-Drive v… 以降が必要、現在 v…)」/ 相手側「ℹ <宣言した側> が v… 以降を要求しています(現在 v…)」。D-Drive を上げて条件を満たすと両方「依存 OK」。「前の参照に戻す」の赤表示(依存を満たしていません)の条件は従来どおり | □ 未 |
| 15-29 | **依存の警告にアセットのパスが付かない(Q-3)**: 15-12 の状態(依存の警告が出る)で、Data(`.asset`)が 1 件以上ある状態で `Validation > Run All`。続けて任意の Data(例: Anchor の `.asset`)の Inspector の「検証」節を見る。可能なら `Tools/CI/run-ci.cmd` 相当で `CI.ValidateAll` を実行して `TestResults/ddrive-validation.junit.xml` も見る | コンソールの `DD-PKGDEP-*` は `[DDrive][Validation] (project): …` で、`Assets/…` のパスが付かない(1 回の Run All で 1 回だけ)。Data の Inspector の「検証」節に依存の警告は出ない。JUnit の該当 `testcase` の `classname` は `(project)` | □ 未 |
| 15-30 | **実行順の検査の除外(Q-4。T-Drive 導入後に確認)**: T-Drive の `FacialCorrectionRunner`(実行順 10000。カメラを読むだけ)が入り、T-Drive のブリッジ(Editor)が `ICameraExecutionOrderExemptionProvider` で宣言した版(T-Drive の FU-3 対応後)で、CutsceneData が 1 件以上ある状態で `Validation > Run All`。また、`Project Settings > D-Drive > 実行順の検査の除外` に、プロジェクト内の「カメラを読むだけ」のスクリプトの型の完全修飾名 + 理由を書いて `Run All` | 宣言された型(`FacialCorrectionRunner` / 設定に書いた型)に Warning が出ず、Info が 1 件「実行順の検査から除外: N 型 — <型名>(理由: …。宣言元: …)」。**宣言していない他のスクリプトは従来どおり Warning**。理由を空にした行・存在しない型名の行は設定画面に黄色の注意が出て、`Run All` に Warning(`DD-CAMEXEC-EXEMPT-INVALID`)が出て除外されない。何も宣言していなければ従来と同じ結果(Info も出ない)。設定画面の追加・削除・Undo が効く。自動テスト(`CameraExecutionOrderExemptionTests` / `ExternalContractCameraExemptionTests`)で確認済みなのは、解決のロジック・Validator への組み込み・外部アセンブリのダミー提供口の自動発見まで | □ 未(T-Drive 導入後に確認) |

## 16. Canvas の埋め込み(入れ子)対応の確認(2026-10-03 追記)

U-28（[39](39_usability_fixes_2026-09-17.md) 2026-10-03 追記・[07_canvas_prefab.md](07_canvas_prefab.md) A-4）。自動テストで確認済み: 親を Open すると子の ElementFx が子ルート基準で効く（Appear → Idle、Close で Disappear・入力ゲート）/ 子のボタン・スライダー配線（SendSignal の ElementPath が子のルート基準で EmbeddedRootPath が埋め込み位置、CloseSelf は親を閉じる、SetOption）/ 親の行が子の行に勝つ / 入れ子の入れ子 / 循環・未解決・パス不一致で警告 + 継続 / 子の単独 Open・`EmbeddedCanvases` 空は従来どおり / プール再利用で配線・状態が残らない（PlayMode `EmbeddedCanvasTests` 15 件）、パス変換・Validator 8 コード・埋め込み候補の検出・登録・自動収集の除外・グループ構築・持ち主の解決・Canvas ルートの検出（EditMode `EmbeddedCanvasPathsTests` / `CanvasEmbeddedEditingTests` 26 件）。**目視でしか確認できないのは、SceneView での再生・Hierarchy / プレハブステージでの選択に追従・ウィンドウの見た目**。

準備: ブランチを切ってから行う（作ったアセット・Prefab は確認後に削除してよい）。Hud の中に Option を入れる例を一から作る。

1. **Option**: Prefab `Option_Test`（ルート RectTransform の下に `Panel`（Image）と `BtnX`（Image + `UiButton`、`DoubleClickSec` = 0））。Canvas データ `CANVAS_OptionTest`（Asset Browser の「＋ 新規作成」）を作り、`Prefab` に設定、Flags の Load = Preload。Inspector で ButtonWire を 1 つ（`ButtonPath` = `BtnX`、Trigger = Click、Action = SendSignal、SignalKey = `option/apply`）
2. **Hud**: Prefab `Hud_Test`（ルート RectTransform + Canvas の下に `Title`（Image））。**Project から `Option_Test` を `Hud_Test` の中へドラッグして入れ子にし、名前を `OptionRoot` に変える**。Canvas データ `CANVAS_HudTest`（`Prefab` に設定、Layer = HUD、Load = Preload）

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 16-1 | `CANVAS_HudTest` を Canvas Editor で開く（Inspector 最上部の「Canvas Editor で開く」） | 「埋め込み Canvas(入れ子の子 Canvas)」欄に「入れ子 Prefab から検出(未登録)」が出て、`OptionRoot = CANVAS_OptionTest` の行に「埋め込みとして登録」ボタンがある | □ 未 |
| 16-2 | 「埋め込みとして登録」を押す | 登録済みの行（RootPath `OptionRoot`・子 `CANVAS_OptionTest`・「この Canvas を編集」・「削除」）に変わり、検出の欄から消える。ステータスに登録した旨。Ctrl+Z で登録が戻る | □ 未 |
| 16-3 | 「要素を自動収集(Image / UiButton / パネル)」を押す | ElementFx 一覧が「CANVAS_HudTest の要素(…)」と「埋め込み: CANVAS_OptionTest(OptionRoot)」に分かれる。親の要素には `Title` だけが入り（`OptionRoot` 自身は Image などを持たないので集まらない）、`OptionRoot/Panel`・`OptionRoot/BtnX` は**入らない**（未登録の入れ子なら入る） | □ 未 |
| 16-4 | 「埋め込み: …」の「この Canvas を編集」を押す | 編集対象が `CANVAS_OptionTest` に切り替わり、ツールバーの下に「← CANVAS_HudTest へ戻る」と「埋め込みとして編集中: CANVAS_HudTest > CANVAS_OptionTest」が出る。Inspector・ElementFx 一覧・Validation も子のものになる | □ 未 |
| 16-5 | 子の「要素を自動収集」→ `Panel` の Appear に `PopIn`、`BtnX` の Idle に `Pulse` を割り当てる。続けて「← CANVAS_HudTest へ戻る」を押す | 親に戻る（戻る行が消える）。親の一覧の「埋め込み: …」欄に子の行が読み取りで並ぶ（`Panel   Appear: PopIn / Idle: なし / Disappear: なし`、`BtnX   … Idle: Pulse …`） | □ 未 |
| 16-6 | 親（Hud）で「確認用シーンを開く」を押す | 専用シーンで Hud が開き、**子の Panel が PopIn で現れ、BtnX が Pulse し続ける**（子の CanvasData の設定が親の中で効いている）。ステータスは「プレビュー表示中」 | □ 未 |
| 16-7 | 16-6 の状態で Hierarchy の `[D-Drive] UI Root` 配下の `…/OptionRoot/Panel` を選ぶ | **編集対象が自動で `CANVAS_OptionTest` に切り替わり**（「← 戻る」が出る）、ElementFx 一覧の `Panel` の行が展開されて青い縦線で強調され、そこまでスクロールする。続けて `…/Title` を選ぶと編集対象が `CANVAS_HudTest` に戻り、`Title` の行が強調される | □ 未 |
| 16-8 | 子（`CANVAS_OptionTest`）を編集対象にしたまま `Panel` の Appear 行の「▶ 再生」を押す（親の確認用プレビューが開いている状態） | 親の中の `OptionRoot/Panel` が PopIn で動く（Hierarchy 上の実体が選択される）。連打しても毎回同じ位置から始まる。「■ 停止」で元の状態に戻る | □ 未 |
| 16-9 | Project で `Hud_Test` をダブルクリックして**親のプレハブモード**にし、`CANVAS_OptionTest` を編集対象にして `Panel` の行の「▶ 再生」と「選択」を押す | 親のステージ内の `OptionRoot/Panel` が動く（プレビュー実体は置き直されず、プレハブモードのまま）。「選択」で Hierarchy の `OptionRoot/Panel` が選ばれる。再生後にプレハブモードのタイトルに `*` が付かない（値が元に戻る）。**親の Prefab に勝手に戻されない** | □ 未 |
| 16-10 | `Option_Test` を単独でダブルクリックして**子のプレハブモード**にし、`CANVAS_OptionTest` を編集対象にして `Panel` の「▶ 再生」・「選択して移動(Prefab を開く)」を押す | 子のステージ内の `Panel` が動く / 選択される。「選択して移動」が親の Prefab に切り替えない（子のステージのまま） | □ 未 |
| 16-11 | 「選択に追従」のチェックを外し、Hierarchy で別の要素（Panel ⇔ Title）を選ぶ。チェックを入れ直し、今度は ElementFx の絞り込み欄に文字を入力している最中（カーソルが欄にある間）に Hierarchy で要素を選ぶ | チェック OFF のあいだは編集対象が切り替わらない。ON でも、このウィンドウの入力欄にフォーカスがあるあいだは切り替わらず入力中の文字を失わない（フォーカスを外してから選ぶと切り替わる）。行の「選択」「▶ 再生」で自分が選んだ要素には反応して切り替わらない。**入力途中の欄からフォーカスを外さずに別の場所（Hierarchy）を選んだ場合も、入力した値は元の Canvas に確定し、切り替わった先の別の Canvas には書かれない**（→ 16-24） | □ 未 |
| 16-12 | 絞り込み欄に `Btn` と入れる | 親・子（読み取り表示）とも `Btn` を含む行だけが残る。空に戻すと全部出る | □ 未 |
| 16-13 | `CANVAS_HudTest` の Inspector の ElementEffects に要素を 1 つ足し、`ElementPath` = `OptionRoot/Panel`、`AppearPreset` = `FadeIn` にする（親での上書き） | 親の一覧に「↳ 親での上書き: CANVAS_OptionTest(OptionRoot)」と `[親での上書き] OptionRoot/Panel` の行が出る。子の欄の `Panel` の行に `[親で上書き]` が付く。Validation に Info「親に 'OptionRoot/Panel' の行があるため、…親の設定が優先されます」。「確認用シーンを開く」をやり直すと Panel が **PopIn ではなく FadeIn** で現れる（親の行が勝つ）。この行の「選択」を押しても編集対象は子に切り替わらない | □ 未 |
| 16-14 | プリセットギャラリー（`Tools > D-Drive > Editors > UI Tween · Preset Gallery`）を開き、確認用プレビュー（16-6 の状態）の `…/OptionRoot/Panel` を Hierarchy で選んで「選択中のシーン要素のパスを使う」を押す。続けて `…/Title` でも | `OptionRoot/Panel` の場合: 「適用先」の CanvasData が `CANVAS_OptionTest` に切り替わり、要素パスは **`Panel`**（`HUD/Hud_Test(Clone)/…` ではない）。ステータスに「埋め込み Canvas の要素のため…」。`Title` の場合: CanvasData は `CANVAS_HudTest` のまま、パスは `Title` | □ 未 |
| 16-15 | Canvas Editor の埋め込み行の RootPath を `NoSuch` に変えて Enter。次に RootPath を戻し、子を `CANVAS_HudTest`（自分自身）に変える。次に Option とは別の Canvas データ（無関係な Prefab のもの）に変える | それぞれ Validation に Warning（RootPath が Prefab 内で見つかりません / 自分自身 / この場所の実体が、子 Canvas の Prefab のインスタンスではありません）。いずれも Error ではない。元に戻すと消える | □ 未 |
| 16-16 | （任意・Play Mode）空のシーンで `Ui.Open(CANVASID.Hud_Test)` を呼ぶ小さな MonoBehaviour を作って再生し、`Ui.OnSignal("option/apply", …)` を購読して `OptionRoot/BtnX` をクリックする | Hud を開くだけで Panel が PopIn・BtnX が Pulse し、クリックで `option/apply` が届く（`args.Canvas` は Hud のハンドル、`args.ElementPath` は**子のルート基準の `BtnX`**〔`Option_Test` を単独で開いたときと同じ値〕、`args.EmbeddedRootPath` は `OptionRoot`）。子を別に Open していない。自動テストで確認済みのため、実機・実シーンでの通し確認として任意 | □ 未 |
| 16-17 | Canvas Editor を幅 500px 前後に縮め、埋め込み欄・グループ表示・戻る行を見る | 見切れず、縦にスクロールできる（RootPath / 子の欄は折り返す）。ObjectField の欄が極端に潰れない | □ 未 |
| 16-18 | **重なる埋め込み（2026-10-03 追加）**: Option に `Inner`（別の子 Canvas `CANVAS_VolumeTest`）を埋め込んでおき（Option の Canvas Editor で登録）、Hud の Canvas Editor を開く | 「入れ子 Prefab から検出(未登録)」に `OptionRoot/Inner` は**出ない**（入れ子の入れ子は、その子の Canvas Editor で登録するもの）。それでも手で「+ 手動で追加」して `OptionRoot/Inner` を足すと Validation に Warning（`DD-CANVAS-EMBED-NESTED-ROOT`: 埋め込みが重なっています） | □ 未 |
| 16-19 | 16-18 で重ねた状態のまま Hud を開き（確認用シーンまたは Play Mode）、`OptionRoot/Inner` 配下のボタン（`SendSignal` を配線）を 1 回クリックする | シグナルは **1 回だけ**届く（2 回届かない）。`args.EmbeddedRootPath` は `OptionRoot/Inner`（内側の登録が担当）。ElementFx も同じ要素に 2 本重ならない（見た目が二重にならない）。自動テスト（`EmbeddedCanvasTests.OverlappingEmbedRegistrations_*`）で確認済みで、ここは通し確認 | □ 未 |
| 16-20 | **トリガー単位の優先**: Option の `BtnX` に `Click` と `LongPress` の ButtonWire を持たせ、Hud の Buttons に `OptionRoot/BtnX` の `Click` だけを書いて Hud を開く。クリックと長押しをする | クリックは Hud の配線だけが動き、長押しは Option の `LongPress` の配線が動く（親の `Click` が子の `LongPress` を消さない）。Validation の Info は「親に 'OptionRoot/BtnX' の Click 配線があるため、… 同じ要素・同じトリガーの配線は使われません(別のトリガーの配線は子の設定が使われます)」の 1 件だけ。ElementFx は要素単位のまま（親に `OptionRoot/Panel` の行があれば Panel の Appear / Idle / Disappear は全部親の行） | □ 未 |
| 16-21 | **シグナルのパス**: 16-16 に加えて、`Option_Test` を単独で `Ui.Open` して同じボタンをクリックする | 単独でも埋め込みでも `args.ElementPath` は `BtnX`（同じ書き方で受けられる）。単独のとき `args.EmbeddedRootPath` は空、埋め込みのときは `OptionRoot`。入れ子の入れ子（Hud → Option → Volume）の Volume のボタンは `ElementPath` が Volume のルート基準、`EmbeddedRootPath` が `OptionRoot/Inner`（最外のルートからの連結） | □ 未 |
| 16-22 | **子が Preload でない**: `CANVAS_OptionTest` の Load を LazyLoad に戻し、`CANVAS_HudTest` の Canvas Editor の Validation を見る | Warning `DD-CANVAS-EMBED-NOT-PRELOAD`（子 Canvas の Load が Preload ではありません…）。Preload に戻すと消える | □ 未 |
| 16-23 | **RootPath の書式**: 埋め込み行の RootPath 欄に `OptionRoot/`、`/OptionRoot`、`Group\OptionRoot` を順に入力して Enter | 確定時に自動で正規化され（先頭末尾の `/` が除かれ、`\` が `/` になる）、Warning は出ない（Inspector から直接 `//` などを書いたときは `DD-CANVAS-EMBED-PATH-FORM` の Warning） | □ 未 |
| 16-24 | **入力途中で別の Canvas へ切り替わる**: Hud の Canvas Editor で、埋め込み行の RootPath 欄（または ElementFx の欄）に文字を入力したまま Enter を押さず、Hierarchy で Option の中の要素（別の Canvas に属する要素）をクリックする | 入力した値は**元の Canvas（Hud）の同じ行**に確定する（Ctrl+Z で戻せる）。切り替わった先（Option）の同じ番号の行には書かれず、入力した値が消えもしない。再現できた / できなかったを結果欄に書く（UI Toolkit のイベント順に依存するため。再現できなくても、コード側は各欄が作ったときの対象に書く形になっている） | □ 未 |

| 16-25 | **優先の 2 つの規則（2026-10-04 追加、修正ラウンド 3、FX-R-10）**: 16-18 で重ねた状態で、Option 自身の行（`Inner/Deep` の ElementFx）と Volume の行（`Deep`）が同じ要素を指すようにして Hud を開く。続けて、重なる登録をやめて Option 自身が `Inner` に Volume を埋め込む形に直し、同じ確認をする | 重なる登録（Warning）のときは、より内側の登録（Volume）の行が `OptionRoot/Inner` 配下の要素を担当する。正しい形（Option が Volume を埋め込む）に直すと、外側（Option）の行が勝つ（Volume の行は Info `DD-CANVAS-EMBED-OVERRIDE`）。Hud 自身の行はどちらの形でも子の設定に勝つ。[07] の優先の表（A / B）のとおり | □ 未 |

後片付け: `git status` で作ったアセット・Prefab を確認し、不要なら削除する（`ProjectSettings/` や `Assets/AddressableAssetsData/` に改行だけの差分が出たら `git checkout -- <path>`）。

## 17. M-4 禁止 API の許可の確認(2026-10-05 追記)

M-4（[11_tasks.md] M-4 節・[42_distribution.md] §5.9）。自動テストで確認済み（`ForbiddenApiAllowanceTests` 23 件）: 同じ行 / 直前行の許可・別の規則名では許可されない・理由なし / 空の括弧 / 不明な規則名は無効で分かるメッセージ・未使用は Info・全角括弧と大文字小文字・理由に括弧を含む・1 行に 2 規則・直前行コメントが 2 行先と別コメント行越しには効かない・文字列リテラル内と `/* */` は無視・設定の許可リスト（フォルダ / 1 ファイル / 規則指定 / 理由なし・不明な規則名は無効）・許可が無いときの結果が従来と同じ・許可件数の集計・D-Drive 自身のソースに誤認が無いこと。ここでは **実際の Unity と CI の経路で、人の目で見て確認する部分**だけを書く。

準備: ブランチを切ってから行う（`Assets/` に確認用スクリプトと、`ProjectSettings/DDriveProjectSettings.asset` が変わる）。持ち込み先の形に揃えるため、`DDriveProjectSettings.IsDevelopmentRepo` が false のプロジェクト（空プロジェクト / MS2026）で行うのが望ましい（このリポジトリでは CI の走査ルートが `Packages/com.ddrive.core` になり、`Assets/` の確認用スクリプトは走査されない）。`Assets/Scratch/AllowTest.cs` を作り、次の内容にする。

```csharp
using UnityEngine;
public class AllowTest : MonoBehaviour
{
    void Update()
    {
        var a = Time.unscaledTime;                                   // 行 7: 許可なし
        var b = Time.unscaledTime; // ddrive-allow: Time(実時間で測る)  // 行 8: 理由つき
        var c = Time.unscaledTime; // ddrive-allow: Time               // 行 9: 理由なし
        // ddrive-allow: Instantiate(NGO の NetworkObject は Instantiate → Spawn が正規手順)
        var d = Instantiate(gameObject);                             // 行 11: 直前行の許可
        var e = 0; // ddrive-allow: Time(この行には当たりが無い)         // 行 12: 未使用
        var f = Time.unscaledTime; // ddrive-allow: Tiem(綴り違い)     // 行 13: 不明な規則名
    }
}
```

実行は `Unity -batchmode -nographics -projectPath <プロジェクト> -executeMethod DDrive.Editor.CI.ValidateAll`。結果は `TestResults/ddrive-validation.junit.xml` とログで見る（Editor を開いたままでも、`Tools > D-Drive > Validation > Forbidden API 許可一覧` で許可の一覧・無効な許可の Warning・当たりの件数は見られる）。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 17-1 | 上のスクリプトで `Tools > D-Drive > Validation > 禁止 API の検査`（ウィンドウ。2026-10-05 修正ラウンド 5 で追加。再走査ボタンあり）を開く（CI では `CI.ValidateAll` のログ / Console の `[DDrive][ForbiddenApi]` の行。バッチは Editor を閉じて実行する） | **Error（禁止 API）になるのは行 7（許可なし）と行 9（理由なし。メッセージの末尾に「許可コメントに理由が必要です」の案内が付く）と行 13（綴り違いの許可は無効）の 3 件**。行 8（理由つき）と行 11（直前行の許可）は Error にならない | □ 未 |
| 17-2 | 同じログの Warning / Info を見る | Warning: 行 13 に「規則名 'Tiem' は存在しません(使える規則名: …)」（`DD-FORBIDDEN-ALLOW-UNKNOWN-RULE`）。Info: 行 12 に「使われていない許可コメントです(Time)」（`DD-FORBIDDEN-ALLOW-UNUSED`）、「禁止 API の許可: 2 件(コメント 2、設定 0)」（`DD-FORBIDDEN-ALLOW-SUMMARY`） | □ 未 |
| 17-3 | 行 9 を `// ddrive-allow: Time(実時間で測る)` に直し、行 13 の `Tiem` を `Time` に、行 12 のコメントを消して、もう一度実行する | 行 7 だけが Error。Warning と未使用の Info は消え、許可は 4 件（コメント 4、設定 0）。行 7 も許可すると Error が 0 になり終了コードが 0 | □ 未 |
| 17-4 | 行 11 の直前行の許可コメントと `var d = …` の間に空行（または別のコメント行）を入れて実行する | 行 11 が Error に戻る（許可は次の 1 行だけに効く）。空行を消すと許可される | □ 未 |
| 17-5 | `Project Settings > D-Drive > 禁止 API の除外` を開く（**最初に、欄が灰色でなく編集できること**を見る。2026-10-05: hideFlags が編集不可でないこと・SerializedObject での追加 / 反映 / 保存 / Undo・Redo は自動で確認済みだが、画面の見た目は目視）。要素を 1 つ足し、パス = `Assets/Scratch/`、規則名 = 空、理由 = 空 のまま保存する。続けて Undo（Ctrl+Z）で要素が消え、Redo で戻ること、Editor を開き直しても保存した要素が残ることを見る | 「除外の一覧」が表示され、要素の各欄にツールチップ（パス = ファイルと完全一致またはフォルダの配下に一致〔文字列の前方一致ではない〕、規則名 = 空欄で全規則、理由 = 必須）が出る。理由が空の要素の下に Warning の枠（理由が空です）。`Validation > Run All` に `DD-FORBIDDEN-ALLOW-SETTINGS-INVALID` の Warning が出る（Data が 1 件以上あるとき）。`CI.ValidateAll` を実行しても行 7 は Error のまま（無効な要素は効かない） | □ 未 |
| 17-6 | 理由を `確認用` と入れる（規則名は空のまま）。`CI.ValidateAll` を実行する | `Assets/Scratch/` の当たりがすべて許可に変わり、Error が 0（許可件数の Info の「設定 n」が増える）。Warning の枠と Run All の Warning が消える。規則名を `Instantiate` にすると `Time` の当たりは Error に戻り、`Instantiate` の行だけ許可される。規則名に綴り違いを入れると無効の Warning が出る | □ 未 |
| 17-7 | `禁止 API の検査` ウィンドウの「許可済み」と、`Tools > D-Drive > Validation > Forbidden API 許可一覧`（Console 出力）を見る。ウィンドウの各行のボタン（`ファイル:行`）を押す | ウィンドウの「許可済み」に許可した箇所ごとの 1 行（ファイル:行・規則名・コメント / 設定・理由）、「許可されていない当たり」に未許可の当たり（ファイル:行・規則名・該当行）、「無効・未使用の許可」に Warning / Info。ボタンで外部エディタが該当ファイルの該当行で開く。Console に「禁止 API の許可: N 件(コメント n、設定 m)」と、許可した箇所ごとの 1 行（`[comment]` / `[settings]`・ファイル:行・規則名・理由）が出る。Warning（無効な許可）があれば続けて出る | □ 未 |
| 17-8 | `DDriveProjectSettings` の旧い設定ファイル（この機能の前に作った `ProjectSettings/DDriveProjectSettings.asset`）のまま Editor を開く | 他の設定（GameData ルート等）がそのまま読め、除外の一覧が空で表示される | □ 未 |
| 17-9 | **MS2026 で v1.4.0 に更新した後の 29 件の仕分け**: MS2026 で `Tools > D-Drive > Update > 更新ウィンドウ` から v1.4.0 に更新し、`CI.ValidateAll` を実行する。**`Tools > D-Drive > Validation > 禁止 API の検査` ウィンドウ**（Editor を開いたまま使える）で 29 件を 1 件ずつ、[11_tasks.md] M-4 節の「MS2026 へ返す文面」の判断基準で仕分ける（ゲームプレイの `Time` → `GameLoop` に登録した `IAssetManager` の `Tick(dt)`、または 1 か所の時間源 + 許可コメント〔`ITimeSource` ではない〕 / 実時間の計測 → `Time.realtimeSinceStartupAsDouble`・`Stopwatch` に替える（許可不要）か許可コメント / NGO の `NetworkObject` の Instantiate → 許可コメント / それ以外の `Instantiate` → `Prefabs.Spawn`・プール / 外部コード → 設定）。直したら「再走査」で当たりが消えることを確認する | 直したものは当たりが消え、許可したものは Error から Info の件数に移る。最終的に禁止 API の Error が 0 になる。許可コメントの理由が具体的で、レビューで妥当と言える（`Forbidden API 許可一覧` で一覧して確認）。24 件の `Flags.Load が Preload ではありません` は別に、`Validation > Run All` の自動修正で直す | □ 未 |

後片付け: `Assets/Scratch/` を削除し、`git status` で `ProjectSettings/DDriveProjectSettings.asset` を確認する（除外の要素を足した差分は `git checkout -- ProjectSettings/DDriveProjectSettings.asset` で戻す。改行だけの差分も同様）。
