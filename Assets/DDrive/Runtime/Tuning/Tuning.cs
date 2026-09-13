using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Runtime.Tuning
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Audio.cs / Options.cs と同じ設計、ADR#3)。
    // 生成された TUNING.キー定数(Assets/Generated/Tuning.g.cs、DDrive.Editor.Codegen.TuningCodegen)と
    // 組み合わせて Tuning.GetFloat(TUNING.InfluenceFanBase) のように読む([27_spec_sheet.md] §3.2)。
    // 例外で止めない: 未登録キーは警告を 1 回だけ出し、既定値(呼び出し側が渡した defaultValue)を返す。
    public static class Tuning
    {
        private static TuningTable _table;
        private static readonly HashSet<string> WarnedKeys = new();

        public static void Bind(TuningTable table)
        {
            _table = table;
            _table?.RebuildIndex();
            WarnedKeys.Clear();
        }

        public static bool IsBound => _table != null;

        public static float GetFloat(string key, float defaultValue = 0f)
        {
            if (TryFindEntry(key, out var entry))
            {
                return entry.Type == TuningValueType.Float ? entry.ValueFloat : entry.ValueInt;
            }

            return defaultValue;
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            if (TryFindEntry(key, out var entry))
            {
                return entry.Type == TuningValueType.Int ? entry.ValueInt : (int)entry.ValueFloat;
            }

            return defaultValue;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            if (TryFindEntry(key, out var entry))
            {
                return entry.ValueBool;
            }

            return defaultValue;
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (TryFindEntry(key, out var entry))
            {
                return entry.ValueString;
            }

            return defaultValue;
        }

        private static bool TryFindEntry(string key, out TuningEntry entry)
        {
            if (_table != null && _table.TryFindIndex(key, out var index))
            {
                entry = _table.Entries[index];
                return true;
            }

            entry = default;
            if (WarnedKeys.Add(key))
            {
                Debug.LogWarning($"[DDrive] Tuning: キー '{key}' が TuningTable に見つかりません。既定値を使います。");
            }

            return false;
        }
    }
}
