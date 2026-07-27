using DDrive.Editor.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    public class ListenerPadMathTests
    {
        private const float Epsilon = 1e-4f;

        [Test]
        public void SourceOffset_ListenerBehindSource_FacingForward_SourceIsAhead()
        {
            // リスナーが音源の手前(南)5m、正面(0°=奥向き)を向いている → 音源は前方 5m。
            var local = ListenerPadMath.SourceOffsetInListenerLocal(new Vector2(0f, -5f), 0f);

            Assert.AreEqual(0f, local.x, Epsilon);
            Assert.AreEqual(5f, local.z, Epsilon);
        }

        [Test]
        public void SourceOffset_ListenerFacingEast_SourceAppearsOnLeft()
        {
            // リスナーが南5m・東(90°)向き → 音源(北方向)は左手 5m に聞こえるはず。
            var local = ListenerPadMath.SourceOffsetInListenerLocal(new Vector2(0f, -5f), 90f);

            Assert.AreEqual(-5f, local.x, Epsilon);
            Assert.AreEqual(0f, local.z, Epsilon);
        }

        [Test]
        public void SourceOffset_FullTurn_MatchesZeroDegrees()
        {
            var at0 = ListenerPadMath.SourceOffsetInListenerLocal(new Vector2(2f, -3f), 0f);
            var at360 = ListenerPadMath.SourceOffsetInListenerLocal(new Vector2(2f, -3f), 360f);

            Assert.AreEqual(at0.x, at360.x, Epsilon);
            Assert.AreEqual(at0.z, at360.z, Epsilon);
        }

        [Test]
        public void SourceOffset_RotationPreservesDistance()
        {
            var pos = new Vector2(3f, -4f); // 距離 5
            foreach (var angle in new[] { 0f, 45f, 90f, 180f, 270f })
            {
                var local = ListenerPadMath.SourceOffsetInListenerLocal(pos, angle);
                Assert.AreEqual(5f, local.magnitude, 1e-3f, $"angle={angle}");
            }
        }

        [Test]
        public void DistanceToSource_IsListenerMagnitude()
        {
            Assert.AreEqual(5f, ListenerPadMath.DistanceToSource(new Vector2(3f, -4f)), Epsilon);
        }

        [Test]
        public void PixelToMeters_RoundTripsWithMetersToPixel()
        {
            var rect = new Rect(10f, 20f, 300f, 200f);
            var original = new Vector2(4.5f, -7.25f);

            var px = ListenerPadMath.MetersToPixel(original, rect, 10f, 10f);
            var roundTripped = ListenerPadMath.PixelToMeters(px, rect, 10f, 10f);

            Assert.AreEqual(original.x, roundTripped.x, 1e-3f);
            Assert.AreEqual(original.y, roundTripped.y, 1e-3f);
        }

        [Test]
        public void PixelToMeters_TopOfRect_IsPositiveForward()
        {
            var rect = new Rect(0f, 0f, 200f, 200f);

            // GUI 座標は下が+のため、矩形の上端(y=0)は前方(+y)のはず。
            var meters = ListenerPadMath.PixelToMeters(new Vector2(100f, 0f), rect, 10f, 10f);

            Assert.AreEqual(0f, meters.x, Epsilon);
            Assert.AreEqual(10f, meters.y, Epsilon);
        }

        [Test]
        public void ClampToRange_LimitsBothAxesIndependently()
        {
            var clamped = ListenerPadMath.ClampToRange(new Vector2(15f, -3f), 10f, 2f);

            Assert.AreEqual(10f, clamped.x, Epsilon);
            Assert.AreEqual(-2f, clamped.y, Epsilon);
        }
    }
}
