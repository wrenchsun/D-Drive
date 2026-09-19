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
        private const string GameDataRoot = "Packages/com.ddrive.core/Tests/Editor/TempCutsceneGameData";
        private const string SourceRoot = "Packages/com.ddrive.core/Tests/Editor/TempCutsceneSourceAssets";

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

        // ── ResolveInitialFocusMode(純ロジック、docs/45 P1-2 2026-09-20) ──

        [Test]
        public void ResolveInitialFocusMode_NoFocusCurves_ReturnsOff()
        {
            var asset = ScriptableObject.CreateInstance<CutsceneCameraClip>();
            try
            {
                // Extract() 直後の既定値と同じ(見つからなかったチャンネルは空カーブ、[26_timeline.md] §4.6.4)。
                asset.FocalLengthMm = new AnimationCurve();
                asset.FocusDistance = new AnimationCurve();
                asset.Aperture = new AnimationCurve();

                Assert.AreEqual(CameraFocusMode.Off, CutsceneImportService.ResolveInitialFocusMode(asset),
                    "焦点距離/ピント距離/絞りのいずれも取れなければ Focus=Off(「取れなければ書かない」)");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ResolveInitialFocusMode_FocusDistanceCurveFound_ReturnsVolume()
        {
            var asset = ScriptableObject.CreateInstance<CutsceneCameraClip>();
            try
            {
                asset.FocalLengthMm = new AnimationCurve();
                asset.FocusDistance = AnimationCurve.Constant(0f, 1f, 3f);
                asset.Aperture = new AnimationCurve();

                Assert.AreEqual(CameraFocusMode.Volume, CutsceneImportService.ResolveInitialFocusMode(asset),
                    "ピント距離カーブが 1 つでも取れれば Volume(既定)");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
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

        // [42_distribution.md] §2.3-6(P-4、2026-09-20) — 実 Maya 素材の代わりに開発リポジトリの実データ
        // (UnityChan サンプル FBX)をコピーして使うため、開発リポジトリ専用。DevRepoOnlyGuard 参照。
        [Test]
        [Category("DevRepoOnly")]
        public void ProcessPaths_CameraPropsFileOnly_CreatesCutsceneData()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
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
        [Category("DevRepoOnly")]
        public void ProcessPaths_CharacterFileWithoutCameraProps_IsSkipped()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
            var charPath = CopyAsset(SampleCharacterFbx, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero.fbx");

            var report = CutsceneImportService.ProcessPaths(new[] { charPath }, SourceRoot, GameDataRoot);

            Assert.AreEqual(0, report.Created);
            Assert.AreEqual(1, report.Skipped);
            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath));
        }

        [Test]
        [Category("DevRepoOnly")]
        public void ProcessPaths_ShotSet_BuildsCameraAndCharacterTracks()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
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
        [Category("DevRepoOnly")]
        public void ProcessPaths_Reimport_DoesNotDuplicateCutsceneDataOrTracks()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
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
        [Category("DevRepoOnly")]
        public void ProcessPaths_Reimport_PreservesManuallyAddedTrackAndCameraSettings()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
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

        // ── SourceFrameRange の永続化(docs/45 P1-4、2026-09-20) ──

        [Test]
        [Category("DevRepoOnly")]
        public void ProcessPaths_SourceFrameRange_PersistsTrimmedClipAsSubAsset_AndDoesNotDuplicateOnReimport()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
            var cameraPath = CopyAsset(SampleCameraPropsFbx, $"{SourceRoot}/Cutscene/Opening/Opening01.fbx");
            var charPath = CopyAsset(SampleCharacterFbx, $"{SourceRoot}/Cutscene/Opening/Opening01__Hero.fbx");
            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);

            var dataPath = CutsceneImportService.ComputeCutsceneDataPath(GameDataRoot, "Opening", "Opening01");
            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);

            // [26_timeline.md] §5.1「逃げ道」を有効にする(既定 0/0 だとトリムが走らないため)。
            data.SourceFrameRange = new FrameRange { Start = 0, End = 2 };
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);

            var timelinePath = AssetDatabase.GetAssetPath(data.Timeline);
            var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(timelinePath);
            var clipCount = subAssets.Count(o => o is AnimationClip);
            Assert.GreaterOrEqual(clipCount, 1,
                "SourceFrameRange で切り出したクリップが TimelineAsset(.playable)のサブアセットとして" +
                "永続化されていること(非永続のままだとドメインリロードで参照が消える、docs/45 P1-4)");

            // 再取り込みでは同名の既存サブアセットを差し替えて再利用し、孤児を残さない。
            CutsceneImportService.ProcessPaths(new[] { cameraPath, charPath }, SourceRoot, GameDataRoot);
            var subAssets2 = AssetDatabase.LoadAllAssetRepresentationsAtPath(timelinePath);
            var clipCount2 = subAssets2.Count(o => o is AnimationClip);
            Assert.AreEqual(clipCount, clipCount2, "再取り込みでサブアセットが増えてはならない(孤児を残さない)");
        }

        // ── AC: CutsceneImportProfile.DefaultFrameRate を変えても既存 CutsceneData は変わらない ──

        [Test]
        [Category("DevRepoOnly")]
        public void ChangingDefaultFrameRate_DoesNotAffectExistingCutsceneData()
        {
            DevRepoOnlyGuard.SkipUnlessDevRepo();
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
