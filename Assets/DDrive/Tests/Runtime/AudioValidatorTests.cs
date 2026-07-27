using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class SeDataValidatorTests
    {
        private static SeData ValidSe()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            data.Clips = new[] { AudioClip.Create("c", 100, 1, 44100, false) };
            data.Volume = 1f;
            data.MaxConcurrent = 8;
            data.MinDistance = 1f;
            data.MaxDistance = 30f;
            data.Loop = true;
            data.DopplerEnabled = false;
            return data;
        }

        private static List<ValidationResult> Validate(SeData data)
            => new SeDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();

        [Test]
        public void ValidSeData_HasNoErrors()
        {
            var results = Validate(ValidSe());
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void MissingClips_IsError()
        {
            var data = ValidSe();
            data.Clips = null;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Clip")));
        }

        [Test]
        public void MissingMixer_IsWarning()
        {
            var results = Validate(ValidSe());
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Mixer")));
        }

        [Test]
        public void SpatialAnchor_WithoutPath_IsError()
        {
            var data = ValidSe();
            data.Spatial = SpatialMode.Anchor;
            data.Anchor = new AnchorDef { Space = AnchorSpace.BoneName, Path = "" };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Anchor")));
        }

        [Test]
        public void MaxDistanceLessThanMinDistance_IsError()
        {
            var data = ValidSe();
            data.MinDistance = 10f;
            data.MaxDistance = 5f;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("MaxDistance")));
        }

        [Test]
        public void DopplerWithoutLoop_IsWarning()
        {
            var data = ValidSe();
            data.DopplerEnabled = true;
            data.Loop = false;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Doppler")));
        }

        [Test]
        public void NonPositiveMaxConcurrent_IsError()
        {
            var data = ValidSe();
            data.MaxConcurrent = 0;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("MaxConcurrent")));
        }

        [Test]
        public void ZeroVolume_IsWarning()
        {
            var data = ValidSe();
            data.Volume = 0f;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Volume")));
        }

        [Test]
        public void SourcesCountMismatchWithClips_IsWarning()
        {
            var data = ValidSe();
            data.Sources = new[]
            {
                new SeClipSource { Source = AudioClip.Create("s1", 10, 1, 44100, false) },
                new SeClipSource { Source = AudioClip.Create("s2", 10, 1, 44100, false) },
            };

            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Sources")));
        }

        [Test]
        public void StartOffsetBeyondClipLength_IsWarning()
        {
            var data = ValidSe();
            data.Clips = new[] { AudioClip.Create("c", 1000, 1, 1000, false) }; // 1秒クリップ(Unity最低周波数1000Hz)
            data.StartOffsetSec = 2f;

            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("StartOffsetSec")));
        }
    }

    public class BgmDataValidatorTests
    {
        private static BgmData ValidBgm()
        {
            var data = ScriptableObject.CreateInstance<BgmData>();
            data.LoopBody = AudioClip.Create("loop", 100, 1, 44100, false);
            data.Volume = 1f;
            data.LoopStartSec = 0.0;
            data.LoopEndSec = 10.0;
            return data;
        }

        private static List<ValidationResult> Validate(BgmData data)
            => new BgmDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();

        [Test]
        public void ValidBgmData_HasNoErrors()
        {
            var results = Validate(ValidBgm());
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void MissingLoopBody_IsError()
        {
            var data = ValidBgm();
            data.LoopBody = null;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("LoopBody")));
        }

        [Test]
        public void LoopEndBeforeLoopStart_IsError()
        {
            var data = ValidBgm();
            data.LoopStartSec = 5.0;
            data.LoopEndSec = 2.0;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("LoopEndSec")));
        }

        [Test]
        public void ZeroVolume_IsWarning()
        {
            var data = ValidBgm();
            data.Volume = 0f;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning));
        }
    }
}
