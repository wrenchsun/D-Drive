using UnityEngine;

namespace DDrive.Runtime.Anim2D
{
    // [05_model_animation.md] C-4 — 8 方向 BlendTree の向きをワールドの移動方向から決める補助(2026-09-11)。
    // OH_CASE2026_ITAMI の DirectionalSpriteAnimator にあった「カメラ相対の向き + 指数スムージング」を D-Drive 側に持ってきたもの。
    // ゲーム側は SetWorldDirection(移動ベクトル) を呼ぶだけで、毎フレーム Anim2D.SetDirection に平滑化した x, y が入る。
    //   CameraRelative=true : カメラの Yaw を基準に向きを回す(見下ろし・クォータービューで「画面上の向き」に合わせる)
    //   Smoothing          : 0 で即時。秒単位の時定数(t = 1 - exp(-dt / Smoothing))
    // Data は持たず Animator と Anim2D ファサードにだけ依存する(Manager を new しない)。
    [DisallowMultipleComponent]
    public sealed class Anim2DFacing : MonoBehaviour
    {
        [Tooltip("方向パラメータを書く Animator(未設定なら自身の Animator)。")]
        public Animator Target;

        [Tooltip("カメラの向き基準で回す(Camera.main。無ければワールド基準)。")]
        public bool CameraRelative = true;

        [Tooltip("向きの平滑化(秒。0 で即時)。")]
        [Min(0f)] public float Smoothing = 0.08f;

        [Tooltip("BlendTree の X パラメータ名。")]
        public string ParamX = "x";

        [Tooltip("BlendTree の Y パラメータ名。")]
        public string ParamY = "y";

        [Tooltip("開始時の向き(画面上の 2D。上 = (0,1))。")]
        public Vector2 InitialDirection = Vector2.down;

        private Vector2 _desired;
        private Vector2 _current;
        // フルネームで参照する: [16_camera_haptics.md] 5-2 で追加された DDrive.Runtime.Camera 名前空間と
        // 型名(UnityEngine.Camera)が衝突するため(CS0118)。
        private UnityEngine.Camera _camera;

        // 定常経路(Update)で animator.parameters(配列 alloc)を踏まないよう、対象 / パラメータ名が変わった時だけ解決する。
        private Animator _cachedAnimator;
        private RuntimeAnimatorController _cachedController;
        private string _cachedParamX;
        private string _cachedParamY;
        private int _hashX;
        private int _hashY;

        public Vector2 CurrentDirection => _current;

        private void Awake()
        {
            if (Target == null)
            {
                Target = GetComponent<Animator>();
            }

            _desired = InitialDirection.sqrMagnitude > 0f ? InitialDirection.normalized : Vector2.down;
            _current = _desired;
        }

        // ワールド XZ 平面(3D)の移動ベクトル。長さ 0 なら向きを変えない(最後の向きを保つ)。
        public void SetWorldDirection(Vector3 worldDir)
        {
            var planar = new Vector2(worldDir.x, worldDir.z);
            if (planar.sqrMagnitude <= 1e-6f)
            {
                return;
            }

            var yaw = 0f;
            if (CameraRelative)
            {
                if (_camera == null)
                {
                    _camera = UnityEngine.Camera.main;
                }

                if (_camera != null)
                {
                    yaw = _camera.transform.eulerAngles.y;
                }
            }

            _desired = ToScreenDirection(planar, yaw);
        }

        // 2D(画面上)の向きを直接指定する。
        public void SetScreenDirection(Vector2 screenDir)
        {
            if (screenDir.sqrMagnitude > 1e-6f)
            {
                _desired = screenDir.normalized;
            }
        }

        // ワールド XZ の向きをカメラ Yaw 基準の画面向きへ(上 = カメラの前方)。純関数(テスト用)。
        // Unity の Yaw は上から見て時計回り(+Z → +X)なので、ワールド → 画面はその逆(反時計回り = 数学の正回転)で Yaw ぶん回す。
        public static Vector2 ToScreenDirection(Vector2 worldXZ, float cameraYawDeg)
        {
            var rad = cameraYawDeg * Mathf.Deg2Rad;
            var cos = Mathf.Cos(rad);
            var sin = Mathf.Sin(rad);
            var rotated = new Vector2(worldXZ.x * cos - worldXZ.y * sin, worldXZ.x * sin + worldXZ.y * cos);
            return rotated.sqrMagnitude > 1e-8f ? rotated.normalized : Vector2.down;
        }

        // 指数平滑化の 1 ステップ。純関数(テスト用)。
        public static Vector2 Smooth(Vector2 current, Vector2 desired, float smoothing, float dt)
        {
            if (smoothing <= 0f)
            {
                return desired;
            }

            var t = 1f - Mathf.Exp(-dt / smoothing);
            var next = Vector2.Lerp(current, desired, t);
            return next.sqrMagnitude > 1e-8f ? next.normalized : desired;
        }

        // Animator / パラメータ名の変更を検知してハッシュを取り直す(存在しないパラメータは 0 = 書かない)。
        private void RefreshParameterCache()
        {
            _cachedAnimator = Target;
            _cachedController = Target != null ? Target.runtimeAnimatorController : null;
            _cachedParamX = ParamX;
            _cachedParamY = ParamY;
            _hashX = Anim2D.ResolveFloatParameterHash(Target, ParamX);
            _hashY = Anim2D.ResolveFloatParameterHash(Target, ParamY);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>平滑化と Animator への反映を 1 ステップ進める(通常は Update から呼ばれる。テスト / 独自ループ用に公開)。</summary>
        public void Tick(float dt)
        {
            _current = Smooth(_current, _desired, Smoothing, dt);
            if (Target == null)
            {
                return;
            }

            if (!ReferenceEquals(_cachedAnimator, Target) ||
                !ReferenceEquals(_cachedController, Target.runtimeAnimatorController) ||
                !string.Equals(_cachedParamX, ParamX) ||
                !string.Equals(_cachedParamY, ParamY))
            {
                RefreshParameterCache();
            }

            Anim2D.SetDirection(Target, _current, _hashX, _hashY);
        }
    }
}
