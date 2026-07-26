using System;
using UnityEngine;

namespace DDrive.Foundation.Values
{
    // デザイナーが調整するスカラー値の統一表現(FR-19)。Evaluate は 0 alloc の純関数。
    [Serializable]
    public struct ValueDef
    {
        public ValueMode Mode;

        public float Constant;
        public EaseDef Parametric;
        public AnimationCurve Curve;

        public float From;
        public float To;
        public bool Normalized;

        public TimeDef Time;
        public LoopMode Loop;
        public int LoopCount;

        public static ValueDef Constant01(float value) => new() { Mode = ValueMode.Constant, Constant = value };

        // t: 正規化時間 0..1。Loop の解決は EvaluateAt が担う。
        public float Evaluate(float t)
        {
            switch (Mode)
            {
                case ValueMode.Constant:
                    return Constant;

                case ValueMode.Parametric:
                    return Mathf.LerpUnclamped(From, To, Parametric.Evaluate(t));

                case ValueMode.Curve:
                {
                    var raw = Curve != null ? Curve.Evaluate(t) : 0f;
                    return Normalized ? raw : Mathf.LerpUnclamped(From, To, raw);
                }

                default:
                    return 0f;
            }
        }

        // Duration は TimeDef から解決した「1 周期」の実尺。Rate/Speed は t が 0→1 進む時間として定義する
        // (Speed の基準尺は呼び出し側が持つため、ここでは基準=1 秒相当として扱う)。
        public float Duration => Time.Mode switch
        {
            TimeMode.Duration => Time.Value,
            TimeMode.Speed => Time.Value > 0f ? 1f / Time.Value : 0f,
            TimeMode.Rate => Time.Value > 0f ? 1f / Time.Value : 0f,
            _ => 0f,
        };

        public float EvaluateAt(float elapsedSeconds) => Evaluate(ResolveNormalizedT(elapsedSeconds));

        // Duration/Loop の解決だけを取り出したもの。ValueDefColor 等、複数フィールドで
        // 同じ時間軸を共有したい場合に t だけを求めるために公開する。
        public float ResolveNormalizedT(float elapsedSeconds)
        {
            var duration = Duration;
            if (duration <= 0f)
            {
                return 1f;
            }

            var speedScale = Time.SpeedScale <= 0f ? 1f : Time.SpeedScale;
            var p = (elapsedSeconds * speedScale) / duration;

            return Loop switch
            {
                LoopMode.Once => Mathf.Clamp01(p),
                LoopMode.Loop => ResolveLoopT(p),
                LoopMode.PingPong => ResolvePingPongT(p),
                _ => Mathf.Clamp01(p),
            };
        }

        private float ResolveLoopT(float p)
        {
            if (LoopCount > 0 && p >= LoopCount)
            {
                return 1f;
            }

            return Mathf.Repeat(p, 1f);
        }

        private float ResolvePingPongT(float p)
        {
            var clamped = LoopCount > 0 ? Mathf.Min(p, LoopCount) : p;
            return Mathf.PingPong(clamped, 1f);
        }
    }
}
