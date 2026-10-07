using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DDrive.Editor.Update;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.3 / §9 Q-9 MCP-6(2026-10-07) — ddrive_release_check。
    // Tools/Release/check-release.ps1 を pwsh で起動して -Json の結果をそのまま返す薄いラッパー(Editor 内で再実装しない)。
    // リリースの判断・タグ・push は人の操作のまま(bump-version はツールにしない)。
    public static class DDriveReleaseTools
    {
        public const int TimeoutMs = 120_000;
        private const int DrainTimeoutMs = 5000;
        private const int RawMaxChars = 500;
        public const string ScriptRelativePath = "Tools/Release/check-release.ps1";

        private static readonly Regex SafeRef = new Regex(@"^[A-Za-z0-9_][A-Za-z0-9._/\-]*$", RegexOptions.Compiled);

        [McpTool(
            "ddrive_release_check",
            "リリース前の検査(check-release.ps1 -Json)。開発リポジトリ専用",
            Idempotency = McpIdempotency.Safe,
            Group = "build",
            Examples = new[] { "{\"guard_only\":true,\"base\":\"v1.4.1\"}" })]
        public static JObject ReleaseCheck(
            [McpArg("base", "比較対象のタグ/ブランチ(例 v1.4.1)。省略で origin/main")]
            string @base = null,
            [McpArg("guard_only", "true なら CHANGELOG ガードだけ(CI と同じ)")]
            bool guard_only = false)
        {
            return McpGuard.Run(() =>
            {
                var root = ResolveRepoRoot();
                var script = Path.Combine(root, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(script))
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, "開発リポジトリでのみ使える(" + ScriptRelativePath + " が無い)");
                }

                var args = BuildArguments(script, @base, guard_only);
                var (exitCode, stdout, stderr) = RunPwsh(args, root, TimeoutMs);
                return ParseOutput(stdout, stderr, exitCode);
            });
        }

        // 純粋関数(テスト用)。base は引数注入を避けるため英数 . _ / - だけ許し、先頭の - は拒否する。
        public static string[] BuildArguments(string scriptPath, string baseRef, bool guardOnly)
        {
            var args = new System.Collections.Generic.List<string>
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-Json",
            };
            if (!string.IsNullOrWhiteSpace(baseRef))
            {
                var trimmed = baseRef.Trim();
                if (!SafeRef.IsMatch(trimmed))
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, $"base '{baseRef}' は使えない文字を含みます(英数 . _ / - のみ)");
                }

                args.Add("-Base");
                args.Add(trimmed);
            }

            if (guardOnly)
            {
                args.Add("-GuardOnly");
            }

            return args.ToArray();
        }

        // 純粋関数(テスト用)。JSON が読めればそれを返し、読めなければ {ok:false, raw:<先頭 500 文字>}。
        public static JObject ParseOutput(string stdout, string stderr, int exitCode)
        {
            var text = (stdout ?? string.Empty).Trim();
            // JSON は 1 行で出る。余計な行(警告等)が前にあっても最後の '{' 始まりの行を読む。
            string candidate = null;
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    candidate = trimmed;
                }
            }

            if (candidate != null)
            {
                try
                {
                    var parsed = McpJson.Parse(candidate);
                    if (parsed != null && parsed["checks"] is JArray && parsed["ok"] != null)
                    {
                        return parsed;
                    }
                }
                catch (Exception)
                {
                    // JSON でなければ raw にフォールバックする。
                }
            }

            var raw = string.IsNullOrWhiteSpace(text) ? (stderr ?? string.Empty).Trim() : text;
            if (raw.Length > RawMaxChars)
            {
                raw = raw.Substring(0, RawMaxChars);
            }

            var result = new JObject { ["ok"] = false, ["raw"] = raw };
            if (exitCode != 0)
            {
                result["exitCode"] = exitCode;
            }

            return result;
        }

        // 埋め込みパッケージ(Packages/com.ddrive.core)の 2 つ上 = 開発リポジトリのルート。それ以外(git URL 参照等)は拒否。
        private static string ResolveRepoRoot()
        {
            var info = PackageInfo.FindForAssembly(typeof(DDriveReleaseTools).Assembly);
            if (info == null || info.source != PackageSource.Embedded)
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, "開発リポジトリでのみ使える(パッケージが埋め込みではない)");
            }

            var packageDir = Path.GetFullPath(info.resolvedPath);
            return Path.GetFullPath(Path.Combine(packageDir, "..", ".."));
        }

        private static (int exitCode, string stdout, string stderr) RunPwsh(string[] arguments, string workingDirectory, int timeoutMs)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            // 認証プロンプトで固まらない(GitProcess と同じ)。
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GCM_INTERACTIVE"] = "never";

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (stdout)
                    {
                        stdout.AppendLine(e.Data);
                    }
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

            try
            {
                process.Start();
            }
            catch (Win32Exception)
            {
                throw new McpToolError(McpGuard.CodeException, "pwsh が無い(PowerShell 7 を PATH に入れてください)");
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

            if (!process.WaitForExit(timeoutMs))
            {
                GitProcess.KillTree(process);
                throw new McpToolError(McpGuard.CodeException, $"check-release がタイムアウトしました({timeoutMs / 1000} 秒)");
            }

            // 標準出力の読み切りを待つ(子孫がパイプを掴んだままでも無期限にならないよう上限を付ける)。
            Task.Run(() => process.WaitForExit()).Wait(DrainTimeoutMs);

            string outText;
            string errText;
            lock (stdout)
            {
                outText = stdout.ToString();
            }

            lock (stderr)
            {
                errText = stderr.ToString();
            }

            return (process.ExitCode, outText, errText);
        }
    }
}
