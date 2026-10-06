# 59. 2026-10-06 自前レビュー結果（修正ラウンド 6 = docs/58 の指摘 GA-R-01〜12 への対応 + 固定値(Constant)の ValueDef の検査の修正）

> **対象**: 2026-10-06 に main へ入った 2 つの PR。どちらも Sonnet のサブエージェントが実装し、まとめ役は差分を読まずにマージした。**v1.4.0 のタグ前の最終確認を兼ねる**。
>
> | マージ | PR | 内容 |
> |---|---|---|
> | `d4a2588` | #113 | 修正ラウンド 6（[58](58_review_round5_p15fix_2026-10-06.md) の対応）: GA-R-01（持ち込み先への「`OnEnable` で `Register` / `OnDisable` で `Unregister`」の案内 6 か所 + docs/02 §8 + スキャナのメッセージ、契約テスト E-9b）・GA-R-02（`SignalLocal` / `ApplySignal` の保護）・GA-R-03（Editor 契約スナップショットのコンストラクタ行）・GA-R-05（`ddriveUpdate` の値はオブジェクト、固定テストを `TryParse` に）・GA-R-06（BOM・実 git テストの隔離）・GA-R-07（プロジェクト全体の Validator 6 つを「アセットに紐付けず 1 回」に）・GA-R-08〜12（検査ウィンドウ・設定画面・提供口の例外の Warning・CHANGELOG・設定ファイル） |
> | `6bf56f7` | #115 | `ValueDefValidator` が `Mode=Constant` のとき Time の欄（Duration の Value <= 0・無限ループ + Duration=0・SpeedScale <= 0）を検査しない。既存テスト 5 件を Parametric に書き換え、`ConstantTimeValidationTests`（全具象 Data 型の新規作成直後・v1.0.0 フィクスチャ）を追加 |
>
> 参考（レビュー対象外。前提として差分の範囲だけ確認）: #114（`69aca4f`）・#116（`a6fc496`）= 開発リポジトリの確認用データ（`Assets/GameData`）と `docs/11` のみ。`Packages/` の変更は 0 件。
>
> **方法**: 専用 worktree を `a6fc496`（detached → 本ブランチ）に合わせ、`git diff d4a2588^1 d4a2588`（34 ファイル）・`git diff 6bf56f7^1 6bf56f7`（7 ファイル）・`git diff 9f40cbb..a6fc496 -- …/Compat/Snapshots/` と、変更後のファイル全体（`ValueDef` / `ValueDef3` / `ValueDefColor` / `TimeDef` / `ValueDefValidator`、**ValueDef を持つ全 Data の欄と、その Time を読む全経路**〔`CameraFxManager` / `HapticsManager` / `BgmManager` / `UiTweenManager` / `UiTweenData` / `UiSlider` / `MaterialManager` / `VfxManager` / `UiInteractable` / `CutsceneCameraClip` / `AnimationClipEditorUtility`（Retiming）/ `CameraFxEditorWindow` / `ValueDefDrawer` と各 Data の Validator〕、`BgmData` / `BgmDataValidator` / `BgmManager.StartLoopBody` / `AudioEditorWindow` / `ImportRuleHandlers`（BGM）、`ConstantTimeValidationTests` / `ValueDefValidatorTests` / `ValidatorSeverityRegistryTests` / `LegacyAssetFixtureTests` / `SerializedLayoutSnapshotBuilder.ConcreteDataTypes`、`IAssetManager` / `GameLoop` / `GameLoopDriver` / `DDriveRuntimeBootstrap`（`Awake` / `OnDestroy` / 実行順）・運用ページのコード例・E-9b、`CI.RunValidation` / `ValidatorRegistry.RunAll` / `DataValidationRunner` と 6 つの Validator の `Validate` 入口、`SpecWebSender`、`PresentationManager`（`SignalLocal` / `ApplySignal` / `FlushPendingUnknownKey`）、`DdriveUpdateDeclaration`（`TryParse` / `Parse` / `ParseVersion`）・`UpdatePreflight`、`EditorContractSnapshotBuilder`、`ForbiddenApiWindow`・2 つの設定画面、`ProjectSettings/DDriveProjectSettings.asset`、`Tools/CI/Summarize-Results.ps1`、`CHANGELOG.md` の `[Unreleased]`）を**読むだけ**で確認した。実装者の報告（[58] の「→ 対応」・[11] の M-4 / 「確認用データの整理」節・CHANGELOG・docs/17 §6）は信用せず、コードと突き合わせた。**Unity は起動しておらず、コンパイル・EditMode / PlayMode テスト・`CI.ValidateAll`・`run-ci.cmd`・実 git は一切実行していない**（対応記録の「green」「Error 0」は未確認）。指摘はコードを読んで確認した事実か、Unity / .NET の挙動についての推定で、推定のものは「確度」欄に**推定**と書いた。
>
> 前提として読んだもの: `CLAUDE.md`（§0）、[docs/12](../12_review.md) §3 / §7、[docs/42](../42_distribution.md) §4.2.1・§5（§5.8・§5.9・§5.14）、[58](58_review_round5_p15fix_2026-10-06.md)（元の指摘と対応記録。書式と重大度の基準）、[docs/17](../17_value_definition.md) §6、[docs/11](../11_tasks.md) M-4 節・「開発リポジトリの確認用データの整理」節、`docs/50_consumer_guide/operation.html`、`CHANGELOG.md` の `[Unreleased]`。

## 総評

- **リリースを止める実バグ（P1）は見つからなかった**。互換面は v1.3.1 から見て**追加のみ**のまま（`git diff 9f40cbb..a6fc496 -- …/Compat/Snapshots/` は 4 ファイル `+131 / −0`、削除・変更行 0 件。本 2 PR で増えたのは `editor-contract.txt` の `ctor` 4 行だけ）。ネットメッセージの形式・送信回数は不変。
- **#115（Constant の Time を検査しない）は方向として正しい**。`ValueDef.Evaluate` は Constant で `t` を使わず、`SpeedScale` は `ResolveNormalizedT` の中でしか使われない（`<= 0` は 1 扱い）ので、SpeedScale の検査を外したことで見逃す不具合は無い。「無限ループ + Duration=0 のフリーズ」を外した実装者の判断も妥当（`ResolveNormalizedT` は尺 0 で t=1 を返し、何も止まらない）。**ただし「Constant なら Time はどこからも読まれない」は成り立たない**: `CameraShakeData.Envelope` と `HapticsData.LowFreq` / `HighFreq` は、`Mode` に関係なく `ValueDef.Duration`（= `Time`）を**演出の寿命**として読む（`CameraFxManager.IsExpired`・`HapticsManager.IsExpired`）。そのため「Constant + 尺 0」の揺れ / 振動は**最初の Tick で消えて何も起きない**のに、以前は出ていた Error が今は何も出ない（**GB-R-01、P2**。下の「Constant の Time を読む経路の調査結果」）。新規作成の既定値（Parametric 0.3 / 0.2 秒）では起きず、デザイナーが尺を 0 にしたかコードで `Constant01()` を入れたときだけ起きる。
- **同じ種類の不具合（ツールで新規作成しただけで Error）は `BgmData` にも残っている**（**GB-R-02、P2**）。`LoopEndSec <= LoopStartSec` の Error は既定の 0 / 0 でも出るが、実行時（`BgmManager.StartLoopBody`）と Audio エディタは 0 / 0 を「クリップ全体をループ」として正しく扱う。BGM の取り込み（音源を置くだけの ImportRule）は `LoopBody` しか設定しないので、**音源を入れた直後の BgmData が必ず Error** になり、持ち込み先の CI（Error で fail）と SpecWeb の Placeholder 判定を汚す。#115 と同じ扱い（Error を減らす方向 = PATCH、§5.8 で自由）で v1.4.0 に入れるのを推奨。
- **GA-R-01〜12 は記録どおり解消**（下の表）。GA-R-01 の運用ページのコード例は、using・名前空間・`IAssetManager` の 5 メンバー・`GameLoop.Register` / `Unregister` の署名と一致し、**Assembly-CSharp（asmdef なし）ならコピーしてそのままコンパイルが通る**（`DDrive.Foundation` / `DDrive.Runtime` は `autoReferenced: true`）。残る穴は「Bootstrap より先に `OnEnable` が走ると黙って登録されないまま」と「`Tick` の中で自分を無効にすると次の Manager がそのフレーム飛ばされる」の 2 点の案内（**GB-R-04、P3**）。
- **GA-R-07 は件数・重さ・コードを変えていない**: 6 つとも `IUniversalValidator` で、`Validate(null, ctx)` を想定した作り（`data` を見ない、または `data == null` で per-asset 部分だけを飛ばす）。`SpecDiffValidator` の 2 通りの呼び出しも、静的キャッシュ `_lastCtx` が同じ context なので全体の指摘は 1 回、Data ごとの指摘は全 Data に従来どおり紐付く。変わったのは報告の**並び順**（全体の指摘が末尾に来る）と表示だけ。テストが「紐付かないこと」を固定できているのは `Code` を持つ 3 種だけ（**GB-R-06、P3**）。
- **GA-R-02 / 05 / 06 は正しい**。保護は `FireTrack` の直後で、受信した Signal の適用順・回数は「自分が止められた後の残りを飛ばす」以外変わらない。`ddriveUpdate` の確定規則・docs/42 §4.2.1・固定テスト（`TryParse` 経由、4 + 6 入力）・`Parse` の互換が一致。
- **未レビューのコードは残っていない**: `9f40cbb..a6fc496` のマージ 31 件のうちコードを含む PR はすべて docs/53〜59 のどれかの対象（下の「レビュー済み / 未レビューの PR の対応表」）。#114 / #116 は `Assets/GameData` と docs のみ。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **0** | – |
| P2（直すべき不具合・設計上の穴） | **2** | GB-R-01・GB-R-02 |
| P3（整理・改善） | **6** | GB-R-03〜08 |

---

## GA-R-01〜12 の解消確認

「解消」= 元の失敗の筋書きが起きなくなり、対応記録が実装と一致し、別の経路を壊していない。行番号は `a6fc496`。

| 指摘 | 判定 | 根拠（ファイル:行） | 補足 |
|---|---|---|---|
| GA-R-01 案内に登録解除・null | **解消**（案内の追記 2 点 = GB-R-04） | `docs/50_consumer_guide/operation.html:105-145`（コード例 `GameCooldownManager`）、`docs/11_tasks.md:386`、`docs/02_core_framework.md:224`、`Documentation~/AGENTS_CONSUMER.md`・`…/references/common-warnings.md`・`docs/12_review.md`・`docs/ProgrammerManual/rules.html`（各 1 か所）、`Editor/Validation/ForbiddenApiScanner.cs:33`（参照先を §8 + 運用ページへ）、`Tests/Runtime/ExternalContract/ExternalContractLoopTests.cs:174-225`・`ExternalPackage/ExternalGameTimeBehaviour.cs` | 下の「GA-R-01 のコード例の確認」。6 か所 + docs/02 + スキャナで「`OnEnable` で登録 / `OnDisable` で解除・null なら何もしない・`IsReady` 不要・`Tick` で例外を出さない・ポーズ中も `Tick` は来る」が一致。パッケージ同梱の `Documentation~/ConsumerGuide/operation.html` と `Documentation~/ProgrammerManual/rules.html` は旧文のまま（`bump-version.ps1` の同期で置き換わる。下の「タグ前に残る作業」4） |
| GA-R-02 `SignalLocal` / `ApplySignal` | **解消** | `Runtime/Presentation/PresentationManager.cs:931-937`・`:999-1004` | 位置は `FireTrack` の直後で `FireDueTracks` と同じ形。ネットへの `Broadcast` は `Signal` の入口（`:902-910`）で済んでいて、受信側の `ApplySignal` は受信 1 回につき 1 回呼ばれるだけ。保留分の適用（`FlushPendingUnknownKey`、`:578-638`）は `!instance.Done` で次の保留を飛ばすので、止められた後に残りの保留 Signal が適用されることもない。テスト 2 件（`PresentationTickReentrancyTests`）は止めた場合・止めない場合の両方を固定 |
| GA-R-03 コンストラクタ行 | **解消** | `Editor/Compat/EditorContractSnapshotBuilder.cs:110-116`、`editor-contract.txt` +4 行 | 引数なしの暗黙コンストラクタは class だけに出る（`CutsceneImportResult` / `CutsceneImportRole` の `ctor()`）。struct（`TextureImportProfile.Rule`・`CameraExecutionOrderExemption`）は暗黙の引数なしを `GetConstructors` が返さないので、出るのは明示の 2 つだけ — スナップショットと一致。行は名前順で各型の先頭に入り、既存行の表記は不変。引数名も行に入る（引数名の変更も検出される = 名前付き引数の互換も守れる） |
| GA-R-04 Q-1 の文面 | **記録どおり**（ユーザーの確認待ち） | `docs/verification/43_manual_verification_2026-09-17.md` 15-5 / 15-27 | コードは変更なし。docs/43 の手順の状態の書き分けは実装と一致 |
| GA-R-05 `ddriveUpdate` 自体の型 | **解消** | `Editor/Update/PackageDependencyChecker.cs:65-110`、`Tests/Editor/Update/DdriveUpdateFormatCompatTests.cs:11-23,129-180`、`docs/42_distribution.md:360,364` | 下の「GA-R-05 / 06 の確認」 |
| GA-R-06 BOM・実 git テスト | **解消**（細部 = GB-R-08） | `PackageDependencyChecker.cs:79-80`、`GitProcess.cs:88`、`P15VerificationFixTests.cs:65-81` | 同上 |
| GA-R-07 全体の Validator | **解消**（テストの弱さ = GB-R-06） | `Editor/Validation/CI.cs:129-188`、`DataValidationSection.cs:188-192` | 下の「GA-R-07 の確認」 |
| GA-R-08 検査ウィンドウ | **解消**（(2)(3) は記録どおり見送り。細部 = GB-R-08） | `Editor/Validation/ForbiddenApiWindow.cs:21-65` | ドメインリロード後の `CreateGUI` は `s_scanOnCreate == false`（static はリロードで初期化）なので走査しない。メニューからの初回は走査する |
| GA-R-09 設定画面 | **解消**（(2) は見送り） | `ForbiddenApiAllowSettingsProvider.cs:21-108`、`CameraExecutionOrderExemptionSettingsProvider.cs:19-97` | 描画のたびの走査ルート解決・全アセンブリの `GetType` は無くなった。`-=` → `+=` で二重購読なし |
| GA-R-10 提供口の例外 | **解消**（(1)(2)(4)(5) は既知の限界として記載） | `CameraExecutionOrderExemptions.cs:173-177` | 既存コード `DD-CAMEXEC-EXEMPT-INVALID`（Warning）の範囲で出る。提供口が無い・例外が無いときの結果は従来と同じ |
| GA-R-11 CHANGELOG | **解消** | `CHANGELOG.md:14-21,65-67,84` | (1)〜(4) とも反映。下の「挙動の変更と CHANGELOG の照合」で 1 件の抜け（GB-R-05） |
| GA-R-12 設定ファイル | **解消** | `ProjectSettings/DDriveProjectSettings.asset:26-27` | 差分は `_forbiddenApiAllowEntries: []` と `_cameraExecutionOrderExemptions: []` の 2 行の追加だけ。既存行の変更なし。`DD-FORBIDDEN-ALLOW-SUMMARY` の旧メニュー案内は記録どおり未対応（害なし） |

---

## Constant の Time を読む経路の調査結果（#115 の観点 (a)）

ValueDef を持つ欄（`grep "public ValueDef"` と `ValueDefValidator` の走査規則〔AssetDataBase の public フィールド・DDrive 名前空間の struct・配列 / List〕で列挙）ごとに、`Time`（`Duration` / `ResolveNormalizedT` / `Time.*` の直接参照）を誰が読むかを調べた。「Constant で Time が意味を持つ」= `Mode=Constant` でも `Time` の値で実行時の挙動が変わる。

| Data / 欄（既定値） | Time を読むもの | Constant で Time が意味を持つか | Constant + 尺 0 の実行時 | #115 後の検査 | 判定 |
|---|---|---|---|---|---|
| `Anim2DData.Retiming`（`Constant01(1)`） | `AnimationClipEditorUtility.BuildRetimingTimes`（Editor）が `Evaluate(u)` だけ | **無** | – | 検査なし（正しい） | 問題なし。Constant だと全フレームが同じ時刻に潰れるのは `IsStrictlyIncreasing` が焼き込み前に弾く（Time とは無関係） |
| `BgmData.FadeIn` / `FadeOut`（Parametric 0.5 / 1.0 秒） | `BgmManager`（`:151` `fadeIn.Duration > 0`、`:231` / `:259-260` `Mathf.Max(Duration, 0.0001)`）がフェードの長さとして読む | **有** | フェードなし（即時）。0 = 「フェードしない」として自然 | 検査なし | 問題なし（0 は正当な意味を持つ） |
| `CameraShakeData.Frequency`（`Constant01(20)`） | `CameraFxManager:453` `EvaluateAt` → Constant は t を使わない | **無** | – | 検査なし（正しい） | 問題なし。#115 の主目的（新規作成の Error）はここ |
| `CameraShakeData.Envelope`（Parametric 0.3 秒） | **`CameraFxManager.IsExpired`（`:227-231`）が `Envelope.Duration` を揺れの寿命として読む**・`:159` / `:445` `EvaluateAt`・`CameraFxEditorWindow:282`（プレビューの尺） | **有** | **最初の Tick で `IsExpired` → 揺れない**（`duration <= 0f` で即時失効） | **検査なし（以前は Error）** | **GB-R-01** |
| `HapticsData.LowFreq` / `HighFreq`（Parametric 0.2 秒） | **`HapticsManager.IsExpired`（`:170-174`）が `Max(Low.Duration, High.Duration)` を振動の寿命として読む**・`HapticsDataValidator:23`（長すぎる警告） | **有** | 両方 Constant + 尺 0 なら**最初の Tick で失効 → 振動しない** | **検査なし（以前は Error）** | **GB-R-01** |
| `MaterialData.Anims[].Value`（既定 struct = Constant） | `MaterialManager:532` `EvaluateAt` | **無** | – | `MaterialDataValidator:171` が Constant を別の Warning（動きません）で扱う | 問題なし |
| `ButtonSkinData` / `SliderSkinData` の `StateVisual.Scale`（`Constant01(1)`） | `UiInteractable:762`・`UiSlider:942` `Evaluate(1f)` だけ | **無** | – | 検査なし（正しい） | 問題なし（#115 の主目的） |
| `UiTweenData.Tracks[].Motion`（既定 struct） | `UiTweenManager.IsTrackFinished`（`:464-476`）・`UiTweenData.TrackEnd`（`:134-148`）が `Motion.Duration`（× `LoopCount`）を完了時刻として読む。形は Constant = 1（`EvaluateShape`、`:616-627`） | **有** | 尺 0 = 即座に終値をセットして完了。尺 X = 終値で X 秒保持してから完了 | 検査なし。ただし `UiTweenDataValidator:30` の Warning（尺 0 + Loop/PingPong）は Mode に関係なく残る | 問題なし（0 は正当な意味。「無限ループで完了しない」は Warning が拾う） |
| `VfxData` のパラメータ `Anim` | `VfxManager:845` が Constant を飛ばす | **無** | – | 検査なし（正しい） | 問題なし |
| `CutsceneCameraClip.BlendIn` / `BlendOut`（`Constant01(1)`） | `CutsceneCameraBehaviour`（`CutsceneCameraClip.cs:159,168`）が `Duration` をブレンドの長さとして読む | **有** | ブレンドなし（既定の意図どおり） | **対象外**（`PlayableAsset` で AssetDataBase ではないので `ValueDefValidator` は走らない） | 問題なし |
| `UiSlider.Response` / `FollowMotion` / `DelayFollowMotion` | `UiSlider:461-465,481-487`・`:511`（`HasDelayFollowMotion` が Constant でも `Time.Value` を読む） | **有**（Tooltip「Mode=Constant,Value=0=即時」= 設計） | 即時 | **対象外**（MonoBehaviour） | 問題なし |
| `ValueDefColor.Alpha`（**D-Drive の Data には使われていない**。Foundation の公開 API） | **`ValueDefColor.EvaluateAt`（`ValueDefColor.cs:23-27`）が `Alpha.ResolveNormalizedT` を Gradient（`Mode=Curve`）の時間軸として使う** | **有**（Alpha が Constant でも、色の Gradient の進み方は Alpha の Time で決まる） | Gradient が常に t=1（終端色のまま） | **検査なし（以前は Error）**。Validator は ValueDefColor について `Alpha` だけを返す（`ValueDefValidator.cs:58-61,88-90`） | **GB-R-03**（持ち込み先が使ったときだけ） |
| `ValueDef3.X/Y/Z` | 各軸が自分の Time。`Uniform` は X だけ | 各軸の Mode どおり | – | 各軸ごと | 問題なし |
| 全欄の `SpeedScale` | `ResolveNormalizedT` だけ（`<= 0` は 1）。寿命・完了の判定（`IsExpired` / `IsTrackFinished` / BGM のフェード）はどれも `SpeedScale` を見ない | – | – | – | **SpeedScale の検査を外したことで見逃す不具合は無い** |

**結論**: Retiming（再生速度ではなく「フレーム位置」のカーブ）を含め、「値は Constant だが別の経路で Time を読む」のは **CameraShake の Envelope・Haptics の 2 モーター・UiTween の Motion・BGM のフェード・（検査対象外の）カットシーンのブレンド / UiSlider・ValueDefColor の Alpha**。このうち「尺 0」が**正当な意味を持たない（= 演出が黙って消える）**のは CameraShake の Envelope と Haptics だけ（GB-R-01）。ValueDefColor は D-Drive 内に使用箇所が無い（GB-R-03）。

---

## GA-R-01 のコード例の確認（運用ページ `operation.html:113-144`）

| 確認項目 | 実コード | 一致 |
|---|---|---|
| `IAssetManager` の全メンバー | `Foundation/Manager/IAssetManager.cs:7-14`: `AssetType Type { get; }` / `Tick(float)` / `OnPause(PauseChannel, bool)` / `StopAll(StopReason)` / `OnSceneUnload()` の 5 つ | ○（5 つとも public で実装、署名一致） |
| using | `AssetType` = `DDrive.Foundation.Identity`、`IAssetManager` / `GameLoop` / `StopReason` = `DDrive.Foundation.Manager`、`PauseChannel` = `DDrive.Foundation.Pause`、`DDriveRuntimeBootstrap` = `DDrive.Runtime.Loop` | ○（5 つの using で過不足なし） |
| `GameLoop.Register` / `Unregister` | `GameLoop.cs:26-34`（public、`IAssetManager` 1 引数） | ○ |
| `Instance` / `Loop` / `GameLoop` | `DDriveRuntimeBootstrap.Instance`（public static get）・`Loop`（`GameLoopDriver`、public get）・`GameLoopDriver.GameLoop`（public get） | ○。`boot == null` / `boot.Loop == null` は Unity の null 比較（破棄済みも null） |
| `AssetType.None` | 実在（`GameLoop` は `Type` を見ない） | ○ |
| アセンブリ参照 | `DDrive.Foundation` / `DDrive.Runtime` の asmdef は `autoReferenced: true` | ○（Assembly-CSharp ならそのまま通る。**持ち込み先が自分の asmdef に置くなら 2 つの参照が要る**が、例には書いていない = GB-R-04） |
| `_loop` を覚えて解除 | 終了時に `Instance` が先に null になっても（`DDriveRuntimeBootstrap.OnDestroy:254-263`）、覚えた `GameLoop` から外せる | ○（案内の主張どおり） |

**実行順の罠の確認**:

- **同じシーンの初期オブジェクト**: Bootstrap は `[DefaultExecutionOrder(-1000)]`（`DDriveRuntimeBootstrap.cs:36`）。同時にロードされるオブジェクトの `Awake` / `OnEnable` は実行順の順に呼ばれるので（Unity の仕様。**推定**ではなく既存の [26] / [58] でも前提にしている挙動）、既定の実行順のスクリプトの `OnEnable` では `Instance` と `Loop` が揃っている。○
- **`[DefaultExecutionOrder]` が -1000 より小さいスクリプト・Bootstrap が後からロードされるシーン構成・`KeepAcrossScenes = false` で Bootstrap が作り直された後もゲーム側が `DontDestroyOnLoad` で生き残る構成**: `OnEnable` は 1 回しか来ないので、**黙って登録されないまま**（`Tick` が来ない）。案内は「起動前は登録しない」とだけ書き、「そのままでは後から登録されない」ことと、気づく手段（警告 1 行・`Start` での再試行）を書いていない → GB-R-04。
- **ドメインリロード無効**: `Instance` は Bootstrap の `OnDestroy` で null に戻り、`GameLoop` は `GameLoopDriver` ごとのインスタンスなので、Play の度に作り直される。ゲーム側も `OnDisable` で解除するので問題なし。○
- **Host 引き継ぎ・`ResetNetworkedState`**: `GameLoop` / `GameLoopDriver` は作り直されない（各 Manager の状態だけを捨てる）。登録はそのまま有効。○
- **`Tick` 中の `Unregister`**: `GameLoop.Tick` は `for (i < _managers.Count)` で `List.Remove` に対する添字の補正が無い（`GameLoop.cs:36-42`）。`Tick` の中で自分（または前にいる Manager）を `SetActive(false)` / `enabled = false` にすると `OnDisable` → `Unregister` が即座に走り、**直後の 1 つの Manager がそのフレームだけ `Tick` されない**（例外は出ない。`Destroy` は遅延するので起きない）。`Register` は末尾追加なので同じフレームに `Tick` される。案内には無い → GB-R-04。D-Drive 自身の Manager は先に登録されていて自分を外さないので影響しない。
- **E-9b の検証範囲**: Bootstrap が無いと登録しない・`Tick` で `dt` が届く・HitStop 静止で `dt = 0`・スローで `unscaledDeltaTime × 0.5`・破棄（`DestroyImmediate` → `OnDisable`）後は `Tick` されない、を固定している。案内の主張のうち**元の失敗の筋書き（シーン遷移で破棄されたのに `Tick` される）そのもの**はシーンのロードを使わず `DestroyImmediate` で代替しているが、`OnDisable` の経路は同じなので実効はある。`orphan` の確認は他のテストが Bootstrap を残していると落ちる（テスト順序への依存。既存の E-9 と同じ前提）。

## GA-R-05 / 06 の確認

- **確定規則と実装**: `TryParse`（`PackageDependencyChecker.cs:65-110`）は、空・空白 → false、壊れた JSON・根が配列 → false、`ddriveUpdate` 無し・`null` → true + `Empty`、オブジェクトでない値（文字列・配列・数値・真偽）→ false + `Unreadable`、オブジェクトなら `requires` / `compatibleWith` の中の文字列だけを読む（それ以外は黙って無視）。docs/42 §4.2.1 規則 3（`:360`）と最後の行（`:364`）の記述と一致。`Parse` は `Unreadable` → `Empty` に丸める（従来の互換）。
- **固定テスト**: `Pkg` ヘルパーが `TryParse` を通すようになり（`DdriveUpdateFormatCompatTests.cs:13-23`）、4 入力 → 読めない + `DD-PKGDEP-BAD-DECLARATION`（Warning）、6 入力 → 読めた + 空、旧 `Parse` の互換、BOM を固定。後で誰かが `TryParse` を「空」に戻すとテストが赤になる。○
- **旧版（v1.4.0）が将来の宣言を読んだとき**: `ddriveUpdate` の値の型を広げない（規則 3・6）ので、将来の拡張（オブジェクトの中の新キー・別のトップレベルキー）は v1.4.0 では黙って無視される。将来の版が規則に反して `ddriveUpdate` を配列等にした場合だけ v1.4.0 が Warning（`BAD-DECLARATION`）を出す = 安全側。十分。
- **BOM**: `TryParse` の先頭で `TrimStart('﻿')`。同じ `fetchedText` を読む `ParseVersion`（`:130-140`、`UpdatePreflight.cs:76`）は BOM を取らないが、読めなければタグの版にフォールバックする（`:77`）ので結果は変わらない（GB-R-08 の細部）。
- **実 git テスト**: `init` / `add` / `commit` に `user.name` / `user.email` / `commit.gpgsign=false` / `tag.gpgsign=false` / `core.autocrlf=false` / `core.hooksPath=<存在しないフォルダ>` / `init.templateDir=` を `-c` で渡し、`commit` は `--no-verify`。準備の失敗は `Assume`（Inconclusive）、後始末は従来どおり `finally`。git が無ければ従来どおり `Assume`。検査本体の `git show`（`GitArguments.ShowPackageJson`）は `-c` を付けないが、読み取りだけなのでフック・署名の影響を受けない。○

## GA-R-07 の確認

| 観点 | 確認結果 |
|---|---|
| 6 つが `IUniversalValidator` か | すべて `IUniversalValidator`（`CI.RunValidation` の `if (validator is not IUniversalValidator) continue;` で黙って落ちるものは無い） |
| `asset == null` を想定した作りか | `PackageDependencyValidator` / `ProjectSetupValidator` / `ContentHashCatalogCoverageValidator` / `CatalogAddressCoverageValidator` / `CameraExecutionOrderValidator` は `data` を一切読まない（context のガード + `ctx` だけ）。`SpecDiffValidator` は `data == null` で per-asset 部分だけを飛ばす（`:61`）。null 参照例外・早期 return で何も出さなくなる経路は無い |
| context のキャッシュ | 全体用の context は `RunAll` の context とは別（`CI.cs:152`）。6 つは `RunAll` に登録されないので、`RunAll` 側の context で 2 回目が走ることは無い。`SpecDiffValidator` の静的キャッシュ（`_lastCtx`）は null の呼び出しで更新され、続く各 Data の呼び出しは同じ context なので全体の指摘を繰り返さない |
| `SpecDiffValidator` の件数 | 旧: `RunAll` の最初の Data の呼び出しで「全体 + その Data の分」、以降の Data は「その Data の分」。新: null で「全体」、各 Data で「その Data の分」。**件数は同じ**、全体の指摘の紐付け先だけが変わる。`assets` に null は入らない（`LoadAllAssetDataAssets` が除外、`CI.cs:254`）が、入っても `data == null` で何も出ない |
| Data が 0 件 | 旧: `RunAll` が universal を null で 1 回ずつ。新: 6 つは別経路で null 1 回、残りは `RunAll` が従来どおり。件数同じ |
| `includeProjectWideValidators: false`（SpecWeb） | `IsProjectWide` の 5 つは従来どおり除外。`ProjectSetupValidator` は asset = null で出るようになったが Warning だけ（12 か所すべて `ValidationResult.Warning`）で、SpecWeb は Error かつ asset 付きだけを見る（`SpecWebSender.cs:153`）ので判定は変わらない |
| 個別検証（Inspector の「検証」節） | `DataValidationRunner.Run` は変更なし（`IsProjectWide` の 5 つを除外、`ProjectSetupValidator` は従来どおり出る） |
| `UpdateStepsFactory` の Validation 段 | `CI.RunValidation()` の件数を数えるだけ（`UpdateStepsFactory.cs:97`）。件数不変 |
| `DD-SETUP-*` を読む他のコード | コードで `DD-SETUP-*` の結果を読む箇所は無い（定義と Fix アクションだけ） |
| JUnit の classname | `(project)` に変わる。`Tools/CI/Summarize-Results.ps1:71-73` は表示に使うだけ、`run-ci.cmd` / `check-release.ps1` は classname を見ない |
| 並び順 | 全体の指摘は報告の**末尾**に移った（旧: 最初の Data の結果の中）。件数・重さ・コードは不変。並びに依存するコードは無い |

**件数・重さ・コードは変わっていない**（コード読みで確認。実装者の「新旧を並べた件数比較はしていない」は事実で、本レビューも実行での比較はしていない）。

---

## P2 — 直すべき不具合・設計上の穴

### GB-R-01. 【#115】CameraShake の Envelope と Haptics のモーターは Constant でも Time を「寿命」として読む。Constant + 尺 0 で演出が黙って消えるのに、以前の Error が出なくなった

- **場所**: `Foundation/Validation/ValueDefValidator.cs:129-163`（`usesTime = def.Mode != ValueMode.Constant`）、`Runtime/Camera/CameraFxManager.cs:227-231`（`IsExpired` = `Envelope.Duration <= 0 || Elapsed >= duration`）、`Runtime/Haptics/HapticsManager.cs:170-174`（`Max(LowFreq.Duration, HighFreq.Duration)`）、`Runtime/Camera/CameraShakeDataValidator.cs:20-51`・`Runtime/Haptics/HapticsDataValidator.cs:23-27`（尺 0 の検査なし）
- **何が問題か**: 2 つの Manager は `ValueDef.Duration`（= `Time` から求めた尺）を `Mode` に関係なく演出の寿命として使う。`Envelope` を Constant にした「一定の強さで N 秒揺らす」、Haptics の「一定の強さで N 秒振動」は正当な使い方（`ValueDefDrawer` は Constant でも Time の欄を出している）で、そこで尺が 0 だと**最初の Tick で失効して何も起きない**。#115 以前はこの形が「TimeMode=Duration ですが Value が 0 以下です」の Error で見つかっていたが、今は何も出ない。実装者の報告「Constant の `Evaluate` は `Time` を使わないため実害は検査のみ」は `Evaluate` についてだけ正しく、寿命の判定には当てはまらない。
- **失敗の筋書き**: プログラマーがコードで `shake.Envelope = ValueDef.Constant01(1f)`（Time は既定 0）と書く、またはデザイナーが Envelope を Constant にして尺を 0 にする → Validation は Error 0 → 実機で揺れない / 振動しない。Haptics は 2 モーターのどちらかに尺があれば起きない。新規作成の既定値（Parametric 0.3 / 0.2 秒）では起きない。
- **直し方の案**: `ValueDefValidator` は今の形（Constant では Time を見ない）のままにし、**種別の Validator に「尺が 0 以下で再生されない」Warning を足す**（`CameraShakeDataValidator`: `shake.Envelope.Duration <= 0f` / `HapticsDataValidator`: `Max(LowFreq.Duration, HighFreq.Duration) <= 0f`）。Mode を問わず `Duration` で判定すれば、TimeMode=Speed / Rate で Value=0 の場合（以前から未検出）も拾える。新しい検査は Warning（[42] §5.8 の「新しい検査は Warning 始まり」）なので MINOR の範囲でタグ前に入れられる（Code を付けるなら `validator-severity.txt` に行が増えるだけ）。
- **確度**: 確認済み（コード読み）。新規作成の既定値では起きないことも確認済み
- → **対応（2026-10-06 修正ラウンド 7、b4c9624）**: `ValueDefValidator` は #115 のまま（戻さない）。種別の Validator に **新規コード**の Warning を足した。`CameraShakeDataValidator`: `DD-SHAKE-ENVELOPE-ZERO-DURATION`（`Envelope.Duration <= 0`。「Envelope の尺(Duration)が 0 以下のため、再生してもすぐ終わり何も起きません」）、`HapticsDataValidator`: `DD-HAPTICS-ZERO-DURATION`（`Max(LowFreq.Duration, HighFreq.Duration) <= 0`）。判定は Mode に関係なく `Duration`（TimeMode=Speed / Rate で Value=0 も拾う）。**二重に出さない**: 「Time を使うモード（Constant 以外）+ `TimeMode=Duration` + Value <= 0」は `ValueDefValidator` の Error が既に出るので、その形のときは Warning を出さない（Haptics は 2 モーターのどちらかがその形なら出さない）。新規作成直後の既定値（Parametric 0.3 / 0.2 秒）では出ない。重さは Warning（[42] §5.8）。v1.3.1 ではこの形は Error だったので「Error → Warning に下がる」ことを CHANGELOG の挙動の変更に明記。Code は `private const`（公開 API を増やさない）。**他の「Time を寿命 / 完了時刻として読む」欄の確認（実コード）**: `BgmData.FadeIn` / `FadeOut`（`BgmManager` の `Duration > 0` / `Max(Duration, 0.0001)`）= 尺 0 は「フェードしない」で正当、`UiTweenData.Tracks[].Motion`（`UiTweenManager.IsTrackFinished` = `trackElapsed >= Motion.Duration`）= 尺 0 は「即座に終値をセットして完了」で正当（無限ループで完了しない形は `UiTweenDataValidator` の Warning が Mode を問わず残る）→ **「Constant + 尺 0 で黙って無効になる」形は成立しないので Warning は足さない**。テスト: `CameraShakeDataValidatorTests`（Constant + 尺 0 = Warning のみ・Error なし / Constant + 尺あり = 何も出ない / Parametric + Duration 0 = Warning なし（`ValueDefValidator` が Error）/ Speed + Value 0 = Warning）、`HapticsDataValidatorTests`（両モーター Constant + 尺 0 = Warning のみ / 片方に尺あり = 何も出ない / Parametric + Duration 0 = Warning なし）。

### GB-R-02. 【#115 と同じ種類】新規作成・取り込み直後の BgmData が必ず「LoopEndSec が LoopStartSec 以下です」の Error になる（実行時は 0 / 0 を全体ループとして正しく扱う）

- **場所**: `Runtime/Audio/BgmDataValidator.cs:29-32`（`LoopEndSec <= LoopStartSec` → Error）、`Runtime/Audio/BgmData.cs:25-29`（既定 0 / 0。Tooltip「LoopBody の長さ以上なら『クリップ全体をループ』扱い」）、`Runtime/Audio/BgmManager.cs:299-318`（`LoopEndSec > LoopStartSec` でなければ終端 = クリップ末尾）、`Editor/Audio/AudioEditorWindow.cs:275`（同じく end <= start はクリップ長）、`Editor/Import/ImportRuleHandlers.cs:49-52`（BGM の取り込みは `LoopBody` だけを設定）
- **何が問題か**: 実行時（`StartLoopBody`）は 0 / 0 を「0 秒からクリップ末尾までのループ」として扱い（`endSample = LoopBody.samples`）、`LoopStartSec > 0` + `LoopEndSec = 0` も「開始位置から末尾まで」として扱う。Audio エディタの波形表示も同じ。つまり **`LoopEndSec = 0` は「末尾まで」の意味で正しく動く**のに、Validator だけがそれを Error にする。音源を `SourceAssets/Bgm/` に置くだけの取り込みは `LoopBody` しか設定しないので、**音源を入れた直後の BgmData が必ず Error 1 件**を持つ（`LoopBody` 未設定の Error と違い、設定しても消えない）。#115 と同じ「ツールで作っただけで Error」で、v1.0.0 から。実装者は [11] に「参考。直さない」と記録している。
- **失敗の筋書き**: 持ち込み先（MS2026）で BGM を 1 曲取り込む → `CI.ValidateAll` が Error で fail（[42] §5.8 のとおり持ち込み先の CI は「Error があれば fail」）。SpecWeb の送信で `isPlaceholder = true`（`SpecWebSender.cs:146-171`）となり、Web 側で「インポート済」に進めない。開発リポジトリでは #114 で `BGM_Title_Test` の `LoopEndSec` をクリップ長に直して回避しただけ。
- **直し方の案**: 条件を `bgm.LoopEndSec > 0 && bgm.LoopEndSec <= bgm.LoopStartSec` に絞る（0 は「末尾まで」）。Error が減る方向だけの変更で [42] §5.8 の PATCH（自由）。#115 と同じく CHANGELOG の互換性節（挙動の変更・PATCH 相当）と修正節に 1 行、`BgmDataValidator` のテストに 0 / 0・Start>0 / End=0 → Error なし、End>0 かつ End<=Start → Error を足す。v1.4.0 に入れるのを推奨（タグを止める理由ではない）。
- **確度**: 確認済み（コード読み。実行時の 0 / 0 の再生結果は実機未確認だが、分岐は明確）
- → **対応（2026-10-06 修正ラウンド 7、652df75）**: Error の条件を実行時（`BgmManager.StartLoopBody`）の意味に合わせた。実効の終端 = `LoopEndSec > 0` ならそれ、0 以下なら `LoopBody.length`（`LoopBody` が null なら不明）。**実効の終端が確定していて `LoopStartSec` 以下のときだけ Error**（メッセージ・重さ・コードは従来のまま）。各組み合わせの実行時の扱いと検査:

| LoopStartSec / LoopEndSec | 実行時（`StartLoopBody`） | 検査 |
|---|---|---|
| 0 / 0（既定。取り込み直後） | `endSample = samples` → クリップ全体をループ | Error なし（以前は Error） |
| Start > 0 / End = 0（Start がクリップ内） | `endSample = samples` → Start から末尾までをループ | Error なし（以前は Error） |
| Start >= クリップ長 / End = 0 | `startSample` が `samples - 1` に丸められ、1 サンプルのループになる（破綻） | **Error のまま**（実効の終端 = クリップ長 <= Start） |
| End > 0 かつ End <= Start（例 5.0 / 2.0、2.0 / 2.0） | End が無視され Start から末尾までになる（指定と違う範囲） | **Error のまま** |
| End > クリップ長（Start < クリップ長） | `endSample` を `samples` に丸める | Error なし（従来も Error ではない） |
| `LoopBody` 未設定 / End = 0 / Start > 0 | クリップが無く鳴らない | この検査は出さない（`LoopBody` 未設定の Error が別にある） |

テスト（`BgmDataValidatorTests`）: `LoopBody` だけ設定した BgmData が Error 0・Start>0 / End=0 が Error なし・Start >= クリップ長 / End=0 が Error・End がクリップ長超過が Error なし・End == Start > 0 が Error。`FreshlyCreatedData_OfEveryConcreteType_…`（GB-R-07 の直し後は `ValueDefValidator` の Error 0 を見る）に加え、全具象 Data 型の新規作成直後に `DataValidationRunner` を回して Error を一覧すると（`execute_code`）、残るのは「未設定」系（ModelData・CanvasData・VfxData・PrefabData の Prefab、BgmData の `LoopBody`、SeData・AnimData の Clip、TextureData の Texture、Anim2DData の Clip 未生成、AnimData の StateName）だけで、**値の範囲系の Error は 0**（Anim2D / Skin / CameraShake / BGM とも）。`BGM_Title_Test` は #114 のままで触っていない。

---

## P3 — 整理・改善

### GB-R-03. 【#115】`ValueDefColor` の Alpha は、Constant でも Gradient の時間軸になる。Constant + 尺 0 で色が終端のまま止まるのに検査されなくなった（D-Drive 内に使用箇所は無い）

- **場所**: `Foundation/ValueDef/ValueDefColor.cs:23-27`（`EvaluateAt` が `Alpha.ResolveNormalizedT` を色全体の t に使う）、`ValueDefValidator.cs:58-61,88-90`（ValueDefColor について `Alpha` だけを返す）
- **何が問題か**: `ValueDefColor { Mode = Curve, Curve = <Gradient>, Alpha = Constant01(1) }`（色は変えたいが透明度は一定）は自然な書き方だが、Alpha の Time が 0 だと Gradient が常に t=1。以前は Alpha の Error で見つかった。D-Drive の Data には ValueDefColor の欄が無い（grep 0 件）ので D-Drive 自身は影響を受けず、Foundation の公開型を持ち込み先が自分の Data に使ったときだけ起きる。
- **直し方の案**: `ValueDefValidator` で ValueDefColor の Alpha を検査するとき、`ValueDefColor.Mode == Curve` なら Alpha を「Time を使う」扱いにする（`CheckValueDef` に `forceUsesTime` 引数）。Error を戻す方向だが、v1.4.0 のタグ前なら v1.3.1 と同じ重さに戻すだけ。またはタグ後に Warning で。
- **確度**: 確認済み（コード読み）
- → **見送り（2026-10-06 修正ラウンド 7）**: 指示どおり検査は足さない（D-Drive の Data に `ValueDefColor` の欄が無い）。`docs/17` §6 に「Constant では Time を検査しない。寿命として Time を読む種別は種別側の Validator が見る」と書いた。`ValueDefColor.Alpha` が Constant でも Gradient の時間軸として Time を使う点は、持ち込み先が使ったときの注意として `docs/17` に 1 行。

### GB-R-04. 【GA-R-01】案内に「Bootstrap より先に `OnEnable` が走ると黙って登録されない」「`Tick` の中で自分を無効にすると次の Manager がそのフレーム飛ばされる」「asmdef の参照」が無い

- **場所**: `docs/50_consumer_guide/operation.html:105-144`（コード例）、`Foundation/Manager/GameLoop.cs:34-42`（`Unregister` = `List.Remove`、`Tick` は添字の補正なし）
- **何が問題か**: (1) コード例は `Instance` が null なら `return` するだけで、後から登録し直す手段も知らせる手段も無い。`[DefaultExecutionOrder(-2000)]` のゲーム側スクリプト・Bootstrap を後からロードする構成・Bootstrap の作り直しで、`Tick` が一度も来ないまま気づけない（例外も警告も出ない）。(2) `Tick` 中に `OnDisable` が走ると（`SetActive(false)` / `enabled = false`）、`List.Remove` で後ろが詰まり、直後の 1 つがそのフレーム飛ばされる。(3) 持ち込み先が asmdef を使う場合は `DDrive.Foundation` / `DDrive.Runtime` の参照が要る（MS2026 は現状 Assembly-CSharp だが、[49] では asmdef の有無で `DDrive.Generated.asmdef` を判断している）。
- **直し方の案（コード変更なし）**: 運用ページの例の `return` の前に `Debug.LogWarning("… Bootstrap が無いので Tick に登録しません")` を 1 行入れる（または「起動順が Bootstrap より前になりうるなら `Start` でも登録を試す」と 1 文）。「`Tick` の中で自分を無効にしない（必要なら次のフレームに回す）」と「asmdef なら 2 つを参照」を 1 文ずつ。D-Drive 側で `GameLoop` を写しの走査にする（`PresentationManager` / `CutsceneManager` と同じ形）のは挙動の変更なので M-5（案）と一緒に。
- **確度**: (1)(3) 確認済み、(2) 確認済み（コード読み。`OnDisable` が即時に呼ばれるのは Unity の仕様）
- → **対応（2026-10-06 修正ラウンド 7、d95cf10）**: (1) 起動順: コード例を `OnEnable` + `Start` の再試行 + 警告ログに変更（`TryRegister()` を共通化。`Register` は `Contains` で二重登録を防ぐので `OnEnable` と `Start` で 2 回呼んでも 1 回扱い）。成立は実コードで確認: Bootstrap は `[DefaultExecutionOrder(-1000)]` の `Awake`、`OnEnable` は実行順の小さいスクリプトでは先に走り `Instance` が null、`Start` は同じシーンの全 `Awake` / `OnEnable` の後。**Bootstrap が後からロードされるシーン構成は `Start` でも間に合わない**ので警告ログで気づけるようにしている（案内に明記）。E-9b の外部 Manager（`ExternalGameTimeBehaviour`）も同じ形に変え、テストに「Bootstrap の `Awake` より後に `Start` が走ると登録される」「`Start` でも Bootstrap が無ければ警告 1 行」を追加。(2) `Tick` 中の `Unregister`: **事実を確認した**。`GameLoop.Tick` は `for (i = 0; i < _managers.Count; i++) _managers[i].Tick(dt)`（`GameLoop.cs:36-42`）で、`Unregister` は `List.Remove`。自分または前にいる Manager を外すと後ろが詰まり、直後の 1 つがそのフレームだけ `Tick` されない（後ろの Manager を外す場合は飛ばない）。`BroadcastPause` / `StopAll` / `NotifySceneUnload` も同じ形。例外は出ない。案内に「`Tick` の中で `Unregister` / 無効化しない。フラグを立てて次のフレームの頭か `LateUpdate` で外す」を追記。**`GameLoop` 自体は直さない**（Foundation の挙動変更になるため。リリース後のチケット候補として docs/60 §6 に 1 行）。(3) asmdef: `DDrive.Foundation` と `DDrive.Runtime` への参照が要る（asmdef なしの `Assembly-CSharp` なら不要）を追記。追記先: 運用ページ（コード例も差し替え）・docs/11 M-4 節の返答文・消費側スキル `common-warnings.md`・`AGENTS_CONSUMER.md`・docs/12 §3・ProgrammerManual `rules.html`・docs/60 第 4 章。`Documentation~/ConsumerGuide` と `Documentation~/ProgrammerManual` は `bump-version.ps1` の同期で置き換わる（手では編集していない）。

### GB-R-05. 【#115】CHANGELOG に SpecWeb の Placeholder 判定への影響が無い・`ValueDefValidatorTests` の残りの細部

- **場所**: `CHANGELOG.md:14,89`、`Editor/Spec/SpecWebSender.cs:146-171,518-530`、`ValueDefValidator.cs:155-158`
- **細部**: (1) SpecWeb は「Error があるアセット = Placeholder」で送る。#115 で、Time の Error だけを持っていた Anim2D / Skin / CameraShake（持ち込み先で確認用データを直していないもの）は `isPlaceholder = false` に変わり、SpecDiff の「まだ Placeholder のようです」Warning も消える。正しい方向の変化だが、CHANGELOG の互換性節には「CI の Error が減る」しか無い。「SpecWeb への送信で Placeholder 扱いでなくなる Data がある」を 1 語足すと SpecWeb の担当者に伝わる。(2) `TimeMode=Rate なのに Loop=Once` の Warning（`:155-158`）は Constant でも出る（Time を使わないのに）。#115 の考え方に合わせるなら `usesTime` で絞る（Warning を減らす変更 = 自由）。実害は小さい。
- **確度**: 確認済み
- → **対応（2026-10-06 修正ラウンド 7、本 PR の docs コミット）**: (1) 実コードで事実を確認した。`SpecWebSender.FindAssetPathsWithValidationErrors` は `CI.RunValidation(includeProjectWideValidators: false)` の Error を持つアセットのパスを集め、それが `isPlaceholder = true` になる。`assetParams` の items は `isPlaceholder = false` のものだけ。`SpecDiffValidator` も同じ判定を使って「インポート済なのにまだ Placeholder のようです」Warning を出す。よって #115 と GB-R-02 で Error が減ると、**原因が ValueDef の Time（Constant）か BGM の `LoopEndSec` だけだった Data は Placeholder でなくなり、`assetParams` にも載り、上の Warning も出なくなる**。CHANGELOG の「挙動の変更」に 1 項を追加。(2) `TimeMode=Rate なのに Loop=Once` の Warning を `usesTime` で絞る件は、Warning を減らす方向だが指摘に「実害は小さい」とあり、今回のラウンドは指示の範囲に絞って**見送り**（次の機会に）。

### GB-R-06. 【GA-R-07】テストが「紐付かないこと」を固定できているのは Code のある 3 種だけ・名前での判定

- **場所**: `Tests/Editor/Update/P15VerificationFixTests.cs:388-433`、`DataValidationSection.cs:161-173,183-192`
- **細部**: (1) `RunValidation_ProjectScopedFindings_…` は `DD-SETUP-` / `DD-CAMEXEC` / `DD-PKGDEP-` のコードで「全体の指摘」を見分けるが、`SpecDiffValidator`（全体の指摘）・`ContentHashCatalogCoverageValidator`・`CatalogAddressCoverageValidator`・`CameraExecutionOrderValidator` の G-1 Warning は Code を持たないので、これらが Data に紐付いても赤にならない。`SpecDiffValidator` の Data ごとの指摘が今も Data に紐付くこと、新旧で件数が同じことのテストも無い。偽の `SpecDiffValidator`（`RepoRootOverride` / `TuningTableOverride`）で全体 1 件 + Data ごと 1 件を作り、`Asset == null` が 1 件・Data 付きが 1 件、を固定すると安い。(2) 判定は型の**単純名**（`GetType().Name`）なので、持ち込み先が同じ名前の別の Validator（例 `MyGame.SpecDiffValidator`）を書くと、`IUniversalValidator` でなければ `Run All` で**黙って実行されなくなる**（`CI.cs:157-160` の `continue`）。`typeof(…)` での比較にするか、`IUniversalValidator` でないものは対象外にする（まれ）。
- **確度**: 確認済み（コード読み）
- → **対応（2026-10-06 修正ラウンド 7、3676f12）**: (2) 判定を型の単純名から**型そのもの**に変えた（`DataValidationRunner` の `ProjectWideValidatorNames`（`HashSet<string>`）→ `ProjectWideValidatorTypes`（`HashSet<Type>`）。5 つとも `DDrive.Editor` 内の型なので `typeof` で書ける。`IsProjectWide` / `IsProjectScopedInRunAll` / 個別検証の発見の 3 経路が同じ集合を使う。公開シグネチャは不変）。持ち込み先が同じ名前の別の Validator を書いても取り違えない（Run All から黙って落ちることも、個別検証から外れることもない）。本体コードの変更はこの 1 か所だけ（指摘 (2) への直接の対応で最小）。(1) テスト: `SameNamedValidatorsFromOtherNamespaces_AreNotTreatedAsProjectScoped`（同名の偽 Validator が全体用として扱われない）、`EveryProjectScopedValidator_IsAnIUniversalValidator_SoRunAllNeverDropsIt`（6 つが `IUniversalValidator`）、`RunValidation_GlobalFindingsOfCodelessValidators_AreNotAttachedToAnyAsset`（Code を持たない 3 つ〔ContentHash・CatalogAddress・CameraExecutionOrder〕の全体の指摘を空の context で出し、同じメッセージが Run All で Data に紐付かないことを見る。context が違えばメッセージも違いうるため一致したものだけを見る弱い確認）。**`SpecDiffValidator` の Data ごとの指摘が従来どおり Data に紐付くことの専用テスト（偽の `RepoRootOverride` / `TuningTableOverride`）は、仕様書スナップショットの JSON を組み立てる必要があり今回は見送り**（型の判定と `CI.RunValidation` の分岐は変えていない）。

### GB-R-07. 【#115】`ConstantTimeValidationTests` の壊れやすさ（持ち込み先で有効にしたとき）

- **場所**: `Tests/Editor/ConstantTimeValidationTests.cs:20-22,30,46,75`
- **細部**: (1) 型の列挙は `SerializedLayoutSnapshotBuilder.ConcreteDataTypes()`（`DDrive.*` のアセンブリだけ・`DDrive.Tests*` と抽象型を除く）なので、持ち込み先固有の Data 型・テスト用 Data（`HolderData` / `TestAssetData`）・外部契約のダミー（`ExternalContract.*`）は入らない。○ (2) 一方で検査は `DataValidationRunner.Run` = **持ち込み先の `IValidator` も含む全 Validator** なので、持ち込み先の Validator が D-Drive の型に「SpeedScale」「Duration=0」「TimeMode=Duration」を含む Error を出す、または `Debug.LogError` を出すと（Unity Test Framework は想定外の Error ログで失敗にする）このテストが落ちる。目的は ValueDef の検査なので、`new ValueDefValidator().Validate(...)` だけを呼ぶ方が狙いに合い、壊れにくい（2 つ目のテストはそうしている）。(3) Error の見分けがメッセージの部分一致（`IsTimeError`）なので、文言を変えると黙って通る。(4) フィクスチャのパス `Packages/com.ddrive.core/…` を `Directory.GetFiles` で読むのは既存の `LegacyAssetFixtureTests` と同じ形（P-11 / P-12 の持ち込み先の実行でも同じテストは落ちていない記録なので問題ない見込み = **推定**）。
- **確度**: (1)(3) 確認済み、(2) 推定（持ち込み先の Validator 次第）、(4) 推定
- → **対応（2026-10-06 修正ラウンド 7、3676f12）**: (1) 型の列挙は D-Drive の Data 型だけ（`ConcreteDataTypes`。変更なし）。(2)(3) 検査を `DataValidationRunner.Run`（持ち込み先の Validator 込み）から `new ValueDefValidator().Validate(...)` だけに変え、判定を「メッセージの部分一致」から「**`ValueDefValidator` が Error を 1 件も返さないこと**」に変えた（テスト名 `FreshlyCreatedData_OfEveryConcreteType_HasNoValueDefError`。持ち込み先の Validator・`Debug.LogError` に左右されない。文言を変えても黙って通らない）。「他の Error（参考）」の出力は削除（一覧が必要なときは `execute_code` で取れる = GB-R-02 の確認で実施）。(4) 推定のまま（`LegacyAssetFixtureTests` と同じ形）。

### GB-R-08. 細部（検査ウィンドウの初回の二重走査・`ParseVersion` の BOM・E-9b の順序依存）

- **`ForbiddenApiWindow.Open`**（`ForbiddenApiWindow.cs:25-37`）: `GetWindow` の中で `CreateGUI` が同期的に呼ばれる場合、初回は `CreateGUI` が走査したうえで `window._body != null` の分岐でもう一度 `Rescan()` する（全 `.cs` を 2 回読む）。`CreateGUI` が遅れて呼ばれる場合は 1 回。どちらでも結果は正しい（**推定**: Unity 6 の `CreateGUI` の呼ばれる時機による）。直すなら `Open` で「`CreateGUI` 済みだったか」を `GetWindow` の**前**に `HasOpenInstances<ForbiddenApiWindow>()` で調べる。
- **`ParseVersion`**（`PackageDependencyChecker.cs:130-140`）: BOM を取り除かない。読めなければタグの版にフォールバックするので結果は変わらない（揃えるなら同じ `TrimStart`）。
- **E-9b**（`ExternalContractLoopTests.cs:177-181`）: 最初の「Bootstrap が無ければ登録しない」は、前のテストが Bootstrap を残していると失敗する（既存の E-9 の後始末に依存）。
- **確度**: 1 つ目は推定、他は確認済み
- → **対応（2026-10-06 修正ラウンド 7、8a57324）**: (a) `ForbiddenApiWindow.Open`: `GetWindow` の**前**に `HasOpenInstances<ForbiddenApiWindow>()` で既に開いていたかを調べ、**既に開いていたときだけ**再走査する形にした（初めて開くときは `CreateGUI` が 1 回だけ走査する。`CreateGUI` が `GetWindow` の中で同期的に呼ばれても後から呼ばれても 1 回）。**Unity 6 の `CreateGUI` の時機は推定のまま**で、自動テストは無い（EditorWindow の生成時機に依存するため）。どちらの時機でも結果は正しく、走査回数が減るだけの安全な変更。(b) `ParseVersion` に `TrimStart('\uFEFF')` を追加（`TryParse` と揃えた。`PackageDependencyCheckerTests.ParseVersion_ReadsVersionOrNull` に BOM 付きを追加）。(c) E-9b の「Bootstrap が無ければ登録しない」が前のテストの残りに依存する件は**見送り**: 既存の E-9 と同じ前提で、全件実行で 2 回とも green。クリーンアップで隠すより、残りがあれば落ちて分かる方を残す。

---

## 確認して問題なしだった観点

- **#115 (b) フリーズの検査を Constant で外した判断**: 妥当。`ResolveNormalizedT` は尺 0 で t=1 を返し（`ValueDef.cs:64-68`）、Constant の `Evaluate` は t を使わないので、Constant で「フリーズ」は起きない。寿命を Duration で決める CameraShake / Haptics は `Loop` を見ない（`IsExpired`）ので Loop + 尺 0 でも無限にならない。UiTween の「完了しない無限ループ」は `LoopCount = 0` なら尺に関係なく起き、`UiTweenDataValidator:30` の Warning（Mode を問わない）が残る。
- **#115 (c) 書き換えた既存テスト 5 件**: `DurationMode_ValueZero_IsError`・`LoopWithZeroDuration_ProducesFreezeSpecificError`・`NonPositiveSpeedScale_IsError`・`Validate_FindsNestedValueDefsInValueDef3AndValueDefColor`（X と Alpha）・`Registry…`（`holder.Motion`）は、`Mode = Parametric, From = 0, To = 1` にしただけで期待（Error が出る・パスが付く）は元のまま。`From != To` で余計な Warning も出ない。追加の 4 件（Constant01 / `default` / Constant + Loop〔Error なし・Info あり〕/ Parametric と Curve は従来どおり 2 種の Error / 新規作成の Anim2D・CameraShake・ButtonSkin）で、Constant で Error が出ないことと Time を使うモードで出ることの両方が固定されている（ValueDefColor の Alpha だけ未固定 = GB-R-03）。
- **#115 (e) 互換**: Error が減る方向だけ（[42] §5.8 の「既存 Error → 引き下げ・削除は自由（PATCH）」）。`ValueDefValidator` の結果は `Code` を持たず、`ValidatorSeverityRegistryTests` は Code のある結果だけを集める（`Target = None` の universal は `ResolveDataType` が null で飛ばされる）ので `validator-severity.txt` に影響なし。シリアライズ・既定値・ファクトリ・公開 API・スナップショットは不変（`git diff 6bf56f7^1 6bf56f7` で Validator 1 ファイルとテスト・docs・CHANGELOG のみ）。CHANGELOG の区分（互換性節「挙動の変更(PATCH 相当)」+ 修正節）も適切（Placeholder の 1 語だけ = GB-R-05）。
- **#115 docs/17**: §6 の表に「Mode=Constant は対象外」を 3 行、「SpeedScale 既定 1」を実コード（既定 0、実行時は 1 扱い）に修正。実装と一致。
- **GA-R-02**: 保護の位置・ネットの送信回数・受信の適用順は不変。保留分の適用も `!instance.Done` で止まる。
- **GA-R-03**: v1.3.1 の行の削除・変更 0 件（`git diff 9f40cbb..a6fc496 -- …/Compat/Snapshots/` が `+131 / −0`）。
- **GA-R-07**: 件数・重さ・コード不変、null 安全、SpecWeb・個別検証・更新ウィンドウへの影響なし（上の表）。
- **GA-R-12**: コミットされた設定ファイルは空リスト 2 行の追加だけ。
- **6 か所 + docs/02 + スキャナの文言**: 「`OnEnable` / `OnDisable` の対・null なら何もしない・`IsReady` 不要・`Tick` で例外を出さない・ポーズ中も `Tick`」が全か所で一致（`docs/11_tasks.md:386` の返答文を含む）。スキャナのメッセージは「§8 と運用ページ」を指し、§8 に該当の段落がある。
- **#114 / #116**: `Packages/`・`ProjectSettings/`・`Tools/` の変更 0 件（`Assets/GameData` 71 + 5 ファイルと `docs/11`）。

---

## 挙動の変更と CHANGELOG の照合（v1.3.1 → `a6fc496`）

[58] の一覧（#15〜#23）からの差分（本 2 PR で新しく生じた行）に限る。

| # | v1.3.1 から見た挙動の変更 | 区分（本レビューの見立て） | CHANGELOG `[Unreleased]` |
|---|---|---|---|
| 24（新規） | `Run All` / `CI.ValidateAll` で 6 つの全体 Validator の結果がアセットに紐付かず `(project)`。報告の並びで全体の指摘が末尾に来る | Editor の出力の変更（件数・重さ・コード不変） | **あり**（`:17`。並び順は記載なし = 記載不要） |
| 25（新規） | `Presentation.Signal`（ローカル・受信とも）で、購読者が自分を止めたら残りの OnSignal トラックを発火しない | 不具合修正（PATCH 相当） | **あり**（`:65` の FZ-R-07 の項に追記） |
| 26（変更） | `ddriveUpdate` の値がオブジェクトでなければ「読めない宣言」（Warning・事前確認できませんでした） | 未リリースの P-15 の確定（v1.3.1 に無い） | **あり**（`:15`・`:34`） |
| 27（新規） | BOM 付き package.json を読める | 未リリースの機能の堅牢化 | **あり**（`:19`） |
| 28（新規） | 禁止 API の検査ウィンドウはドメインリロード後に自動で走査しない | 未リリースの機能の変更 | **あり**（`:19`） |
| 29（新規） | 除外の提供口の例外が Warning `DD-CAMEXEC-EXEMPT-INVALID` にも出る | 追加のみ（既存コードの Warning。未リリースの機能） | **あり**（`:18`） |
| 30（新規） | Editor 契約スナップショットにコンストラクタ行 | 契約の固定範囲の拡大（追加のみ） | **あり**（`:16`） |
| 31（新規） | `Mode=Constant` の ValueDef の Time の Error（3 種）が出なくなる | Validation の Error を減らす（PATCH 相当） | **あり**（`:14`・`:89`） |
| 32（新規） | 31 の結果、SpecWeb 送信で Placeholder 扱いでなくなる Data がある | 31 の波及（正しい方向） | **記載なし**（GB-R-05） |
| 33（新規） | 持ち込み先ガイドの案内・契約テスト E-9b | ドキュメント・テスト | **あり**（`:20`） |

MS2026 が踏みそうなもの: **#24**（CI が JUnit の classname を見ていれば）、**#31**（`CI.ValidateAll` の Error が減る = 良い方向）、GB-R-02（BGM を取り込むと Error。v1.3.1 から同じ）。

---

## レビュー済み / 未レビューの PR の対応表（`git log --merges 9f40cbb..a6fc496`）

| PR | マージ | 内容 | コード | レビュー |
|---|---|---|---|---|
| #86 | `9218bc0` | FC-0（docs 起票） | なし | [53] |
| #87〜#97 | `2d6605c`〜`59111a7` | FC-1〜FC-20 の実装 11 件 | あり | [53] |
| #98 | `cd312f3` | P-15 更新ウィンドウの追加パッケージ対応 | あり | [54] |
| #99 | `348da50` | docs/53 | なし（docs のみ） | – |
| #100 | `cb5ba07` | U-28 Canvas の埋め込み | あり | [54] |
| #101 | `45e86a8` | docs/54 | なし | – |
| #102 | `20c75bf` | 修正ラウンド 1 | あり | [55] |
| #103 | `64c0301` | 修正ラウンド 2 | あり | [55] |
| #104 | `4e4f61b` | docs/55 | なし | – |
| #105 | `84269f4` | 修正ラウンド 3 | あり | [56] |
| #106 | `772f6c3` | docs/56 | なし | – |
| #107 | `0a41d03` | 修正ラウンド 4 | あり | [57] |
| #108 | `d0b6971` | M-4 禁止 API の許可 | あり | [57] |
| #109 | `d2576cd` | docs/57 | なし | – |
| #110 | `6d42329` | 修正ラウンド 5 | あり | [58] |
| #111 | `9020727` | P-15 確認の不具合対応（BUG-1 / Q-1〜Q-4） | あり | [58] |
| #112 | `3e3eb75` | docs/58 | なし | – |
| #113 | `d4a2588` | 修正ラウンド 6 | あり | **本書** |
| #114 | `69aca4f` | 確認用データの整理 1（`Assets/GameData` 70 ファイル + docs/11） | データのみ（`Packages/` 0） | 対象外（参考として範囲だけ確認） |
| #115 | `6bf56f7` | Constant の ValueDef の検査 | あり | **本書** |
| #116 | `a6fc496`（`caa8377` = main の取り込み） | 確認用データの整理 2（`Assets/GameData` 4 ファイル + docs/11） | データのみ | 対象外 |

docs のみの PR（#99・#101・#104・#106・#109・#112）は `git diff --name-only` で `docs/` 以外の変更が 0 件であることを確認した。first-parent の非マージコミット（main への直接コミット）は 0 件。**本書の時点で、どのレビューにも入っていないコードの PR は無い**。ただし本書の GB-R-01 / 02（と P3 のうち対応するもの）を直す PR を入れれば、それは新たに未レビューになる。

---

## v1.4.0 のタグを打ってよいかの所見

**P1 は無く、互換面（スナップショット・メッセージ形式・公開 API）は追加のみなので、コードの正しさの面ではタグを止める理由は無い。** GA-R-01〜12 は記録どおり解消し、#115 は方向として正しい（Constant の新規作成で Error が出る不具合は直っている）。

**タグ前に残る作業（順序つき）**:

1. **GB-R-01・GB-R-02 を入れるかの判断**（ユーザー）。どちらも小さく（Validator の条件 1 か所 + テスト + CHANGELOG 1 行ずつ）、GB-R-01 は Warning の追加（MINOR の範囲）、GB-R-02 は Error を減らす変更（PATCH）なので**タグ前に入れるのが安い**。入れるなら差分の短いレビュー（本書の観点の再確認だけ）を挟む。見送るなら v1.4.1 で（GB-R-02 は v1.0.0 からの既存）。GB-R-03〜08 は v1.4.x でよい（GB-R-03 だけは、Error を戻す方向なのでタグ前なら「v1.3.1 と同じ重さ」のまま入れられる）。
2. **人による確認**（別 PC で進行中。本レビューでは状態を更新していない）: [43] §17（17-1〜17-8、許可コメントの書式はタグで固定）・§15 の再確認（15-25〜15-29。15-27 は GA-R-04 の文面の確認と一緒に）・§16（Canvas の埋め込み 25 行）、[52]（FC チケット。§22 はタグ後）。docs/43 には `□ 未` が 40 行、docs/52 には 59 行残っている（2026-10-06 時点の `a6fc496`）。
3. **`Tools/CI/run-ci.cmd` 全段 green**（Editor を閉じて）。[11] の「確認用データの整理」節の記録（`CI.ValidateAll` Error 0 / Warning 22 / Info 35・`MigrateCheck` green・EditMode 1620 / PlayMode 942 green）は本レビューでは未確認。`run-ci` の [4/8]（ID 再生成 + `git diff --exit-code`）〜[7/8] は誰も通していない。
4. **`check-release.ps1` → `bump-version.ps1 -Tag`**（[12] §7）。同期の後に、パッケージ同梱の `Documentation~/ConsumerGuide/operation.html` に GA-R-01 のコード例（`GameCooldownManager`）が入っていること、`Documentation~/ProgrammerManual/rules.html` が新しい文になっていることを目視（今は両方とも旧文）。
5. **SpecWeb**（`Tools/SpecWeb/push.cmd`。デプロイ②は UI で更新）。
6. **MS2026 への返答**（`docs/11` M-4 の返答文。GB-R-04 の 1〜2 文を足すなら送る前に）。

---

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・`CI.ValidateAll`・`run-ci.cmd`・`ForbiddenApiWindow` と設定画面の見た目・実 git）。特に `ConstantTimeValidationTests` が開発リポジトリで green か、`DataValidationRunner.Run` が全具象型で想定外の Error ログを出さないか、GB-R-08 の `CreateGUI` の時機は**未確認**。
- GA-R-07 の新旧の件数の実行での比較（コード読みでは同じ）。
- 持ち込み先（MS2026）での `testables` 有効時の `ConstantTimeValidationTests` / E-9b の実行結果、MS2026 の CI が JUnit の classname を使っているか。
- GB-R-02 の 0 / 0 の BGM の実際の再生（ループの継ぎ目の音）。分岐はコードで確認したが実機では聞いていない。
- 人による確認（docs/43・docs/52）の進行状況（別 PC。本書は `a6fc496` の文書の状態だけを見た）。
- `UiTweenManager` / `UiManager` の同形の走査（[56] FY-R-03 の「報告のみ」）は今回も読んでいない。
