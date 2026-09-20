# 持ち込み先向け CI テンプレート

このフォルダ（`Tools~/CI/`）は Unity から見えない場所（`~` 付きフォルダは Package Manager が import しない）にある配布物です。持ち込み先プロジェクトで CI に組み込みたい場合は、中身をコピーして使ってください。

| ファイル | 用途 |
|---|---|
| `run-ddrive-ci.cmd` | ローカル PC で D-Drive の互換性・品質チェックを実行するバッチ（開発リポジトリの `Tools/CI/run-ci.cmd` の縮小版） |
| `ddrive-ci.yml` | GitHub Actions のワークフロー雛形 |

## 使い方

1. `run-ddrive-ci.cmd` を持ち込み先プロジェクトの `Tools/CI/`（無ければ好きな場所）へコピーする
2. `ddrive-ci.yml` を持ち込み先プロジェクトの `.github/workflows/` へコピーする
3. `runs-on` のラベル・`UNITY_EDITOR_PATH` 等を、持ち込み先の runner 構成に合わせて書き換える

## MS2026 のように「Unity のコンパイル・テストを実行する CI がまだ無い」場合

このテンプレートを作成した時点（2026-09-20）で `C:\Users\yamag\wrench\MS2026` を実際に確認したところ、既存の CI は次の 2 本だけで、Unity のコンパイル・テストを実行する CI は無い:

| 既存ワークフロー | 実行環境 | 内容 |
|---|---|---|
| Unity Hygiene | GitHub Hosted（`ubuntu-latest`） | `.meta` 欠落・LFS 追跡漏れ・命名規則等、Unity を起動しない静的チェック |
| Build Windows | self-hosted runner（Windows、ラベル付き） | Unity をバッチモードで起動してビルドし、成果物を配布する |

このような場合、`ddrive-ci.yml` は**既存のワークフローに手を加えて差し込むのではなく、新しいワークフローとして追加する**。MS2026 の `Build Windows` は、次の構成で Unity をバッチモードから直接呼び出しており、ライセンスアクティベーションの手順を含んでいない（= その self-hosted runner の Unity Editor が既にライセンス認証済みの前提で動いている）。`ddrive-ci.yml` もこの構成に倣っている:

- GitHub Hosted runner は使わない（Unity がインストールされていないため）
- self-hosted runner を `runs-on: [self-hosted, windows, <プロジェクト固有のラベル>]` のように**ラベルで指定**し、他のプロジェクトの runner に誤って割り当てない
- `push`（対象ブランチ）は `paths` で `Assets/**`・`Packages/**`・`ProjectSettings/**` に絞り、ドキュメントだけの変更では動かさない
- `workflow_dispatch` で手動実行もできるようにする
- Unity 呼び出しは `-batchmode -nographics -quit -projectPath <repo> -executeMethod <メソッド>` の形（ライセンス関連の追加ステップは、持ち込み先の runner が未認証の場合のみ別途用意する。**game-ci 等のコンテナベースのアクションは使わず、既存の self-hosted 呼び出しの流儀に合わせている**）

self-hosted runner がまだ無い場合の登録手順・PC 側の前提（Unity のインストール場所・作業フォルダの扱い等）は、既に self-hosted runner を使っている自分のプロジェクトの CI 資産（例: MS2026 の `Docs/CI.md` の「Build Windows」節）を参照して同じ流儀に合わせるのが最短です。

## 実行される内容

`run-ddrive-ci.cmd` / `ddrive-ci.yml` はどちらも次の 4 段を順に実行し、途中で失敗したら止まります。

1. `DDrive.Editor.CI.MigrateCheck` — 未適用のデータマイグレーションが無いか
2. `DDrive.Editor.CI.ValidateAll` — Validation の Error が無いか
3. `DDrive.Editor.CI.RegenerateIds` + `git diff --exit-code` — ID/調整値の生成コードに未コミットの差分が出ないか
4. EditMode / PlayMode テスト

**注意**: 4 の EditMode/PlayMode は、このプロジェクトの Test Runner に登録されているテストを実行します。D-Drive 自身のテストを含めたい場合は、セットアップウィザードの「テストを有効化する」（`manifest.json` の `testables` に `com.ddrive.core` を追加）を ON にしてください。OFF のままなら、このステップは持ち込み先プロジェクト自身のテストだけを実行します（それでも `run-ddrive-ci.cmd`/`ddrive-ci.yml` に含めておく価値があります）。

CHANGELOG ガード・Performance テスト・NetCheck（2 クライアント自動テスト）は D-Drive の開発リポジトリ専用の検査のため、このテンプレートには含めていません。
