using System;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §4/§8 — SE/VFX の Cosmetic 配送メッセージ。
    //
    // AnchorNetId(6-0 で接続): INetBridge.ResolveNetId(Transform→NetId の逆引き)で contextRoot の
    // NetId が解決できた場合は実値を送る。受信側は AnchorNetId!=0 のときは ResolveNetObject で解決して
    // そこへ追従再生し、解決できない(0、または NGO 未接続/対象が既に Despawn 済み)場合は
    // 「送信時点で解決したワールド座標(Position)」に固定で再生する(2026-07-27 時点の MVP から継続)。
    // 同様に ParamValue の paramOverrides 配送も未実装(AC の要求範囲外のため今回は見送り)。
    [Serializable]
    public struct SeNetMsg : INetMessage
    {
        public ulong SeId;
        public ulong AnchorNetId; // 0 = 未解決(Position にフォールバック)
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
        public ulong AnchorNetId; // 0 = 未解決(Position にフォールバック)
        public Vector3 Position;
    }

    [Serializable]
    public struct VfxNetBatchMsg : INetMessage
    {
        public VfxNetMsg[] Items;
    }
}
