# 65. 2026-10-06 自前レビュー結果（PR #133 = 埋め込んだ子 Canvas の有効 / 無効）

> **対象**: 未マージの PR #133（`origin/feat/embedded-canvas-active`、`fc9e00a`。Draft）。別 PC のセッションが実装し、誰もレビューしていない。
>
> | 対象 | PR | 差分 | 内容 |
> |---|---|---|---|
> | 1 | #133（`29b19b7` 本体 + main の取り込み 2 回 + `fc9e00a` = 決定事項を docs に反映） | `git diff origin/main...origin/feat/embedded-canvas-active`（18 ファイル、+1171 / -9） | `EmbeddedCanvas.StartInactive`・`ButtonWire.EmbeddedRootPath`・`UiAction.ActivateEmbedded` / `DeactivateEmbedded` / `ToggleEmbedded`（7〜9）・`Ui.SetEmbeddedActive` / `IsEmbeddedActive`（`UiManager` に同名）・Canvas Editor の「無効で始める」と「表示 / 非表示(作業用)」・配線の対象の欄・`CanvasEmbeddedActiveValidator`（Warning 2 種）・テスト PlayMode 8 + EditMode 3 |
>
> **方法**: 専用 worktree で `git fetch` し、`git diff` と変更後のファイルを**読むだけ**。`UiManager` は差分のほか、照合のために Open（`OpenData`）・Close（`Close` / `TryFinalizeClose` / `FinalizeClose`）・入力ゲート（`RecomputeBlocking` / `PendingAppearCount`）・`TickElementFx` / `StartDisappear` / `StartAllDisappearFx`・埋め込みの適用（`SetupEmbeddedCanvases` / `ProcessEmbedLevel`）を読んだ。`fc9e00a`（決定事項の反映）は docs / CHANGELOG の 3 ファイルだけで、**コードの差分は無い**ことを `git diff 772f325 fc9e00a --stat` で確かめた。**Unity は起動しておらず、コンパイル・EditMode / PlayMode テストは一切実行していない**。
>
> 前提として読んだもの: `CLAUDE.md` §0、[docs/12](12_review.md) §3、[docs/42](42_distribution.md) §5、[docs/07](07_canvas_prefab.md)（埋め込み・ButtonWire・A-4 の追記）、[62](62_review_verification_fixes_u29_n8_2026-10-06.md)・[63](63_review_pr126_pr129_2026-10-06.md)（書式・U-28 / U-29 の経緯）、[docs/43](43_manual_verification_2026-09-17.md) §16。

## 総評

- **P1 が 1 件ある（GG-R-01）。直すまでマージしない方がよい**。埋め込みの無効化が Open 直後の入力ゲートの数を減らすのに、ゲートの再計算（`RecomputeBlocking`）を呼ばない。「親を開いた直後にコードで子を隠す」だけで、**親が操作できないまま戻らない**ことがある。直しは数行。
- **P2 が 2 件**: 子の Disappear の途中で親を閉じると、数えていない Disappear の完了で閉じ待ちの数を減らし、**親の Disappear が終わる前にプールへ返す**（GG-R-02）。有効化のときの FirstSelected の選択が、上に開いているモーダルや入力ブロック中の親でも選択を奪う（GG-R-03。決定 3 の範囲内の手当て）。
- **互換**: 追加のみ。`UiAction` は末尾に 7〜9、`ButtonWire` / `EmbeddedCanvas` は末尾に 1 欄ずつ、`Ui` / `UiManager` に 2 メソッドずつ。スナップショットの差分は `enums.txt` +3 行・`public-api-DDrive.Runtime.txt` +12 行で、**すべて追加行**（削除・変更の行は 0）。`serialized-layout.txt` は `CanvasData` の直下の欄だけなので差分が無いのが正しい。ただし CHANGELOG の「互換性」節への追記が無い（GG-R-04）。
- **決定 4（データが勝つ）の互換上の影響は、リリース済みの持ち込み先には無い**。`CanvasData.EmbeddedCanvases` 自体が U-28 で `[Unreleased]`（v1.4.0、タグ未実施）なので、v1.3.1 以前のデータに登録済みの埋め込みは存在しない。影響するのは開発リポジトリ（と v1.4.0 前の main を参照している環境）のうち「登録済みの埋め込みのルートを Prefab 側で無効にしてある」データだけ（下の「決定事項の確認」）。
- **定常経路**: Tick で増えたのは `FinishEmbedDeactivations`（埋め込みの数のループ。`Deactivating` のときだけ内側のループ）と、`Held` / `DisappearStarted` の早期 `continue` だけ。LINQ・クロージャ・boxing・アロケーションは無い。Open では `List<EmbedState>` と埋め込みごとの `EmbedState`、要素ごとの `GetComponent<CanvasGroup>` が増えるが、既存の `EmbedNode` / `ElementFxRuntime` と同じ程度で許容範囲。
- **Editor**: 「無効で始める」は作ったときの `owner` に `Undo.RecordObject` + `SetDirty`。「表示 / 非表示(作業用)」は、確認用プレビューでは実 `UiManager.SetEmbeddedActive`（ADR-4 どおり実 Manager を駆動）、プレハブモードでは `SceneVisibilityManager` で、**データにも Prefab にも書かない**（コード上、`SetDirty` / `RecordObject` / `SetActive` を Prefab の実体に呼ぶ経路は無い）。`ChangeEmbedWithCleanup` は `StartInactive` を保つようになったが、`Register`（同じ RootPath の子の差し替え）は保たない（GG-R-05）。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **1** | GG-R-01 |
| P2（直すべき不具合・設計上の穴） | **2** | GG-R-02・GG-R-03 |
| P3（整理・改善） | **9** | GG-R-04〜GG-R-12 |

---

## マージしてよいかの判定

**GG-R-01 を直してからマージする**。GG-R-02・GG-R-03 も小さいので同じ PR で直すのを勧める（v1.4.0 のタグ前）。P3 はマージ後・v1.4.x でよいが、GG-R-04（CHANGELOG の互換性節）は互換性ポリシーの手続きなので**タグ前に必須**、GG-R-05 は 1 行なので入れられるなら入れる。

- 直した後に EditMode / PlayMode を全件実行する（PR 本文のとおり、`07a4fad` 取り込み後は関連 EditMode 83 件しか回していない）。GG-R-11 のテストを足す。
- 人による確認 16-43〜16-45（プレハブモードの作業用表示・配線からの切り替え・検査）は未。

## 観点別の確認

| 観点 | 結果 |
|---|---|
| 入力ゲート（親の入力を子の Appear 完了まで待つ） | 無効で始まる子の要素は `ApplyInitialEmbedStates` で `CountsForGate` から外し、Open の `RecomputeBlocking` より前に数を減らす ○。有効化の Appear はゲートに数えない（`CountsForGate` は Open の分だけ。`OnAppearCompleted` は `CountsForGate` を見る）○。**無効化の途中でゲートから外す経路が再計算を呼ばない = GG-R-01** |
| 入れ子（外側が無効の間） | `EmbedState.Parent` の連鎖で `IsEmbedShown` を判定し、外側が無効なら状態だけ覚える（`Active` を変え、GameObject は内側だけ有効 / 無効にする）○。外側を有効にすると `RestartEmbedFx` が「表示される」内側だけやり直す ○。外側の Disappear の途中で内側を無効にした場合の細部 = GG-R-08 |
| プール返却で Open 時に戻す | `FinalizeClose` → `RestoreEmbedObjects` が Open 時の `activeSelf` へ戻し、次の Open は `StartInactive` から決め直す ○（テストあり）。要素の見た目（Disappear の終端値）は既存どおり戻さない（既存の Close の Disappear と同じ扱い。`RestartEmbedFx` は Open 時に控えた値へ戻す）|
| Disappear 途中での再有効化 | `Active = true` / `Deactivating = false` → `RestartEmbedFx` が Disappear を止め、控えた見た目へ戻して Appear からやり直す ○（テストなし = GG-R-11）|
| Disappear 途中で親を閉じる | **GG-R-02** |
| 単独で開いた子 | `EmbeddedRootPath` 空 + 属する埋め込みなし → 警告 1 回 + no-op（`CloseSelf` に読み替えない）○（テストあり）|
| 未登録パス・無効なハンドル・閉じている途中 | 警告 1 回 + no-op / 何もしない / `false` ○（例外にしない = §0-4）。未登録パスの警告のキーは毎回 `"active:" + rootPath` を作る（GG-R-12 の細部）|
| 配線（親 / 子） | `ExecuteEmbeddedWire` は配線が属する埋め込みのパス（`embedRoot`）に `EmbeddedRootPath` を連結して Open のルート基準にする ○。親の配線から入れ子の入れ子を指す場合 = GG-R-07。SliderWire = GG-R-09 |
| Undo | 「無効で始める」: `Undo.RecordObject(owner)` + `SetDirty(owner)` ○。配線の対象の欄: 既存の `UpdateWire`（Undo 対応）○。`ChangeEmbedWithCleanup` は `StartInactive` を保つ ○（テストあり）。`Register` = GG-R-05 |
| 作業用の表示 / 非表示 | 保存しない ○（総評）。プレハブモードの `SceneVisibilityManager` はシーンの可視状態（Library 側）に書き、Prefab のファイルは変えない。ステージを閉じたときに戻るかは**推定**（GG-R-12）|
| 互換 | 総評のとおり追加のみ ○。enum の値は明示されていないが末尾追加で 7〜9（スナップショットで固定）○ |
| 新しい検査 | 2 種とも Warning（重さの互換に触れない）○。`validator-severity.txt` は代表の検査だけを集める既存の方針なので未登録で可 |
| docs | docs/07 A-4 の追記・docs/43 §16 の 16-41〜16-45・CHANGELOG「追加」✓。CHANGELOG「互換性」節 = GG-R-04。DesignerManual は未更新（機能の説明が要る。GG-R-12）|

---

## P1 — リリース前に必ず直す

### GG-R-01. Open の Appear の途中で埋め込みを無効にすると、入力ゲートの数は減るが再計算されず、親が操作できないまま戻らない

- **場所**: `Runtime/Canvas/UiManager.cs:1861-1916`（`StartEmbedDisappear` が `instance.PendingAppearCount` を減らす = 1880-1884）、`:1736`（`SetEmbeddedActive`。戻り値も再計算も無い）、`:725-749`（`RecomputeBlocking` = `PendingAppearCount > 0` の間 `interactable` / `blocksRaycasts` を false）、`:2423-2437`（Tick は `TickElementFx` の `gateChanged` のときだけ再計算。`gateChanged` は `OnAppearCompleted` が `CountsForGate && PendingAppearCount > 0` で減らしたときだけ true）
- **内容**: 例: 親 `Hud` の自前の要素には Appear が無く（または先に終わり）、`StartInactive = false` の子 `OptionRoot` の `Panel` に Appear がある。`var h = Ui.Open(HUD); Ui.SetEmbeddedActive(h, "OptionRoot", false);`（開いた直後に、状況に応じて子を隠す = この機能のよくある使い方）。Open の時点で `PendingAppearCount = 1` → `RecomputeBlocking` で親は入力不可。`SetEmbeddedActive(false)` → `StartEmbedDisappear` が `Panel` の `CountsForGate` を外して `PendingAppearCount = 0` にするが、`RecomputeBlocking` は呼ばれない。以後、`OnAppearCompleted` は数が 0 なので `changed = false`、Tick も再計算しない → **親の `CanvasGroup.interactable` / `blocksRaycasts` が false のまま**。別の Canvas の Open / Close（`RecomputeBlocking` を呼ぶ）があるまで戻らない。`IsOpening` は false を返すので、外からも気付きにくい。Canvas Editor の「表示 / 非表示(作業用)」を開いた直後に押しても同じ。
- **直し方**: `SetEmbeddedActive` の無効化の枝で、`StartEmbedDisappear` の前後で `PendingAppearCount` が変わったら `RecomputeBlocking()` を呼ぶ（`StartEmbedDisappear` が「ゲートが変わったか」を out で返す形でもよい）。`ApplyInitialEmbedStates` は Open の `RecomputeBlocking` より前なので不要。テスト: 上の手順で `GetComponent<CanvasGroup>(h).interactable` が数フレーム以内に true になること。
- **確度**: コード読みで確認（`RecomputeBlocking` の呼び出し元は `OpenData`・`FinalizeClose`・Tick の `gateChanged` の 3 か所だけ = grep）

## P2 — 直すべき不具合

### GG-R-02. 子の Disappear の途中（または終わった直後の Tick 前）に親を閉じると、数えていない Disappear の完了で閉じ待ちの数を減らし、親の Disappear が終わる前に閉じ切る

- **場所**: `Runtime/Canvas/UiManager.cs:2207-2222`（`StartDisappear` は `DisappearStarted` なら何もしない = `PendingDisappearCount` に数えない）、`:2111-2124`（閉じている間の Tick: `DisappearStarted && !DisappearDone && !IsTweenPlaying` で `PendingDisappearCount` を 1 減らす）、`:1861-1916`（`StartEmbedDisappear` は `DisappearStarted = true`・`DisappearDone = !playing`）、`:1919-1940`（`FinishEmbedDeactivations` は閉じている間は呼ばれない）
- **内容**: 子 `OptionRoot` を無効化 → 子の要素は `DisappearStarted = true`・`DisappearDone = false`（再生中）。その間に親を `Close` → `StartAllDisappearFx` が `PendingDisappearCount = 0` から数え直し、親の要素 A（Disappear 0.3 秒）で 1。子の要素は `DisappearStarted` なので数えない。子の Disappear（0.1 秒）が先に終わると、閉じている間の Tick が子の要素で `PendingDisappearCount` を 1 → 0 に減らし、`TryFinalizeClose` → **A の Disappear の途中で `FinalizeClose`**（`CloseAsync` が早く返る・プールへ返した実体に A の tween が書き続ける・次の Open で途中の値が見える）。Disappear が終わってから Tick の `FinishEmbedDeactivations` までの間に `Close` が来た場合も同じ（`DisappearDone` が false のまま tween は止まっている）。外側の Disappear 中に内側を無効化して外側を有効に戻した場合に残る `DisappearStarted` の要素（GG-R-08）も同じ経路に入る。
- **直し方**: どちらか。(a) `StartDisappear` で `r.DisappearStarted && !r.DisappearDone` のとき、`IsTweenPlaying(r.DisappearHandle)` なら `PendingDisappearCount++`（閉じ待ちに数え直す）、止まっていれば `DisappearDone = true`。(b) `ElementFxRuntime` に「Close の数に入れたか」の印を持ち、閉じている間の Tick はその印の要素だけ数を減らす。(a) の方が小さい。テスト: 子に FadeOut 0.1 秒、親に 0.3 秒の Disappear を置き、子を無効化した直後に親を閉じ、0.2 秒の時点で `IsOpen` が true であること。
- **確度**: コード読みで確認

### GG-R-03. 有効化のときの FirstSelected の選択が、上に開いているモーダル・入力ブロック中の親からも選択を奪う

- **場所**: `Runtime/Canvas/UiManager.cs:1975-1987`（`SelectFirstOfEmbed`）、`:1752-1770`（有効化の枝で無条件に呼ぶ）
- **内容**: 決定 3（有効化で子の FirstSelected を選択）は実装どおり。ただし、親がスタックの最上位でない・モーダルの下で入力ブロック中・閉じている途中でない Appear 待ち、のいずれでも `EventSystem.current.SetSelectedGameObject` を呼ぶ。例: ポーズメニュー（Modal）を開いている間に、ゲームコードが HUD の埋め込み（FirstSelected あり）を有効にすると、パッドのフォーカスが HUD（`interactable = false`）のボタンへ移り、ポーズメニューを操作できなくなる。`MoveSelectionOutOf`（無効化）は今の選択が子の配下にあるときだけ動くので問題ない。
- **直し方**: 選択するのは「親が `_stack` の最上位の、閉じていない Canvas」または「今の選択が null か親の Canvas の配下」のときだけにする（`RecomputeBlocking` と同じ判定で、親がブロックされていれば選ばない）。決定 3 の意図（パッドで子を出した直後にフォーカスが親に残らない）はこの条件で満たせる。docs/07 の「有効化」の行に条件を 1 文足す。
- **確度**: コード読みで確認（`SetSelectedGameObject` が `interactable = false` の `CanvasGroup` 配下の要素も選択することは uGUI の仕様からの**推定**）

## P3 — 整理・改善

### GG-R-04. CHANGELOG の「互換性」節に今回の追加が無い

- **場所**: `CHANGELOG.md` `[Unreleased]` → 「互換性」節（46-47 行目の「データの欄」「公開 API」の列挙）
- **内容**: 「追加」節には書いてあるが、CLAUDE.md §0-10・[docs/12](12_review.md) §3「互換性」は、スナップショットを更新した追加を**互換性節に追記**することを求めている。`UiAction` 7〜9・`ButtonWire.EmbeddedRootPath`・`EmbeddedCanvas.StartInactive`・`Ui` / `UiManager` の `SetEmbeddedActive` / `IsEmbeddedActive` が互換性節の列挙に無い。あわせて「データが勝つ」は U-28（未リリース）の範囲内の挙動なので「挙動の変更」には当たらない旨を書いておくと、持ち込み先が読むときに迷わない。
- **直し方**: 46 行目の U-28 の `CanvasData.EmbeddedCanvases` の後に `EmbeddedCanvas.StartInactive`・`ButtonWire.EmbeddedRootPath`・`UiAction.ActivateEmbedded` / `DeactivateEmbedded` / `ToggleEmbedded`（= 7〜9）、47 行目の `DDrive.Runtime.Ui` の列挙に 2 メソッドずつを足す。
- **確度**: コード読みで確認

### GG-R-05. 検出からの「埋め込みとして登録」で同じ RootPath の子を差し替えると `StartInactive` が消える

- **場所**: `Editor/Canvas/CanvasEmbeddedEditing.cs:250`（`Register` が `new EmbeddedCanvas { RootPath, Canvas }` で行を作り直す）
- **内容**: `ChangeEmbedWithCleanup` は直した（保つ）が、`Register` の「同じ RootPath があれば子だけ差し替える」枝は直していない。差し替えると「無効で始める」が黙って外れる（Undo で戻せる）。
- **直し方**: `var row = parent.EmbeddedCanvases[i]; row.Canvas = id; parent.EmbeddedCanvases[i] = row;`。EditMode テストを 1 件。
- **確度**: コード読みで確認

### GG-R-06. 埋め込みの RootPath を変えても、配線の `EmbeddedRootPath` は追従しない

- **場所**: `Editor/Canvas/CanvasEmbeddedEditing.cs:655-700`（`ChangeEmbedWithCleanup`）
- **内容**: RootPath 欄を `OptionRoot` → `OptionPanel` に変えると、`EmbeddedRootPath = "OptionRoot"` の配線は未登録を指したまま（`DD-CANVAS-WIRE-EMBED-UNKNOWN` の Warning と行の ⚠ は出る）。気付けるので P3。
- **直し方**: 同じ Undo グループで、同じ CanvasData の `Buttons` のうち `EmbeddedRootPath == 旧 RootPath` を新しい値に書き換える（件数をステータスに出す）。または Warning の文に「RootPath を変えた場合は配線の対象も選び直す」を足す。
- **確度**: コード読みで確認

### GG-R-07. 親の配線から入れ子の入れ子（`OptionRoot/Inner`）を指せない（実行時は動くが、欄で選べず、検査が Warning を出す）

- **場所**: `Editor/Canvas/CanvasEditorWindow.ButtonWires.cs`（対象の欄は `owner.EmbeddedCanvases` の直下の RootPath だけ）、`Editor/Canvas/CanvasEmbeddedValidator.cs`（`IsRegistered` も直下だけ）、`Runtime/Canvas/UiManager.cs:1212-1229`（実行時は `Combine(embedRoot, EmbeddedRootPath)` を Open のルート基準の Prefix と比べるので `OptionRoot/Inner` で動く）
- **内容**: `Ui.SetEmbeddedActive` の説明（docs/07）は「入れ子の入れ子は `OptionRoot/Inner`」と書いており、配線でも Inspector で書けば動く。しかし Canvas Editor の欄では選べず、書くと `DD-CANVAS-WIRE-EMBED-UNKNOWN`（「押しても何も起きません」）が**誤って**出る。
- **直し方**: 欄と検査の候補を「直下の登録 + その子 CanvasData の登録を連結したパス（深さ上限まで）」に広げる。または docs/07 に「配線は直下の埋め込みだけ（入れ子の入れ子は子の CanvasData の配線で）」と制限を書き、実行時もそれに揃える。前者を勧める。
- **確度**: コード読みで確認

### GG-R-08. 外側の Disappear の途中で内側を無効にすると内側が即消える / 同じ親の中で重なる登録では外側の無効化が内側の要素を止めない

- **場所**: `Runtime/Canvas/UiManager.cs:1761-1775`（無効化で `!wasShown` なら即 `SetActive(false)`）、`:1584-1600`（重なる登録は両方とも `Parent = node.EmbedIndex` = 互いに親子にならない）、`:1662-1680`（`FindInnermostEmbed`）
- **内容**: (1) 外側が `Deactivating`（Disappear 中）の間に内側を無効にすると、`IsEmbedShown` が false なので内側のルートを即無効にし、外側の Disappear の途中で内側だけ消える。内側の要素は `DisappearStarted` のまま残り、外側をすぐ有効に戻すと `RestartEmbedFx` の対象外（内側は表示されない）なので `DisappearStarted` が残る（GG-R-02 の経路に入る）。(2) 同じ親の中で `A` と `A/B` を両方登録する設定の誤り（既存の docs/07 で「より内側が担当」）では、`A/B` の要素は `A` の内側と判定されないため、`A` を無効化しても `A/B` の要素の Idle は止まらず（見えないまま tween が回る）、`A` を有効に戻しても `A/B` はやり直さない。
- **直し方**: (1) 外側が `Deactivating` のときは内側も「状態だけ覚える」（`SetActive(false)` は外側の完了に任せる）。(2) `Parent` を「`Root` の祖先にある登録のうち最も内側」で決める（`FindInnermostEmbed` と同じ判定を `EmbedState` 同士にも使う）。
- **確度**: コード読みで確認

### GG-R-09. SliderWire で埋め込みのアクションを選ぶと、黙って何もしない

- **場所**: `Runtime/Canvas/UiManager.cs:1314-1335`（`ExecuteSliderWire` の switch に無い）、`Runtime/Canvas/CanvasDataValidator.cs`（スライダーの配線の検査）
- **内容**: `SliderWire.Action` も `UiAction` なので、Inspector で `ActivateEmbedded` 等を選べるが、`SliderWire` に `EmbeddedRootPath` は無く、実行時は何も起きず、検査も出ない（`SetOption` をボタンで選んだ場合は ⚠ が出るのと非対称）。
- **直し方**: `CanvasEmbeddedActiveValidator` に「SliderWire の Action が埋め込みのアクション（スライダーでは何も起きません）」の Warning を足す。
- **確度**: コード読みで確認

### GG-R-10. `ButtonWire.EmbeddedRootPath` と既存の `SignalArgs.EmbeddedRootPath` は同じ名前で、基準と意味が違う

- **場所**: `Runtime/Canvas/CanvasData.cs:115-116`、`Runtime/Canvas/UiManager.cs:35`（`SignalArgs.EmbeddedRootPath` = 送り手が属する埋め込みの、Open した Canvas のルート基準のパス）
- **内容**: 決定 1 で名前は確定（改名しない）。`ButtonWire` 側は「その配線を持つ CanvasData のルート基準の、**切り替える対象**」で、空の意味も「自分が属する埋め込み」。Signal の受け手が `args.EmbeddedRootPath` をそのまま `Ui.SetEmbeddedActive` に渡すのは正しい（Open ルート基準どうし）が、`ButtonWire.EmbeddedRootPath` を `Ui.SetEmbeddedActive` に渡すのは子の配線では誤り（基準が違う）。
- **直し方**: 名前は変えず、Tooltip と docs/07 の表に「`SignalArgs.EmbeddedRootPath`（Open ルート基準・送り手）とは基準が違う」を 1 文足す。
- **確度**: コード読みで確認

### GG-R-11. テストの抜け

- **場所**: `Tests/Runtime/EmbeddedCanvasTests.cs`（追加 8 件）、`Tests/Editor/CanvasEmbeddedEditingTests.cs`・`CanvasOverrideCleanupTests.cs`（追加 3 件）
- **内容**: 追加のテストは初期状態・切り替え・プール・入れ子・配線・単独の子を見ており実効性はあるが、次が無い。(1) Open の Appear の途中で無効化して入力が戻る（GG-R-01。今のコードでは落ちる）。(2) 子の Disappear の途中で親を閉じ、親の Disappear を待つ（GG-R-02。今のコードでは落ちる）。(3) Disappear の途中で再有効化（Appear からやり直し、`IsEmbeddedActive` が true、最後に無効にならない）。(4) 有効化で子の FirstSelected が選ばれる / 無効化で親の FirstSelected へ移る（`EventSystem` を置く）。(5) 「無効で始める」の Undo と `Register` での保持（GG-R-05）。
- **直し方**: 上の 5 件を足す（(4) は GG-R-03 の条件も含める）。
- **確度**: コード読みで確認

### GG-R-12. 細部（作業用の表示・警告のキー・マニュアル）

- **場所**: `Editor/Canvas/CanvasEditorWindow.EmbedActive.cs:57-96`、`Runtime/Canvas/UiManager.cs:1744-1748, 1220-1223`、`docs/DesignerManual/`
- **内容**: (1) プレハブモードの作業用の非表示は `SceneVisibilityManager` の状態（Library 側に保存される）で、ツールチップは「ステージを閉じると戻る」と書いているが、プレハブステージの可視状態が閉じたときに消えるかは**未確認**（残る場合、次にプレハブモードを開いたとき非表示のまま = 目のアイコンで戻せる）。16-43 で確かめ、残るなら文言を「目のアイコン / もう一度押すと戻る」に変える。確認用プレビューとプレハブモードの両方が開いているときはプレハブモード側だけを切り替える（`stage != null` を先に見る）ことも文言に出すとよい。(2) 未登録パスの警告のキー `"active:" + rootPath` / `"wire-self:" + ButtonPath` は、警告済みでも呼ぶたびに文字列を作る（定常経路ではないので実害なし。気になるなら `(owner, key)` の組の作り方を既存の `WarnEmbedOnce` の呼び出しと揃えたまま、メッセージの補間だけ遅らせる）。(3) DesignerManual（Canvas Editor の埋め込みのページ）に「無効で始める」「表示 / 非表示(作業用)」「配線の 3 アクション」が無い（機能の説明として要る。[マニュアルは機能のみ] の方針どおり）。
- **直し方**: 上記のとおり。
- **確度**: (1) は**推定**、(2)(3) はコード読みで確認

---

## 見送り（指摘にしなかったもの）と理由

- **Open で増えるアロケーション**（`List<EmbedState>`・`EmbedState`・`GetComponent<CanvasGroup>`）: Open は既に `EmbedNode` / `ElementFxRuntime` / 担当表を作っており同程度。[docs/12](12_review.md) §3 の禁止は LINQ・クロージャ・boxing で、いずれも無い。
- **プール返却時に要素の見た目（Disappear の終端値）を戻さない**: 既存の Close の Disappear と同じ扱い（`FinalizeClose` はルートの alpha / scale / 位置だけ戻す）。今回の PR で悪化していない。
- **`SelectFirstOfEmbed` が `Transform.Find`、`MoveSelectionOutOf` が `FindTransform` を使う**: 両方とも Prefab 内の相対パスの解決で、結果は同じ。
- **`validator-severity.txt` に新しいコードが無い**: 既存の方針（代表の検査だけを集める。[42] §5.11-8）どおり。
- **`IsEmbeddedActive` が `Deactivating` 中に false を返す（`Toggle` で再有効化になる）**: 「今の指定」を返す設計で、トグルの直感にも合う。

## 決定事項の確認（2026-10-06 に山口さんが決定済み。コードが決定どおりか）

| # | 決定 | コード | 判定 |
|---|---|---|---|
| 1 | 名前は現状のまま確定（`StartInactive` / `SetEmbeddedActive`・`IsEmbeddedActive` / `EmbeddedRootPath` / `ActivateEmbedded`・`DeactivateEmbedded`・`ToggleEmbedded`） | `CanvasData.cs`・`Ui.cs`・`UiManager.cs` の名前、スナップショット（`enums.txt`・`public-api-DDrive.Runtime.txt`）、docs/07 の表がすべて一致 | **決定どおり**。以降は互換性ポリシーの対象。`EmbeddedRootPath` の基準の違いだけ説明を足す（GG-R-10） |
| 2 | 配線のアクション 3 つを入れる | `UiAction` 末尾 7〜9、`ExecuteButtonWire` → `ExecuteEmbeddedWire`、Canvas Editor の対象の欄・`DescribeProblem`・要約、検査 `DD-CANVAS-WIRE-EMBED-UNKNOWN`、PlayMode テスト 2 件 | **決定どおり**。入れ子の入れ子を欄で選べない・誤警告（GG-R-07）、SliderWire で選べてしまう（GG-R-09）は P3 |
| 3 | 有効化のとき子の FirstSelected を選択する | `SetEmbeddedActive` の有効化の枝で、表示される場合だけ `SelectFirstOfEmbed`（子のルート基準で解決、未設定なら何もしない）。Open のときは従来どおり親の FirstSelected | **決定どおり**。ただし条件なしで選択を奪う（GG-R-03、P2）。決定の意図（パッドで出した直後にフォーカスを子へ）を保ったまま、親がブロックされていないときに限るのを勧める |
| 4 | 登録済みの埋め込みはデータ（`StartInactive`）が Prefab 側に勝つ | `ApplyInitialEmbedStates` が登録済みの埋め込みのルートだけ `SetActive(!StartInactive)`、未登録の入れ子 Prefab には触らない。`RestoreEmbedObjects` がプール返却で Open 時の状態へ戻す（テストあり） | **決定どおり**。既存データへの影響は下 |

**決定 4 が既存データの挙動を変える範囲**:

- **リリース済みの持ち込み先（v1.3.1 以前のタグを参照する MS2026 など）には影響なし**。`EmbeddedCanvases` は U-28 で追加された `[Unreleased]`（v1.4.0、タグ未実施）の欄で、v1.3.1 以前のデータには登録済みの埋め込みが存在しない。
- 影響するのは「v1.4.0 前の main で作った CanvasData」のうち、**登録済みの埋め込みのルートを Prefab 側で無効にしてある**ものだけ。開いたときに子が見えるようになる（以前は見えなかった）。直し方はその行の「無効で始める」をオンにするだけ。開発リポジトリの確認用データ（`CANVAS_HudTest` など）で該当が無いかは、タグ前に 1 回 Prefab を開いて確かめるとよい（このレビューでは Unity を起動していないので未確認）。
- 付随する変化: ゲームコードが登録済みの埋め込みのルートを `SetActive` で直接切り替えていた場合、プールへ返すときに Open 時の状態へ戻る（以前は切り替えた状態のまま次の Open に持ち越していた）。これも U-28 の範囲内（未リリース）。docs/07 の「Prefab 側の状態との関係」に「登録済みの埋め込みのルートを直接 `SetActive` せず `Ui.SetEmbeddedActive` を使う」を 1 文足すとよい。
- 入力ゲートへの影響: Prefab で無効・`StartInactive = false` の子は、以前は無効のまま Appear をゲートに数えていた（見えない子の Appear 完了を待つ）が、今は有効になって Appear が見える形で数えられる。待つ時間は同じで、挙動としては改善。

## 確認して問題なしだった観点

- 互換: 追加のみ（スナップショットの差分はすべて追加行）。`ButtonWire` / `EmbeddedCanvas` は struct の末尾に 1 欄ずつで、既存のシリアライズ済みデータは既定値（空 / false）で読める。`StartInactive = false`・新しいアクションを使わない既存データの Open / Close / ゲート / Tick の動作は、`CountsForGate = HasAppear`・`AppearNotBefore = AppearDelay` の置き換えで従来と同値（`Held` は埋め込みが無効のときだけ立つ）。
- 入れ子の状態の覚え方・外側の有効化で表示される内側だけやり直す・プール返却で戻す・開き直しで持ち越さない（テストあり）。
- 単独で開いた子の「自分を隠す」が警告 1 回 + no-op で、`CloseSelf` に読み替えない。
- Editor の書き込み先（`owner`）・Undo / SetDirty・作業用の表示がデータにも Prefab にも書かないこと、`ChangeEmbedWithCleanup` の `StartInactive` の保持、新しいウィンドウ / メニューの追加なし（`DDriveMenu`・`ScrollView` ルートの規約の対象外）、ランタイム asmdef から `UnityEditor` の参照なし、禁止 API（`Instantiate` 等・時間の直接参照）の追加なし。
- `fc9e00a`（決定事項の反映）は docs/07・docs/43・CHANGELOG の文言だけで、コードは `29b19b7` + main の取り込みのまま。

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・Canvas Editor の操作・確認用プレビュー・プレハブモード）。PR の「green」「実クリックで確認」の報告は**未確認**。
- Unity の挙動に依存する推定: `SceneVisibilityManager` のプレハブステージでの状態の寿命、`interactable = false` の `CanvasGroup` 配下の要素への `SetSelectedGameObject`、tween（`UiTweenManager`）が無効な GameObject の上でも進むこと。
- `UiTweenManager` のプリセットの from / to の扱い（Open 時に控えた見た目へ戻す `RestartEmbedFx` が、相対のプリセットでも期待どおりになるか）は読んでいない。
- DesignerManual・`Packages/com.ddrive.core/Documentation~` は差分なし（未更新）を確認しただけ。
- メインの checkout と、そこで開いている Unity には触れていない。

---

## 再レビュー（2026-10-06、`a0a9e2a`）

> **対象**: PR #133 の `origin/feat/embedded-canvas-active` の先頭 `a0a9e2a`（「fix(canvas): 埋め込みの有効 / 無効のレビュー [65] 対応」）。前回のレビュー時点 `fc9e00a` からの差分 `git diff fc9e00a..a0a9e2a`（13 ファイル、+609 / -58。コードは `UiManager.cs`・`CanvasData.cs`〔Tooltip〕・Editor 5 ファイル・テスト 3 ファイル）。実装側の対応表（「対応」節）は PR #133 のブランチ側の docs/65 にある。
>
> **方法**: 専用 worktree で `git fetch` し、差分と変更後のファイルを**読むだけ**。`UiManager` は `SetEmbeddedActive`・`CanSelectInto`・`RestartEmbedFx`・`StartEmbedDisappear`・`FinishEmbedDeactivations`・`CompleteEmbedDeactivation`・`SettleInactiveInner`・`TickElementFx`（開いている間 / 閉じている間）・`StartAllDisappearFx` / `StartDisappear`・`RecomputeBlocking`・`OpenData`・`Close` / `TryFinalizeClose` / `FinalizeClose`・`SetupEmbedsOf`（`MaxEmbedDepth` と循環の扱い）を通して読んだ。Editor は `CanvasButtonWireEditing.CollectEmbedPaths` / `HasEmbed` / `DescribeProblem`、`CanvasEmbeddedEditing.Register` / `ChangeEmbedWithCleanup` / `RetargetEmbeddedWires`、`CanvasEmbeddedActiveValidator`、`CanvasDataValidator` の `DD-CANVAS-EMBED-NESTED-ROOT`。**Unity は起動しておらず、コンパイル・テストは実行していない**（実装側の報告 EditMode 1755/1755・PlayMode 964/964 は未確認）。

### 結論

**マージしてよい**。前回の P1（GG-R-01）・P2（GG-R-02・GG-R-03）は解消した。新しく出た指摘は P3 の 5 件（GG-R-13〜GG-R-17）だけで、どれも操作不能・データ破損につながるものではない。GG-R-16（CHANGELOG の検査の件数と docs/43 の確認手順）は文言だけなので、**v1.4.0 のタグ前**に直すのを勧める。残りはマージ後・v1.4.x でよい。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1 | **0** | — |
| P2 | **0** | — |
| P3 | **5** | GG-R-13〜GG-R-17 |

### 前回の指摘の状態

| 指摘 | 状態 | 確認したこと |
|---|---|---|
| GG-R-01（P1） | **解消** | `SetEmbeddedActive` の無効化の枝で、`MoveSelectionOutOf` の後・`StartEmbedDisappear` の前に `PendingAppearCount` を控え、数が変わったら `RecomputeBlocking()`（Disappear が無く `CompleteEmbedDeactivation` に進む場合も、ゲートから外すのは `StartEmbedDisappear` の中なので拾える）。**境界**: (a) Appear の tween が終わった直後・その Tick の前に無効化した場合、要素は `AppearCompleted = false`・`DisappearStarted = false` なので `StartEmbedDisappear` がゲートから外し、その場で再計算する。次の Tick は `DisappearStarted` で飛ばすので二重に減らさない。(b) 同じ Tick で `OnAppearCompleted` が先に減らした場合は `CountsForGate = false` で `StartEmbedDisappear` は減らさず、Tick の `gateChanged` で再計算される。(c) `!wasShown`（外側が無効 / 無効化の途中）の枝は、要素が既に `Held` か `DisappearStarted`（ゲートから外れている）なので数は変わらず、再計算は要らない。(d) 他の埋め込みの Appear が残っていれば `RecomputeBlocking` は入力不可のまま（正しい）。テスト `SetEmbeddedActive_DeactivateDuringOpenAppear_ReleasesParentInputGate` は、Tick が再計算しない状況（数の変化は `SetEmbeddedActive` の中だけ）で `interactable` を見ており、直しの有無を区別できる |
| GG-R-02（P2） | **解消** | 案 (a)。`StartDisappear` で `DisappearStarted && !DisappearDone` の要素は、再生中なら `PendingDisappearCount++`、止まっていれば `DisappearDone = true`。**二重カウント**: 閉じている間の Tick が減らすのは `DisappearStarted && !DisappearDone && !IsTweenPlaying` の要素で、減らした時点で `DisappearDone = true` にするので 1 要素 1 回。`StartAllDisappearFx` は `Close` の `Closing` ガードで 1 回だけ、最初に 0 から数え直す。無効化の Disappear が最初から無かった要素（`DisappearDone = true`）は数えず減らさない。閉じている間は `SetEmbeddedActive`（`Closing` で早期 return）・`FinishEmbedDeactivations`（開いている間の枝だけ）・`SettleInactiveInner` が走らないので、数えた後に状態を書き換える経路は無い。テスト `Close_DuringEmbedDisappear_WaitsForParentDisappear` は 0.25 秒（子 0.1 秒・親 0.5 秒）で `IsOpen` を見ており、直す前のコードでは落ちる |
| GG-R-03（P2） | **解消**（条件の細部は GG-R-15） | `CanSelectInto` = 閉じていない・`PendingAppearCount == 0`・`_stack` の最上位。`RecomputeBlocking` が入力不可にするのは「閉じていないモーダルより下」か「`PendingAppearCount > 0`」なので、**最上位で Appear 待ちでなければ親の `CanvasGroup` は必ず入力可**で、EventSystem 上も選択して操作できる状態と一致する（最上位なら上にモーダルは無い）。逆向き（入力可なのに選ばない）は GG-R-15。テストはモーダルの下で選択を奪わないことを見ている |
| GG-R-04 | **解消** | CHANGELOG「互換性」節のデータの欄・公開 API の列挙に追記し、「挙動の変更には当たらない」も書いた。ただし「追加」節の検査の件数が古い（GG-R-16） |
| GG-R-05 | **解消** | `Register` の差し替えは `row.Canvas = id` だけ。テストあり |
| GG-R-06 | **解消**（範囲は GG-R-14） | `ChangeEmbedWithCleanup` の `Undo.RecordObject(parent)` の後・同じ Undo グループの中で `RetargetEmbeddedWires`。旧 RootPath 自身とその配下（`TryToChildPath` の区切り単位。`InnerX` は触らない）を付け替える。配列の長さを変えないので、先に作った整理の計画（`ApplyCleanup`）の添字はずれない |
| GG-R-07 | **解消**（深さと循環の扱いは GG-R-13） | `CollectEmbedPaths` を欄・`DescribeProblem`・`CanvasEmbeddedActiveValidator` で共有。検査は配線に埋め込みのアクションがあるときだけ遅延して 1 回集め、lookup は `ValidationContext` ごとのキャッシュ（`BuildLookup` を internal にして共有）なので、検査の重さは CanvasData 1 件あたり 1 回の再帰で済む |
| GG-R-08 (1) | **解消**（端の挙動は GG-R-17） | 無効化の `!wasShown` の枝で、外側が `Deactivating` なら GameObject を無効にしない（`HasDeactivatingAncestor`）。外側の完了（`CompleteEmbedDeactivation`）と、表示される状態での再有効化の両方から `SettleInactiveInner` が内側を無効にし、要素を `Held` に戻す |
| GG-R-08 (2) | **見送りは妥当** | 同じ親に `A` と `A/B` を両方登録する形は、`CanvasDataValidator` の `DD-CANVAS-EMBED-NESTED-ROOT`（Warning。同じ親の登録どうしを `TryToChildPath` で比べる）が検出する。docs/07 に「保証しない」と明記済み。実行時の判定を変えると担当表（内側の登録が先に担当）との整合も見直しになり、設定の誤りのために入れる重さではない |
| GG-R-09 | **解消** | Warning `DD-CANVAS-SLIDER-EMBED-ACTION`。docs/07 の表にも追加。CHANGELOG「追加」節は未反映（GG-R-16） |
| GG-R-10 | **解消** | `ButtonWire.EmbeddedRootPath` の Tooltip と docs/07「配線」に基準の違いを追記（名前は変えていない = 決定 1 どおり。Tooltip は属性の文字列だけで、シリアライズ形式・スナップショットに影響しない） |
| GG-R-11 | **解消**（足りない分は GG-R-17） | PlayMode 5 件（入力ゲートの復帰・Disappear 途中の Close・Disappear 途中の再有効化・FirstSelected の条件・外側の Disappear 中の内側の無効化）、EditMode 2 件 + 既存テストへのスライダーの検査の追加。「無効で始める」トグルの Undo を人の確認（16-41）に回したのは、UI のコールバックの中の `Undo.RecordObject` で切り出したロジックの関数が無いため妥当 |
| GG-R-12 | (1) **解消**（16-43 の手順は GG-R-16）／ (2) **見送りは妥当** ／ (3) **未**（別担当） | (2): 文字列を作るのは「未登録のパス」「属する埋め込みが無い」という設定の誤りの経路だけで、正しい設定の定常経路（Tick・Open・正しいパスの切り替え）では作らない。毎フレーム呼ぶ使い方でも設定を直せば消える。(3): DesignerManual は本体セッションが更新する（このレビューでは見ていない） |

### 新しいコードの確認（問題なしだった観点）

- **定常経路**: Tick に増えたものは無い（`FinishEmbedDeactivations` は前回のまま）。`HasDeactivatingAncestor`・`SettleInactiveInner`・`CanSelectInto` は `SetEmbeddedActive` と無効化の完了（1 回）からだけ呼ばれ、LINQ・クロージャ・boxing・アロケーションは無い。`RecomputeBlocking` はゲートの数が変わったときだけ。
- **再入**: 新しく外へ出る呼び出しは `SelectFirstOfEmbed`（`SetSelectedGameObject`）だけで、状態を書き換え終わった後（`RestartEmbedFx`・`SettleInactiveInner` の後）に呼ぶ。`MoveSelectionOutOf` の位置は前回から変わっていない。
- **Undo**: `Register` は差し替えの前に `Undo.RecordObject(parent)`、`RetargetEmbeddedWires` は呼び出し側の `RecordObject(parent)` の後・`CollapseUndoOperations` の前なので、RootPath の変更と配線の付け替えが 1 回の Ctrl+Z で戻る。`RetargetEmbeddedWires` は public だが単体で呼ぶ場合の Undo は呼び出し側の責任（コメントに明記）。
- **Validator の重さ**: 新しい検査 1 種は Warning だけ（既存の検査の重さは変えない）。`CollectEmbedPaths` は lookup のキャッシュを使い、プロジェクト全体を読み直さない。
- **互換**: 差分にスナップショットの変更は無い（Runtime 側は Tooltip の文字列だけ。`CanvasButtonWireEditing` は Editor の公開クラスだが互換スナップショットの対象外で、`HasEmbed` / `DescribeProblem` の引数もこの PR〔未リリース〕で追加したもの）。
- **docs/07 とコードの一致**: 有効化の FirstSelected の条件（最上位・閉じていない・Appear 待ちでない）、無効化でのゲートの再計算、Disappear 途中の Close、外側の Disappear 中の内側の無効化、`DD-CANVAS-EMBED-NESTED-ROOT` の扱い、配線の入れ子の入れ子と欄、RootPath の変更での付け替え、`DD-CANVAS-SLIDER-EMBED-ACTION`、テストの件数（PlayMode 8 + 5 = 13、EditMode 3 + 2 = 5）はコードと一致。

### GG-R-13（P3）. 配線の対象の候補（`CollectEmbedPaths`）が、実行時の深さの上限・循環の扱いと一致しない

- **場所**: `Editor/Canvas/CanvasButtonWireEditing.cs:265-292`（`depth >= 8` で打ち切り、循環は `child != canvas` だけ）、`Runtime/Canvas/UiManager.cs:1443`・`:1572-1582`（`MaxEmbedDepth = 8` は Open した CanvasData を 1 段目と数え、`ChainContains` で親側の連鎖にある CanvasData を飛ばす）
- **内容**: (1) 深さ: 実行時は Open した CanvasData から 7 段下までしか埋め込まない。Editor は配線を持つ CanvasData（それ自体が埋め込みの子のこともある）から 8 段下まで候補に出す。上限を超えたパスは欄で選べ、`DD-CANVAS-WIRE-EMBED-UNKNOWN` も出ないが、実行時は未登録の警告 + 何もしない。(2) 循環: `A → B → A` のような 2 段以上の循環では、実行時には存在しない `OptionRoot/Inner/OptionRoot/Inner/…` が深さ 8 まで候補に並び、そのパスを指す配線の検査も黙る（循環そのものは `DD-CANVAS-EMBED-CYCLE` が出るので気付けはする）。どちらも設定の誤りか極端な深さのときだけで、正しい設定には影響しない。
- **直し方**: `FindOverlappingRegistration` と同じく、たどってきた CanvasData の `HashSet` で循環を止める。深さは実行時に合わせて `MaxEmbedDepth - 1`（少なくとも「配線を持つ CanvasData が Open される場合」と一致させる）にし、Runtime と Editor の別々の定数をコメントで相互参照する。
- **確度**: コード読みで確認

### GG-R-14（P3）. RootPath の付け替え（GG-R-06）は同じ CanvasData の配線だけで、上の CanvasData の入れ子の入れ子のパスは追従しない。付け替えたことも表示されない

- **場所**: `Editor/Canvas/CanvasEmbeddedEditing.cs:697`・`:710-734`（`RetargetEmbeddedWires` は `parent.Buttons` だけ。`ChangeEmbedWithCleanup` は戻り値〔付け替えた本数〕を使っていない）
- **内容**: GG-R-07 で親の配線から `OptionRoot/Inner` を指せるようになったため、子 `Option` の埋め込み `Inner` を `Inner2` に変えると、親 `Hud` の配線 `OptionRoot/Inner` は古いまま残る（`DD-CANVAS-WIRE-EMBED-UNKNOWN` と行の ⚠ が出るので気付ける）。また、付け替えが起きたことはステータスに出ないので、デザイナーは配線が書き換わったことに気付かない（Undo では戻る）。前回の GG-R-06 で勧めた「件数をステータスに出す」が入っていない。
- **直し方**: 付け替えた本数が 1 以上ならステータスに「配線 N 本の対象を付け替えました」を出す。上の CanvasData の追従は docs/07 に「付け替えるのは同じ CanvasData の配線だけ（上の CanvasData の `…/Inner` は検査の ⚠ を見て選び直す）」と範囲を書けば十分。
- **確度**: コード読みで確認

### GG-R-15（P3）. FirstSelected を選ぶ条件「スタックの最上位」は、入力できる親でも選ばない場合がある

- **場所**: `Runtime/Canvas/UiManager.cs:1833-1841`（`CanSelectInto`）
- **内容**: 安全側（選択を奪わない）に倒した判定で、GG-R-03 の不具合は無い。ただし `_stack` は開いた順なので、親より後に**モーダルでない** Canvas（通知・トースト・後から開いた HUD など）が開いていると、親は入力できるのに子の FirstSelected を選ばない。閉じている途中のモーダルが上に残っている間（`RecomputeBlocking` は閉じている途中のモーダルをブロックに数えない）も同じ。パッドで操作中に通知が出ていると、子を出してもフォーカスが親に残る（決定 3 の意図が満たされない場面がある）。
- **直し方**: 今の条件のままでよければ、docs/07 の「有効化」の行に「モーダルでない Canvas が後から開いている場合も選ばない」を 1 文足す。広げるなら「`_stack` で親より上の Canvas がすべて閉じている途中かモーダルでない」かつ「今の選択が null か親の Canvas の配下」のときに選ぶ（前回の直し方の後半）。決定 3 の範囲の判断なので、人の確認（16-44 のパッド操作）で困るかを見て決めればよい。
- **確度**: コード読みで確認（実際の画面構成で困るかは未確認）

### GG-R-16（P3、タグ前）. CHANGELOG と docs/43 の文言が今回の対応に追いついていない

- **場所**: `CHANGELOG.md:99`（「検査 2 件（Warning: `DD-CANVAS-WIRE-EMBED-UNKNOWN` / `DD-CANVAS-EMBED-FIRSTSELECTED-INACTIVE`）」）、`docs/43_manual_verification_2026-09-17.md` 16-43
- **内容**: (1) CHANGELOG の「追加」節は検査 2 件のままで、`DD-CANVAS-SLIDER-EMBED-ACTION` が無い（docs/07 の表には入っている）。(2) docs/07 と PR 側の docs/65「対応」は「ステージを閉じたときに戻るかは 16-43 で確認」としているが、16-43 の期待結果は「消える / 戻る」「`*` が付かない」だけで、**非表示のまま閉じてもう一度プレハブモードを開いたとき非表示が残るか**を見る手順が無い。このままだと確認が抜ける。
- **直し方**: (1) 「検査 3 件」にして `DD-CANVAS-SLIDER-EMBED-ACTION` を足す。(2) 16-43 に「非表示のままプレハブモードを閉じ、もう一度開く → 表示が戻っているか（残る場合は目のアイコンで戻せるか）」を足す。あわせて、入れ子の入れ子を欄で選ぶ・RootPath の変更で配線が付け替わる、の 2 つを 16-44 / 16-45 に 1 行ずつ足すとよい（自動テストはロジックだけで、欄の表示は見ていない）。
- **確度**: 読んで確認

### GG-R-17（P3）. 入れ子の端の挙動とテストの抜け

- **場所**: `Runtime/Canvas/UiManager.cs:2016-2051`（`SettleInactiveInner`）、`Tests/Runtime/EmbeddedCanvasTests.cs`
- **内容**: (1) `SettleInactiveInner` は内側の `Deactivating` を下ろさない。内側 `I` を先に無効化（`I` の Disappear 再生中、`I.Deactivating = true`）→ 外側 `O` を無効化（`O` 自身に Disappear が無く即完了）、または `O` をすぐ有効に戻す、の順だと、`SettleInactiveInner(O)` が `I` の Disappear を途中で止めて即無効にする（`I` 自身の Disappear が切れる）。`I.Deactivating` は残るが、次の Tick の `FinishEmbedDeactivations` が（要素は `Held` なので）`CompleteEmbedDeactivation(I)` を呼んで下ろすだけで、実害は無い。(2) テストは外側の Disappear の**完了後**に外側を有効に戻す場合だけを見ていて、外側の **Disappear の途中**で有効に戻す経路（`SetEmbeddedActive(true)` の中の `SettleInactiveInner`。GG-R-08 (1) のもう半分）を見ていない。`CanSelectInto` の `PendingAppearCount > 0`（親の Appear 待ち）で選ばない場合も無い。
- **直し方**: (1) `SettleInactiveInner` で `inner.Deactivating = false` を下ろす（1 行）。内側の Disappear が切れることは docs/07 の端の挙動として許容してよい。(2) PlayMode を 2 件足す（外側の Disappear 中に内側を無効化 → 外側を有効に戻す → 外側は有効・内側は無効・内側の tween が止まっている／親の Appear の途中で子を有効化 → 選択は変わらない）。
- **確度**: コード読みで確認

### 再レビューで見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・Canvas Editor の欄・プレハブモードの作業用表示）。実装側の報告（EditMode 1755/1755・PlayMode 964/964）は**未確認**。
- `SetSelectedGameObject` と `CanvasGroup.interactable` の組み合わせの実挙動（uGUI の仕様からの推定のまま）。`SceneVisibilityManager` の状態の寿命（16-43 待ち）。
- DesignerManual（GG-R-12 (3)、別担当）。
- メインの checkout と、そこで開いている Unity には触れていない。
