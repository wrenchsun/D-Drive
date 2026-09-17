# 07. Canvas (UI) / 汎用 Prefab 詳細設計

関連: [02_core_framework.md](02_core_framework.md) / [03_audio.md](03_audio.md)

---

# Part A — Canvas

## A-1. 要件

- ボタンイベント、十字キー操作の配線管理、有効化・常時・閉じるイベント
- Manager: Open / Close / スタック / ポップアップ / 戻る

## A-2. データ構造

```csharp
public class CanvasData : AssetDataBase
{
    public GameObject Prefab;             // Canvas ルート Prefab
    [Header("Layer/Sort")]
    public UiLayer Layer;                 // HUD / Menu / Popup / Overlay / Loading
    public int SortOffset;
    [Header("Open/Close")]
    public UiTransition OpenTransition;   // Fade/Slide/Scale/Anim(AnimId)
    public UiTransition CloseTransition;
    public bool CloseOnBack;              // 戻るボタン/Bで閉じるか
    public bool ModalBlocksInput;         // 背後の入力を止めるか
    public bool PauseGameWhileOpen;       // 開いている間 Gameplay をポーズ
    [Header("Navigation")]
    public NavNode[] Navigation;          // ★十字キー配線 (下記)
    public string FirstSelected;          // 初期フォーカス要素パス
    [Header("Wiring")]
    public ButtonWire[] Buttons;          // ★ボタン→アクションの配線 (下記)
    public SliderWire[] Sliders;          // ★スライダー→アクション/オプションの配線（[18] §B-4）
}

[Serializable]
public struct NavNode      // 十字キー/スティックの明示配線
{
    public string Element;               // Prefab 内 Selectable への相対パス
    public string Up, Down, Left, Right; // 遷移先要素パス (空=Unity自動)
}

[Serializable]
public struct ButtonWire
{
    public string ButtonPath;            // Prefab 内 UiButton への相対パス
    public WireTrigger Trigger;          // Click/DoubleClick/LongPress/Repeat ([15] Part A)
    public UiAction Action;              // OpenCanvas/CloseSelf/SendSignal/PlayPresentation
    public AssetRef Target;              // OpenCanvas→CanvasId 等
    public string SignalKey;             // SendSignal のキー(コード側で購読)
    public SeId ClickSe;                 // 決定音 (空=Skin/Layer既定音)
}
```

ボタンは uGUI Button でなく**独自実装の UiButton**（[15_ui_interaction.md](15_ui_interaction.md) Part A）。スライダーも同様に uGUI Slider でなく**独自実装の UiSlider**（[18_ui_controls.md](18_ui_controls.md)）で、両者は共通の `UiInteractable` 基底（状態機械・Skin・Locked・ナビゲーション）を共有する。音量・画面揺れ・振動などの標準オプションへは `SliderWire.Action=SetOption` でコードを書かずに接続できる。また CanvasData は要素単位の出現/常時/消滅演出 `ElementFx[]`（同 Part B §B-4: UiTweenId × Appear/Idle/Disappear + スタッガー遅延 + SE）を持つ。

設計方針:

- **配線をデータ化**することで、UI 遷移（設定画面→サウンド設定 等）はデザイナーだけで組める
- コードに通知が必要な操作のみ `SendSignal("shop/buy")` → プログラマーは `Ui.OnSignal("shop/buy")` を購読。Button に直接リスナーを書かない
- 決定音・カーソル音は Layer ごとの既定 SE + 個別上書き（[03] と連携）

## A-3. Manager API

```csharp
public static class Ui
{
    public static UniTask<CanvasHandle> Open(CanvasId id);       // スタックに積む
    public static UniTask Close(CanvasHandle h);
    public static UniTask CloseTop();                            // 「戻る」
    public static UniTask<CanvasHandle> Popup(CanvasId id);      // モーダル
    public static IDisposable OnSignal(string key, Action<SignalArgs> cb);
    public static void SetLayerVisible(UiLayer layer, bool v);   // 演出中HUD非表示等
}
```

内部実装:

- Layer ごとのルート Canvas 配下に Prefab を Pool から Rent して配置
- スタック管理: Open で push、Back 入力で `CloseOnBack` の最上位を Close
- Navigation は Open 時に `Selectable.navigation` へ explicit 設定として適用
- `PauseGameWhileOpen` → PauseService.Push/Pop（[02] §10）
- OnEnable / OnDisable イベント → AssetEvent 発火（Duck・BGM 切替をここに設定可能）

## A-4. エディタ / Validation

- CanvasEditor: Prefab をプレビュー表示し、Selectable を自動収集 → Navigation をノードグラフ（矢印表示）で編集。ボタン配線もリスト編集。ゲームパッド入力シミュレーションでフォーカス移動を確認
- Validation: Prefab Missing (Error) / ButtonPath・Element パス不整合 (Error) / Navigation の到達不能要素 (Warning) / FirstSelected 未設定 (Warning) / SendSignal のキーがコード側に購読なし (Info)。Slider 関連の検査は [18] §B-7 を参照

### 実装メモ（2026-09-10、4-1 / 4-5 Canvas 側）

- 実装: `Assets/DDrive/Runtime/Canvas/`(`CanvasData.cs` / `UiManager.cs` / `Ui.cs` / `CanvasDataValidator.cs`)+ `Assets/DDrive/Editor/Canvas/`(`CanvasEditorWindow.cs` / `CanvasNavigationCollector.cs`)。ModelsManager([05] A-3)/ PrefabsManager([07] B-3)と同じ設計で InstanceStore + PoolService の Rent/Return/Discard、EventBus の Begin/Fire/End を踏襲する。namespace は疑似コードと異なり `DDrive.Runtime.Ui`(フォルダは `Runtime/Canvas/`)
- ルート: 初回使用時に `UiManager.EnsureRoot()` が `"[D-Drive] UI Root"` を生成し(Play 中は `DontDestroyOnLoad`、Edit 中は `HideFlags.DontSave`)、`UiLayer` の値ごとに `Canvas`(ScreenSpaceOverlay, sortingOrder = レイヤー index * 100)+ `GraphicRaycaster` + `CanvasGroup` を持つ子を作る。EventSystem は作らず、Play 中に無ければ 1 回だけ警告する
- スタック: レイヤー別ではなく単一のグローバル `_stack`(`List<Handle<CanvasMarker>>`)で開いた順序を管理する。`Open`/`Popup` で末尾に push、`Close` は演出完了後に `Remove`、`CloseTop`/`CloseTopAsync` はスタック上位から `CloseOnBack==true` かつ閉じ処理中でないものを 1 つ探して閉じる(間に `CloseOnBack==false` があってもスキップして探し続ける)
- 演出: `UiTransition.Kind` が `None`/`Anim` または `Duration<=0` は即時完了、それ以外(`Fade`/`Slide`/`Scale`)は `_transitions`(小さいリスト、LINQ/クロージャなし)に積んで `Tick(dt)` ごとに `EaseDef.Evaluate` で進める。`OpenAsync`/`PopupAsync`/`CloseAsync` は対応する `TransitionState.Completion`(`UniTaskCompletionSource`)を await する。`Kind=Anim` は `UiManager.AnimHook`(`Func<AssetId<AnimMarker>, Animator, Handle<AnimMarker>>`)が設定されていれば呼び出すだけで、再生完了待ちはしない(即時完了扱い。UiButton/AnimEditor からの本格配線は 4-2/4-6 以降)
- モーダルブロッキング: `OpenData(data, modal:true)` かつ `ModalBlocksInput` のとき `IsModalBlocking=true` を立て、`RecomputeBlocking()` がスタック中で最も上にある「閉じ処理中でないモーダル」より下の全 CanvasGroup の `interactable`/`blocksRaycasts` を false にする(Push/Pop のたびに再計算するため、複数モーダルが重なっても閉じた順に正しく復元される)
- ポーズ: `PauseGameWhileOpen` は `Open` 時に `PauseService.Push(PauseChannel.Gameplay)`、`Close` の演出完了時に `Pop`(疑似コードの `[02] §10` と同じ規則)
- イベント/シグナル: `UiManager.Events`(EventBus)で OnSpawn/OnEnable/OnDisable/OnDestroy を発火する(Bootstrap が `UiDispatcher` という 3 つ目の `AssetEventDispatcher` を Prefabs/Anim とは独立して生成する)。`SendSignal(key, handle, elementPath)` はコード購読(`OnSignal`)へ配る用途で、UiButton 本体の配線(4-2/4-6)は未実装
- Placeholder: 未登録 ID や `Prefab` 未設定の `CanvasData` は名前 `"<Placeholder:CANVAS>"` の空 `RectTransform` を生成する(Pool を経由しない。Close で `Object.Destroy`)
- Validation: 疑似コードの「SendSignal のキーが購読なし (Info)」は Validate 時点でランタイムの購読状況を知りようがないため実装せず、代わりにチケット仕様通り「`ButtonWire.Action==SendSignal` なのに `SignalKey` が空 (Error)」を検査する。到達不能な Selectable の判定は `Navigation` の Up/Down/Left/Right が指す要素だけを「到達済み」とし、`FirstSelected` に一致する要素は対象外にする
- 未実装(後続チケット): ElementFx(4-9)は完了、UiSlider 本体は 2026-09-11(4-14)で実装済み(下記追記参照)。CanvasEditor のノードグラフ・ゲームパッド入力シミュレーション(4-3)は 2026-09-11 に実装済み(下記追記参照)
- **2026-09-11 追記(4-2/4-6)**: UiButton/UiInteractable 本体を実装し、`ButtonWire` の実行(Trigger→UiButton イベント購読、Open で配線 → Close で解除)まで完了した。詳細は [15_ui_interaction.md](15_ui_interaction.md) の実装メモを参照。`Navigation` は `Selectable` に加え `UiInteractable`(`UiNavigation` コンポーネント)にも対応し、`UiManager.MoveFocus(Vector2)` を新設した
- テスト: `Assets/DDrive/Tests/Runtime/UiManagerTests.cs`(`UiManagerTests` 12 件 + `CanvasDataValidatorTests` 4 件)、`Assets/DDrive/Tests/Editor/CanvasEditorTests.cs`(4 件)。Open/Close/スタック/Popup ブロッキングと復元/CloseTop の CloseOnBack/PauseGameWhileOpen/Navigation 明示配線/OnSignal 購読解除/Fade 演出の Tick 完了と OpenAsync/ライフサイクルイベント/未登録 ID の Placeholder、Validator の 4 ケース、Selectable 自動収集(新規収集・既存保持・null Prefab)、DataEditorRegistry 解決を確認

### 実装メモ追記(2026-09-11、4-9 ElementFx + 4-7 残り レイヤー既定 SE)

- `CanvasData.ElementEffects`(`ElementFx[]`)を追加。1 要素 = `ElementPath` + Appear/Idle/Disappear(それぞれ id/Preset)+ `AppearDelay` + Appear/DisappearSe([15] B-4 参照)
- **`UiLayerSettings`**(新規 `ScriptableObject`。`Assets/DDrive/Runtime/Canvas/UiLayerSettings.cs`)を追加。`AssetDataBase` ではなくプロジェクト単位の設定アセット 1 個(`DDriveRuntimeBootstrap.LayerSettings` を Inspector 直参照、`Ui.SetLayerSettings` で `UiManager` へ配る)。`UiLayer` ごとに `DefaultButtonSkin` / `DefaultClickSe` / `DefaultHoverSe` / `DefaultDeniedSe` / `DefaultAppear` / `DefaultDisappear` を持つ
- **Open**: `ApplyLayerDefaults` → `SetupElementFx` を `WireButtons` の直後に実行する。`ApplyLayerDefaults` は Prefab 内の全 `UiInteractable` に `SetDefaultSe` を配り、`SkinId` 未設定 かつ 明示 `SetVisual` 未実行(`HasExplicitSkin==false`)の対象にだけ `ApplyDefaultSkin` を当てる。`SetupElementFx` は `ElementPath` を解決して `ElementFxRuntime` の一覧を作り、Appear を持つ要素の数だけ `PendingAppearCount` を積む(未解決パスは 1 回だけ警告してスキップ)
- **入力ゲート**: `RecomputeBlocking` がモーダルブロックとは独立に `PendingAppearCount > 0` の Canvas を非対話化する。要素の Appear が完了するたびに `UiManager.Tick` が呼び直す
- **Close**: `StartAllDisappearFx`(全要素の Disappear を一斉開始し `PendingDisappearCount` を確定)→ `StartTransition(CloseTransition)` の順。実際に `FinalizeClose` するのは `CloseTransitionCompleted && PendingDisappearCount<=0` の両方が揃ってから(`TryFinalizeClose`)。`StopAll` は演出を待たず `Stop(complete:true)` で畳んでから閉じる
- 詳細な実装メモ・解決順・スタッガーの仕組みは [15_ui_interaction.md](15_ui_interaction.md) B-4 の 2026-09-11 実装メモを参照
- テスト: `Assets/DDrive/Tests/Runtime/ElementFxTests.cs`(`ElementFxTests` 8 件 + `ElementFxValidatorTests` 5 件)

### 実装メモ追記(2026-09-11、4-16 SliderWire + OptionStore)

- `SliderWire` の形を疑似コード段階の `{ SliderPath, OptionKey(string), Target, SignalKey }` から [18_ui_controls.md](18_ui_controls.md) B-4 の形(`ElementPath` / `Trigger(SliderTrigger)` / `Action(UiAction)` / `SignalKey` / `Option(OptionKey)` / `ThrottleSec`)へ置き換えた。`UiAction` に `SetOption` を追加(SliderWire 専用)
- `UiManager.WireSliders` を `WireButtons` と同じ形で追加。`Open` 時、`Action=SetOption` の配線は `OptionStore.Get(Option)` を `Min..Max` へ写像して `SetValueSilent` で初期化する。`Trigger` ごとに `UiSlider` の `OnValueChanged`/`OnCommit`/`OnNotchPassed`/`OnLimitReached` を購読し(`Close` で `WireUnsubscribers` から解除、`ButtonWire` と共用)、`Action=SetOption` なら `OptionStore.Set(Option, slider.NormalizedValue)`、`Action=SendSignal` なら `SendSignal(key, handle, path, value)` を呼ぶ。`Trigger=Changed` は `SliderWire.ThrottleSec` と `UiSlider.ChangeThrottleSec` の大きい方を採用する(スライダー側の設定を尊重しつつ配線からも間引ける)
- `SignalArgs` に `float Value` を追加(既定値 0 の追加パラメータなので既存呼び出しは無変更で動く)。`SendSignal` も同様に `value=0f` を追加パラメータにした
- `OptionStore`(`Assets/DDrive/Runtime/Ui/OptionStore.cs`)は `DDriveRuntimeBootstrap.Options` として構築し、起動時に `PlayerPrefsOptionStorage` から `Load`、`Ui.SetOptionStore` で `UiManager` へ渡す。静的ファサード `DDrive.Runtime.Ui.Options`(`Ui`/`UiSkins` と同じ設計)も Bind する
- Validation: `CanvasDataValidator` に `ValidateSliders`(配線: ElementPath 不整合 Error / Action=SetOption で Option=None Error / Trigger=Changed かつ ThrottleSec=0 で Action=PlayPresentation は Warning)と `ValidateSliderComponents`(Prefab 内の全 `UiSlider` に `UiSliderValidation.Validate` を適用)を追加。NavNode の左右設定 + `EscapeOnLimit=false` の警告は `ValidateNavigation` に統合した
- 詳細な UiSlider 本体の実装メモは [18_ui_controls.md](18_ui_controls.md) B-7 の 2026-09-11 実装メモを参照
- テスト: `Assets/DDrive/Tests/Runtime/UiManagerTests.cs` に `SliderWire_SetOption_InitializesFromStore_AndWritesBackOnCommit` / `SliderWire_SendSignal_CarriesValue` および `CanvasDataValidatorTests` に 3 ケース追加。`Assets/DDrive/Tests/Runtime/UiSliderTests.cs` に `OptionStoreTests`(4 件)を追加

### レビュー対応(2026-09-11、Phase 4 コードレビュー)

- **プールした Canvas が再オープン時に見えない**: `CloseTransition`(Scale/Fade/Slide)の終端値(scale 0 / alpha 0 / スライドオフセット位置)を残したまま `_pool.Return` していたため、次に `Rent` した実体をそのまま `OpenData` が使うと非表示のまま開いていた。`UiManager.FinalizeClose` の Return 直前で `CanvasGroup.alpha=1` / `localScale=Vector3.one` / `anchoredPosition=BaseAnchoredPosition` に戻すよう修正
- **`CloseTransition=None` で `CloseAsync` が ElementFx.Disappear を待たずに返る**: `AwaitCloseTransition` は `_transitions`(演出が実際に走っているものだけ積まれる)を走査していたため、`Kind=None`/`Duration<=0` は何も見つからず即座に返っていた。`CanvasInstance` に `CloseCompletion`(`UniTaskCompletionSource`)を追加し、`FinalizeClose`(実際に閉じ切った瞬間、`CloseTransitionCompleted && PendingDisappearCount<=0` の両方が揃ってから)で解決するよう変更。`CloseAsync` はこれを await する(`AwaitCloseTransition` は削除)
- **`OptionStore` が一度も保存されない**: `DDriveRuntimeBootstrap` は起動時に `Options.Load` するだけで `Save` を一度も呼んでいなかった。`OptionStore` に `Storage`(注入可能)+ `SaveIfDirty()`(`Set` のたびに dirty フラグを立て、保存後にクリア)を追加し、`Bootstrap.OnDestroy`/`OnApplicationQuit` から呼ぶようにした
- テスト: `Assets/DDrive/Tests/Runtime/UiManagerTests.cs` の `Close_WithScaleTransition_ThenReopen_PooledCanvas_IsVisibleAgain`(プール再利用時の scale/alpha 復元)、`Assets/DDrive/Tests/Runtime/UiSliderTests.cs` の `OptionStoreTests.SaveIfDirty_WritesOnlyAfterChange_ThenClearsDirtyFlag`(OptionStore の保存)

### 実装メモ（2026-09-11、4-3 CanvasEditor: Navigation ノードグラフ / パッド入力シミュレーション）

- **モデル**: `Assets/DDrive/Editor/Canvas/NavigationGraph.cs`(UI 非依存の純粋クラス)。`NavigationGraph.Build(CanvasData, GameObject prefab)` が Prefab 内の全 `Selectable`/`UiInteractable` を(`Navigation` への登録有無に関わらず)ノード化し(`IsListed` で区別)、`Navigation` に書かれているが Prefab 側で見つからない要素も `HasComponent=false` のノードとして可視化する(パス不整合の発見用)。ノードの表示座標は対象 `RectTransform` の world corners の中心を Prefab ルート基準に投影し、画面表示に合わせて上下反転する(`ComputePosition`)。`RectTransform` が無い/計算できないノードは `AssignGridFallback` でグリッドに並べる。`Unreachable(firstSelected)` は BFS ではなく `CanvasDataValidator.ValidateNavigation` と同じ「いずれかの方向から参照されている要素(または FirstSelected)は到達済み」という参照集合の一致判定にしている(Validator と結果を確実に一致させるため。`CanvasGraphTests.Unreachable_AgreesWithCanvasDataValidator_OnThreeNodeSample` で一致を確認)。`SetLink`/`ClearLink`/`ClearAllLinks` は `CanvasData.Navigation` を直接書き換えるだけの純粋なヘルパーで、Undo/SetDirty は呼び出し側(`CanvasEditorWindow`)の責務
- **表示/編集**: `Assets/DDrive/Editor/Canvas/NavigationGraphView.cs`。`UnityEditor.Experimental.GraphView` は実験 API のため使わず、手組みの `VisualElement` で実装した(owner instruction: ノードグラフは実 UI を描画しない静的な編集ダイアグラムとして許容)。ノードは絶対配置の `VisualElement`(矢印用に 4 方向のポート `VisualElement` を持つ)、矢印は `generateVisualContent` + `Painter2D`(`MoveTo`/`LineTo`/`Stroke` + 三角形の矢頭を `Fill`)で方向ごとに色分けして描く。パンは中ドラッグ(または Alt+左ドラッグ)、ズームはホイールで `_world.transform.scale` を変更する(パン/ズームは `VisualElement.transform` の render transform のみを動かし、ヒットテスト座標の変換は `ScreenToWorld`(`(localPos - pan) / zoom`)で手動計算するため UI Toolkit のバージョン挙動に依存しない)。編集はポートの `PointerDown` → ドラッグ中は仮の矢印を描画 → 別ノード上で `PointerUp` すると `OnSetLink(from, dir, to)` を呼ぶ(ドロップ先が無ければキャンセル)。右クリックで `ContextualMenuManipulator`(「FirstSelected にする」「リンクを全て削除」+ 方向ごとの「◯◯ を削除」(その方向にリンクが無ければ無効化)、ダブルクリックで `EditorGUIUtility.PingObject` による Hierarchy Ping
- **CanvasEditorWindow 統合**: 「Navigation グラフ」`Foldout`(初期 320px、`SliderInt` で高さ変更可)+「自動レイアウトを更新」「到達不能を検出」(結果を赤枠ノードと一覧テキストの両方に表示)「未配線を自動リンク」(Automatic のままの要素を一覧するだけでデータは書き換えない。ボタン名は疑似コード通りだが実際の挙動はレポートのみで、後述の理由により自動リンクは行わない)。グラフの編集操作(`OnSetLink`/`OnClearLink`/`OnClearAllLinks`/`OnSetFirstSelected`)は全て `ApplyNavEdit` 経由で `Undo.RecordObject` + `EditorUtility.SetDirty` + `RefreshValidation`/`RebuildGraph` を行う。`Undo.undoRedoPerformed` を購読し Undo/Redo 後にグラフを再構築する
- **「未配線を自動リンク」を実装しない理由**: [07] A-3 実装メモにある通り、4 方向とも空の `NavNode` は `UiManager.ApplyNavigation` が `Navigation.Mode.Automatic`(Unity 標準の自動ナビゲーション)のまま扱う設計になっており、ここへ機械的にリンクを書き込むと明示化されて自動ナビゲーションの恩恵(レイアウト変更への追従)を失う。そのためボタンは対象を一覧するだけに留めた
- **パッド操作シミュレーション**: 「確認用シーンで開く」で実際に開いた `UiManager` インスタンスに対してのみ動作する(owner instruction 2026-09-10: ウィンドウ内に UI を再現描画しない)。▲▼◀▶ は `EventSystem.current` があれば `UiManager.MoveFocus(Vector2)` を、無ければ新設の `UiManager.MoveFocusFrom(Handle<CanvasMarker>, string currentPath, Vector2 dir, out string nextPath)` を呼ぶ。`MoveFocusFrom` は (1) 対象要素の `NavNode` に明示リンクがあればそれを辿り、(2) 無ければ `Selectable.interactable`/`UiInteractable.CanFocus` な要素の中から指定方向にある最も近いもの(主軸距離 + 副軸ズレ×2 のペナルティで採点。Unity の自動ナビゲーションに近い簡易ヒューリスティック)を選ぶ。`EventSystem.current` があれば `Selectable.Select()`/`SetSelectedGameObject` で実際に選択も反映する。ノードグラフは `SetFocusedPath` でフォーカス中のノードに白枠のリングを表示し、ウィンドウにも現在のフォーカスパスをラベル表示する。「決定」は `UiManager.GetComponent<UiButton>` で対象を解決して `UiButton.SimulateClick()` を呼ぶ(`ISubmitHandler` 実装のみの独自コンポーネントへの配線は未対応。現状 `UiButton` のみ)
- **デザイナーが手動確認すべき点**: (1) ノードグラフのポートドラッグ&ドロップの実際の操作感(マウス操作は自動テストできない)、(2) パン/ズームの視認性(特に要素数が多い Prefab)、(3) パッド操作シミュレーションのフォーカス移動が実際の見た目(SceneView/GameView)と一致すること
- テスト: `Assets/DDrive/Tests/Editor/CanvasGraphTests.cs`(8 件)。`NavigationGraph.Build` のノード収集(登録済み/未登録、座標の上下判定)、`Unreachable` と `CanvasDataValidator` の一致、`SetLink`/`ClearLink`/`ClearAllLinks`、`UiManager.MoveFocusFrom` の明示リンク追従・方向フォールバック・該当なしケース、`UiButton.SimulateClick` の `OnClick` 発火

### 追記（2026-09-12、ノードグラフのドラッグ移動）

- **経緯**: ノード座標を実 `RectTransform` の位置そのまま投影しているため、密集した UI(ボタンが近接するパネル等)では箱同士が重なり、ポート/矢印が判読できず配線しづらいという指摘(デザイナー確認事項(2)で懸念されていたもの)。自動で間隔を空けるより、必要な箇所だけ手で退避できる方が良いため手動ドラッグ移動を追加した
- **実装**: `NavigationGraphView` のノード本体(`BuildNodeElement` の box)に `PointerDown`/`PointerMove`/`PointerUp` を追加し、ポート以外の場所を左ドラッグするとノードを移動できる(ポートは `RegisterPortDrag` 側で `StopPropagation` 済みのため、リンク作成のドラッグとは干渉しない。ダブルクリックの Ping も従来通り)。動かした位置は `_manualPositions`(`Dictionary<string, Vector2>`、パスをキー)にビュー側で保持し、`SetGraph`(=`RebuildGraph`。リンク編集や Undo/Redo のたびに呼ばれる)を跨いで維持する。当初は `CanvasData` に保存しない設計だったが、2026-09-12 にユーザー要望で例外的に永続化するよう変更した(下記追記を参照)。「自動レイアウトを更新」ボタンは `NavigationGraphView.ClearManualLayout()` を呼んでから再構築し、Prefab のレイアウトどおりの自動配置に戻す
- **デザイナーが手動確認すべき点に追加**: (4) ノード本体のドラッグ移動がポートからのリンク作成ドラッグと誤操作なく区別できること、リンク編集後も動かした位置が保持されること
- **配線を切る(UE ブループリント風のカット操作)**: 背景を Ctrl+左ドラッグでなぞると赤い線が引かれ(`_cutPoints` に一定距離ごとに頂点を追加する折れ線)、離した時点でその線分と交差するエッジ(`FindEdgesCrossingCutPath` が矢印の描画線分そのものとの交差判定 `SegmentsIntersect` で収集)をまとめて `OnCutLinks`(`List<NavEdge>`)で通知する。`CanvasEditorWindow` 側は `ApplyNavEdit` で 1 回の Undo にまとめて `NavigationGraph.ClearLink` を順に呼ぶ。右クリックメニューの個別削除(既存)と役割が異なる: ノードが密集して個別にポート/右クリックを狙いにくい場面向けの一括操作
- **デザイナーが手動確認すべき点に追加**: (5) Ctrl+左ドラッグのカット操作が誤って別の操作(パン/リンク作成/ノード移動)と衝突しないこと、意図した配線だけが切れること
- **Reroute point(UE ブループリント風の中継点)**: 配線(矢印)を空クリックではなくダブルクリックすると、その位置に中継点を挿入できる(`TryFindWireNear` が矢印の折れ線各区間との距離判定でヒットを取り、`InsertReroutePoint` が該当区間のインデックスにそのまま挿入する)。中継点は小さな丸の `VisualElement`(`BuildRerouteElement`)としてノードと同様にドラッグで移動でき、右クリックメニューから削除できる。エッジは `From/Direction` の組で一意に決まるため、中継点は `(From, Direction)` をキーに `List<Vector2>` として保持する(`_reroutePoints`)。矢印描画(`DrawArrows`)・配線カット(`FindEdgesCrossingCutPath`)は共通の `BuildEdgePolyline`(始点ノード中心→中継点→終点ノード中心)を使うため、中継点を経由した折れ線に対しても矢頭表示とカットが正しく機能する。ノード位置(`_manualPositions`)と同様、2026-09-12 からは `CanvasData` へ永続化される(下記追記を参照)。リンクを削除すると `SetGraph` 時点の `PruneReroutePoints` で孤立した中継点も消える。「自動レイアウトを更新」は `ClearManualLayout` 経由でノード位置と一緒に中継点も全消去する
- **デザイナーが手動確認すべき点に追加**: (6) 配線のダブルクリックでの中継点追加、ドラッグ移動、右クリック削除の操作感、中継点を挟んだ配線でもカット(Ctrl+ドラッグ)と矢印の向きが正しいこと

### バグ修正（2026-09-12、パッド操作シミュレーションが FirstSelected 未設定だと反応しない）

- **症状**: 「確認用シーンで開く」でパッド操作シミュレーションのボタンは有効になるが、`FirstSelected` が未設定の `CanvasData` では ▲▼◀▶ を押しても何も起きず、「フォーカス: (未確認)」のまま変化しなかった
- **原因**: `CanvasEditorWindow` は `FirstSelected` 未設定時 `_simFocusPath` を空文字(=ルート。`NavigationGraph`/エディタ側の慣習)にする。`UiManager.MoveFocusFrom` はこれを `FindTransform(root, currentPath)` で解決していたが、`FindTransform` は `NavNode.Up/Down/Left/Right` の「未設定」を表すために空文字を `null` として扱う設計になっており、「ルート自身」を指す空文字もこれと区別できず `null` になっていた。結果、`currentRect == null` で即 `false` を返し、方向探索(`CollectFocusableTransforms` からの最近傍検索)まで到達しなかった
- **修正**: `Assets/DDrive/Runtime/Canvas/UiManager.cs` の `MoveFocusFrom` で、`currentPath` が空のときだけ `FindTransform` を経由せず `root` 自身を現在位置として使うようにした(`NavNode.Up` 等の「未設定」判定には影響しない、`MoveFocusFrom` 内のこの 1 箇所だけの変更)。これにより `FirstSelected` 未設定でも、ルート(Prefab 全体の矩形)を起点に指定方向の最も近い `Selectable`/`UiInteractable` を見つけて最初のフォーカスが決まるようになった
- テスト: `CanvasGraphTests.MoveFocusFrom_FromRootPath_FindsNearestElement_WhenFirstSelectedNotSet` を追加(回帰防止)

### 追記（2026-09-12、ノードグラフの手動レイアウトを例外的に永続化）

- **経緯**: ノードのドラッグ位置・Reroute point はエディタセッション内だけの一時状態として実装していたが、「毎回開くたびに並べ直すのは手間」というユーザー要望を受け、例外的に `CanvasData` へ保存するようにした。CLAUDE.md #5(Data は読み取り専用/エディタが書き換えるときは Undo.RecordObject + SetDirty)には従うが、#9 で言うような「実行時の挙動に関わるデータ」ではなく、あくまで見た目の整理用データという位置付け
- **追加した Data**: `Assets/DDrive/Runtime/Canvas/CanvasData.cs` に `[Serializable] struct NavNodeLayout { string Element; Vector2 Position; }` と `[Serializable] struct NavEdgeWaypoint { string Element; string Direction; Vector2[] Points; }` を追加し、`CanvasData.NavigationNodeLayout` / `CanvasData.NavigationEdgeWaypoints` として持たせた(どちらも `[HideInInspector]`。通常の Inspector には出さず、`CanvasEditorWindow` のノードグラフからだけ操作する)。`Direction` は Editor 専用の `NavDirection` enum をランタイム側が参照できない(Runtime asmdef は Editor asmdef を参照できない)ため、`ToString()`/`Enum.TryParse` で文字列として橋渡しする
- **実装**: `NavigationGraphView` に `OnLayoutChanged`(Action)を追加し、ノードのドラッグ確定時(`PointerUp`)・Reroute point のドラッグ確定時・追加(`InsertReroutePoint`)・削除(`RemoveReroutePoint`)・全消去(`ClearManualLayout`)のたびに発火する。`LoadLayout(NavNodeLayout[], NavEdgeWaypoint[])`(内部辞書へ読み込む)と `ExportNodeLayout()`/`ExportEdgeWaypoints()`(内部辞書から配列を作る)を公開し、`CanvasEditorWindow` は `OnLayoutChanged` で `Undo.RecordObject` + `Export*` の代入 + `SetDirty` を行う(グラフ形状は変わらないため `ApplyNavEdit` は使わず `RebuildGraph` は呼ばない)。逆方向の読み込みは `RebuildGraph()` の先頭で毎回 `LoadLayout(_target?.NavigationNodeLayout, _target?.NavigationEdgeWaypoints)` を呼ぶことで行う(冪等なので毎回呼んでも無害。`Undo.RecordObject` は `CanvasData` の全フィールドをスナップショットするため、Ctrl+Z でリンクと一緒にレイアウトも元に戻る)
- **デザイナーが手動確認すべき点に追加**: (7) ウィンドウを閉じて開き直しても・Unity を再起動しても、ドラッグしたノード位置と Reroute point が復元されること。Undo(Ctrl+Z)でレイアウト変更も戻ること
- テスト: `CanvasEditorTests.NavigationGraphView_LoadLayout_ThenExport_RoundTripsNodePositionsAndWaypoints` / `NavigationGraphView_LoadLayout_IgnoresUnknownDirectionString` を追加

### 追記（2026-09-12、「確認用シーンで開く」で Selectable も自動収集）

- **経緯**: パッド操作シミュレーション/ノードグラフを使うには `Navigation`(Selectable の収集結果)が必要だが、「Selectable を自動収集」ボタンを別途押す一手間があった。プレビューを開く時点でほぼ確実に収集したくなるため、`PlacePreview` の中で自動的に `CollectSelectables()` を呼ぶようにした
- **実装**: `CanvasEditorWindow.PlacePreview()` で `_manager.OpenData(_target)` が成功した(`_manager.IsOpen(_previewHandle)`)場合だけ `CollectSelectables()` を続けて呼ぶ(表示に失敗した場合は呼ばない)。`CollectSelectables` は `CanvasNavigationCollector.CollectMerged` で既存の行を保持しつつ不足分だけ追加するため、「確認用シーンで開く」を何度押しても安全(重複追加や上書きは起きない)
- 副作用として、プレビューを開くたびに `_statusLabel` の表示が「プレビュー表示中」から `CollectSelectables` 側の「Selectable を N 件収集しました」に置き換わる(意図した挙動)

### 追記（2026-09-12、「確認用シーンで開く」で要素(ElementFx)も自動収集 + ElementFx 割当を Foldout 化）

- **自動収集**: 上記と同じ理由で `PlacePreview` の同じ分岐(プレビュー表示成功時)から `CollectElementFx()` も呼ぶようにした(`CollectSelectables()` の直後)。こちらも `CanvasElementFxCollector.CollectMerged` で既存行を保持するため繰り返し実行しても安全。2 つとも `SetTarget(_target)` を内部で呼び直す(Inspector/Validation/ElementFx 割当/グラフを再構築)ため多少冗長だが、実害はない。最終的な `_statusLabel` の表示は後に呼ばれる `CollectElementFx` 側の「ElementFx を N 件収集しました」になる
- **ElementFx 割当の Foldout 化**: 「ElementFx 割当(Appear / Idle / Disappear)」のラベル+HelpBox+一覧(`_elementFxContainer`)を `Foldout`(`_elementFxFoldout`、初期値 `true`)にまとめた。要素数が多い Prefab で「Navigation グラフ」を編集する際にスクロールが長くなりすぎるのを緩和する意図(Navigation グラフの Foldout と同様の扱い)

### バグ修正（2026-09-12、CanvasData/ButtonSkinData は Flags.Load=Preload 必須）

- **症状**: `ButtonWire`/`SliderWire`/`UiLayerSettings` を正しく設定していても、実機/Play で `Ui.Open` した Canvas が常に `"<Placeholder:CANVAS>"` になり、レイヤー既定 Skin(4-7)も一切効かないことがある(手動確認シート [23_manual_verification_2026-09-11.md](23_manual_verification_2026-09-11.md) 相当の検証で発覚)
- **原因**: `Ui.Open`/`Ui.Popup` は `AssetRegistry.ResolveOrPlaceholder`、`UiManager.ApplyLayerDefaults`(4-7 の `UiLayerSettings.DefaultButtonSkin` 解決)は `TryResolveSync` を使うが、両方とも「既に `_loaded` キャッシュにあるものしか返さない」同期専用の解決であり、`Flags.Load` の既定値 `LazyLoad`(「初回参照時にロード」のはずの非同期パス)を経由しない。`_loaded` に乗るのは `Flags.Load=Preload` でカタログ登録時にプリロードされたものだけ。B-3([07] 実装メモ 2026-09-10 の Codex レビュー P2)で `PrefabsManager.Preload` に対して一度直した同種の罠が、`CanvasData`/`ButtonSkinData` 側には残っていた
- **対応**: `AssetCreationService.Create()`(`Assets/DDrive/Editor/AssetBrowser/AssetCreationService.cs`)で `AssetType.Canvas`/`AssetType.ControlSkin` を新規作成するときは既定 `Flags.Load=Preload` にする。既存アセット向けに `AddressablesRegistrationValidator`(`Assets/DDrive/Editor/Validation/AddressablesRegistrationValidator.cs`)へ「対象 AssetType で `Flags.Load != Preload`」を Error + FixAction(`Flags.Load=Preload` に書き換えて保存)として追加し、`Validation > Run All` で拾えるようにした
- 今後 `SliderSkinData`(4-17 で型追加時)等、`UiInteractable`/`UiManager` の同期解決に乗る新しい `ControlSkinData` 派生を増やす場合は、`AssetCreationService` の分岐と `AddressablesRegistrationValidator.NeedsPreload` の両方に追記すること

### 追記（2026-09-12、CanvasEditor に「Disappear を再生」ボタン追加 → 同日中に撤去）

- **経緯**: 「確認用シーンで開く」は実 `UiManager.OpenData` を呼ぶため Appear→Idle は元から Tick で再生されていたが、「閉じる」は `StopAll(Manual)` で演出を待たず即完了させる実装だったため、ElementFx の Disappear / CloseTransition を Editor 上で見る手段が無かった(ADR-4 の「実 Manager を Editor から駆動する」に対し、消える演出だけ確認できない片手落ちだった)
- **対応**: `CanvasEditorWindow` に「Disappear を再生」ボタンを追加(`PlacePreview`/`RemovePreview` の間)。実際に `UiManager.Close(handle)` を呼び、`OnEditorUpdate` の `Tick` で CloseTransition + ElementFx.Disappear が最後まで再生されるのを待ってから後片付けする(`_awaitingDisappearFinish` フラグで `IsOpen==false` になった瞬間を検知)。「閉じる」(即時 `StopAll`)はそのまま残し、見た目を待たず片付けたいときと使い分けられるようにした
- 確認: SceneView スクリーンショットで PopIn(Appear)→FadeOut(Disappear、ボタン押下)の一連が実際に描画されること、`Disappear` 完了後に `_manager.IsOpen`/`_previewRoot` が正しくクリアされ、コンソールにエラーが出ないことを確認済み
- **撤去(同日、ユーザー指示)**: 同日中に UI Tween Editor 側へ「実要素をプレビュー対象に自動割り当てして再生できる」機能(§後述「Canvas Editor から実要素で確認」)が入り、Disappear の Track を UI Tween Editor で個別に確認できるようになったため、Canvas Editor 専用の「Disappear を再生」ボタン(`PlayDisappearPreview`/`FinishDisappearPreview`/`_awaitingDisappearFinish`)は不要と判断し削除した

### 追記（2026-09-12、CanvasEditor に専用の確認用シーン切り替えを追加、配置ボタンと統合）

- **経緯**: Canvas Editor は「今開いているシーンで OpenData する」(旧「確認用シーンで開く」)のみで、VFX/Anim のような専用シーン切り替えが無かった。UI は Screen Space - Overlay で 3D ライティングに依存しないためこれ自体は妥当だが、シーンに既にユーザー自身の Canvas 等が配置されていると重なって見分けが付かない(ユーザー報告)
- **対応**: `CanvasPreviewSceneSetup`(`VfxPreviewSceneSetup` と同構造)を新設し、`Assets/GameData/PreviewScenes/CanvasPreviewScene.unity`(EventSystem のみの空シーン)に切り替えられるようにした
- **ボタン統合(同日、ユーザー指示)**: 当初は「確認用シーンを開く」(切り替えのみ)と「ここに配置」(OpenData)を別ボタンにしたが、切り替えたら必ず置きたいだけで手間なだけだった。1 ボタン(`OpenPreviewSceneAndPlace`)に統合し、`CanvasPreviewSceneSetup.OpenOrCreate()` が `bool` を返して保存ダイアログでキャンセルされた場合は続けて OpenData しないようにした(2026-09-14: メニュー用の `void OpenOrCreate()` と、ウィンドウが使う `internal bool TryOpenOrCreate()` に分離)
- **EventSystem が作られないバグ修正(同日)**: 当初は入力モジュールの判断を `EditorApplication.ExecuteMenuItem("GameObject/UI/Event System")` に任せていたが、この呼び出しはフォーカス/選択状態次第で何も作らずに黙って失敗することがあり、実際に EventSystem 自体が入らないシーンができてしまった(ユーザーの手元で発生・確認)。`DDrive.Editor` から `Unity.InputSystem` を直接参照する(asmdef 変更)のは避けたいため、リフレクションで `InputSystemUIInputModule` を探して `EventSystem` に付ける方式に変更(`CanvasPreviewSceneSetup.AddEventSystem`)。見つからない場合は警告ログのみで例外にしない(TL;DR #4)
- 実装: `Assets/DDrive/Editor/Preview/CanvasPreviewSceneSetup.cs`、`Assets/DDrive/Editor/Canvas/CanvasEditorWindow.cs`(`OpenPreviewSceneAndPlace`)

### 追記（2026-09-12、Navigation グラフの矢印が向きが分かりづらいバグを修正）

- **経緯**: 矢印を描く `_arrowLayer` はノードの箱より先に(＝背面に)追加していたため、矢印はノード中心まで描いていても、その終端(矢頭そのもの)が丸ごとノードの箱の下に隠れて見えなかった。さらに A→B の Down と B→A の Up のように同じ 2 ノードを結ぶ逆向きのリンクが両方あると、中心同士を結ぶ直線が完全に重なって色でしか区別できなかった(ユーザー報告)
- **対応**(`NavigationGraphView.cs`): `ClipToBoxEdge` でノードの箱の境界の手前まで線を引っ込め、隙間の中に矢頭がはっきり見えるようにした。`ComputeParallelOffset` で同じ 2 ノードを結ぶ逆向きのエッジを進行方向と垂直に(パスの文字列比較で決めた向きに)ずらし、2 本の平行線として見えるようにした。矢頭のサイズも 9x5 → 13x7 に拡大
- ヒットテスト(`TryFindWireNear`、Reroute point 追加位置)は従来どおりノード中心同士の直線を使う(見た目の調整のみで判定ロジックは変えない)
  - 2026-09-14(レビュー対応): 描画・当たり判定・カット判定が同じ形(`BuildDisplayPolyline`: 平行ずらし + 箱の手前での打ち切り)を使うように統一した。ノードの箱が重なって打ち切り後の向きが逆になる端は打ち切らない(以前は重ねると矢印が逆向きになった)

### 追記（2026-09-12、ElementFx 行に直接再生(▶/⏸/■)を追加）

- **経緯**: ElementFx(Appear/Idle/Disappear)の見た目を確認するには UI Tween Editor を開く必要があったが(直接指定(UiTweenData)があるときだけ)、プリセット指定のときはそもそも開けず、確認手段が無かった。「Canvas Editor 内で再生・一時停止・停止まで完結したい」という要望を受けた
- **対応**: `CanvasEditorWindow.BuildPhasePlaybackRow` を追加。各行に「▶ 再生」「⏸ 一時停止/▶ 再開」「■ 停止」を置き、プリセット指定・直接指定(UiTweenData)のどちらでも、確認用シーンの実要素(ElementPath で解決した RectTransform)に対して実 `UiTweenManager` で再生する(ADR-4 のまま。埋め込みで独自の再生経路は作らない)。プレビュー未表示なら自動で開く(`PlacePreview`)。プリセットは `UiPresetFactory.Build` でトラックへ変換してから `UiTweenManager.PlayTracks` に渡す
- **一時停止 API を追加**: `UiTweenManager` に Handle 単位の `SetPaused`/`IsPaused` を追加(`AnimManager.SetPaused` と同じ設計。2026-09-14 訂正: ゲームのポーズ `OnPause` と同じ `Paused` フラグを使うため独立ではなく、`PauseWithGame` の Tween はゲームのポーズ解除でこの一時停止も解ける)
- Handle は `(ElementPath, Phase)` をキーに `CanvasEditorWindow._phasePreviewHandles` で保持し、`OnEditorUpdate` から毎フレーム状態(再生中/一時停止/停止中)をボタンとラベルに反映する。対象の CanvasData を切り替えたとき、および確認用プレビューを閉じたときにクリアする(パス文字列が別データで偶然一致して誤表示することを避けるため)
- 直接指定を編集する既存の「▶」ボタンは「✎ Tween Editor」に改名(新しい「▶ 再生」と役割が紛らわしくなるため)。挙動は変えていない
- EditMode 340/340・PlayMode 488/488 green、`execute_code` でプリセット/直接指定の両経路・一時停止トグル・停止時の Handle 破棄を確認済み

### 追記（2026-09-12、ElementFx を全要素まとめて再生 / 各行を折りたたみ表示に）

- **一括再生**: 「▶ 全 Appear」「▶ 全 Idle」「▶ 全 Disappear」「■ 全て停止」を ElementFx 割当セクションの先頭に追加(`PlayAllPhasePreview`/`StopAllPhasePreview`)。登録済みの全要素のうち、その区間に割り当て(プリセット or 直接指定)がある要素だけをそれぞれの設定でまとめて再生する。1 行ずつ「▶ 再生」を押す手間を無くすのが目的で、内部的には既存の行内再生(`PlayPhasePreview`)をループで呼ぶだけ
- **折りたたみ**: 要素数が多いと縦に長くなりすぎるため、各要素の箱を `Box` から `Foldout` に変更し、デフォルトを折りたたみ状態にした。展開状態は `ElementPath` をキーに `_elementFxExpanded` で保持し、他の行の編集で全体が再構築されても開閉が飛ばないようにしている
- EditMode 340/340・PlayMode 488/488 green、`execute_code` で一括再生(割り当て済みの行だけ再生される)・一括停止・Foldout がデフォルト折りたたみであることを確認済み

### 追記（2026-09-14、コードレビュー対応: Canvas Editor の後片付け・Undo）

- **閉じる / 閉じて開き直すで演出が残る**
  - `RemovePreview` は行ごとの直接再生を止めてから `UiManager.StopAll` → `UiTweenManager.StopAll` の順に止める。以前は Handle を捨てるだけだった。
  - そのため、プールに戻った要素の上で Idle ループが動き続け、次に開いた Appear とぶつかっていた。
- **ウィンドウを閉じても UI Root が残る**
  - `OnDisable` で `UiManager.DestroyEditorRoot()`(Edit Mode 専用。新設)を呼ぶ。
  - `OnEnable` で `[D-Drive] Canvas Preview` の残骸を `EditorPreviewRoots.DestroyAll` で消す。
  - プレビューのルートは `EditorPreviewRoots.CreateRoot` で作る。
- **「確認用シーンを開く」の押し直し**
  - 先に `RemovePreview` する。すでに確認用シーンにいるときはシーンを開き直さず、表示だけやり直す。
  - `OnActiveSceneChanged` でも両 Manager を止めて UI Root を破棄する。以前はシーンの読み直しで実体だけが消え、`UiManager` に中身の無いインスタンスが残っていた。
- **Undo**
  - Undo/Redo で ElementFx の割り当て一覧と検証も作り直す。
  - 行ごとの読み書きは範囲を確認する。以前は行が減った後の ▶ で範囲外の例外が出た。
- **行ごとの ▶**:再生前にその要素の Tween を全部止める(`UiTweenManager.StopAll(RectTransform)`)。自動の Idle と混ざらない。
  - 残る制約: UiManager は Appear が止まったのを完了とみなし、次の Tick で Idle を始める。
- **一括再生**
  - 「▶ 全〜」は開けなかったら 1 回で打ち切り、成功数を表示する。
  - 暗黙の自動収集(`CollectSelectables` / `CollectElementFx`)は差分があるときだけ書き込む(▶ のたびに Undo が積まれアセットが dirty になっていた)。
- **UiManager**:プレースホルダ(Prefab 未設定)の Canvas を Edit Mode で閉じたときは `DestroyImmediate` を使う(`Destroy` はエラーになる)。
- **UI Tween Editor**
  - 実要素で再生する前に、`ApplyTrack` が書き込む全項目を保存する(位置・sizeDelta・拡大率・回転・CanvasGroup の有無と alpha・色・fillAmount)。
  - 停止・完了・対象の切り替え・閉じるときに元へ戻す。自動で付いた CanvasGroup も外す。
  - Prefab アセットは対象外。
  - ▶ の連打は前の再生を止めてからやり直す。
  - Undo/Redo で一覧を作り直す。
  - 「完了」の表示は、再生中から終了に変わった 1 回だけ出す。
- **重複の整理**
  - 相対パスは `TransformPath.GetRelative` に統一した(docs/24 整理項目 6)。
  - 「(ルート)」表記は `UiTweenEditorWindow.RootElementLabel` に統一した。
  - 同名の兄弟要素には「 #2」を付けて区別する。
  - `FindUiTweenData` は Id → アセットのキャッシュを持つ。

### 実装メモ（2026-09-14、5-11 ImportRule）

> `Assets/SourceAssets/Canvas/<カテゴリ>/*.prefab` に UI 用 Prefab を置くだけでも `CanvasData`(Prefab のみ設定、Layer/Transition/Wiring 等は既定値)が自動生成される（[09_editor_tools.md](09_editor_tools.md) §1.1）。元ファイル削除時は Data を消さず、A-4 の既存 Validator の「Prefab が未設定(または Missing)です」がそのまま欠落表示を担う。

### 実装メモ（2026-09-17、U-19 Canvas + Panel の一発生成）

**「CanvasData を作るたびに Canvas を作って Panel を足して…」を 1 操作にまとめた。** 入口は `Tools > D-Drive > Generate > Canvas + Panel と CanvasData を作成` と `GameObject > D-Drive > Canvas + Panel(CanvasData も作成)`（Hierarchy 右クリック）の 2 つで、どちらも `CanvasSetupService.CreateCanvasWithPanel`（`Editor/Canvas/CanvasSetupService.cs`）を呼ぶ。

- 命名・カテゴリの入力と CanvasData の作成は既存の `NewAssetDialog`（種別を CanvasData に固定）→ `AssetCreationService.Create` をそのまま通す（並行経路を作らない）。作成後のコールバックで Prefab を組み立てて `CanvasData.Prefab` に入れる（`Undo.RecordObject` + `EditorUtility.SetDirty`）
- 生成される Prefab: ルート = `RectTransform + Canvas(ScreenSpaceOverlay) + CanvasScaler(1920x1080 / Match 0.5) + GraphicRaycaster`、その子に `Panel`（`RectTransform` 四辺ストレッチ + `Image`）。A-2 の「ルートは RectTransform を持つオブジェクト、Canvas があれば `SortOffset` が `sortingOrder` に加算」に合わせてルートに Canvas を付けている（`UiManager.OpenData` が `overrideSorting = true` にして使う）
- 保存先は `Assets/GameData/Prefabs/Canvas/<CanvasData のファイル名>.prefab`。`SourceAssets/Canvas/` に置くと上記 5-11 の ImportRule が 2 つ目の CanvasData を作ってしまうため避けている
- Hierarchy から呼んだ場合のみ、作った Prefab を右クリックしたオブジェクトの子として配置する（`Undo.RegisterCreatedObjectUndo`、`EventSystem` が無ければ作る）。詳細は [09_editor_tools.md](09_editor_tools.md) §6.3

---

# Part B — 汎用 Prefab

## B-1. 要件

- 種類 / タグ / コリジョンレイヤーを管理。Spawn/Despawn/Pool/Preload

## B-2. データ構造

```csharp
public class PrefabData : AssetDataBase
{
    public GameObject Prefab;
    public PrefabKind Kind;            // Gimmick/Projectile/Pickup/Environment/...
    public string[] GameplayTags;      // ゲームロジック用タグ("Destructible"等)
    public int CollisionLayer;         // 生成時に SetLayer(再帰)
    public LodProfile Lod;
    // Flags.Pool: Projectile 等は Pooled(32, 128) を推奨
}
```

## B-3. Manager API

```csharp
public static class Prefabs
{
    public static PrefabHandle Spawn(PrefabId id, Vector3 pos, Quaternion rot);
    public static PrefabHandle Spawn(PrefabId id, Transform parent);
    public static void Despawn(PrefabHandle h);
    public static void Preload(params PrefabId[] ids);
    public static UniTask PreloadAsync(params PrefabId[] ids); // 2026-09-10 追加。下記 Codex レビュー対応を参照
}
// Handle: h.Go / h.GetComponent<T>() / h.Move() / h.HasTag("Destructible")
```

- OnSpawn/OnDestroy イベント（着地煙 VFX・出現 SE 等）はデータ側で設定可能 → 「出現演出のためだけのスクリプト」を撲滅
- ゲーム固有ロジックは従来通り Prefab 上のコンポーネントに書く（本システムは生成管理とメタ情報のみ担当）

### 実装メモ（2026-09-10、4-4 / 4-5 Prefab 側）

- **「確認用シーンに配置」の修正と共通化（U-4/U-5、2026-09-17）**: `PrefabEditorWindow` の「確認用シーンに配置」は、名前に反して確認用シーンを開かず、今開いているシーンにしか Spawn していなかった（ユーザー報告）。Model / Anim / Anim2D と同じ「片付ける → `VfxPreviewSceneSetup.TryOpenOrCreate` → 配置 → SceneView をフォーカス」に揃えた。右クリックの「このシーンに配置 / このシーンに本配置」も含め、実装は `Editor/Preview/PreviewPlacement.cs` + `PreviewPlacementButton.cs` の共通部品に集約している（[09_editor_tools.md] §2.1）。`CanvasEditorWindow` の「確認用シーンを開く」も同じ共通部品に載せ替えた（本配置は `CanvasData.Prefab` を `PrefabUtility.InstantiatePrefab` で置く）

- 実装: `Assets/DDrive/Runtime/Prefab/`（`PrefabData.cs` / `PrefabsManager.cs` / `Prefabs.cs` / `PrefabDataValidator.cs`）+ `Assets/DDrive/Editor/Prefab/PrefabEditorWindow.cs`。ModelsManager([05] A-3)と同じ設計で InstanceStore + PoolService の Rent/Return、`AssetFlags.Pool` の `Kind/InitialCount/MaxCount`（疑似コードの `PrewarmCount` ではなく実際のフィールド名 `InitialCount` を使用）、`VfxManager.SetLayerRecursively`、`LodProfile`（`Model.LodProfile` を再利用、複製しない）を踏襲
- Handle: `Handle<PrefabMarker>`。疑似コードの `PrefabHandle` は型エイリアスではなく実際にはこの Handle。`PrefabHandleExtensions` で `h.Go()` / `h.GetComponent<T>()` / `h.Move(pos, rot)` / `h.HasTag(tag)` / `h.Despawn()` / `h.IsValid()` を提供
- イベント: `PrefabsManager.Events`(EventBus) を公開し、Spawn で `Begin + Fire(OnSpawn)`、Despawn で `Fire(OnDestroy) + End` を発火する。SE/VFX/配置セットへの実配線は Bootstrap が `Anim.Events` 用とは別の 2 つ目の `AssetEventDispatcher`（`PrefabDispatcher`）を Prefabs.Events に対して生成することで行う（Anim と同じ Dispatcher に相乗りさせない。`GetContextTransform(InstanceContext)` で発火元 Instance の Transform を渡す)
- Placeholder: 未登録 ID や `Prefab` 未設定の `PrefabData` は `SpawnData` が `Prefab == null` を検出して名前 `"<Placeholder:PREFAB>"` の空 GameObject を生成する（Pool を経由しない。Despawn で `Object.Destroy`）。開発ビルド/エディタで Data ごとに 1 回だけ警告
- タイポ検出: `GameplayTagDictionary`(同 namespace, `PrefabDataValidator.cs` 内)が `ValidationContext.AllAssets` から全 `PrefabData.GameplayTags` を集めて既知タグ集合を作り、大文字小文字違い、または編集距離1以内（挿入/削除/置換のいずれか1回）の近似一致をタイポの疑いとして警告する
- テスト: `Assets/DDrive/Tests/Runtime/PrefabsManagerTests.cs`。Spawn(位置/レイヤー再帰/親子付け)、Despawn(Pooled=プール返却・再利用で同じ GameObject / None=破棄・再利用で別 GameObject)、HasTag/GetComponent、OnSpawn/OnDestroy イベント発火、未登録 ID の Placeholder 生成、Preload の無例外確認、PreloadAsync の Prewarm 確認、StopAll

#### Codex レビュー対応（2026-09-10）

> - **P1（Pool.Kind==None の意味）**: `Flags.Pool.Kind` の既定値 `None` は「プールしない」の意味だが、従来は `Despawn` が Kind に関わらず常に `IPoolService.Return` していたため、None 指定の Instance が Free に無限に貯まり続け（待機中インスタンスが永久に生き残る）、`MaxCount`/`Persistent` も効かなかった。修正後は Instance 生成自体は引き続き `IPoolService.Rent` 経由で統一(親付け/上限管理の一貫性を保つ)しつつ、`Despawn` は `Kind == Pooled` のときだけ `Return`、`Kind == None`（既定）のときは新設の `IPoolService.Discard(PooledObject)` で Active から取り除いて即座に破棄する(Play モードは `Object.Destroy`、Edit モードは `DestroyImmediate`。`IPoolable.OnReturn` は「戻って再利用される」通知なので Discard では呼ばない)。ModelsManager([05] A-3)も同じ規則。**VFX / SE(`VfxManager`/`AudioManager`)は対象外**で、Kind に関わらず常にプールする既存挙動を維持する(短命・高頻度再生のため)
> - **P2（Preload が lazy カタログで空振りする）**: 同期 `Preload` は `AssetRegistry.ResolveOrPlaceholder` を使っており、これは「既にロード済みの ID」しか実データを返さない(未解決の lazy な Addressables エントリは placeholder のまま Prewarm 対象から外れて素通りする)。`PreloadAsync(params PrefabId[] ids)` を追加し、`ResolveAsync<PrefabData>` で確実にロードしてから `Kind == Pooled` かつ `InitialCount > 0` のときだけ Prewarm する。同期 `Preload` は `TryResolveSync` を使うよう変更し、解決できなかった ID は開発ビルド/エディタで警告を出して `PreloadAsync` の利用を促す
> - テスト: `PrefabsManagerTests.Despawn_NonePolicy_DestroysGameObject_AndReuseGivesDifferentGameObject` / `Despawn_PooledPolicy_ReturnsToPool_AndReuseGivesSameGameObject` / `PreloadAsync_ResolvesLazyEntry_AndPrewarmsPool`、`PoolServiceTests.Discard_*`

#### 実装メモ追記（2026-09-11、4-13 Simulated Spawn）

- `PrefabsManager` に `INetBridge netBridge = null` を追加(既定 null はシングルプレイ相当で従来どおり常にローカル Spawn)。`Bootstrap` は `NetBridge`(`LocalLoopbackBridge`)を渡す。詳細な通信フロー・レート制限・メッセージ定義は [14_networking.md](14_networking.md) §4 の実装メモを参照
- `PrefabDataValidator` に Simulated 検証 3 種(NetworkObject 未設定 Error / Kind 不一致 Info / Pooled 併用 Warning)を追加([14] §10)
- テスト: `Assets/DDrive/Tests/Runtime/PrefabSimulatedSpawnTests.cs`(`FakeNetBridge` で IsServer/IsClient を切替できるテスト用 bridge を新設)

## B-4. Validation

Prefab Missing (Error) / CollisionLayer 未定義値 (Error) / Kind=Projectile で Pool 未設定 (Warning) / GameplayTags のタイポ検出（登録済みタグ辞書と照合, Warning） / NetMode=Simulated で NetworkObject 未設定 (Error) / Simulated で Kind が Projectile・Gimmick・Character 以外 (Info) / Simulated と Pool=Pooled の併用 (Warning)（4-13。[14_networking.md](14_networking.md) §10）

### 実装メモ（2026-09-14、5-11 ImportRule）

> `Assets/SourceAssets/Prefab/<カテゴリ>/*.prefab` に Prefab を置くだけでも `PrefabData`(Prefab のみ設定、Kind/Tags 等は既定値)が自動生成される（[09_editor_tools.md](09_editor_tools.md) §1.1）。元ファイル削除時は Data を消さず、本節の既存 Validator の「Prefab が未設定(または Missing)です」がそのまま欠落表示を担う。
