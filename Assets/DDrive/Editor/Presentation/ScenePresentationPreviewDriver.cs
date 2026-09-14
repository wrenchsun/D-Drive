using System;
using DDrive.Editor.Anim;
using DDrive.Editor.CameraFx;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using R3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4(5-4) — PresentationEditor の統合プレビュー。ウィンドウ内には何も描かず、
    // 開いているシーン / プレハブモードで実 PresentationManager を駆動する(ADR-4、CLAUDE.md §0-7)。
    //
    // 各 Kind の委譲先は「1 種別 1 ドライバ」の既存資産をそのまま束ねるだけで、二重実装しない:
    //   Anim/Anim2D + Vfx + モデル配置(「モデル選択」) → SceneAnimPreviewDriver(3-3/3-4 で実装済み。
    //     ModelData を SpawnModel した Animator/Transform をそのまま PlayContext.Self にする)
    //   CameraShake → SceneCameraShakePreviewDriver(5-2c)
    //   Haptic      → EditorHapticsPreviewDriver(5-2c)
    //   Se          → このドライバ専用の実 AudioManager(EditorAudioFactory。AnimDriver.Audio は
    //     ステージ切替のたびに作り直される内部実装のため、Presentation 側では直接持たない)
    //   Bgm/Canvas/UiTween → 5-4 時点では未配線(PresentationManager の「Manager 未設定」警告 + no-op で継続)
    // 全員が同じ EditorAnchorRegistry を共有するので、ID 解決はランタイムと同じ結果になる。
    public sealed class ScenePresentationPreviewDriver : IDisposable
    {
        public const string PreviewRootName = "[D-Drive] Presentation Preview";

        public AssetRegistry Registry { get; }
        public SceneAnimPreviewDriver AnimDriver { get; }
        public SceneCameraShakePreviewDriver ShakeDriver { get; }
        public EditorHapticsPreviewDriver HapticsDriver { get; }
        public TimeService Time { get; } = new();

        // Play() のたびに _audio(このドライバのシーン内 AudioManager)を束ねて作り直す(下記 EnsureAudio 参照)。
        public PresentationManager Manager { get; private set; }

        public Handle<PresentationMarker> Current { get; private set; } = Handle<PresentationMarker>.Invalid;

        // Kind=Signal トラック(データ→コード方向の通知)。ウィンドウはログ表示に使う。
        public event Action<string> OnDataSignal;

        private GameObject _root;
        private PoolService _pool;
        private AudioManager _audio;
        private double _lastTick;
        private bool _ticking;

        // ctx.Self として使う Transform(SpawnModel で配置したモデル、または借用中の Animator)。無ければ null。
        public Transform SelfRoot => AnimDriver.CurrentRoot != null ? AnimDriver.CurrentRoot.transform : null;

        public bool HasSelf => SelfRoot != null;

        public ScenePresentationPreviewDriver(AssetRegistry registry = null)
        {
            Registry = registry ?? EditorAnchorRegistry.Build();
            AnimDriver = new SceneAnimPreviewDriver(Registry);
            ShakeDriver = new SceneCameraShakePreviewDriver(Registry);
            HapticsDriver = new EditorHapticsPreviewDriver(Registry);
            EnsureAudio();

            EditorSceneManager.activeSceneChangedInEditMode += OnStageChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // ── モデル配置(「モデル選択」、ModelEditorWindow / AnimEditorWindow と同じ SpawnModel を再利用) ──

        public Animator SpawnModel(ModelData model, Vector3 position, Quaternion rotation)
            => AnimDriver.SpawnModel(model, position, rotation, requireAnimator: false);

        public void ReleaseModel() => AnimDriver.ReleaseTarget();

        // ── 再生 ──

        public Handle<PresentationMarker> Play(PresentationData data)
        {
            if (data == null)
            {
                return Handle<PresentationMarker>.Invalid;
            }

            StopCurrent();
            EnsureAudio();
            EnsureTicking();

            var self = SelfRoot;
            var ctx = new PlayContext
            {
                Self = self,
                Target = null,
                Position = self != null ? self.position : Vector3.zero,
                OnSignal = key => OnDataSignal?.Invoke(key),
            };

            Current = Manager.PlayData(data, in ctx);
            return Current;
        }

        public void Signal(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                Manager.Signal(Current, key);
            }
        }

        public void Cancel() => Manager.Cancel(Current);

        public void SetPaused(bool paused) => Manager.SetPaused(Current, paused);

        public void SetSpeed(float speed) => Manager.SetSpeed(Current, speed);

        public void Seek(float time) => Manager.Seek(Current, time);

        public bool IsPlaying => Manager != null && Manager.IsPlaying(Current);

        public float NormalizedTime => Manager != null ? Manager.GetNormalizedTime(Current) : -1f;

        public Observable<Unit> OnCompleted => Manager != null ? Manager.OnCompleted(Current) : Observable.Empty<Unit>();

        public Observable<Unit> OnCancelled => Manager != null ? Manager.OnCancelled(Current) : Observable.Empty<Unit>();

        public Observable<string> OnMarker => Manager != null ? Manager.OnMarker(Current) : Observable.Empty<string>();

        public Observable<PresentationTrack> OnTrackFired
            => Manager != null ? Manager.OnTrackFired(Current) : Observable.Empty<PresentationTrack>();

        // 再生中なら止める(タイマー自体は止めない。連続プレビュー用)。
        public void StopCurrent()
        {
            if (Manager != null && Manager.IsPlaying(Current))
            {
                Manager.Cancel(Current);
            }

            Current = Handle<PresentationMarker>.Invalid;
        }

        // ── Tick ──

        private void EnsureTicking()
        {
            if (_ticking)
            {
                return;
            }

            _ticking = true;
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
        }

        private void EditorTick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTick), 0f, 0.25f);
            _lastTick = now;
            Tick(dt);
        }

        // テストからも直接呼べる公開 Tick(他の Scene*PreviewDriver と同じ形)。
        // Time.ScaledDeltaTime を渡す点は GameLoopDriver と同じ(HitStop トラックの TimeScale が
        // PresentationManager.Tick(AtTime の進行)に反映される。ただし AnimDriver/Vfx/Shake/Haptics は
        // それぞれ自分の Unscaled dt で自走しているため、HitStop 中もそれらは止まらない。要判断は
        // docs/28 参照)。
        public void Tick(float dt)
        {
            Time.Tick(dt);
            Manager?.Tick(Time.ScaledDeltaTime(dt));
        }

        // ── ライフサイクル ──

        // Play() のたびに(scene/stage 切替で _audio が失われていれば)作り直す。
        private void EnsureAudio()
        {
            if (_root != null)
            {
                return;
            }

            _root = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            StageUtility.PlaceGameObjectInCurrentStage(_root);
            _pool = new PoolService();
            _pool.SetInstanceParent(_root.transform);
            _audio = EditorAudioFactory.Create(_pool, _root.transform, Registry);
            RebuildManager();
        }

        private void RebuildManager()
        {
            Manager = new PresentationManager(
                Registry,
                Time,
                audio: _audio,
                bgm: null,
                vfx: AnimDriver.Vfx.Manager,
                anim: AnimDriver.Manager,
                ui: null,
                uiTween: null,
                cameraFx: ShakeDriver.Manager,
                haptics: HapticsDriver.Manager);
        }

        private void DestroyAudioRoot()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            _root = null;
            _pool = null;
            _audio = null;
        }

        private void StopAndReset()
        {
            StopCurrent();
            if (_ticking)
            {
                _ticking = false;
                EditorApplication.update -= EditorTick;
            }

            DestroyAudioRoot();
            Manager = null;
        }

        private void OnStageChanged(Scene previous, Scene current) => StopAndReset();

        private void OnPrefabStageChanged(PrefabStage stage) => StopAndReset();

        // Presentation 自体のシーン上の状態(_root)は DontSave なので保存対象にならないが、Se の
        // 再生元(PoolService の Instance)がシーンへ焼き込まれないよう、念のため保存直前にも止める
        // (SceneCameraShakePreviewDriver.OnSceneSaving と同じ方針)。
        private void OnSceneSaving(Scene scene, string path)
        {
            if (_root != null && _root.scene == scene)
            {
                StopCurrent();
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                StopAndReset();
            }
        }

        public void Dispose()
        {
            StopAndReset();
            EditorSceneManager.activeSceneChangedInEditMode -= OnStageChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            HapticsDriver.Dispose();
            ShakeDriver.Dispose();
            AnimDriver.Dispose();
        }
    }
}
