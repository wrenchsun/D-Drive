# 1017. T-Drive アニメーション作業台からの依頼（DD-1 / 2 / 3 / 7 / 9 / 14 / 15）の D-Drive 側対応（TA チケット、設計）

> 2026-10-23 作成。T-Drive の仕様書 [20_animation_workbench_spec.md §8.1](https://github.com/wrenchsun/T-Drive/blob/main/docs/20_animation_workbench_spec.md)「D-Drive 側（要相談）」の 7 件を、D-Drive の実コードと突き合わせてチケット（TA-1〜TA-7、[1004](1004_tasks.md) TA 節）に落とした設計書。実装は Sonnet のエージェントに委任し、レビュー・リリース判断は上位モデルが行う（MS / X 節と同じ運用）。

## 0. 要約

| 項目 | 内容 |
|---|---|
| 出典 | T-Drive `docs/20_animation_workbench_spec.md` §8.1（ID は T-Drive の `docs/21` コードレビューの番号）。T-Drive 側でできる対応（ログ・マニュアル・既定で `EVT_` を出さない・検証の警告）は済んでいる |
| 背景 | T-Drive のアニメーション作業台（Maya）が、MS2026 の命名規則（`SK_<ModelId>.fbx` / `A_<ModelId>_<動作>.fbx`）でモデル・アニメーションを分割出力し、ショット（`<Shot>.fbx` + `<Shot>__<ModelId>.fbx`、D-Drive のカットシーン規則そのまま）を出す。D-Drive の取り込み（`ImportRuleService` / `CutsceneImportService`）がそれを正しく受けられるようにする |
| 範囲 | **Editor 専用の取り込み経路のみ**（`Packages/com.ddrive.core/Editor/Import/`・`Editor/Cutscene/`）。ランタイム・ネット・Data のシリアライズ形式（`CutsceneImportProfile` の欄の追加を除く）・公開 API の変更は無い。すべて追加のみ（[42](42_distribution.md) §5.4 = MINOR、v1.11.0 見込み） |
| 触る契約（承認済み 2026-10-23） | E-17 (c)（[42](42_distribution.md) §5.14「D-Drive が `ModelImporter` に書くのは `animationType` / `avatarSetup` / `sourceAvatar` だけ」）を **`SourceAssets/Cutscene/` と `SourceAssets/Anim/` の FBX に限って** `preserveHierarchy` / `importAnimatedCustomProperties` の 2 つ（初回取り込みのみ）に広げる（TA-2 / TA-3、§3 Q-1）。`SourceAssets/Model/`（Toon の `SK_` モデル）には今までどおり触らない |
| 検証の限界 | 実 Maya 素材（T-Drive の出力）がこの環境に無いため、EditMode テストはコードで組んだ `AnimationClip` / 既存サンプル FBX（UnityChan）のコピーで行う。**アニメ付きユーザープロパティ（`ddEvent`）が Unity 6 の取り込みで取れるか・階層の道筋・カットのステップ化は、T-Drive の実出力で人が確認する**（§5、[1001](1001_open_items.md) に行を足す） |

## 1. 依頼と実コードの突き合わせ

| ID | 依頼 | 実コードでの確認（2026-10-23） | 判断 → チケット |
|---|---|---|---|
| DD-1 | `SK_<ModelId>.fbx` の ModelData 識別子が `SKModelId` になり、カットシーンの `<Shot>__<ModelId>` と一致しない。識別子を作る前に `SK_` / `SM_` を外す案 | `ImportRuleService.cs:495-496` が `Path.GetFileNameWithoutExtension` → `AssetNamingService.ToIdentifier`（`_` を落として次を大文字化）。`CutsceneImportService.FindModelDataByIdentifier(:654-671)` は ModelData の**ファイル名の最後の `_` 区切り**（`MODEL_<cat>_<識別子>` の識別子）と完全一致で探す。したがって `MODEL_Chara_SKMisotenChara` は `MisotenChara` に当たらない。**依頼どおり** | **TA-1**: Model 種別の取り込みで `SK_` / `SM_` を識別子の元から外す + `FindModelDataByIdentifier` に接頭辞つきの救済 |
| DD-2 | `EVT_<キー>` + `ddEvent` を読むコードが無く、`EVT_` が小物のトラックになる。小物から除いて Signal マーカーにする実装（`importAnimatedCustomProperties` も要る） | `ProcessCameraPropsFile(:351-360)` はカメラ以外のルート直下の子を全部 `ProcessPropChild` に渡す。`EVT_` の除外は無い。[26](26_timeline.md) §5.5 / §7.3 のとおり 6-10c で「未検証・不採用」。`CutsceneSignalNotification.Key`（`cutscene/xxx` 規約）と `CutsceneSignalTrack` は既にある。**依頼どおり**（ただし実 FBX での検証は人） | **TA-3**: `EVT_` を小物から除外（検証できなくてもこれは確実に効く）+ `ddEvent` の立ち上がりを Signal マーカーにする（`importAnimatedCustomProperties` を Cutscene のカメラ + 小物 FBX に初回のみ ON） |
| DD-3 | トップレベルが 1 つの FBX はそのノードがプレハブのルートになり名前がファイル名に替わる（階層の道筋がずれる）。Cutscene / Anim の FBX に `preserveHierarchy = true` を提案 | `CutsceneFbxPostprocessor.OnPreprocessModel` は `animationType` / `avatarSetup` / `sourceAvatar` しか書かない（E-17 (c) の静的検査 `ExternalContractStaticScanTests` が `preserveHierarchy` への代入を禁止している）。`SourceAssets/Anim/` には前処理が無い。Humanoid のキャラ FBX は筋肉カーブなので影響が薄いが、小物（Generic）と `A_*.fbx` を Generic で使うときに効く。**依頼どおり、ただし E-17 の改定が要る**（§3 Q-1） | **TA-2**: Cutscene / Anim フォルダの FBX に限り初回取り込み（`importSettingsMissing`）で `preserveHierarchy = true` |
| DD-7 | Ubercam のカットはステップだが、ベイクで毎フレームのキーになる。D-Drive は写すだけ（`StepFps` 既定 0）なので 60 fps 再生で 2 台の間の絵が 1 コマ出る可能性。カットを検出してステップにする案 | `CutsceneCameraCurveExtractor.Extract` はカーブをそのまま写す。`CutsceneCameraClip.StepFps` は時刻の量子化であってカットの検出ではない。T-Drive の Ubercam はキーを毎フレーム打ち、区間の切り替わりは隣のキーで位置が飛ぶ。**依頼どおり** | **TA-4**: 抽出後に「隣り合うキーで位置 / 回転が閾値を超えて飛ぶ」箇所を検出し、その 2 キーの接線を Constant（ステップ）にする。閾値は `CutsceneImportProfile` |
| DD-9 | 先頭フレームのイベントは `ddEvent` が 1 のキーだけで 0 → 1 の変化が無い。「最初に 1 になったフレーム（先頭を含む）」と読む案 | TA-3 の読み方の規則として取り込む。**依頼どおり** | **TA-3** に含める |
| DD-14 | `humanoid.json` / `animset.json` の写しを `SourceAssets/Model/<cat>/` に置くと「対象外の拡張子」の案内ログが出る | `ImportRuleService.cs:293-340` の案内ログ。種別フォルダが正しく拡張子が対象外なら `hints.UnsupportedExtension` に入る。害は無いが毎セッション 1 回出る。**依頼どおり**（ログの対象外にする） | **TA-6**: 付帯ファイルの拡張子（`.json` / `.txt` / `.md`）を案内ログの対象から外す |
| DD-15 | 小物のトラックに FBX 全体のクリップを渡しているので、カーブの道筋 `PRP_Mic/...` が小物に当たらない可能性 | `ProcessPropChild(:371-384)` → `BuildOrUpdateAnimationRoleTrack` に `clip`（FBX 全体）をそのまま渡す。Animation トラックは `SpawnModel` で生成した小物の ModelData のインスタンスに束縛されるので、クリップ内の道筋 `PRP_Mic`（ルート直下の子）はインスタンスのルート（= 小物自身）に当たらない。**依頼どおり**（既存の穴。キャラ側は Humanoid なので無関係） | **TA-5**: 小物ごとに「そのノード以下のカーブだけ、道筋をそのノード起点に付け替えた」サブクリップを切り出して渡す |

スコープ外（T-Drive に返す）: `A_<ModelId>_<動作>.fbx` の AnimData 識別子は `AMisotenCharaIdle`（`ANIM_<cat>_AMisotenCharaIdle`）になる。T-Drive 仕様 §6.2 の「`ANIM_<Category>_A_<ModelId>_<動作>`」は `_` が落ちる点だけ違う。依頼票に入っていないので変えない（`A_` を外すと `Idle` だけになり衝突するため、外さないのが正しい）。

## 2. チケット別設計

### 2.0 共通方針

- **すべて `Packages/com.ddrive.core/Editor/`**。Runtime を触らない。新しいクラスは `CutsceneShotParser` と同じく **AssetDatabase 非依存の純ロジック**を切り出し、EditMode テストで検証する
- 例外で止めない（CLAUDE.md §0-4）。取れないものは `report.Log` の警告 / 情報 + 従来どおりの結果
- 再取り込みの流儀（[26](26_timeline.md) 実装メモ (2)）を守る: **自動生成分だけ差し替え、デザイナーの追加は保持**
- `CutsceneImportProfile` の欄は追加のみ（`[Tooltip]` 付き、既定値は「従来と同じ結果」になる値にはしない = 新しい既定で動くが、0 や OFF で従来に戻せる）
- 1 チケット = 1 コミット。各チケットで docs の該当節に 2026-10-23 付きで追記（§4 の表）

### 2.1 TA-1（DD-1）: Model 種別の取り込みで `SK_` / `SM_` を識別子から外す

- `AssetNamingService` に **public** `static string StripModelFilePrefix(string rawName)` を追加: 先頭が `SK_` または `SM_`（大文字・`_` 必須。`Skeleton_` は対象外）なら外して返す。接頭辞のリストは `public static readonly string[] ModelFilePrefixes = { "SK_", "SM_" }`（MS2026 `Docs/Rules/FolderStructure.md` §3 が出典。コメントに書く）
- `ImportRuleService.ProcessOne`（`:495` 付近）で、`handler.Target == AssetType.Model` のときだけ `identifier = ToIdentifier(StripModelFilePrefix(rawName), fallback)`。**表示名（`rawName`）は変えない**（`SK_MisotenChara` のまま。ファイル名で探せるように）。`IImportRuleHandler` にメンバーを足さない（外部実装がある公開インターフェース）
- 外した結果が空か数字始まりなら従来どおり fallback が前置される（`SK_.fbx` → `Model`）
- `CutsceneImportService.FindModelDataByIdentifier`: 完全一致で見つからなければ、最後の区切りが `"SK" + identifier` / `"SM" + identifier` と一致するものも採用する（TA-1 より前に取り込まれた `MODEL_*_SKMisotenChara` の救済。T-Drive が「取り込み後に識別子を直す」手順を書いている既存データも、直さなくてもバインドが埋まる）
- 既存の取り込み済み Data は `ImportSourceGuid` で**スキップ**されるので名前は変わらない（[42](42_distribution.md) §5.9 生成コードは不変）。新規取り込みだけが新しい名前になる
- テスト: `AssetNamingServiceTests`（`StripModelFilePrefix` の表: `SK_Hero` → `Hero`、`SM_Rock` → `Rock`、`Skeleton` → そのまま、`sk_hero` → そのまま、`SK_` → 空）、`ImportRuleServiceTests`（`SK_X.fbx` を `SourceAssets/Model/Test/` に置くと識別子 `X`・表示名 `SK_X`。`.fbx` のフィクスチャは既存テストが使う UnityChan のコピーでよい）、`CutsceneImportServiceTests`（`MODEL_T_SKHero` があるとき識別子 `Hero` で見つかる）

### 2.2 TA-2（DD-3）: Cutscene / Anim フォルダの FBX に `preserveHierarchy`

- `CutsceneFbxPostprocessor.OnPreprocessModel`: Cutscene フォルダ配下で `assetImporter.importSettingsMissing`（= 初回取り込み）のときだけ `modelImporter.preserveHierarchy = true`。キャラ FBX・カメラ + 小物 FBX の両方（`isCharacter` の分岐より前）。2 回目以降（`.meta` がある）は触らない = デザイナーが Inspector で変えた値を上書きしない
- `SourceAssets/Anim/` の FBX: `Editor/Import/` に新規 `AnimFbxPreprocessor : AssetPostprocessor`（`OnPreprocessModel`。`ImportRuleService.DefaultSourceRoot + "/Anim/"` 配下・`importSettingsMissing`・`.fbx` のみ）。`ImportRuleService` の `Suppress` 相当（テスト用の抑止）を同じ流儀で持つ
- **E-17 (c) の改定**（[42](42_distribution.md) §5.14 の表と `ExternalContractStaticScanTests.ForbiddenModelImporterWrite`）: 「`preserveHierarchy` / `importAnimatedCustomProperties` は `SourceAssets/Cutscene/` と `SourceAssets/Anim/` の FBX に限り、初回取り込みのみ書く」を追記し、静的検査は **`CutsceneFbxPostprocessor.cs` / `AnimFbxPreprocessor.cs` の 2 ファイルだけ**この 2 プロパティの代入を許す（ファイル名の許可リスト。他のファイルで書いたら今までどおり赤）。`Model` フォルダ（Toon の `SK_` モデル）は不変なので T-Drive の Toon 側の前提は壊れない。CHANGELOG の互換性節に「契約の明文化・広げたのは Cutscene / Anim の 2 プロパティだけ」を書く
- テスト: `ExternalContractStaticScanTests` の許可リストの効き（許可ファイル以外の代入を検出する control）、`CutsceneImportServiceTests` に「初回取り込みで `preserveHierarchy` が true になり、Inspector 相当で false に戻して再インポートしても true に戻らない」

### 2.3 TA-3（DD-2 + DD-9）: `EVT_` を小物から除外し、`ddEvent` の立ち上がりを Signal マーカーにする

**確実に効く部分**（実 FBX が無くても動く）:

- `CutsceneShotParser` に `public static bool IsEventLocator(string nodeName, out string key)`: 名前空間（`:`）以降を捨て、`EVT_` 接頭辞なら true、`key` = 接頭辞を外した残り（`EVT_Hit` → `Hit`、`EVT_Hit1` → `Hit1`。Maya が重複で付けた数字は外さない: `Combo2` と区別できないため。T-Drive 側の検証が重複を警告している）
- `ProcessCameraPropsFile` のループで `IsEventLocator` が true の子は `ProcessPropChild` に渡さない（**小物トラック + 「ModelData('EVT_Hit') が見つかりません」の警告が出なくなる**）。代わりに下のマーカー生成へ

**要検証の部分**（アニメ付きユーザープロパティ）:

- `CutsceneFbxPostprocessor.OnPreprocessModel`: Cutscene フォルダのカメラ + 小物 FBX（`!isCharacter`）で `importSettingsMissing` のとき `modelImporter.importAnimatedCustomProperties = true`（TA-2 と同じ初回のみ・E-17 の許可リスト内）
- `CutsceneFbxPostprocessor.OnPostprocessGameObjectWithAnimatedUserProperties(GameObject go, EditorCurveBinding[] bindings)`: Cutscene フォルダのときだけ、`propertyName` が `ddEvent`（`CutsceneImportProfile.EventPropertyName`、既定 `"ddEvent"`）の束縛を `type = typeof(Transform)` に揃えてクリップに残す（型が null の束縛は取り込まれない。Unity の FBX 取り込みでユーザープロパティの既定の束縛先が版で違うため、ここで明示する）。それ以外の束縛は触らない
- `CutsceneImportService.ProcessCameraPropsFile`: `AnimationUtility.GetCurveBindings(clip)` から `path` が `EVT_` ロケーターのノード（`AnimationUtility.CalculateTransformPath(child, root)`）に一致し `propertyName` が `EventPropertyName` のカーブを探す（`type` は見ない）。見つかれば `CutsceneEventCurveReader.FindRisingEdges(curve, frameRate)`（新規・純ロジック）で**「値が 0.5 以上になった最初のフレーム、その後 0.5 未満に戻ってから再び 0.5 以上になったフレーム」**を列挙する（先頭キーが既に 1 なら先頭フレームが 1 件目 = DD-9。整数のステップキーを想定するが、補間されていても閾値 0.5 で同じ結果）。見つからなければ `report.Log($"情報: {assetPath} の '{child.name}' にイベントの時刻({EventPropertyName})が取れませんでした(Signal マーカーは Timeline で手で置いてください。[26] §5.5)")`
- マーカーの置き場: `CutsceneSignalTrack` を **固定名 `"Events (FBX)"`**（`CutsceneImportService.EventTrackName` 定数）で 1 本作り、**そのトラックのマーカーは再取り込みのたびに全部作り直す**（= 自動生成の単位。デザイナーが手で置くマーカーは別の Signal トラックに置く。docs とマニュアルに書く）。`CutsceneSignalNotification.Key` = `profile.EventKeyPrefix + key`（既定 `"cutscene/"` → `cutscene/Hit`。大文字小文字は Maya の名前のまま）。`time` = フレーム / `clip.frameRate`（`SourceFrameRange` でトリムするときは開始フレーム分を引く。`CutsceneFrameRangeTrimmer` と同じ基準）
- `CutsceneImportProfile` に追加: `string EventPropertyName = "ddEvent"`、`string EventKeyPrefix = "cutscene/"`、`bool ImportEventLocators = true`（OFF なら除外だけ行い、マーカーは作らない）
- テスト: `CutsceneShotParserTests`（`IsEventLocator` の表）、新規 `CutsceneEventCurveReaderTests`（コードで組んだカーブ: 0→1 1 回 / 先頭から 1 / 0→1→0→1 / 全部 0 / キー無し）、`CutsceneImportServiceTests`（UnityChan のコピーには `EVT_` ノードが無いので、**サブアセットとして `EVT_Hit` 子を持つ Prefab を FBX の代わりに使えないか**を確認し、無理なら `BuildEventMarkers(timeline, clip, eventNodes, profile, report)` を internal に切り出してコードで組んだ `AnimationClip` で検証する。「再取り込みで `Events (FBX)` のマーカーが作り直され、別名の Signal トラックのマーカーは残る」を含める）
- [26](26_timeline.md) §5.5 の「イベント用ロケーター(任意・要検証)」と §7.3 を「D-Drive 1.11.0 で実装。実 FBX での確認は人（[1001](1001_open_items.md)）」に書き換え、T-Drive 側へ「`export.includeEvents` の既定を true に戻せる」と伝える文面を §4 に置く

### 2.4 TA-4（DD-7）: カメラのカットを検出してステップにする

- `CutsceneCameraCurveExtractor` に `public static int ApplyCutSteps(CutsceneCameraClip target, float positionThreshold, float rotationThresholdDeg)`（純ロジック。戻り値 = ステップにしたカット数）。`Extract` の末尾で `profile` の閾値を使って呼ぶ（`Extract` の署名は変えず、`CutsceneImportService.BuildOrUpdateCameraTrack` が `Extract` の後に呼ぶ）
- 判定: 位置 3 カーブに共通のキー時刻の並び（キーは毎フレーム打たれている前提。時刻が揃っていなければ `PosX` のキー時刻を基準に他を `Evaluate`）で、隣り合うキー i, i+1 の**位置の距離 > `positionThreshold`（単位は Unity の長さ、既定 1.0）** または **回転の角度差 > `rotationThresholdDeg`（既定 60°）** ならカット。0 以下の閾値はその判定を無効
- 処理: カットの両側のキー（i の右接線・i+1 の左接線）を `AnimationUtility.SetKeyRightTangentMode / SetKeyLeftTangentMode(…, AnimationUtility.TangentMode.Constant)` にする。対象は **位置・回転・画角・焦点距離・ピント・絞りの全カーブ**（カットで同時に切り替わるため。該当時刻にキーが無いカーブは触らない）
- 30 fps のデータを 60 fps で再生しても、カットの間のフレームは「前のカメラの姿勢」に留まる（補間の絵が出ない）。既存の `StepFps` とは独立（両方効く）
- `CutsceneImportProfile` に追加: `float CameraCutPositionThreshold = 1f`、`float CameraCutRotationThresholdDeg = 60f`
- 再取り込みのたびにカーブは上書きされるので、毎回適用する（デザイナー設定ではなくカーブ由来）
- テスト: `CutsceneCameraCurveExtractorTests` にコードで組んだカーブ（毎フレームのキーで 10 フレーム目に 5 m 飛ぶ → 9 と 10 のキーの接線が Constant・`Evaluate(9.5/30)` が 9 の値 / 閾値 0 で何もしない / 回転だけ 90° 飛ぶ / ゆっくり動くカメラは触らない）

### 2.5 TA-5（DD-15）: 小物ごとにサブクリップを切り出す

- 新規 `Editor/Cutscene/CutsceneSubClipExtractor`（純ロジック）: `public static AnimationClip ExtractForNode(AnimationClip source, string nodePath)`。`source` の束縛のうち `path == nodePath` または `path.StartsWith(nodePath + "/")` のものだけを新しいクリップに写し、`path` を**そのノード起点に付け替える**（`PRP_Mic` → `""`、`PRP_Mic/Lid` → `Lid`）。`frameRate` / `wrapMode` / `legacy` を写す。該当カーブが 1 本も無ければ **null**（= 呼び元は従来どおり全体クリップを渡し、情報ログ）
- `ProcessPropChild`: `nodePath = AnimationUtility.CalculateTransformPath(child, root.transform)` でサブクリップを作り、`PersistTrimmedClip(timeline, sub, "<ファイル名>_<小物名>")` で `.playable` のサブアセットとして永続化（既存の仕組みを再利用。再取り込みは同名サブアセットの中身を差し替える）。`SourceFrameRange` のトリムは**サブクリップに対して**行う（順序: 切り出し → トリム）
- 小物が Generic の ModelData（Prefab のルートに Animator）で、ルートの Transform カーブが `""` の道筋で当たる。Prefab 側の子の名前が Maya と同じなら子のカーブも当たる（違えば当たらないのは Unity の標準どおり。docs に書く）
- テスト: 新規 `CutsceneSubClipExtractorTests`（コードで組んだクリップ: `PRP_Mic` と `PRP_Mic/Lid` と `CAM_Main` の束縛 → `PRP_Mic` で切り出すと 2 本・道筋 `""` と `Lid`・`CAM_Main` は入らない / `PRP_Micro` を `PRP_Mic` で切り出さない〔前方一致ではなく `/` 区切り〕/ 該当無しで null）、`CutsceneImportServiceTests` に「小物トラックのクリップがサブアセットで、束縛の道筋がノード起点」

### 2.6 TA-6（DD-14）: 付帯ファイルを案内ログの対象から外す

- `ImportRuleService` に `private static readonly HashSet<string> CompanionExtensions = { ".json", ".txt", ".md" }`（コメント: T-Drive の `humanoid.json` / `animset.json` の写し。[1017] DD-14）。案内ログの収集（`:293-340` の `UnsupportedExtension` に入れる手前）で、この拡張子は何もせず return
- 取り込み対象（`HandlersByFolder`）には元々入っていないので、`.json` を Data にすることは無い。Validation の重さは不変（これはログであって Validator ではない）
- テスト: `ImportRuleServiceTests` に「`SourceAssets/Model/Test/humanoid.json` を置いても `UnsupportedExtension` の案内が出ない」「`SourceAssets/Model/Test/foo.xyz` は今までどおり出る」

### 2.7 TA-7: docs・マニュアル・CHANGELOG・リリース（v1.11.0）

- docs の更新先は §4。`DesignerManual/cutscene-maya-export.html`（イベント用ロケーターの節を「対応済み・`Events (FBX)` トラック・手で置くマーカーは別トラック」に、Ubercam のカットの注意、小物の道筋）。ProgrammerManual の取り込み規則（`SK_` / `SM_`）
- CHANGELOG `[Unreleased]` → `[1.11.0]`。互換性節: 追加のみ（MINOR）+ E-17 (c) の明文化
- リリースは上位モデルが行う（Opus レビュー → P1 対応 → run-ci 全段。Editor 専用だが MINOR の正式版なので軽い経路にしない）

## 3. 決めること（起票時の仮の決め。違っていればここを直す）

| # | 事項 | 仮の決め | 理由 |
|---|---|---|---|
| Q-1 | E-17 (c) を広げてよいか（`preserveHierarchy` / `importAnimatedCustomProperties`） | **広げる。ただし Cutscene / Anim フォルダ・初回取り込みのみ・許可ファイル 2 つ**。**✅ 2026-10-23 ユーザー承認**（「Cutscene / Anim フォルダの FBX・初回取り込みに限って preserveHierarchy / importAnimatedCustomProperties まで広げる」） | 依頼主の T-Drive 自身が求めている。Toon が前提にする `Model` フォルダは不変。[42](42_distribution.md) §5.14 の契約の文言変更なので CHANGELOG に明記し、**ユーザー承認を得る**（承認前に TA-2 / TA-3 を実装してもよいが、リリースはしない） |
| Q-2 | Signal マーカーのキー | `cutscene/` + Maya の名前のまま（`EVT_Hit` → `cutscene/Hit`） | [26](26_timeline.md) §5.5 の例は `cutscene/hit` だが、小文字化すると `FootPlant` が読めなくなる。接頭辞は profile で変えられる |
| Q-3 | カットの閾値の既定 | 位置 1.0（Unity 単位 = m）/ 回転 60° | 30 fps で 1 フレームに 1 m = 30 m/s。通常のカメラワークでは出ない。Maya が cm のときは取り込みのスケール（0.01）後の値で判定するので同じ |
| Q-4 | `EVT_` の除外を profile で切れるようにするか | 切れない（常に除外） | 小物トラックになって困ることはあっても、小物にしたいことは無い |
| Q-5 | `A_<ModelId>_<動作>.fbx` の識別子（`AMisotenCharaIdle`） | 変えない | 依頼に無い。`A_` を外すと動作名だけになり衝突する |

## 4. docs の更新先（各チケットで同じコミットに）

| チケット | 更新する文書 |
|---|---|
| TA-1 | [10](10_workflow.md) 命名・配置規約（`SK_` / `SM_` を外す）、[09](09_editor_tools.md) §1.1 取り込みルール、[26](26_timeline.md) §5.5「Maya のモデル名と ModelData 識別子」 |
| TA-2 | [42](42_distribution.md) §5.14 E-17、[26](26_timeline.md) §5.2 / §5.4、[05](05_model_animation.md) の FBX 取り込み |
| TA-3 | [26](26_timeline.md) §5.2 / §5.5（イベント用ロケーター）/ §7.3、`DesignerManual/cutscene-maya-export.html` |
| TA-4 | [26](26_timeline.md) §4.6.3（StepFps との関係）/ §5.2、マニュアル |
| TA-5 | [26](26_timeline.md) §5.2 / §5.5（小物） |
| TA-6 | [09](09_editor_tools.md) §1.1 案内ログ |
| 全部 | [1004](1004_tasks.md) TA 節の状態列、CHANGELOG `[Unreleased]`、本書 §6 |

## 5. 人による確認（T-Drive の実出力が要るもの。[1001](1001_open_items.md) 1-A に行を足す）

| # | 確認 | 期待 |
|---|---|---|
| 5-1 | T-Drive の出力タブで `SK_MisotenChara.fbx` を D-Drive の `SourceAssets/Model/<cat>/` へ | `MODEL_<cat>_MisotenChara` ができ、表示名は `SK_MisotenChara`。`humanoid.json` / `animset.json` の写しで案内ログが出ない |
| 5-2 | `<Shot>__MisotenChara.fbx` を `SourceAssets/Cutscene/<cat>/` へ | Bindings の Model が自動で `MisotenChara` に埋まる。`preserveHierarchy` が ON で取り込まれ、ルートの名前がファイル名に替わっていない |
| 5-3 | `export.includeEvents` を ON にした `<Shot>.fbx` | `Events (FBX)` トラックに `cutscene/<キー>` のマーカーが、shot.json の `events[].frame` と同じ時刻に並ぶ（先頭フレームのイベントを含む）。`EVT_` の小物トラックと「ModelData('EVT_…') が見つかりません」が出ない。**取れなければ情報ログが出て、他は従来どおり**（この場合は `OnPostprocessGameObjectWithAnimatedUserProperties` の束縛の実際の形を `Debug.Log` で見て TA-3 を直す）。**`EVT_` がカメラ + 小物 FBX のルート直下に来ていること**（T-Drive は Maya で `tdShot_<Shot>` グループの下に置く。Export Selection で親が落ちればルート直下、`tdShot_*` が残っていればそれが小物のトラックになる = T-Drive 側で外す。[reviews/1018](reviews/1018_review_ta_2026-10-23.md) 総評） |
| 5-4 | カメラが 2 区間あるショットを 60 fps の CutsceneData で再生 | カットの前後に 2 台の間の絵が出ない（Timeline ウィンドウのスクラブで切り替わりのフレームを 1 コマずつ） |
| 5-5 | `PRP_Mic` が動くショット | 小物のインスタンスが Maya と同じに動く |

## 6. 実装メモ（実装時に追記）

**2026-10-23 実装（TA-1〜TA-6、Sonnet）。設計から変えた点:**

- **TA-1 / TA-6 は 1 コミット**（`ImportRuleService.cs` を両方で触るため）。TA-6 の既存テスト `ProcessPaths_UnmatchedExtension_IsIgnored` は `notes.txt` を「対象外拡張子」の例にしていたので `notes.xyz` に変えた（`.txt` は付帯ファイルになったため）。TA-1 の統合テストは UnityChan のコピー（DevRepoOnly）。`MatchesPrefixedIdentifier` は `internal`（InternalsVisibleTo が無いので単体テストは `FindModelDataByIdentifier` 経由）。
- **TA-4 / TA-5 は 1 コミット**（`CutsceneImportService.cs` を両方で触るため）。`ApplyCutSteps` は `Extract` の中ではなく `BuildOrUpdateCameraTrack` が `Extract` の直後に毎回呼ぶ（設計どおり）。カットは位置 3 カーブの `Evaluate`（PosX のキー時刻を基準）と回転 4 カーブの `Quaternion.Angle` で判定。
- **TA-5 の順序が設計と違う**: 設計は「切り出し → トリム」だが、`ProcessCameraAndProps` は FBX 全体のクリップを**先に**トリムして `_Trim` サブアセットにしてから小物へ渡すので、実装は「トリム済みクリップから切り出し」（結果は同じ。0 秒起点）。サブアセット名は `<ファイル名>_<小物名>`。取り込み全体の統合テスト（小物付き FBX）は実素材が無く未実施（`CutsceneSubClipExtractor` の単体テストのみ）。
- **TA-2**: 書き込みは `CutsceneFbxPostprocessor.ApplyInitialImportSettings` / `AnimFbxPreprocessor.ApplyInitialImportSettings`（`public static`、InternalsVisibleTo が無いためテストから呼ぶ）。静的検査は `preserveHierarchy` を `ForbiddenModelImporterWrite` から外して別の正規表現 + ファイル名の許可リストに移した。`E17_OnlyAnimationTypeAndAvatarSetup…` の `written` 集合の許可に 2 プロパティを足した。`AnimFbxPreprocessor` は `ImportRulePostprocessor.Suppress` / `ImportRuleService.AutoImport` に従う。
- **TA-3 の `OnPostprocessGameObjectWithAnimatedUserProperties`**: 設計の「束縛の `type` を `typeof(Transform)` に揃えて残す」は実装していない。この通知の `bindings` を書き換えてクリップに反映できるかが Unity のドキュメント・実素材で確認できず、不確実な書き換えは入れない方針（[26] §7.3）。代わりに**束縛を触らず**、1 FBX につき最初の 1 件だけ `ddEvent` の束縛の形（path / type）を `Debug.Log` する診断にした。読み取り側（`BuildEventMarkers`）は束縛の `type` を見ず、道筋 + プロパティ名だけで探す。実 FBX で取れなければ情報ログ + 従来どおり。**§5-3 で束縛の実際の形を見て、必要ならここを直す。**
- **TA-3 のマーカー時刻**: `clip` がトリム済み（0 秒起点）なので、カーブのキー時刻（秒）をそのまま使う（開始フレームの引き算は不要）。`FindRisingEdges(curve, frameRate)` は秒を返す。`BuildEventMarkers` は `public static`（`Action<string>` のログ + `CutsceneEventNode` 構造体）にして、コードで組んだ `AnimationClip` でテストした。再取り込みで `Events (FBX)` が既にあって今回イベントが無いときも、マーカーは空にする（トラックは残す）。
- **Compat**: EditMode 全件（2425 件、Compat スナップショットを含む）が green。`CutsceneImportProfile` は Compat の対象外で、スナップショットの更新は不要だった。

**未検証（実 FBX / T-Drive の実出力が要る）**: §5 の 5-1〜5-5 すべて。特に `ddEvent` が Unity 6 の取り込みで取れるか（5-3）、カットのステップ化の閾値（5-4）、小物のインスタンスが動くか（5-5）。TA-7（マニュアル・リリース）は上位モデルが行う。
