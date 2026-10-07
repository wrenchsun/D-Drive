using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Migration;
using DDrive.Editor.Settings;
using DDrive.Foundation.Data;
using DDrive.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;
using UnityMCP.Editor.Settings;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.1 / §5.3 / §6.3 MCP-2(2026-10-07) — ddrive_status(完成版)。
    // 1 回で version / compile / tests / validation / migration / addressables / mcp が揃う。
    // どのセクションも「安く」読めるものだけ(全 Validator を走らせない・何も書かない)。
    //  - tests       : isuzu の直近のテスト結果(SessionState)。無ければ TestResults/*-results.xml の新しい方
    //  - validation  : McpValidationCache の要約(ddrive_validate / Record が書く)。未実行なら {cached:false}
    //  - migration   : DDriveMigrationRunner.PlanProject() の件数(読み取りのみ)
    //  - addressables: AddressablesSync.CountMissingEntries()(読み取りのみ)
    //  - mcp         : isuzu の記述子(%LOCALAPPDATA%/UnityMCP/instances/<hash>.json)。トークンは返さない
    public static class DDriveStatusTools
    {
        private static readonly string[] AllSections =
        {
            "version", "compile", "tests", "validation", "migration", "addressables", "mcp",
        };

        // isuzu TestRunnerTools の SessionState キー(同ツールは internal のため JSON を直接読む)。
        private const string IsuzuTestSessionKey = "UnityMCP.LastTestRun";

        private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        [McpTool(
            "ddrive_status",
            "D-Drive の状態を 1 回で返す(compile/tests/validation/mcp 等)。sections で絞る",
            Idempotency = McpIdempotency.Safe,
            MaxResultSizeChars = McpGuard.MaxMaxChars,
            Group = "diagnostics")]
        [McpReturns("version", "schema", "compile", "tests", "validation", "migration", "addressables", "mcp")]
        public static JObject Status(
            [McpArg("sections", "カンマ区切り。省略で全部(version,compile,tests,…)")]
            string sections = null)
        {
            return McpGuard.Run(() =>
            {
                var wanted = ParseSections(sections);
                var result = new JObject();

                if (wanted.Contains("version"))
                {
                    result["version"] = DDriveVersion.Value;
                    result["schema"] = DDriveSchema.Current;
                }

                if (wanted.Contains("compile"))
                {
                    result["compile"] = McpJson.Obj(("ok", McpJson.Keep(!EditorUtility.scriptCompilationFailed)));
                }

                if (wanted.Contains("tests"))
                {
                    var tests = ReadLastTests();
                    if (tests != null)
                    {
                        result["tests"] = new JObject { ["last"] = tests };
                    }
                }

                if (wanted.Contains("validation"))
                {
                    result["validation"] = McpValidationCache.TryGet(out var summary)
                        ? summary
                        : McpJson.Obj(("cached", McpJson.Keep(false)));
                }

                if (wanted.Contains("migration"))
                {
                    result["migration"] = McpJson.Obj(("pending", McpJson.Keep(DDriveMigrationRunner.PlanProject().TotalCount)));
                }

                if (wanted.Contains("addressables"))
                {
                    var missing = AddressablesSync.CountMissingEntries();
                    if (missing >= 0)
                    {
                        result["addressables"] = McpJson.Obj(("missing", McpJson.Keep(missing)));
                    }
                }

                if (wanted.Contains("mcp"))
                {
                    result["mcp"] = BuildMcpSection();
                }

                return result;
            });
        }

        // 未知の section は無視する(将来 section を足しても古いクライアントが落ちない)。全部未知なら空の {} を返す。
        private static HashSet<string> ParseSections(string sections)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(sections))
            {
                set.UnionWith(AllSections);
                return set;
            }

            foreach (var part in sections.Split(','))
            {
                var name = part.Trim().ToLowerInvariant();
                if (Array.IndexOf(AllSections, name) >= 0)
                {
                    set.Add(name);
                }
            }

            return set;
        }

        // ── tests ──

        private static JObject ReadLastTests()
        {
            var fromSession = ReadTestsFromIsuzuSession();
            if (fromSession != null)
            {
                return fromSession;
            }

            // フォールバック: プロジェクト直下の TestResults/*-results.xml のうち新しい方(CLI 実行でも残る)。
            JObject best = null;
            var bestTime = DateTime.MinValue;
            foreach (var (file, mode) in new[] { ("editmode-results.xml", "EditMode"), ("playmode-results.xml", "PlayMode") })
            {
                var path = Path.Combine(ProjectRoot(), "TestResults", file);
                if (!File.Exists(path))
                {
                    continue;
                }

                var time = File.GetLastWriteTimeUtc(path);
                if (time <= bestTime)
                {
                    continue;
                }

                var parsed = ReadTestRunXml(path, mode, time);
                if (parsed != null)
                {
                    best = parsed;
                    bestTime = time;
                }
            }

            return best;
        }

        private static JObject ReadTestsFromIsuzuSession()
        {
            try
            {
                var raw = SessionState.GetString(IsuzuTestSessionKey, string.Empty);
                if (string.IsNullOrEmpty(raw))
                {
                    return null;
                }

                var json = McpJson.Parse(raw);
                if ((string)json["status"] != "completed")
                {
                    return null;
                }

                return McpJson.Obj(
                    ("mode", (string)json["mode"]),
                    ("passed", McpJson.Keep((int?)json["passed"] ?? 0)),
                    ("failed", McpJson.Keep((int?)json["failed"] ?? 0)),
                    ("inconclusive", (int?)json["inconclusive"] ?? 0),
                    ("at", (string)json["completedAt"]));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static JObject ReadTestRunXml(string path, string mode, DateTime writeTimeUtc)
        {
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
                using var stream = File.OpenRead(path);
                using var reader = XmlReader.Create(stream, settings);
                if (!reader.ReadToFollowing("test-run"))
                {
                    return null;
                }

                return McpJson.Obj(
                    ("mode", mode),
                    ("passed", McpJson.Keep(ParseInt(reader.GetAttribute("passed")))),
                    ("failed", McpJson.Keep(ParseInt(reader.GetAttribute("failed")))),
                    ("inconclusive", ParseInt(reader.GetAttribute("inconclusive"))),
                    ("at", writeTimeUtc.ToString(TimeFormat, CultureInfo.InvariantCulture)));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int ParseInt(string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        // ── mcp ──

        private static JObject BuildMcpSection()
        {
            McpInstanceInfo.TryReadDescriptor(
                Application.dataPath, out var port, out var preferred, out var portMismatch, out var pid, out var projectName);
            var fixedPort = false;
            try
            {
                fixedPort = McpSettings.instance.httpPort > 0;
            }
            catch (Exception)
            {
                // isuzu の設定が読めなければ fixedPort は出さない。
            }

            var section = McpJson.Obj(
                ("writeEnabled", McpJson.Keep(DDriveProjectSettings.instance.McpAllowWrite)),
                ("playing", McpGuard.IsPlaying),
                ("project", projectName ?? Application.productName),
                ("port", port),
                ("preferredPort", preferred),
                ("portMismatch", portMismatch),
                ("fixedPort", fixedPort),
                ("pid", pid));
            if (fixedPort)
            {
                // [1002] §6.1 (c): 固定すると他プロジェクト・他アプリのポートと衝突しうる。ProjectSetupValidator の DD-MCP-FIXED-PORT と対。
                section["warning"] = FixedPortWarning;
            }

            // [1002] §11.2 D MCP-14(2026-10-07): 他の MCP パッケージ(ids。無ければ省略)と isuzu の版(解決済みの版 → 無ければ manifest の #ref)。
            try
            {
                var scan = DDrive.Editor.Update.McpPackageSupport.ScanManifest(DDrive.Editor.Setup.ManifestJson.LoadProjectManifest());
                if (scan.HasOthers)
                {
                    var others = new JArray();
                    foreach (var other in scan.Others)
                    {
                        others.Add(other.Id);
                    }

                    section["otherMcp"] = others;
                }

                var version = ResolveIsuzuVersion(scan);
                if (version != null)
                {
                    section["isuzuVersion"] = version;
                }
            }
            catch (Exception)
            {
                // manifest が読めなければ省略(status は止めない)。
            }

            return section;
        }

        private static string ResolveIsuzuVersion(DDrive.Editor.Update.McpPackageSupport.McpScan scan)
        {
            if (!scan.IsuzuInstalled)
            {
                return null;
            }

            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpSettings).Assembly);
            if (info != null && info.name == DDrive.Editor.Update.McpPackageSupport.IsuzuPackageId && !string.IsNullOrEmpty(info.version))
            {
                return info.version;
            }

            var reference = scan.IsuzuRef;
            return !string.IsNullOrEmpty(reference) && (reference[0] == 'v' || reference[0] == 'V') ? reference.Substring(1) : reference;
        }

        public const string FixedPortWarning = "ポート固定は衝突の元";

        private static string ProjectRoot() => Path.GetDirectoryName(Application.dataPath);
    }
}
