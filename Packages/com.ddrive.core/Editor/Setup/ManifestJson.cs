using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDrive.Editor.Setup
{
    // [42_distribution.md] §3.5/§3.6/§6 P-6(2026-09-20) — `Packages/manifest.json` は JSON なので
    // テキスト編集してよい(CLAUDE.md §0-1 の禁止対象は .unity/.prefab/.asset/画像/音であり、
    // manifest.json/packages-lock.json は明示的に除外されている)。読み書きのロジックをここに集約し、
    // ウィザード(ProjectSetupWizardWindow)・検査(ProjectSetupInspector)・適用(ProjectSetupActions)の
    // どこからも同じ形式で扱えるようにする。実ファイル I/O(Load/Save)と、JObject を受け取る純粋な
    // 判定/変更関数を分けているのは、後者を EditMode テストで(実 manifest.json に触らず)固定するため。
    public static class ManifestJson
    {
        public const string DependenciesKey = "dependencies";
        public const string TestablesKey = "testables";
        public const string ScopedRegistriesKey = "scopedRegistries";

        // プロジェクトの Assets/ の 1 つ上(プロジェクトルート)配下の Packages/manifest.json。
        public static string ProjectManifestPath()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));

        public static JObject LoadProjectManifest() => Load(ProjectManifestPath());

        public static void SaveProjectManifest(JObject manifest) => Save(ProjectManifestPath(), manifest);

        public static JObject Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
        }

        public static void Save(string path, JObject manifest)
        {
            if (string.IsNullOrEmpty(path) || manifest == null)
            {
                return;
            }

            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            if (!json.EndsWith("\n", StringComparison.Ordinal))
            {
                json += "\n";
            }

            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        // ── dependencies ──

        public static bool HasDependency(JObject manifest, string packageId)
            => manifest?[DependenciesKey]?[packageId] != null;

        public static string GetDependencyValue(JObject manifest, string packageId)
            => manifest?[DependenciesKey]?[packageId]?.ToString();

        // manifest を直接書き換える(呼び出し側が Save するまではメモリ上のみ)。
        public static void SetDependency(JObject manifest, string packageId, string value)
        {
            if (manifest == null || string.IsNullOrEmpty(packageId))
            {
                return;
            }

            if (manifest[DependenciesKey] is not JObject deps)
            {
                deps = new JObject();
                manifest[DependenciesKey] = deps;
            }

            deps[packageId] = value;
        }

        // ── scopedRegistries ──

        public static bool HasScopedRegistry(JObject manifest, string url, string scope)
        {
            var entry = FindScopedRegistryByUrl(manifest, url);
            if (entry == null)
            {
                return false;
            }

            var scopes = entry["scopes"] as JArray;
            return scopes != null && scopes.Any(s => string.Equals((string)s, scope, StringComparison.Ordinal));
        }

        public static void AddScopedRegistry(JObject manifest, string name, string url, string scope)
        {
            if (manifest == null || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(scope))
            {
                return;
            }

            var entry = FindScopedRegistryByUrl(manifest, url);
            if (entry == null)
            {
                if (manifest[ScopedRegistriesKey] is not JArray registries)
                {
                    registries = new JArray();
                    manifest[ScopedRegistriesKey] = registries;
                }

                entry = new JObject
                {
                    ["name"] = name,
                    ["url"] = url,
                    ["scopes"] = new JArray(scope),
                };
                registries.Add(entry);
                return;
            }

            if (entry["scopes"] is not JArray scopes)
            {
                scopes = new JArray();
                entry["scopes"] = scopes;
            }

            if (!scopes.Any(s => string.Equals((string)s, scope, StringComparison.Ordinal)))
            {
                scopes.Add(scope);
            }
        }

        private static JObject FindScopedRegistryByUrl(JObject manifest, string url)
        {
            if (manifest?[ScopedRegistriesKey] is not JArray registries || string.IsNullOrEmpty(url))
            {
                return null;
            }

            foreach (var item in registries)
            {
                if (item is JObject obj && string.Equals((string)obj["url"], url, StringComparison.OrdinalIgnoreCase))
                {
                    return obj;
                }
            }

            return null;
        }

        // ── testables ──

        public static bool HasTestable(JObject manifest, string packageId)
        {
            if (manifest?[TestablesKey] is not JArray testables || string.IsNullOrEmpty(packageId))
            {
                return false;
            }

            return testables.Any(t => string.Equals((string)t, packageId, StringComparison.Ordinal));
        }

        // enabled=true: 無ければ追加。enabled=false: あれば削除(配列が空になったらキー自体を残す
        // かどうかは manifest.json の慣習に合わせて空配列のまま残す。null にはしない)。冪等。
        public static void SetTestable(JObject manifest, string packageId, bool enabled)
        {
            if (manifest == null || string.IsNullOrEmpty(packageId))
            {
                return;
            }

            if (manifest[TestablesKey] is not JArray testables)
            {
                if (!enabled)
                {
                    return; // 元々無いものを「無効化」しても何もしない。
                }

                testables = new JArray();
                manifest[TestablesKey] = testables;
            }

            var already = testables.FirstOrDefault(t => string.Equals((string)t, packageId, StringComparison.Ordinal));
            if (enabled)
            {
                if (already == null)
                {
                    testables.Add(packageId);
                }
            }
            else if (already != null)
            {
                testables.Remove(already);
            }
        }
    }
}
