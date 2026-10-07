using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using BigInteger = System.Numerics.BigInteger;

namespace DDrive.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.5 MCP-3(2026-10-07) — SerializedProperty ⇄ JSON の変換。
    // ddrive_asset_get / list / set / create が「欄の値」を読み書きするときの唯一の口。
    //
    // 値の形(Read の出力 = Write の入力。Write は同じ形を受ける):
    //   int / long / uint 等 = 数値、ulong = 10 進文字列(JS で 2^53 を超えると桁落ちするため)、float / double = 数値、bool、string
    //   enum = 名前(Write は名前の大小文字無視か数値。[Flags] の組み合わせは Read が数値で返す)
    //   Color = {r,g,b,a}(Write は "#RRGGBB[AA]" も可)、Vector2/3/4・Quaternion = {x,y,(z,w)}(Write は配列も可)、
    //   Rect = {x,y,w,h}、Bounds = {center:{x,y,z},size:{x,y,z}}、Vector2Int/3Int = {x,y,(z)}
    //   AnimationCurve = {keys:[{t,v,in,out}]}
    //   Object 参照 = アセットパス文字列("Assets/..." / "Packages/..."。サブアセットは "path#名前")か null
    //   AssetId 系 = {type,id}(id は 10 進文字列。未設定は null)
    //   ValueDef = 最小 JSON({mode:"Constant",value:1} / {mode:"Parametric",ease,from,to,time,...} / {mode:"Curve",curve,...}。
    //     [17_value_definition.md])。ValueDef3 / ValueDefColor は通常の入れ子オブジェクト
    //   その他の struct / class = 入れ子オブジェクト(Write は渡したキーだけを上書きする「パッチ」)
    //   配列 = JArray(Write は配列全体の置き換えだけ。要素単位の更新は不可。増やした要素は直前の要素の複製から始まる)
    //   ManagedReference = {"$type":型名, ...欄}(Write は既存インスタンスの欄のパッチのみ。型の新規生成は不可)
    // 対応外の型(Gradient 等)は Read が "<unsupported:型>" の文字列、Write が invalid_params。
    public static class SerializedFieldIo
    {
        private const int MaxDepth = 12;
        private const int DescribeMaxChars = 200;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ───────────────────────── Read ─────────────────────────

        public static JToken Read(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            if (p == null)
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, $"欄 '{path}' がありません");
            }

            return Read(p);
        }

        public static JToken Read(SerializedProperty p) => Read(p, 0);

        private static JToken Read(SerializedProperty p, int depth)
        {
            if (depth > MaxDepth)
            {
                return new JValue("<too deep>");
            }

            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return ReadInteger(p);
                case SerializedPropertyType.Boolean:
                    return new JValue(p.boolValue);
                case SerializedPropertyType.Float:
                    return ReadFloat(p);
                case SerializedPropertyType.String:
                    return new JValue(p.stringValue ?? string.Empty);
                case SerializedPropertyType.Character:
                    return new JValue(((char)p.intValue).ToString());
                case SerializedPropertyType.LayerMask:
                    return new JValue(p.intValue);
                case SerializedPropertyType.Enum:
                    return ReadEnum(p);
                case SerializedPropertyType.Color:
                {
                    var c = p.colorValue;
                    return new JObject { ["r"] = Num(c.r), ["g"] = Num(c.g), ["b"] = Num(c.b), ["a"] = Num(c.a) };
                }

                case SerializedPropertyType.Vector2:
                {
                    var v = p.vector2Value;
                    return new JObject { ["x"] = Num(v.x), ["y"] = Num(v.y) };
                }

                case SerializedPropertyType.Vector3:
                {
                    var v = p.vector3Value;
                    return new JObject { ["x"] = Num(v.x), ["y"] = Num(v.y), ["z"] = Num(v.z) };
                }

                case SerializedPropertyType.Vector4:
                {
                    var v = p.vector4Value;
                    return new JObject { ["x"] = Num(v.x), ["y"] = Num(v.y), ["z"] = Num(v.z), ["w"] = Num(v.w) };
                }

                case SerializedPropertyType.Quaternion:
                {
                    var q = p.quaternionValue;
                    return new JObject { ["x"] = Num(q.x), ["y"] = Num(q.y), ["z"] = Num(q.z), ["w"] = Num(q.w) };
                }

                case SerializedPropertyType.Vector2Int:
                {
                    var v = p.vector2IntValue;
                    return new JObject { ["x"] = v.x, ["y"] = v.y };
                }

                case SerializedPropertyType.Vector3Int:
                {
                    var v = p.vector3IntValue;
                    return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
                }

                case SerializedPropertyType.Rect:
                {
                    var r = p.rectValue;
                    return new JObject { ["x"] = Num(r.x), ["y"] = Num(r.y), ["w"] = Num(r.width), ["h"] = Num(r.height) };
                }

                case SerializedPropertyType.Bounds:
                {
                    var b = p.boundsValue;
                    return new JObject
                    {
                        ["center"] = new JObject { ["x"] = Num(b.center.x), ["y"] = Num(b.center.y), ["z"] = Num(b.center.z) },
                        ["size"] = new JObject { ["x"] = Num(b.size.x), ["y"] = Num(b.size.y), ["z"] = Num(b.size.z) },
                    };
                }

                case SerializedPropertyType.AnimationCurve:
                    return ReadCurve(p.animationCurveValue);
                case SerializedPropertyType.ObjectReference:
                    return ReadObjectReference(p);
                case SerializedPropertyType.ManagedReference:
                {
                    var obj = ReadChildren(p, depth);
                    var typeName = p.managedReferenceFullTypename;
                    if (string.IsNullOrEmpty(typeName))
                    {
                        return JValue.CreateNull();
                    }

                    var result = new JObject { ["$type"] = typeName };
                    foreach (var kv in obj)
                    {
                        result[kv.Key] = kv.Value;
                    }

                    return result;
                }

                case SerializedPropertyType.Generic:
                    if (p.isArray)
                    {
                        var array = new JArray();
                        var count = p.arraySize;
                        for (var i = 0; i < count; i++)
                        {
                            array.Add(Read(p.GetArrayElementAtIndex(i), depth + 1));
                        }

                        return array;
                    }

                    if (IsAssetId(p))
                    {
                        return ReadAssetId(p);
                    }

                    if (IsValueDef(p))
                    {
                        return ReadValueDef(p, depth);
                    }

                    return ReadChildren(p, depth);
                default:
                    return new JValue("<unsupported:" + p.propertyType + ">");
            }
        }

        private static JObject ReadChildren(SerializedProperty p, int depth)
        {
            var obj = new JObject();
            var it = p.Copy();
            var end = p.GetEndProperty();
            var enter = it.Next(true);
            while (enter && !SerializedProperty.EqualContents(it, end))
            {
                obj[it.name] = Read(it, depth + 1);
                enter = it.Next(false);
            }

            return obj;
        }

        private static JToken ReadInteger(SerializedProperty p)
        {
            var type = IntegerTypeName(p);
            if (type == "ulong")
            {
                return new JValue(p.ulongValue.ToString(Inv));
            }

            return new JValue(p.longValue);
        }

        private static JToken ReadFloat(SerializedProperty p)
        {
            if (p.type == "double")
            {
                var d = p.doubleValue;
                return double.IsNaN(d) || double.IsInfinity(d) ? new JValue(d.ToString(Inv)) : NumToken(d);
            }

            return Num(p.floatValue);
        }

        // float を「0.1f → 0.10000000149」にならない 10 進の最短表現で JSON の数にする。
        // 整数値(1.0 など)は整数で出す(JSON が "1.0" より "1" の方が短く、トークンを食わない)。
        private static JToken Num(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f))
            {
                return new JValue(f.ToString(Inv));
            }

            return NumToken(double.Parse(f.ToString("R", Inv), Inv));
        }

        private static JToken NumToken(double d)
            => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? new JValue((long)d) : new JValue(d);

        private static JToken ReadEnum(SerializedProperty p)
        {
            var index = p.enumValueIndex;
            var names = p.enumNames;
            if (index >= 0 && index < names.Length)
            {
                return new JValue(names[index]);
            }

            return new JValue(p.enumValueFlag);
        }

        private static JToken ReadCurve(AnimationCurve curve)
        {
            var keys = new JArray();
            if (curve != null)
            {
                foreach (var k in curve.keys)
                {
                    keys.Add(new JObject { ["t"] = Num(k.time), ["v"] = Num(k.value), ["in"] = Num(k.inTangent), ["out"] = Num(k.outTangent) });
                }
            }

            return new JObject { ["keys"] = keys };
        }

        private static JToken ReadObjectReference(SerializedProperty p)
        {
            var obj = p.objectReferenceValue;
            if (obj == null)
            {
                return JValue.CreateNull();
            }

            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
            {
                return JValue.CreateNull();
            }

            return new JValue(AssetDatabase.IsMainAsset(obj) ? path : path + "#" + obj.name);
        }

        private static bool IsAssetId(SerializedProperty p)
            => p.type != null && p.type.StartsWith("AssetId`1", StringComparison.Ordinal);

        private static JToken ReadAssetId(SerializedProperty p)
        {
            var value = p.FindPropertyRelative("value");
            var type = p.FindPropertyRelative("type");
            if (value == null || type == null || value.ulongValue == 0)
            {
                return JValue.CreateNull();
            }

            return new JObject { ["type"] = ((JValue)ReadEnum(type)).Value.ToString(), ["id"] = value.ulongValue.ToString(Inv) };
        }

        private static bool IsValueDef(SerializedProperty p) => p.type == "ValueDef";

        // ValueDef の最小 JSON。Mode が使わない欄(Constant の From/To/Time 等)は出さない。
        private static JToken ReadValueDef(SerializedProperty p, int depth)
        {
            var mode = (JValue)ReadEnum(p.FindPropertyRelative("Mode"));
            var modeName = mode.Value.ToString();
            var result = new JObject { ["mode"] = modeName };

            if (modeName == "Constant")
            {
                result["value"] = Num(p.FindPropertyRelative("Constant").floatValue);
                return result;
            }

            if (modeName == "Parametric")
            {
                var parametric = p.FindPropertyRelative("Parametric");
                var kind = ((JValue)ReadEnum(parametric.FindPropertyRelative("Kind"))).Value.ToString();
                if (kind == "CustomBezier")
                {
                    var p1 = parametric.FindPropertyRelative("BezierP1").vector2Value;
                    var p2 = parametric.FindPropertyRelative("BezierP2").vector2Value;
                    result["bezier"] = new JArray(Num(p1.x), Num(p1.y), Num(p2.x), Num(p2.y));
                }
                else
                {
                    result["ease"] = ReadEnum(parametric.FindPropertyRelative("Ease"));
                }

                result["from"] = Num(p.FindPropertyRelative("From").floatValue);
                result["to"] = Num(p.FindPropertyRelative("To").floatValue);
            }
            else
            {
                result["curve"] = ReadCurve(p.FindPropertyRelative("Curve").animationCurveValue);
                if (p.FindPropertyRelative("Normalized").boolValue)
                {
                    result["normalized"] = true;
                }
                else
                {
                    result["from"] = Num(p.FindPropertyRelative("From").floatValue);
                    result["to"] = Num(p.FindPropertyRelative("To").floatValue);
                }
            }

            var time = p.FindPropertyRelative("Time");
            var timeObj = new JObject
            {
                ["mode"] = ReadEnum(time.FindPropertyRelative("Mode")),
                ["value"] = Num(time.FindPropertyRelative("Value").floatValue),
            };
            var speed = time.FindPropertyRelative("SpeedScale").floatValue;
            if (speed > 0f && !Mathf.Approximately(speed, 1f))
            {
                timeObj["speed"] = Num(speed);
            }

            if (time.FindPropertyRelative("IgnoreTimeScale").boolValue)
            {
                timeObj["ignoreTimeScale"] = true;
            }

            result["time"] = timeObj;

            var loop = ((JValue)ReadEnum(p.FindPropertyRelative("Loop"))).Value.ToString();
            if (loop != "Once")
            {
                result["loop"] = loop;
            }

            var loopCount = p.FindPropertyRelative("LoopCount").intValue;
            if (loopCount != 0)
            {
                result["loopCount"] = loopCount;
            }

            return result;
        }

        // ───────────────────────── Write ─────────────────────────

        public static void Write(SerializedProperty p, JToken value) => Write(p, value, 0);

        private static void Write(SerializedProperty p, JToken value, int depth)
        {
            if (depth > MaxDepth)
            {
                throw Bad(p, "浅い入れ子", value);
            }

            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    WriteInteger(p, value);
                    return;
                case SerializedPropertyType.Boolean:
                    p.boolValue = ToBool(p, value);
                    return;
                case SerializedPropertyType.Float:
                    if (p.type == "double")
                    {
                        p.doubleValue = ToDouble(p, value);
                    }
                    else
                    {
                        p.floatValue = ToFloat(p, value);
                    }

                    return;
                case SerializedPropertyType.String:
                    if (value.Type != JTokenType.String && value.Type != JTokenType.Null)
                    {
                        throw Bad(p, "文字列", value);
                    }

                    p.stringValue = value.Type == JTokenType.Null ? string.Empty : (string)value;
                    return;
                case SerializedPropertyType.Character:
                    if (value.Type != JTokenType.String || ((string)value).Length != 1)
                    {
                        throw Bad(p, "1 文字の文字列", value);
                    }

                    p.intValue = ((string)value)[0];
                    return;
                case SerializedPropertyType.LayerMask:
                    p.intValue = (int)ToLong(p, value, int.MinValue, int.MaxValue);
                    return;
                case SerializedPropertyType.Enum:
                    WriteEnum(p, value);
                    return;
                case SerializedPropertyType.Color:
                    p.colorValue = ToColor(p, value);
                    return;
                case SerializedPropertyType.Vector2:
                {
                    var v = p.vector2Value;
                    var c = Components(p, value, "x", "y");
                    p.vector2Value = new Vector2(c[0] ?? v.x, c[1] ?? v.y);
                    return;
                }

                case SerializedPropertyType.Vector3:
                {
                    var v = p.vector3Value;
                    var c = Components(p, value, "x", "y", "z");
                    p.vector3Value = new Vector3(c[0] ?? v.x, c[1] ?? v.y, c[2] ?? v.z);
                    return;
                }

                case SerializedPropertyType.Vector4:
                {
                    var v = p.vector4Value;
                    var c = Components(p, value, "x", "y", "z", "w");
                    p.vector4Value = new Vector4(c[0] ?? v.x, c[1] ?? v.y, c[2] ?? v.z, c[3] ?? v.w);
                    return;
                }

                case SerializedPropertyType.Quaternion:
                {
                    var q = p.quaternionValue;
                    var c = Components(p, value, "x", "y", "z", "w");
                    p.quaternionValue = new Quaternion(c[0] ?? q.x, c[1] ?? q.y, c[2] ?? q.z, c[3] ?? q.w);
                    return;
                }

                case SerializedPropertyType.Vector2Int:
                {
                    var v = p.vector2IntValue;
                    var c = Components(p, value, "x", "y");
                    p.vector2IntValue = new Vector2Int(ToIntComponent(p, c[0], v.x), ToIntComponent(p, c[1], v.y));
                    return;
                }

                case SerializedPropertyType.Vector3Int:
                {
                    var v = p.vector3IntValue;
                    var c = Components(p, value, "x", "y", "z");
                    p.vector3IntValue = new Vector3Int(
                        ToIntComponent(p, c[0], v.x), ToIntComponent(p, c[1], v.y), ToIntComponent(p, c[2], v.z));
                    return;
                }

                case SerializedPropertyType.Rect:
                {
                    var r = p.rectValue;
                    var c = Components(p, value, "x", "y", "w", "h");
                    p.rectValue = new Rect(c[0] ?? r.x, c[1] ?? r.y, c[2] ?? r.width, c[3] ?? r.height);
                    return;
                }

                case SerializedPropertyType.Bounds:
                    WriteBounds(p, value);
                    return;
                case SerializedPropertyType.AnimationCurve:
                    p.animationCurveValue = ToCurve(p, value);
                    return;
                case SerializedPropertyType.ObjectReference:
                    WriteObjectReference(p, value);
                    return;
                case SerializedPropertyType.ManagedReference:
                    WriteManagedReference(p, value, depth);
                    return;
                case SerializedPropertyType.Generic:
                    if (p.isArray)
                    {
                        WriteArray(p, value, depth);
                        return;
                    }

                    if (IsAssetId(p))
                    {
                        WriteAssetId(p, value);
                        return;
                    }

                    if (IsValueDef(p))
                    {
                        WriteValueDef(p, value);
                        return;
                    }

                    WriteObject(p, value, depth);
                    return;
                default:
                    throw new McpToolError(
                        McpGuard.CodeInvalidParams,
                        $"'{p.propertyPath}' は型 {p.propertyType} のため MCP からは書けません(Inspector で編集してください)");
            }
        }

        private static void WriteObject(SerializedProperty p, JToken value, int depth)
        {
            if (!(value is JObject obj))
            {
                throw Bad(p, "オブジェクト({欄名:値}。渡したキーだけを上書き)", value);
            }

            foreach (var kv in obj)
            {
                var child = p.FindPropertyRelative(kv.Key);
                if (child == null)
                {
                    throw new McpToolError(
                        McpGuard.CodeInvalidParams,
                        $"'{p.propertyPath}' に欄 '{kv.Key}' がありません(欄: {ChildNames(p)})");
                }

                Write(child, kv.Value, depth + 1);
            }
        }

        private static void WriteManagedReference(SerializedProperty p, JToken value, int depth)
        {
            if (value.Type == JTokenType.Null)
            {
                p.managedReferenceValue = null;
                return;
            }

            if (!(value is JObject obj) || string.IsNullOrEmpty(p.managedReferenceFullTypename))
            {
                throw Bad(p, "既存インスタンスの欄のパッチ({欄名:値})か null(型の新規生成は不可)", value);
            }

            foreach (var kv in obj)
            {
                if (kv.Key == "$type")
                {
                    continue;
                }

                var child = p.FindPropertyRelative(kv.Key);
                if (child == null)
                {
                    throw new McpToolError(
                        McpGuard.CodeInvalidParams,
                        $"'{p.propertyPath}' に欄 '{kv.Key}' がありません(欄: {ChildNames(p)})");
                }

                Write(child, kv.Value, depth + 1);
            }
        }

        private static void WriteArray(SerializedProperty p, JToken value, int depth)
        {
            if (!(value is JArray array))
            {
                throw Bad(p, "配列(全体の置き換え。要素単位の更新は不可)", value);
            }

            p.arraySize = array.Count;
            for (var i = 0; i < array.Count; i++)
            {
                Write(p.GetArrayElementAtIndex(i), array[i], depth + 1);
            }
        }

        private static string ChildNames(SerializedProperty p)
        {
            var names = new List<string>();
            var it = p.Copy();
            var end = p.GetEndProperty();
            var enter = it.Next(true);
            while (enter && !SerializedProperty.EqualContents(it, end))
            {
                names.Add(it.name);
                enter = it.Next(false);
            }

            return string.Join(",", names);
        }

        // ── 数値・bool ──

        private static string IntegerTypeName(SerializedProperty p)
        {
            switch (p.type)
            {
                case "ulong":
                case "UInt64":
                    return "ulong";
                case "uint":
                case "UInt32":
                    return "uint";
                case "long":
                case "Int64":
                    return "long";
                case "short":
                case "Int16":
                    return "short";
                case "ushort":
                case "UInt16":
                    return "ushort";
                case "byte":
                case "UInt8":
                    return "byte";
                case "sbyte":
                case "SInt8":
                    return "sbyte";
                default:
                    return "int";
            }
        }

        private static void WriteInteger(SerializedProperty p, JToken value)
        {
            if (!TryGetBigInteger(value, out var big))
            {
                throw Bad(p, "整数", value);
            }

            var type = IntegerTypeName(p);
            BigInteger min, max;
            switch (type)
            {
                case "ulong":
                    min = 0;
                    max = ulong.MaxValue;
                    break;
                case "uint":
                    min = 0;
                    max = uint.MaxValue;
                    break;
                case "long":
                    min = long.MinValue;
                    max = long.MaxValue;
                    break;
                case "short":
                    min = short.MinValue;
                    max = short.MaxValue;
                    break;
                case "ushort":
                    min = 0;
                    max = ushort.MaxValue;
                    break;
                case "byte":
                    min = 0;
                    max = byte.MaxValue;
                    break;
                case "sbyte":
                    min = sbyte.MinValue;
                    max = sbyte.MaxValue;
                    break;
                default:
                    min = int.MinValue;
                    max = int.MaxValue;
                    break;
            }

            if (big < min || big > max)
            {
                throw Bad(p, $"整数({type}: {min} 〜 {max})", value);
            }

            if (type == "ulong")
            {
                p.ulongValue = (ulong)big;
            }
            else if (type == "int")
            {
                p.intValue = (int)big;
            }
            else
            {
                p.longValue = (long)big;
            }
        }

        private static bool TryGetBigInteger(JToken value, out BigInteger result)
        {
            result = BigInteger.Zero;
            switch (value.Type)
            {
                case JTokenType.Integer:
                    return BigInteger.TryParse(value.ToString(), NumberStyles.AllowLeadingSign, Inv, out result);
                case JTokenType.Float:
                {
                    var d = (double)value;
                    if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d)
                    {
                        return false;
                    }

                    result = new BigInteger(d);
                    return true;
                }

                case JTokenType.String:
                {
                    var s = ((string)value).Trim();
                    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        return BigInteger.TryParse("0" + s.Substring(2), NumberStyles.AllowHexSpecifier, Inv, out result);
                    }

                    return BigInteger.TryParse(s, NumberStyles.AllowLeadingSign, Inv, out result);
                }

                default:
                    return false;
            }
        }

        private static long ToLong(SerializedProperty p, JToken value, long min, long max)
        {
            if (!TryGetBigInteger(value, out var big) || big < min || big > max)
            {
                throw Bad(p, $"整数({min} 〜 {max})", value);
            }

            return (long)big;
        }

        private static int ToIntComponent(SerializedProperty p, float? component, int fallback)
        {
            if (!component.HasValue)
            {
                return fallback;
            }

            if (Mathf.Floor(component.Value) != component.Value)
            {
                throw Bad(p, "整数の成分", new JValue(component.Value));
            }

            return (int)component.Value;
        }

        private static double ToDouble(SerializedProperty p, JToken value)
        {
            double d;
            switch (value.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    d = (double)value;
                    break;
                case JTokenType.String:
                    if (!double.TryParse((string)value, NumberStyles.Float, Inv, out d))
                    {
                        throw Bad(p, "数値", value);
                    }

                    break;
                default:
                    throw Bad(p, "数値", value);
            }

            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                throw Bad(p, "有限の数値", value);
            }

            return d;
        }

        private static float ToFloat(SerializedProperty p, JToken value)
        {
            var d = ToDouble(p, value);
            var f = (float)d;
            if (float.IsInfinity(f))
            {
                throw Bad(p, "float の範囲の数値", value);
            }

            return f;
        }

        private static bool ToBool(SerializedProperty p, JToken value)
        {
            if (value.Type == JTokenType.Boolean)
            {
                return (bool)value;
            }

            if (value.Type == JTokenType.String)
            {
                var s = ((string)value).Trim();
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            throw Bad(p, "true / false", value);
        }

        // 成分(x,y,...)を JObject か JArray から読む。無い成分は null(= 既存値のまま)。
        private static float?[] Components(SerializedProperty p, JToken value, params string[] names)
        {
            var result = new float?[names.Length];
            if (value is JArray array)
            {
                if (array.Count != names.Length)
                {
                    throw Bad(p, $"{{{string.Join(",", names)}}} か長さ {names.Length} の配列", value);
                }

                for (var i = 0; i < names.Length; i++)
                {
                    result[i] = ToFloat(p, array[i]);
                }

                return result;
            }

            if (value is JObject obj)
            {
                foreach (var kv in obj)
                {
                    var index = Array.IndexOf(names, kv.Key);
                    if (index < 0)
                    {
                        throw Bad(p, $"キーは {string.Join(",", names)} のみ('{kv.Key}' は不可)", value);
                    }

                    result[index] = ToFloat(p, kv.Value);
                }

                return result;
            }

            throw Bad(p, $"{{{string.Join(",", names)}}} か長さ {names.Length} の配列", value);
        }

        private static Color ToColor(SerializedProperty p, JToken value)
        {
            var current = p.colorValue;
            if (value.Type == JTokenType.String)
            {
                if (ColorUtility.TryParseHtmlString((string)value, out var parsed))
                {
                    return parsed;
                }

                throw Bad(p, "#RRGGBB / #RRGGBBAA の文字列か {r,g,b,a}", value);
            }

            var c = Components(p, value, "r", "g", "b", "a");
            if (value is JArray && c[3] == null)
            {
                c[3] = 1f;
            }

            return new Color(c[0] ?? current.r, c[1] ?? current.g, c[2] ?? current.b, c[3] ?? current.a);
        }

        private static void WriteBounds(SerializedProperty p, JToken value)
        {
            if (!(value is JObject obj))
            {
                throw Bad(p, "{center:{x,y,z},size:{x,y,z}}", value);
            }

            var b = p.boundsValue;
            var center = b.center;
            var size = b.size;
            foreach (var kv in obj)
            {
                if (kv.Key == "center")
                {
                    var c = Components(p, kv.Value, "x", "y", "z");
                    center = new Vector3(c[0] ?? center.x, c[1] ?? center.y, c[2] ?? center.z);
                }
                else if (kv.Key == "size")
                {
                    var c = Components(p, kv.Value, "x", "y", "z");
                    size = new Vector3(c[0] ?? size.x, c[1] ?? size.y, c[2] ?? size.z);
                }
                else
                {
                    throw Bad(p, "キーは center,size のみ", value);
                }
            }

            p.boundsValue = new Bounds(center, size);
        }

        private static AnimationCurve ToCurve(SerializedProperty p, JToken value)
        {
            var keysToken = value is JObject obj ? obj["keys"] : value;
            if (!(keysToken is JArray keys))
            {
                throw Bad(p, "{keys:[{t,v,in,out}]}", value);
            }

            var frames = new Keyframe[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                if (!(keys[i] is JObject key))
                {
                    throw Bad(p, "keys の要素は {t,v,in,out}", value);
                }

                foreach (var kv in key)
                {
                    if (kv.Key != "t" && kv.Key != "v" && kv.Key != "in" && kv.Key != "out")
                    {
                        throw Bad(p, "keys の要素のキーは t,v,in,out のみ", value);
                    }
                }

                if (key["t"] == null || key["v"] == null)
                {
                    throw Bad(p, "keys の要素は t と v が必須", value);
                }

                frames[i] = new Keyframe(
                    ToFloat(p, key["t"]),
                    ToFloat(p, key["v"]),
                    key["in"] != null ? ToFloat(p, key["in"]) : 0f,
                    key["out"] != null ? ToFloat(p, key["out"]) : 0f);
            }

            return new AnimationCurve(frames);
        }

        // ── enum ──

        private static void WriteEnum(SerializedProperty p, JToken value)
        {
            if (value.Type == JTokenType.String)
            {
                var text = ((string)value).Trim();
                var names = p.enumNames;
                for (var i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], text, StringComparison.OrdinalIgnoreCase))
                    {
                        p.enumValueIndex = i;
                        return;
                    }
                }

                if (!TryGetBigInteger(value, out _))
                {
                    throw Bad(p, "enum の名前(" + string.Join("/", names) + ")か数値", value);
                }
            }

            if (!TryGetBigInteger(value, out var big) || big < int.MinValue || big > int.MaxValue)
            {
                throw Bad(p, "enum の名前(" + string.Join("/", p.enumNames) + ")か数値", value);
            }

            p.enumValueFlag = (int)big;
        }

        // ── Object 参照 ──

        private static void WriteObjectReference(SerializedProperty p, JToken value)
        {
            if (value.Type == JTokenType.Null)
            {
                p.objectReferenceValue = null;
                return;
            }

            if (value.Type != JTokenType.String)
            {
                throw Bad(p, "アセットパス文字列か null", value);
            }

            var text = ((string)value).Trim();
            if (text.Length == 0)
            {
                p.objectReferenceValue = null;
                return;
            }

            var path = text;
            string subName = null;
            var hash = text.IndexOf('#');
            if (hash >= 0)
            {
                path = text.Substring(0, hash);
                subName = text.Substring(hash + 1);
            }

            if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                throw Bad(p, "アセットパス(Assets/... か Packages/...)か null", value);
            }

            var expected = ExpectedTypeName(p);
            var obj = LoadTyped(path, subName, expected);
            if (obj == null)
            {
                throw new McpToolError(
                    McpGuard.CodeInvalidParams,
                    $"'{p.propertyPath}': '{text}' が見つからないか型が合いません(必要な型: {expected ?? "Object"})");
            }

            p.objectReferenceValue = obj;
        }

        // "PPtr<$AudioClip>" → "AudioClip"。型が読めなければ null(= 型の検査を省く)。
        private static string ExpectedTypeName(SerializedProperty p)
        {
            var type = p.type;
            if (string.IsNullOrEmpty(type))
            {
                return null;
            }

            var start = type.IndexOf("$", StringComparison.Ordinal);
            var end = type.LastIndexOf('>');
            if (start < 0 || end <= start)
            {
                return null;
            }

            var name = type.Substring(start + 1, end - start - 1);
            return string.IsNullOrEmpty(name) || name == "Object" ? null : name;
        }

        private static UnityEngine.Object LoadTyped(string path, string subName, string expected)
        {
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main == null)
            {
                return null;
            }

            if (subName == null && IsOfTypeName(main, expected))
            {
                return main;
            }

            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (candidate == null || (subName != null && candidate.name != subName))
                {
                    continue;
                }

                if (IsOfTypeName(candidate, expected))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsOfTypeName(UnityEngine.Object obj, string expected)
        {
            if (expected == null)
            {
                return true;
            }

            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                if (t.Name == expected)
                {
                    return true;
                }
            }

            return false;
        }

        // ── AssetId ──

        private static void WriteAssetId(SerializedProperty p, JToken value)
        {
            var valueProp = p.FindPropertyRelative("value");
            var typeProp = p.FindPropertyRelative("type");
            if (valueProp == null || typeProp == null)
            {
                throw Bad(p, "{type,id}", value);
            }

            if (value.Type == JTokenType.Null)
            {
                valueProp.ulongValue = 0;
                typeProp.enumValueFlag = 0;
                return;
            }

            JToken idToken;
            JToken typeToken = null;
            if (value is JObject obj)
            {
                foreach (var kv in obj)
                {
                    if (kv.Key != "type" && kv.Key != "id")
                    {
                        throw Bad(p, "キーは type,id のみ", value);
                    }
                }

                idToken = obj["id"];
                typeToken = obj["type"];
                if (idToken == null)
                {
                    throw Bad(p, "{type,id}(id は 10 進文字列)", value);
                }
            }
            else
            {
                // id だけ(文字列・数値)を渡したときは、既存の type を保つ。
                idToken = value;
            }

            if (!McpJson.TryParseId(idToken, out var id))
            {
                throw Bad(p, "id は 10 進または 0x 16 進の文字列", value);
            }

            if (typeToken != null)
            {
                if (typeToken.Type != JTokenType.String)
                {
                    throw Bad(p, "type は AssetType 名", value);
                }

                WriteEnum(typeProp, typeToken);
            }
            else if (typeProp.enumValueFlag == 0)
            {
                throw Bad(p, "type(AssetType 名)も必要です({type,id})", value);
            }

            valueProp.ulongValue = id;
        }

        // ── ValueDef ──

        private static readonly string[] ValueDefKeys =
        {
            "mode", "value", "ease", "bezier", "from", "to", "normalized", "curve", "time", "loop", "loopCount",
        };

        private static readonly string[] TimeKeys = { "mode", "value", "speed", "ignoreTimeScale" };

        private static void WriteValueDef(SerializedProperty p, JToken value)
        {
            if (!(value is JObject obj))
            {
                throw Bad(p, "{mode:\"Constant\",value} / {mode:\"Parametric\",ease,from,to,time} / {mode:\"Curve\",curve,from,to,time}", value);
            }

            foreach (var kv in obj)
            {
                if (Array.IndexOf(ValueDefKeys, kv.Key) < 0)
                {
                    throw Bad(p, "キーは " + string.Join(",", ValueDefKeys) + " のみ('" + kv.Key + "' は不可)", value);
                }
            }

            var modeProp = p.FindPropertyRelative("Mode");
            if (obj["mode"] != null)
            {
                WriteEnum(modeProp, obj["mode"]);
            }
            else if (obj["curve"] != null)
            {
                SetEnumByName(modeProp, "Curve");
            }
            else if (obj["ease"] != null || obj["bezier"] != null)
            {
                SetEnumByName(modeProp, "Parametric");
            }
            else if (obj["value"] != null)
            {
                SetEnumByName(modeProp, "Constant");
            }

            if (obj["value"] != null)
            {
                p.FindPropertyRelative("Constant").floatValue = ToFloat(p, obj["value"]);
            }

            var parametric = p.FindPropertyRelative("Parametric");
            if (obj["ease"] != null)
            {
                SetEnumByName(parametric.FindPropertyRelative("Kind"), "NamedEase");
                WriteEnum(parametric.FindPropertyRelative("Ease"), obj["ease"]);
            }

            if (obj["bezier"] != null)
            {
                if (!(obj["bezier"] is JArray bez) || bez.Count != 4)
                {
                    throw Bad(p, "bezier は [x1,y1,x2,y2]", value);
                }

                SetEnumByName(parametric.FindPropertyRelative("Kind"), "CustomBezier");
                parametric.FindPropertyRelative("BezierP1").vector2Value = new Vector2(ToFloat(p, bez[0]), ToFloat(p, bez[1]));
                parametric.FindPropertyRelative("BezierP2").vector2Value = new Vector2(ToFloat(p, bez[2]), ToFloat(p, bez[3]));
            }

            if (obj["from"] != null)
            {
                p.FindPropertyRelative("From").floatValue = ToFloat(p, obj["from"]);
            }

            if (obj["to"] != null)
            {
                p.FindPropertyRelative("To").floatValue = ToFloat(p, obj["to"]);
            }

            if (obj["normalized"] != null)
            {
                p.FindPropertyRelative("Normalized").boolValue = ToBool(p, obj["normalized"]);
            }

            if (obj["curve"] != null)
            {
                p.FindPropertyRelative("Curve").animationCurveValue = ToCurve(p, obj["curve"]);
            }

            if (obj["time"] != null)
            {
                WriteTimeDef(p, p.FindPropertyRelative("Time"), obj["time"]);
            }

            if (obj["loop"] != null)
            {
                WriteEnum(p.FindPropertyRelative("Loop"), obj["loop"]);
            }

            if (obj["loopCount"] != null)
            {
                p.FindPropertyRelative("LoopCount").intValue = (int)ToLong(p, obj["loopCount"], 0, int.MaxValue);
            }
        }

        private static void WriteTimeDef(SerializedProperty owner, SerializedProperty time, JToken value)
        {
            if (!(value is JObject obj))
            {
                throw Bad(owner, "time は {mode,value,speed,ignoreTimeScale}", value);
            }

            foreach (var kv in obj)
            {
                if (Array.IndexOf(TimeKeys, kv.Key) < 0)
                {
                    throw Bad(owner, "time のキーは " + string.Join(",", TimeKeys) + " のみ", value);
                }
            }

            if (obj["mode"] != null)
            {
                WriteEnum(time.FindPropertyRelative("Mode"), obj["mode"]);
            }

            if (obj["value"] != null)
            {
                time.FindPropertyRelative("Value").floatValue = ToFloat(owner, obj["value"]);
            }

            if (obj["speed"] != null)
            {
                time.FindPropertyRelative("SpeedScale").floatValue = ToFloat(owner, obj["speed"]);
            }

            if (obj["ignoreTimeScale"] != null)
            {
                time.FindPropertyRelative("IgnoreTimeScale").boolValue = ToBool(owner, obj["ignoreTimeScale"]);
            }
        }

        private static void SetEnumByName(SerializedProperty p, string name)
        {
            var names = p.enumNames;
            for (var i = 0; i < names.Length; i++)
            {
                if (names[i] == name)
                {
                    p.enumValueIndex = i;
                    return;
                }
            }
        }

        // ───────────────────────── 補助 ─────────────────────────

        // 直下の欄名を、シリアライズ順に(m_Script を除いて、HideInInspector の欄も含めて)列挙する。
        public static List<string> TopLevelNames(SerializedObject so)
        {
            var names = new List<string>();
            var it = so.GetIterator();
            var enter = it.Next(true);
            while (enter)
            {
                if (it.depth == 0 && it.name != "m_Script")
                {
                    names.Add(it.name);
                }

                enter = it.Next(false);
            }

            return names;
        }

        // from / to の表示用。長い値は 200 文字で切った文字列にする(返り値を膨らませない)。
        public static JToken Describe(JToken value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            var text = value.ToString(Newtonsoft.Json.Formatting.None);
            if (text.Length <= DescribeMaxChars)
            {
                return value;
            }

            return new JValue(text.Substring(0, DescribeMaxChars) + "…");
        }

        private static McpToolError Bad(SerializedProperty p, string expected, JToken value)
        {
            var got = value == null ? "null" : value.ToString(Newtonsoft.Json.Formatting.None);
            if (got.Length > 60)
            {
                got = got.Substring(0, 60) + "…";
            }

            return new McpToolError(
                McpGuard.CodeInvalidParams,
                $"'{p.propertyPath}' は {expected} が必要です(受け取った値: {got})");
        }
    }
}
