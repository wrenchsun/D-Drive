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
        private const string TempDir = "Packages/com.ddrive.core/Tests/Editor/Temp";
        private const string PngPath = TempDir + "/GridDownscaled.png";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "Temp");
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

        // 矩形は元画像(64px)基準で切る。Max Size(32)はユーザー設定なので Slicer が書き換えないこと(2026-09-11 レビュー対応)。
        [Test]
        public void SliceAndCollect_UsesSourcePixelSpace_AndKeepsMaxTextureSize()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PngPath);
            Assume.That(texture.width == 32, "前提: Max Size 32 で縮小されている");

            Assert.IsTrue(SpriteSlicer.SliceAndCollect(texture, 4, 1, 4, out var sprites));

            Assert.AreEqual(4, sprites.Length);
            var importer = (TextureImporter)AssetImporter.GetAtPath(PngPath);
            Assert.AreEqual(32, importer.maxTextureSize, "Max Size を勝手に変えない");

            // spritesheet(SpriteMetaData)の矩形は元画像のピクセル座標: 64 / 4 列 = 16
#pragma warning disable CS0618 // spritesheet は旧 API だが SpriteSlicer と同じ経路を検証する
            var metas = importer.spritesheet;
#pragma warning restore CS0618
            Assert.AreEqual(4, metas.Length);
            Assert.AreEqual(16f, metas[0].rect.width, 0.01f, "元画像 64 / 4 列 = 16(縮小後の 32 / 4 = 8 ではない)");
            Assert.AreEqual(64f, metas[0].rect.height, 0.01f);
            Assert.AreEqual(16f, metas[1].rect.x, 0.01f);
            Assert.AreEqual(48f, metas[3].rect.x, 0.01f);

            // インポート後の Sprite は縮小率(32/64)ぶんスケールされた矩形になる(Unity 側の変換)。
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(PngPath);
            var ratio = imported.width / 64f;
            Assert.AreEqual(32, imported.width, "Max Size 32 のまま縮小されている");
            Assert.AreEqual(16f * ratio, sprites[0].rect.width, 0.51f);
            Assert.AreEqual(48f * ratio, sprites[3].rect.x, 0.51f);
        }
    }
}
