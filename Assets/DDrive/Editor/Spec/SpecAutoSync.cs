using System;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §4.2(旧 [27_spec_sheet.md])— Unity 起動時・ドメインリロード後に「取得と差分検出だけ」
    // を行う。自動では適用しない(例外: 設定で ON にした「未着手の新規行→Placeholder 自動作成」のみ)。
    // テスト実行中(バッチモードでの CI テスト実行)やバッチモードでは走らせない。
    // ネットワーク待ちで Editor を止めないよう、delayCall 経由・完全非同期(コールバック)で行う。
    //
    // W-9(2026-09-14): 取得元を Google スプレッドシート(gviz CSV, SpecFetcher/SpecSheetParser)から
    // Web アプリ(GAS)の API(SpecWebFetcher/SpecWebParser)に差し替えた。3 種類の取得
    // (assets.list・tuningScalarList・tuningTableList)を順に行い、最後に W-11 の
    // SpecSnapshotWriter で Specs/*.json へスナップショットを書き出す。
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
            if (settings == null || !settings.AutoFetchOnStartup || string.IsNullOrEmpty(settings.WebAppUrl))
            {
                return; // URL 未設定なら何もしない([32] §5)
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
            if (settings == null || string.IsNullOrEmpty(settings.WebAppUrl))
            {
                onComplete?.Invoke();
                return;
            }

            var readToken = DDriveSpecSettings.ReadToken;

            SpecWebFetcher.FetchGet(settings.WebAppUrl, "assets.list", readToken, "includeArchived=1", assetResult =>
            {
                if (!assetResult.Success)
                {
                    Debug.LogWarning($"[DDrive] 仕様書(Web)の取得に失敗しました: {assetResult.Error}");
                    onComplete?.Invoke();
                    return;
                }

                var parsedAssets = SpecWebParser.ParseAssets(assetResult.Json, settings.HumanAppUrl);
                var diff = SpecDiffService.ComputeDiff(parsedAssets);

                SpecWebFetcher.FetchGet(settings.WebAppUrl, "tuningScalarList", readToken, null, tuningResult =>
                {
                    var parsedTuning = tuningResult.Success
                        ? SpecWebParser.ParseTuningScalars(tuningResult.Json)
                        : new SpecParseResult<SpecTuningRow>();

                    SpecWebFetcher.FetchGet(settings.WebAppUrl, "tuningTableList", readToken, null, tuningTableResult =>
                    {
                        var parsedTuningTables = tuningTableResult.Success
                            ? SpecWebParser.ParseTuningTables(tuningTableResult.Json)
                            : new SpecParseResult<SpecTuningTableRow>();

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
                                diff = SpecDiffService.ComputeDiff(SpecWebParser.ParseAssets(assetResult.Json, settings.HumanAppUrl));
                            }
                        }
                        else if (diff.New.Count + diff.Changed.Count > 0)
                        {
                            Debug.Log($"[DDrive] 仕様書に変更 {diff.New.Count + diff.Changed.Count} 件があります(Tools > D-Drive > 仕様書と同期で確認できます)。");
                        }

                        // W-11: 取得できた分だけ Specs/*.json へスナップショットを書き出す(git 履歴の代用、[32] §1.4)。
                        var snapshot = SpecSnapshotWriter.Write(assetResult.Json, tuningResult.Json, tuningTableResult.Json);
                        if (!string.IsNullOrEmpty(snapshot.Warning))
                        {
                            Debug.LogWarning($"[DDrive] Specs/*.json の書き出しで警告: {snapshot.Warning}");
                        }

                        var warning = assetResult.Warning ?? tuningResult.Warning ?? tuningTableResult.Warning;
                        var error = tuningResult.Success ? null : tuningResult.Error;
                        SpecCache.Set(parsedAssets, parsedTuning, diff, warning, error);
                        SpecCache.SetTuningTables(parsedTuningTables);
                        onComplete?.Invoke();
                    });
                });
            });
        }
    }
}
