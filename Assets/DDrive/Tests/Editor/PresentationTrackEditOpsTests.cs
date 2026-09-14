using DDrive.Editor.Presentation;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-4 — PresentationEditor のトラック追加/移動/削除/複製が Undo 1 回で戻ることを検証する。
    // ウィンドウ(PresentationEditorWindow)を起動せずに検証できるよう、実際の操作は
    // PresentationTrackEditOps(純粋な Undo 付き操作)に切り出してある(CameraFxPresets と同じ設計)。
    public class PresentationTrackEditOpsTests
    {
        private PresentationData _data;

        [SetUp]
        public void SetUp()
        {
            _data = ScriptableObject.CreateInstance<PresentationData>();
            _data.Tracks = new[]
            {
                new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.AtTime, Time = 0f, SignalKey = "start" },
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_data);
        }

        [Test]
        public void AddTrack_AppendsTrack_AndUndoRestoresOriginal()
        {
            Undo.IncrementCurrentGroup();
            var index = PresentationTrackEditOps.AddTrack(_data, TrackKind.Vfx, 1.5f, null);

            Assert.AreEqual(1, index);
            Assert.AreEqual(2, _data.Tracks.Length);
            Assert.AreEqual(TrackKind.Vfx, _data.Tracks[1].Kind);
            Assert.AreEqual(1.5f, _data.Tracks[1].Time, 0.001f);

            Undo.PerformUndo();
            Assert.AreEqual(1, _data.Tracks.Length, "Undo 1 回で追加前に戻る");
            Assert.AreEqual(TrackKind.Marker, _data.Tracks[0].Kind);
        }

        [Test]
        public void AddTrack_WithAsset_SetsAssetRef_MatchingKind()
        {
            var vfx = ScriptableObject.CreateInstance<DDrive.Runtime.Vfx.VfxData>();
            vfx.Id = 12345UL;

            Undo.IncrementCurrentGroup();
            PresentationTrackEditOps.AddTrack(_data, TrackKind.Vfx, 0f, vfx);

            var added = _data.Tracks[^1];
            Assert.AreEqual(DDrive.Foundation.Identity.AssetType.Vfx, added.Asset.Type);
            Assert.AreEqual(12345UL, added.Asset.Id);

            Object.DestroyImmediate(vfx);
        }

        [Test]
        public void RemoveTrack_RemovesAtIndex_AndUndoRestoresOriginal()
        {
            PresentationTrackEditOps.AddTrack(_data, TrackKind.Vfx, 1f, null);
            Assert.AreEqual(2, _data.Tracks.Length);

            Undo.IncrementCurrentGroup();
            PresentationTrackEditOps.RemoveTrack(_data, 0);
            Assert.AreEqual(1, _data.Tracks.Length);
            Assert.AreEqual(TrackKind.Vfx, _data.Tracks[0].Kind);

            Undo.PerformUndo();
            Assert.AreEqual(2, _data.Tracks.Length, "Undo 1 回で削除前に戻る");
            Assert.AreEqual(TrackKind.Marker, _data.Tracks[0].Kind);
        }

        [Test]
        public void DuplicateTrack_InsertsCopyRightAfter_AndUndoRestoresOriginal()
        {
            Undo.IncrementCurrentGroup();
            PresentationTrackEditOps.DuplicateTrack(_data, 0);

            Assert.AreEqual(2, _data.Tracks.Length);
            Assert.AreEqual(_data.Tracks[0].Kind, _data.Tracks[1].Kind);
            Assert.AreEqual(_data.Tracks[0].SignalKey, _data.Tracks[1].SignalKey);

            Undo.PerformUndo();
            Assert.AreEqual(1, _data.Tracks.Length, "Undo 1 回で複製前に戻る");
        }

        [Test]
        public void SetTrackTime_MultipleFrames_CollapsedIntoOneUndo_RestoresOriginalTime()
        {
            var group = Undo.GetCurrentGroup();

            // ドラッグ中の複数フレーム分を模擬(DrawTimeline の MouseDrag と同じ呼び方)。
            PresentationTrackEditOps.SetTrackTime(_data, 0, 0.5f);
            PresentationTrackEditOps.SetTrackTime(_data, 0, 1.0f);
            PresentationTrackEditOps.SetTrackTime(_data, 0, 1.5f);
            Undo.CollapseUndoOperations(group);

            Assert.AreEqual(1.5f, _data.Tracks[0].Time, 0.001f);

            Undo.PerformUndo();
            Assert.AreEqual(0f, _data.Tracks[0].Time, 0.001f, "ドラッグ全体が Undo 1 回で最初の時刻に戻る");
        }

        [Test]
        public void RemoveTrack_OutOfRange_DoesNothing()
        {
            PresentationTrackEditOps.RemoveTrack(_data, 5);
            Assert.AreEqual(1, _data.Tracks.Length);
        }

        [Test]
        public void AddTrack_NullData_ReturnsMinusOne_NoThrow()
        {
            Assert.AreEqual(-1, PresentationTrackEditOps.AddTrack(null, TrackKind.Vfx, 0f, null));
        }
    }
}
