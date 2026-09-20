using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Tests.Editor
{
    // [削除の確認画面(2026-09-14、UE の Delete Assets 相当)] — AssetDeleteExecutionService の各実行分岐。
    // ウィンドウ(UI)は開かず、このサービスだけを直接叩く(CLAUDE.md §0-9 のとおり実プロジェクトの
    // GameData/カタログ/Addressables/既存シーンには書き込まない。識別子は "ZzTestDelExec" のみ)。
    public class AssetDeleteExecutionServiceTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempDelExecGameData";

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
                TestTempFolder.CreateFolder("TempDelExecGameData");
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
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
        }

        [Test]
        public void Execute_ArchiveOnly_ArchivesPrimaryAndCascade_DoesNotDelete()
        {
            var primary = CreateSe("ZzTestDelExecArchivePrimary");
            var primaryPath = AssetDatabase.GetAssetPath(primary);
            Track(primaryPath);

            var cascade = CreateSe("ZzTestDelExecArchiveCascade");
            var cascadePath = AssetDatabase.GetAssetPath(cascade);
            Track(cascadePath);

            Assert.IsFalse(ArchiveTagService.IsArchived(primary));
            Assert.IsFalse(ArchiveTagService.IsArchived(cascade));

            var request = new DeleteExecutionRequest
            {
                Action = DeleteAction.ArchiveOnly,
                PrimaryTargets = new List<DeleteTarget> { new DeleteTarget(primary, AssetType.Se, primaryPath) },
                CascadeTargets = new List<DeleteTarget> { new DeleteTarget(cascade, AssetType.Se, cascadePath) },
            };

            var result = AssetDeleteExecutionService.Execute(request);

            Assert.AreEqual(2, result.Results.Count);
            Assert.IsTrue(result.Results.All(r => r.ArchivedOnly && !r.Deleted));
            Assert.IsTrue(ArchiveTagService.IsArchived(primary));
            Assert.IsTrue(ArchiveTagService.IsArchived(cascade));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SeData>(primaryPath), "Archive のみなので削除されていないはず");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SeData>(cascadePath));
        }

        [Test]
        public void Execute_ForceDelete_DeletesEvenWithUsages_AndUsageRemainsDangling()
        {
            EnsureFolder();

            var target = CreateSe("ZzTestDelExecForceTarget");
            var targetPath = AssetDatabase.GetAssetPath(target);
            var targetId = target.Id;

            var referencer = CreateSe("ZzTestDelExecForceReferencer");
            var referencerPath = AssetDatabase.GetAssetPath(referencer);
            Track(referencerPath);
            SetAnchorRef(referencer, AssetType.Se, targetId);

            DependencyGraphService.UpdatePaths(new[] { targetPath, referencerPath }, null);
            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, targetId).Any(u => u.SourcePath == referencerPath));

            var request = new DeleteExecutionRequest
            {
                Action = DeleteAction.ForceDelete,
                PrimaryTargets = new List<DeleteTarget> { new DeleteTarget(target, AssetType.Se, targetPath) },
                ScanCodeReferences = false,
            };

            var result = AssetDeleteExecutionService.Execute(request);

            Assert.AreEqual(1, result.Results.Count);
            Assert.IsTrue(result.Results[0].Deleted);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SeData>(targetPath), "強制削除なので参照が残っていても消えるはず");
            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, targetId).Any(u => u.SourcePath == referencerPath),
                "参照元(referencer)は書き換えていないので、削除後も参照が残ったままのはず(実行時 Placeholder になる想定)");
        }

        [Test]
        public void Execute_ReplaceThenDelete_DataOnly_ReplacesAndDeletes()
        {
            var target = CreateSe("ZzTestDelExecReplaceTarget");
            var targetPath = AssetDatabase.GetAssetPath(target);
            var targetId = target.Id;

            var replacement = CreateSe("ZzTestDelExecReplaceReplacement");
            Track(AssetDatabase.GetAssetPath(replacement));

            var referencer = CreateSe("ZzTestDelExecReplaceReferencer");
            var referencerPath = AssetDatabase.GetAssetPath(referencer);
            Track(referencerPath);
            SetAnchorRef(referencer, AssetType.Se, targetId);

            DependencyGraphService.UpdatePaths(new[] { targetPath, referencerPath }, null);

            var request = new DeleteExecutionRequest
            {
                Action = DeleteAction.ReplaceThenDelete,
                PrimaryTargets = new List<DeleteTarget> { new DeleteTarget(target, AssetType.Se, targetPath) },
                ReplacementPlans = new List<ReplacementPlan> { new ReplacementPlan(AssetType.Se, targetId, replacement.Id) },
                ScanCodeReferences = false,
            };

            var result = AssetDeleteExecutionService.Execute(request);

            Assert.AreEqual(1, result.Results.Count);
            Assert.IsTrue(result.Results[0].Deleted);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SeData>(targetPath));
            CollectionAssert.Contains(result.ChangedDataPaths, referencerPath);
            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, replacement.Id).Any(u => u.SourcePath == referencerPath));
        }

        [Test]
        public void Execute_ReplaceThenDelete_RemainingSceneUsage_HoldsBackAsArchiveOnly()
        {
            EnsureFolder();

            var target = CreateSe("ZzTestDelExecReplaceSceneTarget");
            var targetPath = AssetDatabase.GetAssetPath(target);
            var targetId = target.Id;
            Track(targetPath); // Scene 参照が残るため削除されずアーカイブのみで残る想定(後始末が必要)

            var replacement = CreateSe("ZzTestDelExecReplaceSceneReplacement");
            Track(AssetDatabase.GetAssetPath(replacement));

            var scenesBefore = SceneManager.sceneCount;
            var activeScene = SceneManager.GetActiveScene();
            var go = new GameObject("ZzTestDelExecReplaceSceneRoot");
            string scenePath;
            try
            {
                var emitter = go.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = targetId;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                scenePath = $"{TestRoot}/ZzTestDelExecReplaceScene.unity";
                EditorSceneManager.SaveScene(activeScene, scenePath, saveAsCopy: true);
                Track(scenePath);

                DependencyGraphService.UpdatePaths(new[] { targetPath, scenePath }, null);
                Assert.AreEqual(scenesBefore, SceneManager.sceneCount);

                var request = new DeleteExecutionRequest
                {
                    Action = DeleteAction.ReplaceThenDelete,
                    PrimaryTargets = new List<DeleteTarget> { new DeleteTarget(target, AssetType.Se, targetPath) },
                    ReplacementPlans = new List<ReplacementPlan> { new ReplacementPlan(AssetType.Se, targetId, replacement.Id) },
                    ScanCodeReferences = false,
                };

                var result = AssetDeleteExecutionService.Execute(request);

                Assert.AreEqual(1, result.Results.Count);
                Assert.IsTrue(result.Results[0].ArchivedOnly, "Scene 参照が残るので削除せずアーカイブのみになるはず");
                Assert.IsFalse(result.Results[0].Deleted);
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SeData>(targetPath));
                Assert.IsTrue(result.RemainingSceneUsages.Any(u => u.SourcePath == scenePath));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Execute_CascadeTarget_DeletedWhenUnused_SkippedWhenStillUsedElsewhere()
        {
            EnsureFolder();

            var primary = CreateSe("ZzTestDelExecCascadePrimary");
            var primaryPath = AssetDatabase.GetAssetPath(primary);

            var trulyUnusedDependency = CreateSe("ZzTestDelExecCascadeUnused");
            var unusedPath = AssetDatabase.GetAssetPath(trulyUnusedDependency);

            var stillUsedDependency = CreateSe("ZzTestDelExecCascadeStillUsed");
            var stillUsedPath = AssetDatabase.GetAssetPath(stillUsedDependency);
            Track(stillUsedPath);

            var otherReferencer = CreateSe("ZzTestDelExecCascadeOtherReferencer");
            var otherReferencerPath = AssetDatabase.GetAssetPath(otherReferencer);
            Track(otherReferencerPath);
            SetAnchorRef(otherReferencer, AssetType.Se, stillUsedDependency.Id);

            DependencyGraphService.UpdatePaths(new[] { primaryPath, unusedPath, stillUsedPath, otherReferencerPath }, null);

            var request = new DeleteExecutionRequest
            {
                Action = DeleteAction.ForceDelete,
                PrimaryTargets = new List<DeleteTarget> { new DeleteTarget(primary, AssetType.Se, primaryPath) },
                CascadeTargets = new List<DeleteTarget>
                {
                    new DeleteTarget(trulyUnusedDependency, AssetType.Se, unusedPath),
                    new DeleteTarget(stillUsedDependency, AssetType.Se, stillUsedPath),
                },
                ScanCodeReferences = false,
            };

            var result = AssetDeleteExecutionService.Execute(request);

            // ForceDelete は「一緒に削除」対象も無条件で削除する(強制削除の一部として選んだため)。
            Assert.IsTrue(result.Results.Any(r => r.Target.Path == unusedPath && r.Deleted));
            Assert.IsTrue(result.Results.Any(r => r.Target.Path == stillUsedPath && r.Deleted));
        }

        [Test]
        public void Execute_ReplaceThenDelete_CascadeTarget_SkippedWhenStillUsedElsewhere()
        {
            EnsureFolder();

            var primary = CreateSe("ZzTestDelExecReplaceCascadePrimary");
            var primaryPath = AssetDatabase.GetAssetPath(primary);

            var replacement = CreateSe("ZzTestDelExecReplaceCascadeReplacement");
            Track(AssetDatabase.GetAssetPath(replacement));

            var stillUsedDependency = CreateSe("ZzTestDelExecReplaceCascadeStillUsed");
            var stillUsedPath = AssetDatabase.GetAssetPath(stillUsedDependency);
            Track(stillUsedPath);

            var otherReferencer = CreateSe("ZzTestDelExecReplaceCascadeOtherReferencer");
            var otherReferencerPath = AssetDatabase.GetAssetPath(otherReferencer);
            Track(otherReferencerPath);
            SetAnchorRef(otherReferencer, AssetType.Se, stillUsedDependency.Id);

            DependencyGraphService.UpdatePaths(new[] { primaryPath, stillUsedPath, otherReferencerPath }, null);

            var request = new DeleteExecutionRequest
            {
                Action = DeleteAction.ReplaceThenDelete,
                PrimaryTargets = new List<DeleteTarget> { new DeleteTarget(primary, AssetType.Se, primaryPath) },
                CascadeTargets = new List<DeleteTarget> { new DeleteTarget(stillUsedDependency, AssetType.Se, stillUsedPath) },
                ReplacementPlans = new List<ReplacementPlan>(),
                ScanCodeReferences = false,
            };

            var result = AssetDeleteExecutionService.Execute(request);

            Assert.IsFalse(result.Results.Any(r => r.Target.Path == stillUsedPath), "他から使われている一緒に削除対象は削除しないはず");
            Assert.IsTrue(result.SkippedCascadeStillUsed.Any(u => u.SourcePath == otherReferencerPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SeData>(stillUsedPath));
        }

        // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-7) — コード参照のヒット
        // (ファイル:行)は削除**前**に取る。削除後は MoveAssetToTrash 済みのオブジェクトが fake null に
        // なるため、結果画面で `r.Target.Asset != null` を条件に取り直していた旧実装ではヒット一覧が
        // 絶対に出なかった(docs/09 §10 の「クリックでエディタを開く」が不動作)。
        // 「コード参照あり」はこのテスト自身のソース中の定数参照で再現する(AssetCreationService が作る
        // ファイル名 "SE_Category_ZzTestDelExecScanProbe" → AssetIdGenerator.ToConstantName の規則):
        // SEID.CategoryZzTestDelExecScanProbe
        [Test]
        public void Execute_ForceDelete_ScanCodeReferences_CapturesHitsBeforeDelete()
        {
            var asset = CreateSe("ZzTestDelExecScanProbe");
            var path = AssetDatabase.GetAssetPath(asset);
            Track(path);

            var request = new DeleteExecutionRequest
            {
                Action = DeleteAction.ForceDelete,
                PrimaryTargets = new List<DeleteTarget> { new DeleteTarget(asset, AssetType.Se, path) },
                ScanCodeReferences = true,
            };

            var result = AssetDeleteExecutionService.Execute(request);

            var perAsset = result.Results.Single();
            Assert.IsTrue(perAsset.Deleted);
            Assert.IsNotNull(perAsset.CodeReferenceWarning, "自前コードに ID 定数の参照があれば警告が付くはず");
            Assert.IsNotNull(perAsset.CodeReferenceHits, "警告が出たらヒット一覧(ファイル:行)も入るはず");
            Assert.Greater(perAsset.CodeReferenceHits.Count, 0);
            Assert.Greater(perAsset.CodeReferenceHits[0].Line, 0);
        }

        // 2026-09-17([41] P2-8) — 結果画面が「アーカイブのみ」と「参照が残っていて削除できなかった」を
        // 出し分けられるよう、実行した操作を結果に持たせる。
        [Test]
        public void Execute_RecordsRequestedActionInResult()
        {
            var asset = CreateSe("ZzTestDelExecActionRecord");
            var path = AssetDatabase.GetAssetPath(asset);
            Track(path);

            var request = new DeleteExecutionRequest
            {
                Action = DeleteAction.ArchiveOnly,
                PrimaryTargets = new List<DeleteTarget> { new DeleteTarget(asset, AssetType.Se, path) },
                ScanCodeReferences = false,
            };

            var result = AssetDeleteExecutionService.Execute(request);

            Assert.AreEqual(DeleteAction.ArchiveOnly, result.Action);
            Assert.IsTrue(result.Results.Single().ArchivedOnly);
        }
    }
}
