using System;

namespace DDrive.Foundation.Values
{
    // 「形」と分離された「速さ」の統一表現([17_value_definition.md] §1)。
    [Serializable]
    public struct TimeDef
    {
        public TimeMode Mode;
        public float Value;
        public float SpeedScale;
        public bool IgnoreTimeScale;

        public static TimeDef Duration(float seconds) => new() { Mode = TimeMode.Duration, Value = seconds, SpeedScale = 1f };
        public static TimeDef Rate(float unitsPerSecond) => new() { Mode = TimeMode.Rate, Value = unitsPerSecond, SpeedScale = 1f };
    }
}
