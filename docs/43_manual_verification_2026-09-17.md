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
| 12 | **U-2 FBX のマテリアルスロット** — FBX を入れ直して `ModelData` の Material スロットが埋まるか。既存モデルは Model Editor の「元ファイル再読み込み」で埋まるか |
| 13 | **U-4 / U-5 / U-6 配置ボタン** — Prefab Editor が確認用シーンに置くか / 全エディタで右クリック → 2 メニューが出るか / 配置後に SceneView がフォーカスするか / 「このシーンに本配置」がシーン保存後も残るか（一時配置の掃除に巻き込まれないこと） |
| 14 | **U-16〜U-19 メニュー** — `Assets/D-Drive/Data を作成/`（12 種、対象外のアセットで灰色になるか） / `GameObject/D-Drive/`（7 項目、Undo が効くか） / `Tools > D-Drive > Generate > Canvas + Panel と CanvasData を作成` |
| 15 | **U-22 / U-24 Anchor** — SceneView に基準の 3 軸とワールド座標ラベルが出るか / Anchor Group の「手置きの点に変換」で点の位置と番号が変わらないか |
| 16 | **U-9 / U-10 / U-12 / U-13 / U-14 / U-15** — PresetGallery ボタン / 「SE も鳴らす」の見切れ / プレビューバー / 検証セクション / Fade 欄 / 仕様書 URL |
| 17 | **P1-2 の安全弁（意図的に壊して確認する）** — トークンを空にして「取得」→「適用」。TuningTable が消えず、画面に理由が出ること。修正前はここで TUNING が空生成されコンパイル不能になっていた |
| 18 | **P1-1 Pool** — `PrefabData` の `Flags.Pool` を `Kind = Pooled` / `MaxCount = 1` にして Spawn → Spawn → 1 つ目を Despawn。2 つ目が消えないこと |

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
| Presentation に Anchor 上書きが無い | `Audio.PlaySe(id, anchor)` / `Vfx.Spawn(id, anchor)` にはあるコード側からの Anchor 上書きが、Presentation のトラックには無い（`PresentationManager` に `AnchorId` が 1 件もヒットしない）。意図的な仕様か漏れかは未判断 |
| 種別固有フィールドの編集可否 | U-11 は `AssetDataBase` 共通 4 フィールドのみ対象と決定済み（2026-09-17）。将来変えるなら `[InspectorReadOnly]` を足すだけでよい |
| Toolbar の折り返し | U-27 で「Unity の制約で折り返せない」は**誤りだと判明**（`flexWrap` + `height: StyleKeyword.Auto` で可能）。現状溢れている Toolbar は無いため未適用。手法は [09 §7.1.2](09_editor_tools.md) |
| テストの穴 13 件 | [41](41_phase6_review_2026-09-17.md)。ネット系 5 件（偽造 Result / 保留のフラッシュ / タイムアウト時のイベント / 保留経由の偽造 / Cancel のレート制限）は実バグが 5 回出ている領域なので優先度が高い。**PC 2 台の実機確認が要る** |
| P2-6 の本筋 | Validation の「全体結果」を Asset 無しの別経路にする（`ValidatorRegistry` = Foundation を触るため範囲外にしていた） |
| 移植（P チケット） | [42](42_distribution.md)。A-1〜A-9 は 2026-09-17 に確定済み。**A-6（`KnownPrefixes` の不整合）は P-5（1.0.0）より前が最後の修正機会** |
