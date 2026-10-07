using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DDrive.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §5.2 MCP-1(2026-10-07) — 返り値を小さく作るためのヘルパー。
    // null の欄・false の bool・空の配列は出さない(既定値は省略)。出したいときは Keep で明示する。
    // JSON は圧縮形(インデント無し)で返す。
    public static class McpJson
    {
        public const int DefaultLimit = 50;
        public const int DefaultMaxLimit = 200;

        // 「null / false / 空配列でも出す」印。Obj の value に包んで渡す。
        public readonly struct Kept
        {
            public readonly object Value;

            public Kept(object value)
            {
                Value = value;
            }
        }

        public static Kept Keep(object value) => new Kept(value);

        public static JObject Obj(params (string key, object value)[] pairs)
        {
            var obj = new JObject();
            if (pairs == null)
            {
                return obj;
            }

            for (var i = 0; i < pairs.Length; i++)
            {
                var (key, value) = pairs[i];
                if (value is Kept kept)
                {
                    obj[key] = kept.Value == null ? JValue.CreateNull() : ToToken(kept.Value);
                    continue;
                }

                if (IsOmitted(value))
                {
                    continue;
                }

                obj[key] = ToToken(value);
            }

            return obj;
        }

        private static JToken ToToken(object value) => value is JToken token ? token : JToken.FromObject(value);

        private static bool IsOmitted(object value)
        {
            switch (value)
            {
                case null:
                    return true;
                case bool b:
                    return !b;
                case JValue jv:
                    return jv.Type == JTokenType.Null || (jv.Type == JTokenType.Boolean && !jv.Value<bool>());
                case JArray ja:
                    return ja.Count == 0;
                case string _:
                    return false;
                case ICollection c:
                    return c.Count == 0;
                default:
                    return false;
            }
        }

        // cursor = 整数オフセット(文字列)。next は続きがあるときだけ付く(無ければ終わり)。
        // limit <= 0 は既定(50)、maxLimit を超えたら maxLimit に丸める。
        public static JObject Page<T>(
            IReadOnlyList<T> items, string cursor, int limit, Func<T, JObject> map, int maxLimit = DefaultMaxLimit)
        {
            var offset = 0;
            if (!string.IsNullOrEmpty(cursor)
                && (!int.TryParse(cursor, out offset) || offset < 0))
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, $"cursor '{cursor}' が不正です");
            }

            if (limit <= 0)
            {
                limit = DefaultLimit;
            }

            limit = Math.Min(limit, maxLimit);

            var count = items?.Count ?? 0;
            var array = new JArray();
            var end = (int)Math.Min((long)count, (long)offset + limit);
            for (var i = offset; i < end; i++)
            {
                array.Add(map(items[i]));
            }

            var result = new JObject { ["items"] = array };
            if (end < count)
            {
                result["next"] = end.ToString();
            }

            return result;
        }

        public static string Compact(JObject obj) => obj == null ? "{}" : obj.ToString(Formatting.None);
    }
}
