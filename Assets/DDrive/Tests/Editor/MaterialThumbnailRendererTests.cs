using DDrive.Editor.Materials;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §2 例外 — Material のウィンドウ内サムネイル(2026-09-11)。実 MaterialManager の共有 Material を描く。
    public class MaterialThumbnailRendererTests
    {
        private MaterialManager _manager;
        private MaterialData _data;
        private MaterialThumbnailRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _manager = new MaterialManager(null);
            _data = ScriptableObject.CreateInstance<MaterialData>();
            _data.Shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _data.Common = MaterialCommon.Default;
            _renderer = new MaterialThumbnailRenderer();
        }

        [TearDown]
        public void TearDown()
        {
            _renderer.Dispose();
            _manager.Clear();
            Object.DestroyImmediate(_data);
        }

        [TestCase(MaterialPreviewShape.Sphere)]
        [TestCase(MaterialPreviewShape.Plane)]
        [TestCase(MaterialPreviewShape.Cube)]
        public void Render_Primitive_ReturnsTextureOfRequestedSize(MaterialPreviewShape shape)
        {
            var material = _manager.GetData(_data);

            var texture = _renderer.Render(material, shape, 30f, 45f, 128, 96);

            // PreviewRenderUtility は EditorGUIUtility.pixelsPerPoint 倍(例: 1.25)でテクスチャを作るので、サイズは「以上 + 同じ比率」で見る。
            Assert.IsNotNull(texture);
            Assert.GreaterOrEqual(texture.width, 128);
            Assert.GreaterOrEqual(texture.height, 96);
            Assert.AreEqual(128f / 96f, (float)texture.width / texture.height, 0.05f);
        }

        [Test]
        public void Render_Model_ReturnsNull()
        {
            Assert.IsNull(_renderer.Render(_manager.GetData(_data), MaterialPreviewShape.Model, 0f, 0f, 64, 64));
        }

        [Test]
        public void Render_NullMaterial_ReturnsNull()
        {
            Assert.IsNull(_renderer.Render(null, MaterialPreviewShape.Sphere, 0f, 0f, 64, 64));
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            _renderer.Render(_manager.GetData(_data), MaterialPreviewShape.Sphere, 0f, 0f, 32, 32);
            _renderer.Dispose();
            Assert.DoesNotThrow(() => _renderer.Dispose());
        }
    }
}
