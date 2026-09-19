using UnityEngine;

namespace DDrive.Editor.Audio
{
    // 波形表示(1-7)の描画データ生成。テクスチャ化と分離した純ロジック部分はテスト可能に保つ。
    public static class WaveformRenderer
    {
        // 各カラム(横1px)に対応するサンプル区間の最小/最大振幅を求める。
        // チャンネルはインターリーブ済み配列を想定し、全チャンネルまとめて集計する。
        public static (float min, float max)[] BuildColumns(float[] samples, int channels, int columnCount)
        {
            var columns = new (float min, float max)[Mathf.Max(1, columnCount)];
            if (samples == null || samples.Length == 0 || channels <= 0)
            {
                return columns;
            }

            var frameCount = samples.Length / channels;
            if (frameCount == 0)
            {
                return columns;
            }

            for (var c = 0; c < columns.Length; c++)
            {
                var startFrame = (long)c * frameCount / columns.Length;
                var endFrame = (long)(c + 1) * frameCount / columns.Length;
                if (endFrame <= startFrame)
                {
                    endFrame = startFrame + 1;
                }

                var min = 0f;
                var max = 0f;
                for (var f = startFrame; f < endFrame && f < frameCount; f++)
                {
                    for (var ch = 0; ch < channels; ch++)
                    {
                        var v = samples[f * channels + ch];
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                }

                columns[c] = (min, max);
            }

            return columns;
        }

        public static Texture2D BuildTexture(AudioClip clip, int width, int height, Color background, Color wave)
        {
            var texture = new Texture2D(Mathf.Max(8, width), Mathf.Max(8, height), TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color[texture.width * texture.height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = background;
            }

            if (clip != null && clip.samples > 0)
            {
                var channels = Mathf.Max(1, clip.channels);
                var samples = new float[clip.samples * channels];
                if (clip.GetData(samples, 0))
                {
                    var columns = BuildColumns(samples, channels, texture.width);
                    var mid = texture.height / 2;

                    for (var x = 0; x < texture.width; x++)
                    {
                        var (min, max) = columns[x];
                        var yMin = Mathf.Clamp(mid + Mathf.RoundToInt(min * (mid - 1)), 0, texture.height - 1);
                        var yMax = Mathf.Clamp(mid + Mathf.RoundToInt(max * (mid - 1)), 0, texture.height - 1);

                        for (var y = yMin; y <= yMax; y++)
                        {
                            pixels[y * texture.width + x] = wave;
                        }
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false);
            return texture;
        }
    }
}
