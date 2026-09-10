using System;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §4/§10 — NetMode.Simulated な Prefab の生成メッセージ(4-13)。
    //
    // Cosmetic(SeNetMsg/VfxNetMsg)と違い、Simulated はサーバー権威。クライアントは Spawn を要求するだけで
    // 実体を持たず(ローカル Instantiate しない)、サーバーが検証の上で生成してから結果を全員へ通知する。
    // NgoNetBridge との実際の NetworkObject 複製配線は Phase 6 の NGO 統合で行う([14] §12)。
    [Serializable]
    public struct PrefabSpawnRequestMsg : INetMessage
    {
        public ulong PrefabId;
        public Vector3 Position;
        public Quaternion Rotation;
        public uint RequestKey; // 呼び出し元が結果を突き合わせるための任意キー(現状は未使用でも可)
    }

    [Serializable]
    public struct PrefabSpawnedMsg : INetMessage
    {
        public ulong PrefabId;
        public ulong NetObjectId; // LocalLoopbackBridge では常に 0(NGO 統合後に NetworkObjectId を入れる)
        public Vector3 Position;
        public Quaternion Rotation;
        public uint RequestKey;
    }

    [Serializable]
    public struct PrefabDespawnedMsg : INetMessage
    {
        public ulong NetObjectId;
    }
}
