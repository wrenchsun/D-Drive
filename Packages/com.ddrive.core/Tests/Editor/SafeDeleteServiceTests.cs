using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-6 — 安全な削除(参照チェック→Archive→確認ダイアログ→カタログ/Addressables 登録解除+
    // アイコンごと MoveAssetToTrash)。DependencyGraphServiceTests と同じ方針: 一時フォルダのみを使い、
    // 識別子は "ZzTest5006" のみを使う。実プロジェクトの GameData/カタログ/Addressables グループには書き込まない。
    public class SafeDeleteServiceTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempSafeDeleteGameData";

        private readonly List<string> _trackedPaths = new();

        [SetUp]
        public void SetUp()
        {
            DependencyGraphPostprocessor.Suppress = true;
            DependencyGraphService.ResetInMemoryCacheForTests();

            // 既定は本物の EditorUtility.DisplayDialog を絶対に呼ばせない(Test Runner はヘッドレスではなく
            // 実 Editor 上で動くため、モーダルが出ると main thread が本当にブロックされてテストが無限にハングする。
            // 2026-09-14 実測: このガードを入れる前に実際にハングし、editor_dialog_press で救出した)。
            // 各テストは必要な戻り値だけをこの既定から上書きする。
            SafeDeleteService.ConfirmDialogOverride = (_, _, _, _) => true;
            SafeDeleteService.InfoDialogOverride = (_, _, _) => { };
        }

        [TearDown]
        public void TearDown()
        {
            SafeDeleteService.ConfirmDialogOverride = null;
            SafeDeleteService.InfoDialogOverride = null;

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
                TestTempFolder.CreateFolder("TempSafeDeleteGameData");
            }
        }

        [Test]
        public void TryDelete_NoUsages_MovesToTrash_AndClearsCatalogAndAddressablesEntry()
        {
            var data = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006 Unused", "Category", "ZzTest5006Unused", gameDataRoot: TestRoot);
            var path = AssetDatabase.GetAssetPath(data);
            var guid = AssetDatabase.AssetPathToGUID(path);
            var id = data.Id;

            DependencyGraphService.UpdatePaths(new[] { path }, null);

            SafeDeleteService.ConfirmDialogOverride = (_, _, _, _) => true;

            var report = SafeDeleteService.TryDelete(data, AssetType.Se, requireGraphBuilt: true, scanCodeReferences: false);

            Assert.AreEqual(SafeDeleteService.DeleteOutcome.Deleted, report.Outcome);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SeData>(path), "Data は OS のゴミ箱へ移動されて Assets から消えているはず");

            var catalogPath = $"{TestRoot}/Catalogs/AudioCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(catalogPath);
            Assert.IsFalse(catalog != null && catalog.Entries.Any(e => e.Id == id), "カタログから登録解除されているはず");

            if (AddressableAssetSettingsDefaultObject.SettingsExists)
            {
                var entry = AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(guid);
                Assert.IsNull(entry, "Addressables エントリも削除されているはず");
            }

            Assert.IsEmpty(DependencyGraphService.FindReferencesIn(path), "依存グラフからも外れているはず");
        }

        [Test]
        public void TryDelete_HasUsages_BlocksAndDoesNotDeleteOrConfirm()
        {
            EnsureFolder();

            var data = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006 Used", "Category", "ZzTest5006Used", gameDataRoot: TestRoot);
            var dataPath = AssetDatabase.GetAssetPath(data);
            Track(dataPath);

            var referencer = new GameObject("ZzTest5006Referencer");
            string prefabPath;
            try
            {
                var emitter = referencer.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = data.Id;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                prefabPath = $"{TestRoot}/ZzTest5006Referencer.prefab";
                PrefabUtility.SaveAsPrefabAsset(referencer, prefabPath);
                Track(prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(referencer);
            }

            DependencyGraphService.UpdatePaths(new[] { dataPath, prefabPath }, null);

            var confirmCalled = false;
            SafeDeleteService.ConfirmDialogOverride = (_, _, _, _) => { confirmCalled = true; return true; };

            var report = SafeDeleteService.TryDelete(data, AssetType.Se, requireGraphBuilt: true, scanCodeReferences: false);

            Assert.AreEqual(SafeDeleteService.DeleteOutcome.BlockedByUsages, report.Outcome);
            Assert.AreEqual(1, report.BlockingUsages.Count);
            Assert.IsFalse(confirmCalled, "参照がある時点で中止するので確認ダイアログまで到達しないはず");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SeData>(dataPath), "削除されていないはず");
        }

        [Test]
        public void TryDelete_CancelledAtFinalConfirm_StillArchivesButDoesNotDelete()
        {
            var data = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006 Cancel", "Category", "ZzTest5006Cancel", gameDataRoot: TestRoot);
            var path = AssetDatabase.GetAssetPath(data);
            Track(path);

            DependencyGraphService.UpdatePaths(new[] { path }, null);

            Assert.IsFalse(ArchiveTagService.IsArchived(data));

            SafeDeleteService.ConfirmDialogOverride = (_, _, _, _) => false; // 最終確認でキャンセル

            var report = SafeDeleteService.TryDelete(data, AssetType.Se, requireGraphBuilt: true, scanCodeReferences: false);

            Assert.AreEqual(SafeDeleteService.DeleteOutcome.CancelledByUser, report.Outcome);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SeData>(path), "キャンセルしたので削除されていないはず");
            Assert.IsTrue(ArchiveTagService.IsArchived(data), "削除フローの一部として Archived タグは付いた状態で残るはず(取り消せる通常の Data 変更)");
        }

        // 「グラフ未構築」ガード(CachedFileCount==0 の判定)自体は、他の EditMode テストが同じプロセス内で
        // 共有 Library キャッシュ(Library/DDriveDeps/)へ書き込む都合上、テスト順序に依存せず再現するのが
        // 難しいため自動テストの対象にしない(要判断: docs/28 参照。実際のガード動作は手動検証で確認する)。
    }
}
