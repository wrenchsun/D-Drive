using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §22 / [29_network_device_test.md] §27(N-8、2026-10-06) — Cutscene のマーカーが実 NGO で
    // 「最初から再生は 0 秒も発火 / 受信側は開始位置 s から遡って 0.5 秒以内だけ発火 / 送信者(予測再生)は全部 1 回ずつ」
    // になっていることを、NetCheck の各プロセスのログから判定する純関数(Unity API 非依存、EditMode テスト済み)。
    //
    // 入力は `[NetCheck] cutscene_xxx key=value ...` 形式の行(NetCheckRunner が出す。形式は Parse 参照)。
    // プロセス内の自己判定(NetCheckRunner)と、別 PC のログを集めた後の判定(Run-NetCheck.ps1 -JudgeOnly が
    // 同じ規則を PowerShell で再実装して突き合わせる)の両方がこの規則を使う。
    public struct NetCheckCutsceneMarkerSpec
    {
        public string Key;
        public double Time;

        public NetCheckCutsceneMarkerSpec(string key, double time)
        {
            Key = key;
            Time = time;
        }
    }

    public sealed class NetCheckCutscenePlayVerdict
    {
        public string Handle;
        public string NetKey = "n/a";
        public bool IsSender;
        public double S;                 // 受信側の開始位置(秒)。送信者は 0
        public int SilentReported = -1;  // 本体のログの「無音にしたマーカー n 件」(送信者は -1)
        public bool Pass = true;
        public string Reason = "ok";
        public string Fired = string.Empty; // 外部マーカーの発火(キー:回数,...)
    }

    public sealed class NetCheckCutsceneSummary
    {
        public bool Pass;
        public string Reason;
        public int SenderPlays;
        public int ReceivedPlays;
        public double MinS = -1;
        public double MaxS = -1;
        public double MeanS = -1;

        // Player が Timeline から Signal マーカー(クラス名とファイル名が違う型)を読み込めたか。false の間は Signal の判定をしない
        // (外部マーカーだけで判定する。docs/29 §27 の「Player で Signal トラックが読めない」切り分け用)。
        public bool SignalLoaded = true;
        public readonly List<NetCheckCutscenePlayVerdict> Plays = new();
    }

    public static class NetCheckCutsceneJudge
    {
        // CutsceneManager.RemoteMarkerGraceSec と同じ値(本体は private const のため判定専用に複製)。
        public const double GraceSec = 0.5d;

        // ログの s は F3(小数 3 桁)に丸められているため、境界(s − t = 0.5)ちょうど付近は発火 0 回 / 1 回のどちらも許す。
        public const double BoundaryTolerance = 0.0006d;

        // 1 つの時刻に置いたマーカーの種類数(Signal + 外部マーカー = 2)。本体のログの「無音にした n 件」は種類の合算。
        public const int DefaultKindsPerTime = 2; // Signal + 外部マーカー。Player で Signal が読めないとき(signal=0)は EvaluateLog が 1 に下げる

        public const string Tag = "[NetCheck] ";

        // M-6(2026-10-06): NetCheck のテスト用 Timeline(CUT_NetCheck_Markers)に置いた「D-Drive の全トラック / マーカー / クリップ種別」。
        // Player がこの全種別を Timeline から読み込めたことを cut_local の自己判定に含める(読めない種別があれば FAIL)。
        // 型名は Type.Name(Player のログで Unity が名前を解決できなかった型は "null" になる)。
        // [64_review_m6] GF-R-06 — 外から書き換えられないよう読み取り専用(v1.4.0 のタグ前に型を確定)。
        public static IReadOnlyList<string> ExpectedTrackTypes { get; } = Array.AsReadOnly(new[]
        {
            "CutsceneEventTrack", "CutsceneSignalTrack", "CutsceneShakeTrack", "CutsceneHapticTrack",
            "CutsceneSeTrack", "CutsceneVfxTrack", "CutsceneUiTrack", "CutsceneCameraTrack",
            "CutscenePresentationTrack", "CutsceneAnchorGroupTrack", "MarkerTrack",
        });

        public static IReadOnlyList<string> ExpectedMarkerTypes { get; } = Array.AsReadOnly(new[]
        {
            "CutsceneEventNotification", "CutsceneSignalNotification", "CutsceneShakeNotification",
            "CutsceneHapticNotification", "NetCheckCutsceneMarker",
        });

        public static IReadOnlyList<string> ExpectedClipTypes { get; } = Array.AsReadOnly(new[]
        {
            "CutsceneSeClip", "CutsceneVfxClip", "CutsceneUiClip", "CutsceneCameraClip",
            "CutscenePresentationClip", "CutsceneAnchorGroupClip",
        });

        public static NetCheckCutsceneMarkerSpec[] DefaultMarkers()
        {
            return new[]
            {
                new NetCheckCutsceneMarkerSpec("m0", 0.0),
                new NetCheckCutsceneMarkerSpec("m1", 0.1),
                new NetCheckCutsceneMarkerSpec("m4", 0.4),
                new NetCheckCutsceneMarkerSpec("m6", 0.6),
                new NetCheckCutsceneMarkerSpec("m15", 1.5),
            };
        }

        // 1 つのマーカー(時刻 t)に対する期待発火回数の範囲。
        public static void ExpectedCount(double t, bool isSender, double s, out int min, out int max)
        {
            min = 1;
            max = 1;
            if (isSender || t > s)
            {
                return; // 送信者は全部 1 回 / 開始位置より後のマーカーは通常の Tick で 1 回
            }

            var d = s - t;
            if (d > GraceSec + BoundaryTolerance)
            {
                min = 0;
                max = 0;
            }
            else if (d >= GraceSec - BoundaryTolerance)
            {
                min = 0; // 境界ちょうど(丸め誤差の範囲)は仕様上 0.5 ちょうどは発火だが、F3 丸めで判別できないので両方許す
                max = 1;
            }
        }

        // 受信側で「無音にしたはず」の件数(種類数込み)の範囲。
        public static void ExpectedSilentCount(IReadOnlyList<NetCheckCutsceneMarkerSpec> markers, double s, int kindsPerTime, out int min, out int max)
        {
            min = 0;
            max = 0;
            for (var i = 0; i < markers.Count; i++)
            {
                ExpectedCount(markers[i].Time, false, s, out var lo, out var hi);
                if (hi == 0)
                {
                    min += kindsPerTime;
                }

                if (lo == 0)
                {
                    max += kindsPerTime;
                }
            }
        }

        // `[NetCheck] cutscene_xxx k=v k=v` を分解する。該当しない行は false。
        public static bool TryParseLine(string line, out string kind, Dictionary<string, string> kv)
        {
            kind = null;
            kv.Clear();
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            var idx = line.IndexOf(Tag + "cutscene_", StringComparison.Ordinal);
            if (idx < 0)
            {
                return false;
            }

            var rest = line.Substring(idx + Tag.Length);
            var parts = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            kind = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var eq = parts[i].IndexOf('=');
                if (eq > 0)
                {
                    kv[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
                }
            }

            return true;
        }

        // 本体の受信ログ(CutsceneManager)を分解する。形式:
        //   `[Net/Host|Client] Cutscene: 受信した再生の開始位置 s=0.234 秒(NetworkTime − StartNetTime)・猶予(0.5 秒)を超えて
        //    無音にしたマーカー 4 件(HandleNetKey=0xAB000001、'名前')。`
        public static bool TryParseEngineReceiveLine(string line, out double s, out int silent, out string netKey)
        {
            s = 0;
            silent = -1;
            netKey = null;
            if (string.IsNullOrEmpty(line) || !line.Contains("Cutscene: 受信した再生の開始位置 s="))
            {
                return false;
            }

            var si = line.IndexOf("s=", StringComparison.Ordinal);
            var se = si + 2;
            var end = se;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.' || line[end] == '-'))
            {
                end++;
            }

            if (!double.TryParse(line.Substring(se, end - se), NumberStyles.Float, CultureInfo.InvariantCulture, out s))
            {
                return false;
            }

            var mi = line.IndexOf("無音にしたマーカー", StringComparison.Ordinal);
            if (mi >= 0)
            {
                var p = mi + "無音にしたマーカー".Length;
                while (p < line.Length && line[p] == ' ')
                {
                    p++;
                }

                var q = p;
                while (q < line.Length && char.IsDigit(line[q]))
                {
                    q++;
                }

                int.TryParse(line.Substring(p, q - p), NumberStyles.Integer, CultureInfo.InvariantCulture, out silent);
            }

            var ki = line.IndexOf("HandleNetKey=", StringComparison.Ordinal);
            if (ki >= 0)
            {
                var p = ki + "HandleNetKey=".Length;
                var q = p;
                while (q < line.Length && (char.IsLetterOrDigit(line[q])))
                {
                    q++;
                }

                netKey = line.Substring(p, q - p);
            }

            return true;
        }

        private sealed class PlayAcc
        {
            public string Handle;
            public bool IsSender;
            public bool HasRecv;
            public string NetKey = "n/a";
            public double S;
            public int Silent = -1;
            public readonly Dictionary<string, int> Markers = new();
            public readonly Dictionary<string, int> Signals = new();
        }

        // 1 プロセス分のログ行を判定する。config 行が無ければ既定のマーカー構成・種類数 2 を使う。
        public static NetCheckCutsceneSummary EvaluateLog(IEnumerable<string> lines)
        {
            var summary = new NetCheckCutsceneSummary();
            var kv = new Dictionary<string, string>();
            var markers = DefaultMarkers();
            var kinds = DefaultKindsPerTime;
            var role = "observe";
            var expectPlays = -1;
            var configuredPlays = -1;
            var timelineOk = true;
            var timelineReason = string.Empty;
            var signalLoaded = true;
            var accs = new Dictionary<string, PlayAcc>();
            var order = new List<PlayAcc>();

            PlayAcc Get(string handle)
            {
                if (!accs.TryGetValue(handle, out var a))
                {
                    a = new PlayAcc { Handle = handle };
                    accs[handle] = a;
                    order.Add(a);
                }

                return a;
            }

            foreach (var line in lines)
            {
                if (!TryParseLine(line, out var kind, kv))
                {
                    continue;
                }

                switch (kind)
                {
                    case "cutscene_config":
                        if (kv.TryGetValue("role", out var r)) { role = r; }
                        if (kv.TryGetValue("expectPlays", out var ep)) { int.TryParse(ep, NumberStyles.Integer, CultureInfo.InvariantCulture, out expectPlays); }
                        if (kv.TryGetValue("plays", out var pl)) { int.TryParse(pl, NumberStyles.Integer, CultureInfo.InvariantCulture, out configuredPlays); }
                        if (kv.TryGetValue("kinds", out var kd)) { int.TryParse(kd, NumberStyles.Integer, CultureInfo.InvariantCulture, out kinds); }
                        if (kv.TryGetValue("markers", out var ml))
                        {
                            var list = new List<NetCheckCutsceneMarkerSpec>();
                            foreach (var item in ml.Split(','))
                            {
                                var c = item.IndexOf(':');
                                if (c > 0 && double.TryParse(item.Substring(c + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var tm))
                                {
                                    list.Add(new NetCheckCutsceneMarkerSpec(item.Substring(0, c), tm));
                                }
                            }

                            if (list.Count > 0) { markers = list.ToArray(); }
                        }

                        break;

                    case "cutscene_timeline":
                        // markers=<型名:件数,...>。Timeline から Signal / 外部マーカーが集められていなければ読み込み失敗。
                        if (kv.TryGetValue("ok", out var ok) && ok != "1")
                        {
                            timelineOk = false;
                            timelineReason = "timeline_markers_not_loaded";
                        }

                        if (kv.TryGetValue("signal", out var sg) && sg != "1")
                        {
                            signalLoaded = false;
                            if (timelineOk) { timelineOk = false; timelineReason = "timeline_signal_not_loaded"; }
                        }

                        // M-6: 期待する全種別(ExpectedTrackTypes / ExpectedMarkerTypes / ExpectedClipTypes)のうち読めなかったもの。
                        if (kv.TryGetValue("missing", out var ms) && ms != "none" && timelineOk)
                        {
                            timelineOk = false;
                            timelineReason = "timeline_kinds_missing:" + ms;
                        }

                        break;

                    case "cutscene_play":
                        if (kv.TryGetValue("handle", out var hp)) { var a = Get(hp); a.IsSender = true; }
                        break;

                    case "cutscene_own_key":
                        if (kv.TryGetValue("handle", out var ho) && kv.TryGetValue("netKey", out var nko)) { Get(ho).NetKey = nko; }
                        break;

                    case "cutscene_recv":
                        if (kv.TryGetValue("handle", out var hr))
                        {
                            var a = Get(hr);
                            a.HasRecv = true;
                            if (kv.TryGetValue("netKey", out var nk)) { a.NetKey = nk; }
                            if (kv.TryGetValue("s", out var sv)) { double.TryParse(sv, NumberStyles.Float, CultureInfo.InvariantCulture, out a.S); }
                            if (kv.TryGetValue("silent", out var sl)) { int.TryParse(sl, NumberStyles.Integer, CultureInfo.InvariantCulture, out a.Silent); }
                        }

                        break;

                    case "cutscene_marker":
                    case "cutscene_signal":
                        if (kv.TryGetValue("handle", out var hm) && kv.TryGetValue("key", out var key))
                        {
                            var dict = kind == "cutscene_marker" ? Get(hm).Markers : Get(hm).Signals;
                            dict.TryGetValue(key, out var n);
                            dict[key] = n + 1;
                        }

                        break;
                }
            }

            summary.SignalLoaded = signalLoaded;
            // 本体の「無音にしたマーカー n 件」は読み込めたマーカーの種類の合算。Signal が読めていなければ外部マーカーの 1 種類だけ。
            if (!signalLoaded)
            {
                kinds = 1;
            }

            var sumS = 0d;
            var failReason = timelineOk ? null : timelineReason;
            var seenNetKeys = new HashSet<string>();
            foreach (var a in order)
            {
                var v = new NetCheckCutscenePlayVerdict
                {
                    Handle = a.Handle,
                    NetKey = a.NetKey,
                    IsSender = !a.HasRecv,
                    S = a.HasRecv ? a.S : 0d,
                    SilentReported = a.HasRecv ? a.Silent : -1,
                };

                // 送信者(予測再生)に s は無い。受信ログの無い handle(cutscene_play だけ)は送信者として判定する。
                if (v.IsSender) { summary.SenderPlays++; }
                else
                {
                    summary.ReceivedPlays++;
                    sumS += v.S;
                    if (summary.MinS < 0 || v.S < summary.MinS) { summary.MinS = v.S; }
                    if (v.S > summary.MaxS) { summary.MaxS = v.S; }
                }

                var sb = new StringBuilder();
                var reasons = new List<string>();
                // GD-R-12: observe のプロセスに送信者はありえない(受信ログの無い再生は判定できない)。同じ netKey の受信は 1 回だけ。
                if (v.IsSender && role != "trigger") { reasons.Add("unmatched_play"); }
                if (!v.IsSender && v.NetKey != "n/a" && !seenNetKeys.Add(v.NetKey)) { reasons.Add($"duplicate_netkey {v.NetKey}"); }
                for (var i = 0; i < markers.Length; i++)
                {
                    ExpectedCount(markers[i].Time, v.IsSender, v.S, out var lo, out var hi);
                    a.Markers.TryGetValue(markers[i].Key, out var got);
                    a.Signals.TryGetValue(markers[i].Key, out var gotSig);
                    if (sb.Length > 0) { sb.Append(','); }
                    sb.Append(markers[i].Key).Append(':').Append(got);
                    if (got > 1) { reasons.Add($"duplicate_fire key={markers[i].Key} count={got}"); }
                    else if (got < lo || got > hi) { reasons.Add($"marker_mismatch key={markers[i].Key} got={got} expected={lo}..{hi}"); }

                    if (!signalLoaded) { }
                    else if (gotSig > 1) { reasons.Add($"duplicate_signal key={markers[i].Key} count={gotSig}"); }
                    else if (gotSig < lo || gotSig > hi) { reasons.Add($"signal_mismatch key={markers[i].Key} got={gotSig} expected={lo}..{hi}"); }
                }

                if (!v.IsSender && v.SilentReported >= 0)
                {
                    ExpectedSilentCount(markers, v.S, kinds, out var smin, out var smax);
                    if (v.SilentReported < smin || v.SilentReported > smax)
                    {
                        reasons.Add($"silent_count_mismatch got={v.SilentReported} expected={smin}..{smax}");
                    }
                }

                v.Fired = sb.ToString();
                if (reasons.Count > 0)
                {
                    v.Pass = false;
                    v.Reason = string.Join(";", reasons);
                    failReason ??= $"play {a.Handle} {v.Reason}";
                }

                summary.Plays.Add(v);
            }

            if (summary.ReceivedPlays > 0) { summary.MeanS = sumS / summary.ReceivedPlays; }

            if (failReason == null)
            {
                if (role == "trigger" && configuredPlays >= 0 && summary.SenderPlays != configuredPlays)
                {
                    failReason = $"sender_plays_mismatch got={summary.SenderPlays} expected={configuredPlays}";
                }
                else if (role != "trigger" && expectPlays >= 0 && summary.ReceivedPlays != expectPlays)
                {
                    failReason = $"received_plays_mismatch got={summary.ReceivedPlays} expected={expectPlays}";
                }
                else if (role != "trigger" && expectPlays < 0 && summary.ReceivedPlays == 0 && summary.SenderPlays == 0)
                {
                    failReason = "no_play_observed";
                }
            }

            summary.Pass = failReason == null;
            summary.Reason = failReason ?? "ok";
            return summary;
        }

        public static string FormatSummaryLine(NetCheckCutsceneSummary s)
        {
            var inv = CultureInfo.InvariantCulture;
            return $"{Tag}cutscene_summary plays={s.SenderPlays} recv={s.ReceivedPlays} " +
                   $"sMin={s.MinS.ToString("F3", inv)} sMax={s.MaxS.ToString("F3", inv)} sMean={s.MeanS.ToString("F3", inv)} " +
                   $"fired={(s.Plays.Count > 0 ? s.Plays[s.Plays.Count - 1].Fired : "none")} signal={(s.SignalLoaded ? "loaded" : "NOT_LOADED")} verdict={(s.Pass ? "PASS" : "FAIL")} reason={s.Reason.Replace(' ', '_')}";
        }
    }
}
