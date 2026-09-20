using System;
using System.Diagnostics;
using System.Text;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — `IGitTagLister` の実配線(実 `git` CLI を
    // `System.Diagnostics.Process` で起動する)。EditMode テストの対象外(実プロセスに触れるため。
    // `UpdateStepsFactory` と同じ位置づけ)。例外で呼び出し元を止めない(CLAUDE.md §0-4): PATH に `git`
    // が無い・30 秒でタイムアウト・非 0 終了・例外のいずれも `warningMessage` を添えて null を返すだけにする。
    public sealed class GitCliTagLister : IGitTagLister
    {
        private const int TimeoutMs = 30000;

        public string ListTags(string repoUrl, out string warningMessage)
        {
            warningMessage = null;

            if (string.IsNullOrEmpty(repoUrl))
            {
                warningMessage = "リポジトリ URL を解決できませんでした。";
                return null;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "ls-remote --tags " + Quote(repoUrl),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = new Process { StartInfo = startInfo };

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                if (!process.Start())
                {
                    warningMessage = "git を起動できませんでした(PATH を確認してください)。";
                    return null;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(TimeoutMs))
                {
                    TryKill(process);
                    warningMessage = "git ls-remote がタイムアウトしました(30 秒)。";
                    return null;
                }

                // WaitForExit(int) 版はストリームの完全な読み切りを保証しないため、明示的に待つ
                // (Microsoft のドキュメント推奨パターン)。
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    var firstLine = FirstNonEmptyLine(stderr.ToString());
                    warningMessage = string.IsNullOrEmpty(firstLine)
                        ? $"git ls-remote が失敗しました(exit code {process.ExitCode})。"
                        : firstLine;
                    return null;
                }

                return stdout.ToString();
            }
            catch (Exception e)
            {
                warningMessage = "git ls-remote の実行に失敗しました: " + e.Message;
                return null;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // 例外で止めない(CLAUDE.md §0-4)。Kill 自体の失敗は無視する。
            }
        }

        private static string FirstNonEmptyLine(string text)
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

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
