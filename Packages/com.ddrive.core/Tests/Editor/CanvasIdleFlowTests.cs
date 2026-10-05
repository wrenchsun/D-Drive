using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Editor.CanvasTool;
using DDrive.Foundation.Loader;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using Image = UnityEngine.UI.Image;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // 2026-10-06(U-29b): Canvas Editor の「Idle を流す」(プレハブモードで Idle を流し続ける)の部品 CanvasIdleFlow の検証。
    // 流している間の値を必ず元へ戻すこと(止める・保存の直前・プレハブモードを閉じる・対象の切り替え・ウィンドウを閉じる = どれも Stop())、
    // 選択した要素だけ止めて取り直すこと、何を流すか(埋め込みの子を含む、親の行が勝つ)を、プレハブステージを開かずに確かめる。
    // 実際のプレハブモード(ステージ内の要素・保存・閉じる)は MCP の execute_code で実ウィンドウを使って確認する(docs/43 16-26 以降)。
    public class CanvasIdleFlowTests
    {
        private sealed class NoLoader : IAssetLoader
        {
            public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
                => UniTask.FromResult<T>(null);

            public void Release(string address)
            {
            }

            public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress) => UniTask.CompletedTask;
        }

        private GameObject _root;
        private RectTransform _panel;
        private RectTransform _button;
        private UiTweenManager _tweens;
        private CanvasIdleFlow _flow;
        private CanvasData _data;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("IdleFlowRoot", typeof(RectTransform));
            ((RectTransform)_root.transform).sizeDelta = new Vector2(800f, 600f);
            _panel = NewChild("Panel", _root.transform);
            _panel.anchoredPosition = new Vector2(30f, -20f);
            _panel.localScale = new Vector3(1.5f, 1.5f, 1f);
            _button = NewChild("Btn", _panel);
            _button.anchoredPosition = new Vector2(5f, 6f);
            _tweens = new UiTweenManager(new AssetRegistry(new NoLoader()));
            _flow = new CanvasIdleFlow(_tweens, _ => null);
            _data = ScriptableObject.CreateInstance<CanvasData>();
            _data.Id = 9201;
        }

        [TearDown]
        public void TearDown()
        {
            _flow.Stop();
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(_data);
        }

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(100f, 40f);
            return rt;
        }

        private static CanvasIdleFlow.Entry Idle(string path, UiPreset preset)
            => new CanvasIdleFlow.Entry(path, new UiPresetRef { Preset = preset }, default);

        private List<CanvasIdleFlow.Entry> FloatAndBreathe() => new()
        {
            Idle("Panel", UiPreset.Float),
            Idle("Panel/Btn", UiPreset.Breathe),
        };

        // ── 流す / 止めたら元の値へ戻る ──

        [Test]
        public void Start_MovesTheElements_Stop_RestoresEveryValue()
        {
            var panelColor = _panel.GetComponent<Image>().color;
            Assert.AreEqual(2, _flow.Start(_root.transform, FloatAndBreathe()));
            Assert.IsTrue(_flow.IsActive);
            Assert.IsTrue(_flow.HasRunning);

            _tweens.Tick(0.4f);
            Assert.AreNotEqual(new Vector2(30f, -20f), _panel.anchoredPosition, "Float で動く");
            Assert.AreNotEqual(Vector3.one, _button.localScale, "Breathe で拡縮する");

            _flow.Stop();

            Assert.AreEqual(new Vector2(30f, -20f), _panel.anchoredPosition);
            Assert.AreEqual(new Vector3(1.5f, 1.5f, 1f), _panel.localScale);
            Assert.AreEqual(new Vector2(5f, 6f), _button.anchoredPosition);
            Assert.AreEqual(Vector3.one, _button.localScale);
            Assert.AreEqual(panelColor, _panel.GetComponent<Image>().color);
            Assert.IsFalse(_flow.IsActive);
            Assert.AreEqual(0, _flow.Count);
        }

        [Test]
        public void Stop_AtAnyMomentOfTheLoop_AlwaysRestoresTheOriginalValues()
        {
            // 保存の直前(prefabSaving)・プレハブモードを閉じる・ウィンドウを閉じる = どれも Stop()。流している途中のどの時点でも元の値に戻る。
            foreach (var elapsed in new[] { 0.05f, 0.3f, 0.8f, 1.6f, 2.5f, 7.3f })
            {
                _flow.Start(_root.transform, FloatAndBreathe());
                _tweens.Tick(elapsed);
                _flow.Stop();
                Assert.AreEqual(new Vector2(30f, -20f), _panel.anchoredPosition, $"t={elapsed}");
                Assert.AreEqual(Vector3.one, _button.localScale, $"t={elapsed}");
            }
        }

        [Test]
        public void Stop_WhenNotRunning_OrCalledTwice_IsHarmless()
        {
            Assert.DoesNotThrow(() => _flow.Stop());
            _flow.Start(_root.transform, FloatAndBreathe());
            _flow.Stop();
            Assert.DoesNotThrow(() => _flow.Stop());
        }

        [Test]
        public void Stop_DestroyedTarget_DoesNotThrow()
        {
            _flow.Start(_root.transform, FloatAndBreathe());
            UnityEngine.Object.DestroyImmediate(_panel.gameObject);
            Assert.IsTrue(_flow.HasDestroyedTargets);
            Assert.DoesNotThrow(() => _flow.Stop());
        }

        [Test]
        public void Start_UnknownPath_IsSkipped_AndNoPresetEntryDoesNothing()
        {
            var entries = new List<CanvasIdleFlow.Entry> { Idle("Nowhere", UiPreset.Float), Idle("Panel", UiPreset.None), Idle("Panel", UiPreset.Float) };
            Assert.AreEqual(1, _flow.Start(_root.transform, entries), "見つかる要素 + 流せる指定だけ流す");
        }

        [Test]
        public void Start_RootPathEmpty_IsTheStageRoot()
        {
            Assert.AreEqual(1, _flow.Start(_root.transform, new List<CanvasIdleFlow.Entry> { Idle(string.Empty, UiPreset.Breathe) }));
            _tweens.Tick(0.3f);
            Assert.AreNotEqual(Vector3.one, _root.transform.localScale);
            _flow.Stop();
            Assert.AreEqual(Vector3.one, _root.transform.localScale);
        }

        // ── 編集とぶつからない: 選択した要素(と祖先)だけ止めて、外れたら取り直して再開 ──

        [Test]
        public void SuspendFor_SelectedElement_StopsOnlyThatOne_AndRestoresItsValue()
        {
            _flow.Start(_root.transform, FloatAndBreathe());
            _tweens.Tick(0.4f);
            Assert.AreNotEqual(Vector3.one, _button.localScale);

            // Btn だけ選んだ: Btn とその祖先(Panel)は止まって元の値。
            _flow.SuspendFor(new Transform[] { _button });
            Assert.AreEqual(2, _flow.SuspendedCount);
            Assert.AreEqual(new Vector2(30f, -20f), _panel.anchoredPosition);
            Assert.AreEqual(Vector3.one, _button.localScale);
            Assert.IsFalse(_flow.HasRunning);

            // 選択を外す: 再開
            _flow.SuspendFor(null);
            Assert.AreEqual(0, _flow.SuspendedCount);
            _tweens.Tick(0.4f);
            Assert.AreNotEqual(new Vector2(30f, -20f), _panel.anchoredPosition);
            _flow.Stop();
        }

        [Test]
        public void SuspendFor_ParentSelected_DoesNotStopTheChild()
        {
            _flow.Start(_root.transform, FloatAndBreathe());
            _tweens.Tick(0.4f);

            _flow.SuspendFor(new Transform[] { _panel });

            Assert.AreEqual(1, _flow.SuspendedCount, "選択した Panel だけ止まる(子の Btn は流れ続ける)");
            Assert.AreEqual(new Vector2(30f, -20f), _panel.anchoredPosition);
            Assert.IsTrue(_flow.HasRunning);
            _flow.Stop();
        }

        [Test]
        public void SuspendFor_UserEditWhileSuspended_BecomesTheNewBaseline()
        {
            _flow.Start(_root.transform, FloatAndBreathe());
            _tweens.Tick(0.4f);
            _flow.SuspendFor(new Transform[] { _panel });

            _panel.anchoredPosition = new Vector2(99f, 77f); // ユーザーが動かした

            _flow.SuspendFor(null); // 選択を外す → 取り直して再開
            _tweens.Tick(0.4f);
            _flow.Stop();

            Assert.AreEqual(new Vector2(99f, 77f), _panel.anchoredPosition, "ユーザーの編集後の値へ戻る(編集が失われない / Idle の値が混ざらない)");
        }

        // ── 何を流すか ──

        private static ElementFx Fx(string path, UiPreset idle = UiPreset.None)
            => new ElementFx { ElementPath = path, IdlePreset = new UiPresetRef { Preset = idle } };

        private static CanvasData NewData(ulong id, string name)
        {
            var d = ScriptableObject.CreateInstance<CanvasData>();
            d.Id = id;
            d.name = name;
            return d;
        }

        [Test]
        public void CollectEntries_OnlyRowsWithIdle_AndChildRowsAreCombinedWithTheEmbedRoot()
        {
            var child = NewData(9202, "Child");
            try
            {
                child.ElementEffects = new[] { Fx("Deep", UiPreset.Float), Fx("NoIdle") };
                _data.ElementEffects = new[] { Fx("Panel", UiPreset.Pulse), Fx("Plain") };
                CanvasEmbeddedEditing.Register(_data, "Inner", child);
                var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { _data, child });

                var entries = new List<CanvasIdleFlow.Entry>();
                CanvasIdleFlow.CollectEntries(_data, lookup, entries);

                CollectionAssert.AreEquivalent(new[] { "Panel", "Inner/Deep" }, Paths(entries));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(child);
            }
        }

        [Test]
        public void CollectEntries_ParentRowOverridesChild_EvenWhenTheParentRowIsEmpty()
        {
            var child = NewData(9203, "Child");
            try
            {
                child.ElementEffects = new[] { Fx("A", UiPreset.Float), Fx("B", UiPreset.Float) };
                _data.ElementEffects = new[] { Fx("Inner/A") }; // 空の親の行(= 規則 A: 子の設定を黙って打ち消す)
                CanvasEmbeddedEditing.Register(_data, "Inner", child);
                var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { _data, child });

                var entries = new List<CanvasIdleFlow.Entry>();
                CanvasIdleFlow.CollectEntries(_data, lookup, entries);

                CollectionAssert.AreEqual(new[] { "Inner/B" }, Paths(entries), "実行時と同じ: 親の行が勝つので Inner/A は流れない");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(child);
            }
        }

        [Test]
        public void CollectEntries_NestedEmbeds_UseChainedRoots_AndCyclesAreIgnored()
        {
            var child = NewData(9204, "Child");
            var grand = NewData(9205, "Grand");
            try
            {
                grand.ElementEffects = new[] { Fx("G", UiPreset.Sway) };
                child.ElementEffects = new[] { Fx("C", UiPreset.Float) };
                CanvasEmbeddedEditing.Register(_data, "Inner", child);
                CanvasEmbeddedEditing.Register(child, "Sub", grand);
                CanvasEmbeddedEditing.Register(grand, "Loop", _data); // 循環
                var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { _data, child, grand });

                var entries = new List<CanvasIdleFlow.Entry>();
                CanvasIdleFlow.CollectEntries(_data, lookup, entries);

                CollectionAssert.AreEquivalent(new[] { "Inner/C", "Inner/Sub/G" }, Paths(entries));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(child);
                UnityEngine.Object.DestroyImmediate(grand);
            }
        }

        [Test]
        public void Signature_ChangesWithContent_AndStage()
        {
            var a = new List<CanvasIdleFlow.Entry> { Idle("Panel", UiPreset.Float) };
            var same = new List<CanvasIdleFlow.Entry> { Idle("Panel", UiPreset.Float) };
            var changed = new List<CanvasIdleFlow.Entry> { Idle("Panel", UiPreset.Pulse) };
            Assert.AreEqual(CanvasIdleFlow.Signature(1, a), CanvasIdleFlow.Signature(1, same));
            Assert.AreNotEqual(CanvasIdleFlow.Signature(1, a), CanvasIdleFlow.Signature(1, changed));
            Assert.AreNotEqual(CanvasIdleFlow.Signature(1, a), CanvasIdleFlow.Signature(2, a));
        }

        private static List<string> Paths(List<CanvasIdleFlow.Entry> entries)
        {
            var list = new List<string>();
            foreach (var e in entries)
            {
                list.Add(e.Path);
            }

            return list;
        }

        // ── ウィンドウ(画面に出さずに確かめられる範囲) ──

        [Test]
        public void Window_SetIdleFlow_WithoutPrefabStage_DoesNothingAndCanBeToggledOff()
        {
            CanvasEditorWindow window = null;
            try
            {
                try
                {
                    window = ScriptableObject.CreateInstance<CanvasEditorWindow>();
                    window.CreateGUI();
                }
                catch (Exception e)
                {
                    Assert.Inconclusive("ウィンドウを画面なしで構築できない環境: " + e.GetType().Name);
                }

                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var flowField = typeof(CanvasEditorWindow).GetField("_idleFlow", flags);

                window.SetIdleFlow(true);
                var flow = (CanvasIdleFlow)flowField.GetValue(window);
                Assert.IsNotNull(flow);
                Assert.IsFalse(flow.IsActive, "プレハブモードでなければ何も流さない");
                var toggle = window.rootVisualElement.Query<UnityEngine.UIElements.Toggle>().Where(t => t.label.StartsWith("Idle を流す", StringComparison.Ordinal)).First();
                Assert.IsNotNull(toggle, "トグルがある");
                Assert.IsTrue(toggle.value, "SetIdleFlow(true) でトグルの表示も追従する");

                window.SetIdleFlow(false);
                Assert.IsFalse(flow.IsActive);
                Assert.IsFalse(toggle.value);
            }
            finally
            {
                if (window != null)
                {
                    UnityEngine.Object.DestroyImmediate(window);
                }
            }
        }
    }
}
