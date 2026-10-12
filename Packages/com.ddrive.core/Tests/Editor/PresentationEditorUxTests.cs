using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Inspector;
using DDrive.Editor.Presentation;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-5 / D-10: 新 PresentationEditor(Editor/Presentation)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(PresentationSample = 「剣の一撃」: トラック 13 本・Signal / Marker あり・Anchor / Params の混在・Asset 未設定のエラー 1 本)を
    // 一時フォルダに作って確かめる(実データは触らない)。
    public class PresentationEditorUxTests
    {
        private const string TmpFolder = "Assets/__PresentationEditorUxTestTmp";
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type EditorType => EditorAssembly.GetType("DDrive.Editor.Presentation.PresentationEditor", true);
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private PresentationData _data;
        private EditorWindow _window;

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                EditorUtility.ClearDirty(_data);
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
            var sample = EditorAssembly.GetType("DDrive.Editor.Presentation.PresentationSample", true);
            var result = sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder });
            _data = (PresentationData)result.GetType().GetField("Presentation").GetValue(result);
            _window = (EditorWindow)ScriptableObject.CreateInstance(EditorType);
            var baseType = EditorType.BaseType;
            baseType.GetField("_target", Inst).SetValue(_window, _data);
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
        public void PresentationFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var guideType = EditorAssembly.GetType("DDrive.Editor.Presentation.PresentationFieldGuide", true);
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var inGuide = new HashSet<string>();
            var tiers = new Dictionary<string, int>();
            foreach (var e in (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance))
            {
                var field = (string)e.GetType().GetField("Field").GetValue(e);
                inGuide.Add(field);
                tiers[field] = (int)e.GetType().GetField("Tier").GetValue(e);
            }

            var data = ScriptableObject.CreateInstance<PresentationData>();
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

                Assert.IsEmpty(missing, "PresentationFieldGuide に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    // "(" で始まる欄は Data のフィールドではなく補助の行(タイムライン・Signal・環境など)。
                    if (!field.StartsWith("(", StringComparison.Ordinal))
                    {
                        Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が PresentationData にありません");
                    }
                }

                // 必須の段 = Validator が Error にする欄(Tracks の Asset 空・SignalKey 空)。
                Assert.AreEqual(0, tiers[nameof(PresentationData.Tracks)], "Tracks は必須");
                Assert.AreNotEqual(0, tiers[nameof(PresentationData.TotalDuration)]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void PresentationData_NewEditorIsPrimary_OldIsSecondWithOrder100()
        {
            var entries = new List<DataEditorRegistry.Entry>(DataEditorRegistry.GetEntries(typeof(PresentationData)));
            Assert.GreaterOrEqual(entries.Count, 2);
            Assert.AreEqual(EditorType, entries[0].WindowType, "新が第 1 候補");
            Assert.AreEqual(0, entries[0].Order);
            Assert.AreEqual(typeof(PresentationEditorWindow), entries[1].WindowType, "旧が第 2 候補");
            Assert.AreEqual(100, entries[1].Order);
            StringAssert.StartsWith("(旧)", entries[1].Label);
        }

        // ── 実運用規模のサンプルで開く ──

        [Test]
        public void Sample_BuildsHeroCardsAndFooter_WithPlayback()
        {
            BuildSampleAndWindow();
            var root = _window.rootVisualElement;
            Assert.AreEqual(13, _data.Tracks.Length);
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNotNull(root.Q(className: "ux-bar__speed"), "速度");
            // 流れ / トラック / 合図 / 確認する / 管理情報 / その他の検証
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
        public void Sample_TrackList_IsOneRowPerTrack_WithCountAndFilters()
        {
            BuildSampleAndWindow();
            var list = Field("_trackList");
            var rows = ((VisualElement)list).Query(className: "ux-fxrow").ToList();
            Assert.AreEqual(13, rows.Count, "トラック 1 本 = 1 行");
            var count = _window.rootVisualElement.Query<Label>(className: "ux-itemlist__count").ToList()[0];
            StringAssert.Contains("13 トラック中 調整あり", count.text);
            // Kind チップ + 検索 + 調整あり / なしの絞り込み。
            var chips = _window.rootVisualElement.Query<Button>(className: "ux-filter").ToList();
            Assert.IsNotNull(chips.Find(b => b.text == "調整あり"));
            Assert.IsNotNull(chips.Find(b => b.text == "Vfx"));
            Click(chips.Find(b => b.text == "調整あり"));
            var shown = (int)list.GetType().GetProperty("ShownCount").GetValue(list);
            Assert.Less(shown, 13);
            Assert.Greater(shown, 0);
            // 行の要約(時刻・位置・Params)が 1 行で出る。
            var summaries = _window.rootVisualElement.Query<Label>(className: "ux-fxrow__summary").ToList();
            Assert.IsTrue(summaries.Exists(l => l.text.Contains("t=0.12s") && l.text.Contains("位置: Self") && l.text.Contains("Params 2")), "Vfx トラックの要約");
        }

        [Test]
        public void Sample_TrackList_MoreThan20_UsesListView()
        {
            BuildSampleAndWindow();
            var tracks = new List<PresentationTrack>(_data.Tracks);
            for (var i = 0; i < 10; i++)
            {
                tracks.Add(PresentationTrackEditOps.CreateTrack(TrackKind.HitStop, 0.1f * i, null));
            }

            _data.Tracks = tracks.ToArray();
            var section = Field("_trackSection");
            section.GetType().GetMethod("Rebuild").Invoke(section, null);
            var lvs = _window.rootVisualElement.Query<ListView>(className: "ux-itemlist__lv").ToList();
            Assert.AreEqual(1, lvs.Count, "20 行を超えたら ListView(仮想化)");
        }

        [Test]
        public void Sample_ChangeOneTrackTime_AndUndo()
        {
            BuildSampleAndWindow();
            EditorType.GetField("_openTrack", Inst).SetValue(_window, 3);
            EditorType.GetField("_selectedTrack", Inst).SetValue(_window, 3);
            Call("ReRenderTrackList");
            Assert.IsNotNull(FindProp("Time(秒)"), "展開した行に Time の欄がある");
            Assert.AreEqual(0.12f, _data.Tracks[3].Time, 1e-4f);
            // 欄の編集(PropertyField)は実ウィンドウの execute_code で確認する。ここではタイムラインのマーカーのドラッグと同じ経路(SetTrackTime)で時刻を変える。
            Undo.IncrementCurrentGroup();
            PresentationTrackEditOps.SetTrackTime(_data, 3, 0.5f);
            Call("AfterDataWrite");
            Assert.AreEqual(0.5f, _data.Tracks[3].Time, 1e-4f);
            var summaries = _window.rootVisualElement.Query<Label>(className: "ux-fxrow__summary").ToList();
            Assert.IsTrue(summaries.Exists(l => l.text.Contains("t=0.5s")), "要約が追従する");
            Undo.PerformUndo();
            Assert.AreEqual(0.12f, _data.Tracks[3].Time, 1e-4f, "Undo で戻る");
        }

        private PropertyField FindProp(string label)
        {
            foreach (var pf in _window.rootVisualElement.Query<PropertyField>().ToList())
            {
                if (pf.label == label)
                {
                    return pf;
                }
            }

            return null;
        }

        [Test]
        public void Sample_AddAndRemoveOneTrack()
        {
            BuildSampleAndWindow();
            var before = _data.Tracks.Length;
            Call("AddTrack", TrackKind.Vfx, 0.7f, null);
            Assert.AreEqual(before + 1, _data.Tracks.Length, "トラック 1 本を追加");
            Assert.AreEqual(0.7f, _data.Tracks[before].Time, 1e-4f);
            var rows = ((VisualElement)Field("_trackList")).Query(className: "ux-fxrow").ToList();
            Assert.AreEqual(before + 1, rows.Count);
            Undo.IncrementCurrentGroup();
            Call("RemoveTrack", before);
            Assert.AreEqual(before, _data.Tracks.Length, "トラック 1 本を削除");
            Undo.PerformUndo();
            Assert.AreEqual(before + 1, _data.Tracks.Length, "Undo で戻る");
        }

        [Test]
        public void Sample_MoveAndDuplicateTrack()
        {
            BuildSampleAndWindow();
            var first = _data.Tracks[0].Kind;
            var second = _data.Tracks[1].Kind;
            Call("MoveTrack", 0, 1);
            Assert.AreEqual(second, _data.Tracks[0].Kind);
            Assert.AreEqual(first, _data.Tracks[1].Kind);
            var count = _data.Tracks.Length;
            Call("DuplicateTrack", 0);
            Assert.AreEqual(count + 1, _data.Tracks.Length);
        }

        [Test]
        public void Sample_SignalButtons_ListOnSignalKeys_DisabledWhileStopped()
        {
            BuildSampleAndWindow();
            var buttons = _window.rootVisualElement.Query<Button>().ToList();
            var signal = buttons.Find(b => b.text == "Signal: parry");
            Assert.IsNotNull(signal, "OnSignal のキーごとにボタン");
            Call("UpdateSignalButtonsEnabled", false);
            Assert.IsFalse(signal.enabledSelf, "停止中は無効");
        }

        [Test]
        public void Sample_ShowsTrackFlag_ForTrackWithMissingAsset()
        {
            BuildSampleAndWindow();
            var issues = (IDictionary)Field("_trackIssue");
            Assert.IsTrue(issues.Contains(12), "Asset 未設定のトラックに ⚠ が付く");
            StringAssert.Contains("Asset", (string)issues[12]);
        }

        [Test]
        public void Sample_ReturnsExpectedAdjustedCount()
        {
            BuildSampleAndWindow();
            // 位置 / Params / Scope に手を入れたトラック: Vfx 2 本(位置あり)・CameraShake(Scope)・HitStop(Params)の 4 本。
            var adjusted = 0;
            for (var i = 0; i < _data.Tracks.Length; i++)
            {
                if ((bool)Call("TrackAdjusted", i))
                {
                    adjusted++;
                }
            }

            Assert.AreEqual(4, adjusted);
        }

        [Test]
        public void Sample_TuneKnobsInOpenRow_ShowParamsOfTheTrack()
        {
            BuildSampleAndWindow();
            EditorType.GetField("_openTrack", Inst).SetValue(_window, 3);
            Call("ReRenderTrackList");
            var knobs = _window.rootVisualElement.Query(className: "ux-knob").ToList();
            Assert.AreEqual(2, knobs.Count, "Params 2 つ = つまみ 2 本");
            var pads = _window.rootVisualElement.Query(className: "ux-anchor").ToList();
            Assert.AreEqual(1, pads.Count, "Vfx トラックは AnchorPad");
        }
    }
}
