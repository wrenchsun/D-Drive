using System.Collections.Generic;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AnimId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim.AnimMarker>;

namespace DDrive.Tests.Runtime
{
    // [05_model_animation.md] B-3 — 3-1: CrossFade 再生(土台無しでも時間追跡)・Frame/Time/OnLoop/OnDestroy 発火・中断・BlendShape。
    public class AnimManagerTests
    {
        private AnimManager _manager;
        private GameObject _actor;
        private Animator _animator;
        private readonly List<EventTrigger> _fired = new();

        [SetUp]
        public void SetUp()
        {
            _manager = new AnimManager(new AssetRegistry(new FakeAssetLoader()));
            _manager.Events.OnEventFired += (_, evt) => _fired.Add(evt.Trigger);
            _actor = new GameObject("Actor");
            _animator = _actor.AddComponent<Animator>();
            _fired.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // P5 レビュー対応(2026-09-14) tests P2-3: Facade_UnboundAnim_... が Anim.Bind(null) を
            // テスト本体でしか呼んでいなかった(ScenePreloadTests/TuningTests の流儀に揃える)。
            Anim.Bind(null);
            _manager.StopAll(StopReason.Manual);
            Object.DestroyImmediate(_actor);
        }

        private static AnimationClip CreateClip(float seconds = 1f, float frameRate = 30f)
        {
            var clip = new AnimationClip { legacy = true, frameRate = frameRate };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, seconds, 1f));
            return clip;
        }

        private static AnimData CreateAnim(ulong id = 1, bool loop = false, params AssetEvent[] events)
        {
            var data = ScriptableObject.CreateInstance<AnimData>();
            data.Id = id;
            data.DisplayName = $"Anim{id}";
            data.Clip = CreateClip();
            data.Loop = loop;
            data.Events = events;
            return data;
        }

        private int Count(EventTrigger t) => _fired.FindAll(x => x == t).Count;

        // EventBus.Fire は「Data に定義されたイベントのうち該当トリガのもの」を発火する。
        // ライフサイクルの発火順を検証するテストは、全トリガのイベントを 1 つずつ持つ Data を使う。
        private static AssetEvent[] Lifecycle() => new[]
        {
            new AssetEvent { Trigger = EventTrigger.OnSpawn },
            new AssetEvent { Trigger = EventTrigger.OnEnable },
            new AssetEvent { Trigger = EventTrigger.OnLoop },
            new AssetEvent { Trigger = EventTrigger.OnDisable },
            new AssetEvent { Trigger = EventTrigger.OnDestroy },
        };

        [Test]
        public void PlayData_TracksTime_AndEndsWithDestroyEvents()
        {
            var handle = _manager.PlayData(CreateAnim(1, false, Lifecycle()), _animator);

            Assert.IsTrue(_manager.IsPlaying(handle));
            Assert.AreEqual(1, Count(EventTrigger.OnSpawn));
            Assert.AreEqual(1, Count(EventTrigger.OnEnable));
            Assert.IsNotNull(_actor.GetComponent<AnimatorProxy>(), "AnimatorProxy が自動で付く");

            _manager.Tick(0.5f);
            Assert.AreEqual(0.5f, _manager.GetNormalizedTime(handle), 1e-4f);
            Assert.IsTrue(_manager.IsPlaying(handle));

            _manager.Tick(0.6f);
            Assert.IsFalse(_manager.IsPlaying(handle), "1 秒のクリップは 1.1 秒で終わる");
            Assert.AreEqual(1, Count(EventTrigger.OnDisable));
            Assert.AreEqual(1, Count(EventTrigger.OnDestroy));
        }

        [Test]
        public void Loop_FiresOnLoopEachWrap_AndKeepsPlaying()
        {
            var handle = _manager.PlayData(CreateAnim(1, true, Lifecycle()), _animator);

            _manager.Tick(1.1f);
            _manager.Tick(1.0f);

            Assert.IsTrue(_manager.IsPlaying(handle));
            Assert.AreEqual(2, Count(EventTrigger.OnLoop));
            Assert.AreEqual(2, _manager.GetLoopCount(handle));
            Assert.AreEqual(0, Count(EventTrigger.OnDestroy));
        }

        [Test]
        public void FrameEvent_FiresAtClipFrame_NotGameFrame()
        {
            // 30fps のフレーム 15 = 0.5 秒。Tick 回数ではなくクリップ時間で判定する。
            var evt = new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Action = EventAction.PlayAsset };
            var handle = _manager.PlayData(CreateAnim(events: evt), _animator);

            _manager.Tick(0.4f);
            Assert.AreEqual(0, Count(EventTrigger.Frame));

            _manager.Tick(0.2f);
            Assert.AreEqual(1, Count(EventTrigger.Frame));

            _manager.Tick(0.1f);
            Assert.AreEqual(1, Count(EventTrigger.Frame), "同じ周回で二重に出ない");
            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [Test]
        public void FrameEvent_RefiresEveryLoop()
        {
            var evt = new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f };
            _manager.PlayData(CreateAnim(loop: true, events: evt), _animator);

            _manager.Tick(0.6f);
            _manager.Tick(0.6f); // 周回
            _manager.Tick(0.6f); // 2 周目の 0.2s → 0.8s: 再発火

            Assert.AreEqual(2, Count(EventTrigger.Frame));
        }

        [Test]
        public void TimeEvent_AtClipEnd_IsNotLost()
        {
            var evt = new AssetEvent { Trigger = EventTrigger.Time, Time = 1f };
            _manager.PlayData(CreateAnim(events: evt), _animator);

            _manager.Tick(2f);

            Assert.AreEqual(1, Count(EventTrigger.Time), "終端イベントは終了直前に拾う");
        }

        [Test]
        public void Play_SameAnimatorAndLayer_InterruptsPrevious()
        {
            var first = _manager.PlayData(CreateAnim(1, false, Lifecycle()), _animator);
            var second = _manager.PlayData(CreateAnim(2, false, Lifecycle()), _animator);

            Assert.IsFalse(_manager.IsPlaying(first));
            Assert.IsTrue(_manager.IsPlaying(second));
            Assert.AreEqual(1, Count(EventTrigger.OnDisable), "中断は OnDisable");
            Assert.AreEqual(0, Count(EventTrigger.OnDestroy), "中断では OnDestroy は出ない");
        }

        [Test]
        public void Play_DifferentLayer_DoesNotInterrupt()
        {
            var upper = CreateAnim(2);
            upper.Layer = 1;
            var first = _manager.PlayData(CreateAnim(1), _animator);
            var second = _manager.PlayData(upper, _animator);

            Assert.IsTrue(_manager.IsPlaying(first));
            Assert.IsTrue(_manager.IsPlaying(second));
        }

        [Test]
        public void Stop_FiresOnDisableOnly()
        {
            var handle = _manager.PlayData(CreateAnim(1, false, Lifecycle()), _animator);
            _manager.Stop(handle);

            Assert.IsFalse(_manager.IsPlaying(handle));
            Assert.AreEqual(1, Count(EventTrigger.OnDisable));
            Assert.AreEqual(0, Count(EventTrigger.OnDestroy));
        }

        [Test]
        public void SetSpeed_ScalesProgress()
        {
            var handle = _manager.PlayData(CreateAnim(), _animator);
            _manager.SetSpeed(handle, 2f);
            _manager.Tick(0.25f);

            Assert.AreEqual(0.5f, _manager.GetNormalizedTime(handle), 1e-4f);
        }

        [Test]
        public void Play_UnregisteredId_UsesPlaceholderWithFallbackLength()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Unregistered AssetId"));
            var handle = _manager.Play(new AnimId(999, AssetType.Anim), _animator);

            Assert.IsTrue(_manager.IsPlaying(handle));
            _manager.Tick(AnimData.FallbackLengthSec + 0.1f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Play_WithoutAnimator_ReturnsInvalid()
        {
            LogAssert.Expect(LogType.Warning, new Regex("without an Animator"));
            var handle = _manager.PlayData(CreateAnim(), null);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Tick_AnimatorDestroyed_ReleasesInstance()
        {
            var handle = _manager.PlayData(CreateAnim(), _animator);
            Object.DestroyImmediate(_actor);
            _actor = new GameObject("Replacement");

            _manager.Tick(0.1f);

            Assert.IsFalse(_manager.IsPlaying(handle));
            Assert.AreEqual(0, _manager.ActiveCount);
        }

        [Test]
        public void BlendShapes_AreAppliedFromCurve()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            mesh.AddBlendShapeFrame("Smile", 100f, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
            var smrGo = new GameObject("Face");
            smrGo.transform.SetParent(_actor.transform);
            var smr = smrGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;

            var data = CreateAnim();
            data.BlendShapes = new[] { new BlendShapeTrack { ShapeName = "Smile", Weight = AnimationCurve.Linear(0f, 0f, 1f, 100f) } };
            _manager.PlayData(data, _animator);
            _manager.Tick(0.5f);

            Assert.AreEqual(50f, smr.GetBlendShapeWeight(0), 0.5f);
            Assert.IsTrue(_actor.GetComponent<AnimatorProxy>().HasBlendShape("Smile"));
        }

        [Test]
        public void Facade_Unbound_IsNoOp()
        {
            Anim.Bind(null);
            var h = Anim.Play(new AnimId(1, AssetType.Anim), _animator);
            Assert.IsFalse(Anim.IsPlaying(h));
            Assert.AreEqual(-1f, h.NormalizedTime());
        }

        // ── Validator ──

        private static List<ValidationResult> Validate(AnimData data)
            => new(new AnimDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })));

        [Test]
        public void Validator_ClipMissing_IsError()
        {
            var data = ScriptableObject.CreateInstance<AnimData>();
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Clip")));
        }

        [Test]
        public void Validator_FrameBeyondClip_IsError_AndOnLoopWithoutLoop_IsWarning()
        {
            var data = CreateAnim(1, false, new AssetEvent { Trigger = EventTrigger.Frame, Time = 45f }, new AssetEvent { Trigger = EventTrigger.OnLoop });
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Frame")));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("OnLoop")));
        }
    }
}
