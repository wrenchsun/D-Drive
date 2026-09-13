using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Tests.Runtime
{
    // [08_presentation.md] §6。
    public class PresentationDataValidatorTests
    {
        private static PresentationData ValidData()
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Tracks = new[]
            {
                new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Vfx, Asset = AssetRef.From(new VfxId(1, AssetType.Vfx)) },
                new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "hit", Kind = TrackKind.HitStop, Params = new[] { ParamValue.Of(0.05f) } },
            };
            data.TotalDuration = 1f;
            data.Interruptible = true;
            return data;
        }

        private static List<ValidationResult> Validate(PresentationData data)
            => new PresentationDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();

        [Test]
        public void ValidData_HasNoErrors()
        {
            var results = Validate(ValidData());
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void MissingAssetOnVfxTrack_IsError()
        {
            var data = ValidData();
            var tracks = data.Tracks;
            tracks[0].Asset = default;
            data.Tracks = tracks;

            var results = Validate(data);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Asset")));
        }

        [Test]
        public void OnSignalTrack_EmptySignalKey_IsError()
        {
            var data = ValidData();
            var tracks = data.Tracks;
            tracks[1].SignalKey = string.Empty;
            data.Tracks = tracks;

            var results = Validate(data);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("SignalKey")));
        }

        [Test]
        public void MarkerTrack_EmptyName_IsError()
        {
            var data = ValidData();
            data.Tracks = new[]
            {
                new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "" },
            };

            var results = Validate(data);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("名前")));
        }

        [Test]
        public void AtTimeExceedsTotalDuration_IsWarning()
        {
            var data = ValidData();
            data.TotalDuration = 0.1f;
            var tracks = data.Tracks;
            tracks[0].Time = 5f;
            data.Tracks = tracks;

            var results = Validate(data);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("TotalDuration")));
        }

        [Test]
        public void NonInterruptible_LongDuration_IsWarning()
        {
            var data = ValidData();
            data.Interruptible = false;
            data.TotalDuration = 15f;

            var results = Validate(data);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Interruptible")));
        }

        [Test]
        public void SelfReferencingTrack_IsError()
        {
            var data = ValidData();
            data.Id = 555;
            var tracks = data.Tracks;
            tracks[0].Asset = new AssetRef { Type = AssetType.Presentation, Id = 555 };
            data.Tracks = tracks;

            var results = Validate(data);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("循環")));
        }
    }
}
