using System.Reflection;
using System.Threading.Tasks;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class SeEmitterTests
    {
        private GameObject _sourcePrefab;
        private AudioManager _manager;
        private GameObject _emitterObject;

        [SetUp]
        public void SetUp()
        {
            _sourcePrefab = new GameObject("SeSourcePrefab");
            _sourcePrefab.AddComponent<AudioSource>();
        }

        [TearDown]
        public void TearDown()
        {
            Audio.Bind((AudioManager)null);
            if (_emitterObject != null)
            {
                Object.DestroyImmediate(_emitterObject);
            }

            Object.DestroyImmediate(_sourcePrefab);
        }

        private static void SetSeId(SeEmitter emitter, AssetId<SeMarker> id)
        {
            var field = typeof(SeEmitter).GetField("seId", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(emitter, id);
        }

        [Test]
        public async Task OnEnable_PlaysRegisteredSe_OnDisable_Stops()
        {
            var loader = new FakeAssetLoader();
            var clip = AudioClip.Create("Loop", 4410, 1, 44100, false);
            var seData = ScriptableObject.CreateInstance<SeData>();
            seData.Id = 42;
            seData.Clips = new[] { clip };
            seData.Loop = true;
            loader.Assets["se/env"] = seData;

            var registry = new AssetRegistry(loader);
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new System.Collections.Generic.List<CatalogEntry>
            {
                new CatalogEntry { Id = 42, Type = AssetType.Se, Address = "se/env", Flags = new DDrive.Foundation.Data.AssetFlags { Load = DDrive.Foundation.Data.LoadMode.Preload } },
            });
            await registry.RegisterCatalogAsync(catalog);

            _manager = new AudioManager(new DDrive.Foundation.Pool.PoolService(), registry, _sourcePrefab);
            Audio.Bind(_manager);

            _emitterObject = new GameObject("Emitter");
            _emitterObject.SetActive(false);
            var emitter = _emitterObject.AddComponent<SeEmitter>();
            SetSeId(emitter, new AssetId<SeMarker>(42, AssetType.Se));
            _emitterObject.SetActive(true); // ここで初めて正しい seId で OnEnable が発火する

            var handleField = typeof(SeEmitter).GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance);
            var handle = (DDrive.Foundation.Handle.Handle<SeMarker>)handleField.GetValue(emitter);
            Assert.IsTrue(_manager.IsPlaying(handle));

            _emitterObject.SetActive(false);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }
    }
}
