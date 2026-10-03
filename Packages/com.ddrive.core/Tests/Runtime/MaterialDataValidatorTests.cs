using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // FC-19(2026-10-03、[51_tdrive_integration.md] §4.20): Albedo 未設定 Warning の条件と、
    // 未使用の RenderingLayerMask への Info。警告を減らす方向のみ(新しい Warning は足さない)。
    public class MaterialDataValidatorTests
    {
        private const string AlbedoMessage = "Common.Albedo";

        private readonly List<Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
        }

        private MaterialData Mat(System.Action<MaterialData> configure = null)
        {
            var data = ScriptableObject.CreateInstance<MaterialData>();
            data.Id = 1;
            data.Shader = Shader.Find("Hidden/DDriveTests/PassKeyword"); // _BaseMap を持つ
            Assert.IsNotNull(data.Shader);
            configure?.Invoke(data);
            _cleanup.Add(data);
            return data;
        }

        private static List<ValidationResult> Validate(MaterialData mat)
            => new List<ValidationResult>(new MaterialDataValidator().Validate(mat, new ValidationContext(new List<AssetDataBase> { mat })));

        [Test]
        public void Albedo_Missing_WhiteTint_TexturedShader_StillWarns()
        {
            var results = Validate(Mat());
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains(AlbedoMessage)));
        }

        [Test]
        public void Albedo_Missing_ColoredTint_DoesNotWarn()
        {
            var results = Validate(Mat(d => d.Common.AlbedoTint = Color.red));
            Assert.IsFalse(results.Exists(r => r.Message.Contains(AlbedoMessage)), "色だけのマテリアルでは出さない");
        }

        [Test]
        public void Albedo_Missing_ShaderWithoutAlbedoProperty_DoesNotWarn()
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null || MaterialCommonBinding.IsSupported(shader, MaterialCommonBinding.CommonChannel.Albedo))
            {
                Assert.Ignore("Albedo プロパティを持たないシェーダーが見つからないため省略");
            }

            var results = Validate(Mat(d => d.Shader = shader));
            Assert.IsFalse(results.Exists(r => r.Message.Contains(AlbedoMessage)), "Albedo テクスチャのプロパティが無いシェーダーでは出さない");
        }

        [Test]
        public void RenderingLayerMask_Zero_NoInfo_NonZero_Info()
        {
            Assert.IsFalse(Validate(Mat()).Exists(r => r.Code == "DD-MAT-RENDERINGLAYERMASK-UNUSED"));

            var results = Validate(Mat(d => d.RenderingLayerMask = 4));
            var info = results.Find(r => r.Code == "DD-MAT-RENDERINGLAYERMASK-UNUSED");
            Assert.AreEqual(ValidationSeverity.Info, info.Severity);
            Assert.IsTrue(info.Message.Contains("LightLayerMask"));
        }
    }
}
