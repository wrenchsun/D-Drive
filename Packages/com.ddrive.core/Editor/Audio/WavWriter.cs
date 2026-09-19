using System;
using System.IO;

namespace DDrive.Editor.Audio
{
    // 16bit PCM WAV エンコーダ。トリム結果を実ファイルとして書き出すために使う
    // (AudioClip.Create したクリップはアセット保存しても PCM データが永続化されないため、
    //  トリム済みデータは .wav としてプロジェクトに置き、Unity のインポーターに読ませる)。
    public static class WavWriter
    {
        public static byte[] Encode(float[] interleavedSamples, int channels, int sampleRate)
        {
            if (interleavedSamples == null || interleavedSamples.Length == 0 || channels <= 0 || sampleRate <= 0)
            {
                throw new ArgumentException("WavWriter.Encode: invalid input");
            }

            var sampleCount = interleavedSamples.Length;
            var byteRate = sampleRate * channels * 2;
            var dataSize = sampleCount * 2;

            using var stream = new MemoryStream(44 + dataSize);
            using var writer = new BinaryWriter(stream);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * 2)); // block align
            writer.Write((short)16); // bits per sample

            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            foreach (var sample in interleavedSamples)
            {
                var clamped = Math.Clamp(sample, -1f, 1f);
                writer.Write((short)Math.Round(clamped * short.MaxValue));
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}
