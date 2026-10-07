using System;
using System.Collections.Generic;
using DDrive.Editor.Migration;
using Newtonsoft.Json.Linq;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.3 MCP-6(2026-10-07) — ddrive_migrate。
    // plan = DDriveMigrationRunner.PlanProject(読み取り専用)、apply = ApplyToProject(1 つの Undo グループ)。
    // Destructive なので isuzu が confirm を要求する(plan も)。plan は書き込み許可が無くても使える(読み取りだけなので)。
    public static class DDriveMigrateTools
    {
        public const string ModePlan = "plan";
        public const string ModeApply = "apply";

        // 計画 1 行(マイグレーション 1 個分)。
        public readonly struct PendingRow
        {
            public readonly string Id;
            public readonly string Kind; // "data" | "project"
            public readonly int Targets;
            public readonly string Description;

            public PendingRow(string id, string kind, int targets, string description = null)
            {
                Id = id;
                Kind = kind;
                Targets = targets;
                Description = description;
            }
        }

        // SchemaVersion を Current へ刻印するだけの Data(適用すべき IDataMigration が無いもの)の仮の ID。
        public const string SchemaStampId = "schema-stamp";

        [McpTool(
            "ddrive_migrate",
            "Data / プロジェクトのマイグレーション。plan=未適用の一覧、apply=適用(Undo 1 回分)",
            Destructive = true,
            UndoGroup = "D-Drive MCP: マイグレーション",
            Group = "build")]
        [McpReturns("pending", "count", "applied", "failed", "after", "warnings", "log")]
        public static JObject Migrate(
            [McpArg("mode", "plan(既定。読み取りだけ) / apply(適用。書き込み許可が要る)")]
            string mode = null)
        {
            return McpGuard.Run(() =>
            {
                var parsed = ParseMode(mode);
                if (parsed == ModeApply)
                {
                    McpGuard.EnsureCanWrite();
                    return ApplyJson(Apply());
                }

                return PlanJson(Rows(DDriveMigrationRunner.PlanProject()));
            });
        }

        public static string ParseMode(string mode)
        {
            var key = string.IsNullOrWhiteSpace(mode) ? ModePlan : mode.Trim().ToLowerInvariant();
            if (key != ModePlan && key != ModeApply)
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, $"mode '{mode}' は未知です。plan / apply のいずれか");
            }

            return key;
        }

        // 計画を「マイグレーション Id ごとの対象数」の行に畳む(Data は Id ごとに集計、プロジェクトは 1 行 = 1 件)。
        public static List<PendingRow> Rows(DDriveMigrationRunner.MigrationPlan plan)
        {
            var rows = new List<PendingRow>();
            if (plan == null)
            {
                return rows;
            }

            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var planned in plan.DataMigrations)
            {
                var id = planned.Migration.Id;
                if (!counts.ContainsKey(id))
                {
                    counts[id] = 0;
                    order.Add(id);
                }

                counts[id]++;
            }

            foreach (var id in order)
            {
                rows.Add(new PendingRow(id, "data", counts[id]));
            }

            if (plan.SchemaStampOnly.Count > 0)
            {
                rows.Add(new PendingRow(SchemaStampId, "data", plan.SchemaStampOnly.Count, "SchemaVersion を現行へ刻印するだけ"));
            }

            foreach (var project in plan.ProjectMigrations)
            {
                rows.Add(new PendingRow(project.Id, "project", 1));
            }

            return rows;
        }

        public static JObject PlanJson(IReadOnlyList<PendingRow> rows)
        {
            var pending = new JArray();
            foreach (var row in rows)
            {
                pending.Add(RowJson(row));
            }

            return new JObject { ["pending"] = pending, ["count"] = rows.Count };
        }

        private static JObject RowJson(PendingRow row)
        {
            return McpJson.Obj(
                ("id", row.Id),
                ("kind", row.Kind),
                ("targets", row.Targets),
                ("description", row.Description));
        }

        // apply の結果(純粋なデータ。ApplyJson が JSON にする)。
        public sealed class ApplyOutcome
        {
            public readonly List<PendingRow> Applied = new List<PendingRow>();
            public readonly List<(string id, string msg)> Failed = new List<(string id, string msg)>();
            public int AfterPending;
            public int Warnings;
            public List<string> Log = new List<string>();
        }

        private const int MaxLogLines = 20;

        private static ApplyOutcome Apply()
        {
            var outcome = new ApplyOutcome();
            var before = Rows(DDriveMigrationRunner.PlanProject());
            var context = DDriveMigrationRunner.ApplyToProject();
            var afterPlan = DDriveMigrationRunner.PlanProject();
            var after = Rows(afterPlan);
            outcome.AfterPending = afterPlan.TotalCount;
            outcome.Warnings = context.WarningCount;
            for (var i = 0; i < context.Log.Count && i < MaxLogLines; i++)
            {
                outcome.Log.Add(context.Log[i]);
            }

            // Runner は Id ごとの成否を返さないので、適用前の計画のうち適用後も残っているものを failed とする。
            var stillPending = new HashSet<string>();
            foreach (var row in after)
            {
                stillPending.Add(row.Id);
            }

            foreach (var row in before)
            {
                if (stillPending.Contains(row.Id))
                {
                    outcome.Failed.Add((row.Id, "適用後も未適用のまま(警告のログを確認してください)"));
                }
                else
                {
                    outcome.Applied.Add(row);
                }
            }

            return outcome;
        }

        public static JObject ApplyJson(ApplyOutcome outcome)
        {
            var applied = new JArray();
            foreach (var row in outcome.Applied)
            {
                applied.Add(new JObject { ["id"] = row.Id, ["targets"] = row.Targets });
            }

            var failed = new JArray();
            foreach (var (id, msg) in outcome.Failed)
            {
                failed.Add(new JObject { ["id"] = id, ["msg"] = msg });
            }

            var result = new JObject
            {
                ["applied"] = applied,
                ["failed"] = failed,
                ["after"] = new JObject { ["pending"] = outcome.AfterPending },
            };
            if (outcome.Warnings > 0)
            {
                result["warnings"] = outcome.Warnings;
                result["log"] = new JArray(outcome.Log);
            }

            return result;
        }
    }
}
