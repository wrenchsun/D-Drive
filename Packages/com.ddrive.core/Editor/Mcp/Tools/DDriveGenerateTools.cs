using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Editor.Dependencies;
using DDrive.Editor.Inspector;
using DDrive.Editor.Preload;
using DDrive.Editor.Settings;
using DDrive.Runtime.Loading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.3 MCP-6(2026-10-07) — ddrive_generate(7 種の再生成を 1 ツールにまとめる)。
    // ツールはアダプタに徹し、各 target は既存サービスを呼ぶだけ:
    //   ids → AssetIdGenerator.Regenerate / tuning → TuningCodegen.Regenerate / addressables → AddressablesSync.SyncAll
    //   preload → ScenePreloadGenerator / prefabs → DefaultPrefabs.EnsureSeEmitterPrefab / deps → DependencyGraphService.RebuildAll
    //   icons → AssetIconService.CreateDefaultIconsForAll(onlyMissing:true)
    // preview の意味は target ごとに違う(実装メモ MCP-6 参照): 何も書かずに結果が分かるものは結果を、分からないものは wouldXxx だけ返す。
    public static class DDriveGenerateTools
    {
        public const string TargetIds = "ids";
        public const string TargetTuning = "tuning";
        public const string TargetAddressables = "addressables";
        public const string TargetPreload = "preload";
        public const string TargetPrefabs = "prefabs";
        public const string TargetDeps = "deps";
        public const string TargetIcons = "icons";

        public static readonly string[] Targets =
        {
            TargetIds, TargetTuning, TargetAddressables, TargetPreload, TargetPrefabs, TargetDeps, TargetIcons,
        };

        // preload の scene 引数。
        public const string SceneCurrent = "current";
        public const string SceneAll = "all";

        // テスト専用: deps の実再構築(重い)を差し替える。null なら本物の DependencyGraphService.RebuildAll。
        public static Action RebuildOverride;

        [McpTool(
            "ddrive_generate",
            "ID 定数・Tuning・Addressables・Preload・標準プレハブ・依存グラフ・アイコンを再生成",
            UndoGroup = "D-Drive MCP: 再生成",
            Group = "build",
            Examples = new[] { "{\"kind\":\"ids\",\"preview\":true}", "{\"kind\":\"preload\",\"scene\":\"all\"}" })]
        [McpReturns("target", "ok", "preview", "changed", "summary", "needsRebuild", "hint")]
        public static JObject Generate(
            [McpArg("kind", "ids / tuning / addressables / preload / prefabs / deps / icons(isuzu が target を予約しているので kind)", Required = true)]
            string kind,
            [McpArg("preview", "true なら書かずに予定だけ返す(中身は target ごとに違う)")]
            bool preview = false,
            [McpArg("scene", "preload のみ: current(既定。開いているシーン) / all(ビルド設定の有効な全シーン)")]
            string scene = null)
        {
            return McpGuard.Run(() =>
            {
                McpGuard.EnsureCanWrite();
                switch (ParseTarget(kind))
                {
                    case TargetIds: return Ids(preview);
                    case TargetTuning: return Tuning(preview);
                    case TargetAddressables: return Addressables(preview);
                    case TargetPreload: return Preload(preview, scene);
                    case TargetPrefabs: return Prefabs(preview);
                    case TargetDeps: return Deps(preview);
                    default: return Icons(preview);
                }
            });
        }

        // 純粋関数(テスト用)。未知の target は invalid_params に一覧を載せる。
        public static string ParseTarget(string target)
        {
            var key = (target ?? string.Empty).Trim().ToLowerInvariant();
            if (Array.IndexOf(Targets, key) < 0)
            {
                throw new McpToolError(
                    McpGuard.CodeInvalidParams,
                    $"kind '{target}' は未知です。{string.Join(" / ", Targets)} のいずれか");
            }

            return key;
        }

        // {target, ok?, preview?, changed?, summary}。changed が分からないとき(preview の wouldXxx)は null で省く。
        public static JObject Result(string target, bool? changed, object summary, bool preview = false, bool ok = true)
        {
            var result = new JObject { ["target"] = target };
            if (!ok)
            {
                result["ok"] = false;
            }

            if (preview)
            {
                result["preview"] = true;
            }

            if (changed.HasValue)
            {
                result["changed"] = changed.Value;
            }

            result["summary"] = summary is JToken token ? token : JToken.FromObject(summary);
            return result;
        }

        // ── ids ──

        public static string IdsPath() => DDriveProjectSettings.instance.GeneratedRoot + "/AssetIds.g.cs";

        private static JObject Ids(bool preview)
        {
            var path = IdsPath();
            if (preview)
            {
                // Regenerate は ID の払い出し(Data の書き換え)と保存を伴い、出力先を変えても副作用が消えないので、
                // 実行せず出力先だけ返す。
                return Result(TargetIds, null, new { wouldWrite = path, exists = File.Exists(path) }, preview: true);
            }

            var before = ReadOrNull(path);
            var r = AssetIdGenerator.Regenerate();
            var after = ReadOrNull(path);
            return IdsResult(r, before, after, path);
        }

        public static JObject IdsResult(AssetIdGenerator.Result r, string before, string after, string path)
        {
            var duplicates = new JArray();
            foreach (var d in r.Duplicates)
            {
                duplicates.Add(new JObject
                {
                    ["id"] = McpJson.FormatId(d.Id),
                    ["paths"] = new JArray(d.PathA, d.PathB),
                });
            }

            var summary = new JObject
            {
                ["total"] = r.TotalCount,
                ["assigned"] = r.AssignedCount,
                ["duplicates"] = duplicates,
                ["path"] = path,
            };
            // 新しい ID を払い出したときはファイルが同じでも Data が変わっているので changed。
            var changed = r.Success && (r.AssignedCount > 0 || !string.Equals(before, after, StringComparison.Ordinal));
            return Result(TargetIds, changed, summary, ok: r.Success);
        }

        // ── tuning ──

        private static JObject Tuning(bool preview)
        {
            var path = DDriveProjectSettings.instance.GeneratedRoot + "/Tuning.g.cs";
            var before = ReadOrNull(path);
            if (preview)
            {
                // 一時ファイルへ書いて比較する(Assets の外なので import も走らない。TuningCodegen は Data を書き換えない)。
                var temp = Path.Combine(Path.GetTempPath(), "ddrive_tuning_preview_" + Guid.NewGuid().ToString("N") + ".g.cs");
                try
                {
                    var p = TuningCodegen.Regenerate(null, temp);
                    return TuningResult(p, before, ReadOrNull(temp), path, preview: true);
                }
                finally
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (Exception)
                    {
                        // 一時ファイルが消せなくても続行する(CLAUDE.md §0-4)。
                    }
                }
            }

            var r = TuningCodegen.Regenerate();
            return TuningResult(r, before, ReadOrNull(path), path, preview: false);
        }

        public static JObject TuningResult(TuningCodegen.Result r, string before, string after, string path, bool preview)
        {
            var summary = new JObject
            {
                ["keys"] = r.TotalCount,
                ["tables"] = r.TableCount,
                ["columns"] = r.ColumnCount,
                ["path"] = path,
            };
            var changed = r.Success && !string.Equals(before, after, StringComparison.Ordinal);
            return Result(TargetTuning, changed, summary, preview, ok: r.Success);
        }

        // ── addressables ──

        private static JObject Addressables(bool preview)
        {
            if (!AddressablesSync.IsAvailable)
            {
                return Result(TargetAddressables, false, new { available = false }, preview, ok: false);
            }

            if (preview)
            {
                var missing = AddressablesSync.CountMissingEntries();
                return Result(TargetAddressables, missing > 0, new { missing }, preview: true);
            }

            var (fixedAssets, catalogs, missingCatalog) = AddressablesSync.SyncAll(log: false);
            return AddressablesResult(fixedAssets, catalogs, missingCatalog);
        }

        public static JObject AddressablesResult(int fixedAssets, int catalogs, int missingCatalog)
        {
            return Result(TargetAddressables, fixedAssets > 0, new { fixedAssets, catalogs, missingCatalog });
        }

        // ── preload ──

        private static JObject Preload(bool preview, string scene)
        {
            var mode = ParseScene(scene);
            var scenePaths = new List<string>();
            if (mode == SceneAll)
            {
                foreach (var s in EditorBuildSettings.scenes)
                {
                    if (s != null && s.enabled && !string.IsNullOrEmpty(s.path))
                    {
                        scenePaths.Add(s.path);
                    }
                }
            }
            else
            {
                var active = EditorSceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path))
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, "開いているシーンが保存されていません。保存するか scene=all を指定してください");
                }

                scenePaths.Add(active.path);
            }

            if (DependencyGraphService.CachedFileCount == 0)
            {
                // 未構築のまま書くと空の Preload リストで上書きしてしまうので、書かずに案内する。
                return new JObject
                {
                    ["target"] = TargetPreload,
                    ["needsRebuild"] = true,
                    ["hint"] = DDriveDependencyTools.RebuildHint,
                };
            }

            if (preview)
            {
                return Result(TargetPreload, null, new { scenes = scenePaths.Count, wouldUpdate = scenePaths }, preview: true);
            }

            var before = HashPreloadLists();
            var lists = new List<ScenePreloadList>();
            if (mode == SceneAll)
            {
                lists.AddRange(ScenePreloadGenerator.GenerateForAllBuildScenes());
            }
            else
            {
                var list = ScenePreloadGenerator.GenerateForScene(scenePaths[0]);
                if (list != null)
                {
                    lists.Add(list);
                }
            }

            var after = HashPreloadLists();
            var entries = 0;
            var paths = new JArray();
            foreach (var list in lists)
            {
                entries += list.Entries.Count;
                paths.Add(AssetDatabase.GetAssetPath(list));
            }

            var changed = before.Count != after.Count || before.Any(kv => !after.TryGetValue(kv.Key, out var h) || h != kv.Value);
            return Result(TargetPreload, changed, new JObject { ["scenes"] = lists.Count, ["entries"] = entries, ["lists"] = paths });
        }

        // 純粋関数(テスト用)。
        public static string ParseScene(string scene)
        {
            var key = string.IsNullOrWhiteSpace(scene) ? SceneCurrent : scene.Trim().ToLowerInvariant();
            if (key != SceneCurrent && key != SceneAll)
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, $"scene '{scene}' は未知です。current / all のいずれか");
            }

            return key;
        }

        private static Dictionary<string, string> HashPreloadLists()
        {
            var map = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ScenePreloadList)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!File.Exists(path))
                {
                    continue;
                }

                using var sha = SHA256.Create();
                map[path] = Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
            }

            return map;
        }

        // ── prefabs ──

        private static JObject Prefabs(bool preview)
        {
            var path = DefaultPrefabs.SeEmitterPrefabPath;
            var existed = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path) != null;
            var wouldCreate = existed ? new string[0] : new[] { path };
            if (preview)
            {
                return Result(TargetPrefabs, !existed, new { wouldCreate }, preview: true);
            }

            // DefaultPrefabs.GenerateAll と同じ処理(Ping・ログだけ省く)。既存は上書きしない(冪等)。
            DefaultPrefabs.EnsureSeEmitterPrefab();
            return Result(TargetPrefabs, !existed, new { created = wouldCreate });
        }

        // ── deps ──

        private static JObject Deps(bool preview)
        {
            if (preview)
            {
                return Result(
                    TargetDeps,
                    null,
                    new { wouldRebuild = true, cachedFiles = DependencyGraphService.CachedFileCount },
                    preview: true);
            }

            var sw = Stopwatch.StartNew();
            if (RebuildOverride != null)
            {
                RebuildOverride();
            }
            else
            {
                DependencyGraphService.RebuildAll();
            }

            sw.Stop();
            return DepsResult(DependencyGraphService.CachedFileCount, DependencyGraphService.CachedEdgeCount, sw.Elapsed.TotalSeconds);
        }

        public static JObject DepsResult(int files, int edges, double seconds)
        {
            return Result(TargetDeps, true, new { files, edges, seconds = Math.Round(seconds, 1) });
        }

        // ── icons ──

        private static JObject Icons(bool preview)
        {
            if (preview)
            {
                // 対象の数え上げは CreateDefaultIconsForAll の内部処理なので、予定だけ返す。
                return Result(TargetIcons, null, new { wouldRun = true, onlyMissing = true }, preview: true);
            }

            var created = AssetIconService.CreateDefaultIconsForAll(onlyMissing: true);
            return Result(TargetIcons, created > 0, new { created });
        }

        private static string ReadOrNull(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
