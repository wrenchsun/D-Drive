# 66. 2026-10-06 自前レビュー結果（PR #135 = docs/63 GE-R-01・GE-R-02 への対応 + DesignerManual の追記）

> **対象**: 未マージの PR 1 本。**v1.4.0 のタグ前の差分レビュー**。
>
> | 対象 | PR | 差分 | 内容 |
> |---|---|---|---|
> | 1 | #135（`origin/fix/manual-and-editmode-camera-save`、`3e0d9e6`） | `git diff origin/main...origin/fix/manual-and-editmode-camera-save`（19 ファイル、+403 / −43） | GE-R-01（保存時のカメラの退避と書き直し）・GE-R-02（Timeline API が読めないときは書かない・警告 1 回・`masterDirector`）、`CutsceneEditModeCameraWriter` の public 化、テスト `CutsceneEditModeCameraSaveTests`（5 件）、DesignerManual 5 ページ + SpecWeb 再生成、docs/26・52 §24・60・63・CHANGELOG |
>
> **方法**: 専用 worktree で `git fetch` し、`git diff` と変更後のファイル全体を**読むだけ**。読んだもの: `CutsceneEditModeCameraWriter`・`CutsceneEditModePreviewProvider`（全体）・`CutsceneEditModeManagers`・`SceneCameraShakePreviewDriver`（保存時・後始末の順序の照合用）・追加テスト・Compat のスナップショットテスト（`PublicApiSnapshotTests` / `EditorContractSnapshotTests`）・DesignerManual の差分と UI の文字列（`CanvasEditorWindow(.ButtonWires).cs`・`CanvasButtonWireEditing`・`ModelEditorWindow`・`SourceDataCreation`）。**Unity は起動しておらず、コンパイル・EditMode / PlayMode テストは一切実行していない**（PR 本文の「EditMode 1755 / PlayMode 951 green」は未確認）。Unity の挙動に依存する推定は「確度」欄に**推定**と書いた。
>
> 前提として読んだもの: `CLAUDE.md` §0、[docs/12](12_review.md) §3、[docs/26](26_timeline.md) §4.4、[docs/63](63_review_pr126_pr129_2026-10-06.md)（GE-R-01・GE-R-02 の指摘元、書式と重大度の基準）、[docs/62](62_review_verification_fixes_u29_n8_2026-10-06.md)、[docs/42](42_distribution.md) §5。

## 総評

- **P1 は無し。P2 が 1 件（GH-R-01）**。PR の直し方（`sceneSaving` で控えた元の姿勢へ戻し、`sceneSaved` で開いていれば書き直す）自体は正しいが、**同じ Edit Mode プレビューの Shake ドライバ（`SceneCameraShakePreviewDriver`）が後から自分の `sceneSaving` で「Shake を初めて鳴らした時点の姿勢」= カットシーンの姿勢へ戻し直す**ため、Shake マーカーのあるカットシーンを Timeline ウィンドウで再生した後に保存すると、GE-R-01 と同じくカットシーンの姿勢が保存される。後始末（`TearDown` / Play Mode 突入）も同じ順序で負ける。PR 前からある経路だが、本 PR が「保存されなくなった」と CHANGELOG・docs/63 に書く対象そのもの。
- **GE-R-01 の本筋は解消**。`sceneSaving` / `sceneSaved` の順序、保存の失敗（`sceneSaved` が来ない）でも次の更新の `Apply` が `_suspendedForSave` を下ろして書き直すので自己回復する、複数シーン（カメラの属するシーンの保存のときだけ戻す。`Scene` の比較はハンドル）、プレハブステージ（入った時点で `ResetSessions` が片付けるので対象外）、Undo（`Undo.*` を使わないので Undo スタックに混ざらない）、ドメインリロード（`OnBeforeAssemblyReload` で後始末 + 購読解除、静的コンストラクターで 1 回だけ購読 = 二重購読なし）のいずれも問題なし。
- **GE-R-02 は解消**。`TimelineEditor.inspectedDirector` / `masterDirector` は Timeline 1.8 系（Unity 6000.3 同梱）の public static プロパティで、名前は正しい。見つからない・例外のときは `false`（書かない）+ 警告 1 回。実際の reflection の解決をテスト（`RealReflectionPath_ResolvesTimelineApi_…`）で固定したので、Timeline パッケージの更新で名前が変わると開発リポジトリの EditMode で赤になる。残りは細部（GH-R-04）。
- **GE-R-01 の後半（書いている途中で `Camera.main` が替わると前のカメラをカットシーンの姿勢のまま捨てる）は未対応**のまま、docs/63 は GE-R-01 全体を「対応済み」にしている（GH-R-02）。
- **互換**: `Runtime/` と `Tests/Editor/Compat/`（スナップショット）の差分 0 件。public 化・追加 public（`CutsceneEditModeCameraWriter`・`IsInspectedBy`・`IsInspectedByTimelineWindow`・`SuspendCameraForSave` / `ResumeCameraAfterSave`・`*ForTests`）はすべて `DDrive.Editor`。スナップショットは `DDrive.Foundation` / `DDrive.Runtime` だけが対象で、[12] §3 の表も「`DDrive.Editor` の `public` は対象外」。PATCH 相当の主張どおり。
- **マニュアル**: 経緯・以前との違いは混ざっていない（機能だけ）。UI の文言（「ボタンの配線(Buttons)」「+ 配線を追加」「この Canvas を編集」「← 親へ戻る」「元ファイル再読み込み」・⚠ の 4 文言・「Prefab の中に '…' の UiButton が見つかりません」・`[DDrive] MaterialData: 新規 N 件 / 更新 N 件 / 変更なし N 件`）はコードと一致。細部の食い違い・矛盾が数点（GH-R-07）。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **0** | – |
| P2（直すべき不具合・設計上の穴） | **1** | GH-R-01 |
| P3（整理・改善） | **6** | GH-R-02〜07 |

---

## P2 — 直すべき不具合

### GH-R-01. Shake マーカーを鳴らした後に保存・後始末すると、Shake ドライバが「最初に鳴らした時点の姿勢」= カットシーンの姿勢へ戻し直し、それが保存される

- **場所**: `Editor/Cutscene/CutsceneEditModePreviewProvider.cs`（静的コンストラクターの `sceneSaving += OnSceneSaving`、`TearDown` の `ResetSessions()` → `_managers.Dispose()` の順、`OnPlayModeStateChanged`）、`Editor/Cutscene/CutsceneEditModeManagers.cs:64`（`new SceneCameraShakePreviewDriver(Registry)`）、`Editor/Camera/SceneCameraShakePreviewDriver.cs:57, 87-108, 160-176, 208-216`
- **細部**:
  1. `FireShake` → `ShakeDriver.Play` → `EnsureTicking` → `CaptureOriginalCameraState()` が、**その時点の `Camera.main` の親とローカル姿勢**を控える。Edit Mode のカットシーンでは、そのときカメラは `CutsceneEditModeCameraWriter.Apply` が書いた**カットシーンの姿勢**（Camera クリップの区間内なら）。控えは `_ticking` が立っている間（= `CutsceneEditModeManagers` が生きている間）取り直されない。
  2. `sceneSaving` の購読順は「プロバイダーの静的コンストラクター（先）→ `PrepareContext` の `EnsureManagers` で作られる Shake ドライバ（後）」。.NET のマルチキャストは購読順に呼ぶので、保存時は (a) 本 PR の `SuspendForSave` がワールドの元の姿勢へ戻す → (b) Shake ドライバの `OnSceneSaving` → `RestoreCameraNow()` が親を戻して `localPosition / localRotation = 1. の控え`（カットシーンの姿勢）を書く。**保存されるのは (b) の姿勢**。
  3. 後始末も同じ順で負ける: `TearDown`（ドメインリロード前・`TearDownForTests`）は `ResetSessions()`（Writer が元へ戻す）→ `_managers.Dispose()`（Shake が控えへ戻す）。`ExitingEditMode` もプロバイダーのハンドラ（先）→ Shake ドライバのハンドラ（後）。シーン切替・プレハブステージはカメラ破棄後 / 片付け済みなので実害は少ない。
  - 結果: 「Shake マーカーのあるカットシーンを Timeline ウィンドウで再生 → Ctrl+S」で、CHANGELOG・docs/26・docs/63 が「保存されない」とした姿勢が保存される。再コンパイル・Play Mode 突入の後もカメラがカットシーンの姿勢で残り（シーンは dirty にならない見込みなので、次に他の変更と一緒に保存したときに焼き込まれる）、GD-R-01 の「片付けのときは書き込む前の姿勢へ戻す」も Shake を鳴らした後は成り立たない。
  - PR 前からある経路（Shake ドライバの控えは GD-R-01 以前から）だが、本 PR の主張の範囲に入る。テスト・[52] §24 の目視手順はどちらもこの組み合わせを通らない。
- **直し方の案**: カメラの「元に戻す」を Cutscene 側が最後に行う順序にする。
  1. プロバイダーの `OnSceneSaving` で、`_managers?.ShakeDriver.RestoreCameraNow()` を**先に**呼んでから `SuspendForSave(scene)`。Shake ドライバ自身の `sceneSaving` はその後にもう一度走るので、`CutsceneEditModeManagers` 用には Shake ドライバの `sceneSaving` 購読を外す（コンストラクターに `subscribeSceneSaving: false` 相当を足す、または Cutscene 用は購読しない派生 / 設定）か、`RestoreCameraNow` 後は控えを捨てて次の Tick で取り直す（`_ticking` 中でも `_cameraTransform` を null にして `EnsureTicking` 相当で再取得）。後者なら Shake ドライバ単体（ShakeEditor の「実際にカメラを揺らして確認」）の振る舞いも「保存のたびに今の姿勢を控え直す」で整合する。
  2. `TearDown` は `_managers?.Dispose()` を `ResetSessions()` の**前**に（Shake が控えへ戻した後に Writer がワールドの元の姿勢へ戻す）。`ExitingEditMode` でも `_managers?.ShakeDriver.StopAndRestore()` を `ResetCapture()` の前に呼ぶ。
  3. テスト: `Apply` → `_managers.ShakeDriver.Play(任意の CameraShakeData)`（`EnsureAndGetManagers()` 経由）→ `ShakeDriver.Tick(0.05f)` → 実際の保存（GH-R-03 の追加シーン保存）または `SuspendCameraForSave` の後に Shake の復元を呼んだ状態で、カメラのワールド姿勢が元の姿勢であることを確かめる。`TearDownForTests()` の後も同様。
- **確度**: 購読順・控えの取り方・`RestoreCameraNow` の書き方はコード読みで確認。Shake 鳴動時にカメラがカットシーンの姿勢であることは Camera クリップの区間内で Shake マーカーを置いた場合（演出として普通の組み合わせ）。実機での再現は未確認。

---

## P3 — 整理・改善

### GH-R-02. GE-R-01 の後半（書いている途中で `Camera.main` が替わる）は未対応なのに、docs/63 は GE-R-01 全体を「対応済み」にしている

- **場所**: `Editor/Cutscene/CutsceneEditModeCameraWriter.cs` `CaptureIfNeeded` / `RestoreIfNeeded`、`docs/63_review_pr126_pr129_2026-10-06.md` GE-R-01 の見出し
- **細部**: `_capturedCamera != cam` のとき `CaptureIfNeeded` は前のカメラを戻さずに新しいカメラを控え直し、`RestoreIfNeeded(cam)` は控えと違うカメラなら何もしない。前のカメラはカットシーンの姿勢のまま、どの `ResetCapture` / `SuspendForSave` の対象にもならない（保存すれば焼き込まれる）。docs/63 の直し方の案の 2 点目そのもの。
- **直し方の案**: `CaptureIfNeeded` の先頭で `if (_hasOriginal && _capturedCamera != cam) ResetCapture();`（`_capturedCamera` が破棄済みなら何もしない既存の分岐で足りる）。テストは `Apply` → 別のカメラに MainCamera タグを移す → `Apply` → 1 台目が元の姿勢、を 1 件。docs/63 の見出しは、直すまで「一部対応（保存時の退避のみ）」にする。
- **確度**: コード読みで確認。

### GH-R-03. テストが保存の配線（`sceneSaving` / `sceneSaved` の購読）・別シーン・「警告 1 回で書かない」の実経路を通らない

- **場所**: `Tests/Editor/CutsceneEditModeCameraSaveTests.cs`
- **細部**: (1) 5 件とも `SuspendCameraForSave` / `ResumeCameraAfterSave` を直接呼ぶので、静的コンストラクターの購読（本 PR の本体）が抜けても緑。(2) カメラが保存対象でないシーンにある場合（`SuspendForSave` が `false`）、保存が失敗して `sceneSaved` が来ない場合（次の `Apply` で `_suspendedForSave` が下りる）のテストが無い。(3) `IsInspectedBy_NullProperty_ReturnsFalse_AndNeverWrites` は `IsInspectedBy(null, …)` が `false` を返すことだけで、名前の「NeverWrites」も、`IsInspectedByTimelineWindow` の「API 不在 → 警告 1 回 → false」も見ていない（`PropertyInfo` が `static readonly` で差し替えられない）。実シーンの保存を [52] §24 の目視に回したのは理由として分かるが、**自動化できる**。
- **直し方の案**: (1)(2) は `EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive)` にカメラ（MainCamera）を置いて `Assets/` 下の一時パスへ `EditorSceneManager.SaveScene` し、保存されたテキストの `m_LocalPosition` を読む（または `sceneSaved` の時点でカメラが書き直されていることを見る）→ `AssetDatabase.DeleteAsset` + `CloseScene`。もう 1 つ空の追加シーンを保存してカメラが動かないことも同じ形で見られる。(3) は警告の判定を `PropertyInfo` を引数に取る内部関数へ寄せ（`IsInspectedBy` と同じ形）、`LogAssert.Expect(LogType.Warning, …)` で 2 回呼んで 1 回だけ出ることを固定する。テスト名は実際に見ている内容に合わせる。
- **確度**: コード読みで確認。追加シーンの一時保存がテストランナー下で問題なく動くかは**推定**（既存テストに同種の保存があればそれに合わせる）。

### GH-R-04. reflection の例外経路: 毎更新で例外を投げ続け、警告に中身が出ない。「1 回」はドメインリロードまで

- **場所**: `Editor/Cutscene/CutsceneEditModePreviewProvider.cs` `IsInspectedByTimelineWindow` / `WarnTimelineApiUnavailableOnce`
- **細部**: (1) `GetValue` が投げる場合、毎 `EditorApplication.update` × Context 数で例外を生成・捕捉し続ける（警告は 1 回だけなので気付きにくい。Editor の更新なので重大ではない）。(2) `PropertyInfo.GetValue` の例外は `TargetInvocationException` で包まれるので、警告の `e.Message` は「Exception has been thrown by the target of an invocation.」になり原因が出ない。(3) `_warnedTimelineApiUnavailable` が戻るのはドメインリロードだけ（`TearDown` では戻らない）。API が無い状態はリロードまで変わらないので妥当だが、意図をコメントに残すとよい。
- **直し方の案**: 一度例外になったら「この版では使えない」として以降は `GetValue` を呼ばずに `false`（`_timelineApiBroken = true`）。警告は `e.InnerException ?? e` の型名と `Message` を出す。リセット条件（ドメインリロードのみ）をコメントに。
- **確度**: コード読みで確認。

### GH-R-05. [52] §24 の目視手順が空振りしうる（保存が起きない・Shake を通らない）

- **場所**: `docs/52_manual_verification_fc.md` §24（24-1〜24-3）
- **細部**: (1) Ctrl+S / `File > Save` は**シーンが dirty でないと書き込まない**（docs/63 総評のとおり、プレビューの書き込みでは dirty にならない見込み）。手順に「保存の前に別のオブジェクトを少し動かして dirty にする」が無いので、何も保存されずに 24-2・24-3 が「OK」になりうる。(2) 24-3 の「シーンの自動保存」は Unity 標準には無い（コードのコメントの「自動保存」も同じ）。(3) GH-R-01 の組み合わせ（Shake マーカーを再生で通過してから保存）が無い。
- **直し方の案**: 24-1 の先頭に「確認用シーンの別のオブジェクト（カメラ以外）を少し動かしてシーンを dirty（タブに `*`）にしてから」を足す。24-3 は「`File > Save`」だけにする（自動保存の語を消す。コメントも）。GH-R-01 を直したら「Shake マーカーを含む区間を ▶ 再生で通過させてから保存」の行を足す。
- **確度**: (1) は Unity の標準の挙動（**推定**ではないが、プレビューの書き込みで dirty にならないことは**推定**）。

### GH-R-06. CHANGELOG: 保存の不具合は v1.3.1 にはなかった見込みで、「修正」節の独立した行ではなく既存の Edit Mode プレビューの行に含めるのが規則どおり

- **場所**: `CHANGELOG.md` `[Unreleased]` の「修正」節（追加された行）
- **細部**: 「修正」節の冒頭は「v1.3.1 からある不具合の修正（v1.4.0 で入る機能への修正は、その機能の記述に含めてある）」。同じ節の既存行によると、プレビュー用 Director は 2026-09-20 の DontSave 化から `FindObjectsByType` で見つからず、**v1.3.1 では Edit Mode のカメラ自体が書かれていなかった**（`OnEditorUpdate` が Context を見つけられない）。保存で姿勢が焼き込まれるのは、その修正（v1.4.0、未リリース）で書くようになってから起きる問題で、GE-R-02 の「書く側に倒れる」も #129（未リリース）で入ったもの。v1.3.1 の利用者から見ると「Edit Mode のカメラのプレビューが動くようになった」1 件で足りる。
- **直し方の案**: 追加行を消し、直下の既存行（「Cutscene の Edit Mode プレビュー（…）で、Timeline ウィンドウで再生してもマーカーが発火せず…」）の末尾の「カメラへ書くのは…戻す」の文に「シーンの保存中は書き込む前の姿勢で保存する。Timeline の状態を読めない版では書かず警告を 1 回出す」を足す。`CutsceneEditModeCameraWriter` の public 化は Editor のみで互換面外なので書かなくてよい（書くなら同じ行の括弧内）。
- **確度**: v1.3.1 の時点で DontSave 化が入っていたことは CHANGELOG・docs/26 の記述からの**推定**（v1.0.0〜v1.3.1 のタグのコードは見ていない）。

### GH-R-07. マニュアルの細部（矛盾 1・文言の省略 1・古い予定 1・警告の説明なし）

- **場所**: `docs/DesignerManual/material-data.html`・`canvas-editor.html`・`cutscene-maya-export.html`（`Tools/SpecWeb/html/manual/designer/` の同名も）
- **細部**:
  1. material-data: 「同じ Material をもう一度選ぶと『変更なし』に数えられ、Common だけ更新されます」は、同じ文の中で「変更なし」と「更新」が並んで読み手が迷う。実際は docs/63 GE-R-03 のとおり「変更なし」は Common の差分だけで決まり、シェーダーの寄せ・固有の引き継ぎで書き換えても「変更なし」に数える。
  2. canvas-editor: 「⚠ うち N 件は入れ子 Prefab の要素です」の引用は、実際の表示「⚠ うち N 件は入れ子 Prefab(埋め込みの子など)の要素です。…」の省略（UI と完全には一致しない）。
  3. canvas-editor の「Action=PlayPresentation はまだ動きません(Phase 5 予定)」（本 PR で書き換えた行）は、Phase 5 は完了済みで予定が古い（開発の経緯の語でもある）。同じページの表の「(実行時は未対応。警告だけが出ます)」と揃える。
  4. cutscene-maya-export: Timeline の状態を読めない Unity / Timeline の版で出る警告「Timeline ウィンドウの状態を取得できないため、Edit Mode のカメラのプレビューを無効にします…」の意味（カメラだけ動かない、SE・VFX・マーカーは確認できる）が書かれていない。デザイナーが最初に目にするのはこの警告。
- **直し方の案**: 1 は「同じ Material をもう一度選ぶと、Common に違いが無ければ『変更なし』に数えます（Common だけ更新され、固有の調整は残ります）」程度に。2 は表示どおりに引用する。3 は「Action=PlayPresentation は実行時に未対応です（警告だけが出ます）」。4 は Edit Mode のカメラの段落に 1 文足す。
- **確度**: 文言はコードと突き合わせて確認。

---

## 見送り（指摘にしない）と理由

- **Editor のテスト用 public の増加**（`ReadInspectedDirectorForTests` 等）: `DDrive.Editor` は互換面外（[12] §3・`PublicApiSnapshotTests` の対象外）で、InternalsVisibleTo を置かない方針（`CutsceneEditModeDirectorSetup` 等と同じ理由）に沿う。名前に `ForTests` が付いており用途も明確。
- **`masterDirector` が無く `inspectedDirector` だけある版**: 実行時は `inspectedDirector` だけで動き（入れ子の編集中だけ戻る = 害なし）、テストの `IsTimelineWindowApiResolved` は両方を要求するので開発リポジトリでは検出される。妥当。
- **保存の直後にシーン上のカメラとディスクの内容が食い違う**: 書き直しは dirty にしない見込みなので、閉じる・切り替えるときに保存を促されず、カメラは `ResetCapture` で元へ戻る（切替時は破棄済み）。プレビューの性質として妥当。
- **`OnBeforeAssemblyReload` での購読解除**: ドメインリロードで静的状態ごと消えるので実質は不要だが、無害で意図が明確。
- **DesignerManual の canvas-editor「ボタンの配線」節**: PR #126 の UI と一致（グループの見出し・`[親での上書き]` / `[親で上書き]`・⚠ の 4 文言・「Prefab の中に '…' の UiButton が見つかりません」）。他ページ（canvas-data）とは 1 行の相互リンクだけで重複は小さい。

## 確認して問題なしだった観点

| 観点 | 結果 |
|---|---|
| `sceneSaving` / `sceneSaved` の順序 | 保存は同期で、間に `EditorApplication.update` は挟まらない。`Suspend` で戻し `Consume` で印を下ろして `Apply`（または `ResetCapture`）○ |
| 保存のキャンセル・失敗 | 「名前を付けて保存」のダイアログのキャンセルは `sceneSaving` の前。書き込みの失敗で `sceneSaved` が来なくても、次の更新の `Apply` が `_suspendedForSave = false` にして書き直す / 閉じていれば `ResetCapture` ○ |
| 複数シーン | `_capturedCamera.gameObject.scene == scene` のときだけ戻す。`SaveOpenScenes` はシーンごとに `sceneSaving` / `sceneSaved` が対になる ○ |
| 保存対象でないシーンにカメラ | 何もしない（`false`）○（テストは無い = GH-R-03） |
| プレハブステージ | 入る / 出るで `ResetSessions` が片付ける。プレハブの保存は `sceneSaving` を通らない ○ |
| 保存時の dirty | 本 PR も `Undo.*` / `SetDirty` / `MarkSceneDirty` を呼ばない（コード上の経路なし。**推定**は docs/63 と同じ）○ |
| Undo スタック | 書き込み・戻しはどれも Undo に記録しない ○ |
| 二重購読・購読漏れ | 購読は `[InitializeOnLoad]` の静的コンストラクターで 1 回、解除は `beforeAssemblyReload` で全件（`update`・`playModeStateChanged`・`activeSceneChangedInEditMode`・プレハブステージ 2 件・保存 2 件・自身）○ |
| ドメインリロード後 | `TearDown` で元へ戻し、静的状態（`_suspendedForSave` 含む）は初期化される ○（Shake を鳴らした後は GH-R-01） |
| reflection の名前 | `UnityEditor.Timeline.TimelineEditor, Unity.Timeline.Editor` の public static `inspectedDirector` / `masterDirector` ○。asmdef は変えていない（CLAUDE.md §0-9）○ |
| 書かない側に倒れること | 型 / プロパティが無い → `false` + 警告 1 回、例外 → `false` + 警告 1 回 ○（細部は GH-R-04） |
| 互換 | `Runtime/`・Compat スナップショットの差分 0 件、追加の public は Editor のみ ○ |
| マニュアル | 機能だけ（経緯・以前との違いの記述なし）、UI の文言と一致（細部は GH-R-07）、SpecWeb の生成物は DesignerManual と同じ行数の差分 ○ |
| docs | docs/26 §4.4 の追記・docs/60 の行・docs/63 の「対応済み」は内容と一致（GE-R-01 の後半だけ GH-R-02） |

## 結論: マージしてよいか

**マージしてよい（条件付き）**。本 PR は GE-R-01・GE-R-02 の本筋を正しく直しており、退行は無い。ただし **GH-R-01（P2）は v1.4.0 のタグ前に直す**こと。直す場所が同じ 2 ファイル（`CutsceneEditModePreviewProvider` の保存・後始末の順序）なので、**この PR に足してからマージするのが望ましい**。別 PR にする場合は、CHANGELOG・docs/63 の「保存されない」の記述に「Shake マーカーを鳴らした後を除く」を足すか、修正まで [52] §24 の確認を保留する。

- マージ前に入れられるとよいもの: GH-R-02（1 行 + テスト 1 件）、GH-R-05（[52] §24 の手順の dirty の 1 文。これが無いと目視確認が空振りする）。
- マージ後・v1.4.x でよいもの: GH-R-03・04・06・07。

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・Timeline ウィンドウでの保存・Shake を鳴らした後の保存）。PR 本文の「green」は**未確認**。
- Unity / Timeline の挙動に依存する推定: スクリプトからの Transform / Camera の書き込みでシーンが dirty にならないこと、Timeline ウィンドウを閉じたときの `inspectedDirector` / `masterDirector` の値。
- v1.3.1 のタグのコード（GH-R-06 は CHANGELOG・docs からの推定）。
- SpecWeb の生成物は差分の行数と DesignerManual との対応だけ（中身の 1 行ずつの照合はしていない）。デプロイは PR 本文どおり未実施。
- メインの checkout と、そこで開いている Unity には触れていない。

## 対応状況（2026-10-06、PR #135 に追加コミット）

| 指摘 | 対応 |
|---|---|
| GH-R-01 | **対応済み**。Edit Mode プレビュー用の Shake ドライバは保存・後始末を自分では購読せず（`SceneCameraShakePreviewDriver(registry, ownsCameraLifecycle: false)`、既定 true で単体のドライバの動作は不変）、`CutsceneEditModePreviewProvider` が「Shake の復元（`RestoreCameraForSave` / `StopAndRestore`）→ Writer の復元」の順で呼ぶ（保存・`ResetSessions`・Play Mode 突入）。購読順に依存しない。テスト `RealSave_AfterShakeStarted_DoesNotStorePreviewPose`（旧い配線へ戻すと赤になることを確認）・`TearDown_AfterShakeStarted_RestoresOriginalPose` |
| GH-R-02 | **対応済み**。`CaptureIfNeeded` が控えと違うカメラなら先に `ResetCapture()`。テスト `MainCameraChangedMidway_RestoresPreviousCamera`。docs/63 の見出しにも反映 |
| GH-R-03 | **対応済み（案と少し違う）**。(1)(2) 実際のシーン保存を通すテスト（作業中のシーンを `saveAsCopy` で一時パスへ保存。未保存の無題シーンでは追加シーンを作れず `NewScene(Additive)` が使えないため）・別シーン・保存の失敗からの復帰。(3) `IsInspectedByOrWarn(PropertyInfo…)` に切り出して、例外を投げるプロパティで「false・警告 1 回・以降は読まない」を固定。テスト名も内容に合わせた |
| GH-R-04 | **対応済み**。一度例外になったら `_timelineApiBroken` で以降は読まない、警告に `InnerException` の型とメッセージ、リセット条件（ドメインリロードのみ）をコメントに |
| GH-R-05 | **対応済み**。docs/52 §24 に「先にシーンを変更済みにする」、24-3 の「自動保存」を削除、24-4（Shake を通過してから保存）を追加。コードのコメントの「自動保存」も直した |
| GH-R-06 | **対応済み**。CHANGELOG の独立した行を消し、既存の Edit Mode プレビューの行に統合 |
| GH-R-07 | **対応済み**。material-data（変更なしの説明）・canvas-editor（⚠ の引用・PlayPresentation の古い予定）・canvas-data（同）・cutscene-maya-export（保存時の挙動と警告の意味）。マニュアル再生成済み |

---

## 再レビュー（2026-10-06、729cd48）

> **対象**: PR #135 の追加コミット `729cd48`（前回 `3e0d9e6` から。main のマージ分〔#133 の埋め込み Canvas の有効 / 無効・docs/65 等〕は `git diff origin/main...HEAD` と突き合わせて除外し、#133 のマニュアル追記〔canvas-editor / canvas-data〕だけ見た）。
>
> **方法**: 専用 worktree で `git diff 3e0d9e6..729cd48` と変更後のファイル全体を**読むだけ**。読んだもの: `SceneCameraShakePreviewDriver`（全体）・`CutsceneEditModePreviewProvider`（全体）・`CutsceneEditModeCameraWriter`（全体）・`CutsceneEditModeManagers`・`CameraFxManager`（揺れ用ノードの付け外し）・`CutsceneEditModeCameraSaveTests`（全体）・`SceneCameraShakePreviewDriver` の他の利用箇所（`CameraFxEditorWindow`・`ScenePresentationPreviewDriver`）・CHANGELOG・docs/26 §4.4・docs/52 §24・docs/63 の見出し・DesignerManual 4 ページの差分と UI / Validator の文字列。**Unity は起動しておらず、コンパイル・テストは実行していない**（実装側の「EditMode 1767/1767・PlayMode 964/964」は未確認）。

### 総評（再レビュー）

- **GH-R-02〜07 は解消**。GH-R-01 も**報告された筋書き（Shake を鳴らした後、Writer がカメラを書いている間に保存・後始末）は解消**し、テスト `RealSave_AfterShakeStarted_DoesNotStorePreviewPose` / `TearDown_AfterShakeStarted_RestoresOriginalPose` で固定された。`ownsCameraLifecycle: false` は `CutsceneEditModeManagers` だけが使い（`CameraFxEditorWindow`・`ScenePresentationPreviewDriver` は既定 true のまま = 単体の動作は不変）、Provider の保存・`ResetSessions`（シーン切替・プレハブステージ・`TearDown` = ドメインリロード前 / テスト）・Play Mode 突入のすべてで「Shake → Writer」の順になっている。減衰中の保存も、Shake の復元（ノード破棄 + 親へ戻す）→ Writer がワールドの元の姿勢を書く、で揺れのオフセットは残らない。
- ただし **同じ根（Shake ドライバの控え = 鳴らした時点のカットシーンの姿勢を、自分の復元でローカル姿勢として書き戻す）が、Writer が控えを手放した後に残る**（GH-R-08、P2）。Shake ドライバは一度鳴らすと `ResetSessions` まで `_ticking` のままなので、「Shake マーカーを再生で通過 → Timeline ウィンドウを閉じる（またはカーソルを Camera クリップの外へ）→ 後で Ctrl+S / Play Mode 突入 / 再コンパイル」で、カットシーンの姿勢が保存される・カメラがその姿勢へ飛んで残る。前回の GH-R-01 の筋書きより起きやすい（閉じた後の保存のほうが普通）。CHANGELOG・docs/26 の「Shake マーカーを鳴らした後も同じ」はこの範囲では成り立たない。
- **GH-R-04 の「以降読まない」をドメインリロードまで続けるのは妥当**。Timeline パッケージの版はドメインリロードを跨がないと変わらず、`inspectedDirector` / `masterDirector` の getter（`state?.…`）は一時的な状態で投げる形ではない。万一一時的な例外でも、無効になるのはカメラのプレビューだけ（書かない側）で、警告に原因（`InnerException`）が出て、再コンパイルで戻る。
- **GH-R-03 の `saveAsCopy` テスト**: 作業中のシーンのパス・dirty の状態は変わらない（`saveAsCopy: true`）、一時ファイルは `finally` の `AssetDatabase.DeleteAsset` で `.meta` ごと消える（保存に失敗しても `DeleteAsset` は false を返すだけ）。バッチモードでも `SaveScene` は動く（**推定**。無題シーンのコピー保存はバッチでも可）。細部は GH-R-09。
- **互換**: 追加の public はすべて `DDrive.Editor`（`ownsCameraLifecycle` 引数〔既定値付きの末尾追加〕・`RestoreCameraForSave`・`IsInspectedByOrWarn`・`ResetTimelineApiStateForTests`）。`Runtime/` と Compat スナップショットの差分は main 側（#133）の分だけで、本 PR の追加コミットには無い。
- **マニュアル**: 機能だけ（経緯・以前との違いなし）。#133 の追記（「無効で始める」「表示 / 非表示(作業用)」「(このボタンが属する埋め込み)」・3 アクション・Warning 3 種のコード・「子を単独で開いているときは警告が 1 回」）は `CanvasEditorWindow.EmbedActive.cs`・`CanvasEditorWindow.ButtonWires.cs`・`CanvasEmbeddedValidator.cs`・`UiManager.WarnEmbedOnce` と一致。細部 1 点（GH-R-10）。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1 | **0** | – |
| P2 | **1** | GH-R-08 |
| P3 | **3** | GH-R-09〜11 |

### 解消した指摘

| 指摘 | 確認 |
|---|---|
| GH-R-01 | **報告の筋書きは解消**（Writer が書いている間の保存・`TearDown`・Play Mode 突入・シーン / プレハブステージ切替で Shake → Writer の順。購読順に依存しない）。テスト 2 件で固定。Writer が控えを手放した後は GH-R-08 |
| GH-R-02 | 解消。`CaptureIfNeeded` が控えと違うカメラなら先に `ResetCapture()`（破棄済みなら戻さない）。テスト `MainCameraChangedMidway_RestoresPreviousCamera`。docs/63 の見出しも更新 |
| GH-R-03 | 解消。実保存（`saveAsCopy`）で `sceneSaving` / `sceneSaved` の購読を通る・別シーン・保存失敗からの復帰・`IsInspectedByOrWarn` で「false・警告 1 回・以降読まない」（`LogAssert.Expect` + 読み出し回数）。テスト名も内容どおり |
| GH-R-04 | 解消。`_timelineApiBroken`・`InnerException ?? e` の型名とメッセージ・リセット条件のコメント |
| GH-R-05 | 解消。24-1 に「先に変更済み（`*`）にする」、24-3 の「自動保存」削除（コードのコメントも）、24-4（Shake 通過後の保存）追加 |
| GH-R-06 | 解消。独立行を消して既存の Edit Mode プレビューの行に統合 |
| GH-R-07 | 解消。material-data（「Common に違いが無ければ変更なし」）・canvas-editor（⚠ を表示どおり引用、PlayPresentation の古い予定）・canvas-data（同）・cutscene-maya-export（保存時の挙動と警告の意味） |

### P2 — 直すべき不具合

#### GH-R-08. Shake を鳴らした後、Writer が控えを手放してから（Timeline ウィンドウを閉じる / Camera クリップの外へ出る）保存・後始末すると、Shake ドライバがカットシーンの姿勢を書き戻す

- **場所**: `Editor/Camera/SceneCameraShakePreviewDriver.cs:106-120`（`CaptureOriginalCameraState`）・`169-187`（`RestoreCameraNow` が `localPosition / localRotation = 控え` を書く）、`Editor/Cutscene/CutsceneEditModePreviewProvider.cs:557`（`SuspendCameraForSave`）・`562`（`RestoreCameraAfterShake`）・`397-401`（閉じたら `ResetCapture`）、`Editor/Cutscene/CutsceneEditModeCameraWriter.cs:87-98`（区間外で `RestoreIfNeeded` が控えを捨てる）・`126-141`（控えが無ければ `SuspendForSave` は何もしない）
- **細部**:
  1. Camera クリップの区間内で Shake マーカーが鳴ると、Shake ドライバは**その時点の**カメラのローカル姿勢（= カットシーンの姿勢 P_c）を控え、`CameraFxManager` はカメラを揺れ用ノード（DontSave）の子にする。Shake ドライバは `ResetSessions` / `Dispose` まで `_ticking` のまま（揺れが終わっても控えは捨てない）。
  2. Timeline ウィンドウを閉じる（または再生 / スクラブで Camera クリップの外へ出る）と、Writer は**ワールドの元の姿勢 P_o** を書いて控えを捨てる（カメラはノードの子のまま、見た目は P_o）。
  3. その後の保存: Provider は Shake の `RestoreCameraForSave` → カメラを元の親へ戻して**ローカル = P_c** を書く → Writer の `SuspendForSave` は控えが無いので何もしない → **P_c が保存される**。`sceneSaved` でも Writer は何もしない（`ConsumeSuspendedForSave` が false）ので、保存の後もカメラは P_c に飛んだまま（次の Shake の Tick がノードを付け直しても P_c 基準）。
  4. 後始末も同じ: Play Mode 突入（`ExitingEditMode`）・シーン切替前のプレハブステージの出入り・再コンパイル（`TearDown`）で `StopAndRestore` が P_c を書き、Writer の `ResetCapture` は何もしない → カメラは P_c で Play Mode に入る / 残る（dirty にはならない見込みなので、次に他の変更と一緒に保存したときに焼き込まれる）。
  - 前回 GH-R-01 と根は同じ（Cutscene の文脈では Shake ドライバの控えは「元の姿勢」ではない）。今回の直し方は「Writer が控えを持っている間」だけ成り立つ。CHANGELOG（「Shake マーカーを鳴らした後も同じ」）・docs/26 §4.4・[52] 24-4 の期待はこの経路を含まない。テストも Writer が書いている状態だけ。
- **直し方の案**（どれか 1 つ）:
  1. **推奨**: `ownsCameraLifecycle: false` のとき、Shake ドライバの復元は**親子構造とノードだけ**を戻し、カメラのローカル姿勢は書かない（`CameraFxManager.DetachCurrentCamera` と同じく、ノードの揺れのオフセットを基準へ戻してから `SetParent(original, worldPositionStays: true)` + Sibling Index、ノード破棄）。姿勢の持ち主は Writer だけになり、Writer が控えを持っていれば元の姿勢へ、持っていなければ今見えている姿勢（= Writer が戻した P_o）のまま。単体のドライバ（既定 true）の動作は変えない。
  2. Writer が控えを手放す箇所（Provider の `ResetCapture` 呼び出し・`Apply` の区間外）の前に必ず `ShakeDriver.StopAndRestore()` を呼び、その後 Writer が戻す。ただし `RestoreIfNeeded` は Writer の内部なので、Writer に「手放す前」のコールバックを足すか、Provider 側で区間外を判定する必要があり、1 より配線が増える。
  - テスト: `Apply` → `ShakeDriver.Play` + `Tick` → `IsInspectedOverrideForTests = _ => false` で `ResetCapture()`（閉じた相当）→ (a) 実保存（`saveAsCopy`）で `{x: 1, y: 2, z: 3}`、(b) `TearDownForTests()` 後に `OriginalPos`。区間外（`holder.HasData = false` で `Apply`）でも同じ。[52] §24 に「24-4 のあと Timeline ウィンドウを閉じてから保存 → 元の姿勢」を 1 行。
- **確度**: コード読みで確認（`RestoreCameraNow` が `SetParent(_originalParent, false)` の後にローカル姿勢を控えで上書きすること、`_ticking` が揺れの終了で下りないこと、Writer が控えを捨てた後の `SuspendForSave` が false を返すこと）。実機での再現は未確認。

### P3 — 整理・改善

#### GH-R-09. Shake の揺れの途中で Writer が控えを取ると、揺れのオフセットが「元の姿勢」に混ざる

- **場所**: `Editor/Cutscene/CutsceneEditModeCameraWriter.cs:79-83`（`CaptureIfNeeded` が `cam.transform.position / rotation` = ワールドを控える）
- **細部**: Camera クリップより前に置いた Shake マーカーが鳴り、揺れている最中に Camera クリップの区間へ入ると、Writer はノードのオフセット込みのワールド姿勢を「元の姿勢」として控える。保存・閉じる・後始末でその姿勢（元の位置から揺れの振幅ぶんずれた位置）へ戻り、保存されうる。ずれは揺れの振幅（通常は数 cm・数度）で、GH-R-08 よりずっと小さい。
- **直し方の案**: GH-R-08 の案 1 と合わせ、Writer の控えを取る前に Shake ドライバのノードを基準へ戻す（Provider が `Apply` の前に「Shake が揺れ用ノードを持っていればそのオフセットを除いた姿勢」を渡す）か、`ownsCameraLifecycle: false` のドライバに「カメラの基準姿勢（ノードの基準 × カメラのローカル）」を返す読み取りを足して Writer が使う。優先度は低い。
- **確度**: コード読みで確認（`AttachNode` はノードをカメラの姿勢に置き、`ApplyOffsetToNode` がノードに揺れを足す）。

#### GH-R-10. 実保存テストの判定がシーン全体の文字列検索で、作業中のシーンの内容に左右されうる / 一時パスが `Assets/` 直下

- **場所**: `Tests/Editor/CutsceneEditModeCameraSaveTests.cs:140-154, 165-166, 189-190`
- **細部**: (1) `saveAsCopy` は**作業中のシーン全体**を書き出すので、`StringAssert.Contains("m_LocalPosition: {x: 1, y: 2, z: 3}")` は作業中のシーンに同じ位置のオブジェクトがあれば、カメラが正しく保存されなくても緑になりうる（逆に `{x: 10, y: 20, z: 30}` のオブジェクトがあれば赤）。確認用シーンを開いたまま MCP からテストを流す運用なので起こりうる。(2) 一時パス `Assets/__GeSaveTest.unity` は `Assets/` 直下に取り込まれ、他のポストプロセッサ（D-Drive の取り込み系を含む）を一瞬通る。`finally` で消えるので実害は小さい。
- **直し方の案**: (1) 位置をテスト固有の値（例 `{x: 1.234, y: 5.678, z: 9.012}`）にするか、保存したテキストからカメラの GameObject 名（`CamSaveTestCamera`）を含む塊の `Transform` だけを見る。(2) `Assets/__DDriveTemp/` のように既存テストの一時置き場があればそれに合わせる（無ければ現状でよい）。
- **確度**: コード読みで確認。

#### GH-R-11. canvas-data の Warning の引用が実際の文言の末尾を省いている

- **場所**: `docs/DesignerManual/canvas-data.html`（`Tools/SpecWeb/html/manual/designer/canvas-data.html` も）の検査の表、「FirstSelected '…' は、無効で始まる埋め込み EmbeddedCanvases[i] '…'(StartInactive)の配下です」
- **細部**: 実際の文言（`CanvasEmbeddedValidator.cs:329`）は末尾に「(開いた直後は選択できません)」が付く。前回 GH-R-07 の 2 と同じ種類（省略するなら「…」を付ける）。他の 2 件は全文一致。
- **直し方の案**: 末尾の括弧まで引用する。
- **確度**: 文言を突き合わせて確認。

### 確認して問題なしだった観点（再レビュー）

| 観点 | 結果 |
|---|---|
| `ownsCameraLifecycle: false` の利用者 | `CutsceneEditModeManagers` のみ。`CameraFxEditorWindow`・`ScenePresentationPreviewDriver`・既存テストは既定 true ○ |
| 購読しない Shake ドライバの後始末の漏れ | Provider が保存（`SuspendCameraForSave`）・`ResetSessions`（シーン切替・プレハブステージ 2 件・`TearDown`）・`ExitingEditMode` で呼ぶ。`Dispose` は `StopAndRestore` のあと `_subscribed` のときだけ解除（二重解除なし）○ |
| アセンブリリロード | `OnBeforeAssemblyReload` → `TearDown` → `ResetSessions`（Shake → Writer）→ `_managers.Dispose()`（Shake は既に停止済みで `StopAndRestore` は no-op）○ |
| 減衰中の保存 | `RestoreCameraNow` がノードを破棄して元の親へ戻し、その後 Writer がワールドの元の姿勢を書くので揺れのオフセットは保存されない ○（Writer が控えを持っている場合。持っていなければ GH-R-08） |
| 保存後の再開 | `sceneSaved` で Writer が書き直し、次の Shake の Tick で `CameraFxManager` がノードを付け直す（`_shakeNode` は破棄済み = Unity の null）○ |
| GH-R-02 の分岐 | 前のカメラが破棄済みなら `ResetCapture` は戻さず控えだけ捨てる ○ |
| `saveAsCopy` と作業中のシーン | パス・dirty は変わらない。一時ファイルは `finally` で `DeleteAsset`（`.meta` ごと）○（判定の細部は GH-R-10） |
| GH-R-04 の持続 | ドメインリロードまで。版の変更はリロードを伴い、書かない側に倒れるだけ ○ |
| テストの後始末 | `TearDown` で `IsInspectedOverrideForTests` / Timeline API の印 / Writer / Provider を戻し、無効にした既存の MainCamera を戻す ○ |
| 互換 | 本 PR の追加コミットの public 追加は `DDrive.Editor` のみ。コンストラクタ引数は既定値付きの末尾追加 ○ |
| マニュアル | 機能だけ。cutscene-maya-export の保存・警告の説明、canvas の埋め込みの有効 / 無効は UI / コードと一致 ○（GH-R-11 のみ） |

### 結論: マージしてよいか（再レビュー）

**マージしてよい（条件付き）**。前回の 7 件はすべて解消し、退行は無い。ただし **GH-R-08（P2）は v1.4.0 のタグ前に直す**こと。直す場所は `SceneCameraShakePreviewDriver` の復元（`ownsCameraLifecycle: false` のときローカル姿勢を書かない）でほぼ閉じ、テスト 1〜2 件で足りるので、**この PR に足してからマージするのが望ましい**。別 PR にする場合は、CHANGELOG と docs/26 §4.4 の「Shake マーカーを鳴らした後も同じ」に「Timeline ウィンドウを開いている間」の限定を付けるか、修正まで [52] 24-4 の確認を保留する。

- マージ後・v1.4.x でよいもの: GH-R-09・10・11。

### 見られなかった範囲（再レビュー）

- Unity 上での実行（コンパイル・テスト・Timeline ウィンドウでの保存・Shake 後に閉じてからの保存）。「EditMode 1767/1767・PlayMode 964/964」は**未確認**。
- `saveAsCopy` で `sceneSaving` / `sceneSaved` が呼ばれること（実装側の「旧い配線へ戻すと赤」を根拠に信頼）、バッチモードでの動作（**推定**）。
- SpecWeb の生成物は DesignerManual との差分の対応だけ。
- メインの checkout と、そこで開いている Unity には触れていない。

## 対応状況（再レビュー 729cd48 への対応、2026-10-06）

| 指摘 | 対応 |
|---|---|
| GH-R-08 | **対応済み**。`ownsCameraLifecycle: false` のとき、Shake ドライバの `RestoreCameraNow` はカメラのローカル姿勢を書かず、`Manager.StopAll`（`CameraFxManager` が揺れのオフセットを基準へ戻し、今のワールド姿勢のまま元の親へ付け直してノードを破棄）だけにした。姿勢の持ち主は Writer だけ。単体のドライバ（既定 true）は不変。テスト `RealSave_AfterShake_ThenPreviewClosed_KeepsOriginalPose`・`TearDown_AfterShake_ThenPreviewClosed_KeepsOriginalPose` |
| GH-R-09 | **見送り**。揺れの振幅ぶん（数 cm・数度）のずれで、Camera クリップより前に置いた Shake が揺れている最中に区間へ入る場合に限られる。直すには Writer に Shake の基準姿勢を渡す配線が要り、GH-R-08 の修正で姿勢の持ち主が Writer だけになった後は、v1.4.x で Writer の控えの取り方（ノードの基準を引く）と一緒に直すほうが安全 |
| GH-R-10 | **対応済み（(1) のみ）**。テストの位置をテスト固有の値（`{x: 1.25, y: 2.5, z: 3.75}` など）にして、作業中のシーンの内容と衝突しにくくした。(2) の一時パスは、既存の保存系テストに専用の置き場が無いため現状のまま |
| GH-R-11 | **対応済み**。canvas-data の引用の末尾に「(開いた直後は選択できません)」を足し、マニュアルを再生成 |

## 3 回目（2026-10-06、c85c943）

対象は 729cd48 からの差分のうち main のマージ（92a195c）を除いた c85c943 だけ（`SceneCameraShakePreviewDriver`・`CutsceneEditModeCameraSaveTests`・docs/26・docs/52 24-5・canvas-data）。GH-R-08 の対応に絞って読んだ。Unity での実行はしていない（「EditMode 1783/1783・PlayMode 964/964」は**未確認**）。

### GH-R-08 の判定: **解消**（カットシーンの姿勢が残る / 保存される問題は閉じた）

- `ownsCameraLifecycle: false` のとき `RestoreCameraNow` は控えたローカル姿勢（= Shake を鳴らした時点のカットシーンの姿勢）を書かなくなった。Shake → Timeline を閉じる（Writer が `ResetCapture`）→ 保存 / Play Mode 突入（`ExitingEditMode` → Shake → Writer）/ 再コンパイル（`TearDown` → `ResetSessions`）のどれでも、カットシーンの姿勢（数 m 単位）へ戻る経路は無くなった ○
- 単体ドライバ（既定 true）の経路は 1 行も変わっていない ○。`ownsCameraLifecycle` の利用者は引き続き `CutsceneEditModeManagers` のみ ○
- 揺れのノードを外す瞬間: `StopAllKeepingWorldPose` は外す前のワールド姿勢を控えて書き戻すので、外した瞬間にカメラは動かない（見た目のジャンプは無い）○。ただしその代わりに揺れのオフセットが姿勢に残る（GH-R-12）
- 親が動いている最中: 控え → `StopAll` → 書き戻しは同じフレーム内で完結し、`CameraFxManager.DetachCurrentCamera` は `SetParent(…, true)` なので、親の動きでずれることはない ○
- ノードが既に破棄されている場合: カメラごと消えていれば `_cameraTransform` が Unity の null で素の `StopAll` に落ちる。カメラだけ手で外してノードを消した場合も `DetachCurrentCamera` はノードの null を飛ばして元の親へ付け直すだけ ○。なお `StopAndRestore` は `StopAllKeepingWorldPose` を 2 回通る（直後の `RestoreCameraNow` でもう一度）が、2 回目は `_camera == null` で no-op なので害は無い
- 追加テスト 2 件は「Tick 1 回 → `ResetCapture` → 保存 / 後始末」で、修正前のコードなら赤になる形 ○（ただし下の GH-R-12 の経路は通らない）

### GH-R-09 の見送り: **妥当**

ずれは揺れの振幅ぶんで、「Camera クリップより前の Shake が揺れている最中に区間へ入る」ときに限られる。直し方は Writer の控えの取り方（ノードの基準を引く）か Provider の呼び順で、下の GH-R-12 と同じ場所・同じ大きさの話なので、v1.4.x で一緒に直すのがよい。

### 残り（新規）

#### GH-R-12（P3）. 「ワールド姿勢を保持」で揺れのオフセットがカメラの姿勢に焼き込まれる経路が 2 つある

- **場所**: `Editor/Camera/SceneCameraShakePreviewDriver.cs` `StopAllKeepingWorldPose`、`Editor/Cutscene/CutsceneEditModePreviewProvider.cs:291, 400`（Tick 中の `ResetCapture`）
- **細部**:
  1. **Writer が控えを持っていない（Camera クリップの外 / Camera トラックが無い）ときに、揺れの途中で保存・Play Mode 突入・再コンパイル**: 今見えているワールド姿勢 = 基準 + 揺れのオフセットをそのまま書くので、オフセットぶんずれた姿勢が保存される / 残る。729cd48 では控えたローカル姿勢（この場合は正しい元の姿勢）へ戻っていたので、この経路だけは小さな退行。保存の後もティックは続き、次のノードはずれた姿勢を基準に付くので、揺れの途中で保存を繰り返すと積み上がる
  2. **Timeline を閉じた（Tick の中の `ResetCapture`）のが揺れの途中**: Provider は Tick 中の `ResetCapture`（291 / 400 行）の前に Shake の復元を呼ばないので、Writer は「その瞬間のオフセット o(t0) が乗ったノード」の子としてワールドの元の姿勢を書く。その後も Shake のティックは続き、揺れが収まってノードが基準へ戻ると、カメラは元の姿勢から o(t0) ぶんずれたまま残る（保存すればそれが保存される）。追加テストは `ResetCapture` の後に Tick しないので通る
  - どちらもずれは揺れの振幅（通常は数 cm・数度）で、GH-R-08 の本体（数 m 単位）より小さく、GH-R-09 と同じ大きさ。[52] 24-5 は Shake が収まってから閉じれば起きない
- **直し方の案**: `ownsCameraLifecycle: false` でも `Manager.StopAll` だけ（揺れを基準へ戻してから `SetParent(…, true)` = 揺れの無い姿勢。書き戻しはしない）にし、Provider の Tick 中の `ResetCapture`（291 / 400 行）の前にも `_managers.ShakeDriver.RestoreCameraNow()` を呼ぶ（保存・後始末と同じ「Shake → Writer」の順）。こうすると、Writer が控えを持つときは Writer が最後に元の姿勢を書き、持たないときは揺れの無い姿勢になる。GH-R-09（Writer の控えの取り方）と同じ PR で直し、テストは「`ResetCapture` の後に揺れが収まるまで Tick してから保存」「Writer 無しで揺れの途中に保存」の 2 件を足す。docs/26 §4.4 の「カメラの姿勢は書かない」も、実際はワールド姿勢を書き戻しているので文言を合わせる
- **確度**: コード読みで確認（`CameraFxManager.DetachCurrentCamera` / `AttachNode` / `ApplyOffsetToNode` と Provider の呼び順）。Unity では未実行

#### GH-R-13（P3、記録のみ）. Cutscene プレビューでは保存すると揺れが止まる

- `ownsCameraLifecycle: false` の `RestoreCameraForSave` は `Manager.StopAll` を通るので、揺れの途中で保存するとその揺れは打ち切られる（単体ドライバは保存後も揺れが続く）。コメントには書かれており、保存を優先する方針として受け入れてよい。マニュアルに書くほどではない

### 結論: マージしてよいか（3 回目）

**マージしてよい**。GH-R-08 は解消し、前回までの指摘の退行も無い。残りは GH-R-09・GH-R-12（いずれも揺れの振幅ぶんのずれ、P3）と GH-R-13（記録のみ）で、v1.4.x でまとめて直せばよい。v1.4.0 のタグ前に直す必要は無いが、[52] 24-4 / 24-5 は「揺れが収まってから閉じる / 保存する」手順で確認すること（揺れの途中だと GH-R-12 の数 cm のずれが見えうる）。

### 見られなかった範囲（3 回目）

- Unity 上での実行（コンパイル・テスト・Timeline ウィンドウでの手順）。テスト件数は**未確認**
- main のマージ分（92a195c）の中身は対象外
- メインの checkout と、そこで開いている Unity には触れていない
