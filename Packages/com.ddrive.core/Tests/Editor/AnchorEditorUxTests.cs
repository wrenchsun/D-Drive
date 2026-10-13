using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Anchor;
using DDrive.Editor.Preview;
using UnityEditor.UIElements;
using DDrive.Editor.Inspector;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-11: 新 AnchorEditor / AnchorGroupEditor(Editor/Anchor)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(AnchorSample = 22 点の配置セット + 3 段の Anchor 連鎖)を一時フォルダに作って確かめる(実データは触らない)。
    public class AnchorEditorUxTests
    {
        private const string TmpFolder = "Assets/__AnchorEditorUxTestTmp";
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type AnchorType => EditorAssembly.GetType("DDrive.Editor.Anchor.AnchorEditor", true);
        private static Type GroupType => EditorAssembly.GetType("DDrive.Editor.Anchor.AnchorGroupEditor", true);

        private object _sample;
        private EditorWindow _window;
        private Type _windowType;

        [SetUp]
        public void SetUp() => UxTestWindows.CloseAll();

        [TearDown]
        public void TearDown()
        {
            UxTestWindows.CloseAll();
            _window = null;
            _sample = null;
            UxTestWindows.DeleteFolder(TmpFolder);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => UxTestWindows.Release();

        private void BuildSample()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.Anchor.AnchorSample", true);
            _sample = UxTestWindows.Batch(() => sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder }));
        }

        private AnchorData[] Anchors => (AnchorData[])_sample.GetType().GetField("Anchors").GetValue(_sample);

        private AnchorGroupData Main => (AnchorGroupData)_sample.GetType().GetField("Main").GetValue(_sample);

        private AnchorGroupData[] Groups => (AnchorGroupData[])_sample.GetType().GetField("Groups").GetValue(_sample);

        private VfxData[] Vfx => (VfxData[])_sample.GetType().GetField("Vfx").GetValue(_sample);

        private void OpenWindow(Type type, UnityEngine.Object target)
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();
            _windowType = type;
            _window = (EditorWindow)ScriptableObject.CreateInstance(type);
            var baseType = type.BaseType;
            baseType.GetField("_target", Inst).SetValue(_window, target);
            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                baseType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }
        }

        private object Field(string name) => _windowType.GetField(name, Inst).GetValue(_window);

        private void SetField(string name, object value) => _windowType.GetField(name, Inst).SetValue(_window, value);

        private object Call(string method, params object[] args)
        {
            for (var t = _windowType; t != null; t = t.BaseType)
            {
                var m = t.GetMethod(method, Inst | BindingFlags.DeclaredOnly);
                if (m != null)
                {
                    return m.Invoke(_window, args);
                }
            }

            Assert.Fail("メソッド " + method + " が見つかりません");
            return null;
        }

        private object Prop(string name)
        {
            for (var t = _windowType; t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, Inst | BindingFlags.DeclaredOnly);
                if (p != null)
                {
                    return p.GetValue(_window);
                }
            }

            Assert.Fail("プロパティ " + name + " が見つかりません");
            return null;
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

        private static void AssertGuideCoversEveryField<T>(string guideTypeName) where T : ScriptableObject
        {
            var inGuide = new HashSet<string>(GuideFields(guideTypeName));
            var data = ScriptableObject.CreateInstance<T>();
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

                Assert.IsEmpty(missing, guideTypeName + " に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    if (!field.StartsWith("(", StringComparison.Ordinal))
                    {
                        Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が " + typeof(T).Name + " にありません");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        private static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        // 実体のある VFX(ParticleSystem 付きプレハブ)を VfxData.Prefab に入れる。
        private static GameObject MakeVfxPrefab(string name)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{TmpFolder}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // ── 欄の表 ──

        [Test]
        public void AnchorFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            AssertGuideCoversEveryField<AnchorData>("DDrive.Editor.Anchor.AnchorFieldGuide");
        }

        [Test]
        public void AnchorGroupFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            AssertGuideCoversEveryField<AnchorGroupData>("DDrive.Editor.Anchor.AnchorGroupFieldGuide");
        }

        // ── 登録表([DataEditor]) ──

        [Test]
        public void Registry_NewEditorsFirst_OldOnesLabelledAndLast()
        {
            foreach (var (dataType, newType, oldType) in new[]
            {
                (typeof(AnchorData), AnchorType, typeof(AnchorEditorWindow)),
                (typeof(AnchorGroupData), GroupType, typeof(AnchorGroupEditorWindow)),
            })
            {
                var entries = DataEditorRegistry.GetEntries(dataType);
                Assert.AreEqual(newType, entries[0].WindowType, dataType.Name + " の先頭は新エディター");
                Assert.AreEqual(0, entries[0].Order);
                var old = entries[entries.Count - 1];
                Assert.AreEqual(oldType, old.WindowType);
                Assert.AreEqual(100, old.Order);
                StringAssert.StartsWith("(旧)", old.Label);
            }
        }

        [Test]
        public void OldWindows_StillOpen_WithOldTitle()
        {
            AnchorEditorWindow.Open(null);
            AnchorGroupEditorWindow.Open(null);
            var a = Resources.FindObjectsOfTypeAll<AnchorEditorWindow>();
            var g = Resources.FindObjectsOfTypeAll<AnchorGroupEditorWindow>();
            Assert.AreEqual(1, a.Length);
            Assert.AreEqual(1, g.Length);
            StringAssert.StartsWith("(旧)", a[0].titleContent.text);
            StringAssert.StartsWith("(旧)", g[0].titleContent.text);
        }

        // ── サンプル ──

        [Test]
        public void Sample_HasThreeLevelChain_AndMixedTwentyTwoPointGroup()
        {
            BuildSample();
            Assert.AreEqual(6, Anchors.Length);
            Assert.AreEqual(6, Groups.Length);
            var chain = AnchorChainEditor.CollectRootToTarget(Anchors[2]);
            Assert.AreEqual(3, chain.Count, "右手 → 銃口 → マズルフラッシュ");
            Assert.AreEqual(Anchors[0], chain[0]);

            var main = Main;
            var specs = new AnchorSpawnSpec[AnchorGroupData.MaxPoints];
            var count = AnchorGroupPlanner.EnumeratePoints(null, main, specs);
            Assert.AreEqual(22, count, "Grid 4×4 + 手置き 6");
            Assert.Greater(count, 20, "20 行を超える = 一覧は ListView");
            Assert.AreEqual(AnchorLayoutKind.Grid, main.Layout);
            Assert.AreEqual(6, main.Points.Length);
            Assert.AreEqual(4, main.Overrides.Length);
            Assert.IsTrue(main.Overrides[0].Skip);
            Assert.AreEqual(1, main.Children.Length);
            Assert.AreEqual(2, main.SharedVfx.Length);
            Assert.AreEqual(1, main.SharedSe.Length);
            Assert.IsTrue(main.OriginAnchorId.IsValid);
            var kinds = new HashSet<AnchorLayoutKind>();
            foreach (var g in Groups)
            {
                kinds.Add(g.Layout);
            }

            Assert.AreEqual(5, kinds.Count, "Manual / Grid / Circle / Line / Random が混在");
        }

        // ── Anchor Editor ──

        [Test]
        [Category("RequiresGraphics")]
        public void AnchorEditor_BuildsHeroCardsAndFooter_WithPadAsMainPart()
        {
            BuildSample();
            OpenWindow(AnchorType, Anchors[2]);
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNull(root.Q(className: "ux-bar__speed"), "Anchor は速度を持たない");
            // 出る位置 / 試し出し / 基準を決める / 親 Anchor / ばらつき / ヒエラルキーから作る / 管理情報(+ その他の検証)
            Assert.GreaterOrEqual(root.Query(className: "ux-card").ToList().Count, 7, "カード数");
            Assert.IsNotNull(root.Q(className: "ux-anchor"), "AnchorPad");
            Assert.AreEqual(2, root.Query<ObjectField>(className: "ux-slot__field").ToList().Count, "確認用 VFX / SE の欄");
            var buttons = root.Query<Button>().ToList();
            Assert.IsTrue(buttons.Exists(b => b.text == "選択した Transform から作成"), "ヒエラルキーから作る");
            Assert.IsTrue(buttons.Exists(b => b.text == "AnchorRig から一括生成"));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void AnchorEditor_PadWrite_UpdatesLocalOffset_AndUndoRestores()
        {
            BuildSample();
            var data = Anchors[1];
            OpenWindow(AnchorType, data);
            var pad = _window.rootVisualElement.Q(className: "ux-anchor");
            Assert.IsNotNull(pad);
            var before = data.LocalOffset;

            Undo.IncrementCurrentGroup();
            pad.GetType().GetMethod("Write", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pad, new object[] { new Vector3(1.5f, 0.4f, -2f), true });
            Assert.AreEqual(new Vector3(1.5f, 0.4f, -2f), data.LocalOffset, "パッドの書き込みが LocalOffset に入る");

            Undo.PerformUndo();
            Assert.AreEqual(before, data.LocalOffset, "Undo 1 回で戻る");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void AnchorEditor_ChainRow_ShowsRootToTarget_AndButtonsSwitchTarget()
        {
            BuildSample();
            OpenWindow(AnchorType, Anchors[2]);
            var root = _window.rootVisualElement;
            var chain = root.Q(className: "ux-anc-chain");
            Assert.IsNotNull(chain, "連鎖の行");
            var buttons = chain.Query<Button>().ToList();
            Assert.AreEqual(3, buttons.Count, "ルート → 親 → 自分");
            StringAssert.Contains("ANC_Sample_RightHand", buttons[0].text);
            Assert.IsFalse(buttons[2].enabledSelf, "自分自身は押せない");
            Click(buttons[1]);
            Assert.AreEqual(Anchors[1], AnchorType.BaseType.GetField("_target", Inst).GetValue(_window), "押した Anchor に切り替わる");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void AnchorEditor_OpenParent_SwitchesToParent()
        {
            BuildSample();
            OpenWindow(AnchorType, Anchors[1]);
            Call("OpenParent");
            Assert.AreEqual(Anchors[0], AnchorType.BaseType.GetField("_target", Inst).GetValue(_window));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void AnchorEditor_SceneHandleToggle_StopsSceneDrawing()
        {
            BuildSample();
            OpenWindow(AnchorType, Anchors[2]);
            Assert.IsTrue((bool)Prop("WantsSceneGui"), "既定は SceneView に表示");
            var toggle = _window.rootVisualElement.Query<Toggle>().ToList().Find(t => t.label == "SceneView に表示");
            Assert.IsNotNull(toggle, "SceneView 表示のトグル");
            SendChange(toggle, false);
            Assert.IsFalse((bool)Field("_sceneHandle"));
            Assert.IsFalse((bool)Prop("WantsSceneGui"), "OFF なら描かない");
            SendChange(toggle, true);
            Assert.IsTrue((bool)Prop("WantsSceneGui"));
        }

        private static void SendChange(Toggle toggle, bool value)
        {
            using (var e = ChangeEvent<bool>.GetPooled(!value, value))
            {
                e.target = toggle;
                toggle.SendEvent(e);
            }
        }

        [Test]
        [Category("RequiresGraphics")]
        public void AnchorEditor_ResolutionText_FollowsAttachTarget()
        {
            BuildSample();
            OpenWindow(AnchorType, Anchors[2]);
            var text = (string)Call("DescribeResolution");
            StringAssert.Contains("スポーン先が未指定", text);
            StringAssert.Contains("RightHand", text, "ルートの名前が出る");

            var rig = new GameObject("Rig");
            var hand = new GameObject("Hand_R");
            hand.transform.SetParent(rig.transform);
            try
            {
                SetField("_attachTarget", rig);
                text = (string)Call("DescribeResolution");
                StringAssert.Contains("Hand_R", text);
                StringAssert.Contains("✓", text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }
        }

        [Test]
        [Category("RequiresGraphics")]
        public void AnchorEditor_PlayWithTestVfx_DrivesRealManager_AtTheAnchorPosition()
        {
            BuildSample();
            var vfx = Vfx[0];
            vfx.Prefab = MakeVfxPrefab("AnchorTestFx");
            EditorUtility.SetDirty(vfx);
            var anchor = Anchors[4]; // 足元(World 固定)
            anchor.LocalOffset = new Vector3(1f, 0f, 2f);
            OpenWindow(AnchorType, anchor);
            SetField("_testVfx", vfx);

            Call("PlayMain", false);
            Assert.IsTrue((bool)Prop("IsPlaying"), "▶ で確認用 VFX が出る");
            var driver = (DDrive.Editor.Vfx.SceneVfxPreviewDriver)Field("_driver");
            var handle = (DDrive.Foundation.Handle.Handle<VfxMarker>)Field("_handle");
            var go = driver.Manager.GetGameObject(handle);
            Assert.IsNotNull(go, "実 VfxManager が生成した実体");
            Assert.AreEqual(new Vector3(1f, 0f, 2f), go.transform.position, "Anchor のオフセットの位置に出る");

            Call("StopMain");
            Assert.IsFalse((bool)Prop("IsPlaying"), "■ で止まる");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void VfxEditor_OpenAnchorEditor_OpensNewAnchorEditor()
        {
            BuildSample();
            var vfx = Vfx[1];
            vfx.AnchorId = new AssetId<AnchorMarker>(Anchors[3].Id, AssetType.Anchor);
            OpenWindow(EditorAssembly.GetType("DDrive.Editor.Vfx.VfxEditor", true), vfx);
            Call("OpenAnchorEditor");
            var windows = Resources.FindObjectsOfTypeAll(AnchorType);
            Assert.AreEqual(1, windows.Length, "新 AnchorEditor が開く");
            Assert.AreEqual(Anchors[3], AnchorType.BaseType.GetField("_target", Inst).GetValue(windows[0]));
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<AnchorEditorWindow>().Length, "旧は開かない");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void SeEditor_OpenAnchorEditor_OpensNewAnchorEditor()
        {
            BuildSample();
            var se = ((SeData[])_sample.GetType().GetField("Se").GetValue(_sample))[0];
            se.AnchorId = new AssetId<AnchorMarker>(Anchors[5].Id, AssetType.Anchor);
            OpenWindow(EditorAssembly.GetType("DDrive.Editor.Audio.SeEditor", true), se);
            Call("OpenAnchorEditor");
            var windows = Resources.FindObjectsOfTypeAll(AnchorType);
            Assert.AreEqual(1, windows.Length);
            Assert.AreEqual(Anchors[5], AnchorType.BaseType.GetField("_target", Inst).GetValue(windows[0]));
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<AnchorEditorWindow>().Length);
        }

        // ── Anchor Group Editor ──

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_BuildsHeroCardsFooter_AndPointListAsListView()
        {
            BuildSample();
            OpenWindow(GroupType, Main);
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"));
            Assert.IsNotNull(root.Q(className: "ux-bar"));
            // 試し出し / 点の一覧 / 配置パターン / 原点 / 出すもの / タイミング / 管理情報
            Assert.GreaterOrEqual(root.Query(className: "ux-card").ToList().Count, 7, "カード数");
            var list = root.Q(className: "ux-itemlist");
            Assert.IsNotNull(list, "点の一覧");
            Assert.IsNotNull(list.Q<ListView>(), "22 点 > 20 なので ListView");
            var count = list.Q<Label>(className: "ux-itemlist__count");
            StringAssert.Contains("22 点中 上書きあり 4", count.text);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_SmallGroup_ListsOneRowPerPoint_WithoutListView()
        {
            BuildSample();
            OpenWindow(GroupType, Groups[4]); // Manual 5 点
            var list = _window.rootVisualElement.Q(className: "ux-itemlist");
            Assert.IsNull(list.Q<ListView>(), "5 点は仮想化しない");
            Assert.AreEqual(5, list.Query(className: "ux-fxrow").ToList().Count, "1 点 = 1 行");
            Assert.AreEqual(5, list.Query<Label>(className: "ux-fxrow__name").ToList().FindAll(l => l.text.Length == 1).Count, "手置きの点は名前(A〜E)を出す");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_LayoutSwitch_ShowsOnlyMatchingPatternRows()
        {
            BuildSample();
            var data = Groups[1]; // Circle
            OpenWindow(GroupType, data);
            var conditional = (IList)Field("_conditional");
            Assert.Greater(conditional.Count, 17, "パターンの欄 17 + 手置きへの変換");
            Call("RefreshAll", true);
            var shown = 0;
            var applicable = 0;
            foreach (var item in conditional)
            {
                var row = (VisualElement)item.GetType().GetField("Row").GetValue(item);
                var visible = ((Func<bool>)item.GetType().GetField("Visible").GetValue(item))();
                if (visible)
                {
                    applicable++;
                    if (!row.ClassListContains("ux-hidden"))
                    {
                        shown++;
                    }
                }
                else
                {
                    Assert.IsTrue(row.ClassListContains("ux-hidden"), "Layout に合わない欄(Grid / Line / Random)は隠れる");
                }
            }

            Assert.AreEqual(5 + 1, applicable, "Circle の欄 5 + 変換(Circle はパターンなので出る)");
            Assert.GreaterOrEqual(shown, 3, "標準モードでも Circle の主な欄(点の数・半径)と変換は見える");

            data.Layout = AnchorLayoutKind.Manual;
            Call("RefreshAll", true);
            foreach (var item in conditional)
            {
                var row = (VisualElement)item.GetType().GetField("Row").GetValue(item);
                Assert.IsTrue(row.ClassListContains("ux-hidden"), "Manual ではパターンの欄も変換も隠れる");
            }
        }

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_SelectPoint_HighlightsRow_AndShowsSelectionText()
        {
            BuildSample();
            OpenWindow(GroupType, Groups[4]);
            Call("SelectPoint", 2);
            Assert.AreEqual(2, (int)Field("_selectedIndex"));
            var rows = _window.rootVisualElement.Query(className: "ux-fxrow").ToList();
            Assert.IsTrue(rows[2].ClassListContains("ux-fxrow--hl"), "選んだ行が強調される");
            Assert.IsFalse(rows[1].ClassListContains("ux-fxrow--hl"));
            var label = (Label)Field("_selectionLabel");
            StringAssert.Contains("#2", label.text);
            StringAssert.Contains("手置き", label.text);
            Call("SelectPoint", 2);
            Assert.AreEqual(-1, (int)Field("_selectedIndex"), "もう一度押すと解除");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_AddOverrideAndToggleSkip_UndoRestores()
        {
            BuildSample();
            var data = Groups[4];
            OpenWindow(GroupType, data);
            Assert.IsNull(data.Overrides);

            Undo.IncrementCurrentGroup();
            Call("ToggleSkip", 3);
            Assert.AreEqual(1, data.Overrides.Length);
            Assert.AreEqual(3, data.Overrides[0].Index);
            Assert.IsTrue(data.Overrides[0].Skip, "× で出さない");
            var list = _window.rootVisualElement.Q(className: "ux-itemlist");
            StringAssert.Contains("上書きあり 1", list.Q<Label>(className: "ux-itemlist__count").text);
            Undo.PerformUndo();
            Assert.IsTrue(data.Overrides == null || data.Overrides.Length == 0, "Undo 1 回で戻る");

            Undo.IncrementCurrentGroup();
            Call("AddOverride", 1, false);
            Assert.AreEqual(1, data.Overrides.Length);
            Assert.IsFalse(data.Overrides[0].Skip);
            Call("ToggleSkip", 1);
            Assert.IsTrue(data.Overrides[0].Skip, "既にある上書きの Skip を切り替える");
            Call("ToggleSkip", 1);
            Assert.IsFalse(data.Overrides[0].Skip, "もう一度で戻る");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_ConvertPatternToPoints_BakesPointsAndSwitchesToManual()
        {
            BuildSample();
            var data = Groups[1]; // Circle 8 点
            OpenWindow(GroupType, data);
            Undo.IncrementCurrentGroup();
            var n = (int)Call("ConvertPatternToPointsNow");
            Assert.AreEqual(8, n);
            Assert.AreEqual(AnchorLayoutKind.Manual, data.Layout);
            Assert.AreEqual(8, data.Points.Length);
            var list = _window.rootVisualElement.Q(className: "ux-itemlist");
            Assert.AreEqual(8, list.Query(className: "ux-fxrow").ToList().Count, "変換後も 8 点の一覧");
            Undo.PerformUndo();
            Assert.AreEqual(AnchorLayoutKind.Circle, data.Layout, "Undo で元のパターンに戻る");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_ApplyPatternDrag_ChangesPatternSize()
        {
            BuildSample();
            var data = Groups[1]; // Circle 半径 2
            OpenWindow(GroupType, data);
            SetField("_selectedIndex", 0);
            Call("ApplyPatternDrag", new Vector3(0f, 0f, 3.5f));
            Assert.AreEqual(3.5f, data.CircleRadius, 1e-4f, "0 番の点を引くと半径が変わる");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_Play_PlansAllPoints_OrOnlyTheSelectedOne()
        {
            BuildSample();
            var vfx = Vfx[0];
            vfx.Prefab = MakeVfxPrefab("AnchorGroupTestFx");
            var data = Groups[4]; // Manual 5 点・SharedVfx = vfx[0]
            OpenWindow(GroupType, data);

            Call("PlayPreview", -1);
            var handles = (IList)Field("_handles");
            Assert.AreEqual(5, handles.Count, "全点を実 Manager で出す");
            Assert.IsTrue((bool)Prop("IsPlaying"));
            Call("StopMain");
            Assert.AreEqual(0, handles.Count);

            Call("PlayPreview", 2);
            Assert.AreEqual(1, handles.Count, "選んだ 1 点だけ");
            Call("StopMain");
        }

        [Test]
        public void GroupEditor_OpenWithAttach_PassesSpawnTarget()
        {
            BuildSample();
            var attach = new GameObject("Self");
            try
            {
                GroupType.GetMethod("Open", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AnchorGroupData), typeof(GameObject) }, null)
                    .Invoke(null, new object[] { Main, attach });
                var windows = Resources.FindObjectsOfTypeAll(GroupType);
                Assert.AreEqual(1, windows.Length);
                Assert.AreEqual(attach, GroupType.GetField("_attachTarget", Inst).GetValue(windows[0]), "スポーン先がそのまま入る");
                Assert.AreEqual(Main, GroupType.BaseType.GetField("_target", Inst).GetValue(windows[0]));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(attach);
            }
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Presentation_OpenTrackEditorTogether_OpensGroupEditorWithAttach()
        {
            BuildSample();
            var presentation = ScriptableObject.CreateInstance<PresentationData>();
            try
            {
                var track = DDrive.Editor.Presentation.PresentationTrackEditOps.CreateTrack(TrackKind.AnchorGroup, 0f, Main);
                presentation.Tracks = new[] { track };
                var presentationType = EditorAssembly.GetType("DDrive.Editor.Presentation.PresentationEditor", true);
                OpenWindow(presentationType, presentation);
                Call("OpenTrackEditorTogether", 0);
                var windows = Resources.FindObjectsOfTypeAll(GroupType);
                Assert.AreEqual(1, windows.Length, "新 AnchorGroupEditor が開く");
                Assert.AreEqual(Main, GroupType.BaseType.GetField("_target", Inst).GetValue(windows[0]));
                Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<AnchorGroupEditorWindow>().Length, "旧は開かない");
            }
            finally
            {
                if (_window != null)
                {
                    EditorUtility.ClearDirty(presentation);
                    _window.Close();
                    _window = null;
                }

                UnityEngine.Object.DestroyImmediate(presentation);
            }
        }

        [Test]
        [Category("RequiresGraphics")]
        public void GroupEditor_OriginChain_ShowsAnchorChainWithOpenButtons()
        {
            BuildSample();
            OpenWindow(GroupType, Main); // 原点 = 銃口(右手 → 銃口)
            var root = _window.rootVisualElement;
            var chains = root.Query(className: "ux-anc-chain").ToList();
            Assert.AreEqual(1, chains.Count);
            var buttons = chains[0].Query<Button>().ToList();
            Assert.AreEqual(2, buttons.Count);
            Click(buttons[1]);
            var windows = Resources.FindObjectsOfTypeAll(AnchorType);
            Assert.AreEqual(1, windows.Length, "押すと Anchor Editor で開く");
            Assert.AreEqual(Anchors[1], AnchorType.BaseType.GetField("_target", Inst).GetValue(windows[0]));
        }
    }
}
