# Documentation~

`DesignerManual/`・`ProgrammerManual/`・`ConsumerGuide/`・`migrations/` はここでは**生成物**です。正本は開発リポジトリ直下の
`docs/DesignerManual/`・`docs/ProgrammerManual/`・`docs/50_consumer_guide/`・`docs/migrations/`（HTML/Markdown + images + style）で、**この
Documentation~ 配下のコピーを直接編集しないでください**（次のリリースで上書き・削除されます）。

## 同期のしくみ（P-9、2026-09-20。ConsumerGuide は 2026-09-20 追加）

`Tools/Release/bump-version.ps1` が実行のたびに（バージョンが変わらないときも）以下をミラー同期します
（[docs/42_distribution.md](https://github.com/wrenchsun/D-Drive/blob/main/docs/42_distribution.md) §2.1・§4.1）:

```
docs/DesignerManual/    -> Documentation~/DesignerManual/    （コピー+上書き、消えたファイルは削除）
docs/ProgrammerManual/  -> Documentation~/ProgrammerManual/  （同上）
docs/migrations/        -> Documentation~/migrations/        （同上）
docs/50_consumer_guide/ -> Documentation~/ConsumerGuide/     （同上。導入・更新・運用の HTML ガイド、[docs/50_consumer_guide.md]）
CHANGELOG.md（リポジトリ直下）-> Documentation~/ の 1 階層上の CHANGELOG.md（Packages/com.ddrive.core/CHANGELOG.md）
```

手動で同期だけしたい場合（バージョンは上げない）は `pwsh Tools/Release/bump-version.ps1 -Version <現在の版> -SkipChecks` を実行してください。

構成:

```
Documentation~/
  DesignerManual/   … docs/DesignerManual/ の同期先(生成物)
  ProgrammerManual/ … docs/ProgrammerManual/ の同期先(生成物)
  ConsumerGuide/    … docs/50_consumer_guide/ の同期先(生成物。導入手順/更新方法/運用方法の HTML)
  migrations/       … docs/migrations/ の同期先(生成物)
  AGENTS_CONSUMER.md … 消費側エージェント向け規約(P-10 で新規作成)
  skills/ddrive-consumer/ … 消費側スキル(P-10 で新規作成)
```

`Editor/Manual/ManualPages.GetManualFolder` は、`Documentation~/DesignerManual`(または `ProgrammerManual`)
が存在すればそちらを優先し、無ければ開発リポジトリ直下の `docs/...Manual` にフォールバックします。
