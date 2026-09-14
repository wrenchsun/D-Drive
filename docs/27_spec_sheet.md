# 27. 仕様書スプレッドシート連携(テンプレート + 同期) 詳細設計(ドラフト)

関連: [10_workflow.md](10_workflow.md) §3 命名・ID 規約 / [13_extensions.md](13_extensions.md) A-2 未実装タブ / [11_tasks.md](11_tasks.md) 5-12〜5-14, 6-9

> **2026-09-14: [32_spec_web.md](32_spec_web.md)(HTML 仕様書、Google Apps Script)へ置き換え予定。本書は旧方式。**
> スプレッドシートへの入力が大変という理由で、仕様書全体を Web アプリ(GAS)へ統合する方針にユーザーが決定した。
> 5-12(テンプレート)は廃止、5-13〜5-16 は取得・パース層のみ [32] §5.1 の新方式に差し替える予定([32] §6 移行計画)。
> 移行が完了するまでの参照として本書は残す。
>
> **2026-09-14 追記(W-9〜W-11 実装完了)**: 取得・パース層(`SpecFetcher`/`SpecCsv`/`SpecSheetParser`)を
> `SpecWebFetcher`/`SpecWebParser`(Web アプリ = GAS の JSON API から取得)に差し替えた
> ([32_spec_web.md](32_spec_web.md) §5.1 実装メモ参照)。旧クラスは既存テストの CSV フィクスチャが
> そのまま使えるため物理削除せず残しているが、本番の同期経路(`SpecAutoSync`/`SpecSyncWindow`)からは
> 呼ばれない。差分・適用ロジック(`SpecDiffService`/`SpecSyncService`)・`TuningTable` の基本設計は
> 本書のとおり変更なしで再利用している(拡張は W-10 で追加)。
>
> **2026-09-15 追記(6-9 実装完了)**: 本書のスプレッドシート方式は廃止済みで、Web 発注ツール
> ([32_spec_web.md](32_spec_web.md)、特に §10 のアセット発注ツールへの再定義)へ移行済み。
> 下の §6(Validation / CI)の検査は、旧方式のシート・列の言葉のまま実装するのではなく、
> Web 発注ツール前提の読み替え表([32_spec_web.md](32_spec_web.md) の「実装メモ(2026-09-15、6-9)」
> 参照)で `Assets/DDrive/Editor/Validation/SpecDiffValidator.cs` として実装した。§6 の表自体は
> 元の設計の記録として変更していない。

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

### 4.5.1 実装メモ(2026-09-14、5-16)

対象コード: `Assets/DDrive/Editor/AssetBrowser/NewAssetDialog.cs`(「仕様書から選ぶ」セクション追加)、
`Assets/DDrive/Editor/Spec/SpecSyncService.cs`(`ApplyExtraFields` を `public` 化)、
`Assets/DDrive/Editor/Spec/SpecCache.cs`(`RecomputeDiff()` を追加)。

- **状態タグ/Assignee/Description/SpecUrl の反映はコピペしない**: `SpecSyncService.ApplyExtraFields(asset, row)` を
  `private` → `public` にして、`NewAssetDialog.CreateAsset()` からも直接呼ぶ。ダイアログでは選択中の仕様書行
  (`_selectedSpecRow`)の `Status`/`Assignee` と、ダイアログの「備考」「仕様リンク」欄(新設 `TextField`)の値から
  最小限の `SpecAssetRow` を組み立てて渡すだけで、同期の「新規 → Placeholder 作成」(`SpecSyncService.ApplyNew`)と
  全く同じ反映ロジックを通る
- **「備考」「仕様リンク」欄を新設**: 既存の 表示名/カテゴリ/識別子 の下に追加した(手入力でも使える。空でも作成可)。
  Status/Assignee は選んだ行から引くだけで、ダイアログには専用の入力欄を置いていない。
  **2026-09-14 対応済み(P5 レビュー第 1 弾 5-R。§9.1 の要判断だった項目)**: 識別子/表示名/カテゴリの
  いずれかを手で書き換えたら選択を解除するようにした(下記「レビュー対応」参照)。備考/仕様リンクは
  対象外(自由記述として保持してよいと判断)
- **一覧のフィルタ**: `SpecCache.GetUncreatedRows(null)` で全「未作成」行を取得し、ダイアログの `_definitions`
  (種別ロック時はロック対象だけ)に含まれる `AssetType` だけへローカルに絞り込む。`GetUncreatedRows` 自体の
  `filterType` 引数(単一 `AssetType`)は使わず、複数種別ロック(Audio = Se/Bgm)に対応するため呼び出し側で絞る
- **行を選ぶと**: 種別ドロップダウンをその行の `AssetType` に一致する最初の選択肢へ切り替え(`ControlSkin` のように
  1 `AssetType` に複数の具象 Data 型がある場合、どちらを作るかは人がドロップダウンで選び直す)、カテゴリ/識別子/
  表示名/備考/仕様リンクの各欄を埋める
- **作成後に一覧から消える**: `AssetCreationService.Create` の直後、`SpecCache.RecomputeDiff()` を呼ぶ(新設)。
  ネットへは行かず、取得済みの `LastAssetRows` と最新の `AssetDatabase` 状態から差分だけ再計算する。
  `LastFetchUtc` は更新しない(仕様書自体を再取得したわけではないため、キャッシュの新しさの表示を変えない)
- **キャッシュの古さ判定**: 取得から 1 時間を「古い可能性があります」の閾値にした(要判断。§9 参照)。閾値を
  超えている、またはキャッシュが無い場合だけ「仕様書を再取得」ボタンを出す。再取得は既存の `SpecAutoSync.Run`
  (`applyAutoPlaceholders: false`)をそのまま呼ぶ(非同期・新規行の自動作成はしない)
- **設定 URL 未設定時**: `DDriveSpecSettings.Load()`(`GetOrCreate()` は呼ばない = この場から設定 SO を自動生成
  しない)が `null` または `SpreadsheetUrl` が空なら、案内文(`HelpBox`)だけを出して一覧・検索欄は組み立てない

## 5. 仕様書リンク(5-14)

- 全 Data の基底 `AssetDataBase` に `SpecUrl`(string)を追加(**シリアライズ形式の変更 = 追加のみ**。CLAUDE.md §0-9 により着手前に確認)
- Inspector 上部(アイコンの隣)に「仕様書を開く」ボタン。`アセット` タブの「仕様」列から同期で自動設定される
- 「ロード検出時に仕様書を参照して割り当て」は §4.2 の自動同期で実現する(アセットを開いたときに毎回ネットへ取りに行くと遅く、オフラインで壊れるため、起動時に一括取得したキャッシュを使う)

---

## 6. Validation / CI(6-9)

> **2026-09-15: 実装は下表のシート/列の言葉のままではなく、Web 発注ツール前提の読み替え表
> ([32_spec_web.md](32_spec_web.md) の「実装メモ(2026-09-15、6-9)」)で行った。**
> `Assets/DDrive/Editor/Validation/SpecDiffValidator.cs` が `Specs/assets.json`・`Specs/tuning.json`
> (Web 発注ツールのスナップショット)を見て検査する。下表は元の設計の記録として残す。

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

#### レビュー対応(2026-09-14、P5 レビュー第 1 弾)

- **P2: `GetBool`/`GetString` が型不一致を検出しなかった(review1_runtime.md #6)**: `TuningEntry.Type` と
  呼び出した `Get*` が一致しない場合(例: 実体が `Float` の値を `GetBool` で読む)、以前は既定値のフィールド
  (`ValueBool`/`ValueString` 等、未設定なら `false`/`""`)をそのまま返すだけで何も警告しなかった。
  `Tuning.Get*` すべてで型比較を行い、不一致なら 1 キー 1 回だけ警告する(未登録キーの警告
  `WarnedKeys` とは別の `WarnedTypeMismatchKeys` を使う。`GetFloat`/`GetInt` は互換として扱う従来どおり
  `Float`⇄`Int` の相互変換のみ許容し、`Bool`/`String` との不一致だけ警告する)。
- **整理: 未登録キー警告に `#if DEVELOPMENT_BUILD || UNITY_EDITOR` が無かった**: `Tuning.Get*` は定常経路
  (毎フレーム呼ばれうる)なので、製品ビルドでのログ汚染・コストを避けるため他の警告と同じガードを付けた
  (新設の型不一致警告も同じガード)。

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

### 9.1 2026-09-14 追加(5-16 実装時)

8. ~~選択後に他の欄を手で書き換えても Status/Assignee は選択時のまま~~ → **2026-09-14 対応済み(P5 レビュー第 1 弾 5-R、review1_editor.md #4)**: 識別子/表示名/カテゴリのいずれかを手で書き換えたら `_selectedSpecRow` を解除する(`NewAssetDialog.OnManuallyEditedField`)ようにした。選択中の行は「仕様書から選ぶ」欄の上部に常に表示し(検索で一覧から外れても分かる)、「解除」ボタンでも明示的に外せる(`RefreshSelectedSpecRowIndicator`/`ClearSelectedSpecRow`)。備考/仕様リンクは自由記述として保持してよいと判断し、解除の対象にしていない。`OnSpecRowSelected` 自身が各欄へ値を代入する間は `_applyingSpecRowValues` フラグでこの自動解除を無効化する(選んだ直後に自分で解除してしまわないようにするガード)。テスト: `NewAssetDialogSpecPickerTests.ManuallyEditingIdentifierAfterSelectingSpecRow_ClearsSelection_AndCreateDoesNotApplyExtraFields` / `ClearSelectedSpecRow_Button_RemovesIndicator_AndUnboldsListRow`
9. **キャッシュの古さの閾値は 1 時間**: 起動時自動同期はドメインリロードごとに 1 回しか走らないため、ドメインリロード無しで長時間 Editor を開き続けた場合に「古い可能性があります」を出す目安として 1 時間にした(根拠は無く暫定)。長すぎる/短すぎるかは運用してみて判断してほしい
10. **`NewAssetDialog` に `gameDataRoot` のテスト用オーバーライドが無い**: 既存の `Open(...)` はどちらも `AssetCreationService.DefaultGameDataRoot`(`Assets/GameData`)固定で作成する。5-16 の統合テスト(`CreateFromSelectedSpecRow_AppliesExtraFields_AndRemovesRowFromCache`)は実際に `Assets/GameData` 配下にアセットを作り、カタログ(`AudioCatalog.asset`)・Addressables エントリを含めてテスト側で後始末している。他の Spec 系テストのように `gameDataRoot: TestRoot` で隔離できないため、今後同種のテストを増やすなら `NewAssetDialog` にテスト用の差し替え口を用意することを検討してほしい
11. **設定 URL 未設定時に `DDriveSpecSettings` を自動生成しない**: ダイアログを開くたびに設定 SO ができてしまうのを避けるため、`Load()` のみを呼び `GetOrCreate()` は呼ばない(既存の `SpecSyncWindow` の「設定を保存」だけが生成する)。ダイアログからは案内文のみで、設定自体はできない
