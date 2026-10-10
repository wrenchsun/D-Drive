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

### 3 案に共通で入れるもの

- `FieldGuide`（`VfxData` 用。欄ごとに: フィールド名 / 平易なラベル / 一言説明 / 段（必須・よく使う・詳細）/ 目的カード / 検索語 / 既定値判定）。将来はパッケージ側の共通基盤にする前提で、種別に依存しない形（`FieldGuide<TData>` または表のクラス）にする。
- 共通欄（AssetDataBase の 14 欄）は「管理情報」としてまとめ、既定では **DisplayName と Category だけ**を見せる。
- 対象アセットの選択（Project 選択に追従 / 🔒 固定）、▶ プレビュー、検証（`DataValidationSection`）、保存は既存の仕組みを再利用。
- 横幅 500px で見切れない。`ScrollView` ルート。

## 4. 判断のしかた（ユーザー）

`Tools > D-Drive > Prototypes >` の 3 ウィンドウを、同じ VfxData（例: 確認用データの `Hit` 系）で開いて比べる。観点:

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

