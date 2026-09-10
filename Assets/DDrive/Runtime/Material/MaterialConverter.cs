using System.Collections.Generic;
using DDrive.Foundation.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-2「相互変換機能」— MaterialData(ShaderA) → ShaderB の変換(チケット 3-6、2026-09-10)。
    //   共通データ(MaterialCommon)はそのまま維持 / 固有データ(Specific)は ShaderConversionTable で名前を付け替え、
    //   表に無いものは「変換先に同名・同型があれば維持、無ければ破棄」して差分レポートを返す。
    // 純関数(Data を書き換えない)。エディタは Result を表示してから新規 Data の作成 / 既存 Data への適用(Undo)を行う。
    public static class MaterialConverter
    {
        public enum Outcome
        {
            Mapped,     // 表の対応で名前を付け替えた
            Kept,       // 同名・同型が変換先にあったのでそのまま
            Dropped,    // 変換先に無い(警告)
            Discarded,  // 表で「意図的に破棄」と宣言されている
        }

        public struct Entry
        {
            public string FromProperty;
            public string ToProperty;
            public Outcome Outcome;
            public ParamValue Value; // 変換後の値(Dropped / Discarded は元の値)
        }

        public sealed class Result
        {
            public Shader TargetShader;
            public readonly List<Entry> Entries = new();
            public readonly List<ShaderParam> Specific = new();
            public int MappedCount;
            public int KeptCount;
            public int DroppedCount;
            public int DiscardedCount;
            public bool UsedTable;
        }

        // tables: プロジェクト内の Table 群(null / 空なら表無しで変換)。
        public static Result Convert(MaterialData source, Shader target, IReadOnlyList<ShaderConversionTable> tables)
        {
            var result = new Result { TargetShader = target };
            if (source == null || target == null)
            {
                return result;
            }

            var hasRule = TryFindRule(source.Shader, target, tables, out var rule);
            result.UsedTable = hasRule;
            var specific = source.Specific;
            if (specific == null)
            {
                return result;
            }

            for (var i = 0; i < specific.Length; i++)
            {
                var param = specific[i];
                if (string.IsNullOrEmpty(param.Property))
                {
                    continue;
                }

                var entry = new Entry { FromProperty = param.Property, Value = param.Value };
                if (hasRule && TryFindMapping(rule, param.Property, out var mapping))
                {
                    if (mapping.IsDrop)
                    {
                        entry.Outcome = Outcome.Discarded;
                        result.DiscardedCount++;
                        result.Entries.Add(entry);
                        continue;
                    }

                    entry.ToProperty = mapping.ToProperty;
                    entry.Value = ApplyScaleOffset(param.Value, mapping);
                    if (IsCompatible(target, mapping.ToProperty, entry.Value.Type))
                    {
                        entry.Outcome = Outcome.Mapped;
                        result.MappedCount++;
                        result.Specific.Add(new ShaderParam { Property = mapping.ToProperty, Value = entry.Value });
                    }
                    else
                    {
                        entry.Outcome = Outcome.Dropped; // 表の指定先が変換先に無い(表の記述ミス)
                        result.DroppedCount++;
                    }

                    result.Entries.Add(entry);
                    continue;
                }

                if (IsCompatible(target, param.Property, param.Value.Type))
                {
                    entry.ToProperty = param.Property;
                    entry.Outcome = Outcome.Kept;
                    result.KeptCount++;
                    result.Specific.Add(param);
                }
                else
                {
                    entry.Outcome = Outcome.Dropped;
                    result.DroppedCount++;
                }

                result.Entries.Add(entry);
            }

            return result;
        }

        // 変換結果を dest へ書き込む(呼び出し側が Undo.RecordObject / SetDirty を行う。ランタイムでは使わない)。
        public static void ApplyTo(MaterialData dest, MaterialData source, Result result)
        {
            if (dest == null || result == null)
            {
                return;
            }

            if (source != null && dest != source)
            {
                dest.Common = source.Common;
                dest.RenderQueueOffset = source.RenderQueueOffset;
                dest.RenderingLayerMask = source.RenderingLayerMask;
                dest.Anims = source.Anims != null ? (MaterialAnim[])source.Anims.Clone() : null;
                dest.Flags = source.Flags;
            }

            dest.Shader = result.TargetShader;
            dest.Specific = result.Specific.ToArray();
        }

        public static bool TryFindRule(Shader from, Shader to, IReadOnlyList<ShaderConversionTable> tables, out ShaderConversionTable.Rule rule)
        {
            if (tables != null)
            {
                for (var i = 0; i < tables.Count; i++)
                {
                    if (tables[i] != null && tables[i].TryFind(from, to, out rule))
                    {
                        return true;
                    }
                }
            }

            rule = default;
            return false;
        }

        private static bool TryFindMapping(in ShaderConversionTable.Rule rule, string property, out ShaderConversionTable.Mapping mapping)
        {
            if (rule.Mappings != null)
            {
                for (var i = 0; i < rule.Mappings.Length; i++)
                {
                    if (rule.Mappings[i].FromProperty == property)
                    {
                        mapping = rule.Mappings[i];
                        return true;
                    }
                }
            }

            mapping = default;
            return false;
        }

        private static ParamValue ApplyScaleOffset(ParamValue value, in ShaderConversionTable.Mapping mapping)
        {
            var scale = mapping.Scale == 0f && mapping.Offset == 0f ? 1f : mapping.Scale;
            switch (value.Type)
            {
                case ParamValueType.Float:
                    value.FloatValue = value.FloatValue * scale + mapping.Offset;
                    break;
                case ParamValueType.Int:
                    value.IntValue = Mathf.RoundToInt(value.IntValue * scale + mapping.Offset);
                    break;
            }

            return value;
        }

        // 変換先シェーダーにそのプロパティがあり、型が値と両立するか。
        public static bool IsCompatible(Shader shader, string property, ParamValueType valueType)
        {
            if (shader == null || string.IsNullOrEmpty(property))
            {
                return false;
            }

            var index = shader.FindPropertyIndex(property);
            if (index < 0)
            {
                return false;
            }

            var type = shader.GetPropertyType(index);
            switch (valueType)
            {
                case ParamValueType.Float:
                case ParamValueType.Bool:
                    return type == ShaderPropertyType.Float || type == ShaderPropertyType.Range || type == ShaderPropertyType.Int;
                case ParamValueType.Int:
                    return type == ShaderPropertyType.Int || type == ShaderPropertyType.Float || type == ShaderPropertyType.Range;
                case ParamValueType.Color:
                    return type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector;
                case ParamValueType.Vector:
                    return type == ShaderPropertyType.Vector || type == ShaderPropertyType.Color;
                case ParamValueType.Object:
                    return type == ShaderPropertyType.Texture;
                default:
                    return false;
            }
        }
    }
}
