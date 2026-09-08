using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using AnchorGroupId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorGroupMarker>;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Tests.Runtime
{
    // [22_anchor_group.md] — パターン生成・再生計画・再生・Validation。
    public class AnchorGroupTests
    {
        private readonly AnchorLayoutPoint[] _buffer = new AnchorLayoutPoint[AnchorGroupData.MaxPoints];
        private PoolService _pool;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _prefab = new GameObject("GroupVfxPrefab");
            var ps = _prefab.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
        }

        private static AnchorGroupData Grid(int x, int z, float spacing = 1f, ulong id = 100)
        {
            var g = ScriptableObject.CreateInstance<AnchorGroupData>();
            g.Id = id;
            g.name = $"Group_{id}";
            g.Layout = AnchorLayoutKind.Grid;
            g.GridCountX = x;
            g.GridCountY = 1;
            g.GridCountZ = z;
            g.GridSpacing = Vector3.one * spacing;
            g.GridCentered = true;
            return g;
        }

        private static AssetRegistry Registry(params AssetDataBase[] assets)
        {
            var loader = new FakeAssetLoader();
            var entries = new List<CatalogEntry>();
            foreach (var a in assets)
            {
                var type = a is AnchorGroupData ? AssetType.AnchorGroup : a is AnchorData ? AssetType.Anchor : a is VfxData ? AssetType.Vfx : AssetType.Se;
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
                    case AnchorGroupData: registry.ResolveAsync<AnchorGroupData>(a.Id).GetAwaiter().GetResult(); break;
                    case AnchorData: registry.ResolveAsync<AnchorData>(a.Id).GetAwaiter().GetResult(); break;
                    case VfxData: registry.ResolveAsync<VfxData>(a.Id).GetAwaiter().GetResult(); break;
                }
            }

            return registry;
        }

        private VfxData Vfx(ulong id)
        {
            var v = ScriptableObject.CreateInstance<VfxData>();
            v.Id = id;
            v.Prefab = _prefab;
            v.LifeMode = VfxLifeMode.Loop;
            return v;
        }

        // ── AnchorLayout ──

        [Test]
        public void Grid_3x3_Centered_HasNinePointsAroundOrigin()
        {
            var count = AnchorLayout.Generate(Grid(3, 3), _buffer, sampleRandom: false);

            Assert.AreEqual(9, count);
            Assert.Less(Vector3.Distance(new Vector3(-1f, 0f, -1f), _buffer[0].LocalOffset), 1e-4f, "先頭は角");
            Assert.Less(Vector3.Distance(Vector3.zero, _buffer[4].LocalOffset), 1e-4f, "中央は原点");
            Assert.Less(Vector3.Distance(new Vector3(1f, 0f, 1f), _buffer[8].LocalOffset), 1e-4f);
        }

        [Test]
        public void Grid_NotCentered_StartsAtOrigin()
        {
            var g = Grid(2, 2);
            g.GridCentered = false;
            AnchorLayout.Generate(g, _buffer, false);
            Assert.AreEqual(Vector3.zero, _buffer[0].LocalOffset);
            Assert.Less(Vector3.Distance(new Vector3(1f, 0f, 1f), _buffer[3].LocalOffset), 1e-4f);
        }

        [Test]
        public void Circle_FourPoints_OnRadius_FacingOutward()
        {
            var g = ScriptableObject.CreateInstance<AnchorGroupData>();
            g.Layout = AnchorLayoutKind.Circle;
            g.CircleCount = 4;
            g.CircleRadius = 2f;
            g.CircleFaceOutward = true;

            var count = AnchorLayout.Generate(g, _buffer, false);

            Assert.AreEqual(4, count);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 2f), _buffer[0].LocalOffset), 1e-4f);
            Assert.Less(Vector3.Distance(new Vector3(2f, 0f, 0f), _buffer[1].LocalOffset), 1e-4f);
            Assert.Less(Mathf.DeltaAngle(90f, _buffer[1].LocalEuler.y), 1e-3f, "外向き");
        }

        [Test]
        public void Line_Centered_SpansLength()
        {
            var g = ScriptableObject.CreateInstance<AnchorGroupData>();
            g.Layout = AnchorLayoutKind.Line;
            g.LineCount = 3;
            g.LineLength = 2f;
            g.LineDirection = Vector3.right;

            var count = AnchorLayout.Generate(g, _buffer, false);

            Assert.AreEqual(3, count);
            Assert.Less(Vector3.Distance(new Vector3(-1f, 0f, 0f), _buffer[0].LocalOffset), 1e-4f);
            Assert.Less(Vector3.Distance(new Vector3(1f, 0f, 0f), _buffer[2].LocalOffset), 1e-4f);
        }

        [Test]
        public void Random_WithSeed_IsDeterministic_AndWithinRadius()
        {
            var g = ScriptableObject.CreateInstance<AnchorGroupData>();
            g.Layout = AnchorLayoutKind.Random;
            g.RandomCount = 5;
            g.RandomRadius = 0.5f;
            g.RandomSeed = 42;

            AnchorLayout.Generate(g, _buffer, true);
            var first = _buffer[3].LocalOffset;
            AnchorLayout.Generate(g, _buffer, true);

            Assert.AreEqual(first, _buffer[3].LocalOffset);
            for (var i = 0; i < 5; i++)
            {
                Assert.LessOrEqual(_buffer[i].LocalOffset.magnitude, 0.5f + 1e-4f);
            }
        }

        [Test]
        public void ManualPoints_AreAppendedAfterPattern()
        {
            var g = Grid(2, 1);
            g.Points = new[] { new AnchorGroupPoint { Name = "Extra", LocalOffset = new Vector3(0f, 5f, 0f) } };

            var count = AnchorLayout.Generate(g, _buffer, false);

            Assert.AreEqual(3, count);
            Assert.AreEqual(new Vector3(0f, 5f, 0f), _buffer[2].LocalOffset);
            Assert.AreEqual(2, _buffer[2].Index);
        }

        [Test]
        public void ComposePoint_AppliesOriginRotation_AndStagger()
        {
            var g = Grid(1, 1);
            g.DelayPerIndex = 0.1f;
            var origin = AnchorSpawnSpec.FromDef(new AnchorDef { LocalOffset = new Vector3(1f, 0f, 0f), LocalEuler = new Vector3(0f, 90f, 0f), LocalScale = Vector3.one });
            var point = new AnchorLayoutPoint { Index = 3, LocalOffset = new Vector3(0f, 0f, 1f), LocalScale = Vector3.one };

            var spec = AnchorLayout.ComposePoint(origin, point, g, sampleRandom: false);

            Assert.Less(Vector3.Distance(new Vector3(2f, 0f, 0f), spec.Def.LocalOffset), 1e-4f);
            Assert.AreEqual(0.3f, spec.DelaySec, 1e-5f);
        }

        // ── AnchorGroupPlanner ──

        [Test]
        public void Plan_SharedVfx_ProducesOneActionPerPoint()
        {
            var g = Grid(3, 3);
            g.SharedVfx = new[] { new VfxId(1, AssetType.Vfx) };
            var actions = new List<AnchorGroupAction>();

            var count = AnchorGroupPlanner.Plan(Registry(g), g, sampleRandom: false, actions);

            Assert.AreEqual(9, count);
            Assert.AreEqual(9, actions.Count);
            Assert.AreEqual(4, actions[4].PointIndex);
            Assert.Less(Vector3.Distance(Vector3.zero, actions[4].Spec.Def.LocalOffset), 1e-4f);
        }

        [Test]
        public void Plan_OverrideSkipAndReplace()
        {
            var g = Grid(3, 3);
            g.SharedVfx = new[] { new VfxId(1, AssetType.Vfx) };
            g.Overrides = new[]
            {
                new AnchorGroupOverride { Index = 0, Skip = true },
                new AnchorGroupOverride { Index = 4, Vfx = new[] { new VfxId(2, AssetType.Vfx) } },
            };
            var actions = new List<AnchorGroupAction>();

            AnchorGroupPlanner.Plan(Registry(g), g, false, actions);

            Assert.AreEqual(8, actions.Count, "index 0 は出さない");
            var center = actions.Find(a => a.PointIndex == 4);
            Assert.AreEqual(2UL, center.AssetId, "中央は差し替え");
        }

        [Test]
        public void Plan_ChildGroup_AtOnePoint_AddsNestedActions()
        {
            var parent = Grid(2, 1, spacing: 2f, id: 100);
            parent.SharedVfx = new[] { new VfxId(1, AssetType.Vfx) };
            var child = ScriptableObject.CreateInstance<AnchorGroupData>();
            child.Id = 200;
            child.name = "Child";
            child.Layout = AnchorLayoutKind.Line;
            child.LineCount = 2;
            child.LineLength = 1f;
            child.LineDirection = Vector3.up;
            child.LineCentered = false;
            child.Origin = new AnchorDef { Space = AnchorSpace.BoneName, Path = "Ignored", LocalOffset = new Vector3(99f, 0f, 0f), LocalScale = Vector3.one };
            child.SharedVfx = new[] { new VfxId(3, AssetType.Vfx) };
            parent.Children = new[] { new AnchorGroupChild { Group = new AnchorGroupId(200, AssetType.AnchorGroup), AtIndex = 1 } };
            var actions = new List<AnchorGroupAction>();

            AnchorGroupPlanner.Plan(Registry(parent, child), parent, false, actions);

            Assert.AreEqual(4, actions.Count, "親 2 点 + 子 2 点");
            var nested = actions.FindAll(a => a.Depth == 1);
            Assert.AreEqual(2, nested.Count);
            Assert.AreEqual(1, nested[0].PointIndex, "子は親の点番号を引き継ぐ");
            // 親の点 1 は x=+1。子は原点を無視してそこから上へ並ぶ。
            Assert.Less(Vector3.Distance(new Vector3(1f, 0f, 0f), nested[0].Spec.Def.LocalOffset), 1e-4f);
            Assert.Less(Vector3.Distance(new Vector3(1f, 1f, 0f), nested[1].Spec.Def.LocalOffset), 1e-4f);
        }

        [Test]
        public void Plan_OriginAnchorAsset_IsUsedAsFrame()
        {
            var anchor = AnchorChainTestRegistry.Anchor(50, offset: new Vector3(0f, 2f, 0f));
            var g = Grid(1, 1);
            g.OriginAnchorId = AnchorChainTestRegistry.Id(50);
            g.SharedVfx = new[] { new VfxId(1, AssetType.Vfx) };
            var actions = new List<AnchorGroupAction>();

            AnchorGroupPlanner.Plan(Registry(g, anchor), g, false, actions);

            Assert.AreEqual(1, actions.Count);
            Assert.Less(Vector3.Distance(new Vector3(0f, 2f, 0f), actions[0].Spec.Def.LocalOffset), 1e-4f);
        }

        // ── AnchorGroupPlayer ──

        [Test]
        public void Player_Play_SpawnsAllPoints_AndKillStopsThem()
        {
            var g = Grid(3, 3);
            g.SharedVfx = new[] { new VfxId(1, AssetType.Vfx) };
            var vfxData = Vfx(1);
            var registry = Registry(g, vfxData);
            var vfxManager = new VfxManager(_pool, registry);
            var player = new AnchorGroupPlayer(registry, vfxManager, null);

            var handle = player.Play(new AnchorGroupId(100, AssetType.AnchorGroup));

            Assert.IsTrue(player.IsPlaying(handle));
            Assert.AreEqual(9, vfxManager.ActiveCount);
            Assert.AreEqual(1, player.ActiveCount);

            player.Kill(handle);
            Assert.IsFalse(player.IsPlaying(handle));
            Assert.AreEqual(0, vfxManager.ActiveCount);
            Assert.AreEqual(0, player.ActiveCount);
        }

        [Test]
        public void Player_Tick_ReleasesFinishedGroups()
        {
            var g = Grid(2, 1);
            g.SharedVfx = new[] { new VfxId(1, AssetType.Vfx) };
            var registry = Registry(g, Vfx(1));
            var vfxManager = new VfxManager(_pool, registry);
            var player = new AnchorGroupPlayer(registry, vfxManager, null);

            var handle = player.Play(new AnchorGroupId(100, AssetType.AnchorGroup));
            vfxManager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            player.Tick();

            Assert.AreEqual(0, player.ActiveCount);
            Assert.IsFalse(player.IsPlaying(handle));
        }

        [Test]
        public void Facade_Unbound_IsNoOp()
        {
            Anchors.Bind(null);
            var h = Anchors.Play(new AnchorGroupId(1, AssetType.AnchorGroup));
            Assert.IsFalse(Anchors.IsPlaying(h));
        }

        // ── Validator ──

        [Test]
        public void Validator_NoAssets_IsWarning_AndSelfChild_IsError()
        {
            var g = Grid(2, 2);
            g.Children = new[] { new AnchorGroupChild { Group = new AnchorGroupId(100, AssetType.AnchorGroup), AtIndex = -1 } };
            var results = new List<ValidationResult>(new AnchorGroupDataValidator().Validate(g, new ValidationContext(new List<AssetDataBase> { g })));

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("自分自身")));
        }

        [Test]
        public void Validator_ZeroPoints_IsError()
        {
            var g = ScriptableObject.CreateInstance<AnchorGroupData>();
            g.Layout = AnchorLayoutKind.Manual;
            var results = new List<ValidationResult>(new AnchorGroupDataValidator().Validate(g, new ValidationContext(new List<AssetDataBase> { g })));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("点が 1 つも")));
        }
    }
}
