using System.Linq;
using DDrive.Editor.Spec;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [32_spec_web.md] §10.4.2 O-6 — パラメータスキーマ・現在値の反射生成(SpecParamSchemaBuilder)。
    // すべて ScriptableObject.CreateInstance でメモリ上に作ったインスタンスだけを使い、AssetDatabase
    // にアセットファイルを一切作らない(実 GameData・カタログ・Addressables グループには触れない)。
    public class SpecParamSchemaBuilderTests
    {
        [Test]
        public void BuildSchemas_OneEntryPerConcreteType_ControlSkinHasTwo()
        {
            var schemas = SpecParamSchemaBuilder.BuildSchemas();

            // AssetType(None 除く)は 17 種類、ControlSkin だけ ButtonSkinData/SliderSkinData の
            // 2 具象型があるため、スキーマの総数は 16 + 2 = 18 になる([27_spec_sheet.md] §7.2)。
            Assert.AreEqual(18, schemas.Count);

            var controlSkinSchemas = schemas.Where(s => (string)s["assetType"] == "ControlSkin").ToArray();
            Assert.AreEqual(2, controlSkinSchemas.Length);
            CollectionAssert.AreEquivalent(
                new[] { "ButtonSkinData", "SliderSkinData" },
                controlSkinSchemas.Select(s => (string)s["concreteType"]).ToArray());
        }

        [Test]
        public void BuildSchemas_SeData_ContainsOwnFieldsWithTooltipAndRange_ExcludesBaseAndInternalFields()
        {
            var schemas = SpecParamSchemaBuilder.BuildSchemas();
            var seSchema = schemas.Single(s => (string)s["concreteType"] == "SeData");
            Assert.AreEqual("Se", (string)seSchema["assetType"]);

            var fields = (JArray)seSchema["fields"];
            var clips = fields.Single(f => (string)f["name"] == "Clips");
            Assert.AreEqual("AudioClip[]", (string)clips["type"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)clips["tooltip"]), "[Tooltip] の内容が入っているはず");

            var volume = fields.Single(f => (string)f["name"] == "Volume");
            Assert.AreEqual("float", (string)volume["type"]);
            Assert.AreEqual(0f, (float)volume["min"]);
            Assert.AreEqual(1f, (float)volume["max"]);

            var fieldNames = fields.Select(f => (string)f["name"]).ToArray();
            // AssetDataBase(全種共通の管理項目)は Web 側の発注が別欄で持つため、含めない。
            CollectionAssert.DoesNotContain(fieldNames, "Id");
            CollectionAssert.DoesNotContain(fieldNames, "DisplayName");
            CollectionAssert.DoesNotContain(fieldNames, "Category");
            CollectionAssert.DoesNotContain(fieldNames, "Icon");
            // Unity 内部フィールドも除外する。
            CollectionAssert.DoesNotContain(fieldNames, "m_Script");
        }

        [Test]
        public void BuildSchemas_ButtonSkinData_IncludesIntermediateBaseFields_ButNotAssetDataBaseFields()
        {
            var schemas = SpecParamSchemaBuilder.BuildSchemas();
            var buttonSchema = schemas.Single(s => (string)s["concreteType"] == "ButtonSkinData");

            var fields = (JArray)buttonSchema["fields"];
            var fieldNames = fields.Select(f => (string)f["name"]).ToArray();

            // ControlSkinData(中間基底)のフィールドは種別固有パラメータとして含める。
            CollectionAssert.Contains(fieldNames, "AlphaHitThreshold");
            CollectionAssert.Contains(fieldNames, "HoverSe");

            var alphaHitThreshold = fields.Single(f => (string)f["name"] == "AlphaHitThreshold");
            Assert.AreEqual(0f, (float)alphaHitThreshold["min"]);
            Assert.AreEqual(1f, (float)alphaHitThreshold["max"]);

            var hoverSe = fields.Single(f => (string)f["name"] == "HoverSe");
            Assert.AreEqual("AssetId<SeMarker>", (string)hoverSe["type"]);

            // AssetDataBase のフィールドは除外する。
            CollectionAssert.DoesNotContain(fieldNames, "Id");
            CollectionAssert.DoesNotContain(fieldNames, "DisplayName");
        }

        [Test]
        public void BuildCurrentValues_ObjectReference_UsesDisplayNameOnly()
        {
            var data = ScriptableObject.CreateInstance<TextureData>();
            var texture = new Texture2D(4, 4) { name = "SpecParamSchemaBuilderTestTexture" };
            data.Texture = texture;

            try
            {
                var values = SpecParamSchemaBuilder.BuildCurrentValues(data);
                Assert.AreEqual("SpecParamSchemaBuilderTestTexture", (string)values["Texture"]);
            }
            finally
            {
                data.Texture = null;
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void BuildCurrentValues_UnsetObjectReference_IsOmitted()
        {
            var data = ScriptableObject.CreateInstance<TextureData>();

            try
            {
                var values = SpecParamSchemaBuilder.BuildCurrentValues(data);
                Assert.IsNull(values["Texture"], "未設定の参照は実体を送らない(null のまま省略する)");
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void BuildCurrentValues_Array_ShowsCountOnly_NotElements()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            var clip1 = AudioClip.Create("SpecParamSchemaBuilderTestClip1", 10, 1, 44100, false);
            var clip2 = AudioClip.Create("SpecParamSchemaBuilderTestClip2", 10, 1, 44100, false);
            data.Clips = new[] { clip1, clip2 };

            try
            {
                var values = SpecParamSchemaBuilder.BuildCurrentValues(data);
                Assert.AreEqual("2 件", (string)values["Clips"]);
            }
            finally
            {
                data.Clips = null;
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(clip1);
                Object.DestroyImmediate(clip2);
            }
        }

        [Test]
        public void BuildCurrentValues_Enum_ShowsDisplayName()
        {
            var data = ScriptableObject.CreateInstance<TextureData>();
            data.Usage = TextureUsage.UI;

            try
            {
                var values = SpecParamSchemaBuilder.BuildCurrentValues(data);
                Assert.AreEqual("UI", (string)values["Usage"]);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void BuildCurrentValues_ExcludesAssetDataBaseFields()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            data.DisplayName = "テスト";
            data.Id = 12345UL;

            try
            {
                var values = SpecParamSchemaBuilder.BuildCurrentValues(data);
                Assert.IsNull(values["DisplayName"]);
                Assert.IsNull(values["Id"]);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }
    }
}
