using System.IO;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Materials;
using DDrive.Foundation.Data;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [06_material_texture.md] A-2 Maya FBX 自動生成(3-7): Unity Material → MaterialData + TextureData、再インポートで固有調整を保持。
    public class MayaMaterialImporterTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempGameData";
        private const string TempFolder = "Assets/DDrive/Tests/Editor/Temp";
        private const string TexturePath = TempFolder + "/T_MayaTest_N.png";
        private const string MaterialPath = TempFolder + "/MayaTestMat.mat";

        private MayaImportProfile _profile;

        [SetUp]
        public void SetUp()
        {
            MayaModelPostprocessor.Suppress = true;
            TexturePostprocessorSuppress(true);
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
            }

            var png = new Texture2D(4, 4);
            File.WriteAllBytes(TexturePath, png.EncodeToPNG());
            Object.DestroyImmediate(png);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new UnityEngine.Material(shader) { name = "Body" };
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            material.SetTexture(material.HasProperty("_BumpMap") ? "_BumpMap" : "_MainTex", tex);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.red);
            }

            AssetDatabase.CreateAsset(material, MaterialPath);

            _profile = ScriptableObject.CreateInstance<MayaImportProfile>();
            _profile.TargetShader = shader;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_profile);
            AssetDatabase.DeleteAsset(MaterialPath);
            AssetDatabase.DeleteAsset(TexturePath);
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                AssetDatabase.SaveAssets();
            }

            MayaModelPostprocessor.Suppress = false;
            TexturePostprocessorSuppress(false);
        }

        // TexturePostprocessor(3-8)があれば抑止する(無くてもテストは成立する)。
        private static void TexturePostprocessorSuppress(bool on)
        {
            var type = System.Type.GetType("DDrive.Editor.Materials.TexturePostprocessor, DDrive.Editor");
            var field = type?.GetField("Suppress");
            field?.SetValue(null, on);
        }

        [Test]
        public void ImportMaterial_CreatesMaterialData_AndTextureData_WithIds()
        {
            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(MaterialPath);
            var report = new MayaMaterialImporter.Report();

            var created = MayaMaterialImporter.ImportMaterial(source, "Player", "Body", _profile, report, TestRoot);

            Assert.IsNotNull(created);
            Assert.AreNotEqual(0UL, created.Id);
            Assert.AreEqual("Player", created.Category);
            Assert.AreEqual($"Body/{AssetDatabase.AssetPathToGUID(MaterialPath)}/MayaTestMat", created.SourceMaterial, "FBX名/元アセット GUID/マテリアル名で同定する(2026-09-11 レビュー対応 I2)");
            Assert.AreEqual(1, report.Created);
            Assert.AreEqual(1, report.TexturesCreated, "参照テクスチャの TextureData が作られる");
            Assert.IsTrue(created.Common.Normal.IsValid || created.Common.Albedo.IsValid, "テクスチャがチャンネルに載る");
            if (source.HasProperty("_BaseColor"))
            {
                Assert.AreEqual(Color.red, created.Common.AlbedoTint);
            }

            StringAssert.Contains("Material/Player/", AssetDatabase.GetAssetPath(created));
        }

        [Test]
        public void ImportMaterial_Reimport_UpdatesCommonOnly_AndKeepsSpecific()
        {
            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(MaterialPath);
            var first = MayaMaterialImporter.ImportMaterial(source, "Player", "Body", _profile, new MayaMaterialImporter.Report(), TestRoot);
            Assert.IsNotNull(first);

            // 固有調整を入れてから、DCC 側の変更(色)を再インポート。
            first.Specific = new[] { new ShaderParam { Property = "_Metallic", Value = ParamValue.Of(0.9f) } };
            first.RenderQueueOffset = 7;
            EditorUtility.SetDirty(first);
            if (source.HasProperty("_BaseColor"))
            {
                source.SetColor("_BaseColor", Color.blue);
            }
            else
            {
                source.color = Color.blue;
            }

            var report = new MayaMaterialImporter.Report();
            var second = MayaMaterialImporter.ImportMaterial(source, "Player", "Body", _profile, report, TestRoot);

            Assert.AreSame(first, second, "同じ FBX / マテリアル名は既存を更新する(重複生成しない)");
            Assert.AreEqual(1, report.Updated);
            Assert.AreEqual(0, report.Created);
            Assert.AreEqual(Color.blue, second.Common.AlbedoTint, "Common は更新される");
            Assert.AreEqual(1, second.Specific.Length, "Specific は保持");
            Assert.AreEqual(0.9f, second.Specific[0].Value.FloatValue, 1e-5f);
            Assert.AreEqual(7, second.RenderQueueOffset, "Render 設定は保持");

            var again = MayaMaterialImporter.ImportMaterial(source, "Player", "Body", _profile, report, TestRoot);
            Assert.AreSame(first, again);
            Assert.AreEqual(1, report.Unchanged, "差分が無ければ変更なし");
        }

        [Test]
        public void Profile_ResolveCategory_UsesParentFolder()
        {
            var profile = ScriptableObject.CreateInstance<MayaImportProfile>();
            try
            {
                Assert.AreEqual("Player", profile.ResolveCategory("Assets/SourceAssets/Player/Body.fbx"));
                Assert.AreEqual(profile.FixedCategory, profile.ResolveCategory("Assets/SourceAssets/Body.fbx"));
                profile.CategoryFromFolder = false;
                profile.FixedCategory = "Fixed";
                Assert.AreEqual("Fixed", profile.ResolveCategory("Assets/SourceAssets/Player/Body.fbx"));
                Assert.IsTrue(profile.AppliesTo("Assets/SourceAssets/Player/Body.fbx"));
                Assert.IsFalse(profile.AppliesTo("Assets/DDrive/Tests/Editor/Temp/x.fbx"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
