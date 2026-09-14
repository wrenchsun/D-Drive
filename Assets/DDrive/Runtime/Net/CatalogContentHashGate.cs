using System;
using System.Collections.Generic;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §7(6-5) — カタログ ContentHash の接続時照合(オーケストレーション)。
    //
    // 流れ:
    //   1. Client: DDriveRuntimeBootstrap.RegisterCatalogsAsync() 完了(SetLocalSummary)後、接続済みなら
    //      自分のハッシュを Broadcast する(1 回だけ)。
    //   2. Host: 新規接続(ClientConnected)ごとに「タイムアウト期限」を記録する。CatalogContentHashMsg を
    //      受信したら比較し、結果(Matched/Descriptions)を該当 Client へ SendTo する。
    //   3. 不一致・タイムアウトのどちらでも同じ方針(CatalogContentHashPolicy.Decide)を適用する:
    //      開発ビルド/エディタは警告のみで継続、リリースビルドは Host が該当 Client を切断する
    //      (2026-09-15 ユーザー決定、docs/11_tasks.md P6)。
    //   4. Client: Host からの結果を受けて自分側でも警告ログを出す(「双方に警告ログ」の要求)。
    //
    // Loopback(シングルプレイ)でも構築されるが、ClientConnected が通常発火しないため実質 no-op
    // (他 Manager と同じく「通信の有無で挙動を変えない」原則。[14] §1)。
    public sealed class CatalogContentHashGate
    {
        private readonly INetBridge _netBridge;
        private readonly double _timeoutSeconds;
        private readonly bool _isDevelopmentOrEditor;

        private readonly Dictionary<ulong, double> _pendingHostSideDeadlines = new();
        private readonly List<PendingMessage> _pendingBeforeReady = new();
        private readonly List<ulong> _expiredScratch = new();

        private readonly IDisposable _subHash;
        private readonly IDisposable _subResult;

        private CatalogContentHasher.CatalogHashEntry[] _localCatalogs = Array.Empty<CatalogContentHasher.CatalogHashEntry>();
        private ulong _localCombinedHash;
        private bool _registryReady;
        private bool _clientHashSent;

        // Client 視点: 実際に接続が確立した(ClientConnected が発火した)ことを覚えておく。
        // カタログ登録(SetLocalSummary)が先に済んでいても、接続前に Broadcast を試みない
        // (NgoNetBridge.Broadcast は未接続時に警告+no-op になるだけで、送信済みフラグだけが
        // 誤って立ってしまい再送されない事故を避ける)。
        private bool _clientConnectedFired;

        private struct PendingMessage
        {
            public ulong SenderId;
            public CatalogContentHashMsg Msg;
        }

        // NetDebugOverlay 表示用の一言状態("検証中" / "OK" / "不一致: ..." 等)。Host/Client どちらでも
        // 同じフィールドを見ればよいようにしている(役割ごとに別プロパティを持たない)。
        public string LastStatusText { get; private set; } = "検証中...";

        // ネット越しに接続してきた相手を実際に切断したことをテスト/上位が確認できるようにする通知。
        public event Action<ulong, string> ClientDisconnectedForMismatch;

        public CatalogContentHashGate(INetBridge netBridge, double timeoutSeconds, bool isDevelopmentOrEditor)
        {
            _netBridge = netBridge ?? throw new ArgumentNullException(nameof(netBridge));
            _timeoutSeconds = timeoutSeconds;
            _isDevelopmentOrEditor = isDevelopmentOrEditor;

            _subHash = _netBridge.Subscribe<CatalogContentHashMsg>(OnReceiveHashMsg);
            _subResult = _netBridge.Subscribe<CatalogContentHashResultMsg>(OnReceiveResultMsg);
            _netBridge.ClientConnected += OnClientConnected;
        }

        public void Dispose()
        {
            _subHash?.Dispose();
            _subResult?.Dispose();
            _netBridge.ClientConnected -= OnClientConnected;
        }

        // DDriveRuntimeBootstrap.RegisterCatalogsAsync() が IsReady=true にした直後に呼ぶ(自分の
        // カタログ内容が確定した時点。[14] §7 実装メモ参照)。
        public void SetLocalSummary(ulong combinedHash, CatalogContentHasher.CatalogHashEntry[] catalogs)
        {
            _localCombinedHash = combinedHash;
            _localCatalogs = catalogs ?? Array.Empty<CatalogContentHasher.CatalogHashEntry>();
            _registryReady = true;

            FlushPendingBeforeReady();
            TrySendOwnHash();
        }

        private void OnClientConnected(ulong clientId)
        {
            if (_netBridge.IsServer)
            {
                // 2026-09-15 修正(6-7 の自動テストで発覚した実バグ) — NGO の OnClientConnectedCallback は
                // Host 自身の自己接続(StartHost 時に Host が自分自身に対しても発火させる)でも呼ばれる
                // ([14_networking.md] §2/§12、NgoNetBridge.HandleClientConnected のコメント参照)。
                // Host は自分にハッシュを送る必要が無く(TrySendOwnHash が IsServer を弾いて no-op)、
                // 自己分の保留期限をここで登録してしまうと、実クライアントの有無に関わらず
                // `_timeoutSeconds` 後に必ずタイムアウトして LastStatusText が "OK" → "ContentHash 未受信"
                // に戻ってしまう(実機・自動テストの全シナリオで再現。Host 自身の clientId は無視する)。
                if (clientId == _netBridge.LocalClientId)
                {
                    return;
                }

                // 1v1(MS2026)想定だが Dictionary なので複数クライアントでも自然に扱える。
                _pendingHostSideDeadlines[clientId] = _netBridge.NetworkTime + _timeoutSeconds;
                return;
            }

            // Client 視点: 自分がちょうど繋がった(NGO の OnClientConnectedCallback は自分自身の接続でも
            // 発火する。[14] §5(5-9) 実装メモ参照)。
            _clientConnectedFired = true;
            TrySendOwnHash();
        }

        private void TrySendOwnHash()
        {
            if (_clientHashSent || !_registryReady || !_clientConnectedFired)
            {
                return;
            }

            if (!_netBridge.IsClient || _netBridge.IsServer)
            {
                return; // Host/Loopback は自分のハッシュを送る必要が無い(Host 権威、上部コメント参照)。
            }

            _clientHashSent = true;
            _netBridge.Broadcast(new CatalogContentHashMsg { CombinedHash = _localCombinedHash, Catalogs = _localCatalogs }, NetChannel.ReliableOrdered);
        }

        private void OnReceiveHashMsg(ulong senderId, CatalogContentHashMsg msg)
        {
            if (!_netBridge.IsServer)
            {
                return; // 比較は Host だけが行う(Host 権威)。
            }

            if (!_registryReady)
            {
                // Host 自身のカタログ登録がまだ済んでいない(理論上のレース。[14_networking.md] 6-0 修正3の
                // 「課題3」と同種のガード)。登録完了後に FlushPendingBeforeReady() でまとめて処理する。
                _pendingBeforeReady.Add(new PendingMessage { SenderId = senderId, Msg = msg });
                return;
            }

            ProcessHostSide(senderId, msg);
        }

        private void FlushPendingBeforeReady()
        {
            if (_pendingBeforeReady.Count == 0)
            {
                return;
            }

            var pending = new List<PendingMessage>(_pendingBeforeReady);
            _pendingBeforeReady.Clear();

            for (var i = 0; i < pending.Count; i++)
            {
                ProcessHostSide(pending[i].SenderId, pending[i].Msg);
            }
        }

        private void ProcessHostSide(ulong senderId, CatalogContentHashMsg msg)
        {
            _pendingHostSideDeadlines.Remove(senderId);

            var matched = msg.CombinedHash == _localCombinedHash;
            var descriptions = matched
                ? Array.Empty<string>()
                : CatalogContentHashPolicy.DescribeDifferences(_localCatalogs, msg.Catalogs).ToArray();

            // 合成ハッシュは違うのにカタログ単位の差分が見つからない(理論上は起こらない)ときも、
            // 例外で止めずに最低限の説明を残す(CLAUDE.md §0-4。以前は DescribeDifferences 側で付けていた)。
            if (!matched && descriptions.Length == 0)
            {
                descriptions = new[] { "(詳細不明: カタログ名の突き合わせでは差分が見つかりませんでした)" };
            }

            if (matched)
            {
                LastStatusText = "OK";
                _netBridge.SendTo(senderId, new CatalogContentHashResultMsg { Matched = true, Descriptions = descriptions }, NetChannel.ReliableOrdered);
                return;
            }

            var outcome = ApplyOutcome(senderId, descriptions, "ContentHash 不一致");
            if (outcome == CatalogContentHashPolicy.Outcome.Disconnect)
            {
                // 切断済みの Client へ追ってメッセージを送る意味は無い(NGO 的にも既に切断処理中)。
                return;
            }

            _netBridge.SendTo(senderId, new CatalogContentHashResultMsg { Matched = false, Descriptions = descriptions }, NetChannel.ReliableOrdered);
        }

        private void OnReceiveResultMsg(ulong senderId, CatalogContentHashResultMsg msg)
        {
            if (_netBridge.IsServer)
            {
                return; // Host は自分で判定済み(§ProcessHostSide)。
            }

            if (msg.Matched)
            {
                LastStatusText = "OK";
                return;
            }

            LastStatusText = "不一致: " + string.Join(", ", msg.Descriptions);
            Debug.LogWarning($"[Net/Client] CatalogContentHashGate: {LastStatusText}(Host と同じ GameData か確認してください)。");
        }

        // Bootstrap から毎フレーム(または一定間隔で)呼ぶ。タイムアウトした Client を検出する
        // (偽装: ハッシュを送らない/遅延させるクライアントへの対処、[14] §7 実装メモ)。
        public void Tick(double networkTimeNow)
        {
            if (!_netBridge.IsServer || _pendingHostSideDeadlines.Count == 0)
            {
                return;
            }

            _expiredScratch.Clear();
            foreach (var kv in _pendingHostSideDeadlines)
            {
                if (networkTimeNow >= kv.Value)
                {
                    _expiredScratch.Add(kv.Key);
                }
            }

            for (var i = 0; i < _expiredScratch.Count; i++)
            {
                var clientId = _expiredScratch[i];
                _pendingHostSideDeadlines.Remove(clientId);
                ApplyOutcome(clientId, new[] { "(タイムアウト: ContentHash が届きませんでした)" }, "ContentHash 未受信");
                // タイムアウトは Client からの応答が無いケースなので、警告継続でも追加のメッセージは送らない
                // (Client 側はそもそも自分のハッシュが届いていないことを知る手段が無く、送っても意味が薄い。
                // Host のログ + NetDebugOverlay だけで十分と判断した)。
            }
        }

        private CatalogContentHashPolicy.Outcome ApplyOutcome(ulong clientId, string[] descriptions, string reasonPrefix)
        {
            var detail = string.Join(", ", descriptions);
            var outcome = CatalogContentHashPolicy.Decide(_isDevelopmentOrEditor);

            switch (outcome)
            {
                case CatalogContentHashPolicy.Outcome.WarnAndContinue:
                    LastStatusText = $"{reasonPrefix}(Client {clientId}): {detail}";
                    Debug.LogWarning($"[Net/Host] CatalogContentHashGate: {LastStatusText} — 開発ビルド/エディタのため接続は継続します。");
                    break;

                case CatalogContentHashPolicy.Outcome.Disconnect:
                    LastStatusText = $"{reasonPrefix}(Client {clientId}): 切断しました";
                    Debug.LogError($"[Net/Host] CatalogContentHashGate: {reasonPrefix}(Client {clientId}): {detail} — リリースビルドのため切断します。");
                    _netBridge.DisconnectClient(clientId, "カタログの ContentHash が一致しません(GameData のバージョンが異なります)。");
                    ClientDisconnectedForMismatch?.Invoke(clientId, detail);
                    break;
            }

            return outcome;
        }
    }
}
