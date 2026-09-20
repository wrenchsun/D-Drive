# D-Drive (`com.ddrive.core`)

D-Drive（Designer-Driven Re: IDE Visual Environment）は、プログラマーが **ID だけ**でモックを完成させ、デザイナーが専用エディタで中身（Audio/VFX/Model/Animation/Material/UI/Presentation/Timeline 等）を作れるようにする Unity フレームワークです。

## 導入（5 ステップ）

### 1. `manifest.json` に 1 行追加する

プロジェクトの `Packages/manifest.json` の `dependencies` に、このリポジトリを git URL で追加します。`?path=` でパッケージのサブフォルダを指定し、`#vX.Y.Z` でバージョンを固定します。

```json
"com.ddrive.core": "git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0"
```

SSH 鍵で GitHub に認証する環境では、次の `git+ssh` 形式でも参照できます（挙動は同じです）。

```json
"com.ddrive.core": "git+ssh://git@github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0"
```

このリポジトリは private のままなので、参照する側の PC に GitHub への git 認証（HTTPS の Git 資格情報、または SSH 鍵）が設定されている必要があります。開発機は HTTPS の Git 資格情報（Git Credential Manager 等でキャッシュされたトークン）で解決できる環境になっているため、まずは `git+https` 形式を試し、環境が SSH 鍵運用のときだけ `git+ssh` 形式に切り替えてください。

### 2. Unity を開く

Package Manager が git URL を解決し、`package.json` の `dependencies`（下表「レジストリ配布」）を自動解決します。git 配布の依存（UniTask・R3）はこの時点では自動追加されないため、次のステップのウィザードで追加します。

### 3. セットアップウィザードを実行する

`Tools > D-Drive > Setup > セットアップウィザード` を開き、上から順に確認・適用します。

- 依存パッケージの追加（UniTask・R3・`org.nuget.r3` の scoped registry。不足があれば「追加」ボタンで導入）
- 置き場所の選択（既定 `Assets/GameData` 等のまま／1 つの親フォルダ配下にまとめる／個別指定。持ち込み先に「`Assets` 直下に新規フォルダを作らない」規約がある場合は「1 つの親フォルダ配下」を選ぶ）
- 既定フォルダ・設定ファイルの生成、Addressables の初期化、起動オブジェクトの配置
- テストを有効化するか（既定 OFF。D-Drive 自身のテストをこのプロジェクトの Test Runner で実行できるようにするかどうか）
- エージェント向けスキルのコピー（AI エージェントを使う場合。下記「ドキュメント」参照）

同じ検査は `Validation > Run All` の `ProjectSetupValidator` としても実行できます。

### 4. SE を 1 件登録して試聴する

Asset Browser で新規 SE アセットを作成し、音源ファイルを割り当てて試聴します（Unity の操作自体は持ち込み先の環境・MCP 構成に従ってください。D-Drive 独自の操作手順はありません）。

### 5. Play Mode で ID を再生する

登録した SE の ID 定数（`SEID.X`）を使って 1 行で再生できます。

```csharp
using DDrive.Generated;
using DDrive.Runtime.Audio;

Audio.PlaySe(SEID.X);
```

シーンに起動オブジェクト（`DDriveRuntimeBootstrap`）が置かれていることが前提です（ステップ 3 のウィザードが配置します）。

## 依存関係

| 種別 | パッケージ | 持ち込み先での扱い |
|---|---|---|
| レジストリ配布（`package.json` で自動解決） | `com.unity.addressables` 2.3.1 / `com.unity.inputsystem` 1.19.0 / `com.unity.nuget.newtonsoft-json` 3.2.1 / `com.unity.render-pipelines.universal` 17.3.0 / `com.unity.timeline` 1.8.12 / `com.unity.ugui` 2.0.0 | Package Manager が自動解決する。URP と Timeline は `DDrive.Runtime` の必須依存（ゲーム実行時にも必要） |
| git 配布（ウィザードが検査・手動追加を提案） | `com.cysharp.unitask`（`#2.5.11`）/ `com.cysharp.r3`（`#1.3.1`）+ scoped registry `org.nuget.r3`（`https://unitynuget-registry.openupm.com`） | `package.json` の `dependencies` には書けない種類の依存のため、セットアップウィザードの「1. 依存パッケージ」が不足を検出し「追加」ボタンで導入する |
| 任意（NGO を使う場合のみ） | `com.unity.netcode.gameobjects` 2.13.2 | 導入すると `versionDefines` の `DDRIVE_NGO` が自動で有効になり、ネットワーク連携コードが動く。導入しない場合は D-Drive はシングルプレイ相当（`LocalLoopbackBridge`）で動く。NGO を使う場合は Host/Client 全員が同じ版を導入すること |
| 開発専用（持ち込み先には不要） | Unity MCP（`com.coplaydev.unity-mcp` / `jp.shiranui-isuzu.unity-mcp`）、テスト用パッケージ | パッケージには含まれない。**Unity の操作（コンパイル確認・テスト実行等）は持ち込み先自身の MCP 構成に従う** |

## 既知の制約

- レンダーパイプラインは **URP のみ**対応（Built-in / HDRP は非対応）
- Unity **6000.3** 以上
- `Assets` 直下に新規フォルダを作らない規約を持つプロジェクトでは、セットアップウィザードの「置き場所」で「1 つの親フォルダ配下にまとめる」プリセットを選ぶ（既定のままだと `Assets/GameData` 等が直下に作られる）
- NGO（マルチプレイ）を使わない場合は導入不要。使う場合は Host/Client 全員が同じ D-Drive の版・同じ NGO の版であること

## 更新する

1. `Packages/manifest.json` の `#vX.Y.Z` タグを新しい版に書き換える（Package Manager の UI からでも可）
2. Unity を開き直し、コンパイルエラーが無いことを確認する（エラーがあれば `CHANGELOG.md` の「破壊あり」を疑い、`docs/migrations/` の移行ガイドを確認する）
3. `Tools > D-Drive > Update > 更新ウィンドウ` を開き、「更新を適用」を実行する（データマイグレーション → ID/調整値の再生成 → Addressables 同期 → Validation の順にまとめて実行され、途中で失敗するとそこで止まる）
4. `Validation > Run All` で Error が無いことを確認する

詳しい手順は `Documentation~/AGENTS_CONSUMER.md` および開発リポジトリの `docs/42_distribution.md` §4.2 を参照してください。

## ロールバック

更新を取りやめる場合は、manifest / lock ファイル / 更新で変わった `.asset` / `Assets/Generated` の変更コミットを `git revert` し、Unity を開き直します。同一メジャーバージョン内の更新であれば、旧フィールドが残っているため旧版でも読めます（新版だけの値は次に保存したときに失われます）。メジャーバージョンをまたぐ更新のロールバックは「更新前のコミットへ戻す」以外の方法を保証しません（詳細は `docs/42_distribution.md` §4.4）。

## 困ったときは

- まず `Validation > Run All` を実行し、表示される Error/Warning の説明文に従う
- AI エージェント向けの規約・使い方は `Documentation~/AGENTS_CONSUMER.md` と `Documentation~/skills/ddrive-consumer/`
- デザイナー / プログラマー向けの詳しい操作手順は `Documentation~/DesignerManual/` / `Documentation~/ProgrammerManual/`
- それでも解決しない場合は、開発リポジトリ（`github.com/wrenchsun/D-Drive`）へ Issue を立てるか、CHANGELOG の該当版の記述を確認する

## ドキュメント

- 設計書・運用ドキュメントは開発リポジトリの `docs/`（`docs/42_distribution.md` が配布・互換性ポリシーの正本）
- デザイナー / プログラマー向けマニュアルは `Documentation~/`。正本は `docs/DesignerManual` / `docs/ProgrammerManual` で、`Tools/Release/bump-version.ps1`（P-9）がリリースのたびに同期する。`Documentation~/` 配下を直接編集しないこと（次のリリースで上書きされる）
- AI エージェント向けの消費側規約は `Documentation~/AGENTS_CONSUMER.md`、Claude Code 向けスキルは `Documentation~/skills/ddrive-consumer/`（セットアップウィザード・更新ウィンドウが `.claude/skills/ddrive-consumer/` へコピーする）

## バージョニング

`CHANGELOG.md`（開発リポジトリ直下）に従って [Semantic Versioning](https://semver.org/lang/ja/) を採用する。互換性ポリシーの詳細は [docs/42_distribution.md](../../docs/42_distribution.md) §5 を参照。
