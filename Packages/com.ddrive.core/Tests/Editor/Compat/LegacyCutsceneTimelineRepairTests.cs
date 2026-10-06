using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DDrive.Editor;
using DDrive.Editor.Migration;
using DDrive.Editor.Settings;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace DDrive.Tests.Editor.Compat
{
    // [64_review_m6_2026-10-06.md] GF-R-01〜04 のテスト。旧形式(v1.3.1 以前)の .playable が
    // (1) Id 記録済みでも検出・再判定される(Validator / Plan)、(2) 保存で失われない(GF-R-02 の実測)、
    // (3) 1 ファイルの失敗で全体が止まらず警告にまとまる、(4) 開いて未保存の Timeline は書き換えない、ことを固定する。
    // フィクスチャは `.playable.txt`(Packages 内でマイグレーションの対象にならない)を一時フォルダへ `.playable` としてコピーして使う。
    public class LegacyCutsceneTimelineRepairTests
    {
        private const string FixturePath = "Packages/com.ddrive.core/Tests/Editor/Compat/Fixtures/legacy_m6/Legacy_Cutscene_Timeline_v1_3_1.playable.txt";
        private const string SignalIdent = "m_EditorClassIdentifier: DDrive.Runtime:DDrive.Runtime.Cutscene.Tracks:";

        private string _tempDir;
        private Func<string, bool> _savedProbe;

        [SetUp]
        public void SetUp()
        {
            _tempDir = null;
            // [64] GF-R-17 — 件数を `Assets/` 全体で数えるテストがあるので、旧形式の .playable が既に残っているプロジェクト
            // (= 「更新を適用」の前の持ち込み先)では前提を満たさない。実プロジェクトの状態に依存して赤くならないよう保留にする。
            if (CutsceneTimelineScriptReferenceMigration.FindTargetPaths().Count > 0)
            {
                Assert.Ignore("プロジェクトに旧形式の .playable が既に残っている(マイグレーションを適用してから実行する)");
            }

            _savedProbe = CutsceneTimelineScriptReferenceMigration.IsDirtyProbe;
            _tempDir = "Assets/__M6RepairTest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_tempDir == null)
            {
                return;
            }

            CutsceneTimelineScriptReferenceMigration.IsDirtyProbe = _savedProbe;
            AssetDatabase.DeleteAsset(_tempDir);
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }

            var meta = _tempDir + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }

            AssetDatabase.Refresh();
        }

        private string CopyFixture(string name)
        {
            var path = _tempDir + "/" + name + ".playable";
            File.Copy(Path.GetFullPath(FixturePath), path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return path;
        }

        private static ValidationContext NewContext() => new ValidationContext(new List<AssetDataBase>());

        // ── GF-R-01(a): Validator ──

        [Test]
        public void Validator_ReportsLegacyTimeline_AsError_AndFixActionClearsIt()
        {
            var path = CopyFixture("Legacy");
            var results = new List<ValidationResult>(new CutsceneTimelineLegacyReferenceValidator().Validate(null, NewContext()));
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Error, results[0].Severity);
            Assert.AreEqual(CutsceneTimelineLegacyReferenceValidator.Code, results[0].Code);
            StringAssert.Contains(path, results[0].Message);
            Assert.IsNotNull(results[0].FixAction);

            results[0].FixAction();

            Assert.IsEmpty(new List<ValidationResult>(new CutsceneTimelineLegacyReferenceValidator().Validate(null, NewContext())));
        }

        [Test]
        public void Validator_FixAction_WarnsInsteadOfSilentNoOp_WhenTheTimelineCannotBeRewritten()
        {
            var path = CopyFixture("Legacy");
            var before = File.ReadAllText(path);
            var results = new List<ValidationResult>(new CutsceneTimelineLegacyReferenceValidator().Validate(null, NewContext()));
            Assert.AreEqual(1, results.Count);

            CutsceneTimelineScriptReferenceMigration.IsDirtyProbe = _ => true;
            LogAssert.Expect(LogType.Warning, new Regex(@"DD-CUTSCENE-LEGACY-SCRIPT-REF: 一部の Timeline を修正できませんでした"));
            results[0].FixAction();

            Assert.AreEqual(before, File.ReadAllText(path), "書き換えられないときはファイルを変えない");
        }

        [Test]
        public void Validator_ReportsOncePerContext_AndNothingWhenClean()
        {
            CopyFixture("Legacy");
            var validator = new CutsceneTimelineLegacyReferenceValidator();
            var ctx = NewContext();
            Assert.AreEqual(1, new List<ValidationResult>(validator.Validate(null, ctx)).Count);
            Assert.AreEqual(0, new List<ValidationResult>(validator.Validate(null, ctx)).Count, "同じ ValidationContext では 2 回目は報告しない");
        }

        [Test]
        public void Validator_IsRegisteredAsProjectWide_AndDiscoverable()
        {
            var found = false;
            foreach (var v in CI.DiscoverValidators())
            {
                if (v is CutsceneTimelineLegacyReferenceValidator)
                {
                    found = true;
                    Assert.IsTrue(DataValidationRunner.IsProjectWide(v));
                }
            }

            Assert.IsTrue(found, "CI.RunValidation(= ValidateAll)が発見できる");
        }

        [Test]
        public void RunValidation_IncludesTheLegacyTimelineError()
        {
            CopyFixture("Legacy");
            var reports = CI.RunValidation();
            var hit = false;
            foreach (var r in reports)
            {
                if (r.Result.Code == CutsceneTimelineLegacyReferenceValidator.Code && r.Result.Severity == ValidationSeverity.Error)
                {
                    hit = true;
                }
            }

            Assert.IsTrue(hit, "Run All / CI.ValidateAll に Error として出る(Player ビルド前の CI が赤になる)");
        }

        // ── GF-R-01(c): Id 記録済みでも再判定 ──

        [Test]
        public void Plan_IncludesMigration_WhenIdIsRecordedButLegacyRemains()
        {
            var migration = new CutsceneTimelineScriptReferenceMigration();
            var settings = DDriveProjectSettings.instance;
            if (!settings.HasAppliedMigration(migration.Id))
            {
                Assert.Ignore("このプロジェクトではまだ適用済みの記録が無い(再判定の前提を満たさない)");
            }

            var before = DDriveMigrationRunner.Plan(Array.Empty<IDataMigration>(), Array.Empty<AssetDataBase>(), new IProjectMigration[] { migration }, settings);
            Assert.AreEqual(0, before.ProjectMigrations.Count, "旧形式が無ければ Id 記録済みは計画に入らない");

            CopyFixture("Legacy");
            var after = DDriveMigrationRunner.Plan(Array.Empty<IDataMigration>(), Array.Empty<AssetDataBase>(), new IProjectMigration[] { migration }, settings);
            Assert.AreEqual(1, after.ProjectMigrations.Count, "Id 記録済みでも旧形式が残っていれば計画に入る");
            Assert.IsTrue(DDriveMigrationRunner.HasPendingMigrations(), "CI.MigrateCheck(HasPendingMigrations)が赤になる");

            CutsceneTimelineScriptReferenceMigration.MigratePaths(new MigrationContext(false), CutsceneTimelineScriptReferenceMigration.FindTargetPaths());
            var fixedPlan = DDriveMigrationRunner.Plan(Array.Empty<IDataMigration>(), Array.Empty<AssetDataBase>(), new IProjectMigration[] { migration }, settings);
            Assert.AreEqual(0, fixedPlan.ProjectMigrations.Count);
        }

        // ── GF-R-03: ファイル単位で続行・走査範囲 ──

        [Test]
        public void MigratePaths_ContinuesAfterAFailure_AndReportsWarnings()
        {
            var locked = CopyFixture("Locked");
            var ok = CopyFixture("Ok");
            var lockedBefore = File.ReadAllText(locked);
            var ctx = new MigrationContext(false);
            int rewritten;

            // 読み取りだけ許す共有で開いておくと、書き込みが IOException になる(Windows)。他の OS では擬似的に失敗を作る。
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                using (new FileStream(Path.GetFullPath(locked), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    rewritten = CutsceneTimelineScriptReferenceMigration.MigratePaths(ctx, new[] { locked, ok });
                }
            }
            else
            {
                CutsceneTimelineScriptReferenceMigration.IsDirtyProbe = p => p == locked;
                rewritten = CutsceneTimelineScriptReferenceMigration.MigratePaths(ctx, new[] { locked, ok });
            }

            Assert.AreEqual(1, rewritten, "失敗した 1 件があっても、残りは書き換える");
            Assert.GreaterOrEqual(ctx.WarningCount, 1);
            StringAssert.Contains(locked, string.Join("\n", ctx.Log));
            Assert.AreEqual(lockedBefore, File.ReadAllText(locked));
            Assert.IsNull(CutsceneTimelineScriptReferenceMigration.Rewrite(File.ReadAllText(ok), (a, n) => "x", out _), "成功した 1 件は直っている");
            var remaining = CutsceneTimelineScriptReferenceMigration.FindTargetPaths().FindAll(p => p.StartsWith(_tempDir + "/", StringComparison.Ordinal));
            Assert.AreEqual(1, remaining.Count, "書き換えられなかった 1 件だけが残る(このテストの一時フォルダ内で数える)");
        }

        [Test]
        public void Apply_DoesNotReportSuccessOrRecord_WhenAMigrationWarns()
        {
            var plan = DDriveMigrationRunner.Plan(
                Array.Empty<IDataMigration>(), Array.Empty<AssetDataBase>(), new IProjectMigration[] { new WarningMigration() }, settings: null);
            Assert.AreEqual(1, plan.ProjectMigrations.Count);

            var ctx = DDriveMigrationRunner.Apply(plan, settings: null);

            Assert.AreEqual(1, ctx.WarningCount);
            var log = string.Join("\n", ctx.Log);
            StringAssert.Contains("適用済みとして記録しませんでした", log);
            StringAssert.DoesNotContain("プロジェクト全体のマイグレーション", log.Replace("適用済みとして記録しませんでした", string.Empty), "成功の記録(Note)を出さない");
        }

        // GF-R-13: 例外も失敗として数える
        [Test]
        public void Apply_DoesNotReportSuccessOrRecord_WhenAMigrationThrows()
        {
            var migration = new ThrowingProjectMigration();
            var settings = DDriveProjectSettings.instance;
            var plan = DDriveMigrationRunner.Plan(
                Array.Empty<IDataMigration>(), Array.Empty<AssetDataBase>(), new IProjectMigration[] { migration }, settings);
            Assert.AreEqual(1, plan.ProjectMigrations.Count);

            LogAssert.ignoreFailingMessages = true;
            MigrationContext ctx;
            try
            {
                ctx = DDriveMigrationRunner.Apply(plan, settings);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.AreEqual(1, ctx.WarningCount, "例外も警告として数える(メニュー・更新ウィンドウが成功と出さない)");
            var log = string.Join("\n", ctx.Log);
            StringAssert.Contains("InvalidOperationException", log);
            StringAssert.Contains("適用済みとして記録しませんでした", log);
            Assert.IsFalse(settings.HasAppliedMigration(migration.Id), "例外のときは Id を記録しない");
        }

        [Test]
        public void ScanScope_IsAssetsOnly()
        {
            CopyFixture("Legacy");
            foreach (var p in CutsceneTimelineScriptReferenceMigration.FindTargetPaths())
            {
                StringAssert.StartsWith("Assets/", p);
            }
        }

        // ── GF-R-04: 開いて未保存の Timeline ──

        [Test]
        public void DirtyOpenTimeline_IsNotRewritten_AndWarns()
        {
            var path = CopyFixture("Legacy");
            var before = File.ReadAllText(path);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            Assert.IsNotNull(timeline);
            EditorUtility.SetDirty(timeline);

            var ctx = new MigrationContext(false);
            var rewritten = CutsceneTimelineScriptReferenceMigration.MigratePaths(ctx, new[] { path });

            Assert.AreEqual(0, rewritten);
            Assert.GreaterOrEqual(ctx.WarningCount, 1);
            StringAssert.Contains("未保存", string.Join("\n", ctx.Log));
            Assert.AreEqual(before, File.ReadAllText(path), "未保存の Timeline はファイルを書き換えない");
        }

        [Test]
        public void ProbeFalse_RewritesNormally()
        {
            var path = CopyFixture("Legacy");
            CutsceneTimelineScriptReferenceMigration.IsDirtyProbe = _ => false;
            var ctx = new MigrationContext(false);
            Assert.AreEqual(1, CutsceneTimelineScriptReferenceMigration.MigratePaths(ctx, new[] { path }));
            Assert.AreEqual(0, ctx.WarningCount);
        }

        // ── GF-R-02: マイグレーション前に開いて保存したときにデータが失われるか(実測を固定) ──

        [Test]
        public void SavingBeforeMigration_KeepsLegacyObjects_AndMigrationStillRecoversThem()
        {
            var path = CopyFixture("Legacy");
            var before = File.ReadAllText(path);
            var beforeKeys = Keys(before);
            var beforeDocs = CountOf(before, "--- !u!");
            var beforeLegacy = CountOf(before, SignalIdent);
            Assert.AreEqual(6, beforeLegacy);

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            Assert.IsNotNull(timeline);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            var after = File.ReadAllText(path);

            Debug.Log($"[GF-R-02] 保存前後: docs {beforeDocs}→{CountOf(after, "--- !u!")}、旧形式の識別子 {beforeLegacy}→{CountOf(after, SignalIdent)}、"
                      + $"m_Script fileID 0 {CountOf(before, "m_Script: {fileID: 0}")}→{CountOf(after, "m_Script: {fileID: 0}")}、Key {string.Join(",", beforeKeys)}→{string.Join(",", Keys(after))}");

            Assert.AreEqual(beforeDocs, CountOf(after, "--- !u!"), "オブジェクトの数が保存で変わらない");
            Assert.AreEqual(beforeLegacy, CountOf(after, SignalIdent), "旧形式のトラック / マーカーが保存で落ちない");
            CollectionAssert.AreEquivalent(beforeKeys, Keys(after), "マーカーの値(Key)が保存で失われない");

            // その後にマイグレーションすれば、トラック 1 + マーカー 5 が値ごと読める
            CutsceneTimelineScriptReferenceMigration.IsDirtyProbe = _ => false;
            CutsceneTimelineScriptReferenceMigration.MigratePaths(new MigrationContext(false), new[] { path });
            AssertTimelineLoads(path, beforeKeys);
        }

        [Test]
        public void EditingAndSavingBeforeMigration_DoesNotDropLegacyObjects()
        {
            var path = CopyFixture("Legacy");
            var before = File.ReadAllText(path);
            var beforeKeys = Keys(before);

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            Assert.IsNotNull(timeline);
            TrackAsset added = null;
            try
            {
                added = timeline.CreateTrack<MarkerTrack>(null, "added");
            }
            catch (Exception e)
            {
                Debug.Log("[GF-R-02] 旧形式のまま CreateTrack が例外: " + e.GetType().Name + ": " + e.Message);
            }

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            var after = File.ReadAllText(path);
            Debug.Log($"[GF-R-02] 編集して保存: 追加トラック {(added != null ? "あり" : "なし")}、旧形式の識別子 {CountOf(before, SignalIdent)}→{CountOf(after, SignalIdent)}、Key {string.Join(",", beforeKeys)}→{string.Join(",", Keys(after))}");

            Assert.AreEqual(CountOf(before, SignalIdent), CountOf(after, SignalIdent), "トラックを足して保存しても旧形式のオブジェクトが落ちない");
            CollectionAssert.AreEquivalent(beforeKeys, Keys(after));

            CutsceneTimelineScriptReferenceMigration.IsDirtyProbe = _ => false;
            CutsceneTimelineScriptReferenceMigration.MigratePaths(new MigrationContext(false), new[] { path });
            AssertTimelineLoads(path, beforeKeys);
        }

        private static void AssertTimelineLoads(string path, List<string> expectedKeys)
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            Assert.IsNotNull(timeline);
            var signalTracks = 0;
            var signalKeys = new List<string>();
            foreach (var track in timeline.GetOutputTracks())
            {
                Assert.IsNotNull(track);
                if (track is CutsceneSignalTrack)
                {
                    signalTracks++;
                }

                foreach (var marker in track.GetMarkers())
                {
                    if (marker is CutsceneSignalNotification n)
                    {
                        signalKeys.Add(n.Key);
                    }
                }
            }

            Assert.AreEqual(1, signalTracks);
            Assert.AreEqual(5, signalKeys.Count);
            foreach (var k in signalKeys)
            {
                CollectionAssert.Contains(expectedKeys, k);
            }
        }

        // 旧形式(識別子が SignalNotification)のオブジェクトの Key 値(順不同の比較用)。
        private static List<string> Keys(string yaml)
        {
            var keys = new List<string>();
            foreach (Match m in Regex.Matches(yaml, "m_EditorClassIdentifier: [^\r\n]*\r?\n(?:[^\r\n]*\r?\n)?[^\r\n]*Key: ([^\r\n]*)"))
            {
                keys.Add(m.Groups[1].Value.Trim());
            }

            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        private static int CountOf(string text, string needle)
        {
            var n = 0;
            var i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                n++;
                i += needle.Length;
            }

            return n;
        }

        private sealed class ThrowingProjectMigration : IProjectMigration
        {
            public string Id => "test-throwing-project-migration-do-not-record";

            public void Migrate(MigrationContext context) => throw new InvalidOperationException("boom");
        }

        private sealed class WarningMigration : IProjectMigration
        {
            public string Id => "test-warning-migration-do-not-record";

            public void Migrate(MigrationContext context) => context.Warn("一部を処理できませんでした");
        }
    }
}
