using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Inspector;
using DDrive.Editor.Preview;
using DDrive.Editor.Ui;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-13: 新 ButtonSkinEditor / SliderSkinEditor / SliderEditor(Editor/Ui)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(SkinSample。全部入りの Button Skin / Slider Skin・既定のままの Skin・SE 3・Tween 1)を一時フォルダに作って確かめる(実データは触らない)。
    public class SkinEditorUxTests
    {
        private const string TmpFolder = "Assets/__SkinEditorUxTestTmp";
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type ButtonType => EditorAssembly.GetType("DDrive.Editor.Ui.ButtonSkinEditor", true);
        private static Type SliderSkinType => EditorAssembly.GetType("DDrive.Editor.Ui.SliderSkinEditor", true);
        private static Type SliderType => EditorAssembly.GetType("DDrive.Editor.Ui.SliderEditor", true);
        private static Type RowType => EditorAssembly.GetType("DDrive.Editor.Ui.SkinStateRow", true);
        private static Type PartsType => EditorAssembly.GetType("DDrive.Editor.Ui.SkinParts", true);
        private static Type TweenEditorType => EditorAssembly.GetType("DDrive.Editor.Ui.UiTweenEditor", true);

        private object _sample;
        private EditorWindow _window;
        private readonly List<GameObject> _objects = new List<GameObject>();

        [OneTimeSetUp]
        public void BuildSampleOnce()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.Ui.SkinSample", true);
            _sample = sample.GetMethod("Build", Stat).Invoke(null, new object[] { TmpFolder });
        }

        [OneTimeTearDown]
        public void DeleteSample()
        {
            if (AssetDatabase.IsValidFolder(TmpFolder))
            {
                AssetDatabase.DeleteAsset(TmpFolder);
            }
        }

        [TearDown]
        public void TearDown()
        {
            ClearDirty();
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w == null)
                {
                    continue;
                }

                var t = w.GetType();
                if (t == ButtonType || t == SliderSkinType || t == SliderType || t == TweenEditorType
                    || t == typeof(ButtonSkinEditorWindow) || t == typeof(SliderSkinEditorWindow) || t == typeof(SliderEditorWindow))
                {
                    UnityEngine.Object.DestroyImmediate(w);
                }
            }

            foreach (var go in _objects)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _objects.Clear();
            _window = null;
            foreach (var name in new[] { "[D-Drive] Button Skin Editor Preview", "[D-Drive] Slider Skin Editor Preview", "[D-Drive] Slider Editor Preview 2" })
            {
                EditorPreviewRoots.DestroyAll(name);
            }

            ClearDirty();
        }

        // 実体を書き換えるテスト(↺ → Undo など)のあとで、未保存の確認ダイアログが出ないようにする。
        private void ClearDirty()
        {
            foreach (var d in new UnityEngine.Object[] { Button, ButtonEmpty, Slider, SliderEmpty, Part<UiTweenData>("Tween") })
            {
                if (d != null)
                {
                    EditorUtility.ClearDirty(d);
                }
            }
        }

        // ── 部品 ──

        private T Part<T>(string name) where T : class => (T)_sample.GetType().GetField(name).GetValue(_sample);

        private ButtonSkinData Button => Part<ButtonSkinData>("Button");

        private ButtonSkinData ButtonEmpty => Part<ButtonSkinData>("ButtonEmpty");

        private SliderSkinData Slider => Part<SliderSkinData>("Slider");

        private SliderSkinData SliderEmpty => Part<SliderSkinData>("SliderEmpty");

        private void OpenWindow(Type type, UnityEngine.Object target)
        {
            _window = (EditorWindow)ScriptableObject.CreateInstance(type);
            if (target != null)
            {
                type.BaseType.GetField("_target", Inst).SetValue(_window, target);
            }

            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                (type.GetMethod("CreateGUI", Inst) ?? type.BaseType.GetMethod("CreateGUI", Inst)).Invoke(_window, null);
            }
        }

        private object Prop(string name) => _window.GetType().GetProperty(name, Inst).GetValue(_window);

        private VisualElement StateList => (VisualElement)Prop("StateList");

        private IList Rows => (IList)StateList.GetType().GetProperty("Rows").GetValue(StateList);

        private VisualElement RowOf(string field)
        {
            foreach (var r in Rows)
            {
                var part = RowType.GetProperty("Part").GetValue(r);
                if ((string)part.GetType().GetField("Field").GetValue(part) == field)
                {
                    return (VisualElement)r;
                }
            }

            Assert.Fail("行 " + field + " がありません");
            return null;
        }

        // 見えているカードの見出し(検証の結びつかなかった結果を出す「その他の検証」は、結果があるときだけ出るので除く)。
        private static List<string> CardTitles(VisualElement root)
        {
            var titles = new List<string>();
            foreach (var l in root.Query<Label>(className: "ux-card__title").ToList())
            {
                if (l.text != "その他の検証")
                {
                    titles.Add(l.text);
                }
            }

            return titles;
        }

        private static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        private string CountText() => StateList.Q<Label>(className: "ux-itemlist__count").text;

        private static IEnumerable<string> GuideFields(string guideTypeName)
        {
            var guideType = EditorAssembly.GetType(guideTypeName, true);
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var list = new List<string>();
            foreach (var e in (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance))
            {
                list.Add((string)e.GetType().GetField("Field").GetValue(e));
            }

            return list;
        }

        private static void AssertGuideCovers(string guideTypeName, ScriptableObject data)
        {
            var inGuide = new HashSet<string>(GuideFields(guideTypeName));
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

            Assert.IsEmpty(missing, guideTypeName + " に無い欄: " + string.Join(", ", missing));
            foreach (var field in inGuide)
            {
                if (!field.StartsWith("(", StringComparison.Ordinal))
                {
                    Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が " + data.GetType().Name + " にありません");
                }
            }
        }

        // ── 欄の表 ──

        [Test]
        public void ButtonFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var d = ScriptableObject.CreateInstance<ButtonSkinData>();
            try
            {
                AssertGuideCovers("DDrive.Editor.Ui.ButtonSkinFieldGuide", d);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(d);
            }
        }

        [Test]
        public void SliderSkinFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var d = ScriptableObject.CreateInstance<SliderSkinData>();
            try
            {
                AssertGuideCovers("DDrive.Editor.Ui.SliderSkinFieldGuide", d);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(d);
            }
        }

        // ── 登録表([DataEditor]) ──

        [Test]
        public void Registry_NewEditorsFirst_OldOnesLabelledAndLast()
        {
            var button = DataEditorRegistry.GetEntries(typeof(ButtonSkinData));
            Assert.AreEqual(ButtonType, button[0].WindowType, "先頭は新エディター");
            Assert.AreEqual(0, button[0].Order);
            Assert.AreEqual(typeof(ButtonSkinEditorWindow), button[button.Count - 1].WindowType);
            Assert.AreEqual(100, button[button.Count - 1].Order);
            StringAssert.StartsWith("(旧)", button[button.Count - 1].Label);

            var slider = DataEditorRegistry.GetEntries(typeof(SliderSkinData));
            Assert.AreEqual(SliderSkinType, slider[0].WindowType);
            Assert.AreEqual(0, slider[0].Order);
            Assert.AreEqual(SliderType, slider[1].WindowType, "Slider Editor は Skin Editor の次(Order 5)");
            Assert.AreEqual(5, slider[1].Order);
            Assert.AreEqual(typeof(SliderSkinEditorWindow), slider[2].WindowType);
            Assert.AreEqual(typeof(SliderEditorWindow), slider[3].WindowType);
            foreach (var old in new[] { slider[2], slider[3] })
            {
                StringAssert.StartsWith("(旧)", old.Label);
                Assert.GreaterOrEqual(old.Order, 100);
            }
        }

        [Test]
        public void OldWindows_StillOpen_WithOldTitles()
        {
            ButtonSkinEditorWindow.Open(null);
            SliderSkinEditorWindow.Open(null);
            SliderEditorWindow.Open((UiSlider)null);
            foreach (var w in new EditorWindow[]
            {
                Resources.FindObjectsOfTypeAll<ButtonSkinEditorWindow>()[0],
                Resources.FindObjectsOfTypeAll<SliderSkinEditorWindow>()[0],
                Resources.FindObjectsOfTypeAll<SliderEditorWindow>()[0],
            })
            {
                StringAssert.StartsWith("(旧)", w.titleContent.text);
            }
        }

        // ── サンプル・要約 ──

        [Test]
        public void Sample_HasEveryStateConfigured_AndPartsAndSes()
        {
            Assert.IsTrue(Button.Hover.Tint != Color.white && Button.Pressed.EnterTween.IsValid && Button.Selected.AnimFrames.Length == 4 && Button.Locked.ScrollSpeed != Vector2.zero);
            Assert.IsTrue(Button.Hover.EnterPreset.Preset != UiPreset.None);
            Assert.IsTrue(Button.HoverSe.IsValid && Button.ClickSe.IsValid && Button.DeniedSe.IsValid);
            Assert.IsNotNull(Button.ScrollMaterial);
            Assert.AreEqual(4, new[] { Slider.Track, Slider.Fill, Slider.Handle, Slider.DelayFill }.Length);
            Assert.IsTrue(Slider.Handle.OverrideSprite != null && Slider.Fill.Tint.a > 0f);
            Assert.IsTrue(Slider.GrabSe.IsValid && Slider.ReleaseSe.IsValid && Slider.NotchSe.IsValid && Slider.LimitSe.IsValid && Slider.DeniedSe.IsValid);
        }

        [Test]
        public void Summary_StatesAndParts_ReadAsOneLine()
        {
            var summary = PartsType.GetMethod("Summary", Stat);
            string Sum(StateVisual v, bool isPart) => (string)summary.Invoke(null, new object[] { v, isPart, null });

            StringAssert.Contains("既定のまま", Sum(StateVisual.Default, false));
            StringAssert.Contains("既定のまま", Sum(default(StateVisual), true));

            var hover = Button.Hover;
            var text = Sum(hover, false);
            StringAssert.Contains("×1.06", text);
            StringAssert.Contains("PunchScale", text);

            var selected = Sum(Button.Selected, false);
            StringAssert.Contains("画像 ", selected);
            StringAssert.Contains("コマ 4 枚 @8fps", selected);

            StringAssert.Contains("スクロール (0.2, 0)", Sum(Button.Locked, false));
            StringAssert.Contains("α0.6", Sum(Button.Disabled, false));
            StringAssert.Contains("画像 ", Sum(Slider.Handle, true));
            Assert.IsFalse(Sum(Slider.Fill, true).Contains("演出"), "パーツは演出を持たない");
        }

        // ── 土台(ヒーロー・カード・固定バー) ──

        [Test]
        public void ButtonEditor_BuildsHeroCardsAndFooter_WithoutSpeed()
        {
            OpenWindow(ButtonType, Button);
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNull(root.Q(className: "ux-bar__speed"), "Skin は速度を持たない");
            var titles = CardTitles(root);
            CollectionAssert.AreEqual(new[] { "状態ごとの見た目", "音(SE)", "押せる範囲", "確認する", "管理情報" }, titles);
        }

        [Test]
        public void SliderSkinEditor_BuildsPresetsStatesPartsSeHitAndCheckCards()
        {
            OpenWindow(SliderSkinType, Slider);
            var titles = CardTitles(_window.rootVisualElement);
            CollectionAssert.AreEqual(new[] { "動きの雛形(確認用)", "状態・パーツの見た目", "パーツと操作", "音と触覚", "押せる範囲", "確認する", "管理情報" }, titles);
        }

        // ── 状態の一覧(1 状態 = 1 行) ──

        [Test]
        public void Button_StateList_IsOneRowPerState_WithCountAndNoListView()
        {
            OpenWindow(ButtonType, Button);
            Assert.AreEqual(6, Rows.Count, "6 状態 = 6 行");
            Assert.IsNull(StateList.Q<ListView>(), "6 行は ListView にしない");
            StringAssert.Contains("6 状態中 設定済み 5", CountText(), "Normal だけ既定のまま");
            foreach (var r in Rows)
            {
                var row = (VisualElement)r;
                Assert.IsNotNull(row.Q(className: "ux-sk-sum"), "要約の行");
                Assert.IsNotEmpty((string)RowType.GetProperty("SummaryText").GetValue(r));
                Assert.IsFalse((bool)RowType.GetProperty("IsOpen").GetValue(r), "初期は ⚙ を閉じている(展開はその行だけ)");
            }
        }

        [Test]
        public void Slider_StateList_HasTenRows_StatesAndParts_WithKindChips()
        {
            OpenWindow(SliderSkinType, Slider);
            Assert.AreEqual(10, Rows.Count, "状態 6 + パーツ 4");
            StringAssert.Contains("10 状態・パーツ中", CountText());
            var chips = StateList.Query<Button>(className: "ux-filter").ToList();
            var names = new List<string>();
            foreach (var c in chips)
            {
                names.Add(c.text);
            }

            CollectionAssert.Contains(names, "状態");
            CollectionAssert.Contains(names, "パーツ");
            // パーツの行には ▶ が無い(状態ごとの演出は部品本体に掛かる)。
            var track = RowOf("Track");
            Assert.AreEqual(2, track.Query<Button>(className: "ux-btn--icon").ToList().Count, "パーツは ↺ と ⚙ だけ");
            var hover = RowOf("Hover");
            Assert.AreEqual(3, hover.Query<Button>(className: "ux-btn--icon").ToList().Count, "状態は ▶ ↺ ⚙");
        }

        [Test]
        public void EmptySkin_ShowsEverythingAsDefault()
        {
            OpenWindow(ButtonType, ButtonEmpty);
            StringAssert.Contains("6 状態中 設定済み 0", CountText());
            foreach (var r in Rows)
            {
                StringAssert.Contains("既定のまま", (string)RowType.GetProperty("SummaryText").GetValue(r));
            }
        }

        [Test]
        public void StateRow_ResetToDefault_ThenUndo_RestoresTheState()
        {
            OpenWindow(ButtonType, Button);
            var before = JsonUtility.ToJson(Button.Hover);
            Undo.IncrementCurrentGroup();
            var reset = RowOf("Hover").Query<Button>(className: "ux-btn--icon").ToList()[1];
            Click(reset);
            Assert.AreNotEqual(before, JsonUtility.ToJson(Button.Hover), "↺ で既定に戻る");
            Undo.PerformUndo();
            Assert.AreEqual(before, JsonUtility.ToJson(Button.Hover), "Undo 1 回で元に戻る");
        }

        [Test]
        public void StateRow_Gear_OpensOnlyThatRow_WithTheStateFields()
        {
            OpenWindow(ButtonType, Button);
            var pressed = RowOf("Pressed");
            RowType.GetMethod("SetOpen").Invoke(pressed, new object[] { true, true });
            Assert.IsTrue((bool)RowType.GetProperty("IsOpen").GetValue(pressed));
            Assert.IsFalse((bool)RowType.GetProperty("IsOpen").GetValue(RowOf("Hover")), "ほかの行は閉じたまま");
            Assert.GreaterOrEqual(pressed.Query<PropertyField>().ToList().Count, 8, "StateVisual の欄(演出・色・拡大率・画像・コマ・スクロール)");
            var tween = pressed.Query<Button>().ToList().Find(b => b.text.Contains("Tween Editor"));
            Assert.IsNotNull(tween);
            Assert.IsTrue(tween.enabledSelf, "Tween が設定されている状態は「開く」が押せる");
        }

        [Test]
        public void StateRow_TweenEditorButton_OpensTheNewUiTweenEditor()
        {
            OpenWindow(ButtonType, Button);
            var pressed = RowOf("Pressed");
            RowType.GetMethod("SetOpen").Invoke(pressed, new object[] { true, true });
            var tween = pressed.Query<Button>().ToList().Find(b => b.text.Contains("Tween Editor"));
            foreach (var old in Resources.FindObjectsOfTypeAll(TweenEditorType))
            {
                UnityEngine.Object.DestroyImmediate(old);
            }

            Click(tween);
            var opened = Resources.FindObjectsOfTypeAll(TweenEditorType);
            Assert.AreEqual(1, opened.Length, "新 UiTweenEditor が開く");
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<UiTweenEditorWindow>().Length, "旧は開かない");
            Assert.AreEqual(Part<UiTweenData>("Tween"), TweenEditorType.BaseType.GetField("_target", Inst).GetValue(opened[0]));
        }

        // ── 確認する・SE の試聴 ──

        [Test]
        public void CheckSection_IsCheckOnly_NoStateBlocksOrPropertyFields()
        {
            OpenWindow(ButtonType, Button);
            var check = (VisualElement)Prop("Check");
            Assert.IsNotNull(check);
            Assert.IsNotNull(check.Q<PopupField<string>>(), "状態遷移の選択");
            Assert.AreEqual(0, check.Query<PropertyField>().ToList().Count, "設定欄は新エディターの行が持つ");
            Assert.IsNotNull(check.Q<Toggle>(), "当たり判定を表示 / ループ / SE も鳴らす");
        }

        [Test]
        public void SeRows_HaveAuditionButtons_EnabledOnlyWhenTheSeExists()
        {
            OpenWindow(ButtonType, Button);
            ButtonType.BaseType.GetMethod("RefreshAll", Inst).Invoke(_window, new object[] { true }); // 行の ▶ の押せる / 押せないを最新にする(窓では 150ms ごとの更新が行う)
            var rows = _window.rootVisualElement.Query<VisualElement>(className: "ux-sk-se").ToList();
            Assert.AreEqual(4, rows.Count, "Hover / Click / LongPress / Denied");
            var enabled = 0;
            foreach (var r in rows)
            {
                var btns = r.Query<Button>(className: "ux-btn--icon").ToList();
                Assert.AreEqual(2, btns.Count, "▶ と ■");
                if (btns[0].enabledSelf)
                {
                    enabled++;
                }
            }

            Assert.AreEqual(3, enabled, "SE が設定済みの 3 つだけ ▶ が押せる(LongPress は未設定)");
        }

        [Test]
        public void Transition_PlaysAndStops_ThroughTheCheckSection()
        {
            OpenWindow(ButtonType, Button);
            ButtonType.GetMethod("PlaceForTest", Inst).Invoke(_window, null);
            var check = Prop("Check");
            var busy = check.GetType().GetProperty("IsBusy", Inst);
            Assert.IsFalse((bool)busy.GetValue(check));
            check.GetType().GetMethod("PlaySequence", Inst).Invoke(check, new object[] { int.MaxValue });
            Assert.IsTrue((bool)busy.GetValue(check), "固定バーの ▶ = 「全状態を順に」の遷移");
            check.GetType().GetMethod("StopAll").Invoke(check, null);
            Assert.IsFalse((bool)busy.GetValue(check));
        }

        // ── プレビュー部品 ──

        [Test]
        public void Button_Place_CreatesPreviewWithTheSkin_AndRemoveOnClose()
        {
            OpenWindow(ButtonType, Button);
            ButtonType.GetMethod("PlaceForTest", Inst).Invoke(_window, null);
            var preview = Prop("PreviewButton") as UiButton;
            Assert.IsNotNull(preview, "実 UiButton が確認用に置かれる");
            Assert.IsNotNull(EditorPreviewRoots.Find("[D-Drive] Button Skin Editor Preview"));

            UnityEngine.Object.DestroyImmediate(_window);
            _window = null;
            Assert.IsNull(EditorPreviewRoots.Find("[D-Drive] Button Skin Editor Preview"), "閉じたらプレビューも片付く");
        }

        // ── 雛形 5 種(Slider Skin / Slider Editor) ──

        [Test]
        public void SliderSkin_Preset_AppliesToThePreviewSlider_AndUndoRestores()
        {
            OpenWindow(SliderSkinType, Slider);
            var slider = (UiSlider)SliderSkinType.GetMethod("EnsurePreviewSlider", Inst).Invoke(_window, null);
            Assert.IsNotNull(slider);
            Assert.AreEqual(0, slider.Notches);
            Undo.IncrementCurrentGroup();
            SliderSkinType.GetMethod("ApplyPreset", Inst).Invoke(_window, new object[] { SliderPresets.SliderPreset.スタミナ });
            Assert.AreEqual(4, slider.Notches);
            Assert.AreEqual(100f, slider.Max);
            Undo.PerformUndo();
            Assert.AreEqual(0, slider.Notches, "Undo 1 回で元に戻る");
            Assert.AreEqual(1f, slider.Max);
        }

        [Test]
        public void SliderSkin_PresetsRow_HasFiveButtons()
        {
            OpenWindow(SliderSkinType, Slider);
            var presets = (VisualElement)Prop("Presets");
            var names = new List<string>();
            foreach (var b in presets.Query<Button>().ToList())
            {
                names.Add(b.text);
            }

            CollectionAssert.AreEqual(new[] { "音量", "感度", "HPバー", "スタミナ", "キャラメイク" }, names);
        }

        [Test]
        public void SliderSkin_DragSimulation_MovesThePreviewSlider()
        {
            OpenWindow(SliderSkinType, Slider);
            var slider = (UiSlider)SliderSkinType.GetMethod("EnsurePreviewSlider", Inst).Invoke(_window, null);
            SliderSkinType.GetMethod("StartDragSim", Inst).Invoke(_window, new object[] { 0f, 1f });
            slider.DragTo(1f);
            Assert.AreEqual(slider.Max, slider.Value, 1e-3f);
            slider.EndDrag();
        }

        // ── Slider Editor(UiSlider コンポーネント) ──

        private UiSlider MakeRealSlider()
        {
            var canvas = new GameObject("RealCanvas", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
            _objects.Add(canvas);
            return PreviewSliderFactory.Create(canvas.transform, "RealSlider", Vector2.zero);
        }

        [Test]
        public void SliderEditor_BuildsCards_AndShowsTheResponseCurveAndNotchBar()
        {
            var slider = MakeRealSlider();
            slider.Notches = 4;
            OpenWindow(SliderType, null);
            SliderType.GetMethod("SetTargetForTest", Inst).Invoke(_window, new object[] { slider });
            var titles = new List<string>();
            foreach (var l in _window.rootVisualElement.Query<Label>(className: "ux-card__title").ToList())
            {
                titles.Add(l.text);
            }

            CollectionAssert.AreEqual(new[] { "応答曲線", "値の範囲・刻み・ノッチ", "実操作", "追従比較", "Skin プレビュー", "雛形", "検証" }, titles);
            var graph = Prop("Graph");
            var curve = (float[])graph.GetType().GetProperty("Curve").GetValue(graph);
            Assert.AreEqual(64, curve.Length, "応答曲線のサンプル数");
            Assert.AreEqual(4, (int)Prop("NotchBar").GetType().GetProperty("Notches").GetValue(Prop("NotchBar")));
            Assert.IsNotNull(_window.rootVisualElement.Q(className: "ux-sl-graph"));
            Assert.IsNotNull(_window.rootVisualElement.Q(className: "ux-sl-notch"));
        }

        [Test]
        public void SliderEditor_Preset_AppliesToTheTarget_AndUndoRestores_AndRefreshesTheCurve()
        {
            var slider = MakeRealSlider();
            OpenWindow(SliderType, null);
            SliderType.GetMethod("SetTargetForTest", Inst).Invoke(_window, new object[] { slider });
            Undo.IncrementCurrentGroup();
            SliderType.GetMethod("ApplyPresetForTest", Inst).Invoke(_window, new object[] { SliderPresets.SliderPreset.音量 });
            Assert.AreEqual(ValueMode.Parametric, slider.Response.Mode);
            var graph = Prop("Graph");
            var curve = (float[])graph.GetType().GetProperty("Curve").GetValue(graph);
            Assert.Less(curve[32], 0.5f, "InQuad の応答は中央で 0.5 未満(下側を細かく)");
            Undo.PerformUndo();
            Assert.AreEqual(ValueMode.Constant, slider.Response.Mode);
        }

        [Test]
        public void SliderEditor_ValidationInline_ShowsTheProblem()
        {
            var slider = MakeRealSlider();
            slider.Min = 5f;
            slider.Max = 1f;
            OpenWindow(SliderType, null);
            SliderType.GetMethod("SetTargetForTest", Inst).Invoke(_window, new object[] { slider });
            Assert.GreaterOrEqual((int)Prop("ValidationCount"), 1);
            Assert.IsNotNull(_window.rootVisualElement.Q(className: "ux-issue"), "検証はカード内に 1 行ずつ出る");
        }

        [Test]
        public void SliderEditor_RealSlider_IsNotDrivenByDragSimulation()
        {
            var slider = MakeRealSlider();
            OpenWindow(SliderType, null);
            SliderType.GetMethod("SetTargetForTest", Inst).Invoke(_window, new object[] { slider });
            SliderType.GetMethod("StartDragSimulate", Inst).Invoke(_window, new object[] { false });
            Assert.IsFalse((bool)Prop("DragSimActive"), "シーン上の実物は書き換えない");
            StringAssert.Contains("実物", (string)Prop("EventLogText"));
        }

        [Test]
        public void SliderEditor_SampleSlider_DragSimulationRuns_AndCompareAndSkinPreviewAreCreated()
        {
            OpenWindow(SliderType, null);
            SliderType.GetMethod("PlaceSampleInScene", Inst).Invoke(_window, new object[] { Slider, PreviewPlaceMode.CurrentScene });
            var target = (UiSlider)Prop("Target");
            Assert.IsNotNull(target, "サンプルが対象になる");
            SliderType.GetMethod("StartDragSimulate", Inst).Invoke(_window, new object[] { false });
            Assert.IsTrue((bool)Prop("DragSimActive"));
            SliderType.GetMethod("SpawnCompareSlider", Inst).Invoke(_window, null);
            Assert.IsNotNull(Prop("CompareSlider"));
            SliderType.GetMethod("SpawnSkinStatesPreview", Inst).Invoke(_window, null);
            Assert.AreEqual(6, (int)Prop("SkinPreviewCount"), "6 状態を並べる");
        }

        [Test]
        public void SliderEditor_OpenFromSkin_PlacesASampleWithTheSkin()
        {
            SliderType.GetMethod("Open", new[] { typeof(SliderSkinData) }).Invoke(null, new object[] { Slider });
            _window = (EditorWindow)Resources.FindObjectsOfTypeAll(SliderType)[0];
            var target = (UiSlider)Prop("Target");
            Assert.IsNotNull(target);
            Assert.AreEqual(Slider.Fill.Tint, target.FillRect.GetComponent<UnityEngine.UI.Image>().color, "Skin のパーツが当たっている");
        }
    }
}
