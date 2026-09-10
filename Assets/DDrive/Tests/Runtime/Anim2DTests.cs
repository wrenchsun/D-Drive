using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using NUnit.Framework;
using UnityEngine;
using Anim2DId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim2D.Anim2DMarker>;

namespace DDrive.Tests.Runtime
{
    // [05_model_animation.md] C-3 / C-4 / C-6 — 3-12: Anim2DData(AnimData 派生)の再生・方向パラメータ・Validator。
    public class Anim2DTests
    {
        private GameObject _actor;
        private Animator _animator;
        private readonly List<Object> _cleanup = new();

        [SetUp]
        public void SetUp()
        {
            _actor = new GameObject("Actor2D");
            _animator = _actor.AddComponent<Animator>();
        }

        [TearDown]
        public void TearDown()
        {
            Anim2D.Bind(null, null);
            Object.DestroyImmediate(_actor);
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
        }

        private Anim2DData Data(ulong id, DirectionSet directions = DirectionSet.None)
        {
            var clip = new AnimationClip { frameRate = 12f };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var data = ScriptableObject.CreateInstance<Anim2DData>();
            data.Id = id;
            data.DisplayName = $"Anim2D{id}";
            data.Clip = clip;
            data.Directions = directions;
            _cleanup.Add(data);
            _cleanup.Add(clip);
            return data;
        }

        private static AssetRegistry Registry(params AssetDataBase[] assets)
        {
            var loader = new FakeAssetLoader();
            var entries = new List<CatalogEntry>();
            foreach (var a in assets)
            {
                var address = $"anim2d/{a.Id}";
                loader.Assets[address] = a;
                entries.Add(new CatalogEntry { Id = a.Id, Type = AssetType.Anim2D, Address = address });
            }

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(entries);
            var registry = new AssetRegistry(loader);
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            foreach (var a in assets)
            {
                registry.ResolveAsync<AnimData>(a.Id).GetAwaiter().GetResult();
            }

            return registry;
        }

        [Test]
        public void Play_ThroughFacade_TracksTimeWithAnimManager()
        {
            var data = Data(1);
            var registry = Registry(data);
            var manager = new AnimManager(registry);
            Anim2D.Bind(manager, registry);

            var h = Anim2D.Play(new Anim2DId(1, AssetType.Anim2D), _animator);
            Assert.IsTrue(Anim2D.IsPlaying(h), "Anim2DData は AnimData の派生として AnimManager で再生される");
            manager.Tick(0.5f);
            Assert.AreEqual(0.5f, manager.GetNormalizedTime(h), 1e-3f);
            Anim2D.Stop(h);
            Assert.IsFalse(Anim2D.IsPlaying(h));
        }

#if UNITY_EDITOR
        [Test]
        public void Play_WithDirection_SetsBlendTreeParameters()
        {
            var data = Data(2, DirectionSet.Eight);
            var registry = Registry(data);
            Anim2D.Bind(new AnimManager(registry), registry);

            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddParameter("x", AnimatorControllerParameterType.Float);
            controller.AddParameter("y", AnimatorControllerParameterType.Float);
            controller.AddLayer("Base");
            _animator.runtimeAnimatorController = controller;
            _cleanup.Add(controller);

            Anim2D.Play(new Anim2DId(2, AssetType.Anim2D), _animator, new Vector2(3f, 4f));
            Assert.AreEqual(0.6f, _animator.GetFloat("x"), 1e-3f, "正規化して x に入る");
            Assert.AreEqual(0.8f, _animator.GetFloat("y"), 1e-3f);

            Anim2D.SetDirection(_animator, Vector2.zero);
            Assert.AreEqual(0.6f, _animator.GetFloat("x"), 1e-3f, "0 ベクトルは方向を変えない");

            Anim2D.SetDirection(_animator, new Vector2(-1f, 0f));
            Assert.AreEqual(-1f, _animator.GetFloat("x"), 1e-3f);
        }
#endif

        [Test]
        public void Facade_Unbound_IsNoOp()
        {
            Anim2D.Bind(null, null);
            var h = Anim2D.Play(new Anim2DId(1, AssetType.Anim2D), _animator);
            Assert.IsFalse(Anim2D.IsPlaying(h));
            Assert.DoesNotThrow(() => Anim2D.SetDirection(_animator, Vector2.up));
        }

        [Test]
        public void Validator_FlagsMissingDirectionClips_AndUnusedOnes()
        {
            var eight = Data(3, DirectionSet.Eight);
            eight.DirectionClips = new AnimationClip[3];
            var results = new List<ValidationResult>(new Anim2DDataValidator().Validate(eight, new ValidationContext(new List<AssetDataBase> { eight })));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("DirectionClips")));

            var none = Data(4);
            none.DirectionClips = new[] { none.Clip };
            results = new List<ValidationResult>(new Anim2DDataValidator().Validate(none, new ValidationContext(new List<AssetDataBase> { none })));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning));

            var missingClip = ScriptableObject.CreateInstance<Anim2DData>();
            _cleanup.Add(missingClip);
            results = new List<ValidationResult>(new Anim2DDataValidator().Validate(missingClip, new ValidationContext(new List<AssetDataBase> { missingClip })));
            Assert.IsTrue(results.Exists(r => r.Message.Contains("Clip")));
        }
    }
}
