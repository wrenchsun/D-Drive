using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Inspector;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim2D;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-7 / D-10: 新 Anim2DEditor(Editor/Anim2D)+ 作成ウィザード。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(Anim2DSample = 「勇者 歩き」: 24 コマ・8 方向・イベント 12 件)を一時フォルダに作って確かめる(実データは触らない)。
    public class Anim2DEditorUxTests
    {
        private const string TmpFolder = "Assets/__Anim2DEditorUxTestTmp";
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type EditorType => EditorAssembly.GetType("DDrive.Editor.Anim2D.Anim2DEditor", true);
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private Anim2DData _data;
        private EditorWindow _window;

        [SetUp]
        public void SetUp() => UxTestWindows.CloseAll();

        [TearDown]
        public void TearDown()
        {
            UxTestWindows.CloseAll();
            _window = null;
            _data = null;
            UxTestWindows.DeleteFolder(TmpFolder);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => UxTestWindows.Release();

        private void BuildSampleAndWindow()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();
            var sample = EditorAssembly.GetType("DDrive.Editor.Anim.Anim2DSample", true);
            var result = sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder }); // 画像のインポートが要る(スプライトを読む)ので Batch にしない
            _data = (Anim2DData)result.GetType().GetField("Anim").GetValue(result);

            _window = (EditorWindow)ScriptableObject.CreateInstance(EditorType);
            var baseType = EditorType.BaseType;
            baseType.GetField("_target", Inst).SetValue(_window, _data);
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
        public void Anim2DFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var guideType = EditorAssembly.GetType("DDrive.Editor.Anim2D.Anim2DFieldGuide", true);
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var inGuide = new HashSet<string>();
            var tiers = new Dictionary<string, int>();
            foreach (var e in (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance))
            {
                var field = (string)e.GetType().GetField("Field").GetValue(e);
                inGuide.Add(field);
                tiers[field] = (int)e.GetType().GetField("Tier").GetValue(e);
            }

            var data = ScriptableObject.CreateInstance<Anim2DData>();
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

                Assert.IsEmpty(missing, "Anim2DFieldGuide に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    if (!field.StartsWith("(", StringComparison.Ordinal))
                    {
                        Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が Anim2DData にありません");
                    }
                }

                Assert.AreEqual(0, tiers["Clip"], "Clip は必須");
                Assert.AreNotEqual(0, tiers["Events"]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void AnimEditor_RejectsAnim2DData_UnlessOpenedForAnim2D()
        {
            var animEditor = EditorAssembly.GetType("DDrive.Editor.Anim.AnimEditor", true);
            var accepts = animEditor.GetMethod("AcceptsTarget", Inst);
            var w = (EditorWindow)ScriptableObject.CreateInstance(animEditor);
            var data = ScriptableObject.CreateInstance<Anim2DData>();
            try
            {
                Assert.IsFalse((bool)accepts.Invoke(w, new object[] { data }), "新 AnimEditor は Anim2DData を直接は対象にしない(Anim2DEditor へ向ける)");
                animEditor.GetField("_allowAnim2D", Inst).SetValue(w, true);
                Assert.IsTrue((bool)accepts.Invoke(w, new object[] { data }), "OpenForAnim2D で開かれたときは対象にできる(イベントのタイムライン編集)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(w);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        // ── 実運用規模のサンプルで開く ──

        [Test]
        [Category("RequiresGraphics")]
        public void Sample_BuildsHeroCardsAndFooter_WithPlayback()
        {
            BuildSampleAndWindow();
            var root = _window.rootVisualElement;
            Assert.AreEqual(12, _data.Events.Length);
            Assert.AreEqual(8, _data.DirectionClips.Length);
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNotNull(root.Q(className: "ux-bar__speed"), "速度");
            // 動かす / 向き / コマ / 合図 / 確認する / 管理情報 / その他の検証
            Assert.AreEqual(7, root.Query(className: "ux-card").ToList().Count, "カード数");
        }

        [Test]
        [Category("RequiresGraphics")]
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
        [Category("RequiresGraphics")]
        public void Sample_FrameList_IsOneRowPerFrame_AndUsesListViewAbove20()
        {
            BuildSampleAndWindow();
            var list = (VisualElement)Field("_frameList");
            Assert.AreEqual(24, (int)list.GetType().GetProperty("TotalCount").GetValue(list), "24 コマ");
            var count = list.Query<Label>(className: "ux-itemlist__count").ToList()[0];
            StringAssert.Contains("24 コマ中 絵あり 24", count.text);
            Assert.AreEqual(1, list.Query<ListView>(className: "ux-itemlist__lv").ToList().Count, "20 行を超えたら ListView(仮想化)");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Sample_DirectionList_IsOneRowPerDirection_WithAnglesAndCount()
        {
            BuildSampleAndWindow();
            var list = (VisualElement)Field("_dirList");
            Assert.AreEqual(8, list.Query(className: "ux-fxrow").ToList().Count, "方向 1 つ = 1 行");
            var count = list.Query<Label>(className: "ux-itemlist__count").ToList()[0];
            StringAssert.Contains("8 方向中 クリップあり 8", count.text);
            var chips = list.Query<Label>(className: "ux-fxrow__chip").ToList();
            Assert.IsTrue(chips.Exists(l => l.text == "315°"), "角度のチップ");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Sample_SetDirectionClip_ChangesData_FlagsEmpty_AndUndoRestores()
        {
            BuildSampleAndWindow();
            var before = _data.DirectionClips[3];
            Undo.IncrementCurrentGroup();
            Call("SetDirectionClip", 3, null);
            Assert.IsNull(_data.DirectionClips[3], "クリップ 1 行の変更がデータに入る");
            var list = (VisualElement)Field("_dirList");
            Assert.AreEqual(1, list.Query<Label>(className: "ux-lineflag").ToList().Count, "空のスロットに ⚠");
            StringAssert.Contains("クリップあり 7", list.Query<Label>(className: "ux-itemlist__count").ToList()[0].text);
            Undo.PerformUndo();
            Assert.AreEqual(before, _data.DirectionClips[3], "Undo で戻る");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Sample_EventList_IsOneRowPerEvent_WithCountAndFilters()
        {
            BuildSampleAndWindow();
            var list = (VisualElement)Field("_eventList");
            Assert.AreEqual(12, list.Query(className: "ux-fxrow").ToList().Count, "イベント 1 件 = 1 行");
            StringAssert.Contains("12 イベント中 対象あり 12", list.Query<Label>(className: "ux-itemlist__count").ToList()[0].text);
            var chips = list.Query<Button>(className: "ux-filter").ToList();
            Click(chips.Find(b => b.text == "VFX"));
            var shown = (int)list.GetType().GetProperty("ShownCount").GetValue(list);
            Assert.AreEqual(4, shown, "VFX のチップで 4 件(土煙 ×2・火花・オーラ)");
            var summaries = list.Query<Label>(className: "ux-fxrow__summary").ToList();
            Assert.IsTrue(summaries.Exists(l => l.text.Contains("F4") && l.text.Contains("0.33s")), "Frame イベントの要約(12fps)");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Sample_OverLengthEvent_ShowsFlag_AndInlineError()
        {
            BuildSampleAndWindow();
            var list = (VisualElement)Field("_eventList");
            Assert.AreEqual(1, list.Query<Label>(className: "ux-lineflag").ToList().Count, "Clip 長を超えたイベントに ⚠(1 件)");
            // AnimDataValidator の検査(Clip の長さ超え)が Events の行の下に出る。
            Assert.IsTrue(_window.rootVisualElement.Query(className: "ux-issue").ToList().Count > 0, "検証がインラインに出る");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Sample_ApplyRetiming_UniformRewritesClipTimes()
        {
            BuildSampleAndWindow();
            EditorType.GetField("_placement", Inst).SetValue(_window, DDrive.Editor.Anim2D.PlacementMode.Uniform);
            Undo.IncrementCurrentGroup();
            Call("ApplyRetiming");
            Assert.IsTrue(DDrive.Editor.Anim2D.AnimationClipEditorUtility.LoadSprites(_data.Clip, out var sprites, out var times));
            Assert.AreEqual(24, sprites.Length);
            Assert.Greater(times[1], times[0], "時刻が単調増加");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Sample_PlayAndStop_DrivesRealAnimManager()
        {
            BuildSampleAndWindow();
            Call("PlacePreview");
            var preview = (GameObject)Field("_previewObject");
            Assert.IsNotNull(preview, "プレビュー物(SpriteRenderer + Animator)が置かれる");
            Assert.IsNotNull(preview.GetComponent<SpriteRenderer>().sprite, "先頭コマの絵が入る");
            Call("PlayFresh");
            Assert.IsTrue((bool)EditorType.GetProperty("IsPlaying", Inst).GetValue(_window), "▶ で実 AnimManager が再生中");
            Call("StopAnim");
            Assert.IsFalse((bool)EditorType.GetProperty("IsPlaying", Inst).GetValue(_window), "■ で止まる");
            Call("RemovePreview");
            Assert.IsNull(Field("_previewObject"));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void EmptyState_HasNewCreateButton()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();
            _window = (EditorWindow)ScriptableObject.CreateInstance(EditorType);
            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                EditorType.BaseType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }

            var buttons = _window.rootVisualElement.Query<Button>().ToList();
            Assert.IsTrue(buttons.Exists(b => b.text == "＋ 新規作成"), "空状態の「＋ 新規作成」");
        }

        // ── 作成ウィザード ──

        [Test]
        public void Wizard_PagesBuild_AndNextIsBlockedUntilInputsAreValid()
        {
            var wizardType = EditorAssembly.GetType("DDrive.Editor.Anim2D.Anim2DCreateWizard", true);
            var open = wizardType.GetMethod("OpenAt", BindingFlags.Static | BindingFlags.NonPublic);
            var w = (EditorWindow)open.Invoke(null, new object[] { 0 });
            try
            {
                var go = wizardType.GetMethod("Go", Inst);
                var root = w.rootVisualElement;
                Assert.AreEqual(5, root.Query<Button>(className: "ux-wz__tile").ToList().Count, "雛形 5 種");
                go.Invoke(w, new object[] { 1 });
                var next = (Button)wizardType.GetField("_next", Inst).GetValue(w);
                Assert.IsFalse(next.enabledSelf, "素材を選ぶまで次へ進めない");
                go.Invoke(w, new object[] { 2 });
                Assert.IsFalse(((Button)wizardType.GetField("_next", Inst).GetValue(w)).enabledSelf, "Name / State を入れるまで次へ進めない");
                go.Invoke(w, new object[] { 3 });
                Assert.IsNotNull(root.Q(className: "ux-wz__line"), "確認ページの要約");
            }
            finally
            {
                w.Close();
            }
        }

        [Test]
        public void CreateService_BuildsClipAndAnim2DData_FromASheet()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.Anim.Anim2DSample", true);
            var result = sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder }); // 画像のインポートが要る(スプライトを読む)ので Batch にしない
            var sheet = (Texture2D)result.GetType().GetField("Sheet").GetValue(result);
            var root = TestTempFolder.Root + "/Ux7GameData";
            var created = default(Anim2DData);
            try
            {
                var requestType = EditorAssembly.GetType("DDrive.Editor.Anim2D.Anim2DCreateRequest", true);
                var request = Activator.CreateInstance(requestType);
                void Set(string name, object value) => requestType.GetField(name).SetValue(request, value);
                Set("Texture", sheet);
                Set("Slice", Enum.ToObject(EditorAssembly.GetType("DDrive.Editor.Anim2D.Anim2DSliceMode", true), 0));
                Set("GridColumns", 8);
                Set("GridRows", 3);
                Set("GridSpriteCount", 24);
                Set("Name", "Ux7");
                Set("State", "Walk");
                Set("ClipFolder", TmpFolder);
                Set("GameDataRoot", root);
                var service = EditorAssembly.GetType("DDrive.Editor.Anim2D.Anim2DCreateService", true);
                var args = new object[] { request, null };
                created = (Anim2DData)service.GetMethod("Create", BindingFlags.Static | BindingFlags.Public).Invoke(null, args);
                Assert.IsNotNull(created, "1 件作れる: " + args[1]);
                Assert.IsNotNull(created.Clip);
                Assert.AreEqual("Walk", created.StateName);
                Assert.IsTrue(DDrive.Editor.Anim2D.AnimationClipEditorUtility.LoadSprites(created.Clip, out var sprites, out _));
                Assert.AreEqual(24, sprites.Length);
                Assert.AreNotEqual(0UL, created.Id);
                var inputs = (string)service.GetMethod("CheckNaming", BindingFlags.Static | BindingFlags.Public).Invoke(null, new[] { request });
                Assert.IsNull(inputs);
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(root))
                {
                    AddressablesSync.RemoveEntriesUnder(root);
                    AssetDatabase.DeleteAsset(root);
                    using (DDrive.Editor.Versioning.VersionStampSuppression.Scope())
                    {
                        AssetDatabase.SaveAssets();
                    }
                }
            }
        }
    }
}
