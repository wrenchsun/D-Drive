using Cysharp.Threading.Tasks;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using R3;
using UnityEngine;

namespace DDrive.Samples
{
    // [11_tasks.md] 6-0(D) — 実機 2 台確認用の自動チェック。Host が剣攻撃デモ(PRES_Demo_SkillSlash)を
    // 一定間隔で Play → Signal("hit") し、両端末で位相差・Signal 受信・HitStop 発火をログ出力する。
    // `[Net/Host]`/`[Net/Client]` プレフィックスに加え、`[DDriveNetCheck] key=value ...` 形式の行を出す
    // (docs/29_network_device_test.md の判定基準はこの行を機械的に読む)。
    // Assets/GameData/PreviewScenes/NetCheckScene.unity に置く。NetBridgeSmokeTest の Ping もこのシーンに
    // 同居させる(疎通確認の一次情報源として残す)。
    public sealed class NetCheckRunner : MonoBehaviour
    {
        // PRES_Demo_SkillSlash.asset の Id(PresentationSkillSlashDemo.cs と同じ値)。
        private static readonly AssetId<PresentationMarker> SkillSlashId = new(0xCD2986D134D20E66UL, AssetType.Presentation);

        [Tooltip("ctx.Self。未設定ならこの GameObject の Transform を使う")]
        [SerializeField] private Transform actor;

        [Tooltip("Host がデモを再生する間隔(秒)")]
        [SerializeField] private float playIntervalSeconds = 3f;

        [Tooltip("Play から Signal(\"hit\") までの遅延(秒)")]
        [SerializeField] private float signalDelaySeconds = 0.5f;

        [Tooltip("Client のときだけ、偽造 Cancel を時々送って Host/他クライアントが破棄することをログで確認する(D の AC)")]
        [SerializeField] private bool sendForgedCancelPeriodically = true;

        [Tooltip("偽造メッセージを送る間隔(秒)")]
        [SerializeField] private float forgedMessageIntervalSeconds = 5f;

        private float _playTimer;
        private float _forgedTimer;
        private float _heartbeatTimer;
        private int _playCount;
        private int _lastActiveCount = -1;
        private bool _connected = true;
        private bool _disconnectLogged;
        private PresentationHandle _handle;
        private NgoNetBridge _ngoBridge;

        private async void Start()
        {
            if (actor == null)
            {
                actor = transform;
            }

            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap != null)
            {
                // [11_tasks.md] 6-0 修正2/修正5 — WhenReady を待つ前に配線する(Late Join のスナップショットは
                // 起動直後に届く可能性があるため。Presentation/NetBridge 自体は Awake 時点で既に存在する)。
                if (bootstrap.Presentation != null)
                {
                    bootstrap.Presentation.OnNetworkReceivedPlay += OnNetworkReceivedPlay;
                }

                if (bootstrap.NetBridge is NgoNetBridge ngo)
                {
                    _ngoBridge = ngo;
                    _ngoBridge.ClientDisconnected += OnBridgeDisconnected;
                }

                await bootstrap.WhenReady;
            }

            LogCheck("ready", "1", "role", RoleOf(bootstrap));

            var autoTestName = bootstrap != null ? bootstrap.LaunchOptions.AutoTestName : null;
            if (!string.IsNullOrEmpty(autoTestName))
            {
                RunAutoTestAndQuit(autoTestName).Forget();
            }
        }

        private void OnDestroy()
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap != null && bootstrap.Presentation != null)
            {
                bootstrap.Presentation.OnNetworkReceivedPlay -= OnNetworkReceivedPlay;
            }

            if (_ngoBridge != null)
            {
                _ngoBridge.ClientDisconnected -= OnBridgeDisconnected;
            }
        }

        private void Update()
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap == null || bootstrap.NetBridge == null)
            {
                return;
            }

            Heartbeat(bootstrap);

            if (bootstrap.NetBridge.IsServer)
            {
                _playTimer += Time.deltaTime;
                if (_playTimer >= playIntervalSeconds)
                {
                    _playTimer = 0f;
                    PlayAndSignal(bootstrap);
                }
            }
            else if (sendForgedCancelPeriodically)
            {
                _forgedTimer += Time.deltaTime;
                if (_forgedTimer >= forgedMessageIntervalSeconds)
                {
                    _forgedTimer = 0f;
                    SendForgedCancel(bootstrap);
                }
            }
        }

        // [14_networking.md] §5 — 両端末とも「今アクティブな Presentation の数」「NetworkTime」を定期的に
        // ログへ出す。Late Join の確認は「新規クライアントの activeCount が 0→1 に変わる行」を見ればよい。
        // [11_tasks.md] 6-0 修正1/修正5 — rtt_app_ms(アプリ層の Ping/Pong 往復)と connected(Client が
        // Host との接続を保っているか)も併記する。
        private void Heartbeat(DDriveRuntimeBootstrap bootstrap)
        {
            _heartbeatTimer += Time.deltaTime;
            var activeCount = bootstrap.Presentation != null ? bootstrap.Presentation.DebugActiveHandles().Count : -1;

            if (_heartbeatTimer < 1f && activeCount == _lastActiveCount)
            {
                return;
            }

            _heartbeatTimer = 0f;
            _lastActiveCount = activeCount;

            var rttAppMs = _ngoBridge != null && _ngoBridge.AppRoundTripMs.HasValue ? _ngoBridge.AppRoundTripMs.Value.ToString("F0") : "n/a";

            LogCheck(
                "heartbeat", "1",
                "role", RoleOf(bootstrap),
                "clientId", bootstrap.NetBridge.LocalClientId.ToString(),
                "networkTime", bootstrap.NetBridge.NetworkTime.ToString("F2"),
                "activeCount", activeCount.ToString(),
                "connected", _connected ? "1" : "0",
                "rtt_app_ms", rttAppMs);
        }

        private void PlayAndSignal(DDriveRuntimeBootstrap bootstrap)
        {
            _playCount++;
            var startTime = bootstrap.NetBridge.NetworkTime;
            _handle = Presentation.Play(SkillSlashId, new PlayContext { Self = actor, Position = actor.position });
            LogCheck("play", _playCount.ToString(), "startNetTime", startTime.ToString("F2"));

            // [11_tasks.md] 6-0 修正2 — Host(行為者)自身の OnSignal トラック発火も、Client と全く同じ
            // OnTrackFired 経路(自分の Broadcast が ClientsAndHost 経由で戻ってきて初めて発火する)で
            // 観測できる。signal_fire(意図表明)と signal_recv(実際の発火)を両方ログに出すことで、
            // Host 自身の位相差(≒ループバック分のごく小さな遅延)も確認できるようにする。
            var handleNetKey = bootstrap.Presentation != null ? bootstrap.Presentation.DebugHandleNetKeyOf(_handle.Raw) : 0u;
            SubscribeSignalRecvLogging(bootstrap, _handle.Raw, handleNetKey);

            SignalAfterDelay(bootstrap, _handle, handleNetKey).Forget();
        }

        private async UniTaskVoid SignalAfterDelay(DDriveRuntimeBootstrap bootstrap, PresentationHandle handle, uint handleNetKey)
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(signalDelaySeconds));
            handle.Signal("hit");
            LogCheck("signal_fire", "hit", "key", KeyText(handleNetKey), "networkTime", bootstrap.NetBridge.NetworkTime.ToString("F2"));
        }

        // [11_tasks.md] 6-0 修正2(実機確認で発見した課題2) — 「Client 側で Signal 中継を観測できない」対応。
        // PresentationManager.OnNetworkReceivedPlay(ネット受信で新規生成された Instance の通知)経由で
        // 各ピアが自分の見ている Instance の OnTrackFired を購読し、実際に OnSignal トラックが発火した
        // タイミングで signal_recv をログに出す(Host 自身の予測 Instance にも同じ仕組みを使う。上の
        // PlayAndSignal 参照)。
        private void OnNetworkReceivedPlay(Handle<PresentationMarker> handle, uint handleNetKey)
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap == null)
            {
                return;
            }

            SubscribeSignalRecvLogging(bootstrap, handle, handleNetKey);
        }

        private void SubscribeSignalRecvLogging(DDriveRuntimeBootstrap bootstrap, Handle<PresentationMarker> handle, uint handleNetKey)
        {
            if (bootstrap.Presentation == null)
            {
                return;
            }

            // TrackFiredSubject は Instance の Cleanup 時に Dispose されるため、購読はワンショットの
            // ローカル関数でよい(明示的な IDisposable の保持・破棄は行わない。確認用サンプル専用)。
            void OnFired(PresentationTrack track)
            {
                if (track.Trigger != TrackTrigger.OnSignal)
                {
                    return;
                }

                var bs = DDriveRuntimeBootstrap.Instance;
                var networkTime = bs != null && bs.NetBridge != null ? bs.NetBridge.NetworkTime.ToString("F2") : "n/a";
                LogCheck("signal_recv", track.SignalKey, "key", KeyText(handleNetKey), "networkTime", networkTime);
            }

            // Subscribe(Action<T>) は拡張メソッド(R3.ObservableExtensions)なので `using R3;` が無いと
            // 見えず、Observable<T> 本体の Subscribe(Observer<T>) だけが候補になって型エラーになる
            // (実際に試して確認した。DDrive.Runtime.Presentation の他ファイルはこの using を持つ)。
            bootstrap.Presentation.OnTrackFired(handle).Subscribe(OnFired);
        }

        // [11_tasks.md] 6-0 修正4 — 偽造 Cancel は毎回まったくのランダムな(存在しない)キーではなく、
        // 半分程度は「実在するが自分が発行していない」キーを狙う(IsAuthorizedSender の発行者不一致の
        // 経路を安定して踏ませる)。相手が居ない/対象が見当たらない場合は従来どおり完全ランダムにフォール
        // バックする(その場合は未知キー破棄の経路を踏む)。
        private void SendForgedCancel(DDriveRuntimeBootstrap bootstrap)
        {
            uint forgedKey = 0;
            if (bootstrap.Presentation != null)
            {
                var active = bootstrap.Presentation.DebugActiveHandles();
                for (var i = 0; i < active.Count; i++)
                {
                    var key = bootstrap.Presentation.DebugHandleNetKeyOf(active[i]);
                    if (key != 0)
                    {
                        forgedKey = key;
                        break;
                    }
                }
            }

            if (forgedKey == 0)
            {
                forgedKey = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            }

            bootstrap.NetBridge.Broadcast(new PresentationCancelMsg { HandleNetKey = forgedKey }, NetChannel.ReliableOrdered);
            LogCheck("forged_cancel_sent", forgedKey.ToString());
        }

        // [11_tasks.md] 6-0 修正5(オーケストレーター追加指示、実機確認で発見した課題5) — Host を止めても
        // Client が切断を一切ログに出さなかった。NgoNetBridge.ClientDisconnected(NetworkManager の
        // OnClientDisconnectCallback を中継)を購読し、自分(Client)が Host との接続を失ったときだけ
        // heartbeat の connected を 0 にし、disconnected 行を 1 回だけ出す。個々の Client の切断を Host が
        // 観測したケースは、Host 自身は動作を継続するため connected には影響させない。
        private void OnBridgeDisconnected(ulong clientId, string reason)
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap == null || bootstrap.NetBridge == null)
            {
                return;
            }

            if (!bootstrap.NetBridge.IsServer)
            {
                _connected = false;
            }

            if (_disconnectLogged)
            {
                return;
            }

            _disconnectLogged = true;
            LogCheck("disconnected", "1", "role", RoleOf(bootstrap), "reason", string.IsNullOrEmpty(reason) ? "unknown" : reason);
        }

        // -ddrive-autotest <name> 用。ヘッドレスで一定時間チェックを走らせてからアプリを終了する
        // (実行結果は Player.log の [DDriveNetCheck] 行をオーケストレーター/人が確認する)。
        private async UniTaskVoid RunAutoTestAndQuit(string name)
        {
            LogCheck("autotest_start", name);
            await UniTask.Delay(System.TimeSpan.FromSeconds(playIntervalSeconds * 3 + 2f));
            LogCheck("autotest_done", name);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static string RoleOf(DDriveRuntimeBootstrap bootstrap)
        {
            if (bootstrap == null || bootstrap.NetBridge == null)
            {
                return "unknown";
            }

            if (bootstrap.NetBridge.IsServer)
            {
                return bootstrap.NetBridge.IsClient ? "host" : "server";
            }

            return bootstrap.NetBridge.IsClient ? "client" : "off";
        }

        private static string KeyText(uint handleNetKey) => $"0x{handleNetKey:X8}";

        // [DDriveNetCheck] key=value ... 形式(機械的に読める行。docs/29 参照)。
        private static void LogCheck(params string[] keyValues)
        {
            var sb = new System.Text.StringBuilder("[DDriveNetCheck] ");
            for (var i = 0; i + 1 < keyValues.Length; i += 2)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(keyValues[i]).Append('=').Append(keyValues[i + 1]);
            }

            Debug.Log(sb.ToString());
        }
    }
}
