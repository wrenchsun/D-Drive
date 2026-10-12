using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Anim;
using DDrive.Editor.Inspector;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-6 / D-10: 新 AnimEditor(Editor/Anim)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(AnimSample = 「剣の連撃」: Controller のステート 14 行・イベント 12 件・IK・BlendShape 3 本)を
    // 一時フォルダに作って確かめる(実データは触らない)。
    public class AnimEditorUxTests
    {
        private const string TmpFolder = "Assets/__AnimEditorUxTestTmp";
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type EditorType => EditorAssembly.GetType("DDrive.Editor.Anim.AnimEditor", true);
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private AnimData _data;
        private EditorWindow _window;
        private Animator _animator;

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                EditorUtility.ClearDirty(_data);
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }

            if (_animator != null)
            {
                UnityEngine.Object.DestroyImmediate(_animator.gameObject);
                _animator = null;
            }

            if (AssetDatabase.IsValidFolder(TmpFolder))
            {
                AssetDatabase.DeleteAsset(TmpFolder);
            }
        }

        private void BuildSampleAndWindow(bool withAnimator = true)
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.Anim.AnimSample", true);
            var result = sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder });
            _data = (AnimData)result.GetType().GetField("Anim").GetValue(result);
            if (withAnimator)
            {
                var controller = result.GetType().GetField("Controller").GetValue(result);
                _animator = (Animator)sample.GetMethod("BuildAnimatorObject", BindingFlags.Static | BindingFlags.Public).Invoke(null, new[] { controller });
            }

            _window = (EditorWindow)ScriptableObject.CreateInstance(EditorType);
            var baseType = EditorType.BaseType;
            baseType.GetField("_target", Inst).SetValue(_window, _data);
            if (_animator != null)
            {
                EditorType.GetField("_sceneTarget", Inst).SetValue(_window, _animator);
            }

            // 値の変更イベント・ボタンの操作は panel が無いと配送されないので、実際にウィンドウを出す(CreateGUI は Show が呼ぶ)。
            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                baseType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }
        }

        private object Field(string name) => EditorType.GetField(name, Inst).GetValue(_window);

        private object Call(string method, params object[] args) => EditorType.GetMethod(method, Inst).Invoke(_window, args);

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
        public void AnimFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var guideType = EditorAssembly.GetType("DDrive.Editor.Anim.AnimFieldGuide", true);
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var inGuide = new HashSet<string>();
            var tiers = new Dictionary<string, int>();
            foreach (var e in (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance))
            {
                var field = (string)e.GetType().GetField("Field").GetValue(e);
                inGuide.Add(field);
                tiers[field] = (int)e.GetType().GetField("Tier").GetValue(e);
            }

            var data = ScriptableObject.CreateInstance<AnimData>();
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

                Assert.IsEmpty(missing, "AnimFieldGuide に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    // "(" で始まる欄は Data のフィールドではなく補助の行(ステート一覧・タイムライン・ブレンド確認など)。
                    if (!field.StartsWith("(", StringComparison.Ordinal))
                    {
                        Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が AnimData にありません");
                    }
                }

                // 必須の段 = Validator が Error にする欄(Clip 未設定)。
                Assert.AreEqual(0, tiers[nameof(AnimData.Clip)], "Clip は必須");
                Assert.AreNotEqual(0, tiers[nameof(AnimData.Events)]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void AnimData_NewEditorIsPrimary_OldIsSecondWithOrder100()
        {
            var entries = new List<DataEditorRegistry.Entry>(DataEditorRegistry.GetEntries(typeof(AnimData)));
            Assert.GreaterOrEqual(entries.Count, 2);
            Assert.AreEqual(EditorType, entries[0].WindowType, "新が第 1 候補");
            Assert.AreEqual(0, entries[0].Order);
            Assert.AreEqual(typeof(AnimEditorWindow), entries[1].WindowType, "旧が第 2 候補");
            Assert.AreEqual(100, entries[1].Order);
            StringAssert.StartsWith("(旧)", entries[1].Label);
        }

        [Test]
        public void Anim2DData_StillOpensInOldEditors_UntilUx7()
        {
            var entries = new List<DataEditorRegistry.Entry>(DataEditorRegistry.GetEntries(typeof(Anim2DData)));
            Assert.AreEqual(typeof(AnimEditorWindow).Assembly.GetType("DDrive.Editor.Anim2D.Anim2DEditorWindow", true), entries[0].WindowType, "Anim2D の主エディターは変えない");
            var data = ScriptableObject.CreateInstance<Anim2DData>();
            try
            {
                var accepts = EditorType.GetMethod("AcceptsTarget", Inst);
                var w = (EditorWindow)ScriptableObject.CreateInstance(EditorType);
                try
                {
                    Assert.IsFalse((bool)accepts.Invoke(w, new object[] { data }), "新 AnimEditor は Anim2DData を対象にしない");
                    Assert.IsTrue((bool)accepts.Invoke(w, new object[] { ScriptableObject.CreateInstance<AnimData>() }));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(w);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        // ── 実運用規模のサンプルで開く ──

        [Test]
        public void Sample_BuildsHeroCardsAndFooter_WithPlayback()
        {
            BuildSampleAndWindow();
            var root = _window.rootVisualElement;
            Assert.AreEqual(12, _data.Events.Length);
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNotNull(root.Q(className: "ux-bar__speed"), "速度");
            // 動かす / 合図 / 表情・手足 / 確認する / 管理情報 / その他の検証
            Assert.AreEqual(6, root.Query(className: "ux-card").ToList().Count, "カード数");
        }

        [Test]
        public void Sample_AllFoldoutsAreClosedByDefault()
        {
            BuildSampleAndWindow();
            var root = _window.rootVisualElement;
            EditorType.GetMethod("CollapseFoldouts", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root });
            var open = new List<string>();
            foreach (var f in root.Query<Foldout>().ToList())
            {
                if (f.value)
                {
                    open.Add(f.text);
                }
            }

            Assert.IsEmpty(open, "既定で開いている Foldout: " + string.Join(", ", open));
        }

        [Test]
        public void Sample_EventList_IsOneRowPerEvent_WithCountAndFilters()
        {
            BuildSampleAndWindow();
            var list = (VisualElement)Field("_eventList");
            Assert.AreEqual(12, list.Query(className: "ux-fxrow").ToList().Count, "イベント 1 件 = 1 行");
            var count = list.Query<Label>(className: "ux-itemlist__count").ToList()[0];
            StringAssert.Contains("12 イベント中 対象あり 11", count.text);
            var chips = list.Query<Button>(className: "ux-filter").ToList();
            Assert.IsNotNull(chips.Find(b => b.text == "対象なし"));
            Assert.IsNotNull(chips.Find(b => b.text == "SE"));
            Assert.IsNotNull(chips.Find(b => b.text == "VFX"));
            Click(chips.Find(b => b.text == "VFX"));
            var shown = (int)list.GetType().GetProperty("ShownCount").GetValue(list);
            Assert.AreEqual(4, shown, "VFX のチップで 4 件(軌跡 ×2・火花・土煙)");
            // 行の要約(時刻・繰り返し)が 1 行で出る。
            var summaries = list.Query<Label>(className: "ux-fxrow__summary").ToList();
            Assert.IsTrue(summaries.Exists(l => l.text.Contains("F6") && l.text.Contains("0.20s")), "Frame イベントの要約");
        }

        [Test]
        public void Sample_EventList_MoreThan20_UsesListView()
        {
            BuildSampleAndWindow();
            var events = new List<AssetEvent>(_data.Events);
            for (var i = 0; i < 10; i++)
            {
                events.Add(new AssetEvent { Trigger = EventTrigger.Frame, Time = i, Action = EventAction.PlayAsset });
            }

            _data.Events = events.ToArray();
            var section = Field("_eventSection");
            section.GetType().GetMethod("Rebuild").Invoke(section, null);
            var lvs = _window.rootVisualElement.Query<ListView>(className: "ux-itemlist__lv").ToList();
            Assert.AreEqual(1, lvs.Count, "20 行を超えたら ListView(仮想化)");
        }

        [Test]
        public void Sample_ChangeOneEventTime_AndUndo()
        {
            BuildSampleAndWindow();
            EditorType.GetField("_openEvent", Inst).SetValue(_window, 2);
            Call("ReRenderEventList");
            var rows = ((VisualElement)Field("_eventList")).Query(className: "ux-fxrow__details").ToList();
            Assert.AreEqual(1, rows.Count, "展開した行は 1 本だけ");
            Assert.AreEqual(6f, _data.Events[2].Time, 1e-4f);
            Undo.IncrementCurrentGroup();
            Func<AssetEvent, AssetEvent> change = ev =>
            {
                ev.Time = 15f;
                return ev;
            };
            Call("EditEvent", 2, "Set Event Frame", change);
            Assert.AreEqual(15f, _data.Events[2].Time, 1e-4f);
            var summaries = ((VisualElement)Field("_eventList")).Query<Label>(className: "ux-fxrow__summary").ToList();
            Assert.IsTrue(summaries.Exists(l => l.text.Contains("F15")), "要約が追従する");
            Undo.PerformUndo();
            Assert.AreEqual(6f, _data.Events[2].Time, 1e-4f, "Undo で戻る");
        }

        [Test]
        public void Sample_AddAndRemoveOneEvent()
        {
            BuildSampleAndWindow();
            var before = _data.Events.Length;
            Call("AddEventAtPlayhead", AssetType.Se);
            Assert.AreEqual(before + 1, _data.Events.Length, "イベント 1 件を追加");
            Assert.AreEqual(EventAction.PlayAsset, _data.Events[before].Action);
            Assert.IsTrue(_data.Events[before].Target.IsAssigned, "最初の SE が入る");
            var rows = ((VisualElement)Field("_eventList")).Query(className: "ux-fxrow").ToList();
            Assert.AreEqual(before + 1, rows.Count);
            Undo.IncrementCurrentGroup();
            Call("RemoveEvent", before);
            Assert.AreEqual(before, _data.Events.Length, "イベント 1 件を削除");
            Undo.PerformUndo();
            Assert.AreEqual(before + 1, _data.Events.Length, "Undo で戻る");
        }

        [Test]
        public void Sample_DuplicateEvent_AndFlagOverLength()
        {
            BuildSampleAndWindow();
            var count = _data.Events.Length;
            Call("DuplicateEvent", 0);
            Assert.AreEqual(count + 1, _data.Events.Length);
            var flags = (IDictionary)Field("_eventFlags");
            // 末尾(Frame 90 = Clip 長 36 フレームを超える)に ⚠。複製で 1 つ後ろにずれる。
            var last = (Label)flags[count];
            Assert.IsTrue(last.IsShownForTest(), "Clip 長を超えたイベントに ⚠");
            Assert.IsFalse(((Label)flags[0]).IsShownForTest());
        }

        [Test]
        public void Sample_StateList_ListsEveryStateOfTheController_WithAnimDataPresence()
        {
            BuildSampleAndWindow();
            var list = (VisualElement)Field("_stateList");
            Assert.IsNotNull(list);
            // 12 ステート + BlendTree の 2 クリップ + Upper Body の 1 = 14 行。
            var section = Field("_stateSection");
            var fold = (Foldout)section.GetType().GetProperty("Fold").GetValue(section);
            fold.value = true;
            Assert.AreEqual(14, list.Query(className: "ux-fxrow").ToList().Count, "ステート / クリップ 1 件 = 1 行");
            var count = list.Query<Label>(className: "ux-itemlist__count").ToList()[0];
            StringAssert.Contains("14 ステート中 AnimData あり 6", count.text);
            StringAssert.Contains("14 ステート・AnimData あり 6", fold.text);
        }

        [Test]
        public void Sample_StateWithoutAnimData_IsReportedAsMissing()
        {
            BuildSampleAndWindow();
            var choices = (IList)Field("_clipChoices");
            var index = -1;
            for (var i = 0; i < choices.Count; i++)
            {
                var name = (string)choices[i].GetType().GetField("StateName").GetValue(choices[i]);
                if (name == "Attack3")
                {
                    index = i;
                }
            }

            Assert.GreaterOrEqual(index, 0, "Attack3 のステートがある");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimData>(TmpFolder + "/ANIM_Attack3_Sample.asset"));
            // 作成は AssetBrowser の作成パイプライン(実 Assets/GameData を触る)なので、ここでは作成せず「あり」「なし」の判定だけを確かめる。
            var find = EditorType.GetMethod("FindAnimDataFor", Inst);
            Assert.IsNull(find.Invoke(_window, new[] { choices[index] }), "Attack3 の AnimData は無い");
        }

        [Test]
        public void Sample_BlendShapeList_FlagsShapeMissingOnTheModel()
        {
            BuildSampleAndWindow();
            var list = (VisualElement)Field("_shapeList");
            Assert.AreEqual(3, list.Query(className: "ux-fxrow").ToList().Count, "BlendShape 1 本 = 1 行");
            var flags = (IDictionary)Field("_shapeFlags");
            Assert.IsFalse(((Label)flags[0]).IsShownForTest(), "mouth_A はモデルにある");
            Assert.IsFalse(((Label)flags[1]).IsShownForTest(), "eye_blink はモデルにある");
            Assert.IsTrue(((Label)flags[2]).IsShownForTest(), "smile はモデルに無いので ⚠");
        }

        [Test]
        public void Sample_AddAndRemoveBlendShape_WithUndo()
        {
            BuildSampleAndWindow();
            Call("AddShape", "brow_up");
            Assert.AreEqual(4, _data.BlendShapes.Length);
            Assert.AreEqual("brow_up", _data.BlendShapes[3].ShapeName);
            Undo.IncrementCurrentGroup();
            Call("RemoveShape", 3);
            Assert.AreEqual(3, _data.BlendShapes.Length);
            Undo.PerformUndo();
            Assert.AreEqual(4, _data.BlendShapes.Length, "Undo で戻る");
        }

        [Test]
        public void Sample_IkRow_HasFourToggles_AndTheWeightCurve()
        {
            BuildSampleAndWindow();
            var toggles = _window.rootVisualElement.Query<Toggle>(className: "ux-iktoggle").ToList();
            Assert.AreEqual(4, toggles.Count, "左手 / 右手 / 左足 / 右足");
            var paths = toggles.ConvertAll(t => t.bindingPath);
            CollectionAssert.AreEquivalent(new[] { "Ik.LeftHand", "Ik.RightHand", "Ik.LeftFoot", "Ik.RightFoot" }, paths, "Data の IK の 4 つに結びつく");
            Assert.IsTrue(_data.Ik.LeftHand && _data.Ik.RightFoot && !_data.Ik.RightHand && !_data.Ik.LeftFoot, "サンプルは左手と右足");
            Assert.AreEqual(1, _window.rootVisualElement.Query<CurveField>(className: "ux-ikcurve").ToList().Count);
        }

        [Test]
        public void Sample_ModelCheck_ReportsBlendShapeMissingOnTheModel()
        {
            BuildSampleAndWindow();
            var issues = (IList)Field("_modelIssues");
            Assert.AreEqual(1, issues.Count, "smile だけモデルに無い");
            var message = (string)issues[0].GetType().GetField("Message").GetValue(issues[0]);
            StringAssert.Contains("smile", message);
            var missing = (IEnumerable)Field("_missingShapes");
            CollectionAssert.Contains(new List<object>(Enumerate(missing)), "smile");
        }

        private static IEnumerable<object> Enumerate(IEnumerable e)
        {
            foreach (var x in e)
            {
                yield return x;
            }
        }

        [Test]
        public void Sample_ValidationErrorIsBoundToTheEventsField_AndShownInline()
        {
            BuildSampleAndWindow();
            var results = DDrive.Editor.Validation.DataValidationRunner.Run(_data);
            var overLength = results.Find(r => r.Message.Contains("Clip 長"));
            Assert.IsNotNull(overLength);
            Assert.AreEqual(nameof(AnimData.Events), overLength.Field, "Validator の結果に欄名が付く");
            var issues = _window.rootVisualElement.Query(className: "ux-issue").ToList();
            Assert.IsTrue(issues.Count >= 1, "欄の下にインラインで出る");
        }

        // ── 対象と再生(実 AnimManager) ──

        [Test]
        public void Open_WithAttach_SetsTheSceneTarget()
        {
            BuildSampleAndWindow(false);
            var sample = EditorAssembly.GetType("DDrive.Editor.Anim.AnimSample", true);
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(TmpFolder + "/AC_SwordFighter.controller");
            _animator = (Animator)sample.GetMethod("BuildAnimatorObject", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { controller });
            var open = EditorType.GetMethod("Open", new[] { typeof(AnimData), typeof(GameObject) });
            Assert.IsNotNull(open, "Open(AnimData, GameObject)(Presentation の「一緒に調整」の入口)");
            UnityEngine.Object.DestroyImmediate(_window);
            _window = null;
            open.Invoke(null, new object[] { _data, _animator.gameObject });
            var windows = Resources.FindObjectsOfTypeAll(EditorType);
            Assert.AreEqual(1, windows.Length);
            _window = (EditorWindow)windows[0];
            Assert.AreEqual(_animator, Field("_sceneTarget"), "Self の Animator がそのまま対象になる");
        }

        [Test]
        public void Sample_PlayAndStop_DrivesTheRealAnimManager()
        {
            BuildSampleAndWindow();
            var baseType = EditorType.BaseType;
            baseType.GetMethod("PlayMain", Inst).Invoke(_window, new object[] { false });
            Assert.IsTrue((bool)EditorType.GetProperty("IsPlaying", Inst).GetValue(_window), "▶ で再生が始まる");
            baseType.GetMethod("StopMain", Inst).Invoke(_window, null);
            Assert.IsFalse((bool)EditorType.GetProperty("IsPlaying", Inst).GetValue(_window), "■ で止まる");
        }
    }

    internal static class AnimEditorUxTestExtensions
    {
        // UxShow.IsShown は internal(別アセンブリのテストからは見えない)ので、USS クラスで同じ判定をする。
        public static bool IsShownForTest(this VisualElement e) => e != null && !e.ClassListContains("ux-hidden");
    }
}
