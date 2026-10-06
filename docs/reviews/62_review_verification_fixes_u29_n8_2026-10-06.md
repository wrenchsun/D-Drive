# 62. 2026-10-06 自前レビュー結果（確認で見つかった不具合の修正 PR #125・U-29・PR #122・N-8）

> **対象**: 2026-10-06 の 4 つの差分。いずれも Sonnet 等が実装し、まとめ役は差分を読んでいない。**v1.4.0 のタグ前の差分レビュー**。
>
> | 対象 | PR | 差分 | 内容 |
> |---|---|---|---|
> | 1 | #125（**未マージ**、`origin/fix/verification-findings-2026-10-06` = `2c69019`） | `git diff fb685b1...2c69019`（16 ファイル） | A = Cutscene の Edit Mode プレビューでマーカーが発火しない（DontSave の Director が `FindObjectsByType` に出ない）、C = `CanvasEditorWindow.IsEditingText` が Unity 6000.3 で常に false、D = 右クリックの MaterialData 作成で取り込みの警告を Console に出す・「既存 N 件」、細部（`CanvasEmbeddedValidator` の子の名前・`AssetRegistry` の Placeholder 警告の追記・文言）、docs/43 §16・docs/52 への確認結果の記入 |
> | 2 | #123 U-29（`fb685b1`） | `git diff fb685b1^1 fb685b1` | Canvas Editor: 埋め込みの登録時に親の配下の行を整理（U-29a）、プレハブモードで Idle を流すトグル（U-29b、`CanvasIdleFlow`） |
> | 3 | #122（`eb0d144`） | `git diff eb0d144^1 eb0d144` | Canvas Editor の Undo / Redo 後の再描画（`RefreshAfterUndoRedo`）、🔒 と「選択に追従」の表示、禁止 API の検査ウィンドウの行（`ForbiddenApiRowText`） |
> | 4 | #124 N-8（`33b79f2`） | `git diff 33b79f2^1 33b79f2`（27 ファイル） | NetCheck に Cutscene のマーカーのシナリオ `cut_*`（`NetCheckRunner`・`NetCheckCutsceneMarker`・`NetLaunchArgs`・`NetCheckCutsceneJudge`・`Run-NetCheck.ps1`・`NetCheckCutscene.ps1`、`DDrive.Runtime.Ngo.asmdef` に Timeline 参照） |
>
> **方法**: 専用 worktree で `git fetch` し、対象 2〜4 は `33b79f2`（detached → 本ブランチ）で、対象 1 は `git diff` / `git show origin/fix/verification-findings-2026-10-06:<path>` で**読むだけ**。変更後のファイル全体（`CutsceneEditModePreviewProvider`・`CutsceneEditModeDirectorSetup`・`CutsceneEditModeCameraWriter`・`CutsceneEditModeManagers`・`CutsceneMarkerCursor`・`CutsceneSeClip` / `CutsceneCameraClip`〔`CutsceneCameraMixerBehaviour`・`CutsceneCameraStateHolder`〕・`ExternalContractMarkerEditModeTests`・`CutsceneEditModeDirectorSetupTests`、`CanvasIdleFlow`・`ElementFxStateSnapshot`・`CanvasEmbeddedEditing` の U-29a 部分・`EmbeddedPaths.TryToChildPath`・`CanvasEditorWindow` の Idle / 埋め込み欄 / Undo / 入力中の判定の範囲・`CanvasOverrideCleanupTests` の一覧、`SourceDataCreation`・`MayaMaterialImporter` / `UnityMaterialMigrator` の Report、`AssetRegistry.NotifyPlaceholderUsed`、`NetCheckCutsceneJudge`・`NetCheckRunner` の N-8 部分・`NetCheckCutsceneMarker`・`NetLaunchArgs`・`NetCheckCutscene.ps1`・`Run-NetCheck.ps1` の N-8 部分・`CUT_NetCheck_Markers_Timeline.playable`、互換スナップショット）を実装者の報告（PR 本文・CHANGELOG・docs）を信用せずにコードと突き合わせた。`git merge-tree` で PR #125 と main（`33b79f2`）の衝突を確かめた。**Unity は起動しておらず、コンパイル・EditMode / PlayMode テスト・NetCheck は一切実行していない**。Unity の挙動に依存する推定は「確度」欄に**推定**と書いた。
>
> 前提として読んだもの: `CLAUDE.md` §0、[docs/12](../12_review.md) §3、[docs/42](../42_distribution.md) §5（§5.4・§5.9）、[61](61_review_round7_release_tools_2026-10-06.md)（書式と重大度の基準）、[docs/07](../07_canvas_prefab.md)（埋め込み・優先順位の 2 規則）、[docs/39](../archive/39_usability_fixes_2026-09-17.md)（U-28・U-29）、[docs/26](../26_timeline.md) §4.4、[docs/45](45_review_cutscene_2026-09-19.md) P1-5、[docs/14](../14_networking.md) §21〜§23、[docs/29](../verification/29_network_device_test.md) §27、[docs/43](../verification/43_manual_verification_2026-09-17.md) §16・§17、[docs/52](../verification/52_manual_verification_fc.md)。

## 総評

- **P1（リリースを止める実バグ・互換性破壊・データ破損）は見つからなかった**。互換面は v1.3.1 から見て追加のみのまま（`git diff 9f40cbb..33b79f2 -- …/Compat/Snapshots/` は 4 ファイル `+178 / −0`、**削除・変更行 0 件**。N-8 の分は `public-api-DDrive.Runtime.txt` の `+47` で、すべて CHANGELOG に書かれた意図した追加）。PR #125 はスナップショットを変えない（Editor の public 追加 2 件は [42] §5.4 の互換面の外）。
- **PR #125 の A（Edit Mode のプレビュー）の修正は正しい**。`PrepareContext` で覚えた Context を検索結果に足し、`Contains` で重複を除き、破棄済み（Unity の `== null`）は毎回外す。ドメインリロード（`beforeAssemblyReload` → `TearDown` で一覧も空）・シーン切替 / プレハブステージの出入り / Play Mode 突入（Director ごと破棄 → 次の更新で一覧から外れる）・別の Cutscene / 同じ Cutscene の開き直し（同じ GameObject・同じ Context を使い回し、Timeline が変われば `Session` を作り直す）で**古い Context を駆動し続ける・二重に駆動する・リークする経路は無い**。追加テスト `E20_EditPreview_DontSaveDirectorFromSetup_Fires` は修正前に必ず落ちる形で、実効性がある。
- **ただし、これで初めて実際に動くようになる Edit Mode のカメラの書き込みに穴がある**: プレビュー用 Director が残っている間（Timeline ウィンドウを閉じた後も）毎フレーム `Camera.main` を Cutscene の姿勢に書き続け、シーン切替・Play Mode 突入・再コンパイルでも**元の姿勢へ戻さない**（**GD-R-01、P2**）。Camera クリップを持つ Cutscene で確認用シーンのカメラが動かせなくなり、保存すればその姿勢が残る。docs/43 §10 / docs/52 3-1 の Camera の確認は素材待ちで未実施なので、**素材が届く前（= タグ前）に直す**のがよい。SE / VFX / UI / Shake / Haptic / Event は既存の Editor 用プレビュー部品（DontSave のルート・専用の Registry / Pool）を通るだけで、シーンを汚す・Play Mode の Manager と混ざる経路は見つからなかった（例外の隔離の細部 = GD-R-04）。
- **PR #125 の C / D / 細部は目的どおり**。`IsTextInputElement` は実際の要素（`TextField` / `IntegerField` / `ToolbarSearchField`）を作ってクラス名を確かめるテストで固定されており、Unity 側の改名には赤で気付ける。`AssetRegistry` の追記は「1 ID 1 回」の `_placeholderWarnedIds.Add` の内側なので**定常経路の割り当てにならず**、文面を見ている既存テスト（`Regex("Unregistered AssetId")`）・NetCheck（`"resolved to Placeholder"` の部分一致）にも影響しない。D は設計の細部に P3（GD-R-08）。
- **U-29a（登録時の整理）はデータを消す操作として安全側**。設定のある行は必ず 1 回確認し、キャンセルでは何も書かず（欄の値も `SetValueWithoutNotify(previousValue)` で戻す）、適用は 1 つの Undo グループ。配下の判定は `/` 区切りの前方一致でルート自身を除く。`IsDefaultFx` は全 10 欄（`UiPresetRef` 5 欄・`EaseDef` 4 欄）を見ており、欄の数をテストで固定している。親の配線（Buttons / Sliders）は消さない。残るのは「意図して置いた空の行」の扱い（GD-R-09、P3）。
- **U-29b（プレハブモードで Idle を流す）は、プレハブの保存（Ctrl+S・プレハブモードの自動保存・閉じるときの保存ダイアログ）・閉じる・対象の切り替え・Play Mode・ウィンドウを閉じる / ドメインリロードのすべてで流す前の値へ戻す**（`prefabSaving` は書き出しの前に呼ばれる）。`ElementFxStateSnapshot` は UiTween が書く値（`TweenProperty` の 11 種 = anchoredPosition / sizeDelta / scale / rotation〔X・Y 含む〕/ CanvasGroup の alpha〔足した CanvasGroup は外す〕/ Graphic の色〔色相含む〕/ Image の fillAmount）を全部控えている。**ただし、埋め込みの子（入れ子 Prefab のインスタンス）の要素を流している間、その値は親のプレハブモードで「入れ子インスタンスへの上書き」に見え、Overrides の Apply で子の Prefab アセットに途中の値が書かれうる**（`prefabSaving` は来ない。**GD-R-02、P2**、推定）。Undo / Redo との組み合わせにも細部（GD-R-10、P3）。
- **PR #122 は問題なし**（`RefreshAfterUndoRedo` が入力途中の欄を作り直す細部のみ = GD-R-11、P3）。
- **N-8 は `-ddrive-cutscene-test` を付けないとき新しいコードが動かず、既存 9 シナリオの挙動は変わらない**（下の「N-8 の既存シナリオへの影響」）。判定は送信者 / 受信者・s・無音の件数・二重発火を正しく見ているが、**`signal=0`（Player で Signal マーカーが読めない = M-6 の不具合）を `[WARN]` だけで PASS にする**ため、NetCheck が緑でも本物の不具合が隠れ、M-6 の後に再発しても気付けない（**GD-R-03、P2**）。
- **M-6（このレビューの対象外）への所見**: `CUT_NetCheck_Markers_Timeline.playable` の Signal トラック / マーカー 6 件は `m_Script: {fileID: 0}` + `m_EditorClassIdentifier` で保存されている。ファイルを分けるだけで既存のアセットが Player で読めるようになるか（ビルド時に Editor が `m_EditorClassIdentifier` から型を引いて正しく書き出すか）は**推定では読めるが未確認**。M-6 の後に再保存（`AssetDatabase.ForceReserializeAssets`）するか、少なくとも再保存なしで NetCheck の `signal=1` を確かめること。持ち込み先（MS2026）の Timeline も同じ（下の「M-6 への所見」）。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **0** | – |
| P2（直すべき不具合・設計上の穴） | **3** | GD-R-01（Edit Mode のカメラ）・GD-R-02（Idle と入れ子 Prefab の Apply）・GD-R-03（NetCheck の `signal=0` が PASS） |
| P3（整理・改善） | **10** | GD-R-04〜13 |

---

## 対象 1: PR #125

### A. Edit Mode のプレビュー（Context の寿命）

行番号は `2c69019` の `Editor/Cutscene/CutsceneEditModePreviewProvider.cs`。

| 場面 | 何が起きるか | 判定 |
|---|---|---|
| ドメインリロード | `beforeAssemblyReload` → `TearDown`（:409-415）→ `ResetSessions` が Director を破棄（`CutsceneEditModeDirectorSetup.TearDown`）・`_preparedContexts.Clear()`・Manager 群を `Dispose`。静的な一覧は再構築後も空 | ○ |
| シーン切替 / プレハブステージの出入り | `ResetSessions`（:401-407）が Director を破棄。覚えた Context は残るが、次の更新の `CollectContexts`（:151-171）が `== null` で外す | ○（1 フレームだけ一覧に残るが駆動はしない） |
| Play Mode 突入 | `ExitingEditMode`（:382-395）で Director を破棄。Play Mode 中は `OnEditorUpdate` 冒頭で戻る（:178）。終了後の最初の更新で外れる | ○ |
| Timeline ウィンドウで別の Cutscene を開く | `EnsureDirector` は同名の GameObject を使い回し、`PrepareContext` は同じ Context を `Contains` で重複登録しない。`playableAsset` が変わるので `Session` を作り直す（:220） | ○ |
| 同じ Cutscene を開き直す | `director.time = 0` → 次の更新で「巻き戻し」として無音で 0 へ追いつく（:266-270） | ○ |
| Director が外部要因で破棄（Hierarchy で削除） | 次の更新で一覧から外れ、`_sessions` も stale として消える | ○（Undo で復活させた場合は GD-R-04 の細部） |
| 検索結果と覚えた分の重複 | `_contextBuffer.Contains`（:164）。DontSave でない Director（確認用ハーネス等）は従来どおり検索で入る | ○ |
| 静的な状態 | `_contextBuffer` は毎回 `Clear` してから作り直す使い回しのバッファ。`_preparedContexts` は `TearDown` で空 | ○ |

**修正ラウンド 1〜7 で Edit Mode 側に入れた規則（0 秒のマーカー・先頭判定・途中再生・外部マーカーのカーソル）は、実経路でも意図どおりに働く**。判定は `director.state` と `director.time`・`Session.LastTime` だけで行い、Director が検索で見つかるか（今回の修正点）とは独立している。実経路で増えるのは `EnsureDirector` が `time = 0` + `Evaluate()` してから `PrepareContext` する順序だけで、最初の更新では `WasPlaying = false`・`LastTime = 0` の新しい `Session` になり、再生開始の立ち上がりで「先頭からの再生」と判定される（FC-R-03 の 0 秒のマーカーが鳴る）。docs/52 4-1〜4-4 の「修正後 OK」の記録と一致。

**これで初めて動くようになる経路の安全性**:

- `CutsceneEditModeManagers` は DontSave のルート（`[D-Drive] Cutscene Edit Preview`）の下に専用の `PoolService` を持ち、Audio / VFX / UI / Model / AnchorGroup は既存の Editor 用プレビュー部品（`EditorAudioFactory`・`SceneVfxPreviewDriver`・`SceneCameraShakePreviewDriver`・`EditorHapticsPreviewDriver`）と同じ構成。Registry は `EditorAnchorRegistry.Build()` の専用品で、静的ファサード（Play Mode の Manager）には触れない。SE / VFX / UI クリップは `Context.ManagerRefs` が null でなければそちらを使う（`CutsceneSeClip.cs:55` 等）ので、Edit Mode で静的ファサードへ落ちるのは `ManagerRefs` が null のときだけで、それは毎回の更新で作り直している（:210-214）。
- **カメラ**: `CutsceneEditModeCameraWriter.Apply`（:282 から毎更新）は `Camera.main` を直接書き、`ResetCapture` は元の姿勢を**戻さずに**捨てる → **GD-R-01**。
- **例外**: 外部マーカー（`ExternalMarkerCursor`）だけ例外を 1 件ずつ隔離し、Event / Shake / Haptic の発火（:355-380）は隔離していない → **GD-R-04**。

**追加テスト `E20_EditPreview_DontSaveDirectorFromSetup_Fires` の実効性**: `EnsureDirector` が作る DontSave の Director を `PrepareContext` して `OnEditorUpdate` を手で進め、外部マーカーの発火・`ManagerRefs`・`FireEnabled` を確かめる。修正前は `FindObjectsByType` に出ないので発火 0 件で必ず落ちる。前提（DontSave であること・検索に出ないこと）もアサートしており、Unity 側の挙動が変わったら理由付きで落ちる。実効性は十分。後始末は `finally` の `CutsceneEditModeDirectorSetup.TearDown` + フィクスチャの `TearDown` の `TearDownForTests` で漏れない。

**「`EnsureDirector_CalledTwice_DoesNotAccumulateSpawnedModels` が 1 回だけ失敗」の原因の見当** → **GD-R-06**（推定: 静的な `_managers` の Registry が古い）。

### C. 入力中の判定（`IsTextInputElement`）

- 判定（`CanvasEmbeddedEditing.cs` の追加部分）は要素とその祖先のクラス名（`unity-base-text-field` / `__input` / `unity-search-field-base`）。`TextField`・数値欄（`IntegerField` / `FloatField`、`Vector3Field` の子・`Slider` の入力欄も祖先で拾う）・検索欄は入力中、`Button` / `Label` / `Toggle` / `ObjectField` / `PopupField` は入力中ではない。ObjectField・Popup は文字の入力途中の値を持たないので対象外で正しい。
- **IMGUIContainer の中の入力**: `CanvasEditorWindow` に IMGUIContainer は無い（grep）ので該当しない。
- **フォーカスが残ったままウィンドウ外をクリック**: `IsEditingText` は `focusedWindow != this` なら false（既存の行）。Hierarchy / SceneView で選択を変えると、その時点でフォーカスは向こうのウィンドウにあるので**判定は効かない**（切り替わる）。入力途中の値は、埋め込み欄が遅延確定で `owner`（切り替え前の対象）を捕まえて書くため、別の CanvasData に書かれることはない。つまり判定が効くのは「このウィンドウにフォーカスがあるまま選択が変わる」場合（スクリプト・ショートカット経由）に限られる → **GD-R-07（P3、文面）**。
- **PR #122 の再描画・U-29 の Idle との整合**: 入力中の判定は「選択に追従」の切り替えだけを止める。Undo / Redo の再描画（GD-R-11）・Idle の選択による一時停止（`SuspendFor`）はこの判定と独立で、矛盾はない。

### D. 右クリックの MaterialData 作成（`SourceDataCreation`）

- Report の持ち回り: `BeginBatch` が新しい Report を作り、`EndBatch`（`finally` から必ず呼ばれる）が警告を出して Report を捨てる。キャンセル（`BeginBatch` が false）でも `EndBatch` が呼ばれ、次の操作に混ざらない。**例外で混ざる経路は 1 つ**: `BeginBatch` 自身が例外を投げると `try` の外なので `EndBatch` が呼ばれないが、次の `BeginBatch` が Report と `_materialHandling` を作り直すので実害はない → GD-R-08 の細部。
- 「既存 N 件」: `Report.Created` が増えなかったら既存。既存の Data を**更新した**（`Report.Updated`）場合も「既存 … はそのまま開きます」と出る → GD-R-08。
- 「警告:」の行の拾い方は文字列の接頭辞（`MayaMaterialImporter.cs:277`・`UnityMaterialMigrator.cs:108,113` の 3 か所が `"警告: "` で始まる）。脆いが、接頭辞は 3 か所とも同じ書き方で、変わると Console に出なくなるだけ（データは壊れない）→ GD-R-08。
- 新しい public `Option.LastCreateWasExisting`（`Func<bool>`）は `CreateOverride` の戻り値に情報を足せない（既存の public デリゲートの型を変えない）ための追加で、互換面（[42] §5.4、Editor は対象外）にも触れない。設計としては「直前の呼び出しの結果を別のデリゲートで聞く」形で、`CreateOverride` を `CreateFromSelection` 以外から呼ぶと静的な Report に溜まり続ける（次の `BeginBatch` で捨てられる）→ GD-R-08 の細部。

### 細部

- `CanvasEmbeddedValidator.ChildName`: null 安全・空の DisplayName でアセット名。問題なし。
- `AssetRegistry` の追記: 上の総評のとおり、定常経路の割り当てなし・「1 回だけ」の抑制が効く・既存テスト / NetCheck の部分一致に影響なし。「Unregistered AssetId …」の頭と「カタログには登録済み」の理由が字面で矛盾するが、既存の文面を変えない方針（CHANGELOG に明記）としては妥当。
- 「元ファイルを再読み込み」→「元ファイル再読み込み」: コード・Tooltip・テストのコメント・docs/52 は揃っている。DesignerManual の 2 か所は未修正（PR 本文の判断 4 のとおり、マージ後に本体セッションで対応）。

### docs/43・docs/52 の記入（抜き取り）

PR 本文の報告と、記入された結果（docs/43 16-11〜16-25、docs/52 1-1〜1-15・2-2・4-1〜4-4・6-1〜6-3・11-1〜11-10・14-1〜14-2・15.x・19-x）を突き合わせた。**食い違いは無い**: 16-11 は「一部 NG → 修正後 OK」（C の修正）、docs/52 4-1〜4-4 は「NG → 修正後 OK」（A の修正）、15.x の 4 は「一部 NG → 修正後 OK」（D の修正）、Camera・SE クリップ・FBX の行は「素材が必要」で □ 未のまま、PR 本文の「Edit Mode で SE / VFX / UI / Camera・Shake / Haptic が動くことは未確認」とも一致。細かい点: docs/52 4-4 は OK としつつ「一時停止からの再開は未確認」と書いている（OK の範囲を本文で限定しているので誤りではない）。

### main（`33b79f2`）との衝突と取り込みの注意

- `git merge-tree 33b79f2 2c69019`: **衝突は `docs/verification/52_manual_verification_fc.md` の 1 か所だけ**（4-4 の行の直後〜4-5 の行。PR #125 は 4-4 を書き換え、N-8 は 4-5 の末尾に「NetCheck の `cut_*` と [29] §27 を使う」を追記した。隣接行の衝突）。解き方: **4-4 は PR #125 側、4-5 は main（N-8）側**をそのまま採る。`CHANGELOG.md` は自動で合わさる（「修正」節の A の項と N-8 の 2 項は別の節）。
- PR #125 のテスト結果（EditMode 1702 / PlayMode 951）は `fb685b1` ベースで、N-8 を含まない。**マージ後に EditMode / PlayMode を両方回し直す**（N-8 で `NetCheckCutsceneJudgeTests` 28 件・`NetLaunchArgsTests` 2 件が増えている）。M-6（作業中）とは触るファイルが重ならない（PR #125 は `Runtime/Cutscene/Tracks/` に触れない）。

---

## 対象 2: U-29（PR #123）

### U-29a（登録時の行の整理）

| 観点 | 確認 | 判定 |
|---|---|---|
| `IsDefaultFx` の網羅 | `ElementFx` の 10 欄（ElementPath 以外の 9 欄）・`UiPresetRef` 5 欄・`EaseDef` 4 欄を全部見る。`IsDefaultFx_CoversEveryField_FieldCountsAreFixed` が欄の数を固定し、`IsDefaultFx_AnyFieldSet_IsNotDefault` が欄ごとに「1 つでも変えたら既定でない」を確かめる。欄が増えればテストが赤になり、判定の更新を促す | ○（固定の仕方は妥当。欄の**型**が変わった場合〔例: float → ValueDef〕は数が同じでも検出しないが、シリアライズ形式の変更は互換ポリシーで禁止なので実質起きない） |
| 配下の判定 | `EmbeddedPaths.TryToChildPath`: 前方一致 + 次の文字が `/`（`Option2/…` は `Option` の配下にならない）、`child.Length == 0`（ルート自身）を除く。入れ子の入れ子（`A/B/C/…`）も配下として数える = 子から見ても上書きなので正しい | ○ |
| 重なる登録 | 既に `A/B` が登録済みで `A` を登録すると、`A/B/…` の行も整理対象になる。どちらの子から見ても親の行は上書きなので整理の対象として正しい | ○ |
| 確認の 3 択 | 設定のある行が 0 件なら確認なしで Remove（既定の行だけ消える）。1 件以上なら `DisplayDialogComplex`（0 = 取り除く・1 = キャンセル〔Esc もここ〕・2 = 残す） | ○ |
| キャンセル | `RegisterWithCleanup` / `ChangeEmbedWithCleanup` は確認の前に何も書かず、キャンセルなら `Cancelled = true` だけを返す。欄は `SetValueWithoutNotify(previousValue)` で戻る（RootPath 欄・子欄の両方） | ○ |
| Undo 1 グループ | `IncrementCurrentGroup` → `Register` / 欄の書き換え → `ApplyCleanup`（`RecordObject`）→ `CollapseUndoOperations`。`RegisterWithCleanup_IsOneUndoGroup` で固定 | ○ |
| `ConfirmOverrideCleanupForTests` の残留 | テストの `TearDown` で必ず null に戻す。名前は FC-R-24 の `…ForTests` の流儀どおり（Editor の public で互換面の外） | ○ |
| 設定のある行を確認なしで消す経路 | `ConfirmOverrides` は `CustomRows.Count > 0` なら必ず確認を出す（テスト差し替え時を除く）。3 つの入口（検出からの登録・欄の変更・「まとめて整理」）とも同じ関数を通る | **無い**（安全側） |
| 意図して置いた**空の行** | docs/07 の規則 A では空の行も要素単位で子に勝つ（= 子の演出を止める手段になる）。U-29a はそれを「自動収集されただけの行」として確認なしで消す | → **GD-R-09（P3）** |

### U-29b（プレハブモードで Idle を流す）

**Prefab を汚す経路の洗い出し**（行番号は `33b79f2` の `CanvasEditorWindow.cs` / `CanvasIdleFlow.cs`）:

| 経路 | 対処 | 判定 |
|---|---|---|
| Ctrl+S / File > Save（プレハブモード） | `PrefabStage.prefabSaving` → `OnPrefabSaving`（:1865）が `StopIdleFlow`（復元）→ 保存 → 次の確認（0.25 秒）で再開 | ○ |
| プレハブモードの Auto Save | 同じ `prefabSaving` を通る。Idle の書き込み自体は Undo を通らないので自動保存の契機にならない（**推定**。下の「見られなかった範囲」） | ○（推定） |
| プレハブモードを閉じるときの保存ダイアログ | ダイアログで「保存」→ `prefabSaving`（復元してから書く）。`prefabStageClosing` でも復元 | ○ |
| `PrefabUtility.SaveAsPrefabAsset` | Canvas Editor の中でステージのルートを直接保存する箇所は無い（`CanvasSetupService.cs:99` は新規作成用の別ルート）。外部ツールがステージのルートを直接保存すると途中の値が書かれるが、それは既存の ElementFx ▶ 再生（2026-09-29）と同じ前提 | ○（既存と同じ） |
| **入れ子 Prefab への Apply** | 埋め込みの子の要素（入れ子 Prefab のインスタンスの中）を流すと、その値は親のプレハブモードで**入れ子インスタンスへの上書き**として Overrides に出る。Apply（Overrides の「Apply All」・右クリックの「Apply to Prefab '子'」・Variant の base への Apply）は**子の Prefab アセット**に書き、親の `prefabSaving` は来ない | → **GD-R-02（P2、推定）** |
| Undo に Idle の値が記録される | Idle は Undo を通らずに書く。選択した要素は `SuspendFor` で先に元の値へ戻してから記録が取られる（`OnSelectionChange` は選択の直後に同期で呼ばれる）。ただし Undo / Redo で**流れている要素**の値が戻った場合、停止時の復元が控えの値で上書きする | → **GD-R-10（P3）** |
| 選択中の要素をユーザーが動かす | 選択した要素とその祖先（`IsChildOf`）は一時停止・元の値へ戻し・控えを捨てる。選択が外れたら現在の値を控え直して再開（`CanvasIdleFlow.cs:319-374`）。取り違えは無い | ○ |
| ドメインリロード直前 | `OnDisable`（EditorWindow はドメインリロード前に必ず通る）で `StopIdleFlow` | ○ |
| クラッシュ | 未保存のプレハブステージの変更は失われる（自動保存の直前には `prefabSaving` で復元されている）。途中の値が残るのは「途中の値のまま保存された」場合だけで、上の経路で防いでいる | ○ |
| Play Mode に入る | `ExitingEditMode` で `SetIdleFlow(false)`（トグルもオフ） | ○ |

- **`ElementFxStateSnapshot` の欄**: anchoredPosition・sizeDelta・localScale・localRotation・CanvasGroup の alpha（Alpha トラックが足した CanvasGroup は `DestroyImmediate` で外す）・Graphic の color・Image の fillAmount。`TweenProperty` の 11 種（`PathMove` は anchoredPosition、`RotationX/Y` は localRotation、`ColorHue` は Graphic の color）を全部覆う。アンカー・pivot・マテリアルのプロパティは UiTween が書かないので不要。
- **負荷**: 毎フレームの選択比較は `Selection.activeInstanceID` と `Selection.count` の比較だけ。0.25 秒ごとの `CollectEntries` は小さな割り当て（List / HashSet / Sort の比較子）があるが Editor の確認用で、定常経路（ゲームの Tick）ではない。`SceneView.RepaintAll` は 30Hz に間引き、動いている要素があるときだけ。
- **`OnEditorUpdate` の中の例外**: `UiTweenManager.Tick` は破棄済みの対象を扱う（既存の ElementFx ▶ 再生と同じ）。`HasDestroyedTargets` で 0.25 秒以内に作り直す。新たに例外を出しそうな箇所は見つからなかった。
- **▶ 再生との排他**: 行の ▶ 再生は `HoldIdleFlowForPhasePreview` で Idle 全体を止め（復元）、▶ 側の控え（`_stageStates`）が空になってから再開する。同じ要素を 2 つの控えが同時に持つことはない。
- **編集対象の切り替え**: `ApplyTarget` で `StopIdleFlow`（:545）。
- **親 / 子のプレハブモードでの対象の解決**: `GetTargetStage(out prefix, out owner)` でステージの Prefab を持つ CanvasData（親）を引き、`CollectEntries` は実行時（`UiManager.AppendElementFx` / `SetupEmbeddedCanvases`）と同じ「先に担当した行が勝つ」（Open した CanvasData 自身 → 浅い入れ子 → 深い入れ子、同じ深さの中は RootPath の深い登録が先・配列順、空の行も要素単位で勝つ、循環・深さ 8 で打ち切り）。読み比べた範囲で規則は一致。Editor 側の複製なので将来ずれる危険はあり、docs/07 の規則を変えるときは両方を直す必要がある（既知の方針、`EmbeddedPaths.cs:7`）。
- **ADR-4**: 再生はウィンドウ専用の実 `UiTweenManager` に `UiManager.StartIdle` と同じ形（直接指定 Id → `UiTweenData`、無ければ `UiPresetFactory.Build`）で流す。Editor 専用の再生経路を新しく作っていない（既存の ElementFx ▶ 再生と同じ構成）。

---

## 対象 3: PR #122

- `RefreshAfterUndoRedo`: 編集対象（`_target`）は変えず、ヘッダー・グラフ・埋め込み欄・ElementFx 一覧・Validation を現在のデータから作り直す。`_root == null`（`CreateGUI` 前）では何もしない。Undo の範囲外の行を引く問題（2026-09-14 のレビュー対応）を埋め込み欄にも広げた形で、正しい。作り直しで入力途中の欄・スクロール位置が失われうる → **GD-R-11（P3、推定）**。
- 🔒 の表示: `_lockTarget` を変えるたびに `UpdateFollowLockUi`（チェックの値は保持して灰色にし、理由を出す）。`CreateGUI` の最後にも呼ぶので開き直しでも一致する。動作（`_followSelection && !_lockTarget`）は不変。問題なし。
- `ForbiddenApiRowText.DisplayPath`: 末尾のファイル名と行番号を必ず残し、`maxChars` が小さすぎても `suffix + 2` に丸めるので `Substring` の範囲外は起きない。UI 側の `TextOverflowPosition.Start` と二重の省略になるが害はない。`ViolationLines` は全体エラー（`Excerpt == null`）の分岐も正しい。問題なし。
- public にしたテスト用メンバー（`RefreshAfterUndoRedo`・`ForbiddenApiRowText`）: Editor の public で互換面の外（[42] §5.4）。プロジェクトの方針（InternalsVisibleTo を置かない）どおり。

---

## 対象 4: N-8（PR #124）

### 既存シナリオへの影響

`-ddrive-cutscene-test` を付けないとき（`_cutMode == null`）:

- `SetupCutsceneTest` は値が空なら何もせずに戻る（購読もしない）。`OnLogMessageReceived` の新しい分岐・`Tick` の早期 return・`ApplyCutsceneVerdict` はすべて `_cutMode != null` が条件。`RequireSignalActivity = !isOffRole && _cutMode == null` は従来の値と同じ。`OnDestroy` の `Fired -= …` は購読していなくても無害。
- `_requireLateJoinRestore` の条件に `string.IsNullOrEmpty(CutsceneTest)` が足された。空なら従来どおり。**未対応の値（`-ddrive-cutscene-test foo`）を付けたときだけ、Cutscene のシナリオは無効なのに latejoin の判定が外れる**（GD-R-12 の細部）。
- `Run-NetCheck.ps1` の既存シナリオは `Build-Args` に `-Cut` を渡さないので起動引数は不変。`ConvertTo-Json -Depth 8` は既存の結果の書き出しにも効くが、深さを増やしただけで内容は変わらない。

**結論: 既存 9 シナリオと実機テストの挙動は変わらない**。

### 判定（`NetCheckCutsceneJudge` / `NetCheckCutscene.ps1`）

| 観点 | 確認 | 判定 |
|---|---|---|
| 期待集合 | 送信者は全マーカー 1 回、受信者は `t > s` で 1 回・`s − t ≤ 0.5` で 1 回・`> 0.5` で 0 回、境界 ±0.6ms は 0..1（ログの s が F3 丸めのため）。[14] §22 の表と一致 | ○ |
| `s` と netKey の対応づけ | 本体の受信ログ（`Application.logMessageReceived` から同期で呼ばれる = インスタンス生成直後・最初の Tick の前）の時点で「まだ知らない再生中ハンドル」をその受信に対応づける。同じフレームに 2 件受信しても、ログは 1 件ずつ生成直後に出るので取り違えない（**推定**: 本体が受信 1 件ごとに生成 → ログの順で処理している前提。`CutsceneManager.cs:1132-1136` で確認） | ○（推定） |
| 二重発火 | 同じハンドルで同じキーが 2 回以上なら `duplicate_fire`。Signal も同じ | ○ |
| 同じ netKey を 2 回受信（Host 引き継ぎ後の再送など） | ハンドルが別なので各再生は個別に PASS になる。**netKey の重複は見ていない**。実機手順（[29] §27 の R1〜R5）・`Run-NetCheck.ps1` の observe はすべて `-ddrive-cutscene-expect-plays` を付けるので件数の不一致で FAIL になる（`cut_latejoin` の遅れて参加する Client を除く） | △ → GD-R-12 |
| Late Join | 受信側の s が 0.5 を超える再生は「遡って 0.5 秒以内だけ」として判定される | ○ |
| Host 引き継ぎ | follower は受信した Cutscene の再生で `no_signal_recv_after_migration` を満たす。旧 Host は `expectPlays=0` | ○ |
| **「Signal が読めないときは判定しない」分岐** | `cutscene_timeline signal=0` なら Signal の判定を飛ばし、無音の件数を 1 種類で数え、**PASS**（Format に `[WARN]` を出すだけ） | **✗ → GD-R-03（P2）** |
| 受信ログが無い再生 | `cutscene_recv` が無いハンドルは**送信者として**判定（全マーカー 1 回）。observe のプロセスでは送信者はありえないのに、`expect-plays` 無しなら `no_play_observed` にもならず PASS しうる | △ → GD-R-12 |
| PowerShell 側の穴 | ログが無い → `log_file_missing`、cutscene 行が無い → `no_cutscene_lines`、RESULT 行が無い → `no_result_line` で FAIL。シナリオの実行では全プロセスのログを必ず読むので、**一部のプロセスのログが欠けても PASS にはならない**。ただし `-JudgeOnly -Logs` は渡されたログだけを見るので、**PC 1 台分のログを渡し忘れても PASS** になりうる（trigger のログが無ければ netKey の突き合わせも飛ぶ） | △ → GD-R-12 |
| `NetCheckCutsceneMarker` のログの形式 | `cutscene_marker key=… markerTime=… elapsed=… handle=…`（InvariantCulture）。C# / PowerShell の両方の分解と一致 | ○ |

### 公開面（+47 行）の扱い

- 追加は `DDrive.Runtime.Net` の `NetCheckCutsceneJudge`（static、const 4・メソッド 7）・`NetCheckCutsceneMarkerSpec`・`NetCheckCutscenePlayVerdict`・`NetCheckCutsceneSummary`、`NetLaunchOptions` の 5 欄、`NetLaunchArgs` の 5 定数。`DDrive.Runtime.Ngo` の `NetCheckCutsceneMarker`（public の `Marker`）はスナップショットの対象外のアセンブリ。
- **internal にできるか**: Judge 一式は `DDrive.Runtime.Ngo` の `NetCheckRunner` と `DDrive.Tests.Editor` から使われ、プロジェクトの方針で InternalsVisibleTo を置かないため、internal にはできない（`NetCheckJudge` / `NetCheckCounters` / `NetCheckResult` も同じ理由で v1.3.1 から public = 前例どおり）。
- **推奨: 現状のまま「追加のみ」として受け入れる**（MINOR の範囲・CHANGELOG に明記済み・スナップショット更新済み）。理由: (1) 前例と同じ置き場と流儀、(2) Ngo 側へ移すとテストの asmdef が `DDRIVE_NGO` 制約付きのアセンブリを参照することになり、テストの構成を変える方がコストが大きい、(3) 持ち込み先が使う理由のない型で、将来外すときは [42] §5.4 の `[Obsolete]` 手続き（2 MINOR）で外せる。**今後の方針として**「NetCheck 専用の型は増やさない・増やすなら `NetCheckCutsceneJudge` のような 1 つの static クラスの中のネスト型にまとめる」を docs/14 か docs/42 に一言残すとよい。
- `NetLaunchArgs` のコマンドライン引数 `-ddrive-cutscene-*` は [42] §5.9 の弱い互換面（`-ddrive-net` と同じ扱い）に入る。追加のみで問題なし。
- `NetCheckCutsceneMarker` は持ち込み先の Timeline の「Add Marker」メニューに出る → GD-R-13（P3）。

### asmdef への Timeline 参照

`DDrive.Runtime.asmdef` が既に `Unity.Timeline` を参照し、`package.json` が `com.unity.timeline` 1.8.12 に依存しているので、持ち込み先に Timeline が無い状態は起きない。`DDrive.Runtime.Ngo` は `defineConstraints: DDRIVE_NGO`（NGO があるときだけコンパイル）のままで、`versionDefines` も不変。**持ち込み先のビルドへの影響なし**。

### パッケージ外のデータ

確認用データ（`Assets/GameData/Cutscene/NetCheck/`・`CutsceneCatalog.asset`・Addressables グループ）と ID 定数（`Assets/Generated/AssetIds.g.cs` の `NetCheckMarkers`）はすべて `Packages/com.ddrive.core/` の外で、持ち込み先には出ない。`NetCheckRunner` が ID の値（`0xDB878BC5DDC47593`）を直書きしているのは既存の `PRES_Demo_SkillSlash` と同じ流儀で、`-ddrive-cutscene-test` を付けない限り引かれない。なお docs/14 §23 と `NetCheckRunner` のコメントは定数名を `CUTID.CUT_NetCheck_Markers` と書いているが、生成された名前は `NetCheckMarkers`（細部、GD-R-13 に同梱）。

### M-6 への所見（対象外）

- 本 worktree の `33b79f2` で、`TrackAsset` / `Marker` / `MonoBehaviour` の派生でクラス名とファイル名が一致しない型は 15 個（`CutsceneNotificationTracks.cs` の Track 4 + Notification 4、`CutsceneCameraClip.cs` の `CutsceneCameraTrack` / `CutsceneCameraStateHolder`、`CutsceneAnchorGroupClip.cs` / `CutscenePresentationClip.cs` / `CutsceneSeClip.cs` / `CutsceneUiClip.cs` / `CutsceneVfxClip.cs` の各 Track）。メインの checkout の作業ツリー（git status）で見える M-6 の新しいファイル名はこれと対応している（中身は見ていない）。
- `CUT_NetCheck_Markers_Timeline.playable` の Signal のトラック / マーカー 6 件は `m_Script: {fileID: 0}`（GUID なし）+ `m_EditorClassIdentifier`。Editor が読めるのは `m_EditorClassIdentifier` で型を引いているから。**ファイルを分けた後、再保存しない既存アセットのビルドで正しい `m_Script` が書かれるかは未確認**（推定ではビルド時に Editor が型を引いて書き出すので読めるが、確証はない）。M-6 の確認では (1) 再保存なしで NetCheck の `cutscene_timeline signal=1` を見る、(2) 念のため D-Drive の Cutscene の Timeline を `AssetDatabase.ForceReserializeAssets` で保存し直す道具（または手順）を用意し、持ち込み先（MS2026）の Timeline にも同じ案内を CHANGELOG に書く、を勧める。

---

## P2 — 直すべき不具合・設計上の穴

### GD-R-01. 【PR #125 A】Edit Mode のプレビューが、Director が残っている間ずっと `Camera.main` を Cutscene の姿勢に書き続け、片付けのときに元へ戻さない

- **場所**: `Editor/Cutscene/CutsceneEditModeCameraWriter.cs:28-58`（`Apply`）・`:92-96`（`ResetCapture`）、`Editor/Cutscene/CutsceneEditModePreviewProvider.cs:282`（`2c69019`、毎更新で `Apply`）・`:382-407`（`OnPlayModeStateChanged` / `ResetSessions` が `ResetCapture` だけ呼ぶ）、`Runtime/Cutscene/Tracks/CutsceneCameraClip.cs:93-201`（`HasData` を下ろすのはクリップの区間外を評価したときだけ）
- **何が問題か**: これまでプレビュー用 Director が検索に出ず、この経路は**一度も動いていなかった**。修正後は、(1) Director が残っている間（docs/26 の追記どおり、Timeline ウィンドウを閉じても・別のアセットを選んでも残る）毎更新で `Apply` が走り、最後に評価された `holder.HasData = true` のまま**確認用シーンの Main Camera を毎フレーム書き戻す**（手で動かしてもすぐ戻る）。(2) シーン切替・プレハブステージ・Play Mode 突入・再コンパイルの片付けは `ResetCapture`（控えた元の姿勢を**捨てるだけ**）で、Director を破棄した後は `Apply` も呼ばれないので、**カメラは Cutscene の姿勢のまま残る**。Undo を通らない書き込みなので、確認用シーンを保存するとその姿勢が保存される。Play Mode に入ると、その姿勢から始まる。
- **失敗の筋書き**: Camera クリップを持つ Cutscene を「▶ Timeline ウィンドウで開く」→ 開いた瞬間（`EnsureDirector` の `Evaluate`）に 0 秒の Camera クリップの姿勢がカメラに書かれる → Timeline ウィンドウを閉じて、確認用シーンのカメラを手で動かそうとしても戻される → 別の作業のために確認用シーンを保存（Ctrl+S）→ Main Camera の姿勢が Cutscene の 0 秒の姿勢で保存される。docs/26 §4.4 の「確認用シーンのカメラを手で動かしておけば、その位置からの繋ぎを確認できる」という運用とも衝突する。
- **直し方の案**: (a) `ResetCapture` を「控えたカメラがまだ生きていれば元の姿勢へ戻してから捨てる」にする（`ResetSessions`・`ExitingEditMode`・`TearDown` の全経路で効く）。(b) 書くのは Timeline ウィンドウがその Director を評価している間だけにする: `director.playableGraph.IsValid()` が false（ウィンドウを閉じた・別の Director を開いた）なら `RestoreIfNeeded`。あるいは `FireEnabled`（再生中）または前回から `director.time` が変わったとき（スクラブ）だけ書き、それ以外は戻す。(c) テスト: Camera トラックを持つ Timeline で `EnsureDirector` → 更新 → `TearDown`（または `ResetSessions` 相当）→ `Camera.main` の姿勢が元に戻っていること。
- **確度**: 確認済み（コード読み）。実際のカメラの挙動は未確認（Camera クリップの素材が無く、docs/43 §10・docs/52 3-1 も未実施）。シーンが dirty 扱いになるかは**推定**（Undo を通らない書き込みは dirty にならないことが多いが、他の変更と一緒に保存されれば姿勢は残る）。
- **マージとの関係**: PR #125 のマージは止めない（A の修正自体は正しく、カメラ以外の Edit Mode のプレビューが初めて動く）。**v1.4.0 のタグ前に直す**（マージで経路が生きるため）。

### GD-R-02. 【U-29b】埋め込みの子の要素を流している間、その値は入れ子 Prefab インスタンスへの上書きに見え、Apply で子の Prefab アセットに途中の値が書かれうる

- **場所**: `Editor/Canvas/CanvasIdleFlow.cs:123-178`（`CollectEntries` が埋め込みの子の行も集める）・`:232-262`（`Start` がステージのルートから要素を引いて流す）、`Editor/Canvas/CanvasEditorWindow.cs:1858-1870`（後始末の契機は親のステージの `prefabStageClosing` / `prefabSaving` だけ）
- **何が問題か**: 親のプレハブモードでは、埋め込みの子の Canvas は**入れ子 Prefab のインスタンス**で、その中の RectTransform 等の値を書き換えると、Unity はそれを「入れ子インスタンスへの上書き（Property Modification）」として扱う（Hierarchy で太字・Overrides ドロップダウンに出る）。Overrides の「Apply All」・要素の右クリックの「Apply to Prefab '子'」・Inspector の値の右クリックの Apply は、**親の保存（`prefabSaving`）を通らずに子の Prefab アセットへ直接書く**。Idle を流している最中に Apply すると、子の Prefab に Idle の途中の値（位置・スケール・alpha・CanvasGroup の追加）が保存される。親の要素でも、親が Prefab Variant なら「base へ Apply」で同じことが起きる。
- **失敗の筋書き**: 親 `CANVAS_Hud` のプレハブモードで「Idle を流す」をオン → 子 `OptionTest` の `Panel` が Idle（Pulse）で拡縮している → デザイナーが別の要素（子の中の、Idle の無い Text）を直して Overrides の「Apply All」で子の Prefab に反映 → Idle 中の `Panel` の scale（例 1.07）も上書きとして一緒に Apply され、子の Prefab の `Panel` の scale が 1.07 で保存される。トグルを切ると親のステージでは 1.0 に戻るが、子の Prefab は 1.07 のまま（今度は親側に「1.0 への上書き」が残る）。
- **直し方の案**: (a) 最小: 埋め込みの子（ステージの Prefab 自身ではない入れ子インスタンスの中）の要素は流さない（トグルの文言を「この Prefab の要素」に変え、子の Idle は子のプレハブモードで流す）。(b) 流すなら、`PrefabUtility.IsPartOfPrefabInstance` の要素について Apply の前に止める手段が Unity に無いので、ツールチップ・マニュアル・ウィンドウのヒントに「流している間は Overrides の Apply をしない（止めてから Apply）」を明記し、Hierarchy の太字の理由を説明する。(c) どちらにしても、親が Variant の場合も同じ注意が要る。
- **確度**: **推定**（Unity の入れ子 Prefab の上書きの仕組みからの推定。実機では未確認。コードでは「入れ子インスタンスの中の要素を Undo を通らずに書く」「Apply の前に止める処理が無い」ことを確認済み）。
- **マージとの関係**: マージ済み。Editor のみ・持ち込み先のデータは壊さない（デザイナーの作業データ）。v1.4.0 で (a) か (b) を入れることを勧める（(b) は文言だけなので安い）。

### GD-R-03. 【N-8】`cutscene_timeline signal=0`（Player で Signal マーカーが読めない）を `[WARN]` だけで PASS にする

- **場所**: `Runtime/Net/NetCheckCutsceneJudge.cs:335-338, 375`、`Tools/CI/NetCheckCutscene.ps1:74, 101, 131, 170`
- **何が問題か**: `signal=0` のとき Signal の判定を丸ごと飛ばし、無音の件数も 1 種類で数え、他が合えば **PASS**。現状の Player ビルドは M-6 の不具合で `signal=0` なので、NetCheck の `cut_*` は「Player で Signal トラックが読めない」という**本物の不具合があるまま緑**になる（docs/14 §22 の「実 NGO でのローカルの結果: PASS」はこの状態での PASS）。M-6 の後に同じ不具合が再発しても（新しいトラック型をファイル名違いで足す等）NetCheck は緑のまま。判定の目的（[52] 4-5 の実機確認の代わり）からすると、Signal はマーカーの主要な種類で、外部マーカーだけの PASS は確認として弱い。
- **失敗の筋書き**: M-6 の修正後、別の PR が `CutsceneXxxTrack` を既存のファイルに追記する → Player でそのトラックが読めなくなる → `cutscene_timeline ok=1 signal=…` の `ok` は外部マーカーだけを見るので 1 のまま → `run-ci.cmd` の 8 段目（NetCheck）は ALL GREEN。
- **直し方の案**: `signal=0` を既定で **FAIL**（`timeline_signal_not_loaded`）にし、M-6 が入るまでの間だけ使う明示の逃げ道（例 `-ddrive-cutscene-allow-no-signal`、または `Run-NetCheck.ps1 -AllowNoSignal`）を用意する。M-6 のマージと同時に逃げ道を外す（または既定を反転する）。あわせて `ok` の条件にトラックの型の件数（`CutsceneSignalTrack:1`）を足すと、トラックだけ読めてマーカーが読めない場合も拾える。
- **確度**: 確認済み（コード読み。docs/14 §23.1 の Player のログ `signal=0` と一致）
- **マージとの関係**: マージ済み。M-6 と同時に直すのが自然（M-6 のレビューで確認する）。

---

## P3 — 整理・改善

### GD-R-04. 【PR #125 A】Edit Mode の Event / Shake / Haptic の発火に例外の隔離が無く、例外が出ると次の更新で同じマーカーを鳴らし直す

- **場所**: `Editor/Cutscene/CutsceneEditModePreviewProvider.cs:237-265, 280-281, 355-380`（`2c69019`）
- **細部**: 外部マーカーは 1 件ずつ `try/catch` で隔離しているが、`FireEvent`（`EventBus.RaiseAdHoc` → `AssetEventDispatcher.HandleEvent`、どちらも `catch` なし）・`FireShake`・`FireHaptic` は隔離していない。再生開始の立ち上がりの分岐で例外が出ると `session.WasPlaying` / `LastTime` が更新されず、次の更新で再び「立ち上がり」に入って `Collect`（カーソルを 0 に戻す）→ 同じマーカーを**毎更新で鳴らし直す**（SE の連打）。Manager は例外を出さない方針（§0-4）なので起きにくいが、これまで動いていなかった経路なので、Placeholder の扱いなどで初めて例外が見つかる可能性はある。あわせて細部: (1) Timeline ウィンドウのループ再生で末尾から先頭へ戻ったとき、巻き戻しとして無音で追いつくので、2 周目以降の 0 秒〜最初の更新までのマーカーは鳴らない（Play Mode にループは無いので仕様の範囲だが docs/26 に一言）。(2) Hierarchy で消したプレビュー用 Director を Undo で戻すと、覚えた一覧からは外れているので（DontSave で検索にも出ない）駆動されない（開き直せば戻る）。
- **直し方の案**: 4 つの `Fire*` の呼び出しを外部マーカーと同じく 1 件ずつ `try/catch` + `Debug.LogException` で隔離する（または `CutsceneMarkerCursor.Advance` の `action` 呼び出しを隔離する）。`session.WasPlaying` / `LastTime` の更新を `finally` に置く。
- **確度**: 確認済み（コード読み。例外が実際に出る経路は見つけていない）

### GD-R-05. 【PR #125 A】プレビュー用 Director の片付けの時機（Timeline ウィンドウを閉じても残る）を、利用者が知る手段が docs/26 だけ

- **場所**: `docs/26_timeline.md` §4.4 の追記（`2c69019`）、docs/52 1-14
- **細部**: 実装どおりに手順書を直した（PR 本文の B）のは妥当だが、GD-R-01 と組み合わせると「閉じたのにカメラが戻らない / 動かせない」になる。GD-R-01 を直せば実害は消えるが、DesignerManual の Cutscene のページにも「確認用シーンを切り替える・Play Mode に入ると片付く」と一言あるとよい。
- **直し方の案**: GD-R-01 の修正と同じ PR でマニュアルに 1 行。
- **確度**: 確認済み（docs 読み）

### GD-R-06. 【PR #125 テスト】`EnsureDirector_CalledTwice_DoesNotAccumulateSpawnedModels` の 1 回だけの失敗は、静的な `_managers`（古い Registry）の持ち越しが原因と見られる

- **場所**: `Tests/Editor/CutsceneEditModeDirectorSetupTests.cs:24-31, 129-138`、`Editor/Cutscene/CutsceneEditModePreviewProvider.cs:128-139`（`EnsureAndGetManagers` は既にあれば作り直さない）、`Editor/Cutscene/CutsceneEditModeManagers.cs:61-67`（Registry は作成時の `EditorAnchorRegistry.Build()`）
- **細部**: このフィクスチャは後始末を `TearDown` でだけ行い、`SetUp` で静的な Manager 群を作り直さない。テストの前に `_managers` が残っていると（同じ Editor で直前に「▶ Timeline ウィンドウで開く」で手の確認をした・後始末をしない別のテストが先に走った）、その Registry には**このテストが作った ModelData（ID 900000000002）が入っていない**ので、`Models.Spawn` が Placeholder / 無効なハンドルになり、`childCount` が 0 で落ちる。`CalledTwice` はフィクスチャの中で名前順が最初（`CalledTwice` < `CreatesDontSave…`）なので、持ち越しを受けるのはこのテストだけで、その `TearDown` で片付くため再実行では再現しない。PR #125 の報告（全件の 1 回目だけ・再実行で再現せず・確認と同じ日の同じ Editor）と合う。修正で増えたのではなく既存のテストの穴。
- **直し方の案**: フィクスチャに `[SetUp]` を足して `CutsceneEditModePreviewProvider.TearDownForTests()` と `CutsceneEditModeDirectorSetup.TearDown()` を先に呼ぶ（または `CreateModelData` の後で `CutsceneEditModePreviewProvider.RefreshRegistry()`）。
- **確度**: **推定**（コード読み。再現は試していない）

### GD-R-07. 【PR #125 C】入力中の判定が効くのは「Canvas Editor にフォーカスがあるまま選択が変わる」場合だけ

- **場所**: `Editor/Canvas/CanvasEditorWindow.cs:1257-1266`（`2c69019`）、CHANGELOG の追記・docs/43 16-11 の記録
- **細部**: `IsEditingText` は `focusedWindow != this` で false を返す（既存）。Hierarchy / SceneView のクリックで選択を変えるとフォーカスはそちらへ移るので、ふつうの操作では判定は効かず、編集対象は切り替わる。入力途中の値が別の CanvasData に書かれることは無い（遅延確定の欄は切り替え前の `owner` に書く）ので実害は無いが、CHANGELOG の「入力欄にフォーカスがある間は編集対象を切り替えない」は実際より強く読める。クラス名への依存は、実要素を作るテストで固定されているので許容できる。
- **直し方の案**: CHANGELOG / docs/43 の文言を「このウィンドウにフォーカスがあり、入力欄を編集している間に（ショートカット等で）選択が変わっても切り替えない」に限定する。
- **確度**: 確認済み（コード読み）

### GD-R-08. 【PR #125 D】右クリックの MaterialData 作成の細部（「既存」の数え方・`BeginBatch` の例外・警告の拾い方）

- **場所**: `Editor/Creation/SourceDataCreation.cs:143-193, 215-243, 284-314, 333-338`（`2c69019`）
- **細部**: (1) 既存の Data を**更新した**（`Report.Updated`、Common の差分を書き戻した）場合も「既存 N 件はそのまま開きます」と数えて出す。「そのまま」は不正確。(2) `BeginBatch` は `try` の外なので、`BeginBatch` 自身が例外を出すと `EndBatch` が呼ばれない（次の `BeginBatch` が作り直すので実害なし）。(3) 警告の行は `"警告"` の接頭辞で拾う。3 か所の書き方が変わると黙って出なくなる。(4) `CreateOverride` を `CreateFromSelection` 以外から呼ぶと静的な Report に溜まる（次の `BeginBatch` で捨てられる）。
- **直し方の案**: (1) 集計を「新規 / 更新 / 変更なし」の 3 つにする（`LastCreateWasExisting` の代わりに Report の差分で数える）。(2) `BeginBatch` の呼び出しも `try` の中へ。(3) `MayaMaterialImporter.Report` に `Warn(string)`（警告専用の一覧）を足し、接頭辞での判別をやめる（Editor の内部なので互換の問題なし）。
- **確度**: 確認済み（コード読み）

### GD-R-09. 【U-29a】意図して置いた「空の行」（子の演出を止めるための上書き）も確認なしで取り除く

- **場所**: `Editor/Canvas/CanvasEmbeddedEditing.cs`（`IsDefaultFx`・`ConfirmOverrides`・`ApplyCleanup`。`33b79f2` の 316-560 行付近）
- **細部**: docs/07 の規則 A では、親の空の行も要素単位で子に勝つので、「子の ElementFx をこの親の中でだけ止める」手段として空の行を置ける。U-29a はそれを「自動収集されただけの行」とみなして確認なしで消す（ステータスに件数が出る・Undo で戻せる）。また、RootPath 欄で正規化後に同じ値になる入力（`OptionRoot/` → `OptionRoot`）でも整理と確認が走る。
- **直し方の案**: 確認ダイアログを出すときは「既定のままの行 N 件も取り除きます」に加えてパスを数件並べる（現状は件数だけ）。設定のある行が無く既定の行だけのときも、件数が一定以上（例 3 件以上）なら 1 回確認する、または docs/07 に「空の行で子を止めたい場合は、登録の後に置く」と書く。RootPath が変わらないときは何もしない。
- **確度**: 確認済み（コード読み）

### GD-R-10. 【U-29b】Undo / Redo で流れている要素の値が戻っても、Idle を止めるときの復元が控えの値で上書きする

- **場所**: `Editor/Canvas/CanvasEditorWindow.cs:259-276`（`RefreshAfterUndoRedo` は Idle に触れない）、`Editor/Canvas/CanvasIdleFlow.cs:212-227`（`Signature` に `EaseOverride` / `Se` が入っていない）
- **細部**: 選択していない要素の値が Undo / Redo で変わる（Undo の履歴に選択の変更が入らない操作・複数の要素をまとめて変えたツール操作の取り消しなど）と、その要素の Idle は控えた（Undo 前の）値を基準に流れ続け、止めたとき・保存の直前に控えの値へ戻す → Undo が**黙って取り消され**、その値で保存される。選択の変更も Undo に入る通常の操作では、Undo で要素が再選択されて先に一時停止するので起きにくい。あわせて、`Signature` が Ease の上書きを見ないので、流している間に Ease を変えても作り直されない。
- **直し方の案**: `RefreshAfterUndoRedo`（または `OnUndoRedoPerformed`）で `StopIdleFlow()` を呼ぶ。次の確認（0.25 秒）で Undo 後の値を控え直して自動で再開する（既存の仕組みで足りる）。`Signature` に `EaseOverride` の 4 欄を足す。
- **確度**: **推定**（Undo の履歴に選択が入る条件に依存。コードでは「Undo 後に控えを取り直さない」ことを確認済み）

### GD-R-11. 【PR #122】`RefreshAfterUndoRedo` は入力途中の欄を作り直す

- **場所**: `Editor/Canvas/CanvasEditorWindow.cs:259-276`
- **細部**: `Undo.undoRedoPerformed` はどのウィンドウの Undo でも来る。埋め込み欄・ElementFx 一覧を作り直すので、RootPath 欄（遅延確定）に打ちかけの文字は失われ、作り直す範囲によってはスクロール位置・折りたたみ状態も戻りうる。Undo の頻度からすると実害は小さい。
- **直し方の案**: 作り直す前に、フォーカス中の要素が入力欄（`IsTextInputElement`）なら埋め込み欄の作り直しだけ次のフレームへ遅らせる、またはスクロール位置を控えて戻す。現状維持でもよい（docs/09 に一言）。
- **確度**: **推定**（UI Toolkit の作り直しの挙動からの推定。実機未確認）

### GD-R-12. 【N-8】判定の穴（observe の「送信者」扱い・`-JudgeOnly` の部分的なログ・netKey の重複・未対応の値）

- **場所**: `Runtime/Net/NetCheckCutsceneJudge.cs:345-352, 405-415`、`Tools/CI/NetCheckCutscene.ps1:108-113, 152-156, 178-219`、`Runtime/Ngo/NetCheck/NetCheckRunner.cs:240-241`
- **細部**: (1) 受信ログ（`cutscene_recv`）が無いハンドルは送信者として判定する。observe のプロセスで送信者はありえないのに、`expect-plays` を付けない observe（`cut_latejoin` の遅れて参加する Client）では、本体の受信ログが出ない（文言の変更・非開発ビルド）と、s の判定・無音の件数の判定が丸ごと飛んで PASS しうる。(2) `-JudgeOnly -Logs` は渡されたログだけを見る。PC 1 台分を渡し忘れても PASS、trigger のログが無ければ netKey の突き合わせも黙って飛ぶ。(3) 同じ netKey の受信が 2 回あっても、件数の判定が無いと検出しない。(4) `-ddrive-cutscene-test` に未対応の値を付けると、Cutscene のシナリオは無効なのに latejoin の Presentation の判定が外れる。(5) 本体の受信ログは `{elapsed:F3}`（現在のカルチャ）で、小数点がカンマのロケールでは s が整数部だけに読まれる（既存のログ、日本語 Windows では問題なし）。
- **直し方の案**: (1) role が observe のとき、受信ログの無いハンドルは `unmatched_play`（FAIL）にする。(2) `-JudgeOnly` に `-Expect trigger=1,observe=3` のような件数の指定を足すか、trigger のログが 1 本も無ければ FAIL。(3) 受信者の netKey の重複を FAIL にする。(4) `_requireLateJoinRestore` の条件を `_cutMode == null` に揃える（`SetupCutsceneTest` の後で決める）。(5) 本体のログを `ToString("F3", CultureInfo.InvariantCulture)` に（Editor / 開発ビルドのログのみ・文面は不変）。
- **確度**: 確認済み（コード読み）

### GD-R-13. 【N-8】`NetCheckCutsceneMarker` が持ち込み先の Timeline のマーカーのメニューに出る

- **場所**: `Runtime/Ngo/NetCheck/NetCheckCutsceneMarker.cs:15-26`
- **細部**: `autoReferenced` のランタイムのアセンブリにある public の `Marker` 派生なので、NGO を入れている持ち込み先（MS2026）のデザイナーの Timeline の「Add Marker」メニューに `NetCheckCutsceneMarker` が出る。誤って置くと、`Fired` を購読する者がいないので何も起きない（害は小さいが紛らわしい）。`public static Action Fired` は誰でも書き換えられる。あわせて、docs/14 §23 と `NetCheckRunner` のコメントの定数名 `CUTID.CUT_NetCheck_Markers` は、生成された名前（`NetCheckMarkers`）と違う。
- **直し方の案**: クラスに `[UnityEngine.Timeline.HideInMenu]` を付ける（シリアライズ・型名は不変なので既存のアセットに影響なし）。`Fired` は `event` にする（購読側の書き方は変わらない）。コメントと docs の定数名を直す。
- **確度**: 確認済み（コード読み。メニューの表示は Timeline の仕様からの推定）

---

## PR #125 をマージしてよいかの判定

**マージしてよい。マージ前に直すべき指摘は無い**。

- マージ前に必要なのは、`docs/52` の 1 か所の衝突の解消（4-4 は PR 側・4-5 は main 側を採る）と、マージ後の EditMode / PlayMode の全件の再実行（PR のテストは N-8 を含まない `fb685b1` ベース）だけ。
- **マージ後・v1.4.0 のタグ前に直すもの**: GD-R-01（Edit Mode のカメラ。A の修正で初めて生きる経路）。小さいので GD-R-04（例外の隔離）・GD-R-06（テストの `SetUp`）も同じ PR で直すとよい。
- マージ後で v1.4.x でもよいもの: GD-R-05・07・08。
- PR 本文の「判断が要る点」への所見: (1) D の自動テストが無い → GD-R-08 の (3) を入れるなら、Report の警告の一覧だけは単体テストできる。(2) 「…は未対応のため DDrive/Lit として変換します」も Warning で出る → 利用者が選んだ結果の報告で、Warning の量も 1 操作 1 件ずつなので妥当（Info にするなら `Report` の種類を分ける GD-R-08 (3) の後）。(3) 追加した public 2 件 → Editor の public で互換面の外、問題なし。(4) DesignerManual は後追いで可。(5) Edit Mode の SE / VFX / UI / Camera・Shake / Haptic の未確認 → Camera は GD-R-01 を直してから確認するのがよい。

## N-8 の公開面の扱いの推奨

**現状のまま「追加のみ」として受け入れる**（上の「公開面（+47 行）の扱い」）。internal 化はプロジェクトの方針（InternalsVisibleTo を置かない）と前例（`NetCheckJudge` 一式が v1.3.1 から public）から見て不釣り合い。Ngo 側への移動はテストの構成変更のコストが大きい。今後 NetCheck 専用の型を増やさない方針を docs に一言。`NetCheckCutsceneMarker`（スナップショット対象外の Ngo 側）は GD-R-13 の `[HideInMenu]` を勧める。

## 確認して問題なしだった観点

- 互換: `9f40cbb..33b79f2` のスナップショット 4 ファイルは `+178 / −0`（削除・変更 0 件）。N-8 の `+47` は CHANGELOG の「NetCheck の確認道具の公開面」の列挙と一致し、余計な public は無い。PR #125・U-29・PR #122 はスナップショットを変えない。
- PR #125 A: Context の寿命（ドメインリロード・シーン切替・プレハブステージ・Play Mode・開き直し・別の Cutscene・外部要因の破棄）、重複排除、静的な状態の後始末。修正ラウンド 1〜7 の Edit Mode の規則が実経路で働くこと。追加テストの実効性。Play Mode の Manager と混ざらないこと（専用 Registry / Pool、Play Mode 中は停止、突入時に片付け）。
- PR #125 細部: `AssetRegistry` の追記は 1 ID 1 回・定常経路の割り当てなし・既存テストと NetCheck の文字列一致に影響なし。`ChildName` の null 安全。
- U-29a: `IsDefaultFx` の網羅とテストでの固定、配下の判定（区切り・ルート除外・入れ子・重なり）、3 択、キャンセルで何も変えない（欄の値も戻す）、Undo 1 グループ、設定のある行を確認なしで消す経路が無い、配線は消さない、テスト用の差し替え口の後始末。
- U-29b: 保存（Ctrl+S・自動保存・閉じるときの保存）・閉じる・切り替え・Play Mode・ウィンドウを閉じる / ドメインリロードの全契機での復元、`ElementFxStateSnapshot` が UiTween の書く全欄を控える、選択中の要素の取り違えなし、▶ 再生との排他、実行時と同じ優先規則、ADR-4 との整合、負荷。
- PR #122: 🔒 の状態遷移、`ForbiddenApiRowText` の省略表示の範囲、public のテスト用メンバー（互換面の外）。
- N-8: 既存 9 シナリオ・実機テストの挙動が変わらない（`-ddrive-cutscene-test` 無しで新しいコードは動かない、`Run-NetCheck.ps1` の既存シナリオの起動引数は不変）。期待集合・二重発火・Late Join・Host 引き継ぎの判定。シナリオの実行で一部のプロセスのログが欠けたら FAIL。asmdef の Timeline 参照は持ち込み先のビルドに影響しない。確認用データと ID 定数はパッケージ外。

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・NetCheck・Edit Mode のプレビュー・プレハブモードの Idle）。PR #125・U-29・N-8 の「green」「PASS」の報告は**未確認**。
- Unity の挙動に依存する推定: `Object.FindObjectsByType` が DontSave を返さないこと（テストのアサートと Unity の仕様の記述から）、`GameObject.Find` が DontSave の Director を見つけること（既存の片付けがこれに依存）、Undo を通らない Transform の書き込みでシーン / プレハブステージが dirty になるか、プレハブモードの自動保存の契機、入れ子 Prefab の上書きの見え方と Apply の対象（GD-R-02）、Undo の履歴に選択の変更が入る条件（GD-R-10）、Timeline の「Add Marker」メニューの列挙（GD-R-13）、DontSave の親の下に Spawn したモデルが保存から外れるか（docs/45 P1-5 の範囲。今回は変わっていない）。
- `CanvasEditorWindow` の U-29 の差分（+386 行）は Idle・埋め込み欄・後始末の契機の範囲だけを読んだ。`CanvasIdleFlowTests` / `CanvasOverrideCleanupTests` はテスト名と主要な数件だけ。DesignerManual（canvas-editor.html）の U-29 / PR #122 の差分は読んでいない。
- `NetCheckRunner` の N-8 以外の部分、`Run-NetCheck.ps1` の既存シナリオの部分、`docs/29` §27 の手順の全文（R1〜R5 の起動コマンドの `expect-plays` の有無だけ確認）。
- docs/43・docs/52 の記入は抜き取り（各節の数行）。
- M-6 の作業ツリー（メインの checkout）の中身。上の「M-6 への所見」はファイル名の対応と `.playable` の内容からの所見のみ。
- メインの checkout と、そこで開いている Unity には触れていない。
