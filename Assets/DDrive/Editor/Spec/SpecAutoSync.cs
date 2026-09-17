using System;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §4.2(旧 [27_spec_sheet.md])— Unity 起動時・ドメインリロード後に「取得と差分検出だけ」
    // を行う。自動では適用しない(例外: 設定で ON にした「新規行→Placeholder 自動作成」のみ。
    // 2026-09-17([41] P1-7): 状態が 3 値(発注済/納品済/インポート済)になり「未着手」は無くなったため
    // 文言から外した。実装も元から状態では絞っていない(Web に有って D-Drive に無い行すべてが対象))。
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
                    var message = $"仕様書(Web)の取得に失敗しました: {assetResult.Error}";
                    Debug.LogWarning($"[DDrive] {message}");
                    SpecCache.SetError(message); // P1-2 (b)/(c): 失敗を画面に出せるようにする(前回値は保持)
                    onComplete?.Invoke();
                    return;
                }

                var parsedAssets = SpecWebParser.ParseAssets(assetResult.Json, settings.HumanAppUrl);

                // 2026-09-17([41] P1-2 (b)) — GAS は常に HTTP 200 を返すため(ContentAdapter.js)、
                // トークン未設定/無効(401)・許可外(403)・レート制限でも request.result は Success になる。
                // 「取得できた」と「中身が使える」は別なので、エンベロープ段(行番号 0 の Issue = ok:false /
                // JSON 不正 / items 欠落)の失敗を見て、失敗なら前回のキャッシュを一切上書きしない。
                var assetsFailure = DescribeEnvelopeFailure(parsedAssets);
                if (assetsFailure != null)
                {
                    var message = $"仕様書(Web)の取得に失敗しました: {assetsFailure}";
                    Debug.LogWarning($"[DDrive] {message}");
                    SpecCache.SetError(message);
                    onComplete?.Invoke();
                    return;
                }

                var diff = SpecDiffService.ComputeDiff(parsedAssets);

                SpecWebFetcher.FetchGet(settings.WebAppUrl, "tuningScalarList", readToken, null, tuningResult =>
                {
                    // 失敗(取得失敗 or ok:false)のときは空の結果ではなく「前回値のまま」にする。
                    // 空の結果を載せると SpecSyncWindow の「適用」が TuningTable.Entries を全消しにする(P1-2)。
                    var parsedTuning = tuningResult.Success
                        ? SpecWebParser.ParseTuningScalars(tuningResult.Json)
                        : null;
                    var tuningFailure = tuningResult.Success
                        ? DescribeEnvelopeFailure(parsedTuning)
                        : tuningResult.Error;
                    var tuningForCache = tuningFailure == null ? parsedTuning : SpecCache.LastTuningRows;

                    SpecWebFetcher.FetchGet(settings.WebAppUrl, "tuningTableList", readToken, null, tuningTableResult =>
                    {
                        var parsedTuningTables = tuningTableResult.Success
                            ? SpecWebParser.ParseTuningTables(tuningTableResult.Json)
                            : null;
                        var tuningTableFailure = tuningTableResult.Success
                            ? DescribeEnvelopeFailure(parsedTuningTables)
                            : tuningTableResult.Error;

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

                        // P1-2 (b): 調整値側の失敗理由を LastError に載せる(以前は tuningScalarList の
                        // 取得失敗しか見ず、ok:false と tuningTableList の失敗は完全に黙っていた)。
                        var error = CombineErrors(
                            tuningFailure == null ? null : $"調整値(スカラー)を更新できませんでした: {tuningFailure}",
                            tuningTableFailure == null ? null : $"調整値(テーブル)を更新できませんでした: {tuningTableFailure}");
                        if (error != null)
                        {
                            Debug.LogWarning($"[DDrive] {error}(前回の取得結果を保持します)");
                        }

                        SpecCache.Set(parsedAssets, tuningForCache, diff, warning, error);
                        if (tuningTableFailure == null)
                        {
                            SpecCache.SetTuningTables(parsedTuningTables);
                        }

                        onComplete?.Invoke();
                    });
                });
            });
        }

        // 2026-09-17(docs/41_phase6_review_2026-09-17.md P1-2 (b)) — 行番号 0 の Issue は
        // SpecWebParser.TryParseEnvelope が積むエンベロープ段の失敗(`ok:false` / JSON 不正 /
        // items 欠落)。行ごとの Issue(重複識別子等)は「一部の行だけ無効」なので失敗扱いにしない。
        private static string DescribeEnvelopeFailure<T>(SpecParseResult<T> parsed)
        {
            if (parsed == null)
            {
                return "応答を解釈できませんでした。";
            }

            for (var i = 0; i < parsed.Issues.Count; i++)
            {
                if (parsed.Issues[i].RowNumber == 0)
                {
                    return parsed.Issues[i].Message;
                }
            }

            return null;
        }

        private static string CombineErrors(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
            {
                return string.IsNullOrEmpty(b) ? null : b;
            }

            return string.IsNullOrEmpty(b) ? a : a + " / " + b;
        }
    }
}
