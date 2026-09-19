using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [44_review_2026-09-19.md] P1-1 の再発防止テスト。
    //
    // 実アセット相当の「開いて編集中(dirty だが未保存)」の AssetDataBase が存在する状態で、
    // 本番の Editor サービス(AssetCreationService.Create / AddressablesSync.SyncAll /
    // AddressablesRegistrationValidator の FixAction)を呼んでも、その無関係な dirty アセットの
    // Version が変わらないことを固定する(6bba07a はテスト自身の SaveAssets() しか塞いでおらず、
    // これらの本番コード内の引数なし SaveAssets() は素通しだった)。
    public class VersionStampLeakRegressionTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempVersionStampLeak";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); } // Addressables 設定の dirty を後続テストに持ち越さない
            }
        }

        // 「実アセットを開いて編集中(dirty だが未保存)」を模す: 保存済みの SeData を作ってから
        // 1 フィールドだけ書き換えて SetDirty するが、あえてここでは保存しない。
        private static SeData MakeDirtyBystander(string identifier)
        {
            var bystander = (SeData)AssetCreationService.Create(
                typeof(SeData), AssetType.Se, "Bystander", "Player", identifier, gameDataRoot: TestRoot);
            Assert.IsNotNull(bystander);
            Assert.AreEqual(1, bystander.Version, "precondition: 新規作成は v1");

            bystander.DisplayName = "Bystander (unsaved edit)";
            EditorUtility.SetDirty(bystander);
            return bystander;
        }

        [Test]
        public void Create_DoesNotBumpVersionOfUnrelatedDirtyAsset()
        {
            var bystander = MakeDirtyBystander("Bystander1");
            var expectedVersion = bystander.Version;

            var created = AssetCreationService.Create(
                typeof(SeData), AssetType.Se, "Target", "Player", "Target1", gameDataRoot: TestRoot);

            Assert.IsNotNull(created);
            Assert.AreEqual(1, created.Version, "新規作成された側は v1 になる(StampNew)");
            Assert.AreEqual(expectedVersion, bystander.Version, "無関係な dirty アセットの Version は進まない");
        }

        [Test]
        public void AddressablesSync_SyncAll_DoesNotBumpVersionOfUnrelatedDirtyAsset()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var bystander = MakeDirtyBystander("Bystander2");
            var expectedVersion = bystander.Version;

            // SyncAll は Tests/ 配下を対象外にする(includeTestFolders: false)ため、この bystander 自体は
            // SyncAll の同期対象にはならない。ここで確認したいのは「SyncAll 内部の(抑止された)
            // AssetDatabase.SaveAssets() が、そのとき project 内で偶然 dirty な他の AssetDataBase まで
            // 巻き込んで版数を進めないか」であり、SyncAll 自身が bystander を直しに行くかどうかは関係ない。
            AddressablesSync.SyncAll(log: false);

            Assert.AreEqual(expectedVersion, bystander.Version, "無関係な dirty アセットの Version は進まない");
        }

        [Test]
        public void AddressablesRegistrationValidator_FixAction_DoesNotBumpVersionOfUnrelatedDirtyAsset()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var bystander = MakeDirtyBystander("Bystander3");
            var expectedVersion = bystander.Version;

            var target = AssetCreationService.Create(
                typeof(SeData), AssetType.Se, "Target", "Player", "Target3", gameDataRoot: TestRoot);
            Assert.IsNotNull(target);
            Assert.IsTrue(AddressablesSync.RemoveEntry(target));

            var validator = new AddressablesRegistrationValidator { IncludeTestFolders = true };
            var results = new List<ValidationResult>(
                validator.Validate(target, new ValidationContext(new List<AssetDataBase> { target })));
            Assert.AreEqual(1, results.Count, "precondition: Addressables 未登録の Error が 1 件");
            Assert.IsNotNull(results[0].FixAction);

            results[0].FixAction();

            Assert.IsNotNull(AddressablesSync.FindEntry(target), "precondition: FixAction が登録している");
            Assert.AreEqual(expectedVersion, bystander.Version, "無関係な dirty アセットの Version は進まない");
        }
    }
}
