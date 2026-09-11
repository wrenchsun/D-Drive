using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Materials;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [06_material_texture.md] A-2 — aiStandardSurface → DDrive/AiStandardSurface の写像と、Maya インポータでの固有引き継ぎ(2026-09-11)。
    public class AiStandardSurfaceMapperTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempGameData";
        private const string TempFolder = "Assets/DDrive/Tests/Editor/Temp";
        private const string MaterialPath = TempFolder + "/AiSurfaceTest.mat";

        // MaterialDescription の代わり(値を辞書で持つ)。
        private sealed class FakeSource : IArnoldSource
        {
            public readonly Dictionary<string, float> Floats = new();
            public readonly Dictionary<string, Vector4> Colors = new();
            public readonly Dictionary<string, Texture> Textures = new();

            public bool TryGetFloat(string name, out float value) => Floats.TryGetValue(name, out value);
            public bool TryGetColor(string name, out Vector4 value) => Colors.TryGetValue(name, out value);
            public bool TryGetTexture(string name, out Texture texture, out Vector2 offset, out Vector2 scale)
            {
                offset = Vector2.zero;
                scale = Vector2.one;
                return Textures.TryGetValue(name, out texture);
            }
        }

        private Shader _shader;

        [SetUp]
        public void SetUp()
        {
            _shader = Shader.Find(AiStandardSurfaceMapper.ShaderName);
            Assume.That(_shader != null, "DDrive/AiStandardSurface が必要(Assets/SourceAssets/Shaders/AiStandardSurface)");
            MayaModelPostprocessor.Suppress = true;
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
            }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(MaterialPath);
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                AssetDatabase.SaveAssets();
            }

            MayaModelPostprocessor.Suppress = false;
        }

        private static FakeSource MayaSource()
        {
            var src = new FakeSource();
            src.Floats["TypeId"] = AiStandardSurfaceMapper.MayaArnoldTypeId;
            return src;
        }

        [Test]
        public void IsMayaArnoldStandardSurface_ByTypeId()
        {
            Assert.IsTrue(AiStandardSurfaceMapper.IsMayaArnoldStandardSurface(MayaSource()));
            Assert.IsFalse(AiStandardSurfaceMapper.IsMayaArnoldStandardSurface(new FakeSource()));
        }

        [Test]
        public void Apply_MapsAllArnoldParameters_Opaque()
        {
            var src = MayaSource();
            src.Floats["base"] = 0.9f;
            src.Colors["baseColor"] = new Vector4(0.2f, 0.4f, 0.6f, 1f);
            src.Floats["diffuseRoughness"] = 0.3f;
            src.Floats["metalness"] = 0.7f;
            src.Floats["specular"] = 0.8f;
            src.Colors["specularColor"] = new Vector4(1f, 0.9f, 0.8f, 1f);
            src.Floats["specularRoughness"] = 0.25f;
            src.Floats["specularIOR"] = 1.8f;
            src.Floats["specularAnisotropy"] = 0.1f;
            src.Floats["coat"] = 0.5f;
            src.Colors["coatColor"] = new Vector4(0.9f, 0.9f, 1f, 1f);
            src.Floats["coatRoughness"] = 0.2f;
            src.Floats["coatIOR"] = 1.6f;
            src.Floats["sheen"] = 0.4f;
            src.Colors["sheenColor"] = new Vector4(1f, 0.5f, 0.5f, 1f);
            src.Floats["sheenRoughness"] = 0.6f;
            src.Floats["emission"] = 2f;
            src.Colors["emissionColor"] = new Vector4(1f, 0.5f, 0f, 1f);
            src.Colors["opacity"] = new Vector4(1f, 1f, 1f, 1f);
            src.Floats["transmission"] = 0f;
            src.Floats["subsurface"] = 0.3f;
            src.Colors["subsurfaceColor"] = new Vector4(1f, 0.8f, 0.7f, 1f);
            src.Floats["thinWalled"] = 0f;

            var m = new UnityEngine.Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                Assert.IsTrue(AiStandardSurfaceMapper.Apply(src, m, _shader));
                Assert.AreEqual(_shader, m.shader);
                Assert.AreEqual(0.9f, m.GetFloat("_BaseWeight"), 1e-4f);
                Assert.AreEqual(0.2f, m.GetColor("_BaseColor").r, 1e-4f);
                Assert.AreEqual(0.3f, m.GetFloat("_DiffuseRoughness"), 1e-4f);
                Assert.AreEqual(0.7f, m.GetFloat("_Metallic"), 1e-4f);
                Assert.AreEqual(0.8f, m.GetFloat("_SpecularWeight"), 1e-4f);
                Assert.AreEqual(0.9f, m.GetColor("_SpecularColor").g, 1e-4f);
                Assert.AreEqual(0.75f, m.GetFloat("_Smoothness"), 1e-4f, "smoothness = 1 - roughness");
                Assert.AreEqual(1.8f, m.GetFloat("_SpecularIOR"), 1e-4f);
                Assert.AreEqual(0.1f, m.GetFloat("_SpecularAnisotropy"), 1e-4f);
                Assert.AreEqual(0.5f, m.GetFloat("_CoatWeight"), 1e-4f);
                Assert.AreEqual(0.2f, m.GetFloat("_CoatRoughness"), 1e-4f);
                Assert.AreEqual(1.6f, m.GetFloat("_CoatIOR"), 1e-4f);
                Assert.AreEqual(0.4f, m.GetFloat("_SheenWeight"), 1e-4f);
                Assert.AreEqual(0.5f, m.GetColor("_SheenColor").g, 1e-4f);
                Assert.AreEqual(0.6f, m.GetFloat("_SheenRoughness"), 1e-4f);
                Assert.AreEqual(2f, m.GetColor("_EmissionColor").r, 1e-4f, "emissionColor × emission");
                Assert.IsTrue(m.IsKeywordEnabled("_EMISSION"));
                Assert.AreEqual(1f, m.GetFloat("_Opacity"), 1e-4f);
                Assert.AreEqual(0f, m.GetFloat("_TransmissionWeight"), 1e-4f);
                Assert.AreEqual(0.3f, m.GetFloat("_SubsurfaceWeight"), 1e-4f);
                Assert.AreEqual(0f, m.GetFloat("_ThinWalled"), 1e-4f);
                Assert.AreEqual(0f, m.GetFloat("_Surface"), 1e-4f, "不透明");
                Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Geometry, m.renderQueue);
                Assert.AreEqual(2f, m.GetFloat("_Cull"), 1e-4f, "Back");
            }
            finally
            {
                Object.DestroyImmediate(m);
            }
        }

        [Test]
        public void Apply_OpacityOrTransmission_MakesTransparent_AndThinWalledIsDoubleSided()
        {
            var src = MayaSource();
            src.Colors["opacity"] = new Vector4(0.5f, 0.6f, 0.7f, 1f);
            src.Floats["thinWalled"] = 1f;
            var m = new UnityEngine.Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                AiStandardSurfaceMapper.Apply(src, m, _shader);
                Assert.AreEqual(0.5f, m.GetFloat("_Opacity"), 1e-4f, "最小成分");
                Assert.AreEqual(1f, m.GetFloat("_Surface"), 1e-4f, "透過");
                Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, m.renderQueue);
                Assert.AreEqual(0f, m.GetFloat("_Cull"), 1e-4f, "thinWalled → Cull Off");

                var src2 = MayaSource();
                src2.Floats["transmission"] = 0.3f;
                AiStandardSurfaceMapper.Apply(src2, m, _shader);
                Assert.AreEqual(1f, m.GetFloat("_Surface"), 1e-4f, "transmission > 0 も透過");
                Assert.AreEqual(0.3f, m.GetFloat("_TransmissionWeight"), 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(m);
            }
        }

        [Test]
        public void Apply_TexturedInputs_GoToMaps()
        {
            var tex = new Texture2D(4, 4);
            var src = MayaSource();
            src.Textures["baseColor"] = tex;
            src.Textures["metalness"] = tex;
            src.Textures["specularRoughness"] = tex;
            src.Textures["normalCamera"] = tex;
            src.Textures["opacity"] = tex;
            var m = new UnityEngine.Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                AiStandardSurfaceMapper.Apply(src, m, _shader);
                Assert.AreSame(tex, m.GetTexture("_BaseMap"));
                Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), "テクスチャ時は Tint 白");
                Assert.AreSame(tex, m.GetTexture("_MetalnessMap"));
                Assert.AreEqual(1f, m.GetFloat("_Metallic"), 1e-4f);
                Assert.AreSame(tex, m.GetTexture("_SpecularRoughnessMap"));
                Assert.AreEqual(1f, m.GetFloat("_Smoothness"), 1e-4f, "マップ時は Smoothness 1(マップ側で減衰)");
                Assert.AreSame(tex, m.GetTexture("_BumpMap"));
                Assert.IsTrue(m.IsKeywordEnabled("_NORMALMAP"));
                Assert.AreSame(tex, m.GetTexture("_OpacityMap"));
                Assert.AreEqual(1f, m.GetFloat("_Surface"), 1e-4f, "Opacity マップがあれば透過");
            }
            finally
            {
                Object.DestroyImmediate(m);
                Object.DestroyImmediate(tex);
            }
        }

        // Maya インポータ: 元 Material が DDrive/AiStandardSurface ならそのシェーダーで MaterialData を作り、Arnold 固有の値を引き継ぐ。
        [Test]
        public void MayaImporter_KeepsAiShader_AndCopiesSpecific()
        {
            var src = MayaSource();
            src.Floats["coat"] = 0.5f;
            src.Floats["sheen"] = 0.2f;
            src.Floats["specularIOR"] = 1.7f;
            src.Colors["baseColor"] = new Vector4(0.1f, 0.2f, 0.3f, 1f);
            var material = new UnityEngine.Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "AiBody" };
            AiStandardSurfaceMapper.Apply(src, material, _shader);
            AssetDatabase.CreateAsset(material, MaterialPath);

            var profile = ScriptableObject.CreateInstance<MayaImportProfile>(); // TargetShader 未設定 → 元のシェーダーを維持
            try
            {
                var report = new MayaMaterialImporter.Report();
                var data = MayaMaterialImporter.ImportMaterial(material, "Ai", "TestFbx", profile, report, TestRoot);

                Assert.IsNotNull(data, report.ToString());
                Assert.AreEqual(_shader, data.Shader, "DDrive/ のシェーダーはそのまま");
                Assert.AreEqual(0.1f, data.Common.AlbedoTint.r, 1e-3f);
                Assert.AreEqual(0.5f, System.Array.Find(data.Specific, p => p.Property == "_CoatWeight").Value.FloatValue, 1e-4f);
                Assert.AreEqual(0.2f, System.Array.Find(data.Specific, p => p.Property == "_SheenWeight").Value.FloatValue, 1e-4f);
                Assert.AreEqual(1.7f, System.Array.Find(data.Specific, p => p.Property == "_SpecularIOR").Value.FloatValue, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ResolveTargetShader_Priority()
        {
            var profile = ScriptableObject.CreateInstance<MayaImportProfile>();
            var urpLit = new UnityEngine.Material(Shader.Find("Universal Render Pipeline/Lit"));
            var ai = new UnityEngine.Material(_shader);
            try
            {
                Assert.AreEqual(UnityMaterialMigrator.LitShaderName, MayaMaterialImporter.ResolveTargetShader(profile, urpLit).name, "URP Lit → DDrive/Lit");
                Assert.AreEqual(_shader, MayaMaterialImporter.ResolveTargetShader(profile, ai), "DDrive/ はそのまま");
                profile.TargetShader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.AreEqual(profile.TargetShader, MayaMaterialImporter.ResolveTargetShader(profile, ai), "Profile 指定が最優先");
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(urpLit);
                Object.DestroyImmediate(ai);
            }
        }
    }
}
