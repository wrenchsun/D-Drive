using System;
using System.Collections.Generic;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Model;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Preview
{
    // [09_editor_tools.md] §2 — 各専用エディタが共有するプレビュー基盤。
    // 専用プレビューシーン(NewPreviewScene)内で実 Manager 群を初期化し、
    // EditMode 中は EditorApplication.update から Tick(dt) を回す(ADR-4:
    // Editor 専用の再生経路を作らず、実行時と同じ AudioManager/BgmManager を通す)。
    //
    // Phase 1 の範囲: Audio(SE/BGM)の再生・停止・ループ・速度(=ピッチ)。
    // 背景/ライト切替・比較表示・スクリーンショットは視覚系種別の実装(Phase 2+)で拡張する。
    public sealed class PreviewService : IDisposable
    {
        private Scene _previewScene;
        private GameObject _root;
        private GameObject _seSourceTemplate;
        private PoolService _pool;

        private double _lastTickTime;
        private float _speed = 1f;
        private bool _initialized;

        private readonly List<Handle<SeMarker>> _activeSeHandles = new();
        private readonly List<UnityEngine.Object> _dataCopies = new();

        // EditMode では ParticleSystem が自動シミュレーションされない(再生時間が進むのは PlayMode のみ)。
        // そのため試聴中の VFX は Tick で手動 Simulate して進める。GetComponentsInChildren を毎 Tick
        // 呼ばないよう、Spawn 時に ParticleSystem 配列をキャッシュする。
        private readonly List<(Handle<VfxMarker> handle, ParticleSystem[] systems)> _activeVfx = new();

        public AudioManager AudioManager { get; private set; }
        public BgmManager BgmManager { get; private set; }
        public VfxManager VfxManager { get; private set; }
        public ModelsManager ModelsManager { get; private set; }

        // 試聴の基準になる AudioListener。開いているシーンに存在すればそれを、
        // 無ければプレビューシーン内に生成したものを指す(3D 減衰・パンの基準)。
        public Transform ListenerTransform { get; private set; }

        // VFX/Model の視覚プレビュー用([04_vfx.md] §5 / [05_model_animation.md] A-4)。
        // Audio と同じプレビューシーンにカメラ+ライトを置き、任意モデルを Anchor 確認用に読み込める。
        public Camera PreviewCamera { get; private set; }
        public Light PreviewLight { get; private set; }
        public GameObject PreviewModelRoot { get; private set; }

        private RenderTexture _renderTexture;

        public bool IsInitialized => _initialized;

        // 速度 0.1x–2x。Audio では再生ピッチとして適用する(再生中の SE にも即時反映)。
        public float Speed
        {
            get => _speed;
            set
            {
                _speed = Mathf.Clamp(value, 0.1f, 2f);
                ApplySpeedToActiveHandles();
            }
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _previewScene = EditorSceneManager.NewPreviewScene();

            _root = new GameObject("DDrivePreviewRoot");
            SceneManager.MoveGameObjectToScene(_root, _previewScene);

            _seSourceTemplate = new GameObject("SeSourceTemplate");
            _seSourceTemplate.transform.SetParent(_root.transform);
            _seSourceTemplate.AddComponent<AudioSource>();
            _seSourceTemplate.SetActive(false);

            _pool = new PoolService();
            _pool.SetInstanceParent(_root.transform);

            // AnchorData を登録済みの Registry(SeData.AnchorId / AnchorEditor の試し出しを実 AnchorChain で解決する)。
            var registry = EditorAnchorRegistry.Build();
            AudioManager = new AudioManager(_pool, registry, _seSourceTemplate);

            var bgmChannelA = CreateBgmChannel("BgmChannelA");
            var bgmChannelB = CreateBgmChannel("BgmChannelB");
            BgmManager = new BgmManager(registry, bgmChannelA, bgmChannelB);

            VfxManager = new VfxManager(_pool, registry);
            ModelsManager = new ModelsManager(_pool, registry);

            // _root は既に _previewScene に属しているため、親にぶら下げるだけでシーンも引き継ぐ
            // (MoveGameObjectToScene はルートオブジェクトにしか使えず、親付け後に呼ぶと例外になる)。
            var cameraGo = new GameObject("PreviewCamera");
            cameraGo.transform.SetParent(_root.transform);
            PreviewCamera = cameraGo.AddComponent<Camera>();
            PreviewCamera.enabled = false; // 手動 Render() のみで使う(毎フレーム自動描画しない)
            PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            PreviewCamera.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            PreviewCamera.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -4f), Quaternion.identity);
            PreviewCamera.transform.LookAt(Vector3.up * 1f);

            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(_root.transform);
            PreviewLight = lightGo.AddComponent<Light>();
            PreviewLight.type = LightType.Directional;
            PreviewLight.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var existingListener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
            if (existingListener != null)
            {
                ListenerTransform = existingListener.transform;
            }
            else
            {
                var listenerGo = new GameObject("PreviewListener");
                listenerGo.transform.SetParent(_root.transform);
                listenerGo.AddComponent<AudioListener>();
                ListenerTransform = listenerGo.transform;
            }

            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
            _initialized = true;
        }

        public Handle<SeMarker> PlaySe(SeData data, bool forceLoop = false)
        {
            if (!_initialized || data == null)
            {
                return Handle<SeMarker>.Invalid;
            }

            var playData = data;

            // ループ試聴はアセット本体を書き換えず、ランタイムコピーで行う(非破壊)。
            // また、ループ音を重ね掛けするとコピーと再生が無限に積み上がるため、
            // ループ試聴の開始時は前の試聴を止めてから鳴らす。
            if (forceLoop && !data.Loop)
            {
                StopAll();
                var copy = UnityEngine.Object.Instantiate(data);
                copy.Loop = true;
                _dataCopies.Add(copy);
                playData = copy;
            }

            var handle = AudioManager.PlaySeData(playData);
            if (AudioManager.IsPlaying(handle))
            {
                // 試聴の再現性を優先し、ランダムピッチではなく速度そのままを適用する。
                AudioManager.SetPitch(handle, _speed);
                _activeSeHandles.Add(handle);
            }

            return handle;
        }

        // AnchorEditor の試し出し: Anchor アセットを明示し、スポーン先(メインシーン側の Transform でも可)を基準に鳴らす。
        public Handle<SeMarker> PlaySe(SeData data, Transform contextRoot, DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker> anchor)
        {
            if (!_initialized || data == null)
            {
                return Handle<SeMarker>.Invalid;
            }

            var handle = AudioManager.PlaySeData(data, contextRoot: contextRoot, anchorOverride: anchor);
            if (AudioManager.IsPlaying(handle))
            {
                AudioManager.SetPitch(handle, _speed);
                _activeSeHandles.Add(handle);
            }

            return handle;
        }

        // 配置セットのプレビュー用: 合成済みの姿勢で鳴らす([22] §3.7)。
        public Handle<SeMarker> PlaySe(SeData data, Transform contextRoot, in DDrive.Runtime.Anchoring.AnchorSpawnSpec spec)
        {
            if (!_initialized || data == null)
            {
                return Handle<SeMarker>.Invalid;
            }

            var handle = AudioManager.PlaySeData(data, spec, contextRoot);
            if (AudioManager.IsPlaying(handle))
            {
                AudioManager.SetPitch(handle, _speed);
                _activeSeHandles.Add(handle);
            }

            return handle;
        }

        public void PlayBgm(BgmData data)
        {
            if (_initialized && data != null)
            {
                BgmManager.PlayBgmData(data);
            }
        }

        // VfxEditor(2-4)向け。VfxData はカタログ未登録の編集中アセットであることが多いため、
        // ID 解決を経由せず直接 SpawnData する(PlaySeData と同じ考え方)。
        public Handle<VfxMarker> PlayVfx(VfxData data, Transform attach = null)
        {
            if (!_initialized || data == null)
            {
                return Handle<VfxMarker>.Invalid;
            }

            var handle = VfxManager.SpawnData(data, contextRoot: attach);
            var go = VfxManager.GetGameObject(handle);
            if (go != null)
            {
                _activeVfx.Add((handle, go.GetComponentsInChildren<ParticleSystem>(true)));
            }

            return handle;
        }

        public void StopVfx(Handle<VfxMarker> handle) => VfxManager?.Stop(handle);

        public void StopAllVfx() => VfxManager?.StopAll(Foundation.Manager.StopReason.Manual);

        // ModelEditor(2-6)向け。
        public Handle<ModelMarker> SpawnModel(ModelData data, Vector3 pos, Quaternion rot)
            => _initialized && data != null ? ModelsManager.SpawnData(data, pos, rot) : Handle<ModelMarker>.Invalid;

        public void DespawnModel(Handle<ModelMarker> handle) => ModelsManager?.Despawn(handle);

        public void StopAllModels() => ModelsManager?.StopAll(Foundation.Manager.StopReason.Manual);

        // Anchor のボーン確認(2-4)・ターンテーブル表示(2-6)用に、任意の Prefab をプレビューシーンへ
        // 読み込む。既存のものは破棄してから差し替える(比較表示は複数呼び出しで切り替える運用)。
        public GameObject SetPreviewModel(GameObject prefab)
        {
            ClearPreviewModel();

            if (prefab == null)
            {
                return null;
            }

            PreviewModelRoot = UnityEngine.Object.Instantiate(prefab, _root.transform);
            return PreviewModelRoot;
        }

        public void ClearPreviewModel()
        {
            if (PreviewModelRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(PreviewModelRoot);
                PreviewModelRoot = null;
            }
        }

        public void SetBackgroundColor(Color color)
        {
            if (PreviewCamera != null)
            {
                PreviewCamera.backgroundColor = color;
            }
        }

        public void SetLightIntensity(float intensity)
        {
            if (PreviewLight != null)
            {
                PreviewLight.intensity = intensity;
            }
        }

        // 指定サイズで PreviewCamera を手動レンダリングし、結果の RenderTexture を返す
        // (Camera.enabled=false のため、Repaint 時にここから明示的に呼ぶ想定)。
        public RenderTexture Render(int width, int height)
        {
            if (PreviewCamera == null || width <= 0 || height <= 0)
            {
                return null;
            }

            if (_renderTexture == null || _renderTexture.width != width || _renderTexture.height != height)
            {
                if (_renderTexture != null)
                {
                    _renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(_renderTexture);
                }

                _renderTexture = new RenderTexture(width, height, 16) { name = "DDrivePreviewRT" };
            }

            PreviewCamera.targetTexture = _renderTexture;
            PreviewCamera.aspect = (float)width / height;
            PreviewCamera.Render();
            PreviewCamera.targetTexture = null;

            return _renderTexture;
        }

        public void StopAll()
        {
            if (!_initialized)
            {
                return;
            }

            AudioManager.StopAll(Foundation.Manager.StopReason.Manual);
            BgmManager.StopBgm(0f);
            _activeSeHandles.Clear();
            ReleaseDataCopies();
        }

        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            EditorApplication.update -= EditorTick;
            StopAll();
            StopAllVfx();
            StopAllModels();
            _activeVfx.Clear();
            ReleaseDataCopies();
            ClearPreviewModel();

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(_renderTexture);
                _renderTexture = null;
            }

            if (_previewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(_previewScene);
            }

            _initialized = false;
        }

        // テストからも直接呼べる公開 Tick(EditorApplication.update の実体)。
        public void Tick(float dt)
        {
            if (!_initialized)
            {
                return;
            }

            AudioManager.Tick(dt);
            BgmManager.Tick(dt);
            VfxManager.Tick(dt);
            ModelsManager.Tick(dt);
            AdvanceVfxSimulation(dt);

            for (var i = _activeSeHandles.Count - 1; i >= 0; i--)
            {
                if (!AudioManager.IsPlaying(_activeSeHandles[i]))
                {
                    _activeSeHandles.RemoveAt(i);
                }
            }
        }

        private void EditorTick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            Tick(dt);
        }

        // 試聴中の VFX が1つでもあるか(エディタウィンドウが「再生中は毎フレーム Repaint する」判定に使う)。
        public bool HasActiveVfx => _activeVfx.Count > 0;

        private void AdvanceVfxSimulation(float dt)
        {
            for (var i = _activeVfx.Count - 1; i >= 0; i--)
            {
                var (handle, systems) = _activeVfx[i];
                if (!VfxManager.IsPlaying(handle))
                {
                    _activeVfx.RemoveAt(i);
                    continue;
                }

                // PlayMode 中は Unity が自動でシミュレーションするため二重に進めない。
                if (Application.isPlaying)
                {
                    continue;
                }

                EditModeParticleStepper.Step(systems, dt * _speed);
            }
        }

        private AudioSource CreateBgmChannel(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform);
            return go.AddComponent<AudioSource>();
        }

        private void ApplySpeedToActiveHandles()
        {
            foreach (var handle in _activeSeHandles)
            {
                AudioManager.SetPitch(handle, _speed);
            }
        }

        private void ReleaseDataCopies()
        {
            foreach (var copy in _dataCopies)
            {
                if (copy != null)
                {
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }

            _dataCopies.Clear();
        }
    }
}
