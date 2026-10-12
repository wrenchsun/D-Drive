using System.Collections.Generic;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.CameraFx
{
    // 波形グラフ(CameraFxGraphView)に渡す 1 本の線。Slot: 0 = 中立(Envelope)/ 1 = 青 / 2 = 橙。Samples は -1..1(Shake)/ 0..1(Haptics)。
    public readonly struct CameraFxSeries
    {
        public readonly string Label;
        public readonly int Slot;
        public readonly float[] Samples;

        public CameraFxSeries(string label, int slot, float[] samples)
        {
            Label = label;
            Slot = slot;
            Samples = samples;
        }
    }

    // グラフが読む / 書く Data の窓口。Shake / Haptics で線の作り方と編集できるもの(長さ・強さのつまみ)が違うので、種別ごとに実装する。
    // 書き込みは Undo.RecordObject + SetDirty(1 回のドラッグを 1 回の Undo にまとめるのはビュー側)。
    public abstract class CameraFxGraphSource
    {
        public const int SampleCount = 192;

        // true: -1..1 の波(中央線あり)/ false: 0..1 の強さ(下端が 0)。
        public abstract bool Bipolar { get; }

        public abstract string EmptyMessage { get; }

        // グラフが表す 1 周期の長さ(秒)。0 以下 = 描けない。
        public abstract float Duration { get; }

        public abstract IReadOnlyList<CameraFxSeries> BuildSeries();

        // 長さのつまみ(右端のドラッグ)で書き換えられるか。尺が Speed / Rate で決まっているデータは書き換えない。
        public abstract bool CanEditDuration { get; }

        public abstract void SetDuration(float seconds);

        // 強さのつまみ(時間 0 の位置の点。ドラッグで強さを決める)。Shake は持たない。
        public virtual int HandleCount => 0;

        public virtual bool CanEditHandle(int index) => false;

        public virtual float GetHandle(int index) => 0f;

        public virtual void SetHandle(int index, float value) { }

        // 強さのつまみが対応する線(Series の添字)。
        public virtual int HandleSeries(int index) => index;

        protected static bool IsDurationTime(ValueDef def) => def.Time.Mode == TimeMode.Duration;
    }

    public sealed class ShakeGraphSource : CameraFxGraphSource
    {
        private readonly CameraShakeData _data;

        public ShakeGraphSource(CameraShakeData data)
        {
            _data = data;
        }

        public override bool Bipolar => true;

        public override string EmptyMessage => _data == null ? "対象を選択してください" : "Envelope の尺が 0 です(波形なし)。「揺れ方」の Envelope の長さを入れてください";

        public override float Duration => _data != null ? _data.Envelope.Duration : 0f;

        public override bool CanEditDuration => _data != null && IsDurationTime(_data.Envelope);

        public override void SetDuration(float seconds)
        {
            if (!CanEditDuration)
            {
                return;
            }

            Undo.RecordObject(_data, "揺れの長さを変更");
            _data.Envelope.Time.Value = seconds;
            EditorUtility.SetDirty(_data);
        }

        public override IReadOnlyList<CameraFxSeries> BuildSeries()
        {
            var duration = Duration;
            if (_data == null || duration <= 0f)
            {
                return null;
            }

            var envelope = new float[SampleCount];
            var wave = new float[SampleCount];
            var peak = 0f;
            for (var i = 0; i < SampleCount; i++)
            {
                var t = i / (float)(SampleCount - 1) * duration;
                var envelopeValue = Mathf.Clamp01(_data.Envelope.EvaluateAt(t));
                envelope[i] = envelopeValue;

                var freq = Mathf.Max(0f, _data.Frequency.EvaluateAt(t));
                wave[i] = envelopeValue * ShapeFactor(_data.Pattern, t, freq);
                peak = Mathf.Max(peak, Mathf.Abs(wave[i]));
            }

            // 位置と回転は同じ波形(大きさだけ違う)なので、形は 1 本にして、凡例に峰値(最大の振れ幅)を出す。
            var posPeak = _data.PosAmplitude.magnitude * peak;
            var rotPeak = _data.RotAmplitude.magnitude * peak;
            return new[]
            {
                new CameraFxSeries("減衰(Envelope)", 0, envelope),
                new CameraFxSeries($"揺れの形(位置の峰値 {posPeak:0.00}m・回転の峰値 {rotPeak:0.0}°)", 1, Normalize(wave)),
            };
        }

        // Impulse / CustomCurve は方向性のある一撃(振動しない)、PerlinNoise / DecaySine は波打つ様子を
        // 参考表示として重ねる(実際の乱数位相はインスタンスごとにずれるため厳密な再現ではない)。
        internal static float ShapeFactor(ShakePattern pattern, float t, float freq) => pattern switch
        {
            ShakePattern.PerlinNoise => Mathf.PerlinNoise(0.123f, t * freq) * 2f - 1f,
            ShakePattern.DecaySine => Mathf.Sin(t * freq * Mathf.PI * 2f),
            _ => 1f,
        };

        internal static float[] Normalize(float[] raw)
        {
            var max = 0f;
            foreach (var v in raw)
            {
                max = Mathf.Max(max, Mathf.Abs(v));
            }

            if (max <= 1e-6f)
            {
                return raw;
            }

            var result = new float[raw.Length];
            for (var i = 0; i < raw.Length; i++)
            {
                result[i] = raw[i] / max;
            }

            return result;
        }
    }

    public sealed class HapticsGraphSource : CameraFxGraphSource
    {
        private readonly HapticsData _data;

        public HapticsGraphSource(HapticsData data)
        {
            _data = data;
        }

        public override bool Bipolar => false;

        public override string EmptyMessage => _data == null ? "対象を選択してください" : "LowFreq / HighFreq の尺が 0 です(波形なし)。「モーター」の長さを入れてください";

        public override float Duration => _data != null ? Mathf.Max(_data.LowFreq.Duration, _data.HighFreq.Duration) : 0f;

        public override bool CanEditDuration => _data != null && (IsDurationTime(_data.LowFreq) || IsDurationTime(_data.HighFreq));

        // 長い方が指定の秒数になるよう、両方のモーターの尺を同じ比率で変える(尺が 0 のモーターは指定の秒数にする)。
        public override void SetDuration(float seconds)
        {
            if (!CanEditDuration)
            {
                return;
            }

            var current = Duration;
            var factor = current > 0f ? seconds / current : 1f;
            Undo.RecordObject(_data, "振動の長さを変更");
            if (IsDurationTime(_data.LowFreq))
            {
                _data.LowFreq.Time.Value = _data.LowFreq.Time.Value > 0f ? _data.LowFreq.Time.Value * factor : seconds;
            }

            if (IsDurationTime(_data.HighFreq))
            {
                _data.HighFreq.Time.Value = _data.HighFreq.Time.Value > 0f ? _data.HighFreq.Time.Value * factor : seconds;
            }

            EditorUtility.SetDirty(_data);
        }

        public override IReadOnlyList<CameraFxSeries> BuildSeries()
        {
            var duration = Duration;
            if (_data == null || duration <= 0f)
            {
                return null;
            }

            var low = new float[SampleCount];
            var high = new float[SampleCount];
            for (var i = 0; i < SampleCount; i++)
            {
                var t = i / (float)(SampleCount - 1) * duration;
                low[i] = Mathf.Clamp01(_data.LowFreq.EvaluateAt(t));
                high[i] = Mathf.Clamp01(_data.HighFreq.EvaluateAt(t));
            }

            return new[]
            {
                new CameraFxSeries("低周波(ドスン・Low)", 1, low),
                new CameraFxSeries("高周波(ビリビリ・High)", 2, high),
            };
        }

        public override int HandleCount => 2;

        public override bool CanEditHandle(int index)
        {
            var mode = Motor(index).Mode;
            return mode == ValueMode.Constant || mode == ValueMode.Parametric;
        }

        public override float GetHandle(int index)
        {
            var def = Motor(index);
            return Mathf.Clamp01(def.Mode == ValueMode.Constant ? def.Constant : def.EvaluateAt(0f));
        }

        // 強さのつまみ: Constant なら固定値、Parametric なら始まりの値(From)を書く。
        public override void SetHandle(int index, float value)
        {
            if (!CanEditHandle(index))
            {
                return;
            }

            Undo.RecordObject(_data, "振動の強さを変更");
            value = Mathf.Clamp01(value);
            if (index == 0)
            {
                Write(ref _data.LowFreq, value);
            }
            else
            {
                Write(ref _data.HighFreq, value);
            }

            EditorUtility.SetDirty(_data);
        }

        private static void Write(ref ValueDef def, float value)
        {
            if (def.Mode == ValueMode.Constant)
            {
                def.Constant = value;
            }
            else
            {
                def.From = value;
            }
        }

        private ValueDef Motor(int index) => index == 0 ? _data.LowFreq : _data.HighFreq;
    }
}
