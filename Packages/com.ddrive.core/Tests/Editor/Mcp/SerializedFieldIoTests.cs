using DDrive.Editor.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.5 MCP-3(2026-10-07) — SerializedFieldIo の Read / Write(型ごとの往復)と ID の文字列化。
    // アセットは作らない(ScriptableObject.CreateInstance したテスト専用の McpIoProbe / 実 Data 型のインスタンスだけ)。
    public class SerializedFieldIoTests
    {
        private McpIoProbe _probe;
        private SerializedObject _so;

        [SetUp]
        public void SetUp()
        {
            _probe = ScriptableObject.CreateInstance<McpIoProbe>();
            _so = new SerializedObject(_probe);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_probe);
        }

        private JToken RoundTrip(string path, string json)
        {
            var token = JToken.Parse(json);
            SerializedFieldIo.Write(_so.FindProperty(path), token);
            return SerializedFieldIo.Read(_so, path);
        }

        private void AssertRoundTrip(string path, string json)
        {
            var expected = JToken.Parse(json);
            var actual = RoundTrip(path, json);
            Assert.IsTrue(JToken.DeepEquals(expected, actual), $"{path}: 期待 {expected} / 実際 {actual}");
        }

        private void RoundTripExpecting(string path, string writeJson, string expectedJson)
        {
            var actual = RoundTrip(path, writeJson);
            Assert.IsTrue(JToken.DeepEquals(JToken.Parse(expectedJson), actual), $"{path}: 期待 {expectedJson} / 実際 {actual}");
        }

        private McpToolError ExpectInvalid(string path, string json)
        {
            var ex = Assert.Throws<McpToolError>(() => SerializedFieldIo.Write(_so.FindProperty(path), JToken.Parse(json)));
            Assert.AreEqual(McpGuard.CodeInvalidParams, ex.Code);
            StringAssert.Contains(path, ex.Message, "エラー文に欄のパスが入る");
            return ex;
        }

        // ── 数値・文字列 ──

        [Test]
        public void Scalars_RoundTrip()
        {
            AssertRoundTrip("Flag", "true");
            AssertRoundTrip("IntValue", "-42");
            AssertRoundTrip("UIntValue", "4000000000");
            AssertRoundTrip("LongValue", "-9000000000");
            AssertRoundTrip("ByteValue", "200");
            AssertRoundTrip("FloatValue", "0.1");
            AssertRoundTrip("DoubleValue", "0.30000000000000004");
            AssertRoundTrip("Text", "\"こんにちは\"");
            AssertRoundTrip("Letter", "\"x\"");
            AssertRoundTrip("Layers", "5");
        }

        [Test]
        public void ULong_IsStringInJson_AndAcceptsDecimalHexAndNumber()
        {
            AssertRoundTrip("ULongValue", "\"18446744073709551615\"");

            // 16 進・数値でも入る(出力は 10 進文字列)。
            Assert.AreEqual("255", (string)RoundTrip("ULongValue", "\"0xFF\""));
            Assert.AreEqual("5", (string)RoundTrip("ULongValue", "5"));
            Assert.AreEqual(JTokenType.String, SerializedFieldIo.Read(_so, "ULongValue").Type);
        }

        [Test]
        public void Integer_OutOfRange_IsInvalidParamsNamingThePath()
        {
            var ex = ExpectInvalid("ByteValue", "300");
            StringAssert.Contains("byte", ex.Message);
            ExpectInvalid("UIntValue", "-1");
            ExpectInvalid("IntValue", "1.5");
            ExpectInvalid("ULongValue", "\"abc\"");
            ExpectInvalid("Flag", "3");
            ExpectInvalid("FloatValue", "\"abc\"");
        }

        [Test]
        public void Float_IsWrittenAsShortestDecimal()
        {
            SerializedFieldIo.Write(_so.FindProperty("FloatValue"), new JValue(0.1));
            var token = (JValue)SerializedFieldIo.Read(_so, "FloatValue");
            Assert.AreEqual(0.1, (double)token.Value, 1e-12);
            Assert.AreEqual("0.1", token.ToString(Newtonsoft.Json.Formatting.None));
        }

        // ── enum ──

        [Test]
        public void Enum_ByNameCaseInsensitive_AndByNumber()
        {
            Assert.AreEqual("Gamma", (string)RoundTrip("Mode", "\"gamma\""));
            Assert.AreEqual("Beta", (string)RoundTrip("Mode", "1"));
            Assert.AreEqual("Alpha", (string)RoundTrip("Mode", "\"0\""));
            var ex = ExpectInvalid("Mode", "\"Delta\"");
            StringAssert.Contains("Alpha", ex.Message);
        }

        // ── Unity の値型 ──

        [Test]
        public void UnityValueTypes_RoundTrip()
        {
            AssertRoundTrip("Tint", "{\"r\":0.5,\"g\":0.25,\"b\":1,\"a\":0.75}");
            AssertRoundTrip("Vec2", "{\"x\":1.5,\"y\":-2}");
            AssertRoundTrip("Vec3", "{\"x\":1,\"y\":2,\"z\":3}");
            AssertRoundTrip("Vec4", "{\"x\":1,\"y\":2,\"z\":3,\"w\":4}");
            AssertRoundTrip("Rot", "{\"x\":0,\"y\":0,\"z\":0,\"w\":1}");
            AssertRoundTrip("Vec2I", "{\"x\":3,\"y\":-4}");
            AssertRoundTrip("Vec3I", "{\"x\":1,\"y\":2,\"z\":3}");
            AssertRoundTrip("Area", "{\"x\":1,\"y\":2,\"w\":30,\"h\":40}");
            AssertRoundTrip("Box", "{\"center\":{\"x\":1,\"y\":2,\"z\":3},\"size\":{\"x\":4,\"y\":5,\"z\":6}}");
        }

        [Test]
        public void Color_AcceptsHtmlString_AndPartialObjectKeepsOtherChannels()
        {
            SerializedFieldIo.Write(_so.FindProperty("Tint"), JToken.Parse("\"#FF0000\""));
            var c = _so.FindProperty("Tint").colorValue;
            Assert.AreEqual(1f, c.r, 1e-4f);
            Assert.AreEqual(0f, c.g, 1e-4f);

            SerializedFieldIo.Write(_so.FindProperty("Tint"), JToken.Parse("{\"g\":0.5}"));
            c = _so.FindProperty("Tint").colorValue;
            Assert.AreEqual(1f, c.r, 1e-4f, "渡さなかった成分は保たれる");
            Assert.AreEqual(0.5f, c.g, 1e-4f);
        }

        [Test]
        public void Vector_AcceptsArray_AndRejectsUnknownKey()
        {
            SerializedFieldIo.Write(_so.FindProperty("Vec3"), JToken.Parse("[1,2,3]"));
            Assert.AreEqual(new Vector3(1, 2, 3), _so.FindProperty("Vec3").vector3Value);
            ExpectInvalid("Vec3", "{\"q\":1}");
            ExpectInvalid("Vec3", "[1,2]");
            ExpectInvalid("Vec2I", "{\"x\":1.5}");
        }

        [Test]
        public void AnimationCurve_RoundTrip()
        {
            AssertRoundTrip("Curve", "{\"keys\":[{\"t\":0,\"v\":0,\"in\":0,\"out\":1},{\"t\":1,\"v\":2,\"in\":1,\"out\":0}]}");
            ExpectInvalid("Curve", "{\"keys\":[{\"t\":0}]}");
        }

        // ── Object 参照 ──

        [Test]
        public void ObjectReference_ByAssetPath_NullAndTypeCheck()
        {
            const string path = "Packages/com.ddrive.core/package.json";
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TextAsset>(path), "前提: package.json は TextAsset として読める");

            AssertRoundTrip("TextRef", "\"" + path + "\"");
            Assert.IsNotNull(_so.FindProperty("TextRef").objectReferenceValue);

            AssertRoundTrip("TextRef", "null");
            Assert.IsNull(_so.FindProperty("TextRef").objectReferenceValue);

            // 型違い(AudioClip の欄に TextAsset)・存在しないパス・パスの形が違うものは invalid_params。
            ExpectInvalid("ClipRef", "\"" + path + "\"");
            ExpectInvalid("TextRef", "\"Assets/NoSuchFile.txt\"");
            ExpectInvalid("TextRef", "\"C:/x.txt\"");
            ExpectInvalid("TextRef", "5");
        }

        // ── 入れ子・配列 ──

        [Test]
        public void NestedObject_IsAPatch_KeepingUnnamedKeys()
        {
            AssertRoundTrip("Group", "{\"Gain\":2,\"Inner\":{\"Label\":\"a\",\"Count\":3}}");
            SerializedFieldIo.Write(_so.FindProperty("Group"), JToken.Parse("{\"Inner\":{\"Count\":9}}"));
            var group = (JObject)SerializedFieldIo.Read(_so, "Group");
            Assert.AreEqual(2.0, (double)group["Gain"], 1e-6);
            Assert.AreEqual("a", (string)group["Inner"]["Label"]);
            Assert.AreEqual(9, (int)group["Inner"]["Count"]);

            // ドット区切りのパスでも書ける。
            SerializedFieldIo.Write(_so.FindProperty("Group.Inner.Label"), new JValue("z"));
            Assert.AreEqual("z", (string)SerializedFieldIo.Read(_so, "Group.Inner.Label"));

            var ex = Assert.Throws<McpToolError>(() => SerializedFieldIo.Write(_so.FindProperty("Group"), JToken.Parse("{\"Nope\":1}")));
            StringAssert.Contains("Nope", ex.Message);
            StringAssert.Contains("Gain", ex.Message, "使える欄を案内する");
        }

        [Test]
        public void Arrays_AreReplacedWhole()
        {
            AssertRoundTrip("Names", "[\"a\",\"b\",\"c\"]");
            AssertRoundTrip("Names", "[\"x\"]");
            AssertRoundTrip("Names", "[]");
            AssertRoundTrip("Numbers", "[1,2,3]");
            AssertRoundTrip("Items", "[{\"Label\":\"p\",\"Count\":1},{\"Label\":\"q\",\"Count\":2}]");
            ExpectInvalid("Names", "\"notAnArray\"");
            ExpectInvalid("Numbers", "[1,\"x\"]");
        }

        // ── AssetId ──

        [Test]
        public void AssetId_RoundTrip_NullAndShorthand()
        {
            Assert.IsTrue(SerializedFieldIo.Read(_so, "SeRef").Type == JTokenType.Null, "未設定は null");

            AssertRoundTrip("SeRef", "{\"type\":\"Se\",\"id\":\"1234567890123456789\"}");
            Assert.AreEqual("1234567890123456789", (string)SerializedFieldIo.Read(_so, "SeRef")["id"]);

            // type が既にあれば id だけ(文字列・16 進・数値)でも書ける。
            SerializedFieldIo.Write(_so.FindProperty("SeRef"), new JValue("0x10"));
            Assert.AreEqual("16", (string)SerializedFieldIo.Read(_so, "SeRef")["id"]);
            Assert.AreEqual("Se", (string)SerializedFieldIo.Read(_so, "SeRef")["type"]);

            AssertRoundTrip("SeRef", "null");
            ExpectInvalid("SeRef", "{\"id\":\"5\"}");
            ExpectInvalid("SeRef", "{\"type\":\"Se\"}");
            ExpectInvalid("SeRef", "{\"type\":\"Se\",\"id\":\"zz\"}");
            ExpectInvalid("SeRef", "{\"type\":\"Nope\",\"id\":\"5\"}");
            ExpectInvalid("SeRef", "{\"type\":\"Se\",\"id\":\"5\",\"extra\":1}");
        }

        // ── ValueDef ──

        [Test]
        public void ValueDef_Constant_IsMinimal()
        {
            AssertRoundTrip("Motion", "{\"mode\":\"Constant\",\"value\":1.5}");
            var json = (JObject)SerializedFieldIo.Read(_so, "Motion");
            Assert.AreEqual(2, json.Count, "Constant は mode と value だけ");
        }

        [Test]
        public void ValueDef_Parametric_RoundTrip()
        {
            AssertRoundTrip(
                "Motion",
                "{\"mode\":\"Parametric\",\"ease\":\"OutQuad\",\"from\":1,\"to\":0,\"time\":{\"mode\":\"Duration\",\"value\":0.3},\"loop\":\"PingPong\",\"loopCount\":2}");
            // 渡さなかった欄は前の値のまま(パッチ)。loop を戻すには明示する。読み出しでは既定値(Once / 0)は省く。
            RoundTripExpecting(
                "Motion",
                "{\"mode\":\"Parametric\",\"bezier\":[0.25,0.1,0.25,1],\"from\":0,\"to\":10,\"loop\":\"Once\",\"loopCount\":0,\"time\":{\"mode\":\"Rate\",\"value\":2,\"speed\":2,\"ignoreTimeScale\":true}}",
                "{\"mode\":\"Parametric\",\"bezier\":[0.25,0.1,0.25,1],\"from\":0,\"to\":10,\"time\":{\"mode\":\"Rate\",\"value\":2,\"speed\":2,\"ignoreTimeScale\":true}}");
        }

        [Test]
        public void ValueDef_Curve_RoundTrip_AndModeInferredFromKeys()
        {
            AssertRoundTrip(
                "Motion",
                "{\"mode\":\"Curve\",\"curve\":{\"keys\":[{\"t\":0,\"v\":0,\"in\":0,\"out\":0},{\"t\":1,\"v\":1,\"in\":0,\"out\":0}]},\"from\":0,\"to\":5,\"time\":{\"mode\":\"Duration\",\"value\":1}}");

            // mode を省くと、渡したキーから決まる(value → Constant)。
            SerializedFieldIo.Write(_so.FindProperty("Motion"), JToken.Parse("{\"value\":7}"));
            var json = (JObject)SerializedFieldIo.Read(_so, "Motion");
            Assert.AreEqual("Constant", (string)json["mode"]);
            Assert.AreEqual(7.0, (double)json["value"], 1e-6);
        }

        [Test]
        public void ValueDef_UnknownKeyOrBadShape_IsInvalid()
        {
            ExpectInvalid("Motion", "{\"mode\":\"Constant\",\"valu\":1}");
            ExpectInvalid("Motion", "5");
            ExpectInvalid("Motion", "{\"bezier\":[1,2]}");
            ExpectInvalid("Motion", "{\"time\":{\"len\":1}}");
        }

        // ── 対応外・補助 ──

        [Test]
        public void Unsupported_ReadsAsMarker_WriteIsInvalid()
        {
            var read = SerializedFieldIo.Read(_so, "Grad");
            StringAssert.StartsWith("<unsupported", (string)read);
            var ex = Assert.Throws<McpToolError>(() => SerializedFieldIo.Write(_so.FindProperty("Grad"), JToken.Parse("{}")));
            Assert.AreEqual(McpGuard.CodeInvalidParams, ex.Code);
            StringAssert.Contains("Grad", ex.Message);
        }

        [Test]
        public void Read_UnknownPath_IsInvalidParams()
        {
            var ex = Assert.Throws<McpToolError>(() => SerializedFieldIo.Read(_so, "NoSuchField"));
            Assert.AreEqual(McpGuard.CodeInvalidParams, ex.Code);
        }

        [Test]
        public void Describe_CapsLongValuesAt200Chars()
        {
            var longText = new JValue(new string('x', 500));
            var capped = SerializedFieldIo.Describe(longText);
            Assert.LessOrEqual(((string)capped).Length, 205);
        }

        [Test]
        public void Describe_ShortValue_IsReturnedAsIs()
        {
            var v = new JValue(5);
            Assert.AreSame(v, SerializedFieldIo.Describe(v));
        }

        [Test]
        public void TopLevelNames_ExcludeScriptAndListEveryField()
        {
            var names = SerializedFieldIo.TopLevelNames(_so);
            CollectionAssert.DoesNotContain(names, "m_Script");
            CollectionAssert.Contains(names, "Flag");
            CollectionAssert.Contains(names, "Items");
            CollectionAssert.Contains(names, "Grad");
        }

        // ── 実 Data 型(SeData)でも往復する ──

        [Test]
        public void RealDataClass_SeData_FieldsRoundTrip()
        {
            var se = ScriptableObject.CreateInstance<DDrive.Runtime.Audio.SeData>();
            try
            {
                var so = new SerializedObject(se);
                SerializedFieldIo.Write(so.FindProperty("Volume"), new JValue(0.5));
                SerializedFieldIo.Write(so.FindProperty("PitchRange"), JToken.Parse("{\"x\":0.9,\"y\":1.1}"));
                SerializedFieldIo.Write(so.FindProperty("Spatial"), new JValue("AtPosition"));
                SerializedFieldIo.Write(so.FindProperty("Tags"), JToken.Parse("[\"a\",\"b\"]"));
                SerializedFieldIo.Write(so.FindProperty("Flags.Load"), new JValue("Preload"));
                so.ApplyModifiedProperties();

                Assert.AreEqual(0.5f, se.Volume, 1e-5f);
                Assert.AreEqual(new Vector2(0.9f, 1.1f).x, se.PitchRange.x, 1e-5f);
                CollectionAssert.AreEqual(new[] { "a", "b" }, se.Tags);
                Assert.AreEqual(DDrive.Foundation.Data.LoadMode.Preload, se.Flags.Load);
            }
            finally
            {
                Object.DestroyImmediate(se);
            }
        }

        // ── ID の文字列化(McpJson) ──

        [Test]
        public void McpJson_IdParsing_AcceptsDecimalHexAndNumberToken()
        {
            Assert.IsTrue(McpJson.TryParseId("1234567890123456789", out var id));
            Assert.AreEqual(1234567890123456789UL, id);
            Assert.IsTrue(McpJson.TryParseId(" 0xFF ", out id));
            Assert.AreEqual(255UL, id);
            Assert.IsTrue(McpJson.TryParseId(new JValue(42), out id));
            Assert.AreEqual(42UL, id);
            Assert.IsTrue(McpJson.TryParseId(new JValue("18446744073709551615"), out id));
            Assert.AreEqual(ulong.MaxValue, id);

            Assert.IsFalse(McpJson.TryParseId("0", out _), "0 は未設定の ID");
            Assert.IsFalse(McpJson.TryParseId("-1", out _));
            Assert.IsFalse(McpJson.TryParseId("abc", out _));
            Assert.IsFalse(McpJson.TryParseId("", out _));
            Assert.IsFalse(McpJson.TryParseId(new JValue(1.5), out _));
            Assert.IsFalse(McpJson.TryParseId((JToken)null, out _));

            Assert.AreEqual("18446744073709551615", McpJson.FormatId(ulong.MaxValue));
        }
    }
}
