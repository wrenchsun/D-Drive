using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DDrive.Editor.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §1.4/§5.1(SpecSnapshotWriter)/§8 W-11 — 取得した Web(GAS) の JSON をそのまま
    // repo 直下の Specs/*.json へスナップショットとして書き出す(Assets 外 = Unity のインポート対象外)。
    // 「履歴・ロールバックは作らない代わりに git 履歴で追える」ようにするための最大の新規実装
    // ([32] §1.4)。キー順を安定させて整形するため、オブジェクトのキーはすべて再帰的にアルファベット順へ
    // 並べ替えてから書き出す(Web 側の実装が Object.keys の順序をどう返すかに依存せず、
    // 同じ内容なら常に同じバイト列になる = 実質的な変更が無い同期で git diff が空になる)。
    //
    // 書き出す内容は生の Web 応答(items)からそのまま取る(ddriveState・comments・revision 等も含めた
    // フルフィデリティなスナップショット。SpecAssetRow/SpecTuningRow は同期に必要な最小限の項目しか
    // 持たないため、スナップショット用には使わない)。
    public static class SpecSnapshotWriter
    {
        public const string DefaultRelativeAssetsPath = "Specs/assets.json";
        public const string DefaultRelativeTuningPath = "Specs/tuning.json";

        // プロジェクトの Assets/ の 1 つ上 = repo ルート(Unity プロジェクトルート)。
        public static string DefaultRepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public sealed class Result
        {
            public bool AssetsWritten;
            public bool TuningWritten;
            public string AssetsPath;
            public string TuningPath;
            public string Warning;
        }

        // assetsJson: assets.list の生応答({ items:[...], ok:true, ... })。null/失敗時は書かない。
        // tuningScalarJson: tuningScalarList の生応答({ items:{...} })。
        // tuningTableJson: tuningTableList の生応答({ items:{...} })。
        public static Result Write(string assetsJson, string tuningScalarJson, string tuningTableJson, string repoRoot = null)
        {
            repoRoot ??= DefaultRepoRoot;
            var result = new Result();

            // [42_distribution.md] §3.4/§7 B-6(P-5) — DDriveProjectSettings.SpecsRoot(既定 "Specs")から
            // 相対パスを組み立てる。既定値のままなら DefaultRelative*Path と同じ値になり挙動は変わらない。
            var specsRoot = DDriveProjectSettings.instance.SpecsRoot;
            var relativeAssetsPath = $"{specsRoot}/assets.json";
            var relativeTuningPath = $"{specsRoot}/tuning.json";

            try
            {
                if (TryBuildAssetsSnapshot(assetsJson, out var assetsSnapshot, out var assetsWarning))
                {
                    var path = Path.Combine(repoRoot, relativeAssetsPath);
                    WriteJsonFile(path, assetsSnapshot);
                    result.AssetsWritten = true;
                    result.AssetsPath = path;
                }

                result.Warning = CombineWarning(result.Warning, assetsWarning);

                var tuningPath = Path.Combine(repoRoot, relativeTuningPath);
                if (TryBuildTuningSnapshot(tuningScalarJson, tuningTableJson, tuningPath, out var tuningSnapshot, out var tuningWarning))
                {
                    WriteJsonFile(tuningPath, tuningSnapshot);
                    result.TuningWritten = true;
                    result.TuningPath = tuningPath;
                }

                result.Warning = CombineWarning(result.Warning, tuningWarning);
            }
            catch (Exception e)
            {
                // CLAUDE.md §0-4: 例外で止めない。スナップショット書き出しの失敗は警告に留め、
                // 同期の他の部分(Data の作成・更新)には影響させない。
                Debug.LogWarning($"[DDrive] Specs/*.json の書き出しに失敗しました: {e.Message}");
                result.Warning = CombineWarning(result.Warning, e.Message);
            }

            return result;
        }

        private static bool TryBuildAssetsSnapshot(string assetsJson, out JObject snapshot, out string warning)
        {
            snapshot = null;
            warning = null;
            if (string.IsNullOrEmpty(assetsJson))
            {
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(assetsJson);
            }
            catch (Exception e)
            {
                warning = $"assets.json 用の応答を解釈できません: {e.Message}";
                return false;
            }

            if (root.Value<bool?>("ok") == false)
            {
                warning = $"assets.list がエラーを返したため assets.json を更新しませんでした: {(string)root["error"]}";
                return false;
            }

            var items = root["items"] as JArray ?? new JArray();
            var sortedItems = items
                .OfType<JObject>()
                .OrderBy(i => (string)i["id"] ?? string.Empty, StringComparer.Ordinal)
                .Select(i => (JToken)SortKeysDeep(i))
                .ToArray();

            snapshot = new JObject { ["items"] = new JArray(sortedItems) };
            return true;
        }

        private static bool TryBuildTuningSnapshot(string tuningScalarJson, string tuningTableJson, string existingPath, out JObject snapshot, out string warning)
        {
            snapshot = null;
            warning = null;

            var scalars = ExtractSortedItemsMap(tuningScalarJson, "tuningScalarList", ref warning);
            var tables = ExtractSortedItemsMap(tuningTableJson, "tuningTableList", ref warning);

            if (scalars == null && tables == null)
            {
                return false;
            }

            // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-5) — 片方だけ失敗した
            // ときに、失敗した側を空配列 `[]` で書き出していた(両方 null のときだけスキップしていた)。
            // §8 W-11 の「失敗した部分は書き込まず警告を返す」と食い違い、`Specs/tuning.json` の
            // git diff に「全スカラー削除」が現れ、SpecDiffValidator の範囲チェック(スナップショットの
            // min/max と TuningTable の値を比べる)も黙って無効になっていた。
            // 失敗した側は既存ファイルの該当配列をそのまま温存する。
            if (scalars == null || tables == null)
            {
                var existing = TryReadExistingSnapshot(existingPath);

                if (scalars == null)
                {
                    scalars = ExistingArray(existing, "scalars");
                    warning = CombineWarning(warning, "tuningScalarList を更新できなかったため、Specs/tuning.json の scalars は前回の内容を保持しました。");
                }

                if (tables == null)
                {
                    tables = ExistingArray(existing, "tables");
                    warning = CombineWarning(warning, "tuningTableList を更新できなかったため、Specs/tuning.json の tables は前回の内容を保持しました。");
                }
            }

            snapshot = new JObject
            {
                ["scalars"] = new JArray(scalars),
                ["tables"] = new JArray(tables),
            };
            return true;
        }

        // 既存の Specs/*.json(前回のスナップショット)。無い / 壊れている場合は null(温存対象が無い扱い)。
        private static JObject TryReadExistingSnapshot(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }

                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] 既存の '{path}' を読めませんでした(該当部分は空で書き出します): {e.Message}");
                return null;
            }
        }

        private static JToken[] ExistingArray(JObject existing, string propertyName)
        {
            if (existing == null || existing[propertyName] is not JArray array || array.Count == 0)
            {
                return Array.Empty<JToken>();
            }

            var result = new JToken[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                result[i] = array[i].DeepClone();
            }

            return result;
        }

        // { items: { "<key>": {...} } } 形式の応答から、キーでソートした値配列を返す(id フィールドが
        // 無いエントリには key を id として補う。Storage.putItem は id を必ず入れるため通常は不要な保険)。
        private static JToken[] ExtractSortedItemsMap(string json, string apiNameForWarning, ref string warning)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception e)
            {
                warning = CombineWarning(warning, $"{apiNameForWarning} 用の応答を解釈できません: {e.Message}");
                return null;
            }

            if (root.Value<bool?>("ok") == false)
            {
                warning = CombineWarning(warning, $"{apiNameForWarning} がエラーを返しました: {(string)root["error"]}");
                return null;
            }

            var items = root["items"] as JObject;
            if (items == null)
            {
                return Array.Empty<JToken>();
            }

            return items.Properties()
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p =>
                {
                    var value = p.Value as JObject ?? new JObject();
                    if (value["id"] == null)
                    {
                        value["id"] = p.Name;
                    }

                    return (JToken)SortKeysDeep(value);
                })
                .ToArray();
        }

        // JObject のキーをアルファベット順に並べ替えた新しい JObject を返す(配列・ネストしたオブジェクトも再帰的に処理)。
        private static JToken SortKeysDeep(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                {
                    var sorted = new JObject();
                    foreach (var prop in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        sorted[prop.Name] = SortKeysDeep(prop.Value);
                    }

                    return sorted;
                }
                case JArray array:
                {
                    var sortedArray = new JArray();
                    foreach (var item in array)
                    {
                        sortedArray.Add(SortKeysDeep(item));
                    }

                    return sortedArray;
                }
                default:
                    return token.DeepClone();
            }
        }

        private static void WriteJsonFile(string path, JObject content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var text = content.ToString(Formatting.Indented) + "\n";
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string CombineWarning(string existing, string addition)
        {
            if (string.IsNullOrEmpty(addition))
            {
                return existing;
            }

            return string.IsNullOrEmpty(existing) ? addition : existing + " / " + addition;
        }
    }
}
