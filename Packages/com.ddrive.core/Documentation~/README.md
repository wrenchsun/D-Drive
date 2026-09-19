# Documentation~

このフォルダは P-5 時点では雛形のみです。

`docs/DesignerManual/` と `docs/ProgrammerManual/`（HTML + images + style、開発リポジトリの正本）は、
ここへコピーするのではなく **P-9（リリース手順の道具化）でリリースのたびに同期する**方針にしています
（[docs/42_distribution.md](https://github.com/wrenchsun/D-Drive/blob/main/docs/42_distribution.md) §2.1）。

同期後は以下の構成になる想定です:

```
Documentation~/
  DesignerManual/   … docs/DesignerManual/ の同期先
  ProgrammerManual/ … docs/ProgrammerManual/ の同期先
  AGENTS_CONSUMER.md … 消費側エージェント向け規約(P-10 で新規作成)
  skills/ddrive-consumer/ … 消費側スキル(P-10 で新規作成)
```

`Editor/Manual/ManualPages.GetManualFolder` は、`Documentation~/DesignerManual`(または `ProgrammerManual`)
が存在すればそちらを優先し、無ければ開発リポジトリ直下の `docs/...Manual` にフォールバックします
（このリポジトリでは同期前のため、現状は常にフォールバック側が使われます。挙動は変わりません）。
