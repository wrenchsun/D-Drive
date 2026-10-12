using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Inspector;
using DDrive.Editor.Materials;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Material;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // 2026-09-14 — AssetBrowser の一覧をダブルクリック(または Enter)したときの
    // 「Data 型 → 開くエディタ型」の解決(DataEditorRegistry.TryGetPrimary / OpenDefault、[09_editor_tools.md] §1)。
    // AssetBrowserWindow.OnItemsChosen はこのヘルパーへ委譲しているだけなので、
    // ウィンドウ内の ListView を操作せずヘルパーそのものを検証する。
    public class AssetBrowserDoubleClickTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempGameData_DblClick";

        // 専用エディタを一切持たない Data 型(候補なしケース用)。DDrive.Tests アセンブリなので
        // DataEditorRegistryTests の「全 Data 型はエディタを持つべき」検査の対象にはならない。
        private sealed class NoEditorData : AssetDataBase
        {
        }

        // 新 VfxEditor は internal(Editor/Ux の方針)なので型名で引く。
        private static System.Type NewVfxEditorType => typeof(VfxEditorWindow).Assembly.GetType("DDrive.Editor.Vfx.VfxEditor", true);
        private static System.Type NewMaterialEditorType => typeof(VfxEditorWindow).Assembly.GetType("DDrive.Editor.Materials.MaterialEditor", true);

        [TearDown]
        public void TearDown()
        {
            // 開いたままのテスト用ウィンドウを必ず閉じる(preview-via-scene の方針どおり残骸を残さない)。
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window is VfxEditorWindow or MaterialEditorWindow || window.GetType() == NewVfxEditorType || window.GetType() == NewMaterialEditorType)
                {
                    window.Close();
                }
            }

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); } // Addressables 設定の dirty を後続テストに持ち越さない
            }
        }

        [Test]
        public void TryGetPrimary_SingleCandidate_ResolvesToThatWindow()
        {
            // VfxData の既定は新 VfxEditor(Order 0)。旧 VfxEditorWindow は "(旧)" Order 100 の第 2 候補。
            Assert.IsTrue(DataEditorRegistry.TryGetPrimary(typeof(VfxData), out var primary));
            Assert.AreEqual(NewVfxEditorType, primary.WindowType);
        }

        [Test]
        public void TryGetPrimary_MultipleCandidates_ResolvesToOrderMinimumAsPrimary()
        {
            // MaterialData には新 MaterialEditor(Order 0) / MaterialThumbnailWindow(Order=5) /
            // MaterialConvertWindow(Order=10) / (旧) MaterialEditorWindow(Order=100) が付いている。主エディタは Order 最小の
            // 新 MaterialEditor(= Inspector の「エディターで開く」列の先頭ボタンと同じ)。
            var entries = DataEditorRegistry.GetEntries(typeof(MaterialData));
            Assert.Greater(entries.Count, 1, "このテストは MaterialData に複数の [DataEditor] が付いていることが前提です");

            Assert.IsTrue(DataEditorRegistry.TryGetPrimary(typeof(MaterialData), out var primary));
            Assert.AreEqual(NewMaterialEditorType, primary.WindowType);
            Assert.AreEqual(entries[0].WindowType, primary.WindowType, "TryGetPrimary は GetEntries の先頭と一致するはず");
        }

        [Test]
        public void TryGetPrimary_NoCandidate_ReturnsFalse()
        {
            Assert.IsFalse(DataEditorRegistry.TryGetPrimary(typeof(NoEditorData), out var primary));
            Assert.IsNull(primary);
        }

        [Test]
        public void TryGetPrimary_NullType_ReturnsFalse()
        {
            Assert.IsFalse(DataEditorRegistry.TryGetPrimary(null, out var primary));
            Assert.IsNull(primary);
        }

        [Test]
        public void OpenDefault_NoCandidateData_ReturnsFalse_AndDoesNotThrow()
        {
            var data = ScriptableObject.CreateInstance<NoEditorData>();
            try
            {
                Assert.IsFalse(DataEditorRegistry.OpenDefault(data));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void OpenDefault_NullData_ReturnsFalse()
        {
            Assert.IsFalse(DataEditorRegistry.OpenDefault(null));
        }

        // [48_p11_install_test_2026-09-20.md] フォローアップ「-nographics 起因の 14 件」— `EditorWindow.GetWindow<T>()`
        // による実ウィンドウ生成は `-batchmode -nographics`(この開発リポジトリの `Tools/CI/run-ci.cmd` を含む)
        // では `No graphic device is available` で失敗する(D-Drive/持ち込み先固有ではない Unity の制約。
        // 実際に `run-ci.cmd` 相当のバッチ実行で再現することを確認した)。
        [Test]
        [Category("RequiresGraphics")]
        public void OpenDefault_SingleCandidate_OpensThatWindow_WithAssetAsTarget()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();

            var asset = AssetCreationService.Create(typeof(VfxData), AssetType.Vfx, "TestVfx", "Category", "DblClickVfx", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            Assert.IsTrue(DataEditorRegistry.OpenDefault(asset));

            var window = EditorWindow.GetWindow(NewVfxEditorType);
            Assert.IsNotNull(window);

            var targetProp = NewVfxEditorType.GetProperty("Target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(targetProp, "VfxEditor.Target が見つかりません(実装が変わった場合はテストを追従させてください)");
            Assert.AreSame(asset, targetProp.GetValue(window));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void OpenDefault_MultipleCandidates_OpensPrimaryWindow_NotSecondaryTools()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();

            var asset = AssetCreationService.Create(typeof(MaterialData), AssetType.Material, "TestMaterial", "Category", "DblClickMaterial", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            Assert.IsTrue(DataEditorRegistry.OpenDefault(asset));

            var window = EditorWindow.GetWindow(NewMaterialEditorType);
            Assert.IsNotNull(window);
        }
    }
}
