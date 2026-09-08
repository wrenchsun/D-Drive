using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    // AnchorId の連鎖(子 → 親 → … → ルート)を辿って「1 つの AnchorDef 相当」に合成した結果。
    // Manager はこれを既存の AnchorPose の式にそのまま通す([21_anchor_spec.md] §3.2 / [04] §2.6)。
    public struct AnchorSpawnSpec
    {
        // 合成済み定義。Space/Path/FollowRotation/DetachOnStop はルートの値、オフセット/回転/スケールは連鎖の合成。
        public AnchorDef Def;

        // ランダム分(Spawn 時に 1 回サンプリング)。AnchorPoint 由来の extra/jitter/scaleMul と同じ扱いで Instance が保持する。
        public Vector3 ExtraOffset;
        public Quaternion JitterRotation;
        public float ScaleMultiplier;

        // 生成タイミング。連鎖では Delay は加算、Chance は乗算。
        public float DelaySec;
        public float SpawnChance;

        public static AnchorSpawnSpec FromDef(in AnchorDef def) => new()
        {
            Def = def,
            ExtraOffset = Vector3.zero,
            JitterRotation = Quaternion.identity,
            ScaleMultiplier = 1f,
            DelaySec = 0f,
            SpawnChance = 1f,
        };
    }

    // AnchorData の連鎖を Registry から辿って合成する。定常経路(Spawn/Play)から呼ばれるため
    // 固定長バッファで GC alloc 0。循環・深さ超過は警告して「そこまで」をルートとして扱う(例外で止めない)。
    public static class AnchorChain
    {
        public const int MaxDepth = 8;

        private static readonly AnchorData[] Buffer = new AnchorData[MaxDepth];

        // sampleRandom=false は「静的な合成だけ」(エディタの再適用・ネット送信位置の計算用)。
        public static AnchorSpawnSpec Resolve(IAssetRegistry registry, AssetId<AnchorMarker> id, bool sampleRandom)
        {
            var count = CollectChain(registry, id);
            if (count == 0)
            {
                return AnchorSpawnSpec.FromDef(AnchorDef.WorldDefault);
            }

            // Buffer[0] = 末端(指定された id)、Buffer[count-1] = ルート。
            var spec = Compose(Buffer, count, sampleRandom);
            System.Array.Clear(Buffer, 0, count);
            return spec;
        }

        // ルート → 末端の順に並んだノード列を合成する(純粋関数。テストからも直接呼べる)。
        // nodesLeafToRoot: [0]=末端 … [count-1]=ルート。
        public static AnchorSpawnSpec Compose(AnchorData[] nodesLeafToRoot, int count, bool sampleRandom)
        {
            var root = nodesLeafToRoot[count - 1];
            var def = root.ToDef();

            var pos = def.LocalOffset;
            var rot = Quaternion.Euler(def.LocalEuler);
            var scale = def.LocalScale;

            var extra = Vector3.zero;
            var jitter = Quaternion.identity;
            var scaleMul = 1f;
            var delay = 0f;
            var chance = 1f;

            for (var i = count - 1; i >= 0; i--)
            {
                var node = nodesLeafToRoot[i];

                if (i != count - 1)
                {
                    // 子: 親で決まった姿勢(pos/rot/scale)を基準にオフセットを積む。
                    var childScale = node.LocalScale == Vector3.zero ? Vector3.one : node.LocalScale;
                    pos += rot * Vector3.Scale(scale, node.LocalOffset);
                    rot *= Quaternion.Euler(node.LocalEuler);
                    scale = Vector3.Scale(scale, childScale);
                }

                delay += node.DelaySec;
                chance *= Mathf.Clamp01(node.SpawnChance);

                if (!sampleRandom)
                {
                    continue;
                }

                if (node.DelayJitterSec > 0f)
                {
                    delay += Random.Range(0f, node.DelayJitterSec);
                }

                if (node.PositionJitterRadius > 0f)
                {
                    // その段の向きで散らばらせ、ルート基準(target-local)の追加オフセットへ畳み込む。
                    extra += rot * (Random.insideUnitSphere * node.PositionJitterRadius);
                }

                if (node.EulerJitter != Vector3.zero)
                {
                    jitter *= Quaternion.Euler(
                        Random.Range(-node.EulerJitter.x, node.EulerJitter.x),
                        Random.Range(-node.EulerJitter.y, node.EulerJitter.y),
                        Random.Range(-node.EulerJitter.z, node.EulerJitter.z));
                }

                if (node.ScaleRange.x > 0f || node.ScaleRange.y > 0f)
                {
                    scaleMul *= Random.Range(Mathf.Min(node.ScaleRange.x, node.ScaleRange.y), Mathf.Max(node.ScaleRange.x, node.ScaleRange.y));
                }
            }

            def.LocalOffset = pos;
            def.LocalEuler = rot.eulerAngles;
            def.LocalScale = scale;

            return new AnchorSpawnSpec
            {
                Def = def,
                ExtraOffset = extra,
                JitterRotation = jitter,
                ScaleMultiplier = scaleMul,
                DelaySec = delay,
                SpawnChance = chance,
            };
        }

        // id から Parent を辿って Buffer に積む。戻り値はノード数(0 = id が無効)。
        private static int CollectChain(IAssetRegistry registry, AssetId<AnchorMarker> id)
        {
            var count = 0;
            var current = id;

            while (current.IsValid)
            {
                if (count >= MaxDepth)
                {
                    Debug.LogWarning($"[DDrive] Anchor chain deeper than {MaxDepth} at {id}; treating the reached node as root.");
                    break;
                }

                // 未登録は Placeholder(World 既定値・Parent 無し)に落ちるので、ここで連鎖は自然に止まる。
                var node = registry.ResolveOrPlaceholder<AnchorData>(current.Value);

                for (var i = 0; i < count; i++)
                {
                    if (ReferenceEquals(Buffer[i], node))
                    {
                        Debug.LogWarning($"[DDrive] Anchor chain has a cycle at {current}; treating '{Buffer[count - 1].name}' as root.");
                        return count;
                    }
                }

                Buffer[count++] = node;
                current = node.Parent;
            }

            return count;
        }
    }
}
