using DDrive.Editor.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    public class AudioClipTrimUtilityTests
    {
        private const int SampleRate = 1000;

        private static AudioClip CreateClip(float[] samples, string name = "clip")
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        [Test]
        public void TryDetectSilenceTrim_FindsLoudRegionBetweenSilence()
        {
            // 3 無音 + 4 有音(0.5) + 3 無音 = samples[3..6] が有音区間。
            var samples = new float[10];
            samples[3] = 0.5f;
            samples[4] = 0.5f;
            samples[5] = 0.5f;
            samples[6] = 0.5f;
            var clip = CreateClip(samples);

            var found = AudioClipTrimUtility.TryDetectSilenceTrim(clip, 0.01f, out var start, out var end);

            Assert.IsTrue(found);
            Assert.AreEqual(3f / SampleRate, start, 1e-6f);
            Assert.AreEqual(7f / SampleRate, end, 1e-6f);
        }

        [Test]
        public void TryDetectSilenceTrim_AllSilence_ReturnsZeroLengthRange()
        {
            var samples = new float[10];
            var clip = CreateClip(samples);

            var found = AudioClipTrimUtility.TryDetectSilenceTrim(clip, 0.01f, out var start, out var end);

            Assert.IsTrue(found);
            Assert.AreEqual(0f, start);
            Assert.AreEqual(0f, end);
        }

        [Test]
        public void TryDetectSilenceTrim_NullClip_ReturnsFalse()
        {
            var found = AudioClipTrimUtility.TryDetectSilenceTrim(null, 0.01f, out _, out _);
            Assert.IsFalse(found);
        }

        [Test]
        public void CreateTrimmedClip_ExtractsExactSubRange()
        {
            var samples = new float[10];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = i / 10f;
            }

            var clip = CreateClip(samples);

            var trimmed = AudioClipTrimUtility.CreateTrimmedClip(clip, 3f / SampleRate, 7f / SampleRate);

            Assert.IsNotNull(trimmed);
            Assert.AreEqual(4, trimmed.samples);

            var trimmedData = new float[trimmed.samples];
            trimmed.GetData(trimmedData, 0);

            CollectionAssert.AreEqual(new[] { samples[3], samples[4], samples[5], samples[6] }, trimmedData);
        }

        [Test]
        public void CreateTrimmedClip_EndSecNotGreaterThanStart_TrimsToClipEnd()
        {
            var samples = new float[10];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = i / 10f;
            }

            var clip = CreateClip(samples);

            var trimmed = AudioClipTrimUtility.CreateTrimmedClip(clip, 5f / SampleRate, 0f);

            Assert.IsNotNull(trimmed);
            Assert.AreEqual(5, trimmed.samples);
        }

        [Test]
        public void CreateTrimmedClip_NullSource_ReturnsNull()
        {
            Assert.IsNull(AudioClipTrimUtility.CreateTrimmedClip(null, 0f, 1f));
        }
    }
}
