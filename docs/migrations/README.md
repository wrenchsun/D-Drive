# 移行ガイド（migrations）

関連: [../42_distribution.md](../42_distribution.md) §4（更新の取り込みフロー）・§5.12（破壊的変更をどうしても行う場合の手続き）/ [../../CHANGELOG.md](../../CHANGELOG.md)

## これは何か

D-Drive（`com.ddrive.core`）が **MAJOR バージョン**を上げるとき（互換面を破壊するとき）、対象バージョンごとに 1 本の移行ガイドをこのフォルダに置く。

- ファイル名: `vN.md`（例: `v2.md` = 2.0.0 への移行ガイド）
- 雛形: [TEMPLATE.md](TEMPLATE.md) をコピーして書く
- **CHANGELOG.md の「破壊あり」に必ずこのファイルへのリンクを張る**（[../42_distribution.md](../42_distribution.md) §4.1）

## いつ作るか

[../42_distribution.md](../42_distribution.md) §5.12 の手続きに従う:

1. issue/設計メモに「何を・なぜ・代替案（2 段階で回避できないか）」を書き、ユーザー承認を取る
2. 少なくとも 2 回の MINOR で `[Obsolete]`・Warning・移行ツール（`IDataMigration`/`IProjectMigration`）を**先に出す**
3. MAJOR で削除する回に、このフォルダへ `vN.md`（[TEMPLATE.md](TEMPLATE.md) 準拠）を追加する
4. 持ち込み先（最初は MS2026）で実際に §4.2 の更新手順を実施し、詰まった点をこのガイドに追記する

## 発効前の注意（2026-09-20 時点）

互換性ポリシー（[../42_distribution.md](../42_distribution.md) §5）は **P チケット完了（P-13）まで発効していない**。発効前に行った「最後のチャンス」の整理（§5.13。例: `KnownPrefixes` の追加）は破壊的変更の手続きを踏まず、[CHANGELOG.md](../../CHANGELOG.md) の該当バージョン節にその旨を記録するだけでよい。移行ガイド（`vN.md`）を書くのは **P-13 発効後の MAJOR** からになる。

## `next-major.md`（P-9 で追加予定）

P-9（リリース手順の道具化）で、`[Obsolete]` 付与済みのまま次の MAJOR で削除される予定の API を自動列挙する `next-major.md` をこのフォルダに追加する（[../42_distribution.md](../42_distribution.md) §6 P-9）。本 README は現時点（P-2）ではその雛形を含まない。

## 変更履歴

- 2026-09-20: 新規作成（P-2）。ドキュメントのみ、実際の移行ガイド（`vN.md`）はまだ無い。
