using System.Collections.Generic;
using UnityEngine;

namespace ExternalPackage.Fake
{
    // LateUpdate で「この Unity フレームの Update で外部 Track の ProcessFrame が呼ばれ済みか」を記録する。
    [DefaultExecutionOrder(10000)]
    public sealed class ExternalLateFrameReader : MonoBehaviour
    {
        public readonly List<bool> ProcessedThisFrame = new();

        private void LateUpdate()
            => ProcessedThisFrame.Add(ExternalProbeLog.LastProcessFrameUnityFrame == Time.frameCount);
    }
}
