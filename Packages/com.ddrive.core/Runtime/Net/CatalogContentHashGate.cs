using System;
using System.Collections.Generic;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using DDrive.Runtime;
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
        // NGO では Host(Server)の ClientId は常に 0(NetworkManager.ServerClientId と同じ)。
        // PresentationManager.TrustedRelayClientId と同じ考え方の定数([14_networking.md] §9)。
        private const ulong HostClientId = 0UL;

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

        // [42_distribution.md] §5.6/§6 P-8(2026-09-20) — NetDebugOverlay 表示用(自分の版は常に分かる)。
        public string LocalPackageVersion => DDriveVersion.Value;

        // Host 側は受信した CatalogContentHashMsg から相手の版を都度更新する(表示専用)。Client 側は
        // 現状の設計では Host の版を受け取る手段が無い(CatalogContentHashResultMsg にフィールドを
        // 追加していない。本チケットが追加するのは CatalogContentHashMsg のみ)ため空のまま。
        public string LastKnownRemotePackageVersion { get; private set; } = string.Empty;

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
            _netBridge.ClientDisconnected += OnClientDisconnected;
        }

        public void Dispose()
        {
            _subHash?.Dispose();
            _subResult?.Dispose();
            _netBridge.ClientConnected -= OnClientConnected;
            _netBridge.ClientDisconnected -= OnClientDisconnected;
        }

        // [14_networking.md] §18(N-5、2026-09-24、D-1) — Host 引き継ぎ(同一プロセスで
        // StopNetworking() → 別ロールで再 Start)向け。接続セッションに紐づく状態(Client 側の送信済み
        // フラグ・Host 側の保留期限/結果辞書・LastStatusText)だけを初期状態へ戻す。ローカルのカタログ内容
        // (_localCombinedHash/_localCatalogs/_registryReady)は再計算不要なので保持する(再接続のたびに
        // SetLocalSummary を呼び直す必要が無い)。
        //
        // 修正前は _clientHashSent/_clientConnectedFired が一度立つと戻らないため、新しい Host に
        // 再接続した Client がハッシュを再送せず、リリースビルドでは ContentHashTimeoutSeconds 後に
        // 切断されていた(D-1)。呼び出し元: DDriveRuntimeBootstrap.StopNetworking() /
        // Client 視点の切断時(OnNetClientDisconnected)。
        public void Reset()
        {
            _clientHashSent = false;
            _clientConnectedFired = false;
            _pendingHostSideDeadlines.Clear();
            _pendingBeforeReady.Clear();
            LastStatusText = "検証中...";
            LastKnownRemotePackageVersion = string.Empty;
        }

        // DDriveRuntimeBootstrap.RegisterCatalogsAsync() が IsReady=true にした直後に呼ぶ(自分の
        // カタログ内容が確定した時点。[14] §7 実装メモ参照)。
        public void SetLocalSummary(ulong combinedHash, CatalogContentHasher.CatalogHashEntry[] catalogs)
        {
            _localCombinedHash = combinedHash;
            _localCatalogs = catalogs ?? Array.Empty<CatalogContentHasher.CatalogHashEntry>();
            _registryReady = true;

            FlushPendingBeforeReady();

            // 2026-09-17 レビュー対応(P2-3) — Host のカタログ登録(LoadMode.Preload 込みの
            // RegisterCatalogAsync)が _timeoutSeconds より長くかかる環境では、先に接続して正しく
            // ハッシュを送った正規の Client まで「ContentHash 未受信」扱いで切断されていた
            // (Tick は保留の有無を見ずに ClientConnected 時点からの期限だけで判定していた)。
            // 期限は「Host が判定できるようになった時点」から数え直す。
            RebasePendingHostSideDeadlines();

            TrySendOwnHash();
        }

        // 未 ready 中に接続していた Client の保留期限を「今」から数え直す(P2-3)。
        private void RebasePendingHostSideDeadlines()
        {
            if (!_netBridge.IsServer || _pendingHostSideDeadlines.Count == 0)
            {
                return;
            }

            var deadline = _netBridge.NetworkTime + _timeoutSeconds;

            // Dictionary は列挙中に値を書き換えられないため、キーを一旦退避する(scratch を使い回して alloc しない)。
            _expiredScratch.Clear();
            foreach (var kv in _pendingHostSideDeadlines)
            {
                _expiredScratch.Add(kv.Key);
            }

            for (var i = 0; i < _expiredScratch.Count; i++)
            {
                _pendingHostSideDeadlines[_expiredScratch[i]] = deadline;
            }

            _expiredScratch.Clear();
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

        // 2026-09-17 レビュー対応(P2-2) — 接続後 _timeoutSeconds 以内(ハッシュ送信前)に切断/クラッシュした
        // Client の保留期限を掃除する。これが無いと、居ない Client に対して Host がタイムアウト扱いの
        // Debug.LogError + DisconnectClient + ClientDisconnectedForMismatch を発火し、LastStatusText
        // (NetDebugOverlay / NetCheck の content_hash)も誤った内容に上書きされていた。
        private void OnClientDisconnected(ulong clientId, string reason)
        {
            if (!_netBridge.IsServer)
            {
                return; // Client 視点の「自分が切れた」通知は Host 側の保留台帳とは無関係。
            }

            _pendingHostSideDeadlines.Remove(clientId);
            RemovePendingBeforeReady(clientId);
        }

        // 未 ready 中に受信して保留していたハッシュのうち、切断済み Client の分を捨てる
        // (FlushPendingBeforeReady が切断済み Client へ SendTo するのを防ぐ)。
        private void RemovePendingBeforeReady(ulong clientId)
        {
            for (var i = _pendingBeforeReady.Count - 1; i >= 0; i--)
            {
                if (_pendingBeforeReady[i].SenderId == clientId)
                {
                    _pendingBeforeReady.RemoveAt(i);
                }
            }
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
            _netBridge.Broadcast(
                new CatalogContentHashMsg
                {
                    CombinedHash = _localCombinedHash,
                    Catalogs = _localCatalogs,
                    PackageVersion = DDriveVersion.Value,
                    ProtocolVersion = DDriveProtocol.Current,
                },
                NetChannel.ReliableOrdered);
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
            LastKnownRemotePackageVersion = string.IsNullOrEmpty(msg.PackageVersion) ? "unknown" : msg.PackageVersion;

            // [42_distribution.md] §5.6(P-8、2026-09-20) — ContentHash の照合より先に D-Drive の版
            // (ProtocolVersion)を照合する。旧版 Client(この Msg にフィールドが無い版が送ってきた場合、
            // JsonUtility の既定値のまま 0 で届く)も同じ扱いになる。一致すれば従来どおり ContentHash の
            // 照合に進む。
            if (msg.ProtocolVersion != DDriveProtocol.Current)
            {
                var versionDetail = DescribeVersionMismatch(msg);
                var versionOutcome = ApplyOutcome(senderId, new[] { versionDetail }, "D-Drive の版が違います");
                if (versionOutcome == CatalogContentHashPolicy.Outcome.Disconnect)
                {
                    // 切断済みの Client へ追ってメッセージを送る意味は無い(ContentHash 不一致時と同じ扱い)。
                    return;
                }

                _netBridge.SendTo(senderId, new CatalogContentHashResultMsg { Matched = false, Descriptions = new[] { versionDetail } }, NetChannel.ReliableOrdered);
                return;
            }

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

            // 2026-09-18 レビュー対応(41 テストの穴 1: 偽造 Result) — Host(HostClientId=0)以外からの
            // Result は無視する。NgoNetBridge.Broadcast の Client→Host 依頼(RequestBroadcastRpc)は
            // 「型登録済みなら」Client からでも呼び出せてしまい、CatalogContentHashResultMsg もここで
            // Subscribe されているため型登録される。中継時の senderId には真の送信元(攻撃者)が入るため、
            // 従来は senderId を見ずに LastStatusText を上書きしており、改造 Client が本物の Host 判定
            // (不一致警告)を偽の Matched=true で塗り替えられた(PresentationManager.IsAuthorizedSender と
            // 同じ考え方の発行者検証をここにも適用する)。
            if (senderId != HostClientId)
            {
                return;
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
            // 2026-09-17 レビュー対応(P2-3) — Host 自身のカタログ登録が終わるまでは誰も判定できない
            // (受信済みハッシュも _pendingBeforeReady に積まれたまま)。未 ready の間はタイムアウト
            // 判定そのものを行わず、SetLocalSummary で期限を数え直す。
            if (!_netBridge.IsServer || !_registryReady || _pendingHostSideDeadlines.Count == 0)
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

        // [42_distribution.md] §5.6(P-8) — 「D-Drive の版が違う(自: x / 相手: y)」の x/y を組み立てる。
        // PackageVersion は表示専用(照合していない実際の理由=ProtocolVersion を併記して分かりやすくする)。
        private static string DescribeVersionMismatch(CatalogContentHashMsg msg)
        {
            var remoteVersion = string.IsNullOrEmpty(msg.PackageVersion) ? "unknown" : msg.PackageVersion;
            return $"自: {DDriveVersion.Value}(Protocol {DDriveProtocol.Current}) / 相手: {remoteVersion}(Protocol {msg.ProtocolVersion})";
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
