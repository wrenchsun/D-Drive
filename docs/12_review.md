# 12. 実装レビュープロセス

関連: [11_tasks.md](11_tasks.md) / [10_workflow.md](10_workflow.md) / [00_requirements.md](00_requirements.md) §7

---

## 1. レビューの種類とタイミング

```
設計レビュー ─▶ 実装(PR)レビュー ─▶ マイルストーンレビュー ─▶ 受け入れレビュー
 (着手前)        (チケット毎)         (Phase毎デモ)          (M6)
```

| 種類 | 対象 | 参加者 | 成果 |
|---|---|---|---|
| 設計レビュー | 公開 API・データ構造を変えるチケット（各 Phase の *-1 系） | 基盤 + ED + リード | API 署名の承認。以後の変更は再レビュー |
| PR レビュー | 全チケット | 担当外 1 名以上 | 下記チェックリスト + AC 検証 |
| マイルストーンレビュー | Phase 末デモ | チーム全員 + デザイナー代表 | デモ合格 / 改善チケット起票 |
| 受け入れレビュー | 要件 §7 の成功基準 5 項目 | リード + PO | v1 リリース判定 |

## 2. PR 運用ルール

- 1 PR = 1 チケット。500 行超は分割
- PR テンプレに記載必須: 対応チケット番号 / AC の自己検証結果 / スクリーンショット or 動画（エディタ UI 系は必須）
- CI ゲート（全 PR 共通、fail でマージ不可）:
  1. ビルド（Runtime asmdef が Editor 非依存で通る）
  2. EditMode + PlayMode テスト
  3. `ValidateAll`（サンプルデータの Validation green）
  4. ID 定数の再生成差分なし
- コード PR とデータ PR は分離（[10] §2）。データ PR は Validation green + プレビュー動画で足りる（コードレビュー不要）

## 3. コード PR チェックリスト

### 全 PR 共通
- [ ] 禁止 API 不使用（`Instantiate`/`Resources.Load`/`AudioSource.Play` の直接呼び出し — Roslyn Analyzer でも機械検出）
- [ ] ランタイムコードに `UnityEditor` 参照なし
- [ ] 定常経路（Tick/Play/Spawn）に GC alloc なし（LINQ / クロージャ / boxing 禁止）。**2026-09-15(6-2)から自動検証あり**: `Assets/DDrive/Tests/Performance/`(asmdef `DDrive.Tests.Performance`、`com.unity.test-framework.performance` 使用)が Tick 系の定常経路(Pool の Rent/Return・Vfx/Se/CameraFx/Haptics の Tick・Presentation の Tick+Signal・GameLoopDriver の 1 フレーム)を `GC.GetAllocatedBytesForCurrentThread()` の差分で hard assert する(`-testCategory Performance` で実行、Unity Test Runner または `Tools/CI/run-ci.cmd`。GitHub Actions での自動実行は P7 末の CI 導入まで保留、[33] §8 参照)。**既知の未解決 alloc(このチェックでは検出されるが対象外)**: Spawn/Play 系(`VfxManager.SpawnDataLocal`・`AudioManager.PlaySeData`・`PresentationManager.PlayLocalInternal`)は呼び出しごとに Instance(class)を 1 個 new する既存設計(`PresentationInstance` は List×7 + R3 Subject×4 も new する)。Instance プーリングという大きな設計変更が必要なため、対応するテスト(`*_RecordsAllocForKnownIssue`)は Performance レポートに記録するだけで assert しない。新しい Tick/Spawn/Play コードを書くときは、この既知課題を新たな LINQ/クロージャ追加の免罪符にしないこと(既存の allocation を増やさない・可能なら減らす)
- [ ] 例外でなく警告 + no-op / Placeholder で継続する（デザイナーの作業を止めない）
- [ ] public API に XML doc コメント

### Manager / 基盤
- [ ] `Play/Spawn(id, ctx) → Handle` の命名規約準拠
- [ ] Data を書き換えていない（Data は読み取り専用。Instance ごとの現在値は Instance 側に持つ — `ModelsManager.Materials` の例）
- [ ] Manager を new するのは `DDriveRuntimeBootstrap` / テスト / Editor プレビューだけ（[02] §14）。Instance を破棄するときは、その Instance が起動した他 Manager の再生（Anim 等）を止めている
- [ ] Pause / StopAll / OnSceneUnload に応答する
- [ ] Pool の Return パスでリセット漏れなし（Trail/Particle/コールバック解除）
- [ ] Handle の世代チェックが全操作に入っている
- [ ] UniTask のキャンセル（CancellationToken）伝播

### Data / シリアライズ
- [ ] フィールド追加のみ（削除・型変更は移行コード + 移行手順書必須）
- [ ] 既存アセットのデシリアライズ互換テスト
- [ ] 新フィールドに対応する Validator 追加
- [ ] デフォルト値が「安全側」（音量 1、Loop off 等）
- [ ] 調整パラメータが ValueDef で定義されている（生の float + AnimationCurve でない）
- [ ] 編集 UI に ValueDef 共通 Drawer を使用している（種別独自のカーブエディタを作らない）

### Editor / ツール
- [ ] 全操作 Undo 対応
- [ ] `AssetDatabase.CreateAsset` 直後に `CreateFolder` / `Refresh` を挟んでいない（作りたてのアセットが再インポートされ、メモリ上の変更と dirty が消える。フォルダは先に作り、Id 等は `SaveAssetIfDirty` で即確定する。2026-09-08 `AssetCreationService` で実例あり）
- [ ] プレビューが実 Manager 経路（Editor 専用再生コードなし）
- [ ] 専用エディタを持つ Data 種別は EditorWindow に `[DataEditor(typeof(XxxData), "…で開く")]` を付けた（Inspector 最上部の「エディターで開く」ボタン、[09] §8。`DataEditorRegistryTests` が未登録を検出する）
- [ ] 1000 件規模での動作確認（仮想化・遅延ロード）
- [ ] 保存フック（Version/Author/Validation）が動く
- [ ] ドメインリロード・プレビューシーン破棄でリークなし（`NewPreviewScene` の Close 確認)
- [ ] `AssetDatabase.FindAssets` を直接呼んでいない（`DDrive.Editor.AssetSearch.FindAssets` 経由。Unity 6000.3 の `FindAssets` は 1 回ごとに走査ファイル数比例のネイティブメモリを解放せず保持するため、プロジェクト変更までキャッシュする。アセット作成直後に同フレームで検索するなら `AssetSearch.Invalidate()`。2026-09-11 実測、[09] §9）
- [ ] `AssetDatabase.SaveAssets()` を直に呼んでいない（`DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed()` / `SaveDirty(obj)` 経由。引数なし `SaveAssets()` はプロジェクト全体の dirty な `AssetDataBase` を無差別に版数へ乗せるため、実アセットを開いて編集中に別の一括処理が走ると無関係な版数が進む。`Tests/Editor/NoDirectSaveAssetsCallTests.cs` が機械検出する。使い分けは [09] §4.1 の判断表、[44_review_2026-09-19.md] P1-1）

### 互換性（草案・2026-09-20 追加、P チケット完了＝P-13 発効後に必須化）

> **現状は草案（P-2 の成果物）**。[42_distribution.md](42_distribution.md) §5 が定める互換性ポリシーの要約。**P-13 が CLAUDE.md §0 TL;DR に昇格させるまでは参考情報**であり、このチェックリストが red でも今の PR は止めない。発効後は「全 PR 共通」と同格の必須項目になる。

P-13 発効後、以下の互換面のいずれかに触れる PR は、対応するスナップショットテスト（[42] §5.11、`Tests/Editor/`）が green であることを確認してからマージする。**スナップショットテストが赤なら、その PR はマージしない**（[42] §5.0-2）。

| 互換面 | やってよいこと | MINOR（互換を保ったまま追加） | MAJOR（§5.12 の手続きが必須） | 判定テスト（[42] §5.11） |
|---|---|---|---|---|
| シリアライズ形式（`.asset`/`.prefab`/`.unity` に書かれる全フィールド、[42] §5.1） | 既定値が安全側の新フィールド追加（`[Tooltip]` 必須） | フィールド追加 / `[FormerlySerializedAs]` 付き改名 / `[HideInInspector][Obsolete]` を残した型変換（2 段階） | フィールド削除・型の直接変更・struct のフィールド順変更・asmdef 名の変更 | `SerializedLayoutSnapshotTests` + `LegacyAssetFixtureTests` |
| シリアライズされる enum（`AssetType` 含む全部、§5.2） | 末尾への値追加のみ | 末尾追加（MINOR）、値の改名は `[Obsolete]` エイリアス併存で条件付き MINOR | 途中挿入・並べ替え・削除・基底型変更 | `SerializedEnumSnapshotTests` |
| ID・Address・定数名（§5.3） | 変更しないことがやってよいこと | – | `StableHashFromGuid` の算法・`ToConstantName`/`KnownPrefixes`・定数クラス名・Address 規則・カタログ名マッピングの変更はすべて MAJOR（**発効後は例外なし**。[42] §5.13 の「最後のチャンス」は発効前限定） | `IdHashGoldenTests` / `ConstantNameGoldenTests` / `CodegenGoldenTests` |
| 公開 API（`DDrive.Foundation`/`DDrive.Runtime` の `public`、§5.4。`DDrive.Editor` の `public` は対象外） | 型・メンバ追加、オーバーロード追加、既定引数追加 | 同左（MINOR） | 削除・改名・シグネチャ変更・戻り値変更・名前空間移動・asmdef 分割は禁止（先に `[Obsolete]` を 2 MINOR 分挟んでから MAJOR で削除。§5.12） | `PublicApiSnapshotTests` |
| ContentHash・ネットメッセージ（§5.6） | 新しいメッセージ型の追加、既存メッセージへのフィールド追加（欠落時は安全な既定値） | 同左（MINOR） | ハッシュの算法・対象フィールド変更、メッセージの改名・削除・型変更、`NetChannel`/直列化方式の変更 | `CatalogContentHasherGoldenTests` / `NetMessageSnapshotTests` |
| 生成コード（`AssetIds.g.cs`/`Tuning.g.cs`、§5.7） | 新 AssetType の定数クラス追加（データが増えた結果） | 同左（MINOR） | クラス名・名前空間・命名規則・`static readonly` の形の変更、`KnownPrefixes` への追加 | `CodegenGoldenTests` |
| Validation の重さ（Error/Warning、§5.8） | 新しい検査を Warning として追加 | 次の MINOR 以降で Warning→Error に昇格（CHANGELOG に「Error 昇格: XxxValidator」を明記） | 新規検査をいきなり Error にする（緊急時のみ §5.12 の手続きで例外） | `ValidatorSeverityRegistryTests` |
| 依存パッケージ・Unity 版（§5.10） | Unity パッチ版更新、依存の PATCH/MINOR 更新 | 依存追加・Unity マイナー版更新・既存依存の参照範囲拡大（Editor→Runtime 等）（CHANGELOG 必須） | Unity メジャー版更新 | `package.json`/manifest 一致テスト（`PackageVersionConsistencyTests`） |

**手続きの要点**（詳細は [42] §5.12）: MAJOR は (1) issue/設計メモでユーザー承認 → (2) `[Obsolete]`/Warning/移行ツールを 2 MINOR 分先出し → (3) MAJOR で削除 + `CHANGELOG.md`「破壊あり」+ `docs/migrations/vN.md` → (4) スナップショット更新 → (5) 持ち込み先（MS2026）で更新手順を実施し結果を移行ガイドに追記。**MAJOR は年 1 回まで**（MS2026 開発フェーズ中は 0 回）。

CHANGELOG ガード（[42] §5.11-10）: `Tests/Editor/Snapshots/**` が変わった PR で `CHANGELOG.md` が変わっていなければ fail。

**ゴールデンの更新手順（2026-09-20 追加、P-3）**: 上表のスナップショットテストが赤くなったら、まず「意図した変更か」を確認する（MINOR=追加のみ→更新して進める / MAJOR=削除・改名・変更→[42] §5.12 の手続きが必須）。意図した変更なら:

1. 純粋なリフレクション/`SerializedObject` 走査で作れるもの（公開 API・シリアライズ enum・シリアライズ形式レイアウト・ネットメッセージのフィールド一覧・Editor 契約）は `Tools > D-Drive > Compat > スナップショットを更新`（`CompatSnapshotMenu`）を実行する
2. 一時フィクスチャに依存するもの（Tuning コード生成・ContentHash・KnownPrefixes 由来の定数名例・Validator の重さ）は、環境変数 `DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1` を設定してから対象テストを再実行すると、比較の代わりにゴールデンへ書き込む
3. `Tests/Editor/Compat/Fixtures/v1_0_0/*.asset`（旧版フィクスチャ）は Unity Editor 経由（`AssetDatabase.CreateAsset`）で作る。`.asset` をテキストで手編集しない。新しい AssetType を追加したら対応する Data 型のフィクスチャも追加する（`LegacyAssetFixtureTests.AllConcreteDataTypes_HaveFixture` が検出する）
4. 差分を確認したら、同じ PR で `CHANGELOG.md` の `[Unreleased]` 互換性節に「何を・なぜ・MINOR/MAJOR どちらか」を追記する（CHANGELOG ガードの対象）

`AssetIds.g.cs` は Unity 採番の GUID に依存し全文一致ゴールデンにできないため、`CodegenGoldenTests` は生成される 1 行の構文の形だけを検証する（詳細は [42] §5.11 実装メモ）。新しく書く `IValidator` は `ValidationResult` の `Code`（安定した識別子、例: `DD-VFX-003`）を必ず渡すこと（既存呼び出しは省略可能引数のため変更不要だが、新規は必須にする）。

## 4. データ PR（デザイナー）チェックリスト

- [ ] Validation エラー 0 / 警告は理由をコメント
- [ ] 命名規約準拠（[10] §3）
- [ ] タグはタグ辞書から選択
- [ ] プレビュー動画 or スクリーンショット添付
- [ ] Addressable グループ割当済み（CI でも検出）

## 5. マイルストーンレビュー（デモ基準）

| MS | デモ内容（合格基準） |
|---|---|
| M0 | 未登録 ID の Play で Placeholder 動作。Loopback ⇔ NGO 両ブリッジでテスト green |
| M1 | デザイナー本人が SE 登録→試聴→実機再生を 15 分以内で完遂 |
| M2 | VFX の Anchor 調整とUI パーティクルが通常 API で動く。**2 クライアントで同一 VFX/SE が再生される** |
| M3 | Anim の Frame イベント→SE/VFX が同時プレビューで確認できる。Maya FBX からマテリアル自動生成 |
| M4 | UI 遷移（2 画面 + ポップアップ + 戻る）がデータだけで組める。音量設定画面がスクリプト 0 行で完成し、パッドでスライダー操作できる。Prefab がサーバー権威で複製生成される |
| M5 | 剣攻撃 Presentation を **コード変更なしで** デザイナーが調整するライブデモ。**2 クライアント + サーバーで同期再生・Late Join 復元** |
| M6 | 要件 §7 の成功基準 5 項目 + NFR ベンチ達成。ContentHash 不一致検出 |

ネットワーク対応が v1 必須のため、M2 以降のデモは **2 クライアント + サーバー構成で実施**する。シングル動作のみのデモは合格としない。

## 6. 定着レビュー（リリース後の継続運用）

- 週次: Validation レポート（Error/Warning 件数の推移）と未使用アセット数をチームに共有
- 月次: 「デザイナーがコードを触った回数 / プログラマーがアセットを触った回数」をふりかえり — 0 に近いほど責務分離が機能している
- 新種別追加（Timeline/Dialogue 等）時は本ドキュメント群のテンプレ（データ構造/Manager/エディタ/Validation/チケット）に沿って設計レビューから開始
