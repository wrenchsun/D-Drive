using System.Collections;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;

namespace DDrive.Tests.Runtime
{
    public class AudioManagerTests
    {
        private GameObject _sourcePrefab;
        private PoolService _pool;
        private AssetRegistry _registry;
        private AudioManager _manager;

        [SetUp]
        public void SetUp()
        {
            _sourcePrefab = new GameObject("SeSourcePrefab");
            _sourcePrefab.AddComponent<AudioSource>();
            _pool = new PoolService();
            _registry = new AssetRegistry(new FakeAssetLoader());
            _manager = new AudioManager(_pool, _registry, _sourcePrefab);
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_sourcePrefab);
        }

        private static AudioClip CreateClip(float seconds = 0.2f)
        {
            return AudioClip.Create("TestClip", (int)(44100 * seconds), 1, 44100, false);
        }

        private static SeData CreateSeData(ulong id, AudioClip clip = null, int maxConcurrent = 8, float cooldown = 0f)
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            data.Id = id;
            data.Clips = new[] { clip != null ? clip : CreateClip() };
            data.SelectMode = ClipSelectMode.First;
            data.Volume = 1f;
            data.MaxConcurrent = maxConcurrent;
            data.CooldownSec = cooldown;
            return data;
        }

        [Test]
        public void PlaySeData_StartsPlayback()
        {
            var data = CreateSeData(1);
            var handle = _manager.PlaySeData(data);

            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [Test]
        public void PlaySeData_WithStartOffset_BeginsPastOffset()
        {
            var data = CreateSeData(1, CreateClip(2f));
            data.StartOffsetSec = 1f;

            var handle = _manager.PlaySeData(data);

            Assert.IsTrue(_manager.IsPlaying(handle));
            var time = _manager.GetTime(handle);
            Assert.IsTrue(time.HasValue);
            Assert.GreaterOrEqual(time.Value, 0.9f);
        }

        [Test]
        public void Stop_StopsPlaybackAndReturnsToPool()
        {
            var data = CreateSeData(1);
            var handle = _manager.PlaySeData(data);
            _manager.Stop(handle);

            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void PlaySeData_ExceedingMaxConcurrent_StopsOldestOfSameSe()
        {
            var data = CreateSeData(1, maxConcurrent: 2);
            var a = _manager.PlaySeData(data);
            var b = _manager.PlaySeData(data);
            var c = _manager.PlaySeData(data);

            Assert.IsFalse(_manager.IsPlaying(a));
            Assert.IsTrue(_manager.IsPlaying(b));
            Assert.IsTrue(_manager.IsPlaying(c));
        }

        [Test]
        public void PlaySeData_WithinCooldown_IsRejected()
        {
            var data = CreateSeData(1, cooldown: 10f);
            var first = _manager.PlaySeData(data);
            var second = _manager.PlaySeData(data);

            Assert.IsTrue(_manager.IsPlaying(first));
            Assert.AreEqual(Handle<SeMarker>.Invalid, second);
        }

        [Test]
        public void GlobalPoolLimit_ReclaimsLowestPriorityAcrossDifferentSeData()
        {
            _manager.SetGlobalSeLimit(1);

            var low = CreateSeData(1);
            low.Flags = new AssetFlags { Priority = 0 };
            var lowHandle = _manager.PlaySeData(low);

            var high = CreateSeData(2);
            high.Flags = new AssetFlags { Priority = 10 };
            var highHandle = _manager.PlaySeData(high);

            Assert.IsFalse(_manager.IsPlaying(lowHandle));
            Assert.IsTrue(_manager.IsPlaying(highHandle));
        }

        [Test]
        public void PlaySe_UnregisteredId_FallsBackToPlaceholderAndStillPlays()
        {
            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            var handle = _manager.PlaySe(new SeId(999, AssetType.Se));

            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [UnityTest]
        public IEnumerator Tick_AutoReclaimsFinishedNonLoopingSe()
        {
            var shortClip = CreateClip(0.05f);
            var data = CreateSeData(1, shortClip);
            var handle = _manager.PlaySeData(data);

            yield return new WaitForSeconds(0.2f);
            _manager.Tick(0.2f);

            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void PlaySe_WithContextRoot_ResolvesBoneNameAnchorAndFollowsPosition()
        {
            var character = new GameObject("Character");
            var hand = new GameObject("Hand");
            hand.transform.SetParent(character.transform);
            hand.transform.position = new Vector3(1f, 2f, 3f);

            var data = CreateSeData(1);
            data.Anchor = new AnchorDef { Space = AnchorSpace.BoneName, Path = "Hand" };

            var handle = _manager.PlaySeData(data, contextRoot: character.transform);
            Assert.AreEqual(hand.transform.position, GetSourcePosition(handle));

            hand.transform.position = new Vector3(9f, 9f, 9f);
            _manager.Tick(0.016f);
            Assert.AreEqual(hand.transform.position, GetSourcePosition(handle));

            Object.DestroyImmediate(character);
        }

        [Test]
        public void PlaySe_ExplicitPosition_OverridesAnchorEntirely()
        {
            var character = new GameObject("Character");
            var hand = new GameObject("Hand");
            hand.transform.SetParent(character.transform);
            hand.transform.position = new Vector3(1f, 2f, 3f);

            var data = CreateSeData(1);
            data.Spatial = SpatialMode.AtPosition;
            data.Anchor = new AnchorDef { Space = AnchorSpace.BoneName, Path = "Hand" };

            var explicitPos = new Vector3(50f, 0f, 0f);
            var handle = _manager.PlaySeData(data, explicitPosition: explicitPos);

            Assert.AreEqual(explicitPos, GetSourcePosition(handle));

            Object.DestroyImmediate(character);
        }

        [Test]
        public void FollowTargetDestroyed_WithoutDetachOnStop_StopsPlayback()
        {
            var character = new GameObject("Character");
            var data = CreateSeData(1);
            data.Anchor = new AnchorDef { Space = AnchorSpace.ContextTarget, DetachOnStop = false };

            var handle = _manager.PlaySeData(data, contextRoot: character.transform);
            Object.DestroyImmediate(character);

            _manager.Tick(0.016f);

            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void FollowTargetDestroyed_WithDetachOnStop_KeepsPlaying()
        {
            var character = new GameObject("Character");
            var data = CreateSeData(1);
            data.Anchor = new AnchorDef { Space = AnchorSpace.ContextTarget, DetachOnStop = true };

            var handle = _manager.PlaySeData(data, contextRoot: character.transform);
            Object.DestroyImmediate(character);

            _manager.Tick(0.016f);

            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        private Vector3 GetSourcePosition(Handle<SeMarker> handle)
        {
            var pos = _manager.GetPosition(handle);
            Assert.IsTrue(pos.HasValue);
            return pos.Value;
        }
    }
}
