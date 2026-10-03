using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(Material の命名)。E-12(_Toon* が予約名と衝突しない)・E-15(Merge が _Toon* を既定値付きで登録する)。
    // シェーダーは ShaderUtil.CreateShaderAsset でメモリ上に作る(外部パッケージの Toon シェーダーを模す)。
    public class ExternalContractMaterialNamingTests
    {
        private const string ShaderSource = @"
Shader ""Hidden/ExternalContract/ToonNaming""
{
    Properties
    {
        _BaseMap (""Albedo"", 2D) = ""white"" {}
        _BaseColor (""Tint"", Color) = (1, 1, 1, 1)
        _ToonStencilRef (""Stencil Ref"", Float) = 1
        _ToonColor (""Toon Color"", Color) = (0.1, 0.2, 0.3, 1)
        _ToonSteps (""Toon Steps"", Integer) = 4
        _ToonVec (""Toon Vector"", Vector) = (1, 2, 3, 4)
        _ToonTex (""Toon Tex"", 2D) = ""white"" {}
    }
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Vert(float4 p : POSITION) : SV_POSITION { return p; }
            float4 Frag() : SV_Target { return 1; }
            ENDHLSL
        }
    }
}";

        private static readonly string[] ToonNames = { "_ToonStencilRef", "_ToonColor", "_ToonSteps", "_ToonVec", "_ToonTex" };

        private Shader _shader;
        private MaterialData _data;

        [SetUp]
        public void SetUp()
        {
            _shader = ShaderUtil.CreateShaderAsset(ShaderSource);
            Assert.IsNotNull(_shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(_shader), "テスト用シェーダーがコンパイルできない");
            _data = ScriptableObject.CreateInstance<MaterialData>();
            _data.Shader = _shader;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_data);
            Object.DestroyImmediate(_shader);
        }

        [Test]
        public void E12_ToonPrefixedNames_AreSpecific_NotReservedOrCommon()
        {
            foreach (var name in ToonNames)
            {
                Assert.AreEqual(MaterialCommonNaming.Kind.Specific, MaterialCommonNaming.Classify(name), name);
                Assert.IsTrue(MaterialCommonNaming.IsSpecific(name, ShaderPropertyFlags.None), name);
                Assert.IsFalse(MaterialCommonNaming.IsCommon(name), name);
            }
        }

        [Test]
        public void E12_Validator_DoesNotWarnAboutToonProperties()
        {
            var specific = new List<ShaderParam>();
            foreach (var name in ToonNames)
            {
                specific.Add(new ShaderParam { Property = name, Value = ParamValue.Of(1f) });
            }

            _data.Specific = specific.ToArray();
            var results = new MaterialDataValidator().Validate(_data, new ValidationContext(new List<AssetDataBase> { _data })).ToList();
            var toonRelated = results.Where(r => r.Message.Contains("_Toon")).ToList();
            Assert.IsEmpty(toonRelated, string.Join(" / ", toonRelated.Select(r => r.Message)));
            Assert.IsFalse(results.Any(r => r.Message.Contains("上書き")), "共通チャンネル / 描画ステート / 予約名の衝突警告が出ない");
        }

        [Test]
        public void E15_Merge_RegistersToonPropertiesWithShaderDefaults()
        {
            var report = new MaterialSpecificResolver.MergeReport();
            var merged = MaterialSpecificResolver.Merge(null, _shader, report);

            CollectionAssert.AreEquivalent(ToonNames, merged.Select(p => p.Property).ToArray());
            CollectionAssert.AreEquivalent(ToonNames, report.Added);
            Assert.IsEmpty(report.Conflict);

            var color = merged.First(p => p.Property == "_ToonColor");
            Assert.AreEqual(ParamValueType.Color, color.Value.Type);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), color.Value.ColorValue);
            Assert.AreEqual(ParamValueType.Int, merged.First(p => p.Property == "_ToonSteps").Value.Type);
            Assert.AreEqual(4, merged.First(p => p.Property == "_ToonSteps").Value.IntValue);
            Assert.AreEqual(ParamValueType.Vector, merged.First(p => p.Property == "_ToonVec").Value.Type);
            Assert.AreEqual(ParamValueType.Object, merged.First(p => p.Property == "_ToonTex").Value.Type);
        }

        [Test]
        public void E15_Merge_KeepsExistingToonValue_AndDoesNotDuplicate()
        {
            var existing = new[] { new ShaderParam { Property = "_ToonColor", Value = ParamValue.Of(new Color(0.9f, 0.8f, 0.7f, 1f)) } };
            var report = new MaterialSpecificResolver.MergeReport();
            var merged = MaterialSpecificResolver.Merge(existing, _shader, report);

            Assert.AreEqual(1, merged.Count(p => p.Property == "_ToonColor"));
            Assert.AreEqual(new Color(0.9f, 0.8f, 0.7f, 1f), merged.First(p => p.Property == "_ToonColor").Value.ColorValue, "既存の値は上書きしない");
            CollectionAssert.Contains(report.Kept, "_ToonColor");
            Assert.AreEqual(ToonNames.Length, merged.Length);
        }
    }
}
