using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using R3;
using UnityEngine;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;
using ShakeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.CameraShake.ShakeMarker>;
using HapticId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker>;
using AnchorId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker>;

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
            // P5 レビュー対応(2026-09-14) tests P2-3: Facade_UnboundPresentation_... が Presentation.Bind(null)
            // を呼ぶが、それをテスト本体でしか呼んでいなかった(ScenePreloadTests/TuningTests の
            // 「static facade は毎テスト後に必ず Bind(null) で戻す」流儀に揃える)。
            Presentation.Bind(null);
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
        private VfxId RegisterVfx(VfxParam[] parms = null)
        {
            var id = _nextVfxId++;
            var address = $"vfx/{id}";
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = id;
            data.Prefab = _vfxPrefab;
            data.LifeMode = VfxLifeMode.Loop;
            data.Params = parms;

            _loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Vfx, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<VfxData>(id).GetAwaiter().GetResult();

            return new VfxId(id, AssetType.Vfx);
        }

        // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照) — AnchorId/埋め込み
        // Anchor を指定できる版(RegisterVfx と同じ登録手順)。
        private VfxId RegisterVfxWithAnchor(AnchorDef? embeddedAnchor = null, AnchorId anchorId = default)
        {
            var id = _nextVfxId++;
            var address = $"vfx/{id}";
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = id;
            data.Prefab = _vfxPrefab;
            data.LifeMode = VfxLifeMode.Loop;
            if (embeddedAnchor.HasValue)
            {
                data.Anchor = embeddedAnchor.Value;
            }

            data.AnchorId = anchorId;

            _loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Vfx, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<VfxData>(id).GetAwaiter().GetResult();

            return new VfxId(id, AssetType.Vfx);
        }

        // [08_presentation.md] 実装メモ(2026-09-19) — AnchorId 連鎖を含むケースのテスト用(AnchorChainTests.cs
        // の AnchorChainTestRegistry は別の AssetRegistry を新規作成するため、このテストクラスの既存 _registry
        // へ直接登録するための専用ヘルパーを用意した)。
        private AnchorId RegisterAnchor(ulong id, Vector3 offset = default, ulong parent = 0)
        {
            var address = $"anchor/{id}";
            var data = ScriptableObject.CreateInstance<AnchorData>();
            data.Id = id;
            data.LocalOffset = offset;
            data.LocalScale = Vector3.one;
            if (parent != 0)
            {
                data.Parent = new AnchorId(parent, AssetType.Anchor);
            }

            _loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Anchor, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<AnchorData>(id).GetAwaiter().GetResult();

            return new AnchorId(id, AssetType.Anchor);
        }

        // [08_presentation.md] 実装メモ(2026-09-19) — PlayData で新規に Spawn されたクローンの
        // ParticleSystemRenderer を、Instantiate 前後の InstanceID 差分で特定する
        // (VfxTrack_ParamsAppliedByIndex_... と同じ手法。PlayMode では他テストの残骸が残ることがあるため)。
        private ParticleSystemRenderer PlayAndFindSpawnedRenderer(PresentationData data)
        {
            var before = new HashSet<int>();
            foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                before.Add(r.GetInstanceID());
            }

            _manager.PlayData(data, new PlayContext());

            foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!before.Contains(r.GetInstanceID()))
                {
                    return r;
                }
            }

            return null;
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

        // ── 5-4 追補(2026-09-14)(a): パラメータ上書き ──
        // PresentationTrack.Params[i] を、参照先 VfxData.Params[i].Label のインデックス対応で
        // 既存の VfxManager.SetParam(Label 解決)へ渡す([08] 実装メモ参照)。TearDown で _pool.Clear
        // するため、このテストの時点でアクティブな VFX インスタンス(ParticleSystemRenderer)は 1 つだけ。
        [Test]
        public void VfxTrack_ParamsAppliedByIndex_ToVfxDataParamsLabel_ViaPropertyBlock()
        {
            var vfxId = RegisterVfx(new[]
            {
                new VfxParam { Label = "Alpha", Type = VfxParamType.Float, TargetProperty = "_Alpha", Default = ParamValue.Of(0f) },
                new VfxParam { Label = "Size", Type = VfxParamType.Float, TargetProperty = "_Size", Default = ParamValue.Of(0f) },
            });
            var track = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = TrackKind.Vfx,
                Asset = AssetRef.From(vfxId),
                Params = new[] { ParamValue.Of(0.25f), ParamValue.Of(9f) },
            };
            var data = CreateData(112, track);

            // VfxManager.Materialize は Rent した実体を `SetParent(null)` で切り離す(プール置き場から
            // 独立させて自由に配置するため)ため、専用コンテナへ Spawn 先を限定する手は使えない。
            // このテストクラスは PlayMode(Tests/Runtime)で走り、Object.Destroy はフレーム末まで実際の
            // 破棄が遅延される(TearDown の _pool.Clear が Destroy を呼ぶだけで、次のテストまでに
            // 1 フレーム経過するとは限らない)ため、他のテストの残骸がシーンに残っていることがある。
            // そこで PlayData の前後で ParticleSystemRenderer の InstanceID 集合を比較し、新規に
            // 現れた 1 つだけを「自分が Spawn したクローン」として特定する(親子関係・active 状態に依存しない)。
            var before = new HashSet<int>();
            foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                before.Add(r.GetInstanceID());
            }

            _manager.PlayData(data, new PlayContext());

            Assert.AreEqual(1, _vfx.ActiveCount);

            ParticleSystemRenderer spawnedRenderer = null;
            foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!before.Contains(r.GetInstanceID()))
                {
                    spawnedRenderer = r;
                    break;
                }
            }

            Assert.IsNotNull(spawnedRenderer, "Spawn されたクローンの ParticleSystemRenderer が見つかりません(新規出現分)");

            var block = new MaterialPropertyBlock();
            spawnedRenderer.GetPropertyBlock(block);
            Assert.AreEqual(0.25f, block.GetFloat(Shader.PropertyToID("_Alpha")), 1e-4f, "track.Params[0] は VfxData.Params[0](Alpha)に対応する");
            Assert.AreEqual(9f, block.GetFloat(Shader.PropertyToID("_Size")), 1e-4f, "track.Params[1] は VfxData.Params[1](Size)に対応する");
        }

        [Test]
        public void VfxTrack_ParamsLongerThanVfxDataParams_IgnoresExtra_DoesNotThrow()
        {
            var vfxId = RegisterVfx(new[]
            {
                new VfxParam { Label = "Alpha", Type = VfxParamType.Float, TargetProperty = "_Alpha", Default = ParamValue.Of(0f) },
            });
            var track = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = TrackKind.Vfx,
                Asset = AssetRef.From(vfxId),
                Params = new[] { ParamValue.Of(0.5f), ParamValue.Of(1f), ParamValue.Of(2f) }, // VfxData.Params は 1 個しかない
            };
            var data = CreateData(113, track);

            Assert.DoesNotThrow(() => _manager.PlayData(data, new PlayContext()));
            Assert.AreEqual(1, _vfx.ActiveCount, "Params 個数が VfxData.Params を超えても VFX 自体は発火する");
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

        // ── P5 レビュー第 1 弾 追加テスト ── (review1_tests.md「追加すべきテスト」①)
        // Fired フラグは Seek で前後に跳んでも保持される(review1_runtime.md「問題なし」節で確認済みの
        // 挙動を回帰テストとして固定する): 通過済みトラックを Seek で通り過ぎても、後方 Seek で戻っても、
        // 再度前方へ Seek しても二重発火しない。
        [Test]
        public void Seek_JumpForwardThenBackwardThenForward_DoesNotRefireMarkerTrack()
        {
            var order = new List<string>();
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0.5f, Kind = TrackKind.Marker, SignalKey = "m" };
            var data = CreateData(110, track);
            data.TotalDuration = 5f;

            var handle = _manager.PlayData(data, new PlayContext());
            _manager.OnMarker(handle).Subscribe(name => order.Add(name));

            _manager.Seek(handle, 1f); // 前方へジャンプ(0.5s を通過)
            CollectionAssert.AreEqual(new[] { "m" }, order, "通過したトラックが 1 回発火する");

            _manager.Seek(handle, 0.1f); // 後方へジャンプ(0.5s より前へ戻る)
            CollectionAssert.AreEqual(new[] { "m" }, order, "後方 Seek では Fired が保持されるため再発火しない");

            _manager.Seek(handle, 2f); // 再度前方へジャンプ(0.5s を再び「通過」する形になる)
            CollectionAssert.AreEqual(new[] { "m" }, order, "同じトラックを再度通過しても二重発火しない");

            _manager.Tick(0.01f);
            CollectionAssert.AreEqual(new[] { "m" }, order, "Tick でも再発火しない");
        }

        // ── P5 レビュー第 1 弾 追加テスト ── (review1_tests.md「追加すべきテスト」⑦)
        // StopOnCancel=false(既定値)のトラックは、Presentation 自体が Cancel された後も委譲先の実体
        // (ここでは VFX)を止めない(Cancel_Interruptible_StopsStopOnCancelTracks_AndRaisesOnCancelled の
        // StopOnCancel=true と対になる回帰テスト)。
        [Test]
        public void Cancel_StopOnCancelFalseTrack_ContinuesPlayingAfterCancel()
        {
            var vfxId = RegisterVfx();
            var cancelled = 0;
            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Vfx, Asset = AssetRef.From(vfxId), StopOnCancel = false };
            var data = CreateData(111, track);
            data.TotalDuration = 5f;

            var handle = _manager.PlayData(data, new PlayContext());
            _manager.OnCancelled(handle).Subscribe(_ => cancelled++);
            Assert.AreEqual(1, _vfx.ActiveCount);

            _manager.Cancel(handle);

            Assert.IsFalse(_manager.IsPlaying(handle), "Presentation 自体は Cancel される");
            Assert.AreEqual(1, cancelled);
            Assert.AreEqual(1, _vfx.ActiveCount, "StopOnCancel=false の VFX は Cancel 後も再生を継続する");
        }

        // [08_presentation.md] SceneView Anchor 表示(2026-09-19) — ResolveContextRoot を public 化した際の
        // 回帰テスト。Editor 側の PresentationTrackAnchorResolver がこれをそのまま再利用する(コピペしない)ため、
        // TrackTargetMode ごとのマッピングをここで固定しておく。
        [Test]
        public void ResolveContextRoot_MapsEachTargetModeAsDocumented()
        {
            var selfGo = new GameObject("Self");
            var targetGo = new GameObject("Target");
            try
            {
                var ctx = new PlayContext { Self = selfGo.transform, Target = targetGo.transform };

                Assert.AreSame(selfGo.transform, PresentationManager.ResolveContextRoot(in ctx, TrackTargetMode.Self));
                Assert.AreSame(targetGo.transform, PresentationManager.ResolveContextRoot(in ctx, TrackTargetMode.ContextTarget));
                Assert.IsNull(PresentationManager.ResolveContextRoot(in ctx, TrackTargetMode.World));
                Assert.IsNull(PresentationManager.ResolveContextRoot(in ctx, TrackTargetMode.Anchor));
            }
            finally
            {
                Object.DestroyImmediate(selfGo);
                Object.DestroyImmediate(targetGo);
            }
        }

        // ── 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照)── FireVfx が
        // PresentationTrackAnchorComposer の 3 ケースをそのまま反映することを確認する
        // (合成の数式自体は PresentationTrackAnchorComposerTests で検証済み。ここでは
        // PresentationManager.FireVfx が正しい引数〔track / data / registry〕で呼んでいることを確認する)。

        [Test]
        public void FireVfx_TrackOnly_SpawnsAtTrackAnchorPosition()
        {
            var vfxId = RegisterVfx(); // Anchor/AnchorId とも既定値(アセット側は未設定)。
            var track = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = TrackKind.Vfx,
                Asset = AssetRef.From(vfxId),
                Anchor = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(1f, 2f, 3f), LocalScale = Vector3.one },
            };
            var data = CreateData(300, track);

            var renderer = PlayAndFindSpawnedRenderer(data);

            Assert.IsNotNull(renderer, "Spawn されたクローンが見つかりません");
            Assert.Less(Vector3.Distance(new Vector3(1f, 2f, 3f), renderer.transform.position), 1e-4f);
        }

        [Test]
        public void FireVfx_AssetOnly_SpawnsAtAssetEmbeddedAnchorPosition()
        {
            // トラック側は既定値のまま(未設定)、アセット側の埋め込み Anchor だけを設定する。
            var vfxId = RegisterVfxWithAnchor(embeddedAnchor: new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(4f, 5f, 6f), LocalScale = Vector3.one });
            var track = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = TrackKind.Vfx,
                Asset = AssetRef.From(vfxId),
            };
            var data = CreateData(301, track);

            var renderer = PlayAndFindSpawnedRenderer(data);

            Assert.IsNotNull(renderer, "Spawn されたクローンが見つかりません");
            Assert.Less(Vector3.Distance(new Vector3(4f, 5f, 6f), renderer.transform.position), 1e-4f);
        }

        [Test]
        public void FireVfx_AssetOnly_ViaAnchorIdChain_SpawnsAtChainPosition()
        {
            var anchorId = RegisterAnchor(600, offset: new Vector3(7f, 0f, 0f));
            var vfxId = RegisterVfxWithAnchor(anchorId: anchorId);
            var track = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = TrackKind.Vfx,
                Asset = AssetRef.From(vfxId),
            };
            var data = CreateData(302, track);

            var renderer = PlayAndFindSpawnedRenderer(data);

            Assert.IsNotNull(renderer, "Spawn されたクローンが見つかりません");
            Assert.Less(Vector3.Distance(new Vector3(7f, 0f, 0f), renderer.transform.position), 1e-4f);
        }

        [Test]
        public void FireVfx_Both_ComposesTrackAsParentOfAsset()
        {
            // トラック: World 原点から +X 1m, Y+90°回転。アセット側(埋め込み): 親の前方(+Z)に 1m。
            var vfxId = RegisterVfxWithAnchor(embeddedAnchor: new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(0f, 0f, 1f), LocalScale = Vector3.one });
            var track = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = TrackKind.Vfx,
                Asset = AssetRef.From(vfxId),
                Anchor = new AnchorDef { Space = AnchorSpace.World, LocalOffset = new Vector3(1f, 0f, 0f), LocalEuler = new Vector3(0f, 90f, 0f), LocalScale = Vector3.one },
            };
            var data = CreateData(303, track);

            var renderer = PlayAndFindSpawnedRenderer(data);

            Assert.IsNotNull(renderer, "Spawn されたクローンが見つかりません");
            // 親の向き(Y+90°)で子の前方(+Z)は +X になる: (1,0,0) + (1,0,0) = (2,0,0)。
            Assert.Less(Vector3.Distance(new Vector3(2f, 0f, 0f), renderer.transform.position), 1e-4f);
        }

        [Test]
        public void FireVfx_Neither_SpawnsAtWorldOrigin()
        {
            var vfxId = RegisterVfx();
            var track = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = TrackKind.Vfx,
                Asset = AssetRef.From(vfxId),
            };
            var data = CreateData(304, track);

            var renderer = PlayAndFindSpawnedRenderer(data);

            Assert.IsNotNull(renderer, "Spawn されたクローンが見つかりません");
            Assert.Less(Vector3.Distance(Vector3.zero, renderer.transform.position), 1e-4f);
        }
    }
}
