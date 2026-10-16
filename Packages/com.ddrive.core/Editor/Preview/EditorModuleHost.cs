using System;
using System.Collections.Generic;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Extension;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Preview
{
    // [1014_extension_platform.md] §2.3(X-3) — Editor プレビューに IDDriveModule を乗せる口。
    // ModuleRegistry の module から Manager を作って(DDriveBootstrapContext.ForEditorPreview、IsEditorPreview = true)GameLoop に登録し、
    // Edit Mode で EditorApplication.update から Tick する。Dispose で StopAll → 逆順に Unbind → GameLoop から解除。
    // ランタイムの DDriveRuntimeBootstrap と同じ ModuleRunner を使うので、順序・例外時の扱い(警告 1 件 + その module を飛ばす)も同じ。
    // 既存の窓・Driver には乗せていない。持ち込み先の窓が自前の Manager 群を組むときに、この Host を一緒に持つ。
    public sealed class EditorModuleHost : IDisposable
    {
        private readonly ModuleRunner _runner;
        private double _lastTickTime;
        private bool _disposed;

        public GameLoop Loop { get; } = new();

        public DDriveBootstrapContext Context { get; }

        // module が作って Register した Manager(Start の順)。
        public IReadOnlyList<IAssetManager> Managers => _runner.Managers;

        // pool / registry は呼び出し側(プレビューの Manager 群)のものを渡す。pause / netBridge は省略可(省略時は新規 / null)。
        public EditorModuleHost(IAssetRegistry registry, IPoolService pool, PauseService pauseService = null, INetBridge netBridge = null)
        {
            Context = DDriveBootstrapContext.ForEditorPreview(registry, pool, Loop, pauseService, netBridge: netBridge);
            _runner = new ModuleRunner(ModuleRegistry.All);
            _runner.Start(Context);
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
        }

        // テストからも呼べる Tick(dt は秒)。
        public void Tick(float dt)
        {
            if (!_disposed)
            {
                Loop.Tick(dt);
            }
        }

        private void EditorTick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            Tick(dt);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            EditorApplication.update -= EditorTick;
            Loop.StopAll(StopReason.SceneUnload);
            _runner.Stop();
        }
    }
}
