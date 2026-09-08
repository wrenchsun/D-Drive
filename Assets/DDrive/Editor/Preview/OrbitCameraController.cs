using UnityEngine;

namespace DDrive.Editor.Preview
{
    // VfxEditor(2-4)/ModelEditor(2-6) 共通のオービットカメラ操作(ドラッグ回転+ホイールズーム)。
    public sealed class OrbitCameraController
    {
        public float Yaw = 20f;
        public float Pitch = 15f;
        public float Distance = 4f;

        private bool _dragging;

        public void HandleInput(Rect rect)
        {
            var evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown when rect.Contains(evt.mousePosition):
                    _dragging = true;
                    evt.Use();
                    break;

                case EventType.MouseDrag when _dragging:
                    Yaw += evt.delta.x * 0.5f;
                    Pitch = Mathf.Clamp(Pitch - evt.delta.y * 0.5f, -80f, 80f);
                    evt.Use();
                    break;

                case EventType.MouseUp when _dragging:
                    _dragging = false;
                    evt.Use();
                    break;

                case EventType.ScrollWheel when rect.Contains(evt.mousePosition):
                    Distance = Mathf.Clamp(Distance + evt.delta.y * 0.2f, 0.5f, 30f);
                    evt.Use();
                    break;
            }
        }

        public void Apply(Camera camera, Vector3 pivot)
        {
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            camera.transform.SetPositionAndRotation(pivot + rot * new Vector3(0f, 0f, -Distance), rot);
        }
    }
}
