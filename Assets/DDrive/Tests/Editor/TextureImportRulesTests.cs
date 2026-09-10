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
