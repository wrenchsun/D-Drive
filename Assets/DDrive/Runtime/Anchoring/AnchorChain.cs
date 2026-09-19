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

    // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照) — AnchorData(ScriptableObject)
    // の値そのもの、またはトラック/埋め込み Anchor 由来の「ジッター/ディレイ/確率を持たない 1 段」を同じ形で
    // 表現する。AnchorChain.Compose の本体(ComposeNodes)はこの型の配列だけを見て合成するため、
    // PresentationTrackAnchorComposer が「トラック Anchor を親、アセット側を子として合成する」ケースでも
    // 同じアルゴリズムを再利用できる(コピペしない)。
    public struct AnchorChainNode
    {
        public AnchorDef Def;
        public float PositionJitterRadius;
        public Vector3 EulerJitter;
        public Vector2 ScaleRange;
        public float DelaySec;
        public float DelayJitterSec;
        public float SpawnChance;

        public static AnchorChainNode FromAnchorData(AnchorData data) => new()
        {
            Def = data.ToDef(),
            PositionJitterRadius = data.PositionJitterRadius,
            EulerJitter = data.EulerJitter,
            ScaleRange = data.ScaleRange,
            DelaySec = data.DelaySec,
            DelayJitterSec = data.DelayJitterSec,
            SpawnChance = data.SpawnChance,
        };

        // トラック / 埋め込み Anchor(AnchorDef そのもの)用: ジッター・ディレイ・確率を持たない単純ノード。
        public static AnchorChainNode FromDef(in AnchorDef def) => new()
        {
            Def = def,
            ScaleRange = new Vector2(1f, 1f),
            SpawnChance = 1f,
        };
    }

    // AnchorData の連鎖を Registry から辿って合成する。定常経路(Spawn/Play)から呼ばれるため
    // 固定長バッファで GC alloc 0。循環・深さ超過は警告して「そこまで」をルートとして扱う(例外で止めない)。
    public static class AnchorChain
    {
        public const int MaxDepth = 8;

        private static readonly AnchorData[] Buffer = new AnchorData[MaxDepth];
        private static readonly AnchorChainNode[] NodeBuffer = new AnchorChainNode[MaxDepth];

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
            for (var i = 0; i < count; i++)
            {
                NodeBuffer[i] = AnchorChainNode.FromAnchorData(nodesLeafToRoot[i]);
            }

            var spec = ComposeNodes(NodeBuffer, count, sampleRandom);
            System.Array.Clear(NodeBuffer, 0, count);
            return spec;
        }

        // 合成の本体(AnchorData に依存しない)。[08_presentation.md] 実装メモ(2026-09-19)—
        // PresentationTrackAnchorComposer が「トラック Anchor を親、アセット側を子」として合成するのに
        // 再利用する(コピペしない)。アルゴリズム自体は旧 Compose(AnchorData[], ...) と同一。
        public static AnchorSpawnSpec ComposeNodes(AnchorChainNode[] nodesLeafToRoot, int count, bool sampleRandom)
        {
            var root = nodesLeafToRoot[count - 1];
            var def = root.Def;

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
                    var childScale = node.Def.LocalScale == Vector3.zero ? Vector3.one : node.Def.LocalScale;
                    pos += rot * Vector3.Scale(scale, node.Def.LocalOffset);
                    rot *= Quaternion.Euler(node.Def.LocalEuler);
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

        // id から Parent を辿って buffer に積む(leaf→root順)。戻り値はノード数(0 = id が無効)。
        // public: PresentationTrackAnchorComposer(Runtime/Presentation)が「アセット側の連鎖単独」を
        // 呼び出し側所有の buffer へ取得するために使う(内部の静的 Buffer とは独立、再入可能)。
        public static int CollectChainInto(IAssetRegistry registry, AssetId<AnchorMarker> id, AnchorData[] buffer)
        {
            var count = 0;
            var current = id;

            while (current.IsValid)
            {
                if (count >= buffer.Length)
                {
                    Debug.LogWarning($"[DDrive] Anchor chain deeper than {buffer.Length} at {id}; treating the reached node as root.");
                    break;
                }

                // 未登録は Placeholder(World 既定値・Parent 無し)に落ちるので、ここで連鎖は自然に止まる。
                var node = registry.ResolveOrPlaceholder<AnchorData>(current.Value);

                var duplicate = false;
                for (var i = 0; i < count; i++)
                {
                    if (ReferenceEquals(buffer[i], node))
                    {
                        Debug.LogWarning($"[DDrive] Anchor chain has a cycle at {current}; treating '{buffer[count - 1].name}' as root.");
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    break;
                }

                buffer[count++] = node;
                current = node.Parent;
            }

            return count;
        }

        private static int CollectChain(IAssetRegistry registry, AssetId<AnchorMarker> id) => CollectChainInto(registry, id, Buffer);
    }
}
