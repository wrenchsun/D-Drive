using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — `IRemotePackageJsonFetcher` の実配線。
    // 上げ先のタグの package.json だけを、`git` の浅い sparse clone(`--depth 1 --filter=blob:none --sparse`)で
    // プロジェクトの `Temp/DDriveUpdate/<guid>/` に取って読む。ユーザーの git 認証(認証ヘルパー・SSH 鍵)を
    // そのまま使うので private リポジトリでも動く。全体 30 秒のタイムアウト。失敗しても例外は投げず
    // `warningMessage` を返すだけ(呼び出し側は「事前確認できなかった。更新後に確認します」と表示して続行できる)。
    // 実プロセスに触れるため EditMode テストの対象外(`GitCliTagLister` と同じ位置づけ)。
    public sealed class GitSparsePackageJsonFetcher : IRemotePackageJsonFetcher
    {
        private const int TotalTimeoutMs = 30000;

        public string FetchPackageJson(string cloneUrl, string subPath, string reference, out string warningMessage)
        {
            warningMessage = null;
            if (string.IsNullOrEmpty(cloneUrl) || string.IsNullOrEmpty(reference))
            {
                warningMessage = "リポジトリ URL または版を解決できませんでした。";
                return null;
            }

            string workDir = null;
            try
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                workDir = Path.Combine(projectRoot, "Temp", "DDriveUpdate", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(workDir));

                var deadline = DateTime.UtcNow.AddMilliseconds(TotalTimeoutMs);
                var cleanPath = string.IsNullOrEmpty(subPath) ? null : subPath.Replace('\\', '/').Trim('/');

                if (!Run(
                        "clone --depth 1 --filter=blob:none --sparse --no-tags --branch " + Quote(reference) + " " + Quote(cloneUrl) + " " + Quote(workDir),
                        null, deadline, out var error))
                {
                    warningMessage = error;
                    return null;
                }

                if (cleanPath != null
                    && !Run("sparse-checkout set " + Quote(cleanPath), workDir, deadline, out error))
                {
                    warningMessage = error;
                    return null;
                }

                var packageJson = Path.Combine(workDir, cleanPath ?? string.Empty, "package.json");
                if (!File.Exists(packageJson))
                {
                    warningMessage = "その版に package.json が見つかりませんでした。";
                    return null;
                }

                return File.ReadAllText(packageJson);
            }
            catch (Exception e)
            {
                warningMessage = "git の実行に失敗しました: " + e.Message;
                return null;
            }
            finally
            {
                TryDelete(workDir);
            }
        }

        // 全体の期限(deadline)までに終わらなければ強制終了する。
        private static bool Run(string arguments, string workingDirectory, DateTime deadline, out string error)
        {
            error = null;
            var remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                error = "git がタイムアウトしました(30 秒)。";
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            // 認証プロンプトで固まらないようにする(認証ヘルパー・SSH 鍵があれば従来どおり使われる)。
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

            using var process = new Process { StartInfo = startInfo };
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            if (!process.Start())
            {
                error = "git を起動できませんでした(PATH を確認してください)。";
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(remainingMs))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (Exception)
                {
                    // 例外で止めない(CLAUDE.md §0-4)。
                }

                error = "git がタイムアウトしました(30 秒)。";
                return false;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                error = FirstNonEmptyLine(stderr.ToString()) ?? $"git が失敗しました(exit code {process.ExitCode})。";
                return false;
            }

            return true;
        }

        private static void TryDelete(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return;
            }

            try
            {
                // .git 配下の読み取り専用ファイルで Delete が失敗するため属性を外してから消す。
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(dir, true);
            }
            catch (Exception)
            {
                // 一時フォルダの掃除失敗は無視する(Temp/ は Unity が管理する場所)。
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
