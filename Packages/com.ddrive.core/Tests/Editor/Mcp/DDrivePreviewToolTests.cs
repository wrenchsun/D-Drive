using System;
using System.Collections.Generic;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Editor.Preview;
using DDrive.Editor.Settings;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.4 MCP-7(2026-10-07) — ddrive_preview。実際に音を鳴らす・確認用シーンを開き直す操作は
    // ユーザーの Editor を乱すので、ここでは形・拒否・掃除・未保存ブロックだけを確認する(実機は HTTP で確認)。
    public class DDrivePreviewToolTests
    {
        private bool _originalAllowWrite;

        [SetUp]
        public void SetUp()
        {
            _originalAllowWrite = DDriveProjectSettings.instance.McpAllowWrite;
            DDriveProjectSettings.instance.SetMcpAllowWrite(true, save: false);
        }

        [TearDown]
        public void TearDown()
        {
            DDrivePreviewTools.ResetForTests();
            DDriveProjectSettings.instance.SetMcpAllowWrite(_originalAllowWrite, save: false);
        }

        private static string Code(JObject r) => (string)r["error"]?["code"];

        [Test]
        public void ParseAction_AcceptsSixActions_RejectsOthers()
        {
            foreach (var a in DDrivePreviewTools.Actions)
            {
                Assert.AreEqual(a, DDrivePreviewTools.ParseAction(a.ToUpperInvariant()));
            }

            Assert.Throws<McpToolError>(() => DDrivePreviewTools.ParseAction("preview"));
            Assert.Throws<McpToolError>(() => DDrivePreviewTools.ParseAction(null));
        }

        [Test]
        public void ParseScene_DefaultsToCommon_RejectsUnknown()
        {
            Assert.AreEqual("common", DDrivePreviewTools.ParseScene(null));
            Assert.AreEqual("canvas", DDrivePreviewTools.ParseScene("Canvas"));
            Assert.Throws<McpToolError>(() => DDrivePreviewTools.ParseScene("net"));
        }

        [Test]
        public void Preview_InvalidAction_IsInvalidParams()
        {
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDrivePreviewTools.Preview("dance")));
        }

        [Test]
        public void Status_HasShape_AndWorksWhenWriteDisabled()
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(false, save: false);
            var r = DDrivePreviewTools.Preview("status");
            Assert.IsNull(r["error"], r.ToString());
            Assert.IsNotNull(r["scene"]);
            Assert.AreEqual(JTokenType.Boolean, r["previewScene"].Type);
            Assert.IsInstanceOf<JArray>(r["playing"]);
            Assert.AreEqual(0, ((JArray)r["playing"]).Count);
            Assert.AreEqual(JTokenType.Boolean, r["playMode"].Type);
        }

        [TestCase("open")]
        [TestCase("play")]
        [TestCase("stop")]
        [TestCase("stop_all")]
        [TestCase("sweep")]
        public void WriteActions_AreRejectedWhenWriteDisabled(string action)
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(false, save: false);
            Assert.AreEqual(McpGuard.CodeWriteDisabled, Code(DDrivePreviewTools.Preview(action)));
        }

        [Test]
        public void Play_UnsupportedType_IsInvalidParams()
        {
            var r = DDrivePreviewTools.Preview("play", type: "Canvas", id: "1");
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(r), r.ToString());
            StringAssert.Contains("Se", (string)r["error"]["msg"]);
        }

        [Test]
        public void Stop_RequiresKnownHandle()
        {
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDrivePreviewTools.Preview("stop")));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDrivePreviewTools.Preview("stop", handle: "999")));
        }

        [Test]
        public void StopAll_WhenNothingPlaying_ReportsZero()
        {
            var r = DDrivePreviewTools.Preview("stop_all");
            Assert.AreEqual(0, (int)r["stopped"], r.ToString());
        }

        // Clip 未設定の SE は鳴らない = ok:false(登録簿にも載らない)。音は出ない。
        [Test]
        public void PlayAsset_SeWithoutClip_IsNotRegistered()
        {
            var se = ScriptableObject.CreateInstance<SeData>();
            try
            {
                var r = DDrivePreviewTools.PlayAsset(AssetType.Se, se);
                Assert.IsNull(r["error"], r.ToString());
                Assert.IsFalse((bool)r["ok"], r.ToString());
                Assert.AreEqual(0, ((JArray)DDrivePreviewTools.Preview("status")["playing"]).Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(se);
            }
        }

        // sweep は EditorPreviewSweeper.DestroyOrphans のラッパー: 件数を返し、シーンに属するプレビューや本物のオブジェクトは消さない。
        // (孤児そのものは DontSave の実体をシーン外に作れないため作らない。孤児の判定は EditorPreviewSweeperTests が見ている)
        [Test]
        public void Sweep_ReturnsCount_AndKeepsPreviewRootsInAScene()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var inScene = new GameObject(EditorPreviewSweeper.Prefix + " McpSweepTest") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(inScene, scene);
            try
            {
                var r = DDrivePreviewTools.Preview("sweep");
                Assert.IsNull(r["error"], r.ToString());
                Assert.GreaterOrEqual((int)r["destroyed"], 0);
                Assert.IsTrue(inScene != null, "シーンに属するプレビューは孤児ではない");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // 未保存のシーンがあるとき、open は保存ダイアログを出さず blocked を返し、何も開かない。
        // (無題シーンが未保存の環境では追加ロードのシーンを作れないため、未保存の一覧は差し替えで与える)
        [Test]
        public void Open_WithUnsavedScene_IsBlocked()
        {
            var active = SceneManager.GetActiveScene().path;
            string kind = null;
            foreach (var candidate in DDrivePreviewTools.SceneKinds)
            {
                if (DDrivePreviewTools.ScenePathOf(candidate) != active)
                {
                    kind = candidate;
                    break;
                }
            }

            DDrivePreviewTools.DirtyScenesOverride = () => new List<string> { "Assets/Unsaved.unity" };
            try
            {
                var r = DDrivePreviewTools.Preview("open", scene: kind);
                Assert.AreEqual(DDrivePreviewTools.BlockedUnsaved, (string)r["blocked"], r.ToString());
                Assert.AreEqual("Assets/Unsaved.unity", (string)r["scenes"][0]);
                Assert.AreEqual(active, SceneManager.GetActiveScene().path, "シーンは切り替わらない");
            }
            finally
            {
                DDrivePreviewTools.DirtyScenesOverride = null;
            }
        }

        // 実際の dirty 判定: 保存済みシーンが無い・変更が無ければ空(今の状態に依らず形だけ確認)。
        [Test]
        public void DirtyScenes_IsAListOfPaths()
        {
            var list = DDrivePreviewTools.DirtyScenes();
            Assert.IsNotNull(list);
            foreach (var path in list)
            {
                Assert.IsFalse(string.IsNullOrEmpty(path));
            }
        }

        [Test]
        public void CanOpenWithoutDialog_TrueWhenTargetAlreadyActive()
        {
            var active = SceneManager.GetActiveScene().path;
            Assert.IsTrue(DDrivePreviewTools.CanOpenWithoutDialog(active, out var dirty));
            Assert.AreEqual(0, dirty.Count);
        }

        [Test]
        public void ToolDefinition_IsWithinBudget()
        {
            var attr = (McpToolAttribute)Attribute.GetCustomAttribute(
                typeof(DDrivePreviewTools).GetMethod("Preview"), typeof(McpToolAttribute));
            Assert.AreEqual("ddrive_preview", attr.Name);
            Assert.AreEqual("authoring", attr.Group);
            Assert.LessOrEqual(attr.Description.Length, 80);
        }
    }
}
