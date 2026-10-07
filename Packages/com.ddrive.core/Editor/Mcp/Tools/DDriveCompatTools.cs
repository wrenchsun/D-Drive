using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DDrive.Editor.Compat;
using Newtonsoft.Json.Linq;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.3 MCP-6(2026-10-07) — ddrive_compat(読み取り専用の差分)/ ddrive_compat_update(Destructive)。
    // 差分は Tests/Editor/Compat/*Tests.cs と同じビルダー(CompatSnapshotMenu.UpdateAll が書くのと同じ 6 種)で現在の文字列を作り、
    // 保存済みファイルと行集合で比べる。removed > 0 は互換性違反の疑い(MAJOR、docs/42 §5.12)。
    // 一時フィクスチャ依存の 2 種(tuning-codegen / validator-severity)はテスト側でしか作れないので対象外。
    public static class DDriveCompatTools
    {
        public const int SampleLines = 5;

        public sealed class Snapshot
        {
            public readonly string Name;
            public readonly string Path;
            public readonly Func<string> Build;

            public Snapshot(string name, string path, Func<string> build)
            {
                Name = name;
                Path = path;
                Build = build;
            }
        }

        // CompatSnapshotMenu.UpdateAll と同じ 6 種(順序も同じ)。
        public static IReadOnlyList<Snapshot> Snapshots { get; } = new List<Snapshot>
        {
            new Snapshot("public-api-DDrive.Foundation", CompatSnapshotPaths.PublicApiFoundation, () => PublicApiSnapshotBuilder.Build("DDrive.Foundation")),
            new Snapshot("public-api-DDrive.Runtime", CompatSnapshotPaths.PublicApiRuntime, () => PublicApiSnapshotBuilder.Build("DDrive.Runtime")),
            new Snapshot("serialized-layout", CompatSnapshotPaths.SerializedLayout, SerializedLayoutSnapshotBuilder.Build),
            new Snapshot("enums", CompatSnapshotPaths.Enums, SerializedEnumSnapshotBuilder.Build),
            new Snapshot("net-messages", CompatSnapshotPaths.NetMessages, NetMessageSnapshotBuilder.Build),
            new Snapshot("editor-contract", CompatSnapshotPaths.EditorContract, EditorContractSnapshotBuilder.Build),
        };

        public const string RemovedWarning = "removed 行は互換性違反の疑い(MAJOR。docs/42 §5.12)。意図した変更でなければ戻す";

        public sealed class DiffRow
        {
            public string Snapshot;
            public int Added;
            public int Removed;
            public List<string> Sample = new List<string>();
            public bool MissingFile;
        }

        [McpTool(
            "ddrive_compat",
            "互換性スナップショットと現在のコードの差分(added/removed)。読み取り専用",
            Idempotency = McpIdempotency.Safe,
            Group = "diagnostics")]
        public static JObject Compat()
        {
            return McpGuard.Run(() => DiffJson(DiffAll()));
        }

        [McpTool(
            "ddrive_compat_update",
            "互換性スナップショットを現在のコードで上書き(意図した追加のときだけ)",
            Destructive = true,
            Group = "build")]
        public static JObject CompatUpdate()
        {
            return McpGuard.Run(() =>
            {
                McpGuard.EnsureCanWrite();
                var before = DiffAll();
                CompatSnapshotMenu.UpdateAll();
                return UpdateJson(before);
            });
        }

        public static List<DiffRow> DiffAll()
        {
            var rows = new List<DiffRow>();
            foreach (var snapshot in Snapshots)
            {
                var saved = File.Exists(snapshot.Path) ? File.ReadAllText(snapshot.Path) : null;
                var row = Diff(snapshot.Name, saved, snapshot.Build());
                if (row.Added > 0 || row.Removed > 0 || row.MissingFile)
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        // 純粋関数(テスト用)。saved が null ならファイル無し(全行 added)。空行は無視、CRLF は LF に揃える。
        public static DiffRow Diff(string name, string saved, string current)
        {
            var savedLines = Lines(saved);
            var currentLines = Lines(current);
            var savedSet = new HashSet<string>(savedLines, StringComparer.Ordinal);
            var currentSet = new HashSet<string>(currentLines, StringComparer.Ordinal);

            var row = new DiffRow { Snapshot = name, MissingFile = saved == null };
            var removedSample = new List<string>();
            foreach (var line in savedLines)
            {
                if (!currentSet.Contains(line))
                {
                    row.Removed++;
                    removedSample.Add("-" + line);
                }
            }

            var addedSample = new List<string>();
            foreach (var line in currentLines)
            {
                if (!savedSet.Contains(line))
                {
                    row.Added++;
                    addedSample.Add("+" + line);
                }
            }

            // 互換性違反の疑いが大きい removed を先に出す。
            foreach (var line in removedSample)
            {
                if (row.Sample.Count >= SampleLines)
                {
                    break;
                }

                row.Sample.Add(line);
            }

            foreach (var line in addedSample)
            {
                if (row.Sample.Count >= SampleLines)
                {
                    break;
                }

                row.Sample.Add(line);
            }

            return row;
        }

        private static List<string> Lines(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            foreach (var line in Regex.Replace(text, "\r\n?", "\n").Split('\n'))
            {
                if (line.Length > 0)
                {
                    result.Add(line);
                }
            }

            return result;
        }

        public static JObject DiffJson(IReadOnlyList<DiffRow> rows)
        {
            var changed = new JArray();
            var anyRemoved = false;
            foreach (var row in rows)
            {
                anyRemoved |= row.Removed > 0;
                var item = new JObject
                {
                    ["snapshot"] = row.Snapshot,
                    ["added"] = row.Added,
                    ["removed"] = row.Removed,
                    ["sample"] = new JArray(row.Sample),
                };
                if (row.MissingFile)
                {
                    item["missingFile"] = true;
                }

                changed.Add(item);
            }

            var result = new JObject { ["ok"] = !anyRemoved, ["changed"] = changed };
            if (anyRemoved)
            {
                result["warning"] = RemovedWarning;
            }

            return result;
        }

        // before = 更新前の差分(= 今回書き換わるスナップショット)。
        public static JObject UpdateJson(IReadOnlyList<DiffRow> before)
        {
            var updated = new JArray();
            var anyRemoved = false;
            foreach (var row in before)
            {
                updated.Add(row.Snapshot);
                anyRemoved |= row.Removed > 0;
            }

            var result = new JObject
            {
                ["updated"] = updated,
                ["hint"] = "CHANGELOG の [Unreleased] 互換性節に追記する(何を・なぜ・MINOR/MAJOR)。tuning-codegen / validator-severity は対象外(環境変数 DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1 でテスト再実行)",
            };
            if (anyRemoved)
            {
                result["warning"] = RemovedWarning;
            }

            return result;
        }
    }
}
