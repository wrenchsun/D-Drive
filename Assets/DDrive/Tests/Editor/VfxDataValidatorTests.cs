using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // Prefab のマテリアル/シェーダーを介した検査(TargetProperty 存在チェック)を行うため、
    // AssetDatabase/シェーダーの実体が要る EditMode(Editor asmdef)側に置く。
    public class VfxDataValidatorTests
    {
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("VfxValidatorTestPrefab");
            var renderer = _prefab.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            renderer.sharedMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_prefab);
        }

        private VfxData ValidVfx()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Prefab = _prefab;
            data.LifeMode = VfxLifeMode.OneShot;
            data.FadeOutSec = 0.2f;
            data.Render = VfxRenderMode.World3D;
            return data;
        }

        private static List<ValidationResult> Validate(VfxData data)
            => new VfxDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();

        [Test]
        public void ValidVfxData_HasNoErrors()
        {
            var results = Validate(ValidVfx());
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void MissingPrefab_IsError()
        {
            var data = ValidVfx();
            data.Prefab = null;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Prefab")));
        }

        [Test]
        public void LoopWithoutPoolLimit_IsWarning()
        {
            var data = ValidVfx();
            data.LifeMode = VfxLifeMode.Loop;
            data.Flags = new AssetFlags { Pool = PoolPolicy.None };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Pool")));
        }

        [Test]
        public void LoopWithPoolLimit_NoWarning()
        {
            var data = ValidVfx();
            data.LifeMode = VfxLifeMode.Loop;
            data.Flags = new AssetFlags { Pool = PoolPolicy.Pooled(4, 16) };
            var results = Validate(data);
            Assert.IsFalse(results.Exists(r => r.Message.Contains("Pool")));
        }

        [Test]
        public void UiOverlayWithGame3DDomain_IsWarning()
        {
            var data = ValidVfx();
            data.Render = VfxRenderMode.UIOverlay;
            data.Flags = new AssetFlags { Domain = AssetDomain.Game3D };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("UIOverlay")));
        }

        [Test]
        public void FadeOutOver10Seconds_IsWarning()
        {
            var data = ValidVfx();
            data.FadeOutSec = 15f;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("FadeOutSec")));
        }

        [Test]
        public void ParamTargetProperty_NotOnPrefabMaterial_IsError()
        {
            var data = ValidVfx();
            data.Params = new[]
            {
                new VfxParam { Label = "Ghost", Type = VfxParamType.Float, TargetProperty = "_DoesNotExistProperty" },
            };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Ghost")));
        }

        [Test]
        public void ParamTargetProperty_ExistsOnPrefabMaterial_NoError()
        {
            var data = ValidVfx();
            data.Params = new[]
            {
                new VfxParam { Label = "BaseColor", Type = VfxParamType.Color, TargetProperty = "_BaseColor" },
            };
            var results = Validate(data);
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("BaseColor")));
        }

        [Test]
        public void BuiltInShaderMaterial_IsError()
        {
            var data = ValidVfx();
            var renderer = _prefab.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard"));

            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error &&
                                               r.Message.Contains("Built-in") && r.Message.Contains("URP")));
        }

        [Test]
        public void UrpShaderMaterial_NoShaderPipelineError()
        {
            var results = Validate(ValidVfx());
            Assert.IsFalse(results.Exists(r => r.Message.Contains("Built-in") && r.Message.Contains("URP")));
        }
    }
}
