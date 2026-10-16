using System;
using System.Collections;
using System.IO;
using System.Linq;
using DDrive.Editor.Extension;
using DDrive.Editor.Menu;
using DDrive.Foundation.Extension;
using DDrive.Runtime.Extension;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExternalContract.Tests
{
    // [docs/1014] X-5 — 雛形(Packages/com.ddrive.core/Samples~/ExtensionTemplate/)を、持ち込み先と同じ手順でプロジェクトにコピーして
    // コンパイルが通ること、コピーした種別 / module / provider が D-Drive に認識されることを確かめる(空プロジェクトでの導入試験の代わりに、
    // 開発リポジトリで 1 回)。コンパイルを跨ぐのでドメインリロードを待つ([UnityTest] + RecompileScripts)。終わったら消してもう一度コンパイルする。
    // 雛形は Samples~ に置く(Unity からは見えない)ので、ビルド・Validation・他のテストには混ざらない。このテストがコピーしている間だけ見える。
    public class ExtensionTemplateCompilesTests
    {
        private const string Dest = "Assets/Tests/DDriveTemp/ExtensionTemplateSmoke";

        private static string TemplateRoot => Path.Combine(
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDriveMenu).Assembly).resolvedPath, "Samples~", "ExtensionTemplate");

        private static void RemoveCopy()
        {
            if (AssetDatabase.IsValidFolder(Dest))
            {
                AssetDatabase.DeleteAsset(Dest);
            }

            if (Directory.Exists(Dest))
            {
                Directory.Delete(Dest, true);
            }

            if (File.Exists(Dest + ".meta"))
            {
                File.Delete(Dest + ".meta");
            }
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(from))
            {
                CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Directory.Exists(Dest))
            {
                RemoveCopy();
                AssetDatabase.Refresh();
            }
        }

        [UnityTest]
        public IEnumerator Template_CopiedIntoProject_CompilesAndIsRecognizedByDDrive()
        {
            var source = TemplateRoot;
            if (!Directory.Exists(source))
            {
                Assert.Ignore("雛形(Samples~/ExtensionTemplate)がこのパッケージに無い: " + source);
            }

            RemoveCopy();
            Directory.CreateDirectory("Assets/Tests/DDriveTemp");
            CopyDirectory(source, Dest);
            AssetDatabase.Refresh();

            // コンパイルが通る(失敗すると RecompileScripts がテストを失敗させる)。ドメインリロードを跨ぐ。
            yield return new RecompileScripts(true);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToList();
            Assert.Contains("Sample.Extension.Runtime", assemblies, "Runtime の asmdef がコンパイルされた");
            Assert.Contains("Sample.Extension.Editor", assemblies, "Editor の asmdef がコンパイルされた");

            // 種別: 接頭辞・フォルダ・カタログ名。
            AssetKindRegistry.Refresh();
            Assert.IsTrue(AssetKindRegistry.TryGet("Sample", out var kind), "IAssetKindProvider が発見される");
            Assert.AreEqual("SampleData", kind.DataType.Name);
            Assert.AreEqual("SMP", kind.Prefix);
            Assert.AreEqual("SampleCatalog", kind.CatalogName);
            Assert.AreEqual(DDrive.Foundation.Data.LoadMode.Preload, kind.DefaultLoad);

            // module / provider。
            ModuleRegistry.Refresh();
            Assert.IsTrue(ModuleRegistry.All.Any(m => m.GetType().FullName == "SampleExtension.SampleModule"), "IDDriveModule が発見される");
            ExtensionProviders.Refresh();
            Assert.IsTrue(ExtensionProviders.TryGetSample("Sample", out _), "ISampleProvider");
            Assert.IsTrue(ExtensionProviders.TryGetPreviewScene("Sample", out _), "IPreviewSceneProvider");
            Assert.IsTrue(ExtensionProviders.TryGetManualPage("sample-editor", out var manual, out _), "IManualPageProvider");
            StringAssert.Contains("Documentation~/sample-editor.md", manual);

            // 窓: [DataEditor] が登録され、Order 0 の UxEditorWindow<T> 派生。
            var dataType = kind.DataType;
            Assert.IsTrue(DDrive.Editor.Inspector.DataEditorRegistry.TryGetPrimary(dataType, out var primary), "[DataEditor] が登録される");
            Assert.AreEqual(0, primary.Order);
            Assert.AreEqual("SampleEditor", primary.WindowType.Name);

            // 後片付け: 消してもう一度コンパイルする(他のテストにコピーを残さない)。
            RemoveCopy();
            AssetDatabase.Refresh();
            yield return new RecompileScripts(true);

            Assert.IsFalse(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Sample.Extension.Runtime"), "コピーを消したので雛形のアセンブリも無い");
            AssetKindRegistry.Refresh();
            ModuleRegistry.Refresh();
            ExtensionProviders.Refresh();
        }
    }
}
