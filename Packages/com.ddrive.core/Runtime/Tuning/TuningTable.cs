using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Runtime.Tuning
{
    // [27_spec_sheet.md] §3.2 → [32_spec_web.md] §5.3 / [11_tasks.md] 5-13・W-10 — 仕様書の調整値を
    // 取り込む先。AssetDataBase ではない(UiLayerSettings と同じ理由。プロジェクト単位の設定であり、
    // AssetId で個別に引く対象ではない)。DDriveRuntimeBootstrap の Inspector 直参照 1 個だけを想定する。
    // 仕様書からの取り込み(DDrive.Editor.Spec.SpecSyncService)が Entries/Tables を上書きする。
    //
    // W-10(2026-09-14): テーブル型・Enum 型に対応するため拡張した。[32_spec_web.md] §5.3・§9-7 で
    // 確認済みの「案A」(既存フィールドへの追加のみ。削除・型変更なし)のとおり実装している。
    // 既存の .asset(Entries のみを使う古いデータ)はそのまま読める(Tables は既定で空配列)。
    public enum TuningValueType : byte
    {
        Float,
        Int,
        Bool,
        String,

        // W-10 追加。値そのものは ValueString(スカラー)/TuningCellValue.S(テーブルのセル)に入れる。
        Enum,
    }

    [Serializable]
    public struct TuningEntry
    {
        [Tooltip("<機能>/<名前> の書式(例: Influence/FanBase)。Signal キーと同じ考え方([27] §3.2)。")]
        public string Key;
        public TuningValueType Type;
        public float ValueFloat;
        public int ValueInt;
        public bool ValueBool;
        public string ValueString;

        [Tooltip("float/int の範囲チェック用(Validation・エディタのスライダー範囲)。Min==Max なら無効。")]
        public float Min;
        public float Max;
        public string Unit;
        [TextArea]
        public string Description;

        // W-10 追加(案A)。Type=Enum のときの選択肢一覧。値自体は ValueString に入れる。
        public string[] EnumOptions;
    }

    // W-10 追加(案A、[32_spec_web.md] §5.3) — テーブル型調整値の列定義。
    [Serializable]
    public struct TuningTableColumn
    {
        public string Key;
        public TuningValueType Type;
        public float Min;
        public float Max;
        public string Unit;
        public string[] EnumOptions;
    }

    // W-10 追加 — テーブル 1 行の 1 セル。型ごとの値をすべて持つ(TuningEntry と同じ「型を跨いで
    // フィールドを持つ」設計。列の Type に応じて該当フィールドだけを読む)。
    [Serializable]
    public struct TuningCellValue
    {
        public string ColumnKey;
        public float F;
        public int I;
        public bool B;
        public string S;
    }

    // W-10 追加 — テーブル 1 行(rowId + セル配列)。
    [Serializable]
    public struct TuningTableRow
    {
        public string RowId;
        public TuningCellValue[] Cells;
    }

    // W-10 追加 — テーブル型調整値 1 件(キー + 列定義 + 行)。コメントは持たせない
    // ([32_spec_web.md] §5.3「Data は読み取り専用の運用を守るため、企画同士のやり取りである
    // 『コメント』はゲーム資産に混ぜない」)。
    [Serializable]
    public struct TuningTableEntry
    {
        public string Key;
        public TuningTableColumn[] Columns;
        public TuningTableRow[] Rows;
    }

    [CreateAssetMenu(menuName = "D-Drive/Tuning/Tuning Table", fileName = "DDriveTuningTable")]
    public sealed class TuningTable : ScriptableObject
    {
        public TuningEntry[] Entries = Array.Empty<TuningEntry>();

        // W-10 追加(案A)。既存の Entries はそのまま、テーブル型はこの新しい配列に追加した。
        public TuningTableEntry[] Tables = Array.Empty<TuningTableEntry>();

        // Bind() 時に 1 回だけ構築するキー→添字の索引。定常経路(Tuning.Get*)で LINQ・boxing を
        // 発生させないため、辞書の値は int(添字)のみにする([12_review.md] §3)。
        [NonSerialized] private Dictionary<string, int> _index;
        [NonSerialized] private Dictionary<string, int> _tableIndex;

        public void RebuildIndex()
        {
            _index = new Dictionary<string, int>(Entries.Length, StringComparer.Ordinal);
            for (var i = 0; i < Entries.Length; i++)
            {
                var key = Entries[i].Key;
                if (!string.IsNullOrEmpty(key))
                {
                    _index[key] = i; // 重複キーは後勝ち(仕様書側の衝突検出は SpecWebParser が別途行う)
                }
            }

            _tableIndex = new Dictionary<string, int>(Tables.Length, StringComparer.Ordinal);
            for (var i = 0; i < Tables.Length; i++)
            {
                var key = Tables[i].Key;
                if (!string.IsNullOrEmpty(key))
                {
                    _tableIndex[key] = i;
                }
            }
        }

        public bool TryFindIndex(string key, out int index)
        {
            if (_index == null)
            {
                RebuildIndex();
            }

            return _index.TryGetValue(key, out index);
        }

        public bool TryFindTableIndex(string key, out int index)
        {
            if (_tableIndex == null)
            {
                RebuildIndex();
            }

            return _tableIndex.TryGetValue(key, out index);
        }
    }
}
