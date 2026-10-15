using System.Collections;
using System.Collections.Generic;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace DDrive.Tests.Runtime
{
    // [26_timeline.md] §4.6.6(MS-2、2026-10-15) — Cutscene.Play / PlayData の CutscenePlayOptions(指定カメラ・入力ロック無視)。
    public class CutscenePlayOptionsTests
    {
        private readonly List<Object> _created = new();
        private CutsceneManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        private Camera CreateCamera(string name, bool main, float x)
        {
            var go = new GameObject(name, typeof(Camera));
            if (main)
            {
                go.tag = "MainCamera";
            }

            go.transform.position = new Vector3(x, 0f, 0f);
            _created.Add(go);
            return go.GetComponent<Camera>();
        }

        // PosX が 0 → 10 へ 1 秒で動くカメラクリップ 1 本の Cutscene。
        private CutsceneData CreateCameraCutscene(bool lockInput = false, string bindTrackName = null)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 1.0;
            var track = timeline.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var clip = track.CreateClip<CutsceneCameraClip>();
            clip.duration = 1.0;
            var camAsset = (CutsceneCameraClip)clip.asset;
            camAsset.PosX = AnimationCurve.Linear(0f, 0f, 1f, 10f);
            camAsset.StepFps = 0f;
            camAsset.BlendIn = ValueDef.Constant01(1f);
            camAsset.BlendOut = ValueDef.Constant01(1f);
            camAsset.Focus = CameraFocusMode.Off;

            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);
            data.Timeline = timeline;
            data.Origin = CutsceneOrigin.World;
            data.LockInput = lockInput;
            if (bindTrackName != null)
            {
                var animTrack = timeline.CreateTrack<AnimationTrack>(null, bindTrackName);
                data.Bindings = new[] { new CutsceneBinding { TrackName = bindTrackName, Target = CutsceneBindTarget.MainCamera } };
                _ = animTrack;
            }

            return data;
        }

        [UnityTest]
        public IEnumerator SpecifiedCamera_IsDriven_AndMainCameraIsUntouched()
        {
            var main = CreateCamera("MsMain", true, 100f);
            var studio = CreateCamera("MsStudio", false, -50f);

            var handle = _manager.PlayData(CreateCameraCutscene(), new PlayContext(), new CutscenePlayOptions(studio));
            _manager.Tick(0.5f);
            yield return TestFrameWait.EndOfFrameOrNextUpdate();

            Assert.AreEqual(5f, studio.transform.position.x, 0.05f, "指定カメラに姿勢が書かれる");
            Assert.AreEqual(100f, main.transform.position.x, 0.001f, "Camera.main は動かない");
            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [UnityTest]
        public IEnumerator TwoCutscenes_OnDifferentCameras_PlayTogether_WithoutWarning()
        {
            var main = CreateCamera("MsMain", true, 100f);
            var studio = CreateCamera("MsStudio", false, -50f);

            _manager.PlayData(CreateCameraCutscene(), new PlayContext());
            _manager.PlayData(CreateCameraCutscene(), new PlayContext(), new CutscenePlayOptions(studio));
            _manager.Tick(0.5f);
            yield return TestFrameWait.EndOfFrameOrNextUpdate();

            Assert.AreEqual(5f, main.transform.position.x, 0.05f);
            Assert.AreEqual(5f, studio.transform.position.x, 0.05f);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TwoCutscenes_OnSameCamera_FirstWins_WithOneWarning()
        {
            var studio = CreateCamera("MsStudio", false, -50f);

            var first = CreateCameraCutscene();
            var second = CreateCameraCutscene();
            _manager.PlayData(first, new PlayContext(), new CutscenePlayOptions(studio));
            _manager.PlayData(second, new PlayContext(), new CutscenePlayOptions(studio));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("既にカメラを駆動"));
            _manager.Tick(0.25f);
            yield return TestFrameWait.EndOfFrameOrNextUpdate(); // Applier の LateUpdate が Submit を消費する(Tick を続けて呼ぶと検出 3 になる)
            _manager.Tick(0.25f); // 2 回目の警告は出ない(Data ごとに 1 回)
            yield return TestFrameWait.EndOfFrameOrNextUpdate();

            Assert.AreEqual(5f, studio.transform.position.x, 0.05f);
        }

        [UnityTest]
        public IEnumerator DestroyedSpecifiedCamera_EndsThatPlaybackWithOneWarning_OthersContinue()
        {
            var main = CreateCamera("MsMain", true, 100f);
            var studio = CreateCamera("MsStudio", false, -50f);

            var mainHandle = _manager.PlayData(CreateCameraCutscene(), new PlayContext());
            var studioHandle = _manager.PlayData(CreateCameraCutscene(), new PlayContext(), new CutscenePlayOptions(studio));
            _manager.Tick(0.2f);
            yield return TestFrameWait.EndOfFrameOrNextUpdate();

            Object.DestroyImmediate(studio.gameObject);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("対象のカメラが見つからなくなった"));
            _manager.Tick(0.2f);
            yield return TestFrameWait.EndOfFrameOrNextUpdate();
            _manager.Tick(0.2f);
            yield return TestFrameWait.EndOfFrameOrNextUpdate();

            Assert.IsTrue(_manager.IsPlaying(studioHandle), "Cutscene 自体は続く(カメラ演出だけ終了)");
            Assert.IsTrue(_manager.IsPlaying(mainHandle));
            Assert.AreEqual(6f, main.transform.position.x, 0.1f, "Camera.main の本は影響を受けず続く");
        }

        [Test]
        public void IgnoreInputLock_DoesNotCountAsInputLock()
        {
            CreateCamera("MsMain", true, 0f);
            var data = CreateCameraCutscene(lockInput: true);

            var ignored = _manager.PlayData(data, new PlayContext(), new CutscenePlayOptions(null, ignoreInputLock: true));
            Assert.IsFalse(_manager.IsInputLockedAny);
            Assert.IsFalse(_manager.IsInputLocked(ignored));
            _manager.Cancel(ignored);

            var locked = _manager.PlayData(data, new PlayContext());
            Assert.IsTrue(_manager.IsInputLockedAny, "既定は従来どおり LockInput を数える");
            Assert.IsTrue(_manager.IsInputLocked(locked));
        }

        [Test]
        public void MainCameraBinding_ResolvesToSpecifiedCamera()
        {
            var main = CreateCamera("MsMain", true, 0f);
            var studio = CreateCamera("MsStudio", false, 0f);
            var data = CreateCameraCutscene(bindTrackName: "BindProbe");

            _manager.PlayData(data, new PlayContext(), new CutscenePlayOptions(studio));
            PlayableDirector director = null;
            foreach (var d in Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None))
            {
                if (d.playableAsset == data.Timeline)
                {
                    director = d;
                }
            }

            Assert.IsNotNull(director);
            var track = FindTrack(data.Timeline, "BindProbe");

            Assert.AreSame(studio.transform, director.GetGenericBinding(track), "MainCamera バインドは指定カメラの Transform");
            Assert.AreNotSame(main.transform, director.GetGenericBinding(track));
        }

        private static TrackAsset FindTrack(TimelineAsset timeline, string name)
        {
            foreach (var t in timeline.GetOutputTracks())
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
