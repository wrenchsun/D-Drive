using System;
using DDrive.Editor.CameraFx;
using DDrive.Editor.Preview;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Event;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19 追加) — Cutscene の SE/VFX/UI/AnchorGroup
    // クリップ + Event/Shake/Haptic マーカーが Edit Mode で使う実 Manager 一式を束ねる。
    // 「1 種別 1 ドライバ」の既存資産(SceneVfxPreviewDriver / SceneCameraShakePreviewDriver /
    // EditorHapticsPreviewDriver)をそのまま組み合わせるだけで、二重実装しない
    // (`ScenePresentationPreviewDriver` と同じ構成方針。ADR-4「実 Manager を Editor から駆動する」)。
    //
    // Presentation クリップは含めない(§4.4 実装メモの簡略化: Edit Mode 未対応。PresentationManager は
    // Audio/Bgm/Vfx/Anim/Ui/UiTween/CameraFx/Haptics/NetBridge を束ねる大掛かりな配線が要り、本対応の
    // スコープでは Play Mode で確認する運用に留めた)。
    public sealed class CutsceneEditModeManagers : IDisposable
    {
        public const string PreviewRootName = "[D-Drive] Cutscene Edit Preview";

        public AssetRegistry Registry { get; }

        // Shake/Haptic マーカーはクリップではないため CutsceneDirectorManagerRefs には入れず、
        // 監視役(CutsceneEditModeMarkerWatcher)がここから直接引く。
        public SceneCameraShakePreviewDriver ShakeDriver { get; }
        public EditorHapticsPreviewDriver HapticsDriver { get; }

        // SE/VFX/UI/AnchorGroup クリップ用(CutsceneDirectorContext.ManagerRefs に渡す)。
        public AudioManager Audio { get; private set; }
        public SceneVfxPreviewDriver VfxDriver { get; private set; }
        public VfxManager Vfx => VfxDriver?.Manager;
        public UiManager Ui { get; private set; }
        public AnchorGroupPlayer Groups { get; private set; }

        // Event マーカー(AssetEvent の PlayAsset)用。
        public AssetEventDispatcher Dispatcher { get; private set; }

        // Bindings の Target=SpawnModel を解決するための Editor 用 ModelsManager
        // (`CutsceneEditModeDirectorSetup` がプレビュー用 Director を組み立てるときに使う)。
        public ModelsManager Models => _models;

        private ModelsManager _models;
        private MaterialManager _materials;
        private EventBus _eventMarkerBus;
        private GameObject _root;
        private PoolService _pool;

        public CutsceneEditModeManagers(AssetRegistry registry = null)
        {
            Registry = registry ?? EditorAnchorRegistry.Build();
            ShakeDriver = new SceneCameraShakePreviewDriver(Registry);
            HapticsDriver = new EditorHapticsPreviewDriver(Registry);
            EnsureManagers();
        }

        // アセット追加後に呼び直す(SceneAnimPreviewDriver.RefreshRegistry と同じ位置付け)。
        public void RefreshRegistry()
        {
            EditorAnchorRegistry.Refresh(Registry);
            _materials?.Clear();
        }

        // Event マーカー通過時に呼ぶ(AssetEventDispatcher.HandleEvent を直接叩く。Repeat=KeepWhilePlaying の
        // 後始末は StopAll() まで持ち越す簡易実装 — Editor プレビューは短時間の確認用途のため許容する)。
        public void RaiseEvent(in AssetEvent evt)
            => _eventMarkerBus?.RaiseAdHoc(new InstanceContext(0, 0), in evt);

        private void EnsureManagers()
        {
            if (_root != null && Audio != null)
            {
                return;
            }

            _root = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            StageUtility.PlaceGameObjectInCurrentStage(_root);

            _pool = new PoolService();
            _pool.SetInstanceParent(_root.transform);
            Audio = EditorAudioFactory.Create(_pool, _root.transform, Registry);
            VfxDriver = new SceneVfxPreviewDriver(Registry);
            _materials = new MaterialManager(Registry);
            _models = new ModelsManager(_pool, Registry, null, _materials);
            Groups = new AnchorGroupPlayer(Registry, Vfx, Audio);
            Ui = new UiManager(_pool, Registry);

            _eventMarkerBus = new EventBus();
            Dispatcher = new AssetEventDispatcher(_eventMarkerBus, Registry, Audio, Vfx, _ => null, Groups);
        }

        // Audio/Models/Groups/Ui の毎フレーム進行。Vfx/Shake/Haptics は自前で EditorApplication.update に
        // 乗って自走する(SceneVfxPreviewDriver/SceneCameraShakePreviewDriver/EditorHapticsPreviewDriver の
        // 既存実装どおり)ためここではティックしない。
        public void Tick(float dt)
        {
            Audio?.Tick(dt);
            _models?.Tick(dt);
            Groups?.Tick();
            Ui?.Tick(dt);
        }

        public void Dispose()
        {
            VfxDriver?.Dispose();
            ShakeDriver.Dispose();
            HapticsDriver.Dispose();
            Dispatcher?.Dispose();
            Dispatcher = null;

            Audio?.StopAll(StopReason.Manual);
            Groups?.StopAll();
            Ui?.StopAll(StopReason.Manual);
            _materials?.Clear();

            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            _root = null;
            Audio = null;
            VfxDriver = null;
            Ui = null;
            Groups = null;
            _models = null;
            _materials = null;
            _pool = null;
        }
    }
}
