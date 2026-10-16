using System;
using System.Collections.Generic;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Extension;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Preview
{
    // [1014_extension_platform.md] §2.3(X-3) — Editor プレビューに IDDriveModule を乗せる口。
    // ModuleRegistry の module から Manager を作って(DDriveBootstrapContext.ForEditorPreview、IsEditorPreview = true)GameLoop に登録し、
    // Edit Mode で EditorApplication.update から Tick する。Dispose で StopAll → GameLoop から解除。
    // ランタイムの DDriveRuntimeBootstrap と同じ ModuleRunner を使うので、順序・例外時の扱い(警告 1 件 + その module を飛ばす)も同じ。
    //
    // X-R-01: Host は module の Bind() / Unbind()(静的ファサードの結び付け)を呼ばない(組み込みのプレビューが Audio.Bind 等を
    // 呼ばないのと同じ作法)。module のインスタンスは Host ごとに新しく作る(Play の Bootstrap・他の Host と状態を共有しない)。
    // X-R-09: Play Mode 突入・シーン切替・再コンパイルで自動停止し、Play Mode 中は Tick しない。
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

        // options: Registry / Pool は呼び出し側(プレビューの Manager 群)のものを渡す。省略(null)時は空の Registry / 新規の Pool。
        // options.GameLoop は無視される(Host が自分の Loop を持つ)。options 自体は書き換えない。
        public EditorModuleHost(EditorPreviewContextOptions options = null)
        {
            var o = options != null ? options.Clone() : new EditorPreviewContextOptions();
            o.GameLoop = Loop;
            o.Registry ??= new AssetRegistry(new NullAssetLoader());
            o.Pool ??= new PoolService();
            Context = DDriveBootstrapContext.ForEditorPreview(o);
            _runner = new ModuleRunner(ModuleRegistry.CreateInstances());
            _runner.Start(Context, bindFacades: false);
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
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
            // Play Mode 中(突入の途中を含む)は Edit 用の Manager を動かさない。
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _lastTickTime = EditorApplication.timeSinceStartup;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            Tick(dt);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && !_disposed)
            {
                Loop.StopAll(StopReason.SceneUnload);
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            if (!_disposed)
            {
                Loop.StopAll(StopReason.SceneUnload);
            }
        }

        private void OnBeforeAssemblyReload() => Dispose();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            EditorApplication.update -= EditorTick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            Loop.StopAll(StopReason.SceneUnload);
            _runner.Stop();
        }
    }
}
