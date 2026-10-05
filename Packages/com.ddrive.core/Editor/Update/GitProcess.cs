using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03、レビュー PC-R-01/03/21) — `git` CLI を 1 回起動する共通の入口
    // (`GitCliTagLister` / `GitPackageJsonFetcher` が使う)。バックグラウンドスレッドから呼べる(Unity API を使わない)。
    //   ・引数は ArgumentList で 1 個ずつ渡す(引用符の組み立てをしない)。標準入力は閉じる(待ちにならない)。
    //   ・認証プロンプトで固まらない: GIT_TERMINAL_PROMPT=0 / GCM_INTERACTIVE=never(認証ヘルパー・SSH 鍵のキャッシュは従来どおり使われる)。
    //   ・タイムアウト・キャンセルでは、子プロセス(git-remote-https / ssh 等)ごとプロセスツリーを止める(Windows は taskkill /T、
    //     それ以外は子孫を pgrep -P で辿って kill -KILL + Kill)。例外で呼び出し元を止めない(CLAUDE.md §0-4)。
    //   ・実行中の git は台帳(Running)に持ち、ドメインリロード直前(AssemblyReloadEvents.beforeAssemblyReload)と Editor 終了時
    //     (EditorApplication.quitting)に止める(ワーカースレッドがドメインごと消えても孤児として残さない。FX-R-06)。
    public static class GitProcess
    {
        // 終了後に標準出力 / エラーの読み切りを待つ上限。git の子孫(ssh の ControlPersist 等)がパイプを掴んだままだと
        // 引数なしの WaitForExit() は無期限になるため(FX-R-05)。超えたら読めた分で結果を返す。
        private const int DrainTimeoutMs = 5000;

        private static readonly List<Process> Running = new();

        [InitializeOnLoadMethod]
        private static void RegisterShutdownHooks()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= KillAllRunning;
            AssemblyReloadEvents.beforeAssemblyReload += KillAllRunning;
            EditorApplication.quitting -= KillAllRunning;
            EditorApplication.quitting += KillAllRunning;
        }

        // 実行中の git をすべてツリーごと止める(ドメインリロード直前・Editor 終了時)。Unity API は使わない。
        public static void KillAllRunning()
        {
            Process[] snapshot;
            lock (Running)
            {
                snapshot = Running.ToArray();
            }

            foreach (var process in snapshot)
            {
                KillTree(process);
            }
        }

        // テスト用: 台帳に載っている実行中の git の数。
        public static int RunningCount
        {
            get
            {
                lock (Running)
                {
                    return Running.Count;
                }
            }
        }

        public readonly struct Outcome
        {
            public readonly bool Success;
            public readonly string Stdout;

            // ユーザーに見せる 1 行の理由(Success のときは null)。
            public readonly string Error;
            public readonly bool Cancelled;

            public Outcome(bool success, string stdout, string error, bool cancelled)
            {
                Success = success;
                Stdout = stdout;
                Error = error;
                Cancelled = cancelled;
            }
        }

        // git の起動情報(純粋な組み立て。実プロセスは起動しない)。git を呼ぶ経路はすべてこれを通る(`GitPackageJsonFetcher` の
        // clone / show、`GitCliTagLister` の ls-remote)。
        // 標準出力 / エラーは **UTF-8 で読む**(2026-10-06、P-15 確認 BUG-1)。`ProcessStartInfo` の既定は OS の既定コードページ
        // (日本語 Windows では Shift_JIS)で、`git show` が出す UTF-8 の package.json(description の日本語等)が文字化けして
        // JSON が壊れていた。git は blob のバイト列をそのまま出し(`git show` は変換しない)、エラーメッセージも UTF-8 で出す。
        // `core.quotepath` は diff / status 等のパス表示にだけ効き、本ツールが使う clone / show / ls-remote には関係しないので足さない。
        // `LC_ALL` も足さない(git のメッセージの言語はユーザーの設定に任せる。理由の 1 行は `ErrorLine` が `fatal:` / `error:` で
        // 見つけられなければ最初の非空行を使うので、日本語のメッセージでも読める)。BOM なしで扱う(非同期読み取りでは BOM が読み捨てられず先頭に残ることがあるので、package.json の解析側 `DdriveUpdateDeclaration.TryParse` が先頭の U+FEFF を取り除く)。
        public static ProcessStartInfo BuildStartInfo(string[] arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            };
            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GCM_INTERACTIVE"] = "never";
            startInfo.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
            return startInfo;
        }

        // maxStdoutChars: 標準出力の上限(超えたら失敗)。0 以下で無制限。
        public static Outcome Run(string[] arguments, string workingDirectory, int timeoutMs, CancellationToken cancellation, int maxStdoutChars = 0)
        {
            try
            {
                var startInfo = BuildStartInfo(arguments, workingDirectory);

                using var process = new Process { StartInfo = startInfo };
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var tooLarge = false;
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }

                    lock (stdout)
                    {
                        if (maxStdoutChars > 0 && stdout.Length > maxStdoutChars)
                        {
                            tooLarge = true;
                            return;
                        }

                        stdout.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (stderr)
                        {
                            stderr.AppendLine(e.Data);
                        }
                    }
                };

                if (!process.Start())
                {
                    return Fail("git を起動できませんでした(PATH を確認してください)。");
                }

                lock (Running)
                {
                    Running.Add(process);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception)
                {
                    // 閉じられなくても続行する。
                }

                var stopwatch = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        KillTree(process);
                        return new Outcome(false, null, "キャンセルしました。", true);
                    }

                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        KillTree(process);
                        return Fail($"git がタイムアウトしました({timeoutMs / 1000} 秒)。");
                    }
                }

                // WaitForExit(int) はストリームの読み切りを保証しないため、明示的に待つ(Microsoft 推奨パターン)。
                // ただし引数なしの WaitForExit() は、git の子孫がパイプを掴んだままだと無期限に止まる(FX-R-05)ので、上限を付ける。
                // 超えたら読めた分で結果を返す。この時点で git 本体は終了済みで、パイプを掴んでいる子孫(ssh の ControlPersist 等)は
                // 親を失って git の子ではなくなっているため、止められない(KillTree は HasExited で何もしないので呼ばない。FY-R-04)。
                // 待っていたスレッドプールのスレッドは、子孫がパイプを閉じた時点(または終了した時点)で自然に終わる。
                var drain = Task.Run(() => process.WaitForExit());
                drain.Wait(DrainTimeoutMs);

                if (process.ExitCode != 0)
                {
                    string text;
                    lock (stderr)
                    {
                        text = stderr.ToString();
                    }

                    return Fail(ErrorLine(text) ?? $"git が失敗しました(exit code {process.ExitCode})。");
                }

                if (tooLarge)
                {
                    return Fail("git の出力が大きすぎます。");
                }

                lock (stdout)
                {
                    return new Outcome(true, stdout.ToString(), null, false);
                }
            }
            catch (Exception e)
            {
                return Fail("git の実行に失敗しました: " + e.Message);
            }
            finally
            {
                lock (Running)
                {
                    Running.RemoveAll(p => p == null || HasExitedSafe(p));
                }
            }
        }

        private static bool HasExitedSafe(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (Exception)
            {
                return true; // 破棄済み・取得できない = 台帳から外してよい
            }
        }

        private static Outcome Fail(string message) => new(false, null, message, false);

        // プロセスツリーごと止める。Process.Kill() は git 本体だけで、子の git-remote-https / ssh が残る。
        public static void KillTree(Process process)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }

                var pid = process.Id;
                var windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
                var info = new ProcessStartInfo
                {
                    FileName = windows ? "taskkill" : "pkill",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                if (windows)
                {
                    info.ArgumentList.Add("/T");
                    info.ArgumentList.Add("/F");
                    info.ArgumentList.Add("/PID");
                    info.ArgumentList.Add(pid.ToString());
                }
                else
                {
                    // 直下の子だけでなく孫(git-remote-https → fetch-pack 等)も止める(FX-R-05)。子孫を先に集めてから KILL する。
                    KillDescendantsUnix(pid);
                }

                if (windows)
                {
                    try
                    {
                        using var killer = Process.Start(info);
                        killer?.WaitForExit(5000);
                    }
                    catch (Exception)
                    {
                        // taskkill が無い環境では Kill() にフォールバックする。
                    }
                }

                if (!process.HasExited)
                {
                    process.Kill();
                }

                process.WaitForExit(2000);
            }
            catch (Exception)
            {
                // 例外で止めない(CLAUDE.md §0-4)。
            }
        }

        private const int MaxDescendantDepth = 6;

        // Windows 以外: pid の子孫を pgrep -P で再帰的に集め、葉の側から kill -KILL する。pgrep / kill が無い環境では何もしない
        // (呼び出し側が Process.Kill() にフォールバックし、残った子孫の一時フォルダは次回の CleanupStale が消す)。
        private static void KillDescendantsUnix(int pid)
        {
            var found = new List<int>();
            CollectChildren(pid, 0, found);
            for (var i = found.Count - 1; i >= 0; i--)
            {
                RunQuiet("kill", "-KILL", found[i].ToString());
            }
        }

        private static void CollectChildren(int pid, int depth, List<int> found)
        {
            if (depth >= MaxDescendantDepth)
            {
                return;
            }

            var output = RunQuiet("pgrep", "-P", pid.ToString());
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            foreach (var token in output.Split('\n'))
            {
                if (int.TryParse(token.Trim(), out var child) && child > 0 && !found.Contains(child))
                {
                    found.Add(child);
                    CollectChildren(child, depth + 1, found);
                }
            }
        }

        private static string RunQuiet(string fileName, string a, string b)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                info.ArgumentList.Add(a);
                info.ArgumentList.Add(b);
                using var process = Process.Start(info);
                if (process == null)
                {
                    return null;
                }

                var text = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                return text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // 標準エラーから理由の 1 行を選ぶ(`fatal:` / `error:` で始まる最後の行。無ければ最初の非空行。
        // 先頭は「Cloning into …」のような進捗のことがある)。
        public static string ErrorLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            string found = null;
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim('\r', '\n', ' ');
                if (trimmed.StartsWith("fatal:", System.StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("error:", System.StringComparison.OrdinalIgnoreCase))
                {
                    found = trimmed;
                }
            }

            return found ?? FirstNonEmptyLine(text);
        }

        public static string FirstNonEmptyLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim('\r', '\n', ' ');
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }

            return null;
        }
    }
}
