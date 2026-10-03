using UnityEngine;

namespace DDrive.Runtime.Viewing
{
    // [51_tdrive_integration.md] §4.4(FC-3) — 「今どこから見ているか」。Unity のワールド座標(m / Y-up / 左手 / 前 +Z)の
    // 位置・回転と縦画角(度)をそのまま持つ。単位変換・座標変換はしない。
    // VerticalFovDegrees は Camera.fieldOfView をそのまま入れる(正射影カメラでも変換せず、その値のまま)。
    // 外部の IViewProvider が値を作れるよう public コンストラクタを持つ。
    public readonly struct ViewPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float VerticalFovDegrees;
        public readonly ViewSource Source;
        public readonly Camera Camera; // 実カメラ(無ければ null)

        public ViewPose(Vector3 position, Quaternion rotation, float verticalFovDegrees, ViewSource source, Camera camera)
        {
            Position = position;
            Rotation = rotation;
            VerticalFovDegrees = verticalFovDegrees;
            Source = source;
            Camera = camera;
        }
    }
}
