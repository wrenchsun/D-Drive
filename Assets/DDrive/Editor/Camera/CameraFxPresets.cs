using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.CameraFx
{
    // [16_camera_haptics.md] §C-2(5-2c) — 「モーターカーブ編集」のプリセット(Pulse/Rumble/Heartbeat/
    // Explosion/Hit_Small/Hit_Large/Landing/Earthquake/Alarm/Engine の 10 種、Shake/Haptics それぞれ)。
    // 具体的な数値は暫定値(要判断: docs/28_manual_verification_phase5.md 5-2c 節参照。デザイナー確認前提の
    // たたき台で、実プレイでの調整を妨げるものではない)。適用は対象アセットへの直接書き込み + Undo。
    public enum CameraFxPresetKind
    {
        Pulse,
        Rumble,
        Heartbeat,
        Explosion,
        HitSmall,
        HitLarge,
        Landing,
        Earthquake,
        Alarm,
        Engine,
    }

    public static class CameraFxPresets
    {
        public static readonly CameraFxPresetKind[] All =
        {
            CameraFxPresetKind.Pulse,
            CameraFxPresetKind.Rumble,
            CameraFxPresetKind.Heartbeat,
            CameraFxPresetKind.Explosion,
            CameraFxPresetKind.HitSmall,
            CameraFxPresetKind.HitLarge,
            CameraFxPresetKind.Landing,
            CameraFxPresetKind.Earthquake,
            CameraFxPresetKind.Alarm,
            CameraFxPresetKind.Engine,
        };

        public static string Label(CameraFxPresetKind kind) => kind switch
        {
            CameraFxPresetKind.HitSmall => "Hit_Small",
            CameraFxPresetKind.HitLarge => "Hit_Large",
            _ => kind.ToString(),
        };

        private static ValueDef Decay(float from, float to, float durationSec, Ease ease = Ease.OutQuad, LoopMode loop = LoopMode.Once, int loopCount = 0) => new()
        {
            Mode = ValueMode.Parametric,
            Parametric = EaseDef.Named(ease),
            From = from,
            To = to,
            Time = new TimeDef { Mode = TimeMode.Duration, Value = durationSec, SpeedScale = 1f },
            Loop = loop,
            LoopCount = loopCount,
        };

        // ── Shake ──

        public static void ApplyShakePreset(CameraShakeData data, CameraFxPresetKind kind)
        {
            if (data == null)
            {
                return;
            }

            Undo.RecordObject(data, $"Apply Shake Preset ({Label(kind)})");

            switch (kind)
            {
                case CameraFxPresetKind.Pulse:
                    data.Pattern = ShakePattern.Impulse;
                    data.PosAmplitude = new Vector3(0.05f, 0.05f, 0f);
                    data.RotAmplitude = Vector3.zero;
                    data.Frequency = ValueDef.Constant01(0f);
                    data.Envelope = Decay(1f, 0f, 0.15f);
                    data.TraumaWeight = 1f;
                    data.MaxStack = 3;
                    break;

                case CameraFxPresetKind.Rumble:
                    data.Pattern = ShakePattern.PerlinNoise;
                    data.PosAmplitude = new Vector3(0.05f, 0.05f, 0.02f);
                    data.RotAmplitude = new Vector3(0f, 0f, 0.5f);
                    data.Frequency = ValueDef.Constant01(8f);
                    data.Envelope = Decay(1f, 0.7f, 1.0f, Ease.Linear);
                    data.TraumaWeight = 0.8f;
                    data.MaxStack = 2;
                    break;

                case CameraFxPresetKind.Heartbeat:
                    data.Pattern = ShakePattern.DecaySine;
                    data.PosAmplitude = new Vector3(0.03f, 0f, 0f);
                    data.RotAmplitude = Vector3.zero;
                    data.Frequency = ValueDef.Constant01(2f);
                    data.Envelope = Decay(1f, 0f, 0.6f, Ease.InOutQuad, LoopMode.Loop, 2);
                    data.TraumaWeight = 0.6f;
                    data.MaxStack = 2;
                    break;

                case CameraFxPresetKind.Explosion:
                    data.Pattern = ShakePattern.PerlinNoise;
                    data.PosAmplitude = new Vector3(0.35f, 0.35f, 0.1f);
                    data.RotAmplitude = new Vector3(2f, 2f, 3f);
                    data.Frequency = ValueDef.Constant01(25f);
                    data.Envelope = Decay(1f, 0f, 0.6f, Ease.OutExpo);
                    data.TraumaWeight = 1.5f;
                    data.MaxStack = 1;
                    break;

                case CameraFxPresetKind.HitSmall:
                    data.Pattern = ShakePattern.Impulse;
                    data.PosAmplitude = new Vector3(0.06f, 0.04f, 0f);
                    data.RotAmplitude = new Vector3(0f, 0f, 0.5f);
                    data.Frequency = ValueDef.Constant01(0f);
                    data.Envelope = Decay(1f, 0f, 0.12f);
                    data.TraumaWeight = 1f;
                    data.MaxStack = 4;
                    break;

                case CameraFxPresetKind.HitLarge:
                    data.Pattern = ShakePattern.PerlinNoise;
                    data.PosAmplitude = new Vector3(0.18f, 0.14f, 0.05f);
                    data.RotAmplitude = new Vector3(0f, 0f, 1.5f);
                    data.Frequency = ValueDef.Constant01(18f);
                    data.Envelope = Decay(1f, 0f, 0.25f);
                    data.TraumaWeight = 1.3f;
                    data.MaxStack = 2;
                    break;

                case CameraFxPresetKind.Landing:
                    data.Pattern = ShakePattern.Impulse;
                    data.PosAmplitude = new Vector3(0f, 0.12f, 0f);
                    data.RotAmplitude = new Vector3(1f, 0f, 0f);
                    data.Frequency = ValueDef.Constant01(0f);
                    data.Envelope = Decay(1f, 0f, 0.2f);
                    data.TraumaWeight = 1f;
                    data.MaxStack = 2;
                    break;

                case CameraFxPresetKind.Earthquake:
                    data.Pattern = ShakePattern.PerlinNoise;
                    data.PosAmplitude = new Vector3(0.12f, 0.08f, 0.08f);
                    data.RotAmplitude = new Vector3(0.5f, 0.5f, 0.5f);
                    data.Frequency = ValueDef.Constant01(4f);
                    data.Envelope = Decay(1f, 1f, 3.0f, Ease.Linear, LoopMode.Loop, 0);
                    data.TraumaWeight = 1f;
                    data.MaxStack = 1;
                    break;

                case CameraFxPresetKind.Alarm:
                    data.Pattern = ShakePattern.DecaySine;
                    data.PosAmplitude = new Vector3(0.02f, 0.02f, 0f);
                    data.RotAmplitude = new Vector3(0f, 0f, 0.3f);
                    data.Frequency = ValueDef.Constant01(3f);
                    data.Envelope = Decay(1f, 1f, 2.0f, Ease.Linear, LoopMode.Loop, 0);
                    data.TraumaWeight = 0.5f;
                    data.MaxStack = 1;
                    break;

                case CameraFxPresetKind.Engine:
                    data.Pattern = ShakePattern.PerlinNoise;
                    data.PosAmplitude = new Vector3(0.015f, 0.015f, 0.01f);
                    data.RotAmplitude = new Vector3(0.1f, 0.1f, 0.1f);
                    data.Frequency = ValueDef.Constant01(30f);
                    data.Envelope = Decay(1f, 1f, 5.0f, Ease.Linear, LoopMode.Loop, 0);
                    data.TraumaWeight = 0.4f;
                    data.MaxStack = 1;
                    break;
            }

            EditorUtility.SetDirty(data);
        }

        // ── Haptics ──

        public static void ApplyHapticsPreset(HapticsData data, CameraFxPresetKind kind)
        {
            if (data == null)
            {
                return;
            }

            Undo.RecordObject(data, $"Apply Haptics Preset ({Label(kind)})");

            switch (kind)
            {
                case CameraFxPresetKind.Pulse:
                    data.LowFreq = Decay(0.7f, 0f, 0.08f);
                    data.HighFreq = Decay(0.3f, 0f, 0.08f);
                    data.Priority = HapticPriority.Normal;
                    break;

                case CameraFxPresetKind.Rumble:
                    data.LowFreq = Decay(0.5f, 0.4f, 1.0f, Ease.Linear, LoopMode.Loop, 0);
                    data.HighFreq = ValueDef.Constant01(0f);
                    data.Priority = HapticPriority.Normal;
                    break;

                case CameraFxPresetKind.Heartbeat:
                    data.LowFreq = Decay(0.8f, 0f, 0.3f, Ease.InOutQuad, LoopMode.Loop, 2);
                    data.HighFreq = ValueDef.Constant01(0f);
                    data.Priority = HapticPriority.Normal;
                    break;

                case CameraFxPresetKind.Explosion:
                    data.LowFreq = Decay(1f, 0f, 0.5f, Ease.OutExpo);
                    data.HighFreq = Decay(1f, 0f, 0.2f);
                    data.Priority = HapticPriority.High;
                    break;

                case CameraFxPresetKind.HitSmall:
                    data.LowFreq = Decay(0.4f, 0f, 0.08f);
                    data.HighFreq = Decay(0.6f, 0f, 0.05f);
                    data.Priority = HapticPriority.Normal;
                    break;

                case CameraFxPresetKind.HitLarge:
                    data.LowFreq = Decay(0.9f, 0f, 0.2f);
                    data.HighFreq = Decay(0.7f, 0f, 0.15f);
                    data.Priority = HapticPriority.High;
                    break;

                case CameraFxPresetKind.Landing:
                    data.LowFreq = Decay(0.6f, 0f, 0.15f);
                    data.HighFreq = Decay(0.2f, 0f, 0.1f);
                    data.Priority = HapticPriority.Normal;
                    break;

                case CameraFxPresetKind.Earthquake:
                    data.LowFreq = Decay(0.5f, 0.5f, 3.0f, Ease.Linear, LoopMode.Loop, 0);
                    data.HighFreq = Decay(0.1f, 0.1f, 3.0f, Ease.Linear, LoopMode.Loop, 0);
                    data.Priority = HapticPriority.High;
                    break;

                case CameraFxPresetKind.Alarm:
                    data.LowFreq = Decay(0.3f, 0f, 0.5f, Ease.Linear, LoopMode.Loop, 0);
                    data.HighFreq = Decay(0.3f, 0f, 0.5f, Ease.Linear, LoopMode.Loop, 0);
                    data.Priority = HapticPriority.Low;
                    break;

                case CameraFxPresetKind.Engine:
                    data.LowFreq = Decay(0.25f, 0.25f, 5.0f, Ease.Linear, LoopMode.Loop, 0);
                    data.HighFreq = Decay(0.1f, 0.1f, 5.0f, Ease.Linear, LoopMode.Loop, 0);
                    data.Priority = HapticPriority.Low;
                    break;
            }

            EditorUtility.SetDirty(data);
        }
    }
}
