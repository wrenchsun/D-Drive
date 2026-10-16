using System.Collections.Generic;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Foundation.Data;
using DDrive.Foundation.Extension;
using DDrive.Foundation.Identity;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Mcp
{
    // [1014_extension_platform.md] §2.5(X-5、2026-10-16) — MCP の type 引数が Extension 種別の種別キー(KindKey)を受ける。
    // ddrive_asset_list / get / create / set / delete の type、ddrive_validate の scope=type:<KindKey> / asset:<KindKey>:<id>。
    // このアセンブリは DDrive.Tests* なので provider は既定では発見されない。SetUp で IncludeTestAssemblies = true にして発見し直す。
    public class DDriveExtensionKindMcpTests
    {
        public struct McpKindMarker
        {
        }

        [AssetIdDefinition(AssetType.Extension, typeof(McpKindMarker), "MCPKINDID", KindKey = "McpKind")]
        public sealed class McpKindDataWithDefinition : AssetDataBase
        {
            public float Strength = 1f;
            [HideInInspector] public int Hidden;
            [System.NonSerialized] public int Transient;
        }

        public sealed class McpKindProvider : IAssetKindProvider
        {
            public string KindKey => "McpKind";
            public System.Type DataType => typeof(McpKindDataWithDefinition);
            public string DisplayName => "Mcp Kind";
            public string Prefix => "MCP";
            public string Folder => "McpKind";
            public string CatalogName => "McpKindCatalog";
            public LoadMode DefaultLoad => LoadMode.Preload;
            public Texture2D Icon => null;
            public int Order => 2000;
        }

        private bool _savedInclude;

        [SetUp]
        public void SetUp()
        {
            _savedInclude = AssetKindRegistry.IncludeTestAssemblies;
            AssetKindRegistry.IncludeTestAssemblies = true;
            AssetKindRegistry.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            AssetKindRegistry.IncludeTestAssemblies = _savedInclude;
            AssetKindRegistry.Refresh();
        }

        [Test]
        public void FieldTables_ResolvesKindKey_AndBuiltInStillResolves()
        {
            Assert.IsTrue(FieldTables.TryResolve("McpKind", out var entry));
            Assert.AreEqual(AssetType.Extension, entry.Type);
            Assert.AreEqual("McpKind", entry.KindKey);
            Assert.AreEqual(typeof(McpKindDataWithDefinition), entry.DataClasses[0]);
            CollectionAssert.AreEqual(new[] { "Strength" }, entry.MainFields[0], "Data クラスが宣言した公開欄(非シリアライズ・HideInInspector 以外)");

            Assert.IsTrue(FieldTables.TryResolve("mcpkind", out var lower));
            Assert.AreEqual("McpKind", lower.KindKey, "大小文字無視");
            Assert.IsTrue(FieldTables.TryResolve("Mcp Kind", out _), "DisplayName でも引ける");

            Assert.IsTrue(FieldTables.TryResolve("Se", out var se));
            Assert.AreEqual(AssetType.Se, se.Type);
            Assert.AreEqual(string.Empty, se.KindKey, "組み込み種別の KindKey は空");

            Assert.IsFalse(FieldTables.TryResolve("NoSuchKind", out _));
            Assert.IsFalse(FieldTables.TryResolve(string.Empty, out _));
            StringAssert.Contains("McpKind", FieldTables.TypeNames());
            Assert.AreSame(FieldTables.RequireType("McpKind").DataClasses[0], entry.DataClasses[0]);
        }

        [Test]
        public void FieldTables_DefaultFields_CommonPlusDeclaredFields_ForExtension_AndUnchangedForBuiltIn()
        {
            FieldTables.TryResolve("McpKind", out var entry);
            var fields = new List<string>(FieldTables.DefaultFields(entry));
            CollectionAssert.AreEqual(new[] { "DisplayName", "Category", "Tags", "Description", "Strength" }, fields);

            FieldTables.TryResolve("Vfx", out var vfx);
            CollectionAssert.AreEqual(
                new List<string>(FieldTables.DefaultFields(AssetType.Vfx, typeof(DDrive.Runtime.Vfx.VfxData))),
                new List<string>(FieldTables.DefaultFields(vfx, typeof(DDrive.Runtime.Vfx.VfxData))));
        }

        [Test]
        public void FieldTables_Matches_ChecksTypeAndKindKey()
        {
            FieldTables.TryResolve("McpKind", out var entry);
            FieldTables.TryResolve("Se", out var se);
            var ext = ScriptableObject.CreateInstance<McpKindDataWithDefinition>();
            var seData = ScriptableObject.CreateInstance<DDrive.Runtime.Audio.SeData>();
            try
            {
                Assert.IsTrue(FieldTables.Matches(entry, ext));
                Assert.IsFalse(FieldTables.Matches(entry, seData));
                Assert.IsFalse(FieldTables.Matches(se, ext));
                Assert.IsTrue(FieldTables.Matches(se, seData));
                Assert.IsFalse(FieldTables.Matches(entry, null));
            }
            finally
            {
                Object.DestroyImmediate(ext);
                Object.DestroyImmediate(seData);
            }
        }

        [Test]
        public void ValidationScope_ParsesKindKey_AndBuiltInIsUnchanged()
        {
            var type = ValidationScope.Parse("type:McpKind");
            Assert.AreEqual(ValidationScope.ScopeKind.Type, type.Kind);
            Assert.AreEqual(AssetType.Extension, type.Type);
            Assert.AreEqual("McpKind", type.KindKey);
            Assert.AreEqual("type:McpKind", type.Text);

            var asset = ValidationScope.Parse("asset:mcpkind:42");
            Assert.AreEqual(ValidationScope.ScopeKind.Asset, asset.Kind);
            Assert.AreEqual("McpKind", asset.KindKey);
            Assert.AreEqual(42UL, asset.Id);
            Assert.AreEqual("asset:McpKind:42", asset.Text);

            var se = ValidationScope.Parse("type:Se");
            Assert.AreEqual(AssetType.Se, se.Type);
            Assert.AreEqual(string.Empty, se.KindKey);
            Assert.AreEqual("type:Se", se.Text);

            // X-R-05: 組み込みの種別名は provider より先に解決される(大小無視)。
            var model = ValidationScope.Parse("type:model");
            Assert.AreEqual(AssetType.Model, model.Type);
            Assert.IsTrue(string.IsNullOrEmpty(model.KindKey));

            Assert.Throws<McpToolError>(() => ValidationScope.Parse("type:NoSuchKind"));
        }

        // ddrive_asset_create の preview: 種別キーから Data クラス・接頭辞・フォルダが決まる(何も作らない)。
        [Test]
        public void AssetCreate_Preview_UsesProviderPrefixAndFolder()
        {
            var result = DDriveAssetTools.CreateIn(DDrive.Tests.Editor.TestTempFolder.Root + "/TempMcpKind", "McpKind", "テスト", null, "Probe", null, null, true);
            var path = (string)result["wouldCreate"];
            StringAssert.Contains("/McpKind/", path, "provider.Folder 配下");
            StringAssert.Contains("MCP_Probe", path, "provider.Prefix の接頭辞");
            Assert.IsFalse(UnityEditor.AssetDatabase.IsValidFolder(DDrive.Tests.Editor.TestTempFolder.Root + "/TempMcpKind/McpKind"), "preview は何も作らない");
        }
    }
}
