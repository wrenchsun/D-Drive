using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2 / [03_audio.md] — AudioManager(Se、AudioSource プール)の定常経路(Tick)の
    // 0 alloc と、Play 側の既知課題(Vfx と同型、下記コメント)を記録する。
    public class AudioAllocTests
    {
        private GameObject _sourcePrefab;
        private PoolService _pool;
        private AssetRegistry _registry;
        private AudioManager _manager;

        [SetUp]
        public void SetUp()
        {
            _sourcePrefab = new GameObject("SeAllocPrefab");
            _sourcePrefab.AddComponent<AudioSource>();
            _pool = new PoolService();
            _registry = new AssetRegistry(new NullAssetLoader());
            _manager = new AudioManager(_pool, _registry, _sourcePrefab);
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_sourcePrefab);
        }

        private static AudioClip CreateClip(float seconds)
        {
            return AudioClip.Create("PerfTestClip", (int)(44100 * seconds), 1, 44100, false);
        }

        private static SeData CreateData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            data.Id = id;
            // Tick を測定している間に鳴り終わって自動 Stop されないよう、十分長いクリップを使う
            // (AudioManager.Tick は非ループの AudioSource が isPlaying=false になると自動 Stop する)。
            data.Clips = new[] { CreateClip(120f) };
            data.SelectMode = ClipSelectMode.First;
            data.Volume = 1f;
            data.MaxConcurrent = 32;
            data.CooldownSec = 0f;
            return data;
        }

        // Tick は毎フレーム必ず呼ばれる定常経路。既に再生中の Instance を回すだけなら 0 alloc であるべき
        // ([12_review.md] §3)。
        [Test, Performance]
        public void Tick_ActivePlayback_AllocatesNothing()
        {
            var data = CreateData(1);
            _manager.PlaySeData(data);

            AllocProbe.AssertZeroAlloc(
                "Se.Tick",
                warmup: () => _manager.Tick(0.016f),
                measured: () => _manager.Tick(0.016f));
        }

        // Play(=PlaySeData)は 1 アクションにつき 1 回の経路。AudioSource 自体は Pool 経由で本チケットで
        // 0 alloc 化済みだが、SeInstance(class)は Play ごとに 1 個 new する既存設計(Vfx の VfxInstance と
        // 同じ既知課題、[12_review.md] §3 参照)。Performance レポートに記録するだけに留め、CI の
        // fail 条件にはしない。
        [Test, Performance]
        public void PlaySeData_StopImmediately_RecordsAllocForKnownIssue()
        {
            var data = CreateData(2);

            AllocProbe.RecordGc("Se.PlayStop", () =>
            {
                var handle = _manager.PlaySeData(data);
                _manager.Stop(handle);
            });
        }
    }
}
