using DDrive.Editor.Preview;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // AC(1-6): EditMode で SE/ループ/速度変更が動く。
    // 音の聴感そのものは検証できないため、実 AudioManager の観測可能な状態
    // (isPlaying/pitch/loop)と、プレビューシーンのライフサイクルを検証する。
    public class PreviewServiceTests
    {
        private PreviewService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new PreviewService();
            _service.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        private static SeData CreateSeData(bool loop = false)
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            data.Id = 42;
            data.Clips = new[] { AudioClip.Create("preview", 44100, 1, 44100, false) };
            data.Volume = 1f;
            data.MaxConcurrent = 8;
            data.Loop = loop;
            return data;
        }

        [Test]
        public void PlaySe_InEditMode_StartsPlaybackThroughRealAudioManager()
        {
            var handle = _service.PlaySe(CreateSeData());

            Assert.IsTrue(_service.AudioManager.IsPlaying(handle));
        }

        [Test]
        public void PlaySe_WithForceLoop_PlaysLoopedWithoutMutatingOriginalAsset()
        {
            var data = CreateSeData(loop: false);
            var handle = _service.PlaySe(data, forceLoop: true);

            Assert.IsTrue(_service.AudioManager.GetLoop(handle) ?? false, "preview playback should be looped");
            Assert.IsFalse(data.Loop, "original asset must stay non-looped (non-destructive preview)");
        }

        [Test]
        public void Speed_AppliesToActivePlaybackAsPitch()
        {
            var handle = _service.PlaySe(CreateSeData());

            _service.Speed = 1.5f;

            Assert.AreEqual(1.5f, _service.AudioManager.GetPitch(handle) ?? 0f, 1e-4f);
        }

        [Test]
        public void Speed_IsClampedToDocumentedRange()
        {
            _service.Speed = 99f;
            Assert.AreEqual(2f, _service.Speed);

            _service.Speed = 0f;
            Assert.AreEqual(0.1f, _service.Speed);
        }

        [Test]
        public void StopAll_StopsPlayback()
        {
            var handle = _service.PlaySe(CreateSeData(loop: true));
            Assert.IsTrue(_service.AudioManager.IsPlaying(handle));

            _service.StopAll();

            Assert.IsFalse(_service.AudioManager.IsPlaying(handle));
        }

        [Test]
        public void Tick_DrivesManagersWithoutError()
        {
            _service.PlaySe(CreateSeData(loop: true));

            for (var i = 0; i < 10; i++)
            {
                _service.Tick(0.016f);
            }

            Assert.Pass();
        }

        [Test]
        public void PlayBgm_StartsBgmPlayback()
        {
            var bgm = ScriptableObject.CreateInstance<BgmData>();
            bgm.LoopBody = AudioClip.Create("bgm", 44100, 1, 44100, false);
            bgm.Volume = 1f;

            _service.PlayBgm(bgm);
            _service.Tick(0.1f);

            Assert.IsTrue(_service.BgmManager.IsPlaying);
        }

        [Test]
        public void Dispose_TearsDownCleanly_AndSecondDisposeIsSafe()
        {
            Assert.IsTrue(_service.IsInitialized);
            _service.Dispose();
            Assert.IsFalse(_service.IsInitialized);
            Assert.DoesNotThrow(() => _service.Dispose());
        }
    }
}
