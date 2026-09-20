# 50. 持ち込み先向け持ち込み先ガイド（導入・更新・運用、HTML）

関連: [42_distribution.md](42_distribution.md)（配布・更新・互換性ポリシーの設計）/ `Packages/com.ddrive.core/README.md`（パッケージ同梱の入口）/ `Packages/com.ddrive.core/Documentation~/AGENTS_CONSUMER.md`（AI エージェント向け規約）/ `Packages/com.ddrive.core/Documentation~/skills/ddrive-consumer/SKILL.md`（Claude Code 向け詳細手順）/ [48_p11_install_test_2026-09-20.md](48_p11_install_test_2026-09-20.md)・[49_p12_ms2026_install_2026-09-20.md](49_p12_ms2026_install_2026-09-20.md)（実施記録・発見事項）/ [ProgrammerManual/getting-started.html](ProgrammerManual/getting-started.html) §1-4「持ち込み先」

D-Drive（`com.ddrive.core`）を**持ち込み先プロジェクト（MS2026 等）で初めて触るプログラマー**向けに、「導入手順」「更新方法」「運用方法」を個別の HTML ページに分けたガイド。[DesignerManual](DesignerManual/Readme.html)・[ProgrammerManual](ProgrammerManual/Readme.html) と同じ書式（共通スタイル・パンくず・ページ内リンク集）で、`docs/50_consumer_guide/` 配下に置く。

**位置づけ**: DesignerManual/ProgrammerManual が「D-Drive を使ってアセット・コードを作る」ためのマニュアルであるのに対し、本ガイドは「D-Drive というパッケージ自体を自分のプロジェクトに入れる・上げる・保守する」という、持ち込み先のプロジェクト管理者・プログラマー向けの手順書。内容はパッケージ `README.md`・`docs/42_distribution.md`・`AGENTS_CONSUMER.md`・`skills/ddrive-consumer/` を要約・再構成したもので、矛盾があればそれらの正本（特に直近リリースの内容）を優先する。

## 索引

| ページ | 内容 | 目安時間 |
|---|---|---|
| [50_consumer_guide/index.html](50_consumer_guide/index.html) | 目次。読む順番・関連ドキュメントへの導線 | 2分 |
| [50_consumer_guide/install.html](50_consumer_guide/install.html) | 導入手順: 依存確認 → manifest 追加 → セットアップウィザード → SE 試聴・Play Mode 確認 | 10分 |
| [50_consumer_guide/update.html](50_consumer_guide/update.html) | 更新方法: 更新チェック → manifest 版上げ → 更新を適用 → Validation、ロールバック、MAJOR 更新時の移行ガイドの読み方 | 8分 |
| [50_consumer_guide/operation.html](50_consumer_guide/operation.html) | 運用方法: 日常の Validation、ID 再生成、Addressables 同期、CI テンプレ、命名規則との付き合い方、テストの有効化、エージェント向けスキル、困ったときの見方 | 10分 |

## 正本の所在

- **本ガイドの正本は `docs/50_consumer_guide/*.html`**（このリポジトリ）。`Tools/Release/bump-version.ps1` が DesignerManual/ProgrammerManual/migrations と同じ仕組みでリリースのたびに `Packages/com.ddrive.core/Documentation~/ConsumerGuide/` へミラー同期する（[42_distribution.md](42_distribution.md) §2.1・§4.1）。`Documentation~/ConsumerGuide/` 配下を直接編集しないこと（次のリリースで上書きされる）
- 内容の出典（矛盾があれば新しい版を採る）: `Packages/com.ddrive.core/README.md`（導入 5 ステップ・依存表・既知の制約・更新・ロールバック）、[42_distribution.md](42_distribution.md) §4（更新の取り込みフロー）・§5（互換性ポリシー）、[48_p11_install_test_2026-09-20.md](48_p11_install_test_2026-09-20.md)・[49_p12_ms2026_install_2026-09-20.md](49_p12_ms2026_install_2026-09-20.md)（実際に踏んだ手順・見つかった不具合と対処）、`Packages/com.ddrive.core/Tools~/CI/README.md`（CI テンプレ）、`Packages/com.ddrive.core/Documentation~/skills/ddrive-consumer/`（Claude Code 向け詳細手順・よくある警告・更新チェックリスト）、`Packages/com.ddrive.core/Documentation~/AGENTS_CONSUMER.md`（AI エージェント向け規約）、`Editor/Update/UpdateWindow.cs`・`Editor/Setup/ProjectSetupWizardWindow.cs`（ウィンドウの節構成・ボタン文言）
- 本ガイドは**機能・手順のみ**を書く。バグ修正の経緯や「以前は〜だった」という履歴は書かない（日付付きの発見記録は上記 [48](48_p11_install_test_2026-09-20.md)/[49](49_p12_ms2026_install_2026-09-20.md) に任せてリンクする）

## 変更履歴

- 2026-09-20: 新規作成。`docs/50_consumer_guide/{index,install,update,operation}.html` の 4 ページ構成で、パッケージ README・42_distribution・48/49・Tools~/CI・skills/ddrive-consumer・AGENTS_CONSUMER・UpdateWindow/ProjectSetupWizardWindow の実際の文言をもとに作成。`Tools/Release/bump-version.ps1` のミラー同期対象に `docs/50_consumer_guide/` → `Documentation~/ConsumerGuide/` を追加。
