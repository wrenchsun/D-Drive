# 34. オンボーディング（プログラマー・新メンバー向け）

関連: [README.md](README.md)（読む順番の全体像）/ [10_workflow.md](10_workflow.md)（ロール・命名規約）/ [20_mcp_setup.md](20_mcp_setup.md)（Unity MCP）/ [33_ci_setup.md](33_ci_setup.md)（CI）/ [AGENTS.md](../AGENTS.md)（AI エージェント向け）

> デザイナー向けのオンボーディングは [docs/DesignerManual/getting-started.html](DesignerManual/getting-started.html)（Unity 操作の実演）。本書は**プログラマー・新規参画メンバー**が、環境構築からプロジェクトの考え方・検証ループまでを最短で把握するための入口。

---

## 1. 環境構築

| 必要なもの | バージョン / 補足 |
|---|---|
| Unity Editor | **6000.3.13f1**（Unity Hub からこのバージョンだけを入れる。勝手に上げない。[CLAUDE.md] §1） |
| Git | PATH に必要（Package Manager の git URL 解決・Unity MCP 導入に使う） |
| `uv` / `uvx` | CoplayDev 版 Unity MCP サーバーの起動に使う（<https://docs.astral.sh/uv/>）。isuzu-unity 版（組み込み）を使うだけなら必須ではない |
| Node.js | `Tools/SpecWeb`（発注ツール、Google Apps Script）のロジックをローカルテストするときのみ必要。バージョンはリポジトリの `.nvmrc`/CI 設定に無ければ LTS で問題ない |
| clasp | `Tools/SpecWeb` を実際に Apps Script へデプロイする（`push.cmd`）ときのみ必要。**通常のゲーム開発作業では不要**（発注ツールの改修を担当する人だけ） |

セットアップの順序:

1. リポジトリを `git clone` する
2. Unity Hub で 6000.3.13f1 をインストールし、このプロジェクトを開く（初回はパッケージ解決・コンパイルに数分かかる）
3. AI エージェント（Claude Code 等）を使う場合は、Unity Editor 経由での確認ループのために Unity MCP を導入する。手順は [20_mcp_setup.md](20_mcp_setup.md) §1、§4（isuzu-unity 版を優先導入）
4. Node.js を使う作業（`Tools/SpecWeb`）が発生するまでは、Node/clasp のセットアップは後回しでよい

---

## 2. リポジトリの地図

```
Assets/DDrive/                 D-Drive 本体。層 = asmdef（[01_architecture.md] §4-5）
  Foundation/   Registry / Loader / Pool / Handle / EventBus / Pause / ValueDef / Validation
  Runtime/      種別ごとの Data / Manager / 静的ファサード(Audio, Vfx, Anim, CameraFx, Haptics ...)
                Anchoring / Presentation / Loop(GameLoopDriver・DDriveRuntimeBootstrap) / Net
  Editor/       AssetBrowser / 各専用エディタ / Preview / Codegen / Validation
  Tests/        Editor(EditMode) / Runtime(全プラットフォーム対象asmdefのため PlayMode でのみ実行される)
Assets/GameData/               ツールが管理する Data(.asset)・カタログ・標準プレハブ・確認用シーン
Assets/SourceAssets/           人が管理する実データ(音源・モデル・テクスチャ等)
docs/                          設計書(00〜33)。DesignerManual/ はデザイナー向け HTML
Tools/SpecWeb/                 発注ツール（Google Apps Script、clasp 管理）
.claude/skills/ddrive-agent-workflow/  Claude Code 用のエージェント作業手順（新種別追加チェックリスト等）
AGENTS.md                      Codex 等その他エージェント向けの自己完結した要約
```

`Assets/GameData/` 配下のファイル名・配置フォルダは AssetBrowser が自動生成・維持するもので、**人（プログラマー含む）が手で決めない**（[10_workflow.md](10_workflow.md) §3）。

---

## 3. 最初に読む docs の順番

1. [README.md](README.md)「読む順番」表 — 全体の索引
2. ルートの [CLAUDE.md](../CLAUDE.md) — TL;DR の禁止事項・現在の進捗・MCP 状態（**最初に必ず読む**）
3. [01_architecture.md](01_architecture.md) — 4 層構成・Data/Instance/Handle 分離・ADR（なぜこう設計されているか）
4. [02_core_framework.md](02_core_framework.md) — AssetId / Registry / Loader / Pool / Event / Validation の基盤 API
5. 自分が最初に触る種別の設計書（[03_audio.md](03_audio.md) 等）
6. [10_workflow.md](10_workflow.md) — ロール別責務・命名規約・フォルダ配置規約（**ツールが維持するもの**という前提を理解する）
7. [12_review.md](12_review.md) — PR チェックリスト
8. 新種別を追加する・AI エージェントに実装を頼む場合は [.claude/skills/ddrive-agent-workflow/SKILL.md](../.claude/skills/ddrive-agent-workflow/SKILL.md) と [references/new-asset-type-checklist.md](../.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md)

---

## 4. コンセプト: 「ID だけでモックを作る」流れ

D-Drive の一番大事な考え方は、**プログラマーは中身（音・見た目）が無くても ID だけでゲームロジックを完成させられる**こと（[10_workflow.md](10_workflow.md) §2）。

```
① プログラマー: モック実装
   - Presentation.Play(PRESENTID.SkillSlash, ctx) のように、生成済みの ID 定数を使ってコードを書く
   - ID が未登録でも Placeholder（空の代役）で動作する ★ここでゲームロジックは完成
   - PR#A: ゲームコードのみ。アセット不要でレビュー可能
        │
② デザイナー: ID の中身を作る（Asset Browser・専用エディタ）
        │
③ 結合確認: 実機ビルドで確認 → 微調整は②の繰り返し（コード PR 不要）
```

- ID 定数はツールが生成する（`SEID.PlayerSlash` / `VFXID.SkillFire` / `PRESID.SkillSlash` のような、各種別の `*ID` 静的クラス）。**手で書かない**。`Tools > D-Drive > Generate > Regenerate Asset IDs` で再生成できる
- 各種別には `Audio` / `Vfx` / `Anim` / `Presentation` のような**静的ファサード**があり、`Audio.PlaySe(SEID.PlayerSlash)` のような 1 行で呼び出せる（内部で Manager → Registry → Pool → Instance → Handle という解決を行うが、呼び出し側はそれを意識しない）
- 未登録 ID を Play すると、警告ログ + Placeholder（無音 / マゼンタの球など）で継続する。**例外で処理を止めない**のが方針（[CLAUDE.md] §0-4）
- 調整値（enemy の HP 等の数値）は `TUNING.キー定数` / `Tuning.GetFloat(...)` のように、[調整値タブ](DesignerManual/spec-sync.html#調整値タブ) 経由でコードを書き換えずに変更できる

---

## 5. 検証ループの要点

コンパイル・テストの確認は Unity MCP（isuzu-unity 優先）経由で行う。詳細な手順・待ち時間の目安は [.claude/skills/ddrive-agent-workflow/SKILL.md](../.claude/skills/ddrive-agent-workflow/SKILL.md) §2 を参照。要点だけ:

- `compile_request` → 20〜25 秒待つ → `compile_status` で `succeeded` を確認
- `test_run mode=edit` → `test_results`（EditMode、20〜30 秒） / `test_run mode=play` → `test_results`（PlayMode、1〜2 分。`Tests/Runtime` は asmdef が全プラットフォーム対象のため PlayMode でしか走らない）
- **EditMode と PlayMode の両方が green になるまで完了報告しない**。Unity MCP に接続できていなければ「未検証」と明示する（[CLAUDE.md] §3、§4）
- ネットワーク機能（NGO/`INetBridge`）は PlayMode テストに加えて実機確認が必要（[29_network_device_test.md](29_network_device_test.md)）。ユニットテストだけでは検出できない実バグが複数回見つかっている
- テスト前後で `git status`/`git diff` に差分が出ないことを確認する（特に `Assets/AddressableAssetsData/AssetGroups/*.asset`）。テストが実データを汚していないかの確認

---

## 6. CI（6-1、docs/33）

`.github/workflows/ci.yml` は Validation → ID 差分検出 → EditMode → PlayMode を実行できる状態まで作られているが、**2026-09-15 のユーザー決定で本稼働は Phase 7 末に延期されている**（MS2026 側に CI が既にあるため）。現在は PR/push では起動せず、Actions タブからの手動実行（`workflow_dispatch`）のみ（[33_ci_setup.md](33_ci_setup.md) 冒頭の注記）。それまでの検査は次の方法で行う:

- ローカルでの同一検査: `Tools/CI/run-ci.cmd`（+ `Tools/CI/Summarize-Results.ps1`）
- Unity の Test Runner、または Unity MCP 経由の `test_run`/`test_results`（§5 参照）

セルフホストランナーの登録・ブランチ保護の設定は Phase 7 末に行う作業として [33_ci_setup.md](33_ci_setup.md) §1-2 に手順がまとめてある。

---

## 7. AI エージェントとの関わり方

D-Drive の実装作業は Claude Code / Codex 等の AI エージェントに委任することが多い。新規メンバーが AI エージェントに作業を頼む・レビューする場合の入口:

| ファイル | 対象 | 内容 |
|---|---|---|
| [AGENTS.md](../AGENTS.md)（リポジトリ直下） | Codex 等、`.claude/skills/` を読まないエージェント | 禁止事項・ディレクトリ地図・作業手順・検証ループ・コミット慣習の自己完結した要約 |
| [.claude/skills/ddrive-agent-workflow/SKILL.md](../.claude/skills/ddrive-agent-workflow/SKILL.md) | Claude Code | 上記と同内容の詳細版。新種別追加のフルチェックリスト・MCP 検証ループの具体的なコマンド列・ワークツリー運用の注意 |
| [.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md](../.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md) | 同上 | 新しい `AssetType` を追加するときの 11 項目 + 他システム統合の任意項目 |

一次情報は常に [CLAUDE.md](../CLAUDE.md) と `docs/`。AI エージェントが書いたコード・docs も、通常の PR と同じレビュー（[12_review.md](12_review.md)）を通す（「AI がやった」はレビュー省略の理由にならない）。

---

## 8. Web 発注ツール（Tools/SpecWeb）との関わり方

企画が「誰が・何を・いつまでに作るか」を管理するブラウザアプリ。プログラマーとしての関わり方は基本的に次の 2 点だけ:

1. **企画が発注ツールに発注を登録すると、D-Drive 側に Placeholder（空の ID）が自動でできる。** プログラマーはその ID 定数（例: `SEID.PlayerSlash`）を、中身ができる前からコードで使い始めてよい
2. デザイナーが中身を作り込み、Validation の Error が無くなると、発注ツール側の状態は自動で「インポート済」に進む（手動操作は不要）

発注ツール自体の実装（Google Apps Script、`Tools/SpecWeb/`）を触る場合は [32_spec_web.md](32_spec_web.md) と [.claude/skills/ddrive-agent-workflow/SKILL.md](../.claude/skills/ddrive-agent-workflow/SKILL.md) §5（Node テストの限界・実デプロイでの目視確認が必須な理由）を参照。デザイナー向けの使い方説明は [DesignerManual/spec-sync.html](DesignerManual/spec-sync.html) にまとまっており、そちらと重複する説明は書かないこと。

---

## 9. 困ったときは

| 症状 | 参照先 |
|---|---|
| Unity MCP が繋がらない | [20_mcp_setup.md](20_mcp_setup.md) §1 の表 |
| 新しい種別（AssetType）を追加したい | [.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md](../.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md) |
| コミット・PR の作法が分からない | [12_review.md](12_review.md) / [AGENTS.md](../AGENTS.md) §5 |
| CI が「Waiting for a runner…」のまま進まない | [33_ci_setup.md](33_ci_setup.md) §1（ランナー未登録の間は仕様） |
| デザイナー向け機能の使い方を聞かれた | [DesignerManual/Readme.html](DesignerManual/Readme.html)（プログラマーが答えるのではなくこのページへ誘導する） |

---

## 10. 持ち込み先での始め方（D-Drive を UPM パッケージとして導入したプロジェクト向け）

このリポジトリ（D-Drive 開発リポジトリ）ではなく、D-Drive を `com.ddrive.core` として導入した**別のプロジェクト**（MS2026 等）で新しくメンバーになった場合の入口。

**まずパッケージ `README.md`（`Packages/com.ddrive.core/README.md`）の「導入（5 ステップ）」を読む**: manifest への git URL 追加 → Unity を開く → セットアップウィザード → SE を 1 件登録して試聴 → Play Mode で `Audio.PlaySe(SEID.X)`。困ったときの参照先（Validation・AI エージェント向け規約・マニュアル）も同じ README にまとまっている。

### デザイナーの最初の 1 時間

1. README の 5 ステップを一度実行してもらう（環境構築はプログラマーと分担してよい）
2. [DesignerManual/getting-started.html](DesignerManual/getting-started.html) の「はじめての15分」を実施
3. [DesignerManual/package-setup.html](DesignerManual/package-setup.html) で、セットアップウィザード・更新ウィンドウの画面と役割をひととおり確認する
4. Asset Browser で自分が担当する種別（Audio/VFX 等）の新規作成 → 専用エディタでの割り当て → Validation の実行、までを一度通す

### プログラマーの最初の 1 時間

1. README の 5 ステップを実行し、`Audio.PlaySe(SEID.X)` が動くところまで確認する
2. [ProgrammerManual/getting-started.html](../Packages/com.ddrive.core/Documentation~/ProgrammerManual/getting-started.html)（同梱先。正本は `docs/ProgrammerManual/getting-started.html`）の「1-4. 持ち込み先」を読み、置き場所（`Assets` 直下禁止の規約がある場合の扱い）・更新手順を把握する
3. `Documentation~/AGENTS_CONSUMER.md` を読み、AI エージェントに実装を頼む前提を揃える（禁止事項・ID 経由の利用・Validation）。Claude Code を使う場合は `Documentation~/skills/ddrive-consumer/SKILL.md` も読む
4. 未登録 ID で Placeholder が返る挙動（[CLAUDE.md] §0-4 と同じ「例外で止めない」方針）を実際に確認し、デザイナー側の作業待ちとコードのバグを区別できるようにする

一次情報は常にパッケージ `README.md` と `Documentation~/`。設計の背景を知りたい場合は D-Drive 開発リポジトリの [docs/42_distribution.md](42_distribution.md) を参照する（持ち込み先には同梱されないため、開発リポジトリを別途参照できる場合のみ）。
