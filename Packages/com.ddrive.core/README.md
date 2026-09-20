# D-Drive (`com.ddrive.core`)

D-Drive（Designer-Driven Re: IDE Visual Environment）は、プログラマーが **ID だけ**でモックを完成させ、デザイナーが専用エディタで中身（Audio/VFX/Model/Animation/Material/UI/Presentation/Timeline 等）を作れるようにする Unity フレームワークです。

## 導入（5 ステップ）

### 1. `manifest.json` に D-Drive と依存（UniTask・R3）をまとめて追加する

プロジェクトの `Packages/manifest.json` に、D-Drive 本体(git URL。`?path=` でサブフォルダ、`#vX.Y.Z` でバージョンを固定)と、git 配布のため `package.json` に書けない 2 つの依存(UniTask・R3)、および R3 が使う NuGet パッケージの scoped registry を**まとめて**追加してください。以下はそのままコピー&ペーストできる断片です(版は開発リポジトリの `Packages/manifest.json` と同じものを使っています)。

```json
{
  "scopedRegistries": [
    {
      "name": "Unity NuGet",
      "url": "https://unitynuget-registry.openupm.com",
      "scopes": [
        "org.nuget"
      ]
    }
  ],
  "dependencies": {
    "com.ddrive.core": "git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11",
    "com.cysharp.r3": "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.3.1",
    "org.nuget.r3": "1.3.1"
  }
}
```

実際の `manifest.json` には既に `dependencies` と(あれば)`scopedRegistries` があるはずなので、上記はそれぞれの中身をマージしてください(`scopedRegistries` に他のレジストリが既にある場合は配列に 1 件追加、`Unity NuGet` が既にあるなら `scopes` に `org.nuget` を追加するだけで足ります)。

SSH 鍵で GitHub に認証する環境では、`com.ddrive.core` の値を次の `git+ssh` 形式に置き換えても参照できます(挙動は同じです)。

```json
"com.ddrive.core": "git+ssh://git@github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0"
```

このリポジトリは private のままなので、参照する側の PC に GitHub への git 認証(HTTPS の Git 資格情報、または SSH 鍵)が設定されている必要があります。開発機は HTTPS の Git 資格情報(Git Credential Manager 等でキャッシュされたトークン)で解決できる環境になっているため、まずは `git+https` 形式を試し、環境が SSH 鍵運用のときだけ `git+ssh` 形式に切り替えてください。

タグが無いコミットを直接指定したい場合(リリース前の検証等)は、`#` の後ろに**省略しない 40 桁のフルコミットハッシュ**を書いてください。短縮形(例 `#6c65a89`)は Unity の Package Manager が `Could not clone … Make sure […] is a valid branch name, tag or full commit hash` で解決に失敗します(2026-09-20、P-11 で実際に確認)。

NGO(マルチプレイ、`com.unity.netcode.gameobjects`)は**任意**です。使う場合だけ、下の「依存関係」表のとおり別途 `manifest.json` に追加してください(D-Drive 側の NGO 連携コードは別アセンブリ `DDrive.Runtime.Ngo` に分離されており、`versionDefines` の `DDRIVE_NGO` が自動で有効になったときだけコンパイルされます。NGO を追加しなくても D-Drive 本体・他のアセンブリのコンパイルには影響しません)。

### 2. Unity を開く

Package Manager が git URL をすべて解決し、`package.json` の `dependencies`(下表「レジストリ配布」。Addressables・Input System・URP 等)も自動解決します。ここまでで D-Drive の全アセンブリがコンパイルできる状態になります。

### 3. セットアップウィザードを実行する

`Tools > D-Drive > Setup > セットアップウィザード` を開き、上から順に確認・適用します。手順 1 で依存を追加済みのため、ウィザードの「1. 依存パッケージ」の項目は通常「OK」表示になります(これは事後の保険で、たとえば手順 1 で `org.nuget.r3` の scoped registry を書き忘れた等、依存が欠けたままウィザードだけ実行してしまった場合に検出・追加できるようにするためのものです)。

- 依存パッケージの検査(不足があれば「追加」ボタンで導入)
- 置き場所の選択(既定 `Assets/GameData` 等のまま／1 つの親フォルダ配下にまとめる／個別指定。持ち込み先に「`Assets` 直下に新規フォルダを作らない」規約がある場合は「1 つの親フォルダ配下」を選ぶ)
- 既定フォルダ・設定ファイルの生成、Addressables の初期化、起動オブジェクトの配置
- テストを有効化するか(既定 OFF。D-Drive 自身のテストをこのプロジェクトの Test Runner で実行できるようにするかどうか)
- エージェント向けスキルのコピー(AI エージェントを使う場合。下記「ドキュメント」参照)

ウィザードは URP(Universal Render Pipeline)アセットの生成・割り当てまでは行いません(検査して警告するだけ)。空プロジェクトで URP をまだ使っていない場合は、Unity のメニュー `Assets > Create > Rendering > URP Asset (with Universal Renderer)` で作成し、`Project Settings > Graphics`/`Quality` に割り当ててください(2026-09-20、P-11 で確認: 割り当てないままだと D-Drive のシェーダー関連 Validator・テストの一部が Warning/Fail のままになります)。

同じ検査は `Validation > Run All` の `ProjectSetupValidator` としても実行できます。

> **Addressables の一括同期**: 「4. 既定フォルダ・設定の生成」は、種別ごとの空カタログを作った直後に(Addressables が初期化済みなら)全カタログを自動で Addressables に登録します。ウィザードの「5. Addressables 同期」にある「全カタログ・Data を今すぐ同期する」ボタンでも同じ処理をいつでも実行できます(`Tools > D-Drive > Update` の「Addressables 登録を同期」と同じ)。既定の順番どおり「4」を「5. Addressables 初期化」より先に実行した場合は、初期化後にこのボタンを 1 回押してください。押さないと `Validation > Run All` で「カタログが Addressables に未登録」の Error が種別の数だけ出ます。

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
| git 配布（手順 1 で `manifest.json` にまとめて追加する） | `com.cysharp.unitask`（`#2.5.11`）/ `com.cysharp.r3`（`#1.3.1`）+ scoped registry `org.nuget.r3`（`https://unitynuget-registry.openupm.com`） | `package.json` の `dependencies` には書けない種類の依存のため、手順 1 の manifest 断片に含めて最初から追加する。書き忘れた場合はセットアップウィザードの「1. 依存パッケージ」が不足を検出し「追加」ボタンで導入できる(事後の保険) |
| 任意（NGO を使う場合のみ。別 asmdef `DDrive.Runtime.Ngo`） | `com.unity.netcode.gameobjects` 2.13.2 | 導入すると `versionDefines` の `DDRIVE_NGO` が自動で有効になり、`DDrive.Runtime.Ngo`(NGO 連携コード一式)がコンパイルされて Bootstrap がそれを使う。**導入しなくても D-Drive 本体(`DDrive.Foundation`/`DDrive.Runtime`/`DDrive.Editor`)のコンパイルには一切影響しない**(未導入時は `DDrive.Runtime.Ngo` アセンブリごとコンパイル対象外になるだけ)。導入しない場合は D-Drive はシングルプレイ相当（`LocalLoopbackBridge`）で動く。NGO を使う場合は Host/Client 全員が同じ版を導入すること |
| 開発専用（持ち込み先には不要） | Unity MCP（`com.coplaydev.unity-mcp` / `jp.shiranui-isuzu.unity-mcp`）、テスト用パッケージ | パッケージには含まれない。**Unity の操作（コンパイル確認・テスト実行等）は持ち込み先自身の MCP 構成に従う** |

## 既知の制約

- レンダーパイプラインは **URP のみ**対応（Built-in / HDRP は非対応）
- Unity **6000.3** 以上
- `Assets` 直下に新規フォルダを作らない規約を持つプロジェクトでは、セットアップウィザードの「置き場所」で「1 つの親フォルダ配下にまとめる」プリセットを選ぶ（既定のままだと `Assets/GameData` 等が直下に作られる）
- NGO（マルチプレイ）を使わない場合は導入不要。使う場合は Host/Client 全員が同じ D-Drive の版・同じ NGO の版であること

## 更新する

1. `Tools > D-Drive > Update > 更新ウィンドウ` を開き、最上段の「1. 更新チェック」で「最新の版を確認」を押す（`git ls-remote` でタグを取得し、現在の参照と比較して「最新です」/MINOR/MAJOR を表示する。MAJOR のときは赤字で移行ガイドを読むよう警告する）
2. 「manifest を選んだ版に更新する」を押す（確認ダイアログの後、`Packages/manifest.json` の `#vX.Y.Z` タグだけを新しい版に書き換える。手動で `manifest.json` を編集したい場合や Package Manager の UI から書き換えても構わない）
3. Unity が再コンパイルするのを待ち、コンパイルエラーが無いことを確認する（エラーがあれば `CHANGELOG.md` の「破壊あり」を疑い、`Documentation~/migrations/` の移行ガイドを確認する）
4. 同じ更新ウィンドウの「4. 更新を適用」を実行する（データマイグレーション → ID/調整値の再生成 → Addressables 同期 → Validation の順にまとめて実行され、途中で失敗するとそこで止まる）
5. `Validation > Run All` で Error が無いことを確認する

詳しい手順は `Documentation~/AGENTS_CONSUMER.md` および開発リポジトリの `docs/42_distribution.md` §4.2 を参照してください。

## ロールバック

- **manifest を選んだ版に更新した直後で、まだ何も適用していない場合**: 更新ウィンドウの「1. 更新チェック」にある「前の参照に戻す」を押すと、直前の `com.ddrive.core` の参照値に戻せます（もう一度押すと戻す前の状態に入れ替えられます）
- **それ以外の場合**: manifest / lock ファイル / 更新で変わった `.asset` / `Assets/Generated` の変更コミットを `git revert` し、Unity を開き直します

同一メジャーバージョン内の更新であれば、旧フィールドが残っているため旧版でも読めます（新版だけの値は次に保存したときに失われます）。メジャーバージョンをまたぐ更新のロールバックは「更新前のコミットへ戻す」以外の方法を保証しません（詳細は `docs/42_distribution.md` §4.4）。

## 困ったときは

- まず `Validation > Run All` を実行し、表示される Error/Warning の説明文に従う
- AI エージェント向けの規約・使い方は `Documentation~/AGENTS_CONSUMER.md` と `Documentation~/skills/ddrive-consumer/`
- デザイナー / プログラマー向けの詳しい操作手順は `Documentation~/DesignerManual/` / `Documentation~/ProgrammerManual/`
- それでも解決しない場合は、開発リポジトリ（`github.com/wrenchsun/D-Drive`）へ Issue を立てるか、CHANGELOG の該当版の記述を確認する

## ドキュメント

- 設計書・運用ドキュメントは開発リポジトリの `docs/`（`docs/42_distribution.md` が配布・互換性ポリシーの正本）
- デザイナー / プログラマー向けマニュアルは `Documentation~/`。正本は `docs/DesignerManual` / `docs/ProgrammerManual` で、`Tools/Release/bump-version.ps1`（P-9）がリリースのたびに同期する。`Documentation~/` 配下を直接編集しないこと（次のリリースで上書きされる）
- **導入・更新・運用の手順を個別ページにした持ち込み先ガイド**は `Documentation~/ConsumerGuide/`（正本は開発リポジトリの `docs/50_consumer_guide/`、同じく `bump-version.ps1` が同期する）。このパッケージを初めて触るプログラマー向けの入口はまず `ConsumerGuide/index.html`
- AI エージェント向けの消費側規約は `Documentation~/AGENTS_CONSUMER.md`、Claude Code 向けスキルは `Documentation~/skills/ddrive-consumer/`（セットアップウィザード・更新ウィンドウが `.claude/skills/ddrive-consumer/` へコピーする）

## バージョニング

`CHANGELOG.md`（開発リポジトリ直下）に従って [Semantic Versioning](https://semver.org/lang/ja/) を採用する。互換性ポリシーの詳細は [docs/42_distribution.md](../../docs/42_distribution.md) §5 を参照。
