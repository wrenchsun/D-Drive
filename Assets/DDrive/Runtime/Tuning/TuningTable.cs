using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Runtime.Tuning
{
    // [27_spec_sheet.md] §3.2 / [11_tasks.md] 5-13 — 仕様書の「調整値」タブを取り込む先。
    // AssetDataBase ではない(UiLayerSettings と同じ理由。プロジェクト単位の設定であり、
    // AssetId で個別に引く対象ではない)。DDriveRuntimeBootstrap の Inspector 直参照 1 個だけを想定する。
    // シートからの取り込み(DDrive.Editor.Spec.SpecSyncService)が Entries を上書きする。
    public enum TuningValueType : byte
    {
        Float,
        Int,
        Bool,
        String,
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
    }

    [CreateAssetMenu(menuName = "D-Drive/Tuning/Tuning Table", fileName = "DDriveTuningTable")]
    public sealed class TuningTable : ScriptableObject
    {
        public TuningEntry[] Entries = Array.Empty<TuningEntry>();

        // Bind() 時に 1 回だけ構築するキー→添字の索引。定常経路(Tuning.Get*)で LINQ・boxing を
        // 発生させないため、辞書の値は int(添字)のみにする([12_review.md] §3)。
        [NonSerialized] private Dictionary<string, int> _index;

        public void RebuildIndex()
        {
            _index = new Dictionary<string, int>(Entries.Length, StringComparer.Ordinal);
            for (var i = 0; i < Entries.Length; i++)
            {
                var key = Entries[i].Key;
                if (!string.IsNullOrEmpty(key))
                {
                    _index[key] = i; // 重複キーは後勝ち(シート側の衝突検出は SpecSheetParser が別途行う)
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
    }
}
