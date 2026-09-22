// [42_distribution.md] §2.3-9 / §7 A-7(P-4、2026-09-20) — NGO(com.unity.netcode.gameobjects)を
// versionDefines(DDRIVE_NGO)で切り離す。ファイル全体が NGO 依存なので、NGO 未導入の持ち込み先
// （DDRIVE_NGO 未定義）ではファイル全体をコンパイル対象外にする（LocalLoopbackBridge だけでコンパイル・動作する）。
#if DDRIVE_NGO
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Net;
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // INetBridge の Netcode for GameObjects(2.13.2) 実装。シーンに 1 つ NetworkObject として配置する
    // (NetworkManager と同じシーン。MS2026 側では Host が生成する NetworkPrefab に含めてもよい)。
    // LocalLoopbackBridge と差し替えるだけでゲームコード・データ側は無改修で動く(FR-13.1)。
    //
    // MS2026 移植方針([14_networking.md] §12, 2026-09-08 統一):
    //   - 接続モデルは Host(+Client) / Client の 1v1。D-Drive の「Server」= MS2026 の「Host」。
    //   - 権威は Host。Broadcast/SendTo は Host から呼ぶのが正規経路。
    //   - Client から Cosmetic を出したい場合は Server 宛 RPC で Host に「依頼」し、Host が検証(レート制限)の上で
    //     全員へ配る(MS2026 ルール「入力はクライアントが送る / 表示は両者が受け取って描く」)。
    //   - 配送は NGO 2.x の統一 RPC(`[Rpc(Unity.Netcode.SendTo.*)]`)を使う。旧 `[ClientRpc]` は 2.x では SendTo.NotServer 扱いで
    //     **ホスト自身に届かない**ため、ホストの Manager が Cosmetic を再生できない(2026-09-08 修正)。
    //   - NetChannel.Unreliable は RpcDelivery.Unreliable に対応させる(欠落許容の演出イベント)。
    //     ただし NGO の Unreliable は 1 パケット(MTU)制限があるため、大きいペイロードは Reliable にフォールバックする。
    //   - ログは "[Net/Host]" / "[Net/Client]" プレフィックス(MS2026 Networking.md §5)。
    public sealed class NgoNetBridge : NetworkBehaviour, INetBridge
    {
        // Unreliable RPC で安全に送れるペイロードの目安(bytes)。NGO の Unreliable は 1 パケットに収まる必要がある。
        public const int UnreliablePayloadLimit = 1000;

        // Client からの Cosmetic 依頼のレート制限(1 クライアントあたり / 秒)。超過分は破棄して警告。
        public const int ClientRelayLimitPerSecond = 60;

        private sealed class Subscription : IDisposable
        {
            private readonly Action _onDispose;
            public Subscription(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }

        private struct RelayBudget
        {
            public double WindowStart;
            public int Count;
        }

        private readonly Dictionary<string, Type> _keyToType = new();
        private readonly Dictionary<string, List<Delegate>> _handlers = new();
        private readonly Dictionary<ulong, RelayBudget> _relayBudgets = new();

        public double NetworkTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

        // [14_networking.md] §2/§12(6-0) — NetworkManager.LocalClientId を橋渡しする。HandleNetKey の発行者
        // 埋め込み・検証([14] §9)に使う。未接続時は 0(ServerClientId と同じ扱い)。
        public ulong LocalClientId => NetworkManager != null ? NetworkManager.LocalClientId : 0UL;

        // [14_networking.md] §5(5-9) — Late Join のアクティブ演出スナップショット送信に使う新規接続通知。
        // NGO の OnClientConnectedCallback は Host/Client 双方で発火する(自分自身の接続も含む)ため、
        // 実際に「Host として送るかどうか」の判定は購読側(PresentationManager)が IsServer を見て行う。
        public event Action<ulong> ClientConnected;

        // [11_tasks.md] 6-0(B) — NetDebugOverlay 用の受信メッセージ数。
        public int ReceivedMessageCount { get; private set; }

        // [14_networking.md] §16(N-3、2026-09-22) — Host 1 + Client 3(MS2026 の 4 人対戦)の接続確認用。
        // Server(Host)のときだけ NetworkManager.ConnectedClientsIds.Count を返す(NGO は StartHost() 時に
        // Host 自身の LocalClientId も ConnectedClientsIds に含めるため、この数には Host 自身が含まれる。
        // つまり Host 1 + Client 3 が全員繋がった状態では 4 になる)。Client では常に 0(自分から見た
        // 他クライアントの一覧は NGO のセキュリティ上取得できないため)。NetworkManager 自体が無い/未接続
        // (IsSpawned 前)は 0。
        public int ConnectedClientCount => IsServer && NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;

        // [11_tasks.md] 6-0 修正5(オーケストレーター追加指示、実機確認で発見) — 切断通知。
        // (clientId, reason)。Host 視点は「どの Client が切断したか」、Client 視点は「自分(=Host との接続)が
        // 切れた」ことを表す(切断時の clientId は NGO の実装上 Client 自身の LocalClientId になる)。
        public event Action<ulong, string> ClientDisconnected;

        // [11_tasks.md] 6-0 修正6(オーケストレーター追加指示、実機確認 v2 の切断確認で発見) — Client 自身が
        // Host との接続を失った(切断された)ことを表す(既定 true。Host は常に true のまま)。
        // NetCheckRunner/NetDebugOverlay の「接続中/切断」表示・rtt_app_ms のリセット判定に使う。
        public bool IsConnected { get; private set; } = true;

        // [11_tasks.md] 6-0 修正1 — UnityTransport.SetDebugSimulatorParameters は Obsolete化されており
        // 実際には何の効果も持たない(NetgoTransportConfigurator.cs 冒頭コメント参照)。D-Drive 側の
        // アプリ層で送信/受信キューに遅延を入れて代替する。0 = 無効(既定。既存挙動を変えない)。
        // 開発ビルドのみ有効(ConfigureAppLayerSimLatency 側で強制する)。
        private int _appLayerSimLatencyMs;

        // [11_tasks.md] 6-6(K3 修正、実機確認 v4 §12 で発見) — アプリ層遅延キューの実体。以前は
        // メッセージ 1 件につき独立した `UniTask.Delay(...).Forget()` を個別に発火していたため、
        // ほぼ同時に複数メッセージが積まれた場合に「実際に送信/配送される順序」が実装上保証されなかった
        // (UniTask の PlayerLoopTimer が同一フレームで満了した複数の Delay をどの順で再開するかは
        // 未規定)。実機確認 v4(200ms 遅延・ホットスポットが数秒止まってまとめて届いた区間)で
        // `PresentationSignalMsg` が対応する `PresentationPlayMsg` より先に処理され「未知のキー」として
        // 破棄される実バグとして観測された(docs/29 §12、K3)。`Queue<T>` による本物の FIFO に置き換え、
        // 先頭から「解放予定時刻(ReleaseAtTime)を過ぎたものだけ」取り出す(遅延幅は 3 キュー共通の
        // 固定値のため、先頭が未到達ならそれより後ろの要素も必ず未到達 = 早期終了できる)ことで、
        // 同一キュー内のメッセージは必ず送信/受信した順に処理されるようにした。Update() で毎フレーム
        // 排出する(`_appLayerSimLatencyMs<=0` の既定時は即 return、0 alloc・0 cost)。
        private struct AppLayerQueueEntry
        {
            public string Key;
            public string Json;
            public ulong SenderOrOriginId;
            public ulong TargetClientId; // _delayedSendToQueue のみ使用
            public NetChannel Channel;
            public float ReleaseAtTime; // Time.time 基準(元の UniTask.Delay の既定挙動=スケール済み時間に揃える)
        }

        private readonly Queue<AppLayerQueueEntry> _delayedSendToAllQueue = new();
        private readonly Queue<AppLayerQueueEntry> _delayedSendToQueue = new();
        private readonly Queue<AppLayerQueueEntry> _delayedDispatchQueue = new();

        // [11_tasks.md] 6-0 修正1 — トランスポートの RTT(NetworkTransport.GetCurrentRtt)はシミュレーター
        // 遅延を反映しないため、Client→Host→Client の Ping/Pong 往復で計測した「アプリ層の RTT」を別途持つ
        // (null = まだ計測できていない。Host 自身は計測しない=常に null)。
        //
        // [11_tasks.md] 6-6(K2 修正、実機確認 v4 §12 で発見。2026-09-18 v5 実機確認 §16.2 で状態遷移の
        // 実バグが見つかり再修正) — 通信停止中に最後の実測値のまま固着させず経過時間を下限として返す
        // 仕組みと、それが「通信途絶の疑い」であることを示す stale フラグ。状態遷移ロジックそのものは
        // `AppRoundTripTracker`(Unity API 非依存、EditMode テスト済み)に切り出してある。再修正の経緯・
        // 旧実装の実バグの詳細は AppRoundTripTracker.cs 冒頭のコメント参照。
        private readonly AppRoundTripTracker _appRoundTripTracker = new();

        public double? AppRoundTripMs => _appRoundTripTracker.GetRoundTripMs(Time.unscaledTimeAsDouble);

        public bool IsAppRoundTripMsStale => _appRoundTripTracker.IsStale;

        private CancellationTokenSource _pingLoopCts;

        // [44_review_2026-09-19.md] P2-1 — 偽造 NetPongMsg を検出したことの警告を、開発ビルドで 1 回だけ出す
        // (PresentationManager.WarnUnknownKeyDiscardedOnce と同じ考え方)。
        private bool _warnedForgedPong;

        private string LogTag => IsServer ? "[Net/Host]" : "[Net/Client]";

        public override void OnNetworkSpawn()
        {
            // 6-6(K3 修正) — Spawn ごとにキューを空にする(既に切断で Clear 済みの古い残留物を引き継がない)。
            _delayedSendToAllQueue.Clear();
            _delayedSendToQueue.Clear();
            _delayedDispatchQueue.Clear();
            IsConnected = true;
            _warnedForgedPong = false;

            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
                NetworkManager.OnTransportFailure += HandleTransportFailure;
            }

            // Ping/Pong は通常の Subscribe 経路(Dispatch)に相乗りする(NgoNetBridge 自身が
            // 自分の Broadcast/SendTo を購読する。Client→Host は既存の中継経路を、Host→Client は
            // SendTo をそのまま使う。既存の型登録([_keyToType])に乗るため特別扱いは不要)。
            Subscribe<NetPingMsg>(OnPingMsgReceived);
            Subscribe<NetPongMsg>(OnPongMsgReceived);

            if (IsClient && !IsServer)
            {
                _pingLoopCts = new CancellationTokenSource();
                PingLoopAsync(_pingLoopCts.Token).Forget();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.OnTransportFailure -= HandleTransportFailure;
            }

            _pingLoopCts?.Cancel();
            _pingLoopCts?.Dispose();
            _pingLoopCts = null;

            // 6-0 修正6/6-6(K3 修正) — オブジェクト自体の Despawn(シーン破棄等)でもキューを空にする。
            _delayedSendToAllQueue.Clear();
            _delayedSendToQueue.Clear();
            _delayedDispatchQueue.Clear();
        }

        // [11_tasks.md] 6-6(K3 修正) — アプリ層遅延キューの排出。既定(`_appLayerSimLatencyMs<=0`、
        // 開発ビルド以外や `-ddrive-sim-latency` 未指定)では 3 キューとも常に空のままなので、
        // 各 while の `Count > 0` 判定だけで即座に抜ける(0 alloc・実質 0 cost。`_appLayerSimLatencyMs` 自体を
        // 早期リターン条件にしないのは、キューに積んだ後で値が変わっても残留メッセージを取りこぼさないため)。
        // 3 キューとも同じ遅延幅を使うため、先頭(最も古い要素)が未到達ならそれより後ろも必ず未到達
        // ─ 早期 break してよい ─ という前提で FIFO を保ったまま排出する。
        private void Update()
        {
            var now = Time.time;

            while (_delayedSendToAllQueue.Count > 0 && _delayedSendToAllQueue.Peek().ReleaseAtTime <= now)
            {
                var entry = _delayedSendToAllQueue.Dequeue();
                SendToAllImmediate(entry.Key, entry.Json, entry.Channel, entry.SenderOrOriginId);
            }

            while (_delayedSendToQueue.Count > 0 && _delayedSendToQueue.Peek().ReleaseAtTime <= now)
            {
                var entry = _delayedSendToQueue.Dequeue();
                SendToImmediate(entry.Key, entry.Json, entry.TargetClientId, entry.Channel, entry.SenderOrOriginId);
            }

            while (_delayedDispatchQueue.Count > 0 && _delayedDispatchQueue.Peek().ReleaseAtTime <= now)
            {
                var entry = _delayedDispatchQueue.Dequeue();
                DispatchImmediate(entry.Key, entry.Json, entry.SenderOrOriginId);
            }
        }

        private void HandleClientConnected(ulong clientId) => ClientConnected?.Invoke(clientId);

        // [11_tasks.md] 6-0 修正5 — 実機確認(PC-B)で「Host を止めても Client が切断を一切ログに出さない」
        // ことが発見された。NetworkManager.OnClientDisconnectCallback/OnTransportFailure を購読して
        // ログに残す(自動再接続は MS2026 の規約に無いため実装しない。要判断: 将来必要になったら追加)。
        private void HandleClientDisconnected(ulong clientId)
        {
            var reason = NetworkManager != null ? NetworkManager.DisconnectReason : null;
            var reasonText = string.IsNullOrEmpty(reason) ? "unknown" : reason;

            if (IsServer)
            {
                Debug.Log($"[Net/Host] NgoNetBridge: Client {clientId} が切断しました(reason={reasonText})。");
            }
            else
            {
                Debug.Log($"[Net/Client] NgoNetBridge: Host から切断されました(reason={reasonText})。");

                // 6-0 修正6/6-6(K3 修正、オーケストレーター追加指示) — 自分(Client)が Host との接続を
                // 失った場合だけ、(1)最後の値を表示し続けないよう App RTT をリセットし、(2)アプリ層遅延
                // キューに残っている送受信を破棄する(§ 上のコメント参照。Host 視点でどれか 1 Client が
                // 抜けたケースは、MS2026 の 1v1 前提では他に対象が居ないため対象外)。
                IsConnected = false;
                // K2 修正 — 切断後は「経過時間による下限推定」も出さない(n/a に戻す)。Ping ループ自体は
                // OnNetworkDespawn 側で止まる(_pingLoopCts.Cancel())が、そのタイミングより前にここへ
                // 来ることがあるため明示的にリセットする(2026-09-18 再修正: AppRoundTripTracker.Reset()
                // に委譲。次回接続時にゼロから測り直せる)。
                _appRoundTripTracker.Reset();
                _delayedSendToAllQueue.Clear();
                _delayedSendToQueue.Clear();
                _delayedDispatchQueue.Clear();
            }

            ClientDisconnected?.Invoke(clientId, reasonText);
        }

        private void HandleTransportFailure()
        {
            Debug.LogWarning($"{LogTag} NgoNetBridge: トランスポート層で失敗が発生しました(NetworkManager.OnTransportFailure)。");
        }

        // [11_tasks.md] 6-0 修正1(B) — 開発ビルド + 明示指定時だけ有効にする。Debug.isDebugBuild は
        // Editor 実行時、または「Development Build」を付けたプレイヤーで true になる([CLAUDE.md] 例外で
        // 止めない: 未指定(0 以下)なら常に無効で既存挙動を変えない)。
        public void ConfigureAppLayerSimLatency(int simLatencyMs)
        {
            _appLayerSimLatencyMs = Debug.isDebugBuild && simLatencyMs > 0 ? simLatencyMs : 0;
            if (_appLayerSimLatencyMs > 0)
            {
                Debug.Log($"{LogTag} NgoNetBridge: UnityTransport のシミュレーターは無効化されている(SetDebugSimulatorParameters が Obsolete/no-op)ため、アプリ層の送受信キューで遅延({_appLayerSimLatencyMs}ms)を代替します。");
            }
        }

        // [11_tasks.md] 6-0 修正1 — Client のみ、1 秒おきに Host へ Ping を送り Pong の往復時間を測る。
        private async UniTaskVoid PingLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1d), cancellationToken: ct).SuppressCancellationThrow();
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                Broadcast(new NetPingMsg { SentAtNetworkTime = NetworkTime }, NetChannel.Unreliable);

                // K2 修正(2026-09-18 再修正) — 未達カウント・経過時間の基準点更新は
                // AppRoundTripTracker.OnPingSent に委譲する(前回の Pong が未受信なら 1 回分の未達として
                // カウントする。Pong が届いていれば OnPongReceived で既に 0 にリセットされているため
                // 加算しない)。
                _appRoundTripTracker.OnPingSent(Time.unscaledTimeAsDouble);
            }
        }

        private void OnPingMsgReceived(ulong senderId, NetPingMsg msg)
        {
            // Host だけが Pong を返す(Client 同士は直接通信できないため。[14_networking.md] §2)。
            if (!IsServer)
            {
                return;
            }

            SendTo(senderId, new NetPongMsg { OriginalSentAtNetworkTime = msg.SentAtNetworkTime }, NetChannel.Unreliable);
        }

        private void OnPongMsgReceived(ulong senderId, NetPongMsg msg)
        {
            // [44_review_2026-09-19.md] P2-1 — 送信元検証(CatalogContentHashGate.OnReceiveResultMsg と同じ形)。
            // NetPongMsg は OnNetworkSpawn で Subscribe されるため _keyToType に登録済みになり、
            // RequestBroadcastRpc(Client→Host の中継依頼)は「型登録済みなら」中継してしまう。改造 Client が
            // Broadcast(new NetPongMsg{...}) すると、Host を含む全ピアがそれを受信する。
            // Host は PingLoopAsync を自分では起動しない(!IsServer 限定)ため、正当な Pong の宛先には
            // 絶対にならない = Host が受信する Pong は常に不正(明示的に弾く)。
            if (IsServer)
            {
                WarnForgedPongOnce(senderId);
                return;
            }

            // Client にとっての正当な送信元は「Ping を送った相手 = Host(NetworkManager.ServerClientId、
            // 常に 0)」だけ。実際の一致判定と状態更新は AppRoundTripTracker(EditMode テスト対象)に委ねる。
            var trustedSenderId = NetworkManager != null ? NetworkManager.ServerClientId : 0UL;
            if (senderId != trustedSenderId)
            {
                WarnForgedPongOnce(senderId);
                return;
            }

            var measuredMs = Math.Max(0d, (NetworkTime - msg.OriginalSentAtNetworkTime) * 1000d);
            // K2 修正(2026-09-18 再修正) — Pong が返った=もう「経過時間による下限推定」ではない。
            // AppRoundTripTracker.OnPongReceived が実測値の反映・未達カウントのリセット・応答待ち
            // 起点のクリアをまとめて行う。
            _appRoundTripTracker.OnPongReceived(measuredMs, senderId, trustedSenderId);
        }

        // 6-0 修正4 の WarnUnknownKeyDiscardedOnce と同じ考え方(開発ビルドのみ、1 回だけ出す)。
        private void WarnForgedPongOnce(ulong senderId)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_warnedForgedPong)
            {
                return;
            }

            _warnedForgedPong = true;
            Debug.LogWarning($"{LogTag} NgoNetBridge: 送信元 ClientId({senderId}) が正当な Pong の送信元と一致しないため破棄しました。");
#endif
        }

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage
        {
            var key = KeyOf<T>();
            var json = JsonUtility.ToJson(msg);

            if (IsServer)
            {
                // Host 自身が行為者。発行者は Host の LocalClientId(P1-2 対応: 6-0 まで全員に常に
                // ServerClientId として配送されていたため、Client 行為者の HandleNetKey 検証が機能しなかった)。
                SendToAll(key, json, channel, LocalClientId);
                return;
            }

            if (!IsClient || !IsSpawned)
            {
                Debug.LogWarning($"{LogTag} NgoNetBridge.Broadcast: 未接続のため送信できません({key})。");
                return;
            }

            // Client 発: Host へ依頼し、Host が検証して全員へ配る(自分も Host からの RPC で受信して再生する)。
            RequestBroadcastRpc(key, json, channel == NetChannel.Unreliable);
        }

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
        {
            if (!IsServer)
            {
                Debug.LogWarning($"{LogTag} NgoNetBridge.SendTo は Host からのみ呼べます(Client→特定 Client の直接送信は権威モデル上許可しない)。");
                return;
            }

            var json = JsonUtility.ToJson(msg);
            var key = KeyOf<T>();
            var originClientId = LocalClientId; // 直接送信は常に Host が発行者

            if (_appLayerSimLatencyMs > 0)
            {
                DelayedSendTo(key, json, clientId, channel, originClientId);
                return;
            }

            SendToImmediate(key, json, clientId, channel, originClientId);
        }

        // [11_tasks.md] 6-6(K3 修正) — 送信キュー側の遅延。RpcTarget(RpcTargetUse.Temp)は実際の送信時
        // (Update() からの SendToImmediate 呼び出し)に作り直す(Enqueue 時点で作って保持すると Temp な
        // 内部リソースが先に解放される可能性があるため)。切断時は OnNetworkDespawn/HandleClientDisconnected
        // がキューそのものを Clear() するため、ここでは何もチェックしない(残っていれば = まだ有効)。
        private void DelayedSendTo(string key, string json, ulong clientId, NetChannel channel, ulong originClientId)
        {
            _delayedSendToQueue.Enqueue(new AppLayerQueueEntry
            {
                Key = key,
                Json = json,
                TargetClientId = clientId,
                Channel = channel,
                SenderOrOriginId = originClientId,
                ReleaseAtTime = Time.time + _appLayerSimLatencyMs / 1000f,
            });
        }

        private void SendToImmediate(string key, string json, ulong clientId, NetChannel channel, ulong originClientId)
        {
            var target = RpcTarget.Single(clientId, RpcTargetUse.Temp);
            if (channel == NetChannel.Unreliable && FitsUnreliable(json))
            {
                ReceiveUnreliableToRpc(key, json, originClientId, target);
            }
            else
            {
                ReceiveToRpc(key, json, originClientId, target);
            }
        }

        public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage
        {
            var key = KeyOf<T>();
            _keyToType[key] = typeof(T);

            if (!_handlers.TryGetValue(key, out var list))
            {
                list = new List<Delegate>();
                _handlers[key] = list;
            }

            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public Transform ResolveNetObject(ulong netId)
        {
            if (NetworkManager != null && NetworkManager.SpawnManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj))
            {
                return netObj.transform;
            }

            return null;
        }

        // [14_networking.md] §4/§5(6-0) — ResolveNetObject の逆方向。渡された Transform(またはその親)に
        // NetworkObject が付いていて Spawn 済みなら NetworkObjectId を返す。それ以外は 0(呼び出し元は
        // 既存のとおり Position 等にフォールバックする)。
        public ulong ResolveNetId(Transform transform)
        {
            if (transform == null)
            {
                return 0UL;
            }

            var netObj = transform.GetComponentInParent<NetworkObject>();
            return netObj != null && netObj.IsSpawned ? netObj.NetworkObjectId : 0UL;
        }

        // [14_networking.md] §5 追加指示(2026-09-14, 6-0) — HapticsData.LocalPlayerOnly の誤爆防止に使う。
        // NetworkObject の所有者(OwnerClientId)がローカルクライアントと一致するかどうかを見る。
        public bool IsLocalPlayerObject(Transform transform)
        {
            if (transform == null)
            {
                return false;
            }

            var netObj = transform.GetComponentInParent<NetworkObject>();
            return netObj != null && netObj.IsSpawned && netObj.OwnerClientId == LocalClientId;
        }

        // [14_networking.md] §3/§10(6-0) — NetMode.Simulated な Prefab の Host 権威生成。root は既に
        // ローカルへ Instantiate 済み(PrefabsManager.SpawnData の Pool.Rent 結果)であることを前提にする
        // (NetworkObject を Spawn するだけで、生成そのものは既存の Pool 経路に任せる)。
        public ulong SpawnNetworked(GameObject root)
        {
            if (!IsServer || root == null)
            {
                return 0UL;
            }

            var netObj = root.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                // Validator(PrefabDataValidator)が Error として検出する組み合わせ([14] §10)。
                // ランタイムは例外で止めず、NetObjectId=0 のローカル専用インスタンスとして継続する。
                Debug.LogWarning($"{LogTag} NgoNetBridge.SpawnNetworked: '{root.name}' に NetworkObject が無いため NetworkObjectId を発行できません。");
                return 0UL;
            }

            if (!netObj.IsSpawned)
            {
                netObj.Spawn();
            }

            return netObj.NetworkObjectId;
        }

        public void DespawnNetworked(ulong netId, bool destroy)
        {
            if (!IsServer || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj) && netObj.IsSpawned)
            {
                netObj.Despawn(destroy);
            }
        }

        // [14_networking.md] §7(6-5) — ContentHash 不一致(リリースビルド)時に Host が該当 Client を
        // 切断する。NetworkManager.DisconnectClient は Client 側に DisconnectReasonMessage を送ってから
        // 切断するため、Client の NetworkManager.DisconnectReason に reason がそのまま届く
        // (既存の HandleClientDisconnected と同じ仕組み)。
        public void DisconnectClient(ulong clientId, string reason)
        {
            if (!IsServer || NetworkManager == null)
            {
                Debug.LogWarning($"{LogTag} NgoNetBridge.DisconnectClient は Host からのみ呼べます(Client からの呼び出しは無視しました)。");
                return;
            }

            NetworkManager.DisconnectClient(clientId, reason);
        }

        // ── 送信(Host 側) ──

        private void SendToAll(string key, string json, NetChannel channel, ulong originClientId)
        {
            if (_appLayerSimLatencyMs > 0)
            {
                DelayedSendToAll(key, json, channel, originClientId);
                return;
            }

            SendToAllImmediate(key, json, channel, originClientId);
        }

        // [11_tasks.md] 6-6(K3 修正) — 送信キュー側の遅延(ConfigureAppLayerSimLatency 参照)。
        // DelayedSendTo と同じく、切断時はキュー自体が Clear() されるためここでの追加チェックは不要。
        private void DelayedSendToAll(string key, string json, NetChannel channel, ulong originClientId)
        {
            _delayedSendToAllQueue.Enqueue(new AppLayerQueueEntry
            {
                Key = key,
                Json = json,
                Channel = channel,
                SenderOrOriginId = originClientId,
                ReleaseAtTime = Time.time + _appLayerSimLatencyMs / 1000f,
            });
        }

        private void SendToAllImmediate(string key, string json, NetChannel channel, ulong originClientId)
        {
            if (channel == NetChannel.Unreliable && FitsUnreliable(json))
            {
                ReceiveUnreliableRpc(key, json, originClientId);
            }
            else
            {
                ReceiveRpc(key, json, originClientId);
            }
        }

        private static bool FitsUnreliable(string json) => Encoding.UTF8.GetByteCount(json) <= UnreliablePayloadLimit;

        // ── RPC(Host → 全員。ホスト自身も含む) ──
    // 注: enum Unity.Netcode.SendTo は本クラスの SendTo<T>() メソッドと名前が衝突するため完全修飾する。
        // originClientId: 本来の発行者(Host 自身、または Client→Host 依頼の送信元)。P1-2 対応(6-0):
        // 以前は Dispatch が常に NetworkManager.ServerClientId を使っていたため、中継された Client 発の
        // メッセージが全ピアで「Host から来た」ものとして扱われ、HandleNetKey の発行者検証が機能しなかった。

        [Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void ReceiveRpc(string typeKey, string json, ulong originClientId)
        {
            Dispatch(typeKey, json, originClientId);
        }

        [Rpc(Unity.Netcode.SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
        private void ReceiveUnreliableRpc(string typeKey, string json, ulong originClientId)
        {
            Dispatch(typeKey, json, originClientId);
        }

        // ── RPC(Host → 特定クライアント) ──

        [Rpc(Unity.Netcode.SendTo.SpecifiedInParams)]
        private void ReceiveToRpc(string typeKey, string json, ulong originClientId, RpcParams rpcParams)
        {
            Dispatch(typeKey, json, originClientId);
        }

        [Rpc(Unity.Netcode.SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        private void ReceiveUnreliableToRpc(string typeKey, string json, ulong originClientId, RpcParams rpcParams)
        {
            Dispatch(typeKey, json, originClientId);
        }

        // ── RPC(Client → Host の依頼) ──
        // Host は発信者ごとのレート制限を掛けてから中継する([14_networking.md] §9: クライアント発の中継は無条件に行わない)。
        // Simulated な生成はこの経路を通さない(Prefab の Simulated Spawn は Phase 4 で Host 権威の専用 API を用意する)。
        [Rpc(Unity.Netcode.SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastRpc(string typeKey, string json, bool unreliable, RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            if (!_keyToType.ContainsKey(typeKey))
            {
                Debug.LogWarning($"{LogTag} 未登録のメッセージ種別 '{typeKey}' の中継依頼を Client {sender} から受信したため破棄しました。");
                return;
            }

            if (!ConsumeRelayBudget(sender))
            {
                Debug.LogWarning($"{LogTag} Client {sender} からの中継依頼がレート制限({ClientRelayLimitPerSecond}/秒)を超えたため破棄しました({typeKey})。");
                return;
            }

            // 真の発行者(sender)を全ピアへ伝える。以後の Presentation 側の発行者検証([14] §9、6-0)が
            // これを使って偽造 Signal/Cancel/Play を破棄できるようにする。
            SendToAll(typeKey, json, unreliable ? NetChannel.Unreliable : NetChannel.ReliableOrdered, sender);
        }

        private bool ConsumeRelayBudget(ulong clientId)
        {
            var now = NetworkTime;
            _relayBudgets.TryGetValue(clientId, out var budget);

            if (now - budget.WindowStart >= 1d)
            {
                budget.WindowStart = now;
                budget.Count = 0;
            }

            budget.Count++;
            _relayBudgets[clientId] = budget;
            return budget.Count <= ClientRelayLimitPerSecond;
        }

        // ── 受信 ──

        private void Dispatch(string key, string json, ulong senderId)
        {
            if (_appLayerSimLatencyMs > 0)
            {
                DelayedDispatch(key, json, senderId);
                return;
            }

            DispatchImmediate(key, json, senderId);
        }

        // [11_tasks.md] 6-6(K3 修正) — 受信キュー側の遅延。ReceivedMessageCount は「実際に処理した時点」で
        // 増やす(NetDebugOverlay の受信レート表示が遅延込みの実感と一致するようにする、変更なし)。
        // 6-0 修正6/6-6 — 切断時はキュー自体が Clear() されるため、古い PlayMsg が NetworkTime=0 に
        // 巻き戻った状態で処理される実バグ(修正済み)は再発しない。同じキューを共有する他メッセージより
        // 先に処理されることも無い(Update() の FIFO 排出。K3 の直接の修正)。
        private void DelayedDispatch(string key, string json, ulong senderId)
        {
            _delayedDispatchQueue.Enqueue(new AppLayerQueueEntry
            {
                Key = key,
                Json = json,
                SenderOrOriginId = senderId,
                ReleaseAtTime = Time.time + _appLayerSimLatencyMs / 1000f,
            });
        }

        private void DispatchImmediate(string key, string json, ulong senderId)
        {
            ReceivedMessageCount++;
            if (!_keyToType.TryGetValue(key, out var type) || !_handlers.TryGetValue(key, out var list))
            {
                return;
            }

            var msg = JsonUtility.FromJson(json, type);

            foreach (var d in list)
            {
                d.DynamicInvoke(senderId, msg);
            }
        }

        private static string KeyOf<T>() => typeof(T).FullName;
    }
}
#endif // DDRIVE_NGO
