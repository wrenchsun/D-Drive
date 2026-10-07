using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using DDrive.Editor.Setup;
using Newtonsoft.Json.Linq;

namespace DDrive.Editor.Update
{
    // [1002_ddrive_mcp.md] §11 MCP-14(2026-10-07) — isuzu MCP の導入を更新ウィンドウ・セットアップウィザードに統合するための
    // 純関数(`manifest` は JObject で受け取り、実 manifest・実 PackageManager・`.mcp.json` には触れない)。
    // ウィンドウ側(UpdateWindow / ProjectSetupWizardWindow)は配線だけを持つ(P-15 と同じ分け方)。
    // D-Drive 本体の package.json の依存には isuzu を入れない(MCP は任意機能。§7)。
    public static class McpPackageSupport
    {
        public const string IsuzuPackageId = "jp.shiranui-isuzu.unity-mcp";

        // D-Drive が確認済みの推奨版(MCP-13 で v4.4.2)。上げるときは D-Drive 側で確認してから変える(isuzu の版上げに連動させない)。
        public const string RecommendedIsuzuRef = "v4.4.2";

        public const string IsuzuRepositoryUrl = "https://github.com/isuzu-shiranui/UnityMCP.git";

        public const string RecommendedIsuzuUrl = IsuzuRepositoryUrl + "?path=" + IsuzuPackageId + "#" + RecommendedIsuzuRef;

        public const string CoplayDevPackageId = "com.coplaydev.unity-mcp";

        public readonly struct KnownMcpPackage
        {
            public readonly string Id;
            public readonly string DisplayName;

            // 既定でポートを固定する(= 他の Editor / アプリと衝突しうる)か。
            public readonly bool FixedPort;

            // D-Drive が「外してよい」と表に書いたもの(manifest から 1 行消すだけ。未知のものは false)。
            public readonly bool Removable;

            public KnownMcpPackage(string id, string displayName, bool fixedPort, bool removable)
            {
                Id = id;
                DisplayName = displayName;
                FixedPort = fixedPort;
                Removable = removable;
            }
        }

        // 既知の MCP パッケージ(追加のみ)。
        public static readonly IReadOnlyList<KnownMcpPackage> KnownMcpPackages = new[]
        {
            new KnownMcpPackage(IsuzuPackageId, "Unity MCP(isuzu)", false, false),
            new KnownMcpPackage(CoplayDevPackageId, "CoplayDev Unity MCP", true, true),
        };

        public static bool TryGetKnown(string id, out KnownMcpPackage known)
        {
            for (var i = 0; i < KnownMcpPackages.Count; i++)
            {
                if (string.Equals(KnownMcpPackages[i].Id, id, StringComparison.Ordinal))
                {
                    known = KnownMcpPackages[i];
                    return true;
                }
            }

            known = default;
            return false;
        }

        // ── 走査 ──

        public sealed class OtherMcp
        {
            public string Id;
            public string DisplayName; // 既知のときだけ(未知は null)
            public bool Known;
            public bool Removable;
            public bool FixedPort;

            public string Label => DisplayName != null ? $"{DisplayName}({Id})" : Id;
        }

        public sealed class McpScan
        {
            public bool IsuzuInstalled;

            // manifest の値が git URL なら #ref、そうでなければ値そのもの(レジストリ版・file: 等)。未導入は null。
            public string IsuzuRef;

            // isuzu の ref が RecommendedIsuzuRef と同じか。
            public bool IsuzuIsRecommended;

            public List<OtherMcp> Others = new();

            public bool HasOthers => Others.Count > 0;

            public bool HasRemovable
            {
                get
                {
                    for (var i = 0; i < Others.Count; i++)
                    {
                        if (Others[i].Removable)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }

        // manifest.dependencies を走査する。未知 = id に "mcp" を含む(大小無視)isuzu 以外のもの。
        public static McpScan ScanManifest(JObject manifest)
        {
            var scan = new McpScan();
            if (manifest?[ManifestJson.DependenciesKey] is not JObject deps)
            {
                return scan;
            }

            foreach (var prop in deps.Properties())
            {
                var id = prop.Name;
                if (string.Equals(id, IsuzuPackageId, StringComparison.Ordinal))
                {
                    scan.IsuzuInstalled = true;
                    var value = prop.Value?.Type == JTokenType.String ? (string)prop.Value : null;
                    scan.IsuzuRef = ExtractRef(value);
                    scan.IsuzuIsRecommended = string.Equals(scan.IsuzuRef, RecommendedIsuzuRef, StringComparison.Ordinal);
                    continue;
                }

                if (TryGetKnown(id, out var known))
                {
                    scan.Others.Add(new OtherMcp { Id = id, DisplayName = known.DisplayName, Known = true, Removable = known.Removable, FixedPort = known.FixedPort });
                }
                else if (id.IndexOf("mcp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    scan.Others.Add(new OtherMcp { Id = id, Known = false, Removable = false, FixedPort = false });
                }
            }

            return scan;
        }

        private static string ExtractRef(string manifestValue)
        {
            if (string.IsNullOrEmpty(manifestValue))
            {
                return null;
            }

            var url = GitPackageUrl.Parse(manifestValue);
            return url.IsGitUrl ? (string.IsNullOrEmpty(url.Ref) ? null : url.Ref) : manifestValue;
        }

        // ── 推奨版との比較(DD-MCP-ISUZU-OUTDATED / 一覧の表示) ──

        public enum RefComparison
        {
            // タグ(vX.Y.Z / X.Y.Z)ではない(ブランチ・コミット・HEAD・未指定)。古いとは言わない。
            Unknown,
            Older,
            Same,
            Newer,
        }

        private static readonly Regex PlainVersion = new(@"^\d+\.\d+\.\d+", RegexOptions.Compiled);

        public static RefComparison CompareToRecommended(string installedRef) => Compare(installedRef, RecommendedIsuzuRef);

        public static RefComparison Compare(string installedRef, string recommendedRef)
        {
            if (!TryParseTag(installedRef, out var installed) || !TryParseTag(recommendedRef, out var recommended))
            {
                return RefComparison.Unknown;
            }

            var cmp = installed.CompareTo(recommended);
            return cmp < 0 ? RefComparison.Older : cmp > 0 ? RefComparison.Newer : RefComparison.Same;
        }

        private static bool TryParseTag(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var t = text.Trim();
            if (t.Length > 1 && (t[0] == 'v' || t[0] == 'V'))
            {
                return SemVer.TryParse(t.Substring(1), out version);
            }

            return PlainVersion.IsMatch(t) && SemVer.TryParse(t, out version);
        }

        // ── 計画 ──

        public enum McpChoice
        {
            // 他の MCP を残して導入する。
            KeepOthers,

            // 外してよい既知の MCP(Removable)だけを manifest から外して導入する。
            RemoveRemovable,

            // 何もしない。
            Cancel,
        }

        public readonly struct McpPlan
        {
            public readonly bool Cancelled;

            // 追加する依存(isuzu が既に manifest にあるときは null)。
            public readonly string AddId;
            public readonly string AddValue;

            // manifest から外す依存(Removable のみ。未知のものは決して入らない)。
            public readonly IReadOnlyList<string> RemoveIds;

            // 残す他の MCP(Validation DD-MCP-MULTIPLE の対象になる)。
            public readonly IReadOnlyList<string> KeptIds;

            // P-15 の管理対象(ManagedPackages)に登録するパッケージ ID(Cancel のときは null)。
            public readonly string ManagedPackageId;

            public McpPlan(bool cancelled, string addId, string addValue, IReadOnlyList<string> removeIds, IReadOnlyList<string> keptIds, string managedPackageId)
            {
                Cancelled = cancelled;
                AddId = addId;
                AddValue = addValue;
                RemoveIds = removeIds;
                KeptIds = keptIds;
                ManagedPackageId = managedPackageId;
            }

            public bool ChangesManifest => AddId != null || RemoveIds.Count > 0;
        }

        public static McpPlan BuildPlan(McpScan scan, McpChoice choice)
        {
            var none = Array.Empty<string>();
            if (choice == McpChoice.Cancel || scan == null)
            {
                return new McpPlan(true, null, null, none, none, null);
            }

            var remove = new List<string>();
            var kept = new List<string>();
            foreach (var other in scan.Others)
            {
                if (choice == McpChoice.RemoveRemovable && other.Removable)
                {
                    remove.Add(other.Id);
                }
                else
                {
                    kept.Add(other.Id);
                }
            }

            return scan.IsuzuInstalled
                ? new McpPlan(false, null, null, remove, kept, IsuzuPackageId)
                : new McpPlan(false, IsuzuPackageId, RecommendedIsuzuUrl, remove, kept, IsuzuPackageId);
        }

        // 計画を manifest(メモリ上)に適用する。追加する 1 行と外す行以外は変えない。`.mcp.json` 等には触れない。
        public static void ApplyToManifest(JObject manifest, McpPlan plan)
        {
            if (manifest == null || plan.Cancelled)
            {
                return;
            }

            if (plan.AddId != null)
            {
                ManifestJson.SetDependency(manifest, plan.AddId, plan.AddValue);
            }

            if (manifest[ManifestJson.DependenciesKey] is JObject deps)
            {
                foreach (var id in plan.RemoveIds)
                {
                    deps.Remove(id);
                }
            }
        }

        // ── 確認ダイアログの文面 ──

        // 他の MCP が無ければ null(ダイアログを出さない)。
        public static string BuildConfirmText(McpScan scan)
        {
            if (scan == null || !scan.HasOthers)
            {
                return null;
            }

            var names = new StringBuilder();
            for (var i = 0; i < scan.Others.Count; i++)
            {
                if (i > 0)
                {
                    names.Append("、");
                }

                names.Append(scan.Others[i].Label);
            }

            var sb = new StringBuilder();
            sb.Append("他の MCP(").Append(names).Append(")が入っています。\n\n");
            sb.Append("2 つの MCP は同じ Editor を同時に操作でき、固定ポートのものは衝突の元です。\n");
            if (scan.HasRemovable)
            {
                sb.Append("「外して続行」は manifest.json から該当の 1 行を消すだけで、`.mcp.json` などに残っている設定は消しません(手で消してください)。");
                var unknown = new List<string>();
                foreach (var other in scan.Others)
                {
                    if (!other.Removable)
                    {
                        unknown.Add(other.Id);
                    }
                }

                if (unknown.Count > 0)
                {
                    sb.Append("\n外せないもの(").Append(string.Join("、", unknown)).Append(")は残ります。");
                }
            }
            else
            {
                sb.Append("このパッケージは D-Drive では外しません。両方残して導入するか、キャンセルしてください。");
            }

            return sb.ToString();
        }

        // 導入後の画面に出す手動コマンド(pwsh が無い・実行に失敗したときの案内)。
        public static string BuildManualRegisterCommand(string projectRoot)
            => string.IsNullOrEmpty(projectRoot)
                ? "pwsh Packages/com.ddrive.core/Tools~/Mcp/register-mcp.ps1 -ProjectPath ."
                : $"pwsh \"{projectRoot}/Packages/com.ddrive.core/Tools~/Mcp/register-mcp.ps1\" -ProjectPath \"{projectRoot}\"";
    }
}
