namespace DDrive.Runtime.Net
{
    // [11_tasks.md] 6-7 — NetCheckRunner(Assets/DDrive/Samples/NetCheckRunner.cs)が `-ddrive-autotest`
    // 実行時に自分自身の観測結果から PASS/FAIL を判定するための純粋ロジック。Unity API 非依存
    // (NetLaunchArgs と同じ方針。EditMode テストで検証する)。
    //
    // 設計方針: ここで判定できるのは「自分の Player.log/自分のプロセス内で観測できたこと」だけ。
    // Host の signal_fire と Client の signal_recv の位相差(② の後半)のような「両プロセスのログを
    // 突き合わせないと分からない」項目は、`Tools/CI/NetCheckAnalyze.ps1`(run-netcheck.cmd から呼ぶ)側の
    // 責務にしている([docs/29] §4「両方のログを外部スクリプトが判定」)。
    //
    // 各条件は [11_tasks.md] 6-7 の PASS 条件(①〜⑥)に対応する:
    //   ① 接続成立                       → ConnectedAtEnd(ただし DisconnectedObserved が true の場合は
    //                                       "disconnect" シナリオの想定どおりの終了なので不接続を許容し、
    //                                       ⑤ の実質チェックに委ねる。2026-09-15 修正、下記 Evaluate 参照)
    //   ② Signal 中継(自プロセス側の下限)  → RequireSignalActivity + SignalRecvCount
    //   ③ 偽造 Cancel の全件破棄           → ForgedCancelSentCount == ForgedCancelDiscardedCount
    //   ④ Late Join 復元                  → RequireLateJoinRestore + LateJoinRestoreObserved(+PlaceholderObserved==false)
    //   ⑤ 切断検知 + 演出 0                → DisconnectedObserved(=自分(Client)が Host との接続を失った)の
    //                                       ときだけ ActiveAndVfxZeroedAfterDisconnect を要求。Host が他
    //                                       Client の切断を観測しただけのケースは対象外(NetCheckRunner 側
    //                                       で役割ごとに絞り込む。2026-09-15 修正)
    //   ⑥ Exception/Error 0               → ExceptionOrErrorCount
    // 加えて 6-5(ContentHash)・A7(遅延猶予 0.5 秒)の判定も同じ関数に含める。
    public struct NetCheckCounters
    {
        // 実行時にわかる基本情報。
        public bool ConnectedAtEnd;
        public bool IsOffRole; // -ddrive-net off(シングルプレイ相当)。この場合は接続関連の判定を全てスキップする

        // ⑥ Exception/Error 0。Application.logMessageReceived で LogType.Exception/Error を数える。
        public int ExceptionOrErrorCount;

        // ② Signal 中継(自プロセス側の下限確認)。実際に演出が回っていることの下限チェック。
        public bool RequireSignalActivity;
        public int SignalFireCount;
        public int SignalRecvCount;

        // ③ 偽造 Cancel の全件破棄。Client 発の Broadcast は Host 経由で ClientsAndHost へ中継され、
        // 送信元自身にも同じ破棄ログが返ってくるため、Client 自身のログだけで検証できる
        // ([14_networking.md] §9、NgoNetBridge.Broadcast の「自分も Host からの RPC で受信して再生する」)。
        public int ForgedCancelSentCount;
        public int ForgedCancelDiscardedCount;

        // ④ Late Join 復元。「新規クライアントの activeCount が 0→1 に変わる行」を見ればよい([docs/29] §4)。
        public bool RequireLateJoinRestore;
        public bool LateJoinRestoreObserved;
        public bool PlaceholderObserved; // Unregistered AssetId ... resolved to Placeholder が 1 件でも出たら NG

        // A7(猶予 0.5 秒)。track_fired が猶予を超えているのに発火してしまった/track_skipped が猶予内
        // なのにスキップされてしまった場合はどちらも実バグ(0 が正常)。
        public int TrackFiredOverGraceCount;
        public int TrackSkippedWithinGraceCount;

        // ⑤ 切断検知 + 演出 0。切断が実際に起きたときだけ判定する(起きなければ該当なしとして PASS 側)。
        public bool DisconnectedObserved;
        public bool ActiveAndVfxZeroedAfterDisconnect;

        // 6-5(ContentHash)。開発ビルド/エディタでは不一致でも警告のみで継続する方針([11_tasks.md] P6 決定事項)
        // のため、ここでは「不一致のまま終わった」場合だけ FAIL にする(不一致検出時に切断されるリリース
        // ビルドの経路は 6-5 側でユニットテスト済み、[docs/29] §14)。
        public bool ContentHashApplicable;
        public string ContentHashStatus; // CatalogContentHashGate.LastStatusText の最終値
    }

    public struct NetCheckResult
    {
        public bool Pass;
        public string Reason; // FAIL 時は理由コード、PASS 時は概要(ログ用)

        public static NetCheckResult PassResult(string summary) => new NetCheckResult { Pass = true, Reason = summary };
        public static NetCheckResult FailResult(string reason) => new NetCheckResult { Pass = false, Reason = reason };
    }

    public static class NetCheckJudge
    {
        public static NetCheckResult Evaluate(in NetCheckCounters c)
        {
            if (c.ExceptionOrErrorCount > 0)
            {
                return NetCheckResult.FailResult($"exception_or_error_count={c.ExceptionOrErrorCount}");
            }

            if (c.IsOffRole)
            {
                // シングルプレイ相当。ネット関連の判定対象が無いため、Exception/Error が無ければ PASS。
                return NetCheckResult.PassResult("off_role_no_net_checks");
            }

            // 2026-09-15 修正(6-7 判定バグ) — "disconnect" シナリオは Host が先に終了し、Client は
            // Host との接続を失ったまま(再接続はしない設計)で自分の自動テスト時間を使い切って終了する。
            // つまり ConnectedAtEnd=false は「切断シナリオが正しく動いた証拠」であり、それ自体は FAIL 材料
            // ではない。DisconnectedObserved(=自分が切断を検知した)なら、この場では失敗にせず、後段の
            // 「切断後に演出が 0 になったか」(⑤)の実質的なチェックに委ねる。逆に DisconnectedObserved が
            // false のまま未接続で終わった場合(切断イベントに気づかずに接続が切れた)は、従来どおり
            // "not_connected" として扱う。
            if (!c.ConnectedAtEnd && !c.DisconnectedObserved)
            {
                return NetCheckResult.FailResult("not_connected");
            }

            if (c.PlaceholderObserved)
            {
                return NetCheckResult.FailResult("placeholder_observed");
            }

            if (c.RequireSignalActivity && c.SignalRecvCount <= 0)
            {
                return NetCheckResult.FailResult("no_signal_recv_observed");
            }

            if (c.ForgedCancelSentCount > 0 && c.ForgedCancelSentCount != c.ForgedCancelDiscardedCount)
            {
                return NetCheckResult.FailResult($"forged_cancel_mismatch sent={c.ForgedCancelSentCount} discarded={c.ForgedCancelDiscardedCount}");
            }

            if (c.RequireLateJoinRestore && !c.LateJoinRestoreObserved)
            {
                return NetCheckResult.FailResult("late_join_not_restored");
            }

            if (c.TrackFiredOverGraceCount > 0)
            {
                return NetCheckResult.FailResult($"track_fired_over_grace={c.TrackFiredOverGraceCount}");
            }

            if (c.TrackSkippedWithinGraceCount > 0)
            {
                return NetCheckResult.FailResult($"track_skipped_within_grace={c.TrackSkippedWithinGraceCount}");
            }

            if (c.DisconnectedObserved && !c.ActiveAndVfxZeroedAfterDisconnect)
            {
                return NetCheckResult.FailResult("vfx_or_active_not_cleared_after_disconnect");
            }

            if (c.ContentHashApplicable && c.ContentHashStatus != "OK")
            {
                return NetCheckResult.FailResult($"content_hash_not_ok status={c.ContentHashStatus ?? "null"}");
            }

            return NetCheckResult.PassResult(
                $"signal_fire={c.SignalFireCount} signal_recv={c.SignalRecvCount} " +
                $"forged_sent={c.ForgedCancelSentCount} forged_discarded={c.ForgedCancelDiscardedCount} " +
                $"late_join_restored={c.LateJoinRestoreObserved} disconnected={c.DisconnectedObserved} " +
                $"content_hash={c.ContentHashStatus ?? "n/a"}");
        }
    }
}
