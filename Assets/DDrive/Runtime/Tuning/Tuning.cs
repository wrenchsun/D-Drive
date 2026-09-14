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

        // P5 レビュー対応(2026-09-14): GetBool/GetString が型不一致(entry.Type が要求と違う)を検出しない
        // 問題への対応。「未登録キー」の警告(WarnedKeys)とは別に 1 キー 1 回だけ警告する。
        private static readonly HashSet<string> WarnedTypeMismatchKeys = new();

        public static void Bind(TuningTable table)
        {
            _table = table;
            _table?.RebuildIndex();
            WarnedKeys.Clear();
            WarnedTypeMismatchKeys.Clear();
        }

        public static bool IsBound => _table != null;

        public static float GetFloat(string key, float defaultValue = 0f)
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.Float && entry.Type != TuningValueType.Int)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetFloat));
                }

                return entry.Type == TuningValueType.Float ? entry.ValueFloat : entry.ValueInt;
            }

            return defaultValue;
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.Float && entry.Type != TuningValueType.Int)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetInt));
                }

                return entry.Type == TuningValueType.Int ? entry.ValueInt : (int)entry.ValueFloat;
            }

            return defaultValue;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.Bool)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetBool));
                }

                return entry.ValueBool;
            }

            return defaultValue;
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.String)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetString));
                }

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
                // P5 レビュー対応(2026-09-14): 整理項目 — 未登録キー警告(実行時に毎フレーム呼ばれうる
                // 定常経路)を開発ビルド/エディタ限定にする(製品ビルドでログ汚染・コスト増を避ける)。
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Tuning: キー '{key}' が TuningTable に見つかりません。既定値を使います。");
#endif
            }

            return false;
        }

        private static void WarnTypeMismatchOnce(string key, TuningValueType actualType, string calledFrom)
        {
            if (!WarnedTypeMismatchKeys.Add(key))
            {
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"[DDrive] Tuning: キー '{key}' は {actualType} 型ですが {calledFrom} で読まれました。想定と異なる値が返る可能性があります。");
#endif
        }
    }
}
