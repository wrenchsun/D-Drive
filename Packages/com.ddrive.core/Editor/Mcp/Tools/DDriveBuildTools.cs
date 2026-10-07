using System;
using System.Diagnostics;
using System.IO;
using DDrive.Editor.Build;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.4 MCP-7(2026-10-07) — ddrive_build_netcheck。実機確認用 Windows ビルド(NetCheckBuilder.Build)。
    // ビルドは Editor のメインスレッドで同期実行する(1〜3 分。その間 Editor は固まる)。isuzu は長いメインスレッド処理を
    // 自動で「ジョブ」にして HTTP は先に jobId を返す(job_status で結果を取れる)ので、ツール側でジョブ管理は持たない。
    // 出力は Builds/DDriveNetCheck(.gitignore 済み)を置き換える。
    public static class DDriveBuildTools
    {
        // テスト専用: ビルド本体を差し替える(実ビルドを走らせない)。引数は development。
        public static Func<bool, NetCheckBuilder.BuildResult> BuildOverride;

        [McpTool(
            "ddrive_build_netcheck",
            "実機確認用 Windows ビルド(Builds/DDriveNetCheck を置換、1〜3 分)",
            Group = "build",
            MaxResultSizeChars = 2000)]
        [McpReturns("success", "exe", "zip", "error", "seconds")]
        public static JObject BuildNetCheck(
            [McpArg("development", "true(既定)=Development ビルド。false=リリース相当")]
            bool development = true)
        {
            return McpGuard.Run(() =>
            {
                McpGuard.EnsureCanWrite();
                var watch = Stopwatch.StartNew();
                var result = BuildOverride != null ? BuildOverride(development) : NetCheckBuilder.Build(development: development);
                watch.Stop();
                return ToJson(result, watch.Elapsed.TotalSeconds);
            });
        }

        // ビルド結果 → {success, exe?, zip?, error?, seconds}。パスはプロジェクト相対。
        public static JObject ToJson(NetCheckBuilder.BuildResult result, double seconds)
        {
            return McpJson.Obj(
                ("success", McpJson.Keep(result.Success)),
                ("exe", RelativeToProject(result.ExecutablePath)),
                ("zip", RelativeToProject(result.ZipPath)),
                ("error", string.IsNullOrEmpty(result.Error) ? null : result.Error),
                ("seconds", Math.Round(seconds, 1)));
        }

        private static string RelativeToProject(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName;
                return root == null ? path : Path.GetRelativePath(root, path).Replace('\\', '/');
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
