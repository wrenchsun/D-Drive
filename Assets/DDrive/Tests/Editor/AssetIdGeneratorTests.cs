using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DDrive.Editor.Codegen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    public class AssetIdGeneratorTests
    {
        private const string TempDir = "Assets/DDrive/Tests/Editor/Temp";
        private readonly List<string> _createdAssetPaths = new();
        private string _tempOutputPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
            }

            _tempOutputPath = Path.Combine(Path.GetTempPath(), "ddrive_test_ids_" + Guid.NewGuid().ToString("N") + ".cs");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var path in _createdAssetPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<TestAssetData>(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            _createdAssetPaths.Clear();

            if (File.Exists(_tempOutputPath))
            {
                File.Delete(_tempOutputPath);
            }
        }

        private TestAssetData CreateAsset(string name)
        {
            var data = ScriptableObject.CreateInstance<TestAssetData>();
            data.DisplayName = name;
            var path = $"{TempDir}/{name}.asset";
            AssetDatabase.CreateAsset(data, path);
            _createdAssetPaths.Add(path);
            return data;
        }

        [Test]
        public void Regenerate_AssignsStableIdAndIsIdempotent()
        {
            var asset = CreateAsset("SE_Gen_Idempotent");
            AssetDatabase.SaveAssets();

            var first = AssetIdGenerator.Regenerate(_tempOutputPath);
            Assert.IsTrue(first.Success);
            // 他のテスト(AssetIdLookupTests 等)も同じ TestAssetData 型の一時アセットを作ることがあるため、
            // プロジェクト内で自分だけが対象という前提を置かない(>=1 のみ検証)。
            Assert.GreaterOrEqual(first.AssignedCount, 1);
            Assert.AreNotEqual(0UL, asset.Id);
            var idAfterFirst = asset.Id;
            var firstContent = File.ReadAllText(_tempOutputPath);

            var second = AssetIdGenerator.Regenerate(_tempOutputPath);
            Assert.IsTrue(second.Success);
            Assert.AreEqual(0, second.AssignedCount);
            Assert.AreEqual(idAfterFirst, asset.Id);
            var secondContent = File.ReadAllText(_tempOutputPath);
            Assert.AreEqual(firstContent, secondContent);
        }

        [Test]
        public void Regenerate_IdSurvivesRename()
        {
            var asset = CreateAsset("SE_Gen_RenameMe");
            AssetDatabase.SaveAssets();
            AssetIdGenerator.Regenerate(_tempOutputPath);
            var idBefore = asset.Id;
            Assert.AreNotEqual(0UL, idBefore);

            var error = AssetDatabase.RenameAsset(_createdAssetPaths[0], "SE_Gen_Renamed");
            Assert.IsTrue(string.IsNullOrEmpty(error), error);
            _createdAssetPaths[0] = TempDir + "/SE_Gen_Renamed.asset";

            AssetIdGenerator.Regenerate(_tempOutputPath);
            Assert.AreEqual(idBefore, asset.Id);
        }

        [Test]
        public void Regenerate_DetectsDuplicateIds()
        {
            var a = CreateAsset("SE_Gen_DupA");
            var b = CreateAsset("SE_Gen_DupB");
            AssetDatabase.SaveAssets();

            AssetIdGenerator.Regenerate(_tempOutputPath);
            Assert.AreNotEqual(a.Id, b.Id);

            b.Id = a.Id;
            EditorUtility.SetDirty(b);
            AssetDatabase.SaveAssets();

            LogAssert.Expect(LogType.Error, new Regex(".*Duplicate AssetId.*"));
            var result = AssetIdGenerator.Regenerate(_tempOutputPath);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Duplicates.Count);
            Assert.AreEqual(a.Id, result.Duplicates[0].Id);
        }
    }
}
