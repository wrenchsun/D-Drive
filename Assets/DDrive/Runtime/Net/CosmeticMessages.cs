using System;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §4/§8 — SE/VFX の Cosmetic 配送メッセージ。
    //
    // 既知のスコープ制限(2026-07-27時点): AnchorNetId は将来の NGO アダプタ向けの予約フィールドで、
    // 現状は常に 0(未使用)。INetBridge には Transform→NetId の逆引きが無く(ResolveNetObject は
    // 逆方向のみ)、送信側でアタッチ先の NetId を安全に得る手段が Foundation 層に無いため、今回は
    // 「送信時点で解決したワールド座標」を送るだけの MVP とする(受信側でアンカーへの追従はしない)。
    // 実際の NGO アダプタでは contextRoot.GetComponent<NetworkObject>().NetworkObjectId を
    // 直接使えるため、そちらで AnchorNetId を埋めて追従再生に拡張できる。
    // 同様に ParamValue の paramOverrides 配送も未実装(AC の要求範囲外のため今回は見送り)。
    [Serializable]
    public struct SeNetMsg : INetMessage
    {
        public ulong SeId;
        public ulong AnchorNetId; // 予約(常に 0)
        public Vector3 Position;
    }

    [Serializable]
    public struct SeNetBatchMsg : INetMessage
    {
        public SeNetMsg[] Items;
    }

    [Serializable]
    public struct VfxNetMsg : INetMessage
    {
        public ulong VfxId;
        public ulong AnchorNetId; // 予約(常に 0)
        public Vector3 Position;
    }

    [Serializable]
    public struct VfxNetBatchMsg : INetMessage
    {
        public VfxNetMsg[] Items;
    }
}
