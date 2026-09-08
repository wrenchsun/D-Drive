using System;
using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Vfx
{
    // [04_vfx.md] §5(2026-07-28 改定 / 2026-09-08 改修) — VFX プレビューは独自ビューポートではなく
    // 「開いているシーンに直接スポーンして SceneView で確認する」方式で行う。
    // ライティング・ポストプロセス・Skybox 等はそのシーンの設定がそのまま適用されるため、
    // 確認専用シーンを用意して本番同等の環境で調整できる。
    //
    // プレハブモード(Prefab Stage)が開いている間は、まとめ用ルートをステージのシーンに置くことで
    // エフェクト Prefab の編集中にもその場で再生確認できる(スポーン物はプレハブには保存されない)。
    // ただし「対象 VfxData.Prefab 自身のステージ」では別インスタンスを出すと編集中の実体と二重になるため、
    // ステージ内の ParticleSystem をその場で再生する(InPlace。Inspector で ParticleSystem を編集しながら確認する
    // 前提)。InPlace は Manager を通らないので Anchor / パラメータの即時反映は対象外(擬似ハンドル InPlaceHandle)。
    // ステージの開閉はシーン切替と同じ扱いで、スポーン物を破棄して台帳をリセットする。
    //
    // スポーンした GameObject は HideFlags.DontSave を付けてシーンへ保存されないようにし、
    // Hierarchy を散らかさないよう "[D-Drive] VFX Preview" ルートの下にまとめる。
    // Dispose(ウィンドウを閉じる/再コンパイル)時に必ず破棄する。シーンが切り替わった場合は
    // スポーン物がシーンごと消えるため、台帳をリセットして次の Play からやり直す。
    public sealed class SceneVfxPreviewDriver : IDisposable
    {
        public const string PreviewRootName = "[D-Drive] VFX Preview";

        private readonly PoolService _pool = new();
        private readonly List<(Handle<VfxMarker> handle, ParticleSystem[] systems, VfxData data)> _active = new();
        private readonly HashSet<GameObject> _spawnedRoots = new();
        private double _lastTickTime;
        private float _speed = 1f;
        private GameObject _previewRoot;

        // プレハブモードでの「その場再生」状態。ステージの prefabContentsRoot 配下の ParticleSystem を直接進める。
        private struct InPlaceState
        {
            public bool Active;
            public bool Stopping;
            public float Elapsed;
            public VfxData Data;
            public GameObject Root;
            public ParticleSystem[] Systems;
            public float TimeAfterOurStep; // 直前に自分で進めた後の Systems[0].time(外部が進めたかの検出用)
        }

        private InPlaceState _inPlace;

        // その場再生を表す擬似ハンドル(Manager の台帳には存在しない。Manager に渡しても警告なしで無効扱い)。
        public static readonly Handle<VfxMarker> InPlaceHandle = Handle<VfxMarker>.Sentinel(0);

        public VfxManager Manager { get; }

        // 再生速度(0.1x-2x)。EditMode では手動 Simulate の dt に乗算し、PlayMode 中は
        // 実 Manager の SetSpeed(simulationSpeed)で反映する(二重適用しない)。
        public float Speed
        {
            get => _speed;
            set
            {
                _speed = Mathf.Clamp(value, 0.1f, 2f);
                if (Application.isPlaying)
                {
                    foreach (var (handle, _, _) in _active)
                    {
                        Manager.SetSpeed(handle, _speed);
                    }
                }
            }
        }

        public bool HasActive => _active.Count > 0 || _inPlace.Active;

        // 対象 Prefab 自身のプレハブモードが開いている(= Play がその場再生になる)か。
        public bool IsInPlaceTarget(VfxData data)
        {
            var stage = CurrentPrefabStage;
            return stage != null && data != null && data.Prefab != null && stage.prefabContentsRoot != null &&
                   stage.assetPath == AssetDatabase.GetAssetPath(data.Prefab);
        }

        // プレハブモードで開いているステージ(無ければ null)。スポーン先のシーンを決める。
        public PrefabStage CurrentPrefabStage => PrefabStageUtility.GetCurrentPrefabStage();

        // スポーン物をまとめる親(遅延生成。シーン切替で消えたら作り直す)。
        public GameObject PreviewRoot
        {
            get
            {
                if (_previewRoot == null)
                {
                    _previewRoot = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
                    // プレハブモード中はステージのシーンへ移す(通常時は現在のシーンのまま)。
                    StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
                }

                return _previewRoot;
            }
        }

        public SceneVfxPreviewDriver()
        {
            Manager = new VfxManager(_pool, new AssetRegistry(new NullAssetLoader()));
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
        }

        public Handle<VfxMarker> Play(VfxData data, Transform attach = null)
        {
            if (data == null)
            {
                return Handle<VfxMarker>.Invalid;
            }

            if (IsInPlaceTarget(data))
            {
                return PlayInPlace(data, CurrentPrefabStage.prefabContentsRoot);
            }

            var handle = Manager.SpawnData(data, contextRoot: attach);
            var go = Manager.GetGameObject(handle);
            if (go != null)
            {
                go.hideFlags = HideFlags.DontSave;
                // VfxManager は Rent 直後に SetParent(null) するため、ここで毎回まとめ直す(ワールド姿勢は維持)。
                go.transform.SetParent(PreviewRoot.transform, true);
                _spawnedRoots.Add(go);
                _active.Add((handle, go.GetComponentsInChildren<ParticleSystem>(true), data));

                if (Application.isPlaying)
                {
                    Manager.SetSpeed(handle, _speed);
                }
            }

            return handle;
        }

        public bool IsPlaying(Handle<VfxMarker> handle)
            => handle == InPlaceHandle ? _inPlace.Active : Manager.IsPlaying(handle);

        public void Stop(Handle<VfxMarker> handle)
        {
            if (handle == InPlaceHandle)
            {
                StopInPlace(clear: false);
                return;
            }

            Manager.Stop(handle);
        }

        public void Kill(Handle<VfxMarker> handle)
        {
            if (handle == InPlaceHandle)
            {
                StopInPlace(clear: true);
                return;
            }

            Manager.Kill(handle);
        }

        public void StopAll()
        {
            StopInPlace(clear: true);
            Manager.StopAll(StopReason.Manual);
        }

        // ── プレハブモードのその場再生 ──

        private Handle<VfxMarker> PlayInPlace(VfxData data, GameObject root)
        {
            StopInPlace(clear: true);

            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                // 配列に子も個別に入るため withChildren=false で二重適用を避ける。
                ps.Clear(false);
                ps.Simulate(0f, false, true);
                ps.Play(false);
            }

            _inPlace = new InPlaceState { Active = true, Data = data, Root = root, Systems = systems };
            RecordTimeAfterOurStep();
            return InPlaceHandle;
        }

        private void StopInPlace(bool clear)
        {
            if (!_inPlace.Active)
            {
                return;
            }

            foreach (var ps in _inPlace.Systems)
            {
                if (ps != null)
                {
                    ps.Stop(false, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (clear)
            {
                _inPlace = default;
            }
            else
            {
                _inPlace.Stopping = true;
            }
        }

        // EditMode の手動 Simulate では ParticleSystem.IsAlive が「一時停止中」として true を返し続けるため、
        // 粒子数と再生位置から生死を判定する(PlayMode でも同じ式で問題ない)。
        //   stopping=false: 粒子が残っている / ループ中 / まだ duration 内 → 生存
        //   stopping=true : 放出は止めた後なので、粒子が残っている間だけ生存
        private static bool AnyAlive(ParticleSystem[] systems, bool stopping)
        {
            foreach (var ps in systems)
            {
                if (ps == null)
                {
                    continue;
                }

                if (ps.particleCount > 0)
                {
                    return true;
                }

                if (stopping)
                {
                    continue;
                }

                var main = ps.main;
                if (main.loop || ps.time < main.duration - 1e-4f)
                {
                    return true;
                }
            }

            return false;
        }

        // ParticleSystem を Inspector で選択中は Unity 標準のプレビュー(Particle Effect パネル)が同じ実体を
        // 進めることがある。二重に進めると速度が倍になるため、「前回自分が進めた後の時刻」から
        // 外部に動かされていたらこのフレームは進めない(選択状態ではなく実際の時刻変化で判定する)。
        private bool IsUnityPreviewDriving()
        {
            var first = _inPlace.Systems.Length > 0 ? _inPlace.Systems[0] : null;
            return first != null && !Mathf.Approximately(first.time, _inPlace.TimeAfterOurStep);
        }

        private void RecordTimeAfterOurStep()
        {
            var first = _inPlace.Systems.Length > 0 ? _inPlace.Systems[0] : null;
            _inPlace.TimeAfterOurStep = first != null ? first.time : 0f;
        }

        private void TickInPlace(float dt)
        {
            if (!_inPlace.Active)
            {
                return;
            }

            if (_inPlace.Root == null)
            {
                _inPlace = default;
                return;
            }

            if (!Application.isPlaying)
            {
                if (!IsUnityPreviewDriving())
                {
                    EditModeParticleStepper.Step(_inPlace.Systems, dt * _speed);
                }

                RecordTimeAfterOurStep();
            }

            _inPlace.Elapsed += dt * _speed;

            if (_inPlace.Stopping)
            {
                if (!AnyAlive(_inPlace.Systems, stopping: true))
                {
                    _inPlace = default;
                }

                return;
            }

            switch (_inPlace.Data != null ? _inPlace.Data.LifeMode : VfxLifeMode.Loop)
            {
                case VfxLifeMode.Loop:
                    break;
                case VfxLifeMode.Duration:
                    if (_inPlace.Elapsed >= _inPlace.Data.Duration)
                    {
                        StopInPlace(clear: false);
                    }

                    break;
                default:
                    if (!AnyAlive(_inPlace.Systems, stopping: false))
                    {
                        _inPlace = default;
                    }

                    break;
            }
        }

        // Data.Anchor の編集内容を再生中の全 Instance に即時反映する(Path/Space の変更は再スポーンが必要)。
        public void ReapplyAnchor(Handle<VfxMarker> handle) => Manager.ReapplyAnchor(handle);

        public void ReapplyAnchorToAll()
        {
            foreach (var (handle, _, _) in _active)
            {
                Manager.ReapplyAnchor(handle);
            }
        }

        private void EditorTick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            Tick(dt);
        }

        // テストからも直接呼べる公開 Tick(EditorApplication.update の実体)。
        public void Tick(float dt)
        {
            Manager.Tick(dt);
            TickInPlace(dt);

            var anyAlive = _inPlace.Active;
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var (handle, systems, data) = _active[i];
                if (!Manager.IsPlaying(handle))
                {
                    _active.RemoveAt(i);
                    continue;
                }

                anyAlive = true;

                // EditMode では ParticleSystem が自動シミュレーションされないため手動で進める。
                // PlayMode 中は Unity が自動で進めるので二重適用しない。
                if (!Application.isPlaying)
                {
                    EditModeParticleStepper.Step(systems, dt * _speed);

                    // 手動 Simulate 中は IsAlive が true のままになり Manager の OneShot 終了判定が効かない
                    // (リピート再生が始まらない)ため、粒子が尽きたらこちらで終わらせる。
                    if (data != null && data.LifeMode == VfxLifeMode.OneShot && !AnyAlive(systems, stopping: false))
                    {
                        Manager.Kill(handle);
                        _active.RemoveAt(i);
                        continue;
                    }
                }
            }

            if (anyAlive)
            {
                SceneView.RepaintAll();
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene current) => ResetForStageChange();

        private void OnPrefabStageChanged(PrefabStage stage) => ResetForStageChange();

        // シーン切替・プレハブモードの開閉で「スポーン先のシーン」が変わるので、台帳をリセットして
        // 次の Play からやり直す。旧シーンに属していたスポーン物はシーンごと破棄済み(null)だが、
        // メインシーンで再生中にプレハブモードを開いた場合などは生き残っているため、ここで明示的に消す。
        private void ResetForStageChange()
        {
            // その場再生の実体はステージ側のもの。粒子だけ消して手を離す。
            StopInPlace(clear: true);
            Manager.StopAll(StopReason.SceneUnload);
            _active.Clear();
            DestroySpawnedObjects();
        }

        private void DestroySpawnedObjects()
        {
            foreach (var go in _spawnedRoots)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawnedRoots.Clear();

            if (_previewRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;
        }

        public void Dispose()
        {
            EditorApplication.update -= EditorTick;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            StopAll(); // その場再生も粒子を消して解放する
            _active.Clear();

            // プール返却済みの非アクティブ実体も含め、シーンに残さず確実に消す。
            DestroySpawnedObjects();
        }
    }
}
