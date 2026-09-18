using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;
using ModelId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Model.ModelMarker>;
using PresentationId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Presentation.PresentationMarker>;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;

namespace DDrive.Tests.Runtime
{
    // [26_timeline.md] §4.1/§4.2/§4.7(6-10a)・§5.4/§6(6-10d) — 基本チェック + Humanoid/Avatar 不整合・
    // Presentation⇄Cutscene 循環参照・Cosmetic+Simulated 参照・OnSignal 付き Presentation クリップ。
    public class CutsceneDataValidatorTests
    {
        private readonly List<CutsceneData> _created = new();
        private readonly List<Object> _createdExtra = new();

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

            foreach (var o in _createdExtra)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _createdExtra.Clear();
        }

        private TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _createdExtra.Add(timeline);
            return timeline;
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

        private static List<ValidationResult> Validate(CutsceneData data, params AssetDataBase[] extraAssets)
        {
            var all = new List<AssetDataBase> { data };
            all.AddRange(extraAssets);
            return new CutsceneDataValidator().Validate(data, new ValidationContext(all)).ToList();
        }

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

        // ── 6-10d ──

        // [26_timeline.md] §5.4 — TrackHasHumanoidClip は AnimationClip.humanMotion(実 Humanoid FBX 由来の
        // 読み取り専用プロパティ。テストで直接 true を作れない)を見るため、この単体テストは「Humanoid
        // クリップでない(既定の AnimationClip)ときは Avatar 未設定でも Warning にならない」負のケースだけを
        // 固定する。Warning が実際に出る側の確認は実 Maya FBX を使った手動確認(docs/43)に委ねる
        // ([26_timeline.md] §7.3 と同種の制約)。
        [Test]
        public void SpawnModelBinding_NonHumanoidClip_MissingAvatar_NoWarning()
        {
            var timeline = CreateTimeline();
            var track = timeline.CreateTrack<AnimationTrack>(null, "Prop");
            var clip = new AnimationClip();
            _createdExtra.Add(clip);
            track.CreateClip(clip);

            var model = ScriptableObject.CreateInstance<ModelData>();
            _createdExtra.Add(model);
            model.Id = 900;
            model.Avatar = null;

            var data = ValidData();
            data.Timeline = timeline;
            data.Bindings = new[]
            {
                new CutsceneBinding { TrackName = "Prop", Target = CutsceneBindTarget.SpawnModel, Model = new ModelId(900, AssetType.Model) },
            };

            var results = Validate(data, model);
            Assert.IsFalse(results.Exists(r => r.Message.Contains("Humanoid Avatar")));
        }

        [Test]
        public void CosmeticCutscene_ReferencesSimulatedSe_IsInfo()
        {
            var timeline = CreateTimeline();
            var track = timeline.CreateTrack<CutsceneSeTrack>(null, "SE");
            var clip = track.CreateClip<CutsceneSeClip>();
            ((CutsceneSeClip)clip.asset).SeId = new SeId(42, AssetType.Se);

            var se = ScriptableObject.CreateInstance<DDrive.Runtime.Audio.SeData>();
            _createdExtra.Add(se);
            se.Id = 42;
            var seFlags = se.Flags;
            seFlags.Net = NetMode.Simulated;
            se.Flags = seFlags;

            var data = ValidData();
            data.Timeline = timeline;
            var cosmeticFlags = data.Flags;
            cosmeticFlags.Net = NetMode.Cosmetic;
            data.Flags = cosmeticFlags;

            var results = Validate(data, se);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("Simulated")));
        }

        [Test]
        public void PresentationClipWithOnSignalTrack_IsWarning()
        {
            var timeline = CreateTimeline();
            var track = timeline.CreateTrack<CutscenePresentationTrack>(null, "Presentation");
            var clip = track.CreateClip<CutscenePresentationClip>();
            ((CutscenePresentationClip)clip.asset).PresentationId = new PresentationId(7, AssetType.Presentation);

            var presentation = ScriptableObject.CreateInstance<PresentationData>();
            _createdExtra.Add(presentation);
            presentation.Id = 7;
            presentation.Tracks = new[]
            {
                new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.OnSignal, SignalKey = "hit" },
            };

            var data = ValidData();
            data.Timeline = timeline;

            var results = Validate(data, presentation);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("OnSignal")));
        }

        [Test]
        public void PresentationReferencesBackToCutscene_IsCycleError()
        {
            var timeline = CreateTimeline();
            var track = timeline.CreateTrack<CutscenePresentationTrack>(null, "Presentation");
            var clip = track.CreateClip<CutscenePresentationClip>();
            ((CutscenePresentationClip)clip.asset).PresentationId = new PresentationId(8, AssetType.Presentation);

            var data = ValidData();
            data.Timeline = timeline;
            data.Id = 123;

            var presentation = ScriptableObject.CreateInstance<PresentationData>();
            _createdExtra.Add(presentation);
            presentation.Id = 8;
            presentation.Tracks = new[]
            {
                new PresentationTrack { Kind = TrackKind.Timeline, Trigger = TrackTrigger.AtTime, Asset = new AssetRef { Type = AssetType.Cutscene, Id = 123 } },
            };

            var results = Validate(data, presentation);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("循環参照")));
        }
    }
}
