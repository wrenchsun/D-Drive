using System.Collections;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    public class BgmManagerTests
    {
        private GameObject _channelObjectA;
        private GameObject _channelObjectB;
        private AudioSource _channelA;
        private AudioSource _channelB;
        private BgmManager _manager;

        [SetUp]
        public void SetUp()
        {
            _channelObjectA = new GameObject("BgmChannelA");
            _channelA = _channelObjectA.AddComponent<AudioSource>();
            _channelObjectB = new GameObject("BgmChannelB");
            _channelB = _channelObjectB.AddComponent<AudioSource>();
            _manager = new BgmManager(new AssetRegistry(new FakeAssetLoader()), _channelA, _channelB);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_channelObjectA);
            Object.DestroyImmediate(_channelObjectB);
        }

        private static AudioClip CreateClip(float seconds)
        {
            return AudioClip.Create("clip", Mathf.Max(1, (int)(44100 * seconds)), 1, 44100, false);
        }

        private static ValueDef FastFade(float seconds) => new()
        {
            Mode = ValueMode.Parametric,
            Parametric = EaseDef.Named(DDrive.Foundation.Easing.Ease.Linear),
            From = 0f,
            To = 1f,
            Time = TimeDef.Duration(seconds),
        };

        [UnityTest]
        public IEnumerator PlayBgmData_WithoutIntro_StartsLoopingPlayback()
        {
            var data = ScriptableObject.CreateInstance<BgmData>();
            data.LoopBody = CreateClip(1f);
            data.Volume = 0.8f;

            _manager.PlayBgmData(data);

            yield return new WaitForSeconds(0.15f);
            _manager.Tick(0.15f);

            Assert.IsTrue(_manager.IsPlaying);
        }

        [UnityTest]
        public IEnumerator PlayBgmData_WithIntro_KeepsPlayingThroughHandoff()
        {
            var data = ScriptableObject.CreateInstance<BgmData>();
            data.Intro = CreateClip(0.2f);
            data.LoopBody = CreateClip(1f);
            data.Volume = 1f;

            _manager.PlayBgmData(data);

            yield return new WaitForSeconds(0.1f);
            _manager.Tick(0.1f);
            Assert.IsTrue(_manager.IsPlaying, "should be playing the intro by now");

            yield return new WaitForSeconds(0.35f);
            _manager.Tick(0.35f);
            Assert.IsTrue(_manager.IsPlaying, "should have handed off to the loop body and still be playing");
        }

        [UnityTest]
        public IEnumerator PlayBgmData_Crossfade_OldFadesOutNewFadesIn()
        {
            var first = ScriptableObject.CreateInstance<BgmData>();
            first.LoopBody = CreateClip(3f);
            first.Volume = 1f;
            first.FadeOut = FastFade(0.2f);

            _manager.PlayBgmData(first);
            yield return new WaitForSeconds(0.15f);
            _manager.Tick(0.15f);

            var second = ScriptableObject.CreateInstance<BgmData>();
            second.LoopBody = CreateClip(3f);
            second.Volume = 0.5f;
            second.FadeIn = FastFade(0.2f);

            _manager.PlayBgmData(second);
            yield return new WaitForSeconds(0.05f);
            _manager.Tick(0.05f);

            // クロスフェード開始直後: 新トラックはまだ 0 に近く、旧トラックはまだ鳴っている。
            Assert.IsTrue(_manager.IsPlaying);

            yield return new WaitForSeconds(0.3f);
            _manager.Tick(0.3f);

            // クロスフェード完了後も再生は継続している(新トラック側)。
            Assert.IsTrue(_manager.IsPlaying);
        }

        [UnityTest]
        public IEnumerator StopBgm_FadesOutThenStops()
        {
            var data = ScriptableObject.CreateInstance<BgmData>();
            data.LoopBody = CreateClip(2f);
            data.Volume = 1f;
            data.FadeOut = FastFade(0.1f);

            _manager.PlayBgmData(data);
            yield return new WaitForSeconds(0.1f);
            _manager.Tick(0.1f);
            Assert.IsTrue(_manager.IsPlaying);

            _manager.StopBgm();

            yield return new WaitForSeconds(0.05f);
            _manager.Tick(0.05f);
            Assert.IsTrue(_manager.IsPlaying, "still fading out");

            yield return new WaitForSeconds(0.2f);
            _manager.Tick(0.2f);
            Assert.IsFalse(_manager.IsPlaying, "fade-out duration elapsed, should be stopped");
        }

        [UnityTest]
        public IEnumerator PlayBgmData_CustomLoopPoints_SplicesToNextChannelAtLoopEnd()
        {
            var data = ScriptableObject.CreateInstance<BgmData>();
            data.LoopBody = CreateClip(2f);
            data.LoopStartSec = 0.2;
            data.LoopEndSec = 0.5;
            data.Volume = 1f;

            _manager.PlayBgmData(data);

            yield return new WaitForSeconds(0.1f);
            _manager.Tick(0.1f);
            Assert.IsTrue(_manager.IsPlaying);

            // ループ区間(0.3秒)+ルックアヘッドを超えるまで待ち、継ぎ目後も再生継続していることを確認する。
            yield return new WaitForSeconds(0.5f);
            _manager.Tick(0.5f);
            Assert.IsTrue(_manager.IsPlaying, "should have spliced to the other channel and kept playing");
        }
    }
}
