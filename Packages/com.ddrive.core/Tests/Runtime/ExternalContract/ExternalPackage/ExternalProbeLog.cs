using System.Collections.Generic;

namespace ExternalPackage.Fake
{
    // 外部パッケージ(T-Drive 等)を模したダミー Track / Clip / Mixer が評価の様子を書き残す場所。
    // 外部アセンブリから見える公開 API だけで書く(D-Drive の internal には触れない)。
    public static class ExternalProbeLog
    {
        public struct Frame
        {
            public double Time;
            public object PlayerData;
            public int UnityFrame;
        }

        public static readonly List<Frame> Frames = new();

        // 最後に ProcessFrame が呼ばれた Unity のフレーム番号(LateUpdate 側から読む)。
        public static int LastProcessFrameUnityFrame = -1;

        public static void Reset()
        {
            Frames.Clear();
            LastProcessFrameUnityFrame = -1;
        }
    }
}
