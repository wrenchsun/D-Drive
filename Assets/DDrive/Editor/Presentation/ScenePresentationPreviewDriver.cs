using System;
using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.CameraFx;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
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

        // [22_anchor_group.md] §5(Presentation 統合) — TrackKind.AnchorGroup が出した VFX を
        // AnimDriver.Vfx(SceneVfxPreviewDriver)へ Adopt するための台帳(AnimDriver.AdoptGroupVfx と同じ設計。
        // Manager.OnAnchorGroupPlayed で追加し、Tick 毎に生存確認して再生終了分を外す)。
        private readonly List<Handle<AnchorGroupMarker>> _groupHandles = new();
        private readonly List<Handle<VfxMarker>> _vfxScratch = new();

        // ctx.Self として使う Transform(SpawnModel で配置したモデル、または借用中の Animator)。無ければ null。
        public Transform SelfRoot => AnimDriver.CurrentRoot != null ? AnimDriver.CurrentRoot.transform : null;

        public bool HasSelf => SelfRoot != null;

        public ScenePresentationPreviewDriver(AssetRegistry registry = null)
        {
            Registry = registry ?? EditorAnchorRegistry.Build();
            // P5 レビュー対応(2026-09-14) 5-4 追補(b): AnimDriver(内部の Vfx を含む)/HapticsDriver に
            // この Time(TimeService)を共有させ、HitStop 中は自前の Unscaled dt へ ScaledDeltaTime を
            // 掛けて止まるようにする。ShakeDriver には渡さない(ランタイムの CameraFx は HitStop 中も
            // 揺れを止めない仕様のままにする。[16] Part A / docs/08 実装メモ参照)。
            AnimDriver = new SceneAnimPreviewDriver(Registry, Time);
            ShakeDriver = new SceneCameraShakePreviewDriver(Registry);
            HapticsDriver = new EditorHapticsPreviewDriver(Registry, timeService: Time);
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
            // [22_anchor_group.md] §5 — AnimDriver.Groups は AnimDriver.EnsureManagers()(SpawnModel/PreviewSe 等
            // で初めて呼ばれる)が済むまで null のままのことがある。EnsureAudio() はコンストラクタ時点で
            // 一度 Manager を作った後は _root が生き続ける限り何もしない(下記 EnsureAudio 参照)ため、
            // 「配置」→「再生」の通常操作順では初回 Manager 構築時に Groups がまだ null だった、という
            // タイミング問題が起き得る。直前で StopCurrent() 済み(副作用なし)なので、Play() の都度作り直す。
            RebuildManager();
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
        // PresentationManager.Tick(AtTime の進行)に反映される)。
        // P5 レビュー対応(2026-09-14) 5-4 追補(b): AnimDriver/Vfx/Haptics はそれぞれ自分の
        // EditorApplication.update フックで自走しているが、コンストラクタでこの Time を共有させたため、
        // 各ドライバの EditorTick が Time.ScaledDeltaTime(現在の TimeScale を読むだけの純関数)を
        // 掛けてから Tick するようになり、HitStop 中は AtTime の進行と同様にそれらも止まる
        // (Time.Tick(...) 自体はここで 1 回だけ呼び、他ドライバ側では呼ばない。二重減算を避ける)。
        // ShakeDriver(CameraFx)だけはランタイム仕様どおり Unscaled のまま(HitStop 中も揺れを止めない)。
        public void Tick(float dt)
        {
            Time.Tick(dt);
            Manager?.Tick(Time.ScaledDeltaTime(dt));
            AdoptGroupVfx();
        }

        // AnimDriver.AdoptGroupVfx と同じ考え方: 配置セットのディレイ待ちで後から生まれた VFX も含めて
        // 毎 Tick 拾い直す。台帳が空なら走査コストは実質 0。
        private void OnGroupPlayed(Handle<AnchorGroupMarker> handle)
        {
            if (!_groupHandles.Contains(handle))
            {
                _groupHandles.Add(handle);
            }

            AdoptGroupVfx();
        }

        private void AdoptGroupVfx()
        {
            var groups = AnimDriver.Groups;
            if (groups == null || _groupHandles.Count == 0)
            {
                return;
            }

            for (var i = _groupHandles.Count - 1; i >= 0; i--)
            {
                if (!groups.IsPlaying(_groupHandles[i]))
                {
                    _groupHandles.RemoveAt(i);
                    continue;
                }

                _vfxScratch.Clear();
                groups.CollectVfxHandles(_groupHandles[i], _vfxScratch);
                foreach (var h in _vfxScratch)
                {
                    AnimDriver.Vfx.Adopt(h);
                }
            }
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
                haptics: HapticsDriver.Manager,
                groups: AnimDriver.Groups);
            Manager.OnAnchorGroupPlayed += OnGroupPlayed;
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
            _groupHandles.Clear();
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
