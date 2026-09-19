# 移行ガイド雛形（vN.md を書くときにコピーする）

> このファイル自体は雛形であり、実在するバージョンの移行手順ではない。`docs/migrations/vN.md` を作るときにこの内容をコピーし、山括弧 `<...>` の部分を埋める。書式・分量は既存の CHANGELOG・docs の「変更履歴は該当節に日付付きで追記する」慣習に合わせる。

---

# vN.0.0 への移行ガイド

- **対象バージョン**: `<現在の版>` → `vN.0.0`
- **リリース日**: `<YYYY-MM-DD>`
- **関連**: [../42_distribution.md](../42_distribution.md) §5.12 / [../../CHANGELOG.md](../../CHANGELOG.md) の `vN.0.0` 節

## 1. 何が壊れるか（破壊内容）

<互換面（[../42_distribution.md](../42_distribution.md) §5.1〜§5.10 のどれか）を 1 つずつ列挙。「何を」「なぜ」「[../42_distribution.md] のどの禁止規則に該当するか」を書く>

- 例: `Handle<T>.Cancel(CancellationToken)` を削除した（§5.4 公開 API）。理由: `UniTask` のキャンセル伝播規約と重複しており、`Handle<T>.Dispose()` に統合した

## 2. 症状（更新すると何が起きるか）

<持ち込み先で実際に何が起こるか。コンパイルエラーのメッセージ例、Validation の新しい Error、既存 `.asset` の値が既定値に戻る等を具体的に書く>

```
例: CS1061: 'Handle<SeMarker>' に 'Cancel' の定義がありません
```

## 3. 手順（どう直すか）

1. <ステップ 1>
2. <ステップ 2>
3. <[../42_distribution.md] §4.2 の標準更新手順（読む→退避→版を進める→Unity を開く→更新ツール→検査→実機→コミット）のうち、このバージョン固有の追加作業があれば書く>

### 該当するコード例（Before / After）

```csharp
// Before (vN-1 以前)
var handle = Audio.Play(SEID.Jump);
handle.Cancel(token);

// After (vN 以降)
var handle = Audio.Play(SEID.Jump, token);
handle.Dispose();
```

## 4. ロールバック可否

<[../42_distribution.md] §4.4 のとおり、同一 MAJOR 内のロールバックは旧フィールド保持により基本的に可能。MAJOR をまたぐロールバックは「更新前のコミットに戻す」以外に保証しないことを明記する>

- 可否: `<可能 / 更新前のコミットへ戻す以外に保証しない>`
- 理由: `<...>`

## 5. 実施記録（持ち込み先ごとに追記する）

| 持ち込み先 | 実施日 | 実施者 | 所要時間 | 詰まった点 |
|---|---|---|---|---|
| MS2026 | `<YYYY-MM-DD>` | `<氏名>` | `<分>` | `<あれば>` |
