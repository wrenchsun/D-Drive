using System;

namespace DDrive.Foundation.Easing
{
    // easings.net 準拠の参照実装。0 alloc・純関数(同じ (ease,t) は常に同じ結果)。
    public static class EasingCore
    {
        private const float C1 = 1.70158f;
        private const float C2 = C1 * 1.525f;
        private const float C3 = C1 + 1f;
        private const float N1 = 7.5625f;
        private const float D1 = 2.75f;
        private static readonly float C4 = (2f * MathF.PI) / 3f;
        private static readonly float C5 = (2f * MathF.PI) / 4.5f;

        public static float Evaluate(Ease ease, float t) => ease switch
        {
            Ease.Linear => t,

            Ease.InSine => 1f - MathF.Cos((t * MathF.PI) / 2f),
            Ease.OutSine => MathF.Sin((t * MathF.PI) / 2f),
            Ease.InOutSine => -(MathF.Cos(MathF.PI * t) - 1f) / 2f,

            Ease.InQuad => t * t,
            Ease.OutQuad => 1f - (1f - t) * (1f - t),
            Ease.InOutQuad => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2) / 2f,

            Ease.InCubic => t * t * t,
            Ease.OutCubic => 1f - MathF.Pow(1f - t, 3),
            Ease.InOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3) / 2f,

            Ease.InQuart => t * t * t * t,
            Ease.OutQuart => 1f - MathF.Pow(1f - t, 4),
            Ease.InOutQuart => t < 0.5f ? 8f * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 4) / 2f,

            Ease.InQuint => t * t * t * t * t,
            Ease.OutQuint => 1f - MathF.Pow(1f - t, 5),
            Ease.InOutQuint => t < 0.5f ? 16f * t * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 5) / 2f,

            Ease.InExpo => t <= 0f ? 0f : MathF.Pow(2f, 10f * t - 10f),
            Ease.OutExpo => t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t),
            Ease.InOutExpo => InOutExpo(t),

            Ease.InCirc => 1f - MathF.Sqrt(1f - MathF.Pow(t, 2)),
            Ease.OutCirc => MathF.Sqrt(1f - MathF.Pow(t - 1f, 2)),
            Ease.InOutCirc => t < 0.5f
                ? (1f - MathF.Sqrt(1f - MathF.Pow(2f * t, 2))) / 2f
                : (MathF.Sqrt(1f - MathF.Pow(-2f * t + 2f, 2)) + 1f) / 2f,

            Ease.InBack => C3 * t * t * t - C1 * t * t,
            Ease.OutBack => 1f + C3 * MathF.Pow(t - 1f, 3) + C1 * MathF.Pow(t - 1f, 2),
            Ease.InOutBack => InOutBack(t),

            Ease.InElastic => InElastic(t),
            Ease.OutElastic => OutElastic(t),
            Ease.InOutElastic => InOutElastic(t),

            Ease.InBounce => 1f - OutBounce(1f - t),
            Ease.OutBounce => OutBounce(t),
            Ease.InOutBounce => t < 0.5f
                ? (1f - OutBounce(1f - 2f * t)) / 2f
                : (1f + OutBounce(2f * t - 1f)) / 2f,

            _ => t,
        };

        private static float InOutExpo(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t < 0.5f
                ? MathF.Pow(2f, 20f * t - 10f) / 2f
                : (2f - MathF.Pow(2f, -20f * t + 10f)) / 2f;
        }

        private static float InOutBack(float t)
        {
            return t < 0.5f
                ? (MathF.Pow(2f * t, 2) * ((C2 + 1f) * 2f * t - C2)) / 2f
                : (MathF.Pow(2f * t - 2f, 2) * ((C2 + 1f) * (t * 2f - 2f) + C2) + 2f) / 2f;
        }

        private static float InElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((t * 10f - 10.75f) * C4);
        }

        private static float OutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * C4) + 1f;
        }

        private static float InOutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t < 0.5f
                ? -(MathF.Pow(2f, 20f * t - 10f) * MathF.Sin((20f * t - 11.125f) * C5)) / 2f
                : (MathF.Pow(2f, -20f * t + 10f) * MathF.Sin((20f * t - 11.125f) * C5)) / 2f + 1f;
        }

        private static float OutBounce(float t)
        {
            if (t < 1f / D1)
            {
                return N1 * t * t;
            }

            if (t < 2f / D1)
            {
                t -= 1.5f / D1;
                return N1 * t * t + 0.75f;
            }

            if (t < 2.5f / D1)
            {
                t -= 2.25f / D1;
                return N1 * t * t + 0.9375f;
            }

            t -= 2.625f / D1;
            return N1 * t * t + 0.984375f;
        }
    }
}
