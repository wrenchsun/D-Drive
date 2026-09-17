using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DDrive.Editor.Spec;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Tuning;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace DDrive.Editor.Validation
{
    // [27_spec_sheet.md] §6(旧: スプレッドシート方式の検査表) → [32_spec_web.md] §10(発注ツールへの
    // 再定義)6-9。ネットワークには一切出ず、リポジトリ直下の Specs/assets.json・Specs/tuning.json
    // (SpecSnapshotWriter が同期のたびに書き出すスナップショット)と実データ(カタログ・TuningTable)を
    // 比較するだけの Validator(CI でも動く)。
    //
    // 読み替え表(2026-09-15、旧方式→現行の Web 発注ツール前提):
    //   シートにあって Data が無い              → 発注はあるが D-Drive に Data が無い               → Info
    //   Data が「本番」なのに Placeholder        → 発注の status が「インポート済」なのに Placeholder → Warning
    //   シートから消えたが Data 残存             → スナップショットに無い発注由来の Data が残っている  → Info
    //   調整値が範囲外                          → TuningTable の値が Web 側定義(min/max)の範囲外      → Error
    //   必須列空 / 識別子書式違反(Web側で検証済み) → スナップショットの形式が壊れている               → Warning
    //
    // スナップショットが無いプロジェクト(仕様書同期を使っていない)では Info 1 件だけ出して他の検査はしない。
    public sealed class SpecDiffValidator : IUniversalValidator
    {
        // 値は SpecStatusTag に 1 か所化した(2026-09-17、[41] P1-7。GAS 側 SPEC_WEB_ASSET_STATUSES が正)。
        private const string ImportedStatus = DDrive.Editor.Spec.SpecStatusTag.ImportedStatus;

        // テスト用オーバーライド。既定(null)は本番動作(Specs/*.json の実パス・DDriveSpecSettings/
        // プロジェクト内の TuningTable を自動解決)。
        public string RepoRootOverride { get; set; }
        public TuningTable TuningTableOverride { get; set; }

        public AssetType Target => AssetType.None;

        // ctx(Run All / CI.ValidateAll の 1 回の実行)ごとに 1 回だけ「全体」の結果を計算する。
        // AddressablesRegistrationValidator の「Addressables 設定なし」検出と同じ、ctx をキーにした
        // 重複防止(ValidatorRegistry.RunAll はこの Validate を「全アセットの数」だけ呼ぶため)。
        private static ValidationContext _lastCtx;
        private static List<ValidationResult> _lastGlobalResults;
        private static SnapshotData _lastSnapshot;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (_lastCtx != ctx)
            {
                _lastCtx = ctx;
                _lastSnapshot = LoadSnapshot(ResolveRepoRoot());
                _lastGlobalResults = ComputeGlobalResults(_lastSnapshot, ResolveTuningTable());

                foreach (var result in _lastGlobalResults)
                {
                    yield return result;
                }
            }

            if (data == null || !_lastSnapshot.HasAnyFile)
            {
                yield break;
            }

            foreach (var result in ComputePerAssetResults(data, _lastSnapshot))
            {
                yield return result;
            }
        }

        private string ResolveRepoRoot()
            => string.IsNullOrEmpty(RepoRootOverride) ? SpecSnapshotWriter.DefaultRepoRoot : RepoRootOverride;

        private TuningTable ResolveTuningTable()
        {
            if (TuningTableOverride != null)
            {
                return TuningTableOverride;
            }

            var settings = DDriveSpecSettings.Load();
            if (settings != null && settings.TuningTable != null)
            {
                return settings.TuningTable;
            }

            var guids = AssetSearch.FindAssets("t:" + nameof(TuningTable));
            if (guids.Length == 0)
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<TuningTable>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // ── スナップショットの読み込み ──

        private sealed class SnapshotData
        {
            public bool AssetsFileFound;
            public bool AssetsParsedOk; // ルート直下の items(配列)まで読めたか(個々の要素の不備は別途 Warning)
            public bool TuningFileFound;
            public bool TuningParsedOk;

            public readonly Dictionary<string, JObject> AssetsByKey = new(StringComparer.Ordinal);
            public readonly List<JObject> ScalarItems = new();
            public readonly List<JObject> TableItems = new();
            public readonly List<string> FormatWarnings = new();

            public bool HasAnyFile => AssetsFileFound || TuningFileFound;
        }

        private static SnapshotData LoadSnapshot(string repoRoot)
        {
            var snapshot = new SnapshotData();
            var assetsPath = Path.Combine(repoRoot, SpecSnapshotWriter.DefaultRelativeAssetsPath);
            var tuningPath = Path.Combine(repoRoot, SpecSnapshotWriter.DefaultRelativeTuningPath);

            LoadAssetsFile(assetsPath, snapshot);
            LoadTuningFile(tuningPath, snapshot);

            return snapshot;
        }

        private static void LoadAssetsFile(string path, SnapshotData snapshot)
        {
            if (!File.Exists(path))
            {
                return;
            }

            snapshot.AssetsFileFound = true;

            if (!TryParseJsonFile(path, out var root, out var warning))
            {
                snapshot.FormatWarnings.Add(warning);
                return;
            }

            if (root["items"] is not JArray items)
            {
                snapshot.FormatWarnings.Add($"'{SpecSnapshotWriter.DefaultRelativeAssetsPath}' に items(配列)がありません。");
                return;
            }

            snapshot.AssetsParsedOk = true;

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not JObject item)
                {
                    snapshot.FormatWarnings.Add($"'{SpecSnapshotWriter.DefaultRelativeAssetsPath}' の items[{i}] がオブジェクトではありません。");
                    continue;
                }

                if (item.Value<bool?>("archived") == true)
                {
                    continue; // 論理削除済みは同期対象外(SpecWebParser.ParseAssets と同じ扱い)
                }

                var assetType = (string)item["assetType"];
                var identifier = (string)item["identifier"];
                var id = (string)item["id"];

                if (string.IsNullOrEmpty(assetType) || string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(id))
                {
                    snapshot.FormatWarnings.Add($"'{SpecSnapshotWriter.DefaultRelativeAssetsPath}' の items[{i}] に id/assetType/identifier のいずれかがありません。");
                    continue;
                }

                snapshot.AssetsByKey[assetType + "::" + identifier] = item;
            }
        }

        private static void LoadTuningFile(string path, SnapshotData snapshot)
        {
            if (!File.Exists(path))
            {
                return;
            }

            snapshot.TuningFileFound = true;

            if (!TryParseJsonFile(path, out var root, out var warning))
            {
                snapshot.FormatWarnings.Add(warning);
                return;
            }

            var scalars = root["scalars"] as JArray;
            var tables = root["tables"] as JArray;

            if (scalars == null && tables == null)
            {
                snapshot.FormatWarnings.Add($"'{SpecSnapshotWriter.DefaultRelativeTuningPath}' に scalars/tables(配列)がありません。");
                return;
            }

            snapshot.TuningParsedOk = true;

            CollectTuningItems(scalars, "scalars", snapshot, snapshot.ScalarItems);
            CollectTuningItems(tables, "tables", snapshot, snapshot.TableItems);
        }

        private static void CollectTuningItems(JArray array, string arrayName, SnapshotData snapshot, List<JObject> destination)
        {
            if (array == null)
            {
                return;
            }

            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JObject item)
                {
                    snapshot.FormatWarnings.Add($"'{SpecSnapshotWriter.DefaultRelativeTuningPath}' の {arrayName}[{i}] がオブジェクトではありません。");
                    continue;
                }

                if (string.IsNullOrEmpty((string)item["id"]))
                {
                    snapshot.FormatWarnings.Add($"'{SpecSnapshotWriter.DefaultRelativeTuningPath}' の {arrayName}[{i}] に id がありません。");
                    continue;
                }

                destination.Add(item);
            }
        }

        private static bool TryParseJsonFile(string path, out JObject root, out string warning)
        {
            root = null;
            warning = null;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                warning = $"'{path}' を読み込めません: {e.Message}";
                return false;
            }

            try
            {
                root = JObject.Parse(text);
                return true;
            }
            catch (Exception e)
            {
                warning = $"'{path}' の JSON を解釈できません: {e.Message}";
                return false;
            }
        }

        // ── 全体(1回だけ計算する)結果: スナップショット無し・形式不良・未作成・調整値範囲外 ──

        private static List<ValidationResult> ComputeGlobalResults(SnapshotData snapshot, TuningTable table)
        {
            var results = new List<ValidationResult>();

            if (!snapshot.HasAnyFile)
            {
                results.Add(ValidationResult.Info(
                    "仕様書のスナップショットが見つかりません(Specs/assets.json・Specs/tuning.json)。" +
                    "仕様書同期(Tools > D-Drive > 仕様書と同期)を使っていないプロジェクトではこの検査は行われません。"));
                return results;
            }

            foreach (var warning in snapshot.FormatWarnings)
            {
                results.Add(ValidationResult.Warning(warning));
            }

            if (snapshot.AssetsParsedOk)
            {
                var existingIndex = SpecDiffService.BuildExistingIndex();
                foreach (var kv in snapshot.AssetsByKey)
                {
                    if (existingIndex.ContainsKey(kv.Key))
                    {
                        continue;
                    }

                    var displayName = (string)kv.Value["displayName"] ?? string.Empty;
                    results.Add(ValidationResult.Info(
                        $"仕様書に発注がありますが D-Drive に Data がありません: {kv.Key}({displayName})。"));
                }
            }

            results.AddRange(CheckTuningRanges(snapshot, table));

            return results;
        }

        private static IEnumerable<ValidationResult> CheckTuningRanges(SnapshotData snapshot, TuningTable table)
        {
            if (table == null || !snapshot.TuningParsedOk)
            {
                yield break;
            }

            foreach (var item in snapshot.ScalarItems)
            {
                var key = (string)item["id"];
                if (string.IsNullOrEmpty(key) || !TryGetNumericRange(item, out var min, out var max))
                {
                    continue;
                }

                if (!table.TryFindIndex(key, out var index))
                {
                    continue;
                }

                var entry = table.Entries[index];
                if (entry.Type != TuningValueType.Float && entry.Type != TuningValueType.Int)
                {
                    continue;
                }

                var value = entry.Type == TuningValueType.Float ? entry.ValueFloat : entry.ValueInt;
                if (value < min || value > max)
                {
                    yield return ValidationResult.Error(
                        $"調整値 '{key}' の値 {value} が仕様書の範囲 [{min}, {max}] の外です。");
                }
            }

            foreach (var item in snapshot.TableItems)
            {
                foreach (var result in CheckTuningTableRanges(item, table))
                {
                    yield return result;
                }
            }
        }

        private static IEnumerable<ValidationResult> CheckTuningTableRanges(JObject item, TuningTable table)
        {
            var tableKey = (string)item["id"];
            if (string.IsNullOrEmpty(tableKey) || !table.TryFindTableIndex(tableKey, out var tableIndex))
            {
                yield break;
            }

            if (item["columns"] is not JArray columns)
            {
                yield break;
            }

            var tableEntry = table.Tables[tableIndex];

            foreach (var columnToken in columns)
            {
                if (columnToken is not JObject columnObj)
                {
                    continue;
                }

                var columnKey = (string)columnObj["key"];
                if (string.IsNullOrEmpty(columnKey) || !TryGetNumericRange(columnObj, out var min, out var max))
                {
                    continue;
                }

                if (!TryFindColumn(tableEntry, columnKey, out var column) ||
                    (column.Type != TuningValueType.Float && column.Type != TuningValueType.Int))
                {
                    continue;
                }

                foreach (var row in tableEntry.Rows)
                {
                    if (!TryFindCell(row, columnKey, out var cell))
                    {
                        continue;
                    }

                    var value = column.Type == TuningValueType.Float ? cell.F : cell.I;
                    if (value < min || value > max)
                    {
                        yield return ValidationResult.Error(
                            $"調整値テーブル '{tableKey}' の行 '{row.RowId}' 列 '{columnKey}' の値 {value} が仕様書の範囲 [{min}, {max}] の外です。");
                    }
                }
            }
        }

        // Min==Max は「範囲チェック無効」を表す既存の約束(TuningEntry.Min の Tooltip、[27_spec_sheet.md] §3.2)。
        private static bool TryGetNumericRange(JObject item, out float min, out float max)
        {
            min = 0f;
            max = 0f;

            var minToken = item["min"];
            var maxToken = item["max"];
            if (!IsNumeric(minToken) || !IsNumeric(maxToken))
            {
                return false;
            }

            min = minToken.ToObject<float>();
            max = maxToken.ToObject<float>();
            return min != max;
        }

        private static bool IsNumeric(JToken token)
            => token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer);

        private static bool TryFindColumn(TuningTableEntry entry, string key, out TuningTableColumn column)
        {
            foreach (var c in entry.Columns)
            {
                if (string.Equals(c.Key, key, StringComparison.Ordinal))
                {
                    column = c;
                    return true;
                }
            }

            column = default;
            return false;
        }

        private static bool TryFindCell(TuningTableRow row, string columnKey, out TuningCellValue cell)
        {
            foreach (var c in row.Cells)
            {
                if (string.Equals(c.ColumnKey, columnKey, StringComparison.Ordinal))
                {
                    cell = c;
                    return true;
                }
            }

            cell = default;
            return false;
        }

        // ── 個別アセットに紐づく結果: インポート済なのに Placeholder・スナップショットに無い残存アセット ──

        private static IEnumerable<ValidationResult> ComputePerAssetResults(AssetDataBase data, SnapshotData snapshot)
        {
            if (data.Id == 0)
            {
                yield break; // ID 未発行は作成パイプライン/別 Validator の責務(AddressablesRegistrationValidator と同じ判断)
            }

            var path = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            var assetType = ResolveAssetType(data);
            if (assetType == AssetType.None)
            {
                yield break;
            }

            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!SpecIdentifierCodec.TryExtractIdentifier(fileName, assetType, out var identifier))
            {
                yield break;
            }

            var key = assetType + "::" + identifier;

            if (snapshot.AssetsParsedOk && snapshot.AssetsByKey.TryGetValue(key, out var item))
            {
                var status = (string)item["status"] ?? string.Empty;
                if (status == ImportedStatus && IsPlaceholder(data))
                {
                    yield return ValidationResult.Warning(
                        $"仕様書の状態は「{ImportedStatus}」ですが '{data.name}'({key}) はまだ Placeholder のようです(Validation でエラーがあります)。");
                }
            }
            else if (snapshot.AssetsParsedOk && !string.IsNullOrEmpty(data.SpecUrl))
            {
                yield return ValidationResult.Info(
                    $"'{data.name}'({key}) は仕様書のスナップショットに見つかりません(削除・アーカイブ・リネーム済みの可能性があります)。");
            }
        }

        private static AssetType ResolveAssetType(AssetDataBase data)
        {
            var attr = data.GetType().GetCustomAttribute<AssetIdDefinitionAttribute>();
            return attr?.Type ?? AssetType.None;
        }

        // docs/32_spec_web.md §10.4.1 の決定(既存 Validator の実行結果を再利用して Placeholder を判定する)を
        // そのまま踏襲する。
        //
        // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-6 (b)) — 自前で
        // `ValidatorRegistry` を組んで「自分自身を除く全 Validator」を 1 アセットに対して回していたのを、
        // 既存の「個別検証」(`DataValidationRunner.Run`、docs/09 §11)に置き換えた。旧実装の問題:
        //   - `IUniversalValidator` を含めていたため、**プロジェクト全体の Error が 1 件でもあると**
        //     チェックした「インポート済」アセット全件が「まだ Placeholder のようです」Warning に化けた
        //   - 内側の `RunAll` が全体系 Validator の「1 回だけ」ガード(`_lastRunContext` /
        //     `_noSettingsReported`)を書き換えるため、Report Window / CI に同じ Error が件数ぶん重複した
        // `DataValidationRunner` は「プロジェクト全体を 1 回まとめて見る Validator」(`SpecDiffValidator`
        // 自身と `ContentHashCatalogCoverageValidator`)を除外し、1 アセット単位で意味がある検査
        // (種別 Validator + ValueDef / Addressables 登録 / NetMode)だけを実行する
        // = 自分自身も外れるので無限再帰にもならない。Validator 一覧のキャッシュも向こうが持つ。
        private static bool IsPlaceholder(AssetDataBase data)
        {
            var results = DataValidationRunner.Run(data);
            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].Severity == ValidationSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
