using DDrive.Runtime.Cutscene.Tracks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.6.2 — CutsceneManager が Tick(Update)で計算した「この 1 フレームに書くべき値」。
    // Camera.main の座標系(ワールド)で渡す(原点([26] §4.2.1)は CutsceneManager 側で掛け済み)。
    public struct CutsceneCameraWriteRequest
    {
        public Vector3 WorldPos;
        public Quaternion WorldRot;
        public float Fov;
        public float FocusDistance;
        public float Aperture;
        public float FocalLength;
        public float Weight; // ゲームカメラとのブレンド重み w([26] §4.6.2)。0 は「何もしない」。
        public CameraFocusMode Focus;
    }

    // [26_timeline.md] §4.6.5 — 実行順の契約の D-Drive 側の履行。CutsceneManager.Tick(Update フェーズ)が
    // 評価だけを行い、実際の Camera/Volume への書き込みはここ(LateUpdate、実行順 1000)で行う。
    // ゲーム側のカメラ制御(契約 G-1: LateUpdate なら実行順 1000 未満)が先に書いた「今フレームの G」を
    // 読んでからブレンドして上書きするため、Cutscene が無いフレーム(Weight=0)は一切触らない。
    //
    // CameraFx(5-2)の DDriveCameraShakeNode と同じ「無ければ作る、見つからなければ警告 1 回 + no-op」の
    // 流儀。Camera.main に直接コンポーネントを付ける(Shake ノードのような親子挿入はしない。Camera 本体の
    // Transform に書くため、Shake の親ノードと両立する、[26] §4.6.2)。
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class DDriveCutsceneCameraApplier : MonoBehaviour
    {
        // [26] §4.6.5 — DDriveRuntimeBootstrap の -1000 と対称(「-1000=誰よりも先に組み立てる」
        // 「+1000=誰よりも後にカメラを書く」の両端を D-Drive が占める)。
        public const int ExecutionOrder = 1000;

        private bool _hasPending;
        private CutsceneCameraWriteRequest _pending;

        private bool _restoreValid;
        private float _restoreFov;
        private float _restoreFocusDistance;

        private GameObject _volumeGo;
        private Volume _volume;
        private VolumeProfile _profile;
        private DepthOfField _dof;
        private bool _volumeWarned;

        // ── 検出 2([26] §4.6.5): endCameraRendering 時点の姿勢が自分の書き込みと一致するか ──
        private bool _appliedLateUpdateThisFrame;
        private Vector3 _lastAppliedPos;
        private Quaternion _lastAppliedRot = Quaternion.identity;
        private float _lastAppliedFov;
        private object _currentOwnerKey;
        private bool _overwriteWarnedThisPlayback;
        private int _overwriteFrameCount;
        private int _totalFrameCount;

        // ── 検出 3: Submit() したのに LateUpdate が走らなかった(無効化・破棄・Camera.main 差し替え) ──
        private bool _hasEverSubmitted;
        private bool _ranLateUpdateSinceLastSubmit;

        public static DDriveCutsceneCameraApplier EnsureOn(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            var applier = camera.GetComponent<DDriveCutsceneCameraApplier>();
            if (applier == null)
            {
                applier = camera.gameObject.AddComponent<DDriveCutsceneCameraApplier>();
            }
            else if (!applier.enabled)
            {
                applier.enabled = true;
            }

            return applier;
        }

        // CutsceneManager.Tick から 1 フレームにつき最大 1 回呼ぶ。戻り値 false は「前回の Submit が
        // LateUpdate で適用されなかった」ことを示す(検出 3。呼び出し側は警告 + そのプレイバックを
        // BlendOut 無しで終了させる、[26] §4.6.5 検出 3)。
        public bool Submit(in CutsceneCameraWriteRequest req, object ownerKey)
        {
            if (!Equals(ownerKey, _currentOwnerKey))
            {
                _currentOwnerKey = ownerKey;
                _overwriteWarnedThisPlayback = false;
                _overwriteFrameCount = 0;
                _totalFrameCount = 0;
            }

            var ranSinceLastSubmit = !_hasEverSubmitted || _ranLateUpdateSinceLastSubmit;
            _hasEverSubmitted = true;
            _ranLateUpdateSinceLastSubmit = false;

            _pending = req;
            _hasPending = true;
            return ranSinceLastSubmit;
        }

        // 所有権が終わったとき(Cleanup / G-5 / 検出 3)に呼ぶ。控えていた画角・ピントを書き戻し、
        // Volume の weight を 0 に戻す([26] §4.6.2「w=0 のフレームは何もしない」と同じ最終状態)。
        public void Restore()
        {
            var cam = GetComponent<Camera>();
            if (cam != null && _restoreValid)
            {
                cam.fieldOfView = _restoreFov;
                cam.focusDistance = _restoreFocusDistance;
            }

            _restoreValid = false;
            _hasPending = false;

            if (_volume != null)
            {
                _volume.weight = 0f;
            }

            _overwriteWarnedThisPlayback = false;
            _overwriteFrameCount = 0;
            _totalFrameCount = 0;
            _currentOwnerKey = null;
        }

        private void LateUpdate()
        {
            _ranLateUpdateSinceLastSubmit = true;

            if (!_hasPending)
            {
                _appliedLateUpdateThisFrame = false;
                return;
            }

            _hasPending = false;

            var cam = GetComponent<Camera>();
            if (cam == null)
            {
                _appliedLateUpdateThisFrame = false;
                return;
            }

            var w = Mathf.Clamp01(_pending.Weight);
            if (w <= 0f)
            {
                // [26] §4.6.2 — w=0 のフレームは一切触らない。
                _appliedLateUpdateThisFrame = false;
                return;
            }

            if (!_restoreValid)
            {
                // 再生開始時に一度だけ控える([26] §4.6.2「再生開始時に控えた値を G とし、終了時に書き戻す」)。
                _restoreFov = cam.fieldOfView;
                _restoreFocusDistance = cam.focusDistance;
                _restoreValid = true;
            }

            // G(このフレームにゲーム側が書いた姿勢)は「書き込み直前」に読む(契約 G-1 により、実行順 1000
            // 未満のゲーム側 LateUpdate はここより先に走っている、[26] §4.6.5)。
            var g = cam.transform.position;
            var gr = cam.transform.rotation;

            cam.transform.SetPositionAndRotation(
                Vector3.Lerp(g, _pending.WorldPos, w),
                Quaternion.Slerp(gr, _pending.WorldRot, w));
            cam.fieldOfView = Mathf.Lerp(_restoreFov, _pending.Fov, w);

            ApplyFocus(cam, w);

            _lastAppliedPos = cam.transform.position;
            _lastAppliedRot = cam.transform.rotation;
            _lastAppliedFov = cam.fieldOfView;
            _appliedLateUpdateThisFrame = true;
        }

        // [26_timeline.md] §4.6.4 — 画角/ピント距離は Camera にも書く。絞り・焦点距離は URP の Volume の
        // DepthOfField のみ(Camera 側に相当 API が無い)。Focus=Off は何も書かない、CameraOnly は
        // Camera.focusDistance のみ(Volume は作らない、HDRP 移植時の逃げ道)。
        private void ApplyFocus(Camera cam, float w)
        {
            // [26_timeline.md] §4.6.4 / docs/45 P1-2(2026-09-20) 二重防御 — 取り込み側(CutsceneImportService.
            // ResolveInitialFocusMode)が「取れなければ Focus=Off」を新規クリップにしか適用できない
            // (再取り込みは既存の Focus を保持する)ため、取り込み済みデータで Focus=Volume のまま
            // ピント距離カーブが空(評価結果が 0 以下)になるケースが起こりうる。その場合は Off と同じ扱いに
            // して、Mathf.Max(0.01f, 0) の 1cm 張り付きを書かない。
            if (_pending.Focus == CameraFocusMode.Off || _pending.FocusDistance <= 0f)
            {
                if (_volume != null)
                {
                    _volume.weight = 0f;
                }

                return;
            }

            cam.focusDistance = _pending.FocusDistance;

            if (_pending.Focus == CameraFocusMode.CameraOnly)
            {
                if (_volume != null)
                {
                    _volume.weight = 0f;
                }

                return;
            }

            if (!EnsureVolume(cam))
            {
                return;
            }

            _dof.focusDistance.value = Mathf.Max(0.01f, _pending.FocusDistance);
            _dof.aperture.value = Mathf.Clamp(_pending.Aperture > 0f ? _pending.Aperture : 5.6f, 0.05f, 32f);
            _dof.focalLength.value = Mathf.Max(1f, _pending.FocalLength > 0f ? _pending.FocalLength : 50f);
            _volume.weight = w;
        }

        // [26] §4.6.4 — 「D-Drive 専用の Global Volume を初回に 1 つ作る。毎フレームの alloc は無し
        // (プロファイルとオーバーライドは 1 回だけ作り、値を書き換えるだけ)」。
        private bool EnsureVolume(Camera cam)
        {
            if (_volume != null)
            {
                return true;
            }

            var layer = ResolveVolumeLayer(cam);
            if (layer < 0)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (!_volumeWarned)
                {
                    _volumeWarned = true;
                    Debug.LogWarning("[DDrive] Cutscene: Camera の Volume マスクにレイヤーが見つからないため、DoF(ピント)は書き込みません([26_timeline.md] §4.6.4)。");
                }
#endif
                return false;
            }

            _volumeGo = new GameObject("DDriveCutsceneVolume") { layer = layer };
            _volumeGo.transform.SetParent(transform, false);
            _volume = _volumeGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 1000f;
            _volume.weight = 0f;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _dof = _profile.Add<DepthOfField>(true);
            _dof.mode.overrideState = true;
            _dof.mode.value = DepthOfFieldMode.Bokeh;
            _dof.focusDistance.overrideState = true;
            _dof.aperture.overrideState = true;
            _dof.focalLength.overrideState = true;
            _volume.sharedProfile = _profile;

            if (gameObject.scene.name == "DontDestroyOnLoad")
            {
                DontDestroyOnLoad(_volumeGo);
            }

            return true;
        }

        private static int ResolveVolumeLayer(Camera cam)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            var mask = data != null ? data.volumeLayerMask.value : 0;
            if (mask == 0)
            {
                return -1;
            }

            for (var i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    return i;
                }
            }

            return -1;
        }

        // [26] §4.6.5 検出 2 — Editor / Development Build のみ。描画に使われた姿勢と自分の書き込みを比較する。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (!_appliedLateUpdateThisFrame || cam.transform != transform)
            {
                return;
            }

            _totalFrameCount++;

            var posDiff = (cam.transform.position - _lastAppliedPos).sqrMagnitude;
            var rotDiff = Quaternion.Angle(cam.transform.rotation, _lastAppliedRot);
            var fovDiff = Mathf.Abs(cam.fieldOfView - _lastAppliedFov);

            if (posDiff > 1e-4f * 1e-4f || rotDiff > 1e-4f || fovDiff > 1e-4f)
            {
                _overwriteFrameCount++;
                if (!_overwriteWarnedThisPlayback)
                {
                    _overwriteWarnedThisPlayback = true;
                    Debug.LogWarning($"[DDrive] Cutscene: カメラの書き込みが上書きされました({_overwriteFrameCount}/{_totalFrameCount} フレーム)。ゲームカメラ制御の実行順を確認してください([26_timeline.md] §4.6.5、CameraExecutionOrderValidator は 6-10d)。");
                }
            }

            _appliedLateUpdateThisFrame = false;
        }
#endif
    }
}
