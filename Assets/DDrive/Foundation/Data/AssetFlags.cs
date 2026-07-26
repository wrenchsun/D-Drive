using System;
using DDrive.Foundation.Net;

namespace DDrive.Foundation.Data
{
    public enum PauseMode
    {
        PauseWithGame,
        IgnorePause,
        UIOnly,
    }

    public enum LoadMode
    {
        // 既定値(0)は LazyLoad。未設定のまま放置しても勝手にプリロードされないようにする。
        LazyLoad,
        Preload,
        Streaming,
    }

    public enum AssetDomain
    {
        Game3D,
        UI,
        Both,
    }

    public enum PoolPolicyKind
    {
        None,
        Pooled,
    }

    [Serializable]
    public struct PoolPolicy
    {
        public PoolPolicyKind Kind;
        public int InitialCount;
        public int MaxCount;

        public static readonly PoolPolicy None = default;

        public static PoolPolicy Pooled(int initialCount, int maxCount) => new PoolPolicy
        {
            Kind = PoolPolicyKind.Pooled,
            InitialCount = initialCount,
            MaxCount = maxCount,
        };
    }

    [Serializable]
    public struct AssetFlags
    {
        public PauseMode Pause;
        public LoadMode Load;
        public PoolPolicy Pool;
        public int Priority;
        public bool Persistent;
        public AssetDomain Domain;
        public NetMode Net;
    }
}
