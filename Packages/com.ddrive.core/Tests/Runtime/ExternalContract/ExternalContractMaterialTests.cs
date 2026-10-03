using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(Material)。E-10 / E-11(doc17 §1・§3 #1)。
    // 外部パッケージの Toon シェーダー(_Toon*)の固有パラメータは、D-Drive が名前を絞らずに実行時 Material へそのまま書く。
    public class ExternalContractMaterialTests
    {
        private MaterialManager _manager;
        private GameObject _target;
        private Renderer _renderer;
        private Texture2D _texture;
        private readonly List<Object> _cleanup = new();

        [SetUp]
        public void SetUp()
        {
            _manager = new MaterialManager(new AssetRegistry(new ExternalContractLoader()));
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _renderer = _target.GetComponent<Renderer>();
            _texture = new Texture2D(2, 2);
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Clear();
            Object.DestroyImmediate(_target);
            Object.DestroyImmediate(_texture);
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
        }

        private UnityEngine.Material Build(params ShaderParam[] specific)
        {
            var shader = Shader.Find("Hidden/ExternalContract/ToonProps");
            Assert.IsNotNull(shader, "テスト用シェーダー(Tests/Runtime/ExternalContract/Shaders)が見つからない");
            var data = ScriptableObject.CreateInstance<MaterialData>();
            _cleanup.Add(data);
            data.Id = 1;
            data.DisplayName = "ExternalContractToon";
            data.Shader = shader;
            data.Common = MaterialCommon.Default;
            data.Specific = specific;
            _manager.ApplyData(_renderer, 0, data);
            return _manager.GetData(data);
        }

        [Test]
        public void E10_UnknownPrefixSpecific_IsWrittenToRuntimeMaterial_AsIs()
        {
            var material = Build(
                new ShaderParam { Property = "_ToonFloat", Value = ParamValue.Of(0.75f) },
                new ShaderParam { Property = "_ToonInt", Value = ParamValue.Of(7) },
                new ShaderParam { Property = "_ToonFlag", Value = ParamValue.Of(true) },
                new ShaderParam { Property = "_ToonColor", Value = ParamValue.Of(new Color(0.5f, 0.25f, 0.125f, 1f)) },
                new ShaderParam { Property = "_ToonVec", Value = new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(9f, 8f, 7f, 6f) } },
                new ShaderParam { Property = "_ToonTex", Value = new ParamValue { Type = ParamValueType.Object, ObjectValue = _texture } });

            Assert.AreEqual(0.75f, material.GetFloat("_ToonFloat"), 1e-5f);
            Assert.AreEqual(7, material.GetInteger("_ToonInt"));
            Assert.AreEqual(1f, material.GetFloat("_ToonFlag"), "Bool は 0/1 の Float");
            var c = material.GetColor("_ToonColor");
            Assert.AreEqual(0.5f, c.r, 1e-5f);
            Assert.AreEqual(0.25f, c.g, 1e-5f);
            Assert.AreEqual(0.125f, c.b, 1e-5f);
            var v = material.GetVector("_ToonVec");
            Assert.AreEqual(9f, v.x, 1e-5f);
            Assert.AreEqual(6f, v.w, 1e-5f);
            Assert.AreSame(_texture, material.GetTexture("_ToonTex"));
        }

        [Test]
        public void E10_UnspecifiedToonProperty_KeepsShaderDefault()
        {
            var material = Build(new ShaderParam { Property = "_ToonFloat", Value = ParamValue.Of(0.5f) });
            Assert.AreEqual(0.5f, material.GetFloat("_ToonFloat"), 1e-5f);
            Assert.AreEqual(3, material.GetInteger("_ToonInt"), "Specific に無いプロパティはシェーダー既定値のまま");
        }

        [Test]
        public void E11_PropertyNotInShader_IsSkipped_WithoutExceptionOrLog_AndOthersStillApply()
        {
            UnityEngine.Material material = null;
            Assert.DoesNotThrow(() => material = Build(
                new ShaderParam { Property = "_ToonNone", Value = ParamValue.Of(5f) },
                new ShaderParam { Property = string.Empty, Value = ParamValue.Of(5f) },
                new ShaderParam { Property = "_ToonFloat", Value = ParamValue.Of(0.5f) }));
            Assert.IsFalse(material.HasProperty("_ToonNone"));
            Assert.AreEqual(0.5f, material.GetFloat("_ToonFloat"), 1e-5f, "存在しない名前の後ろの項目も適用される");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
