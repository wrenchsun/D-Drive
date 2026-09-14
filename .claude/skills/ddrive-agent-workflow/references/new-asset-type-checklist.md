# 新しい AssetType を追加する手順（詳細チェックリスト）

この手順は 2026-09-14 に実装された `Shake`（CameraShake）/ `Haptics` 種別（コミット `7ca7afe` 5-2、`4afe4e1` 5-2b、`eace5e5` 5-2c）を実際に手本にしている。関連コミットは `git log --oneline --all | grep -iE "5-2|shake|haptic"` で確認できる。

新種別を作る前に、必ず似た既存種別（新種別が「アセット参照を持つ」なら Vfx、「アセットを持たず ID だけの薄い設定値」なら Shake/Haptics、「複数種別を束ねる」なら Presentation）の実装を一式読んでから始める（`CLAUDE.md` §3「grep してから書く」）。

## 1. AssetType enum

`Assets/DDrive/Foundation/Identity/AssetType.cs`

- `enum AssetType : byte` の**末尾に追加するだけ**。既存値の並び替え・挿入・削除は禁止（YAML に整数値で永続化されるため、既存アセットの種別が壊れる）
- コメントで追加理由・日付・対応する設計 doc を書く（既存の書式を真似る。例: `// [16_camera_haptics.md]: ... 2026-09-14 追加。`）

## 2. Data クラス

新規ファイル（例: `Assets/DDrive/Runtime/<種別フォルダ>/<種別>Data.cs`）。手本: `Assets/DDrive/Runtime/Camera/CameraShakeData.cs`、`Assets/DDrive/Runtime/Haptics/HapticsData.cs`。

```csharp
[CreateAssetMenu(menuName = "D-Drive/<カテゴリ>/<種別名> Data", fileName = "<PREFIX>_New<種別名>")]
[AssetIdDefinition(AssetType.X, typeof(XMarker), "XID")]
public class XData : AssetDataBase
{
    // 調整パラメータは ValueDef で定義する（生の float + AnimationCurve にしない）
    // フィールドには [Tooltip("...")] を付ける
}

public struct XMarker { } // AssetId<XMarker> の型引数専用。新規に作る
```

- `[AssetIdDefinition]` を付けるだけで `Editor/Codegen/AssetIdGenerator.cs` が TypeCache 反射で自動収集する。**他に ID 生成の登録作業は不要**（`AssetIdGenerator.FindDefinitions` が `AssetIdDefinitionAttribute` を持つ `AssetDataBase` 派生型を全アセンブリから探す）
- `AssetIdGenerator.KnownPrefixes`（同ファイル内）にプレフィックス文字列を追加する（`ToConstantName` の検査用。無くても動くが、種別接頭辞の一貫性チェックに含めるなら追加）
- Data クラスの XML doc コメントは public API のため必須（[`../../../docs/12_review.md`](../../../docs/12_review.md) §3）

## 3. Manager + Handle/Instance + 静的ファサード

- Manager: `IAssetManager` 実装（`Tick(dt)` / `OnPause` / `StopAll` / `OnSceneUnload`）。命名規約 `Play/Spawn(id, ctx) → Handle`、`Stop(handle)`。手本: `Assets/DDrive/Runtime/Haptics/HapticsManager.cs`（2 モーター合成の例）、`Assets/DDrive/Runtime/Camera/CameraFxManager.cs`（Trauma 合成の例）
- Handle: `Handle<XMarker>`（世代付き struct、既存の `Handle<T>` 汎用型を再利用。種別ごとに新しい Handle 型を作らない）
- 静的ファサード: 手本は `Assets/DDrive/Runtime/Camera/CameraFx.cs`。`Bind(Manager)` / `IsBound` / 各 API を `_instance?.Foo() ?? デフォルト値` で no-op フォールバックする（Bind 前・未 Bind 時に例外を出さない）
- Manager が同期 API のみで ID を解決する場合（`ResolveOrPlaceholder<T>`、ほぼ全種別がこれ）、対象 Data が `Flags.Load = Preload` でない限り初回参照時は必ず Placeholder になる（[`../../../docs/02_core_framework.md`](../../../docs/02_core_framework.md) §4）。→ 手順 6 で Preload 既定にする

## 4. Validator

新規ファイル（例: `Assets/DDrive/Runtime/<種別フォルダ>/XDataValidator.cs`）。手本: `Assets/DDrive/Runtime/Camera/CameraShakeDataValidator.cs`。

```csharp
public sealed class XDataValidator : IValidator
{
    public AssetType Target => AssetType.X;
    public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
    {
        if (data is not XData x) yield break;
        // 種別固有の検査。ValueDef の汎用検査（Curve未設定・Duration<=0 等）は
        // 既存の ValueDefValidator に任せて重複させない
    }
}
```

- `public` かつ引数無しコンストラクタが必須（`Editor/Validation/CI.cs` の `DiscoverValidators()` が `Activator.CreateInstance` で TypeCache 反射生成するため）。**登録リストは無い**ので、クラスを置くだけで AssetBrowser の Validation ⚠ に載る
- テストは `Assets/DDrive/Tests/Editor/XDataValidatorTests.cs`

## 5. AssetNamingService（ファイル名・フォルダ規約）

`Assets/DDrive/Editor/AssetBrowser/AssetNamingService.cs`

```csharp
public static string GetTypePrefix(AssetType type) => type switch
{
    ...
    AssetType.X => "XPREFIX",  // ファイル名先頭の接頭辞（大文字、既存と衝突しないもの）
    ...
};

public static string GetTargetFolder(AssetType type) => type switch
{
    ...
    AssetType.X => "X",  // GameData/ 配下のサブフォルダ名
    ...
};
```

人はファイル名・配置フォルダを意識しない（AssetBrowser が自動生成する。[`../../../docs/10_workflow.md`](../../../docs/10_workflow.md) §3）。この switch を追加しないと `_ => "ASSET"` / `_ => "Misc"` にフォールバックしてしまう。

## 6. AssetCreationService（作成フロー・カタログ・Preload 既定）

`Assets/DDrive/Editor/AssetBrowser/AssetCreationService.cs`

- カタログ名マッピングの switch にケースを追加（既存カタログを再利用するか新規カタログ名にするかを決める。例: `AssetType.Shake or AssetType.Haptics => "CameraFxCatalog"` のように 1 カタログを複数種別で共有してもよい）
- **Preload 既定の判定**: Manager が手順 3 の同期解決のみなら、Preload 既定にする種別の switch/条件に追加する（既存例: `assetType == AssetType.Canvas || assetType == AssetType.ControlSkin || assetType == AssetType.Presentation || assetType == AssetType.Shake || assetType == AssetType.Haptics` のような条件式。コメントで理由を書く）
- カタログアセット自体（`Assets/GameData/Catalogs/XCatalog.asset`）は AssetBrowser の新規作成 or `Generate/Addressables 登録を同期` で作る。手でカタログ .asset を作らない

## 7. DDriveRuntimeBootstrap への配線

`Assets/DDrive/Runtime/Loop/DDriveRuntimeBootstrap.cs`

- Awake 相当のブロックで `X = new XManager(Registry, ...)` を生成し `loop.Register(X)`
- 静的ファサードの Bind（`Runtime.X.X.Bind(X)`）。Teardown 側で逆順に Unbind
- 非標準の Tick 系列（Unscaled dt 等）が必要なら `UnscaledCameraFxAdapter` と同じ「`IAssetManager` に橋渡しするアダプタ」パターンを使う（`loop.Register` するのはアダプタ側）
- `OptionStore` 等、オプション画面から `GlobalScale` 相当を触る設計なら、初期化子への配線もこのタイミングで追加する
- テスト: `Assets/DDrive/Tests/Runtime/RuntimeBootstrapTests.cs` に組み立て・Bind/Unbind の確認を追加

## 8. 専用エディタ

- 既存の近い種別のウィンドウに相乗りできるなら 1 ウィンドウで複数 Data 型を扱う（手本: `CameraFxEditorWindow` が `CameraShakeData`/`HapticsData` を 1 ウィンドウで扱う。`AudioEditorWindow` の SE/BGM も同じ設計）。新規ウィンドウが必要なら `Assets/DDrive/Editor/<種別フォルダ>/XEditorWindow.cs` を作る
- `CreateGUI()` は必ず `ScrollView`（`flexGrow=1`）を `rootVisualElement` に追加してから内容を積む（[`../../../docs/09_editor_tools.md`](../../../docs/09_editor_tools.md) §7）
- `public static Open(XData)` に `[DataEditor(typeof(XData), "Xで開く")]` を付ける（Inspector 最上部「エディターで開く」ボタンが自動で付く。§8）。**忘れると `Tests/Editor/DataEditorRegistryTests.cs` が検出する**（対応表に無い concrete `AssetDataBase` 派生型は `Exempt` に理由付きで明示する必要がある）
- メニュー登録は `DDriveMenu` 定数経由のみ（`Assets/DDrive/Editor/Menu/DDriveMenu.cs`）。文字列直書き禁止
- プレビューはウィンドウ内描画にしない。手本: `SceneCameraShakePreviewDriver`（実 Manager で開いているシーンの `Camera.main` を直接揺らす）、`EditorHapticsPreviewDriver`（実 Manager 経由でパッドを振動）。確認用シーンのセットアップは `CameraShakePreviewSceneSetup`（`VfxPreviewSceneSetup` と同じ流儀）を手本にする + `Tools/D-Drive/Editors/` に「確認用シーンを開く」メニューを追加
- 波形・カーブ編集は種別独自のエディタを作らず `ValueDefDrawer` の `PropertyField` をそのまま使う（読み取り専用の重ね描き表示だけ独自に作ってよい。手本: `WaveformGraphGui`）
- プリセットが要るなら `XPresets`（Undo 対応、手本: `CameraFxPresets`）

## 9. テスト

- `Assets/DDrive/Tests/Editor/`: `XDataValidatorTests.cs`、専用エディタのプレビュードライバのテスト（Fake 実装で非ゼロ出力→0 復帰などの後始末確認）
- `Assets/DDrive/Tests/Runtime/`: `XManagerTests.cs`（PlayMode。Pause/StopAll/OnSceneUnload への応答、Handle の世代チェック、Pool の Return/Discard 等）
- 新規 `.cs` を書いたら Unity に `.meta` を生成させる（手で作らない）
- EditMode + PlayMode の両方が green になったことを確認する（[`../../SKILL.md`](../SKILL.md) §2 の検証ループ参照）

## 10. docs 更新（同じ PR で）

- 種別の設計 doc（既存 doc に節を追加するか、新規 `docs/0X_*.md` を作る）に「実装メモ（日付、チケット番号）」を追記
- [`../../../docs/02_core_framework.md`](../../../docs/02_core_framework.md) §14: Bootstrap 配線の追記（既存の「**2026-09-14 追記(5-2/5-2b)**: …」のような書式）
- [`../../../docs/09_editor_tools.md`](../../../docs/09_editor_tools.md) §6: メニュー構成のツリーに追加。§8: `[DataEditor]` 対応表に追記
- [`../../../docs/10_workflow.md`](../../../docs/10_workflow.md) §3: `AssetNamingService.GetTypePrefix` の表に接頭辞の例を追記（人が読む命名規約の一覧）
- [`../../../docs/11_tasks.md`](../../../docs/11_tasks.md): 対応するチケット行の AC 右に「→ ✅ 実装（要約）: …」を追記
- デザイナーが実際に触る機能なら `docs/DesignerManual/<page>.html` を新設（既存ページのページ構成「概要 / 開き方 / はじめの一歩 / 画面の説明 / 設定項目 / よくある使い方 / よくある警告 / 注意点 / 関連ページ」に合わせる）+ `docs/DesignerManual/Readme.html` からリンク

## 11. Addressables / カタログの整合確認

- 作成した Data・カタログが Addressables に登録されていること（`AssetCreationService.Create` が自動でやるが、既存データを後から種別変更した場合は `Generate/Addressables 登録を同期` を実行）
- `Tools/D-Drive/Validation/Run All` を実行してエラー 0 を確認（`AddressablesRegistrationValidator` が未登録を検出する）
- テスト実行前後で Addressables グループ 2 ファイル（`Assets/AddressableAssetsData/AssetGroups/*.asset`）の `git diff` が増えていないことを確認する（本文 [`../../SKILL.md`](../SKILL.md) §2 参照）

## 12. 任意: 他システムとの統合（該当する場合のみ）

新種別が使う Unity パッケージや、他のサブシステムと接続する場合は追加で以下が要る（Shake/Haptics 追加時に判明。汎用手順ではないので新種別ごとに必要性を判断する）。

- **新しい Unity パッケージ（例: Input System）を Manager/Editor で直接使う場合**、`Assets/DDrive/Runtime/DDrive.Runtime.asmdef` / `Assets/DDrive/Editor/DDrive.Editor.asmdef` の `references` にそのパッケージのアセンブリ名を追加する（例: `"Unity.InputSystem"`）。asmdef 変更は忘れやすいので、コンパイルエラー（型が見つからない）が出たら真っ先に確認する
- **新種別を Presentation トラックの 1 種として再生できるようにする場合**、`Assets/DDrive/Runtime/Presentation/PresentationManager.cs` / `PresentationTrack.cs` にトラック種別を追加し、`Assets/DDrive/Editor/Presentation/PresentationTrackKindMapping.cs` にもマッピングを追加する（[08_presentation.md] 参照）
- **その種別のプレビューが Presentation 経由のプレビュー（`ScenePresentationPreviewDriver` 等）から解決される場合**、`Assets/DDrive/Editor/Preview/EditorAnchorRegistry.cs` に登録が無いと `ResolveOrPlaceholder` が常に Placeholder を返す（Bgm/CameraShake/Haptics で実際に踏んだ問題。手本: コミット `6517d50`）
- **オプション画面（音量・振動・画面揺れの強さ等）から `SetGlobalScale` 相当を操作可能にする場合**、`Assets/DDrive/Runtime/Ui/OptionStore.cs` に該当 `OptionKey` を追加し、起動時に Bootstrap から反映する
