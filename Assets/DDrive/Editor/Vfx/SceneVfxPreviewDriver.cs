using System;
using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Vfx
{
    // [04_vfx.md] §5(2026-07-28 改定) — VFX プレビューは独自ビューポートではなく
    // 「開いているシーンに直接スポーンして SceneView で確認する」方式で行う。
    // ライティング・ポストプロセス・Skybox 等はそのシーンの設定がそのまま適用されるため、
    // 確認専用シーンを用意して本番同等の環境で調整できる。
    //
    // スポーンした GameObject は HideFlags.DontSave を付けてシーンへ保存されないようにし、
    // Dispose(ウィンドウを閉じる/再コンパイル)時に必ず破棄する。
    public sealed class SceneVfxPreviewDriver : IDisposable
    {
        private readonly PoolService _pool = new();
        private readonly List<(Handle<VfxMarker> handle, ParticleSystem[] systems)> _active = new();
        private readonly HashSet<GameObject> _spawnedRoots = new();
        private double _lastTickTime;

        public VfxManager Manager { get; }

        // 再生速度(0.1x-2x)。EditMode では手動 Simulate の dt に乗算して適用する。
        public float Speed = 1f;

        public bool HasActive => _active.Count > 0;

        public SceneVfxPreviewDriver()
        {
            Manager = new VfxManager(_pool, new AssetRegistry(new NullAssetLoader()));
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
        }

        public Handle<VfxMarker> Play(VfxData data, Transform attach = null)
        {
            if (data == null)
            {
                return Handle<VfxMarker>.Invalid;
            }

            var handle = Manager.SpawnData(data, contextRoot: attach);
            var go = Manager.GetGameObject(handle);
            if (go != null)
            {
                go.hideFlags = HideFlags.DontSave;
                _spawnedRoots.Add(go);
                _active.Add((handle, go.GetComponentsInChildren<ParticleSystem>(true)));
            }

            return handle;
        }

        public void Stop(Handle<VfxMarker> handle) => Manager.Stop(handle);

        public void StopAll() => Manager.StopAll(StopReason.Manual);

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

            var anyAlive = false;
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var (handle, systems) = _active[i];
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
                    foreach (var ps in systems)
                    {
                        if (ps != null)
                        {
                            ps.Simulate(dt * Speed, false, false, false);
                        }
                    }
                }
            }

            if (anyAlive)
            {
                SceneView.RepaintAll();
            }
        }

        public void Dispose()
        {
            EditorApplication.update -= EditorTick;
            StopAll();
            _active.Clear();

            // プール返却済みの非アクティブ実体も含め、シーンに残さず確実に消す。
            foreach (var go in _spawnedRoots)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawnedRoots.Clear();
        }
    }
}
