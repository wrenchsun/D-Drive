namespace DDrive.Runtime.Net
{
    // [11_tasks.md] 6-6(K2 修正、2026-09-18 再修正、docs/29_network_device_test.md §16.2)—
    // NgoNetBridge の「アプリ層 RTT(Ping/Pong)」計測・stale(通信途絶の疑い)判定の状態遷移だけを
    // 切り出した純粋ロジック(Unity API 非依存。NetCheckJudge と同じ方針。EditMode テストで検証する)。
    //
    // v5 実機確認(§16.2)で見つかった実バグの真因は 2 つ:
    //   1. 経過時間の基準を「直近の Ping 送信時刻」にしていたため、Pong が返らなくても 1 秒ごとの
    //      Ping 送信で基準点そのものが毎秒リセットされ、通信停止がどれだけ長引いても 1 秒周期の
    //      ノコギリ波(高々 1000ms 前後)にしかならなかった。
    //   2. stale フラグが「Ping 送信〜Pong 到達までの間」を指しており、これは平常運用でも毎秒
    //      発生する(Ping 周期 1 秒に対して RTT は数百 ms)ため、通信が正常でも頻繁に true になって
    //      いた。
    //
    // 修正方針(docs/29 §16.3 の 2 案のうち「未応答のまま最も古い Ping の送信時刻」を採用):
    //   1'. 経過時間の基準を「現在も応答待ちが続いている一連の Ping のうち、最初の 1 通を送った時刻」
    //      (`_unansweredStreakStartRealtime`)にする。Pong が 1 通も返らない限り、Ping が何回
    //      再送されてもこの基準点は動かない(Pong を受信した瞬間だけ null に戻り、次に未応答が
    //      始まったタイミングで新しい基準点が立つ)。
    //      「最後に Pong を受信した時刻」を基準にする案も検討したが、まだ 1 度も Pong を受信できて
    //      いない接続直後からの断線(基準点が無い)を素直に扱えない・応答待ちが始まった時点そのものを
    //      指すためダウンタイムの下限としてより正確(過大評価しない)という 2 点でこちらを選んだ。
    //   2'. stale フラグは「連続 N 回 Pong が返っていない」ことを要求するように変更した(既定 N=3。
    //      DefaultStalePongMissThreshold 参照)。
    //
    // Unity の `Time.unscaledTimeAsDouble` 相当の実時間は呼び出し側が明示的に渡す(このクラス自体は
    // UnityEngine を参照しない。CLAUDE.md §0-3 の禁止 API チェックの対象外にする狙いと、実時間の
    // 経過を EditMode テストから自由に模擬できるようにする狙いの両方)。
    public sealed class AppRoundTripTracker
    {
        // Ping ループは 1Hz、Pong の配送は NetChannel.Unreliable(パケットロス上等)のため、1 回だけ
        // Pong が届かなくても実際の通信途絶とは限らない(単発ロスは通常の Wi-Fi でも起こりうる)。
        // 3 回連続(≒3 秒間無応答)を閾値にすることで、単発ロスは吸収しつつ、実際の通信途絶は
        // docs/29 §16 の実機確認(6 秒切断)の範囲内で十分早く検出できるようにする。
        public const int DefaultStalePongMissThreshold = 3;

        private readonly int _staleThreshold;
        private double? _lastMeasuredMs;
        private double _unansweredStreakStartRealtime = -1d;
        private bool _awaitingPong;
        private int _consecutiveMissedPongCount;

        public AppRoundTripTracker(int staleThreshold = DefaultStalePongMissThreshold)
        {
            _staleThreshold = staleThreshold;
        }

        // 連続して `_staleThreshold` 回 Pong が返っていない(=通信途絶の疑い)。
        public bool IsStale => _consecutiveMissedPongCount >= _staleThreshold;

        // NgoNetBridge.AppRoundTripMs 相当。`nowRealtime` は呼び出し側の現在実時刻
        // (Time.unscaledTimeAsDouble)。未応答の Ping がある間は、応答待ちが始まった時刻
        // (`_unansweredStreakStartRealtime`)からの経過時間を下限として返す(それが最後の実測値を
        // 上回っている場合のみ。下回っている間はまだ正常な RTT の範囲内なので実測値を返す)。
        public double? GetRoundTripMs(double nowRealtime)
        {
            if (_awaitingPong && _unansweredStreakStartRealtime >= 0d)
            {
                var elapsedMs = (nowRealtime - _unansweredStreakStartRealtime) * 1000d;
                if (!_lastMeasuredMs.HasValue || elapsedMs > _lastMeasuredMs.Value)
                {
                    return elapsedMs;
                }
            }

            return _lastMeasuredMs;
        }

        // 1 秒おきの Ping 送信時に呼ぶ(NgoNetBridge.PingLoopAsync 相当)。既に応答待ちの Ping がある
        // (=前回送った Ping の Pong がまだ返っていない)場合は 1 回分の未達としてカウントするだけで、
        // 応答待ちの起点(`_unansweredStreakStartRealtime`)は動かさない。まだ応答待ちが無い場合は、
        // この送信が新しい応答待ちの起点になる。通常運用では RTT(数百 ms)<< Ping 周期(1 秒)のため、
        // 次の送信までに毎回 Pong が届き、応答待ちが 1 秒以上続くことはなく未達カウントは 0 のまま
        // 維持される。
        public void OnPingSent(double nowRealtime)
        {
            if (_awaitingPong)
            {
                _consecutiveMissedPongCount++;
            }
            else
            {
                _unansweredStreakStartRealtime = nowRealtime;
            }

            _awaitingPong = true;
        }

        // Pong 受信時に呼ぶ(NgoNetBridge.OnPongMsgReceived 相当)。measuredMs は Client→Host→Client の
        // 実測往復時間。
        public void OnPongReceived(double measuredMs)
        {
            _lastMeasuredMs = measuredMs;
            _awaitingPong = false;
            _unansweredStreakStartRealtime = -1d;
            _consecutiveMissedPongCount = 0;
        }

        // 切断時に呼ぶ(NgoNetBridge.HandleClientDisconnected の Client 分岐相当)。次回接続時に
        // ゼロから測り直せるよう全状態を初期化する。
        public void Reset()
        {
            _lastMeasuredMs = null;
            _awaitingPong = false;
            _unansweredStreakStartRealtime = -1d;
            _consecutiveMissedPongCount = 0;
        }
    }
}
