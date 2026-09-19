using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Tests.Editor
{
    // [06_material_texture.md] A-2 — MaterialCommonBinding.ApplyBlend のブレンドステート(2026-09-11 レビュー対応)。
    // DDrive/Lit・DDrive/Unlit は URP 17.3 と同じく `Blend [_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]` と
    // `AlphaToMask [_AlphaToMask]` で書くため、アルファ側を設定しないと半透明が描画先のアルファを潰す(サムネイル / アイコン
    // PNG に穴が開く)。URP の BaseShaderGUI と同じ値になることを固定する。
    public class MaterialCommonBindingTests
    {
        private static readonly int SrcBlendAlpha = Shader.PropertyToID("_SrcBlendAlpha");
        private static readonly int DstBlendAlpha = Shader.PropertyToID("_DstBlendAlpha");
        private static readonly int AlphaToMask = Shader.PropertyToID("_AlphaToMask");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");

        private UnityEngine.Material _material;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("DDrive/Lit");
            if (shader == null)
            {
                Assert.Ignore("シェーダー 'DDrive/Lit' がプロジェクトに無いためスキップ");
            }

            _material = new UnityEngine.Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        [TearDown]
        public void TearDown()
        {
            if (_material != null)
            {
                Object.DestroyImmediate(_material);
            }
        }

        [Test]
        public void ApplyBlend_Transparent_SetsAlphaBlendState()
        {
            MaterialCommonBinding.ApplyBlend(_material, BlendType.Transparent, 0.5f);

            Assert.AreEqual((float)BlendMode.One, _material.GetFloat(SrcBlendAlpha));
            Assert.AreEqual((float)BlendMode.OneMinusSrcAlpha, _material.GetFloat(DstBlendAlpha), "描画先のアルファを潰さない");
            Assert.AreEqual((float)BlendMode.OneMinusSrcAlpha, _material.GetFloat(DstBlend));
            Assert.AreEqual(0f, _material.GetFloat(AlphaToMask), "Transparent では AlphaToMask を切る");
        }

        [Test]
        public void ApplyBlend_Cutout_EnablesAlphaToMask()
        {
            MaterialCommonBinding.ApplyBlend(_material, BlendType.Cutout, 0.5f);

            Assert.AreEqual(1f, _material.GetFloat(AlphaToMask), "Opaque + AlphaClip は AlphaToMask(URP と同じ)");
            Assert.AreEqual((float)BlendMode.One, _material.GetFloat(SrcBlendAlpha));
            Assert.AreEqual((float)BlendMode.Zero, _material.GetFloat(DstBlendAlpha));
            Assert.IsTrue(_material.IsKeywordEnabled("_ALPHATEST_ON"));
        }

        [Test]
        public void ApplyBlend_Opaque_KeepsDestinationAlpha()
        {
            MaterialCommonBinding.ApplyBlend(_material, BlendType.Opaque, 0.5f);

            Assert.AreEqual((float)BlendMode.One, _material.GetFloat(SrcBlendAlpha));
            Assert.AreEqual((float)BlendMode.Zero, _material.GetFloat(DstBlendAlpha));
            Assert.AreEqual(0f, _material.GetFloat(AlphaToMask));
        }
    }
}
