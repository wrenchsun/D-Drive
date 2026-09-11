using System.IO;
using DDrive.Editor.Anim2D;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [05_model_animation.md] C-5 — Grid 分割は元画像のピクセル座標で切る(Max Size で縮小されるテクスチャでもずれない。2026-09-11 修正)。
    public class SpriteSlicerTests
    {
        private const string TempDir = "Assets/DDrive/Tests/Editor/Temp";
        private const string PngPath = TempDir + "/GridDownscaled.png";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
            }

            var png = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 64];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            png.SetPixels32(pixels);
            png.Apply();
            File.WriteAllBytes(PngPath, png.EncodeToPNG());
            Object.DestroyImmediate(png);
            AssetDatabase.ImportAsset(PngPath, ImportAssetOptions.ForceSynchronousImport);

            // Max Size で 32 に縮小された状態を作る(実プロジェクトの 2500 → 2048 と同じ状況)
            var importer = (TextureImporter)AssetImporter.GetAtPath(PngPath);
            importer.maxTextureSize = 32;
            importer.SaveAndReimport();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(PngPath);
        }

        [Test]
        public void SliceAndCollect_UsesSourcePixelSpace_WhenTextureIsDownscaled()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PngPath);
            Assume.That(texture.width == 32, "前提: Max Size 32 で縮小されている");

            Assert.IsTrue(SpriteSlicer.SliceAndCollect(texture, 4, 1, 4, out var sprites));

            Assert.AreEqual(4, sprites.Length);
            var importer = (TextureImporter)AssetImporter.GetAtPath(PngPath);
            Assert.AreEqual(16384, importer.maxTextureSize, "Automatic と同じく元サイズで扱う");
            Assert.AreEqual(64, sprites[0].texture.width, "縮小されていない");
            Assert.AreEqual(16f, sprites[0].rect.width, 0.01f, "元画像 64 / 4 列 = 16(縮小後の 32 / 4 = 8 ではない)");
            Assert.AreEqual(64f, sprites[0].rect.height, 0.01f);
            Assert.AreEqual(16f, sprites[1].rect.x, 0.01f);
            Assert.AreEqual(48f, sprites[3].rect.x, 0.01f);
        }
    }
}
