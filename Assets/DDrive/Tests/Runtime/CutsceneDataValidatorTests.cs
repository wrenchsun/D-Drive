using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using NUnit.Framework;
using UnityEngine;
using ModelId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Model.ModelMarker>;

namespace DDrive.Tests.Runtime
{
    // [26_timeline.md] §4.1/§4.2/§4.7(6-10a) — 基本チェックのみ(fps 検査等の拡張は 6-10d)。
    public class CutsceneDataValidatorTests
    {
        private readonly List<CutsceneData> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var data in _created)
            {
                if (data != null)
                {
                    Object.DestroyImmediate(data);
                }
            }

            _created.Clear();
        }

        private CutsceneData ValidData()
        {
            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);
            data.FrameRate = 30f;
            data.Origin = CutsceneOrigin.Self;
            data.Skip = CutsceneSkip.Immediate;
            data.Bindings = new[]
            {
                new CutsceneBinding { TrackName = "Camera", Target = CutsceneBindTarget.MainCamera },
                new CutsceneBinding { TrackName = "Hero", Target = CutsceneBindTarget.Self },
            };
            return data;
        }

        private static List<ValidationResult> Validate(CutsceneData data)
            => new CutsceneDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();

        [Test]
        public void ValidData_HasNoErrors()
        {
            var results = Validate(ValidData());
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void MissingTimeline_IsWarning()
        {
            var data = ValidData();
            data.Timeline = null;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Timeline")));
        }

        [Test]
        public void SpawnModelWithoutModel_IsError()
        {
            var data = ValidData();
            data.Bindings = new[] { new CutsceneBinding { TrackName = "Prop", Target = CutsceneBindTarget.SpawnModel } };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("SpawnModel")));
        }

        [Test]
        public void SpawnModelWithModel_HasNoError()
        {
            var data = ValidData();
            data.Bindings = new[]
            {
                new CutsceneBinding { TrackName = "Prop", Target = CutsceneBindTarget.SpawnModel, Model = new ModelId(1, AssetType.Model) },
            };
            var results = Validate(data);
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void DuplicateTrackName_IsWarning()
        {
            var data = ValidData();
            data.Bindings = new[]
            {
                new CutsceneBinding { TrackName = "Hero", Target = CutsceneBindTarget.Self },
                new CutsceneBinding { TrackName = "Hero", Target = CutsceneBindTarget.Target },
            };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("重複")));
        }

        [Test]
        public void EmptyTrackName_IsError()
        {
            var data = ValidData();
            data.Bindings = new[] { new CutsceneBinding { TrackName = "", Target = CutsceneBindTarget.Self } };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("TrackName")));
        }

        [Test]
        public void OriginAnchorPointWithoutName_IsError()
        {
            var data = ValidData();
            data.Origin = CutsceneOrigin.AnchorPoint;
            data.OriginAnchorName = "";
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("OriginAnchorName")));
        }

        [Test]
        public void SceneObjectByNameWithoutName_IsError()
        {
            var data = ValidData();
            data.Bindings = new[] { new CutsceneBinding { TrackName = "Prop", Target = CutsceneBindTarget.SceneObjectByName, SceneObjectName = "" } };
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("SceneObjectName")));
        }

        [Test]
        public void SkipToMarkerWithoutKey_IsWarning()
        {
            var data = ValidData();
            data.Skip = CutsceneSkip.ToMarker;
            data.SkipToMarkerKey = "";
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("SkipToMarkerKey")));
        }

        [Test]
        public void PredictLocalWithoutCosmetic_IsInfo()
        {
            var data = ValidData();
            data.PredictLocal = true;
            var flags = data.Flags;
            flags.Net = NetMode.Local;
            data.Flags = flags;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("PredictLocal")));
        }

        [Test]
        public void SimulatedNet_IsInfo()
        {
            var data = ValidData();
            var flags = data.Flags;
            flags.Net = NetMode.Simulated;
            data.Flags = flags;
            var results = Validate(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("Simulated")));
        }
    }
}
