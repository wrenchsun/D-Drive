# 27. 仕様書スプレッドシート連携(テンプレート + 同期) 詳細設計(ドラフト)

関連: [10_workflow.md](10_workflow.md) §3 命名・ID 規約 / [13_extensions.md](13_extensions.md) A-2 未実装タブ / [11_tasks.md](11_tasks.md) 5-12〜5-14, 6-9

> 2026-09-13 ドラフト。ユーザー要望:「Google スプレッドシートの仕様書をこのツール向けにするテンプレート(項目は多すぎず)」「仕様書リンクを設定すると、ロード検出時に仕様書を参照して内容を自動で割り当てる」。既存の「01_仕様書」(方眼紙形式、別の人が所有)は変更せず、**新規作成したスプレッドシートで運用する**(ユーザー確認済み)。

---

## 1. 方針

- **人が読むシート**と**ツールが読むシート**を 1 つのスプレッドシート内で分ける。人向けは自由に書いてよい(結合セル・画像 OK)。ツール向けは「1 行目が見出し、1 行 1 件、結合セル禁止」の表だけ
- 人が入力するのは**意味情報だけ**([10] §3 と同じ原則)。ファイル名・ID・接頭辞はツールが作る。企画の人に `SE_Player_Slash` のような命名規則を打たせない
- 同期は **スプレッドシート → D-Drive の一方向**。D-Drive からシートへは書かない(権限・競合を避ける)。代わりに「シートに貼る用の一覧」を D-Drive がコピーできるようにする(§4.4)
- 取り込んだ結果は .asset として git にコミットする。**ゲーム実行時はシートに一切依存しない**(オフライン・ビルドの再現性)

---

## 2. テンプレート構成

| タブ | 読む人 | 役割 |
|---|---|---|
| `README` | 人 | 書き方の説明(このドキュメント §3 の要約)。最初のタブ |
| `概要` | 人 | ゲーム概要・スケジュール(既存「01_仕様書」の全体概要と同じ内容) |
| `機能_<名前>` | 人 | 機能ごとの仕様。現行の「〇見出し / ■項目 / イメージ | 仕様要件」レイアウトを簡略化して踏襲。末尾に「調整値」表を置く |
| `アセット` | **ツール** | 作るべきアセットの一覧(§3.1) |
| `調整値` | **ツール** | ゲームバランス等の調整値(§3.2) |
| `_選択肢` | ツール(非表示) | 種別・カテゴリ・タグ・状態のプルダウン元。D-Drive からコピーして貼る(§4.4) |

### 2.1 人向け「機能_<名前>」タブの型(簡略版)

```
■ <項目名>                    状態: 検討中 / 確定      担当: ○○    更新: 9/13
┌───────────────┬──────────────────────────────┐
│ イメージ(画像・図) │ 仕様要件(箇条書き)                  │
└───────────────┴──────────────────────────────┘
関連アセット: (「アセット」タブの識別子を列挙。例: SE Player/Slash, VFX Skill/FireBall)
調整値:        (「調整値」タブのキーを列挙)
```

- 1 タブの中身は自由記述。ツールは読まない
- 「関連アセット」「調整値」の欄は、ツール向けタブへの**参照**を書くだけにして、値そのものは二重に書かない(食い違いの元)

---

## 3. ツール向けタブの列定義(最小限)

### 3.1 `アセット` タブ

| 列 | 必須 | 入力 | 例 | D-Drive での扱い |
|---|---|---|---|---|
| 種別 | ○ | プルダウン | SE / BGM / VFX / Anim / Model / Canvas / Cutscene … | AssetType |
| カテゴリ | ○ | プルダウン(種別ごと 2 階層まで) | Player/Attack | Category |
| 識別子 | ○ | 英語 PascalCase | Slash | ID 定数名・ファイル名の元。**同期のキー**(種別 + 識別子) |
| 表示名 | ○ | 日本語可 | 剣の斬撃音 | DisplayName |
| 状態 | ○ | 未着手 / 仮 / 本番 / 保留 | 仮 | タグ(`State/Placeholder` 等)。未着手は Placeholder Data を作る |
| 担当 | | 自由 | よしだ | 担当者(未実装タブ [13] A-2 の担当割当に使う) |
| 仕様 | | 人向けタブのセルへのリンク | (リンク) | Data の SpecUrl(§5) |
| 備考 | | 自由 | 3 段階で音程を変える | Description |

- **9 列以下に抑える**。種別ごとの細かいパラメータ(音量・色・長さ等)はシートに書かず、D-Drive の専用エディタで作る(シートに書くと二重管理になり、結局どちらかが古くなる)
- 行頭が `#` の行、空行は無視(メモ・区切りに使える)

### 3.2 `調整値` タブ

| 列 | 必須 | 例 |
|---|---|---|
| キー | ○ | `Influence/FanBase`(`<機能>/<名前>`、Signal キーと同じ書式) |
| 値 | ○ | 1.0 |
| 型 | ○ | float / int / bool / string |
| 最小 / 最大 | | 0 / 10(Validation とエディタのスライダー範囲) |
| 単位 | | 秒 / % / 倍 |
| 説明 | | ファン 1 人あたりの影響力の素点 |

- 現行仕様書の「調整可能項目(外から触れるように/赤字が調整値)」をこの表に置き換える
- D-Drive 側は新規 SO `TuningTable`(キー → 値)に取り込み、定数クラス `TUNING.InfluenceFanBase` を生成して `Tuning.Get(TUNING.InfluenceFanBase)` で読む(実装詳細は 5-13 着手時に確定)

---

## 4. 同期(5-13)

### 4.1 取得方法

| 方式 | 準備 | 長所 | 短所 |
|---|---|---|---|
| **A. リンク共有 + CSV 取得(推奨・初期)** | スプレッドシートを「リンクを知っている全員が閲覧可」にする | 鍵の配布が不要。Unity から URL だけで取れる | URL を知っていれば誰でも閲覧できる |
| B. Sheets API + サービスアカウント | サービスアカウントにシートを閲覧共有、鍵 JSON をメンバーに配布 | 非公開のまま使える | 鍵の管理が必要(git に入れない) |

- A は `https://docs.google.com/spreadsheets/d/<ID>/gviz/tq?tqx=out:csv&sheet=<タブ名>` を `UnityWebRequest` で取る(Editor 専用、Runtime では呼ばない)
- **自宅 PC をサーバーにする方式は採らない**(Google ドライブのアプリ経由ではスプレッドシートは `.gsheet` というリンクだけで中身が来ない / PC の常時起動と外部公開が必要)
- 設定は `DDriveSpecSettings`(SO、`Assets/GameData/Settings/`)にスプレッドシート URL・タブ名を持つ。取得した CSV のキャッシュは `Library/DDriveSpec/`(コミットしない)

### 4.2 いつ同期するか

- 手動: `Tools > D-Drive > 仕様書と同期`
- 自動(設定で ON/OFF、既定 ON): Unity 起動時・ドメインリロード後に**取得と差分検出だけ**行い、差分があれば通知(AssetBrowser に「仕様書に変更 n 件」バッジ)。**自動では適用しない**
- 例外: 「未着手の新規行 → Placeholder Data 作成」だけは設定で自動適用可(既定 OFF)

### 4.3 差分プレビュー画面

| 区分 | 例 | 適用時の動作 |
|---|---|---|
| 新規 | シートにあって Data が無い | Placeholder Data を作成(ID 採番・Addressables 登録・定数生成は AssetBrowser の新規作成と同じ経路) |
| 変更 | 表示名・カテゴリ・状態・担当・備考・仕様リンクが違う | その項目だけ上書き(`Undo.RecordObject` + `SetDirty`) |
| シートから消えた | Data はあるがシートに無い | **何もしない**。「Archive 候補」として一覧に出すだけ(削除は 5-6 の安全な削除で人が行う) |
| 衝突 | 同じ 種別+識別子 が 2 行 / 識別子が PascalCase でない | 適用不可。行番号付きで表示 |

- **シートが上書きしてよい項目は上の表の「変更」列のものだけ**。音源・カーブ・Prefab などデザイナーが作った中身には触れない
- リネーム: シートで識別子を変えると「新規 1 + 消えた 1」に見える。差分画面で「これは ○○ のリネーム」と結び付けると ID(ulong)を保ったまま識別子だけ変える

### 4.4 D-Drive → シートへ貼る補助

- 「選択肢をコピー」: 種別・カテゴリ・タグ・状態の一覧を TSV でクリップボードへ → `_選択肢` タブに貼るとプルダウンが D-Drive と一致する
- 「既存アセットをコピー」: 既に D-Drive にあるアセットを `アセット` タブの形式でコピー(運用開始時の初期入力用)

---

### 4.5 新規作成ダイアログから選ぶ(5-16、2026-09-13 追加)

- `NewAssetDialog`(AssetBrowser・各エディタの「＋ 新規作成」共通)に「仕様書から選ぶ」を追加
- 一覧に出すのは `アセット` タブのうち **まだ Data が無い行だけ**。エディタから種別固定で開いたときはその種別に絞る。表示名・識別子で検索できる
- 選ぶと 種別 / カテゴリ / 識別子 / 表示名 / 備考 / 仕様リンク が入力済みになる。内容を確認して「作成」を押すだけ(差分同期の「新規 → Placeholder 作成」と同じ結果になる)
- 一覧は §4.2 の取得キャッシュを使う。キャッシュが無い・古いときは「仕様書を再取得」ボタンを出す
- 同期(§4.3)でまとめて作るか、必要なものだけダイアログで 1 件ずつ作るかを使い分けられる

## 5. 仕様書リンク(5-14)

- 全 Data の基底 `AssetDataBase` に `SpecUrl`(string)を追加(**シリアライズ形式の変更 = 追加のみ**。CLAUDE.md §0-9 により着手前に確認)
- Inspector 上部(アイコンの隣)に「仕様書を開く」ボタン。`アセット` タブの「仕様」列から同期で自動設定される
- 「ロード検出時に仕様書を参照して割り当て」は §4.2 の自動同期で実現する(アセットを開いたときに毎回ネットへ取りに行くと遅く、オフラインで壊れるため、起動時に一括取得したキャッシュを使う)

---

## 6. Validation / CI(6-9)

| 検査 | 重度 |
|---|---|
| シートにあって Data が無い(未作成) | Info(未実装タブ [13] A-2 に表示) |
| Data の状態タグが「本番」なのに Placeholder のまま | Warning |
| シートから消えたが Data が残っている | Info |
| 調整値の値が最小〜最大の範囲外 | Error |
| `アセット` タブの必須列が空 / 識別子の書式違反 | Error(同期画面) |

---

## 7. 未決事項

1. 取得方法 A(リンク共有)で良いか。学外に見られて困る内容を書くなら B
2. `調整値` を D-Drive に取り込むか(取り込むならゲームコードの読み方 `Tuning.Get` を 5-13 で決める)。仕様書の参照用に書くだけなら取り込まない
3. テンプレートのスプレッドシートを誰の Google ドライブに作るか(ユーザーのドライブで作成して共有する想定)

### 7.1 2026-09-14 自律作業での既定(ユーザー未確認)

チケット 5-12 の実装にあたり、Unity や外部 Google アカウントへの直接書き込みを避けるため、以下を**仮の既定**として実装した。
ユーザー未確認のため、次回レビュー時に上記 §7 の 1〜3 と併せて確定させること。

1. **取得方法は A(リンク共有 + CSV)** を既定とする。5-13 の実装は A を前提に進めてよい。非公開が必要な内容が出てきたら B に切り替える
2. **`調整値` は D-Drive に取り込む**。5-13 で新規 SO `TuningTable`(キー→値)を追加し、`Tuning.Get(TUNING.キー)` で読む方針(§3.2 の記載どおり)
3. **テンプレートは Google ドライブに直接作成しない**。代わりに、Google スプレッドシートにそのままインポートできる **`.xlsx` テンプレート**をリポジトリに置いた。ユーザー(または企画)が自分の Google ドライブにアップロードし、「アプリで開く → Google スプレッドシート」で開いて使う運用にする
   - テンプレート本体: [docs/SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx](SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx)
   - 生成スクリプト(再生成用): [docs/SpecSheetTemplate/make_template.py](SpecSheetTemplate/make_template.py)(openpyxl 使用。手で .xlsx を直接編集せず、このスクリプトを直して再生成する)
   - 記入ガイド(企画向け、アップロード手順・共有設定・よくある間違い): [docs/SpecSheetTemplate/README.md](SpecSheetTemplate/README.md)
   - タブ構成は本ドキュメント §2 のとおり(`README` / `概要` / `機能_サンプル` / `アセット` / `調整値` / `_選択肢`(非表示))

### 7.2 種別表記の対応表(`アセット` タブ「種別」列)

`アセット` タブの「種別」列は、[docs/10_workflow.md](10_workflow.md) §3.3 の SourceAssets 9 種別フォルダ命名(`Se`/`Bgm`/`Texture`/...)と同じ考え方で、
**`AssetType`(`Assets/DDrive/Foundation/Identity/AssetType.cs`)の enum 名をそのまま**使う(`SE`/`BGM` のようなファイル名接頭辞ではない)。
5-13 の同期実装は `Enum.TryParse<AssetType>(cell, ignoreCase: true)` で変換できるため、変換テーブルを別途持つ必要がない。

| `アセット` タブの種別表記 | `AssetType` enum 値 | ファイル名接頭辞(`AssetNamingService.GetTypePrefix`) |
|---|---|---|
| `Se` | `AssetType.Se` | `SE` |
| `Bgm` | `AssetType.Bgm` | `BGM` |
| `Vfx` | `AssetType.Vfx` | `VFX` |
| `Anim` | `AssetType.Anim` | `ANIM` |
| `Anim2D` | `AssetType.Anim2D` | `ANIM2D` |
| `Material` | `AssetType.Material` | `MAT` |
| `Texture` | `AssetType.Texture` | `TEX` |
| `Canvas` | `AssetType.Canvas` | `CANVAS` |
| `Prefab` | `AssetType.Prefab` | `PREFAB` |
| `Presentation` | `AssetType.Presentation` | `PRES` |
| `Shake` | `AssetType.Shake` | `SHAKE` |
| `Haptics` | `AssetType.Haptics` | `HAPTIC` |
| `UiTween` | `AssetType.UiTween` | `UITWEEN` |
| `Model` | `AssetType.Model` | `MODEL` |
| `Anchor` | `AssetType.Anchor` | `ANC` |
| `AnchorGroup` | `AssetType.AnchorGroup` | `ANCG` |
| `ControlSkin` | `AssetType.ControlSkin` | `SKIN` |

`AssetType.None` は選択肢に含めない(未設定を表す内部値のため)。

`状態` 列の値は既存コードに対応する固定タグが無いため(`TagCatalog` 自体が未実装)、本ドキュメント §3.1 で決めた
`未着手 / 仮 / 本番 / 保留` の 4 値を `_選択肢` タブにそのまま置いている。[13_extensions.md](13_extensions.md) A-1 の
「未実装タブ」が使う `未着手・作業中・完了` とは**別の語彙**(役割が異なるため、無理に統一しない)。

---

## 8. 実装メモ(2026-09-14、5-13/5-14)

対象コード: `Assets/DDrive/Editor/Spec/*`(新規)、`Assets/DDrive/Editor/Codegen/TuningCodegen.cs`(新規)、
`Assets/DDrive/Runtime/Tuning/*`(新規)、`Assets/DDrive/Foundation/Data/AssetDataBase.cs`(フィールド追加)、
`Assets/DDrive/Runtime/Loop/DDriveRuntimeBootstrap.cs`、`Assets/DDrive/Editor/Inspector/AssetDataInspector.cs`、
`Assets/DDrive/Editor/AssetBrowser/AssetBrowserWindow.cs`。

### 8.1 追加したシリアライズフィールド(`AssetDataBase`、追加のみ)

- `public string Assignee` - 「担当」列の反映先。既存の `Author`(保存時に自動記録される最終更新者)とは別物
- `public string SpecUrl` - 「仕様」列の反映先(5-14)。Inspector の「仕様書を開く」ボタン(`SpecUrlGui`)から `Application.OpenURL` で開く。空なら非表示

「状態」列は新規フィールドを増やさず、既存の `Tags`(string[])に `State/仮` のような値を1つだけ載せる方式にした
(`DDrive.Editor.Spec.SpecStatusTag`)。`TagCatalog` が未実装のため「既存のタグ機構があればそれ」の要件をこう解釈した。

### 8.2 同期の仕組み(`Assets/DDrive/Editor/Spec/`)

| ファイル | 役割 |
|---|---|
| `DDriveSpecSettings.cs` | 接続設定(SO)。`Assets/GameData/Settings/DDriveSpecSettings.asset` に `GetOrCreate()` で必要時のみ自動生成 |
| `SpecCsv.cs` | gviz CSV URL の組み立て + 引用符/カンマ/改行対応の CSV パーサ(純ロジック) |
| `SpecSheetParser.cs` | CSV から `SpecAssetRow`/`SpecTuningRow` へ変換。必須列欠落・種別不明・識別子書式違反・キー重複を `Issues` に集める |
| `SpecIdentifierCodec.cs` | 既存アセットの識別子をファイル名から逆算する(§8.3) |
| `SpecDiffService.cs` | 新規/変更/Archive候補/衝突を算出する |
| `SpecSyncService.cs` | 新規(`AssetCreationService.Create` 経由)・変更の適用、TuningTable への取り込み、選択肢/既存アセットの TSV 生成 |
| `SpecFetcher.cs` | `UnityWebRequest` + `Library/DDriveSpec/` キャッシュ(Editor 専用) |
| `SpecAutoSync.cs` | `[InitializeOnLoad]` + `delayCall` で起動時に取得と差分検出だけ行う |
| `SpecCache.cs` | 直近の取得・差分結果の保持(AssetBrowser のバッジ、5-16 の入力元) |
| `SpecSyncWindow.cs` | `Tools > D-Drive > 仕様書と同期`(差分プレビュー + 適用 + TSV コピー) |

### 8.3 既存アセットとの結び付け(新フィールドを増やさない決定)

`AssetDataBase` は識別子そのものを保持しない(表示名は日本語可、カテゴリは自由記述)。
`AssetNamingService.BuildFileName` が組み立てるファイル名 `{prefix}_{categorySegment}_{identifier}` は、
`categorySegment` と `identifier` のどちらも `_` を含み得ない(識別子は PascalCase、カテゴリセグメントは英数字のみに
正規化される)ため、ファイル名を `_` で分割した最後のトークンは常に元の識別子と一致する。この性質を使い、
`SpecIdentifierCodec.TryExtractIdentifier` でファイル名から識別子を逆算し、「種別+識別子」で仕様書の行と結び付けている
(新しい永続フィールドを増やさずに済ませる決定。§9 の要判断も参照)。

### 8.4 Tuning(調整値)の読み方

`TuningTable`(`Assets/DDrive/Runtime/Tuning/TuningTable.cs`)は `AssetDataBase` ではない
(`UiLayerSettings` と同じ「プロジェクト単位の設定 SO」。`DDriveRuntimeBootstrap.TuningTable` の Inspector 直参照 1 個だけを
想定し、Addressables には登録しない)。同期(`SpecSyncService.ApplyTuning`)が仕様書の「調整値」タブで丸ごと上書きする。

コードからの読み方:

    using DDrive.Generated; // TUNING.キー定数。Tools > D-Drive > Generate > Regenerate Tuning Keys で再生成
    using DDrive.Runtime.Tuning;

    float hitStopSec = Tuning.GetFloat(TUNING.CombatHitStopSec, defaultValue: 0.05f);

未登録キーは警告を1回だけ出し、呼び出し側が渡した `defaultValue` を返す(例外で止めない)。`Tuning.Bind`/`GetFloat`/
`GetInt`/`GetBool`/`GetString` は `Options.cs` と同じ静的ファサード設計(ADR#3)。

### 8.5 自動取得と通知

`SpecAutoSync`(`[InitializeOnLoad]`)が起動時・ドメインリロード後に `delayCall` 経由で取得+差分検出だけを行う。
`Application.isBatchMode` またはコマンドライン引数 `-runTests` のときは走らせない(インタラクティブな Test Runner
ウィンドウ経由の実行は検出できないため、自動同期は Assets を書かない(取得+差分検出のみ)設計にして安全側に振っている。
§9 の要判断に記載)。差分があれば `AssetBrowserWindow` のツールバーに「仕様書に変更 n 件」ボタンが出て、押すと
`SpecSyncWindow` が開く。

### 8.6 5-16 で使うキャッシュ API

`DDrive.Editor.Spec.SpecCache.GetUncreatedRows(AssetType? filterType = null)` が、まだ Data の無い(新規)行だけを返す
(`SpecCache.LastDiff.New` から組み立てる)。`NewAssetDialog` の「仕様書から選ぶ」はこれを呼べばよい。

---

## 9. 要判断(2026-09-14、5-13/5-14 実装時)

1. **Archive 候補の絞り込み**: 「シートから消えた」の対象を、`SpecUrl` が設定済み(=一度でも仕様書と同期された)アセットだけに限定した。手動で個別に作った既存アセットが「仕様書に無い」だけで毎回 Archive 候補に出るのを避けるため。運用開始直後、まだ1度も同期していない既存アセットは Archive 候補に出ない(想定どおり)
2. **識別子の逆算方式**: §8.3 のとおり、新しい永続フィールドを増やさずファイル名から逆算する方式にした。カテゴリの表記だけを変えて識別子は変えていない場合、ファイル名の再計算結果が変わり結び付けが外れる可能性がある(§4.3 のリネーム結び付けと同様、未対応)
3. **ControlSkin の自動作成**: `AssetType.ControlSkin` は `ButtonSkinData`/`SliderSkinData` の2つの具象型があり、どちらを作るか一意に決められないため、シートの新規行が `ControlSkin` のときは自動作成をスキップし警告ログのみ出す(手動作成が必要)
4. **自動同期のテスト検出**: `Application.isBatchMode` と `-runTests` コマンドライン引数だけで判定している。Unity Editor 内で Test Runner ウィンドウから対話的に EditMode/PlayMode テストを実行するケースは検出できない。ただし自動同期は Assets を書き換えない(取得+差分検出のみ)ため、テスト中に走っても実害は無い設計にしている
5. **リネーム結び付け(§4.3 末尾)**: 未実装。シートで識別子を変えると「新規1件 + シートから消えた扱い(Archive候補、SpecUrl 設定済みの場合のみ)」に見える
6. **差分プレビュー画面の選択粒度**: `SpecSyncWindow` は行ごとのチェックボックスで新規/変更を個別に適用できるが、Archive 候補・衝突には何のアクションも付けていない(仕様どおり「表示のみ」)
7. **`DDriveSpecSettings`/`TuningTable` の .asset は今回コミットしていない**: `Assets/GameData/Settings/` に自動生成される想定だが、ユーザーが実際に URL を設定して初回同期するまで存在しない。今回のブランチでは生成していない(手順は docs/28 の確認手順を参照)
