using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.Materials;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [06_material_texture.md] B-3/B-4 — TextureImportProfile / TexturePostprocessor / TextureDataValidator(チケット 3-8)。
    public class TextureImportRulesTests
    {
        private const string TempDir = "Assets/DDrive/Tests/Editor/Temp";
        private readonly List<string> _createdAssetPaths = new();
        private readonly List<Object> _createdObjects = new();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _createdObjects)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }

            _createdObjects.Clear();

            foreach (var path in _createdAssetPaths)
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            _createdAssetPaths.Clear();
        }

        // ---- Profile matching ----

        [Test]
        public void TryMatch_NormalSuffix_MatchesNormalMapRule()
        {
            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsTrue(profile.TryMatch("Assets/SourceAssets/T_Body_N.png", out var rule));
            Assert.AreEqual("NormalMap", rule.Name);
        }

        [Test]
        public void TryMatch_UiSuffix_MatchesUiSpriteRule()
        {
            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsTrue(profile.TryMatch("Assets/SourceAssets/Icon_UI.png", out var rule));
            Assert.AreEqual("UI Sprite", rule.Name);
        }

        [Test]
        public void TryMatch_TPrefix_MatchesModelDefaultRule()
        {
            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsTrue(profile.TryMatch("Assets/SourceAssets/T_Body.png", out var rule));
            Assert.AreEqual("Model default", rule.Name);
        }

        // ---- Substance Painter の標準エクスポート名(2026-09-11) ----

        [TestCase("Assets/SourceAssets/Chara_Body_Normal.png", "Substance _Normal", TextureImporterType.NormalMap, false, TextureChannel.Normal, false)]
        [TestCase("Assets/SourceAssets/Chara_Body_Normal_DirectX.png", "Substance _Normal_DirectX", TextureImporterType.NormalMap, false, TextureChannel.Normal, true)]
        [TestCase("Assets/SourceAssets/Chara_Body_Normal_OpenGL.png", "Substance _Normal_OpenGL", TextureImporterType.NormalMap, false, TextureChannel.Normal, false)]
        [TestCase("Assets/SourceAssets/Chara_Body_MaskMap.png", "Substance _MaskMap", TextureImporterType.Default, false, TextureChannel.Mask, false)]
        [TestCase("Assets/SourceAssets/Chara_Body_MetallicSmoothness.png", "Substance _MetallicSmoothness", TextureImporterType.Default, false, TextureChannel.Mask, false)]
        [TestCase("Assets/SourceAssets/Chara_Body_Roughness.png", "Substance _Roughness", TextureImporterType.Default, false, TextureChannel.Other, false)]
        [TestCase("Assets/SourceAssets/Chara_Body_BaseMap.png", "Substance _BaseMap", TextureImporterType.Default, true, TextureChannel.Albedo, false)]
        [TestCase("Assets/SourceAssets/Chara_Body_AlbedoTransparency.png", "Substance _AlbedoTransparency", TextureImporterType.Default, true, TextureChannel.Albedo, false)]
        [TestCase("Assets/SourceAssets/Chara_Body_Emissive.png", "Substance _Emissive", TextureImporterType.Default, true, TextureChannel.Emission, false)]
        [TestCase("Assets/SourceAssets/chara_body_basecolor.png", "Substance _BaseColor", TextureImporterType.Default, true, TextureChannel.Albedo, false)]
        public void TryMatch_SubstancePainterNames(string path, string ruleName, TextureImporterType type, bool srgb, TextureChannel channel, bool flipGreen)
        {
            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsTrue(profile.TryMatch(path, out var rule), path);
            Assert.AreEqual(ruleName, rule.Name);
            Assert.AreEqual(type, rule.Type);
            Assert.AreEqual(srgb, rule.SRgb);
            Assert.AreEqual(channel, rule.Channel);
            Assert.AreEqual(flipGreen, rule.FlipGreenChannel);
        }

        [Test]
        public void TryMatch_ShortSuffix_StillWins_ForDDriveNames()
        {
            // 短縮規約(_N)は Substance の _Normal と競合しない
            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsTrue(profile.TryMatch("Assets/SourceAssets/T_Body_N.png", out var rule));
            Assert.AreEqual("NormalMap", rule.Name);
        }

        [Test]
        public void Apply_DirectXNormal_FlipsGreenChannel()
        {
            var path = TempDir + "/Apply_Normal_DirectX.png";
            var png = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            File.WriteAllBytes(path, png.EncodeToPNG());
            Object.DestroyImmediate(png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            _createdAssetPaths.Add(path);

            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsTrue(profile.TryMatch(path, out var rule));
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);

            Assert.IsTrue(TextureImportProfile.Apply(importer, rule));
            importer.SaveAndReimport();
            importer = (TextureImporter)AssetImporter.GetAtPath(path);

            Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType);
            Assert.IsTrue(importer.flipGreenChannel, "DirectX 形式は緑反転");
            Assert.IsEmpty(TextureImportProfile.Diff(importer, rule), "適用後は規約と一致");
        }

        [Test]
        public void TryMatch_NoRuleMatches_ReturnsFalse()
        {
            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsFalse(profile.TryMatch("Assets/SourceAssets/Foo.png", out _));
        }

        [Test]
        public void AppliesTo_ExcludesDDriveAndTestsPaths_IncludesSourceAssets()
        {
            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsFalse(profile.AppliesTo("Assets/DDrive/Runtime/Something/T_Foo.png"));
            Assert.IsFalse(profile.AppliesTo("Assets/DDrive/Tests/Editor/Temp/T_Foo.png"));
            Assert.IsTrue(profile.AppliesTo("Assets/SourceAssets/x.png"));
        }

        // ---- Apply / Diff end-to-end on a real imported texture ----

        private string ImportTempPng(string fileName)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    tex.SetPixel(x, y, Color.white);
                }
            }

            tex.Apply();
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            var path = $"{TempDir}/{fileName}";
            File.WriteAllBytes(path, bytes);
            _createdAssetPaths.Add(path);

            TexturePostprocessor.Suppress = true; // Tests/ は AppliesTo 対象外だが、念のため通常経路を止める
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TexturePostprocessor.Suppress = false;

            return path;
        }

        [Test]
        public void Apply_NormalMapRule_MakesImporterMatch_AndDiffBecomesEmpty()
        {
            var path = ImportTempPng("T_ApplyTest_N.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsTrue(profile.TryMatch(path, out var rule));
            Assert.AreEqual("NormalMap", rule.Name);

            var diffsBefore = TextureImportProfile.Diff(importer, rule);
            Assert.Greater(diffsBefore.Count, 0);

            var changed = TextureImportProfile.Apply(importer, rule);
            Assert.IsTrue(changed);
            importer.SaveAndReimport();

            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType);
            var diffsAfter = TextureImportProfile.Diff(importer, rule);
            CollectionAssert.IsEmpty(diffsAfter);
        }

        // ---- Validator ----

        private TextureData CreateTextureData(Texture2D texture, TextureChannel channel)
        {
            var data = ScriptableObject.CreateInstance<TextureData>();
            data.Texture = texture;
            data.Channel = channel;
            data.Usage = TextureUsage.Model;
            _createdObjects.Add(data);
            return data;
        }

        private static List<ValidationResult> Validate(TextureData data)
            => new TextureDataValidator().Validate(data, new ValidationContext(new List<DDrive.Foundation.Data.AssetDataBase> { data })).ToList();

        [Test]
        public void MissingTexture_IsError()
        {
            var data = CreateTextureData(null, TextureChannel.Albedo);
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Texture")));
        }

        [Test]
        public void NormalChannel_WithoutNormalMapImporter_IsError_AndFixActionResolvesIt()
        {
            var path = ImportTempPng("T_ValidatorTest_N.png");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var data = CreateTextureData(texture, TextureChannel.Normal);

            var results = Validate(data);
            var error = results.Find(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("NormalMap"));
            Assert.IsNotNull(error.FixAction, "Channel=Normal の未設定エラーが見つかりませんでした");

            error.FixAction.Invoke();

            var resultsAfter = Validate(data);
            Assert.IsFalse(resultsAfter.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("NormalMap")));
        }

        [Test]
        public void UiUsage_WithoutSprite_IsError()
        {
            var path = ImportTempPng("T_ValidatorUiTest.png");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var data = CreateTextureData(texture, TextureChannel.Other);
            data.Usage = TextureUsage.UI;

            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Sprite")));
        }
    }
}
