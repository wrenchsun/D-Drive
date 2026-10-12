using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Inspector;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-4 / D-10: 新 CanvasEditor(Editor/Canvas)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(CanvasSample = HUD 相当: 要素 15 + 埋め込み 3 + 配線 7)を一時フォルダに作って確かめる(実データは触らない)。
    public class CanvasEditorUxTests
    {
        private const string TmpFolder = "Assets/__CanvasEditorUxTestTmp";
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type EditorType => EditorAssembly.GetType("DDrive.Editor.CanvasTool.CanvasEditor", true);
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private object _sample;
        private CanvasData _parent;
        private EditorWindow _window;

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                EditorUtility.ClearDirty(_parent);
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }

            if (AssetDatabase.IsValidFolder(TmpFolder))
            {
                AssetDatabase.DeleteAsset(TmpFolder);
            }
        }

        private void BuildSampleAndWindow()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.CanvasTool.CanvasSample", true);
            _sample = sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder });
            _parent = (CanvasData)_sample.GetType().GetField("Parent").GetValue(_sample);
            _window = (EditorWindow)ScriptableObject.CreateInstance(EditorType);
            var baseType = EditorType.BaseType;
            baseType.GetField("_target", Inst).SetValue(_window, _parent);
            // 値の変更イベント(ChangeEvent)・ボタンの操作は panel が無いと配送されないので、実際にウィンドウを出す(CreateGUI は Show が呼ぶ)。
            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                baseType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }
        }

        private object Field(string name) => EditorType.GetField(name, Inst).GetValue(_window);

        private static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        // ── 欄の表 ──

        [Test]
        public void CanvasFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var guideType = EditorAssembly.GetType("DDrive.Editor.CanvasTool.CanvasFieldGuide", true);
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var inGuide = new HashSet<string>();
            var tiers = new Dictionary<string, int>();
            foreach (var e in (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance))
            {
                var field = (string)e.GetType().GetField("Field").GetValue(e);
                inGuide.Add(field);
                tiers[field] = (int)e.GetType().GetField("Tier").GetValue(e);
            }

            var data = ScriptableObject.CreateInstance<CanvasData>();
            try
            {
                var so = new SerializedObject(data);
                var readOnly = new HashSet<string> { "Id", "Version", "Author", "UpdatedAt", "m_Script" };
                var missing = new List<string>();
                var it = so.GetIterator();
                var enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (!readOnly.Contains(it.name) && !inGuide.Contains(it.name))
                    {
                        missing.Add(it.name);
                    }
                }

                Assert.IsEmpty(missing, "CanvasFieldGuide に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が CanvasData にありません");
                }

                // 必須の段 = Validator が Error にする欄(Prefab)。
                Assert.AreEqual(0, tiers[nameof(CanvasData.Prefab)], "Prefab は必須");
                Assert.AreNotEqual(0, tiers[nameof(CanvasData.Layer)]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void CanvasData_NewEditorIsPrimary_OldIsSecondWithOrder100()
        {
            var entries = new List<DataEditorRegistry.Entry>(DataEditorRegistry.GetEntries(typeof(CanvasData)));
            Assert.GreaterOrEqual(entries.Count, 2);
            Assert.AreEqual(EditorType, entries[0].WindowType, "新が第 1 候補");
            Assert.AreEqual(0, entries[0].Order);
            Assert.AreEqual(typeof(CanvasEditorWindow), entries[1].WindowType, "旧が第 2 候補");
            Assert.AreEqual(100, entries[1].Order);
            StringAssert.StartsWith("(旧)", entries[1].Label);
        }

        // ── 実運用規模のサンプルで開く ──

        [Test]
        public void SampleHud_BuildsHeroCardsAndFooter_WithoutSpeedOrRepeat()
        {
            BuildSampleAndWindow();
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNull(root.Q(className: "ux-bar__speed"), "速度は出さない");
            // 出す / 要素の動き / 配線 / 埋め込み / Navigation / 音・イベント / 管理情報 / その他の検証
            Assert.AreEqual(8, root.Query(className: "ux-card").ToList().Count, "カード数");
        }

        [Test]
        public void SampleHud_AllFoldoutsAreClosedByDefault()
        {
            BuildSampleAndWindow();
            var root = _window.rootVisualElement;
            EditorType.GetMethod("CollapseFoldouts", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root });
            var folds = root.Query<Foldout>().ToList();
            Assert.Greater(folds.Count, 0);
            var open = new List<string>();
            foreach (var f in folds)
            {
                if (f.value)
                {
                    open.Add(f.text);
                }
            }

            Assert.IsEmpty(open, "既定で開いている Foldout: " + string.Join(", ", open));
            Assert.AreEqual(4, root.Query<Foldout>(className: "ux-sec__fold").ToList().Count, "一覧の欄(要素の動き・配線・埋め込み・Navigation)");
        }

        [Test]
        public void SampleHud_FxList_IsOneRowPerElement_WithCountAndFilters()
        {
            BuildSampleAndWindow();
            var fxList = Field("_fxList");
            fxList.GetType().GetMethod("Render").Invoke(fxList, null);
            var rows = ((VisualElement)fxList).Query(className: "ux-fxrow").ToList();
            // 要素 15 行 + 埋め込みの 1 行要約 3 行(子の中身は展開しない)。
            Assert.AreEqual(18, rows.Count, "要素 1 つ = 1 行 + 埋め込み 1 つ = 1 行");
            var count = _window.rootVisualElement.Query<Label>(className: "ux-itemlist__count").ToList()[0];
            StringAssert.Contains("15 要素中 設定済み", count.text);
            // 設定済みだけ → 件数が減る。
            var chips = _window.rootVisualElement.Query<Button>(className: "ux-filter").ToList();
            Click(chips.Find(b => b.text == "設定済み"));
            var shown = (int)fxList.GetType().GetProperty("ShownCount").GetValue(fxList);
            Assert.Less(shown, 18);
            Assert.Greater(shown, 0);
        }

        [Test]
        public void SampleHud_FxList_MoreThan20_UsesListView()
        {
            BuildSampleAndWindow();
            var list = new List<ElementFx>(_parent.ElementEffects);
            for (var i = 0; i < 12; i++)
            {
                list.Add(new ElementFx { ElementPath = "Panel/Extra" + i });
            }

            _parent.ElementEffects = list.ToArray();
            EditorType.GetMethod("SyncSections", Inst).Invoke(_window, new object[] { false });
            var lvs = _window.rootVisualElement.Query<ListView>(className: "ux-itemlist__lv").ToList();
            Assert.AreEqual(1, lvs.Count, "20 行を超えたら ListView(仮想化)");
        }

        [Test]
        public void SampleHud_ChangeOneFxRow_AndUndo()
        {
            BuildSampleAndWindow();
            ((HashSet<string>)Field("_fxOpen")).Add("fx:Panel/Title");
            var fxList = Field("_fxList");
            fxList.GetType().GetMethod("Render").Invoke(fxList, null);
            var popups = _window.rootVisualElement.Query<PopupField<string>>(className: "ux-fxphase__popup").ToList();
            Assert.AreEqual(3, popups.Count, "展開した行の Appear / Idle / Disappear");
            var index = Array.FindIndex(_parent.ElementEffects, e => e.ElementPath == "Panel/Title");
            Assert.AreEqual(UiPreset.None, _parent.ElementEffects[index].AppearPreset.Preset);
            Undo.IncrementCurrentGroup();
            popups[0].value = "FadeIn";
            Assert.AreEqual(UiPreset.FadeIn, _parent.ElementEffects[index].AppearPreset.Preset);
            Undo.PerformUndo();
            Assert.AreEqual(UiPreset.None, _parent.ElementEffects[index].AppearPreset.Preset, "Undo で戻る");
        }

        [Test]
        public void SampleHud_AddAndRemoveOneWire()
        {
            BuildSampleAndWindow();
            var before = _parent.Buttons.Length;
            var buttons = _window.rootVisualElement.Query<Button>().ToList();
            Click(buttons.Find(b => b.text == "＋ 配線"));
            Assert.AreEqual(before + 1, _parent.Buttons.Length, "配線 1 本を追加");
            var lines = _window.rootVisualElement.Query(className: "ux-wireline").ToList();
            Assert.Greater(lines.Count, 0, "追加した行のボタンは展開されて 1 配線 = 1 行");
            foreach (var line in lines)
            {
                var del = line.Query<Button>().ToList().Find(b => b.text == "削除");
                if (del != null && line.Q<EnumField>() != null)
                {
                    Click(del);
                    break;
                }
            }

            Assert.AreEqual(before, _parent.Buttons.Length, "配線 1 本を削除");
        }

        [Test]
        public void SampleHud_EmbeddedStartInactive_ToggleAndUndo()
        {
            BuildSampleAndWindow();
            ((HashSet<string>)Field("_embedOpen")).Add("embed:0");
            var embedList = Field("_embedList");
            embedList.GetType().GetMethod("Render").Invoke(embedList, null);
            var toggle = _window.rootVisualElement.Query<Toggle>().ToList().Find(t => t.text == "無効で始める");
            Assert.IsNotNull(toggle);
            Assert.IsFalse(_parent.EmbeddedCanvases[0].StartInactive);
            Undo.IncrementCurrentGroup();
            toggle.value = true;
            Assert.IsTrue(_parent.EmbeddedCanvases[0].StartInactive);
            Undo.PerformUndo();
            Assert.IsFalse(_parent.EmbeddedCanvases[0].StartInactive);
        }

        [Test]
        public void SampleHud_EmbeddedChild_IsOneLineWithOpenButton_NotExpanded()
        {
            BuildSampleAndWindow();
            var fxList = Field("_fxList");
            fxList.GetType().GetMethod("Render").Invoke(fxList, null);
            var opens = _window.rootVisualElement.Query<Button>().ToList().FindAll(b => b.text == "開く");
            Assert.GreaterOrEqual(opens.Count, 3, "埋め込み 3 つ = 「開く」");
            // 子の中身(子の ElementFx の行)は親の画面に展開しない: 子の要素名(BtnOk)の行は無い。
            foreach (var label in _window.rootVisualElement.Query<Label>(className: "ux-fxrow__name").ToList())
            {
                StringAssert.DoesNotContain("BtnOk", label.text);
            }
        }

        [Test]
        public void SampleHud_CollectNavigation_AddsSelectables()
        {
            BuildSampleAndWindow();
            _parent.Navigation = new NavNode[0];
            var buttons = _window.rootVisualElement.Query<Button>().ToList();
            Click(buttons.Find(b => b.text == "Selectable を自動収集"));
            Assert.Greater(_parent.Navigation.Length, 0);
        }

        [Test]
        public void SampleHud_ShowsFxRowFlag_WhenValidationHasIndexedProblem()
        {
            BuildSampleAndWindow();
            var list = new List<ElementFx>(_parent.ElementEffects) { new ElementFx { ElementPath = "Panel/Missing" } };
            _parent.ElementEffects = list.ToArray();
            EditorType.GetMethod("AfterDataWrite", Inst).Invoke(_window, null);
            var flags = (IDictionary)Field("_fxLineFlags");
            Assert.Greater(flags.Count, 0, "行ごとの ⚠ の印がある");
            var issues = (IDictionary)Field("_fxIssue");
            Assert.IsTrue(issues.Contains(list.Count - 1), "見つからない要素の行に印が付く");
        }
    }
}
