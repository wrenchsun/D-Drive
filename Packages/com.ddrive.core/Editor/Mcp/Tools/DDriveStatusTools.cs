using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
            Group = "diagnostics")]
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
            var descriptor = ReadDescriptor();
            var preferred = (int?)descriptor?["preferredPort"];
            var port = (int?)descriptor?["port"];
            var fixedPort = false;
            try
            {
                fixedPort = McpSettings.instance.httpPort > 0;
            }
            catch (Exception)
            {
                // isuzu の設定が読めなければ fixedPort は出さない。
            }

            return McpJson.Obj(
                ("writeEnabled", McpJson.Keep(DDriveProjectSettings.instance.McpAllowWrite)),
                ("playing", McpGuard.IsPlaying),
                ("project", (string)descriptor?["projectName"] ?? Application.productName),
                ("port", port),
                ("preferredPort", preferred),
                ("portMismatch", port.HasValue && preferred.HasValue && port.Value != preferred.Value),
                ("fixedPort", fixedPort),
                ("pid", (int?)descriptor?["pid"]));
        }

        // この Editor の記述子を読む(トークンは返り値に入れない)。ハッシュ規則は Tools/Mcp/register-mcp.ps1 と同じ。
        private static JObject ReadDescriptor()
        {
            try
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root))
                {
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                }

                var path = Path.Combine(root, "UnityMCP", "instances", HashDataPath(Application.dataPath) + ".json");
                return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string HashDataPath(string dataPath)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(dataPath ?? string.Empty));
            var sb = new StringBuilder(16);
            for (var i = 0; i < 8; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }

            return sb.ToString();
        }

        private static string ProjectRoot() => Path.GetDirectoryName(Application.dataPath);
    }
}
