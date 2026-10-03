using System.Collections.Generic;
using UnityEngine;

namespace ExternalPackage.Fake
{
    // 外部の Runner を模す。実行順 10000 の LateUpdate で指定シェイプへ重みを書く(T-Drive の Runner と同じ位置)。
    // LateUpdate の冒頭で「他のシェイプの現在値」を記録する(= その直前の Update で D-Drive が何を書いたか / 書かなかったか)。
    [DefaultExecutionOrder(10000)]
    public sealed class ExternalLateBlendShapeWriter : MonoBehaviour
    {
        public SkinnedMeshRenderer Smr;
        public int WriteIndex;
        public float WriteWeight;

        // 書く前に見えていた重み(WriteIndex 側 / 観測用の別シェイプ側)。
        public readonly List<float> SeenAtLateStart = new();
        public int ObserveIndex = -1;
        public readonly List<float> ObservedAtLateStart = new();
        public int LateCount;

        private void LateUpdate()
        {
            if (Smr == null)
            {
                return;
            }

            LateCount++;
            SeenAtLateStart.Add(Smr.GetBlendShapeWeight(WriteIndex));
            if (ObserveIndex >= 0)
            {
                ObservedAtLateStart.Add(Smr.GetBlendShapeWeight(ObserveIndex));
            }

            Smr.SetBlendShapeWeight(WriteIndex, WriteWeight);
        }
    }
}
