# 26. Timeline 連携(Maya FBX 取り込み + D-Drive トラック) 詳細設計

関連: [08_presentation.md](08_presentation.md) §3 Timeline トラック / [05_model_animation.md](05_model_animation.md) / [02_core_framework.md](02_core_framework.md) §3 AssetEvent / [14_networking.md](14_networking.md) §3 NetMode・§5 Presentation の同期 / [16_camera_haptics.md](16_camera_haptics.md) Part A(CameraFx) / [13_extensions.md](13_extensions.md) B-9 カットシーン種別 / [42_distribution.md](42_distribution.md) §3.5 依存・§5.10 / [11_tasks.md](11_tasks.md) 6-10a〜6-10d

> 2026-09-13 ドラフト。ユーザー要望:「Maya のカメラやアニメを Timeline に流し込み、Unity 側で SE や VFX を足す。ほかのアセットと同様にイベントも付けたい。Maya 側スクリプトは無しが望ましい」。
>
> **2026-09-13 ユーザー決定: 実装は P6 の最後(6-10a〜d)、運用開始後に行う**(実際にカットシーンを作る段階がまだ先のため。旧チケット番号 5-3a〜d)。
>
> **2026-09-18 設計確定**: §7 の未決事項 6 件にユーザー回答が出た(短い演出のみ / カメラはシームレス / 1 ショット = 1 FBX(フレーム範囲の逃げ道あり)/ Humanoid・アニメ FBX 分離 / fps 30・60 切替 / ネットは両方)。あわせて追加要望 2 件(**カメラのステップ fps を Unity 側で調整できること**、**ピント等のカメラ設定の持ち越し**)を設計に落とした。§3.1(Presentation との使い分け)・§4.6(カメラ)・§4.7(ネット)・§5.3(fps)・§5.4(Humanoid)が新規/改定。
>
> **実装メモ(2026-09-18、6-10a)**: CutsceneData/CutsceneManager/`Cutscene.Play`・ネット(§4.7)・入力ロック(§4.5.1)を実装した([11_tasks.md] 6-10a 行に要約)。設計からの補足・簡略化 3 点を記録する。(1) §4.1 のフィールド一覧に無かった `OriginAnchorName`(Origin=AnchorPoint の対象名)・`SkipToMarkerKey`(Skip=ToMarker の対象マーカー名)を実装上必要な欄として追加した(いずれも自明な補完で、ユーザー判断を要する変更ではないと判断)。(2) §4.5「PlayableDirector は Pool から借用」は、`PoolService` がプレハブの Instantiate を前提にしており CutsceneRoot に元アセットが無いため、専用の軽量な free-list(`CutsceneManager` 内部)で代替した(「使い捨てない」という意図は同じ)。(3) §4.2.1 末尾の要検証事項(Self/Target バインドの Timeline トラックオフセット)は 6-10a では未実装のまま TODO とした(CutsceneRoot の姿勢は Origin どおりに置くが、Self/Target 自体のクリップに対するオフセット適用は行わない。6-10b でカメラクリップ・実機確認と合わせて対応する)。Skip=ToMarker は 6-10b の D-Drive Signal マーカー導入までは Immediate(末尾へ飛ばす)にフォールバックする(警告 1 回)。6-6 の「未知キー保留バッファ」(Signal/Cancel が Play より先に届く場合の防波堤)は 6-10a では実装していない(実機確認で同種の問題が出た場合に追加する)。
>
> **実装メモ(2026-09-18、6-10b)**: D-Drive トラック群(`Runtime/Cutscene/Tracks/`)と Camera クリップを実装した([11_tasks.md] 6-10b 行に要約)。設計からの補足・簡略化を記録する。**(1) Event/Signal/Shake/Haptic マーカーは Unity 標準の Signal 配送(`INotification`/`INotificationReceiver`)を採用しなかった**: 実装調査の結果、`TimeNotificationBehaviour.PrepareFrame`(Timeline 側のマーカー通知の実体)は `FrameData.EvaluationType.Evaluate`(`PlayableDirector.Play()` を呼ばず `Evaluate()` だけを呼ぶ経路。まさに CutsceneManager の Manual 駆動そのもの)のフレームでは通知を一切送らない実装(コメント「Never trigger on scrub」)であることが分かった。native 通知に頼るとバージョン依存の発火不安定リスクを抱えるため、代わりにマーカーは `Marker`(`IMarker`)だけを実装する「時刻付きデータ」として扱い、`CutsceneManager.CollectMarkers`/`AdvanceMarkers` が Play() 時に `TrackAsset.GetMarkers()` で時刻順に集め、Tick() で `elapsed` が跨いだら直接発火する(既存の `EventBus.Tick`/`PresentationManager.FireDueTracks` と同じ「跨いだら発火、Seek は無音でスキップ」パターンに揃えた)。PlayMode テストで実際に発火することを確認済み。**(2) 静的ファサードの完全修飾が必須**: `DDrive.Runtime.Cutscene.Tracks` 名前空間のコードから素の `Vfx`/`Audio`/`Ui`/`Anchors`/`Presentation`/`CameraFx`/`Haptics` を書くと、`DDrive.Runtime` 配下の同名子ネームスペース(`DDrive.Runtime.Vfx` 等)が名前解決で先に見つかり `CS0234` になる(既存コードはこれらの静的ファサードを他ネームスペースから素の名前で呼んだ例が無かったため未発見だった)。`DDrive.Runtime.Vfx.Vfx.Spawn(...)` のように完全修飾する。**(3) カメラの所有権は単純化**: 同時に複数の Cutscene がカメラクリップを持つ場合、最初の 1 本が勝ち、2 本目以降は警告 1 回でカメラ以外のトラックだけ再生する(合成しない)。Skip/Cancel によるカメラの戻りは Timeline の最終評価値をそのまま使うため、`BlendOut` の秒数どおりに実時間で滑らかに戻るわけではない(既存の Cleanup 経路で即座に復元されるため大きな破綻はないが、厳密な wall-clock ブレンドアウトは未実装。TODO)。**(4) Skip=ToMarker を接続**: 6-10a の TODO(D-Drive Signal マーカー導入まで Immediate にフォールバック)を解消し、`CutsceneSignalNotification.Key` 一致のマーカーを走査して対象秒を求めるようにした(見つからなければ Immediate にフォールバック、警告 1 回)。
>
> **実装メモ(2026-09-18、6-10c)**: Maya FBX(スクリプト無し)→ CutsceneData + TimelineAsset の自動構築を実装した([11_tasks.md] 6-10c 行に要約)。設計からの補足・簡略化・要検証の結果を記録する。**(1) `IImportRuleHandler` は採用しなかった**([09_editor_tools.md] §1.1 に「6-10c で `IImportRuleHandler` を 1 つ追加する形で拡張する想定」とあったが、実装時に見送った): 既存の `IImportRuleHandler`(`ImportRuleService`)は「1 元ファイル = 1 Data」の `Configure(data, source, path)` しか持たず、Cutscene の「1 ショット = カメラ+小物 FBX 1 本 + キャラごとの FBX N 本 → CutsceneData 1 個」という N:1 の対応・「自動生成トラックだけ差し替え、デザイナーの追加は保持」という再取り込みの個別更新を表現できない。代わりに `MayaModelPostprocessor`(FBX→MaterialData、3-7)と同じ位置付けの専用パイプライン(`CutsceneFbxPostprocessor` + `CutsceneImportService`、`Assets/DDrive/Editor/Cutscene/`)として実装し、`ImportRuleService.KnownNonTargetTypeFolders` に `"Cutscene"` を加えて汎用の案内ログ対象からは外した(`"Shaders"` が `AiStandardSurfacePreprocessor` 専用フォルダとして除外されているのと同じ扱い)。`AssetNamingService`/`ImportSourceGuid`(カメラ+小物 FBX の GUID を代表)/`SourceFbxGuids[]`(キャラ FBX の追跡、`CutsceneData` に新規追加)は §6 の設計どおり。**(2) 再取り込みの差し替え粒度はトラック単位**: 役割名(カメラの GameObject 名 / キャラの Model 識別子 / 小物のノード名)と一致する既存トラック(型も一致)を探し、見つかればそのクリップ 1 本だけを更新する(カーブ/AnimationClip 参照を上書き)。見つからなければ新規トラックを追加する。デザイナーが追加した他名称のトラック(SE/VFX/Signal 等)やカメラクリップの `StepFps`/`BlendIn`/`BlendOut`/`Focus` は新規トラック作成時にしか初期値を入れず、既存トラックの更新では触らない(EditMode テストで確認)。**Bindings も同じ方針**(既存の `TrackName` と一致するものは Target/Model を変更しない。デザイナーが SpawnModel から Self/Target に変えた場合も保持される)。**簡略化(TODO)**: FBX からノードが消えた場合の既存トラックの自動ミュートは未実装(バッチには「今の子ノード一覧」しか渡らず前回との差分を取っていないため。§5.2 の「消えたノードのトラックはミュート」は今後の課題)。1 つの Animation トラックに 2 つ目以降のクリップをデザイナーが追加した場合、再取り込みが `GetClips().FirstOrDefault()` で先頭クリップを更新するため意図と違うクリップを触る可能性がある(想定利用〔1 トラック 1 クリップ〕の範囲外)。**(3) §7.3 要検証事項の結果**: Unity Editor への MCP 接続はできたが実 Maya 素材(FBX)がこの環境に無いため、**画角/焦点距離/ピント距離/絞りの実際の FBX バインディング名、アニメ付きカスタムプロパティの取得可否、Humanoid+Timeline オフセットの実機挙動、Volume.weight 追従は今回未検証のまま**。画角は Unity の Animation ウィンドウで長年 `"field of view"` という束縛名を使う既知の挙動を前提に実装し、焦点距離/ピント距離/絞りは `m_FocalLength`/`m_FocusDistance`/`m_Aperture` 等の候補名を複数試して見つからなければ空カーブ(§4.6.4 の「取れなければ書かない」)にフォールバックする防御的な実装にした(`CutsceneCameraCurveExtractor`)。**イベント用ロケーター(`dd_focusDistance`/`dd_fStop`/`EVT_*` → Signal)は不採用**: `OnPostprocessGameObjectWithAnimatedUserProperties` の実際の挙動を実 FBX 無しで安全に検証する手段が無く、「不確実な実装を採用する」より「未対応として明記する」を選んだ(§5.5 のとおり、Unity の Timeline ウィンドウで手動でマーカーを置く運用に留める)。実際の Maya 書き出しでの確認はユーザーの今後の作業として残る。テスト: `Tests/Editor/{CutsceneShotParserTests,CutsceneCameraCurveExtractorTests,CutsceneImportServiceTests}.cs`(純ロジック〔命名規則・パス計算〕はコード上の文字列だけで検証、カメラカーブ抽出はコードで組んだ `AnimationClip`+`Camera` で検証、ショット取り込み全体は既存サンプル FBX〔UnityChan〕をカメラ+小物・キャラのファイル名に見立ててコピーして検証)。`DDrive.Editor`/`DDrive.Tests.Editor` asmdef に `Unity.Timeline` を追加(§7.1-10 で承認済みの Runtime 追加の自然な帰結。Editor 側で `TimelineAsset`/`AnimationTrack` 等を直接構築するために必要、あらためてユーザー判断を要する新規依存ではない)。
>
> **実装メモ(2026-09-18、6-10d)**: 確認用シーン・Inspector 導線・Validator 拡張・マニュアル更新を実装した([11_tasks.md] 6-10d 行に要約)。設計からの補足・簡略化・発見事項を記録する。
> **(1) fps 検査 6 種は Runtime と Editor の 2 つの Validator に分けた**: §5.3 の 6 検査のうち #4(Camera クリップの `StepFps` > `FrameRate`)・#5(整数倍でない)・#6(30/60 以外)は `CutsceneData.FrameRate` と Camera クリップの `StepFps` だけで判定できるため既存の Runtime `CutsceneDataValidator` に置けたが、#1〜#3(FBX の fps との比較)は `CutsceneImportProfile`(Editor asmdef)への参照が要るため新設の Editor 側 `CutsceneFpsValidator`(`Assets/DDrive/Editor/Cutscene/CutsceneFpsValidator.cs`)に実装した。「FBX の fps」自体は Data に保存されないため、キャラ/小物の Animation トラックが参照する `AnimationClip.frameRate`(取り込み時に FBX のテイク fps がそのまま入る、実行時プロパティで読める)で近似した。
> **(2) Humanoid/Avatar 不整合は「Warning が実際に出る側」を単体テストできなかった**: `AnimationClip.humanMotion` は実 Humanoid FBX から焼かれた場合だけ true になる読み取り専用プロパティで、テストコードから直接 true を作る手段が無い(§7.3 と同種の制約)。実装(`CutsceneDataValidator.ValidateHumanoidAvatarMismatch`)自体は「Humanoid クリップを持つ Animation トラックの Binding(SpawnModel)が Avatar 未設定の ModelData を指す」を Warning にするが、テストは「Humanoid クリップでない(既定の空 AnimationClip)ときは Avatar 未設定でも警告しない」負のケースのみを固定した。実 Maya FBX での確認はユーザーの手動確認(docs/43)に委ねる。
> **(3) `CameraExecutionOrderValidator` の実装で `MonoImporter.GetExecutionOrder` の実際の挙動を発見**: 当初「ProjectSettings の値があればそれ、無ければ `DefaultExecutionOrder` 属性」という仕様どおりに `MonoImporter.GetExecutionOrder(script)` を直接使ったが、Unity MCP 経由の実機確認(`execute_code`)で **`DDriveCutsceneCameraApplier`([DefaultExecutionOrder(1000)])に対し `GetExecutionOrder` が 0 を返す**(ProjectSettings の Script Execution Order リストに一度も明示登録されていないスクリプトは、属性値を反映せず常に 0 を返す)ことを確認した。そのため `GetExecutionOrder` が 0 のときだけ `[DefaultExecutionOrder]` 属性値へフォールバックする実装に修正した(`CameraExecutionOrderValidator.DefaultScriptOrderProvider`)。この発見は本チケットのスコープ外だが、将来「ProjectSettings の実効値」を扱うコードを書く際の既知の落とし穴として記録する。
> **(4) `CutscenePreviewHarness` は Runtime asmdef に置いた**: 確認用シーンで Play Mode 中に任意の `CutsceneData` を手動再生する薄い `MonoBehaviour` ハーネスを、当初 `DDrive.Editor.Cutscene` 名前空間(Editor asmdef)に実装したが、実機確認で **`GameObject.AddComponent<T>()` が Editor 専用プラットフォームの asmdef に属する `MonoBehaviour` に対しては失敗する**(例外もログも出ず `null` を返すだけ)ことを発見した。既存の `SceneAnimPreviewDriver`/`SceneCameraShakePreviewDriver` 等が `MonoBehaviour` ではなく `IDisposable` の素の C# クラスとして実装されている理由がまさにこれだったと判明した。`CutscenePreviewHarness` は公開ファサード(`Cutscene`/`CutsceneHandle`)しか使わず Manager を `new` したり `UnityEditor` を参照したりしないため、Runtime asmdef(`Runtime/Cutscene/CutscenePreviewHarness.cs`)に置いても既存規約に反しない。
> **(5) 確認用シーンでの目視確認は Play Mode が前提**: `CutsceneManager` は `DDriveRuntimeBootstrap`(Play Mode の起動時)/ テスト / Editor プレビュー以外では生成されない([02] §14)。§4.4 が想定した「確認用シーンに Editor 用 Manager 群を置き、Edit Mode のスクラブでも実 Manager を駆動する」の完全実装(`PreviewService` と同じ仕組み)は本チケットでは行わなかった(既存の `Scene*PreviewDriver` 群と同水準の実装が要り、3 点チケットの範囲を超えると判断)。**TODO として残す**: 代わりに確認用シーンには `DDriveRuntimeBootstrap` を配置し、Play Mode に入って `CutscenePreviewHarness`(または CutsceneData の Inspector の「再生」ボタン)で再生する運用にした。Play Mode 中は標準 Timeline ウィンドウでのスクラブも実 Manager 経由でそのまま機能する(Application.isPlaying==true のため、6-10b の各クリップの発火ガードを通過する)。
> **(6) Presentation⇄Cutscene 循環参照検出は Cutscene 側のみ**: `CutsceneDataValidator` に Presentation クリップ経由で自身に戻る循環を辿る Error 検査を追加したが、`PresentationDataValidator` 側(Timeline トラック経由で Cutscene → 元の Presentation に戻るケース)には対称の検査を追加していない(チケット範囲が `CutsceneDataValidator` のみだったため)。実害は「Cutscene から見て見つかる循環は Cutscene 側の Validation で必ず Error になる」ため片方の Validator が検出すれば Run All 全体としては気づける。対称性を持たせるかは次点の要判断として残す。
>
> **2026-09-18(同日 2 回目)**: §7.2 の未決のうち 3 件が確定した。**(1) CutsceneData / PresentationData は併存**(判断基準は「Maya の FBX を使うなら Cutscene」、`CutsceneHandle.Signal` は持たない)。**(2) `DDrive.Runtime` asmdef に `Unity.Timeline` + URP を直接追加することを承認**(CLAUDE.md §0-9 の asmdef 構成変更。持ち込み先に URP がランタイム必須依存として増える → [42] §3.5 / §5.10 に追記)。**(3) ゲームカメラ制御との実行順は D-Drive が契約として定める**(MS2026 側が未定のため。§4.6.5 を「確認事項」から「契約 + 違反の検出」に書き換え、[ProgrammerManual/rules.html](ProgrammerManual/rules.html) に追記)。§7.1 に 9〜11 として記録。**同日、残る 6 件(Cinemachine アダプタ / キャラごと FBX / AudioListener / LockInput / Skip 権限 / StepFps 既定値)もすべて提案どおりで確定し、§7.2 は「決定」の表に書き換えた(未決なし = 6-10a に着手できる状態)**。帰結として §4.5.1(入力ロックの分担)・§5.1.1(Maya 作業者の手順)を追加し、[DesignerManual/cutscene-maya-export.html](DesignerManual/cutscene-maya-export.html) を新設した。§7.3 の要検証(Unity 実機で確かめるもの)は残る。
>
> **実装メモ(2026-09-19、Edit Mode プレビュー「Timeline ウィンドウ主導」案 B)**: 6-10d が TODO として残した「§4.4 の Edit Mode スクラブでも実 Manager を駆動する」を実装した。**ユーザー決定: Edit Mode プレビューでは `CutsceneManager` を通さない**(BlendIn/Out・Skip・Cancel・入力ロック・ネットは Play Mode で確認する、という逸脱を受け入れた)。Play Mode の経路(`CutsceneManager`/`DDriveRuntimeBootstrap`/`CutscenePreviewHarness`)は無変更(既存 PlayMode テスト全件 green で確認)。
> **(1) `CutsceneDirectorContext`(Runtime、`Runtime/Cutscene/CutsceneDirectorContext.cs`)** — PlayableDirector の GameObject に付ける文脈。`bool FireEnabled` + `CutsceneDirectorManagerRefs ManagerRefs`(Audio/Vfx/Ui/Groups の 4 種、`[NonSerialized]`)を持つ。SE/VFX/UI/AnchorGroup/Presentation の 5 クリップは `CreatePlayable(graph, owner)` の `owner` から `owner.GetComponent<CutsceneDirectorContext>()` を引く(`CutsceneCameraMixerBehaviour.Owner` と同じ「owner から辿る」手法)。**`Application.isPlaying` ガードは `Context.FireEnabled` に置き換えた**(クリップ 5 種 + `CutsceneManager.Advance*Markers`)。`CutsceneManager.RentDirector` は CutsceneRoot に `CutsceneDirectorContext` を付け `FireEnabled = true` 固定・`ManagerRefs = null` のままにする。**`ManagerRefs` が null の場合は各クリップが既存どおり静的ファサード(`Audio.PlaySe` 等)へフォールバックする**ため、Play Mode の挙動は本対応の前後で完全に同一(`CutsceneManager` のコンストラクタ・`DDriveRuntimeBootstrap` の配線は変更していない — Play Mode で Manager 参照を直接注入しても静的ファサード経由と結果は同じで、テストされていない新しい配線を追加するリスクだけが増えると判断した。ドキュメント上「§4.4/§6 で ctor 配線を検討」としていたが、この理由で見送った)。Presentation クリップだけは Edit Mode 用 `ManagerRefs` を持たない(下記(3))。
> **(2) 「Timeline ウィンドウが実際に再生中か」は公開 API で判定できた**: 実機確認(Unity MCP `execute_code`)で `UnityEditor.Timeline.TimelineEditorWindow.playbackControls.Play()`/`Pause()` を叩くと、公開 API である **`PlayableDirector.state`(`PlayState.Playing`/`Paused`)がそのまま追従する**ことを確認した。スクラブ(`TimelinePlaybackControls.SetCurrentTime`)は `Paused` のまま変化しない。そのため §4.4 が想定していた「dt 相当で time が単調に進んだら再生中」という近似のフォールバックは不要になり、`CutsceneEditModePreviewProvider`(`Editor/Cutscene/CutsceneEditModePreviewProvider.cs`、`[InitializeOnLoad]`)は毎 `EditorApplication.update` で `director.state == PlayState.Playing` を `context.FireEnabled` へそのまま映すだけで済む。**Play Mode 中はこの監視役自体を完全に止める**(`Application.isPlaying` なら即 return。Play Mode の CutsceneRoot にも同じ `CutsceneDirectorContext` が付くため、止めないと二重駆動になる)。
> **(3) Edit Mode 用 Manager 群(`CutsceneEditModeManagers`)は `ScenePresentationPreviewDriver` と同じ構成方針**: 既存の 1 種別 1 ドライバ資産(`SceneVfxPreviewDriver`・`SceneCameraShakePreviewDriver`・`EditorHapticsPreviewDriver`)をそのまま束ね、自前で `AudioManager`(`EditorAudioFactory`)・`UiManager`・`ModelsManager`・`AnchorGroupPlayer`・`AssetEventDispatcher`(Event マーカー用に専用の `EventBus` を 1 つ持つ)を組み立てる。全員が `EditorAnchorRegistry` を共有する。**Presentation クリップは Edit Mode 未対応のまま(スコープを削った簡略化)**: `PresentationManager` は Audio/Bgm/Vfx/Anim/Ui/UiTween/CameraFx/Haptics/NetBridge を束ねる大掛かりな配線が要り、本対応のスコープでは見送った(`ManagerRefs` を持たないため既存の静的ファサードにフォールバックし、Edit Mode では未 Bind のため no-op のまま安全に継続する。Play Mode で確認する運用に変わりはない)。
> **(4) マーカーの跨ぎ判定は `CutsceneMarkerCursor<T>`(Runtime、新規切り出し)を使う**: `CutsceneManager.CollectMarkers`/`AdvanceMarkers`(6-10b)と同じ「時刻順に集め、跨いだら発火、Seek は無音でスキップ」パターンをジェネリックなユーティリティとして独立させ、`CutsceneEditModePreviewProvider` の監視役から使う。**`CutsceneManager` 自身の内部実装(既存のフィールド構成)はあえて触っていない** — 見た目だけの共通化のために、実機で何度も検証済みの Play Mode の定常経路(6-10a〜d の全テストが通る状態)を書き換えるリスクを避けた。巻き戻し(スクラブで `director.time` が後退)を検出したら `ResetCursor()` + 無音 `Advance` でカーソルを追いつかせ、次に前進したときに正しく再発火できるようにしている。Event マーカーは `CutsceneEditModeManagers` の `AssetEventDispatcher` へ `EventBus.RaiseAdHoc` で直接渡す、Signal マーカーは要求どおりログのみ(`Debug.Log`)、Shake/Haptic マーカーは ID を Registry で解決してから `SceneCameraShakePreviewDriver.Play`/`EditorHapticsPreviewDriver.Play`(ID 直呼びの `CameraFxManager.Shake`/`HapticsManager.Play` を素通しすると、これらのドライバの `EnsureTicking()`〔カメラの元姿勢の記録・`EditorApplication.update` 購読〕を経由せず揺れ/振動が実際には効かないため、必ずドライバの `Play` を経由する)。
> **(5) カメラは Play Mode の `DDriveCutsceneCameraApplier` と完全に別経路**: `CutsceneEditModeCameraWriter`(Editor、static)が `Application.isPlaying` で自分自身を止めたうえで、`CutsceneCameraStateHolder`(Camera クリップのミキサーが書く置き場、Play Mode と共用)を読み、原点(プレビュー用 Director の Transform)を掛けて `Camera.main` へ**ブレンド無しで**直接書く(§4.4「スクラブ中はブレンド無し」を Edit Mode 全体の既定にした)。`HasData=false` になったら書き込み前の姿勢(位置・回転・画角・ピント距離)へ戻す。Play Mode 用の `DDriveCutsceneCameraApplier`(実行順 1000 の contract)には一切触れておらず、二重書き込みは `Application.isPlaying` の相互排他だけで防いでいる。
> **(6) 導線**: `CutsceneDataEditor` の「▶ Timeline ウィンドウで開く」が `CutsceneEditModeDirectorSetup.OpenTimelineWindow` を呼ぶ。確認用シーン(`CutscenePreviewSceneSetup.TryOpenOrCreate()`)を開き、`"Cutscene Timeline Preview (Edit Mode)"` という専用の GameObject を(無ければ作って)使い回し、`CutsceneManager.ApplyOrigin`/`ApplyBindings`(Play Mode)の簡略版で Origin・Bindings を解決する(`Target` バインドは Edit Mode に「相手」の概念が無いため `Self` と同じ確認用アクターで代用する近似)。`CutsceneEditModePreviewProvider.PrepareContext` で `CutsceneDirectorContext`+`ManagerRefs` を付けたあと、その GameObject を選択して `Window/Sequencing/Timeline` を開く。既存の Play Mode ボタン(● 再生 / Cancel / Skip、`CutscenePreviewHarness` 経由)はそのまま残した。
> **簡略化・既知の制約(TODO として残す)**: (a) Presentation クリップは Edit Mode 非対応(上記(3))。(b) `CutsceneManager` のコンストラクタ・`DDriveRuntimeBootstrap` は変更していない(上記(1)の理由)。(c) カメラの「元の姿勢」の記録は Editor プロセス全体で 1 つ(`CutsceneEditModeCameraWriter` は static)— 確認用シーンは通常 `Camera.main` が 1 台のみのため実害は無いが、複数カメラを行き来する特殊なシーン構成では想定外の復元になりうる。(d) `PresentationDataValidator`/`CutsceneDataValidator` に「Edit Mode で対応していないトラック」を警告する Validator は追加していない(気づきにくさは HelpBox の文言で補っている、`CutsceneDataEditor`)。

---

## 1. Unity Timeline の基礎(前提知識)

D-Drive の設計を読むのに必要な範囲だけまとめる。

### 1.1 登場人物

| 名前 | 何か | D-Drive で例えると |
|---|---|---|
| **TimelineAsset**(`.playable` ファイル) | トラックとクリップの並び = 「楽譜」。プロジェクトのアセット | Data |
| **PlayableDirector**(シーンのコンポーネント) | TimelineAsset を再生する「演奏者」。再生・停止・シーク・速度・更新方法を持つ | Manager の Instance |
| **Track**(トラック) | 1 行。何を動かすかの種類(Animation / Audio / Activation / Control / Signal …) | PresentationTrack の Kind |
| **Clip**(クリップ) | トラック上の「開始〜終了」の区間。例: 攻撃モーション 0〜1.2 秒 | 区間を持つ演出 |
| **Marker**(マーカー) | トラック上の「一点」。通過時に通知を出す | AssetEvent(Trigger=Time) |
| **Binding**(バインド) | トラックが「シーンのどのオブジェクトを動かすか」の結び付け | PlayContext.Self / Target |

### 1.2 一番大事な性質: 「アセット」と「バインド」は別物

- TimelineAsset には「Animation トラック 1 に、このクリップを 0 秒から」までしか入っていない。**どの Animator を動かすかは PlayableDirector 側(=シーン側)に保存される**
- なので 1 つの TimelineAsset を、プレイヤー A でも B でも使い回せる(バインドを差し替えるだけ)
- 逆に言うと、TimelineAsset を単体で渡されても「何を動かすか」が決まらない。**D-Drive はここを「役割名」で解決する**(§4.2)。プログラマーは `Cutscene.Play(CUTID.Opening, ctx)` だけ書き、誰を動かすかは ctx と役割名から自動で決まる

### 1.3 標準トラック

| トラック | 用途 | 備考 |
|---|---|---|
| Animation | Animator に AnimationClip を流す。Transform だけのオブジェクト(カメラ等)も可 | クリップ同士のブレンド(重ねるとクロスフェード)、Avatar Mask、Override トラック(上半身だけ差し替え)ができる |
| Audio | AudioSource で AudioClip を鳴らす | D-Drive の AudioManager(バス・ダッキング・同時発音制限)を**通らない**。D-Drive では原則使わず D-Drive SE トラックを使う |
| Activation | 区間中だけ GameObject を表示 | — |
| Control | 区間中に Prefab / ParticleSystem / 子 Timeline を出す | D-Drive の Pool・VfxManager を**通らない**。D-Drive VFX トラックで代替 |
| Signal | マーカー通過で SignalReceiver の UnityEvent を呼ぶ | シーンに Receiver を置く必要があり、ID で追えない。D-Drive Event トラックで代替 |

### 1.4 時間と再生

- `DirectorUpdateMode`: GameTime(Time.timeScale に従う)/ UnscaledGameTime / Manual(自分で `Evaluate` を呼ぶ)。**D-Drive は Manual にして GameLoopDriver から進める**(ポーズ・ヒットストップ・スロー・ネット同期を他 Manager と揃えるため)
- `WrapMode`: Hold(最後で止まる)/ Loop / None(終わったら解除)
- Timeline のフレームレート(既定 60)は表示・スナップ用。**Maya のシーン fps と合わせないとキーがフレームの間に来て見た目がずれる**(§5.3)
- 編集中(Edit Mode)は Timeline ウィンドウでスクラブすると PlayableDirector が `Evaluate` される。**独自トラックはこの「巻き戻し・飛ばし」に耐える作りが必要**(SE を鳴らしっぱなしにしない、VFX を時刻に合わせて Simulate する等。§4.4)

### 1.5 独自トラックの作り方(実装者向け)

- クリップ: `PlayableAsset`(データ)+ `PlayableBehaviour`(再生中の処理: `OnBehaviourPlay` / `ProcessFrame` / `OnBehaviourPause`)
- トラック: `TrackAsset` に `[TrackClipType]` / `[TrackBindingType]` を付ける
- マーカー: `Marker` + `INotification` を実装し、`INotificationReceiver` で受ける
- これらは Runtime asmdef に置き、`Unity.Timeline` を参照する(**asmdef 変更。2026-09-18 ユーザー承認済み**、§6 / §7.1-10)

---

## 2. 目標(ユーザー要望の分解)

1. **Maya → Unity**: Maya は普通に FBX を書き出すだけ(スクリプト無し)。Unity 側で FBX を検知し、カメラ・キャラ・小物のアニメを Timeline のトラックとして自動で並べる
2. **Unity で演出を足す**: 並んだ Timeline に、D-Drive の SE / VFX / 配置セット / 画面揺れ / 触覚 / UI などをクリップやマーカーで足す。**中身は ID 参照で、実再生は各 Manager**(ADR-4 と同じ)
3. **イベント**: ほかのアセットと同じ `AssetEvent`(PlayAsset / SetParam / SendMessage / Duck、Repeat)をそのまま置ける。コード側へは Signal / Marker で通知できる
4. **プログラマーは ID だけ**: `Cutscene.Play(CUTID.Opening, ctx)`、`await` で終了待ち、スキップ、`OnMarker` 購読
5. **(2026-09-18 追加)カメラはゲームカメラと繋がる**: Maya カメラへの切り替わり・戻りが滑らか(§4.6.2)。カメラの「滑らかさ(ステップ fps)」と「ピント・絞り・画角」は **Unity 側で、ショットごとに、Maya をやり直さずに**調整できる(§4.6.3 / §4.6.4)
6. **(2026-09-18 追加)用途は数秒の短い演出**(必殺技のカットイン、ラウンド開始/勝利デモ等)。長いカットシーン(数十秒〜分)は作らない。PresentationData と用途が重なるので使い分けを決める(§3.1)

---

## 3. 全体像

```
Maya ──FBX 書き出し(標準機能のみ)──▶ Assets/SourceAssets/Cutscene/<カテゴリ>/<ショット>.fbx           … カメラ + 小物
                                     Assets/SourceAssets/Cutscene/<カテゴリ>/<ショット>__<Model識別子>.fbx … キャラの骨アニメ(キャラごと 1 本、§5.1)
                                               │ AssetPostprocessor(6-10c)
                                               ▼
                         CutsceneData(新種別)  ── TimelineAsset(自動生成・再生成可能)
                           ├ 役割バインド表(Camera / Self / Target / 名前付き)+ 原点(§4.2.1)
                           ├ FrameRate / SourceFrameRange(§5.3 / §5.1)
                           ├ Events(AssetEvent[] 共通)
                           └ Flags(Net = Local / Cosmetic で同期方式を切替、§4.7)
                                               │
Unity(デザイナー)── Timeline ウィンドウで D-Drive トラックを追加(6-10b)
                           ├ D-Drive Camera クリップ(自動生成。ステップ fps / ブレンド / ピント設定はここで調整、§4.6)
                           ├ D-Drive Event トラック(AssetEvent マーカー)
                           ├ D-Drive SE / VFX / AnchorGroup / Shake / Haptic / Presentation クリップ
                           └ Signal マーカー(コード通知)
                                               │
ゲーム ── Cutscene.Play(CUTID.X, ctx) → CutsceneManager
                           ├ PlayableDirector をプールから借りる(Manual 更新)
                           ├ 役割名 → ctx / シーンのオブジェクトへバインド
                           ├ カメラ: ゲームカメラの姿勢と Timeline カメラをブレンドして書き込む(§4.6)
                           └ 各クリップ・マーカーは既存 Manager / AssetEventDispatcher へ委譲
```

- **編集 UI は Unity 標準の Timeline ウィンドウを使う**(独自のタイムライン UI は作らない。業界標準で学習資料も多く、クリップのドラッグ・ブレンド・スナップ・カーブ編集が揃っている)。D-Drive 側は「D-Drive トラックの Inspector」「確認用シーン」「バインド検査」「Validation」を足す
- PresentationData(5-1)の `TrackKind.Timeline` は CutsceneData を 1 本のトラックとして再生する(短い技演出の中に Timeline を入れる用途。§3.1)

### 3.1 CutsceneData と PresentationData の使い分け(2026-09-18 追記)

用途が「短い演出のみ」に決まったため、両者は正面から重なる(どちらも数秒の SE/VFX/揺れ/HitStop の束)。**「併存させ、判断基準を 1 つに絞る」(案 A)で確定**(2026-09-18 ユーザー決定、§7.1-9)。以下は判断基準と、併存を選んだ根拠(代案 B / C は不採用)。

**判断基準(デザイナー向け。上から順に当てはまったところで決まる)**

| 質問 | Yes なら |
|---|---|
| Maya で作ったカメラやキャラの動き(FBX)を使う? | **Cutscene** |
| 演出中、カメラをゲームから奪って別のカメラワークにする? | **Cutscene** |
| ヒット判定など「ゲームの結果を待ってから」続きを出す(`OnSignal`)? | **Presentation** |
| 既存の SE / VFX / 揺れ / HitStop を時間で並べるだけ(キャラの動きは Animator のモーション)? | **Presentation** |
| Maya カメラも使うし、ヒットも待ちたい | **Presentation を親**にして、Timeline トラック(`TrackKind.Timeline`)で Cutscene を呼ぶ |

一言で言うと **「Maya の FBX を使うなら Cutscene、使わないなら Presentation」**。それ以外の機能(SE/VFX/Shake/Haptic/UI/Event/Signal)はどちらにも同じ形で置けるので、迷う要素を FBX の有無だけにする。

**両者の性格の違い(併存させる理由)**

| 観点 | PresentationData(5-1、実装済み) | CutsceneData(6-10) |
|---|---|---|
| 時間の主役 | ゲームロジック(`OnSignal` で待つトラックがある。`AtTime` は補助) | Timeline の時間軸(全部 `AtTime` 相当)。**コードの合図を「待つ」トラックは持たない** |
| 編集 UI | PresentationEditor(D-Drive 独自、5-4) | Unity 標準 Timeline ウィンドウ + D-Drive の Inspector |
| Data の作り手 | デザイナーが手で作る | **ツール(FBX 取り込み)が作り、デザイナーが足す**(再取り込みで自動生成部分が更新される) |
| カメラ | 触らない(Shake は CameraFx の後段ノードのみ) | 奪う(ブレンドして戻す、§4.6) |
| fps の概念 | 無い(表示上 60fps 換算のみ) | ある(`FrameRate`、Maya の fps が真実、§5.3) |
| ネット | Cosmetic + `PredictLocal`(5-8) | 同じ流儀(§4.7) |
| 入れ子 | Timeline トラックで Cutscene を呼べる | Presentation クリップで Presentation を呼べる |

入れ子は両方向を許す(現行設計どおり)。**循環(Presentation → Cutscene → 同じ Presentation)は Validation Error**(既存の「Presentation が自身を含む」Error の拡張)。

**代案とトレードオフ**

| 案 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| **A. 併存(採用、2026-09-18)** | 上の判断基準で使い分け。相互に入れ子可 | 5-1〜5-16 で実装・ネット対応・エディタ済みの Presentation を変えない。FBX 取り込みが作る Data と人が作る Data が種別で分かれ、再取り込みが人の編集を壊さない(§5.2-3)。fps・カメラ・バインド表など Timeline 固有の欄が Presentation に増えない | 種別が 1 つ増える(`CUTID` / カタログ / Validator / エディタ導線)。「どっちで作るか」を一度は考える必要がある(→ 判断基準で吸収) |
| B. Presentation に統合 | `PresentationData` に `Timeline`・`Bindings`・`FrameRate`・カメラ設定を持たせ、CutsceneData を作らない | 種別・ID 系統・エディタ導線が 1 つ | Timeline を使わない大多数の演出に無意味な欄が並ぶ。FBX 取り込みが PresentationData を自動生成することになり「ツールが管理する Data」と「人が作る Data」が同じ種別に混在(`ImportSourceGuid` の有無で挙動を分ける実装が要る)。編集 UI が PresentationEditor と Timeline ウィンドウの 2 つに割れる(同じ Data の同じ時間軸を 2 つの UI が触る)。ネット経路が「AtTime トラック方式」と「Director シーク方式」の 2 系統になる |
| C. Cutscene に統合(Presentation 廃止) | 全部 Timeline で作る | 編集 UI が Unity 標準 1 つ | 実装済みの Presentation(5-1〜5-16、ネット・Late Join・エディタ・デモ・テスト)を捨てる。`OnSignal`(ゲーム結果待ち)を Timeline で素直に表せない(マーカーで止めて待つ拡張が要る)。不採用 |

---

## 4. D-Drive 側の設計

### 4.1 新種別 CutsceneData(AssetType.Cutscene、接頭辞 `CUT`)

```csharp
public sealed class CutsceneData : AssetDataBase
{
    public TimelineAsset Timeline;           // 自動生成 or 手置き
    public CutsceneBinding[] Bindings;       // 役割名 → 解決方法
    public CutsceneOrigin Origin;            // Self / World / AnchorPoint(§4.2.1、2026-09-18 追加)
    public float FrameRate = 30f;            // Maya のシーン fps(取り込みで自動設定。§5.3、2026-09-18 追加)
    public FrameRange SourceFrameRange;      // 取り込むフレーム範囲。Start=End=0 で FBX 全体(既定)。§5.1「逃げ道」(2026-09-18 追加)
    public CutsceneSkip Skip;                // 不可 / 即終了 / 指定マーカーまで飛ばす
    public DirectorWrapMode Wrap;            // 既定 None
    public bool LockInput;                   // 再生中はプレイヤー入力を止める(UiManager/入力側へ通知)
    public bool PredictLocal;                // Flags.Net=Cosmetic のとき、行為者は Broadcast を待たず即再生(PresentationData と同じ意味。§4.7、2026-09-18 追加)
    // Events(AssetEvent[])は AssetDataBase 共通。Trigger=Time で Timeline 上の秒として扱う
    // Flags.Net(AssetDataBase 共通): Local = ローカル再生 / Cosmetic = 同期再生(§4.7)
}

[Serializable]
public struct CutsceneBinding
{
    public string TrackName;                 // "Camera" / "Hero" / "Enemy01" …(FBX のノード名・ファイル名から自動)
    public CutsceneBindTarget Target;        // MainCamera / Self / Target / SpawnModel / SceneObjectByName / AnchorPoint
    public AssetId<ModelMarker> Model;       // SpawnModel のとき: ModelsManager で出して終了時に返す
    public string SceneObjectName;
}

[Serializable]
public struct FrameRange { public int Start; public int End; }   // End は含む。Start=End=0 で「全体」
```

- `AssetType` enum に `Cutscene` を**末尾追加**(既存値は不変。[13] B-9 の枠を使う)
- 定数クラスは `CUTID`。`KnownPrefixes` にも `CUT` を追加する([42] §5.13 に記載済み。P 発効前なので今なら追加できる)
- カメラごとの調整値(ステップ fps・ブレンド・ピント)は CutsceneData ではなく **Timeline 上の D-Drive Camera クリップ**が持つ(§4.6)。ショットごとに変えたいものはクリップ側、カットシーン全体で 1 つのものは Data 側、という分け方
- プロジェクト共通の既定値(既定 fps、カメラのステップ fps・ブレンド秒の初期値)は **`CutsceneImportProfile`**([05] の `Anim2DImportProfile` と同じ `FindOrDefault()` パターンの設定 SO)に持つ(§5.3)

### 4.2 バインド解決(「誰を動かすか」)

| Target | 解決先 | 典型 |
|---|---|---|
| MainCamera | ゲームのメインカメラ。再生中は Transform / 画角 / ピントを **ブレンド付きで** 書き込み、終了時にゲームカメラへ戻す(§4.6) | Maya カメラ |
| Self / Target | `PlayContext.Self` / `Target` の Animator | 主人公・敵 |
| SpawnModel | ModelData を ModelsManager で生成して結び、終了時に返却 | カットシーン専用の登場人物・小物 |
| SceneObjectByName | シーン内の名前一致 | 背景ギミック |
| AnchorPoint | AnchorPoint([21])の位置に置く | 「右手に剣を持たせる」等 |

未解決は **警告 + そのトラックだけミュートで続行**(TL;DR #4)。Validation で事前に検出する。

#### 4.2.1 原点(Maya のワールド座標をゲームのどこに置くか)(2026-09-18 追記)

Maya のカメラ・キャラ・小物は Maya シーンのワールド座標で焼かれているが、ゲームでは「プレイヤーが今いる場所」で再生したい。`CutsceneData.Origin` で決める。

| Origin | 意味 | 典型 |
|---|---|---|
| Self(既定) | `ctx.Self` の位置・向きを Maya の原点に合わせる(Y 回転のみ。傾きは無視) | 必殺技カットイン(使用者の位置で) |
| World | Maya の座標をそのまま使う | ステージ固定の開始デモ |
| AnchorPoint | 名前付き AnchorPoint([21])の位置・向き | ステージごとの定位置 |

- 実装: プールした `CutsceneRoot`(空 GameObject)を原点に置き、MainCamera 以外の Maya 由来トラックの対象(SpawnModel した Model・小物)をその子にする。カメラは §4.6 の書き込み時に原点の姿勢を掛ける(親子付けはしない = CameraFx の Shake ノードと干渉させない)。Self / Target にバインドしたキャラは既にゲーム内の位置にいるので、Timeline の Animation トラックのオフセット(`ApplyTransformOffsets`)を原点の姿勢に設定して「Maya での相対位置」を保つ。**Humanoid のルートモーションの扱い(Bake Into Pose の可否)と Timeline トラックオフセットの組み合わせは 6-10a 着手時に実機で確認する**(要検証、§7.3)

### 4.3 D-Drive トラック(6-10b)

| トラック / マーカー | 中身 | 委譲先 |
|---|---|---|
| **Camera** クリップ(2026-09-18 追加) | Maya カメラの位置・回転・画角・ピント・絞りのカーブ + ステップ fps + ブレンド秒(§4.6)。取り込みが自動生成し、デザイナーは設定だけ触る | CutsceneManager(Camera + Volume へ書き込み) |
| **D-Drive Event** マーカー | `AssetEvent` 1 件(PlayAsset / SetParam / SendMessage / Duck、Repeat、Anchor) | `AssetEventDispatcher`(Anim / VFX と同じ経路) |
| **SE** クリップ | SeId。区間の長さ = ループ SE の鳴らし続け時間(OneShot は開始点のみ使用) | AudioManager |
| **VFX** クリップ | VfxId + AnchorDef。区間終了で Stop(ループ VFX 向け) | VfxManager |
| **AnchorGroup** クリップ | 配置セット | AnchorGroupPlayer |
| **Shake / Haptic** マーカー | ShakeId / HapticId(5-2 / 5-2b 完了後) | CameraFx / HapticsManager |
| **UI** クリップ | CanvasId(字幕・レターボックス等)を区間中だけ Open | UiManager |
| **Presentation** クリップ | PresId。区間開始で `Presentation.Play`、区間終了(または Cutscene の中断)で Cancel。`OnSignal` トラックを持つ Presentation は Signal を送る手段が無いので Validation Warning(§3.1) | PresentationManager |
| **Signal** マーカー | 文字列キー(`cutscene/xxx` 規約) | `CutsceneHandle.OnMarker` → コード |

- 1 本の「D-Drive トラック」に SE / VFX / Event を全部載せる案もあるが、**種類ごとに行を分ける方が Timeline ウィンドウ上で読みやすい**ので分ける
- クリップの Inspector は既存の IdRef ドロップダウン(AssetIdDrawer)を使う = 名前で選べる、Missing は赤表示
- **キャラ・小物のアニメは標準の Animation トラック**を使う(Humanoid のリターゲット・ブレンド・Avatar Mask をそのまま使うため)。**カメラだけ D-Drive Camera クリップ**にする理由: 標準 Animation トラックは Animator 経由でしか値を書けず、URP の被写界深度(Volume の `DepthOfField`)や「評価時のステップ fps 量子化」を扱えないため(§4.6)

### 4.4 編集中プレビュー(スクラブ対応)

- Timeline ウィンドウでの再生・スクラブ中も **実 Manager を駆動する**(ADR-4)。確認用シーン `CutscenePreviewScene` に Editor 用 Manager 群を置き、`PreviewService` と同じ仕組みで動かす
- 巻き戻し・飛ばし: SE は「区間に入った瞬間」だけ鳴らし、スクラブで同じ点を何度も通っても連打しない(ドラッグ中は無音、再生ボタン中のみ発音)。VFX は `SceneVfxPreviewDriver` の手動 Simulate で「クリップ開始からの経過秒」に合わせる
- Maya カメラのトラックは Game ビューで確認できるように、プレビュー中はメインカメラにバインドする。**ブレンドの確認**: 再生開始時のカメラ姿勢を「ゲームカメラの姿勢」とみなしてブレンドインし、終了時にそこへ戻る(確認用シーンのカメラを手で動かしておけば、その位置からの繋ぎを確認できる)。スクラブ中はブレンド無し(Timeline カメラの姿勢そのもの)にする — スクラブで「繋ぎ」は確認できない、と割り切る
- ステップ fps(§4.6.3)はスクラブ時にも効く(量子化は評価関数の中で行うため、Timeline ウィンドウの再生でもそのまま見える)

### 4.5 ランタイム(CutsceneManager)

- `Cutscene.Play(id, ctx)` → `CutsceneHandle`(Presentation と同じ API 形: Cancel / Skip / Pause / Resume / SetSpeed / Seek / IsPlaying / NormalizedTime / OnCompleted / OnCancelled / OnMarker / WaitAsync)。**`Signal`(コード → データ)は持たない**(§3.1: Cutscene は「待つ」トラックを持たない。ヒット待ちが要るなら Presentation を親にする。2026-09-18)
- PlayableDirector は Pool から借用。`timeUpdateMode = Manual`、GameLoopDriver の Tick で `time += dt; Evaluate()`
- ポーズ: `Flags.Pause` に従う。HitStop / スローは TimeService の dt に乗る
- 終了・キャンセル時: バインドを外し、SpawnModel を返却、カメラをブレンドアウトしてゲームカメラへ返す(§4.6.2)、KeepWhilePlaying の SE / VFX を止める(EventBus.End)
- ネット: §4.7(2026-09-18 に節として独立)

#### 4.5.1 入力ロック(`LockInput`)の分担(2026-09-18 決定、§7.2-4)

**決定: D-Drive は「ロック中かどうか」を公開・通知するだけで、実際に入力を止めるのはゲーム側の責務**。理由: 入力系(Input System の Action Map 切替、Host 権威での入力破棄、UI のフォーカス制御)はゲームごとに違い、D-Drive が共通 API を作ると MS2026 の入力設計を縛る。Presentation にも入力ロックの概念は無く、Cutscene だけが例外を持つ理由も無い。D-Drive 側が持つのは次の 3 つだけ:

| 公開面 | 形 | 意味 |
|---|---|---|
| `CutsceneHandle.IsInputLocked` | `bool`(読み取り専用) | `Data.LockInput && IsPlaying`。BlendOut 中(Skip / Cancel 後の戻し区間)も `IsPlaying` なので **true のまま**(戻し中に入力で動かれるとブレンドが破綻するため)。Handle が無効なら false |
| `Cutscene.IsInputLocked` / `Cutscene.OnInputLockChanged` | 静的ファサード。`bool` + R3 `Observable<bool>` | 「`LockInput` な Cutscene が **1 つでも**再生中か」。複数同時再生(入れ子・重なり)を数え、0→1 / 1→0 の**変化時だけ**発火する(`PauseService.OnPauseChanged` と同じエッジ通知。深さ 2 以上の増減では発火しない)。ゲーム側は通常こちらを購読する(Handle を個別に追わなくてよい) |
| EventBus(Data 側の通知) | `EventBus.Fire(ctx, EventTrigger.Custom, "cutscene/input_lock")` / `"cutscene/input_unlock"` | ロックの開始・終了を **AssetEvent の Custom トリガ**として流す。デザイナーが CutsceneData の Events に「`input_lock` で字幕 Canvas を開く / レターボックス SE を鳴らす」等を Data だけで足せる(コードを書かずに「入力を受け付けていない」見せ方を作れる) |

- ゲーム側の実装例(MS2026): `Cutscene.OnInputLockChanged.Subscribe(locked => inputActions.Player.Set(!locked))`。Host 権威のゲームロジックが「ロック中のクライアントからの入力を捨てる」側も MS2026 側の実装(D-Drive は関知しない。[14] §3 の Host 権威と同じ分担)
- Cosmetic(§4.7)では各クライアントが**自分の**ロック状態を持つ(受信側でも `LockInput` なら true になる)。Host が「相手の入力を受け付けない」ようにするかはゲームルール
- `Flags.Pause` でポーズ中もロック状態は変わらない(ポーズ解除で続きから)。`Cancel` / 終了で必ず解除される(Handle の破棄経路は 1 本。解除漏れを作らない)
- 持ち込み先のプログラマー向けの記述は [ProgrammerManual/rules.html](ProgrammerManual/rules.html) の「Cutscene と共存するためのゲーム側の責務」節(実行順の契約 §4.6.5 と同じ節にまとめる。両方を同時に更新する)

### 4.6 カメラ(シームレス化・ステップ fps・設定の持ち越し)(2026-09-18 新規)

#### 4.6.1 方式: Cinemachine は使わず、D-Drive 独自のブレンドで足りる

**確認した事実**: `Packages/manifest.json` に `com.unity.cinemachine` は**入っていない**(`com.unity.timeline` 1.8.12 はある)。Cinemachine を使うなら新規依存になる。

| 方式 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| **A. D-Drive 独自ブレンド(採用)** | CutsceneManager が「ゲームカメラが今フレーム計算した姿勢」と「Timeline カメラの姿勢」を重み `w(t)` で補間して Camera に書く(§4.6.2)。新規依存なし | 要件(位置・回転・画角・ピントの数百 ms の繋ぎ)に対して過不足がない。Maya カメラは焼き済みなので Cinemachine の強み(追従・ノイズ・手続き的カメラ)は使わない。[42] §3.5 の依存表・ウィザードが増えない。ゲーム側のカメラ制御方式(自前 / Cinemachine)を問わず動く | ブレンドの補間は D-Drive が持つ(Cinemachine の Blend 曲線・BlendList は使えない)。ゲームカメラ制御は「`LateUpdate` 以前・実行順 1000 未満で姿勢を書く」契約(§4.6.5)を守る必要がある |
| B. Cinemachine 導入 | Timeline の `CinemachineShot` クリップ + `CinemachineBrain` のブレンドに任せる | ブレンド曲線・優先度・Impulse(揺れ)が標準で揃う。Cinemachine を使うゲームなら自然 | **新規依存**(`com.unity.cinemachine`)。[42] §5.10「依存の追加 = MINOR + ウィザード検査」、§3.5 依存表と README の更新、MS2026 が Cinemachine を使わないなら持ち込み先に不要な依存を強いる。CameraFx(5-2)の Shake ノード方式とブレンドの主体が二重になる。「短い演出のみ」の用途に対して重い |

結論: **v1 は A**。将来ゲーム側が Cinemachine を採用したときは、[42] §7 A-7 の NGO と同じ流儀で `versionDefines`(`DDRIVE_CINEMACHINE`)を切り、「MainCamera 役割を `CinemachineCamera` にバインドする」任意アダプタを後付けできるようにしておく(v1 では作らない。§7.2-1)。

**2026-09-20 追記(P-1、移植先の事実確認)**: 上記「確認した事実」は**このリポジトリ(D-Drive 開発リポジトリ)の** `Packages/manifest.json` の話であり、今も変わらず `com.unity.cinemachine` は入っていない。一方、最初の移植先 **MS2026 には `com.unity.cinemachine` 3.1.7 が導入済み**であることを 2026-09-19 に確認した([42_distribution.md] の「MS2026 実態との差分」節)。これにより表 B の欠点「MS2026 が Cinemachine を使わないなら持ち込み先に不要な依存を強いる」は MS2026 に関しては当てはまらなくなった(既に入っているため)。ただし v1 の結論(A: D-Drive 独自ブレンド、Cinemachine アダプタは作らない)はこの事実だけでは覆らない: MS2026 が実際にゲームカメラを Cinemachine で組むかどうか、`CinemachineBrain` の運用実態は未確認であり、Maya カメラが焼き済みで Cinemachine の強み(追従・ノイズ)を使わない理由(表 A の利点)も変わっていない。`DDRIVE_CINEMACHINE` アダプタをどの P チケットで扱うか(新規チケットにするか P-4/P-12 に含めるか)は [42_distribution.md] §7 B-11 に要判断として追加した。

#### 4.6.2 ブレンド(ゲームカメラ ⇄ Timeline カメラ)

D-Drive Camera クリップ(§4.3)が持つ設定:

```csharp
public sealed class CutsceneCameraClip : PlayableAsset   // TimelineAsset のサブアセット。取り込みが自動生成
{
    // 焼かれたカーブ(取り込み時に FBX の AnimationClip から抽出。再取り込みで上書き)
    public AnimationCurve PosX, PosY, PosZ;
    public AnimationCurve RotX, RotY, RotZ, RotW;    // 四元数。評価後に正規化
    public AnimationCurve FieldOfView;               // 垂直画角(度)。Maya の焦点距離 + フィルムゲートから算出
    public AnimationCurve FocalLengthMm;             // 焦点距離(mm)。DoF の Bokeh 計算にも使う
    public AnimationCurve FocusDistance;             // ピント距離(m)。取れなければ空(§5.2 要検証)
    public AnimationCurve Aperture;                  // 絞り(f 値)。同上
    // デザイナーが触る設定(再取り込みで保持)
    public float StepFps;                            // 0 = 量子化なし(元 fps のまま)。§4.6.3
    public ValueDef BlendIn;                         // 0→1 の重み曲線 + 秒(TimeDef)。既定 0.25s / EaseInOutSine
    public ValueDef BlendOut;                        // 同上
    public CameraFocusMode Focus;                    // Off / Volume(既定。DoF を書く) / CameraOnly(Camera.focusDistance だけ。HDRP 向け)
}
```

ブレンドの規則:

- **重み `w`**: クリップ先頭から `BlendIn` の間 0→1、クリップ末尾(または Skip / Cancel 時点)から `BlendOut` の間 1→0。クリップの途中は 1。1 つの Timeline にカメラクリップが複数(ショット切替)あるとき、**クリップ間は Timeline の標準ブレンド(重なり)で Timeline カメラ同士を補間**し、ゲームカメラとのブレンドは最初のクリップの頭と最後のクリップの尻だけに掛かる
- **ゲームカメラの姿勢 `G`**: 「このフレームにゲーム側のカメラ制御が書いた姿勢」。書き込み直前に読む(§4.6.5 の実行順で保証)。画角・ピント等の「制御スクリプトが毎フレーム書くとは限らない値」は **再生開始時に控えた値を `G` とし、終了時に書き戻す**(毎フレーム読むと自分が前フレームに書いた値を読んでしまい、元に戻らなくなるため)
- **Timeline カメラの姿勢 `T`**: クリップのカーブを(ステップ fps で量子化した時刻で)評価し、原点(§4.2.1)の姿勢を掛けたもの
- **書き込み**: 位置 `lerp(G,T,w)`、回転 `slerp(G,T,w)`、画角 `lerp`、ピント距離・絞り `lerp`、DoF Volume の `weight = w`(§4.6.4)。`w = 0` のフレームは何も書かない(ゲームカメラに一切触らない = Cutscene が無いときと完全に同じ)
- Skip / Cancel: その時点の `T` を固定して `BlendOut` で戻す(急に切れない)。`BlendOut = 0` なら即座に戻る
- CameraFx(5-2)の Shake ノードは Camera の**親**にあり、本節の書き込みは Camera **本体**の Transform に行うため両立する(カットシーン中も Shake マーカーが効く)。`Camera.main` の付け替えは行わないので、CameraFx のノード再アタッチ処理も走らない

代案(不採用): **2 カメラ方式**(カットシーン専用 Camera をプールし、ゲームカメラを無効化して切り替える)。ゲームカメラ制御との実行順を気にしなくてよい利点があるが、URP の追加設定(`UniversalAdditionalCameraData` のレンダラー・ポストプロセス・Volume マスク・スタック)を毎回複製する必要があり、`Camera.main` が切り替わることで CameraFx のノードが付け替わる・AudioListener が別カメラに残る、と副作用が多い。1 カメラ上書き方式の方が「Cutscene が無いときと同じ」を保ちやすい。

#### 4.6.3 ステップ fps(「ぬるぬる過ぎる」対策。Unity 側・ショットごと・非破壊)

ユーザー要望: カメラだけコマ落ちさせたい(24fps 映画風・12fps ストップモーション風)。**Maya で焼く時ではなく Unity の再生時に調整できること**が必須(書き出し時に間引くと、気に入らなかったとき Maya からやり直しになる)。

| 方式 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| **A. 評価時に量子化(採用)** | クリップの評価時刻を `tq = floor((t − clipStart) × StepFps) / StepFps + clipStart` に丸めてからカーブを評価する。`StepFps = 0` で無効 | 非破壊(カーブは元の fps のまま)。ショットごと(= クリップごと)に違う値にできる。Inspector の数値 1 つで即反映、スクラブでもプレビューでも同じ見え方。将来「オプション画面でカメラの滑らかさ」のような実行時変更もできる | 元 fps の約数でない値(30fps 元に 24)は間隔が不均一になる(Validation で Warning、§5.3)。量子化はカメラの全チャンネル(位置・回転・画角・ピント)に同時に掛ける(片方だけ滑らかだと気持ち悪い) |
| B. 取り込み時にキーを間引く(Constant 接線) | FBX 取り込みでカーブを再サンプル | Timeline ウィンドウのカーブ表示が「見た目どおり」になる | 値を変えるたび再取り込み。1 FBX に 1 値しか持てない(ショットごとに変えるには FBX を分ける)。ステップ有無を見比べるのに手間が掛かる |

採用 A の補足:

- **キャラ・小物・SE/VFX には掛けない**(量子化するのはカメラクリップの評価だけ)。「カメラだけコマ落ち、キャラは滑らか」がユーザーの意図
- スロー再生(TimeService)中は Timeline 時刻の進みが遅くなるので、同じ `StepFps` でも保持フレーム数が増える(= スローでもコマ落ち感が保たれる)。これは意図どおりとする
- ブレンド(§4.6.2)は量子化した `T` に対して掛ける。ブレンド中だけ滑らか、という見え方になるが、`BlendIn/Out` は数百 ms なので許容する
- 既定値は `CutsceneImportProfile.DefaultCameraStepFps`(初期値 0 = 無し)。取り込みで新規生成したクリップにだけ適用し、既存クリップの値は保持する

#### 4.6.4 カメラ設定の持ち越し(画角・ピント・絞り)と URP の Volume

ユーザー要望: Maya で決めたピント等をそのまま使いたい。**注意点**: URP では被写界深度は Camera ではなく **Volume の `DepthOfField` オーバーライド**で決まる。Maya のピント距離を `Camera` に入れても何も起きない(`Camera.focusDistance` は物理カメラの値で、URP の DoF はこれを読まない)。したがって **Camera と Volume の両方に書く**。

| 値 | Maya 側 | Unity で書く先 | 備考 |
|---|---|---|---|
| 画角 | 焦点距離(mm)+ フィルムゲート | `Camera.fieldOfView`(物理カメラ有効時は `focalLength` + `sensorSize`) | FBX 取り込みが Camera の画角カーブを作る(要検証、§5.2)。作らなければ焦点距離カーブから D-Drive が計算 |
| ピント距離 | `focusDistance` | **Volume: `DepthOfField.focusDistance`**(+ `Camera.focusDistance` にも書く。HDRP へ持ち込んだときはこちらが効く) | FBX に載るかは要検証。載らなければ Maya 側でカスタムアトリビュート `dd_focusDistance` をカメラに追加してキーを打つ(標準機能。§5.5 のロケーターと同じ経路) |
| 絞り | `fStop` | **Volume: `DepthOfField.aperture`** | 同上(`dd_fStop`) |
| 焦点距離 | 上と同じ | **Volume: `DepthOfField.focalLength`**(Bokeh モードのボケ量に効く) | 画角と同じカーブから |
| Near / Far | — | 引き継がない(ゲームカメラの値のまま) | Maya の値はモデリング用の値で、ゲームの描画距離とは無関係 |

Volume の書き方:

- **シーンの Volume(プロジェクトの VolumeProfile)は触らない**(Data 読み取り専用の原則と同じ。カットシーンが終わったあとに設定が残る事故を防ぐ)
- CutsceneManager が **D-Drive 専用の Global Volume**(`DDriveCutsceneVolume`、`priority` は大きめ、`isGlobal = true`)を初回に 1 つ作り、実行時に `ScriptableObject.CreateInstance<VolumeProfile>()` で作ったプロファイルに `DepthOfField`(`mode = Bokeh`)だけを載せる。**毎フレームの alloc は無し**(プロファイルとオーバーライドは 1 回だけ作り、`focusDistance.value` 等を書き換えるだけ)。`weight = w`(§4.6.2)で、ブレンド中は DoF もフェードする。`w = 0` のとき `weight = 0` = 通常時は存在しないのと同じ
- Volume は `Camera` の `UniversalAdditionalCameraData.volumeLayerMask` に含まれるレイヤーに置く(そのカメラの Volume マスクに含まれる最初のレイヤーを使う。無ければ警告 1 回 + DoF を書かない)。`renderPostProcessing = false` のカメラでは DoF が効かないので、これも警告 1 回(Validation は静的に検査できないため実行時警告のみ)
- `Focus = Off` にすればピントは一切書かない(低スペック向けに DoF を切る、既存の DoF 設定を尊重する、等)。`CameraOnly` は Volume を作らない(HDRP 移植時の逃げ道。[42] B-1 のとおり URP 以外は非対応だが、コードの分岐点だけ用意しておく)
- 型は `UnityEngine.Rendering.Volume` / `VolumeProfile`(Core RP)と `UnityEngine.Rendering.Universal.DepthOfField`(URP)。**`DDrive.Runtime.asmdef` は現状 URP を参照していない**(Editor のみ参照)ため asmdef 変更が要る → **2026-09-18 に直接追加を承認済み**(§6、§7.1-10)

#### 4.6.5 実行順の契約(ゲームカメラ制御より後に書く)(2026-09-18 改定: 確認事項 → D-Drive が課す契約)

> **2026-09-18 ユーザー回答**: MS2026 のカメラ制御は「どこで姿勢を書くか」がまだ決まっていない。**D-Drive 側が仕様を決め、MS2026 のカメラがそれに合わせる**(§7.1-11)。したがって本節は「着手前に確認する事項」ではなく、**持ち込み先のゲームコードが守るべき契約**として書く。契約の持ち込み先向けの記述は [ProgrammerManual/rules.html](ProgrammerManual/rules.html) 「ゲームカメラ制御の実行順」節(本節と同じ内容。両方を同時に更新する)。

**前提(確認済みの事実)**

- `GameLoopDriver` は `Update` で全 Manager を Tick する(`Runtime/Loop/GameLoopDriver.cs`)。ゲームのカメラ制御は普通 `LateUpdate` で書くので、Tick の中で Camera に書くと**そのあとゲーム側に上書きされる**
- Unity の `DefaultExecutionOrder` は `Update` / `LateUpdate` など同じフェーズ内の呼び出し順を決める(値が小さいほど先。既定 0。ProjectSettings > Script Execution Order の設定があればそちらが優先)。D-Drive は既に `DDriveRuntimeBootstrap` を `[DefaultExecutionOrder(-1000)]` にして「全 `Awake` より先」を確保している([02] §14、ProgrammerManual/bootstrap.html)

**D-Drive 側の実装(契約の D-Drive 側の履行)**

- CutsceneManager は Tick(`Update`)では「`T` と `w` の評価」までを行い、Camera への書き込みは **`DDriveCutsceneCameraApplier`**(`Camera.main` に初回自動追加する小さな MonoBehaviour。CameraFx の Shake ノードと同じ「無ければ作る、見つからなければ警告 1 回 + no-op」の流儀)が **`LateUpdate`** で行う。書き込み直前に Camera の現在姿勢を `G` として読む(§4.6.2)
- Applier は **`[DefaultExecutionOrder(DDriveCutsceneCameraApplier.ExecutionOrder)]`、`public const int ExecutionOrder = 1000`** を付ける。値の根拠:
  - `DDriveRuntimeBootstrap` の `-1000` と対称。D-Drive は「−1000 = 誰よりも先に組み立てる」「+1000 = 誰よりも後にカメラを書く」の両端を占め、**ゲームコードは (−1000, 1000) の範囲に収まる**、という 1 行で説明できる規約になる
  - 典型的なゲームコードは既定 0、手で順序を付ける場合も ±数百に収まるのが通例。1000 は「意識して超えない限り超えない」大きさで、かつ `int.MaxValue` のような極端な値と違って将来 D-Drive 側でさらに後段が要るとき(例: 1100)の余地が残る
  - 定数を public にして、ゲーム側が「D-Drive より前」を明示したいときに `DDriveCutsceneCameraApplier.ExecutionOrder - 1` のように参照できるようにする(数値の直書きを避ける。`DDriveMenu` 定数と同じ考え方)
- Applier 自身の順序が ProjectSettings の Script Execution Order で書き換えられていないことも契約に含める(下の検出 1 で検査)
- `w = 0` のフレーム(Cutscene 非再生時)は `LateUpdate` で何もしない(§4.6.2)。Applier が付いているだけではゲームカメラに影響しない

**ゲーム側(持ち込み先)が守る条件 — 契約**

| # | 条件 | 理由 |
|---|---|---|
| G-1 | カメラ(`Camera.main` の Transform・`fieldOfView`・物理カメラ値)への書き込みは **`Update` / `FixedUpdate` / `LateUpdate` のいずれかで行う**。`LateUpdate` の場合、そのスクリプトの実行順は **1000 未満**(`DefaultExecutionOrder` も ProjectSettings の Script Execution Order も) | Applier(1000)が同じフレームの `LateUpdate` の最後に `G` を読んで上書きするため |
| G-2 | **`LateUpdate` より後で姿勢を書かない**: `PlayerLoop` の `PostLateUpdate` 以降に挿した独自システム、`Application.onBeforeRender`、`RenderPipelineManager.beginFrameRendering` / `beginCameraRendering` / `Camera.onPreCull` 等の描画コールバック内での Transform・画角の書き込みを行わない。`yield return new WaitForEndOfFrame()` からの書き込みも避ける(描画の後なので絵は潰さないが、次フレームの `G` を汚し G-4 と同じ問題になる) | いずれも Applier の後に走り、D-Drive の書き込みを潰す |
| G-3 | Cinemachine を使う場合、`CinemachineBrain` の Update Method は `LateUpdate` / `SmartUpdate` / `FixedUpdate` のいずれか(既定のまま)で、Brain の実行順も 1000 未満 | Brain は `LateUpdate` で書くので G-1 と同じ扱い。`DDRIVE_CINEMACHINE` アダプタ(§7.2-1)を作るまではこの契約だけで共存する |
| G-4 | (推奨)ゲームカメラ制御は**自前の状態**(追従目標・前フレームの自分の計算値)から毎フレーム姿勢を計算し、`Camera.main.transform` の現在値を「前フレームの自分の姿勢」として読み戻さない | 再生中は Camera に D-Drive が `lerp(G,T,w)` を書いているため、読み戻す実装だと `G` が D-Drive の書き込みに引きずられる(見た目は BlendOut が近い位置へ戻るだけなので破綻はしないが、終了後にゲームカメラが「本来の位置」へ改めて動く) |
| G-5 | 再生中に `Camera.main` を差し替えない(差し替えたら Applier は新しいカメラへ付け直し、その回の再生は BlendOut 無しで終了する。CameraFx の P1 対応と同じ挙動) | 1 カメラ上書き方式の前提 |

**契約が守られなかったときに起きること**

| 破り方 | 症状 | 補足 |
|---|---|---|
| G-1 違反(`LateUpdate` で実行順 ≥ 1000 のスクリプトがカメラを書く)/ G-2 違反で**毎フレーム**上書き | **演出が効かない**: カメラはゲームカメラのまま、キャラ・SE・VFX だけ再生される。さらに Volume の DoF(§4.6.4)だけは効くので、ゲームカメラのままピントだけ変わる不自然な絵になる | いちばん気づきにくい壊れ方(エラーが出ず、「カメラが動かない」だけ)。検出 2 で必ずログに出す |
| G-2 違反で**間欠的に**上書き(コルーチンの `WaitForEndOfFrame`、特定条件でだけ走る補正等) | **カメラがカクつく / 震える**: フレームごとに `T` と `G` が交互に出る | 検出 2 が「上書きされたフレーム数」を数えるので間欠でも分かる |
| G-4 違反 | 再生終了後、ゲームカメラが BlendOut の到達点から本来の位置へもう一度動く(二段階の戻り) | 破綻ではないので Info 相当。検出は難しい(ゲーム側の実装次第)ため契約の「推奨」に留める |
| G-5 違反 | その回の再生のカメラだけ即時に切れる(BlendOut 無し)。次回からは新しいカメラで正常 | 警告 1 回 |

**検出(気づけない壊れ方を作らない)**

| # | 手段 | 何を見るか | 重度 / 出し方 | 実装チケット |
|---|---|---|---|---|
| 1 | **Validation(静的、Editor)**: `CameraExecutionOrderValidator`(`IValidator`。`Validation > Run All` と CI の `ValidateAll` に載る) | (a) `MonoImporter.GetAllRuntimeMonoScripts()` の全スクリプトについて **実効実行順**(ProjectSettings の値があればそれ、無ければ `DefaultExecutionOrder` 属性)を求め、**1000 以上のものが D-Drive 以外にあれば Warning**(「LateUpdate でカメラを書いている場合、Cutscene のカメラが効きません」)。(b) `DDriveCutsceneCameraApplier` の実効実行順が 1000 でなければ **Error**(ProjectSettings で書き換えられている)。(c) `Assets/` 配下の `.cs` を `ForbiddenApiScanner` と同じテキスト走査で調べ、`PlayerLoop.SetPlayerLoop` / `PostLateUpdate` / `WaitForEndOfFrame` / `onBeforeRender` / `beginCameraRendering` を含むファイルを **Info** で列挙(「カメラを書いていないか人が確認する」用。誤検知は許容し、重度を上げない) | Warning(a)/ Error(b)/ Info(c)。[42] §5.8 の 2 段階ルールどおり、(a) は Warning から始める | 6-10d |
| 2 | **実行時(Editor + Development Build)**: Applier が `RenderPipelineManager.endCameraRendering` で自分のカメラの描画直後に **「このフレームに自分が書いた姿勢」と「描画に使われた Camera の姿勢」を比較**する | `w > 0` のフレームで位置・回転・`fieldOfView` が自分の書き込みと一致しない(許容差 1e-4)なら「上書きされた」と数える。**再生 1 回につき警告 1 回**(`CutsceneHandle` 単位。上書きされたフレーム数 / 総フレーム数と、G-1・G-2 の確認を促す文言、`CameraExecutionOrderValidator` の実行を案内)。描画の**後**で比べるので、`PostLateUpdate`・`onBeforeRender`・`beginCameraRendering`(購読順に関係なく)のどこで書かれても「描画に使われた姿勢が D-Drive の書き込みと違う」として一括で捕まえられる(G-2 の全パターン)。比較は float 数個で alloc 無し(購読は `OnEnable` で 1 回)。製品ビルドでは購読しない(`UNITY_EDITOR \|\| DEVELOPMENT_BUILD`)。「`endCameraRendering` 時点の Transform = 描画に使われた姿勢」は §7.3 で実機確認 | 警告ログ(1 回 / 再生)。確認用シーン(6-10d)の再生でも同じ経路が動くので、デザイナーのプレビュー段階でも分かる | 6-10b |
| 3 | **実行時**: Applier の `LateUpdate` が Cutscene 再生中に呼ばれなかったフレーム(Applier が無効化された / `Camera.main` が消えた / 別カメラに差し替わった) | 再生 1 回につき警告 1 回 + G-5 の処理(付け直し、その回は即終了) | 6-10b |

- 検出 2 の許容差は `Volume` の `weight` には掛けない(Volume は D-Drive 専用なので他者が書かない)
- 「合わない場合は Applier の挿入点を PlayerLoop の `PostLateUpdate` 末尾に変える」という旧版の逃げ道は**採らない**(D-Drive 側が契約を決めた以上、ゲーム側が合わせる。`PlayerLoop` の書き換えは持ち込み先の他システムと衝突しやすく、[42] の互換性ポリシー上も維持が重い)。契約を守れない持ち込み先が出た場合はそのときに改めて判断する(§7.2 の表に追記)
- MS2026 側への伝え方: [ProgrammerManual/rules.html](ProgrammerManual/rules.html) の節 + MS2026 のカメラ設計時に本節を参照してもらう(移植時のチェックは [42] P-12)

### 4.7 ネット(ローカル再生と同期再生の両方)(2026-09-18 新規)

ユーザー決定: 両方要る。**新しいフィールドは足さず、既存の `AssetFlags.Net`(全 Data 共通、[02] §2 / [14] §3)で切り替える。** Presentation(5-8 / 5-9 / 6-0)と同じ流儀にそのまま乗せる。

| `Flags.Net` | 意味 | 使いどころ |
|---|---|---|
| **Local**(既定) | 呼んだクライアントだけで再生。メッセージを送らない | 自分の画面だけの演出(勝利ポーズのカメラ等)。両者のゲームコードが同じタイミングで `Play` を呼ぶなら位相はほぼ揃うが、保証はしない |
| **Cosmetic** | 行為者が `CutscenePlayMsg` を Broadcast し、全員が `StartNetTime` に合わせてシークして再生。Skip / Cancel も中継。Late Join は Host の台帳から復元 | 両者が**同じ映像を同じ位相で**見る必要がある演出(必殺技カットイン、ラウンド開始デモ) |
| Simulated | Cutscene には意味を持たせない(Presentation と同じ)。Validation Info でローカル再生にフォールバック | — |

Cosmetic の中身(Presentation 5-8/5-9 の設計をそのまま流用。新規メッセージ型は 3 つ):

- `CutscenePlayMsg { CutId, SelfNetId, TargetNetId, Position, StartNetTime, Seed, HandleNetKey }` / `CutsceneSeekMsg { HandleNetKey, ToTime }`(Skip 用)/ `CutsceneCancelMsg { HandleNetKey }`。`HandleNetKey` の採番・`ReliableOrdered`・Host 経由中継・レート制限(60/秒/クライアント)・未知キー保留(6-6 の K3)はすべて `PresentationManager` の既存実装と同じ規則
- **受信側のシーク**: `elapsed = NetworkTime − StartNetTime` で `director.time` を合わせる。Timeline のカメラ・Animation トラックは連続系なので途中から再生できる。D-Drive SE / VFX クリップの one-shot は Presentation と同じ猶予(0.5 秒)以内なら遅れて発火、それより古ければスキップ
- **Skip**: `Skip` の種類(即終了 / マーカーまで)は Data で決まる。Cosmetic では Skip を Broadcast(`CutsceneSeekMsg`)し、自分を含む全員が受信してから Seek する(Cancel と同じ「Broadcast 前に自分だけ飛ばない」規則)。「相手にスキップさせない」はゲームロジック側の仕事(`CutsceneSkip.Disabled` にすれば Data として禁止もできる)。既定は「誰でも Skip でき、全員に効く」(§7.2-5)
- **PredictLocal**: Presentation と同じ意味。必殺技の入力に対して行為者は即再生し、自分の Broadcast を受信しても二重生成しない。既定 false
- **Late Join**: Host の台帳(`HandleNetKey → {Data, ctx, StartNetTime, Seed}`)から `CutscenePlayMsg` を再送 → 受信側はシーク再生で復元。Presentation と違い、カットシーンは連続系なので途中参加でも「今の位置から」見える
- **カメラ**: 受信側でもカメラを奪う(それが「同じ映像」の意味)。相手の画面でカメラを奪ってよいかはゲームデザインの問題で、奪いたくなければ Local にする
- **LockInput**: ローカル。各クライアントが自分の入力を止める(Host 権威のゲームロジックが入力を捨てる側の実装は MS2026 側)
- ContentHash([14] §7、6-5)は `CatalogEntry` の Id / Type / Address / `Flags.Net` を対象にするため、Cutscene のカタログを `ContentHashCatalogCoverageValidator` の対象に含める(6-10a。含めないと Error)

---

## 5. Maya → Unity 取り込み(6-10c、Maya スクリプト無し)

### 5.1 Maya 側でやること(標準 FBX 書き出しだけ)

| 項目 | 設定 | 理由 |
|---|---|---|
| 書き出し単位 | **1 ショット = 1 FBX セット**(決定 2026-09-18)。セット = カメラ + 小物の `<ショット>.fbx` 1 本 + **キャラごと**の `<ショット>__<Model識別子>.fbx`(§5.4 Humanoid の制約) | Unity 側で「このファイル群 = この CutsceneData」と 1 対 1 にできる |
| 逃げ道: 1 FBX に複数ショット | `CutsceneData.SourceFrameRange` にフレーム範囲を入れると、その範囲だけを切り出す。2 つ目以降の CutsceneData は AssetBrowser の「同じ FBX から別ショットを作る」で作り、`ImportSourceGuid`(既存フィールド)で同じ FBX を指す | 既定は A(1 ショット = 1 FBX)。範囲が空(0/0)なら FBX 全体 |
| Animation > Bake Animation | ON(開始〜終了フレーム) | IK・コンストレイント・エクスプレッションは Unity に来ないため焼く |
| Cameras | ON | カメラの位置・回転・画角(Focal Length)を持ってくる |
| 単位 / アップ軸 | cm / Y-up(Unity の既定取り込みに合わせる) | スケールずれ防止 |
| シーンの fps | プロジェクトの既定(30 または 60。`CutsceneImportProfile.DefaultFrameRate`)| §5.3。違っても取り込めるが Validation Info |
| 命名 | §5.5 の最小ルール(キャラ FBX のファイル名サフィックス = D-Drive のモデル識別子) | Unity 側でトラックと役割・使うモデルを自動判定するため |

> キャラクター本体(メッシュ・リグ)は通常どおり別 FBX で ModelData に登録し、カットシーン FBX には**アニメーションだけ**入れる(決定 2026-09-18。同じキャラを複数ショットで使い回すため、また Humanoid のリターゲットを ModelData の Avatar で揃えるため。§5.4)。

#### 5.1.1 Maya 作業者の手順(決定 2026-09-18、§7.2-2「キャラごとに FBX を分ける」の帰結)

1 ショットにつき **「カメラ + 小物」1 回 + キャラの数だけ** `File > Export Selection` を行う。Maya スクリプトは使わない(標準の FBX 書き出しだけ)。デザイナー / アーティスト向けの手順書は [DesignerManual/cutscene-maya-export.html](DesignerManual/cutscene-maya-export.html)(本節と同じ内容。両方を同時に更新する)。

| 手順 | 内容 | 補足 |
|---|---|---|
| 0. 準備 | シーンの fps をプロジェクト既定(30 / 60、§5.3)に合わせる。単位 cm・Y-up。キャラリグの**名前空間(または参照ファイル名)を D-Drive の Model 識別子に揃える**(`Hero:` 等)。複数カメラで作った場合は Camera Sequencer の「Ubercam 作成」で 1 台に焼く | fps が違っても取り込めるが Validation Info(§5.3)。識別子が違ってもバインド表で手で結べる(§5.5) |
| 1. カメラ + 小物 | カメラ(Ubercam)・小物(`PRP_*`)・イベント用ロケーター(`EVT_*`)を選択 → `File > Export Selection` → FBX。書き出し名 `<ショット>.fbx`、保存先 `Assets/SourceAssets/Cutscene/<カテゴリ>/` | キャラは選ばない。動かない背景も選ばない |
| 2. キャラ(キャラの数だけ繰り返す) | そのキャラの**ルートジョイント**を選択(階層ごと。メッシュは選ばない)→ `File > Export Selection` → `<ショット>__<Model識別子>.fbx`(`__` は 2 つ)。同じキャラ 2 体目は `<ショット>__<Model識別子>_2.fbx` | 1 FBX = 1 Humanoid の制約(§5.4)のため。Export Selection の回数 = キャラ数 |
| 3. 書き出しオプション(1・2 共通) | `Animation` ON / `Bake Animation` ON(開始〜終了 = ショットの全フレーム、Step 1)/ `Cameras` ON / `Lights` OFF / `Embed Media` OFF / `Include > Input Connections` **OFF**(選択していないノードが付いてくるのを防ぐ)/ キャラ FBX は `Deformed Models`・`Skins`・`Blend Shapes` **OFF**(アニメだけ)/ 単位 Automatic(cm)/ Up Axis Y | **同じショットの全ファイルで開始〜終了フレームを揃える**(ずれると Validation Warning、§5.3)。`@` をファイル名に使わない(§5.5) |
| 4. Unity で確認 | ファイルをフォルダに置くだけで自動取り込み(§5.2)。`Validation > Run All` で fps 不一致 / ModelData 未発見 / Avatar 未設定の Warning を確認 | Unity 側の作業はデザイナー(D-Drive トラックの追加、§4.3) |
| 5. 直すとき | Maya で直して**同じファイル名で上書き**書き出し → Unity が再取り込みし、自動生成トラックだけ差し替わる(デザイナーが足した D-Drive トラック・`StepFps` 等の設定は保持、§5.2-4) | ファイル名を変えると別ショット扱いになる |

- 1 FBX に複数ショットを入れた場合(逃げ道)は、Unity 側で `SourceFrameRange` を切る(§5.1 表)。Maya 側の手順は同じ
- 「キャラごとに Export Selection」を嫌って 1 FBX に全キャラを入れると、Humanoid では片方しかリターゲットできず Validation Warning になる(§5.4)。Generic で逃げる経路は作らない

### 5.2 Unity 側の自動処理

1. `Assets/SourceAssets/Cutscene/<カテゴリ>/` に FBX が入る → `AssetPostprocessor` が検知(`ImportRule` の 10 種別目として `Cutscene` を追加。[10] §3 の 9 種別 + 1)
2. ファイル名で役割を分ける: `<ショット>.fbx` = カメラ + 小物(Generic)、`<ショット>__<Model識別子>.fbx` = そのキャラの骨アニメ(**Humanoid**。`ModelImporter.animationType = Humanoid`、`avatarSetup = CopyFromOther`、`sourceAvatar = ModelData.Avatar`。§5.4)。同じ `<ショット>` のファイル群を 1 つの CutsceneData にまとめる
3. FBX 内のアニメ付きノードを §5.5 のルールで分類し、まとまりごとに AnimationClip を切り出す(カメラ / キャラ / 小物 / イベント用ロケーター)。`SourceFrameRange` があればその範囲だけ
4. TimelineAsset を生成(既存なら**自動生成トラックだけ差し替え、デザイナーが足した D-Drive トラックは保持**)
   - カメラ → **D-Drive Camera クリップ**(§4.3、§4.6)。取り込み後の Camera コンポーネントのカーブ(位置・回転・画角)を `AnimationUtility.GetEditorCurve` で抽出してクリップのカーブに写す。画角: Unity の FBX 取り込みがカメラの画角アニメ(`field of view` / `focalLength`)を作るかは**要検証**。作らなければ焦点距離カーブ + フィルムゲートから D-Drive が垂直画角を計算する。**ピント距離・絞り**: FBX のカメラ属性(`FocusDistance` / `FStop`)を Unity が Camera に反映するかは**要検証**(§7.3)。反映しなければ Maya 側でカメラにカスタムアトリビュート `dd_focusDistance` / `dd_fStop` を追加してキーを打つ運用にし、`OnPostprocessGameObjectWithAnimatedUserProperties` で読む(ロケーターと同じ経路)。どちらも取れなければカーブは空 = ピントは書かない(`Focus = Off` 相当)
   - クリップの `StepFps` / `BlendIn` / `BlendOut` / `Focus` は新規生成時だけ `CutsceneImportProfile` の既定値を入れ、再取り込みでは保持する
   - キャラ → Animation トラック(Humanoid クリップ)。役割 = ファイル名サフィックスの `Hero`、使うモデルは ModelData 識別子 `Hero` を自動で探してバインド表に入れる(見つからなければ空欄 + Validation Warning。手で選び直せる)
   - 小物 → Animation トラック(Generic)。役割 = 名前空間 / `PRP_` 接頭辞
   - イベント用ロケーター → D-Drive Signal マーカー(§5.5、要検証)
5. CutsceneData を作成・更新(`Assets/GameData/Cutscene/<カテゴリ>/CUT_<カテゴリ>_<識別子>.asset`)、`FrameRate` を FBX の fps に設定、Addressables 登録、ID 採番
6. 再取り込み時の差分(ノードが増えた / 消えた / 長さが変わった / fps が変わった)をログと Validation に出す。**消えたノードのトラックは削除せずミュート**(デザイナーの手作業を消さない)

### 5.3 fps とタイミング(2026-09-18 改定)

ユーザー決定: 30 と 60 を切り替えられるようにする(どちらかに固定しない)。「切り替え」が何を指すかを 2 段に分ける。

| 段 | 何か | どこに持つ | 変えると何が起きるか |
|---|---|---|---|
| **プロジェクトの既定 fps** | 「このプロジェクトの Maya シーンは何 fps で作るか」の宣言 | `CutsceneImportProfile.DefaultFrameRate`(30 / 60。`FindOrDefault()` の設定 SO、[05] の `Anim2DImportProfile` と同型) | 新規 CutsceneData の初期値と、Validation の比較基準が変わるだけ。**既存の CutsceneData は変わらない** |
| **CutsceneData ごとの fps** | その FBX が実際に焼かれた fps(真実は Maya) | `CutsceneData.FrameRate`(取り込みで FBX から自動設定。手で変えられる) | `TimelineAsset.editorSettings.frameRate` に反映(Timeline ウィンドウのスナップ・フレーム表示)。**再生は秒ベース**なので、30fps のカットシーンも 60fps の描画で滑らかに補間される(逆も同じ)。カメラをコマ落ちさせたいなら `StepFps`(§4.6.3)であって `FrameRate` ではない |

- D-Drive のクリップ / マーカーは秒で持つが、Inspector ではフレーム表記も併記(Anim の Frame イベントと同じ。換算は `CutsceneData.FrameRate`)
- **Validation**(6-10d):

| 検査 | 重度 | 意味 |
|---|---|---|
| 同じ CutsceneData を構成する FBX 群(カメラ / キャラ / 小物)の fps が不一致 | Warning | 旧 §5.3 の「カメラ 24fps、キャラ 30fps」問題。必ずずれる |
| FBX の fps と `CutsceneData.FrameRate` が不一致(手で変えた / 再取り込みで変わった) | Warning | 自動修正(FixAction: FBX に合わせる) |
| FBX の fps とプロジェクト既定 fps が不一致 | Info | 混在自体は許す(30 で作った既存ショットと 60 の新ショットが同居してよい)。「宣言と違う」ことだけ知らせる |
| Camera クリップの `StepFps` > `FrameRate` | Warning | 元より細かくはできない(値は無視されず、単に効かない) |
| `FrameRate` が `StepFps` の整数倍でない(30 元に 24 等) | Warning | 保持フレーム数が不均一になる。候補(60 → 30/20/15/12/10、30 → 15/10/6)を提示 |
| `FrameRate` が 30 / 60 以外 | Info | 24fps 等も動くが、プロジェクト方針(30 / 60)から外れている |

### 5.4 Humanoid(決定 2026-09-18)

ユーザー決定: **Humanoid**。キャラ本体の FBX とアニメの FBX は分ける。

- **リターゲット**: カットシーン FBX(アニメ専用)を `Humanoid` + `CopyFromOther`(`sourceAvatar = ModelData.Avatar`)で取り込む。同じ Avatar 定義を使うので、ModelData の Prefab(Humanoid Animator)にそのまま流せ、筋肉マッピングのずれによる姿勢崩れが起きない。`ModelData.Avatar` が未設定なら既存の `ModelDataValidator` の Error(「Animator はあるが Avatar 未設定」)が先に出るので、Cutscene 側は Warning + `CreateFromThisModel` で仮に取り込む(骨だけの FBX からでも Avatar は作れる)
- **1 FBX = 1 Humanoid の制約**: Unity の `ModelImporter` は 1 ファイルに 1 つの Avatar / `animationType` しか持てない。**1 つの FBX に 2 キャラ(`Hero:` と `EnemyBoss:`)を入れると Humanoid では片方しかリターゲットできない**。したがって **キャラごとに FBX を分ける**(`<ショット>__Hero.fbx`、`<ショット>__EnemyBoss.fbx`。Maya では Export Selection をキャラ数分行う。スクリプト不要だが手数は増える。§7.2-2)。カメラ・小物は Generic なので 1 本にまとめてよい
- 同じキャラ 2 体(`Hero_2`)は `<ショット>__Hero_2.fbx`。ModelData は `Hero` を引く(末尾の `_2` 以降を落として検索)
- Generic は**小物専用**(剣・扉など。ボーン名一致で流す。Validation で検出)。キャラを Generic で取り込む経路は作らない(ModelData 側が Humanoid 前提のため、Generic クリップは Humanoid Animator で再生されない)
- Timeline の Animation トラック設定: `Apply Foot IK` は既定 OFF(Maya で焼いた足位置を優先)。ルートモーション(Hips のワールド移動)と原点(§4.2.1)の組み合わせは 6-10a で実機確認(§7.3)
- **Avatar Mask / Override トラック**(上半身だけ差し替え等)は Timeline 標準機能がそのまま使える(Humanoid の利点)

### 5.5 FBX に何を入れるか / Unity でどう見分けるか / 命名規則(2026-09-13 追記、2026-09-18 改定)

**入れるもの**

| 入れる | 内容 | 備考 |
|---|---|---|
| ○ カメラ | 位置・回転・画角(焦点距離)。可能ならピント距離・絞り(§5.2 要検証) | ショットの切り替えは Maya の Camera Sequencer の標準機能「Ubercam 作成」で 1 台のカメラに焼くのが簡単(スクリプト不要)。複数カメラのまま出す場合は Unity 側でカメラごとに Camera クリップを作り、Timeline 上で並べる(クリップ間は Timeline の標準ブレンド、§4.6.2) |
| ○ キャラの骨アニメ | ジョイントのアニメだけ(メッシュは入れない「アニメ専用」書き出し)。**キャラごとに別 FBX**(§5.4) | キャラ本体は別途 ModelData として登録済みのものを使う |
| △ 小物のアニメ | 剣・扉など動くもの | 動かない背景は入れない(Unity 側のシーンで配置) |
| △ イベント用ロケーター | 「ここで SE / VFX」の目印 | §下記。無くても Unity の Timeline で後から置ける |
| × メッシュ・マテリアル・ライト | — | 見た目は Unity 側(ModelData / MaterialData)が真実。ライトは Unity で作る |

**Unity での見分け方**

- **キャラは FBX のファイル名で分かる**(`<ショット>__<Model識別子>.fbx`。§5.4 の制約でキャラごとに 1 本なので、名前空間に頼る必要が無い)。名前空間(`Hero:`)は付いていてもよい(サフィックスが無いファイルの救済に使う)
- **カメラは名前に頼らず判別できる**。FBX はノードの「種類(カメラ / ジョイント / 空ノード)」を持っており、Unity の取り込み結果でも Camera コンポーネントが付くので確実に分かる
- 小物は名前空間(またはノード名の接頭辞 `PRP_`)で判別する

**最小の命名規則(Maya 作業者が守るのはこれだけ)**

| 対象 | ルール | 例 | Unity 側の対応 |
|---|---|---|---|
| ショット | ファイル名 `<ショット>.fbx`(カメラ + 小物) | `Opening01.fbx` | CutsceneData `CUT_<カテゴリ>_Opening01` |
| キャラ | ファイル名 `<ショット>__<Model識別子>.fbx`(`__` 2 つ) | `Opening01__Hero.fbx` | 役割 `Hero`、ModelData `MODEL_*_Hero` を自動でバインド、Humanoid + `ModelData.Avatar` で取り込み |
| 同じキャラを 2 体 | 識別子の末尾に `_2` 以降 | `Opening01__Hero_2.fbx` | 同じ ModelData を 2 体生成 |
| カメラ | 1 台なら自由。複数なら `CAM_<名前>` | `CAM_Main` | 役割 MainCamera(既定)。ピント・絞りは任意でカスタムアトリビュート `dd_focusDistance` / `dd_fStop`(§5.2) |
| 小物 | 名前空間 = Model 識別子、または `PRP_<Model 識別子>` | `PRP_Sword` | ModelData Sword |
| イベント(任意) | ロケーター `EVT_<キー>` | `EVT_Hit` | Signal マーカー `cutscene/hit` |

- `@` 区切り(`Model@Anim.fbx`)は使わない。Unity が「同じフォルダの `Model.fbx` にアニメを合流させる」レガシー挙動を起こすため、`__` にした
- **Maya のモデル名と Unity の ModelData 識別子を一致させるのが要**。Maya のキャラリグのファイル名や名前空間を、仕様書「アセット」タブ([27] §3.1)の Model 識別子に揃えて運用すれば、取り込み時の手作業がほぼ無くなる
- 一致しない場合でも CutsceneData のバインド表で手動で結び付けられる(ルールは「自動で埋まる」ためのもので、破ったら取り込めないわけではない)

**イベント用ロケーター(任意・要検証)**

- ロケーター `EVT_Hit` にカスタムアトリビュート(例 `ddEvent`、整数)を追加してキーを打つと、FBX にアニメ付きユーザープロパティとして出る。Unity の `AssetPostprocessor.OnPostprocessGameObjectWithAnimatedUserProperties` で読めるので、値が変わったフレームに Signal マーカーを自動で置ける
- Maya 側はアトリビュート追加とキー打ちだけ(標準機能)。ただし Unity 2023 以降の FBX 取り込みで確実に取れるかは 6-10c 着手時に検証する。取れなければこの機能は外し、Unity の Timeline 上で置く運用にする。**カメラのピント・絞り(`dd_focusDistance` / `dd_fStop`)も同じ経路に相乗りする**ので、検証は一度で済む

---

## 6. 影響範囲(実装時に確認が要るもの)(2026-09-18 改定)

- **asmdef(2026-09-18 ユーザー承認済み、§7.1-10)**: `DDrive.Runtime` に `Unity.Timeline` を追加(1.5 のとおり)。加えて §4.6.4 の Volume 書き込みのため **`Unity.RenderPipelines.Universal.Runtime` と `Unity.RenderPipelines.Core.Runtime`** も追加(現状は `DDrive.Editor` だけが参照)。Runtime asmdef の参照が増えるのは CLAUDE.md §0-9「asmdef 構成は聞く」の対象で、**「直接追加」を承認**(Timeline も URP も必須依存であり、任意依存の NGO と違って `versionDefines` で切る理由が無い)。不採用の代案 = Timeline 関連を別 asmdef `DDrive.Runtime.Timeline`(Foundation / Runtime / Timeline / URP を参照)に分ける案。`PresentationManager` の `TrackKind.Timeline` から Cutscene を呼ぶには `DDrive.Runtime` 側にインタフェース(`ICutscenePlayer`)を置いて Bootstrap で差し込む逆依存の回避が要り、生成コード(`AssetIds.g.cs` の `AssetId<CutsceneMarker>`)と P-5 の `DDrive.Generated.asmdef` も新 asmdef を参照する必要があるため見送った
  - **配布への影響([42] に 2026-09-18 付きで追記済み)**: これまで URP は `DDrive.Editor` だけが参照していた(= 持ち込み先で Editor がコンパイルできる条件)が、6-10 以降は **`DDrive.Runtime` が実行時に URP を参照する**。持ち込み先にとって URP は「Editor 拡張の都合」ではなく**ゲーム実行に必須の依存**になる。[42] §3.5 の依存表(`com.unity.render-pipelines.universal` / `com.unity.timeline` の「持ち込み先での扱い」)と §3.4 の記述を更新し、§5.10 に「既存依存の参照範囲が Editor → Runtime に広がる変更も『依存の追加』と同じ区分(MINOR + 明記)」を追記した。B-1(URP 以外は非対応)とは整合する(HDRP / Built-in の持ち込み先は元々非対応)。P 発効(P-13)前の変更なので今回は区分の適用対象外(記録のみ)
  - 新しいパッケージ依存は増えない(`com.unity.timeline` 1.8.12・URP 17.3.0 は manifest に導入済み)
- **Cinemachine は導入しない**(§4.6.1)。manifest 変更なし。[42] §3.5 依存表は `com.unity.timeline` が既に「○(6-10 以降)」で載っているので P-1 で「6-10 で有効化済み」に書き換えるだけ
- **AssetType enum**: `Cutscene` を末尾追加(シリアライズ値は不変)。`KnownPrefixes` に `CUT`([42] §5.13)
- **新規メッセージ型** 3 つ(§4.7)。P 発効前なので追加は自由だが、型名・名前空間はワイヤ互換([42] §5.6)になるので `Runtime/Net/CutsceneMessages.cs` に Presentation と同じ命名で置く
- **`ImportRule` の対象種別に `Cutscene` を追加**(9 → 10。[10] §3、[09] §1.1)。`SourceAssets/Cutscene/` の 1 つの `<ショット>` に複数 FBX が対応するため、既存の「1 元ファイル = 1 Data」の対応付け(`ImportSourceGuid`)は**カメラ FBX の GUID を代表**にし、キャラ FBX は CutsceneData 側のリスト(`SourceFbxGuids[]`)で追跡する
- **`CutsceneImportProfile`**(設定 SO、§4.1 / §5.3)を `Assets/GameData/Settings/` に `FindOrDefault()` で生成([42] §2.1 の「G: 持ち込み先で作る」分類)
- **ContentHash**: Cutscene カタログを `ContentHashCatalogCoverageValidator` の対象に含める(§4.7)
- **`DDriveCutsceneCameraApplier` / `DDriveCutsceneVolume`**: ランタイムがシーンに置く永続オブジェクト(CameraFx の `DDriveCameraShakeNode` と同じ流儀。DontDestroyOnLoad のカメラなら追従)。Applier は `[DefaultExecutionOrder(1000)]`(`public const int ExecutionOrder`)で、実行順の契約(§4.6.5)の D-Drive 側の履行。Editor / Development Build では `RenderPipelineManager.endCameraRendering` を購読して上書き検出(§4.6.5 検出 2・3)を行う
- **`CameraExecutionOrderValidator`(新規 `IValidator`、6-10d)**: 実行順の契約(§4.6.5 検出 1)。全ランタイムスクリプトの実効実行順(ProjectSettings の Script Execution Order > `DefaultExecutionOrder` 属性)を調べ、D-Drive 以外で 1000 以上 → Warning、Applier 自身が 1000 でない → Error、`PlayerLoop` / `onBeforeRender` / 描画コールバックを使うファイル → Info。P-6 の `ProjectSetupValidator` とは別の Validator にする(こちらは Cutscene を使わないプロジェクトには関係ないため、`CutsceneData` が 1 件も無いときは検査を省略)
- **`Cutscene` 静的ファサードの入力ロック公開面**(§4.5.1): `Cutscene.IsInputLocked` / `Cutscene.OnInputLockChanged`(R3)/ `CutsceneHandle.IsInputLocked` + EventBus Custom トリガ `cutscene/input_lock` / `input_unlock`。入力を実際に止める API は D-Drive に作らない(ゲーム側の責務)
- 標準の Audio / Control / Signal トラックは禁止 API 規約(AudioSource.Play / Instantiate 直呼び)と衝突するので、Validation で「D-Drive トラックを使ってください」と Warning を出す。**標準 Animation トラックをカメラにバインドしている**場合も同様に Warning(Camera クリップを使う)
- `PresentationManager` の `TrackKind.Timeline`(現在は警告 + no-op)を `CutsceneManager` に接続。Presentation → Cutscene → Presentation の循環を `PresentationDataValidator` / `CutsceneDataValidator` の両方で Error にする
- **Edit Mode プレビュー(2026-09-19、§4.4 実装メモ)**: `CutsceneDirectorContext`(Runtime、`Runtime/Cutscene/CutsceneDirectorContext.cs`)+ `CutsceneMarkerCursor<T>`(Runtime、`Runtime/Cutscene/CutsceneMarkerCursor.cs`)を新規追加。Editor 側は `Assets/DDrive/Editor/Cutscene/` に `CutsceneEditModeManagers`(Manager 束ね)・`CutsceneEditModePreviewProvider`(`[InitializeOnLoad]` の監視役)・`CutsceneEditModeCameraWriter`(カメラ直書き)・`CutsceneEditModeDirectorSetup`(確認用シーンにプレビュー用 Director を組み立てる)を追加。既存の `CutsceneManager`/`DDriveRuntimeBootstrap`/`CutscenePreviewHarness`(Play Mode 経路)は変更なし。asmdef 変更なし(`DDrive.Editor` は既に `Unity.Timeline` を参照済み)

---

## 7. 決定事項と要検証事項(2026-09-18 時点で未決なし)

### 7.1 決定(2026-09-18 ユーザー回答)

| # | 項目 | 決定 | 設計への反映 |
|---|---|---|---|
| 1 | 用途の比重 | **短い演出のみ**(数秒。長いカットシーンは作らない)。PresentationData と役割が重なることは承知のうえ | §3.1 使い分け(併存。統合可否は同日 2 回目の回答で確定、下の 9) |
| 2 | カメラ | **シームレスにしたい**(Maya カメラをただ再生するのではなく、ゲームカメラとの繋ぎが要る) | §4.6.1 独自ブレンド(Cinemachine 不採用)、§4.6.2 ブレンド仕様 |
| 3 | 書き出し単位 | **A(1 ショット = 1 FBX)**。ただし「1 FBX に複数ショット」の逃げ道として `CutsceneData` にフレーム範囲を持てる(既定は A) | §4.1 `SourceFrameRange`、§5.1。Humanoid の制約でキャラごとに FBX を分ける「FBX セット」になった(§5.4) |
| 4 | キャラアニメ | **Humanoid**。キャラ本体の FBX とアニメの FBX は分ける | §5.4、§5.5 命名規則(ファイル名サフィックス) |
| 5 | fps | **30 と 60 を切り替えられる**(固定しない) | §5.3(プロジェクト既定 = `CutsceneImportProfile`、Data ごと = `FrameRate`、再生は秒ベース) |
| 6 | ネット | **ローカル再生と同期再生の両方**(`CutsceneData` で切替) | §4.7(既存 `Flags.Net` の Local / Cosmetic。新フィールド無し) |
| 7 | (追加要望)カメラの fps を自由に | Unity の再生時に、ショットごとに調整できること | §4.6.3 評価時量子化(`StepFps`、非破壊) |
| 8 | (追加要望)カメラ設定の持ち越し(ピント等) | URP では DoF は Volume の設定なので Camera と Volume の両方へ書く | §4.6.4 |
| 9 | (同日 2 回目)**CutsceneData と PresentationData の統合可否** | **併存(案 A)**。判断基準は「Maya の FBX を使うなら Cutscene、使わないなら Presentation」の 1 つ。`CutsceneHandle.Signal` を持たない設計も確定 | §3.1(採用を明記)、§4.5。不採用の理由: B(Presentation に統合)は PresentationData のシリアライズ形式が大きく変わり(§0-9)、編集 UI とネット経路が 2 系統に割れる。C(Cutscene に統合)は 5-1〜5-16 の作り直しで `OnSignal` を Timeline で表せない |
| 10 | (同日 2 回目)**asmdef**: `DDrive.Runtime` に `Unity.Timeline` + URP(Universal.Runtime / Core.Runtime)を直接追加 | **直接追加を承認**(CLAUDE.md §0-9 の asmdef 構成変更。**2026-09-18 ユーザー承認**)。別 asmdef `DDrive.Runtime.Timeline` 案は不採用 | §1.5、§4.6.4、§6(配布への影響: 持ち込み先に URP がランタイム必須依存として増える → [42] §3.4 / §3.5 / §5.10 に追記)。不採用の理由: 別 asmdef は `ICutscenePlayer` の逆依存回避・生成コード・`DDrive.Generated.asmdef` の参照追加が要り、必須依存を切り離す利点が無い |
| 11 | (同日 2 回目)**ゲームカメラ制御の実行順** | **MS2026 側は未定のため、D-Drive が仕様(契約)を決め、MS2026 のカメラがそれに合わせる** | §4.6.5 を「確認事項」から「契約(G-1〜G-5)+ `DefaultExecutionOrder(1000)` + 違反時の症状 + 検出 3 手段(Validation / 実行時 endCameraRendering 比較 / Applier 未実行検出)」に書き換え。[ProgrammerManual/rules.html](ProgrammerManual/rules.html) に追記。旧案「Applier を `PostLateUpdate` 末尾へ挿す」は不採用(PlayerLoop 書き換えは持ち込み先の他システムと衝突しやすく、互換性ポリシー上も重い) |

### 7.2 決定(2026-09-18 同日 2 回目の回答。**未決なし = 6-10a に着手できる状態**)

旧「未決(6-10a 着手前にユーザーが決める)」の 9 件のうち 3 件は §7.1-9〜11 に昇格、残る 6 件も**すべて提案どおり**で確定した。経緯を追えるよう、採った理由と不採用の代案を残す。

| # | 内容 | 決定(2026-09-18) | 採った理由 | 不採用の代案とその理由 | 反映先 |
|---|---|---|---|---|---|
| 1 | **Cinemachine アダプタ** | **v1 は無し**。ゲーム側が Cinemachine を採用したら `DDRIVE_CINEMACHINE` の `versionDefines` で任意対応を後付けする | 要件(数百 ms の繋ぎ)に独自ブレンドで足り、`com.unity.cinemachine` は manifest に無い = 新規依存になる。[42] §5.10(依存追加 = MINOR + ウィザード検査)と §3.5 依存表を今増やす理由が無い。Cinemachine を使わない持ち込み先に依存を強いない | 今から導入: ブレンド曲線・Impulse が標準で揃うが、CameraFx(5-2)の Shake ノードとブレンド主体が二重になり、「短い演出のみ」の用途に重い | §4.6.1。実行順の契約(§4.6.5 G-3)で Cinemachine とはアダプタ無しでも共存する。**2026-09-20 追記(P-1)**: 「`com.unity.cinemachine` は manifest に無い」は D-Drive 開発リポジトリの話のまま変わらないが、移植先 MS2026 には 2026-09-19 時点で Cinemachine 3.1.7 が**導入済み**と確認した。MS2026 に限れば「持ち込み先に不要な依存を強いない」という不採用理由は当てはまらなくなるが、v1 の結論(アダプタ無し)自体は変えない。アダプタをいつ・どの P チケットで作るかは [42_distribution.md] §7 B-11 に要判断として追加した。**2026-09-20 決定(ユーザー回答)**: 初回リリース(1.0.0)はアダプタ無しの現状をそのまま同梱し、アダプタは確認済みになってから以降のアップデートで配信する |
| 2 | **キャラごとに FBX を分ける** | **分ける**。Maya 作業者は 1 ショットにつき「カメラ + 小物」1 回 + キャラの数だけ `Export Selection` を行う(スクリプト無しの運用は維持) | Unity の `ModelImporter` は 1 FBX に 1 Avatar / `animationType` しか持てず、Humanoid(§7.1-4)で 2 キャラを 1 FBX に入れると片方しかリターゲットできない。Export Selection の回数が増えるだけで Maya 側の標準機能で完結する | 1 ショットにキャラ 1 体までに制限(演出の幅を狭める)/ 2 体目以降を Generic にする(ModelData 側も Generic が要り、Humanoid のリターゲット・Avatar Mask の利点を失う) | §5.1.1(手順を具体化)、§5.4、§5.5、[DesignerManual/cutscene-maya-export.html](DesignerManual/cutscene-maya-export.html) |
| 3 | **AudioListener** をカットシーン中にカメラへ追従させるか | **追従させない** | 1 カメラ上書き方式(§4.6.2)では Listener はカメラに付いたままなので**自動で追従する**。別オブジェクトに Listener を置く構成なら追従しないが、数秒の演出では聞こえ方の差が小さい | Listener を一時的にカメラへ移す: 元の親への戻し・`Camera.main` 差し替え時の付け替えが増え、CameraFx の P1(ノード孤児化)と同種の事故要因になる | §4.6.2(追記なし。本表が記録) |
| 4 | **`LockInput` の受け口** | 6-10a では `CutsceneHandle.IsInputLocked` + `Cutscene.IsInputLocked` / `Cutscene.OnInputLockChanged`(R3)+ EventBus Custom トリガだけ用意し、**実際に入力を止めるのはゲーム側の責務** | 入力系(Action Map 切替・Host 権威での入力破棄・UI フォーカス)はゲームごとに違い、D-Drive が共通 API を作ると MS2026 の入力設計を縛る。Presentation にも入力ロックの概念は無い | D-Drive 側に入力ロックの共通 API(Input System の Action Map を D-Drive が切る等): MS2026 の入力系に依存し、持ち込み先ごとに分岐が要る | §4.5.1(仕様を具体化)、§6、[ProgrammerManual/rules.html](ProgrammerManual/rules.html) |
| 5 | **Cosmetic 時の Skip 権限** | **誰でも Skip でき、全員に効く**(Broadcast)。禁止したい演出は Data の `CutsceneSkip.Disabled` | 「誰が飛ばせるか」はゲームルールで、D-Drive が決めるべきでない。Data で禁止できるので最小の機能で足りる。Cancel と同じ「Broadcast 前に自分だけ飛ばない」規則に乗るだけで実装が増えない | Host のみ / 行為者のみ: ネットの権限判定が D-Drive に入り、ゲームルールを Data 側に固定してしまう | §4.7 |
| 6 | **`StepFps` の既定値** | **0(量子化なし)**。プロジェクト既定は `CutsceneImportProfile.DefaultCameraStepFps` で変更可 | 「Maya の見た目どおり」を既定にし、コマ落ちは意図して付ける値にする。見た目の好みは実データで決めるべきで、既定が 0 なら「何もしていない」ことが自明 | 24 を既定にする: Maya 30fps 元に 24 は整数倍でなく Validation Warning(§5.3)が既定で出てしまう | §4.6.3 |

### 7.3 要検証(6-10c 着手時に Unity 実機で確かめる。設計は変えない)

> **2026-09-18(6-10c)結果**: この環境には Maya 由来の実 FBX(カメラアニメ付き)が無く、Unity MCP は接続できたが
> 「実 FBX を Editor に取り込んで見た目を確認する」検証はできなかった。以下 5 項目は**未検証のまま**、防御的な
> 実装(見つからなければ空/フォールバック、例外にしない)で進めた。実際の Maya 書き出しでの確認はユーザーの
> 今後の作業として残る(実 FBX を `SourceAssets/Cutscene/` に置いて Console の警告と Timeline の中身を見る)。

- Unity の FBX 取り込みがカメラの**画角アニメ**(`field of view` / `focalLength` カーブ)を作るか。作らなければ焦点距離から計算(§5.2) → **未検証**。`CutsceneCameraCurveExtractor` は `"field of view"` の束縛名で抽出を試みる(Unity の Animation ウィンドウの既知の挙動を根拠にした実装。実 FBX での確認はできていない)
- FBX のカメラ属性 **`FocusDistance` / `FStop`** が Unity の Camera に反映されるか。反映しなければ `dd_focusDistance` / `dd_fStop` のカスタムアトリビュート経路(§5.2) → **未検証**。`m_FocalLength`/`m_FocusDistance`/`m_Aperture` 等の候補プロパティ名を順に試し、見つからなければ空カーブ(§4.6.4 の「取れなければ書かない」)にフォールバックする実装のみ
- `OnPostprocessGameObjectWithAnimatedUserProperties` でアニメ付きカスタムアトリビュートが Unity 6 でも取れるか(ロケーターとピント・絞りの両方が依存) → **未検証・不採用**。実 FBX 無しで API の実際の挙動(コールバック形・タイミング)を安全に確認できないため、6-10c ではこの経路(イベント用ロケーター → Signal、`dd_focusDistance`/`dd_fStop`)を実装しなかった。Unity の Timeline ウィンドウで手動でマーカー/カメラ設定を置く運用に留める(§5.5)
- Humanoid + Timeline Animation トラックのオフセットで、Maya のワールド座標と原点(§4.2.1)の合成が期待どおりになるか(ルートモーション / Bake Into Pose の設定) → **未検証**(6-10a の TODO のまま。実キャラ FBX が無いため確認できていない)
- URP の `Volume.weight` を毎フレーム書き換えたときの DoF の追従(Bokeh モードの `focusDistance` 変更にフレーム遅れが無いか) → **未検証**(6-10b 実装済みの `DDriveCutsceneCameraApplier` の範囲。6-10c では変更していない)
- (2026-09-18 追加、§4.6.5 検出 2)`RenderPipelineManager.endCameraRendering` 時点の Camera の Transform / `fieldOfView` が「その描画に使われた姿勢」と一致すること(URP 17.3 で `beginCameraRendering` 内の書き込みが同じフレームの描画に反映されるか、`onBeforeRender` の呼び出し位置)。一致しない経路があれば比較点を `Camera.onPostRender` 相当の別コールバックに変える(契約 G-1〜G-5 は変えない) → **未検証**(6-10b 実装済みの範囲。6-10c では変更していない)
