namespace DDrive.Runtime.Haptics
{
    // [16_camera_haptics.md] Part B — 出力先の抽象化。既定は GamepadHapticOutput(Input System)。
    // プラットフォーム別 SDK(Switch HD振動 / DualSense 等)はこのインタフェースを実装して差し替える。
    public interface IHapticOutput
    {
        // low/high は 0..1(HapticsManager 側で既に GlobalScale・合成済み)。
        void SetMotors(float low, float high);
    }
}
