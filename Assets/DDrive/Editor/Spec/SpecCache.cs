using System;
using System.Collections.Generic;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.2 — 直近の取得・差分結果をメモリに保持する(取得のたびにネットへ行かないため)。
    // AssetBrowser の「仕様書に変更 n 件」バッジ(SpecAutoSync が更新)と、5-16(NewAssetDialog の
    // 「仕様書から選ぶ」)が同じキャッシュを読む想定。ドメインリロードで消える(再取得は自動/手動同期で行う)。
    public static class SpecCache
    {
        public static SpecParseResult<SpecAssetRow> LastAssetRows { get; private set; }
        public static SpecParseResult<SpecTuningRow> LastTuningRows { get; private set; }

        // W-9(2026-09-14) 追加: テーブル型調整値(tuningTableList)の最終取得結果。
        // スカラーとは独立に保持する(Set() の呼び出しタイミングが違うため。SpecAutoSync.Run 参照)。
        public static SpecParseResult<SpecTuningTableRow> LastTuningTableRows { get; private set; }

        public static SpecDiffResult LastDiff { get; private set; }
        public static DateTime LastFetchUtc { get; private set; }
        public static string LastWarning { get; private set; }
        public static string LastError { get; private set; }
        public static bool HasData => LastDiff != null;

        public static event Action Updated;

        public static void Set(
            SpecParseResult<SpecAssetRow> assetRows,
            SpecParseResult<SpecTuningRow> tuningRows,
            SpecDiffResult diff,
            string warning,
            string error)
        {
            LastAssetRows = assetRows;
            LastTuningRows = tuningRows;
            LastDiff = diff;
            LastWarning = warning;
            LastError = error;
            LastFetchUtc = DateTime.UtcNow;
            Updated?.Invoke();
        }

        // W-9(2026-09-14) 追加: テーブル型調整値の取得結果だけを更新する(Set() とは独立、上記コメント参照)。
        public static void SetTuningTables(SpecParseResult<SpecTuningTableRow> tuningTableRows)
        {
            LastTuningTableRows = tuningTableRows;
            Updated?.Invoke();
        }

        // 新規/変更として検出されている件数(AssetBrowser のバッジ用)。
        public static int PendingChangeCount => LastDiff == null ? 0 : LastDiff.New.Count + LastDiff.Changed.Count;

        // 5-16: NewAssetDialog の「仕様書から選ぶ」でアセットを作成した直後に呼ぶ。ネットへは行かず、
        // 既に取得済みの LastAssetRows を使って既存アセットとの照合だけ再計算する(= 作った行を
        // 「新規」一覧から消す)。LastFetchUtc は更新しない(仕様書自体を再取得したわけではないため、
        // キャッシュの新しさの表示(§8.6 の古さ判定)を変えない)。
        public static void RecomputeDiff()
        {
            if (LastAssetRows == null)
            {
                return;
            }

            LastDiff = SpecDiffService.ComputeDiff(LastAssetRows);
            Updated?.Invoke();
        }

        // 5-16 向け: まだ Data が無い(=新規)行だけを返す。filterType を指定すると種別固定で開いた
        // エディタからの絞り込みに使える。
        public static IReadOnlyList<SpecAssetRow> GetUncreatedRows(AssetType? filterType = null)
        {
            var list = new List<SpecAssetRow>();
            if (LastDiff == null)
            {
                return list;
            }

            foreach (var change in LastDiff.New)
            {
                if (filterType.HasValue && change.Row.Type != filterType.Value)
                {
                    continue;
                }

                list.Add(change.Row);
            }

            return list;
        }
    }
}
