# AGENTS_CONSUMER.md — D-Drive を導入したプロジェクトの AI エージェント向け規約

このファイルは、D-Drive（`com.ddrive.core`）を UPM パッケージとして導入した**持ち込み先プロジェクト**で作業する AI エージェント（Claude Code、Codex 等）向けの規約です。D-Drive 自身を開発するときの手順（新しい種別の追加・ワークツリー運用・SpecWeb の検証等）は含みません。それらは開発リポジトリ側の `CLAUDE.md` / `.claude/skills/ddrive-agent-workflow/` にあり、持ち込み先には同梱されません。

Claude Code を使っている場合は、より詳しい手順書として `Documentation~/skills/ddrive-consumer/SKILL.md` も参照してください（セットアップウィザード・更新ウィンドウがこのプロジェクトの `.claude/skills/ddrive-consumer/` へコピーします）。

このプロジェクト自身の `CLAUDE.md`（フォルダ規約・命名規約・コーディング規約）と本書が競合する場合は、**このプロジェクトの `CLAUDE.md` を優先**してください。

## 1. やってはいけないこと

1. **`.unity` / `.prefab` / `.asset` / 画像 / 音声ファイルをテキストエディタで直接編集しない。** すべて Unity Editor（MCP ツールまたは人の手作業）経由で変更する。`.meta` ファイルを手で作らない・消さない・GUID を書き換えない
2. **禁止 API を直接呼び出さない**: `Instantiate` / `Resources.Load` / `AudioSource.Play` の直接呼び出しは D-Drive の `ForbiddenApiScanner`（Validation の一部）が検出します。D-Drive の管理下にあるアセット・オブジェクトは、D-Drive の静的ファサード（`Audio` / `Vfx` / `Anim` / `Presentation` 等）経由で扱ってください
3. **Data（`.asset`）は読み取り専用として扱う。** ゲームコードが D-Drive の Data アセット（`AssetDataBase` 派生）のフィールドを実行時に書き換えることは想定されていません。デザイナーが専用エディタで編集するものです。エディタ拡張から Data を書き換える必要がある場合のみ `Undo.RecordObject` + `EditorUtility.SetDirty` を使う
4. **パッケージ（`Packages/com.ddrive.core/`）を改造しない。** git URL 参照で導入したパッケージは読み取り専用として扱い、直接編集しない（`Library/PackageCache` 内の変更は次回の解決で消えます）。機能を拡張したい場合は、D-Drive が提供する拡張点（`IValidator` の自動発見、`ImportRule` ハンドラ、`IHapticOutput`、`INetBridge`、`IAssetBehaviour`、`[DataEditor]`）を使うか、開発リポジトリへ変更を提案してください
5. **`DDrive.*` の asmdef を触らない。** 持ち込み先固有のコード（ゲーム側のファサード呼び出し、独自 Validator 等）は自分のプロジェクトの asmdef に置き、`DDrive.*` という名前空間は持ち込み先で使わない

## 2. ID 経由の利用と `Generated` の扱い

D-Drive の一番大事な考え方は「プログラマーは中身（音・見た目）が無くても ID だけでゲームロジックを完成させられる」ことです。

- `Assets/Generated/`（または `DDrive.Generated.asmdef` を出力する設定を選んだ場合はそのフォルダ）にある `AssetIds.g.cs`・`Tuning.g.cs` は**生成物**です。手で編集しない。中身が古い場合は Unity のメニュー `Tools > D-Drive > Generate > Regenerate Asset IDs` / `Regenerate Tuning Keys` で再生成する
- ID 定数（`SEID.X` / `VFXID.X` / `PRESID.X` 等）が未登録の状態で `Play`/`Spawn` しても例外にはならず、警告ログ + Placeholder（無音・代役の見た目）で処理が継続します。デザイナー側の作業が未完了なだけでもこの状態になるので、慌てて実装を変えない
- 静的ファサード（`Audio` / `Vfx` / `Anim` / `Anim2D` / `Models` / `Mats` / `Prefabs` / `Ui` / `UiFx` / `UiSkins` / `CameraFx` / `Haptics` / `Presentation` / `Tuning` / `ScenePreload` 等）が公開 API です。`DDrive.Runtime` / `DDrive.Foundation` の `public` メンバのみ参照してよく、`DDrive.Editor` の型はゲームコードから参照しない

## 3. Validation / `CI.ValidateAll`

- `Tools > D-Drive > Validation > Run All`（または `-executeMethod DDrive.Editor.CI.ValidateAll`）が Data・カタログ・Addressables 登録・ProjectSettings 等をまとめて検査します。Error があると実行時に Placeholder になる・コンパイルが壊れる等の問題が起きるため、CI ではこれを fail 条件にしてください
- 新しい Warning が増えることがあります（D-Drive の更新に伴う仕様変更の周知が目的）。Error は更新直後には増えない設計（2 段階ルール）なので、更新した直後に CI が落ちた場合はまず「更新の手順を最後まで終えたか」（下記「更新手順」）を確認する

## 4. 更新手順

1. `Packages/manifest.json` の `#vX.Y.Z` タグを新しい版に書き換える
2. Unity を開き直し、コンパイルエラーが無いことを確認する
3. `Tools > D-Drive > Update > 更新ウィンドウ` を開き、「更新を適用」を実行する（データマイグレーション → ID/調整値の再生成 → Addressables 同期 → Validation の順で自動実行され、途中の段が失敗したらそこで止まる）
4. `Validation > Run All` で Error が無いことを確認する
5. manifest / lock / 更新で変わった `.asset` / 生成コードをコミットする

詳細は `Documentation~/README.md`・パッケージ `README.md`・開発リポジトリの `docs/42_distribution.md` §4.2 を参照してください。

## 5. 困ったときの参照先

| 知りたいこと | 参照先 |
|---|---|
| デザイナー向けの操作手順（Asset Browser・専用エディタ・試聴等） | `Documentation~/DesignerManual/` |
| プログラマー向けの API・コード例 | `Documentation~/ProgrammerManual/` |
| Claude Code 向けの詳しい消費側手順 | `Documentation~/skills/ddrive-consumer/SKILL.md` |
| 互換性ポリシー・更新の設計根拠 | 開発リポジトリの `docs/42_distribution.md` |

## 6. Unity の操作について

Unity Editor の操作（コンパイル確認・テスト実行・シーン編集等）は、**持ち込み先プロジェクト自身の MCP 構成に従ってください。** D-Drive 独自の MCP セットアップ・トークン運用は持ち込み先には含まれません。
