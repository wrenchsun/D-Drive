using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Event;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-5 — 依存関係グラフ(収集/キャッシュ/差分更新)のテスト。
    // キャッシュ本体(Library/DDriveDeps/)は .gitignore 済みのため書き込んでも git status には出ないが、
    // 実プロジェクトの Library に残骸(削除済みファイルを指す索引)を残さないよう、作った分は必ず
    // UpdatePaths(deletedPaths:) で外してから Assets 側も削除する。識別子は "ZzTest5005" 系のみを使う。
    //
    // ImportRuleServiceTests と同じ方針で AssetPostprocessor 経由の delayCall は経由せず、
    // DependencyGraphService.UpdatePaths を直接呼ぶ(タイミング依存を避ける)。
    public class DependencyGraphServiceTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempDepsGameData";

        private readonly List<string> _trackedPaths = new();

        [SetUp]
        public void SetUp()
        {
            DependencyGraphPostprocessor.Suppress = true;
            DependencyGraphService.ResetInMemoryCacheForTests();
        }

        [TearDown]
        public void TearDown()
        {
            if (_trackedPaths.Count > 0)
            {
                DependencyGraphService.UpdatePaths(null, _trackedPaths);
                _trackedPaths.Clear();
            }

            DependencyGraphService.ResetInMemoryCacheForTests();
            DependencyGraphPostprocessor.Suppress = false;

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        private void Track(string path) => _trackedPaths.Add(path);

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                TestTempFolder.CreateFolder("TempDepsGameData");
            }
        }

        [Test]
        public void UpdatePaths_DataAssetWithAssetId_IsFoundByFindUsages_AndFindReferencesIn()
        {
            var data = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5005 Se", "Category", "ZzTest5005DataId", gameDataRoot: TestRoot);
            var path = AssetDatabase.GetAssetPath(data);
            Track(path);

            var so = new SerializedObject(data);
            var anchorId = so.FindProperty("AnchorId");
            anchorId.FindPropertyRelative("value").ulongValue = 424242UL;
            anchorId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Anchor;
            so.ApplyModifiedProperties();
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }

            DependencyGraphService.UpdatePaths(new[] { path }, null);

            var usages = DependencyGraphService.FindUsages(AssetType.Anchor, 424242UL);
            Assert.IsTrue(usages.Any(u => u.SourcePath == path && u.PropertyPath == "AnchorId" && u.ComponentType == nameof(SeData)));

            var refs = DependencyGraphService.FindReferencesIn(path);
            Assert.IsTrue(refs.Any(r => r.TargetType == AssetType.Anchor && r.TargetId == 424242UL));
        }

        [Test]
        public void UpdatePaths_DataAssetWithAssetRefEvent_IsFoundByFindUsages()
        {
            var data = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5005 Se Event", "Category", "ZzTest5005EventRef", gameDataRoot: TestRoot);
            var path = AssetDatabase.GetAssetPath(data);
            Track(path);

            data.Events = new[]
            {
                new AssetEvent
                {
                    Trigger = EventTrigger.OnSpawn,
                    Action = EventAction.PlayAsset,
                    Target = new AssetRef { Type = AssetType.Vfx, Id = 777777UL },
                },
            };
            EditorUtility.SetDirty(data);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }

            DependencyGraphService.UpdatePaths(new[] { path }, null);

            var usages = DependencyGraphService.FindUsages(AssetType.Vfx, 777777UL);
            Assert.IsTrue(usages.Any(u => u.SourcePath == path && u.ComponentType == nameof(SeData)));
        }

        [Test]
        public void UpdatePaths_PrefabWithMonoBehaviourIdRef_CollectsObjectPathAndComponentType()
        {
            EnsureFolder();

            var root = new GameObject("ZzTest5005Root");
            GameObject child = null;
            try
            {
                child = new GameObject("ZzTest5005Child");
                child.transform.SetParent(root.transform);
                var emitter = child.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = 555555UL;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                var prefabPath = $"{TestRoot}/ZzTest5005.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Track(prefabPath);

                DependencyGraphService.UpdatePaths(new[] { prefabPath }, null);

                var usages = DependencyGraphService.FindUsages(AssetType.Se, 555555UL);
                Assert.IsTrue(usages.Any(u => u.SourcePath == prefabPath
                    && u.ObjectPath == "ZzTest5005Child"
                    && u.ComponentType == nameof(SeEmitter)
                    && u.PropertyPath == "seId"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UpdatePaths_SceneWithMonoBehaviourIdRef_CollectsUsage_AndLeavesSceneCountUnchanged()
        {
            EnsureFolder();

            var scenesBefore = SceneManager.sceneCount;
            var activeScene = SceneManager.GetActiveScene();

            var go = new GameObject("ZzTest5005SceneRoot");
            try
            {
                var emitter = go.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = 666666UL;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                var scenePath = $"{TestRoot}/ZzTest5005Scene.unity";

                // EditorSceneManager.NewScene(..., Additive) は Test Runner の「無題・未保存シーン」上では
                // 使えない(Unity 側の制約: "Cannot create a new scene additively with an untitled scene
                // unsaved.")。そのため新規シーンを作らず、今アクティブなシーン(Test Runner 用の無題シーン
                // に今回のオブジェクトを足したもの)を saveAsCopy でファイルへスナップショットするだけにする
                // (アクティブシーン自体の紐付け・ダーティ状態は変えない)。
                EditorSceneManager.SaveScene(activeScene, scenePath, saveAsCopy: true);
                Track(scenePath);

                Assert.AreEqual(scenesBefore, SceneManager.sceneCount);

                DependencyGraphService.UpdatePaths(new[] { scenePath }, null);

                Assert.AreEqual(scenesBefore, SceneManager.sceneCount, "収集後も開いているシーン数は変わらないはず(Additive で開いて閉じるだけ)");

                var usages = DependencyGraphService.FindUsages(AssetType.Se, 666666UL);
                Assert.IsTrue(usages.Any(u => u.SourcePath == scenePath && u.ObjectPath == "ZzTest5005SceneRoot" && u.ComponentType == nameof(SeEmitter)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void UpdatePaths_Deleted_RemovesFromIndex()
        {
            var data = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5005 Del", "Category", "ZzTest5005Delete", gameDataRoot: TestRoot);
            var path = AssetDatabase.GetAssetPath(data);

            var so = new SerializedObject(data);
            var anchorId = so.FindProperty("AnchorId");
            anchorId.FindPropertyRelative("value").ulongValue = 313131UL;
            anchorId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Anchor;
            so.ApplyModifiedProperties();
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }

            DependencyGraphService.UpdatePaths(new[] { path }, null);
            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Anchor, 313131UL).Any(u => u.SourcePath == path));

            AssetDatabase.DeleteAsset(path);
            DependencyGraphService.UpdatePaths(null, new[] { path });

            Assert.IsFalse(DependencyGraphService.FindUsages(AssetType.Anchor, 313131UL).Any(u => u.SourcePath == path));
            Assert.IsEmpty(DependencyGraphService.FindReferencesIn(path));
        }

        [Test]
        public void FindUnusedIds_UnreferencedAsset_IsListed_ReferencedAsset_IsNot()
        {
            var unused = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5005 Unused", "Category", "ZzTest5005Unused", gameDataRoot: TestRoot);
            Track(AssetDatabase.GetAssetPath(unused));

            var used = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5005 Used", "Category", "ZzTest5005Used", gameDataRoot: TestRoot);
            var usedPath = AssetDatabase.GetAssetPath(used);
            Track(usedPath);

            GameObject referencer = null;
            string referencerPath;
            try
            {
                referencer = new GameObject("ZzTest5005Referencer");
                var emitter = referencer.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = used.Id;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                referencerPath = $"{TestRoot}/ZzTest5005Referencer.prefab";
                PrefabUtility.SaveAsPrefabAsset(referencer, referencerPath);
                Track(referencerPath);
            }
            finally
            {
                if (referencer != null)
                {
                    Object.DestroyImmediate(referencer);
                }
            }

            DependencyGraphService.UpdatePaths(new[] { usedPath, referencerPath }, null);

            var unusedIds = DependencyGraphService.FindUnusedIds();
            Assert.IsTrue(unusedIds.Any(u => u.Type == AssetType.Se && u.Id == unused.Id), "参照されていない ID は一覧に出るはず");
            Assert.IsFalse(unusedIds.Any(u => u.Type == AssetType.Se && u.Id == used.Id), "参照されている ID は一覧に出ないはず");
        }

        // [49_p12_ms2026_install_2026-09-20.md] 項目5 — 起動直後の全量再インポートで OnPostprocessAllAssets が
        // 読み取り専用パッケージ内のシーンのパスまで渡してくるため、RebuildAll/UpdatePaths がそれを開こうとして
        // 「Opening scene in read-only package!」のモーダルが連続表示される事故があった。純粋な判定ロジック
        // (IsScannablePath)だけを、PackageManager への実アクセスを伴わずに検証する。
        [Test]
        public void IsScannablePath_AssetsPath_IsScannable()
        {
            Assert.IsTrue(DependencyGraphService.IsScannablePath("Assets/GameData/PreviewScenes/PreviewScene.unity", null));
        }

        [Test]
        public void IsScannablePath_PackagesPath_WithoutPackageInfo_IsNotScannable()
        {
            // 通常は起こらない(Packages/ 配下は必ず PackageInfo を持つ)が、null(解決不能)は安全側 = 対象外にする。
            Assert.IsFalse(DependencyGraphService.IsScannablePath("Packages/com.example.unknown/Foo.unity", null));
        }

        [TestCase(PackageSource.Embedded, true)]
        [TestCase(PackageSource.Local, true)]
        [TestCase(PackageSource.Git, false)]
        [TestCase(PackageSource.Registry, false)]
        [TestCase(PackageSource.BuiltIn, false)]
        [TestCase(PackageSource.LocalTarball, false)]
        public void IsScannablePath_PackagesPath_DependsOnPackageSource(PackageSource source, bool expected)
        {
            Assert.AreEqual(expected, DependencyGraphService.IsScannablePath(
                "Packages/com.ddrive.core/Tests/Editor/Fixtures/Whatever.unity", source));
        }

        [TestCase(null)]
        [TestCase("")]
        public void IsScannablePath_NullOrEmptyPath_IsNotScannable(string path)
        {
            Assert.IsFalse(DependencyGraphService.IsScannablePath(path, PackageSource.Embedded));
        }

        // 実際にこの開発リポジトリの D-Drive 自身(埋め込みパッケージ)のパスは対象、
        // 他パッケージ(PackageCache/レジストリ配布。ここでは Addressables で代表)は対象外になることを、
        // 実際の PackageInfo 解決込みで確認する(RebuildAll と同じ絞り込みが機能していることの実地確認)。
        [Test]
        public void IsScannablePath_DevRepo_OwnEmbeddedPackage_IsScannable()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DependencyGraphService).Assembly);
            Assume.That(packageInfo != null, "D-Drive がパッケージ化されていない環境ではスキップ");
            Assume.That(packageInfo.source == PackageSource.Embedded, "開発リポジトリでは埋め込みパッケージのはず");

            var samplePath = packageInfo.assetPath + "/Tests/Editor/DependencyGraphServiceTests.cs";
            Assert.IsTrue(DependencyGraphService.IsScannablePath(samplePath, packageInfo.source));
        }

        [Test]
        public void IsScannablePath_OtherPackage_IsNotScannable()
        {
            const string otherPackageJson = "Packages/com.unity.addressables/package.json";
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(otherPackageJson);
            Assume.That(packageInfo != null, "com.unity.addressables が導入されていない環境ではスキップ");

            // 実在しないが同じパッケージ配下のシーンパスでも同じ判定になるはず(判定は path prefix + source のみで決まる)。
            var sceneUnderThatPackage = "Packages/com.unity.addressables/Tests/Editor/Expected/Sample.unity";
            Assert.IsFalse(DependencyGraphService.IsScannablePath(sceneUnderThatPackage, packageInfo.source));
        }
    }
}
