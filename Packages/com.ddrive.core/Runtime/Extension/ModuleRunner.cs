using System;
using System.Collections.Generic;
using DDrive.Foundation.Manager;
using UnityEngine;

namespace DDrive.Runtime.Extension
{
    // [1014_extension_platform.md] §2.2/§2.3(X-2/X-3) — IDDriveModule の起動と終了の実体。DDriveRuntimeBootstrap と
    // Editor プレビュー(EditorModuleHost)が同じ規則で使う: Start = Order 順に CreateManagers → GameLoop.Register → Bind()、
    // Stop = 逆順に Unbind() → Manager の Unregister。例外・null 要素は module 単位で警告して飛ばす(例外で止めない)。
    public sealed class ModuleRunner
    {
        private readonly IReadOnlyList<IDDriveModule> _modules;
        private readonly List<IDDriveModule> _bound = new();
        private readonly List<IAssetManager> _managers = new();
        private GameLoop _loop;

        public ModuleRunner(IReadOnlyList<IDDriveModule> modules)
        {
            _modules = modules ?? Array.Empty<IDDriveModule>();
        }

        // 登録した module の Manager(Start の順)。
        public IReadOnlyList<IAssetManager> Managers => _managers;

        // Bind まで成功した module の数。
        public int BoundCount => _bound.Count;

        public void Start(in DDriveBootstrapContext ctx)
        {
            _loop = ctx.GameLoop;
            for (var i = 0; i < _modules.Count; i++)
            {
                var module = _modules[i];
                var name = module.GetType().FullName;
                try
                {
                    var managers = module.CreateManagers(in ctx);
                    if (managers != null)
                    {
                        var warnedNull = false;
                        for (var j = 0; j < managers.Count; j++)
                        {
                            if (managers[j] == null)
                            {
                                if (!warnedNull)
                                {
                                    warnedNull = true;
                                    Debug.LogWarning($"[DDrive] IDDriveModule '{name}'.CreateManagers returned a null element; it was skipped.");
                                }

                                continue;
                            }

                            _loop.Register(managers[j]);
                            _managers.Add(managers[j]);
                        }
                    }

                    module.Bind();
                    _bound.Add(module);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] IDDriveModule '{name}' failed to start and was skipped: {e.Message}");
                }
            }
        }

        // 逆順に Unbind。呼び出し側が先に StopAll を済ませておく。Manager は GameLoop から外す。
        public void Stop()
        {
            for (var i = _bound.Count - 1; i >= 0; i--)
            {
                var module = _bound[i];
                try
                {
                    module.Unbind();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] IDDriveModule '{module.GetType().FullName}'.Unbind threw and was ignored: {e.Message}");
                }
            }

            _bound.Clear();

            if (_loop != null)
            {
                for (var i = _managers.Count - 1; i >= 0; i--)
                {
                    _loop.Unregister(_managers[i]);
                }
            }

            _managers.Clear();
        }
    }
}
