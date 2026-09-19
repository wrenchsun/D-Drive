using System.Collections.Generic;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AnchorGroupId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorGroupMarker>;

namespace DDrive.Tests.Runtime
{
    // [08_presentation.md] / [22_anchor_group.md] §5(Presentation 統合) — TrackKind.AnchorGroup が
    // AnchorGroupPlayer.PlayData に正しく委譲されること、StopOnCancel で止まること、groups 未設定なら
    // 警告 1 回 + no-op で継続することを検証する。実カタログ・実 GameData には触れない。
    public class PresentationAnchorGroupTests
    {
        private PoolService _pool;
        private FakeAssetLoader _loader;
        private AssetRegistry _registry;
        private VfxManager _vfx;
        private AnchorGroupPlayer _groups;
        private TimeService _time;
        private GameObject _vfxPrefab;
        private ulong _nextId = 950001;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _vfxPrefab = CreateParticlePrefab();
            _vfx = new VfxManager(_pool, _registry);
            _groups = new AnchorGroupPlayer(_registry, _vfx, null);
            _time = new TimeService();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_vfxPrefab);
        }

        private static GameObject CreateParticlePrefab()
        {
            var go = new GameObject("PresentationAnchorGroupTestVfxPrefab");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return go;
        }

        // AnchorGroupTests.Registry と同じ流儀の登録ヘルパー(1 つの VFX を共通アセットに持つ配置セット)。
        private AnchorGroupId RegisterGroupWithOneVfx()
        {
            var vfxId = _nextId++;
            var vfxAddress = $"vfx/{vfxId}";
            var vfxData = ScriptableObject.CreateInstance<VfxData>();
            vfxData.Id = vfxId;
            vfxData.Prefab = _vfxPrefab;
            vfxData.LifeMode = VfxLifeMode.Loop;

            var groupId = _nextId++;
            var groupAddress = $"anchorgroup/{groupId}";
            var group = ScriptableObject.CreateInstance<AnchorGroupData>();
            group.Id = groupId;
            group.Layout = AnchorLayoutKind.Manual;
            group.Points = new[] { new AnchorGroupPoint { LocalScale = Vector3.one } };
            group.SharedVfx = new[] { new AssetId<VfxMarker>(vfxId, AssetType.Vfx) };

            _loader.Assets[vfxAddress] = vfxData;
            _loader.Assets[groupAddress] = group;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry>
            {
                new() { Id = vfxId, Type = AssetType.Vfx, Address = vfxAddress },
                new() { Id = groupId, Type = AssetType.AnchorGroup, Address = groupAddress },
            });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<VfxData>(vfxId).GetAwaiter().GetResult();
            _registry.ResolveAsync<AnchorGroupData>(groupId).GetAwaiter().GetResult();

            return new AnchorGroupId(groupId, AssetType.AnchorGroup);
        }

        private static PresentationData BuildData(AnchorGroupId groupId, bool stopOnCancel, bool interruptible = true)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Tracks = new[]
            {
                new PresentationTrack
                {
                    Trigger = TrackTrigger.AtTime,
                    Time = 0f,
                    Kind = TrackKind.AnchorGroup,
                    Asset = AssetRef.From(groupId),
                    StopOnCancel = stopOnCancel,
                },
            };
            data.TotalDuration = 5f;
            data.Interruptible = interruptible;
            return data;
        }

        [Test]
        public void AnchorGroupTrack_FiresAnchorGroupPlayer_PlayData()
        {
            var groupId = RegisterGroupWithOneVfx();
            var manager = new PresentationManager(_registry, _time, groups: _groups);
            var data = BuildData(groupId, stopOnCancel: false);

            manager.PlayData(data, new PlayContext());

            Assert.AreEqual(1, _groups.ActiveCount, "AnchorGroup トラックの発火で AnchorGroupPlayer.PlayData が実際に呼ばれ、1 グループが再生中になっていること");

            Object.DestroyImmediate(data);
        }

        [Test]
        public void AnchorGroupTrack_StopOnCancel_StopsGroupOnCancel()
        {
            var groupId = RegisterGroupWithOneVfx();
            var manager = new PresentationManager(_registry, _time, groups: _groups);
            var data = BuildData(groupId, stopOnCancel: true);

            var handle = manager.PlayData(data, new PlayContext());
            Assert.AreEqual(1, _groups.ActiveCount);

            manager.Cancel(handle);

            Assert.AreEqual(0, _groups.ActiveCount, "StopOnCancel=true のトラックは Cancel() で AnchorGroupPlayer.Stop が呼ばれ、台帳から外れること");

            Object.DestroyImmediate(data);
        }

        [Test]
        public void AnchorGroupTrack_StopOnCancelFalse_KeepsPlayingAfterCancel()
        {
            var groupId = RegisterGroupWithOneVfx();
            var manager = new PresentationManager(_registry, _time, groups: _groups);
            var data = BuildData(groupId, stopOnCancel: false);

            var handle = manager.PlayData(data, new PlayContext());
            Assert.AreEqual(1, _groups.ActiveCount);

            manager.Cancel(handle);

            Assert.AreEqual(1, _groups.ActiveCount, "StopOnCancel=false のトラックは Vfx/Se と同じく Cancel() の対象外(既存の FiredVfx 等と同じ規則)");

            Object.DestroyImmediate(data);
        }

        [Test]
        public void AnchorGroupTrack_GroupsNotConfigured_WarnsOnce_AndNoOps()
        {
            var groupId = RegisterGroupWithOneVfx();
            // groups を渡さない(既定 null = 未配線)。
            var manager = new PresentationManager(_registry, _time);
            var data = BuildData(groupId, stopOnCancel: false);

            LogAssert.Expect(LogType.Warning, new Regex(".*AnchorGroup.*Manager.*未設定.*"));

            Assert.DoesNotThrow(() => manager.PlayData(data, new PlayContext()));
            Assert.AreEqual(0, _groups.ActiveCount, "groups が未配線でも例外にせず no-op で継続する(共有している _groups 自体は呼ばれない)");

            Object.DestroyImmediate(data);
        }
    }
}
