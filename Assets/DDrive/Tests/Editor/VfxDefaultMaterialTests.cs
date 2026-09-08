using DDrive.Editor.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [04_vfx.md] §2 — URP 対応の既定パーティクルマテリアル生成の検証。
    public class VfxDefaultMaterialTests
    {
        private const string TestPath = "Assets/DDrive/Tests/Editor/TempMaterials/M_DefaultParticleUnlit.mat";

        [TearDown]
        public void TearDown()
        {
            var folder = System.IO.Path.GetDirectoryName(TestPath)?.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void EnsureDefaultMaterial_UsesUrpParticleUnlitShader()
        {
            var material = VfxDefaultMaterial.EnsureDefaultMaterial(TestPath);

            Assert.IsNotNull(material);
            Assert.AreEqual("Universal Render Pipeline/Particles/Unlit", material.shader.name,
                "Built-in RP のシェーダーだと URP プロジェクトでは描画されないため、必ず URP 用シェーダーを使う");
        }

        [Test]
        public void EnsureDefaultMaterial_IsTransparentAdditive_AndVisibleWithoutTexture()
        {
            var material = VfxDefaultMaterial.EnsureDefaultMaterial(TestPath);

            Assert.AreEqual(Color.white, material.GetColor("_BaseColor"), "テクスチャ無しでも白で見える既定色");
            Assert.AreEqual(1f, material.GetFloat("_Surface"), "Surface=Transparent");
            Assert.AreEqual(0, material.GetInt("_ZWrite"), "透明合成なので ZWrite は無効");
        }

        [Test]
        public void EnsureDefaultMaterial_CalledTwice_ReturnsSameAsset_DoesNotOverwrite()
        {
            var first = VfxDefaultMaterial.EnsureDefaultMaterial(TestPath);
            first.SetColor("_BaseColor", Color.red);
            EditorUtility.SetDirty(first);
            AssetDatabase.SaveAssets();

            var second = VfxDefaultMaterial.EnsureDefaultMaterial(TestPath);

            Assert.AreEqual(AssetDatabase.GetAssetPath(first), AssetDatabase.GetAssetPath(second));
            Assert.AreEqual(Color.red, second.GetColor("_BaseColor"), "既存アセットは上書きされず、手動変更が保持される");
        }
    }
}
