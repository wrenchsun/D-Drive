using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [削除の確認画面(2026-09-14、UE の Delete Assets 相当)] — AssetDeleteAnalysisService.Analyze の分類ロジック。
    // DependencyGraphServiceTests/DependencyTreeBuilderTests と同じ方針: 一時フォルダのみを使い、
    // 識別子は "ZzTestDelAnalysis" のみを使う。SeData.AnchorId(AssetId<AnchorMarker>)を汎用の
    // id ホルダーとして使う手法も同じ(DependencyGraphCollector は総称引数を見ないため転用できる)。
    public class AssetDeleteAnalysisServiceTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempDelAnalysisGameData";

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
                AssetDatabase.SaveAssets();
            }
        }

        private void Track(string path) => _trackedPaths.Add(path);

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempDelAnalysisGameData");
            }
        }

        private static SeData CreateSe(string name)
            => (SeData)AssetCreationService.Create(typeof(SeData), AssetType.Se, name, "Category", name, gameDataRoot: TestRoot);

        private static void SetAnchorRef(SeData data, AssetType type, ulong id)
        {
            var so = new SerializedObject(data);
            var anchorId = so.FindProperty("AnchorId");
            anchorId.FindPropertyRelative("value").ulongValue = id;
            anchorId.FindPropertyRelative("type").enumValueIndex = (int)type;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Analyze_ClassifiesDataPrefabUsages_AndDeleteTargetInternalReferences()
        {
            EnsureFolder();

            var t1 = CreateSe("ZzTestDelAnalysisT1");
            var t1Path = AssetDatabase.GetAssetPath(t1);
            Track(t1Path);

            var t2 = CreateSe("ZzTestDelAnalysisT2"); // 削除対象どうし: t2 が t1 を参照する
            var t2Path = AssetDatabase.GetAssetPath(t2);
            Track(t2Path);
            SetAnchorRef(t2, AssetType.Se, t1.Id);

            var externalData = CreateSe("ZzTestDelAnalysisExternalData");
            var externalDataPath = AssetDatabase.GetAssetPath(externalData);
            Track(externalDataPath);
            SetAnchorRef(externalData, AssetType.Se, t1.Id);

            GameObject referencer = null;
            string prefabPath;
            try
            {
                referencer = new GameObject("ZzTestDelAnalysisReferencer");
                var emitter = referencer.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = t1.Id;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                prefabPath = $"{TestRoot}/ZzTestDelAnalysisReferencer.prefab";
                PrefabUtility.SaveAsPrefabAsset(referencer, prefabPath);
                Track(prefabPath);
            }
            finally
            {
                if (referencer != null)
                {
                    Object.DestroyImmediate(referencer);
                }
            }

            DependencyGraphService.UpdatePaths(new[] { t1Path, t2Path, externalDataPath, prefabPath }, null);

            var analysis = AssetDeleteAnalysisService.Analyze(new List<DeleteTarget>
            {
                new DeleteTarget(t1, AssetType.Se, t1Path),
                new DeleteTarget(t2, AssetType.Se, t2Path),
            });

            var fromExternalData = analysis.Usages.FirstOrDefault(u => u.Reference.SourcePath == externalDataPath);
            Assert.AreEqual(ReferenceFileKind.Data, fromExternalData.Kind);
            Assert.IsFalse(fromExternalData.IsFromDeleteTarget);

            var fromPrefab = analysis.Usages.FirstOrDefault(u => u.Reference.SourcePath == prefabPath);
            Assert.AreEqual(ReferenceFileKind.Prefab, fromPrefab.Kind);
            Assert.IsFalse(fromPrefab.IsFromDeleteTarget);

            var fromT2 = analysis.Usages.FirstOrDefault(u => u.Reference.SourcePath == t2Path);
            Assert.IsTrue(fromT2.IsFromDeleteTarget, "削除対象どうしの参照は IsFromDeleteTarget=true で区別されるはず");

            Assert.AreEqual(2, analysis.ExternalUsageCount, "外部からの参照(Data 1件 + Prefab 1件)のみを数えるはず");
            Assert.IsTrue(analysis.HasBlockingExternalUsages);
            Assert.IsFalse(analysis.HasExternalSceneUsages);
        }

        [Test]
        public void Analyze_Dependencies_WouldBecomeUnused_TrueWhenNoOtherUsage_FalseWhenUsedElsewhere()
        {
            var target = CreateSe("ZzTestDelAnalysisTarget");
            var targetPath = AssetDatabase.GetAssetPath(target);
            Track(targetPath);

            var onlyUsedByTarget = CreateSe("ZzTestDelAnalysisOnlyUsedByTarget");
            Track(AssetDatabase.GetAssetPath(onlyUsedByTarget));

            var usedElsewhereToo = CreateSe("ZzTestDelAnalysisUsedElsewhereToo");
            Track(AssetDatabase.GetAssetPath(usedElsewhereToo));

            var otherReferencer = CreateSe("ZzTestDelAnalysisOtherReferencer");
            var otherReferencerPath = AssetDatabase.GetAssetPath(otherReferencer);
            Track(otherReferencerPath);
            SetAnchorRef(otherReferencer, AssetType.Se, usedElsewhereToo.Id);

            SetAnchorRef(target, AssetType.Se, onlyUsedByTarget.Id);

            DependencyGraphService.UpdatePaths(new[] { targetPath, otherReferencerPath }, null);

            // target が2つ目の依存先も持つよう、Events 経由でもう1件足す(AnchorId は1個しか無いため)。
            var target2 = CreateSe("ZzTestDelAnalysisTarget2");
            var target2Path = AssetDatabase.GetAssetPath(target2);
            Track(target2Path);
            SetAnchorRef(target2, AssetType.Se, usedElsewhereToo.Id);
            DependencyGraphService.UpdatePaths(new[] { target2Path }, null);

            var analysis = AssetDeleteAnalysisService.Analyze(new List<DeleteTarget>
            {
                new DeleteTarget(target, AssetType.Se, targetPath),
                new DeleteTarget(target2, AssetType.Se, target2Path),
            });

            var onlyDep = analysis.Dependencies.FirstOrDefault(d => d.Id == onlyUsedByTarget.Id);
            Assert.IsTrue(onlyDep.WouldBecomeUnused, "削除対象以外から使われていない依存先は WouldBecomeUnused=true のはず");

            var sharedDep = analysis.Dependencies.FirstOrDefault(d => d.Id == usedElsewhereToo.Id);
            Assert.IsFalse(sharedDep.WouldBecomeUnused, "削除対象以外(otherReferencer)からも使われているので false のはず");
        }

        [Test]
        public void Analyze_Dependencies_IsAlsoDeleteTarget_WhenDependencyIsAnotherDeleteTarget()
        {
            var a = CreateSe("ZzTestDelAnalysisA");
            var aPath = AssetDatabase.GetAssetPath(a);
            Track(aPath);

            var b = CreateSe("ZzTestDelAnalysisB");
            var bPath = AssetDatabase.GetAssetPath(b);
            Track(bPath);

            SetAnchorRef(a, AssetType.Se, b.Id);
            DependencyGraphService.UpdatePaths(new[] { aPath, bPath }, null);

            var analysis = AssetDeleteAnalysisService.Analyze(new List<DeleteTarget>
            {
                new DeleteTarget(a, AssetType.Se, aPath),
                new DeleteTarget(b, AssetType.Se, bPath),
            });

            var dep = analysis.Dependencies.FirstOrDefault(d => d.Id == b.Id);
            Assert.IsTrue(dep.IsAlsoDeleteTarget);
        }

        [Test]
        public void Analyze_Dependencies_UnresolvedTarget_IsMarkedUnresolved()
        {
            var a = CreateSe("ZzTestDelAnalysisUnresolved");
            var aPath = AssetDatabase.GetAssetPath(a);
            Track(aPath);

            SetAnchorRef(a, AssetType.Se, 0xABCDEFUL);
            DependencyGraphService.UpdatePaths(new[] { aPath }, null);

            var analysis = AssetDeleteAnalysisService.Analyze(new List<DeleteTarget> { new DeleteTarget(a, AssetType.Se, aPath) });

            var dep = analysis.Dependencies.FirstOrDefault(d => d.Id == 0xABCDEFUL);
            Assert.IsTrue(dep.IsUnresolved);
        }
    }
}
