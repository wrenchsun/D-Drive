using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEngine;
using MaterialId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Material.MaterialMarker>;
using TextureId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Material.TextureMarker>;

namespace DDrive.Tests.Runtime
{
    // [06_material_texture.md] A — 3-5: MaterialData → 共有 Material 生成 / Apply / Replace / FadeTo / MaterialAnim / Validator。
    public class MaterialManagerTests
    {
        private FakeAssetLoader _loader;
        private AssetRegistry _registry;
        private MaterialManager _manager;
        private GameObject _target;
        private Renderer _renderer;
        private Texture2D _texture;
        private readonly List<Object> _cleanup = new();

        [SetUp]
        public void SetUp()
        {
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _manager = new MaterialManager(_registry);
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

        private static Shader Lit() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        private MaterialData Mat(ulong id, System.Action<MaterialData> configure = null)
        {
            var data = ScriptableObject.CreateInstance<MaterialData>();
            data.Id = id;
            data.DisplayName = $"Mat{id}";
            data.Shader = Lit();
            data.Common = MaterialCommon.Default;
            configure?.Invoke(data);
            _cleanup.Add(data);
            return data;
        }

        private TextureData Tex(ulong id)
        {
            var data = ScriptableObject.CreateInstance<TextureData>();
            data.Id = id;
            data.Texture = _texture;
            data.Channel = TextureChannel.Albedo;
            _cleanup.Add(data);
            return data;
        }

        private void Register(params AssetDataBase[] assets)
        {
            var entries = new List<CatalogEntry>();
            foreach (var a in assets)
            {
                var type = a is MaterialData ? AssetType.Material : AssetType.Texture;
                var address = $"asset/{a.Id}";
                _loader.Assets[address] = a;
                entries.Add(new CatalogEntry { Id = a.Id, Type = type, Address = address });
            }

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(entries);
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            foreach (var a in assets)
            {
                if (a is MaterialData) _registry.ResolveAsync<MaterialData>(a.Id).GetAwaiter().GetResult();
                else _registry.ResolveAsync<TextureData>(a.Id).GetAwaiter().GetResult();
            }
        }

        [Test]
        public void Get_BuildsSharedMaterial_FromCommon_AndTextureId()
        {
            var tex = Tex(50);
            var mat = Mat(1, m =>
            {
                m.Common.Albedo = new TextureId(50, AssetType.Texture);
                m.Common.AlbedoTint = Color.red;
                m.Common.Blend = BlendType.Transparent;
                m.Common.DoubleSided = true;
            });
            Register(mat, tex);

            var a = _manager.Get(new MaterialId(1, AssetType.Material));
            var b = _manager.Get(new MaterialId(1, AssetType.Material));

            Assert.IsNotNull(a);
            Assert.AreSame(a, b, "Data ごとに 1 実体を共有する");
            Assert.AreEqual(1, _manager.BuiltCount);
            Assert.AreSame(_texture, a.mainTexture, "TextureData の ID からテクスチャを解決する");
            Assert.AreEqual(Color.red, a.HasProperty("_BaseColor") ? a.GetColor("_BaseColor") : a.color);
            Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, a.renderQueue);
            Assert.AreEqual("Transparent", a.GetTag("RenderType", false));
            if (a.HasProperty("_Cull"))
            {
                Assert.AreEqual(0f, a.GetFloat("_Cull"), "DoubleSided → Cull Off");
            }
        }

        [Test]
        public void Get_UnregisteredId_ReturnsPlaceholderMagenta_AndDoesNotThrow()
        {
            UnityEngine.Material result = null;
            Assert.DoesNotThrow(() => result = _manager.Get(new MaterialId(999, AssetType.Material)));
            Assert.IsNotNull(result);
            var color = result.HasProperty("_BaseColor") ? result.GetColor("_BaseColor") : result.color;
            Assert.AreEqual(Color.magenta, color);
        }

        [Test]
        public void Apply_And_Replace_SwapRendererSlot()
        {
            var a = Mat(1);
            var b = Mat(2, m => m.Common.AlbedoTint = Color.blue);
            Register(a, b);

            _manager.Apply(_renderer, 0, new MaterialId(1, AssetType.Material));
            Assert.AreSame(_manager.GetData(a), _renderer.sharedMaterial);

            var replaced = _manager.Replace(new MaterialId(1, AssetType.Material), new MaterialId(2, AssetType.Material));
            Assert.AreEqual(1, replaced);
            Assert.AreSame(_manager.GetData(b), _renderer.sharedMaterial);
        }

        [Test]
        public void FadeTo_UsesTempMaterial_ThenSettlesOnSharedTarget()
        {
            var a = Mat(1, m => m.Common.AlbedoTint = Color.black);
            var b = Mat(2, m => m.Common.AlbedoTint = Color.white);
            Register(a, b);
            _manager.ApplyData(_renderer, 0, a);

            var h = _manager.FadeToData(_renderer, 0, b, 1f);
            Assert.IsTrue(_manager.IsFading(h));
            Assert.AreNotSame(_manager.GetData(a), _renderer.sharedMaterial, "フェード中は一時 Material");
            Assert.AreNotSame(_manager.GetData(b), _renderer.sharedMaterial);

            _manager.Tick(0.5f);
            var mid = _renderer.sharedMaterial.HasProperty("_BaseColor") ? _renderer.sharedMaterial.GetColor("_BaseColor") : _renderer.sharedMaterial.color;
            // Material.Lerp はリニア空間で補間するため sRGB 値は 0.5 ちょうどにならない(≈0.735)。両端でないことを確認する。
            Assert.Greater(mid.r, 0.1f, "中間で Lerp されている");
            Assert.Less(mid.r, 0.95f, "中間で Lerp されている");

            _manager.Tick(0.6f);
            Assert.IsFalse(_manager.IsFading(h));
            Assert.AreSame(_manager.GetData(b), _renderer.sharedMaterial, "終了で共有 Material に戻る(一時 Material を残さない)");
            Assert.AreEqual(0, _manager.ActiveFadeCount);
        }

        [Test]
        public void FadeTo_ZeroSeconds_AppliesImmediately()
        {
            var b = Mat(2);
            Register(b);
            var h = _manager.FadeToData(_renderer, 0, b, 0f);
            Assert.IsFalse(_manager.IsFading(h));
            Assert.AreSame(_manager.GetData(b), _renderer.sharedMaterial);
        }

        [Test]
        public void Tick_MaterialAnim_ScrollsUvOffset_AndPauseStopsIt()
        {
            var mat = Mat(1, m =>
            {
                m.Flags.Pause = PauseMode.PauseWithGame;
                m.Anims = new[]
                {
                    new MaterialAnim
                    {
                        Property = "_BaseMap",
                        Channel = MaterialAnimChannel.OffsetU,
                        Value = new ValueDef
                        {
                            Mode = ValueMode.Parametric,
                            From = 0f,
                            To = 1f,
                            Time = TimeDef.Rate(1f),
                            Loop = LoopMode.Loop,
                        },
                    },
                };
            });
            Register(mat);
            var material = _manager.GetData(mat);
            if (!material.HasProperty("_BaseMap"))
            {
                Assert.Ignore("URP Lit が無い環境");
            }

            _manager.Tick(0.25f);
            Assert.AreEqual(0.25f, material.GetTextureOffset("_BaseMap").x, 1e-3f);

            _manager.OnPause(DDrive.Foundation.Pause.PauseChannel.Gameplay, true);
            _manager.Tick(0.25f);
            Assert.AreEqual(0.25f, material.GetTextureOffset("_BaseMap").x, 1e-3f, "Pause 中は進まない");

            _manager.OnPause(DDrive.Foundation.Pause.PauseChannel.Gameplay, false);
            _manager.Tick(0.25f);
            Assert.AreEqual(0.5f, material.GetTextureOffset("_BaseMap").x, 1e-3f);
        }

        [Test]
        public void Models_SetMaterial_AppliesThroughMaterialManager()
        {
            var mat = Mat(1, m => m.Common.AlbedoTint = Color.green);
            Register(mat);

            var pool = new PoolService();
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                var model = ScriptableObject.CreateInstance<ModelData>();
                model.Id = 7;
                model.Prefab = prefab;
                model.Slots = new[] { new MaterialSlot { RendererPath = string.Empty, SlotIndex = 0, Material = new MaterialId(1, AssetType.Material) } };
                _cleanup.Add(model);

                var models = new ModelsManager(pool, _registry, null, _manager);
                var h = models.SpawnData(model, Vector3.zero, Quaternion.identity);
                var renderer = models.GetGameObject(h).GetComponent<Renderer>();
                Assert.AreSame(_manager.GetData(mat), renderer.sharedMaterial, "Slots の Material が Spawn 時に適用される");

                var other = Mat(2);
                Register(other);
                models.SetMaterial(h, 0, new MaterialId(2, AssetType.Material));
                Assert.AreSame(_manager.GetData(other), renderer.sharedMaterial, "SetMaterial が実際に差し替える(2-5 の残課題)");
                models.Despawn(h);
            }
            finally
            {
                pool.Clear(PoolScope.Global);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Validator_FlagsMissingAlbedo_AndQueueMismatch()
        {
            var mat = Mat(1, m =>
            {
                m.Common.Blend = BlendType.Transparent;
                m.RenderQueueOffset = -1500;
            });
            var results = new List<ValidationResult>(new MaterialDataValidator().Validate(mat, new ValidationContext(new List<AssetDataBase> { mat })));
            Assert.IsTrue(results.Exists(r => r.Message.Contains("Albedo")));
            Assert.IsTrue(results.Exists(r => r.Message.Contains("Opaque 帯")));
        }

        [Test]
        public void Facade_Unbound_IsNoOp()
        {
            Mats.Bind(null);
            Assert.IsNull(Mats.Get(new MaterialId(1, AssetType.Material)));
            Assert.DoesNotThrow(() => Mats.Apply(_renderer, 0, new MaterialId(1, AssetType.Material)));
            Assert.AreEqual(0, Mats.Replace(new MaterialId(1, AssetType.Material), new MaterialId(2, AssetType.Material)));
            Assert.IsFalse(Mats.FadeTo(_renderer, new MaterialId(1, AssetType.Material), 1f).IsFading());
        }
    }
}
