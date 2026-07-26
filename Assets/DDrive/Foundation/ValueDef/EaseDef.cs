using System;
using DDrive.Foundation.Easing;
using UnityEngine;

namespace DDrive.Foundation.Values
{
    // Mode=Parametric の中身。名前付き Ease 31 種、または任意 CubicBezier のどちらか。
    [Serializable]
    public struct EaseDef
    {
        public ParametricKind Kind;
        public Ease Ease;
        public Vector2 BezierP1;
        public Vector2 BezierP2;

        public static EaseDef Named(Ease ease) => new() { Kind = ParametricKind.NamedEase, Ease = ease };

        public static EaseDef Bezier(Vector2 p1, Vector2 p2) => new()
        {
            Kind = ParametricKind.CustomBezier,
            BezierP1 = p1,
            BezierP2 = p2,
        };

        public float Evaluate(float t) => Kind == ParametricKind.CustomBezier
            ? CubicBezierEase.Evaluate(BezierP1.x, BezierP1.y, BezierP2.x, BezierP2.y, t)
            : EasingCore.Evaluate(Ease, t);
    }
}
