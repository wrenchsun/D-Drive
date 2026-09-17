using System.IO;
using System.Text.RegularExpressions;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Import;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-11 — ImportRuleService(SourceAssets/<種別>/<カテゴリ>/ への配置検知)のテスト。
    // MayaMaterialImporterTests と同じ方針: 実ファイル(wav/png/prefab/anim。fbx は既存のサンプル資産をコピーして使う)を
    // テスト専用の一時フォルダに作り、ProcessPaths を直接呼ぶ(AssetPostprocessor 経由の delayCall はタイミング依存のため経由しない)。
    public class ImportRuleServiceTests
    {
        private const string GameDataRoot = "Assets/DDrive/Tests/Editor/TempGameData";
        private const string SourceRoot = "Assets/DDrive/Tests/Editor/TempSourceAssets";

        // 埋め込み AnimationClip を持つ既存サンプルアニメーション(コピーして使う。読み取りのみで元ファイルは変更しない)。
        private const string SampleAnimFbx = "Assets/SourceAssets/Data/UnityChan/Animations/unitychan_WAIT00.fbx";
        private const string SampleModelFbx = "Assets/SourceAssets/Data/UnityChan/Models/BoxUnityChan.fbx";

        [SetUp]
        public void SetUp()
        {
            // このテストは ProcessPaths を直接呼ぶため、実運用の AssetPostprocessor 経由の自動実行は止めておく
            // (delayCall のタイミングでテストと競合し、二重生成やテスト後の残骸を生む事故を避ける)。
            ImportRulePostprocessor.Suppress = true;

            // 案内ログの「パスごとに1回だけ」は static な HashSet で持っているため、テスト間で残ると
            // 後続のテストで警告が出なくなってしまう。テストごとにリセットする。
            ImportRuleService.ResetImportHintStateForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ImportRulePostprocessor.Suppress = false;
            CleanupGameData();
            CleanupSourceAssets();
        }

        private static void CleanupGameData()
        {
            if (AssetDatabase.IsValidFolder(GameDataRoot))
            {
                AddressablesSync.RemoveEntriesUnder(GameDataRoot);
                AssetDatabase.DeleteAsset(GameDataRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        private static void CleanupSourceAssets()
        {
            if (AssetDatabase.IsValidFolder(SourceRoot))
            {
                AssetDatabase.DeleteAsset(SourceRoot);
            }
        }

        // ── テスト用元ファイルの作成ヘルパー ──

        private static string WriteWav(string relativeAssetPath)
        {
            EnsureDiskFolder(Path.GetDirectoryName(relativeAssetPath));
            var absolute = Path.GetFullPath(relativeAssetPath);
            const int sampleCount = 200;
            const int sampleRate = 8000;
            const int channels = 1;
            const int bitsPerSample = 16;
            var dataSize = sampleCount * channels * (bitsPerSample / 8);

            using (var stream = new FileStream(absolute, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * bitsPerSample / 8);
                writer.Write((short)(channels * bitsPerSample / 8));
                writer.Write((short)bitsPerSample);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);
                writer.Write(new byte[dataSize]);
            }

            AssetDatabase.ImportAsset(relativeAssetPath, ImportAssetOptions.ForceSynchronousImport);
            return relativeAssetPath;
        }

        private static string WritePng(string relativeAssetPath)
        {
            EnsureDiskFolder(Path.GetDirectoryName(relativeAssetPath));
            var tex = new Texture2D(4, 4);
            File.WriteAllBytes(Path.GetFullPath(relativeAssetPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(relativeAssetPath, ImportAssetOptions.ForceSynchronousImport);
            return relativeAssetPath;
        }

        private static string WriteAnimClip(string relativeAssetPath)
        {
            EnsureDiskFolder(Path.GetDirectoryName(relativeAssetPath));
            var clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, relativeAssetPath);
            return relativeAssetPath;
        }

        private static string WritePrefab(string relativeAssetPath)
        {
            EnsureDiskFolder(Path.GetDirectoryName(relativeAssetPath));
            var go = new GameObject("TempPrefabSource");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, relativeAssetPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            return relativeAssetPath;
        }

        private static string CopyAsset(string sourcePath, string relativeAssetPath)
        {
            EnsureDiskFolder(Path.GetDirectoryName(relativeAssetPath));
            AssetDatabase.CopyAsset(sourcePath, relativeAssetPath);
            return relativeAssetPath;
        }

        private static void EnsureDiskFolder(string assetFolderPath)
        {
            var absolute = Path.GetFullPath(assetFolderPath);
            if (!Directory.Exists(absolute))
            {
                Directory.CreateDirectory(absolute);
            }
        }

        // ── ルーティング(フォルダ→種別、カテゴリ抽出、拡張子フィルタ) ──

        [Test]
        public void ProcessPaths_UnknownTypeFolder_IsIgnored()
        {
            var path = WriteWav($"{SourceRoot}/NotARule/Foo.wav");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);
        }

        [Test]
        public void ProcessPaths_UnmatchedExtension_IsIgnored()
        {
            var relative = $"{SourceRoot}/Se/Player/notes.txt";
            EnsureDiskFolder(Path.GetDirectoryName(relative));
            File.WriteAllText(Path.GetFullPath(relative), "memo");
            AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceSynchronousImport);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[DDrive\] ImportRule 案内:.*'Se'.*対象拡張子"));
            var report = ImportRuleService.ProcessPaths(new[] { relative }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);
        }

        // ── 案内ログ(2026-09-14 追加): 置き方を間違えたときに Console へ 1 回だけ警告する ──

        [Test]
        public void ProcessPaths_DirectlyUnderRoot_LogsHintOnce()
        {
            var relative = $"{SourceRoot}/Foo.wav";
            var path = WriteWav(relative);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[DDrive\] ImportRule 案内:.*種別フォルダの下に置いてください"));
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);

            // 同じパスをもう一度渡しても、このセッションでは 2 度目の警告は出ない。
            var second = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, second.Created);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ProcessPaths_UnknownTypeFolder_LogsHintOnce()
        {
            var path = WriteWav($"{SourceRoot}/NotARule/Foo.wav");

            LogAssert.Expect(LogType.Warning, new Regex(@"\[DDrive\] ImportRule 案内:.*'NotARule'.*種別フォルダではありません"));
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);

            ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ProcessPaths_KnownNonTargetFolder_NoHint()
        {
            // Shaders / Data は Maya→Material 経路・サンプル資産が既に使っている既知の非対象フォルダのため、
            // 種別フォルダとして不明でも警告しない。
            var path = WriteWav($"{SourceRoot}/Data/Foo.wav");

            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ProcessPaths_SamplesFolder_NoHint()
        {
            // Samples はサンプル素材の退避先([10_workflow.md] §3.3、2026-09-14)で、ImportRule の対象外。
            // Shaders / Data と同様に警告しない。
            var path = WriteWav($"{SourceRoot}/Samples/Shizuku/FBX/Foo.wav");

            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ProcessPaths_HiddenOrMetaOrFolder_NoHint()
        {
            // 隠しファイル・フォルダ自体は案内の対象外(実運用では OnPostprocessAllAssets が
            // フォルダの作成・移動もまとめて渡してくるため、ここで無視しておく必要がある)。
            var folder = $"{SourceRoot}/Se/_Check";
            EnsureDiskFolder(folder);
            AssetDatabase.Refresh();

            var hidden = $"{SourceRoot}/Se/.DS_Store";
            EnsureDiskFolder(Path.GetDirectoryName(hidden));
            File.WriteAllText(Path.GetFullPath(hidden), string.Empty);

            var report = ImportRuleService.ProcessPaths(new[] { folder, hidden }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Se / Bgm(AudioClip) ──

        [Test]
        public void ProcessPaths_Se_CreatesSeData_WithClipAndCategory()
        {
            var path = WriteWav($"{SourceRoot}/Se/Player/Slash.wav");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<SeData>($"{GameDataRoot}/Audio/SE/Player/SE_Player_Slash.asset");
            Assert.IsNotNull(data, "SeData should be created at the conventional path");
            Assert.IsNotNull(data.Clips);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(path), data.Clips[0]);
            Assert.AreEqual("Player", data.Category);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(path), data.ImportSourceGuid);
        }

        [Test]
        public void ProcessPaths_Se_Reimport_DoesNotCreateDuplicate()
        {
            var path = WriteWav($"{SourceRoot}/Se/Player/Slash.wav");
            ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);

            var second = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, second.Created);
            Assert.AreEqual(1, second.Skipped);

            var guids = AssetDatabase.FindAssets("t:" + nameof(SeData), new[] { GameDataRoot });
            Assert.AreEqual(1, guids.Length, "reimport must not create a second SeData for the same source file");
        }

        [Test]
        public void ProcessPaths_Bgm_CreatesBgmData_WithLoopBody()
        {
            var path = WriteWav($"{SourceRoot}/Bgm/Field/Theme.wav");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<BgmData>($"{GameDataRoot}/Audio/BGM/Field/BGM_Field_Theme.asset");
            Assert.IsNotNull(data);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(path), data.LoopBody);
        }

        // ── Texture ──

        [Test]
        public void ProcessPaths_Texture_CreatesTextureData_WithTextureReference()
        {
            var path = WritePng($"{SourceRoot}/Texture/Env/Rock.png");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<TextureData>($"{GameDataRoot}/Texture/Env/TEX_Env_Rock.asset");
            Assert.IsNotNull(data);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(path), data.Texture);
        }

        // ── Prefab / Canvas / Vfx(GameObject Prefab) ──

        [Test]
        public void ProcessPaths_Prefab_CreatesPrefabData_WithPrefabReference()
        {
            var path = WritePrefab($"{SourceRoot}/Prefab/Gimmick/Box.prefab");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<PrefabData>($"{GameDataRoot}/Prefab/Gimmick/PREFAB_Gimmick_Box.asset");
            Assert.IsNotNull(data);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(path), data.Prefab);
        }

        [Test]
        public void ProcessPaths_Canvas_CreatesCanvasData_WithPrefabReference()
        {
            var path = WritePrefab($"{SourceRoot}/Canvas/Title/Root.prefab");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<CanvasData>($"{GameDataRoot}/Canvas/Title/CANVAS_Title_Root.asset");
            Assert.IsNotNull(data);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(path), data.Prefab);
        }

        [Test]
        public void ProcessPaths_Vfx_CreatesVfxData_WithPrefabReference()
        {
            var path = WritePrefab($"{SourceRoot}/Vfx/Skill/Fire.prefab");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<VfxData>($"{GameDataRoot}/Vfx/Skill/VFX_Skill_Fire.asset");
            Assert.IsNotNull(data);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(path), data.Prefab);
        }

        // ── Anim / Anim2D(AnimationClip、.anim と .fbx 埋め込みの両方) ──

        [Test]
        public void ProcessPaths_Anim_FromAnimFile_CreatesAnimData_WithClip()
        {
            var path = WriteAnimClip($"{SourceRoot}/Anim/Player/Slash.anim");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<AnimData>($"{GameDataRoot}/Anim/Player/ANIM_Player_Slash.asset");
            Assert.IsNotNull(data);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AnimationClip>(path), data.Clip);
        }

        [Test]
        public void ProcessPaths_Anim_FromEmbeddedFbxClip_CreatesAnimData_WithFirstNonPreviewClip()
        {
            var path = CopyAsset(SampleAnimFbx, $"{SourceRoot}/Anim/Player/Wait.fbx");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var guids = AssetDatabase.FindAssets("t:" + nameof(AnimData), new[] { GameDataRoot });
            Assert.AreEqual(1, guids.Length);
            var data = AssetDatabase.LoadAssetAtPath<AnimData>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.IsNotNull(data.Clip);
            StringAssert.DoesNotStartWith("__preview__", data.Clip.name);
        }

        [Test]
        public void ProcessPaths_Anim2D_FromAnimFile_CreatesAnim2DData_WithClip_AndNoDirections()
        {
            var path = WriteAnimClip($"{SourceRoot}/Anim2D/Player/Walk.anim");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<Anim2DData>($"{GameDataRoot}/Anim2D/Player/ANIM2D_Player_Walk.asset");
            Assert.IsNotNull(data);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AnimationClip>(path), data.Clip);
            Assert.AreEqual(DirectionSet.None, data.Directions);
        }

        // ── Model(GameObject。FBX は既存サンプルをコピーして使う) ──

        [Test]
        public void ProcessPaths_Model_FromFbx_CreatesModelData_WithPrefabReference()
        {
            var path = CopyAsset(SampleModelFbx, $"{SourceRoot}/Model/Enemy/Box.fbx");
            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created);

            var data = AssetDatabase.LoadAssetAtPath<ModelData>($"{GameDataRoot}/Model/Enemy/MODEL_Enemy_Box.asset");
            Assert.IsNotNull(data);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(path), data.Prefab);
        }

        // ── 元ファイル削除時: Data は消えず、参照が null になるだけ(欠落は既存 Validator が担う) ──

        [Test]
        public void DeletingSourceFile_KeepsData_ButReferenceBecomesNull()
        {
            var path = WriteWav($"{SourceRoot}/Se/Player/Footstep.wav");
            ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);

            var dataPath = $"{GameDataRoot}/Audio/SE/Player/SE_Player_Footstep.asset";
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SeData>(dataPath));

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.Refresh();

            var data = AssetDatabase.LoadAssetAtPath<SeData>(dataPath);
            Assert.IsNotNull(data, "Data must not be deleted when the source file disappears");
            Assert.IsTrue(data.Clips == null || data.Clips.Length == 0 || data.Clips[0] == null,
                "the reference should now read as missing so the existing SeDataValidator reports it");
        }
    }
}
