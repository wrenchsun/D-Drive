using System;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.2 — Unity 起動時・ドメインリロード後に「取得と差分検出だけ」を行う。
    // 自動では適用しない(例外: 設定で ON にした「未着手の新規行→Placeholder 自動作成」のみ)。
    // テスト実行中(バッチモードでの CI テスト実行)やバッチモードでは走らせない([27] §4.2)。
    // ネットワーク待ちで Editor を止めないよう、delayCall 経由・完全非同期(コールバック)で行う。
    [InitializeOnLoad]
    public static class SpecAutoSync
    {
        private static bool _scheduled;

        static SpecAutoSync()
        {
            EditorApplication.delayCall += RunOnceOnLoad;
        }

        private static void RunOnceOnLoad()
        {
            if (_scheduled)
            {
                return;
            }

            _scheduled = true;

            if (ShouldSkip())
            {
                return;
            }

            var settings = DDriveSpecSettings.Load();
            if (settings == null || !settings.AutoFetchOnStartup || string.IsNullOrEmpty(settings.SpreadsheetUrl))
            {
                return; // URL 未設定なら何もしない([27] §5)
            }

            Run(settings, applyAutoPlaceholders: true);
        }

        // -batchmode(CI)と -runTests(Unity Test Framework の CLI 実行)のときは走らせない。
        // インタラクティブな Test Runner ウィンドウ経由の実行はここでは検出できないため、
        // 自動同期は「取得と差分検出のみ(Assets を書かない)」に留めている(docs/28 の「要判断」に記載)。
        private static bool ShouldSkip()
        {
            if (Application.isBatchMode)
            {
                return true;
            }

            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (string.Equals(arg, "-runTests", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // 手動同期ウィンドウ(SpecSyncWindow の「取得」)からも呼ぶ。applyAutoPlaceholders は
        // 起動時自動実行のときだけ true にする(手動「取得」では常に差分プレビューを見せるだけにする)。
        public static void Run(DDriveSpecSettings settings, bool applyAutoPlaceholders, Action onComplete = null)
        {
            if (settings == null || string.IsNullOrEmpty(settings.SpreadsheetUrl))
            {
                onComplete?.Invoke();
                return;
            }

            var assetUrl = SpecCsv.BuildGvizCsvUrl(settings.SpreadsheetUrl, settings.AssetSheetName);
            SpecFetcher.Fetch(assetUrl, settings.AssetSheetName, assetResult =>
            {
                if (!assetResult.Success)
                {
                    Debug.LogWarning($"[DDrive] 仕様書の取得に失敗しました: {assetResult.Error}");
                    onComplete?.Invoke();
                    return;
                }

                var parsedAssets = SpecSheetParser.ParseAssetSheet(assetResult.Csv);
                var diff = SpecDiffService.ComputeDiff(parsedAssets);

                var tuningUrl = SpecCsv.BuildGvizCsvUrl(settings.SpreadsheetUrl, settings.TuningSheetName);
                SpecFetcher.Fetch(tuningUrl, settings.TuningSheetName, tuningResult =>
                {
                    var parsedTuning = tuningResult.Success
                        ? SpecSheetParser.ParseTuningSheet(tuningResult.Csv)
                        : new SpecParseResult<SpecTuningRow>();

                    if (applyAutoPlaceholders && settings.AutoApplyNewPlaceholders && diff.New.Count > 0)
                    {
                        var applied = 0;
                        foreach (var change in diff.New)
                        {
                            if (SpecSyncService.ApplyNew(change, settings.GameDataRoot) != null)
                            {
                                applied++;
                            }
                        }

                        if (applied > 0)
                        {
                            Debug.Log($"[DDrive] 仕様書同期: 新規行 {applied} 件を自動で Placeholder 作成しました。");
                            diff = SpecDiffService.ComputeDiff(SpecSheetParser.ParseAssetSheet(assetResult.Csv));
                        }
                    }
                    else if (diff.New.Count + diff.Changed.Count > 0)
                    {
                        Debug.Log($"[DDrive] 仕様書に変更 {diff.New.Count + diff.Changed.Count} 件があります(Tools > D-Drive > 仕様書と同期で確認できます)。");
                    }

                    var warning = assetResult.Warning ?? tuningResult.Warning;
                    var error = tuningResult.Success ? null : tuningResult.Error;
                    SpecCache.Set(parsedAssets, parsedTuning, diff, warning, error);
                    onComplete?.Invoke();
                });
            });
        }
    }
}
