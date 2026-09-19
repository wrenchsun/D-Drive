using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19 追加) — Camera クリップの評価結果
    // (`CutsceneCameraStateHolder`)を、確認用シーンの `Camera.main` へ直接書く。ブレンド無し
    // (Timeline カメラの姿勢そのもの。§4.4「スクラブ中はブレンド無し」を Edit Mode 全体の既定にした)、
    // 原点はプレビュー用 Director の Transform(`CutsceneManager.ApplyOrigin` がプレイ時に CutsceneRoot の
    // Transform を Origin どおりに置くのと同じ考え方。Edit Mode 側は「▶ Timeline ウィンドウで開く」時に
    // 1 回だけ置く、CutsceneDataEditor 参照)。
    //
    // Play Mode の `DDriveCutsceneCameraApplier`(実行順 1000 の LateUpdate)とは完全に別経路(二重に書かない
    // ため `Application.isPlaying` で自分自身を止める)。ゲームカメラ制御との実行順契約([26] §4.6.5)は
    // Play Mode 専用の話であり、Edit Mode の確認用シーンにはゲームのカメラ制御自体が存在しないため対象外。
    internal static class CutsceneEditModeCameraWriter
    {
        private static bool _hasOriginal;
        private static Camera _capturedCamera;
        private static Vector3 _originalPos;
        private static Quaternion _originalRot;
        private static float _originalFov;
        private static float _originalFocusDistance;

        // Director のいる GameObject の Transform を原点として Camera.main に直接書く。
        // holder.HasData が false(カメラクリップの区間外)なら、書き込む前の姿勢へ戻す。
        public static void Apply(GameObject directorRoot)
        {
            if (Application.isPlaying || directorRoot == null)
            {
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var holder = directorRoot.GetComponent<CutsceneCameraStateHolder>();
            if (holder == null || !holder.HasData)
            {
                RestoreIfNeeded(cam);
                return;
            }

            CaptureIfNeeded(cam);

            var root = directorRoot.transform;
            cam.transform.SetPositionAndRotation(root.TransformPoint(holder.LocalPos), root.rotation * holder.LocalRot);
            cam.fieldOfView = holder.Fov;

            if (holder.Focus != CameraFocusMode.Off)
            {
                cam.focusDistance = holder.FocusDistance;
            }
        }

        private static void CaptureIfNeeded(Camera cam)
        {
            if (_hasOriginal && _capturedCamera == cam)
            {
                return;
            }

            _capturedCamera = cam;
            _originalPos = cam.transform.position;
            _originalRot = cam.transform.rotation;
            _originalFov = cam.fieldOfView;
            _originalFocusDistance = cam.focusDistance;
            _hasOriginal = true;
        }

        private static void RestoreIfNeeded(Camera cam)
        {
            if (!_hasOriginal || _capturedCamera != cam)
            {
                return;
            }

            cam.transform.SetPositionAndRotation(_originalPos, _originalRot);
            cam.fieldOfView = _originalFov;
            cam.focusDistance = _originalFocusDistance;
            _hasOriginal = false;
            _capturedCamera = null;
        }

        // シーン切替・セッション終了・ドメインリロード前に呼ぶ(前の Camera.main への参照を捨てるだけ。
        // 姿勢の復元自体は次に Apply() が「HasData=false」を見たときに自然に行われるため、ここでは
        // 強制復元しない — 既にシーンが切り替わっていれば cam 自体が別物になっている)。
        public static void ResetCapture()
        {
            _hasOriginal = false;
            _capturedCamera = null;
        }
    }
}
