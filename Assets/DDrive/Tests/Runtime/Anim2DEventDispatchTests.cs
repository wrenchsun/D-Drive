using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using Anim2DId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim2D.Anim2DMarker>;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Tests.Runtime
{
    // U-20([39_usability_fixes_2026-09-17.md]) — 「Anim2D の Anim Editor から SE・VFX を設定はできるが再生されない」の
    // 再現テスト。AnimIntegrationTests.EventDispatcher_FrameEvent_SpawnsVfxAtAnimatorContext と同じ配線(AnimManager +
    // AssetEventDispatcher)を、AnimData ではなく Anim2DData(+ Anim2D 静的ファサード)で行い、3D 側との差分を検証する。
    public class Anim2DEventDispatchTests
    {
        private PoolService _pool;
        private GameObject _vfxPrefab;
        private GameObject _actor;
        private Animator _animator;
        private AudioSource _seSourcePrefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _vfxPrefab = new GameObject("FxPrefab");
            _vfxPrefab.AddComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _actor = new GameObject("Actor2D");
            _animator = _actor.AddComponent<Animator>();
            var seSourceGo = new GameObject("SeSourcePrefab");
            _seSourcePrefab = seSourceGo.AddComponent<AudioSource>();
        }

        [TearDown]
        public void TearDown()
        {
            Anim2D.Bind(null, null);
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_vfxPrefab);
            Object.DestroyImmediate(_actor);
            Object.DestroyImmediate(_seSourcePrefab.gameObject);
        }

        private static AnimationClip Clip(float frameRate = 30f)
        {
            var clip = new AnimationClip { legacy = true, frameRate = frameRate };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            return clip;
        }

        private static AssetRegistry Registry(params AssetDataBase[] assets)
        {
            var loader = new FakeAssetLoader();
            var entries = new List<CatalogEntry>();
            foreach (var a in assets)
            {
                var type = a is Anim2DData ? AssetType.Anim2D : a is VfxData ? AssetType.Vfx : a is SeData ? AssetType.Se : AssetType.Anim;
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
                    case Anim2DData: registry.ResolveAsync<AnimData>(a.Id).GetAwaiter().GetResult(); break;
                    case VfxData: registry.ResolveAsync<VfxData>(a.Id).GetAwaiter().GetResult(); break;
                    case SeData: registry.ResolveAsync<SeData>(a.Id).GetAwaiter().GetResult(); break;
                }
            }

            return registry;
        }

        // AnimEditorWindow(Anim Editor)の Events(Trigger=Frame, Action=PlayAsset)を Anim2DData に設定したのと
        // 同じ状態を作り、実プレイ経路(Anim2D 静的ファサード → AnimManager → AssetEventDispatcher)で SE/VFX が
        // 鳴る/出るかを検証する。
        [Test]
        public void Anim2D_FrameEvent_PlaysSeAndSpawnsVfx_ThroughFacade()
        {
            var vfx = ScriptableObject.CreateInstance<VfxData>();
            vfx.Id = 21;
            vfx.Prefab = _vfxPrefab;
            vfx.LifeMode = VfxLifeMode.Loop;
            vfx.Anchor = new AnchorDef { Space = AnchorSpace.ContextTarget, LocalScale = Vector3.one };

            var se = ScriptableObject.CreateInstance<SeData>();
            se.Id = 22;
            se.Clips = new[] { AudioClip.Create("TestClip", 4410, 1, 44100, false) };
            se.SelectMode = ClipSelectMode.First;
            se.Volume = 1f;
            se.MaxConcurrent = 8;

            var anim2D = ScriptableObject.CreateInstance<Anim2DData>();
            anim2D.Id = 20;
            anim2D.Clip = Clip();
            anim2D.Events = new[]
            {
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Action = EventAction.PlayAsset, Target = AssetRef.From(new VfxId(21, AssetType.Vfx)) },
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Action = EventAction.PlayAsset, Target = AssetRef.From(new SeId(22, AssetType.Se)) },
            };

            var registry = Registry(vfx, se, anim2D);
            var animManager = new AnimManager(registry);
            var vfxManager = new VfxManager(_pool, registry);
            var audioManager = new AudioManager(_pool, registry, _seSourcePrefab.gameObject);
            using var dispatcher = new AssetEventDispatcher(animManager.Events, registry, audioManager, vfxManager, animManager.GetContextTransform);

            Anim2D.Bind(animManager, registry);

            var h = Anim2D.Play(new Anim2DId(20, AssetType.Anim2D), _animator);
            Assert.IsTrue(Anim2D.IsPlaying(h));

            animManager.Tick(0.4f); // 15フレーム(0.5s)に満たない
            Assert.AreEqual(0, vfxManager.ActiveCount, "0.4s ではまだ 15 フレーム目(0.5s)に到達しない");
            Assert.AreEqual(0, audioManager.ActiveCount);

            animManager.Tick(0.2f); // 0.6s → 15フレーム目を通過
            Assert.AreEqual(1, vfxManager.ActiveCount, "Anim2DData の Frame イベントで VFX が出るはず(AnimData と同じ挙動であるべき)");
            Assert.AreEqual(1, audioManager.ActiveCount, "Anim2DData の Frame イベントで SE が鳴るはず(AnimData と同じ挙動であるべき)");
        }

        // U-20 の真因の再現テスト。`Anim2D.Play(Anim2DId, Animator)` は `AnimManager.Play` 経由で
        // `AssetRegistry.ResolveOrPlaceholder`(完全同期。未ロードなら自分ではロードしにいかず Placeholder を返すだけ)
        // でしか Anim2DData を解決しない。カタログ登録(RegisterCatalogAsync)は Flags.Load(既定 LazyLoad)の
        // アセットを自動ロードしないため、他の経路(ModelData.DefaultAnimation の依存解決等)で先にロードされて
        // いない Anim2DId を直接 Play すると、実データではなく空の Events を持つ Placeholder が再生される
        // (Placeholder は AnimManager.CreatePlaceholder が DisplayName だけ設定し、Events は null のまま)。
        // 2D キャラクターは ModelData(3D 専用)を経由しないため、この「他経路での先行ロード」が起きにくく、
        // 3D 側より顕在化しやすい。CanvasData(2026-09-12)/PresentationData・ShakeData・HapticsData(2026-09-14)
        // で見つかった「同期解決のみの種別は既定 Load を Preload にする」という既存の教訓が、Anim/Anim2D には
        // まだ適用されていなかった(AssetCreationService.cs のリスト漏れ)。
        [Test]
        public void Anim2D_IdPlay_WithoutPriorPreload_ResolvesPlaceholder_AndEventsDoNotFire()
        {
            var vfx = ScriptableObject.CreateInstance<VfxData>();
            vfx.Id = 31;
            vfx.Prefab = _vfxPrefab;
            vfx.LifeMode = VfxLifeMode.Loop;
            vfx.Anchor = new AnchorDef { Space = AnchorSpace.ContextTarget, LocalScale = Vector3.one };
            vfx.Flags = new AssetFlags { Load = LoadMode.Preload };

            var anim2D = ScriptableObject.CreateInstance<Anim2DData>();
            anim2D.Id = 30;
            anim2D.Clip = Clip();
            anim2D.Events = new[]
            {
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Action = EventAction.PlayAsset, Target = AssetRef.From(new VfxId(31, AssetType.Vfx)) },
            };
            anim2D.Flags = new AssetFlags { Load = LoadMode.LazyLoad }; // AssetCreationService の既定値(現状)を再現

            var loader = new FakeAssetLoader();
            loader.Assets["vfx/31"] = vfx;
            loader.Assets["anim2d/30"] = anim2D;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry>
            {
                new() { Id = vfx.Id, Type = AssetType.Vfx, Address = "vfx/31", Flags = vfx.Flags },
                new() { Id = anim2D.Id, Type = AssetType.Anim2D, Address = "anim2d/30", Flags = anim2D.Flags },
            });
            var registry = new AssetRegistry(loader);
            // Bootstrap 相当: カタログ登録だけ行う(Preload フラグのアセットだけが自動ロードされる)。
            // Anim2DId を「他のどこかで」明示的に ResolveAsync することは一切しない(実際のゲームで
            // Anim2DId をスクリプトのフィールドから直接 Play するケースを模す)。
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();

            var animManager = new AnimManager(registry);
            var vfxManager = new VfxManager(_pool, registry);
            using var dispatcher = new AssetEventDispatcher(animManager.Events, registry, null, vfxManager, animManager.GetContextTransform);
            Anim2D.Bind(animManager, registry);

            var h = Anim2D.Play(new Anim2DId(30, AssetType.Anim2D), _animator);
            Assert.IsTrue(Anim2D.IsPlaying(h), "Placeholder でも FallbackLengthSec の間は再生中扱いになる");

            animManager.Tick(1f); // Placeholder の FallbackLengthSec(0.5s)を優に超える
            Assert.AreEqual(0, vfxManager.ActiveCount,
                "真因の再現: Anim2DId が事前ロードされていないと ResolveOrPlaceholder が Placeholder(Events=null)を返すため、" +
                "Anim2DData に設定した Frame イベントの VFX が一切出ない");
        }
    }
}
