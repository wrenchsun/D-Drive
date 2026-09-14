using System;
using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DDrive.Editor.Anim
{
    // [05_model_animation.md] B-4(2026-09-09 追加) — AnimEditor の「シーン(SceneView)で再生」。
    // ウィンドウ内ビューポートではなく、開いているシーン / プレハブモードのモデルを実 AnimManager で
    // その場で動かし、SceneView で確認する(VFX の SceneVfxPreviewDriver と同じ方式、ADR-4)。
    //
    // - 対象: シーン上の Animator(借用)か、確認用 ModelData をこのドライバがシーンへ配置したもの(自前)
    // - 借用した Animator は再生前に全 Transform / BlendShape ウェイトをスナップショットし、Stop / 対象解除 /
    //   ステージ切替 / Prefab 保存の直前に元のポーズへ戻す(シーンや Prefab に再生中のポーズを残さない)
    // - AnimManager が付ける AnimatorProxy はこちらで付けた分だけ DontSave にして解除時に外す
    // - イベント(PlayAsset)の SE / VFX は AssetEventDispatcher 経由で、VFX は SceneVfxPreviewDriver に
    //   引き取らせる(DontSave・まとめ用ルート・EditMode の手動 Simulate)。SE は自前の AudioManager
    // - 配置物は "[D-Drive] Anim Preview" ルート(DontSave)の下。Dispose / ステージ切替で必ず破棄する
    public sealed class SceneAnimPreviewDriver : IDisposable
    {
        public const string PreviewRootName = "[D-Drive] Anim Preview";

        private struct TransformSnapshot
        {
            public Transform Target;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private struct BlendSnapshot
        {
            public SkinnedMeshRenderer Renderer;
            public float[] Weights;
        }

        private readonly List<TransformSnapshot> _pose = new();
        private readonly List<BlendSnapshot> _blend = new();

        private PoolService _pool;
        private GameObject _root;
        private AssetEventDispatcher _dispatcher;
        private AnchorGroupPlayer _groups;
        // ModelData.Slots のマテリアルを確認用シーンでも実適用するための実 MaterialManager(ランタイムの
        // DDriveRuntimeBootstrap と同じ配線。2026-09-12: 未接続で「ID 保存のみ」になっていた抜けを修正)。
        private MaterialManager _materials;
        private readonly List<Handle<AnchorGroupMarker>> _groupHandles = new();
        private readonly List<Handle<VfxMarker>> _vfxScratch = new();
        private Handle<ModelMarker> _spawnedModel = Handle<ModelMarker>.Invalid;
        private readonly List<Handle<ModelMarker>> _extraModels = new(); // ModelEditor の並列表示(対象とは別に配置)
        private AnimatorProxy _addedProxy;
        private bool _proxyPreexisted;
        private double _lastTickTime;
        private float _speed = 1f;

        public AssetRegistry Registry { get; }

        // 再生・イベント発火の実体。ウィンドウはこの Events を購読してログを出す(ステージ切替でも作り直さない)。
        public AnimManager Manager { get; }

        // イベント経由の VFX をシーンに出す(自前で Tick する)。
        public SceneVfxPreviewDriver Vfx { get; }

        public AudioManager Audio { get; private set; }
        public ModelsManager Models { get; private set; }

        // 現在の対象 Animator(借用 or 自前配置)。無ければ null。
        public Animator Current { get; private set; }

        // Current がこのドライバの配置した確認用モデルか(true なら解除時に Despawn、false なら元ポーズへ復元)。
        public bool OwnsCurrent => Models != null && Models.IsValid(_spawnedModel);

        // 自前で配置した確認用モデルの Handle / ルート(ModelEditor のターンテーブルや Material 反映に使う)。
        public Handle<ModelMarker> SpawnedModelHandle => _spawnedModel;

        public GameObject CurrentRoot => OwnsCurrent ? Models.GetGameObject(_spawnedModel) : (Current != null ? Current.gameObject : null);

        public bool HasActive => Manager.ActiveCount > 0 || Vfx.HasActive;

        public GameObject PreviewRoot => _root;

        public float Speed
        {
            get => _speed;
            set
            {
                _speed = Mathf.Clamp(value, 0.1f, 2f);
                Vfx.Speed = _speed;
            }
        }

        // registry: 省略時はプロジェクト内の Anchor / VFX / SE を登録した EditorAnchorRegistry(テストからは差し替える)。
        public SceneAnimPreviewDriver(AssetRegistry registry = null)
        {
            Registry = registry ?? EditorAnchorRegistry.Build();
            Manager = new AnimManager(Registry);
            Vfx = new SceneVfxPreviewDriver(Registry);
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            PrefabStage.prefabSaving += OnPrefabSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        // Hierarchy の選択(その親/子)の Animator、無ければプレハブモードのルート配下の Animator。Project 内のアセットは対象外。
        public static Animator SuggestTarget()
        {
            var selected = Selection.activeGameObject;
            if (selected != null && !EditorUtility.IsPersistent(selected))
            {
                var animator = selected.GetComponentInParent<Animator>();
                if (animator == null)
                {
                    animator = selected.GetComponentInChildren<Animator>(true);
                }

                if (animator != null)
                {
                    return animator;
                }
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
            {
                return stage.prefabContentsRoot.GetComponentInChildren<Animator>(true);
            }

            return null;
        }

        // シーン上の Animator を対象にする(借用。再生前のポーズを記録する)。
        public void SetTarget(Animator target)
        {
            if (target == Current)
            {
                return;
            }

            ReleaseTarget();
            if (target == null)
            {
                return;
            }

            if (EditorUtility.IsPersistent(target))
            {
                Debug.LogWarning("[DDrive] シーン(またはプレハブモード)上の Animator を指定してください。Project 内の Prefab アセットはその場で動かせません。");
                return;
            }

            Snapshot(target);
            Current = target;
        }

        // 確認用モデルを開いているシーン / プレハブモードに配置して対象にする(保存されない)。
        // requireAnimator=false は Animator の無い静的モデルも配置する用途(ModelEditor)。
        public Animator SpawnModel(ModelData model, Vector3 position, Quaternion rotation, bool requireAnimator = true)
        {
            ReleaseTarget();
            if (model == null || model.Prefab == null)
            {
                return null;
            }

            EnsureManagers();
            _spawnedModel = Models.SpawnData(model, position, rotation);
            var go = Models.GetGameObject(_spawnedModel);
            if (go == null)
            {
                _spawnedModel = Handle<ModelMarker>.Invalid;
                return null;
            }

            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(_root.transform, true);
            Current = Models.GetAnimator(_spawnedModel);
            if (Current == null && requireAnimator)
            {
                Debug.LogWarning($"[DDrive] Model '{model.DisplayName ?? model.name}' の Prefab に Animator がありません。再生できません。");
            }

            SceneView.RepaintAll();
            return Current;
        }

        // 対象とは別にもう 1 体配置する(並列比較用)。ReleaseTarget / ステージ切替 / Dispose でまとめて撤去する。
        public Handle<ModelMarker> SpawnExtraModel(ModelData model, Vector3 position, Quaternion rotation)
        {
            if (model == null || model.Prefab == null)
            {
                return Handle<ModelMarker>.Invalid;
            }

            EnsureManagers();
            var handle = Models.SpawnData(model, position, rotation);
            var go = Models.GetGameObject(handle);
            if (go == null)
            {
                return Handle<ModelMarker>.Invalid;
            }

            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(_root.transform, true);
            _extraModels.Add(handle);
            SceneView.RepaintAll();
            return handle;
        }

        public void DespawnExtraModel(Handle<ModelMarker> handle)
        {
            if (Models != null && Models.IsValid(handle))
            {
                Models.Despawn(handle);
            }

            _extraModels.Remove(handle);
            SceneView.RepaintAll();
        }

        // イベント一覧の ▶(単体試聴)。対象があればその位置(contextRoot)から出す。
        public void PreviewSe(SeData data)
        {
            if (data == null)
            {
                return;
            }

            EnsureManagers();
            Audio.PlaySeData(data, contextRoot: Current != null ? Current.transform : null);
        }

        public void PreviewVfx(VfxData data)
        {
            if (data == null)
            {
                return;
            }

            Vfx.Play(data, Current != null ? Current.transform : null);
        }

        public void PreviewGroup(AnchorGroupData data)
        {
            if (data == null)
            {
                return;
            }

            EnsureManagers();
            var handle = _groups.PlayData(data, Current != null ? Current.transform : null);
            if (_groups.IsPlaying(handle))
            {
                _groupHandles.Add(handle);
            }

            AdoptGroupVfx();
        }

        // 配置セットが(ディレイ後も含めて)出した VFX をシーンの台帳へ引き取る。
        private void AdoptGroupVfx()
        {
            if (_groups == null)
            {
                return;
            }

            for (var i = _groupHandles.Count - 1; i >= 0; i--)
            {
                if (!_groups.IsPlaying(_groupHandles[i]))
                {
                    _groupHandles.RemoveAt(i);
                    continue;
                }

                _vfxScratch.Clear();
                _groups.CollectVfxHandles(_groupHandles[i], _vfxScratch);
                foreach (var h in _vfxScratch)
                {
                    Vfx.Adopt(h);
                }
            }
        }

        public Handle<AnimMarker> Play(AnimData data, float fade = -1f) => Play(data, Current, fade);

        public Handle<AnimMarker> Play(AnimData data, Animator target, float fade = -1f)
        {
            if (data == null || target == null)
            {
                return Handle<AnimMarker>.Invalid;
            }

            if (target != Current)
            {
                SetTarget(target);
            }

            if (Current == null)
            {
                return Handle<AnimMarker>.Invalid;
            }

            EnsureManagers();
            // Play 系の入口で一時停止を一括解除する(前回の ⏸ が残って Tick が止まり続けないように)。
            ClearPause();
            var handle = Manager.PlayData(data, Current, fade);
            TrackProxy();
            SceneView.RepaintAll();
            return handle;
        }

        public void Seek(Handle<AnimMarker> handle, float normalizedTime)
        {
            Manager.Seek(handle, normalizedTime);
            SceneView.RepaintAll();
        }

        // 再生を止めて、借用中の対象なら元のポーズに戻す(対象自体は保持する)。
        // 再生を止める。ポーズはその瞬間のまま残す(2026-09-10: 停止で初期ポーズに戻ると確認しづらいため)。
        // 元のポーズへ戻すのは RestorePoseNow / ReleaseTarget / ステージ切替 / Prefab 保存の直前。
        // イベントで出た SE / VFX / 配置セットを全部消す(最初から再生し直すとき・停止のとき)。
        public void StopSpawned()
        {
            Audio?.StopAll(StopReason.Manual);
            _groups?.StopAll();
            _groupHandles.Clear();
            Vfx.StopAll();
        }

        public void Stop()
        {
            Manager.StopAll(StopReason.Manual);
            StopSpawned();
            ClearPause();
            SceneView.RepaintAll();
        }

        // 借用中の対象を再生前のポーズに戻す(自前配置のモデルは何もしない)。
        public void RestorePoseNow()
        {
            RestorePose();
            SceneView.RepaintAll();
        }

        // プレビュー全体の一時停止(2026-09-10): 対象の Anim だけでなく、同時再生中の他レイヤーの Anim、
        // イベントで出た SE / VFX / 配置セットのディレイもまとめて止める。Seek は一時停止中でも効く。
        public bool IsPaused { get; private set; }

        public void SetPaused(Handle<AnimMarker> handle, bool paused)
        {
            if (!paused)
            {
                ClearPause();
                SceneView.RepaintAll();
                return;
            }

            // 無効 Handle(終了済み / 未再生)で一時停止にすると解除経路が無いまま Tick が止まるので弾く。
            if (!Manager.IsPlaying(handle))
            {
                return;
            }

            Manager.SetPausedAll(true);
            IsPaused = true;
            Audio?.SetPausedAll(true);
            Vfx.Paused = true;
            SceneView.RepaintAll();
        }

        // 一時停止を全経路でまとめて解除する(Play / Stop / 対象解除 / ステージ切替 / Dispose)。
        private void ClearPause()
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            Manager.SetPausedAll(false);
            Audio?.SetPausedAll(false);
            Vfx.Paused = false;
        }

        // 対象を手放す: 借用なら復元 + 付けた Proxy を外す、自前配置なら Despawn。
        public void ReleaseTarget()
        {
            Manager.StopAll(StopReason.Manual);
            ClearPause();
            RestorePose();
            if (_addedProxy != null)
            {
                UnityEngine.Object.DestroyImmediate(_addedProxy);
            }

            _addedProxy = null;
            _proxyPreexisted = false;
            if (Models != null && Models.IsValid(_spawnedModel))
            {
                Models.Despawn(_spawnedModel);
            }

            _spawnedModel = Handle<ModelMarker>.Invalid;
            if (Models != null)
            {
                foreach (var extra in _extraModels)
                {
                    if (Models.IsValid(extra))
                    {
                        Models.Despawn(extra);
                    }
                }
            }

            _extraModels.Clear();
            _pose.Clear();
            _blend.Clear();
            Current = null;
            SceneView.RepaintAll();
        }

        public void Tick(float dt)
        {
            if (IsPaused)
            {
                return; // 時間・イベント・ディレイ・SE 終了判定を全部止める
            }

            Manager.Tick(dt * _speed);
            Audio?.Tick(dt);
            Models?.Tick(dt);
            if (_groups != null)
            {
                _groups.Tick();
                AdoptGroupVfx();
            }
            if (Manager.ActiveCount > 0)
            {
                SceneView.RepaintAll();
            }
        }

        public void Dispose()
        {
            EditorApplication.update -= EditorTick;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            PrefabStage.prefabSaving -= OnPrefabSaving;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            ResetForStageChange();
            Vfx.Dispose();
        }

        // ── 内部 ──

        private void EnsureManagers()
        {
            if (_root != null && Audio != null)
            {
                return;
            }

            _root = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            StageUtility.PlaceGameObjectInCurrentStage(_root);

            // AudioManager 生成の定型手順(PoolService + テンプレ AudioSource + AudioListener 確認)は
            // ScenePresentationPreviewDriver(5-4)と共用する EditorAudioFactory に切り出した(2026-09-14)。
            _pool = new PoolService();
            _pool.SetInstanceParent(_root.transform);
            Audio = EditorAudioFactory.Create(_pool, _root.transform, Registry);
            _materials = new MaterialManager(Registry);
            Models = new ModelsManager(_pool, Registry, Manager, _materials);

            _groups = new AnchorGroupPlayer(Registry, Vfx.Manager, Audio);
            _dispatcher?.Dispose();
            _dispatcher = new AssetEventDispatcher(Manager.Events, Registry, Audio, Vfx.Manager, Manager.GetContextTransform, _groups);
            _dispatcher.OnVfxSpawned += Vfx.Adopt;
            _dispatcher.OnGroupPlayed += OnGroupPlayed;
        }

        // イベント(PlayAsset=AnchorGroup)で出た配置セットを台帳に載せ、その VFX をシーンの台帳へ引き取る。
        private void OnGroupPlayed(Handle<AnchorGroupMarker> handle)
        {
            if (!_groupHandles.Contains(handle))
            {
                _groupHandles.Add(handle);
            }

            AdoptGroupVfx();
        }

        private void Snapshot(Animator target)
        {
            _pose.Clear();
            _blend.Clear();
            foreach (var t in target.GetComponentsInChildren<Transform>(true))
            {
                _pose.Add(new TransformSnapshot { Target = t, Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale });
            }

            foreach (var smr in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0)
                {
                    continue;
                }

                var weights = new float[smr.sharedMesh.blendShapeCount];
                for (var i = 0; i < weights.Length; i++)
                {
                    weights[i] = smr.GetBlendShapeWeight(i);
                }

                _blend.Add(new BlendSnapshot { Renderer = smr, Weights = weights });
            }

            _proxyPreexisted = target.GetComponent<AnimatorProxy>() != null;
        }

        private void RestorePose()
        {
            foreach (var s in _pose)
            {
                if (s.Target != null)
                {
                    s.Target.localPosition = s.Position;
                    s.Target.localRotation = s.Rotation;
                    s.Target.localScale = s.Scale;
                }
            }

            foreach (var b in _blend)
            {
                if (b.Renderer == null)
                {
                    continue;
                }

                for (var i = 0; i < b.Weights.Length; i++)
                {
                    b.Renderer.SetBlendShapeWeight(i, b.Weights[i]);
                }
            }
        }

        // AnimManager が付けた Proxy を「こちらで付けた」ものとして記録し、保存対象から外す。
        private void TrackProxy()
        {
            if (Current == null || OwnsCurrent || _proxyPreexisted || _addedProxy != null)
            {
                return;
            }

            var proxy = Current.GetComponent<AnimatorProxy>();
            if (proxy != null)
            {
                proxy.hideFlags = HideFlags.DontSave;
                _addedProxy = proxy;
            }
        }

        private void EditorTick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            Tick(dt);
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current) => ResetForStageChange();

        private void OnPrefabStageChanged(PrefabStage stage) => ResetForStageChange();

        // Prefab 保存の直前に元ポーズへ戻す(再生中のポーズが Prefab に書かれないように)。
        private void OnPrefabSaving(GameObject root)
        {
            Stop();
            RestorePose();
        }

        // シーン保存の直前に、借用中の対象を元ポーズへ戻す(停止後に残しているポーズや再生中のポーズがシーンに書かれないように)。
        // 再生中なら次の Tick で再サンプルされる。停止後のポーズ維持は保存で解除される(保存を優先)。
        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            if (Current == null || OwnsCurrent || Current.gameObject.scene != scene)
            {
                return;
            }

            RestorePose();
        }

        private void ResetForStageChange()
        {
            ReleaseTarget();
            ClearPause();
            Audio?.StopAll(StopReason.SceneUnload);
            _dispatcher?.Dispose();
            _dispatcher = null;
            _groups?.StopAll();
            _groups = null;
            _groupHandles.Clear();
            Audio = null;
            Models = null;
            _materials?.Clear(); // 共有 Material(生成物)を破棄。Renderer 側は _root ごと消える
            _materials = null;
            _pool = null;
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            _root = null;
        }
    }
}
