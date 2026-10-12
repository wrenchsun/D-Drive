using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Inspector;
using DDrive.Editor.Ui;
using DDrive.Foundation.Data;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-12: 新 UiTweenEditor + UiPresetGallery(Editor/Ui)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(UiTweenSample。HUD の入場演出 14 トラック・全部入り 22 トラック・カタログ 24 件)を一時フォルダに作って確かめる(実データは触らない)。
    public class UiTweenEditorUxTests
    {
        private const string TmpFolder = "Assets/__UiTweenEditorUxTestTmp";
        private const string FavKey = "DDrive.UiPresetGallery.Favorites";
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type EditorType => EditorAssembly.GetType("DDrive.Editor.Ui.UiTweenEditor", true);
        private static Type GalleryType => EditorAssembly.GetType("DDrive.Editor.Ui.UiPresetGallery", true);
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private object _sample;
        private EditorWindow _window;
        private EditorWindow _gallery;
        private string _savedFavourites;
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp]
        public void SetUp() => _savedFavourites = EditorPrefs.GetString(FavKey, string.Empty);

        [TearDown]
        public void TearDown()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w == null)
                {
                    continue;
                }

                var t = w.GetType();
                if (t == EditorType || t == GalleryType || t == typeof(UiTweenEditorWindow) || t == typeof(UiPresetGalleryWindow))
                {
                    if (t == EditorType)
                    {
                        var data = t.BaseType.GetField("_target", Inst).GetValue(w) as UnityEngine.Object;
                        if (data != null)
                        {
                            EditorUtility.ClearDirty(data);
                        }
                    }

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
            _gallery = null;
            _sample = null;
            EditorPrefs.SetString(FavKey, _savedFavourites ?? string.Empty);
            if (AssetDatabase.IsValidFolder(TmpFolder))
            {
                AssetDatabase.DeleteAsset(TmpFolder);
            }
        }

        // ── 部品 ──

        private object BuildSample()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.Ui.UiTweenSample", true);
            _sample = sample.GetMethod("Build", Stat).Invoke(null, new object[] { TmpFolder });
            return _sample;
        }

        private T Part<T>(string name) where T : class => (T)_sample.GetType().GetField(name).GetValue(_sample);

        private UiTweenData Main => Part<UiTweenData>("Main");

        private UiTweenData Big => Part<UiTweenData>("Big");

        private void OpenEditor(UiTweenData target)
        {
            _window = (EditorWindow)ScriptableObject.CreateInstance(EditorType);
            EditorType.BaseType.GetField("_target", Inst).SetValue(_window, target);
            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                EditorType.BaseType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }
        }

        private object Prop(string name) => EditorType.GetProperty(name, Inst).GetValue(_window);

        private object Call(string method, params object[] args)
        {
            var m = EditorType.GetMethod(method, Inst) ?? EditorType.BaseType.GetMethod(method, Inst);
            Assert.IsNotNull(m, method);
            return m.Invoke(_window, args);
        }

        private object Field(string name) => EditorType.GetField(name, Inst).GetValue(_window);

        private VisualElement TrackList => (VisualElement)Prop("TrackList");

        private IList Rows => (IList)TrackList.GetType().GetProperty("Rows").GetValue(TrackList);

        private VisualElement RowAt(int index)
        {
            foreach (var r in Rows)
            {
                if ((int)r.GetType().GetProperty("Index").GetValue(r) == index)
                {
                    return (VisualElement)r;
                }
            }

            Assert.Fail("行 " + index + " がありません");
            return null;
        }

        private List<Button> IconButtons(int index) => RowAt(index).Q(className: "ux-trk__head").Query<Button>(className: "ux-btn--icon").ToList();

        private static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        private string CountText() => TrackList.Q<Label>(className: "ux-itemlist__count").text;

        private RectTransform MakeScene(out GameObject root, params string[] childNames)
        {
            root = new GameObject("UxTestRoot", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
            _objects.Add(root);
            RectTransform first = null;
            foreach (var n in childNames)
            {
                var c = new GameObject(n, typeof(RectTransform), typeof(UnityEngine.UI.Image)) { hideFlags = HideFlags.HideAndDontSave };
                c.transform.SetParent(root.transform, false);
                ((RectTransform)c.transform).sizeDelta = new Vector2(200f, 80f);
                first ??= (RectTransform)c.transform;
            }

            return first;
        }

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

        // ── 欄の表 ──

        [Test]
        public void FieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var inGuide = new HashSet<string>(GuideFields("DDrive.Editor.Ui.UiTweenFieldGuide"));
            var data = ScriptableObject.CreateInstance<UiTweenData>();
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

                Assert.IsEmpty(missing, "UiTweenFieldGuide に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    if (!field.StartsWith("(", StringComparison.Ordinal))
                    {
                        Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が UiTweenData にありません");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        // ── 登録表([DataEditor]) ──

        [Test]
        public void Registry_NewEditorFirst_OldOneLabelledAndLast()
        {
            var entries = DataEditorRegistry.GetEntries(typeof(UiTweenData));
            Assert.AreEqual(EditorType, entries[0].WindowType, "先頭は新エディター");
            Assert.AreEqual(0, entries[0].Order);
            var old = entries[entries.Count - 1];
            Assert.AreEqual(typeof(UiTweenEditorWindow), old.WindowType);
            Assert.AreEqual(100, old.Order);
            StringAssert.StartsWith("(旧)", old.Label);
        }

        [Test]
        public void OldWindows_StillOpen_WithOldTitles()
        {
            UiTweenEditorWindow.Open(null);
            var windows = Resources.FindObjectsOfTypeAll<UiTweenEditorWindow>();
            Assert.AreEqual(1, windows.Length);
            StringAssert.StartsWith("(旧)", windows[0].titleContent.text);

            UiPresetGalleryWindow.Open();
            var old = Resources.FindObjectsOfTypeAll<UiPresetGalleryWindow>();
            Assert.AreEqual(1, old.Length);
            StringAssert.StartsWith("(旧)", old[0].titleContent.text);
        }

        // ── サンプル ──

        [Test]
        public void Sample_HasMixedKindsEasesAndLoops_AndABigOne()
        {
            BuildSample();
            Assert.GreaterOrEqual(Main.Tracks.Length, 10);
            var kinds = new HashSet<int>();
            var eases = new HashSet<string>();
            var loops = 0;
            foreach (var t in Main.Tracks)
            {
                kinds.Add(UiTweenTrackInfoKind(t.Property));
                eases.Add(UiTweenTrackInfoEase(t.Motion));
                if (t.Motion.Loop != LoopMode.Once)
                {
                    loops++;
                }
            }

            Assert.AreEqual(6, kinds.Count, "種類(位置 / 拡縮・サイズ / 回転 / 透明度 / 色 / 塗り)が混在");
            Assert.GreaterOrEqual(eases.Count, 8, "イージングが混在");
            Assert.GreaterOrEqual(loops, 3);
            Assert.AreEqual(22, Big.Tracks.Length);
            Assert.AreEqual(24, Part<UiPresetCatalog>("Catalog").Entries.Length);
        }

        private static int UiTweenTrackInfoKind(TweenProperty p) => (int)InfoType.GetMethod("KindOf", Stat).Invoke(null, new object[] { p });

        private static string UiTweenTrackInfoEase(ValueDef m) => (string)InfoType.GetMethod("EaseText", Stat).Invoke(null, new object[] { m });

        private static Type InfoType => EditorAssembly.GetType("DDrive.Editor.Ui.UiTweenTrackInfo", true);

        // ── トラックの文字 ──

        [Test]
        public void TrackInfo_Summary_ShowsFromToDurationEaseDelayAndLoop()
        {
            BuildSample();
            var summary = (string)InfoType.GetMethod("Summary", Stat).Invoke(null, new object[] { Main.Tracks[2] }); // 拡縮 0 → 1 OutBack 0.1 秒遅れ
            StringAssert.Contains("×0 → ×1", summary);
            StringAssert.Contains("0.35 秒", summary);
            StringAssert.Contains("OutBack", summary);
            StringAssert.Contains("0.1 秒遅れ", summary);
            var slide = (string)InfoType.GetMethod("Summary", Stat).Invoke(null, new object[] { Main.Tracks[1] });
            StringAssert.Contains("画面外(左)", slide);
            var loop = (string)InfoType.GetMethod("Summary", Stat).Invoke(null, new object[] { Main.Tracks[8] });
            StringAssert.Contains("往復", loop);
            var bez = (string)InfoType.GetMethod("Summary", Stat).Invoke(null, new object[] { Main.Tracks[12] });
            StringAssert.Contains("ベジェ", bez);
        }

        [Test]
        public void TrackInfo_AxisMax_RoundsUpToANiceValue_AndNormalizeFollowsProperty()
        {
            var axis = (float)InfoType.GetMethod("AxisMax", Stat).Invoke(null, new object[] { 1.6f });
            Assert.AreEqual(2f, axis, 1e-4f, "1.6 秒の 1.25 倍 = 2.0 → 2 秒");
            Assert.AreEqual(0.5f, (float)InfoType.GetMethod("AxisMax", Stat).Invoke(null, new object[] { 0f }), 1e-4f);

            // 位置(Vector)→ 透明度(Float)に変えると、値の型も Float になる。
            var make = InfoType.GetMethod("NewTrack", Stat);
            var track = (TweenTrack)make.Invoke(null, new object[] { TweenProperty.AnchoredPosition });
            Assert.AreEqual(ParamValueType.Vector, track.ToValue.Type);
            track.Property = TweenProperty.Alpha;
            var args = new object[] { track };
            InfoType.GetMethod("NormalizeValues", Stat).Invoke(null, args);
            track = (TweenTrack)args[0];
            Assert.AreEqual(ParamValueType.Float, track.ToValue.Type);
            Assert.AreEqual(ParamValueType.Float, track.FromValue.Type);
        }

        // ── 土台(ヒーロー・カード・固定バー) ──

        [Test]
        public void Editor_BuildsHeroCardsAndFooter_WithSpeed()
        {
            BuildSample();
            OpenEditor(Main);
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNotNull(root.Q(className: "ux-bar__speed"), "UI Tween は速度を持つ");
            // 雛形 / トラック / 全体の長さ / 確認する / 管理情報(その他の検証はサンプルの未登録で出ることがある)
            Assert.GreaterOrEqual(root.Query(className: "ux-card").ToList().Count, 5, "カード数");
            Assert.IsNotNull(root.Q(className: "ux-ut-presets"), "雛形");
            Assert.IsNotNull(root.Q(className: "ux-ut-list"), "トラックの一覧");
            Assert.IsNotNull(root.Q(className: "ux-ut-check"), "確認する");
            Assert.IsTrue(root.Query<Label>().ToList().Exists(l => l.text.StartsWith("SceneView:")), "経路ハンドルの描画権の表示");
        }

        // ── トラックの一覧(1 トラック = 1 行・件数・ListView) ──

        [Test]
        public void TrackList_OneRowPerTrack_WithCountAndKindChips_NoListViewUnder21()
        {
            BuildSample();
            OpenEditor(Main);
            Assert.AreEqual(14, Rows.Count, "1 トラック = 1 行");
            Assert.AreEqual(14, TrackList.Query(className: "ux-ut-row").ToList().Count);
            StringAssert.StartsWith("14 トラック中 ループ 3", CountText());
            Assert.IsNull(TrackList.Q<ListView>(), "20 行以下は ListView にしない");
            // 絞り込みの種類チップ(位置 / 拡縮・サイズ / 回転 / 透明度 / 色 / 塗り)
            var chips = TrackList.Query<Button>(className: "ux-filter").ToList().ConvertAll(b => b.text);
            foreach (var kind in new[] { "位置", "拡縮・サイズ", "回転", "透明度", "色", "塗り", "ループ", "1 回だけ" })
            {
                CollectionAssert.Contains(chips, kind);
            }
        }

        [Test]
        public void TrackList_MoreThan20Tracks_UsesListView()
        {
            BuildSample();
            OpenEditor(Big);
            StringAssert.StartsWith("22 トラック中", CountText());
            Assert.IsNotNull(TrackList.Q<ListView>(), "20 行を超える一覧は ListView(仮想化)");
        }

        [Test]
        public void TrackList_ExternalSearch_FiltersRows()
        {
            BuildSample();
            OpenEditor(Main);
            var list = TrackList.GetType();
            Assert.IsTrue((bool)list.GetMethod("AnyMatch").Invoke(TrackList, new object[] { "透明度" }));
            Assert.IsFalse((bool)list.GetMethod("AnyMatch").Invoke(TrackList, new object[] { "ぜったいにない語" }));
            list.GetMethod("SetExternalQuery").Invoke(TrackList, new object[] { "往復" });
            StringAssert.Contains("表示 2", CountText());
            list.GetMethod("SetExternalQuery").Invoke(TrackList, new object[] { string.Empty });
            Assert.AreEqual(14, TrackList.Query(className: "ux-ut-row").ToList().Count);
        }

        [Test]
        public void TrackList_OverEightTracks_ShowsFlagsAndInlineWarning_AndHidesMotionFromToNoise()
        {
            BuildSample();
            OpenEditor(Main);
            var flags = TrackList.Query<Label>(className: "ux-ut-flag").ToList().FindAll(l => !l.ClassListContains("ux-hidden"));
            Assert.AreEqual(6, flags.Count, "9 本目以降(14 - 8 = 6 本)に再生されない印");
            StringAssert.Contains("再生されません", flags[0].text);
            var messages = _window.rootVisualElement.Query<Label>(className: "ux-issue__msg").ToList().ConvertAll(l => l.text);
            Assert.IsTrue(messages.Exists(m => m.Contains("14 本あります")), "8 本を超える警告が欄の下に出る");
            Assert.IsFalse(messages.Exists(m => m.Contains("From と To が同じ")), "Tween では当てはまらない ValueDef の検査は外す");
            StringAssert.Contains("同時に動かせるのは 8 本まで", TrackList.Q<Label>(className: "ux-lineflag").text);
        }

        // ── トラック 1 行の変更・Undo ──

        [Test]
        public void RowReset_RewritesOneTrack_AndUndoRestores()
        {
            BuildSample();
            OpenEditor(Main);
            var before = JsonUtility.ToJson(Main);
            Undo.IncrementCurrentGroup();
            Click(IconButtons(3)[1]); // #3 回転 ↺
            var reset = (TweenTrack)InfoType.GetMethod("NewTrack", Stat).Invoke(null, new object[] { TweenProperty.Rotation });
            Assert.AreEqual(reset.Motion.Duration, Main.Tracks[3].Motion.Duration, 1e-4f);
            Assert.AreEqual(0f, Main.Tracks[3].Delay, 1e-4f, "遅れも既定に戻る");
            Assert.AreEqual(14, Main.Tracks.Length, "ほかのトラックは増えも減りもしない");
            Undo.PerformUndo();
            Assert.AreEqual(before, JsonUtility.ToJson(Main), "Undo 1 回で元に戻る");
        }

        [Test]
        public void TimeBar_Drag_WritesDelayAndDuration_AsOneUndo()
        {
            BuildSample();
            OpenEditor(Main);
            var bar = RowAt(2).Q(className: "ux-ut-bar");
            Assert.IsNotNull(bar);
            var before = JsonUtility.ToJson(Main);
            Undo.IncrementCurrentGroup();
            bar.GetType().GetMethod("ApplyForTest", Inst).Invoke(bar, new object[] { 0.4f, 0.6f });
            Assert.AreEqual(0.4f, Main.Tracks[2].Delay, 1e-4f);
            Assert.AreEqual(0.6f, Main.Tracks[2].Motion.Duration, 1e-4f);
            Undo.PerformUndo();
            Assert.AreEqual(before, JsonUtility.ToJson(Main));
        }

        [Test]
        public void AddTrack_OpensTheNewRow_AndDeleteRemovesIt_UndoBringsBack()
        {
            BuildSample();
            var data = Part<UiTweenData>("Simple");
            OpenEditor(data);
            Assert.AreEqual(1, Rows.Count);
            Undo.IncrementCurrentGroup();
            TrackList.GetType().GetMethod("AddTrack").Invoke(TrackList, new object[] { TweenProperty.Scale });
            Assert.AreEqual(2, data.Tracks.Length);
            Assert.AreEqual(TweenProperty.Scale, data.Tracks[1].Property);
            Assert.AreEqual(2, Rows.Count, "一覧が作り直される");
            Assert.IsTrue((bool)RowAt(1).GetType().GetProperty("IsOpen").GetValue(RowAt(1)), "足したトラックの ⚙ が開く");

            Undo.IncrementCurrentGroup();
            Click(IconButtons(1)[3]); // ×
            Assert.AreEqual(1, data.Tracks.Length);
            Undo.PerformUndo();
            Assert.AreEqual(2, data.Tracks.Length, "削除を Undo");
        }

        [Test]
        public void RowDetails_ChangeProperty_NormalizesValueTypes_AsOneUndo()
        {
            BuildSample();
            var data = Part<UiTweenData>("Simple"); // Alpha 0 → 1
            OpenEditor(data);
            Click(IconButtons(0)[2]); // ⚙
            var enumField = RowAt(0).Q<EnumField>();
            Assert.IsNotNull(enumField, "⚙ で項目(Property)が出る");
            var before = JsonUtility.ToJson(data);
            Undo.IncrementCurrentGroup();
            enumField.value = TweenProperty.AnchoredPosition;
            Assert.AreEqual(TweenProperty.AnchoredPosition, data.Tracks[0].Property);
            Assert.AreEqual(ParamValueType.Vector, data.Tracks[0].FromValue.Type, "値の型も位置(2 値)に合わせる");
            Undo.PerformUndo();
            Assert.AreEqual(before, JsonUtility.ToJson(data));
        }

        [Test]
        public void RowDetails_FromToFields_WriteTypedValues()
        {
            BuildSample();
            var data = Part<UiTweenData>("Simple");
            OpenEditor(data);
            Click(IconButtons(0)[2]);
            var floats = RowAt(0).Query<FloatField>().ToList();
            Assert.GreaterOrEqual(floats.Count, 2, "開始値・終了値");
            var to = floats.Find(f => f.label.Contains("終了値"));
            Assert.IsNotNull(to);
            to.value = 0.5f;
            Assert.AreEqual(0.5f, data.Tracks[0].ToValue.FloatValue, 1e-4f);
            Assert.AreEqual(ParamValueType.Float, data.Tracks[0].ToValue.Type);
        }

        [Test]
        public void RowDetails_DuplicateAndMove_ReorderTracks()
        {
            BuildSample();
            var data = Part<UiTweenData>("Pop"); // Scale / Alpha / Rotation
            OpenEditor(data);
            Click(IconButtons(0)[2]);
            var buttons = RowAt(0).Query<Button>().ToList();
            Click(buttons.Find(b => b.text.Contains("複製")));
            Assert.AreEqual(4, data.Tracks.Length);
            Assert.AreEqual(TweenProperty.Scale, data.Tracks[1].Property);
            Assert.AreEqual(TweenProperty.Scale, data.Tracks[0].Property);
            // 開いている行(複製した 1 番)を 1 つ下へ
            buttons = RowAt(1).Query<Button>().ToList();
            Click(buttons.Find(b => b.text.Contains("下へ")));
            Assert.AreEqual(TweenProperty.Alpha, data.Tracks[1].Property);
            Assert.AreEqual(TweenProperty.Scale, data.Tracks[2].Property);
        }

        [Test]
        public void PathMove_ShowsSplineTools_AndAddsAndRemovesPoints()
        {
            BuildSample();
            OpenEditor(Main);
            Click(IconButtons(7)[2]); // #7 経路移動(4 点)
            var buttons = RowAt(7).Query<Button>().ToList();
            Click(buttons.Find(b => b.text.Contains("＋点を追加")));
            Assert.AreEqual(5, Main.Tracks[7].Path.Points.Length);
            Click(buttons.Find(b => b.text.Contains("最後の点を削除")));
            Click(buttons.Find(b => b.text.Contains("最後の点を削除")));
            Assert.AreEqual(3, Main.Tracks[7].Path.Points.Length);
        }

        // ── 雛形 ──

        private object Picker => Prop("Picker");

        private string PickAndApply(string preset, bool append)
        {
            Picker.GetType().GetMethod("Select").Invoke(Picker, new object[] { preset });
            return (string)Picker.GetType().GetMethod("Apply").Invoke(Picker, new object[] { append });
        }

        [Test]
        public void Presets_ReplaceAndAppend_FollowTheOldRules_AndUndoWorks()
        {
            BuildSample();
            var data = Part<UiTweenData>("Simple"); // Alpha 1 本
            OpenEditor(data);
            var before = JsonUtility.ToJson(data);
            Undo.IncrementCurrentGroup();
            var msg = PickAndApply("PopIn", false);
            StringAssert.Contains("PopIn", msg);
            Assert.AreEqual(1, data.Tracks.Length);
            Assert.AreEqual(TweenProperty.Scale, data.Tracks[0].Property);
            Assert.AreEqual(1, Rows.Count, "一覧が作り直される");
            Undo.PerformUndo();
            Assert.AreEqual(before, JsonUtility.ToJson(data));

            msg = PickAndApply("SlideFadeInLeft", true); // 2 本(位置 + 透明度)を足す
            StringAssert.Contains("2 本追加", msg);
            Assert.AreEqual(3, data.Tracks.Length);
            Assert.AreEqual(3, Rows.Count);
        }

        [Test]
        public void Presets_Append_StopsAtEightTracks()
        {
            BuildSample();
            OpenEditor(Main); // 14 本
            var msg = PickAndApply("FadeIn", true);
            StringAssert.Contains("上限", msg);
            Assert.AreEqual(14, Main.Tracks.Length, "上限を超えているので足さない");
        }

        [Test]
        public void Presets_CatalogTab_AppliesCatalogTween()
        {
            BuildSample();
            var data = Part<UiTweenData>("Empty");
            OpenEditor(data);
            Picker.GetType().GetMethod("SelectTab").Invoke(Picker, new object[] { GalleryTab.カタログ });
            var msg = PickAndApply("ポップ", false);
            StringAssert.Contains("カタログ", msg);
            Assert.AreEqual(3, data.Tracks.Length, "カタログの「ポップ」= ポップインの 3 トラック");
        }

        [Test]
        public void Presets_Register_UpsertsIntoTheCatalog()
        {
            BuildSample();
            var catalog = Part<UiPresetCatalog>("Catalog");
            var data = Part<UiTweenData>("Simple");
            OpenEditor(data);
            var box = Picker.GetType().GetProperty("Register").GetValue(Picker);
            var boxType = box.GetType();
            boxType.GetMethod("SetCatalog").Invoke(box, new object[] { catalog });
            boxType.GetMethod("SetSource").Invoke(box, new object[] { data });

            boxType.GetMethod("SetName").Invoke(box, new object[] { "新しい独自プリセット" });
            boxType.GetMethod("Register").Invoke(box, null);
            Assert.AreEqual(25, catalog.Entries.Length, "新しい名前は足される");
            Assert.AreEqual(data, catalog.Entries[24].Tween);

            boxType.GetMethod("SetName").Invoke(box, new object[] { "ポップ" });
            boxType.GetMethod("Register").Invoke(box, null);
            Assert.AreEqual(25, catalog.Entries.Length, "同名は上書き");
            Assert.AreEqual(data, catalog.Entries[1].Tween);
        }

        // ── 実要素での再生 ──

        [Test]
        public void Play_DrivesTheRealElement_AndRestoresItWhenStopped()
        {
            BuildSample();
            var data = Part<UiTweenData>("Pop"); // Scale 0→1 / Alpha 0→1 / Rotation -12→0
            var rt = MakeScene(out _, "Panel");
            OpenEditor(data);
            EditorType.GetField("_previewTarget", Inst).SetValue(_window, rt);
            Assert.AreEqual(Vector3.one, rt.localScale);
            Assert.IsNull(rt.GetComponent<CanvasGroup>());

            Call("PlayMain", false);
            Assert.IsTrue((bool)EditorType.GetProperty("IsPlaying", Inst).GetValue(_window), "▶ で再生が始まる");
            var manager = (UiTweenManager)Field("_previewManager");
            manager.Tick(0.05f);
            Assert.Less(rt.localScale.x, 1f, "実要素が実際に動く");
            Assert.IsNotNull(rt.GetComponent<CanvasGroup>(), "Alpha のために CanvasGroup が付く");

            Call("StopMain");
            Assert.IsFalse((bool)EditorType.GetProperty("IsPlaying", Inst).GetValue(_window));
            Assert.AreEqual(Vector3.one, rt.localScale, "■ で再生前の見た目に戻る");
            Assert.AreEqual(0f, rt.localEulerAngles.z, 1e-3f);
            Assert.IsNull(rt.GetComponent<CanvasGroup>(), "元々無かった CanvasGroup は取り除く");
        }

        [Test]
        public void Play_ToTheEnd_RestoresAutomatically()
        {
            BuildSample();
            var data = Part<UiTweenData>("Pop");
            var rt = MakeScene(out _, "Panel");
            OpenEditor(data);
            EditorType.GetField("_previewTarget", Inst).SetValue(_window, rt);
            Call("PlayMain", false);
            var manager = (UiTweenManager)Field("_previewManager");
            for (var i = 0; i < 20; i++)
            {
                manager.Tick(0.1f);
            }

            Assert.IsFalse(manager.IsPlaying((DDrive.Foundation.Handle.Handle<UiTweenMarker>)Field("_previewHandle")), "尺を超えたら終わる");
            Call("OnEditorUpdate"); // 「再生中 → 終了」を拾って元に戻す
            Assert.AreEqual(Vector3.one, rt.localScale);
            Assert.IsNull(rt.GetComponent<CanvasGroup>());
            StringAssert.Contains("戻しました", (string)Prop("StatusText"));
        }

        [Test]
        public void PlayTrack_PlaysOnlyThatTrack()
        {
            BuildSample();
            var data = Part<UiTweenData>("Pop");
            var rt = MakeScene(out _, "Panel");
            OpenEditor(data);
            EditorType.GetField("_previewTarget", Inst).SetValue(_window, rt);
            Call("PlayTrack", 1); // Alpha だけ
            var manager = (UiTweenManager)Field("_previewManager");
            manager.Tick(0.05f);
            Assert.AreEqual(Vector3.one, rt.localScale, "拡縮は動かさない");
            Assert.IsNotNull(rt.GetComponent<CanvasGroup>());
            Assert.Less(rt.GetComponent<CanvasGroup>().alpha, 1f);
            Call("StopMain");
            Assert.IsNull(rt.GetComponent<CanvasGroup>());
        }

        [Test]
        public void Play_WithoutTarget_SaysSo_AndDoesNotThrow()
        {
            BuildSample();
            OpenEditor(Part<UiTweenData>("Pop"));
            Call("PlayMain", false);
            Assert.IsFalse((bool)EditorType.GetProperty("IsPlaying", Inst).GetValue(_window));
            StringAssert.Contains("プレビュー対象がありません", (string)Prop("StatusText"));
            Assert.IsFalse((bool)EditorType.BaseType.GetProperty("WantPlaying", Inst).GetValue(_window), "対象が無いときはリピートしない");
        }

        [Test]
        public void Play_RefusesPrefabAssets()
        {
            BuildSample();
            var data = Part<UiTweenData>("Pop");
            OpenEditor(data);
            var path = TmpFolder + "/UxTestPrefab.prefab";
            var go = new GameObject("UxTestPrefab", typeof(RectTransform));
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            EditorType.GetField("_previewTarget", Inst).SetValue(_window, prefab.GetComponent<RectTransform>());
            Call("PlayMain", false);
            Assert.IsFalse((bool)EditorType.GetProperty("IsPlaying", Inst).GetValue(_window));
            StringAssert.Contains("Prefab アセット", (string)Prop("StatusText"));
        }

        // ── 対象の要素(収集・Open) ──

        [Test]
        public void Collect_ListsElements_WithUniqueLabels_AndSelectingSetsTheTarget()
        {
            BuildSample();
            var rt = MakeScene(out var root, "Button", "Button", "Panel");
            OpenEditor(Part<UiTweenData>("Pop"));
            Call("SetCollectRootForTest", root);
            Call("CollectElementsForTest");
            var choices = (IList)Prop("ElementChoices");
            Assert.AreEqual(4, choices.Count, "(ルート)+ 子 3 つ");
            var labels = new List<string>();
            foreach (var c in choices)
            {
                labels.Add((string)c.GetType().GetField("Item1").GetValue(c));
            }

            CollectionAssert.AllItemsAreUnique(labels, "同名の兄弟はラベルを一意にする");
            CollectionAssert.Contains(labels, "Button #2");
            Call("SelectElementForTest", "Button #2");
            Assert.IsNotNull(Prop("PreviewTarget"));
            Assert.AreNotSame(rt, Prop("PreviewTarget"), "2 つ目の Button が選ばれる"); // Transform は IEnumerable なので AreNotEqual だと子の無い Transform 同士が等しくなる
        }

        [Test]
        public void Open_WithCollectRootAndElement_SelectsItAsThePreviewTarget()
        {
            BuildSample();
            var rt = MakeScene(out var root, "Panel");
            var data = Part<UiTweenData>("Pop");
            var open = EditorType.GetMethod("Open", Stat, null, new[] { typeof(UiTweenData), typeof(GameObject), typeof(RectTransform), typeof(string) }, null);
            _window = (EditorWindow)open.Invoke(null, new object[] { data, root, rt, "Panel" });
            if (_window.rootVisualElement.childCount == 0)
            {
                EditorType.BaseType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }

            Assert.AreSame(rt, Prop("PreviewTarget"));
            StringAssert.Contains("Panel", (string)Prop("StatusText"));
            Assert.AreEqual(data, EditorType.BaseType.GetField("_target", Inst).GetValue(_window));
        }

        // ── ギャラリー ──

        private void OpenGallery(UiTweenData source = null)
        {
            _gallery = (EditorWindow)GalleryType.GetMethod("Open", Stat).Invoke(null, new object[] { source });
            if (_gallery.rootVisualElement.childCount == 0)
            {
                GalleryType.GetMethod("CreateGUI", Inst).Invoke(_gallery, null);
            }
        }

        private object G(string prop) => GalleryType.GetProperty(prop, Inst).GetValue(_gallery);

        private void GCall(string method, params object[] args)
        {
            var m = GalleryType.GetMethod(method, Inst);
            Assert.IsNotNull(m, method);
            m.Invoke(_gallery, args);
        }

        [Test]
        public void Gallery_BuildsHeroCardsAndTabs_WithTwentyAppearCards()
        {
            OpenGallery();
            var root = _gallery.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"));
            Assert.IsTrue(root.ClassListContains("ux-root"), "共通テーマが当たる");
            Assert.AreEqual(20, (int)G("ShownCount"), "出現タブ = 20 枚");
            Assert.AreEqual(20, root.Query(className: "ux-ut-gcard").ToList().Count);
            Assert.IsFalse((bool)G("UsesListView"));
            var tabs = root.Query<Button>(className: "ux-filter").ToList().ConvertAll(b => b.text);
            foreach (var t in new[] { "出現", "常時", "消滅", "強調", "カタログ", "★ お気に入り" })
            {
                CollectionAssert.Contains(tabs, t);
            }

            Assert.GreaterOrEqual(root.Query<VisualElement>(className: "ux-ut-sketch").ToList().Count, 20, "カードの曲線スケッチ");
        }

        [Test]
        public void Gallery_TabsAndSearch_Filter()
        {
            OpenGallery();
            GCall("SelectTab", GalleryTab.常時);
            Assert.AreEqual(9, (int)G("ShownCount"), "常時 = 9 種");
            GCall("SelectTab", GalleryTab.強調);
            Assert.AreEqual(11, (int)G("ShownCount"), "強調 = 11 種");
            GCall("SelectTab", GalleryTab.出現);
            GCall("SetSearch", "fade");
            var names = new List<string>();
            foreach (var c in (IEnumerable<GalleryCard>)G("ShownCards"))
            {
                names.Add(c.Name);
            }

            CollectionAssert.Contains(names, "FadeIn");
            CollectionAssert.Contains(names, "SlideFadeInLeft");
            Assert.IsFalse(names.Contains("PopIn"));
            GCall("SetSearch", "ぜったいにない語");
            Assert.AreEqual(0, (int)G("ShownCount"));
            StringAssert.Contains("該当するプリセットがありません", string.Join("|", _gallery.rootVisualElement.Query<Label>().ToList().ConvertAll(l => l.text)));
        }

        [Test]
        public void Gallery_Favourites_ToggleFilterAndPersist()
        {
            OpenGallery();
            GCall("ToggleFavouriteForTest", "PopIn");
            GCall("ToggleFavouriteForTest", "Pulse");
            Assert.IsTrue((bool)GalleryType.GetMethod("IsFavourite", Inst).Invoke(_gallery, new object[] { "PopIn" }));
            StringAssert.Contains("PopIn", EditorPrefs.GetString(FavKey, string.Empty), "お気に入りは旧ギャラリーと同じ保存先に残る");

            GCall("SetFavouritesOnly", true);
            Assert.AreEqual(2, (int)G("ShownCount"), "全タブからお気に入りだけ集める");
            // 同じ表示の先頭には ★ のカード
            var stars = _gallery.rootVisualElement.Query<Button>().ToList().FindAll(b => b.text == "★");
            Assert.AreEqual(2, stars.Count);

            GCall("SetFavouritesOnly", false);
            GCall("ToggleFavouriteForTest", "PopIn");
            GCall("SetFavouritesOnly", true);
            Assert.AreEqual(1, (int)G("ShownCount"));
        }

        [Test]
        public void Gallery_CatalogTab_MoreThan20Cards_UsesListView()
        {
            BuildSample();
            OpenGallery();
            GCall("SelectTab", GalleryTab.カタログ);
            Assert.GreaterOrEqual((int)G("ShownCount"), 24);
            Assert.IsTrue((bool)G("UsesListView"), "20 件を超えるカード一覧は ListView(仮想化)");
        }

        [Test]
        public void Gallery_Play_OnASceneElement_RestoresOnStop()
        {
            OpenGallery();
            var rt = MakeScene(out _, "Panel");
            var tryArgs = new object[] { "PopIn", null };
            Assert.IsTrue((bool)GalleryType.GetMethod("TryGetCard", Inst).Invoke(_gallery, tryArgs));
            var msg = (string)GalleryType.GetMethod("PlayOn", Inst).Invoke(_gallery, new[] { tryArgs[1], rt });
            StringAssert.Contains("再生中", msg);
            Assert.IsTrue((bool)G("IsPreviewing"));
            ((UiTweenManager)GalleryType.GetField("_previewManager", Inst).GetValue(_gallery)).Tick(0.02f);
            Assert.Less(rt.localScale.x, 1f, "PopIn は 0 から膨らむ");
            GalleryType.GetMethod("StopPreview", Inst).Invoke(_gallery, null);
            Assert.IsFalse((bool)G("IsPreviewing"));
            Assert.AreEqual(Vector3.one, rt.localScale, "止めると元の見た目に戻る");
        }

        [Test]
        public void Gallery_Play_RefusesPrefabAssets_AndNoSelection()
        {
            BuildSample();
            OpenGallery();
            var tryArgs = new object[] { "PopIn", null };
            GalleryType.GetMethod("TryGetCard", Inst).Invoke(_gallery, tryArgs);
            var msg = (string)GalleryType.GetMethod("PlayOn", Inst).Invoke(_gallery, new[] { tryArgs[1], null });
            StringAssert.Contains("RectTransform を選択", msg);
            Assert.IsFalse((bool)G("IsPreviewing"));
        }

        [Test]
        public void Gallery_ApplyToSource_ReplacesAndAppends_ViaTheSharedOps()
        {
            BuildSample();
            var data = Part<UiTweenData>("Simple");
            OpenGallery(data);
            Assert.AreEqual(data, G("Source"));
            var tryArgs = new object[] { "SlideFadeInLeft", null };
            GalleryType.GetMethod("TryGetCard", Inst).Invoke(_gallery, tryArgs);
            var card = tryArgs[1];
            var msg = (string)GalleryType.GetMethod("ApplyToSource", Inst).Invoke(_gallery, new[] { card, true });
            StringAssert.Contains("追加しました", msg);
            Assert.AreEqual(3, data.Tracks.Length);
            msg = (string)GalleryType.GetMethod("ApplyToSource", Inst).Invoke(_gallery, new[] { card, false });
            StringAssert.Contains("置き換えました", msg);
            Assert.AreEqual(2, data.Tracks.Length);
        }

        [Test]
        public void Gallery_ApplyToElement_WritesTheElementFxRow()
        {
            OpenGallery();
            var canvas = ScriptableObject.CreateInstance<CanvasData>();
            try
            {
                GCall("SetCanvas", canvas);
                var tryArgs = new object[] { "PopIn", null };
                GalleryType.GetMethod("TryGetCard", Inst).Invoke(_gallery, tryArgs);
                GalleryType.GetMethod("ApplyToElement", Inst).Invoke(_gallery, new[] { tryArgs[1] });
                Assert.AreEqual(1, canvas.ElementEffects.Length);
                Assert.AreEqual(UiPreset.PopIn, canvas.ElementEffects[0].AppearPreset.Preset);
                StringAssert.Contains("PopIn", (string)G("Status"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void Gallery_Register_UsesTheSharedBox()
        {
            BuildSample();
            var catalog = Part<UiPresetCatalog>("Catalog");
            OpenGallery(Part<UiTweenData>("Simple"));
            var box = G("RegisterBox");
            box.GetType().GetMethod("SetCatalog").Invoke(box, new object[] { catalog });
            box.GetType().GetMethod("SetName").Invoke(box, new object[] { "ギャラリーから登録" });
            box.GetType().GetMethod("Register").Invoke(box, null);
            Assert.AreEqual(25, catalog.Entries.Length);
            Assert.AreEqual("ギャラリーから登録", catalog.Entries[24].Name);
        }

        // ── 配線(docs/1010 D-4): 新 CanvasEditor の ElementFx 行の「✎ Tween Editor」は新 UiTweenEditor を開く ──

        [Test]
        public void NewCanvasEditor_TweenEditorButton_OpensTheNewUiTweenEditor_NotTheOldOne()
        {
            BuildSample();
            const string canvasFolder = "Assets/__UiTweenEditorUxTestTmpCanvas";
            try
            {
                var canvasSample = EditorAssembly.GetType("DDrive.Editor.CanvasTool.CanvasSample", true);
                var built = canvasSample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { canvasFolder });
                var parent = (CanvasData)built.GetType().GetField("Parent").GetValue(built);
                var tween = Main;
                var fx = parent.ElementEffects;
                fx[0].Appear = new DDrive.Foundation.Identity.AssetId<UiTweenMarker>(tween.Id, DDrive.Foundation.Identity.AssetType.UiTween);
                EditorUtility.SetDirty(parent);

                var canvasEditorType = EditorAssembly.GetType("DDrive.Editor.CanvasTool.CanvasEditor", true);
                var canvasWindow = (EditorWindow)ScriptableObject.CreateInstance(canvasEditorType);
                try
                {
                    canvasEditorType.BaseType.GetField("_target", Inst).SetValue(canvasWindow, parent);
                    canvasWindow.Show();
                    if (canvasWindow.rootVisualElement.childCount == 0)
                    {
                        canvasEditorType.BaseType.GetMethod("CreateGUI", Inst).Invoke(canvasWindow, null);
                    }

                    var open = (HashSet<string>)canvasEditorType.GetField("_fxOpen", Inst).GetValue(canvasWindow);
                    open.Add("fx:" + (fx[0].ElementPath ?? string.Empty));
                    var fxList = canvasEditorType.GetField("_fxList", Inst).GetValue(canvasWindow);
                    fxList.GetType().GetMethod("Render").Invoke(fxList, null);

                    var edit = canvasWindow.rootVisualElement.Query<Button>().ToList().Find(b => b.text.Contains("Tween Editor") && b.enabledSelf);
                    Assert.IsNotNull(edit, "直接指定の Tween があるときだけ押せる「✎ Tween Editor」");
                    Click(edit);

                    var windows = Resources.FindObjectsOfTypeAll(EditorType);
                    Assert.AreEqual(1, windows.Length, "新 UiTweenEditor が開く");
                    Assert.AreSame(tween, EditorType.BaseType.GetField("_target", Inst).GetValue(windows[0]));
                    Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<UiTweenEditorWindow>().Length, "旧は開かない");
                    EditorUtility.ClearDirty(parent);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasWindow);
                }
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(canvasFolder))
                {
                    AssetDatabase.DeleteAsset(canvasFolder);
                }
            }
        }

        // ── 旧エディターの Open(target, collectRoot, assigned, label) と同じ形の入口 ──

        [Test]
        public void NewEditor_HasTheSameOpenSignatureAsTheOldOne()
        {
            var oldOpen = typeof(UiTweenEditorWindow).GetMethod("Open", new[] { typeof(UiTweenData), typeof(GameObject), typeof(RectTransform), typeof(string) });
            var newOpen = EditorType.GetMethod("Open", Stat, null, new[] { typeof(UiTweenData), typeof(GameObject), typeof(RectTransform), typeof(string) }, null);
            Assert.IsNotNull(oldOpen);
            Assert.IsNotNull(newOpen, "Canvas Editor / Skin の「Tween Editor で開く」が渡す 4 引数の入口");
        }
    }
}
