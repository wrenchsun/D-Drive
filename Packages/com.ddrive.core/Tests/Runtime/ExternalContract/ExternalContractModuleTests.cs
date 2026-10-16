using System.Collections.Generic;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Runtime.Extension;
using DDrive.Runtime.Loop;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] E-25 — IDDriveModule(起動配線の口、X-2)。ダミー module は ExternalPackage.Fake.ExternalModuleProbe。
    public class ExternalContractModuleTests
    {
        private GameObject _go;
        private bool _savedInclude;

        [SetUp]
        public void SetUp()
        {
            _savedInclude = ModuleRegistry.IncludeTestAssemblies;
            ModuleRegistry.IncludeTestAssemblies = true;
            ModuleRegistry.Refresh();
            ExternalModuleProbe.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }

            ModuleRegistry.IncludeTestAssemblies = _savedInclude;
            ModuleRegistry.Refresh();
            ExternalModuleProbe.Reset();
        }

        private DDriveRuntimeBootstrap Create(bool bindFacades = true)
        {
            _go = new GameObject("ModuleBootstrapTest");
            _go.SetActive(false);
            var bootstrap = _go.AddComponent<DDriveRuntimeBootstrap>();
            bootstrap.CatalogLabel = string.Empty;
            bootstrap.KeepAcrossScenes = false;
            bootstrap.BindFacades = bindFacades;
            _go.SetActive(true);
            return bootstrap;
        }

        private static List<string> Prefixed(string prefix)
        {
            var r = new List<string>();
            foreach (var l in ExternalModuleProbe.Log)
            {
                if (l.StartsWith(prefix))
                {
                    r.Add(l.Substring(prefix.Length));
                }
            }

            return r;
        }

        [Test]
        public void E25_ModuleManagers_ReceiveLoopCalls()
        {
            var b = Create();
            Assert.AreEqual(5, ExternalModuleProbe.Managers.Count);

            var gl = b.Loop.GameLoop;
            gl.Tick(0.25f);
            gl.BroadcastPause(PauseChannel.UI, true);
            gl.StopAll(StopReason.SceneUnload);
            gl.NotifySceneUnload();

            foreach (var m in ExternalModuleProbe.Managers)
            {
                Assert.AreEqual(1, m.TickCount);
                Assert.AreEqual(0.25f, m.LastDt);
                Assert.AreEqual(1, m.PauseCount);
                Assert.AreEqual(PauseChannel.UI, m.LastPauseChannel);
                Assert.IsTrue(m.LastPaused);
                Assert.AreEqual(1, m.StopAllCount);
                Assert.AreEqual(1, m.SceneUnloadCount);
            }
        }

        [Test]
        public void E25_Modules_RunInOrder_ThenTypeNameOrder()
        {
            Create();
            CollectionAssert.AreEqual(new[] { "First", "SameA", "SameB", "Faulty", "Late" }, Prefixed("Create:"));
            CollectionAssert.AreEqual(new[] { "First", "SameA", "SameB", "Faulty", "Late" }, Prefixed("Bind:"));
        }

        [Test]
        public void E25_BindAndUnbind_AreSymmetric_AndUnbindIsReversed()
        {
            Create();
            Object.DestroyImmediate(_go);
            _go = null;

            CollectionAssert.AreEqual(new[] { "Late", "Faulty", "SameB", "SameA", "First" }, Prefixed("Unbind:"));
            Assert.AreEqual(5, Prefixed("Bind:").Count);
            Assert.AreEqual(5, Prefixed("Unbind:").Count);
        }

        [Test]
        public void E25_ThrowingModule_IsSkippedWithOneWarning_OthersKeepRunning()
        {
            ExternalModuleProbe.FaultyThrowsInCreate = true;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"ExternalModuleProbe\+Faulty.*failed to start and was skipped: System.InvalidOperationException: boom"));

            var b = Create();

            CollectionAssert.AreEqual(new[] { "First", "SameA", "SameB", "Late" }, Prefixed("Bind:"));
            Assert.IsTrue(DDrive.Runtime.Ui.Ui.IsBound, "組み込みのファサードは動く");
            Assert.IsNotNull(b.Ui);

            b.Loop.GameLoop.Tick(0.1f);
            foreach (var m in ExternalModuleProbe.Managers)
            {
                Assert.AreEqual(1, m.TickCount);
            }

            Object.DestroyImmediate(_go);
            _go = null;
            CollectionAssert.AreEqual(new[] { "Late", "SameB", "SameA", "First" }, Prefixed("Unbind:"));
        }

        [Test]
        public void E25_NullElement_IsSkippedWithOneWarning()
        {
            ExternalModuleProbe.FaultyReturnsNullElement = true;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"ExternalModuleProbe\+Faulty.*null element"));

            var b = Create();

            Assert.AreEqual(5, ExternalModuleProbe.Managers.Count);
            Assert.AreEqual(5, Prefixed("Bind:").Count);
            b.Loop.GameLoop.Tick(0.1f);
            Assert.AreEqual(1, ExternalModuleProbe.Managers[0].TickCount);
        }

        [Test]
        public void E25_Context_ExposesBuiltInManagersAndRegistry()
        {
            var b = Create();
            Assert.IsTrue(ExternalModuleProbe.HasContext);
            var ctx = ExternalModuleProbe.LastContext;

            Assert.AreSame(b.Registry, ctx.Registry);
            Assert.AreSame(b.Pool, ctx.Pool);
            Assert.AreSame(b.NetBridge, ctx.NetBridge);
            Assert.AreSame(b.Loop.GameLoop, ctx.GameLoop);
            Assert.AreSame(b.Loop.PauseService, ctx.PauseService);
            Assert.AreSame(b.Ui, ctx.Ui);
            Assert.AreSame(b.Cutscene, ctx.Cutscene);
            Assert.AreSame(b.Audio, ctx.Audio);
            Assert.AreSame(b.Bgm, ctx.Bgm);
            Assert.AreSame(b.Vfx, ctx.Vfx);
            Assert.AreSame(b.Models, ctx.Models);
            Assert.AreSame(b.Prefabs, ctx.Prefabs);
            Assert.AreSame(b.Anim, ctx.Anim);
            Assert.IsNotNull(ctx.TimeSource);
            Assert.IsNotNull(ctx.Events);
            Assert.IsFalse(ctx.IsEditorPreview);
        }

        // ── X-R-01: Bind / Unbind は BindFacades = true の Bootstrap だけ。module のインスタンスは Runner ごと ──

        [Test]
        public void E25_BindFacadesFalse_CreatesAndTicksManagers_ButNeverBindsOrUnbinds()
        {
            var b = Create(bindFacades: false);

            Assert.AreEqual(5, ExternalModuleProbe.Managers.Count, "Manager は作られて GameLoop に載る");
            Assert.AreEqual(0, Prefixed("Bind:").Count, "BindFacades = false では Bind を呼ばない");
            Assert.IsNull(ExternalModuleProbe.FacadeTarget);

            b.Loop.GameLoop.Tick(0.1f);
            foreach (var m in ExternalModuleProbe.Managers)
            {
                Assert.AreEqual(1, m.TickCount);
            }

            Object.DestroyImmediate(_go);
            _go = null;
            Assert.AreEqual(0, Prefixed("Unbind:").Count, "Bind していない module は Unbind もされない");
        }

        [Test]
        public void E25_TwoRunners_DoNotBreakEachOthersFacade()
        {
            var play = Create(); // Play の Bootstrap(ファサードを結ぶ)
            var target = ExternalModuleProbe.FacadeTarget;
            Assert.IsNotNull(target);
            Assert.AreEqual(5, ExternalModuleProbe.Managers.Count);

            // 別の Runner(Editor プレビューの Host 相当: BindFacades = false)を起動して止める。
            var loop = new GameLoop();
            var ctx = DDriveBootstrapContext.ForEditorPreview(new EditorPreviewContextOptions
            {
                GameLoop = loop, Registry = play.Registry, Pool = play.Pool,
            });
            var second = new ModuleRunner(ModuleRegistry.CreateInstances());
            second.Start(in ctx, bindFacades: false);
            Assert.AreEqual(10, ExternalModuleProbe.Managers.Count, "module のインスタンス(と Manager)は Runner ごとに別");
            Assert.AreEqual(5, Prefixed("Bind:").Count, "2 台目は Bind しない");
            Assert.AreEqual(0, second.BoundCount);

            second.Stop();
            Assert.AreSame(target, ExternalModuleProbe.FacadeTarget, "2 台目を止めても 1 台目のファサードは外れない");

            var before = target.TickCount;
            Assert.IsTrue(ExternalModuleProbe.FacadePlay(), "ファサードの Play は Play の Manager に流れる");
            Assert.AreEqual(before + 1, target.TickCount);
        }

        [Test]
        public void E25_ModuleRegistry_CreatesNewInstancesEachTime_ButCachesTypes()
        {
            var a = ModuleRegistry.CreateInstances();
            var b = ModuleRegistry.CreateInstances();
            Assert.AreEqual(a.Count, b.Count);
            Assert.GreaterOrEqual(a.Count, 5);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.AreNotSame(a[i], b[i], "インスタンスは共有しない");
                Assert.AreSame(a[i].GetType(), b[i].GetType());
            }
        }

        // ── X-R-12: Bind / Unbind / Order の例外 ──

        [Test]
        public void E25_ThrowingBind_IsSkippedWithOneWarning_AndNotUnbound()
        {
            ExternalModuleProbe.FaultyThrowsInBind = true;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"ExternalModuleProbe\+Faulty.*failed to start and was skipped: System.InvalidOperationException: bind boom"));

            var b = Create();

            CollectionAssert.AreEqual(new[] { "First", "SameA", "SameB", "Late" }, Prefixed("Bind:"));
            Assert.AreEqual(5, ExternalModuleProbe.Managers.Count, "Bind が失敗した module の Manager も作られている(登録は残る)");
            b.Loop.GameLoop.Tick(0.1f);
            foreach (var m in ExternalModuleProbe.Managers)
            {
                Assert.AreEqual(1, m.TickCount);
            }

            Object.DestroyImmediate(_go);
            _go = null;
            CollectionAssert.AreEqual(new[] { "Late", "SameB", "SameA", "First" }, Prefixed("Unbind:"), "Bind まで成功した module だけが Unbind される");
        }

        [Test]
        public void E25_ThrowingUnbind_IsIgnoredWithOneWarning_OthersStillUnbind()
        {
            ExternalModuleProbe.FaultyThrowsInUnbind = true;
            Create();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"ExternalModuleProbe\+Faulty'\.Unbind threw and was ignored: System.InvalidOperationException: unbind boom"));

            Object.DestroyImmediate(_go);
            _go = null;

            CollectionAssert.AreEqual(new[] { "Late", "SameB", "SameA", "First" }, Prefixed("Unbind:"));
        }

        [Test]
        public void E25_ThrowingOrder_UsesZeroWithOneWarning()
        {
            ExternalModuleProbe.FaultyThrowsInOrder = true;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"ExternalModuleProbe\+Faulty'\.Order threw and 0 is used: System.InvalidOperationException: order boom"));

            Create();

            // Order 0 扱い: First(-5) → Faulty(0、型名順で SameA の前) → SameA → SameB → Late(5)
            CollectionAssert.AreEqual(new[] { "First", "Faulty", "SameA", "SameB", "Late" }, Prefixed("Create:"));
        }
    }
}
