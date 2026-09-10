using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [06_material_texture.md] A-2 相互変換(3-6): 共通データ維持 / 固有は表で対応・同名維持・破棄レポート。
    public class MaterialConverterTests
    {
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

        private static Shader Lit() => Shader.Find("Universal Render Pipeline/Lit");
        private static Shader Unlit() => Shader.Find("Universal Render Pipeline/Unlit");

        private MaterialData Source()
        {
            var data = ScriptableObject.CreateInstance<MaterialData>();
            data.DisplayName = "Src";
            data.Shader = Lit();
            data.Common = MaterialCommon.Default;
            data.Common.AlbedoTint = Color.cyan;
            data.RenderQueueOffset = 5;
            data.Specific = new[]
            {
                new ShaderParam { Property = "_BaseColor", Value = ParamValue.Of(Color.red) }, // Unlit にもある → 維持
                new ShaderParam { Property = "_Metallic", Value = ParamValue.Of(0.5f) },       // Unlit に無い → 表で対応 or 破棄
                new ShaderParam { Property = "_OcclusionStrength", Value = ParamValue.Of(1f) }, // 表で意図的に破棄
            };
            _cleanup.Add(data);
            return data;
        }

        private ShaderConversionTable Table()
        {
            var table = ScriptableObject.CreateInstance<ShaderConversionTable>();
            table.Rules = new[]
            {
                new ShaderConversionTable.Rule
                {
                    From = Lit(),
                    To = Unlit(),
                    Mappings = new[]
                    {
                        new ShaderConversionTable.Mapping { FromProperty = "_Metallic", ToProperty = "_Cutoff", Scale = 2f, Offset = -0.25f },
                        new ShaderConversionTable.Mapping { FromProperty = "_OcclusionStrength", ToProperty = string.Empty },
                    },
                },
            };
            _cleanup.Add(table);
            return table;
        }

        [Test]
        public void Convert_WithoutTable_KeepsSameNamed_AndDropsMissing()
        {
            if (Lit() == null || Unlit() == null)
            {
                Assert.Ignore("URP シェーダーが無い環境");
            }

            var result = MaterialConverter.Convert(Source(), Unlit(), null);

            Assert.IsFalse(result.UsedTable);
            Assert.AreEqual(1, result.KeptCount, "_BaseColor は同名・同型で維持");
            Assert.AreEqual(2, result.DroppedCount, "_Metallic / _OcclusionStrength は Unlit に無い");
            Assert.AreEqual(1, result.Specific.Count);
            Assert.AreEqual("_BaseColor", result.Specific[0].Property);
        }

        [Test]
        public void Convert_WithTable_MapsRenames_AppliesScaleOffset_AndDiscardsSilently()
        {
            if (Lit() == null || Unlit() == null)
            {
                Assert.Ignore("URP シェーダーが無い環境");
            }

            var result = MaterialConverter.Convert(Source(), Unlit(), new List<ShaderConversionTable> { Table() });

            Assert.IsTrue(result.UsedTable);
            Assert.AreEqual(1, result.MappedCount);
            Assert.AreEqual(1, result.KeptCount);
            Assert.AreEqual(1, result.DiscardedCount);
            Assert.AreEqual(0, result.DroppedCount, "表に載っていれば警告付きの破棄は出ない");

            var cutoff = result.Specific.Find(p => p.Property == "_Cutoff");
            Assert.AreEqual("_Cutoff", cutoff.Property);
            Assert.AreEqual(0.5f * 2f - 0.25f, cutoff.Value.FloatValue, 1e-5f, "Scale / Offset が掛かる");
        }

        [Test]
        public void ApplyTo_NewData_CopiesCommonAndRender_SetsShaderAndSpecific()
        {
            if (Lit() == null || Unlit() == null)
            {
                Assert.Ignore("URP シェーダーが無い環境");
            }

            var source = Source();
            var result = MaterialConverter.Convert(source, Unlit(), new List<ShaderConversionTable> { Table() });
            var dest = ScriptableObject.CreateInstance<MaterialData>();
            _cleanup.Add(dest);

            MaterialConverter.ApplyTo(dest, source, result);

            Assert.AreSame(Unlit(), dest.Shader);
            Assert.AreEqual(Color.cyan, dest.Common.AlbedoTint, "共通データはそのまま維持");
            Assert.AreEqual(5, dest.RenderQueueOffset);
            Assert.AreEqual(2, dest.Specific.Length);
            Assert.AreSame(Lit(), source.Shader, "変換元は書き換えない");
            Assert.AreEqual(3, source.Specific.Length);
        }

        [Test]
        public void Convert_NullInputs_ReturnEmptyResult()
        {
            var result = MaterialConverter.Convert(null, Unlit(), null);
            Assert.AreEqual(0, result.Entries.Count);
            Assert.AreEqual(0, result.Specific.Count);
        }
    }
}
