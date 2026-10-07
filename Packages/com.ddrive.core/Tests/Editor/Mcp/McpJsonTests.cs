using System.Collections.Generic;
using DDrive.Editor.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §5.2 / §7 MCP-1(2026-10-07) — 返り値ヘルパーの契約。
    public class McpJsonTests
    {
        [Test]
        public void Obj_DropsNullFalseAndEmptyArrays()
        {
            var o = McpJson.Obj(
                ("a", 1),
                ("n", null),
                ("f", false),
                ("t", true),
                ("empty", new string[0]),
                ("emptyJ", new JArray()),
                ("s", "x"),
                ("zero", 0));

            Assert.IsTrue(o.ContainsKey("a"));
            Assert.IsTrue(o.ContainsKey("t"));
            Assert.IsTrue(o.ContainsKey("s"));
            Assert.IsTrue(o.ContainsKey("zero"));
            Assert.IsFalse(o.ContainsKey("n"));
            Assert.IsFalse(o.ContainsKey("f"));
            Assert.IsFalse(o.ContainsKey("empty"));
            Assert.IsFalse(o.ContainsKey("emptyJ"));
        }

        [Test]
        public void Obj_Keep_ForcesFalseNullAndEmpty()
        {
            var o = McpJson.Obj(
                ("f", McpJson.Keep(false)),
                ("n", McpJson.Keep(null)),
                ("e", McpJson.Keep(new string[0])));

            Assert.AreEqual(false, (bool)o["f"]);
            Assert.AreEqual(JTokenType.Null, o["n"].Type);
            Assert.AreEqual(0, ((JArray)o["e"]).Count);
        }

        [Test]
        public void Page_ReturnsNext_OnlyWhenMore()
        {
            var items = new List<int> { 1, 2, 3, 4, 5 };

            var first = McpJson.Page(items, null, 2, i => new JObject { ["v"] = i });
            Assert.AreEqual(2, ((JArray)first["items"]).Count);
            Assert.AreEqual("2", (string)first["next"]);

            var second = McpJson.Page(items, "2", 2, i => new JObject { ["v"] = i });
            Assert.AreEqual(3, (int)second["items"][0]["v"]);
            Assert.AreEqual("4", (string)second["next"]);

            var last = McpJson.Page(items, "4", 2, i => new JObject { ["v"] = i });
            Assert.AreEqual(1, ((JArray)last["items"]).Count);
            Assert.IsFalse(last.ContainsKey("next"));
        }

        [Test]
        public void Page_ExactFit_HasNoNext()
        {
            var items = new List<int> { 1, 2 };
            var r = McpJson.Page(items, null, 2, i => new JObject { ["v"] = i });
            Assert.IsFalse(r.ContainsKey("next"));
        }

        [Test]
        public void Page_ClampsLimit_ToMaxLimit()
        {
            var items = new List<int>();
            for (var i = 0; i < 300; i++)
            {
                items.Add(i);
            }

            var r = McpJson.Page(items, null, 1000, i => new JObject { ["v"] = i });
            Assert.AreEqual(200, ((JArray)r["items"]).Count);
            Assert.AreEqual("200", (string)r["next"]);
        }

        [Test]
        public void Page_BadCursor_Throws()
        {
            var items = new List<int> { 1 };
            var ex = Assert.Throws<McpToolError>(
                () => McpJson.Page(items, "abc", 10, i => new JObject()));
            Assert.AreEqual(McpGuard.CodeInvalidParams, ex.Code);
        }

        [Test]
        public void Compact_HasNoIndent()
        {
            var s = McpJson.Compact(new JObject { ["a"] = 1, ["b"] = new JArray(1, 2) });
            Assert.AreEqual("{\"a\":1,\"b\":[1,2]}", s);
        }
    }
}
