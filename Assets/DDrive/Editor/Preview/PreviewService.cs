using System;
using System.Collections.Generic;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
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

        public AudioManager AudioManager { get; private set; }
        public BgmManager BgmManager { get; private set; }

        // 試聴の基準になる AudioListener。開いているシーンに存在すればそれを、
        // 無ければプレビューシーン内に生成したものを指す(3D 減衰・パンの基準)。
        public Transform ListenerTransform { get; private set; }

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

            var registry = new AssetRegistry(new NullAssetLoader());
            AudioManager = new AudioManager(_pool, registry, _seSourceTemplate);

            var bgmChannelA = CreateBgmChannel("BgmChannelA");
            var bgmChannelB = CreateBgmChannel("BgmChannelB");
            BgmManager = new BgmManager(registry, bgmChannelA, bgmChannelB);

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

        public void PlayBgm(BgmData data)
        {
            if (_initialized && data != null)
            {
                BgmManager.PlayBgmData(data);
            }
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
            ReleaseDataCopies();

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
