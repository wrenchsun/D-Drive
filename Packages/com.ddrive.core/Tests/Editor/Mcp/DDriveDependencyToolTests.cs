using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Editor.Settings;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.2 MCP-4(2026-10-07) — ddrive_asset_usages / unused / delete / ddrive_editor_open。
    // 参照の整形は合成データ(純粋関数)で、削除は一時 GameData ルート(Assets/Tests/DDriveTemp/...)の中だけで検証する。
    // 実 Assets/GameData の依存グラフは再構築しない(DependencyGraphService は一時パスだけ UpdatePaths で索引に載せる)。
    public class DDriveDependencyToolTests
    {
        private const string TestRoot = "Assets/Tests/DDriveTemp/McpDependencyToolsGameData";

        private bool _originalAllowWrite;
        private readonly List<string> _trackedPaths = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _originalAllowWrite = DDriveProjectSettings.instance.McpAllowWrite;
            DDriveProjectSettings.instance.SetMcpAllowWrite(true, save: false);
            DependencyGraphPostprocessor.Suppress = true;
            DependencyGraphService.ResetInMemoryCacheForTests();

            // 既定でモーダルを絶対に出さない(出るとテストが無限にハングする。SafeDeleteServiceTests と同じ方針)。
            SafeDeleteService.ConfirmDialogOverride = (_, _, _, _) => true;
            SafeDeleteService.InfoDialogOverride = (_, _, _) => { };
        }

        [TearDown]
        public void TearDown()
        {
            DDriveDependencyTools.GraphBuiltOverride = null;
            SafeDeleteService.ConfirmDialogOverride = null;
            SafeDeleteService.InfoDialogOverride = null;
            DDriveProjectSettings.instance.SetMcpAllowWrite(_originalAllowWrite, save: false);

            if (_trackedPaths.Count > 0)
            {
                DependencyGraphService.UpdatePaths(null, _trackedPaths);
                _trackedPaths.Clear();
            }

            DependencyGraphService.ResetInMemoryCacheForTests();
            DependencyGraphPostprocessor.Suppress = false;

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope())
                {
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private static string Code(JObject r) => (string)r["error"]?["code"];

        // 一時ルートに Se を作り、依存グラフの索引に載せる(Delete が「構築済み」と見なすため)。
        private (JObject created, string path) CreateIndexedSe(string name)
        {
            var created = DDriveAssetTools.CreateIn(TestRoot, "Se", name, "Test", null, null, null, false);
            Assert.IsNull(created["error"], created.ToString());
            var path = (string)created["path"];
            DependencyGraphService.UpdatePaths(new[] { path }, null);
            _trackedPaths.Add(path);
            return (created, path);
        }

        private static DependencyReference Ref(string source, string objectPath, string component)
            => new DependencyReference(source, objectPath, component, "Field", AssetType.Se, 1);

        // ── usages(純粋関数) ──

        [Test]
        public void KindOf_ClassifiesByExtension_AndNamesDataType()
        {
            Assert.AreEqual("scene", DDriveDependencyTools.KindOf(Ref("Assets/A.unity", "", "X")));
            Assert.AreEqual("prefab", DDriveDependencyTools.KindOf(Ref("Assets/A.prefab", "Root/Child", "X")));
            Assert.AreEqual("playable", DDriveDependencyTools.KindOf(Ref("Assets/A.playable", "", "X")));
            Assert.AreEqual("PresentationData", DDriveDependencyTools.KindOf(Ref("Assets/A.asset", "", "PresentationData")));
            Assert.AreEqual("data", DDriveDependencyTools.KindOf(Ref("Assets/A.asset", "", "")));
            Assert.IsNull(DDriveDependencyTools.KindOf(Ref("Assets/A.txt", "", "X")));
        }

        [Test]
        public void UsagesJson_ShapeAndPaging()
        {
            var list = new List<DependencyReference>();
            for (var i = 0; i < 5; i++)
            {
                list.Add(Ref($"Assets/Src{i}.prefab", i == 0 ? "Root/Child" : "", "AudioSource"));
            }

            var page1 = DDriveDependencyTools.UsagesJson(list, null, 2, 100000);
            Assert.AreEqual(5, (int)page1["count"]);
            Assert.IsNull(page1["items"], "items ではなく usages");
            var usages = (JArray)page1["usages"];
            Assert.AreEqual(2, usages.Count);
            Assert.AreEqual("Assets/Src0.prefab", (string)usages[0]["path"]);
            Assert.AreEqual("Root/Child", (string)usages[0]["objectPath"]);
            Assert.AreEqual("prefab", (string)usages[0]["kind"]);
            Assert.IsNull(usages[1]["objectPath"], "空の objectPath は出さない");
            Assert.AreEqual("2", (string)page1["next"]);

            var page3 = DDriveDependencyTools.UsagesJson(list, "4", 2, 100000);
            Assert.AreEqual(1, ((JArray)page3["usages"]).Count);
            Assert.IsNull(page3["next"]);

            var truncated = DDriveDependencyTools.UsagesJson(list, null, 50, 120);
            Assert.IsTrue((bool)truncated["truncated"]);
            Assert.Less(((JArray)truncated["usages"]).Count, 5);
            Assert.IsNotNull(truncated["next"]);

            Assert.Throws<McpToolError>(() => DDriveDependencyTools.UsagesJson(list, "abc", 2, 4000));
        }

        [Test]
        public void UnusedJson_ShapeAndArchivedMark()
        {
            var rows = new List<UnusedAssetId>
            {
                new UnusedAssetId(AssetType.Se, 1234567890123UL, "Assets/A.asset", "A"),
                new UnusedAssetId(AssetType.Bgm, 2UL, "Assets/B.asset", "B"),
            };

            var r = DDriveDependencyTools.UnusedJson(rows, null, 0, 4000, path => path == "Assets/B.asset");
            Assert.AreEqual(2, (int)r["count"]);
            var items = (JArray)r["items"];
            Assert.AreEqual("Se", (string)items[0]["type"]);
            Assert.AreEqual(JTokenType.String, items[0]["id"].Type);
            Assert.AreEqual("1234567890123", (string)items[0]["id"]);
            Assert.AreEqual("A", (string)items[0]["name"]);
            Assert.IsNull(items[0]["archived"], "false は出さない");
            Assert.IsTrue((bool)items[1]["archived"]);
        }

        // ── needsRebuild ──

        [Test]
        public void NeedsRebuild_Shape()
        {
            var r = DDriveDependencyTools.NeedsRebuild();
            Assert.IsTrue((bool)r["needsRebuild"]);
            Assert.AreEqual("ddrive_generate target=deps", (string)r["hint"]);
        }

        [Test]
        public void ReadTools_ReturnNeedsRebuild_WhenGraphNotBuilt()
        {
            var (created, _) = CreateIndexedSe("McpNeedsRebuild");
            DDriveDependencyTools.GraphBuiltOverride = () => false;

            var usages = DDriveDependencyTools.Usages("Se", (string)created["id"]);
            Assert.IsNull(usages["error"], usages.ToString());
            Assert.IsTrue((bool)usages["needsRebuild"]);
            Assert.IsNull(usages["usages"]);

            var unused = DDriveDependencyTools.Unused("Se");
            Assert.IsTrue((bool)unused["needsRebuild"]);

            var del = DDriveDependencyTools.Delete("Se", (string)created["id"], preview: true);
            Assert.IsTrue((bool)del["needsRebuild"], "グラフ未構築なら削除も進まない");
            Assert.IsTrue(File.Exists((string)created["path"]));
        }

        [Test]
        public void Usages_OfIndexedAsset_IsEmpty_AndBadArgsAreRejected()
        {
            var (created, _) = CreateIndexedSe("McpUsagesEmpty");
            var r = DDriveDependencyTools.Usages("Se", (string)created["id"]);
            Assert.IsNull(r["error"], r.ToString());
            Assert.AreEqual(0, (int)r["count"]);
            Assert.AreEqual(0, ((JArray)r["usages"]).Count);

            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveDependencyTools.Usages("Nope", "1")));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveDependencyTools.Usages("Se", "zzz")));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveDependencyTools.Usages("Se", "7")));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveDependencyTools.Unused("Nope")));
        }

        [Test]
        public void Unused_IncludesIndexedAssetWithNoReferences()
        {
            var (created, _) = CreateIndexedSe("McpUnusedOne");
            var r = DDriveDependencyTools.Unused("Se", limit: 200, max_chars: 200000);
            Assert.IsNull(r["error"], r.ToString());
            var ours = ((JArray)r["items"]).FirstOrDefault(i => (string)i["id"] == (string)created["id"]);
            Assert.IsNotNull(ours, "参照の無い一時 Se は未使用に出る");
            Assert.AreEqual("McpUnusedOne", (string)ours["name"]);
            Assert.AreEqual("Se", (string)ours["type"]);
        }

        // ── delete ──

        [Test]
        public void Delete_Preview_ReturnsWouldDelete_AndKeepsFile()
        {
            var (created, path) = CreateIndexedSe("McpDelPreview");
            var r = DDriveDependencyTools.Delete("Se", (string)created["id"], preview: true, scan_code: true);
            Assert.IsNull(r["error"], r.ToString());
            Assert.AreEqual(path, (string)r["wouldDelete"]);
            Assert.AreEqual(path, (string)r["path"]);
            Assert.IsNull(r["deleted"], "preview は deleted を返さない");
            Assert.IsNull(r["blocked"]);
            Assert.IsNull(r["blockers"]);
            Assert.IsTrue(File.Exists(path), "preview は消さない");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SeData>(path));
            Assert.IsFalse(ArchiveTagService.IsArchived(AssetDatabase.LoadAssetAtPath<SeData>(path)), "preview は Archived も付けない");
        }

        [Test]
        public void Delete_Real_RemovesFile_AndAddressablesEntry()
        {
            var (created, path) = CreateIndexedSe("McpDelReal");
            var asset = AssetDatabase.LoadAssetAtPath<SeData>(path);
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (AddressablesSync.IsAvailable)
            {
                Assert.IsTrue(AddressablesSync.IsRegistered(asset), "前提: 作成時に Addressables 登録済み");
            }

            var confirmCalls = 0;
            System.Func<string, string, string, string, bool> mine = (_, _, _, _) => { confirmCalls++; return true; };
            SafeDeleteService.ConfirmDialogOverride = mine;

            var r = DDriveDependencyTools.Delete("Se", (string)created["id"], preview: false, scan_code: true);
            Assert.IsNull(r["error"], r.ToString());
            Assert.IsTrue((bool)r["deleted"], r.ToString());
            Assert.AreEqual(path, (string)r["path"]);
            Assert.IsFalse(File.Exists(path), "ファイルが消える");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SeData>(path));
            Assert.AreEqual(0, confirmCalls, "テスト側の差し替えではなくツール自身の自動承諾が使われる");

            // Addressables のエントリも残らない(TryDelete の PerformDelete が外す)。
            if (AddressablesSync.IsAvailable)
            {
                Assert.IsFalse(AddressablesSync.IsGuidRegistered(guid), "Data の Addressables エントリは削除済み");
            }

            // 呼び出し前の差し替えは finally で元に戻る。
            Assert.AreSame(mine, SafeDeleteService.ConfirmDialogOverride);
        }

        [Test]
        public void ResultJson_ListsBlockersAndCodeRefs_CappedAtMaxListed()
        {
            var (created, path) = CreateIndexedSe("McpDelBlocked");
            var asset = AssetDatabase.LoadAssetAtPath<SeData>(path);
            Assert.IsNotNull(asset);
            var plan = new DDriveDependencyTools.DeletePlan { Path = path };
            plan.Blockers.Add(Ref("Assets/Some.prefab", "Root/Obj", "AudioSource"));
            for (var i = 0; i < DDriveDependencyTools.MaxListed + 3; i++)
            {
                plan.Blockers.Add(Ref($"Assets/Scene{i}.unity", "", "Mono"));
            }

            plan.CodeRefs.Add(new CodeReferenceScan.Hit("Assets/Scripts/A.cs", 12));

            var json = DDriveDependencyTools.ResultJson(plan, false);
            Assert.IsFalse((bool)json["deleted"]);
            Assert.AreEqual(path, (string)json["path"]);
            var blockers = (JArray)json["blockers"];
            Assert.AreEqual(DDriveDependencyTools.MaxListed, blockers.Count, "一覧は上限で切る");
            Assert.AreEqual(plan.Blockers.Count, (int)json["blockerCount"]);
            Assert.AreEqual("prefab", (string)blockers[0]["kind"]);
            Assert.AreEqual("Assets/Some.prefab", (string)blockers[0]["path"]);
            Assert.AreEqual("Root/Obj", (string)blockers[0]["objectPath"]);
            Assert.AreEqual("scene", (string)blockers[1]["kind"]);
            Assert.AreEqual("Assets/Scripts/A.cs", (string)json["codeRefs"][0]["file"]);
            Assert.AreEqual(12, (int)json["codeRefs"][0]["line"]);

            var preview = DDriveDependencyTools.PreviewJson(plan);
            Assert.IsTrue((bool)preview["blocked"]);
            Assert.AreEqual(path, (string)preview["wouldDelete"]);

            Assert.IsTrue(File.Exists(path), "整形だけでは消えない");
            Assert.IsNotNull(created);
        }

        [Test]
        public void ToProjectRelative_StripsProjectRoot_AndKeepsRelativePaths()
        {
            var root = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            Assert.AreEqual("Packages/x/A.cs", DDriveDependencyTools.ToProjectRelative(root + "/Packages/x/A.cs"));
            Assert.AreEqual("Assets/B.cs", DDriveDependencyTools.ToProjectRelative("Assets\\B.cs"));
            Assert.AreEqual("Assets/B.cs", DDriveDependencyTools.ToProjectRelative("Assets/B.cs"));
        }

        [Test]
        public void DeletePlan_IsBlocked_ByUsagesOrCodeRefs()
        {
            // 実コードを書かずに、分析結果(IsBlocked)だけを確かめる。
            var plan = new DDriveDependencyTools.DeletePlan { Path = "Assets/X.asset" };
            Assert.IsFalse(plan.IsBlocked);
            plan.CodeRefs.Add(new CodeReferenceScan.Hit("A.cs", 1));
            Assert.IsTrue(plan.IsBlocked, "コード参照だけでも拒否");
        }

        [Test]
        public void Delete_RefusedWhenWriteDisabled()
        {
            var (created, path) = CreateIndexedSe("McpDelDisabled");
            DDriveProjectSettings.instance.SetMcpAllowWrite(false, save: false);
            var r = DDriveDependencyTools.Delete("Se", (string)created["id"], preview: true);
            Assert.AreEqual(McpGuard.CodeWriteDisabled, Code(r));
            Assert.IsTrue(File.Exists(path));
        }

        // ── ddrive_editor_open ──

        [Test]
        public void EditorOpen_OpensAudioEditorForSe()
        {
            if (Application.isBatchMode)
            {
                Assert.Ignore("batch mode では EditorWindow を開かない");
            }

            var (created, _) = CreateIndexedSe("McpOpenSe");
            var r = DDriveDependencyTools.EditorOpen("Se", (string)created["id"]);
            try
            {
                Assert.IsNull(r["error"], r.ToString());
                Assert.IsNotNull(r["opened"], r.ToString());
                Assert.AreEqual(JTokenType.String, r["opened"].Type);
                StringAssert.Contains("Audio", (string)r["opened"]);
            }
            finally
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    if (w.GetType().Name == (string)r["opened"])
                    {
                        w.Close();
                    }
                }
            }
        }

        [Test]
        public void EditorOpen_BadArgsAreRejected()
        {
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveDependencyTools.EditorOpen("Nope", "1")));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveDependencyTools.EditorOpen("Se", "123")));
        }

        // ── 属性 ──

        [Test]
        public void ToolAttributes_AreDeclaredAsDesigned()
        {
            T Attr<T>(string method) where T : System.Attribute
                => (T)System.Attribute.GetCustomAttribute(typeof(DDriveDependencyTools).GetMethod(method), typeof(T));

            var usages = Attr<UnityMCP.Editor.Core.Attributes.McpToolAttribute>("Usages");
            Assert.AreEqual("ddrive_asset_usages", usages.Name);
            Assert.AreEqual("diagnostics", usages.Group);
            Assert.IsFalse(usages.Destructive);

            var unused = Attr<UnityMCP.Editor.Core.Attributes.McpToolAttribute>("Unused");
            Assert.AreEqual("ddrive_asset_unused", unused.Name);
            Assert.AreEqual("diagnostics", unused.Group);

            var del = Attr<UnityMCP.Editor.Core.Attributes.McpToolAttribute>("Delete");
            Assert.AreEqual("ddrive_asset_delete", del.Name);
            Assert.IsTrue(del.Destructive, "削除は Destructive(confirm 必須)");
            Assert.AreEqual("authoring", del.Group);

            var open = Attr<UnityMCP.Editor.Core.Attributes.McpToolAttribute>("EditorOpen");
            Assert.AreEqual("ddrive_editor_open", open.Name);
            Assert.AreEqual("authoring", open.Group);
            Assert.IsFalse(open.Destructive);
        }
    }
}
