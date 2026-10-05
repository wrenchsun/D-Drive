using System;
using System.Reflection;
using DDrive.Editor.CanvasTool;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // 2026-10-06(docs/43 16-2 / 16-24): Canvas Editor の「埋め込み Canvas」欄が Undo / Redo のあと描き直されなかった不具合の回帰テスト。
    // ウィンドウを画面に出さず CreateInstance + CreateGUI だけで検証する(rootVisualElement が使えない環境では Inconclusive)。
    // 編集対象はメモリ上の CanvasData(アセットを作らない)。
    public class CanvasEditorWindowUndoTests
    {
        private CanvasEditorWindow _window;
        private CanvasData _parent;
        private CanvasData _child;

        [SetUp]
        public void SetUp()
        {
            _parent = ScriptableObject.CreateInstance<CanvasData>();
            _parent.Id = 9001;
            _parent.name = "UndoParent";
            _child = ScriptableObject.CreateInstance<CanvasData>();
            _child.Id = 9002;
            _child.name = "UndoChild";
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                // 画面に出していないので Close() は使えない(DestroyImmediate で OnDisable が走り、プレビューの後始末もされる)
                UnityEngine.Object.DestroyImmediate(_window);
            }

            Undo.ClearUndo(_parent);
            UnityEngine.Object.DestroyImmediate(_parent);
            UnityEngine.Object.DestroyImmediate(_child);
        }

        private void OpenWindowOrInconclusive()
        {
            try
            {
                _window = ScriptableObject.CreateInstance<CanvasEditorWindow>();
                _window.CreateGUI();
                var setTarget = typeof(CanvasEditorWindow).GetMethod("SetTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                setTarget.Invoke(_window, new object[] { _parent });
            }
            catch (Exception e)
            {
                Assert.Inconclusive("ウィンドウを画面なしで構築できない環境: " + e.GetType().Name);
            }
        }

        private int CountButtons(string text)
        {
            var count = 0;
            _window.rootVisualElement.Query<Button>().ForEach(b =>
            {
                if (b.text == text)
                {
                    count++;
                }
            });
            return count;
        }

        [Test]
        public void UndoRedo_RedrawsEmbeddedRows()
        {
            OpenWindowOrInconclusive();
            Assert.AreEqual(0, CountButtons("削除"));

            Undo.IncrementCurrentGroup();
            Assert.IsTrue(CanvasEmbeddedEditing.Register(_parent, "OptionRoot", _child));
            Undo.FlushUndoRecordObjects();
            // 登録はウィンドウの外(データ直接)で行ったので、ウィンドウは自分で描き直すまで古いまま。ここで 1 度描き直して「登録済み 1 行」を確認する。
            _window.RefreshAfterUndoRedo();
            Assert.AreEqual(1, CountButtons("削除"), "登録したら行が出る");

            Undo.PerformUndo();
            Assert.AreEqual(0, _parent.EmbeddedCanvases?.Length ?? 0, "データは Undo で戻る");
            // Undo.PerformUndo は undoRedoPerformed を発火する(ウィンドウは購読している)。表示も戻ること。
            Assert.AreEqual(0, CountButtons("削除"), "Undo のあと、登録済みの行が表示に残らない");

            Undo.PerformRedo();
            Assert.AreEqual(1, _parent.EmbeddedCanvases.Length);
            Assert.AreEqual(1, CountButtons("削除"), "Redo のあと、行が現れる");
        }

        [Test]
        public void UndoRedo_RedrawsRootPathField()
        {
            OpenWindowOrInconclusive();
            CanvasEmbeddedEditing.AddEmpty(_parent);
            _window.RefreshAfterUndoRedo();

            Undo.IncrementCurrentGroup();
            Undo.RecordObject(_parent, "Canvas: 埋め込み Canvas の RootPath");
            _parent.EmbeddedCanvases[0].RootPath = "Typed";
            EditorUtility.SetDirty(_parent);
            Undo.FlushUndoRecordObjects();
            _window.RefreshAfterUndoRedo();
            Assert.AreEqual("Typed", FirstRootPathValue());

            Undo.PerformUndo();
            Assert.AreEqual(string.Empty, _parent.EmbeddedCanvases[0].RootPath ?? string.Empty);
            Assert.AreEqual(string.Empty, FirstRootPathValue(), "Undo のあと RootPath 欄が古い値のまま残らない");
        }

        [Test]
        public void LockToggle_GraysOutFollowSelection_AndKeepsItsValue()
        {
            OpenWindowOrInconclusive();
            var root = _window.rootVisualElement;
            var follow = root.Query<Toggle>().Where(t => t.label == "選択に追従").First();
            var lockToggle = root.Query<UnityEditor.UIElements.ToolbarToggle>().Where(t => t.text == "🔒").First();
            Assert.IsNotNull(follow);
            Assert.IsNotNull(lockToggle);
            var before = follow.value;
            Assert.IsTrue(follow.enabledSelf);

            SetLock(true);
            Assert.IsFalse(follow.enabledSelf, "🔒 が ON の間は「選択に追従」が灰色");
            Assert.AreEqual(before, follow.value, "チェックの値は保持される");
            StringAssert.Contains("追従しません", root.Query<Label>().Where(l => l.text.Contains("追従しません")).First().text);

            SetLock(false);
            Assert.IsTrue(follow.enabledSelf);
            Assert.AreEqual(before, follow.value);
        }

        // 画面に出していないウィンドウでは値変更のコールバックが届かないので、コールバックの中身(状態 + 表示更新)を直接実行する。
        // (実際のウィンドウでトグルを操作した結果は MCP の execute_code で確認する)
        private void SetLock(bool on)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(CanvasEditorWindow).GetField("_lockTarget", flags).SetValue(_window, on);
            typeof(CanvasEditorWindow).GetMethod("UpdateFollowLockUi", flags).Invoke(_window, null);
        }

        private string FirstRootPathValue()
        {
            string value = null;
            _window.rootVisualElement.Query<TextField>().ForEach(f =>
            {
                if (value == null && f.isDelayed && f.tooltip.StartsWith("親 Prefab ルートからの相対パス", StringComparison.Ordinal))
                {
                    value = f.value;
                }
            });
            return value;
        }
    }
}
