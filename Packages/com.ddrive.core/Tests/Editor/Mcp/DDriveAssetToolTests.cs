using System;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Editor.Settings;
using DDrive.Foundation.Data;
using DDrive.Runtime.Audio;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.2 MCP-3(2026-10-07) — ddrive_asset_list / get / create / set。
    // 実作成を伴うテストは一時 GameData ルート(Assets/Tests/DDriveTemp/...)の中だけで行い、TearDown で
    // Addressables のエントリごと消す(AssetCreationServiceTests と同じ後始末。実 Assets/GameData は触らない)。
    // Addressables への登録は AssetCreationService.Create が一時ルートのアセットを実グループに入れるが、
    // RemoveEntriesUnder で外す。グループ asset の dirty は TearDown で保存して持ち越さない。
    public class DDriveAssetToolTests
    {
        private const string TestRoot = "Assets/Tests/DDriveTemp/McpAssetToolsGameData";

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
            DDriveProjectSettings.instance.SetMcpAllowWrite(_originalAllowWrite, save: false);
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope())
                {
                    AssetDatabase.SaveAssets(); // Addressables 設定の dirty を後続テストに持ち越さない
                }
            }
        }

        private static string Code(JObject r) => (string)r["error"]?["code"];

        private static JObject CreateSe(string name, JObject fields = null, string category = "Test", bool preview = false)
            => DDriveAssetTools.CreateIn(TestRoot, "Se", name, category, null, null, fields, preview);

        private static SeData Load(JObject created)
            => AssetDatabase.LoadAssetAtPath<SeData>((string)created["path"]);

        // ── create ──

        [Test]
        public void Create_Preview_WritesNothing()
        {
            var r = CreateSe("McpPreview", preview: true);
            Assert.IsNull(r["error"], r.ToString());
            StringAssert.StartsWith(TestRoot, (string)r["wouldCreate"]);
            StringAssert.EndsWith("SE_Test_McpPreview.asset", (string)r["wouldCreate"]);
            Assert.AreEqual("McpPreview", (string)r["identifier"]);
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestRoot), "preview はフォルダも作らない");
        }

        [Test]
        public void Create_Set_Get_List_RoundTrip()
        {
            var created = CreateSe(
                "McpRound",
                JObject.Parse("{\"Volume\":0.5,\"Loop\":true,\"Tags\":[\"a\"],\"PitchRange\":{\"x\":0.9,\"y\":1.1}}"));
            Assert.IsNull(created["error"], created.ToString());
            var idText = (string)created["id"];
            Assert.IsTrue(ulong.TryParse(idText, out var idValue) && idValue != 0, "id は 10 進文字列");
            Assert.AreEqual(JTokenType.String, created["id"].Type);
            Assert.IsNotNull(created["validation"]["errors"]);
            Assert.IsNotNull(created["validation"]["warnings"]);
            if (AddressablesSync.IsAvailable)
            {
                Assert.IsTrue((bool)created["addressable"], "Create が Addressables へ同じ address で登録する");
            }

            var asset = Load(created);
            Assert.IsNotNull(asset);
            Assert.AreEqual(idValue, asset.Id);
            Assert.AreEqual(0.5f, asset.Volume, 1e-5f);
            Assert.IsTrue(asset.Loop);
            CollectionAssert.AreEqual(new[] { "a" }, asset.Tags);
            Assert.AreEqual("McpRound", asset.DisplayName);
            Assert.AreEqual("Test", asset.Category);
            Assert.AreEqual(1, asset.Version, "新規作成は v1");
            Assert.AreEqual("[mcp] 作成", asset.ChangeNote);

            // 同じ種別の ID 重複なしで、カタログにも載っている。
            var catalog = AssetDatabase.LoadAssetAtPath<DDrive.Foundation.Registry.AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            Assert.IsTrue(catalog.Entries.Any(e => e.Id == asset.Id));

            // set: 差分が {field,from,to}、ChangeNote の先頭に [mcp] が 1 つ、Version が進む。
            var set = DDriveAssetTools.Set("Se", idText, JObject.Parse("{\"Description\":\"説明\",\"Volume\":0.8}"));
            Assert.IsNull(set["error"], set.ToString());
            var changed = (JArray)set["changed"];
            Assert.AreEqual(2, changed.Count);
            var volume = changed.First(c => (string)c["field"] == "Volume");
            Assert.AreEqual(0.5, (double)volume["from"], 1e-6);
            Assert.AreEqual(0.8, (double)volume["to"], 1e-6);
            Assert.IsNotNull(set["validation"]);

            asset = Load(created);
            Assert.AreEqual("説明", asset.Description);
            Assert.AreEqual(0.8f, asset.Volume, 1e-5f);
            Assert.AreEqual(2, asset.Version, "set は保存フックで Version が進む");
            StringAssert.StartsWith("[mcp] ", asset.ChangeNote);
            StringAssert.DoesNotContain("[mcp] [mcp]", asset.ChangeNote);

            // もう一度 set しても、接頭辞は 1 つのまま。
            DDriveAssetTools.Set("Se", idText, JObject.Parse("{\"Description\":\"説明2\"}"));
            Assert.AreEqual(1, CountOccurrences(Load(created).ChangeNote, "[mcp] "));

            // get: 既定の欄(共通 + 種別の主要欄)。id は 16 進でも引ける。
            var get = DDriveAssetTools.Get("Se", "0x" + idValue.ToString("X"));
            Assert.IsNull(get["error"], get.ToString());
            Assert.AreEqual(idText, (string)get["id"]);
            Assert.AreEqual(AssetDatabase.GetAssetPath(asset), (string)get["path"]);
            var fields = (JObject)get["fields"];
            foreach (var name in FieldTables.DefaultFields(DDrive.Foundation.Identity.AssetType.Se, typeof(SeData)))
            {
                Assert.IsNotNull(fields[name], $"既定の欄 {name} が返る");
            }

            Assert.AreEqual("説明2", (string)fields["Description"]);
            Assert.IsNull(fields["Id"], "既定では Id 等は返さない");
            Assert.IsNotNull(get["validation"]);

            // path でも引ける。
            var byPath = DDriveAssetTools.Get("Se", path: AssetDatabase.GetAssetPath(asset), fields: "Volume,Flags.Load");
            Assert.AreEqual(0.8, (double)byPath["fields"]["Volume"], 1e-6);
            Assert.AreEqual("Preload", (string)byPath["fields"]["Flags.Load"], "Se は既定 Preload");

            // list: 絞り込み(query / category)と fields。
            var list = DDriveAssetTools.List("Se", query: "mcpround", fields: "id,name,category,Volume");
            Assert.IsNull(list["error"], list.ToString());
            var row = (JObject)((JArray)list["items"]).Single();
            Assert.AreEqual(idText, (string)row["id"]);
            Assert.AreEqual("McpRound", (string)row["name"]);
            Assert.AreEqual("Test", (string)row["category"]);
            Assert.AreEqual(0.8, (double)row["Volume"], 1e-6);
            Assert.AreEqual(1, (int)list["total"]);
            Assert.IsNull(list["next"]);

            var none = DDriveAssetTools.List("Se", query: "mcpround", category: "Other");
            Assert.AreEqual(0, ((JArray)none["items"]).Count);
        }

        private static int CountOccurrences(string text, string part)
        {
            var n = 0;
            var i = 0;
            while ((i = text.IndexOf(part, i, StringComparison.Ordinal)) >= 0)
            {
                n++;
                i += part.Length;
            }

            return n;
        }

        [Test]
        public void Create_DuplicateIdentifier_IsRejected()
        {
            Assert.IsNull(CreateSe("McpDup")["error"]);
            var second = CreateSe("McpDup");
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(second));
            StringAssert.Contains("既に", (string)second["error"]["msg"]);
        }

        [Test]
        public void Create_InvalidIdentifierOrBadArgs_AreRejected_WithoutCreatingAnything()
        {
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.CreateIn(TestRoot, "Se", "x", "T", "bad id", null, null, false)));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.CreateIn(TestRoot, "Se", "x", "T", "lower", null, null, false)));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.CreateIn(TestRoot, "Nope", "x", "T", null, null, null, false)));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.CreateIn(TestRoot, "Se", " ", "T", null, null, null, false)));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.CreateIn(TestRoot, "ControlSkin", "x", "T", null, null, null, false)));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.CreateIn(TestRoot, "Se", "x", "T", null, "BgmData", null, false)));
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestRoot), "どれも何も作らない");
        }

        [Test]
        public void Create_UnwritableField_FailsBeforeCreatingAnything()
        {
            var badType = CreateSe("McpBadField", JObject.Parse("{\"Volume\":\"loud\"}"));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(badType));
            StringAssert.Contains("Volume", (string)badType["error"]["msg"]);

            var readOnly = CreateSe("McpBadField", JObject.Parse("{\"Id\":\"5\"}"));
            Assert.AreEqual(McpGuard.CodeReadOnlyField, Code(readOnly));

            var unknown = CreateSe("McpBadField", JObject.Parse("{\"NoSuch\":1}"));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(unknown));

            Assert.IsFalse(AssetDatabase.IsValidFolder(TestRoot), "fields が書けないときは何も作らない");
        }

        [Test]
        public void Create_ControlSkin_WithDataClass_ResolvesTheClass()
        {
            var r = DDriveAssetTools.CreateIn(TestRoot, "ControlSkin", "McpSkin", "Test", null, "SliderSkinData", null, true);
            Assert.IsNull(r["error"], r.ToString());
            StringAssert.Contains("SKIN_Test_McpSkin", (string)r["wouldCreate"]);
        }

        [Test]
        public void Create_ChangeNoteInFields_IsPrefixedOnce()
        {
            var r = CreateSe("McpNote", JObject.Parse("{\"ChangeNote\":\"最初のメモ\"}"));
            Assert.IsNull(r["error"], r.ToString());
            Assert.AreEqual("[mcp] 最初のメモ", Load(r).ChangeNote);
        }

        // ── set ──

        [Test]
        public void Set_ReadOnlyField_IsRejected_AndWritesNothing()
        {
            var created = CreateSe("McpReadOnly");
            var asset = Load(created);
            var idBefore = asset.Id;
            var versionBefore = asset.Version;

            var r = DDriveAssetTools.Set("Se", (string)created["id"], JObject.Parse("{\"Description\":\"x\",\"Id\":\"5\"}"));
            Assert.AreEqual(McpGuard.CodeReadOnlyField, Code(r));
            StringAssert.Contains("Id", (string)r["error"]["msg"]);

            foreach (var name in McpGuard.ReadOnlyFields)
            {
                var each = DDriveAssetTools.Set("Se", (string)created["id"], new JObject { [name] = "1" });
                Assert.AreEqual(McpGuard.CodeReadOnlyField, Code(each), name);
            }

            // 書かれていないことを、ディスクから読み直した SerializedObject で確かめる。
            AssetDatabase.ImportAsset((string)created["path"], ImportAssetOptions.ForceUpdate);
            var reloaded = Load(created);
            var so = new SerializedObject(reloaded);
            Assert.AreEqual(idBefore, reloaded.Id);
            Assert.AreEqual(string.Empty, so.FindProperty("Description").stringValue ?? string.Empty, "同じ呼び出しの Description も書かれない");
            Assert.AreEqual(versionBefore, reloaded.Version);
            Assert.IsFalse(EditorUtility.IsDirty(reloaded));
        }

        [Test]
        public void Set_TypeMismatchInSecondField_IsAtomic()
        {
            var created = CreateSe("McpAtomic");
            var r = DDriveAssetTools.Set("Se", (string)created["id"], JObject.Parse("{\"Description\":\"x\",\"Volume\":\"loud\"}"));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(r));
            StringAssert.Contains("Volume", (string)r["error"]["msg"]);

            var asset = Load(created);
            Assert.IsTrue(string.IsNullOrEmpty(asset.Description), "先に書ける欄があっても、1 つでも不正なら何も書かない");
            Assert.IsFalse(EditorUtility.IsDirty(asset));
        }

        [Test]
        public void Set_Preview_ReturnsDiffOnly()
        {
            var created = CreateSe("McpSetPreview");
            var r = DDriveAssetTools.Set("Se", (string)created["id"], JObject.Parse("{\"Description\":\"プレビュー\"}"), preview: true);
            Assert.IsNull(r["error"], r.ToString());
            var change = (JObject)((JArray)r["wouldChange"]).Single();
            Assert.AreEqual("Description", (string)change["field"]);
            Assert.AreEqual("プレビュー", (string)change["to"]);
            Assert.IsNull(r["changed"]);

            var asset = Load(created);
            Assert.IsTrue(string.IsNullOrEmpty(asset.Description));
            Assert.IsFalse(EditorUtility.IsDirty(asset));
            Assert.AreEqual("[mcp] 作成", asset.ChangeNote, "preview は ChangeNote も変えない");
        }

        [Test]
        public void Set_NoChange_DoesNotTouchTheAsset()
        {
            var created = CreateSe("McpNoChange");
            var asset = Load(created);
            var version = asset.Version;
            var note = asset.ChangeNote;

            var r = DDriveAssetTools.Set("Se", (string)created["id"], JObject.Parse("{\"Category\":\"Test\"}"));
            Assert.IsNull(r["error"], r.ToString());
            Assert.AreEqual(0, ((JArray)r["changed"]).Count);

            asset = Load(created);
            Assert.AreEqual(version, asset.Version, "値が変わらない set で版を進めない");
            Assert.AreEqual(note, asset.ChangeNote);
            Assert.IsFalse(EditorUtility.IsDirty(asset));
        }

        [Test]
        public void Set_ChangeNoteInFields_PrefixesTheNewValueOnce()
        {
            var created = CreateSe("McpSetNote");
            DDriveAssetTools.Set("Se", (string)created["id"], JObject.Parse("{\"ChangeNote\":\"音量を調整\"}"));
            Assert.AreEqual("[mcp] 音量を調整", Load(created).ChangeNote);

            DDriveAssetTools.Set("Se", (string)created["id"], JObject.Parse("{\"ChangeNote\":\"[mcp] 既に付いている\"}"));
            Assert.AreEqual("[mcp] 既に付いている", Load(created).ChangeNote);
        }

        [Test]
        public void Set_EmptyChangeNote_GetsAGeneratedSummary()
        {
            var created = CreateSe("McpSetEmptyNote");
            // ChangeNote を空にしておいてから別の欄を変える(空のときだけ要約が入る)。
            var asset = Load(created);
            Assert.IsNotNull(asset);
            var so = new SerializedObject(asset);
            so.FindProperty("ChangeNote").stringValue = string.Empty;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            DDriveAssetTools.Set("Se", (string)created["id"], JObject.Parse("{\"Description\":\"d\"}"));
            StringAssert.StartsWith("[mcp] 変更: Description", Load(created).ChangeNote);
        }

        [Test]
        public void Set_ElementPathsAndBadFieldNames_AreInvalidParams()
        {
            var created = CreateSe("McpBadPaths");
            foreach (var key in new[] { "Tags[0]", "Tags.Array.data[0]", "NoSuchField", "Flags.NoSuch", "" })
            {
                var r = DDriveAssetTools.Set("Se", (string)created["id"], new JObject { [key] = "x" });
                Assert.AreEqual(McpGuard.CodeInvalidParams, Code(r), key);
            }

            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.Set("Se", (string)created["id"], new JObject())));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.Set("Se", (string)created["id"], null)));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.Set("Se", "99", JObject.Parse("{\"Description\":\"x\"}"))));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.Set("Se", "abc", JObject.Parse("{\"Description\":\"x\"}"))));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.Set("Nope", "1", JObject.Parse("{\"Description\":\"x\"}"))));
        }

        [Test]
        public void Set_NestedAndWholeArrayFields_Work()
        {
            var created = CreateSe("McpNested");
            var r = DDriveAssetTools.Set(
                "Se", (string)created["id"],
                JObject.Parse("{\"Flags.Load\":\"Preload\",\"Tags\":[\"x\",\"y\"],\"Anchor\":{\"LocalOffset\":{\"x\":1,\"y\":2,\"z\":3}}}"));
            Assert.IsNull(r["error"], r.ToString());
            var asset = Load(created);
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load);
            CollectionAssert.AreEqual(new[] { "x", "y" }, asset.Tags);
            Assert.AreEqual(2f, asset.Anchor.LocalOffset.y, 1e-5f);
        }

        // ── get / list ──

        [Test]
        public void Get_UnknownField_IsInvalid_AndStarReturnsEverything()
        {
            var created = CreateSe("McpGetFields");
            var bad = DDriveAssetTools.Get("Se", (string)created["id"], fields: "Volume,Nope");
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(bad));
            StringAssert.Contains("Nope", (string)bad["error"]["msg"]);

            var all = DDriveAssetTools.Get("Se", (string)created["id"], fields: "*", max_chars: 100000);
            var fields = (JObject)all["fields"];
            Assert.IsNotNull(fields["Id"]);
            Assert.AreEqual((string)created["id"], (string)fields["Id"], "全欄には Id も出る(読むだけ)");
            Assert.IsNotNull(fields["Clips"]);
            Assert.IsNotNull(fields["Flags"]);
            Assert.IsNull(all["truncated"]);
        }

        [Test]
        public void Get_MaxChars_DropsTrailingFieldsWholeAndSaysSo()
        {
            var created = CreateSe("McpGetTruncate");
            var r = DDriveAssetTools.Get("Se", (string)created["id"], fields: "*", max_chars: 700);
            Assert.IsNull(r["error"], r.ToString());
            Assert.IsTrue((bool)r["truncated"]);
            Assert.IsNotEmpty((string)r["omitted"], "落とした欄名が出る");
            Assert.LessOrEqual(McpJson.Compact(r).Length, 700, "max_chars に収まる");
            Assert.IsNotNull(r["fields"]["Id"], "先頭の欄は残る");
        }

        [Test]
        public void Get_PathOfOtherType_IsRejected()
        {
            var created = CreateSe("McpWrongType");
            var r = DDriveAssetTools.Get("Bgm", path: (string)created["path"]);
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(r));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.Get("Se")), "id も path も無い");
        }

        [Test]
        public void List_Paging_NextCursor_AndCategoryHierarchy()
        {
            CreateSe("McpPageA", category: "Pg/One");
            CreateSe("McpPageB", category: "Pg/One");
            CreateSe("McpPageC", category: "Pg/Two");

            var first = DDriveAssetTools.List("Se", category: "Pg", limit: 2);
            Assert.AreEqual(2, ((JArray)first["items"]).Count);
            Assert.AreEqual(3, (int)first["total"]);
            Assert.AreEqual("2", (string)first["next"]);

            var second = DDriveAssetTools.List("Se", category: "Pg", limit: 2, cursor: (string)first["next"]);
            Assert.AreEqual(1, ((JArray)second["items"]).Count);
            Assert.IsNull(second["next"]);

            var one = DDriveAssetTools.List("Se", category: "Pg/One");
            Assert.AreEqual(2, ((JArray)one["items"]).Count);

            var badCursor = DDriveAssetTools.List("Se", cursor: "abc");
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(badCursor));
        }

        [Test]
        public void List_AllFields_ReturnsEveryField()
        {
            var created = CreateSe("McpListAll");
            var list = DDriveAssetTools.List("Se", query: "McpListAll", fields: "*", max_chars: 100000);
            var row = (JObject)((JArray)list["items"]).Single();
            Assert.IsNotNull(row["id"]);
            Assert.IsNotNull(row["Volume"]);
            Assert.AreEqual((string)created["id"], (string)row["Id"]);

        }

        [Test]
        public void FindAll_Anim_DoesNotMixAnim2D()
        {
            // Anim2DData は AnimData の派生。t:AnimData が拾っても、種別(AssetType)で絞り直されること。
            var entry = FieldTables.RequireType("Anim");
            foreach (var asset in DDriveAssetTools.FindAll(entry))
            {
                Assert.IsNotInstanceOf<DDrive.Runtime.Anim2D.Anim2DData>(asset, AssetDatabase.GetAssetPath(asset));
            }

            foreach (var asset in DDriveAssetTools.FindAll(FieldTables.RequireType("Anim2D")))
            {
                Assert.IsInstanceOf<DDrive.Runtime.Anim2D.Anim2DData>(asset);
            }
        }

        [Test]
        public void List_UnknownType_IsInvalidParams()
        {
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.List("Nope")));
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveAssetTools.List("None")));
        }

        // ── ガード ──

        [Test]
        public void WriteTools_RefuseWhenWriteDisabled()
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(false, save: false);
            Assert.AreEqual(McpGuard.CodeWriteDisabled, Code(CreateSe("McpDisabled")));
            Assert.AreEqual(McpGuard.CodeWriteDisabled, Code(DDriveAssetTools.Set("Se", "1", JObject.Parse("{\"Description\":\"x\"}"))));
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestRoot));

            // 読み取りは書き込み設定に関係なく使える。
            Assert.IsNull(DDriveAssetTools.List("Se", limit: 1)["error"]);
        }

        [Test]
        public void ToolAttributes_AreAsSpecified()
        {
            McpToolAttribute Attr(string method) => (McpToolAttribute)Attribute.GetCustomAttribute(
                typeof(DDriveAssetTools).GetMethod(method), typeof(McpToolAttribute));

            Assert.AreEqual(McpIdempotency.Safe, Attr(nameof(DDriveAssetTools.List)).Idempotency);
            Assert.AreEqual(McpIdempotency.Safe, Attr(nameof(DDriveAssetTools.Get)).Idempotency);
            Assert.IsFalse(Attr(nameof(DDriveAssetTools.Create)).Destructive, "create は Destructive ではない(preview を自前で持つ)");
            Assert.IsFalse(Attr(nameof(DDriveAssetTools.Set)).Destructive);
            Assert.AreEqual("D-Drive MCP: Data の変更", Attr(nameof(DDriveAssetTools.Set)).UndoGroup);
            foreach (var m in new[] { "List", "Get", "Create", "Set" })
            {
                Assert.AreEqual("authoring", Attr(m).Group);
            }
        }
    }
}
