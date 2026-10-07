using System;
using System.Collections.Generic;
using DDrive.Editor.Dependencies;
using DDrive.Editor.Inspector;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.2 / §5.2 MCP-4(2026-10-07) — ddrive_asset_usages / unused / delete / ddrive_editor_open。
    // ツールはアダプタに徹する。参照の検索は DependencyGraphService、削除は SafeDeleteService.TryDelete、
    // コード参照は CodeReferenceScan、エディタを開くのは DataEditorRegistry を呼ぶだけ。
    //  - 依存グラフが未構築(CachedFileCount == 0。UsagesWindow / UnusedAssetsWindow と同じ判定)なら
    //    例外にせず {needsRebuild:true, hint:"ddrive_generate target=deps"} を返す
    //  - ID は MCP-3 と同じ 10 進文字列。type / id の解決は DDriveAssetTools.Locate を使う
    public static class DDriveDependencyTools
    {
        public const string RebuildHint = "ddrive_generate target=deps";

        // 一覧に出す blockers / codeRefs の最大件数(超えた分は件数だけ)。返り値を小さく保つため。
        public const int MaxListed = 20;

        private const string TypeArgText = "種別名(Se,Bgm,Vfx,Anim,Anim2D,Material,Texture,Canvas,Prefab,Presentation,Shake,Haptics,UiTween,Model,Anchor,AnchorGroup,ControlSkin,Cutscene)";

        // テスト専用: 依存グラフが「構築済みか」を差し替える(null なら CachedFileCount > 0 を見る)。
        public static Func<bool> GraphBuiltOverride;

        public static bool IsGraphBuilt()
            => GraphBuiltOverride != null ? GraphBuiltOverride() : DependencyGraphService.CachedFileCount > 0;

        public static JObject NeedsRebuild()
            => McpJson.Obj(("needsRebuild", true), ("hint", RebuildHint));

        // ── ddrive_asset_usages ──

        [McpTool(
            "ddrive_asset_usages",
            "Data を参照している場所(Data / Prefab / Scene / Timeline)の一覧。削除前の確認に",
            Idempotency = McpIdempotency.Safe,
            Group = "diagnostics")]
        public static JObject Usages(
            [McpArg("type", TypeArgText, Required = true)]
            string type,
            [McpArg("id", "ID(10 進文字列か 0x 16 進)", Required = true)]
            string id,
            [McpArg("cursor", "続き(返り値の next をそのまま渡す)")]
            string cursor = null,
            [McpArg("limit", "最大件数(既定 50、最大 200)")]
            int limit = 0,
            [McpArg("max_chars", "返り値の最大文字数(既定 4000)")]
            int max_chars = 0)
        {
            return McpGuard.Run(() =>
            {
                var entry = FieldTables.RequireType(type);
                var asset = DDriveAssetTools.Locate(entry, id, null);
                if (!IsGraphBuilt())
                {
                    return NeedsRebuild();
                }

                var usages = DependencyGraphService.FindUsages(entry.Type, asset.Id);
                return UsagesJson(usages, cursor, limit, max_chars);
            });
        }

        // 参照元の一覧 → {count, usages, next?, truncated?}。グラフに依らない純粋関数(合成データでテストする)。
        public static JObject UsagesJson(IReadOnlyList<DependencyReference> usages, string cursor, int limit, int maxChars)
        {
            var head = new JObject { ["count"] = usages.Count };
            var result = ItemPaging.Fit(head, usages, cursor, limit, maxChars, MapUsage);
            var items = result.Property("items");
            items.Replace(new JProperty("usages", items.Value));
            return result;
        }

        public static JObject MapUsage(DependencyReference usage)
            => McpJson.Obj(
                ("path", usage.SourcePath),
                ("objectPath", string.IsNullOrEmpty(usage.ObjectPath) ? null : usage.ObjectPath),
                ("kind", KindOf(usage)));

        // 参照元が何か。.unity / .prefab / .playable はそのまま、.asset(Data)は Data 型名(ComponentType)。
        public static string KindOf(DependencyReference usage)
        {
            switch (ClassifiedReference.ClassifyPath(usage.SourcePath))
            {
                case ReferenceFileKind.Scene:
                    return "scene";
                case ReferenceFileKind.Prefab:
                    return "prefab";
                case ReferenceFileKind.Timeline:
                    return "playable";
                case ReferenceFileKind.Data:
                    return string.IsNullOrEmpty(usage.ComponentType) ? "data" : usage.ComponentType;
                default:
                    return null;
            }
        }

        // ── ddrive_asset_unused ──

        [McpTool(
            "ddrive_asset_unused",
            "どこからも参照されていない Data の一覧(削除候補)。archived は Archived タグ付き",
            Idempotency = McpIdempotency.Safe,
            Group = "diagnostics")]
        public static JObject Unused(
            [McpArg("type", "種別名で絞る。省略で全種別")]
            string type = null,
            [McpArg("cursor", "続き(返り値の next をそのまま渡す)")]
            string cursor = null,
            [McpArg("limit", "最大件数(既定 50、最大 200)")]
            int limit = 0,
            [McpArg("max_chars", "返り値の最大文字数(既定 4000)")]
            int max_chars = 0)
        {
            return McpGuard.Run(() =>
            {
                AssetType? only = null;
                if (!string.IsNullOrWhiteSpace(type))
                {
                    only = FieldTables.RequireType(type).Type;
                }

                if (!IsGraphBuilt())
                {
                    return NeedsRebuild();
                }

                // UnusedAssetsWindow と同じく Archived のものも含めて出す(除外しない)。印だけ付ける。
                var rows = new List<UnusedAssetId>();
                foreach (var info in DependencyGraphService.FindUnusedIds())
                {
                    if (only == null || info.Type == only.Value)
                    {
                        rows.Add(info);
                    }
                }

                rows.Sort((a, b) => string.CompareOrdinal(a.AssetPath, b.AssetPath));
                return UnusedJson(rows, cursor, limit, max_chars, path =>
                    ArchiveTagService.IsArchived(AssetDatabase.LoadAssetAtPath<AssetDataBase>(path)));
            });
        }

        public static JObject UnusedJson(
            IReadOnlyList<UnusedAssetId> rows, string cursor, int limit, int maxChars, Func<string, bool> isArchived)
        {
            var head = new JObject { ["count"] = rows.Count };
            return ItemPaging.Fit(head, rows, cursor, limit, maxChars, info => McpJson.Obj(
                ("type", info.Type.ToString()),
                ("id", McpJson.FormatId(info.Id)),
                ("name", info.DisplayName),
                ("archived", isArchived != null && isArchived(info.AssetPath))));
        }

        // ── ddrive_asset_delete ──

        // confirm / dry_run は isuzu の ToolInvoker が Destructive の呼び出しにだけ注入し、メソッドには渡さない
        // (confirm 無しは confirmation_required)。読み取り専用の確認は自前の引数 preview(MCP-5 と同じ)。
        [McpTool(
            "ddrive_asset_delete",
            "Data を安全に削除(ゴミ箱へ。参照・コード参照があれば拒否)。先に preview=true で確認",
            Destructive = true,
            UndoGroup = "D-Drive MCP: Data の削除",
            Group = "authoring")]
        public static JObject Delete(
            [McpArg("type", TypeArgText, Required = true)]
            string type,
            [McpArg("id", "ID(10 進文字列か 0x 16 進)", Required = true)]
            string id,
            [McpArg("preview", "true なら削除せず、削除した場合の結果と blockers だけ返す")]
            bool preview = false,
            [McpArg("scan_code", "true(既定)なら .cs 内の定数参照も検査し、あれば拒否する")]
            bool scan_code = true)
        {
            return McpGuard.Run(() =>
            {
                McpGuard.EnsureCanWrite();
                var entry = FieldTables.RequireType(type);
                var asset = DDriveAssetTools.Locate(entry, id, null);
                if (!IsGraphBuilt())
                {
                    return NeedsRebuild();
                }

                var path = AssetDatabase.GetAssetPath(asset);
                var plan = Analyze(asset, entry.Type, path, scan_code);
                if (preview)
                {
                    return PreviewJson(plan);
                }

                if (plan.IsBlocked)
                {
                    return ResultJson(plan, false);
                }

                return ExecuteDelete(asset, entry.Type, path, plan);
            });
        }

        public sealed class DeletePlan
        {
            public string Path;
            public List<DependencyReference> Blockers = new List<DependencyReference>();
            public List<CodeReferenceScan.Hit> CodeRefs = new List<CodeReferenceScan.Hit>();

            public bool IsBlocked => Blockers.Count > 0 || CodeRefs.Count > 0;
        }

        // 読み取り専用の分析。SafeDeleteService.TryDelete が削除前に見るもの(参照 = FindUsages、
        // コード参照 = CodeReferenceScan)と同じ。Archived タグも付けない。
        public static DeletePlan Analyze(AssetDataBase asset, AssetType type, string path, bool scanCode)
        {
            var plan = new DeletePlan { Path = path };
            plan.Blockers.AddRange(DependencyGraphService.FindUsages(type, asset.Id));
            if (scanCode)
            {
                plan.CodeRefs.AddRange(CodeReferenceScan.FindPossibleReferenceHits(asset, path));
            }

            return plan;
        }

        public static JObject PreviewJson(DeletePlan plan)
        {
            var result = ResultJson(plan, null);
            result["wouldDelete"] = plan.Path;
            // 拒否される(blockers / codeRefs がある)ときの印。wouldDelete だけ見て実行しないように。
            if (plan.IsBlocked)
            {
                result["blocked"] = true;
            }

            return result;
        }

        // {deleted?, path, blockers?, blockerCount?, codeRefs?}
        public static JObject ResultJson(DeletePlan plan, bool? deleted)
        {
            var result = new JObject();
            if (deleted.HasValue)
            {
                result["deleted"] = deleted.Value;
            }

            result["path"] = plan.Path;
            var blockers = new JArray();
            foreach (var usage in plan.Blockers)
            {
                if (blockers.Count >= MaxListed)
                {
                    break;
                }

                blockers.Add(McpJson.Obj(
                    ("kind", KindOf(usage) ?? "other"),
                    ("path", usage.SourcePath),
                    ("objectPath", string.IsNullOrEmpty(usage.ObjectPath) ? null : usage.ObjectPath)));
            }

            if (blockers.Count > 0)
            {
                result["blockers"] = blockers;
                if (plan.Blockers.Count > blockers.Count)
                {
                    result["blockerCount"] = plan.Blockers.Count;
                }
            }

            var codeRefs = new JArray();
            foreach (var hit in plan.CodeRefs)
            {
                if (codeRefs.Count >= MaxListed)
                {
                    break;
                }

                codeRefs.Add(new JObject { ["file"] = ToProjectRelative(hit.RelativePath), ["line"] = hit.Line });
            }

            if (codeRefs.Count > 0)
            {
                result["codeRefs"] = codeRefs;
            }

            return result;
        }

        // CodeReferenceScan.Hit はパッケージ内のファイルを絶対パスで返すことがあるので、プロジェクト相対にそろえる。
        public static string ToProjectRelative(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            var normalized = path.Replace('\\', '/');
            var root = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(root) && normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(root.Length + 1);
            }

            return normalized;
        }

        // 分析で blockers / codeRefs が無いと確かめたあとの実削除。ダイアログは自動で答える
        // (確認 = 続行、情報 = 握りつぶす)。元の差し替え先は finally で戻す。
        private static JObject ExecuteDelete(AssetDataBase asset, AssetType type, string path, DeletePlan plan)
        {
            var oldConfirm = SafeDeleteService.ConfirmDialogOverride;
            var oldInfo = SafeDeleteService.InfoDialogOverride;
            SafeDeleteService.ConfirmDialogOverride = (_, _, _, _) => true;
            SafeDeleteService.InfoDialogOverride = (_, _, _) => { };
            SafeDeleteService.DeleteReport report;
            try
            {
                // コード参照は上で検査済み(scan_code=false ならユーザーが不要と指示した)ので、ここでは再走査しない。
                report = SafeDeleteService.TryDelete(asset, type, requireGraphBuilt: true, scanCodeReferences: false);
            }
            finally
            {
                SafeDeleteService.ConfirmDialogOverride = oldConfirm;
                SafeDeleteService.InfoDialogOverride = oldInfo;
            }

            switch (report.Outcome)
            {
                case SafeDeleteService.DeleteOutcome.Deleted:
                    return new JObject { ["deleted"] = true, ["path"] = path };
                case SafeDeleteService.DeleteOutcome.GraphNotBuilt:
                    return NeedsRebuild();
                case SafeDeleteService.DeleteOutcome.BlockedByUsages:
                    plan.Blockers.Clear();
                    plan.Blockers.AddRange(report.BlockingUsages);
                    return ResultJson(plan, false);
                default:
                    return ResultJson(plan, false);
            }
        }

        // ── ddrive_editor_open ──

        [McpTool(
            "ddrive_editor_open",
            "Data の専用エディタを Editor で開く(人が見るため)。無ければ Inspector で選択する",
            Idempotency = McpIdempotency.Safe,
            Group = "authoring")]
        public static JObject EditorOpen(
            [McpArg("type", TypeArgText, Required = true)]
            string type,
            [McpArg("id", "ID(10 進文字列か 0x 16 進)", Required = true)]
            string id)
        {
            return McpGuard.Run(() =>
            {
                var entry = FieldTables.RequireType(type);
                var asset = DDriveAssetTools.Locate(entry, id, null);
                return Open(asset);
            });
        }

        // 専用エディタがあれば主エディタ(Order 最小 = OpenDefault と同じ)を開き、無ければ選択 + Ping。モーダルは出さない。
        public static JObject Open(AssetDataBase asset)
        {
            if (DataEditorRegistry.TryGetPrimary(asset.GetType(), out var primary))
            {
                primary.Open(asset);
                return new JObject { ["opened"] = primary.WindowType.Name };
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            return McpJson.Obj(("opened", McpJson.Keep(null)), ("inspector", true));
        }
    }
}
