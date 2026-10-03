using UnityEngine;
using UnityEngine.Playables;

namespace ExternalPackage.Fake
{
    // 外部の Track が返す Mixer。ProcessFrame で時刻・バインド先・Unity のフレーム番号を残す。
    public sealed class ExternalProbeMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            ExternalProbeLog.LastProcessFrameUnityFrame = Time.frameCount;
            ExternalProbeLog.Frames.Add(new ExternalProbeLog.Frame
            {
                Time = playable.GetTime(),
                PlayerData = playerData,
                UnityFrame = Time.frameCount,
            });
        }
    }
}
