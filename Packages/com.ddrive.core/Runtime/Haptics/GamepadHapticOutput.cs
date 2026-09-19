using UnityEngine;
using UnityEngine.InputSystem;

namespace DDrive.Runtime.Haptics
{
    // [16_camera_haptics.md] Part B — 既定の出力先。Input System の Gamepad.SetMotorSpeeds を使う。
    // パッド未接続時は no-op(例外にしない。CLAUDE.md §0-4)。
    public sealed class GamepadHapticOutput : IHapticOutput
    {
        public void SetMotors(float low, float high)
        {
            var pad = Gamepad.current;
            if (pad == null)
            {
                return;
            }

            pad.SetMotorSpeeds(Mathf.Clamp01(low), Mathf.Clamp01(high));
        }
    }
}
