using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using AnimId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim.AnimMarker>;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Tests.Runtime
{
    // 3-3/3-4: Seek、Models.PlayAnim の委譲、AssetEventDispatcher(Frame イベント → VFX)の配線。
    public class AnimIntegrationTests
    {
        private PoolService _pool;
        private GameObject _vfxPrefab;
        private GameObject _modelPrefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _vfxPrefab = new GameObject("FxPrefab");
            _vfxPrefab.AddComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _modelPrefab = new GameObject("ModelPrefab");
            _modelPrefab.AddComponent<Animator>();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_vfxPrefab);
            Object.DestroyImmediate(_modelPrefab);
        }

        private static AnimationClip Clip()
        {
            var clip = new AnimationClip { legacy = true, frameRate = 30f };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            return clip;
        }

        private static AssetRegistry Registry(params AssetDataBase[] assets)
        {
            var loader = new FakeAssetLoader();
            var entries = new List<CatalogEntry>();
            foreach (var a in assets)
            {
                var type = a is AnimData ? AssetType.Anim : a is VfxData ? AssetType.Vfx : a is ModelData ? AssetType.Model : AssetType.Se;
                var address = $"asset/{a.Id}";
                loader.Assets[address] = a;
                entries.Add(new CatalogEntry { Id = a.Id, Type = type, Address = address });
            }

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(entries);
            var registry = new AssetRegistry(loader);
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            foreach (var a in assets)
            {
                switch (a)
                {
                    case AnimData: registry.ResolveAsync<AnimData>(a.Id).GetAwaiter().GetResult(); break;
                    case VfxData: registry.ResolveAsync<VfxData>(a.Id).GetAwaiter().GetResult(); break;
                    case ModelData: registry.ResolveAsync<ModelData>(a.Id).GetAwaiter().GetResult(); break;
                }
            }

            return registry;
        }

        [Test]
        public void Seek_SetsTime_AndMarksEarlierEventsAsFired()
        {
            var manager = new AnimManager(new AssetRegistry(new FakeAssetLoader()));
            var fired = 0;
            manager.Events.OnEventFired += (_, e) => { if (e.Trigger == EventTrigger.Frame) fired++; };
            var data = ScriptableObject.CreateInstance<AnimData>();
            data.Clip = Clip();
            data.Events = new[] { new AssetEvent { Trigger = EventTrigger.Frame, Time = 6f } }; // 0.2s
            var animator = _modelPrefab.GetComponent<Animator>();

            var handle = manager.PlayData(data, animator);
            manager.Seek(handle, 0.5f);

            Assert.AreEqual(0.5f, manager.GetNormalizedTime(handle), 1e-4f);
            Assert.AreEqual(0, fired, "シークでは発火しない");
            manager.Tick(0.1f);
            Assert.AreEqual(0, fired, "シーク位置より前のイベントは発火済み扱い");

            manager.Seek(handle, 0f);
            manager.Tick(0.3f);
            Assert.AreEqual(1, fired, "巻き戻せば再び発火する");
        }

        [Test]
        public void Models_PlayAnim_DelegatesToAnimManager_AndDefaultAnimationAutoPlays()
        {
            var anim = ScriptableObject.CreateInstance<AnimData>();
            anim.Id = 7;
            anim.Clip = Clip();
            var model = ScriptableObject.CreateInstance<ModelData>();
            model.Id = 3;
            model.Prefab = _modelPrefab;
            model.DefaultAnimation = new AnimId(7, AssetType.Anim);
            var registry = Registry(anim, model);
            var animManager = new AnimManager(registry);
            var models = new ModelsManager(_pool, registry, animManager);

            var h = models.SpawnData(model, Vector3.zero, Quaternion.identity);

            Assert.AreEqual(1, animManager.ActiveCount, "DefaultAnimation が Spawn 直後に再生される");
            Assert.IsNotNull(models.GetAnimator(h));

            var second = models.PlayAnim(h, new AnimId(7, AssetType.Anim));
            Assert.IsTrue(animManager.IsPlaying(second));
            Assert.AreEqual(1, animManager.ActiveCount, "同じ Animator+Layer なので前の再生は中断される");

            models.Despawn(h);
        }

        [Test]
        public void EventDispatcher_FrameEvent_SpawnsVfxAtAnimatorContext()
        {
            var vfx = ScriptableObject.CreateInstance<VfxData>();
            vfx.Id = 11;
            vfx.Prefab = _vfxPrefab;
            vfx.LifeMode = VfxLifeMode.Loop;
            vfx.Anchor = new AnchorDef { Space = AnchorSpace.ContextTarget, LocalOffset = new Vector3(0f, 1f, 0f), LocalScale = Vector3.one };
            var anim = ScriptableObject.CreateInstance<AnimData>();
            anim.Clip = Clip();
            anim.Events = new[]
            {
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Action = EventAction.PlayAsset, Target = AssetRef.From(new VfxId(11, AssetType.Vfx)) },
            };
            var registry = Registry(vfx);
            var animManager = new AnimManager(registry);
            var vfxManager = new VfxManager(_pool, registry);
            var spawned = 0;
            using var dispatcher = new AssetEventDispatcher(animManager.Events, registry, null, vfxManager, animManager.GetContextTransform);
            dispatcher.OnVfxSpawned += _ => spawned++;
            _modelPrefab.transform.position = new Vector3(5f, 0f, 0f);
            var animator = _modelPrefab.GetComponent<Animator>();

            animManager.PlayData(anim, animator);
            animManager.Tick(0.4f);
            Assert.AreEqual(0, vfxManager.ActiveCount);

            animManager.Tick(0.2f);
            Assert.AreEqual(1, vfxManager.ActiveCount, "15 フレーム(0.5s)で VFX が出る");
            Assert.AreEqual(1, spawned);

            // Anchor=ContextTarget なので Animator の位置 + (0,1,0) に出る。
            var expected = new Vector3(5f, 1f, 0f);
            var found = false;
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (Vector3.Distance(ps.transform.position, expected) < 1e-3f)
                {
                    found = true;
                }
            }

            Assert.IsTrue(found, "Animator の Transform を contextRoot として解決する");
        }
    }
}
