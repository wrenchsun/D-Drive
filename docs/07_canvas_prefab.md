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

- **症状**: `ButtonWire`/`SliderWire`/`UiLayerSettings` を正しく設定していても、実機/Play で `Ui.Open` した Canvas が常に `"<Placeholder:CANVAS>"` になり、レイヤー既定 Skin(4-7)も一切効かないことがある(手動確認シート [23_manual_verification_2026-09-11.md](verification/23_manual_verification_2026-09-11.md) 相当の検証で発覚)
- **原因**: `Ui.Open`/`Ui.Popup` は `AssetRegistry.ResolveOrPlaceholder`、`UiManager.ApplyLayerDefaults`(4-7 の `UiLayerSettings.DefaultButtonSkin` 解決)は `TryResolveSync` を使うが、両方とも「既に `_loaded` キャッシュにあるものしか返さない」同期専用の解決であり、`Flags.Load` の既定値 `LazyLoad`(「初回参照時にロード」のはずの非同期パス)を経由しない。`_loaded` に乗るのは `Flags.Load=Preload` でカタログ登録時にプリロードされたものだけ。B-3([07] 実装メモ 2026-09-10 の Codex レビュー P2)で `PrefabsManager.Preload` に対して一度直した同種の罠が、`CanvasData`/`ButtonSkinData` 側には残っていた
- **対応**: `AssetCreationService.Create()`(`Assets/DDrive/Editor/AssetBrowser/AssetCreationService.cs`)で `AssetType.Canvas`/`AssetType.ControlSkin` を新規作成するときは既定 `Flags.Load=Preload` にする。既存アセット向けに `AddressablesRegistrationValidator`(`Assets/DDrive/Editor/Validation/AddressablesRegistrationValidator.cs`)へ「対象 AssetType で `Flags.Load != Preload`」を Error + FixAction(`Flags.Load=Preload` に書き換えて保存)として追加し、`Validation > Run All` で拾えるようにした
- 今後 `SliderSkinData`(4-17 で型追加時)等、`UiInteractable`/`UiManager` の同期解決に乗る新しい `ControlSkinData` 派生を増やす場合は、`AssetCreationService` の分岐と `AddressablesRegistrationValidator.NeedsPreload` の両方に追記すること

### 追記（2026-09-17、Validator 側の対象リストに Presentation/Shake/Haptics が抜けていたのを修正）

- **経緯**: U-20(Anim/Anim2D の SE/VFX 不発、上記と同種のバグ)の対応で `AddressablesRegistrationValidator.NeedsPreload` に Anim/Anim2D を追加した際、`AssetCreationService.Create()` の既定 Preload 化には元々入っていた `Presentation`/`Shake`/`Haptics`(2026-09-14、5-1/5-2/5-2b で追加)が Validator 側に反映されていないことが判明した。`PresentationManager.PlayData`/`CameraFxManager.ShakeData`/`HapticsManager.PlayData` はいずれも `ResolveOrPlaceholder` のみで同期解決するため、同じ穴(既存アセットが `Flags.Load=LazyLoad` のままでも Validation で検出できない)が空いていた。実データ(`Assets/GameData/Presentation/**`、`Assets/GameData/Camera/Demo/SHAKE_Demo_DemoHitSmall.asset`、`Assets/GameData/Haptics/Demo/HAPTIC_Demo_DemoHitPunch.asset`)を確認したところ幸い全て `Flags.Load=Preload` 済みで、実害は出ていなかった
- **原因**: 対象 AssetType の一覧が `AssetCreationService.Create()` と `AddressablesRegistrationValidator.NeedsPreload` の 2 箇所に別々に持たれており、片方だけ更新して追加漏れが起きる事故がこれで 4 回目(Canvas/ControlSkin → Presentation/Shake/Haptics → Anim/Anim2D → 今回の Presentation/Shake/Haptics 検出漏れ)だった
- **対応**: 判定を `AssetCreationService.NeedsPreloadDefault(AssetType)` という 1 つの public static メソッドに一本化し、`Create()` と `AddressablesRegistrationValidator.Validate()` の両方がこれを呼ぶように変更した(対象種別の追加は今後この 1 箇所で済む)。テストは `Assets/DDrive/Tests/Editor/AddressablesRegistrationValidatorTests.cs` に `PresentationAsset_FlagsLoadRegression_IsDetectedAndFixed` 等を追加

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

### バグ修正（2026-09-17、U-23: ElementFx の「▶ 再生」を連打すると位置がずれる）

- **症状**: SlideIn 系などを割り当てた ElementFx 行の「▶ 再生」を、前の Tween が終わる前に連打すると、要素の最終着地位置が本来の位置から少しずつずれていく。Canvas Editor を閉じて開き直す(=確認用シーンを作り直す)と正しい位置に戻る
- **真因**: `PlayPhasePreview` は連打時に `UiTweenManager.StopAll(elementTarget)` → `UiPresetFactory.Build` で同じプリセットを取り直す、という手順を踏む。`UiPresetFactory.Build` の SlideIn 系は `target.anchoredPosition`(= 呼び出し時点の "現在位置")を新しい Tween の静止位置(To)としてそのまま採用する設計だが、修正前の `StopAll(RectTransform target)` には `Stop(handle, complete)` のような完了引数が無く、中断された Tween を最終値へ進めずに Instance を取り除くだけだった。そのため連打で割り込まれた瞬間の(オフスクリーンと静止位置の中間の)位置がそのまま次の Tween の "静止位置" として採用されてしまい、連打するたびに本来の位置からずれていった。「開き直すと戻る」のは、確認用シーンを作り直すことで Prefab に保存された正しい位置から Instance が再生成されるため
- **対応**: `UiTweenManager.StopAll(RectTransform target, bool complete = false)` に `complete` 引数を追加し(`Stop(handle, complete)` と同じ規約。既定 `false` は既存の挙動を維持)、内部実装も `Stop(handle, complete)` を呼ぶように統一した。`UiFx.StopAll(RectTransform, bool)` にも同じ引数を追加。`CanvasEditorWindow.PlayPhasePreview` の呼び出しを `StopAll(elementTarget, complete: true)` に変更し、連打で中断された Tween を必ず最終値へスナップしてから次の `cur` を読み直すようにした
- **テスト**: `Assets/DDrive/Tests/Runtime/UiTweenTests.cs` に `RapidReplay_SlideInPreset_WithStopAllComplete_SettlesAtRestPosition`(3 回連打しても本来の静止位置に収束することを確認。修正前は `StopAll(rt)`(complete 引数なしの当時の唯一のシグネチャ)で連打すると `restX=0` に対し実測 `-328.05` に着地しており、赤であることを確認済み)と `StopAll_WithoutComplete_LeavesTargetAtInterruptedPosition`(complete=false の既定動作を固定)を追加
- 実装ファイル: `Assets/DDrive/Runtime/UiTween/UiTweenManager.cs`(`StopAll`)、`Assets/DDrive/Runtime/UiTween/UiFx.cs`(`StopAll`)、`Assets/DDrive/Editor/Canvas/CanvasEditorWindow.cs`(`PlayPhasePreview`)

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

### 実装メモ（2026-09-17、U-21 Canvas Editor で要素の移動）

**症状**: `CanvasEditorWindow` に要素（RectTransform）を移動する手段が無かった。「確認用シーンを開く」で置くプレビュー実体は `UiManager.OpenData` が生成する Prefab リンク無しの実体（ADR-4 の確認用プレビュー）で、そこを直接動かしても `CanvasData.Prefab` には反映されない。

**対応**: ウィンドウ内に実 UI を描画・編集する専用機構を新設するのは 2026-09-10 の owner instruction（ウィンドウ内描画を避け、確認用シーン / Prefab を開いて SceneView で確認する）に反するため、既存の `ModelEditorWindow.OpenPrefab` / `VfxEditorWindow.OpenPrefab` と同じ導線（`AssetDatabase.OpenAsset(prefab)` でプレハブモードを開く）を Canvas Editor にも追加した。

- ツールバーに「Prefab を開く(要素の移動)」ボタンを追加。押すとプレビュー実体を `RemovePreview()` で片付けてから `CanvasData.Prefab` をプレハブモードで開く
- ElementFx の各要素の Foldout 先頭に「選択して移動(Prefab を開く)」ボタンを追加。対象 Prefab がプレハブモードで開いていなければ自動で開き、`PrefabStage.prefabContentsRoot.transform.Find(elementPath)` でその要素の GameObject を選択・`PreviewPlacement.Focus` で SceneView へフォーカスする
- 実際の移動・回転・リサイズは Unity 標準の Move/Rotate/Rect ツールで行う。プレハブモードは通常のシーン編集と同じ Undo 機構に乗るため、Ctrl+Z でそのまま戻せる（独自の Undo コードは不要）
- 実装: `Assets/DDrive/Editor/Canvas/CanvasEditorWindow.cs`（`OpenPrefab` / `SelectElementForMove`）。新しい共通部品は増やしていない（既存の `RemovePreview` / `PreviewPlacement.Focus` を再利用）

### 追記（2026-10-03、Canvas の埋め込み(入れ子)対応）

**背景**: D-Drive に「Canvas の入れ子」の概念が無く、Hud の Prefab の中に Option の Prefab を入れても「1 枚の大きな Prefab」として扱われ、親を Open したとき子の `CanvasData`（ElementFx・ボタン配線等）は使われなかった。子の演出を親の中で効かせるには、親の `CanvasData` に親から見た長いパス（`OptionRoot/Panel/BtnX`）で行を書くしかなく、同じ子を別の親に入れるたびに書き直しになっていた。**決定（ユーザー 2026-10-03「おすすめで作ってみて」）**: 埋め込み Canvas（親の中に子 Canvas として登録し、子の設定は子の `CanvasData` に 1 か所で持つ）を軸に、Canvas Editor の一覧のグループ表示と、選択に合わせた編集対象の自動切り替えを足す。

**データ（追加のみ）**: `CanvasData` の末尾に `EmbeddedCanvas[] EmbeddedCanvases`。`EmbeddedCanvas`（`[Serializable]` struct）= `string RootPath`（親 Prefab ルートからの相対パス = 子のルート。`root.Find` 基準）+ `AssetId<CanvasMarker> Canvas`（子の `CanvasData`。`Open(CanvasId)` と同じ型。設計では `ButtonWire.Target` と同じ `AssetRef` に合わせる案だったが、Inspector のピッカーが Canvas 種別に絞られる `AssetId<CanvasMarker>` にした）。既定は空 = 従来どおり。`SchemaVersion` は上げない。

**実行時（`UiManager.OpenData`、Open の最後に 1 回）**: `EmbeddedCanvases` の各要素について `RootPath` で子のルートを見つけ、子の `CanvasData` を `AssetRegistry.TryResolveSync` で同期解決（Canvas は Preload 前提）し、**子の `ElementEffects` / `Buttons` / `Sliders` を子のルート基準のパスで適用する**。子の Prefab は Instantiate しない（親 Prefab に既に入っている実体を使う）。適用先は親の `CanvasInstance` と同じ（ElementFx は同じ `ElementFx` リストに乗るので Appear の入力ゲート・Close の Disappear 待ち・Idle の停止・プール返却時の後始末は親のものがそのまま子の分も働く。配線の購読解除も同じリスト）。入れ子の入れ子は浅い段から順に辿る（深さ上限 8 + 循環検出 = 警告 1 回 + その埋め込みだけ打ち切り）。`EmbeddedCanvases` が空なら何もしない（割り当ても無い）。子を**単独で** Open する従来の使い方は変えない（同じ `CanvasData` を単独でも埋め込みでも使える）。

**親のものを使うもの / 子のものを使うもの**:

| 項目 | 埋め込み時にどちらを使うか |
|---|---|
| 子の `ElementEffects` / `Buttons` / `Sliders` | **子のもの**（子のルート基準で適用。次の優先順位あり） |
| `Navigation` / `FirstSelected` / `NavigationNodeLayout` ほか | 親のもの（子の設定は無視。親の `Navigation` に長いパスで書く） |
| レイヤー既定（Skin・SE・Appear/Disappear の既定） | 親の `Layer` のもの |
| Canvas 全体の開閉演出（`OpenTransition` / `CloseTransition`）・`CloseOnBack`・`ModalBlocksInput`・`PauseGameWhileOpen` | 親のもの |
| `Layer` / `SortOffset` / `Flags`（プール方針等） | 親のもの |

**優先順位（親の行が勝つ。担当の単位 = 2026-10-03 レビュー PC-R-08 で確定）**: 1 つの要素の設定は 1 回だけ適用する。先に担当した側が勝ち、後の側の同じ設定は適用しない。**担当の単位**は次のとおり。

| 種類 | 担当の単位 | 例 |
|---|---|---|
| `ElementFx` | **要素**（1 行 = Appear / Idle / Disappear の組） | 親に `OptionRoot/Panel` の行があれば、子の `Panel` の行は（Appear だけ・Idle だけでも）使われない |
| `ButtonWire` | **(要素, トリガー)**（`Click` / `DoubleClick` / `LongPress` / `Repeat`） | 親が `OptionRoot/BtnX` の `Click` だけ配線していれば、子の同じ要素の `Click` は使われないが、子の `LongPress` の配線は使われる |
| `SliderWire` | **(要素, トリガー)**（`Changed` / `Commit` / `NotchPassed` / `LimitReached`） | ボタンと同じ |

**優先順は 2 つの別の規則（2026-10-04 ラウンド 3、レビュー FX-R-10 で整理）**:

| | 規則 | 対象 | 内容 |
|---|---|---|---|
| (A) | **外側が勝つ** | Open した `CanvasData` 自身の行・配線 vs 埋め込みの子の設定 | Open した `CanvasData` 自身の行 > 浅い入れ子の子 > 深い入れ子の子（外側ほど強い = 孫 < 子 < 親）。親の行は、その要素が見つからなくても（適用できなくても）担当として数える。子の設定を使う場所ごとに上書きできる。**これが正しい使い方**（Hud → Option → Volume のように、子が自分の入れ子を持つ形も同じ規則） |
| (B) | **内側（具体的）が配下を担当する** | 同じ親の中で**重なる登録**（`OptionRoot` と `OptionRoot/Inner` の両方を親の `EmbeddedCanvases` に書く） | **設定の誤り**（Validator が `DD-CANVAS-EMBED-NESTED-ROOT` の Warning）。実行時は壊れないよう、`RootPath` が深い方（より具体的な登録）がその配下の要素を先に担当する。= 既存データ（長いパスの行）はそのまま動く。(A) とは別の規則で、(B) の中での優先は「外側が勝つ」ではない（`OptionRoot/Inner` 配下の要素では、`OptionRoot` の Option の行より `OptionRoot/Inner` の Volume の行が先）。重なる登録を直す（Option 自身が `Inner` に Volume を埋め込む形にする）と、(A) の規則に戻る |

上の 2 つのどちらでも、1 つの要素（ボタン / スライダーは (要素, トリガー)）は 1 回だけ適用される。実装は Open 時に作る担当表（`UiManager.EmbedClaims`。Open した Canvas のルート基準のパス + トリガーの集合）で、同じ要素に配線・演出が二重に付かない（ボタン 1 回で `OpenCanvas` / `SendSignal` が 2 回走らない）。子の `ElementPath` が空（子のルート自身）の行は従来どおり対象外（`root.Find("")` は null）。子ルート自身を動かしたいときは親側に `RootPath` を `ElementPath` にした行を書く。

**入れ子の入れ子と重なる登録の関係**: 入れ子の入れ子は、**子の `CanvasData` 自身が `Inner` を埋め込みとして持つ**形（Hud → Option → Volume）が基本。親（Hud）の `EmbeddedCanvases` に `OptionRoot` と `OptionRoot/Inner` の両方を登録すると同じ要素に Option と Volume の設定が重なる。実行時は上の (B) のとおり 1 回だけ適用する（内側の登録が先）が、Validator が `DD-CANVAS-EMBED-NESTED-ROOT`（Warning）で知らせ、Canvas Editor の「入れ子 Prefab から検出」は、他の候補・登録済みの埋め込みルートの**配下**にある入れ子 Prefab（= 入れ子の入れ子）を提案しない（その子 Canvas 自身の Canvas Editor で登録する。手で「+ 手動で追加」することは妨げない）。Unity の `PrefabUtility.IsAnyPrefabInstanceRoot` は、親 Prefab の中の入れ子の入れ子の Prefab インスタンスでも真になる（実際に確認した）ので、この除外が要る。

**子の `ButtonWire` / `SliderWire` のアクションの意味（埋め込み時）**: 子の配線は「親を Open した `CanvasInstance` のハンドル」に対して実行される。

| アクション | 埋め込み時の意味 |
|---|---|
| `OpenCanvas` | 変わらない（`Target` の Canvas を開く。親・子に依存しない） |
| `CloseSelf` | **埋め込み先の親（開いている Canvas）を閉じる**（確定。子は単独のハンドルを持たないため）。「子のパネルだけ閉じる」ではない（子のパネルだけ閉じたいときは親側のロジックか、親の `Navigation`/表示切り替えで行う） |
| `CloseTop` | 変わらない（スタック最上位を閉じる） |
| `SendSignal` | `SignalArgs.Canvas` = 親のハンドル（Open した Canvas）、`SignalArgs.ElementPath` = **配線を持っている子 `CanvasData` のルート基準のパス**（その子を単独で Open したときと同じ値。入れ子の入れ子では孫のルート基準）、`SignalArgs.EmbeddedRootPath` = **Open した Canvas のルートから見た、その子の埋め込みルートのパス**（入れ子の入れ子は最外のルートからの連結 `OptionRoot/Inner`。親自身の配線・単独で Open したときは空文字）。ボタンもスライダーも同じ。受け手は埋め込みでも単独でも同じ書き方（`Key` / `ElementPath`）で動き、どこに埋め込まれたかが要るときだけ `EmbeddedRootPath` を使う |
| `PlayPresentation` | 変わらない（Phase 5 で実装予定の警告） |
| `SetOption`（スライダー） | 変わらない（`OptionStore` との直結。Open 時の初期値設定も子のスライダーに行われる） |

**フェイルソフト**: `RootPath` が親 Prefab に見つからない / 子の `CanvasData` が未設定または Registry に無い（Preload されていない）/ 循環 / 深さ超過 → 警告 1 回（親 `CanvasData` ごと・設定ごと）+ その埋め込みをスキップ。子のデータ内のパスが見つからない行は既存どおり警告 1 回 + その行だけスキップ。いずれも例外では止めず、他の行・他の埋め込みは適用する。

**ネット・ContentHash への影響**: なし。Canvas はローカル表示の前提（`CanvasData` はネット同期の対象ではなく、ContentHash は `CatalogEntry`（Id/Type/Address/NetMode）だけから作られるため、`CanvasData` の欄が増えても変わらない — `CatalogContentHasher` の定義を確認済み）。

**Validator（新規コードの Warning / Info のみ。既存の重さは変えない）**: `CanvasDataValidator` に `DD-CANVAS-EMBED-ROOT`（`RootPath` が親 Prefab に無い）/ `-DUP`（同じ `RootPath` の重複）/ `-UNSET`（子が未設定）/ `-SELF`（自分自身）。2026-10-03 のレビュー対応で `-NESTED-ROOT`（埋め込みが重なる = 一方の `RootPath` が他方の配下）と `-PATH-FORM`（`RootPath` の書式: 先頭・末尾の `/`・`\`・`//`・`./`）を足した。Editor の `CanvasEmbeddedValidator`（`Editor/Canvas/`。他アセットを引く検査）に `DD-CANVAS-EMBED-MISSING`（子の CanvasData が見つからない）/ `-CYCLE`（入れ子をたどると親に戻る）/ `-PREFAB`（`RootPath` の実体が子の `CanvasData.Prefab` のインスタンスでない）/ `-NOT-PRELOAD`（子の `CanvasData` が Preload でない = 親を Open した時点で読み込まれていないとスキップされる）/ `-NESTED-ROOT`（子 Canvas が自分で埋め込んでいる場所が、親の別の登録と重なる）、Info `-OVERRIDE`（親と子が同じ要素を指す = 親の設定が優先）。

**Canvas Editor（`CanvasEditorWindow` 内。新しい EditorWindow は作らない）**: 純ロジックは `Editor/Canvas/CanvasEmbeddedEditing.cs` に分けた（EditMode でテスト）。

- **埋め込み Canvas セクション**: 登録済みの行（`RootPath` / 子の `CanvasData` / 「この Canvas を編集」/ 削除）と、親 Prefab の中の入れ子 Prefab インスタンスのうち元 Prefab が既存の `CanvasData.Prefab` と一致する未登録のもの（「入れ子 Prefab から検出」）に「埋め込みとして登録」ボタン。「+ 手動で追加」も可。`Undo.RecordObject` + `SetDirty`。
- **一覧のグループ表示**: ElementFx の一覧を「親の要素」と「埋め込み: 子の名前(RootPath)」に分けて折りたたみ可能にした（埋め込みが無い CanvasData は従来どおりの平らな一覧）。埋め込みグループには子の `ElementEffects` を**読み取り表示**（行数と各行の Appear/Idle/Disappear の要約）し、親が上書きしている行に `[親で上書き]` を付ける。「この Canvas を編集」で編集対象を子に切り替える。埋め込みルート配下を指す親の行は親の一覧に「↳ 親での上書き: …」として出す。ElementFx 見出しの下に絞り込み（要素のパスの部分一致）を追加した。
- **自動収集**: 登録済みの埋め込みルートの**配下**の要素は親の自動収集（「要素を自動収集」・確認用プレビューを開いたときの自動収集・「一括適用: 全ボタンに反映」）に入れない（子の `CanvasData` の担当。埋め込みルート自身は親の要素として集める）。既に親にある行は消さない（上書きとして残る）。未登録の入れ子 Prefab は従来どおり拾う。
- **登録時の「親での上書き」の整理（2026-10-06、U-29a）**: 登録の前に親で「要素を自動収集」を押すと入れ子の中の要素も親の行として集まり、登録後は「親での上書き」として子の設定に勝ってしまう（優先の規則 (A)。中身が空の行でも要素単位で勝つ）。そこで、埋め込みとして登録するとき（検出からの登録・RootPath 欄 / 子 CanvasData 欄の変更）と、登録済みの埋め込みの「上書きをまとめて整理…」で、親の `ElementEffects` のうち**埋め込みルートの配下（ルート自身は除く）**の行を整理する。**中身が既定のままの行**（`ElementPath` 以外の全欄が `default` = 自動収集されただけ）は確認なしで取り除き、**設定が入っている行**があれば 1 回確認（「取り除く(子の CanvasData の設定を使う)」/「残す(親での上書きとして残る)」/「キャンセル(登録しない)」）。登録と整理は 1 つの Undo グループ。親の `Buttons` / `Sliders` の配線は自動収集されないので削除せず、件数をステータスに出すだけ。詳細は [39](archive/39_usability_fixes_2026-09-17.md) 2026-10-06 追記。
  - **2026-10-06 追記（レビュー [62] GD-R-09）**: 親の「中身が空の行」は、子の演出をこの親の中でだけ止めるための上書きとしても使える（規則 (A)）。自動収集で集まっただけの行と区別できないので、(1) **登録の操作**（検出からの登録・RootPath / 子の欄の変更）は従来どおり、既定のままの行だけなら確認なしで取り除く。わざと空の行を置いて子を止めたい場合は、**登録の後に**置く。(2) 利用者が自分で押す**「上書きをまとめて整理…」は、既定のままの行だけのときも、対象の行のパスを並べて 1 回確認する**（取り除く / キャンセル）。(3) 設定のある行の確認の本文にも、一緒に取り除く既定のままの行のパスを並べる（件数だけにしない）。(4) RootPath / 子の欄は、値が変わらないとき（`OptionRoot/` → `OptionRoot` のように正規化して同じになる入力・同じ子の選び直し）は整理も確認もしない。
- **編集対象の切り替え**: 親から子へ切り替えると「← <親の名前> へ戻る」と「埋め込みとして編集中: Hud > Option」が出る（親の連なり = `_ancestors`。入れ子の入れ子も外側から内側への連なりで保持）。ツールバー下の「選択に追従」トグル（既定オン、EditorPrefs `DDrive.CanvasEditor.FollowSelection`）: Hierarchy / プレハブステージ / 確認用プレビューで選んだ GameObject が埋め込みルート配下なら編集対象を子の `CanvasData` に、配下でなければ親に切り替え、該当の ElementFx 行を展開してスクロール・青い縦線で強調する。**このウィンドウ自身が選んだもの（行の「選択」・▶ 再生時の自動選択）には反応しない**（親での上書き行の「選択」で子へ切り替わらないように）。このウィンドウの入力欄（テキスト・数値）にフォーカスがあるあいだは切り替えない（入力中の値を失わない）。ウィンドウの外（Hierarchy など）へフォーカスが移ってから切り替わるときは、入力途中（遅延確定）の欄を**切り替え前の Canvas に確定してから**切り替える（各欄の確定は作ったときの対象に書く。別の `CanvasData` には書かれない）。別の Canvas の Prefab をプレハブモードで開いて要素を選んだときも、その Canvas に切り替わる（埋め込みを使わない場合も同じ）。🔒 ロックが ON のときも切り替えない。
- **プレビュー再生（▶）・「選択」・「選択して移動」**: 子を編集対象にしたまま、(a) 親の Prefab のプレハブモード、(b) 親の確認用プレビュー（子を編集中は親の連なりの最外側を `OpenData` する = UiManager が子の設定を本番と同じに適用する）、(c) 子自身のプレハブモード / プレビュー、のどれでも動く。子のパスは親の実体の中の位置へ変換して解決する（`CanvasEmbeddedEditing.ToAncestorPath`）。以前の「対象 CanvasData の Prefab とステージの `assetPath` が完全一致のときだけ」は「対象の Prefab、または対象を埋め込んでいる親の Prefab」に広げた。「選択して移動」は、対象自身か親のステージが既に開いていればそのまま使い（子のプレハブモードから親へ勝手に戻さない）、どちらも開いていなければ対象自身の Prefab を開く。再生前の状態の保存 / 復元は既存の `ElementFxStateSnapshot` を使う。確認用プレビューの実体が対象と無関係な Canvas のものなら使わず、▶ 時に開き直す。パッド操作シミュレーション（▲▼◀▶決定）は、プレビューに開いている `CanvasData` 自身を編集しているときだけ有効。
- **プリセットギャラリーの「選択中のシーン要素のパスを使う」**: 以前は常に「シーン階層の最上位（`selected.root`）」からのパスだったため、確認用プレビュー（UiManager が `[D-Drive] UI Root/<レイヤー>/<Canvas>` の下に置く実体）で選ぶと `HUD/<Canvas名>/…` のように CanvasData のルート基準にならなかった（**実在した問題**。`CanvasEmbeddedEditing.FindCanvasRoot` のテストで再現・修正を確認）。Canvas のルート（プレハブステージのルート / プレビュー実体の 3 段目 / `CanvasData.Prefab` のインスタンス）からのパスにし、見つからなければ従来どおり最上位。埋め込みルート配下の要素を選んだ場合は適用先を子の `CanvasData` に切り替えて子基準のパスにする。

**互換**: 追加のみ（MINOR）。`EmbeddedCanvases` が空 / null の既存データは実行時・Editor とも従来どおり（既存テスト無改修）。公開 API の追加: `EmbeddedCanvas`・`CanvasData.EmbeddedCanvases`・`EmbeddedCanvasPaths`（`DDrive.Runtime.Ui`）。

**不採用・将来の候補**: (C) 子をスロットへ別 Canvas として Open する方式（スロット Transform に子 Canvas の Prefab を Rent する。親の Prefab に子を入れ子で置く必要が無くなるが、Open 時の生成・Close の連動・Handle の持ち方が増える）、(E) ElementFx のコンポーネント化（要素の GameObject に `UiElementFx` コンポーネントを付けデータを Prefab 側に持つ。パス文字列が要らなくなるが、Data 駆動・デザイナーの一覧編集という方針とぶつかり、既存データの移行も要る）。いずれも今回は見送り。

---

# Part B — 汎用 Prefab

### 追記（2026-10-06、Canvas Editor の「ボタンの配線」欄）

`CanvasData.Buttons`（ButtonWire）を Canvas Editor の画面で編集できるようにした（Editor のみ。データ形式・実行時の動作は変えない）。それまでは Inspector の `Buttons` 配列に、パス・トリガー・アクション・対象の ID を手で書いていた。

- **一覧**: Prefab 内の `UiButton` を全部並べる（配線の無いボタンも出る。ここから「+ 配線を追加」で足す）。Prefab に無いパスを指す配線は、注意を付けた行として残す（勝手に消さない）。
- **1 本の配線**: トリガー → アクション、アクションに応じた欄（`OpenCanvas` = 開く Canvas を `CanvasData` で選ぶ / `SendSignal` = キー）、クリック SE（`SeData` で選ぶ）、削除。アクションに関係のない欄は出さない。`AssetRef` / `AssetId` の ID はアセットを選ぶ形で入れる（数値を書かせない）。
- **追加の既定**: トリガーはそのボタンでまだ使っていないもの（Click → DoubleClick → LongPress → Repeat の順）、アクションは None。
- **その場の注意**: 対象の未設定・キーの未設定・同じボタン + 同じトリガーの重複・`SetOption`（スライダー専用）を行の下に出す（Validation の欄にも従来どおり出る）。`PlayPresentation` は実行時に未対応なので、その旨を行に出す。
- **埋め込み**: ElementFx の一覧と同じグループ分け。親自身のボタン / 「↳ 親での上書き」（埋め込みの配下のボタンを指す親の配線。編集できる）/ 「埋め込み: 子」（子の配線の読み取り表示と「この Canvas を編集」）。子の配線は子の CanvasData で編集する。親での上書きは要素 + トリガー単位なので、子の同じトリガーの行にだけ `[親で上書き]` が付く。重なる登録では内側（長い RootPath）の登録の配下として扱う。
- **書き込み**: 各欄は作ったときの対象（owner）に `Undo.RecordObject` + `SetDirty` で書く（選択に追従して編集対象が切り替わった後に、古い行から別の CanvasData へ書かない）。
- **実装**: ロジック = `CanvasButtonWireEditing`（`BuildGroups` / `AddWire` / `RemoveWire` / `FirstFreeTrigger` / `DescribeProblem` / `Summarize`。EditMode テスト `CanvasEmbeddedEditingTests.ButtonWires_*` 4 件）、画面 = `CanvasEditorWindow.ButtonWires.cs`（`CanvasEditorWindow` を partial にして分けた）。
- **入れていないもの**: スライダーの配線（`Sliders` / SliderWire）の編集欄（従来どおり Inspector）。配線のアクションを増やすとき（子の表示 / 非表示の切り替えなど）は、`CanvasButtonWireEditing.UsesTarget` 等と `BuildWireRow` の出し分けに足す。
- 人による確認: [43](verification/43_manual_verification_2026-09-17.md) §16 の 16-36〜16-40。
- **レビュー [63](reviews/63_review_pr126_pr129_2026-10-06.md) の対応（2026-10-06、GE-R-03〜08）**: (GE-R-05) 「親での上書き」の行の「+ 配線を追加」は、子の CanvasData がそのボタンで使っているトリガーも避ける（`FirstFreeTrigger` / `AddWire` に `CanvasLookup`。4 つとも使われていれば足さずにステータスで案内）。親での上書きが Action = None で子の同じトリガーの配線を止めているときは行に ⚠。(GE-R-06) 行の ⚠ に「対象 UiButton の LongPressSec が 0 以下」「OpenCanvas + CooldownSec が 0」を追加、上書きの行も Prefab（入れ子のインスタンスの中）に UiButton があるかを見る。`CanvasDataValidator` に Warning `DD-CANVAS-WIRE-NO-UIBUTTON`（要素はあるが UiButton が付いていない。実行時は配線されない）。(GE-R-07) Id の無い CanvasData を選んでも対象を消さず欄を前の値へ戻す。見つからない SE は「(Id 0x… の SeData が見つかりません)」を出し、全 SeData の走査は 1 回の再構築につき 1 回まで。(GE-R-08) 各欄は書く前に、その添字の配線が行を作ったときと同じ（ButtonPath + Trigger）か確かめ、違えば書かずに欄を作り直す（Inspector で `Buttons` を並べ替えた後の誤書き込みを防ぐ。ElementFx の欄は従来のまま）。(GE-R-04) RootPath 欄は正規化後の値が同じでも欄の表示を正規化後に揃える。Idle を流す注意の文言に「Prefab Variant の base に属する要素」を追加。(GE-R-03) 右クリック作成の要約は、Common が同じでもシェーダーの寄せ・固有の引き継ぎで既存 Data を書き換えたら「更新」に数える（`UnityMaterialMigrator`）。 **2 回目（GE-R-09〜13）**: `CopySpecificValues` は値が同じなら書かず引き継ぎにも数えない（変えていない .mat の再実行は「変更なし」のまま）/ 上書きの行からの追加と None の注意は入れ子の入れ子（孫）の配線も見る（`CollectWireStages`。内側が未解決なら外側へ落とさない）/ Id の無い SeData も対象を消さず欄を戻す / 範囲外になった古い行の操作も欄を作り直して知らせる / ルートの UiButton は行にせず、空のパスの配線には「ルート要素には配線できません」/ ツールバーの「対象」欄が縮む（500px で重ならない）/ 整理・変更の確認のキャンセルは「キャンセル(何も変更しない)」。人による確認: [43](verification/43_manual_verification_2026-09-17.md) §16 の 16-46・16-47。 **3 回目（GE-R-14〜18）**: 「削除」も行を作ったときの配線か確かめる / ルート要素（空のパス）の UiButton は配線できない（実行時に解決されない。Validation は従来どおり「ButtonPath '' が Prefab 内で見つかりません」の Error）。埋め込みの子の Prefab のルートをボタンにしたいときは、親の配線（パス = 埋め込みの RootPath）として Inspector で足す（親の欄は既存の配線からしか上書きの行を作らない。欄で足せるようにするかは後続） / 確認ダイアログのキャンセルの意味は呼び出し側が明示 / Canvas Editor の Validation の欄に `CanvasEmbeddedActiveValidator` を追加（16-45 で抜けが見つかった）。

### 追記（2026-10-06、埋め込んだ子 Canvas の有効 / 無効）

2026-10-06 の確認で出た要望「親で子のデフォルト enable を設定可能、作業用に一時 on/off 切り替え可能」への対応。名前と動作は 2026-10-06 に山口さんが決定（下の「決定事項」）。互換面は追加のみ（MINOR）。

**データ（末尾に追加）**

| 場所 | 欄 | 意味 |
|---|---|---|
| `EmbeddedCanvas` | `bool StartInactive` | 親を開いたとき、この子を無効（非表示）で始める。既定 false = 従来どおり有効で始まる |
| `ButtonWire` | `string EmbeddedRootPath` | `ActivateEmbedded` / `DeactivateEmbedded` / `ToggleEmbedded` の対象。その配線を持つ CanvasData のルート基準の RootPath。空 = このボタンが属している埋め込み（自分自身） |
| `UiAction` | `ActivateEmbedded = 7` / `DeactivateEmbedded = 8` / `ToggleEmbedded = 9` | 配線から埋め込みを有効 / 無効 / 切り替え |

**API**: `Ui.SetEmbeddedActive(handle, rootPath, active)` / `Ui.IsEmbeddedActive(handle, rootPath)`（`UiManager` に同名）。`rootPath` は Open した Canvas のルート基準（入れ子の入れ子は `OptionRoot/Inner` のように最外のルートから連結した形）。

**動作**

- **無効で始まる子**: ルートの GameObject を無効にし、配下の要素の Appear は始めない。親の入力ゲート（Appear が終わるまで入力を止める）の数にも入れない（無効の子が親の操作を止めない）。
- **有効化**: GameObject を有効にし、配下の要素を Open した時点の見た目に戻して Appear → Idle を始める。子の CanvasData に `FirstSelected` があればそれを選択する（親がスタックの最上位で、閉じておらず、Appear 待ちでもないときだけ。上にモーダルがある・入力ブロック中の親からは選択を奪わない。レビュー [65] GG-R-03）。親の入力は止めない。Disappear の途中で有効に戻した場合も、Appear からやり直して無効にならない。
- **無効化**: 配下の Idle を止めて Disappear を再生し、終わったら GameObject を無効にする（Disappear が無ければ即）。配下に今の選択があれば、親の `FirstSelected` へ移す（無ければ選択を外す）。Open の Appear の途中で無効にした要素は入力ゲートから外し、その場で親の入力を再計算する（GG-R-01）。子の Disappear の途中で親を Close しても、親の Disappear が終わるまで閉じ切らない（GG-R-02）。
- **入れ子の入れ子**: 外側が無効の間に内側を切り替えたときは状態だけ覚え、外側を有効にしたときに、有効な内側だけ Appear を始める。外側の Disappear の途中で内側を無効にしたときは、内側だけ先に消さず外側の完了で一緒に無効にする（GG-R-08）。同じ親の中で `A` と `A/B` の両方を登録する設定の誤り（`DD-CANVAS-EMBED-NESTED-ROOT`）での外側 / 内側の扱いは保証しない。
- **Prefab 側の状態との関係**: 登録済みの埋め込みは、データ（`StartInactive`）が Prefab 側の有効 / 無効に勝つ（Prefab で無効にしてあっても、`StartInactive` がオフなら開いたときに有効になる）。プールへ返すときに Open した時点の状態へ戻し、次の Open はデータから決め直す（前回の切り替えを持ち越さない）。未登録の入れ子 Prefab には触らない。**ゲームコードは登録済みの埋め込みのルートを直接 `SetActive` せず、`Ui.SetEmbeddedActive` を使う**（直接切り替えた状態はプール返却で戻る）。
- **配線**: 親の配線は `EmbeddedRootPath` に登録済みの RootPath を書く（入れ子の入れ子は `OptionRoot/Inner` のように連結。Canvas Editor の欄でも選べる）。子の配線で空にすると「自分が属する埋め込み」（子の「閉じる」ボタンで自分を隠す）。子を単独で開いているとき（属する埋め込みが無い）は警告 1 回 + 何もしない。**`ButtonWire.EmbeddedRootPath`（切り替える対象。その配線を持つ CanvasData のルート基準）と `SignalArgs.EmbeddedRootPath`（送り手が属する埋め込み。Open した Canvas のルート基準）は同じ名前だが基準が違う**ので、子の配線の値をそのまま `Ui.SetEmbeddedActive` に渡さない（GG-R-10）。埋め込みの RootPath を Canvas Editor で変えると、同じ CanvasData の配線の `EmbeddedRootPath`（旧 RootPath とその配下）は新しい RootPath に付け替わる（GG-R-06）。`SliderWire` でこれらのアクションを選んでも何も起きない（Warning `DD-CANVAS-SLIDER-EMBED-ACTION`。GG-R-09）。
- **例外で止めない**: 登録されていない rootPath・無効なハンドル・閉じている途中の Canvas は、警告（1 回）+ no-op。

**Editor**

- 埋め込み行に「無効で始める」（`StartInactive`。Undo 対応）と「表示 / 非表示(作業用)」（保存しない。確認用プレビューでは実 `UiManager.SetEmbeddedActive` を呼ぶので子の Appear / Disappear も再生される。プレハブモードでは `SceneVisibilityManager` で SceneView の表示だけを切り替え、Prefab を汚さない）。実装 = `CanvasEditorWindow.EmbedActive.cs`。
- 「ボタンの配線」欄: 3 つのアクションのとき、対象の埋め込みを選ぶ欄（先頭「(このボタンが属する埋め込み)」= 空、以下は登録済みの RootPath）が出る。
- RootPath / 子の変更（`ChangeEmbedWithCleanup`）と、検出からの登録での子の差し替え（`Register`）は、その行の `StartInactive` を保つ。
- レビュー [65](reviews/65_review_pr133_embedded_active_2026-10-06.md) の対応（2026-10-06）: GG-R-01〜03・05〜10 は上記のとおり。見送り = GG-R-08 (2)（同じ親に `A` と `A/B` を登録する設定の誤りは既存の Warning で検出する）、GG-R-12 (2)（警告キーの文字列生成。定常経路ではない）、GG-R-12 (3) DesignerManual（2026-10-06 に canvas-editor / canvas-data へ追記済み）。GG-R-12 (1) はツールチップの文言を「もう一度押す / 目のアイコンで戻る」に変えた（ステージを閉じたときに戻るかは 16-43 で確認）。

**Validation（新規。Warning のみ。既存の検査の重さは変えない）**: `CanvasEmbeddedActiveValidator`

| コード | 内容 |
|---|---|
| `DD-CANVAS-WIRE-EMBED-UNKNOWN` | 配線の `EmbeddedRootPath` が、その CanvasData の EmbeddedCanvases に登録されていない |
| `DD-CANVAS-EMBED-FIRSTSELECTED-INACTIVE` | `FirstSelected` が、無効で始まる埋め込みの配下にある |
| `DD-CANVAS-SLIDER-EMBED-ACTION` | `SliderWire` の Action が埋め込みのアクション（スライダーでは何も起きない） |

**テスト**: PlayMode `EmbeddedCanvasTests` に 13 件（無効で始まる・有効化 / 無効化の演出・演出なし・Prefab 側が無効・開き直し・入れ子・配線 2 件、レビュー [65] 対応で入力ゲートの復帰・Disappear 途中の Close・Disappear 途中の再有効化・FirstSelected の選択条件・外側の Disappear 中の内側の無効化）、EditMode に 5 件（配線の欄のロジック・検査・`StartInactive` の保持 2 件・入れ子の入れ子のパス）。

**決定事項（2026-10-06、山口）**

1. 名前は上記のとおり確定: `StartInactive` / `SetEmbeddedActive`・`IsEmbeddedActive` / `EmbeddedRootPath` / `ActivateEmbedded`・`DeactivateEmbedded`・`ToggleEmbedded`（以降は互換性ポリシーの対象。改名しない）。
2. 配線のアクション 3 つを入れる。
3. 有効化のとき、子の `FirstSelected` を選択する（パッド操作で子を出した直後にフォーカスが親に残らないようにする、の推奨案を採用）。
4. 登録済みの埋め込みは、データ（`StartInactive`）が Prefab 側の有効 / 無効に勝つ。

人による確認: [43](verification/43_manual_verification_2026-09-17.md) §16 の 16-41〜16-45。

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

### 追記（2026-10-03、レビュー [54] PC-R-04/06/08/18 の対応 — 埋め込みの確定仕様）

- **担当の単位・優先順・重なる登録**は上の「優先順位」のとおり確定（ElementFx は要素単位、ボタン / スライダーは (要素, トリガー) 単位。優先の規則 (A) 外側が勝つ・(B) 重なる登録は内側が配下を担当（設定の誤り）。実行時は 1 要素 1 回）。
- **`SendSignal` の `ElementPath` は子のルート基準**（以前は親ルート基準で、受け手が「単独で開いたとき」と「埋め込まれたとき」の両対応を迫られた）。埋め込みの位置は新しい `SignalArgs.EmbeddedRootPath` に分けた（`SignalArgs` に欄と 5 引数のコンストラクタ、`UiManager.SendSignal` に 5 引数のオーバーロードを追加。既存の署名は変えていない = 追加のみ）。v1.4.0 のタグ前の変更で、v1.3.1 以前の挙動（親自身の配線・単独 Open）は変わらない。
- `CloseSelf`（子の配線）= 開いた親を閉じる、のまま（確定）。
- `EmbeddedCanvasPaths`（`Combine` / `TryToChildPath` / `IsJoinedPath`）は **internal 化**（`DDrive.Runtime` の公開 API に汎用の文字列ユーティリティを残さない）。`IsJoinedPath` は担当表方式で不要になり削除。Editor は同じ規則の internal 複製（`Editor/Canvas/EmbeddedPaths.cs`）を持ち、両者の一致はリフレクションのテストで固定。
