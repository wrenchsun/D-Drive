using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Net;
using DDrive.Runtime.Prefab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // 4-13: NetMode.Simulated の Prefab はサーバー権威で生成する。クライアントは要求を送るだけで
    // ローカル Instantiate しない([14_networking.md] §3/§4/§10)。
    public class PrefabSimulatedSpawnTests
    {
        private PoolService _pool;
        private AssetRegistry _registry;
        private FakeAssetLoader _loader;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _prefab = new GameObject("SimulatedTestPrefab");
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
        }

        private PrefabData CreateSimulatedData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Id = id;
            data.Prefab = _prefab;
            data.Flags.Net = NetMode.Simulated;
            return data;
        }

        [Test]
        public void ServerSpawn_Simulated_InstantiatesLocally_AndBroadcastsSpawned()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true };
            var manager = new PrefabsManager(_pool, _registry, netBridge: bridge);
            var data = CreateSimulatedData(1);

            var handle = manager.SpawnData(data, Vector3.one, Quaternion.identity);

            Assert.AreNotEqual(Handle<PrefabMarker>.Invalid, handle);
            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(1, bridge.BroadcastCount);
            Assert.IsInstanceOf<PrefabSpawnedMsg>(bridge.LastMessage);
        }

        [Test]
        public void ServerDespawn_Simulated_BroadcastsDespawned()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true };
            var manager = new PrefabsManager(_pool, _registry, netBridge: bridge);
            var data = CreateSimulatedData(1);
            var handle = manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            manager.Despawn(handle);

            Assert.AreEqual(2, bridge.BroadcastCount); // Spawned + Despawned
            Assert.IsInstanceOf<PrefabDespawnedMsg>(bridge.LastMessage);
        }

        [Test]
        public void ClientSpawn_Simulated_DoesNotInstantiate_AndSendsRequest()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true };
            var manager = new PrefabsManager(_pool, _registry, netBridge: bridge);
            var data = CreateSimulatedData(1);

            var handle = manager.SpawnData(data, new Vector3(1f, 2f, 3f), Quaternion.identity);

            Assert.AreEqual(Handle<PrefabMarker>.Invalid, handle);
            Assert.AreEqual(0, manager.ActiveCount);
            Assert.AreEqual(1, bridge.SendToCount);
            Assert.IsInstanceOf<PrefabSpawnRequestMsg>(bridge.LastMessage);
            Assert.AreEqual(1UL, ((PrefabSpawnRequestMsg)bridge.LastMessage).PrefabId);
        }

        [Test]
        public void ClientSpawn_NonSimulated_StillSpawnsLocally()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true };
            var manager = new PrefabsManager(_pool, _registry, netBridge: bridge);
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Id = 2;
            data.Prefab = _prefab;
            data.Flags.Net = NetMode.Local;

            var handle = manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.AreNotEqual(Handle<PrefabMarker>.Invalid, handle);
            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(0, bridge.SendToCount);
            Assert.AreEqual(0, bridge.BroadcastCount);
        }

        [Test]
        public void Server_ReceivesRequest_ResolvesFromRegistry_AndSpawnsOnce()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true };
            var manager = new PrefabsManager(_pool, _registry, netBridge: bridge);
            var data = CreateSimulatedData(5);
            RegisterAndCache(data);

            // clientId=3 から Spawn 要求を受け取ったことを模擬する。
            bridge.SendTo(3UL, new PrefabSpawnRequestMsg { PrefabId = 5, Position = Vector3.zero, Rotation = Quaternion.identity }, NetChannel.ReliableOrdered);

            Assert.AreEqual(1, manager.ActiveCount);
        }

        [Test]
        public void Server_IgnoresRequest_ForNonSimulatedPrefab()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true };
            var manager = new PrefabsManager(_pool, _registry, netBridge: bridge);
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Id = 6;
            data.Prefab = _prefab;
            data.Flags.Net = NetMode.Local;
            RegisterAndCache(data);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Simulated ではない.*"));
            bridge.SendTo(3UL, new PrefabSpawnRequestMsg { PrefabId = 6, Position = Vector3.zero, Rotation = Quaternion.identity }, NetChannel.ReliableOrdered);

            Assert.AreEqual(0, manager.ActiveCount);
        }

        [Test]
        public void Server_RateLimitsRequests_PerClient_60PerSecond()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true };
            var manager = new PrefabsManager(_pool, _registry, netBridge: bridge);
            var data = CreateSimulatedData(7);
            RegisterAndCache(data);

            LogAssert.ignoreFailingMessages = true;
            for (var i = 0; i < 61; i++)
            {
                bridge.SendTo(9UL, new PrefabSpawnRequestMsg { PrefabId = 7, Position = Vector3.zero, Rotation = Quaternion.identity }, NetChannel.ReliableOrdered);
            }
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(60, manager.ActiveCount);
        }

        private void RegisterAndCache(PrefabData data)
        {
            var address = "prefab/" + data.Id;
            _loader.Assets[address] = data;

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new System.Collections.Generic.List<CatalogEntry> { new() { Id = data.Id, Type = AssetType.Prefab, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<PrefabData>(data.Id).GetAwaiter().GetResult();
        }
    }

    // [14_networking.md] §10(4-13) — Validator 側の Simulated 検証。
    public class PrefabDataValidatorSimulatedTests
    {
        private GameObject _prefabWithoutNetworkObject;

        [TearDown]
        public void TearDown()
        {
            if (_prefabWithoutNetworkObject != null)
            {
                Object.DestroyImmediate(_prefabWithoutNetworkObject);
            }
        }

        // [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) — NetworkObject の検査自体は
        // DDrive.Runtime.Ngo アセンブリの `PrefabNetworkObjectValidator`(defineConstraints=DDRIVE_NGO)へ
        // 切り出したため、`PrefabDataValidator` 単体は NGO の有無に関わらず NetworkObject 関連の結果を
        // 一切出さない(NGO ありのときの「NetworkObject が無ければ Error」の検証は
        // Tests/Runtime/Ngo/PrefabNetworkObjectValidatorTests.cs 側で行う)。
        [Test]
        public void Simulated_WithoutNetworkObject_DoesNotReportNetworkObjectItself()
        {
            _prefabWithoutNetworkObject = new GameObject("NoNetworkObject");
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Prefab = _prefabWithoutNetworkObject;
            data.Kind = PrefabKind.Projectile;
            data.Flags.Net = NetMode.Simulated;

            var results = new System.Collections.Generic.List<DDrive.Foundation.Validation.ValidationResult>(
                new PrefabDataValidator().Validate(data, new DDrive.Foundation.Validation.ValidationContext(new System.Collections.Generic.List<AssetDataBase> { data })));

            Assert.IsFalse(results.Exists(r => r.Message.Contains("NetworkObject")), "NetworkObject 検査は DDrive.Runtime.Ngo アセンブリ側の責務(PrefabNetworkObjectValidator)");
        }

        [Test]
        public void Simulated_WithUnexpectedKind_IsInfo()
        {
            _prefabWithoutNetworkObject = new GameObject("NoNetworkObject2");
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Prefab = _prefabWithoutNetworkObject;
            data.Kind = PrefabKind.Pickup; // Projectile/Gimmick/Character 以外
            data.Flags.Net = NetMode.Simulated;

            var results = new System.Collections.Generic.List<DDrive.Foundation.Validation.ValidationResult>(
                new PrefabDataValidator().Validate(data, new DDrive.Foundation.Validation.ValidationContext(new System.Collections.Generic.List<AssetDataBase> { data })));

            Assert.IsTrue(results.Exists(r => r.Severity == DDrive.Foundation.Validation.ValidationSeverity.Info));
        }

        [Test]
        public void Simulated_WithPooledPolicy_IsWarning()
        {
            _prefabWithoutNetworkObject = new GameObject("NoNetworkObject3");
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Prefab = _prefabWithoutNetworkObject;
            data.Kind = PrefabKind.Projectile;
            data.Flags.Net = NetMode.Simulated;
            data.Flags.Pool = PoolPolicy.Pooled(0, 8);

            var results = new System.Collections.Generic.List<DDrive.Foundation.Validation.ValidationResult>(
                new PrefabDataValidator().Validate(data, new DDrive.Foundation.Validation.ValidationContext(new System.Collections.Generic.List<AssetDataBase> { data })));

            Assert.IsTrue(results.Exists(r => r.Severity == DDrive.Foundation.Validation.ValidationSeverity.Warning && r.Message.Contains("併用注意")));
        }
    }
}
