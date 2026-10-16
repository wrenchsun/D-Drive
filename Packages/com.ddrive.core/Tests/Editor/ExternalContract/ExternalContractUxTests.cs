using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DDrive.Editor.Inspector;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Pool;
using DDrive.Runtime.Extension;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] E-27 — エディター基盤の公開(X-3、[1014] §2.3)。持ち込み先が UxEditorWindow<T> 派生の窓を public API だけで書ける。
    // 窓は ExternalSampleEditorWindow(このテスト用アセンブリ = 外部の窓の模擬)。UxTestHooks / UxScreenshot / UxTestWindows は internal なのでリフレクションで使う。
    public class ExternalContractUxTests
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static Assembly EditorAssembly => typeof(UxTheme).Assembly;
        private static Type TestWindows => Type.GetType("DDrive.Tests.Editor.UxTestWindows, DDrive.Tests.Editor", true);

        private readonly List<UnityEngine.Object> _cleanup = new();
        private bool _savedIncludeModules;
        private int _windowsBefore;

        [SetUp]
        public void SetUp()
        {
            TestWindows.GetMethod("CloseAll").Invoke(null, null);
            _windowsBefore = (int)TestWindows.GetMethod("Count").Invoke(null, null);
            _savedIncludeModules = ModuleRegistry.IncludeTestAssemblies;
            ExternalModuleProbe.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    UnityEngine.Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
            TestWindows.GetMethod("CloseAll").Invoke(null, null);
            Assert.AreEqual(_windowsBefore, (int)TestWindows.GetMethod("Count").Invoke(null, null), "ウィンドウの取りこぼし");
            ModuleRegistry.IncludeTestAssemblies = _savedIncludeModules;
            ModuleRegistry.Refresh();
            ExternalModuleProbe.Reset();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => TestWindows.GetMethod("Release").Invoke(null, null);

        private ExternalSampleData NewData(string name = "UxExternalSample")
        {
            var d = ScriptableObject.CreateInstance<ExternalSampleData>();
            d.name = name;
            d.Knobs.Add(new ExternalSampleKnob { Name = "Size", Value = 0.4f });
            d.Knobs.Add(new ExternalSampleKnob { Name = "Color tint", Value = 0.8f });
            _cleanup.Add(d);
            return d;
        }

        // 窓を表示せずに組み立てる(UxFoundationTests と同じ手順)。
        private EditorWindow BuildWindow(Type type, UnityEngine.Object data)
        {
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            _cleanup.Add(window);
            var baseType = type.BaseType;
            baseType.GetField("_target", Inst).SetValue(window, data);
            baseType.GetMethod("CreateGUI", Inst).Invoke(window, null);
            return window;
        }

        private static HashSet<string> ClassesOf(VisualElement root)
        {
            var set = new HashSet<string>();
            foreach (var e in root.Query<VisualElement>().ToList())
            {
                foreach (var c in e.GetClasses())
                {
                    set.Add(c);
                }
            }

            return set;
        }

        // (a) 窓が開いて、ヒーロー / カード / 固定バー / 検索 / 未調整を隠す / 自動保存が組み込みの窓(VfxEditor)と同じ USS クラスで組まれる。
        [Test]
        public void E27_Window_BuildsSameChromeAsBuiltInWindow()
        {
            var external = BuildWindow(typeof(ExternalSampleEditorWindow), NewData());
            var vfxData = ScriptableObject.CreateInstance<DDrive.Runtime.Vfx.VfxData>();
            _cleanup.Add(vfxData);
            var builtIn = BuildWindow(EditorAssembly.GetType("DDrive.Editor.Vfx.VfxEditor", true), vfxData);

            var ext = ClassesOf(external.rootVisualElement);
            var built = ClassesOf(builtIn.rootVisualElement);
            foreach (var cls in new[]
                     {
                         "ux-root", "ux-page", "ux-scroll", "ux-hero", "ux-hero__title", "ux-hero__actions", "ux-hero__modes", "ux-seg",
                         "ux-searchbox", "ux-searchbox__field", "ux-hidetoggle", "ux-card", "ux-bar", "ux-bar__mod", "ux-bar__speed", "ux-bar__repeat",
                     })
            {
                Assert.IsTrue(built.Contains(cls), "組み込み(VfxEditor)に " + cls + " が無い(テストの前提)");
                Assert.IsTrue(ext.Contains(cls), "外部の窓に " + cls + " が無い");
            }

            var root = external.rootVisualElement;
            Assert.IsTrue(root.ClassListContains("ux-root"));
            Assert.IsNotNull(root.Q<Toggle>(className: "ux-hidetoggle"), "未調整を隠す");
            var toggles = root.Query<Toggle>().ToList();
            Assert.IsTrue(toggles.Exists(t => t.label == "自動保存"), "固定バーの自動保存");

            // 目的別カード 2 + 管理情報 + その他の検証(隠れている)
            var cards = root.Query(className: "ux-card").ToList();
            Assert.AreEqual(4, cards.Count, "カード数(見せ方 / 長さと調整 / 管理情報 / その他の検証)");
        }

        // (a) 外部の部品: DurationBar と TuneKnobs が行になり、つまみが Data から作られる。
        [Test]
        public void E27_Window_HostsDurationBarAndTuneKnobsFromPublicTypes()
        {
            var data = NewData();
            var window = BuildWindow(typeof(ExternalSampleEditorWindow), data);
            var root = window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-durbar"), "DurationBar");
            Assert.IsNotNull(root.Q(className: "ux-tune"), "TuneKnobsRow");
            Assert.AreEqual(2, new ExternalSampleTuneSource(data).Count);
            Assert.AreEqual(TuneCategories.Names[1], TuneCategories.Names[TuneCategories.Infer(TuneValueKind.Float, "Size", "Size")]);
        }

        // (a) 再生: 土台の ▶ ■ が外部の IsPlaying / StartPlayback / StopPlayback に届き、UxPreviewDriverBase の Tick が外部 Manager に届く。
        [Test]
        public void E27_Window_PlaybackDrivesExternalManagerThroughPreviewDriver()
        {
            var window = (ExternalSampleEditorWindow)BuildWindow(typeof(ExternalSampleEditorWindow), NewData());
            var driver = window.Driver;
            Assert.IsNotNull(driver);

            var baseType = typeof(UxEditorWindow<ExternalSampleData>);
            baseType.GetMethod("PlayMain", Inst).Invoke(window, new object[] { false });
            Assert.IsTrue(driver.Playing);
            driver.Speed = 2f;
            driver.Tick(0.1f);
            Assert.AreEqual(1, driver.Manager.TickCount);
            Assert.AreEqual(0.2f, driver.Manager.LastDt, 1e-4f, "Speed が掛かる");

            baseType.GetMethod("StopMain", Inst).Invoke(window, null);
            Assert.IsFalse(driver.Playing);
        }

        // UxPreviewDriverBase: ルート(名前・DontSave)・Paused・自動停止・Dispose。
        [Test]
        public void E27_PreviewDriverBase_RootPausedResetAndDispose()
        {
            var driver = new ExternalSamplePreviewDriver();
            try
            {
                Assert.IsTrue(driver.RootName.StartsWith("[D-Drive]"), "掃除(EditorPreviewSweeper)の対象になる名前");
                var root = driver.PreviewRoot;
                Assert.IsNotNull(root);
                Assert.AreEqual(driver.RootName, root.name);
                Assert.IsTrue((root.hideFlags & HideFlags.DontSaveInEditor) != 0);
                Assert.IsTrue(EditorPreviewSweeper.IsPreviewRoot(root));

                driver.Play();
                driver.Tick(0.1f);
                Assert.AreEqual(1, driver.Manager.TickCount);
                driver.Paused = true;
                driver.Tick(0.1f);
                Assert.AreEqual(1, driver.Manager.TickCount, "Paused 中は OnTick が呼ばれない");
                driver.Paused = false;

                // シーン保存の直前: 停止するがルートは残す。
                typeof(UxPreviewDriverBase).GetMethod("OnSceneSaving", Inst).Invoke(driver, new object[] { default(UnityEngine.SceneManagement.Scene), "x.unity" });
                Assert.AreEqual(PreviewResetReason.SceneSaving, driver.LastResetReason);
                Assert.IsFalse(driver.Playing);
                Assert.IsTrue(root != null, "保存の直前ではルートを壊さない");

                // Play Mode 突入: 停止してルートを破棄。
                driver.Play();
                typeof(UxPreviewDriverBase).GetMethod("OnPlayModeStateChanged", Inst).Invoke(driver, new object[] { PlayModeStateChange.ExitingEditMode });
                Assert.AreEqual(PreviewResetReason.EnteringPlayMode, driver.LastResetReason);
                Assert.IsTrue(root == null, "ルートは破棄される");
            }
            finally
            {
                driver.Dispose();
                driver.Dispose(); // 二重 Dispose は無害
            }

            Assert.AreEqual(PreviewResetReason.Dispose, driver.LastResetReason);
            Assert.IsTrue(driver.IsDisposed);
            driver.Tick(0.1f);
            Assert.AreEqual(1, driver.Manager.TickCount, "Dispose 後は Tick しない");
        }

        // (c) EditorModuleHost: ExternalModuleProbe の module から Manager を作って Edit Mode で Tick し、Dispose で逆順に Unbind。
        [Test]
        public void E27_EditorModuleHost_TicksModuleManagers_AndUnbindsOnDispose()
        {
            ModuleRegistry.IncludeTestAssemblies = true;
            ModuleRegistry.Refresh();

            var host = new EditorModuleHost(EditorAnchorRegistry.Build(), new PoolService());
            try
            {
                Assert.AreEqual(5, host.Managers.Count);
                Assert.IsTrue(host.Context.IsEditorPreview);
                Assert.IsNull(host.Context.Ui, "プレビューが渡さなかった組み込み Manager は null");
                Assert.IsNotNull(host.Context.Registry);
                CollectionAssert.AreEqual(new[] { "First", "SameA", "SameB", "Faulty", "Late" }, Prefixed("Bind:"));

                host.Tick(0.25f);
                foreach (var m in ExternalModuleProbe.Managers)
                {
                    Assert.AreEqual(1, m.TickCount);
                    Assert.AreEqual(0.25f, m.LastDt);
                }
            }
            finally
            {
                host.Dispose();
                host.Dispose();
            }

            CollectionAssert.AreEqual(new[] { "Late", "Faulty", "SameB", "SameA", "First" }, Prefixed("Unbind:"));
            foreach (var m in ExternalModuleProbe.Managers)
            {
                Assert.AreEqual(1, m.StopAllCount);
            }

            host.Tick(0.25f);
            Assert.AreEqual(1, ExternalModuleProbe.Managers[0].TickCount, "Dispose 後は Tick しない");
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

        // [DataEditor] の主エディター検査(DataEditorRegistryTests)が Extension 種別の窓も対象になる前提: 外部の窓が登録され、新エディター(UxEditorWindow<T> 派生・Order 0)として引ける。
        [Test]
        public void E27_ExternalWindow_IsRegisteredAsPrimaryDataEditor()
        {
            Assert.IsTrue(DataEditorRegistry.TryGetPrimary(typeof(ExternalSampleData), out var primary));
            Assert.AreEqual(typeof(ExternalSampleEditorWindow), primary.WindowType);
            Assert.AreEqual(0, primary.Order);
            Assert.AreEqual("Sample Editor で開く", primary.Label);
        }

        // (d) 契約: 外部が継承・構築する公開型のコンストラクタ・抽象メンバー・USS クラス名。
        [Test]
        public void E27_Contract_ConstructorsAbstractMembersAndUssClasses()
        {
            // コンストラクタ(シグネチャは editor-contract.txt の UxPublicApi 節でも固定される)
            AssertCtor(typeof(UxCard), typeof(string), typeof(string), typeof(Action));
            AssertCtor(typeof(UxRow), typeof(SerializedObject), typeof(SerializedObject), typeof(IReadOnlyList<FieldGuideEntry>), typeof(string), typeof(string), typeof(FieldTier), typeof(bool), typeof(VisualElement), typeof(Action));
            AssertCtor(typeof(UxVisualRow), typeof(SerializedObject), typeof(SerializedObject), typeof(IReadOnlyList<FieldGuideEntry>), typeof(string), typeof(string), typeof(FieldTier), typeof(VisualElement), typeof(Action));
            AssertCtor(typeof(DurationBar), typeof(UnityEngine.Object), typeof(Func<DurationBar.Values>), typeof(Action<DurationBar.Values>), typeof(Action<bool>));
            AssertCtor(typeof(TuneKnobs), typeof(ITuneSource), typeof(SerializedObject), typeof(Action), typeof(Action));
            AssertCtor(typeof(TuneKnobsRow), typeof(SerializedObject), typeof(SerializedObject), typeof(IReadOnlyList<FieldGuideEntry>), typeof(string), typeof(TuneKnobs), typeof(Action));
            AssertCtor(typeof(AnchorPad), typeof(UnityEngine.Object), typeof(Func<Vector3>), typeof(Action<Vector3>), typeof(Action<bool>));
            AssertCtor(typeof(DataSwitcher), typeof(Type), typeof(Func<DDrive.Foundation.Data.AssetDataBase>), typeof(Action<DDrive.Foundation.Data.AssetDataBase>));

            // 派生側が必ず実装する抽象メンバー
            AssertAbstract(typeof(UxEditorWindow<ExternalSampleData>), "PrefsKey", "Guide", "ManualPage", "CardDescription");
            AssertAbstract(typeof(FieldGuide<ExternalSampleData>), "SpecificEntries", "SpecificPurposes");
            AssertAbstract(typeof(UxPreviewDriverBase), "OnTick", "OnReset");

            // USS クラス名(テーマに定義がある。外部の C# / USS が名前で参照する)
            var uss = File.ReadAllText(Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(EditorAssembly).resolvedPath, "Editor", "Ux", "Theme", "DDriveUx.uss"));
            foreach (var cls in new[]
                     {
                         "ux-root", "ux-dark", "ux-light", "ux-hero", "ux-card", "ux-row", "ux-bar", "ux-seg", "ux-durbar", "ux-tune",
                         "ux-searchbox", "ux-hidetoggle", "ux-hidden", "ux-btn", "ux-chip", "ux-badge", "ux-dim", "ux-issues",
                     })
            {
                StringAssert.Contains("." + cls, uss, "USS に ." + cls + " が無い");
            }

            Assert.AreEqual("ux-hidden", UxShow.HiddenClass);
            Assert.AreEqual("管理情報", FieldGuide<ExternalSampleData>.AdminPurpose);
        }

        private static void AssertCtor(Type type, params Type[] args)
        {
            var c = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, args, null);
            Assert.IsNotNull(c, type.Name + " の public コンストラクタ(" + string.Join(", ", Array.ConvertAll(args, a => a.Name)) + ")");
        }

        private static void AssertAbstract(Type type, params string[] names)
        {
            foreach (var n in names)
            {
                MethodInfo m = null;
                for (var t = type; t != null && m == null; t = t.BaseType)
                {
                    var p = t.GetProperty(n, Inst | BindingFlags.DeclaredOnly);
                    m = p != null ? p.GetMethod : t.GetMethod(n, Inst | BindingFlags.DeclaredOnly);
                }

                Assert.IsNotNull(m, type.Name + "." + n + " が無い");
                Assert.IsTrue(m.IsAbstract, type.Name + "." + n + " は abstract のまま");
                Assert.IsTrue(m.IsFamily || m.IsPublic, type.Name + "." + n + " は protected/public(外部の派生が実装できる)");
            }
        }

        // (b) 現在のエディターの配色(Light / Dark)で、外部の窓と組み込みの VfxEditor のスクリーンショットを撮る
        // (パスは DDRIVE_UX_SHOT_DIR か Temp/ddrive-x3-shots。ファイル名の末尾が -light / -dark)。
        // テーマの切替はテストの中では反映されない(窓の描画ループが回る前にテストが進む)ので、切り替えは呼び出し側が行い、
        // 配色ごとにこのテストを 1 回ずつ実行する(`SwitchSkinAndRepaintAllViews` を execute_code で呼ぶ → 反映を待つ → 再実行)。
        [UnityTest]
        public IEnumerator E27_Screenshots_CurrentTheme()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assume.That(false, "グラフィックデバイスが無い環境のためスキップ");
            }

            var dir = Environment.GetEnvironmentVariable("DDRIVE_UX_SHOT_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Path.GetTempPath(), "ddrive-x3-shots");
            }

            Directory.CreateDirectory(dir);

            var shot = EditorAssembly.GetType("DDrive.Editor.Ux.UxScreenshot", true);
            var save = shot.GetMethod("Save", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var suffix = EditorGUIUtility.isProSkin ? "-dark" : "-light";

            var data = NewData("ScreenshotSample");
            var vfxData = ScriptableObject.CreateInstance<DDrive.Runtime.Vfx.VfxData>();
            vfxData.name = "ScreenshotVfx";
            _cleanup.Add(vfxData);

            var ext = EditorWindow.CreateWindow<ExternalSampleEditorWindow>();
            ext.position = new Rect(60, 60, 560, 820);
            ext.Show();
            typeof(UxEditorWindow<ExternalSampleData>).GetMethod("SetTarget", Inst).Invoke(ext, new object[] { data, false });
            var vfxType = EditorAssembly.GetType("DDrive.Editor.Vfx.VfxEditor", true);
            var vfx = (EditorWindow)ScriptableObject.CreateInstance(vfxType);
            vfx.position = new Rect(660, 60, 560, 820);
            vfx.Show();
            vfxType.BaseType.GetMethod("SetTarget", Inst).Invoke(vfx, new object[] { vfxData, false });

            var saved = new List<string>();
            try
            {
                var until = EditorApplication.timeSinceStartup + 2.0;
                while (EditorApplication.timeSinceStartup < until)
                {
                    ext.Repaint();
                    vfx.Repaint();
                    yield return null;
                }

                foreach (var (w, name) in new[] { ((EditorWindow)ext, "external-sample"), (vfx, "vfx-editor") })
                {
                    var path = Path.Combine(dir, name + suffix + ".png");
                    if ((bool)save.Invoke(null, new object[] { w, path }))
                    {
                        saved.Add(path);
                    }
                }
            }
            finally
            {
                if (ext != null)
                {
                    ext.Close();
                }

                if (vfx != null)
                {
                    vfx.Close();
                }
            }

            Debug.Log("[E27] screenshots: " + string.Join(", ", saved));
            Assert.GreaterOrEqual(saved.Count, 1, "スクリーンショットが 1 枚も撮れなかった");
        }
    }
}
