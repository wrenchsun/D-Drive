using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Tuning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §5.2/§7/§8 W-12・§10.4.2 O-6 — D-Drive → Web(GAS) への送信(選択肢・
    // アセット実状態・TUNING 定数のコード参照・パラメータスキーマ/現在値)。企画側が Web で
    // 入力した内容(本文・調整値の値・機能仕様ページ・コメント)には一切触れない(送信するのは
    // choices/assetState/tuningUsage/assetParams の 4 kind のみ)。assetParams も一方向
    // (D-Drive → Web)専用で、対応する書き込み API(Web 側が値を書き換える経路)は存在しない([32] §10.4.2)。
    // 書き込みトークンで呼べる API をこの 4 kind に固定するサーバー側のゲートは
    // Tools/SpecWeb/src/Code.js(handleApiRequest_)と Tools/SpecWeb/src/DDriveSync.js を参照。
    public static class SpecWebSender
    {
        // ── choices ──

        public static void SendChoices(string webAppUrl, string writeToken, Action<SpecWebFetchResult> onComplete)
        {
            var payload = BuildChoicesPayload();
            SpecWebFetcher.FetchPost(webAppUrl, "choices", writeToken, payload.ToString(Formatting.None), onComplete);
        }

        // AssetType 一覧・既存アセットから収集したカテゴリ一覧([32] §5.2「選択肢」)。
        public static JObject BuildChoicesPayload()
        {
            var assetTypes = Enum.GetValues(typeof(AssetType)).Cast<AssetType>()
                .Where(t => t != AssetType.None)
                .Select(t => t.ToString())
                .ToArray();

            var categories = SpecDiffService.BuildExistingIndex().Values
                .Select(a => a.Category)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToArray();

            return new JObject
            {
                ["assetTypes"] = new JArray(assetTypes),
                ["categories"] = new JArray(categories),
                // タグの統制語彙(TagCatalog 相当)は現状 D-Drive 側に存在しないため、空配列を送る
                // (docs/32_spec_web.md §9 の要判断に引き継ぎ: TagCatalog 実装後に候補を収集する)。
                ["tags"] = new JArray(),
            };
        }

        // ── assetState ──

        public static void SendAssetState(string webAppUrl, string writeToken, Action<SpecWebFetchResult> onComplete)
        {
            var payload = BuildAssetStatePayload();
            SpecWebFetcher.FetchPost(webAppUrl, "assetState", writeToken, payload.ToString(Formatting.None), onComplete);
        }

        // 作成済みか・使用箇所数・最終同期時刻等([32] §5.2「アセットの実状態」)。
        //
        // 追補(2026-09-14): isPlaceholder / hasIcon を実値にした(旧: 常に false/null。
        // docs/32_spec_web.md §9 の要判断 14 に記載していた内容への対応)。
        //   isPlaceholder: 「その Data の必須参照が未設定」を表す専用フラグは D-Drive 側に無いが、
        //     既存の各 Validator(IValidator、種別ごとに実装済み。例: SeDataValidator の
        //     「Clip が未設定(または Missing)です」)がまさに同じ判定を Error として持っている。
        //     これを再利用し、「その Data に対する Validator の結果に 1 件以上 Error があるか」を
        //     isPlaceholder とする(CI.RunValidation() が全 Validator を発見して実行する既存の
        //     エントリポイント。Validation > Run All と同じもの)。Warning は許容(Placeholder 扱いしない)。
        //   iconAssetId: Web(Drive)側にアイコンをアップロードする実装は本チケットの範囲外
        //     (Drive API への書き込みが必要になるため)。常に null のまま送る(要判断として引き継ぐ)。
        //   hasIcon(新設): アイコンを Drive にアップロードせずに「D-Drive 側にアイコンが割り当て済みか」
        //     だけを bool で伝える。GAS の ContentService/doPost の応答は大きな base64 画像を都度
        //     送るには不向き(応答サイズ・実行時間の余裕を消費する)なため、実装コストと得られる
        //     情報量を比べて「あり/なし」の bool だけを追加する方を選んだ(要判断として docs に記載)。
        public static JObject BuildAssetStatePayload()
        {
            var lastSyncedAt = DateTime.UtcNow.ToString("o");
            var items = new JArray();
            var assetsWithErrors = FindAssetPathsWithValidationErrors();

            foreach (var kv in SpecDiffService.BuildExistingIndex())
            {
                var separator = kv.Key.IndexOf("::", StringComparison.Ordinal);
                if (separator < 0)
                {
                    continue;
                }

                var typeName = kv.Key.Substring(0, separator);
                if (!Enum.TryParse<AssetType>(typeName, out var assetType))
                {
                    continue;
                }

                var asset = kv.Value;
                var usageCount = 0;
                try
                {
                    usageCount = DependencyGraphService.FindUsages(assetType, asset.Id).Count;
                }
                catch (Exception e)
                {
                    // 依存グラフキャッシュが無い/壊れている場合でも送信全体を止めない(CLAUDE.md §0-4)。
                    Debug.LogWarning($"[DDrive] '{kv.Key}' の使用箇所数を取得できませんでした: {e.Message}");
                }

                var assetPath = AssetDatabase.GetAssetPath(asset);
                items.Add(new JObject
                {
                    ["id"] = kv.Key,
                    ["created"] = true,
                    ["isPlaceholder"] = !string.IsNullOrEmpty(assetPath) && assetsWithErrors.Contains(assetPath),
                    ["iconAssetId"] = null,
                    ["hasIcon"] = asset.Icon != null,
                    ["usageCount"] = usageCount,
                    ["lastSyncedAt"] = lastSyncedAt,
                });
            }

            return new JObject { ["items"] = items };
        }

        // Validation(CI.RunValidation、[Editor > Validation > Run All]と同じ Validator 発見規則)の
        // 結果から、Error severity が 1 件以上ある AssetDataBase のアセットパス集合を作る。
        // 例外を投げず(CLAUDE.md §0-4)、失敗時は「Error 無し」扱い(isPlaceholder は false 側へ倒す
        // 保守的な既定)にする。
        //
        // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-6 (a)): プロジェクト全体を
        // 1 回まとめて見る Validator を除外する(`includeProjectWideValidators: false`)。
        // `ValidatorRegistry.RunAll` は `IUniversalValidator` の結果も「その時渡されたアセット」に
        // 紐付けるため、以前は「調整値が 1 つでも範囲外(SpecDiffValidator)/ カタログがラベル未登録
        // (ContentHashCatalogCoverageValidator)」だと**無関係なアセット**が `isPlaceholder=true` で
        // 送られ、`assetParams` からも外れて Web 側で「インポート済」に進めなかった。
        // 1 アセット単位で意味がある検査(種別 Validator + ValueDef / Addressables 登録 / NetMode)は
        // 従来どおり isPlaceholder に含める。
        private static HashSet<string> FindAssetPathsWithValidationErrors()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var report in DDrive.Editor.CI.RunValidation(includeProjectWideValidators: false))
                {
                    if (report.Result.Severity != ValidationSeverity.Error || report.Asset == null)
                    {
                        continue;
                    }

                    var path = AssetDatabase.GetAssetPath(report.Asset);
                    if (!string.IsNullOrEmpty(path))
                    {
                        result.Add(path);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] isPlaceholder 判定用の Validation 実行に失敗しました(isPlaceholder は false 扱いになります): {e.Message}");
            }

            return result;
        }

        // ── assetParams ──

        // [32] §10.4.2(O-6) — パラメータのスキーマ(16種類分、ControlSkin は2件)+ インポート済
        // アセットの現在値を1回で送る。書き込みトークンの許可表(Tools/SpecWeb/src/Code.js の
        // DDRIVE_WRITE_TOKEN_ALLOWED_APIS)に 'assetParams' を追加したのも本チケット(O-6 D-Drive 側)。
        public static void SendAssetParams(string webAppUrl, string writeToken, Action<SpecWebFetchResult> onComplete)
        {
            var payload = BuildAssetParamsPayload();
            SpecWebFetcher.FetchPost(webAppUrl, "assetParams", writeToken, payload.ToString(Formatting.None), onComplete);
        }

        // { schemas: [...], items: [...] }([32] §10.4.2)。items は isPlaceholder=false
        // (assetState と同じ Validation Error 判定、§10.4.1)のアセットだけを対象にする。
        public static JObject BuildAssetParamsPayload()
        {
            var schemas = SpecParamSchemaBuilder.BuildSchemas();
            var items = new JArray();
            var assetsWithErrors = FindAssetPathsWithValidationErrors();

            foreach (var kv in SpecDiffService.BuildExistingIndex())
            {
                var asset = kv.Value;
                if (asset == null)
                {
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(asset);
                var isPlaceholder = !string.IsNullOrEmpty(assetPath) && assetsWithErrors.Contains(assetPath);
                if (isPlaceholder)
                {
                    continue; // [32] §10.4.2: インポート済(isPlaceholder=false)のみ現在値を送る
                }

                try
                {
                    items.Add(new JObject
                    {
                        ["id"] = kv.Key,
                        ["concreteType"] = asset.GetType().Name,
                        ["currentValues"] = SpecParamSchemaBuilder.BuildCurrentValues(asset),
                    });
                }
                catch (Exception e)
                {
                    // CLAUDE.md §0-4: 1 件失敗しても送信全体は止めない。
                    Debug.LogWarning($"[DDrive] '{kv.Key}' のパラメータ現在値の取得に失敗しました: {e.Message}");
                }
            }

            return new JObject { ["schemas"] = schemas, ["items"] = items };
        }

        // ── tuningUsage ──

        public static void SendTuningUsage(string webAppUrl, string writeToken, TuningTable table, Action<SpecWebFetchResult> onComplete)
        {
            var payload = BuildTuningUsagePayload(table);
            SpecWebFetcher.FetchPost(webAppUrl, "tuningUsage", writeToken, payload.ToString(Formatting.None), onComplete);
        }

        // TUNING 定数へのコード参照が無いキーの一覧([32] §3.2.4「コード未使用の検出」)。
        private static readonly string[] ScanRoots = { "DDrive", "Generated" };

        public static JObject BuildTuningUsagePayload(TuningTable table)
        {
            var unusedKeys = new JArray();
            if (table == null || table.Entries == null)
            {
                return new JObject { ["unusedKeys"] = unusedKeys };
            }

            var sourceText = ReadAllScannableSource();
            foreach (var entry in table.Entries)
            {
                if (string.IsNullOrEmpty(entry.Key))
                {
                    continue;
                }

                // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-10): 同じ規則の
                // 複製をやめ、TuningCodegen.ToConstantName(internal 化)をそのまま使う。
                var constName = "TUNING." + TuningCodegen.ToConstantName(entry.Key);
                if (!sourceText.Any(text => text.IndexOf(constName, StringComparison.Ordinal) >= 0))
                {
                    unusedKeys.Add(entry.Key);
                }
            }

            return new JObject { ["unusedKeys"] = unusedKeys };
        }

        private static List<string> ReadAllScannableSource()
        {
            var texts = new List<string>();
            foreach (var root in ScanRoots)
            {
                var rootPath = Path.Combine(Application.dataPath, root);
                if (!Directory.Exists(rootPath))
                {
                    continue;
                }

                foreach (var file in Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories))
                {
                    var normalized = file.Replace('\\', '/');
                    if (normalized.EndsWith("/Generated/Tuning.g.cs", StringComparison.Ordinal))
                    {
                        continue; // 生成ファイル自身に定数が並ぶのは当然なので除外
                    }

                    try
                    {
                        texts.Add(File.ReadAllText(normalized));
                    }
                    catch (Exception)
                    {
                        // 読めないファイルはスキップする(CLAUDE.md §0-4)。
                    }
                }
            }

            return texts;
        }
    }
}
