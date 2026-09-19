using System;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Registry;
using DDrive.Runtime.CameraShake;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.CameraFx
{
    // [16_camera_haptics.md] §C-2(5-2c) — ShakeEditor の「実際にカメラを揺らして確認」。
    // ウィンドウ内には何も描かず、開いているシーンの Camera.main を実 CameraFxManager で揺らして
    // SceneView / Game ビューで確認する(ADR-4: 実 Manager を Editor から駆動する。Editor 専用の
    // 再生経路を作らない)。
    //
    // CameraFxManager は Tick 中に Camera.main の直上へ "DDriveCameraShakeNode" を挿入して
    // カメラをその子にする(ランタイムの正規動作。ノード自体は永続する前提)。エディタでこれをそのまま
    // 使うと、確認後にシーンを保存したときカメラの親子構造(揺れたオフセット)がシーンに焼き込まれて
    // しまう。そのため、このドライバは:
    //   - 初回 Tick 開始前にカメラの元の親 / Sibling Index / ローカル姿勢を記録する
    //   - 毎 Tick 後、生成されたシェイクノードを見つけ次第 HideFlags.DontSave を付ける
    //     (CLAUDE.md §0-7「Shake ノードは DontSave」)
    //   - シーン保存の直前(EditorSceneManager.sceneSaving)・ウィンドウを閉じる(Dispose)・
    //     シーン切替 / プレハブモード切替のタイミングで、カメラを元の親子構造・ローカル姿勢へ完全に戻し、
    //     ノードを破棄する(SceneAnimPreviewDriver の OnSceneSaving と同じパターン)。
    //     再生中に保存された場合、次の Tick で再度ノードが挿入されて揺れは続く(Anim と同じ「保存を優先」方針)。
    public sealed class SceneCameraShakePreviewDriver : IDisposable
    {
        private const string ShakeNodeName = "DDriveCameraShakeNode";

        private readonly AssetRegistry _registry;
        private readonly ViewRepaintThrottle _repaint = new();
        private double _lastTick;
        private bool _ticking;

        private Transform _cameraTransform;
        private Transform _originalParent;
        private int _originalSiblingIndex;
        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;
        private Transform _shakeNode;

        public CameraFxManager Manager { get; }

        // registry: 省略時はプロジェクト内アセットを登録した EditorAnchorRegistry(Shake/Haptics 自体は
        // ID 解決を経由しない ShakeData/PlayData を使うため、実質何を渡しても良い)。テストは差し替える。
        public SceneCameraShakePreviewDriver(AssetRegistry registry = null)
        {
            _registry = registry ?? EditorAnchorRegistry.Build();
            Manager = new CameraFxManager(_registry);
            EditorSceneManager.activeSceneChangedInEditMode += OnStageChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // 現在 Camera.main が見つかっているか(見つからない場合は確認用シーンの案内を出す用)。
        public bool HasCamera => UnityEngine.Camera.main != null;

        // 合成に寄与しうる Instance 数(連打テストの表示用)。
        public int ActiveCount => Manager.ActiveCount;

        public Handle<ShakeMarker> Play(CameraShakeData data, float strengthScale = 1f)
        {
            if (data == null)
            {
                return Handle<ShakeMarker>.Invalid;
            }

            EnsureTicking();
            return Manager.ShakeData(data, strengthScale: strengthScale);
        }

        // デザイナー向け一斉停止(フェード付き)。ノードの取り外しはしない(連打テストを続けられるように)。
        public void StopAll(float fadeOut = 0.1f) => Manager.StopAllWithFade(fadeOut);

        private void EnsureTicking()
        {
            if (_ticking)
            {
                return;
            }

            _ticking = true;
            _lastTick = EditorApplication.timeSinceStartup;
            CaptureOriginalCameraState();
            EditorApplication.update += EditorTick;
        }

        private void CaptureOriginalCameraState()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                _cameraTransform = null;
                return;
            }

            _cameraTransform = cam.transform;
            _originalParent = _cameraTransform.parent;
            _originalSiblingIndex = _cameraTransform.GetSiblingIndex();
            _originalLocalPos = _cameraTransform.localPosition;
            _originalLocalRot = _cameraTransform.localRotation;
        }

        private void EditorTick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTick), 0f, 0.25f);
            _lastTick = now;
            Tick(dt);
        }

        // テストからも直接呼べる公開 Tick(EditorApplication.update の実体。SceneVfxPreviewDriver と同じ形)。
        public void Tick(float dt)
        {
            Manager.Tick(dt);
            MarkShakeNodeDontSave();

            if (Manager.ActiveCount > 0)
            {
                _repaint.Request();
            }
        }

        // CameraFxManager が挿入したノードを見つけ次第 DontSave にする([09] §2 プレビュー規約)。
        // 名前一致に加え、記録しておいたカメラの親であることも確認する(デザイナーが同名の本物の
        // オブジェクトを置いていた場合に誤って触らないため)。
        private void MarkShakeNodeDontSave()
        {
            if (_cameraTransform == null)
            {
                return;
            }

            var parent = _cameraTransform.parent;
            if (parent == null || parent.name != ShakeNodeName)
            {
                return;
            }

            if ((parent.gameObject.hideFlags & HideFlags.DontSave) == 0)
            {
                parent.gameObject.hideFlags = HideFlags.DontSave;
            }

            _shakeNode = parent;
        }

        // カメラを元の親子構造・ローカル姿勢へ完全に戻し、ノードを破棄する。ティックは止めない
        // (次に Play() されたときすぐ再開できるよう Manager 自体は生かしたまま。再度 Tick が
        // 走ればノードは再挿入される)。
        public void RestoreCameraNow()
        {
            if (_cameraTransform == null)
            {
                return;
            }

            _cameraTransform.SetParent(_originalParent, false);
            _cameraTransform.SetSiblingIndex(_originalSiblingIndex);
            _cameraTransform.localPosition = _originalLocalPos;
            _cameraTransform.localRotation = _originalLocalRot;

            if (_shakeNode != null)
            {
                UnityEngine.Object.DestroyImmediate(_shakeNode.gameObject);
            }

            _shakeNode = null;
        }

        // ティックも止めて完全に手を離す(ウィンドウを閉じる・シーン切替・Play Mode 突入の直前)。
        public void StopAndRestore()
        {
            if (!_ticking)
            {
                return;
            }

            _ticking = false;
            EditorApplication.update -= EditorTick;
            Manager.StopAll(StopReason.Manual);
            RestoreCameraNow();
            _cameraTransform = null;
        }

        // シーン保存の直前に、揺れたオフセット/ノードの親子構造がシーンへ焼き込まれないよう元に戻す。
        // 再生中なら次の Tick で再度ノードが挿入されて揺れは続く(SceneAnimPreviewDriver.OnSceneSaving と同じ方針)。
        private void OnSceneSaving(Scene scene, string path)
        {
            if (_cameraTransform == null || _cameraTransform.gameObject.scene != scene)
            {
                return;
            }

            RestoreCameraNow();
        }

        private void OnStageChanged(Scene previous, Scene current) => StopAndRestore();

        private void OnPrefabStageChanged(PrefabStage stage) => StopAndRestore();

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                StopAndRestore();
            }
        }

        public void Dispose()
        {
            StopAndRestore();
            EditorSceneManager.activeSceneChangedInEditMode -= OnStageChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }
    }
}
