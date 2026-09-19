using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Materials;
using DDrive.Editor.Model;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [05_model_animation.md] A-4 / U-2(2026-09-17) — Slot 自動収集が「Renderer が使っている Material から作られた
    // MaterialData」を引き当てて割り当てる(以前は常に None だった)。
    public class ModelSlotBinderTests
    {
        private const string TestRoot = "Packages/com.ddrive.core/Tests/Editor/TempSlotGameData";
        private const string TempFolder = "Packages/com.ddrive.core/Tests/Editor/TempSlot";
        private const string MaterialPath = TempFolder + "/SlotBinderMat.mat";

        private MayaImportProfile _profile;
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            MayaModelPostprocessor.Suppress = true;
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "TempSlot");
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            AssetDatabase.CreateAsset(new UnityEngine.Material(shader), MaterialPath);

            _profile = ScriptableObject.CreateInstance<MayaImportProfile>();
            _profile.TargetShader = shader;

            _root = new GameObject("SlotBinderRoot");
            var body = new GameObject("Body");
            body.transform.SetParent(_root.transform);
            body.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(MaterialPath);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_profile);
            AssetDatabase.DeleteAsset(MaterialPath);
            AssetDatabase.DeleteAsset(TempFolder);
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }

            MayaModelPostprocessor.Suppress = false;
        }

        // 既存経路(MayaMaterialImporter)で作った MaterialData を、同じ Material を使う Renderer のスロットに割り当てる。
        private DDrive.Runtime.Material.MaterialData ImportMaterialData()
        {
            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(MaterialPath);
            // sourceKey は「元アセットのファイル名」= ModelSlotBinder が最初に試すキー。
            return MayaMaterialImporter.ImportMaterial(source, "Player", "SlotBinderMat", _profile, new MayaMaterialImporter.Report(), TestRoot);
        }

        [Test]
        public void BuildSlots_AssignsMaterialDataIdFromRendererMaterial()
        {
            var data = ImportMaterialData();
            Assert.IsNotNull(data);

            var report = new ModelSlotBinder.Report();
            var slots = ModelSlotBinder.BuildSlots(_root, null, report, TestRoot);

            Assert.AreEqual(1, slots.Length);
            Assert.AreEqual("Body", slots[0].RendererPath);
            Assert.AreEqual(0, slots[0].SlotIndex);
            Assert.AreEqual(data.Id, slots[0].Material.Value, "生成済み MaterialData の ID がスロットに入る(U-2)");
            Assert.AreEqual(1, report.Bound);
            Assert.AreEqual(0, report.Unresolved);
        }

        [Test]
        public void BuildSlots_KeepsExistingAssignment()
        {
            ImportMaterialData();
            var manual = new AssetId<DDrive.Runtime.Material.MaterialMarker>(999UL, AssetType.Material);
            var existing = new[] { new MaterialSlot { RendererPath = "Body", SlotIndex = 0, Material = manual } };

            var report = new ModelSlotBinder.Report();
            var slots = ModelSlotBinder.BuildSlots(_root, existing, report, TestRoot);

            Assert.AreEqual(999UL, slots[0].Material.Value, "デザイナーが割り当てた ID は上書きしない");
            Assert.AreEqual(1, report.Kept);
            Assert.AreEqual(0, report.Bound);
        }

        [Test]
        public void BuildSlots_NoMaterialData_LeavesSlotUnassignedAndReportsIt()
        {
            var report = new ModelSlotBinder.Report();
            var slots = ModelSlotBinder.BuildSlots(_root, null, report, TestRoot);

            Assert.AreEqual(1, slots.Length);
            Assert.IsFalse(slots[0].Material.IsValid, "対応する MaterialData が無ければ None のまま(例外にしない)");
            Assert.AreEqual(1, report.Unresolved);
        }
    }
}
