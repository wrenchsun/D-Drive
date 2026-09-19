using System;
using UnityEngine;

namespace DDrive.Foundation.Values
{
    // 色は Parametric に相当する概念が薄いため Constant / Gradient の 2 モード + Alpha 専用 ValueDef。
    [Serializable]
    public struct ValueDefColor
    {
        public ValueMode Mode; // Constant または Curve のみ使う(Parametric は非対応)
        public Color Constant;
        public Gradient Curve;
        public ValueDef Alpha;

        public Color Evaluate(float t)
        {
            var baseColor = Mode == ValueMode.Curve && Curve != null ? Curve.Evaluate(t) : Constant;
            baseColor.a = Alpha.Evaluate(t);
            return baseColor;
        }

        // Curve(Gradient) 自体は TimeDef を持たないため、Alpha.TimeDef/Loop を全体の時間軸として共有する。
        public Color EvaluateAt(float elapsedSeconds)
        {
            var t = Alpha.ResolveNormalizedT(elapsedSeconds);
            return Evaluate(t);
        }
    }
}
