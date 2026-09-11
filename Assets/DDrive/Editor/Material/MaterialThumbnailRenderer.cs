using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements; // CapturePointer / ReleasePointer 拡張メソッド

namespace DDrive.Editor.Materials
{
    // [09_editor_tools.md] §2 の例外(2026-09-11 決定): Material だけはウィンドウ内サムネイルを併用する。
    // 描くのは実 MaterialManager が生成した共有 Material そのもの(Editor 専用の再生経路は持たない = ADR-4 の趣旨は維持)。
    // 既定ライト 1 灯 + 環境光のみで描くため、実シーン照明・リフレクション・ポスプロ・ModelData 適用の確認は
    // 従来どおり MaterialPreviewBuilder のシーン配置で行う。
    public sealed class MaterialThumbnailRenderer : IDisposable
    {
        private PreviewRenderUtility _utility;
        private Mesh _sphere;
        private Mesh _cube;
        private Mesh _quad;

        public Color BackgroundColor = new(0.18f, 0.18f, 0.18f, 1f);
        public float CameraDistance = 3.2f;

        // shape が Model のときはサムネイルを出さない(null)。呼び出し側がシーン配置へ誘導する。
        public Texture Render(UnityEngine.Material material, MaterialPreviewShape shape, float turntableDeg, float lightDeg, int width, int height)
            => Render(material, shape, turntableDeg, 0f, lightDeg, width, height);

        // yawDeg = Y 軸回転(ターンテーブル + 横ドラッグ)、pitchDeg = X 軸の傾き(縦ドラッグ、呼び出し側で ±80° に丸める)。
        public Texture Render(UnityEngine.Material material, MaterialPreviewShape shape, float yawDeg, float pitchDeg, float lightDeg, int width, int height)
        {
            var turntableDeg = yawDeg;
            var mesh = MeshFor(shape);
            if (material == null || mesh == null || width <= 0 || height <= 0)
            {
                return null;
            }

            EnsureUtility();
            _utility.BeginPreview(new Rect(0f, 0f, width, height), GUIStyle.none);

            var cam = _utility.camera;
            cam.backgroundColor = BackgroundColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(0f, 0f, -CameraDistance);
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.fieldOfView = 30f;

            _utility.lights[0].intensity = 1.2f;
            _utility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f + lightDeg, 0f);
            _utility.lights[1].intensity = 0.4f;
            _utility.lights[1].transform.rotation = Quaternion.Euler(-20f, 220f + lightDeg, 0f);
            _utility.ambientColor = new Color(0.25f, 0.25f, 0.25f, 1f);

            // Quad は Y 軸回転だけだと裏を向くので、板は Z 回転で見せる(傾きは無視)。
            var rotation = shape == MaterialPreviewShape.Plane ? Quaternion.Euler(0f, 0f, turntableDeg) : Quaternion.Euler(pitchDeg, turntableDeg, 0f);
            var scale = shape == MaterialPreviewShape.Plane ? Vector3.one * 1.6f : Vector3.one;
            _utility.DrawMesh(mesh, Matrix4x4.TRS(Vector3.zero, rotation, scale), material, 0);
            _utility.Render(true);
            return _utility.EndPreview();
        }

        public void Dispose()
        {
            _utility?.Cleanup();
            _utility = null;
        }

        private void EnsureUtility()
        {
            if (_utility != null)
            {
                return;
            }

            _utility = new PreviewRenderUtility();
            _utility.cameraFieldOfView = 30f;
        }

        // サムネイル上のドラッグで回す(2026-09-11)。横 = yaw、縦 = pitch。呼び出し側が角度を持ち、dirty を立てて描き直す。
        public const float DragDegreesPerPixel = 0.5f;
        public const float MaxPitchDeg = 80f;

        // 回転方向の反転(EditorPrefs。Material Editor とポップアップで共有)。
        private const string InvertXKey = "DDrive.MaterialThumbnail.InvertDragX";
        private const string InvertYKey = "DDrive.MaterialThumbnail.InvertDragY";

        public static bool InvertDragX
        {
            get => EditorPrefs.GetBool(InvertXKey, false);
            set => EditorPrefs.SetBool(InvertXKey, value);
        }

        public static bool InvertDragY
        {
            get => EditorPrefs.GetBool(InvertYKey, false);
            set => EditorPrefs.SetBool(InvertYKey, value);
        }

        // 「X 反転」「Y 反転」トグルの行。両ウィンドウで同じ見た目にする。
        public static VisualElement CreateInvertToggles()
        {
            // label ではなく text を使う(label はフィールド列の固定幅を取り、2 つ並べると行からはみ出す)。
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };
            var x = new Toggle { text = "X 反転", value = InvertDragX, tooltip = "横ドラッグの回転方向を反転する", style = { marginRight = 6, marginLeft = 0 } };
            x.RegisterValueChangedCallback(evt => InvertDragX = evt.newValue);
            row.Add(x);
            var y = new Toggle { text = "Y 反転", value = InvertDragY, tooltip = "縦ドラッグの傾き方向を反転する", style = { marginLeft = 0 } };
            y.RegisterValueChangedCallback(evt => InvertDragY = evt.newValue);
            row.Add(y);
            return row;
        }

        public static void AttachDrag(UnityEngine.UIElements.VisualElement element, Action<float, float> onDragDegrees)
        {
            var dragging = false;
            var last = Vector2.zero;
            element.RegisterCallback<UnityEngine.UIElements.PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                dragging = true;
                last = evt.position;
                element.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            element.RegisterCallback<UnityEngine.UIElements.PointerMoveEvent>(evt =>
            {
                if (!dragging || !element.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                var delta = (Vector2)evt.position - last;
                last = evt.position;
                var yaw = delta.x * DragDegreesPerPixel * (InvertDragX ? -1f : 1f);
                var pitch = delta.y * DragDegreesPerPixel * (InvertDragY ? -1f : 1f);
                onDragDegrees(yaw, pitch);
                evt.StopPropagation();
            });
            element.RegisterCallback<UnityEngine.UIElements.PointerUpEvent>(evt =>
            {
                if (!dragging)
                {
                    return;
                }

                dragging = false;
                element.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });
            element.RegisterCallback<UnityEngine.UIElements.PointerCaptureOutEvent>(_ => dragging = false);
        }

        private Mesh MeshFor(MaterialPreviewShape shape)
        {
            switch (shape)
            {
                case MaterialPreviewShape.Sphere:
                    return _sphere ??= Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
                case MaterialPreviewShape.Cube:
                    return _cube ??= Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                case MaterialPreviewShape.Plane:
                    return _quad ??= Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                default:
                    return null;
            }
        }
    }
}
