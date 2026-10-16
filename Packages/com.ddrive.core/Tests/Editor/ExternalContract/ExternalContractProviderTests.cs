using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DDrive.Editor.Extension;
using DDrive.Editor.Manual;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Registry;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] E-28 — サンプル・確認用シーン・マニュアルの口(X-4、[1014] §2.4)。
    // ダミー provider は ExternalSampleProviders.cs(ExternalSampleData = 種別 "Sample")。ExtensionProviders.IncludeTestAssemblies = true で拾わせる。
    // 実データ(Assets/GameData)には何も作らない: 窓の SampleGameDataRoot を一時フォルダへ向け、TearDown で一時フォルダごと消す。
    public class ExternalContractProviderTests
    {
        private const string Temp = "Assets/Tests/DDriveTemp/X4Providers";
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static Type TestWindows => Type.GetType("DDrive.Tests.Editor.UxTestWindows, DDrive.Tests.Editor", true);

        private readonly List<UnityEngine.Object> _cleanup = new();
        private bool _savedInclude;
        private bool _savedKindInclude;
        private int _windowsBefore;

        [SetUp]
        public void SetUp()
        {
            TestWindows.GetMethod("CloseAll").Invoke(null, null);
            _windowsBefore = (int)TestWindows.GetMethod("Count").Invoke(null, null);
            _savedInclude = ExtensionProviders.IncludeTestAssemblies;
            _savedKindInclude = DDrive.Foundation.Extension.AssetKindRegistry.IncludeTestAssemblies;
            ExternalProviderTestState.Reset();
            ExternalProviderTestState.GameDataRoot = Temp + "/GameData";
            ExtensionProviders.IncludeTestAssemblies = false;
            ExtensionProviders.Refresh();
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
            ExtensionProviders.IncludeTestAssemblies = _savedInclude;
            ExtensionProviders.Refresh();
            DDrive.Foundation.Extension.AssetKindRegistry.IncludeTestAssemblies = _savedKindInclude;
            DDrive.Foundation.Extension.AssetKindRegistry.Refresh();
            ExternalProviderTestState.Reset();

            // 開いたテスト用シーンから抜けてから一時フォルダを消す。
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.IsValidFolder("Assets/Tests/DDriveTemp/X4Providers"))
            {
                AssetDatabase.DeleteAsset("Assets/Tests/DDriveTemp/X4Providers");
            }

            if (AssetDatabase.IsValidFolder("Assets/Tests/DDriveTemp") && AssetDatabase.FindAssets(string.Empty, new[] { "Assets/Tests/DDriveTemp" }).Length == 0)
            {
                AssetDatabase.DeleteAsset("Assets/Tests/DDriveTemp");
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => TestWindows.GetMethod("Release").Invoke(null, null);

        private void UseProviders()
        {
            ExtensionProviders.IncludeTestAssemblies = true;
            ExtensionProviders.Refresh();
            // 種別の接頭辞(SMP)・フォルダ・カタログ名は IAssetKindProvider(ExternalSampleKind)から引くので、そちらも見せる。
            DDrive.Foundation.Extension.AssetKindRegistry.IncludeTestAssemblies = true;
            DDrive.Foundation.Extension.AssetKindRegistry.Refresh();
        }

        // 窓を表示せずに組み立てる(target は null 可 = 空状態)。
        private ExternalSampleEditorWindow BuildWindow(ExternalSampleData target)
        {
            var window = ScriptableObject.CreateInstance<ExternalSampleEditorWindow>();
            _cleanup.Add(window);
            var baseType = typeof(UxEditorWindow<ExternalSampleData>);
            baseType.GetField("_target", Inst).SetValue(window, target);
            baseType.GetMethod("CreateGUI", Inst).Invoke(window, null);
            return window;
        }

        private static ExternalSampleData TargetOf(EditorWindow w)
            => (ExternalSampleData)typeof(UxEditorWindow<ExternalSampleData>).GetField("_target", Inst).GetValue(w);

        // パネルに載っていない要素にはイベントが届かないので、Button の clicked(Clickable のイベントのバッキングフィールド)を直接呼ぶ。
        private static void Click(Button button)
        {
            Assert.IsNotNull(button, "ボタンが見つかりません");
            var field = typeof(Clickable).GetField("clicked", Inst);
            Assert.IsNotNull(field, "Clickable.clicked のバッキングフィールドが見つかりません");
            ((Action)field.GetValue(button.clickable))?.Invoke();
        }

        private static Button ButtonWithText(VisualElement root, string text)
        {
            foreach (var b in root.Query<Button>().ToList())
            {
                if (b.text != null && b.text.Contains(text))
                {
                    return b;
                }
            }

            return null;
        }

        // 発見: テスト用アセンブリは既定で除外、重複は警告 1 回 + 先勝ち、KindKey / ページ key で引ける。
        [Test]
        public void E28_Discovery_ExcludesTestAssembliesByDefault_AndFirstWinsOnDuplicates()
        {
            Assert.IsFalse(ExtensionProviders.TryGetSample(ExternalSampleKind.Key, out _), "既定ではテスト用アセンブリの provider は見えない");
            Assert.IsFalse(ExtensionProviders.TryGetPreviewScene(ExternalSampleKind.Key, out _));
            Assert.IsFalse(ExtensionProviders.TryGetManualPage("external-sample", out _, out _));

            UseProviders();
            LogAssert.Expect(LogType.Warning, new Regex(@"Duplicate ISampleProvider KindKey 'Sample'"));

            Assert.IsTrue(ExtensionProviders.TryGetSample(ExternalSampleKind.Key, out var sample));
            Assert.IsInstanceOf<ExternalSampleSampleProvider>(sample, "型のフルネーム順で先のものが勝つ");
            Assert.IsTrue(ExtensionProviders.TryGetSample(ExternalSampleKind.Key, out _));
            Assert.AreEqual(1, new List<ISampleProvider>(ExtensionProviders.Samples).FindAll(p => p.KindKey == ExternalSampleKind.Key).Count);

            Assert.IsTrue(ExtensionProviders.TryGetPreviewScene(ExternalSampleKind.Key, out var scene));
            Assert.IsInstanceOf<ExternalSamplePreviewSceneProvider>(scene);

            Assert.IsTrue(ExtensionProviders.TryGetManualPage("external-sample", out var path, out var title));
            Assert.AreEqual("Sample Editor", title);
            StringAssert.EndsWith("none.html", path);

            Assert.IsFalse(ExtensionProviders.TryGetSample("NoSuchKind", out _));
            Assert.IsFalse(ExtensionProviders.TryGetSample(string.Empty, out _));
        }

        // (a) S-14 相当: 空状態 → サンプルを作る → Data が規定のフォルダに出来てカタログに載る → 確認用シーンが開く → ▶ が押せる。
        [Test]
        public void E28_EmptyState_CreateSample_OpenPreviewScene_Play_FlowWorks()
        {
            UseProviders();
            LogAssert.Expect(LogType.Warning, new Regex(@"Duplicate ISampleProvider KindKey 'Sample'"));

            // 確認用のテスト用シーン(Assets/Tests/DDriveTemp/ の下に作る小さなシーン)。
            AssetCreationEnsure(Temp);
            var scenePath = Temp + "/Preview.unity";
            var created = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("PreviewMarker");
            Assert.IsTrue(EditorSceneManager.SaveScene(created, scenePath));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // 別のシーンに移って未保存を残さない
            ExternalProviderTestState.ScenePath = scenePath;

            var window = BuildWindow(null);
            var root = window.rootVisualElement;

            // 空状態: 「サンプルを作る」が出る。確認用シーンはまだ(対象が無いのでヒーローが無い)。
            var sampleButton = root.Q<Button>(className: "ux-empty__sample");
            Assert.IsNotNull(sampleButton, "ISampleProvider があるのでボタンが出る");
            Click(sampleButton);

            var data = TargetOf(window);
            Assert.IsNotNull(data, "サンプルが選ばれる");
            Assert.AreEqual(1, ExternalProviderTestState.CreateSampleCount);
            var assetPath = AssetDatabase.GetAssetPath(data);
            StringAssert.StartsWith(Temp + "/GameData/", assetPath, "規定のフォルダ(窓の SampleGameDataRoot)に出来る");
            StringAssert.StartsWith("SMP_", Path.GetFileNameWithoutExtension(assetPath), "種別の接頭辞(IAssetKindProvider.Prefix)");
            Assert.AreEqual("Sample", data.Category);
            Assert.AreEqual("Sample", data.DisplayName);
            Assert.AreEqual(2f, ((ExternalSampleData)data).Intensity, "provider が作った中身が引き継がれる");
            StringAssert.Contains("/Sample", ExternalProviderTestState.LastSampleFolder, "素材用のフォルダが provider に渡る");
            Assert.IsTrue(data.Id != 0, "Id が振られている");

            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(Temp + "/GameData/Catalogs/" + ExternalSampleKind.CatalogNameValue + ".asset");
            Assert.IsNotNull(catalog, "カタログが作られる");
            var found = false;
            foreach (var e in catalog.Entries)
            {
                found |= e.Id == data.Id && e.KindKey == ExternalSampleKind.Key;
            }

            Assert.IsTrue(found, "カタログに KindKey 付きで載る");

            // 同じサンプルをもう一度押しても増えない(既にあればそれを選ぶ)。
            SetTargetNull(window);
            Click(window.rootVisualElement.Q<Button>(className: "ux-empty__sample"));
            Assert.AreSame(data, TargetOf(window));
            Assert.AreEqual(1, ExternalProviderTestState.CreateSampleCount, "2 回目は provider を呼ばない");

            // ヒーローの「確認用シーン」→ シーンが開き Setup が呼ばれる。
            var sceneButton = ButtonWithText(window.rootVisualElement, "確認用シーン");
            Assert.IsNotNull(sceneButton, "IPreviewSceneProvider があるのでヒーローにボタンが出る");
            Click(sceneButton);
            Assert.AreEqual(scenePath, UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            Assert.AreEqual(1, ExternalProviderTestState.SetupCount);
            Assert.IsNotNull(GameObject.Find("PreviewMarker"), "確認用シーンの中身");

            // ▶ が押せる(外部の Manager が駆動される)。
            var play = ButtonWithText(window.rootVisualElement, "再生");
            Assert.IsNotNull(play, "固定バーの ▶");
            Click(play);
            Assert.IsTrue(window.Driver.Playing);
        }

        // (b) provider が無い Extension 種別では、サンプルも確認用シーンのボタンも出ない。
        [Test]
        public void E28_NoProvider_NoSampleButton_NoPreviewSceneButton()
        {
            var empty = BuildWindow(null);
            Assert.IsNull(empty.rootVisualElement.Q<Button>(className: "ux-empty__sample"), "provider が無ければサンプルのボタンを出さない");

            var data = ScriptableObject.CreateInstance<ExternalSampleData>();
            _cleanup.Add(data);
            var window = BuildWindow(data);
            Assert.IsNull(ButtonWithText(window.rootVisualElement, "確認用シーン"), "provider が無ければ確認用シーンのボタンを出さない");
            Assert.AreEqual(0, ExternalProviderTestState.CreateSampleCount);
        }

        // provider の例外・null・欠けたシーンは警告で済み、止まらない。
        [Test]
        public void E28_ProviderFailures_WarnAndContinue()
        {
            UseProviders();
            LogAssert.Expect(LogType.Warning, new Regex(@"Duplicate ISampleProvider KindKey 'Sample'"));
            ExtensionProviders.TryGetSample(ExternalSampleKind.Key, out var sample);

            ExternalProviderTestState.SampleThrows = true;
            LogAssert.Expect(LogType.Warning, new Regex(@"CreateSample が例外を出しました"));
            Assert.IsNull(ExtensionProviderActions.CreateOrGetSample(sample, typeof(ExternalSampleData), ExternalProviderTestState.GameDataRoot));

            ExtensionProviders.TryGetPreviewScene(ExternalSampleKind.Key, out var scene);
            ExternalProviderTestState.ScenePath = Temp + "/Missing.unity";
            LogAssert.Expect(LogType.Warning, new Regex(@"確認用シーンが見つかりません"));
            Assert.IsFalse(ExtensionProviderActions.OpenPreviewScene(scene));
            Assert.AreEqual(0, ExternalProviderTestState.SetupCount);
        }

        // (c) ManualLauncher: 組み込みのページ名に無い key は IManualPageProvider の宣言から解決する(組み込みが先)。
        [Test]
        public void E28_ManualLauncher_ResolvesExternalPages_BuiltInWins()
        {
            AssetCreationEnsure(Temp);
            var file = Temp + "/sample-editor.html";
            File.WriteAllText(file, "<html><title>Sample Editor</title></html>");
            AssetDatabase.ImportAsset(file);
            ExternalProviderTestState.ManualPath = file;

            Assert.IsFalse(ManualLauncher.TryResolveExternalPage("external-sample", out _), "provider が見えない間は外部ページではない");

            UseProviders();
            Assert.IsTrue(ManualLauncher.TryResolveExternalPage("external-sample", out var full));
            Assert.AreEqual(Path.GetFullPath(file), full, "プロジェクト基準の相対パスが絶対パスに解決される");
            Assert.IsTrue(File.Exists(full));

            Assert.IsFalse(ManualLauncher.TryResolveExternalPage("no-such-page", out _));

            // 組み込みのトップページと同じ key を外部が宣言しても、組み込み(docs/DesignerManual)が先。
            var builtIn = Path.Combine(ManualPages.GetManualFolder(ManualPages.GetProjectRoot(), ManualKind.Designer), ManualPages.TopPageName + ".html");
            if (File.Exists(builtIn))
            {
                Assert.IsFalse(ManualLauncher.TryResolveExternalPage("index", out _) && ManualPages.TopPageName == "index");
            }

        }

        private static void AssetCreationEnsure(string folder)
        {
            var parts = folder.Split('/');
            var path = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = path + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(path, parts[i]);
                }

                path = next;
            }
        }

        private static void SetTargetNull(ExternalSampleEditorWindow window)
        {
            typeof(UxEditorWindow<ExternalSampleData>).GetField("_target", Inst).SetValue(window, null);
            typeof(UxEditorWindow<ExternalSampleData>).GetMethod("CreateGUI", Inst).Invoke(window, null);
        }
    }
}
