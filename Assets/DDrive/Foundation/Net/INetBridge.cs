using System;
using UnityEngine;

namespace DDrive.Foundation.Net
{
    // トランスポート抽象。シングルプレイは LocalLoopbackBridge、v1 のマルチプレイは NGO アダプタ(0-14)。
    public interface INetBridge
    {
        bool IsServer { get; }
        bool IsClient { get; }
        double NetworkTime { get; }

        // [14_networking.md] §2/§5(6-0) — ローカル(自分自身)の ClientId。NGO では NetworkManager.LocalClientId、
        // Loopback(シングルプレイ)では常に 0(Host 相当)。HandleNetKey へ発行者を埋め込む検証([14] §9)に使う。
        ulong LocalClientId { get; }

        void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage;
        void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage;
        IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage;
        Transform ResolveNetObject(ulong netId);

        // [14_networking.md] §4/§5(6-0) — Transform → NetId の逆引き(ResolveNetObject の逆方向)。
        // SelfNetId/TargetNetId/AnchorNetId を実値で送るための primitive。解決できない場合は 0
        // (受信側は既存のとおり Position にフォールバックする)。
        ulong ResolveNetId(Transform transform);

        // [14_networking.md] §5 追加指示(2026-09-14, 6-0) — 解決済みの Transform が「ローカルクライアントが
        // 所有するオブジェクトか」を判定する(HapticsData.LocalPlayerOnly の誤爆防止に使う)。
        // Loopback は常に true(シングルプレイは全て自分)。NGO は NetworkObject.OwnerClientId == LocalClientId。
        bool IsLocalPlayerObject(Transform transform);

        // [14_networking.md] §3/§10(6-0) — NetMode.Simulated な Prefab を Host 権威で NetworkObject として
        // 生成する(すでにローカルへ Instantiate 済みの root を渡す)。対応しない実装(Loopback 等)は常に 0 を返し、
        // 呼び出し元は今までどおり NetObjectId=0 のローカル専用インスタンスとして扱う。
        ulong SpawnNetworked(GameObject root);

        // 対になる Despawn。destroy=false は NGO の Despawn(destroy:false) 相当(Pool へ戻す想定)。
        // 対応しない実装は no-op。
        void DespawnNetworked(ulong netId, bool destroy);

        // [14_networking.md] §5(5-9) — 新規接続通知(Host 視点)。Late Join でアクティブな演出リストを
        // スナップショット送信するための最小限の口。シングルプレイ(LocalLoopbackBridge)では通常発火しない。
        event Action<ulong> ClientConnected;

        // [14_networking.md] §7(6-5) — カタログ ContentHash 不一致(リリースビルド)時に Host が該当
        // Client を切断するための primitive。Host からのみ意味を持つ(Client/Loopback からの呼び出しは
        // no-op または警告)。reason は null 可(NGO では NetworkManager.DisconnectReason 経由で Client 側に
        // 伝わる)。
        void DisconnectClient(ulong clientId, string reason);
    }
}
