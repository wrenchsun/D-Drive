# 1020. 専用エディター土台（`UxEditorWindow`）の軽量化 — MS2026 PERF-2 の本修正

> 起票 2026-10-31。チケットは [1004](1004_tasks.md) EP 節（EP-1〜EP-4、v1.12.0 MINOR 見込み）。
> 出典: MS2026 `Docs/Notes/2026-10/2026-10-25_wrenchsun_floor-fx-editor-weight.md`・`Docs/Records/Perf/2026-10-25_2255_FloorFxEditor.md`・`Docs/Spec/tasks.md` PERF-2 / KI-048 / KI-049。

## 1. 経緯

MS2026 の Floor Fx / Show / Emitter / Loop Editor（`UxEditorWindow<TData>` 派生、1 層 73 行・3 層 219 行）が重い（山口 2026-10-25）。MS2026 側で直せる分（色の行の毎回の映し直し・引き表の期限切れ・Show の検査の `FindAssets`）は 2026-10-25〜26 に済み。**残りは D-Drive の土台の仕事**で、MS2026 は `FloorRowThrottle`（土台の private `_rows` をリフレクションで一時的に空にして仕事を飛ばす回避策。KI-048）で凌いでいる。D-Drive 側で本修正し、タグを上げたら MS2026 は `FloorRowThrottle` と各窓の `_throttle` を外す。

MS2026 の実測（ComboTwist 219 行、Debug の Editor）:

| 土台の仕事 | 重さ | 起きる頻度 |
|---|---|---|
| `Tick()` の全行 `UpdateState()` + `Sync()`（1 行 約 0.04 ms。深いパスの `FindProperty` ×2 が大半） | 約 5 ms | 150 ms ごと、Data が変わっていなくても |
| 欄 1 回の編集 → `OnDataEdited()` = `RunValidation()` + `RefreshAll(true)` | 約 6〜10 ms | 編集ごと。ドラッグ中は毎マウス移動（20 回で 約 290 ms） |
| 窓を開く・タブ切替・フォーカス移動で UI Toolkit の結びつけが出す `SerializedPropertyChangeEvent`（1 回で 186〜288 件）を「編集」として `OnPropertyEdited` / `OnDataEdited` へ流す | 件数分の上の仕事 + 種別側の副作用（MS2026 は床のプレビュー起動 → 計算シェーダーのコンパイルで 63 秒止まった。KI-049） | 窓の表示・切替ごと |
| `Tick` の間隔 150 ms が `schedule.Execute(Tick).Every(150)` で固定 | — | 種別側から変えられない |

## 2. 直し方（土台 `Editor/Ux/UxEditorWindow*.cs` のみ。公開 API は追加のみ）

### EP-1 変化の無い Tick は行ループを飛ばす

- `Tick()` で、`EditorUtility.GetDirtyCount(Target)`・`_mode`・`_query`・`_modifiedOnly` が前回の行ループ時と同じなら、全行の `UpdateState()` / `Sync()` と署名の計算を飛ばす（`SyncFooter()`・`So.Update()`・`TickRepeat` / `AutoSaveTick` / `OnTick()` は今までどおり毎回）。
- **7 tick に 1 回（`OnSlowTick` と同じ拍）は必ず全行を通す**（Inspector 以外の外からの変化・参照切れ・シーン依存の `Sync` の取り込みのため）。
- 対象の切替・`RebuildBody`・Undo / Redo のあとは基準を捨て、次の Tick は全行を通す。
- EP-3 の「まとめ待ち」の間は行ループを飛ばす（止まったあとの 1 回に任せる）。
- 隠れた行だけ飛ばす最適化は入れない（`IsModified` の件数・印が古くなる。MS2026 も不採用）。行の遅延生成も今回は対象外。

### EP-2 結びつけの偽通知を捨てる

- `OnPropertyChanged(evt)` で `EditorUtility.GetDirtyCount(Target)` が前回の実編集時と同じなら **実際には Data が変わっていない通知**として `OnPropertyEdited` / `OnDataEdited` を呼ばない。
- 基準（`_lastEditDirty`）は `BuildBody` の末尾・`SetTarget`・`OnUndoRedo`（`OnSideEffect` のあと）で現在値に合わせる。`RowChanged` / `VisualEdited` / 一括リセットなど土台が自分で書いた経路でも合わせる（次の結びつけ通知を偽と判定できるように）。
- `ApplyModifiedProperties` は値が変わらなければ dirty を進めないので、同じ値の再入力は編集扱いにならない（今までは通知が来れば編集扱いだった。意図した変更）。

### EP-3 連続編集の検査 + 全行の更新をまとめる

- `OnDataEdited()`: 前回の実編集から **0.3 秒**より空いていればすぐ `RunValidation()` + `RefreshAll(true)`（単発の編集は今までどおり即反映）。0.3 秒以内に続いた 2 回目以降は保留にし、最後の編集から **200 ms** 後に 1 回だけ行う（`rootVisualElement.schedule.Execute(...)` を 1 本持ち `ExecuteLater(200)` で延ばす）。
- `OnPropertyEdited(path)` は実編集ごとに今までどおり毎回呼ぶ（種別の「編集中は再生を撮り直す」等の契約を変えない。まとめるのは土台の後始末だけ）。
- 保留は `RebuildBody` / `SetTarget` / `OnUndoRedo` / `RowChanged` / `RefreshAfterExternalWrite` / `OnDisable` で捨てる（それぞれ自分で全行を更新する、または窓が消える）。
- 定数は `UxEditorWindow` の `private const`（`EditBurstGapSec = 0.3`、`EditFlushDelayMs = 200`）。種別から変える口は今回は作らない。

### EP-4 Tick の間隔を種別から変えられる

- `protected virtual int TickIntervalMs => 150;` を追加し、`CreateGUI` の `Every(...)` に使う（50 未満は 50 に丸める）。`OnSlowTick` は今までどおり 7 tick ごと（間隔を変えると拍も変わる。コメントに明記）。
- 公開 API（`editor-contract.txt` の `== UxPublicApi ==`）への追加なので `Tools > D-Drive > Compat > スナップショットを更新`（または `ddrive_compat_update confirm:true`）+ CHANGELOG の互換性節に「追加のみ（MINOR）」。

### 計測の口（テスト・MS2026 の実測用）

- `UxTestHooks`（internal static）に `SkippedTicks` / `SpuriousEdits` / `CoalescedEdits`（int、累計）を足す。テストと持ち込み先の実測道具はリフレクションで読む（`UxTestWindows` が `SuppressDialogs` を読むのと同じ形）。

## 3. 受け入れ条件

- EditMode テスト（新規 `Tests/Editor/UxEditorPerfTests.cs`。既存の UX テストと同じ衛生 = `UxTestWindows.CloseAll` / `RequiresGraphics`）:
  1. 変化の無い連続 Tick で 2 回目以降が飛ぶ（`SkippedTicks` が増える）、7 回目は飛ばない
  2. `So` 経由で値を書いて `ApplyModifiedProperties` したあとの Tick は飛ばない（`IsModified` / 変更チップが更新される）
  3. dirty が進んでいない `SerializedPropertyChangeEvent` は `SpuriousEdits` が増え、`OnPropertyEdited` が呼ばれない。dirty が進んでいれば呼ばれる
  4. 0.3 秒以内の 2 回目の実編集は `CoalescedEdits` が増え、保留の実行後に検証・行の状態が最新になる（スケジューラーが回らない環境では internal の `FlushPendingEdits()` を呼んで確認してよい）
  5. `TickIntervalMs` を上書きした派生窓で `Every` の間隔が変わる（反射で確認できなければ、50 未満の丸めだけ単体で）
- 既存の EditMode 全件 green（`*UxTests` はクラス単位・ウィンドウ数が増えないこと。[09](09_editor_tools.md) §16）。Editor 専用の変更なので PlayMode は省略可（[12 §7](12_review.md)、2026-10-10 指示）。
- `editor-contract.txt` の差分は `TickIntervalMs` の追加だけ。
- docs: [1010](1010_editor_ux_migration.md) §2 に「定期更新の省略・編集のまとめ・偽通知の破棄」を追記、[09](09_editor_tools.md) §16 の土台の説明にひと言、`CHANGELOG.md` `[Unreleased]`。

## 4. MS2026 側の後始末（D-Drive v1.12.0 を取り込んだら）

- `Assets/_Project/DDriveExtensions/Floor/Editor/Common/FloorRowThrottle.cs` と Fx / Show / Emitter / Loop の `_throttle` の行を消す（KI-048 の手順）。`FloorFxEditorPerf` の回避策あり / なしの比較は土台の `UxTestHooks` の計数に置き換えるか消す。
- `OnPropertyEdited` / `OnSideEffect` に入れていた `IsRealEdit` の判定は土台が済ませるので外せる（KI-049）。

## 5. 今回やらないもの（MS2026 `tasks.md` 118 行目の①〜⑤ と KI-050。軽量化ではないので別起票）

① `ux-ikcurve` の `CurveField` の既定の高さ ② ヒーローにチップを足す正式な口 ③ 固定バーの ▶ の文言を変える口 ④ `IPreviewSceneProvider` の「開く前」の口 ⑤ 個別検証でも別種別を参照する Validator の参照先が文脈に載ること（KI-046） ⑥ 幅 400 px 未満での縦積み（KI-050）。[1004](1004_tasks.md) §2 の候補に 1 行で置いた。
