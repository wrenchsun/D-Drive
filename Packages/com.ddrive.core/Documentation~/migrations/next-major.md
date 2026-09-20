# 次の MAJOR で削除する候補（[Obsolete] 棚卸し）

> `Tools/Release/list-obsolete.ps1` が自動生成する。手で編集しない(リリース準備のときに再実行して上書きする)。
> 関連: [../42_distribution.md](../42_distribution.md) §5.4・§5.12・§7 A-5（`[Obsolete]` の猶予は付与から少なくとも 2 MINOR、MAJOR は年 1 回まで）・[../12_review.md](../12_review.md) §7（リリース手順）。

生成日時: 2026-09-20 10:07

`Packages/com.ddrive.core/{Foundation,Runtime,Editor}` の `[Obsolete(...)]` 属性を正規表現で静的に走査した一覧。
行コメント(`//`)中の言及は対象外。ブロックコメント(`/* ... */`)の中にある場合は誤検出し得る(簡易スキャンのため。
実際に削除してよいかは必ず人が最終確認すること)。

現在、`[Obsolete]` 属性が付与された公開 API はありません。

## 運用ルール

- 新しく `[Obsolete]` を付けるときは、メッセージに `since x.y.z`(付与したパッケージ版)を含める([../12_review.md](../12_review.md) §3・§7)。
- 削除できるのは「付与から少なくとも 2 回の MINOR リリースを経た」後の次の MAJOR([../42_distribution.md](../42_distribution.md) §5.12・§7 A-5)。
- MAJOR リリースの前に `Tools/Release/list-obsolete.ps1` を再実行し、削除候補を確認してから [../42_distribution.md](../42_distribution.md) §5.12 の手続きに進む。
