using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03、レビュー PC-R-01/03/21) — `git` CLI を 1 回起動する共通の入口
    // (`GitCliTagLister` / `GitPackageJsonFetcher` が使う)。バックグラウンドスレッドから呼べる(Unity API を使わない)。
    //   ・引数は ArgumentList で 1 個ずつ渡す(引用符の組み立てをしない)。標準入力は閉じる(待ちにならない)。
    //   ・認証プロンプトで固まらない: GIT_TERMINAL_PROMPT=0 / GCM_INTERACTIVE=never(認証ヘルパー・SSH 鍵のキャッシュは従来どおり使われる)。
    //   ・タイムアウト・キャンセルでは、子プロセス(git-remote-https / ssh 等)ごとプロセスツリーを止める(Windows は taskkill /T、
    //     それ以外は pkill -P + Kill)。例外で呼び出し元を止めない(CLAUDE.md §0-4)。
    public static class GitProcess
    {
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

        // maxStdoutChars: 標準出力の上限(超えたら失敗)。0 以下で無制限。
        public static Outcome Run(string[] arguments, string workingDirectory, int timeoutMs, CancellationToken cancellation, int maxStdoutChars = 0)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                };
                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    startInfo.WorkingDirectory = workingDirectory;
                }

                startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
                startInfo.Environment["GCM_INTERACTIVE"] = "never";
                startInfo.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";

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
                process.WaitForExit();

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
                    info.ArgumentList.Add("-KILL");
                    info.ArgumentList.Add("-P");
                    info.ArgumentList.Add(pid.ToString());
                }

                try
                {
                    using var killer = Process.Start(info);
                    killer?.WaitForExit(5000);
                }
                catch (Exception)
                {
                    // taskkill / pkill が無い環境では Kill() にフォールバックする。
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
