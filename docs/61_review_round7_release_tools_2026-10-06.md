# 61. 2026-10-06 自前レビュー結果（修正ラウンド 7 = docs/59 の指摘 GB-R-01〜08 への対応 + リリース準備のスクリプト）

> **対象**: 2026-10-06 に main へ入った 2 つの PR。どちらも Sonnet のサブエージェントが実装し、まとめ役は差分を読まずにマージした。**v1.4.0 のタグ前の短い差分レビュー**。
>
> | マージ | PR | 内容 |
> |---|---|---|
> | `f8524da` | #119 | 修正ラウンド 7（[59](59_review_round6_valuedef_2026-10-06.md) の対応）: GB-R-01（尺 0 の揺れ / 振動の Warning 2 種）・GB-R-02（`BgmData` の LoopEnd 検査を実行時の意味に）・GB-R-04（持ち込み先への Tick 登録の案内とコード例、E-9b）・GB-R-06 / 07（全体用 Validator の判定を `Type` に、`ConstantTimeValidationTests` を `ValueDefValidator` だけに）・GB-R-08（`ForbiddenApiWindow.Open`・`ParseVersion` の BOM）・`run-netcheck.cmd` の全 ASCII 化 |
> | `42c958e` | #118 | リリース準備のスクリプト（`Tools/` のみ）: `run-ci.cmd`（全 ASCII 化・Unity が開いていたら FAIL・各段の成果物の検査・テスト XML の `failed` で判定・`ALL GREEN` は実行 + スキップ = 8 段のときだけ）と新規 `check-test-result.cmd` |
>
> **方法**: 専用 worktree を `f8524da`（detached → 本ブランチ）に合わせ、`git diff f8524da^1 f8524da`（31 ファイル）・`git diff 42c958e^1 42c958e -- Tools/`・`git diff 9f40cbb..f8524da -- …/Compat/Snapshots/` と、変更後のファイル全体（`BgmDataValidator` / `BgmManager`〔`Play`・`StartLoopBody`・`Tick` のスプライス〕/ `BgmData` / `AudioEditorWindow` の範囲、`CameraShakeDataValidator` / `HapticsDataValidator` / `CameraFxManager.IsExpired` / `HapticsManager.IsExpired` / `ValueDef.Duration` / `ValueDefValidator.CheckValueDef` / `CameraShakeData`・`HapticsData` の既定値、`ValidatorSeverityRegistryTests`、`GameLoop` / `DDriveRuntimeBootstrap`〔実行順・`Register` の順〕/ `PoolService.Return` / `VfxManager.Tick`、運用ページのコード例・`ExternalGameTimeBehaviour`・E-9b、`DataValidationRunner` と 6 つの全体用 Validator の宣言、`ConstantTimeValidationTests`、`ForbiddenApiWindow`、`run-ci.cmd` / `check-test-result.cmd` / `run-netcheck.cmd` / `Run-NetCheck.ps1`（引数と終了コード）/ `check-release.ps1`（`-GuardOnly`・`-Base`）/ `bump-version.ps1`（引数・書き換え・同期・`-Tag`）、`.github/workflows/ci.yml`、`docs/12` §7、`docs/60` 第 2 章、`CHANGELOG.md` の `[Unreleased]` の該当 3 項）を**読むだけ**で確認した。実装者の報告（[59] の「→ 対応」・CHANGELOG・docs/60）は信用せず、コードと突き合わせた。**Unity は起動しておらず、コンパイル・EditMode / PlayMode テスト・`CI.ValidateAll`・`run-ci.cmd`・NetCheck は一切実行していない**。`check-test-result.cmd` だけは、scratchpad に置いた手書きの結果 XML（Unity の形を模したもの）に対して単体で実行し、パースの挙動（空白・括弧・日本語を含むパス）を確かめた（リポジトリ・メインの checkout には触れていない）。推定のものは「確度」欄に**推定**と書いた。
>
> 前提として読んだもの: `CLAUDE.md`（§0）、[docs/42](42_distribution.md) §5.8、[59](59_review_round6_valuedef_2026-10-06.md)（元の指摘と対応記録。書式と重大度の基準）、[docs/12](12_review.md) §7、`docs/50_consumer_guide/operation.html` の Tick の案内、[docs/60](60_release_1_4_0_prep.md) 第 2 章。

## 総評

- **リリースを止める実バグ（P1）は見つからなかった**。互換面は v1.3.1 から見て追加のみのまま（`git diff 9f40cbb..f8524da -- …/Compat/Snapshots/` は 4 ファイル `+131 / −0`、削除・変更行 0 件。本 2 PR はスナップショットを変えていない）。
- **GB-R-01〜08 は記録どおり解消（見送りは記録どおり）**。新しい Warning 2 種の条件は `IsExpired` の実装（Mode に関係なく `Duration <= 0` で即時失効）と一致し、`ValueDefValidator` の Error との二重報告の除外も漏れがない。UiTween の Motion と BGM のフェードを対象外にした判断も妥当。
- **GB-R-02 の新しい条件は「0 / 0 と Start>0 / End=0 を Error にしない」目的には正しい**が、「実行時の意味と一致」は言い過ぎで、**Start がクリップ長以上で End > Start を明示した組み合わせ（短いクリップに差し替えた後など）と負の値では、実行時は 1 サンプルのループに潰れるのに検査が黙る**（**GC-R-01、P2**。v1.0.0 からの既存の穴で、今回の変更で悪化はしていない = タグを止める理由ではない）。下の「BGM のループ位置の場合分け」。
- **GB-R-04 のコード例は**、Assembly-CSharp ならコピーしてそのままコンパイルが通り（using・名前空間・`IAssetManager` の 5 メンバーが E-9b の `ExternalGameTimeBehaviour` と同一で、こちらは契約テストのアセンブリでコンパイルされている）、`OnEnable` と `Start` の二重登録・無効化 → 再有効化・`DontDestroyOnLoad` で二重登録せず、`OnDisable` で必ず解除する。**「Tick の中で無効化 / `Unregister` しない」という案内とコード例の `OnDisable` は矛盾しない**（案内が禁じているのはまさにその経路。D-Drive 自身の Manager は Bootstrap の `Awake` で全部先に登録されるので、D-Drive の `Tick`〔VFX のプール返却・Presentation / Cutscene のマーカー購読者〕から持ち込み先の Manager が無効化されても添字は飛ばない）。E-9b は新しい形（`Start` での再試行・警告 1 行）を検証している。残るのは Bootstrap の作り直しの扱い 1 点（**GC-R-08、P3**）。
- **リリース道具に偽陽性（何も / 一部しか実行していないのに `ALL GREEN`）の現実的な筋書きは見つからなかった**。古い成果物は最初に消して消せなければ止まり、各段は今回の成果物の有無で判定し、テストは XML の `failed`・`total`・`result` で判定する。防御の甘さとして、`check-test-result.cmd` の想定外の終了コード（構文エラーの 255 等）を OK 扱いする点（**GC-R-02、P3**。現実のパスでは `run-ci.cmd` 自身が先に止まるので到達しない）。**偽陰性・初回で止まりそうな点は数点ある**（4 段目の `git diff` がページャで止まる / Unity の保存した差分で落ちる、ロックファイルの判定が推定に依存、Unity 終了コード 2 + `failed=0` + `inconclusive=0` を FAIL にする規則）。下の「`run-ci.cmd` の初回実行で止まりそうな点」。
- **docs/60 第 2 章の手順は `bump-version.ps1` の実際の引数・書き換え・同期対象・コミット・タグと一致**。docs/12 §7 と `bump-version.ps1` 冒頭のコメントは同期対象が 2 つだけの古い記述（**GC-R-06、P3**）。
- **CHANGELOG の「挙動の変更」の 3 点（尺 0 の揺れ / 振動は Warning、BGM の検査、SpecWeb の Placeholder 判定）は正しく書かれている**。

| 重大度 | 件数 | 内容 |
|---|---|---|
| P1（実バグ / 互換性破壊 / データ破損の恐れ = リリース前に必ず直す） | **0** | – |
| P2（直すべき不具合・設計上の穴） | **1** | GC-R-01 |
| P3（整理・改善） | **8** | GC-R-02〜09 |

---

## GB-R-01〜08 の解消確認

「解消」= 元の失敗の筋書きが起きなくなり、対応記録が実装と一致し、別の経路を壊していない。行番号は `f8524da`。

| 指摘 | 判定 | 根拠（ファイル:行） | 補足 |
|---|---|---|---|
| GB-R-01 尺 0 の揺れ / 振動 | **解消** | `Runtime/Camera/CameraShakeDataValidator.cs:46-56`、`Runtime/Haptics/HapticsDataValidator.cs:20-40`、`CameraFxManager.cs:227-231`、`HapticsManager.cs:170-174`、`Foundation/ValueDef/ValueDef.cs:50-56` | 判定は `ValueDef.Duration`（Duration=Value、Speed / Rate は `Value > 0 ? 1/Value : 0`）で `IsExpired` と同じ式。TimeMode=Rate / Speed で Value=0（`ValueDefValidator` は Duration 指定しか見ない）も Warning になる。ループ指定は `IsExpired` が見ないので検査も見ない（一致）。Haptics は `Max(Low, High)` で寿命を決めるので「両方 0 以下」のときだけ（一致）。二重報告の除外は `ValueDefValidator.CheckValueDef:131-136` の条件（Constant 以外 + Duration 指定 + Value <= 0）と同一。Haptics で片方だけがその形のときは Warning を出さないが、その場合はそのモーターの Error が出ているので検出漏れにはならない。既定値（Parametric 0.3 / 0.2 秒）では出ないので `validator-severity.txt` は不変（= 新しい Code の重さはゴールデンで固定されていない、GC-R-07）。新規コードは条件 1 つの分岐だけで重さの問題なし。UiTween の Motion（尺 0 = 即座に終値で完了）・BGM のフェード（尺 0 = フェードしない）は尺 0 に正当な意味があり、対象外の判断は妥当 |
| GB-R-02 BgmData の LoopEnd | **解消**（目的の 0 / 0・Start>0 / End=0 は Error にならない）**ただし既存の穴 = GC-R-01** | `Runtime/Audio/BgmDataValidator.cs:29-37`、`BgmManager.cs:295-332` | 下の「BGM のループ位置の場合分け」。クリップ長を読む副作用: Validator を実行するのは Editor（`DataValidationRunner` / `CI`）だけで、Runtime・Foundation から Validator を呼ぶ経路は無い（grep）。`AudioClip.length` はクリップのヘッダ情報で、音声データのデコードは起きない。`LoopBody` は直接参照なので BgmData を読み込んだ時点でクリップのオブジェクトも読み込み済み（Addressables 経由でも依存として一緒に載る）。`SeDataValidator.cs:69` が既に `Clips[0].length` を読んでいる前例と同じ。Missing（偽 null）は `!= null` で 0 扱いになり、`LoopBody` 未設定の Error が別に出る |
| GB-R-03 ValueDefColor | **見送り**（記録どおり） | `docs/17` §6 | 対象外 |
| GB-R-04 Tick 登録の案内 | **解消**（細部 = GC-R-08） | `docs/50_consumer_guide/operation.html:105-160`、`Tests/Runtime/ExternalContract/ExternalPackage/ExternalGameTimeBehaviour.cs`、`ExternalContractLoopTests.cs:181-205`、`Documentation~/AGENTS_CONSUMER.md:12`、`…/references/common-warnings.md:29` | 抜き取り 3 か所（運用ページ・AGENTS_CONSUMER・common-warnings）で、`Start` での再試行 + 警告・`Tick` 中に `Unregister` / 無効化しない（フラグ → 次フレーム / `LateUpdate`）・asmdef の 2 参照が一致。`GameLoop.Register` は `Contains` で二重登録しない（`GameLoop.cs:11-17`）。コード例の `TryRegister` は `_loop != null` で先に抜けるので `Register` を 2 回呼ぶことすら無い。無効化 → 再有効化は `OnDisable` で `_loop = null` → `OnEnable` で再登録。`Start` は一生に 1 回だけで、無効のまま生成されたオブジェクトでは有効化後の最初のフレーム。E-9b は「`OnEnable` 時点で Bootstrap 無し → `Start` で登録 → `Tick` が届く」「Bootstrap 無しのまま → 警告 1 行・未登録」を固定（`LogAssert.Expect` の位置も `Start` の前で正しい） |
| GB-R-05 CHANGELOG・Rate+Once | **解消**（(2) は記録どおり見送り） | `CHANGELOG.md:31-33` | 下の「CHANGELOG の 3 点」 |
| GB-R-06 全体用 Validator の判定 | **解消** | `Editor/Validation/DataValidationSection.cs:163-178,190-191,265` | 5 つ + `ProjectSetupValidator` はすべて `public sealed class`（`SpecDiffValidator.cs:28` 他）で、派生型・ジェネリックは存在しえない。`GetType()` の完全一致で漏れる型は無い。`IsProjectWide` / `IsProjectScopedInRunAll` / 個別検証の発見 / `CI.RunValidation(includeProjectWideValidators)` / `SpecWebSender` の 5 経路が同じ集合を使う。名前で判定している箇所は他に無い（grep） |
| GB-R-07 `ConstantTimeValidationTests` | **解消**（弱めすぎていない） | `Tests/Editor/ConstantTimeValidationTests.cs:20-61` | #115 の不具合（Constant の Time を検査する）が再発すると、新規作成直後の `CameraShakeData.Frequency = Constant01(20f)`（Time 既定 0、TimeMode 既定 = Duration）が「TimeMode=Duration ですが Value が 0 以下です」の Error になり、このテストは赤になる。判定は「`ValueDefValidator` の Error 0 件」なので、メッセージの部分一致だった以前より**強い**（Curve 未設定など他の ValueDef の Error でも赤）。失ったのは「`DataValidationRunner` が `ValueDefValidator` を実際に回しているか」の確認だけで、それは他のテスト（`ValueDefValidatorTests` の Fresh 系）でも見ている |
| GB-R-08 細部 | **解消**（(c) は記録どおり見送り） | `Editor/Validation/ForbiddenApiWindow.cs:25-63`、`Editor/Update/PackageDependencyChecker.cs:137-138` | `HasOpenInstances` を `GetWindow` の前に取るので、初回は `CreateGUI` の 1 回だけ。既に開いていて `_body` 未生成（`CreateGUI` 前）のときは `s_scanOnCreate = true` のまま `CreateGUI` が走査（1 回）。`CreateGUI` は走査後に `s_scanOnCreate = false` に戻す。`ParseVersion` の BOM はテスト 1 行で固定 |
| `run-netcheck.cmd` の ASCII 化 | **問題なし** | `Tools/CI/run-netcheck.cmd` | 差分はメッセージの英語化と `chcp` の削除だけ。引数（`%~1` → `-OnlyScenario "%SCENARIO%"`）・終了コード・`Run-NetCheck.ps1` の呼び出しは不変。`%ERRORLEVEL%` を読む `if` は 1 行ずつで、括弧ブロック内の凍結の問題は無い |

---

## BGM のループ位置の場合分け（GB-R-02 の確認）

実行時 = `BgmManager.StartLoopBody`（`BgmManager.cs:295-332`）。`hasCustomLoopPoints = LoopBody != null && (Start > 0 || End < LoopBody.length)`。カスタムの経路では `startSample = Clamp(Round(Start × f), 0, samples − 1)`、`endSample = End > Start ? Round(End × f) : samples` を `Clamp(endSample, startSample + 1, samples)`。区間の尺 = `(endSample − startSample) / f` ごとに `Tick` でもう一方のチャンネルに継ぐ（スプライス）。Intro は `Intro.samples / Intro.frequency` 後に `StartLoopBody` を呼ぶだけで、ループ位置の解釈には影響しない。検査 = `BgmDataValidator.cs:32-36`（実効の終端 = `End > 0 ? End : (LoopBody?.length ?? 0)`、`実効の終端 > 0 && 実効の終端 <= Start` で Error）。

| Start / End（L = クリップ長） | 実行時に起きること | 検査 | 一致 |
|---|---|---|---|
| 0 / 0（既定・取り込み直後） | `End(0) < L` でカスタムの経路 → 0〜末尾をスプライスで繰り返す（`loop = true` ではないが結果は全体ループ） | 何も出ない | ○ |
| Start 正（< L）/ 0 | Start〜末尾を繰り返す | 何も出ない | ○ |
| Start ≥ L / 0 | `startSample = samples − 1`、`endSample = samples` → **1 サンプルの区間を毎 Tick 継ぐ**（破綻） | Error（実効の終端 = L ≤ Start） | ○ |
| End > 0 かつ End ≤ Start（Start < L） | End を無視して Start〜末尾（指定と違う範囲） | Error | ○（意図との不一致として） |
| 0 ≤ Start < L、End > L | `endSample` を `samples` に丸め、Start〜末尾。Start = 0 なら `loop = true` の全体ループ | 何も出ない | ○ |
| **Start ≥ L かつ End > Start**（例: L = 10、12 / 20。長いクリップで決めたループ位置のまま短いクリップに差し替えた） | `startSample = samples − 1`、`endSample` は `samples` に丸め → **1 サンプルのループ**（破綻） | **何も出ない**（実効の終端 = 20 > 12） | **×（GC-R-01）** |
| **Start < 0 かつ End = 0**（例: −1 / 0。Inspector で直接入力） | `End(0) > Start(−1)` なので `endSample = Round(0) = 0` → `Clamp` で `startSample + 1 = 1` → **1 サンプルのループ** | **何も出ない**（実効の終端 = L > −1） | **×（GC-R-01）** |
| Start < 0 かつ End 正（例: −1 / 5） | `startSample = 0`、0〜5 秒 | 何も出ない | ○（実害なし） |
| Start がクリップ長のわずかに手前（`float` の `length` と `samples / f` の丸めの差、1 サンプル未満） | `Round(Start × f)` が `samples` になると 1 サンプルのループ | 何も出ない場合がある | △（境界。実害は理論上のみ） |
| `LoopBody` 未設定（End 0 / Start 任意） | クリップ null で `loop = true` のまま鳴らない | 「`LoopBody` 未設定」の Error のみ（ループ検査は実効の終端 0 で黙る） | ○ |
| `LoopBody` 未設定、End > 0 かつ End ≤ Start | 鳴らない | `LoopBody` 未設定の Error + ループの Error（2 件） | ○（重複は無害） |
| `LoopBody` が Missing | 偽 null で未設定と同じ | 同上 | ○ |
| クリップ長が取れない（未ロード） | Editor の Validator では `LoopBody` はアセット参照と一緒に読み込み済みで `length` はヘッダから取れる。実行時に Validator を呼ぶ経路は無い | – | 該当なし |

**実行時に破綻するのに検査が黙る組み合わせ: あり（2 通り）**。どちらも v1.0.0 から（以前の条件 `End <= Start` でも黙っていた）で、今回の変更による悪化ではない。区間が極端に短い（数十サンプル）場合に `Tick` 単位のスプライスが間に合わない問題も検査は見ないが、これは Validator の範囲外（既存）。

---

## P2 — 直すべき不具合・設計上の穴

### GC-R-01. 【GB-R-02】`BgmDataValidator` は「Start がクリップ長以上で End を明示」と「Start が負で End = 0」を見逃す（実行時は 1 サンプルのループに潰れる）

- **場所**: `Runtime/Audio/BgmDataValidator.cs:29-37`、`Runtime/Audio/BgmManager.cs:312-318`、`CHANGELOG.md:32`（「実行時の意味に合わせて絞る」）、docs/59 GB-R-02 の対応記録の表
- **何が問題か**: 新しい条件は「実効の終端 ≤ Start」だけを見るが、実行時は `startSample` をクリップの範囲に、`endSample` を `[startSample + 1, samples]` に丸めるので、(a) **Start ≥ クリップ長なら End に何を書いても 1 サンプルの区間**になり、(b) **Start < 0 かつ End = 0 は `End > Start` が真になって `endSample = 0` → 1 サンプルの区間**になる。どちらも検査は黙る。docs/59 の対応記録と CHANGELOG は「実行時の意味に合わせた」と書いているが、この 2 つは表に無い。
- **失敗の筋書き**: (a) 60 秒の仮 BGM で `LoopStartSec = 12 / LoopEndSec = 48` を Audio エディタで決めた後、`LoopBody` を 10 秒の本番素材に差し替える → Validation は Error 0 → 実機で BGM がブツッという 1 サンプルの繰り返し（ほぼ無音 + クリック）になる。(b) Inspector で `LoopStartSec` に負の値を打つ（Audio エディタの波形は 0 未満を作らないので、Inspector の直接入力のときだけ）。
- **直し方の案**: 実行時と同じ丸めをサンプル単位で再現して、**区間が潰れる・丸められる**ときに知らせる。既存の Error 条件（メッセージ・重さ）は変えず、新しい Code の **Warning**（[42] §5.8 の「新しい検査は Warning 始まり」。例 `DD-BGM-LOOP-OUT-OF-CLIP`）で、`LoopBody != null` のとき `startSample = Round(Start × f)` が `< 0` または `>= samples`（= Start がクリップの外）を出す。(b) は「`LoopStartSec` / `LoopEndSec` が負」の Warning でも足りる。Error を増やさないので MINOR の範囲でタグ前にも入れられるが、既存の穴なので v1.4.1 でもよい。
- **確度**: 確認済み（コード読み。実機の音は未確認）
- **→ 見送り**（2026-10-06）。v1.0.0 からの既存の穴で今回悪化していないため、**v1.4.1 で新しい Code の Warning（例 `DD-BGM-LOOP-OUT-OF-CLIP`）を足す**（まとめ役の決定）。起票は [60](60_release_1_4_0_prep.md) 第 6 章。コードは変えていない。

---

## P3 — 整理・改善

### GC-R-02. 【#118】`run-ci.cmd` の `:judge_tests` は、`check-test-result.cmd` の終了コードが 1 以外なら（255 等の想定外でも）OK にする

- **場所**: `Tools/CI/run-ci.cmd:258-279`
- **細部**: 判定は `J_XML_EXIT == 1` → FAIL、`== 10` → OK（Inconclusive あり）、**それ以外は全部 `[OK]`**。`check-test-result.cmd` が構文エラー（`exit 255`）で落ちても OK になる。実測（scratchpad、2026-10-06）: パスに `)` を含む XML を渡すと `check-test-result.cmd` は「… was unexpected at this time.」で 255 を返す。ただし同じパスなら `run-ci.cmd` 自身が 74 行目の `for` ブロック（`%RESULTS_DIR%` を括弧の中で展開）で先に構文エラーになり 1 段も実行しないので、**現実の偽陽性の筋書きにはならない**（防御の甘さ）。空白・日本語を含むパスは実測で正しく読めた。
- **直し方の案**: `if not "!J_XML_EXIT!"=="0" if not "!J_XML_EXIT!"=="10"` を FAIL にする（白リスト）。あわせて、括弧の中の `echo` で `%RESULTS_DIR%` 等を展開しない（ブロックの外で `set` してから `!VAR!` で出す）と、`)` を含むパスでも止まらなくなる。
- **確度**: 確認済み（コード読み + 単体実行）
- **→ 対応**（2026-10-06、PR `chore/release-tools-polish`）。`run-ci.cmd` の `:judge_tests` は `check-test-result.cmd` の戻り値を 0 / 10 の白リストで判定し、それ以外（255 等）は `[FAIL] … returned an unexpected exit code` にした。偽の `check-test-result.cmd`（`exit /b 255`）で FAIL・終了コード 1 を確認。括弧内の `%RESULTS_DIR%` の展開（`)` を含むパスで構文エラー）は直していない（このリポジトリのパスは該当しない。1 段も実行せず止まる側）。

### GC-R-03. 【#118】4 段目の `git diff --exit-code` はページャで止まりうる・未追跡のファイルを見ない

- **場所**: `Tools/CI/run-ci.cmd:154`
- **細部**: (1) 差分があると、対話コンソールでは git が `less` を起動し、`q` を押すまでスクリプトが止まる（差分がある = FAIL の場面なので結果は正しいが、無人で回すと戻ってこない）。全差分を画面に流すのも読みにくい。(2) `git diff` は未追跡のファイルを見ないので、ID 再生成が新しいファイル（新しい `.g.cs` / `.meta`）を作っても検出しない（CI の yml も同じ）。(3) docs/60 §2.1 の (c) のとおり、Unity のバッチ起動が保存しただけの差分（ProjectSettings・`packages-lock.json`・Addressables の設定）でも「ID 再生成で差分」と出る。
- **直し方の案**: `git --no-pager diff --exit-code --stat` に変え、`git status --porcelain` が空かも見る。メッセージを「ID 再生成の後に作業ツリーが clean でない」に寄せる。
- **確度**: (1)(2) 確認済み（git の既定の挙動）、(3) 推定（Unity が何を保存するか次第）
- **→ 対応**（2026-10-06、同 PR）。4 段目の `git diff` を `git --no-pager diff --exit-code --stat` に変更（ページャで止まらない・全文を流さない）。あわせて `git --no-pager status --porcelain` が空かも見て、未追跡ファイルが増えたとき（`[FAIL] … the working tree is not clean after regeneration, for example new untracked files:` + `git status --short`）も FAIL にした（対象は従来の差分確認と同じ = リポジトリ全体。`TestResults/`・`Builds/` は gitignore 済み）。git を呼ぶ他の箇所は `run-ci.cmd` に無い。偽のリポジトリで「4 段目の再生成が未追跡ファイルを作る」を FAIL・終了コード 1 で確認。(3) の Unity が保存しただけの差分は推定のままで、FAIL のメッセージに「無関係なファイルかもしれない。上の一覧を読む」を足した。

### GC-R-04. 【#118】Unity の終了コード 2 で `failed=0`・`inconclusive=0` のときを FAIL にする規則は、Ignore（Skipped）だけの実行で偽陰性になりうる

- **場所**: `Tools/CI/run-ci.cmd:270-274`、`Tools/CI/check-test-result.cmd:67-76`（`skipped` を読まない）
- **細部**: Unity Test Framework が終了コード 2 を返す条件が「失敗あり」だけでなく「結果が Passed でない」なら、`Assert.Ignore`（このリポジトリに多数。Addressables 設定が無い・開発リポジトリ専用）で Skipped だけが出た実行は 2 を返し、この規則で FAIL になる。開発リポジトリでは Addressables の設定があり DevRepoOnlyGuard は Inconclusive を使うので、**通常は起きない**。
- **直し方の案**: `check-test-result.cmd` で `skipped` も読み、`skipped > 0` なら 10 と同じ扱いにする。または、この規則自体（「2 なのに何も無い」を FAIL）を「警告して OK」に下げる。
- **確度**: 推定（Unity の終了コード 2 の条件）
- **→ 対応**（2026-10-06、同 PR）。`check-test-result.cmd` が `skipped` も読み（無ければ 0）、件数行に `skipped=` を出す。戻り値の意味: **0 = failed 0 で Inconclusive も Skipped も無い / 10 = failed 0 で Inconclusive または Skipped がある（OK）/ 1 = FAIL**（10 の意味を「Inconclusive または Skipped」に広げた。呼び出し側・コメント・docs/11 P-16・docs/60 を一致させた）。`run-ci.cmd` は「終了コード 2 かつ failed 0 かつ（Inconclusive > 0 または Skipped > 0）」を OK、「終了コード 2 かつ全部 0」を従来どおり FAIL。偽の Unity で skipped だけ（終了コード 2）= OK、終了コード 2 + 全部 0 = FAIL を確認。

### GC-R-05. 【#118】8 段目を飛ばしたときも、結果の要約は前回の `TestResults\NetCheck\results.json` を読む

- **場所**: `Tools/CI/run-ci.cmd:74`（削除の一覧に NetCheck が無い）・`:210-215`
- **細部**: 古い成果物の削除は 10 ファイルだけで、`TestResults\NetCheck\results.json` / `summary.md` は残る。exe が無くて 8 段目を飛ばしても、`Summarize-Results.ps1` には前回の NetCheck の結果が渡り、要約に古い PASS が出る。合否（`ALL GREEN`）は段ごとの判定で決まり要約の終了コードは読まないので、**表示が紛らわしいだけ**。
- **直し方の案**: 最初の削除に `NetCheck\results.json` を足す（または 8 段目を飛ばしたときは `-NetCheckResultsPath` を渡さない）。
- **確度**: 確認済み（コード読み）
- **→ 対応**（2026-10-06、同 PR）。実行開始時の古い成果物の削除に `TestResults\NetCheck\results.json` と `summary.md` を足した（小さい方）。あわせて `Summarize-Results.ps1` の NetCheck の行を「skipped（結果ファイルなし。NetCheck は任意ステップで、今回は実行していない）」に変えた。前回の結果ファイルを置いたうえで 8 段目をスキップする実行で、古い PASS が出ずに skipped と表示されることを確認。

### GC-R-06. 【docs】docs/12 §7 と `bump-version.ps1` 冒頭のコメントは、同期対象を 2 つしか書いていない

- **場所**: `docs/12_review.md:145`、`Tools/Release/bump-version.ps1:18-22`（コメント）。実装は `:237-263`
- **細部**: 実装は `DesignerManual`・`ProgrammerManual`・`migrations`・`50_consumer_guide → ConsumerGuide` の 4 つ + `CHANGELOG.md` を同期し、`-Tag` の `git add` も 8 パス。docs/60 §2.1 の手順 5 はこれと一致しているが、docs/12 §7 の手順 5 とスクリプト冒頭のコメントは `DesignerManual`・`ProgrammerManual`（と CHANGELOG）だけ。docs/12 §7 は check-release（手順 3）→ run-ci（手順 4）、docs/60 は run-ci（3）→ check-release（4）と順序も違う（どちらでも結果は同じ）。
- **直し方の案**: docs/12 §7 の手順 5 とコメントに 2 つを足し、§7 から docs/60 §2.1 を正本として参照する。
- **確度**: 確認済み
- **→ 対応**（2026-10-06、同 PR）。`docs/12_review.md` §7 の手順 5 と `bump-version.ps1` 冒頭のコメントを、実コード（`bump-version.ps1` の同期ブロック）どおり `DesignerManual`・`ProgrammerManual`・`migrations`（→ `Documentation~/migrations`）・`50_consumer_guide`（→ `Documentation~/ConsumerGuide`）の 4 つのミラー + `CHANGELOG.md`（→ パッケージの `CHANGELOG.md`）に直した。`bump-version.ps1` は**コメントだけ**で処理は変えていない。docs/12 §7 の先頭に「当日の手順の正本は docs/60 §2.1（順序は run-ci → check-release）」を足した。

### GC-R-07. 【GB-R-01】新しい Code 2 つの重さが `validator-severity.txt` で固定されていない

- **場所**: `Tests/Editor/Compat/ValidatorSeverityRegistryTests.cs:95-145`、`Tests/Editor/Compat/Snapshots/validator-severity.txt`（`DD-ADDR-CATALOG-MISSING=Error` の 1 行のみ）
- **細部**: 汎用の収集は「新規作成直後のインスタンス」に各 Validator を当てて Code の付いた結果を拾うが、既定値（0.3 / 0.2 秒）では尺 0 の Warning が出ないので、`DD-SHAKE-ENVELOPE-ZERO-DURATION` / `DD-HAPTICS-ZERO-DURATION` はゴールデンに載らない。後で誰かが Error に上げても互換テストは赤にならない（[42] §5.8 の「Error への昇格は CHANGELOG 必須」を機械で守れない）。既存の他の Code（`DD-MAT-*`・`DD-CANVAS-EMBED-*` 等）も同じ事情で、本ラウンド固有ではない。
- **直し方の案**: `AddressablesRegistrationValidator` と同じく、尺 0 の Data を組んで当てる個別の収集を足す（ゴールデンに 2 行追加 = 追加のみ）。他の Code もまとめて扱うなら v1.4.x で。
- **確度**: 確認済み
- **→ 見送り**（2026-10-06）。後で誰かが Error に上げても互換テストが赤にならない点は、他の Code も同じ事情（本ラウンド固有ではない）。コードは変えない。**v1.4.x の候補**として [60](60_release_1_4_0_prep.md) 第 6 章に起票（`AddressablesRegistrationValidator` と同じく尺 0 の Data を組んで当てる個別の収集を足し、ゴールデンに 2 行追加 = 追加のみ）。

### GC-R-08. 【GB-R-04】コード例は、Bootstrap が作り直されたとき（`DontDestroyOnLoad` の Manager が古い `GameLoop` を握ったまま）を扱わない

- **場所**: `docs/50_consumer_guide/operation.html:119-155`（`TryRegister` の `if (_loop != null) return true;`）、`ExternalGameTimeBehaviour.cs` も同形
- **細部**: docs/59 GB-R-04 (1) は「Bootstrap の作り直し」も挙げていたが、対応は起動順だけ。持ち込み先の Manager が `DontDestroyOnLoad` で、Bootstrap が `KeepAcrossScenes = false` で作り直される構成では、`_loop` は破棄された Bootstrap の `GameLoop` を指したまま `OnDisable` も走らないので、新しい Bootstrap の `Tick` は来ず、警告も出ない。MS2026 は `KeepAcrossScenes` を使う前提なら実害なし。
- **直し方の案**: 案内に 1 文（「Bootstrap をシーンごとに作り直す構成では、Manager もシーンに置く（`DontDestroyOnLoad` にしない）」）。コードで扱うなら `TryRegister` で `_loop != boot.Loop.GameLoop` のとき付け替える。
- **確度**: 確認済み（コード読み。`DDriveRuntimeBootstrap.OnDestroy` で `Instance = null`）
- **→ 対応**（2026-10-06、同 PR。案内の 1 文だけ。コードは変えない）。「Bootstrap をシーンごとに作り直す構成（`KeepAcrossScenes` を使わない）では、Manager も同じシーンに置く（`DontDestroyOnLoad` にしない）。残ると破棄された Bootstrap の `GameLoop` を握ったまま `Tick` が来ず、警告も出ない」を、運用ページ（`docs/50_consumer_guide/operation.html`）の Tick の案内、`Documentation~/AGENTS_CONSUMER.md`、`Documentation~/skills/ddrive-consumer/references/common-warnings.md` の 3 か所に足した。`build-manual.js` の再生成が要るページ（DesignerManual / ProgrammerManual）ではない。`Documentation~/ConsumerGuide/` へのミラーはリリース時の `bump-version.ps1` が行う。`TryRegister` で `_loop != boot.Loop.GameLoop` のとき付け替えるコード側の対応は、コード例を契約テスト（E-9b）と揃える必要があるため見送り。

### GC-R-09. 【リリース道具】CHANGELOG ガードは、リリース当日（`main == origin/main`）には何も検査しない

- **場所**: `Tools/Release/check-release.ps1:34-62`（`-Base` 既定 `origin/main`）、`Tools/Release/ReleaseChecks.ps1:232-251`、`run-ci.cmd:94`
- **細部**: ガードは `git diff --name-only origin/main..HEAD` でスナップショットの変更を探す。docs/60 §2.1 の手順 2（`git pull --ff-only`）の後は差分が空なので、1 段目も check-release のガードも常に「対象外で OK」になる。リリースの判定としては `[Unreleased]` の互換性節が空でない検査（check-release・bump-version）が実質の守り。今回は CHANGELOG を整理済みなので実害なし。
- **直し方の案**: リリース当日は `check-release.ps1 -Base v1.3.1`（前のタグ）で実行する、と docs/60 に 1 行。
- **確度**: 確認済み（コード読み）
- **→ 対応**（2026-10-06、同 PR。docs だけ）。`check-release.ps1` には比較の起点を指定する **`-Base`（既定 `origin/main`）が既にある**ので、スクリプトは変えず、当日の手順を「直近のリリースタグ（`v1.3.1`）を起点に `-GuardOnly -Base v1.3.1` で実行する」に直した（[12](12_review.md) §7・[60](60_release_1_4_0_prep.md) 2.1）。実行結果（版上げ前）: `-GuardOnly -Base v1.3.1` は green（スナップショットが変わり `CHANGELOG.md` も変わっている）、通常実行（`-RequireVersionBump` 付き）の `-Base v1.3.1` は**版が上がっていないので FAIL が想定どおり**（`package.json` の version が v1.3.1 時点から上がっていません）。版上げ（`bump-version.ps1 -Tag`）の後に同じ `-Base v1.3.1` で通常実行すると green になる想定（未確認。版は上げていない）。

---

## `run-ci.cmd` の初回実行で止まりそうな点

修正後の `run-ci.cmd` は本物の Unity で全段を通していない（まとめ役は 2026-10-06 に各段を直接実行して確認）。読んで分かる範囲で、初回に止まる / 紛らわしい結果になりそうな順に挙げる。**いずれも「止まる・FAIL になる」側で、偽の `ALL GREEN` になる筋書きではない**。

1. **ロックファイルの判定（推定）**: 「Unity が開いていると `Temp\UnityLockfile` を `type` で読めない」は Unity が共有読み取りを許さずに開いている前提で、根拠はスクリプトにも docs にも無い（実測の記録なし）。外れた場合は `[WARN] … leftover` で進み、2 段目以降の Unity が「別のインスタンスがこのプロジェクトを開いている」で終了コード 1 を返して全段 FAIL になる（安全側）。その場合 2 段目のメッセージは「pending migrations」と出るので、ログを読む。**Unity を開いたまま 1 回だけ実行して `[FAIL] Unity Editor is open` が出るかを最初に確かめると安い**（何も実行せずに終わる）。前回の異常終了の残りは `[WARN]` で続行、バッチの Unity 自身のロックファイルは各段が終われば外れる（次の段の判定は最初の 1 回だけなので関係しない）。
2. **4 段目の `git diff`**（GC-R-03）: 2・3 段目の Unity の起動で何か保存されると FAIL。差分があると `less` が開いて止まる（`q` で抜ける）。手順 2 の `git status` clean を守っても、バッチ起動による保存は防げない。出たら `git diff --stat` で中身を見る（docs/60 (c) のとおり）。
3. **2 段目のメッセージ**: 終了コードが 0 以外なら一律「there are pending migrations」。ライセンス・コンパイルエラー・別インスタンスでも同じ文言なので、`migrate-check.log` を読む。
4. **Unity の終了コード 2 の規則**（GC-R-04）: 開発リポジトリでは起きない見込みだが、EditMode で `Assert.Ignore` が出る状態（Addressables の設定が読めない等）だと「exit code 2 but … no failed or inconclusive」で FAIL。
5. **件数は検査しない**: `total > 0` と `failed = 0` しか見ないので、テストアセンブリの一部だけが走った場合（`testables` の誤りなど）も OK になる。初回は要約の件数を docs/60 の想定値（EditMode 成功 1599・保留 21、PlayMode 成功 941・保留 1、Performance 11）と目で突き合わせる。
6. **8 段目**: exe を最新のコードから作り直していること（古い exe は古いコードを検査する）、`pwsh` があること、UDP 7801〜7881 が空いていること。作り直した exe の初回起動で Windows のファイアウォールの確認が出ることがある（推定。127.0.0.1 だけなら拒否でも動く見込み）。飛ばすと要約に前回の NetCheck の結果が出る（GC-R-05）。
7. **起動のしかた**: Git Bash からは `./Tools/CI/run-ci.cmd`（`Tools\CI\…` は bash がバックスラッシュを消す）。docs/60 の「Git Bash からでも可」はこの書き方の前提。実行後、そのコンソールのコードページは 65001 のままになる（`chcp` は `setlocal` で戻らない。表示だけの問題）。
8. **パス**: 空白・日本語を含むパスは問題なし（`check-test-result.cmd` で実測）。`)` を含むプロジェクトパスでは 74 行目で構文エラーになり 1 段も実行しない（このリポジトリのパスは該当しない）。

### `run-ci.cmd` の初回実行で止まりそうな点への対応（2026-10-06、PR `chore/release-tools-polish`）

- **3（2 段目のメッセージ）→ 対応**: `CI.MigrateCheck` は `[DDrive][Migration] …` で始まる行を出す（`Editor/Validation/CI.cs`。未適用のときは Error、無いときは Log。本文は日本語）。ASCII の .cmd で使える目印はこの接頭辞だけなので、終了コードが 0 以外のときに `findstr` で `migrate-check.log` を調べ、**接頭辞の行があれば `exit code N, probably pending migrations`、無ければ `Unity failed, exit code N, and the log has no migration line`（ライセンス・コンパイルエラー・別インスタンスの可能性。ログを読む）**と区別して表示する。プロダクトコードは変えていない。偽の Unity で「接頭辞つきで exit 1」「接頭辞なしで exit 1」の 2 通りを確認。
- **5（件数）→ 対応**: 最後の要約（`Summarize-Results.ps1`）の各テスト段の行に `全 N 件 / Passed / Failed / Inconclusive / Skipped` を出す（期待値との比較はしない。[60](60_release_1_4_0_prep.md) の目安の件数と見比べる）。各段の直後にも `result=… total=… passed=… failed=… inconclusive=… skipped=…` が出る。
- **7（起動のしかた）→ 対応**: `run-ci.cmd` のヘッダのコメントと [60](60_release_1_4_0_prep.md)・[12](12_review.md) に、Git Bash からは `./Tools/CI/run-ci.cmd`、`cmd //c "Tools\\CI\\run-ci.cmd"` でも動くことを明記。PowerShell の `cmd /c`・`chcp 932`・`chcp 437`・Git Bash の 2 通りの計 5 通りの起動で、文字化けのエラーが出ず、Unity が開いている今は `[FAIL] Unity Editor is open` で終了コード 1 になることを確認した。
- **1（ロックファイル）・2（4 段目）・4（終了コード 2）・6（8 段目）・8（`)` を含むパス）**: 2・4 は上の GC-R-03・04 で対応。1・6・8 は推定・環境依存のため手順の注意のまま（変更なし）。

---

## CHANGELOG の 3 点（`[Unreleased]` の「挙動の変更」）

| 項目 | 記述 | 判定 |
|---|---|---|
| 尺 0 の揺れ / 振動は Warning | `CHANGELOG.md:31`（Constant の Time を検査しない項に追記。v1.3.1 では Error だった・新規 Code で Warning 始まり・Time を使うモードは従来どおり `ValueDefValidator` の Error のみ）+ 検査の追加の節 `:53` | 正しい |
| BGM の検査 | `:32`（(a) End 指定で Start 以下、(b) End = 0 で Start がクリップ末尾以上、のときだけ Error。メッセージ・重さ・コード不変）+ 修正の節 `:121` | 正しい（GC-R-01 の 2 通りは書かれていないが、今回の変更で挙動が変わったものではない） |
| SpecWeb の Placeholder 判定 | `:33`（Error の原因が Constant の Time か BGM の `LoopEndSec` だけだった Data は Placeholder でなくなり、`assetParams` に載り、SpecDiff の Warning も消える） | 正しい（`SpecWebSender` の判定〔Error を持つアセット = Placeholder〕と一致することは docs/59 GB-R-05 で確認済み） |

---

## 確認して問題なしだった観点

- 互換: スナップショットは `9f40cbb..f8524da` で `+131 / −0`（削除・変更 0）。新しい Code は `private const` で公開 API を増やさない。`HashSet<Type>` への変更は private で公開シグネチャ不変。
- GB-R-01: Warning の条件と `IsExpired` が一致（Rate / Speed / ループ / Haptics の Max）。二重報告の除外条件は `ValueDefValidator` と同一。
- GB-R-04: コード例のコンパイル（E-9b と同じ using・メンバー）、二重登録しない、`OnDisable` で必ず解除、案内との矛盾なし、E-9b が新しい形を検証。
- GB-R-06: 全体用の 6 型はすべて `sealed`。名前での判定は残っていない。
- GB-R-07: #115 が再発すれば赤になる（むしろ強くなった）。
- GB-R-08: 走査回数の分岐、`ParseVersion` の BOM。
- `run-netcheck.cmd`: 引数・終了コード・呼び出しは不変。
- `check-test-result.cmd`: 1 行の `test-run` 要素の属性を順序に依らず読み、`failed` が読めない・`total` が 0 / 空・`result` に `Failed` を含む（`Failed(Child)` も）ときは FAIL。子要素の `failed` は読まない（`findstr` で `<test-run ` の行だけを取る。Unity の XML は要素ごとに改行される）。空白・日本語を含むパスで正しく読めることを実測。
- `run-ci.cmd`: 括弧ブロック内の `ERRORLEVEL` はすべて `!ERRORLEVEL!` で直後に変数へ写している。`call :judge_tests` の中の `%~dp0` はバッチファイルのフォルダに展開される（実測）。`OVERALL_EXIT` は `setlocal` を跨がずに更新される。古い成果物は最初に消し、消せなければ何も実行せずに止まる。各段の成果物の有無で判定し、Unity が起動しなかった段は FAIL。
- docs/60 第 2 章 手順 5: `bump-version.ps1` の引数（`-Version` / `-Part` / `-DryRun` / `-Tag` / `-NoCommit`）、書き換える 3 ファイル、同期 4 + CHANGELOG、`-Tag` の明示パスの `git add` → `Release vX.Y.Z` のコミット → `git tag -a`（push なし）、日付は実行日、と一致。

---

## v1.4.0 のタグを打ってよいかの所見

**本 2 PR の範囲では、タグを止める理由は無い**（P1 0 件。P2 の GC-R-01 は v1.0.0 からの既存の穴で今回悪化していない）。タグの前提は docs/60 §2.1 のとおり:

1. **人による確認（別 PC で進行中）の完了**と、その NG の修正があればその差分の短いレビュー。
2. **Unity を閉じて `run-ci.cmd` を全段**（`ALL GREEN: ran 8 steps, skipped 0 steps`）。上の「初回実行で止まりそうな点」の 1（Unity を開いたまま 1 回だけ試してロックファイルの判定を確かめる）と 5（件数の目視）を勧める。
3. GC-R-01 を v1.4.0 に入れるかの判断（ユーザー）。入れるなら Warning の追加（MINOR の範囲）で、短いレビューを挟む。見送るなら v1.4.1。GC-R-02〜09 は v1.4.x でよい（GC-R-06 / 09 は docs だけなのでタグ前に直しても安い）。

---

## 見られなかった範囲

- Unity 上での実行（コンパイル・EditMode / PlayMode テスト・`CI.ValidateAll`・`run-ci.cmd` の全段・NetCheck・`ForbiddenApiWindow` の `CreateGUI` の時機）。対応記録の「green」は**未確認**。特に E-9b の追加部分（`Start` の時機・`LogAssert.Expect`）と `RunValidation_GlobalFindingsOfCodelessValidators_…` が開発リポジトリで green か。
- Unity が `Temp\UnityLockfile` をどの共有モードで開くか（推定のまま）、`-runTests` の終了コード 2 の正確な条件（推定のまま）、バッチ起動で Unity が保存するファイル。
- BGM の 1 サンプルのループの実際の音（GC-R-01 の筋書きはコード読みのみ）。
- `Run-NetCheck.ps1`・`Summarize-Results.ps1` の中身（引数と終了コードの入口だけ確認）。docs/60 の第 2 章以外・CHANGELOG の 3 点以外は通読していない。
- メインの checkout と、そこで開いている Unity には触れていない。
