using Cysharp.Threading.Tasks;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
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
        private PresentationHandle _handle;

        private async void Start()
        {
            if (actor == null)
            {
                actor = transform;
            }

            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap != null)
            {
                await bootstrap.WhenReady;
            }

            LogCheck("ready", "1", "role", RoleOf(bootstrap));

            var autoTestName = bootstrap != null ? bootstrap.LaunchOptions.AutoTestName : null;
            if (!string.IsNullOrEmpty(autoTestName))
            {
                RunAutoTestAndQuit(autoTestName).Forget();
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

            LogCheck(
                "heartbeat", "1",
                "role", RoleOf(bootstrap),
                "clientId", bootstrap.NetBridge.LocalClientId.ToString(),
                "networkTime", bootstrap.NetBridge.NetworkTime.ToString("F2"),
                "activeCount", activeCount.ToString());
        }

        private void PlayAndSignal(DDriveRuntimeBootstrap bootstrap)
        {
            _playCount++;
            var startTime = bootstrap.NetBridge.NetworkTime;
            _handle = Presentation.Play(SkillSlashId, new PlayContext { Self = actor, Position = actor.position });
            LogCheck("play", _playCount.ToString(), "startNetTime", startTime.ToString("F2"));
            SignalAfterDelay(_handle).Forget();
        }

        private async UniTaskVoid SignalAfterDelay(PresentationHandle handle)
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(signalDelaySeconds));
            handle.Signal("hit");
            LogCheck("signal", "hit");
        }

        // [11_tasks.md] 6-0(D/F) — 偽造メッセージが破棄されることをログで確認するための故意の攻撃。
        // 実在しない(または他人の)HandleNetKey を騙って Cancel を送る。IsAuthorizedSender の検証により
        // 全ピアで無視され、[Net/Host] または [Net/Client] の警告ログが 1 行出るはずである。
        private void SendForgedCancel(DDriveRuntimeBootstrap bootstrap)
        {
            var forgedKey = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            bootstrap.NetBridge.Broadcast(new PresentationCancelMsg { HandleNetKey = forgedKey }, NetChannel.ReliableOrdered);
            LogCheck("forged_cancel_sent", forgedKey.ToString());
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
