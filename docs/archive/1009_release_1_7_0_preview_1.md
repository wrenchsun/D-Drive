# 1009 試験版 v1.7.0-preview.1 リリース記録（2026-10-10）

専用エディターのサンプル A〜D（[1008](../1008_editor_ux_redesign.md)）を持ち込み先で試すための試験版。**確認後にタグとサンプルを削除する**（後始末 = [1001](../1001_open_items.md)）。互換性スナップショットは変更なし（サンプルは internal のみ）。

## 手順と結果

| 手順 | 結果 |
|---|---|
| サンプルを `Packages/com.ddrive.core/Editor/Prototypes/` へ移動（asmdef `DDrive.Editor.Prototypes`、全型 internal）、`vfx_sample.prefab` 同梱（AnchorRig のネストは Unpack して自己完結）、空状態の「サンプル VfxData を作る」ボタン | PR #189 マージ |
| リリース道具のプレリリース対応（`ReleaseChecks.ps1` の SemVer、`PackageVersionConsistencyTests`） | 追加テスト含め green |
| `ddrive_compat` | `changed: []` |
| テスト | EditMode 2078/2078、PlayMode 964/964 |
| `run-ci.cmd` | ALL GREEN、skipped 0（EditMode 2078 / PlayMode 964 / Performance 11 / NetCheck 15 シナリオ） |
| `bump-version.ps1 -Version 1.7.0-preview.1 -Tag` → `check-release.ps1 -Base v1.6.0` | green |
| push | `main` と `v1.7.0-preview.1`（GitHub Release は作らない） |

## MS2026 へ渡す文面

> D-Drive の試験版 `v1.7.0-preview.1` を出しました。専用エディターの UI 案（サンプル A〜D、VfxData のみ）を試して、方向を決めてください。
> 1. `Tools > D-Drive > Update > 更新ウィンドウ` の「更新先の版」一覧でプレリリース `v1.7.0-preview.1` を**明示的に選び**（既定の候補には出ません）、更新する（manifest が `#v1.7.0-preview.1` になります）。
> 2. `Tools > D-Drive > Prototypes` の 4 本（A 段階表示 / B ステップ型 / C 目的別カード / D デザイン重視）を同じ VfxData で開いて比べる。VfxData が無ければ各ウィンドウの空状態の「サンプル VfxData を作る（vfx_sample）」で作れる。
> 3. 比べる観点は [1008](../1008_editor_ux_redesign.md) §4。
> 確認後にこの試験版とサンプルは削除するので、**manifest は後で正式版に戻します**。

## 後始末

[1001](../1001_open_items.md) の「試験版 v1.7.0-preview.1 の後始末」を参照。
