using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Tests.Runtime
{
    // [08_presentation.md] §6。
    public class PresentationDataValidatorTests
    {
        // P5 レビュー第 1 弾 整理-4(2026-09-14): ValidData() が作る ScriptableObject.CreateInstance が
        // どのテストでも DestroyImmediate されず、テスト実行ごとにリークしていた。生成した分をここに集め、
        // TearDown でまとめて破棄する。
        private readonly List<PresentationData> _created = new();

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

        private PresentationData ValidData()
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            _created.Add(data);
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
        public void AnchorGroupTrack_AssetTypeMismatch_IsError()
        {
            var data = ValidData();
            data.Tracks = new[]
            {
                // Kind=AnchorGroup なのに Asset.Type が Vfx のまま(取り違えたデータ)。
                new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.AnchorGroup, Asset = AssetRef.From(new VfxId(1, AssetType.Vfx)) },
            };

            var results = Validate(data);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("種別")));
        }

        [Test]
        public void AnchorGroupTrack_CorrectAssetType_HasNoError()
        {
            var data = ValidData();
            data.Tracks = new[]
            {
                new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.AnchorGroup, Asset = new AssetRef { Type = AssetType.AnchorGroup, Id = 1 } },
            };

            var results = Validate(data);
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        // [08_presentation.md] 実装メモ(2026-09-19、トラック/アセット両方の Anchor 参照)。
        [Test]
        public void VfxTrack_BothTrackAndAssetAnchorSet_IsInfo()
        {
            var vfx = ScriptableObject.CreateInstance<VfxData>();
            try
            {
                vfx.Id = 42;
                vfx.Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Hand", LocalScale = Vector3.one };

                var data = ValidData();
                var tracks = data.Tracks;
                tracks[0] = new PresentationTrack
                {
                    Trigger = TrackTrigger.AtTime,
                    Time = 0f,
                    Kind = TrackKind.Vfx,
                    Asset = new AssetRef { Type = AssetType.Vfx, Id = 42 },
                    Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Foot", LocalScale = Vector3.one },
                };
                data.Tracks = tracks;

                var results = new PresentationDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data, vfx })).ToList();
                Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("親子合成")));
            }
            finally
            {
                Object.DestroyImmediate(vfx);
            }
        }

        [Test]
        public void VfxTrack_OnlyAssetAnchorSet_NoAnchorInfo()
        {
            var vfx = ScriptableObject.CreateInstance<VfxData>();
            try
            {
                vfx.Id = 43;
                vfx.Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Hand", LocalScale = Vector3.one };

                var data = ValidData();
                var tracks = data.Tracks;
                tracks[0] = new PresentationTrack
                {
                    Trigger = TrackTrigger.AtTime,
                    Time = 0f,
                    Kind = TrackKind.Vfx,
                    Asset = new AssetRef { Type = AssetType.Vfx, Id = 43 },
                    // トラック側は既定値(未設定)のまま。
                };
                data.Tracks = tracks;

                var results = new PresentationDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data, vfx })).ToList();
                Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("親子合成")));
            }
            finally
            {
                Object.DestroyImmediate(vfx);
            }
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

        // [11_tasks.md] N-4(2026-09-22) — Scope=ParticipantsOnly は「ネット受信 Instance に限り、当事者以外の
        // 発火を抑える」機能のため、Flags.Net=Local(常にローカル、ネット再生自体をしない)な Presentation では
        // 意味を持たない。[14_networking.md] §5 実装メモ参照。
        [Test]
        public void ParticipantsOnlyScope_OnLocalPresentation_IsInfo()
        {
            var data = ValidData();
            data.Flags.Net = NetMode.Local;
            var tracks = data.Tracks;
            tracks[1].Scope = PresentationEffectScope.ParticipantsOnly; // tracks[1] は HitStop
            data.Tracks = tracks;

            var results = Validate(data);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("ParticipantsOnly")));
        }

        [Test]
        public void ParticipantsOnlyScope_OnCosmeticPresentation_HasNoInfo()
        {
            var data = ValidData();
            data.Flags.Net = NetMode.Cosmetic;
            var tracks = data.Tracks;
            tracks[1].Scope = PresentationEffectScope.ParticipantsOnly; // tracks[1] は HitStop
            data.Tracks = tracks;

            var results = Validate(data);
            Assert.IsFalse(results.Any(r => r.Message.Contains("ParticipantsOnly")));
        }
    }
}
