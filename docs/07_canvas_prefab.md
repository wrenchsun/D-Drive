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
- 未実装(後続チケット): UiSlider 本体([18])、ElementFx(4-9)、CanvasEditor のノードグラフ・ゲームパッド入力シミュレーション(4-3)。`SliderWire` は現時点ではデータのみ
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

## B-4. Validation

Prefab Missing (Error) / CollisionLayer 未定義値 (Error) / Kind=Projectile で Pool 未設定 (Warning) / GameplayTags のタイポ検出（登録済みタグ辞書と照合, Warning）
