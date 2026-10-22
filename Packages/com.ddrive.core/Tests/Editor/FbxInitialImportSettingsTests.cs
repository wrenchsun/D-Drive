using System.IO;
using DDrive.Editor.Cutscene;
using DDrive.Editor.Import;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // 1017 TA-2(DD-3) — Cutscene / Anim フォルダの FBX は初回取り込みのときだけ preserveHierarchy = true にし、
    // 2 回目以降(デザイナーが Inspector で変えた値)は上書きしない。実 FBX が要るため開発リポジトリの
    // UnityChan サンプルをコピーして使う(DevRepoOnlyGuard 参照)。
    public class FbxInitialImportSettingsTests
    {
        private const string Folder = TestTempFolder.Root + "/TempFbxInitialSettings";
        private const string SampleFbx = "Assets/SourceAssets/Data/UnityChan/Models/BoxUnityChan.fbx";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        private static ModelImporter CopyAndGetImporter()
        {
            Directory.CreateDirectory(Path.GetFullPath(Folder));
            AssetDatabase.Refresh();
            var path = Folder + "/Sample.fbx";
            Assert.IsTrue(AssetDatabase.CopyAsset(SampleFbx, path));
            return (ModelImporter)AssetImporter.GetAtPath(path);
        }

        [Test]
        [Category("DevRepoOnly")]
        public void CutsceneFbx_FirstImportTurnsPreserveHierarchyOn_ButLaterImportKeepsDesignerValue()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
            var importer = CopyAndGetImporter();
            importer.preserveHierarchy = false;

            CutsceneFbxPostprocessor.ApplyInitialImportSettings(importer, true);
            Assert.IsTrue(importer.preserveHierarchy, "初回取り込みでは ON");

            importer.preserveHierarchy = false; // デザイナーが Inspector で戻した
            CutsceneFbxPostprocessor.ApplyInitialImportSettings(importer, false);
            Assert.IsFalse(importer.preserveHierarchy, "2 回目以降は上書きしない");
        }

        [Test]
        [Category("DevRepoOnly")]
        public void AnimFbx_FirstImportTurnsPreserveHierarchyOn_ButLaterImportKeepsDesignerValue()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
            var importer = CopyAndGetImporter();
            importer.preserveHierarchy = false;

            AnimFbxPreprocessor.ApplyInitialImportSettings(importer, true);
            Assert.IsTrue(importer.preserveHierarchy);

            importer.preserveHierarchy = false;
            AnimFbxPreprocessor.ApplyInitialImportSettings(importer, false);
            Assert.IsFalse(importer.preserveHierarchy);
        }

        [TestCase("Assets/SourceAssets/Anim/Test/A_Hero_Idle.fbx", true)]
        [TestCase("Assets/SourceAssets/Anim/Test/A_Hero_Idle.anim", false)]
        [TestCase("Assets/SourceAssets/Model/Test/SK_Hero.fbx", false)]
        [TestCase("Assets/SourceAssets/Cutscene/Test/Shot.fbx", false)]
        public void AnimFbx_IsUnderAnimFolder_OnlyForFbxUnderAnim(string path, bool expected)
        {
            Assert.AreEqual(expected, AnimFbxPreprocessor.IsUnderAnimFolder(path));
        }
    }
}
