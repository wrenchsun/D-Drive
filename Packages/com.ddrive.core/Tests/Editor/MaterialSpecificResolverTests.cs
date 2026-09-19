using DDrive.Editor.Materials;
using DDrive.Foundation.Data;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Tests.Editor
{
    // [06_material_texture.md] A-2 — 共通チャンネル命名規約(MaterialCommonNaming)+ Specific 自動解決(MaterialSpecificResolver / Sync)。
    // シェーダーは ShaderUtil.CreateShaderAsset でメモリ上に作る(SourceAssets の実シェーダーに依存しない)。
    public class MaterialSpecificResolverTests
    {
        private const string ShaderSource = @"
Shader ""DDrive/Tests/SpecificResolver""
{
    Properties
    {
        _BaseMap (""Albedo"", 2D) = ""white"" {}
        _BaseColor (""Tint"", Color) = (1, 1, 1, 1)
        _BumpMap (""Normal"", 2D) = ""bump"" {}
        _EmissionColor (""Emission"", Color) = (0, 0, 0, 1)
        [HideInInspector] _Surface (""Surface"", Float) = 0
        [HideInInspector] _SrcBlend (""Src"", Float) = 1
        _Cutoff (""Cutoff"", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull (""Cull"", Float) = 2
        [HideInInspector] _InternalState (""Internal"", Float) = 7
        [PerRendererData] _PerRenderer (""PerRenderer"", Float) = 1
        _RimColor (""Rim Color"", Color) = (0.2, 0.6, 1.0, 1)
        _RimPower (""Rim Power"", Range(0.5, 8)) = 3.0
        _Steps (""Steps"", Integer) = 4
        _Offset (""Offset"", Vector) = (1, 2, 3, 4)
        _DetailMap (""Detail"", 2D) = ""gray"" {}
    }
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            float4 Vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 Frag() : SV_Target { return 1; }
            ENDHLSL
        }
    }
}";

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

        // --- MaterialCommonNaming ---

        [TestCase("_BaseMap", MaterialCommonNaming.Kind.CommonChannel)]
        [TestCase("_MainTex", MaterialCommonNaming.Kind.CommonChannel)]
        [TestCase("_EmissionColor", MaterialCommonNaming.Kind.CommonChannel)]
        [TestCase("_Surface", MaterialCommonNaming.Kind.RenderState)]
        [TestCase("_Cull", MaterialCommonNaming.Kind.RenderState)]
        [TestCase("_QueueOffset", MaterialCommonNaming.Kind.RenderState)]
        [TestCase("_BaseMap_ST", MaterialCommonNaming.Kind.Derived)]
        [TestCase("_DetailMap_TexelSize", MaterialCommonNaming.Kind.Derived)]
        [TestCase("unity_LightData", MaterialCommonNaming.Kind.Reserved)]
        [TestCase("_UnityFoo", MaterialCommonNaming.Kind.Reserved)]
        [TestCase("", MaterialCommonNaming.Kind.Reserved)]
        [TestCase("_RimColor", MaterialCommonNaming.Kind.Specific)]
        [TestCase("_ST", MaterialCommonNaming.Kind.Specific)] // 接尾辞だけの名前は付随名ではない
        public void Classify_FollowsNamingConvention(string property, MaterialCommonNaming.Kind expected)
        {
            Assert.AreEqual(expected, MaterialCommonNaming.Classify(property));
        }

        [Test]
        public void IsSpecific_ExcludesHiddenAndPerRendererFlags()
        {
            Assert.IsTrue(MaterialCommonNaming.IsSpecific("_RimColor", ShaderPropertyFlags.None));
            Assert.IsFalse(MaterialCommonNaming.IsSpecific("_RimColor", ShaderPropertyFlags.HideInInspector));
            Assert.IsFalse(MaterialCommonNaming.IsSpecific("_RimColor", ShaderPropertyFlags.PerRendererData));
            Assert.IsFalse(MaterialCommonNaming.IsSpecific("_RimColor", ShaderPropertyFlags.NonModifiableTextureData));
            Assert.IsFalse(MaterialCommonNaming.IsSpecific("_BaseMap", ShaderPropertyFlags.None));
        }

        // --- Resolve ---

        [Test]
        public void Resolve_ReturnsOnlySpecificPropertiesWithDefaults()
        {
            var resolved = MaterialSpecificResolver.Resolve(_shader);

            CollectionAssert.AreEquivalent(
                new[] { "_RimColor", "_RimPower", "_Steps", "_Offset", "_DetailMap" },
                resolved.ConvertAll(p => p.Property));

            var rimColor = resolved.Find(p => p.Property == "_RimColor");
            Assert.AreEqual(ParamValueType.Color, rimColor.Value.Type);
            Assert.AreEqual(new Color(0.2f, 0.6f, 1.0f, 1f), rimColor.Value.ColorValue);

            var rimPower = resolved.Find(p => p.Property == "_RimPower");
            Assert.AreEqual(ParamValueType.Float, rimPower.Value.Type);
            Assert.AreEqual(3.0f, rimPower.Value.FloatValue);

            var steps = resolved.Find(p => p.Property == "_Steps");
            Assert.AreEqual(ParamValueType.Int, steps.Value.Type);
            Assert.AreEqual(4, steps.Value.IntValue);

            var offset = resolved.Find(p => p.Property == "_Offset");
            Assert.AreEqual(ParamValueType.Vector, offset.Value.Type);
            Assert.AreEqual(new Vector4(1, 2, 3, 4), offset.Value.VectorValue);

            var detail = resolved.Find(p => p.Property == "_DetailMap");
            Assert.AreEqual(ParamValueType.Object, detail.Value.Type);
            Assert.IsNull(detail.Value.ObjectValue);
        }

        [Test]
        public void Resolve_NullShader_ReturnsEmpty()
        {
            Assert.IsEmpty(MaterialSpecificResolver.Resolve(null));
        }

        // D-Drive 標準シェーダーの固有は「ヘッダーコメントに書いてある通り」であることを実シェーダーで固定する(2026-09-11 レビュー対応)。
        [Test]
        public void Resolve_DDriveLit_HasOnlyOcclusionStrength()
        {
            var lit = FindShaderOrIgnore("DDrive/Lit");

            var resolved = MaterialSpecificResolver.Resolve(lit);

            CollectionAssert.AreEquivalent(new[] { "_OcclusionStrength" }, resolved.ConvertAll(p => p.Property));
        }

        [Test]
        public void Resolve_DDriveUnlit_HasNoSpecific()
        {
            var unlit = FindShaderOrIgnore("DDrive/Unlit");

            Assert.IsEmpty(MaterialSpecificResolver.Resolve(unlit), "DDrive/Unlit に固有は無い(ヘッダーコメントの通り)");
        }

        private static Shader FindShaderOrIgnore(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
            {
                Assert.Ignore($"シェーダー '{name}' がプロジェクトに無いためスキップ");
            }

            return shader;
        }

        // --- Merge ---

        [Test]
        public void Merge_KeepsExistingValues_AddsMissing_KeepsStale()
        {
            var existing = new[]
            {
                new ShaderParam { Property = "_RimPower", Value = ParamValue.Of(6.5f) },       // 既存(値を変えている)
                new ShaderParam { Property = "_OldParam", Value = ParamValue.Of(1f) },         // シェーダーに無い
            };
            var report = new MaterialSpecificResolver.MergeReport();

            var merged = MaterialSpecificResolver.Merge(existing, _shader, report);

            Assert.AreEqual("_RimPower", merged[0].Property);
            Assert.AreEqual(6.5f, merged[0].Value.FloatValue, "既存の値は上書きしない");
            Assert.AreEqual("_OldParam", merged[1].Property, "シェーダーに無いものも残す");
            CollectionAssert.AreEquivalent(new[] { "_RimColor", "_Steps", "_Offset", "_DetailMap" }, report.Added);
            CollectionAssert.AreEquivalent(new[] { "_RimPower" }, report.Kept);
            CollectionAssert.AreEquivalent(new[] { "_OldParam" }, report.Stale);
            Assert.AreEqual(6, merged.Length);
        }

        [Test]
        public void Merge_NothingMissing_ReportsUnchanged()
        {
            var full = MaterialSpecificResolver.Resolve(_shader).ToArray();
            var report = new MaterialSpecificResolver.MergeReport();

            var merged = MaterialSpecificResolver.Merge(full, _shader, report);

            Assert.IsFalse(report.Changed);
            Assert.AreEqual(full.Length, merged.Length);
        }

        // 共通チャンネル名が Specific に入っていると Common を黙って上書きするので、Merge が Conflict に挙げる(2026-09-11 レビュー対応)。
        [Test]
        public void Merge_CommonChannelInSpecific_IsReportedAsConflict()
        {
            var lit = FindShaderOrIgnore("DDrive/Lit");
            var existing = new[]
            {
                new ShaderParam { Property = "_Metallic", Value = ParamValue.Of(1f) },   // 共通チャンネル名
                new ShaderParam { Property = "_Cutoff", Value = ParamValue.Of(0.7f) },   // 描画ステート名
            };
            var report = new MaterialSpecificResolver.MergeReport();

            var merged = MaterialSpecificResolver.Merge(existing, lit, report);

            CollectionAssert.Contains(report.Conflict, "_Metallic");
            CollectionAssert.Contains(report.Conflict, "_Cutoff");
            Assert.AreEqual("_Metallic", merged[0].Property, "データは失わない(配列には残す)");
            Assert.IsTrue(MaterialSpecificResolver.IsConflicting(lit, "_Metallic"));
            Assert.IsFalse(MaterialSpecificResolver.IsConflicting(lit, "_OcclusionStrength"), "本来の固有は衝突ではない");
        }

        [Test]
        public void RemoveConflicts_DropsCommonChannels_AndUndoRestores()
        {
            var lit = FindShaderOrIgnore("DDrive/Lit");
            _data.Shader = lit;
            _data.Specific = new[]
            {
                new ShaderParam { Property = "_Metallic", Value = ParamValue.Of(1f) },
                new ShaderParam { Property = "_OcclusionStrength", Value = ParamValue.Of(0.5f) },
            };
            Undo.IncrementCurrentGroup();

            var removed = MaterialSpecificSync.RemoveConflicts(_data);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(1, _data.Specific.Length);
            Assert.AreEqual("_OcclusionStrength", _data.Specific[0].Property);

            Undo.PerformUndo();
            Assert.AreEqual(2, _data.Specific.Length, "Undo で削除前に戻る");
        }

        [Test]
        public void FindUnregistered_ListsShaderSpecificsNotInData()
        {
            var existing = new[] { new ShaderParam { Property = "_RimColor", Value = ParamValue.Of(Color.red) } };

            var missing = MaterialSpecificResolver.FindUnregistered(existing, _shader);

            CollectionAssert.AreEquivalent(new[] { "_RimPower", "_Steps", "_Offset", "_DetailMap" }, missing);
        }

        // --- Sync(Editor、Undo) ---

        [Test]
        public void Sync_WritesToData_AndUndoRestores()
        {
            _data.Specific = new[] { new ShaderParam { Property = "_RimColor", Value = ParamValue.Of(Color.red) } };
            Undo.IncrementCurrentGroup();

            var report = MaterialSpecificSync.Sync(_data);

            Assert.IsTrue(report.Changed);
            Assert.AreEqual(5, _data.Specific.Length);
            Assert.AreEqual(Color.red, _data.Specific[0].Value.ColorValue, "既存の値は保持");

            Undo.PerformUndo();
            Assert.AreEqual(1, _data.Specific.Length, "Undo で同期前に戻る");
        }

        [Test]
        public void Sync_NoShader_DoesNothing()
        {
            _data.Shader = null;
            _data.Specific = null;

            var report = MaterialSpecificSync.Sync(_data);

            Assert.IsFalse(report.Changed);
            Assert.IsNull(_data.Specific);
        }

        [Test]
        public void Sync_AlreadyComplete_DoesNotDirty()
        {
            MaterialSpecificSync.Sync(_data);
            EditorUtility.ClearDirty(_data);

            var report = MaterialSpecificSync.Sync(_data);

            Assert.IsFalse(report.Changed);
            Assert.IsFalse(EditorUtility.IsDirty(_data));
        }

        // --- Validator ---

        [Test]
        public void Validator_ReportsUnregisteredSpecificAsInfo()
        {
            _data.Specific = null;
            var results = new System.Collections.Generic.List<DDrive.Foundation.Validation.ValidationResult>(
                new MaterialDataValidator().Validate(_data, default));

            var info = results.Find(r => r.Severity == DDrive.Foundation.Validation.ValidationSeverity.Info && r.Message.Contains("_RimColor"));
            Assert.IsNotNull(info.Message, "未登録の固有が Info で報告される");
        }

        // 2026-09-11 レビュー対応: Specific に共通チャンネル名が入っていると Common を上書きするので Warning。
        [Test]
        public void Validator_ReportsCommonChannelInSpecificAsWarning()
        {
            _data.Specific = new[] { new ShaderParam { Property = "_BaseColor", Value = ParamValue.Of(Color.green) } };

            var results = new System.Collections.Generic.List<DDrive.Foundation.Validation.ValidationResult>(
                new MaterialDataValidator().Validate(_data, default));

            var warning = results.Find(r => r.Severity == DDrive.Foundation.Validation.ValidationSeverity.Warning
                                            && r.Message.Contains("_BaseColor") && r.Message.Contains("上書き"));
            Assert.IsNotNull(warning.Message, "共通チャンネル名の Specific が Warning で報告される");
        }
    }
}
