using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Camera;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using R3;
using UnityEngine;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;
using ShakeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Camera.ShakeMarker>;
using HapticId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker>;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 5-1 — AtTime 順序 / Signal 発火 / Cancel / HitStop 後の時間進行 / WaitAsync / OnCompleted /
    // 無効 Handle を検証する。実カタログ・実 GameData には触れず、Id はテスト専用のダミー値を使う。
    public class PresentationManagerTests
    {
        private PoolService _pool;
        private FakeAssetLoader _loader;
        private AssetRegistry _registry;
        private VfxManager _vfx;
        private AudioManager _audio;
        private AnimManager _anim;
        private CameraFxManager _cameraFx;
        private HapticsManager _haptics;
        private FakeHapticOutput _hapticOutput;
        private TimeService _time;
        private PresentationManager _manager;
        private GameObject _vfxPrefab;
        private GameObject _seSourcePrefab;
        private ulong _nextVfxId = 900001;
        private ulong _nextShakeId = 910001;
        private ulong _nextHapticId = 920001;

        private sealed class FakeHapticOutput : IHapticOutput
        {
            public float Low;
            public float High;
            public void SetMotors(float low, float high)
            {
                Low = low;
                High = high;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _vfxPrefab = CreateParticlePrefab();
            _vfx = new VfxManager(_pool, _registry);

            _seSourcePrefab = new GameObject("SeSourcePrefab");
            _seSourcePrefab.AddComponent<AudioSource>();
            _audio = new AudioManager(_pool, _registry, _seSourcePrefab);

            _anim = new AnimManager(_registry);
            _cameraFx = new CameraFxManager(_registry);
            _hapticOutput = new FakeHapticOutput();
            _haptics = new HapticsManager(_registry, _hapticOutput);
            _time = new TimeService();
            _manager = new PresentationManager(_registry, _time, _audio, null, _vfx, _anim, null, null, _cameraFx, _haptics);
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_vfxPrefab);
            Object.DestroyImmediate(_seSourcePrefab);
        }

        private static GameObject CreateParticlePrefab()
        {
            var go = new GameObject("PresentationTestVfxPrefab");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 5f;
            main.loop = false;
            main.startLifetime = 5f;
            return go;
        }

        // カタログ登録 + ResolveAsync まで済ませ、Presentation 内部の ResolveOrPlaceholder が
        // 実データを引けるようにする(EventRepeatTests 等と同じ idiom)。
        private VfxId RegisterVfx()
        {
            var id = _nextVfxId++;
            var address = $"vfx/{id}";
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = id;
            data.Prefab = _vfxPrefab;
            data.LifeMode = VfxLifeMode.Loop;

            _loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Vfx, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<VfxData>(id).GetAwaiter().GetResult();

            return new VfxId(id, AssetType.Vfx);
        }

        private ShakeId RegisterShake()
        {
            var id = _nextShakeId++;
            var address = $"shake/{id}";
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            data.Id = id;
            data.PosAmplitude = new Vector3(1f, 0f, 0f);
            data.MaxStack = 5;

            _loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Shake, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<CameraShakeData>(id).GetAwaiter().GetResult();

            return new ShakeId(id, AssetType.Shake);
        }

        private HapticId RegisterHaptic()
        {
            var id = _nextHapticId++;
            var address = $"haptic/{id}";
            var data = ScriptableObject.CreateInstance<HapticsData>();
            data.Id = id;

            _loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Haptics, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<HapticsData>(id).GetAwaiter().GetResult();

            return new HapticId(id, AssetType.Haptics);
        }

        private static PresentationData CreateData(ulong id, params PresentationTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Id = id;
            data.Tracks = tracks;
            data.Interruptible = true;
            return data;
        }

        // ── AtTime(0.00) 即時発火 ──

        [Test]
        public void PlayData_AtTimeZeroVfxTrack_FiresImmediately()
        {
            var vfxId = RegisterVfx();
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Vfx, Asset = AssetRef.From(vfxId) };
            var data = CreateData(100, track);

            var handle = _manager.PlayData(data, new PlayContext());

            Assert.AreEqual(1, _vfx.ActiveCount);
            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        // ── AtTime 順序 ──

        [Test]
        public void Tick_AtTimeTracks_FireInTimeOrder()
        {
            // Time=0 は Play() 呼び出し時点で即時発火する([08] §3)ため、Play() より後にしか Subscribe
            // できない Observable では観測できない(呼び出し側は Vfx/Se 等と同じく、時刻 0 の副作用は
            // Play() の戻り値を待たず起きる前提で設計されている)。ここでは 0 秒トラックを含めず、
            // Tick による「順序どおりの発火」だけを検証する。
            var order = new List<string>();
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0.2f, Kind = TrackKind.Marker, SignalKey = "a" };
            var t1 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0.5f, Kind = TrackKind.Marker, SignalKey = "b" };
            var data = CreateData(101, t0, t1);
            data.TotalDuration = 1f;

            var handle = _manager.PlayData(data, new PlayContext());
            _manager.OnMarker(handle).Subscribe(name => order.Add(name));

            CollectionAssert.IsEmpty(order, "AtTime > 0 のトラックは Play() 時点では発火しない");

            _manager.Tick(0.3f);
            CollectionAssert.AreEqual(new[] { "a" }, order, "0.2s 到達で 1 番目が発火し、0.5s 未到達の 2 番目はまだ発火しない");

            _manager.Tick(0.3f);
            CollectionAssert.AreEqual(new[] { "a", "b" }, order, "0.6s 経過で 2 番目のトラックが発火する");
        }

        [Test]
        public void PlayData_AtTimeZeroMarkerTrack_FiresBeforePlayDataReturns()
        {
            // Time=0 は即時発火するため、Play() より前に登録された PlayContext.OnSignal のような
            // 「呼び出し前に渡すコールバック」でしか観測できない(Kind=Signal 側で検証)。Marker はここでは
            // 「例外にならず、Fired 済みとして扱われ Tick で再発火しない」ことだけを確認する。
            var order = new List<string>();
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "a" };
            var data = CreateData(109, t0);
            data.TotalDuration = 1f;

            var handle = _manager.PlayData(data, new PlayContext());
            _manager.OnMarker(handle).Subscribe(name => order.Add(name));

            _manager.Tick(1f);
            CollectionAssert.IsEmpty(order, "Time=0 は Play() 内で発火済みのため、Tick で再度発火しない");
        }

        // ── Signal 発火 ──

        [Test]
        public void Signal_FiresMatchingOnSignalTrack_OnceOnly()
        {
            var vfxId = RegisterVfx();
            var track = new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "hit", Kind = TrackKind.Vfx, Asset = AssetRef.From(vfxId) };
            var data = CreateData(102, track);
            data.TotalDuration = 5f;

            var handle = _manager.PlayData(data, new PlayContext());
            Assert.AreEqual(0, _vfx.ActiveCount, "OnSignal トラックは Signal が来るまで発火しない");

            _manager.Signal(handle, "hit");
            Assert.AreEqual(1, _vfx.ActiveCount);

            _manager.Signal(handle, "hit");
            Assert.AreEqual(1, _vfx.ActiveCount, "同じトラックは 1 回しか発火しない");
        }

        [Test]
        public void Play_SignalKindTrack_InvokesPlayContextOnSignal()
        {
            var received = new List<string>();
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Signal, SignalKey = "hit" };
            var data = CreateData(103, track);
            var ctx = new PlayContext { OnSignal = key => received.Add(key) };

            _manager.PlayData(data, in ctx);

            CollectionAssert.AreEqual(new[] { "hit" }, received);
        }

        // ── Cancel ──

        [Test]
        public void Cancel_Interruptible_StopsStopOnCancelTracks_AndRaisesOnCancelled()
        {
            var vfxId = RegisterVfx();
            var cancelled = 0;
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Vfx, Asset = AssetRef.From(vfxId), StopOnCancel = true };
            var data = CreateData(104, track);
            data.TotalDuration = 5f;

            var handle = _manager.PlayData(data, new PlayContext());
            _manager.OnCancelled(handle).Subscribe(_ => cancelled++);
            Assert.AreEqual(1, _vfx.ActiveCount);

            _manager.Cancel(handle);

            Assert.AreEqual(0, _vfx.ActiveCount, "StopOnCancel=true の VFX は Cancel で止まる");
            Assert.IsFalse(_manager.IsPlaying(handle));
            Assert.AreEqual(1, cancelled);
        }

        [Test]
        public void Cancel_NonInterruptible_IsIgnored()
        {
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "x" };
            var data = CreateData(105, track);
            data.TotalDuration = 5f;
            data.Interruptible = false;

            var handle = _manager.PlayData(data, new PlayContext());
            _manager.Cancel(handle);

            Assert.IsTrue(_manager.IsPlaying(handle), "Interruptible=false は Cancel() を無視する");
        }

        // ── HitStop 後の時間進行 ──

        [Test]
        public void HitStop_Track_PausesThenResumesTimeService()
        {
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.HitStop, Params = new[] { ParamValue.Of(0.2f) } };
            var data = CreateData(106, track);

            _manager.PlayData(data, new PlayContext());

            Assert.AreEqual(0f, _time.TimeScale, "HitStop 中は TimeScale=0");
            _time.Tick(0.1f);
            Assert.AreEqual(0f, _time.TimeScale);
            _time.Tick(0.15f);
            Assert.AreEqual(1f, _time.TimeScale, "HitStop の秒数が経過すると 1 に戻る");
        }

        // ── WaitAsync / OnCompleted ──

        [Test]
        public void WaitAsync_And_OnCompleted_ResolveWhenDurationElapses()
        {
            var completed = 0;
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "m" };
            var data = CreateData(107, track);
            data.TotalDuration = 0.5f;

            var handle = _manager.PlayData(data, new PlayContext());
            _manager.OnCompleted(handle).Subscribe(_ => completed++);
            var task = _manager.WaitAsync(handle, default);

            Assert.AreNotEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(0, completed);

            _manager.Tick(0.5f);

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(1, completed);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        // ── CameraShake / Haptic トラック(5-2 / 5-2b) ──

        [Test]
        public void CameraShakeTrack_FiresIntoCameraFx_AndStopsOnCancel()
        {
            var shakeId = RegisterShake();
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.CameraShake, Asset = AssetRef.From(shakeId), StopOnCancel = true };
            var data = CreateData(200, track);
            data.TotalDuration = 5f;

            var handle = _manager.PlayData(data, new PlayContext { Position = Vector3.zero });

            Assert.AreEqual(1, _cameraFx.ActiveCount, "CameraShake トラックの発火で CameraFxManager に Instance が追加される");

            _manager.Cancel(handle);
            _cameraFx.Tick(0f); // fade=0 で Stop したため、次の Tick で台帳から外れる

            Assert.AreEqual(0, _cameraFx.ActiveCount, "StopOnCancel=true の Shake は Cancel で止まる(即時 fade=0)");
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void HapticTrack_FiresIntoHaptics_AndStopsOnCancel()
        {
            var hapticId = RegisterHaptic();
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Haptic, Asset = AssetRef.From(hapticId), StopOnCancel = true };
            var data = CreateData(201, track);
            data.TotalDuration = 5f;

            var handle = _manager.PlayData(data, new PlayContext());
            _haptics.Tick(0.01f);
            Assert.Greater(_hapticOutput.Low + _hapticOutput.High, 0f, "Haptic トラックの発火で出力が 0 より大きくなる");

            _manager.Cancel(handle);
            _haptics.Tick(0.01f);

            Assert.AreEqual(0f, _hapticOutput.Low);
            Assert.AreEqual(0f, _hapticOutput.High);
        }

        // ── 無効 Handle ──

        [Test]
        public void InvalidHandle_AllOperations_AreNoOpAndDoNotThrow()
        {
            var invalid = Handle<PresentationMarker>.Invalid;

            Assert.DoesNotThrow(() =>
            {
                _manager.Signal(invalid, "x");
                _manager.Cancel(invalid);
                _manager.SetPaused(invalid, true);
                _manager.SetSpeed(invalid, 2f);
                _manager.Seek(invalid, 1f);
            });

            Assert.IsFalse(_manager.IsPlaying(invalid));
            Assert.AreEqual(-1f, _manager.GetNormalizedTime(invalid));

            var waitTask = _manager.WaitAsync(invalid, default);
            Assert.AreEqual(UniTaskStatus.Succeeded, waitTask.Status);
        }

        [Test]
        public void Facade_UnboundPresentation_PlayReturnsInvalidHandle_AndIsNoOp()
        {
            Presentation.Bind(null);

            var handle = Presentation.Play(default, new PlayContext());

            Assert.IsFalse(handle.IsPlaying);
            Assert.DoesNotThrow(() => handle.Signal("x"));
            Assert.DoesNotThrow(() => handle.Cancel());
        }

        // ── CameraShake/Haptic は 5-2/5-2b で実装済み。この Fixture は CameraFx/Haptics を配線していない
        // ため、ここでは「委譲先 Manager 未設定」の警告 1 回 + no-op のほうを検証する
        // (専用の Fixture は CameraFxManagerTests / HapticsManagerTests / PresentationManagerTests 内の
        // 別テスト(CameraShakeAndHapticTracks_...)を参照)。Timeline は引き続き 6-10 待ちで未実装。

        [Test]
        public void UnimplementedKind_DoesNotThrow_AndPresentationStillCompletes()
        {
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Timeline };
            var data = CreateData(108, track);

            Handle<PresentationMarker> handle = default;
            Assert.DoesNotThrow(() => handle = _manager.PlayData(data, new PlayContext()));

            _manager.Tick(0.1f);
            Assert.IsFalse(_manager.IsPlaying(handle), "尺 0(Time=0 のみ)なので直後の Tick で完了する");
        }
    }
}
