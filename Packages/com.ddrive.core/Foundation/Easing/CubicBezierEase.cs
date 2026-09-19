using System;

namespace DDrive.Foundation.Easing
{
    // CSS cubic-bezier(x1,y1,x2,y2) 相当。P0=(0,0), P3=(1,1) 固定、P1=(x1,y1), P2=(x2,y2)。
    // 入力 t を X として Newton-Raphson で媒介変数を解き、対応する Y を返す。
    public static class CubicBezierEase
    {
        public static float Evaluate(float x1, float y1, float x2, float y2, float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            var guess = t;
            for (var i = 0; i < 8; i++)
            {
                var x = SampleCurve(guess, x1, x2) - t;
                if (MathF.Abs(x) < 1e-5f)
                {
                    break;
                }

                var derivative = SampleCurveDerivative(guess, x1, x2);
                if (MathF.Abs(derivative) < 1e-6f)
                {
                    break;
                }

                guess -= x / derivative;
            }

            return SampleCurve(guess, y1, y2);
        }

        private static float SampleCurve(float t, float p1, float p2)
        {
            var u = 1f - t;
            return 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t;
        }

        private static float SampleCurveDerivative(float t, float p1, float p2)
        {
            var u = 1f - t;
            return 3f * u * u * p1 + 6f * u * t * (p2 - p1) + 3f * t * t * (1f - p2);
        }
    }
}
