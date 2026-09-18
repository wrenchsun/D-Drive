using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Validation
{
    // [26_timeline.md] §4.6.5 検出1 / §6(6-10d) — ゲームカメラ制御の実行順の契約を静的に検査する。
    //
    // (a) D-Drive 以外のランタイムスクリプトの実効実行順(ProjectSettings > Script Execution Order が
    //     優先、無ければ [DefaultExecutionOrder] 属性)が 1000(= DDriveCutsceneCameraApplier.ExecutionOrder)
    //     以上なら Warning(「LateUpdate でカメラを書いている場合、Cutscene のカメラが効きません」)。
    // (b) DDriveCutsceneCameraApplier 自身の実効実行順が 1000 でなければ Error(ProjectSettings で
    //     書き換えられている)。
    // (c) Assets/ 配下の .cs を ForbiddenApiScanner と同じテキスト走査で調べ、PlayerLoop.SetPlayerLoop /
    //     PostLateUpdate / WaitForEndOfFrame / onBeforeRender / beginCameraRendering を含むファイルを Info。
    //
    // CutsceneData が 1 件も無いプロジェクトでは省略する(Cutscene を使わないプロジェクトに無関係な
    // 検査を出さないため)。ContentHashCatalogCoverageValidator と同じ「IUniversalValidator + 1 回の
    // ValidationContext につき 1 回だけ実行する」ガードを使う(RunAll は Validator を asset ごとに
    // 呼ぶため、asset 数分重複報告しないようにする)。
    //
    // テスト用フック: 実際の ProjectSettings / MonoImporter を書き換えずに (a)/(b) をテストできるよう、
    // スクリプト一覧の取得を差し替え可能にしている(EditMode テストが独自の ScriptOrderInfo 列を注入する)。
    // (c) はファイルシステムの走査のみで ProjectSettings に依存しないため差し替えは用意していない。
    public sealed class CameraExecutionOrderValidator : IUniversalValidator
    {
        public readonly struct ScriptOrderInfo
        {
            public readonly string AssetPath;
            public readonly string TypeName;
            public readonly int EffectiveOrder;

            public ScriptOrderInfo(string assetPath, string typeName, int effectiveOrder)
            {
                AssetPath = assetPath;
                TypeName = typeName;
                EffectiveOrder = effectiveOrder;
            }
        }

        // DDriveCutsceneCameraApplier のクラス名(型を直接参照して nameof で取る。文字列直書きを避ける)。
        private static readonly string ApplierTypeName = nameof(DDriveCutsceneCameraApplier);

        // テスト用フック。既定は実際の MonoImporter を読む(ProjectSettings は読むだけで書き換えない)。
        public static Func<IEnumerable<ScriptOrderInfo>> ScriptOrderProvider = DefaultScriptOrderProvider;

        private static ValidationContext _lastRunContext;

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (_lastRunContext == ctx)
            {
                yield break;
            }

            _lastRunContext = ctx;

            if (!HasAnyCutscene(ctx))
            {
                yield break;
            }

            var applierFound = false;

            foreach (var info in ScriptOrderProvider())
            {
                if (info.TypeName == ApplierTypeName)
                {
                    applierFound = true;
                    if (info.EffectiveOrder != DDriveCutsceneCameraApplier.ExecutionOrder)
                    {
                        yield return ValidationResult.Error($"DDriveCutsceneCameraApplier の実効実行順が {info.EffectiveOrder} です。{DDriveCutsceneCameraApplier.ExecutionOrder} である必要があります(ProjectSettings > Script Execution Order を確認してください、[26_timeline.md] §4.6.5)。");
                    }

                    continue;
                }

                if (!IsDDrivePath(info.AssetPath) && info.EffectiveOrder >= DDriveCutsceneCameraApplier.ExecutionOrder)
                {
                    yield return ValidationResult.Warning($"スクリプト '{info.AssetPath}'({info.TypeName})の実効実行順が {info.EffectiveOrder} です(D-Drive の Cutscene カメラ適用順 {DDriveCutsceneCameraApplier.ExecutionOrder} 以上)。LateUpdate でカメラを書いている場合、Cutscene のカメラが効きません([26_timeline.md] §4.6.5 契約 G-1)。");
                }
            }

            // DDriveCutsceneCameraApplier が一覧に見つからない(=まだシーンに追加されていない等)場合は
            // 判定不能として黙る。エラーにはしない(Applier は Camera.main に初回自動追加されるだけの
            // コンポーネントで、常に存在するとは限らない)。

            foreach (var result in ScanRiskyPatternFiles())
            {
                yield return result;
            }

            _ = applierFound; // 将来「Applier のスクリプト自体が見つからない」検査を足す余地。現状は無視。
        }

        private static bool HasAnyCutscene(ValidationContext ctx)
        {
            var all = ctx.AllAssets;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] is CutsceneData)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDDrivePath(string assetPath)
            => assetPath != null && assetPath.Replace('\\', '/').StartsWith("Assets/DDrive/", StringComparison.Ordinal);

        private static IEnumerable<ScriptOrderInfo> DefaultScriptOrderProvider()
        {
            var scripts = MonoImporter.GetAllRuntimeMonoScripts();
            foreach (var script in scripts)
            {
                if (script == null)
                {
                    continue;
                }

                var cls = script.GetClass();
                if (cls == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(script);
                var order = MonoImporter.GetExecutionOrder(script);

                // MonoImporter.GetExecutionOrder は ProjectSettings > Script Execution Order に
                // 「明示的に登録された」上書き値だけを返す。[DefaultExecutionOrder] 属性を付けただけの
                // スクリプト(その ProjectSettings リストに一度も登録されていないもの)は 0 を返す
                // (属性値を自動で反映しない)。実機確認(execute_code)で
                // DDriveCutsceneCameraApplier([DefaultExecutionOrder(1000)])が GetExecutionOrder=0 を
                // 返すことを確認済み。「ProjectSettings の値があればそれ、無ければ DefaultExecutionOrder
                // 属性」([26_timeline.md] §4.6.5 検出1 の仕様どおり)を実現するため、0 のときだけ属性を
                // 読みにフォールバックする(明示的に 0 へ上書きされているケースとは区別できないが、
                // その場合も実効値は 0 のままなので実害は無い)。
                if (order == 0)
                {
                    var attrs = cls.GetCustomAttributes(typeof(DefaultExecutionOrder), false);
                    if (attrs.Length > 0)
                    {
                        order = ((DefaultExecutionOrder)attrs[0]).order;
                    }
                }

                yield return new ScriptOrderInfo(path, cls.Name, order);
            }
        }

        // 検出1(c) — [00_requirements.md]/ForbiddenApiScanner と同じテキスト走査(専用 Roslyn 解析への
        // 置き換えは将来の改善余地、簡易版)。誤検知は許容し重度を上げない(Info のまま)。
        private static readonly Regex RiskyPattern = new(
            @"PlayerLoop\.SetPlayerLoop|PostLateUpdate|WaitForEndOfFrame|onBeforeRender|beginCameraRendering",
            RegexOptions.Compiled);

        private static IEnumerable<ValidationResult> ScanRiskyPatternFiles()
        {
            const string root = "Assets";
            if (!Directory.Exists(root))
            {
                yield break;
            }

            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var normalized = file.Replace('\\', '/');

                // このファイル自身の文字列がパターンに一致してしまうため自己除外する
                // (ForbiddenApiScanner.cs と同じ自己検出回避)。
                if (normalized.EndsWith("CameraExecutionOrderValidator.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(normalized);
                }
                catch (IOException)
                {
                    continue;
                }

                if (RiskyPattern.IsMatch(text))
                {
                    yield return ValidationResult.Info($"スクリプト '{normalized}' に PlayerLoop/onBeforeRender/描画コールバックの使用が見つかりました。カメラへの書き込みが Cutscene の実行順の契約(G-1〜G-5)に違反していないか確認してください([26_timeline.md] §4.6.5)。");
                }
            }
        }
    }
}
