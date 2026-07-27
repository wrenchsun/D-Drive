using DDrive.Editor.Audio;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    public class WaveformRendererTests
    {
        [Test]
        public void BuildColumns_SingleChannel_CapturesMinMaxPerColumn()
        {
            // 4 カラム × 各2サンプル。カラムごとに異なる振幅を持たせる。
            var samples = new[] { 0.1f, -0.1f, 0.5f, -0.5f, 1f, -1f, 0.2f, 0f };

            var columns = WaveformRenderer.BuildColumns(samples, channels: 1, columnCount: 4);

            Assert.AreEqual(4, columns.Length);
            Assert.AreEqual((-0.1f, 0.1f), columns[0]);
            Assert.AreEqual((-0.5f, 0.5f), columns[1]);
            Assert.AreEqual((-1f, 1f), columns[2]);
            Assert.AreEqual((0f, 0.2f), columns[3]);
        }

        [Test]
        public void BuildColumns_TwoChannels_AggregatesAcrossChannels()
        {
            // 1 フレーム = [L, R]。L=0.2, R=-0.8 → min=-0.8, max=0.2。
            var samples = new[] { 0.2f, -0.8f };

            var columns = WaveformRenderer.BuildColumns(samples, channels: 2, columnCount: 1);

            Assert.AreEqual((-0.8f, 0.2f), columns[0]);
        }

        [Test]
        public void BuildColumns_MoreColumnsThanFrames_DoesNotThrow()
        {
            var samples = new[] { 0.5f, -0.5f };

            var columns = WaveformRenderer.BuildColumns(samples, channels: 1, columnCount: 10);

            Assert.AreEqual(10, columns.Length);
        }

        [Test]
        public void BuildColumns_EmptyOrNullSamples_ReturnsSilentColumns()
        {
            var fromNull = WaveformRenderer.BuildColumns(null, 1, 4);
            var fromEmpty = WaveformRenderer.BuildColumns(new float[0], 1, 4);

            Assert.AreEqual((0f, 0f), fromNull[0]);
            Assert.AreEqual((0f, 0f), fromEmpty[0]);
        }
    }
}
