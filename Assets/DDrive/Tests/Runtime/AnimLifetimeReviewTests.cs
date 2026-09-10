using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEngine;
using AnimId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim.AnimMarker>;

namespace DDrive.Tests.Runtime
{
    // 2026-09-09 レビュー指摘の回帰テスト:
    //  P0-3 Model を Despawn したらその Animator の Anim が止まる(プール再利用で残らない)
    //  P1-4 SetMaterial が共有 Data を書き換えない(Instance 側の上書き)
    //  P1-5 AnimatorProxy の Layer 別状態 / 解放時の Animator.speed 復元
    //  P2-6 大きい dt で複数周回分の OnLoop / Frame イベントを落とさない
    public class AnimLifetimeReviewTests
    {
        private PoolService _pool;
        private GameObject _modelPrefab;
        private readonly List<EventTrigger> _fired = new();

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _modelPrefab = new GameObject("ReviewModelPrefab");
            _modelPrefab.AddComponent<Animator>();
            var body = new GameObject("Body");
            body.transform.SetParent(_modelPrefab.transform);
            body.AddComponent<MeshRenderer>();
            _fired.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_modelPrefab);
        }

        private static AnimationClip Clip(float seconds = 1f)
        {
            var clip = new AnimationClip { legacy = true, frameRate = 30f };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, seconds, 1f));
            return clip;
        }

        private static AnimData Anim(ulong id, int layer = 0, bool loop = false, params AssetEvent[] events)
        {
            var data = ScriptableObject.CreateInstance<AnimData>();
            data.Id = id;
            data.DisplayName = $"Anim{id}";
            data.Clip = Clip();
            data.Layer = layer;
            data.Loop = loop;
            data.Events = events;
            return data;
        }

        private static AssetRegistry Registry(params AssetDataBase[] assets)
        {
            var loader = new FakeAssetLoader();
            var entries = new List<CatalogEntry>();
            foreach (var a in assets)
            {
                var type = a is AnimData ? AssetType.Anim : AssetType.Model;
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
                if (a is AnimData) registry.ResolveAsync<AnimData>(a.Id).GetAwaiter().GetResult();
                else registry.ResolveAsync<ModelData>(a.Id).GetAwaiter().GetResult();
            }

            return registry;
        }

        private int Count(EventTrigger t) => _fired.FindAll(x => x == t).Count;

        // ── P0-3 ──

        [Test]
        public void Despawn_StopsAnimsOfThatModel_AndPooledReuseStartsClean()
        {
            var anim = Anim(7);
            var model = ScriptableObject.CreateInstance<ModelData>();
            model.Id = 3;
            model.Prefab = _modelPrefab;
            model.DefaultAnimation = new AnimId(7, AssetType.Anim);
            var registry = Registry(anim, model);
            var animManager = new AnimManager(registry);
            var models = new ModelsManager(_pool, registry, animManager);

            var h1 = models.SpawnData(model, Vector3.zero, Quaternion.identity);
            var extra = animManager.PlayData(Anim(8, layer: 1), models.GetAnimator(h1)); // 外部から直接 Play した分
            Assert.AreEqual(2, animManager.ActiveCount);
            Assert.AreEqual(1, models.GetOwnedAnimCount(h1), "DefaultAnimation は Instance が所有する");

            models.Despawn(h1);
            Assert.AreEqual(0, animManager.ActiveCount, "Despawn で所有分も外部 Play 分も止まる");
            Assert.IsFalse(animManager.IsPlaying(extra));

            // プールから同じ GameObject を再利用しても旧アニメは残らない(DefaultAnimation だけが新規に再生される)。
            var plain = ScriptableObject.CreateInstance<ModelData>();
            plain.Id = 4;
            plain.Prefab = _modelPrefab;
            var h2 = models.SpawnData(plain, Vector3.zero, Quaternion.identity);
            Assert.AreEqual(0, animManager.ActiveCount);
            models.Despawn(h2);
        }

        // ── P1-4 ──

        [Test]
        public void SetMaterial_DoesNotMutateSharedData_AndIsPerInstance()
        {
            var model = ScriptableObject.CreateInstance<ModelData>();
            model.Id = 3;
            model.Prefab = _modelPrefab;
            model.Slots = new[] { new MaterialSlot { RendererPath = "Body", SlotIndex = 0 } };
            var models = new ModelsManager(_pool, new AssetRegistry(new FakeAssetLoader()));

            var a = models.SpawnData(model, Vector3.zero, Quaternion.identity);
            var b = models.SpawnData(model, Vector3.zero, Quaternion.identity);
            var materialId = new AssetId<MaterialMarker>(123UL, AssetType.Material);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*MaterialData.*"));
            models.SetMaterial(a, 0, materialId);

            Assert.AreEqual(0UL, model.Slots[0].Material.Value, "共有 Data は書き換えない");
            Assert.IsTrue(models.TryGetMaterial(a, 0, out var onA));
            Assert.AreEqual(123UL, onA.Value);
            Assert.IsTrue(models.TryGetMaterial(b, 0, out var onB));
            Assert.AreEqual(0UL, onB.Value, "別 Instance には漏れない");
            models.Despawn(a);
            models.Despawn(b);
        }

        // ── P1-5 ──

        [Test]
        public void Proxy_KeepsStatePerLayer()
        {
            var manager = new AnimManager(new AssetRegistry(new FakeAssetLoader()));
            var animator = _modelPrefab.GetComponent<Animator>();
            var layer0 = Anim(1, layer: 0);
            var layer1 = Anim(2, layer: 1);

            var h0 = manager.PlayData(layer0, animator);
            manager.PlayData(layer1, animator);
            var proxy = animator.GetComponent<AnimatorProxy>();

            Assert.AreSame(layer0, proxy.GetActiveData(0));
            Assert.AreSame(layer1, proxy.GetActiveData(1));
            Assert.AreEqual(2, proxy.ActiveLayerCount);

            manager.Tick(0.25f);
            Assert.AreEqual(0.25f, proxy.GetNormalizedTime(0), 1e-3f);
            Assert.AreEqual(0.25f, proxy.GetNormalizedTime(1), 1e-3f);

            manager.Stop(h0);
            Assert.IsNull(proxy.GetActiveData(0), "止めた Layer だけ外れる");
            Assert.AreSame(layer1, proxy.GetActiveData(1));
            manager.StopAll(StopReason.Manual);
        }

        [Test]
        public void Release_RestoresAnimatorSpeed()
        {
            var manager = new AnimManager(new AssetRegistry(new FakeAssetLoader()));
            var animator = _modelPrefab.GetComponent<Animator>();

            var h = manager.PlayData(Anim(1), animator);
            manager.SetSpeed(h, 2f);
            Assert.AreEqual(2f, animator.speed, 1e-4f);

            manager.Stop(h);
            Assert.AreEqual(1f, animator.speed, 1e-4f, "解放時に Animator.speed を戻す");

            // 同じ Animator に別の再生が残っていればその速度になる。
            var slow = manager.PlayData(Anim(2, layer: 1), animator);
            manager.SetSpeed(slow, 0.5f);
            var fast = manager.PlayData(Anim(3, layer: 0), animator);
            manager.SetSpeed(fast, 2f);
            manager.Stop(fast);
            Assert.AreEqual(0.5f, animator.speed, 1e-4f);
            manager.StopAll(StopReason.Manual);
        }

        [Test]
        public void SetPaused_FreezesTimeAndEvents_SeekStillUpdatesPose()
        {
            var manager = new AnimManager(new AssetRegistry(new FakeAssetLoader()));
            manager.Events.OnEventFired += (_, evt) => _fired.Add(evt.Trigger);
            var data = Anim(1, 0, false, new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f });
            var h = manager.PlayData(data, _modelPrefab.GetComponent<Animator>());

            manager.Tick(0.2f);
            manager.SetPaused(h, true);
            Assert.IsTrue(manager.IsPaused(h));
            manager.Tick(1.0f);
            Assert.AreEqual(0.2f, manager.GetNormalizedTime(h), 1e-3f, "一時停止中は時間が進まない");
            Assert.AreEqual(0, Count(EventTrigger.Frame), "一時停止中はイベントも出ない");
            Assert.IsTrue(manager.IsPlaying(h), "一時停止は再生中扱い(停止ではない)");

            manager.Seek(h, 0.8f);
            Assert.AreEqual(0.8f, manager.GetNormalizedTime(h), 1e-3f, "一時停止中でもシークできる");
            Assert.AreEqual(0.8f, _modelPrefab.transform.localPosition.x, 1e-2f, "シークでポーズが更新される(0.8s → x=0.8)");

            manager.SetPaused(h, false);
            manager.Tick(0.1f);
            Assert.AreEqual(0.9f, manager.GetNormalizedTime(h), 1e-3f, "再開で続きから進む");
            manager.StopAll(StopReason.Manual);
        }

        [Test]
        public void StopAllFor_InterruptsOnlyThatAnimator()
        {
            var manager = new AnimManager(new AssetRegistry(new FakeAssetLoader()));
            var other = new GameObject("Other");
            var otherAnimator = other.AddComponent<Animator>();
            try
            {
                var h1 = manager.PlayData(Anim(1), _modelPrefab.GetComponent<Animator>());
                var h2 = manager.PlayData(Anim(2), otherAnimator);

                manager.StopAllFor(otherAnimator);
                Assert.IsTrue(manager.IsPlaying(h1));
                Assert.IsFalse(manager.IsPlaying(h2));
                manager.StopAll(StopReason.Manual);
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        // ── P2-6 ──

        [Test]
        public void Tick_LargeDt_ProcessesEveryLoopAndFrameEvent()
        {
            var manager = new AnimManager(new AssetRegistry(new FakeAssetLoader()));
            manager.Events.OnEventFired += (_, evt) => _fired.Add(evt.Trigger);
            var data = Anim(1, 0, true,
                new AssetEvent { Trigger = EventTrigger.OnLoop },
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f });
            var h = manager.PlayData(data, _modelPrefab.GetComponent<Animator>());

            manager.Tick(2.5f); // 1 秒のクリップを 2 周半

            Assert.AreEqual(2, manager.GetLoopCount(h));
            Assert.AreEqual(2, Count(EventTrigger.OnLoop), "通過した周回ぶん OnLoop が出る");
            Assert.AreEqual(3, Count(EventTrigger.Frame), "各周回の Frame 15 + 現在周回の Frame 15");
            Assert.AreEqual(0.5f, manager.GetNormalizedTime(h), 1e-3f);
            manager.StopAll(StopReason.Manual);
        }

        [Test]
        public void Tick_LargeDt_NonLoop_FinishesOnce()
        {
            var manager = new AnimManager(new AssetRegistry(new FakeAssetLoader()));
            manager.Events.OnEventFired += (_, evt) => _fired.Add(evt.Trigger);
            var data = Anim(1, 0, false,
                new AssetEvent { Trigger = EventTrigger.OnDestroy },
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f });
            var h = manager.PlayData(data, _modelPrefab.GetComponent<Animator>());

            manager.Tick(5f);

            Assert.IsFalse(manager.IsPlaying(h));
            Assert.AreEqual(1, Count(EventTrigger.OnDestroy));
            Assert.AreEqual(1, Count(EventTrigger.Frame), "終端までのイベントは 1 回だけ");
        }
    }
}
