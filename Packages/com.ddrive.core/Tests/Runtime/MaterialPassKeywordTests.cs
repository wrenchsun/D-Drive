using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // FC-11(2026-10-03): MaterialData.DisabledPasses / EnabledKeywords。
    public class MaterialPassKeywordTests
    {
        private const string Keyword = "_DDRIVE_TEST_FEATURE";

        private MaterialManager _manager;
        private GameObject _target;
        private Renderer _renderer;
        private readonly List<Object> _cleanup = new();

        [SetUp]
        public void SetUp()
        {
            _manager = new MaterialManager(new AssetRegistry(new FakeAssetLoader()));
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _renderer = _target.GetComponent<Renderer>();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Clear();
            Object.DestroyImmediate(_target);
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
        }

        private static Shader TestShader()
        {
            var shader = Shader.Find("Hidden/DDriveTests/PassKeyword");
            Assert.IsNotNull(shader, "テスト用シェーダー(Tests/Runtime/Shaders)が見つからない");
            return shader;
        }

        private MaterialData Mat(ulong id, System.Action<MaterialData> configure = null)
        {
            var data = ScriptableObject.CreateInstance<MaterialData>();
            data.Id = id;
            data.DisplayName = $"PK{id}";
            data.Shader = TestShader();
            data.Common = MaterialCommon.Default;
            configure?.Invoke(data);
            _cleanup.Add(data);
            return data;
        }

        private UnityEngine.Material Built(MaterialData data)
        {
            _manager.ApplyData(_renderer, 0, data);
            return _manager.GetData(data);
        }

        [Test]
        public void Default_Empty_KeepsAllPassesEnabled_AndNoKeyword()
        {
            var m = Built(Mat(1));
            Assert.IsTrue(m.GetShaderPassEnabled("ShadowCaster"));
            Assert.IsTrue(m.GetShaderPassEnabled("DepthOnly"));
            Assert.IsFalse(m.IsKeywordEnabled(Keyword));
        }

        [Test]
        public void DisabledPasses_DisablesOnlyListedPass()
        {
            var m = Built(Mat(1, d => d.DisabledPasses = new[] { "ShadowCaster" }));
            Assert.IsFalse(m.GetShaderPassEnabled("ShadowCaster"));
            Assert.IsTrue(m.GetShaderPassEnabled("DepthOnly"));
            Assert.IsTrue(m.GetShaderPassEnabled("UniversalForward"));
        }

        [Test]
        public void DisabledPasses_IsCaseInsensitive()
        {
            var upper = Built(Mat(1, d => d.DisabledPasses = new[] { "SHADOWCASTER" }));
            Assert.IsFalse(upper.GetShaderPassEnabled("ShadowCaster"));
            var lower = Built(Mat(2, d => d.DisabledPasses = new[] { "shadowcaster" }));
            Assert.IsFalse(lower.GetShaderPassEnabled("ShadowCaster"));
        }

        [Test]
        public void DisabledPasses_PerMaterial_DoesNotLeakToOtherData()
        {
            var a = Built(Mat(1, d => d.DisabledPasses = new[] { "ShadowCaster" }));
            var b = Built(Mat(2));
            Assert.IsFalse(a.GetShaderPassEnabled("ShadowCaster"));
            Assert.IsTrue(b.GetShaderPassEnabled("ShadowCaster"));
        }

        [Test]
        public void UnknownOrEmptyNames_AreIgnored_WithoutException()
        {
            UnityEngine.Material m = null;
            Assert.DoesNotThrow(() => m = Built(Mat(1, d =>
            {
                d.DisabledPasses = new[] { null, string.Empty, "NoSuchPass", "ShadowCaster" };
                d.EnabledKeywords = new[] { null, string.Empty };
            })));
            Assert.IsFalse(m.GetShaderPassEnabled("ShadowCaster"));
            Assert.IsTrue(m.GetShaderPassEnabled("DepthOnly"));
        }

        [Test]
        public void EnabledKeywords_AreEnabled()
        {
            var m = Built(Mat(1, d => d.EnabledKeywords = new[] { Keyword }));
            Assert.IsTrue(m.IsKeywordEnabled(Keyword));
        }

        [Test]
        public void EnabledKeywords_DoNotBreakCommonKeywords_AndAreAppliedLast()
        {
            // Normal 無しなら Common は _NORMALMAP を無効にする。EnabledKeywords は Common の後なので最終的に有効。
            var m = Built(Mat(1, d => d.EnabledKeywords = new[] { "_NORMALMAP", Keyword }));
            Assert.IsTrue(m.IsKeywordEnabled("_NORMALMAP"), "EnabledKeywords が最終");
            Assert.IsTrue(m.IsKeywordEnabled(Keyword));

            // 指定しなければ Common の判断(Normal 無し = 無効)のまま。
            var plain = Built(Mat(2));
            Assert.IsFalse(plain.IsKeywordEnabled("_NORMALMAP"));
        }

        [Test]
        public void FadeTo_TempMaterial_AndSettledMaterial_KeepPassesAndKeywords()
        {
            var from = Mat(1);
            var to = Mat(2, d =>
            {
                d.DisabledPasses = new[] { "ShadowCaster" };
                d.EnabledKeywords = new[] { Keyword };
            });
            _manager.ApplyData(_renderer, 0, from);

            var h = _manager.FadeToData(_renderer, 0, to, 1f);
            Assert.IsTrue(_manager.IsFading(h));
            Assert.IsFalse(_renderer.sharedMaterial.GetShaderPassEnabled("ShadowCaster"), "フェード開始直後(一時 Material)");
            Assert.IsTrue(_renderer.sharedMaterial.IsKeywordEnabled(Keyword));

            _manager.Tick(0.5f);
            Assert.IsFalse(_renderer.sharedMaterial.GetShaderPassEnabled("ShadowCaster"), "Lerp 後(フェード中)");
            Assert.IsTrue(_renderer.sharedMaterial.IsKeywordEnabled(Keyword));

            _manager.Tick(0.6f);
            Assert.IsFalse(_renderer.sharedMaterial.GetShaderPassEnabled("ShadowCaster"), "完了後(共有 Material)");
            Assert.IsTrue(_renderer.sharedMaterial.IsKeywordEnabled(Keyword));
        }

        [Test]
        public void UnityBehavior_MaterialCopy_InheritsPassesAndKeywords_AndLerpDoesNotTouchThem()
        {
            // docs/51 §4.12 の「未確認」の実機確認結果を固定する(2026-10-03、Unity 6000.3.13f1)。
            var src = new UnityEngine.Material(TestShader());
            src.SetShaderPassEnabled("ShadowCaster", false);
            src.EnableKeyword(Keyword);
            var copy = new UnityEngine.Material(src);
            var plain = new UnityEngine.Material(TestShader());
            var lerped = new UnityEngine.Material(TestShader());
            try
            {
                Assert.IsFalse(copy.GetShaderPassEnabled("ShadowCaster"), "new Material(src) はパスの無効化を引き継ぐ");
                Assert.IsTrue(copy.IsKeywordEnabled(Keyword), "new Material(src) はキーワードを引き継ぐ");

                copy.Lerp(plain, plain, 0.5f);
                Assert.IsFalse(copy.GetShaderPassEnabled("ShadowCaster"), "Lerp はパスの状態を変えない");
                Assert.IsTrue(copy.IsKeywordEnabled(Keyword), "Lerp はキーワードを変えない");

                lerped.Lerp(src, src, 0.5f);
                Assert.IsTrue(lerped.GetShaderPassEnabled("ShadowCaster"), "Lerp は相手のパス状態を持ってこない");
                Assert.IsFalse(lerped.IsKeywordEnabled(Keyword), "Lerp は相手のキーワードを持ってこない");
            }
            finally
            {
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(lerped);
            }
        }

        [Test]
        public void FadeTo_FromDisabled_ToPlain_TempKeepsFromState_ThenSettlesOnTargetState()
        {
            var from = Mat(1, d => d.DisabledPasses = new[] { "ShadowCaster" });
            var to = Mat(2);
            _manager.ApplyData(_renderer, 0, from);

            _manager.FadeToData(_renderer, 0, to, 1f);
            Assert.IsFalse(_renderer.sharedMaterial.GetShaderPassEnabled("ShadowCaster"), "フェード中は複製元(from)の状態が残る");

            _manager.Tick(1.1f);
            Assert.IsTrue(_renderer.sharedMaterial.GetShaderPassEnabled("ShadowCaster"), "完了で to の共有 Material(パス有効)に戻る");
        }

        [Test]
        public void ShaderInfo_ListsLightModes_AndKeywords()
        {
            var modes = new List<string>();
            MaterialShaderInfo.CollectLightModes(TestShader(), modes);
            Assert.IsTrue(MaterialShaderInfo.ContainsIgnoreCase(modes, "UniversalForward"));
            Assert.IsTrue(MaterialShaderInfo.ContainsIgnoreCase(modes, "ShadowCaster"), "Unity は ShadowCaster を SHADOWCASTER で返すことがある");
            Assert.IsTrue(MaterialShaderInfo.ContainsIgnoreCase(modes, "DepthOnly"));

            var keywords = new List<string>();
            MaterialShaderInfo.CollectKeywords(TestShader(), keywords);
            CollectionAssert.Contains(keywords, Keyword);
        }

        private static List<ValidationResult> Validate(MaterialData mat)
            => new List<ValidationResult>(new MaterialDataValidator().Validate(mat, new ValidationContext(new List<AssetDataBase> { mat })));

        [Test]
        public void Validator_Warns_UnknownPass_AndInfos_UndeclaredKeyword()
        {
            var mat = Mat(1, d =>
            {
                d.DisabledPasses = new[] { "ShadowCaster", "NoSuchPass" };
                d.EnabledKeywords = new[] { Keyword, "_NO_SUCH_KEYWORD" };
            });
            var results = Validate(mat);
            var pass = results.Find(r => r.Message.Contains("NoSuchPass"));
            Assert.IsNotNull(pass.Message);
            Assert.AreEqual(ValidationSeverity.Warning, pass.Severity);
            var keyword = results.Find(r => r.Message.Contains("_NO_SUCH_KEYWORD"));
            Assert.IsNotNull(keyword.Message);
            Assert.AreEqual(ValidationSeverity.Info, keyword.Severity);
            Assert.IsFalse(results.Exists(r => r.Message.Contains("DisabledPasses 'ShadowCaster'")), "有効なパス名は警告しない");
            Assert.IsFalse(results.Exists(r => r.Message.Contains("EnabledKeywords '" + Keyword + "'")), "宣言済みキーワードは指摘しない");
        }

        [Test]
        public void Validator_Empty_ReportsNothingAboutPassesOrKeywords()
        {
            var results = Validate(Mat(1));
            Assert.IsFalse(results.Exists(r => r.Message.Contains("DisabledPasses") || r.Message.Contains("EnabledKeywords")));
        }
    }
}
