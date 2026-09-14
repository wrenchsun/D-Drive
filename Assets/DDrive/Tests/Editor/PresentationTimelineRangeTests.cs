using DDrive.Editor.Presentation;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [08_presentation.md] 5-4 追補(2026-09-14) — タイムラインの表示範囲(PresentationTimelineRange)。
    // ユーザー報告「シークバーでどこにいるか分からない」の根本原因(TotalDuration 未設定でタイムラインの
    // 表示範囲が潰れる)への対応。ランタイムの PresentationTiming.EffectiveDuration は変更していないため、
    // このテストでは表示専用の DisplayDuration / SuggestedTotalDuration だけを検証する。
    public class PresentationTimelineRangeTests
    {
        private PresentationData _data;

        [SetUp]
        public void SetUp()
        {
            _data = ScriptableObject.CreateInstance<PresentationData>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_data);
        }

        private static PresentationTrack AtTime(TrackKind kind, float time, AssetRef asset = default)
            => new() { Kind = kind, Trigger = TrackTrigger.AtTime, Time = time, Asset = asset };

        // ── DisplayDuration ──

        [Test]
        public void DisplayDuration_TotalDurationSet_ReturnsAsIs_NoMargin()
        {
            _data.TotalDuration = 3f;
            _data.Tracks = new[] { AtTime(TrackKind.Vfx, 100f) }; // 超過していても TotalDuration をそのまま使う

            Assert.AreEqual(3f, PresentationTimelineRange.DisplayDuration(_data), 0.001f);
        }

        [Test]
        public void DisplayDuration_Unset_WithAtTimeTracks_UsesMaxTimePlusMargin()
        {
            _data.TotalDuration = 0f;
            _data.Tracks = new[] { AtTime(TrackKind.Vfx, 0.5f), AtTime(TrackKind.Se, 2f) };

            Assert.AreEqual(2f + PresentationTimelineRange.AutoMargin, PresentationTimelineRange.DisplayDuration(_data), 0.001f);
        }

        [Test]
        public void DisplayDuration_Unset_NoAtTimeTracks_FallsBackToOneSecond()
        {
            _data.TotalDuration = 0f;
            _data.Tracks = new[] { new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.OnSignal, SignalKey = "hit" } };

            Assert.AreEqual(PresentationTimelineRange.FallbackNoTracksDuration, PresentationTimelineRange.DisplayDuration(_data), 0.001f);
        }

        [Test]
        public void DisplayDuration_Unset_NoTracksAtAll_FallsBackToOneSecond()
        {
            _data.TotalDuration = 0f;
            _data.Tracks = null;

            Assert.AreEqual(PresentationTimelineRange.FallbackNoTracksDuration, PresentationTimelineRange.DisplayDuration(_data));
        }

        [Test]
        public void DisplayDuration_NullData_FallsBackToOneSecond_NoThrow()
        {
            Assert.AreEqual(PresentationTimelineRange.FallbackNoTracksDuration, PresentationTimelineRange.DisplayDuration(null));
        }

        // ── TryGetMaxAtTimeTrackTime ──

        [Test]
        public void TryGetMaxAtTimeTrackTime_MixedTriggers_OnlyCountsAtTime()
        {
            _data.Tracks = new[]
            {
                AtTime(TrackKind.Vfx, 1f),
                new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.OnSignal, Time = 99f, SignalKey = "x" },
                AtTime(TrackKind.Se, 4f),
            };

            var found = PresentationTimelineRange.TryGetMaxAtTimeTrackTime(_data.Tracks, out var max);

            Assert.IsTrue(found);
            Assert.AreEqual(4f, max, 0.001f);
        }

        [Test]
        public void TryGetMaxAtTimeTrackTime_NoAtTimeTracks_ReturnsFalse()
        {
            _data.Tracks = new[] { new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "x" } };

            Assert.IsFalse(PresentationTimelineRange.TryGetMaxAtTimeTrackTime(_data.Tracks, out var max));
            Assert.AreEqual(0f, max);
        }

        // ── EstimateAssetTailSeconds(ベストエフォートの長さ見積り) ──

        [Test]
        public void EstimateAssetTailSeconds_AnimWithoutClip_KnownTrue_UsesFallbackLength()
        {
            var anim = ScriptableObject.CreateInstance<AnimData>();

            var tail = PresentationTimelineRange.EstimateAssetTailSeconds(TrackKind.Anim, anim, out var known);

            Assert.IsTrue(known, "Anim は解決できれば常に長さが分かる(Clip 無しでも FallbackLengthSec)");
            Assert.AreEqual(AnimData.FallbackLengthSec, tail, 0.001f);

            Object.DestroyImmediate(anim);
        }

        [Test]
        public void EstimateAssetTailSeconds_SeWithClip_KnownTrue_SubtractsStartOffset()
        {
            var se = ScriptableObject.CreateInstance<SeData>();
            var clip = AudioClip.Create("clip1s", 44100, 1, 44100, false); // length = 1.0s
            se.Clips = new[] { clip };
            se.StartOffsetSec = 0.3f;

            var tail = PresentationTimelineRange.EstimateAssetTailSeconds(TrackKind.Se, se, out var known);

            Assert.IsTrue(known);
            Assert.AreEqual(0.7f, tail, 0.01f);

            Object.DestroyImmediate(se);
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void EstimateAssetTailSeconds_SeWithoutClips_KnownFalse()
        {
            var se = ScriptableObject.CreateInstance<SeData>();

            PresentationTimelineRange.EstimateAssetTailSeconds(TrackKind.Se, se, out var known);

            Assert.IsFalse(known);

            Object.DestroyImmediate(se);
        }

        [Test]
        public void EstimateAssetTailSeconds_UnhandledKindOrNullAsset_KnownFalse()
        {
            PresentationTimelineRange.EstimateAssetTailSeconds(TrackKind.Vfx, null, out var knownNullAsset);
            Assert.IsFalse(knownNullAsset);

            var se = ScriptableObject.CreateInstance<SeData>();
            PresentationTimelineRange.EstimateAssetTailSeconds(TrackKind.Vfx, se, out var knownWrongKind);
            Assert.IsFalse(knownWrongKind, "Vfx は長さ推定の対象外(要判断。[08] 実装メモ参照)");

            Object.DestroyImmediate(se);
        }

        // ── SuggestedTotalDuration(「トラックの最後に合わせる」ボタンの計算) ──

        [Test]
        public void SuggestedTotalDuration_NoTracks_FallsBackToOneSecond()
        {
            _data.Tracks = null;
            Assert.AreEqual(PresentationTimelineRange.FallbackNoTracksDuration, PresentationTimelineRange.SuggestedTotalDuration(_data));
        }

        [Test]
        public void SuggestedTotalDuration_UnresolvableAsset_UsesMaxTimePlusAutoMargin()
        {
            _data.Tracks = new[] { AtTime(TrackKind.Vfx, 1f, new AssetRef { Id = 999UL }) };

            // リゾルバが常に null を返す(=見つからない)ケース。
            var result = PresentationTimelineRange.SuggestedTotalDuration(_data, (_, _) => null);

            Assert.AreEqual(1f + PresentationTimelineRange.AutoMargin, result, 0.001f);
        }

        [Test]
        public void SuggestedTotalDuration_ResolvedSeAsset_IncorporatesClipLength()
        {
            var se = ScriptableObject.CreateInstance<SeData>();
            var clip = AudioClip.Create("clip1_5s", 66150, 1, 44100, false); // length = 1.5s
            se.Clips = new[] { clip };

            _data.Tracks = new[] { AtTime(TrackKind.Se, 2f, new AssetRef { Id = 42UL }) };

            var result = PresentationTimelineRange.SuggestedTotalDuration(_data, (kind, id) => kind == TrackKind.Se && id == 42UL ? se : null);

            // 2s(Time) + 1.5s(Clip の長さ、StartOffsetSec=0) = 3.5s。AutoMargin(0.5) は使われない。
            Assert.AreEqual(3.5f, result, 0.01f);

            Object.DestroyImmediate(se);
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void SuggestedTotalDuration_PicksMaxAcrossTracks()
        {
            _data.Tracks = new[]
            {
                AtTime(TrackKind.Marker, 5f), // known=false → 5 + 0.5 = 5.5
                AtTime(TrackKind.Vfx, 0.5f), // known=false → 0.5 + 0.5 = 1.0
            };

            var result = PresentationTimelineRange.SuggestedTotalDuration(_data, (_, _) => null);

            Assert.AreEqual(5.5f, result, 0.001f);
        }
    }
}
