# 45. 2026-09-19 自前レビュー結果(Cutscene / Timeline、6-10a〜d + Edit Mode プレビュー)

> **対象**: [docs/44](44_review_2026-09-19.md) が対象外にした Cutscene / Timeline 一式。独立レビューを一度も受けていない範囲。
>
> | コミット | 内容 |
> |---|---|
> | `f02c68b` | 6-10a: Timeline 基盤(CutsceneData / CutsceneManager / `Cutscene.Play`・ネット・入力ロック) |
> | `02bc145` | 6-10b: Timeline トラック群・Camera クリップ・スクラブプレビュー |
> | `e562059` | 6-10c: Maya FBX から Cutscene/Timeline を自動構築 |
> | `883a372` | 6-10d: 確認用シーン・Inspector 導線・Validator・マニュアル更新 |
> | `fe17586` | Edit Mode プレビュー(Timeline ウィンドウ主導、案 B) |
> | `bf76062` | うち `CutsceneFpsValidator.cs` / `CutsceneImportService.cs` の `SaveAssets` 置換のみ |
>
> **方法**: 読み取り専用。**作業ツリーの HEAD(`bf76062`、クリーン)をそのまま読んだ**(ユーザーが同時に Unity で人による確認作業中のため、**Unity MCP でのコンパイル・テスト実行は行っていない**)。したがって本書の指摘はすべて **コードを読んで確認した事実**であり、エディタ上での実挙動確認は含まない。行番号は HEAD 時点のもの。
>
> 参照した設計: [docs/26](26_timeline.md) 全節(特に §3.1 / §4.1〜§4.7 / §5 / §7 と 6-10a〜d・2026-09-19 の各実装メモ)、[docs/12](12_review.md) §3、[docs/41](41_phase6_review_2026-09-17.md) / [docs/44](44_review_2026-09-19.md) の観点・書式。

## 観点

docs/12 §3 のチェックリスト + docs/41・44 の観点を引き継ぎ、今回の依頼で明示された評価項目を足した。

- **runtime**: 定常経路(`CutsceneManager.Tick` / `Evaluate` / クリップの `ProcessFrame` / Camera ミキサー / `AdvanceMarkers`)の LINQ・クロージャ・boxing・GC alloc、例外で止めない(警告 + no-op)、Placeholder、`PlayableDirector` の free-list(借用・返却・二重解放)、実行順の契約(§4.6.5)と Submit 消費検出、カメラ所有権、BlendIn/Out と Skip/Cancel の戻し、Origin と Self/Target バインドのオフセット、入力ロックのエッジ、Wrap / SourceFrameRange / FrameRate、Presentation クリップ経由の循環
- **net**(§4.7 / [docs/14](14_networking.md)): 発行者検証・レート制限・偽造・Late Join・Skip=ToMarker の同期・PredictLocal・Cosmetic/Local の分岐、`PresentationManager.TrackKind.Timeline` との整合、Simulated
- **Edit Mode プレビュー**(`fe17586`): `FireEnabled` 置き換えで Play Mode の挙動が不変か、`[InitializeOnLoad]` のライフサイクル(ドメインリロード・シーン切替・Prefab ステージ・Play Mode 突入)、プレビュー用 Director / AudioSource の残留、`PlayableDirector.state` 判定、カメラ復元と二重書きガード、`TimelineEditorWindow.SetTimeline` のリフレクション、Bindings 解決時の SpawnModel の返却
- **import**(6-10c): 命名規則の境界、再取り込みの差し替え粒度、カメラカーブ抽出、`CutsceneFrameRangeTrimmer`、AssetPostprocessor の再入・部分失敗
- **validator**: `CutsceneDataValidator` / `CutsceneFpsValidator` / `CameraExecutionOrderValidator`
- **tests**: 追加テストが本当に検証しているか、Play Mode 専用経路の穴、実アセット汚染
- **docs**: docs/26 の設計・§7 の決定との一致、CLAUDE.md §1 / docs/11 / docs/42 §3.5、`DesignerManual/cutscene-maya-export.html`

---

## P1 — 通常使用で誤動作 / データ破損 / 認可の抜け

### runtime / camera

**P1-1. `CutsceneCameraStateHolder` が Director の返却・貸出でリセットされないため、Cancel 直後に再生した「カメラトラックを持たない Cutscene」が前回のカメラ姿勢を奪ったまま固定する**

`Assets/DDrive/Runtime/Cutscene/CutsceneManager.cs:637-649`(`ReturnDirector`)と `:605-635`(`RentDirector`)。`CutsceneCameraStateHolder` は CutsceneRoot 生成時に 1 回だけ付き(`:625`)、free-list で使い回される。**`HasData` を false に戻すのは `CutsceneCameraMixerBehaviour.ProcessFrame`(`Runtime/Cutscene/Tracks/CutsceneCameraClip.cs:186-190`)だけ**で、返却・貸出のどちらでもリセットしていない。

`UpdateCameraForInstance`(`CutsceneManager.cs:1202-1227`)は `holder.HasData` をそのまま信じて `_cameraOwner` を取り、`holder.LocalPos/LocalRot/Fov/GameBlendWeight` を `Submit` する(`:1263-1276`)。

**再現条件**:

1. Camera クリップを持つ Cutscene A を再生し、**クリップ区間の途中で `Cancel()`**(または `StopAll` / シーンアンロード / 親 Presentation の Cancel)。`CancelInternal → Cleanup`(`:950-959`、`:1310-1341`)は `Evaluate()` を呼ばずに `ReturnDirector` するため、holder は `HasData=true` + A の最終値のまま残る。
2. 続けて **Camera トラックを持たない** Cutscene B を再生する。free-list は LIFO なので同じスロットが返り、B の Timeline にはカメラミキサーが無いので holder は一度も更新されない。
3. B の最初の Tick で `hasData==true` → B がカメラ所有権を取り、**A の最後の姿勢・画角を B の尺のあいだ書き続ける**(`Weight` も A の値)。

Skip 経由(`ApplySeek` → `Evaluate()`、`:1024-1036`)は最終フレームでミキサーが走り `HasData=false` になるため再現しない。**Cancel 系だけが穴**。

**直し方**: `ReturnDirector`(または `RentDirector` のスロット再利用時)で holder を `HasData=false` に戻す。ついでに `slot.Director.ClearGenericBinding` 相当の掃除(整理項目参照)も同じ場所に置ける。

**✅ 対応済み 2026-09-20**: `CutsceneManager.ReturnDirector`(`Assets/DDrive/Runtime/Cutscene/CutsceneManager.cs`)で `CutsceneCameraStateHolder.HasData = false` にリセットするようにした(カメラ所有権自体は既存の `Cleanup` → `ReleaseCameraOwnership` で全返却経路から解放済みだったため未変更)。PlayMode テスト `CutsceneTimelineTracksTests.Cancel_MidCameraClip_DoesNotLeakCameraStateToNextCameraLessCutscene` を追加(カメラクリップ途中で Cancel → カメラ無しの Cutscene を再生 → `Camera.main` が動かないことを固定)。`slot.Director` の GenericBinding 掃除は整理項目のまま未対応(スコープ外)。

### runtime / camera

**P1-2. 取り込んだままの Camera クリップ(`Focus=Volume` 既定 + ピント距離カーブ空)で、DoF のピントが 0.01m に張り付き画面全体がボケる。docs/26 §4.6.4 とデザイナーマニュアルは「取れなければ書かない」と明記している**

- 取り込みは新規 Camera クリップに **`asset.Focus = CameraFocusMode.Volume` を無条件で入れる**(`Assets/DDrive/Editor/Cutscene/CutsceneImportService.cs:469-475`)。
- カーブ抽出は、候補プロパティ名が見つからないとき **空の `AnimationCurve` を入れる**(`Assets/DDrive/Editor/Cutscene/CutsceneCameraCurveExtractor.cs:66-67`)。docs/26 §7.3 のとおり `m_FocusDistance` / `m_Aperture` が実 FBX で取れるかは**未検証**で、取れないのが既定の想定。
- ミキサーは空カーブを `Evaluate` して 0 を書く(`Tracks/CutsceneCameraClip.cs:150-151, 197-198`)。
- Applier は `Focus != Off` のとき **重み `w` を掛けずに** `cam.focusDistance = 0` を書き、Volume には `focusDistance = Mathf.Max(0.01f, 0) = 0.01`、`aperture = 5.6`、`focalLength = 50` を入れて `weight = w` にする(`Assets/DDrive/Runtime/Cutscene/DDriveCutsceneCameraApplier.cs:187-220`、特に `:199` と `:216-219`)。

結果、**ピント距離 1cm・f5.6・50mm の Bokeh DoF が weight 1 で掛かる** = カットシーン中だけ画面が一面ボケる。docs/26 §4.6.4 の「どちらも取れなければカーブは空 = ピントは書かない(`Focus = Off` 相当)」と、`docs/DesignerManual/cutscene-maya-export.html:69` の「取得できなければ空のまま(ピントは書き込まれません)」のどちらにも反する。

**顕在化の条件**: カメラの `renderPostProcessing = true`(URP でポストプロセスが有効)。6-10d の確認用シーンのカメラは `UniversalAdditionalCameraData` を既定のまま付けるだけ(`Editor/Preview/CutscenePreviewSceneSetup.cs:77`)なので**確認用シーンでは見えず**、持ち込み先の実カメラで初めて出る、という気づきにくい形になっている。

**直し方**: (1) `CutsceneCameraCurveExtractor.Extract` が `FocusDistance` を取れなかったら `target.Focus = CameraFocusMode.Off` にする(または `CutsceneImportService` 側で「取れた時だけ Volume」にする)。(2) Applier 側も保険として `_pending.FocusDistance <= 0f` なら DoF を書かず `weight=0` にする。(3) `CutsceneDataValidator` に「`Focus=Volume` だが `FocusDistance` カーブが空」の Warning を足す。

**✅ 対応済み 2026-09-20**: (1)(2) を実装。`CutsceneImportService.ResolveInitialFocusMode`(新規)で「焦点距離/ピント距離/絞りのいずれか 1 つでも取れれば Volume、全て空なら Off」を新規クリップ生成時にのみ適用(再取り込みではデザイナー設定の `Focus` を保持する既存方針は変えていない)。`DDriveCutsceneCameraApplier.ApplyFocus` にも `_pending.FocusDistance <= 0f` なら DoF を書かない二重防御を追加。(3) の Validator Warning は未対応(スコープ外、P2-13 と合わせて別途検討)。EditMode テスト(`CutsceneImportServiceTests.ResolveInitialFocusMode_*`)+ PlayMode テスト(`CutsceneTimelineTracksTests.CameraClip_FocusVolume_EmptyFocusDistanceCurve_DoesNotWriteDoF`)を追加。

### net

**P1-3. Late Join / 接続直後の `CutscenePlayMsg` が「未登録」として破棄される。Presentation の 6-0 修正3(`SetRegistryReady` の保留キュー)が Cutscene に移植されていない**

`Assets/DDrive/Runtime/Cutscene/CutsceneManager.cs:741-767`。受信は `OnReceivePlayMsg → OnReceivePlayMsgInternal` と **Presentation とまったく同じ 2 段構えの形**(`:741`、`:855`、`:880`)になっているのに、Presentation が持つ `_registryReady` の保留キュー(`Runtime/Presentation/PresentationManager.cs:647-688`)だけが入っていない。`:763` の `_registry.IsRegistered(msg.CutId, AssetType.Cutscene)` が false になり、警告 1 行を出して捨てる。

`DDriveRuntimeBootstrap` は `Presentation.SetRegistryReady(false)` を Build 時に入れ(`Runtime/Loop/DDriveRuntimeBootstrap.cs:319`)、`RegisterCatalogsAsync` 完了時に `true` へ戻す(`:569`)。**Cutscene には対応する呼び出しが 1 つも無い**(grep で確認)。

**再現条件**: Late Join(Host の `OnClientConnected` は接続直後に台帳ぶんの `CutscenePlayMsg` を送る、`CutsceneManager.cs:816-853`)。参加側の `RegisterCatalogsAsync` は `Start()` から `Forget()` で走る非同期(`Bootstrap.cs:177`)なので、接続直後のこのメッセージは**まさに未登録の窓**に当たる。docs/26 §4.7 の「Late Join は Host の台帳から復元」が機能しない。6-0 の実機確認で Presentation について見つかった課題(docs/11 6-0 修正3)と同一。

**直し方**: `PresentationManager` の `PendingNetMessage` キュー + `SetRegistryReady` をそのまま移植し、`Bootstrap.Build()` と `RegisterCatalogsAsync` の 2 箇所へ `Cutscene.SetRegistryReady(...)` を足す。

**✅ 対応済み 2026-09-20**: `PresentationManager.SetRegistryReady`(`_registryReady` + `PendingNetMessage` キュー)をそのまま `CutsceneManager` に移植(Play/Seek/Cancel の到着順を保つ)。`DDriveRuntimeBootstrap.Build()`(`Presentation.SetRegistryReady(false)` の直後)と `RegisterCatalogsAsync` 完了時(`Presentation.SetRegistryReady(true)` の直後)に `Cutscene.SetRegistryReady(...)` を追加。PlayMode テスト `CutsceneManagerTests.OnReceivePlayMsg_BeforeRegistryReady_IsQueued_AndFlushedAfterReady` / `OnReceiveCancelMsg_BeforeRegistryReady_IsQueued_AndAppliedInOrderAfterReady`(Play→Cancel の順序保持)を追加。

### import

**P1-4. `SourceFrameRange`(1 FBX に複数ショットの「逃げ道」)を使うと、Timeline が**シリアライズできない一時 AnimationClip**を参照し、ドメインリロード後にクリップ参照が消える**

`Assets/DDrive/Editor/Cutscene/CutsceneFrameRangeTrimmer.cs:25` は `new AnimationClip { ... }` を返すだけで、`AssetDatabase.CreateAsset` も `AddObjectToAsset` もしない。その戻り値が

- `Assets/DDrive/Editor/Cutscene/CutsceneImportService.cs:288-291`(カメラ + 小物)
- 同 `:373-376`(キャラ)

で `clip` に入り、`:415` の `asset.clip = clip`(`AnimationPlayableAsset`)に**ディスク上の `.playable` から**代入される。参照先が非永続オブジェクトなので、シリアライズ時には空参照になり、**次のドメインリロード / プロジェクト再起動でそのトラックのアニメが消える**(エラーも警告も出ない)。切り出したクリップ自体もどこからも `Destroy` されずリークする。

あわせて、切り出しクリップは `humanMotion` 等の Humanoid 由来のフラグを引き継がないため、`CutsceneDataValidator.ValidateHumanoidAvatarMismatch`(`Runtime/Cutscene/CutsceneDataValidator.cs:238-257`)も効かなくなる。

**再現条件**: `CutsceneData.SourceFrameRange` に 0/0 以外を入れて再取り込み(docs/26 §5.1 の「逃げ道」)。既定(0/0)では `ShouldTrim` が false で元のサブアセットをそのまま使うため発生しない。

**直し方**: 切り出したクリップを CutsceneData か TimelineAsset のサブアセット(`AssetDatabase.AddObjectToAsset` + 既存があれば上書き)として永続化する。永続化しないなら `SourceFrameRange` 自体を「未対応」として Validator で Error にする方が安全(現状は Validator も `End < Start` しか見ていない、`CutsceneDataValidator.cs:46-49`)。

**✅ 対応済み 2026-09-20**: `CutsceneImportService.PersistTrimmedClip`(新規)を追加し、`CutsceneFrameRangeTrimmer.TrimClip` が返す一時 AnimationClip を TimelineAsset(`.playable`)のサブアセットとして `AssetDatabase.AddObjectToAsset` で永続化(カメラ+小物・キャラの両呼び出し箇所)。サブアセット名は元 FBX のファイル名から一意に決め、再取り込みでは同名の既存サブアセットへ `EditorUtility.CopySerialized` で内容だけ差し替えて再利用する(孤児を残さない)。`TrimClip` が「切り出さず元のクリップをそのまま返した」場合(既に永続、または fps 不明)は何もしない安全弁も追加。EditMode テスト `CutsceneImportServiceTests.ProcessPaths_SourceFrameRange_PersistsTrimmedClipAsSubAsset_AndDoesNotDuplicateOnReimport` を追加。

### editor(Edit Mode プレビュー)

**P1-5. Edit Mode プレビュー用の Director が確認用シーンに保存される作りで、(a) 押すたびに SpawnModel が返却されず溜まり、(b) `playOnAwake=true` のまま Play Mode で勝手に再生される**

`Assets/DDrive/Editor/Cutscene/CutsceneEditModeDirectorSetup.cs:35-45`:

```csharp
var directorGo = GameObject.Find(DirectorName) ?? new GameObject(DirectorName);
var director = directorGo.GetComponent<PlayableDirector>();
if (director == null) { director = directorGo.AddComponent<PlayableDirector>(); }
```

`hideFlags` を付けず、`playOnAwake` / `timeUpdateMode` も触っていない(Play Mode 側の `CutsceneManager.RentDirector` は `:619-620` で両方明示している)。

- **(a) SpawnModel の返却漏れ**: `:183-198` の `managers.Models.Spawn(binding.Model, directorRoot)` は、`ModelsManager.Spawn(id, parent)`(`Runtime/Model/ModelsManager.cs:116-129`)が**プールのインスタンス親から `directorRoot` へ付け替える**実装なので、生成したモデルは DontSave のプレビュールート(`CutsceneEditModeManagers.cs:88`)ではなく**シーンに保存される Director の子**になる。`Despawn` はどこからも呼ばれない(grep で確認)。「▶ Timeline ウィンドウで開く」を押すたびに同じモデルが 1 体ずつ増え、シーンを保存すると確定する。`CutsceneEditModeManagers.Dispose`(`:115-141`)がプレビュールートを消しても、付け替え済みのモデルは孤児として残る。
- **(b) Play Mode での自動再生**: `PlayableDirector.playOnAwake` の既定は true。保存された確認用シーンで Play Mode に入ると、この Director が `CutsceneManager` を通さずに Timeline を再生する。`CutsceneDirectorContext.FireEnabled` は **public な直列化フィールド**(`Runtime/Cutscene/CutsceneDirectorContext.cs:26`)で、Edit Mode の監視役が再生中に true を書く(`CutsceneEditModePreviewProvider.cs:176-177`)。その状態で保存 → Play Mode に入ると、監視役は `Application.isPlaying` で止まる(`:126-129`)ため **true のまま固定**され、SE/VFX/UI/AnchorGroup/Presentation クリップが静的ファサード(= 本番 Manager)経由で発火する。Animation トラックは `FireEnabled` と無関係に常に動く。

**直し方**: Director の GameObject を `hideFlags = HideFlags.DontSave` にする(既存の `CutsceneEditModeManagers.PreviewRootName` と同じ流儀)、`director.playOnAwake = false` を明示する、`PrepareContext` で `FireEnabled=false` を入れているのと同様に `OnPlayModeStateChanged(ExitingEditMode)` でも false に戻す、`ApplyBindings` で前回 Spawn したハンドルを保持して次回に `Despawn` する(または Director の子を作り直す)。

**✅ 対応済み 2026-09-20**: `CutsceneEditModeDirectorSetup` のプレビュー用 GameObject を `HideFlags.DontSave` にし、`director.playOnAwake = false` を明示。`OpenTimelineWindow` の本体を `EnsureDirector`(テストから直接呼べる公開 API)として切り出し、押し直すたびに `ApplyBindings` の先頭で前回 Spawn した Model(`_spawnedModels`)を `Despawn` してから作り直すようにした。加えて、`OnPlayModeStateChanged(ExitingEditMode)`・`OnActiveSceneChanged`・`OnPrefabStageChanged`・ドメインリロード(`AssemblyReloadEvents.beforeAssemblyReload`)のすべてで `CutsceneEditModeDirectorSetup.TearDown()`(Despawn + Director 自体を `DestroyImmediate`)を呼ぶようにした(`FireEnabled` を false に戻すだけでなく Director ごと消すため、(b) は「残った Director が誤って再生する」リスクごと無くなる)。テストの穴 8 も合わせて対応: `CutsceneEditModePreviewProvider.TearDownForTests()`(新規公開 API)を `CutsceneEditModePreviewProviderTests` の `TearDown` から呼び、静的 Manager 群(`[D-Drive] Cutscene Edit Preview` プレビュールート)がテスト実行後にシーンへ残らないようにした。EditMode テスト `CutsceneEditModeDirectorSetupTests`(DontSave / playOnAwake=false / 2 回呼んでもモデルが 1 体)を追加。

---

## P2 — エッジケース / 規約違反 / パフォーマンス

### net

**P2-1. Late Join の再送が `SelfNetId=0 / TargetNetId=0` 固定のため、既定の `Origin=Self` では参加者の画面だけカットシーンがワールド原点で再生される**

`Assets/DDrive/Runtime/Cutscene/CutsceneManager.cs:839-852`。台帳(`ActiveNetworkedEntry`)は `Ctx` を持っているのに、再送時に `_netBridge.ResolveNetId(entry.Ctx.Self)` を引き直さず 0 を送っている。受信側は `ctx.Self` が null のまま `PlayLocalInternal` → `ApplyOrigin` が `Origin=Self`(**`CutsceneData.Origin` の既定値**、`CutsceneData.cs:82`)で `WarnUnresolvedBinding` + 原点(0,0,0) に落ちる(`:271-283`)。`Target=Self` の Animation トラックも同じく未解決でミュートになる(`:360-362`, `:403-407`)。

`PresentationManager.OnClientConnected`(`Runtime/Presentation/PresentationManager.cs:818-831`)も同じく 0 を送っており**既存の流儀どおり**だが、Presentation では「SE/VFX が `Position` にフォールバックする」程度で済むのに対し、Cutscene では**演出全体の原点**が変わるため影響が大きい。

**直し方**: 再送時に `ResolveNetId(entry.Ctx.Self/Target)` を引き直す(送信時点で解決できなければ従来どおり 0)。Presentation 側も同時に直すのが望ましい。

**P2-2. Cosmetic な Presentation の Timeline トラックから Cosmetic な Cutscene を呼ぶと、全クライアントが `CutscenePlayMsg` を Broadcast して N 重に再生される(Validator の検査も無い)**

`Runtime/Presentation/PresentationManager.cs:1618-1634`(`FireTimeline`)は、受信側でローカル再生された Presentation からも無条件に `_cutscene.PlayData(data, in ctx)` を呼ぶ。`CutsceneManager.PlayData`(`Runtime/Cutscene/CutsceneManager.cs:188-196`)は `Flags.Net == Cosmetic` なら `PlayCosmeticNetworked` に入り **各クライアントが自分の `HandleNetKey` で Broadcast** する。

2 台なら、Host と Client がそれぞれ 1 本ずつ Broadcast し、双方が 2 本(PredictLocal=true なら自分の予測分も)再生する。カメラ所有権は最初の 1 本が勝つ(`:1228-1239`)ので絵は 1 つに見えるが、SE/VFX/Event マーカーは重複し、入力ロックの深さも 2 になる。

docs/26 §3.1 は「Presentation を親にして Timeline トラックで Cutscene を呼ぶ」を推奨経路として挙げているのに、その組み合わせのネット挙動が定義されていない(§4.7 にも記述無し)。

**直し方**: `FireTimeline` は「入れ子は常にローカル再生」にする(`PlayData` ではなく `PlayLocalInternal` 相当の入口を internal で用意する)か、最低限 `CutsceneDataValidator` / `PresentationDataValidator` に「Cosmetic な Presentation の Timeline トラックが Cosmetic な Cutscene を指している」Warning を足す。docs/26 §4.7 に決定を書く。

### runtime

**P2-3. `DDriveCutsceneCameraApplier.Restore()` がカメラの Transform を戻さないため、毎フレーム姿勢を書かないカメラ(= 6-10d の確認用シーン)は演出終了後もカットシーンの最終姿勢に取り残される**

`Assets/DDrive/Runtime/Cutscene/DDriveCutsceneCameraApplier.cs:108-129`。控えて戻すのは `fieldOfView` と `focusDistance` だけで、位置・回転は「ゲーム側が毎フレーム書く `G` を読む」前提(`:166-173`)。

ゲーム側にカメラ制御が無い場合、`G` は**前フレームに自分が書いた値**になるので `lerp(G, T, w)` は BlendOut 中も T の近くに収束し、`w=0` になった時点でそのまま止まる。`Editor/Preview/CutscenePreviewSceneSetup.cs:72-77` が作る確認用シーンの `Main Camera` にはスクリプトが 1 つも付かないので、**デザイナーが最初に触る場所でこの挙動になる**(docs/26 §4.6.2 は「終了時にゲームカメラへ返す」と書いている)。

`Tests/Runtime/CutsceneTimelineTracksTests.cs:218` は `focusDistance` の書き戻しだけを assert しており、Transform は見ていない。

**直し方**: §4.6.2 が画角・ピントに対して定めている「再生開始時に控えた値を `G` とし、終了時に書き戻す」を Transform にも適用する(開始時の位置・回転を控え、`Restore()` で戻す)か、少なくとも「ゲームカメラ制御が無い場合は戻らない」ことを §4.6.5 の契約(G-4 の隣)とマニュアルに明記する。

**P2-4. `CutsceneHandle.IsInputLocked` が終了済み Handle で「Invalid handle access」警告を出す(docs/26 §4.5.1 は「Handle が無効なら false」)**

`Runtime/Cutscene/CutsceneManager.cs:1058-1059` だけが `_instances.TryGet` を直接使っている。`TryGet` は範囲内の無効ハンドルで `RecordInvalidAccess` → `InvalidAccessCount++` + `Debug.LogWarning`(`Foundation/Handle/InstanceStore.cs:43-60, 87-94`)。同じクラスの `IsPlaying`(`:1054`)・`GetNormalizedTime`(`:1061-1069`)・`OnCompleted` 等は `IsValidSilent` / `TryGetInstanceSilent` を使い分けている。

§4.5.1 の想定利用は「ゲーム側が毎フレーム見る」なので、演出終了後も参照し続けるのが普通の書き方になる = Editor/Development Build で毎フレーム警告が出る。

**直し方**: `IsInputLocked` を `TryGetInstanceSilent` に変える(1 行)。

**P2-5. `CutsceneData.Wrap`(`DirectorWrapMode`)が実質機能しない。Loop/Hold を選んでも尺で必ず完了する**

`Runtime/Cutscene/CutsceneManager.cs:238`(`director.extrapolationMode = data.Wrap`)を設定しているが、Tick は `elapsed` を自前で進めて `director.time` を **duration にクランプ**し(`:1183`)、`elapsed >= Duration` で無条件に `Complete` する(`:1192-1195`)。`extrapolationMode` はクリップ外挿の挙動であり、ループ再生にはならない。`CutsceneDataValidator` にも Wrap の検査は無い。

**直し方**: Loop を実装する(`Complete` の代わりに `elapsed -= Duration` + マーカーカーソルのリセット)か、`Wrap` を `[HideInInspector]` にして「現状 None のみ対応」を Tooltip / Validator の Info に書く。P-13 発効後はフィールドの削除ができなくなる点にも注意。

### editor(Edit Mode プレビュー)

**P2-6. `CutsceneEditModePreviewProvider.OnEditorUpdate` が、Cutscene を触っていなくても毎エディタ更新でシーン全走査する**

`Assets/DDrive/Editor/Cutscene/CutsceneEditModePreviewProvider.cs:135`:

```csharp
var contexts = Object.FindObjectsByType<CutsceneDirectorContext>(FindObjectsSortMode.None);
```

`[InitializeOnLoad]` で `EditorApplication.update` に常時購読(`:75`)しているため、**D-Drive の Editor アセンブリが入っているだけで**、どのシーンを開いていても毎フレーム全 GameObject を走査して配列を確保する。早期 return は `Application.isPlaying` と「見つからなかったら」だけ。docs/44 の P2-2 / P2-3(SceneView 再描画ごとの全走査)と同種で、こちらは**常時**動く点がより重い。

**直し方**: `_sessions` が空かつ直近の `PrepareContext` が無い間は走査間隔を落とす(例: 0.5 秒に 1 回だけ再探索してキャッシュ)、または `ObjectChangeEvents` / `EditorSceneManager` のイベントでキャッシュを無効化する。

**P2-7. Edit Mode 用 Manager 群の破棄が `beforeAssemblyReload` にしか繋がっていないため、「Enter Play Mode without Domain Reload」ではプレビュー用の Manager・プール・AudioSource が Play Mode へ持ち越される**

`CutsceneEditModePreviewProvider.cs:80`(`AssemblyReloadEvents.beforeAssemblyReload += TearDown`)と `:241-250`(`OnPlayModeStateChanged` は `ResetCapture` + `_sessions.Clear()` だけで `_managers` を触らない)。

ドメインリロードを切っている場合、`_managers`(`CutsceneEditModeManagers`)とその `[D-Drive] Cutscene Edit Preview` ルート配下の `SeSourceTemplate` / プールのクローン・Editor 用 `VfxManager` 等がそのまま Play Mode に残り、`SceneVfxPreviewDriver` / `SceneCameraShakePreviewDriver` / `EditorHapticsPreviewDriver` は自前で `EditorApplication.update` に乗っているため**Play Mode 中も自走**する(本番 Manager と二重にカメラ揺れ等を出す)。監視役自体は `:126-129` で止まるので、止まっているのは `FireEnabled` の同期と `_managers.Tick` だけ。

**直し方**: `OnPlayModeStateChanged(ExitingEditMode)` でも `TearDown()` を呼ぶ(`EnteredEditMode` で必要になれば作り直す作りなので副作用は無い)。

**P2-8. `CutsceneEditModeCameraWriter` が Edit Mode で `Camera.main` の Transform を Undo 無しで書き換え、Play Mode 突入時は「控えた姿勢」を復元せずに捨てる**

`Assets/DDrive/Editor/Cutscene/CutsceneEditModeCameraWriter.cs:50-57`(`cam.transform.SetPositionAndRotation(...)`・`cam.fieldOfView` を直書き)と `:92-96`(`ResetCapture` は `_hasOriginal=false` にするだけで復元しない)。`OnPlayModeStateChanged(ExitingEditMode)` からこの `ResetCapture` が呼ばれる(`CutsceneEditModePreviewProvider.cs:247`)。

Edit Mode での Transform 変更はシーンを dirty にするため、**プレビューでカメラを動かした状態のまま Play Mode に入る → 戻ってくる → 保存**すると、確認用シーンの Main Camera がカットシーンの姿勢で確定する。docs/12 §3「Editor / ツール: 全操作 Undo 対応」に照らすと、既存の `SceneCameraShakePreviewDriver` 等と同じ種類の逸脱ではあるが、**復元を諦める分岐が明示的に入っている**のはこのコードだけ。

**直し方**: `ResetCapture` を「復元してから捨てる」にする(`RestoreIfNeeded` を公開して `ExitingEditMode` で呼ぶ)。書き込みを `Undo.RecordObject(cam.transform, ...)` で包むかは既存ドライバ群と合わせて方針を決める(§4.4 の実装メモに残す)。

### import

**P2-9. カメラの回転カーブが Euler(`localEulerAnglesRaw.*`)で取り込まれた場合、抽出できずに「回転 identity」で静かに成立してしまう**

`Assets/DDrive/Editor/Cutscene/CutsceneCameraCurveExtractor.cs:44-47`は `m_LocalRotation.x/y/z/w`(四元数)だけを探し、4 本そろわなければ `:58-62` で `RotX/Y/Z` を空カーブ・`RotW` を定数 1(= identity)にする。`:69` の戻り値は `posX != null || ... || fov != null` なので、**位置さえ取れていれば `found=true` になり警告が出ない**(`CutsceneImportService.cs:477-481`)。

FBX の回転補間設定(`ModelImporter.animationRotationError` / Euler 保持)によっては `localEulerAnglesRaw.x/y/z` で入るため、docs/26 §7.3 で「未検証」とされている領域のうち**画角・ピントだけでなく回転も同じリスクを持つ**。症状は「カメラが平行移動しかしない」で、これも気づきにくい壊れ方。

**直し方**: `localEulerAnglesRaw.x/y/z`(および `m_LocalRotation` が無い場合の `localEulerAngles.*`)をフォールバックとして読み、Quaternion に変換して書く。少なくとも「回転カーブが取れなかった」を `report.Log` の Warning に格上げする。

**P2-10. 小物(Props)のトラックに FBX 全体の同一 AnimationClip をそのまま割り当てており、docs/26 §5.2-3 の「まとまりごとに AnimationClip を切り出す」が未実装**

`CutsceneImportService.cs:321-329`(ルート直下の非カメラ子ノードを列挙)→ `:334-351`(`ProcessPropChild`)→ `:386-436`(`BuildOrUpdateAnimationRoleTrack`)。`clip` は `AnimSourceLoader.Load`(`Editor/Import/ImportRuleHandlers.cs:116-135`)が返す **FBX の先頭サブアセット 1 本**で、カメラ・全小物のカーブが同居している。これを小物の数だけ別トラックに、しかも `Target=SpawnModel` で別々の ModelData にバインドするので、各クリップのカーブパス(`PRP_Sword` 等)はバインド先の階層と一致せず、**実際には何も動かない**可能性が高い(実 FBX が無いため実挙動は未確認)。

**直し方**: ノードごとにカーブを抜いた AnimationClip を作ってサブアセットとして永続化する(P1-4 と同じ仕組みが要る)。当面は「小物は 1 ノードのみ / パスが一致する構成で使う」制限を docs/26 §5.2 と `cutscene-maya-export.html` に明記する。

**P2-11. `OnPreprocessModel` の中からプロジェクト全体の `AssetSearch.FindAssets` + `LoadAssetAtPath` を呼んでいる**

`Assets/DDrive/Editor/Cutscene/CutsceneFbxPostprocessor.cs:27`(`CutsceneImportProfile.FindOrDefault()` → `CutsceneImportProfile.cs:35-48` の `AssetSearch.FindAssets` + `LoadAssetAtPath`)と `:50`(`CutsceneImportService.FindModelDataByIdentifier` → `CutsceneImportService.cs:527-537` で `t:ModelData` の全走査 + `LoadAssetAtPath`)、さらに `:56` で `model.Avatar` を触る。

インポートコールバック中の `AssetDatabase` 参照は Unity が非推奨としている経路で、インポート順によっては「まだインポートされていない ModelData」を引けない/再入的なインポートを誘発する。`AssetSearch` はプロジェクト変更までキャッシュする([docs/09](09_editor_tools.md) §9)ので、**同一バッチで先に作られた ModelData を見落とす**ことも起きうる。既存の `MayaModelPostprocessor` が同じことをしているかは本レビューでは未確認。

**直し方**: `OnPreprocessModel` では `animationType = Human` までにして、`sourceAvatar` の設定は `delayCall` バッチ(`Flush` → `ProcessPaths`)側で `ModelImporter` を取り直して `SaveAndReimport` する、が定石。少なくとも `AssetSearch.Invalidate()` の要否をコメントに残す。

**P2-12. ショットのグループ化キーに区切り文字が入っていない(`category + "" + shot`)**

`CutsceneImportService.cs:96`:

```csharp
var key = category + "" + shot;
```

`""` は区切り文字の書き忘れと読める。`Battle/Opening01.fbx` と `BattleOpening/01.fbx` のように「カテゴリ末尾 + ショット先頭」が同じ文字列になる組み合わせで 1 つの `ShotGroup` に混ざり、`CameraPropsPath` が後勝ちで上書きされる(`:109`)。実害の確率は低いが、直すのは 1 文字。

### validator

**P2-13. docs/26 §4.6.4 が定めた「`renderPostProcessing = false` のカメラでは DoF が効かないので警告 1 回」が実装されていない**

`Runtime/Cutscene/DDriveCutsceneCameraApplier.cs:224-286`。`EnsureVolume` は `volumeLayerMask` が 0 のときだけ警告し(`:234-241`)、`UniversalAdditionalCameraData.renderPostProcessing` は一切見ない。P1-2 と合わせると「確認用シーンではピントが効かない(警告も無い)」「本番カメラでは全面ボケる」という両極端になる。

**直し方**: `EnsureVolume` で `data.renderPostProcessing == false` なら警告 1 回 + `Focus` を実質 Off にする。

**P2-14. `CameraExecutionOrderValidator` が Run All のたびに `Assets/` 配下の `.cs` 全件(現状 659 ファイル)を読み込む。D-Drive 自身のテストも Info に出る**

`Assets/DDrive/Editor/Validation/CameraExecutionOrderValidator.cs:169-204`(`Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)` + 1 件ずつ `File.ReadAllText`)。`IsDDrivePath` による D-Drive 除外は (a) の実行順検査にしか掛かっておらず、(c) のテキスト走査は素通し。実測(grep)では現在ヒットするのは `Assets/DDrive/Tests/Runtime/CutsceneTimelineTracksTests.cs`(`WaitForEndOfFrame` を使うテスト)1 件だけで、**D-Drive 自身のテストコードについて Info を出し続ける**ことになる。

`ForbiddenApiScanner` と同じ走査だとコメントにあるが、あちらはメニューから明示実行する検査で、こちらは `Run All` / CI の `ValidateAll` に毎回乗る。docs/12 §3「1000 件規模での動作確認」の対象。

**直し方**: (c) も `Assets/DDrive/` を除外する(持ち込み先のゲームコードを見るのが目的なので除外して問題ない)。読み込みを `File.ReadLines` の逐次判定にする、または `Run All` の中でも明示オプションにする。

---

## 整理項目(バグではない)

### runtime

- `Runtime/Cutscene/CutsceneManager.cs:1209` — `Tick` から毎フレーム `instance.Slot.Root.GetComponent<CutsceneCameraStateHolder>()` を引いている。holder は `RentDirector` で 1 回だけ付けるので `CutsceneDirectorSlot` に持たせればゼロコストになる(P1-1 の修正と同じ場所)。
- `Runtime/Cutscene/CutsceneManager.cs:637-649` — `ReturnDirector` が `SetGenericBinding` を解除しない。再利用時は前回の Timeline のトラックに対するバインドが残り、`Transform`/`Animator`(= SpawnModel した Model の Animator も含む)への参照を Director が持ち続ける。
- `Runtime/Cutscene/CutsceneManager.cs:653-685` — `Flags.Net=Cosmetic` かつ `PredictLocal=false` のとき `Play()` の戻り値は常に `Handle.Invalid`。呼び出し側は Cancel / Skip / `WaitAsync` / `IsInputLocked` のいずれも使えない(`WaitAsync` は即完了する)。Presentation と同じ既存仕様なので本レビューでは指摘に留めるが、[ProgrammerManual](ProgrammerManual/) に 1 行あると事故が減る。
- `Runtime/Cutscene/CutsceneManager.cs:145-159` — コンストラクタで `Subscribe` / `ClientConnected +=` するが、解除する経路(`Dispose`)が無い。`DDriveRuntimeBootstrap.Teardown` も `loop.Unregister(Cutscene)` だけ(`Runtime/Loop/DDriveRuntimeBootstrap.cs:492`)。`PresentationManager` も同様なので既存の流儀どおり。
- `Runtime/Cutscene/CutsceneManager.cs:104, 973, 1005` — `_skipWarned` を「Skip=Disabled の警告」と「ToMarker が見つからない警告」で共用している。同じ Data で両方が起きることは無いので実害は無いが、意味の違う 2 つを 1 つの HashSet に入れているのは読みにくい。
- `Runtime/Cutscene/CutsceneManager.cs:432-441` — `WarnUnresolvedBinding` は既に警告済みでもキー文字列(`$"{id}:{trackName}"`)を毎回生成する。`ApplyBindings` は Play ごとに全バインドを回るので Spawn 経路の alloc(docs/12 §3 の既知課題群と同じ扱い)。
- `Runtime/Cutscene/CutsceneManager.cs:307-333` — docs/26 §4.2.1 の「Self / Target にバインドしたキャラは Animation トラックのオフセット(`ApplyTransformOffsets`)を原点の姿勢に設定する」は**未実装のまま**(`ApplyTransformOffsets` は Cutscene 配下に 1 件も無い)。6-10a 実装メモの TODO が 6-10b〜d でも解消されていないことを確認した(docs/26 §7.3 の記載と一致)。
- `Runtime/Cutscene/Tracks/CutscenePresentationClip.cs:45` — `new PlayContext { Self = root }` で `Position` を入れていない。Presentation 側が `ctx.Position` を使うトラック(Vfx/Se の `TrackTargetMode` や CameraShake)では原点扱いになる。`root.position` を入れるだけで揃う。
- `Runtime/Cutscene/Tracks/CutsceneCameraClip.cs:39-42` — クラス既定の `BlendIn/BlendOut` は `ValueDef.Constant01(1f)` = `Duration` 0(`Foundation/ValueDef/ValueDef.cs:24, 49-55`)で「ブレンド無し」。docs/26 §4.6.2 の「既定 0.25s / EaseInOutSine」は取り込み時のみ(`CutsceneImportService.cs:494-502`)。Timeline ウィンドウで手動追加した Camera クリップはブレンドしない。
- `Runtime/Cutscene/Tracks/CutsceneCameraClip.cs:192-200` — 各入力の重みを `totalWeight` で正規化しているため、Timeline のクリップ ease-in/ease-out(重み < 1)がカメラには効かない(常にフル強度)。仕様として意図しているならコメントに残す。
- `Runtime/Cutscene/CutsceneManager.cs:1188-1195` — `AdvanceMarkers` の中で Cancel された場合でも、その後 `_events.Tick(instance.EventCtx, dt)` が `_events.End` 済みの ctx に対して呼ばれる。EventBus 側が無視するだけなら無害だが、順序を `if (!instance.Done)` で囲うと意図が明確になる。

### editor

- `Editor/Cutscene/CutsceneEditModeManagers.cs:100-101, 107-113` — Event マーカー用の `_eventMarkerBus` を `Tick` していない。`AssetEvent` の `Repeat`(EveryNSeconds)と `KeepWhilePlaying` の後始末が Edit Mode では効かない(コメントに「簡易実装」と明記済み)。
- `Editor/Cutscene/CutsceneEditModeDirectorSetup.cs:71` — `EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline")` のメニューパス直書き。CLAUDE.md §0-6 は D-Drive のメニューについての規約だが、Unity 側のパスも定数化しておくとバージョン差異のときに 1 箇所で済む。
- `Editor/Validation/CameraExecutionOrderValidator.cs:54, 60-65` — `private static ValidationContext _lastRunContext` が最後の `ValidationContext`(= 全アセットの `List`)を保持し続ける。docs/44 整理項目の `CatalogAddressCoverageValidator` と同根。
- `Editor/Validation/CameraExecutionOrderValidator.cs:75-85` — `info.TypeName == nameof(DDriveCutsceneCameraApplier)` の**クラス名一致**で Applier を同定するため、持ち込み先に同名クラスがあると誤判定する。`AssetPath` も併せて見るとよい。
- `Editor/Cutscene/CutsceneImportService.cs:229-233` — `timeline.editorSettings.frameRate` を入れるのは `isNew` のときだけ。既存 CutsceneData に後から Timeline を作り直した場合(`:193-202`)は 60 のままになる。
- `Editor/Cutscene/CutsceneShotParser.cs:11, 33-42` — `StripDuplicateSuffix` は `^(.*)_(\d+)$` なので `Robot_01` → `Robot`、`Enemy_2B` はそのまま、のように「数字で終わる正規の識別子」も落とす。docs/26 §5.4 の想定(`_2` 以降)どおりではあるが、境界を `cutscene-maya-export.html` に 1 行書いておくと事故が減る。
- `Runtime/Cutscene/CutsceneDataValidator.cs:353-366, 455-484` — `CheckCosmeticSimulatedReference` / `FindAsset` が参照 1 件ごとに `ctx.AllAssets` を線形走査する(クリップ数 × 全アセット数)。現状の規模では問題無いが docs/12 §3「1000 件規模」の対象。
- `Runtime/Cutscene/CutscenePreviewHarness.cs:15-22` — コメントが `fe17586` 以前のまま(「Edit Mode(非再生中)のスクラブでは Camera/SE/VFX 等の実 Manager 適用までは届かない」「TODO として docs/26 に記録した」)。Edit Mode プレビューは実装済みなので、現状に合わせて直す。
- `docs/11_tasks.md:277`(6-10d 行) — Inspector 導線を「Timeline ウィンドウで開く〔`AssetDatabase.OpenAsset`〕」と書いたままで、`fe17586` の `CutsceneEditModeDirectorSetup.OpenTimelineWindow`(確認用シーン + プレビュー用 Director + Bindings 解決)に置き換わったことが反映されていない。**docs/26 §4.4 の実装メモにしか記録が無い**。CLAUDE.md §3-4 の「同じ PR で docs を更新する」に該当。
- `CLAUDE.md` §1 の進捗行 — 「Timeline(6-10a〜d)完了。次は P チケット」のままで `fe17586`(Edit Mode プレビュー)が入っていない。docs/44 の同種指摘(`ead2149` / `9705e2a` 未反映)と合わせて 1 回で直すとよい。
- `docs/DesignerManual/cutscene-maya-export.html:69` — P1-2 の修正後に「取得できなければ空のまま(ピントは書き込まれません)」が正しくなる。修正前に配信し直す場合は文言を実装に合わせる必要がある。
- `Assets/DDrive/Editor/Cutscene/CutsceneDataEditor.cs:56, 88` — `FindFirstObjectByType<CutscenePreviewHarness>()` を `OnInspectorGUI` のたびに 2 回呼ぶ(Inspector 再描画ごと)。1 回に纏められる。

---

## テストの穴(この範囲で追加すべきテスト)

1. **偽造メッセージ**(`CutscenePlayMsg` / `CutsceneSeekMsg` / `CutsceneCancelMsg`)のテストが 1 件も無い。`IsAuthorizedSender`(`CutsceneManager.cs:689-702`)と `ConsumeSeekCancelBudget`(`:704-723`)は Presentation からの移植だが、Cutscene 側は「発行者でない Client からの Seek/Cancel を破棄する」検証が無い。`Tests/Runtime/CutsceneManagerTests.cs` の `DelayedNetBridge` は `InjectReceive` 相当を持っているはずなので、docs/44 テストの穴 3 と同じ手口で追加できる。
2. **Late Join**(`OnClientConnected` → 台帳の再送)のテストが無い。P1-3(Registry 未 ready)と P2-1(SelfNetId=0)のどちらも、このテストを書けば 1 回で顕在化する。
3. **Director 再利用時のカメラ状態**(P1-1)。「カメラ付き Cutscene を Cancel → カメラ無し Cutscene を再生 → `Camera.main` が動かないこと」を PlayMode テストで固定する。
4. **`Focus=Volume` + 空 `FocusDistance` カーブ**(P1-2)。既存の `CutsceneTimelineTracksTests.CameraClip_StepFpsQuantizes...` は `Focus = CameraFocusMode.CameraOnly` に逃がしており(`:196`)、Volume 経路が 1 度もテストされていない。
5. **`SourceFrameRange` 取り込みの永続性**(P1-4)。`CutsceneFrameRangeTrimmerTests` はメモリ上のカーブ切り出ししか見ていない。`ProcessPaths` 後に `AssetDatabase.Contains(asset.clip)` が true であることを assert する EditMode テストが要る。
6. **`IsInputLocked` を終了後に呼ぶ**(P2-4)。`LogAssert.NoUnexpectedReceived()` を併用すれば無効ハンドル警告の有無で白黒がつく。
7. **`Wrap = Loop` / `Hold`**(P2-5)。現状の挙動(必ず完了する)を仕様として固定するか、実装して固定するか。
8. **`CutsceneEditModePreviewProviderTests` が静的 `_managers` を破棄しない**(`Tests/Editor/CutsceneEditModePreviewProviderTests.cs:30, 47-48`)。`PrepareContext` は `EnsureManagers()` を呼び、`CutsceneEditModeManagers` が `[D-Drive] Cutscene Edit Preview` ルート(+ `SeSourceTemplate` とプールのクローン)を**そのとき開いているシーン**に `StageUtility.PlaceGameObjectInCurrentStage` で置く(`CutsceneEditModeManagers.cs:88-93`、`Editor/Preview/EditorAudioFactory.cs`)。テストは `_go` しか消さないので、**EditMode テストを流すとこのルートがユーザーのシーンに残る**(ドメインリロードまで)。`HideFlags.DontSave` なので保存はされないが、報告されている `SeSourceTemplate(Clone)` 残留の説明になりうる。TearDown で破棄する API(例 `CutsceneEditModePreviewProvider.TearDownForTests()`)を足す。
9. **`CutsceneImportServiceTests` が実 Addressables 設定を触る**(`Tests/Editor/CutsceneImportServiceTests.cs:41-52` の `AddressablesSync.RemoveEntriesUnder` + `AssetCreationService.Create` 経由の登録)。docs/44 テストの穴 6 と同根で、テスト前後の `git status --porcelain` 一致を 1 度実測して記録するとよい。

### 追加されたテストの評価(依頼事項「追加テストが本当に検証しているか」)

読んだ範囲では、6-10a〜d + `fe17586` のテストは**おおむね意味のある検証**をしている。特に:

- `CutsceneTimelineTracksTests.Applier_DetectsOverwrite_WhenLaterScriptWritesCameraInLateUpdate`(`:224-262`)は、実行順 1001 のプローブを実際に置いて `endCameraRendering` 比較(§4.6.5 検出 2)が 1 回だけ警告することまで固定しており、契約違反の検出が本当に動くことを示している。
- `CutsceneTimelineTracksTests.SkipToMarker_SeeksToMarkerTime_NotDuration_AndDoesNotRefire`(`:108-132`)は「マーカー時刻へ飛ぶ」「跨いだマーカーは無音」の両方を押さえている。
- `CutsceneImportServiceTests.ProcessPaths_Reimport_PreservesManuallyAddedTrackAndCameraSettings`(`:204`)は 6-10c の肝(デザイナーの追加を壊さない)を直接検証している。
- `CameraExecutionOrderValidatorTests` は `ScriptOrderProvider` の差し替えで ProjectSettings を汚さずに (a)(b) を両側から検証しており、`DDriveScriptAtThreshold_IsNotWarned` で除外規則も押さえている。
- **PlayMode テスト(`Tests/Runtime/Cutscene*.cs` 3 本)は実アセットを 1 件も触っていない**(`AssetDatabase` / `SaveAssets` / `SetDirty` の呼び出しが 0 件。grep で確認)。docs/44 の「Tests/Runtime に汚染経路は無い」は Cutscene 追加後も維持されている。

---

## 誤検知(疑ったが読み直して問題無しと判断)

次のレビューで同じ道を通らないために残す。

### runtime / net

- **`AdvanceMarkers` が Unity 標準の Signal 配送を使っていないのは手抜き** — `Evaluate()` 駆動では `TimeNotificationBehaviour` が通知を送らない、という docs/26 §4.3 実装メモの判断どおりで、`CutsceneTimelineTracksTests` が実際に発火することを固定している。`MarkerTrack` 派生にしているので Timeline ウィンドウ上の編集(ドラッグ・スナップ)も効く。
- **中継された Cosmetic メッセージの `senderId` が Host に化けて発行者検証が素通しになる** — `NgoNetBridge.RequestBroadcastRpc`(`Runtime/Net/NgoNetBridge.cs:594-612`)が**真の発行者**を `SendToAll` に渡し、Host 自身の `Broadcast` も `LocalClientId` を使う(`:340-345`)。Presentation と同じ前提が Cutscene でもそのまま成立する。
- **`Flags.Net = Simulated` が未処理** — `PlayData`(`:190`)は Cosmetic 以外をローカル再生に落とし、`CutsceneDataValidator.cs:105-108` が Info を出す。docs/26 §4.7 の表どおり。
- **`ResolveBindingObject` が未解決時に例外を投げる / 止まる** — `null` を返して `SetGenericBinding(track, null)` するだけで、警告 1 回(同一 Data + トラック名で重複排除)のうえ継続する。TL;DR #4 どおり。
- **`Cancel()` が Cosmetic のとき自分側を止めずに Broadcast だけして戻るのは取りこぼし** — `:941-945`。自分の Broadcast も自分に返ってくる(NGO は Host 経由で送信者にも配送、Loopback も同様)前提で、`OnReceiveCancelMsgInternal` が全員分をまとめて止める。「Broadcast 前に自分だけ飛ばない」(§4.7)という明示の設計。
- **入力ロックが Cancel/Complete のどちらかで解除漏れする** — 解除は `ReleaseInputLockIfNeeded`(`:1145-1159`)1 本で、`CancelInternal`(`:953`)と `Complete`(`:1301`)の両方から必ず通る。`LockCounted` フラグで二重解除も防いでいる。`InputLock_EdgeFires_OnlyOnZeroOneTransition...` が入れ子のエッジも固定済み。
- **`Complete` が `PlayLocalInternal` の中から呼ばれると、返す Handle が最初から無効**(`:253-257`) — ネット復元で既に尺を超えている場合だけの経路で、その手前(`:773-776`)で `elapsed >= duration` は早期 return しているため実質到達しない。ローカル再生では `elapsedSeek=0` なので発生しない。

### editor

- **`TimelineEditorWindow.SetTimeline` のリフレクションが Unity 6000.3 で動かない** — `Library/PackageCache/com.unity.timeline@…/Editor/TimelineEditor.cs:20` の `public static TimelineEditorWindow GetWindow()` と `Editor/Window/TimelineEditorWindow.cs:37` の `public abstract void SetTimeline(PlayableDirector director)` を確認した。`BindingFlags.Public | Static` + 引数 `PlayableDirector` で一致する。見つからなければ `?.` で安全に諦める作りなので、将来の変更でも例外にはならない。
- **`CutsceneDataEditor` に `[DataEditor]` が無いのは付け忘れ** — 編集 UI は標準 Timeline ウィンドウで専用 EditorWindow を持たない([docs/09](09_editor_tools.md) §8 の対象外)。`DataEditorRegistryTests` の Exempt と同じ扱いで、コメント(`:10-13`)にも理由が書かれている。
- **Edit Mode プレビューが Play Mode の挙動を変えている** — `CutsceneManager.RentDirector` が `FireEnabled = true` 固定 + `ManagerRefs = null`(`:631-632`)で、各クリップは `ManagerRefs?.X` が null のとき従来の静的ファサード経路にそのまま落ちる(`CutsceneSeClip.cs:55-66` ほか)。`CutsceneDirectorContextGatingTests` がクリップ 5 種について「`Context=null` でも発火しない」「`FireEnabled=false` で発火しない」「フォールバック経路で発火する」を固定している。**ただし `Context == null` のときは発火しない**ので、Play Mode の CutsceneRoot に Context が付かない経路があると無音になる(現状は `RentDirector` が必ず付ける)。
- **`CutsceneEditModePreviewProvider` が Play Mode 中も Director を駆動して二重になる** — `:126-129` で `Application.isPlaying` を最初に弾いている。カメラ側も `CutsceneEditModeCameraWriter.Apply` が同じガードを持つ(`:30`)。相互排他は成立している(ただし P2-7 の Dispose 漏れは別問題)。
- **`CameraExecutionOrderValidator` がアセット 1 件ごとに重複報告する** — `_lastRunContext == ctx` のガード(`:60-65`)と `DataValidationRunner.ProjectWideValidatorNames` への登録(`Editor/Validation/DataValidationSection.cs:162-171`)の両方が入っている。docs/41 P2-6 の集約が維持されている。
- **`MonoImporter.GetExecutionOrder` が 0 を返す問題への対処が場当たり** — `DefaultScriptOrderProvider`(`:122-161`)のコメントどおり「ProjectSettings に明示登録されていないスクリプトは属性値を反映せず 0 を返す」実機確認の結果に基づく実装で、docs/26 §6 実装メモ(3)と一致する。「明示的に 0 に上書きされた場合と区別できない」も明記済み。

### docs / 依存

- **asmdef の Timeline / URP 追加が docs に反映されていない** — `Assets/DDrive/Runtime/DDrive.Runtime.asmdef` の `Unity.Timeline` / `Unity.RenderPipelines.Universal.Runtime` / `Unity.RenderPipelines.Core.Runtime` は、[docs/42](42_distribution.md) `:187` `:190` の依存表(「6-10 以降は Runtime も参照 = ゲーム実行時の必須依存」)と一致している。`Packages/manifest.json` への新規追加は無い(§6 の記述どおり)。
- **`AssetType.Cutscene` 追加の周辺配線漏れ** — `AssetNamingService`(`CUT` / `Cutscene` フォルダ)、`AssetCreationService`(`CutsceneCatalog`・ContentHash 対象)、`AssetIdGenerator.KnownPrefixes`(`CUT`)、`EditorAnchorRegistry`(プレビュー Registry への収集)がいずれも入っている。

---

## 件数

| 区分 | 件数 |
|---|---|
| P1 | 5(runtime/camera 2 / net 1 / import 1 / editor 1) |
| P2 | 14(net 2 / runtime 3 / editor 3 / import 4 / validator 2) |
| 整理項目 | 22(runtime 11 / editor・docs 11) |
| テストの穴 | 9 |
| 誤検知 | 17 |

## 変更履歴

- 2026-09-19: 新規作成。`f02c68b` / `02bc145` / `e562059` / `883a372` / `fe17586`(+ `bf76062` の Cutscene 分)を読み取り専用レビュー(Unity MCP 未使用、作業ツリーの HEAD = `bf76062` を参照)。
- 2026-09-20: P1-1〜P1-5 の 5 件を対応(各節に「✅ 対応済み」を追記。詳細は [docs/26_timeline.md](26_timeline.md) の各実装メモ「docs/45 P1 対応(2026-09-20)」を参照)。EditMode 931/931・PlayMode 758/758 green(Unity MCP `mcp__UnityMCP__*` で確認)。P2 以下・整理項目・テストの穴の残りは未対応。
