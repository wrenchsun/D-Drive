using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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

        // ID は JSON では 10 進文字列(ulong が 2^53 を超えると JS のクライアントで桁落ちするため)。MCP-3 で決定。
        public static string FormatId(ulong id) => id.ToString(CultureInfo.InvariantCulture);

        // 10 進または 0x 16 進の文字列。0 は不可(未設定の ID を指すため)。前後の空白は許す。
        public static bool TryParseId(string text, out ulong id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var s = text.Trim();
            var ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(s.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out id)
                : ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out id);
            return ok && id != 0;
        }

        // 文字列に加え、JSON の整数(ulong の範囲)も許す(許容。2^53 を超える数は送り側で桁落ちするので文字列を推奨)。
        public static bool TryParseId(JToken token, out ulong id)
        {
            id = 0;
            if (token == null)
            {
                return false;
            }

            switch (token.Type)
            {
                case JTokenType.String:
                    return TryParseId((string)token, out id);
                case JTokenType.Integer:
                    return TryParseId(token.ToString(), out id);
                default:
                    return false;
            }
        }

        // ISO 日時の文字列を DateTime に化けさせずに読む(JObject.Parse は既定で日付形式の文字列を DateTime にする)。
        public static JObject Parse(string json)
        {
            return JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
        }

        public static string Compact(JObject obj) => obj == null ? "{}" : obj.ToString(Formatting.None);
    }
}
