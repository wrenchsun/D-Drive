using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.CameraFx;
using DDrive.Editor.Inspector;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-10: 新 ShakeEditor / HapticsEditor(Editor/Camera)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 雛形 10 種をすべて使ったサンプル(CameraFxSample)を一時フォルダに作って確かめる(実データは触らない)。
    public class CameraFxEditorUxTests
    {
        private const string TmpFolder = "Assets/__CameraFxEditorUxTestTmp";
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type ShakeType => EditorAssembly.GetType("DDrive.Editor.CameraFx.ShakeEditor", true);
        private static Type HapticsType => EditorAssembly.GetType("DDrive.Editor.CameraFx.HapticsEditor", true);
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private object _sample;
        private EditorWindow _window;
        private Type _windowType;

        [TearDown]
        public void TearDown()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && (w.GetType() == ShakeType || w.GetType() == HapticsType || w.GetType() == typeof(CameraFxEditorWindow)))
                {
                    var data = w.GetType() == typeof(CameraFxEditorWindow) ? null : w.GetType().BaseType.GetField("_target", Inst).GetValue(w) as UnityEngine.Object;
                    if (data != null)
                    {
                        EditorUtility.ClearDirty(data);
                    }

                    UnityEngine.Object.DestroyImmediate(w);
                }
            }

            _window = null;
            _sample = null;
            if (AssetDatabase.IsValidFolder(TmpFolder))
            {
                AssetDatabase.DeleteAsset(TmpFolder);
            }
        }

        private object BuildSample()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.CameraFx.CameraFxSample", true);
            _sample = sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder });
            return _sample;
        }

        private CameraShakeData[] Shakes => (CameraShakeData[])_sample.GetType().GetField("Shakes").GetValue(_sample);

        private HapticsData[] Haptics => (HapticsData[])_sample.GetType().GetField("Haptics").GetValue(_sample);

        private void OpenWindow(Type type, UnityEngine.Object target)
        {
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

        private object Call(string method, params object[] args)
        {
            var m = _windowType.GetMethod(method, Inst) ?? _windowType.BaseType.GetMethod(method, Inst);
            Assert.IsNotNull(m, method);
            return m.Invoke(_window, args);
        }

        private static IEnumerable<string> GuideFields(string guideTypeName, out Dictionary<string, int> tiers)
        {
            var guideType = EditorAssembly.GetType(guideTypeName, true);
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var list = new List<string>();
            tiers = new Dictionary<string, int>();
            foreach (var e in (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance))
            {
                var field = (string)e.GetType().GetField("Field").GetValue(e);
                list.Add(field);
                tiers[field] = (int)e.GetType().GetField("Tier").GetValue(e);
            }

            return list;
        }

        private static void AssertGuideCoversEveryField<T>(string guideTypeName) where T : ScriptableObject
        {
            var inGuide = new HashSet<string>(GuideFields(guideTypeName, out _));
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

        // ── 欄の表 ──

        [Test]
        public void ShakeFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            AssertGuideCoversEveryField<CameraShakeData>("DDrive.Editor.CameraFx.ShakeFieldGuide");
        }

        [Test]
        public void HapticsFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            AssertGuideCoversEveryField<HapticsData>("DDrive.Editor.CameraFx.HapticsFieldGuide");
        }

        // ── 登録表([DataEditor]) ──

        [Test]
        public void Registry_NewEditorsFirst_OldOneLabelledAndLast()
        {
            foreach (var (dataType, newType) in new[] { (typeof(CameraShakeData), ShakeType), (typeof(HapticsData), HapticsType) })
            {
                var entries = DataEditorRegistry.GetEntries(dataType);
                Assert.AreEqual(newType, entries[0].WindowType, dataType.Name + " の先頭は新エディター");
                Assert.AreEqual(0, entries[0].Order);
                var old = entries[entries.Count - 1];
                Assert.AreEqual(typeof(CameraFxEditorWindow), old.WindowType);
                Assert.AreEqual(100, old.Order);
                StringAssert.StartsWith("(旧)", old.Label);
            }
        }

        [Test]
        public void OldWindow_StillOpens_WithOldTitle()
        {
            CameraFxEditorWindow.Open(null);
            var windows = Resources.FindObjectsOfTypeAll<CameraFxEditorWindow>();
            Assert.AreEqual(1, windows.Length);
            StringAssert.StartsWith("(旧)", windows[0].titleContent.text);
        }

        // ── サンプル(雛形 10 種) ──

        [Test]
        public void Sample_HasTenShakesAndTenHaptics_OnePerPreset()
        {
            BuildSample();
            Assert.AreEqual(10, Shakes.Length);
            Assert.AreEqual(10, Haptics.Length);
            Assert.AreEqual(CameraFxPresets.All.Length, Shakes.Length);
            // 雛形ごとに波形が違う(Explosion は長さ 0.6 秒の Perlin、Earthquake は 3 秒のループ)。
            Assert.AreEqual(ShakePattern.PerlinNoise, Shakes[3].Pattern);
            Assert.AreEqual(0.6f, Shakes[3].Envelope.Duration, 1e-4f);
            Assert.AreEqual(3f, Shakes[7].Envelope.Duration, 1e-4f);
            Assert.AreEqual(HapticPriority.High, Haptics[3].Priority);
        }

        // ── Shake Editor ──

        [Test]
        public void ShakeEditor_BuildsHeroCardsAndFooter()
        {
            BuildSample();
            OpenWindow(ShakeType, Shakes[3]);
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNull(root.Q(className: "ux-bar__speed"), "Shake は速度を持たない");
            // 雛形 / 波形 / 揺れ方 / 強さ / 重なったとき / 確認する / 管理情報(その他の検証はサンプルの未登録で出ることがある)
            Assert.GreaterOrEqual(root.Query(className: "ux-card").ToList().Count, 7, "カード数");
            Assert.IsNotNull(root.Q(className: "ux-fxp"), "雛形のボタン列");
            Assert.IsNotNull(root.Q(className: "ux-fxg"), "波形グラフ");
            Assert.AreEqual(10, root.Query<Button>(className: "ux-fxp__btn").ToList().Count, "雛形 10 種");
        }

        [Test]
        public void ShakeEditor_ApplyPreset_RewritesAllAtOnce_AndUndoRestores()
        {
            BuildSample();
            var data = Shakes[0]; // Pulse
            OpenWindow(ShakeType, data);
            var before = JsonUtility.ToJson(data);
            var button = _window.rootVisualElement.Query<Button>(className: "ux-fxp__btn").ToList().Find(b => b.text == "Explosion");
            Assert.IsNotNull(button);

            Undo.IncrementCurrentGroup();
            Click(button);
            Assert.AreEqual(ShakePattern.PerlinNoise, data.Pattern);
            Assert.AreEqual(0.6f, data.Envelope.Duration, 1e-4f);
            Assert.AreEqual(new Vector3(0.35f, 0.35f, 0.1f), data.PosAmplitude);
            Assert.AreEqual(1.5f, data.TraumaWeight, 1e-4f);

            Undo.PerformUndo();
            Assert.AreEqual(before, JsonUtility.ToJson(data), "Undo 1 回で元に戻る");
        }

        [Test]
        public void ShakeEditor_PlayAndStop_DrivesRealManager_AndStacks()
        {
            BuildSample();
            OpenWindow(ShakeType, Shakes[1]); // Rumble(MaxStack 2)
            Assert.IsFalse((bool)_windowType.GetProperty("IsPlaying", Inst).GetValue(_window));
            Call("PlayMain", false);
            Assert.IsTrue((bool)_windowType.GetProperty("IsPlaying", Inst).GetValue(_window), "▶ で揺れが始まる");
            Call("PlayStacked");
            var driver = (SceneCameraShakePreviewDriver)Field("_driver");
            Assert.AreEqual(2, driver.ActiveCount, "連打で重なる");
            Call("StopMain");
            driver.Tick(0.2f);
            Assert.AreEqual(0, driver.ActiveCount, "■ で止まる(フェード後)");
            Assert.IsFalse((bool)_windowType.GetProperty("IsPlaying", Inst).GetValue(_window));
        }

        [Test]
        public void ShakeEditor_SwitchToHaptics_OpensHapticsEditorWithHapticsData()
        {
            BuildSample();
            OpenWindow(ShakeType, Shakes[0]);
            Selection.activeObject = Haptics[2];
            HapticsType.GetMethod("SwitchFrom", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { _window });
            var windows = Resources.FindObjectsOfTypeAll(HapticsType);
            Assert.AreEqual(1, windows.Length, "Haptics Editor が開く");
            var target = HapticsType.BaseType.GetField("_target", Inst).GetValue(windows[0]) as HapticsData;
            Assert.AreEqual(Haptics[2], target);
            Assert.AreEqual(Shakes[0], ShakeType.BaseType.GetField("_target", Inst).GetValue(_window), "Shake 側はそのまま残る");
            // 戻る
            Selection.activeObject = Shakes[5];
            ShakeType.GetMethod("SwitchFrom", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { windows[0] });
            Assert.AreEqual(Shakes[5], ShakeType.BaseType.GetField("_target", Inst).GetValue(_window));
        }

        [Test]
        public void ShakeEditor_InvalidValues_ShowInlineWarnings()
        {
            BuildSample();
            var data = Shakes[0];
            data.PosAmplitude = Vector3.zero;
            data.RotAmplitude = Vector3.zero;
            OpenWindow(ShakeType, data);
            var issues = _window.rootVisualElement.Query(className: "ux-issue").ToList();
            Assert.IsTrue(issues.Exists(i => i.Q<Label>(className: "ux-issue__msg").text.Contains("両方ゼロ")), "揺れ幅ゼロの警告が欄の下に出る");
        }

        // ── Haptics Editor ──

        [Test]
        public void HapticsEditor_BuildsHeroCardsAndFooter_AndShowsPadStatus()
        {
            BuildSample();
            OpenWindow(HapticsType, Haptics[3]);
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"));
            Assert.IsNotNull(root.Q(className: "ux-bar"));
            Assert.IsNotNull(root.Q(className: "ux-fxg"));
            Assert.AreEqual(10, root.Query<Button>(className: "ux-fxp__btn").ToList().Count);
            var status = root.Query<Label>(className: "ux-note").ToList().Find(l => l.text.StartsWith("パッド:"));
            Assert.IsNotNull(status, "パッドの状態の表示");
            StringAssert.Contains("接続", status.text); // 「接続なし」または「接続中(名前)」
        }

        [Test]
        public void HapticsEditor_ApplyPreset_AndPlayWithoutPad_DoesNotThrow()
        {
            BuildSample();
            var data = Haptics[0];
            OpenWindow(HapticsType, data);
            var before = JsonUtility.ToJson(data);
            var button = _window.rootVisualElement.Query<Button>(className: "ux-fxp__btn").ToList().Find(b => b.text == "Earthquake");
            Undo.IncrementCurrentGroup();
            Click(button);
            Assert.AreEqual(3f, data.LowFreq.Duration, 1e-4f);
            Assert.AreEqual(HapticPriority.High, data.Priority);
            Undo.PerformUndo();
            Assert.AreEqual(before, JsonUtility.ToJson(data));

            Call("PlayMain", false);
            Call("StopMain");
        }

        // ── 波形グラフ ──

        [Test]
        public void ShakeGraphSource_BuildsTwoSeries_AndWritesDurationOnlyForDurationTime()
        {
            BuildSample();
            var source = new ShakeGraphSource(Shakes[3]);
            var series = source.BuildSeries();
            Assert.AreEqual(2, series.Count, "減衰と揺れの形");
            Assert.AreEqual(CameraFxGraphSource.SampleCount, series[0].Samples.Length);
            Assert.IsTrue(source.Bipolar);
            Assert.IsTrue(source.CanEditDuration);
            Undo.IncrementCurrentGroup();
            source.SetDuration(1.25f);
            Assert.AreEqual(1.25f, Shakes[3].Envelope.Duration, 1e-4f);
            Undo.PerformUndo();
            Assert.AreEqual(0.6f, Shakes[3].Envelope.Duration, 1e-4f);

            Shakes[3].Envelope.Time.Mode = TimeMode.Speed;
            Shakes[3].Envelope.Time.Value = 2f;
            Assert.IsFalse(source.CanEditDuration, "Speed / Rate の尺はドラッグで書かない");
            source.SetDuration(9f);
            Assert.AreEqual(2f, Shakes[3].Envelope.Time.Value);

            Shakes[3].Envelope.Time.Mode = TimeMode.Duration;
            Shakes[3].Envelope.Time.Value = 0f;
            Assert.IsNull(source.BuildSeries(), "尺 0 は描かない");
            StringAssert.Contains("尺が 0", source.EmptyMessage);
        }

        [Test]
        public void HapticsGraphSource_HandlesEditStrength_AndDurationScalesBothMotors()
        {
            BuildSample();
            var data = Haptics[3]; // Explosion: Low 1→0 0.5s / High 1→0 0.2s
            var source = new HapticsGraphSource(data);
            Assert.AreEqual(2, source.HandleCount);
            Assert.IsFalse(source.Bipolar);
            Assert.AreEqual(1f, source.GetHandle(0), 1e-4f);

            Undo.IncrementCurrentGroup();
            source.SetHandle(1, 0.35f);
            Assert.AreEqual(0.35f, data.HighFreq.From, 1e-4f);
            Assert.AreEqual(1f, data.LowFreq.From, 1e-4f, "もう片方は変えない");
            Undo.PerformUndo();
            Assert.AreEqual(1f, data.HighFreq.From, 1e-4f);

            source.SetDuration(1f); // 長い方(Low 0.5s)が 1s → 比率 2 倍
            Assert.AreEqual(1f, data.LowFreq.Duration, 1e-4f);
            Assert.AreEqual(0.4f, data.HighFreq.Duration, 1e-4f);

            data.LowFreq = ValueDef.Constant01(0.6f);
            Assert.IsTrue(source.CanEditHandle(0));
            source.SetHandle(0, 0.8f);
            Assert.AreEqual(0.8f, data.LowFreq.Constant, 1e-4f);

            data.HighFreq.Mode = ValueMode.Curve;
            Assert.IsFalse(source.CanEditHandle(1), "カーブ指定の強さはつまみで書かない");
        }

        [Test]
        public void GraphView_DragDuration_WritesOnce_AndEndDragCollapsesUndo()
        {
            BuildSample();
            var data = Shakes[0]; // Pulse 0.15s
            var t = EditorAssembly.GetType("DDrive.Editor.CameraFx.CameraFxGraphView", true);
            var view = Activator.CreateInstance(t, new object[] { new ShakeGraphSource(data), null });
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            t.GetField("_drag", Inst).SetValue(view, Enum.ToObject(t.GetNestedType("Drag", BindingFlags.NonPublic), 1)); // Duration
            t.GetField("_undoGroup", Inst).SetValue(view, group);
            var apply = t.GetMethod("ApplyDrag", Inst);
            // レイアウトが無い(contentRect が 0)と Plot の幅は 1 なので、結果は軸の最大値に張り付く。値が書かれて Undo で戻ることだけを確かめる。
            apply.Invoke(view, new object[] { new Vector2(1000f, 10f), false });
            Assert.AreNotEqual(0.15f, data.Envelope.Duration);
            t.GetMethod("EndDrag", Inst).Invoke(view, null);
            Undo.PerformUndo();
            Assert.AreEqual(0.15f, data.Envelope.Duration, 1e-4f, "1 回のドラッグは 1 回の Undo で戻る");
        }

        // ── 検索・モード ──

        [Test]
        public void ShakeEditor_SimpleMode_ShowsPresetsAndGraphOnly()
        {
            BuildSample();
            OpenWindow(ShakeType, Shakes[0]);
            EditorPrefs.SetInt("DDrive.Ux.Shake.Mode", 0);
            _windowType.BaseType.GetField("_mode", Inst).SetValue(_window, 0);
            Call("RebuildBody");
            var root = _window.rootVisualElement;
            var shown = root.Query<VisualElement>(className: "ux-row").ToList().FindAll(r => r.IsShownForTest());
            // 雛形と波形(必須の段に置いてバッジを隠した 2 行)だけが見える。
            Assert.AreEqual(2, shown.Count, "かんたんは雛形と波形グラフの 2 行");
            EditorPrefs.DeleteKey("DDrive.Ux.Shake.Mode");
        }

        private static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }
    }
}
