using System.Collections.Generic;
using DDrive.Editor.Cutscene;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §5.3(6-10d) — CutsceneFpsValidator の fps 検査 6 種。
    // CutsceneImportProfile.FindOrDefault() はプロジェクトに実アセットが無ければ組み込み既定
    // (DefaultFrameRate=30)を返す(現状 Assets/GameData/Settings に CutsceneImportProfile.asset は無い)。
    public class CutsceneFpsValidatorTests
    {
        private readonly List<Object> _created = new();

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

        private TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            return timeline;
        }

        private CutsceneData CreateData(TimelineAsset timeline, float frameRate)
        {
            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);
            data.Timeline = timeline;
            data.FrameRate = frameRate;
            return data;
        }

        private AnimationClip CreateClip(float frameRate)
        {
            var clip = new AnimationClip { frameRate = frameRate };
            _created.Add(clip);
            return clip;
        }

        private static List<ValidationResult> Run(CutsceneData data)
        {
            var validator = new CutsceneFpsValidator();
            var ctx = new ValidationContext(new List<AssetDataBase> { data });
            return new List<ValidationResult>(validator.Validate(data, ctx));
        }

        private static bool Any(List<ValidationResult> results, ValidationSeverity severity, string contains)
        {
            foreach (var r in results)
            {
                if (r.Severity == severity && r.Message.Contains(contains))
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void MixedClipFrameRates_IsWarning()
        {
            var timeline = CreateTimeline();
            var heroTrack = timeline.CreateTrack<AnimationTrack>(null, "Hero");
            heroTrack.CreateClip(CreateClip(30f));
            var propTrack = timeline.CreateTrack<AnimationTrack>(null, "Sword");
            propTrack.CreateClip(CreateClip(60f));

            var data = CreateData(timeline, 30f);
            var results = Run(data);

            Assert.IsTrue(Any(results, ValidationSeverity.Warning, "一致していません"), "混在した fps が検出されていない");
        }

        [Test]
        public void FrameRateMismatchWithClip_IsWarning_AndFixActionAligns()
        {
            var timeline = CreateTimeline();
            var track = timeline.CreateTrack<AnimationTrack>(null, "Hero");
            track.CreateClip(CreateClip(60f));

            var data = CreateData(timeline, 30f);
            var results = Run(data);

            ValidationResult? mismatch = null;
            foreach (var r in results)
            {
                if (r.Severity == ValidationSeverity.Warning && r.Message.Contains("FrameRate") && r.FixAction != null)
                {
                    mismatch = r;
                }
            }

            Assert.IsTrue(mismatch.HasValue, "FrameRate 不一致の Warning(FixAction 付き)が見つからない");
            mismatch.Value.FixAction();
            Assert.AreEqual(60f, data.FrameRate, "FixAction が FBX(クリップ)の fps に合わせていない");
        }

        [Test]
        public void FrameRateDiffersFromProjectDefault_IsInfo()
        {
            var data = CreateData(CreateTimeline(), 60f); // 既定(組み込み) DefaultFrameRate=30 と不一致
            var results = Run(data);

            Assert.IsTrue(Any(results, ValidationSeverity.Info, "プロジェクト既定 fps"));
        }

        [Test]
        public void FrameRateNot30Or60_IsInfo()
        {
            var data = CreateData(CreateTimeline(), 45f);
            var results = Run(data);

            Assert.IsTrue(Any(results, ValidationSeverity.Info, "30 / 60 以外"));
        }

        [Test]
        public void FrameRate30Or60_NoNot30Or60Info()
        {
            var data = CreateData(CreateTimeline(), 30f);
            var results = Run(data);

            Assert.IsFalse(Any(results, ValidationSeverity.Info, "30 / 60 以外"));
        }

        [Test]
        public void CameraStepFpsGreaterThanFrameRate_IsWarning()
        {
            var timeline = CreateTimeline();
            var camTrack = timeline.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var clip = camTrack.CreateClip<CutsceneCameraClip>();
            ((CutsceneCameraClip)clip.asset).StepFps = 90f;

            var data = CreateData(timeline, 30f);
            var results = Run(data);

            Assert.IsTrue(Any(results, ValidationSeverity.Warning, "を超えています"));
        }

        [Test]
        public void CameraStepFpsNotIntegerMultiple_IsWarning()
        {
            var timeline = CreateTimeline();
            var camTrack = timeline.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var clip = camTrack.CreateClip<CutsceneCameraClip>();
            ((CutsceneCameraClip)clip.asset).StepFps = 24f; // 30 / 24 = 1.25(整数でない)

            var data = CreateData(timeline, 30f);
            var results = Run(data);

            Assert.IsTrue(Any(results, ValidationSeverity.Warning, "整数倍ではありません"));
        }

        [Test]
        public void CameraStepFpsIntegerMultiple_NoWarning()
        {
            var timeline = CreateTimeline();
            var camTrack = timeline.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var clip = camTrack.CreateClip<CutsceneCameraClip>();
            ((CutsceneCameraClip)clip.asset).StepFps = 15f; // 30 / 15 = 2

            var data = CreateData(timeline, 30f);
            var results = Run(data);

            Assert.IsFalse(Any(results, ValidationSeverity.Warning, "整数倍ではありません"));
            Assert.IsFalse(Any(results, ValidationSeverity.Warning, "を超えています"));
        }
    }
}
