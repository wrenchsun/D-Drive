using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DDrive.Editor.Settings;
using DDrive.Editor.Setup;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Update
{
    // [1002_ddrive_mcp.md] §11 MCP-14(2026-10-07) — isuzu MCP の導入の配線(更新ウィンドウとセットアップウィザードが共有する)。
    // 判断のロジックは純関数 `McpPackageSupport` に置き、ここは「ダイアログ → manifest 保存 → Resolve → 管理対象登録 →
    // 導入後の案内パネル」の副作用だけを持つ。manifest の書き換えは**ユーザーがボタンを押したときだけ**(自動では書かない)。
    public static class McpInstallActions
    {
        // 導入の入口。キャンセル・失敗のときは null(何も変えない)。成功したら適用した計画を返す。
        public static McpPackageSupport.McpPlan? InstallFromUserClick()
        {
            var manifest = ManifestJson.LoadProjectManifest();
            if (manifest == null)
            {
                UnityEngine.Debug.LogWarning("[DDrive][MCP] Packages/manifest.json が読めませんでした。");
                return null;
            }

            var scan = McpPackageSupport.ScanManifest(manifest);
            var choice = AskChoice(scan);
            var plan = McpPackageSupport.BuildPlan(scan, choice);
            if (plan.Cancelled)
            {
                return null;
            }

            if (plan.ChangesManifest)
            {
                McpPackageSupport.ApplyToManifest(manifest, plan);
                ManifestJson.SaveProjectManifest(manifest);
                try
                {
                    Client.Resolve();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[DDrive][MCP] Client.Resolve に失敗しました(Unity を開き直すと解決されます): " + e.Message);
                }
            }

            if (plan.ManagedPackageId != null)
            {
                DDriveProjectSettings.instance.RegisterManagedPackage(plan.ManagedPackageId);
            }

            UnityEngine.Debug.Log($"[DDrive][MCP] {plan.AddId ?? "(導入済み)"} を導入しました(manifest 追加: {plan.AddValue ?? "なし"}、外した MCP: {(plan.RemoveIds.Count == 0 ? "なし" : string.Join(", ", plan.RemoveIds))})。");
            return plan;
        }

        // 他の MCP があれば確認ダイアログ。無ければ「そのまま導入」(KeepOthers = 何も外さない)。
        private static McpPackageSupport.McpChoice AskChoice(McpPackageSupport.McpScan scan)
        {
            var text = McpPackageSupport.BuildConfirmText(scan);
            if (text == null)
            {
                return McpPackageSupport.McpChoice.KeepOthers;
            }

            if (scan.HasRemovable)
            {
                var other = scan.Others.Find(o => o.Removable);
                var result = EditorUtility.DisplayDialogComplex(
                    "D-Drive: Unity MCP(isuzu)の導入",
                    text,
                    "続行(両方残す)",
                    $"{other.DisplayName ?? other.Id} を外して続行",
                    "キャンセル");
                return result switch
                {
                    0 => McpPackageSupport.McpChoice.KeepOthers,
                    1 => McpPackageSupport.McpChoice.RemoveRemovable,
                    _ => McpPackageSupport.McpChoice.Cancel,
                };
            }

            return EditorUtility.DisplayDialog("D-Drive: Unity MCP(isuzu)の導入", text, "続行(両方残す)", "キャンセル")
                ? McpPackageSupport.McpChoice.KeepOthers
                : McpPackageSupport.McpChoice.Cancel;
        }

        // ── 登録スクリプト ──

        public static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        // パッケージ同梱(Tools~/Mcp)→ 無ければ開発リポジトリ直下(Tools/Mcp)。見つからなければ null。
        public static string ResolveRegisterScript()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpPackageSupport).Assembly);
            return ResolveRegisterScript(info?.resolvedPath, ProjectRoot());
        }

        public static string ResolveRegisterScript(string packageRoot, string projectRoot)
        {
            if (!string.IsNullOrEmpty(packageRoot))
            {
                var bundled = Path.Combine(packageRoot, "Tools~", "Mcp", "register-mcp.ps1");
                if (File.Exists(bundled))
                {
                    return bundled;
                }
            }

            if (!string.IsNullOrEmpty(projectRoot))
            {
                var repo = Path.Combine(projectRoot, "Tools", "Mcp", "register-mcp.ps1");
                if (File.Exists(repo))
                {
                    return repo;
                }
            }

            return null;
        }

        // pwsh の起動情報(純粋な組み立て。実プロセスは起動しない)。-ProjectPath は必ず渡す
        // (パッケージ内から実行すると既定の ..\.. がプロジェクトルートにならないため)。
        public static ProcessStartInfo BuildRegisterStartInfo(string scriptPath, string projectRoot)
        {
            var info = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-File");
            info.ArgumentList.Add(scriptPath);
            info.ArgumentList.Add("-ProjectPath");
            info.ArgumentList.Add(projectRoot);
            return info;
        }

        public readonly struct RegisterResult
        {
            public readonly bool Started; // false: pwsh が無い等で起動できなかった
            public readonly int ExitCode;
            public readonly string Output;
            public readonly bool Cancelled;

            public RegisterResult(bool started, int exitCode, string output, bool cancelled)
            {
                Started = started;
                ExitCode = exitCode;
                Output = output;
                Cancelled = cancelled;
            }

            public bool Success => Started && !Cancelled && ExitCode == 0;
        }

        // バックグラウンドスレッドから呼べる(Unity API を使わない)。タイムアウト・キャンセルでツリーごと止める。例外で止めない。
        public static RegisterResult RunRegisterScript(string scriptPath, string projectRoot, int timeoutMs, CancellationToken cancellation)
        {
            try
            {
                using var process = new Process { StartInfo = BuildRegisterStartInfo(scriptPath, projectRoot) };
                var output = new StringBuilder();
                DataReceivedEventHandler append = (_, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (output)
                        {
                            output.AppendLine(e.Data);
                        }
                    }
                };
                process.OutputDataReceived += append;
                process.ErrorDataReceived += append;
                try
                {
                    if (!process.Start())
                    {
                        return new RegisterResult(false, -1, "pwsh を起動できませんでした。", false);
                    }
                }
                catch (Exception e)
                {
                    return new RegisterResult(false, -1, "pwsh を起動できませんでした(PowerShell 7 が PATH に必要です): " + e.Message, false);
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
                        GitProcess.KillTree(process);
                        return new RegisterResult(true, -1, Snapshot(output), true);
                    }

                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        GitProcess.KillTree(process);
                        return new RegisterResult(true, -1, Snapshot(output) + $"\nタイムアウトしました({timeoutMs / 1000} 秒)。", false);
                    }
                }

                Task.Run(() => process.WaitForExit()).Wait(3000);
                return new RegisterResult(true, process.ExitCode, Snapshot(output), false);
            }
            catch (Exception e)
            {
                return new RegisterResult(false, -1, "登録スクリプトの実行に失敗しました: " + e.Message, false);
            }
        }

        private static string Snapshot(StringBuilder sb)
        {
            lock (sb)
            {
                return sb.ToString();
            }
        }

        // 出力の末尾 maxLines 行(画面に出す分)。
        public static string TailLines(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var lines = text.Replace("\r", string.Empty).TrimEnd('\n').Split('\n');
            if (lines.Length <= maxLines)
            {
                return string.Join("\n", lines);
            }

            var tail = new List<string>(lines).GetRange(lines.Length - maxLines, maxLines);
            return "…\n" + string.Join("\n", tail);
        }

        // ── 導入後の案内パネル(B) ──

        // plan: 導入に使った計画(外した MCP の注意書きに使う)。1 画面に 3 つの手順を出す。
        public static VisualElement BuildPostInstallPanel(McpPackageSupport.McpPlan plan)
        {
            var root = new VisualElement
            {
                style =
                {
                    marginTop = 4,
                    marginBottom = 4,
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 4,
                    paddingBottom = 4,
                    backgroundColor = new Color(0.25f, 0.45f, 0.75f, 0.2f),
                },
            };
            root.Add(Wrap("Unity MCP(isuzu)を導入しました。続けて次の 3 つを行ってください。", true));

            root.Add(Wrap("1. Unity の Package Manager が解決するのを待つ(または Unity を開き直す)。Console にエラーが出ず、Project Settings に isuzu の MCP 設定が出たら完了です。", false));

            root.Add(Wrap("2. Claude Code に登録する(Unity の解決が終わったあと)。", false));
            var script = ResolveRegisterScript();
            var projectRoot = ProjectRoot();
            var manual = McpPackageSupport.BuildManualRegisterCommand(projectRoot.Replace('\\', '/'));
            var output = Wrap(string.Empty, false);
            output.style.display = DisplayStyle.None;
            output.selection.isSelectable = true;
            var runButton = new Button { text = "登録スクリプトを実行" };
            CancellationTokenSource cts = null;
            Task<RegisterResult> task = null;
            IVisualElementScheduledItem poll = null;
            runButton.clicked += () =>
            {
                if (task != null)
                {
                    return;
                }

                if (script == null)
                {
                    ShowOutput(output, "登録スクリプト(Tools~/Mcp/register-mcp.ps1)が見つかりません。手動で実行してください:\n" + manual);
                    return;
                }

                cts = new CancellationTokenSource();
                var token = cts.Token;
                runButton.SetEnabled(false);
                ShowOutput(output, "実行中…");
                task = Task.Run(() => RunRegisterScript(script, projectRoot, 60000, token));
                poll = root.schedule.Execute(() =>
                {
                    if (task == null || !task.IsCompleted)
                    {
                        return;
                    }

                    poll.Pause();
                    var result = task.Result;
                    task = null;
                    cts?.Dispose();
                    cts = null;
                    runButton.SetEnabled(true);
                    if (!result.Started)
                    {
                        ShowOutput(output, result.Output + "\n手動で実行してください:\n" + manual);
                    }
                    else
                    {
                        ShowOutput(output, (result.Success ? "登録しました。" : $"失敗しました(exit {result.ExitCode})。Unity の解決が終わっているか確認し、もう一度実行してください。") + "\n" + TailLines(result.Output, 12));
                    }
                }).Every(200);
            };
            root.Add(runButton);
            root.Add(output);
            root.Add(Wrap("手動で実行する場合: " + manual, false));

            root.Add(Wrap("3. 書き込みツール(Data の作成・変更・削除など)を使うなら許可を ON にする(既定は OFF。読み取りツールは許可なしで使えます)。", false));
            root.Add(new Button(() => SettingsService.OpenProjectSettings(McpSettingsProvider.SettingsPath)) { text = "書き込みツールの設定を開く" });

            if (plan.RemoveIds != null && plan.RemoveIds.Count > 0)
            {
                root.Add(Wrap($"外した MCP({string.Join("、", plan.RemoveIds)})の設定が `.mcp.json` などに残っている場合は、手で消してください(D-Drive は持ち込み先のファイルには触れません)。", false));
            }

            return root;
        }

        private static Label Wrap(string text, bool bold)
        {
            var label = new Label(text)
            {
                style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1, minWidth = 0, marginBottom = 4 },
            };
            if (bold)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            return label;
        }

        private static void ShowOutput(Label output, string text)
        {
            output.text = text;
            output.style.display = DisplayStyle.Flex;
        }
    }
}
