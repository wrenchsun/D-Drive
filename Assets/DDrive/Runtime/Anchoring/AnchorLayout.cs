using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    // 配置セットの 1 点(原点ローカル)。パターン生成と手置きを同じ形にする。
    public struct AnchorLayoutPoint
    {
        public int Index;
        public Vector3 LocalOffset;
        public Vector3 LocalEuler;
        public Vector3 LocalScale;
    }

    // [22_anchor_group.md] §3.2 — パターンから点を生成する純粋関数。ランタイム(再生)とエディタ(ギズモ)が同じ式を通す。
    // 呼び出し側が用意した固定長バッファに書き込み、点数を返す(GC alloc 0)。
    public static class AnchorLayout
    {
        // sampleRandom=false のとき Random レイアウトは固定シード(Seed が 0 なら 1)で生成する(エディタ表示が毎フレーム動かないように)。
        public static int Generate(AnchorGroupData group, AnchorLayoutPoint[] buffer, bool sampleRandom)
        {
            if (group == null || buffer == null)
            {
                return 0;
            }

            var count = 0;
            switch (group.Layout)
            {
                case AnchorLayoutKind.Grid:
                    count = GenerateGrid(group, buffer);
                    break;
                case AnchorLayoutKind.Circle:
                    count = GenerateCircle(group, buffer);
                    break;
                case AnchorLayoutKind.Line:
                    count = GenerateLine(group, buffer);
                    break;
                case AnchorLayoutKind.Random:
                    count = GenerateRandom(group, buffer, sampleRandom);
                    break;
                case AnchorLayoutKind.Manual:
                default:
                    break;
            }

            // 手置きの点はどのパターンにも追加できる(Manual はこれだけ)。
            if (group.Points != null)
            {
                for (var i = 0; i < group.Points.Length && count < buffer.Length; i++)
                {
                    var p = group.Points[i];
                    buffer[count] = new AnchorLayoutPoint
                    {
                        Index = count,
                        LocalOffset = p.LocalOffset,
                        LocalEuler = p.LocalEuler,
                        LocalScale = p.LocalScale == Vector3.zero ? Vector3.one : p.LocalScale,
                    };
                    count++;
                }
            }

            return count;
        }

        private static int GenerateGrid(AnchorGroupData g, AnchorLayoutPoint[] buffer)
        {
            var cx = Mathf.Max(1, g.GridCountX);
            var cy = Mathf.Max(1, g.GridCountY);
            var cz = Mathf.Max(1, g.GridCountZ);
            var origin = g.GridCentered
                ? new Vector3(-(cx - 1) * g.GridSpacing.x * 0.5f, -(cy - 1) * g.GridSpacing.y * 0.5f, -(cz - 1) * g.GridSpacing.z * 0.5f)
                : Vector3.zero;

            var count = 0;
            for (var y = 0; y < cy && count < buffer.Length; y++)
            {
                for (var z = 0; z < cz && count < buffer.Length; z++)
                {
                    for (var x = 0; x < cx && count < buffer.Length; x++)
                    {
                        buffer[count] = new AnchorLayoutPoint
                        {
                            Index = count,
                            LocalOffset = origin + new Vector3(x * g.GridSpacing.x, y * g.GridSpacing.y, z * g.GridSpacing.z),
                            LocalEuler = Vector3.zero,
                            LocalScale = Vector3.one,
                        };
                        count++;
                    }
                }
            }

            return count;
        }

        private static int GenerateCircle(AnchorGroupData g, AnchorLayoutPoint[] buffer)
        {
            var n = Mathf.Max(1, g.CircleCount);
            var arc = Mathf.Clamp(g.CircleArc, 0f, 360f);
            // 全周なら n 等分、扇形なら両端を含む n 点。
            var step = arc >= 360f - 1e-3f ? 360f / n : (n > 1 ? arc / (n - 1) : 0f);

            var count = 0;
            for (var i = 0; i < n && count < buffer.Length; i++)
            {
                var angle = g.CircleStartAngle + step * i;
                var rad = angle * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                buffer[count] = new AnchorLayoutPoint
                {
                    Index = count,
                    LocalOffset = dir * g.CircleRadius,
                    LocalEuler = g.CircleFaceOutward ? new Vector3(0f, angle, 0f) : Vector3.zero,
                    LocalScale = Vector3.one,
                };
                count++;
            }

            return count;
        }

        private static int GenerateLine(AnchorGroupData g, AnchorLayoutPoint[] buffer)
        {
            var n = Mathf.Max(1, g.LineCount);
            var dir = g.LineDirection.sqrMagnitude > 1e-6f ? g.LineDirection.normalized : Vector3.forward;
            var step = n > 1 ? g.LineLength / (n - 1) : 0f;
            var start = g.LineCentered ? -dir * (g.LineLength * 0.5f) : Vector3.zero;

            var count = 0;
            for (var i = 0; i < n && count < buffer.Length; i++)
            {
                buffer[count] = new AnchorLayoutPoint
                {
                    Index = count,
                    LocalOffset = start + dir * (step * i),
                    LocalEuler = Vector3.zero,
                    LocalScale = Vector3.one,
                };
                count++;
            }

            return count;
        }

        private static int GenerateRandom(AnchorGroupData g, AnchorLayoutPoint[] buffer, bool sampleRandom)
        {
            var n = Mathf.Max(1, g.RandomCount);
            var useSeed = g.RandomSeed != 0 || !sampleRandom;
            var seed = g.RandomSeed != 0 ? g.RandomSeed : 1;

            Random.State saved = default;
            if (useSeed)
            {
                saved = Random.state;
                Random.InitState(seed);
            }

            var count = 0;
            for (var i = 0; i < n && count < buffer.Length; i++)
            {
                buffer[count] = new AnchorLayoutPoint
                {
                    Index = count,
                    LocalOffset = Random.insideUnitSphere * g.RandomRadius,
                    LocalEuler = Vector3.zero,
                    LocalScale = Vector3.one,
                };
                count++;
            }

            if (useSeed)
            {
                Random.state = saved;
            }

            return count;
        }

        // 親(原点 or 上位の点)の合成結果に、点のオフセットとその点のランダム/ディレイ/確率を積む。
        // AnchorChain.Compose の子ノードと同じ式([04] §2.6)。
        public static AnchorSpawnSpec ComposePoint(in AnchorSpawnSpec parent, in AnchorLayoutPoint point, AnchorGroupData group, bool sampleRandom)
        {
            var def = parent.Def;
            var rot = Quaternion.Euler(def.LocalEuler);
            var scale = def.LocalScale == Vector3.zero ? Vector3.one : def.LocalScale;

            def.LocalOffset += rot * Vector3.Scale(scale, point.LocalOffset);
            var pointRot = rot * Quaternion.Euler(point.LocalEuler);
            def.LocalEuler = pointRot.eulerAngles;
            def.LocalScale = Vector3.Scale(scale, point.LocalScale == Vector3.zero ? Vector3.one : point.LocalScale);

            var spec = new AnchorSpawnSpec
            {
                Def = def,
                ExtraOffset = parent.ExtraOffset,
                JitterRotation = parent.JitterRotation,
                ScaleMultiplier = parent.ScaleMultiplier,
                DelaySec = parent.DelaySec + group.DelayPerIndex * point.Index,
                SpawnChance = parent.SpawnChance * Mathf.Clamp01(group.ChancePerPoint),
            };

            if (!sampleRandom)
            {
                return spec;
            }

            if (group.DelayJitterSec > 0f)
            {
                spec.DelaySec += Random.Range(0f, group.DelayJitterSec);
            }

            if (group.PositionJitterRadius > 0f)
            {
                spec.ExtraOffset += pointRot * (Random.insideUnitSphere * group.PositionJitterRadius);
            }

            if (group.EulerJitter != Vector3.zero)
            {
                spec.JitterRotation *= Quaternion.Euler(
                    Random.Range(-group.EulerJitter.x, group.EulerJitter.x),
                    Random.Range(-group.EulerJitter.y, group.EulerJitter.y),
                    Random.Range(-group.EulerJitter.z, group.EulerJitter.z));
            }

            if (group.ScaleRange.x > 0f || group.ScaleRange.y > 0f)
            {
                spec.ScaleMultiplier *= Random.Range(Mathf.Min(group.ScaleRange.x, group.ScaleRange.y), Mathf.Max(group.ScaleRange.x, group.ScaleRange.y));
            }

            return spec;
        }
    }
}
