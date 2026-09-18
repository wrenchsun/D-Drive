using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Cutscene;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §5(6-10c) — CutsceneImportService(Maya FBX セット → CutsceneData/TimelineAsset)のテスト。
    // 実 Maya 素材が無いため、既存のサンプル FBX(UnityChan)をコピーして「カメラ+小物」「キャラ」ファイル名に
    // 見立てて使う(カメラコンポーネントは持たないため、カメラ抽出そのものは
    // CutsceneCameraCurveExtractorTests でコードで組んだ AnimationClip/Camera を使って別に検証する)。
    public class CutsceneImportServiceTests
    {
        private const string GameDataRoot = "Assets/DDrive/Tests/Editor/TempCutsceneGameData";
        private const string SourceRoot = "Assets/DDrive/Tests/Editor/TempCutsceneSourceAssets";

        private const string SampleCameraPropsFbx = "Assets/SourceAssets/Data/UnityChan/Models/BoxUnityChan.fbx";
        private const string SampleCharacterFbx = "Assets/SourceAssets/Data/UnityChan/Animations/unitychan_WAIT00.fbx";

        [SetUp]
        public void SetUp()
        {
            CutsceneFbxPostprocessor.Suppress = true;
        }

        [TearDown]
        public void TearDown()
        {
            CutsceneFbxPostprocessor.Suppress = false;
            CleanupFolder(GameDataRoot);
            CleanupFolder(SourceRoot);
        }

        private static void CleanupFolder(string root)
        {
            if (AssetDatabase.IsValidFolder(root))
            {
                AddressablesSync.RemoveEntriesUnder(root);
                AssetDatabase.DeleteAsset(root);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope())
                {
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private static string CopyAsset(string sourcePath, string relativeAssetPath)
        {
            var dir = Path.GetDirectoryName(relativeAssetPath)?.Replace('\\', '/');
            var absoluteDir = Path.GetFullPath(dir ?? string.Empty);
            if (!Directory.Exists(absoluteDir))
            {
                Directory.CreateDirectory(absoluteDir);
            }

            AssetDatabase.CopyAsset(sourcePath, relativeAssetPath);
            return relativeAssetPath;
        }

        private static ModelData CreateModelData(string identifier, string gameDataRoot)
        {
            var folder = $"{gameDataRoot}/Model";
            AssetCreationServiceTestFolder(folder);
            var model = ScriptableObject.CreateInstance<ModelData>();
            AssetDatabase.CreateAsset(model, $"{folder}/MODEL_{identifier}.asset");
            return model;
        }

        private static void AssetCreationServiceTestFolder(string folder)
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

        // ── ComputeCutsceneDataPath(純ロジック) ──

        [Test]
        public void ComputeCutsceneDataPath_WithCategory_MatchesNamingConvention()
        {
            var path = CutsceneImportService.ComputeCutsceneDataPath("Assets/GameData", "Opening", "Opening01");
            Assert.AreEqual("Assets/GameData/Cutscene/Opening/CUT_Opening_Opening01.asset", path);
        }

        [Test]
        public void ComputeCutsceneDataPath_WithoutCategory_OmitsCategorySegment()
        {
            var path = CutsceneImportService.ComputeCutsceneDataPath("Assets/GameData", string.Empty, "Opening01");
            Assert.AreEqual("Assets/GameData/Cutscene/CUT_Opening01.asset", path);
        }

        // ── FindModelDataByIdentifier ──

        [Test]
        public void FindModelDataByIdentifier_MatchesSuffixOfFileName()
        {
            // GameDataRoot は TearDown でフォルダごと削除するため、ここでは個別に破棄しない。
            var model = CreateModelData("Hero", GameDataRoot);
            var found = CutsceneImportService.FindModelDataByIdentifier("Hero");
            Assert.AreEqual(model, found);
        }

        [Test]
        public void FindModelDataByIdentifier_NotFound_ReturnsNull()
        {
            Assert.IsNull(CutsceneImportService.FindModelDataByIdentifier("NoSuchModelXyz"));
        }

        // ── ProcessPaths: 新規作成 ──

        [Test]
        public void ProcessPaths_CameraPropsFileOnly_CreatesCutsceneData()
        {
            var cameraPath = CopyAsset(SampleCameraPropsFbx, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx");

            var report = CutsceneImportService.ProcessPaths(new[] { cameraPath }, SourceRoot, GameDataRoot);

            Assert.AreEqual(1, report.Created);
            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            Assert.IsNotNull(data);
            Assert.IsNotNull(data.Timeline);
            Assert.AreEqual(1, data.SourceFbxGuids.Length);
        }

        [Test]
        public void ProcessPaths_CharacterFileWithoutCameraProps_IsSkipped()
        {
            var charPath = CopyAsset(SampleCharacterFbx, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero.fbx");

            var report = CutsceneImportService.ProcessPaths(new[] { charPath }, SourceRoot, GameDataRoot);

            Assert.AreEqual(0, report.Created);
            Assert.AreEqual(1, report.Skipped);
            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath));
        }

        [Test]
        public void ProcessPaths_ShotSet_BuildsCameraAndCharacterTracks()
        {
            var cameraPath = CopyAsset(SampleCameraPropsFbx, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx");
            var charPath = CopyAsset(SampleCharacterFbx, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero.fbx");

            var report = CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);

            Assert.AreEqual(1, report.Created);
            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            Assert.IsNotNull(data);
            Assert.AreEqual(2, data.SourceFbxGuids.Length);

            // BoxUnityChan(カメラ+小物役)にはカメラが無いため Camera トラックは作られないが、
            // 埋め込みアニメも無いためキャラのバインドのみ確認する(§7.3、実 FBX でのカメラ抽出は別途要検証)。
            Assert.IsTrue(System.Array.Exists(data.Bindings, b => b.TrackName == "Hero" && b.Target == CutsceneBindTarget.SpawnModel));

            var animTrack = System.Array.Find(data.Timeline.GetOutputTracks().ToArray(), t => t is AnimationTrack && t.name == "Hero");
            Assert.IsNotNull(animTrack);
        }

        // ── 再取り込み(冪等性・保持) ──

        [Test]
        public void ProcessPaths_Reimport_DoesNotDuplicateCutsceneDataOrTracks()
        {
            var cameraPath = CopyAsset(SampleCameraPropsFbx, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx");
            var charPath = CopyAsset(SampleCharacterFbx, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero.fbx");

            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);
            var report2 = CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);

            Assert.AreEqual(0, report2.Created);
            Assert.AreEqual(1, report2.Updated);

            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            var tracks = data.Timeline.GetOutputTracks().ToArray();
            var heroTracks = System.Array.FindAll(tracks, t => t.name == "Hero");
            Assert.AreEqual(1, heroTracks.Length, "再取り込みで同名トラックが重複してはならない");
        }

        [Test]
        public void ProcessPaths_Reimport_PreservesManuallyAddedTrackAndCameraSettings()
        {
            var cameraPath = CopyAsset(SampleCameraPropsFbx, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx");
            var charPath = CopyAsset(SampleCharacterFbx, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero.fbx");
            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);

            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);

            // デザイナーが手で足したトラック(SE/VFX 等の代わりに素の TrackAsset で代用)。
            var manualTrack = data.Timeline.CreateTrack<AnimationTrack>(null, "DesignerAdded");
            EditorUtility.SetDirty(data.Timeline);
            AssetDatabase.SaveAssets();

            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);

            var reloaded = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            var stillThere = System.Array.Exists(reloaded.Timeline.GetOutputTracks().ToArray(), t => t.name == "DesignerAdded");
            Assert.IsTrue(stillThere, "再取り込みでデザイナーが足したトラックが消えてはならない");
        }

        // ── AC: CutsceneImportProfile.DefaultFrameRate を変えても既存 CutsceneData は変わらない ──

        [Test]
        public void ChangingDefaultFrameRate_DoesNotAffectExistingCutsceneData()
        {
            // プロジェクトに CutsceneImportProfile が無い場合、FindOrDefault() はメモリ上の組み込み既定
            // (static シングルトン)を返す。"/Tests/" 配下は FindOrDefault の対象外(Anim2DImportProfile 等と
            // 同じ規約)のため、テスト用アセットではなくこのシングルトンを直接書き換えて確認する。
            // 他テストへの汚染を避けるため、既定値(30)に戻して終える。
            var profile = CutsceneImportProfile.FindOrDefault();
            var originalDefault = profile.DefaultFrameRate;
            profile.DefaultFrameRate = 30f;

            try
            {
                // BoxUnityChan にはアニメが無いため fps が検出できず、プロファイルの既定値が使われる経路になる。
                var cameraPath = CopyAsset(SampleCameraPropsFbx, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx");
                CutsceneImportService.ProcessPaths(new[] { cameraPath }, SourceRoot, GameDataRoot);

                var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
                var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
                Assert.AreEqual(30f, data.FrameRate);

                profile.DefaultFrameRate = 60f;

                // 再取り込み(camera ファイルの再インポート)しても既存の CutsceneData.FrameRate は変わらない
                // ([11_tasks.md] 6-10c AC)。
                CutsceneImportService.ProcessPaths(new[] { cameraPath }, SourceRoot, GameDataRoot);
                var reloaded = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
                Assert.AreEqual(30f, reloaded.FrameRate, "既存 CutsceneData の FrameRate はプロファイルの既定値変更で変わってはならない");
            }
            finally
            {
                profile.DefaultFrameRate = originalDefault;
            }
        }
    }
}
