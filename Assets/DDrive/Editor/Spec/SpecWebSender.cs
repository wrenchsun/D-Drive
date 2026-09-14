using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Tuning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §5.2/§7/§8 W-12 — D-Drive → Web(GAS) への送信(選択肢・アセット実状態・
    // TUNING 定数のコード参照)。企画側が Web で入力した内容(本文・調整値の値・機能仕様ページ・
    // コメント)には一切触れない(送信するのは §5.2 の 3 kind: choices/assetState/tuningUsage のみ)。
    // 書き込みトークンで呼べる API をこの 3 kind に固定するサーバー側のゲートは
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
        // isPlaceholder / iconAssetId は現状信頼できる取得手段が無いため既定値を送る
        // (docs/32_spec_web.md §9 の要判断に引き継ぎ。下記コメント参照)。
        public static JObject BuildAssetStatePayload()
        {
            var lastSyncedAt = DateTime.UtcNow.ToString("o");
            var items = new JArray();

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

                items.Add(new JObject
                {
                    ["id"] = kv.Key,
                    ["created"] = true,
                    // isPlaceholder: D-Drive 側に「Placeholder かどうか」を表す専用フラグが無いため
                    // 常に false を送る(実際にはコンテンツが空の Placeholder のままの場合もある。
                    // 要判断として docs/32_spec_web.md §9 に引き継ぐ)。
                    ["isPlaceholder"] = false,
                    // iconAssetId: Web(Drive)側にアイコンをアップロードする実装は本チケットの範囲外
                    // (Drive API への書き込みが必要になるため)。常に null を送る(要判断として引き継ぐ)。
                    ["iconAssetId"] = null,
                    ["usageCount"] = usageCount,
                    ["lastSyncedAt"] = lastSyncedAt,
                });
            }

            return new JObject { ["items"] = items };
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

                var constName = "TUNING." + TuningCodegenConstantName(entry.Key);
                if (!sourceText.Any(text => text.IndexOf(constName, StringComparison.Ordinal) >= 0))
                {
                    unusedKeys.Add(entry.Key);
                }
            }

            return new JObject { ["unusedKeys"] = unusedKeys };
        }

        // TuningCodegen.ToConstantName は private のため、同じ規則をここに複製する
        // (公開するほど汎用ではない小さな文字列変換のため、依存を増やさず複製する判断。
        // TuningCodegen.cs のロジックを変更する場合はここも合わせて更新すること)。
        private static string TuningCodegenConstantName(string key)
        {
            var tokens = key.Split(new[] { '/', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var token in tokens)
            {
                var clean = new string(token.Where(c => char.IsLetterOrDigit(c)).ToArray());
                if (clean.Length == 0)
                {
                    continue;
                }

                sb.Append(char.ToUpperInvariant(clean[0]));
                if (clean.Length > 1)
                {
                    sb.Append(clean.Substring(1));
                }
            }

            if (sb.Length == 0)
            {
                sb.Append("Unnamed");
            }

            if (!char.IsLetter(sb[0]))
            {
                sb.Insert(0, '_');
            }

            return sb.ToString();
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
