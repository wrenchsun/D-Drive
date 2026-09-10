using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Tests.Runtime
{
    // [02] §3 AssetEvent.Repeat(2026-09-10): 毎周回 / 1 回 / 再生中は維持(終了で止める)。
    public class EventRepeatTests
    {
        private static InstanceContext Ctx() => new(1, 1);

        [Test]
        public void ResetOnce_RearmsOnlyEveryLoopEvents()
        {
            var bus = new EventBus();
            var fired = new List<EventRepeat>();
            bus.OnEventFired += (_, e) => fired.Add(e.Repeat);
            var ctx = Ctx();
            bus.Begin(ctx, new[]
            {
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Repeat = EventRepeat.EveryLoop },
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Repeat = EventRepeat.Once },
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Repeat = EventRepeat.KeepWhilePlaying },
            });

            bus.TickAnimation(ctx, 1f, 30f);
            Assert.AreEqual(3, fired.Count, "1 周目は全部発火");

            bus.ResetOnce(ctx); // ループ
            bus.TickAnimation(ctx, 1f, 30f);
            Assert.AreEqual(4, fired.Count, "2 周目は EveryLoop だけ");
            Assert.AreEqual(EventRepeat.EveryLoop, fired[3]);

            var ended = false;
            bus.OnSessionEnded += c => ended = c.Equals(ctx);
            bus.End(ctx);
            Assert.IsTrue(ended, "End で終了通知が出る");
        }

        [Test]
        public void KeepWhilePlaying_StopsVfxWhenAnimEnds()
        {
            var pool = new PoolService();
            var prefab = new GameObject("KeepVfxPrefab");
            prefab.AddComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var actor = new GameObject("Actor");
            var animator = actor.AddComponent<Animator>();
            try
            {
                var vfx = ScriptableObject.CreateInstance<VfxData>();
                vfx.Id = 11;
                vfx.Prefab = prefab;
                vfx.LifeMode = VfxLifeMode.Loop;
                var loader = new FakeAssetLoader();
                loader.Assets["vfx/11"] = vfx;
                var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
                catalog.SetEntries(new List<CatalogEntry> { new() { Id = 11, Type = AssetType.Vfx, Address = "vfx/11" } });
                var registry = new AssetRegistry(loader);
                registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
                registry.ResolveAsync<VfxData>(11).GetAwaiter().GetResult();

                var clip = new AnimationClip { legacy = true, frameRate = 30f };
                clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                var anim = ScriptableObject.CreateInstance<AnimData>();
                anim.Clip = clip;
                anim.Loop = true;
                anim.Events = new[]
                {
                    new AssetEvent { Trigger = EventTrigger.Frame, Time = 3f, Action = EventAction.PlayAsset, Target = AssetRef.From(new VfxId(11, AssetType.Vfx)), Repeat = EventRepeat.KeepWhilePlaying },
                };

                var animManager = new AnimManager(registry);
                var vfxManager = new VfxManager(pool, registry);
                using var dispatcher = new AssetEventDispatcher(animManager.Events, registry, null, vfxManager, animManager.GetContextTransform);

                var h = animManager.PlayData(anim, animator);
                animManager.Tick(0.5f);
                Assert.AreEqual(1, vfxManager.ActiveCount, "1 回出る");
                Assert.AreEqual(1, dispatcher.KeptCount);

                animManager.Tick(1.0f); // 周回しても増えない
                animManager.Tick(1.0f);
                Assert.AreEqual(1, vfxManager.ActiveCount, "ループしても再スポーンしない");

                animManager.Stop(h); // 中断 → 維持していた VFX を止める
                Assert.AreEqual(0, dispatcher.KeptCount);
                vfxManager.Tick(0.1f);
                Assert.AreEqual(0, vfxManager.ActiveCount, "終了で Stop され、粒子が無いので消える");
            }
            finally
            {
                pool.Clear(PoolScope.Global);
                Object.DestroyImmediate(actor);
                Object.DestroyImmediate(prefab);
            }
        }
    }
}
