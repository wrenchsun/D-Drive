using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Cutscene;
using DDrive.Editor.Import;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Model;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(取り込み)。E-4(A-4)・E-7(A-7)。
    // 外部 ScriptedImporter / AssetPostprocessor 自体は Unity 標準の仕組みで D-Drive 側の制約が無いため、契約テストにはしない(docs/51 §4.11)。
    public class ExternalContractImportTests
    {
        private const string TempRoot = "Assets/Tests/DDriveTemp/ExternalContractImport";
        private const string SourceRoot = TempRoot + "/SourceAssets";
        private const string GameDataRoot = TempRoot + "/GameData";

        private const string SampleCameraPropsFbx = "Assets/SourceAssets/Data/UnityChan/Models/BoxUnityChan.fbx";
        private const string SampleCharacterFbx = "Assets/SourceAssets/Data/UnityChan/Animations/unitychan_WAIT00.fbx";

        [SetUp]
        public void SetUp()
        {
            CutsceneFbxPostprocessor.Suppress = true;
            ImportRulePostprocessor.Suppress = true;
            ImportRuleService.ResetImportHintStateForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
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

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
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
        }

        private static string CopyAsset(string sourcePath, string destinationPath)
        {
            EnsureFolder(Path.GetDirectoryName(destinationPath)?.Replace('\\', '/'));
            Assert.IsTrue(AssetDatabase.CopyAsset(sourcePath, destinationPath), $"{sourcePath} をコピーできない");
            return destinationPath;
        }

        // E-7: 外部パッケージ用の置き場所(SourceAssets/Cutscene/ 配下の未知の拡張子)は案内ログも出さず、例外も出さない。
        [Test]
        public void E7_UnknownExtensionUnderCutsceneFolder_NoExceptionNoLog()
        {
            var paths = new[] { SourceRoot + "/Cutscene/Opening/Opening01.fctrack", SourceRoot + "/Cutscene/Opening/Opening01.fcpose.json" };
            ImportRuleService.Report report = null;
            Assert.DoesNotThrow(() => report = ImportRuleService.ProcessPaths(paths, SourceRoot, GameDataRoot));
            Assert.IsNotNull(report);
            LogAssert.NoUnexpectedReceived();
        }

        // E-7: 未知のフォルダ(例: SourceAssets/Facial/)に置かれても例外は出さない(警告ログが出るかは FC-6 で変わるため固定しない)。
        [Test]
        public void E7_UnknownTypeFolder_NoException()
        {
            var paths = new[] { SourceRoot + "/Facial/Hero/Hero.fcpose.json", SourceRoot + "/Facial/Hero/Hero.fctrack" };
            LogAssert.ignoreFailingMessages = true;
            ImportRuleService.Report report = null;
            Assert.DoesNotThrow(() => report = ImportRuleService.ProcessPaths(paths, SourceRoot, GameDataRoot));
            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.Created, "未知のフォルダのファイルから Data は作られない");
        }

        // E-7: D-Drive の Cutscene FBX 取り込みは .fbx 以外を無視する(外部の拡張子を持つファイルを渡しても何も作らない)。
        [Test]
        public void E7_CutsceneImport_IgnoresNonFbxExtensions()
        {
            var paths = new[] { SourceRoot + "/Cutscene/Opening/Opening01.fctrack", SourceRoot + "/Cutscene/Opening/Opening01__Hero.fcpose.json" };
            Assert.DoesNotThrow(() => CutsceneImportService.ProcessPaths(paths, SourceRoot, GameDataRoot));
            Assert.IsFalse(AssetDatabase.IsValidFolder(GameDataRoot + "/Cutscene"), "対象外の拡張子で CutsceneData は作られない");
        }

        // ── E-17: 取り込みがボーン・名前・スケールを変えない(FC-20 の確認項目 1〜5。合成 FBX フィクスチャ = docs/51 U-15 (b)) ──

        // 合成の小さな ASCII FBX(スキンメッシュ 1 つ + ボーン Hips / Spine / head + ブレンドシェイプ 3 つ)。
        // パッケージ内なので Assets/SourceAssets の自動取り込み(MayaModelPostprocessor 等)の対象にならない。
        public const string RigFixture = "Packages/com.ddrive.core/Tests/Editor/ExternalContract/Fixtures/ExternalContractRig.fbx";
        private static readonly string[] BoneNames = { "Hips", "Spine", "head" };
        private static readonly string[] ShapeNames = { "FC_test_Neutral_R0_C0", "fcs_test_R0_C0", "smile" };

        [Test]
        public void E17_ImportRuleService_KeepsBonesShapeNamesScaleAndImporterSettings()
        {
            var fixture = AssetDatabase.LoadAssetAtPath<GameObject>(RigFixture);
            Assert.IsNotNull(fixture, "合成 FBX フィクスチャを読めない: " + RigFixture);
            var fixtureSmr = fixture.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(fixtureSmr);
            CollectionAssert.AreEqual(BoneNames, fixtureSmr.bones.Select(b => b.name).ToArray(), "フィクスチャ自体のボーン名");
            CollectionAssert.AreEqual(ShapeNames, Enumerable.Range(0, fixtureSmr.sharedMesh.blendShapeCount).Select(i => fixtureSmr.sharedMesh.GetBlendShapeName(i)).ToArray(), "フィクスチャ自体のシェイプ名");

            var path = CopyAsset(RigFixture, $"{SourceRoot}/Model/ExternalContract/Rig.fbx");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created, report.ToString());

            var data = AssetDatabase.LoadAssetAtPath<ModelData>($"{GameDataRoot}/Model/ExternalContract/MODEL_ExternalContract_Rig.asset");
            Assert.IsNotNull(data, "ModelData が作られる");
            Assert.IsNotNull(data.Prefab);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(path), data.Prefab, "ModelData.Prefab は FBX のルートそのまま");

            var smr = data.Prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(smr);
            CollectionAssert.AreEqual(BoneNames, smr.bones.Select(b => b.name).ToArray(), "ボーンの GameObject が残り、名前が元ファイルと一致する");
            CollectionAssert.AreEqual(ShapeNames, Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Select(i => smr.sharedMesh.GetBlendShapeName(i)).ToArray(), "シェイプ名がそのまま(FC_ / fcs_ 含む)");

            // スケール: 元ファイルと同じ(取り込み経由で変わらない)。
            Assert.AreEqual(fixture.transform.localScale, data.Prefab.transform.localScale);
            Assert.AreEqual(fixtureSmr.sharedMesh.bounds.size.x, smr.sharedMesh.bounds.size.x, 1e-5f);
            Assert.AreEqual(fixtureSmr.sharedMesh.bounds.size.y, smr.sharedMesh.bounds.size.y, 1e-5f);
            for (var i = 0; i < smr.bones.Length; i++)
            {
                Assert.AreEqual(fixtureSmr.bones[i].localScale, smr.bones[i].localScale, BoneNames[i]);
                Assert.AreEqual(fixtureSmr.bones[i].localPosition, smr.bones[i].localPosition, BoneNames[i]);
            }

            // ModelImporter の設定は D-Drive が触らない(元ファイルの既定のまま = フィクスチャ本体と同じ)。
            var baseline = (ModelImporter)AssetImporter.GetAtPath(RigFixture);
            var copied = (ModelImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(baseline.importBlendShapes, copied.importBlendShapes);
            Assert.AreEqual(baseline.optimizeGameObjects, copied.optimizeGameObjects);
            Assert.AreEqual(baseline.meshCompression, copied.meshCompression);
            Assert.AreEqual(baseline.globalScale, copied.globalScale);
            Assert.AreEqual(baseline.useFileScale, copied.useFileScale);
            Assert.AreEqual(baseline.animationType, copied.animationType);
            Assert.AreEqual(baseline.extraExposedTransformPaths.Length, copied.extraExposedTransformPaths.Length);
            Assert.IsTrue(copied.importBlendShapes, "既定のまま(ブレンドシェイプを取り込む)");
            Assert.IsFalse(copied.optimizeGameObjects, "既定のまま(ボーン階層を潰さない)");
        }

        // E-4(A-4): 再取り込みで、外部型の TrackAsset と外部が足した Binding が消えない(DevRepoOnly: UnityChan の FBX が要る)。
        [Test]
        [Category("DevRepoOnly")]
        public void E4_Reimport_PreservesExternalTrackAndExternalBinding()
        {
#if !DDRIVE_DEV_REPO
            Assume.That(false, "開発リポジトリの実データ(Assets/SourceAssets 等)が必要なためスキップ(DDRIVE_DEV_REPO 未定義)");
#endif
            var cameraPath = CopyAsset(SampleCameraPropsFbx, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx");
            var charPath = CopyAsset(SampleCharacterFbx, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero.fbx");
            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);

            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            Assert.IsNotNull(data);

            const string ExternalName = "Hero_Dummy";
            data.Timeline.CreateTrack<ExternalProbeTrack>(null, ExternalName);
            var bindings = data.Bindings.ToList();
            bindings.Add(new CutsceneBinding { TrackName = ExternalName, Target = CutsceneBindTarget.SceneObjectByName, SceneObjectName = "ExternalHero" });
            data.Bindings = bindings.ToArray();
            EditorUtility.SetDirty(data.Timeline);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);

            var reloaded = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            var external = reloaded.Timeline.GetOutputTracks().OfType<ExternalProbeTrack>().Where(t => t.name == ExternalName).ToList();
            Assert.AreEqual(1, external.Count, "再取り込みで外部型のトラックが消えず、重複もしない");
            Assert.IsTrue(
                reloaded.Bindings.Any(b => b.TrackName == ExternalName && b.SceneObjectName == "ExternalHero"),
                "再取り込みで外部が足した Binding が消えない");
        }
    }
}
