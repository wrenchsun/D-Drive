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
