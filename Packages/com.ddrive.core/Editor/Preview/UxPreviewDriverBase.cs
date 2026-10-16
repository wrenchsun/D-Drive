using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Preview
{
    // 確認用プレビューを自動で止めて片付ける理由。
    public enum PreviewResetReason
    {
        SceneChanged,
        PrefabStageChanged,
        EnteringPlayMode,
        AssemblyReload,
        SceneSaving,
        Dispose,
    }

    // [1014_extension_platform.md] §2.3(X-3) — 持ち込み先の専用エディターが「▶ で自分の Manager を実物で動かし ■ で戻す」を
    // 書くための汎用の基底(既存の Scene*PreviewDriver と同じ作法を、種別に依存しない形で切り出したもの)。
    //
    // 基底が持つもの: ① プレビューのルート(名前は "[D-Drive] " で始まる DontSave。開いているシーン、プレハブモード中はステージに置く。
    // EditorPreviewSweeper の掃除対象)/ ② Edit Mode の Tick 駆動(EditorApplication.update、dt は 0.25 秒で頭打ち、Speed を掛けて
    // OnTick へ。Paused 中は呼ばない)/ ③ シーン切替・プレハブモードの開閉・Play Mode 突入・再コンパイル・シーン保存の直前・Dispose での
    // 自動停止(OnReset)。ルートはシーン保存の直前以外では破棄する(保存では DontSave のルートは書き出されないので残す)。
    // 派生は OnTick で Manager.Tick を呼び、OnReset で再生中の実体を止めて台帳を空にする。Manager の生成・登録は派生の責任。
    public abstract class UxPreviewDriverBase : IDisposable
    {
        public const string RootNamePrefix = EditorPreviewSweeper.Prefix + " ";

        private readonly string _rootName;
        private GameObject _previewRoot;
        private double _lastTickTime;
        private float _speed = 1f;
        private bool _paused;
        private bool _disposed;

        // rootName は "[D-Drive]" で始まらなければ補う(掃除の対象にするため)。
        protected UxPreviewDriverBase(string rootName)
        {
            if (string.IsNullOrEmpty(rootName))
            {
                rootName = "Preview";
            }

            _rootName = rootName.StartsWith(EditorPreviewSweeper.Prefix, StringComparison.Ordinal) ? rootName : RootNamePrefix + rootName;
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        public string RootName => _rootName;

        public bool IsDisposed => _disposed;

        // スポーン物をまとめる親(遅延生成。シーン切替で消えたら作り直す)。
        public GameObject PreviewRoot
        {
            get
            {
                if (_previewRoot == null)
                {
                    _previewRoot = EditorPreviewRoots.CreateRoot(_rootName);
                    StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
                }

                return _previewRoot;
            }
        }

        // 再生速度(0.1x–2x)。OnTick には dt に掛けた値が渡る。
        public float Speed
        {
            get => _speed;
            set => _speed = Mathf.Clamp(value, 0.1f, 2f);
        }

        // true の間は OnTick を呼ばない(Manager を止める処理は派生が OnPausedChanged で行う)。
        public bool Paused
        {
            get => _paused;
            set
            {
                if (_paused == value)
                {
                    return;
                }

                _paused = value;
                OnPausedChanged(value);
            }
        }

        // テストからも呼べる Tick(EditorApplication.update の実体)。dt は秒、Speed は基底が掛ける。
        public void Tick(float dt)
        {
            if (_disposed || _paused)
            {
                return;
            }

            OnTick(dt * _speed);
        }

        protected abstract void OnTick(float scaledDt);

        // 再生中の実体を止めて台帳を空にする。ルートの破棄は基底が行う。
        protected abstract void OnReset(PreviewResetReason reason);

        protected virtual void OnPausedChanged(bool paused) { }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Reset(PreviewResetReason.Dispose);
            _disposed = true;
            EditorApplication.update -= EditorTick;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
        }

        private void EditorTick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            // X-R-09: Play Mode 突入後(自動停止の後)は Tick を回さない。
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            Tick(dt);
        }

        private void Reset(PreviewResetReason reason)
        {
            try
            {
                OnReset(reason);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] プレビュー '{_rootName}' の停止中に例外が出ました(続行します): {e.Message}");
            }

            if (reason != PreviewResetReason.SceneSaving && _previewRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewRoot);
                _previewRoot = null;
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene current) => Reset(PreviewResetReason.SceneChanged);

        private void OnPrefabStageChanged(PrefabStage stage) => Reset(PreviewResetReason.PrefabStageChanged);

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                Reset(PreviewResetReason.EnteringPlayMode);
            }
        }

        private void OnBeforeAssemblyReload() => Reset(PreviewResetReason.AssemblyReload);

        private void OnSceneSaving(Scene scene, string path) => Reset(PreviewResetReason.SceneSaving);
    }
}
