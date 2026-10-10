# 1008. 専用エディターの UI/UX 全面見直し（サンプルエディターで方向を決める）

> 起票 2026-10-10。持ち込み先（MS2026）から「**学習コストが高い**」「**パラメータが多すぎてどれを触ればいいか把握できていない**」という声が上がった。個別の欠陥修正（U-xx 系）ではなく情報の出し方そのものを見直す。**まずサンプルエディターを複数作り、ユーザーが触って方向を決める**（ユーザー指示）。UI/UX の観点で有効な便利機能の追加は可。
> 関連: [09_editor_tools.md](09_editor_tools.md) §6〜§8（ウィンドウ規約）/ [reviews/19](reviews/19_vfx_usability_review.md)（VFX の使い勝手レビュー）/ [1004_tasks.md](1004_tasks.md) UX 節（チケット）

## 1. 現状（2026-10-10 の調査、コードと docs から）

| 事実 | 影響 |
|---|---|
| 専用エディターは約 20 ウィンドウ。共通 UI 部品（セクション・ヘルプ・開閉記憶）も USS/UXML も無く、各ウィンドウが C# でインラインに組んでいる | 見た目・構造の一貫性が無く、1 つ覚えても次に活きない。横断的な改善は全ファイルに及ぶ |
| 全 Data に共通欄が約 14（Id / DisplayName / Description / Category / Tags / Icon / Assignee / SpecUrl / Version / Author / UpdatedAt / ChangeNote / Flags / Events）、その下に固有欄（Vfx 10、Se 14、Canvas 16 + ネスト）が並ぶ | 「最初の 1 つ」を作るのに必須なのは多くが 1〜2 欄（Vfx = Prefab、Se = Clip、Canvas = Prefab）なのに、画面上では区別が付かない |
| 段階表示（基本/詳細、初級/上級）の仕組みが無い。隠し方は「閉じた Foldout」だけで、VFX の「基本設定」「Anchor」「パラメータ」、Presentation の「共通設定」は既定で開いている | 最小経路に不要な節が最初から見える |
| Canvas Editor は最小経路の Prefab 欄が最下部の既定 Inspector 内にあり、上部は追従・自動収集・プリセット一括など Prefab と無関係の操作列 | 「最初に触る欄」が最も見つけにくい位置にある |
| 「雛形から作る」プリセットは Shake/Haptics・Slider・UI Tween だけ。VFX / Presentation / Canvas / SE は空から始まる | 何を埋めればよいか分からない |
| ウィンドウ内に「次にやること」を示す仕組みが無い。検証（`DataValidationSection`）は問題の列挙で、手順の案内ではない | 学習はマニュアル頼み。マニュアルは項目解説型で「触らなくてよい欄」を示していない。導線ページは SE 1 件の「はじめての 15 分」のみ |
| Tooltip は Vfx/Se/Bgm/Material/Model/Anchor がほぼ 100%、Canvas 50%、AnchorGroup 31%、ControlSkin 22%。ネスト構造（ButtonWire 等）の欄は無いものが多い | 欄名だけでは意味が分からない |

## 2. 方針

1. **欄を減らさない**（互換性ポリシー [42](42_distribution.md) §5: シリアライズ形式は追加のみ）。**見せ方を変える**。
2. **「必須 / よく使う / 詳細」の 3 段**に欄を分類する根拠はコードにある（Validator が Error にする欄 = 必須、既定値で警告が出ない欄 = 詳細）。分類は種別ごとの静的な表（`FieldGuide`）として 1 箇所に置き、どのサンプルも同じ表を使う。表には **平易な日本語ラベル・一言説明・検索語**も持たせる。
3. **サンプルは同じ Data 種別（`VfxData`）で 3 方向**作り、同じ土俵で比べる。VFX は必須欄が 1 つ（Prefab）、プレビューの基盤（`SceneVfxPreviewDriver`）と検証（`DataValidationSection`）が揃っていて、使い勝手レビュー（[19](reviews/19_vfx_usability_review.md)）の前例がある。
4. **サンプルは開発リポジトリの `Assets/EditorPrototypes/Editor/`** に置く（パッケージ `com.ddrive.core` に入れない。asmdef `DDrive.EditorPrototypes` が `DDrive.Editor` / `DDrive.Runtime` / `DDrive.Foundation` を参照）。既存の `VfxEditorWindow` は触らない。メニューは `DDriveMenu.Root + "Prototypes/"`。採用が決まった方向を本実装するときに、パッケージ側へ共通部品として移す。
5. サンプルでも [09](09_editor_tools.md) §7 の規約（`ScrollView` ルート、横幅 500px で見切れない）と §0 の禁止事項（Data を書くときは `Undo.RecordObject` + `SetDirty`）は守る。プレビューは既存の `SceneVfxPreviewDriver` を使い、ウィンドウ内描画はしない。

## 3. サンプル 3 方向

同じ `VfxData` を開き、同じ `FieldGuide` を使う。違いは「欄をどう見せ、どう辿らせるか」。

### A. 段階表示（かんたん / 標準 / 詳細）+「次にやること」

- 上部に **モード切替**（かんたん / 標準 / 詳細）。かんたん = 必須欄 + プレビューのみ、標準 = + よく使う欄、詳細 = 全欄（既存 Inspector 相当）。選んだモードは EditorPrefs に記憶。
- 最上部に **「次にやること」カード**: Validator の結果と欄の状態から、今やるべき 1 手を 1 行で出す（例: 「1. Prefab を設定する → 2. ▶ で確認する → 3. 保存」。完了した手は ✓）。
- 隠れている欄がある節には「あと N 個の設定（詳細で表示）」のリンクを出す。
- 便利機能: 欄の右に **「既定に戻す」**（既定値と違う欄だけ目印が付く）。
- 狙い: 「何を触ればよいか」をモードが答える。既存エディターへの移植が最も素直。

### B. ステップ型（作る → 見る → 整える → 登録）+ 雛形から始める

- **ページ送り**（戻る / 次へ、上部に進捗 1/4〜4/4）。1 ページに出す欄は 1〜3 個。
  1. **選ぶ**: 雛形（ヒット一発 / 常駐ループ / UI の上に出す / 空から）と Prefab
  2. **見る**: 確認用シーンで ▶（リピート・速度）。ここで「良ければ次へ」
  3. **整える**: 出る位置（Anchor の 2D パッド・高さ）と長さ（LifeMode / Duration）だけ
  4. **登録**: 検証の結果 → 表示名・カテゴリ → 保存。完了後は「すべての設定を見る」で標準エディターへ
- 雛形は `VfxData` の欄の組（LifeMode / Duration / FadeOut / Render / Anchor）を一括で入れる（Undo 1 回）。
- 狙い: 初回の学習コストを最小にする。2 回目以降は A や C が速いので、「新規作成のとき」専用になる想定。

### C. 目的別カード + 設定の検索

- 構造（Data のフィールド順）ではなく **「何をしたい？」** で束ねたカード: 「出す」「出る場所を変える」「長さ・消え方を変える」「見た目を調整する（Params）」「音・イベントと合わせる」「管理情報」。各カードは 1〜4 欄で、平易なラベル + 一言説明（FieldGuide から）。
- 上部に **設定の検索欄**（例: 「高さ」「ループ」「レイヤー」と打つとその欄だけ出る。ラベル・説明・検索語・元のフィールド名に一致）。
- フィルタ: **「変更済みだけ」**（既定値と違う欄だけ表示）/ 「必須だけ」。
- 各カードに「？」（マニュアルの該当節を開く。既存 `ManualLauncher`）。
- 狙い: 欄数が多いまま「探せる」ようにする。Canvas のように欄が多い種別に向く。

### D. デザイン重視（専用テーマ + ビジュアル部品）— 2026-10-10 追加（ユーザー指示「D でデザインをかなり凝ったもの」）

A〜C は「情報の出し方」の比較だったが、D は **見た目と触り心地を本気で作り込む**。機能は A の段階表示 + C の検索を土台にし、以下を専用の USS テーマで実装する（インラインスタイル禁止。`Assets/EditorPrototypes/Editor/Theme/` に USS を置き、`styleSheets.Add`）。Unity の Light / Dark テーマ両対応。

| 部品 | 内容 |
|---|---|
| ヘッダー（ヒーロー） | 左にアセットのアイコン（`Icon` があればそれ、無ければ種別の既定アイコンを大きく）、右に表示名（大きめの太字）・ID・カテゴリのチップ・状態チップ（✓ 検証 OK / ⚠ 警告 N / ✕ エラー N を色付き）。下に主操作のボタン列（▶ 再生 / ■ 停止 / 確認用シーン / 保存）を「主ボタンは塗り、副ボタンは枠だけ」で区別 |
| 段のバッジ | 欄のラベル左に 必須（赤系）/ よく使う（青系）/ 詳細（灰）の小さなバッジ。モード切替はヘッダー直下の **セグメントコントロール**（かんたん / 標準 / 詳細）。切替はアニメーション無しでよいが、選択中は塗りで明示 |
| カード | 目的ごとのカード（C と同じ束ね方）。角丸・薄い枠・見出し + 一言説明・右上に「？」。カード内の欄は 2 段組み（ラベル列固定幅、入力列伸縮）。空のカードは「この設定は既定のままで大丈夫です」の薄いプレースホルダー |
| ビジュアル入力 | **長さ**: LifeMode / Duration / FadeOut を 1 本の横バー（OneShot = 固定長バー + 末尾のフェード部分を半透明で、Loop = 無限記号）で表し、バー上のドラッグで Duration、端のドラッグで FadeOut を編集（Undo 対応）。**位置**: Anchor を上から見た 2D パッド（B より大きめ、グリッド + 中心マーク + 現在位置の点、側面に高さスライダー）。**レイヤー**: Render / RenderLayer をトグル + ドロップダウンで横並び |
| 検索 | ヘッダー右の検索欄。入力中はカードが絞られ、一致した語をラベル内でハイライト（太字 or 背景色） |
| 変更の可視化 | 既定値と違う欄は左端に細い色付きバー。ヘッダーの状態チップに「変更 N 件」。「↺ すべて既定に戻す」は確認ダイアログ付き |
| 検証 | `DataValidationSection` の中身を、カード内の該当欄の下に **インラインの注意行**（アイコン + 1 行 + 修正ボタン）としても出す |
| 空状態 | 対象未選択のときは、中央に大きめの案内（「Project で VfxData を選ぶか、ここにドロップ」）とドロップ領域 |
| 余白・文字 | 8px グリッド、見出し 14px 太字 / 本文 12px / 補足 11px 薄色。長いラベルは省略 + tooltip |

狙い: 「使いたくなる」見た目で学習コストの体感を下げる。採用するなら USS テーマを共通基盤（パッケージ側）にし、A〜C で選んだ構造に当てる。

### E. D をベースにしたフィードバック反映版 — 2026-10-10 追加（ユーザーのフィードバック）

ユーザーが D を触った結果: 「調整して再生するのに上まで戻るのが若干だるい」「触っていない設定は折り畳めるとよい」「AnchorPad のような視覚的な部品は分かりやすい」「Params（画像 2 = Unity 既定の配列 Inspector）はデザイナーが運用することを考えるともっと分かりやすくしたい。色 / サイズ etc. で事前に項目分けされていて調整だけで済むように。今は調整したい項目にアクセスしにくい」。E は D の見た目・部品をそのまま使い、次を変える。

| 変更 | 内容 |
|---|---|
| 固定アクションバー | ▶ 再生 / ■ 停止 / リピート / 保存 / 「変更 N 件」を **ウィンドウ下部に常時表示**（スクロールしても動かない。ScrollView の外側に置く。[09](09_editor_tools.md) §7 の「末尾へ到達できる」を満たすため、バーの高さぶんを ScrollView の下余白にする）。ヘッダーの主操作は残してよいが、バーと同じ状態（再生中は ■ が塗り）を示す |
| 未調整の折りたたみ | 「詳細」モードで、**既定値のままの欄はカードごとに「未調整の設定（N）」として自動で折りたたむ**。変更した欄・必須・検証に引っかかった欄は開いたまま。ヘッダー近くにトグル「未調整を隠す」（既定 ON、EditorPrefs に記憶）。「標準」「かんたん」は従来どおり |
| Params を「調整つまみ」に作り直す | Unity 既定の配列 Inspector をやめ、**つまみ 1 つ = 1 行**で、左に**カテゴリのチップ**（色 / サイズ / 速度 / 強さ / 不透明度 / その他。`Type` と `Label` / `TargetProperty` の語〔color, size, speed, scale, alpha, intensity, strength, rate 等〕から自動判定、手で変更可）、中央に **デザイナー向けの調整部品**（Color → ColorField、Float → Slider〔範囲は Default から自動: 0〜Default×4 または 0〜1〕、Int → SliderInt、Vector → 3 欄、Texture → ObjectField、Curve/Gradient は表示のみ。値は `Default` に書く）、右に ↺。行の「⚙」で **定義の詳細**（Label / Type / TargetProperty / Anim）を開く（上級者向け、既定で閉じる）。上部に **カテゴリのフィルタ**（チップに件数、押すとそのカテゴリだけ）。再生中は既存の「即時反映」（`VfxEditorWindow` の Params 節がやっていること）を使い、動かしながら見える |
| つまみを足す | 「＋ つまみを追加」でパレット（色 / サイズ / 速度 / 強さ / 不透明度 / 寿命 / 自由入力）。パレットの各項目は Label / Type / **TargetProperty の候補**を埋める。候補は **Prefab から実際に取れるもの**（[04](04_vfx.md): Shader プロパティ名 or VFX Graph の exposed 名。`VfxDataValidator` が「TargetProperty が Prefab に存在しない」を Error にする判定と同じ列挙を使い、無いものは勧めない）。候補が複数あればドロップダウン、無ければ「この Prefab には色のプロパティがありません」と出して追加しない |
| Data の切替 | ヒーロー（表示名の上）に **◀ [名前 ▾] ▶**。ドロップダウンは検索付き（`AdvancedDropdown`）で、先頭に「最近開いた」（最大 5 件、SessionState）、続けて `Category` 別に全 VfxData を並べる。◀ ▶ は同じカテゴリ内を表示名順に移る（端で止まり、tooltip に前後の名前）。🔒 固定中でも切り替えられる（Project 選択への追従とは独立）。切替時は再生中のプレビューを止める（Data は SerializedObject 経由で書くので破棄する変更は無い） |
| 視覚部品の継続 | AnchorPad / DurationBar は D のまま。つまみの Color / Float は変更すると行の左端に色バー（D と同じ変更の可視化） |

狙い: 「調整 → 再生」の往復を 0 スクロールにし、Params をデザイナーが「選んで回すだけ」にする。採用時は Params の行 UI を既存 `VfxEditorWindow` の Params 節にも移植する。

### 3 案に共通で入れるもの

- `FieldGuide`（`VfxData` 用。欄ごとに: フィールド名 / 平易なラベル / 一言説明 / 段（必須・よく使う・詳細）/ 目的カード / 検索語 / 既定値判定）。将来はパッケージ側の共通基盤にする前提で、種別に依存しない形（`FieldGuide<TData>` または表のクラス）にする。
- 共通欄（AssetDataBase の 14 欄）は「管理情報」としてまとめ、既定では **DisplayName と Category だけ**を見せる。
- 対象アセットの選択（Project 選択に追従 / 🔒 固定）、▶ プレビュー、検証（`DataValidationSection`）、保存は既存の仕組みを再利用。
- 横幅 500px で見切れない。`ScrollView` ルート。

## 4. 判断のしかた（ユーザー）

`Tools > D-Drive > Prototypes >` の 5 ウィンドウ（A〜E）を、同じ VfxData（例: 確認用データの `Hit` 系）で開いて比べる。観点:

| 観点 | 問い |
|---|---|
| 最初の 1 つ | 初めての人が Prefab を入れて ▶ まで迷わず行けるか |
| 2 回目以降 | 慣れた人が目的の欄にすぐ辿り着けるか |
| 移植のしやすさ | 他の 19 ウィンドウ（特に Canvas / Presentation）に同じ型を当てられるか |
| 便利機能 | 「次にやること」「既定に戻す」「雛形」「検索」「変更済みだけ」のどれが効くか（混ぜてよい） |

決まったら [1004](1004_tasks.md) に本実装のチケット（共通部品 → VFX → Canvas → Presentation → 残り）を切る。

## 5. 実装メモ

**2026-10-10 追記(UX-0、サンプル 3 本)**

- 置き場: `Assets/EditorPrototypes/Editor/`（asmdef `DDrive.EditorPrototypes` = Editor 専用・autoReferenced=false。参照は `DDrive.Foundation` / `DDrive.Runtime` / `DDrive.Editor`。パッケージには入れない）。
- ファイル: `PrototypeMenu.cs`（メニュー `Tools/D-Drive/Prototypes/` の A / B / C。`[DataEditor]` は付けない）/ `FieldGuide.cs`（`FieldTier`・`FieldGuideEntry`・種別非依存の `FieldGuide` 抽象と検索一致）/ `VfxFieldGuide.cs`（VfxData 全固有欄 + 共通欄の表。節 = A 用、目的カード = C 用）/ `FieldGuideUi.cs`（`GuidedField` = 案内付き 1 欄、`FieldGuideUi.MakeField(so, defaultSo, entry, showResetButton)`、読み取り専用の管理情報、カード枠）/ `PrototypeWindowBase.cs`（対象追従 + 🔒、▶ プレビュー〔`SceneVfxPreviewDriver`〕、リピート・速度、確認用シーン、`DataValidationSection`、既定値比較）/ `PrototypeAWindow.cs` / `PrototypeBWindow.cs` / `PrototypeCWindow.cs`。
- 段の根拠: `VfxDataValidator` が Error にするのは Prefab のみ → 必須。普段触る欄と Warning の対象（LifeMode・Duration・Render・Anchor・Params・DisplayName・Category）→ よく使う。既定のままで警告が出ない欄 → 詳細（FadeOutSec・AnchorId・RenderLayer・LightLayerMask・Flags・Events・その他の共通欄）。
- 既定に戻す: `ScriptableObject.CreateInstance<VfxData>()` を既定値の基準にし、`SerializedProperty.EqualContents` で比較。違う欄にだけ「●」と「↺」が出る。戻すときは `Undo.RecordObject` + `CopyFromSerializedProperty` + `SetDirty`。
- 比べ方: Project で `Assets/GameData/Vfx/Player/VFX_Player_Slash`（斬撃 1、Prefab 設定済み）か `斬撃2` を選び、3 ウィンドウを開く（選択に追従）。Prefab を外した状態で A の「次にやること」と B の「次へ」が止まる様子、LifeMode を Loop に変えて ● と ↺ が付く様子（A / C）、C の検索に「高さ」「ループ」「レイヤー」を入れる様子を見るとよい。B は雛形を押してから 2 → 3 → 4 と進める。
- 割り切った点: 対象は VfxData のみ。B の 2D パッドは X/Z のみ（±3m 固定、Space が World 以外でも相対値として書く、Undo / 外部変更でパッドは再描画されない）。A の「次にやること」は 3 手固定（Prefab → ▶ → 保存）。C の「？」は VFX Editor のマニュアルページを開くだけ（欄ごとの節には飛ばない）。「スポーン先」「複数同時再生」「Anchor の SceneView ハンドル」「Params 定義の編集支援」は既存 `VfxEditorWindow` 側にだけある。`MaskField` ではなく既定の `PropertyField` で LightLayerMask を出す。`RenderLayer` だけ `LayerField`。
- 検証: コンパイル 0 エラー、3 ウィンドウを `menu_execute` で開いて要素生成（A / C = 全 20 欄、B = ページごとに 1〜2 欄）、A のモード切替で表示欄が 1 / 8 / 20、C の検索「高さ」で Anchor のみ、EditMode 2077 green。

**2026-10-10 追記（UX-0b、サンプル D = デザイン重視）**

![サンプル D（Dark / 標準モード）](images/1008_prototype_d.png)

- 追加ファイル（すべて `Assets/EditorPrototypes/Editor/`）: `PrototypeDWindow.cs`（窓本体: ヒーロー・モード・検索・カード構築・検証のインライン化・空状態）/ `PdRows.cs`（`PdRow` = 1 欄、`PdLifeRow` / `PdAnchorRow` / `PdRenderRow` = ビジュアル入力を載せた複合欄、`PdCard` = カード）/ `DurationBar.cs`（長さの横バー）/ `AnchorPad.cs`（位置の 2D パッド + 高さスライダー）/ `PdPainter.cs`（`PdPalette` = Painter2D 用の色と角丸・破線の補助）/ `Theme/PrototypeD.uss`（テーマ。色・余白・文字のすべて）。`PrototypeMenu.cs` に `D デザイン重視` を追加。
- 土台の変更: `PrototypeWindowBase` に `CustomChrome`（true の窓はツールバー・狙い・対象欄を作らず、対象が無くても `BuildBody` が呼ばれる）・`ConfigureRoot`・`Locked` を足した（A〜C は既定 false で動作不変）。
- 表（§3-D）との対応: ヒーロー = アイコン / 表示名（18px 太字）/ ID / カテゴリのチップ / 状態チップ（✓ 検証 OK・⚠ 警告 N・✕ エラー N）/「変更 N 件」チップ（クリックで「変更した設定だけ」）/ 主ボタン（▶ 再生は塗り、他は枠）。段のバッジ = 必須（赤）・よく使う（青）・詳細（灰）+ ヘッダー直下のセグメント（選択中は塗り）。カード = 角丸・薄い枠・見出し 14px + 一言説明 + 右上「？」、欄は 2 段組み（ラベル 172px 固定）、欄が無いカードは「この設定は既定のままで大丈夫です」。ビジュアル入力 = `DurationBar`（OneShot / Duration = 固定長バー + 末尾の余韻を半透明、Loop = ∞、バー上ドラッグで Duration、右の空き地のドラッグで FadeOut、Shift で 0.25 秒刻み）・`AnchorPad`（1m グリッド・距離リング・原点・現在位置・原点からの破線・ホバー位置・高さのスライダー・X/Z/高さの数値）・Render をセグメント + 表示レイヤーのドロップダウン横並び（詳細のみ）。検索 = ヒーロー右、一致語を `<mark>` で強調（検索語だけに一致したときは説明の末尾に出す）、検索中はモードに関わらず全欄から探す。変更の可視化 = 既定値と違う欄の左端に橙のバー + 「↺」、「↺ すべて既定に戻す」は `DisplayDialog` で確認（管理情報は対象外、Undo 可）。検証 = 該当欄の下にアイコン + 1 行 + 修正ボタン（メッセージ中のフィールド名 / 代表語で欄に結びつけ、結びつかないものは末尾の「その他の検証」）。Error / Warning がある欄はモードで隠れていても見せる。空状態 = 中央の案内 + 破線のドロップ領域（`DragAndDrop` で VfxData を受ける）+ ObjectField + 最近の VfxData。8px グリッド、見出し 14px / 本文 12px / 補足 11px、横幅 500px・`ScrollView` ルート。Light / Dark は root の `pd-light` / `pd-dark` クラスと USS 変数で切り替え（実行中の切替も追従）。C# にインラインスタイルは無い（`display` の出し入れだけ C# から行う）。
- Undo: バー / パッドのドラッグは `Undo.RecordObject` + `SetDirty` で、1 回のドラッグ = 1 回の Undo（`CollapseUndoOperations`）。外部変更・Undo は 150ms の定期更新と `Undo.undoRedoPerformed` で再描画する。
- 割り切った点: 対象は VfxData のみ。Painter2D の色は USS の `--pd-p-*` を 0 サイズの子要素の `resolvedStyle` から読む（`CustomStyleProperty` はテーマ切替のあとに読み直されなかったため）。パッドの縮尺は縦 ±3m 固定（範囲外は端に寄せて警告色）、Space が World 以外でも相対値として書く。リピート再生・速度スライダーは D には無い（A〜C の `BuildPlayRow` はインラインスタイルのため流用していない）。「？」は VFX Editor のマニュアルページを開くだけ。Flags / Params / Events は既定の `PropertyField`（見た目は USS で揃えただけ）。検証メッセージと欄の結びつけは文字列の照合（新しい検査メッセージは「その他の検証」に出る）。
- 見つけた不具合（A〜C にも影響）: `SerializedProperty.EqualContents` は「同じプロパティか」の比較で値の比較ではないため、UX-0 の既定値との比較（`GuidedField.UpdateState`）は常に「変更あり」になっていた。`SerializedProperty.DataEquals` に直した（A〜C の「●」「↺」が実際に違う欄にだけ出るようになる）。
- 検証: コンパイル 0 エラー、スクリーンショットで Dark / Light 両方を確認、モード切替（かんたん / 標準 / 詳細）・検索・DurationBar と AnchorPad のドラッグ（ポインタイベントを送って値と Undo を確認）・外部変更の再描画・「すべて既定に戻す」のダイアログ・空状態を確認。


**2026-10-10 追記（UX-0d、サンプル E = D + フィードバック反映）**

![サンプル E（Dark / 詳細モード、Params の調整つまみと下部バー）](images/1008_prototype_e.png)

- 追加ファイル（`Packages/com.ddrive.core/Editor/Prototypes/`、全型 internal）: `PrototypeEWindow.cs`（D のコードをコピーして改変した窓本体）/ `PeTune.cs`（`PeTunePanel` = 調整つまみの一覧・フィルタ・追加・削除、`PeTuneRow` = `PdRow` 派生）/ `PeProps.cs`（TargetProperty 候補の列挙・カテゴリ推定）/ `DataSwitcher.cs`（Data 切替部品）/ `Theme/PrototypeE.uss`（E 専用の追加。D の `PrototypeD.uss` の変数を共有）。`PrototypeMenu.cs` に `E フィードバック反映` を追加。土台への追加: `PrototypeWindowBase` に `CreateFooter()`（ScrollView の外に置く要素）・`Repeat`・`ApplyParamLive()`、`PdRow.NoFold`、`PdCard` の折りたたみ（`SetFolded` / `SetFoldShown` / `IsFolded`）。D の見た目・動作は変えていない。
- 固定アクションバー: ▶ 再生（再生中は「▶ やり直し」）/ ■ 停止（再生中は赤の塗り）/ リピート / 変更 N 件 / 保存（未保存は「●」）。ルート直下で ScrollView の下に置いた兄弟要素なので、スクロール領域とは重ならず、末尾まで普通にスクロールできる（下余白は不要）。対象が無い空状態では隠す。ヒーローの ▶ ■ 保存はバーに移した（確認用シーンと 🔒 固定は残した）。
- 未調整の折りたたみ: 詳細モードで、必須でなく・既定値のままで・検証に引っかからない欄をカードごとの「▸ 未調整の設定（N）」へ移す。ヒーローのトグル「未調整を隠す」（既定 ON、`EditorPrefs`）。検索中・「変更した設定だけ」中は畳まない。編集中に欄が動かないよう、振り分けは構築・モード切替・トグル・Undo・一括リセットのときだけ行う（折りたたみの中で変更した欄は次の振り分けまでそこに残る。検証の Error / Warning が付いた欄だけは即座に外へ出る）。
- 調整つまみ: 1 つまみ 1 行（分類チップ / 名前 / 部品 / ↺ / ⚙）。部品は Color → `ColorField`（HDR は値が 1 を超えるとき）、Float → `Slider`（範囲は開いたときの値から: 1 以下は 0〜1、超えるなら 0〜値×4。負なら下限も×4）、Int → `SliderInt`、Vector → 3 欄（W は保持）、Texture → `ObjectField`、Curve / Gradient は表示のみ。値は `Default` に書く（`Undo.RecordObject` + `SetDirty`、1.2 秒以内の連続操作は 1 回の Undo にまとめる）。再生中は `ApplyParamLive` が `VfxManager.SetParam`（`VfxEditorWindow` の Params 節と同じ）で即時反映。左端の色バーと ↺ は「このウィンドウで開いたときの値」との差。⚙ は Label / Type / TargetProperty / Anim と「削除」（既定で閉じる）。分類（色 / サイズ / 速度 / 強さ / 不透明度 / その他）は Type と Label / TargetProperty の語から推定し、チップをクリックして手で変えられる（上書きは `SessionState`。`VfxParam` にフィールドは足していないのでシリアライズ形式は不変）。上部のフィルタは件数付き。
- つまみを足す: 「＋ つまみを追加」→ 色 / サイズ / 速度 / 強さ / 不透明度 / 寿命 / 自由入力。TargetProperty の候補は、`VfxDataValidator` が TargetProperty の存在を判定するのと同じ列挙（Prefab の全 `Renderer` の `sharedMaterial`）から、`Shader.GetPropertyName` で実在するプロパティだけを返す（`PeProps.Enumerate`。Validator の `AnyRendererHasProperty` は `DDrive.Runtime` の private で Editor から見えず `InternalsVisibleTo` も無いため、同じ列挙を Editor 側に持った。Validator 側は変更していない）。色 = Color 型、他 = Float / Range 型で名前に size / speed / intensity / alpha / life などの語を含むもの（既に使っている TargetProperty は除く）。候補 1 つなら即追加、複数ならドロップダウン、無ければ「この Prefab には色のプロパティがありません」と出して追加しない。追加する値は材質の現在値。自由入力は TargetProperty 空の Float を足して ⚙ を開く。
- Data の切替: `DataSwitcher`（◀ [名前 ▾] ▶）。E のみ（A〜C は既存の対象 ObjectField、D は空状態の最近一覧で切り替え。`PrototypeWindowBase` には載せていない）。
- 割り切った点: 対象は VfxData のみ。VFX Graph の Exposed 名は候補に出さない（Validator と同じ判定のため）。寿命は材質に該当プロパティが無いことが多く、その場合は追加できない。つまみの分類の手動上書きは Label ごと（Label を変えると推定に戻る）。つまみの ↺ の基準はウィンドウを開き直す・対象を切り替えると更新される。
- 検証: コンパイル 0 エラー。`capture_screenshot` は他アプリが前面で使えなかったため `GUIView.GrabPixels` で窓を画像化して確認（Dark）。下部バーがスクロールで動かない・「未調整を隠す」の ON / OFF で折りたたみが出入りする・フィルタ・追加パレット（サイズ候補なしの文言、不透明度 = `_InvFade`、自由入力）・Slider 3 回の変更が Undo 1 回で戻る・再生中の Slider 操作が実インスタンスの MaterialPropertyBlock に即時反映される・Data 切替（ドロップダウンの項目数 = VfxData 件数 3、◀ ▶ で対象が変わる）を確認。

**2026-10-10 追記（UX-0f、E を旧 VFX Editor と機能同等にする）**

ユーザーのフィードバック: 「Prefab で開くボタンが無い」「RenderLayer など調整できない項目がある」「Anchor Editor で開くなど、旧エディターの便利機能が消えている」。旧 `VfxEditorWindow`（`VfxEditorWindow*.cs`）の全機能を棚卸しし、無い・一部のものをすべて E に入れた。

| 機能 | 旧エディターの場所 | E の場所 | 状態 |
|---|---|---|---|
| 🔒 対象を固定 / Project の選択に追従 | ツールバー | ヒーローの主操作列「🔒 固定」 | あり |
| 確認用シーンを開く（右クリックで配置） | ツールバー | ヒーローの主操作列「確認用シーン」 | あり |
| **Prefab を開く**（プレハブモード） | ツールバー | ヒーローの主操作列「Prefab で開く」（Prefab 未設定なら案内ダイアログ） | **追加** |
| Project で表示 | ツールバー | ヒーローの「Project」（アイコンのクリックも従来どおり） | **追加** |
| ＋ 新規作成（`NewAssetDialog`） | ツールバー | ヒーローの「＋ 新規」（作成した Data をそのまま開く） | **追加** |
| 対象アセット欄 / Data 切替 | 上部の ObjectField | ◀ [名前 ▾] ▶ と空状態のピッカー | あり |
| 再生 / 停止 / やり直し | 再生行 | 下部の固定バー | あり |
| リピート | 再生行 | 下部の固定バー | あり |
| **速度**（0.1〜2） | 再生行 | 下部の固定バー「速度」 | **追加** |
| 再生状態の表示 | ステータス文 | ▶ ボタンの文言（やり直し）と ■ の塗り | あり |
| **スポーン先**（シーン内オブジェクト） | 上部の ObjectField | 「出る場所を変える」カードの「出る場所の基準」 | **追加** |
| **Path を一覧から選ぶ**（ボーン / ★AnchorPoint、World なら NamedObject に切替） | Anchor 節 | 同上（`VfxAnchorSceneGui.FillPathMenu`） | **追加** |
| **解決の状況の文**（✓ / ⚠ どこに出るか） | Anchor 節 | 同上（`VfxAnchorSceneGui.DescribeResolution`） | **追加** |
| Space / Path / 高さ / 向き / スケール / 回転追従 / 親消滅後も残す | Anchor 節 | AnchorPad（X / Z / 高さ）+ 詳細の「位置の全設定」（全フィールド） | あり |
| **Anchor Editor で開く** | Anchor 節（AnchorId の横） | 「共通の出る位置（Anchor アセット）」欄の下（AnchorId の設定が無いと灰色） | **追加** |
| **埋め込みをアセット化** | Anchor 節 | 同上 | **追加** |
| Anchor アセット使用中の案内 / 見つからない警告 | Anchor 節 | 同上、AnchorPad の注記 | **追加**（案内文） |
| **SceneView の Anchor 目印・ハンドル**（移動 / 回転、AnchorId なら連鎖表示、描画権 `SceneGuiOwner`） | Anchor 節のトグル + `OnSceneGui` | 「出る場所の基準」の「SceneView に表示」トグル + `PrototypeWindowBase` の `duringSceneGui`。描画は旧エディターと共通の `VfxAnchorSceneGui.Draw` | **追加** |
| AnchorId / Space / Path の変更で再生中の実体を撮り直す | `RestartMainIfPlaying` | `PrototypeWindowBase.OnPropertyChanged` | **追加** |
| Prefab 差し替えで再スポーン | 基本設定 | 同 | あり |
| **カメラ / ライトが無い警告、プレハブモード中の案内** | `RefreshSceneHelp` | ヒーロー直下の HelpBox | **追加** |
| **プレハブモードの開閉・保存で台帳リセット / 再撮り** | `PrefabStage` 購読 | `PrototypeWindowBase` | **追加** |
| 基本設定: Prefab / 寿命モード / Duration / FadeOutSec | 基本設定 | 「出す」カード・DurationBar | あり |
| 描画モード（Render） | 基本設定 | 「出す」カードの「表示先」（詳細でも畳まない） | あり |
| **レイヤー（RenderLayer）** | 基本設定 | 表示先の右に**常に表示**（3D のときは薄く + tooltip） | **直した** |
| **ライトレイヤー（LightLayerMask）** | 基本設定（`MaskField`） | 「出す」カードの `MaskField`（層名付き） | **直した** |
| 共通フラグ（Pool / Pause / Net） | 基本設定 | 「長さ・消え方」カードの Flags | あり |
| UIOverlay のときの注意文 | 上部のラベル | 表示先の下に注意文 | **追加** |
| Params の即時反映 | パラメータ節 | 調整つまみ（E 独自の UI） | あり |
| Params の定義の追加・削除（Label / Type / TargetProperty / Anim） | 「定義の追加・削除」 | 「＋ つまみを追加」と行の「⚙」 | あり |
| イベント（OnSpawn / OnLoop / OnDestroy → SE / VFX 連携） | イベント節 | 「音・イベントと合わせる」カード | あり |
| **複数同時再生**（最大 8 スロット） | 複数同時再生節 | 「複数同時再生」カード（詳細。▶ / ■） | **追加** |
| 検証（Validator と同じ結果、修正ボタン） | 検証節 | 欄の下のインライン表示 + ヒーローの状態チップ | あり |
| マニュアルを開く | （ツールバーに無い） | 各カードの「？」 | あり |
| Undo / Redo の同期 | `OnUndoRedo` | `OnUndoRedo`（再描画 + 検証 + Anchor 反映） | あり |
| 再コンパイル / PlayMode 遷移に耐える状態 | `[SerializeField]` | 対象・固定・リピート・速度・スポーン先・SceneView 表示を `[SerializeField]` | あり |
| シーン切替でプレビューを止める | `OnActiveSceneChanged` | 同 | あり |

「調整できない欄」の確認: `VfxData`（`AssetDataBase` を含む）の全フィールドを `VfxFieldGuide` の表と `SerializedObject` の全プロパティで突き合わせた。編集できない欄は Id / Version / Author / UpdatedAt（保存時に自動記録）と、HideInInspector の ImportSourceGuid / SchemaVersion だけ。それ以外はいずれかのカードに出る（詳細の「未調整の設定」に畳まれていても開けば編集できる）。直したのは RenderLayer（詳細モードでしか出ず、未調整だと畳まれていた）と LightLayerMask（数値欄だった）。表の RenderLayer / LightLayerMask の目的カードは「出す」にした。

**保存の仕組み（E）**

- 欄を編集すると Data が dirty になり、フッターの「保存 ●」が点く。保存は 3 つの経路: 「保存」ボタン / Ctrl+S（File > Save）/ 自動保存。いずれも `DDriveAssetSave.SaveDirty(Target)`（対象 1 個だけ）で、**版数・最終更新者・日時は保存時に進む**（機械的な一括処理用の `SaveAllSuppressed` は使わない。A〜D の保存ボタンも同じ経路に直した）。
- 自動保存はフッターの「自動保存」（既定 OFF、EditorPrefs）。ON なら、Undo の 1 ステップが確定してから 1 秒何も起きなければ保存する（スライダーのドラッグ中は保存しない。再生中でも可）。
- 未保存のまま別の Data へ切り替える（◀ ▶・ドロップダウン・選択追従）・ウィンドウを閉じるときは「保存する / 保存しない」を聞く（自動保存 ON なら聞かずに保存）。

- 共通部品: 旧エディターの SceneView 描画・解決の文・Path 一覧を `DDrive.Editor.Vfx.VfxAnchorSceneGui`（Editor アセンブリの static。公開 API ではない）に切り出し、`VfxEditorWindow` もこれを使う。互換性スナップショットは変わらない。追加ファイル: `PeAnchorTools.cs`（出る場所の基準 / Anchor アセット / ライトレイヤー / 複数同時再生の行）。
- 検証: コンパイル 0 エラー。`execute_code` で Prefab で開く（プレハブモードに入る）・Anchor Editor で開く（窓が開く）・RenderLayer と LightLayerMask の変更と Undo・速度・スポーン先と Path 一覧・複数再生の ▶ / ■・自動保存（1 秒後に版数が進む）と切替時の保存を確認、`GUIView.GrabPixels` の画像で崩れを確認。

![サンプル E（機能同等、「出る場所を変える」カード）](images/1008_prototype_e.png)

## 6. 試験版（v1.7.0-preview.1、2026-10-10）

サンプル A〜D（E は 2026-10-10 に追加、次の試験版に含める）を持ち込み先（MS2026 等）で試せるよう、プレリリース `v1.7.0-preview.1` として配布する。確認後にタグとサンプルは削除する（後始末 = [1001](1001_open_items.md)）。

- 場所: `Packages/com.ddrive.core/Editor/Prototypes/`（asmdef `DDrive.Editor.Prototypes`、Editor のみ・全型 internal。公開契約・互換性スナップショットには影響しない）。
- 試し方: 更新ウィンドウ（`Tools > D-Drive > Update`）の「更新先の版」一覧からプレリリースを明示選択 → manifest が `#v1.7.0-preview.1` になる。`Tools > D-Drive > Prototypes` の 5 本（A〜E）を VfxData で開いて比べる。
- サンプル VfxData は、各ウィンドウの空状態（対象未選択）の「サンプル VfxData を作る（vfx_sample）」ボタンで作れる（同梱の `Samples/vfx_sample.prefab` を使う `VFX_Sample_Hit`。既にあれば選ぶだけ）。
- 比べる観点は §4。
