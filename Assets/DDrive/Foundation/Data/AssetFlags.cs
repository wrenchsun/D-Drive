using System;
using DDrive.Foundation.Net;
using UnityEngine;

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

        [Tooltip("シーン開始時等にあらかじめ生成しておく数。")]
        public int InitialCount;

        [Tooltip("同時に存在できる上限。超過時は最も優先度の低いものを強制回収する。")]
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
        [Tooltip("PauseWithGame=ゲームのポーズに追従 / IgnorePause=常に再生継続 / UIOnly=UI操作系のみ追従。")]
        public PauseMode Pause;

        [Tooltip("LazyLoad=初回参照時にロード / Preload=カタログ登録時に一括ロード / Streaming=随時ストリーミング。")]
        public LoadMode Load;

        [Tooltip("プール(使い回し)の設定。None ならプールしない。")]
        public PoolPolicy Pool;

        [Tooltip("同時再生数上限に達した時の優先度。数値が高いほど残りやすい。")]
        public int Priority;

        [Tooltip("シーンをまたいで破棄しないか。")]
        public bool Persistent;

        [Tooltip("Game3D=通常のゲーム内 / UI=UI専用 / Both=両方で使う。")]
        public AssetDomain Domain;

        [Tooltip("Local=完全ローカル / Cosmetic=見た目のみ全クライアントで再生 / Simulated=結果に影響するためサーバー権威。")]
        public NetMode Net;
    }
}
