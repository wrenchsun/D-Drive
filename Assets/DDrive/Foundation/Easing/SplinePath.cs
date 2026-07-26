using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Foundation.Easing
{
    // CatmullRom/Hermite/BSpline は制御点を n-1 個のセグメントとして通し番号で辿る(端は clamp して
    // 実質両端点を複製した扱いにする)。Bezier は 4 点 1 セグメント(3n+1 点)。
    // Evaluate(u) は弧長テーブルで再パラメータ化した等速アクセサ(FR-16.2)。
    public sealed class SplinePath
    {
        private const int SamplesPerSegment = 256;

        private readonly Vector3[] _points;
        private readonly SplineType _type;
        private readonly int _segmentCount;
        private readonly float[] _sampleParams;
        private readonly float[] _cumulativeLengths;

        public float Length { get; }

        public SplinePath(IReadOnlyList<Vector3> points, SplineType type)
        {
            _points = new Vector3[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                _points[i] = points[i];
            }

            _type = type;
            _segmentCount = ComputeSegmentCount(_points.Length, type);

            var totalSamples = _segmentCount * SamplesPerSegment + 1;
            _sampleParams = new float[totalSamples];
            _cumulativeLengths = new float[totalSamples];

            var prev = EvaluateRaw(0f);
            _sampleParams[0] = 0f;
            _cumulativeLengths[0] = 0f;

            for (var i = 1; i < totalSamples; i++)
            {
                var globalT = (float)i / (totalSamples - 1) * _segmentCount;
                var p = EvaluateRaw(globalT);
                _cumulativeLengths[i] = _cumulativeLengths[i - 1] + Vector3.Distance(prev, p);
                _sampleParams[i] = globalT;
                prev = p;
            }

            Length = _cumulativeLengths[totalSamples - 1];
        }

        // u: 0-1 の正規化パラメータ。弧長ベースで等速。
        public Vector3 Evaluate(float u)
        {
            u = Mathf.Clamp01(u);
            var targetLength = u * Length;

            var lo = 0;
            var hi = _cumulativeLengths.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (_cumulativeLengths[mid] < targetLength)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            if (lo == 0)
            {
                return EvaluateRaw(_sampleParams[0]);
            }

            var lenA = _cumulativeLengths[lo - 1];
            var lenB = _cumulativeLengths[lo];
            var segFrac = lenB > lenA ? (targetLength - lenA) / (lenB - lenA) : 0f;
            var globalT = Mathf.Lerp(_sampleParams[lo - 1], _sampleParams[lo], segFrac);

            return EvaluateRaw(globalT);
        }

        private static int ComputeSegmentCount(int pointCount, SplineType type)
        {
            return type == SplineType.Bezier
                ? Mathf.Max(1, (pointCount - 1) / 3)
                : Mathf.Max(1, pointCount - 1);
        }

        private Vector3 EvaluateRaw(float globalT)
        {
            globalT = Mathf.Clamp(globalT, 0f, _segmentCount);
            var seg = Mathf.Min((int)globalT, _segmentCount - 1);
            var localT = globalT - seg;
            return EvaluateSegment(seg, localT);
        }

        private Vector3 EvaluateSegment(int seg, float t)
        {
            switch (_type)
            {
                case SplineType.Bezier:
                {
                    var i = seg * 3;
                    return CubicBezier(GetClamped(i), GetClamped(i + 1), GetClamped(i + 2), GetClamped(i + 3), t);
                }

                case SplineType.Hermite:
                {
                    var p0 = GetClamped(seg);
                    var p1 = GetClamped(seg + 1);
                    var m0 = (GetClamped(seg + 1) - GetClamped(seg - 1)) * 0.5f;
                    var m1 = (GetClamped(seg + 2) - GetClamped(seg)) * 0.5f;
                    return Hermite(p0, p1, m0, m1, t);
                }

                case SplineType.BSpline:
                    return UniformCubicBSpline(GetClamped(seg - 1), GetClamped(seg), GetClamped(seg + 1), GetClamped(seg + 2), t);

                default:
                    return CatmullRom(GetClamped(seg - 1), GetClamped(seg), GetClamped(seg + 1), GetClamped(seg + 2), t);
            }
        }

        private Vector3 GetClamped(int index) => _points[Mathf.Clamp(index, 0, _points.Length - 1)];

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private static Vector3 Hermite(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            var h00 = 2f * t3 - 3f * t2 + 1f;
            var h10 = t3 - 2f * t2 + t;
            var h01 = -2f * t3 + 3f * t2;
            var h11 = t3 - t2;
            return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
        }

        private static Vector3 UniformCubicBSpline(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            var b0 = (1f - 3f * t + 3f * t2 - t3) / 6f;
            var b1 = (4f - 6f * t2 + 3f * t3) / 6f;
            var b2 = (1f + 3f * t + 3f * t2 - 3f * t3) / 6f;
            var b3 = t3 / 6f;
            return b0 * p0 + b1 * p1 + b2 * p2 + b3 * p3;
        }
    }
}
