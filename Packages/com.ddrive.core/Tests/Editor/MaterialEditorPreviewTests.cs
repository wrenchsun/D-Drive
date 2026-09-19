using DDrive.Editor.Materials;
using DDrive.Foundation.Pool;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [06_material_texture.md] A-4 — MaterialEditorWindow のプレビュー生成(チケット 3-9)。
    // UI を介さず MaterialPreviewBuilder だけを検証する(EditorWindow は起動しない)。
    public class MaterialEditorPreviewTests
    {
        private GameObject _parent;
        private MaterialManager _manager;
        private PoolService _pool;
        private ModelsManager _models;
        private GameObject _modelPrefab;
        private ModelData _modelData;
        private MaterialData _materialData;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("PreviewParent");
            _manager = new MaterialManager(null);
            _pool = new PoolService();
            _pool.SetInstanceParent(_parent.transform);
            _models = new ModelsManager(_pool, null);

            _materialData = ScriptableObject.CreateInstance<MaterialData>();
            _materialData.DisplayName = "PreviewMat";
            _materialData.Shader = Lit();
            _materialData.Common = MaterialCommon.Default;

            // Slot 名 "Body" の Renderer(slot 0)を持つ最小プレハブ。
            _modelPrefab = new GameObject("PreviewModelPrefab");
            var bodyRenderer = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyRenderer.name = "Body";
            bodyRenderer.transform.SetParent(_modelPrefab.transform);

            _modelData = ScriptableObject.CreateInstance<ModelData>();
            _modelData.Prefab = _modelPrefab;
            _modelData.Slots = new[]
            {
                new MaterialSlot { RendererPath = "Body", SlotIndex = 0, Material = default },
            };
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Clear();
            Object.DestroyImmediate(_modelPrefab);
            Object.DestroyImmediate(_modelData);
            Object.DestroyImmediate(_materialData);
            if (_parent != null)
            {
                Object.DestroyImmediate(_parent);
            }
        }

        private static Shader Lit() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        [TestCase(MaterialPreviewShape.Sphere)]
        [TestCase(MaterialPreviewShape.Plane)]
        [TestCase(MaterialPreviewShape.Cube)]
        public void Create_Primitive_HasRendererWithSharedMaterial(MaterialPreviewShape shape)
        {
            var preview = MaterialPreviewBuilder.Create(shape, null, _materialData, _manager, _parent.transform, Vector3.zero, _models, "PreviewShape");

            Assert.IsNotNull(preview);
            Assert.IsNotNull(preview.Root);
            Assert.AreEqual(1, preview.Renderers.Length);
            Assert.IsTrue(_manager.TryGetBuilt(_materialData, out var shared));
            Assert.AreEqual(shared, preview.Renderers[0].sharedMaterial);
            Assert.AreEqual(HideFlags.DontSave, preview.Root.hideFlags);
            Assert.AreEqual(_parent.transform, preview.Root.transform.parent);

            preview.Dispose();
        }

        [Test]
        public void Create_Model_AppliesMaterialToSlotRenderer()
        {
            var preview = MaterialPreviewBuilder.Create(MaterialPreviewShape.Model, _modelData, _materialData, _manager, _parent.transform, Vector3.zero, _models, "PreviewModel");

            Assert.IsNotNull(preview);
            Assert.IsNotNull(preview.Root);
            var bodyTransform = preview.Root.transform.Find("Body");
            Assert.IsNotNull(bodyTransform);
            var renderer = bodyTransform.GetComponent<Renderer>();
            Assert.IsNotNull(renderer);
            Assert.IsTrue(_manager.TryGetBuilt(_materialData, out var shared));
            Assert.AreEqual(shared, renderer.sharedMaterial);

            preview.Dispose();
        }

        [Test]
        public void Create_Model_WithoutSlots_AppliesMaterialToEveryRenderer()
        {
            _modelData.Slots = System.Array.Empty<MaterialSlot>();
            var preview = MaterialPreviewBuilder.Create(MaterialPreviewShape.Model, _modelData, _materialData, _manager, _parent.transform, Vector3.zero, _models, "PreviewModelNoSlots");

            Assert.IsNotNull(preview);
            Assert.GreaterOrEqual(preview.Renderers.Length, 1);
            Assert.IsTrue(_manager.TryGetBuilt(_materialData, out var shared));
            foreach (var renderer in preview.Renderers)
            {
                Assert.AreEqual(shared, renderer.sharedMaterial);
            }

            preview.Dispose();
        }

        [Test]
        public void Dispose_Primitive_DestroysRoot()
        {
            var preview = MaterialPreviewBuilder.Create(MaterialPreviewShape.Sphere, null, _materialData, _manager, _parent.transform, Vector3.zero, _models, "DisposeSphere");
            Assert.IsNotNull(preview.Root);
            var root = preview.Root;

            preview.Dispose();

            Assert.IsNull(preview.Root);
            Assert.IsTrue(root == null); // Unity のオーバーロードされた == で破棄済みを判定する
        }

        [Test]
        public void Dispose_Model_DespawnsInstance()
        {
            var preview = MaterialPreviewBuilder.Create(MaterialPreviewShape.Model, _modelData, _materialData, _manager, _parent.transform, Vector3.zero, _models, "DisposeModel");
            Assert.IsNotNull(preview.Root);
            var handle = preview.ModelHandle;
            Assert.IsTrue(_models.IsValid(handle));

            preview.Dispose();

            Assert.IsFalse(_models.IsValid(handle));
        }

        [Test]
        public void Create_MissingRequirements_ReturnsNull()
        {
            Assert.IsNull(MaterialPreviewBuilder.Create(MaterialPreviewShape.Model, null, _materialData, _manager, _parent.transform, Vector3.zero, _models, "NoModel"));
            Assert.IsNull(MaterialPreviewBuilder.Create(MaterialPreviewShape.Sphere, null, _materialData, null, _parent.transform, Vector3.zero, _models, "NoManager"));
        }
    }
}
