# Extension Template — D-Drive を変えずに新しい種別を足す雛形

D-Drive（`com.ddrive.core`）のソースを一切変えずに、持ち込み先のプロジェクトだけで新しい種別（Data + Manager + 専用エディター …）を足す、**最小のプラグイン 1 式**です。種別 `Sample`（ID 定数クラス `SMPID`、ファイル接頭辞 `SMP_`）が入っています。設計は D-Drive リポジトリの `docs/1014_extension_platform.md`、契約は `docs/42_distribution.md` §5.14 E-25〜E-29。

## 何が入っているか

```
ExtensionTemplate/
  README.md                         この説明
  Documentation~/sample-editor.md   「？」が開くマニュアルページ(IManualPageProvider が宣言)
  Runtime/                          asmdef Sample.Extension.Runtime(DDrive.Foundation / DDrive.Runtime 参照)
    SampleData.cs                   Data + [AssetIdDefinition(AssetType.Extension, ..., "SMPID", KindKey = "Sample")]
    SampleKind.cs                   IAssetKindProvider(接頭辞 SMP・フォルダ・カタログ名・Preload・Order)
    SampleManager.cs                IAssetManager + IExtensionAssetManager(Play → Handle、Tick で経過時間、StopAll)
    Samples.cs                      静的ファサード(Bind / IsBound / Play / Stop。未 Bind は no-op)
    SampleModule.cs                 IDDriveModule(Manager を作って GameLoop に載せ、ファサードを Bind)
    link.xml                        IL2CPP のコード除去(Managed Stripping)で module が消えないようにする(アセンブリ名を付け替えたら直す)
    SampleDataValidator.cs          IKindValidator(Code は SMP- 接頭辞)
  Editor/                           asmdef Sample.Extension.Editor(DDrive.Editor 参照。Editor 専用)
    SampleEditor.cs                 UxEditorWindow<SampleData> 派生 + FieldGuide + ▶(UxPreviewDriverBase + EditorModuleHost)
    SampleSamples.cs                ISampleProvider(空状態の「サンプルを作る」)
    SamplePreviewScene.cs           IPreviewSceneProvider(確認用シーン。シーンは同梱しない)
    SampleManual.cs                 IManualPageProvider(外部のマニュアルページ)
```

## コピーして使う

1. `Packages/com.ddrive.core/Samples~/ExtensionTemplate/`（Package Manager の Samples タブの Import でも、フォルダごとコピーでも可）を、プロジェクトの **`Assets/_Project/DDriveExtensions/<Name>/`** へコピーする（例: `Assets/_Project/DDriveExtensions/LightCue/`）。`Samples~` は Unity から見えない置き場なので、コピーするまでビルドにも Validation にも混ざらない。
2. 名前を付け替える（下の一覧）。付け替えたらコンパイルが通ることを確認する（`Tools > D-Drive > Validation > Run All` も 1 回）。
3. `Tools > D-Drive > Generate > Regenerate Asset IDs` で ID 定数(`<Name>ID.Xxx`)を生成する。AssetBrowser（`Tools > D-Drive > Asset Browser`）の「＋ 新規作成」に新しい種別が出るので、Data を作る → ダブルクリックで専用エディターが開く → ▶ で自分の Manager が動く。
4. 空状態の「サンプルを作る」を押すと、`ISampleProvider` が作った Data が命名規則どおりに保存され、カタログと Addressables に登録される。

## 名前の付け替え箇所

| 何を | どこ | 例（`Sample` → `LightCue`） |
|---|---|---|
| 種別キー（他の種別と重ならない文字列） | `SampleKind.Key`（`SampleData` の `KindKey =`、各 provider の `KindKey` はこれを参照） | `"LightCue"` |
| ファイル名の接頭辞 | `SampleKind.Prefix` | `"CUE"`（`CUE_Xxx.asset`） |
| ID 定数クラス名 | `SampleData` の `[AssetIdDefinition(..., "SMPID", ...)]` の第 3 引数 | `"CUEID"`（`CUEID.Xxx`） |
| ID の型（マーカー） | `SampleMarker`（`SampleData.cs`）。`Handle<…>` / `AssetId<…>` が使う | `LightCueMarker` |
| フォルダ・カタログ | `SampleKind.Folder` / `CatalogName` | `"LightCue"` / `"LightCueCatalog"` |
| クラス名・ファイル名 | `Sample*` すべて（**ScriptableObject はクラス名 = ファイル名**。`SampleData` は `SampleData.cs`） | `LightCueData` / `LightCueData.cs` |
| 名前空間・asmdef | `SampleExtension` / `Sample.Extension.Runtime` / `Sample.Extension.Editor`（asmdef の `name` と、Editor 側の `references` の Runtime 名） | `LightCue.Extension.*` |
| 検証 Code の接頭辞 | `SampleDataValidator`（`SMP-…`） | `CUE-…`（`DD-` は使わない） |
| メニュー・窓の名前 | `SampleEditor`（`DDriveMenu.Editors + "Sample Editor"`、`[DataEditor]` のラベル、`OpenWindow` のタイトル） | `"Light Cue Editor"` |
| 確認用シーン・マニュアル | `SamplePreviewScene.ScenePath` / `SampleManual`（key・path） | 自分のシーン / ページ |
| 昇格のとき以外は変えない | `[AssetIdDefinition]` の `AssetType.Extension` | （enum は増やさない） |

## 作り込むところ

- **Data**: `SampleData` に欄を足す。欄を足したら `SampleFieldGuide`（`SampleEditor.cs`）にも書く（書き漏れると窓に出ない）。
- **Manager**: `SampleManager` の `Tick` / `Play` に挙動を書く。定常経路（`Tick` / `Play`）に LINQ・クロージャ・boxing を書かない。`Time.time` 等を直接読まない（`Tick(dt)` の dt を使う）。例外で止めない（未登録 ID は Placeholder で動く）。
- **ファサード**: `Samples` を呼ぶ側のコードは `Samples.Play(SMPID.Xxx)` だけで動く（未 Bind なら何もしない）。
- **窓**: `SampleEditor.CreateRow` を override すると、特定の欄に `DurationBar` / `TuneKnobs` / `AnchorPad` などを載せられる（`Tests/Editor/ExternalContract/ExternalSampleEditorWindow.cs` に書き方の見本）。

## D-Drive 本体に昇格するとき

プラグインとして作った種別を D-Drive 本体の種別（`AssetType` の enum 値）にしたくなったら（D-Drive の更新が必要。MS2026 開発中は `AssetType` の追加は MINOR で、ユーザー承認が要る）:

1. D-Drive に `AssetType` の値を**末尾に**追加し、`SampleData` の `[AssetIdDefinition]` を `AssetType.Extension` から新しい値へ書き換える（`KindKey` は外す）。
2. 既存の Data は `.asset` の種別が変わるので、`CatalogEntry.Type` / `KindKey` を直すマイグレーション（`IProjectMigration`）を足す。ID（`Id`）と Address は変えない。
3. `IAssetKindProvider` を消し、命名規則・フォルダ・カタログを D-Drive 本体の表（`AssetNamingService` ほか）へ移す。Manager の配線は `DDriveRuntimeBootstrap` へ。
4. `ProgrammerManual/extending.html` の「D-Drive 本体に種別を足す手順」に従う。

昇格は必須ではない。プラグインのまま運用して構わない（D-Drive の更新で壊れないよう、公開 API は追加のみの契約）。
