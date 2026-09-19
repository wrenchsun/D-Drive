using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class ModelDataValidatorTests
    {
        private GameObject _prefab;
        private GameObject _bodyChild;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("ModelValidatorTestPrefab");
            _bodyChild = new GameObject("Body");
            _bodyChild.transform.SetParent(_prefab.transform);
            _bodyChild.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_prefab);
        }

        private ModelData ValidModel()
        {
            var data = ScriptableObject.CreateInstance<ModelData>();
            data.Prefab = _prefab;
            return data;
        }

        private static List<ValidationResult> Validate(ModelData data)
            => new ModelDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();

        [Test]
        public void ValidModelData_HasNoErrors()
        {
            var results = Validate(ValidModel());
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void MissingPrefab_IsError()
        {
            var data = ValidModel();
            data.Prefab = null;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Prefab")));
        }

        [Test]
        public void AnimatorWithoutAvatar_IsError()
        {
            var data = ValidModel();
            _prefab.AddComponent<Animator>();
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Avatar")));
        }

        [Test]
        public void NoAnimator_MissingAvatar_IsNotFlagged()
        {
            var data = ValidModel();
            var results = Validate(data);
            Assert.IsFalse(results.Exists(r => r.Message.Contains("Avatar")));
        }

        [Test]
        public void SlotRendererPath_NotFound_IsError()
        {
            var data = ValidModel();
            data.Slots = new[] { new MaterialSlot { RendererPath = "DoesNotExist", SlotIndex = 0 } };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("RendererPath")));
        }

        [Test]
        public void SlotRendererPath_Found_NoError()
        {
            var data = ValidModel();
            data.Slots = new[]
            {
                new MaterialSlot
                {
                    RendererPath = "Body",
                    SlotIndex = 0,
                    Material = new AssetId<MaterialMarker>(1UL, AssetType.Material),
                },
            };
            var results = Validate(data);
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void SlotWithoutMaterial_IsWarning()
        {
            var data = ValidModel();
            data.Slots = new[] { new MaterialSlot { RendererPath = "Body", SlotIndex = 0 } };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Material")));
        }

        [Test]
        public void BuiltInShaderMaterial_IsError()
        {
            var data = ValidModel();
            var renderer = _bodyChild.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard"));

            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error &&
                                               r.Message.Contains("Built-in") && r.Message.Contains("URP")));
        }
    }
}
