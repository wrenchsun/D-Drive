# 移行ガイド（migrations）

関連: [../42_distribution.md](../42_distribution.md) §4（更新の取り込みフロー）・§5.12（破壊的変更をどうしても行う場合の手続き）/ [../../CHANGELOG.md](../../CHANGELOG.md)

## これは何か

D-Drive（`com.ddrive.core`）が **MAJOR バージョン**を上げるとき（互換面を破壊するとき）、対象バージョンごとに 1 本の移行ガイドをこのフォルダに置く。

- ファイル名: `vN.md`（例: `v2.md` = 2.0.0 への移行ガイド）
- 雛形: [TEMPLATE.md](TEMPLATE.md) をコピーして書く
- **CHANGELOG.md の「破壊あり」に必ずこのファイルへのリンクを張る**（[../42_distribution.md](../42_distribution.md) §4.1）

## いつ作るか

[../42_distribution.md](../42_distribution.md) §5.12 の手続きに従う:

1. issue/設計メモに「何を・なぜ・代替案（2 段階で回避できないか）」を書き、ユーザー承認を取る
2. 少なくとも 2 回の MINOR で `[Obsolete]`・Warning・移行ツール（`IDataMigration`/`IProjectMigration`）を**先に出す**
3. MAJOR で削除する回に、このフォルダへ `vN.md`（[TEMPLATE.md](TEMPLATE.md) 準拠）を追加する
4. 持ち込み先（最初は MS2026）で実際に §4.2 の更新手順を実施し、詰まった点をこのガイドに追記する

## 発効前の注意（2026-09-20 時点）

互換性ポリシー（[../42_distribution.md](../42_distribution.md) §5）は **P チケット完了（P-13）まで発効していない**。発効前に行った「最後のチャンス」の整理（§5.13。例: `KnownPrefixes` の追加）は破壊的変更の手続きを踏まず、[CHANGELOG.md](../../CHANGELOG.md) の該当バージョン節にその旨を記録するだけでよい。移行ガイド（`vN.md`）を書くのは **P-13 発効後の MAJOR** からになる。

## `next-major.md`（P-9、2026-09-20 追加）

`[Obsolete]` 付与済みのまま次の MAJOR で削除される予定の API を自動列挙した一覧。`Tools/Release/list-obsolete.ps1` が `Packages/com.ddrive.core/{Foundation,Runtime,Editor}` の `[Obsolete(...)]` を正規表現で静的に走査して生成する（手で編集しない）。MAJOR リリースの前（§5.12 手続きの前）に再実行して確認する（[../42_distribution.md](../42_distribution.md) §6 P-9）。2026-09-20 時点では該当 0 件。

## マイグレーション基盤（`DDriveMigrationRunner`）の使い方（P-7、2026-09-20）

上の「MAJOR での破壊的変更」だけでなく、**MINOR でのシリアライズ形式の型変更**（[../42_distribution.md](../42_distribution.md) §5.1「型変更は条件付き MINOR」）でも、実データの値を新形式へ移すのに使う。実装は `Packages/com.ddrive.core/Editor/Migration/`。

### スキーマ版（`SchemaVersion`）とは

`AssetDataBase.SchemaVersion`（`Version`＝保存回数とは別物）は「今の D-Drive コードが期待するデータ形式の版」を表す単調増加の整数。`VersionStampProcessor` が保存の都度 `DDriveSchema.Current`（`Foundation/Data/DDriveSchema.cs`）を書き込む。既存 `.asset` は `0` のまま読まれ、「1.0.0 以前の形式」を意味する。`SchemaVersionValidator`（Warning、Code `DD-SCHEMA-OUTDATED`）が `SchemaVersion < DDriveSchema.Current` の Data を知らせる。

### `IDataMigration` の書き方

```csharp
namespace DDrive.Editor.Migration
{
    public sealed class MyFieldTypeChangeMigration : IDataMigration
    {
        public string Id => "2026-xx-xx-myfield-float-to-valuedef"; // 一度公開したら変えない
        public int FromSchema => 1; // 対象にする Data の SchemaVersion(この値未満は対象外)
        public int ToSchema => 2;   // 適用後に書き込む SchemaVersion(通常は DDriveSchema.Current)

        public bool AppliesTo(AssetDataBase data) => data is MyData; // 型・フィールドの状態で絞り込む

        public void Migrate(AssetDataBase data, MigrationContext context)
        {
            var d = (MyData)data;
            d.NewValueDefField = ValueDef.Constant(d.OldFloatField); // 旧フィールドから新フィールドへ値を移す
            context.Note($"{d.name}: OldFloatField({d.OldFloatField}) -> NewValueDefField");
            // SchemaVersion 自体はここで書かない(Runner がまとめて ToSchema を書く)。
        }
    }
}
```

書き方の制約（[../42_distribution.md](../42_distribution.md) §4.3・§5.1 と対応）:

- **旧フィールドは消さない**。`[HideInInspector] [Obsolete]` を付けて次の MAJOR まで残す（同一 MAJOR 内ならダウングレードしても旧コードが旧フィールドを読める、§4.4）
- **`Migrate` の中で `SchemaVersion` を書かない**。`DDriveMigrationRunner.Apply` が適用後にまとめて `ToSchema` を書く
- **登録リストは無い**。`IValidator` と同じく `TypeCache` で自動発見される（クラスを書くだけでよい）。ただしテストアセンブリ（`DDrive.Tests.*`）内の実装は実運用の発見（`DiscoverDataMigrations`/`DiscoverProjectMigrations`、`CI.MigrateCheck` を含む）から除外される
- **`Id` は一度公開したら変えない**。`DDriveProjectSettings.AppliedMigrationIds` に記録され、CHANGELOG・移行ガイドからも参照する識別子になる

`AssetDataBase` を持たない SO（`AssetCatalog`・`TuningTable`・`UiLayerSettings` 等）の一括処理は `IProjectMigration { string Id; void Migrate(MigrationContext); }` を使う。対象ごとの `SchemaVersion` が無いため、二重適用の防止は `DDriveProjectSettings.AppliedMigrationIds` に `Id` が記録済みかどうかで判定する。

### Runner の動かし方

- **ドライラン**: `Tools > D-Drive > Update > マイグレーション(ドライラン)`。実データは一切変更せず、対象件数と一覧をコンソールに出す（`DDriveMigrationRunner.Plan` は読み取りのみ）
- **適用**: `Tools > D-Drive > Update > マイグレーション(適用)`。`Undo.RecordObject` → `Migrate` → `SchemaVersion` 書き込み → `EditorUtility.SetDirty` を対象ごとに行い、`VersionStampSuppression.Scope()` 内で `DDriveAssetSave.SaveAllSuppressed()` する（＝マイグレーションの適用は `Version`〔保存回数〕を進めない）。Ctrl+Z で戻せる
- **CI**: `DDrive.Editor.CI.MigrateCheck`（`-executeMethod DDrive.Editor.CI.MigrateCheck`）が未適用のマイグレーションを検知して exit 1 にする。`Tools/CI/run-ci.cmd` の `[1/7]` として `ValidateAll` より前に実行する
- 適用順は `FromSchema` の昇順。対象は `SchemaVersion < ToSchema && AppliesTo(data)` を満たす Data だけ（二重適用は自然に防止される。`SchemaVersion` が既に `ToSchema` 以上なら対象外になるため）

## 変更履歴

- 2026-09-20: 新規作成（P-2）。ドキュメントのみ、実際の移行ガイド（`vN.md`）はまだ無い。
- 2026-09-20（P-7）: マイグレーション基盤（`DDriveMigrationRunner`）の使い方・書き方の制約を追記。
