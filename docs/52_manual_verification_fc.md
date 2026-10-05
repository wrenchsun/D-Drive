# 52. FC チケット（T-Drive 連携）の人による確認手順

関連: [51_tdrive_integration.md](51_tdrive_integration.md)（設計・FC チケット本体） / [11_tasks.md](11_tasks.md)（FC 節） / [43_manual_verification_2026-09-17.md](43_manual_verification_2026-09-17.md)・[28_manual_verification_phase5.md](28_manual_verification_phase5.md)（同じ書式の手順書）

> FC チケット（T-Drive 連携のための D-Drive 側の追加、FC-1〜FC-20）の**人による確認**をチケットごとにまとめる手順書。
> 自動検証（コンパイル 0 エラー・EditMode / PlayMode 全件 green）は各チケットの PR で通している。**見た目・操作・Inspector の出し分け**は人が確認する。
> 実装済みのチケットだけ手順を書いてある。未実装のチケットは枠だけ置いてあり、**実装した担当が自分のチケットの節を埋める**（書式は FC-1 の節に合わせる）。
> 不具合を見つけたら、該当チケット行（[11](11_tasks.md) FC 節）と [51](51_tdrive_integration.md) の該当節「実装メモ」を参照して修正する。

## 0. 確認の進め方

- **所要時間の目安**: FC-1 = 約 25 分 / FC-2・FC-12 = 約 15 分 / FC-11 = 約 15 分（実装済み分の合計 約 55 分）
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

[51] §4.4、[26](26_timeline.md) §4.6.5、[42] §5.14（E-18）、ProgrammerManual `extending.html#viewcamera`。D-Drive 単体には使い手（視点を読む機能）が無い公開 API なので、**目視で確認するのは確認用シーンでの 1 項目だけ**。以下は**自動テストで確認済み**:

- PlayMode `ExternalContractViewTests`（5 件）: `E18_NoCamera_ReturnsFalse_WithoutWarning`（カメラ無しで `false`・`Source = None`・警告なし）/ `E18_MainCamera_ReturnsWorldValuesUnchanged`（位置・回転・縦画角が `Camera.main` と無変換で一致）/ `E18_ExternalProvider_ResolvesSplitScreenBySubject_PriorityAndUnregister`（外部アセンブリの `IViewProvider` の `subject` 振り分け・優先度・`Unregister`）/ `E18_TryGetCurrent_AllocatesNothing`（割り当て 0）/ `E18_CutsceneOwnsCamera_ReturnsCutSource_FromLaterLateUpdate`（カットシーン所有中は `Source = Cutscene` で姿勢が `Camera.main` と一致。実行順 1000 より後の LateUpdate から読むとそのフレームのカット姿勢）
- PlayMode `ViewCameraTests`（6 件）: 同優先度の登録順・再登録 / プロバイダの例外隔離 / 破棄済みプロバイダの除去 / `TryGetView` 内からの登録 / 正射影カメラで `fieldOfView` をそのまま返す / 実 `CutsceneManager` が駆動中の `Source = Cutscene`・カットの進行への追従・終了後に `MainCamera`
- EditMode `ViewCameraEditModeTests`（1 件）: Edit Mode（Timeline ウィンドウのスクラブ中）でも `Camera.main` の現在の姿勢を `MainCamera` で返す

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 3-1 | `CutsceneData` の Inspector の「▶ Cutscene確認用シーンを開く」で開く確認用シーン（[43] §7）で Play し、Camera クリップを持つ `CutsceneData` を再生する。再生中に Console へ `ViewCamera.TryGetCurrent` の結果を出す小さなテスト用コンポーネント（`[DefaultExecutionOrder(1001)]`、`LateUpdate` で呼ぶ）を `Camera.main` に付けておく | カットの区間中は `Source = Cutscene` で、位置・回転が Scene ビューの `Camera.main` と一致する。カットが終わると `Source = MainCamera` に戻る。**エラー・警告は出ない** | □ 未 |

**T-Drive 導入後に確認**: [52] 末尾「T-Drive 導入後に確認」の FC-3 の項。

**要判断（FC-3）**: なし（U-4 は決定済み。[51] §8）。

## 4. FC-4: 外部パッケージのマーカーの汎用の受け口

[51] §4.5、[26](26_timeline.md) §4.3 / §4.4、[42] §5.14（E-20）。外部パッケージが `Marker` + `ICutsceneMarker` を実装するための汎用の受け口で、D-Drive 単体には使い手（外部のマーカー）が無いため、**目視で確認する機能は無い**。以下は**自動テストで確認済み**（外部アセンブリ相当のダミー `ExternalPackage.Fake.ExternalFireMarker` を使用）:

- PlayMode `ExternalContractMarkerTests`（7 件）: `E20_ExternalMarker_FiresOncePerCrossing_WithContext`（跨いだ Tick で 1 回・文脈の中身・巻き戻しで再発火しない・`ICutsceneMarker` を実装しない外部マーカーは無視）/ `E20_ExternalMarkers_CrossedInOneTick_FireInTimeOrder` / `E20_SeekAndSkip_AreSilent` / `E20_FireEnabledFalse_DoesNotFire` / `E20_ThrowingMarker_IsIsolated_OthersAndTickContinue` / `E20_CrossingExternalMarker_AllocatesNothingBeyondPlainTick` / `E20_LateJoin_PastMarkersAreSilent_FutureOnesFireLocally`
- EditMode `ExternalContractMarkerEditModeTests`（3 件）: Edit Mode のプレビューで `IsEditPreview = true`・再生中に 1 回 / スクラブは無音・再生開始の立ち上がりで再発火しない / 巻き戻しは無音
- 既存 4 種のマーカー（Event / Signal / Shake / Haptic）の発火は、既存の Cutscene テスト（EditMode / PlayMode 全件 green）で不変を確認済み

**回帰確認（人、任意）**: 既存のカットシーンのマーカーがこれまでどおり動くことを、確認用シーンで 1 回見る。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 4-1 | Event / Signal / Shake / Haptic のいずれかのマーカーを置いた `CutsceneData` を、Timeline ウィンドウで開いて再生する（`CutsceneDataEditor` の「▶ Timeline ウィンドウで開く」） | 再生中にマーカーの時刻を跨ぐと従来どおり発火する（Shake / Haptic / Event。Signal は Edit Mode ではコンソールに `Cutscene Signal (Edit Mode プレビュー)` が出る）。スクラブでは発火しない。**エラー・例外は出ない** | □ 未 |

| 4-2 | （2026-10-03 追記、レビュー FC-R-03）Signal マーカーを **0 秒ちょうど**に置いた `CutsceneData` を Timeline ウィンドウで開き、再生位置を 0 に戻して再生する。続けて、再生位置を途中（例: 2 秒）へスクラブしてから再生する | 先頭から再生したときは 0 秒のマーカーが再生の最初に発火する（Signal はコンソールに `Cutscene Signal (Edit Mode プレビュー)` が 1 回出る）。スクラブしただけでは発火せず、途中からの再生でも 0 秒のマーカーは出ない。Play Mode で `Cutscene.Play` したときも、最初の Tick で 0 秒のマーカーが 1 回発火する（Event / Signal / Shake / Haptic いずれも） | □ 未 |
| 4-3 | （同上、FC-R-04）Timeline ウィンドウの**上端のマーカー領域**（トラックではなく、時間軸の上の帯）に Signal マーカーを置いて再生する | トラック上に置いたときと同じように発火する（自動テストでも `markerTrack` が収集されることを確認済み） | □ 未 |

| 4-4 | （2026-10-04 追記、修正ラウンド 3、FX-R-01 / FX-R-04）**Edit Mode の先頭判定**: 0 秒と 0.04 秒に Signal マーカーを置いた `CutsceneData` を Timeline ウィンドウで開く。(a) 再生位置を 0 にして再生する（数回繰り返す。毎回、再生位置を 0 に戻してから）。(b) 再生位置を **0.05 秒付近**へスクラブしてから再生する。(c) （修正ラウンド 4、FY-R-05）2.0 秒ちょうどと 2.01 秒（最初の更新で進む範囲）に Signal を置き、再生位置を 2.0 秒へスクラブしてから再生する。続けて一時停止 → 再開（再開位置のすぐ後〔0.01 秒後など〕にもマーカーを置く）も試す | (a) 毎回、再生の最初に 0 秒のマーカーが **1 回**発火する（コンソールに Signal が出る）。(b) 0 秒・0.04 秒のマーカーは**発火しない**（途中からの再生）。どちらも更新の間隔や PC の重さで結果が変わらない。(c) 再生を始めた位置（2.0 秒）ちょうどのマーカーは**出ず**、再生を始めてすぐの 2.01 秒は **1 回**出る（最初の 1 フレーム分を飛ばさない）。一時停止からの再開も同じ（再開位置ちょうどは出ず、そのすぐ後は出る）。自動テストでも確認済み（`ExternalContractMarkerEditModeTests.E20_EditPreview_ScrubThenPlay_*` / `ResumeFromPause_*`） | □ 未 |
| 4-5 | （2026-10-04 追記、修正ラウンド 3・4、FX-R-01 / FY-R-02）**ネットの構成**（2 台: PC-A Host + PC-B Client、できれば [29] §25 の 4 台: A Host / B・C Client / D Client。遅延 200ms 設定。[29] の手順）。0 秒・0.1 秒・0.4 秒・0.6 秒に Signal（または SE）マーカーを置いた Cosmetic の `CutsceneData`（PredictLocal 有効）を使う。(a) **Host の操作**で再生し、Host と Client の両方で見る。(b) **Client の操作**で再生し、**別の Client**（Client → Host → Client の 2 区間）でも見る。`NetDebugOverlay` 等で、受信した端末の**開始位置（`NetworkTime − StartNetTime`）が 0.5 秒にどれだけ近づくか**を記録する（遅延 200ms で 0.4〜0.5 秒台になりうる）。(c) 再生開始から **0.5 秒より後に途中参加**した端末（3 台目、または一度切断して再接続した Client）で見る。(d) [29] §25 の `host_migration` シナリオの**後**に同じカットシーンを再生し、二重に鳴らない・鳴り損ねないことを見る | (a) Host（送信側）と Client（受信側）の**両方**で、0 秒と 0.1 秒のマーカーが 1 回ずつ鳴る（遅延 200ms でも Client が鳴らないことがない）。(b) 送信した Client・Host・別の Client で、開始位置が 0.5 秒以内の端末は 0 / 0.1 / 0.4 秒が全部鳴る。**0.5 秒を超えた端末は、遡って 0.5 秒以内のマーカーだけ鳴り（例: 開始位置 0.7 秒なら 0.4 / 0.6 秒。0 / 0.1 秒は鳴らない）、全部が無音にはならない**（0 秒のマーカーは遅延が 0.5 秒を超えた端末では鳴らない = 仕様）。記録した開始位置と鳴ったマーカーが [14] §22 の表と合う。(c) 途中参加した端末では、参加時点から遡って 0.5 秒以内のマーカーだけ鳴り、それより前（開始直後のマーカー）は鳴らない。(d) `host_migration` 後も、再生したカットシーンのマーカーが各端末で 1 回ずつ鳴る（古いカットシーンが再送されて鳴り直さない）。ログに破棄・警告が出ない。**(e)（2026-10-05 修正ラウンド 5、FZ-R-10）**開発ビルド / Editor のログに、受信した端末ごとに 1 行 `[Net/Host|Client] Cutscene: 受信した再生の開始位置 s=… 秒(NetworkTime − StartNetTime)・猶予(0.5 秒)を超えて無音にしたマーカー n 件(HandleNetKey=…)` が出る。**この行の `s` を各端末の開始位置として記録し**、鳴ったマーカーの組と [14] §22 の表（`s − マーカーの時刻 > 0.5` のものだけ無音）が合うこと、`n` が無音にしたマーカーの数と一致することを見る **（2026-10-06 追記、N-8）この確認は NetCheck の `cut_*` シナリオと [29] §27（R1〜R5 の起動コマンド・ログの抜き出し・`Run-NetCheck.ps1 -JudgeOnly` での判定）を使う。**メモ: ローカル複数プロセス（実 NGO・127.0.0.1）では PASS（2026-10-06。[14] §22・§23） | □ 未（自動テストは `CutsceneNetMarkerSymmetryTests` で遅延 0ms / 200ms・Client → Host → 別の Client の 2 区間・Late Join・Skip / Seek・予測再生を止めた後の再生し直し防止を確認済み。実機は要確認） |
| 4-6 | （2026-10-06 追記、M-6）**Player ビルドでカットシーンのトラック / マーカー / クリップが読み込まれる**。NetCheck の開発ビルドで `cut_local` を実行する（`Tools/CI/run-ci.cmd` の 8 段目、または `pwsh -File Tools/CI/Run-NetCheck.ps1 -OnlyScenario cut_local`）。持ち込み先（MS2026）では、更新後に「更新を適用」（マイグレーション `cutscene-timeline-monoscript-v1`）を実行してから、自分のカットシーンを含む開発ビルドを作り、Player.log に `referenced script … missing` が出ないことを見る | `[NetCheck] cutscene_timeline ok=1 signal=1 … missing=none`（Event / Signal / Shake / Haptic / SE / VFX / UI / Camera / Presentation / AnchorGroup の全トラックが読めている）。`RESULT=PASS`。持ち込み先の `.playable` に `m_Script` の行だけの差分が出る | □ 開発リポジトリは NetCheck の `cut_local` で自動確認済み（2026-10-06、全 15 シナリオ PASS）。持ち込み先（MS2026）は未 |

**T-Drive 導入後に確認**: [52] 末尾「T-Drive 導入後に確認」の FC-4 の項（T-Drive 側で `ICutsceneMarker` を実装したマーカーを使うとき）。

## 5. FC-5: カットシーン取り込み完了の公開イベント（Editor）

[51] §4.6、[26](26_timeline.md) §5.2 の 7、[42] §5.9・§5.14（E-19）。**自動テストで確認済み**（EditMode `ExternalContractCutsceneListenerTests` 8 件 + `EditorContractSnapshotTests`。合成 FBX と外部アセンブリ相当のダミーリスナーで通す）: 外部アセンブリのリスナーが発見される・`Order` 昇順（同値は型名順）・既定では何もしない・例外を投げても後続のリスナーと取り込みが止まらない・ショット 1 つにつき 1 回呼ばれ `Result`（ショット名・カテゴリ・`IsNew`・Data・Timeline・SourceFbxGuids 確定済み）が正しい・リスナーが足した Binding とトラックがディスクに保存される・再取り込みで消えず重複しない（`IsNew = false` で再度呼ばれる）・`ScanAll`（手動の再取り込み）でも呼ばれる・UnityChan の FBX で `Roles` のキャラの役（`RoleName` / `ModelIdentifier` / `Kind` / `Track` / `SourcePath`）が正しい（`DevRepoOnly`）。リスナー本体は T-Drive が入るまで D-Drive 単体には無いので、**目視で確認する機能は無い**。以下は「既存の取り込みがこれまでどおり動く」ことだけを人が確かめる（回帰確認）。

準備: `Assets/SourceAssets/Cutscene/<カテゴリ>/` に Maya の FBX のセット（`<ショット>.fbx` と `<ショット>__<Model識別子>.fbx`）がある状態。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 5-1 | メニュー `Tools > D-Drive > Generate > SourceAssets/Cutscene からインポートルールを再実行` を実行する | Console に `[DDrive] Cutscene 取り込み(再実行): CutsceneData 新規 0 / 更新 N …` が出る。**エラー・例外は出ない**。既存の `CutsceneData` の Bindings・デザイナーが足したトラックはそのまま残る | □ 未 |
| 5-2 | 同じメニューをもう一度実行する | 結果は 5-1 と同じ（トラック・Bindings が増えない） | □ 未 |

**T-Drive 導入後に確認**: [52] 末尾「T-Drive 導入後に確認」の FC-1 / FC-5 の項（fctrack の取り込みがリスナーで Facial トラックと `SameAsTrack` の binding を足し、再取り込みでも消えず重複しない）。

## 6. FC-6: 取り込みルールの外部拡張 / 不明な種別フォルダの扱い

[51] §4.7、[09] §1.1、[42] §5.9・§5.14（E-21）。**自動テストで確認済み**（EditMode `ExternalContractImportExtensionTests` の `E21_*` 10 件 + `EditorContractSnapshotTests`。外部アセンブリ相当のダミーが、テスト中だけ static フラグで名乗る）: 外部 `IImportRuleHandler` が組み込み 9 件の後ろに載る（組み込みの順序は不変・何も名乗らなければ 9 件のまま）・外部フォルダのファイルから Data が作られ案内ログが出ない・再取り込みで二重生成しない・拡張子違いは従来どおり案内・組み込みの種別フォルダ / `Cutscene` を名乗る外部ハンドラは警告 1 回 + 無視・外部ハンドラ同士の取り合いは型名の早い方・`Configure` / `LoadSource` の例外で取り込みが止まらない（他のファイルは作られる）・`IImportRuleFolderOptOut` で宣言したフォルダは案内が出ず宣言しないフォルダは従来どおり出る・空 / null / 空白 / 区切り入り / 重複の宣言を無視・組み込みの `Shaders` / `Cutscene` は静か。外部拡張は T-Drive が入るまで D-Drive 単体には無いので、**目視で確認する機能は無い**。以下は「既存の取り込みがこれまでどおり動く」ことの確認。

準備: 通常の D-Drive プロジェクト（Test Runner のダミーは名乗らないので影響しない）。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 6-1 | `Assets/SourceAssets/Se/Test/` に wav を 1 つ置く | これまでどおり `SeData` が `Assets/GameData/Audio/SE/Test/` に作られる。Console に ImportRule の警告・エラーは出ない | □ 未 |
| 6-2 | `Assets/SourceAssets/NotAKind/x.wav` を置く | Console に `[DDrive] ImportRule 案内: 'NotAKind' は種別フォルダではありません(対応フォルダ: Se / Bgm / … / Vfx …)` が 1 回出る（従来どおり。D-Drive 単体では `Facial` も同じ扱い） | □ 未 |
| 6-3 | メニュー `Tools > D-Drive > Generate > SourceAssets の既定フォルダを作成` を実行する | 組み込み 9 種別 + `Cutscene` のフォルダと README ができる（外部ハンドラがあればそのフォルダも）。エラーは出ない | □ 未 |

**T-Drive 導入後に確認**: [52] 末尾「T-Drive 導入後に確認」の FC-6 の項（`SourceAssets/Facial/` に置いても案内ログが出ない）。

## 7. FC-7: 依存関係の追跡が Timeline クリップ内の参照まで届く

[51] §4.8、[09] §10。自動テストで確認済み（EditMode `DependencyGraphTimelineTests` 9 件）: SE クリップ・Shake マーカー・Presentation クリップの `AssetId` が使用箇所に出る（`UpdatePaths_PlayableWith*`）/ `.playable` の変更・削除で索引が更新される（`UpdatePaths_PlayableChangedAndDeleted_UpdatesIndex`）/ Cutscene の Timeline からだけ参照されている SE が未使用にならず、参照の無い SE は従来どおり未使用（`FindUnusedIds_SeReferencedOnlyFromTimeline_IsNotUnused`）/ `.playable` から `CutsceneData` を引ける（`FindCutscenePathsUsing_*`）/ 古いキャッシュ版でも `.playable` が補完される（`EnsureLoaded_WithOutdatedCacheVersion_BackfillsPlayables`）/ `.playable` の分類（`ClassifyPath_Playable_IsTimeline`）。以下は UI の目視。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 7-1 | SE を使うクリップ（SE トラックの `CutsceneSeClip`）を持つ Timeline の `CutsceneData` を用意する（確認用に作ったものでよい）。`Tools > D-Drive > Generate > 依存関係グラフを再構築` を実行する | Console に「DependencyGraph: 再構築完了」が出る | □ 未 |
| 7-2 | Asset Browser でそのクリップが使っている SE を右クリック →「使用箇所を表示」 | 参照元に `.playable` のパスが出て、行の横に `(Cutscene: CUT_xxx)` が付く。`SeId` の項目名とトラック名 / クリップ名が見える。行をダブルクリックすると Project ウィンドウでその `.playable` が選ばれる | □ 未 |
| 7-3 | ツールバーの「未使用...」を開く | 7-2 の SE は一覧に**出ない**（Cutscene から使われているため）。どこからも使われていない別の SE は従来どおり出る | □ 未 |
| 7-4 | 7-2 の SE を右クリック →「削除...」 | 削除ウィンドウの参照元に「Timeline」の欄が出て、`.playable` が外部参照として載り、そのまま削除は進められない（「参照を差し替えてから削除」でも Timeline の中は自動では変わらず、結果画面の「手動で直す」一覧に残る）。確認だけにしてキャンセルで閉じる | □ 未 |
| 7-5 | Timeline ウィンドウでクリップの SE を別の SE に変えて保存し、7-2 を別の SE と元の SE の両方で見る | 元の SE の使用箇所から `.playable` が消え、新しい SE に出る（再構築なしで更新される） | □ 未 |

**T-Drive 導入後に確認**: [52] 末尾「T-Drive 導入後に確認」の FC-7 の項。

## 8. FC-8: カットシーンのキャラ FBX でブレンドシェイプのカーブを通す（保留）

未実装（実装時に追記）

## 9. FC-9: デバッグ / 調整（7-3 / 7-4）との接続（保留）

未実装（実装時に追記）

## 10. FC-10: 外部拡張の契約テスト

**目視で確認する項目は無い**。契約は全て自動テストで固定済み（[42](42_distribution.md) §5.14 の表。`Tests/Runtime/ExternalContract/`・`Tests/Editor/ExternalContract/`）:

- E-1 / E-13（Prefab 上の外部コンポーネントのプール往復・Slots の空 / 無効 ID）: `ExternalContractModelTests`
- E-2 / E-3 / E-5（外部 Track・Clip・Marker の評価・シーク・一時停止中は Evaluate されない・検証 0 件）: `ExternalContractTimelineTests`
- E-4 / E-7 / E-17（再取り込みで外部トラック・Binding が残る・未知の拡張子 / フォルダで例外なし・取り込みがボーン / シェイプ名 / スケールを変えない）: `ExternalContractImportTests`（E-4 は `DevRepoOnly`）
- E-6（外部アセンブリの `IValidator` が発見される）: `ExternalContractValidatorTests`
- E-8 / E-9（Update と外部 LateUpdate の書き込みが衝突しない・外部 `IAssetManager` が `GameLoop` に駆動される）: `ExternalContractLoopTests`
- E-10 / E-11 / E-12 / E-15（`_Toon*` の Specific が Material へそのまま書かれる・予約名と衝突しない・Merge が登録する）: `ExternalContractMaterialTests` / `ExternalContractMaterialNamingTests`
- E-14 / E-17（静的）: `ExternalContractStaticScanTests`

実際の確認は Test Runner（EditMode と PlayMode の両方で `ExternalContract` を検索して全件 Pass）。実 FBX（UnityChan 等）・実 T-Drive での確認は「22. T-Drive 導入後に確認」へ。

結果: ☑ 自動テストで確認済み（EditMode 16 件・PlayMode 14 件）

## 11. FC-11: MaterialData にパスの無効化・キーワードの欄の確認

[51] §4.12（実装メモ付き）、[06](06_material_texture.md) 2026-10-03 追記。**Unity が影などのパスを実際に止めるかの目視と、Material Editor の操作感**を確認する。

自動テストで確認済み（再確認不要）: `MaterialPassKeywordTests`（`DisabledPasses` で指定したパスだけが無効になる・指定なしは従来どおり・存在しない名前 / 空文字は無視して例外なし・大文字小文字を区別しない・`EnabledKeywords` が有効になり Common のキーワードを壊さない・`FadeTo` の一時 Material と完了後の共有 Material の両方で保たれる・`new Material(from)` はパスの状態とキーワードを引き継ぎ `Lerp` は触らない・パス / キーワードの列挙・Validator の Warning / Info）、`LegacyAssetFixtureTests`（旧版のアセットが警告 0 で読める）、`SerializedLayoutSnapshotTests`。

### 11.1 Material Editor の「Passes / Keywords」欄

準備: `Tools > D-Drive > Editors > Material` で Material Editor（`MaterialEditorWindow`）を開き、`ShadowCaster` パスを持つシェーダー（`DDrive/Lit` など）を設定した MaterialData を選ぶ。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 11-1 | MaterialData を選ぶ（TextureData を選ぶと欄が消えることも確認） | ウィンドウの Inspector の上に「Passes / Keywords」欄が出る。パスの一覧に `UniversalForward` / `SHADOWCASTER` / `DepthOnly` などシェーダーのパスが並ぶ（Unity が返す綴りで、`SHADOWCASTER` のように大文字のことがある）。TextureData のときは欄が出ない | □ 未 |
| 11-2 | `SHADOWCASTER`（`ShadowCaster`）にチェックを入れる | Inspector の `Disabled Passes` に 1 件入る。Ctrl+Z で戻る（チェックも外れる） | □ 未 |
| 11-3 | キーワード欄のテキストに任意の名前（例 `_MY_FEATURE`）を入れて「追加」 | キーワードの行が 1 件増え、Inspector の `Enabled Keywords` にも入る。もう一度「削除」で消える。空欄で「追加」しても何も起きない | □ 未 |
| 11-4 | 「（候補から追加）」を開く | そのシェーダーが宣言しているキーワード（`_NORMALMAP` など）が並び、選ぶと追加される（Shader が宣言キーワードを持たなければ候補欄自体が出ない） | □ 未 |
| 11-5 | Inspector の `Disabled Passes` に存在しない名前（`NoSuchPass`）を手で足す | パス欄に「NoSuchPass（シェーダーに無い）」のチェック済み項目が出て、外せる。`Tools > D-Drive > Validation > Run All` でその MaterialData に Warning「DisabledPasses 'NoSuchPass' は…LightMode にありません」が出る | □ 未 |
| 11-6 | `Enabled Keywords` にシェーダーが宣言していない名前（`_NO_SUCH_KEYWORD`）を足し、Validation を実行 | **Info**「EnabledKeywords '…' はシェーダー '…' が宣言していないキーワードです」が出る（Warning ではない） | □ 未 |
| 11-7 | Shader を別のシェーダーに変える | パスの一覧が新しいシェーダーのものに変わる | □ 未 |

### 11.2 影が実際に消える（目視）

準備: 影を受ける床と Directional Light（影 ON）のある確認用シーン（開いて SceneView / GameView で見る。ウィンドウ内プレビューは使わない）。立方体などに、`ShadowCaster` を持つシェーダーの MaterialData を `Mats.Apply`（または `Tools > D-Drive` の確認用シーン / Material Editor の「シーンにプレビューを配置」）で適用する。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 11-8 | `DisabledPasses` が空の MaterialData を適用して Play（または配置） | 床に影が落ちる | □ 未 |
| 11-9 | `DisabledPasses` に `ShadowCaster` を入れて再生成（Material Editor の「再生成」）または再度適用 | **影が消える**（本体は描画されたまま）。URP の SRP Batcher 有効 / 無効の両方で消えるか（影が残る場合は報告。コードでは `Material.SetShaderPassEnabled` の状態が false になるところまでしか確認できていない） | □ 未 |
| 11-10 | `FadeTo` で 11-8 の Material から 11-9 の Material へフェードさせる（`Mats.FadeTo` を呼ぶ簡単なボタン等） | フェード開始直後から影が消える。フェード完了後も消えたまま | □ 未 |

**要判断（FC-11）**: なし（U-10 は決定済み。[51] §8）。11-9 で影が消えない場合は SRP の仕様の問題なので、欄の意味（「LightMode のパスを `SetShaderPassEnabled` で止める」）を [51] §4.12 に追記したうえで別の手段（RendererShadowCastingMode 等）を検討する。

### 11.3 LightMode タグの無いパス（輪郭線）を止める（2026-10-03 追記、レビュー FC-R-06）

自動テストで確認済み: `UntaggedPassRenderTests`（実描画。LightMode タグの無いパス〔赤〕と `UniversalForward` のパス〔緑〕を持つシェーダーで、`SRPDefaultUnlit` を止めるとタグ無しのパスが描かれない）、`MaterialPassKeywordTests`（一覧に `SRPDefaultUnlit` が出る・Validator が警告しない）。目視で確認するのは、T-Drive の Toon の輪郭線が実際に消えること（**T-Drive 導入後に確認**）。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 11.3-1 | LightMode タグの無いパス（輪郭線）を持つシェーダー（T-Drive の Toon 等）の `MaterialData` を Material Editor で開き、「Passes / Keywords」の候補を見る | `SRPDefaultUnlit` が候補に出る。チェックして保存した後、Console に `DD-MAT-PASS-UNKNOWN` の警告が出ない（Validation > Run All でも） | □ 未（T-Drive 導入後） |
| 11.3-2 | 上の `MaterialData` を割り当てたモデルを SceneView / Game ビューで見る | 輪郭線が描かれない（影・本体は残る） | □ 未（T-Drive 導入後） |

## 12. FC-12: モデルのスポーン / 返却の通知

§2（FC-2 と同一 PR のため同じ節）を参照

## 13. FC-13: インスタンスごとのマテリアル値（保留）

未実装（実装時に追記）

## 14. FC-14: 変換表・テクスチャ規則の提供口

[51] §4.15、[06] B-3 の 2026-10-03 追記、[42] §5.9・§5.14（E-22）。**自動テストで確認済み**（EditMode `ExternalContractImportExtensionTests` の `E22_*` 8 件 + `EditorContractSnapshotTests`。ダミーの外部提供口がテスト中だけ規則 / 表を返す）: 外部規則 0 件のとき `TryMatch` の結果が従来どおり（`_N` / `T_` 等）・外部規則（接尾辞 `_ToonMask` → sRGB オフ、接頭辞 `T_Toon`）が Profile の `T_` 接頭辞の規則より先に効く・外部規則に当たらない `T_` は従来どおり・Profile に同じ条件の規則があれば Profile 優先（条件が違う外部規則は上書きされない）・実際の `TextureImporter` に `Apply` すると sRGB がオフになり `Diff` が空・提供口の例外が隔離され他の提供口は有効（ログは 1 回）・外部の変換表が `Assets/` の表の後ろ・D-Drive 同梱の表の前に載り null / 重複は無視・`Tests` 配下の表は除外・順序が決定的。T-Drive が入るまで D-Drive 単体には外部提供口が無いので、**目視で確認する機能は無い**。以下は既存機能の確認。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 14-1 | メニュー `Tools > D-Drive > Editors > Material 変換`（`MaterialConvertWindow`）を開き「変換テーブルを再読み込み」を押す | エラーなく開き、変換元 MaterialData を選ぶと従来どおり「(変換テーブル使用)」または「この組の変換テーブル無し。N 件の Table を確認」が出る | □ 未 |
| 14-2 | `Assets/SourceAssets/` に `Foo_N.png` を置く | これまでどおり Texture Type が NormalMap / sRGB オフになる | □ 未 |

**T-Drive 導入後に確認**: [52] 末尾「T-Drive 導入後に確認」の FC-14 の項（T-Drive の変換表がマテリアル変換ウィンドウに載る・`_ToonMask` が sRGB オフで取り込まれる）。

## 15. FC-15: 知らないシェーダーを `DDrive/Lit` に変換しない

[51] §4.16（推奨案の既定 `KeepSource` は不採用。U-9 = (c) 確認ダイアログ + `MayaImportProfile.UnknownShaderPolicy`）、[06] A-2 の 2026-10-03 追記。**自動テストで確認済み**（`UnknownShaderPolicyTests` 19 件。実ダイアログは出さず `UnknownShaderGuard.PromptOverrideForTests` で差し替え）: 確認は 1 操作 1 回（Material が複数でも 1 回）・知らないシェーダーが無ければ出ない・保つ / 変換 / キャンセルの各結果（キャンセルは MaterialData も Slots も作らない・変えない）・非対話（`Rebuild` の既存シグネチャ・`Migrate` の既存シグネチャ）は従来どおり Lit でダイアログ無し・`KeepSource` は対話でも出さずに保つ（Specific 登録・既存 Data の有効な Shader は上書きしない）・`ConvertToLit` は従来どおり・`UnknownShaderPolicy` の既定が Ask（旧 Profile = 0）。以下は実ダイアログと Editor の目視。

準備: 知らないシェーダー（例: T-Drive の Toon、または Sprites/Default などの変換表に無いシェーダー）を使う Material を持つ Prefab（または FBX）を `ModelData` の `Prefab` にする。Project に `MayaImportProfile` が無ければ `Create > D-Drive > Material > Maya Import Profile` で作る（Inspector の「Unknown Shader Policy」の既定が `Ask`、ツールチップに説明が出ること）。

### 15.1 Ask: 「元ファイルを再読み込み」で確認ダイアログが 1 回出る

1. Profile の Unknown Shader Policy = `Ask` にする
2. `Tools > D-Drive > Editors` から Model エディタを開き、上の `ModelData` を選んで「元ファイルを再読み込み」を押す（知らないシェーダーの Material が複数あっても同様）

期待する結果: 「知らないシェーダーが見つかりました」のダイアログが **1 回だけ**出る。本文にシェーダー名と件数（6 種類以上なら「ほか N 種類」）、3 択の説明、Profile の欄の案内がある。ボタンは「元のシェーダーのまま保つ」「キャンセル（何もしない）」「DDrive/Lit に変換」。

結果: □ 未

### 15.2 3 択それぞれ

1. 15.1 のダイアログで「元のシェーダーのまま保つ」→ 作られた MaterialData の Shader がそのシェーダー（Material Editor / Inspector で確認）。Slots に結び付く
2. MaterialData を消してから再度押し、「DDrive/Lit に変換」→ Shader が `DDrive/Lit`
3. MaterialData を消してから再度押し、「キャンセル（何もしない）」（または Esc）→ MaterialData は作られず、ModelData の Slots も変わらない（Console に中断のログ）

期待する結果: 上記のとおり。キャンセルで途中まで書き換えた状態が残らない。

結果: □ 未

### 15.3 KeepSource / ConvertToLit ではダイアログが出ない

1. Profile の Unknown Shader Policy を `KeepSource` にして 15.1 の操作 → ダイアログは出ず、Shader は元のシェーダーのまま
2. `ConvertToLit` にして同様 → ダイアログは出ず、`DDrive/Lit`
3. `KeepSource` のまま、すでに Shader が入っている MaterialData（15.2 の 2 で作った Lit のもの）に対して再度「元ファイルを再読み込み」→ Shader は Lit のまま（上書きされない）

期待する結果: 上記のとおり。

結果: □ 未

### 15.4 メニューからの変換でも 1 回だけ

1. Profile = `Ask`。Project で知らないシェーダーの Material を複数選び、`Tools > D-Drive > Generate > 選択した Material を D-Drive/Lit・Unlit の MaterialData に変換`

期待する結果: ダイアログは 1 回だけ（Material の数だけ出ない）。選択に応じて保つ / 変換 / キャンセル（キャンセルなら MaterialData が作られず Console に中断のログ）。`選択したモデルから MaterialData を生成` も同様。

結果: □ 未

### 15.5 自動取り込み（非対話）は従来どおり

1. Profile = `Ask`（`AutoImport` ON、対象パスは `Assets/SourceAssets` 配下）。知らないシェーダーの Material を含む FBX を `Assets/SourceAssets` 配下へ入れる（または再インポート）

期待する結果: ダイアログは出ず、`DDrive/Lit` の MaterialData が作られる（従来と同じ）。Profile を `KeepSource` にして再インポートすると、新規に作られる MaterialData は元のシェーダーのまま（既存の Data の Shader は変わらない）。

結果: □ 未

### 15.6 右クリックの「Material を作成」でも 1 回だけ確認される（2026-10-03 追記、レビュー FC-R-01）

自動テストで確認済み（`UnknownShaderPolicyTests`）: 右クリック作成の事前確認は 1 操作 1 回・キャンセルで何も作らない・非対話 + Ask は既存 Data の知らないシェーダーを Lit に戻さない（元の `.mat` が知らないシェーダーのままのとき）・シェーダーが欠けた Material は、新規の Data は Lit にし、**既存の Data があるときはシェーダー参照も固有の設定も共通（色・テクスチャ参照・Blend 等）も書き換えない**（2026-10-04、FX-R-02 / 修正ラウンド 4 の FY-R-01。Data の内容・`IsDirty`・`.asset` のバイト列が変わらないことまでテスト済み。欠けたシェーダーの Material はプロパティが読めないことも確認。欠けた参照が Unity の `==` で null になること・未設定と区別できることも確認）。以下は実ダイアログの目視。

1. Profile = `Ask`。15.2 の 1 で「保つ」を選んで作った MaterialData が既にある、知らないシェーダーの `.mat` を Project で選び、右クリック > `Assets > D-Drive > Data を作成 > MaterialData を作成`（`AssetContextMenu`。Project の右クリックメニューにも同じ項目が出る）
2. 複数の `.mat`（知らないシェーダー）を選んで同じ操作をする
3. シェーダー参照が欠けた `.mat`（パッケージを外した Toon など。Inspector のシェーダーが `Hidden/InternalErrorShader` でピンク）で、**まだ MaterialData が無いもの**を含めて同じ操作をする
4. （2026-10-04 追加、FX-R-02）手順 1 の「保つ」で作った MaterialData（Toon 等）が既にある状態で、T-Drive のパッケージを一時的に外す（または `manifest.json` から外して解決できなくする）→ その `.mat`（シェーダーが欠けてピンクになる）で右クリック作成、または Model エディタの「元ファイルを再読み込み」を行う。パッケージを戻して再度確認する。**実データの複製で行い、事前に MaterialData の色（AlbedoTint）・Albedo 等のテクスチャ・Blend を既定と違う値にしておく**

期待する結果: 1・2 は確認ダイアログが **1 回だけ**出る（Material の数だけ出ない）。「保つ」なら既存の MaterialData の Shader は変わらない。「キャンセル」なら何も作られず Console に中断のログが出る。3 は、ダイアログに「シェーダーが見つからない（欠けている）Material が N 件」の行が出て、**新規の** MaterialData は `DDrive/Lit` になる（Console に警告）。4 は、既存の MaterialData の **Shader 欄・固有の設定に加えて、共通（色・テクスチャ参照・Blend 等）も変わらない**（Console に「既存の MaterialData … はシェーダー・固有・共通(色・テクスチャ等)とも変更しませんでした」の警告）。パッケージを戻したあと、その MaterialData は元の Toon のまま使える。

結果: □ 未

**要判断（FC-15）**: なし。実装の範囲外として残した点は [51] §4.16 実装メモ 7（単体 .mat の `DDrive/AiStandardSurface` 等は従来どおり Lit に変換される）。`SourceDataCreation` の .mat 取り込み（右クリック作成）は 2026-10-03 のレビュー対応で対話的な操作として扱うようになった（15.6）。

## 16. FC-16: モデルの名前付きスロットセット

未実装（実装時に追記）

## 17. FC-17: ModelData に外部データへの汎用参照欄

未実装（実装時に追記）

## 18. FC-18: プロジェクト設定の検証の拡張点

未実装（実装時に追記）

## 19. FC-19: 検証の警告の調整

[51] §4.20、[06] A-4。自動テストで確認済み（PlayMode `MaterialDataValidatorTests` 4 件）: Albedo 無し + `AlbedoTint` 白 + Albedo のあるシェーダーでは従来どおり Warning / `AlbedoTint` が白以外なら Warning なし / Albedo のプロパティを持たないシェーダーでは Warning なし / `RenderingLayerMask` が 0 のとき Info なし・0 以外のとき Info（`DD-MAT-RENDERINGLAYERMASK-UNUSED`）。以下は Inspector・Validation 画面の目視。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 19-1 | 確認用の MaterialData（Albedo 未設定・AlbedoTint 白・Shader `DDrive/Lit`）を作り `Tools > D-Drive > Validation > Run All` を実行する | 「Common.Albedo（ベースカラー）が未設定です」が Warning で出る | □ 未 |
| 19-2 | 19-1 の MaterialData の AlbedoTint を赤などにして再度 Run All | 上の Warning が消える | □ 未 |
| 19-3 | MaterialData の `RenderingLayerMask` にマウスを載せる。次に 4 など 0 以外を入れて Run All | ツールチップに「未使用。ライトレイヤーは ModelData.LightLayerMask を使う」とある。Run All に Info（RenderingLayerMask は実行時に使われません…）が 1 件出る（Warning / Error は増えない）。0 に戻すと消える | □ 未 |

確認後、確認用の MaterialData は削除してください。

## 20. FC-20: 所有接頭辞（`FC_` / `fcs_`）の一覧と検査

[51] §4.21、[05] B-6。自動テストで確認済み: 判定（`FC_x` / `fcs_x` は true、`fc_x` / `Smile` は false）・Validator の Warning 有無・合成 Mesh の Prefab を Spawn → `AnimManager.Tick` → 外部の後書きが次の Tick で上書きされない・名前不変・指定した通常シェイプだけが書かれる（`ExternalBlendShapeOwnershipTests`）。以下は AnimEditor の表示の目視。

### 20.1 モデル情報の一覧で外部管理のシェイプが隠れる

1. `FC_` または `fcs_` で始まるシェイプと通常のシェイプ（例 `Smile`）を持つモデルの Prefab を用意する（顔の補正を使うモデル、または合成メッシュ）
2. `Tools > D-Drive > Editors` から Anim エディタを開き、確認用モデルとしてその Prefab を対象にする（「モデル Prefab を開く」でも可）
3. 「モデル情報」の 1 行要約と「モデル情報の詳細（BlendShape 一覧など）」を見る
4. 詳細の中の「外部管理のシェイプも表示」をオンにする

期待する結果: 手順 3 では要約が「BlendShape N 個（外部管理 M 件を除く）」、詳細の一覧に `FC_*` / `fcs_*` が出ず、末尾に「外部管理 M 件」の説明が出る。手順 4 では `FC_*` / `fcs_*` も一覧に出る。

結果: □ 未

### 20.2 AnimData が外部管理のシェイプを指すと警告

1. AnimData の `BlendShapes` に ShapeName `FC_Test_Neutral_R0_C0` の行を足す（既存の値は消えないことも確認）
2. Validation（`Tools > D-Drive > Validation > Run All`）を実行する

期待する結果: 「BlendShape 'FC_Test_Neutral_R0_C0' は外部パッケージが管理するシェイプです…」の Warning が出る。`Smile` のような通常名では出ない。

結果: □ 未

## 22. T-Drive 導入後に確認

T-Drive のパッケージ（`TDrive.*`）が入ってから、T-Drive 側と合わせて確認する項目。今は項目名だけ（実装した担当・T-Drive 側の担当が手順を足す）。

- FC-1: T-Drive の fctrack 取り込み（FC-5 のリスナー）が `SameAsTrack` の binding を足し、Facial トラックがカットシーンのキャラと同じ相手に結ばれる
- FC-2 / FC-12: `ToonCharacter` が `IModelInstanceListener` でスロット適用後に `CharacterLook` を配る / 返却で後片付けする。`FacialCorrectionRunner` の `OnDisable` と重みの復元が二重になっても表情が壊れない
- FC-5: T-Drive の fctrack 取り込みを `ICutsceneImportListener` で実装したあと、FBX を置く → `.fctrack` を置く（順序を入れ替えても）→ `Generate > SourceAssets/Cutscene からインポートルールを再実行` で、`.playable` に Facial トラック（`<Model>_Facial(auto)`）が 1 つだけ付き、`CutsceneData.Bindings` に `SameAsTrack` の binding が 1 件だけ入る（FBX の再取り込みで消えず・増えない）
- FC-4: T-Drive 側で `FacialMarker : Marker, ICutsceneMarker`（`Bridges.DDrive`）を実装したとき、Timeline に置いたマーカーが Play で時刻を跨いだ瞬間に 1 回だけ `Fire` され（Seek / Skip / 途中参加では呼ばれず）、Timeline ウィンドウの再生でも同じ（スクラブでは呼ばれない）
- FC-6: T-Drive の `IImportRuleFolderOptOut` 実装（`Facial` を宣言）が入ったあと、`Assets/SourceAssets/Facial/<キャラ>/` にファイルを置いても Console に `ImportRule 案内` の警告が出ない（宣言していない名前のフォルダには従来どおり出る）
- FC-14: T-Drive の `IShaderConversionTableProvider` / `ITextureImportRuleProvider` 実装が入ったあと、(a) マテリアル変換ウィンドウの表に T-Drive パッケージ内の変換表が載り、`Assets/` に同じ組の表を置くとそちらが優先される (b) `*_ToonMask.png` を取り込むと sRGB オフ（`T_` で始まる名前でも）になり、Texture の Validation が Warning を出さない
- FC-3: T-Drive の `Bridges.DDrive` が `ViewCamera.TryGetCurrent` を視点解決の最後のフォールバックに設定したとき、カットシーン中も表情の補正が実際のカット姿勢（ブレンド中を含む）に追従する。Runner の `LateUpdate` の実行順が 1000 より後であること（それより前だとカットシーン中は 1 フレーム遅れる）
- FC-3(実行順の検査の除外。2026-10-06、P-15 確認 Q-4): T-Drive の `Bridges.DDrive`(Editor)が `ICameraExecutionOrderExemptionProvider` で `FacialCorrectionRunner` を宣言したあと、CutsceneData が 1 件以上ある状態で `Tools > D-Drive > Validation > Run All` → Runner に実行順(10000)の Warning が出ず、Info「実行順の検査から除外: 1 型 — …FacialCorrectionRunner(理由: …。宣言元: …)」が 1 件出る。宣言していない他のスクリプトの Warning は従来どおり。ブリッジが無い状態では従来どおり Runner に Warning が出る。手順は [43] §15 の 15-30。解決のロジック・Validator への組み込み・外部アセンブリのダミーの自動発見は自動テストで確認済み(`CameraExecutionOrderExemptionTests` / `ExternalContractCameraExemptionTests`)
- FC-7: T-Drive の Timeline クリップ・マーカー（外部パッケージのもの）が `AssetId` / `AssetRef` で SE・VFX 等を参照しているとき、それらが使用箇所に `.playable` として出る（`UnityEngine.Object` の直接参照の Facial データは出ない。出さない仕様）
- FC-19: T-Drive の Toon シェーダー（`_BaseMap` を持たないものがあれば）の MaterialData で、Albedo が空でも「Common.Albedo が未設定」の Warning が出ない
- FC-15: T-Drive 側の対応が入ったとき
- 修正ラウンド 1（レビュー [53]）: (a) 0 秒に置いた `FacialMarker` が先頭から再生したとき 1 回発火する（4-2。途中参加・Seek では出ない） (b) Toon の輪郭線が `DisabledPasses = { SRPDefaultUnlit }` で消える（11.3） (c) T-Drive を入れる前に Toon の `.mat` だけがあるプロジェクトで KeepSource にしても、欠けたシェーダーの**新規**データは Lit になる（15.6 の 3）。T-Drive の導入後、Toon の MaterialData がある状態でパッケージを外して再生成しても、既存データの Shader は変わらない（15.6 の 4。修正ラウンド 3）。Cosmetic のカットシーンの 0 秒のマーカーが Host / Client の両方で鳴る（4-5。修正ラウンド 3）
- 修正ラウンド 5（レビュー [57]）: (a) 4-5 で受信した端末の開始位置と無音にしたマーカー数がログの 1 行で読める (b) 自分の Presentation を Marker の購読者が止めたとき、同じ Presentation の後ろのトラックが鳴らない・出ない（自動テスト `PresentationTickReentrancyTests` で確認済み。目視は不要）
- 修正ラウンド 4（レビュー [56]）: (a) Client の操作で再生したカットシーンが別の Client でも、直近 0.5 秒のマーカーが鳴る・途中参加は参加時点から遡って 0.5 秒以内だけ鳴る（4-5）。(b) Edit Mode で途中から再生・再開したとき最初の 1 フレーム分を飛ばさない（4-4 の (c)）。(c) 欠けたシェーダーの間、既存 MaterialData の色・テクスチャ等も変わらない（15.6 の 4）
- FC-10: T-Drive のパッケージを入れたうえで、MS2026 の Test Runner で `ExternalContract` の全件 Pass（外部パッケージが入った状態でも、ダミーの `IValidator` が Run All を汚さない・実 FBX〔T-Drive のキャラ〕でボーン名 / シェイプ名 / スケールが取り込み〜Spawn で変わらない、を実物でも見る。E-17 の実 FBX 版は [51] §4.11 実装メモ (3)）

## 23. 検査の調整（2026-10-06、修正ラウンド 7）

[59](59_review_round6_valuedef_2026-10-06.md) GB-R-01 / GB-R-02。自動テストで確認済み（`CameraShakeDataValidatorTests`・`HapticsDataValidatorTests`・`BgmDataValidatorTests`・`ConstantTimeValidationTests`）。目視は次の 3 点だけ。確認用の Data は確認後に削除してください。

| # | 手順 | 期待する結果 | 結果 |
|---|---|---|---|
| 23-1 | Anim2D・ControlSkin（Button / Slider）・CameraShake の Data を AssetBrowser などで新規作成して保存し（設定は触らない）、`Tools > D-Drive > Validation > Run All` を実行する | 新規作成しただけのこれらの Data が Error にならない（Clip 等の「未設定」系は出てよい） | □ 未 |
| 23-2 | Audio エディタで BGM の音源を 1 本取り込む（`SourceAssets/Bgm/` に置く → BgmData が作られる）。LoopStart / LoopEnd は 0 / 0 のまま Run All | その BgmData に「LoopEndSec が LoopStartSec 以下です」の Error が出ない。LoopStart に 5、LoopEnd に 2 を入れると Error が出る | □ 未 |
| 23-3 | CameraShake の Envelope を固定値（Mode=Constant）にして尺（Time）を 0 にし、Run All を実行する | 「Envelope の尺(Duration)が 0 以下のため、再生してもすぐ終わり何も起きません」の Warning が出る（Error は出ない）。尺に 0.4 を入れると消える | □ 未 |

## 要判断（全体）

- なし（実装済みチケットの要判断は各節末尾。未決は [51] §8 の U-14。U-15 は FC-10 で決定済み）
