using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Materials;
using DDrive.Foundation.Data;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [06_material_texture.md] A-2 — Unity 標準シェーダーの Material → DDrive/Lit・Unlit の MaterialData(2026-09-11)。
    public class UnityMaterialMigratorTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempGameData";
        private const string TempFolder = TestTempFolder.Root + "/Temp";
        private const string LitMaterialPath = TempFolder + "/MigrateLit.mat";
        private const string UnlitMaterialPath = TempFolder + "/MigrateUnlit.mat";

        private readonly System.Collections.Generic.List<string> _subFolders = new();

        [SetUp]
        public void SetUp()
        {
            MayaModelPostprocessor.Suppress = true;
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                TestTempFolder.CreateFolder("Temp");
            }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(LitMaterialPath);
            AssetDatabase.DeleteAsset(UnlitMaterialPath);
            foreach (var folder in _subFolders)
            {
                AssetDatabase.DeleteAsset(folder);
            }

            _subFolders.Clear();
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }

            MayaModelPostprocessor.Suppress = false;
        }

        [Test]
        public void ResolveTargetShader_MapsUrpAndBuiltin()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Assume.That(lit != null && unlit != null, "URP のシェーダーが必要");
            Assume.That(Shader.Find(UnityMaterialMigrator.LitShaderName) != null, "DDrive/Lit が必要(Packages/com.ddrive.core/Runtime/Shaders)");

            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, UnityMaterialMigrator.ResolveTargetShader(lit).name);
            Assert.AreEqual(UnityMaterialMigrator.UnlitShaderName, UnityMaterialMigrator.ResolveTargetShader(unlit).name);
            Assert.IsNull(UnityMaterialMigrator.ResolveTargetShader(Shader.Find("Hidden/InternalErrorShader")), "未対応は null");
            Assert.IsTrue(UnityMaterialMigrator.IsSupported(lit));
        }

        [Test]
        public void Migrate_UrpLit_CreatesDDriveLitData_WithCommonAndSpecific()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Assume.That(lit != null && Shader.Find(UnityMaterialMigrator.LitShaderName) != null);

            var material = new UnityEngine.Material(lit) { name = "MigrateLit" };
            material.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.6f, 1f));
            material.SetFloat("_Metallic", 0.75f);
            material.SetFloat("_Smoothness", 0.25f);
            material.SetFloat("_OcclusionStrength", 0.4f);
            AssetDatabase.CreateAsset(material, LitMaterialPath);

            var report = new MayaMaterialImporter.Report();
            var data = UnityMaterialMigrator.Migrate(material, "Migrate", report, TestRoot);

            Assert.IsNotNull(data, report.ToString());
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, data.Shader.name);
            AssertColor(new Color(0.2f, 0.4f, 0.6f, 1f), data.Common.AlbedoTint);
            Assert.AreEqual(0.75f, data.Common.Metallic, 0.001f);
            Assert.AreEqual(0.25f, data.Common.Smoothness, 0.001f);
            // 同定キーは「SourceKey / 元 Material の GUID / 名前」(同名 Material の取り違え防止。2026-09-11)
            Assert.AreEqual($"{UnityMaterialMigrator.SourceKey}/{AssetDatabase.AssetPathToGUID(LitMaterialPath)}/MigrateLit", data.SourceMaterial);

            var occlusion = System.Array.Find(data.Specific, p => p.Property == "_OcclusionStrength");
            Assert.AreEqual("_OcclusionStrength", occlusion.Property, "DDrive/Lit の固有が登録される");
            Assert.AreEqual(0.4f, occlusion.Value.FloatValue, 0.001f, "元 Material の値を引き継ぐ");

            // 再実行: 同じ Data を更新し、増えない
            var again = UnityMaterialMigrator.Migrate(material, "Migrate", report, TestRoot);
            Assert.AreSame(data, again);
        }

        [Test]
        public void Migrate_UrpUnlit_CreatesDDriveUnlitData()
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Assume.That(unlit != null && Shader.Find(UnityMaterialMigrator.UnlitShaderName) != null);

            var material = new UnityEngine.Material(unlit) { name = "MigrateUnlit" };
            material.SetColor("_BaseColor", Color.green);
            AssetDatabase.CreateAsset(material, UnlitMaterialPath);

            var data = UnityMaterialMigrator.Migrate(material, "Migrate", null, TestRoot);

            Assert.IsNotNull(data);
            Assert.AreEqual(UnityMaterialMigrator.UnlitShaderName, data.Shader.name);
            AssertColor(Color.green, data.Common.AlbedoTint);
            var expected = MaterialSpecificResolver.Resolve(data.Shader).Count;
            Assert.AreEqual(expected, data.Specific?.Length ?? 0, "変換先シェーダーの固有が既定値で登録される");
        }

        // 同名の Material が別フォルダにあっても 1 つの MaterialData を奪い合わない(2026-09-11 レビュー対応)。
        [Test]
        public void Migrate_SameNameInDifferentFolders_CreatesTwoData()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Assume.That(lit != null && Shader.Find(UnityMaterialMigrator.LitShaderName) != null);

            var pathA = CreateSubFolderMaterial("FolderA", lit);
            var pathB = CreateSubFolderMaterial("FolderB", lit);
            var report = new MayaMaterialImporter.Report();

            var a = UnityMaterialMigrator.Migrate(AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(pathA), "Migrate", report, TestRoot);
            var b = UnityMaterialMigrator.Migrate(AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(pathB), "Migrate", report, TestRoot);

            Assert.IsNotNull(a, report.ToString());
            Assert.IsNotNull(b, report.ToString());
            Assert.AreNotSame(a, b, "フォルダ違いの同名 Material はそれぞれの MaterialData になる");
            Assert.AreNotEqual(a.SourceMaterial, b.SourceMaterial);
            Assert.AreEqual(2, report.Created);
        }

        // 未対応シェーダーは Lit に倒し、その旨をレポートに残す。
        [Test]
        public void Migrate_UnsupportedShader_FallsBackToLit_AndReports()
        {
            var unsupported = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/InternalErrorShader");
            Assume.That(unsupported != null && Shader.Find(UnityMaterialMigrator.LitShaderName) != null);
            Assume.That(!UnityMaterialMigrator.IsSupported(unsupported), "変換表に無いシェーダーであること");

            var material = new UnityEngine.Material(unsupported) { name = "MigrateLit" };
            AssetDatabase.CreateAsset(material, LitMaterialPath);

            var report = new MayaMaterialImporter.Report();
            var data = UnityMaterialMigrator.Migrate(material, "Migrate", report, TestRoot);

            Assert.IsNotNull(data, report.ToString());
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, data.Shader.name, "未対応は Lit に倒す");
            StringAssert.Contains("未対応", string.Join("\n", report.Lines));
        }

        private string CreateSubFolderMaterial(string folderName, Shader shader)
        {
            var folder = TempFolder + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(TempFolder, folderName);
            }

            _subFolders.Add(folder);
            var path = folder + "/SameName.mat";
            AssetDatabase.CreateAsset(new UnityEngine.Material(shader) { name = "SameName" }, path);
            return path;
        }

        // Color の == は成分の float 誤差(sRGB/linear 変換等)で落ちるので許容誤差付きで比べる。
        private static void AssertColor(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 0.002f, "r");
            Assert.AreEqual(expected.g, actual.g, 0.002f, "g");
            Assert.AreEqual(expected.b, actual.b, 0.002f, "b");
            Assert.AreEqual(expected.a, actual.a, 0.002f, "a");
        }

        [Test]
        public void CollectFromSelection_TakesMaterialsAndRendererMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new UnityEngine.Material(lit) { name = "MigrateLit" };
            AssetDatabase.CreateAsset(material, LitMaterialPath);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.GetComponent<Renderer>().sharedMaterial = material;
            try
            {
                var list = UnityMaterialMigrator.CollectFromSelection(new Object[] { material, go, null });
                Assert.AreEqual(1, list.Count, "重複なし・アセット化された Material だけ");
                Assert.AreSame(material, list[0]);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
