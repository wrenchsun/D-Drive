using System;
using System.Linq;
using System.Reflection;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Inspector;
using DDrive.Foundation.Data;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-16 / UX-17: Inspector の「エディターで開く」列(新 = 主ボタン・旧 = 折りたたみ「旧エディター」)、
    // Cutscene の Inspector(共通テーマ・導線)、AssetBrowser の空状態、Tuning の自動保存の既定(ON)。
    // 実ウィンドウは開かない(UI Toolkit の要素を組んで構造を見るだけ)ので RequiresGraphics は付けない。
    public class InspectorBrowserUxTests
    {
        private const string TuningAutoSaveKey = "DDrive.Ux.Tuning.AutoSave";
        private const string LegacyFoldKey = "DDrive.Inspector.LegacyEditorsFold";
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private sealed class NoEditorData : AssetDataBase
        {
        }

        private UnityEngine.Object _created;
        private UnityEditor.Editor _editor;
        private bool _oldFold;

        [SetUp]
        public void SetUp()
        {
            UxTestWindows.CloseAll();
            _oldFold = SessionState.GetBool(LegacyFoldKey, false);
            SessionState.SetBool(LegacyFoldKey, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_editor != null)
            {
                UnityEngine.Object.DestroyImmediate(_editor);
            }

            if (_created != null)
            {
                UnityEngine.Object.DestroyImmediate(_created);
            }

            SessionState.SetBool(LegacyFoldKey, _oldFold);
            UxTestWindows.CloseAll();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => UxTestWindows.Release();

        // パネルに載っていない要素にはイベントが届かないので、Button の clicked(Clickable のイベントのバッキングフィールド)を直接呼ぶ。
        private static void Click(Button button)
        {
            var clickable = button.clickable;
            var field = typeof(Clickable).GetField("clicked", Inst);
            Assert.IsNotNull(field, "Clickable.clicked のバッキングフィールドが見つかりません");
            ((Action)field.GetValue(clickable))?.Invoke();
        }

        // ── 「エディターで開く」列 ──

        [Test]
        public void Header_Build_NewEditorIsFilledPrimary_AndLegacyIsInsideClosedFold()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            _created = data;

            var root = DataEditorHeader.Build(data);
            Assert.IsTrue(root.ClassListContains("ux-root"), "共通テーマ(UxTheme)が当たっている");

            var main = root.Q("open-main");
            Assert.IsNotNull(main, "主ボタンの列");
            var mainButtons = main.Query<Button>().ToList();
            Assert.GreaterOrEqual(mainButtons.Count, 1);
            Assert.IsTrue(mainButtons[0].ClassListContains("ux-btn--primary"), "新エディター(Order 0)が主ボタン(塗り)");
            Assert.IsFalse(mainButtons.Any(b => b.text.Contains("(旧)")), "主ボタンの列に旧エディターは並ばない");

            var fold = root.Q("legacy-fold");
            Assert.IsNotNull(fold, "旧エディターの折りたたみがある");
            var legacyButtons = root.Q("legacy-fold-body").Query<Button>().ToList();
            Assert.GreaterOrEqual(legacyButtons.Count, 1);
            Assert.IsTrue(legacyButtons.All(b => b.text.Contains("(旧)")), "折りたたみの中は「(旧) 〜」だけ");
            Assert.IsFalse(legacyButtons.Any(b => b.ClassListContains("ux-btn--primary")), "旧は副ボタン(枠)で塗らない");
            Assert.IsTrue(root.Q("legacy-fold-body").ClassListContains("ux-hidden"), "既定は閉じている");
        }

        [Test]
        public void Header_Build_FoldHead_TogglesOpenAndClosed()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            _created = data;

            var root = DataEditorHeader.Build(data);
            var head = root.Q<Button>("legacy-fold-head");
            var body = root.Q("legacy-fold-body");
            Assert.IsTrue(head.text.StartsWith("▸"));

            Click(head);
            Assert.IsFalse(body.ClassListContains("ux-hidden"), "押すと開く");
            Assert.IsTrue(head.text.StartsWith("▾"));

            Click(head);
            Assert.IsTrue(body.ClassListContains("ux-hidden"), "もう一度押すと閉じる");
        }

        [Test]
        public void Header_Build_NoEditorType_HasNoButtons()
        {
            var data = ScriptableObject.CreateInstance<NoEditorData>();
            _created = data;

            var root = DataEditorHeader.Build(data);
            Assert.IsNull(root.Q("open-main"));
            Assert.IsNull(root.Q("legacy-fold"));
        }

        [Test]
        public void Header_Split_MaterialData_KeepsToolsAsSecondaryAndLegacyInFold()
        {
            var primary = new System.Collections.Generic.List<DataEditorRegistry.Entry>();
            var secondary = new System.Collections.Generic.List<DataEditorRegistry.Entry>();
            var legacy = new System.Collections.Generic.List<DataEditorRegistry.Entry>();
            var split = typeof(DataEditorHeader).GetMethod("Split", Stat);
            split.Invoke(null, new object[] { typeof(DDrive.Runtime.Material.MaterialData), primary, secondary, legacy });

            Assert.AreEqual(1, primary.Count);
            Assert.AreEqual(0, primary[0].Order);
            Assert.AreEqual(2, secondary.Count, "プレビューのポップアップ / シェーダー変換は副ボタン");
            Assert.IsTrue(secondary.All(e => e.Order > 0 && e.Order < DataEditorHeader.LegacyOrder));
            Assert.AreEqual(1, legacy.Count);
            Assert.IsTrue(legacy[0].Label.StartsWith("(旧)"));
        }

        // ── Cutscene の Inspector ──

        // IMGUI のまま(UI Toolkit 化しない。IMGUIContainer を含む Inspector を起動時に復元表示してクラッシュした実例があるため)。
        [Test]
        public void CutsceneInspector_StaysImgui_AndHasNoEditorWindowEntries()
        {
            Assert.AreEqual(0, DataEditorRegistry.GetEntries(typeof(CutsceneData)).Count, "CutsceneData は専用 EditorWindow を持たない(旧エディターへの導線も無い)");

            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created = data;
            _editor = UnityEditor.Editor.CreateEditor(data);
            Assert.IsNull(_editor.CreateInspectorGUI(), "OnInspectorGUI(IMGUI)を使う");
        }

        // ── AssetBrowser の空状態 ──

        [Test]
        public void AssetBrowser_EmptyMessage_NamesTheNewEditorsAndDistinguishesFilter()
        {
            var method = typeof(AssetBrowserWindow).GetMethod("EmptyMessage", Stat);
            Assert.IsNotNull(method);
            var none = (string)method.Invoke(null, new object[] { false });
            var filtered = (string)method.Invoke(null, new object[] { true });

            StringAssert.Contains("新規", none);
            StringAssert.Contains("Editor", none);
            StringAssert.DoesNotContain("(旧)", none);
            StringAssert.Contains("絞り込み", filtered);
            Assert.AreNotEqual(none, filtered);
        }

        // ── Tuning の自動保存の既定 ──

        [Test]
        public void TuningEditor_AutoSave_DefaultsToOn_WhenPrefIsUnset()
        {
            var windowType = typeof(DDrive.Editor.Menu.DDriveMenu).Assembly.GetType("DDrive.Editor.Tuning.TuningEditor", true);
            var had = EditorPrefs.HasKey(TuningAutoSaveKey);
            var old = had ? EditorPrefs.GetBool(TuningAutoSaveKey) : false;
            EditorPrefs.DeleteKey(TuningAutoSaveKey);

            var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            try
            {
                var prop = windowType.GetProperty("AutoSave", Inst);
                Assert.IsTrue((bool)prop.GetValue(window), "Tuning は自動保存が既定 ON(旧ウィンドウは編集のたびに保存していたため)");
                Assert.IsTrue((bool)windowType.GetField("AutoSaveDefault", Stat).GetValue(null));

                prop.SetValue(window, false);
                Assert.IsFalse((bool)prop.GetValue(window), "明示的に OFF にした値は保たれる");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                if (had)
                {
                    EditorPrefs.SetBool(TuningAutoSaveKey, old);
                }
                else
                {
                    EditorPrefs.DeleteKey(TuningAutoSaveKey);
                }
            }
        }

        [Test]
        public void VfxEditor_AutoSave_StaysOff_ByDefault()
        {
            const string key = "DDrive.Ux.Vfx.AutoSave";
            var windowType = typeof(DDrive.Editor.Menu.DDriveMenu).Assembly.GetType("DDrive.Editor.Vfx.VfxEditor", true);
            var had = EditorPrefs.HasKey(key);
            var old = had ? EditorPrefs.GetBool(key) : false;
            EditorPrefs.DeleteKey(key);

            var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            try
            {
                Assert.IsFalse((bool)windowType.GetProperty("AutoSave", Inst).GetValue(window), "Tuning 以外の既定は OFF のまま");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                if (had)
                {
                    EditorPrefs.SetBool(key, old);
                }
            }
        }
    }
}
