using System;
using UnityEngine;

namespace DDrive.Editor.Audio
{
    // SeData の非破壊トリミング機能(Source→Clips のベイク)を支える純粋なユーティリティ。
    // AudioClip.GetData/Create/SetData はランタイムでも呼べるが、この機能自体が
    // 認識レベルの「編集ツール」なので Editor アセンブリに置く。
    public static class AudioClipTrimUtility
    {
        // 振幅が threshold 未満の先頭/末尾を無音とみなし、トリム範囲を提案する。
        // 全区間が無音の場合は (0, 0) を返す(=クリップ全体が無音)。
        public static bool TryDetectSilenceTrim(AudioClip clip, float thresholdLinear, out float trimStartSec, out float trimEndSec)
        {
            trimStartSec = 0f;
            trimEndSec = clip != null ? clip.length : 0f;

            if (clip == null || clip.samples <= 0)
            {
                return false;
            }

            var channels = Mathf.Max(1, clip.channels);
            var samples = clip.samples;
            var data = new float[samples * channels];

            if (!clip.GetData(data, 0))
            {
                return false;
            }

            var firstLoud = -1;
            var lastLoud = -1;

            for (var i = 0; i < samples; i++)
            {
                var loud = false;
                var baseIndex = i * channels;
                for (var c = 0; c < channels; c++)
                {
                    if (Mathf.Abs(data[baseIndex + c]) >= thresholdLinear)
                    {
                        loud = true;
                        break;
                    }
                }

                if (!loud)
                {
                    continue;
                }

                if (firstLoud < 0)
                {
                    firstLoud = i;
                }

                lastLoud = i;
            }

            if (firstLoud < 0)
            {
                trimStartSec = 0f;
                trimEndSec = 0f;
                return true;
            }

            trimStartSec = (float)firstLoud / clip.frequency;
            trimEndSec = (float)(lastLoud + 1) / clip.frequency;
            return true;
        }

        // trimEndSec <= trimStartSec は「クリップ終端まで」を意味する。
        public static bool TryExtractTrimmedSamples(
            AudioClip source, float trimStartSec, float trimEndSec,
            out float[] interleaved, out int channels, out int frequency)
        {
            interleaved = null;
            channels = 0;
            frequency = 0;

            if (source == null || source.samples <= 0)
            {
                return false;
            }

            channels = Mathf.Max(1, source.channels);
            frequency = source.frequency;
            var totalSamples = source.samples;

            var startSample = Mathf.Clamp(Mathf.RoundToInt(trimStartSec * frequency), 0, totalSamples);

            // 開始位置がクリップ末尾以降(Source を短いクリップに差し替えた等)は切り出し不能。
            // Max(1,0) で 1 サンプル確保すると末尾越えの Array.Copy で例外になるため、明示的に失敗させる。
            if (startSample >= totalSamples)
            {
                return false;
            }

            var endSample = trimEndSec > trimStartSec
                ? Mathf.Clamp(Mathf.RoundToInt(trimEndSec * frequency), startSample + 1, totalSamples)
                : totalSamples;

            var length = endSample - startSample;

            var allData = new float[totalSamples * channels];
            if (!source.GetData(allData, 0))
            {
                return false;
            }

            interleaved = new float[length * channels];
            Array.Copy(allData, startSample * channels, interleaved, 0, length * channels);
            return true;
        }

        // メモリ上での利用(試聴等)向け。※このクリップをアセット保存しても PCM は永続化されない。
        // 永続化には WavWriter で .wav として書き出すこと(SeTrimApplier 参照)。
        public static AudioClip CreateTrimmedClip(AudioClip source, float trimStartSec, float trimEndSec)
        {
            if (!TryExtractTrimmedSamples(source, trimStartSec, trimEndSec, out var trimmed, out var channels, out var frequency))
            {
                return null;
            }

            var clip = UnityEngine.AudioClip.Create($"{source.name}_Trimmed", trimmed.Length / channels, channels, frequency, false);
            clip.SetData(trimmed, 0);
            return clip;
        }
    }
}
