using DDrive.Foundation.Data;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [14_networking.md] §3/§4/§8, [11_tasks.md] 2-8。
    // 実際の2端末確認は Multiplayer Play Mode での手動確認が必要(NetBridgeSmokeTest と同じ理由)だが、
    // 「同じ INetBridge を共有する2つの独立した Manager インスタンス」を立てることで、
    // Broadcast → 両方の受信ハンドラが発火 → 両方で実際に再生される、という
    // Cosmetic 配送の核となる仕組みを EditMode で機械的に検証できる。
    public class CosmeticDeliveryTests
    {
        private LocalLoopbackBridge _sharedBridge;

        private GameObject _sePrefabA;
        private GameObject _sePrefabB;
        private PoolService _sePoolA;
        private PoolService _sePoolB;
        private AudioManager _seClientA;
        private AudioManager _seClientB;

        private PoolService _vfxPoolA;
        private PoolService _vfxPoolB;
        private GameObject _vfxPrefab;
        private VfxManager _vfxClientA;
        private VfxManager _vfxClientB;

        [SetUp]
        public void SetUp()
        {
            _sharedBridge = new LocalLoopbackBridge();

            _sePrefabA = new GameObject("SeSourceA");
            _sePrefabA.AddComponent<AudioSource>();
            _sePrefabB = new GameObject("SeSourceB");
            _sePrefabB.AddComponent<AudioSource>();
            _sePoolA = new PoolService();
            _sePoolB = new PoolService();
            _seClientA = new AudioManager(_sePoolA, new AssetRegistry(new FakeAssetLoader()), _sePrefabA, _sharedBridge);
            _seClientB = new AudioManager(_sePoolB, new AssetRegistry(new FakeAssetLoader()), _sePrefabB, _sharedBridge);

            _vfxPrefab = new GameObject("VfxPrefab");
            _vfxPrefab.AddComponent<ParticleSystem>();
            _vfxPoolA = new PoolService();
            _vfxPoolB = new PoolService();
            _vfxClientA = new VfxManager(_vfxPoolA, new AssetRegistry(new FakeAssetLoader()), _sharedBridge);
            _vfxClientB = new VfxManager(_vfxPoolB, new AssetRegistry(new FakeAssetLoader()), _sharedBridge);
        }

        [TearDown]
        public void TearDown()
        {
            _sePoolA.Clear(PoolScope.Global);
            _sePoolB.Clear(PoolScope.Global);
            Object.DestroyImmediate(_sePrefabA);
            Object.DestroyImmediate(_sePrefabB);

            _vfxPoolA.Clear(PoolScope.Global);
            _vfxPoolB.Clear(PoolScope.Global);
            Object.DestroyImmediate(_vfxPrefab);
        }

        private static SeData CosmeticSeData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            data.Id = id;
            data.Clips = new[] { AudioClip.Create("cosmetic", 4410, 1, 44100, false) };
            data.Volume = 1f;
            data.MaxConcurrent = 8;
            data.Flags = new AssetFlags { Net = NetMode.Cosmetic };
            return data;
        }

        [Test]
        public void CosmeticSe_DoesNotPlayImmediately_ReturnsInvalidHandle()
        {
            var handle = _seClientA.PlaySeData(CosmeticSeData(1));
            Assert.IsFalse(_seClientA.IsPlaying(handle));
        }

        [Test]
        public void CosmeticSe_AfterTick_PlaysOnSenderAndOtherClient()
        {
            _seClientA.PlaySeData(CosmeticSeData(1));

            // Broadcast は Tick でまとめて送出される(バッチ)。
            _seClientA.Tick(0.016f);
            _seClientB.Tick(0.016f);

            // 送信元(A)自身も loopback で自分の Broadcast を受け取って初めて鳴る(直接鳴らしていない)。
            Assert.IsTrue(_seClientA.ActiveCount > 0, "sender should also play via its own loopback receive");
            Assert.IsTrue(_seClientB.ActiveCount > 0, "other client should play the same Cosmetic SE");
        }

        [Test]
        public void LocalSe_StillPlaysImmediately_NoRegressionFromCosmeticRouting()
        {
            var data = CosmeticSeData(2);
            data.Flags = new AssetFlags { Net = NetMode.Local };

            var handle = _seClientA.PlaySeData(data);
            Assert.IsTrue(_seClientA.IsPlaying(handle));
        }

        [Test]
        public void CosmeticSe_MultiplePlaysInSameTick_AreSentAsOneBatch()
        {
            var bridge = new CountingNetBridge();
            var pool = new PoolService();
            var prefab = new GameObject("CountingSource");
            prefab.AddComponent<AudioSource>();
            var manager = new AudioManager(pool, new AssetRegistry(new FakeAssetLoader()), prefab, bridge);

            manager.PlaySeData(CosmeticSeData(1));
            manager.PlaySeData(CosmeticSeData(2));
            manager.PlaySeData(CosmeticSeData(3));

            manager.Tick(0.016f);

            Assert.AreEqual(1, bridge.BroadcastCount, "3 plays in the same tick must collapse into 1 batched broadcast");

            pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(prefab);
        }

        private static VfxData CosmeticVfxData(ulong id, GameObject prefab)
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = id;
            data.Prefab = prefab;
            data.LifeMode = VfxLifeMode.Loop;
            data.Flags = new AssetFlags { Net = NetMode.Cosmetic };
            return data;
        }

        [Test]
        public void CosmeticVfx_DoesNotSpawnImmediately_ReturnsInvalidHandle()
        {
            var handle = _vfxClientA.SpawnData(CosmeticVfxData(10, _vfxPrefab));
            Assert.IsFalse(_vfxClientA.IsPlaying(handle));
        }

        [Test]
        public void CosmeticVfx_AfterTick_SpawnsOnSenderAndOtherClient()
        {
            _vfxClientA.SpawnData(CosmeticVfxData(10, _vfxPrefab));

            _vfxClientA.Tick(0.016f);
            _vfxClientB.Tick(0.016f);

            Assert.IsTrue(_vfxClientA.ActiveCount > 0, "sender should also spawn via its own loopback receive");
            Assert.IsTrue(_vfxClientB.ActiveCount > 0, "other client should spawn the same Cosmetic VFX");
        }
    }
}
