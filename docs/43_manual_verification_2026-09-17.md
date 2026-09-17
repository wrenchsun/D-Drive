# 43. 人による確認手順（2026-09-17、U チケット完了時点）

関連: [39_usability_fixes_2026-09-17.md](39_usability_fixes_2026-09-17.md)（U チケット本体） / [42_distribution.md](42_distribution.md)（移植・P チケット） / [36_manual_screenshot_list.md](36_manual_screenshot_list.md)（撮影リスト） / [41_phase6_review_2026-09-17.md](41_phase6_review_2026-09-17.md)（レビューの残り） / [33_ci_setup.md](33_ci_setup.md)

> この書面は 2026-09-17 の作業（U-7 / U-8 / U-11 / U-20 / U-21 / U-23 / U-25 / U-27、プログラマーマニュアル新設、
> SpecWeb 配信対応、移植方針 A-1〜A-9 の確定）に対する**人の確認手順**をまとめたもの。
> **上から順に実施できる並びにしてある**（`clasp push` の前後で節を分けている）。
> コード変更はすべてコンパイル・テスト green まで確認済みだが、**画面の見た目と操作は未確認**。

## 0. 前提の確認（最初に 1 回）

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
