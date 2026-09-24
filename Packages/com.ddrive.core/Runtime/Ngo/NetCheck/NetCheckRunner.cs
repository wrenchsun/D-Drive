// [42_distribution.md] §2.3-9(P-4、2026-09-20) — NGO 実機 2 台確認用サンプル。NgoNetBridge を直接型参照するのでファイル全体を DDRIVE_NGO で囲う。
// [14_networking.md] §16(N-3、2026-09-22) — `Samples~/NetCheck/` から `DDrive.Runtime.Ngo` アセンブリ本体
// (`Runtime/Ngo/NetCheck/`)へ移設した(GUID 不変。NetCheckScene.unity の参照は壊れない)。名前空間も
// `DDrive.Samples` から `DDrive.Runtime.Net` に揃えた(DDrive.Runtime.Ngo.asmdef の rootNamespace と同じ)。
#if DDRIVE_NGO
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Presentation;
using R3;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §16(N-3) — この namespace 変更(DDrive.Samples → DDrive.Runtime.Net)で判明した
    // コンパイルエラーの修正。DDrive.Runtime.Net は DDrive.Runtime の子であり、DDrive.Runtime.Presentation
    // (兄弟 namespace)も同じ親の下にあるため、未修飾の `Presentation` は namespace `DDrive.Runtime.Presentation`
    // 自身に解決されてしまい、同名の静的ファサードクラス `DDrive.Runtime.Presentation.Presentation` に
    // 解決されない(namespace メンバの直接解決は、ファイル先頭〔コンパイル単位スコープ〕の using より
    // 優先順位が高いため。CS0234 で実際に検出した)。DDrive.Samples 名前空間ではこの衝突が起きなかった
    // (DDrive.Runtime の子ではないため)。using エイリアスを namespace ブロックの内側(このスコープ自身)に
    // 置くことで、このスコープの解決を DDrive.Runtime レベルまで探しに行く前に確定させる。
    using Presentation = DDrive.Runtime.Presentation.Presentation;

    // [11_tasks.md] 6-0(D) — 実機確認用の自動チェック。Host が剣攻撃デモ(PRES_Demo_SkillSlash)を
    // 一定間隔で Play → Signal("hit") し、各端末で位相差・Signal 受信・HitStop 発火をログ出力する。
    // `[Net/Host]`/`[Net/Client]` プレフィックスに加え、`[DDriveNetCheck] key=value ...` 形式の行を出す
    // (docs/29_network_device_test.md の判定基準はこの行を機械的に読む)。
    // Assets/GameData/PreviewScenes/NetCheckScene.unity に置く。NetBridgeSmokeTest の Ping もこのシーンに
    // 同居させる(疎通確認の一次情報源として残す)。
    // [14_networking.md] §16(N-3、2026-09-22) — Host 1 + Client 3(MS2026 の 4 人対戦)を見据え、
    // 接続クライアント数(clients=<n>)・他 Client 離脱(client_left=<clientId>)・期待クライアント数の
    // 到達判定(-ddrive-expect-clients)を追加した。1v1 前提だった当事者判定(HitStop 等)は N-4 の範囲(未着手)。
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
        private int _lastVfxActive = -1;
        private bool _disconnectLogged;
        private PresentationHandle _handle;
        private NgoNetBridge _ngoBridge;

        // [11_tasks.md] 6-7 — `-ddrive-autotest` 実行時の自動判定用カウンタ。役割・シナリオ名は Start() で
        // 一度だけ確定させる(Update()/ログコールバックからは読むだけ)。判定ロジック自体は
        // `DDrive.Runtime.Net.NetCheckJudge`(Unity API 非依存の純関数、EditMode テスト済み)に分離してある。
        private string _role = "unknown";
        private string _scenarioName;
        private bool _requireLateJoinRestore;
        private int _exceptionOrErrorCount;
        private int _signalFireCount;
        private int _signalRecvCount;
        private int _forgedCancelSentCount;
        private int _forgedCancelDiscardedCount;

        // [14_networking.md] §16(N-3、2026-09-22 追記) — quad(Host 1 + Client 3)では Broadcast が全ピアに
        // 届くため、他 Client が送った偽造 Cancel の破棄ログも自分のログに出る。1v1 前提のまま「破棄ログが
        // 見えたら自分の送信分」として数えていたため、quad では sent の約 3 倍が discarded として観測される
        // 実バグ(forged_cancel_mismatch)があった。自分が送った鍵だけを覚えておき、破棄ログにその鍵が
        // 含まれるときだけ数える(確認用サンプル専用コードのため HashSet の allocation は許容する)。
        private readonly HashSet<uint> _forgedCancelKeysSent = new();
        private bool _placeholderObserved;
        private int _trackFiredOverGraceCount;
        private int _trackSkippedWithinGraceCount;
        private bool _maxActiveCountObservedPositive;
        private bool _vfxAndActiveZeroedAfterDisconnect;

        // [14_networking.md] §16(N-3) — Host 1 + Client 3 対応。ExpectedClientCount は Host 役のときだけ
        // -ddrive-expect-clients から読む(未指定/Client 役は 0 のまま=従来どおり判定をスキップ)。
        // MaxConnectedClientsObserved は Heartbeat() が Host 役のときだけ NgoNetBridge.ConnectedClientCount
        // の最大値を積む(Client の入れ替わり〔quad_leave〕があっても「一度でも全員揃った実績」を保持する)。
        private int _expectedClientCount;
        private int _maxConnectedClientsObserved;
        private int _lastClientCount = -1;

        // [14_networking.md] §18/N-6(2026-09-24) — Host 引き継ぎ(ホストマイグレーション)の自動確認用。
        // `-ddrive-migrate` 未指定(None)の既存 8 シナリオは以下の処理を一切行わない(無改修)。
        private NetMigrationRole _migrationRole;
        private string _migrationHost;
        private ushort _migrationPort;
        private bool _migrationAttempted;
        private bool _migrationCompleted;
        private int _signalRecvAfterMigrationCount;
        private bool _contentHashOkAfterMigration;

        // [14_networking.md] §19/N-7(2026-09-24) — 実機確認([29_network_device_test.md] §25 ラウンド2
        // 「気づいた点」)で follower の `migrated=1 role=client newClientId=0` が `StartClient()` 直後
        // (ClientId 割り当て前)に `LocalClientId` を読んでいるため常に 0 になる表示だけの不具合が見つかった。
        // `StartHost`/`StartClient` が true を返した時点ではまだ接続確立前(Heartbeat 参照)なので、この
        // フラグが立っている間だけ毎 Heartbeat で接続確立を確認し、確立した最初の 1 回だけ `migrated=1` を
        // 出す(TryLogMigrated 参照)。
        private bool _migratedLogPending;

        // MS2026/Docs/Spec/03_Network.md §10.5 の Tuning キー既定値と同じ(D-Drive はこの確認用コード内で
        // 複製する。ゲーム側の Tuning テーブルには依存しない、RemoteOneShotGraceMs と同じ考え方)。
        private const float MigrationGraceSeconds = 1f;      // successor が待つ秒数
        private const float MigrationFollowerExtraDelaySeconds = 1f; // follower は +1 秒(合計 2 秒)
        private const float MigrationRetryIntervalSeconds = 2f;
        private const float MigrationTimeoutSeconds = 15f;

        // [11_tasks.md] 6-7 判定バグ修正(2026-09-15) — ⑤(切断後の演出 0)の判定対象は「自分(Client)が
        // Host との接続を失った」場合だけにする。`_ngoBridge.ClientDisconnected` は Host 側でも「他 Client が
        // 切断した」ときに発火する(OnBridgeDisconnected のコメント参照)が、Host は他 Client の 1 人が
        // 抜けても自分のデモ演出(この Runner が周期再生している分)を止めない設計であり、
        // `CancelAllNetworked()` も Client 視点の切断時にだけ呼ばれる([docs/29_network_device_test.md] §8)。
        // 旧実装は role を見ずに `_disconnectLogged` をそのまま判定に使っていたため、実機の 1 対 1 構成でも
        // Host 側が「切断を検知したのに自分の演出が 0 にならない」という偽陽性 FAIL になっていた
        // (run-netcheck.cmd 初回実行、pair0/pair200/latejoin の 3 シナリオ全てで再現)。
        private bool _selfDisconnectedObserved;

        // [11_tasks.md] 6-7 判定バグ修正(2026-09-15) — ②(偽造 Cancel 全件破棄)の in-flight 除外。
        // 遅延シナリオ(pair200 等)では、シナリオ終了間際に送った偽造 Cancel の破棄ログが届く前に
        // プロセスが終了してしまい、sent と discarded の数が食い違う偽陽性 FAIL になっていた
        // (`-ddrive-sim-latency 200` で forged_cancel_sent の最後の 1 件が該当)。往復に掛かる時間の
        // 見積り分だけ、シナリオ終了間際は新規送信を止める。
        private float _autoTestDurationSeconds = -1f;
        private float _autoTestStartRealTime;
        private float _forgedCancelStopMarginSeconds = 2f;

        // remoteOneShotGraceSec の既定値([31] A7)と同じ。PresentationManager 側の定数を公開していないため、
        // 判定専用にここで複製する(値を変える場合は両方直す。ズレても判定が保守的になる方向〔猶予短縮〕なら
        // 実害は小さいが、要判断として残す)。
        private const float RemoteOneShotGraceMs = 500f;

        private async void Start()
        {
            if (actor == null)
            {
                actor = transform;
            }

            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap != null)
            {
                // [11_tasks.md] 6-0 修正2/修正5/修正6 — WhenReady を待つ前に配線する(Late Join のスナップ
                // ショットは起動直後に届く可能性があるため。Presentation/NetBridge 自体は Awake 時点で
                // 既に存在する)。
                if (bootstrap.Presentation != null)
                {
                    bootstrap.Presentation.OnNetworkReceivedPlay += OnNetworkReceivedPlay;
                    bootstrap.Presentation.OnRemoteOneShotSkipped += OnRemoteOneShotSkipped;
                    bootstrap.Presentation.OnAtTimeTrackFired += OnAtTimeTrackFired;
                }

                if (bootstrap.NetBridge is NgoNetBridge ngo)
                {
                    _ngoBridge = ngo;
                    _ngoBridge.ClientDisconnected += OnBridgeDisconnected;
                }

                await bootstrap.WhenReady;
            }

            _role = RoleOf(bootstrap);
            LogCheck("ready", "1", "role", _role);

            // [14_networking.md] §18/N-6(2026-09-24) — Host 引き継ぎの自動確認。-ddrive-migrate
            // successor|follower が指定されたときだけ、切断検知後に MS2026 §10.2 と同じ手順(Stop → 再 Start)
            // を試みる(OnBridgeDisconnected 参照)。再接続先は -ddrive-migrate-host/-migrate-port の
            // 明示指定を優先し、未指定なら通常の接続先(-ddrive-host/-ddrive-port、既定値込み)にフォール
            // バックする(successor の StartHost はポートのみ使う。既定は同じ Port で listen する)。
            // [14_networking.md] §19/N-7(2026-09-24) — 実機確認([29_network_device_test.md] §25 ラウンド2
            // 「気づいた点」)の「起動時に -ddrive-migrate の構成を 1 行ログすると切り分けが楽」という指摘を
            // 反映し、ready ログの直後に構成値を出す(値の解決ロジック自体は N-6 から変更していない)。
            if (bootstrap != null)
            {
                _migrationRole = bootstrap.LaunchOptions.MigrationRole;
                var migrationHost = bootstrap.LaunchOptions.MigrationHost;
                _migrationHost = !string.IsNullOrEmpty(migrationHost) ? migrationHost : (bootstrap.LaunchOptions.Host ?? bootstrap.DefaultHostAddress);
                var migrationPort = bootstrap.LaunchOptions.MigrationPort ?? bootstrap.LaunchOptions.Port ?? bootstrap.DefaultPort;
                _migrationPort = (ushort)migrationPort;

                if (_migrationRole != NetMigrationRole.None)
                {
                    LogCheck(
                        "migrate_config", "1",
                        "role", _migrationRole == NetMigrationRole.Successor ? "successor" : "follower",
                        "host", _migrationHost,
                        "port", _migrationPort.ToString());
                }
            }

            // [14_networking.md] §16(N-3) — -ddrive-expect-clients は Host/Client どちらのプロセスにも
            // 同じ値が渡り得るが、実際に判定へ使うのは Host 役のときだけ(EvaluateResult/Heartbeat 側で
            // 役割を見て絞り込む)。ここでは値をそのまま保持するだけ。
            _expectedClientCount = bootstrap != null ? (bootstrap.LaunchOptions.ExpectedClientCount ?? 0) : 0;

            // [11_tasks.md] 6-7 — Exception/Error(PASS 条件⑥)と偽造 Cancel の破棄(条件③)・Late Join の
            // Placeholder 誤解決(条件④)は、この Runner 自身のイベント購読では観測できない箇所(Presentation/
            // NgoNetBridge 内部の Debug.Log*)で発生するため、標準ログを直接フックして数える。
            Application.logMessageReceived += OnLogMessageReceived;

            var autoTestName = bootstrap != null ? bootstrap.LaunchOptions.AutoTestName : null;
            if (!string.IsNullOrEmpty(autoTestName))
            {
                _scenarioName = autoTestName;

                // [14_networking.md] §16(N-3、2026-09-22 追記) — `-ddrive-autotest` 実行時(=run-netcheck.cmd の
                // ヘッドレス自動判定シナリオ)だけ音を止める。`Hidden`/`-batchmode` でも AudioSource 自体は
                // 再生されスピーカーへ出力され得るため、複数シナリオを連続実行すると SE が鳴り続けて実害が
                // あった(ユーザー報告、2026-09-22)。手動実行・実機確認(-ddrive-autotest 未指定)では
                // 従来どおり鳴らす(判定ロジックには一切影響しない。AudioListener.volume は Signal/Track の
                // 発火判定〔ログベース〕を変えない)。
                AudioListener.volume = 0f;

                // "latejoin" シナリオは Client 側でだけ意味を持つ判定(Host は「後から接続してくる相手」を
                // 待つだけで、自分の activeCount が 0→復元 になるわけではない)。
                _requireLateJoinRestore = _role == "client" && autoTestName.IndexOf("latejoin", System.StringComparison.OrdinalIgnoreCase) >= 0;
                var autoTestSeconds = bootstrap != null ? bootstrap.LaunchOptions.AutoTestSeconds : null;

                // [11_tasks.md] 6-7 判定バグ修正(2026-09-15) — 偽造 Cancel の in-flight 除外マージン。
                // Client 発の Broadcast は Host 経由で全員に中継されるため、往復には少なくとも
                // (Client→Host→ClientsAndHost の 2 ホップ分の遅延)が掛かる。`-ddrive-sim-latency` は
                // 片道分(ms)なので 4 倍(送信 2 ホップ×行き来)+固定バッファ 2 秒を見込む。
                var simLatencyMs = bootstrap != null ? bootstrap.LaunchOptions.SimLatencyMs : null;
                _forgedCancelStopMarginSeconds = 2f + (simLatencyMs ?? 0) / 1000f * 4f;

                RunAutoTestAndQuit(autoTestName, autoTestSeconds).Forget();
            }
        }

        private void OnDestroy()
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap != null && bootstrap.Presentation != null)
            {
                bootstrap.Presentation.OnNetworkReceivedPlay -= OnNetworkReceivedPlay;
                bootstrap.Presentation.OnRemoteOneShotSkipped -= OnRemoteOneShotSkipped;
                bootstrap.Presentation.OnAtTimeTrackFired -= OnAtTimeTrackFired;
            }

            if (_ngoBridge != null)
            {
                _ngoBridge.ClientDisconnected -= OnBridgeDisconnected;
            }

            Application.logMessageReceived -= OnLogMessageReceived;
        }

        // [11_tasks.md] 6-7 — 標準ログをフックして、この Runner のイベント購読では観測できない箇所
        // (PresentationManager/NgoNetBridge 内部の Debug.Log*)のカウントを行う。この確認用シーン専用の
        // サンプルコードのため、定常経路(Tick/Spawn/Play)の LINQ・クロージャ・boxing 禁止の対象外
        // (CLAUDE.md §0-3 は Runtime の本体コードが対象。Debug.unityLogger の呼び出し頻度は低く、
        // ここでの文字列比較コストは実プレイに影響しない)。
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                _exceptionOrErrorCount++;
                return;
            }

            if (type != LogType.Warning && type != LogType.Log)
            {
                return;
            }

            // 条件③: 偽造 Cancel の破棄。Client 発の Broadcast は Host 経由で ClientsAndHost へ中継され、
            // 送信元自身にも同じ破棄ログが返ってくるため、単一プロセスのログだけで送信数と破棄数を突き合わせられる
            // ([14_networking.md] §9、NgoNetBridge.Broadcast のコメント参照)。このシーンでは Cancel を明示的に
            // 送るのは SendForgedCancel だけなので、"PresentationCancelMsg" の破棄ログは全て偽造分だと判定できる。
            // [14_networking.md] §16(N-3、2026-09-22 追記) — quad では他 Client 発の破棄ログも同じ経路(Broadcast
            // はクライアントすべてへ届く)で見えるため、上記だけでは自分の送信分以外まで数えてしまう
            // (forged_cancel_mismatch の原因)。PresentationManager 側の破棄ログ(発行者不一致・未知キーの
            // どちらも)には `HandleNetKey=0xXXXXXXXX`(KeyText と同じ書式)が既に含まれているため、自分が
            // 送った鍵のときだけ数える。
            // [14_networking.md] §16(N-3、2026-09-22 追記・forged_cancel_mismatch 残存分の修正) — 鍵一致
            // だけでは、複数 Client が偶然同じ実キーを偽造対象に選んだ場合に他 Client の破棄まで数えてしまう。
            // 発行者不一致の破棄ログ(PresentationManager)は「送信元 ClientId(N)」を含むので、ログに
            // `ClientId(` があるときは自分の LocalClientId のものだけを数える(未知キー側のログには送信元が
            // 無いので鍵一致だけで判定する)。
            if (condition.Contains("PresentationCancelMsg") && condition.Contains("破棄しました"))
            {
                var keyMatched = false;
                foreach (var sentKey in _forgedCancelKeysSent)
                {
                    if (condition.Contains(KeyText(sentKey)))
                    {
                        keyMatched = true;
                        break;
                    }
                }

                if (keyMatched && condition.Contains("ClientId("))
                {
                    var bootstrap = DDriveRuntimeBootstrap.Instance;
                    if (bootstrap != null && bootstrap.NetBridge != null)
                    {
                        var selfClientIdText = $"ClientId({bootstrap.NetBridge.LocalClientId})";
                        keyMatched = condition.Contains(selfClientIdText);
                    }
                }

                if (keyMatched)
                {
                    _forgedCancelDiscardedCount++;
                }

                return;
            }

            // 条件④: Late Join 直後の Placeholder 誤解決(修正済みのはずの回帰)。
            if (condition.Contains("resolved to Placeholder"))
            {
                _placeholderObserved = true;
            }
        }

        private void Update()
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap == null || bootstrap.NetBridge == null)
            {
                return;
            }

            // [14_networking.md] §16(N-3) — Manual モード(-ddrive-net manual)では役割が Start() の
            // WhenReady 完了時点ではまだ確定していない(接続ボタンを押すまで off のまま)。off/unknown の
            // 間だけ毎フレーム再評価し、host/client/server に変わった瞬間に ready ログを出す。
            // Auto モードは Start() 時点で既に確定しているため、この分岐は最初の 1 回で条件が false に
            // なり以後は素通り(既存の Auto 経路の挙動・ログは不変)。
            if (_role != "host" && _role != "client" && _role != "server")
            {
                var resolvedRole = RoleOf(bootstrap);
                if (resolvedRole != _role)
                {
                    _role = resolvedRole;
                    if (_role == "host" || _role == "client" || _role == "server")
                    {
                        LogCheck("ready", "1", "role", _role);
                    }
                }
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
            // [11_tasks.md] 6-0 修正6(オーケストレーター追加指示) — 切断中は偽造 Cancel を送らない
            // (Broadcast 側が毎回「未接続のため送信できません」警告を出し続けるだけになるため)。
            // [11_tasks.md] 6-7 判定バグ修正(2026-09-15) — シナリオ終了間際も送らない(in-flight 除外。
            // HasTimeForForgedCancelRoundTrip 参照)。
            else if (sendForgedCancelPeriodically && Connected(bootstrap) && HasTimeForForgedCancelRoundTrip())
            {
                _forgedTimer += Time.deltaTime;
                if (_forgedTimer >= forgedMessageIntervalSeconds)
                {
                    _forgedTimer = 0f;
                    SendForgedCancel(bootstrap);
                }
            }
        }

        // [11_tasks.md] 6-7 判定バグ修正(2026-09-15) — シナリオ終了(Application.Quit)までの残り時間が
        // 偽造 Cancel の破棄ログを受け取るのに十分無ければ新規送信を止める。`-ddrive-autotest` 未指定
        // (手動実行)時は `_autoTestDurationSeconds` が設定されないため常に true(従来どおり無制限)。
        private bool HasTimeForForgedCancelRoundTrip()
        {
            if (_autoTestDurationSeconds < 0f)
            {
                return true;
            }

            var remaining = _autoTestDurationSeconds - (Time.time - _autoTestStartRealTime);
            return remaining > _forgedCancelStopMarginSeconds;
        }

        // [11_tasks.md] 6-0 修正5/修正6 — Host/Loopback は常に接続中扱い(Host は自分自身に対して
        // 切断しない)。Client は NgoNetBridge.IsConnected(切断で false になる)を見る。
        // role=off(NetBridgeMode=Ngo で -ddrive-net off 相当。IsServer/IsClient どちらも false)では
        // 未接続として connected=0 を返す(以前は既定値のまま connected=1 と紛らわしく出ていた)。
        private bool Connected(DDriveRuntimeBootstrap bootstrap)
        {
            var bridge = bootstrap.NetBridge;
            if (bridge == null)
            {
                return false;
            }

            if (bridge.IsServer)
            {
                return true;
            }

            if (!bridge.IsClient)
            {
                return false;
            }

            return _ngoBridge == null || _ngoBridge.IsConnected;
        }

        // [14_networking.md] §5 — 両端末とも「今アクティブな Presentation の数」「NetworkTime」を定期的に
        // ログへ出す。Late Join の確認は「新規クライアントの activeCount が 0→1 に変わる行」を見ればよい。
        // [11_tasks.md] 6-0 修正1/修正5 — rtt_app_ms(アプリ層の Ping/Pong 往復)と connected(Client が
        // Host との接続を保っているか)も併記する。
        // [11_tasks.md] 6-0 修正7(実機確認 v3 で発見した「切断後もVFXが残り続ける」実バグの判定用) —
        // vfx_active(VfxManager.ActiveCount = 生存中の VFX インスタンス数)も併記する。スクリーンショットの
        // 白画素カウントに頼らず、切断前後で 0 に落ちることをログだけで確認できるようにする(docs/29 §8)。
        private void Heartbeat(DDriveRuntimeBootstrap bootstrap)
        {
            // [14_networking.md] §19/N-7(2026-09-24) — `migrated=1` の newClientId 表示バグ修正。
            // Heartbeat() は Update() から毎フレーム呼ばれるため、下の間引き(1 秒/変化なしでスキップ)より
            // 前でこの確認を行うことで「接続確立後の最初の Heartbeat」を取りこぼさない。
            if (_migratedLogPending)
            {
                TryLogMigrated(bootstrap);
            }

            _heartbeatTimer += Time.deltaTime;
            var activeCount = bootstrap.Presentation != null ? bootstrap.Presentation.DebugActiveHandles().Count : -1;
            var vfxActive = bootstrap.Vfx != null ? bootstrap.Vfx.ActiveCount : -1;

            // [14_networking.md] §16(N-3) — Host 役のときだけ意味を持つ接続クライアント数(Host 自身を
            // 除いたリモート Client の数。Host 1 + Client 3 が全員繋がった状態では 3。詳細は
            // NgoNetBridge.ConnectedClientCount 側のコメント参照)。Client では -1(既存の
            // activeCount/vfxActive の「対象外は -1」という表現と揃える)。
            var clientCount = bootstrap.NetBridge.IsServer && _ngoBridge != null ? _ngoBridge.ConnectedClientCount : -1;

            if (_heartbeatTimer < 1f && activeCount == _lastActiveCount && vfxActive == _lastVfxActive && clientCount == _lastClientCount)
            {
                return;
            }

            _heartbeatTimer = 0f;
            _lastActiveCount = activeCount;
            _lastVfxActive = vfxActive;
            _lastClientCount = clientCount;

            if (bootstrap.NetBridge.IsServer && clientCount > _maxConnectedClientsObserved)
            {
                _maxConnectedClientsObserved = clientCount;
            }

            var rttAppMs = _ngoBridge != null && _ngoBridge.AppRoundTripMs.HasValue ? _ngoBridge.AppRoundTripMs.Value.ToString("F0") : "n/a";

            // 6-6(K2 修正、2026-09-18 再修正) — 通信停止中に rtt_app_ms が前回値のまま固着していないこと
            // をログだけで判定できるよう併記する。IsAppRoundTripMsStale は「連続 3 回 Pong 無応答(≒3 秒間
            // 無応答)= 通信途絶の疑い」を表す(旧実装は Ping 送信〜Pong 到達までの間〔平常時にも毎秒
            // 発生する〕を stale としていたため、平常時にも 1 になる実バグがあった。NgoNetBridge.cs 参照)。
            var rttAppStale = _ngoBridge != null && _ngoBridge.IsAppRoundTripMsStale ? "1" : "0";

            // 6-5(ContentHash)/6-7 — NetDebugOverlay と同じ状態文字列("検証中..."/"OK"/"不一致: ...")を
            // ログにも出す(自動判定は末尾の RESULT 行に反映する。CatalogContentHashGate.LastStatusText)。
            var contentHash = bootstrap.NetHashGate != null ? bootstrap.NetHashGate.LastStatusText : "n/a";

            LogCheck(
                "heartbeat", "1",
                "role", RoleOf(bootstrap),
                "clientId", bootstrap.NetBridge.LocalClientId.ToString(),
                "networkTime", bootstrap.NetBridge.NetworkTime.ToString("F2"),
                "activeCount", activeCount.ToString(),
                "connected", Connected(bootstrap) ? "1" : "0",
                "rtt_app_ms", rttAppMs,
                "rtt_app_stale", rttAppStale,
                "vfx_active", vfxActive.ToString(),
                "content_hash", contentHash,
                "clients", clientCount.ToString());

            // [11_tasks.md] 6-7 — 条件④(Late Join 復元)の下限確認: 接続中に activeCount>0 を一度でも
            // 観測できれば、Late Join のスナップショットが Placeholder に落ちず反映されたと判定する
            // (`_placeholderObserved` が false のままであることと合わせて判定する。RunAutoTestAndQuit 参照)。
            if (Connected(bootstrap) && activeCount > 0)
            {
                _maxActiveCountObservedPositive = true;
            }

            // 条件⑤(切断検知 + 演出 0): 自分(Client)が Host との接続を失ったことが観測された後、
            // activeCount/vfx_active が両方 0 になった瞬間があれば、ネット経由の演出が強制終了されたと
            // 判定する(以後に再度 >0 になっても、一度でも 0 になった実績自体が「後片付けが機能した」
            // 証拠として残す。sticky)。2026-09-15 修正: `_disconnectLogged` は Host が他 Client の切断を
            // 観測した場合にも true になる(ログの dedupe フラグ)ため、判定には使わない
            // (`_selfDisconnectedObserved` の定義を参照)。
            if (_selfDisconnectedObserved && activeCount == 0 && vfxActive == 0)
            {
                _vfxAndActiveZeroedAfterDisconnect = true;
            }

            // [14_networking.md] §18/N-6(2026-09-24) — D-1(CatalogContentHashGate.Reset)の自動確認。
            // follower が再接続後に自分のハッシュを送り直し、新 Host との照合が改めて "OK" になったことを
            // 観測する(sticky。一度 OK を観測すれば以後不一致に変わっても実績として残す設計は他の
            // sticky フラグと同じ)。successor はホスト側の判定〔ExpectedClientCount〕に委ねるため見ない。
            if (_migrationCompleted && _role == "client" && contentHash == "OK")
            {
                _contentHashOkAfterMigration = true;
            }
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
            _signalFireCount++;
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

            var presentation = bootstrap.Presentation;

            // TrackFiredSubject は Instance の Cleanup 時に Dispose されるため、購読はワンショットの
            // ローカル関数でよい(明示的な IDisposable の保持・破棄は行わない。確認用サンプル専用)。
            // AtTime トラックの発火ログ(track_fired)はここでは扱わない: OnSignal トラックは
            // handle.Signal(...)/受信より後にしか発火しないため Play() の戻り後に購読しても間に合うが、
            // AtTime(Time=0)のトラックは Play() 呼び出し自体の中で同期的に発火してしまうため、ここで
            // 購読する前に発火が終わっているタイミング問題がある(実際に踏んだ)。そのため AtTime は
            // Manager 全体で 1 つの `OnAtTimeTrackFired` イベント(Start() で一度だけ購読)を使う。
            void OnFired(PresentationTrack track)
            {
                if (track.Trigger != TrackTrigger.OnSignal)
                {
                    return;
                }

                var bs = DDriveRuntimeBootstrap.Instance;
                var networkTime = bs != null && bs.NetBridge != null ? bs.NetBridge.NetworkTime.ToString("F2") : "n/a";

                _signalRecvCount++;

                // [14_networking.md] §18/N-6(2026-09-24) — follower(migration 完了後もまだ Client のまま)
                // が新 Host からの Signal を受信できていることの下限確認。successor は「移行後の期待人数」
                // (ExpectedClientCount/MaxConnectedClientsObserved、上の EvaluateResult 参照)で確認するため
                // ここでは follower のみカウントする。
                if (_migrationCompleted && _role == "client")
                {
                    _signalRecvAfterMigrationCount++;
                }

                // [11_tasks.md] 6-0 修正2/修正6 — kind を足す(同じ key で OnSignal トラック数ぶん出るのが
                // 分かるようにする。軽微な要判断だった②の対応)。
                LogCheck("signal_recv", track.SignalKey, "key", KeyText(handleNetKey), "networkTime", networkTime, "kind", track.Kind.ToString());
            }

            // Subscribe(Action<T>) は拡張メソッド(R3.ObservableExtensions)なので `using R3;` が無いと
            // 見えず、Observable<T> 本体の Subscribe(Observer<T>) だけが候補になって型エラーになる
            // (実際に試して確認した。DDrive.Runtime.Presentation の他ファイルはこの using を持つ)。
            presentation.OnTrackFired(handle).Subscribe(OnFired);
        }

        // [11_tasks.md] 6-0 修正6 — 実機確認 v2 で見つかった「遅延があると開始直後のワンショット演出が
        // リモートで一切発火しない」実バグの判定用ログ。スクリーンショットに頼らず、AtTime トラックが
        // 実際に発火したことをログだけで確認できるようにする(late_ms = (elapsed - track.Time) を ms に
        // したもの。通常再生・予測再生では 0 に近い)。Manager 全体で 1 つの event を Start() で一度だけ
        // 購読するため、Play() 内の同期的な初回発火にも間に合う(SubscribeSignalRecvLogging のコメント参照)。
        private void OnAtTimeTrackFired(PresentationTrack track, uint handleNetKey, float elapsed)
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            var networkTime = bootstrap != null && bootstrap.NetBridge != null ? bootstrap.NetBridge.NetworkTime.ToString("F2") : "n/a";
            var lateMsValue = (elapsed - track.Time) * 1000f;
            var lateMs = lateMsValue.ToString("F0");
            LogCheck("track_fired", "1", "kind", track.Kind.ToString(), "time", track.Time.ToString("F2"), "key", KeyText(handleNetKey), "late_ms", lateMs, "networkTime", networkTime);

            // [11_tasks.md] 6-7(A7 の猶予 0.5 秒の逆側チェック) — 猶予を超えて遅れたのに発火してしまった
            // 場合は、PresentationManager 側の猶予判定にバグがある(本来 track_skipped になるはず)。
            if (lateMsValue > RemoteOneShotGraceMs)
            {
                _trackFiredOverGraceCount++;
            }
        }

        // [11_tasks.md] 6-0 修正6 — 猶予を超えて実際にスキップされたワンショットトラックを開発ビルドのみ
        // ログに出す(製品ビルドでのログ汚染・コストを避ける。他の Warn* 系と同じ既存の慣習)。
        private void OnRemoteOneShotSkipped(PresentationTrack track, uint handleNetKey, float lateSec)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var lateMsValue = lateSec * 1000f;
            LogCheck("track_skipped", "1", "kind", track.Kind.ToString(), "time", track.Time.ToString("F2"), "key", KeyText(handleNetKey), "late_ms", lateMsValue.ToString("F0"));

            // [11_tasks.md] 6-7(A7 の猶予 0.5 秒チェック) — 猶予以内なのにスキップされた場合は実バグ
            // (Late Join 直後の大幅に古い演出だけがここに来るはずで、猶予以内のものは track_fired の
            // はず)。
            if (lateMsValue <= RemoteOneShotGraceMs)
            {
                _trackSkippedWithinGraceCount++;
            }
#endif
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
            _forgedCancelSentCount++;
            // [14_networking.md] §16(N-3、2026-09-22 追記) — OnLogMessageReceived が「自分が送った鍵か」を
            // 判定するために保持する(quad 対応、forged_cancel_mismatch の修正)。
            _forgedCancelKeysSent.Add(forgedKey);
            LogCheck("forged_cancel_sent", forgedKey.ToString());
        }

        // [11_tasks.md] 6-0 修正5(実機確認で発見した課題5)/修正6 — Host を止めても Client が切断を一切
        // ログに出さなかった。NgoNetBridge.ClientDisconnected(NetworkManager の OnClientDisconnectCallback
        // を中継)を購読し、disconnected 行を 1 回だけ出す。heartbeat の connected は NgoNetBridge.
        // IsConnected(6-0 修正6 で追加。自分が切断されたときだけ false になる)を Connected() 経由で
        // 直接見るため、ここで別途状態を持たない(単一の情報源に一本化)。個々の Client の切断を Host が
        // 観測したケースは、Host 自身は動作を継続するため connected には影響しない。
        private void OnBridgeDisconnected(ulong clientId, string reason)
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap == null || bootstrap.NetBridge == null)
            {
                return;
            }

            // 判定用フラグ(⑤)は「自分(Client)が Host との接続を失った」場合だけ立てる。2026-09-15 修正。
            if (_role == "client")
            {
                _selfDisconnectedObserved = true;

                // [14_networking.md] §18/N-6(2026-09-24) — Host 引き継ぎの自動確認。自分(Client)が Host
                // との接続を失ったときだけ、MS2026 §10.2 の手順(successor/follower)を 1 回だけ試みる。
                if (_migrationRole != NetMigrationRole.None && !_migrationAttempted)
                {
                    _migrationAttempted = true;
                    RunHostMigrationAsync(bootstrap).Forget();
                }
            }

            // [14_networking.md] §16(N-3) — Host 役は「他 Client が 1 人抜けても自分は継続する」ことを
            // 示すため、離脱した clientId を毎回(dedupe せず)ログに出す(quad_leave の判定用)。既存の
            // `disconnected=1` 行(下の _disconnectLogged ガード)は 1 回だけの汎用ログのままにする
            // (挙動を変えない)。
            if (_role == "host" || _role == "server")
            {
                LogCheck("client_left", clientId.ToString());
            }

            if (_disconnectLogged)
            {
                return;
            }

            _disconnectLogged = true;
            LogCheck("disconnected", "1", "role", RoleOf(bootstrap), "reason", string.IsNullOrEmpty(reason) ? "unknown" : reason);
        }

        // [14_networking.md] §18/N-6(2026-09-24) — MS2026/Docs/Spec/03_Network.md §10.2 のシーケンスを
        // この確認用コードで再現する。successor は Migration/GraceSeconds(1 秒)、follower は +1 秒(合計 2 秒)
        // 待ってから StopNetworking() → StartHost/StartClient を試みる(戻り値 false なら
        // Migration/RetryIntervalSeconds〔2 秒〕ごとに再試行し、Migration/ReconnectTimeoutSeconds
        // 〔15 秒〕で諦めて migration_failed=1 をログする)。D-Drive はゲーム側の GameStateSnapshot 復元
        // ([03_Network.md] §10.3、MS2026 の責務)には関与しない。この Runner にとっての「成功」は
        // StartHost/StartClient が true を返すことだけ。
        private async UniTaskVoid RunHostMigrationAsync(DDriveRuntimeBootstrap bootstrap)
        {
            var isSuccessor = _migrationRole == NetMigrationRole.Successor;
            var graceSeconds = isSuccessor ? MigrationGraceSeconds : MigrationGraceSeconds + MigrationFollowerExtraDelaySeconds;
            await UniTask.Delay(System.TimeSpan.FromSeconds(graceSeconds));

            // MS2026 の手順は「全端末が自分で NetworkManager.Shutdown() を呼んでから bootstrap.
            // StopNetworking() を呼ぶ」だが、この確認用コードは NGO 自体の切断検知(HandleClientDisconnected)
            // に乗じているため、NetworkManager は既に非 Listening のことが多い。StopNetworking() 側
            // (DoManualStop、N-5)が Shutdown 済みでもリセットだけは必ず実行するため、ここでは単純に
            // StopNetworking() を呼ぶだけでよい。
            bootstrap.StopNetworking();

            if (isSuccessor)
            {
                // [14_networking.md] §16(N-3) — successor は -ddrive-expect-clients を「移行後の期待数」
                // として使う(旧 Host 分の実績を引き継がない)。
                _maxConnectedClientsObserved = 0;
                _lastClientCount = -1;
            }

            var deadline = Time.time + MigrationTimeoutSeconds;
            var succeeded = false;
            while (Time.time < deadline)
            {
                succeeded = isSuccessor
                    ? bootstrap.StartHost(_migrationPort)
                    : bootstrap.StartClient(_migrationHost, _migrationPort);

                if (succeeded)
                {
                    break;
                }

                await UniTask.Delay(System.TimeSpan.FromSeconds(MigrationRetryIntervalSeconds));
            }

            if (!succeeded)
            {
                LogCheck("migration_failed", "1", "role", isSuccessor ? "successor" : "follower");
                return;
            }

            // [14_networking.md] §16(N-3) の遅延評価と同じ理由で、この Runner 自身の `_role` フィールドは
            // 起動時に一度確定させたまま(すでに "client")なので、Update() の遅延評価(off/unknown のときだけ
            // 再評価する)には乗らない。`_role` の更新自体は TryLogMigrated(接続確立を確認できた時点)で
            // 行う(既存の RoleOf() をそのまま使う)。
            //
            // [14_networking.md] §19/N-7(2026-09-24) — `_migrationCompleted`(NetCheckJudge の判定用)は
            // 従来どおりここ(StartHost/StartClient 成功時点)で立てる(判定条件は変えない)。一方、
            // `migrated=1` のログ自体は実機確認([29_network_device_test.md] §25 ラウンド2「気づいた点」)で
            // 見つかった不具合の修正: `StartHost`/`StartClient` が true を返した直後はまだ接続確立前で、
            // 特に follower は `LocalClientId` が割り当て前(常に 0)のため、ここでは `migrating=1` だけを
            // 出し、実際の `migrated=1 newClientId=...` は接続確立後の最初の Heartbeat(TryLogMigrated)まで
            // 遅延させる。
            _migrationCompleted = true;
            LogCheck("migrating", "1", "role", isSuccessor ? "successor" : "follower");
            _migratedLogPending = true;
        }

        // [14_networking.md] §19/N-7(2026-09-24) — 接続確立後(successor は IsServer、follower は
        // NgoNetBridge.IsConnected かつ LocalClientId!=0)になった最初の Heartbeat 呼び出しで 1 回だけ
        // `migrated=1 role=host|client newClientId=<実 ClientId>` を出す。
        private void TryLogMigrated(DDriveRuntimeBootstrap bootstrap)
        {
            var isSuccessor = _migrationRole == NetMigrationRole.Successor;
            var established = isSuccessor
                ? bootstrap.NetBridge.IsServer
                : _ngoBridge != null && _ngoBridge.IsConnected && bootstrap.NetBridge.LocalClientId != 0;

            if (!established)
            {
                return;
            }

            _migratedLogPending = false;
            _role = RoleOf(bootstrap);
            LogCheck("migrated", "1", "role", _role, "newClientId", bootstrap.NetBridge.LocalClientId.ToString());
        }

        // -ddrive-autotest <name> 用。ヘッドレスで一定時間チェックを走らせてから、この Runner 自身が
        // 観測した結果を DDrive.Runtime.Net.NetCheckJudge(純関数)で判定し、`RESULT=PASS|FAIL` を
        // 1 行ログしてから終了する([11_tasks.md] 6-7)。Host/Client 2 プロセスを跨る判定(Signal 中継の
        // 位相差など)はここでは行わない(単一プロセスのログだけでは分からないため。`Tools/CI/
        // NetCheckAnalyze.ps1` が両方の Player.log を読んで追加で判定する。[docs/29] §4)。
        private async UniTaskVoid RunAutoTestAndQuit(string name, float? autoTestSeconds)
        {
            LogCheck("autotest_start", name);
            var durationSeconds = autoTestSeconds ?? (playIntervalSeconds * 3 + 2f);

            // [11_tasks.md] 6-7 判定バグ修正(2026-09-15) — HasTimeForForgedCancelRoundTrip が使う基準時刻。
            _autoTestDurationSeconds = durationSeconds;
            _autoTestStartRealTime = Time.time;

            await UniTask.Delay(System.TimeSpan.FromSeconds(durationSeconds));
            LogCheck("autotest_done", name);

            var result = EvaluateResult();
            LogCheck("RESULT", result.Pass ? "PASS" : "FAIL", "scenario", name, "reason", result.Reason ?? "n/a");

            var exitCode = result.Pass ? 0 : 1;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }

        // [11_tasks.md] 6-7 — この Runner が自分自身のログ/イベント購読から集められた観測値を
        // `NetCheckCounters` にまとめ、`NetCheckJudge.Evaluate`(Unity API 非依存、EditMode テスト済み)に
        // 渡すだけの薄いアダプタ。判定の条件そのものは NetCheckJudge 側に集約してある。
        private NetCheckResult EvaluateResult()
        {
            var bootstrap = DDriveRuntimeBootstrap.Instance;
            var isOffRole = _role != "host" && _role != "client" && _role != "server";

            var contentHashStatus = bootstrap != null && bootstrap.NetHashGate != null ? bootstrap.NetHashGate.LastStatusText : null;
            var contentHashApplicable = !isOffRole && contentHashStatus != null && contentHashStatus != "検証中...";

            var counters = new NetCheckCounters
            {
                ConnectedAtEnd = bootstrap != null && Connected(bootstrap),
                IsOffRole = isOffRole,
                ExceptionOrErrorCount = _exceptionOrErrorCount,
                RequireSignalActivity = !isOffRole,
                SignalFireCount = _signalFireCount,
                SignalRecvCount = _signalRecvCount,
                ForgedCancelSentCount = _forgedCancelSentCount,
                ForgedCancelDiscardedCount = _forgedCancelDiscardedCount,
                RequireLateJoinRestore = _requireLateJoinRestore,
                LateJoinRestoreObserved = _maxActiveCountObservedPositive,
                PlaceholderObserved = _placeholderObserved,
                TrackFiredOverGraceCount = _trackFiredOverGraceCount,
                TrackSkippedWithinGraceCount = _trackSkippedWithinGraceCount,
                DisconnectedObserved = _selfDisconnectedObserved,
                ActiveAndVfxZeroedAfterDisconnect = _vfxAndActiveZeroedAfterDisconnect,
                ContentHashApplicable = contentHashApplicable,
                ContentHashStatus = contentHashStatus,

                // [14_networking.md] §16(N-3) — Host 役のときだけ意味を持つ(Client 役・未指定は 0 のまま
                // なので NetCheckJudge 側の判定は素通りする。既存の 1v1 シナリオは無改修)。
                ExpectedClientCount = (_role == "host" || _role == "server") ? _expectedClientCount : 0,
                MaxConnectedClientsObserved = _maxConnectedClientsObserved,

                // [14_networking.md] §18/N-6(2026-09-24) — Host 引き継ぎ。MigrationExpected は
                // `-ddrive-migrate` 未指定(既存 8 シナリオ)なら常に false のまま=NetCheckJudge 側の判定は
                // 完全にスキップされる(無改修)。
                MigrationExpected = _migrationRole != NetMigrationRole.None,
                MigrationCompleted = _migrationCompleted,
                IsSuccessor = _migrationRole == NetMigrationRole.Successor,
                SignalRecvAfterMigrationCount = _signalRecvAfterMigrationCount,
                ContentHashOkAfterMigration = _contentHashOkAfterMigration,
            };

            return NetCheckJudge.Evaluate(counters);
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
#endif // DDRIVE_NGO
