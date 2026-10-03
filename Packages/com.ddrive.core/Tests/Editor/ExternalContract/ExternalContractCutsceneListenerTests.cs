using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Cutscene;
using DDrive.Editor.Import;
using DDrive.Runtime.Cutscene;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約: カットシーン取り込み完了の公開イベント(FC-5、E-19)。
    // 外部アセンブリ(ExternalContract.Tests.Editor)の public な ICutsceneImportListener 実装が TypeCache で発見され、
    // Order 順に呼ばれ、例外が隔離され、リスナーが足した Binding / トラックが保存されて再取り込みで消えないことを固定する。
    // 入力は合成 FBX(ExternalContractImportTests.RigFixture)を「カメラ+小物」「キャラ」に見立てる(アニメーション無し =
    // トラックは作られないが CutsceneData / Timeline は作られリスナーは呼ばれる)。役の中身は UnityChan を使う DevRepoOnly で確認。
    public class ExternalContractCutsceneListenerTests
    {
        private const string TempRoot = "Assets/Tests/DDriveTemp/ExternalContractListener";
        private const string SourceRoot = TempRoot + "/SourceAssets";
        private const string GameDataRoot = TempRoot + "/GameData";

        private const string SampleCameraPropsFbx = "Assets/SourceAssets/Data/UnityChan/Models/BoxUnityChan.fbx";
        private const string SampleCharacterFbx = "Assets/SourceAssets/Data/UnityChan/Animations/unitychan_WAIT00.fbx";

        private const string ExternalTrackName = "Hero_Facial(auto)";
        private const string SourceRoleName = "Hero";

        private static readonly string[] ExpectedOrder =
        {
            nameof(ExternalThrowingListener), nameof(ExternalListenerA), nameof(ExternalListenerB), nameof(ExternalListenerLast),
        };

        [SetUp]
        public void SetUp()
        {
            CutsceneFbxPostprocessor.Suppress = true;
            ImportRulePostprocessor.Suppress = true;
            ExternalListenerProbe.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            ExternalListenerProbe.Reset();
            ImportRulePostprocessor.Suppress = false;
            CutsceneFbxPostprocessor.Suppress = false;
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TempRoot);
                AssetDatabase.DeleteAsset(TempRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope())
                {
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private static string CopyAsset(string sourcePath, string destinationPath)
        {
            var dir = Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
            var parts = dir.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }

            Assert.IsTrue(AssetDatabase.CopyAsset(sourcePath, destinationPath), $"{sourcePath} をコピーできない");
            return destinationPath;
        }

        private static string[] SyntheticShotPaths()
        {
            return new[]
            {
                CopyAsset(ExternalContractImportTests.RigFixture, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx"),
                CopyAsset(ExternalContractImportTests.RigFixture, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero.fbx"),
            };
        }

        // 外部が書くリスナーの典型: 既にあるかを Result で確認してから Binding とトラックを足す(再取り込みで重複させない)。
        private static void AddExternalBindingAndTrackIfMissing(CutsceneImportResult result)
        {
            var bindings = (result.Data.Bindings ?? Array.Empty<CutsceneBinding>()).ToList();
            if (!bindings.Any(b => b.TrackName == ExternalTrackName))
            {
                bindings.Add(new CutsceneBinding { TrackName = ExternalTrackName, Target = CutsceneBindTarget.SameAsTrack, SourceTrackName = SourceRoleName });
                result.Data.Bindings = bindings.ToArray();
            }

            if (!result.Timeline.GetOutputTracks().Any(t => t.name == ExternalTrackName))
            {
                result.Timeline.CreateTrack<ExternalProbeTrack>(null, ExternalTrackName);
            }
        }

        // ── 発見・順序・隔離 ──

        [Test]
        public void E19_ExternalAssemblyListeners_AreDiscovered_InOrderThenTypeName()
        {
            var names = CutsceneImportListeners.Discover().Select(l => l.GetType().Name).ToList();

            foreach (var expected in ExpectedOrder)
            {
                Assert.Contains(expected, names, "外部アセンブリの public 型が発見される: " + expected);
            }

            Assert.AreEqual("ExternalContract.Tests.Editor", typeof(ExternalListenerA).Assembly.GetName().Name, "テストアセンブリ除外(DDrive.Tests*)に当たらない外部アセンブリ");
            var own = names.Where(n => n.StartsWith("External", StringComparison.Ordinal)).ToList();
            CollectionAssert.AreEqual(ExpectedOrder, own, "Order 昇順(-10 → 0 → 0 → 50)、同値は型のフルネーム順(A → B)");
        }

        [Test]
        public void E19_Listeners_AreInactiveByDefault_AndNotifyDoesNotThrow()
        {
            var result = new CutsceneImportResult { ShotName = "X", Category = string.Empty, Roles = new List<CutsceneImportRole>() };
            Assert.DoesNotThrow(() => CutsceneImportListeners.Notify(result));
            Assert.AreEqual(0, CutsceneImportListeners.Notify(null), "null の結果では誰も呼ばない");
        }

        [Test]
        public void E19_ThrowingListener_DoesNotStopOthers()
        {
            var calls = new List<string>();
            ExternalListenerProbe.Sink = (name, _) => calls.Add(name);
            ExternalListenerProbe.ThrowEnabled = true;
            LogAssert.Expect(LogType.Exception, new Regex("external listener failure"));

            var result = new CutsceneImportResult { ShotName = "X", Category = string.Empty, Roles = new List<CutsceneImportRole>() };
            CutsceneImportListeners.Notify(result);

            var mine = calls.Where(c => c.StartsWith("External", StringComparison.Ordinal)).ToList();
            CollectionAssert.AreEqual(ExpectedOrder, mine, "例外を投げたリスナーの後ろも呼ばれる");
        }

        // ── 取り込み経路(合成 FBX) ──

        [Test]
        public void E19_ProcessPaths_CallsListenersOncePerShot_WithShotInfo_AfterBindingsAreSettled()
        {
            var calls = new List<(string name, CutsceneImportResult result, bool dataIsAsset, bool hasTimelinePath)>();
            ExternalListenerProbe.Sink = (name, r) => calls.Add((name, r, AssetDatabase.Contains(r.Data), !string.IsNullOrEmpty(r.TimelinePath)));

            var report = CutsceneImportService.ProcessPaths(SyntheticShotPaths(), SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created, report.ToString());

            var a = calls.Where(c => c.name == nameof(ExternalListenerA)).ToList();
            Assert.AreEqual(1, a.Count, "ショット 1 つにつき 1 回(2 本の FBX = 1 ショット)");
            var result = a[0].result;
            Assert.AreEqual("Opening01", result.ShotName);
            Assert.AreEqual("Opening", result.Category);
            Assert.IsTrue(result.IsNew);
            Assert.IsNotNull(result.Data);
            Assert.IsNotNull(result.Timeline);
            Assert.AreSame(result.Data.Timeline, result.Timeline);
            Assert.IsTrue(a[0].dataIsAsset, "呼び出し時点で CutsceneData は既にアセットとして存在する");
            Assert.IsTrue(a[0].hasTimelinePath);
            Assert.AreEqual(2, result.Data.SourceFbxGuids.Length, "SourceFbxGuids は確定済み");
            Assert.IsNotNull(result.Roles);
        }

        [Test]
        public void E19_ListenerAdditions_AreSavedByDDrive_AndSurviveReimportWithoutDuplicates()
        {
            var isNewHistory = new List<bool>();
            ExternalListenerProbe.Sink = (name, r) =>
            {
                if (name != nameof(ExternalListenerA))
                {
                    return;
                }

                isNewHistory.Add(r.IsNew);
                AddExternalBindingAndTrackIfMissing(r); // 保存はリスナーでは呼ばない(D-Drive が呼び出し後に 1 回保存する)
            };

            var paths = SyntheticShotPaths();
            CutsceneImportService.ProcessPaths(paths, SourceRoot, GameDataRoot);

            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            Assert.IsNotNull(data);
            var timelinePath = AssetDatabase.GetAssetPath(data.Timeline);

            // ディスク上のファイルに書かれている = D-Drive がリスナー呼び出しの後に保存した。
            var dataText = File.ReadAllText(dataPath);
            Assert.IsTrue(dataText.Contains("SourceTrackName: " + SourceRoleName) && dataText.Contains(ExternalTrackName), "リスナーが足した Binding が CutsceneData に保存されている");
            Assert.IsTrue(File.ReadAllText(timelinePath).Contains(ExternalTrackName), "リスナーが足したトラックが .playable に保存されている");

            // 再取り込み(同じ経路)。IsNew = false で再び呼ばれ、足したものは消えず・重複しない。
            CutsceneImportService.ProcessPaths(paths, SourceRoot, GameDataRoot);
            CollectionAssert.AreEqual(new[] { true, false }, isNewHistory);

            var reloaded = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            Assert.AreEqual(1, reloaded.Bindings.Count(b => b.TrackName == ExternalTrackName), "Binding が消えず重複もしない");
            Assert.AreEqual(1, reloaded.Timeline.GetOutputTracks().OfType<ExternalProbeTrack>().Count(t => t.name == ExternalTrackName), "外部トラックが消えず重複もしない");
            Assert.AreEqual(CutsceneBindTarget.SameAsTrack, reloaded.Bindings.First(b => b.TrackName == ExternalTrackName).Target);
        }

        [Test]
        public void E19_ScanAll_ManualReimport_AlsoCallsListeners()
        {
            SyntheticShotPaths();
            var calls = 0;
            ExternalListenerProbe.Sink = (name, r) =>
            {
                if (name == nameof(ExternalListenerA))
                {
                    calls++;
                }
            };

            var report = CutsceneImportService.ScanAll(SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created, report.ToString());
            Assert.AreEqual(1, calls, "ScanAll(手動の再取り込み)も同じ経路でリスナーが呼ばれる");
            CutsceneImportService.ScanAll(SourceRoot, GameDataRoot);
            Assert.AreEqual(2, calls);
        }

        [Test]
        public void E19_ThrowingListener_DoesNotStopImport_AndLaterListenersStillSave()
        {
            ExternalListenerProbe.ThrowEnabled = true;
            ExternalListenerProbe.Sink = (name, r) =>
            {
                if (name == nameof(ExternalListenerA))
                {
                    AddExternalBindingAndTrackIfMissing(r);
                }
            };
            LogAssert.Expect(LogType.Exception, new Regex("external listener failure"));

            var report = CutsceneImportService.ProcessPaths(SyntheticShotPaths(), SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created, "リスナーの例外で取り込みは止まらない");

            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            Assert.IsTrue(data.Bindings.Any(b => b.TrackName == ExternalTrackName));
        }

        // ── 役の中身(UnityChan の FBX が要る) ──

        [Test]
        [Category("DevRepoOnly")]
        public void E19_Roles_DescribeCharacterTrack_AndReimportKeepsListenerAdditions()
        {
#if !DDRIVE_DEV_REPO
            Assume.That(false, "開発リポジトリの実データ(Assets/SourceAssets 等)が必要なためスキップ(DDRIVE_DEV_REPO 未定義)");
#endif
            var cameraPath = CopyAsset(SampleCameraPropsFbx, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx");
            var charPath = CopyAsset(SampleCharacterFbx, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero_2.fbx");
            CutsceneImportResult last = null;
            ExternalListenerProbe.Sink = (name, r) =>
            {
                if (name == nameof(ExternalListenerA))
                {
                    last = r;
                    AddExternalBindingAndTrackIfMissing(r);
                }
            };

            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);
            Assert.IsNotNull(last);
            var hero = last.Roles.FirstOrDefault(role => role.Kind == CutsceneImportRoleKind.Character);
            Assert.IsNotNull(hero, "キャラの役が Roles に入る");
            Assert.AreEqual("Hero_2", hero.RoleName, "RoleName = TrackName(__ 以降。重複接尾辞を除く前)");
            Assert.AreEqual("Hero", hero.ModelIdentifier, "ModelIdentifier は重複接尾辞を除いたもの");
            Assert.IsInstanceOf<AnimationTrack>(hero.Track);
            Assert.AreEqual(hero.RoleName, hero.Track.name);
            Assert.AreEqual(charPath, hero.SourcePath);

            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);
            Assert.IsFalse(last.IsNew);
            Assert.AreEqual(1, last.Data.Bindings.Count(b => b.TrackName == ExternalTrackName));
            Assert.AreEqual(1, last.Timeline.GetOutputTracks().OfType<ExternalProbeTrack>().Count());
        }
    }
}
