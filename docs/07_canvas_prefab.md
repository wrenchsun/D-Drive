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
}
// Handle: h.Go / h.GetComponent<T>() / h.Move() / h.HasTag("Destructible")
```

- OnSpawn/OnDestroy イベント（着地煙 VFX・出現 SE 等）はデータ側で設定可能 → 「出現演出のためだけのスクリプト」を撲滅
- ゲーム固有ロジックは従来通り Prefab 上のコンポーネントに書く（本システムは生成管理とメタ情報のみ担当）

## B-4. Validation

Prefab Missing (Error) / CollisionLayer 未定義値 (Error) / Kind=Projectile で Pool 未設定 (Warning) / GameplayTags のタイポ検出（登録済みタグ辞書と照合, Warning）
