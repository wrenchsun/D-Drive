using DDrive.Editor.CameraFx;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [16_camera_haptics.md] §C-2(5-2c) — プリセット 10 種(Shake/Haptics 各)の適用 + Undo を検証する。
    public class CameraFxPresetsTests
    {
        [Test]
        public void ApplyShakePreset_AllTenKinds_SetsNonZeroAmplitude_AndUndoRestores()
        {
            foreach (var kind in CameraFxPresets.All)
            {
                var data = ScriptableObject.CreateInstance<CameraShakeData>();
                data.PosAmplitude = Vector3.zero;
                data.RotAmplitude = Vector3.zero;

                Undo.IncrementCurrentGroup();
                CameraFxPresets.ApplyShakePreset(data, kind);

                Assert.IsTrue(data.PosAmplitude != Vector3.zero || data.RotAmplitude != Vector3.zero,
                    $"{kind}: PosAmplitude / RotAmplitude が両方ゼロのまま");

                Undo.PerformUndo();
                Assert.AreEqual(Vector3.zero, data.PosAmplitude, $"{kind}: Undo で PosAmplitude が戻らない");
                Assert.AreEqual(Vector3.zero, data.RotAmplitude, $"{kind}: Undo で RotAmplitude が戻らない");

                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void ApplyHapticsPreset_AllTenKinds_SetsNonZeroDuration_AndUndoRestores()
        {
            foreach (var kind in CameraFxPresets.All)
            {
                var data = ScriptableObject.CreateInstance<HapticsData>();
                data.LowFreq = ValueDef.Constant01(0f);
                data.HighFreq = ValueDef.Constant01(0f);

                Undo.IncrementCurrentGroup();
                CameraFxPresets.ApplyHapticsPreset(data, kind);

                Assert.IsTrue(data.LowFreq.Duration > 0f || data.HighFreq.Duration > 0f,
                    $"{kind}: LowFreq / HighFreq の尺が両方 0 のまま");

                Undo.PerformUndo();
                Assert.AreEqual(0f, data.LowFreq.Duration, $"{kind}: Undo で LowFreq が戻らない");
                Assert.AreEqual(0f, data.HighFreq.Duration, $"{kind}: Undo で HighFreq が戻らない");

                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void All_HasExactlyTenKinds()
        {
            Assert.AreEqual(10, CameraFxPresets.All.Length);
        }

        [Test]
        public void Label_UsesUnderscoreForHitPresets()
        {
            Assert.AreEqual("Hit_Small", CameraFxPresets.Label(CameraFxPresetKind.HitSmall));
            Assert.AreEqual("Hit_Large", CameraFxPresets.Label(CameraFxPresetKind.HitLarge));
            Assert.AreEqual("Pulse", CameraFxPresets.Label(CameraFxPresetKind.Pulse));
        }
    }
}
