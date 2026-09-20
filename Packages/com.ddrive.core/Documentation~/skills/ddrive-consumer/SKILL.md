---
name: ddrive-consumer
description: D-Drive パッケージ（com.ddrive.core）を導入したプロジェクトでゲームコード・データ・設定を触るときに読む。D-Drive / SEID / VFXID / Presentation / Cutscene / AssetBrowser / Validation / 更新（アップデート）に関する作業のいずれかを行うときに使う。
---

# D-Drive 消費側スキル（ddrive-consumer）

このスキルは、D-Drive（`com.ddrive.core`）を UPM パッケージとして導入した**このプロジェクト**で D-Drive を使う手順です。D-Drive 自身を開発する手順（新しい種別の追加・SpecWeb の検証・Unity MCP のトークン運用等）は含みません。それらは D-Drive の開発リポジトリ側の作業であり、このプロジェクトでは行いません。

**このプロジェクト自身の `CLAUDE.md`（フォルダ規約・命名規約・コーディング規約）と本書が競合する場合は、このプロジェクトの `CLAUDE.md` を優先してください。**

禁止事項（Data のテキスト編集禁止・禁止 API・Data 読み取り専用・パッケージ改造禁止・asmdef を触らない 等）は `Documentation~/AGENTS_CONSUMER.md` にまとめてあります。ここでは重複させず、作業の進め方だけを説明します。

## 1. ID 経由の利用パターン

D-Drive の基本形は「ID を渡して静的ファサードを呼ぶ」だけです。ID が指すデータがまだ無くても例外にはならず、警告 + Placeholder で継続します。

```csharp
using DDrive.Generated;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using DDrive.Runtime.Presentation;

// SE を鳴らす
Audio.PlaySe(SEID.X);

// VFX を出す（位置・向きは呼び出し側の Transform 等から渡す）
Vfx.Spawn(VFXID.X, position, rotation);

// 演出（SE/VFX/揺れ/HitStop の束）を再生する
var handle = Presentation.Play(PRESID.X, ctx);

// Maya の FBX を取り込んだカットシーンを再生する
var cutsceneHandle = Cutscene.Play(CUTID.X, ctx);
```

- ID 定数（`SEID.X` 等）は `Assets/Generated/`（または `DDrive.Generated.asmdef`）にある**生成物**です。手で書かない。古ければ `Tools > D-Drive > Generate > Regenerate Asset IDs` で作り直す
- `Handle` は未使用でも構いません。途中で止めたい場合だけ受け取って `Cancel`/`Stop` に渡します

## 2. アセットの作り方

1. `Tools > D-Drive > AssetBrowser` を開き、対象の種別（Se/Vfx/Presentation 等）で新規作成する（`[CreateAssetMenu]` を手動で使わない。AssetBrowser がファイル名・配置フォルダ・カタログ登録・Addressables 登録をまとめて行う）
2. 専用エディタ（`[DataEditor]` 属性が付いたウィンドウ。Inspector 最上部の「エディターで開く」からも開ける）で中身（音源・エフェクト・タイムライン等）を割り当てる
3. `Validation > Run All` を実行し、Error が無いことを確認する

Data の配置フォルダ・ファイル名はツールが決めるものです。人が手でファイルを移動・リネームしない（ID は GUID 由来で不変ですが、命名規約が崩れます）。

## 3. Validation の読み方

- `Tools > D-Drive > Validation > Run All` が Data・カタログ・Addressables 登録・ProjectSettings 等をまとめて検査します。CI からは `-executeMethod DDrive.Editor.CI.ValidateAll` で同じ検査を実行できます
- **Error** は実行時に Placeholder になる・コンパイルが壊れる等、実害のある問題です。CI ではこれを fail 条件にする
- **Warning** は仕様変更の周知や、まだ壊れていないが直したほうがよい状態です。D-Drive の更新直後に新しい Warning が増えることがあります（Error は更新直後には増えない設計）
- よくある警告と対処は [references/common-warnings.md](references/common-warnings.md) を参照

## 4. 更新手順

1. `Packages/manifest.json` の `#vX.Y.Z` タグを新しい版に書き換える
2. Unity を開き直し、コンパイルエラーが無いことを確認する（エラーがあれば「破壊あり」の変更を踏んでいるので `docs/migrations/` の移行ガイドを確認する）
3. `Tools > D-Drive > Update > 更新ウィンドウ` を開く
   1. 「1. 版と CHANGELOG」で前回適用した版との差分・「破壊あり」の有無を確認する
   2. 「2. マイグレーション（プレビュー）」で対象件数を確認する（この時点では何も変更しない）
   3. 「3. 更新を適用」を押す。内部で (a) データマイグレーション → (b) ID/調整値の再生成 → (c) Addressables 登録の同期 → (d) `Validation > Run All` → (e) 「前回適用した版」の更新、の順に実行され、**途中の段が失敗したらそこで止まる**
4. `Validation > Run All` で Error が無いことを確認する
5. manifest / lock / 更新で変わった `.asset` / 生成コードをコミットする

チェックリスト形式は [references/update-checklist.md](references/update-checklist.md) を参照。

## 5. Presentation / Cutscene の入口（どちらを使うか）

上から順に当てはまったところで決める（Maya の FBX を使うかどうかが最大の判断軸）。

| 質問 | Yes なら |
|---|---|
| Maya で作ったカメラやキャラの動き（FBX）を使う? | **Cutscene** |
| 演出中、カメラをゲームから奪って別のカメラワークにする? | **Cutscene** |
| ヒット判定など「ゲームの結果を待ってから」続きを出す（`OnSignal`）? | **Presentation** |
| 既存の SE / VFX / 揺れ / HitStop を時間で並べるだけ（キャラの動きは Animator のモーション）? | **Presentation** |
| Maya カメラも使うし、ヒットも待ちたい | **Presentation を親**にして、Timeline トラック（`TrackKind.Timeline`）で Cutscene を呼ぶ |

一言で言うと「**Maya の FBX を使うなら Cutscene、使わないなら Presentation**」。それ以外の要素（SE/VFX/Shake/Haptic/UI/Event/Signal）はどちらにも同じ形で置けます。両者は相互に入れ子にできますが、循環（Presentation → Cutscene → 同じ Presentation）は Validation Error になります。

## 6. Unity の操作について

Unity Editor の操作（コンパイル確認・テスト実行等）は、このプロジェクト自身の MCP 構成に従ってください。D-Drive 独自の MCP セットアップはありません。
